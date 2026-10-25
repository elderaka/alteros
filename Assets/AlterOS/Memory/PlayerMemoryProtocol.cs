using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AlterOS.Conversation;

namespace AlterOS.Memory
{
    /// <summary>Strict v3 bootstrap and typed local-memory command codec.</summary>
    public static class PlayerMemoryProtocol
    {
        public const int Version = 3;
        public const int MaximumEventsPerSync = 32;
        public const int MaximumSourceSyncBytes = 16 * 1024;
        private static readonly HashSet<string> Operations = new HashSet<string>(new[]
        { "consent", "source_sync", "profile_set", "confirm", "context", "list", "forget", "onboarding", "onboarding_read", "question_ack" }, StringComparer.Ordinal);

        public static string SerializeBootstrapEnvelope(string requestId, string apiKey, string playerId, string memoryRoot)
        {
            RequireText(requestId, 128, "requestId");
            if (apiKey == null || apiKey.Length > 4096) throw new FormatException("Bootstrap key is invalid.");
            RequireText(playerId, 128, "playerId");
            if (string.IsNullOrWhiteSpace(memoryRoot) || !Path.IsPathRooted(memoryRoot)) throw new FormatException("Memory root must be absolute.");
            string fullRoot = Path.GetFullPath(memoryRoot);
            if (!string.Equals(Path.GetFileName(fullRoot), "player-memory.sqlite3", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(fullRoot)), "Memory", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Memory root is outside the host-owned layout.");
            return Envelope("bootstrap", requestId, "{\"apiKey\":" + JsonString(apiKey) +
                ",\"playerId\":" + JsonString(playerId) + ",\"memoryRoot\":" + JsonString(fullRoot) + "}");
        }

        public static string SerializeControlEnvelope(string type, string requestId)
        {
            if (type != "cancel" && type != "shutdown") throw new ArgumentException("Unsupported control type.", nameof(type));
            RequireText(requestId, 128, "requestId");
            return Envelope(type, requestId, "{}");
        }

        public static string SerializeConsentEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, bool enabled)
        { return Command(requestId, "consent", playerId, consentRevision, nowUtc, "{\"enabled\":" + Bool(enabled) + "}"); }

        public static string SerializeConsentReadEnvelope(string requestId, string playerId, int consentRevision, DateTimeOffset nowUtc)
        { return Command(requestId, "consent", playerId, consentRevision, nowUtc, "{\"enabled\":null}"); }

        public static string SerializeProfileSetEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, PlayerMemoryProposal proposal, PlayerMemoryEvidence evidence)
        {
            ValidateProposal(proposal);
            ValidateEvidence(evidence);
            if (proposal.sourceId != evidence.sourceId || proposal.scope != evidence.scope || proposal.evidenceQuote.Length == 0 ||
                !evidence.text.Contains(proposal.evidenceQuote)) throw new FormatException("Memory source binding is invalid.");
            return Command(requestId, "profile_set", playerId, consentRevision, nowUtc,
                "{\"proposal\":" + SerializeProposal(proposal) + ",\"evidence\":" + SerializeEvidence(evidence) + "}");
        }

        public static string SerializeConfirmEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, PlayerMemoryProposal proposal, PlayerMemoryEvidence evidence, PlayerMemoryEvidence confirmation)
        {
            ValidateProposal(proposal); ValidateEvidence(evidence); ValidateEvidence(confirmation);
            bool confirmationEstablishesScope = evidence.scope == "unclassified" && confirmation.scope == proposal.scope;
            if (proposal.sourceId != evidence.sourceId || (proposal.scope != evidence.scope && !confirmationEstablishesScope) ||
                !evidence.text.Contains(proposal.evidenceQuote) || confirmation.actor != "player" ||
                confirmation.sourceKind != "player_confirmation" || !confirmation.explicitlyConfirmed)
                throw new FormatException("Memory confirmation is invalid.");
            return Command(requestId, "confirm", playerId, consentRevision, nowUtc,
                "{\"proposal\":" + SerializeProposal(proposal) + ",\"evidence\":" + SerializeEvidence(evidence) +
                ",\"confirmation\":" + SerializeEvidence(confirmation) + "}");
        }

        public static string SerializeContextEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, string query)
        { return Command(requestId, "context", playerId, consentRevision, nowUtc, "{\"query\":" + JsonString(Bounded(query, 2048, "query")) + "}"); }

        public static string SerializeListEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, string cursor, int limit, bool includeSuperseded)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            return Command(requestId, "list", playerId, consentRevision, nowUtc, "{\"cursor\":" + JsonString(cursor ?? "") +
                ",\"limit\":" + limit.ToString(CultureInfo.InvariantCulture) + ",\"includeSuperseded\":" + Bool(includeSuperseded) + "}");
        }

        public static string SerializeForgetEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, string memoryId)
        { return Command(requestId, "forget", playerId, consentRevision, nowUtc, "{\"memoryId\":" + JsonString(Bounded(memoryId, 128, "memoryId")) + "}"); }

        public static string SerializeOnboardingEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, string stepId, bool completed)
        { return Command(requestId, "onboarding", playerId, consentRevision, nowUtc, "{\"stepId\":" + JsonString(Bounded(stepId, 64, "stepId")) + ",\"completed\":" + Bool(completed) + "}"); }

        public static string SerializeOnboardingReadEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc)
        { return Command(requestId, "onboarding_read", playerId, consentRevision, nowUtc, "{}"); }

        public static string SerializeQuestionAckEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, string intent, string sessionId, string eventId, DateTimeOffset presentedAtUtc)
        {
            if (intent != "preferred_name" && intent != "interest" && intent != "shared_activity_preference")
                throw new FormatException("Question intent is not allowlisted.");
            return Command(requestId, "question_ack", playerId, consentRevision, nowUtc,
                "{\"intent\":" + JsonString(intent) + ",\"sessionId\":" + JsonString(Bounded(sessionId, 128, "sessionId")) +
                ",\"eventId\":" + JsonString(Bounded(eventId, 128, "eventId")) +
                ",\"presentedAtUtc\":" + JsonString(IsoUtc(presentedAtUtc)) + "}");
        }

        public static string SerializeSourceSyncEnvelope(string requestId, string playerId, int consentRevision,
            DateTimeOffset nowUtc, long afterSequence, long throughSequence, IReadOnlyList<PlayerMemorySourceSyncEvent> events)
        {
            if (afterSequence < 0 || throughSequence < afterSequence || events == null || events.Count > MaximumEventsPerSync)
                throw new FormatException("Source sync batch is invalid.");
            int byteCount = 0;
            var jsonEvents = new List<string>();
            long previousSequence = afterSequence;
            foreach (PlayerMemorySourceSyncEvent item in events)
            {
                if (item == null || item.sequence <= previousSequence || item.evidence == null || item.evidence.playerId != playerId)
                    throw new FormatException("Source sync event binding is invalid.");
                if (item.sequence > throughSequence) throw new FormatException("Source sync event is outside its page cursor.");
                ValidateEvidence(item.evidence);
                byteCount += Encoding.UTF8.GetByteCount(item.evidence.text);
                if (byteCount > MaximumSourceSyncBytes) throw new FormatException("Source sync batch exceeds its limit.");
                jsonEvents.Add("{\"sequence\":" + item.sequence.ToString(CultureInfo.InvariantCulture) +
                    ",\"evidence\":" + SerializeEvidence(item.evidence) + "}");
                previousSequence = item.sequence;
            }
            string data = "{\"afterSequence\":" + afterSequence.ToString(CultureInfo.InvariantCulture) +
                ",\"throughSequence\":" + throughSequence.ToString(CultureInfo.InvariantCulture) +
                ",\"events\":[" + string.Join(",", jsonEvents) + "]}";
            return Command(requestId, "source_sync", playerId, consentRevision, nowUtc, data);
        }

        public static string SerializeMemoryTurnEnvelope(string requestId, ConversationJobV2 conversation,
            PlayerMemoryTurnPolicyV3 memory)
        {
            string policy = SerializeTurnPolicy(memory, conversation);
            string payload = "{\"conversation\":" + ConversationProtocol.SerializeJobPayload(conversation) + ",\"memory\":" + policy + "}";
            if (Encoding.UTF8.GetByteCount(payload) > ConversationProtocol.MaximumTurnBytes)
                throw new FormatException("Memory turn exceeds its limit.");
            return Envelope("turn", requestId, payload);
        }

        internal static string SerializeTurnPolicy(PlayerMemoryTurnPolicyV3 memory, ConversationJobV2 conversation)
        {
            if (conversation == null || memory == null || string.IsNullOrWhiteSpace(memory.playerId) ||
                memory.playerId.Length > 128 || memory.consentRevision < 0 || string.IsNullOrWhiteSpace(memory.currentPlayerSourceId) ||
                memory.currentPlayerSourceId.Length > 128 || memory.presentedHistorySourceIds.Count != conversation.presentedHistory.Count)
                throw new FormatException("Memory turn policy is invalid.");
            if (!string.IsNullOrEmpty(memory.authorizedQuestionIntent) && memory.authorizedQuestionIntent != "preferred_name" &&
                memory.authorizedQuestionIntent != "interest" && memory.authorizedQuestionIntent != "shared_activity_preference")
                throw new FormatException("Memory question intent is invalid.");
            string sources = "[" + string.Join(",", memory.presentedHistorySourceIds.Select(JsonString)) + "]";
            string policy = "{\"playerId\":" + JsonString(memory.playerId) +
                ",\"consentRevision\":" + memory.consentRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"nowUtc\":" + JsonString(IsoUtc(memory.nowUtc)) +
                ",\"currentPlayerSourceId\":" + JsonString(memory.currentPlayerSourceId) +
                ",\"focusAllowed\":" + NullableBool(memory.focusAllowed) + ",\"quiet\":" + NullableBool(memory.quiet) +
                ",\"explicitRecall\":" + Bool(memory.explicitRecall) +
                ",\"authorizedQuestionIntent\":" + (string.IsNullOrEmpty(memory.authorizedQuestionIntent) ? "null" : JsonString(memory.authorizedQuestionIntent)) +
                ",\"memoryPolicyRequired\":" + Bool(memory.memoryPolicyRequired) +
                ",\"presentedHistorySourceIds\":" + sources + "}";
            return policy;
        }

        public static ConversationProtocolMessage ParseEnvelope(string json)
        {
            XmlElement root = ConversationContractValidator.ReadRoot(json, ConversationProtocol.MaximumEnvelopeBytes);
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(root,
                new[] { "protocolVersion", "type", "requestId", "payload" });
            if (ConversationContractValidator.ReadInt(ConversationContractValidator.Required(fields, "protocolVersion")) != Version)
                throw new FormatException("Unsupported player-memory protocol version.");
            string type = ConversationContractValidator.ReadString(ConversationContractValidator.Required(fields, "type"));
            string requestId = Bounded(ConversationContractValidator.ReadString(ConversationContractValidator.Required(fields, "requestId")), 128, "requestId");
            XmlElement payload = ConversationContractValidator.Required(fields, "payload");
            object result;
            switch (type)
            {
                case "bootstrap": result = ParseBootstrap(payload); break;
                case "ready": result = ParseReady(payload); break;
                case "memory": result = ParseCommand(payload); break;
                case "turn": result = ParseTurn(payload); break;
                case "result": result = ParseResultEnvelope(payload); break;
                case "error": result = ParseError(payload); break;
                case "cancel": case "shutdown":
                    ConversationContractValidator.ReadObject(payload, new string[0]);
                    result = new ConversationEmptyV2();
                    break;
                default: throw new FormatException("Unsupported player-memory protocol message type.");
            }
            return new ConversationProtocolMessage(Version, type, requestId, result);
        }

        public static string ReadRequestId(string json)
        {
            XmlElement root = ConversationContractValidator.ReadRoot(json, ConversationProtocol.MaximumEnvelopeBytes);
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(root,
                new[] { "protocolVersion", "type", "requestId", "payload" });
            if (ConversationContractValidator.ReadInt(fields["protocolVersion"]) != Version)
                throw new FormatException("Unsupported player-memory protocol version.");
            return ReadBoundedField(fields["requestId"], 128, false);
        }

        private static PlayerMemoryBootstrapV3 ParseBootstrap(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "apiKey", "playerId", "memoryRoot" });
            string key = ReadBounded(fields, "apiKey", 4096, true);
            string player = ReadBounded(fields, "playerId", 128, false);
            string path = ReadBounded(fields, "memoryRoot", 1024, false);
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetFileName(path), "player-memory.sqlite3", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "Memory", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Memory root is invalid.");
            return new PlayerMemoryBootstrapV3(key, player, Path.GetFullPath(path));
        }

        private static PlayerMemoryCommandV3 ParseCommand(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "operation", "playerId", "consentRevision", "nowUtc", "data" });
            string operation = ConversationContractValidator.ReadString(fields["operation"]);
            if (!Operations.Contains(operation)) throw new FormatException("Unsupported memory operation.");
            string player = ReadBounded(fields, "playerId", 128, false);
            int revision = ConversationContractValidator.ReadInt(fields["consentRevision"]);
            if (revision < 0) throw new FormatException("Consent revision is invalid.");
            string now = ReadUtc(fields["nowUtc"]);
            XmlElement data = fields["data"];
            ValidateCommandData(operation, data);
            return new PlayerMemoryCommandV3(operation, player, revision, now, BuildCommandData(operation, player, data));
        }

        private static PlayerMemoryTurnV3 ParseTurn(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "conversation", "memory" });
            ConversationJobV2 conversation = ConversationProtocol.ParseJobPayload(fields["conversation"]);
            Dictionary<string, XmlElement> memory = ConversationContractValidator.ReadObject(fields["memory"],
                new[] { "playerId", "consentRevision", "nowUtc", "currentPlayerSourceId", "focusAllowed", "quiet",
                    "explicitRecall", "authorizedQuestionIntent", "memoryPolicyRequired", "presentedHistorySourceIds" });
            string player = ReadBoundedField(memory["playerId"], 128, false);
            int revision = ConversationContractValidator.ReadInt(memory["consentRevision"]);
            if (revision < 0) throw new FormatException("Memory consent revision is invalid.");
            DateTimeOffset now = DateTimeOffset.Parse(ReadUtc(memory["nowUtc"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            string currentSource = ReadBoundedField(memory["currentPlayerSourceId"], 128, false);
            bool? focus = NullableBoolean(memory["focusAllowed"]);
            bool? quiet = NullableBoolean(memory["quiet"]);
            bool explicitRecall = ConversationContractValidator.ReadBoolean(memory["explicitRecall"]);
            string questionIntent = NullableString(memory["authorizedQuestionIntent"], 64);
            if (!string.IsNullOrEmpty(questionIntent) && questionIntent != "preferred_name" && questionIntent != "interest" &&
                questionIntent != "shared_activity_preference") throw new FormatException("Memory question intent is invalid.");
            bool policyRequired = ConversationContractValidator.ReadBoolean(memory["memoryPolicyRequired"]);
            List<string> historySources = ConversationContractValidator.ReadStringArray(memory["presentedHistorySourceIds"], ConversationProtocol.MaximumHistoryUtterances);
            if (historySources.Count != conversation.presentedHistory.Count)
                throw new FormatException("Memory history source IDs are misaligned.");
            var policy = new PlayerMemoryTurnPolicyV3(player, revision, now, currentSource, focus, quiet,
                explicitRecall, questionIntent, policyRequired, historySources);
            return new PlayerMemoryTurnV3(conversation, policy);
        }

        private static object ParseResultEnvelope(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new string[0],
                new[] { "conversation", "status", "reasonCode", "consent", "memory", "context", "page", "lastSequence", "count", "stepId", "completed" });
            XmlElement conversation;
            if (fields.TryGetValue("conversation", out conversation))
            {
                if (fields.Count != 1) throw new FormatException("Memory turn result shape is invalid.");
                return ConversationProtocol.ParseResultPayload(conversation);
            }
            return ParseResult(element);
        }

        private static PlayerMemoryCommandDataV3 BuildCommandData(string operation, string playerId, XmlElement data)
        {
            switch (operation)
            {
                case "consent":
                    Dictionary<string, XmlElement> consent = ConversationContractValidator.ReadObject(data, new[] { "enabled" });
                    return new PlayerMemoryConsentDataV3(ConversationContractValidator.TypeOf(consent["enabled"]) == "null"
                        ? (bool?)null : ConversationContractValidator.ReadBoolean(consent["enabled"]));
                case "profile_set":
                    Dictionary<string, XmlElement> profile = ConversationContractValidator.ReadObject(data, new[] { "proposal", "evidence" });
                    return new PlayerMemoryProfileSetDataV3(ParseProposal(profile["proposal"]), ParseEvidence(profile["evidence"]));
                case "confirm":
                    Dictionary<string, XmlElement> confirmation = ConversationContractValidator.ReadObject(data, new[] { "proposal", "evidence", "confirmation" });
                    return new PlayerMemoryConfirmDataV3(ParseProposal(confirmation["proposal"]), ParseEvidence(confirmation["evidence"]),
                        ParseEvidence(confirmation["confirmation"]));
                case "context":
                    Dictionary<string, XmlElement> context = ConversationContractValidator.ReadObject(data, new[] { "query" });
                    return new PlayerMemoryContextDataV3(ReadBoundedField(context["query"], 2048, true));
                case "list":
                    Dictionary<string, XmlElement> list = ConversationContractValidator.ReadObject(data, new[] { "cursor", "limit", "includeSuperseded" });
                    return new PlayerMemoryListDataV3(ReadBoundedField(list["cursor"], 256, true),
                        ConversationContractValidator.ReadInt(list["limit"]), ConversationContractValidator.ReadBoolean(list["includeSuperseded"]));
                case "forget":
                    Dictionary<string, XmlElement> forget = ConversationContractValidator.ReadObject(data, new[] { "memoryId" });
                    return new PlayerMemoryForgetDataV3(ReadBoundedField(forget["memoryId"], 128, false));
                case "onboarding":
                    Dictionary<string, XmlElement> onboarding = ConversationContractValidator.ReadObject(data, new[] { "stepId", "completed" });
                    return new PlayerMemoryOnboardingDataV3(ReadBoundedField(onboarding["stepId"], 64, false),
                        ConversationContractValidator.ReadBoolean(onboarding["completed"]));
                case "onboarding_read":
                    ConversationContractValidator.ReadObject(data, new string[0]);
                    return new PlayerMemoryOnboardingReadDataV3();
                case "question_ack":
                    Dictionary<string, XmlElement> question = ConversationContractValidator.ReadObject(data,
                        new[] { "intent", "sessionId", "eventId", "presentedAtUtc" });
                    string presented = ReadUtc(question["presentedAtUtc"]);
                    return new PlayerMemoryQuestionAckDataV3(ReadBoundedField(question["intent"], 64, false),
                        ReadBoundedField(question["sessionId"], 128, false), ReadBoundedField(question["eventId"], 128, false),
                        DateTimeOffset.Parse(presented, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
                case "source_sync":
                    Dictionary<string, XmlElement> sync = ConversationContractValidator.ReadObject(data, new[] { "afterSequence", "throughSequence", "events" });
                    long after = ReadLong(sync["afterSequence"]);
                    long through = ReadLong(sync["throughSequence"]);
                    if (after < 0 || through < after) throw new FormatException("Source cursor range is invalid.");
                    var events = new List<PlayerMemorySourceSyncEvent>();
                    foreach (XmlElement item in ConversationContractValidator.ReadArray(sync["events"], MaximumEventsPerSync))
                    {
                        Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(item, new[] { "sequence", "evidence" });
                        PlayerMemoryEvidence evidence = ParseEvidence(fields["evidence"]);
                        if (evidence.playerId != playerId) throw new FormatException("Source player binding is invalid.");
                        long sequence = ReadLong(fields["sequence"]);
                        if (sequence <= after || sequence > through || (events.Count > 0 && sequence <= events[events.Count - 1].sequence))
                            throw new FormatException("Source event is outside its cursor range.");
                        events.Add(new PlayerMemorySourceSyncEvent(sequence, evidence));
                    }
                    return new PlayerMemorySourceSyncDataV3(after, through, events);
                default: throw new FormatException("Unsupported memory operation.");
            }
        }

        private static PlayerMemoryProposal ParseProposal(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "category", "key", "value", "scope", "sourceId", "evidenceQuote" });
            return new PlayerMemoryProposal
            {
                category = ReadBoundedField(fields["category"], 80, false), key = ReadBoundedField(fields["key"], 80, false),
                value = ReadBoundedField(fields["value"], 512, false), scope = ReadBoundedField(fields["scope"], 32, false),
                sourceId = ReadBoundedField(fields["sourceId"], 128, false), evidenceQuote = ReadBoundedField(fields["evidenceQuote"], 512, false)
            };
        }

        private static PlayerMemoryEvidence ParseEvidence(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "playerId", "sessionId", "sourceId", "actor", "scope", "sourceKind", "text", "eventAtUtc", "explicitlyConfirmed" });
            DateTimeOffset? eventAt = ConversationContractValidator.TypeOf(fields["eventAtUtc"]) == "null" ? (DateTimeOffset?)null :
                DateTimeOffset.Parse(ReadUtc(fields["eventAtUtc"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return new PlayerMemoryEvidence
            {
                playerId = ReadBoundedField(fields["playerId"], 128, false), sessionId = ReadBoundedField(fields["sessionId"], 128, true),
                sourceId = ReadBoundedField(fields["sourceId"], 128, false),
                actor = ReadBoundedField(fields["actor"], 32, false), scope = ReadBoundedField(fields["scope"], 32, false),
                sourceKind = ReadBoundedField(fields["sourceKind"], 32, false), text = ReadBoundedField(fields["text"], 512, false),
                eventAtUtc = eventAt, explicitlyConfirmed = ConversationContractValidator.ReadBoolean(fields["explicitlyConfirmed"])
            };
        }

        private static void ValidateCommandData(string operation, XmlElement data)
        {
            switch (operation)
            {
                case "consent":
                    Dictionary<string, XmlElement> consentFields = ConversationContractValidator.ReadObject(data, new[] { "enabled" });
                    if (ConversationContractValidator.TypeOf(consentFields["enabled"]) != "null")
                        ConversationContractValidator.ReadBoolean(consentFields["enabled"]);
                    break;
                case "source_sync": ValidateSourceSync(data); break;
                case "profile_set":
                    ConversationContractValidator.ReadObject(data, new[] { "proposal", "evidence" });
                    ValidateProposalElement(data["proposal"]); ValidateEvidenceElement(data["evidence"]); break;
                case "confirm":
                    ConversationContractValidator.ReadObject(data, new[] { "proposal", "evidence", "confirmation" });
                    ValidateProposalElement(data["proposal"]); ValidateEvidenceElement(data["evidence"]); ValidateEvidenceElement(data["confirmation"]); break;
                case "context": ConversationContractValidator.ReadObject(data, new[] { "query" }); ReadBoundedField(data["query"], 2048, true); break;
                case "list":
                    ConversationContractValidator.ReadObject(data, new[] { "cursor", "limit", "includeSuperseded" });
                    ReadBoundedField(data["cursor"], 256, true);
                    int limit = ConversationContractValidator.ReadInt(data["limit"]);
                    if (limit < 1 || limit > 100) throw new FormatException("Memory page size is invalid.");
                    ConversationContractValidator.ReadBoolean(data["includeSuperseded"]); break;
                case "forget": ConversationContractValidator.ReadObject(data, new[] { "memoryId" }); ReadBoundedField(data["memoryId"], 128, false); break;
                case "onboarding":
                    ConversationContractValidator.ReadObject(data, new[] { "stepId", "completed" });
                    ReadBoundedField(data["stepId"], 64, false); ConversationContractValidator.ReadBoolean(data["completed"]); break;
                case "onboarding_read": ConversationContractValidator.ReadObject(data, new string[0]); break;
                case "question_ack": ValidateQuestionAck(data); break;
            }
        }

        private static void ValidateSourceSync(XmlElement data)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(data, new[] { "afterSequence", "throughSequence", "events" });
            long afterSequence = ReadLong(fields["afterSequence"]);
            long throughSequence = ReadLong(fields["throughSequence"]);
            if (afterSequence < 0 || throughSequence < afterSequence) throw new FormatException("Source cursor is invalid.");
            long previousSequence = afterSequence;
            int totalBytes = 0;
            foreach (XmlElement item in ConversationContractValidator.ReadArray(fields["events"], MaximumEventsPerSync))
            {
                Dictionary<string, XmlElement> eventFields = ConversationContractValidator.ReadObject(item, new[] { "sequence", "evidence" });
                long sequence = ReadLong(eventFields["sequence"]);
                if (sequence <= previousSequence || sequence > throughSequence) throw new FormatException("Source sequence is invalid.");
                previousSequence = sequence;
                ValidateEvidenceElement(eventFields["evidence"]);
                Dictionary<string, XmlElement> evidence = ConversationContractValidator.ReadObject(eventFields["evidence"],
                    new[] { "playerId", "sessionId", "sourceId", "actor", "scope", "sourceKind", "text", "eventAtUtc", "explicitlyConfirmed" });
                totalBytes += Encoding.UTF8.GetByteCount(ConversationContractValidator.ReadString(evidence["text"]));
                if (totalBytes > MaximumSourceSyncBytes) throw new FormatException("Source sync exceeds its limit.");
            }
        }

        private static void ValidateProposalElement(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "category", "key", "value", "scope", "sourceId", "evidenceQuote" });
            ReadBoundedField(fields["category"], 80, false); ReadBoundedField(fields["key"], 80, false);
            ReadBoundedField(fields["value"], 512, false); ReadBoundedField(fields["scope"], 32, false);
            ReadBoundedField(fields["sourceId"], 128, false); ReadBoundedField(fields["evidenceQuote"], 512, false);
        }

        private static void ValidateEvidenceElement(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element,
                new[] { "playerId", "sessionId", "sourceId", "actor", "scope", "sourceKind", "text", "eventAtUtc", "explicitlyConfirmed" });
            ReadBoundedField(fields["playerId"], 128, false); ReadBoundedField(fields["sessionId"], 128, true);
            ReadBoundedField(fields["sourceId"], 128, false);
            string actor = ReadBoundedField(fields["actor"], 32, false);
            string scope = ReadBoundedField(fields["scope"], 32, false);
            string sourceKind = ReadBoundedField(fields["sourceKind"], 32, false);
            if (actor != "player" && actor != "game" && actor != "alterra" && actor != "assistant" && actor != "third_party")
                throw new FormatException("Source actor is invalid.");
            if (scope != "real_world_claim" && scope != "in_game_experience" && scope != "fictional" && scope != "unclassified")
                throw new FormatException("Source scope is invalid.");
            if (sourceKind != "player_message" && sourceKind != "player_checkin" && sourceKind != "onboarding" &&
                sourceKind != "profile_input" && sourceKind != "player_confirmation" && sourceKind != "game_event" &&
                sourceKind != "generated_dialogue" && sourceKind != "assistant_message") throw new FormatException("Source kind is invalid.");
            ReadBoundedField(fields["text"], 512, false);
            if (ConversationContractValidator.TypeOf(fields["eventAtUtc"]) == "string") ReadUtc(fields["eventAtUtc"]);
            else if (ConversationContractValidator.TypeOf(fields["eventAtUtc"]) != "null") throw new FormatException("Source timestamp is invalid.");
            ConversationContractValidator.ReadBoolean(fields["explicitlyConfirmed"]);
        }

        private static void ValidateQuestionAck(XmlElement data)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(data,
                new[] { "intent", "sessionId", "eventId", "presentedAtUtc" });
            string intent = ReadBoundedField(fields["intent"], 64, false);
            if (intent != "preferred_name" && intent != "interest" && intent != "shared_activity_preference")
                throw new FormatException("Question intent is invalid.");
            ReadBoundedField(fields["sessionId"], 128, false); ReadBoundedField(fields["eventId"], 128, false);
            ReadUtc(fields["presentedAtUtc"]);
        }

        private static PlayerMemoryReadyV3 ParseReady(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "runtimeVersion", "modelId", "capabilities" });
            Dictionary<string, XmlElement> capabilities = ConversationContractValidator.ReadObject(fields["capabilities"], new[] { "memory", "generation" });
            bool memory = ConversationContractValidator.ReadBoolean(capabilities["memory"]);
            bool generation = ConversationContractValidator.ReadBoolean(capabilities["generation"]);
            if (!memory) throw new FormatException("Required memory capability is unavailable.");
            return new PlayerMemoryReadyV3(ReadBoundedField(fields["runtimeVersion"], 64, false),
                ReadBoundedField(fields["modelId"], 128, true), memory, generation);
        }

        private static PlayerMemoryResultV3 ParseResult(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "status" },
                new[] { "reasonCode", "consent", "memory", "context", "page", "lastSequence", "count", "stepId", "completed" });
            return new PlayerMemoryResultV3(ReadBoundedField(fields["status"], 64, false), OptionalString(fields, "reasonCode"),
                OptionalString(fields, "stepId"), OptionalConsent(fields, "consent"), OptionalRecord(fields, "memory"),
                OptionalContext(fields, "context"), OptionalPage(fields, "page"), OptionalLong(fields, "lastSequence"),
                OptionalInt(fields, "count"), OptionalBoolean(fields, "completed"));
        }

        private static PlayerMemoryErrorV3 ParseError(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ConversationContractValidator.ReadObject(element, new[] { "code", "retryable" });
            string code = ReadBoundedField(fields["code"], 64, false);
            if (code != "memory_unavailable" && code != "schema_unsupported" && code != "invalid_job" &&
                code != "provider_auth" && code != "provider_payment_required" && code != "provider_rate_limited" &&
                code != "provider_unavailable" && code != "provider_timeout" && code != "provider_refusal" &&
                code != "persona_unavailable" && code != "invalid_reply" && code != "internal_error")
                throw new FormatException("Unsupported memory error code.");
            return new PlayerMemoryErrorV3(code, ConversationContractValidator.ReadBoolean(fields["retryable"]));
        }

        private static PlayerMemoryConsentV3 OptionalConsent(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; if (!fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null") return null;
            Dictionary<string, XmlElement> item = ConversationContractValidator.ReadObject(element, new[] { "playerId", "enabled", "revision" });
            return new PlayerMemoryConsentV3(ReadBoundedField(item["playerId"], 128, false),
                ConversationContractValidator.ReadBoolean(item["enabled"]), ConversationContractValidator.ReadInt(item["revision"]));
        }

        private static PlayerMemoryRecordV3 OptionalRecord(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; if (!fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null") return null;
            return ParseRecord(element);
        }

        private static PlayerMemoryRecordV3 ParseRecord(XmlElement element)
        {
            Dictionary<string, XmlElement> f = ConversationContractValidator.ReadObject(element,
                new[] { "memoryId", "playerId", "category", "key", "value", "actor", "scope", "sourceKind", "sourceId", "sourceText",
                    "eventAtUtc", "recordedAtUtc", "validUntilUtc", "freshUntilUtc", "status", "supersedesId" });
            return new PlayerMemoryRecordV3(ReadBoundedField(f["memoryId"], 128, false), ReadBoundedField(f["playerId"], 128, false),
                ReadBoundedField(f["category"], 80, false), ReadBoundedField(f["key"], 80, false), ReadBoundedField(f["value"], 512, false),
                ReadBoundedField(f["actor"], 32, false), ReadBoundedField(f["scope"], 32, false), ReadBoundedField(f["sourceKind"], 32, false),
                ReadBoundedField(f["sourceId"], 128, false), ReadBoundedField(f["sourceText"], 512, true),
                NullableTimestamp(f["eventAtUtc"]), ReadUtc(f["recordedAtUtc"]), NullableTimestamp(f["validUntilUtc"]),
                NullableTimestamp(f["freshUntilUtc"]), ReadBoundedField(f["status"], 32, false), NullableString(f["supersedesId"], 128));
        }

        private static PlayerMemoryContextV3 OptionalContext(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; if (!fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null") return null;
            Dictionary<string, XmlElement> f = ConversationContractValidator.ReadObject(element, new[] { "core", "current", "recalled", "suppressedSourceIds" });
            return new PlayerMemoryContextV3(ReadRecords(f["core"]), ReadRecords(f["current"]), ReadRecords(f["recalled"]),
                ConversationContractValidator.ReadStringArray(f["suppressedSourceIds"], 128));
        }

        private static PlayerMemoryPageV3 OptionalPage(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; if (!fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null") return null;
            Dictionary<string, XmlElement> f = ConversationContractValidator.ReadObject(element, new[] { "status", "items", "nextCursor", "hasMore" });
            if (ReadBoundedField(f["status"], 16, false) != "ok") throw new FormatException("Memory page status is invalid.");
            return new PlayerMemoryPageV3(ReadRecords(f["items"]), ReadBoundedField(f["nextCursor"], 256, false),
                ConversationContractValidator.ReadBoolean(f["hasMore"]));
        }

        private static List<PlayerMemoryRecordV3> ReadRecords(XmlElement element)
        {
            var records = new List<PlayerMemoryRecordV3>();
            foreach (XmlElement item in ConversationContractValidator.ReadArray(element, 100)) records.Add(ParseRecord(item));
            return records;
        }

        private static string Command(string requestId, string operation, string playerId, int revision,
            DateTimeOffset now, string data)
        {
            RequireText(requestId, 128, "requestId"); RequireText(playerId, 128, "playerId");
            if (!Operations.Contains(operation) || revision < 0) throw new FormatException("Memory command is invalid.");
            return Envelope("memory", requestId, "{\"operation\":" + JsonString(operation) + ",\"playerId\":" + JsonString(playerId) +
                ",\"consentRevision\":" + revision.ToString(CultureInfo.InvariantCulture) + ",\"nowUtc\":" + JsonString(IsoUtc(now)) +
                ",\"data\":" + data + "}");
        }

        private static string Envelope(string type, string requestId, string payload)
        {
            string json = "{\"protocolVersion\":3,\"type\":" + JsonString(type) + ",\"requestId\":" + JsonString(requestId) + ",\"payload\":" + payload + "}";
            if (Encoding.UTF8.GetByteCount(json) > ConversationProtocol.MaximumEnvelopeBytes) throw new FormatException("Memory envelope exceeds limit.");
            return json;
        }

        private static string SerializeProposal(PlayerMemoryProposal value)
        {
            return "{\"category\":" + JsonString(value.category) + ",\"key\":" + JsonString(value.key) +
                ",\"value\":" + JsonString(value.value) + ",\"scope\":" + JsonString(value.scope) +
                ",\"sourceId\":" + JsonString(value.sourceId) + ",\"evidenceQuote\":" + JsonString(value.evidenceQuote) + "}";
        }

        private static string SerializeEvidence(PlayerMemoryEvidence value)
        {
            return "{\"playerId\":" + JsonString(value.playerId) + ",\"sessionId\":" + JsonString(value.sessionId ?? string.Empty) +
                ",\"sourceId\":" + JsonString(value.sourceId) +
                ",\"actor\":" + JsonString(value.actor) + ",\"scope\":" + JsonString(value.scope) +
                ",\"sourceKind\":" + JsonString(value.sourceKind) + ",\"text\":" + JsonString(value.text) +
                ",\"eventAtUtc\":" + (value.eventAtUtc.HasValue ? JsonString(IsoUtc(value.eventAtUtc.Value)) : "null") +
                ",\"explicitlyConfirmed\":" + Bool(value.explicitlyConfirmed) + "}";
        }

        private static void ValidateProposal(PlayerMemoryProposal p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            Bounded(p.category, 80, "category"); Bounded(p.key, 80, "key"); Bounded(p.value, 512, "value");
            Bounded(p.scope, 32, "scope"); Bounded(p.sourceId, 128, "sourceId"); Bounded(p.evidenceQuote, 512, "evidenceQuote");
        }

        private static void ValidateEvidence(PlayerMemoryEvidence e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            Bounded(e.playerId, 128, "playerId"); Bounded(e.sessionId ?? string.Empty, 128, "sessionId", true);
            Bounded(e.sourceId, 128, "sourceId"); Bounded(e.actor, 32, "actor");
            Bounded(e.scope, 32, "scope"); Bounded(e.sourceKind, 32, "sourceKind"); Bounded(e.text, 512, "sourceText");
        }

        private static string ReadBounded(Dictionary<string, XmlElement> fields, string name, int max, bool allowEmpty)
        { return ReadBoundedField(fields[name], max, allowEmpty); }

        private static string ReadBoundedField(XmlElement element, int max, bool allowEmpty)
        {
            string value = ConversationContractValidator.ReadString(element);
            return Bounded(value, max, allowEmpty ? "optional" : "required", allowEmpty);
        }

        private static string Bounded(string value, int max, string field, bool allowEmpty = false)
        {
            if (value == null || value.Length > max || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
                throw new FormatException("Memory field " + field + " is invalid.");
            return value;
        }

        private static string OptionalString(Dictionary<string, XmlElement> fields, string name)
        { XmlElement e; return fields.TryGetValue(name, out e) && ConversationContractValidator.TypeOf(e) != "null" ? ReadBoundedField(e, 128, true) : ""; }
        private static string NullableString(XmlElement e, int max)
        { return ConversationContractValidator.TypeOf(e) == "null" ? "" : ReadBoundedField(e, max, true); }
        private static string NullableTimestamp(XmlElement e)
        { return ConversationContractValidator.TypeOf(e) == "null" ? "" : ReadUtc(e); }
        private static string ReadUtc(XmlElement e)
        {
            string value = ReadBoundedField(e, 40, false);
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) || parsed.Offset != TimeSpan.Zero)
                throw new FormatException("Memory timestamp must be UTC.");
            return parsed.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }
        private static long? OptionalLong(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; if (!fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null") return null;
            long value; if (ConversationContractValidator.TypeOf(element) != "number" || !long.TryParse(element.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Memory sequence is invalid.");
            return value;
        }
        private static long ReadLong(XmlElement element)
        {
            long value;
            if (ConversationContractValidator.TypeOf(element) != "number" ||
                !long.TryParse(element.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Expected an integer JSON sequence.");
            return value;
        }
        private static int? OptionalInt(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; return !fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null" ? (int?)null : ConversationContractValidator.ReadInt(element);
        }
        private static bool? OptionalBoolean(Dictionary<string, XmlElement> fields, string name)
        {
            XmlElement element; return !fields.TryGetValue(name, out element) || ConversationContractValidator.TypeOf(element) == "null" ? (bool?)null : ConversationContractValidator.ReadBoolean(element);
        }
        private static bool? NullableBoolean(XmlElement element)
        { return ConversationContractValidator.TypeOf(element) == "null" ? (bool?)null : ConversationContractValidator.ReadBoolean(element); }
        private static string Bool(bool value) { return value ? "true" : "false"; }
        private static string NullableBool(bool? value) { return value.HasValue ? Bool(value.Value) : "null"; }
        private static string IsoUtc(DateTimeOffset value) { return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture); }
        private static void RequireText(string value, int max, string field) { Bounded(value, max, field); }
        private static string JsonString(string value)
        {
            if (value == null) throw new FormatException("Memory text is required.");
            var builder = new StringBuilder(value.Length + 2).Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else builder.Append(c); break;
                }
            }
            return builder.Append('"').ToString();
        }
    }
}
