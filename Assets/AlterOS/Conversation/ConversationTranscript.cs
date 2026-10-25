using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AlterOS.Conversation
{
    public enum ConversationAppendStatus
    {
        Appended,
        Duplicate,
        Unsaved
    }

    public sealed class ConversationAppendResult
    {
        public readonly ConversationAppendStatus Status;
        public readonly long sequence;
        public readonly string Error;

        internal ConversationAppendResult(ConversationAppendStatus status, long eventSequence, string error)
        { Status = status; sequence = eventSequence; Error = error ?? string.Empty; }
    }

    public sealed class ConversationTranscriptEvent
    {
        public readonly string eventId, eventType, sessionId, requestId, responseId, nodeId;
        public readonly string source, channel, topicId, scenarioId, judgmentKind;
        public readonly string speaker, text, displayText, expression, responseJson, statusCode, timestampUtc;

        internal bool HasSamePayload(ConversationTranscriptEvent other)
        {
            return other != null && eventType == other.eventType && sessionId == other.sessionId && requestId == other.requestId &&
                responseId == other.responseId && nodeId == other.nodeId && source == other.source && channel == other.channel &&
                topicId == other.topicId && scenarioId == other.scenarioId && judgmentKind == other.judgmentKind &&
                speaker == other.speaker && text == other.text && displayText == other.displayText && expression == other.expression &&
                responseJson == other.responseJson && statusCode == other.statusCode;
        }

        private ConversationTranscriptEvent(string id, string kind, string session, string request, string response, string node,
            string eventSource, string eventChannel, string topic, string scenario, string judgment, string eventSpeaker,
            string eventText, string display, string cue, string serializedResponse, string stableStatus, DateTimeOffset timestamp)
        {
            eventId = id; eventType = kind; sessionId = session; requestId = request; responseId = response; nodeId = node;
            source = eventSource; channel = eventChannel; topicId = topic; scenarioId = scenario; judgmentKind = judgment;
            speaker = eventSpeaker; text = eventText; displayText = display; expression = cue; responseJson = serializedResponse;
            statusCode = stableStatus; timestampUtc = timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        public static ConversationTranscriptEvent RequestReceived(string session, string request, string eventIdentity,
            string turnChannel, string playerText, DateTimeOffset timestamp)
        {
            return Create(eventIdentity, "request_received", session, request, "", "", "player", turnChannel,
                "", "", "", "Player", playerText, playerText, "", "", "", timestamp);
        }

        public static ConversationTranscriptEvent ResponseReceived(string session, string request, string response,
            string eventIdentity, string judgment, string validatedResponseJson, DateTimeOffset timestamp)
        {
            return Create(eventIdentity, "response_received", session, request, response, "", "generated", "direct",
                "", "", judgment, "", "", "", "", validatedResponseJson, "", timestamp);
        }

        public static ConversationTranscriptEvent NodePresented(string session, string response, string node,
            string eventIdentity, string eventSource, string eventChannel, string topic, string scenario, string eventSpeaker,
            string speechText, string englishDisplayText, string cue, DateTimeOffset timestamp)
        {
            return Create(eventIdentity, "node_presented", session, "", response, node, eventSource, eventChannel,
                topic, scenario, "", eventSpeaker, speechText, englishDisplayText, cue, "", "", timestamp);
        }

        public static ConversationTranscriptEvent TurnStatus(string session, string request, string response,
            string eventIdentity, string stableStatus, DateTimeOffset timestamp)
        {
            return Create(eventIdentity, "turn_status", session, request, response, "", "generated", "direct",
                "", "", "", "", "", "", "", "", stableStatus, timestamp);
        }

        private static ConversationTranscriptEvent Create(string id, string kind, string session, string request,
            string response, string node, string eventSource, string eventChannel, string topic, string scenario,
            string judgment, string eventSpeaker, string eventText, string display, string cue, string serializedResponse,
            string stableStatus, DateTimeOffset timestamp)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || string.IsNullOrWhiteSpace(session) || session.Length > 128)
                throw new ArgumentException("Transcript event identity is invalid.");
            if (request == null || request.Length > 128 || response == null || response.Length > 128 || node == null || node.Length > 64)
                throw new ArgumentException("Transcript event binding is invalid.");
            if (eventSource == null || eventSource.Length > 32 || eventChannel == null || eventChannel.Length > 16 ||
                topic == null || topic.Length > 128 || scenario == null || scenario.Length > 128 ||
                judgment == null || judgment.Length > 32 || eventSpeaker == null || eventSpeaker.Length > 32 ||
                stableStatus == null || stableStatus.Length > 64)
                throw new ArgumentException("Transcript event metadata is invalid.");
            if (eventText == null || eventText.Length > 2048 || display == null || display.Length > 600 || cue == null || cue.Length > 64)
                throw new ArgumentException("Transcript event text is invalid.");
            if (serializedResponse == null || Encoding.UTF8.GetByteCount(serializedResponse) > ConversationProtocol.MaximumResultBytes)
                throw new ArgumentException("Transcript response exceeds its limit.");
            if (kind == "request_received" && (request.Length == 0 || string.IsNullOrWhiteSpace(eventText) || (eventChannel != "direct" && eventChannel != "phone")))
                throw new ArgumentException("Transcript request is invalid.");
            if (kind == "response_received" && (request.Length == 0 || response.Length == 0 ||
                (judgment != "existing" && judgment != "existing_uncovered" && judgment != "continue" && judgment != "clarify" && judgment != "novel_candidate" && judgment != "scenario") || serializedResponse.Length == 0))
                throw new ArgumentException("Transcript response receipt is invalid.");
            if (kind == "node_presented" && (response.Length == 0 || node.Length == 0 || string.IsNullOrWhiteSpace(eventText) || string.IsNullOrWhiteSpace(display) ||
                (eventSource != "authored" && eventSource != "generated") || (eventChannel != "direct" && eventChannel != "phone")))
                throw new ArgumentException("Presented node is invalid.");
            if (kind == "turn_status" && (request.Length == 0 || (stableStatus != "pending" && stableStatus != "cancelled" && stableStatus != "failed" &&
                stableStatus != "delivered" && stableStatus != "late")))
                throw new ArgumentException("Transcript status is invalid.");
            return new ConversationTranscriptEvent(id, kind, session, request, response, node, eventSource, eventChannel,
                topic, scenario, judgment, eventSpeaker, eventText, display, cue, serializedResponse, stableStatus, timestamp);
        }
    }

    public sealed class ConversationTranscriptRecord
    {
        public readonly long sequence;
        public readonly ConversationTranscriptEvent value;
        internal ConversationTranscriptRecord(long eventSequence, ConversationTranscriptEvent transcriptEvent)
        { sequence = eventSequence; value = transcriptEvent; }
    }

    public sealed class ConversationTranscriptPage
    {
        public readonly bool Success;
        public readonly string Error, nextCursor;
        public readonly ReadOnlyCollection<ConversationTranscriptRecord> events;
        internal ConversationTranscriptPage(bool success, string error, string cursor, IEnumerable<ConversationTranscriptRecord> items)
        { Success = success; Error = error ?? string.Empty; nextCursor = cursor ?? "0"; events = Array.AsReadOnly(new List<ConversationTranscriptRecord>(items).ToArray()); }
    }

    public sealed class ConversationHistoryUtterance
    {
        public readonly long sequence;
        public readonly string sourceId, speaker, source, responseId, nodeId, text;
        internal ConversationHistoryUtterance(long eventSequence, string eventSpeaker, string eventSource,
            string response, string node, string utteranceText, string transcriptSourceId)
        { sequence = eventSequence; speaker = eventSpeaker; source = eventSource; responseId = response; nodeId = node; text = utteranceText; sourceId = transcriptSourceId ?? ""; }
    }

    public sealed class ConversationHistorySnapshot
    {
        public readonly bool Success;
        public readonly string Error;
        public readonly ReadOnlyCollection<ConversationHistoryUtterance> utterances;
        internal ConversationHistorySnapshot(bool success, string error, IEnumerable<ConversationHistoryUtterance> items)
        { Success = success; Error = error ?? string.Empty; utterances = Array.AsReadOnly(new List<ConversationHistoryUtterance>(items).ToArray()); }
    }

    [DataContract]
    internal sealed class ConversationTranscriptDiskRecord
    {
        [DataMember(Order = 1)] public int schemaVersion;
        [DataMember(Order = 2)] public long sequence;
        [DataMember(Order = 3)] public string eventId;
        [DataMember(Order = 4)] public string eventType;
        [DataMember(Order = 5)] public string sessionId;
        [DataMember(Order = 6)] public string requestId;
        [DataMember(Order = 7)] public string responseId;
        [DataMember(Order = 8)] public string nodeId;
        [DataMember(Order = 9)] public string source;
        [DataMember(Order = 10)] public string channel;
        [DataMember(Order = 11)] public string topicId;
        [DataMember(Order = 12)] public string scenarioId;
        [DataMember(Order = 13)] public string judgmentKind;
        [DataMember(Order = 14)] public string speaker;
        [DataMember(Order = 15)] public string text;
        [DataMember(Order = 16)] public string displayText;
        [DataMember(Order = 17)] public string expression;
        [DataMember(Order = 18)] public string responseJson;
        [DataMember(Order = 19)] public string statusCode;
        [DataMember(Order = 20)] public string timestampUtc;

        public static ConversationTranscriptDiskRecord From(long sequence, ConversationTranscriptEvent value)
        {
            return new ConversationTranscriptDiskRecord
            {
                schemaVersion = 1, sequence = sequence, eventId = value.eventId, eventType = value.eventType,
                sessionId = value.sessionId, requestId = value.requestId, responseId = value.responseId,
                nodeId = value.nodeId, source = value.source, channel = value.channel, topicId = value.topicId,
                scenarioId = value.scenarioId, judgmentKind = value.judgmentKind, speaker = value.speaker,
                text = value.text, displayText = value.displayText, expression = value.expression,
                responseJson = value.responseJson, statusCode = value.statusCode, timestampUtc = value.timestampUtc
            };
        }

        public ConversationTranscriptEvent ToEvent()
        {
            DateTimeOffset timestamp;
            if (schemaVersion != 1 || !DateTimeOffset.TryParse(timestampUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out timestamp)) throw new FormatException("Invalid event metadata.");
            switch (eventType)
            {
                case "request_received": return ConversationTranscriptEvent.RequestReceived(sessionId, requestId, eventId, channel, text, timestamp);
                case "response_received": return ConversationTranscriptEvent.ResponseReceived(sessionId, requestId, responseId, eventId, judgmentKind, responseJson, timestamp);
                case "node_presented": return ConversationTranscriptEvent.NodePresented(sessionId, responseId, nodeId, eventId, source, channel,
                    topicId, scenarioId, speaker, text, displayText, expression, timestamp);
                case "turn_status": return ConversationTranscriptEvent.TurnStatus(sessionId, requestId, responseId, eventId, statusCode, timestamp);
                default: throw new FormatException("Unsupported event type.");
            }
        }
    }

    /// <summary>Append-only private conversation transcript backed by the local conversations database.</summary>
    public sealed class ConversationTranscript
    {
        public const int MaximumEventBytes = 256 * 1024;
        private const int MaximumPageSize = 100;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly IConversationTranscriptStore store;

        public bool IsReady { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        private static DataContractJsonSerializer CreateSerializer()
        { return new DataContractJsonSerializer(typeof(ConversationTranscriptDiskRecord)); }

        public ConversationTranscript(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Conversation database path is required.");
            var sqliteStore = new SqliteTranscriptStore(databasePath);
            store = sqliteStore;
            IsReady = sqliteStore.IsReady;
            LastError = IsReady ? string.Empty : "storage_unavailable";
        }

        private ConversationTranscript(string databasePath, string legacyPath)
        {
            var sqliteStore = new SqliteTranscriptStore(databasePath);
            store = sqliteStore;
            ConversationTranscriptImportResult import = TranscriptJsonlImporter.ImportOnce(legacyPath, sqliteStore);
            IsReady = import.State == "missing" || import.State == "imported" || import.State == "already_imported";
            IsReady = IsReady && sqliteStore.IsReady;
            LastError = IsReady ? string.Empty :
                (import.State == "conflict" ? "transcript_conflict" : "transcript_recovery_required");
        }

        public static ConversationTranscript CreateInPersistentDataPath()
        {
            string root = UnityEngine.Application.persistentDataPath;
            string databasePath = Path.Combine(root, "Data", "conversations.sqlite3");
            string legacyPath = Path.Combine(root, "alteros-conversation-v2.jsonl");
            return new ConversationTranscript(databasePath, legacyPath);
        }

        public ConversationAppendResult Append(ConversationTranscriptEvent value)
        {
            if (!IsReady) return new ConversationAppendResult(ConversationAppendStatus.Unsaved, 0, "storage_unavailable");
            return store.Append(value);
        }

        public ConversationTranscriptPage ReadPage(string cursor, int count)
        {
            long afterSequence;
            if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out afterSequence) || afterSequence < 0 || count < 1 || count > MaximumPageSize)
                return new ConversationTranscriptPage(false, "invalid_page", "0", new ConversationTranscriptRecord[0]);
            if (!IsReady)
                return new ConversationTranscriptPage(false, "storage_unavailable", afterSequence.ToString(CultureInfo.InvariantCulture), new ConversationTranscriptRecord[0]);
            return store.ReadPage(cursor, count);
        }

        public ConversationHistorySnapshot ReadRecent(int maxUtterances, int maxCharacters)
        {
            if (maxUtterances < 1 || maxUtterances > 12 || maxCharacters < 1 || maxCharacters > 6000)
                return new ConversationHistorySnapshot(false, "invalid_page", new ConversationHistoryUtterance[0]);
            if (!IsReady) return new ConversationHistorySnapshot(false, "storage_unavailable", new ConversationHistoryUtterance[0]);
            return store.ReadRecent(maxUtterances, maxCharacters);
        }

        internal static byte[] EncodeLegacyLine(ConversationTranscriptDiskRecord record)
        {
            using (var buffer = new MemoryStream())
            {
                CreateSerializer().WriteObject(buffer, record);
                if (buffer.Length + 1 > MaximumEventBytes) throw new FormatException("Transcript event exceeds its byte limit.");
                buffer.WriteByte((byte)'\n');
                return buffer.ToArray();
            }
        }

        internal static ConversationTranscriptDiskRecord DecodeLegacyLine(byte[] bytes)
        {
            try
            {
                string json = Utf8.GetString(bytes);
                using (var memory = new MemoryStream(Utf8.GetBytes(json)))
                {
                    var record = CreateSerializer().ReadObject(memory) as ConversationTranscriptDiskRecord;
                    if (record == null) throw new FormatException("Transcript record is invalid.");
                    record.ToEvent();
                    return record;
                }
            }
            catch (FormatException) { throw; }
            catch (Exception) { throw new FormatException("Transcript record is invalid."); }
        }
    }
}
