using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;

namespace AlterOS.Tests.Activities
{
    [TestFixture]
    public sealed class ActivityHostTests
    {
        [Test]
        public void LifecycleNotificationsDoNotConsumeScenarioEventsOrDuplicateStartAndFinish()
        {
            List<FakeActivityExecution> executions;
            var host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            var notifications = new List<ActivityEvent>();
            host.LifecycleChanged += notifications.Add;
            var request = Request("run-1", "step-1", "movement.come_here", Arg("target", "player"));
            var context = Context("run-1", "movement.come_here", "target", "player");
            var result = host.Request(request, context);
            host.Request(request, context);
            host.Stop(result.activityId, "player_stopped");
            host.Stop(result.activityId, "player_stopped");
            CollectionAssert.AreEqual(new[] { ActivityEventKind.Accepted, ActivityEventKind.Terminal },
                notifications.Select(item => item.kind).ToArray());
            Assert.AreEqual(1, host.DrainEvents().Count(item => item.kind == ActivityEventKind.Terminal));
        }

        [Test]
        public void DuplicateRequestReturnsSameTicketAndNoSecondStart()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            ActivityRequest request = Request("run-1", "step-1", "movement.come_here", Arg("target", "player"));
            ScenarioRuntimeSnapshot context = Context("run-1", "movement.come_here", "target", "player");

            ActivityRequestResult first = host.Request(request, context);
            ActivityRequestResult duplicate = host.Request(request, context);

            Assert.AreEqual("accepted", first.status);
            Assert.AreEqual(first.activityId, duplicate.activityId);
            Assert.AreEqual(1, executions.Count);
            Assert.AreEqual(1, executions[0].StartCalls);
        }

        [Test]
        public void ChangedPayloadForSameRequestIdentityIsRejected()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            ScenarioRuntimeSnapshot context = Context("run-1", "movement.come_here", "target", "player");

            ActivityRequestResult first = host.Request(Request("run-1", "step-1", "movement.come_here", Arg("target", "player")), context);
            ActivityRequestResult conflict = host.Request(Request("run-1", "step-1", "movement.come_here", Arg("target", "someone_else")), context);

            Assert.AreEqual("accepted", first.status);
            Assert.AreEqual("unavailable", conflict.status);
            Assert.AreEqual("request_identity_conflict", conflict.reason);
            Assert.AreEqual(1, executions.Count);
        }

        [Test]
        public void SecondActivityIsBusyWhileOneOwnsExecution()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            ActivityRequestResult first = host.Request(Request("run-1", "step-1", "movement.come_here", Arg("target", "player")),
                Context("run-1", "movement.come_here", "target", "player"));

            ActivityRequestResult second = host.Request(Request("run-2", "step-2", "movement.come_here", Arg("target", "player")),
                Context("run-2", "movement.come_here", "target", "player", "step-2"));

            Assert.AreEqual("accepted", first.status);
            Assert.AreEqual("unavailable", second.status);
            Assert.AreEqual("activity_busy", second.reason);
            Assert.AreEqual(1, executions.Count);
        }

        [Test]
        public void SharedDefinitionCanCreateIndependentSequentialExecutions()
        {
            ActivitySettings sharedDefinition = new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement);
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(sharedDefinition, out executions);
            ActivityRequestResult first = host.Request(Request("run-1", "step-1", sharedDefinition.capabilityId, Arg("target", "player")),
                Context("run-1", sharedDefinition.capabilityId, "target", "player"));
            Assert.IsTrue(host.Stop(first.activityId, "player_stopped"));

            ActivityRequestResult second = host.Request(Request("run-2", "step-1", sharedDefinition.capabilityId, Arg("target", "player")),
                Context("run-2", sharedDefinition.capabilityId, "target", "player"));

            Assert.AreEqual("accepted", second.status);
            Assert.AreNotEqual(first.activityId, second.activityId);
            Assert.AreEqual(2, executions.Count);
            Assert.AreEqual(1, executions[0].StopCalls);
            Assert.AreEqual(1, executions[1].StartCalls);
            ActivityView firstOutcome;
            Assert.IsTrue(host.TryGetOutcome(first.activityId, out firstOutcome));
            Assert.AreEqual(ActivityTerminalReason.PlayerStopped, firstOutcome.terminalReason);
        }

        [Test]
        public void UnknownArgumentDoesNotStartExecutor()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);

            ActivityRequestResult result = host.Request(Request("run-1", "step-1", "movement.come_here",
                Arg("target", "player"), Arg("speed", "instant")), Context("run-1", "movement.come_here", "target", "player"));

            Assert.AreEqual("unavailable", result.status);
            Assert.AreEqual("invalid_arguments", result.reason);
            Assert.IsEmpty(executions);
        }

        [Test]
        public void MovementRequestCannotTargetArbitraryAllowedObject()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            ActivityRequest request = Request("run-1", "step-1", "movement.come_here", Arg("target", "other_actor"));
            ScenarioRuntimeSnapshot context = Context("run-1", "movement.come_here", "target", "other_actor");
            ActivityRequestResult result = host.Request(request, context);
            Assert.AreEqual("unavailable", result.status);
            Assert.AreEqual("invalid_arguments", result.reason);
            Assert.IsEmpty(executions);
        }

        [Test]
        public void UnknownCapabilityExecutorPairCannotBeRegistered()
        {
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.Movement, context => new FakeActivityExecution(context) }
            };
            Assert.Throws<ArgumentException>(() => new ActivityHost(
                new[] { new ActivitySettings("movement.teleport", ActivityExecutorKind.Movement) }, factories));
        }

        [Test]
        public void DuplicateAfterTerminalReturnsOriginalTicketAndDoesNotRepublishOutcome()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            ActivityRequest request = Request("run-1", "step-1", "movement.come_here", Arg("target", "player"));
            ScenarioRuntimeSnapshot context = Context("run-1", "movement.come_here", "target", "player");
            ActivityRequestResult first = host.Request(request, context);
            executions[0].CompleteOnNextTick = true;
            host.Tick(1.0);
            IReadOnlyList<ActivityEvent> initialEvents = host.DrainEvents();

            ActivityRequestResult duplicate = host.Request(request, context);
            IReadOnlyList<ActivityEvent> duplicateEvents = host.DrainEvents();

            Assert.AreEqual(first.activityId, duplicate.activityId);
            ActivityView outcome;
            Assert.IsTrue(host.TryGetOutcome(first.activityId, out outcome));
            Assert.AreEqual(ActivityTerminalReason.Completed, outcome.terminalReason);
            Assert.AreEqual(1, initialEvents.Count(item => item.kind == ActivityEventKind.Terminal));
            Assert.IsEmpty(duplicateEvents);
            Assert.AreEqual(1, executions.Count);
        }

        [Test]
        public void ShutdownTwiceReleasesExecutionAndPublishesTerminalOnlyOnce()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            host.Request(Request("run-1", "step-1", "movement.come_here", Arg("target", "player")),
                Context("run-1", "movement.come_here", "target", "player"));

            host.Shutdown("interrupted_by_quit");
            host.Shutdown("interrupted_by_quit");

            Assert.AreEqual(1, executions[0].ShutdownCalls);
            Assert.AreEqual(1, host.DrainEvents().Count(item => item.kind == ActivityEventKind.Terminal));
        }

        [Test]
        public void StaleStopCannotStopReplacementExecution()
        {
            List<FakeActivityExecution> executions;
            ActivityHost host = CreateHost(new ActivitySettings("movement.come_here", ActivityExecutorKind.Movement), out executions);
            ActivityRequestResult first = host.Request(Request("run-1", "step-1", "movement.come_here", Arg("target", "player")),
                Context("run-1", "movement.come_here", "target", "player"));
            Assert.IsTrue(host.Stop(first.activityId, "player_stopped"));
            ActivityRequestResult second = host.Request(Request("run-2", "step-1", "movement.come_here", Arg("target", "player")),
                Context("run-2", "movement.come_here", "target", "player"));

            Assert.IsFalse(host.Stop(first.activityId, "player_stopped"));

            Assert.AreEqual("accepted", second.status);
            Assert.AreEqual(second.activityId, host.Current.activityId);
            Assert.AreEqual(0, executions[1].StopCalls);
        }

        private static ActivityHost CreateHost(ActivitySettings settings, out List<FakeActivityExecution> executions)
        {
            var createdExecutions = new List<FakeActivityExecution>();
            executions = createdExecutions;
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { settings.executorKind, context => { var execution = new FakeActivityExecution(context); createdExecutions.Add(execution); return execution; } }
            };
            int nextId = 0;
            return new ActivityHost(new[] { settings }, factories, () => "activity-" + (++nextId));
        }

        private static ActivityRequest Request(string runId, string stepId, string capabilityId, params ScenarioActionArgument[] args)
        { return new ActivityRequest(runId, stepId, capabilityId, args); }

        private static ScenarioActionArgument Arg(string name, string value)
        { return new ScenarioActionArgument { name = name, value = value }; }

        private static ScenarioRuntimeSnapshot Context(string runId, string capabilityId, string argument, string value, string checkpoint = "step-1")
        {
            return new ScenarioRuntimeSnapshot("m3.test", runId, "turn-1", 1, checkpoint, null, null,
                new[] { new ScenarioCapabilitySnapshot(capabilityId,
                    new[] { new ScenarioArgumentBinding(argument, new[] { value }) }) }, null, null);
        }
    }

    internal sealed class FakeActivityExecution : IActivityExecution
    {
        private readonly ActivityExecutionContext context;
        private readonly List<ActivityEvent> pendingEvents = new List<ActivityEvent>();
        private ActivityView view;
        internal int StartCalls, StopCalls, ShutdownCalls;
        internal bool CompleteOnNextTick;
        public ActivityView View { get { return view; } }

        internal FakeActivityExecution(ActivityExecutionContext context) { this.context = context; }

        public void Start(double nowSeconds)
        {
            StartCalls++;
            view = NewView("running", ActivityTerminalReason.None);
            pendingEvents.Add(NewEvent(ActivityEventKind.PhaseChanged, "running", ActivityTerminalReason.None));
        }

        public void Tick(double nowSeconds)
        {
            if (CompleteOnNextTick && view != null && view.terminalReason == ActivityTerminalReason.None)
            {
                CompleteOnNextTick = false;
                Finish("completed", ActivityTerminalReason.Completed);
            }
        }

        public bool Stop(string reason, double nowSeconds)
        {
            if (view == null || view.terminalReason != ActivityTerminalReason.None) return false;
            StopCalls++;
            Finish(reason, ActivityTerminalReason.PlayerStopped);
            return true;
        }

        public void BeginExchange(string exchangeId, double nowSeconds) { }
        public void EndExchange(string exchangeId, double nowSeconds) { }

        public void Shutdown(string reason, double nowSeconds)
        {
            if (view == null || view.terminalReason != ActivityTerminalReason.None) return;
            ShutdownCalls++;
            Finish(reason, ActivityTerminalReason.InterruptedByQuit);
        }

        public IReadOnlyList<ActivityEvent> DrainEvents()
        {
            ActivityEvent[] events = pendingEvents.ToArray();
            pendingEvents.Clear();
            return events;
        }

        private void Finish(string phase, ActivityTerminalReason reason)
        {
            view = NewView(phase, reason);
            pendingEvents.Add(NewEvent(ActivityEventKind.Terminal, phase, reason));
        }

        private ActivityView NewView(string phase, ActivityTerminalReason reason)
        {
            return new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, phase, reason, true, true);
        }

        private ActivityEvent NewEvent(ActivityEventKind kind, string phase, ActivityTerminalReason reason)
        {
            return new ActivityEvent(kind, context.activityId, context.request.runId, context.request.stepId,
                phase, reason, "");
        }
    }
}


