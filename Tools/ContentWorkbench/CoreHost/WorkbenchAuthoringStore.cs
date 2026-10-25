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
    internal static class WorkbenchAuthoringStore
    {
        private const int MaximumDraftBytes = 1024 * 1024;
        internal const string PackageId = "alteros.authoring";

        public static WorkbenchResult Initialize(WorkbenchEnvironment environment)
        {
            string error;
            if (!WorkspaceIsSafe(environment, out error))
                return WorkbenchResult.Failure("invalid", "authoring_path_rejected", "/database", error);
            try { Directory.CreateDirectory(environment.AuthoringWorkspaceRoot); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "authoring_workspace_create_failed", "/database", exception.GetType().Name); }
            if (!WorkspaceIsSafe(environment, out error))
                return WorkbenchResult.Failure("invalid", "authoring_path_rejected", "/database", error);

            AuthoringResult<AuthoringRepository> created = AuthoringRepository.CreateNew(environment.AuthoringDatabasePath);
            if (!created.IsReady) return ConvertFailure(created);
            using (created.value)
                return Project(created.value.ReadStoreInfo(), ToStoreInfo, "store");
        }

        public static WorkbenchResult Info(WorkbenchEnvironment environment)
        {
            return WithRepository(environment, repository => repository.ReadStoreInfo(), ToStoreInfo, "store");
        }

        public static WorkbenchResult DraftList(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string kind = JsonWire.String(payload, "kind");
            if (kind.Length > 0 && kind != "topic" && kind != "scenario")
                return WorkbenchResult.Failure("invalid", "draft_kind_invalid", "/kind", "kind must be topic or scenario.");

            return WithRepositoryResult(environment, repository =>
            {
                var entries = new List<object>();
                foreach (string selectedKind in kind.Length == 0 ? new[] { "topic", "scenario" } : new[] { kind })
                {
                    string cursor = string.Empty;
                    do
                    {
                        AuthoringResult<AuthoringDocumentPage> page = repository.List(selectedKind, cursor, 100);
                        if (!page.IsReady) return ConvertFailure(page);
                        foreach (AuthoringDocumentSummary summary in page.value.documents)
                        {
                            AuthoringResult<AuthoringDocument> read = repository.Read(summary.documentId, summary.revisionHash);
                            if (!read.IsReady) return ConvertFailure(read);
                            IDictionary<string, object> wrapper;
                            if (summary.logicalPath != DraftLogicalPath(selectedKind, summary.documentId) ||
                                !TryParseDraft(read.value, selectedKind, summary.documentId, out wrapper)) continue;
                            entries.Add(new Dictionary<string, object>
                            {
                                { "kind", selectedKind },
                                { "id", summary.documentId },
                                { "contentHash", summary.revisionHash }
                            });
                        }
                        cursor = page.value.nextCursor;
                    } while (cursor.Length > 0);
                }
                return WorkbenchResult.Ok(new Dictionary<string, object> { { "drafts", entries } });
            });
        }

        public static WorkbenchResult DraftRead(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string kind = JsonWire.String(payload, "kind");
            string id = JsonWire.String(payload, "id");
            if (!TryDraftIdentity(kind, id, out string logicalPath, out WorkbenchResult invalid)) return invalid;
            return WithRepositoryResult(environment, repository =>
            {
                AuthoringResult<AuthoringDocument> read = repository.Read(id, string.Empty);
                if (!read.IsReady) return ConvertFailure(read);
                IDictionary<string, object> wrapper;
                if (read.value.kind != kind || read.value.logicalPath != logicalPath || !TryParseDraft(read.value, kind, id, out wrapper))
                    return WorkbenchResult.Failure("invalid", "draft_json_invalid", "/document", "The SQL revision is not a Workbench draft envelope.");
                return WorkbenchResult.Ok(new Dictionary<string, object>
                {
                    { "kind", kind },
                    { "id", id },
                    { "document", wrapper["document"] },
                    { "editorState", wrapper.ContainsKey("editorState") ? wrapper["editorState"] : null },
                    { "contentHash", read.value.revisionHash }
                });
            });
        }

        public static WorkbenchResult DraftWrite(WorkbenchEnvironment environment, IDictionary<string, object> payload, string requestId)
        {
            string kind = JsonWire.String(payload, "kind");
            string id = JsonWire.String(payload, "id");
            if (!TryDraftIdentity(kind, id, out string logicalPath, out WorkbenchResult invalid)) return invalid;
            object rawDocument;
            if (!payload.TryGetValue("document", out rawDocument) || JsonWire.AsObject(rawDocument) == null)
                return WorkbenchResult.Failure("invalid", "draft_document_invalid", "/document", "document must be a JSON object.");
            object rawEditorState;
            payload.TryGetValue("editorState", out rawEditorState);
            if (rawEditorState != null && JsonWire.AsObject(rawEditorState) == null)
                return WorkbenchResult.Failure("invalid", "editor_state_invalid", "/editorState", "editorState must be a JSON object or null.");
            if (!TryExpectedHash(payload, out string expectedHash, out invalid)) return invalid;

            var wrapper = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "workbenchSchemaVersion", 1 },
                { "kind", kind },
                { "id", id },
                { "document", rawDocument }
            };
            if (payload.ContainsKey("editorState") && rawEditorState != null) wrapper.Add("editorState", rawEditorState);
            byte[] bytes;
            try { bytes = JsonWire.SerializeBytes(wrapper); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "draft_document_invalid", "/document", "Draft JSON could not be serialized: " + exception.GetType().Name); }
            if (bytes.Length > MaximumDraftBytes)
                return WorkbenchResult.Failure("invalid", "draft_too_large", "/document", "Drafts are limited to 1 MiB.");

            var request = new AuthoringSaveRequest
            {
                documentId = id,
                kind = kind,
                logicalPath = logicalPath,
                expectedHash = expectedHash,
                documentBytes = bytes,
                operationId = OperationId("draft:" + (requestId ?? Guid.NewGuid().ToString("N")))
            };
            return WithRepository(environment, repository => repository.Save(request), ToDraftWriteResult, null);
        }

        public static WorkbenchResult List(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string kind = JsonWire.String(payload, "kind");
            string cursor = JsonWire.String(payload, "cursor");
            int limit;
            if (!JsonWire.TryInt(payload, "limit", out limit)) limit = 100;
            return WithRepository(environment, repository => repository.List(kind, cursor, limit), page =>
                new Dictionary<string, object>
                {
                    { "documents", page.documents.Select(ToSummary).ToList() },
                    { "nextCursor", page.nextCursor }
                }, null);
        }

        public static WorkbenchResult Read(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string documentId = JsonWire.String(payload, "documentId");
            string revisionHash = JsonWire.String(payload, "revisionHash");
            return WithRepository(environment, repository => repository.Read(documentId, revisionHash), document =>
            {
                if (document.documentBytes.Length > MaximumDraftBytes)
                    throw new InvalidDataException("The document exceeds the 1 MiB Workbench response limit.");
                object value = JsonWire.Deserialize(document.documentBytes);
                if (JsonWire.AsObject(value) == null)
                    throw new InvalidDataException("The document root is not a JSON object.");
                var result = ToSummary(document);
                result["document"] = value;
                result["contentHash"] = document.revisionHash;
                return result;
            }, null);
        }

        public static WorkbenchResult ListCompiledReferences(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string targetKind = JsonWire.String(payload, "targetKind");
            if (targetKind != "dialogue" && targetKind != "action" && targetKind != "scenario" && targetKind != "topic" &&
                targetKind != "furniture" && targetKind != "item" && targetKind != "text")
                return WorkbenchResult.Failure("invalid", "reference_target_kind_invalid", "/targetKind",
                    "targetKind must be dialogue, action, scenario, topic, furniture, item or text.");
            if (!TrySourcePins(payload, out List<ReferenceSourcePin> pins))
                return WorkbenchResult.Failure("invalid", "reference_source_pins_invalid", "/sourcePins",
                    "sourcePins must contain 1..100 selected document IDs with their exact current revision hashes.");
            string expectedStoreId = JsonWire.String(payload, "expectedStoreId");
            if (!Guid.TryParse(expectedStoreId, out Guid parsedStoreId) || parsedStoreId.ToString("D") != expectedStoreId)
                return WorkbenchResult.Failure("invalid", "reference_store_identity_invalid", "/expectedStoreId", "Select a canonical authoring store identity first.");
            if (!JsonWire.TryLong(payload, "expectedStoreRevision", out long expectedStoreRevision) || expectedStoreRevision < 0)
                return WorkbenchResult.Failure("invalid", "reference_store_revision_invalid", "/expectedStoreRevision", "Select an exact nonnegative authoring store revision first.");

            return WithRepositoryResult(environment, repository =>
            {
                AuthoringResult<AuthoringSnapshot> read = repository.ReadSnapshot(pins.Select(item => item.documentId).ToList());
                if (!read.IsReady) return ConvertFailure(read);
                AuthoringSnapshot snapshot = read.value;
                if (snapshot.storeId != expectedStoreId || snapshot.storeRevision != expectedStoreRevision)
                    return WorkbenchResult.Failure("conflict", "reference_snapshot_changed", "/expectedStoreRevision",
                        "The authoring store changed after the source list was shown. Refresh the selected source revisions before resolving.");
                if (!AuthoringRepository.ValidateSnapshot(snapshot, out string snapshotError))
                    return WorkbenchResult.Failure("invalid", "reference_snapshot_invalid", "/sourcePins", snapshotError);

                var requestedPins = pins.ToDictionary(item => item.documentId, StringComparer.Ordinal);
                foreach (AuthoringDocument document in snapshot.documents)
                    if (!requestedPins.TryGetValue(document.documentId, out ReferenceSourcePin pin) || pin.revisionHash != document.revisionHash)
                        return WorkbenchResult.Failure("conflict", "reference_source_revision_changed", "/sourcePins/" + document.documentId,
                            "A selected source revision changed. Refresh and explicitly select its new revision before resolving.");

                var sources = new List<ContentSourceDocument>(snapshot.documents.Count);
                foreach (AuthoringDocument document in snapshot.documents)
                {
                    AuthoringResult<ContentSourceDocument> projected = AuthoringContentSourceProjection.Project(document);
                    if (!projected.IsReady)
                    {
                        AuthoringDiagnostic diagnostic = projected.diagnostics.FirstOrDefault();
                        return WorkbenchResult.Failure("invalid", diagnostic == null ? "reference_source_projection_failed" : diagnostic.code,
                            diagnostic == null ? document.logicalPath : diagnostic.path,
                            diagnostic == null ? "A selected source could not be projected." : diagnostic.message);
                    }
                    sources.Add(projected.value);
                }

                ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder(
                    PackageId, snapshot.storeId, sources, ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder());
                if (!imported.IsReady) return WorkbenchAuthoringStore.ConvertContentFailure(imported.status, imported.diagnostics);
                ContentPackage package = imported.value;
                ContentResult<List<ContentReleaseDependency>> closure = ContentReleaseCompiler.ResolveDependencies(
                    package, CreateReferenceValidationVocabulary(environment, package));
                if (!closure.IsReady) return ConvertContentFailure(closure.status, closure.diagnostics);
                var sourceByContent = package.legacySources.Where(item => item != null)
                    .GroupBy(item => item.contentId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
                var references = new List<object>();
                foreach (FlowRecord record in package.flows.Where(item => item != null && item.kind == "scenario"))
                    AddCompiledReference(references, "scenario", package.storeId, record.id, record.revision, record.semanticHash,
                        record.kind, sourceByContent, snapshot.documents);
                foreach (DialogueRecord record in package.dialogues.Where(item => item != null))
                    AddCompiledReference(references, "dialogue", package.storeId, record.id, record.revision, record.semanticHash,
                        "dialogue", sourceByContent, snapshot.documents);
                foreach (ActionRecord record in package.actions.Where(item => item != null))
                    AddCompiledReference(references, "action", package.storeId, record.id, record.revision, record.semanticHash,
                        "action", sourceByContent, snapshot.documents);
                foreach (ItemRecord record in package.items.Where(item => item != null))
                    AddCompiledReference(references, "item", package.storeId, record.id, record.revision, record.semanticHash,
                        "item", sourceByContent, snapshot.documents);
                foreach (FurnitureDefinitionRecord record in package.furnitureDefinitions.Where(item => item != null))
                    AddCompiledReference(references, "furniture", package.storeId, record.id, record.revision, record.semanticHash,
                        "furniture", sourceByContent, snapshot.documents);
                foreach (TopicRecord record in package.topics.Where(item => item != null))
                    AddCompiledReference(references, "topic", package.storeId, record.id, record.revision, record.semanticHash,
                        "topic", sourceByContent, snapshot.documents, record.entries);
                foreach (TextRecord record in package.texts.Where(item => item != null))
                    AddCompiledReference(references, "text", package.storeId, record.id, record.revision, record.semanticHash,
                        "text", sourceByContent, snapshot.documents);
                return WorkbenchResult.Ok(new Dictionary<string, object>
                {
                    { "storeId", snapshot.storeId },
                    { "storeRevision", snapshot.storeRevision },
                    { "snapshotHash", snapshot.snapshotHash },
                    { "dependencyCount", closure.value.Count },
                    { "sourcePins", snapshot.documents.Select(document => (object)new Dictionary<string, object>
                        {
                            { "documentId", document.documentId }, { "kind", document.kind }, { "logicalPath", document.logicalPath },
                            { "revisionHash", document.revisionHash }
                        }).ToList() },
                    { "references", references.Where(item => (string)((IDictionary<string, object>)item)["targetKind"] == targetKind)
                        .OrderBy(item => (string)((IDictionary<string, object>)item)["targetKind"], StringComparer.Ordinal)
                        .ThenBy(item => (string)((IDictionary<string, object>)item)["refId"], StringComparer.Ordinal).ToList() }
                });
            });
        }

        internal static ContentHostVocabulary CreateReferenceValidationVocabulary(WorkbenchEnvironment environment,
            ContentPackage package)
        {
            ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
            string[] sequenceCapabilities = catalog.Primitives.All.Select(item => item.requiredCapabilityId)
                .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
            var expressions = new HashSet<string>(StringComparer.Ordinal);
            string presetRoot = Path.Combine(environment.ProjectRoot, "Assets", "Altera", "Presets");
            if (Directory.Exists(presetRoot))
            {
                foreach (string path in Directory.GetFiles(presetRoot, "*.asset", SearchOption.AllDirectories))
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.TrimStart();
                        const string field = "expressionName:";
                        if (!trimmed.StartsWith(field, StringComparison.Ordinal)) continue;
                        string value = trimmed.Substring(field.Length).Trim().Trim('"', '\'');
                        if (value.Length > 0) expressions.Add(value);
                    }
            }

            var canonicalOwners = new HashSet<string>(StringComparer.Ordinal);
            foreach (LegacySourceRecord source in package.legacySources ?? new List<LegacySourceRecord>())
            {
                if (source == null || source.kind != ContentSourceKinds.GenericDialogue) continue;
                try
                {
                    IDictionary<string, object> routing = JsonWire.AsObject(JsonWire.Deserialize(source.metadataJson ?? "{}"));
                    string routeId = JsonWire.String(routing, "routeId");
                    if (!string.IsNullOrWhiteSpace(routeId)) canonicalOwners.Add(routeId);
                }
                catch { }
            }

            return new ContentHostVocabulary(
                new[] { ActivityCapabilityIds.ComeHere, ActivityCapabilityIds.FollowPlayer, ActivityCapabilityIds.DrinkWater,
                    ActivityCapabilityIds.ActivitySequence }
                    .Concat(sequenceCapabilities).Concat(ContentHostEffectFunctionRegistry.RegisteredCapabilityIds),
                expressions, new string[0], new string[0], canonicalOwners, catalog.CreateBuilder());
        }

        private static void AddCompiledReference(List<object> output, string targetKind, string store, string id,
            string revision, string expectedHash, string contentKind, IDictionary<string, List<LegacySourceRecord>> sourceByContent,
            IList<AuthoringDocument> snapshotDocuments, IList<TopicEntryRecord> topicEntries = null)
        {
            sourceByContent.TryGetValue(id ?? string.Empty, out List<LegacySourceRecord> owners);
            var documentByPath = (snapshotDocuments ?? new AuthoringDocument[0]).Where(item => item != null)
                .GroupBy(item => item.logicalPath, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var result = new Dictionary<string, object>
            {
                { "targetKind", targetKind },
                { "refId", id ?? string.Empty },
                { "contentKind", contentKind ?? string.Empty },
                { "ref", new Dictionary<string, object>
                    { { "store", store ?? string.Empty }, { "id", id ?? string.Empty }, { "revision", revision ?? string.Empty }, { "expectedHash", expectedHash ?? string.Empty } } },
                { "ownerSources", (owners ?? new List<LegacySourceRecord>()).Select(owner =>
                    {
                        documentByPath.TryGetValue(owner.relativePath ?? string.Empty, out AuthoringDocument sourceDocument);
                        return (object)new Dictionary<string, object>
                        {
                            { "documentId", sourceDocument == null ? string.Empty : sourceDocument.documentId },
                            { "revisionHash", sourceDocument == null ? string.Empty : sourceDocument.revisionHash },
                            { "sourceId", owner.sourceId }, { "logicalPath", owner.relativePath }, { "sourceKind", owner.kind }
                        };
                    }).ToList() }
            };
            if (topicEntries != null)
                result.Add("topicEntries", topicEntries.Where(item => item != null).Select(item => (object)new Dictionary<string, object>
                {
                    { "entryId", item.id }, { "flowRef", item.flowRef == null ? null : new Dictionary<string, object>
                        { { "store", item.flowRef.store }, { "id", item.flowRef.id }, { "revision", item.flowRef.revision }, { "expectedHash", item.flowRef.expectedHash } } }
                }).ToList());
            output.Add(result);
        }

        private static bool TrySourcePins(IDictionary<string, object> payload, out List<ReferenceSourcePin> pins)
        {
            pins = new List<ReferenceSourcePin>();
            if (payload == null || !payload.TryGetValue("sourcePins", out object raw)) return false;
            IEnumerable items = JsonWire.AsArray(raw);
            if (items == null || raw is string) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in items)
            {
                IDictionary<string, object> value = JsonWire.AsObject(item);
                string id = JsonWire.String(value, "documentId"), hash = JsonWire.String(value, "revisionHash");
                if (value == null || !AuthoringRepository.IsIdentifier(id) || !AuthoringRepository.IsSha256(hash) || !seen.Add(id))
                    return false;
                pins.Add(new ReferenceSourcePin { documentId = id, revisionHash = hash });
            }
            return pins.Count > 0 && pins.Count <= 100;
        }

        private sealed class ReferenceSourcePin
        {
            public string documentId;
            public string revisionHash;
        }

        public static WorkbenchResult Write(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            object rawDocument;
            if (!payload.TryGetValue("document", out rawDocument) || JsonWire.AsObject(rawDocument) == null)
                return WorkbenchResult.Failure("invalid", "authoring_document_invalid", "/document", "document must be a JSON object.");
            if (!TryExpectedHash(payload, out string expectedHash, out WorkbenchResult invalid)) return invalid;
            string operationId = JsonWire.String(payload, "operationId");
            if (!AuthoringRepository.IsOperationId(operationId))
                return WorkbenchResult.Failure("invalid", "authoring_operation_id_invalid", "/operationId", "operationId must be a stable 1..128 character identifier.");
            byte[] bytes;
            try { bytes = JsonWire.SerializeBytes(rawDocument); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "authoring_document_invalid", "/document", "Document JSON could not be serialized: " + exception.GetType().Name); }
            var request = new AuthoringSaveRequest
            {
                documentId = JsonWire.String(payload, "documentId"),
                kind = JsonWire.String(payload, "kind"),
                logicalPath = JsonWire.String(payload, "logicalPath"),
                expectedHash = expectedHash,
                documentBytes = bytes,
                operationId = operationId
            };
            return WithRepository(environment, repository => repository.Save(request), ToSavedDocument, null);
        }

        public static WorkbenchResult ReadSelection(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string name = JsonWire.String(payload, "name", "default");
            return WithRepository(environment, repository => repository.ReadBuildSet(name), ToBuildSet, "selection");
        }

        public static WorkbenchResult WriteSelection(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            if (!TryStringList(payload, "documentIds", out List<string> documentIds))
                return WorkbenchResult.Failure("invalid", "authoring_selection_invalid", "/documentIds", "documentIds must be an array of stable string IDs.");
            if (!TryExpectedHash(payload, out string expectedHash, out WorkbenchResult invalid)) return invalid;
            string operationId = JsonWire.String(payload, "operationId");
            if (!AuthoringRepository.IsOperationId(operationId))
                return WorkbenchResult.Failure("invalid", "authoring_operation_id_invalid", "/operationId", "operationId must be a stable 1..128 character identifier.");
            var request = new AuthoringBuildSetRequest
            {
                name = JsonWire.String(payload, "name", "default"),
                documentIds = documentIds,
                expectedHash = expectedHash,
                operationId = operationId
            };
            return WithRepository(environment, repository => repository.SaveBuildSet(request), ToBuildSet, "selection");
        }

        public static WorkbenchResult Import(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string mode = JsonWire.String(payload, "mode");
            string expectedStoreId = JsonWire.String(payload, "expectedStoreId");
            long expectedStoreRevision;
            if (!JsonWire.TryLong(payload, "expectedStoreRevision", out expectedStoreRevision) || expectedStoreRevision < 0)
                return WorkbenchResult.Failure("invalid", "authoring_store_revision_invalid", "/expectedStoreRevision", "expectedStoreRevision must be a nonnegative store revision.");
            string operationId = JsonWire.String(payload, "operationId");
            if (!AuthoringRepository.IsOperationId(operationId))
                return WorkbenchResult.Failure("invalid", "authoring_operation_id_invalid", "/operationId", "operationId must be a stable 1..128 character identifier.");

            return WithRepositoryResult(environment, repository =>
            {
                AuthoringResult<AuthoringStoreInfo> info = repository.ReadStoreInfo();
                if (!info.IsReady) return ConvertFailure(info);
                if (info.value.storeId != expectedStoreId)
                    return WorkbenchResult.Failure("conflict", "authoring_import_store_conflict", "/expectedStoreId", "The import request targets a different authoring store.");

                AuthoringImportRequest request;
                if (mode == "legacy")
                {
                    ContentResult<ContentSourceManifest> manifest = ContentSourceManifestBuilder.Create(
                        environment.AuthoringImportRoot, PackageId, expectedStoreId);
                    if (!manifest.IsReady) return ConvertContentFailure(manifest.status, manifest.diagnostics);
                    var sources = new List<AuthoringImportSource>(manifest.value.sources.Count);
                    foreach (ContentSourceFile source in manifest.value.sources)
                    {
                        string documentId = ImportedDocumentId(source.kind, source.relativePath);
                        sources.Add(new AuthoringImportSource
                        {
                            documentId = documentId,
                            kind = source.kind,
                            logicalPath = source.relativePath,
                            sourcePath = source.relativePath,
                            sourceHash = source.sha256
                        });
                    }
                    request = new AuthoringImportRequest
                    {
                        sourceRoot = environment.AuthoringImportRoot,
                        sources = sources,
                        expectedStoreId = expectedStoreId,
                        expectedStoreRevision = expectedStoreRevision,
                        operationId = operationId
                    };
                }
                else if (mode == "export")
                {
                    if (expectedStoreRevision != 0)
                        return WorkbenchResult.Failure("conflict", "authoring_export_target_not_empty", "/database", "A snapshot export can only be imported into a fresh empty authoring store.");
                    string snapshotHash = JsonWire.String(payload, "snapshotHash");
                    if (!AuthoringRepository.IsSha256(snapshotHash))
                        return WorkbenchResult.Failure("invalid", "authoring_export_hash_invalid", "/snapshotHash", "snapshotHash must be a lowercase SHA-256.");
                    if (AuthoringPathSafety.ContainsReparsePoint(environment.AuthoringExportsRoot))
                        return WorkbenchResult.Failure("invalid", "authoring_export_path_rejected", "/export", "The configured export root contains a symbolic link or junction.");
                    string directory = Path.Combine(environment.AuthoringExportsRoot, snapshotHash);
                    AuthoringResult<AuthoringImportRequest> created = AuthoringJsonExporter.CreateImportRequest(
                        directory, expectedStoreId, expectedStoreRevision, operationId);
                    if (!created.IsReady) return ConvertFailure(created);
                    request = created.value;
                }
                else
                    return WorkbenchResult.Failure("invalid", "authoring_import_mode_invalid", "/mode", "mode must be legacy or export.");

                AuthoringResult<AuthoringImportReceipt> imported = AuthoringJsonImporter.ImportOnce(repository, request);
                return Project(imported, ToImportReceipt, "receipt");
            });
        }

        public static WorkbenchResult Export(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            if (!TryStringList(payload, "documentIds", out List<string> documentIds))
                return WorkbenchResult.Failure("invalid", "authoring_export_selection_invalid", "/documentIds", "documentIds must be an array of stable string IDs.");
            string storeId = JsonWire.String(payload, "expectedStoreId");
            long storeRevision;
            if (!JsonWire.TryLong(payload, "expectedStoreRevision", out storeRevision) || storeRevision < 0)
                return WorkbenchResult.Failure("invalid", "authoring_store_revision_invalid", "/expectedStoreRevision", "expectedStoreRevision must be a nonnegative store revision.");
            var request = new AuthoringExportRequest
            {
                documentIds = documentIds,
                expectedStoreId = storeId,
                expectedStoreRevision = storeRevision,
                destinationRoot = environment.AuthoringExportsRoot
            };
            return WithRepositoryResult(environment, repository =>
            {
                if (AuthoringPathSafety.ContainsReparsePoint(environment.AuthoringExportsRoot))
                    return WorkbenchResult.Failure("invalid", "authoring_export_path_rejected", "/export", "The configured export root contains a symbolic link or junction.");
                try { Directory.CreateDirectory(environment.AuthoringExportsRoot); }
                catch (Exception exception)
                { return WorkbenchResult.Failure("invalid", "authoring_export_root_create_failed", "/export", exception.GetType().Name); }
                return Project(AuthoringJsonExporter.Export(repository, request), ToExportReceipt, "export");
            });
        }

        public static WorkbenchResult Backup(WorkbenchEnvironment environment)
        {
            string error;
            if (!WorkspaceIsSafe(environment, out error))
                return WorkbenchResult.Failure("invalid", "authoring_path_rejected", "/database", error);
            return WithRepositoryResult(environment, repository =>
            {
                AuthoringResult<AuthoringStoreInfo> info = repository.ReadStoreInfo();
                if (!info.IsReady) return ConvertFailure(info);
                string backupRoot = Path.Combine(environment.AuthoringWorkspaceRoot, "backups");
                if (AuthoringPathSafety.ContainsReparsePoint(backupRoot))
                    return WorkbenchResult.Failure("invalid", "authoring_backup_path_rejected", "/backup", "The configured backup root contains a symbolic link or junction.");
                Directory.CreateDirectory(backupRoot);
                if (AuthoringPathSafety.ContainsReparsePoint(backupRoot))
                    return WorkbenchResult.Failure("invalid", "authoring_backup_path_rejected", "/backup", "The configured backup root contains a symbolic link or junction.");
                string file = "authoring-r" + info.value.storeRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" +
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", System.Globalization.CultureInfo.InvariantCulture) + "-" +
                    Guid.NewGuid().ToString("N") + ".sqlite3";
                AuthoringResult<string> backup = repository.CreateBackup(Path.Combine(backupRoot, file));
                return Project(backup, path => new Dictionary<string, object> { { "backupPath", path } }, "backup");
            });
        }

        private static WorkbenchResult WithRepository<T>(WorkbenchEnvironment environment,
            Func<AuthoringRepository, AuthoringResult<T>> operation, Func<T, object> projector, string key)
        {
            string error;
            if (!WorkspaceIsSafe(environment, out error))
                return WorkbenchResult.Failure("invalid", "authoring_path_rejected", "/database", error);
            AuthoringResult<AuthoringRepository> opened = AuthoringRepository.OpenExisting(environment.AuthoringDatabasePath);
            if (!opened.IsReady) return ConvertFailure(opened);
            using (opened.value)
            {
                try { return Project(operation(opened.value), projector, key); }
                catch (Exception exception)
                { return WorkbenchResult.Failure("invalid", "authoring_response_invalid", "", "The authoring response could not be produced: " + exception.GetType().Name); }
            }
        }

        private static WorkbenchResult WithRepositoryResult(WorkbenchEnvironment environment,
            Func<AuthoringRepository, WorkbenchResult> operation)
        {
            string error;
            if (!WorkspaceIsSafe(environment, out error))
                return WorkbenchResult.Failure("invalid", "authoring_path_rejected", "/database", error);
            AuthoringResult<AuthoringRepository> opened = AuthoringRepository.OpenExisting(environment.AuthoringDatabasePath);
            if (!opened.IsReady) return ConvertFailure(opened);
            using (opened.value)
            {
                try { return operation(opened.value); }
                catch (Exception exception)
                { return WorkbenchResult.Failure("invalid", "authoring_response_invalid", "", "The authoring response could not be produced: " + exception.GetType().Name); }
            }
        }

        private static WorkbenchResult Project<T>(AuthoringResult<T> result, Func<T, object> projector, string key)
        {
            if (result == null) return WorkbenchResult.Failure("invalid", "authoring_result_missing", "", "The authoring operation returned no result.");
            if (!result.IsReady) return ConvertFailure(result);
            try
            {
                object projected = projector == null ? (object)result.value : projector(result.value);
                return WorkbenchResult.Ok(key == null ? projected : new Dictionary<string, object> { { key, projected } });
            }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "authoring_response_invalid", "", "The authoring response could not be produced: " + exception.GetType().Name); }
        }

        private static WorkbenchResult ConvertFailure<T>(AuthoringResult<T> result)
        {
            AuthoringDiagnostic diagnostic = result.diagnostics == null ? null : result.diagnostics.FirstOrDefault();
            string status = result.status == AuthoringStatuses.Ready ? "ok" : result.status;
            return WorkbenchResult.Failure(status, diagnostic == null ? "authoring_operation_failed" : diagnostic.code,
                diagnostic == null ? "" : diagnostic.path,
                diagnostic == null ? "The authoring operation could not be completed." : diagnostic.message);
        }

        private static WorkbenchResult ConvertContentFailure(string status, IList<ContentDiagnostic> diagnostics)
        {
            ContentDiagnostic diagnostic = diagnostics == null ? null : diagnostics.FirstOrDefault();
            return WorkbenchResult.Failure(status == ContentStatuses.Missing ? "invalid" : status,
                diagnostic == null ? "authoring_import_inventory_failed" : diagnostic.code,
                diagnostic == null ? "" : diagnostic.path,
                diagnostic == null ? "The configured legacy content inventory could not be read." : diagnostic.message);
        }

        private static Dictionary<string, object> ToStoreInfo(AuthoringStoreInfo info)
        {
            return new Dictionary<string, object>
            {
                { "storeId", info.storeId },
                { "schemaVersion", info.schemaVersion },
                { "storeRevision", info.storeRevision }
            };
        }

        private static Dictionary<string, object> ToSummary(AuthoringDocumentSummary document)
        {
            return new Dictionary<string, object>
            {
                { "documentId", document.documentId },
                { "kind", document.kind },
                { "sourceId", document.sourceId },
                { "logicalPath", document.logicalPath },
                { "revisionHash", document.revisionHash }
            };
        }

        private static Dictionary<string, object> ToSavedDocument(AuthoringDocument document)
        {
            var result = ToSummary(document);
            result["contentHash"] = document.revisionHash;
            return result;
        }

        private static Dictionary<string, object> ToDraftWriteResult(AuthoringDocument document)
        {
            return new Dictionary<string, object>
            {
                { "kind", document.kind },
                { "id", document.documentId },
                { "contentHash", document.revisionHash }
            };
        }

        private static Dictionary<string, object> ToBuildSet(AuthoringBuildSet buildSet)
        {
            return new Dictionary<string, object>
            {
                { "name", buildSet.name },
                { "documentIds", buildSet.documentIds },
                { "revisionHash", buildSet.revisionHash },
                { "updatedUtc", buildSet.updatedUtc }
            };
        }

        private static Dictionary<string, object> ToImportReceipt(AuthoringImportReceipt receipt)
        {
            return new Dictionary<string, object>
            {
                { "operationId", receipt.operationId },
                { "storeId", receipt.storeId },
                { "storeRevision", receipt.storeRevision },
                { "sourceCount", receipt.sourceCount },
                { "receiptHash", receipt.receiptHash },
                { "sources", receipt.sources.Select(source => (object)new Dictionary<string, object>
                    {
                        { "sourcePath", source.sourcePath },
                        { "sourceHash", source.sourceHash },
                        { "documentId", source.documentId },
                        { "sourceId", source.sourceId }
                    }).ToList() }
            };
        }

        private static Dictionary<string, object> ToExportReceipt(AuthoringExportReceipt receipt)
        {
            return new Dictionary<string, object>
            {
                { "storeId", receipt.storeId },
                { "storeRevision", receipt.storeRevision },
                { "snapshotHash", receipt.snapshotHash },
                { "manifestHash", receipt.manifestHash },
                { "directoryPath", receipt.directoryPath },
                { "documentCount", receipt.documentCount },
                { "reusedExistingExport", receipt.reusedExistingExport }
            };
        }

        private static bool TryParseDraft(AuthoringDocument document, string kind, string id,
            out IDictionary<string, object> wrapper)
        {
            wrapper = null;
            if (document == null || document.documentBytes == null || document.documentBytes.Length > MaximumDraftBytes) return false;
            try
            {
                wrapper = JsonWire.AsObject(JsonWire.Deserialize(document.documentBytes));
                int version;
                if (wrapper == null || !JsonWire.TryInt(wrapper, "workbenchSchemaVersion", out version) || version != 1 ||
                    JsonWire.String(wrapper, "kind") != kind || JsonWire.String(wrapper, "id") != id ||
                    JsonWire.AsObject(wrapper.ContainsKey("document") ? wrapper["document"] : null) == null) return false;
                object editorState;
                return !wrapper.TryGetValue("editorState", out editorState) || editorState == null || JsonWire.AsObject(editorState) != null;
            }
            catch { wrapper = null; return false; }
        }

        private static bool TryDraftIdentity(string kind, string id, out string logicalPath, out WorkbenchResult failure)
        {
            logicalPath = string.Empty;
            failure = null;
            if (kind != "topic" && kind != "scenario")
            {
                failure = WorkbenchResult.Failure("invalid", "draft_kind_invalid", "/kind", "kind must be topic or scenario.");
                return false;
            }
            if (!IsSafeDraftId(id))
            {
                failure = WorkbenchResult.Failure("invalid", "draft_id_invalid", "/id", "Draft ID must be a stable path-safe identifier.");
                return false;
            }
            logicalPath = DraftLogicalPath(kind, id);
            return true;
        }

        private static string DraftLogicalPath(string kind, string id)
        { return "Drafts/" + (kind == "topic" ? "topics" : "scenarios") + "/" + id + ".json"; }

        private static bool IsSafeDraftId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiAlphaNumeric(value[0])) return false;
            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAsciiAlphaNumeric(character) && character != '.' && character != '_' && character != '-') return false;
            }
            return value != "." && value != "..";
        }

        private static bool IsAsciiAlphaNumeric(char value)
        { return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9'); }

        private static bool TryExpectedHash(IDictionary<string, object> payload, out string expectedHash, out WorkbenchResult failure)
        {
            expectedHash = string.Empty;
            failure = null;
            if (!payload.TryGetValue("expectedHash", out object raw) || raw == null) return true;
            if (!(raw is string))
            {
                failure = WorkbenchResult.Failure("invalid", "authoring_hash_invalid", "/expectedHash", "expectedHash must be a string or null.");
                return false;
            }
            expectedHash = (string)raw;
            if (expectedHash.Length > 0 && !AuthoringRepository.IsSha256(expectedHash))
            {
                failure = WorkbenchResult.Failure("invalid", "authoring_hash_invalid", "/expectedHash", "expectedHash must be empty or a lowercase SHA-256.");
                return false;
            }
            return true;
        }

        private static bool TryStringList(IDictionary<string, object> payload, string name, out List<string> values)
        {
            values = new List<string>();
            if (!payload.TryGetValue(name, out object raw)) return false;
            IEnumerable items = JsonWire.AsArray(raw);
            if (items == null || raw is string) return false;
            foreach (object item in items)
            {
                if (!(item is string)) return false;
                values.Add((string)item);
            }
            return true;
        }

        private static string OperationId(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return "workbench_" + ToHex(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))).Substring(0, 48);
        }

        private static string ImportedDocumentId(string kind, string logicalPath)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(kind + "\n" + logicalPath);
                return "src_" + ToHex(hash.ComputeHash(bytes)).Substring(0, 48);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal static bool WorkspaceIsSafe(WorkbenchEnvironment environment, out string error)
        {
            error = string.Empty;
            string[] paths = { environment.WorkbenchRoot, environment.AuthoringWorkspaceRoot, environment.AuthoringDatabasePath };
            foreach (string path in paths)
            {
                if (AuthoringPathSafety.ContainsReparsePoint(path))
                {
                    error = "The configured authoring workspace contains a symbolic link or junction and was not accessed.";
                    return false;
                }
            }
            return true;
        }
    }
}
