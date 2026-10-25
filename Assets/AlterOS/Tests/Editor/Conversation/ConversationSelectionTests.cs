using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Semantics;
using AlterOS.Topics;

namespace AlterOS.Tests
{
    public sealed class ConversationSelectionTests
    {
        [Test]
        public void RetrieverReturnsStableTopEightAndKeepsWeakCandidatesForOnlineJudgment()
        {
            var catalogue = new Dictionary<string, TopicDefinition>();
            var anchors = new List<TopicFamilyAnchorVector>();
            foreach (string id in new[] { "topic.j", "topic.a", "topic.k", "topic.b", "topic.c", "topic.d", "topic.e", "topic.f", "topic.g", "topic.h" })
            {
                catalogue.Add(id, ConversationTestData.Topic(id, ConversationTestData.Scenario("one", 1)));
                anchors.Add(Anchor(id, id + " anchor", 1, 0));
            }
            TopicCandidateSet candidates = TopicCandidateRetriever.Retrieve(new[] { 1f, 0f }, "gte-test-v1", anchors, "gte-test-v1", catalogue, "synthetic");
            Assert.AreEqual(8, candidates.cards.Count);
            CollectionAssert.AreEqual(new[] { "topic.a", "topic.b", "topic.c", "topic.d", "topic.e", "topic.f", "topic.g", "topic.h" },
                candidates.cards.Select(card => card.topicId).ToArray());
            Assert.AreEqual("unchecked", candidates.cards[0].authoredAvailability);
            Assert.Throws<ArgumentException>(() => TopicCandidateRetriever.Retrieve(new[] { 1f, 0f }, "gte-a", anchors, "gte-b", catalogue, "synthetic"));

            TopicCandidateSet weak = TopicCandidateRetriever.Retrieve(new[] { 1f, 0f }, "gte-test-v1", new[] { Anchor("tea", "weak phrase", 0, 1) },
                "gte-test-v1", new Dictionary<string, TopicDefinition> { { "tea", ConversationTestData.Topic("tea", ConversationTestData.Scenario("one", 1)) } }, "synthetic");
            Assert.AreEqual(0f, weak.cards[0].similarity);
            Assert.AreEqual(1, weak.cards.Count);
        }

        [Test]
        public void RetrieverRejectsAnAnchorWhoseTopicIsMissingFromCatalogue()
        {
            Assert.Throws<ArgumentException>(() => TopicCandidateRetriever.Retrieve(new[] { 1f, 0f }, "gte-test-v1",
                new[] { Anchor("missing", "unknown", 1, 0) }, "gte-test-v1", new Dictionary<string, TopicDefinition>(), "synthetic"));
        }

        [Test]
        public void PreparationAnnotatesChannelEligibilityWithoutMutatingRetrievedCards()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea", ConversationTestData.Scenario("direct", 1, "direct"));
            TopicInstance instance = TopicInstance.Create(definition, "tea-instance", "authored");
            var catalogue = new Dictionary<string, TopicDefinition> { { "tea", definition } };
            var instances = new Dictionary<string, TopicInstance> { { "tea", instance } };
            TopicCandidateSet retrieved = TopicCandidateRetriever.Retrieve(new[] { 1f, 0f }, "gte-test-v1",
                new[] { Anchor("tea", "tea", 1, 0) }, "gte-test-v1", catalogue, "synthetic");
            TopicCandidateSet phone = TopicCandidateRetriever.PrepareForJudgment(retrieved, catalogue, instances, new TopicContext(), new TopicHistory(), Now, "phone");
            TopicCandidateSet direct = TopicCandidateRetriever.PrepareForJudgment(retrieved, catalogue, instances, new TopicContext(), new TopicHistory(), Now, "direct");
            Assert.AreEqual("unchecked", retrieved.cards[0].authoredAvailability);
            Assert.AreEqual("blocked", phone.cards[0].authoredAvailability);
            Assert.AreEqual("ready", direct.cards[0].authoredAvailability);
        }

        [Test]
        public void ConversationSelectionPrefersUnseenOnlyWithinHighestPriorityAndKeepsLegacyDraw()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea",
                ConversationTestData.Scenario("seen", 2), ConversationTestData.Scenario("unseen", 2), ConversationTestData.Scenario("lower", 1));
            TopicInstance instance = TopicInstance.Create(definition, "tea-instance", "authored");
            var history = new TopicHistory();
            TopicPlaybackHandle completed = history.BeginPlayback(definition, instance, "seen", Now);
            history.ReportPresentedNode(completed, "n1", Now.AddSeconds(1));
            history.Complete(completed, Now.AddSeconds(2));
            TopicSelectionResult legacyDraw = TopicScenarioSelector.Select(definition, instance, new TopicContext(), history, Now.AddSeconds(3), 0.4);
            Assert.AreEqual("seen", legacyDraw.SelectedScenario.scenarioId);
            TopicSelectionResult conversation = TopicScenarioSelector.SelectForConversation(definition, instance, new TopicContext(), history, Now.AddSeconds(3), 0.1, "phone");
            Assert.AreEqual("unseen", conversation.SelectedScenario.scenarioId);

            TopicDefinition legacy = ConversationTestData.Topic("tea", ConversationTestData.Scenario("direct", 1, "direct"), ConversationTestData.Scenario("either", 1));
            TopicSelectionResult existing = TopicScenarioSelector.Select(legacy, TopicInstance.Create(legacy, "legacy", "authored"), new TopicContext(), new TopicHistory(), Now, 0.9);
            Assert.AreEqual("either", existing.SelectedScenario.scenarioId);
            TopicSelectionResult phone = TopicScenarioSelector.SelectForConversation(legacy, TopicInstance.Create(legacy, "legacy-phone", "authored"), new TopicContext(), new TopicHistory(), Now, 0.9, "phone");
            Assert.AreEqual("either", phone.SelectedScenario.scenarioId);
        }

        [Test]
        public void ConversationSelectionRepeatsHighestTierWhenNoUnseenScenarioRemains()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea",
                ConversationTestData.Scenario("high-a", 3), ConversationTestData.Scenario("high-b", 3), ConversationTestData.Scenario("lower-unseen", 2));
            TopicInstance instance = TopicInstance.Create(definition, "tea-repeat", "authored");
            var history = new TopicHistory();
            int offset = 0;
            foreach (string scenarioId in new[] { "high-a", "high-b" })
            {
                DateTimeOffset start = Now.AddSeconds(offset * 3);
                TopicPlaybackHandle handle = history.BeginPlayback(definition, instance, scenarioId, start);
                history.ReportPresentedNode(handle, "n1", start.AddSeconds(1));
                history.Complete(handle, start.AddSeconds(2));
                offset++;
            }
            TopicSelectionResult selection = TopicScenarioSelector.SelectForConversation(definition, instance,
                new TopicContext(), history, Now.AddSeconds(7), 0.5, "direct");
            Assert.AreEqual(3, selection.WinningPriority.Value);
            Assert.IsTrue(selection.SelectedScenario.scenarioId == "high-a" || selection.SelectedScenario.scenarioId == "high-b");
        }

        private static DateTimeOffset Now { get { return new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero); } }

        private static TopicFamilyAnchorVector Anchor(string topicId, string text, float x, float y)
        { return new TopicFamilyAnchorVector { TopicId = topicId, Anchor = text, Embedding = new[] { x, y } }; }
    }
}
