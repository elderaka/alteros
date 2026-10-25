using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Content
{
    /// <summary>A host-resolved release whose content graph and validation vocabulary are pinned for one run.</summary>
    public sealed class ResolvedContentRelease
    {
        internal readonly ContentPackage package;
        internal readonly ContentRelease release;
        internal readonly ContentHostVocabulary vocabulary;

        internal ResolvedContentRelease(ContentPackage resolvedPackage, ContentRelease resolvedRelease,
            ContentHostVocabulary hostVocabulary)
        { package = resolvedPackage; release = resolvedRelease; vocabulary = hostVocabulary; }
    }

    /// <summary>Validated immutable startup state for a single Scenario occurrence.</summary>
    public sealed class ContentExecutionPlan
    {
        internal readonly ContentPackage package;
        internal readonly ContentRelease release;
        internal readonly ContentHostVocabulary vocabulary;
        internal readonly FlowRecord rootFlow;
        internal readonly Dictionary<string, FlowValue> initialInputs;
        internal readonly ScenarioRuntimeSnapshot context;
        internal readonly string displayLanguage;
        internal readonly string speechLanguage;

        internal ContentExecutionPlan(ContentPackage resolvedPackage, ContentRelease resolvedRelease,
            ContentHostVocabulary hostVocabulary, FlowRecord root, Dictionary<string, FlowValue> inputs,
            ScenarioRuntimeSnapshot runtimeContext, string display, string speech)
        {
            package = resolvedPackage; release = resolvedRelease; vocabulary = hostVocabulary;
            rootFlow = root; initialInputs = inputs; context = runtimeContext;
            displayLanguage = display; speechLanguage = speech;
        }

        public string ReleaseId { get { return release.releaseId; } }
        public string FlowId { get { return rootFlow.id; } }
        public string FlowRevision { get { return rootFlow.revision; } }
    }

    /// <summary>Prepares exact SQL release references for the existing ScenarioSession lifecycle.</summary>
    public static class ContentFlowAdapter
    {
        public static ContentResult<ResolvedContentRelease> Resolve(ContentPackage package, ContentRelease release,
            ContentHostVocabulary hostVocabulary)
        {
            if (package == null || release == null || hostVocabulary == null)
                return Failure<ResolvedContentRelease>("content_release_context_missing", "Package, release and host vocabulary are required.");
            ContentPackageValidationResult validation = ContentPackageValidator.Validate(package, hostVocabulary);
            if (!validation.isValid)
                return Failure<ResolvedContentRelease>(validation.diagnostics.FirstOrDefault(), "content_package_invalid");
            if (release.validatorVersion != ContentPackageValidator.ValidatorVersion || release.dependencies == null ||
                !package.releases.Any(item => item != null && item.releaseId == release.releaseId && item.manifestHash == release.manifestHash))
                return Failure<ResolvedContentRelease>("content_release_not_validated", "The release is not present as a validated immutable package release.");
            if (release.dependencies.Any(item => item == null || item.store != package.storeId))
                return Failure<ResolvedContentRelease>("content_release_cross_store", "Every runtime dependency must be host-resolved in this package store.");
            ContentResult<List<ContentReleaseDependency>> resolvedClosure = ContentReleaseCompiler.ResolveDependencies(package, hostVocabulary);
            if (!resolvedClosure.IsReady || !SameDependencies(resolvedClosure.value, release.dependencies))
                return Failure<ResolvedContentRelease>("content_release_closure_incomplete", "The release does not pin the exact validated package closure.");
            ContentPackage snapshot = CreateExecutionSnapshot(package);
            ContentRelease releaseSnapshot = snapshot.releases.FirstOrDefault(item => item != null &&
                item.releaseId == release.releaseId && item.manifestHash == release.manifestHash);
            if (releaseSnapshot == null)
                return Failure<ResolvedContentRelease>("content_release_snapshot_failed", "The validated release could not be pinned for runtime execution.");
            return ContentResult<ResolvedContentRelease>.Ready(new ResolvedContentRelease(snapshot, releaseSnapshot, hostVocabulary));
        }

        public static ContentResult<ContentExecutionPlan> Prepare(ResolvedContentRelease resolvedRelease, ContentRef flowReference,
            IDictionary<string, FlowValue> inputs, ScenarioRuntimeSnapshot context,
            string displayLanguage = "en", string speechLanguage = "ja")
        {
            if (resolvedRelease == null || resolvedRelease.package == null || resolvedRelease.release == null ||
                resolvedRelease.vocabulary == null || context == null)
                return Failure<ContentExecutionPlan>("content_flow_prepare_context_missing", "A resolved release and host runtime snapshot are required.");
            ContentPackage package = resolvedRelease.package;
            if (flowReference == null || flowReference.store != package.storeId || string.IsNullOrWhiteSpace(flowReference.id) ||
                !IsSha256(flowReference.revision))
                return Failure<ContentExecutionPlan>("content_flow_reference_invalid", "The root Flow must be pinned to an exact local revision.");
            FlowRecord flow = FindFlow(package, flowReference);
            if (flow == null || flow.kind != "scenario")
                return Failure<ContentExecutionPlan>("content_flow_missing", "The exact root Scenario Flow is missing or is not a Scenario.");
            if (!resolvedRelease.release.dependencies.Any(item => item.kind == "flow" && item.store == package.storeId &&
                item.id == flow.id && item.revision == flow.revision && item.expectedHash == flow.semanticHash))
                return Failure<ContentExecutionPlan>("content_flow_outside_release", "The root Flow is not in the exact published release closure.");
            if (context.objects == null || context.hostFacts == null)
                return Failure<ContentExecutionPlan>("content_flow_host_snapshot_incomplete", "Entity and host-fact snapshots must be explicit, including empty snapshots.");

            var resolvedInputs = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            var supplied = inputs ?? new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            var declarations = (flow.parameters ?? new List<FlowParameterRecord>())
                .Where(item => item != null && item.direction == "input").ToDictionary(item => item.name, item => item, StringComparer.Ordinal);
            foreach (string suppliedName in supplied.Keys)
                if (!declarations.ContainsKey(suppliedName))
                    return Failure<ContentExecutionPlan>("content_flow_input_undeclared", "The root Flow does not declare input: " + suppliedName);
            foreach (FlowParameterRecord parameter in declarations.Values)
            {
                FlowValue value;
                if (supplied.TryGetValue(parameter.name, out value))
                {
                    if (value == null || value.type != parameter.valueType || !value.IsAvailableIn(context))
                        return Failure<ContentExecutionPlan>("content_flow_input_type_invalid", "Root input has the wrong type or an unavailable entity: " + parameter.name);
                    resolvedInputs.Add(parameter.name, value);
                }
                else if (!string.IsNullOrEmpty(parameter.defaultValueJson))
                {
                    ContentResult<FlowValue> parsed = FlowValue.ParseJson(parameter.defaultValueJson, parameter.valueType,
                        resolvedRelease.vocabulary, context);
                    if (!parsed.IsReady) return Failure<ContentExecutionPlan>(parsed.diagnostics.FirstOrDefault(), "content_flow_input_default_invalid");
                    resolvedInputs.Add(parameter.name, parsed.value);
                }
                else if (parameter.required)
                    return Failure<ContentExecutionPlan>("content_flow_input_required", "Required root input is missing: " + parameter.name);
            }

            return ContentResult<ContentExecutionPlan>.Ready(new ContentExecutionPlan(package, resolvedRelease.release,
                resolvedRelease.vocabulary, flow, resolvedInputs, context,
                ContentLanguageResolver.Normalize(displayLanguage), ContentLanguageResolver.Normalize(speechLanguage)));
        }

        internal static FlowRecord FindFlow(ContentPackage package, ContentRef reference)
        {
            return package == null || package.flows == null || reference == null ? null : package.flows.FirstOrDefault(item =>
                item != null && item.id == reference.id && item.revision == reference.revision &&
                (string.IsNullOrEmpty(reference.expectedHash) || reference.expectedHash == item.semanticHash));
        }

        internal static ActionRecord FindAction(ContentPackage package, ContentRef reference)
        {
            return package == null || package.actions == null || reference == null ? null : package.actions.FirstOrDefault(item =>
                item != null && item.id == reference.id && item.revision == reference.revision &&
                (string.IsNullOrEmpty(reference.expectedHash) || reference.expectedHash == item.semanticHash));
        }

        internal static DialogueRecord FindDialogue(ContentPackage package, ContentRef reference)
        {
            return package == null || package.dialogues == null || reference == null ? null : package.dialogues.FirstOrDefault(item =>
                item != null && item.id == reference.id && item.revision == reference.revision &&
                (string.IsNullOrEmpty(reference.expectedHash) || reference.expectedHash == item.semanticHash));
        }

        internal static string GetBindingDestinationType(ContentPackage package, FlowRecord flow, StepRecord step, string destinationName)
        {
            if (package == null || flow == null || step == null || string.IsNullOrWhiteSpace(destinationName)) return string.Empty;
            if (step.kind == "subflow")
            {
                FlowRecord callee = FindFlow(package, step.calledFlowRef);
                FlowParameterRecord parameter = callee == null || callee.parameters == null ? null : callee.parameters.FirstOrDefault(item =>
                    item != null && item.direction == "input" && item.name == destinationName);
                return parameter == null ? string.Empty : parameter.valueType;
            }
            if (step.kind == "action")
            {
                ActionRecord action = FindAction(package, step.actionRef);
                if (action == null) return string.Empty;
                try
                {
                    JsonObject contract = ContentJson.Parse(action.contractJson) as JsonObject;
                    JsonArray inputs = contract == null || !contract.ContainsKey("inputs") ? null : contract["inputs"] as JsonArray;
                    JsonObject parameter = inputs == null ? null : inputs.OfType<JsonObject>().FirstOrDefault(item =>
                        ReadString(item, "name") == destinationName);
                    return parameter == null ? string.Empty : ReadString(parameter, "type");
                }
                catch { return string.Empty; }
            }
            return string.Empty;
        }

        internal static ContentResult<ScenarioBeat> ProjectBeat(ContentExecutionPlan plan, BeatRecord beat)
        {
            if (plan == null || beat == null) return Failure<ScenarioBeat>("content_beat_invalid", "A pinned Beat is required.");
            TextRecord text = plan.package.texts == null ? null : plan.package.texts.FirstOrDefault(item => item != null &&
                beat.textRef != null && item.id == beat.textRef.id && item.revision == beat.textRef.revision);
            if (text == null || !plan.release.dependencies.Any(item => item.kind == "text" && item.store == plan.package.storeId &&
                item.id == text.id && item.revision == text.revision && item.expectedHash == text.semanticHash))
                return Failure<ScenarioBeat>("content_beat_text_missing", "The exact Text revision is outside the pinned release.");
            ContentResult<ContentTextResolution> resolved = ContentLanguageResolver.Resolve(text, plan.displayLanguage, plan.speechLanguage);
            if (!resolved.IsReady) return Failure<ScenarioBeat>(resolved.diagnostics.FirstOrDefault(), "content_beat_text_unavailable");
            JsonObject presentation = null;
            try { presentation = ContentJson.Parse(beat.presentationJson ?? "{}") as JsonObject; } catch { }
            return ContentResult<ScenarioBeat>.Ready(new ScenarioBeat
            {
                id = beat.id,
                speaker = ReadString(presentation, "speaker", "alterra"),
                text = resolved.value.speechText,
                displayText = resolved.value.displayText,
                language = resolved.value.speechLanguage,
                expression = beat.expression ?? string.Empty,
                voice = ReadString(presentation, "voice"),
                audioKey = ReadString(presentation, "audioKey"),
                animation = ReadString(presentation, "animation")
            });
        }

        internal static string ReadString(JsonObject item, string name, string fallback = "")
        {
            JsonValue value;
            if (item == null || !item.TryGetValue(name, out value) || ContentJson.IsNull(value)) return fallback;
            try { return (string)value; } catch { return fallback; }
        }

        internal static bool ReadBoolean(JsonObject item, string name, bool fallback = false)
        {
            JsonValue value;
            if (item == null || !item.TryGetValue(name, out value) || value == null) return fallback;
            try { return (bool)value; } catch { return fallback; }
        }

        internal static string ReadTransitionTarget(FlowRecord flow, string fromStepId, string purpose)
        {
            TransitionRecord transition = flow == null || flow.transitions == null ? null : flow.transitions.FirstOrDefault(item =>
                item != null && item.fromStepId == fromStepId && item.purpose == purpose);
            return transition == null ? string.Empty : transition.toStepId;
        }

        internal static TransitionRecord FindTransition(FlowRecord flow, string transitionId)
        { return flow == null || flow.transitions == null ? null : flow.transitions.FirstOrDefault(item => item != null && item.id == transitionId); }

        internal static ChoiceRecord FindChoice(FlowRecord flow, string stepId, string choiceId)
        { return flow == null || flow.choices == null ? null : flow.choices.FirstOrDefault(item => item != null && item.stepId == stepId && item.id == choiceId); }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f')) return false;
            return true;
        }

        private static bool SameDependencies(IEnumerable<ContentReleaseDependency> left, IEnumerable<ContentReleaseDependency> right)
        {
            string[] first = (left ?? Enumerable.Empty<ContentReleaseDependency>()).Select(DependencyKey).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            string[] second = (right ?? Enumerable.Empty<ContentReleaseDependency>()).Select(DependencyKey).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            return first.SequenceEqual(second, StringComparer.Ordinal);
        }

        private static string DependencyKey(ContentReleaseDependency item)
        { return item.kind + "\0" + item.id + "\0" + item.revision + "\0" + item.store + "\0" + item.expectedHash; }

        private static ContentPackage CreateExecutionSnapshot(ContentPackage source)
        {
            var snapshot = new ContentPackage
            { packageId = source.packageId, storeId = source.storeId, sourceManifestHash = source.sourceManifestHash };
            snapshot.topics = source.topics == null ? null : source.topics.Select(CloneTopic).ToList();
            snapshot.flows = source.flows == null ? null : source.flows.Select(CloneFlow).ToList();
            snapshot.dialogues = source.dialogues == null ? null : source.dialogues.Select(CloneDialogue).ToList();
            snapshot.texts = source.texts == null ? null : source.texts.Select(CloneText).ToList();
            snapshot.actions = source.actions == null ? null : source.actions.Select(CloneAction).ToList();
            snapshot.items = source.items == null ? null : source.items.Select(CloneItem).ToList();
            snapshot.furnitureDefinitions = source.furnitureDefinitions == null ? null :
                source.furnitureDefinitions.Select(CloneFurniture).ToList();
            snapshot.rules = source.rules == null ? null : source.rules.Select(item => item == null ? null : new RuleRecord
            { id = item.id, revision = item.revision, semanticHash = item.semanticHash, expressionJson = item.expressionJson }).ToList();
            snapshot.releases = source.releases == null ? null : source.releases.Select(CloneRelease).ToList();
            return snapshot;
        }

        private static TopicRecord CloneTopic(TopicRecord item)
        {
            if (item == null) return null;
            return new TopicRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash, routeId = item.routeId,
                titleTextRef = CloneRef(item.titleTextRef), promptTextRef = CloneRef(item.promptTextRef),
                defaultReplayPolicy = item.defaultReplayPolicy, cooldownSeconds = item.cooldownSeconds,
                entries = item.entries == null ? null : item.entries.Select(entry => entry == null ? null : new TopicEntryRecord
                {
                    id = entry.id, flowRef = CloneRef(entry.flowRef), priority = entry.priority, baseWeight = entry.baseWeight,
                    conditionJson = entry.conditionJson, modifiersJson = entry.modifiersJson, channelsJson = entry.channelsJson,
                    consumptionNodeId = entry.consumptionNodeId
                }).ToList()
            };
        }

        private static FlowRecord CloneFlow(FlowRecord item)
        {
            if (item == null) return null;
            return new FlowRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash,
                kind = item.kind, entryStepId = item.entryStepId,
                steps = item.steps == null ? null : item.steps.Select(step => step == null ? null : new StepRecord
                {
                    id = step.id, kind = step.kind, dialogueRef = CloneRef(step.dialogueRef), actionRef = CloneRef(step.actionRef),
                    calledFlowRef = CloneRef(step.calledFlowRef), dataJson = step.dataJson
                }).ToList(),
                transitions = item.transitions == null ? null : item.transitions.Select(transition => transition == null ? null : new TransitionRecord
                {
                    id = transition.id, fromStepId = transition.fromStepId, toStepId = transition.toStepId,
                    purpose = transition.purpose, ruleRef = CloneRef(transition.ruleRef)
                }).ToList(),
                choices = item.choices == null ? null : item.choices.Select(choice => choice == null ? null : new ChoiceRecord
                {
                    stepId = choice.stepId, id = choice.id, transitionId = choice.transitionId, valueJson = choice.valueJson,
                    labelTextRef = CloneRef(choice.labelTextRef), availabilityRuleRef = CloneRef(choice.availabilityRuleRef),
                    lockedHintTextRef = CloneRef(choice.lockedHintTextRef)
                }).ToList(),
                parameters = item.parameters == null ? null : item.parameters.Select(parameter => parameter == null ? null : new FlowParameterRecord
                {
                    name = parameter.name, direction = parameter.direction, valueType = parameter.valueType,
                    required = parameter.required, defaultValueJson = parameter.defaultValueJson
                }).ToList(),
                variables = item.variables == null ? null : item.variables.Select(variable => variable == null ? null : new FlowVariableRecord
                {
                    name = variable.name, valueType = variable.valueType, mutable = variable.mutable,
                    defaultValueJson = variable.defaultValueJson
                }).ToList(),
                bindings = item.bindings == null ? null : item.bindings.Select(binding => binding == null ? null : new StepBindingRecord
                {
                    stepId = binding.stepId, destinationName = binding.destinationName, sourceKind = binding.sourceKind,
                    literalJson = binding.literalJson, sourceName = binding.sourceName, hostFactId = binding.hostFactId,
                    resultStepId = binding.resultStepId, resultName = binding.resultName
                }).ToList(),
                exits = item.exits == null ? null : item.exits.Select(exit => exit == null ? null : new FlowExitRecord
                { name = exit.name, contractJson = exit.contractJson }).ToList(),
                exitMappings = item.exitMappings == null ? null : item.exitMappings.Select(mapping => mapping == null ? null : new ExitMappingRecord
                {
                    callStepId = mapping.callStepId, calleeFlowRef = CloneRef(mapping.calleeFlowRef),
                    exitName = mapping.exitName, transitionId = mapping.transitionId, outputMappingsJson = mapping.outputMappingsJson
                }).ToList()
            };
        }

        private static DialogueRecord CloneDialogue(DialogueRecord item)
        {
            if (item == null) return null;
            return new DialogueRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash,
                beats = item.beats == null ? null : item.beats.Select(beat => beat == null ? null : new BeatRecord
                {
                    id = beat.id, sequenceOrder = beat.sequenceOrder, textRef = CloneRef(beat.textRef),
                    expression = beat.expression, presentationJson = beat.presentationJson
                }).ToList()
            };
        }

        private static TextRecord CloneText(TextRecord item)
        {
            if (item == null) return null;
            return new TextRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash,
                translations = item.translations == null ? null : item.translations.Select(translation => translation == null ? null :
                    new TranslationRecord { language = translation.language, text = translation.text }).ToList()
            };
        }

        private static ActionRecord CloneAction(ActionRecord item)
        {
            if (item == null) return null;
            return new ActionRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash, capabilityId = item.capabilityId,
                contractJson = item.contractJson, implementationKind = item.implementationKind, composerId = item.composerId,
                functionId = item.functionId, argumentsJson = item.argumentsJson,
                sequenceSteps = item.sequenceSteps == null ? null : item.sequenceSteps.Select(step => step == null ? null :
                    new ActionSequenceStepRecord
                    {
                        ordinal = step.ordinal, stepId = step.stepId, primitiveId = step.primitiveId,
                        argumentsJson = step.argumentsJson, outputsJson = step.outputsJson,
                        successEdgeJson = step.successEdgeJson, failureEdgeJson = step.failureEdgeJson,
                        cancelEdgeJson = step.cancelEdgeJson, checkpointPolicy = step.checkpointPolicy
                    }).ToList()
            };
        }

        private static ItemRecord CloneItem(ItemRecord item)
        {
            return item == null ? null : new ItemRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash,
                displayTextRef = CloneRef(item.displayTextRef), stackLimit = item.stackLimit
            };
        }

        private static FurnitureDefinitionRecord CloneFurniture(FurnitureDefinitionRecord item)
        {
            return item == null ? null : new FurnitureDefinitionRecord
            {
                id = item.id, revision = item.revision, semanticHash = item.semanticHash,
                displayTextRef = CloneRef(item.displayTextRef), definitionJson = item.definitionJson,
                functionBindings = item.functionBindings == null ? null : item.functionBindings.Select(binding => binding == null ? null :
                    new FurnitureFunctionBindingRecord
                    {
                        functionId = binding.functionId, pointId = binding.pointId, useId = binding.useId,
                        functionContractVersion = binding.functionContractVersion, contractJson = binding.contractJson
                    }).ToList()
            };
        }

        private static ContentRelease CloneRelease(ContentRelease item)
        {
            if (item == null) return null;
            return new ContentRelease
            {
                releaseId = item.releaseId, manifestJson = item.manifestJson, manifestHash = item.manifestHash,
                validatorVersion = item.validatorVersion, graphFingerprint = item.graphFingerprint,
                indexFingerprint = item.indexFingerprint,
                dependencies = item.dependencies == null ? null : item.dependencies.Select(dependency => dependency == null ? null :
                    new ContentReleaseDependency { kind = dependency.kind, id = dependency.id, revision = dependency.revision,
                        store = dependency.store, expectedHash = dependency.expectedHash }).ToList()
            };
        }

        private static ContentRef CloneRef(ContentRef reference)
        { return reference == null ? null : new ContentRef(reference.store, reference.id, reference.revision, reference.expectedHash); }

        private static ContentResult<T> Failure<T>(ContentDiagnostic diagnostic, string fallbackCode)
        { return Failure<T>(fallbackCode, diagnostic == null ? "Content data is unavailable." : diagnostic.message); }

        private static ContentResult<T> Failure<T>(string code, string message)
        { return ContentResult<T>.Failure(ContentStatuses.Invalid, code, "content_flow", message); }
    }
}
