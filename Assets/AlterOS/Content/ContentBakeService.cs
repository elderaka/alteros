using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlterOS.Authoring;

namespace AlterOS.Content
{
    public sealed class ContentSemanticBuildInput
    {
        public byte[] metadataUtf8;
        public byte[] vectorBytes;
        public byte[] tokenizerBytes;
        public string expectedContentHash = string.Empty;
        public ContentHostVocabulary hostVocabulary;
    }

    public sealed class ContentBakeArtifact
    {
        public readonly string outputPath;
        public readonly string storeId;
        public readonly string releaseId;
        public readonly string buildManifestHash;
        public readonly int sources;
        public readonly int topics;
        public readonly int flows;
        public readonly int dialogues;
        public readonly int texts;
        public readonly int anchors;

        public ContentBakeArtifact(string path, string store, string release, ContentPackage package)
        {
            outputPath = path ?? string.Empty;
            storeId = store ?? string.Empty;
            releaseId = release ?? string.Empty;
            buildManifestHash = package == null || package.buildManifest == null ? string.Empty : package.buildManifest.Hash;
            sources = package == null || package.legacySources == null ? 0 : package.legacySources.Count;
            topics = package == null || package.topics == null ? 0 : package.topics.Count;
            flows = package == null || package.flows == null ? 0 : package.flows.Count;
            dialogues = package == null || package.dialogues == null ? 0 : package.dialogues.Count;
            texts = package == null || package.texts == null ? 0 : package.texts.Count;
            anchors = package == null || package.anchors == null ? 0 : package.anchors.Count;
        }
    }

    /// <summary>Creates an immutable compiled SQLite artifact from an explicit source inventory or SQL snapshot.</summary>
    public static class ContentBakeService
    {
        public static ContentResult<ContentBakeArtifact> Bake(ContentSourceManifest manifest, string outputPath,
            string streamingAssetsRoot)
        { return Bake(manifest, outputPath, streamingAssetsRoot, null); }

        public static ContentResult<ContentBakeArtifact> Bake(ContentSourceManifest manifest, string outputPath,
            string streamingAssetsRoot, ContentSemanticBuildInput semanticInput)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(streamingAssetsRoot))
                return Failure("bake_configuration_invalid", "A trusted source manifest, staging output path and StreamingAssets root are required.");
            Guid parsedStoreId;
            if (!Guid.TryParse(manifest.storeId, out parsedStoreId))
                return Failure("bake_store_identity_invalid", "The trusted manifest must identify the target content store with a GUID.");
            manifest.storeId = parsedStoreId.ToString("D");

            string fullOutput;
            string fullStreaming;
            string fullSourceRoot;
            try
            {
                fullOutput = Path.GetFullPath(outputPath);
                fullStreaming = Path.GetFullPath(streamingAssetsRoot);
                fullSourceRoot = Path.GetFullPath(manifest.sourceRoot);
            }
            catch (Exception exception) { return Failure("bake_path_invalid", exception.Message); }

            if (IsWithin(fullOutput, fullStreaming))
                return Failure("bake_output_in_streaming_assets", "The compiler staging output must stay outside StreamingAssets.");
            if (IsWithin(fullOutput, fullSourceRoot) || string.Equals(fullOutput, fullSourceRoot, StringComparison.OrdinalIgnoreCase))
                return Failure("bake_output_in_source_root", "The compiler staging output must not overwrite or sit inside the authored source root.");
            if (File.Exists(fullOutput) || Directory.Exists(fullOutput))
                return Failure("bake_output_exists", "The staging output already exists; choose a new artifact path to preserve it.");

            ContentResult<ContentPackage> imported = LegacyContentAdapter.Import(manifest);
            if (!imported.IsReady)
                return Failure(imported.diagnostics.Count == 0 ? "bake_import_failed" : imported.diagnostics[0].code,
                    imported.diagnostics.Count == 0 ? "The named sources could not be imported." : imported.diagnostics[0].message);
            return BakeImported(imported.value, fullOutput, manifest.storeId, semanticInput, string.Empty, string.Empty);
        }

        /// <summary>Bakes only the exact selected, hash-verified authoring revisions in a captured SQLite snapshot.</summary>
        public static ContentResult<ContentBakeArtifact> BakeFromSnapshot(AuthoringSnapshot snapshot, string outputPath,
            ContentSemanticBuildInput semanticInput)
        {
            string snapshotError;
            if (!AuthoringRepository.ValidateSnapshot(snapshot, out snapshotError))
                return Failure("authoring_snapshot_invalid", snapshotError);
            if (string.IsNullOrWhiteSpace(outputPath))
                return Failure("bake_configuration_invalid", "A staging output path is required.");

            string fullOutput;
            try { fullOutput = Path.GetFullPath(outputPath); }
            catch (Exception exception) { return Failure("bake_path_invalid", exception.Message); }
            if (Path.GetFullPath(fullOutput).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "StreamingAssets", StringComparison.OrdinalIgnoreCase)))
                return Failure("bake_output_in_streaming_assets", "The compiler staging output must stay outside StreamingAssets.");
            if (File.Exists(fullOutput) || Directory.Exists(fullOutput))
                return Failure("bake_output_exists", "The staging output already exists; choose a new artifact path to preserve it.");

            var sources = new List<ContentSourceDocument>(snapshot.documents.Count);
            foreach (AuthoringDocument document in snapshot.documents)
            {
                AuthoringResult<ContentSourceDocument> projection = AuthoringContentSourceProjection.Project(document);
                if (!projection.IsReady)
                    return Failure(projection.diagnostics.Count == 0 ? "authoring_source_projection_failed" : projection.diagnostics[0].code,
                        projection.diagnostics.Count == 0 ? "A selected authoring document could not be projected." : projection.diagnostics[0].message);
                sources.Add(projection.value);
            }

            string packageId = "authoring_" + snapshot.storeId.Replace("-", string.Empty);
            ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder(packageId, snapshot.storeId, sources,
                semanticInput == null || semanticInput.hostVocabulary == null ? null : semanticInput.hostVocabulary.SequenceBuilder);
            if (!imported.IsReady)
                return Failure(imported.diagnostics.Count == 0 ? "bake_import_failed" : imported.diagnostics[0].code,
                    imported.diagnostics.Count == 0 ? "The selected authoring documents could not be imported." : imported.diagnostics[0].message);
            return BakeImported(imported.value, fullOutput, snapshot.storeId, semanticInput, snapshot.storeId, snapshot.snapshotHash);
        }

        private static ContentResult<ContentBakeArtifact> BakeImported(ContentPackage package, string fullOutput,
            string storeId, ContentSemanticBuildInput semanticInput, string authoringStoreId, string authoringSnapshotHash)
        {
            if (semanticInput != null)
            {
                if (semanticInput.metadataUtf8 == null || semanticInput.vectorBytes == null || semanticInput.tokenizerBytes == null ||
                    semanticInput.hostVocabulary == null || string.IsNullOrWhiteSpace(semanticInput.expectedContentHash))
                    return Failure("bake_semantic_input_invalid", "Baked GTE bytes, their exact source hash, and host vocabulary are required together.");
                ContentResult<GteSemanticImportResult> semantic = GteSemanticAssetImporter.ImportInto(package,
                    semanticInput.metadataUtf8, semanticInput.vectorBytes, semanticInput.tokenizerBytes,
                    semanticInput.expectedContentHash, semanticInput.hostVocabulary);
                if (!semantic.IsReady)
                    return Failure(semantic.diagnostics.Count == 0 ? "bake_semantic_import_failed" : semantic.diagnostics[0].code,
                        semantic.diagnostics.Count == 0 ? "The baked GTE index could not be imported." : semantic.diagnostics[0].message);
            }
            CompiledReleaseResult compiled = semanticInput == null
                ? ContentReleaseCompiler.Compile(package)
                : ContentReleaseCompiler.Compile(package, semanticInput.hostVocabulary);
            if (!compiled.IsReady)
                return Failure(compiled.diagnostics.Count == 0 ? "bake_compile_failed" : compiled.diagnostics[0].code,
                    compiled.diagnostics.Count == 0 ? "The content release could not be compiled." : compiled.diagnostics[0].message);
            try { compiled.package.buildManifest = ContentBuildManifest.Create(compiled.package, compiled.release,
                authoringStoreId, authoringSnapshotHash); }
            catch (Exception exception) { return Failure("bake_manifest_invalid", exception.Message); }

            string temporary = fullOutput + ".building-" + Guid.NewGuid().ToString("N");
            bool moved = false;
            try
            {
                string parent = Path.GetDirectoryName(fullOutput);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                ContentResult<ContentRepository> opened = ContentRepository.Open(temporary, ContentStoreMode.ReadWrite, storeId);
                if (!opened.IsReady)
                    return Failure(opened.diagnostics.Count == 0 ? "bake_store_open_failed" : opened.diagnostics[0].code,
                        opened.diagnostics.Count == 0 ? "The staging store could not be created." : opened.diagnostics[0].message);
                ContentResult<string> staged = opened.value.Stage(compiled.package);
                if (!staged.IsReady)
                    return Failure(staged.diagnostics.Count == 0 ? "bake_stage_failed" : staged.diagnostics[0].code,
                        staged.diagnostics.Count == 0 ? "The compiled content could not be staged." : staged.diagnostics[0].message);
                if (File.Exists(fullOutput) || Directory.Exists(fullOutput))
                    return Failure("bake_output_raced", "The requested output path appeared while the artifact was being built.");
                File.Move(temporary, fullOutput);
                moved = true;
                return ContentResult<ContentBakeArtifact>.Ready(new ContentBakeArtifact(fullOutput,
                    opened.value.StoreId, compiled.release.releaseId, compiled.package));
            }
            catch (Exception exception)
            {
                return Failure("bake_write_failed", exception.Message);
            }
            finally
            {
                if (!moved && File.Exists(temporary))
                {
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static bool IsWithin(string candidate, string root)
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static ContentResult<ContentBakeArtifact> Failure(string code, string message)
        { return ContentResult<ContentBakeArtifact>.Failure(ContentStatuses.Invalid, code, "", message); }
    }
}
