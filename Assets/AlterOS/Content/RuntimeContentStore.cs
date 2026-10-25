using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Scenarios;
using AlterOS.Furniture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    [Serializable]
    public sealed class ContentRuntimeConfiguration
    {
        public string databasePath = string.Empty;
        public string expectedStoreId = string.Empty;
        public string expectedReleaseId = string.Empty;
        public string expectedBuildManifestHash = string.Empty;
        public string semanticProfileId = string.Empty;
    }

    public sealed class RuntimeContentDocument
    {
        public readonly string kind;
        public readonly string relativePath;
        public readonly string sourceId;
        public readonly string sourceHash;
        public readonly string contentId;
        public readonly string contentRevision;
        public readonly byte[] bytes;
        public readonly string json;

        internal RuntimeContentDocument(LegacySourceRecord source)
        {
            kind = source.kind;
            relativePath = source.relativePath;
            sourceId = source.sourceId;
            sourceHash = source.sourceHash;
            contentId = source.contentId;
            contentRevision = source.contentRevision;
            bytes = (byte[])source.sourceBytes.Clone();
            json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
    }

    /// <summary>Opens one exact, read-only SQL content release and its source/hash coverage.</summary>
    public sealed class RuntimeContentStore : IContentReleaseReader
    {
        private readonly Dictionary<string, RuntimeContentDocument> documents;
        private readonly FurnitureCatalogueReleaseMetadata furnitureCatalogue;
        private readonly ContentReleaseDependency[] furnitureCatalogueDependencies;
        private readonly object executionReleaseGate = new object();
        private ContentHostVocabulary executionReleaseVocabulary;
        private ContentResult<ResolvedContentRelease> executionRelease;
        private static string shippedDatabasePath = string.Empty;

        public static RuntimeContentStore Current { get; private set; }
        public ContentRepository Repository { get; private set; }
        public ContentRelease Release { get; private set; }
        public ContentBuildManifest BuildManifest { get; private set; }
        public ContentSemanticSnapshot SemanticIndex { get; private set; }

        private RuntimeContentStore(ContentRepository repository, ContentRelease release,
            ContentBuildManifest manifest, ContentSemanticSnapshot semanticIndex,
            IEnumerable<RuntimeContentDocument> contentDocuments)
        {
            Repository = repository;
            Release = release;
            BuildManifest = manifest.DetachedCopy();
            furnitureCatalogue = ContentBuildManifest.CloneFurnitureCatalogueMetadata(manifest.furnitureCatalogue);
            furnitureCatalogueDependencies = (release.dependencies ?? new List<ContentReleaseDependency>())
                .Where(item => item != null && item.kind == "furniture")
                .Select(item => new ContentReleaseDependency
                {
                    kind = item.kind, id = item.id, revision = item.revision, store = item.store, expectedHash = item.expectedHash
                }).ToArray();
            SemanticIndex = semanticIndex;
            documents = new Dictionary<string, RuntimeContentDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (RuntimeContentDocument document in contentDocuments)
            {
                string path = NormalizeAlias(document.relativePath);
                documents.Add(path, document);
                documents.Add("Dialogue/" + path, document);
            }
        }

        public static ContentResult<RuntimeContentStore> OpenTrustedRelease(ContentRuntimeConfiguration configuration)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.databasePath))
                return Failure(ContentStatuses.Invalid, "runtime_content_configuration_invalid", "A host-selected content database path is required.");
            string fullPath;
            try { fullPath = Path.GetFullPath(configuration.databasePath); }
            catch (Exception exception) { return Failure(ContentStatuses.Invalid, "runtime_content_path_invalid", exception.Message); }

            ContentResult<ContentRepository> opened = ContentRepository.Open(fullPath, ContentStoreMode.ReadOnly,
                string.IsNullOrWhiteSpace(configuration.expectedStoreId) ? null : configuration.expectedStoreId);
            if (!opened.IsReady) return Failure(opened.status, FirstCode(opened.diagnostics, "content_store_unavailable"), FirstMessage(opened.diagnostics));
            ContentRepository repository = opened.value;

            ContentResult<ContentBuildManifest> manifestResult = repository.ReadBuildManifest();
            if (!manifestResult.IsReady) return Failure(manifestResult.status, FirstCode(manifestResult.diagnostics, "content_build_manifest_unavailable"),
                FirstMessage(manifestResult.diagnostics));
            ContentBuildManifest manifest = manifestResult.value;
            if (!string.IsNullOrWhiteSpace(configuration.expectedBuildManifestHash) &&
                !string.Equals(configuration.expectedBuildManifestHash, manifest.Hash, StringComparison.Ordinal))
                return Failure(ContentStatuses.Incompatible, "runtime_content_manifest_pin_mismatch", "The shipped content manifest does not match the host-pinned hash.");
            if (!string.IsNullOrWhiteSpace(configuration.expectedReleaseId) &&
                !string.Equals(configuration.expectedReleaseId, manifest.releaseId, StringComparison.Ordinal))
                return Failure(ContentStatuses.Incompatible, "runtime_content_release_pin_mismatch", "The shipped content release does not match the host-pinned release ID.");

            ContentResult<ContentRelease> releaseResult = repository.ReadRelease(manifest.releaseId);
            if (!releaseResult.IsReady) return Failure(releaseResult.status, FirstCode(releaseResult.diagnostics, "content_release_unavailable"),
                FirstMessage(releaseResult.diagnostics));
            ContentRelease release = releaseResult.value;
            string releaseError;
            if (!ValidateReleaseHeader(release, manifest, repository.StoreId, out releaseError))
                return Failure(ContentStatuses.Incompatible, "runtime_content_release_invalid", releaseError);

            ContentResult<List<LegacySourceRecord>> sourceResult = repository.ReadLegacySources();
            if (!sourceResult.IsReady) return Failure(sourceResult.status, FirstCode(sourceResult.diagnostics, "content_sources_unavailable"),
                FirstMessage(sourceResult.diagnostics));
            List<LegacySourceRecord> sources = sourceResult.value;
            if (!MatchSourceClosure(manifest, sources, out releaseError))
                return Failure(ContentStatuses.Incompatible, "runtime_content_source_coverage_invalid", releaseError);

            string profileId = string.IsNullOrWhiteSpace(configuration.semanticProfileId)
                ? manifest.semanticProfileId : configuration.semanticProfileId;
            ContentResult<ContentSemanticSnapshot> semanticResult = ContentSemanticIndex.Load(repository, release, profileId);
            if (!semanticResult.IsReady) return Failure(semanticResult.status, FirstCode(semanticResult.diagnostics, "content_semantic_index_unavailable"),
                FirstMessage(semanticResult.diagnostics));

            try
            {
                var documents = sources.Select(item => new RuntimeContentDocument(item)).ToArray();
                return ContentResult<RuntimeContentStore>.Ready(new RuntimeContentStore(repository, release, manifest,
                    semanticResult.value, documents));
            }
            catch (Exception exception) { return Failure(ContentStatuses.Invalid, "runtime_content_document_invalid", exception.Message); }
        }

        public static ContentResult<RuntimeContentStore> OpenShippedRelease(string streamingAssetsRoot)
        {
            if (string.IsNullOrWhiteSpace(streamingAssetsRoot))
                return Failure(ContentStatuses.Invalid, "runtime_content_root_invalid", "A host-selected StreamingAssets root is required.");
            string databasePath = Path.Combine(streamingAssetsRoot, "Content", "content.sqlite3");
            string fullPath;
            try { fullPath = Path.GetFullPath(databasePath); }
            catch (Exception exception) { return Failure(ContentStatuses.Invalid, "runtime_content_path_invalid", exception.Message); }
            if (Current != null && string.Equals(shippedDatabasePath, fullPath, StringComparison.OrdinalIgnoreCase))
                return ContentResult<RuntimeContentStore>.Ready(Current);

            ContentResult<RuntimeContentStore> opened = OpenTrustedRelease(new ContentRuntimeConfiguration { databasePath = fullPath });
            if (opened.IsReady)
            {
                shippedDatabasePath = fullPath;
                Current = opened.value;
            }
            return opened;
        }

        public static void ResetShippedCache()
        {
            Current = null;
            shippedDatabasePath = string.Empty;
        }

        public ContentResult<RuntimeContentDocument> ReadDocument(string logicalPath)
        {
            string normalized;
            if (!TryNormalizeAlias(logicalPath, out normalized))
                return ContentResult<RuntimeContentDocument>.Failure(ContentStatuses.Invalid, "runtime_content_alias_invalid", logicalPath,
                    "A safe dialogue-relative content path is required.");
            RuntimeContentDocument document;
            return documents.TryGetValue(normalized, out document)
                ? ContentResult<RuntimeContentDocument>.Ready(document)
                : ContentResult<RuntimeContentDocument>.Failure(ContentStatuses.Missing, "runtime_content_document_missing", normalized,
                    "The pinned SQL release does not contain this authored content path.");
        }

        public ContentResult<ContentSemanticSnapshot> ReadAnchors()
        { return ContentResult<ContentSemanticSnapshot>.Ready(SemanticIndex); }

        public ContentResult<TopicRecord> ReadTopic(ContentRef reference)
        {
            ContentRef pinned;
            string code, message;
            return TryResolvePinnedReference(reference, "topic", out pinned, out code, out message)
                ? Repository.ReadTopic(pinned)
                : ContentResult<TopicRecord>.Failure(ContentStatuses.Incompatible, code,
                    reference == null ? string.Empty : reference.id, message);
        }

        public ContentResult<ContentExecutionPlan> PrepareTopicEntry(ContentRef topicReference, string entryId,
            IDictionary<string, FlowValue> inputs, ScenarioRuntimeSnapshot context, ContentHostVocabulary hostVocabulary,
            string displayLanguage = "en", string speechLanguage = "ja")
        {
            if (hostVocabulary == null || context == null || string.IsNullOrWhiteSpace(entryId))
                return ContentResult<ContentExecutionPlan>.Failure(ContentStatuses.Invalid,
                    "runtime_topic_entry_context_invalid", entryId, "An exact Topic entry, host vocabulary and runtime snapshot are required.");

            ContentResult<TopicRecord> topicResult = ReadTopic(topicReference);
            if (!topicResult.IsReady)
                return ContentResult<ContentExecutionPlan>.Failure(topicResult.status, FirstCode(topicResult.diagnostics,
                    "runtime_topic_unavailable"), topicReference == null ? string.Empty : topicReference.id,
                    FirstMessage(topicResult.diagnostics));
            TopicEntryRecord[] entries = (topicResult.value.entries ?? new List<TopicEntryRecord>())
                .Where(item => item != null && item.id == entryId).ToArray();
            if (entries.Length != 1)
                return ContentResult<ContentExecutionPlan>.Failure(entries.Length == 0 ? ContentStatuses.Missing : ContentStatuses.Incompatible,
                    entries.Length == 0 ? "runtime_topic_entry_missing" : "runtime_topic_entry_ambiguous", entryId,
                    "The exact Topic revision must contain one matching entry ID.");

            ContentRef flowReference;
            string code, message;
            if (!TryResolvePinnedReference(entries[0].flowRef, "flow", out flowReference, out code, out message))
                return ContentResult<ContentExecutionPlan>.Failure(ContentStatuses.Incompatible, "runtime_topic_flow_unpinned", entryId, message);
            ContentResult<FlowRecord> flowResult = ReadFlow(flowReference);
            if (!flowResult.IsReady)
                return ContentResult<ContentExecutionPlan>.Failure(flowResult.status, FirstCode(flowResult.diagnostics,
                    "runtime_topic_flow_unavailable"), flowReference.id, FirstMessage(flowResult.diagnostics));
            if (!string.IsNullOrEmpty(entries[0].consumptionNodeId) &&
                !(flowResult.value.steps ?? new List<StepRecord>()).Any(item => item != null && item.id == entries[0].consumptionNodeId))
                return ContentResult<ContentExecutionPlan>.Failure(ContentStatuses.Incompatible,
                    "runtime_topic_consumption_node_missing", entryId,
                    "The Topic entry checkpoint is outside its exact pinned Scenario Flow revision.");

            ContentResult<ResolvedContentRelease> resolved = ResolveExecutionRelease(hostVocabulary);
            if (!resolved.IsReady)
                return ContentResult<ContentExecutionPlan>.Failure(resolved.status, FirstCode(resolved.diagnostics,
                    "runtime_content_release_unavailable"), Release.releaseId, FirstMessage(resolved.diagnostics));
            TopicRecord projectedTopic = resolved.value.package.topics.FirstOrDefault(item => item != null &&
                item.id == topicResult.value.id && item.revision == topicResult.value.revision);
            if (!SameTopic(topicResult.value, projectedTopic))
                return ContentResult<ContentExecutionPlan>.Failure(ContentStatuses.Incompatible,
                    "runtime_topic_projection_mismatch", topicReference.id,
                    "The SQL Topic rows differ from the exact source projection in the selected release.");
            return ContentFlowAdapter.Prepare(resolved.value, flowReference, inputs, context, displayLanguage, speechLanguage);
        }

        private static bool SameTopic(TopicRecord left, TopicRecord right)
        {
            if (left == null || right == null || left.id != right.id || left.revision != right.revision ||
                left.semanticHash != right.semanticHash || left.routeId != right.routeId ||
                left.defaultReplayPolicy != right.defaultReplayPolicy || left.cooldownSeconds != right.cooldownSeconds ||
                !SameReference(left.titleTextRef, right.titleTextRef) || !SameReference(left.promptTextRef, right.promptTextRef)) return false;
            TopicEntryRecord[] leftEntries = (left.entries ?? new List<TopicEntryRecord>()).Where(item => item != null)
                .OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
            TopicEntryRecord[] rightEntries = (right.entries ?? new List<TopicEntryRecord>()).Where(item => item != null)
                .OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
            return leftEntries.Length == rightEntries.Length && leftEntries.Zip(rightEntries, SameTopicEntry).All(item => item);
        }

        private static bool SameTopicEntry(TopicEntryRecord left, TopicEntryRecord right)
        {
            return left != null && right != null && left.id == right.id && SameReference(left.flowRef, right.flowRef) &&
                left.priority == right.priority && left.baseWeight == right.baseWeight &&
                left.conditionJson == right.conditionJson && left.modifiersJson == right.modifiersJson &&
                left.channelsJson == right.channelsJson && left.consumptionNodeId == right.consumptionNodeId;
        }

        private static bool SameReference(ContentRef left, ContentRef right)
        {
            return left != null && right != null && left.store == right.store && left.id == right.id &&
                left.revision == right.revision && (string.IsNullOrEmpty(left.expectedHash) ||
                    string.IsNullOrEmpty(right.expectedHash) || left.expectedHash == right.expectedHash);
        }

        public ContentResult<ResolvedContentRelease> WarmExecutionRelease(ContentHostVocabulary hostVocabulary)
        { return ResolveExecutionRelease(hostVocabulary); }

        private ContentResult<ResolvedContentRelease> ResolveExecutionRelease(ContentHostVocabulary hostVocabulary)
        {
            lock (executionReleaseGate)
            {
                if (ReferenceEquals(executionReleaseVocabulary, hostVocabulary) && executionRelease != null)
                    return executionRelease;
                executionRelease = BuildExecutionRelease(hostVocabulary);
                executionReleaseVocabulary = hostVocabulary;
                return executionRelease;
            }
        }

        private ContentResult<ResolvedContentRelease> BuildExecutionRelease(ContentHostVocabulary hostVocabulary)
        {
            ContentResult<ContentPackage> package = Repository.ReadCompiledPackageForRelease(Release,
                BuildManifest.packageId, BuildManifest.sourceManifestHash);
            if (!package.IsReady)
                return ContentResult<ResolvedContentRelease>.Failure(package.status,
                    FirstCode(package.diagnostics, "runtime_compiled_release_unavailable"), Release.releaseId,
                    FirstMessage(package.diagnostics));
            return ContentFlowAdapter.Resolve(package.value, Release, hostVocabulary);
        }

        public ContentResult<DialogueRecord> ReadDialogue(ContentRef reference)
        {
            ContentRef pinned;
            string code, message;
            return TryResolvePinnedReference(reference, "dialogue", out pinned, out code, out message)
                ? Repository.ReadDialogue(pinned)
                : ContentResult<DialogueRecord>.Failure(ContentStatuses.Incompatible, code, reference == null ? "" : reference.id, message);
        }

        public ContentResult<ActionRecord> ReadAction(ContentRef reference)
        {
            ContentRef pinned;
            string code, message;
            return TryResolvePinnedReference(reference, "action", out pinned, out code, out message)
                ? Repository.ReadAction(pinned)
                : ContentResult<ActionRecord>.Failure(ContentStatuses.Incompatible, code, reference == null ? "" : reference.id, message);
        }

        public ContentResult<ItemRecord> ReadItem(ContentRef reference)
        {
            ContentRef pinned;
            string code, message;
            return TryResolvePinnedReference(reference, "item", out pinned, out code, out message)
                ? Repository.ReadItem(pinned)
                : ContentResult<ItemRecord>.Failure(ContentStatuses.Incompatible, code, reference == null ? "" : reference.id, message);
        }

        public ContentResult<FlowRecord> ReadFlowById(string flowId)
        {
            if (string.IsNullOrWhiteSpace(flowId) || flowId.Length > 128)
                return ContentResult<FlowRecord>.Failure(ContentStatuses.Invalid, "runtime_flow_id_invalid", flowId,
                    "A valid Flow ID is required.");
            ContentReleaseDependency[] matches = (Release.dependencies ?? new List<ContentReleaseDependency>())
                .Where(item => item.kind == "flow" && item.id == flowId && item.store == Repository.StoreId).ToArray();
            if (matches.Length != 1)
                return ContentResult<FlowRecord>.Failure(matches.Length == 0 ? ContentStatuses.Missing : ContentStatuses.Incompatible,
                    matches.Length == 0 ? "runtime_flow_unpinned" : "runtime_flow_ambiguous", flowId,
                    "The selected release must contain exactly one revision of this Flow ID.");
            ContentReleaseDependency dependency = matches[0];
            return ReadPinnedFlow(new ContentRef(dependency.store, dependency.id, dependency.revision, dependency.expectedHash));
        }

        public ContentResult<FlowRecord> ReadFlow(ContentRef reference)
        {
            ContentRef pinned;
            string code, message;
            return TryResolvePinnedReference(reference, "flow", out pinned, out code, out message)
                ? ReadPinnedFlow(pinned)
                : ContentResult<FlowRecord>.Failure(ContentStatuses.Incompatible, code, reference == null ? "" : reference.id, message);
        }

        private ContentResult<FlowRecord> ReadPinnedFlow(ContentRef pinned)
        {
            ContentResult<FlowRecord> result = Repository.ReadFlow(pinned);
            if (!result.IsReady) return result;
            foreach (StepRecord step in result.value.steps ?? new List<StepRecord>())
            {
                if (step.actionRef != null && !ResolveFlowStepReference(step.actionRef, "action"))
                    return ContentResult<FlowRecord>.Failure(ContentStatuses.Incompatible, "runtime_flow_action_unpinned", pinned.id,
                        "A Flow Action reference is outside, hash-mismatched or ambiguous in the selected release closure.");
                if (step.dialogueRef != null && !ResolveFlowStepReference(step.dialogueRef, "dialogue"))
                    return ContentResult<FlowRecord>.Failure(ContentStatuses.Incompatible, "runtime_flow_dialogue_unpinned", pinned.id,
                        "A Flow Dialogue reference is outside, hash-mismatched or ambiguous in the selected release closure.");
            }
            return result;
        }

        private bool ResolveFlowStepReference(ContentRef reference, string kind)
        {
            ContentReleaseDependency[] matches = (Release.dependencies ?? new List<ContentReleaseDependency>())
                .Where(item => item.kind == kind && item.id == reference.id && item.revision == reference.revision &&
                    item.store == reference.store).ToArray();
            if (matches.Length != 1 || !string.IsNullOrEmpty(reference.expectedHash) &&
                reference.expectedHash != matches[0].expectedHash) return false;
            reference.expectedHash = matches[0].expectedHash;
            return true;
        }

        public ContentResult<FurnitureDefinitionRecord> ReadFurnitureDefinition(ContentRef reference)
        {
            ContentRef pinned;
            string code, message;
            return TryResolvePinnedReference(reference, "furniture", out pinned, out code, out message)
                ? Repository.ReadFurnitureDefinition(pinned)
                : ContentResult<FurnitureDefinitionRecord>.Failure(ContentStatuses.Incompatible, code, reference == null ? "" : reference.id, message);
        }

        public ContentResult<CompiledFurnitureCatalogue> ReadFurnitureCatalogue()
        {
            try { return ReadVerifiedFurnitureCatalogue(); }
            catch (Exception exception)
            {
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_invalid", "furniture.catalogue", exception.Message);
            }
        }

        private ContentResult<CompiledFurnitureCatalogue> ReadVerifiedFurnitureCatalogue()
        {
            FurnitureCatalogueReleaseMetadata metadata = furnitureCatalogue;
            if (metadata == null)
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Unavailable,
                    "furniture_catalogue_settings_unavailable", "furniture.catalogue",
                    "This pinned release does not contain compiled Furniture catalogue settings.");

            if (metadata.settings == null || metadata.definitions == null || metadata.definitions.Count == 0 ||
                string.IsNullOrEmpty(metadata.fingerprint))
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_metadata_invalid", "furniture.catalogue",
                    "The privately pinned Furniture catalogue metadata is incomplete.");

            string fingerprint = ContentBuildManifest.ComputeFurnitureCatalogueFingerprint(metadata.settings, metadata.definitions);
            if (fingerprint != metadata.fingerprint)
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Incompatible,
                    "furniture_catalogue_fingerprint_invalid", "furniture.catalogue",
                    "The compiled Furniture catalogue fingerprint does not match its exact settings and references.");

            ContentReleaseDependency[] pinned = (furnitureCatalogueDependencies ?? new ContentReleaseDependency[0])
                .OrderBy(item => item.id, StringComparer.Ordinal).ThenBy(item => item.revision, StringComparer.Ordinal).ToArray();
            ContentRef[] declared = (metadata.definitions ?? new List<ContentRef>())
                .OrderBy(item => item == null ? string.Empty : item.id, StringComparer.Ordinal)
                .ThenBy(item => item == null ? string.Empty : item.revision, StringComparer.Ordinal).ToArray();
            if (pinned.Length == 0 || pinned.Length != declared.Length || pinned.Where((item, index) => declared[index] == null ||
                item.store != declared[index].store || item.id != declared[index].id || item.revision != declared[index].revision ||
                item.expectedHash != declared[index].expectedHash).Any())
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Incompatible,
                    "furniture_catalogue_release_refs_invalid", "furniture.catalogue",
                    "The catalogue references do not equal the exact Furniture dependency set of this pinned release.");

            FurnitureCatalogueDocument document;
            try { document = JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(JsonConvert.SerializeObject(metadata.settings)); }
            catch (Exception exception)
            {
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_settings_invalid", "furniture.catalogue", exception.Message);
            }
            if (document == null)
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_settings_invalid", "furniture.catalogue", "The compiled settings envelope could not be copied.");
            document.definitions = new List<FurnitureDefinition>();

            foreach (ContentRef reference in declared)
            {
                ContentReleaseDependency exact = pinned.SingleOrDefault(item => item.store == reference.store && item.id == reference.id &&
                    item.revision == reference.revision && item.expectedHash == reference.expectedHash);
                if (exact == null || reference.store != Repository.StoreId)
                    return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Incompatible,
                        "runtime_content_ref_unpinned", reference.id,
                        "The exact Furniture reference is outside the privately pinned release closure.");
                var pinnedReference = new ContentRef(exact.store, exact.id, exact.revision, exact.expectedHash);
                ContentResult<FurnitureDefinitionRecord> result = Repository.ReadFurnitureDefinition(pinnedReference);
                if (!result.IsReady)
                    return ContentResult<CompiledFurnitureCatalogue>.Failure(result.status,
                        result.diagnostics.Count == 0 ? "furniture_revision_unavailable" : result.diagnostics[0].code,
                        reference.id, result.diagnostics.Count == 0 ? "The exact Furniture revision could not be read." : result.diagnostics[0].message);
                try
                {
                    FurnitureDefinition definition = JsonConvert.DeserializeObject<FurnitureDefinition>(result.value.definitionJson);
                    if (definition == null || definition.definitionId != result.value.id)
                        return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Incompatible,
                            "furniture_definition_identity_invalid", reference.id,
                            "The decoded SQL definition identity does not match its pinned Furniture row.");
                    document.definitions.Add(definition);
                }
                catch (Exception exception)
                {
                    return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                        "furniture_definition_decode_failed", reference.id, exception.Message);
                }
            }

            foreach (FurnitureDiagnostic diagnostic in FurnitureCatalogueValidator.Validate(document))
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    diagnostic.code, "furniture.catalogue." + diagnostic.path, diagnostic.message);

            try
            {
                FurnitureCatalogueDocument detached = JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(JsonConvert.SerializeObject(document));
                return ContentResult<CompiledFurnitureCatalogue>.Ready(new CompiledFurnitureCatalogue(detached, fingerprint));
            }
            catch (Exception exception)
            {
                return ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_copy_failed", "furniture.catalogue", exception.Message);
            }
        }

        private bool TryResolvePinnedReference(ContentRef reference, string kind, out ContentRef pinned,
            out string errorCode, out string errorMessage)
        {
            pinned = null;
            errorCode = "runtime_content_ref_invalid";
            errorMessage = "A store-local exact ID and revision are required.";
            if (reference == null || reference.store != Repository.StoreId || string.IsNullOrWhiteSpace(reference.id) ||
                string.IsNullOrWhiteSpace(reference.revision))
                return false;
            ContentReleaseDependency[] matches = Release.dependencies.Where(item => item.kind == kind && item.id == reference.id &&
                item.revision == reference.revision && item.store == reference.store).ToArray();
            if (matches.Length != 1 || !string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != matches[0].expectedHash)
            {
                errorCode = "runtime_content_ref_unpinned";
                errorMessage = "The exact reference is outside this pinned release or its expected hash differs.";
                return false;
            }
            pinned = new ContentRef(reference.store, reference.id, reference.revision, matches[0].expectedHash);
            errorCode = errorMessage = string.Empty;
            return true;
        }

        public RuntimeContentDocument[] ReadDocuments(string sourceKind, string pathPrefix = null)
        {
            return documents.Values.Distinct().Where(item => item.kind == sourceKind &&
                (string.IsNullOrEmpty(pathPrefix) || item.relativePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.relativePath, StringComparer.Ordinal).ToArray();
        }

        private static bool ValidateReleaseHeader(ContentRelease release, ContentBuildManifest manifest,
            string storeId, out string error)
        {
            error = string.Empty;
            try
            {
                JsonObject header = ContentJson.Parse(release.manifestJson) as JsonObject;
                if (header == null || header.Count != 7 || (string)header["releaseId"] != release.releaseId ||
                    (string)header["storeId"] != storeId || (string)header["sourceManifestHash"] != manifest.sourceManifestHash ||
                    (string)header["graphFingerprint"] != release.graphFingerprint ||
                    (string)header["indexFingerprint"] != release.indexFingerprint ||
                    (int)header["dependencyCount"] != release.dependencies.Count ||
                    manifest.releaseId != release.releaseId || manifest.storeId != storeId)
                { error = "The immutable release header does not match the build manifest and stored release rows."; return false; }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static bool MatchSourceClosure(ContentBuildManifest manifest, IList<LegacySourceRecord> sources, out string error)
        {
            error = string.Empty;
            if (sources == null || sources.Count != manifest.sources.Count)
            { error = "The SQL source rows do not match the exact build manifest source count."; return false; }
            foreach (ContentBuildSource entry in manifest.sources)
            {
                LegacySourceRecord source = sources.FirstOrDefault(item => item.kind == entry.kind &&
                    NormalizeAlias(item.relativePath) == NormalizeAlias(entry.path));
                if (source == null || source.sourceId != entry.sourceId || source.sourceHash != entry.sourceHash || source.payloadHash != entry.payloadHash ||
                    source.contentId != entry.contentId || source.contentRevision != entry.contentRevision ||
                    source.sourceBytes == null || source.sourceBytes.Length == 0)
                { error = "A SQL source row differs from its exact release manifest mapping: " + entry.path; return false; }
            }
            return true;
        }

        private static bool TryNormalizeAlias(string path, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return false;
            string value = path.Replace('\\', '/').TrimStart('/');
            if (value.StartsWith("Dialogue/", StringComparison.OrdinalIgnoreCase)) value = value.Substring("Dialogue/".Length);
            if (value.Length == 0 || value.Length > 512 || value.Contains(":") || value.StartsWith("/", StringComparison.Ordinal)) return false;
            string[] segments = value.Split('/');
            if (segments.Any(item => item.Length == 0 || item == "." || item == "..")) return false;
            normalized = string.Join("/", segments);
            return true;
        }

        private static string NormalizeAlias(string path)
        {
            string value;
            if (!TryNormalizeAlias(path, out value)) throw new InvalidDataException("A stored content path is not a safe dialogue-relative alias.");
            return value;
        }

        private static string FirstCode(IList<ContentDiagnostic> diagnostics, string fallback)
        { return diagnostics == null || diagnostics.Count == 0 ? fallback : diagnostics[0].code; }
        private static string FirstMessage(IList<ContentDiagnostic> diagnostics)
        { return diagnostics == null || diagnostics.Count == 0 ? "The requested content release is unavailable." : diagnostics[0].message; }
        private static ContentResult<RuntimeContentStore> Failure(string status, string code, string message)
        { return ContentResult<RuntimeContentStore>.Failure(status, code, "runtime_content", message); }
    }
}
