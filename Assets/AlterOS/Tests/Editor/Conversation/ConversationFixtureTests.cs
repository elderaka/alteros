using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using AlterOS.Topics;

namespace AlterOS.Tests
{
    [DataContract]
    internal sealed class ConversationFixtureDocument
    {
        [DataMember] public string requestJson;
        [DataMember] public string judgmentJson;
        [DataMember] public string expectedOutcome;
        [DataMember] public string expectedScenarioId;
        [DataMember] public bool expectedNovel;
        [DataMember] public string[] completedScenarioIds;
        [DataMember] public FixtureTopic[] topics;
        [DataMember] public FixtureCandidate[] candidates;
    }

    [DataContract]
    internal sealed class FixtureTopic
    {
        [DataMember] public string topicId;
        [DataMember] public FixtureScenario[] scenarios;
    }

    [DataContract]
    internal sealed class FixtureScenario
    {
        [DataMember] public string scenarioId;
        [DataMember] public int priority;
        [DataMember] public string channel;
    }

    [DataContract]
    internal sealed class FixtureCandidate
    {
        [DataMember] public string topicId;
        [DataMember] public float similarity;
        [DataMember] public int supportCount;
        [DataMember] public string availability;
    }

    public sealed class ConversationFixtureTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        private static readonly string[] Fixtures = { "authored-unseen.json", "phone-blocked.json", "continuation.json", "novel-provisional.json" };

        [Test]
        public void NamedFixturesParseAndExecuteTheProductionPlanner()
        {
            int executed = 0;
            foreach (string file in Fixtures)
            {
                string path = Path.Combine("Tools", "Conversation", "Fixtures", file);
                Assert.IsTrue(File.Exists(path), "Missing conversation fixture: " + path);
                ConversationFixtureDocument fixture;
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                    fixture = (ConversationFixtureDocument)new DataContractJsonSerializer(typeof(ConversationFixtureDocument)).ReadObject(stream);
                Assert.IsNotNull(fixture, "Fixture JSON did not parse: " + file);
                ConversationRequest request = ConversationContractValidator.ParseRequest(fixture.requestJson);
                ConversationJudgment judgment = string.IsNullOrEmpty(fixture.judgmentJson)
                    ? null : ConversationContractValidator.ParseJudgment(fixture.judgmentJson);
                var catalogue = new Dictionary<string, TopicDefinition>();
                var instances = new Dictionary<string, TopicInstance>();
                var candidateSet = new TopicCandidateSet { indexIdentity = "synthetic-fixture-v1", source = "synthetic" };
                var history = new TopicHistory();
                if (fixture.topics != null)
                    foreach (FixtureTopic source in fixture.topics)
                    {
                        var scenarios = new List<TopicScenario>();
                        foreach (FixtureScenario sourceScenario in source.scenarios)
                        {
                            TopicScenario scenario = ConversationTestData.Scenario(sourceScenario.scenarioId, sourceScenario.priority,
                                string.IsNullOrEmpty(sourceScenario.channel) ? null : sourceScenario.channel);
                            scenarios.Add(scenario);
                        }
                        TopicDefinition definition = ConversationTestData.Topic(source.topicId, scenarios.ToArray());
                        TopicInstance instance = TopicInstance.Create(definition, source.topicId + "-instance", "authored");
                        catalogue.Add(source.topicId, definition);
                        instances.Add(source.topicId, instance);
                        foreach (string completedId in fixture.completedScenarioIds ?? new string[0])
                            if (definition.scenarios.Exists(scenario => scenario.scenarioId == completedId))
                            {
                                TopicPlaybackHandle handle = history.BeginPlayback(definition, instance, completedId, Now);
                                history.ReportPresentedNode(handle, definition.scenarios.Find(scenario => scenario.scenarioId == completedId).nodes[0].id, Now.AddSeconds(1));
                                history.Complete(handle, Now.AddSeconds(2));
                            }
                    }
                if (fixture.candidates != null)
                    foreach (FixtureCandidate candidate in fixture.candidates)
                        candidateSet.cards.Add(new TopicCandidateCard
                        {
                            topicId = candidate.topicId, similarity = candidate.similarity, supportCount = candidate.supportCount,
                            authoredAvailability = candidate.availability, examples = new List<string>(),
                        });

                ConversationPlan plan = UnifiedConversationPlanner.Plan(request, candidateSet, judgment, catalogue, instances,
                    new TopicContext(), history, Now.AddSeconds(3), 0, new HashSet<string> { "joy", "thinking" });
                Assert.AreEqual(fixture.expectedOutcome, plan.outcome, file);
                Assert.AreEqual(fixture.expectedScenarioId ?? "", plan.scenarioId, file);
                Assert.AreEqual(fixture.expectedNovel, plan.isNovelCandidate, file);
                executed++;
            }
            Assert.AreEqual(Fixtures.Length, executed, "Every named fixture must execute the real planner.");
        }
    }
}
