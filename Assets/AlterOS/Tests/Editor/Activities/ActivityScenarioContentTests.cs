using System;
using System.IO;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class ActivityScenarioContentTests
    {
        [TestCase("movement.follow_player")]
        [TestCase("needs.drink_water")]
        public void MenuActivityStartsARealScenarioAndWaitsForItsOwnOutcome(string capability)
        {
            var library = JsonUtility.FromJson<AuthoredActivityScenarioLibrary>(File.ReadAllText(Path.Combine(
                Directory.GetCurrentDirectory(), "Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json")));
            var entry = library.entries.Single(item => item.capabilityId == capability);
            var context = Context(entry.scenario);
            Assert.IsEmpty(ScenarioDefinitionValidator.Validate(entry.scenario, context));
            var presentation = new ScenarioConversationPresentationModel();
            Assert.AreEqual("request_activity", presentation.Start(entry.scenario, "run", context).Single().type);
            presentation.Advance(new ScenarioEvent { kind = "activity_result", runId = "run",
                stepId = entry.scenario.entryStepId, activityId = "first", success = true }, context);
            Assert.AreEqual("wait", presentation.CurrentStepKind);
            Assert.IsTrue(presentation.Current.scenarioActive);
            Assert.IsEmpty(presentation.Advance(new ScenarioEvent { kind = "condition_met", runId = "wrong",
                activityId = "first", conditionId = ActivityConditionIds.Terminal }, context));
            var effects = presentation.Advance(new ScenarioEvent { kind = "condition_met", runId = "run",
                activityId = "first", conditionId = ActivityConditionIds.Terminal }, context);
            if (capability == ActivityCapabilityIds.DrinkWater)
            {
                var returning = effects.Single(effect => effect.type == "request_activity");
                Assert.AreEqual("movement.come_here", returning.activityRequest.capabilityId);
                presentation.Advance(new ScenarioEvent { kind = "activity_result", runId = "run",
                    stepId = returning.stepId, activityId = "return", success = true }, context);
                effects = presentation.Advance(new ScenarioEvent { kind = "condition_met", runId = "run",
                    activityId = "return", conditionId = ActivityConditionIds.Terminal }, context);
            }
            Assert.IsTrue(effects.Any(effect => effect.type == "show_dialogue"));
            Assert.AreEqual("dialogue", presentation.CurrentStepKind);
            presentation.DismissExchange();
            Assert.IsFalse(presentation.Current.exchangeOpen);
            var reopened = presentation.ResumeForInteraction(context).Single(effect => effect.type == "show_dialogue");
            foreach (var beat in reopened.step.beats)
                Assert.IsTrue(presentation.AcknowledgeDialogueBeat(reopened.stepId, beat.id));
            Assert.IsTrue(presentation.CompleteDialogue("run", reopened.stepId).Any(effect => effect.type == "end_scenario"));
            Assert.IsEmpty(presentation.ResumeForInteraction(context));
        }

        [TestCase(false, true, "defer")]
        [TestCase(false, false, "bark_then_e")]
        [TestCase(true, false, "independent")]
        public void ContentCannotAuthorizeAnIndependentConversation(bool authorized, bool busy, string expected)
        {
            Assert.AreEqual(expected, ScenarioEngagementPolicy.Resolve("independent", authorized, busy));
        }

        [TestCase("drink", "unavailable")]
        [TestCase("return", "return_failed")]
        public void RejectedActionUsesItsTruthfulFailureBranch(string actionId, string expected)
        {
            var library = JsonUtility.FromJson<AuthoredActivityScenarioLibrary>(File.ReadAllText(Path.Combine(
                Directory.GetCurrentDirectory(), "Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json")));
            var entry = library.entries.Single(item => item.capabilityId == ActivityCapabilityIds.DrinkWater);
            var context = Context(entry.scenario);
            var session = new ScenarioSession();
            session.Start(entry.scenario, "run", context);
            if (actionId == "return")
            {
                session.Advance(new ScenarioEvent { kind = "activity_result", runId = "run", stepId = "drink", activityId = "drink-id", success = true }, context);
                session.Advance(new ScenarioEvent { kind = "condition_met", runId = "run", activityId = "drink-id", conditionId = ActivityConditionIds.Terminal }, context);
            }
            var result = session.Advance(new ScenarioEvent { kind = "activity_result", runId = "run", stepId = actionId, success = false }, context);
            Assert.AreEqual(expected, result.Single(effect => effect.type == "show_dialogue").stepId);
        }

        private static ScenarioRuntimeSnapshot Context(ScenarioDefinition scenario)
        {
            return new ScenarioRuntimeSnapshot(scenario.scenarioId, "run", "turn", 0, scenario.entryStepId,
                new string[0], new[] { "gentle", "happy", "thinking" }, new[] {
                    new ScenarioCapabilitySnapshot("movement.follow_player", new[] { new ScenarioArgumentBinding("target", new[] { "player" }) }),
                    new ScenarioCapabilitySnapshot("movement.come_here", new[] { new ScenarioArgumentBinding("target", new[] { "player" }) }),
                    new ScenarioCapabilitySnapshot("needs.drink_water", new[] { new ScenarioArgumentBinding("target", new[] { "available_dispenser" }) })
                }, null, new[] { ActivityConditionIds.Terminal });
        }
    }
}
