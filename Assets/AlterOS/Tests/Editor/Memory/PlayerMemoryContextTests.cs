using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Memory;

namespace AlterOS.Tests
{
    public sealed class PlayerMemoryContextTests
    {
        [Test]
        public void TranscriptSyncIncludesOnlyPlayerRequestsAndPreservesSourceAndSessionIds()
        {
            string path = Path.Combine(Path.GetTempPath(), "AlterOS-memory-sync-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            try
            {
                var transcript = new ConversationTranscript(path);
                DateTimeOffset now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
                transcript.Append(ConversationTranscriptEvent.RequestReceived("session-1", "request-1", "request-1:request", "direct", "I like tea", now));
                transcript.Append(ConversationTranscriptEvent.NodePresented("session-1", "response-1", "node-1", "presented-1",
                    "generated", "direct", "", "", "Altera", "We can talk about tea", "We can talk about tea", "", now));
                transcript.Append(ConversationTranscriptEvent.RequestReceived("session-2", "request-2", "request-2:request", "phone", "I like cats", now));

                ConversationTranscriptPage page = transcript.ReadPage("0", 10);
                IReadOnlyList<PlayerMemorySourceSyncEvent> batch = PlayerMemoryTranscriptSync.BuildBatch(page, "player-1", true, 0);

                Assert.AreEqual(2, batch.Count);
                Assert.AreEqual(1, batch[0].sequence);
                Assert.AreEqual(3, batch[1].sequence);
                Assert.AreEqual("request-1:request", batch[0].evidence.sourceId);
                Assert.AreEqual("session-2", batch[1].evidence.sessionId);
                Assert.AreEqual("player", batch[0].evidence.actor);
                Assert.AreEqual("unclassified", batch[0].evidence.scope);
                Assert.AreEqual(0, PlayerMemoryTranscriptSync.BuildBatch(page, "player-1", false, 0).Count);

                PlayerMemoryTranscriptSyncBatch pageBatch = PlayerMemoryTranscriptSync.BuildPage(page, "player-1", true, 0);
                Assert.AreEqual(3, pageBatch.throughSequence);
                Assert.AreEqual(2, pageBatch.events.Count);
            }
            finally
            {
                foreach (string suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
                {
                    string candidate = path + suffix;
                    if (File.Exists(candidate)) File.Delete(candidate);
                }
            }
        }

        [Test]
        public void SuppressionRemovesForgottenRecentSourceAndMissingIdsFailClosedWhenRequired()
        {
            var history = new[]
            {
                new ConversationPresentedUtteranceV2("player", "player", "", "", "forgotten words", "source-forgotten"),
                new ConversationPresentedUtteranceV2("alterra", "generated", "response-1", "node-1", "safe words", "source-safe"),
                new ConversationPresentedUtteranceV2("player", "player", "", "", "unlinked legacy words")
            };
            var suppressed = new HashSet<string> { "source-forgotten" };

            IReadOnlyList<ConversationPresentedUtteranceV2> filtered = MemoryConversationHistoryFilter.Filter(history, suppressed, true);

            Assert.AreEqual(1, filtered.Count);
            Assert.AreEqual("safe words", filtered[0].text);
        }

        [Test]
        public void PreMemorySavesKeepLegacyHistoryButStillHonorKnownSuppression()
        {
            var history = new[]
            {
                new ConversationPresentedUtteranceV2("player", "player", "", "", "legacy words"),
                new ConversationPresentedUtteranceV2("player", "player", "", "", "forgotten words", "source-forgotten")
            };
            IReadOnlyList<ConversationPresentedUtteranceV2> filtered = MemoryConversationHistoryFilter.Filter(
                history, new HashSet<string> { "source-forgotten" }, false);

            Assert.AreEqual(1, filtered.Count);
            Assert.AreEqual("legacy words", filtered[0].text);
        }

        [Test]
        public void MemoryPolicyMarkerPersistsWithoutContainingPlayerData()
        {
            string root = Path.Combine(Path.GetTempPath(), "AlterOS-memory-policy-" + Guid.NewGuid().ToString("N"));
            var first = new PlayerMemoryPolicyFlag(root);
            Assert.IsFalse(first.IsRequired);
            first.MarkRequired();

            var reopened = new PlayerMemoryPolicyFlag(root);
            Assert.IsTrue(reopened.IsRequired);
            string marker = Path.Combine(root, "Memory", "memory-policy-required.flag");
            Assert.AreEqual("required\n", File.ReadAllText(marker));
            Directory.Delete(root, true);
        }
    }
}
