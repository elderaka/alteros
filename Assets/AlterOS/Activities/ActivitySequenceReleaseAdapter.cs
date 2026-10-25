using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using Newtonsoft.Json.Linq;

namespace AlterOS.Activities
{
    /// <summary>Builds the runtime sequence from the normalized Action rows in a pinned release.</summary>
    public static class ActivitySequenceReleaseAdapter
    {
        public static ActivitySequenceBuildResult Build(ActionRecord action, ActivitySequenceBuilder builder)
        {
            if (action == null || builder == null || action.implementationKind != "activity_sequence" ||
                string.IsNullOrWhiteSpace(action.id) || string.IsNullOrWhiteSpace(action.revision) ||
                action.sequenceSteps == null || action.sequenceSteps.Count == 0 || action.sequenceSteps.Count > ActivitySequenceValidator.MaximumStepCount)
                return Invalid("released_action_invalid", "action", "A pinned activity_sequence Action with normalized rows is required.");

            var steps = new List<ActivitySequenceStep>();
            ActionSequenceStepRecord[] rows = action.sequenceSteps.OrderBy(item => item == null ? -1 : item.ordinal).ToArray();
            for (int index = 0; index < rows.Length; index++)
            {
                ActionSequenceStepRecord row = rows[index];
                if (row == null || row.ordinal != index)
                    return Invalid("released_sequence_order_invalid", "sequenceSteps", "Normalized Action rows must have contiguous ordinals.");
                try
                {
                    List<ActivitySequenceArgument> arguments = ReadArguments(row.argumentsJson, "argumentsJson");
                    List<ActivitySequenceResultBinding> outputs = ReadOutputs(row.outputsJson);
                    string success = ReadEdge(row.successEdgeJson);
                    string failure = ReadEdge(row.failureEdgeJson);
                    string cancel = ReadEdge(row.cancelEdgeJson);
                    ActivitySequenceCheckpointPolicy checkpoint = ReadCheckpoint(row.checkpointPolicy);
                    steps.Add(new ActivitySequenceStep(row.stepId, row.primitiveId, arguments, outputs,
                        success, failure, cancel, checkpoint));
                }
                catch (Exception exception)
                {
                    return Invalid("released_sequence_row_invalid", "sequenceSteps[" + index + "]", exception.Message);
                }
            }

            return builder.Build(new ActivitySequenceSource
            {
                id = action.id,
                revision = action.revision,
                steps = steps
            });
        }

        private static List<ActivitySequenceArgument> ReadArguments(string json, string path)
        {
            JArray array = ReadArray(json, path);
            var result = new List<ActivitySequenceArgument>();
            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null || item.Count != 2 || item["name"] == null || item["value"] == null)
                    throw new FormatException("A normalized argument must contain exactly name and value.");
                string name = ReadString(item["name"]);
                JObject value = item["value"] as JObject;
                if (value == null) throw new FormatException("A normalized argument value must be an object.");
                if (value.Count == 1 && value["bindingId"] != null)
                {
                    result.Add(ActivitySequenceArgument.FromBinding(name, ReadString(value["bindingId"])));
                    continue;
                }
                if (value.Count != 2 || value["type"] == null || value["literal"] == null)
                    throw new FormatException("A normalized argument must contain one typed literal or binding.");
                string type = ReadString(value["type"]);
                ContentResult<FlowValue> parsed = type == "enum" && value["literal"].Type == JTokenType.String
                    ? ContentResult<FlowValue>.Ready(FlowValue.Enum((string)value["literal"]))
                    : FlowValue.ParseJson(value["literal"].ToString(Newtonsoft.Json.Formatting.None), type, null, null);
                if (!parsed.IsReady) throw new FormatException("A normalized typed argument literal is invalid: " + type + ".");
                result.Add(ActivitySequenceArgument.FromLiteral(name, parsed.value));
            }
            return result;
        }

        private static List<ActivitySequenceResultBinding> ReadOutputs(string json)
        {
            JArray array = ReadArray(json, "outputsJson");
            var result = new List<ActivitySequenceResultBinding>();
            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null || item.Count != 2 || item["resultName"] == null || item["bindingId"] == null)
                    throw new FormatException("A normalized output binding must contain exactly resultName and bindingId.");
                result.Add(new ActivitySequenceResultBinding(ReadString(item["resultName"]), ReadString(item["bindingId"])));
            }
            return result;
        }

        private static string ReadEdge(string json)
        {
            JObject edge = ContentJson.Parse(json ?? "{}") as JObject;
            if (edge == null || edge.Count != 1 || edge["nextStepId"] == null)
                throw new FormatException("A normalized sequence edge must contain exactly nextStepId.");
            return ReadString(edge["nextStepId"]);
        }

        private static JArray ReadArray(string json, string field)
        {
            JArray result = ContentJson.Parse(json ?? "") as JArray;
            if (result == null) throw new FormatException("Normalized " + field + " must be an array.");
            return result;
        }

        private static ActivitySequenceCheckpointPolicy ReadCheckpoint(string value)
        {
            if (value == "none") return ActivitySequenceCheckpointPolicy.None;
            if (value == "before") return ActivitySequenceCheckpointPolicy.Before;
            if (value == "after") return ActivitySequenceCheckpointPolicy.After;
            throw new FormatException("Normalized checkpoint policy is unsupported.");
        }

        private static string ReadString(JToken token)
        {
            if (token == null || token.Type != JTokenType.String) throw new FormatException("A normalized string value is invalid.");
            return (string)token;
        }

        private static ActivitySequenceBuildResult Invalid(string code, string path, string message)
        {
            return new ActivitySequenceBuildResult(false, null,
                new[] { new ContentDiagnostic(code, path, message) });
        }
    }
}
