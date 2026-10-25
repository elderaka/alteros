using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AlterOS.Conversation;

namespace AlterOS.Memory
{
    public sealed class PlayerMemoryTranscriptSyncBatch
    {
        public readonly long throughSequence;
        public readonly IReadOnlyList<PlayerMemorySourceSyncEvent> events;
        internal PlayerMemoryTranscriptSyncBatch(long through, IReadOnlyList<PlayerMemorySourceSyncEvent> items)
        { throughSequence = through; events = items; }
    }

    /// <summary>Maps only player-authored transcript requests into bounded, consented source batches.</summary>
    public static class PlayerMemoryTranscriptSync
    {
        public static IReadOnlyList<PlayerMemorySourceSyncEvent> BuildBatch(ConversationTranscriptPage page,
            string playerId, bool consentEnabled, long afterSequence, int maximumEvents = 32)
        { return BuildPage(page, playerId, consentEnabled, afterSequence, maximumEvents).events; }

        public static PlayerMemoryTranscriptSyncBatch BuildPage(ConversationTranscriptPage page,
            string playerId, bool consentEnabled, long afterSequence, int maximumEvents = 32)
        {
            if (page == null || !page.Success) throw new InvalidOperationException("transcript_unavailable");
            if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128 || afterSequence < 0 || maximumEvents < 1 || maximumEvents > 32)
                throw new ArgumentException("Transcript sync bounds are invalid.");
            var events = new List<PlayerMemorySourceSyncEvent>();
            long throughSequence = afterSequence;
            int totalBytes = 0;
            foreach (ConversationTranscriptRecord record in page.events)
            {
                if (record.sequence <= afterSequence) continue;
                ConversationTranscriptEvent value = record.value;
                if (consentEnabled && value.eventType == "request_received" && value.source == "player")
                {
                    if (events.Count >= maximumEvents) break;
                    DateTimeOffset timestamp;
                    if (!DateTimeOffset.TryParse(value.timestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                        throw new InvalidOperationException("transcript_unavailable");
                    string text = value.text.Length <= 512 ? value.text : value.text.Substring(0, 512);
                    int bytes = Encoding.UTF8.GetByteCount(text);
                    if (totalBytes + bytes > PlayerMemoryProtocol.MaximumSourceSyncBytes) break;
                    totalBytes += bytes;
                    events.Add(new PlayerMemorySourceSyncEvent(record.sequence, new PlayerMemoryEvidence
                    {
                        playerId = playerId, sessionId = value.sessionId, sourceId = value.eventId, actor = "player",
                        scope = "unclassified", sourceKind = "player_message", text = text,
                        eventAtUtc = timestamp.ToUniversalTime(), explicitlyConfirmed = false
                    }));
                }
                throughSequence = record.sequence;
            }
            return new PlayerMemoryTranscriptSyncBatch(throughSequence, events.AsReadOnly());
        }
    }
}
