using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Content
{
    /// <summary>Resolves one validated typed binding from the current invocation frame and host snapshot.</summary>
    public static class FlowBindingEvaluator
    {
        public static ContentResult<FlowValue> Evaluate(ContentPackage package, FlowRecord flow, StepRecord targetStep,
            StepBindingRecord binding, IDictionary<string, FlowValue> locals, IDictionary<string, FlowValue> inputs,
            IDictionary<string, FlowValue> actionResults, ContentHostVocabulary vocabulary, ScenarioRuntimeSnapshot context)
        {
            if (package == null || flow == null || targetStep == null || binding == null ||
                binding.stepId != targetStep.id || context == null || vocabulary == null)
                return Failure("flow_binding_context_missing", "The package, exact Step binding and host context are required.");
            string expectedType = ContentFlowAdapter.GetBindingDestinationType(package, flow, targetStep, binding.destinationName);
            if (string.IsNullOrEmpty(expectedType)) return Failure("flow_binding_destination_missing", "The destination is not declared by this Action or Subflow call.");

            FlowValue value = null;
            if (binding.sourceKind == "local")
            {
                if (locals == null || !locals.TryGetValue(binding.sourceName, out value))
                    return Failure("flow_binding_local_missing", "The caller invocation has no value for the declared local.");
            }
            else if (binding.sourceKind == "input")
            {
                if (inputs == null || !inputs.TryGetValue(binding.sourceName, out value))
                    return Failure("flow_binding_input_missing", "The current Flow invocation has no value for the declared input.");
            }
            else if (binding.sourceKind == "literal")
            {
                ContentResult<FlowValue> parsed = FlowValue.ParseJson(binding.literalJson, expectedType, vocabulary, context);
                if (!parsed.IsReady) return Failure(parsed);
                value = parsed.value;
            }
            else if (binding.sourceKind == "host_fact")
            {
                bool fact;
                if (!vocabulary.IsContextFactAllowed(binding.hostFactId) || context.hostFacts == null ||
                    !context.hostFacts.TryGetValue(binding.hostFactId, out fact))
                    return Failure("flow_binding_host_fact_unknown", "The host fact is unavailable or not allowlisted.");
                value = FlowValue.Boolean(fact);
            }
            else if (binding.sourceKind == "result")
            {
                string key = ResultKey(binding.resultStepId, binding.resultName);
                if (actionResults == null || !actionResults.TryGetValue(key, out value))
                    return Failure("flow_binding_result_missing", "The earlier Action result has not been supplied by the host.");
            }
            else return Failure("flow_binding_source_unsupported", "The binding source kind is unsupported.");

            if (value == null || value.type != expectedType)
                return Failure("flow_binding_type_mismatch", "The resolved binding value differs from the destination type.");
            if (!value.IsAvailableIn(context))
                return Failure("flow_binding_entity_unavailable", "The resolved entity is no longer present in the current host snapshot.");
            return ContentResult<FlowValue>.Ready(value);
        }

        internal static string ResultKey(string stepId, string resultName)
        { return (stepId ?? string.Empty) + "\0" + (resultName ?? string.Empty); }

        private static ContentResult<FlowValue> Failure(ContentResult<FlowValue> source)
        {
            ContentDiagnostic diagnostic = source.diagnostics.FirstOrDefault();
            return ContentResult<FlowValue>.Failure(source.status,
                diagnostic == null ? "flow_binding_value_invalid" : diagnostic.code,
                diagnostic == null ? "flow_binding" : diagnostic.path,
                diagnostic == null ? "A typed binding could not be resolved." : diagnostic.message);
        }

        private static ContentResult<FlowValue> Failure(string code, string message)
        { return ContentResult<FlowValue>.Failure(ContentStatuses.Invalid, code, "flow_binding", message); }
    }
}
