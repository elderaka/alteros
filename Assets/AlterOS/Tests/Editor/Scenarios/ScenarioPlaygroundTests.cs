using System.Collections.Generic;
using System.Linq;
using AlterOS.Scenarios;
using NUnit.Framework;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioPlaygroundTests
    {
        [Test]
        public void AuthoredAskAndDialogueRunWithoutProviderRequests()
        {
            AlterOS.Topics.TopicDefinition topic = CatTopic();
            ScenarioDefinition definition = AuthoredScenarioAdapter.Convert(topic, topic.scenarios[0]);
            ScenarioPlaygroundSession playground = new ScenarioPlaygroundSession(definition, "playground-cat", Context("playground-cat"));
            Assert.AreEqual("show_dialogue", playground.InitialReport.effects[0].type);

            ScenarioPlaygroundReport ask = playground.Apply(new ScenarioEvent
            {
                kind = "dialogue_complete", runId = "playground-cat", stepId = "opening"
            }, 1d);
            Assert.AreEqual("ask_player", ask.effects[0].type);

            ScenarioPlaygroundReport closing = playground.Apply(new ScenarioEvent
            {
                kind = "answer", runId = "playground-cat", stepId = "question", questionId = "question", answerId = "question-option-0",
                contextRevision = 1
            }, 2d);
            Assert.AreEqual("show_dialogue", closing.effects[0].type);
            Assert.AreEqual("closing", closing.effects[0].stepId);
            Assert.AreEqual(0, closing.paidStageRequestCount);
            Assert.IsFalse(closing.hasSimulatedActivity);
        }

        [Test]
        public void ActionIsExplicitlySimulatedAndTimeMustBeMonotonic()
        {
            ScenarioDefinition definition = new ScenarioDefinition
            {
                scenarioId = "simulated-drawing", topicId = "drawing", entryStepId = "action",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "action", kind = "action", capabilityId = "fixture.drawing",
                        arguments = new List<ScenarioActionArgument> { new ScenarioActionArgument { name = "targetObjectId", value = "table" } },
                        successNextStepId = "wait", failureNextStepId = "end" },
                    new ScenarioStep { id = "wait", kind = "wait", activityId = "simulated-drawing", conditionId = "player_joined",
                        objective = "Player joins the activity.", successNextStepId = "end", failureNextStepId = "end" },
                    new ScenarioStep { id = "end", kind = "end", outcome = "completed" },
                }
            };
            ScenarioPlaygroundSession playground = new ScenarioPlaygroundSession(definition, "playground-drawing", Context("playground-drawing", true));

            Assert.IsTrue(playground.InitialReport.hasSimulatedActivity);
            Assert.IsTrue(playground.InitialReport.effects.Any(effect => effect.type == "simulated_activity_result"));
            Assert.IsTrue(playground.InitialReport.effects.Any(effect => effect.type == "wait_for_condition"));
            Assert.AreEqual(0, playground.InitialReport.paidStageRequestCount);
            Assert.Throws<System.InvalidOperationException>(() => playground.Apply(new ScenarioEvent
            {
                kind = "condition_met", runId = "playground-drawing", stepId = "wait", activityId = "simulated-playground-drawing",
                conditionId = "player_joined"
            }, -1d));
        }

        [Test]
        public void ReplayingSameSyntheticTimelineProducesIdenticalWindowAndCliModelReports()
        {
            AlterOS.Topics.TopicDefinition topic = CatTopic();
            ScenarioDefinition definition = AuthoredScenarioAdapter.Convert(topic, topic.scenarios[0]);
            ScenarioRuntimeSnapshot firstContext = ScenarioPlaygroundSession.CreateSyntheticContext(definition, "parity-run", "parity-turn");
            ScenarioRuntimeSnapshot secondContext = ScenarioPlaygroundSession.CreateSyntheticContext(definition, "parity-run", "parity-turn");
            var windowModel = new ScenarioPlaygroundSession(definition, "parity-run", firstContext);
            var batchModel = new ScenarioPlaygroundSession(definition, "parity-run", secondContext);
            ScenarioEvent input = new ScenarioEvent { kind = "dialogue_complete", runId = "parity-run", stepId = "opening" };

            ScenarioPlaygroundReport windowReport = windowModel.Apply(input, 1d);
            ScenarioPlaygroundReport batchReport = batchModel.Apply(new ScenarioEvent
            {
                kind = input.kind, runId = input.runId, stepId = input.stepId
            }, 1d);

            Assert.AreEqual(windowReport.status, batchReport.status);
            Assert.AreEqual(windowReport.currentStepId, batchReport.currentStepId);
            Assert.AreEqual(windowReport.effects.Count, batchReport.effects.Count);
            for (int index = 0; index < windowReport.effects.Count; index++)
            {
                Assert.AreEqual(windowReport.effects[index].type, batchReport.effects[index].type);
                Assert.AreEqual(windowReport.effects[index].outcome, batchReport.effects[index].outcome);
            }
        }

        static AlterOS.Topics.TopicDefinition CatTopic()
        {
            return new AlterOS.Topics.TopicDefinition
            {
                topicId = "cats", anchors = new List<string> { "cats" },
                scenarios = new List<AlterOS.Topics.TopicScenario>
                {
                    new AlterOS.Topics.TopicScenario
                    {
                        scenarioId = "cat-chat", priority = 1, baseWeight = 1,
                        modifiers = new List<AlterOS.Topics.TopicWeightModifier>(),
                        nodes = new List<AlterOS.Dialogue.CanonicalDialogueNode>
                        {
                            new AlterOS.Dialogue.CanonicalDialogueNode { id = "opening", speaker = "alterra", text = "猫が好き？", displayText = "Do you like cats?", language = "japanese", nextId = "question", presentation = new AlterOS.Dialogue.DialoguePresentation { expression = "gentle" } },
                            new AlterOS.Dialogue.CanonicalDialogueNode { id = "question", speaker = "alterra", text = "どんな猫？", displayText = "What kind of cat?", language = "japanese", presentation = new AlterOS.Dialogue.DialoguePresentation { expression = "curious" },
                                choices = new List<AlterOS.Dialogue.CanonicalDialogueChoice> { new AlterOS.Dialogue.CanonicalDialogueChoice { label = "Playful", nextId = "closing" } } },
                            new AlterOS.Dialogue.CanonicalDialogueNode { id = "closing", speaker = "alterra", text = "ありがとう。", displayText = "Thanks!", language = "japanese", presentation = new AlterOS.Dialogue.DialoguePresentation { expression = "gentle" } },
                        }
                    }
                }
            };
        }

        static ScenarioRuntimeSnapshot Context(string run, bool withCapability = false)
        {
            var capabilities = withCapability
                ? new[] { new ScenarioCapabilitySnapshot("fixture.drawing", new[] { new ScenarioArgumentBinding("targetObjectId", new[] { "table" }) }) }
                : new ScenarioCapabilitySnapshot[0];
            var objects = withCapability ? new[] { new ScenarioObjectSnapshot("table", "use_point") } : new ScenarioObjectSnapshot[0];
            return new ScenarioRuntimeSnapshot("", run, "turn-1", 1, "", new[] { "cats", "drawing" },
                new[] { "gentle", "curious" }, capabilities, objects, new[] { "player_joined" });
        }

    }
}
