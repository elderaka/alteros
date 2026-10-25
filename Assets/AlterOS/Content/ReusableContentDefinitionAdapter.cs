using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Activities;
using AlterOS.Furniture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    /// <summary>Shared source validation and normalized projection for the four reusable definition kinds.</summary>
    internal static class ReusableContentDefinitionAdapter
    {
        internal static List<ContentDiagnostic> ValidateSourceShape(string kind, JsonValue document, string path,
            ActivitySequenceBuilder sequenceBuilder = null)
        {
            var issues = new List<ContentDiagnostic>();
            if (kind == ContentSourceKinds.DialogueDefinition) ValidateDialogue(document, path, issues);
            else if (kind == ContentSourceKinds.ItemDefinition) ValidateItem(document, path, issues);
            else if (kind == ContentSourceKinds.FurnitureDefinition) ValidateFurniture(document, path, issues);
            else if (kind == ContentSourceKinds.ActionDefinition) ValidateAction(document, path, issues, sequenceBuilder);
            else issues.Add(new ContentDiagnostic("content_authoring_kind_unsupported", path, "This source kind is not a reusable definition."));
            return issues;
        }

        internal static string Project(ContentPackage package, string storeId, string kind, string id,
            string revision, JsonValue document, ActivitySequenceBuilder sequenceBuilder, out string metadataJson)
        {
            metadataJson = "{\"schemaVersion\":1,\"id\":" + LegacyContentAdapter.Quote(id) + "}";
            if (kind == ContentSourceKinds.DialogueDefinition)
            {
                var dialogue = new DialogueRecord { id = id, revision = revision, semanticHash = revision };
                JsonArray beats = LegacyContentAdapter.Child(document, "beats") as JsonArray;
                for (int index = 0; index < beats.Count; index++)
                {
                    JsonValue sourceBeat = beats[index];
                    dialogue.beats.Add(new BeatRecord
                    {
                        id = LegacyContentAdapter.ReadString(sourceBeat, "beatId", string.Empty),
                        sequenceOrder = index,
                        textRef = ResolveText(package, storeId, sourceBeat, "text", "textRef"),
                        expression = LegacyContentAdapter.ReadString(sourceBeat, "expression", string.Empty),
                        presentationJson = LegacyContentAdapter.IsNull(LegacyContentAdapter.Child(sourceBeat, "presentation"))
                            ? "{}" : LegacyContentAdapter.CanonicalJson(LegacyContentAdapter.Child(sourceBeat, "presentation"))
                    });
                }
                package.dialogues.Add(dialogue);
                return id;
            }
            if (kind == ContentSourceKinds.ItemDefinition)
            {
                long stackLimit = long.Parse(LegacyContentAdapter.Child(document, "stackLimit").ToString(), CultureInfo.InvariantCulture);
                package.items.Add(new ItemRecord
                {
                    id = id, revision = revision, semanticHash = revision,
                    displayTextRef = ResolveText(package, storeId, document, "displayName", "displayNameRef"),
                    stackLimit = stackLimit
                });
                return id;
            }
            if (kind == ContentSourceKinds.FurnitureDefinition)
            {
                var furniture = new FurnitureDefinitionRecord
                {
                    id = id, revision = revision, semanticHash = revision,
                    displayTextRef = ResolveText(package, storeId, document, "displayName", "displayNameRef"),
                    definitionJson = LegacyContentAdapter.CanonicalJson(LegacyContentAdapter.Child(document, "definition"))
                };
                foreach (JsonValue binding in LegacyContentAdapter.Array(LegacyContentAdapter.Child(document, "functions")))
                    furniture.functionBindings.Add(new FurnitureFunctionBindingRecord
                    {
                        functionId = LegacyContentAdapter.ReadString(binding, "functionId", string.Empty),
                        pointId = LegacyContentAdapter.ReadString(binding, "pointId", string.Empty),
                        useId = LegacyContentAdapter.ReadString(binding, "useId", string.Empty),
                        functionContractVersion = LegacyContentAdapter.ReadInt(binding, "contractVersion", 0),
                        contractJson = LegacyContentAdapter.CanonicalJson(LegacyContentAdapter.Child(binding, "contract"))
                    });
                package.furnitureDefinitions.Add(furniture);
                return id;
            }
            if (kind == ContentSourceKinds.ActionDefinition)
            {
                string capabilityId = LegacyContentAdapter.ReadString(document, "capabilityId", string.Empty);
                string implementationKind = LegacyContentAdapter.ReadString(document, "implementationKind", string.Empty);
                JsonValue data = LegacyContentAdapter.Child(document, "data");
                var action = new ActionRecord
                {
                    id = id, revision = revision, semanticHash = revision,
                    capabilityId = capabilityId, contractJson = LegacyContentAdapter.CanonicalJson(data),
                    implementationKind = implementationKind
                };
                if (implementationKind == "activity_sequence")
                {
                    action.composerId = LegacyContentAdapter.ReadString(data, "composerId", string.Empty);
                    IReadOnlyList<ContentDiagnostic> sequenceDiagnostics;
                    ActivitySequenceDefinition sequence = BuildNormalizedSequence(id, revision, data,
                        sequenceBuilder, out sequenceDiagnostics);
                    if (sequence == null)
                        throw new InvalidDataException(sequenceDiagnostics.Count == 0
                            ? "The Action sequence is unavailable." : sequenceDiagnostics[0].code);
                    action.sequenceSteps.AddRange(ToSequenceRecords(sequence));
                }
                else if (implementationKind == "host_effect")
                {
                    action.functionId = LegacyContentAdapter.ReadString(data, "functionId", string.Empty);
                    JsonValue arguments = LegacyContentAdapter.Child(data, "arguments");
                    action.argumentsJson = LegacyContentAdapter.IsNull(arguments) ? "{}" : LegacyContentAdapter.CanonicalJson(arguments);
                }
                package.actions.Add(action);
                return id;
            }
            throw new InvalidDataException("The reusable definition kind is unsupported: " + kind);
        }

        internal static ActivitySequenceDefinition BuildNormalizedSequence(string id, string revision,
            JsonValue data, ActivitySequenceBuilder builder, out IReadOnlyList<ContentDiagnostic> diagnostics)
        {
            diagnostics = Array.Empty<ContentDiagnostic>();
            if (builder == null)
            {
                diagnostics = new[] { new ContentDiagnostic("sequence_registry_unavailable", "data",
                    "No trusted ActivitySequenceBuilder is installed.") };
                return null;
            }
            var source = new ActivitySequenceSource
            {
                id = id ?? string.Empty,
                revision = revision ?? string.Empty,
                composerId = LegacyContentAdapter.ReadString(data, "composerId", string.Empty)
            };
            JsonArray rawComposerArguments = LegacyContentAdapter.Child(data, "composerArguments") as JsonArray;
            if (rawComposerArguments != null)
            {
                source.composerArguments = new List<ActivitySequenceArgument>();
                foreach (JsonValue rawArgument in rawComposerArguments)
                {
                    string name = LegacyContentAdapter.ReadString(rawArgument, "name", string.Empty);
                    FlowValue literal;
                    string binding;
                    if (!TryReadSequenceValue(LegacyContentAdapter.Child(rawArgument, "value"), out literal, out binding) || literal == null)
                    {
                        diagnostics = new[] { new ContentDiagnostic("sequence_composer_argument_invalid", "composerArguments." + name,
                            "Composer arguments must be typed literals.") };
                        return null;
                    }
                    source.composerArguments.Add(new ActivitySequenceArgument(name, ActivitySequenceValue.FromLiteral(literal)));
                }
            }
            JsonValue rawStepsValue = LegacyContentAdapter.Child(data, "steps");
            if (!LegacyContentAdapter.IsNull(rawStepsValue))
            {
                JsonArray rawSteps = rawStepsValue as JsonArray;
                if (rawSteps == null)
                {
                    diagnostics = new[] { new ContentDiagnostic("sequence_steps_invalid", "steps", "Sequence steps must be an array.") };
                    return null;
                }
                source.steps = new List<ActivitySequenceStep>();
                for (int index = 0; index < rawSteps.Count; index++)
                {
                    JsonValue rawStep = rawSteps[index];
                    JsonArray rawArguments = LegacyContentAdapter.Child(rawStep, "arguments") as JsonArray;
                    JsonArray rawBindings = LegacyContentAdapter.Child(rawStep, "resultBindings") as JsonArray;
                    if (rawArguments == null || rawBindings == null)
                    {
                        diagnostics = new[] { new ContentDiagnostic("sequence_step_shape_invalid", "steps[" + index + "]",
                            "Sequence arguments and resultBindings must be arrays.") };
                        return null;
                    }
                    var arguments = new List<ActivitySequenceArgument>();
                    foreach (JsonValue rawArgument in rawArguments)
                    {
                        string name = LegacyContentAdapter.ReadString(rawArgument, "name", string.Empty);
                        FlowValue literal;
                        string binding;
                        if (!TryReadSequenceValue(LegacyContentAdapter.Child(rawArgument, "value"), out literal, out binding))
                        {
                            diagnostics = new[] { new ContentDiagnostic("sequence_argument_value_invalid", "steps[" + index + "].arguments." + name,
                                "A sequence argument must contain one typed literal or result binding.") };
                            return null;
                        }
                        arguments.Add(new ActivitySequenceArgument(name, literal == null
                            ? ActivitySequenceValue.FromBinding(binding) : ActivitySequenceValue.FromLiteral(literal)));
                    }
                    var bindings = rawBindings.Select(item => new ActivitySequenceResultBinding(
                        LegacyContentAdapter.ReadString(item, "resultName", string.Empty),
                        LegacyContentAdapter.ReadString(item, "bindingId", string.Empty))).ToList();
                    ActivitySequenceCheckpointPolicy checkpoint;
                    string checkpointText = LegacyContentAdapter.ReadString(rawStep, "checkpointPolicy", string.Empty);
                    if (checkpointText == "none") checkpoint = ActivitySequenceCheckpointPolicy.None;
                    else if (checkpointText == "before") checkpoint = ActivitySequenceCheckpointPolicy.Before;
                    else if (checkpointText == "after") checkpoint = ActivitySequenceCheckpointPolicy.After;
                    else
                    {
                        diagnostics = new[] { new ContentDiagnostic("sequence_checkpoint_policy_invalid", "steps[" + index + "].checkpointPolicy",
                            "Checkpoint policy must be none, before, or after.") };
                        return null;
                    }
                    source.steps.Add(new ActivitySequenceStep(
                        LegacyContentAdapter.ReadString(rawStep, "stepId", string.Empty),
                        LegacyContentAdapter.ReadString(rawStep, "primitiveId", string.Empty), arguments, bindings,
                        LegacyContentAdapter.ReadString(rawStep, "successNextStepId", string.Empty),
                        LegacyContentAdapter.ReadString(rawStep, "failureNextStepId", string.Empty),
                        LegacyContentAdapter.ReadString(rawStep, "cancelNextStepId", string.Empty), checkpoint));
                }
            }
            ActivitySequenceBuildResult built = builder.Build(source);
            diagnostics = built.diagnostics;
            return built.isValid ? built.definition : null;
        }

        internal static List<ActionSequenceStepRecord> ToSequenceRecords(ActivitySequenceDefinition definition)
        {
            var records = new List<ActionSequenceStepRecord>();
            for (int index = 0; index < definition.steps.Count; index++)
            {
                ActivitySequenceStep step = definition.steps[index];
                var arguments = new JsonArray();
                foreach (ActivitySequenceArgument argument in step.arguments)
                    arguments.Add(new JsonObject
                    {
                        ["name"] = new JsonPrimitive(argument.name), ["value"] = SequenceValueJson(argument.value)
                    });
                var outputs = new JsonArray();
                foreach (ActivitySequenceResultBinding binding in step.resultBindings)
                    outputs.Add(new JsonObject
                    {
                        ["resultName"] = new JsonPrimitive(binding.resultName), ["bindingId"] = new JsonPrimitive(binding.bindingId)
                    });
                string checkpoint = step.checkpointPolicy == ActivitySequenceCheckpointPolicy.Before ? "before" :
                    step.checkpointPolicy == ActivitySequenceCheckpointPolicy.After ? "after" : "none";
                records.Add(new ActionSequenceStepRecord
                {
                    ordinal = index, stepId = step.stepId, primitiveId = step.primitiveId,
                    argumentsJson = LegacyContentAdapter.CanonicalJson(arguments),
                    outputsJson = LegacyContentAdapter.CanonicalJson(outputs),
                    successEdgeJson = EdgeJson(step.successNextStepId),
                    failureEdgeJson = EdgeJson(step.failureNextStepId),
                    cancelEdgeJson = EdgeJson(step.cancelNextStepId), checkpointPolicy = checkpoint
                });
            }
            return records;
        }

        internal static List<ContentDiagnostic> ValidateNormalizedSequenceAction(ActionRecord action,
            ActivitySequenceBuilder builder)
        {
            var diagnostics = new List<ContentDiagnostic>();
            if (action == null || action.implementationKind != "activity_sequence") return diagnostics;
            JsonValue data;
            try { data = ContentJson.Parse(action.contractJson); }
            catch (Exception exception)
            {
                diagnostics.Add(new ContentDiagnostic("action_sequence_contract_invalid", action.id, exception.Message));
                return diagnostics;
            }
            IReadOnlyList<ContentDiagnostic> buildDiagnostics;
            ActivitySequenceDefinition normalized = BuildNormalizedSequence(action.id, action.revision, data, builder, out buildDiagnostics);
            if (normalized == null)
            {
                diagnostics.AddRange(buildDiagnostics);
                return diagnostics;
            }
            List<ActionSequenceStepRecord> expected = ToSequenceRecords(normalized);
            if (action.sequenceSteps == null || expected.Count != action.sequenceSteps.Count)
            {
                diagnostics.Add(new ContentDiagnostic("action_sequence_normalized_rows_mismatch", action.id,
                    "The stored sequence rows do not match the registered builder output."));
                return diagnostics;
            }
            for (int index = 0; index < expected.Count; index++)
            {
                ActionSequenceStepRecord left = expected[index], right = action.sequenceSteps[index];
                if (right == null || left.ordinal != right.ordinal || left.stepId != right.stepId || left.primitiveId != right.primitiveId ||
                    left.argumentsJson != right.argumentsJson || left.outputsJson != right.outputsJson ||
                    left.successEdgeJson != right.successEdgeJson || left.failureEdgeJson != right.failureEdgeJson ||
                    left.cancelEdgeJson != right.cancelEdgeJson || left.checkpointPolicy != right.checkpointPolicy)
                {
                    diagnostics.Add(new ContentDiagnostic("action_sequence_normalized_rows_mismatch", action.id,
                        "The stored sequence rows do not match the registered builder output."));
                    break;
                }
            }
            return diagnostics;
        }

        private static bool TryReadSequenceValue(JsonValue value, out FlowValue literal, out string binding)
        {
            literal = null; binding = string.Empty;
            JsonObject item = value as JsonObject;
            if (item == null) return false;
            if (item.Count == 1 && item.ContainsKey("bindingId"))
            {
                binding = LegacyContentAdapter.ReadString(item, "bindingId", string.Empty);
                return LegacyContentAdapter.IsIdentifier(binding);
            }
            if (item.Count != 2 || !item.ContainsKey("type") || !item.ContainsKey("literal")) return false;
            string type = LegacyContentAdapter.ReadString(item, "type", string.Empty);
            JsonValue raw = LegacyContentAdapter.Child(item, "literal");
            try
            {
                if (type == "bool" && raw.Type == JTokenType.Boolean) literal = FlowValue.Boolean((bool)raw);
                else if (type == "int64" && ContentJson.IsNumber(raw)) literal = FlowValue.Int64(long.Parse(raw.ToString(), CultureInfo.InvariantCulture));
                else if (type == "double" && ContentJson.IsNumber(raw)) literal = FlowValue.Double(double.Parse(raw.ToString(), CultureInfo.InvariantCulture));
                else if (type == "text" && raw.Type == JTokenType.String) literal = FlowValue.Text((string)raw);
                else if (type == "enum" && raw.Type == JTokenType.String) literal = FlowValue.Enum((string)raw);
                else if (type == "entity" && raw is JsonObject && ((JsonObject)raw).Count == 2 &&
                    ((JsonObject)raw).ContainsKey("kind") && ((JsonObject)raw).ContainsKey("id"))
                    literal = FlowValue.Entity(LegacyContentAdapter.ReadString(raw, "kind", string.Empty), LegacyContentAdapter.ReadString(raw, "id", string.Empty));
                else return false;
                return true;
            }
            catch { return false; }
        }

        private static JsonObject SequenceValueJson(ActivitySequenceValue value)
        {
            if (!string.IsNullOrEmpty(value.bindingId))
                return new JsonObject { ["bindingId"] = new JsonPrimitive(value.bindingId) };
            FlowValue literal = value.literal;
            JsonValue raw;
            if (literal.type == "bool") raw = new JsonPrimitive(literal.boolValue);
            else if (literal.type == "int64") raw = new JsonPrimitive(literal.int64Value);
            else if (literal.type == "double") raw = new JsonPrimitive(literal.doubleValue);
            else if (literal.type == "text" || literal.type == "enum") raw = new JsonPrimitive(literal.textValue);
            else if (literal.type == "entity") raw = new JsonObject
            { ["kind"] = new JsonPrimitive(literal.entityValue.kind), ["id"] = new JsonPrimitive(literal.entityValue.id) };
            else raw = JsonPrimitive.CreateNull();
            return new JsonObject { ["type"] = new JsonPrimitive(literal.type), ["literal"] = raw };
        }

        private static void ValidateDialogue(JsonValue document, string path, List<ContentDiagnostic> issues)
        {
            LegacyContentAdapter.CheckObject(document, path, issues, "schemaVersion", "id", "beats");
            RequireVersionOne(document, path, issues);
            RequireIdentifier(document, "id", path, issues);
            JsonArray beats = LegacyContentAdapter.Child(document, "beats") as JsonArray;
            if (beats == null || beats.Count < 1 || beats.Count > 64)
            {
                issues.Add(new ContentDiagnostic("dialogue_beats_invalid", path + ".beats", "An independent Dialogue requires one to sixty-four ordered beats."));
                return;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < beats.Count; index++)
            {
                string beatPath = path + ".beats[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                JsonValue beat = beats[index];
                LegacyContentAdapter.CheckObject(beat, beatPath, issues, "beatId", "text", "textRef", "expression", "presentation");
                string beatId = LegacyContentAdapter.ReadString(beat, "beatId", string.Empty);
                if (!LegacyContentAdapter.IsIdentifier(beatId) || !ids.Add(beatId))
                    issues.Add(new ContentDiagnostic("dialogue_beat_id_invalid", beatPath + ".beatId", "Beat IDs must be valid and unique within the Dialogue."));
                ValidateTextBinding(beat, "text", "textRef", beatPath, issues);
                string expression = LegacyContentAdapter.ReadString(beat, "expression", string.Empty);
                if (expression.Length > 64)
                    issues.Add(new ContentDiagnostic("dialogue_expression_invalid", beatPath + ".expression", "Expression IDs are limited to sixty-four characters."));
                JsonValue presentation = LegacyContentAdapter.Child(beat, "presentation");
                if (!LegacyContentAdapter.IsNull(presentation) && !(presentation is JsonObject))
                    issues.Add(new ContentDiagnostic("dialogue_presentation_invalid", beatPath + ".presentation", "Beat presentation must be a JSON object."));
            }
        }

        private static void ValidateItem(JsonValue document, string path, List<ContentDiagnostic> issues)
        {
            LegacyContentAdapter.CheckObject(document, path, issues, "schemaVersion", "id", "displayName", "displayNameRef", "stackLimit");
            RequireVersionOne(document, path, issues);
            RequireIdentifier(document, "id", path, issues);
            ValidateTextBinding(document, "displayName", "displayNameRef", path, issues);
            JsonValue value = LegacyContentAdapter.Child(document, "stackLimit");
            long limit;
            if (LegacyContentAdapter.IsNull(value) || !long.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit <= 0)
                issues.Add(new ContentDiagnostic("item_stack_limit_invalid", path + ".stackLimit", "Item stackLimit must be a positive signed 64-bit integer."));
        }

        private static void ValidateFurniture(JsonValue document, string path, List<ContentDiagnostic> issues)
        {
            LegacyContentAdapter.CheckObject(document, path, issues, "schemaVersion", "id", "displayName", "displayNameRef", "definition", "functions");
            RequireVersionOne(document, path, issues);
            string id = RequireIdentifier(document, "id", path, issues);
            ValidateTextBinding(document, "displayName", "displayNameRef", path, issues);
            JsonValue definition = LegacyContentAdapter.Child(document, "definition");
            string definitionPath = path + ".definition";
            LegacyContentAdapter.CheckObject(definition, definitionPath, issues, "definitionId", "displayNameKey", "category", "scalePolicy",
                "defaultScale", "localBounds", "supportKinds", "blocksNavigation", "points", "functionBindings", "presentationBindings");
            string definitionId = LegacyContentAdapter.ReadString(definition, "definitionId", string.Empty);
            if (!LegacyContentAdapter.IsIdentifier(definitionId) || definitionId != id)
                issues.Add(new ContentDiagnostic("furniture_definition_id_mismatch", definitionPath + ".definitionId", "The embedded Furniture ID must match the source ID."));
            string textId = LegacyContentAdapter.ReadString(LegacyContentAdapter.Child(document, "displayName"), "id", string.Empty);
            if (string.IsNullOrEmpty(textId)) textId = LegacyContentAdapter.ReadString(LegacyContentAdapter.Child(document, "displayNameRef"), "id", string.Empty);
            if (LegacyContentAdapter.ReadString(definition, "displayNameKey", string.Empty) != textId)
                issues.Add(new ContentDiagnostic("furniture_display_text_mismatch", definitionPath + ".displayNameKey", "The legacy Furniture displayNameKey must match the owned or pinned Text ID."));
            try
            {
                FurnitureDefinition typedDefinition = JsonConvert.DeserializeObject<FurnitureDefinition>(LegacyContentAdapter.CanonicalJson(definition));
                foreach (FurnitureDiagnostic diagnostic in FurnitureCatalogueValidator.ValidateDefinition(typedDefinition))
                {
                    string suffix = diagnostic.path ?? string.Empty;
                    if (suffix == "definition") suffix = string.Empty;
                    else if (suffix.StartsWith("definition.", StringComparison.Ordinal)) suffix = suffix.Substring("definition.".Length);
                    issues.Add(new ContentDiagnostic(diagnostic.code,
                        string.IsNullOrEmpty(suffix) ? definitionPath : definitionPath + "." + suffix, diagnostic.message));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new ContentDiagnostic("furniture_definition_invalid", definitionPath, exception.Message));
            }
            JsonArray functions = LegacyContentAdapter.Child(document, "functions") as JsonArray;
            if (functions == null || functions.Count > 64)
                issues.Add(new ContentDiagnostic("furniture_functions_invalid", path + ".functions", "Furniture functions must be an array with at most 64 entries."));
            else
            {
                var contracts = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                foreach (JsonValue function in functions)
                {
                    string functionPath = path + ".functions[]";
                    LegacyContentAdapter.CheckObject(function, functionPath, issues, "functionId", "pointId", "useId", "contractVersion", "contract");
                    string functionId = LegacyContentAdapter.ReadString(function, "functionId", string.Empty);
                    if (!IsFurnitureFunctionRegistered(functionId))
                        issues.Add(new ContentDiagnostic("content_function_unregistered", functionPath + ".functionId", "The Furniture function is not registered by the host."));
                    if (!LegacyContentAdapter.IsIdentifier(functionId) || !LegacyContentAdapter.IsIdentifier(LegacyContentAdapter.ReadString(function, "pointId", string.Empty)) ||
                        !LegacyContentAdapter.IsIdentifier(LegacyContentAdapter.ReadString(function, "useId", string.Empty)))
                        issues.Add(new ContentDiagnostic("furniture_function_binding_invalid", functionPath, "Function bindings require a function, point, and use ID."));
                    int version = LegacyContentAdapter.ReadInt(function, "contractVersion", 0);
                    if (version != 1)
                        issues.Add(new ContentDiagnostic("furniture_function_version_invalid", functionPath + ".contractVersion", "Only the registered version 1 function contracts are supported."));
                    JsonObject contract = LegacyContentAdapter.Child(function, "contract") as JsonObject;
                    if (contract == null)
                        issues.Add(new ContentDiagnostic("furniture_function_contract_invalid", functionPath + ".contract", "A function contract object is required."));
                    else if (functionId == FurnitureFunctionIds.PowerSet)
                    {
                        LegacyContentAdapter.CheckObject(contract, functionPath + ".contract", issues, "desiredState");
                        JsonArray states = LegacyContentAdapter.Child(contract, "desiredState") as JsonArray;
                        if (states == null || states.Count != 2 || states[0].Type != JTokenType.String ||
                            states[1].Type != JTokenType.String || (string)states[0] != "Off" || (string)states[1] != "On")
                            issues.Add(new ContentDiagnostic("furniture_function_contract_invalid", functionPath + ".contract.desiredState",
                                "power.set version 1 must declare exactly Off and On states."));
                    }
                    string key = functionId + "\n" + LegacyContentAdapter.ReadString(function, "pointId", string.Empty) + "\n" +
                        LegacyContentAdapter.ReadString(function, "useId", string.Empty);
                    if (!contracts.ContainsKey(key)) contracts.Add(key, function);
                }
                foreach (JsonValue binding in LegacyContentAdapter.Array(LegacyContentAdapter.Child(definition, "functionBindings")))
                {
                    string key = LegacyContentAdapter.ReadString(binding, "functionId", string.Empty) + "\n" +
                        LegacyContentAdapter.ReadString(binding, "pointId", string.Empty) + "\n" +
                        LegacyContentAdapter.ReadString(binding, "useId", string.Empty);
                    if (!contracts.ContainsKey(key))
                        issues.Add(new ContentDiagnostic("furniture_function_contract_missing", definitionPath + ".functionBindings", "Each Furniture binding needs a matching host-registered contract record."));
                }
            }
        }

        private static void ValidateAction(JsonValue document, string path, List<ContentDiagnostic> issues,
            ActivitySequenceBuilder sequenceBuilder)
        {
            LegacyContentAdapter.CheckObject(document, path, issues, "schemaVersion", "id", "capabilityId", "implementationKind", "data");
            RequireVersionOne(document, path, issues);
            RequireIdentifier(document, "id", path, issues);
            RequireIdentifier(document, "capabilityId", path, issues);
            string implementation = LegacyContentAdapter.ReadString(document, "implementationKind", string.Empty);
            JsonValue data = LegacyContentAdapter.Child(document, "data");
            if (implementation == "activity_sequence") ValidateActivitySequence(data, path + ".data",
                LegacyContentAdapter.ReadString(document, "id", string.Empty), sequenceBuilder, issues);
            else if (implementation == "host_effect") ValidateHostEffect(data, path + ".data",
                LegacyContentAdapter.ReadString(document, "capabilityId", string.Empty), issues);
            else issues.Add(new ContentDiagnostic("action_implementation_kind_invalid", path + ".implementationKind",
                "New Actions must use activity_sequence or host_effect; legacy_activity is reserved for migrated compiled rows."));
        }

        private static void ValidateActivitySequence(JsonValue data, string path, string actionId,
            ActivitySequenceBuilder sequenceBuilder, List<ContentDiagnostic> issues)
        {
            int initialIssueCount = issues.Count;
            LegacyContentAdapter.CheckObject(data, path, issues, "steps", "composerId", "composerArguments");
            JsonValue stepsValue = LegacyContentAdapter.Child(data, "steps");
            string composerId = LegacyContentAdapter.ReadString(data, "composerId", string.Empty);
            bool hasSteps = !LegacyContentAdapter.IsNull(stepsValue);
            bool hasComposer = !string.IsNullOrWhiteSpace(composerId);
            if (hasSteps == hasComposer)
            {
                issues.Add(new ContentDiagnostic("action_sequence_source_xor_invalid", path, "Provide exactly one of steps or a trusted composerId."));
                return;
            }
            if (hasComposer)
            {
                if (!LegacyContentAdapter.IsIdentifier(composerId))
                    issues.Add(new ContentDiagnostic("action_composer_invalid", path + ".composerId", "A registered composer ID is required."));
                JsonValue composerArguments = LegacyContentAdapter.Child(data, "composerArguments");
                if (!LegacyContentAdapter.IsNull(composerArguments))
                {
                    JsonArray arguments = composerArguments as JsonArray;
                    if (arguments == null || arguments.Count > 16)
                        issues.Add(new ContentDiagnostic("action_composer_arguments_invalid", path + ".composerArguments", "Composer arguments must be a bounded array."));
                    else foreach (JsonValue argument in arguments)
                    {
                        LegacyContentAdapter.CheckObject(argument, path + ".composerArguments[]", issues, "name", "value");
                        if (!LegacyContentAdapter.IsIdentifier(LegacyContentAdapter.ReadString(argument, "name", string.Empty)))
                            issues.Add(new ContentDiagnostic("action_composer_argument_invalid", path + ".composerArguments[]", "A composer argument needs a valid name."));
                    }
                }
            }
            else
            {
                JsonArray steps = stepsValue as JsonArray;
                if (steps == null || steps.Count < 1 || steps.Count > ActivitySequenceValidator.MaximumStepCount)
                {
                    issues.Add(new ContentDiagnostic("action_sequence_steps_invalid", path + ".steps", "A sequence requires one to sixty-four steps."));
                    return;
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < steps.Count; index++)
                {
                    string stepPath = path + ".steps[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    JsonValue step = steps[index];
                    LegacyContentAdapter.CheckObject(step, stepPath, issues, "stepId", "primitiveId", "arguments", "resultBindings",
                        "successNextStepId", "failureNextStepId", "cancelNextStepId", "checkpointPolicy");
                    string stepId = LegacyContentAdapter.ReadString(step, "stepId", string.Empty);
                    string primitiveId = LegacyContentAdapter.ReadString(step, "primitiveId", string.Empty);
                    if (!LegacyContentAdapter.IsIdentifier(stepId) || !ids.Add(stepId))
                        issues.Add(new ContentDiagnostic("action_sequence_step_id_invalid", stepPath + ".stepId", "Sequence step IDs must be valid and unique."));
                    if (!LegacyContentAdapter.IsIdentifier(primitiveId))
                        issues.Add(new ContentDiagnostic("action_sequence_primitive_invalid", stepPath + ".primitiveId", "A registered primitive ID is required."));
                    ValidateSequenceArguments(LegacyContentAdapter.Child(step, "arguments"), stepPath + ".arguments", issues);
                    ValidateSequenceBindings(LegacyContentAdapter.Child(step, "resultBindings"), stepPath + ".resultBindings", issues);
                    foreach (string edge in new[] { "successNextStepId", "failureNextStepId", "cancelNextStepId" })
                    {
                        string target = LegacyContentAdapter.ReadString(step, edge, string.Empty);
                        if (!string.IsNullOrEmpty(target) && !LegacyContentAdapter.IsIdentifier(target))
                            issues.Add(new ContentDiagnostic("action_sequence_edge_invalid", stepPath + "." + edge, "Sequence transition targets must be valid IDs or empty."));
                    }
                    string checkpoint = LegacyContentAdapter.ReadString(step, "checkpointPolicy", string.Empty);
                    if (checkpoint != "none" && checkpoint != "before" && checkpoint != "after")
                        issues.Add(new ContentDiagnostic("action_sequence_checkpoint_invalid", stepPath + ".checkpointPolicy", "Checkpoint policy must be none, before, or after."));
                }
                for (int index = 0; index < steps.Count; index++)
                    foreach (string edge in new[] { "successNextStepId", "failureNextStepId", "cancelNextStepId" })
                    {
                        string target = LegacyContentAdapter.ReadString(steps[index], edge, string.Empty);
                        if (!string.IsNullOrEmpty(target) && !ids.Contains(target))
                            issues.Add(new ContentDiagnostic("action_sequence_edge_target_missing",
                                path + ".steps[" + index.ToString(CultureInfo.InvariantCulture) + "]." + edge,
                                "A transition must target a step in this sequence."));
                    }
            }

            if (sequenceBuilder == null)
            {
                issues.Add(new ContentDiagnostic("sequence_registry_unavailable", path,
                    "No trusted ActivitySequenceBuilder is installed; this Action cannot be validated or published."));
                return;
            }
            if (issues.Count != initialIssueCount) return;
            IReadOnlyList<ContentDiagnostic> sequenceIssues;
            if (BuildNormalizedSequence(actionId, "0000000000000000000000000000000000000000000000000000000000000000",
                data, sequenceBuilder, out sequenceIssues) == null)
                foreach (ContentDiagnostic diagnostic in sequenceIssues)
                    issues.Add(new ContentDiagnostic(diagnostic.code, path + "." + diagnostic.path, diagnostic.message));
        }

        private static void ValidateHostEffect(JsonValue data, string path, string capabilityId, List<ContentDiagnostic> issues)
        {
            LegacyContentAdapter.CheckObject(data, path, issues, "functionId", "arguments");
            string functionId = LegacyContentAdapter.ReadString(data, "functionId", string.Empty);
            string requiredCapability;
            if (!ContentHostEffectFunctionRegistry.TryGetRequiredCapability(functionId, out requiredCapability))
                issues.Add(new ContentDiagnostic("content_function_unregistered", path + ".functionId", "The host-effect function is not registered."));
            else if (capabilityId != requiredCapability)
                issues.Add(new ContentDiagnostic("host_effect_capability_mismatch", path + ".functionId",
                    "The host-effect function requires its registered capability: " + requiredCapability + "."));
            JsonValue arguments = LegacyContentAdapter.Child(data, "arguments");
            if (functionId == ContentHostEffectFunctionRegistry.PlayerItemGrant)
            {
                LegacyContentAdapter.CheckObject(arguments, path + ".arguments", issues, "itemRef", "quantity");
                ValidateContentRef(LegacyContentAdapter.Child(arguments, "itemRef"), path + ".arguments.itemRef", issues);
                long quantity;
                JsonValue value = LegacyContentAdapter.Child(arguments, "quantity");
                if (LegacyContentAdapter.IsNull(value) || !long.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity <= 0)
                    issues.Add(new ContentDiagnostic("item_grant_quantity_invalid", path + ".arguments.quantity", "An Item grant quantity must be a positive signed 64-bit integer."));
            }
            else if (functionId == ContentHostEffectFunctionRegistry.FurniturePowerSet)
            {
                LegacyContentAdapter.CheckObject(arguments, path + ".arguments", issues, "desiredState");
                string desired = LegacyContentAdapter.ReadString(arguments, "desiredState", string.Empty);
                if (desired != "Off" && desired != "On")
                    issues.Add(new ContentDiagnostic("power_state_invalid", path + ".arguments.desiredState", "power.set accepts only Off or On."));
            }
        }

        private static void ValidateTextBinding(JsonValue owner, string ownedName, string pinnedName,
            string path, List<ContentDiagnostic> issues)
        {
            JsonValue owned = LegacyContentAdapter.Child(owner, ownedName);
            JsonValue pinned = LegacyContentAdapter.Child(owner, pinnedName);
            bool hasOwned = !LegacyContentAdapter.IsNull(owned), hasPinned = !LegacyContentAdapter.IsNull(pinned);
            if (hasOwned == hasPinned)
            {
                issues.Add(new ContentDiagnostic("content_text_binding_xor", path, "Provide exactly one owned localized Text object or pinned Text ContentRef."));
                return;
            }
            if (hasPinned) { ValidateContentRef(pinned, path + "." + pinnedName, issues); return; }
            string textPath = path + "." + ownedName;
            LegacyContentAdapter.CheckObject(owned, textPath, issues, "id", "translations");
            RequireIdentifier(owned, "id", textPath, issues);
            JsonArray translations = LegacyContentAdapter.Child(owned, "translations") as JsonArray;
            if (translations == null || translations.Count < 1 || translations.Count > 64)
            {
                issues.Add(new ContentDiagnostic("content_text_translations_invalid", textPath + ".translations", "Owned Text requires one to sixty-four translations."));
                return;
            }
            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonValue translation in translations)
            {
                string translationPath = textPath + ".translations[]";
                LegacyContentAdapter.CheckObject(translation, translationPath, issues, "language", "text");
                string language = LegacyContentAdapter.ReadString(translation, "language", string.Empty);
                string text = LegacyContentAdapter.ReadString(translation, "text", string.Empty);
                if (language.Length < 2 || language.Length > 16 || !languages.Add(language))
                    issues.Add(new ContentDiagnostic("content_text_language_invalid", translationPath + ".language", "Translation language tags must be bounded and unique."));
                if (text.Length > 8192)
                    issues.Add(new ContentDiagnostic("content_text_value_invalid", translationPath + ".text", "Translation text is limited to 8192 characters."));
            }
        }

        private static void ValidateContentRef(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            LegacyContentAdapter.CheckObject(value, path, issues, "store", "id", "revision", "expectedHash");
            Guid store;
            if (!Guid.TryParse(LegacyContentAdapter.ReadString(value, "store", string.Empty), out store))
                issues.Add(new ContentDiagnostic("content_ref_store_invalid", path + ".store", "A pinned ContentRef must name a store GUID."));
            RequireIdentifier(value, "id", path, issues);
            if (!LegacyContentAdapter.IsSha256(LegacyContentAdapter.ReadString(value, "revision", string.Empty)) ||
                !LegacyContentAdapter.IsSha256(LegacyContentAdapter.ReadString(value, "expectedHash", string.Empty)))
                issues.Add(new ContentDiagnostic("content_ref_revision_invalid", path, "A pinned ContentRef requires a revision and expected SHA-256 hash."));
        }

        private static void ValidateVector3(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            LegacyContentAdapter.CheckObject(value, path, issues, "x", "y", "z");
            foreach (string axis in new[] { "x", "y", "z" })
            {
                double component;
                JsonValue part = LegacyContentAdapter.Child(value, axis);
                if (LegacyContentAdapter.IsNull(part) || !double.TryParse(part.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out component) ||
                    double.IsNaN(component) || double.IsInfinity(component))
                    issues.Add(new ContentDiagnostic("furniture_vector_invalid", path + "." + axis, "Vector components must be finite numbers."));
            }
        }

        private static void ValidateStringArray(JsonValue value, string path, List<ContentDiagnostic> issues, int minimum, int maximum)
        {
            JsonArray values = value as JsonArray;
            if (values == null || values.Count < minimum || values.Count > maximum)
            {
                issues.Add(new ContentDiagnostic("content_string_array_invalid", path, "The string array is missing or outside its supported count."));
                return;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonValue item in values)
            {
                string text = LegacyContentAdapter.ReadStringValue(item, path);
                if (!LegacyContentAdapter.IsIdentifier(text) || !seen.Add(text))
                    issues.Add(new ContentDiagnostic("content_string_array_entry_invalid", path, "String array entries must be valid and unique IDs."));
            }
        }

        private static void ValidateSequenceArguments(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            JsonArray arguments = value as JsonArray;
            if (arguments == null || arguments.Count > 64)
            {
                issues.Add(new ContentDiagnostic("action_sequence_arguments_invalid", path, "Sequence arguments must be an array with at most 64 entries."));
                return;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonValue argument in arguments)
            {
                string argumentPath = path + "[]";
                LegacyContentAdapter.CheckObject(argument, argumentPath, issues, "name", "value");
                string name = LegacyContentAdapter.ReadString(argument, "name", string.Empty);
                if (!LegacyContentAdapter.IsIdentifier(name) || !names.Add(name))
                    issues.Add(new ContentDiagnostic("action_sequence_argument_name_invalid", argumentPath + ".name", "Argument names must be valid and unique."));
                if (!(LegacyContentAdapter.Child(argument, "value") is JsonObject))
                    issues.Add(new ContentDiagnostic("action_sequence_argument_value_invalid", argumentPath + ".value", "An argument requires a typed literal or result binding."));
            }
        }

        private static void ValidateSequenceBindings(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            JsonArray bindings = value as JsonArray;
            if (bindings == null || bindings.Count > 64)
            {
                issues.Add(new ContentDiagnostic("action_sequence_bindings_invalid", path, "Result bindings must be an array with at most 64 entries."));
                return;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonValue binding in bindings)
            {
                string bindingPath = path + "[]";
                LegacyContentAdapter.CheckObject(binding, bindingPath, issues, "resultName", "bindingId");
                string resultName = LegacyContentAdapter.ReadString(binding, "resultName", string.Empty);
                string bindingId = LegacyContentAdapter.ReadString(binding, "bindingId", string.Empty);
                if (!LegacyContentAdapter.IsIdentifier(resultName) || !LegacyContentAdapter.IsIdentifier(bindingId) || !names.Add(resultName))
                    issues.Add(new ContentDiagnostic("action_sequence_binding_invalid", bindingPath, "Result names and binding IDs must be valid and result names unique."));
            }
        }

        private static void RequireVersionOne(JsonValue value, string path, List<ContentDiagnostic> issues)
        {
            if (LegacyContentAdapter.ReadInt(value, "schemaVersion", 0) != 1)
                issues.Add(new ContentDiagnostic("content_source_schema_unsupported", path + ".schemaVersion", "This source kind supports schemaVersion 1."));
        }

        private static string RequireIdentifier(JsonValue value, string field, string path, List<ContentDiagnostic> issues)
        {
            string id = LegacyContentAdapter.ReadString(value, field, string.Empty);
            if (!LegacyContentAdapter.IsIdentifier(id)) issues.Add(new ContentDiagnostic("content_identifier_invalid", path + "." + field, "A stable bounded content ID is required."));
            return id;
        }

        private static bool IsFurnitureFunctionRegistered(string id)
        { return id == FurnitureFunctionIds.PowerSet; }

        private static ContentRef ResolveText(ContentPackage package, string storeId, JsonValue owner,
            string ownedName, string pinnedName)
        {
            JsonValue owned = LegacyContentAdapter.Child(owner, ownedName);
            if (!LegacyContentAdapter.IsNull(owned)) return AddOwnedText(package, storeId, owned);
            JsonValue pinned = LegacyContentAdapter.Child(owner, pinnedName);
            return LegacyContentAdapter.IsNull(pinned) ? null : new ContentRef(
                LegacyContentAdapter.ReadString(pinned, "store", string.Empty), LegacyContentAdapter.ReadString(pinned, "id", string.Empty),
                LegacyContentAdapter.ReadString(pinned, "revision", string.Empty), LegacyContentAdapter.ReadString(pinned, "expectedHash", string.Empty));
        }

        private static ContentRef AddOwnedText(ContentPackage package, string storeId, JsonValue owned)
        {
            string id = LegacyContentAdapter.ReadString(owned, "id", string.Empty);
            JsonArray sourceTranslations = LegacyContentAdapter.Child(owned, "translations") as JsonArray;
            var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonValue row in sourceTranslations)
                sorted.Add(LegacyContentAdapter.ReadString(row, "language", string.Empty), LegacyContentAdapter.ReadString(row, "text", string.Empty));
            var canonical = new JsonArray(sorted.Select(pair => (JsonValue)new JsonObject
            {
                ["language"] = new JsonPrimitive(pair.Key), ["text"] = new JsonPrimitive(pair.Value)
            }));
            string revision = LegacyContentAdapter.Hash(Encoding.UTF8.GetBytes("owned-text\n" + LegacyContentAdapter.CanonicalJson(canonical)));
            if (!package.texts.Any(item => item.id == id && item.revision == revision))
            {
                var text = new TextRecord { id = id, revision = revision, semanticHash = revision };
                foreach (KeyValuePair<string, string> translation in sorted)
                    text.translations.Add(new TranslationRecord { language = translation.Key, text = translation.Value });
                package.texts.Add(text);
            }
            return new ContentRef(storeId, id, revision, revision);
        }

        private static string CanonicalArray(JsonValue value)
        { return LegacyContentAdapter.IsNull(value) ? "[]" : LegacyContentAdapter.CanonicalJson(value); }

        private static string EdgeJson(string target)
        { return "{\"nextStepId\":" + LegacyContentAdapter.Quote(target) + "}"; }
    }
}
