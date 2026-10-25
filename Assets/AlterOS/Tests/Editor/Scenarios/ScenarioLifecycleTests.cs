using System.Linq;
using NUnit.Framework;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioLifecycleTests
    {
        [Test]
        public void NoEventTimeAndExchangeClosingDoNotCompleteTheScenario()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            var started = session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);
            Assert.AreEqual("show_dialogue", started[0].type);

            session.Advance(new ScenarioEvent { kind = "time_advanced", runId = "run-1", stepId = "opening", elapsedSeconds = 7200 }, context);
            Assert.AreEqual("opening", session.Running.currentStepId);

            var closed = session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = "run-1", stepId = "opening" }, context);
            Assert.IsTrue(closed.Any(effect => effect.type == "close_exchange"));
            Assert.IsTrue(closed.Any(effect => effect.type == "wait_for_condition"));
            Assert.AreEqual("wait", session.Running.currentStepId);
            Assert.AreEqual("running", session.Running.status);
        }

        [Test]
        public void WrapUpCompletesOnceAndQuitInterruptsRunningAndSuspendedWithoutRewards()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);
            session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = "run-1", stepId = "opening" }, context);
            var staleCompletion = session.Advance(new ScenarioEvent { kind = "condition_met", runId = "run-1", stepId = "wait",
                activityId = "disappeared-table-activity", conditionId = "fixture.player_at_adjacent_seat" }, context);
            Assert.IsEmpty(staleCompletion);
            Assert.AreEqual("wait", session.Running.currentStepId);
            var wrongCondition = session.Advance(new ScenarioEvent { kind = "condition_met", runId = "run-1", stepId = "wait",
                activityId = "activity-1", conditionId = "fixture.different_condition" }, context);
            Assert.IsEmpty(wrongCondition);
            Assert.AreEqual("wait", session.Running.currentStepId);
            var completed = session.Advance(new ScenarioEvent { kind = "condition_met", runId = "run-1", stepId = "wait",
                activityId = "activity-1", conditionId = "fixture.player_at_adjacent_seat" }, context);
            Assert.AreEqual("completed", completed.Single(effect => effect.type == "end_scenario").outcome);
            Assert.IsNull(session.Running);
            Assert.IsEmpty(session.Advance(new ScenarioEvent { kind = "condition_met", runId = "run-1", stepId = "wait" }, context));

            ScenarioRuntimeSnapshot activeContext = ScenarioTestData.WithRun(context, "run-active");
            session.Start(ScenarioTestData.ActionDefinition(), "run-active", activeContext);
            session.Advance(new ScenarioEvent { kind = "activity_result", runId = "run-active", stepId = "action",
                activityId = "activity-drawing-1", success = true }, activeContext);
            var paused = session.RequestDiversion(ScenarioTestData.DialogueWaitDefinition(), "run-next", DiversionDecision.Pause,
                ScenarioTestData.WithRun(context, "run-next"));
            Assert.IsTrue(paused.Any(effect => effect.type == "release_activity"));
            Assert.AreEqual("run-active", session.Suspended.runId);
            Assert.AreEqual("run-next", session.Running.runId);

            var quit = session.Quit();
            Assert.AreEqual(2, quit.Count(effect => effect.type == "end_scenario" && effect.outcome == "interrupted_by_quit"));
            Assert.IsNull(session.Running);
            Assert.IsNull(session.Suspended);
            Assert.IsNull(new ScenarioSession().Running);
            Assert.IsNull(new ScenarioSession().Suspended);
        }

        [Test]
        public void BackOutPauseAndReplacementRespectTheSingleSuspendedSlot()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);

            ScenarioRuntimeSnapshot nextContext = ScenarioTestData.WithRun(context, "run-2");
            session.RequestDiversion(ScenarioTestData.QuestionDefinition(), "run-2", null, nextContext);
            Assert.AreEqual("run-1", session.Running.runId);
            session.RequestDiversion(ScenarioTestData.QuestionDefinition(), "run-2", DiversionDecision.BackOut, nextContext);
            Assert.AreEqual("run-1", session.Running.runId);
            Assert.IsNull(session.Suspended);

            session.RequestDiversion(ScenarioTestData.QuestionDefinition(), "run-2", DiversionDecision.Pause, nextContext);
            Assert.AreEqual("run-1", session.Suspended.runId);
            Assert.AreEqual("run-2", session.Running.runId);
            string preserved = session.Suspended.currentStepId;

            ScenarioRuntimeSnapshot run3Context = ScenarioTestData.WithRun(context, "run-3");
            var blocked = session.RequestDiversion(ScenarioTestData.ActionDefinition(), "run-3", DiversionDecision.Pause, run3Context);
            Assert.IsTrue(blocked.Any(effect => effect.type == "suspended_slot_full"));
            Assert.AreEqual("run-1", session.Suspended.runId);
            Assert.AreEqual(preserved, session.Suspended.currentStepId);
            Assert.AreEqual("run-2", session.Running.runId);

            session.RequestDiversion(ScenarioTestData.ActionDefinition(), "run-3", DiversionDecision.ReplaceSuspended, run3Context);
            Assert.AreEqual("run-2", session.Suspended.runId);
            Assert.AreEqual("run-3", session.Running.runId);
        }

        [Test]
        public void ContinueSuspendedRequiresAnExplicitEvent()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);
            ScenarioRuntimeSnapshot run2Context = ScenarioTestData.WithRun(context, "run-2");
            session.RequestDiversion(ScenarioTestData.QuestionDefinition(), "run-2", DiversionDecision.Pause, run2Context);
            Assert.AreEqual("run-1", session.Suspended.runId);
            session.Advance(new ScenarioEvent { kind = "answer", runId = "run-2", stepId = "ask",
                questionId = "favorite-trait", answerId = "playful", contextRevision = context.contextRevision }, run2Context);
            session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = "run-2", stepId = "close" }, run2Context);
            Assert.IsNull(session.Running);

            var resumed = session.Advance(new ScenarioEvent { kind = "continue_suspended", runId = "run-1" }, context);
            Assert.AreEqual("run-1", session.Running.runId);
            Assert.IsNull(session.Suspended);
            Assert.AreEqual("show_dialogue", resumed[0].type);
        }

        [Test]
        public void ReturnPromptTriggerDependsOnActivityParticipants()
        {
            foreach (string participant in new[] { "alterra", "player", "together" })
            {
                var session = new ScenarioSession();
                ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
                ScenarioRuntimeSnapshot activeContext = ScenarioTestData.WithRun(context, "run-" + participant);
                ScenarioRuntimeSnapshot diversionContext = ScenarioTestData.WithRun(context, "diversion-" + participant);
                session.Start(ScenarioTestData.ActionDefinition(), "run-" + participant, activeContext);
                session.Advance(new ScenarioEvent { kind = "activity_result", runId = "run-" + participant, stepId = "action",
                    activityId = "activity-" + participant, participant = participant, success = true }, activeContext);
                session.RequestDiversion(ScenarioTestData.DialogueWaitDefinition(), "diversion-" + participant,
                    DiversionDecision.Pause, diversionContext);

                var completed = session.Advance(new ScenarioEvent { kind = "activity_completed", runId = "run-" + participant,
                    activityId = "activity-" + participant, participant = participant, canTalk = true, focusAllowed = true, quiet = false }, activeContext);
                if (participant == "alterra")
                    Assert.AreEqual("return_prompt", completed[0].type);
                else
                    Assert.IsEmpty(completed);

                if (participant != "alterra")
                {
                    var nextInteraction = session.Advance(new ScenarioEvent { kind = "player_interaction", runId = "run-" + participant,
                        canTalk = true, focusAllowed = true, quiet = false }, activeContext);
                    Assert.AreEqual("return_prompt", nextInteraction[0].type);
                }
                Assert.AreEqual("suspended", session.Suspended.status);
                Assert.AreEqual("diversion-" + participant, session.Running.runId);
            }
        }

        [Test]
        public void AutomaticBranchCycleFailsAtTheTransitionBudgetInsteadOfLooping()
        {
            var session = new ScenarioSession();
            var effects = session.Start(ScenarioTestData.AutomaticBranchCycle(), "run-cycle",
                ScenarioTestData.WithRun(ScenarioTestData.RuntimeSnapshot(), "run-cycle"));
            Assert.AreEqual("failed", effects.Single(effect => effect.type == "end_scenario").outcome);
            Assert.IsNull(session.Running);
        }

        [Test]
        public void ActivityPortReturnsAnAuthoritativeIdAndRejectsMissingCapability()
        {
            var port = new FakeScenarioActivityPort();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            var allowed = port.TryRequest(new ActivityRequest("run-1", "action", "fixture.activity.drawing_together",
                new[] { new ScenarioActionArgument { name = "targetObjectId", value = "fixture-drawing-table" } }), context);
            Assert.AreEqual("accepted", allowed.status);
            Assert.AreEqual("fixture-activity-authoritative-1", allowed.activityId);

            var unavailable = port.TryRequest(new ActivityRequest("run-1", "action", "arbitrary.external.write", null), context);
            Assert.AreEqual("unavailable", unavailable.status);
            Assert.AreEqual(2, port.Calls);
        }

        [Test]
        public void NotYetKeepsSuspendedAndWrapUpEndsItWithoutSuccess()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);
            session.RequestDiversion(ScenarioTestData.QuestionDefinition(), "run-2", DiversionDecision.Pause,
                ScenarioTestData.WithRun(context, "run-2"));
            Assert.IsEmpty(session.Advance(new ScenarioEvent { kind = "not_yet", runId = "run-1" }, context));
            Assert.AreEqual("suspended", session.Suspended.status);

            var ended = session.Advance(new ScenarioEvent { kind = "wrap_up", runId = "run-1" }, context);
            Assert.AreEqual("close_exchange", ended[0].type);
            Assert.AreEqual("player_ended", ended.Single(effect => effect.type == "end_scenario").outcome);
            Assert.IsNull(session.Suspended);
            Assert.AreEqual("run-2", session.Running.runId);
        }

        [Test]
        public void CompatibleSelfCareActivityDoesNotCreateOrCompleteAnotherScenario()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);
            session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = "run-1", stepId = "opening" }, context);

            var selfCare = session.Advance(new ScenarioEvent { kind = "activity_completed", runId = "run-1", stepId = "wait",
                activityId = "self-care-drink-1", participant = "self_care" }, context);

            Assert.IsEmpty(selfCare);
            Assert.AreEqual("run-1", session.Running.runId);
            Assert.AreEqual("wait", session.Running.currentStepId);
            Assert.IsNull(session.Suspended);
        }

        [Test]
        public void StartAndEventsRejectRuntimeSnapshotsBoundToAnotherRun()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            Assert.AreEqual("scenario_start_rejected", session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1",
                ScenarioTestData.WithRun(context, "other-run"))[0].type);
            Assert.IsNull(session.Running);

            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);
            Assert.IsEmpty(session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = "run-1", stepId = "opening" },
                ScenarioTestData.WithRun(context, "other-run")));
            Assert.AreEqual("opening", session.Running.currentStepId);
        }

        [Test]
        public void WrapUpRejectsSnapshotBoundToAnotherRun()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", context);

            Assert.IsEmpty(session.Advance(new ScenarioEvent { kind = "wrap_up", runId = "run-1" },
                ScenarioTestData.WithRun(context, "other-run")));
            Assert.AreEqual("run-1", session.Running.runId);
        }
    }
}
