using System;
using System.Collections.Generic;
using AlterOS.Scenarios;
using AlterOS.Topics;
using NUnit.Framework;

namespace AlterOS.Tests.Scenarios
{
    public sealed class AuthoredScenarioAdapterTests
    {
        [Test]
        public void ConvertsAuthoredIdsBilingualBeatsChoicesAndTopicCheckpointWithoutCopyingSelectionRules()
        {
            TopicScenario authored = new TopicScenario
            {
                scenarioId = "cat-chat",
                priority = 8,
                baseWeight = 2,
                consumptionNodeId = "closing",
                modifiers = new List<TopicWeightModifier>(),
                nodes = new List<AlterOS.Dialogue.CanonicalDialogueNode>
                {
                    Node("opening", "猫が好きなの？", "Do you like cats?", "choice", "curious"),
                    new AlterOS.Dialogue.CanonicalDialogueNode
                    {
                        id = "choice", speaker = "alterra", text = "どんな猫が好き？", displayText = "What kind of cat do you like?",
                        language = "japanese", presentation = new AlterOS.Dialogue.DialoguePresentation { expression = "thinking" },
                        choices = new List<AlterOS.Dialogue.CanonicalDialogueChoice>
                        {
                            new AlterOS.Dialogue.CanonicalDialogueChoice { label = "Playful", nextId = "closing" },
                            new AlterOS.Dialogue.CanonicalDialogueChoice { label = "Calm", nextId = "closing" },
                        }
                    },
                    Node("closing", "教えてくれてありがとう。", "Thanks for telling me.", "", "gentle"),
                }
            };
            var topic = new TopicDefinition
            {
                topicId = "pets.cats", anchors = new List<string> { "cats" }, defaultReplayPolicy = TopicReplayPolicy.Repeatable,
                scenarios = new List<TopicScenario> { authored }
            };

            ScenarioDefinition converted = AuthoredScenarioAdapter.Convert(topic, authored);

            Assert.AreEqual("cat-chat", converted.scenarioId);
            Assert.AreEqual("pets.cats", converted.topicId);
            Assert.AreEqual("opening", converted.entryStepId);
            Assert.AreEqual(8, authored.priority, "Priority remains owned by Topic selection.");
            Assert.AreEqual("closing", authored.consumptionNodeId, "TopicHistory retains the authored consumption checkpoint.");
            Assert.AreEqual(5, converted.steps.Count);
            Assert.AreEqual("opening", converted.steps[0].id);
            Assert.AreEqual("猫が好きなの？", converted.steps[0].beats[0].text);
            Assert.AreEqual("Do you like cats?", converted.steps[0].beats[0].displayText);
            Assert.AreEqual("curious", converted.steps[0].beats[0].expression);
            Assert.AreEqual("ask", converted.steps[1].kind);
            Assert.AreEqual("choice", converted.steps[1].questionId);
            Assert.AreEqual(2, converted.steps[1].options.Count);
            Assert.AreEqual("closing", converted.steps[1].options[0].nextStepId);
            Assert.AreEqual("scenario-end", converted.steps[3].id);
            Assert.AreEqual("end", converted.steps[3].kind);
            Assert.AreEqual("completed", converted.steps[3].outcome);
            var runtime = new ScenarioRuntimeSnapshot("cat-chat", "adapter-test", "adapter-turn", 1, "",
                new[] { "pets.cats" }, new[] { "curious", "thinking", "gentle" }, null, null, null);
            Assert.IsEmpty(ScenarioDefinitionValidator.Validate(converted, runtime));
            Assert.IsNotNull(converted.steps.Find(step => step.id == authored.consumptionNodeId),
                "The preserved node ID lets the existing TopicHistory checkpoint remain authoritative.");

            var history = new TopicHistory();
            TopicInstance occurrence = TopicInstance.Create(topic, "generated-cat", "generated");
            DateTimeOffset start = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            TopicPlaybackHandle handle = history.BeginPlayback(topic, occurrence, authored.scenarioId, start);
            Assert.AreEqual("Applied", history.ReportPresentedNode(handle, "opening", start.AddSeconds(1)).Status);
            Assert.AreEqual("Applied", history.ReportPresentedNode(handle, "choice", start.AddSeconds(2)).Status);
            TopicPlaybackResult checkpoint = history.ReportPresentedNode(handle, "closing", start.AddSeconds(3));
            Assert.IsTrue(checkpoint.IsConsumed, "The existing whole-Topic replay rule consumes at its authored checkpoint.");
            Assert.AreEqual("Applied", history.Complete(handle, start.AddSeconds(4)).Status);
        }

        [Test]
        public void RejectsUnsupportedAuthoredLanguageInsteadOfRelabelingItAsJapaneseSpeech()
        {
            TopicDefinition topic = CreateSingleNodeTopic("english-only", "en");
            Assert.Throws<System.ArgumentException>(() => AuthoredScenarioAdapter.Convert(topic, topic.scenarios[0]));
        }

        static TopicDefinition CreateSingleNodeTopic(string id, string language)
        {
            var scenario = new TopicScenario
            {
                scenarioId = "single", priority = 1, baseWeight = 1,
                modifiers = new List<TopicWeightModifier>(), nodes = new List<AlterOS.Dialogue.CanonicalDialogueNode>
                {
                    new AlterOS.Dialogue.CanonicalDialogueNode
                    {
                        id = "line", speaker = "alterra", text = "Hello.", displayText = "Hello.", language = language,
                        presentation = new AlterOS.Dialogue.DialoguePresentation(), choices = new List<AlterOS.Dialogue.CanonicalDialogueChoice>()
                    }
                }
            };
            return new TopicDefinition { topicId = id, anchors = new List<string> { "hello" }, scenarios = new List<TopicScenario> { scenario } };
        }

        static AlterOS.Dialogue.CanonicalDialogueNode Node(string id, string japanese, string english, string next, string expression)
        {
            return new AlterOS.Dialogue.CanonicalDialogueNode
            {
                id = id, speaker = "alterra", text = japanese, displayText = english, language = "japanese", nextId = next,
                presentation = new AlterOS.Dialogue.DialoguePresentation { expression = expression },
                choices = new List<AlterOS.Dialogue.CanonicalDialogueChoice>()
            };
        }
    }
}
