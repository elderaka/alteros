using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Topics;

namespace AlterOS.Tests
{
    public sealed class ConversationCoverageTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void PilotPolicyParsesAndResolvesAllAuthoredExperienceEntries()
        {
            string path = Path.Combine("Assets", "StreamingAssets", "Dialogue", "ConversationCoverage.json");
            Assert.IsTrue(File.Exists(path), "Missing authored coverage policy: " + path);
            ConversationCoveragePolicy policy = ConversationCoverage.ParsePolicy(File.ReadAllText(path));
            var catalogue = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal);
            foreach (string topicId in new[] { "player_tired", "ask_preference", "ask_task_help_short" })
            {
                string topicPath = Path.Combine("Tools", "TopicRuntime", "StagedCatalogue", "topics", topicId + ".json");
                Assert.IsTrue(File.Exists(topicPath), "Missing staged Topic: " + topicPath);
                TopicDefinition definition;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(topicPath))))
                    definition = (TopicDefinition)new DataContractJsonSerializer(typeof(TopicDefinition)).ReadObject(stream);
                Assert.IsNotNull(definition, "Staged Topic did not parse: " + topicId);
                catalogue.Add(topicId, definition);
            }

            Assert.IsEmpty(ConversationCoverage.ValidatePolicy(policy, catalogue));
            Assert.AreEqual(3, policy.topics.Count);
            Assert.IsTrue(ConversationCoverage.AllowsUncoveredDiscussion(policy, "player_tired"));
            Assert.IsTrue(ConversationCoverage.AllowsUncoveredDiscussion(policy, "ask_preference"));
            Assert.IsTrue(ConversationCoverage.AllowsUncoveredDiscussion(policy, "ask_task_help_short"));
            Assert.IsFalse(ConversationCoverage.AllowsUncoveredDiscussion(policy, "player_sad_short"));
        }

        [Test]
        public void MissingAndInvalidPolicyCannotAuthorizeUncoveredDiscussion()
        {
            Assert.IsFalse(ConversationCoverage.AllowsUncoveredDiscussion(null, "player_tired"));
            var policy = new ConversationCoveragePolicy
            {
                topics = new List<ConversationTopicCoverage>
                {
                    new ConversationTopicCoverage { topicId = "unknown", allowUncoveredDiscussion = true,
                        scenarios = new List<ConversationScenarioCoverage> { Experience("unknown_001", "unknown", "Unknown authored coverage.") } }
                }
            };
            Assert.IsNotEmpty(ConversationCoverage.ValidatePolicy(policy,
                new Dictionary<string, TopicDefinition>(StringComparer.Ordinal)));
        }

        [Test]
        public void SnapshotKeepsConsumedAuthoredExperienceBlockedWithoutBlockingSubjectDiscussion()
        {
            TopicDefinition tired = ConversationTestData.Topic("player_tired", ConversationTestData.Scenario("tired_001", 1));
            tired.defaultReplayPolicy = TopicReplayPolicy.Once;
            TopicDefinition other = ConversationTestData.Topic("other", ConversationTestData.Scenario("other_001", 1));
            var catalogue = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal)
            {
                { tired.topicId, tired }, { other.topicId, other }
            };
            var instances = new Dictionary<string, TopicInstance>(StringComparer.Ordinal)
            {
                { tired.topicId, TopicInstance.Create(tired, "tired-instance", "authored") },
                { other.topicId, TopicInstance.Create(other, "other-instance", "authored") }
            };
            var policy = new ConversationCoveragePolicy
            {
                topics = new List<ConversationTopicCoverage>
                {
                    new ConversationTopicCoverage { topicId = "player_tired", allowUncoveredDiscussion = true,
                        scenarios = new List<ConversationScenarioCoverage> { Experience("tired_001", "tired_acknowledgement", "Acknowledges tiredness and suggests rest or a small step.") } }
                }
            };
            var history = new TopicHistory();
            TopicPlaybackHandle once = history.BeginPlayback(tired, instances[tired.topicId], "tired_001", Now);
            history.ReportPresentedNode(once, "n1", Now.AddSeconds(1));
            history.Complete(once, Now.AddSeconds(2));
            bool dirtyBeforeSnapshot = history.IsDirty;
            int completionsBeforeSnapshot = history.GetCompletionCount("tired-instance", "tired_001");

            ConversationCoverageSnapshot snapshot = ConversationCoverage.BuildSnapshot(
                new[] { "player_tired", "other" }, policy, catalogue, instances, new TopicContext(), history, Now.AddSeconds(3), "direct");

            Assert.AreEqual(2, snapshot.topics.Count);
            Assert.IsTrue(snapshot.topics[0].allowUncoveredDiscussion);
            Assert.AreEqual("blocked", snapshot.topics[0].authoredAvailability);
            Assert.AreEqual("tired_acknowledgement", snapshot.topics[0].scenarios[0].experienceKey);
            Assert.IsFalse(snapshot.topics[1].allowUncoveredDiscussion);
            Assert.AreEqual(0, snapshot.topics[1].scenarios.Count);
            Assert.AreEqual(dirtyBeforeSnapshot, history.IsDirty);
            Assert.AreEqual(completionsBeforeSnapshot, history.GetCompletionCount("tired-instance", "tired_001"));
        }

        [Test]
        public void PolicyRequiresEveryAllowedPilotScenarioToHaveUniqueBoundedExperienceMetadata()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea",
                ConversationTestData.Scenario("tea_001", 1), ConversationTestData.Scenario("tea_002", 1));
            var catalogue = new Dictionary<string, TopicDefinition> { { definition.topicId, definition } };
            var policy = new ConversationCoveragePolicy
            {
                topics = new List<ConversationTopicCoverage>
                {
                    new ConversationTopicCoverage { topicId = "tea", allowUncoveredDiscussion = true,
                        scenarios = new List<ConversationScenarioCoverage> { Experience("tea_001", "tea_taste", "Tea preference.") } }
                }
            };

            Assert.IsNotEmpty(ConversationCoverage.ValidatePolicy(policy, catalogue));
            policy.topics[0].scenarios.Add(Experience("tea_002", "tea_taste", "Duplicate experience key."));
            Assert.IsNotEmpty(ConversationCoverage.ValidatePolicy(policy, catalogue));
            policy.topics[0].scenarios[1].experienceKey = "tea_preparation";
            policy.topics[0].scenarios[1].coverageSummary = new string('x', 401);
            Assert.IsNotEmpty(ConversationCoverage.ValidatePolicy(policy, catalogue));
        }

        [Test]
        public void MalformedCatalogueDefinitionReturnsCoverageErrorsWithoutThrowing()
        {
            var definition = new TopicDefinition { topicId = "broken", scenarios = null };
            var policy = new ConversationCoveragePolicy
            {
                topics = new List<ConversationTopicCoverage>
                {
                    new ConversationTopicCoverage { topicId = "broken", allowUncoveredDiscussion = true,
                        scenarios = new List<ConversationScenarioCoverage>() }
                }
            };
            var catalogue = new Dictionary<string, TopicDefinition> { { "broken", definition } };

            List<string> errors = null;
            Assert.DoesNotThrow(() => errors = ConversationCoverage.ValidatePolicy(policy, catalogue));
            Assert.IsNotEmpty(errors);
        }

        private static ConversationScenarioCoverage Experience(string scenarioId, string key, string summary)
        {
            return new ConversationScenarioCoverage
            { scenarioId = scenarioId, experienceKey = key, coverageSummary = summary };
        }
    }
}
