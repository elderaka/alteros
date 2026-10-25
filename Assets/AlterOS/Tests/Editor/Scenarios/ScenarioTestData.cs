using System.IO;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    internal sealed class FakeScenarioActivityPort : IScenarioActivityPort
    {
        internal int Calls;
        public ActivityRequestResult TryRequest(ActivityRequest request, ScenarioRuntimeSnapshot context)
        {
            Calls++;
            if (request == null || context == null || request.runId != context.runId ||
                !context.capabilities.Any(capability => capability.capabilityId == request.capabilityId))
                return ActivityRequestResult.Unavailable("capability_unavailable");
            return ActivityRequestResult.Accepted("fixture-activity-authoritative-1");
        }
    }

    internal static class ScenarioTestData
    {
        internal static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools/ScenarioRuntime/Fixtures/v4/" + name));
        }

        internal static string ConversationFixture(string name)
        {
            return File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools/Conversation/Fixtures/v2/" + name));
        }

        internal static ScenarioRuntimeSnapshot RuntimeSnapshot()
        {
            return new ScenarioRuntimeSnapshot("", "run-1", "turn-1", 1, "",
                new[] { "cats", "drawing_together", "tea" }, new[] { "happy", "gentle", "thinking" },
                new[] { new ScenarioCapabilitySnapshot("fixture.activity.drawing_together",
                    new[] { new ScenarioArgumentBinding("targetObjectId", new[] { "fixture-drawing-table" }) }) },
                new[] { new ScenarioObjectSnapshot("fixture-drawing-table", "use_point") },
                new[] { "fixture.player_at_adjacent_seat" });
        }

        internal static ScenarioDefinition WithStepCount(ScenarioDefinition source, int count)
        {
            var copy = new ScenarioDefinition { scenarioId = source.scenarioId, topicId = source.topicId, entryStepId = source.entryStepId };
            copy.steps.AddRange(source.steps);
            while (copy.steps.Count < count)
                copy.steps.Add(new ScenarioStep { id = "extra-end-" + copy.steps.Count, kind = "end", outcome = "completed", closeExchange = true });
            return copy;
        }

        internal static ScenarioRuntimeSnapshot WithAdditionalObject(ScenarioRuntimeSnapshot source, ScenarioObjectSnapshot extra)
        {
            var objects = new List<ScenarioObjectSnapshot>(source.objects) { extra };
            return new ScenarioRuntimeSnapshot(source.scenarioId, source.runId, source.turnId, source.contextRevision,
                source.checkpointStepId, source.knownTopicIds, source.allowedExpressions, source.capabilities, objects, source.waitConditions);
        }

        internal static ScenarioRuntimeSnapshot WithTurn(ScenarioRuntimeSnapshot source, string turnId)
        {
            return new ScenarioRuntimeSnapshot(source.scenarioId, source.runId, turnId, source.contextRevision,
                source.checkpointStepId, source.knownTopicIds, source.allowedExpressions, source.capabilities, source.objects, source.waitConditions, source.knownIntentIds);
        }

        internal static ScenarioRuntimeSnapshot WithRun(ScenarioRuntimeSnapshot source, string runId)
        {
            return new ScenarioRuntimeSnapshot(source.scenarioId, runId, source.turnId, source.contextRevision,
                source.checkpointStepId, source.knownTopicIds, source.allowedExpressions, source.capabilities, source.objects, source.waitConditions, source.knownIntentIds);
        }

        internal static ScenarioRuntimeSnapshot WithKnownTopics(ScenarioRuntimeSnapshot source, params string[] topics)
        {
            return new ScenarioRuntimeSnapshot(source.scenarioId, source.runId, source.turnId, source.contextRevision,
                source.checkpointStepId, topics, source.allowedExpressions, source.capabilities, source.objects, source.waitConditions, source.knownIntentIds);
        }

        internal static ScenarioRuntimeSnapshot WithKnownIntents(ScenarioRuntimeSnapshot source, params string[] intents)
        {
            return new ScenarioRuntimeSnapshot(source.scenarioId, source.runId, source.turnId, source.contextRevision,
                source.checkpointStepId, source.knownTopicIds, source.allowedExpressions, source.capabilities, source.objects, source.waitConditions, intents);
        }

        internal static ScenarioBeat Beat(string id, string display)
        { return new ScenarioBeat { id = id, text = "続けよう。", displayText = display, language = "japanese", expression = "gentle" }; }

        internal static ScenarioDefinition DialogueWaitDefinition()
        {
            return new ScenarioDefinition
            {
                scenarioId = "scenario-dialogue-wait", topicId = "cats", entryStepId = "opening",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "opening", kind = "dialogue", beats = new List<ScenarioBeat> { Beat("opening-1", "Let's begin.") },
                        nextStepId = "wait", closeExchange = true },
                    new ScenarioStep { id = "wait", kind = "wait", activityId = "activity-1", conditionId = "fixture.player_at_adjacent_seat",
                        objective = "Join Alterra.", successNextStepId = "end", failureNextStepId = "end" },
                    new ScenarioStep { id = "end", kind = "end", outcome = "completed", closeExchange = true }
                }
            };
        }

        internal static ScenarioDefinition QuestionDefinition()
        {
            return new ScenarioDefinition
            {
                scenarioId = "scenario-question", topicId = "cats", entryStepId = "ask",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "ask", kind = "ask", questionId = "favorite-trait", question = Beat("q", "What do you like?"),
                        options = new List<ScenarioQuestionOption> { new ScenarioQuestionOption { optionId = "playful", label = "Playful", value = "playful", nextStepId = "close" } },
                        allowFreeText = true, freeTextNextStepId = "close", skipNextStepId = "close", cancelNextStepId = "close" },
                    new ScenarioStep { id = "close", kind = "dialogue", beats = new List<ScenarioBeat> { Beat("thanks", "Thanks.") },
                        nextStepId = "end", closeExchange = true },
                    new ScenarioStep { id = "end", kind = "end", outcome = "completed", closeExchange = true }
                }
            };
        }

        internal static ScenarioDefinition ActionDefinition()
        {
            return new ScenarioDefinition
            {
                scenarioId = "scenario-action", topicId = "drawing_together", entryStepId = "action",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "action", kind = "action", capabilityId = "fixture.activity.drawing_together",
                        arguments = new List<ScenarioActionArgument> { new ScenarioActionArgument { name = "targetObjectId", value = "fixture-drawing-table" } },
                        successNextStepId = "wait", failureNextStepId = "end" },
                    new ScenarioStep { id = "wait", kind = "wait", activityId = "activity-1", conditionId = "fixture.player_at_adjacent_seat",
                        objective = "Join Alterra.", successNextStepId = "end", failureNextStepId = "end" },
                    new ScenarioStep { id = "end", kind = "end", outcome = "completed", closeExchange = true }
                }
            };
        }

        internal static ScenarioDefinition AutomaticBranchCycle()
        {
            return new ScenarioDefinition
            {
                scenarioId = "scenario-cycle", topicId = "cats", entryStepId = "loop-a",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "loop-a", kind = "branch", sourceId = "loop-b", defaultNextStepId = "loop-b",
                        cases = new List<ScenarioBranchCase> { new ScenarioBranchCase { value = "x", nextStepId = "loop-b" } } },
                    new ScenarioStep { id = "loop-b", kind = "branch", sourceId = "loop-a", defaultNextStepId = "loop-a",
                        cases = new List<ScenarioBranchCase> { new ScenarioBranchCase { value = "x", nextStepId = "loop-a" } } }
                }
            };
        }
    }
}
