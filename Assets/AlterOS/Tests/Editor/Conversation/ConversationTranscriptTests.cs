using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using AlterOS.Conversation;

namespace AlterOS.Tests
{
    public sealed class ConversationTranscriptTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "AlterOS-Transcript-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string NewPath(string root) { return Path.Combine(root, "conversations.sqlite3"); }

        [Test]
        public void TranscriptKeepsMoreThanOneHundredTwentyTurnsAndPagesWithoutLifetimeLoad()
        {
            string root = NewRoot();
            var transcript = new ConversationTranscript(NewPath(root));
            for (int index = 0; index < 125; index++)
            {
                ConversationAppendResult append = transcript.Append(ConversationTranscriptEvent.RequestReceived(
                    "one-companion", "request-" + index, "event-" + index, "direct", "turn " + index, Now.AddMinutes(index)));
                Assert.AreEqual(ConversationAppendStatus.Appended, append.Status);
            }

            ConversationTranscriptPage page = transcript.ReadPage("120", 10);
            Assert.IsTrue(page.Success, page.Error);
            Assert.AreEqual(5, page.events.Count);
            Assert.AreEqual("request-120", page.events[0].value.requestId);
            Assert.AreEqual("request-124", page.events[4].value.requestId);
            Assert.AreEqual("125", page.nextCursor);
            Assert.AreEqual(12, transcript.ReadRecent(12, 6000).utterances.Count);
        }

        [Test]
        public void DuplicateEventIdsAndNodeAcknowledgementsAppendOnlyOnce()
        {
            string root = NewRoot();
            var transcript = new ConversationTranscript(NewPath(root));
            ConversationTranscriptEvent presented = ConversationTranscriptEvent.NodePresented(
                "one-companion", "response-1", "node-1", "event-node-1", "generated", "direct", "", "", "Altera",
                "Hello", "こんにちは", "happy", Now);

            Assert.AreEqual(ConversationAppendStatus.Appended, transcript.Append(presented).Status);
            Assert.AreEqual(ConversationAppendStatus.Duplicate, transcript.Append(presented).Status);
            ConversationTranscriptEvent retry = ConversationTranscriptEvent.NodePresented(
                "one-companion", "response-1", "node-1", "event-node-1", "generated", "direct", "", "", "Altera",
                "Hello", "こんにちは", "happy", Now.AddSeconds(1));
            Assert.AreEqual(ConversationAppendStatus.Duplicate, transcript.Append(retry).Status,
                "An idempotent replay may have a new attempt timestamp but must not conflict with the saved payload.");
            ConversationTranscriptEvent conflicting = ConversationTranscriptEvent.NodePresented(
                "one-companion", "response-1", "node-2", "event-node-1", "generated", "direct", "", "", "Altera",
                "Different", "違う", "happy", Now);
            Assert.AreEqual("event_id_conflict", transcript.Append(conflicting).Error);
            Assert.AreEqual(1, transcript.ReadPage("0", 10).events.Count);
            Assert.AreEqual(1, transcript.ReadRecent(12, 6000).utterances.Count);
        }

        [Test]
        public void PartialFinalLineRequiresRecoveryAndKeepsLegacySourceBytes()
        {
            string root = NewRoot();
            string path = Path.Combine(root, "legacy.jsonl");
            string dbPath = NewPath(root);
            string valid = "{\"schemaVersion\":1,\"sequence\":1,\"eventId\":\"event-1\",\"eventType\":\"request_received\",\"sessionId\":\"one-companion\",\"requestId\":\"request-1\",\"responseId\":\"\",\"nodeId\":\"\",\"source\":\"player\",\"channel\":\"direct\",\"topicId\":\"\",\"scenarioId\":\"\",\"judgmentKind\":\"\",\"speaker\":\"Player\",\"text\":\"hello\",\"displayText\":\"hello\",\"expression\":\"\",\"responseJson\":\"\",\"statusCode\":\"\",\"timestampUtc\":\"2026-10-16T12:00:00.0000000+00:00\"}\n";
            byte[] original = Encoding.UTF8.GetBytes(valid + "{\"schemaVersion\":1");
            File.WriteAllBytes(path, original);
            var store = new SqliteTranscriptStore(dbPath);

            ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(path, store);

            Assert.AreEqual("recovery_required", imported.State);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(0, store.ReadPage("0", 10).events.Count);
        }

        [Test]
        public void CorruptMiddleRecordCannotImportAValidPrefixOrRewriteOriginalBytes()
        {
            string root = NewRoot();
            string path = Path.Combine(root, "legacy-corrupt.jsonl");
            string dbPath = Path.Combine(root, "corrupt.sqlite3");
            string valid = "{\"schemaVersion\":1,\"sequence\":1,\"eventId\":\"event-1\",\"eventType\":\"request_received\",\"sessionId\":\"one-companion\",\"requestId\":\"request-1\",\"responseId\":\"\",\"nodeId\":\"\",\"source\":\"player\",\"channel\":\"direct\",\"topicId\":\"\",\"scenarioId\":\"\",\"judgmentKind\":\"\",\"speaker\":\"Player\",\"text\":\"first\",\"displayText\":\"first\",\"expression\":\"\",\"responseJson\":\"\",\"statusCode\":\"\",\"timestampUtc\":\"2026-10-16T12:00:00.0000000+00:00\"}\n";
            byte[] original = Encoding.UTF8.GetBytes(valid + "{not-json}\n" + valid.Replace("event-1", "event-2").Replace("request-1", "request-2"));
            File.WriteAllBytes(path, original);
            var store = new SqliteTranscriptStore(dbPath);

            ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(path, store);

            Assert.AreEqual("recovery_required", imported.State);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(0, store.ReadPage("0", 10).events.Count);
        }

        [Test]
        public void RestartKeepsPendingReceiptAndUnpresentedReplyOutOfRecentUtterances()
        {
            string root = NewRoot();
            string path = NewPath(root);
            var transcript = new ConversationTranscript(path);
            transcript.Append(ConversationTranscriptEvent.RequestReceived("one-companion", "request-1", "event-request", "direct", "talk about cats", Now));
            transcript.Append(ConversationTranscriptEvent.ResponseReceived("one-companion", "request-1", "response-1", "event-response", "novel_candidate",
                "{\"kind\":\"novel_candidate\",\"topicId\":\"\",\"referencedTopicIds\":[],\"reply\":{\"beats\":[{\"id\":\"b1\",\"text\":\"こんにちは\",\"displayText\":\"Hello\",\"language\":\"japanese\",\"expression\":\"happy\"}]},\"suggestedReplies\":[]}", Now.AddSeconds(1)));

            var restarted = new ConversationTranscript(path);
            ConversationTranscriptPage events = restarted.ReadPage("0", 10);
            ConversationHistorySnapshot recent = restarted.ReadRecent(12, 6000);
            Assert.IsTrue(events.Success, events.Error);
            Assert.AreEqual(2, events.events.Count);
            Assert.AreEqual("response_received", events.events[1].value.eventType);
            Assert.AreEqual(1, recent.utterances.Count);
            Assert.AreEqual("talk about cats", recent.utterances[0].text);

            restarted.Append(ConversationTranscriptEvent.NodePresented("one-companion", "response-1", "n1", "event-presented",
                "generated", "direct", "", "", "Altera", "こんにちは", "Hello", "happy", Now.AddSeconds(2)));
            Assert.AreEqual(2, restarted.ReadRecent(12, 6000).utterances.Count);
        }

        [Test]
        public void RecentPlayerHistoryUsesProviderSpeakerContractAndBuildsAJob()
        {
            string root = NewRoot();
            var transcript = new ConversationTranscript(NewPath(root));
            Assert.AreEqual(ConversationAppendStatus.Appended, transcript.Append(
                ConversationTranscriptEvent.RequestReceived("one-companion", "request-1", "event-1", "direct", "hello", Now)).Status);

            ConversationHistorySnapshot recent = transcript.ReadRecent(12, 6000);
            Assert.IsTrue(recent.Success, recent.Error);
            Assert.AreEqual("player", recent.utterances[0].speaker);
            var presented = new System.Collections.Generic.List<ConversationPresentedUtteranceV2>();
            foreach (ConversationHistoryUtterance item in recent.utterances)
                presented.Add(new ConversationPresentedUtteranceV2(item.speaker, item.source,
                    item.responseId, item.nodeId, item.text, item.sourceId));

            ConversationJobV2 job = ConversationProtocol.BuildJob(
                new ConversationRequest { schemaVersion = 1, requestId = "request-1", sessionId = "one-companion", channel = "direct", text = "hello" },
                new TopicCandidateSet { indexIdentity = "gte-test", source = "local_gte" },
                new ConversationCoverageSnapshot(), presented, "Warm and sincere.", new[] { "neutral" });

            Assert.AreEqual("player", job.presentedHistory[0].speaker);
        }

        [Test]
        public void FailedStorageReportsUnsavedStateAndDoesNotExposePathOrException()
        {
            string root = NewRoot();
            var transcript = new ConversationTranscript(root);
            ConversationAppendResult append = transcript.Append(ConversationTranscriptEvent.RequestReceived(
                "one-companion", "request-1", "event-1", "direct", "hello", Now));
            Assert.AreEqual(ConversationAppendStatus.Unsaved, append.Status);
            Assert.AreEqual("storage_unavailable", append.Error);
            StringAssert.DoesNotContain(root, append.Error);
        }

        [Test]
        public void TranscriptEventsHaveNoCredentialOrGameEffectFields()
        {
            string root = NewRoot();
            string path = NewPath(root);
            var transcript = new ConversationTranscript(path);
            transcript.Append(ConversationTranscriptEvent.ResponseReceived("one-companion", "request-1", "response-1", "event-response", "novel_candidate",
                "{\"kind\":\"novel_candidate\",\"topicId\":\"\",\"referencedTopicIds\":[],\"reply\":{\"beats\":[{\"id\":\"b1\",\"text\":\"こんにちは\",\"displayText\":\"Hello\",\"language\":\"japanese\",\"expression\":\"happy\"}]},\"suggestedReplies\":[]}", Now));

            ConversationTranscriptPage page = transcript.ReadPage("0", 10);
            Assert.IsTrue(page.Success, page.Error);
            Assert.AreEqual(1, page.events.Count);
            StringAssert.DoesNotContain("apiKey", page.events[0].value.responseJson);
            StringAssert.DoesNotContain("\"effect\"", page.events[0].value.responseJson);
            StringAssert.DoesNotContain("toolCall", page.events[0].value.responseJson);
        }
    }
}
