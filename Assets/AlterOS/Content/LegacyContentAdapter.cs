using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Dialogue;
using AlterOS.Activities;
using AlterOS.Scenarios;
using AlterOS.Topics;
using AlterOS.Stats;
using AlterOS.Furniture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    public static class ContentSourceKinds
    {
        public const string GenericDialogue = "generic_dialogue";
        public const string TopicDefinition = "topic_definition";
        public const string ScenarioDefinition = "scenario_definition";
        public const string DialogueDefinition = "dialogue_definition";
        public const string FurnitureDefinition = "furniture_definition";
        public const string FurnitureCatalogueSettings = "furniture_catalogue_settings";
        public const string ItemDefinition = "item_definition";
        public const string ActionDefinition = "action_definition";
        public const string LegacySequence = "legacy_sequence";
        public const string AmbientPool = "ambient_pool";
        public const string ActivityScenarioLibrary = "activity_scenario_library";
        public const string ActivityGreetingMenu = "activity_greeting_menu";
        public const string ConversationCoverage = "conversation_coverage";
        public const string TopicMigrationMap = "topic_migration_map";
        public const string OnboardingSteps = "onboarding_steps";
    }

    [DataContract]
    public sealed class ContentSourceManifest
    {
        [DataMember(IsRequired = true)] public string packageId = string.Empty;
        [DataMember(IsRequired = true)] public string storeId = string.Empty;
        [DataMember(IsRequired = true)] public string sourceRoot = string.Empty;
        [DataMember(IsRequired = true)] public List<ContentSourceFile> sources = new List<ContentSourceFile>();
    }

    [DataContract]
    public sealed class ContentSourceFile
    {
        [DataMember(IsRequired = true)] public string kind = string.Empty;
        [DataMember(IsRequired = true)] public string relativePath = string.Empty;
        [DataMember(IsRequired = true)] public string sha256 = string.Empty;
    }

    /// <summary>One immutable in-memory source record for the shared content compiler.</summary>
    public sealed class ContentSourceDocument
    {
        private readonly byte[] _utf8Bytes;

        public string kind { get; private set; }
        public string logicalPath { get; private set; }
        public string sourceId { get; private set; }
        public string hash { get; private set; }
        public byte[] utf8Bytes { get { return (byte[])_utf8Bytes.Clone(); } }

        public ContentSourceDocument(string kind, string logicalPath, string sourceId, string hash, byte[] utf8Bytes)
        {
            this.kind = kind ?? string.Empty;
            this.logicalPath = logicalPath ?? string.Empty;
            this.sourceId = sourceId ?? string.Empty;
            this.hash = hash ?? string.Empty;
            _utf8Bytes = utf8Bytes == null ? new byte[0] : (byte[])utf8Bytes.Clone();
        }
    }

    /// <summary>Imports only files named by a host-created manifest under its trusted root.</summary>
    public static class LegacyContentAdapter
    {
        private const int MaximumSources = 10000;
        private const int MaximumSourceBytes = 4 * 1024 * 1024;

        public static ContentResult<ContentPackage> Import(ContentSourceManifest manifest)
        {
            string root;
            string validationError;
            if (!ValidateManifest(manifest, out root, out validationError))
                return Failure("source_manifest_invalid", "", validationError);

            var sources = manifest.sources.OrderBy(item => item.kind, StringComparer.Ordinal)
                .ThenBy(item => NormalizeRelativePath(item.relativePath), StringComparer.Ordinal).ToList();
            var documents = new List<ContentSourceDocument>(sources.Count);

            foreach (ContentSourceFile source in sources)
            {
                string relativePath = NormalizeRelativePath(source.relativePath);
                string fullPath;
                if (!ResolveTrustedFile(root, relativePath, out fullPath, out validationError))
                    return Failure("source_path_invalid", relativePath, validationError);

                byte[] bytes;
                try { bytes = File.ReadAllBytes(fullPath); }
                catch (Exception exception) { return Failure("source_read_failed", relativePath, exception.Message); }
                if (bytes.Length == 0 || bytes.Length > MaximumSourceBytes)
                    return Failure("source_size_invalid", relativePath, "The named source is empty or exceeds the four-megabyte import bound.");
                string sourceHash = Hash(bytes);
                if (!string.Equals(sourceHash, (source.sha256 ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal))
                    return Failure("source_hash_mismatch", relativePath, "The source bytes do not match the manifest SHA-256.");
                documents.Add(new ContentSourceDocument(source.kind, relativePath, string.Empty, sourceHash, bytes));
            }

            return ImportDocuments(manifest.packageId, manifest.storeId, documents);
        }

        public static ContentResult<ContentPackage> ImportDocuments(string packageId, string storeId,
            IReadOnlyList<ContentSourceDocument> documents)
        { return ImportDocumentsWithSequenceBuilder(packageId, storeId, documents, ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder()); }

        public static ContentResult<ContentPackage> ImportDocumentsWithSequenceBuilder(string packageId, string storeId,
            IReadOnlyList<ContentSourceDocument> documents, ActivitySequenceBuilder sequenceBuilder)
        {
            Guid parsedStoreId;
            if (!IsIdentifier(packageId) || !Guid.TryParse(storeId, out parsedStoreId) || documents == null ||
                documents.Count == 0 || documents.Count > MaximumSources)
                return Failure("source_inventory_invalid", "", "A bounded package ID, store GUID and 1..10000 typed source documents are required.");
            if (documents.Count(item => item != null && item.kind == ContentSourceKinds.FurnitureCatalogueSettings) > 1)
                return Failure("furniture_catalogue_settings_duplicate", "furniture.catalogue", "Only one Furniture catalogue settings document may enter a package.");

            storeId = parsedStoreId.ToString("D");
            var package = new ContentPackage { packageId = packageId, storeId = storeId };
            List<ContentSourceDocument> sources = documents.OrderBy(item => item.kind, StringComparer.Ordinal)
                .ThenBy(item => NormalizeRelativePath(item.logicalPath), StringComparer.Ordinal).ToList();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceIdentities = new HashSet<string>(StringComparer.Ordinal);
            var manifestText = new StringBuilder();

            foreach (ContentSourceDocument source in sources)
            {
                string relativePath = source == null ? string.Empty : NormalizeRelativePath(source.logicalPath);
                if (source == null || !IsSupportedSourceKind(source.kind) ||
                    !IsSafeRelativePath(relativePath) || !paths.Add(relativePath))
                    return Failure("source_document_invalid", relativePath, "Each in-memory source needs a supported kind and unique safe logical path.");

                byte[] bytes = source.utf8Bytes;
                if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumSourceBytes)
                    return Failure("source_size_invalid", relativePath, "The source is empty or exceeds the four-megabyte import bound.");
                string sourceHash = Hash(bytes);
                if (!string.Equals(sourceHash, source.hash, StringComparison.Ordinal))
                    return Failure("source_hash_mismatch", relativePath, "The immutable source bytes do not match their SHA-256.");

                string json;
                try { json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
                catch (Exception exception) { return Failure("source_encoding_invalid", relativePath, exception.Message); }
                JsonValue document;
                try { document = ContentJson.Parse(json); }
                catch (Exception exception) { return Failure("legacy_json_invalid", relativePath, exception.Message); }

                List<ContentDiagnostic> shapeDiagnostics = ValidateSourceShape(source.kind, document, relativePath, sequenceBuilder);
                if (shapeDiagnostics.Count > 0)
                    return ContentResult<ContentPackage>.Failure(ContentStatuses.Invalid, shapeDiagnostics[0].code,
                        shapeDiagnostics[0].path, shapeDiagnostics[0].message);
                byte[] payloadBytes = NormalizeRuntimePayload(source.kind, bytes);
                string payloadHash = Hash(payloadBytes);

                string sourceId;
                string contentId;
                string contentRevision;
                string metadataJson;
                try
                {
                    if (source.kind == ContentSourceKinds.GenericDialogue)
                    {
                        DialogueDefinition definition = ParseDialogue(document);
                        List<string> errors = DialogueValidator.Validate(definition);
                        if (errors.Count > 0) return Failure("legacy_dialogue_invalid", relativePath, string.Join(" ", errors.ToArray()));
                        if (definition.source != DialogueDefinition.GenericSource)
                            return Failure("legacy_dialogue_kind_unsupported", relativePath, "Only the Generic dialogue source is accepted by this importer.");
                        if (definition.nodes.Count > 64)
                            return Failure("legacy_dialogue_graph_too_large", relativePath, "The current SQL Flow bound is 64 Steps.");
                        sourceId = definition.id;
                        contentId = AddGenericDialogue(package, storeId, definition, document, out contentRevision, out metadataJson);
                    }
                    else if (source.kind == ContentSourceKinds.TopicDefinition)
                    {
                        TopicDefinition definition = ParseTopic(document);
                        List<string> errors = TopicDefinitionValidator.Validate(definition);
                        if (errors.Count > 0) return Failure("legacy_topic_invalid", relativePath, string.Join(" ", errors.ToArray()));
                        if (definition.scenarios.Any(item => item.nodes != null && item.nodes.Count > 64))
                            return Failure("legacy_topic_graph_too_large", relativePath, "The current SQL Flow bound is 64 Steps per Topic Scenario.");
                        string referenceError;
                        if (!ValidateTopicScenarioReferences(definition, package, storeId, out referenceError))
                            return Failure("legacy_topic_scenario_ref_invalid", relativePath, referenceError);
                        sourceId = definition.topicId;
                        contentId = AddTopic(package, storeId, definition, document, out contentRevision, out metadataJson);
                    }
                    else if (source.kind == ContentSourceKinds.ScenarioDefinition)
                    {
                        ScenarioDefinition definition = ParseScenario(document);
                        if (!HasReusableScenarioSteps(document))
                        {
                            IReadOnlyList<string> errors = ScenarioDefinitionValidator.Validate(definition, null);
                            if (errors.Count > 0) return Failure("legacy_scenario_invalid", relativePath, string.Join(" ", errors.ToArray()));
                        }
                        sourceId = definition.scenarioId;
                        contentId = AddScenario(package, storeId, definition, document, out contentRevision, out metadataJson);
                    }
                    else if (source.kind == ContentSourceKinds.DialogueDefinition || source.kind == ContentSourceKinds.ItemDefinition ||
                        source.kind == ContentSourceKinds.FurnitureDefinition || source.kind == ContentSourceKinds.ActionDefinition)
                        ProjectReusableDefinition(package, storeId, source, sourceHash, document, sequenceBuilder,
                            out sourceId, out contentRevision, out contentId, out metadataJson);
                    else if (source.kind == ContentSourceKinds.FurnitureCatalogueSettings)
                    {
                        sourceId = "furniture.catalogue";
                        contentId = sourceId;
                        contentRevision = sourceHash;
                        metadataJson = "{\"documentType\":\"furniture_catalogue_settings\"}";
                        package.furnitureCatalogueSettings = JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(CanonicalJson(document));
                        package.furnitureCatalogueSettings.definitions = new List<FurnitureDefinition>();
                    }
                    else if (IsRawDocumentKind(source.kind))
                    {
                        sourceId = string.IsNullOrEmpty(source.sourceId)
                            ? "legacy_" + Hash(Encoding.UTF8.GetBytes(relativePath)).Substring(0, 40)
                            : source.sourceId;
                        string legacyPathHash = sourceId.StartsWith("legacy_", StringComparison.Ordinal) && sourceId.Length == 47
                            ? sourceId.Substring("legacy_".Length)
                            : Hash(Encoding.UTF8.GetBytes(sourceId)).Substring(0, 40);
                        contentId = "legacy_document_" + legacyPathHash;
                        contentRevision = sourceHash;
                        metadataJson = "{\"documentType\":" + ContentJson.Quote(source.kind) + "}";
                    }
                    else return Failure("source_kind_unsupported", relativePath, "The source kind is not supported by the versioned importer.");
                }
                catch (Exception exception)
                { return Failure("legacy_projection_failed", relativePath, exception.Message); }

                if (!string.IsNullOrEmpty(source.sourceId) && !IsRawDocumentKind(source.kind) && source.sourceId != sourceId)
                    return Failure("source_identity_mismatch", relativePath, "The stable source ID does not match the ID embedded in this content document.");
                string identityKey = source.kind + "\0" + sourceId;
                if (!sourceIdentities.Add(identityKey))
                    return Failure("source_identity_duplicate", relativePath, "Two named sources claim the same kind and stable source ID.");

                package.legacySources.Add(new LegacySourceRecord
                {
                    kind = source.kind,
                    relativePath = relativePath,
                    sourceId = sourceId,
                    sourceHash = sourceHash,
                    payloadHash = payloadHash,
                    contentId = contentId,
                    contentRevision = contentRevision,
                    metadataJson = metadataJson,
                    sourceBytes = payloadBytes
                });
                manifestText.Append(source.kind).Append('\n').Append(relativePath).Append('\n').Append(sourceHash).Append('\n')
                    .Append(payloadHash).Append('\n');
            }

            package.sourceManifestHash = Hash(Encoding.UTF8.GetBytes(packageId + "\n" + storeId + "\n" + manifestText));
            return ContentResult<ContentPackage>.Ready(package);
        }

        private static bool ValidateManifest(ContentSourceManifest manifest, out string root, out string error)
        {
            root = string.Empty;
            error = string.Empty;
            Guid store;
            if (manifest == null || !IsIdentifier(manifest.packageId) || !Guid.TryParse(manifest.storeId, out store) ||
                string.IsNullOrWhiteSpace(manifest.sourceRoot) || manifest.sources == null ||
                manifest.sources.Count == 0 || manifest.sources.Count > MaximumSources)
            { error = "A bounded package ID, store GUID, trusted source root and 1..10000 explicit source entries are required."; return false; }

            try { root = Path.GetFullPath(manifest.sourceRoot); }
            catch (Exception exception) { error = exception.Message; return false; }
            if (!Directory.Exists(root)) { error = "The trusted source root does not exist."; return false; }
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            { error = "A reparse point cannot be used as the trusted source root."; return false; }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentSourceFile source in manifest.sources)
            {
                if (source == null || !IsSupportedSourceKind(source.kind) ||
                    !IsSafeRelativePath(source.relativePath) || !IsSha256(source.sha256))
                { error = "Every source needs a supported kind, safe relative path and lowercase SHA-256."; return false; }
                string key = NormalizeRelativePath(source.relativePath);
                if (!paths.Add(key)) { error = "The source manifest repeats a logical path."; return false; }
            }
            return true;
        }

        private static bool ResolveTrustedFile(string root, string relativePath, out string fullPath, out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;
            if (!IsSafeRelativePath(relativePath)) { error = "A rooted or traversal source path is not permitted."; return false; }
            try { fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception exception) { error = exception.Message; return false; }
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { error = "The resolved source path escapes the trusted root."; return false; }

            string cursor = root;
            foreach (string segment in relativePath.Split('/'))
            {
                cursor = Path.Combine(cursor, segment);
                if (!File.Exists(cursor) && !Directory.Exists(cursor)) { error = "A named source path does not exist."; return false; }
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                { error = "Reparse points are not permitted in a trusted source path."; return false; }
            }
            if (!File.Exists(fullPath)) { error = "The named source is not a file."; return false; }
            return true;
        }

        internal static List<ContentDiagnostic> ValidateSourceShape(string kind, JsonValue document, string path,
            ActivitySequenceBuilder sequenceBuilder = null)
        {
            var issues = new List<ContentDiagnostic>();
            if (kind == ContentSourceKinds.GenericDialogue)
            {
                CheckObject(document, path, issues, "schemaVersion", "id", "source", "routing", "nodes");
                JsonValue routing = Child(document, "routing");
                if (!IsNull(routing))
                {
                    CheckObject(routing, path + ".routing", issues, "routeId", "priority", "startHour", "endHour", "examples", "requiredContext", "blockedContext", "eligibility");
                    ValidateEligibility(Child(routing, "eligibility"), path + ".routing.eligibility", issues);
                }
                foreach (JsonValue node in Array(Child(document, "nodes"))) ValidateCanonicalNode(node, path + ".nodes[]", issues);
            }
            else if (kind == ContentSourceKinds.TopicDefinition)
            {
                CheckObject(document, path, issues, "schemaVersion", "topicId", "menuLabel", "prompt", "anchors", "defaultReplayPolicy", "cooldownSeconds", "scenarios");
                foreach (JsonValue scenario in Array(Child(document, "scenarios")))
                {
                    CheckObject(scenario, path + ".scenarios[]", issues, "scenarioId", "priority", "baseWeight", "condition", "channels", "modifiers", "consumptionNodeId", "nodes", "scenarioRef");
                    if (!IsNull(Child(scenario, "scenarioRef")))
                        CheckObject(Child(scenario, "scenarioRef"), path + ".scenarios[].scenarioRef", issues,
                            "store", "id", "revision", "expectedHash");
                    ValidateTopicCondition(Child(scenario, "condition"), path + ".condition", issues);
                    foreach (JsonValue modifier in Array(Child(scenario, "modifiers")))
                    {
                        CheckObject(modifier, path + ".modifiers[]", issues, "condition", "amount");
                        ValidateTopicCondition(Child(modifier, "condition"), path + ".modifier.condition", issues);
                    }
                    foreach (JsonValue node in Array(Child(scenario, "nodes"))) ValidateCanonicalNode(node, path + ".nodes[]", issues);
                }
            }
            else if (kind == ContentSourceKinds.ScenarioDefinition)
            {
                CheckObject(document, path, issues, "scenarioId", "topicId", "entryStepId", "steps");
                foreach (JsonValue step in Array(Child(document, "steps"))) ValidateScenarioStep(step, path + ".steps[]", issues);
                ValidateReusableScenarioShape(document, path, issues);
            }
            else if (kind == ContentSourceKinds.DialogueDefinition || kind == ContentSourceKinds.ItemDefinition ||
                kind == ContentSourceKinds.FurnitureDefinition || kind == ContentSourceKinds.ActionDefinition)
                issues.AddRange(ReusableContentDefinitionAdapter.ValidateSourceShape(kind, document, path, sequenceBuilder));
            else if (kind == ContentSourceKinds.FurnitureCatalogueSettings)
            {
                JsonObject settings = document as JsonObject;
                if (settings == null)
                    issues.Add(new ContentDiagnostic("furniture_catalogue_settings_invalid", path,
                        "Furniture catalogue settings must be one JSON object."));
                else ValidateFurnitureCatalogueSettings(settings, path, issues);
            }
            else if (kind == ContentSourceKinds.LegacySequence)
            {
                JsonObject root = document as JsonObject;
                if (root == null || !(Child(document, "metadata") is JsonObject) || !(Child(document, "nodes") is JsonArray))
                    issues.Add(new ContentDiagnostic("legacy_sequence_shape_invalid", path,
                        "A legacy DialogueSequence must retain its metadata object and nodes array."));
            }
            else if (kind == ContentSourceKinds.AmbientPool)
            {
                JsonObject root = document as JsonObject;
                if (root == null || !(Child(document, "metadata") is JsonObject) || !(Child(document, "lines") is JsonArray))
                    issues.Add(new ContentDiagnostic("ambient_pool_shape_invalid", path,
                        "An ambient pool must retain its metadata object and lines array."));
            }
            else if (kind == ContentSourceKinds.ActivityScenarioLibrary)
            {
                JsonObject root = document as JsonObject;
                JsonArray entries = Child(document, "entries") as JsonArray;
                if (root == null || entries == null)
                    issues.Add(new ContentDiagnostic("activity_scenario_library_shape_invalid", path,
                        "An activity Scenario library must retain its entries array."));
                else
                    foreach (JsonValue entryValue in entries)
                    {
                        CheckObject(entryValue, path + ".entries[]", issues, "capabilityId", "engagement", "continueLabel",
                            "stopLabel", "waiting", "returning", "readyBark", "scenario");
                        foreach (string beatName in new[] { "waiting", "returning", "readyBark" })
                        {
                            JsonValue beat = Child(entryValue, beatName);
                            if (!IsNull(beat)) CheckObject(beat, path + ".entries[]." + beatName, issues,
                                "id", "text", "displayText", "language", "expression");
                        }
                        JsonValue scenario = Child(entryValue, "scenario");
                        if (!IsNull(scenario))
                        {
                            CheckObject(scenario, path + ".entries[].scenario", issues, "scenarioId", "topicId", "entryStepId", "steps");
                            foreach (JsonValue step in Array(Child(scenario, "steps")))
                                ValidateScenarioStep(step, path + ".entries[].scenario.steps[]", issues);
                        }
                    }
            }
            else if (kind == ContentSourceKinds.ActivityGreetingMenu)
            {
                JsonArray options = Child(document, "options") as JsonArray;
                if (!(document is JsonObject) || options == null)
                    issues.Add(new ContentDiagnostic("activity_greeting_menu_shape_invalid", path,
                        "The activity greeting menu must retain its options array."));
                else foreach (JsonValue option in options)
                    CheckObject(option, path + ".options[]", issues, "label", "capabilityId", "target");
            }
            else if (kind == ContentSourceKinds.ConversationCoverage)
            {
                JsonArray topics = Child(document, "topics") as JsonArray;
                if (!(document is JsonObject) || topics == null)
                    issues.Add(new ContentDiagnostic("conversation_coverage_shape_invalid", path,
                        "Conversation coverage must retain its topics array."));
                else foreach (JsonValue topic in topics)
                {
                    CheckObject(topic, path + ".topics[]", issues, "topicId", "allowUncoveredDiscussion", "scenarios");
                    foreach (JsonValue scenario in Array(Child(topic, "scenarios")))
                        CheckObject(scenario, path + ".topics[].scenarios[]", issues, "scenarioId", "experienceKey", "coverageSummary");
                }
            }
            else if (kind == ContentSourceKinds.TopicMigrationMap)
            {
                JsonArray mapping = Child(document, "mapping") as JsonArray;
                if (!(document is JsonObject) || mapping == null)
                    issues.Add(new ContentDiagnostic("topic_migration_map_shape_invalid", path,
                        "The Topic migration map must retain its mapping array."));
                else foreach (JsonValue row in mapping)
                    CheckObject(row, path + ".mapping[]", issues, "sourceFile", "legacyDialogueId", "legacyRouteId", "topicId",
                        "scenarioId", "legacyPriority", "scenarioPriority", "legacyTimeWindow", "stagedFile");
            }
            else if (kind == ContentSourceKinds.OnboardingSteps)
            {
                JsonArray steps = Child(document, "steps") as JsonArray;
                if (!(document is JsonObject) || steps == null)
                    issues.Add(new ContentDiagnostic("onboarding_steps_shape_invalid", path,
                        "The onboarding document must retain its steps array."));
                else foreach (JsonValue step in steps)
                {
                    CheckObject(step, path + ".steps[]", issues, "stepId", "speechText", "displayText", "choices", "answerCategory");
                    foreach (JsonValue choice in Array(Child(step, "choices")))
                        CheckObject(choice, path + ".steps[].choices[]", issues, "choiceId", "label");
                }
            }
            return issues;
        }

        private static void ValidateFurnitureCatalogueSettings(JsonObject settings, string path, List<ContentDiagnostic> issues)
        {
            CheckObject(settings, path, issues, "schemaVersion", "catalogueVersion", "lengthUnit", "angleUnit",
                "coordinateConvention", "relationshipRules", "capabilities", "legacyAliases");
            JsonValue schemaVersion = Child(settings, "schemaVersion");
            long parsedVersion;
            if (schemaVersion == null || schemaVersion.Type != JTokenType.Integer ||
                !long.TryParse(schemaVersion.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedVersion) || parsedVersion != 1)
                issues.Add(new ContentDiagnostic("furniture_catalogue_settings_invalid", path + ".schemaVersion", "Only Furniture settings schema version 1 is supported."));
            RequireString(settings, "catalogueVersion", path, issues);
            if (string.IsNullOrWhiteSpace(JsonStringValue(Child(settings, "catalogueVersion"))))
                issues.Add(new ContentDiagnostic("furniture_catalogue_settings_invalid", path + ".catalogueVersion", "Catalogue version must be a non-empty string."));
            RequireString(settings, "lengthUnit", path, issues);
            RequireString(settings, "angleUnit", path, issues);
            RequireString(settings, "coordinateConvention", path, issues);
            if (JsonStringValue(Child(settings, "lengthUnit")) != "m" || JsonStringValue(Child(settings, "angleUnit")) != "deg" ||
                JsonStringValue(Child(settings, "coordinateConvention")) != "unity_local_y_up_positive_z_forward")
                issues.Add(new ContentDiagnostic("furniture_catalogue_settings_invalid", path, "Furniture settings use unsupported units or coordinate convention."));
            if (!IsNull(Child(settings, "definitions")))
                issues.Add(new ContentDiagnostic("furniture_catalogue_settings_invalid", path + ".definitions", "Global settings cannot contain copied definition bodies."));

            JsonArray rules = RequireArray(settings, "relationshipRules", path, issues);
            for (int index = 0; index < (rules == null ? 0 : rules.Count); index++)
            {
                string itemPath = path + ".relationshipRules[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                JsonObject rule = rules[index] as JsonObject;
                if (rule == null)
                {
                    issues.Add(new ContentDiagnostic("furniture_catalogue_rule_invalid", itemPath, "A relationship rule must be an object."));
                    continue;
                }
                CheckObject(rule, itemPath, issues, "relationshipRuleId", "kind", "pointTypeIds", "broadPhaseRadiusMeters",
                    "minimumDistanceMeters", "maximumDistanceMeters", "maximumFacingErrorDegrees", "maximumHeightDifferenceMeters",
                    "lateralAllowanceMeters");
                RequireString(rule, "relationshipRuleId", itemPath, issues);
                RequireString(rule, "kind", itemPath, issues);
                RequireStringArray(rule, "pointTypeIds", itemPath, issues);
                foreach (string numericField in new[] { "broadPhaseRadiusMeters", "minimumDistanceMeters", "maximumDistanceMeters",
                    "maximumFacingErrorDegrees", "maximumHeightDifferenceMeters", "lateralAllowanceMeters" })
                    RequireFiniteNumber(rule, numericField, itemPath, issues);
            }

            JsonArray capabilities = RequireArray(settings, "capabilities", path, issues);
            for (int index = 0; index < (capabilities == null ? 0 : capabilities.Count); index++)
            {
                string itemPath = path + ".capabilities[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                JsonObject capability = capabilities[index] as JsonObject;
                if (capability == null)
                {
                    issues.Add(new ContentDiagnostic("furniture_catalogue_capability_invalid", itemPath, "A capability descriptor must be an object."));
                    continue;
                }
                CheckObject(capability, itemPath, issues, "capabilityId", "requiredPointTypeIds", "requiredUseIds");
                RequireString(capability, "capabilityId", itemPath, issues);
                RequireStringArray(capability, "requiredPointTypeIds", itemPath, issues);
                RequireStringArray(capability, "requiredUseIds", itemPath, issues);
            }

            JsonArray aliases = RequireArray(settings, "legacyAliases", path, issues);
            for (int index = 0; index < (aliases == null ? 0 : aliases.Count); index++)
            {
                string itemPath = path + ".legacyAliases[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                JsonObject alias = aliases[index] as JsonObject;
                if (alias == null)
                {
                    issues.Add(new ContentDiagnostic("furniture_catalogue_alias_invalid", itemPath, "A legacy alias must be an object."));
                    continue;
                }
                CheckObject(alias, itemPath, issues, "legacyId", "definitionId", "pointId");
                RequireString(alias, "legacyId", itemPath, issues);
                RequireString(alias, "definitionId", itemPath, issues);
                if (alias.ContainsKey("pointId")) RequireString(alias, "pointId", itemPath, issues);
            }
        }

        private static JsonArray RequireArray(JsonObject parent, string field, string path, List<ContentDiagnostic> issues)
        {
            JsonArray value = Child(parent, field) as JsonArray;
            if (value == null)
                issues.Add(new ContentDiagnostic("furniture_catalogue_field_invalid", path + "." + field, "A required array field is missing or has the wrong JSON kind."));
            return value;
        }

        private static void RequireStringArray(JsonObject parent, string field, string path, List<ContentDiagnostic> issues)
        {
            JsonArray values = RequireArray(parent, field, path, issues);
            if (values == null) return;
            for (int index = 0; index < values.Count; index++)
                if (values[index] == null || values[index].Type != JTokenType.String)
                    issues.Add(new ContentDiagnostic("furniture_catalogue_field_invalid",
                        path + "." + field + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", "Array entries must be strings."));
        }

        private static void RequireString(JsonObject parent, string field, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value = Child(parent, field);
            if (value == null || value.Type != JTokenType.String)
                issues.Add(new ContentDiagnostic("furniture_catalogue_field_invalid", path + "." + field, "A required string field is missing or has the wrong JSON kind."));
        }

        private static void RequireFiniteNumber(JsonObject parent, string field, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value = Child(parent, field);
            if (value == null)
            {
                issues.Add(new ContentDiagnostic("furniture_catalogue_rule_field_missing", path + "." + field,
                    "A relationship rule must explicitly author every numeric tolerance field."));
                return;
            }
            double parsed;
            JsonPrimitive primitive = value as JsonPrimitive;
            string numericValue = primitive == null ? string.Empty : Convert.ToString(primitive.Value, CultureInfo.InvariantCulture);
            if ((value.Type != JTokenType.Integer && value.Type != JTokenType.Float) ||
                !double.TryParse(numericValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                double.IsNaN(parsed) || double.IsInfinity(parsed))
                issues.Add(new ContentDiagnostic("furniture_catalogue_field_invalid", path + "." + field, "A required finite number is missing or has the wrong JSON kind."));
        }

        private static string JsonStringValue(JsonValue value)
        { return value != null && value.Type == JTokenType.String ? (string)value : string.Empty; }

        internal static bool IsRawDocumentKind(string kind)
        {
            return kind == ContentSourceKinds.LegacySequence || kind == ContentSourceKinds.AmbientPool ||
                kind == ContentSourceKinds.ActivityScenarioLibrary || kind == ContentSourceKinds.ActivityGreetingMenu ||
                kind == ContentSourceKinds.ConversationCoverage || kind == ContentSourceKinds.TopicMigrationMap ||
                kind == ContentSourceKinds.OnboardingSteps;
        }

        private static byte[] NormalizeRuntimePayload(string kind, byte[] sourceBytes)
        {
            if (kind != ContentSourceKinds.AmbientPool) return (byte[])sourceBytes.Clone();
            string json = new UTF8Encoding(false, true).GetString(sourceBytes).TrimStart('\uFEFF');
            JsonObject root = ContentJson.Parse(json) as JsonObject;
            JsonArray lines = root == null || !root.ContainsKey("lines") ? null : root["lines"] as JsonArray;
            if (lines == null) return (byte[])sourceBytes.Clone();
            bool changed = false;
            foreach (JsonValue value in lines)
            {
                JsonObject line = value as JsonObject;
                if (line == null) continue;
                if (line.ContainsKey("playCount") && line["playCount"].ToString() != "0")
                { line["playCount"] = new JsonPrimitive(0); changed = true; }
                if (line.ContainsKey("lastPlayTime") && line["lastPlayTime"].ToString() != "0")
                { line["lastPlayTime"] = new JsonPrimitive(0L); changed = true; }
            }
            return changed ? Encoding.UTF8.GetBytes(root.ToString()) : (byte[])sourceBytes.Clone();
        }

        private static void ValidateCanonicalNode(JsonValue node, string path, List<ContentDiagnostic> issues)
        {
            CheckObject(node, path, issues, "id", "speaker", "text", "displayText", "language", "voice", "audioKey", "nextId", "presentation", "choices");
            JsonValue presentation = Child(node, "presentation");
            if (!IsNull(presentation)) CheckObject(presentation, path + ".presentation", issues, "expression", "animation");
            foreach (JsonValue choice in Array(Child(node, "choices")))
                CheckObject(choice, path + ".choices[]", issues, "label", "nextId");
        }

        private static void ValidateEligibility(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            if (IsNull(value)) return;
            CheckObject(value, path, issues, "schemaVersion", "all", "any", "none", "not");
            foreach (string listName in new[] { "all", "any", "none" })
                foreach (JsonValue condition in Array(Child(value, listName)))
                    CheckObject(condition, path + "." + listName + "[]", issues, "statId", "operation", "value");
            ValidateEligibility(Child(value, "not"), path + ".not", issues);
        }

        private static void ValidateTopicCondition(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            if (IsNull(value)) return;
            CheckObject(value, path, issues, "kind", "children", "statId", "operation", "value", "key", "expected", "startHour", "endHour", "objectKind");
            foreach (JsonValue child in Array(Child(value, "children"))) ValidateTopicCondition(child, path + ".children[]", issues);
        }

        private static void ValidateScenarioStep(JsonValue step, string path, List<ContentDiagnostic> issues)
        {
            CheckObject(step, path, issues, "id", "kind", "beats", "nextStepId", "closeExchange", "questionId", "question", "options",
                "allowFreeText", "freeTextNextStepId", "skipNextStepId", "cancelNextStepId", "capabilityId", "arguments",
                "successNextStepId", "failureNextStepId", "activityId", "conditionId", "objective", "sourceId", "cases", "defaultNextStepId", "outcome",
                "dialogueRef", "actionRef", "data");
            foreach (JsonValue beat in Array(Child(step, "beats"))) ValidateScenarioBeat(beat, path + ".beats[]", issues);
            ValidateScenarioBeat(Child(step, "question"), path + ".question", issues);
            foreach (JsonValue option in Array(Child(step, "options")))
                CheckObject(option, path + ".options[]", issues, "optionId", "label", "value", "nextStepId");
            foreach (JsonValue argument in Array(Child(step, "arguments")))
                CheckObject(argument, path + ".arguments[]", issues, "name", "value");
            foreach (JsonValue item in Array(Child(step, "cases")))
                CheckObject(item, path + ".cases[]", issues, "value", "nextStepId");
        }

        private static void ValidateReusableScenarioShape(JsonValue document, string path, List<ContentDiagnostic> issues)
        {
            JsonArray steps = Child(document, "steps") as JsonArray;
            if (steps == null || steps.Count == 0 || steps.Count > 64) return;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string entry = ReadString(document, "entryStepId", string.Empty);
            bool usesReusableReferences = steps.Any(step => ReadString(step, "kind", string.Empty) == "host_effect" ||
                !IsNull(Child(step, "dialogueRef")) || !IsNull(Child(step, "actionRef")));
            if (!usesReusableReferences) return;
            if (!IsIdentifier(entry)) issues.Add(new ContentDiagnostic("scenario_entry_invalid", path + ".entryStepId", "A valid entry Step ID is required."));
            foreach (JsonValue step in steps)
            {
                string stepPath = path + ".steps[" + ReadString(step, "id", string.Empty) + "]";
                string id = ReadString(step, "id", string.Empty), kind = ReadString(step, "kind", string.Empty);
                if (!IsIdentifier(id) || !ids.Add(id)) issues.Add(new ContentDiagnostic("scenario_step_id_invalid", stepPath + ".id", "Step IDs must be valid and unique."));
                bool hasDialogueRef = !IsNull(Child(step, "dialogueRef"));
                bool hasActionRef = !IsNull(Child(step, "actionRef"));
                if (hasDialogueRef) ValidatePinnedSourceReference(Child(step, "dialogueRef"), stepPath + ".dialogueRef", issues);
                if (hasActionRef) ValidatePinnedSourceReference(Child(step, "actionRef"), stepPath + ".actionRef", issues);
                if (kind == "dialogue" || kind == "ask")
                {
                    bool hasInline = kind == "dialogue" ? Array(Child(step, "beats")).Any() : !IsNull(Child(step, "question"));
                    if (hasDialogueRef == hasInline)
                        issues.Add(new ContentDiagnostic("scenario_dialogue_source_ambiguous", stepPath, "A Dialogue or Ask Step needs exactly one inline definition or pinned dialogueRef."));
                }
                else if (kind == "action")
                {
                    if (hasActionRef && (!IsNull(Child(step, "capabilityId")) || Array(Child(step, "arguments")).Any()))
                        issues.Add(new ContentDiagnostic("scenario_action_source_ambiguous", stepPath, "A pinned actionRef cannot be combined with inline action fields."));
                }
                else if (kind == "host_effect")
                {
                    if (!hasActionRef || hasDialogueRef)
                        issues.Add(new ContentDiagnostic("scenario_host_effect_reference_invalid", stepPath, "A host_effect Step requires exactly one pinned actionRef."));
                    JsonValue data = Child(step, "data");
                    CheckObject(data, stepPath + ".data", issues, "schemaVersion", "requiresCompletedStepId");
                    if (ReadInt(data, "schemaVersion", 0) != 1)
                        issues.Add(new ContentDiagnostic("scenario_host_effect_schema_unsupported", stepPath + ".data.schemaVersion", "host_effect data supports schemaVersion 1."));
                    string prerequisite = ReadString(data, "requiresCompletedStepId", string.Empty);
                    if (!IsIdentifier(prerequisite) || prerequisite == id)
                        issues.Add(new ContentDiagnostic("scenario_host_effect_prerequisite_invalid", stepPath + ".data.requiresCompletedStepId", "A host effect must require a different valid completed Step ID."));
                }
                else if (kind != "wait" && kind != "branch" && kind != "end")
                    issues.Add(new ContentDiagnostic("scenario_step_kind_invalid", stepPath + ".kind", "Scenario Step kind is unsupported."));
            }
            foreach (JsonValue effect in steps.Where(step => ReadString(step, "kind", string.Empty) == "host_effect"))
            {
                string effectId = ReadString(effect, "id", string.Empty);
                string prerequisiteId = ReadString(Child(effect, "data"), "requiresCompletedStepId", string.Empty);
                JsonValue prerequisite = steps.FirstOrDefault(step => ReadString(step, "id", string.Empty) == prerequisiteId);
                if (prerequisite == null || ReadString(prerequisite, "kind", string.Empty) != "dialogue")
                    issues.Add(new ContentDiagnostic("scenario_host_effect_prerequisite_invalid", path + ".steps[" + effectId + "].data.requiresCompletedStepId",
                        "A host effect must require a Dialogue Step, not an Ask or Action Step."));
            }
            if (!ids.Contains(entry)) issues.Add(new ContentDiagnostic("scenario_entry_missing", path + ".entryStepId", "The entry Step must exist."));
        }

        private static void ValidatePinnedSourceReference(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            CheckObject(value, path, issues, "store", "id", "revision", "expectedHash");
            string store = ReadString(value, "store", string.Empty);
            string id = ReadString(value, "id", string.Empty);
            string revision = ReadString(value, "revision", string.Empty);
            string hash = ReadString(value, "expectedHash", string.Empty);
            if (!Guid.TryParse(store, out _) || !IsIdentifier(id) || !IsSha256(revision) || !IsSha256(hash) || revision != hash)
                issues.Add(new ContentDiagnostic("scenario_content_reference_invalid", path, "A reusable reference must pin a store, ID, revision, and matching SHA-256 hash."));
        }

        private static void ValidateScenarioBeat(JsonValue beat, string path, List<ContentDiagnostic> issues)
        {
            if (IsNull(beat)) return;
            CheckObject(beat, path, issues, "id", "speaker", "text", "displayText", "language", "expression", "voice", "audioKey", "animation");
        }

        internal static void CheckObject(JsonValue value, string path, List<ContentDiagnostic> issues, params string[] allowed)
        {
            JsonObject item = value as JsonObject;
            if (item == null)
            {
                issues.Add(new ContentDiagnostic("legacy_object_expected", path, "A source object is missing or has the wrong JSON kind."));
                return;
            }
            var known = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonValue> field in item)
                if (!known.Contains(field.Key))
                    issues.Add(new ContentDiagnostic("unsupported_legacy_field", path + "." + field.Key,
                        "The importer does not recognize this source field and will not silently discard it."));
        }

        internal static JsonValue Child(JsonValue value, string name)
        {
            JsonObject item = value as JsonObject;
            return item == null || !item.ContainsKey(name) ? null : item[name];
        }

        internal static IEnumerable<JsonValue> Array(JsonValue value)
        {
            JsonArray array = value as JsonArray;
            return array ?? Enumerable.Empty<JsonValue>();
        }

        internal static bool IsNull(JsonValue value) { return ContentJson.IsNull(value); }

        private static DialogueDefinition ParseDialogue(JsonValue document)
        {
            JsonValue route = Child(document, "routing");
            JsonValue eligibility = Child(route, "eligibility");
            return new DialogueDefinition
            {
                schemaVersion = ReadInt(document, "schemaVersion", DialogueDefinition.CurrentSchemaVersion),
                id = ReadString(document, "id", string.Empty),
                source = ReadString(document, "source", DialogueDefinition.GenericSource),
                routing = new DialogueRouting
                {
                    routeId = ReadString(route, "routeId", string.Empty),
                    priority = ReadFloat(route, "priority", 1f),
                    startHour = ReadInt(route, "startHour", -1),
                    endHour = ReadInt(route, "endHour", -1),
                    examples = ReadStrings(route, "examples"),
                    requiredContext = ReadStrings(route, "requiredContext"),
                    blockedContext = ReadStrings(route, "blockedContext"),
                    eligibility = ParseEligibility(eligibility)
                },
                nodes = ReadObjects(document, "nodes", ParseCanonicalNode)
            };
        }

        private static CanonicalStatEligibility ParseEligibility(JsonValue value)
        {
            if (IsNull(value)) return null;
            return new CanonicalStatEligibility
            {
                schemaVersion = ReadInt(value, "schemaVersion", CanonicalStatEligibility.CurrentSchemaVersion),
                all = ReadObjects(value, "all", ParseStatCondition),
                any = HasChild(value, "any") ? ReadObjects(value, "any", ParseStatCondition) : null,
                none = HasChild(value, "none") ? ReadObjects(value, "none", ParseStatCondition) : null,
                not = ParseEligibility(Child(value, "not"))
            };
        }

        private static CanonicalStatCondition ParseStatCondition(JsonValue value)
        {
            return new CanonicalStatCondition
            {
                statId = ReadString(value, "statId", string.Empty),
                operation = ReadString(value, "operation", string.Empty),
                value = ReadFloat(value, "value", 0f)
            };
        }

        private static TopicDefinition ParseTopic(JsonValue document)
        {
            return new TopicDefinition
            {
                schemaVersion = ReadInt(document, "schemaVersion", 1),
                topicId = ReadString(document, "topicId", string.Empty),
                menuLabel = ReadString(document, "menuLabel", string.Empty),
                prompt = ReadString(document, "prompt", string.Empty),
                anchors = ReadStrings(document, "anchors"),
                defaultReplayPolicy = ReadString(document, "defaultReplayPolicy", TopicReplayPolicy.Repeatable),
                cooldownSeconds = ReadDouble(document, "cooldownSeconds", 0d),
                scenarios = ReadObjects(document, "scenarios", ParseTopicScenario)
            };
        }

        private static TopicScenario ParseTopicScenario(JsonValue value)
        {
            return new TopicScenario
            {
                scenarioId = ReadString(value, "scenarioId", string.Empty),
                priority = ReadInt(value, "priority", 0),
                baseWeight = ReadDouble(value, "baseWeight", 1d),
                condition = ParseTopicCondition(Child(value, "condition")),
                channels = HasChild(value, "channels") ? ReadStrings(value, "channels") : null,
                modifiers = ReadObjects(value, "modifiers", item => new TopicWeightModifier
                { condition = ParseTopicCondition(Child(item, "condition")), amount = ReadDouble(item, "amount", 0d) }),
                consumptionNodeId = ReadString(value, "consumptionNodeId", string.Empty),
                scenarioRef = IsNull(Child(value, "scenarioRef")) ? null : ReadPinnedSourceReference(Child(value, "scenarioRef")),
                nodes = ReadObjects(value, "nodes", ParseCanonicalNode)
            };
        }

        internal static TopicCondition ParseTopicCondition(JsonValue value)
        {
            if (IsNull(value)) return null;
            return new TopicCondition
            {
                kind = ReadString(value, "kind", string.Empty),
                children = ReadObjects(value, "children", ParseTopicCondition),
                statId = ReadString(value, "statId", string.Empty),
                operation = ReadString(value, "operation", string.Empty),
                value = ReadFloat(value, "value", 0f),
                key = ReadString(value, "key", string.Empty),
                expected = ReadBool(value, "expected", true),
                startHour = ReadInt(value, "startHour", -1),
                endHour = ReadInt(value, "endHour", -1),
                objectKind = ReadString(value, "objectKind", string.Empty)
            };
        }

        private static CanonicalDialogueNode ParseCanonicalNode(JsonValue value)
        {
            JsonValue presentation = Child(value, "presentation");
            return new CanonicalDialogueNode
            {
                id = ReadString(value, "id", string.Empty),
                speaker = ReadString(value, "speaker", "alterra"),
                text = ReadString(value, "text", string.Empty),
                displayText = ReadString(value, "displayText", string.Empty),
                language = ReadString(value, "language", string.Empty),
                voice = ReadString(value, "voice", string.Empty),
                audioKey = ReadString(value, "audioKey", string.Empty),
                nextId = ReadString(value, "nextId", string.Empty),
                presentation = new DialoguePresentation
                {
                    expression = ReadString(presentation, "expression", string.Empty),
                    animation = ReadString(presentation, "animation", string.Empty)
                },
                choices = ReadObjects(value, "choices", item => new CanonicalDialogueChoice
                { label = ReadString(item, "label", string.Empty), nextId = ReadString(item, "nextId", string.Empty) })
            };
        }

        private static ScenarioDefinition ParseScenario(JsonValue document)
        {
            return new ScenarioDefinition
            {
                scenarioId = ReadString(document, "scenarioId", string.Empty),
                topicId = ReadString(document, "topicId", string.Empty),
                entryStepId = ReadString(document, "entryStepId", string.Empty),
                steps = ReadObjects(document, "steps", ParseScenarioStep)
            };
        }

        private static bool HasReusableScenarioSteps(JsonValue document)
        {
            return Array(Child(document, "steps")).Any(step => ReadString(step, "kind", string.Empty) == "host_effect" ||
                !IsNull(Child(step, "dialogueRef")) || !IsNull(Child(step, "actionRef")));
        }

        private static ContentRef ReadPinnedSourceReference(JsonValue value)
        {
            return new ContentRef(ReadString(value, "store", string.Empty), ReadString(value, "id", string.Empty),
                ReadString(value, "revision", string.Empty), ReadString(value, "expectedHash", string.Empty));
        }

        private static ScenarioStep ParseScenarioStep(JsonValue value)
        {
            return new ScenarioStep
            {
                id = ReadString(value, "id", string.Empty),
                kind = ReadString(value, "kind", string.Empty),
                beats = ReadObjects(value, "beats", ParseScenarioBeat),
                nextStepId = ReadString(value, "nextStepId", string.Empty),
                closeExchange = ReadBool(value, "closeExchange", false),
                questionId = ReadString(value, "questionId", string.Empty),
                question = ParseScenarioBeat(Child(value, "question")),
                options = ReadObjects(value, "options", item => new ScenarioQuestionOption
                {
                    optionId = ReadString(item, "optionId", string.Empty), label = ReadString(item, "label", string.Empty),
                    value = ReadString(item, "value", string.Empty), nextStepId = ReadString(item, "nextStepId", string.Empty)
                }),
                allowFreeText = ReadBool(value, "allowFreeText", false),
                freeTextNextStepId = ReadString(value, "freeTextNextStepId", string.Empty),
                skipNextStepId = ReadString(value, "skipNextStepId", string.Empty),
                cancelNextStepId = ReadString(value, "cancelNextStepId", string.Empty),
                capabilityId = ReadString(value, "capabilityId", string.Empty),
                arguments = ReadObjects(value, "arguments", item => new ScenarioActionArgument
                { name = ReadString(item, "name", string.Empty), value = ReadString(item, "value", string.Empty) }),
                successNextStepId = ReadString(value, "successNextStepId", string.Empty),
                failureNextStepId = ReadString(value, "failureNextStepId", string.Empty),
                activityId = ReadString(value, "activityId", string.Empty),
                conditionId = ReadString(value, "conditionId", string.Empty),
                objective = ReadString(value, "objective", string.Empty),
                sourceId = ReadString(value, "sourceId", string.Empty),
                cases = ReadObjects(value, "cases", item => new ScenarioBranchCase
                { value = ReadString(item, "value", string.Empty), nextStepId = ReadString(item, "nextStepId", string.Empty) }),
                defaultNextStepId = ReadString(value, "defaultNextStepId", string.Empty),
                outcome = ReadString(value, "outcome", string.Empty)
            };
        }

        private static ScenarioBeat ParseScenarioBeat(JsonValue value)
        {
            if (IsNull(value)) return null;
            return new ScenarioBeat
            {
                id = ReadString(value, "id", string.Empty),
                speaker = ReadString(value, "speaker", "alterra"),
                text = ReadString(value, "text", string.Empty),
                displayText = ReadString(value, "displayText", string.Empty),
                language = ReadString(value, "language", string.Empty),
                expression = ReadString(value, "expression", string.Empty)
            };
        }

        private static List<T> ReadObjects<T>(JsonValue value, string property, Func<JsonValue, T> parse)
        {
            JsonValue child = Child(value, property);
            if (IsNull(child)) return new List<T>();
            JsonArray array = child as JsonArray;
            if (array == null) throw new InvalidDataException("Source field '" + property + "' must be an array.");
            return array.Select(parse).ToList();
        }

        private static List<string> ReadStrings(JsonValue value, string property)
        {
            JsonValue child = Child(value, property);
            if (IsNull(child)) return new List<string>();
            JsonArray array = child as JsonArray;
            if (array == null) throw new InvalidDataException("Source field '" + property + "' must be an array.");
            var result = new List<string>();
            foreach (JsonValue item in array) result.Add(ReadStringValue(item, property));
            return result;
        }

        internal static string ReadString(JsonValue value, string property, string fallback)
        {
            JsonValue child = Child(value, property);
            return IsNull(child) ? fallback : ReadStringValue(child, property);
        }

        internal static string ReadStringValue(JsonValue value, string property)
        {
            try { return (string)value; }
            catch { throw new InvalidDataException("Source field '" + property + "' must be a string."); }
        }

        internal static int ReadInt(JsonValue value, string property, int fallback)
        {
            JsonValue child = Child(value, property);
            if (IsNull(child)) return fallback;
            int result;
            if (!int.TryParse(child.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException("Source field '" + property + "' must be an integer.");
            return result;
        }

        private static float ReadFloat(JsonValue value, string property, float fallback)
        {
            double parsed = ReadDouble(value, property, fallback);
            if (parsed < -float.MaxValue || parsed > float.MaxValue) throw new InvalidDataException("Source field '" + property + "' exceeds finite float bounds.");
            return (float)parsed;
        }

        private static double ReadDouble(JsonValue value, string property, double fallback)
        {
            JsonValue child = Child(value, property);
            if (IsNull(child)) return fallback;
            double result;
            if (!double.TryParse(child.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                double.IsNaN(result) || double.IsInfinity(result))
                throw new InvalidDataException("Source field '" + property + "' must be a finite number.");
            return result;
        }

        private static bool ReadBool(JsonValue value, string property, bool fallback)
        {
            JsonValue child = Child(value, property);
            if (IsNull(child)) return fallback;
            bool result;
            if (!bool.TryParse(child.ToString(), out result)) throw new InvalidDataException("Source field '" + property + "' must be a boolean.");
            return result;
        }

        private static bool HasChild(JsonValue value, string property)
        { JsonObject item = value as JsonObject; return item != null && item.ContainsKey(property); }

        internal static string CanonicalJson(JsonValue value)
        {
            if (IsNull(value)) return "null";
            JsonObject item = value as JsonObject;
            if (item != null)
                return "{" + string.Join(",", item.Properties().OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => Quote(field.Name) + ":" + CanonicalJson(field.Value)).ToArray()) + "}";
            JsonArray array = value as JsonArray;
            if (array != null) return "[" + string.Join(",", array.Select(CanonicalJson).ToArray()) + "]";
            return ContentJson.SerializeCompact(value);
        }

        internal static string Quote(string value)
        {
            return ContentJson.Quote(value);
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || Path.IsPathRooted(path)) return false;
            string normalized = NormalizeRelativePath(path);
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(":")) return false;
            string[] segments = normalized.Split('/');
            return segments.All(segment => !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != "..");
        }

        internal static bool IsIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value == "." || value == "..") return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':')) return false;
            return true;
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f' || character >= 'A' && character <= 'F')) return false;
            return true;
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return ToHex(hash.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static ContentResult<ContentPackage> Failure(string code, string path, string message)
        { return ContentResult<ContentPackage>.Failure(ContentStatuses.Invalid, code, path, message); }

        private static string AddGenericDialogue(ContentPackage package, string storeId, DialogueDefinition definition,
            JsonValue document, out string contentRevision, out string metadataJson)
        {
            JsonValue routing = Child(document, "routing");
            JsonValue nodes = Child(document, "nodes");
            string flowId = DerivedId("legacy_generic", definition.id);
            contentRevision = Hash(Encoding.UTF8.GetBytes("generic-flow\n" + CanonicalJson(nodes)));
            FlowRecord flow = BuildCanonicalNodeFlow(package, storeId, flowId, contentRevision, definition.id,
                definition.nodes, Array(nodes).ToList());
            package.flows.Add(flow);
            metadataJson = CanonicalJson(routing);
            AddAnchors(package, flow.id, flow.revision, "flow", definition.routing.examples, "example_");
            return flow.id;
        }

        private static string AddTopic(ContentPackage package, string storeId, TopicDefinition definition,
            JsonValue document, out string contentRevision, out string metadataJson)
        {
            contentRevision = Hash(Encoding.UTF8.GetBytes("topic\n" + CanonicalJson(document)));
            JsonValue scenariosJson = Child(document, "scenarios");
            JsonArray scenarioArray = scenariosJson as JsonArray;
            ContentRef titleRef = AddText(package, storeId, "topic-title\0" + definition.topicId,
                definition.menuLabel ?? string.Empty, "en", string.Empty, string.Empty);
            ContentRef promptRef = AddText(package, storeId, "topic-prompt\0" + definition.topicId,
                definition.prompt ?? string.Empty, "en", string.Empty, string.Empty);
            var topic = new TopicRecord
            {
                id = definition.topicId,
                revision = contentRevision,
                semanticHash = contentRevision,
                routeId = definition.topicId,
                titleTextRef = titleRef,
                promptTextRef = promptRef,
                defaultReplayPolicy = definition.defaultReplayPolicy,
                cooldownSeconds = definition.cooldownSeconds
            };

            for (int index = 0; index < definition.scenarios.Count; index++)
            {
                TopicScenario scenario = definition.scenarios[index];
                JsonValue scenarioJson = scenarioArray[index];
                ContentRef flowReference;
                if (scenario.scenarioRef != null)
                    flowReference = new ContentRef(scenario.scenarioRef.store, scenario.scenarioRef.id,
                        scenario.scenarioRef.revision, scenario.scenarioRef.expectedHash);
                else
                {
                    JsonValue nodesJson = Child(scenarioJson, "nodes");
                    string flowRevision = Hash(Encoding.UTF8.GetBytes("topic-flow\n" + scenario.scenarioId + "\n" + CanonicalJson(nodesJson)));
                    FlowRecord flow = BuildCanonicalNodeFlow(package, storeId, scenario.scenarioId, flowRevision,
                        definition.topicId + "/" + scenario.scenarioId, scenario.nodes, Array(nodesJson).ToList());
                    package.flows.Add(flow);
                    flowReference = new ContentRef(storeId, flow.id, flow.revision, flow.semanticHash);
                }

                string conditionJson = IsNull(Child(scenarioJson, "condition")) ? "{}" : CanonicalJson(Child(scenarioJson, "condition"));
                string modifiersJson = IsNull(Child(scenarioJson, "modifiers")) ? "[]" : CanonicalJson(Child(scenarioJson, "modifiers"));
                string channelsJson = IsNull(Child(scenarioJson, "channels")) ? "[]" : CanonicalJson(Child(scenarioJson, "channels"));
                topic.entries.Add(new TopicEntryRecord
                {
                    id = scenario.scenarioId,
                    flowRef = flowReference,
                    priority = scenario.priority,
                    baseWeight = scenario.baseWeight,
                    conditionJson = conditionJson,
                    modifiersJson = modifiersJson,
                    channelsJson = channelsJson,
                    consumptionNodeId = scenario.consumptionNodeId ?? string.Empty
                });
            }

            package.topics.Add(topic);
            AddAnchors(package, topic.id, topic.revision, "topic", definition.anchors, "anchor_");
            metadataJson = "{\"schemaVersion\":" + definition.schemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"topicId\":" + Quote(definition.topicId) + "}";
            return topic.id;
        }

        private static bool ValidateTopicScenarioReferences(TopicDefinition definition, ContentPackage package,
            string storeId, out string error)
        {
            error = string.Empty;
            foreach (TopicScenario scenario in definition.scenarios ?? new List<TopicScenario>())
            {
                if (scenario == null || scenario.scenarioRef == null) continue;
                ContentRef reference = scenario.scenarioRef;
                FlowRecord[] matches = (package.flows ?? new List<FlowRecord>()).Where(flow => flow != null &&
                    flow.id == reference.id && flow.revision == reference.revision && flow.kind == "scenario" &&
                    flow.semanticHash == reference.expectedHash).ToArray();
                if (reference.store != storeId || matches.Length != 1)
                {
                    error = "A Topic scenarioRef must resolve to one exact Scenario Flow revision and hash in this source package store.";
                    return false;
                }
            }
            return true;
        }

        private static bool IsSupportedSourceKind(string kind)
        {
            return kind == ContentSourceKinds.GenericDialogue || kind == ContentSourceKinds.TopicDefinition ||
                kind == ContentSourceKinds.ScenarioDefinition || kind == ContentSourceKinds.DialogueDefinition ||
                kind == ContentSourceKinds.FurnitureDefinition || kind == ContentSourceKinds.ItemDefinition ||
                kind == ContentSourceKinds.FurnitureCatalogueSettings || kind == ContentSourceKinds.ActionDefinition || IsRawDocumentKind(kind);
        }

        private static void ProjectReusableDefinition(ContentPackage package, string storeId, ContentSourceDocument source,
            string sourceHash, JsonValue document, ActivitySequenceBuilder sequenceBuilder, out string sourceId, out string contentRevision,
            out string contentId, out string metadataJson)
        {
            sourceId = ReadString(document, "id", string.Empty);
            contentRevision = sourceHash;
            contentId = ReusableContentDefinitionAdapter.Project(package, storeId, source.kind,
                sourceId, contentRevision, document, sequenceBuilder, out metadataJson);
        }

        private static string AddScenario(ContentPackage package, string storeId, ScenarioDefinition definition,
            JsonValue document, out string contentRevision, out string metadataJson)
        {
            contentRevision = Hash(Encoding.UTF8.GetBytes("scenario\n" + CanonicalJson(document)));
            JsonArray rawSteps = Child(document, "steps") as JsonArray;
            var flow = new FlowRecord
            {
                id = definition.scenarioId,
                revision = contentRevision,
                semanticHash = contentRevision,
                kind = "scenario",
                entryStepId = definition.entryStepId
            };

            for (int index = 0; index < definition.steps.Count; index++)
            {
                ScenarioStep sourceStep = definition.steps[index];
                JsonValue rawStep = rawSteps[index];
                var step = new StepRecord
                {
                    id = sourceStep.id,
                    kind = sourceStep.kind,
                    dataJson = sourceStep.kind == "host_effect" ? CanonicalJson(Child(rawStep, "data")) : CanonicalJson(rawStep)
                };
                if (sourceStep.kind == "dialogue")
                {
                    if (!IsNull(Child(rawStep, "dialogueRef"))) step.dialogueRef = ReadPinnedSourceReference(Child(rawStep, "dialogueRef"));
                    else
                    {
                        DialogueRecord dialogue = BuildScenarioDialogue(package, storeId, definition.scenarioId,
                            sourceStep.id, sourceStep.beats, Array(Child(rawStep, "beats")).ToList());
                        package.dialogues.Add(dialogue);
                        step.dialogueRef = new ContentRef(storeId, dialogue.id, dialogue.revision);
                    }
                }
                else if (sourceStep.kind == "ask")
                {
                    if (!IsNull(Child(rawStep, "dialogueRef"))) step.dialogueRef = ReadPinnedSourceReference(Child(rawStep, "dialogueRef"));
                    else
                    {
                        DialogueRecord dialogue = BuildScenarioDialogue(package, storeId, definition.scenarioId,
                            sourceStep.id + ".question", new[] { sourceStep.question }, new[] { Child(rawStep, "question") });
                        package.dialogues.Add(dialogue);
                        step.dialogueRef = new ContentRef(storeId, dialogue.id, dialogue.revision);
                    }
                }
                else if (sourceStep.kind == "host_effect" || sourceStep.kind == "action" && !IsNull(Child(rawStep, "actionRef")))
                    step.actionRef = ReadPinnedSourceReference(Child(rawStep, "actionRef"));
                else if (sourceStep.kind == "action")
                {
                    string contract = "{\"capabilityId\":" + Quote(sourceStep.capabilityId) +
                        ",\"arguments\":" + CanonicalOrEmptyArray(Child(rawStep, "arguments")) + "}";
                    string actionId = DerivedId("action", definition.scenarioId, sourceStep.id);
                    string actionRevision = Hash(Encoding.UTF8.GetBytes("action\n" + contract));
                    package.actions.Add(new ActionRecord
                    {
                        id = actionId,
                        revision = actionRevision,
                        semanticHash = actionRevision,
                        capabilityId = sourceStep.capabilityId,
                        contractJson = contract
                    });
                    step.actionRef = new ContentRef(storeId, actionId, actionRevision);
                }
                flow.steps.Add(step);
                AddScenarioTransitions(flow, package, storeId, definition.scenarioId, sourceStep, rawStep);
            }

            package.flows.Add(flow);
            metadataJson = "{\"topicId\":" + Quote(definition.topicId) + ",\"entryStepId\":" + Quote(definition.entryStepId) + "}";
            return flow.id;
        }

        private static FlowRecord BuildCanonicalNodeFlow(ContentPackage package, string storeId, string flowId,
            string flowRevision, string sourceId, IList<CanonicalDialogueNode> nodes, IList<JsonValue> rawNodes)
        {
            var flow = new FlowRecord
            {
                id = flowId,
                revision = flowRevision,
                semanticHash = flowRevision,
                kind = "scenario",
                entryStepId = nodes[0].id
            };
            string terminalId = "end_" + Hash(Encoding.UTF8.GetBytes(flowId + "\0terminal")).Substring(0, 24);
            bool needsTerminal = false;
            for (int index = 0; index < nodes.Count; index++)
            {
                CanonicalDialogueNode node = nodes[index];
                JsonValue rawNode = rawNodes[index];
                DialogueRecord dialogue = BuildCanonicalNodeDialogue(package, storeId, sourceId, node, rawNode);
                package.dialogues.Add(dialogue);
                flow.steps.Add(new StepRecord
                {
                    id = node.id,
                    kind = "dialogue",
                    dialogueRef = new ContentRef(storeId, dialogue.id, dialogue.revision),
                    dataJson = CanonicalJson(rawNode)
                });
                if (!string.IsNullOrWhiteSpace(node.nextId))
                    AddTransition(flow, node.id, node.nextId, "next", 0);
                if (node.choices == null) continue;
                for (int choiceIndex = 0; choiceIndex < node.choices.Count; choiceIndex++)
                {
                    CanonicalDialogueChoice choice = node.choices[choiceIndex];
                    string target = choice.nextId;
                    if (string.IsNullOrWhiteSpace(target)) { target = terminalId; needsTerminal = true; }
                    string transitionId = AddTransition(flow, node.id, target, "choice", choiceIndex);
                    ContentRef labelRef = AddText(package, storeId,
                        sourceId + "\0" + node.id + "\0choice\0" + choiceIndex.ToString(CultureInfo.InvariantCulture),
                        choice.label, "en", string.Empty, string.Empty);
                    flow.choices.Add(new ChoiceRecord
                    {
                        stepId = node.id,
                        id = "choice_" + choiceIndex.ToString("D3", CultureInfo.InvariantCulture),
                        transitionId = transitionId,
                        valueJson = "{\"label\":" + Quote(choice.label) + "}",
                        labelTextRef = labelRef
                    });
                }
            }
            if (needsTerminal)
                flow.steps.Add(new StepRecord { id = terminalId, kind = "end", dataJson = "{\"outcome\":\"completed\"}" });
            return flow;
        }

        private static DialogueRecord BuildCanonicalNodeDialogue(ContentPackage package, string storeId,
            string sourceId, CanonicalDialogueNode node, JsonValue rawNode)
        {
            string language = NormalizeLanguage(node.language);
            ContentRef textRef = AddText(package, storeId, sourceId + "\0node\0" + node.id,
                node.text, language, node.displayText, DisplayLanguage(language));
            string presentation = CanonicalPresentation(node.speaker, node.language, node.displayText,
                node.voice, node.audioKey, node.presentation == null ? string.Empty : node.presentation.animation);
            string expression = node.presentation == null ? string.Empty : node.presentation.expression;
            string semantic = "canonical-beat\n" + CanonicalJson(rawNode) + "\n" + textRef.id + "\n" + textRef.revision;
            string revision = Hash(Encoding.UTF8.GetBytes(semantic));
            var dialogue = new DialogueRecord { id = DerivedId("dialogue", sourceId, node.id), revision = revision, semanticHash = revision };
            dialogue.beats.Add(new BeatRecord { id = node.id, sequenceOrder = 0, textRef = textRef, expression = expression, presentationJson = presentation });
            return dialogue;
        }

        private static DialogueRecord BuildScenarioDialogue(ContentPackage package, string storeId, string flowId,
            string stepId, IList<ScenarioBeat> beats, IList<JsonValue> rawBeats)
        {
            if (beats == null || beats.Count == 0 || beats.Count > 16) throw new InvalidDataException("Scenario Dialogue must contain one to sixteen beats.");
            var dialogue = new DialogueRecord();
            var semantic = new StringBuilder("scenario-dialogue\n").Append(flowId).Append('\n').Append(stepId).Append('\n');
            for (int index = 0; index < beats.Count; index++)
            {
                ScenarioBeat beat = beats[index];
                JsonValue rawBeat = rawBeats[index];
                string language = NormalizeLanguage(beat.language);
                ContentRef textRef = AddText(package, storeId, flowId + "\0" + stepId + "\0" + beat.id,
                    beat.text, language, beat.displayText, DisplayLanguage(language));
                string presentation = CanonicalPresentation("alterra", beat.language, beat.displayText,
                    beat.voice, beat.audioKey, beat.animation);
                dialogue.beats.Add(new BeatRecord
                {
                    id = beat.id,
                    sequenceOrder = index,
                    textRef = textRef,
                    expression = beat.expression ?? string.Empty,
                    presentationJson = presentation
                });
                semantic.Append(CanonicalJson(rawBeat)).Append('\n').Append(textRef.id).Append('@').Append(textRef.revision).Append('\n');
            }
            dialogue.id = DerivedId("dialogue", flowId, stepId);
            dialogue.revision = Hash(Encoding.UTF8.GetBytes(semantic.ToString()));
            dialogue.semanticHash = dialogue.revision;
            return dialogue;
        }

        private static void AddScenarioTransitions(FlowRecord flow, ContentPackage package, string storeId,
            string flowId, ScenarioStep step, JsonValue rawStep)
        {
            if (step.kind == "dialogue")
                AddTransition(flow, step.id, step.nextStepId, "next", 0);
            else if (step.kind == "ask")
            {
                for (int index = 0; index < step.options.Count; index++)
                {
                    ScenarioQuestionOption option = step.options[index];
                    string transitionId = AddTransition(flow, step.id, option.nextStepId, "option", index);
                    JsonValue rawOption = Array(Child(rawStep, "options")).ElementAt(index);
                    ContentRef labelRef = AddText(package, storeId,
                        flowId + "\0" + step.id + "\0option\0" + index.ToString(CultureInfo.InvariantCulture),
                        option.label, "en", string.Empty, string.Empty);
                    flow.choices.Add(new ChoiceRecord
                    {
                        stepId = step.id,
                        id = option.optionId,
                        transitionId = transitionId,
                        valueJson = "{\"optionId\":" + Quote(option.optionId) + ",\"value\":" + Quote(option.value) + "}",
                        labelTextRef = labelRef
                    });
                    if (IsNull(rawOption)) throw new InvalidDataException("Scenario option source was not preserved.");
                }
                if (step.allowFreeText) AddTransition(flow, step.id, step.freeTextNextStepId, "free_text", 0);
                AddTransition(flow, step.id, step.skipNextStepId, "skip", 0);
                AddTransition(flow, step.id, step.cancelNextStepId, "cancel", 0);
            }
            else if (step.kind == "action" || step.kind == "host_effect" || step.kind == "wait")
            {
                AddTransition(flow, step.id, step.successNextStepId, "success", 0);
                AddTransition(flow, step.id, step.failureNextStepId, "failure", 0);
            }
            else if (step.kind == "branch")
            {
                for (int index = 0; index < step.cases.Count; index++)
                    AddTransition(flow, step.id, step.cases[index].nextStepId, "branch_case", index);
                AddTransition(flow, step.id, step.defaultNextStepId, "branch_default", 0);
            }
        }

        private static string AddTransition(FlowRecord flow, string fromStep, string toStep, string purpose, int ordinal)
        {
            string id = DerivedId("transition", flow.id, fromStep, purpose, ordinal.ToString(CultureInfo.InvariantCulture));
            flow.transitions.Add(new TransitionRecord { id = id, fromStepId = fromStep, toStepId = toStep, purpose = purpose });
            return id;
        }

        private static ContentRef AddText(ContentPackage package, string storeId, string seed,
            string primaryText, string primaryLanguage, string displayText, string displayLanguage)
        {
            string speechLanguage = NormalizeLanguage(primaryLanguage);
            if (!IsLanguage(speechLanguage)) throw new InvalidDataException("A source Text language is unsupported or missing: " + primaryLanguage);
            var translations = new SortedDictionary<string, string>(StringComparer.Ordinal) { [speechLanguage] = primaryText ?? string.Empty };
            string displayLocale = NormalizeLanguage(displayLanguage);
            if (!string.IsNullOrEmpty(displayText) && IsLanguage(displayLocale) && displayLocale != speechLanguage)
                translations[displayLocale] = displayText;
            string semantic = string.Join("\n", translations.Select(pair => pair.Key + "\0" + pair.Value).ToArray());
            string revision = Hash(Encoding.UTF8.GetBytes("text\n" + semantic));
            string id = DerivedId("text", seed);
            var record = new TextRecord { id = id, revision = revision, semanticHash = revision };
            foreach (KeyValuePair<string, string> translation in translations)
                record.translations.Add(new TranslationRecord { language = translation.Key, text = translation.Value });
            package.texts.Add(record);
            return new ContentRef(storeId, id, revision);
        }

        private static void AddAnchors(ContentPackage package, string ownerId, string ownerRevision,
            string ownerKind, IEnumerable<string> values, string rolePrefix)
        {
            int ordinal = 0;
            foreach (string text in values ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(text)) { ordinal++; continue; }
                string textHash = Hash(Encoding.UTF8.GetBytes(text));
                if (!package.anchors.Any(item => item.id == textHash))
                    package.anchors.Add(new AnchorRecord { id = textHash, language = "und", text = text, textHash = textHash });
                package.anchorLinks.Add(new AnchorLinkRecord
                {
                    id = DerivedId("anchorlink", ownerKind, ownerId, ownerRevision, ordinal.ToString(CultureInfo.InvariantCulture)),
                    anchorId = textHash,
                    ownerKind = ownerKind,
                    ownerId = ownerId,
                    ownerRevision = ownerRevision,
                    role = rolePrefix + ordinal.ToString("D4", CultureInfo.InvariantCulture)
                });
                ordinal++;
            }
        }

        private static string CanonicalPresentation(string speaker, string language, string displayText,
            string voice, string audioKey, string animation)
        {
            return "{\"speaker\":" + Quote(speaker) + ",\"language\":" + Quote(language) +
                ",\"displayText\":" + Quote(displayText) + ",\"voice\":" + Quote(voice) +
                ",\"audioKey\":" + Quote(audioKey) + ",\"animation\":" + Quote(animation) + "}";
        }

        private static string CanonicalOrEmptyArray(JsonValue value)
        { return IsNull(value) ? "[]" : CanonicalJson(value); }

        private static string NormalizeLanguage(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string language = value.Trim().ToLowerInvariant();
            if (language == "english") return "en";
            if (language == "japanese") return "ja";
            return language;
        }

        private static string DisplayLanguage(string speechLanguage)
        {
            return speechLanguage == "ja" ? "en" : speechLanguage == "en" ? "en" : "en";
        }

        private static bool IsLanguage(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2 || value.Length > 16) return false;
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z' || character >= '0' && character <= '9' || character == '-')) return false;
            return value[0] >= 'a' && value[0] <= 'z' && value[value.Length - 1] != '-';
        }

        private static string DerivedId(string prefix, params string[] parts)
        {
            string value = prefix + "\0" + string.Join("\0", parts ?? new string[0]);
            return prefix + "_" + Hash(Encoding.UTF8.GetBytes(value)).Substring(0, 48);
        }

    }
}

