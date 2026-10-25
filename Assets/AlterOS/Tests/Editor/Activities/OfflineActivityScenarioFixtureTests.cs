using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;

namespace AlterOS.Tests.Activities
{
    public sealed class OfflineActivityScenarioFixtureTests
    {
        [TestCase("come_here.json")]
        [TestCase("follow_me.json")]
        [TestCase("drink_water.json")]
        public void FixtureValidatesAgainstOnlyItsHostCapability(string file)
        {
            ScenarioDefinition definition = Load(file);
            ScenarioRuntimeSnapshot context = Context(definition, "run-" + definition.scenarioId, definition.entryStepId);
            Assert.IsEmpty(ScenarioDefinitionValidator.Validate(definition, context));
        }

        [TestCase("come_here.json", "arrived")]
        [TestCase("follow_me.json", "finished")]
        [TestCase("drink_water.json", "drank")]
        public void FixtureRunsOfflineThroughScenarioSessionAndCorrelatedFakeHost(string file, string expectedDialogue)
        {
            ScenarioDefinition definition = Load(file);
            string runId = "run-offline-" + definition.scenarioId;
            ScenarioRuntimeSnapshot startContext = Context(definition, runId, definition.entryStepId);
            var session = new ScenarioSession();
            IReadOnlyList<ScenarioEffect> entered = session.Start(definition, runId, startContext);
            Assert.IsTrue(entered.Any(effect => effect.type == "ask_player" || effect.type == "show_dialogue"));
            ScenarioEvent input;
            if (definition.steps.Find(step => step.id == definition.entryStepId).kind == "dialogue")
            {
                input = new ScenarioEvent { kind = "dialogue_complete", runId = runId, stepId = definition.entryStepId };
            }
            else
            {
                ScenarioStep ask = definition.steps.Find(step => step.id == definition.entryStepId);
                input = new ScenarioEvent
                {
                    kind = "answer", runId = runId, stepId = ask.id, questionId = ask.questionId,
                    answerId = "yes", contextRevision = startContext.contextRevision
                };
            }
            IReadOnlyList<ScenarioEffect> actionEffects = session.Advance(input, startContext);
            ScenarioEffect request = actionEffects.Single(effect => effect.type == "request_activity");
            ScenarioStep action = definition.steps.Find(step => step.id == request.stepId);
            ActivityExecutorKind executorKind = action.capabilityId == ActivityCapabilityIds.DrinkWater
                ? ActivityExecutorKind.DrinkWater : ActivityExecutorKind.Movement;
            ActivitySettings settings = new ActivitySettings(action.capabilityId, executorKind);
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { executorKind, activityContext => new CompletedFakeExecution(activityContext) }
            };
            var host = new ActivityHost(new[] { settings }, factories, () => "ticket-" + runId);
            var port = new ScenarioActivityAdapter(host);
            ScenarioRuntimeSnapshot actionContext = Context(definition, runId, action.id);
            ActivityRequestResult accepted = port.TryRequest(request.activityRequest, actionContext);
            Assert.AreEqual("accepted", accepted.status);
            IReadOnlyList<ScenarioEffect> waiting = session.Advance(new ScenarioEvent
            {
                kind = "activity_result", runId = runId, stepId = action.id,
                success = true, activityId = accepted.activityId, participant = "alterra"
            }, actionContext);
            Assert.IsTrue(waiting.Any(effect => effect.type == "wait_for_condition"));

            host.Tick(1d);
            ScenarioEvent terminal = port.DrainScenarioEvents(session.Running, session.Suspended).Single();
            Assert.AreEqual(ActivityConditionIds.Terminal, terminal.conditionId);
            IReadOnlyList<ScenarioEffect> result = session.Advance(terminal, actionContext);
            Assert.IsTrue(result.Any(effect => effect.type == "show_dialogue" && effect.step.id == expectedDialogue));
            Assert.IsFalse(definition.steps.Find(step => step.id == expectedDialogue).closeExchange);
            Assert.AreEqual(runId, session.Running.runId);
        }

        [Test]
        public void UnavailableActionUsesItsDeclaredFailureDialogue()
        {
            ScenarioDefinition definition = Load("drink_water.json");
            string runId = "run-unavailable-drink";
            ScenarioRuntimeSnapshot startContext = Context(definition, runId, definition.entryStepId);
            var session = new ScenarioSession();
            session.Start(definition, runId, startContext);
            ScenarioStep ask = definition.steps.Find(step => step.id == definition.entryStepId);
            IReadOnlyList<ScenarioEffect> effects = session.Advance(new ScenarioEvent
            {
                kind = "answer", runId = runId, stepId = ask.id, questionId = ask.questionId,
                answerId = "yes", contextRevision = startContext.contextRevision
            }, startContext);
            ScenarioEffect request = effects.Single(effect => effect.type == "request_activity");
            ActivityRequestResult unavailable = new ScenarioActivityAdapter(null).TryRequest(request.activityRequest,
                Context(definition, runId, request.stepId));
            Assert.AreEqual("unavailable", unavailable.status);
            IReadOnlyList<ScenarioEffect> failure = session.Advance(new ScenarioEvent
            {
                kind = "activity_result", runId = runId, stepId = request.stepId, success = false
            }, startContext);
            Assert.IsTrue(failure.Any(effect => effect.type == "show_dialogue" && effect.step.id == "could_not_start"));
        }

        [Test]
        public void DecliningFollowStartsNoActivity()
        {
            ScenarioDefinition definition = Load("follow_me.json");
            ScenarioRuntimeSnapshot context = Context(definition, "run-decline", definition.entryStepId);
            var session = new ScenarioSession();
            Assert.IsTrue(session.Start(definition, context.runId, context).Any(effect => effect.type == "ask_player"));

            IReadOnlyList<ScenarioEffect> declined = session.Advance(new ScenarioEvent
            {
                kind = "answer", runId = context.runId, stepId = "ask_to_follow", questionId = "follow_confirmation",
                answerId = "no", contextRevision = context.contextRevision
            }, context);

            Assert.IsFalse(declined.Any(effect => effect.type == "request_activity"));
            Assert.IsTrue(declined.Any(effect => effect.type == "show_dialogue" && effect.step.id == "declined"));
        }

        [Test]
        public void FollowStopAndExchangeSignalsStayWithTheSameScenarioRun()
        {
            ScenarioDefinition definition = Load("follow_me.json");
            ScenarioRuntimeSnapshot context = Context(definition, "run-follow", definition.entryStepId);
            var session = new ScenarioSession();
            session.Start(definition, context.runId, context);
            IReadOnlyList<ScenarioEffect> actionEffects = session.Advance(new ScenarioEvent
            {
                kind = "answer", runId = context.runId, stepId = "ask_to_follow", questionId = "follow_confirmation",
                answerId = "yes", contextRevision = context.contextRevision
            }, context);
            ScenarioEffect action = actionEffects.Single(effect => effect.type == "request_activity");
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.Movement, activity => new StopExecution(activity) }
            };
            var host = new ActivityHost(new[]
            {
                new ActivitySettings(ActivityCapabilityIds.FollowPlayer, ActivityExecutorKind.Movement)
            }, factories, () => "follow-ticket");
            var adapter = new ScenarioActivityAdapter(host);
            ScenarioRuntimeSnapshot requestContext = Context(definition, context.runId, "follow");
            ActivityRequestResult accepted = adapter.TryRequest(action.activityRequest, requestContext);
            Assert.AreEqual("accepted", accepted.status);
            session.Advance(new ScenarioEvent
            {
                kind = "activity_result", runId = context.runId, stepId = "follow", success = true,
                activityId = accepted.activityId, participant = "alterra"
            }, requestContext);

            Assert.IsTrue(adapter.BeginExchange(accepted.activityId, "exchange-1"));
            Assert.IsTrue(adapter.EndExchange(accepted.activityId, "exchange-1"));
            Assert.AreEqual(context.runId, session.Running.runId);
            Assert.IsTrue(adapter.Stop(accepted.activityId, "player_stopped"));
            ScenarioEvent terminal = adapter.DrainScenarioEvents(session.Running, session.Suspended).Single();
            Assert.AreEqual("condition_failed", terminal.kind);
            IReadOnlyList<ScenarioEffect> finished = session.Advance(terminal, requestContext);
            Assert.AreEqual(context.runId, session.Running.runId);
            Assert.IsTrue(finished.Any(effect => effect.type == "show_dialogue" && effect.step.id == "finished"));
            Assert.IsFalse(finished.Any(effect => effect.type == "request_activity"));
        }

        [Test]
        public void CompletionDialogueDoesNotRequestAutomaticExchangeClose()
        {
            ScenarioDefinition comeHere = Load("come_here.json");
            ScenarioDefinition follow = Load("follow_me.json");
            ScenarioDefinition drink = Load("drink_water.json");
            foreach (ScenarioDefinition definition in new[] { comeHere, follow, drink })
            {
                foreach (ScenarioStep step in definition.steps.Where(item => item.kind == "dialogue" && item.id != definition.entryStepId))
                    Assert.IsFalse(step.closeExchange, definition.scenarioId + ":" + step.id);
            }
        }

        [Test]
        public void UnavailableDrinkBranchesNeverClaimSheDrank()
        {
            ScenarioDefinition drink = Load("drink_water.json");
            foreach (string id in new[] { "could_not_start", "not_drunk" })
            {
                ScenarioStep step = drink.steps.Single(item => item.id == id);
                string speech = string.Join(" ", step.beats.Select(beat => beat.text));
                string display = string.Join(" ", step.beats.Select(beat => beat.displayText));
                StringAssert.DoesNotContain("飲めた", speech);
                StringAssert.DoesNotContain("got some water", display);
                StringAssert.DoesNotContain("I drank", display);
            }
        }

        [Test]
        public void RepeatingActivityAttemptUsesFreshRunAndTicket()
        {
            var settings = new ActivitySettings(ActivityCapabilityIds.ComeHere, ActivityExecutorKind.Movement);
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.Movement, activity => new StopExecution(activity) }
            };
            int next = 0;
            var host = new ActivityHost(new[] { settings }, factories, () => "ticket-" + (++next));
            ActivityRequestResult first = host.Request(Request("run-a", "action"), Context("run-a", "action"));
            host.Stop(first.activityId, "player_stopped");
            ActivityRequestResult second = host.Request(Request("run-b", "action"), Context("run-b", "action"));
            Assert.AreEqual("accepted", first.status);
            Assert.AreEqual("accepted", second.status);
            Assert.AreNotEqual(first.activityId, second.activityId);
        }

        private static ScenarioDefinition Load(string file)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/AlterOS/Testbox/Scenarios/" + file);
            ScenarioDefinition definition = UnityEngine.JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(path));
            Assert.IsNotNull(definition, path);
            return definition;
        }

        private static ActivityRequest Request(string runId, string stepId)
        { return new ActivityRequest(runId, stepId, ActivityCapabilityIds.ComeHere, new[] { new ScenarioActionArgument { name = "target", value = "player" } }); }

        private static ScenarioRuntimeSnapshot Context(string runId, string stepId)
        { return Context(new ScenarioDefinition { scenarioId = "m3.come_here.test", topicId = "m3_testbox" }, runId, stepId); }

        private static ScenarioRuntimeSnapshot Context(ScenarioDefinition definition, string runId, string stepId)
        {
            string capability = definition.steps == null ? ActivityCapabilityIds.ComeHere :
                definition.steps.FirstOrDefault(item => item != null && item.kind == "action")?.capabilityId ?? ActivityCapabilityIds.ComeHere;
            string[] values = capability == ActivityCapabilityIds.DrinkWater
                ? new[] { "available_dispenser" } : new[] { "player" };
            return new ScenarioRuntimeSnapshot(definition.scenarioId, runId, "turn-" + runId, 1, stepId,
                new[] { definition.topicId }, new[] { "gentle", "happy", "thinking" },
                new[] { new ScenarioCapabilitySnapshot(capability,
                    new[] { new ScenarioArgumentBinding("target", values) }) },
                null, new[] { ActivityConditionIds.Terminal });
        }

        private sealed class StopExecution : IActivityExecution
        {
            private readonly ActivityExecutionContext context;
            private ActivityView view;
            public ActivityView View { get { return view; } }
            internal StopExecution(ActivityExecutionContext activityContext) { context = activityContext; }
            public void Start(double nowSeconds) { view = NewView(ActivityTerminalReason.None); }
            public void Tick(double nowSeconds) { }
            public bool Stop(string reason, double nowSeconds) { Finish(ActivityTerminalReason.PlayerStopped); return true; }
            public void BeginExchange(string exchangeId, double nowSeconds) { }
            public void EndExchange(string exchangeId, double nowSeconds) { }
            public void Shutdown(string reason, double nowSeconds) { Finish(ActivityTerminalReason.InterruptedByQuit); }
            public IReadOnlyList<ActivityEvent> DrainEvents()
            {
                if (view == null || !view.isTerminal) return new ActivityEvent[0];
                ActivityEvent terminal = new ActivityEvent(ActivityEventKind.Terminal, context.activityId,
                    context.request.runId, context.request.stepId, view.phase, view.terminalReason, "");
                view = NewView(view.terminalReason);
                return new[] { terminal };
            }
            private void Finish(ActivityTerminalReason reason) { view = NewView(reason); }
            private ActivityView NewView(ActivityTerminalReason reason)
            { return new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, reason == ActivityTerminalReason.None ? "running" : "terminal", reason, true, true); }
        }

        private sealed class CompletedFakeExecution : IActivityExecution
        {
            private readonly ActivityExecutionContext context;
            private ActivityView view;
            private bool completed;
            public ActivityView View { get { return view; } }
            internal CompletedFakeExecution(ActivityExecutionContext activityContext) { context = activityContext; }
            public void Start(double nowSeconds) { view = NewView(ActivityTerminalReason.None); }
            public void Tick(double nowSeconds)
            {
                if (completed) return;
                completed = true;
                view = NewView(ActivityTerminalReason.Completed);
            }
            public bool Stop(string reason, double nowSeconds) { return false; }
            public void BeginExchange(string exchangeId, double nowSeconds) { }
            public void EndExchange(string exchangeId, double nowSeconds) { }
            public void Shutdown(string reason, double nowSeconds) { view = NewView(ActivityTerminalReason.InterruptedByQuit); }
            public IReadOnlyList<ActivityEvent> DrainEvents()
            {
                if (view == null || !view.isTerminal) return new ActivityEvent[0];
                ActivityEvent terminal = new ActivityEvent(ActivityEventKind.Terminal, context.activityId,
                    context.request.runId, context.request.stepId, view.phase, view.terminalReason, "");
                view = NewView(view.terminalReason);
                return new[] { terminal };
            }
            private ActivityView NewView(ActivityTerminalReason reason)
            { return new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, reason == ActivityTerminalReason.None ? "running" : "completed", reason, true, true); }
        }
    }
}
