using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Xml;
using AlterOS.Dialogue;
using AlterOS.Topics;

namespace AlterOS.Conversation
{
    public sealed class ConversationProtocolMessage
    {
        public readonly int protocolVersion;
        public readonly string type;
        public readonly string requestId;
        public readonly object payload;

        internal ConversationProtocolMessage(int version, string messageType, string id, object data)
        { protocolVersion = version; type = messageType; requestId = id; payload = data; }
    }

    public sealed class ConversationTurnRequestV2
    {
        public readonly string sessionId, channel, text, activeResponseId, activeTopicId;
        internal ConversationTurnRequestV2(string session, string turnChannel, string turnText, string responseId, string topicId)
        { sessionId = session; channel = turnChannel; text = turnText; activeResponseId = responseId; activeTopicId = topicId; }
    }

    public sealed class ConversationCandidateCardV2
    {
        public readonly string topicId, prompt, authoredAvailability;
        public readonly ReadOnlyCollection<string> examples;
        public readonly double similarity;
        public readonly int supportCount;
        internal ConversationCandidateCardV2(string id, string shortPrompt, IEnumerable<string> anchors, double score, int supports, string availability)
        { topicId = id; prompt = shortPrompt; examples = Array.AsReadOnly(anchors.ToArray()); similarity = score; supportCount = supports; authoredAvailability = availability; }
    }

    public sealed class ConversationCandidateSetV2
    {
        public readonly string indexIdentity, source;
        public readonly ReadOnlyCollection<ConversationCandidateCardV2> cards;
        internal ConversationCandidateSetV2(string identity, string candidateSource, IEnumerable<ConversationCandidateCardV2> items)
        { indexIdentity = identity; source = candidateSource; cards = Array.AsReadOnly(items.ToArray()); }
    }

    public sealed class ConversationCoverageScenarioV2
    {
        public readonly string scenarioId, coverageSummary, experienceKey;
        internal ConversationCoverageScenarioV2(string id, string summary, string key)
        { scenarioId = id; coverageSummary = summary; experienceKey = key; }
    }

    public sealed class ConversationCoverageTopicV2
    {
        public readonly string topicId, authoredAvailability;
        public readonly bool allowUncoveredDiscussion;
        public readonly ReadOnlyCollection<ConversationCoverageScenarioV2> scenarios;
        internal ConversationCoverageTopicV2(string id, bool allowed, string availability, IEnumerable<ConversationCoverageScenarioV2> items)
        { topicId = id; allowUncoveredDiscussion = allowed; authoredAvailability = availability; scenarios = Array.AsReadOnly(items.ToArray()); }
    }

    public sealed class ConversationCoverageSnapshotV2
    {
        public readonly int schemaVersion;
        public readonly ReadOnlyCollection<ConversationCoverageTopicV2> topics;
        internal ConversationCoverageSnapshotV2(int version, IEnumerable<ConversationCoverageTopicV2> items)
        { schemaVersion = version; topics = Array.AsReadOnly(items.ToArray()); }
    }

    public sealed class ConversationPresentedUtteranceV2
    {
        public readonly string speaker, source, responseId, nodeId, text, sourceId;
        public ConversationPresentedUtteranceV2(string utteranceSpeaker, string utteranceSource, string id, string node, string utteranceText,
            string transcriptSourceId = "")
        { speaker = utteranceSpeaker; source = utteranceSource; responseId = id ?? ""; nodeId = node ?? ""; text = utteranceText; sourceId = transcriptSourceId ?? ""; }
    }

    public sealed class ConversationJobV2
    {
        public readonly ConversationTurnRequestV2 request;
        public readonly ConversationCandidateSetV2 candidates;
        public readonly ConversationCoverageSnapshotV2 coverage;
        public readonly ReadOnlyCollection<ConversationPresentedUtteranceV2> presentedHistory;
        public readonly string characterRules;
        public readonly ReadOnlyCollection<string> allowedExpressions;
        internal ConversationJobV2(ConversationTurnRequestV2 turn, ConversationCandidateSetV2 candidateSet,
            ConversationCoverageSnapshotV2 coverageSnapshot, IEnumerable<ConversationPresentedUtteranceV2> history,
            string rules, IEnumerable<string> expressions)
        {
            request = turn; candidates = candidateSet; coverage = coverageSnapshot;
            presentedHistory = Array.AsReadOnly(history.ToArray()); characterRules = rules;
            allowedExpressions = Array.AsReadOnly(expressions.ToArray());
        }
    }

    public sealed class ConversationBeatV2
    {
        public readonly string id, text, displayText, language, expression;
        internal ConversationBeatV2(string beatId, string speech, string display, string speechLanguage, string cue)
        { id = beatId; text = speech; displayText = display; language = speechLanguage; expression = cue; }
    }

    public sealed class ConversationReplyV2
    {
        public readonly ReadOnlyCollection<ConversationBeatV2> beats;
        internal ConversationReplyV2(IEnumerable<ConversationBeatV2> replyBeats) { beats = Array.AsReadOnly(replyBeats.ToArray()); }
    }

    public sealed class ConversationSuggestedReplyV2
    {
        public readonly string label, prompt;
        internal ConversationSuggestedReplyV2(string visibleLabel, string submittedPrompt) { label = visibleLabel; prompt = submittedPrompt; }
    }

    public sealed class ConversationJudgmentV2
    {
        public readonly string kind, topicId;
        public readonly ReadOnlyCollection<string> referencedTopicIds;
        public readonly ConversationReplyV2 reply;
        public readonly ReadOnlyCollection<ConversationSuggestedReplyV2> suggestedReplies;
        internal ConversationJudgmentV2(string judgmentKind, string id, IEnumerable<string> references,
            ConversationReplyV2 generatedReply, IEnumerable<ConversationSuggestedReplyV2> suggestions)
        { kind = judgmentKind; topicId = id; referencedTopicIds = Array.AsReadOnly(references.ToArray()); reply = generatedReply; suggestedReplies = Array.AsReadOnly(suggestions.ToArray()); }
    }

    public sealed class ConversationUsageV2
    {
        public readonly int inputTokens, outputTokens, totalTokens;
        public readonly double? costUsd;
        internal ConversationUsageV2(int input, int output, int total, double? cost) { inputTokens = input; outputTokens = output; totalTokens = total; costUsd = cost; }
    }

    public sealed class ConversationTimingV2
    {
        public readonly int providerMs, totalMs;
        internal ConversationTimingV2(int provider, int total) { providerMs = provider; totalMs = total; }
    }

    public sealed class ConversationResultV2
    {
        public readonly ConversationJudgmentV2 judgment;
        public readonly ConversationUsageV2 usage;
        public readonly ConversationTimingV2 timing;
        internal ConversationResultV2(ConversationJudgmentV2 result, ConversationUsageV2 tokenUsage, ConversationTimingV2 durations)
        { judgment = result; usage = tokenUsage; timing = durations; }
    }

    public sealed class ConversationBootstrapV2
    {
        public readonly string apiKey;
        internal ConversationBootstrapV2(string key) { apiKey = key; }
    }

    public sealed class ConversationReadyV2
    {
        public readonly string runtimeVersion, modelId;
        internal ConversationReadyV2(string runtime, string model) { runtimeVersion = runtime; modelId = model; }
    }

    public sealed class ConversationErrorV2
    {
        public readonly string code;
        public readonly string diagnosticCode;
        public readonly bool retryable;
        internal ConversationErrorV2(string errorCode, bool canRetry, string diagnostic)
        { code = errorCode; retryable = canRetry; diagnosticCode = diagnostic ?? string.Empty; }
    }

    public sealed class ConversationEmptyV2 { internal ConversationEmptyV2() { } }

    public static class ConversationProtocol
    {
        public const int MaximumEnvelopeBytes = 256 * 1024;
        public const int MaximumTurnBytes = 64 * 1024;
        public const int MaximumResultBytes = 128 * 1024;
        public const int MaximumHistoryUtterances = 12;
        public const int MaximumHistoryCharacters = 6000;
        public const int MaximumContextCharacters = 10000;
        public const int MaximumCharacterRuleCharacters = 8000;
        public const int MaximumReplyBeats = 16;
        public const int MaximumReplyCharacters = 12000;
        public const int MaximumNodeCharacters = 600;
        public const int MaximumSuggestions = 3;
        private const string Japanese = "japanese";

        private static readonly HashSet<string> ErrorCodes = new HashSet<string>(new[]
        { "invalid_job", "schema_unsupported", "provider_auth", "provider_payment_required", "provider_rate_limited", "provider_unavailable", "provider_timeout", "provider_refusal", "persona_unavailable", "invalid_reply", "internal_error" }, StringComparer.Ordinal);
        private static readonly HashSet<string> ErrorDiagnosticCodes = new HashSet<string>(new[]
        { "output_format", "judgment_validation", "response_json", "response_envelope",
          "provider_termination", "judgment_schema", "usage_metadata" }, StringComparer.Ordinal);

        public static ConversationProtocolMessage ParseEnvelope(string json)
        {
            try
            {
                XmlElement root = ConversationContractValidator.ReadRoot(json, MaximumEnvelopeBytes);
                Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(root,
                    new[] { "protocolVersion", "type", "requestId", "payload" });
                int version = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(fields, "protocolVersion"));
                if (version == 3) return AlterOS.Memory.PlayerMemoryProtocol.ParseEnvelope(json);
                if (version == 4)
                {
                    ScenarioProtocolMessage scenario = ScenarioTurnProtocol.ParseEnvelope(json);
                    return new ConversationProtocolMessage(4, scenario.type, scenario.requestId, scenario.payload);
                }
                string type = ConversationContractValidator.ReadString(ConversationContractValidator.Required(fields, "type"));
                string requestId = ReadText(ConversationContractValidator.Required(fields, "requestId"), 128, "requestId");
                if (version != 2) throw new FormatException("Unsupported conversation protocol version.");
                XmlElement payloadElement = ConversationContractValidator.Required(fields, "payload");
                object payload;
                switch (type)
                {
                    case "bootstrap": payload = ParseBootstrap(payloadElement); break;
                    case "ready": payload = ParseReady(payloadElement); break;
                    case "turn": payload = ParseJobPayload(payloadElement); break;
                    case "result": payload = ParseResultPayload(payloadElement); break;
                    case "error": payload = ParseError(payloadElement); break;
                    case "cancel":
                    case "shutdown":
                        ConversationContractValidator.ReadObject(payloadElement, new string[0]);
                        payload = new ConversationEmptyV2();
                        break;
                    default: throw new FormatException("Unsupported conversation protocol message type.");
                }
                int payloadBytes = JsonPropertyValueBytes(json, "payload");
                if (type == "turn" && payloadBytes > MaximumTurnBytes)
                    throw new FormatException("Conversation turn payload exceeds its byte limit.");
                if (type == "result" && payloadBytes > MaximumResultBytes)
                    throw new FormatException("Conversation result payload exceeds its byte limit.");
                return new ConversationProtocolMessage(version, type, requestId, payload);
            }
            catch (FormatException exception) { throw new FormatException(exception.Message); }
            catch (Exception) { throw new FormatException("Invalid conversation protocol envelope."); }
        }

        public static ConversationJobV2 BuildJob(ConversationRequest request, TopicCandidateSet candidates,
            ConversationCoverageSnapshot coverage, IEnumerable<ConversationPresentedUtteranceV2> presentedHistory,
            string characterRules, IEnumerable<string> allowedExpressions)
        {
            if (ConversationContractValidator.ValidateRequest(request).Count != 0 ||
                ConversationContractValidator.ValidateCandidateSet(candidates, true).Count != 0)
                throw new FormatException("Conversation job request or candidates are invalid.");
            if (coverage == null || coverage.schemaVersion != 1 || coverage.topics == null || coverage.topics.Count > 9)
                throw new FormatException("Conversation coverage snapshot is invalid.");
            if (string.IsNullOrWhiteSpace(characterRules) || characterRules.Length > MaximumCharacterRuleCharacters)
                throw new FormatException("Character rules are required and bounded.");
            if (presentedHistory == null || allowedExpressions == null || allowedExpressions.Count() > 64)
                throw new FormatException("Conversation history or expressions are invalid.");

            var candidateItems = new List<ConversationCandidateCardV2>();
            foreach (TopicCandidateCard card in candidates.cards)
                candidateItems.Add(new ConversationCandidateCardV2(card.topicId, card.prompt, card.examples, card.similarity, card.supportCount, card.authoredAvailability));
            var coverageItems = new List<ConversationCoverageTopicV2>();
            int contextCharacters = 0;
            var coverageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversationTopicCoverageSnapshot topic in coverage.topics)
            {
                if (topic == null || string.IsNullOrWhiteSpace(topic.topicId) || !coverageIds.Add(topic.topicId) ||
                    (topic.authoredAvailability != "ready" && topic.authoredAvailability != "blocked") || topic.scenarios == null || topic.scenarios.Count > 32)
                    throw new FormatException("Conversation coverage entry is invalid.");
                var scenarios = new List<ConversationCoverageScenarioV2>();
                foreach (ConversationScenarioCoverage scenario in topic.scenarios)
                {
                    if (scenario == null) throw new FormatException("Conversation coverage scenario is invalid.");
                    ReadText(scenario.scenarioId, 128, "scenarioId");
                    ReadText(scenario.coverageSummary, 400, "coverageSummary");
                    ReadText(scenario.experienceKey, 128, "experienceKey");
                    contextCharacters += scenario.coverageSummary.Length;
                    scenarios.Add(new ConversationCoverageScenarioV2(scenario.scenarioId, scenario.coverageSummary, scenario.experienceKey));
                }
                coverageItems.Add(new ConversationCoverageTopicV2(topic.topicId, topic.allowUncoveredDiscussion, topic.authoredAvailability, scenarios));
            }
            var candidateIds = new HashSet<string>(candidateItems.Select(card => card.topicId), StringComparer.Ordinal);
            if (coverageItems.Any(topic => !candidateIds.Contains(topic.topicId)))
                throw new FormatException("Coverage snapshot contains a Topic outside candidate context.");
            foreach (ConversationCandidateCardV2 card in candidateItems)
                contextCharacters += card.prompt.Length + card.examples.Sum(example => example.Length);
            if (contextCharacters > MaximumContextCharacters) throw new FormatException("Candidate and coverage context exceeds the character limit.");

            var history = new List<ConversationPresentedUtteranceV2>();
            foreach (ConversationPresentedUtteranceV2 item in presentedHistory)
            {
                if (item == null) throw new FormatException("Presented history cannot contain null entries.");
                if ((item.speaker != "player" && item.speaker != "alterra" && item.speaker != "altera") ||
                    (item.source != "player" && item.source != "authored" && item.source != "generated") ||
                    item.text == null || item.text.Length == 0)
                    throw new FormatException("Presented history entry is invalid.");
                if (item.text.Length > MaximumNodeCharacters) continue;
                ReadText(item.responseId, 128, "responseId", true);
                ReadText(item.nodeId, 64, "nodeId", true);
                string speaker = item.speaker == "altera" ? "alterra" : item.speaker;
                history.Add(new ConversationPresentedUtteranceV2(speaker, item.source, item.responseId, item.nodeId, item.text, item.sourceId));
            }
            while (history.Count > MaximumHistoryUtterances || history.Sum(item => item.text.Length) > MaximumHistoryCharacters)
                history.RemoveAt(0);

            var expressions = allowedExpressions.ToList();
            if (expressions.Any(string.IsNullOrWhiteSpace) || expressions.Any(value => value.Length > 64) ||
                expressions.Distinct(StringComparer.Ordinal).Count() != expressions.Count)
                throw new FormatException("Allowed expressions are invalid.");
            var turnRequest = new ConversationTurnRequestV2(request.sessionId, request.channel, request.text, request.activeResponseId, request.activeTopicId);
            var job = new ConversationJobV2(turnRequest,
                new ConversationCandidateSetV2(candidates.indexIdentity, candidates.source, candidateItems),
                new ConversationCoverageSnapshotV2(coverage.schemaVersion, coverageItems), history, characterRules, expressions);
            if (Encoding.UTF8.GetByteCount(SerializeJobPayload(job)) > MaximumTurnBytes)
                throw new FormatException("Conversation turn exceeds its byte limit.");
            return job;
        }

        public static List<string> ValidateJudgment(ConversationJobV2 job, ConversationJudgmentV2 judgment, ISet<string> allowedExpressions)
        {
            var errors = new List<string>();
            if (job == null || judgment == null) { errors.Add("Job and judgment are required."); return errors; }
            var cards = job.candidates.cards.ToDictionary(card => card.topicId, StringComparer.Ordinal);
            var coverage = job.coverage.topics.ToDictionary(topic => topic.topicId, StringComparer.Ordinal);
            var references = new HashSet<string>(judgment.referencedTopicIds, StringComparer.Ordinal);
            foreach (string topicId in references)
            {
                if (judgment.kind == "existing_uncovered" && topicId == judgment.topicId) continue;
                if (topicId == job.request.activeTopicId && judgment.kind == "continue") continue;
                ConversationCandidateCardV2 card;
                if (!cards.TryGetValue(topicId, out card) || card.authoredAvailability != "ready")
                    errors.Add("Judgment references unavailable context.");
            }
            if (judgment.kind == "existing" || judgment.kind == "existing_uncovered")
            {
                ConversationCandidateCardV2 card;
                if (!cards.TryGetValue(judgment.topicId, out card) || !references.Contains(judgment.topicId))
                    errors.Add("Selected Topic is outside supplied context.");
                if (judgment.kind == "existing")
                {
                    if (card != null && card.authoredAvailability != "ready") errors.Add("Authored Topic is unavailable.");
                    if (judgment.reply != null) errors.Add("Authored result cannot include generated prose.");
                }
                else
                {
                    ConversationCoverageTopicV2 rule;
                    if (!coverage.TryGetValue(judgment.topicId, out rule) || !rule.allowUncoveredDiscussion)
                        errors.Add("Uncovered Topic discussion is not allowed.");
                    if (judgment.reply == null) errors.Add("Uncovered discussion requires a generated reply.");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(judgment.topicId)) errors.Add("Generated result cannot mint a canonical Topic ID.");
                if (judgment.reply == null) errors.Add("Generated result requires a reply.");
                if (judgment.kind == "continue" && (string.IsNullOrEmpty(job.request.activeResponseId) || string.IsNullOrEmpty(job.request.activeTopicId) || !references.Contains(job.request.activeTopicId)))
                    errors.Add("Continuation is not bound to the active response.");
            }
            if (judgment.reply != null) ValidateReply(judgment.reply, allowedExpressions, errors);
            return errors;
        }

        public static bool IsResponseOwnedBy(ConversationProtocolMessage result, ConversationProtocolMessage turn, string expectedSessionId)
        {
            if (result == null || turn == null || result.type != "result" || turn.type != "turn" ||
                !string.Equals(result.requestId, turn.requestId, StringComparison.Ordinal) || string.IsNullOrEmpty(expectedSessionId)) return false;
            ConversationJobV2 job = turn.payload as ConversationJobV2;
            return job != null && string.Equals(job.request.sessionId, expectedSessionId, StringComparison.Ordinal);
        }

        public static string SerializeTurnEnvelope(string requestId, ConversationJobV2 job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            ReadText(requestId, 128, "requestId");
            string payload = SerializeJobPayload(job);
            if (Encoding.UTF8.GetByteCount(payload) > MaximumTurnBytes) throw new FormatException("Conversation turn exceeds its byte limit.");
            string envelope = "{\"protocolVersion\":2,\"type\":\"turn\",\"requestId\":" + JsonString(requestId) + ",\"payload\":" + payload + "}";
            if (Encoding.UTF8.GetByteCount(envelope) > MaximumEnvelopeBytes) throw new FormatException("Conversation envelope exceeds its byte limit.");
            return envelope;
        }

        public static string SerializeBootstrapEnvelope(string requestId, string apiKey)
        {
            ReadText(requestId, 128, "requestId");
            ReadText(apiKey, 4096, "apiKey");
            return "{\"protocolVersion\":2,\"type\":\"bootstrap\",\"requestId\":" + JsonString(requestId) +
                ",\"payload\":{\"apiKey\":" + JsonString(apiKey) + "}}";
        }

        public static string SerializeControlEnvelope(string type, string requestId)
        {
            if (type != "cancel" && type != "shutdown") throw new ArgumentException("Unsupported control message.", nameof(type));
            ReadText(requestId, 128, "requestId");
            return "{\"protocolVersion\":2,\"type\":" + JsonString(type) + ",\"requestId\":" + JsonString(requestId) + ",\"payload\":{}}";
        }

        public static string SerializeJudgmentForTranscript(ConversationJudgmentV2 judgment)
        {
            if (judgment == null) throw new ArgumentNullException(nameof(judgment));
            var builder = new StringBuilder("{\"kind\":").Append(JsonString(judgment.kind))
                .Append(",\"topicId\":").Append(JsonString(judgment.topicId)).Append(",\"referencedTopicIds\":[");
            AppendStrings(builder, judgment.referencedTopicIds);
            builder.Append("],\"reply\":");
            if (judgment.reply == null) builder.Append("null");
            else
            {
                builder.Append("{\"beats\":[");
                for (int index = 0; index < judgment.reply.beats.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    ConversationBeatV2 beat = judgment.reply.beats[index];
                    builder.Append("{\"id\":").Append(JsonString(beat.id)).Append(",\"text\":").Append(JsonString(beat.text))
                        .Append(",\"displayText\":").Append(JsonString(beat.displayText)).Append(",\"language\":")
                        .Append(JsonString(beat.language)).Append(",\"expression\":").Append(JsonString(beat.expression)).Append('}');
                }
                builder.Append("]}");
            }
            builder.Append("],\"suggestedReplies\":[");
            for (int index = 0; index < judgment.suggestedReplies.Count; index++)
            {
                if (index > 0) builder.Append(',');
                ConversationSuggestedReplyV2 suggestion = judgment.suggestedReplies[index];
                builder.Append("{\"label\":").Append(JsonString(suggestion.label)).Append(",\"prompt\":").Append(JsonString(suggestion.prompt)).Append('}');
            }
            builder.Append("]}");
            string serialized = builder.ToString();
            if (Encoding.UTF8.GetByteCount(serialized) > MaximumResultBytes) throw new FormatException("Validated judgment exceeds the transcript size limit.");
            return serialized;
        }

        public static ConversationReply ToCanonicalReply(ConversationReplyV2 reply)
        {
            var result = new ConversationReply();
            foreach (ConversationBeatV2 beat in reply.beats)
                result.nodes.Add(new CanonicalDialogueNode
                {
                    id = beat.id, speaker = "alterra", text = beat.text, displayText = beat.displayText,
                    language = Japanese, voice = "", audioKey = "", nextId = "",
                    presentation = new DialoguePresentation { expression = beat.expression, animation = "" },
                    choices = new List<CanonicalDialogueChoice>()
                });
            return result;
        }

        internal static ConversationJobV2 ParseJobPayload(XmlElement payload)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(payload,
                new[] { "request", "candidates", "coverage", "presentedHistory", "characterRules", "allowedExpressions" });
            XmlElement requestElement = ConversationContractValidator.Required(fields, "request");
            Dictionary<string, XmlElement> request = ConversationContractValidator.ReadObject(requestElement,
                new[] { "sessionId", "channel", "text", "activeResponseId", "activeTopicId" });
            string sessionId = ReadText(ConversationContractValidator.Required(request, "sessionId"), 128, "sessionId");
            string channel = ConversationContractValidator.ReadString(ConversationContractValidator.Required(request, "channel"));
            string text = ReadText(ConversationContractValidator.Required(request, "text"), ConversationContractValidator.MaximumRequestCharacters, "text");
            string activeResponseId = ReadText(ConversationContractValidator.Required(request, "activeResponseId"), 128, "activeResponseId", true);
            string activeTopicId = ReadText(ConversationContractValidator.Required(request, "activeTopicId"), 128, "activeTopicId", true);
            if (channel != "direct" && channel != "phone") throw new FormatException("Unsupported conversation channel.");
            if (!string.IsNullOrEmpty(activeTopicId) && string.IsNullOrEmpty(activeResponseId)) throw new FormatException("Active Topic requires an active response.");

            Dictionary<string, XmlElement> candidates = ConversationContractValidator.ReadObject(
                ConversationContractValidator.Required(fields, "candidates"), new[] { "indexIdentity", "source", "cards" });
            string indexIdentity = ReadText(ConversationContractValidator.Required(candidates, "indexIdentity"), 256, "indexIdentity");
            string source = ConversationContractValidator.ReadString(ConversationContractValidator.Required(candidates, "source"));
            if (source != "local_gte" && source != "synthetic") throw new FormatException("Unsupported candidate source.");
            var candidateItems = new List<ConversationCandidateCardV2>();
            foreach (XmlElement cardElement in ConversationContractValidator.ReadArray(ConversationContractValidator.Required(candidates, "cards"), 8))
            {
                Dictionary<string, XmlElement> card = ConversationContractValidator.ReadObject(cardElement,
                    new[] { "topicId", "prompt", "examples", "similarity", "supportCount", "authoredAvailability" });
                string availability = ConversationContractValidator.ReadString(ConversationContractValidator.Required(card, "authoredAvailability"));
                if (availability != "ready" && availability != "blocked") throw new FormatException("Candidate availability must be prepared.");
                var examples = ConversationContractValidator.ReadStringArray(ConversationContractValidator.Required(card, "examples"), 3);
                if (examples.Any(example => string.IsNullOrWhiteSpace(example) || example.Length > 256)) throw new FormatException("Candidate anchor is invalid.");
                int supports = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(card, "supportCount"));
                double similarity = ConversationContractValidator.ReadDouble(ConversationContractValidator.Required(card, "similarity"));
                if (supports < 0) throw new FormatException("Candidate support count is invalid.");
                candidateItems.Add(new ConversationCandidateCardV2(ReadText(ConversationContractValidator.Required(card, "topicId"), 128, "topicId"),
                    ReadText(ConversationContractValidator.Required(card, "prompt"), 512, "prompt", true), examples, similarity, supports, availability));
            }
            if (candidateItems.Select(card => card.topicId).Distinct(StringComparer.Ordinal).Count() != candidateItems.Count)
                throw new FormatException("Candidate Topic IDs must be unique.");

            Dictionary<string, XmlElement> coverage = ConversationContractValidator.ReadObject(
                ConversationContractValidator.Required(fields, "coverage"), new[] { "schemaVersion", "topics" });
            int coverageVersion = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(coverage, "schemaVersion"));
            if (coverageVersion != 1) throw new FormatException("Unsupported coverage snapshot version.");
            var coverageItems = new List<ConversationCoverageTopicV2>();
            foreach (XmlElement topicElement in ConversationContractValidator.ReadArray(ConversationContractValidator.Required(coverage, "topics"), 9))
            {
                Dictionary<string, XmlElement> topic = ConversationContractValidator.ReadObject(topicElement,
                    new[] { "topicId", "allowUncoveredDiscussion", "authoredAvailability", "scenarios" });
                string availability = ConversationContractValidator.ReadString(ConversationContractValidator.Required(topic, "authoredAvailability"));
                if (availability != "ready" && availability != "blocked") throw new FormatException("Coverage availability is invalid.");
                var scenarioItems = new List<ConversationCoverageScenarioV2>();
                foreach (XmlElement scenarioElement in ConversationContractValidator.ReadArray(ConversationContractValidator.Required(topic, "scenarios"), 32))
                {
                    Dictionary<string, XmlElement> scenario = ConversationContractValidator.ReadObject(scenarioElement,
                        new[] { "scenarioId", "coverageSummary", "experienceKey" });
                    scenarioItems.Add(new ConversationCoverageScenarioV2(
                        ReadText(ConversationContractValidator.Required(scenario, "scenarioId"), 128, "scenarioId"),
                        ReadText(ConversationContractValidator.Required(scenario, "coverageSummary"), 400, "coverageSummary"),
                        ReadText(ConversationContractValidator.Required(scenario, "experienceKey"), 128, "experienceKey")));
                }
                coverageItems.Add(new ConversationCoverageTopicV2(
                    ReadText(ConversationContractValidator.Required(topic, "topicId"), 128, "topicId"),
                    ConversationContractValidator.ReadBoolean(ConversationContractValidator.Required(topic, "allowUncoveredDiscussion")), availability, scenarioItems));
            }
            if (coverageItems.Select(topic => topic.topicId).Distinct(StringComparer.Ordinal).Count() != coverageItems.Count)
                throw new FormatException("Coverage Topic IDs must be unique.");

            var history = new List<ConversationPresentedUtteranceV2>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(ConversationContractValidator.Required(fields, "presentedHistory"), MaximumHistoryUtterances))
            {
                Dictionary<string, XmlElement> utterance = ConversationContractValidator.ReadObject(item,
                    new[] { "speaker", "source", "responseId", "nodeId", "text" });
                string speaker = ConversationContractValidator.ReadString(ConversationContractValidator.Required(utterance, "speaker"));
                string historySource = ConversationContractValidator.ReadString(ConversationContractValidator.Required(utterance, "source"));
                if ((speaker != "player" && speaker != "alterra") || (historySource != "player" && historySource != "authored" && historySource != "generated"))
                    throw new FormatException("Presented history provenance is invalid.");
                history.Add(new ConversationPresentedUtteranceV2(speaker, historySource,
                    ReadText(ConversationContractValidator.Required(utterance, "responseId"), 128, "responseId", true),
                    ReadText(ConversationContractValidator.Required(utterance, "nodeId"), 64, "nodeId", true),
                    ReadText(ConversationContractValidator.Required(utterance, "text"), MaximumNodeCharacters, "history.text")));
            }
            string rules = ReadText(ConversationContractValidator.Required(fields, "characterRules"), MaximumCharacterRuleCharacters, "characterRules");
            List<string> expressions = ConversationContractValidator.ReadStringArray(ConversationContractValidator.Required(fields, "allowedExpressions"), 64);
            if (expressions.Any(value => value.Length > 64 || string.IsNullOrWhiteSpace(value)) || expressions.Distinct(StringComparer.Ordinal).Count() != expressions.Count)
                throw new FormatException("Allowed expressions are invalid.");
            return BuildJob(new ConversationRequest
            {
                schemaVersion = 1, requestId = "protocol", sessionId = sessionId, channel = channel, text = text,
                activeResponseId = activeResponseId, activeTopicId = activeTopicId
            }, ToM1Candidates(indexIdentity, source, candidateItems),
                new ConversationCoverageSnapshot { schemaVersion = coverageVersion, topics = ToM1Coverage(coverageItems) },
                history, rules, expressions);
        }

        private static TopicCandidateSet ToM1Candidates(string identity, string source, List<ConversationCandidateCardV2> items)
        {
            var result = new TopicCandidateSet { indexIdentity = identity, source = source };
            foreach (ConversationCandidateCardV2 item in items)
                result.cards.Add(new TopicCandidateCard { topicId = item.topicId, prompt = item.prompt, examples = item.examples.ToList(), similarity = (float)item.similarity, supportCount = item.supportCount, authoredAvailability = item.authoredAvailability });
            return result;
        }

        private static List<ConversationTopicCoverageSnapshot> ToM1Coverage(List<ConversationCoverageTopicV2> items)
        {
            var result = new List<ConversationTopicCoverageSnapshot>();
            foreach (ConversationCoverageTopicV2 item in items)
            {
                var topic = new ConversationTopicCoverageSnapshot { topicId = item.topicId, allowUncoveredDiscussion = item.allowUncoveredDiscussion, authoredAvailability = item.authoredAvailability };
                foreach (ConversationCoverageScenarioV2 scenario in item.scenarios)
                    topic.scenarios.Add(new ConversationScenarioCoverage { scenarioId = scenario.scenarioId, coverageSummary = scenario.coverageSummary, experienceKey = scenario.experienceKey });
                result.Add(topic);
            }
            return result;
        }

        internal static ConversationResultV2 ParseResultPayload(XmlElement payload)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(payload, new[] { "judgment", "usage", "timing" });
            Dictionary<string, XmlElement> usage = ConversationContractValidator.ReadObject(ConversationContractValidator.Required(fields, "usage"),
                new[] { "inputTokens", "outputTokens", "totalTokens", "costUsd" });
            int input = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(usage, "inputTokens"));
            int output = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(usage, "outputTokens"));
            int total = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(usage, "totalTokens"));
            XmlElement costElement = ConversationContractValidator.Required(usage, "costUsd");
            double? cost = ConversationContractValidator.TypeOf(costElement) == "null" ? (double?)null : ConversationContractValidator.ReadDouble(costElement);
            if (input < 0 || output < 0 || total < 0 || (cost.HasValue && cost.Value < 0)) throw new FormatException("Usage values are invalid.");
            Dictionary<string, XmlElement> timing = ConversationContractValidator.ReadObject(ConversationContractValidator.Required(fields, "timing"), new[] { "providerMs", "totalMs" });
            int providerMs = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(timing, "providerMs"));
            int totalMs = ConversationContractValidator.ReadInt(ConversationContractValidator.Required(timing, "totalMs"));
            if (providerMs < 0 || totalMs < 0) throw new FormatException("Timing values are invalid.");
            return new ConversationResultV2(ParseJudgment(ConversationContractValidator.Required(fields, "judgment")),
                new ConversationUsageV2(input, output, total, cost), new ConversationTimingV2(providerMs, totalMs));
        }

        private static ConversationJudgmentV2 ParseJudgment(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "kind", "topicId", "referencedTopicIds", "reply", "suggestedReplies" });
            string kind = ConversationContractValidator.ReadString(ConversationContractValidator.Required(fields, "kind"));
            if (kind != "existing" && kind != "existing_uncovered" && kind != "continue" && kind != "clarify" && kind != "novel_candidate")
                throw new FormatException("Unsupported judgment kind.");
            string topicId = ReadText(ConversationContractValidator.Required(fields, "topicId"), 128, "topicId", true);
            List<string> references = ConversationContractValidator.ReadStringArray(ConversationContractValidator.Required(fields, "referencedTopicIds"), 8);
            if (references.Any(string.IsNullOrWhiteSpace) || references.Distinct(StringComparer.Ordinal).Count() != references.Count)
                throw new FormatException("Topic references must be unique and nonempty.");
            XmlElement replyElement = ConversationContractValidator.Required(fields, "reply");
            ConversationReplyV2 reply = ConversationContractValidator.TypeOf(replyElement) == "null" ? null : ParseReply(replyElement);
            var suggestions = new List<ConversationSuggestedReplyV2>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(ConversationContractValidator.Required(fields, "suggestedReplies"), MaximumSuggestions))
            {
                Dictionary<string, XmlElement> suggestion = ConversationContractValidator.ReadObject(item, new[] { "label", "prompt" });
                suggestions.Add(new ConversationSuggestedReplyV2(
                    ReadText(ConversationContractValidator.Required(suggestion, "label"), 80, "suggestion.label"),
                    ReadText(ConversationContractValidator.Required(suggestion, "prompt"), 512, "suggestion.prompt")));
            }
            return new ConversationJudgmentV2(kind, topicId, references, reply, suggestions);
        }

        private static ConversationReplyV2 ParseReply(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "beats" });
            var beats = new List<ConversationBeatV2>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement item in ConversationContractValidator.ReadArray(ConversationContractValidator.Required(fields, "beats"), MaximumReplyBeats))
            {
                Dictionary<string, XmlElement> beat = ConversationContractValidator.ReadObject(item, new[] { "id", "text", "displayText", "language", "expression" });
                string id = ReadText(ConversationContractValidator.Required(beat, "id"), 64, "beat.id");
                if (!ids.Add(id)) throw new FormatException("Reply beat IDs must be unique.");
                beats.Add(new ConversationBeatV2(id,
                    ReadText(ConversationContractValidator.Required(beat, "text"), MaximumNodeCharacters, "beat.text"),
                    ReadText(ConversationContractValidator.Required(beat, "displayText"), MaximumNodeCharacters, "beat.displayText"),
                    ReadText(ConversationContractValidator.Required(beat, "language"), 32, "beat.language"),
                    ReadText(ConversationContractValidator.Required(beat, "expression"), 64, "beat.expression", true)));
            }
            if (beats.Count == 0) throw new FormatException("Reply must contain at least one beat.");
            return new ConversationReplyV2(beats);
        }

        private static ConversationBootstrapV2 ParseBootstrap(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "apiKey" });
            return new ConversationBootstrapV2(ReadText(ConversationContractValidator.Required(fields, "apiKey"), 4096, "apiKey"));
        }

        private static ConversationReadyV2 ParseReady(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "runtimeVersion", "modelId" });
            return new ConversationReadyV2(ReadText(ConversationContractValidator.Required(fields, "runtimeVersion"), 64, "runtimeVersion"),
                ReadText(ConversationContractValidator.Required(fields, "modelId"), 128, "modelId"));
        }

        private static ConversationErrorV2 ParseError(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "code", "retryable" }, new[] { "diagnosticCode" });
            string code = ConversationContractValidator.ReadString(ConversationContractValidator.Required(fields, "code"));
            if (!ErrorCodes.Contains(code)) throw new FormatException("Unsupported conversation error code.");
            XmlElement diagnosticElement;
            string diagnostic = fields.TryGetValue("diagnosticCode", out diagnosticElement)
                ? ConversationContractValidator.ReadString(diagnosticElement) : string.Empty;
            if (fields.ContainsKey("diagnosticCode") &&
                (code != "invalid_reply" || !ErrorDiagnosticCodes.Contains(diagnostic)))
                throw new FormatException("Unsupported conversation diagnostic code.");
            return new ConversationErrorV2(code,
                ConversationContractValidator.ReadBoolean(ConversationContractValidator.Required(fields, "retryable")), diagnostic);
        }

        private static void ValidateReply(ConversationReplyV2 reply, ISet<string> allowedExpressions, List<string> errors)
        {
            if (reply.beats.Count < 1 || reply.beats.Count > MaximumReplyBeats) { errors.Add("Reply beat count is invalid."); return; }
            int characters = 0;
            foreach (ConversationBeatV2 beat in reply.beats)
            {
                if (string.IsNullOrWhiteSpace(beat.text) || string.IsNullOrWhiteSpace(beat.displayText)) errors.Add("Both speech and display text are required.");
                if (beat.language != Japanese) errors.Add("Generated speech language must be japanese.");
                if (!string.IsNullOrEmpty(beat.expression) && (allowedExpressions == null || !allowedExpressions.Contains(beat.expression))) errors.Add("Expression is not host-allowlisted.");
                characters += beat.text.Length + beat.displayText.Length;
            }
            if (characters > MaximumReplyCharacters) errors.Add("Reply exceeds its total text limit.");
        }

        private static string ReadText(XmlElement element, int maximum, string name, bool allowEmpty = false)
        { return ReadText(ConversationContractValidator.ReadString(element), maximum, name, allowEmpty); }

        private static string ReadText(string value, int maximum, string name, bool allowEmpty = false)
        {
            if (value == null || value.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
                throw new FormatException("Conversation " + name + " is invalid or exceeds its limit.");
            return value;
        }

        internal static string SerializeJobPayload(ConversationJobV2 job)
        {
            var builder = new StringBuilder();
            builder.Append("{\"request\":{\"sessionId\":").Append(JsonString(job.request.sessionId))
                .Append(",\"channel\":").Append(JsonString(job.request.channel)).Append(",\"text\":").Append(JsonString(job.request.text))
                .Append(",\"activeResponseId\":").Append(JsonString(job.request.activeResponseId)).Append(",\"activeTopicId\":").Append(JsonString(job.request.activeTopicId))
                .Append("},\"candidates\":{\"indexIdentity\":").Append(JsonString(job.candidates.indexIdentity))
                .Append(",\"source\":").Append(JsonString(job.candidates.source)).Append(",\"cards\":[");
            for (int cardIndex = 0; cardIndex < job.candidates.cards.Count; cardIndex++)
            {
                if (cardIndex != 0) builder.Append(',');
                ConversationCandidateCardV2 card = job.candidates.cards[cardIndex];
                builder.Append("{\"topicId\":").Append(JsonString(card.topicId)).Append(",\"prompt\":").Append(JsonString(card.prompt)).Append(",\"examples\":[");
                AppendStrings(builder, card.examples);
                builder.Append("],\"similarity\":").Append(card.similarity.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(",\"supportCount\":").Append(card.supportCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(",\"authoredAvailability\":").Append(JsonString(card.authoredAvailability)).Append('}');
            }
            builder.Append("]},\"coverage\":{\"schemaVersion\":").Append(job.coverage.schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"topics\":[");
            for (int topicIndex = 0; topicIndex < job.coverage.topics.Count; topicIndex++)
            {
                if (topicIndex != 0) builder.Append(',');
                ConversationCoverageTopicV2 topic = job.coverage.topics[topicIndex];
                builder.Append("{\"topicId\":").Append(JsonString(topic.topicId)).Append(",\"allowUncoveredDiscussion\":")
                    .Append(topic.allowUncoveredDiscussion ? "true" : "false").Append(",\"authoredAvailability\":").Append(JsonString(topic.authoredAvailability)).Append(",\"scenarios\":[");
                for (int scenarioIndex = 0; scenarioIndex < topic.scenarios.Count; scenarioIndex++)
                {
                    if (scenarioIndex != 0) builder.Append(',');
                    ConversationCoverageScenarioV2 scenario = topic.scenarios[scenarioIndex];
                    builder.Append("{\"scenarioId\":").Append(JsonString(scenario.scenarioId)).Append(",\"coverageSummary\":").Append(JsonString(scenario.coverageSummary))
                        .Append(",\"experienceKey\":").Append(JsonString(scenario.experienceKey)).Append('}');
                }
                builder.Append("]}");
            }
            builder.Append("]},\"presentedHistory\":[");
            for (int index = 0; index < job.presentedHistory.Count; index++)
            {
                if (index != 0) builder.Append(',');
                ConversationPresentedUtteranceV2 item = job.presentedHistory[index];
                builder.Append("{\"speaker\":").Append(JsonString(item.speaker)).Append(",\"source\":").Append(JsonString(item.source))
                    .Append(",\"responseId\":").Append(JsonString(item.responseId)).Append(",\"nodeId\":").Append(JsonString(item.nodeId))
                    .Append(",\"text\":").Append(JsonString(item.text)).Append('}');
            }
            builder.Append("],\"characterRules\":").Append(JsonString(job.characterRules)).Append(",\"allowedExpressions\":[");
            AppendStrings(builder, job.allowedExpressions);
            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendStrings(StringBuilder builder, IReadOnlyList<string> values)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (index != 0) builder.Append(',');
                builder.Append(JsonString(values[index]));
            }
        }

        internal static string JsonString(string value)
        {
            var builder = new StringBuilder(value.Length + 2).Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }

        private static int JsonPropertyValueBytes(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            int index = SkipWhitespace(json, 0);
            if (index >= json.Length || json[index++] != '{') return -1;
            while (index < json.Length)
            {
                index = SkipWhitespace(json, index);
                if (index >= json.Length || json[index] == '}') return -1;
                string name = ReadJsonString(json, ref index);
                index = SkipWhitespace(json, index);
                if (index >= json.Length || json[index++] != ':') return -1;
                index = SkipWhitespace(json, index);
                int start = index;
                int end = FindJsonValueEnd(json, index);
                if (end <= start) return -1;
                if (name == propertyName) return Encoding.UTF8.GetByteCount(json.Substring(start, end - start));
                index = SkipWhitespace(json, end);
                if (index < json.Length && json[index] == ',') index++;
                else return -1;
            }
            return -1;
        }

        private static string ReadJsonString(string json, ref int index)
        {
            if (index >= json.Length || json[index++] != '"') return "";
            var builder = new StringBuilder();
            while (index < json.Length)
            {
                char value = json[index++];
                if (value == '"') return builder.ToString();
                if (value != '\\') { builder.Append(value); continue; }
                if (index >= json.Length) return "";
                char escape = json[index++];
                switch (escape)
                {
                    case '"': case '\\': case '/': builder.Append(escape); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length) return "";
                        int code;
                        if (!int.TryParse(json.Substring(index, 4), System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out code)) return "";
                        builder.Append((char)code); index += 4; break;
                    default: return "";
                }
            }
            return "";
        }

        private static int FindJsonValueEnd(string json, int index)
        {
            if (index >= json.Length) return -1;
            if (json[index] == '"') { ReadJsonString(json, ref index); return index; }
            if (json[index] != '{' && json[index] != '[')
            {
                while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != ']' && !char.IsWhiteSpace(json[index])) index++;
                return index;
            }
            var stack = new Stack<char>();
            stack.Push(json[index++] == '{' ? '}' : ']');
            while (index < json.Length && stack.Count > 0)
            {
                char value = json[index];
                if (value == '"') { ReadJsonString(json, ref index); continue; }
                index++;
                if (value == '{') stack.Push('}');
                else if (value == '[') stack.Push(']');
                else if (value == '}' || value == ']')
                { if (stack.Pop() != value) return -1; }
            }
            return stack.Count == 0 ? index : -1;
        }

        private static int SkipWhitespace(string value, int index)
        { while (index < value.Length && char.IsWhiteSpace(value[index])) index++; return index; }
    }
}
