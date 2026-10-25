using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Topics;
using AlterOS.Furniture;
using AlterOS.Activities;

namespace AlterOS.Content
{
    /// <summary>Validates content against host vocabulary and exact package-local references.</summary>
    public static class ContentPackageValidator
    {
        public const string ValidatorVersion = "content-validator-v1";
        public const int MaximumRuleDepth = 8;
        public const int MaximumRuleNodes = 256;
        public const int MaximumSubflowDepth = 8;
        public const int MaximumExpandedSteps = 64;

        public static ContentPackageValidationResult Validate(ContentPackage package, ContentHostVocabulary hostVocabulary)
        {
            var issues = new List<ContentDiagnostic>();
            if (package == null || string.IsNullOrWhiteSpace(package.storeId) || hostVocabulary == null)
            {
                Add(issues, "content_validation_context_missing", "", "A package and host vocabulary are required.");
                return new ContentPackageValidationResult(false, issues);
            }
            if (package.topics == null || package.flows == null || package.dialogues == null || package.texts == null ||
                package.actions == null || package.items == null || package.furnitureDefinitions == null ||
                package.rules == null || package.anchors == null || package.anchorLinks == null ||
                package.embeddingProfiles == null || package.embeddings == null || package.legacySources == null || package.releases == null)
            {
                Add(issues, "content_collections_missing", package.packageId, "All package collections must be present, including empty lists.");
                return new ContentPackageValidationResult(false, issues);
            }

            var texts = Index(package.texts, item => item.id, item => item.revision, item => item.semanticHash, "text", issues);
            var dialogues = Index(package.dialogues, item => item.id, item => item.revision, item => item.semanticHash, "dialogue", issues);
            var actions = Index(package.actions, item => item.id, item => item.revision, item => item.semanticHash, "action", issues);
            var items = Index(package.items, item => item.id, item => item.revision, item => item.semanticHash, "item", issues);
            var furniture = Index(package.furnitureDefinitions, item => item.id, item => item.revision, item => item.semanticHash, "furniture", issues);
            var rules = Index(package.rules, item => item.id, item => item.revision, item => item.semanticHash, "rule", issues);
            var flows = Index(package.flows, item => item.id, item => item.revision, item => item.semanticHash, "flow", issues);
            var topics = Index(package.topics, item => item.id, item => item.revision, item => item.semanticHash, "topic", issues);

            ValidateTexts(package, issues);
            ValidateDialogues(package, texts, hostVocabulary, issues);
            ValidateReusableRecords(package, texts, issues);
            ValidateFurnitureCatalogue(package, issues);
            ValidateActions(package, hostVocabulary, items, issues);
            ValidateRules(package, rules, issues);
            ValidateFlows(package, texts, dialogues, actions, rules, flows, hostVocabulary, issues);
            ValidateTopics(package, texts, flows, issues);
            ValidateAnchors(package, topics, flows, dialogues, hostVocabulary, issues);
            ValidateSemanticEmbeddings(package, issues);
            ValidateLegacySources(package, topics, flows, dialogues, actions, furniture, items, issues);
            ValidateSubflowGraph(package, flows, issues);
            return new ContentPackageValidationResult(issues.Count == 0, issues);
        }

        internal static TopicCondition ResolveRule(ContentPackage package, ContentRef reference, out List<ContentDiagnostic> issues)
        {
            issues = new List<ContentDiagnostic>();
            if (package == null || reference == null || reference.store != package.storeId || !IsIdentifier(reference.id) || !IsSha256(reference.revision))
            {
                Add(issues, "rule_reference_invalid", "rule", "The Rule reference must name an exact local ID and revision.");
                return null;
            }
            RuleRecord rule = package.rules == null ? null : package.rules.FirstOrDefault(item => item != null &&
                item.id == reference.id && item.revision == reference.revision);
            if (rule == null)
            {
                Add(issues, "rule_missing_dependency", reference.id, "The exact Rule revision is missing from the host-resolved package.");
                return null;
            }
            if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != rule.semanticHash)
            {
                Add(issues, "rule_hash_mismatch", reference.id, "The exact Rule hash does not match the supplied reference.");
                return null;
            }
            var active = new HashSet<string>(StringComparer.Ordinal);
            int nodeCount = 0;
            return ExpandRuleRecord(package, rule, active, 1, ref nodeCount, issues);
        }

        private static Dictionary<string, T> Index<T>(IEnumerable<T> values, Func<T, string> getId,
            Func<T, string> getRevision, Func<T, string> getHash, string kind, List<ContentDiagnostic> issues)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T item in values)
            {
                if (item == null) { Add(issues, "content_record_null", kind, "A content record cannot be null."); continue; }
                string id = getId(item) ?? string.Empty;
                string revision = getRevision(item) ?? string.Empty;
                string semanticHash = getHash(item) ?? string.Empty;
                string key = Key(id, revision);
                if (!IsIdentifier(id) || !IsSha256(revision) || revision != semanticHash)
                { Add(issues, "content_revision_invalid", id, "A content revision must equal its canonical SHA-256 semantic hash."); continue; }
                if (!AddUnique(result, key, item)) Add(issues, "content_revision_duplicate", id, "The package repeats an immutable ID/revision pair.");
            }
            return result;
        }

        private static void ValidateTexts(ContentPackage package, List<ContentDiagnostic> issues)
        {
            foreach (TextRecord text in package.texts)
            {
                if (text == null) { Add(issues, "content_record_null", "text", "A Text record cannot be null."); continue; }
                if (text.translations == null || text.translations.Count == 0 || text.translations.Count > 64)
                { Add(issues, "text_translations_invalid", text.id, "A Text needs 1..64 translations."); continue; }
                var languages = new HashSet<string>(StringComparer.Ordinal);
                foreach (TranslationRecord translation in text.translations)
                    if (translation == null || !IsLanguage(translation.language) || translation.text == null || translation.text.Length > 8192 ||
                        !languages.Add(translation.language))
                        Add(issues, "text_translation_invalid", text.id, "A Text translation is malformed or duplicates a locale.");
            }
        }

        private static void ValidateDialogues(ContentPackage package, Dictionary<string, TextRecord> texts,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            foreach (DialogueRecord dialogue in package.dialogues)
            {
                if (dialogue == null) { Add(issues, "content_record_null", "dialogue", "A Dialogue record cannot be null."); continue; }
                if (dialogue.beats == null || dialogue.beats.Count < 1 || dialogue.beats.Count > 16)
                { Add(issues, "dialogue_beat_limit", dialogue.id, "A Dialogue must have one to sixteen Beats."); continue; }
                var beatIds = new HashSet<string>(StringComparer.Ordinal);
                var orders = new HashSet<int>();
                foreach (BeatRecord beat in dialogue.beats)
                {
                    if (beat == null || !IsIdentifier(beat.id) || !beatIds.Add(beat.id) || beat.sequenceOrder < 0 || !orders.Add(beat.sequenceOrder))
                    { Add(issues, "dialogue_beat_invalid", dialogue.id, "A Beat ID/order is invalid or duplicated."); continue; }
                    RequireReference(beat.textRef, package.storeId, texts, item => item.semanticHash, "text", issues, dialogue.id + "/" + beat.id);
                    if (beat.expression != string.Empty && !vocabulary.IsExpressionAllowed(beat.expression))
                        Add(issues, "expression_not_registered", beat.expression, "The expression is not in the host vocabulary.");
                    if (!IsJsonObject(beat.presentationJson)) Add(issues, "presentation_json_invalid", beat.id, "Beat presentation must be a JSON object.");
                }
            }
        }

        private static void ValidateReusableRecords(ContentPackage package, Dictionary<string, TextRecord> texts,
            List<ContentDiagnostic> issues)
        {
            foreach (ItemRecord item in package.items)
            {
                if (item == null || item.stackLimit <= 0)
                { Add(issues, "item_definition_invalid", "item", "An Item requires a positive stack limit."); continue; }
                RequireReference(item.displayTextRef, package.storeId, texts, value => value.semanticHash,
                    "text", issues, item.id + "/displayName");
            }
            foreach (FurnitureDefinitionRecord furniture in package.furnitureDefinitions)
            {
                if (furniture == null || !IsJsonObject(furniture.definitionJson) || furniture.functionBindings == null ||
                    furniture.functionBindings.Count > 32)
                { Add(issues, "furniture_definition_invalid", "furniture", "Furniture definition JSON and bounded function bindings are required."); continue; }
                RequireReference(furniture.displayTextRef, package.storeId, texts, value => value.semanticHash,
                    "text", issues, furniture.id + "/displayName");
                var functionKeys = new HashSet<string>(StringComparer.Ordinal);
                JObject definition;
                try { definition = JObject.Parse(furniture.definitionJson ?? string.Empty); }
                catch { definition = null; }
                JArray authoredBindings = definition == null ? null : definition["functionBindings"] as JArray;
                bool legacyUnmapped = authoredBindings == null || authoredBindings.Count == 0;
                foreach (FurnitureFunctionBindingRecord binding in furniture.functionBindings)
                {
                    if (binding == null || !IsIdentifier(binding.functionId) ||
                        !(legacyUnmapped && binding.pointId == string.Empty && binding.useId == string.Empty) &&
                            (!IsIdentifier(binding.pointId) || !IsIdentifier(binding.useId)) || binding.functionContractVersion != 1 ||
                        binding.functionId != FurnitureFunctionIds.PowerSet ||
                        !functionKeys.Add(binding.functionId + "\n" + binding.pointId + "\n" + binding.useId) ||
                        !IsJsonObject(binding.contractJson))
                        Add(issues, "furniture_function_binding_invalid", furniture.id,
                            "A Furniture function binding must match a registered version 1 contract and unique function/point/use tuple.");
                    if (binding != null && binding.functionId == FurnitureFunctionIds.PowerSet)
                    {
                        JsonObject contract = ParseObject(binding.contractJson, furniture.id + "/" + binding.functionId, issues);
                        if (contract != null)
                        {
                            CheckFields(contract, furniture.id + "/" + binding.functionId, issues, "desiredState");
                            JArray states = contract["desiredState"] as JArray;
                            if (states == null || states.Count != 2 || states[0].Type != JTokenType.String ||
                                states[1].Type != JTokenType.String || (string)states[0] != "Off" || (string)states[1] != "On")
                                Add(issues, "furniture_function_contract_invalid", furniture.id + "/" + binding.functionId,
                                    "power.set version 1 must declare exactly Off and On states.");
                        }
                    }
                }
                var authoredKeys = new HashSet<string>(StringComparer.Ordinal);
                if (authoredBindings != null)
                    foreach (JToken token in authoredBindings)
                    {
                        JObject binding = token as JObject;
                        string key = binding == null ? string.Empty : TokenString(binding["functionId"]) + "\n" +
                            TokenString(binding["pointId"]) + "\n" + TokenString(binding["useId"]);
                        if (binding == null || !authoredKeys.Add(key) || !functionKeys.Contains(key))
                            Add(issues, "furniture_function_contract_missing", furniture.id,
                                "Each authored Furniture function binding needs its exact host contract mapping.");
                    }
                if (!legacyUnmapped && authoredKeys.Count != functionKeys.Count)
                    Add(issues, "furniture_function_mapping_mismatch", furniture.id,
                        "Normalized function contracts must correspond one-to-one with the authored function bindings.");
            }
        }

        internal static ContentDiagnostic ValidateFurnitureCatalogueSource(ContentPackage package)
        {
            var issues = new List<ContentDiagnostic>();
            ValidateFurnitureCatalogue(package, issues);
            return issues.FirstOrDefault();
        }

        private static void ValidateFurnitureCatalogue(ContentPackage package, List<ContentDiagnostic> issues)
        {
            LegacySourceRecord[] sources = (package.legacySources ?? new List<LegacySourceRecord>())
                .Where(item => item != null && item.kind == ContentSourceKinds.FurnitureCatalogueSettings).ToArray();
            if (sources.Length > 1)
            {
                Add(issues, "furniture_catalogue_settings_duplicate", "furniture.catalogue", "Only one Furniture settings source is permitted in a package.");
                return;
            }
            if (sources.Length == 0)
            {
                if (package.furnitureCatalogueSettings != null)
                    Add(issues, "furniture_catalogue_settings_source_missing", "furniture.catalogue", "Compiled catalogue settings must be covered by one exact settings source.");
                return;
            }

            LegacySourceRecord source = sources[0];
            if (source.sourceId != "furniture.catalogue" || source.relativePath != "Furniture/catalogue-settings.json" ||
                source.contentId != "furniture.catalogue" || source.contentRevision != source.sourceHash ||
                package.furnitureCatalogueSettings == null || source.sourceBytes == null || source.sourceBytes.Length == 0)
            {
                Add(issues, "furniture_catalogue_settings_source_invalid", "furniture.catalogue", "The singleton settings source mapping or parsed settings document is missing.");
                return;
            }
            try
            {
                JsonValue raw = ContentJson.Parse(new UTF8Encoding(false, true).GetString(source.sourceBytes).TrimStart('\uFEFF'));
                foreach (ContentDiagnostic issue in LegacyContentAdapter.ValidateSourceShape(source.kind, raw, "furniture.catalogue"))
                    issues.Add(issue);
                FurnitureCatalogueDocument sourceSettings = JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(LegacyContentAdapter.CanonicalJson(raw));
                sourceSettings.definitions = new List<FurnitureDefinition>();
                if (FurnitureCatalogueRegistry.ComputeHash(sourceSettings) != FurnitureCatalogueRegistry.ComputeHash(package.furnitureCatalogueSettings))
                {
                    Add(issues, "furniture_catalogue_settings_source_mismatch", "furniture.catalogue", "Parsed settings differ from the exact source bytes in the selected package.");
                    return;
                }

                FurnitureCatalogueDocument complete = JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(
                    JsonConvert.SerializeObject(package.furnitureCatalogueSettings));
                complete.definitions = package.furnitureDefinitions.Where(item => item != null)
                    .Select(item => JsonConvert.DeserializeObject<FurnitureDefinition>(item.definitionJson)).ToList();
                foreach (FurnitureDiagnostic diagnostic in FurnitureCatalogueValidator.Validate(complete))
                    issues.Add(new ContentDiagnostic(diagnostic.code, "furniture.catalogue." + diagnostic.path, diagnostic.message));
            }
            catch (Exception exception)
            { Add(issues, "furniture_catalogue_settings_invalid", "furniture.catalogue", exception.Message); }
        }

        private static void ValidateActions(ContentPackage package, ContentHostVocabulary vocabulary,
            Dictionary<string, ItemRecord> items, List<ContentDiagnostic> issues)
        {
            foreach (ActionRecord action in package.actions)
            {
                if (action == null) { Add(issues, "content_record_null", "action", "An Action record cannot be null."); continue; }
                if (!vocabulary.IsActionRegistered(action.capabilityId))
                    Add(issues, "capability_not_registered", action.capabilityId, "Content cannot register a new game capability.");
                JsonObject contract = ParseObject(action.contractJson, action.id, issues);
                if (contract == null) { Add(issues, "action_contract_invalid", action.id, "An Action contract must be a bounded JSON object."); continue; }
                if (action.implementationKind == "activity_sequence")
                {
                    if (action.sequenceSteps == null || action.sequenceSteps.Count == 0)
                        Add(issues, "action_sequence_missing", action.id, "An activity_sequence must contain normalized steps produced by the trusted builder.");
                    if (action.sequenceSteps != null)
                    {
                        var ids = new HashSet<string>(StringComparer.Ordinal);
                        var ordinals = new HashSet<int>();
                        foreach (ActionSequenceStepRecord step in action.sequenceSteps)
                            if (step == null || !IsIdentifier(step.stepId) || !ids.Add(step.stepId) || step.ordinal < 0 || !ordinals.Add(step.ordinal) ||
                                !IsIdentifier(step.primitiveId) || !IsJsonArray(step.argumentsJson) || !IsJsonArray(step.outputsJson) ||
                                !IsJsonObject(step.successEdgeJson) || !IsJsonObject(step.failureEdgeJson) || !IsJsonObject(step.cancelEdgeJson) ||
                                (step.checkpointPolicy != "none" && step.checkpointPolicy != "before" && step.checkpointPolicy != "after"))
                                Add(issues, "action_sequence_step_invalid", action.id, "A normalized sequence step is malformed or duplicated.");
                    }
                    foreach (ContentDiagnostic diagnostic in ReusableContentDefinitionAdapter.ValidateNormalizedSequenceAction(
                        action, vocabulary.SequenceBuilder)) issues.Add(diagnostic);
                    ValidateSequenceDialogueReferences(package, action, issues);
                    continue;
                }
                if (action.implementationKind == "host_effect")
                {
                    if (!vocabulary.IsHostEffectFunctionRegistered(action.functionId, action.capabilityId))
                        Add(issues, "host_effect_capability_mismatch", action.id,
                            "A host-effect function must use its registered function-to-capability binding.");
                    if (action.functionId == ContentHostEffectFunctionRegistry.PlayerItemGrant)
                    {
                        JsonObject args = ParseObject(action.argumentsJson, action.id + "/arguments", issues);
                        ContentRef itemRef = ParseContentReference(args == null ? null : args["itemRef"]);
                        ItemRecord item = ResolveReference(itemRef, package.storeId, items, value => value.semanticHash,
                            "item", issues, action.id + "/arguments/itemRef");
                        long quantity;
                        if (args == null || !long.TryParse(TokenString(args["quantity"]), NumberStyles.None, CultureInfo.InvariantCulture, out quantity) ||
                            quantity <= 0 || item != null && quantity > item.stackLimit)
                            Add(issues, "item_grant_quantity_invalid", action.id, "A host Item grant must be positive and no greater than the pinned Item stack limit.");
                    }
                    else if (action.functionId != ContentHostEffectFunctionRegistry.FurniturePowerSet)
                        Add(issues, "content_function_unregistered", action.functionId, "The host-effect function is not registered.");
                    if (!IsJsonObject(action.argumentsJson)) Add(issues, "action_arguments_invalid", action.id, "Host-effect arguments must be a JSON object.");
                    continue;
                }
                if (action.implementationKind != "legacy_activity" && !string.IsNullOrEmpty(action.implementationKind))
                { Add(issues, "action_implementation_kind_invalid", action.id, "The Action implementation kind is unsupported."); continue; }
                CheckFields(contract, action.id, issues, "capabilityId", "arguments", "inputs", "results", "requirements", "outcomes");
                string contractCapability = String(contract, "capabilityId", string.Empty, action.id, issues);
                if (!string.IsNullOrEmpty(contractCapability) && contractCapability != action.capabilityId)
                    Add(issues, "action_contract_capability_mismatch", action.id, "The Action contract capability must match its host-registered record.");
                ValidateActionArguments(contract, action.id, issues);
                ReadActionContractMembers(action, "inputs", action.id, vocabulary, issues);
                ReadActionContractMembers(action, "results", action.id, vocabulary, issues);
            }
        }

        private static void ValidateSequenceDialogueReferences(ContentPackage package, ActionRecord action,
            List<ContentDiagnostic> issues)
        {
            foreach (ActionSequenceStepRecord step in action.sequenceSteps ?? new List<ActionSequenceStepRecord>())
            {
                if (step == null || step.primitiveId != ActivityPrimitiveIds.Bark) continue;
                JsonArray arguments;
                try { arguments = ContentJson.Parse(step.argumentsJson ?? "[]") as JsonArray; }
                catch { arguments = null; }
                JsonObject[] dialogueArguments = arguments == null ? new JsonObject[0] : arguments.OfType<JsonObject>()
                    .Where(item => TokenString(item["name"]) == "dialogueId").ToArray();
                if (dialogueArguments.Length != 1)
                {
                    Add(issues, "sequence_dialogue_id_invalid", action.id + "/" + step.stepId,
                        "A bark primitive must provide one literal dialogueId.");
                    continue;
                }
                JsonObject value = dialogueArguments[0]["value"] as JsonObject;
                if (value == null || value.Properties().Count() != 2 || TokenString(value["type"]) != "text" ||
                    value["literal"] == null || value["literal"].Type != JTokenType.String ||
                    value.Properties().Any(item => item.Name != "type" && item.Name != "literal"))
                {
                    Add(issues, "sequence_dialogue_id_invalid", action.id + "/" + step.stepId,
                        "A bark dialogueId must be a literal string naming one release-local Dialogue.");
                    continue;
                }
                string dialogueId = (string)value["literal"];
                DialogueRecord[] matches = (package.dialogues ?? new List<DialogueRecord>())
                    .Where(item => item != null && item.id == dialogueId).ToArray();
                if (matches.Length == 0)
                    Add(issues, "sequence_dialogue_missing", action.id + "/" + step.stepId,
                        "The bark Dialogue ID is absent from the selected package.");
                else if (matches.Length != 1)
                    Add(issues, "sequence_dialogue_ambiguous", action.id + "/" + step.stepId,
                        "The bark Dialogue ID must resolve to exactly one immutable revision in the selected package.");
                else if (matches[0].beats == null || matches[0].beats.Count == 0)
                    Add(issues, "sequence_dialogue_invalid", action.id + "/" + step.stepId,
                        "The bark Dialogue must include its validated Beat/Text closure.");
            }
        }

        private static void ValidateRules(ContentPackage package, Dictionary<string, RuleRecord> rules, List<ContentDiagnostic> issues)
        {
            foreach (RuleRecord rule in package.rules)
            {
                if (rule == null) continue;
                var active = new HashSet<string>(StringComparer.Ordinal);
                int nodeCount = 0;
                TopicCondition expanded = ExpandRuleRecord(package, rule, active, 1, ref nodeCount, issues);
                if (expanded == null) continue;
                foreach (string error in TopicDefinitionValidator.ValidateCondition(expanded))
                    Add(issues, "rule_condition_invalid", rule.id, error);
            }
        }

        private static void ValidateFlows(ContentPackage package, Dictionary<string, TextRecord> texts,
            Dictionary<string, DialogueRecord> dialogues, Dictionary<string, ActionRecord> actions,
            Dictionary<string, RuleRecord> rules, Dictionary<string, FlowRecord> flows,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            foreach (FlowRecord flow in package.flows)
            {
                if (flow == null) { Add(issues, "content_record_null", "flow", "A Flow record cannot be null."); continue; }
                if (flow.kind != "scenario" && flow.kind != "subflow")
                    Add(issues, "flow_kind_invalid", flow.id, "Flow kind must be scenario or subflow.");
                if (flow.steps == null || flow.steps.Count < 1 || flow.steps.Count > MaximumExpandedSteps)
                { Add(issues, "flow_step_limit", flow.id, "A Flow must have one to sixty-four Steps."); continue; }
                if (flow.transitions != null && flow.transitions.Count > 256 || flow.choices != null && flow.choices.Count > 512 ||
                    flow.bindings != null && flow.bindings.Count > 1024 || flow.exitMappings != null && flow.exitMappings.Count > 256)
                    Add(issues, "flow_child_limit", flow.id, "The Flow exceeds a bounded child-record count.");
                var steps = new Dictionary<string, StepRecord>(StringComparer.Ordinal);
                foreach (StepRecord step in flow.steps)
                {
                    if (step == null || !IsIdentifier(step.id) || !AddUnique(steps, step.id, step))
                    { Add(issues, "flow_step_duplicate", flow.id, "Step IDs must be valid and unique within their Flow."); continue; }
                    if (!IsJsonObject(step.dataJson)) Add(issues, "step_payload_invalid", flow.id + "/" + step.id, "Step data must be a JSON object.");
                    bool hasDialogue = step.dialogueRef != null;
                    bool hasAction = step.actionRef != null;
                    bool hasSubflow = step.calledFlowRef != null;
                    if (step.kind == "dialogue" || step.kind == "ask")
                    {
                        if (!hasDialogue || hasAction || hasSubflow)
                            Add(issues, "step_reference_kind_mismatch", flow.id + "/" + step.id, "Dialogue and Ask Steps require exactly one Dialogue reference.");
                        else RequireReference(step.dialogueRef, package.storeId, dialogues, item => item.semanticHash,
                            "dialogue", issues, flow.id + "/" + step.id);
                    }
                    else if (step.kind == "action" || step.kind == "host_effect")
                    {
                        if (!hasAction || hasDialogue || hasSubflow)
                            Add(issues, "step_reference_kind_mismatch", flow.id + "/" + step.id, "Action and host_effect Steps require exactly one Action reference.");
                        else RequireReference(step.actionRef, package.storeId, actions, item => item.semanticHash,
                            "action", issues, flow.id + "/" + step.id);
                    }
                    else if (step.kind == "subflow")
                    {
                        if (!hasSubflow || hasAction || hasDialogue)
                            Add(issues, "step_reference_kind_mismatch", flow.id + "/" + step.id, "Subflow Steps require exactly one called Flow reference.");
                        else RequireReference(step.calledFlowRef, package.storeId, flows, item => item.semanticHash,
                            "flow", issues, flow.id + "/" + step.id);
                    }
                    else if ((step.kind != "wait" && step.kind != "branch" && step.kind != "end") || hasDialogue || hasAction || hasSubflow)
                        Add(issues, "step_kind_invalid", flow.id + "/" + step.id, "The Step kind or its typed reference is invalid.");
                }
                if (!steps.ContainsKey(flow.entryStepId))
                    Add(issues, "flow_entry_missing", flow.id, "The exact entry Step does not exist.");

                var transitions = new Dictionary<string, TransitionRecord>(StringComparer.Ordinal);
                foreach (TransitionRecord transition in flow.transitions ?? new List<TransitionRecord>())
                {
                    if (transition == null || !IsIdentifier(transition.id) || !AddUnique(transitions, transition.id, transition))
                    { Add(issues, "transition_duplicate", flow.id, "Transition IDs must be valid and unique."); continue; }
                    if (!steps.ContainsKey(transition.fromStepId) || !steps.ContainsKey(transition.toStepId))
                        Add(issues, "transition_target_missing", flow.id + "/" + transition.id, "A Transition must stay inside its exact Flow revision.");
                    if (transition.ruleRef != null)
                        RequireReference(transition.ruleRef, package.storeId, rules, item => item.semanticHash,
                            "rule", issues, flow.id + "/" + transition.id);
                }
                foreach (ChoiceRecord choice in flow.choices ?? new List<ChoiceRecord>())
                {
                    if (choice == null || !steps.ContainsKey(choice.stepId) || !transitions.ContainsKey(choice.transitionId))
                    { Add(issues, "choice_target_missing", flow.id, "A Choice must name an exact Step and Transition in its Flow."); continue; }
                    RequireReference(choice.labelTextRef, package.storeId, texts, item => item.semanticHash,
                        "text", issues, flow.id + "/" + choice.id);
                    if (choice.availabilityRuleRef != null)
                        RequireReference(choice.availabilityRuleRef, package.storeId, rules, item => item.semanticHash,
                            "rule", issues, flow.id + "/" + choice.id);
                    if (choice.lockedHintTextRef != null)
                        RequireReference(choice.lockedHintTextRef, package.storeId, texts, item => item.semanticHash,
                            "text", issues, flow.id + "/" + choice.id);
                }
                ValidateScenarioHostEffectSteps(package, flow, steps, actions, issues);
                ValidateFlowContracts(package, flow, steps, transitions, actions, flows, vocabulary, issues);
            }
        }

        private static void ValidateScenarioHostEffectSteps(ContentPackage package, FlowRecord flow,
            Dictionary<string, StepRecord> steps, Dictionary<string, ActionRecord> actions, List<ContentDiagnostic> issues)
        {
            foreach (StepRecord step in flow.steps.Where(item => item != null && item.kind == "host_effect"))
            {
                JObject data = ParseObject(step.dataJson, flow.id + "/" + step.id + "/data", issues);
                if (data == null) continue;
                CheckFields(data, flow.id + "/" + step.id + "/data", issues, "schemaVersion", "requiresCompletedStepId");
                string prerequisiteId = String(data, "requiresCompletedStepId", string.Empty, flow.id + "/" + step.id, issues);
                int schemaVersion;
                if (!int.TryParse(data["schemaVersion"] == null ? string.Empty : data["schemaVersion"].ToString(),
                    NumberStyles.None, CultureInfo.InvariantCulture, out schemaVersion) || schemaVersion != 1 ||
                    !IsIdentifier(prerequisiteId) || prerequisiteId == step.id)
                {
                    Add(issues, "scenario_host_effect_data_invalid", flow.id + "/" + step.id,
                        "host_effect Steps require strict schemaVersion 1 data and a different required completed Step.");
                    continue;
                }
                ActionRecord action = ResolveReference(step.actionRef, package.storeId, actions, item => item.semanticHash,
                    "action", issues, flow.id + "/" + step.id);
                if (action == null || action.implementationKind != "host_effect")
                    Add(issues, "scenario_host_effect_action_invalid", flow.id + "/" + step.id,
                        "A host_effect Step must pin an Action compiled as a host effect.");
                StepRecord prerequisite;
                if (!steps.TryGetValue(prerequisiteId, out prerequisite) || prerequisite.kind != "dialogue" ||
                    prerequisite.dialogueRef == null ||
                    !Dominates(flow, prerequisiteId, step.id, steps))
                    Add(issues, "scenario_host_effect_prerequisite_invalid", flow.id + "/" + step.id,
                        "The required completed Step must be an earlier Dialogue Step that dominates the effect.");
            }
        }

        private static void ValidateTopics(ContentPackage package, Dictionary<string, TextRecord> texts,
            Dictionary<string, FlowRecord> flows, List<ContentDiagnostic> issues)
        {
            foreach (TopicRecord topic in package.topics)
            {
                if (topic == null) { Add(issues, "content_record_null", "topic", "A Topic record cannot be null."); continue; }
                if (topic.defaultReplayPolicy != TopicReplayPolicy.Repeatable && topic.defaultReplayPolicy != TopicReplayPolicy.Once)
                    Add(issues, "topic_replay_policy_invalid", topic.id, "The Topic replay policy is unsupported.");
                if (!Finite(topic.cooldownSeconds) || topic.cooldownSeconds < 0 || topic.cooldownSeconds > 31536000)
                    Add(issues, "topic_cooldown_invalid", topic.id, "The Topic cooldown is outside its bounded range.");
                RequireReference(topic.titleTextRef, package.storeId, texts, item => item.semanticHash, "text", issues, topic.id + "/title");
                RequireReference(topic.promptTextRef, package.storeId, texts, item => item.semanticHash, "text", issues, topic.id + "/prompt");
                var entryIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (TopicEntryRecord entry in topic.entries ?? new List<TopicEntryRecord>())
                {
                    if (entry == null || !IsIdentifier(entry.id) || !entryIds.Add(entry.id))
                    { Add(issues, "topic_entry_invalid", topic.id, "Topic entry IDs must be valid and unique."); continue; }
                    FlowRecord target = ResolveReference(entry.flowRef, package.storeId, flows, item => item.semanticHash,
                        "flow", issues, topic.id + "/" + entry.id);
                    if (target != null && !string.IsNullOrEmpty(entry.consumptionNodeId) &&
                        !target.steps.Any(step => step != null && step.id == entry.consumptionNodeId))
                        Add(issues, "topic_consumption_node_missing", topic.id + "/" + entry.id, "The consumption checkpoint is outside its pinned Flow revision.");
                    if (!Finite(entry.baseWeight) || entry.baseWeight < 0 || entry.baseWeight > TopicDefinitionValidator.MaximumWeight)
                        Add(issues, "topic_weight_invalid", topic.id + "/" + entry.id, "The Topic entry weight is outside its supported range.");
                    ValidateInlineCondition(entry.conditionJson, topic.id + "/" + entry.id + "/condition", issues);
                    ValidateModifiers(entry.modifiersJson, topic.id + "/" + entry.id + "/modifiers", issues);
                    ValidateChannels(entry.channelsJson, topic.id + "/" + entry.id + "/channels", issues);
                }
            }
        }

        private static void ValidateFlowContracts(ContentPackage package, FlowRecord flow,
            Dictionary<string, StepRecord> steps, Dictionary<string, TransitionRecord> transitions,
            Dictionary<string, ActionRecord> actions, Dictionary<string, FlowRecord> flows,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            var parameters = new Dictionary<string, FlowParameterRecord>(StringComparer.Ordinal);
            var inputCounts = 0;
            var outputCounts = 0;
            foreach (FlowParameterRecord parameter in flow.parameters ?? new List<FlowParameterRecord>())
            {
                if (parameter == null || !IsIdentifier(parameter.name) ||
                    (parameter.direction != "input" && parameter.direction != "output") || !IsValueType(parameter.valueType) ||
                    !AddUnique(parameters, parameter.name, parameter))
                { Add(issues, "flow_parameter_invalid", flow.id, "Parameters need unique names and supported directions/types."); continue; }
                if (parameter.direction == "input") inputCounts++; else outputCounts++;
                ValidateDefault(parameter.valueType, parameter.defaultValueJson, flow.id + "/" + parameter.name, vocabulary, issues);
            }
            if (inputCounts > 16 || outputCounts > 16)
                Add(issues, "flow_parameter_limit", flow.id, "A Flow supports at most sixteen inputs and sixteen outputs.");
            var variables = new Dictionary<string, FlowVariableRecord>(StringComparer.Ordinal);
            foreach (FlowVariableRecord variable in flow.variables ?? new List<FlowVariableRecord>())
            {
                if (variable == null || !IsIdentifier(variable.name) || !IsValueType(variable.valueType) ||
                    parameters.ContainsKey(variable.name) || !AddUnique(variables, variable.name, variable))
                { Add(issues, "flow_name_collision", flow.id, "A local variable must have a unique name distinct from every Flow parameter."); continue; }
                ValidateDefault(variable.valueType, variable.defaultValueJson, flow.id + "/" + variable.name, vocabulary, issues);
            }
            if (variables.Count > 16) Add(issues, "flow_variable_limit", flow.id, "A Flow supports at most sixteen local variables.");

            var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (StepBindingRecord binding in flow.bindings ?? new List<StepBindingRecord>())
            {
                if (binding == null || !steps.ContainsKey(binding.stepId) || !IsIdentifier(binding.destinationName) ||
                    !bindingKeys.Add(binding.stepId + "\0" + binding.destinationName))
                { Add(issues, "step_binding_invalid", flow.id, "Step bindings must name a unique destination on an existing Step."); continue; }
                string expectedType = ResolveBindingDestination(package, steps[binding.stepId], binding.destinationName,
                    actions, flows, vocabulary, issues, flow.id);
                string actualType = ResolveBindingSource(package.storeId, flow, steps, binding, actions, vocabulary, issues);
                bool compatibleLiteralEnum = expectedType == "enum" && actualType == "text" && binding.sourceKind == "literal" &&
                    LiteralEnumAllowed(binding.literalJson, vocabulary);
                if (expectedType != null && actualType != null && expectedType != actualType && !compatibleLiteralEnum)
                    Add(issues, "binding_type_mismatch", flow.id + "/" + binding.stepId, "The binding source and destination types differ.");
            }

            foreach (StepRecord step in flow.steps.Where(item => item != null && item.kind == "subflow"))
            {
                FlowRecord callee = ResolveReference(step.calledFlowRef, package.storeId, flows, item => item.semanticHash,
                    "flow", issues, flow.id + "/" + step.id);
                if (callee == null) continue;
                if (callee.kind != "subflow")
                { Add(issues, "subflow_target_kind_invalid", flow.id + "/" + step.id, "A Subflow Step must call a Flow of kind subflow."); continue; }
                var supplied = new HashSet<string>((flow.bindings ?? new List<StepBindingRecord>()).Where(item => item != null && item.stepId == step.id)
                    .Select(item => item.destinationName), StringComparer.Ordinal);
                foreach (FlowParameterRecord input in (callee.parameters ?? new List<FlowParameterRecord>()).Where(item => item != null && item.direction == "input" && item.required && string.IsNullOrEmpty(item.defaultValueJson)))
                    if (!supplied.Contains(input.name)) Add(issues, "subflow_required_input_missing", flow.id + "/" + step.id,
                        "Required Subflow input is not bound: " + input.name);
            }

            foreach (StepRecord step in flow.steps.Where(item => item != null && item.kind == "action"))
            {
                ActionRecord action = ResolveReference(step.actionRef, package.storeId, actions,
                    item => item.semanticHash, "action", issues, flow.id + "/" + step.id);
                if (action == null) continue;
                Dictionary<string, Tuple<string, bool>> inputs = ReadActionContractMembers(action, "inputs", flow.id + "/" + step.id, vocabulary, issues);
                if (inputs == null) continue;
                var supplied = new HashSet<string>((flow.bindings ?? new List<StepBindingRecord>())
                    .Where(item => item != null && item.stepId == step.id).Select(item => item.destinationName), StringComparer.Ordinal);
                foreach (KeyValuePair<string, Tuple<string, bool>> input in inputs)
                    if (input.Value.Item2 && !supplied.Contains(input.Key))
                        Add(issues, "action_required_input_missing", flow.id + "/" + step.id,
                            "Required Action input is not bound: " + input.Key);
            }

            ValidateFlowExits(package, flow, steps, transitions, flows, variables, parameters, issues);
        }

        private static void ValidateFlowExits(ContentPackage package, FlowRecord flow, Dictionary<string, StepRecord> steps,
            Dictionary<string, TransitionRecord> transitions, Dictionary<string, FlowRecord> flows,
            Dictionary<string, FlowVariableRecord> variables, Dictionary<string, FlowParameterRecord> parameters,
            List<ContentDiagnostic> issues)
        {
            var exits = new Dictionary<string, Dictionary<string, Tuple<string, bool>>>(StringComparer.Ordinal);
            foreach (FlowExitRecord exit in flow.exits ?? new List<FlowExitRecord>())
            {
                if (exit == null || !IsIdentifier(exit.name) || exits.ContainsKey(exit.name))
                { Add(issues, "flow_exit_invalid", flow.id, "Exit names must be valid and unique."); continue; }
                Dictionary<string, Tuple<string, bool>> outputs = ParseExitOutputs(exit.contractJson, flow.id + "/" + exit.name, issues);
                exits.Add(exit.name, outputs);
                foreach (KeyValuePair<string, Tuple<string, bool>> output in outputs)
                {
                    FlowParameterRecord declared;
                    if (!parameters.TryGetValue(output.Key, out declared) || declared.direction != "output" || declared.valueType != output.Value.Item1)
                        Add(issues, "flow_exit_output_undeclared", flow.id + "/" + exit.name,
                            "Exit output must match a declared output parameter: " + output.Key);
                }
                ValidateExitOutputSources(flow, exit.contractJson, variables, parameters,
                    flow.id + "/" + exit.name, issues);
            }
            if (flow.kind == "subflow" && exits.Count == 0)
                Add(issues, "subflow_exit_missing", flow.id, "A published Subflow needs at least one named exit.");

            var mappingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExitMappingRecord mapping in flow.exitMappings ?? new List<ExitMappingRecord>())
            {
                if (mapping == null || !steps.ContainsKey(mapping.callStepId) || steps[mapping.callStepId].kind != "subflow" ||
                    !mappingKeys.Add(mapping.callStepId + "\0" + mapping.exitName))
                { Add(issues, "subflow_exit_call_missing", flow.id, "An exit mapping must belong to a Subflow call Step."); continue; }
                FlowRecord callee = ResolveReference(mapping.calleeFlowRef, package.storeId, flows, item => item.semanticHash,
                    "flow", issues, flow.id + "/" + mapping.callStepId);
                if (callee == null || callee.id != steps[mapping.callStepId].calledFlowRef.id ||
                    callee.revision != steps[mapping.callStepId].calledFlowRef.revision)
                { Add(issues, "subflow_exit_target_mismatch", flow.id + "/" + mapping.callStepId, "The exit mapping must pin the call Step's exact Subflow revision."); continue; }
                Dictionary<string, Tuple<string, bool>> outputs = ReadCalleeExitOutputs(callee, mapping.exitName,
                    flow.id + "/" + mapping.callStepId + "/" + mapping.exitName, issues);
                if (outputs == null) continue;
                TransitionRecord transition;
                if (!transitions.TryGetValue(mapping.transitionId, out transition) || transition.fromStepId != mapping.callStepId ||
                    (transition.purpose != "subflow_exit" && transition.purpose != mapping.exitName))
                    Add(issues, "subflow_exit_transition_invalid", flow.id + "/" + mapping.callStepId, "The named exit must use a caller transition from that call Step.");
                Dictionary<string, string> mappingTypes = ParseOutputMappings(mapping.outputMappingsJson, flow.id + "/" + mapping.callStepId, variables, outputs, issues);
                foreach (KeyValuePair<string, Tuple<string, bool>> output in outputs)
                {
                    string target;
                    if (output.Value.Item2 && (!mappingTypes.TryGetValue(output.Key, out target) || target != output.Value.Item1))
                        Add(issues, "subflow_exit_mapping_incomplete", flow.id + "/" + mapping.callStepId,
                            "Required Subflow exit output is missing or has the wrong type: " + output.Key);
                }
            }

            var mappedExits = new HashSet<string>((flow.exitMappings ?? new List<ExitMappingRecord>())
                .Where(item => item != null).Select(item => item.callStepId + "\0" + item.exitName), StringComparer.Ordinal);
            foreach (StepRecord call in flow.steps.Where(item => item != null && item.kind == "subflow"))
            {
                FlowRecord callee = ResolveReference(call.calledFlowRef, package.storeId, flows, item => item.semanticHash,
                    "flow", issues, flow.id + "/" + call.id);
                if (callee == null || callee.exits == null) continue;
                foreach (FlowExitRecord exit in callee.exits)
                    if (exit != null && !mappedExits.Contains(call.id + "\0" + exit.name))
                        Add(issues, "subflow_exit_mapping_missing", flow.id + "/" + call.id + "/" + exit.name,
                            "Every named Subflow exit must map to a caller transition.");
            }
        }

        private static Dictionary<string, Tuple<string, bool>> ParseExitOutputs(string json, string path, List<ContentDiagnostic> issues)
        {
            var result = new Dictionary<string, Tuple<string, bool>>(StringComparer.Ordinal);
            JsonObject root = ParseObject(json, path, issues);
            if (root == null) return result;
            CheckFields(root, path, issues, "outputs");
            JsonValue outputsValue = Child(root, "outputs");
            JsonArray outputs = outputsValue as JsonArray;
            if (outputs == null) { Add(issues, "flow_exit_outputs_missing", path, "An exit contract needs an outputs array."); return result; }
            foreach (JsonValue value in outputs)
            {
                JsonObject item = value as JsonObject;
                if (item == null) { Add(issues, "flow_exit_output_invalid", path, "Exit outputs must be objects."); continue; }
                CheckFields(item, path + "/outputs[]", issues, "name", "type", "required", "sourceName");
                string name = String(item, "name", string.Empty);
                string type = String(item, "type", string.Empty);
                bool required = Boolean(item, "required", true);
                if (!IsIdentifier(name) || !IsValueType(type) || result.ContainsKey(name))
                    Add(issues, "flow_exit_output_invalid", path, "Exit output names/types must be valid and unique.");
                else result.Add(name, Tuple.Create(type, required));
            }
            return result;
        }

        private static void ValidateExitOutputSources(FlowRecord flow, string json,
            Dictionary<string, FlowVariableRecord> variables, Dictionary<string, FlowParameterRecord> parameters,
            string path, List<ContentDiagnostic> issues)
        {
            JsonObject root = ParseObject(json, path, issues);
            JsonArray outputs = root == null ? null : Child(root, "outputs") as JsonArray;
            if (outputs == null) return;
            foreach (JsonObject output in outputs.OfType<JsonObject>())
            {
                string name = String(output, "name", string.Empty);
                string type = String(output, "type", string.Empty);
                string sourceName = String(output, "sourceName", string.Empty);
                FlowVariableRecord local;
                FlowParameterRecord input;
                string sourceType = variables.TryGetValue(sourceName, out local) ? local.valueType :
                    parameters.TryGetValue(sourceName, out input) && input.direction == "input" ? input.valueType : string.Empty;
                if (string.IsNullOrEmpty(sourceName) || sourceType != type)
                    Add(issues, "flow_exit_output_source_invalid", path + "/" + name,
                        "A named exit output must read a same-typed declared local or Flow input.");
            }
        }

        private static Dictionary<string, Tuple<string, bool>> ReadCalleeExitOutputs(FlowRecord callee, string exitName,
            string path, List<ContentDiagnostic> issues)
        {
            if (callee == null || callee.exits == null) return null;
            FlowExitRecord exit = callee.exits.FirstOrDefault(item => item != null && item.name == exitName);
            if (exit == null)
            { Add(issues, "subflow_exit_not_declared", path, "The called Subflow does not declare this exit."); return null; }
            return ParseExitOutputs(exit.contractJson, path, issues);
        }

        private static Dictionary<string, string> ParseOutputMappings(string json, string path,
            Dictionary<string, FlowVariableRecord> variables, Dictionary<string, Tuple<string, bool>> outputs,
            List<ContentDiagnostic> issues)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            JsonValue parsed;
            try { parsed = ContentJson.Parse(json ?? "[]"); }
            catch (Exception error) { Add(issues, "subflow_output_mappings_invalid", path, error.Message); return result; }
            JsonArray array = parsed as JsonArray;
            if (array == null) { Add(issues, "subflow_output_mappings_invalid", path, "Output mappings must be a JSON array."); return result; }
            var destinations = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonValue value in array)
            {
                JsonObject item = value as JsonObject;
                if (item == null) { Add(issues, "subflow_output_mappings_invalid", path, "An output mapping must be an object."); continue; }
                CheckFields(item, path + "/output[]", issues, "sourceName", "destinationName");
                string sourceName = String(item, "sourceName", string.Empty);
                string destinationName = String(item, "destinationName", string.Empty);
                Tuple<string, bool> output;
                FlowVariableRecord variable;
                if (!outputs.TryGetValue(sourceName, out output) || !variables.TryGetValue(destinationName, out variable) ||
                    variable.valueType != output.Item1 || !variable.mutable || !destinations.Add(destinationName) || result.ContainsKey(sourceName))
                    Add(issues, "subflow_output_mapping_invalid", path, "Output mapping must connect a declared output to a same-typed caller local.");
                else result.Add(sourceName, variable.valueType);
            }
            return result;
        }

        private static string ResolveBindingDestination(ContentPackage package, StepRecord step, string name,
            Dictionary<string, ActionRecord> actions, Dictionary<string, FlowRecord> flows,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues, string flowPath)
        {
            if (step.kind == "subflow")
            {
                FlowRecord callee = ResolveReference(step.calledFlowRef, package.storeId, flows, item => item.semanticHash,
                    "flow", issues, flowPath + "/" + step.id);
                if (callee != null && callee.kind != "subflow")
                { Add(issues, "subflow_target_kind_invalid", flowPath + "/" + step.id, "A Subflow Step must call a Flow of kind subflow."); return null; }
                FlowParameterRecord parameter = callee == null || callee.parameters == null ? null :
                    callee.parameters.FirstOrDefault(item => item != null && item.name == name && item.direction == "input");
                if (parameter == null) { Add(issues, "binding_destination_missing", flowPath + "/" + step.id, "The callee has no matching input parameter: " + name); return null; }
                return parameter.valueType;
            }
            if (step.kind == "action" || step.kind == "host_effect")
            {
                ActionRecord action = ResolveReference(step.actionRef, package.storeId, actions,
                    item => item.semanticHash, "action", issues, flowPath + "/" + step.id);
                if (action == null) return null;
                Dictionary<string, Tuple<string, bool>> inputs = ReadActionContractMembers(action, "inputs",
                    flowPath + "/" + step.id, vocabulary, issues);
                Tuple<string, bool> input;
                if (inputs == null || !inputs.TryGetValue(name, out input))
                { Add(issues, "binding_destination_missing", flowPath + "/" + step.id, "The Action contract has no matching typed input: " + name); return null; }
                return input.Item1;
            }
            Add(issues, "binding_target_invalid", flowPath + "/" + step.id, "A binding destination must belong to a typed Subflow call.");
            return null;
        }

        private static string ResolveBindingSource(string storeId, FlowRecord flow, Dictionary<string, StepRecord> steps,
            StepBindingRecord binding, Dictionary<string, ActionRecord> actions,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            if (binding.sourceKind == "local")
            {
                FlowVariableRecord variable = (flow.variables ?? new List<FlowVariableRecord>()).FirstOrDefault(item => item != null && item.name == binding.sourceName);
                if (variable == null) { Add(issues, "binding_source_missing", flow.id + "/" + binding.stepId, "Local source is undeclared."); return null; }
                return variable.valueType;
            }
            if (binding.sourceKind == "input")
            {
                FlowParameterRecord parameter = (flow.parameters ?? new List<FlowParameterRecord>()).FirstOrDefault(item => item != null && item.name == binding.sourceName && item.direction == "input");
                if (parameter == null) { Add(issues, "binding_source_missing", flow.id + "/" + binding.stepId, "Input source is undeclared."); return null; }
                return parameter.valueType;
            }
            if (binding.sourceKind == "literal")
                return InferJsonType(binding.literalJson, flow.id + "/" + binding.stepId, issues);
            if (binding.sourceKind == "host_fact")
            {
                if (!vocabulary.IsContextFactAllowed(binding.hostFactId))
                { Add(issues, "binding_host_fact_unavailable", binding.hostFactId, "The host did not declare this fact."); return null; }
                return "bool";
            }
            if (binding.sourceKind == "result")
            {
                StepRecord resultStep;
                if (!steps.TryGetValue(binding.resultStepId, out resultStep) || resultStep.kind != "action" ||
                    resultStep.actionRef == null || !Dominates(flow, binding.resultStepId, binding.stepId, steps))
                { Add(issues, "binding_result_not_earlier", flow.id + "/" + binding.stepId, "A result binding must name an Action Step that dominates this Step in the caller graph."); return null; }
                ActionRecord action = ResolveReference(resultStep.actionRef, storeId, actions,
                    item => item.semanticHash, "action", issues, flow.id + "/" + binding.resultStepId);
                if (action == null) return null;
                Dictionary<string, Tuple<string, bool>> results = ReadActionContractMembers(action, "results",
                    flow.id + "/" + binding.resultStepId, vocabulary, issues);
                Tuple<string, bool> result;
                if (results == null || !results.TryGetValue(binding.resultName, out result))
                { Add(issues, "binding_result_missing", flow.id + "/" + binding.resultStepId, "The Action contract does not declare this result: " + binding.resultName); return null; }
                return result.Item1;
            }
            Add(issues, "binding_source_kind_invalid", flow.id + "/" + binding.stepId, "The binding source kind is unsupported.");
            return null;
        }

        private static bool LiteralEnumAllowed(string json, ContentHostVocabulary vocabulary)
        {
            if (vocabulary == null || string.IsNullOrEmpty(json)) return false;
            try
            {
                JsonValue value = ContentJson.Parse(json);
                return value.Type == JTokenType.String && vocabulary.IsEnumValueAllowed((string)value);
            }
            catch { return false; }
        }

        private static void ValidateActionArguments(JsonObject contract, string path, List<ContentDiagnostic> issues)
        {
            JsonValue raw = Child(contract, "arguments");
            if (raw == null || IsNull(raw)) return;
            JsonArray arguments = raw as JsonArray;
            if (arguments == null || arguments.Count > 32)
            { Add(issues, "action_arguments_invalid", path, "Legacy Action arguments must be an array with at most thirty-two entries."); return; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonValue rawArgument in arguments)
            {
                JsonObject argument = rawArgument as JsonObject;
                if (argument == null) { Add(issues, "action_argument_invalid", path, "An Action argument must be an object."); continue; }
                CheckFields(argument, path + "/arguments[]", issues, "name", "value");
                string name = String(argument, "name", string.Empty, path, issues);
                JsonValue value = Child(argument, "value");
                if (!IsIdentifier(name) || !names.Add(name) || value == null || IsNull(value) || value.ToString().Length > 2048)
                    Add(issues, "action_argument_invalid", path, "Action argument names must be unique and values bounded/non-null.");
            }
        }

        private static Dictionary<string, Tuple<string, bool>> ReadActionContractMembers(ActionRecord action, string memberName,
            string path, ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            JsonObject contract = ParseObject(action == null ? null : action.contractJson, path, issues);
            if (contract == null) return null;
            JsonValue raw = Child(contract, memberName);
            if (raw == null || IsNull(raw)) return new Dictionary<string, Tuple<string, bool>>(StringComparer.Ordinal);
            JsonArray members = raw as JsonArray;
            if (members == null || members.Count > 32)
            { Add(issues, "action_contract_members_invalid", path, "Action contract members must be a bounded array."); return null; }
            var result = new Dictionary<string, Tuple<string, bool>>(StringComparer.Ordinal);
            foreach (JsonValue rawMember in members)
            {
                JsonObject member = rawMember as JsonObject;
                if (member == null) { Add(issues, "action_contract_member_invalid", path, "An Action contract member must be an object."); continue; }
                CheckFields(member, path + "/" + memberName + "[]", issues, "name", "type", "required", "defaultValue");
                string name = String(member, "name", string.Empty, path, issues);
                string type = String(member, "type", string.Empty, path, issues);
                bool required = Boolean(member, "required", true);
                if (!IsIdentifier(name) || !IsValueType(type) || result.ContainsKey(name))
                    Add(issues, "action_contract_member_invalid", path, "Action contract member names and types must be valid and unique.");
                else
                {
                    result.Add(name, Tuple.Create(type, required));
                    JsonValue defaultValue = Child(member, "defaultValue");
                    if (defaultValue != null && !IsNull(defaultValue))
                    {
                        result[name] = Tuple.Create(type, false);
                        ValidateDefault(type, defaultValue.ToString(), path + "/" + name, vocabulary, issues);
                    }
                }
            }
            return result;
        }

        private static bool Dominates(FlowRecord flow, string sourceStepId, string targetStepId,
            Dictionary<string, StepRecord> steps)
        {
            if (sourceStepId == targetStepId || !steps.ContainsKey(sourceStepId) || !steps.ContainsKey(targetStepId)) return false;
            var all = new HashSet<string>(steps.Keys, StringComparer.Ordinal);
            var predecessors = steps.Keys.ToDictionary(id => id, id => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (TransitionRecord transition in flow.transitions ?? new List<TransitionRecord>())
                if (transition != null && predecessors.ContainsKey(transition.toStepId) && steps.ContainsKey(transition.fromStepId))
                    predecessors[transition.toStepId].Add(transition.fromStepId);
            var dominators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string id in steps.Keys)
                dominators[id] = id == flow.entryStepId ? new HashSet<string>(new[] { id }, StringComparer.Ordinal) : new HashSet<string>(all, StringComparer.Ordinal);
            bool changed;
            int passes = 0;
            do
            {
                changed = false;
                if (++passes > steps.Count + 1) return false;
                foreach (string id in steps.Keys)
                {
                    if (id == flow.entryStepId) continue;
                    HashSet<string> incoming = null;
                    foreach (string predecessor in predecessors[id])
                    {
                        if (incoming == null) incoming = new HashSet<string>(dominators[predecessor], StringComparer.Ordinal);
                        else incoming.IntersectWith(dominators[predecessor]);
                    }
                    if (incoming == null) incoming = new HashSet<string>(StringComparer.Ordinal);
                    incoming.Add(id);
                    if (!incoming.SetEquals(dominators[id])) { dominators[id] = incoming; changed = true; }
                }
            } while (changed);
            return dominators[targetStepId].Contains(sourceStepId);
        }

        private static void ValidateSubflowGraph(ContentPackage package, Dictionary<string, FlowRecord> flows, List<ContentDiagnostic> issues)
        {
            foreach (FlowRecord flow in package.flows)
            {
                int expanded;
                WalkFlow(flow, package.storeId, flows, new HashSet<string>(StringComparer.Ordinal), 1, out expanded, issues);
            }
        }

        private static void WalkFlow(FlowRecord flow, string storeId, Dictionary<string, FlowRecord> flows,
            HashSet<string> active, int depth, out int expanded, List<ContentDiagnostic> issues)
        {
            expanded = flow == null || flow.steps == null ? 0 : flow.steps.Count;
            if (flow == null) return;
            string key = Key(flow.id, flow.revision);
            if (active.Contains(key)) { Add(issues, "subflow_cycle", flow.id, "Recursive Subflow calls are not publishable."); expanded = 0; return; }
            if (depth > MaximumSubflowDepth) { Add(issues, "subflow_depth_exceeded", flow.id, "Subflow nesting exceeds eight levels."); expanded = 0; return; }
            active.Add(key);
            foreach (StepRecord step in flow.steps ?? new List<StepRecord>())
            {
                if (step == null || step.kind != "subflow" || step.calledFlowRef == null) continue;
                FlowRecord callee = ResolveReference(step.calledFlowRef, storeId, flows, item => item.semanticHash,
                    "flow", issues, flow.id + "/" + step.id);
                if (callee == null) continue;
                if (callee.kind != "subflow")
                { Add(issues, "subflow_target_kind_invalid", flow.id + "/" + step.id, "A Subflow Step must call a Flow of kind subflow."); continue; }
                int nested;
                WalkFlow(callee, storeId, flows, active, depth + 1, out nested, issues);
                expanded += nested;
                if (expanded > MaximumExpandedSteps)
                { Add(issues, "flow_expanded_step_limit", flow.id, "Expanded Flow closure exceeds sixty-four Steps."); break; }
            }
            active.Remove(key);
        }

        private static TopicCondition ExpandRuleRecord(ContentPackage package, RuleRecord rule,
            HashSet<string> active, int depth, ref int nodeCount, List<ContentDiagnostic> issues)
        {
            if (depth > MaximumRuleDepth) { Add(issues, "rule_depth_exceeded", rule.id, "Rule expansion exceeds eight levels."); return null; }
            string key = Key(rule.id, rule.revision);
            if (!active.Add(key)) { Add(issues, "rule_cycle", rule.id, "Shared Rule references contain a cycle."); return null; }
            JsonValue expression;
            try { expression = ContentJson.Parse(rule.expressionJson ?? "{}"); }
            catch (Exception exception) { active.Remove(key); Add(issues, "rule_json_invalid", rule.id, exception.Message); return null; }
            TopicCondition result = ExpandRuleNode(package, expression, active, depth, ref nodeCount, issues, rule.id);
            active.Remove(key);
            return result;
        }

        private static TopicCondition ExpandRuleNode(ContentPackage package, JsonValue value, HashSet<string> active,
            int depth, ref int nodeCount, List<ContentDiagnostic> issues, string path)
        {
            if (++nodeCount > MaximumRuleNodes)
            { Add(issues, "rule_node_limit", path, "Expanded Rule tree exceeds 256 nodes."); return null; }
            JsonObject item = value as JsonObject;
            if (item == null) { Add(issues, "rule_object_expected", path, "A Rule node must be a JSON object."); return null; }
            string kind = String(item, "kind", string.Empty);
            if (kind == "ref")
            {
                CheckFields(item, path, issues, "kind", "store", "id", "revision", "expectedHash");
                string store = String(item, "store", string.Empty);
                string id = String(item, "id", string.Empty);
                string revision = String(item, "revision", string.Empty);
                string expectedHash = String(item, "expectedHash", string.Empty);
                if (store != package.storeId || !IsIdentifier(id) || !IsSha256(revision))
                { Add(issues, "rule_cross_store_unavailable", path, "A Rule reference must be host-resolved to an exact local revision."); return null; }
                RuleRecord target = package.rules.FirstOrDefault(rule => rule != null && rule.id == id && rule.revision == revision);
                if (target == null)
                { Add(issues, "rule_missing_dependency", id, "The exact referenced Rule revision is missing."); return null; }
                if (!string.IsNullOrEmpty(expectedHash) && expectedHash != target.semanticHash)
                { Add(issues, "rule_hash_mismatch", id, "The referenced Rule hash does not match."); return null; }
                return ExpandRuleRecord(package, target, active, depth + 1, ref nodeCount, issues);
            }

            CheckFields(item, path, issues, "kind", "children", "statId", "operation", "value", "key", "expected", "startHour", "endHour", "objectKind");
            var result = new TopicCondition
            {
                kind = kind,
                statId = String(item, "statId", string.Empty),
                operation = String(item, "operation", string.Empty),
                value = Float(item, "value", 0f, path, issues),
                key = String(item, "key", string.Empty),
                expected = Boolean(item, "expected", true),
                startHour = Integer(item, "startHour", -1, path, issues),
                endHour = Integer(item, "endHour", -1, path, issues),
                objectKind = String(item, "objectKind", string.Empty)
            };
            JsonValue childrenValue = Child(item, "children");
            if (childrenValue != null && !IsNull(childrenValue))
            {
                JsonArray children = childrenValue as JsonArray;
                if (children == null) { Add(issues, "rule_children_invalid", path, "Rule children must be an array."); return result; }
                foreach (JsonValue child in children)
                {
                    TopicCondition expanded = ExpandRuleNode(package, child, active, depth + 1, ref nodeCount, issues, path + "/children");
                    result.children.Add(expanded);
                }
            }
            return result;
        }

        private static void ValidateInlineCondition(string json, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value;
            try { value = ContentJson.Parse(json ?? "{}"); }
            catch (Exception exception) { Add(issues, "topic_condition_json_invalid", path, exception.Message); return; }
            JsonObject root = value as JsonObject;
            if (root == null) { Add(issues, "topic_condition_object_invalid", path, "A Topic condition must be an object."); return; }
            if (!root.ContainsKey("kind"))
            {
                if (root.Count == 0) return;
                Add(issues, "topic_condition_kind_missing", path, "A non-empty Topic condition must declare its kind."); return;
            }
            CheckConditionFields(root, path, issues);
            TopicCondition condition = LegacyContentAdapter.ParseTopicCondition(value);
            foreach (string error in TopicDefinitionValidator.ValidateCondition(condition))
                Add(issues, "topic_condition_invalid", path, error);
        }

        private static void ValidateModifiers(string json, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value;
            try { value = ContentJson.Parse(json ?? "[]"); }
            catch (Exception exception) { Add(issues, "topic_modifiers_json_invalid", path, exception.Message); return; }
            JsonArray modifiers = value as JsonArray;
            if (modifiers == null) { Add(issues, "topic_modifiers_invalid", path, "Modifiers must be a JSON array."); return; }
            foreach (JsonValue modifier in modifiers)
            {
                JsonObject item = modifier as JsonObject;
                if (item == null) { Add(issues, "topic_modifier_invalid", path, "A modifier must be an object."); continue; }
                CheckFields(item, path + "/modifiers[]", issues, "condition", "amount");
                JsonValue conditionValue = Child(item, "condition");
                if (conditionValue == null || IsNull(conditionValue))
                { Add(issues, "topic_modifier_condition_missing", path, "A modifier requires a condition."); continue; }
                JsonObject conditionObject = conditionValue as JsonObject;
                if (conditionObject == null) { Add(issues, "topic_modifier_condition_invalid", path, "A modifier condition must be an object."); continue; }
                CheckConditionFields(conditionObject, path + "/modifiers[]/condition", issues);
                TopicCondition condition = LegacyContentAdapter.ParseTopicCondition(conditionValue);
                foreach (string error in TopicDefinitionValidator.ValidateCondition(condition))
                    Add(issues, "topic_modifier_condition_invalid", path, error);
                double amount = Double(item, "amount", double.NaN, path, issues);
                if (!Finite(amount) || Math.Abs(amount) > TopicDefinitionValidator.MaximumWeight)
                    Add(issues, "topic_modifier_amount_invalid", path, "Modifier amount is outside the supported finite range.");
            }
        }

        private static void CheckConditionFields(JsonObject item, string path, List<ContentDiagnostic> issues)
        {
            CheckFields(item, path, issues, "kind", "children", "statId", "operation", "value", "key", "expected",
                "startHour", "endHour", "objectKind");
            JsonArray children = Child(item, "children") as JsonArray;
            if (children == null) return;
            int index = 0;
            foreach (JsonValue child in children)
            {
                JsonObject childObject = child as JsonObject;
                if (childObject == null) Add(issues, "topic_condition_child_invalid", path, "Condition children must be objects.");
                else CheckConditionFields(childObject, path + "/children/" + index.ToString(CultureInfo.InvariantCulture), issues);
                index++;
            }
        }

        private static void ValidateChannels(string json, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value;
            try { value = ContentJson.Parse(json ?? "[]"); }
            catch (Exception exception) { Add(issues, "topic_channels_json_invalid", path, exception.Message); return; }
            JsonArray channels = value as JsonArray;
            if (channels == null) { Add(issues, "topic_channels_invalid", path, "Channels must be an array."); return; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonValue channelValue in channels)
            {
                string channel = StringValue(channelValue, path, issues);
                if ((channel != "direct" && channel != "phone") || !seen.Add(channel))
                    Add(issues, "topic_channel_unsupported", path, "Channels must be a unique subset of direct and phone.");
            }
        }

        private static void ValidateAnchors(ContentPackage package, Dictionary<string, TopicRecord> topics,
            Dictionary<string, FlowRecord> flows, Dictionary<string, DialogueRecord> dialogues,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            var anchors = new Dictionary<string, AnchorRecord>(StringComparer.Ordinal);
            foreach (AnchorRecord anchor in package.anchors)
            {
                if (anchor == null || !IsSha256(anchor.id) || !IsLanguage(anchor.language) ||
                    string.IsNullOrWhiteSpace(anchor.text) || anchor.text.Length > 2048 ||
                    anchor.id != anchor.textHash || ComputeHash(anchor.text) != anchor.textHash || !AddUnique(anchors, anchor.id, anchor))
                    Add(issues, "anchor_invalid", "anchor", "Anchor identity, language, text hash or uniqueness is invalid.");
            }
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AnchorLinkRecord link in package.anchorLinks)
            {
                if (link == null || !IsIdentifier(link.id) || !linkIds.Add(link.id) || !anchors.ContainsKey(link.anchorId) ||
                    !IsIdentifier(link.role) || !IsSha256(link.ownerRevision))
                { Add(issues, "anchor_link_invalid", "anchor_link", "Anchor link identity or anchor owner is invalid."); continue; }
                if (link.ownerKind == "topic")
                    RequireOwner(topics, link.ownerId, link.ownerRevision, link.id, "topic", issues);
                else if (link.ownerKind == "flow")
                    RequireOwner(flows, link.ownerId, link.ownerRevision, link.id, "flow", issues);
                else if (link.ownerKind == "dialogue")
                    RequireOwner(dialogues, link.ownerId, link.ownerRevision, link.id, "dialogue", issues);
                else if (link.ownerKind == "canonical")
                {
                    if (!vocabulary.IsCanonicalOwnerAllowed(link.ownerId))
                        Add(issues, "canonical_owner_unregistered", link.ownerId, "The anchor owner is not in the host canonical-owner vocabulary.");
                }
                else Add(issues, "anchor_owner_kind_invalid", link.id, "The anchor owner kind is unsupported.");
            }
        }

        private static void ValidateSemanticEmbeddings(ContentPackage package, List<ContentDiagnostic> issues)
        {
            var profiles = new Dictionary<string, EmbeddingProfileRecord>(StringComparer.Ordinal);
            foreach (EmbeddingProfileRecord profile in package.embeddingProfiles)
            {
                string code, message;
                if (!ContentSemanticValidation.ValidateProfile(profile, out code, out message) ||
                    profiles.ContainsKey(profile.id))
                {
                    Add(issues, "embedding_profile_invalid", profile == null ? "profile" : profile.id,
                        message ?? "The profile is invalid or its ID is duplicated.");
                    continue;
                }
                profiles.Add(profile.id, profile);
            }

            var anchors = new Dictionary<string, AnchorRecord>(StringComparer.Ordinal);
            foreach (AnchorRecord anchor in package.anchors)
                if (anchor != null && !anchors.ContainsKey(anchor.id)) anchors.Add(anchor.id, anchor);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EmbeddingRecord embedding in package.embeddings)
            {
                if (embedding == null)
                { Add(issues, "embedding_record_invalid", "embedding", "An embedding record cannot be null."); continue; }
                string key = embedding.anchorId + "\0" + embedding.profileId;
                AnchorRecord anchor;
                EmbeddingProfileRecord profile;
                if (!seen.Add(key))
                { Add(issues, "embedding_record_duplicate", key, "An Anchor/profile pair can have only one immutable vector."); continue; }
                if (!anchors.TryGetValue(embedding.anchorId, out anchor) || !profiles.TryGetValue(embedding.profileId, out profile))
                { Add(issues, "embedding_dependency_missing", key, "Every vector must reference an exact local Anchor and Embedding profile."); continue; }
                if (embedding.anchorId != anchor.textHash || !IsSha256(embedding.vectorHash) || embedding.vector == null ||
                    ContentSemanticValidation.Hash(embedding.vector) != embedding.vectorHash)
                { Add(issues, "embedding_source_hash_mismatch", key, "The vector source Anchor hash or vector byte hash does not match the immutable content."); continue; }
                string code, message;
                if (!ContentSemanticValidation.ValidateVector(embedding.vector, profile, out code, out message))
                    Add(issues, code, key, message);
            }
        }

        private static void RequireOwner<T>(Dictionary<string, T> values, string id, string revision, string path,
            string kind, List<ContentDiagnostic> issues)
        {
            if (!values.ContainsKey(Key(id, revision)))
                Add(issues, "anchor_owner_missing", path, "The exact linked " + kind + " revision is missing.");
        }

        private static void ValidateLegacySources(ContentPackage package, Dictionary<string, TopicRecord> topics,
            Dictionary<string, FlowRecord> flows, Dictionary<string, DialogueRecord> dialogues,
            Dictionary<string, ActionRecord> actions, Dictionary<string, FurnitureDefinitionRecord> furniture,
            Dictionary<string, ItemRecord> items, List<ContentDiagnostic> issues)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (LegacySourceRecord source in package.legacySources)
            {
                if (source == null || !IsIdentifier(source.kind) || !IsSafeRelativePath(source.relativePath) ||
                    !IsIdentifier(source.sourceId) || !IsSha256(source.sourceHash) || !IsIdentifier(source.contentId) ||
                    !IsSha256(source.payloadHash) || !IsSha256(source.contentRevision) || source.sourceBytes == null || source.sourceBytes.Length > 4 * 1024 * 1024 ||
                    source.sourceBytes.Length > 0 && ContentSemanticValidation.Hash(source.sourceBytes) != source.payloadHash ||
                    source.sourceBytes.Length == 0 && source.payloadHash != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" ||
                    !keys.Add(source.kind + "\0" + source.relativePath))
                { Add(issues, "legacy_source_mapping_invalid", "legacy_sources", "Legacy source mapping is malformed or duplicated."); continue; }
                if (source.kind == ContentSourceKinds.TopicDefinition)
                    RequireOwner(topics, source.contentId, source.contentRevision, source.relativePath, "topic", issues);
                else if (source.kind == ContentSourceKinds.GenericDialogue || source.kind == ContentSourceKinds.ScenarioDefinition)
                    RequireOwner(flows, source.contentId, source.contentRevision, source.relativePath, "flow", issues);
                else if (source.kind == ContentSourceKinds.DialogueDefinition)
                    RequireOwner(dialogues, source.contentId, source.contentRevision, source.relativePath, "dialogue", issues);
                else if (source.kind == ContentSourceKinds.ActionDefinition)
                    RequireOwner(actions, source.contentId, source.contentRevision, source.relativePath, "action", issues);
                else if (source.kind == ContentSourceKinds.ItemDefinition)
                    RequireOwner(items, source.contentId, source.contentRevision, source.relativePath, "item", issues);
                else if (source.kind == ContentSourceKinds.FurnitureDefinition)
                    RequireOwner(furniture, source.contentId, source.contentRevision, source.relativePath, "furniture", issues);
                else if (source.kind == ContentSourceKinds.FurnitureCatalogueSettings)
                {
                    if (source.sourceId != "furniture.catalogue" || source.contentId != "furniture.catalogue" || source.contentRevision != source.sourceHash)
                        Add(issues, "furniture_catalogue_settings_source_invalid", source.relativePath, "Furniture settings source identity and revision must be the fixed singleton and exact source hash.");
                }
                else if (LegacyContentAdapter.IsRawDocumentKind(source.kind))
                {
                    if (source.sourceBytes.Length == 0)
                        Add(issues, "legacy_source_payload_missing", source.relativePath, "A runtime document source must retain its exact UTF-8 document bytes.");
                }
                else Add(issues, "legacy_source_kind_invalid", source.relativePath, "Source kind is not supported by this validator version.");
                if (!IsJsonObject(source.metadataJson)) Add(issues, "legacy_source_metadata_invalid", source.relativePath, "Source-specific metadata must be a JSON object.");
            }
        }

        private static void Add(List<ContentDiagnostic> issues, string code, string path, string message)
        { issues.Add(new ContentDiagnostic(code, path, message)); }

        private static bool AddUnique<T>(Dictionary<string, T> dictionary, string key, T value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }

        private static string Key(string id, string revision)
        { return (id ?? string.Empty) + "\0" + (revision ?? string.Empty); }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value == "." || value == "..") return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':')) return false;
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f')) return false;
            return true;
        }

        private static bool IsLanguage(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2 || value.Length > 16) return false;
            string[] parts = value.Split('-');
            if (parts.Length == 0 || parts[0].Length < 2 || parts[0].Length > 3) return false;
            foreach (char character in parts[0]) if (character < 'a' || character > 'z') return false;
            for (int index = 1; index < parts.Length; index++)
            {
                if (parts[index].Length < 2 || parts[index].Length > 8) return false;
                foreach (char character in parts[index])
                    if (!(character >= 'a' && character <= 'z' || character >= '0' && character <= '9')) return false;
            }
            return true;
        }

        private static bool IsValueType(string value)
        { return value == "bool" || value == "int64" || value == "double" || value == "text" || value == "enum" || value == "entity"; }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool IsJsonObject(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16384) return false;
            try { return ContentJson.Parse(json) is JsonObject; }
            catch { return false; }
        }

        private static bool IsJsonArray(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16384) return false;
            try { return ContentJson.Parse(json) is JsonArray; }
            catch { return false; }
        }

        private static ContentRef ParseContentReference(JsonValue value)
        {
            JsonObject item = value as JsonObject;
            if (item == null) return null;
            return new ContentRef(TokenString(Child(item, "store")), TokenString(Child(item, "id")),
                TokenString(Child(item, "revision")), TokenString(Child(item, "expectedHash")));
        }

        private static string TokenString(JsonValue value)
        {
            if (IsNull(value)) return string.Empty;
            try { return (string)value; }
            catch { return value.ToString(); }
        }

        private static JsonObject ParseObject(string json, string path, List<ContentDiagnostic> issues)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16384)
            { Add(issues, "json_object_size_invalid", path, "JSON object is empty or exceeds its bound."); return null; }
            try
            {
                JsonObject result = ContentJson.Parse(json) as JsonObject;
                if (result == null) Add(issues, "json_object_expected", path, "A JSON object is required.");
                return result;
            }
            catch (Exception exception) { Add(issues, "json_object_invalid", path, exception.Message); return null; }
        }

        private static JsonValue Child(JsonObject item, string name)
        {
            JsonValue value;
            return item != null && item.TryGetValue(name, out value) ? value : null;
        }

        private static bool IsNull(JsonValue value)
        { return ContentJson.IsNull(value); }

        private static bool HasChild(JsonValue value, string name)
        { return value is JsonObject && ((JsonObject)value).ContainsKey(name); }

        private static void CheckFields(JsonObject value, string path, List<ContentDiagnostic> issues, params string[] allowed)
        {
            if (value == null) return;
            var permitted = new HashSet<string>(allowed ?? new string[0], StringComparer.Ordinal);
            foreach (string key in value.Properties().Select(property => property.Name))
                if (!permitted.Contains(key)) Add(issues, "json_field_unsupported", path + "/" + key, "The field is not supported by this content schema version.");
        }

        private static string String(JsonObject item, string name, string defaultValue = "")
        {
            JsonValue value = Child(item, name);
            if (value == null || IsNull(value)) return defaultValue;
            try { return (string)value; }
            catch { return defaultValue; }
        }

        private static string String(JsonObject item, string name, string defaultValue, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value = Child(item, name);
            if (value == null || IsNull(value)) return defaultValue;
            try { return (string)value; }
            catch { Add(issues, "json_string_expected", path + "/" + name, "A JSON string is required."); return defaultValue; }
        }

        private static string StringValue(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            if (value == null || IsNull(value))
            { Add(issues, "json_string_expected", path, "A JSON string is required."); return string.Empty; }
            try { return (string)value; }
            catch { Add(issues, "json_string_expected", path, "A JSON string is required."); return string.Empty; }
        }

        private static bool Boolean(JsonObject item, string name, bool defaultValue)
        {
            JsonValue value = Child(item, name);
            if (value == null || IsNull(value)) return defaultValue;
            try { return (bool)value; }
            catch { return defaultValue; }
        }

        private static double Double(JsonObject item, string name, double defaultValue, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value = Child(item, name);
            if (value == null || IsNull(value)) return defaultValue;
            double result;
            if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && Finite(result)) return result;
            Add(issues, "json_number_expected", path + "/" + name, "A finite JSON number is required.");
            return defaultValue;
        }

        private static float Float(JsonObject item, string name, float defaultValue, string path, List<ContentDiagnostic> issues)
        {
            double value = Double(item, name, defaultValue, path, issues);
            if (!Finite(value) || value < -float.MaxValue || value > float.MaxValue)
            { Add(issues, "json_float_out_of_range", path + "/" + name, "The number is outside the supported float range."); return defaultValue; }
            return (float)value;
        }

        private static int Integer(JsonObject item, string name, int defaultValue, string path, List<ContentDiagnostic> issues)
        {
            JsonValue value = Child(item, name);
            if (value == null || IsNull(value)) return defaultValue;
            int result;
            if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) return result;
            Add(issues, "json_integer_expected", path + "/" + name, "An integer JSON number is required.");
            return defaultValue;
        }

        private static void ValidateDefault(string valueType, string json, string path,
            ContentHostVocabulary vocabulary, List<ContentDiagnostic> issues)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (json.Length > 2048) { Add(issues, "flow_default_size_invalid", path, "A default value exceeds 2048 characters."); return; }
            string inferred = InferJsonType(json, path, issues);
            if (inferred == null) return;
            if (valueType == "entity")
            {
                JsonObject entity = null;
                try { entity = ContentJson.Parse(json) as JsonObject; } catch { }
                if (entity == null) { Add(issues, "flow_default_type_mismatch", path, "An entity default must be an object with kind and id."); return; }
                CheckFields(entity, path, issues, "kind", "id");
                if (!IsIdentifier(String(entity, "kind")) || !IsIdentifier(String(entity, "id")))
                    Add(issues, "flow_default_entity_invalid", path, "An entity default requires bounded kind and id values.");
                return;
            }
            if (valueType != inferred && !(valueType == "enum" && inferred == "text"))
                Add(issues, "flow_default_type_mismatch", path, "The default value does not match its declared type.");
            else if (valueType == "enum")
            {
                JsonValue parsed = ContentJson.Parse(json);
                string enumValue;
                try { enumValue = (string)parsed; } catch { enumValue = string.Empty; }
                if (vocabulary == null || !vocabulary.IsEnumValueAllowed(enumValue))
                    Add(issues, "flow_default_enum_unregistered", path, "The enum value is not registered by the host.");
            }
            if (json.Length > 2048) Add(issues, "flow_default_size_invalid", path, "A default value exceeds 2048 characters.");
        }

        private static string InferJsonType(string json, string path, List<ContentDiagnostic> issues)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 4096)
            { Add(issues, "binding_literal_invalid", path, "A binding literal is empty or too large."); return null; }
            JsonValue value;
            try { value = ContentJson.Parse(json); }
            catch (Exception exception) { Add(issues, "binding_literal_invalid", path, exception.Message); return null; }
            if (value is JsonObject)
            {
                JsonObject entity = (JsonObject)value;
                if (entity.Count == 2 && IsIdentifier(String(entity, "kind")) && IsIdentifier(String(entity, "id")) &&
                    entity.ContainsKey("kind") && entity.ContainsKey("id")) return "entity";
                Add(issues, "binding_literal_type_unsupported", path, "An entity literal must contain only bounded kind and id fields.");
                return null;
            }
            if (value is JsonArray || IsNull(value))
            { Add(issues, "binding_literal_type_unsupported", path, "Binding literals must be non-null typed values."); return null; }
            if (value.Type == JTokenType.String) return "text";
            if (value.Type == JTokenType.Boolean) return "bool";
            if (ContentJson.IsNumber(value))
            {
                long integer;
                if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return "int64";
                double number;
                if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && Finite(number)) return "double";
            }
            Add(issues, "binding_literal_type_unsupported", path, "The binding literal is not a supported finite scalar.");
            return null;
        }

        private static bool IsSafeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.IndexOf('\0') >= 0 || value.IndexOf(':') >= 0 ||
                value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal)) return false;
            string[] segments = value.Replace('\\', '/').Split('/');
            return segments.All(segment => segment.Length > 0 && segment != "." && segment != "..");
        }

        private static string ComputeHash(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes) builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void RequireReference<T>(ContentRef reference, string storeId, Dictionary<string, T> values,
            Func<T, string> getHash, string kind, List<ContentDiagnostic> issues, string path)
        { ResolveReference(reference, storeId, values, getHash, kind, issues, path); }

        private static T ResolveReference<T>(ContentRef reference, string storeId, Dictionary<string, T> values,
            Func<T, string> getHash, string kind, List<ContentDiagnostic> issues, string path)
        {
            if (reference == null || reference.store != storeId || !IsIdentifier(reference.id) || !IsSha256(reference.revision))
            { Add(issues, kind + "_reference_invalid", path, "A reference must pin an exact local ID and revision."); return default(T); }
            T result;
            if (!values.TryGetValue(Key(reference.id, reference.revision), out result))
            { Add(issues, kind + "_dependency_missing", path, "The exact referenced revision is absent from the package."); return default(T); }
            string semanticHash = getHash(result);
            if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != semanticHash)
            { Add(issues, kind + "_hash_mismatch", path, "The reference hash differs from the pinned record."); return default(T); }
            return result;
        }
    }
}

