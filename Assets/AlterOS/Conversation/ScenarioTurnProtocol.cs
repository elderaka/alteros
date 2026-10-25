using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AlterOS.Memory;
using AlterOS.Scenarios;

namespace AlterOS.Conversation
{
    public sealed class ScenarioProtocolMessage
    {
        public readonly int protocolVersion;
        public readonly string type, requestId;
        public readonly object payload;
        internal ScenarioProtocolMessage(string messageType, string id, object body)
        { protocolVersion = 4; type = messageType; requestId = id; payload = body; }
    }

    public sealed class ScenarioReadyV4
    {
        public readonly string runtimeVersion, modelId;
        public readonly bool memoryAvailable, generationAvailable, scenarioTurnAvailable;
        internal ScenarioReadyV4(string version, string model, bool memory, bool generation, bool scenarioTurn)
        { runtimeVersion = version; modelId = model; memoryAvailable = memory; generationAvailable = generation; scenarioTurnAvailable = scenarioTurn; }
    }

    public sealed class ScenarioErrorV4
    {
        public readonly string code;
        public readonly bool retryable;
        internal ScenarioErrorV4(string errorCode, bool canRetry) { code = errorCode; retryable = canRetry; }
    }

    public sealed class ScenarioUsage
    {
        public readonly int inputTokens, outputTokens, totalTokens;
        public readonly double? costUsd;
        internal ScenarioUsage(int input, int output, int total, double? cost)
        { inputTokens = input; outputTokens = output; totalTokens = total; costUsd = cost; }
    }

    public sealed class ScenarioStageResult
    {
        public readonly string requestId, turnId, stageId, stage, status, segmentStepId;
        public readonly int turnVersion;
        public readonly ScenarioUsage usage;
        public readonly List<ScenarioBeat> beats;
        public readonly ScenarioDefinition scenario;
        public readonly List<ScenarioTopicOffer> topicOffers;
        public readonly List<PlayerMemoryProposal> memoryProposals;
        public readonly string questionProposal;
        public readonly List<string> usedCallbackIds;

        internal ScenarioStageResult(string parent, string turn, int version, string id, string stageKind,
            string state, ScenarioUsage measuredUsage, IEnumerable<ScenarioBeat> opening,
            ScenarioDefinition definition, string segmentId, IEnumerable<ScenarioTopicOffer> offers,
            IEnumerable<PlayerMemoryProposal> proposals = null, string question = "", IEnumerable<string> callbacks = null)
        {
            requestId = parent; turnId = turn; turnVersion = version; stageId = id; stage = stageKind;
            status = state; usage = measuredUsage; beats = new List<ScenarioBeat>(opening ?? new ScenarioBeat[0]);
            scenario = definition; segmentStepId = segmentId ?? "";
            topicOffers = new List<ScenarioTopicOffer>(offers ?? new ScenarioTopicOffer[0]);
            memoryProposals = new List<PlayerMemoryProposal>(proposals ?? new PlayerMemoryProposal[0]);
            questionProposal = question ?? ""; usedCallbackIds = new List<string>(callbacks ?? new string[0]);
        }
    }

    public sealed class ScenarioStageSummary
    {
        public readonly string stage, status;
        public readonly ScenarioUsage usage;
        internal ScenarioStageSummary(string stageKind, string state, ScenarioUsage measuredUsage)
        { stage = stageKind; status = state; usage = measuredUsage; }
    }

    public sealed class ScenarioTurnReceipt
    {
        public readonly string requestId, turnId, terminalReason;
        public readonly int turnVersion;
        public readonly ScenarioStageSummary head, main;
        internal ScenarioTurnReceipt(string request, string turn, int version, ScenarioStageSummary headSummary,
            ScenarioStageSummary mainSummary, string reason)
        { requestId = request; turnId = turn; turnVersion = version; head = headSummary; main = mainSummary; terminalReason = reason; }
    }

    public sealed class ScenarioAnswer
    {
        public readonly string questionId, answerId, freeText;
        internal ScenarioAnswer(string question, string option, string text)
        { questionId = question; answerId = option; freeText = text; }
    }

    public sealed class ScenarioTurnJobV4
    {
        public readonly string turnId, operation;
        public readonly int turnVersion;
        public readonly bool onlineEnabled;
        public readonly ConversationJobV2 conversation;
        public readonly PlayerMemoryTurnV3 memory;
        public readonly ScenarioRuntimeSnapshot runtime;
        public readonly ScenarioAnswer answer;
        internal ScenarioTurnJobV4(string id, int version, string operationKind, bool online, ConversationJobV2 job,
            PlayerMemoryTurnV3 memoryTurn, ScenarioRuntimeSnapshot snapshot, ScenarioAnswer answerData)
        { turnId = id; turnVersion = version; operation = operationKind; onlineEnabled = online; conversation = job; memory = memoryTurn; runtime = snapshot; answer = answerData; }
    }

    public static class ScenarioTurnProtocol
    {
        public const int Version = 4;
        public const int MaximumEnvelopeBytes = 256 * 1024;
        public const int MaximumResultBytes = 128 * 1024;
        public const int MaximumTurnBytes = 64 * 1024;

        public static string SerializeBootstrapEnvelope(string requestId, string apiKey, string playerId, string memoryRoot)
        {
            RequireBounded(requestId, 128, "requestId");
            if (apiKey == null || apiKey.Length > 4096) throw new FormatException("Bootstrap key is invalid.");
            RequireBounded(playerId, 128, "playerId");
            if (string.IsNullOrWhiteSpace(memoryRoot) || !Path.IsPathRooted(memoryRoot)) throw new FormatException("Memory root must be absolute.");
            string fullRoot = Path.GetFullPath(memoryRoot);
            if (!string.Equals(Path.GetFileName(fullRoot), "player-memory.sqlite3", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(fullRoot)), "Memory", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Memory root is outside the host-owned layout.");
            return "{\"protocolVersion\":4,\"type\":\"bootstrap\",\"requestId\":" + ConversationProtocol.JsonString(requestId) +
                ",\"payload\":{\"apiKey\":" + ConversationProtocol.JsonString(apiKey) + ",\"playerId\":" +
                ConversationProtocol.JsonString(playerId) + ",\"memoryRoot\":" + ConversationProtocol.JsonString(fullRoot) + "}}";
        }

        public static string SerializeControlEnvelope(string type, string requestId)
        {
            if (type != "cancel" && type != "shutdown") throw new ArgumentException("Unsupported Scenario control type.", nameof(type));
            RequireBounded(requestId, 128, "requestId");
            return "{\"protocolVersion\":4,\"type\":" + ConversationProtocol.JsonString(type) +
                ",\"requestId\":" + ConversationProtocol.JsonString(requestId) + ",\"payload\":{}}";
        }

        public static string SerializeTurnEnvelope(string requestId, ScenarioTurnJobV4 job)
        {
            RequireBounded(requestId, 128, "requestId");
            if (job == null || job.conversation == null || job.runtime == null || job.turnId != requestId ||
                job.runtime.turnId != job.turnId || job.turnVersion < 1 ||
                (job.operation != "begin" && job.operation != "answer" && job.operation != "continue") ||
                ((job.operation == "answer") != (job.answer != null)))
                throw new FormatException("Scenario turn is invalid or unbound.");
            string memory = job.memory == null ? "null" : PlayerMemoryProtocol.SerializeTurnPolicy(job.memory.memory, job.conversation);
            string answer = job.answer == null ? "null" : "{\"questionId\":" + ConversationProtocol.JsonString(job.answer.questionId) +
                ",\"answerId\":" + ConversationProtocol.JsonString(job.answer.answerId) + ",\"freeText\":" +
                ConversationProtocol.JsonString(job.answer.freeText) + "}";
            string payload = "{\"turnId\":" + ConversationProtocol.JsonString(job.turnId) +
                ",\"turnVersion\":" + job.turnVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"operation\":" + ConversationProtocol.JsonString(job.operation) +
                ",\"onlineEnabled\":" + (job.onlineEnabled ? "true" : "false") +
                ",\"conversation\":" + ConversationProtocol.SerializeJobPayload(job.conversation) +
                ",\"memory\":" + memory + ",\"runtime\":" + SerializeRuntime(job.runtime) + ",\"answer\":" + answer + "}";
            if (Encoding.UTF8.GetByteCount(payload) > MaximumTurnBytes) throw new FormatException("Scenario turn exceeds its byte limit.");
            string envelope = "{\"protocolVersion\":4,\"type\":\"scenario_turn\",\"requestId\":" +
                ConversationProtocol.JsonString(requestId) + ",\"payload\":" + payload + "}";
            if (Encoding.UTF8.GetByteCount(envelope) > MaximumEnvelopeBytes) throw new FormatException("Scenario envelope exceeds its byte limit.");
            ParseEnvelope(envelope);
            return envelope;
        }

        private static string SerializeRuntime(ScenarioRuntimeSnapshot value)
        {
            var builder = new StringBuilder("{\"scenarioId\":").Append(ConversationProtocol.JsonString(value.scenarioId))
                .Append(",\"runId\":").Append(ConversationProtocol.JsonString(value.runId))
                .Append(",\"turnId\":").Append(ConversationProtocol.JsonString(value.turnId))
                .Append(",\"contextRevision\":").Append(value.contextRevision.ToString(CultureInfo.InvariantCulture))
                .Append(",\"checkpointStepId\":").Append(ConversationProtocol.JsonString(value.checkpointStepId))
                .Append(",\"knownTopicIds\":");
            AppendStrings(builder, value.knownTopicIds);
            builder.Append(",\"knownIntentIds\":"); AppendStrings(builder, value.knownIntentIds);
            builder.Append(",\"allowedExpressions\":"); AppendStrings(builder, value.allowedExpressions);
            builder.Append(",\"capabilities\":[");
            for (int i = 0; i < value.capabilities.Count; i++)
            {
                if (i != 0) builder.Append(',');
                ScenarioCapabilitySnapshot capability = value.capabilities[i];
                builder.Append("{\"capabilityId\":").Append(ConversationProtocol.JsonString(capability.capabilityId)).Append(",\"argumentBindings\":[");
                for (int j = 0; j < capability.argumentBindings.Count; j++)
                {
                    if (j != 0) builder.Append(',');
                    ScenarioArgumentBinding binding = capability.argumentBindings[j];
                    builder.Append("{\"name\":").Append(ConversationProtocol.JsonString(binding.name)).Append(",\"values\":");
                    AppendStrings(builder, binding.values); builder.Append('}');
                }
                builder.Append("]}");
            }
            builder.Append("],\"objects\":[");
            for (int i = 0; i < value.objects.Count; i++)
            {
                if (i != 0) builder.Append(',');
                builder.Append("{\"objectId\":").Append(ConversationProtocol.JsonString(value.objects[i].objectId))
                    .Append(",\"kind\":").Append(ConversationProtocol.JsonString(value.objects[i].kind)).Append('}');
            }
            builder.Append("],\"waitConditions\":"); AppendStrings(builder, value.waitConditions); return builder.Append('}').ToString();
        }

        private static void AppendStrings(StringBuilder builder, IEnumerable<string> values)
        {
            builder.Append('['); bool first = true;
            foreach (string value in values) { if (!first) builder.Append(','); first = false; builder.Append(ConversationProtocol.JsonString(value)); }
            builder.Append(']');
        }

        private static void RequireBounded(string value, int maximum, string name)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new FormatException("Scenario " + name + " is invalid."); }

        public static ScenarioProtocolMessage ParseEnvelope(string json)
        {
            XmlElement root = ConversationContractValidator.ReadRoot(json, MaximumEnvelopeBytes);
            Dictionary<string, XmlElement> envelope = ConversationContractValidator.ReadObject(root,
                new[] { "protocolVersion", "type", "requestId", "payload" });
            if (ConversationContractValidator.ReadInt(envelope["protocolVersion"]) != Version)
                throw new FormatException("Unsupported Scenario protocol version.");
            string type = ReadText(envelope["type"], 32);
            string requestId = ReadText(envelope["requestId"], 128);
            XmlElement payload = envelope["payload"];
            int messageBytes = EncodingByteCount(json);
            if (messageBytes > MaximumEnvelopeBytes) throw new FormatException("Scenario protocol message exceeds its byte limit.");
            if (type == "scenario_turn" && messageBytes > MaximumTurnBytes) throw new FormatException("Scenario turn exceeds its byte limit.");
            if ((type == "scenario_stage" || type == "scenario_complete") && messageBytes > MaximumResultBytes)
                throw new FormatException("Scenario result exceeds its byte limit.");
            object parsed;
            switch (type)
            {
                case "ready": parsed = ParseReady(payload); break;
                case "error": parsed = ParseError(payload); break;
                case "scenario_turn": parsed = ParseTurn(requestId, payload); break;
                case "scenario_stage": parsed = ParseStage(requestId, payload); break;
                case "scenario_complete": parsed = ParseComplete(requestId, payload); break;
                default: throw new FormatException("Unsupported Scenario protocol message type.");
            }
            return new ScenarioProtocolMessage(type, requestId, parsed);
        }

        private static ScenarioReadyV4 ParseReady(XmlElement payload)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(payload, new[] { "runtimeVersion", "modelId", "capabilities" });
            Dictionary<string, XmlElement> caps = ConversationContractValidator.ReadObject(fields["capabilities"], new[] { "memory", "generation", "scenario-turn-v1" });
            return new ScenarioReadyV4(ReadText(fields["runtimeVersion"], 64), ReadText(fields["modelId"], 128, true),
                ConversationContractValidator.ReadBoolean(caps["memory"]), ConversationContractValidator.ReadBoolean(caps["generation"]),
                ConversationContractValidator.ReadBoolean(caps["scenario-turn-v1"]));
        }

        private static ScenarioErrorV4 ParseError(XmlElement payload)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(payload, new[] { "code", "retryable" });
            string code = ReadText(fields["code"], 64);
            if (code != "invalid_job" && code != "schema_unsupported" && code != "provider_auth" && code != "provider_payment_required" &&
                code != "provider_rate_limited" && code != "provider_unavailable" && code != "provider_timeout" &&
                code != "provider_refusal" && code != "persona_unavailable" && code != "memory_unavailable" &&
                code != "invalid_reply" && code != "internal_error") throw new FormatException("Unsupported Scenario error code.");
            return new ScenarioErrorV4(code, ConversationContractValidator.ReadBoolean(fields["retryable"]));
        }

        private static ScenarioStageResult ParseStage(string requestId, XmlElement payload)
        {
            Dictionary<string, XmlElement> common = ConversationContractValidator.ReadObject(payload,
                new[] { "turnId", "turnVersion", "stageId", "stage", "status", "usage" },
                new[] { "beats", "scenario", "segmentStepId", "topicOffers", "memoryProposals", "questionProposal", "usedCallbackIds" });
            string turnId = ReadText(common["turnId"], 128);
            int version = ReadPositive(ConversationContractValidator.ReadInt(common["turnVersion"]), "turnVersion");
            if (!string.Equals(turnId, requestId, StringComparison.Ordinal)) throw new FormatException("Scenario stage is not bound to its parent turn.");
            string stage = ReadText(common["stage"], 16);
            string status = ReadStatus(common["status"]);
            ScenarioUsage usage = ParseUsage(common["usage"]);
            ScenarioStageResult result;
            if (stage == "head")
            {
                RequireExact(common, new[] { "turnId", "turnVersion", "stageId", "stage", "status", "usage", "beats" });
                if (common.ContainsKey("scenario") || common.ContainsKey("segmentStepId") || common.ContainsKey("topicOffers"))
                    throw new FormatException("Head cannot contain Scenario actions or offers.");
                List<ScenarioBeat> beats = ParseBeats(ConversationContractValidator.Required(common, "beats"), ScenarioDefinitionValidator.MaximumHeadBeats);
                if (common.ContainsKey("memoryProposals") || common.ContainsKey("questionProposal") || common.ContainsKey("usedCallbackIds"))
                    throw new FormatException("Head stage cannot contain memory metadata.");
                result = new ScenarioStageResult(requestId, turnId, version, ReadText(common["stageId"], 128), stage, status,
                    usage, beats, null, "", null);
            }
            else if (stage == "main")
            {
                RequirePresent(common, new[] { "turnId", "turnVersion", "stageId", "stage", "status", "usage", "scenario", "segmentStepId", "topicOffers" });
                if (common.ContainsKey("beats")) throw new FormatException("Main stage uses a Scenario segment, not head beats.");
                ScenarioDefinition scenario = ParseDefinition(ConversationContractValidator.Required(common, "scenario"));
                string segmentId = ReadText(ConversationContractValidator.Required(common, "segmentStepId"), 64);
                List<ScenarioTopicOffer> offers = ParseOffers(common["topicOffers"]);
                List<PlayerMemoryProposal> proposals = common.ContainsKey("memoryProposals")
                    ? ParseMemoryProposals(common["memoryProposals"]) : new List<PlayerMemoryProposal>();
                string question = common.ContainsKey("questionProposal") && ConversationContractValidator.TypeOf(common["questionProposal"]) != "null"
                    ? ReadText(common["questionProposal"], 64) : "";
                if (question != "" && question != "preferred_name" && question != "interest" && question != "shared_activity_preference")
                    throw new FormatException("Scenario memory question intent is unsupported.");
                List<string> callbacks = common.ContainsKey("usedCallbackIds")
                    ? ConversationContractValidator.ReadStringArray(common["usedCallbackIds"], 1) : new List<string>();
                if (callbacks.Distinct(StringComparer.Ordinal).Count() != callbacks.Count)
                    throw new FormatException("Scenario memory callback IDs must be unique.");
                result = new ScenarioStageResult(requestId, turnId, version, ReadText(common["stageId"], 128), stage, status,
                    usage, null, scenario, segmentId, offers, proposals, question, callbacks);
                if (ScenarioDefinitionValidator.ValidateStage(result, null).Count != 0)
                    throw new FormatException("Main Scenario stage is invalid.");
            }
            else throw new FormatException("Unsupported Scenario stage kind.");
            return result;
        }

        private static ScenarioTurnJobV4 ParseTurn(string requestId, XmlElement payload)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(payload,
                new[] { "turnId", "turnVersion", "operation", "onlineEnabled", "conversation", "runtime" },
                new[] { "memory", "answer" });
            string turnId = ReadText(fields["turnId"], 128);
            if (!string.Equals(turnId, requestId, StringComparison.Ordinal)) throw new FormatException("Scenario turn is not bound to its request.");
            int version = ReadPositive(ConversationContractValidator.ReadInt(fields["turnVersion"]), "turnVersion");
            string operation = ReadText(fields["operation"], 16);
            if (operation != "begin" && operation != "answer" && operation != "continue") throw new FormatException("Unsupported Scenario turn operation.");
            ConversationJobV2 conversation = ConversationProtocol.ParseJobPayload(fields["conversation"]);
            ScenarioRuntimeSnapshot runtime = ParseRuntime(fields["runtime"]);
            if (ScenarioDefinitionValidator.ValidateRuntime(runtime).Count != 0) throw new FormatException("Scenario runtime snapshot is invalid.");
            if (runtime.turnId != turnId) throw new FormatException("Scenario runtime snapshot belongs to another turn.");
            PlayerMemoryTurnV3 memory = fields.ContainsKey("memory") && ConversationContractValidator.TypeOf(fields["memory"]) != "null"
                ? ParseMemoryTurn(fields["memory"], conversation) : null;
            ScenarioAnswer answer = fields.ContainsKey("answer") && ConversationContractValidator.TypeOf(fields["answer"]) != "null"
                ? ParseAnswer(fields["answer"]) : null;
            if ((operation == "answer") != (answer != null)) throw new FormatException("Scenario answer does not match the operation.");
            return new ScenarioTurnJobV4(turnId, version, operation, ConversationContractValidator.ReadBoolean(fields["onlineEnabled"]),
                conversation, memory, runtime, answer);
        }

        private static ScenarioTurnReceipt ParseComplete(string requestId, XmlElement payload)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(payload,
                new[] { "turnId", "turnVersion", "head", "main", "terminalReason" });
            string turnId = ReadText(fields["turnId"], 128);
            if (!string.Equals(turnId, requestId, StringComparison.Ordinal)) throw new FormatException("Scenario receipt is not bound to its turn.");
            int version = ReadPositive(ConversationContractValidator.ReadInt(fields["turnVersion"]), "turnVersion");
            string reason = ReadText(fields["terminalReason"], 32);
            if (reason != "completed" && reason != "cancelled" && reason != "superseded" && reason != "timed_out" && reason != "failed")
                throw new FormatException("Unsupported Scenario terminal reason.");
            ScenarioStageSummary head = ParseSummary(fields["head"], "head");
            ScenarioStageSummary main = ParseSummary(fields["main"], "main");
            return new ScenarioTurnReceipt(requestId, turnId, version, head, main, reason);
        }

        private static ScenarioStageSummary ParseSummary(XmlElement element, string expectedStage)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "stage", "status", "usage" });
            string stage = ReadText(fields["stage"], 16);
            if (stage != expectedStage) throw new FormatException("Scenario stage summary is mislabelled.");
            string status = ReadText(fields["status"], 16);
            if (status != "ready" && status != "failed" && status != "cancelled" && status != "skipped")
                throw new FormatException("Unsupported Scenario stage summary status.");
            return new ScenarioStageSummary(stage, status, ParseUsage(fields["usage"]));
        }

        private static ScenarioDefinition ParseDefinition(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "scenarioId", "topicId", "entryStepId", "steps" });
            var definition = new ScenarioDefinition
            {
                scenarioId = ReadText(fields["scenarioId"], 128, true),
                topicId = ReadText(fields["topicId"], 128, true),
                entryStepId = ReadText(fields["entryStepId"], 64)
            };
            foreach (XmlElement item in ConversationContractValidator.ReadArray(fields["steps"], ScenarioDefinitionValidator.MaximumSteps))
                definition.steps.Add(ParseStep(item));
            if (ScenarioDefinitionValidator.Validate(definition, null).Count != 0) throw new FormatException("Scenario definition is invalid.");
            return definition;
        }

        private static ScenarioStep ParseStep(XmlElement element)
        {
            Dictionary<string, XmlElement> kindFields = ConversationContractValidator.ReadObject(element,
                new[] { "id", "kind" }, new[] { "beats", "nextStepId", "closeExchange", "questionId", "question", "options",
                    "allowFreeText", "freeTextNextStepId", "skipNextStepId", "cancelNextStepId", "capabilityId", "arguments",
                    "successNextStepId", "failureNextStepId", "activityId", "conditionId", "objective", "sourceId", "cases",
                    "defaultNextStepId", "outcome" });
            string kind = ReadText(kindFields["kind"], 16);
            ScenarioStep step = new ScenarioStep { id = ReadText(kindFields["id"], 64), kind = kind };
            switch (kind)
            {
                case "dialogue":
                    RequireExact(kindFields, new[] { "id", "kind", "beats", "nextStepId", "closeExchange" });
                    step.beats = ParseBeats(kindFields["beats"], ScenarioDefinitionValidator.MaximumDialogueBeats);
                    step.nextStepId = ReadText(kindFields["nextStepId"], 64);
                    step.closeExchange = ConversationContractValidator.ReadBoolean(kindFields["closeExchange"]);
                    break;
                case "ask":
                    RequireExact(kindFields, new[] { "id", "kind", "questionId", "question", "options", "allowFreeText", "freeTextNextStepId", "skipNextStepId", "cancelNextStepId" });
                    step.questionId = ReadText(kindFields["questionId"], 64);
                    step.question = ParseBeat(kindFields["question"]);
                    foreach (XmlElement option in ConversationContractValidator.ReadArray(kindFields["options"], ScenarioDefinitionValidator.MaximumQuestionOptions))
                    {
                        Dictionary<string, XmlElement> optionFields = ConversationContractValidator.ReadObject(option,
                            new[] { "optionId", "label", "value", "nextStepId" });
                        step.options.Add(new ScenarioQuestionOption
                        {
                            optionId = ReadText(optionFields["optionId"], 64), label = ReadText(optionFields["label"], 80),
                            value = ReadText(optionFields["value"], 256), nextStepId = ReadText(optionFields["nextStepId"], 64)
                        });
                    }
                    step.allowFreeText = ConversationContractValidator.ReadBoolean(kindFields["allowFreeText"]);
                    step.freeTextNextStepId = ReadText(kindFields["freeTextNextStepId"], 64, !step.allowFreeText);
                    step.skipNextStepId = ReadText(kindFields["skipNextStepId"], 64);
                    step.cancelNextStepId = ReadText(kindFields["cancelNextStepId"], 64);
                    break;
                case "action":
                    RequireExact(kindFields, new[] { "id", "kind", "capabilityId", "arguments", "successNextStepId", "failureNextStepId" });
                    step.capabilityId = ReadText(kindFields["capabilityId"], 128);
                    step.arguments = ParseArguments(kindFields["arguments"]);
                    step.successNextStepId = ReadText(kindFields["successNextStepId"], 64);
                    step.failureNextStepId = ReadText(kindFields["failureNextStepId"], 64);
                    break;
                case "wait":
                    RequireExact(kindFields, new[] { "id", "kind", "activityId", "conditionId", "objective", "successNextStepId", "failureNextStepId" });
                    step.activityId = ReadText(kindFields["activityId"], 128); step.conditionId = ReadText(kindFields["conditionId"], 128);
                    step.objective = ReadText(kindFields["objective"], 240); step.successNextStepId = ReadText(kindFields["successNextStepId"], 64);
                    step.failureNextStepId = ReadText(kindFields["failureNextStepId"], 64);
                    break;
                case "branch":
                    RequireExact(kindFields, new[] { "id", "kind", "sourceId", "cases", "defaultNextStepId" });
                    step.sourceId = ReadText(kindFields["sourceId"], 64); step.defaultNextStepId = ReadText(kindFields["defaultNextStepId"], 64);
                    foreach (XmlElement branch in ConversationContractValidator.ReadArray(kindFields["cases"], 16))
                    {
                        Dictionary<string, XmlElement> branchFields = ConversationContractValidator.ReadObject(branch, new[] { "value", "nextStepId" });
                        step.cases.Add(new ScenarioBranchCase { value = ReadText(branchFields["value"], 128), nextStepId = ReadText(branchFields["nextStepId"], 64) });
                    }
                    break;
                case "end":
                    RequireExact(kindFields, new[] { "id", "kind", "outcome", "closeExchange" });
                    step.outcome = ReadText(kindFields["outcome"], 32);
                    step.closeExchange = ConversationContractValidator.ReadBoolean(kindFields["closeExchange"]);
                    break;
                default: throw new FormatException("Unsupported Scenario step kind.");
            }
            return step;
        }

        private static List<ScenarioActionArgument> ParseArguments(XmlElement element)
        {
            if (ConversationContractValidator.TypeOf(element) != "object") throw new FormatException("Scenario action arguments must be an object.");
            var result = new List<ScenarioActionArgument>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode child in element.ChildNodes)
            {
                XmlElement field = child as XmlElement;
                if (field == null) continue;
                if (!names.Add(field.Name)) throw new FormatException("Duplicate Scenario action argument.");
                if (result.Count == 8) throw new FormatException("Too many Scenario action arguments.");
                result.Add(new ScenarioActionArgument { name = field.Name, value = ReadText(field, 128) });
            }
            return result;
        }

        private static List<ScenarioBeat> ParseBeats(XmlElement element, int maximum)
        {
            var result = new List<ScenarioBeat>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(element, maximum)) result.Add(ParseBeat(item));
            if (result.Count == 0) throw new FormatException("Scenario dialogue requires at least one beat.");
            if (result.Select(beat => beat.id).Distinct(StringComparer.Ordinal).Count() != result.Count)
                throw new FormatException("Scenario beat IDs must be unique.");
            return result;
        }

        private static ScenarioBeat ParseBeat(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "id", "text", "displayText", "language", "expression" });
            string language = ReadText(fields["language"], 32);
            if (language != "japanese") throw new FormatException("Scenario speech language must be japanese.");
            return new ScenarioBeat
            {
                id = ReadText(fields["id"], 64), text = ReadText(fields["text"], 600),
                displayText = ReadText(fields["displayText"], 600), language = language,
                expression = ReadText(fields["expression"], 64, true)
            };
        }

        private static List<ScenarioTopicOffer> ParseOffers(XmlElement element)
        {
            var result = new List<ScenarioTopicOffer>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(element, ScenarioDefinitionValidator.MaximumTopicOffers))
            {
                Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(item,
                    new[] { "offerId", "label", "topicId", "intentId" });
                var offer = new ScenarioTopicOffer
                {
                    offerId = ReadText(fields["offerId"], 64), label = ReadText(fields["label"], 80),
                    topicId = ReadText(fields["topicId"], 128, true), intentId = ReadText(fields["intentId"], 128, true)
                };
                if (string.IsNullOrEmpty(offer.topicId) == string.IsNullOrEmpty(offer.intentId)) throw new FormatException("Topic offer target is ambiguous.");
                result.Add(offer);
            }
            if (result.Select(item => item.offerId).Distinct(StringComparer.Ordinal).Count() != result.Count)
                throw new FormatException("Topic offer IDs must be unique.");
            return result;
        }

        private static List<PlayerMemoryProposal> ParseMemoryProposals(XmlElement element)
        {
            var result = new List<PlayerMemoryProposal>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(element, 3))
            {
                Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(item,
                    new[] { "category", "key", "value", "scope", "sourceId", "evidenceQuote" });
                result.Add(new PlayerMemoryProposal
                {
                    category = ReadText(fields["category"], 64), key = ReadText(fields["key"], 80), value = ReadText(fields["value"], 512),
                    scope = ReadText(fields["scope"], 64), sourceId = ReadText(fields["sourceId"], 128), evidenceQuote = ReadText(fields["evidenceQuote"], 600)
                });
            }
            return result;
        }

        private static ScenarioRuntimeSnapshot ParseRuntime(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "scenarioId", "runId", "turnId", "contextRevision", "checkpointStepId", "knownTopicIds", "knownIntentIds",
                    "allowedExpressions", "capabilities", "objects", "waitConditions" });
            var capabilities = new List<ScenarioCapabilitySnapshot>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(fields["capabilities"], 32))
            {
                Dictionary<string, XmlElement> cap = ConversationContractValidator.ReadObject(item, new[] { "capabilityId", "argumentBindings" });
                var bindings = new List<ScenarioArgumentBinding>();
                foreach (XmlElement bindElement in ConversationContractValidator.ReadArray(cap["argumentBindings"], 8))
                {
                    Dictionary<string, XmlElement> bind = ConversationContractValidator.ReadObject(bindElement, new[] { "name", "values" });
                    bindings.Add(new ScenarioArgumentBinding(ReadText(bind["name"], 64),
                        ConversationContractValidator.ReadStringArray(bind["values"], 32)));
                }
                capabilities.Add(new ScenarioCapabilitySnapshot(ReadText(cap["capabilityId"], 128), bindings));
            }
            var objects = new List<ScenarioObjectSnapshot>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(fields["objects"], 128))
            {
                Dictionary<string, XmlElement> obj = ConversationContractValidator.ReadObject(item, new[] { "objectId", "kind" });
                objects.Add(new ScenarioObjectSnapshot(ReadText(obj["objectId"], 128), ReadText(obj["kind"], 64)));
            }
            return new ScenarioRuntimeSnapshot(ReadText(fields["scenarioId"], 128, true), ReadText(fields["runId"], 128),
                ReadText(fields["turnId"], 128), ReadNonnegative(ConversationContractValidator.ReadInt(fields["contextRevision"]), "contextRevision"),
                ReadText(fields["checkpointStepId"], 64, true), ConversationContractValidator.ReadStringArray(fields["knownTopicIds"], 8),
                ConversationContractValidator.ReadStringArray(fields["allowedExpressions"], 64), capabilities, objects,
                ConversationContractValidator.ReadStringArray(fields["waitConditions"], 64),
                ConversationContractValidator.ReadStringArray(fields["knownIntentIds"], 32));
        }

        private static PlayerMemoryTurnV3 ParseMemoryTurn(XmlElement element, ConversationJobV2 conversation)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "playerId", "consentRevision", "nowUtc", "currentPlayerSourceId", "focusAllowed", "quiet", "explicitRecall",
                    "authorizedQuestionIntent", "memoryPolicyRequired", "presentedHistorySourceIds" });
            string question = ReadText(fields["authorizedQuestionIntent"], 64, true);
            if (question != "" && question != "preferred_name" && question != "interest" && question != "shared_activity_preference")
                throw new FormatException("Scenario memory question intent is unsupported.");
            var policy = new PlayerMemoryTurnPolicyV3(ReadText(fields["playerId"], 128),
                ReadNonnegative(ConversationContractValidator.ReadInt(fields["consentRevision"]), "consentRevision"),
                DateTimeOffset.Parse(ReadText(fields["nowUtc"], 64), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ReadText(fields["currentPlayerSourceId"], 128), ReadNullableBoolean(fields["focusAllowed"]),
                ReadNullableBoolean(fields["quiet"]), ConversationContractValidator.ReadBoolean(fields["explicitRecall"]), question,
                ConversationContractValidator.ReadBoolean(fields["memoryPolicyRequired"]),
                ConversationContractValidator.ReadStringArray(fields["presentedHistorySourceIds"], ConversationProtocol.MaximumHistoryUtterances));
            if (policy.presentedHistorySourceIds.Count != conversation.presentedHistory.Count)
                throw new FormatException("Scenario memory history source IDs are misaligned.");
            return new PlayerMemoryTurnV3(conversation, policy);
        }

        private static ScenarioAnswer ParseAnswer(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "questionId", "answerId", "freeText" });
            string answerId = ReadText(fields["answerId"], 64, true), text = ReadText(fields["freeText"], ConversationContractValidator.MaximumRequestCharacters, true);
            if (string.IsNullOrEmpty(answerId) == string.IsNullOrEmpty(text)) throw new FormatException("Scenario answer must be an option or free text.");
            return new ScenarioAnswer(ReadText(fields["questionId"], 64), answerId, text);
        }

        private static ScenarioUsage ParseUsage(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "inputTokens", "outputTokens", "totalTokens", "costUsd" });
            XmlElement costElement = fields["costUsd"];
            double? cost = ConversationContractValidator.TypeOf(costElement) == "null" ? (double?)null : ConversationContractValidator.ReadDouble(costElement);
            if (cost < 0) throw new FormatException("Scenario cost cannot be negative.");
            int input = ReadNonnegative(ConversationContractValidator.ReadInt(fields["inputTokens"]), "inputTokens");
            int output = ReadNonnegative(ConversationContractValidator.ReadInt(fields["outputTokens"]), "outputTokens");
            int total = ReadNonnegative(ConversationContractValidator.ReadInt(fields["totalTokens"]), "totalTokens");
            if (total < input || total < output) throw new FormatException("Scenario usage totals are inconsistent.");
            return new ScenarioUsage(input, output, total, cost);
        }

        private static bool? ReadNullableBoolean(XmlElement element)
        { return ConversationContractValidator.TypeOf(element) == "null" ? (bool?)null : ConversationContractValidator.ReadBoolean(element); }

        private static string ReadStatus(XmlElement element)
        {
            string status = ReadText(element, 16);
            if (status != "ready" && status != "failed" && status != "cancelled") throw new FormatException("Unsupported Scenario stage status.");
            return status;
        }

        private static void RequireExact(Dictionary<string, XmlElement> fields, string[] expected)
        {
            if (fields.Count != expected.Length || expected.Any(name => !fields.ContainsKey(name)))
                throw new FormatException("Scenario step contains fields outside its declared kind.");
        }

        private static void RequirePresent(Dictionary<string, XmlElement> fields, string[] expected)
        { if (expected.Any(name => !fields.ContainsKey(name))) throw new FormatException("Scenario payload is missing a required field."); }

        private static string ReadText(XmlElement element, int maximum, bool allowEmpty = false)
        {
            string value = ConversationContractValidator.ReadString(element);
            if (value.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(value))) throw new FormatException("Scenario text is invalid or exceeds its limit.");
            return value;
        }

        private static int ReadPositive(int value, string field) { if (value < 1) throw new FormatException("Scenario " + field + " is invalid."); return value; }
        private static int ReadNonnegative(int value, string field) { if (value < 0) throw new FormatException("Scenario " + field + " is invalid."); return value; }
        private static int EncodingByteCount(string value) { return new System.Text.UTF8Encoding(false, true).GetByteCount(value); }
    }
}
