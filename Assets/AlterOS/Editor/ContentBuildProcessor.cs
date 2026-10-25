using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Activities;
using AlterOS.Authoring;
using AlterOS.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlterOS.Editor
{
    /// <summary>Builds and imports the complete, validated SQL content release before Unity builds a player.</summary>
    public sealed class ContentBuildProcessor : IPreprocessBuildWithReport
    {
        private const string AuthoringDatabasePath = "Tools/ContentWorkbench/Workspace/authoring.sqlite3";
        private const string TokenizerAssetPath = "Assets/Resources/AlterOS/Semantics/Models/tokenizer.json";
        private const string RuntimeAssetPath = "Assets/StreamingAssets/Content/content.sqlite3";

        public int callbackOrder { get { return -1000; } }

        public void OnPreprocessBuild(BuildReport report)
        { BuildAndImport(); }

        [MenuItem("Tools/AlterOS/Build Runtime SQL Content")]
        public static void BuildForEditor()
        {
            try { BuildAndImport(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("AlterOS SQL content build failed", exception.Message, "OK");
            }
        }

        private static void BuildAndImport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding the SQL content artifact.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string authoringPath = Path.Combine(projectRoot, AuthoringDatabasePath.Replace('/', Path.DirectorySeparatorChar));
            EnsureSafePath(authoringPath);
            AuthoringResult<AuthoringRepository> opened = AuthoringRepository.OpenExisting(authoringPath);
            if (!opened.IsReady) throw new InvalidDataException(Describe(opened.diagnostics, opened.status));
            AuthoringSnapshot snapshot;
            using (opened.value)
            {
                AuthoringResult<AuthoringBuildSet> selection = opened.value.ReadBuildSet("default");
                if (!selection.IsReady) throw new InvalidDataException(Describe(selection.diagnostics, selection.status));
                AuthoringResult<AuthoringSnapshot> pinned = opened.value.ReadSnapshot(selection.value.documentIds);
                if (!pinned.IsReady) throw new InvalidDataException(Describe(pinned.diagnostics, pinned.status));
                snapshot = pinned.value;
            }

            var selectedSources = new List<ContentSourceDocument>();
            foreach (AuthoringDocument document in snapshot.documents)
            {
                AuthoringResult<ContentSourceDocument> projected = AuthoringContentSourceProjection.Project(document);
                if (!projected.IsReady) throw new InvalidDataException(Describe(projected.diagnostics, projected.status));
                selectedSources.Add(projected.value);
            }
            List<ContentSourceDocument> genericSources = selectedSources.Where(item => item.kind == ContentSourceKinds.GenericDialogue).ToList();
            string[] routes = ReadGenericRouteIds(genericSources);
            ContentHostVocabulary vocabulary = CreateHostVocabulary(routes);
            ContentResult<ContentPackage> projectedPackage = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder(
                "authoring_" + snapshot.storeId.Replace("-", string.Empty), snapshot.storeId, selectedSources,
                vocabulary.SequenceBuilder);
            if (!projectedPackage.IsReady) throw new InvalidDataException(Describe(projectedPackage.diagnostics, projectedPackage.status));

            string semanticTempRoot = Path.Combine(Path.GetTempPath(), "alteros-semantic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(semanticTempRoot);
            string semanticMetadataPath = Path.Combine(semanticTempRoot, "metadata.json");
            string semanticVectorPath = Path.Combine(semanticTempRoot, "vectors.bytes");
            string semanticRequestPath = Path.Combine(semanticTempRoot, "snapshot-anchors.json");
            byte[] metadata;
            byte[] vectors;
            try
            {
                BuildAndBakeSnapshotAnchors(projectRoot, snapshot, genericSources, projectedPackage.value,
                    semanticRequestPath, semanticMetadataPath, semanticVectorPath, semanticTempRoot);
                metadata = File.ReadAllBytes(semanticMetadataPath);
                vectors = File.ReadAllBytes(semanticVectorPath);
            }
            finally { DeleteOwnedTemporaryDirectory(semanticTempRoot); }
            byte[] tokenizer = File.ReadAllBytes(Path.Combine(projectRoot, TokenizerAssetPath.Replace('/', Path.DirectorySeparatorChar)));
            var semanticInput = new ContentSemanticBuildInput
            {
                metadataUtf8 = metadata,
                vectorBytes = vectors,
                tokenizerBytes = tokenizer,
                expectedContentHash = snapshot.snapshotHash,
                hostVocabulary = vocabulary
            };

            string stagedPath = Path.Combine(Path.GetTempPath(), "alteros-content-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            string installPath = Path.Combine(projectRoot, RuntimeAssetPath.Replace('/', Path.DirectorySeparatorChar));
            string installDirectory = Path.GetDirectoryName(installPath);
            string installTemporary = installPath + ".building-" + Guid.NewGuid().ToString("N");
            string backupPath = installPath + ".backup-" + Guid.NewGuid().ToString("N");
            EnsureSafePath(stagedPath);
            EnsureSafePath(installPath);
            EnsureSafePath(installTemporary);
            EnsureSafePath(backupPath);
            bool hadExisting = File.Exists(installPath);
            bool movedOld = false;
            bool installed = false;
            bool assetEditing = false;
            try
            {
                ContentResult<ContentBakeArtifact> baked = ContentBakeService.BakeFromSnapshot(snapshot, stagedPath, semanticInput);
                if (!baked.IsReady) throw new InvalidDataException(Describe(baked.diagnostics, baked.status));
                ContentBakeArtifact artifact = baked.value;
                ContentResult<RuntimeContentStore> verified = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
                {
                    databasePath = stagedPath,
                    expectedStoreId = artifact.storeId,
                    expectedReleaseId = artifact.releaseId,
                    expectedBuildManifestHash = artifact.buildManifestHash
                });
                if (!verified.IsReady) throw new InvalidDataException(Describe(verified.diagnostics, verified.status));

                Directory.CreateDirectory(installDirectory);
                EnsureSafePath(stagedPath);
                EnsureSafePath(installPath);
                EnsureSafePath(installTemporary);
                EnsureSafePath(backupPath);
                AssetDatabase.StartAssetEditing();
                assetEditing = true;
                File.Copy(stagedPath, installTemporary, false);
                if (File.Exists(installPath))
                {
                    File.Replace(installTemporary, installPath, backupPath);
                    movedOld = true;
                }
                else
                    File.Move(installTemporary, installPath);
                AssetDatabase.StopAssetEditing();
                assetEditing = false;
                AssetDatabase.ImportAsset(RuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(RuntimeAssetPath)))
                    throw new InvalidDataException("Unity did not import the generated SQL content asset.");
                RuntimeContentStore.ResetShippedCache();
                installed = true;
                if (movedOld && File.Exists(backupPath)) File.Delete(backupPath);
                Debug.Log("AlterOS SQL content imported: " + artifact.sources + " sources, " + artifact.topics +
                    " Topics, " + artifact.flows + " Flows, " + artifact.anchors + " anchors; release " + artifact.releaseId + ".");
            }
            finally
            {
                if (assetEditing) AssetDatabase.StopAssetEditing();
                if (!installed && movedOld && File.Exists(backupPath))
                {
                    EnsureSafePath(installPath);
                    EnsureSafePath(backupPath);
                    if (File.Exists(installPath)) File.Delete(installPath);
                    File.Move(backupPath, installPath);
                    AssetDatabase.ImportAsset(RuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                else if (!installed && !hadExisting && File.Exists(installPath))
                {
                    EnsureSafePath(installPath);
                    File.Delete(installPath);
                    AssetDatabase.DeleteAsset(RuntimeAssetPath);
                }
                DeleteOwnedTemporary(stagedPath);
                DeleteOwnedTemporary(installTemporary);
                if (installed) DeleteOwnedTemporary(backupPath);
            }
        }

        private static ContentHostVocabulary CreateHostVocabulary(IEnumerable<string> routeIds)
        {
            var expressions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:AlteraExpressionPreset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object preset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (preset == null) continue;
                var serializedPreset = new SerializedObject(preset);
                SerializedProperty expressionName = serializedPreset.FindProperty("expressionName");
                if (expressionName != null && expressionName.propertyType == SerializedPropertyType.String &&
                    !string.IsNullOrWhiteSpace(expressionName.stringValue))
                    expressions.Add(expressionName.stringValue);
            }
            if (expressions.Count == 0) throw new InvalidDataException("No host-registered Altera expression presets were found.");

            var sequenceCatalog = ActivitySequenceCatalogFactory.CreateDefault();
            string[] sequenceCapabilities = sequenceCatalog.Primitives.All.Select(item => item.requiredCapabilityId)
                .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
            return new ContentHostVocabulary(
                new[] { ActivityCapabilityIds.ComeHere, ActivityCapabilityIds.FollowPlayer,
                    ActivityCapabilityIds.DrinkWater }.Concat(sequenceCapabilities)
                    .Concat(ContentHostEffectFunctionRegistry.RegisteredCapabilityIds),
                expressions, new string[0], new string[0], routeIds, sequenceCatalog.CreateBuilder());
        }

        private static void BuildAndBakeSnapshotAnchors(string projectRoot, AuthoringSnapshot snapshot,
            IEnumerable<ContentSourceDocument> genericSources, ContentPackage package, string requestPath,
            string metadataPath, string vectorPath, string cachePath)
        {
            var anchors = new List<SnapshotSemanticAnchor>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentSourceDocument source in genericSources)
            {
                JsonObject document = ContentJson.Parse(new UTF8Encoding(false, true).GetString(source.utf8Bytes)) as JsonObject;
                JsonObject routing = document == null ? null : document["routing"] as JsonObject;
                JsonArray examples = routing == null ? null : routing["examples"] as JsonArray;
                string routeId = routing == null ? string.Empty : (string)routing["routeId"];
                if (examples == null || string.IsNullOrWhiteSpace(routeId))
                    throw new InvalidDataException("A selected Generic source has no validated route examples: " + source.logicalPath);
                string ownerRevision = HashUtf8("gte-route-owner\0" + routeId);
                foreach (JsonValue value in examples)
                    AddSnapshotAnchor(anchors, seen, new SnapshotSemanticAnchor
                    {
                        ownerKind = "canonical", ownerId = routeId, ownerRevision = ownerRevision,
                        role = "gte_route", text = ((string)value ?? string.Empty).Trim()
                    });
            }

            var anchorById = package.anchors.ToDictionary(item => item.id, StringComparer.Ordinal);
            foreach (AnchorLinkRecord link in package.anchorLinks.Where(item => item.ownerKind == "topic"))
            {
                AnchorRecord anchor;
                if (!anchorById.TryGetValue(link.anchorId, out anchor))
                    throw new InvalidDataException("A selected Topic anchor link has no exact Anchor text: " + link.id);
                AddSnapshotAnchor(anchors, seen, new SnapshotSemanticAnchor
                {
                    ownerKind = "topic", ownerId = link.ownerId, ownerRevision = link.ownerRevision,
                    role = link.role, text = anchor.text
                });
            }
            if (anchors.Count == 0)
                throw new InvalidDataException("The selected SQL build set has no Generic route examples or Topic anchors to index.");

            anchors = anchors.OrderBy(item => item.ownerKind, StringComparer.Ordinal).ThenBy(item => item.ownerId, StringComparer.Ordinal)
                .ThenBy(item => item.ownerRevision, StringComparer.Ordinal).ThenBy(item => item.role, StringComparer.Ordinal)
                .ThenBy(item => item.text, StringComparer.Ordinal).ToList();
            var records = new JsonArray();
            foreach (SnapshotSemanticAnchor anchor in anchors)
                records.Add(new JsonObject
                {
                    ["ownerKind"] = new JsonPrimitive(anchor.ownerKind), ["ownerId"] = new JsonPrimitive(anchor.ownerId),
                    ["ownerRevision"] = new JsonPrimitive(anchor.ownerRevision), ["role"] = new JsonPrimitive(anchor.role),
                    ["text"] = new JsonPrimitive(anchor.text)
                });
            var input = new JsonObject
            {
                ["schemaVersion"] = new JsonPrimitive(1), ["snapshotHash"] = new JsonPrimitive(snapshot.snapshotHash),
                ["anchors"] = records
            };
            File.WriteAllBytes(requestPath, Encoding.UTF8.GetBytes(ContentJson.SerializeCompact(input)));

            string python = Path.Combine(projectRoot, "Tools", "SemanticLab", ".venv", "Scripts", "python.exe");
            string script = Path.Combine(projectRoot, "Tools", "SemanticLab", "bake_gte_anchors.py");
            if (!File.Exists(python)) throw new FileNotFoundException("The pinned offline SemanticLab Python environment is missing.", python);
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = python,
                Arguments = QuoteArgument(script) + " --snapshot-input " + QuoteArgument(requestPath) +
                    " --output " + QuoteArgument(vectorPath) + " --metadata " + QuoteArgument(metadataPath) +
                    " --cache-root " + QuoteArgument(cachePath),
                WorkingDirectory = Path.GetDirectoryName(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";
            start.EnvironmentVariables["TRANSFORMERS_OFFLINE"] = "1";
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(metadataPath) || !File.Exists(vectorPath))
                    throw new InvalidDataException("The selected SQL snapshot GTE bake failed offline (exit " + process.ExitCode + "): " +
                        (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
            }
        }

        private static void AddSnapshotAnchor(List<SnapshotSemanticAnchor> output, HashSet<string> seen,
            SnapshotSemanticAnchor anchor)
        {
            if (string.IsNullOrWhiteSpace(anchor.text)) return;
            string key = anchor.ownerKind + "\0" + anchor.ownerId + "\0" + anchor.ownerRevision + "\0" + anchor.role + "\0" + anchor.text;
            if (seen.Add(key)) output.Add(anchor);
        }

        private static string QuoteArgument(string value)
        { return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        private static string HashUtf8(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
        }

        private sealed class SnapshotSemanticAnchor
        { public string ownerKind, ownerId, ownerRevision, role, text; }

        private static string[] ReadGenericRouteIds(IEnumerable<ContentSourceDocument> sources)
        {
            var routes = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentSourceDocument source in sources.OrderBy(item => item.logicalPath, StringComparer.Ordinal))
            {
                JsonObject root = ContentJson.Parse(new UTF8Encoding(false, true).GetString(source.utf8Bytes)) as JsonObject;
                JsonObject routing = root == null || !root.ContainsKey("routing") ? null : root["routing"] as JsonObject;
                string route = routing == null || !routing.ContainsKey("routeId") ? string.Empty : (string)routing["routeId"];
                if (string.IsNullOrWhiteSpace(route)) throw new InvalidDataException("A Generic source has no host-readable route ID: " + source.logicalPath);
                routes.Add(route);
            }
            return routes.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        }

        private static string Describe(System.Collections.Generic.IList<ContentDiagnostic> diagnostics, string status)
        { return diagnostics == null || diagnostics.Count == 0 ? "Content build status: " + status : diagnostics[0].code + ": " + diagnostics[0].message; }

        private static string Describe(System.Collections.Generic.IList<AuthoringDiagnostic> diagnostics, string status)
        { return diagnostics == null || diagnostics.Count == 0 ? "Authoring status: " + status : diagnostics[0].code + ": " + diagnostics[0].message; }

        private static void DeleteOwnedTemporary(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (!AuthoringPathSafety.ContainsReparsePoint(path) && File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void DeleteOwnedTemporaryDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                AuthoringPathSafety.ContainsReparsePoint(fullPath))
                throw new InvalidDataException("Refusing to delete a semantic build directory outside its safe temporary root.");
            Directory.Delete(fullPath, true);
        }

        private static void EnsureSafePath(string path)
        {
            if (AuthoringPathSafety.ContainsReparsePoint(path))
                throw new InvalidDataException("SQL content path contains a symbolic link or junction: " + path);
        }
    }
}
