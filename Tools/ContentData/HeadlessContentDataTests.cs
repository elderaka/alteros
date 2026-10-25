using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using SQLite;
using AlterOS.Activities;
using AlterOS.Authoring;
using AlterOS.Content;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using AlterOS.Scenarios;
using AlterOS.Recording;
using AlterOS.Stats;
using AlterOS.Persistence;
using AlterOS.Furniture;

public static class HeadlessContentDataTests
{
    private const string LegacyGenericCorpusHash = "3959a5260a21e75ff6da9b1717d787e890bf4d4b1f4427709599e116d0d5afc0";
    private static int passed;
    private static int failed;
    private static Assembly runtime;

    public static int Main(string[] args)
    {
        if (args == null || args.Length < 3 || ((args[0] != "BakeCommand" && args[0] != "InspectArtifact" && args[0] != "ProbeRoutes") && args.Length != 3) ||
            (args[0] == "BakeCommand" && args.Length != 5) || (args[0] == "InspectArtifact" && args.Length != 4) ||
            (args[0] == "ProbeRoutes" && args.Length != 5))
        {
            Console.Error.WriteLine("Expected group, synthetic root, runtime assembly path, and optional bake manifest/output paths.");
            return 2;
        }

        string root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        try
        {
            runtime = Assembly.LoadFrom(Path.GetFullPath(args[2]));
            if (args[0] == "Repository") RepositoryGroup(root);
            else if (args[0] == "Bake") BakeGroup(root);
            else if (args[0] == "Publish") PublishGroup(root);
            else if (args[0] == "Flows") FlowGroup(root);
            else if (args[0] == "Semantics") SemanticGroup(root);
            else if (args[0] == "JsonCompatibility") JsonCompatibilityGroup();
            else if (args[0] == "Authoring") AuthoringGroup(root);
            else if (args[0] == "ReusableDefinitions") ReusableDefinitionGroup(root);
            else if (args[0] == "ContentSchemaMigration") ContentSchemaMigrationGroup(root);
            else if (args[0] == "FurnitureCatalogueBridge") FurnitureCatalogueBridgeGroup(root);
            else if (args[0] == "GteTopicAnchors") GteTopicAnchorsGroup();
            else if (args[0] == "BakeCommand") return BakeCommand(root, args[3], args[4]);
            else if (args[0] == "InspectArtifact") return InspectArtifact(args[3]);
            else if (args[0] == "ProbeRoutes") return ProbeRoutes(args[3], args[4]);
            else throw new InvalidOperationException("Unsupported content-data group: " + args[0]);
            Console.WriteLine("RESULT " + args[0] + ": " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL after " + passed + "/" + (passed + failed) + ": " + exception);
            return 1;
        }
    }

    private static void RepositoryGroup(string root)
    {
        Run("UTF-8 path, schema, immutable IDs, and reopen", () => Utf8SchemaAndReadback(root));
        Run("composite foreign keys reject cross-revision rows", () => CompositeForeignKey(root));
        Run("same immutable revision cannot change payload", () => ImmutableRevisionConflict(root));
        Run("wrong-kind Step is rejected by the schema", () => WrongKindStep(root));
        Run("duplicate Beat order and Text language are rejected", () => DuplicateOrderAndLanguage(root));
        Run("read-only authored store rejects staging without mutation", () => ReadOnlyStore(root));
        Run("corrupt and future stores stay byte-for-byte unchanged", () => InvalidStorePreservation(root));
        Run("cross-store and path-like references fail closed", () => CrossStoreReference(root));
        RepositoryApiSignature();
    }

    private static void BakeGroup(string root)
    {
        Run("Generic dialogue preserves route, graph, choices, and presentation", () => GenericDialogueProjection(root));
        Run("ambient play counters stay private while SQL preserves the authored pool", () => AmbientRuntimeStateIsNotBaked(root));
        Run("Topic import preserves replay, gates, weights, channels, and consumption", () => TopicProjection(root));
        Run("Topic entries reference one exact reusable Scenario Flow without copying nodes", () => TopicScenarioReferenceProjection(root));
        Run("referenced Scenario plan uses reusable compiled Action and Item/Furniture SQL closure", () => TopicScenarioExecutionClosure(root));
        Run("Executable Scenario import preserves registered Action data and step graph", () => ScenarioProjection(root));
        Run("Unknown fields, hash mismatch, and traversal fail closed", () => SourceTrustAndUnknownFields(root));
        Run("EN/JA and ID/EN resolve with documented display and speech fallbacks", () => LanguageResolution(root));
        Run("bake compiles, stages, reopens, and verifies a read-only SQL artifact", () => BakeSqlArtifact(root));
        Run("legacy sequence stays byte-identical and resolves from the pinned SQL release", () => RuntimeContentSourceRoundTrip(root));
        Run("the manifest builder covers every current Dialogue JSON format", () => SourceManifestCoversCurrentDialogueInventory());
        Run("the full authored corpus and baked GTE vectors open as one pinned SQL release", () => FullCorpusRuntimeRelease(root));
        Run("Bake APIs expose an explicit trusted manifest and compiler", BakeApiSignature);
    }

    private static void PublishGroup(string root)
    {
        Run("P3 validator and dependency-closure APIs are present", PublishApiSignature);
        Run("source edits add immutable revisions without replacing retained closures", () => SourceRevisionClosure(root));
        Run("host Action allowlist and typed Flow contracts fail closed", () => HostAndFlowValidation(root));
        Run("shared Rule cycles reject and missing facts remain Unknown", () => RuleValidation(root));
        Run("Subflow cycles and incompatible named exits reject", () => SubflowValidation(root));
        Run("release-head publication uses CAS and requires index readiness", () => PublishCas(root));
    }

    private static void FlowGroup(string root)
    {
        Run("FlowValue preserves types and rejects unknown or ambiguous entity references", FlowValueContracts);
        Run("two Scenario callers reuse one Dialogue and keep independent continuations", SharedDialogueContinuations);
        Run("separate invocation frames keep their own typed inputs and locals", InvocationFramesIsolateInputs);
        Run("nested Subflow returns accepted, declined and cancelled with atomic typed outputs", NestedSubflowOutcomes);
        Run("host Action results bind by type and duplicate reports do not replay effects", ActionResultsAreTypedAndRetrySafe);
        Run("content Scenario keeps one suspended frame and Quit does not auto-resume", ContentPauseAndQuit);
        Run("Scenario journal callbacks carry execution identity and isolate observer failures", ScenarioHistoryCallbacks);
    }

    private static void JsonCompatibilityGroup()
    {
        Run("JSON runtime preserves ISO timestamps, JSON quoting, and trailing-content rejection", JsonRuntimeCompatibility);
    }

    private static void AuthoringGroup(string root)
    {
        Run("authoring v1 migration preserves records and enables four new kinds", () => AuthoringV1MigrationAddsKinds(root));
        Run("SQL snapshots expose the pinned source-document compiler seam", AuthoringSourceApiSignature);
        Run("authoring projection retains exact source bytes and unwraps drafts only for compilation", AuthoringProjectionPreservesSourceBytes);
        Run("SQL Generic bytes preserve the existing semantic-profile corpus hash", () => SnapshotGenericHashMatchesFilesystem(root));
        Run("in-memory source import matches explicit filesystem import", () => AuthoringImportParity(root));
        Run("snapshot bake compiles pinned bytes and records authoring provenance", () => SnapshotBakePinsRevision(root));
        Run("selected incomplete draft blocks snapshot bake before output creation", () => IncompleteDraftBlocksSnapshotBake(root));
        Run("six-kind source validator enforces typed Text and Action authority", ReusableKindAuthoringValidation);
    }

    private static void ReusableKindAuthoringValidation()
    {
        byte[] validItem = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"item.water_glass\",\"displayName\":{\"id\":\"text.water_glass\",\"translations\":[{\"language\":\"en\",\"text\":\"Water glass\"}]},\"stackLimit\":99}");
        object valid = ValidateAuthoringSource("item_definition", validItem);
        Require((bool)valid.GetType().GetField("isValid").GetValue(valid),
            "a minimal owned-Text Item definition was rejected: " + Diagnostics(valid));

        byte[] bothTexts = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"item.water_glass\",\"displayName\":{\"id\":\"text.water_glass\",\"translations\":[{\"language\":\"en\",\"text\":\"Water glass\"}]},\"displayNameRef\":{\"store\":\"2ca2e8ce-4380-481a-84d3-9a1d87b175aa\",\"id\":\"text.water_glass\",\"revision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"expectedHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},\"stackLimit\":99}");
        object invalidTextBinding = ValidateAuthoringSource("item_definition", bothTexts);
        Require(!(bool)invalidTextBinding.GetType().GetField("isValid").GetValue(invalidTextBinding) &&
            Diagnostics(invalidTextBinding).Contains("content_text_binding_xor"),
            "an Item with both owned and pinned display Text was not rejected by the shared validator");

        byte[] unknownGrant = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.grant\",\"capabilityId\":\"player.item.grant\",\"implementationKind\":\"host_effect\",\"data\":{\"functionId\":\"player.item.mint_anything\",\"arguments\":{\"itemRef\":{\"store\":\"2ca2e8ce-4380-481a-84d3-9a1d87b175aa\",\"id\":\"item.water_glass\",\"revision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"expectedHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},\"quantity\":1}}}");
        object invalidFunction = ValidateAuthoringSource("action_definition", unknownGrant);
        Require(!(bool)invalidFunction.GetType().GetField("isValid").GetValue(invalidFunction) &&
            Diagnostics(invalidFunction).Contains("content_function_unregistered"),
            "the source validator accepted an Action that names an unregistered host effect");

        byte[] catalogueSettings = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"test-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}");
        object validSettings = ValidateAuthoringSource("furniture_catalogue_settings", catalogueSettings);
        Require((bool)validSettings.GetType().GetField("isValid").GetValue(validSettings),
            "the Furniture settings source contract was rejected: " + Diagnostics(validSettings));
        object settingsWithDefinitions = ValidateAuthoringSource("furniture_catalogue_settings",
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"test-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[],\"definitions\":[{}]}"));
        Require(!(bool)settingsWithDefinitions.GetType().GetField("isValid").GetValue(settingsWithDefinitions),
            "the global Furniture settings source accepted copied definition bodies");
        object incompatibleUnits = ValidateAuthoringSource("furniture_catalogue_settings",
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"test-v1\",\"lengthUnit\":\"ft\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}"));
        Require(!(bool)incompatibleUnits.GetType().GetField("isValid").GetValue(incompatibleUnits),
            "the Furniture settings source accepted incompatible catalogue units");

        string ruleSettings = "{\"schemaVersion\":1,\"catalogueVersion\":\"test-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[{\"relationshipRuleId\":\"rule.test\",\"kind\":\"seatViewing\",\"pointTypeIds\":[\"seat.sit\"],\"broadPhaseRadiusMeters\":1,\"minimumDistanceMeters\":0,\"maximumDistanceMeters\":2,\"maximumFacingErrorDegrees\":30,\"maximumHeightDifferenceMeters\":1,\"lateralAllowanceMeters\":1}],\"capabilities\":[],\"legacyAliases\":[]}";
        string typoRuleSettings = ruleSettings.Replace("maximumFacingErrorDegrees", "maximumFacingErrorDegree");
        object typoRule = ValidateAuthoringSource("furniture_catalogue_settings", Encoding.UTF8.GetBytes(typoRuleSettings));
        Require(!(bool)typoRule.GetType().GetField("isValid").GetValue(typoRule) && Diagnostics(typoRule).Contains("unsupported_legacy_field"),
            "a misspelled nested Furniture tolerance field was accepted and would deserialize to zero");
        string missingRuleSettings = ruleSettings.Replace("\"maximumFacingErrorDegrees\":30,", string.Empty);
        object missingRule = ValidateAuthoringSource("furniture_catalogue_settings", Encoding.UTF8.GetBytes(missingRuleSettings));
        Require(!(bool)missingRule.GetType().GetField("isValid").GetValue(missingRule) && Diagnostics(missingRule).Contains("furniture_catalogue_rule_field_missing"),
            "a missing required Furniture relationship tolerance was accepted");
        string wrongTypeRuleSettings = ruleSettings.Replace("\"maximumFacingErrorDegrees\":30", "\"maximumFacingErrorDegrees\":\"30\"");
        object wrongTypeRule = ValidateAuthoringSource("furniture_catalogue_settings", Encoding.UTF8.GetBytes(wrongTypeRuleSettings));
        Require(!(bool)wrongTypeRule.GetType().GetField("isValid").GetValue(wrongTypeRule),
            "a string-valued Furniture tolerance was accepted as a number");
        string nonFiniteRuleSettings = ruleSettings.Replace("\"maximumFacingErrorDegrees\":30", "\"maximumFacingErrorDegrees\":1e9999");
        object nonFiniteRule = ValidateAuthoringSource("furniture_catalogue_settings", Encoding.UTF8.GetBytes(nonFiniteRuleSettings));
        Require(!(bool)nonFiniteRule.GetType().GetField("isValid").GetValue(nonFiniteRule),
            "a non-finite Furniture tolerance was accepted");
        string capabilitySettings = ruleSettings.Replace("\"capabilities\":[]",
            "\"capabilities\":[{\"capabilityId\":\"cap.test\",\"requiredPointTypeIds\":[],\"requiredUseIds\":[],\"extra\":true}]");
        object malformedCapability = ValidateAuthoringSource("furniture_catalogue_settings", Encoding.UTF8.GetBytes(capabilitySettings));
        Require(!(bool)malformedCapability.GetType().GetField("isValid").GetValue(malformedCapability) &&
            Diagnostics(malformedCapability).Contains("unsupported_legacy_field"),
            "an unsupported nested Furniture capability field was discarded");
        string aliasSettings = ruleSettings.Replace("\"legacyAliases\":[]",
            "\"legacyAliases\":[{\"legacyId\":\"legacy.test\",\"definitionId\":\"furniture.seat.basic\",\"pointId\":\"seat\",\"extra\":true}]");
        object malformedAlias = ValidateAuthoringSource("furniture_catalogue_settings", Encoding.UTF8.GetBytes(aliasSettings));
        Require(!(bool)malformedAlias.GetType().GetField("isValid").GetValue(malformedAlias) &&
            Diagnostics(malformedAlias).Contains("unsupported_legacy_field"),
            "an unsupported nested Furniture alias field was discarded");
    }

    private static void FurnitureCatalogueBridgeGroup(string root)
    {
        Run("compiled SQL Furniture catalogue preserves global metadata and all eight starters", () => CompiledFurnitureCatalogueRoundTrip(root));
        Run("catalogue identity changes with settings and exact Furniture revisions", () => FurnitureCatalogueFingerprintChanges(root));
        Run("missing settings and altered release refs fail closed", () => FurnitureCatalogueFailsClosed(root));
        Run("fractional Furniture settings validate, import, and manifest under comma-decimal culture", FurnitureCatalogueInvariantNumericCulture);
    }

    private static void FurnitureCatalogueInvariantNumericCulture()
    {
        CultureInfo previousCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            ContentPackage package = BuildFurnitureCataloguePackage(true, null, "12121212-1212-4212-8212-121212121212");
            LegacySourceRecord source = package.legacySources.Single(item => item.kind == ContentSourceKinds.FurnitureCatalogueSettings);
            object authoringValidation = ValidateAuthoringSource(ContentSourceKinds.FurnitureCatalogueSettings, source.sourceBytes);
            Require((bool)authoringValidation.GetType().GetField("isValid").GetValue(authoringValidation),
                "valid fractional settings failed source validation under fr-FR: " + Diagnostics(authoringValidation));

            CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package);
            Require(compiled.IsReady, "valid fractional settings failed in-memory source import/compile under fr-FR: " + Detail(compiled));
            ContentBuildManifest manifest = ContentBuildManifest.Create(package, compiled.release);
            Require(manifest.Validate(out _) &&
                ContentBuildManifest.TryParse(manifest.ToJson(), out ContentBuildManifest parsed, out string error) &&
                parsed.Hash == manifest.Hash,
                "valid fractional settings failed schema-3 manifest validation under fr-FR: " + error);
        }
        finally { Thread.CurrentThread.CurrentCulture = previousCulture; }
    }

    private static void CompiledFurnitureCatalogueRoundTrip(string root)
    {
        ContentPackage package = BuildFurnitureCataloguePackage(true, null, "11111111-1111-4111-8111-111111111111");
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package);
        Require(compiled.IsReady, "Furniture package did not compile: " + Detail(compiled));
        package.buildManifest = ContentBuildManifest.Create(package, compiled.release);
        Require(package.buildManifest.schemaVersion == 3 && package.buildManifest.furnitureCatalogue != null &&
            package.buildManifest.ValidateForPackage(package, out string manifestError),
            "schema-3 manifest failed to pin settings and release refs: " + manifestError);
        Require(ContentBuildManifest.TryParse(package.buildManifest.ToJson(), out ContentBuildManifest parsed, out string parseError) &&
            parsed.Hash == package.buildManifest.Hash, "schema-3 manifest did not round-trip strictly: " + parseError);
        parsed.furnitureCatalogue.settings = null;
        Require(!parsed.Validate(out _), "schema-3 manifest validation accepted null compiled settings");
        ContentBuildManifest.TryParse(package.buildManifest.ToJson(), out ContentBuildManifest partialV3, out _);
        partialV3.authoringSnapshotHash = Hash("partial-v3-provenance");
        Require(!partialV3.Validate(out _), "schema-3 manifest accepted only half of its optional authoring provenance pair");
        JObject nullSettingsManifest = JObject.Parse(package.buildManifest.ToJson());
        nullSettingsManifest["furnitureCatalogue"]["settings"] = null;
        Require(!ContentBuildManifest.TryParse(nullSettingsManifest.ToString(Newtonsoft.Json.Formatting.None), out _, out _),
            "manifest parsing accepted null compiled settings");
        JObject typoManifest = JObject.Parse(package.buildManifest.ToJson());
        JObject manifestRule = (JObject)typoManifest["furnitureCatalogue"]["settings"]["relationshipRules"][0];
        JToken tolerance = manifestRule["maximumFacingErrorDegrees"];
        manifestRule.Remove("maximumFacingErrorDegrees");
        manifestRule["maximumFacingErrorDegree"] = tolerance;
        FurnitureCatalogueDocument defaultedSettings = Newtonsoft.Json.JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(
            typoManifest["furnitureCatalogue"]["settings"].ToString(Newtonsoft.Json.Formatting.None));
        MethodInfo fingerprintMethod = typeof(ContentBuildManifest).GetMethod("ComputeFurnitureCatalogueFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);
        typoManifest["furnitureCatalogue"]["fingerprint"] = (string)fingerprintMethod.Invoke(null,
            new object[] { defaultedSettings, package.buildManifest.furnitureCatalogue.definitions });
        Require(!ContentBuildManifest.TryParse(typoManifest.ToString(Newtonsoft.Json.Formatting.None), out _, out _),
            "schema-3 manifest parsing accepted a misspelled nested settings field even with a matching defaulted fingerprint");

        string databasePath = Path.Combine(root, "furniture-catalogue.sqlite3");
        ContentResult<ContentRepository> opened = ContentRepository.Open(databasePath, ContentStoreMode.ReadWrite, package.storeId);
        Require(opened.IsReady, "synthetic Furniture SQL store did not open: " + Detail(opened));
        ContentResult<string> staged = opened.value.Stage(package);
        Require(staged.IsReady, "synthetic Furniture release did not stage: " + Detail(staged));

        ContentResult<RuntimeContentStore> runtimeStore = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration { databasePath = databasePath });
        Require(runtimeStore.IsReady, "synthetic Furniture release did not reopen: " + Detail(runtimeStore));
        ContentResult<CompiledFurnitureCatalogue> catalogue = runtimeStore.value.ReadFurnitureCatalogue();
        Require(catalogue.IsReady, "compiled catalogue reader failed: " + Detail(catalogue));
        string pinnedFingerprint = catalogue.value.fingerprint;
        string pinnedVersion = catalogue.value.document.catalogueVersion;
        Require(catalogue.value.document.definitions.Count == 8 && catalogue.value.document.relationshipRules.Count == 4 &&
            catalogue.value.document.capabilities.Count == 5 && catalogue.value.document.legacyAliases.Count == 2 &&
            pinnedVersion == package.furnitureCatalogueSettings.catalogueVersion,
            "compiled catalogue omitted starter definitions or global rules/capabilities/aliases");

        FurnitureCatalogueReleaseMetadata exposed = runtimeStore.value.BuildManifest.furnitureCatalogue;
        exposed.settings.catalogueVersion = "caller-forged-v2";
        MethodInfo fingerprintForReplacement = typeof(ContentBuildManifest).GetMethod("ComputeFurnitureCatalogueFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);
        exposed.fingerprint = (string)fingerprintForReplacement.Invoke(null, new object[] { exposed.settings, exposed.definitions });
        ContentResult<CompiledFurnitureCatalogue> afterPublicReplacement = runtimeStore.value.ReadFurnitureCatalogue();
        Require(afterPublicReplacement.IsReady && afterPublicReplacement.value.fingerprint == pinnedFingerprint &&
            afterPublicReplacement.value.document.catalogueVersion == pinnedVersion,
            "changing the exposed settings plus fingerprint altered the privately pinned runtime catalogue");
        catalogue.value.document.catalogueVersion = "caller-mutated-result";
        exposed.settings = null;
        ContentResult<CompiledFurnitureCatalogue> afterNullMutation = runtimeStore.value.ReadFurnitureCatalogue();
        Require(afterNullMutation.IsReady && afterNullMutation.value.fingerprint == pinnedFingerprint &&
            afterNullMutation.value.document.catalogueVersion == pinnedVersion,
            "public manifest nulling or returned-document mutation altered the pinned runtime catalogue");
        runtimeStore.value.BuildManifest.furnitureCatalogue = null;
        Require(runtimeStore.value.ReadFurnitureCatalogue().IsReady,
            "nulling public manifest inspection metadata invalidated the already-opened pinned catalogue");

        string legacyPath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");
        FurnitureCatalogueSnapshot legacy = FurnitureCatalogueRegistry.Load(legacyPath);
        FurnitureCatalogueSnapshot sql = FurnitureCatalogueRegistry.CreateSnapshot(afterNullMutation.value.document);
        FurnitureCatalogueSnapshot compiledSnapshot = FurnitureCatalogueRegistry.CreateCompiledSnapshot(
            afterNullMutation.value.document, afterNullMutation.value.fingerprint);
        FurnitureCatalogueSnapshot invalidFingerprint = FurnitureCatalogueRegistry.CreateCompiledSnapshot(
            afterNullMutation.value.document, "not-a-sha256");
        Require(legacy.catalogueHash == sql.catalogueHash && catalogue.value.fingerprint.Length == 64 &&
            compiledSnapshot.catalogueHash != sql.catalogueHash && compiledSnapshot.definitions.Count == 8 &&
            invalidFingerprint.diagnostics.Any(item => item.code == "compiled_fingerprint_invalid"),
            "the compiled release differs from the canonical legacy catalogue content or has no release fingerprint");
    }

    private static void FurnitureCatalogueFingerprintChanges(string root)
    {
        ContentPackage first = BuildFurnitureCataloguePackage(true, null, "22222222-2222-4222-8222-222222222222");
        CompiledReleaseResult firstRelease = ContentReleaseCompiler.Compile(first);
        Require(firstRelease.IsReady, "base synthetic Furniture catalogue did not compile: " + Detail(firstRelease));
        ContentBuildManifest firstManifest = ContentBuildManifest.Create(first, firstRelease.release);

        ContentPackage changedSetting = BuildFurnitureCataloguePackage(true, "settings", "22222222-2222-4222-8222-222222222222");
        CompiledReleaseResult settingRelease = ContentReleaseCompiler.Compile(changedSetting);
        Require(settingRelease.IsReady, "changed-settings fixture did not compile: " + Detail(settingRelease));
        ContentBuildManifest settingManifest = ContentBuildManifest.Create(changedSetting, settingRelease.release);
        Require(firstManifest.furnitureCatalogue.fingerprint != settingManifest.furnitureCatalogue.fingerprint,
            "changing the catalogue settings did not change its compiled fingerprint");

        ContentPackage changedDefinition = BuildFurnitureCataloguePackage(true, "furniture.seat.basic", "22222222-2222-4222-8222-222222222222");
        CompiledReleaseResult definitionRelease = ContentReleaseCompiler.Compile(changedDefinition);
        Require(definitionRelease.IsReady, "changed-definition fixture did not compile: " + Detail(definitionRelease));
        ContentBuildManifest definitionManifest = ContentBuildManifest.Create(changedDefinition, definitionRelease.release);
        Require(firstManifest.furnitureCatalogue.fingerprint != definitionManifest.furnitureCatalogue.fingerprint,
            "changing one exact Furniture definition revision did not change its compiled fingerprint");
    }

    private static void FurnitureCatalogueFailsClosed(string root)
    {
        ContentPackage withoutSettings = BuildFurnitureCataloguePackage(false, null, "33333333-3333-4333-8333-333333333333");
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(withoutSettings);
        Require(compiled.IsReady, "legacy-compatible release without catalogue settings should still compile: " + Detail(compiled));
        withoutSettings.buildManifest = ContentBuildManifest.Create(withoutSettings, compiled.release);
        Require(withoutSettings.buildManifest.schemaVersion == 1 &&
            ContentBuildManifest.TryParse(withoutSettings.buildManifest.ToJson(), out ContentBuildManifest parsedV1, out string v1Error) &&
            parsedV1.Hash == withoutSettings.buildManifest.Hash,
            "a valid legacy schema-1 manifest did not retain its exact round-trip hash: " + v1Error);
        ContentBuildManifest validV2 = ContentBuildManifest.Create(withoutSettings, compiled.release,
            "99999999-9999-4999-8999-999999999999", Hash("snapshot-v2"));
        Require(validV2.schemaVersion == 2 && validV2.Validate(out _) &&
            ContentBuildManifest.TryParse(validV2.ToJson(), out ContentBuildManifest parsedV2, out string v2Error) &&
            parsedV2.Hash == validV2.Hash,
            "a valid legacy schema-2 manifest did not retain provenance and round-trip hash: " + v2Error);
        validV2.authoringStoreId = string.Empty;
        validV2.authoringSnapshotHash = string.Empty;
        Require(!validV2.Validate(out _), "schema-2 manifest validation allowed both mandatory authoring provenance fields to be empty");
        string path = Path.Combine(root, "furniture-without-settings.sqlite3");
        ContentResult<ContentRepository> opened = ContentRepository.Open(path, ContentStoreMode.ReadWrite, withoutSettings.storeId);
        Require(opened.IsReady && opened.value.Stage(withoutSettings).IsReady, "legacy-compatible release did not stage");
        ContentResult<RuntimeContentStore> runtimeStore = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration { databasePath = path });
        Require(runtimeStore.IsReady, "legacy-compatible runtime did not open");
        ContentResult<CompiledFurnitureCatalogue> unavailable = runtimeStore.value.ReadFurnitureCatalogue();
        Require(!unavailable.IsReady && unavailable.status == ContentStatuses.Unavailable &&
            unavailable.diagnostics.Any(item => item.code == "furniture_catalogue_settings_unavailable"),
            "a release without settings did not return unavailable instead of inventing defaults");

        ContentPackage withSettings = BuildFurnitureCataloguePackage(true, null, "44444444-4444-4444-8444-444444444444");
        CompiledReleaseResult ready = ContentReleaseCompiler.Compile(withSettings);
        ContentBuildManifest manifest = ContentBuildManifest.Create(withSettings, ready.release);
        manifest.furnitureCatalogue.definitions.RemoveAt(manifest.furnitureCatalogue.definitions.Count - 1);
        MethodInfo fingerprintMethod = typeof(ContentBuildManifest).GetMethod("ComputeFurnitureCatalogueFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);
        manifest.furnitureCatalogue.fingerprint = (string)fingerprintMethod.Invoke(null,
            new object[] { manifest.furnitureCatalogue.settings, manifest.furnitureCatalogue.definitions });
        Require(!manifest.ValidateForPackage(withSettings, out _),
            "a missing Furniture reference was accepted against the selected release dependency closure");

        ContentPackage foreignPackage = BuildFurnitureCataloguePackage(true, null, "55555555-5555-4555-8555-555555555555");
        CompiledReleaseResult foreignRelease = ContentReleaseCompiler.Compile(foreignPackage);
        ContentBuildManifest foreignManifest = ContentBuildManifest.Create(foreignPackage, foreignRelease.release);
        foreignManifest.furnitureCatalogue.definitions[0].store = "66666666-6666-4666-8666-666666666666";
        foreignManifest.furnitureCatalogue.fingerprint = (string)fingerprintMethod.Invoke(null,
            new object[] { foreignManifest.furnitureCatalogue.settings, foreignManifest.furnitureCatalogue.definitions });
        Require(!foreignManifest.Validate(out _), "a foreign-store Furniture definition ref was accepted in the manifest");

        ContentPackage mismatchedPackage = BuildFurnitureCataloguePackage(true, null, "77777777-7777-4777-8777-777777777777");
        JObject wrongDefinition = JObject.Parse(mismatchedPackage.furnitureDefinitions[1].definitionJson);
        mismatchedPackage.furnitureDefinitions[0].definitionJson = wrongDefinition.ToString(Newtonsoft.Json.Formatting.None);
        CompiledReleaseResult mismatched = ContentReleaseCompiler.Compile(mismatchedPackage);
        Require(!mismatched.IsReady, "a compiled SQL definition body with the wrong identity/starter set was accepted");

        JObject duplicateSettings = JObject.Parse("{\"schemaVersion\":1,\"catalogueVersion\":\"duplicate-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}");
        byte[] duplicateBytes = Encoding.UTF8.GetBytes(duplicateSettings.ToString(Newtonsoft.Json.Formatting.None));
        var oneSettings = new ContentSourceDocument(ContentSourceKinds.FurnitureCatalogueSettings,
            "Furniture/catalogue-settings.json", "furniture.catalogue", Hash(duplicateBytes), duplicateBytes);
        ContentResult<ContentPackage> duplicateImport = LegacyContentAdapter.ImportDocuments("duplicate-furniture-settings",
            "88888888-8888-4888-8888-888888888888", new[] { oneSettings, oneSettings });
        Require(!duplicateImport.IsReady && DiagnosticCodes(duplicateImport).Contains("furniture_catalogue_settings_duplicate"),
            "the compiler accepted duplicate singleton Furniture settings sources");

        JObject canonical = JObject.Parse(File.ReadAllText(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json")));
        JObject invalidSettings = new JObject();
        foreach (string field in new[] { "schemaVersion", "catalogueVersion", "lengthUnit", "angleUnit", "coordinateConvention", "relationshipRules", "capabilities", "legacyAliases" })
            invalidSettings[field] = canonical[field].DeepClone();
        JObject invalidRule = (JObject)invalidSettings["relationshipRules"][0];
        JToken maximumFacingError = invalidRule["maximumFacingErrorDegrees"];
        invalidRule.Remove("maximumFacingErrorDegrees");
        invalidRule["maximumFacingErrorDegree"] = maximumFacingError;
        byte[] invalidSettingsBytes = Encoding.UTF8.GetBytes(invalidSettings.ToString(Newtonsoft.Json.Formatting.None));
        var invalidSettingsSource = new ContentSourceDocument(ContentSourceKinds.FurnitureCatalogueSettings,
            "Furniture/catalogue-settings.json", "furniture.catalogue", Hash(invalidSettingsBytes), invalidSettingsBytes);
        ContentResult<ContentPackage> rejectedImport = LegacyContentAdapter.ImportDocuments("invalid-furniture-settings",
            "88888888-8888-4888-8888-888888888889", new[] { invalidSettingsSource });
        Require(!rejectedImport.IsReady && DiagnosticCodes(rejectedImport).Contains("unsupported_legacy_field"),
            "the in-memory SQL source importer discarded a typo inside relationship rules");

        ContentPackage rejectedCompilePackage = BuildFurnitureCataloguePackage(true, null, "88888888-8888-4888-8888-888888888880");
        LegacySourceRecord compileSettings = rejectedCompilePackage.legacySources.Single(item => item.kind == ContentSourceKinds.FurnitureCatalogueSettings);
        compileSettings.sourceBytes = invalidSettingsBytes;
        CompiledReleaseResult rejectedCompile = ContentReleaseCompiler.Compile(rejectedCompilePackage);
        Require(!rejectedCompile.IsReady && rejectedCompile.diagnostics.Any(item => item.code == "unsupported_legacy_field"),
            "the compiler accepted altered settings bytes with a typo-defaulted nested relationship tolerance: " + Detail(rejectedCompile));
    }

    private static ContentPackage BuildFurnitureCataloguePackage(bool includeSettings, string changed, string storeId)
    {
        string legacyPath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");
        JObject legacy = JObject.Parse(File.ReadAllText(legacyPath));
        var sources = new List<ContentSourceDocument>();
        JObject settings = new JObject();
        foreach (string field in new[] { "schemaVersion", "catalogueVersion", "lengthUnit", "angleUnit", "coordinateConvention", "relationshipRules", "capabilities", "legacyAliases" })
            settings[field] = legacy[field].DeepClone();
        if (changed == "settings") settings["catalogueVersion"] = "test-catalogue-v2";
        if (includeSettings)
        {
            byte[] settingsBytes = Encoding.UTF8.GetBytes(settings.ToString(Newtonsoft.Json.Formatting.None));
            sources.Add(new ContentSourceDocument(ContentSourceKinds.FurnitureCatalogueSettings,
                "Furniture/catalogue-settings.json", "furniture.catalogue", Hash(settingsBytes), settingsBytes));
        }

        foreach (JObject definition in ((JArray)legacy["definitions"]).OfType<JObject>())
        {
            string id = (string)definition["definitionId"];
            JObject copy = (JObject)definition.DeepClone();
            JObject displayName = new JObject
            {
                ["id"] = (string)copy["displayNameKey"],
                ["translations"] = new JArray(new JObject { ["language"] = "en", ["text"] = id })
            };
            if (changed == id) displayName["translations"][0]["text"] = id + " updated";
            JObject source = new JObject
            {
                ["schemaVersion"] = 1,
                ["id"] = id,
                ["displayName"] = displayName,
                ["definition"] = copy,
                ["functions"] = new JArray()
            };
            byte[] bytes = Encoding.UTF8.GetBytes(source.ToString(Newtonsoft.Json.Formatting.None));
            sources.Add(new ContentSourceDocument(ContentSourceKinds.FurnitureDefinition,
                "Furniture/" + id + ".json", id, Hash(bytes), bytes));
        }

        ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocuments("furniture-catalogue-fixture",
            storeId, sources);
        Require(imported.IsReady, "synthetic Furniture sources did not import: " + Detail(imported));
        return imported.value;
    }

    private static object ValidateAuthoringSource(string kind, byte[] bytes)
    {
        Type validator = runtime.GetType("AlterOS.Content.ContentAuthoringValidator", false);
        Require(validator != null, "the shared ContentAuthoringValidator is missing");
        MethodInfo validate = validator.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(byte[]) }, null);
        Require(validate != null, "ContentAuthoringValidator.Validate(kind,documentBytes) is missing");
        return validate.Invoke(null, new object[] { kind, bytes });
    }

    private static string Diagnostics(object result)
    {
        object values = result.GetType().GetField("diagnostics").GetValue(result);
        var codes = new List<string>();
        foreach (object diagnostic in (IEnumerable)values)
            codes.Add((string)diagnostic.GetType().GetField("code").GetValue(diagnostic));
        return string.Join(",", codes.ToArray());
    }

    private static void ContentSchemaMigrationGroup(string root)
    {
        Run("content schema 4 upgrades through schema 6 and preserves the immutable release and original artifact", () => ContentV4ToV5Migration(root));
    }

    private static void ReusableDefinitionGroup(string root)
    {
        Run("Item definition projects owned Text and the exact source revision", () => ItemDefinitionProjection(root));
        Run("Dialogue, Furniture, host-effect Action and lossless sequence project as typed records", () => ReusableDefinitionProjection(root));
        Run("execution snapshots retain reusable Action, Item and Furniture rows", ExecutionSnapshotPreservesReusableRows);
        Run("trusted default sequence catalog normalizes composer arguments and rejects unknown composers", () => DefaultSequenceCatalogBehavior());
        Run("bark Action resolves an exact Dialogue and includes its Text closure", () => BarkDialogueClosure());
        Run("reusable Scenario Steps pin shared Dialogue and Action with a completed Dialogue gate", () => ReusableScenarioProjection());
        Run("Furniture validator preserves a bound dispenser presentation and rejects an undeclared use", () => DispenserPresentationBinding());
    }

    private static void DispenserPresentationBinding()
    {
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        string definition = "\"definitionId\":\"furniture.water_dispenser\",\"displayNameKey\":\"text.dispenser\",\"category\":\"dispenser\",\"scalePolicy\":\"fixed\",\"defaultScale\":{\"x\":1,\"y\":1,\"z\":1},\"localBounds\":{\"minimum\":{\"x\":-0.25,\"y\":0,\"z\":-0.25},\"maximum\":{\"x\":0.25,\"y\":1,\"z\":0.25}},\"supportKinds\":[\"roomFloor\"],\"blocksNavigation\":true,\"points\":[{\"pointId\":\"tap\",\"pointTypeId\":\"dispenser.tap\",\"localPosition\":{\"x\":0,\"y\":0.8,\"z\":0},\"localFacing\":{\"x\":0,\"y\":0,\"z\":1},\"useIds\":[\"collect\"]}],\"presentationBindings\":[{\"descriptorId\":\"dispenser.collect_mock\",\"pointId\":\"tap\",\"useId\":\"collect\"}]";
        byte[] validBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"furniture.water_dispenser\",\"displayName\":{\"id\":\"text.dispenser\",\"translations\":[{\"language\":\"en\",\"text\":\"Water dispenser\"}]},\"definition\":{" + definition + "},\"functions\":[]}");
        ContentResult<ContentPackage> valid = LegacyContentAdapter.ImportDocuments("dispenser-binding", storeId,
            new[] { new ContentSourceDocument(ContentSourceKinds.FurnitureDefinition, "Furniture/furniture.water_dispenser.json",
                "furniture.water_dispenser", Hash(validBytes), validBytes) });
        Require(valid.IsReady && valid.value.furnitureDefinitions.Single().definitionJson.Contains("presentationBindings") &&
            valid.value.furnitureDefinitions.Single().functionBindings.Count == 0,
            "Furniture presentation binding classification was lost during source validation or SQL projection");
        byte[] invalidBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(validBytes).Replace("\"useId\":\"collect\"}]},\"functions\":[]}",
            "\"useId\":\"missing.use\"}]},\"functions\":[]}"));
        ContentResult<ContentPackage> invalid = LegacyContentAdapter.ImportDocuments("dispenser-binding-invalid", storeId,
            new[] { new ContentSourceDocument(ContentSourceKinds.FurnitureDefinition, "Furniture/furniture.water_dispenser.json",
                "furniture.water_dispenser", Hash(invalidBytes), invalidBytes) });
        Require(!invalid.IsReady && invalid.diagnostics.Any(item => item.code == "unknown_presentation_use_id"),
            "a presentation binding with an undeclared point/use pair was accepted");
    }

    private static void ReusableScenarioProjection()
    {
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        byte[] dialogueBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"dialogue.shared\",\"beats\":[{\"beatId\":\"hello\",\"text\":{\"id\":\"text.shared\",\"translations\":[{\"language\":\"en\",\"text\":\"Hello\"}]},\"expression\":\"Neutral\"}]}");
        byte[] actionBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.power_on\",\"capabilityId\":\"power.set\",\"implementationKind\":\"host_effect\",\"data\":{\"functionId\":\"power.set\",\"arguments\":{\"desiredState\":\"On\"}}}");
        string dialogueHash = Hash(dialogueBytes), actionHash = Hash(actionBytes);
        string dialogueRef = "{\"store\":\"" + storeId + "\",\"id\":\"dialogue.shared\",\"revision\":\"" + dialogueHash + "\",\"expectedHash\":\"" + dialogueHash + "\"}";
        string actionRef = "{\"store\":\"" + storeId + "\",\"id\":\"action.power_on\",\"revision\":\"" + actionHash + "\",\"expectedHash\":\"" + actionHash + "\"}";
        byte[] firstBytes = ScenarioWithPinnedEffect("scenario.one", dialogueRef, actionRef, "intro");
        byte[] secondBytes = ScenarioWithPinnedEffect("scenario.two", dialogueRef, actionRef, "intro");
        var sources = new[]
        {
            new ContentSourceDocument(ContentSourceKinds.DialogueDefinition, "Dialogues/dialogue.shared.json", "dialogue.shared", dialogueHash, dialogueBytes),
            new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.power_on.json", "action.power_on", actionHash, actionBytes),
            new ContentSourceDocument(ContentSourceKinds.ScenarioDefinition, "Scenarios/scenario.one.json", "scenario.one", Hash(firstBytes), firstBytes),
            new ContentSourceDocument(ContentSourceKinds.ScenarioDefinition, "Scenarios/scenario.two.json", "scenario.two", Hash(secondBytes), secondBytes)
        };
        ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocuments("reusable-scenarios", storeId, sources);
        Require(imported.IsReady, "pinned Scenario sources did not project: " + Detail(imported));
        Require(imported.value.dialogues.Count == 1 && imported.value.actions.Count == 1 && imported.value.flows.Count == 2 &&
            imported.value.flows.All(flow => flow.steps.Single(step => step.kind == "dialogue").dialogueRef.id == "dialogue.shared" &&
                flow.steps.Single(step => step.kind == "host_effect").actionRef.id == "action.power_on"),
            "two Scenarios duplicated or changed their pinned reusable definitions");
        var vocabulary = new ContentHostVocabulary(new[] { "power.set" }, new[] { "Neutral" }, new string[0], new string[0], new string[0]);
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(imported.value, vocabulary);
        Require(compiled.IsReady && compiled.release.dependencies.Count(item => item.kind == "dialogue" && item.id == "dialogue.shared") == 1 &&
            compiled.release.dependencies.Count(item => item.kind == "action" && item.id == "action.power_on") == 1,
            "compiled release did not retain one exact shared Dialogue and Action dependency");

        string invalidScenarioJson = "{\"scenarioId\":\"scenario.invalid\",\"topicId\":\"topic.test\",\"entryStepId\":\"action_gate\",\"steps\":[" +
            "{\"id\":\"action_gate\",\"kind\":\"action\",\"actionRef\":" + actionRef + "}," +
            "{\"id\":\"effect\",\"kind\":\"host_effect\",\"actionRef\":" + actionRef + ",\"data\":{\"schemaVersion\":1,\"requiresCompletedStepId\":\"action_gate\"},\"successNextStepId\":\"done\",\"failureNextStepId\":\"done\"}," +
            "{\"id\":\"done\",\"kind\":\"end\",\"outcome\":\"completed\"}] }";
        byte[] invalidBytes = Encoding.UTF8.GetBytes(invalidScenarioJson);
        ContentResult<ContentPackage> invalid = LegacyContentAdapter.ImportDocuments("reusable-scenarios-invalid", storeId, new[]
        {
            sources[0], sources[1], new ContentSourceDocument(ContentSourceKinds.ScenarioDefinition,
                "Scenarios/scenario.invalid.json", "scenario.invalid", Hash(invalidBytes), invalidBytes)
        });
        Require(!invalid.IsReady && invalid.diagnostics.Any(item => item.code == "scenario_host_effect_prerequisite_invalid"),
            "an Action Step was incorrectly accepted as the required completed Dialogue prerequisite");

        string askScenarioJson = "{\"scenarioId\":\"scenario.ask_invalid\",\"topicId\":\"topic.test\",\"entryStepId\":\"ask\",\"steps\":[" +
            "{\"id\":\"ask\",\"kind\":\"ask\",\"dialogueRef\":" + dialogueRef +
                ",\"options\":[{\"optionId\":\"yes\",\"label\":\"Yes\",\"value\":\"yes\",\"nextStepId\":\"effect\"}]," +
                "\"allowFreeText\":false,\"skipNextStepId\":\"effect\",\"cancelNextStepId\":\"effect\"}," +
            "{\"id\":\"effect\",\"kind\":\"host_effect\",\"actionRef\":" + actionRef + ",\"data\":{\"schemaVersion\":1,\"requiresCompletedStepId\":\"ask\"},\"successNextStepId\":\"done\",\"failureNextStepId\":\"done\"}," +
            "{\"id\":\"done\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        byte[] askBytes = Encoding.UTF8.GetBytes(askScenarioJson);
        ContentResult<ContentPackage> invalidAsk = LegacyContentAdapter.ImportDocuments("reusable-scenarios-ask-invalid", storeId, new[]
        {
            sources[0], sources[1], new ContentSourceDocument(ContentSourceKinds.ScenarioDefinition,
                "Scenarios/scenario.ask_invalid.json", "scenario.ask_invalid", Hash(askBytes), askBytes)
        });
        Require(!invalidAsk.IsReady && invalidAsk.diagnostics.Any(item => item.code == "scenario_host_effect_prerequisite_invalid"),
            "an Ask whose option, skip and cancel routes all reach the effect was accepted as a completed Dialogue gate");
    }

    private static byte[] ScenarioWithPinnedEffect(string scenarioId, string dialogueRef, string actionRef, string prerequisiteId)
    {
        string json = "{\"scenarioId\":\"" + scenarioId + "\",\"topicId\":\"topic.test\",\"entryStepId\":\"intro\",\"steps\":[" +
            "{\"id\":\"intro\",\"kind\":\"dialogue\",\"dialogueRef\":" + dialogueRef + ",\"nextStepId\":\"effect\"}," +
            "{\"id\":\"effect\",\"kind\":\"host_effect\",\"actionRef\":" + actionRef + ",\"data\":{\"schemaVersion\":1,\"requiresCompletedStepId\":\"" + prerequisiteId + "\"},\"successNextStepId\":\"done\",\"failureNextStepId\":\"done\"}," +
            "{\"id\":\"done\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static void DefaultSequenceCatalogBehavior()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActivitySequenceBuilder builder = catalog.CreateBuilder();
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        byte[] composedBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.wait\",\"capabilityId\":\"scenario.complete\",\"implementationKind\":\"activity_sequence\",\"data\":{\"composerId\":\"sequence.wait_then_complete.v1\",\"composerArguments\":[{\"name\":\"durationSeconds\",\"value\":{\"type\":\"double\",\"literal\":2.5}}]}}");
        byte[] authoredBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.wait.authored\",\"capabilityId\":\"scenario.complete\",\"implementationKind\":\"activity_sequence\",\"data\":{\"steps\":[{\"stepId\":\"wait\",\"primitiveId\":\"wait\",\"arguments\":[{\"name\":\"durationSeconds\",\"value\":{\"type\":\"double\",\"literal\":2.5}}],\"resultBindings\":[],\"successNextStepId\":\"complete\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"},{\"stepId\":\"complete\",\"primitiveId\":\"complete\",\"arguments\":[],\"resultBindings\":[],\"successNextStepId\":\"\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"}]}}");
        var sources = new[]
        {
            new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.wait.json", "action.wait", Hash(composedBytes), composedBytes),
            new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.wait.authored.json", "action.wait.authored", Hash(authoredBytes), authoredBytes)
        };
        ContentResult<ContentPackage> projected = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("sequence-default", storeId, sources, builder);
        Require(projected.IsReady, "trusted default composer did not project: " + Detail(projected));
        ActionRecord composed = projected.value.actions.Single(item => item.id == "action.wait");
        ActionRecord authored = projected.value.actions.Single(item => item.id == "action.wait.authored");
        Require(composed.sequenceSteps.Count == authored.sequenceSteps.Count && composed.sequenceSteps.Zip(authored.sequenceSteps,
            (left, right) => left.primitiveId == right.primitiveId && left.argumentsJson == right.argumentsJson &&
                left.successEdgeJson == right.successEdgeJson && left.checkpointPolicy == right.checkpointPolicy).All(value => value),
            "trusted default composer and equivalent authored steps did not normalize to identical rows");
        string[] registeredSequenceCapabilities = catalog.Primitives.All.Select(item => item.requiredCapabilityId)
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        var vocabulary = new ContentHostVocabulary(registeredSequenceCapabilities, new string[0], new string[0], new string[0],
            new string[0], builder);
        Require(ContentReleaseCompiler.Compile(projected.value, vocabulary).IsReady,
            "the trusted catalog capability allowlist did not compile its registered default sequence");
        byte[] unknownBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.unknown\",\"capabilityId\":\"scenario.complete\",\"implementationKind\":\"activity_sequence\",\"data\":{\"composerId\":\"sequence.unknown\"}}");
        ContentResult<ContentPackage> unknown = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("sequence-unknown", storeId,
            new[] { new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.unknown.json", "action.unknown", Hash(unknownBytes), unknownBytes) }, builder);
        Require(!unknown.IsReady && unknown.diagnostics.Any(item => item.code == "sequence_composer_unregistered"),
            "an unknown composer was accepted by the trusted catalog");
        byte[] unknownPrimitiveBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.unknown.primitive\",\"capabilityId\":\"scenario.complete\",\"implementationKind\":\"activity_sequence\",\"data\":{\"steps\":[{\"stepId\":\"bad\",\"primitiveId\":\"unknown.primitive\",\"arguments\":[],\"resultBindings\":[],\"successNextStepId\":\"\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"}]}}");
        ContentResult<ContentPackage> unknownPrimitive = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("sequence-unknown-primitive", storeId,
            new[] { new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.unknown.primitive.json",
                "action.unknown.primitive", Hash(unknownPrimitiveBytes), unknownPrimitiveBytes) }, builder);
        Require(!unknownPrimitive.IsReady && unknownPrimitive.diagnostics.Any(item => item.code == "sequence_primitive_unregistered"),
            "an unregistered Activity primitive was accepted by the trusted catalog");
    }

    private static void BarkDialogueClosure()
    {
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActivitySequenceBuilder builder = catalog.CreateBuilder();
        string[] capabilities = catalog.Primitives.All.Select(item => item.requiredCapabilityId)
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        var vocabulary = new ContentHostVocabulary(capabilities, new[] { "Neutral" }, new string[0], new string[0],
            new string[0], builder);
        byte[] dialogueBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"dialogue.bark.hello\",\"beats\":[{\"beatId\":\"hello\",\"text\":{\"id\":\"text.bark.hello\",\"translations\":[{\"language\":\"en\",\"text\":\"Hello!\"}]},\"expression\":\"Neutral\"}]}");
        string dialogueRevision = Hash(dialogueBytes);
        byte[] actionBytes = BarkSequenceAction("action.bark.hello", "dialogue.bark.hello");
        var actionSource = new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.bark.hello.json",
            "action.bark.hello", Hash(actionBytes), actionBytes);
        var dialogueSource = new ContentSourceDocument(ContentSourceKinds.DialogueDefinition, "Dialogues/dialogue.bark.hello.json",
            "dialogue.bark.hello", dialogueRevision, dialogueBytes);
        ContentResult<ContentPackage> missing = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("bark-missing", storeId,
            new[] { actionSource }, builder);
        Require(missing.IsReady && !ContentReleaseCompiler.Compile(missing.value, vocabulary).IsReady &&
            ContentPackageValidator.Validate(missing.value, vocabulary).diagnostics.Any(item => item.code == "sequence_dialogue_missing"),
            "bark compiled even though its exact Dialogue ID was outside the selected package");

        ContentResult<ContentPackage> valid = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("bark-valid", storeId,
            new[] { actionSource, dialogueSource }, builder);
        CompiledReleaseResult compiled = valid.IsReady ? ContentReleaseCompiler.Compile(valid.value, vocabulary) : null;
        Require(compiled != null && compiled.IsReady && compiled.release.dependencies.Any(item => item.kind == "dialogue" &&
                item.id == "dialogue.bark.hello" && item.revision == dialogueRevision && item.expectedHash == dialogueRevision) &&
            compiled.release.dependencies.Any(item => item.kind == "text" && item.id == "text.bark.hello"),
            "bark did not pin its unique Dialogue revision and transitive Text closure: " + (valid.IsReady ? Detail(compiled) : Detail(valid)));

        ContentResult<ContentPackage> ambiguous = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("bark-ambiguous", storeId,
            new[] { actionSource, dialogueSource }, builder);
        string secondRevision = Hash("second immutable Dialogue revision");
        DialogueRecord duplicate = ambiguous.value.dialogues.Single();
        ambiguous.value.dialogues.Add(new DialogueRecord
        {
            id = duplicate.id, revision = secondRevision, semanticHash = secondRevision,
            beats = duplicate.beats.Select(beat => new BeatRecord { id = beat.id, sequenceOrder = beat.sequenceOrder,
                textRef = beat.textRef, expression = beat.expression, presentationJson = beat.presentationJson }).ToList()
        });
        ContentPackageValidationResult ambiguousValidation = ContentPackageValidator.Validate(ambiguous.value, vocabulary);
        Require(!ambiguousValidation.isValid && ambiguousValidation.diagnostics.Any(item => item.code == "sequence_dialogue_ambiguous"),
            "bark accepted two revisions with the same Dialogue ID");
    }

    private static byte[] BarkSequenceAction(string actionId, string dialogueId)
    {
        string json = "{\"schemaVersion\":1,\"id\":\"" + actionId + "\",\"capabilityId\":\"dialogue.bark\",\"implementationKind\":\"activity_sequence\",\"data\":{\"steps\":[" +
            "{\"stepId\":\"bark\",\"primitiveId\":\"bark\",\"arguments\":[{\"name\":\"dialogueId\",\"value\":{\"type\":\"text\",\"literal\":\"" + dialogueId + "\"}}]," +
                "\"resultBindings\":[{\"resultName\":\"delivered\",\"bindingId\":\"delivered\"}],\"successNextStepId\":\"complete\",\"failureNextStepId\":\"complete\",\"cancelNextStepId\":\"complete\",\"checkpointPolicy\":\"none\"}," +
            "{\"stepId\":\"complete\",\"primitiveId\":\"complete\",\"arguments\":[],\"resultBindings\":[],\"successNextStepId\":\"\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"}]}}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static void ItemDefinitionProjection(string root)
    {
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        byte[] bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"item.water_glass\",\"displayName\":{\"id\":\"text.water_glass\",\"translations\":[{\"language\":\"en\",\"text\":\"Water glass\"},{\"language\":\"ja\",\"text\":\"水のグラス\"}]},\"stackLimit\":99}");
        var source = new ContentSourceDocument(ContentSourceKinds.ItemDefinition, "Items/item.water_glass.json",
            "item.water_glass", Hash(bytes), bytes);
        ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocuments("item-projection", storeId, new[] { source });
        Require(imported.IsReady, "the Item definition was not projected: " + Detail(imported));
        object package = imported.value;
        System.Collections.IList items = (System.Collections.IList)package.GetType().GetField("items").GetValue(package);
        Require(items.Count == 1, "the Item projection did not create exactly one typed record");
        object item = items[0];
        Require((string)item.GetType().GetField("id").GetValue(item) == "item.water_glass" &&
            (string)item.GetType().GetField("revision").GetValue(item) == Hash(bytes) &&
            Convert.ToInt64(item.GetType().GetField("stackLimit").GetValue(item), CultureInfo.InvariantCulture) == 99,
            "the Item ID, immutable source revision, or stack limit changed during projection");
        object displayRef = item.GetType().GetField("displayTextRef").GetValue(item);
        Require(displayRef != null && (string)displayRef.GetType().GetField("store").GetValue(displayRef) == storeId &&
            (string)displayRef.GetType().GetField("id").GetValue(displayRef) == "text.water_glass",
            "owned display Text did not compile to a stable local Text ContentRef");
        Require(imported.value.texts.Count == 1 && imported.value.texts[0].translations.Count == 2,
            "owned display Text did not preserve both localized translations");
    }

    private static void ReusableDefinitionProjection(string root)
    {
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        byte[] itemBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"item.water_glass\",\"displayName\":{\"id\":\"text.water_glass\",\"translations\":[{\"language\":\"en\",\"text\":\"Water glass\"}]},\"stackLimit\":99}");
        string itemRevision = Hash(itemBytes);
        byte[] dialogueBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"dialogue.cheer\",\"beats\":[{\"beatId\":\"cheer\",\"text\":{\"id\":\"text.cheer\",\"translations\":[{\"language\":\"en\",\"text\":\"Here, cheer!\"}]},\"expression\":\"Neutral\"}]}");
        byte[] furnitureBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"furniture.tv.basic\",\"displayName\":{\"id\":\"text.tv\",\"translations\":[{\"language\":\"en\",\"text\":\"TV\"}]},\"definition\":{\"definitionId\":\"furniture.tv.basic\",\"displayNameKey\":\"text.tv\",\"category\":\"tv\",\"scalePolicy\":\"fixed\",\"defaultScale\":{\"x\":1,\"y\":1,\"z\":1},\"localBounds\":{\"minimum\":{\"x\":-0.5,\"y\":0,\"z\":-0.25},\"maximum\":{\"x\":0.5,\"y\":1,\"z\":0.25}},\"supportKinds\":[\"roomFloor\"],\"blocksNavigation\":true,\"points\":[{\"pointId\":\"screen\",\"pointTypeId\":\"tv.screen\",\"localPosition\":{\"x\":0,\"y\":0.5,\"z\":0},\"localFacing\":{\"x\":0,\"y\":0,\"z\":-1},\"useIds\":[\"interact\"]}],\"functionBindings\":[{\"functionId\":\"power.set\",\"pointId\":\"screen\",\"useId\":\"interact\"}]},\"functions\":[{\"functionId\":\"power.set\",\"pointId\":\"screen\",\"useId\":\"interact\",\"contractVersion\":1,\"contract\":{\"desiredState\":[\"Off\",\"On\"]}}]}");
        byte[] hostActionBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.grant_water\",\"capabilityId\":\"player.item.grant\",\"implementationKind\":\"host_effect\",\"data\":{\"functionId\":\"player.item.grant\",\"arguments\":{\"itemRef\":{\"store\":\"" + storeId + "\",\"id\":\"item.water_glass\",\"revision\":\"" + itemRevision + "\",\"expectedHash\":\"" + itemRevision + "\"},\"quantity\":1}}}");
        byte[] sequenceBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.locate_tv\",\"capabilityId\":\"locate\",\"implementationKind\":\"activity_sequence\",\"data\":{\"steps\":[{\"stepId\":\"reserve\",\"primitiveId\":\"reserve_nearest\",\"arguments\":[],\"resultBindings\":[{\"resultName\":\"target\",\"bindingId\":\"tv_target\"}],\"successNextStepId\":\"approach\",\"failureNextStepId\":\"end\",\"cancelNextStepId\":\"end\",\"checkpointPolicy\":\"after\"},{\"stepId\":\"approach\",\"primitiveId\":\"move_to\",\"arguments\":[{\"name\":\"target\",\"value\":{\"bindingId\":\"tv_target\"}}],\"resultBindings\":[{\"resultName\":\"arrived\",\"bindingId\":\"tv_arrived\"}],\"successNextStepId\":\"bark\",\"failureNextStepId\":\"end\",\"cancelNextStepId\":\"end\",\"checkpointPolicy\":\"after\"},{\"stepId\":\"bark\",\"primitiveId\":\"bark\",\"arguments\":[{\"name\":\"dialogueId\",\"value\":{\"type\":\"text\",\"literal\":\"dialogue.cheer\"}}],\"resultBindings\":[],\"successNextStepId\":\"end\",\"failureNextStepId\":\"end\",\"cancelNextStepId\":\"end\",\"checkpointPolicy\":\"none\"},{\"stepId\":\"end\",\"primitiveId\":\"complete\",\"arguments\":[],\"resultBindings\":[],\"successNextStepId\":\"\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"}]}}");
        byte[] composerActionBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"action.locate_tv.composed\",\"capabilityId\":\"locate\",\"implementationKind\":\"activity_sequence\",\"data\":{\"composerId\":\"fixture.locate_tv\"}}");
        var sources = new[]
        {
            new ContentSourceDocument(ContentSourceKinds.ItemDefinition, "Items/item.water_glass.json", "item.water_glass", Hash(itemBytes), itemBytes),
            new ContentSourceDocument(ContentSourceKinds.DialogueDefinition, "Dialogues/dialogue.cheer.json", "dialogue.cheer", Hash(dialogueBytes), dialogueBytes),
            new ContentSourceDocument(ContentSourceKinds.FurnitureDefinition, "Furniture/furniture.tv.basic.json", "furniture.tv.basic", Hash(furnitureBytes), furnitureBytes),
            new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.grant_water.json", "action.grant_water", Hash(hostActionBytes), hostActionBytes),
            new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.locate_tv.json", "action.locate_tv", Hash(sequenceBytes), sequenceBytes),
            new ContentSourceDocument(ContentSourceKinds.ActionDefinition, "Actions/action.locate_tv.composed.json", "action.locate_tv.composed", Hash(composerActionBytes), composerActionBytes)
        };
        ActivitySequenceBuilder sequenceBuilder = BuildTestActivitySequenceBuilder();
        ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocumentsWithSequenceBuilder("reusable-definitions", storeId, sources, sequenceBuilder);
        Require(imported.IsReady, "the reusable definitions did not project: " + Detail(imported));
        ContentPackage package = imported.value;
        Require(package.dialogues.Count == 1 && package.dialogues[0].id == "dialogue.cheer" && package.topics.Count == 0,
            "independent Dialogue was implicitly copied into a Topic or lost its stable identity");
        Require(package.dialogues[0].beats.Count == 1 && package.dialogues[0].beats[0].textRef.id == "text.cheer" &&
            package.texts.Any(item => item.id == "text.cheer" && item.translations.Any(row => row.language == "en" && row.text == "Here, cheer!")),
            "independent Dialogue beat or owned Text was not projected into the existing Dialogue/Text representation");
        Require(package.furnitureDefinitions.Count == 1 && package.furnitureDefinitions[0].id == "furniture.tv.basic" &&
            package.furnitureDefinitions[0].functionBindings.Single().functionId == "power.set" &&
            package.furnitureDefinitions[0].functionBindings.Single().pointId == "screen" &&
            package.furnitureDefinitions[0].functionBindings.Single().useId == "interact" &&
            package.furnitureDefinitions[0].definitionJson.Contains("functionBindings"),
            "Furniture definition or its registered function binding was not retained");
        Require(package.actions.Count == 3 && package.actions.Single(item => item.id == "action.grant_water").functionId == "player.item.grant" &&
            package.actions.Single(item => item.id == "action.grant_water").argumentsJson.Contains(itemRevision),
            "host-effect Action did not retain its pinned Item reference and authored quantity");
        ActionRecord sequence = package.actions.Single(item => item.id == "action.locate_tv");
        Require(sequence.implementationKind == "activity_sequence" && sequence.sequenceSteps.Count == 4 &&
            sequence.sequenceSteps[1].successEdgeJson.Contains("bark") && sequence.sequenceSteps[1].failureEdgeJson.Contains("end") &&
            sequence.sequenceSteps[1].cancelEdgeJson.Contains("end") && sequence.sequenceSteps[1].outputsJson.Contains("tv_arrived") &&
            sequence.sequenceSteps[1].checkpointPolicy == "after",
            "the normalized Action sequence lost one of its three edges, output bindings, or checkpoint policy");
        ActionRecord composed = package.actions.Single(item => item.id == "action.locate_tv.composed");
        Require(composed.sequenceSteps.Count == sequence.sequenceSteps.Count && composed.sequenceSteps.Zip(sequence.sequenceSteps,
                (left, right) => left.ordinal == right.ordinal && left.stepId == right.stepId && left.primitiveId == right.primitiveId &&
                    left.argumentsJson == right.argumentsJson && left.outputsJson == right.outputsJson &&
                    left.successEdgeJson == right.successEdgeJson && left.failureEdgeJson == right.failureEdgeJson &&
                    left.cancelEdgeJson == right.cancelEdgeJson && left.checkpointPolicy == right.checkpointPolicy).All(value => value),
            "a registered composer did not lower to the same normalized sequence rows as data-authored steps");
        foreach (ContentSourceDocument source in sources)
        {
            LegacySourceRecord retained = package.legacySources.Single(item => item.kind == source.kind && item.sourceId == source.sourceId);
            Require(BytesEqual(source.utf8Bytes, retained.sourceBytes), "the importer changed retained authored bytes for " + source.logicalPath);
        }

        string databasePath = Path.Combine(root, "reusable-definitions.sqlite3");
        ContentResult<ContentRepository> opened = ContentRepository.Open(databasePath, ContentStoreMode.ReadWrite, storeId);
        Require(opened.IsReady, "the reusable-definition SQL store did not open: " + Detail(opened));
        ContentResult<string> staged = opened.value.Stage(package);
        Require(staged.IsReady, "the reusable-definition SQL stage failed: " + Detail(staged));
        ContentRef itemRef = new ContentRef(storeId, "item.water_glass", itemRevision, itemRevision);
        ContentResult<ItemRecord> itemRead = opened.value.ReadItem(itemRef);
        Require(itemRead.IsReady && itemRead.value.stackLimit == 99 && itemRead.value.displayTextRef.id == "text.water_glass",
            "the typed Item reader did not return its exact revision and owned Text reference");
        ContentResult<DialogueRecord> dialogueRead = opened.value.ReadDialogue(new ContentRef(storeId, "dialogue.cheer",
            Hash(dialogueBytes), Hash(dialogueBytes)));
        Require(dialogueRead.IsReady && dialogueRead.value.beats.Single().textRef.id == "text.cheer",
            "the typed Dialogue reader did not return its exact beat and Text reference");
        FurnitureDefinitionRecord expectedFurniture = package.furnitureDefinitions.Single();
        ContentResult<FurnitureDefinitionRecord> furnitureRead = opened.value.ReadFurnitureDefinition(new ContentRef(storeId,
            expectedFurniture.id, expectedFurniture.revision, expectedFurniture.semanticHash));
        Require(furnitureRead.IsReady && furnitureRead.value.functionBindings.Single().functionId == "power.set" &&
            furnitureRead.value.functionBindings.Single().pointId == "screen" && furnitureRead.value.functionBindings.Single().useId == "interact" &&
            furnitureRead.value.definitionJson == expectedFurniture.definitionJson,
            "the typed Furniture reader did not round-trip its definition and function binding");
        ContentResult<ActionRecord> actionRead = opened.value.ReadAction(new ContentRef(storeId, "action.locate_tv",
            Hash(sequenceBytes), Hash(sequenceBytes)));
        Require(actionRead.IsReady && actionRead.value.sequenceSteps.Count == 4 &&
            actionRead.value.sequenceSteps[0].successEdgeJson == sequence.sequenceSteps[0].successEdgeJson &&
            actionRead.value.sequenceSteps[0].failureEdgeJson == sequence.sequenceSteps[0].failureEdgeJson &&
            actionRead.value.sequenceSteps[0].cancelEdgeJson == sequence.sequenceSteps[0].cancelEdgeJson &&
            actionRead.value.sequenceSteps[0].outputsJson == sequence.sequenceSteps[0].outputsJson &&
            actionRead.value.sequenceSteps[0].checkpointPolicy == sequence.sequenceSteps[0].checkpointPolicy,
            "the typed Action reader lost sequence edges, output bindings, or checkpoint policy");
        var registeredCapabilityIds = ContentHostEffectFunctionRegistry.RegisteredCapabilityIds
            .Concat(ActivitySequenceCatalogFactory.CreateDefault().Primitives.All.Select(item => item.requiredCapabilityId)
                .Where(item => !string.IsNullOrWhiteSpace(item)))
            .Concat(new[] { "locate" }).Distinct(StringComparer.Ordinal).ToArray();
        var vocabulary = new ContentHostVocabulary(registeredCapabilityIds, new[] { "Neutral" },
            new string[0], new string[0], new string[0], sequenceBuilder);
        Require(vocabulary.IsActionRegistered(ScenarioItemGrantPersistenceAdapter.RegisteredFunctionId) &&
            vocabulary.IsActionRegistered("dialogue.bark") && !vocabulary.IsActionRegistered("player.item.mint_anything"),
            "build vocabulary did not use the registered Item-grant allowlist or accepted an unknown capability");
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package, vocabulary);
        Require(compiled.IsReady, "the reusable six-kind package did not compile: " + Detail(compiled));
        ActionRecord grantAction = package.actions.Single(item => item.functionId == ScenarioItemGrantPersistenceAdapter.RegisteredFunctionId);
        string originalCapability = grantAction.capabilityId;
        grantAction.capabilityId = "dialogue.bark";
        ContentPackageValidationResult mismatchedFunction = ContentPackageValidator.Validate(package, vocabulary);
        grantAction.capabilityId = originalCapability;
        Require(!mismatchedFunction.isValid && mismatchedFunction.diagnostics.Any(item => item.code == "host_effect_capability_mismatch"),
            "a registered but unrelated dialogue.bark capability authorized player.item.grant");
        Require(compiled.release.dependencies.Any(item => item.kind == "item" && item.id == "item.water_glass" &&
                item.revision == itemRevision && item.expectedHash == itemRevision) &&
            compiled.release.dependencies.Any(item => item.kind == "furniture" && item.id == "furniture.tv.basic") &&
            compiled.release.dependencies.Any(item => item.kind == "dialogue" && item.id == "dialogue.cheer"),
            "the compiled release dependency closure omitted an exact Item, Furniture, or independent Dialogue revision");
        ScenarioItemGrantAdapterBehavior(storeId, itemRevision, Hash(hostActionBytes));
        SqlRuntimeGrantIntegration(root);
    }

    private static void SqlRuntimeGrantIntegration(string root)
    {
        string directory = Path.Combine(root, "runtime-sql-grant");
        string authoringPath = Path.Combine(directory, "authoring", "authoring.sqlite3");
        string stagedPath = Path.Combine(directory, "staged", "content.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(authoringPath));
        AuthoringResult<AuthoringRepository> created = AuthoringRepository.CreateNew(authoringPath);
        Require(created.IsReady, "the runtime grant authoring fixture did not initialize: " + Detail(created));
        AuthoringRepository authoring = created.value;
        string storeId = authoring.ReadStoreInfo().value.storeId;
        string itemJson = "{\"schemaVersion\":1,\"id\":\"item.water_glass\",\"displayName\":{\"id\":\"text.water_glass\",\"translations\":[{\"language\":\"en\",\"text\":\"Water glass\"}]},\"stackLimit\":99}";
        byte[] itemBytes = Encoding.UTF8.GetBytes(itemJson);
        string itemRevision = Hash(itemBytes);
        string dialogueJson = "{\"schemaVersion\":1,\"id\":\"dialogue.delivery\",\"beats\":[{\"beatId\":\"delivery\",\"text\":{\"id\":\"text.delivery\",\"translations\":[{\"language\":\"en\",\"text\":\"Here is water.\"}]},\"expression\":\"Neutral\"}]}";
        byte[] dialogueBytes = Encoding.UTF8.GetBytes(dialogueJson);
        string dialogueRevision = Hash(dialogueBytes);
        string actionJson = "{\"schemaVersion\":1,\"id\":\"action.grant_water\",\"capabilityId\":\"player.item.grant\",\"implementationKind\":\"host_effect\",\"data\":{\"functionId\":\"player.item.grant\",\"arguments\":{\"itemRef\":{\"store\":\"" + storeId + "\",\"id\":\"item.water_glass\",\"revision\":\"" + itemRevision + "\",\"expectedHash\":\"" + itemRevision + "\"},\"quantity\":1}}}";
        byte[] actionBytes = Encoding.UTF8.GetBytes(actionJson);
        string actionRevision = Hash(actionBytes);
        string dialogueRef = "{\"store\":\"" + storeId + "\",\"id\":\"dialogue.delivery\",\"revision\":\"" + dialogueRevision + "\",\"expectedHash\":\"" + dialogueRevision + "\"}";
        string actionRef = "{\"store\":\"" + storeId + "\",\"id\":\"action.grant_water\",\"revision\":\"" + actionRevision + "\",\"expectedHash\":\"" + actionRevision + "\"}";
        string scenarioJson = "{\"scenarioId\":\"scenario.fetch_water\",\"topicId\":\"topic.fixture\",\"entryStepId\":\"delivery\",\"steps\":[" +
            "{\"id\":\"delivery\",\"kind\":\"dialogue\",\"dialogueRef\":" + dialogueRef + ",\"nextStepId\":\"grant\"}," +
            "{\"id\":\"grant\",\"kind\":\"host_effect\",\"actionRef\":" + actionRef + ",\"data\":{\"schemaVersion\":1,\"requiresCompletedStepId\":\"delivery\"},\"successNextStepId\":\"done\",\"failureNextStepId\":\"done\"}," +
            "{\"id\":\"done\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        AuthoringSaveRequest[] documents =
        {
            new AuthoringSaveRequest { documentId = "item.water_glass", kind = "item_definition", logicalPath = "Items/item.water_glass.json", operationId = "runtime-item-save", documentBytes = itemBytes },
            new AuthoringSaveRequest { documentId = "dialogue.delivery", kind = "dialogue_definition", logicalPath = "Dialogues/dialogue.delivery.json", operationId = "runtime-dialogue-save", documentBytes = dialogueBytes },
            new AuthoringSaveRequest { documentId = "action.grant_water", kind = "action_definition", logicalPath = "Actions/action.grant_water.json", operationId = "runtime-action-save", documentBytes = actionBytes },
            new AuthoringSaveRequest { documentId = "scenario.fetch_water", kind = "scenario_definition", logicalPath = "Scenarios/scenario.fetch_water.json", operationId = "runtime-scenario-save", documentBytes = Encoding.UTF8.GetBytes(scenarioJson) }
        };
        foreach (AuthoringSaveRequest document in documents)
        {
            AuthoringResult<AuthoringDocument> saved = authoring.Save(document);
            Require(saved.IsReady, "the runtime grant authoring fixture failed to save " + document.documentId + ": " + Detail(saved));
        }
        AuthoringResult<AuthoringSnapshot> snapshot = authoring.ReadSnapshot(documents.Select(item => item.documentId).ToArray());
        Require(snapshot.IsReady, "the runtime grant authoring snapshot did not capture: " + Detail(snapshot));
        authoring.Dispose();
        string metadataHash = Hash("runtime grant synthetic GTE fixture");
        byte[] metadata = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"model\":\"Alibaba-NLP/gte-multilingual-base\",\"modelRevision\":\"synthetic\",\"codeRevision\":\"synthetic\",\"dimension\":1,\"contentHash\":\"" + metadataHash + "\",\"anchors\":[{\"routeId\":\"fixture.route\",\"text\":\"Synthetic runtime grant anchor\"}]}");
        var vocabulary = new ContentHostVocabulary(new[] { ScenarioItemGrantPersistenceAdapter.RegisteredFunctionId },
            new[] { "Neutral" }, new string[0], new string[0], new[] { "fixture.route" });
        ContentResult<ContentBakeArtifact> baked = ContentBakeService.BakeFromSnapshot(snapshot.value, stagedPath,
            new ContentSemanticBuildInput
            {
                metadataUtf8 = metadata, vectorBytes = BitConverter.GetBytes(1f), tokenizerBytes = Encoding.UTF8.GetBytes("{}"),
                expectedContentHash = metadataHash, hostVocabulary = vocabulary
            });
        Require(baked.IsReady, "the runtime SQL grant release did not bake: " + Detail(baked));
        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
        { databasePath = stagedPath, expectedStoreId = storeId, expectedReleaseId = baked.value.releaseId });
        Require(opened.IsReady, "the runtime SQL grant release did not open: " + Detail(opened));
        RuntimeContentStore content = opened.value;
        ContentReleaseDependency flowDependency = content.Release.dependencies.Single(item => item.kind == "flow" && item.id == "scenario.fetch_water");
        ContentResult<FlowRecord> exactFlow = content.ReadFlow(new ContentRef(flowDependency.store, flowDependency.id,
            flowDependency.revision, flowDependency.expectedHash));
        Require(exactFlow.IsReady && exactFlow.value.steps.Single(step => step.id == "grant").actionRef.expectedHash == actionRevision,
            "the exact SQL Flow reader failed to hydrate its pinned Action semantic hash");

        const string runId = "runtime-grant-run", invocationId = "runtime-grant-invocation";
        var game = new FakeItemGrantGameRepository(storeId)
        {
            hostEffectEvidence = new GameScenarioHostEffectEvidenceResult
            {
                state = "found", flowId = flowDependency.id, flowStoreId = flowDependency.store,
                flowRevision = flowDependency.revision, flowHash = flowDependency.expectedHash,
                effectStepId = "grant", effectStepExecutionId = "runtime-grant-effect", completedStepId = "delivery",
                completedStepExecutionId = "runtime-grant-delivery", completedStepOutcome = "completed"
            }
        };

        var adapter = new ScenarioItemGrantPersistenceAdapter(content, game);
        var request = new ScenarioHostEffectRequest(new ContentRef(storeId, "action.grant_water", actionRevision, actionRevision),
            runId, invocationId, "runtime-grant-effect", "delivery", "runtime-grant-delivery");
        ScenarioHostEffectResult applied = adapter.Apply(request);
        GameItemQuantityOperationResult receipt = game.FindItemQuantityOperation(applied.operationId);
        Require(applied.status == ScenarioHostEffectStatuses.Applied && receipt.IsFound && receipt.change.actionStoreId == storeId &&
            receipt.change.actionId == "action.grant_water" && receipt.change.actionRevision == actionRevision &&
            receipt.change.actionExpectedHash == actionRevision && game.quantity == 1,
            "the real SQL RuntimeContentStore-to-grant path did not apply one grant with complete Action provenance: " + applied.reasonCode);
    }

    private static void ScenarioItemGrantAdapterBehavior(string storeId, string itemRevision, string actionRevision)
    {
        const string actionId = "action.grant_water";
        const string itemId = "item.water_glass";
        var reader = new FakeGrantContentReader
        {
            storeId = storeId,
            item = new ItemRecord { id = itemId, revision = itemRevision, semanticHash = itemRevision, stackLimit = 99 },
            flow = new FlowRecord
            {
                id = "flow.authorized", revision = Hash("flow.authorized"), semanticHash = Hash("flow.authorized"), kind = "scenario",
                entryStepId = "delivery",
                steps = new List<StepRecord>
                {
                    new StepRecord { id = "delivery", kind = "dialogue", dialogueRef = new ContentRef(storeId, "dialogue.delivery", Hash("dialogue.delivery"), Hash("dialogue.delivery")) },
                    new StepRecord { id = "grant", kind = "host_effect", actionRef = new ContentRef(storeId, actionId, actionRevision, actionRevision),
                        dataJson = "{\"schemaVersion\":1,\"requiresCompletedStepId\":\"delivery\"}" }
                }
            },
            action = new ActionRecord
            {
                id = actionId, revision = actionRevision, semanticHash = actionRevision, capabilityId = "player.item.grant",
                implementationKind = "host_effect", functionId = "player.item.grant",
                argumentsJson = "{\"itemRef\":{\"store\":\"" + storeId + "\",\"id\":\"" + itemId +
                    "\",\"revision\":\"" + itemRevision + "\",\"expectedHash\":\"" + itemRevision + "\"},\"quantity\":1}"
            }
        };
        var game = new FakeItemGrantGameRepository(storeId);
        game.hostEffectEvidence = new GameScenarioHostEffectEvidenceResult
        {
            state = "found", flowId = "flow.authorized", effectStepId = "grant", completedStepId = "delivery",
            flowStoreId = storeId, flowRevision = Hash("flow.authorized"), flowHash = Hash("flow.authorized"),
            effectStepExecutionId = "step.grant", completedStepExecutionId = "step.delivery",
            completedStepOutcome = "completed"
        };
        var adapter = new ScenarioItemGrantPersistenceAdapter(reader, game);
        var request = new ScenarioHostEffectRequest(new ContentRef(storeId, actionId, actionRevision, actionRevision),
            "run.test", "invocation.test", "step.grant", "delivery", "step.delivery");
        ScenarioHostEffectResult first = adapter.Apply(request);
        Require(first.status == ScenarioHostEffectStatuses.Applied && game.quantity == 1,
            "the registered Item host effect did not make one durable grant");
        FlowRecord originalFlow = reader.flow;
        reader.flow = new FlowRecord
        {
            id = originalFlow.id, revision = Hash("replacement release flow"),
            semanticHash = Hash("replacement release flow"), kind = "scenario",
            steps = new List<StepRecord> { new StepRecord { id = "grant", kind = "host_effect",
                actionRef = new ContentRef(storeId, "action.other", Hash("other action"), Hash("other action")),
                dataJson = "{\"schemaVersion\":1,\"requiresCompletedStepId\":\"delivery\"}" } }
        };
        ScenarioHostEffectResult replay = adapter.Apply(request);
        Require(replay.status == ScenarioHostEffectStatuses.Replayed && game.quantity == 1,
            "a committed grant receipt did not replay after the same Flow ID moved to another release");
        reader.flow = originalFlow;

        game.hostEffectEvidence.flowId = "flow.unrelated";
        ScenarioHostEffectResult unrelatedFlow = adapter.Apply(request);
        Require(unrelatedFlow.status == ScenarioHostEffectStatuses.Replayed && game.quantity == 1,
            "a committed grant receipt did not replay after the current content release changed");
        game.hostEffectEvidence.flowId = "flow.authorized";

        var changedActionRefs = new[]
        {
            new ContentRef("foreign-store", actionId, actionRevision, actionRevision),
            new ContentRef(storeId, "action.grant_other", actionRevision, actionRevision),
            new ContentRef(storeId, actionId, Hash("different immutable Action revision"), actionRevision),
            new ContentRef(storeId, actionId, actionRevision, Hash("different expected Action hash"))
        };
        foreach (ContentRef changedActionRef in changedActionRefs)
        {
            ScenarioHostEffectResult changedAction = adapter.Apply(new ScenarioHostEffectRequest(
                changedActionRef, "run.test", "invocation.test", "step.grant", "delivery", "step.delivery"));
            Require(changedAction.status == ScenarioHostEffectStatuses.Conflict && game.quantity == 1,
                "receipt-first replay accepted a changed Action store, ID, revision, or expected hash");
            Require(adapter.Apply(request).status == ScenarioHostEffectStatuses.Replayed && game.quantity == 1,
                "a changed replay attempt invalidated the exact original Action reference receipt");
        }
        game.hostEffectEvidence.completedStepId = "not_dialogue";
        ScenarioHostEffectResult nonDialogue = adapter.Apply(request);
        Require(nonDialogue.status == ScenarioHostEffectStatuses.Failed && game.quantity == 1,
            "a non-Dialogue completed Step was accepted as host-effect authority");
        game.hostEffectEvidence.completedStepId = "delivery";
        ScenarioHostEffectResult sameExecution = adapter.Apply(new ScenarioHostEffectRequest(
            request.actionRef, "run.test", "invocation.test", "same.execution", "delivery", "same.execution"));
        Require(sameExecution.status == ScenarioHostEffectStatuses.Failed && game.quantity == 1,
            "one execution identity was accepted as both the effect and its completed prerequisite");

        reader.flow.steps[0].kind = "action";
        game.hostEffectEvidence.effectOutcome = string.Empty;
        game.hostEffectEvidence.effectStepExecutionId = "step.grant.second";
        ScenarioHostEffectResult actionPrerequisite = adapter.Apply(new ScenarioHostEffectRequest(
            request.actionRef, "run.test", "invocation.test", "step.grant.second", "delivery", "step.delivery"));
        Require(actionPrerequisite.status == ScenarioHostEffectStatuses.Failed && game.quantity == 1,
            "a compiled Action Step was accepted as the required Dialogue prerequisite");
        reader.flow = originalFlow;
        game.hostEffectEvidence.effectOutcome = string.Empty;
        game.hostEffectEvidence.flowStoreId = string.Empty;
        game.hostEffectEvidence.effectStepExecutionId = "step.grant.unpinned";
        ScenarioHostEffectResult missingPin = adapter.Apply(new ScenarioHostEffectRequest(
            request.actionRef, "run.test", "invocation.test", "step.grant.unpinned", "delivery", "step.delivery"));
        Require(missingPin.status == ScenarioHostEffectStatuses.Failed && missingPin.reasonCode == "scenario_effect_flow_pin_missing" && game.quantity == 1,
            "an unpinned legacy Flow invocation authorized a new Item grant");
    }

    private static ActivitySequenceBuilder BuildTestActivitySequenceBuilder()
    {
        var primitives = new ActivityPrimitiveRegistry(new[]
        {
            new ActivityPrimitiveDescriptor("reserve_nearest", "1", ActivityPrimitiveOperationKind.Pure, "",
                new ActivityArgumentDescriptor[0], new[] { new ActivityResultDescriptor("target", "entity") }),
            new ActivityPrimitiveDescriptor("move_to", "1", ActivityPrimitiveOperationKind.Pure, "",
                new[] { new ActivityArgumentDescriptor("target", "entity", true) },
                new[] { new ActivityResultDescriptor("arrived", "bool") }),
            new ActivityPrimitiveDescriptor("bark", "1", ActivityPrimitiveOperationKind.Pure, "",
                new[] { new ActivityArgumentDescriptor("dialogueId", "text", true) }, new ActivityResultDescriptor[0]),
            new ActivityPrimitiveDescriptor("complete", "1", ActivityPrimitiveOperationKind.Pure, "",
                new ActivityArgumentDescriptor[0], new ActivityResultDescriptor[0])
        });
        return new ActivitySequenceBuilder(primitives, new[] { new LocateTvTestComposer() });
    }

    private static List<ActivitySequenceStep> LocateTvSteps()
    {
        return new List<ActivitySequenceStep>
        {
            new ActivitySequenceStep("reserve", "reserve_nearest", null,
                new[] { new ActivitySequenceResultBinding("target", "tv_target") }, "approach", "end", "end",
                ActivitySequenceCheckpointPolicy.After),
            new ActivitySequenceStep("approach", "move_to",
                new[] { ActivitySequenceArgument.FromBinding("target", "tv_target") },
                new[] { new ActivitySequenceResultBinding("arrived", "tv_arrived") }, "bark", "end", "end",
                ActivitySequenceCheckpointPolicy.After),
            new ActivitySequenceStep("bark", "bark",
                new[] { ActivitySequenceArgument.FromLiteral("dialogueId", FlowValue.Text("dialogue.cheer")) }, null, "end", "end", "end"),
            new ActivitySequenceStep("end", "complete")
        };
    }

    private sealed class LocateTvTestComposer : IActivitySequenceComposer
    {
        public string composerId { get { return "fixture.locate_tv"; } }
        public ActivitySequenceDefinition Compose(ActivitySequenceSource source)
        { return new ActivitySequenceDefinition(source.id, source.revision, LocateTvSteps()); }
    }

    private static void ContentV4ToV5Migration(string root)
    {
        string sourcePath = Path.GetFullPath(Path.Combine("Assets", "StreamingAssets", "Content", "content.sqlite3"));
        Require(File.Exists(sourcePath), "the checked-in schema-4 release fixture is missing");
        byte[] sourceBefore = File.ReadAllBytes(sourcePath);
        string sourceStore;
        string sourceRelease;
        string legacyActionId = string.Empty, legacyActionRevision = string.Empty, legacyActionHash = string.Empty;
        var sourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var connection = new SQLiteConnection(sourcePath, SQLiteOpenFlags.ReadOnly))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 4, "the installed legacy fixture is not content schema 4");
            sourceStore = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key='store_id'");
            sourceRelease = connection.ExecuteScalar<string>("SELECT release_id FROM release_heads WHERE head_name='default'");
            ContentActionFixture action = connection.Query<ContentActionFixture>(
                "SELECT action_id,revision,semantic_hash FROM actions ORDER BY action_id,revision LIMIT 1").FirstOrDefault();
            if (action != null)
            { legacyActionId = action.action_id; legacyActionRevision = action.revision; legacyActionHash = action.semantic_hash; }
            foreach (string table in new[] { "actions", "topics", "flows", "releases", "release_dependencies", "release_heads", "legacy_sources", "anchors", "embeddings" })
                sourceCounts[table] = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM " + table);
        }

        string copyPath = Path.Combine(root, "content-v4-copy.sqlite3");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(copyPath, sourceBefore);
        ContentResult<ContentRepository> opened = ContentRepository.Open(copyPath, ContentStoreMode.ReadWrite, sourceStore);
        Require(opened.IsReady, "a schema-4 store did not upgrade through schema 6: " + Detail(opened));
        foreach (KeyValuePair<string, int> pair in sourceCounts)
            Require(CountRows(copyPath, pair.Key) == pair.Value, "the v4-to-v5 upgrade changed rows in " + pair.Key);
        using (var connection = new SQLiteConnection(copyPath, SQLiteOpenFlags.ReadOnly))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 6,
                "opening a writable v4 store did not transactionally advance it to schema 6");
            foreach (string table in new[] { "items", "furniture_definitions", "furniture_function_bindings", "action_sequence_steps" })
                Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) == 1,
                    "schema 6 is missing its typed content table: " + table);
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "schema migration left a foreign-key violation");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM actions WHERE implementation_kind='legacy_activity'") == sourceCounts["actions"],
                "schema 4 Actions were not preserved as legacy_activity");
            Require(connection.ExecuteScalar<string>("SELECT release_id FROM release_heads WHERE head_name='default'") == sourceRelease,
                "schema migration changed the explicit default release head");
        }

        ContentResult<ContentRepository> oldReadOnly = ContentRepository.Open(sourcePath, ContentStoreMode.ReadOnly, sourceStore);
        Require(oldReadOnly.IsReady, "the original v4 artifact is no longer readable: " + Detail(oldReadOnly));
        if (!string.IsNullOrEmpty(legacyActionId))
        {
            ContentResult<ActionRecord> legacyAction = oldReadOnly.value.ReadAction(
                new ContentRef(sourceStore, legacyActionId, legacyActionRevision, legacyActionHash));
            Require(legacyAction.IsReady && legacyAction.value.implementationKind == "legacy_activity" &&
                legacyAction.value.sequenceSteps.Count == 0,
                "the read-only v4 action API did not expose its migrated legacy_activity interpretation");
        }
        Require(BytesEqual(sourceBefore, File.ReadAllBytes(sourcePath)), "the original v4 artifact changed during copy migration");

        string version5Path = Path.Combine(root, "content-v5-copy.sqlite3");
        File.WriteAllBytes(version5Path, sourceBefore);
        Type schemaType = typeof(ContentRepository).Assembly.GetType("AlterOS.Content.ContentSchema", true);
        System.Reflection.MethodInfo migrate4To5 = schemaType.GetMethod("MigrateVersion4To5",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Require(migrate4To5 != null, "the explicit schema 4 to 5 migration seam is missing");
        using (var connection = new SQLiteConnection(version5Path, SQLiteOpenFlags.ReadWrite))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            migrate4To5.Invoke(null, new object[] { connection });
            string textRevision = new string('a', 64), furnitureRevision = new string('b', 64);
            string textId = "text.v5.tv", furnitureId = "furniture.tv.basic";
            string definitionJson = "{\"definitionId\":\"furniture.tv.basic\",\"category\":\"tv\"}";
            string textRowHash = ContentHashObjectFields(new object[] { textId, textRevision, textRevision });
            string furnitureRowHash = ContentHashObjectFields(new object[] { furnitureId, furnitureRevision, furnitureRevision,
                textId, textRevision, definitionJson });
            string bindingRowHash = ContentHashFields(new[] { "power.set", "1", "{\"desiredState\":[\"Off\",\"On\"]}" });
            connection.Execute("INSERT INTO texts(text_id,revision,semantic_hash,row_hash) VALUES (?,?,?,?)",
                textId, textRevision, textRevision, textRowHash);
            connection.Execute("INSERT INTO furniture_definitions(furniture_id,revision,semantic_hash,display_text_id,display_text_revision,definition_json,row_hash) VALUES (?,?,?,?,?,?,?)",
                furnitureId, furnitureRevision, furnitureRevision, textId, textRevision, definitionJson, furnitureRowHash);
            connection.Execute("INSERT INTO furniture_function_bindings(furniture_id,furniture_revision,function_id,function_contract_version,contract_json,row_hash) VALUES (?,?,?,?,?,?)",
                furnitureId, furnitureRevision, "power.set", 1, "{\"desiredState\":[\"Off\",\"On\"]}", bindingRowHash);
        }
        using (var connection = new SQLiteConnection(version5Path, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5,
                "the schema-v5 compatibility fixture did not stop at version 5");
        ContentResult<ContentRepository> version5ReadOnly = ContentRepository.Open(version5Path, ContentStoreMode.ReadOnly, sourceStore);
        Require(version5ReadOnly.IsReady, "a schema-v5 store did not remain readable without an implicit migration: " + Detail(version5ReadOnly));
        byte[] version5BeforeTypedRead = File.ReadAllBytes(version5Path);
        ContentResult<FurnitureDefinitionRecord> version5Furniture = version5ReadOnly.value.ReadFurnitureDefinition(
            new ContentRef(sourceStore, "furniture.tv.basic", new string('b', 64), new string('b', 64)));
        Require(version5Furniture.IsReady && version5Furniture.value.definitionJson == "{\"definitionId\":\"furniture.tv.basic\",\"category\":\"tv\"}" &&
            version5Furniture.value.functionBindings.Single().functionId == "power.set" &&
            version5Furniture.value.functionBindings.Single().pointId == string.Empty &&
            version5Furniture.value.functionBindings.Single().useId == string.Empty &&
            BytesEqual(version5BeforeTypedRead, File.ReadAllBytes(version5Path)),
            "read-only v5 Furniture compatibility did not preserve the unmapped binding and original bytes: " + Detail(version5Furniture));
        using (var connection = new SQLiteConnection(version5Path, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5,
                "a read-only schema-v5 open performed an implicit migration");
        ContentResult<ContentRepository> version5Upgrade = ContentRepository.Open(version5Path, ContentStoreMode.ReadWrite, sourceStore);
        Require(version5Upgrade.IsReady, "a schema-v5 store did not upgrade to v6: " + Detail(version5Upgrade));
        ContentResult<FurnitureDefinitionRecord> version6Furniture = version5Upgrade.value.ReadFurnitureDefinition(
            new ContentRef(sourceStore, "furniture.tv.basic", new string('b', 64), new string('b', 64)));
        Require(version6Furniture.IsReady && version6Furniture.value.functionBindings.Single().functionId == "power.set" &&
            version6Furniture.value.functionBindings.Single().pointId == string.Empty &&
            version6Furniture.value.functionBindings.Single().useId == string.Empty,
            "v5-to-v6 migration did not preserve legacy Furniture bindings as explicitly unmapped");
        using (var connection = new SQLiteConnection(version5Path, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 6,
                "a writable schema-v5 open did not migrate to schema 6");

        string rollbackPath = Path.Combine(root, "content-v5-rollback.sqlite3");
        File.WriteAllBytes(rollbackPath, sourceBefore);
        using (var connection = new SQLiteConnection(rollbackPath, SQLiteOpenFlags.ReadWrite))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            migrate4To5.Invoke(null, new object[] { connection });
        }
        string flowId, flowRevision;
        using (var connection = new SQLiteConnection(rollbackPath, SQLiteOpenFlags.ReadWrite))
        {
            connection.Execute("PRAGMA foreign_keys=OFF");
            ContentFlowFixture flow = connection.Query<ContentFlowFixture>("SELECT flow_id,revision FROM flows ORDER BY flow_id,revision LIMIT 1").First();
            flowId = flow.flow_id; flowRevision = flow.revision;
            connection.Execute("INSERT INTO steps(flow_id,flow_revision,step_id,kind,dialogue_id,dialogue_revision,action_id,action_revision,called_flow_id,called_flow_revision,data_json,row_hash) " +
                "VALUES (?,?,?,'action',NULL,NULL,?,?,NULL,NULL,'{}',?)", flowId, flowRevision, "rollback_probe",
                "missing.action", new string('0', 64), new string('0', 64));
        }
        System.Reflection.MethodInfo migrate5To6 = schemaType.GetMethod("MigrateVersion5To6",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool migrationRejected = false;
        try
        {
            using (var connection = new SQLiteConnection(rollbackPath, SQLiteOpenFlags.ReadWrite))
            {
                connection.Execute("PRAGMA foreign_keys=ON");
                migrate5To6.Invoke(null, new object[] { connection });
            }
        }
        catch (System.Reflection.TargetInvocationException) { migrationRejected = true; }
        Require(migrationRejected, "schema-v5 migration accepted an invalid pre-existing foreign key");
        using (var connection = new SQLiteConnection(rollbackPath, SQLiteOpenFlags.ReadOnly))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5 &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM steps WHERE step_id='rollback_probe'") == 1 &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 1,
                "a rejected v5-to-v6 migration did not roll back its schema and data changes");
            Require(!connection.Query<ContentColumnFixture>("PRAGMA table_info(furniture_function_bindings)")
                .Any(column => column.name == "point_id"),
                "a rejected v5-to-v6 migration left the Furniture binding table rebuilt");
        }
    }

    private static void AuthoringV1MigrationAddsKinds(string root)
    {
        string directory = Path.Combine(root, "authoring-v1-migration");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "authoring.sqlite3");
        byte[] legacyBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"preserve\":\"exact\"}");
        string legacyHash = Hash(legacyBytes);
        string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        string importedBytes = Encoding.UTF8.GetString(legacyBytes);
        CreateAuthoringV1Fixture(path, storeId, legacyBytes, legacyHash, importedBytes);

        AuthoringResult<AuthoringRepository> opened = AuthoringRepository.OpenExisting(path);
        Require(opened.IsReady, "the v1 authoring store did not migrate and open: " + Detail(opened));
        using (AuthoringRepository repository = opened.value)
        {
            AuthoringResult<AuthoringStoreInfo> info = repository.ReadStoreInfo();
            Require(info.IsReady, "the migrated authoring store info could not be read: " + Detail(info));
            Require(info.value.schemaVersion == 3,
                "opening a v1 authoring store did not advance through the Furniture settings schema to version 3");
            Require(info.value.storeRevision == 7,
                "the schema migration changed the authoring store revision");

            AuthoringResult<AuthoringDocument> document = repository.Read("legacy-generic", legacyHash);
            Require(document.IsReady && BytesEqual(document.value.documentBytes, legacyBytes),
                "the legacy source document bytes/revision changed during migration: " + Detail(document));
            AuthoringResult<AuthoringBuildSet> buildSet = repository.ReadBuildSet("selected");
            Require(buildSet.IsReady && buildSet.value.documentIds.SequenceEqual(new[] { "legacy-generic" }),
                "the existing build-set selection changed during migration: " + Detail(buildSet));

            string[] kinds = { "dialogue_definition", "furniture_definition", "furniture_catalogue_settings", "item_definition", "action_definition" };
            foreach (string kind in kinds)
            {
                string id = kind == "furniture_catalogue_settings" ? "furniture.catalogue" : "new-" + kind;
                string pathName = kind == "furniture_catalogue_settings" ? "Furniture/catalogue-settings.json" : "New/" + id + ".json";
                byte[] bytes = kind == "furniture_catalogue_settings"
                    ? Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"test-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}")
                    : Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"" + id + "\"}");
                AuthoringResult<AuthoringDocument> saved = repository.Save(new AuthoringSaveRequest
                {
                    documentId = id,
                    kind = kind,
                    logicalPath = pathName,
                    expectedHash = string.Empty,
                    operationId = "save-" + kind,
                    documentBytes = bytes
                });
                Require(saved.IsReady && saved.value.kind == kind,
                    "the migrated store rejected new authoring kind " + kind + ": " + Detail(saved));
            }
        }

        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite, true))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 3,
                "the SQLite user_version did not advance to 3");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM metadata WHERE key='schema_version' AND value='3'") == 1,
                "the authoring schema metadata did not advance to 3");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM document_revisions WHERE document_id='legacy-generic' AND revision_hash=? AND document_bytes=?",
                legacyHash, legacyBytes) == 1, "the legacy revision row or exact BLOB bytes were not preserved");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM operation_receipts WHERE operation_id='legacy-save' AND request_hash=? AND result_status='ready'",
                Hash("legacy-save-request")) == 1, "the pre-migration save receipt changed");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM import_receipts WHERE operation_id='legacy-import' AND request_hash=? AND result_status='ready'",
                Hash("legacy-import-request")) == 1, "the pre-migration import receipt changed");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM import_sources WHERE operation_id='legacy-import' AND source_path='Dialogue/Generic/legacy.json' AND original_bytes=?",
                legacyBytes) == 1, "the imported original bytes/source identity changed");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "the migrated authoring schema contains foreign-key violations");
        }
    }

    private static void CreateAuthoringV1Fixture(string path, string storeId, byte[] documentBytes,
        string revisionHash, string importedBytes)
    {
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create, true))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.BeginTransaction();
            connection.Execute("CREATE TABLE metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
            connection.Execute("CREATE TABLE documents (document_id TEXT PRIMARY KEY NOT NULL CHECK(length(document_id) BETWEEN 1 AND 128), kind TEXT NOT NULL CHECK(kind IN ('generic_dialogue','topic_definition','scenario_definition','legacy_sequence','ambient_pool','activity_scenario_library','activity_greeting_menu','conversation_coverage','topic_migration_map','onboarding_steps','topic','scenario')), source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), logical_path TEXT NOT NULL CHECK(length(logical_path) BETWEEN 1 AND 1024), logical_path_key TEXT NOT NULL UNIQUE CHECK(length(logical_path_key) BETWEEN 1 AND 2048), head_hash TEXT NOT NULL CHECK(length(head_hash)=64), created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL)");
            connection.Execute("CREATE INDEX documents_kind_id ON documents(kind,document_id)");
            connection.Execute("CREATE TABLE document_revisions (document_id TEXT NOT NULL, revision_hash TEXT NOT NULL CHECK(length(revision_hash)=64), document_bytes BLOB NOT NULL CHECK(length(document_bytes) BETWEEN 1 AND 4194304), created_utc TEXT NOT NULL, PRIMARY KEY(document_id,revision_hash), FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE operation_receipts (operation_id TEXT PRIMARY KEY NOT NULL CHECK(length(operation_id) BETWEEN 1 AND 128), operation_kind TEXT NOT NULL CHECK(operation_kind IN ('document.save','build_set.save')), request_hash TEXT NOT NULL CHECK(length(request_hash)=64), result_status TEXT NOT NULL CHECK(result_status IN ('ready','invalid','conflict')), document_id TEXT NOT NULL DEFAULT '', revision_hash TEXT NOT NULL DEFAULT '', build_set_name TEXT NOT NULL DEFAULT '', code TEXT NOT NULL DEFAULT '', message TEXT NOT NULL DEFAULT '', result_payload TEXT NOT NULL DEFAULT '', store_revision INTEGER NOT NULL CHECK(store_revision>=0), created_utc TEXT NOT NULL)");
            connection.Execute("CREATE TABLE import_receipts (operation_id TEXT PRIMARY KEY NOT NULL CHECK(length(operation_id) BETWEEN 1 AND 128), request_hash TEXT NOT NULL CHECK(length(request_hash)=64), result_status TEXT NOT NULL CHECK(result_status IN ('ready','invalid','conflict')), source_count INTEGER NOT NULL CHECK(source_count>=0), store_revision INTEGER NOT NULL CHECK(store_revision>=0), code TEXT NOT NULL DEFAULT '', message TEXT NOT NULL DEFAULT '', created_utc TEXT NOT NULL)");
            connection.Execute("CREATE TABLE import_sources (operation_id TEXT NOT NULL, source_path TEXT NOT NULL CHECK(length(source_path) BETWEEN 1 AND 1024), source_hash TEXT NOT NULL CHECK(length(source_hash)=64), document_id TEXT NOT NULL, source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), original_bytes BLOB NOT NULL CHECK(length(original_bytes) BETWEEN 1 AND 4194304), PRIMARY KEY(operation_id,source_path), FOREIGN KEY(operation_id) REFERENCES import_receipts(operation_id) ON DELETE RESTRICT, FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE INDEX import_sources_document ON import_sources(document_id,operation_id)");
            connection.Execute("CREATE TABLE build_sets (name TEXT PRIMARY KEY NOT NULL CHECK(length(name) BETWEEN 1 AND 64), revision_hash TEXT NOT NULL CHECK(length(revision_hash)=64), updated_utc TEXT NOT NULL)");
            connection.Execute("CREATE TABLE build_set_documents (name TEXT NOT NULL, ordinal INTEGER NOT NULL CHECK(ordinal>=0), document_id TEXT NOT NULL, PRIMARY KEY(name,ordinal), UNIQUE(name,document_id), FOREIGN KEY(name) REFERENCES build_sets(name) ON DELETE RESTRICT, FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");

            connection.Execute("INSERT INTO metadata(key,value) VALUES ('schema_version','1')");
            connection.Execute("INSERT INTO metadata(key,value) VALUES ('store_id',?)", storeId);
            connection.Execute("INSERT INTO metadata(key,value) VALUES ('store_revision','7')");
            connection.Execute("INSERT INTO documents(document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc) VALUES ('legacy-generic','generic_dialogue','legacy-source','Generic/legacy.json','generic/legacy.json',?,'2026-10-22T00:00:00Z','2026-10-22T00:00:00Z')", revisionHash);
            connection.Execute("INSERT INTO document_revisions(document_id,revision_hash,document_bytes,created_utc) VALUES ('legacy-generic',?,?, '2026-10-22T00:00:00Z')", revisionHash, documentBytes);
            connection.Execute("INSERT INTO operation_receipts(operation_id,operation_kind,request_hash,result_status,document_id,revision_hash,store_revision,created_utc) VALUES ('legacy-save','document.save',?,'ready','legacy-generic',?,6,'2026-10-22T00:00:00Z')", Hash("legacy-save-request"), revisionHash);
            connection.Execute("INSERT INTO import_receipts(operation_id,request_hash,result_status,source_count,store_revision,created_utc) VALUES ('legacy-import',?,'ready',1,6,'2026-10-22T00:00:00Z')", Hash("legacy-import-request"));
            connection.Execute("INSERT INTO import_sources(operation_id,source_path,source_hash,document_id,source_id,original_bytes) VALUES ('legacy-import','Dialogue/Generic/legacy.json',?,'legacy-generic','legacy-source',?)", Hash(importedBytes), documentBytes);
            connection.Execute("INSERT INTO build_sets(name,revision_hash,updated_utc) VALUES ('selected',?,'2026-10-22T00:00:00Z')", Hash("legacy-build-set"));
            connection.Execute("INSERT INTO build_set_documents(name,ordinal,document_id) VALUES ('selected',0,'legacy-generic')");
            connection.Execute("PRAGMA user_version=1");
            connection.Commit();
        }
    }

    private static void AuthoringSourceApiSignature()
    {
        Type source = runtime.GetType("AlterOS.Content.ContentSourceDocument", false);
        if (source == null) throw new InvalidOperationException("ContentSourceDocument is missing from the runtime assembly.");
        Type adapter = runtime.GetType("AlterOS.Content.LegacyContentAdapter", true);
        MethodInfo import = adapter.GetMethod("ImportDocuments", BindingFlags.Public | BindingFlags.Static);
        if (import == null) throw new InvalidOperationException("LegacyContentAdapter.ImportDocuments is missing.");
        Type authoringDocument = runtime.GetType("AlterOS.Authoring.AuthoringDocument", true);
        Type readOnlySourceList = typeof(IReadOnlyList<>).MakeGenericType(source);
        if (import.GetParameters().Length != 3 || import.GetParameters()[0].ParameterType != typeof(string) ||
            import.GetParameters()[1].ParameterType != typeof(string) || import.GetParameters()[2].ParameterType != readOnlySourceList)
            throw new InvalidOperationException("ImportDocuments must accept package/store IDs and a typed source-document list.");
        Type projection = runtime.GetType("AlterOS.Authoring.AuthoringContentSourceProjection", true);
        MethodInfo project = projection.GetMethod("Project", BindingFlags.Public | BindingFlags.Static, null, new[] { authoringDocument }, null);
        if (project == null) throw new InvalidOperationException("AuthoringContentSourceProjection.Project(AuthoringDocument) is missing.");
        Type snapshot = runtime.GetType("AlterOS.Authoring.AuthoringSnapshot", true);
        Type bake = runtime.GetType("AlterOS.Content.ContentBakeService", true);
        MethodInfo bakeMethod = bake.GetMethod("BakeFromSnapshot", BindingFlags.Public | BindingFlags.Static, null,
            new[] { snapshot, typeof(string), runtime.GetType("AlterOS.Content.ContentSemanticBuildInput", true) }, null);
        if (bakeMethod == null) throw new InvalidOperationException("ContentBakeService.BakeFromSnapshot is missing.");
    }

    private static void AuthoringProjectionPreservesSourceBytes()
    {
        byte[] genericBytes = Encoding.UTF8.GetBytes(GenericRevisionFixture("unmodified unknown metadata"));
        var generic = new AuthoringDocument
        {
            documentId = "generic-projection",
            kind = "generic_dialogue",
            logicalPath = "Generic/projection.json",
            revisionHash = Hash(genericBytes),
            documentBytes = (byte[])genericBytes.Clone()
        };
        AuthoringResult<ContentSourceDocument> projectedGeneric = AuthoringContentSourceProjection.Project(generic);
        Require(projectedGeneric.IsReady && BytesEqual(projectedGeneric.value.utf8Bytes, genericBytes) &&
            projectedGeneric.value.hash == generic.revisionHash,
            "A complete authored source must reach the compiler byte-identically.");

        byte[] settingsBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"projection-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}");
        var settings = new AuthoringDocument
        {
            documentId = "furniture.catalogue",
            sourceId = "furniture.catalogue",
            kind = ContentSourceKinds.FurnitureCatalogueSettings,
            logicalPath = "Furniture/catalogue-settings.json",
            revisionHash = Hash(settingsBytes),
            documentBytes = (byte[])settingsBytes.Clone()
        };
        AuthoringResult<ContentSourceDocument> projectedSettings = AuthoringContentSourceProjection.Project(settings);
        Require(projectedSettings.IsReady && projectedSettings.value.kind == ContentSourceKinds.FurnitureCatalogueSettings &&
            projectedSettings.value.sourceId == "furniture.catalogue" && BytesEqual(projectedSettings.value.utf8Bytes, settingsBytes),
            "The singleton Furniture settings source must enter the existing selected-snapshot compiler input byte-identically.");

        byte[] wrapperBytes = Encoding.UTF8.GetBytes("{\"status\":\"draft\",\"document\":{\"topicId\":\"topic.projection\",\"scenarios\":[]},\"unknown\":{\"kept\":true}}");
        var draft = new AuthoringDocument
        {
            documentId = "draft-projection",
            kind = "topic",
            sourceId = "topic.projection",
            logicalPath = "Drafts/topics/projection.json",
            revisionHash = Hash(wrapperBytes),
            documentBytes = (byte[])wrapperBytes.Clone()
        };
        AuthoringResult<ContentSourceDocument> projectedDraft = AuthoringContentSourceProjection.Project(draft);
        Require(projectedDraft.IsReady && projectedDraft.value.kind == ContentSourceKinds.TopicDefinition &&
            Encoding.UTF8.GetString(projectedDraft.value.utf8Bytes) == "{\"topicId\":\"topic.projection\",\"scenarios\":[]}" &&
            BytesEqual(draft.documentBytes, wrapperBytes) && draft.revisionHash == Hash(wrapperBytes),
            "A draft wrapper must remain intact in authoring storage while its inner document is projected for compilation.");

        draft.documentBytes = Encoding.UTF8.GetBytes("{\"status\":\"draft\"}");
        draft.revisionHash = Hash(draft.documentBytes);
        AuthoringResult<ContentSourceDocument> incomplete = AuthoringContentSourceProjection.Project(draft);
        Require(!incomplete.IsReady && incomplete.diagnostics.Any(item => item.code == "authoring_draft_document_missing"),
            "A selected draft without its typed content document must fail closed.");
    }

    private static void SnapshotGenericHashMatchesFilesystem(string root)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(GenericRevisionFixture("cached vector profile parity"));
        string genericRoot = Path.Combine(root, "generic-hash", "Generic");
        string sourcePath = Path.Combine(genericRoot, "nested", "one.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
        File.WriteAllBytes(sourcePath, bytes);
        ContentResult<string> filesystem = ContentSourceManifestBuilder.ComputeGenericCorpusHash(genericRoot);
        var document = new ContentSourceDocument("generic_dialogue", "Generic/nested/one.json", "generic.profile",
            Hash(bytes), bytes);
        ContentResult<string> snapshot = ContentSourceManifestBuilder.ComputeGenericCorpusHashFromDocuments(new[] { document });
        Require(filesystem.IsReady && snapshot.IsReady && filesystem.value == snapshot.value,
            "The SQL snapshot hash must match the existing GTE profile hash over exact Generic path and bytes.");
    }

    private static void AuthoringImportParity(string root)
    {
        ContentFixture fixture = WriteSource(root, "authoring-parity/generic.json", "generic_dialogue", GenericRevisionFixture("same authored bytes"));
        ContentSourceManifest manifest = (ContentSourceManifest)BuildManifest(fixture, "bake.authoring-parity");
        ContentResult<ContentPackage> fileImport = LegacyContentAdapter.Import(manifest);
        Require(fileImport.IsReady, "the explicit filesystem import failed: " + Detail(fileImport));

        byte[] bytes = File.ReadAllBytes(Path.Combine(fixture.root, fixture.path.Replace('/', Path.DirectorySeparatorChar)));
        var source = new ContentSourceDocument(fixture.kind, fixture.path, "generic.revision", Hash(bytes), bytes);
        ContentResult<ContentPackage> memoryImport = LegacyContentAdapter.ImportDocuments(manifest.packageId, manifest.storeId, new[] { source });
        Require(memoryImport.IsReady, "the in-memory source import failed: " + Detail(memoryImport));

        LegacySourceRecord fileSource = fileImport.value.legacySources.Single();
        LegacySourceRecord memorySource = memoryImport.value.legacySources.Single();
        Require(fileImport.value.sourceManifestHash == memoryImport.value.sourceManifestHash &&
            fileSource.kind == memorySource.kind && fileSource.relativePath == memorySource.relativePath &&
            fileSource.sourceId == memorySource.sourceId && fileSource.sourceHash == memorySource.sourceHash &&
            fileSource.payloadHash == memorySource.payloadHash && fileSource.contentId == memorySource.contentId &&
            fileSource.contentRevision == memorySource.contentRevision && BytesEqual(fileSource.sourceBytes, memorySource.sourceBytes) &&
            fileImport.value.dialogues.Count == memoryImport.value.dialogues.Count &&
            fileImport.value.texts.Count == memoryImport.value.texts.Count,
            "The authoring snapshot importer changed stable source mapping or compiled content relative to the filesystem adapter.");
    }

    private static void SnapshotBakePinsRevision(string root)
    {
        string authoringPath = Path.Combine(root, "authoring-snapshot", "authoring.sqlite3");
        string outputPath = Path.Combine(root, "authoring-snapshot", "staged", "content.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(authoringPath));
        AuthoringResult<AuthoringRepository> created = AuthoringRepository.CreateNew(authoringPath);
        Require(created.IsReady, "the authoring fixture store could not be created: " + Detail(created));
        AuthoringRepository repository = created.value;
        string firstJson = "{\"schemaVersion\":1,\"id\":\"generic.snapshot\",\"source\":\"generic\",\"routing\":{\"routeId\":\"snapshot_route\",\"priority\":1,\"startHour\":-1,\"endHour\":-1,\"examples\":[\"snapshot\"],\"requiredContext\":[],\"blockedContext\":[]},\"nodes\":[{\"id\":\"line\",\"speaker\":\"alterra\",\"text\":\"Pinned version\",\"displayText\":\"Pinned version\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"gentle\",\"animation\":\"\"},\"choices\":[]}]}";
        byte[] firstBytes = Encoding.UTF8.GetBytes(firstJson);
        AuthoringResult<AuthoringDocument> saved = repository.Save(new AuthoringSaveRequest
        {
            documentId = "generic-snapshot",
            kind = "generic_dialogue",
            logicalPath = "Generic/snapshot.json",
            operationId = "snapshot-save-first",
            documentBytes = firstBytes
        });
        Require(saved.IsReady, "the first authoring revision could not be saved: " + Detail(saved));

        AuthoringResult<AuthoringDocument> draftSaved = repository.Save(new AuthoringSaveRequest
        {
            documentId = "unselected-incomplete-draft",
            kind = "topic",
            logicalPath = "Drafts/topics/unselected.json",
            operationId = "snapshot-save-unselected-draft",
            documentBytes = Encoding.UTF8.GetBytes("{\"status\":\"draft\"}")
        });
        Require(draftSaved.IsReady, "an unselected incomplete draft should remain saveable in the authoring store.");

        AuthoringResult<AuthoringSnapshot> snapshotResult = repository.ReadSnapshot(new[] { "generic-snapshot" });
        Require(snapshotResult.IsReady && snapshotResult.value.documents.Count == 1,
            "the explicit snapshot did not pin only its selected document: " + Detail(snapshotResult));
        AuthoringSnapshot snapshot = snapshotResult.value;
        string originalHash = Hash(firstBytes);
        byte[] changedBytes = Encoding.UTF8.GetBytes(firstJson.Replace("Pinned version", "Later edit"));
        AuthoringResult<AuthoringDocument> changed = repository.Save(new AuthoringSaveRequest
        {
            documentId = "generic-snapshot",
            kind = "generic_dialogue",
            logicalPath = "Generic/snapshot.json",
            expectedHash = originalHash,
            operationId = "snapshot-save-later",
            documentBytes = changedBytes
        });
        Require(changed.IsReady && changed.value.revisionHash != originalHash,
            "a later authoring edit was not installed after the snapshot was captured.");

        ContentResult<ContentBakeArtifact> baked = ContentBakeService.BakeFromSnapshot(snapshot, outputPath, null);
        Require(baked.IsReady, "the pinned SQL snapshot did not bake: " + Detail(baked));
        ContentResult<ContentRepository> opened = ContentRepository.Open(outputPath, ContentStoreMode.ReadOnly, snapshot.storeId);
        Require(opened.IsReady, "the authoring snapshot artifact did not reopen: " + Detail(opened));
        ContentResult<List<LegacySourceRecord>> sourceRows = opened.value.ReadLegacySources();
        Require(sourceRows.IsReady && sourceRows.value.Count == 1 && sourceRows.value[0].sourceHash == originalHash &&
            BytesEqual(sourceRows.value[0].sourceBytes, firstBytes),
            "The staged release did not preserve the exact selected snapshot bytes or included an unselected draft.");
        ContentResult<ContentBuildManifest> manifest = opened.value.ReadBuildManifest();
        Require(manifest.IsReady && manifest.value.schemaVersion == 2 && manifest.value.authoringStoreId == snapshot.storeId &&
            manifest.value.authoringSnapshotHash == snapshot.snapshotHash && baked.value.sources == 1,
            "The staged release is missing its exact authoring store and snapshot provenance: " + Detail(manifest));
        byte[] stagedBytes = File.ReadAllBytes(outputPath);
        ContentResult<ContentBakeArtifact> overwrite = ContentBakeService.BakeFromSnapshot(snapshot, outputPath, null);
        Require(!overwrite.IsReady && DiagnosticCodes(overwrite).Contains("bake_output_exists") &&
            BytesEqual(stagedBytes, File.ReadAllBytes(outputPath)),
            "A later or invalid bake attempt must leave the existing staged release byte-identical.");
        repository.Dispose();
    }

    private static void IncompleteDraftBlocksSnapshotBake(string root)
    {
        string authoringPath = Path.Combine(root, "incomplete-authoring", "authoring.sqlite3");
        string outputPath = Path.Combine(root, "incomplete-authoring", "staged", "content.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(authoringPath));
        AuthoringResult<AuthoringRepository> created = AuthoringRepository.CreateNew(authoringPath);
        Require(created.IsReady, "the incomplete-draft store could not be created: " + Detail(created));
        AuthoringRepository repository = created.value;
        AuthoringResult<AuthoringDocument> saved = repository.Save(new AuthoringSaveRequest
        {
            documentId = "incomplete-topic",
            kind = "topic",
            logicalPath = "Drafts/topics/incomplete.json",
            operationId = "incomplete-topic-save",
            documentBytes = Encoding.UTF8.GetBytes("{\"status\":\"draft\"}")
        });
        Require(saved.IsReady, "a syntactically valid incomplete draft must remain editable: " + Detail(saved));
        AuthoringResult<AuthoringSnapshot> snapshot = repository.ReadSnapshot(new[] { "incomplete-topic" });
        Require(snapshot.IsReady, "the explicit incomplete draft snapshot could not be read: " + Detail(snapshot));
        ContentResult<ContentBakeArtifact> baked = ContentBakeService.BakeFromSnapshot(snapshot.value, outputPath, null);
        Require(!baked.IsReady && !File.Exists(outputPath) && DiagnosticCodes(baked).Contains("authoring_draft_document_missing"),
            "The selected incomplete draft must block the build before it creates an artifact: " + Detail(baked));
        repository.Dispose();
    }

    private static void JsonRuntimeCompatibility()
    {
        Type json = runtime.GetType("AlterOS.Content.ContentJson", false);
        if (json == null) throw new InvalidOperationException("ContentJson is missing from the runtime assembly.");
        MethodInfo parse = json.GetMethod("Parse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo quote = json.GetMethod("Quote", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (parse == null || quote == null) throw new InvalidOperationException("ContentJson parse/quote contract is incomplete.");

        const string timestamp = "2026-10-22T03:43:58.250499+00:00";
        object document = parse.Invoke(null, new object[] { "{\"timestamp\":\"" + timestamp + "\"}" });
        MethodInfo getItem = document.GetType().GetMethod("get_Item", new[] { typeof(string) });
        object timestampToken = getItem == null ? null : getItem.Invoke(document, new object[] { "timestamp" });
        object tokenType = timestampToken == null ? null : timestampToken.GetType().GetProperty("Type").GetValue(timestampToken, null);
        if (tokenType == null || tokenType.ToString() != "String" || timestampToken.ToString() != timestamp)
            throw new InvalidOperationException("ISO timestamp JSON strings must remain strings with their original text.");

        object duplicate = parse.Invoke(null, new object[] { "{\"id\":\"first\",\"id\":\"second\"}" });
        object duplicateValue = getItem == null ? null : getItem.Invoke(duplicate, new object[] { "id" });
        if (duplicateValue == null || duplicateValue.ToString() != "second")
            throw new InvalidOperationException("Duplicate JSON properties must retain System.Json's last-value behavior.");

        foreach (string commentedJson in new[]
        {
            "/* before */ {\"id\":\"value\"}",
            "{\"id\":\"value\" /* inside */}",
            "{\"id\":\"value\"} /* after */"
        })
        {
            bool commentsRejected = false;
            try { parse.Invoke(null, new object[] { commentedJson }); }
            catch (TargetInvocationException) { commentsRejected = true; }
            if (!commentsRejected) throw new InvalidOperationException("JSON comments must remain invalid under the legacy parser contract: " + commentedJson);
        }

        string quoted = (string)quote.Invoke(null, new object[] { "line\nquote\"" });
        if (quoted != "\"line\\nquote\\\"\"") throw new InvalidOperationException("String quoting changed from JSON escaping semantics.");

        bool trailingRejected = false;
        try { parse.Invoke(null, new object[] { "{} {}" }); }
        catch (TargetInvocationException) { trailingRejected = true; }
        if (!trailingRejected) throw new InvalidOperationException("Trailing JSON content must be rejected.");
    }

    private static void ScenarioHistoryCallbacks()
    {
        FlowFixture fixture = BuildFlowFixture();
        const string runId = "run-history-callbacks";
        ScenarioRuntimeSnapshot context = FlowContext(runId, new[] { "history answer", "untouched", "host approved" });
        ContentExecutionPlan plan = Prepare(fixture, "scenario.root", context, FlowInputs("history answer"));
        var observations = new List<ScenarioHistoryObservation>();
        var recording = new List<InteractionObservation>();
        var presentation = new ScenarioConversationPresentationModel
        { HistoryObserver = observations.Add, RecordingObserver = recording.Add };
        IReadOnlyList<ScenarioEffect> effects = presentation.Start(plan, runId, context);
        Require(effects.Any(effect => effect.type == "ask_player") && observations.Any(item => item.kind == "scenario_started") &&
            observations.Any(item => item.kind == "scenario_step_presented" && item.runId == runId &&
                !string.IsNullOrEmpty(item.invocationId) && !string.IsNullOrEmpty(item.stepExecutionId)),
            "Scenario start or Step presentation did not expose stable run/invocation/execution identities");
        Require(recording.Any(item => item.kind == "presented_options" && item.metadata.ContainsKey("optionIds") && item.metadata.ContainsKey("order")),
            "the optional recording hook did not capture the exact presented option IDs/order");
        int recordedSteps = observations.Count(item => item.kind == "scenario_step_presented");
        presentation.ResumeForInteraction(context);
        Require(observations.Count(item => item.kind == "scenario_step_presented") == recordedSteps,
            "re-presenting the same Step execution duplicated its optional history observation");
        string selectedOptionId = presentation.Current.answerOptions[0].optionId;
        presentation.SelectAnswer(presentation.Current.answerOptions[0].stableId, context);
        ScenarioHistoryObservation accepted = observations.LastOrDefault(item => item.kind == "scenario_answer_accepted");
        Require(accepted != null && accepted.answerKind == "option" && accepted.answerOptionId == selectedOptionId &&
            !accepted.GetType().GetFields().Any(field => field.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0),
            "accepted answer history lost its choice ID or exposed a raw free-text field");
        Require(recording.Any(item => item.kind == "answer_selected" && item.metadata.ContainsKey("answerId") &&
            item.metadata["answerId"] == selectedOptionId), "the optional recording hook missed the confirmed choice");

        const string beatRunId = "run-history-beat";
        var beatContext = new ScenarioRuntimeSnapshot("scenario.history.beats", beatRunId, beatRunId, 1,
            "line.one", null, null, null, null, null);
        var beatScenario = new ScenarioDefinition
        {
            scenarioId = "scenario.history.beats", topicId = string.Empty, entryStepId = "line.one",
            steps = new List<ScenarioStep>
            {
                new ScenarioStep
                {
                    id = "line.one", kind = "dialogue", nextStepId = "end.one",
                    beats = new List<ScenarioBeat> { new ScenarioBeat
                    { id = "beat.one", speaker = "altera", text = "こんにちは", displayText = "Hello", language = "japanese" } }
                },
                new ScenarioStep { id = "end.one", kind = "end", outcome = "completed" }
            }
        };
        var beatRecords = new List<InteractionObservation>();
        var beatPresentation = new ScenarioConversationPresentationModel { RecordingObserver = beatRecords.Add };
        Require(beatPresentation.Start(beatScenario, beatRunId, beatContext).Any(effect => effect.type == "show_dialogue") &&
            beatPresentation.AcknowledgeDialogueBeat("line.one", "beat.one") &&
            beatPresentation.AcknowledgeDialogueBeat("line.one", "beat.one") &&
            beatRecords.Count(item => item.kind == "beat_presented") == 1,
            "the optional recording hook did not capture one acknowledged presented beat");

        var isolated = new ScenarioConversationPresentationModel { HistoryObserver = _ => { throw new IOException("synthetic journal unavailable"); } };
        Require(isolated.Start(plan, runId, context).Any(effect => effect.type == "ask_player"),
            "an unavailable optional history observer interrupted the Scenario");
    }

    private static void SemanticGroup(string root)
    {
        Run("content semantic importer, incremental staging, index loader and snapshot publisher APIs are present", SemanticApiSignatures);
        Run("the baked GTE vectors pin the exact current Generic source corpus", CurrentGenericCorpusMatchesBakedGteHash);
        Run("the current baked GTE asset imports into SQL and keeps route ranking equivalent", () => SemanticAssetImportRoundTripAndRanking(root));
        Run("semantic vectors validate atomically and publication requires the loaded matching snapshot", () => EmbeddingStageValidationAndSnapshotPublish(root));
    }

    private static void GteTopicAnchorsGroup()
    {
        Run("selected-snapshot GTE Topic anchors reuse exact Topic links without creating Generic wrappers", GteTopicAnchorsRoundTrip);
    }

    private static void GteTopicAnchorsRoundTrip()
    {
        const string storeId = "2ca2e8ce-4380-481a-84d3-9a1d87b175aa";
        string topicRevision = Hash("topic-revision");
        string text = "Can you turn on the television?";
        string anchorId = Hash(Encoding.UTF8.GetBytes(text));
        var package = new ContentPackage { packageId = "gte-topic-fixture", storeId = storeId };
        package.topics.Add(new TopicRecord { id = "topic.television", revision = topicRevision, semanticHash = topicRevision });
        package.anchors.Add(new AnchorRecord { id = anchorId, textHash = anchorId, text = text, language = "und" });
        package.anchorLinks.Add(new AnchorLinkRecord { id = "topic-anchor-link", anchorId = anchorId, ownerKind = "topic",
            ownerId = "topic.television", ownerRevision = topicRevision, role = "anchor_0000" });
        string snapshotHash = Hash("selected-sql-snapshot");
        string json = "{\"schemaVersion\":2,\"model\":\"Alibaba-NLP/gte-multilingual-base\",\"modelRevision\":\"pinned\"," +
            "\"codeRevision\":\"pipeline-v2\",\"dimension\":1,\"contentHash\":\"" + snapshotHash + "\",\"anchors\":[{" +
            "\"ownerKind\":\"topic\",\"ownerId\":\"topic.television\",\"ownerRevision\":\"" + topicRevision + "\"," +
            "\"role\":\"anchor_0000\",\"text\":\"" + text + "\"}]}";
        byte[] vector = BitConverter.GetBytes(1f);
        var vocabulary = new ContentHostVocabulary(new string[0], new string[0], new string[0], new string[0], new string[0]);
        ContentResult<GteSemanticImportResult> imported = GteSemanticAssetImporter.ImportInto(package,
            Encoding.UTF8.GetBytes(json), vector, Encoding.UTF8.GetBytes("tokenizer-fixture"), snapshotHash, vocabulary);
        Require(imported.IsReady, "a valid selected Topic anchor could not receive its GTE vector: " + Detail(imported));
        Require(package.anchorLinks.Count == 1 && package.anchorLinks[0].ownerKind == "topic" &&
            package.anchorLinks[0].ownerId == "topic.television" && package.anchorLinks[0].role == "anchor_0000" &&
            package.embeddings.Count == 1 && package.embeddings[0].anchorId == anchorId,
            "Topic semantic baking replaced the exact authored Topic link or emitted a Generic route wrapper");
    }

    private static void SemanticApiSignatures()
    {
        Type importer = ContentType("GteSemanticAssetImporter");
        Type index = ContentType("ContentSemanticIndex");
        Type snapshot = ContentType("ContentSemanticSnapshot");
        Type repository = ContentType("ContentRepository");
        Type profile = ContentType("EmbeddingProfileRecord");
        Type reference = ContentType("ContentRef");
        Require(importer.GetMethod("ImportInto", BindingFlags.Public | BindingFlags.Static) != null,
            "GTE metadata/vector import API is missing");
        Require(index.GetMethod("Load", BindingFlags.Public | BindingFlags.Static) != null,
            "immutable semantic index load API is missing");
        Require(repository.GetMethod("StageEmbedding", BindingFlags.Public | BindingFlags.Instance) != null,
            "incremental embedding staging API is missing");
        Require(repository.GetMethod("StageJob", new[] { typeof(string), typeof(string), typeof(string) }) != null &&
            repository.GetMethod("CompleteJob", new[] { typeof(string), typeof(bool), typeof(string) }) != null &&
            repository.GetMethod("ReadJob", new[] { typeof(string) }) != null,
            "the local idempotent content-job lifecycle API is missing");
        Require(repository.GetMethod("Publish", new[]
        { ContentType("ContentRelease"), typeof(string), typeof(long), snapshot }) != null,
            "publication does not require an exact loaded semantic snapshot");
        Require(profile != null && reference != null, "semantic profile or anchor reference types are missing");
    }

    private static void CurrentGenericCorpusMatchesBakedGteHash()
    {
        JsonObject metadata;
        byte[] metadataBytes, vectors, tokenizer;
        ReadGteAssets(out metadata, out metadataBytes, out vectors, out tokenizer);
        string exportRoot = Path.Combine(FindProjectRoot(), "Content", "AuthoringExports");
        DirectoryInfo[] snapshots = Directory.GetDirectories(exportRoot)
            .Where(item => File.Exists(Path.Combine(item, "manifest.json")))
            .Select(item => new DirectoryInfo(item)).ToArray();
        Require(snapshots.Length > 0, "the current SQLite authoring snapshot export is missing");
        var candidates = snapshots.Select(item => new
        {
            directory = item,
            manifest = JToken.Parse(File.ReadAllText(Path.Combine(item.FullName, "manifest.json"))) as JsonObject
        }).Where(item => item.manifest != null).OrderByDescending(item => (long)item.manifest["storeRevision"]).ToArray();
        Require(candidates.Length > 0, "the SQLite authoring snapshot export has no valid manifest");
        var latest = candidates[0];
        Require(candidates.Count(item => (long)item.manifest["storeRevision"] == (long)latest.manifest["storeRevision"]) == 1 &&
            string.Equals(latest.directory.Name, (string)latest.manifest["snapshotHash"], StringComparison.Ordinal),
            "the latest SQLite authoring snapshot is ambiguous or its folder does not match its manifest");
        var documents = new List<ContentSourceDocument>();
        foreach (JsonObject item in ((JsonArray)latest.manifest["documents"]).OfType<JsonObject>()
            .Where(item => (string)item["kind"] == ContentSourceKinds.GenericDialogue))
        {
            string sourcePath = Path.GetFullPath(Path.Combine(latest.directory.FullName,
                ((string)item["originalPath"]).Replace('/', Path.DirectorySeparatorChar)));
            string exportPath = Path.GetFullPath(latest.directory.FullName) + Path.DirectorySeparatorChar;
            Require(sourcePath.StartsWith(exportPath, StringComparison.OrdinalIgnoreCase),
                "the SQLite authoring export contains a source path outside its snapshot");
            byte[] bytes = File.ReadAllBytes(sourcePath);
            string revisionHash = (string)item["revisionHash"];
            Require(Hash(bytes) == revisionHash, "a Generic source differs from its pinned SQLite revision: " + (string)item["logicalPath"]);
            documents.Add(new ContentSourceDocument((string)item["kind"], (string)item["logicalPath"],
                (string)item["sourceId"], revisionHash, bytes));
        }
        ContentResult<string> result = ContentSourceManifestBuilder.ComputeGenericCorpusHashFromDocuments(documents);
        Require(result.IsReady && result.value == (string)metadata["contentHash"],
            "the baked GTE source fingerprint is stale relative to the latest SQLite authoring snapshot: " + Detail(result));
    }

    private static void SemanticAssetImportRoundTripAndRanking(string root)
    {
        JsonObject metadata;
        byte[] metadataBytes, vectorBytes, tokenizerBytes;
        ReadGteAssets(out metadata, out metadataBytes, out vectorBytes, out tokenizerBytes);
        JsonArray metadataAnchors = (JsonArray)metadata["anchors"];
        string[] routeIds = metadataAnchors.OfType<JsonObject>()
            .Select(item => (string)item["routeId"]).Distinct(StringComparer.Ordinal).ToArray();
        var source = BuildFlowFixture();
        ContentPackage package = source.package;
        var vocabulary = new ContentHostVocabulary(new[] { "fixture.action" }, new[] { "gentle", "happy" }, new string[0],
            new[] { "is_home" }, routeIds);
        Type importer = ContentType("GteSemanticAssetImporter");
        MethodInfo import = importer.GetMethod("ImportInto", new[]
        { typeof(ContentPackage), typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(string), typeof(ContentHostVocabulary) });
        Require(import != null, "GTE importer signature does not accept verified baked assets and the authored package");
        int anchorsBeforeRejectedImport = package.anchors.Count;
        object rejectedImport = import.Invoke(null, new object[] { package, metadataBytes, vectorBytes, tokenizerBytes,
            Hash("wrong-source-manifest"), vocabulary });
        Require(Status(rejectedImport) != "ready" && package.anchors.Count == anchorsBeforeRejectedImport &&
            package.embeddingProfiles.Count == 0 && package.embeddings.Count == 0,
            "a GTE asset/source hash mismatch partially mutated the authored package");
        object imported = import.Invoke(null, new object[] { package, metadataBytes, vectorBytes, tokenizerBytes,
            (string)metadata["contentHash"], vocabulary });
        Require(Status(imported) == "ready", "the existing GTE asset import failed: " + Detail(imported));
        Require(package.anchors.Count == 673 && package.anchorLinks.Count == 678 && package.embeddings.Count == 673,
            "the existing GTE asset import changed unique anchor, route link or vector coverage");
        EmbeddingProfileRecord profile = package.embeddingProfiles.Single();
        Require(profile.modelRevision == (string)metadata["modelRevision"] &&
            profile.pipelineHash == Hash(Encoding.UTF8.GetBytes((string)metadata["codeRevision"])) &&
            profile.dimensions == (int)metadata["dimension"] && profile.normalization == "l2" && profile.metric == "dot",
            "the GTE compatibility profile did not preserve model, pipeline or ranking normalization");
        AnchorRecord firstAnchor = package.anchors.First(item => item.id == Hash(Encoding.UTF8.GetBytes((string)metadataAnchors[0]["text"])));
        Require(firstAnchor.language == "und" && package.anchorLinks.Any(link => link.anchorId == firstAnchor.id &&
            link.ownerKind == "canonical" && link.ownerId == (string)metadataAnchors[0]["routeId"]),
            "the imported anchor lost its legacy language marker or route link");

        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package, vocabulary);
        Require(compiled.IsReady, "the GTE semantic package did not compile: " + Detail(compiled));
        string databasePath = Path.Combine(root, "semantic-assets-" + Guid.NewGuid().ToString("N"), "content.sqlite3");
        object repository = OpenRepositoryWithStoreId(databasePath, "ReadWrite", package.storeId);
        Require(Status(Invoke(repository, "Stage", compiled.package)) == "ready", "the SQL semantic package did not stage");
        MethodInfo load = ContentType("ContentSemanticIndex").GetMethod("Load", new[]
        { ContentType("ContentRepository"), ContentType("ContentRelease"), typeof(string) });
        Require(load != null, "ContentSemanticIndex.Load(repository, release, profileId) is missing");
        object loaded = load.Invoke(null, new[] { repository, compiled.release, profile.id });
        Require(Status(loaded) == "ready", "the SQL semantic snapshot did not load: " + Detail(loaded));
        object snapshot = Value(loaded);
        Require((int)GetProperty(snapshot, "AnchorCount") == 673 && (int)GetProperty(snapshot, "RouteCount") == 57 &&
            (int)GetProperty(snapshot, "LinkCount") == 678 && (int)GetProperty(snapshot, "Dimension") == 768,
            "the immutable SQL snapshot changed anchor, route, link or dimension counts");

        float[] query = ReadLittleEndianVector(vectorBytes, 0, profile.dimensions);
        for (int index = 0; index < query.Length; index++) query[index] = query[index] * 0.71f + ReadLittleEndianVector(vectorBytes, 101, profile.dimensions)[index] * 0.29f;
        IList expected = LegacyRouteRank(metadataAnchors, vectorBytes, profile.dimensions, query, 0.45f);
        MethodInfo rank = snapshot.GetType().GetMethod("RankRoutes", new[] { typeof(float[]), typeof(float) });
        Require(rank != null, "the immutable snapshot does not expose the preserved route ranker");
        IEnumerable actual = (IEnumerable)rank.Invoke(snapshot, new object[] { query, 0.45f });
        object[] actualRows = actual.Cast<object>().ToArray();
        Require(actualRows.Length == expected.Count, "SQL ranking returned a different number of route candidates");
        float maximumScoreDifference = 0f;
        for (int index = 0; index < actualRows.Length; index++)
        {
            object actualRow = actualRows[index]; object expectedRow = expected[index];
            float scoreDifference = Math.Abs((float)GetProperty(actualRow, "Score") - (float)GetField(expectedRow, "score"));
            if (scoreDifference > maximumScoreDifference) maximumScoreDifference = scoreDifference;
            Require(GetProperty(actualRow, "RouteId") as string == GetField(expectedRow, "routeId") as string &&
                scoreDifference < 0.00003f &&
                Math.Abs((float)GetProperty(actualRow, "BestSimilarity") - (float)GetField(expectedRow, "bestSimilarity")) < 0.00001f &&
                (int)GetProperty(actualRow, "SupportCount") == (int)GetField(expectedRow, "supportCount") &&
                GetProperty(actualRow, "BestAnchor") as string == GetField(expectedRow, "bestAnchor") as string,
                "SQL route ranking differs from the baked-file route scorer at rank " + index + "; actual=" +
                GetProperty(actualRow, "RouteId") + "/" + GetProperty(actualRow, "Score") + "/" + GetProperty(actualRow, "BestSimilarity") +
                "/" + GetProperty(actualRow, "SupportCount") + "/" + GetProperty(actualRow, "BestAnchor") + "; expected=" +
                GetField(expectedRow, "routeId") + "/" + GetField(expectedRow, "score") + "/" + GetField(expectedRow, "bestSimilarity") +
                "/" + GetField(expectedRow, "supportCount") + "/" + GetField(expectedRow, "bestAnchor"));
        }
        Console.WriteLine("PARITY GTE route_order=exact candidates=" + actualRows.Length + " max_score_delta=" +
            maximumScoreDifference.ToString("F9", CultureInfo.InvariantCulture) + " score_tolerance=0.000030");
        ReportSemanticPerformance(root, databasePath, package.storeId, compiled.release, profile.id, metadataBytes,
            vectorBytes, metadataAnchors, query, firstAnchor.id, (ContentSemanticSnapshot)snapshot);
    }

    private static void EmbeddingStageValidationAndSnapshotPublish(string root)
    {
        FlowFixture fixture = BuildFlowFixture();
        AddSemanticTestTopic(fixture);
        ContentPackage package = fixture.package;
        string anchorText = "local only semantic anchor";
        string anchorHash = Hash(Encoding.UTF8.GetBytes(anchorText));
        package.anchors.Add(new AnchorRecord { id = anchorHash, textHash = anchorHash, text = anchorText, language = "ja" });
        package.anchorLinks.Add(new AnchorLinkRecord
        {
            id = "link.local.semantic", anchorId = anchorHash, ownerKind = "topic", ownerId = "topic.sample",
            ownerRevision = package.topics.Single(item => item.id == "topic.sample").revision, role = "local"
        });
        var profile = new EmbeddingProfileRecord
        {
            id = "fixture.semantic.profile", modelId = "fixture/provider-model", modelRevision = "fixture-r1",
            tokenizerHash = Hash("fixture-tokenizer"), pipelineHash = Hash("fixture-pipeline"),
            dimensions = 2, dtype = "float32-le", normalization = "l2", metric = "cosine"
        };
        package.embeddingProfiles.Add(profile);
        byte[] validVector = FloatVectorBytes(1f, 0f);
        package.embeddings.Add(new EmbeddingRecord { anchorId = anchorHash, profileId = profile.id,
            vector = validVector, vectorHash = Hash(validVector) });
        object validation = ValidatePackage(package, fixture.vocabulary);
        Require((bool)GetField(validation, "isValid"), "a finite normalized local-only vector was rejected: " + DiagnosticCodes(validation));

        FlowFixture dimensionFixture = BuildFlowFixture();
        AddSemanticTestTopic(dimensionFixture);
        AddLocalSemanticAnchor(dimensionFixture, anchorText, "ja", FloatVectorBytes(1f));
        object dimensionValidation = ValidatePackage(dimensionFixture.package, dimensionFixture.vocabulary);
        Require(!(bool)GetField(dimensionValidation, "isValid") && DiagnosticCodes(dimensionValidation).Contains("embedding_dimension_mismatch"),
            "a vector with the wrong profile dimension was accepted");

        FlowFixture normalizationFixture = BuildFlowFixture();
        AddSemanticTestTopic(normalizationFixture);
        AddLocalSemanticAnchor(normalizationFixture, anchorText, "ja", FloatVectorBytes(2f, 0f));
        object normalizationValidation = ValidatePackage(normalizationFixture.package, normalizationFixture.vocabulary);
        Require(!(bool)GetField(normalizationValidation, "isValid") && DiagnosticCodes(normalizationValidation).Contains("embedding_normalization_invalid"),
            "a non-unit vector in an L2-normalized profile was accepted");

        FlowFixture stagingFixture = BuildFlowFixture();
        AddSemanticTestTopic(stagingFixture);
        AnchorRecord stagedAnchor;
        EmbeddingProfileRecord stagedProfile;
        AddLocalSemanticAnchor(stagingFixture, anchorText, "ja", null, out stagedAnchor, out stagedProfile);
        ContentPackage stagingPackage = stagingFixture.package;
        string databasePath = Path.Combine(root, "semantic-stage-" + Guid.NewGuid().ToString("N"), "content.sqlite3");
        object repository = OpenRepositoryWithStoreId(databasePath, "ReadWrite", stagingPackage.storeId);
        CompiledReleaseResult incompleteCompile = ContentReleaseCompiler.Compile(stagingPackage, stagingFixture.vocabulary);
        Require(incompleteCompile.IsReady, "the incomplete semantic draft did not compile: " + Detail(incompleteCompile));
        object incompleteStage = Invoke(repository, "Stage", incompleteCompile.package);
        Require(Status(incompleteStage) == "ready", "the incomplete semantic draft did not stage for incremental embedding: " +
            Status(incompleteStage) + " " + Detail(incompleteStage));
        MethodInfo load = ContentType("ContentSemanticIndex").GetMethod("Load", new[]
        { ContentType("ContentRepository"), ContentType("ContentRelease"), typeof(string) });
        object incomplete = load.Invoke(null, new object[] { repository, incompleteCompile.release, profile.id });
        Require(Status(incomplete) != "ready", "an incomplete profile was presented as a ready semantic index");
        object unpublished = InvokePublish(repository, incompleteCompile.release, "semantic", 0L, null);
        Require(Status(unpublished) != "ready" && Status(Invoke(repository, "ReadReleaseHead", "semantic")) == "missing",
            "content publication succeeded without a loaded compatible semantic snapshot");

        MethodInfo stageEmbedding = repository.GetType().GetMethod("StageEmbedding", new[]
        { ContentType("ContentRef"), ContentType("EmbeddingProfileRecord"), typeof(byte[]), typeof(string) });
        Require(stageEmbedding != null, "the typed incremental StageEmbedding signature is missing");
        var anchorReference = new ContentRef(stagingPackage.storeId, anchorHash, anchorHash, anchorHash);
        Require(Status(stageEmbedding.Invoke(repository, new object[] { anchorReference, stagedProfile, validVector, Hash("wrong-source") })) != "ready",
            "an embedding for stale source text was staged");
        Require(Status(stageEmbedding.Invoke(repository, new object[] { anchorReference, stagedProfile, FloatVectorBytes(float.NaN, 0f), anchorHash })) != "ready",
            "a nonfinite embedding was staged");
        Require(Status(stageEmbedding.Invoke(repository, new object[] { anchorReference, stagedProfile, FloatVectorBytes(float.PositiveInfinity, 0f), anchorHash })) != "ready",
            "an infinite embedding was staged");
        Require(Status(stageEmbedding.Invoke(repository, new object[] { anchorReference, stagedProfile, FloatVectorBytes(1f), anchorHash })) != "ready",
            "a dimension-mismatched embedding was staged");
        MethodInfo stageJob = repository.GetType().GetMethod("StageJob", new[] { typeof(string), typeof(string), typeof(string) });
        MethodInfo completeJob = repository.GetType().GetMethod("CompleteJob", new[] { typeof(string), typeof(bool), typeof(string) });
        MethodInfo readJob = repository.GetType().GetMethod("ReadJob", new[] { typeof(string) });
        string jobKey = "embedding." + anchorHash.Substring(0, 32);
        object stagedJob = stageJob.Invoke(repository, new object[] { jobKey, "embedding", anchorHash });
        Require(Status(stagedJob) == "ready" && GetField(Value(stagedJob), "state") as string == "staged",
            "a synthetic embedding job did not enter its explicit staged state");
        Require(Status(stageEmbedding.Invoke(repository, new object[] { anchorReference, stagedProfile, validVector, anchorHash })) == "ready",
            "a valid compatible embedding did not stage");
        object completedJob = completeJob.Invoke(repository, new object[] { jobKey, true, string.Empty });
        Require(Status(completedJob) == "ready" && GetField(Value(completedJob), "state") as string == "validated" &&
            Status(readJob.Invoke(repository, new object[] { jobKey })) == "ready",
            "the supplied synthetic vector was not tied to a durable validated job receipt");
        object duplicateJob = stageJob.Invoke(repository, new object[] { jobKey, "embedding", anchorHash });
        Require(Status(duplicateJob) == "ready" && GetField(Value(duplicateJob), "state") as string == "validated",
            "replaying a job idempotency key reset a completed job");
        Require(Status(stageJob.Invoke(repository, new object[] { jobKey, "embedding", Hash("changed-source") })) != "ready",
            "a changed job source reused an existing idempotency key");
        EmbeddingProfileRecord conflictingProfile = new EmbeddingProfileRecord
        {
            id = stagedProfile.id, modelId = stagedProfile.modelId, modelRevision = "different-revision", tokenizerHash = stagedProfile.tokenizerHash,
            pipelineHash = stagedProfile.pipelineHash, dimensions = stagedProfile.dimensions, dtype = stagedProfile.dtype,
            normalization = stagedProfile.normalization, metric = stagedProfile.metric
        };
        Require(Status(stageEmbedding.Invoke(repository, new object[] { anchorReference, conflictingProfile, validVector, anchorHash })) != "ready",
            "an incompatible profile reused the last valid profile identity");

        stagingPackage.embeddings.Add(new EmbeddingRecord { anchorId = anchorHash, profileId = stagedProfile.id,
            vector = validVector, vectorHash = Hash(validVector) });
        CompiledReleaseResult completeCompile = ContentReleaseCompiler.Compile(stagingPackage, stagingFixture.vocabulary);
        Require(completeCompile.IsReady && Status(Invoke(repository, "Stage", completeCompile.package)) == "ready",
            "the completed semantic release did not stage");
        object complete = load.Invoke(null, new object[] { repository, completeCompile.release, stagedProfile.id });
        Require(Status(complete) == "ready", "the completed profile failed to load: " + Detail(complete));
        object snapshot = Value(complete);
        object anchorView = Invoke(snapshot, "GetAnchor", anchorHash);
        Require(GetField(anchorView, "language") as string == "ja" &&
            ((IEnumerable)GetField(anchorView, "links")).Cast<object>().Any(item => GetField(item, "ownerKind") as string == "topic"),
            "the local-only anchor language or exact Topic link was lost in SQL");
        MethodInfo rankFamilies = snapshot.GetType().GetMethod("RankTopicFamilies", new[] { typeof(float[]), typeof(float) });
        object[] familyMatches = ((IEnumerable)rankFamilies.Invoke(snapshot, new object[] { new[] { 1f, 0f }, 0.5f })).Cast<object>().ToArray();
        Require(familyMatches.Length == 1 && GetProperty(familyMatches[0], "TopicId") as string == "topic.sample",
            "a local-only Topic anchor was excluded from semantic ranking");
        MethodInfo copyVector = anchorView.GetType().GetMethod("CopyVector", Type.EmptyTypes);
        float[] callerVector = (float[])copyVector.Invoke(anchorView, null);
        callerVector[0] = -100f;
        object[] afterCallerMutation = ((IEnumerable)rankFamilies.Invoke(snapshot, new object[] { new[] { 1f, 0f }, 0.5f })).Cast<object>().ToArray();
        Require((float)GetProperty(familyMatches[0], "Score") == (float)GetProperty(afterCallerMutation[0], "Score"),
            "a caller-owned vector copy mutated the immutable loaded index");
        object published = InvokePublish(repository, completeCompile.release, "semantic", 0L, snapshot);
        Require(Status(published) == "ready", "the exact loaded snapshot did not authorize release-head publication: " + Detail(published));
        object mismatched = InvokePublish(repository, incompleteCompile.release, "semantic", 1L, snapshot);
        Require(Status(mismatched) != "ready" && (long)GetField(Value(Invoke(repository, "ReadReleaseHead", "semantic")), "revision") == 1L,
            "a snapshot for another release replaced the last valid semantic head");
    }

    private static void FlowValueContracts()
    {
        FlowFixture fixture = BuildFlowFixture();
        ScenarioRuntimeSnapshot context = FlowContext("flow-values", new[] { "welcome from flow", "untouched" });
        ContentResult<FlowValue> valid = FlowValue.ParseJson("{\"kind\":\"furniture\",\"id\":\"entity.one\"}",
            "entity", fixture.vocabulary, context);
        Require(valid.IsReady && valid.value.entityValue.id == "entity.one", "a current host entity reference did not resolve");
        ContentResult<FlowValue> missing = FlowValue.ParseJson("{\"kind\":\"furniture\",\"id\":\"missing\"}",
            "entity", fixture.vocabulary, context);
        Require(!missing.IsReady, "a missing entity reference was accepted");
        var ambiguous = new ScenarioRuntimeSnapshot("", "flow-values", "turn-flow-values", 1, "", null, null, null,
            new[] { new ScenarioObjectSnapshot("entity.one", "furniture"), new ScenarioObjectSnapshot("entity.one", "furniture") },
            null, null, new Dictionary<string, bool>());
        ContentResult<FlowValue> ambiguousResult = FlowValue.ParseJson("{\"kind\":\"furniture\",\"id\":\"entity.one\"}",
            "entity", fixture.vocabulary, ambiguous);
        Require(!ambiguousResult.IsReady, "an ambiguous entity reference was accepted");
        Require(!FlowValue.ParseJson("1.5", "int64", fixture.vocabulary, context).IsReady,
            "a fractional number was coerced to int64");
        Require(!FlowValue.ParseJson("\"unregistered\"", "enum", fixture.vocabulary, context).IsReady,
            "an enum value outside the host vocabulary was accepted");
    }

    private static void SharedDialogueContinuations()
    {
        FlowFixture fixture = BuildFlowFixture();
        Require(fixture.release.dependencies.Count(item => item.kind == "dialogue" && item.id == "dialogue.shared") == 1,
            "a reused Dialogue was duplicated in the immutable release closure");
        foreach (Tuple<string, string, string> caller in new[]
        {
            Tuple.Create("scenario.shared.a", "run-shared-a", "completed"),
            Tuple.Create("scenario.shared.b", "run-shared-b", "failed")
        })
        {
            ScenarioRuntimeSnapshot context = FlowContext(caller.Item2, new[] { "welcome from flow", "untouched" });
            ContentExecutionPlan plan = Prepare(fixture, caller.Item1, context);
            var session = new ScenarioSession();
            IReadOnlyList<ScenarioEffect> started = session.Start(plan, caller.Item2, context);
            ScenarioEffect shown = started.FirstOrDefault(item => item.type == "show_dialogue");
            Require(shown != null && shown.step.beats[0].displayText == "Shared prompt", "the shared Dialogue did not enter the existing presenter");
            IReadOnlyList<ScenarioEffect> ended = session.Advance(new ScenarioEvent
            { kind = "dialogue_complete", runId = caller.Item2, stepId = shown.stepId }, context);
            ScenarioEffect result = ended.FirstOrDefault(item => item.type == "end_scenario");
            Require(result != null && result.outcome == caller.Item3, "a caller did not follow its own exact Flow transition");
        }
    }

    private static void NestedSubflowOutcomes()
    {
        FlowFixture fixture = BuildFlowFixture();
        AssertNestedOutcome(fixture, "accepted", "accepted", "welcome from flow", true);
        AssertNestedOutcome(fixture, "declined", "declined", string.Empty, false);
        AssertNestedOutcome(fixture, "cancelled", "cancelled", "untouched", true);
    }

    private static void InvocationFramesIsolateInputs()
    {
        FlowFixture fixture = BuildFlowFixture();
        ScenarioRuntimeSnapshot firstContext = FlowContext("run-frame-first", new[] { "first frame", "second frame", "host approved" });
        ScenarioRuntimeSnapshot secondContext = FlowContext("run-frame-second", new[] { "first frame", "second frame", "host approved" });
        var firstSession = new ScenarioSession();
        var secondSession = new ScenarioSession();
        IReadOnlyList<ScenarioEffect> first = firstSession.Start(Prepare(fixture, "scenario.root", firstContext, FlowInputs("first frame")),
            firstContext.runId, firstContext);
        IReadOnlyList<ScenarioEffect> second = secondSession.Start(Prepare(fixture, "scenario.root", secondContext, FlowInputs("second frame")),
            secondContext.runId, secondContext);
        ScenarioEffect firstRootAsk = first.First(item => item.type == "ask_player");
        ScenarioEffect secondRootAsk = second.First(item => item.type == "ask_player");
        first = firstSession.Advance(new ScenarioEvent
        { kind = "answer", runId = firstContext.runId, stepId = firstRootAsk.stepId, questionId = firstRootAsk.step.questionId,
            answerId = "invite", contextRevision = firstContext.contextRevision }, firstContext);
        second = secondSession.Advance(new ScenarioEvent
        { kind = "answer", runId = secondContext.runId, stepId = secondRootAsk.stepId, questionId = secondRootAsk.step.questionId,
            answerId = "invite", contextRevision = secondContext.contextRevision }, secondContext);
        ScenarioEffect firstNestedAsk = first.First(item => item.type == "ask_player");
        ScenarioEffect secondNestedAsk = second.First(item => item.type == "ask_player");
        first = firstSession.Advance(new ScenarioEvent
        { kind = "answer", runId = firstContext.runId, stepId = firstNestedAsk.stepId, questionId = firstNestedAsk.step.questionId,
            answerId = "accept", contextRevision = firstContext.contextRevision }, firstContext);
        second = secondSession.Advance(new ScenarioEvent
        { kind = "answer", runId = secondContext.runId, stepId = secondNestedAsk.stepId, questionId = secondNestedAsk.step.questionId,
            answerId = "accept", contextRevision = secondContext.contextRevision }, secondContext);
        string firstArgument = first.Single(item => item.type == "request_activity").activityRequest.arguments.Single(item => item.name == "message").value;
        string secondArgument = second.Single(item => item.type == "request_activity").activityRequest.arguments.Single(item => item.name == "message").value;
        Require(firstArgument == "first frame" && secondArgument == "second frame",
            "a Flow input or Subflow output leaked between independent invocation frames");
    }

    private static void AssertNestedOutcome(FlowFixture fixture, string selected, string expectedOutcome,
        string expectedArgument, bool needsAction)
    {
        string runId = "run-nested-" + selected;
        ScenarioRuntimeSnapshot context = FlowContext(runId, new[] { "welcome from flow", "untouched", "host approved" });
        ContentExecutionPlan plan = Prepare(fixture, "scenario.root", context, FlowInputs("welcome from flow"));
        var session = new ScenarioSession();
        IReadOnlyList<ScenarioEffect> effects = session.Start(plan, runId, context);
        ScenarioEffect rootAsk = effects.FirstOrDefault(item => item.type == "ask_player");
        Require(rootAsk != null, "root Flow did not present its Ask Step");
        effects = session.Advance(new ScenarioEvent
        { kind = "answer", runId = runId, stepId = rootAsk.stepId, questionId = rootAsk.step.questionId,
            answerId = "invite", contextRevision = context.contextRevision }, context);
        ScenarioEffect nestedAsk = effects.FirstOrDefault(item => item.type == "ask_player");
        Require(nestedAsk != null && nestedAsk.step.question.displayText == "Accept or decline?", "the nested invitation was not presented");

        if (selected == "accepted")
        {
            // A visible self-loop re-enters the same authored Step with a new occurrence identity.
            effects = session.Advance(new ScenarioEvent
            { kind = "answer", runId = runId, stepId = nestedAsk.stepId, questionId = nestedAsk.step.questionId,
                answerId = "again", contextRevision = context.contextRevision }, context);
            ScenarioEffect repeated = effects.FirstOrDefault(item => item.type == "ask_player");
            Require(repeated != null && repeated.step.sourceStepId == nestedAsk.step.sourceStepId && repeated.stepId != nestedAsk.stepId,
                "a repeated Step visit reused its previous execution identity");
            nestedAsk = repeated;
        }

        if (selected == "cancelled")
        {
            effects = session.Advance(new ScenarioEvent
            { kind = "cancel", runId = runId, stepId = nestedAsk.stepId, questionId = nestedAsk.step.questionId,
                contextRevision = context.contextRevision }, context);
        }
        else
        {
            string answer = selected == "accepted" ? "accept" : "decline";
            effects = session.Advance(new ScenarioEvent
            { kind = "answer", runId = runId, stepId = nestedAsk.stepId, questionId = nestedAsk.step.questionId,
                answerId = answer, contextRevision = context.contextRevision }, context);
        }

        if (needsAction)
        {
            ScenarioEffect request = effects.FirstOrDefault(item => item.type == "request_activity");
            Require(request != null && request.activityRequest.arguments.Single(item => item.name == "message").value == expectedArgument,
                "Subflow output mapping leaked a partial value or did not reach the trusted Action request");
            Require(request.activityRequest.stepId == request.step.id && request.step.sourceStepId.StartsWith("root.action.", StringComparison.Ordinal),
                "the host request did not carry both its unique execution identity and stable source Step identity");
            effects = session.Advance(new ScenarioEvent
            { kind = "activity_result", runId = runId, stepId = request.step.id, activityId = "activity-" + selected, success = true,
                actionResults = new Dictionary<string, FlowValue> { { "confirmation", FlowValue.Text("host approved") } } }, context);
            ScenarioEffect wait = effects.FirstOrDefault(item => item.type == "wait_for_condition");
            Require(wait != null, "accepted host Action did not continue to the existing terminal Wait Step");
            effects = session.Advance(new ScenarioEvent
            { kind = "condition_met", runId = runId, stepId = wait.stepId, activityId = "activity-" + selected,
                conditionId = "fixture.completed" }, context);
            if (selected == "accepted")
            {
                ScenarioEffect confirmed = effects.FirstOrDefault(item => item.type == "request_activity");
                Require(confirmed != null && confirmed.activityRequest.arguments.Single(item => item.name == "message").value == "host approved",
                    "a typed result from the earlier Action Step did not bind into its later Action");
                effects = session.Advance(new ScenarioEvent
                { kind = "activity_result", runId = runId, stepId = confirmed.step.id, activityId = "confirm-" + selected, success = true,
                    actionResults = new Dictionary<string, FlowValue> { { "confirmation", FlowValue.Text("host approved") } } }, context);
            }
        }

        ScenarioEffect final = effects.FirstOrDefault(item => item.type == "end_scenario");
        string trail = string.Join(",", effects.Select(item => item.type + ":" + item.outcome + ":" + item.stepId).ToArray());
        Require(final != null && final.outcome == expectedOutcome,
            "the named Subflow exit did not return the exact " + expectedOutcome + " outcome; effects=" + trail);
    }

    private static void ActionResultsAreTypedAndRetrySafe()
    {
        FlowFixture fixture = BuildFlowFixture();

        ScenarioSession missingSession;
        ScenarioRuntimeSnapshot missingContext;
        ScenarioEffect missingRequest = StartAcceptedAction(fixture, "run-missing-action-result", out missingSession, out missingContext);
        IReadOnlyList<ScenarioEffect> missing = missingSession.Advance(new ScenarioEvent
        { kind = "activity_result", runId = missingContext.runId, stepId = missingRequest.step.id,
            activityId = "activity-missing-result", success = true }, missingContext);
        Require(missing.Any(item => item.type == "end_scenario" && item.outcome == "failed"),
            "a missing required host Action result continued the success path");

        ScenarioSession wrongTypeSession;
        ScenarioRuntimeSnapshot wrongTypeContext;
        ScenarioEffect wrongTypeRequest = StartAcceptedAction(fixture, "run-wrong-action-result", out wrongTypeSession, out wrongTypeContext);
        IReadOnlyList<ScenarioEffect> wrongType = wrongTypeSession.Advance(new ScenarioEvent
        { kind = "activity_result", runId = wrongTypeContext.runId, stepId = wrongTypeRequest.step.id,
            activityId = "activity-wrong-result", success = true,
            actionResults = new Dictionary<string, FlowValue> { { "confirmation", FlowValue.Boolean(true) } } }, wrongTypeContext);
        Require(wrongType.Any(item => item.type == "end_scenario" && item.outcome == "failed"),
            "a wrong-typed host Action result continued the success path");

        ScenarioSession retrySession;
        ScenarioRuntimeSnapshot retryContext;
        ScenarioEffect retryRequest = StartAcceptedAction(fixture, "run-retry-action-result", out retrySession, out retryContext);
        var accepted = new ScenarioEvent
        {
            kind = "activity_result", runId = retryContext.runId, stepId = retryRequest.step.id,
            activityId = "activity-retry-result", success = true,
            actionResults = new Dictionary<string, FlowValue> { { "confirmation", FlowValue.Text("host approved") } }
        };
        IReadOnlyList<ScenarioEffect> acceptedEffects = retrySession.Advance(accepted, retryContext);
        ScenarioEffect wait = acceptedEffects.FirstOrDefault(item => item.type == "wait_for_condition");
        Require(wait != null, "an Action acceptance skipped the existing completion Wait Step");
        IReadOnlyList<ScenarioEffect> duplicate = retrySession.Advance(accepted, retryContext);
        Require(duplicate.Count == 0 && retrySession.Running != null && retrySession.Running.currentStepId == wait.stepId,
            "a duplicate Action result replayed an effect or changed the current Step");
        IReadOnlyList<ScenarioEffect> completed = retrySession.Advance(new ScenarioEvent
        { kind = "condition_met", runId = retryContext.runId, stepId = wait.stepId,
            activityId = "activity-retry-result", conditionId = "fixture.completed" }, retryContext);
        ScenarioEffect confirmed = completed.FirstOrDefault(item => item.type == "request_activity");
        Require(confirmed != null && confirmed.activityRequest.arguments.Single(item => item.name == "message").value == "host approved",
            "the retained typed Action result was unavailable after the completion checkpoint");
        IReadOnlyList<ScenarioEffect> terminal = retrySession.Advance(new ScenarioEvent
        { kind = "activity_result", runId = retryContext.runId, stepId = confirmed.step.id,
            activityId = "confirm-retry-result", success = true,
            actionResults = new Dictionary<string, FlowValue> { { "confirmation", FlowValue.Text("host approved") } } }, retryContext);
        Require(terminal.Any(item => item.type == "end_scenario" && item.outcome == "accepted"),
            "the valid typed Action result path did not finish after host completion");
    }

    private static ScenarioEffect StartAcceptedAction(FlowFixture fixture, string runId,
        out ScenarioSession session, out ScenarioRuntimeSnapshot context)
    {
        context = FlowContext(runId, new[] { "welcome from flow", "untouched", "host approved" });
        session = new ScenarioSession();
        IReadOnlyList<ScenarioEffect> effects = session.Start(Prepare(fixture, "scenario.root", context, FlowInputs("welcome from flow")), runId, context);
        ScenarioEffect rootAsk = effects.First(item => item.type == "ask_player");
        effects = session.Advance(new ScenarioEvent
        { kind = "answer", runId = runId, stepId = rootAsk.stepId, questionId = rootAsk.step.questionId,
            answerId = "invite", contextRevision = context.contextRevision }, context);
        ScenarioEffect nestedAsk = effects.First(item => item.type == "ask_player");
        effects = session.Advance(new ScenarioEvent
        { kind = "answer", runId = runId, stepId = nestedAsk.stepId, questionId = nestedAsk.step.questionId,
            answerId = "accept", contextRevision = context.contextRevision }, context);
        return effects.First(item => item.type == "request_activity");
    }

    private static void ContentPauseAndQuit()
    {
        FlowFixture fixture = BuildFlowFixture();
        ScenarioRuntimeSnapshot context = FlowContext("run-pause-content", new[] { "welcome from flow", "untouched" });
        var session = new ScenarioSession();
        IReadOnlyList<ScenarioEffect> started = session.Start(Prepare(fixture, "scenario.root", context, FlowInputs("welcome from flow")), context.runId, context);
        ScenarioEffect rootAsk = started.First(item => item.type == "ask_player");
        IReadOnlyList<ScenarioEffect> enteredSubflow = session.Advance(new ScenarioEvent
        { kind = "answer", runId = context.runId, stepId = rootAsk.stepId, questionId = rootAsk.step.questionId,
            answerId = "invite", contextRevision = context.contextRevision }, context);
        ScenarioEffect currentAsk = enteredSubflow.First(item => item.type == "ask_player");
        var otherContext = FlowContext("run-other", new[] { "welcome from flow", "untouched" });
        var other = new ScenarioDefinition { scenarioId = "scenario.other", topicId = string.Empty, entryStepId = "other.line" };
        other.steps.Add(new ScenarioStep { id = "other.line", kind = "dialogue", nextStepId = "other.end",
            beats = new List<ScenarioBeat> { new ScenarioBeat { id = "other.beat", text = "待って", displayText = "Wait", language = "japanese" } } });
        other.steps.Add(new ScenarioStep { id = "other.end", kind = "end", outcome = "completed" });
        session.RequestDiversion(other, otherContext.runId, DiversionDecision.Pause, otherContext);
        Require(session.Running != null && session.Suspended != null, "content and legacy occurrences did not retain one running/one suspended slot");
        session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = otherContext.runId,
            stepId = session.Running.currentStepId }, otherContext);
        IReadOnlyList<ScenarioEffect> resumed = session.Advance(new ScenarioEvent { kind = "continue_suspended", runId = context.runId }, context);
        ScenarioEffect resumedAsk = resumed.FirstOrDefault(item => item.type == "ask_player");
        Require(resumedAsk != null && resumedAsk.step.sourceStepId == currentAsk.step.sourceStepId,
            "resuming the suspended content frame lost its current Step or local frame");
        IReadOnlyList<ScenarioEffect> returned = session.Advance(new ScenarioEvent
        { kind = "answer", runId = context.runId, stepId = resumedAsk.stepId, questionId = resumedAsk.step.questionId,
            answerId = "accept", contextRevision = context.contextRevision }, context);
        ScenarioEffect action = returned.FirstOrDefault(item => item.type == "request_activity");
        Require(action != null && action.activityRequest.arguments.Single(item => item.name == "message").value == "welcome from flow",
            "a suspended Subflow frame lost its pinned input/output values on resume");
        session.Quit();
        Require(session.Running == null && session.Suspended == null,
            "Quit left content execution available to auto-resume");
    }

    private static void PublishApiSignature()
    {
        Type validator = ContentType("ContentPackageValidator");
        Type compiler = ContentType("ContentReleaseCompiler");
        Type repository = ContentType("ContentRepository");
        Require(validator.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static) != null, "ContentPackageValidator.Validate is missing");
        Require(compiler.GetMethod("ResolveDependencies", BindingFlags.Public | BindingFlags.Static) != null, "release dependency closure API is missing");
        Require(repository.GetMethod("Publish", BindingFlags.Public | BindingFlags.Instance) != null, "ContentRepository.Publish CAS API is missing");
        Require(repository.GetMethod("ReadReleaseHead", BindingFlags.Public | BindingFlags.Instance) != null, "release-head read API is missing");
        Require(ContentType("ContentRuleEvaluator").GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static) != null,
            "tri-state shared Rule evaluator API is missing");
        Require(ContentType("ContentRuleEvaluator").GetMethod("EvaluateChoice", BindingFlags.Public | BindingFlags.Static) != null,
            "choice availability projection API is missing");
    }

    private static void SourceRevisionClosure(string root)
    {
        string firstJson = GenericRevisionFixture("first text");
        ContentFixture firstFixture = WriteSource(root, "revision/generic.json", "generic_dialogue", firstJson);
        string databasePath = Path.Combine(root, "revision-closure", "content.sqlite3");
        string storeId = Guid.NewGuid().ToString("D");
        object repository = OpenRepositoryWithStoreId(databasePath, "ReadWrite", storeId);
        object firstManifest = BuildManifest(firstFixture, "bake.revision");
        SetField(firstManifest, "storeId", storeId);
        object firstImport = ImportManifest(firstManifest);
        Require(Status(firstImport) == "ready", "initial revision import failed: " + Detail(firstImport));
        object firstCompile = LegacyCompile(Value(firstImport));
        Require(Status(firstCompile) == "ready", "initial revision compile failed: " + Detail(firstCompile));
        object firstPackage = GetField(firstCompile, "package");
        Require(Status(Invoke(repository, "Stage", firstPackage)) == "ready", "initial source revision did not stage");
        object firstRelease = GetField(firstCompile, "release");
        string firstReleaseId = GetField(firstRelease, "releaseId") as string;
        object firstFlow = First(GetField(firstPackage, "flows"));
        string firstFlowId = GetField(firstFlow, "id") as string;
        string firstFlowRevision = GetField(firstFlow, "revision") as string;

        ContentFixture secondFixture = WriteSource(root, "revision/generic.json", "generic_dialogue", GenericRevisionFixture("updated text"));
        object secondManifest = BuildManifest(secondFixture, "bake.revision");
        SetField(secondManifest, "storeId", storeId);
        object secondImport = ImportManifest(secondManifest);
        Require(Status(secondImport) == "ready", "updated source import failed: " + Detail(secondImport));
        object secondCompile = LegacyCompile(Value(secondImport));
        Require(Status(secondCompile) == "ready", "updated source compile failed: " + Detail(secondCompile));
        object secondPackage = GetField(secondCompile, "package");
        object secondFlow = First(GetField(secondPackage, "flows"));
        string secondFlowRevision = GetField(secondFlow, "revision") as string;
        Require(secondFlowRevision != firstFlowRevision, "edited Text did not create a new immutable Flow revision");
        Require(Status(Invoke(repository, "Stage", secondPackage)) == "ready", "edited source path could not stage a second retained revision");

        using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM legacy_sources WHERE source_kind=? AND source_path=?", "generic_dialogue", "revision/generic.json") == 2,
                "one-time source mapping overwrote a previously imported hash/revision");
        object oldReleaseResult = Invoke(repository, "ReadRelease", firstReleaseId);
        Require(Status(oldReleaseResult) == "ready", "old release closure was not retained");
        var oldDependencies = ((IEnumerable)GetField(Value(oldReleaseResult), "dependencies")).Cast<object>();
        Require(oldDependencies.Any(item => GetField(item, "kind") as string == "flow" &&
            GetField(item, "id") as string == firstFlowId && GetField(item, "revision") as string == firstFlowRevision),
            "old release no longer pins the original Flow revision");
    }

    private static void HostAndFlowValidation(string root)
    {
        string storeId = Guid.NewGuid().ToString("D");
        object package = BuildPackage(storeId, "publish-valid");
        object vocabulary = HostVocabulary(new[] { "fixture.action" }, new[] { "gentle", "happy" }, new string[0],
            new[] { "is_home" }, new[] { "topic.sample" });
        object valid = ValidatePackage(package, vocabulary);
        Require((bool)GetField(valid, "isValid"), "a structurally valid host-authorized package was rejected: " + DiagnosticCodes(valid));
        object closure = ContentType("ContentReleaseCompiler").GetMethod("ResolveDependencies",
            new[] { ContentType("ContentPackage"), ContentType("ContentHostVocabulary") }).Invoke(null, new[] { package, vocabulary });
        Require(Status(closure) == "ready" && ((IEnumerable)Value(closure)).Cast<object>()
            .All(item => GetField(item, "store") as string == storeId),
            "validated release closure omitted local exact dependencies or accepted a foreign store");

        object crossStorePackage = BuildPackage(storeId, "publish-cross-store");
        object crossStoreEntry = First(GetField(First(GetField(crossStorePackage, "topics")), "entries"));
        SetField(GetField(crossStoreEntry, "flowRef"), "store", Guid.NewGuid().ToString("D"));
        object crossStore = ValidatePackage(crossStorePackage, vocabulary);
        Require(!(bool)GetField(crossStore, "isValid") && DiagnosticCodes(crossStore).Contains("flow_reference_invalid"),
            "a cross-store Flow dependency was accepted");

        object action = First(GetField(package, "actions"));
        SetField(action, "capabilityId", "unregistered.capability");
        object escalated = ValidatePackage(package, vocabulary);
        Require(!(bool)GetField(escalated, "isValid") && DiagnosticCodes(escalated).Contains("capability_not_registered"),
            "unregistered Action capability was accepted");

        object collisionPackage = BuildPackage(storeId, "publish-collision");
        object flow = First(GetField(collisionPackage, "flows"));
        object input = New("FlowParameterRecord");
        SetField(input, "name", "shared"); SetField(input, "direction", "input"); SetField(input, "valueType", "text"); SetField(input, "required", true);
        Add(GetField(flow, "parameters"), input);
        object variable = New("FlowVariableRecord");
        SetField(variable, "name", "shared"); SetField(variable, "valueType", "text"); SetField(variable, "mutable", true);
        Add(GetField(flow, "variables"), variable);
        object collision = ValidatePackage(collisionPackage, vocabulary);
        Require(!(bool)GetField(collision, "isValid") && DiagnosticCodes(collision).Contains("flow_name_collision"),
            "input/local name collision was accepted");

        object bindingPackage = BuildPackage(storeId, "publish-binding");
        object bindingFlow = First(GetField(bindingPackage, "flows"));
        object bindingAction = First(GetField(bindingPackage, "actions"));
        object bindingStart = First(GetField(bindingFlow, "steps"));
        SetField(bindingStart, "kind", "action");
        SetField(bindingStart, "dialogueRef", null);
        SetField(bindingStart, "actionRef", ContentRef(storeId, GetField(bindingAction, "id") as string,
            GetField(bindingAction, "revision") as string));
        SetField(bindingAction, "contractJson", "{\"inputs\":[{\"name\":\"target\",\"type\":\"text\",\"required\":true}]}");
        object boolLocal = New("FlowVariableRecord");
        SetField(boolLocal, "name", "flag"); SetField(boolLocal, "valueType", "bool"); SetField(boolLocal, "mutable", true);
        Add(GetField(bindingFlow, "variables"), boolLocal);
        object binding = New("StepBindingRecord");
        SetField(binding, "stepId", "start"); SetField(binding, "destinationName", "target"); SetField(binding, "sourceKind", "local"); SetField(binding, "sourceName", "flag");
        Add(GetField(bindingFlow, "bindings"), binding);
        object wrongBinding = ValidatePackage(bindingPackage, vocabulary);
        Require(!(bool)GetField(wrongBinding, "isValid") && DiagnosticCodes(wrongBinding).Contains("binding_type_mismatch"),
            "a bool local was bound to a text input");
    }

    private static void RuleValidation(string root)
    {
        string storeId = Guid.NewGuid().ToString("D");
        object package = BuildPackage(storeId, "publish-rule-cycle");
        string revisionA = Hash("rule.a");
        string revisionB = Hash("rule.b");
        object ruleA = New("RuleRecord");
        SetField(ruleA, "id", "rule.a"); SetField(ruleA, "revision", revisionA); SetField(ruleA, "semanticHash", revisionA);
        SetField(ruleA, "expressionJson", RuleReferenceJson(storeId, "rule.b", revisionB));
        object ruleB = New("RuleRecord");
        SetField(ruleB, "id", "rule.b"); SetField(ruleB, "revision", revisionB); SetField(ruleB, "semanticHash", revisionB);
        SetField(ruleB, "expressionJson", RuleReferenceJson(storeId, "rule.a", revisionA));
        Add(GetField(package, "rules"), ruleA); Add(GetField(package, "rules"), ruleB);
        object vocabulary = HostVocabulary(new[] { "fixture.action" }, new string[0], new string[0], new[] { "is_home" }, new string[0]);
        object cyclic = ValidatePackage(package, vocabulary);
        Require(!(bool)GetField(cyclic, "isValid") && DiagnosticCodes(cyclic).Contains("rule_cycle"), "a shared Rule cycle was accepted");

        object danglingPackage = BuildPackage(storeId, "publish-rule-dangling");
        string missingRevision = Hash("rule.dangling.target");
        object danglingRule = New("RuleRecord"); SetField(danglingRule, "id", "rule.dangling");
        SetField(danglingRule, "revision", Hash("rule.dangling")); SetField(danglingRule, "semanticHash", Hash("rule.dangling"));
        SetField(danglingRule, "expressionJson", RuleReferenceJson(storeId, "rule.dangling.target", missingRevision));
        Add(GetField(danglingPackage, "rules"), danglingRule);
        object dangling = ValidatePackage(danglingPackage, vocabulary);
        Require(!(bool)GetField(dangling, "isValid") && DiagnosticCodes(dangling).Contains("rule_missing_dependency"),
            "a dangling shared Rule reference was accepted");

        object unknownPackage = BuildPackage(storeId, "publish-rule-unknown");
        string factRevision = Hash("rule.missing_fact");
        object factRule = New("RuleRecord");
        SetField(factRule, "id", "rule.missing_fact"); SetField(factRule, "revision", factRevision); SetField(factRule, "semanticHash", factRevision);
        SetField(factRule, "expressionJson", "{\"kind\":\"fact\",\"children\":[],\"key\":\"missing_fact\",\"expected\":true}");
        Add(GetField(unknownPackage, "rules"), factRule);
        Type contextType = ProjectType("AlterOS.Topics.TopicContext");
        object context = Activator.CreateInstance(contextType);
        ((IDictionary)GetField(context, "facts")).Add("is_home", true);
        object evaluation = ContentType("ContentRuleEvaluator").GetMethod("Evaluate").Invoke(null,
            new[] { unknownPackage, ContentRef(storeId, "rule.missing_fact", factRevision, factRevision), vocabulary, context });
        Require(Status(evaluation) == "ready" && GetField(Value(evaluation), "truth") as string == "Unknown",
            "a missing host fact was treated as false/true instead of Unknown");

        string allRevision = Hash("rule.unknown.all");
        object allRule = New("RuleRecord"); SetField(allRule, "id", "rule.unknown.all"); SetField(allRule, "revision", allRevision);
        SetField(allRule, "semanticHash", allRevision);
        SetField(allRule, "expressionJson", "{\"kind\":\"all\",\"children\":[{\"kind\":\"fact\",\"children\":[],\"key\":\"missing_fact\",\"expected\":true},{\"kind\":\"fact\",\"children\":[],\"key\":\"is_home\",\"expected\":true}]}");
        Add(GetField(unknownPackage, "rules"), allRule);
        string anyRevision = Hash("rule.unknown.any");
        object anyRule = New("RuleRecord"); SetField(anyRule, "id", "rule.unknown.any"); SetField(anyRule, "revision", anyRevision);
        SetField(anyRule, "semanticHash", anyRevision);
        SetField(anyRule, "expressionJson", "{\"kind\":\"any\",\"children\":[{\"kind\":\"fact\",\"children\":[],\"key\":\"missing_fact\",\"expected\":true},{\"kind\":\"fact\",\"children\":[],\"key\":\"is_home\",\"expected\":true}]}");
        Add(GetField(unknownPackage, "rules"), anyRule);
        string notRevision = Hash("rule.unknown.not");
        object notRule = New("RuleRecord"); SetField(notRule, "id", "rule.unknown.not"); SetField(notRule, "revision", notRevision);
        SetField(notRule, "semanticHash", notRevision);
        SetField(notRule, "expressionJson", "{\"kind\":\"not\",\"children\":[{\"kind\":\"fact\",\"children\":[],\"key\":\"missing_fact\",\"expected\":true}]}");
        Add(GetField(unknownPackage, "rules"), notRule);
        foreach (string[] rule in new[] { new[] { "rule.unknown.all", allRevision }, new[] { "rule.unknown.any", anyRevision }, new[] { "rule.unknown.not", notRevision } })
        {
            ((IDictionary)GetField(context, "facts"))["is_home"] = rule[0] == "rule.unknown.any" ? false : true;
            object combined = ContentType("ContentRuleEvaluator").GetMethod("Evaluate").Invoke(null,
                new[] { unknownPackage, ContentRef(storeId, rule[0], rule[1], rule[1]), vocabulary, context });
            Require(Status(combined) == "ready" && GetField(Value(combined), "truth") as string == "Unknown",
                "all/any/not discarded Unknown truth");
        }

        object choice = New("ChoiceRecord");
        SetField(choice, "stepId", "start"); SetField(choice, "id", "choice.unknown"); SetField(choice, "transitionId", "start.next");
        SetField(choice, "valueJson", "{}"); SetField(choice, "labelTextRef", ContentRef(storeId, "text.title", Hash("text-title-v1")));
        SetField(choice, "availabilityRuleRef", ContentRef(storeId, "rule.missing_fact", factRevision, factRevision));
        Add(GetField(First(GetField(unknownPackage, "flows")), "choices"), choice);
        object hidden = ContentType("ContentRuleEvaluator").GetMethod("EvaluateChoice").Invoke(null,
            new[] { unknownPackage, choice, vocabulary, context });
        Require(Status(hidden) == "ready" && GetField(Value(hidden), "state") as string == "hidden",
            "an Unknown availability fact did not fail closed as a hidden choice");

        object availableChoice = New("ChoiceRecord");
        SetField(availableChoice, "stepId", "start"); SetField(availableChoice, "id", "choice.available"); SetField(availableChoice, "transitionId", "start.next");
        SetField(availableChoice, "valueJson", "{}"); SetField(availableChoice, "labelTextRef", ContentRef(storeId, "text.title", Hash("text-title-v1")));
        Add(GetField(First(GetField(unknownPackage, "flows")), "choices"), availableChoice);
        object available = ContentType("ContentRuleEvaluator").GetMethod("EvaluateChoice").Invoke(null,
            new[] { unknownPackage, availableChoice, vocabulary, context });
        Require(Status(available) == "ready" && GetField(Value(available), "state") as string == "available",
            "a Choice without an availability Rule was not available");

        string falseRevision = Hash("rule.home.false");
        object falseRule = New("RuleRecord"); SetField(falseRule, "id", "rule.home.false"); SetField(falseRule, "revision", falseRevision);
        SetField(falseRule, "semanticHash", falseRevision);
        SetField(falseRule, "expressionJson", "{\"kind\":\"fact\",\"children\":[],\"key\":\"is_home\",\"expected\":true}");
        Add(GetField(unknownPackage, "rules"), falseRule);
        ((IDictionary)GetField(context, "facts"))["is_home"] = false;
        object lockedChoice = New("ChoiceRecord");
        SetField(lockedChoice, "stepId", "start"); SetField(lockedChoice, "id", "choice.locked"); SetField(lockedChoice, "transitionId", "start.next");
        SetField(lockedChoice, "valueJson", "{}"); SetField(lockedChoice, "labelTextRef", ContentRef(storeId, "text.title", Hash("text-title-v1")));
        SetField(lockedChoice, "availabilityRuleRef", ContentRef(storeId, "rule.home.false", falseRevision, falseRevision));
        SetField(lockedChoice, "lockedHintTextRef", ContentRef(storeId, "text.prompt", Hash("text-prompt-v1")));
        Add(GetField(First(GetField(unknownPackage, "flows")), "choices"), lockedChoice);
        object locked = ContentType("ContentRuleEvaluator").GetMethod("EvaluateChoice").Invoke(null,
            new[] { unknownPackage, lockedChoice, vocabulary, context });
        Require(Status(locked) == "ready" && GetField(Value(locked), "state") as string == "locked",
            "a false availability Rule with a valid hint was not locked");
    }

    private static void SubflowValidation(string root)
    {
        string storeId = Guid.NewGuid().ToString("D");
        object package = BuildPackage(storeId, "publish-subflow-cycle");
        object first = SimpleFlow("subflow.first", "first.step", "subflow");
        object second = SimpleFlow("subflow.second", "second.step", "subflow");
        SetField(First(GetField(first, "steps")), "kind", "subflow");
        SetField(First(GetField(first, "steps")), "calledFlowRef", ContentRef(storeId, "subflow.second", Hash("flow.subflow.second")));
        SetField(First(GetField(second, "steps")), "kind", "subflow");
        SetField(First(GetField(second, "steps")), "calledFlowRef", ContentRef(storeId, "subflow.first", Hash("flow.subflow.first")));
        Add(GetField(package, "flows"), first); Add(GetField(package, "flows"), second);
        object vocabulary = HostVocabulary(new[] { "fixture.action" }, new string[0], new string[0], new string[0], new string[0]);
        object cyclic = ValidatePackage(package, vocabulary);
        Require(!(bool)GetField(cyclic, "isValid") && DiagnosticCodes(cyclic).Contains("subflow_cycle"), "recursive Subflow calls were accepted");

        object incompatible = BuildPackage(storeId, "publish-exit-mismatch");
        object caller = First(GetField(incompatible, "flows"));
        object callee = SimpleFlow("subflow.callee", "callee.end", "subflow");
        object call = New("StepRecord");
        SetField(call, "id", "call.step"); SetField(call, "kind", "subflow"); SetField(call, "calledFlowRef", ContentRef(storeId, "subflow.callee", GetField(callee, "revision") as string));
        SetField(call, "dataJson", "{}"); Add(GetField(caller, "steps"), call);
        object exit = New("FlowExitRecord"); SetField(exit, "name", "accepted");
        SetField(exit, "contractJson", "{\"outputs\":[{\"name\":\"answer\",\"type\":\"text\",\"sourceName\":\"answer\",\"required\":true}]}");
        Add(GetField(callee, "exits"), exit); Add(GetField(incompatible, "flows"), callee);
        object answerLocal = New("FlowVariableRecord"); SetField(answerLocal, "name", "answer");
        SetField(answerLocal, "valueType", "text"); SetField(answerLocal, "mutable", false); SetField(answerLocal, "defaultValueJson", "\"answer\"");
        Add(GetField(callee, "variables"), answerLocal);
        object answerOutput = New("FlowParameterRecord"); SetField(answerOutput, "name", "answer");
        SetField(answerOutput, "direction", "output"); SetField(answerOutput, "valueType", "text"); SetField(answerOutput, "required", true);
        Add(GetField(callee, "parameters"), answerOutput);
        object transition = New("TransitionRecord"); SetField(transition, "id", "call.return"); SetField(transition, "fromStepId", "call.step");
        SetField(transition, "toStepId", "end"); SetField(transition, "purpose", "subflow_exit"); Add(GetField(caller, "transitions"), transition);
        object mapping = New("ExitMappingRecord"); SetField(mapping, "callStepId", "call.step");
        SetField(mapping, "calleeFlowRef", ContentRef(storeId, "subflow.callee", GetField(callee, "revision") as string));
        SetField(mapping, "exitName", "accepted"); SetField(mapping, "transitionId", "call.return"); SetField(mapping, "outputMappingsJson", "[]");
        Add(GetField(caller, "exitMappings"), mapping);
        object badExit = ValidatePackage(incompatible, vocabulary);
        Require(!(bool)GetField(badExit, "isValid") && DiagnosticCodes(badExit).Contains("subflow_exit_mapping_incomplete"),
            "a required named exit output without a caller mapping was accepted");

        object missingInputPackage = BuildPackage(storeId, "publish-subflow-input");
        object inputCaller = First(GetField(missingInputPackage, "flows"));
        object inputCallee = SimpleFlow("subflow.input", "callee.end", "subflow");
        object requiredInput = New("FlowParameterRecord"); SetField(requiredInput, "name", "message");
        SetField(requiredInput, "direction", "input"); SetField(requiredInput, "valueType", "text"); SetField(requiredInput, "required", true);
        Add(GetField(inputCallee, "parameters"), requiredInput);
        object emptyExit = New("FlowExitRecord"); SetField(emptyExit, "name", "done"); SetField(emptyExit, "contractJson", "{\"outputs\":[]}");
        Add(GetField(inputCallee, "exits"), emptyExit);
        Add(GetField(missingInputPackage, "flows"), inputCallee);
        object inputCall = New("StepRecord"); SetField(inputCall, "id", "input.call"); SetField(inputCall, "kind", "subflow");
        SetField(inputCall, "calledFlowRef", ContentRef(storeId, "subflow.input", GetField(inputCallee, "revision") as string));
        SetField(inputCall, "dataJson", "{}"); Add(GetField(inputCaller, "steps"), inputCall);
        object inputTransition = New("TransitionRecord"); SetField(inputTransition, "id", "input.return");
        SetField(inputTransition, "fromStepId", "input.call"); SetField(inputTransition, "toStepId", "end");
        SetField(inputTransition, "purpose", "subflow_exit"); Add(GetField(inputCaller, "transitions"), inputTransition);
        object inputMapping = New("ExitMappingRecord"); SetField(inputMapping, "callStepId", "input.call");
        SetField(inputMapping, "calleeFlowRef", ContentRef(storeId, "subflow.input", GetField(inputCallee, "revision") as string));
        SetField(inputMapping, "exitName", "done"); SetField(inputMapping, "transitionId", "input.return");
        SetField(inputMapping, "outputMappingsJson", "[]"); Add(GetField(inputCaller, "exitMappings"), inputMapping);
        object missingInputResult = ValidatePackage(missingInputPackage, vocabulary);
        Require(!(bool)GetField(missingInputResult, "isValid") && DiagnosticCodes(missingInputResult).Contains("subflow_required_input_missing"),
            "a required Subflow input without a binding was accepted");

        object deepPackage = BuildPackage(storeId, "publish-subflow-depth");
        var deepFlows = new List<object>();
        for (int index = 0; index < 9; index++)
        {
            string id = "subflow.depth." + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            object nested = SimpleFlow(id, "call." + index.ToString(System.Globalization.CultureInfo.InvariantCulture), "subflow");
            object nestedCall = First(GetField(nested, "steps"));
            if (index < 8)
            {
                SetField(nestedCall, "kind", "subflow");
                string nextId = "subflow.depth." + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                SetField(nestedCall, "calledFlowRef", ContentRef(storeId, nextId, Hash("flow." + nextId)));
                object returnStep = New("StepRecord"); SetField(returnStep, "id", "return." + index); SetField(returnStep, "kind", "end"); SetField(returnStep, "dataJson", "{}");
                Add(GetField(nested, "steps"), returnStep);
                object returnTransition = New("TransitionRecord"); SetField(returnTransition, "id", "return." + index);
                SetField(returnTransition, "fromStepId", GetField(nestedCall, "id") as string); SetField(returnTransition, "toStepId", GetField(returnStep, "id") as string);
                SetField(returnTransition, "purpose", "subflow_exit"); Add(GetField(nested, "transitions"), returnTransition);
            }
            object nestedExit = New("FlowExitRecord"); SetField(nestedExit, "name", "done"); SetField(nestedExit, "contractJson", "{\"outputs\":[]}");
            Add(GetField(nested, "exits"), nestedExit);
            deepFlows.Add(nested);
        }
        for (int index = 0; index < 8; index++)
        {
            object nested = deepFlows[index];
            string nextId = "subflow.depth." + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            object depthMapping = New("ExitMappingRecord");
            SetField(depthMapping, "callStepId", "call." + index); SetField(depthMapping, "calleeFlowRef", ContentRef(storeId, nextId, Hash("flow." + nextId)));
            SetField(depthMapping, "exitName", "done"); SetField(depthMapping, "transitionId", "return." + index); SetField(depthMapping, "outputMappingsJson", "[]");
            Add(GetField(nested, "exitMappings"), depthMapping);
            Add(GetField(deepPackage, "flows"), nested);
        }
        Add(GetField(deepPackage, "flows"), deepFlows[8]);
        object depthResult = ValidatePackage(deepPackage, vocabulary);
        Require(!(bool)GetField(depthResult, "isValid") && DiagnosticCodes(depthResult).Contains("subflow_depth_exceeded"),
            "Subflow nesting beyond the documented depth limit was accepted");

        object expandedPackage = BuildPackage(storeId, "publish-subflow-expanded");
        object expandedCaller = First(GetField(expandedPackage, "flows"));
        for (int index = 0; index < 2; index++)
        {
            string calleeId = "subflow.wide." + index;
            object wide = SimpleFlow(calleeId, "wide.0", "subflow");
            for (int stepIndex = 1; stepIndex < 33; stepIndex++)
            {
                object wideStep = New("StepRecord"); SetField(wideStep, "id", "wide." + stepIndex);
                SetField(wideStep, "kind", "end"); SetField(wideStep, "dataJson", "{}"); Add(GetField(wide, "steps"), wideStep);
            }
            object wideExit = New("FlowExitRecord"); SetField(wideExit, "name", "done"); SetField(wideExit, "contractJson", "{\"outputs\":[]}");
            Add(GetField(wide, "exits"), wideExit);
            Add(GetField(expandedPackage, "flows"), wide);

            string callId = "wide.call." + index;
            object wideCall = New("StepRecord"); SetField(wideCall, "id", callId); SetField(wideCall, "kind", "subflow");
            SetField(wideCall, "calledFlowRef", ContentRef(storeId, calleeId, GetField(wide, "revision") as string));
            SetField(wideCall, "dataJson", "{}"); Add(GetField(expandedCaller, "steps"), wideCall);
            object callTransition = New("TransitionRecord"); SetField(callTransition, "id", "wide.return." + index);
            SetField(callTransition, "fromStepId", callId); SetField(callTransition, "toStepId", "end");
            SetField(callTransition, "purpose", "subflow_exit"); Add(GetField(expandedCaller, "transitions"), callTransition);
            object callMapping = New("ExitMappingRecord"); SetField(callMapping, "callStepId", callId);
            SetField(callMapping, "calleeFlowRef", ContentRef(storeId, calleeId, GetField(wide, "revision") as string));
            SetField(callMapping, "exitName", "done"); SetField(callMapping, "transitionId", "wide.return." + index);
            SetField(callMapping, "outputMappingsJson", "[]"); Add(GetField(expandedCaller, "exitMappings"), callMapping);
        }
        object expandedResult = ValidatePackage(expandedPackage, vocabulary);
        Require(!(bool)GetField(expandedResult, "isValid") && DiagnosticCodes(expandedResult).Contains("flow_expanded_step_limit"),
            "expanded Subflow graph beyond sixty-four Steps was accepted");
    }

    private static void PublishCas(string root)
    {
        ContentFixture fixture = WriteSource(root, "publish/generic.json", "generic_dialogue", GenericRevisionFixture("publish"));
        string path = Path.Combine(root, "publish-cas", "content.sqlite3");
        string storeId = Guid.NewGuid().ToString("D");
        object repository = OpenRepositoryWithStoreId(path, "ReadWrite", storeId);
        object manifest = BuildManifest(fixture, "bake.publish"); SetField(manifest, "storeId", storeId);
        object imported = ImportManifest(manifest);
        Require(Status(imported) == "ready", "publication fixture did not import");
        ContentPackage package = (ContentPackage)Value(imported);
        package.anchors.Clear(); package.anchorLinks.Clear();
        object vocabulary = HostVocabulary(new string[0], new[] { "gentle" }, new string[0], new string[0], new string[0]);
        MethodInfo validatedCompile = ContentType("ContentReleaseCompiler").GetMethod("Compile",
            new[] { ContentType("ContentPackage"), ContentType("ContentHostVocabulary") });
        Require(validatedCompile != null, "validated ContentReleaseCompiler.Compile overload is missing");
        object compiled = validatedCompile.Invoke(null, new object[] { package, vocabulary });
        Require(Status(compiled) == "ready", "validated release did not compile: " + Detail(compiled));
        package = (ContentPackage)GetField(compiled, "package");
        Require(Status(Invoke(repository, "Stage", package)) == "ready", "compiled release did not stage");
        object release = GetField(compiled, "release");
        object semanticSnapshot = LoadSemanticSnapshot(repository, release, string.Empty);
        Require(Status(semanticSnapshot) == "ready", "the empty semantic index snapshot did not load for content without anchors");
        object forged = New("ContentRelease");
        foreach (string field in new[] { "releaseId", "manifestJson", "manifestHash", "validatorVersion", "graphFingerprint", "indexFingerprint", "dependencies" })
            SetField(forged, field, GetField(release, field));
        object unattested = InvokePublish(repository, forged, "authored", 0L, Value(semanticSnapshot));
        Require(Status(unattested) == "incompatible", "a release-shaped record bypassed the host compiler validation path");
        object blocked = InvokePublish(repository, release, "authored", 0L, null);
        Require(Status(blocked) == "unavailable" || Status(blocked) == "invalid", "publication succeeded without index readiness");
        object absentHead = Invoke(repository, "ReadReleaseHead", "authored");
        Require(Status(absentHead) == "missing", "failed index readiness changed the release head");
        object published = InvokePublish(repository, release, "authored", 0L, Value(semanticSnapshot));
        Require(Status(published) == "ready" && (long)Value(published) == 1L, "initial release CAS publish failed");
        object conflict = InvokePublish(repository, release, "authored", 0L, Value(semanticSnapshot));
        Require(Status(conflict) == "invalid" || Status(conflict) == "incompatible", "stale expected-head revision was accepted");
        object head = Invoke(repository, "ReadReleaseHead", "authored");
        Require(Status(head) == "ready" && (long)GetField(Value(head), "revision") == 1L &&
            GetField(Value(head), "releaseId") as string == GetField(release, "releaseId") as string,
            "CAS conflict replaced the previously published head");
    }

    private static void GenericDialogueProjection(string root)
    {
        const string json = "{\"schemaVersion\":1,\"id\":\"generic.sample\",\"source\":\"generic\",\"routing\":{\"routeId\":\"sample_route\",\"priority\":1.15,\"startHour\":8,\"endHour\":20,\"examples\":[\"say hello\"],\"requiredContext\":[],\"blockedContext\":[],\"eligibility\":null},\"nodes\":[{\"id\":\"ask\",\"speaker\":\"alterra\",\"text\":\"Choose?\",\"displayText\":\"Choose?\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"hello.audio\",\"nextId\":\"\",\"presentation\":{\"expression\":\"shy\",\"animation\":\"wave\"},\"choices\":[{\"label\":\"yes\",\"nextId\":\"yes\"},{\"label\":\"no\",\"nextId\":\"no\"}]},{\"id\":\"yes\",\"speaker\":\"alterra\",\"text\":\"Yes.\",\"displayText\":\"Yes.\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"warm\",\"animation\":\"\"},\"choices\":[]},{\"id\":\"no\",\"speaker\":\"alterra\",\"text\":\"No.\",\"displayText\":\"No.\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"gentle\",\"animation\":\"\"},\"choices\":[]}]}";
        ContentFixture fixture = WriteSource(root, "generic/sample.json", "generic_dialogue", json);
        object result = Import(fixture, "bake.generic");
        Require(Status(result) == "ready", "Generic import status=" + Status(result) + ": " + Detail(result));
        object package = Value(result);
        object legacy = First(GetField(package, "legacySources"));
        Require(GetField(legacy, "sourceId") as string == "generic.sample", "Generic source identity changed");
        Require(GetField(legacy, "sourceHash") as string == fixture.hash, "source byte hash was not retained");
        string metadata = GetField(legacy, "metadataJson") as string;
        Require(metadata.Contains("sample_route") && metadata.Contains("1.15") && metadata.Contains("say hello"), "route metadata was dropped: " + metadata);
        object flow = First(GetField(package, "flows"));
        Require(((IList)GetField(flow, "steps")).Count == 3, "Generic node count was not preserved");
        Require(((IList)GetField(flow, "choices")).Count == 2, "Generic choices were not projected");
        Require(((IList)GetField(flow, "transitions")).Count >= 2, "Generic graph edges were not projected");
        string presentation = GetField(First(GetField(First(GetField(package, "dialogues")), "beats")), "presentationJson") as string;
        Require(presentation.Contains("hello.audio") && presentation.Contains("wave") && presentation.Contains("alterra"), "voice/presentation metadata was dropped");
        object compilation = LegacyCompile(package);
        Require(Status(compilation) == "ready", "release compiler status=" + Status(compilation) + ": " + Detail(compilation));
        string releaseId = GetField(GetField(compilation, "release"), "releaseId") as string;
        object repeated = LegacyCompile(package);
        Require(Status(repeated) == "ready" && releaseId == GetField(GetField(repeated, "release"), "releaseId") as string,
            "same source package did not produce the same immutable release ID");
    }

    private static void AmbientRuntimeStateIsNotBaked(string root)
    {
        const string json = "{\"metadata\":{\"mode\":\"ambient\",\"topic\":\"test\",\"triggerConditions\":[]},\"lines\":[{\"id\":\"line.one\",\"text\":\"Hello\",\"translatedText\":\"Hello\",\"language\":\"english\",\"playCount\":8,\"lastPlayTime\":123456789,\"isGenerated\":false}]}";
        ContentFixture fixture = WriteSource(root, "idle/idle_pool.json", "ambient_pool", json);
        object imported = Import(fixture, "bake.ambient-projection");
        Require(Status(imported) == "ready", "ambient import status=" + Status(imported) + ": " + Detail(imported));
        object source = First(GetField(Value(imported), "legacySources"));
        byte[] runtimeBytes = GetField(source, "sourceBytes") as byte[];
        FieldInfo payloadHashField = source.GetType().GetField("payloadHash");
        Require(payloadHashField != null, "legacy source rows do not separate the authored hash from their runtime payload hash");
        string payloadHash = payloadHashField.GetValue(source) as string;
        string runtimeJson = new UTF8Encoding(false, true).GetString(runtimeBytes);
        JsonObject runtime = JToken.Parse(runtimeJson) as JsonObject;
        JsonArray lines = runtime == null ? null : runtime["lines"] as JsonArray;
        Require(GetField(source, "sourceHash") as string == fixture.hash && payloadHash == Hash(runtimeBytes) && payloadHash != fixture.hash &&
            lines != null && ((JsonObject)lines[0])["playCount"].ToString() == "0" && ((JsonObject)lines[0])["lastPlayTime"].ToString() == "0",
            "runtime repetition counters from the shared asset were copied into the shipped SQL payload");
    }

    private static void TopicProjection(string root)
    {
        const string json = "{\"schemaVersion\":1,\"topicId\":\"topic.sample\",\"menuLabel\":\"Pick one\",\"prompt\":\"What now?\",\"anchors\":[\"sample phrase\"],\"defaultReplayPolicy\":\"once\",\"cooldownSeconds\":42,\"scenarios\":[{\"scenarioId\":\"scenario.sample\",\"priority\":3,\"baseWeight\":2.5,\"condition\":{\"kind\":\"timeWindow\",\"expected\":true,\"children\":[],\"startHour\":8,\"endHour\":20},\"channels\":[\"direct\",\"phone\"],\"modifiers\":[{\"condition\":{\"kind\":\"fact\",\"expected\":true,\"key\":\"is_home\",\"children\":[]},\"amount\":0.5}],\"consumptionNodeId\":\"checkpoint\",\"nodes\":[{\"id\":\"checkpoint\",\"speaker\":\"alterra\",\"text\":\"こんにちは\",\"displayText\":\"Hello\",\"language\":\"japanese\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"happy\",\"animation\":\"\"},\"choices\":[]}]}]}";
        ContentFixture fixture = WriteSource(root, "topics/topic.sample.json", "topic_definition", json);
        object result = Import(fixture, "bake.topic");
        Require(Status(result) == "ready", "Topic import status=" + Status(result) + ": " + Detail(result));
        object package = Value(result);
        object topic = First(GetField(package, "topics"));
        Require(GetField(topic, "id") as string == "topic.sample" && GetField(topic, "defaultReplayPolicy") as string == "once", "Topic identity or replay policy changed");
        Require((double)GetField(topic, "cooldownSeconds") == 42d, "Topic cooldown changed");
        object entry = First(GetField(topic, "entries"));
        Require((int)GetField(entry, "priority") == 3 && (double)GetField(entry, "baseWeight") == 2.5d, "scenario priority/weight changed");
        Require(GetField(entry, "consumptionNodeId") as string == "checkpoint", "consumption checkpoint was dropped");
        Require((GetField(entry, "conditionJson") as string).Contains("timeWindow"), "condition gate was dropped");
        Require((GetField(entry, "modifiersJson") as string).Contains("is_home"), "modifier gate was dropped");
        Require((GetField(entry, "channelsJson") as string).Contains("direct") && (GetField(entry, "channelsJson") as string).Contains("phone"), "Topic channel gates were dropped");
        object titleText = FindText(package, GetField(topic, "titleTextRef"));
        object promptText = FindText(package, GetField(topic, "promptTextRef"));
        Require(First(GetField(titleText, "translations")) != null && GetField(First(GetField(titleText, "translations")), "text") as string == "Pick one", "Topic menu label was dropped");
        Require(GetField(First(GetField(promptText, "translations")), "text") as string == "What now?", "Topic prompt was dropped");
        Require(((IList)GetField(package, "anchors")).Cast<object>().Any(item => GetField(item, "text") as string == "sample phrase"), "Topic anchor was dropped");
        object legacy = First(GetField(package, "legacySources"));
        Require(GetField(legacy, "sourceId") as string == "topic.sample", "Topic source identity mapping changed");
    }

    private static void TopicScenarioReferenceProjection(string root)
    {
        const string scenarioJson = "{\"scenarioId\":\"scenario.shared\",\"topicId\":\"\",\"entryStepId\":\"start\",\"steps\":[{\"id\":\"start\",\"kind\":\"dialogue\",\"beats\":[{\"id\":\"start.beat\",\"text\":\"こんにちは\",\"displayText\":\"Hello\",\"language\":\"japanese\",\"expression\":\"\"}],\"nextStepId\":\"end\"},{\"id\":\"end\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        string storeId = Guid.NewGuid().ToString("D");
        byte[] scenarioBytes = Encoding.UTF8.GetBytes(scenarioJson);
        var scenarioSource = new ContentSourceDocument("scenario_definition", "scenarios/scenario.shared.json",
            "scenario.shared", Hash(scenarioBytes), scenarioBytes);
        object scenarioResult = ContentType("LegacyContentAdapter").GetMethod("ImportDocuments", new[]
            { typeof(string), typeof(string), typeof(IReadOnlyList<ContentSourceDocument>) })
            .Invoke(null, new object[] { "topic-scenario-ref-probe", storeId, new[] { scenarioSource } });
        Require(Status(scenarioResult) == "ready", "Scenario fixture import failed: " + Detail(scenarioResult));
        object scenarioFlow = First(GetField(Value(scenarioResult), "flows"));
        string revision = GetField(scenarioFlow, "revision") as string;
        string referenceJson = "{\"store\":\"" + storeId + "\",\"id\":\"scenario.shared\",\"revision\":\"" + revision +
            "\",\"expectedHash\":\"" + revision + "\"}";
        string topicJson = "{\"schemaVersion\":1,\"topicId\":\"topic.shared\",\"menuLabel\":\"Shared\",\"prompt\":\"Prompt\",\"anchors\":[\"shared scenario\"],\"defaultReplayPolicy\":\"repeatable\",\"cooldownSeconds\":0,\"scenarios\":[" +
            "{\"scenarioId\":\"entry.first\",\"priority\":2,\"baseWeight\":1,\"condition\":{\"kind\":\"fact\",\"key\":\"is_home\",\"expected\":true,\"children\":[]},\"channels\":[],\"modifiers\":[],\"consumptionNodeId\":\"end\",\"scenarioRef\":" + referenceJson + "}," +
            "{\"scenarioId\":\"entry.second\",\"priority\":1,\"baseWeight\":0.5,\"condition\":{\"kind\":\"fact\",\"key\":\"is_home\",\"expected\":true,\"children\":[]},\"channels\":[],\"modifiers\":[],\"consumptionNodeId\":\"end\",\"scenarioRef\":" + referenceJson + "}]}";
        byte[] topicBytes = Encoding.UTF8.GetBytes(topicJson);
        var sources = new[] { scenarioSource, new ContentSourceDocument("topic_definition", "topics/topic.shared.json",
            "topic.shared", Hash(topicBytes), topicBytes) };
        object result = ContentType("LegacyContentAdapter").GetMethod("ImportDocuments", new[]
            { typeof(string), typeof(string), typeof(IReadOnlyList<ContentSourceDocument>) })
            .Invoke(null, new object[] { "topic-scenario-reference", storeId, sources });
        Require(Status(result) == "ready", "Topic Scenario reference import failed: " + Detail(result) + " codes=" + DiagnosticCodes(result));
        object package = Value(result);
        Require(((IList)GetField(package, "flows")).Count == 1,
            "Topic reference created a copied Flow instead of reusing the standalone Scenario Flow");
        object topic = First(GetField(package, "topics"));
        IList entries = (IList)GetField(topic, "entries");
        Require(entries.Count == 2, "Topic entry count changed");
        foreach (object entry in entries)
        {
            object flowRef = GetField(entry, "flowRef");
            Require(GetField(entry, "id") as string != GetField(flowRef, "id") as string,
                "Topic entry identity was coupled to the reusable Scenario Flow identity");
            Require(GetField(entry, "flowRef") != null && GetField(flowRef, "id") as string == "scenario.shared" &&
                GetField(flowRef, "revision") as string == revision && GetField(flowRef, "expectedHash") as string == revision,
                "Topic entry did not preserve the exact pinned Scenario Flow reference");
            Require(GetField(entry, "consumptionNodeId") as string == "end", "Topic consumption checkpoint was dropped");
        }

        object missingFlow = ImportTopicWithScenarioReference(storeId, scenarioSource, storeId, "scenario.missing", revision, revision, false);
        Require(Status(missingFlow) == "invalid" && DiagnosticCodes(missingFlow).Contains("legacy_topic_scenario_ref_invalid"),
            "a Topic source with a missing Scenario Flow was not rejected: " + DiagnosticCodes(missingFlow));
        object foreignFlow = ImportTopicWithScenarioReference(storeId, scenarioSource, Guid.NewGuid().ToString("D"),
            "scenario.shared", revision, revision, false);
        Require(Status(foreignFlow) == "invalid" && DiagnosticCodes(foreignFlow).Contains("legacy_topic_scenario_ref_invalid"),
            "a cross-store Scenario Flow reference was not rejected: " + DiagnosticCodes(foreignFlow));
        object staleRevision = ImportTopicWithScenarioReference(storeId, scenarioSource, storeId, "scenario.shared", new string('0', 64),
            new string('0', 64), false);
        Require(Status(staleRevision) == "invalid" && DiagnosticCodes(staleRevision).Contains("legacy_topic_scenario_ref_invalid"),
            "a stale Scenario Flow revision was not rejected: " + DiagnosticCodes(staleRevision));
        object badHash = ImportTopicWithScenarioReference(storeId, scenarioSource, storeId, "scenario.shared", revision,
            new string('0', 64), false);
        Require(Status(badHash) == "invalid" && DiagnosticCodes(badHash).Contains("legacy_topic_scenario_ref_invalid"),
            "a mismatched Scenario Flow hash was not rejected: " + DiagnosticCodes(badHash));
        object bothSources = ImportTopicWithScenarioReference(storeId, scenarioSource, storeId, "scenario.shared", revision, revision, true);
        Require(Status(bothSources) == "invalid" && DiagnosticCodes(bothSources).Contains("legacy_topic_invalid"),
            "a Topic entry that supplied both scenarioRef and inline nodes was not rejected: " + DiagnosticCodes(bothSources));

        ContentFixture scenarioFixture = WriteSource(root, "scenario-ref/scenario.shared.json", "scenario_definition", scenarioJson);
        object manifest = BuildManifest(scenarioFixture, "bake.topic-scenario-reference");
        SetField(manifest, "storeId", storeId);
        ContentFixture topicFixture = WriteSource(root, "scenario-ref/topic.shared.json", "topic_definition", topicJson);
        object topicSource = New("ContentSourceFile");
        SetField(topicSource, "kind", topicFixture.kind);
        SetField(topicSource, "relativePath", topicFixture.path);
        SetField(topicSource, "sha256", topicFixture.hash);
        Add(GetField(manifest, "sources"), topicSource);
        string output = Path.Combine(root, "staging", "topic-scenario-reference.sqlite3");
        string streaming = Path.Combine(root, "StreamingAssets");
        string semanticHash = Hash("topic scenario synthetic semantic fixture");
        string semanticJson = "{\"schemaVersion\":2,\"model\":\"Alibaba-NLP/gte-multilingual-base\",\"modelRevision\":\"synthetic\",\"codeRevision\":\"synthetic\",\"dimension\":1,\"contentHash\":\"" + semanticHash +
            "\",\"anchors\":[{\"ownerKind\":\"topic\",\"ownerId\":\"topic.shared\",\"ownerRevision\":\"" + (GetField(topic, "revision") as string) +
            "\",\"role\":\"anchor_0000\",\"text\":\"shared scenario\"}]}";
        var vocabulary = new ContentHostVocabulary(new string[0], new string[0], new string[0], new string[0], new string[0]);
        ContentResult<ContentBakeArtifact> baked = ContentBakeService.Bake((ContentSourceManifest)manifest, output, streaming,
            new ContentSemanticBuildInput
            {
                metadataUtf8 = Encoding.UTF8.GetBytes(semanticJson), vectorBytes = BitConverter.GetBytes(1f),
                tokenizerBytes = Encoding.UTF8.GetBytes("{}"), expectedContentHash = semanticHash, hostVocabulary = vocabulary
            });
        Require(baked.IsReady, "reusable Topic Scenario SQL bake failed: " + Detail(baked));
        object configuration = New("ContentRuntimeConfiguration");
        SetField(configuration, "databasePath", output);
        SetField(configuration, "expectedReleaseId", baked.value.releaseId);
        object opened = ContentType("RuntimeContentStore").GetMethod("OpenTrustedRelease").Invoke(null, new[] { configuration });
        Require(Status(opened) == "ready", "reusable Topic Scenario SQL open failed: " + Detail(opened));
        Type runtimeStore = ContentType("RuntimeContentStore");
        Type referenceType = ContentType("ContentRef");
        MethodInfo readTopic = runtimeStore.GetMethod("ReadTopic", new[] { referenceType });
        Require(readTopic != null, "RuntimeContentStore.ReadTopic(ContentRef) API is missing");
        object topicReference = ContentRef(storeId, "topic.shared", GetField(topic, "revision") as string);
        object readResult = readTopic.Invoke(Value(opened), new[] { topicReference });
        Require(Status(readResult) == "ready" && ((IList)GetField(Value(readResult), "entries")).Count == 2,
            "runtime SQL Topic read did not return the exact reusable entries: " + Detail(readResult));
        object foreignTopic = readTopic.Invoke(Value(opened), new[]
            { ContentRef(Guid.NewGuid().ToString("D"), "topic.shared", GetField(topic, "revision") as string) });
        Require(Status(foreignTopic) == "incompatible", "runtime SQL Topic read accepted a foreign-store reference");

        Type preparationType = typeof(IDictionary<string, FlowValue>);
        MethodInfo prepare = runtimeStore.GetMethod("PrepareTopicEntry", new[]
        {
            referenceType, typeof(string), preparationType, typeof(ScenarioRuntimeSnapshot),
            ContentType("ContentHostVocabulary"), typeof(string), typeof(string)
        });
        Require(prepare != null, "RuntimeContentStore.PrepareTopicEntry exact facade API is missing");
        var context = new ScenarioRuntimeSnapshot("scenario.shared", "run.shared", "turn.shared", 1, "",
            new string[0], new string[0], new ScenarioCapabilitySnapshot[0], new ScenarioObjectSnapshot[0], new string[0]);
        object planResult = prepare.Invoke(Value(opened), new object[] { topicReference, "entry.second",
            new Dictionary<string, FlowValue>(), context, vocabulary, "en", "ja" });
        Require(Status(planResult) == "ready" && GetProperty(Value(planResult), "FlowId") as string == "scenario.shared",
            "runtime SQL Topic entry did not prepare the exact pinned Scenario Flow: " + Detail(planResult));
        object missingEntry = prepare.Invoke(Value(opened), new object[] { topicReference, "entry.missing",
            new Dictionary<string, FlowValue>(), context, vocabulary, "en", "ja" });
        Require(Status(missingEntry) == "missing", "runtime preparation accepted an entry absent from the pinned Topic revision");

        string corruptPath = Path.Combine(root, "staging", "topic-scenario-reference-corrupt.sqlite3");
        File.Copy(output, corruptPath, true);
        using (var connection = new SQLiteConnection(corruptPath, SQLiteOpenFlags.ReadWrite))
            connection.Execute("UPDATE steps SET data_json=? WHERE flow_id=? AND flow_revision=? AND step_id=?",
                "{", "scenario.shared", GetField(First(GetField(Value(readResult), "entries")), "flowRef") is ContentRef
                    ? (GetField(First(GetField(Value(readResult), "entries")), "flowRef") as ContentRef).revision
                    : revision, "end");
        object corruptConfiguration = New("ContentRuntimeConfiguration");
        SetField(corruptConfiguration, "databasePath", corruptPath);
        SetField(corruptConfiguration, "expectedReleaseId", baked.value.releaseId);
        object corruptOpened = ContentType("RuntimeContentStore").GetMethod("OpenTrustedRelease").Invoke(null, new[] { corruptConfiguration });
        Require(Status(corruptOpened) == "ready", "fixture corruption should leave the release/source manifest openable for the compiled-row check");
        object corruptPlan = prepare.Invoke(Value(corruptOpened), new object[] { topicReference, "entry.second",
            new Dictionary<string, FlowValue>(), context, vocabulary, "en", "ja" });
        Require(Status(corruptPlan) != "ready", "runtime preparation masked a corrupt compiled SQL Flow row with unchanged source bytes");
    }

    private static void ExecutionSnapshotPreservesReusableRows()
    {
        const string sequenceArguments = "[{\"name\":\"target\",\"value\":\"fixture-target\"}]";
        var package = new ContentPackage();
        var sequence = new ActionRecord
        {
            id = "action.sequence", revision = Hash("sequence.revision"), semanticHash = Hash("sequence.semantic"),
            capabilityId = "locate", contractJson = "{\"inputs\":[]}", implementationKind = "activity_sequence",
            composerId = "fixture.locate", functionId = "", argumentsJson = "{}",
            sequenceSteps = new List<ActionSequenceStepRecord>
            {
                new ActionSequenceStepRecord { ordinal = 3, stepId = "reserve", primitiveId = "reserve_nearest",
                    argumentsJson = sequenceArguments, outputsJson = "[{\"name\":\"target\"}]", successEdgeJson = "{\"next\":\"done\"}",
                    failureEdgeJson = "{\"next\":\"fail\"}", cancelEdgeJson = "{\"next\":\"cancel\"}", checkpointPolicy = "after" }
            }
        };
        var effect = new ActionRecord
        {
            id = "action.effect", revision = Hash("effect.revision"), semanticHash = Hash("effect.semantic"),
            capabilityId = "player.item.grant", contractJson = "{}", implementationKind = "host_effect",
            composerId = "", functionId = "player.item.grant", argumentsJson = "{\"itemRef\":{\"id\":\"item.water\"},\"quantity\":1}"
        };
        var furniture = new FurnitureDefinitionRecord
        {
            id = "furniture.tv", revision = Hash("furniture.revision"), semanticHash = Hash("furniture.semantic"),
            displayTextRef = new ContentRef("store", "text.tv", Hash("text.tv")), definitionJson = "{\"definitionId\":\"furniture.tv\"}",
            functionBindings = new List<FurnitureFunctionBindingRecord>
            {
                new FurnitureFunctionBindingRecord { functionId = "power.set", pointId = "screen", useId = "interact",
                    functionContractVersion = 1, contractJson = "{\"desiredState\":[\"Off\",\"On\"]}" }
            }
        };
        package.actions.Add(sequence);
        package.actions.Add(effect);
        package.items.Add(new ItemRecord { id = "item.water", revision = Hash("item.revision"), semanticHash = Hash("item.semantic"),
            displayTextRef = new ContentRef("store", "text.water", Hash("text.water")), stackLimit = 9 });
        package.furnitureDefinitions.Add(furniture);

        MethodInfo snapshotMethod = ContentType("ContentFlowAdapter").GetMethod("CreateExecutionSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        Require(snapshotMethod != null, "the validated execution snapshot seam is missing");
        var snapshot = (ContentPackage)snapshotMethod.Invoke(null, new object[] { package });
        ActionRecord copiedSequence = snapshot.actions.Single(item => item.id == sequence.id);
        ActionRecord copiedEffect = snapshot.actions.Single(item => item.id == effect.id);
        Require(copiedSequence.implementationKind == sequence.implementationKind && copiedSequence.composerId == sequence.composerId &&
            copiedSequence.sequenceSteps.Count == 1 && copiedSequence.sequenceSteps[0].ordinal == 3 &&
            copiedSequence.sequenceSteps[0].argumentsJson == sequenceArguments && copiedSequence.sequenceSteps[0].outputsJson == sequence.sequenceSteps[0].outputsJson &&
            copiedSequence.sequenceSteps[0].successEdgeJson == sequence.sequenceSteps[0].successEdgeJson &&
            copiedSequence.sequenceSteps[0].failureEdgeJson == sequence.sequenceSteps[0].failureEdgeJson &&
            copiedSequence.sequenceSteps[0].cancelEdgeJson == sequence.sequenceSteps[0].cancelEdgeJson &&
            copiedSequence.sequenceSteps[0].checkpointPolicy == "after",
            "the execution snapshot erased an activity_sequence discriminator or normalized sequence field");
        Require(copiedEffect.implementationKind == "host_effect" && copiedEffect.functionId == "player.item.grant" &&
            copiedEffect.argumentsJson == effect.argumentsJson, "the execution snapshot erased host-effect implementation fields");
        Require(snapshot.items.Count == 1 && snapshot.items[0].id == "item.water" && snapshot.items[0].semanticHash == package.items[0].semanticHash &&
            snapshot.furnitureDefinitions.Count == 1 && snapshot.furnitureDefinitions[0].definitionJson == furniture.definitionJson &&
            snapshot.furnitureDefinitions[0].functionBindings.Single().contractJson == furniture.functionBindings[0].contractJson,
            "the execution snapshot dropped the selected Item/Furniture closure or nested function binding");
        sequence.sequenceSteps[0].argumentsJson = "[]";
        furniture.functionBindings[0].contractJson = "{}";
        Require(copiedSequence.sequenceSteps[0].argumentsJson == sequenceArguments &&
            snapshot.furnitureDefinitions[0].functionBindings[0].contractJson != "{}",
            "the execution snapshot aliases mutable Action/Furniture source lists");
    }

    private static void TopicScenarioExecutionClosure(string root)
    {
        string storeId = Guid.NewGuid().ToString("D");
        const string itemJson = "{\"schemaVersion\":1,\"id\":\"item.water_glass\",\"displayName\":{\"id\":\"text.water_glass\",\"translations\":[{\"language\":\"en\",\"text\":\"Water glass\"}]},\"stackLimit\":99}";
        const string dialogueJson = "{\"schemaVersion\":1,\"id\":\"dialogue.cheer\",\"beats\":[{\"beatId\":\"cheer\",\"text\":{\"id\":\"text.cheer\",\"translations\":[{\"language\":\"en\",\"text\":\"Here, cheer!\"}]},\"expression\":\"Neutral\"}]}";
        const string furnitureJson = "{\"schemaVersion\":1,\"id\":\"furniture.tv.basic\",\"displayName\":{\"id\":\"text.tv\",\"translations\":[{\"language\":\"en\",\"text\":\"TV\"}]},\"definition\":{\"definitionId\":\"furniture.tv.basic\",\"displayNameKey\":\"text.tv\",\"category\":\"tv\",\"scalePolicy\":\"fixed\",\"defaultScale\":{\"x\":1,\"y\":1,\"z\":1},\"localBounds\":{\"minimum\":{\"x\":-0.5,\"y\":0,\"z\":-0.25},\"maximum\":{\"x\":0.5,\"y\":1,\"z\":0.25}},\"supportKinds\":[\"roomFloor\"],\"blocksNavigation\":true,\"points\":[{\"pointId\":\"screen\",\"pointTypeId\":\"tv.screen\",\"localPosition\":{\"x\":0,\"y\":0.5,\"z\":0},\"localFacing\":{\"x\":0,\"y\":0,\"z\":-1},\"useIds\":[\"interact\"]}],\"functionBindings\":[{\"functionId\":\"power.set\",\"pointId\":\"screen\",\"useId\":\"interact\"}]},\"functions\":[{\"functionId\":\"power.set\",\"pointId\":\"screen\",\"useId\":\"interact\",\"contractVersion\":1,\"contract\":{\"desiredState\":[\"Off\",\"On\"]}}]}";
        string itemRevision = Hash(itemJson);
        const string sequenceJson = "{\"schemaVersion\":1,\"id\":\"action.locate_tv\",\"capabilityId\":\"scenario.complete\",\"implementationKind\":\"activity_sequence\",\"data\":{\"steps\":[{\"stepId\":\"finish\",\"primitiveId\":\"complete\",\"arguments\":[],\"resultBindings\":[],\"successNextStepId\":\"\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"}]}}";
        string sequenceRevision = Hash(sequenceJson);
        string effectJson = "{\"schemaVersion\":1,\"id\":\"action.grant_water\",\"capabilityId\":\"player.item.grant\",\"implementationKind\":\"host_effect\",\"data\":{\"functionId\":\"player.item.grant\",\"arguments\":{\"itemRef\":{\"store\":\"" + storeId + "\",\"id\":\"item.water_glass\",\"revision\":\"" + itemRevision + "\",\"expectedHash\":\"" + itemRevision + "\"},\"quantity\":1}}}";
        string effectRevision = Hash(effectJson);
        string sequenceRef = "{\"store\":\"" + storeId + "\",\"id\":\"action.locate_tv\",\"revision\":\"" + sequenceRevision + "\",\"expectedHash\":\"" + sequenceRevision + "\"}";
        string effectRef = "{\"store\":\"" + storeId + "\",\"id\":\"action.grant_water\",\"revision\":\"" + effectRevision + "\",\"expectedHash\":\"" + effectRevision + "\"}";
        string dialogueRef = "{\"store\":\"" + storeId + "\",\"id\":\"dialogue.cheer\",\"revision\":\"" + Hash(dialogueJson) + "\",\"expectedHash\":\"" + Hash(dialogueJson) + "\"}";
        string scenarioJson = "{\"scenarioId\":\"scenario.shared_execution\",\"topicId\":\"\",\"entryStepId\":\"greet\",\"steps\":[" +
            "{\"id\":\"greet\",\"kind\":\"dialogue\",\"dialogueRef\":" + dialogueRef + ",\"nextStepId\":\"sequence\"}," +
            "{\"id\":\"sequence\",\"kind\":\"action\",\"actionRef\":" + sequenceRef + ",\"successNextStepId\":\"grant\",\"failureNextStepId\":\"end\"}," +
            "{\"id\":\"grant\",\"kind\":\"host_effect\",\"actionRef\":" + effectRef + ",\"data\":{\"schemaVersion\":1,\"requiresCompletedStepId\":\"greet\"},\"successNextStepId\":\"end\",\"failureNextStepId\":\"end\"}," +
            "{\"id\":\"end\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        byte[] scenarioBytes = Encoding.UTF8.GetBytes(scenarioJson);
        var scenarioSource = new ContentSourceDocument(ContentSourceKinds.ScenarioDefinition,
            "Scenarios/scenario.shared_execution.json", "scenario.shared_execution", Hash(scenarioBytes), scenarioBytes);
        ContentResult<ContentPackage> scenarioProjection = LegacyContentAdapter.ImportDocuments("topic-runtime-fixture", storeId, new[] { scenarioSource });
        Require(scenarioProjection.IsReady, "the reusable Action Scenario source did not project: " + Detail(scenarioProjection));
        FlowRecord scenarioFlow = scenarioProjection.value.flows.Single();
        string topicRef = "{\"store\":\"" + storeId + "\",\"id\":\"scenario.shared_execution\",\"revision\":\"" + scenarioFlow.revision + "\",\"expectedHash\":\"" + scenarioFlow.semanticHash + "\"}";
        string topicJson = "{\"schemaVersion\":1,\"topicId\":\"topic.shared_execution\",\"menuLabel\":\"Shared\",\"prompt\":\"Choose\",\"anchors\":[\"shared execution\"],\"defaultReplayPolicy\":\"repeatable\",\"cooldownSeconds\":0,\"scenarios\":[{\"scenarioId\":\"entry.execute\",\"priority\":1,\"baseWeight\":1,\"condition\":{\"kind\":\"fact\",\"key\":\"is_home\",\"expected\":true,\"children\":[]},\"channels\":[],\"modifiers\":[],\"consumptionNodeId\":\"end\",\"scenarioRef\":" + topicRef + "}]}";

        ContentFixture scenarioFixture = WriteSource(root, "topic-runtime/scenario.shared_execution.json", "scenario_definition", scenarioJson);
        object manifest = BuildManifest(scenarioFixture, "bake.topic-runtime-reference");
        SetField(manifest, "storeId", storeId);
        var sources = new[]
        {
            new { kind = "topic_definition", path = "topic-runtime/topic.shared_execution.json", json = topicJson, sourceId = "topic.shared_execution" },
            new { kind = "item_definition", path = "topic-runtime/item.water_glass.json", json = itemJson, sourceId = "item.water_glass" },
            new { kind = "dialogue_definition", path = "topic-runtime/dialogue.cheer.json", json = dialogueJson, sourceId = "dialogue.cheer" },
            new { kind = "furniture_definition", path = "topic-runtime/furniture.tv.basic.json", json = furnitureJson, sourceId = "furniture.tv.basic" },
            new { kind = "action_definition", path = "topic-runtime/action.locate_tv.json", json = sequenceJson, sourceId = "action.locate_tv" },
            new { kind = "action_definition", path = "topic-runtime/action.grant_water.json", json = effectJson, sourceId = "action.grant_water" }
        };
        foreach (var source in sources)
        {
            ContentFixture fixture = WriteSource(root, source.path, source.kind, source.json);
            object manifestSource = New("ContentSourceFile");
            SetField(manifestSource, "kind", fixture.kind); SetField(manifestSource, "relativePath", fixture.path);
            SetField(manifestSource, "sha256", fixture.hash); Add(GetField(manifest, "sources"), manifestSource);
        }
        ContentResult<ContentPackage> imported = LegacyContentAdapter.Import((ContentSourceManifest)manifest);
        Require(imported.IsReady, "the reusable Action Topic source import failed: " + Detail(imported));
        TopicRecord topic = imported.value.topics.Single();
        string output = Path.Combine(root, "staging", "topic-runtime-reference.sqlite3");
        string streaming = Path.Combine(root, "StreamingAssets");
        string semanticHash = Hash("topic runtime semantic fixture");
        ActivitySequenceBuilder sequenceBuilder = ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder();
        string[] capabilities = ScenarioItemGrantPersistenceAdapter.RegisteredCapabilityIds
            .Concat(ActivitySequenceCatalogFactory.CreateDefault().Primitives.All.Select(item => item.requiredCapabilityId))
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        var vocabulary = new ContentHostVocabulary(capabilities, new[] { "Neutral" },
            new string[0], new string[0], new string[0], sequenceBuilder);
        string semanticJson = "{\"schemaVersion\":2,\"model\":\"Alibaba-NLP/gte-multilingual-base\",\"modelRevision\":\"synthetic\",\"codeRevision\":\"synthetic\",\"dimension\":1,\"contentHash\":\"" + semanticHash +
            "\",\"anchors\":[{\"ownerKind\":\"topic\",\"ownerId\":\"topic.shared_execution\",\"ownerRevision\":\"" + topic.revision +
            "\",\"role\":\"anchor_0000\",\"text\":\"shared execution\"}]}";
        var semanticInput = new ContentSemanticBuildInput
        {
            metadataUtf8 = Encoding.UTF8.GetBytes(semanticJson), vectorBytes = BitConverter.GetBytes(1f),
            tokenizerBytes = Encoding.UTF8.GetBytes("{}"), expectedContentHash = semanticHash, hostVocabulary = vocabulary
        };
        ContentResult<ContentBakeArtifact> baked = ContentBakeService.Bake((ContentSourceManifest)manifest, output, streaming, semanticInput);
        Require(baked.IsReady, "the reusable Action Topic SQL bake failed: " + Detail(baked) + " codes=" + DiagnosticCodes(baked) +
            " path=" + (baked.diagnostics.Count == 0 ? "" : baked.diagnostics[0].path));
        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
        { databasePath = output, expectedStoreId = storeId, expectedReleaseId = baked.value.releaseId });
        Require(opened.IsReady, "the reusable Action Topic SQL release did not open: " + Detail(opened));
        var context = new ScenarioRuntimeSnapshot("scenario.shared_execution", "run.execution", "turn.execution", 1, "",
            new string[0], new[] { "Neutral" }, new ScenarioCapabilitySnapshot[0], new ScenarioObjectSnapshot[0], new string[0]);
        ContentResult<ContentExecutionPlan> plan = opened.value.PrepareTopicEntry(new ContentRef(storeId, topic.id, topic.revision, topic.semanticHash),
            "entry.execute", new Dictionary<string, FlowValue>(), context, vocabulary);
        Require(plan.IsReady, "the compiled reusable Action Topic entry did not prepare: " + Detail(plan));
        ContentPackage execution = (ContentPackage)GetNonPublicField(plan.value, "package");
        ActionRecord sequence = execution.actions.Single(item => item.id == "action.locate_tv");
        ActionRecord effect = execution.actions.Single(item => item.id == "action.grant_water");
        Require(sequence.implementationKind == "activity_sequence" && sequence.revision == sequenceRevision &&
            sequence.semanticHash == sequenceRevision && sequence.sequenceSteps.Count == 1 && sequence.sequenceSteps[0].stepId == "finish" &&
            sequence.sequenceSteps[0].primitiveId == "complete" && sequence.sequenceSteps[0].checkpointPolicy == "none",
            "the referenced Scenario plan lost its exact compiled sequence Action identity or normalized rows");
        Require(effect.implementationKind == "host_effect" && effect.functionId == "player.item.grant" && effect.revision == effectRevision &&
            effect.argumentsJson.Contains(itemRevision) && execution.items.Single().id == "item.water_glass" &&
            execution.items.Single().revision == itemRevision && execution.furnitureDefinitions.Single().id == "furniture.tv.basic" &&
            execution.furnitureDefinitions.Single().functionBindings.Single().useId == "interact",
            "the referenced Scenario plan lost its exact compiled host-effect, Item or Furniture closure");

        RequireCompiledSqlMutationRejected(output, Path.Combine(root, "staging", "topic-runtime-corrupt-translation.sqlite3"),
            baked.value.releaseId, storeId, topic, context, vocabulary,
            new[] { "UPDATE translations SET text_value=? WHERE text_id=? AND language=?" },
            new[] { new object[] { "Here, water!", "text.cheer", "en" } }, "a valid-looking Translation body mutation");
        RequireCompiledSqlMutationRejected(output, Path.Combine(root, "staging", "topic-runtime-corrupt-action-parent.sqlite3"),
            baked.value.releaseId, storeId, topic, context, vocabulary,
            new[] { "UPDATE actions SET arguments_json=REPLACE(arguments_json,?,?) WHERE action_id=?" },
            new[] { new object[] { "\"quantity\":1", "\"quantity\":2", "action.grant_water" } },
            "a valid in-range host-effect Action body mutation");
        RequireCompiledSqlMutationRejected(output, Path.Combine(root, "staging", "topic-runtime-corrupt-sequence-row.sqlite3"),
            baked.value.releaseId, storeId, topic, context, vocabulary,
            new[] { "UPDATE action_sequence_steps SET arguments_json=? WHERE action_id=? AND step_id=?" },
            new[] { new object[] { "[ ]", "action.locate_tv", "finish" } }, "a valid Action sequence row mutation");
        RequireCompiledSqlMutationRejected(output, Path.Combine(root, "staging", "topic-runtime-corrupt-transition.sqlite3"),
            baked.value.releaseId, storeId, topic, context, vocabulary,
            new[]
            {
                "UPDATE steps SET data_json=REPLACE(data_json,?,?) WHERE flow_id=? AND step_id=?",
                "UPDATE transitions SET to_step_id=? WHERE flow_id=? AND from_step_id=? AND to_step_id=?"
            },
            new[]
            {
                new object[] { "\"nextStepId\":\"sequence\"", "\"nextStepId\":\"end\"", "scenario.shared_execution", "greet" },
                new object[] { "end", "scenario.shared_execution", "greet", "sequence" }
            }, "a consistent transition/Step redirection to another valid Step");
    }

    private static void RequireCompiledSqlMutationRejected(string sourcePath, string corruptedPath, string releaseId,
        string storeId, TopicRecord topic, ScenarioRuntimeSnapshot context, ContentHostVocabulary vocabulary,
        string[] statements, object[][] arguments, string label)
    {
        File.Copy(sourcePath, corruptedPath, true);
        using (var connection = new SQLiteConnection(corruptedPath, SQLiteOpenFlags.ReadWrite))
            for (int index = 0; index < statements.Length; index++)
                Require(connection.Execute(statements[index], arguments[index]) > 0,
                    "the SQL fixture did not apply " + label + " at statement " + index.ToString(CultureInfo.InvariantCulture));

        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
        { databasePath = corruptedPath, expectedStoreId = storeId, expectedReleaseId = releaseId });
        Require(opened.IsReady, "stored release labels should still open before validating " + label + ": " + Detail(opened));
        MethodInfo hydrate = typeof(ContentRepository).GetMethod("ReadCompiledPackageForRelease", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(hydrate != null, "the exact compiled SQL closure loader is missing");
        object closure = hydrate.Invoke(opened.value.Repository, new object[]
        { opened.value.Release, opened.value.BuildManifest.packageId, opened.value.BuildManifest.sourceManifestHash });
        Require(Status(closure) != "ready" && DiagnosticCodes(closure).Contains("compiled_row_hash_mismatch"),
            "closure hydration accepted " + label + " with original release/dependency labels: " + Detail(closure));
        ContentResult<ContentExecutionPlan> prepared = opened.value.PrepareTopicEntry(
            new ContentRef(storeId, topic.id, topic.revision, topic.semanticHash), "entry.execute",
            new Dictionary<string, FlowValue>(), context, vocabulary);
        Require(!prepared.IsReady && DiagnosticCodes(prepared).Contains("compiled_row_hash_mismatch"),
            "runtime Topic preparation accepted " + label + " with unchanged source bytes: " + Detail(prepared));
    }

    private static object ImportTopicWithScenarioReference(string storeId, ContentSourceDocument scenarioSource,
        string referenceStore, string referenceId, string revision, string expectedHash, bool includeInlineNodes)
    {
        string referenceJson = "{\"store\":\"" + referenceStore + "\",\"id\":\"" + referenceId + "\",\"revision\":\"" + revision +
            "\",\"expectedHash\":\"" + expectedHash + "\"}";
        string nodes = includeInlineNodes
            ? ",\"nodes\":[{\"id\":\"line\",\"speaker\":\"alterra\",\"text\":\"Hi\",\"displayText\":\"Hi\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"\",\"animation\":\"\"},\"choices\":[]}]"
            : string.Empty;
        string topicJson = "{\"schemaVersion\":1,\"topicId\":\"topic.reference.check\",\"menuLabel\":\"Shared\",\"prompt\":\"Prompt\",\"anchors\":[\"reference check\"],\"defaultReplayPolicy\":\"repeatable\",\"cooldownSeconds\":0,\"scenarios\":[" +
            "{\"scenarioId\":\"entry.reference\",\"priority\":1,\"baseWeight\":1,\"condition\":{\"kind\":\"fact\",\"key\":\"is_home\",\"expected\":true,\"children\":[]},\"channels\":[],\"modifiers\":[],\"consumptionNodeId\":\"end\",\"scenarioRef\":" + referenceJson + nodes + "}]}";
        byte[] bytes = Encoding.UTF8.GetBytes(topicJson);
        var topicSource = new ContentSourceDocument("topic_definition", "topics/topic.reference.check.json", "topic.reference.check",
            Hash(bytes), bytes);
        return ContentType("LegacyContentAdapter").GetMethod("ImportDocuments", new[]
            { typeof(string), typeof(string), typeof(IReadOnlyList<ContentSourceDocument>) })
            .Invoke(null, new object[] { "topic-scenario-reference-negative", storeId, new[] { scenarioSource, topicSource } });
    }

    private static void ScenarioProjection(string root)
    {
        const string json = "{\"scenarioId\":\"activity.sample\",\"topicId\":\"\",\"entryStepId\":\"start\",\"steps\":[{\"id\":\"start\",\"kind\":\"dialogue\",\"beats\":[{\"id\":\"start.beat\",\"text\":\"こんにちは\",\"displayText\":\"Hello\",\"language\":\"japanese\",\"expression\":\"gentle\"}],\"nextStepId\":\"ask\"},{\"id\":\"ask\",\"kind\":\"ask\",\"questionId\":\"ask.question\",\"question\":{\"id\":\"ask.question\",\"text\":\"続けますか？\",\"displayText\":\"Continue?\",\"language\":\"japanese\",\"expression\":\"thinking\"},\"options\":[{\"optionId\":\"yes\",\"label\":\"Yes\",\"value\":\"yes\",\"nextStepId\":\"action\"}],\"allowFreeText\":false,\"freeTextNextStepId\":\"\",\"skipNextStepId\":\"end\",\"cancelNextStepId\":\"end\"},{\"id\":\"action\",\"kind\":\"action\",\"capabilityId\":\"movement.follow_player\",\"arguments\":[{\"name\":\"target\",\"value\":\"player\"}],\"successNextStepId\":\"wait\",\"failureNextStepId\":\"end\"},{\"id\":\"wait\",\"kind\":\"wait\",\"activityId\":\"movement.follow_player\",\"conditionId\":\"m3.activity_terminal\",\"objective\":\"Wait\",\"successNextStepId\":\"end\",\"failureNextStepId\":\"end\"},{\"id\":\"end\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        ContentFixture fixture = WriteSource(root, "scenario/activity.sample.json", "scenario_definition", json);
        object result = Import(fixture, "bake.scenario");
        Require(Status(result) == "ready", "Scenario import status=" + Status(result) + ": " + Detail(result));
        object package = Value(result);
        object flow = First(GetField(package, "flows"));
        Require(GetField(flow, "id") as string == "activity.sample", "Scenario ID changed");
        Require(((IList)GetField(flow, "steps")).Count == 5, "Scenario step count changed");
        Require(((IList)GetField(package, "actions")).Count == 1, "registered action definition was not projected");
        object action = First(GetField(package, "actions"));
        Require(GetField(action, "capabilityId") as string == "movement.follow_player", "Action capability identity changed");
        string contract = GetField(action, "contractJson") as string;
        Require(contract.Contains("target") && contract.Contains("player"), "bounded Action arguments were dropped");
        object legacy = First(GetField(package, "legacySources"));
        Require((GetField(legacy, "metadataJson") as string).Contains("start"), "Scenario source identity metadata was dropped");
    }

    private static void SourceTrustAndUnknownFields(string root)
    {
        string unknownJson = "{\"schemaVersion\":1,\"id\":\"generic.unknown\",\"source\":\"generic\",\"unexpectedPermission\":true,\"routing\":{\"routeId\":\"r\",\"priority\":1,\"startHour\":-1,\"endHour\":-1,\"examples\":[\"x\"],\"requiredContext\":[],\"blockedContext\":[]},\"nodes\":[{\"id\":\"n\",\"speaker\":\"alterra\",\"text\":\"hello\",\"displayText\":\"hello\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"\",\"animation\":\"\"},\"choices\":[]}]}";
        ContentFixture unknown = WriteSource(root, "unknown.json", "generic_dialogue", unknownJson);
        object rejected = Import(unknown, "bake.unknown");
        Require(Status(rejected) == "invalid", "unknown source field was silently accepted");
        Require(DiagnosticCodes(rejected).Contains("unsupported_legacy_field"), "unknown field diagnostic code is missing: " + DiagnosticCodes(rejected));

        object mismatch = BuildManifest(unknown, "bake.hash-mismatch");
        SetField(First(GetField(mismatch, "sources")), "sha256", Hash("different"));
        object hashResult = ContentType("LegacyContentAdapter").GetMethod("Import").Invoke(null, new[] { mismatch });
        Require(Status(hashResult) == "invalid" && DiagnosticCodes(hashResult).Contains("source_hash_mismatch"), "source hash mismatch did not fail closed");

        ContentFixture traversal = new ContentFixture { root = unknown.root, path = "../outside.json", kind = "generic_dialogue", hash = unknown.hash };
        object traversalResult = Import(traversal, "bake.traversal");
        Require(Status(traversalResult) == "invalid", "manifest path traversal was accepted");
    }

    private static void LanguageResolution(string root)
    {
        string path = Path.Combine(root, "languages", "content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        object package = BuildPackage(storeId, "languages");
        object text = New("TextRecord");
        string revision = Hash("text.languages");
        SetField(text, "id", "text.languages"); SetField(text, "revision", revision); SetField(text, "semanticHash", revision);
        Add(GetField(text, "translations"), Translation("ja", "日本語"));
        Add(GetField(text, "translations"), Translation("en", "English"));
        Add(GetField(text, "translations"), Translation("id", "Bahasa"));
        Add(GetField(package, "texts"), text);
        Require(Status(Invoke(repository, "Stage", package)) == "ready", "language fixture package did not stage");
        Type resolver = ContentType("ContentLanguageResolver");
        object refValue = ContentRef(storeId, "text.languages", revision);
        MethodInfo resolveRepository = resolver.GetMethod("Resolve", new[]
        { ContentType("ContentRepository"), ContentType("ContentRef"), typeof(string), typeof(string) });
        object enJa = resolveRepository.Invoke(null, new[] { repository, refValue, "en", "ja" });
        Require(Status(enJa) == "ready", "EN/JA resolution unavailable");
        Require(GetField(Value(enJa), "displayText") as string == "English", "English display translation changed");
        Require(GetField(Value(enJa), "speechText") as string == "日本語", "Japanese speech translation changed");
        object idEn = resolveRepository.Invoke(null, new[] { repository, refValue, "id", "en" });
        Require(GetField(Value(idEn), "displayText") as string == "Bahasa" && GetField(Value(idEn), "speechText") as string == "English", "ID/EN translations changed");
        object fallback = resolveRepository.Invoke(null, new[] { repository, refValue, "fr", "id" });
        Require(GetField(Value(fallback), "displayText") as string == "English", "missing display language did not fall back to English");
        object alias = resolveRepository.Invoke(null, new[] { repository, refValue, "english", "japanese" });
        Require(GetField(Value(alias), "displayText") as string == "English" && GetField(Value(alias), "speechText") as string == "日本語", "language aliases changed");
        object silent = resolveRepository.Invoke(null, new[] { repository, refValue, "en", "fr" });
        Require(Status(silent) == "ready" && GetField(Value(silent), "speechText") as string == string.Empty, "missing speech language did not resolve silently");

        object onlyJapanese = New("TextRecord");
        string onlyJapaneseRevision = Hash("text.only-ja");
        SetField(onlyJapanese, "id", "text.only-ja"); SetField(onlyJapanese, "revision", onlyJapaneseRevision); SetField(onlyJapanese, "semanticHash", onlyJapaneseRevision);
        Add(GetField(onlyJapanese, "translations"), Translation("ja", "日本語だけ"));
        Add(GetField(package, "texts"), onlyJapanese);
        Require(Status(Invoke(repository, "Stage", package)) == "ready", "Japanese-only Text did not stage");
        object noEnglish = resolveRepository.Invoke(null, new[] { repository, ContentRef(storeId, "text.only-ja", onlyJapaneseRevision), "fr", "ja" });
        Require(Status(noEnglish) == "unavailable", "missing display and English fallback did not fail closed");
    }

    private static void BakeSqlArtifact(string root)
    {
        const string json = "{\"schemaVersion\":1,\"id\":\"generic.artifact\",\"source\":\"generic\",\"routing\":{\"routeId\":\"artifact_route\",\"priority\":1,\"startHour\":-1,\"endHour\":-1,\"examples\":[\"hello\"],\"requiredContext\":[],\"blockedContext\":[]},\"nodes\":[{\"id\":\"hello\",\"speaker\":\"alterra\",\"text\":\"Hello\",\"displayText\":\"Hello\",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"gentle\",\"animation\":\"\"},\"choices\":[]}]}";
        ContentFixture fixture = WriteSource(root, "artifact/generic.json", "generic_dialogue", json);
        object manifest = BuildManifest(fixture, "bake.artifact");
        string output = Path.Combine(root, "staging", "content.sqlite3");
        string streaming = Path.Combine(root, "StreamingAssets");
        object result = BakeServiceMethod().Invoke(null, new[] { manifest, output, streaming });
        Require(Status(result) == "ready", "ContentBakeService status=" + Status(result) + ": " + Detail(result));
        Require(File.Exists(output), "bake service did not produce the SQL artifact");
        object artifact = Value(result);
        Require((string)GetField(artifact, "releaseId") != string.Empty && (int)GetField(artifact, "sources") == 1 &&
            (int)GetField(artifact, "flows") == 1 && (int)GetField(artifact, "texts") == 1, "bake summary omitted the release or source counts");
        byte[] before = File.ReadAllBytes(output);
        object overwrite = BakeServiceMethod().Invoke(null, new[] { manifest, output, streaming });
        Require(Status(overwrite) == "invalid" && BytesEqual(before, File.ReadAllBytes(output)), "bake overwrote an existing artifact");
        string forbiddenOutput = Path.Combine(streaming, "content.sqlite3");
        object streamingWrite = BakeServiceMethod().Invoke(null, new[] { manifest, forbiddenOutput, streaming });
        Require(Status(streamingWrite) == "invalid" && !File.Exists(forbiddenOutput), "bake wrote to StreamingAssets");

        object repository = OpenRepository(output, "ReadOnly");
        string flowId;
        string flowRevision;
        using (var connection = new SQLiteConnection(output, SQLiteOpenFlags.ReadOnly))
        {
            flowId = connection.ExecuteScalar<string>("SELECT content_id FROM legacy_sources WHERE source_id=?", "generic.artifact");
            flowRevision = connection.ExecuteScalar<string>("SELECT content_revision FROM legacy_sources WHERE source_id=?", "generic.artifact");
        }
        object flowResult = Invoke(repository, "ReadFlow", ContentRef(GetProperty(repository, "StoreId") as string, flowId, flowRevision));
        Require(Status(flowResult) == "ready", "compiled SQL Flow did not reopen: " + Detail(flowResult));
        object releaseResult = Invoke(repository, "ReadRelease", GetField(artifact, "releaseId") as string);
        Require(Status(releaseResult) == "ready", "compiled SQL release did not reopen: " + Detail(releaseResult));
        var dependencies = GetField(Value(releaseResult), "dependencies") as IEnumerable;
        Require(dependencies != null && dependencies.Cast<object>().Any(item => GetField(item, "kind") as string == "flow") &&
            dependencies.Cast<object>().Any(item => GetField(item, "kind") as string == "anchor_link"),
            "normalized SQL release dependency closure omitted Flow or Anchor links");
        using (var connection = new SQLiteConnection(output, SQLiteOpenFlags.ReadOnly))
        {
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM legacy_sources WHERE source_id=?", "generic.artifact") == 1,
                "source identity/hash mapping was not staged");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM releases WHERE release_id=?", GetField(artifact, "releaseId")) == 1,
                "compiled immutable release was not staged");
        }
        Require(!Directory.Exists(streaming) || !Directory.GetFiles(streaming, "*", SearchOption.AllDirectories).Any(),
            "bake wrote into StreamingAssets instead of its staging path");
    }

    private static void RuntimeContentSourceRoundTrip(string root)
    {
        const string json = "{\"metadata\":{\"mode\":\"cinematic\",\"topic\":\"greeting\",\"triggerConditions\":[]},\"nodes\":[{\"id\":\"hello\",\"speaker\":\"alterra\",\"text\":\"Hello\",\"translatedText\":\"Hello\",\"language\":\"english\",\"audioPath\":\"\",\"emotion\":\"gentle\",\"choices\":[]}]}";
        ContentFixture fixture = WriteSource(root, "greet/legacy_greeting.json", "legacy_sequence", json);
        object manifest = BuildManifest(fixture, "bake.legacy-sequence");
        const string scenarioJson = "{\"scenarioId\":\"scenario.companion\",\"topicId\":\"\",\"entryStepId\":\"end\",\"steps\":[{\"id\":\"end\",\"kind\":\"end\",\"outcome\":\"completed\"}]}";
        ContentFixture generic = WriteSource(root, "scenario/companion.json", "scenario_definition", scenarioJson);
        object genericSource = New("ContentSourceFile");
        SetField(genericSource, "kind", generic.kind); SetField(genericSource, "relativePath", generic.path); SetField(genericSource, "sha256", generic.hash);
        Add(GetField(manifest, "sources"), genericSource);
        object imported = ImportManifest(manifest);
        Require(Status(imported) == "ready", "legacy sequence plus canonical import status=" + Status(imported) + ": " + Detail(imported));

        string output = Path.Combine(root, "staging", "legacy-sequence.sqlite3");
        string streaming = Path.Combine(root, "StreamingAssets");
        object baked = BakeServiceMethod().Invoke(null, new[] { manifest, output, streaming });
        Require(Status(baked) == "ready", "legacy sequence bake status=" + Status(baked) + ": " + Detail(baked));

        Type configurationType = ContentType("ContentRuntimeConfiguration");
        object configuration = Activator.CreateInstance(configurationType);
        SetField(configuration, "databasePath", output);
        SetField(configuration, "expectedReleaseId", GetField(Value(baked), "releaseId"));
        object opened = ContentType("RuntimeContentStore").GetMethod("OpenTrustedRelease").Invoke(null, new[] { configuration });
        Require(Status(opened) == "ready", "runtime SQL open status=" + Status(opened) + ": " + Detail(opened));

        object document = Invoke(Value(opened), "ReadDocument", "Dialogue/greet/legacy_greeting.json");
        Require(Status(document) == "ready" && (string)GetField(Value(document), "json") == json,
            "the runtime SQL source did not preserve and resolve the exact legacy JSON bytes: " + Detail(document));
    }

    private static void SourceManifestCoversCurrentDialogueInventory()
    {
        Type builder = runtime.GetType("AlterOS.Content.ContentSourceManifestBuilder", false);
        Require(builder != null, "the full authored Dialogue source manifest builder is missing");
        string sourceRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Dialogue");
        object built = builder.GetMethod("Create").Invoke(null,
            new object[] { sourceRoot, "alteros.dialogue", Guid.NewGuid().ToString("D") });
        Require(Status(built) == "ready", "current Dialogue source inventory status=" + Status(built) + ": " + Detail(built));
        IList sources = GetField(Value(built), "sources") as IList;
        int expected = Directory.GetFiles(sourceRoot, "*.json", SearchOption.AllDirectories).Length;
        Require(sources != null && sources.Count == expected, "the source manifest omitted one or more current Dialogue JSON files");
        string[] requiredKinds = { "generic_dialogue", "topic_definition", "legacy_sequence", "ambient_pool",
            "activity_scenario_library", "activity_greeting_menu", "conversation_coverage", "topic_migration_map", "onboarding_steps" };
        foreach (string kind in requiredKinds)
            Require(sources.Cast<object>().Any(item => GetField(item, "kind") as string == kind),
                "the current source inventory did not classify required format " + kind);
        object imported = ImportManifest(Value(built));
        Require(Status(imported) == "ready", "the full authored Dialogue inventory failed import: " + Detail(imported));
    }

    private static void FullCorpusRuntimeRelease(string root)
    {
        string projectRoot = Directory.GetCurrentDirectory();
        string sourceRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "Dialogue");
        string storeId = Guid.NewGuid().ToString("D");
        ContentResult<ContentSourceManifest> builtManifest = ContentSourceManifestBuilder.Create(sourceRoot, "alteros.dialogue.full", storeId);
        Require(builtManifest.IsReady, "full source manifest status=" + builtManifest.status + ": " + Detail(builtManifest));
        ContentResult<ContentPackage> imported = LegacyContentAdapter.Import(builtManifest.value);
        Require(imported.IsReady, "full source import status=" + imported.status + ": " + Detail(imported));

        JsonObject metadata;
        byte[] metadataBytes, vectorBytes, tokenizerBytes;
        ReadLegacyGteAssets(out metadata, out metadataBytes, out vectorBytes, out tokenizerBytes);
        string[] routeIds = ((JsonArray)metadata["anchors"]).OfType<JsonObject>()
            .Select(item => (string)item["routeId"]).Distinct(StringComparer.Ordinal).ToArray();
        ContentPackage package = imported.value;
        string[] actions = package.actions.Select(item => item.capabilityId).Distinct(StringComparer.Ordinal).ToArray();
        string[] approvedActions = { "movement.come_here", "movement.follow_player", "needs.drink_water" };
        Require(actions.All(item => approvedActions.Contains(item, StringComparer.Ordinal)),
            "the authored corpus includes an Action outside the current host registry: " + string.Join(",", actions));
        string[] expressions = package.dialogues.SelectMany(item => item.beats).Select(item => item.expression)
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        var vocabulary = new ContentHostVocabulary(approvedActions, expressions, new string[0], new string[0], routeIds);
        var semanticInput = new ContentSemanticBuildInput
        {
            metadataUtf8 = metadataBytes, vectorBytes = vectorBytes, tokenizerBytes = tokenizerBytes,
            expectedContentHash = (string)metadata["contentHash"], hostVocabulary = vocabulary
        };
        string output = Path.Combine(root, "full-corpus", "content.sqlite3");
        string streaming = Path.Combine(root, "StreamingAssets");
        ContentResult<ContentBakeArtifact> baked = ContentBakeService.Bake(builtManifest.value, output, streaming, semanticInput);
        Require(baked.IsReady, "full corpus bake status=" + baked.status + ": " + Detail(baked));
        ContentBakeArtifact artifact = baked.value;
        ContentResult<ContentRepository> stagedRepository = ContentRepository.Open(output, ContentStoreMode.ReadOnly, storeId);
        Require(stagedRepository.IsReady, "the full SQL artifact did not reopen read-only");
        ContentResult<ContentBuildManifest> storedManifest = stagedRepository.value.ReadBuildManifest();
        Require(storedManifest.IsReady, "the stored build manifest did not reopen: " + Detail(storedManifest));
        ContentBuildManifest buildManifest = storedManifest.value;
        Require(artifact.sources == builtManifest.value.sources.Count && artifact.anchors == 673 &&
            buildManifest.coverage.Count > 0 && buildManifest.sourceManifestHash == package.sourceManifestHash,
            "the full bake manifest omitted source coverage or the complete GTE Anchor set: artifact_sources=" + artifact.sources +
            " input_sources=" + builtManifest.value.sources.Count + " anchors=" + artifact.anchors +
            " coverage=" + buildManifest.coverage.Count + " source_hash_equal=" + (buildManifest.sourceManifestHash == package.sourceManifestHash));

        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
        {
            databasePath = output, expectedStoreId = storeId, expectedReleaseId = artifact.releaseId,
            expectedBuildManifestHash = artifact.buildManifestHash
        });
        Require(opened.IsReady, "full SQL release open status=" + opened.status + ": " + Detail(opened));
        RuntimeContentStore store = opened.value;
        Require(store.BuildManifest.sources.Count == builtManifest.value.sources.Count && store.SemanticIndex.AnchorCount == 673 &&
            store.SemanticIndex.RouteCount == 57 && store.SemanticIndex.LinkCount == package.anchorLinks.Count + 678 &&
            store.SemanticIndex.Dimension == 768,
            "the opened SQL release changed authored source coverage or GTE dimensions/counts: sources=" +
            store.BuildManifest.sources.Count + " anchors=" + store.SemanticIndex.AnchorCount + " routes=" +
            store.SemanticIndex.RouteCount + " links=" + store.SemanticIndex.LinkCount + " dimension=" + store.SemanticIndex.Dimension);
        foreach (string path in new[] { "Generic/affection_confession/affection_confession_001.json", "topic/activity_scenarios.json",
            "topic/activity_options.json", "topic/topic_options.json", "Topics/migration-map.json", "ConversationCoverage.json",
            "filler/filler_pool.json", "Onboarding/first_meeting.json" })
            Require(store.ReadDocument("Dialogue/" + path).IsReady, "the SQL runtime alias is missing: " + path);
        RuntimeContentDocument idleDocument = store.ReadDocument("idle/idle_pool.json").value;
        JsonObject idlePool = JToken.Parse(idleDocument.json) as JsonObject;
        JsonArray idleLines = idlePool == null ? null : idlePool["lines"] as JsonArray;
        ContentBuildSource idleSource = store.BuildManifest.sources.Single(item => item.path == "idle/idle_pool.json");
        Require(idleLines != null && idleLines.OfType<JsonObject>().All(line => line["playCount"].ToString() == "0" &&
            line["lastPlayTime"].ToString() == "0") && idleSource.sourceHash != idleSource.payloadHash,
            "historical idle repetition counters leaked from the shared JSON source into the SQL release");
        ConversationTopicCatalogLoadResult topicCatalogue = ConversationTopicCatalog.Load(store);
        Require(topicCatalogue.Success && topicCatalogue.Catalogue.Count == 57,
            "the existing Topic catalogue did not validate from the SQL release: " + topicCatalogue.Error);
        string genericRoot = Path.Combine(sourceRoot, "Generic");
        AuthoredDialogueRouteCatalog fileRoutes = AuthoredDialogueRouteCatalog.FromDirectory(genericRoot);
        AuthoredDialogueRouteCatalog sqlRoutes = AuthoredDialogueRouteCatalog.FromDocuments(
            store.ReadDocuments(ContentSourceKinds.GenericDialogue, "Generic/"));
        Require(fileRoutes.RouteCount == sqlRoutes.RouteCount && fileRoutes.RouteCount == 57,
            "the SQL source changed the authored Generic route count");
        foreach (string route in Directory.GetDirectories(genericRoot).Select(Path.GetFileName))
        {
            string[] filePaths = fileRoutes.GetSequencePaths(route).ToArray();
            string[] sqlPaths = sqlRoutes.GetSequencePaths(route).ToArray();
            Require(filePaths.SequenceEqual(sqlPaths, StringComparer.Ordinal), "the SQL source changed stable variant order for route " + route);
            var fileCandidates = new List<AuthoredDialogueVariantCandidate>();
            var sqlCandidates = new List<AuthoredDialogueVariantCandidate>();
            foreach (string relativePath in sqlPaths)
            {
                RuntimeContentDocument document = store.ReadDocument(relativePath).value;
                string sourcePath = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                byte[] sourceBytes = File.ReadAllBytes(sourcePath);
                Require(document != null && BytesEqual(document.bytes, sourceBytes),
                    "a canonical Generic document changed between authored file and SQL projection: " + relativePath);
                fileCandidates.Add(new AuthoredDialogueVariantCandidate(relativePath, File.ReadAllText(sourcePath)));
                sqlCandidates.Add(new AuthoredDialogueVariantCandidate(relativePath, document.json));
            }
            for (int hour = 0; hour < 24; hour++)
            {
                AuthoredDialogueSelectionResult fileSelection = AuthoredDialogueVariantSelector.Select(route, fileCandidates, hour, new StatSnapshot());
                AuthoredDialogueSelectionResult sqlSelection = AuthoredDialogueVariantSelector.Select(route, sqlCandidates, hour, new StatSnapshot());
                Require(fileSelection.SelectedPath == sqlSelection.SelectedPath && fileSelection.Candidates.Count == sqlSelection.Candidates.Count,
                    "the SQL content source changed route selection at hour " + hour + " for " + route);
                for (int index = 0; index < fileSelection.Candidates.Count; index++)
                    Require(fileSelection.Candidates[index].Path == sqlSelection.Candidates[index].Path &&
                        fileSelection.Candidates[index].Status == sqlSelection.Candidates[index].Status,
                        "the SQL content source changed candidate eligibility at hour " + hour + " for " + route);
            }
        }
        Console.WriteLine("CORPUS_SQL sources=" + artifact.sources + " topics=" + artifact.topics + " flows=" + artifact.flows +
            " anchors=" + artifact.anchors + " routes=" + store.SemanticIndex.RouteCount + " router_parity=57x24 source_hash=" + package.sourceManifestHash +
            " release=" + artifact.releaseId);
    }

    private static void BakeApiSignature()
    {
        Type manifest = ContentType("ContentSourceManifest");
        Type adapter = ContentType("LegacyContentAdapter");
        Type compiler = ContentType("ContentReleaseCompiler");
        Require(manifest.GetField("sourceRoot") != null && manifest.GetField("sources") != null, "trusted manifest fields are missing");
        Require(adapter.GetMethod("Import", BindingFlags.Public | BindingFlags.Static) != null, "legacy Import API is missing");
        Require(compiler.GetMethod("Compile", new[] { ContentType("ContentPackage") }) != null, "content compiler API is missing");
        Require(ContentType("ContentLanguageResolver").GetMethod("Resolve", new[]
        { ContentType("ContentRepository"), ContentType("ContentRef"), typeof(string), typeof(string) }) != null,
            "language resolver API is missing");
        Require(ContentType("ContentRepository").GetMethod("ReadText", new[] { ContentType("ContentRef") }) != null, "Text read API is missing");
        Type semanticInput = runtime.GetType("AlterOS.Content.ContentSemanticBuildInput", false);
        Require(semanticInput != null && ContentType("ContentBakeService").GetMethod("Bake", new[]
        { manifest, typeof(string), typeof(string), semanticInput }) != null,
            "the bake service cannot import the exact baked GTE inputs into the SQL release");
    }

    private static int BakeCommand(string root, string manifestPath, string outputPath)
    {
        ContentSourceManifest manifest;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            string sourceRoot = Path.Combine(FindProjectRoot(), "Assets", "StreamingAssets", "Dialogue");
            ContentResult<ContentSourceManifest> generated = ContentSourceManifestBuilder.Create(sourceRoot,
                "alteros.dialogue.full", Guid.NewGuid().ToString("D"));
            if (!generated.IsReady) throw new InvalidDataException("Current full source inventory status=" + generated.status + ": " + Detail(generated));
            manifest = generated.value;
        }
        else
        {
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Trusted bake manifest is missing.", manifestPath);
            Type manifestType = ContentType("ContentSourceManifest");
            using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                manifest = (ContentSourceManifest)new DataContractJsonSerializer(manifestType).ReadObject(stream);
        }
        ContentResult<ContentPackage> imported = LegacyContentAdapter.Import(manifest);
        if (!imported.IsReady) throw new InvalidDataException("Full source import status=" + imported.status + ": " + Detail(imported));
        JsonObject metadata;
        byte[] metadataBytes, vectorBytes, tokenizerBytes;
        ReadLegacyGteAssets(out metadata, out metadataBytes, out vectorBytes, out tokenizerBytes);
        string[] routeIds = ((JsonArray)metadata["anchors"]).OfType<JsonObject>()
            .Select(item => (string)item["routeId"]).Distinct(StringComparer.Ordinal).ToArray();
        ContentPackage package = imported.value;
        string[] actions = package.actions.Select(item => item.capabilityId).Distinct(StringComparer.Ordinal).ToArray();
        string[] approvedActions = { "movement.come_here", "movement.follow_player", "needs.drink_water" };
        if (!actions.All(item => approvedActions.Contains(item, StringComparer.Ordinal)))
            throw new InvalidDataException("The authored corpus contains an Action outside the current host registry.");
        string[] expressions = package.dialogues.SelectMany(item => item.beats).Select(item => item.expression)
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        var semanticInput = new ContentSemanticBuildInput
        {
            metadataUtf8 = metadataBytes, vectorBytes = vectorBytes, tokenizerBytes = tokenizerBytes,
            expectedContentHash = (string)metadata["contentHash"],
            hostVocabulary = new ContentHostVocabulary(approvedActions, expressions, new string[0], new string[0], routeIds)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
        ContentResult<ContentBakeArtifact> result = ContentBakeService.Bake(manifest, outputPath,
            Path.Combine(root, "StreamingAssets"), semanticInput);
        if (!result.IsReady)
        {
            Console.Error.WriteLine("BAKE_FAILED status=" + result.status + " detail=" + Detail(result));
            return 1;
        }
        ContentBakeArtifact artifact = result.value;
        Console.WriteLine("BAKE_ARTIFACT release=" + artifact.releaseId + " sources=" + artifact.sources +
            " topics=" + artifact.topics + " flows=" + artifact.flows + " dialogues=" + artifact.dialogues +
            " texts=" + artifact.texts + " anchors=" + artifact.anchors + " output=" + artifact.outputPath);
        return 0;
    }

    private static object HostVocabulary(string[] actions, string[] expressions, string[] enumValues,
        string[] contextFacts, string[] canonicalOwners)
    {
        return Activator.CreateInstance(ContentType("ContentHostVocabulary"), new object[]
        { actions, expressions, enumValues, contextFacts, canonicalOwners });
    }

    private static object ValidatePackage(object package, object vocabulary)
    {
        return ContentType("ContentPackageValidator").GetMethod("Validate").Invoke(null, new[] { package, vocabulary });
    }

    private static object LegacyCompile(object package)
    {
        MethodInfo method = ContentType("ContentReleaseCompiler").GetMethod("Compile", new[] { ContentType("ContentPackage") });
        return method.Invoke(null, new[] { package });
    }

    private static object ImportManifest(object manifest)
    {
        return ContentType("LegacyContentAdapter").GetMethod("Import").Invoke(null, new[] { manifest });
    }

    private static object OpenRepositoryWithStoreId(string path, string mode, string storeId)
    {
        Type repository = ContentType("ContentRepository");
        Type modeType = ContentType("ContentStoreMode");
        object result = repository.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), modeType, typeof(string) }, null).Invoke(null,
                new[] { (object)path, Enum.Parse(modeType, mode), storeId });
        Require(Status(result) == "ready", "Open(" + mode + ", storeId) returned " + Status(result));
        return Value(result);
    }

    private static object InvokePublish(object repository, object release, string headName, long expectedRevision, object semanticSnapshot)
    {
        MethodInfo method = repository.GetType().GetMethod("Publish", new[]
        { ContentType("ContentRelease"), typeof(string), typeof(long), ContentType("ContentSemanticSnapshot") });
        return method.Invoke(repository, new[] { release, (object)headName, expectedRevision, semanticSnapshot });
    }

    private static object LoadSemanticSnapshot(object repository, object release, string profileId)
    {
        MethodInfo method = ContentType("ContentSemanticIndex").GetMethod("Load", new[]
        { ContentType("ContentRepository"), ContentType("ContentRelease"), typeof(string) });
        return method.Invoke(null, new[] { repository, release, profileId });
    }

    private static string GenericRevisionFixture(string text)
    {
        return "{\"schemaVersion\":1,\"id\":\"generic.revision\",\"source\":\"generic\",\"routing\":{\"routeId\":\"revision_route\",\"priority\":1,\"startHour\":-1,\"endHour\":-1,\"examples\":[\"revision\"],\"requiredContext\":[],\"blockedContext\":[]},\"nodes\":[{\"id\":\"line\",\"speaker\":\"alterra\",\"text\":" + QuoteJson(text) +
            ",\"displayText\":" + QuoteJson(text) + ",\"language\":\"english\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"gentle\",\"animation\":\"\"},\"choices\":[]}]}";
    }

    private static string RuleReferenceJson(string storeId, string ruleId, string revision)
    {
        return "{\"kind\":\"ref\",\"store\":" + QuoteJson(storeId) + ",\"id\":" + QuoteJson(ruleId) +
            ",\"revision\":" + QuoteJson(revision) + ",\"expectedHash\":" + QuoteJson(revision) + "}";
    }

    private static string QuoteJson(string value)
    {
        return Newtonsoft.Json.JsonConvert.ToString(value ?? string.Empty);
    }

    private static object SimpleFlow(string id, string entryStepId, string kind)
    {
        object flow = New("FlowRecord");
        string revision = Hash("flow." + id);
        SetField(flow, "id", id); SetField(flow, "revision", revision); SetField(flow, "semanticHash", revision);
        SetField(flow, "kind", kind); SetField(flow, "entryStepId", entryStepId);
        object step = New("StepRecord"); SetField(step, "id", entryStepId); SetField(step, "kind", "end"); SetField(step, "dataJson", "{\"outcome\":\"completed\"}");
        Add(GetField(flow, "steps"), step);
        return flow;
    }

    private static Type ProjectType(string fullName)
    {
        Type type = runtime.GetType(fullName, false);
        if (type == null) throw new InvalidOperationException("Project runtime type missing: " + fullName);
        return type;
    }

    private static int ProbeRoutes(string artifactPath, string queryPath)
    {
        ContentResult<ContentRepository> repositoryResult = ContentRepository.Open(Path.GetFullPath(artifactPath), ContentStoreMode.ReadOnly);
        Require(repositoryResult.IsReady, "Cannot open selected artifact read-only: " + Detail(repositoryResult));
        ContentRepository repository = repositoryResult.value;
        {
            Require(repository != null, "Cannot open the selected artifact read-only.");
            ContentResult<ContentBuildManifest> manifest = repository.ReadBuildManifest();
            Require(manifest.IsReady, "Cannot read the selected artifact manifest.");
            ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
            {
                databasePath = Path.GetFullPath(artifactPath), expectedStoreId = repository.StoreId,
                expectedReleaseId = manifest.value.releaseId, expectedBuildManifestHash = manifest.value.Hash
            });
            Require(opened.IsReady, "Cannot open exact runtime release: " + Detail(opened));
            RuntimeContentStore store = opened.value;
            {
                ConversationTopicCatalogLoadResult catalogue = ConversationTopicCatalog.Load(store);
                Require(catalogue.Success, "Production conversation catalogue failed: " + catalogue.Error);
                var results = new JArray();
                bool success = true;
                foreach (JObject query in JObject.Parse(File.ReadAllText(queryPath))["queries"])
                {
                    string text = (string)query["text"];
                    string expected = (string)query["expectedTopicId"];
                    float[] vector = query["vector"].ToObject<float[]>();
                    var matches = store.SemanticIndex.RankRoutes(vector);
                    TopicCandidateSet candidates = TopicCandidateRetriever.FromRouteMatches(matches,
                        store.SemanticIndex.IndexFingerprint, catalogue.Catalogue);
                    var context = new AlterOS.Topics.TopicContext();
                    var history = new AlterOS.Topics.TopicHistory();
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    candidates = TopicCandidateRetriever.PrepareForJudgment(candidates, catalogue.Catalogue,
                        catalogue.AuthoredInstances, context, history, now, "direct");
                    var request = new ConversationRequest { requestId = Guid.NewGuid().ToString("N"),
                        sessionId = "synthetic-route-probe", channel = "direct", text = text };
                    ConversationPlan plan = UnifiedConversationPlanner.Plan(request, candidates, null,
                        catalogue.Catalogue, catalogue.AuthoredInstances, context, history, now, 0.5, new HashSet<string>());
                    var selected = plan.outcome == "authored" ? catalogue.Catalogue[plan.topicId].scenarios
                        .FirstOrDefault(item => item.scenarioId == plan.scenarioId) : null;
                    bool flowReady = selected != null && selected.scenarioRef != null && store.ReadFlow(selected.scenarioRef).IsReady;
                    bool pass = string.IsNullOrEmpty(expected) || (plan.topicId == expected && plan.outcome == "authored" && flowReady);
                    success &= pass;
                    results.Add(new JObject { ["text"] = text, ["expectedTopicId"] = expected,
                        ["outcome"] = plan.outcome, ["topicId"] = plan.topicId, ["scenarioId"] = plan.scenarioId,
                        ["scenarioFlowReady"] = flowReady, ["pass"] = pass,
                        ["candidates"] = JArray.FromObject(candidates.cards.Select(card => new {
                            card.topicId, card.similarity, card.authoredAvailability })) });
                }
                Console.WriteLine(new JObject { ["releaseId"] = store.SemanticIndex.ReleaseId,
                    ["indexFingerprint"] = store.SemanticIndex.IndexFingerprint, ["routeCount"] = store.SemanticIndex.RouteCount,
                    ["catalogueCount"] = catalogue.Catalogue.Count, ["pass"] = success, ["queries"] = results }.ToString(Newtonsoft.Json.Formatting.Indented));
                return success ? 0 : 1;
            }
        }
    }

    private static int InspectArtifact(string artifactPath)
    {
        string fullPath = Path.GetFullPath(artifactPath);
        ContentResult<ContentRepository> repositoryResult = ContentRepository.Open(fullPath, ContentStoreMode.ReadOnly);
        Require(repositoryResult.IsReady, "content release could not be opened read-only: " + Detail(repositoryResult));
        ContentRepository repository = repositoryResult.value;
        string storeId = repository.StoreId;
        string releaseId;
        int dependencyRows;
        int topicRows;
        int flowRows;
        int profileRows;
        int embeddingRows;
        using (var connection = new SQLiteConnection(fullPath, SQLiteOpenFlags.ReadOnly))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            Require(connection.ExecuteScalar<string>("PRAGMA integrity_check") == "ok", "SQLite integrity_check failed");
            Require(connection.Query<ForeignKeyViolationRow>("PRAGMA foreign_key_check").Count == 0, "release artifact has foreign-key violations");
            releaseId = connection.ExecuteScalar<string>("SELECT release_id FROM releases ORDER BY release_id LIMIT 1");
            dependencyRows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM release_dependencies WHERE release_id=?", releaseId);
            topicRows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topics");
            flowRows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM flows");
            profileRows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM embedding_profiles");
            embeddingRows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM embeddings");
        }
        Require(!string.IsNullOrWhiteSpace(releaseId), "staged artifact has no immutable release row");
        ContentResult<ContentBuildManifest> manifestResult = repository.ReadBuildManifest();
        Require(manifestResult.IsReady, "content build manifest is unavailable: " + Detail(manifestResult));
        ContentBuildManifest manifest = manifestResult.value;
        Require(manifest.embeddingProfileCount == profileRows && manifest.embeddingCount == embeddingRows &&
            manifest.embeddingProfileCount == 1 && manifest.embeddingCount > 0 && manifest.anchorCount == 673 &&
            !string.IsNullOrWhiteSpace(manifest.semanticProfileId),
            "staged release does not contain the complete authored GTE semantic snapshot: profiles=" + profileRows +
            " embeddings=" + embeddingRows + " manifest_embeddings=" + manifest.embeddingCount + " anchors=" + manifest.anchorCount);
        ContentResult<ContentRelease> releaseResult = repository.ReadRelease(releaseId);
        Require(releaseResult.IsReady, "release dependency closure did not validate: " + Detail(releaseResult));
        var dependencies = releaseResult.value.dependencies as IEnumerable;
        int loadedDependencies = dependencies == null ? 0 : dependencies.Cast<object>().Count();
        Require(loadedDependencies == dependencyRows && loadedDependencies > 0, "release dependency rows were not fully read back");
        object topicDependency = dependencies.Cast<object>().FirstOrDefault(item => GetField(item, "kind") as string == "topic");
        if (topicDependency != null)
        {
            object topic = Invoke(repository, "ReadTopic", ContentRef(storeId, GetField(topicDependency, "id") as string,
                GetField(topicDependency, "revision") as string, GetField(topicDependency, "expectedHash") as string));
            Require(Status(topic) == "ready", "an exact released Topic did not resolve from SQL: " + Detail(topic));
        }
        ContentResult<RuntimeContentStore> runtime = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
        {
            databasePath = fullPath, expectedStoreId = storeId, expectedReleaseId = releaseId,
            expectedBuildManifestHash = manifest.Hash
        });
        Require(runtime.IsReady && runtime.value.SemanticIndex.AnchorCount == 673 &&
            runtime.value.SemanticIndex.RouteCount == 57 && runtime.value.SemanticIndex.Dimension == 768 &&
            runtime.value.BuildManifest.embeddingCount == embeddingRows,
            "the full SQL release or its loaded semantic snapshot did not reopen: " + Detail(runtime));
        Console.WriteLine("ARTIFACT_INSPECT store=" + storeId + " release=" + releaseId + " sources=" + manifest.sources.Count +
            " source_manifest_hash=" + manifest.sourceManifestHash + " build_manifest_hash=" + manifest.Hash +
            " dependencies=" + loadedDependencies +
            " topics=" + topicRows + " flows=" + flowRows + " profiles=" + profileRows + " embeddings=" + embeddingRows +
            " anchors=" + runtime.value.SemanticIndex.AnchorCount + " routes=" + runtime.value.SemanticIndex.RouteCount +
            " dimension=" + runtime.value.SemanticIndex.Dimension + " index_fingerprint=" + runtime.value.SemanticIndex.IndexFingerprint +
            " integrity=ok foreign_keys=0");
        return 0;
    }

    private sealed class ContentFixture
    {
        public string root, path, kind, hash;
    }

    private sealed class ForeignKeyViolationRow
    {
        public string table { get; set; }
        public long rowid { get; set; }
        public string parent { get; set; }
        public int fkid { get; set; }
    }

    private static ContentFixture WriteSource(string parentRoot, string relativePath, string kind, string content)
    {
        string root = Path.Combine(parentRoot, "bake-sources");
        Directory.CreateDirectory(root);
        string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        return new ContentFixture { root = root, path = relativePath, kind = kind, hash = Hash(content) };
    }

    private static object BuildManifest(ContentFixture fixture, string packageId)
    {
        object manifest = New("ContentSourceManifest");
        SetField(manifest, "packageId", packageId); SetField(manifest, "storeId", Guid.NewGuid().ToString("D"));
        SetField(manifest, "sourceRoot", fixture.root);
        object source = New("ContentSourceFile");
        SetField(source, "kind", fixture.kind); SetField(source, "relativePath", fixture.path); SetField(source, "sha256", fixture.hash);
        Add(GetField(manifest, "sources"), source);
        return manifest;
    }

    private static MethodInfo BakeServiceMethod()
    {
        return ContentType("ContentBakeService").GetMethod("Bake", new[]
        { ContentType("ContentSourceManifest"), typeof(string), typeof(string) });
    }

    private static object Import(ContentFixture fixture, string packageId)
    {
        return ContentType("LegacyContentAdapter").GetMethod("Import").Invoke(null, new[] { BuildManifest(fixture, packageId) });
    }

    private static object FindText(object package, object reference)
    {
        string id = GetField(reference, "id") as string;
        string revision = GetField(reference, "revision") as string;
        foreach (object text in (IEnumerable)GetField(package, "texts"))
            if ((GetField(text, "id") as string) == id && (GetField(text, "revision") as string) == revision) return text;
        return null;
    }

    private static string DiagnosticCodes(object result)
    {
        var codes = new List<string>();
        var diagnostics = GetField(result, "diagnostics") as IEnumerable;
        if (diagnostics != null) foreach (object diagnostic in diagnostics) codes.Add(GetField(diagnostic, "code") as string ?? string.Empty);
        return string.Join(",", codes.ToArray());
    }

    private static void Utf8SchemaAndReadback(string root)
    {
        string path = Path.Combine(root, "utf8 日本語", "authored content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        object package = BuildPackage(storeId, "first");
        Require(Status(Invoke(repository, "Stage", package)) == "ready", "valid package did not stage");

        object topicRef = ContentRef(storeId, "topic.sample", Hash("topic-first-v1"));
        object topicResult = Invoke(repository, "ReadTopic", topicRef);
        Require(Status(topicResult) == "ready", "staged Topic read status=" + Status(topicResult) + ", detail=" + Detail(topicResult));
        object topic = Value(topicResult);
        Require(GetField(topic, "id") as string == "topic.sample", "Topic ID changed on read-back: " + (GetField(topic, "id") as string));
        Require(GetField(topic, "revision") as string == Hash("topic-first-v1"), "Topic revision changed on read-back: " + (GetField(topic, "revision") as string));

        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 6, "content schema version was not 6");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='topics'") == 1,
                "topics table is absent");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='release_heads'") == 1,
                "release_heads table is absent");
        }

        object reopened = OpenRepository(path, "ReadWrite");
        Require((string)GetProperty(reopened, "StoreId") == storeId, "store identity changed after reopen");
        object flowResult = Invoke(reopened, "ReadFlow", ContentRef(storeId, "scenario.sample", Hash("flow-first-v1")));
        Require(Status(flowResult) == "ready", "staged Flow read status=" + Status(flowResult) + ", detail=" + Detail(flowResult));
    }

    private static void CompositeForeignKey(string root)
    {
        string path = Path.Combine(root, "foreign-key", "content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        Require(Status(Invoke(repository, "Stage", BuildPackage(storeId, "foreign"))) == "ready", "fixture package did not stage");
        string topicRevision = Hash("topic-foreign-v1");
        using (var connection = new SQLiteConnection(path))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            bool rejected = Throws(() => connection.Execute(
                "INSERT INTO topic_entries(topic_id,topic_revision,entry_id,flow_id,flow_revision,priority,base_weight,condition_json,modifiers_json) VALUES (?,?,?,?,?,?,?,?,?)",
                "topic.sample", topicRevision, "bad-ref", "missing.flow", Hash("missing-revision"), 0, 1d, "{}", "[]"));
            Require(rejected, "a Topic entry with a missing composite Flow reference was inserted");
        }
    }

    private static void ImmutableRevisionConflict(string root)
    {
        string path = Path.Combine(root, "revision-conflict", "content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        Require(Status(Invoke(repository, "Stage", BuildPackage(storeId, "immutable"))) == "ready", "initial revision did not stage");
        object conflicting = BuildPackage(storeId, "immutable");
        object topic = First(GetField(conflicting, "topics"));
        SetField(topic, "routeId", "topic.changed-without-new-revision");
        object result = Invoke(repository, "Stage", conflicting);
        Require(Status(result) == "invalid", "changed payload reused an immutable revision");
        object saved = Value(Invoke(repository, "ReadTopic", ContentRef(storeId, "topic.sample", Hash("topic-immutable-v1"))));
        Require(GetField(saved, "routeId") as string == "topic.sample", "conflicting revision replaced the staged row");
    }

    private static void WrongKindStep(string root)
    {
        string path = Path.Combine(root, "wrong-kind", "content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        Require(Status(Invoke(repository, "Stage", BuildPackage(storeId, "wrong-kind"))) == "ready", "fixture package did not stage");
        using (var connection = new SQLiteConnection(path))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            bool rejected = Throws(() => connection.Execute(
                "INSERT INTO steps(flow_id,flow_revision,step_id,kind,dialogue_id,dialogue_revision,action_id,action_revision,called_flow_id,called_flow_revision,data_json) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                "scenario.sample", Hash("flow-wrong-kind-v1"), "wrong", "dialogue", null, null,
                "action.sample", Hash("action-wrong-kind-v1"), null, null, "{}"));
            Require(rejected, "dialogue Step accepted an Action reference");
        }
    }

    private static void DuplicateOrderAndLanguage(string root)
    {
        string path = Path.Combine(root, "duplicate-order", "content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        Require(Status(Invoke(repository, "Stage", BuildPackage(storeId, "duplicate"))) == "ready", "fixture package did not stage");
        using (var connection = new SQLiteConnection(path))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            bool duplicateOrder = Throws(() => connection.Execute(
                "INSERT INTO beats(dialogue_id,dialogue_revision,beat_id,sequence_order,text_id,text_revision,expression,presentation_json) VALUES (?,?,?,?,?,?,?,?)",
                "dialogue.sample", Hash("dialogue-duplicate-v1"), "beat.duplicate", 0, "text.title", Hash("text-title-v1"), "", "{}"));
            Require(duplicateOrder, "duplicate Dialogue beat order was accepted");
            bool duplicateLanguage = Throws(() => connection.Execute(
                "INSERT INTO translations(text_id,text_revision,language,text_value) VALUES (?,?,?,?)",
                "text.title", Hash("text-title-v1"), "en", "second English owner"));
            Require(duplicateLanguage, "duplicate Text/language owner was accepted");
        }
    }

    private static void ReadOnlyStore(string root)
    {
        string path = Path.Combine(root, "read-only", "content.sqlite3");
        object writable = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(writable, "StoreId");
        Require(Status(Invoke(writable, "Stage", BuildPackage(storeId, "readonly"))) == "ready", "fixture package did not stage");
        byte[] before = File.ReadAllBytes(path);
        object readOnly = OpenRepository(path, "ReadOnly");
        Require(Status(Invoke(readOnly, "Stage", BuildPackage(storeId, "readonly"))) == "invalid",
            "read-only repository accepted Stage");
        Require(BytesEqual(before, File.ReadAllBytes(path)), "read-only Stage changed the database bytes");
    }

    private static void InvalidStorePreservation(string root)
    {
        string futurePath = Path.Combine(root, "invalid", "future.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(futurePath));
        using (var connection = new SQLiteConnection(futurePath)) connection.Execute("PRAGMA user_version=999");
        byte[] futureBytes = File.ReadAllBytes(futurePath);
        object future = OpenResult(futurePath, "ReadWrite");
        Require(Status(future) == "incompatible", "future content schema was accepted");
        Require(BytesEqual(futureBytes, File.ReadAllBytes(futurePath)), "future content database bytes changed");

        string corruptPath = Path.Combine(root, "invalid", "corrupt.sqlite3");
        byte[] corruptBytes = { 0, 1, 2, 3, 4, 255, 0 };
        File.WriteAllBytes(corruptPath, corruptBytes);
        object corrupt = OpenResult(corruptPath, "ReadWrite");
        Require(Status(corrupt) == "unavailable" || Status(corrupt) == "invalid", "corrupt content schema was accepted");
        Require(BytesEqual(corruptBytes, File.ReadAllBytes(corruptPath)), "corrupt content database bytes changed");
    }

    private static void CrossStoreReference(string root)
    {
        string path = Path.Combine(root, "reference", "content.sqlite3");
        object repository = OpenRepository(path, "ReadWrite");
        string storeId = (string)GetProperty(repository, "StoreId");
        Require(Status(Invoke(repository, "Stage", BuildPackage(storeId, "reference"))) == "ready", "fixture package did not stage");
        byte[] before = File.ReadAllBytes(path);
        object badRef = ContentRef("another-store", "../outside", Hash("untrusted-reference"));
        object result = Invoke(repository, "ReadTopic", badRef);
        Require(Status(result) == "invalid" || Status(result) == "missing", "cross-store/path-like ContentRef was resolved");
        Require(BytesEqual(before, File.ReadAllBytes(path)), "invalid reference changed the content database");
    }

    private static void RepositoryApiSignature()
    {
        Type repository = ContentType("ContentRepository");
        Type reference = ContentType("ContentRef");
        Type topic = ContentType("TopicRecord");
        Type flow = ContentType("FlowRecord");
        Type package = ContentType("ContentPackage");
        Type mode = ContentType("ContentStoreMode");
        Require(repository.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), mode }, null) != null, "Open(path, mode) API is missing");
        Require(repository.GetMethod("ReadRelease", new[] { typeof(string) }) != null, "ReadRelease API is missing");
        Require(repository.GetMethod("ReadTopic", new[] { reference }) != null, "ReadTopic(ContentRef) API is missing");
        Require(repository.GetMethod("ReadFlow", new[] { reference }) != null, "ReadFlow(ContentRef) API is missing");
        Require(repository.GetMethod("Stage", new[] { package }) != null, "Stage(ContentPackage) API is missing");
        Require(topic != null && flow != null, "TopicRecord or FlowRecord DTO is missing");
    }

    private sealed class FlowFixture
    {
        public ContentPackage package;
        public ContentRelease release;
        public ContentHostVocabulary vocabulary;
        public string storeId;
    }

    private static FlowFixture BuildFlowFixture()
    {
        string storeId = Guid.NewGuid().ToString("D");
        var package = new ContentPackage
        {
            packageId = "flow.fixture",
            storeId = storeId,
            sourceManifestHash = Hash("flow-fixture-manifest")
        };
        TextRecord sharedText = AddFlowText(package, "text.shared", "Shared prompt", "共有のプロンプト");
        TextRecord rootQuestionText = AddFlowText(package, "text.root-question", "Who should we invite?", "誰を招待しますか？");
        TextRecord nestedQuestionText = AddFlowText(package, "text.nested-question", "Accept or decline?", "受け入れますか、それとも断りますか？");
        TextRecord inviteLabel = AddFlowText(package, "text.invite", "Invite", "招待");
        TextRecord declineLabel = AddFlowText(package, "text.decline", "Decline", "断る");
        TextRecord acceptLabel = AddFlowText(package, "text.accept", "Accept", "受け入れる");
        TextRecord againLabel = AddFlowText(package, "text.again", "Ask again", "もう一度聞く");

        DialogueRecord sharedDialogue = AddFlowDialogue(package, storeId, "dialogue.shared", sharedText);
        DialogueRecord rootQuestion = AddFlowDialogue(package, storeId, "dialogue.root-question", rootQuestionText);
        DialogueRecord nestedQuestion = AddFlowDialogue(package, storeId, "dialogue.nested-question", nestedQuestionText);

        string actionRevision = Hash("action.fixture");
        package.actions.Add(new ActionRecord
        {
            id = "action.fixture", revision = actionRevision, semanticHash = actionRevision,
            capabilityId = "fixture.action",
            contractJson = "{\"inputs\":[{\"name\":\"message\",\"type\":\"text\",\"required\":true}],\"results\":[{\"name\":\"confirmation\",\"type\":\"text\",\"required\":true}]}"
        });

        AddSharedCaller(package, storeId, "scenario.shared.a", sharedDialogue, "completed");
        AddSharedCaller(package, storeId, "scenario.shared.b", sharedDialogue, "failed");

        string subflowRevision = Hash("flow.subflow.invitation");
        var subflow = new FlowRecord
        {
            id = "subflow.invitation", revision = subflowRevision, semanticHash = subflowRevision,
            kind = "subflow", entryStepId = "sub.ask"
        };
        subflow.parameters.Add(new FlowParameterRecord { name = "prompt", direction = "input", valueType = "text", required = true });
        subflow.parameters.Add(new FlowParameterRecord { name = "reply", direction = "output", valueType = "text", required = true });
        subflow.steps.Add(new StepRecord { id = "sub.ask", kind = "ask", dialogueRef = new ContentRef(storeId, nestedQuestion.id, nestedQuestion.revision, nestedQuestion.semanticHash), dataJson = "{\"questionId\":\"nested.question\",\"allowFreeText\":false}" });
        subflow.steps.Add(new StepRecord { id = "sub.accepted", kind = "end", dataJson = "{\"exitName\":\"accepted\",\"outcome\":\"accepted\"}" });
        subflow.steps.Add(new StepRecord { id = "sub.declined", kind = "end", dataJson = "{\"exitName\":\"declined\",\"outcome\":\"declined\"}" });
        subflow.steps.Add(new StepRecord { id = "sub.cancelled", kind = "end", dataJson = "{\"exitName\":\"cancelled\",\"outcome\":\"cancelled\"}" });
        subflow.transitions.Add(FlowTransition("sub.accept", "sub.ask", "sub.accepted", "option"));
        subflow.transitions.Add(FlowTransition("sub.decline", "sub.ask", "sub.declined", "option"));
        subflow.transitions.Add(FlowTransition("sub.again", "sub.ask", "sub.ask", "option"));
        subflow.transitions.Add(FlowTransition("sub.cancel.skip", "sub.ask", "sub.cancelled", "skip"));
        subflow.transitions.Add(FlowTransition("sub.cancel", "sub.ask", "sub.cancelled", "cancel"));
        subflow.choices.Add(FlowChoice(storeId, "sub.ask", "accept", "sub.accept", "Accept", acceptLabel, "accepted"));
        subflow.choices.Add(FlowChoice(storeId, "sub.ask", "decline", "sub.decline", "Decline", declineLabel, "declined"));
        subflow.choices.Add(FlowChoice(storeId, "sub.ask", "again", "sub.again", "Ask again", againLabel, "again"));
        subflow.exits.Add(new FlowExitRecord { name = "accepted", contractJson = "{\"outputs\":[{\"name\":\"reply\",\"type\":\"text\",\"sourceName\":\"prompt\",\"required\":true}]}" });
        subflow.exits.Add(new FlowExitRecord { name = "declined", contractJson = "{\"outputs\":[]}" });
        subflow.exits.Add(new FlowExitRecord { name = "cancelled", contractJson = "{\"outputs\":[]}" });
        package.flows.Add(subflow);
        AddFlowSource(package, subflow);

        string rootRevision = Hash("flow.scenario.root");
        var root = new FlowRecord { id = "scenario.root", revision = rootRevision, semanticHash = rootRevision,
            kind = "scenario", entryStepId = "root.ask" };
        root.parameters.Add(new FlowParameterRecord { name = "welcome", direction = "input", valueType = "text", required = true });
        root.variables.Add(new FlowVariableRecord { name = "reply", valueType = "text", mutable = true, defaultValueJson = "\"untouched\"" });
        root.steps.Add(new StepRecord { id = "root.ask", kind = "ask", dialogueRef = new ContentRef(storeId, rootQuestion.id, rootQuestion.revision, rootQuestion.semanticHash),
            dataJson = "{\"questionId\":\"root.question\",\"allowFreeText\":false}" });
        root.steps.Add(new StepRecord { id = "root.call", kind = "subflow",
            calledFlowRef = new ContentRef(storeId, subflow.id, subflow.revision, subflow.semanticHash), dataJson = "{}" });
        root.steps.Add(new StepRecord { id = "root.action.accepted", kind = "action", actionRef = new ContentRef(storeId, "action.fixture", actionRevision, actionRevision), dataJson = "{}" });
        root.steps.Add(new StepRecord { id = "root.wait.accepted", kind = "wait",
            dataJson = "{\"activityId\":\"fixture.action\",\"conditionId\":\"fixture.completed\",\"objective\":\"Confirm\"}" });
        root.steps.Add(new StepRecord { id = "root.action.confirm", kind = "action", actionRef = new ContentRef(storeId, "action.fixture", actionRevision, actionRevision), dataJson = "{}" });
        root.steps.Add(new StepRecord { id = "root.action.cancelled", kind = "action", actionRef = new ContentRef(storeId, "action.fixture", actionRevision, actionRevision), dataJson = "{}" });
        root.steps.Add(new StepRecord { id = "root.wait.cancelled", kind = "wait",
            dataJson = "{\"activityId\":\"fixture.action\",\"conditionId\":\"fixture.completed\",\"objective\":\"Confirm\"}" });
        root.steps.Add(new StepRecord { id = "root.declined", kind = "end", dataJson = "{\"outcome\":\"declined\"}" });
        root.steps.Add(new StepRecord { id = "root.accepted", kind = "end", dataJson = "{\"outcome\":\"accepted\"}" });
        root.steps.Add(new StepRecord { id = "root.cancelled", kind = "end", dataJson = "{\"outcome\":\"cancelled\"}" });
        root.steps.Add(new StepRecord { id = "root.failed", kind = "end", dataJson = "{\"outcome\":\"failed\"}" });
        root.transitions.Add(FlowTransition("root.invite", "root.ask", "root.call", "option"));
        root.transitions.Add(FlowTransition("root.decline", "root.ask", "root.declined", "option"));
        root.transitions.Add(FlowTransition("root.skip", "root.ask", "root.cancelled", "skip"));
        root.transitions.Add(FlowTransition("root.cancel", "root.ask", "root.cancelled", "cancel"));
        root.transitions.Add(FlowTransition("root.accepted.return", "root.call", "root.action.accepted", "subflow_exit"));
        root.transitions.Add(FlowTransition("root.declined.return", "root.call", "root.declined", "subflow_exit"));
        root.transitions.Add(FlowTransition("root.cancelled.return", "root.call", "root.action.cancelled", "subflow_exit"));
        root.transitions.Add(FlowTransition("root.action.accepted", "root.action.accepted", "root.wait.accepted", "success"));
        root.transitions.Add(FlowTransition("root.action.accepted.failure", "root.action.accepted", "root.failed", "failure"));
        root.transitions.Add(FlowTransition("root.wait.accepted", "root.wait.accepted", "root.accepted", "success"));
        root.transitions.Add(FlowTransition("root.wait.accepted.failure", "root.wait.accepted", "root.failed", "failure"));
        root.transitions.Add(FlowTransition("root.confirm.success", "root.action.confirm", "root.accepted", "success"));
        root.transitions.Add(FlowTransition("root.confirm.failure", "root.action.confirm", "root.failed", "failure"));
        root.transitions.Add(FlowTransition("root.action.cancelled", "root.action.cancelled", "root.wait.cancelled", "success"));
        root.transitions.Add(FlowTransition("root.action.cancelled.failure", "root.action.cancelled", "root.failed", "failure"));
        root.transitions.Add(FlowTransition("root.wait.cancelled", "root.wait.cancelled", "root.cancelled", "success"));
        root.transitions.Add(FlowTransition("root.wait.cancelled.failure", "root.wait.cancelled", "root.failed", "failure"));
        root.choices.Add(FlowChoice(storeId, "root.ask", "invite", "root.invite", "Invite", inviteLabel, "invite"));
        root.choices.Add(FlowChoice(storeId, "root.ask", "decline", "root.decline", "Decline", declineLabel, "decline"));
        root.bindings.Add(new StepBindingRecord { stepId = "root.call", destinationName = "prompt", sourceKind = "input", sourceName = "welcome" });
        root.bindings.Add(new StepBindingRecord { stepId = "root.action.accepted", destinationName = "message", sourceKind = "local", sourceName = "reply" });
        root.bindings.Add(new StepBindingRecord { stepId = "root.action.cancelled", destinationName = "message", sourceKind = "local", sourceName = "reply" });
        root.bindings.Add(new StepBindingRecord { stepId = "root.action.confirm", destinationName = "message", sourceKind = "result",
            resultStepId = "root.action.accepted", resultName = "confirmation" });
        root.transitions.Single(item => item.id == "root.wait.accepted").toStepId = "root.action.confirm";
        root.exitMappings.Add(ExitMapping(storeId, "root.call", subflow, "accepted", "root.accepted.return",
            "[{\"sourceName\":\"reply\",\"destinationName\":\"reply\"}]"));
        root.exitMappings.Add(ExitMapping(storeId, "root.call", subflow, "declined", "root.declined.return", "[]"));
        root.exitMappings.Add(ExitMapping(storeId, "root.call", subflow, "cancelled", "root.cancelled.return", "[]"));
        package.flows.Add(root);
        AddFlowSource(package, root);

        var vocabulary = new ContentHostVocabulary(new[] { "fixture.action" }, new string[0], new string[0],
            new[] { "is_home" }, new string[0]);
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package, vocabulary);
        Require(compiled.status == "ready", "Flow fixture failed validation/compile: " + Detail(compiled));
        return new FlowFixture { package = compiled.package, release = compiled.release, vocabulary = vocabulary, storeId = storeId };
    }

    private static TextRecord AddFlowText(ContentPackage package, string id, string display, string speech)
    {
        string revision = Hash("flow-text\0" + id + "\0" + display + "\0" + speech);
        var text = new TextRecord { id = id, revision = revision, semanticHash = revision };
        text.translations.Add(new TranslationRecord { language = "en", text = display });
        text.translations.Add(new TranslationRecord { language = "ja", text = speech });
        package.texts.Add(text);
        return text;
    }

    private static DialogueRecord AddFlowDialogue(ContentPackage package, string storeId, string id, TextRecord text)
    {
        string revision = Hash("flow-dialogue\0" + id + "\0" + text.revision);
        var dialogue = new DialogueRecord { id = id, revision = revision, semanticHash = revision };
        dialogue.beats.Add(new BeatRecord
        {
            id = id + ".beat", sequenceOrder = 0,
            textRef = new ContentRef(storeId, text.id, text.revision, text.semanticHash),
            expression = string.Empty,
            presentationJson = "{\"speaker\":\"alterra\",\"voice\":\"alterra\",\"audioKey\":\"\",\"animation\":\"\"}"
        });
        package.dialogues.Add(dialogue);
        return dialogue;
    }

    private static void AddSharedCaller(ContentPackage package, string storeId, string id,
        DialogueRecord dialogue, string outcome)
    {
        string revision = Hash("flow-" + id);
        string endId = id + ".end";
        var flow = new FlowRecord { id = id, revision = revision, semanticHash = revision,
            kind = "scenario", entryStepId = id + ".say" };
        flow.steps.Add(new StepRecord { id = id + ".say", kind = "dialogue",
            dialogueRef = new ContentRef(storeId, dialogue.id, dialogue.revision, dialogue.semanticHash), dataJson = "{}" });
        flow.steps.Add(new StepRecord { id = endId, kind = "end", dataJson = "{\"outcome\":" + QuoteJson(outcome) + "}" });
        flow.transitions.Add(FlowTransition(id + ".next", id + ".say", endId, "next"));
        package.flows.Add(flow);
        AddFlowSource(package, flow);
    }

    private static void AddFlowSource(ContentPackage package, FlowRecord flow)
    {
        string relativePath = "scenario/" + flow.id + ".json";
        package.legacySources.Add(new LegacySourceRecord
        {
            kind = ContentSourceKinds.ScenarioDefinition,
            relativePath = relativePath,
            sourceId = flow.id,
            sourceHash = Hash("source\0" + relativePath),
            contentId = flow.id,
            contentRevision = flow.revision,
            metadataJson = "{}"
        });
    }

    private static TransitionRecord FlowTransition(string id, string from, string to, string purpose)
    { return new TransitionRecord { id = id, fromStepId = from, toStepId = to, purpose = purpose }; }

    private static ChoiceRecord FlowChoice(string storeId, string step, string id, string transition,
        string label, TextRecord text, string value)
    {
        return new ChoiceRecord
        {
            stepId = step, id = id, transitionId = transition,
            valueJson = "{\"value\":" + QuoteJson(value) + "}",
            labelTextRef = new ContentRef(storeId, text.id, text.revision, text.semanticHash)
        };
    }

    private static ExitMappingRecord ExitMapping(string storeId, string callStep, FlowRecord callee,
        string exitName, string transition, string outputs)
    {
        return new ExitMappingRecord
        {
            callStepId = callStep,
            calleeFlowRef = new ContentRef(storeId, callee.id, callee.revision, callee.semanticHash),
            exitName = exitName, transitionId = transition, outputMappingsJson = outputs
        };
    }

    private static ScenarioRuntimeSnapshot FlowContext(string runId, IEnumerable<string> allowedMessages)
    {
        var action = new ScenarioCapabilitySnapshot("fixture.action", new[]
        { new ScenarioArgumentBinding("message", allowedMessages) });
        return new ScenarioRuntimeSnapshot("", runId, "turn-" + runId, 1, "", new string[0], new string[0],
            new[] { action }, new[] { new ScenarioObjectSnapshot("entity.one", "furniture") },
            new[] { "fixture.completed" }, null, new Dictionary<string, bool> { { "is_home", true } });
    }

    private static Dictionary<string, FlowValue> FlowInputs(string welcome)
    { return new Dictionary<string, FlowValue>(StringComparer.Ordinal) { { "welcome", FlowValue.Text(welcome) } }; }

    private static ContentExecutionPlan Prepare(FlowFixture fixture, string flowId, ScenarioRuntimeSnapshot context)
    { return Prepare(fixture, flowId, context, new Dictionary<string, FlowValue>(StringComparer.Ordinal)); }

    private static ContentExecutionPlan Prepare(FlowFixture fixture, string flowId, ScenarioRuntimeSnapshot context,
        IDictionary<string, FlowValue> inputs)
    {
        ContentResult<ResolvedContentRelease> resolved = ContentFlowAdapter.Resolve(fixture.package, fixture.release, fixture.vocabulary);
        Require(resolved.IsReady, "A validated package release did not resolve: " + Detail(resolved));
        FlowRecord flow = fixture.package.flows.First(item => item != null && item.id == flowId);
        ContentResult<ContentExecutionPlan> prepared = ContentFlowAdapter.Prepare(resolved.value,
            new ContentRef(fixture.storeId, flow.id, flow.revision, flow.semanticHash),
            inputs, context);
        Require(prepared.IsReady, "The exact Scenario Flow did not prepare: " + Detail(prepared));
        return prepared.value;
    }

    private static object BuildPackage(string storeId, string suffix)
    {
        object package = New("ContentPackage");
        SetField(package, "packageId", "fixture." + suffix);
        SetField(package, "storeId", storeId);

        string titleRevision = Hash("text-title-v1");
        string promptRevision = Hash("text-prompt-v1");
        string dialogueRevision = Hash("dialogue-" + suffix + "-v1");
        string actionRevision = Hash("action-" + suffix + "-v1");
        string flowRevision = Hash("flow-" + suffix + "-v1");
        string topicRevision = Hash("topic-" + suffix + "-v1");

        object title = New("TextRecord");
        SetField(title, "id", "text.title"); SetField(title, "revision", titleRevision); SetField(title, "semanticHash", titleRevision);
        Add(GetField(title, "translations"), Translation("en", "A title"));
        Add(GetField(package, "texts"), title);

        object prompt = New("TextRecord");
        SetField(prompt, "id", "text.prompt"); SetField(prompt, "revision", promptRevision); SetField(prompt, "semanticHash", promptRevision);
        Add(GetField(prompt, "translations"), Translation("en", "A prompt"));
        Add(GetField(package, "texts"), prompt);

        object dialogue = New("DialogueRecord");
        SetField(dialogue, "id", "dialogue.sample"); SetField(dialogue, "revision", dialogueRevision); SetField(dialogue, "semanticHash", dialogueRevision);
        object beat = New("BeatRecord");
        SetField(beat, "id", "beat.start"); SetField(beat, "sequenceOrder", 0);
        SetField(beat, "textRef", ContentRef(storeId, "text.title", titleRevision));
        SetField(beat, "expression", ""); SetField(beat, "presentationJson", "{}");
        Add(GetField(dialogue, "beats"), beat);
        Add(GetField(package, "dialogues"), dialogue);

        object action = New("ActionRecord");
        SetField(action, "id", "action.sample"); SetField(action, "revision", actionRevision); SetField(action, "semanticHash", actionRevision);
        SetField(action, "capabilityId", "fixture.action"); SetField(action, "contractJson", "{}");
        Add(GetField(package, "actions"), action);

        object flow = New("FlowRecord");
        SetField(flow, "id", "scenario.sample"); SetField(flow, "revision", flowRevision); SetField(flow, "semanticHash", flowRevision);
        SetField(flow, "kind", "scenario"); SetField(flow, "entryStepId", "start");
        object start = New("StepRecord");
        SetField(start, "id", "start"); SetField(start, "kind", "dialogue");
        SetField(start, "dialogueRef", ContentRef(storeId, "dialogue.sample", dialogueRevision));
        SetField(start, "dataJson", "{}"); Add(GetField(flow, "steps"), start);
        object end = New("StepRecord"); SetField(end, "id", "end"); SetField(end, "kind", "end"); SetField(end, "dataJson", "{}");
        Add(GetField(flow, "steps"), end);
        object transition = New("TransitionRecord");
        SetField(transition, "id", "start.next"); SetField(transition, "fromStepId", "start"); SetField(transition, "toStepId", "end");
        SetField(transition, "purpose", "next"); Add(GetField(flow, "transitions"), transition);
        Add(GetField(package, "flows"), flow);

        object topic = New("TopicRecord");
        SetField(topic, "id", "topic.sample"); SetField(topic, "revision", topicRevision); SetField(topic, "semanticHash", topicRevision);
        SetField(topic, "routeId", "topic.sample"); SetField(topic, "defaultReplayPolicy", "repeatable"); SetField(topic, "cooldownSeconds", 0d);
        SetField(topic, "titleTextRef", ContentRef(storeId, "text.title", titleRevision));
        SetField(topic, "promptTextRef", ContentRef(storeId, "text.prompt", promptRevision));
        object entry = New("TopicEntryRecord");
        SetField(entry, "id", "main"); SetField(entry, "flowRef", ContentRef(storeId, "scenario.sample", flowRevision));
        SetField(entry, "priority", 0); SetField(entry, "baseWeight", 1d); SetField(entry, "conditionJson", "{}"); SetField(entry, "modifiersJson", "[]");
        Add(GetField(topic, "entries"), entry); Add(GetField(package, "topics"), topic);
        return package;
    }

    private static object Translation(string language, string text)
    {
        object translation = New("TranslationRecord");
        SetField(translation, "language", language); SetField(translation, "text", text);
        return translation;
    }

    private static object ContentRef(string store, string id, string revision, string expectedHash = "")
    {
        object reference = New("ContentRef");
        SetField(reference, "store", store); SetField(reference, "id", id); SetField(reference, "revision", revision);
        SetField(reference, "expectedHash", expectedHash);
        return reference;
    }

    private static object OpenRepository(string path, string mode)
    {
        object result = OpenResult(path, mode);
        Require(Status(result) == "ready", "Open(" + mode + ") returned " + Status(result));
        return Value(result);
    }

    private static object OpenResult(string path, string mode)
    {
        Type repository = ContentType("ContentRepository");
        Type modeType = ContentType("ContentStoreMode");
        object modeValue = Enum.Parse(modeType, mode);
        return repository.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), modeType }, null).Invoke(null, new[] { (object)path, modeValue });
    }

    private static Type ContentType(string name)
    {
        Type type = runtime.GetType("AlterOS.Content." + name, false);
        if (type == null) throw new InvalidOperationException("Repository API missing: AlterOS.Content." + name + " is not implemented.");
        return type;
    }

    private static object New(string name) { return Activator.CreateInstance(ContentType(name)); }
    private static object GetProperty(object instance, string name) { return instance.GetType().GetProperty(name).GetValue(instance, null); }
    private static object GetField(object instance, string name) { return instance.GetType().GetField(name).GetValue(instance); }
    private static object GetNonPublicField(object instance, string name)
    { return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static void SetField(object instance, string name, object value) { instance.GetType().GetField(name).SetValue(instance, value); }
    private static void Add(object list, object value) { ((IList)list).Add(value); }
    private static object First(object list) { return ((IList)list)[0]; }
    private static object Invoke(object target, string method, params object[] args) { return target.GetType().GetMethod(method).Invoke(target, args); }
    private static string Status(object result) { return (string)GetField(result, "status"); }
    private static object Value(object result) { return GetField(result, "value"); }
    private static string Detail(object result)
    {
        var diagnostics = GetField(result, "diagnostics") as IEnumerable;
        if (diagnostics == null) return string.Empty;
        foreach (object diagnostic in diagnostics) return GetField(diagnostic, "message") as string ?? string.Empty;
        return string.Empty;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + Unwrap(exception)); }
    }

    private static Exception Unwrap(Exception exception)
    {
        TargetInvocationException invocation = exception as TargetInvocationException;
        return invocation != null && invocation.InnerException != null ? invocation.InnerException : exception;
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
        return true;
    }

    private sealed class LegacyRouteMatch
    {
        public string routeId;
        public float score;
        public float bestSimilarity;
        public int supportCount;
        public string bestAnchor;
    }

    private sealed class LegacySemanticFileIndex
    {
        private readonly int dimension;
        private readonly string[] routeIds;
        private readonly string[] anchorTexts;
        private readonly float[][] vectors;
        private readonly Dictionary<string, int> anchorIndexes;
        private readonly Dictionary<string, List<int>> indexesByRoute;
        internal int AnchorCount { get { return vectors.Length; } }
        internal int RouteCount { get { return indexesByRoute.Count; } }
        internal long VectorPayloadBytes { get { return (long)vectors.Length * dimension * sizeof(float); } }

        private LegacySemanticFileIndex(int vectorDimension, string[] routes, string[] texts, float[][] values)
        {
            dimension = vectorDimension; routeIds = routes; anchorTexts = texts; vectors = values;
            anchorIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            indexesByRoute = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int index = 0; index < vectors.Length; index++)
            {
                string anchorId = Hash(Encoding.UTF8.GetBytes(anchorTexts[index]));
                if (!anchorIndexes.ContainsKey(anchorId)) anchorIndexes.Add(anchorId, index);
                List<int> routeIndexes;
                if (!indexesByRoute.TryGetValue(routeIds[index], out routeIndexes))
                { routeIndexes = new List<int>(); indexesByRoute.Add(routeIds[index], routeIndexes); }
                routeIndexes.Add(index);
            }
        }

        internal static LegacySemanticFileIndex Load(string metadataPath, string vectorsPath)
        {
            byte[] metadataBytes = File.ReadAllBytes(metadataPath);
            byte[] vectorBytes = File.ReadAllBytes(vectorsPath);
            JsonObject metadata = JToken.Parse(Encoding.UTF8.GetString(metadataBytes)) as JsonObject;
            JsonArray anchors = metadata == null ? null : metadata["anchors"] as JsonArray;
            int dimension = metadata == null ? 0 : (int)metadata["dimension"];
            if (anchors == null || dimension <= 0 || vectorBytes.Length != anchors.Count * dimension * sizeof(float))
                throw new InvalidDataException("The legacy semantic file index has inconsistent metadata and vector bytes.");
            var routeIds = new string[anchors.Count];
            var texts = new string[anchors.Count];
            var vectors = new float[anchors.Count][];
            for (int index = 0; index < anchors.Count; index++)
            {
                JsonObject anchor = (JsonObject)anchors[index];
                routeIds[index] = (string)anchor["routeId"];
                texts[index] = (string)anchor["text"];
                vectors[index] = ReadLittleEndianVector(vectorBytes, index, dimension);
            }
            return new LegacySemanticFileIndex(dimension, routeIds, texts, vectors);
        }

        internal bool ContainsAnchor(string anchorId) { return anchorIndexes.ContainsKey(anchorId); }

        internal LegacyRouteMatch[] RankRoutes(float[] query, float minimumSimilarity)
        {
            var similarities = new float[vectors.Length];
            for (int index = 0; index < vectors.Length; index++)
            {
                float score = 0f;
                for (int axis = 0; axis < dimension; axis++) score += vectors[index][axis] * query[axis];
                similarities[index] = score;
            }
            var result = new List<LegacyRouteMatch>(indexesByRoute.Count);
            foreach (KeyValuePair<string, List<int>> route in indexesByRoute)
            {
                var nearest = new List<int>(3);
                foreach (int anchorIndex in route.Value)
                {
                    int insertAt = 0;
                    while (insertAt < nearest.Count && similarities[nearest[insertAt]] >= similarities[anchorIndex]) insertAt++;
                    if (insertAt >= 3) continue;
                    nearest.Insert(insertAt, anchorIndex);
                    if (nearest.Count > 3) nearest.RemoveAt(3);
                }
                float weighted = 0f, weights = 0f;
                int support = 0;
                for (int index = 0; index < nearest.Count; index++)
                {
                    float similarity = similarities[nearest[index]], weight = 1f / (index + 1);
                    weighted += similarity * weight; weights += weight;
                    if (similarity >= minimumSimilarity) support++;
                }
                weighted /= weights;
                float best = similarities[nearest[0]];
                result.Add(new LegacyRouteMatch { routeId = route.Key, score = weighted + 0.40f * (best - weighted),
                    bestSimilarity = best, supportCount = support, bestAnchor = anchorTexts[nearest[0]] });
            }
            result.Sort((left, right) => right.score.CompareTo(left.score));
            return result.ToArray();
        }
    }

    private static void ReportSemanticPerformance(string root, string databasePath, string storeId, ContentRelease release,
        string profileId, byte[] metadataBytes, byte[] vectorBytes, JsonArray metadataAnchors, float[] query, string anchorId,
        ContentSemanticSnapshot loadedSnapshot)
    {
        string resources = Path.Combine(FindProjectRoot(), "Assets/Resources/AlterOS/Semantics/Models");
        string metadataPath = Path.Combine(resources, "gte-anchors-metadata.json");
        string vectorsPath = Path.Combine(resources, "gte-anchors-vectors.bytes");
        const int samples = 15;
        var firstOpenTimer = Stopwatch.StartNew();
        ContentResult<ContentRepository> firstOpen = ContentRepository.Open(databasePath, ContentStoreMode.ReadOnly, storeId);
        firstOpenTimer.Stop();
        Require(firstOpen.IsReady, "The read-only benchmark database did not open: " + Detail(firstOpen));
        ContentRepository readOnly = firstOpen.value;
        double[] warmOpen = MeasureSamples(samples, () =>
        {
            ContentResult<ContentRepository> opened = ContentRepository.Open(databasePath, ContentStoreMode.ReadOnly, storeId);
            Require(opened.IsReady, "A repeated read-only database open failed.");
        });

        var firstFileTimer = Stopwatch.StartNew();
        LegacySemanticFileIndex firstFile = LegacySemanticFileIndex.Load(metadataPath, vectorsPath);
        firstFileTimer.Stop();
        double[] warmFileLoad = MeasureSamples(samples, () => { LegacySemanticFileIndex.Load(metadataPath, vectorsPath); });
        double[] warmSqlLoad = MeasureSamples(samples, () =>
        {
            ContentResult<ContentSemanticSnapshot> index = ContentSemanticIndex.Load(readOnly, release, profileId);
            Require(index.IsReady, "A repeated SQL semantic index load failed: " + Detail(index));
        });
        double[] fileLookup = MeasurePerOperationSamples(samples, 1000, () => Require(firstFile.ContainsAnchor(anchorId), "File index lost an Anchor ID."));
        double[] sqlLookup = MeasurePerOperationSamples(samples, 1000, () => Require(loadedSnapshot.GetAnchor(anchorId) != null, "SQL snapshot lost an Anchor ID."));
        double[] fileRank = MeasureSamples(samples, () => firstFile.RankRoutes(query, 0.45f));
        double[] sqlRank = MeasureSamples(samples, () => loadedSnapshot.RankRoutes(query, 0.45f));

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long beforeFile = GC.GetTotalMemory(true);
        LegacySemanticFileIndex memoryFile = LegacySemanticFileIndex.Load(metadataPath, vectorsPath);
        long fileManagedBytes = Math.Max(0L, GC.GetTotalMemory(true) - beforeFile);
        GC.KeepAlive(memoryFile);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long beforeSql = GC.GetTotalMemory(true);
        ContentResult<ContentSemanticSnapshot> memorySqlResult = ContentSemanticIndex.Load(readOnly, release, profileId);
        Require(memorySqlResult.IsReady, "The retained SQL index benchmark snapshot did not load.");
        ContentSemanticSnapshot memorySql = memorySqlResult.value;
        long sqlManagedBytes = Math.Max(0L, GC.GetTotalMemory(true) - beforeSql);
        GC.KeepAlive(memorySql);

        Console.WriteLine("BENCHMARK Semantics data=GTE-asset anchors=" + loadedSnapshot.AnchorCount + " routes=" + loadedSnapshot.RouteCount +
            " links=" + loadedSnapshot.LinkCount + " dimension=" + loadedSnapshot.Dimension + " vector_bytes=" + vectorBytes.Length +
            " metadata_bytes=" + metadataBytes.Length + " samples=" + samples + " file_anchor_records=" + metadataAnchors.Count);
        Console.WriteLine("BENCHMARK Semantics first_db_open_ms=" + firstOpenTimer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
            " warm_db_open_p50_p95_ms=" + Summary(warmOpen) + " first_file_index_load_ms=" + firstFileTimer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
            " warm_file_index_load_p50_p95_ms=" + Summary(warmFileLoad) + " warm_sql_index_load_p50_p95_ms=" + Summary(warmSqlLoad));
        Console.WriteLine("BENCHMARK Semantics file_id_lookup_p50_p95_us=" + Summary(fileLookup) +
            " sql_id_lookup_p50_p95_us=" + Summary(sqlLookup) + " file_route_rank_p50_p95_ms=" + Summary(fileRank) +
            " sql_route_rank_p50_p95_ms=" + Summary(sqlRank) + " retained_managed_bytes_file_sql=" + fileManagedBytes + "/" + sqlManagedBytes +
            " file_vector_payload_bytes=" + firstFile.VectorPayloadBytes);
    }

    private static double[] MeasureSamples(int count, Action action)
    {
        var result = new double[count];
        for (int index = 0; index < count; index++)
        {
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            result[index] = timer.Elapsed.TotalMilliseconds;
        }
        return result;
    }

    private static double[] MeasurePerOperationSamples(int count, int operationsPerSample, Action action)
    {
        var result = new double[count];
        for (int sample = 0; sample < count; sample++)
        {
            var timer = Stopwatch.StartNew();
            for (int iteration = 0; iteration < operationsPerSample; iteration++) action();
            timer.Stop();
            result[sample] = timer.Elapsed.TotalMilliseconds * 1000d / operationsPerSample;
        }
        return result;
    }

    private static string Summary(double[] samples)
    {
        double[] ordered = (samples ?? new double[0]).OrderBy(item => item).ToArray();
        if (ordered.Length == 0) return "n/a";
        int medianIndex = (int)Math.Floor((ordered.Length - 1) * 0.50d);
        int p95Index = (int)Math.Ceiling((ordered.Length - 1) * 0.95d);
        return ordered[medianIndex].ToString("F3", CultureInfo.InvariantCulture) + "/" +
            ordered[p95Index].ToString("F3", CultureInfo.InvariantCulture);
    }

    private static void ReadGteAssets(out JsonObject metadata, out byte[] metadataBytes, out byte[] vectorBytes, out byte[] tokenizerBytes)
    {
        string projectRoot = FindProjectRoot();
        string resourceRoot = Path.Combine(projectRoot, "Assets/Resources/AlterOS/Semantics/Models");
        metadataBytes = File.ReadAllBytes(Path.Combine(resourceRoot, "gte-anchors-metadata.json"));
        vectorBytes = File.ReadAllBytes(Path.Combine(resourceRoot, "gte-anchors-vectors.bytes"));
        tokenizerBytes = File.ReadAllBytes(Path.Combine(resourceRoot, "tokenizer.json"));
        metadata = JToken.Parse(Encoding.UTF8.GetString(metadataBytes)) as JsonObject;
        Require(metadata != null, "The existing GTE metadata is not a JSON object.");
    }

    private static void ReadLegacyGteAssets(out JsonObject metadata, out byte[] metadataBytes, out byte[] vectorBytes, out byte[] tokenizerBytes)
    {
        ReadGteAssets(out metadata, out metadataBytes, out vectorBytes, out tokenizerBytes);
        string genericRoot = Path.Combine(FindProjectRoot(), "Assets", "StreamingAssets", "Dialogue", "Generic");
        ContentResult<string> legacyHash = ContentSourceManifestBuilder.ComputeGenericCorpusHash(genericRoot);
        Require(legacyHash.IsReady && legacyHash.value == LegacyGenericCorpusHash,
            "the retained legacy Generic corpus changed from its pinned fingerprint: " + Detail(legacyHash));
        metadata = (JsonObject)metadata.DeepClone();
        metadata["contentHash"] = LegacyGenericCorpusHash;
        metadataBytes = Encoding.UTF8.GetBytes(metadata.ToString(Newtonsoft.Json.Formatting.None));
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(Directory.GetCurrentDirectory()));
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectSettings", "ProjectVersion.txt"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate the AlterOS project root from the validation working directory.");
    }

    private static void AddLocalSemanticAnchor(FlowFixture fixture, string text, string language, byte[] vector,
        out AnchorRecord anchor, out EmbeddingProfileRecord profile)
    {
        string anchorHash = Hash(Encoding.UTF8.GetBytes(text));
        anchor = new AnchorRecord { id = anchorHash, textHash = anchorHash, text = text, language = language };
        fixture.package.anchors.Add(anchor);
        TopicRecord topic = fixture.package.topics.Single(item => item.id == "topic.sample");
        fixture.package.anchorLinks.Add(new AnchorLinkRecord
        { id = "link.local.semantic", anchorId = anchorHash, ownerKind = "topic", ownerId = topic.id,
            ownerRevision = topic.revision, role = "local" });
        profile = new EmbeddingProfileRecord
        {
            id = "fixture.semantic.profile", modelId = "fixture/provider-model", modelRevision = "fixture-r1",
            tokenizerHash = Hash("fixture-tokenizer"), pipelineHash = Hash("fixture-pipeline"),
            dimensions = 2, dtype = "float32-le", normalization = "l2", metric = "cosine"
        };
        fixture.package.embeddingProfiles.Add(profile);
        if (vector != null)
            fixture.package.embeddings.Add(new EmbeddingRecord
            { anchorId = anchorHash, profileId = profile.id, vector = vector, vectorHash = Hash(vector) });
    }

    private static void AddSemanticTestTopic(FlowFixture fixture)
    {
        string storeId = fixture.package.storeId;
        TextRecord title = AddFlowText(fixture.package, "text.semantic-title", "Semantic test", "意味のテスト");
        TextRecord prompt = AddFlowText(fixture.package, "text.semantic-prompt", "Choose this topic", "このトピックを選んでください");
        FlowRecord flow = fixture.package.flows.Single(item => item.id == "scenario.root");
        string revision = Hash("semantic-test-topic-v1");
        var topic = new TopicRecord
        {
            id = "topic.sample", revision = revision, semanticHash = revision, routeId = "topic.sample",
            titleTextRef = new ContentRef(storeId, title.id, title.revision, title.semanticHash),
            promptTextRef = new ContentRef(storeId, prompt.id, prompt.revision, prompt.semanticHash),
            defaultReplayPolicy = "repeatable", cooldownSeconds = 0d
        };
        topic.entries.Add(new TopicEntryRecord
        {
            id = "main", flowRef = new ContentRef(storeId, flow.id, flow.revision, flow.semanticHash),
            priority = 0, baseWeight = 1d, conditionJson = "{}", modifiersJson = "[]", channelsJson = "[]"
        });
        fixture.package.topics.Add(topic);
    }

    private static void AddLocalSemanticAnchor(FlowFixture fixture, string text, string language, byte[] vector)
    {
        AnchorRecord anchor;
        EmbeddingProfileRecord profile;
        AddLocalSemanticAnchor(fixture, text, language, vector, out anchor, out profile);
    }

    private static byte[] FloatVectorBytes(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            byte[] part = BitConverter.GetBytes(values[index]);
            if (!BitConverter.IsLittleEndian) Array.Reverse(part);
            Buffer.BlockCopy(part, 0, bytes, index * sizeof(float), sizeof(float));
        }
        return bytes;
    }

    private static float[] ReadLittleEndianVector(byte[] bytes, int vectorIndex, int dimension)
    {
        var result = new float[dimension];
        int offset = checked(vectorIndex * dimension * sizeof(float));
        for (int index = 0; index < dimension; index++)
        {
            byte[] part = new byte[sizeof(float)];
            Buffer.BlockCopy(bytes, offset + index * sizeof(float), part, 0, sizeof(float));
            if (!BitConverter.IsLittleEndian) Array.Reverse(part);
            result[index] = BitConverter.ToSingle(part, 0);
        }
        return result;
    }

    private static IList LegacyRouteRank(JsonArray anchors, byte[] vectors, int dimension, float[] query, float minimumSimilarity)
    {
        var similarities = new float[anchors.Count];
        for (int anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
        {
            float[] vector = ReadLittleEndianVector(vectors, anchorIndex, dimension);
            float similarity = 0f;
            for (int axis = 0; axis < dimension; axis++) similarity += vector[axis] * query[axis];
            similarities[anchorIndex] = similarity;
        }
        var byRoute = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (int anchorIndex in Enumerable.Range(0, anchors.Count))
        {
            string routeId = (string)((JsonObject)anchors[anchorIndex])["routeId"];
            if (!byRoute.ContainsKey(routeId)) byRoute.Add(routeId, new List<int>());
            byRoute[routeId].Add(anchorIndex);
        }
        var matches = new List<LegacyRouteMatch>();
        foreach (KeyValuePair<string, List<int>> route in byRoute)
        {
            var nearest = new List<int>(3);
            foreach (int anchorIndex in route.Value)
            {
                int insertAt = 0;
                while (insertAt < nearest.Count && similarities[nearest[insertAt]] >= similarities[anchorIndex]) insertAt++;
                if (insertAt >= 3) continue;
                nearest.Insert(insertAt, anchorIndex);
                if (nearest.Count > 3) nearest.RemoveAt(3);
            }
            float weighted = 0f, weights = 0f;
            int supportCount = 0;
            for (int index = 0; index < nearest.Count; index++)
            {
                float similarity = similarities[nearest[index]];
                float weight = 1f / (index + 1);
                weighted += similarity * weight; weights += weight;
                if (similarity >= minimumSimilarity) supportCount++;
            }
            weighted /= weights;
            float best = similarities[nearest[0]];
            matches.Add(new LegacyRouteMatch
            {
                routeId = route.Key,
                score = weighted + 0.40f * (best - weighted),
                bestSimilarity = best,
                supportCount = supportCount,
                bestAnchor = (string)((JsonObject)anchors[nearest[0]])["text"]
            });
        }
        matches.Sort((left, right) => right.score.CompareTo(left.score));
        return matches.Cast<object>().ToList();
    }

    private sealed class FakeGrantContentReader : IContentReleaseReader
    {
        public string storeId;
        public ActionRecord action;
        public ItemRecord item;
        public FlowRecord flow;

        public ContentResult<ActionRecord> ReadAction(ContentRef reference)
        {
            return action != null && reference != null && action.id == reference.id && action.revision == reference.revision
                ? ContentResult<ActionRecord>.Ready(action)
                : ContentResult<ActionRecord>.Failure(ContentStatuses.Missing, "action_missing", reference == null ? "" : reference.id, "missing");
        }

        public ContentResult<ItemRecord> ReadItem(ContentRef reference)
        {
            return item != null && reference != null && item.id == reference.id && item.revision == reference.revision &&
                (string.IsNullOrEmpty(reference.expectedHash) || reference.expectedHash == item.semanticHash)
                ? ContentResult<ItemRecord>.Ready(item)
                : ContentResult<ItemRecord>.Failure(ContentStatuses.Missing, "item_missing", reference == null ? "" : reference.id, "missing");
        }

        public ContentResult<FlowRecord> ReadFlowById(string flowId)
        {
            return flow != null && flow.id == flowId ? ContentResult<FlowRecord>.Ready(flow) :
                ContentResult<FlowRecord>.Failure(ContentStatuses.Missing, "flow_missing", flowId ?? "", "missing");
        }

        public ContentResult<FlowRecord> ReadFlow(ContentRef reference)
        {
            return flow != null && reference != null && reference.store == storeId && reference.id == flow.id &&
                reference.revision == flow.revision && reference.expectedHash == flow.semanticHash
                ? ContentResult<FlowRecord>.Ready(flow)
                : ContentResult<FlowRecord>.Failure(ContentStatuses.Missing, "flow_pin_missing", reference == null ? "" : reference.id, "missing");
        }
    }

    private sealed class ContentActionFixture
    {
        public string action_id { get; set; }
        public string revision { get; set; }
        public string semantic_hash { get; set; }
    }

    private sealed class ContentFlowFixture
    {
        public string flow_id { get; set; }
        public string revision { get; set; }
    }

    private sealed class ContentColumnFixture
    {
        public string name { get; set; }
    }

    private sealed class FakeItemGrantGameRepository : IGameRepository
    {
        private readonly string playerId;
        private readonly System.Collections.Generic.Dictionary<string, GameItemQuantityOperationResult> operations =
            new System.Collections.Generic.Dictionary<string, GameItemQuantityOperationResult>(StringComparer.Ordinal);
        private GameCommitReceipt lastReceipt;
        public long quantity;
        public GameScenarioHostEffectEvidenceResult hostEffectEvidence;
        private long revision;
        public string DatabasePath { get { return "synthetic"; } }

        public FakeItemGrantGameRepository(string storeId) { playerId = "player.synthetic"; }

        public GameLoadResult Load()
        { return new GameLoadResult { state = GamePersistenceStates.Ready, identity = new GameSaveIdentity
            { legacyPlayerId = playerId, accountId = playerId, saveId = "save.synthetic", homeId = "home.synthetic" },
            gameRevision = revision, layoutRevision = 0, progressionJson = "{\"schemaVersion\":4}" }; }

        public GameItemInventoryResult ReadPlayerItemQuantities()
        { return new GameItemInventoryResult { state = GamePersistenceStates.Ready, playerId = playerId, gameRevision = revision,
            items = quantity == 0 ? new List<GameItemQuantityView>() : new List<GameItemQuantityView> { new GameItemQuantityView { itemId = "item.water_glass", quantity = quantity } } }; }

        public GameScenarioHostEffectEvidenceResult ReadScenarioHostEffectEvidence(string runId, string invocationId,
            string effectStepExecutionId, string completedStepExecutionId)
        {
            return hostEffectEvidence == null ? new GameScenarioHostEffectEvidenceResult { state = "missing", errorCode = "scenario_effect_evidence_missing" } :
                hostEffectEvidence;
        }

        public GameCommitReceipt FindReceipt(string operationId)
        { GameItemQuantityOperationResult operation; return operations.TryGetValue(operationId, out operation) ? operation.receipt : null; }

        public GameItemQuantityOperationResult FindItemQuantityOperation(string operationId)
        { GameItemQuantityOperationResult operation; return operations.TryGetValue(operationId, out operation) ? operation : new GameItemQuantityOperationResult { state = "missing" }; }

        public GameFurniturePowerOperationResult FindFurniturePowerOperation(string operationId)
        { return new GameFurniturePowerOperationResult { state = "missing" }; }

        public GameCommitResult Commit(GameCommitRequest request)
        {
            GameItemQuantityChangeRecord change = request.itemQuantityChanges.Single();
            GameEventHistoryContext context = request.eventContexts.Single();
            quantity = change.expectedQuantity + change.delta;
            revision++;
            lastReceipt = new GameCommitReceipt { operationId = request.operationId, payloadHash = "synthetic",
                gameRevision = revision, layoutRevision = 0, state = GamePersistenceStates.Committed };
            operations.Add(request.operationId, new GameItemQuantityOperationResult { state = "found", operationId = request.operationId,
                receipt = lastReceipt, change = change, eventContext = context });
            return new GameCommitResult { state = GamePersistenceStates.Committed, receipt = lastReceipt };
        }

        public GameMigrationReceipt FindMigration(string sourceHash) { return null; }
        public bool CreateConsistentBackup(string destinationPath) { return false; }
        public GameBackupValidationResult ValidateBackup(string path) { return null; }
    }

    private static int CountRows(string databasePath, string table)
    {
        using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM " + table);
    }

    private static string ContentHashFields(string[] values)
    {
        MethodInfo method = typeof(ContentRepository).GetMethod("HashFields", BindingFlags.Static | BindingFlags.NonPublic);
        Require(method != null, "the existing content row-hash writer helper is missing");
        return (string)method.Invoke(null, new object[] { values });
    }

    private static string ContentHashObjectFields(object[] values)
    {
        MethodInfo method = typeof(ContentRepository).GetMethod("HashObjectFields", BindingFlags.Static | BindingFlags.NonPublic);
        Require(method != null, "the existing content revision-hash writer helper is missing");
        return (string)method.Invoke(null, new object[] { values });
    }

    private static string Hash(string text)
    {
        return Hash(Encoding.UTF8.GetBytes(text));
    }

    private static string Hash(byte[] data)
    {
        using (SHA256 hash = SHA256.Create())
        {
            byte[] bytes = hash.ComputeHash(data ?? new byte[0]);
            var output = new StringBuilder(bytes.Length * 2);
            foreach (byte item in bytes) output.Append(item.ToString("x2"));
            return output.ToString();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
