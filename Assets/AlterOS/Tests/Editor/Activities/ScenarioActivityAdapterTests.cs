using System.Collections.Generic;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;

namespace AlterOS.Tests.Activities
{
    public sealed class ScenarioActivityAdapterTests
    {
        [Test]
        public void MissingActivityHostFailsClosed()
        {
            var adapter = new ScenarioActivityAdapter(null);
            ActivityRequestResult result = adapter.TryRequest(null, null);
            Assert.AreEqual("unavailable", result.status);
            Assert.AreEqual("activity_host_unavailable", result.reason);
        }

        [Test]
        public void HostTerminalProjectsToCorrelatedScenarioWaitExactlyOnce()
        {
            AssertScenarioTerminalProjects(ActivityTerminalReason.Completed, "condition_met", "completed");
        }

        [Test]
        public void FailedActivityUsesScenarioWaitFailureBranch()
        {
            AssertScenarioTerminalProjects(ActivityTerminalReason.Failed, "condition_failed", "failed");
        }

        private static void AssertScenarioTerminalProjects(ActivityTerminalReason reason, string expectedEvent, string expectedOutcome)
        {
            ScenarioDefinition definition = Definition();
            ScenarioRuntimeSnapshot context = Context();
            var execution = new FakeExecution();
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.Movement, activityContext => { execution.Context = activityContext; execution.TerminalReason = reason; return execution; } }
            };
            var host = new ActivityHost(new[]
            {
                new ActivitySettings(ActivityCapabilityIds.ComeHere, ActivityExecutorKind.Movement)
            }, factories, () => "activity-ticket");
            var adapter = new ScenarioActivityAdapter(host);
            var session = new ScenarioSession();

            IReadOnlyList<ScenarioEffect> entered = session.Start(definition, context.runId, context);
            ScenarioEffect requestEffect = entered.Single(effect => effect.type == "request_activity");
            ActivityRequestResult accepted = adapter.TryRequest(requestEffect.activityRequest, context);
            Assert.AreEqual("accepted", accepted.status);
            IReadOnlyList<ScenarioEffect> waiting = session.Advance(new ScenarioEvent
            {
                kind = "activity_result", runId = context.runId, stepId = "action", success = true,
                activityId = accepted.activityId, participant = "alterra"
            }, context);
            Assert.IsTrue(waiting.Any(effect => effect.type == "wait_for_condition"));

            host.Tick(1d);
            IReadOnlyList<ScenarioEvent> projected = adapter.DrainScenarioEvents(session.Running, session.Suspended);
            Assert.AreEqual(1, projected.Count);
            Assert.AreEqual(expectedEvent, projected[0].kind);
            Assert.AreEqual(context.runId, projected[0].runId);
            Assert.AreEqual("wait", projected[0].stepId);
            Assert.AreEqual(accepted.activityId, projected[0].activityId);
            Assert.AreEqual(ActivityConditionIds.Terminal, projected[0].conditionId);

            IReadOnlyList<ScenarioEffect> ended = session.Advance(projected[0], context);
            Assert.IsTrue(ended.Any(effect => effect.type == "end_scenario" && effect.outcome == expectedOutcome));
            Assert.IsEmpty(adapter.DrainScenarioEvents(session.Running, session.Suspended));
        }

        private static ScenarioDefinition Definition()
        {
            return new ScenarioDefinition
            {
                scenarioId = "activity-test", topicId = "cats", entryStepId = "action",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep
                    {
                        id = "action", kind = "action", capabilityId = ActivityCapabilityIds.ComeHere,
                        arguments = new List<ScenarioActionArgument> { new ScenarioActionArgument { name = "target", value = "player" } },
                        successNextStepId = "wait", failureNextStepId = "failed"
                    },
                    new ScenarioStep
                    {
                        id = "wait", kind = "wait", activityId = "come-here", conditionId = ActivityConditionIds.Terminal,
                        objective = "Come here movement ends.", successNextStepId = "completed", failureNextStepId = "failed"
                    },
                    new ScenarioStep { id = "completed", kind = "end", outcome = "completed" },
                    new ScenarioStep { id = "failed", kind = "end", outcome = "failed" }
                }
            };
        }

        private static ScenarioRuntimeSnapshot Context()
        {
            return new ScenarioRuntimeSnapshot("activity-test", "run-activity-test", "turn-1", 1, "action",
                new[] { "cats" }, null,
                new[] { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ComeHere,
                    new[] { new ScenarioArgumentBinding("target", new[] { "player" }) }) },
                null, new[] { ActivityConditionIds.Terminal });
        }

        private sealed class FakeExecution : IActivityExecution
        {
            internal ActivityExecutionContext Context;
            internal ActivityTerminalReason TerminalReason;
            private ActivityView view;
            private readonly List<ActivityEvent> events = new List<ActivityEvent>();
            public ActivityView View { get { return view; } }
            public void Start(double nowSeconds) { view = NewView("running", ActivityTerminalReason.None); }
            public void Tick(double nowSeconds) { Finish(TerminalReason); }
            public bool Stop(string reason, double nowSeconds) { Finish(ActivityTerminalReason.PlayerStopped); return true; }
            public void BeginExchange(string exchangeId, double nowSeconds) { }
            public void EndExchange(string exchangeId, double nowSeconds) { }
            public void Shutdown(string reason, double nowSeconds) { Finish(ActivityTerminalReason.InterruptedByQuit); }
            public IReadOnlyList<ActivityEvent> DrainEvents()
            { ActivityEvent[] copy = events.ToArray(); events.Clear(); return copy; }
            private void Finish(ActivityTerminalReason reason)
            {
                if (view == null || view.isTerminal) return;
                view = NewView("terminal", reason);
                events.Add(new ActivityEvent(ActivityEventKind.Terminal, Context.activityId, Context.request.runId,
                    Context.request.stepId, "terminal", reason, ""));
            }
            private ActivityView NewView(string phase, ActivityTerminalReason reason)
            { return new ActivityView(Context.activityId, Context.request.runId, Context.request.stepId,
                Context.settings.capabilityId, phase, reason, true, true); }
        }
    }
}
