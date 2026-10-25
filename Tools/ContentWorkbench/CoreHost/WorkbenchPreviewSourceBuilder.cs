using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Activities;
using AlterOS.Authoring;
using AlterOS.Content;

namespace AlterOS.ContentWorkbench
{
    internal sealed class WorkbenchPreviewSnapshot
    {
        public ContentPackage Package;
        public ContentRelease Release;
        public ResolvedContentRelease ResolvedRelease;
        public ContentHostVocabulary Vocabulary;
        public string RootSourceKind;
        public string RootSourceId;
        public string RootRevision;
        public bool RootIsSynthetic;
        public object RootDocument;
        public object SourceSnapshot;
    }

    /// <summary>Imports one detached preview package from exact read-only authoring pins and/or one inline source.</summary>
    internal static class WorkbenchPreviewSourceBuilder
    {
        private const string SyntheticStoreId = "00000000-0000-4000-8000-000000000001";

        public static WorkbenchResult Prepare(WorkbenchEnvironment environment, IDictionary<string, object> payload,
            string kindId, string defaultSourceKind, WorkbenchPreviewSnapshot output)
        {
            if (environment == null || payload == null || output == null)
                return WorkbenchResult.Failure("invalid", "preview_source_request_invalid", "/document", "A preview source request is required.");

            string sourceKind = JsonWire.String(payload, "sourceKind", defaultSourceKind);
            if (!IsSupportedSourceKind(kindId, sourceKind))
                return WorkbenchResult.Failure("invalid", "preview_source_kind_invalid", "/sourceKind", "sourceKind is not supported for this preview kind.");

            object rawDocument;
            bool hasInlineDocument = payload.TryGetValue("document", out rawDocument);
            if (hasInlineDocument && JsonWire.AsObject(rawDocument) == null)
                return WorkbenchResult.Failure("invalid", "content_document_invalid", "/document", "document must be a JSON object.");
            string rootDocumentId = JsonWire.String(payload, "documentId");
            string rootRevision = JsonWire.String(payload, "revisionHash");
            bool hasPinnedRoot = rootDocumentId.Length > 0 || rootRevision.Length > 0;
            if (hasInlineDocument == hasPinnedRoot)
                return WorkbenchResult.Failure("invalid", "preview_source_selection_invalid", "/document",
                    "Choose exactly one inline document or exact documentId and revisionHash.");
            if (hasPinnedRoot && (!AuthoringRepository.IsIdentifier(rootDocumentId) || !AuthoringRepository.IsSha256(rootRevision)))
                return WorkbenchResult.Failure("invalid", "preview_source_revision_invalid", "/revisionHash",
                    "A saved preview source must pin a document ID and exact SHA-256 revision.");

            if (!TrySourcePins(payload, out List<PreviewSourcePin> pins, out WorkbenchResult pinFailure)) return pinFailure;
            string expectedStoreId = JsonWire.String(payload, "expectedStoreId");
            long expectedStoreRevision = -1;
            if (pins.Count > 0 || hasPinnedRoot)
            {
                if (!Guid.TryParseExact(expectedStoreId, "D", out _) || !JsonWire.TryLong(payload, "expectedStoreRevision", out expectedStoreRevision) || expectedStoreRevision < 0)
                    return WorkbenchResult.Failure("invalid", "preview_store_pin_invalid", "/expectedStoreRevision",
                        "Saved preview sources require the exact authoring store ID and revision.");
            }
            if (hasPinnedRoot && !pins.Any(item => item.documentId == rootDocumentId))
                pins.Add(new PreviewSourcePin { documentId = rootDocumentId, revisionHash = rootRevision });
            else if (hasPinnedRoot && pins.First(item => item.documentId == rootDocumentId).revisionHash != rootRevision)
                return WorkbenchResult.Failure("conflict", "preview_root_pin_mismatch", "/sourcePins",
                    "The selected root document pin does not match revisionHash.");

            var sources = new List<ContentSourceDocument>();
            var sourcePins = new List<object>();
            AuthoringDocument pinnedRoot = null;
            string storeId = SyntheticStoreId;
            long? storeRevision = null;
            string snapshotHash = "";

            if (pins.Count > 0)
            {
                if (!WorkbenchAuthoringStore.WorkspaceIsSafe(environment, out string safetyError))
                    return WorkbenchResult.Failure("invalid", "authoring_path_rejected", "/database", safetyError);
                AuthoringResult<AuthoringRepository> opened = AuthoringRepository.OpenExisting(environment.AuthoringDatabasePath);
                if (!opened.IsReady) return FromAuthoringFailure(opened.status, opened.diagnostics);
                using (opened.value)
                {
                    AuthoringResult<AuthoringSnapshot> read = opened.value.ReadSnapshot(pins.Select(item => item.documentId).ToList());
                    if (!read.IsReady) return FromAuthoringFailure(read.status, read.diagnostics);
                    AuthoringSnapshot snapshot = read.value;
                    if (snapshot.storeId != expectedStoreId || snapshot.storeRevision != expectedStoreRevision)
                        return WorkbenchResult.Failure("conflict", "preview_snapshot_changed", "/expectedStoreRevision",
                            "The authoring store changed after the preview source revisions were selected.");
                    if (!AuthoringRepository.ValidateSnapshot(snapshot, out string snapshotError))
                        return WorkbenchResult.Failure("invalid", "preview_snapshot_invalid", "/sourcePins", snapshotError);

                    var requested = pins.ToDictionary(item => item.documentId, StringComparer.Ordinal);
                    foreach (AuthoringDocument document in snapshot.documents)
                    {
                        if (!requested.TryGetValue(document.documentId, out PreviewSourcePin pin) || pin.revisionHash != document.revisionHash)
                            return WorkbenchResult.Failure("conflict", "preview_source_revision_changed", "/sourcePins/" + document.documentId,
                                "A selected source revision changed. Refresh and explicitly select the new revision before previewing.");
                        AuthoringResult<ContentSourceDocument> projected = AuthoringContentSourceProjection.Project(document);
                        if (!projected.IsReady)
                        {
                            AuthoringDiagnostic diagnostic = projected.diagnostics.FirstOrDefault();
                            return WorkbenchResult.Failure("invalid", diagnostic == null ? "preview_source_projection_failed" : diagnostic.code,
                                diagnostic == null ? document.logicalPath : diagnostic.path,
                                diagnostic == null ? "A selected source could not be projected." : diagnostic.message);
                        }
                        sources.Add(projected.value);
                        if (document.documentId == rootDocumentId) pinnedRoot = document;
                        sourcePins.Add(new Dictionary<string, object>
                        {
                            { "documentId", document.documentId }, { "kind", document.kind },
                            { "logicalPath", document.logicalPath }, { "revisionHash", document.revisionHash }
                        });
                    }
                    storeId = snapshot.storeId;
                    storeRevision = snapshot.storeRevision;
                    snapshotHash = snapshot.snapshotHash;
                }
            }

            string sourceId;
            byte[] inlineBytes = null;
            if (hasInlineDocument)
            {
                sourceId = ReadIdentity(rawDocument, sourceKind);
                if (!AuthoringRepository.IsIdentifier(sourceId))
                    return WorkbenchResult.Failure("invalid", "preview_source_identity_invalid", "/document", "The inline source must contain a stable content identity.");
                if (sources.Any(item => item.kind == sourceKind && item.sourceId == sourceId))
                    return WorkbenchResult.Failure("invalid", "preview_source_identity_duplicate", "/document", "The inline root duplicates a selected source ID; use documentId and revisionHash to preview the saved revision.");
                try { inlineBytes = JsonWire.SerializeBytes(rawDocument); }
                catch (Exception exception)
                { return WorkbenchResult.Failure("invalid", "preview_source_json_invalid", "/document", "The inline source could not be serialized: " + exception.GetType().Name); }
                string sourceHash = AuthoringRepository.Hash(inlineBytes);
                sources.Add(new ContentSourceDocument(sourceKind, "Preview/" + sourceKind + "/" + sourceId + ".json",
                    sourceId, sourceHash, inlineBytes));
                output.RootSourceId = sourceId;
                output.RootRevision = sourceHash;
                output.RootIsSynthetic = true;
                output.RootDocument = rawDocument;
            }
            else
            {
                if (pinnedRoot == null || pinnedRoot.kind != sourceKind || pinnedRoot.revisionHash != rootRevision)
                    return WorkbenchResult.Failure("conflict", "preview_root_revision_changed", "/revisionHash",
                        "The exact saved root source is absent from the selected authoring snapshot.");
                ContentSourceDocument projectedRoot = sources.FirstOrDefault(item => item.logicalPath == pinnedRoot.logicalPath);
                output.RootSourceId = projectedRoot == null ? string.Empty : projectedRoot.sourceId;
                output.RootRevision = pinnedRoot.revisionHash;
                output.RootIsSynthetic = false;
                try { output.RootDocument = JsonWire.Deserialize(pinnedRoot.documentBytes); }
                catch (Exception exception)
                { return WorkbenchResult.Failure("invalid", "preview_source_json_invalid", "/document", "The selected source could not be parsed: " + exception.GetType().Name); }
            }

            output.RootSourceKind = sourceKind;
            output.SourceSnapshot = new Dictionary<string, object>
            {
                { "storeId", storeId }, { "storeRevision", storeRevision }, { "snapshotHash", snapshotHash },
                { "sourcePins", sourcePins }, { "rootIsSynthetic", output.RootIsSynthetic },
                { "rootSourceKind", sourceKind }, { "rootSourceId", output.RootSourceId }, { "rootRevision", output.RootRevision }
            };

            ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder(
                WorkbenchAuthoringStore.PackageId, storeId, sources, ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder());
            if (!imported.IsReady) return FromContentFailure(imported.status, imported.diagnostics);
            ContentPackage package = imported.value;
            ContentHostVocabulary vocabulary = WorkbenchAuthoringStore.CreateReferenceValidationVocabulary(environment, package);
            CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package, vocabulary);
            if (!compiled.IsReady) return FromContentFailure(compiled.status, compiled.diagnostics);
            ContentResult<ResolvedContentRelease> resolved = ContentFlowAdapter.Resolve(compiled.package, compiled.release, vocabulary);
            if (!resolved.IsReady) return FromContentFailure(resolved.status, resolved.diagnostics);

            output.Package = compiled.package;
            output.Release = compiled.release;
            output.ResolvedRelease = resolved.value;
            output.Vocabulary = vocabulary;
            return WorkbenchResult.Ok(null);
        }

        private static bool TrySourcePins(IDictionary<string, object> payload, out List<PreviewSourcePin> pins,
            out WorkbenchResult failure)
        {
            pins = new List<PreviewSourcePin>();
            failure = null;
            if (!payload.TryGetValue("sourcePins", out object raw)) return true;
            IEnumerable entries = JsonWire.AsArray(raw);
            if (entries == null || raw is string)
            {
                failure = WorkbenchResult.Failure("invalid", "preview_source_pins_invalid", "/sourcePins", "sourcePins must be an array.");
                return false;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object entry in entries)
            {
                IDictionary<string, object> value = JsonWire.AsObject(entry);
                string documentId = JsonWire.String(value, "documentId");
                string revisionHash = JsonWire.String(value, "revisionHash");
                if (value == null || !AuthoringRepository.IsIdentifier(documentId) || !AuthoringRepository.IsSha256(revisionHash) || !seen.Add(documentId))
                {
                    failure = WorkbenchResult.Failure("invalid", "preview_source_pins_invalid", "/sourcePins", "Each source pin needs one unique document ID and exact SHA-256 revision.");
                    return false;
                }
                pins.Add(new PreviewSourcePin { documentId = documentId, revisionHash = revisionHash });
                if (pins.Count > 100)
                {
                    failure = WorkbenchResult.Failure("invalid", "preview_source_pins_too_many", "/sourcePins", "A preview may pin at most 100 authoring source documents.");
                    return false;
                }
            }
            return true;
        }

        private static string ReadIdentity(object rawDocument, string sourceKind)
        {
            IDictionary<string, object> document = JsonWire.AsObject(rawDocument);
            if (sourceKind == ContentSourceKinds.FurnitureCatalogueSettings) return "furniture.catalogue";
            string field = sourceKind == ContentSourceKinds.TopicDefinition ? "topicId" :
                sourceKind == ContentSourceKinds.ScenarioDefinition ? "scenarioId" : "id";
            return JsonWire.String(document, field);
        }

        private static bool IsSupportedSourceKind(string kindId, string sourceKind)
        {
            if (kindId == "dialogue") return sourceKind == ContentSourceKinds.DialogueDefinition || sourceKind == ContentSourceKinds.GenericDialogue;
            return kindId == "scenario" && sourceKind == ContentSourceKinds.ScenarioDefinition ||
                kindId == "topic" && sourceKind == ContentSourceKinds.TopicDefinition ||
                kindId == "furniture" && sourceKind == ContentSourceKinds.FurnitureDefinition ||
                kindId == "item" && sourceKind == ContentSourceKinds.ItemDefinition ||
                kindId == "action" && sourceKind == ContentSourceKinds.ActionDefinition;
        }

        private static WorkbenchResult FromAuthoringFailure(string status, IList<AuthoringDiagnostic> diagnostics)
        {
            AuthoringDiagnostic first = diagnostics == null ? null : diagnostics.FirstOrDefault();
            return WorkbenchResult.Failure(status, first == null ? "preview_authoring_snapshot_failed" : first.code,
                first == null ? "/sourcePins" : first.path, first == null ? "The selected authoring revisions could not be read." : first.message);
        }

        private static WorkbenchResult FromContentFailure(string status, IList<ContentDiagnostic> diagnostics)
        {
            ContentDiagnostic first = diagnostics == null ? null : diagnostics.FirstOrDefault();
            string resultStatus = status == ContentStatuses.Missing ? "invalid" : status;
            return WorkbenchResult.Failure(resultStatus, first == null ? "preview_content_package_invalid" : first.code,
                first == null ? "/document" : first.path, first == null ? "The detached preview package is invalid." : first.message);
        }

        private sealed class PreviewSourcePin
        {
            public string documentId;
            public string revisionHash;
        }
    }
}
