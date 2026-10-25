using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.Activities
{
    public sealed class ActivitySequenceValidationResult
    {
        public readonly bool isValid;
        public readonly ReadOnlyCollection<ContentDiagnostic> diagnostics;

        internal ActivitySequenceValidationResult(IEnumerable<ContentDiagnostic> items)
        {
            diagnostics = Array.AsReadOnly((items ?? new ContentDiagnostic[0]).ToArray());
            isValid = diagnostics.Count == 0;
        }
    }

    /// <summary>Strict validator for normalized, non-recursive ActivitySequence data.</summary>
    public sealed class ActivitySequenceValidator
    {
        public const int MaximumStepCount = 64;
        public const double MaximumWaitSeconds = 300d;

        private sealed class BoundActivityResult
        {
            public readonly string primitiveId;
            public readonly ActivityResultDescriptor result;

            public BoundActivityResult(string sourcePrimitiveId, ActivityResultDescriptor sourceResult)
            { primitiveId = sourcePrimitiveId; result = sourceResult; }
        }

        private readonly ActivityPrimitiveRegistry primitives;

        public ActivitySequenceValidator(ActivityPrimitiveRegistry primitiveRegistry)
        {
            primitives = primitiveRegistry ?? throw new ArgumentNullException("primitiveRegistry");
        }

        public ActivitySequenceValidationResult Validate(ActivitySequenceDefinition definition)
        {
            var diagnostics = new List<ContentDiagnostic>();
            if (definition == null)
            {
                Add(diagnostics, "sequence_missing", "sequence", "An ActivitySequenceDefinition is required.");
                return new ActivitySequenceValidationResult(diagnostics);
            }
            if (!ActivitySequenceIdentifiers.IsValid(definition.id))
                Add(diagnostics, "sequence_id_invalid", "id", "The sequence ID is missing or invalid.");
            if (!ActivitySequenceIdentifiers.IsValid(definition.revision))
                Add(diagnostics, "sequence_revision_invalid", "revision", "The sequence revision is missing or invalid.");

            IList<ActivitySequenceStep> steps = definition.steps;
            if (steps.Count == 0 || steps.Count > MaximumStepCount)
                Add(diagnostics, "sequence_step_count_invalid", "steps", "A sequence must contain between 1 and 64 steps.");

            var stepIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < steps.Count; index++)
            {
                ActivitySequenceStep step = steps[index];
                if (step == null || !ActivitySequenceIdentifiers.IsValid(step.stepId))
                {
                    Add(diagnostics, "sequence_step_id_invalid", "steps[" + index + "].stepId", "Every step must have a valid ID.");
                    continue;
                }
                if (stepIndices.ContainsKey(step.stepId))
                    Add(diagnostics, "sequence_step_id_duplicate", "steps[" + index + "].stepId", "Step IDs must be unique.");
                else
                    stepIndices.Add(step.stepId, index);
            }

            var descriptors = new ActivityPrimitiveDescriptor[steps.Count];
            var bindings = new Dictionary<string, BoundActivityResult>(StringComparer.Ordinal);
            for (int index = 0; index < steps.Count; index++)
            {
                ActivitySequenceStep step = steps[index];
                if (step == null || !ActivitySequenceIdentifiers.IsValid(step.stepId)) continue;

                ActivityPrimitiveDescriptor descriptor;
                if (!primitives.TryResolve(step.primitiveId, out descriptor))
                {
                    Add(diagnostics, "sequence_primitive_unregistered", "steps[" + index + "].primitiveId",
                        "The primitive is not registered by the host.");
                    continue;
                }
                descriptors[index] = descriptor;
                if (!Enum.IsDefined(typeof(ActivitySequenceCheckpointPolicy), step.checkpointPolicy))
                    Add(diagnostics, "sequence_checkpoint_policy_invalid", "steps[" + index + "].checkpointPolicy",
                        "The checkpoint policy is not supported.");

                ValidateResultBindings(step, descriptor, index, bindings, diagnostics);
                ValidateTransitions(step, index, stepIndices, diagnostics);
                ValidateFurnitureArguments(step, index, diagnostics);
            }

            HashSet<string>[] guaranteedBindings = FindGuaranteedBindings(steps, stepIndices, bindings);
            for (int index = 0; index < steps.Count; index++)
            {
                ActivitySequenceStep step = steps[index];
                ActivityPrimitiveDescriptor descriptor = descriptors[index];
                if (step == null || descriptor == null) continue;
                ValidateArguments(step, descriptor, index,
                    guaranteedBindings[index] ?? new HashSet<string>(StringComparer.Ordinal), bindings, diagnostics);
            }
            ValidateReachability(steps, stepIndices, diagnostics);
            return new ActivitySequenceValidationResult(diagnostics);
        }

        /// <summary>Validate outputs already supplied by a nonterminal primitive result.</summary>
        public ActivitySequenceValidationResult ValidatePrimitiveOutputs(string primitiveId,
            IEnumerable<ActivitySequenceArgument> outputs)
        {
            return ValidatePrimitiveOutputsCore(primitiveId, outputs, null);
        }

        /// <summary>
        /// Validate completed-success outputs against the current step's result bindings.
        /// Call only after success; accepted/running results may legitimately omit outputs.
        /// </summary>
        public ActivitySequenceValidationResult ValidatePrimitiveSuccessOutputs(string primitiveId,
            IEnumerable<ActivitySequenceArgument> outputs,
            IEnumerable<ActivitySequenceResultBinding> selectedBindings)
        {
            if (selectedBindings == null)
                return new ActivitySequenceValidationResult(new[]
                {
                    new ContentDiagnostic("primitive_output_contract_missing", "resultBindings",
                        "Completed success requires the current step's result-binding contract.")
                });
            return ValidatePrimitiveOutputsCore(primitiveId, outputs, selectedBindings);
        }

        private ActivitySequenceValidationResult ValidatePrimitiveOutputsCore(string primitiveId,
            IEnumerable<ActivitySequenceArgument> outputs,
            IEnumerable<ActivitySequenceResultBinding> selectedBindings)
        {
            var diagnostics = new List<ContentDiagnostic>();
            ActivityPrimitiveDescriptor descriptor;
            if (!primitives.TryResolve(primitiveId, out descriptor))
            {
                Add(diagnostics, "sequence_primitive_unregistered", "primitiveId", "The primitive is not registered by the host.");
                return new ActivitySequenceValidationResult(diagnostics);
            }

            ActivitySequenceArgument[] supplied = (outputs ?? new ActivitySequenceArgument[0]).ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < supplied.Length; index++)
            {
                ActivitySequenceArgument output = supplied[index];
                string path = "outputs[" + index + "]";
                if (output == null || !ActivitySequenceIdentifiers.IsValid(output.name) || output.value == null ||
                    output.value.literal == null || !string.IsNullOrEmpty(output.value.bindingId))
                {
                    Add(diagnostics, "primitive_output_invalid", path, "Primitive outputs must be named literal values.");
                    continue;
                }
                if (!names.Add(output.name))
                    Add(diagnostics, "primitive_output_duplicate", path, "Primitive output names must be unique.");
                ActivityResultDescriptor expected = descriptor.results.FirstOrDefault(item => item.name == output.name);
                if (expected == null)
                {
                    Add(diagnostics, "primitive_output_unknown", path, "The primitive did not declare this output.");
                    continue;
                }
                if (output.value.literal.type != expected.valueType)
                    Add(diagnostics, "primitive_output_type_mismatch", path, "The output type does not match the primitive descriptor.");
                if (!IsWithinBounds(output.value.literal, expected.valueType,
                        expected.minimum, expected.maximum, expected.allowedValues, expected.entityKind))
                    Add(diagnostics, "primitive_output_out_of_range", path,
                        "The output is outside the primitive's declared range or enum values.");
            }

            if (selectedBindings != null)
            {
                var bindingIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (ActivitySequenceResultBinding binding in selectedBindings)
                {
                    if (binding == null || !ActivitySequenceIdentifiers.IsValid(binding.resultName) ||
                        !ActivitySequenceIdentifiers.IsValid(binding.bindingId) || !bindingIds.Add(binding.bindingId))
                    {
                        Add(diagnostics, "primitive_output_binding_invalid", "resultBindings",
                            "Selected result bindings must have valid, unique result and binding IDs.");
                        continue;
                    }
                    if (!descriptor.results.Any(item => item.name == binding.resultName))
                    {
                        Add(diagnostics, "primitive_output_binding_unknown", "resultBindings." + binding.resultName,
                            "The primitive does not declare a selected result binding.");
                        continue;
                    }
                    if (!names.Contains(binding.resultName))
                        Add(diagnostics, "primitive_output_missing", "resultBindings." + binding.resultName,
                            "Completed success omitted a result selected for binding.");
                }
            }
            return new ActivitySequenceValidationResult(diagnostics);
        }

        private static HashSet<string>[] FindGuaranteedBindings(IList<ActivitySequenceStep> steps,
            IDictionary<string, int> stepIndices, IDictionary<string, BoundActivityResult> bindings)
        {
            var incoming = new HashSet<string>[steps.Count];
            if (steps.Count == 0) return incoming;
            incoming[0] = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < steps.Count; index++)
            {
                HashSet<string> availableBefore = incoming[index];
                ActivitySequenceStep step = steps[index];
                if (availableBefore == null || step == null) continue;

                var afterSuccess = new HashSet<string>(availableBefore, StringComparer.Ordinal);
                foreach (ActivitySequenceResultBinding resultBinding in step.resultBindings)
                    if (resultBinding != null && bindings.ContainsKey(resultBinding.bindingId))
                        afterSuccess.Add(resultBinding.bindingId);

                Propagate(step.successNextStepId, afterSuccess, index, stepIndices, incoming);
                Propagate(step.failureNextStepId, availableBefore, index, stepIndices, incoming);
                Propagate(step.cancelNextStepId, availableBefore, index, stepIndices, incoming);
            }
            return incoming;
        }

        private static void Propagate(string target, HashSet<string> available, int sourceIndex,
            IDictionary<string, int> stepIndices, HashSet<string>[] incoming)
        {
            int targetIndex;
            if (string.IsNullOrWhiteSpace(target) || !stepIndices.TryGetValue(target, out targetIndex) || targetIndex <= sourceIndex)
                return;
            if (incoming[targetIndex] == null)
                incoming[targetIndex] = new HashSet<string>(available, StringComparer.Ordinal);
            else
                incoming[targetIndex].IntersectWith(available);
        }

        private static void ValidateArguments(ActivitySequenceStep step, ActivityPrimitiveDescriptor descriptor,
            int stepIndex, ISet<string> availableBindings,
            IDictionary<string, BoundActivityResult> bindings, IList<ContentDiagnostic> diagnostics)
        {
            ActivitySequenceArgument[] supplied = step.arguments == null
                ? new ActivitySequenceArgument[0] : step.arguments.ToArray();
            if (supplied.Any(argument => argument == null || string.IsNullOrWhiteSpace(argument.name)))
                Add(diagnostics, "sequence_argument_name_invalid", "steps[" + stepIndex + "].arguments",
                    "Arguments must have names.");

            var suppliedByName = supplied.Where(argument => argument != null && !string.IsNullOrWhiteSpace(argument.name))
                .GroupBy(argument => argument.name, StringComparer.Ordinal).ToArray();
            foreach (IGrouping<string, ActivitySequenceArgument> group in suppliedByName)
                if (group.Count() != 1)
                    Add(diagnostics, "sequence_argument_duplicate", "steps[" + stepIndex + "].arguments." + group.Key,
                        "An argument may be specified only once.");

            foreach (ActivitySequenceArgument argument in supplied.Where(item => item != null))
            {
                ActivityArgumentDescriptor expected = descriptor.arguments.FirstOrDefault(item => item.name == argument.name);
                if (expected == null)
                {
                    Add(diagnostics, "sequence_argument_unknown", "steps[" + stepIndex + "].arguments." + argument.name,
                        "The primitive does not declare this argument.");
                    continue;
                }
                ValidateValue(argument.value, expected, stepIndex, argument.name,
                    availableBindings, bindings, diagnostics);
            }

            foreach (ActivityArgumentDescriptor expected in descriptor.arguments.Where(item => item.required))
                if (!suppliedByName.Any(group => group.Key == expected.name))
                    Add(diagnostics, "sequence_argument_required", "steps[" + stepIndex + "].arguments." + expected.name,
                        "A required primitive argument is missing.");
        }

        private static void ValidateValue(ActivitySequenceValue value, ActivityArgumentDescriptor expected,
            int stepIndex, string argumentName, ISet<string> availableBindings,
            IDictionary<string, BoundActivityResult> bindings, IList<ContentDiagnostic> diagnostics)
        {
            string path = "steps[" + stepIndex + "].arguments." + argumentName;
            if (value == null)
            {
                Add(diagnostics, "sequence_argument_value_missing", path, "An argument must have a literal or result binding.");
                return;
            }
            bool hasLiteral = value.literal != null;
            bool hasBinding = !string.IsNullOrWhiteSpace(value.bindingId);
            if (hasLiteral == hasBinding)
            {
                Add(diagnostics, "sequence_argument_value_ambiguous", path, "An argument must have exactly one value source.");
                return;
            }

            if (hasLiteral)
            {
                if (!string.IsNullOrEmpty(expected.requiredProducerPrimitiveId))
                    Add(diagnostics, "sequence_argument_binding_required", path,
                        "This argument must use a result binding from the required producer primitive.");
                if (value.literal.type != expected.valueType)
                    Add(diagnostics, "sequence_argument_type_mismatch", path, "The literal type does not match the primitive descriptor.");
                if (!IsWithinBounds(value.literal, expected.valueType, expected.minimum, expected.maximum,
                        expected.allowedValues, expected.entityKind))
                    Add(diagnostics, "sequence_argument_value_out_of_range", path, "The literal is outside the primitive's declared bounds.");
                return;
            }

            BoundActivityResult producer;
            if (!bindings.TryGetValue(value.bindingId, out producer))
            {
                Add(diagnostics, "sequence_binding_unavailable", path, "The result binding has no declared producer.");
                return;
            }
            if (!availableBindings.Contains(value.bindingId))
            {
                Add(diagnostics, "sequence_binding_not_guaranteed", path,
                    "The result binding is not produced on every path to this step.");
                return;
            }
            if (producer.result.valueType != expected.valueType)
            {
                Add(diagnostics, "sequence_argument_type_mismatch", path, "The bound result type does not match the primitive descriptor.");
                return;
            }
            if (!string.IsNullOrEmpty(expected.requiredProducerPrimitiveId) &&
                producer.primitiveId != expected.requiredProducerPrimitiveId)
            {
                Add(diagnostics, "sequence_binding_producer_mismatch", path,
                    "The binding was not produced by the required primitive.");
                return;
            }
            if (!string.IsNullOrEmpty(expected.entityKind) && producer.result.entityKind != expected.entityKind)
            {
                Add(diagnostics, "sequence_binding_entity_kind_mismatch", path,
                    "The binding does not carry the required opaque entity kind.");
                return;
            }
            if (!ResultRangeFits(producer.result, expected))
                Add(diagnostics, "sequence_binding_range_not_proven", path,
                    "The producer's declared range does not prove this argument's bounds.");
        }

        private static bool ResultRangeFits(ActivityResultDescriptor produced, ActivityArgumentDescriptor expected)
        {
            if (!string.IsNullOrEmpty(expected.entityKind) && produced.entityKind != expected.entityKind) return false;
            if (expected.minimum.HasValue && (!produced.minimum.HasValue || produced.minimum.Value < expected.minimum.Value)) return false;
            if (expected.maximum.HasValue && (!produced.maximum.HasValue || produced.maximum.Value > expected.maximum.Value)) return false;
            if (expected.allowedValues.Count > 0 &&
                (produced.allowedValues.Count == 0 || produced.allowedValues.Any(value =>
                    !expected.allowedValues.Contains(value, StringComparer.Ordinal)))) return false;
            return true;
        }

        private static bool IsWithinBounds(FlowValue value, string expectedType, double? minimum,
            double? maximum, IReadOnlyCollection<string> allowedValues, string entityKind = "")
        {
            if (value == null || value.type != expectedType) return false;
            if (!string.IsNullOrEmpty(entityKind) &&
                (value.entityValue == null || value.entityValue.kind != entityKind)) return false;
            if (allowedValues.Count > 0 &&
                ((value.type != "enum" && value.type != "text") ||
                 !allowedValues.Contains(value.textValue, StringComparer.Ordinal))) return false;

            double number;
            if (value.type == "double") number = value.doubleValue;
            else if (value.type == "int64") number = value.int64Value;
            else return true;
            if (minimum.HasValue && number < minimum.Value) return false;
            if (maximum.HasValue && number > maximum.Value) return false;
            return true;
        }

        private static void ValidateResultBindings(ActivitySequenceStep step, ActivityPrimitiveDescriptor descriptor,
            int stepIndex, IDictionary<string, BoundActivityResult> bindings, IList<ContentDiagnostic> diagnostics)
        {
            ActivitySequenceResultBinding[] resultBindings = step.resultBindings == null
                ? new ActivitySequenceResultBinding[0] : step.resultBindings.ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var bindingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActivitySequenceResultBinding binding in resultBindings)
            {
                if (binding == null || !ActivitySequenceIdentifiers.IsValid(binding.resultName) ||
                    !ActivitySequenceIdentifiers.IsValid(binding.bindingId))
                {
                    Add(diagnostics, "sequence_result_binding_invalid", "steps[" + stepIndex + "].resultBindings",
                        "Result bindings require valid result and binding IDs.");
                    continue;
                }
                bool unique = names.Add(binding.resultName) && bindingIds.Add(binding.bindingId) && !bindings.ContainsKey(binding.bindingId);
                if (!unique)
                    Add(diagnostics, "sequence_result_binding_duplicate", "steps[" + stepIndex + "].resultBindings." + binding.resultName,
                        "Result and binding IDs must be unique in the sequence.");

                ActivityResultDescriptor result = descriptor.results.FirstOrDefault(item => item.name == binding.resultName);
                if (result == null)
                    Add(diagnostics, "sequence_result_unknown", "steps[" + stepIndex + "].resultBindings." + binding.resultName,
                        "The primitive does not declare this result.");
                else if (unique)
                    bindings.Add(binding.bindingId, new BoundActivityResult(step.primitiveId, result));
            }
        }

        private static void ValidateFurnitureArguments(ActivitySequenceStep step, int stepIndex,
            IList<ContentDiagnostic> diagnostics)
        {
            if (step.primitiveId != ActivityPrimitiveIds.InvokeFurniture) return;
            ActivitySequenceArgument functionArgument = step.arguments.FirstOrDefault(item => item != null && item.name == "functionId");
            if (functionArgument == null || functionArgument.value == null || functionArgument.value.literal == null ||
                functionArgument.value.literal.type != "enum")
            {
                Add(diagnostics, "sequence_furniture_function_id_literal_required",
                    "steps[" + stepIndex + "].arguments.functionId",
                    "The registered furniture function must be selected as a literal enum.");
                return;
            }

            string functionId = functionArgument.value.literal.textValue;
            bool hasDesiredState = step.arguments.Any(item => item != null && item.name == "desiredState");
            if (functionId == "power.set" && !hasDesiredState)
                Add(diagnostics, "sequence_furniture_argument_required", "steps[" + stepIndex + "].arguments.desiredState",
                    "power.set requires a desiredState enum.");
            else if (functionId == "dispenser.collect_mock" && hasDesiredState)
                Add(diagnostics, "sequence_furniture_argument_unexpected", "steps[" + stepIndex + "].arguments.desiredState",
                    "dispenser.collect_mock does not accept function arguments.");
        }

        private static void ValidateTransitions(ActivitySequenceStep step, int stepIndex,
            IDictionary<string, int> stepIndices, IList<ContentDiagnostic> diagnostics)
        {
            string[] targets = { step.successNextStepId, step.failureNextStepId, step.cancelNextStepId };
            foreach (string target in targets.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                int targetIndex;
                if (!stepIndices.TryGetValue(target, out targetIndex))
                    Add(diagnostics, "sequence_transition_target_missing", "steps[" + stepIndex + "].transition",
                        "A transition target does not exist.");
                else if (targetIndex <= stepIndex)
                    Add(diagnostics, "sequence_transition_not_forward", "steps[" + stepIndex + "].transition",
                        "Sequence transitions must move forward; loops and recursion are not supported.");
            }
        }

        private static void ValidateReachability(IList<ActivitySequenceStep> steps,
            IDictionary<string, int> stepIndices, IList<ContentDiagnostic> diagnostics)
        {
            if (steps.Count == 0 || steps[0] == null || !ActivitySequenceIdentifiers.IsValid(steps[0].stepId)) return;
            var reachable = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                int index = pending.Pop();
                if (!reachable.Add(index) || index < 0 || index >= steps.Count || steps[index] == null) continue;
                ActivitySequenceStep step = steps[index];
                string[] targets = { step.successNextStepId, step.failureNextStepId, step.cancelNextStepId };
                foreach (string target in targets.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    int targetIndex;
                    if (stepIndices.TryGetValue(target, out targetIndex)) pending.Push(targetIndex);
                }
            }

            for (int index = 0; index < steps.Count; index++)
                if (!reachable.Contains(index))
                    Add(diagnostics, "sequence_step_unreachable", "steps[" + index + "].stepId",
                        "Every sequence step must be reachable from the entry step.");
        }

        private static void Add(IList<ContentDiagnostic> diagnostics, string code, string path, string message)
        {
            diagnostics.Add(new ContentDiagnostic(code, path, message));
        }
    }
}
