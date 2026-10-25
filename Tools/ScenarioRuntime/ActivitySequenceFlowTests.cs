using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Scenarios;
using NUnit.Framework;

public sealed class ActivitySequenceFlowTests
{
    [Test]
    public void ActivityHostOwnsAndReplaysSequenceInvocationWithoutSharingItsRunner()
    {
        var runner = new FakeSequenceRunner();
        var host = new ActivityHost(
            new[] { new ActivitySettings(ActivityCapabilityIds.ActivitySequence, ActivityExecutorKind.ActivitySequence) },
            new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.ActivitySequence, context => new ActivitySequenceExecution(context, runner) }
            });
        var context = new ScenarioRuntimeSnapshot("scenario", "run-1", "turn-1", 1L, "scenario-step",
            new string[0], new string[0], new ScenarioCapabilitySnapshot[0], new ScenarioObjectSnapshot[0], new string[0]);
        ActivitySequenceRequest request = new ActivitySequenceRequest(Sequence("host.action", CompleteStep("finish")),
            new ContentRef("actions", "host.action", "r1", "hash"), "run-1", "run-1::flow_0001",
            "run-1::flow_exec_0001", "action-host", context, scenarioStep: "scenario-step");

        ActivityRequestResult first = host.RequestSequence(request);
        ActivityRequestResult replay = host.RequestSequence(request);
        ActivitySequenceRequest changedPresentationStep = new ActivitySequenceRequest(request.definition,
            request.actionDefinitionRef, request.scenarioRunId, request.flowInvocationId, request.stepExecutionId,
            request.actionInvocationId, request.context, scenarioStep: "presentation-step-only-change");
        ActivityRequestResult identityReplay = host.RequestSequence(changedPresentationStep);
        ActivitySequenceRequest changed = new ActivitySequenceRequest(
            new ActivitySequenceDefinition("other.action", "r1", new[] { CompleteStep("finish") }),
            request.actionDefinitionRef, request.scenarioRunId, request.flowInvocationId, request.stepExecutionId,
            request.actionInvocationId, request.context, scenarioStep: request.scenarioStepId);
        ActivityRequestResult conflict = host.RequestSequence(changed);

        Assert.That(runner.startCalls, Is.EqualTo(0), "Sequence primitives start only after the ActivityHost has registered the execution.");
        host.Tick(0d);

        Assert.That(first.status, Is.EqualTo("accepted"));
        Assert.That(replay.activityId, Is.EqualTo(first.activityId));
        Assert.That(identityReplay.activityId, Is.EqualTo(first.activityId));
        Assert.That(conflict.status, Is.EqualTo("unavailable"));
        Assert.That(conflict.reason, Is.EqualTo("request_identity_conflict"));
        Assert.That(runner.startCalls, Is.EqualTo(1));
        Assert.That(runner.lastRequest.stepExecutionId, Is.EqualTo(request.stepExecutionId));
        Assert.That(runner.lastRequest.actionInvocationId, Is.EqualTo(request.actionInvocationId));
    }

    [Test]
    public void ActivitySequenceTerminalReleasesInvocationResourcesExactlyOnce()
    {
        var runner = new FakeSequenceRunner { completeOnTick = true };
        var resources = new FakeSequenceResources();
        var host = new ActivityHost(
            new[] { new ActivitySettings(ActivityCapabilityIds.ActivitySequence, ActivityExecutorKind.ActivitySequence) },
            new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.ActivitySequence, context => new ActivitySequenceExecution(context, runner, resources) }
            });
        var context = new ScenarioRuntimeSnapshot("scenario", "run-resource", "turn", 1L, "scenario-step",
            new string[0], new string[0], new ScenarioCapabilitySnapshot[0], new ScenarioObjectSnapshot[0], new string[0]);
        var request = new ActivitySequenceRequest(Sequence("resource.action", CompleteStep("finish")),
            new ContentRef("actions", "resource.action", "r1", "hash"), "run-resource", "run-resource::flow",
            "run-resource::step-execution", "action-resource", context, scenarioStep: "scenario-step");

        Assert.That(host.RequestSequence(request).status, Is.EqualTo("accepted"));
        host.Tick(1d);
        host.Shutdown("interrupted_by_quit");

        Assert.That(resources.releaseCount, Is.EqualTo(1));
        Assert.That(resources.lastActionInvocationId, Is.EqualTo(request.actionInvocationId));
    }

    [Test]
    public void ActivityHostOwnsSequenceBeforeFirstReservePrimitiveStarts()
    {
        ActivityHost host = null;
        ActivityView ownerAtFirstPrimitive = null;
        var port = new FakeActivityPrimitivePort(request =>
        {
            ownerAtFirstPrimitive = host == null ? null : host.Current;
            return StartResult(request, ActivitySequenceStatuses.Completed, new[]
            {
                ActivitySequenceArgument.FromLiteral("targetToken",
                    FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, "target.first"))
            });
        });
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);
        host = new ActivityHost(
            new[] { new ActivitySettings(ActivityCapabilityIds.ActivitySequence, ActivityExecutorKind.ActivitySequence) },
            new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.ActivitySequence, context => new ActivitySequenceExecution(context, runner) }
            });
        var context = new ScenarioRuntimeSnapshot("scenario", "run-owner", "turn", 1L, "scenario-action",
            new string[0], new string[0], new[]
            {
                new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.ReserveNearest,
                    new[] { new ScenarioArgumentBinding("useId", new[] { "needs.drink_water" }) })
            }, new ScenarioObjectSnapshot[0], new string[0]);
        var definition = Sequence("owner.action",
            new ActivitySequenceStep("reserve", ActivityPrimitiveIds.ReserveNearest,
                new[] { ActivitySequenceArgument.FromLiteral("useId", FlowValue.Text("needs.drink_water")) },
                new[] { new ActivitySequenceResultBinding("targetToken", "target") }, success: "finish"),
            CompleteStep("finish"));
        var request = new ActivitySequenceRequest(definition, new ContentRef("actions", "owner.action", "r1", "hash"),
            "run-owner", "flow-owner", "execution-owner", "action-owner", context, scenarioStep: "scenario-action");

        ActivityRequestResult accepted = host.RequestSequence(request);

        Assert.That(accepted.status, Is.EqualTo("accepted"));
        Assert.That(port.starts, Is.Empty, "The first primitive must wait until ActivityHost has registered its execution.");
        host.Tick(0d);
        Assert.That(port.starts.Select(item => item.primitiveId), Is.EqualTo(new[] { ActivityPrimitiveIds.ReserveNearest }));
        Assert.That(ownerAtFirstPrimitive, Is.Not.Null);
        Assert.That(ownerAtFirstPrimitive.activityId, Is.EqualTo(accepted.activityId));
        Assert.That(ownerAtFirstPrimitive.capabilityId, Is.EqualTo(ActivityCapabilityIds.ActivitySequence));
    }

    [Test]
    public void ScenarioSessionWaitsForAcceptedSequenceAndIgnoresMismatchedTerminalActivity()
    {
        var definition = new ScenarioDefinition
        {
            scenarioId = "scenario.sequence-proof",
            topicId = "topic.sequence-proof",
            entryStepId = "do-action",
            steps = new List<ScenarioStep>
            {
                new ScenarioStep
                {
                    id = "do-action", kind = "action", capabilityId = ActivityCapabilityIds.ActivitySequence,
                    activitySequence = Sequence("action.proof", CompleteStep("finish")),
                    actionRef = new ContentRef("actions", "action.proof", "revision", "hash"),
                    successNextStepId = "end", failureNextStepId = "end"
                },
                new ScenarioStep { id = "end", kind = "end", outcome = "completed" }
            }
        };
        var context = new ScenarioRuntimeSnapshot("scenario.sequence-proof", "run-sequence", "turn", 1L, "",
            new[] { "topic.sequence-proof" }, new[] { "happy" },
            new[] { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]) },
            new ScenarioObjectSnapshot[0], new string[0]);
        var session = new ScenarioSession();

        IReadOnlyList<ScenarioEffect> start = session.Start(definition, "run-sequence", context);
        ActivitySequenceRequest request = start.Single().activitySequenceRequest;
        IReadOnlyList<ScenarioEffect> accepted = session.Advance(new ScenarioEvent
        {
            kind = "activity_accepted", runId = "run-sequence", stepId = "do-action", activityId = request.actionInvocationId
        }, context);
        IReadOnlyList<ScenarioEffect> wrongTerminal = session.Advance(new ScenarioEvent
        {
            kind = "activity_result", runId = "run-sequence", stepId = "do-action", activityId = "different-action", success = true
        }, context);
        IReadOnlyList<ScenarioEffect> completed = session.Advance(new ScenarioEvent
        {
            kind = "activity_result", runId = "run-sequence", stepId = "do-action", activityId = request.actionInvocationId, success = true
        }, context);

        Assert.That(request.actionDefinitionRef.id, Is.EqualTo("action.proof"));
        Assert.That(request.scenarioRunId, Is.EqualTo("run-sequence"));
        Assert.That(request.stepExecutionId, Is.Not.Empty);
        Assert.That(accepted, Is.Empty);
        Assert.That(wrongTerminal, Is.Empty);
        Assert.That(completed.Any(effect => effect.type == "end_scenario"), Is.True);
    }

    [Test]
    public void PlainScenarioRebindsSequenceOwnershipForSecondActionAndFailsUnavailableSecondAction()
    {
        ScenarioDefinition definition = TwoSequenceActions();
        ScenarioRuntimeSnapshot context = TwoSequenceContext();
        var session = new ScenarioSession();

        ScenarioEffect first = session.Start(definition, context.runId, context).Single();
        string firstActivityId = first.activitySequenceRequest.actionInvocationId;
        session.Advance(new ScenarioEvent { kind = "activity_accepted", runId = context.runId,
            stepId = "action-a", activityId = firstActivityId }, context);
        ScenarioEffect second = session.Advance(new ScenarioEvent { kind = "activity_result", runId = context.runId,
            stepId = "action-a", activityId = firstActivityId, success = true }, context).Single();
        string secondActivityId = second.activitySequenceRequest.actionInvocationId;

        Assert.That(secondActivityId, Is.Not.EqualTo(firstActivityId));
        Assert.That(session.Running.activeActivityId, Is.EqualTo(secondActivityId));
        session.Advance(new ScenarioEvent { kind = "activity_accepted", runId = context.runId,
            stepId = "action-b", activityId = firstActivityId }, context);
        session.Advance(new ScenarioEvent { kind = "activity_result", runId = context.runId,
            stepId = "action-b", activityId = firstActivityId, success = true }, context);
        Assert.That(session.Running.activeActivityId, Is.EqualTo(secondActivityId));

        IReadOnlyList<ScenarioEffect> failed = session.Advance(new ScenarioEvent
        {
            kind = "activity_result", runId = context.runId, stepId = "action-b",
            activityId = secondActivityId, success = false
        }, context);

        Assert.That(failed.Single(item => item.type == "end_scenario").outcome, Is.EqualTo("failed"));
    }

    [Test]
    public void AuthoredReleaseRowsExecuteTheThreeBehaviorProofTraces()
    {
        AssertTrace("TurnOnTv", new[] { "reserve_nearest", "move_to", "invoke_furniture", "complete" });
        AssertTrace("LocateTv", new[] { "reserve_nearest", "move_to", "bark", "complete" });
        AssertTrace("FetchDrink", new[] { "reserve_nearest", "move_to", "invoke_furniture", "move_near_player", "complete" });
    }

    [Test]
    public void ScenarioAndActivityHostCompleteEachProofTraceOnlyAfterItsPrimitiveWorkFinishes()
    {
        AssertScenarioTrace("TurnOnTv", new[] { "reserve_nearest", "move_to", "invoke_furniture", "complete" });
        AssertScenarioTrace("LocateTv", new[] { "reserve_nearest", "move_to", "bark", "complete" });
        AssertScenarioTrace("FetchDrink", new[] { "reserve_nearest", "move_to", "invoke_furniture", "move_near_player", "complete" });
    }

    [Test]
    public void PrimitiveDispatcherPollsAndCompletesOnlyTheExactAsyncInvocation()
    {
        var operation = new FakePollingOperation("move_to");
        var dispatcher = new ActivitySequencePrimitiveDispatcher(new[] { operation });
        var identity = new ActivityPrimitiveIdentity("run", "run::flow", "run::step", "activity",
            "move", "activity:move");
        var request = new ActivityPrimitiveRequest(identity, new ContentRef("store", "action", "revision", "hash"),
            "move_to", new ActivitySequenceArgument[0]);

        ActivityPrimitiveStartResult started = dispatcher.Start(request);
        ActivityPrimitiveCompletion[] early = dispatcher.Tick(0d);
        ActivityPrimitiveCompletion[] completed = dispatcher.Tick(1d);

        Assert.That(started.status, Is.EqualTo(ActivitySequenceStatuses.Running));
        Assert.That(early, Is.Empty);
        Assert.That(completed.Length, Is.EqualTo(1));
        Assert.That(completed[0].status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(completed[0].identity.actionInvocationId, Is.EqualTo(identity.actionInvocationId));
        Assert.That(operation.pollCount, Is.EqualTo(2));
    }

    [Test]
    public void BarkPrimitiveFailsWhenNonModalPresenterSuppressesTheLine()
    {
        var presenter = new FakeBarkPresenter { accepted = false };
        var operation = new ActivitySequenceBarkOperation(presenter);

        ActivityPrimitiveStartResult result = operation.Start(BarkRequest("bark-suppressed"));

        Assert.That(result.status, Is.EqualTo(ActivitySequenceStatuses.Failed));
        Assert.That(result.reasonCode, Is.EqualTo("bark_not_presented"));
        Assert.That(presenter.startCount, Is.EqualTo(1));
    }

    [Test]
    public void BarkPrimitiveCompletesOnlyAfterThePresenterAcknowledgesVisibility()
    {
        var presenter = new FakeBarkPresenter { accepted = true };
        var operation = new ActivitySequenceBarkOperation(presenter);
        ActivityPrimitiveRequest request = BarkRequest("bark-visible");

        ActivityPrimitiveStartResult started = operation.Start(request);
        ActivityPrimitiveCompletion beforeVisible = operation.Poll(request.identity, 1d);
        presenter.Complete(request.identity, true);
        ActivityPrimitiveCompletion delivered = operation.Poll(request.identity, 2d);

        Assert.That(started.status, Is.EqualTo(ActivitySequenceStatuses.Running));
        Assert.That(beforeVisible, Is.Null);
        Assert.That(delivered.status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(delivered.outputs.Single().name, Is.EqualTo("delivered"));
        Assert.That(delivered.outputs.Single().value.literal.boolValue, Is.True);
    }

    [Test]
    public void BarkPrimitiveIgnoresVisibilityCallbackAfterCancellation()
    {
        var presenter = new FakeBarkPresenter { accepted = true };
        var operation = new ActivitySequenceBarkOperation(presenter);
        ActivityPrimitiveRequest request = BarkRequest("bark-late");

        ActivityPrimitiveStartResult started = operation.Start(request);
        ActivityPrimitiveCompletion cancelled = operation.Cancel(new ActivityPrimitiveCancellation(request.identity, "quit"));
        presenter.Complete(request.identity, true);

        Assert.That(started.status, Is.EqualTo(ActivitySequenceStatuses.Running));
        Assert.That(cancelled.status, Is.EqualTo(ActivitySequenceStatuses.Cancelled));
        Assert.That(operation.Poll(request.identity, 2d), Is.Null);
        Assert.That(presenter.cancelCount, Is.EqualTo(1));
    }

    [Test]
    public void DispatcherReleasesPendingBarkAndIgnoresItsLateVisibilityCallback()
    {
        var presenter = new FakeBarkPresenter { accepted = true };
        var operation = new ActivitySequenceBarkOperation(presenter);
        var dispatcher = new ActivitySequencePrimitiveDispatcher(new[] { operation });
        ActivityPrimitiveRequest request = BarkRequest("bark-release");

        Assert.That(dispatcher.Start(request).status, Is.EqualTo(ActivitySequenceStatuses.Running));
        dispatcher.ReleaseSequence(request.identity.actionInvocationId);
        presenter.Complete(request.identity, true);
        ActivityPrimitiveCompletion[] completed = dispatcher.Tick(1d);

        Assert.That(presenter.cancelCount, Is.EqualTo(1));
        Assert.That(completed, Is.Empty);
    }

    private static void AssertTrace(string scenario, string[] expectedTrace)
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActionRecord action = TraceAction(scenario);
        ActivitySequenceBuildResult built = ActivitySequenceReleaseAdapter.Build(action, catalog.CreateBuilder());
        Assert.That(built.isValid, Is.True, Diagnostics(built));
        ScenarioRuntimeSnapshot context = TraceContext(scenario);
        var port = new FakeActivityPrimitivePort(request =>
        {
            if (request.primitiveId == ActivityPrimitiveIds.ReserveNearest)
                return StartResult(request, ActivitySequenceStatuses.Completed, new[]
                {
                    ActivitySequenceArgument.FromLiteral("targetToken",
                        FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, "opaque-target"))
                });
            return StartResult(request, ActivitySequenceStatuses.Completed);
        });
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);
        var request = new ActivitySequenceRequest(built.definition,
            new ContentRef("11111111-1111-1111-1111-111111111111", action.id, action.revision, action.semanticHash),
            "run-" + scenario.ToLowerInvariant(), "run-" + scenario.ToLowerInvariant() + "::root",
            "run-" + scenario.ToLowerInvariant() + "::step-1", "action-" + scenario.ToLowerInvariant(), context,
            scenarioStep: "action");

        ActivitySequenceStartResult started = runner.Start(request);
        Assert.That(started.status, Is.EqualTo(ActivitySequenceStatuses.Accepted), started.reasonCode);
        ActivitySequenceView view = runner.Tick(0d);
        for (int tick = 1; tick <= 4 && view.status == ActivitySequenceStatuses.Running; tick++)
            view = runner.Tick(tick);

        Assert.That(view.status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(expectedTrace));
    }

    private static void AssertScenarioTrace(string scenario, string[] expectedTrace)
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActionRecord action = TraceAction(scenario);
        ActivitySequenceBuildResult built = ActivitySequenceReleaseAdapter.Build(action, catalog.CreateBuilder());
        Assert.That(built.isValid, Is.True, Diagnostics(built));
        ScenarioRuntimeSnapshot context = TraceContext(scenario);
        var capabilities = context.capabilities.Concat(new[]
        { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]) });
        context = new ScenarioRuntimeSnapshot(context.scenarioId, context.runId, context.turnId, context.contextRevision,
            context.checkpointStepId, context.knownTopicIds, context.allowedExpressions, capabilities,
            context.objects, context.waitConditions, context.knownIntentIds,
            context.hostFacts.ToDictionary(item => item.Key, item => item.Value));
        var definition = new ScenarioDefinition
        {
            scenarioId = "scenario." + scenario,
            entryStepId = "action",
            steps = new List<ScenarioStep>
            {
                new ScenarioStep { id = "action", kind = "action", capabilityId = ActivityCapabilityIds.ActivitySequence,
                    actionRef = new ContentRef("11111111-1111-1111-1111-111111111111", action.id, action.revision, action.semanticHash),
                    activitySequence = built.definition, successNextStepId = "done", failureNextStepId = "failed" },
                new ScenarioStep { id = "done", kind = "end", outcome = "completed" },
                new ScenarioStep { id = "failed", kind = "end", outcome = "failed" }
            }
        };
        var port = new FakeActivityPrimitivePort(request =>
        {
            if (request.primitiveId == ActivityPrimitiveIds.ReserveNearest)
                return StartResult(request, ActivitySequenceStatuses.Completed, new[]
                {
                    ActivitySequenceArgument.FromLiteral("targetToken",
                        FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, "opaque-target"))
                });
            return StartResult(request, request.primitiveId == ActivityPrimitiveIds.Bark
                ? ActivitySequenceStatuses.Running : ActivitySequenceStatuses.Completed);
        });
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);
        var host = new ActivityHost(
            new[] { new ActivitySettings(ActivityCapabilityIds.ActivitySequence, ActivityExecutorKind.ActivitySequence) },
            new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.ActivitySequence, execution => new ActivitySequenceExecution(execution, runner) }
            });
        var session = new ScenarioSession();
        IReadOnlyList<ScenarioEffect> start = session.Start(definition, context.runId, context);
        ActivitySequenceRequest request = start.Single().activitySequenceRequest;
        ActivityRequestResult accepted = host.RequestSequence(request);
        Assert.That(accepted.status, Is.EqualTo("accepted"), accepted.reason);
        Assert.That(session.Advance(new ScenarioEvent
        {
            kind = "activity_accepted", runId = context.runId, stepId = "action", activityId = accepted.activityId
        }, context), Is.Empty);

        bool barkDelivered = false;
        for (int tick = 0; tick < 8; tick++)
        {
            ActivityView currentOutcome;
            if (host.TryGetOutcome(accepted.activityId, out currentOutcome) && currentOutcome.isTerminal) break;
            host.Tick(tick);
            ActivityPrimitiveRequest bark = port.starts.FirstOrDefault(item => item.primitiveId == ActivityPrimitiveIds.Bark);
            if (bark != null && scenario == "LocateTv" && !barkDelivered)
            {
                Assert.That(host.TryGetOutcome(accepted.activityId, out currentOutcome) && currentOutcome.isTerminal, Is.False,
                    "Locate must stay active until the authored bark is delivered.");
                Assert.That(host.CompletePrimitive(new ActivityPrimitiveCompletion(bark.identity,
                    ActivitySequenceStatuses.Completed, new ActivitySequenceArgument[0], "delivered")), Is.True);
                barkDelivered = true;
            }
        }

        ActivityView outcome;
        Assert.That(host.TryGetOutcome(accepted.activityId, out outcome) && outcome.isTerminal, Is.True);
        Assert.That(outcome.terminalReason, Is.EqualTo(ActivityTerminalReason.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(expectedTrace));
        IReadOnlyList<ScenarioEffect> completed = session.Advance(new ScenarioEvent
        {
            kind = "activity_result", runId = context.runId, stepId = "action", activityId = accepted.activityId, success = true
        }, context);
        Assert.That(completed.Single(item => item.type == "end_scenario").outcome, Is.EqualTo("completed"));
    }

    private static ActivityPrimitiveRequest BarkRequest(string invocationId)
    {
        var identity = new ActivityPrimitiveIdentity("run", "run::flow", "run::step", "activity",
            "bark", invocationId);
        return new ActivityPrimitiveRequest(identity, new ContentRef("store", "action.locate", "revision", "hash"),
            ActivityPrimitiveIds.Bark, new[] { ActivitySequenceArgument.FromLiteral("dialogueId", FlowValue.Text("dialogue.tv_arrival")) });
    }

    [Test]
    public void ReleaseAdapterBuildsDefinitionFromPinnedNormalizedRows()
    {
        var action = new ActionRecord
        {
            id = "sequence.proof",
            revision = new string('a', 64),
            implementationKind = "activity_sequence",
            sequenceSteps = new List<ActionSequenceStepRecord>
            {
                new ActionSequenceStepRecord
                {
                    ordinal = 0,
                    stepId = "pause",
                    primitiveId = ActivityPrimitiveIds.Wait,
                    argumentsJson = "[{\"name\":\"durationSeconds\",\"value\":{\"type\":\"double\",\"literal\":1.25}}]",
                    outputsJson = "[]",
                    successEdgeJson = "{\"nextStepId\":\"finish\"}",
                    failureEdgeJson = "{\"nextStepId\":\"\"}",
                    cancelEdgeJson = "{\"nextStepId\":\"\"}",
                    checkpointPolicy = "none"
                },
                new ActionSequenceStepRecord
                {
                    ordinal = 1,
                    stepId = "finish",
                    primitiveId = ActivityPrimitiveIds.Complete,
                    argumentsJson = "[]",
                    outputsJson = "[]",
                    successEdgeJson = "{\"nextStepId\":\"\"}",
                    failureEdgeJson = "{\"nextStepId\":\"\"}",
                    cancelEdgeJson = "{\"nextStepId\":\"\"}",
                    checkpointPolicy = "none"
                }
            }
        };

        ActivitySequenceBuildResult built = ActivitySequenceReleaseAdapter.Build(action,
            ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder());

        Assert.That(built.isValid, Is.True, Diagnostics(built));
        Assert.That(built.definition.id, Is.EqualTo(action.id));
        Assert.That(built.definition.revision, Is.EqualTo(action.revision));
        Assert.That(built.definition.steps.Select(item => item.stepId).ToArray(), Is.EqualTo(new[] { "pause", "finish" }));
        Assert.That(built.definition.steps[0].arguments.Single().value.literal.doubleValue, Is.EqualTo(1.25d));
        Assert.That(built.definition.steps[0].successNextStepId, Is.EqualTo("finish"));
    }

    [Test]
    public void DefaultCatalogPublishesBoundedCanonicalPrimitivesAndTrustedComposerMetadata()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();

        Assert.That(catalog.Primitives.All.Select(item => item.primitiveId).OrderBy(item => item).ToArray(),
            Is.EqualTo(new[] { "bark", "checkpoint", "complete", "face", "invoke_furniture", "move_near_player", "move_to", "reserve_nearest", "wait" }));
        ActivityPrimitiveDescriptor wait;
        Assert.That(catalog.Primitives.TryResolve(ActivityPrimitiveIds.Wait, out wait), Is.True);
        Assert.That(wait.arguments.Single().name, Is.EqualTo("durationSeconds"));
        Assert.That(wait.arguments.Single().minimum, Is.EqualTo(0d));
        Assert.That(wait.arguments.Single().maximum, Is.EqualTo(300d));
        ActivityPrimitiveDescriptor reserve;
        Assert.That(catalog.Primitives.TryResolve(ActivityPrimitiveIds.ReserveNearest, out reserve), Is.True);
        Assert.That(reserve.requiredCapabilityId, Is.EqualTo("furniture.reserve_nearest"));
        ActivityPrimitiveDescriptor move;
        Assert.That(catalog.Primitives.TryResolve(ActivityPrimitiveIds.MoveTo, out move), Is.True);
        Assert.That(move.arguments.Single().requiredProducerPrimitiveId, Is.EqualTo(ActivityPrimitiveIds.ReserveNearest));
        Assert.That(move.arguments.Single().entityKind, Is.EqualTo(ActivitySequenceEntityKinds.ReservedTarget));
        Assert.That(catalog.Composers.Single().composerId, Is.EqualTo("sequence.wait_then_complete.v1"));
        Assert.That(catalog.Composers.Single().arguments.Single().name, Is.EqualTo("durationSeconds"));
    }

    [Test]
    public void DefaultComposerValidatesTypedArgumentsAndMatchesAuthoredStepNormalization()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActivitySequenceBuilder builder = catalog.CreateBuilder();
        var source = new ActivitySequenceSource
        {
            id = "pause",
            revision = "r1",
            composerId = "sequence.wait_then_complete.v1",
            composerArguments = new List<ActivitySequenceArgument>
            {
                ActivitySequenceArgument.FromLiteral("durationSeconds", FlowValue.Double(1.25d))
            }
        };

        ActivitySequenceBuildResult composed = builder.Build(source);
        ActivitySequenceBuildResult authored = builder.Build(new ActivitySequenceSource
        {
            id = "pause",
            revision = "r1",
            steps = new List<ActivitySequenceStep>
            {
                new ActivitySequenceStep("wait", ActivityPrimitiveIds.Wait,
                    new[] { ActivitySequenceArgument.FromLiteral("durationSeconds", FlowValue.Double(1.25d)) },
                    success: "complete"),
                new ActivitySequenceStep("complete", ActivityPrimitiveIds.Complete)
            }
        });

        Assert.That(composed.isValid, Is.True, Diagnostics(composed));
        Assert.That(authored.isValid, Is.True, Diagnostics(authored));
        AssertEquivalent(composed.definition, authored.definition);
        Assert.That(composed.definition.steps[0].arguments.Single().value.literal.doubleValue, Is.EqualTo(1.25d));

        source.composerArguments[0] = ActivitySequenceArgument.FromLiteral("durationSeconds", FlowValue.Double(301d));
        Assert.That(builder.Build(source).isValid, Is.False);
        source.composerArguments[0] = ActivitySequenceArgument.FromLiteral("durationSeconds", FlowValue.Text("short"));
        Assert.That(builder.Build(source).isValid, Is.False);
        Assert.That(builder.Build(new ActivitySequenceSource
        {
            id = "mixed-source", revision = "r1", steps = new List<ActivitySequenceStep> { CompleteStep("done") },
            composerArguments = new List<ActivitySequenceArgument>
            { ActivitySequenceArgument.FromLiteral("durationSeconds", FlowValue.Double(1d)) }
        }).isValid, Is.False);
    }

    [Test]
    public void DefaultCatalogRequiresReservedOpaqueTargetBindingsAndValidFurnitureArgumentVariants()
    {
        ActivitySequenceBuilder builder = ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder();
        var reserve = new ActivitySequenceStep("reserve", ActivityPrimitiveIds.ReserveNearest,
            new[] { ActivitySequenceArgument.FromLiteral("useId", FlowValue.Text("activity.watch_tv")) },
            new[] { new ActivitySequenceResultBinding("targetToken", "tvTarget") }, success: "move");
        var safeMove = new ActivitySequenceStep("move", ActivityPrimitiveIds.MoveTo,
            new[] { ActivitySequenceArgument.FromBinding("target", "tvTarget") }, success: "finish");
        ActivitySequenceBuildResult valid = builder.Build(new ActivitySequenceSource
        {
            id = "move-to-tv", revision = "r1",
            steps = new List<ActivitySequenceStep> { reserve, safeMove, CompleteStep("finish") }
        });
        Assert.That(valid.isValid, Is.True, Diagnostics(valid));

        ActivitySequenceBuildResult literalTarget = builder.Build(new ActivitySequenceSource
        {
            id = "literal-target", revision = "r1",
            steps = new List<ActivitySequenceStep>
            {
                StepWithArgument("move", ActivityPrimitiveIds.MoveTo, "target", FlowValue.Entity("activity_target", "scene-id"))
            }
        });
        Assert.That(literalTarget.isValid, Is.False);

        var nonReservationProducer = new ActivityPrimitiveDescriptor("fixture.target", "1",
            ActivityPrimitiveOperationKind.Pure, "", new ActivityArgumentDescriptor[0],
            new[] { new ActivityResultDescriptor("targetToken", "entity",
                resultEntityKind: ActivitySequenceEntityKinds.ReservedTarget) });
        var extendedRegistry = new ActivityPrimitiveRegistry(
            ActivitySequenceCatalogFactory.CreateDefault().Primitives.All.Concat(new[] { nonReservationProducer }));
        var extendedBuilder = new ActivitySequenceBuilder(extendedRegistry, null);
        ActivitySequenceBuildResult wrongProducer = extendedBuilder.Build(new ActivitySequenceSource
        {
            id = "wrong-target-producer", revision = "r1",
            steps = new List<ActivitySequenceStep>
            {
                new ActivitySequenceStep("select", "fixture.target", outputBindings:
                    new[] { new ActivitySequenceResultBinding("targetToken", "fixtureTarget") }, success: "move"),
                new ActivitySequenceStep("move", ActivityPrimitiveIds.MoveTo,
                    new[] { ActivitySequenceArgument.FromBinding("target", "fixtureTarget") })
            }
        });
        Assert.That(wrongProducer.isValid, Is.False);

        var resultValidator = new ActivitySequenceValidator(ActivitySequenceCatalogFactory.CreateDefault().Primitives);
        Assert.That(resultValidator.ValidatePrimitiveSuccessOutputs(ActivityPrimitiveIds.ReserveNearest,
            new[] { ActivitySequenceArgument.FromLiteral("targetToken", FlowValue.Entity("furniture", "scene-id")) },
            new[] { new ActivitySequenceResultBinding("targetToken", "target") }).isValid, Is.False);

        var reserveForPower = new ActivitySequenceStep("reserve", ActivityPrimitiveIds.ReserveNearest,
            new[]
            {
                ActivitySequenceArgument.FromLiteral("useId", FlowValue.Text("activity.watch_tv")),
                ActivitySequenceArgument.FromLiteral("functionId", FlowValue.Enum("power.set"))
            }, new[] { new ActivitySequenceResultBinding("targetToken", "tvTarget") }, success: "invoke");
        var invokePower = new ActivitySequenceStep("invoke", ActivityPrimitiveIds.InvokeFurniture,
            new[]
            {
                ActivitySequenceArgument.FromBinding("target", "tvTarget"),
                ActivitySequenceArgument.FromLiteral("functionId", FlowValue.Enum("power.set")),
                ActivitySequenceArgument.FromLiteral("desiredState", FlowValue.Enum("On"))
            }, success: "finish");
        ActivitySequenceBuildResult validPower = builder.Build(new ActivitySequenceSource
        {
            id = "tv-on", revision = "r1", steps = new List<ActivitySequenceStep>
            { reserveForPower, invokePower, CompleteStep("finish") }
        });
        Assert.That(validPower.isValid, Is.True, Diagnostics(validPower));

        var invalidPower = new ActivitySequenceStep("invoke", ActivityPrimitiveIds.InvokeFurniture,
            new[]
            {
                ActivitySequenceArgument.FromBinding("target", "tvTarget"),
                ActivitySequenceArgument.FromLiteral("functionId", FlowValue.Enum("power.set"))
            }, success: "finish");
        ActivitySequenceBuildResult missingPowerState = builder.Build(new ActivitySequenceSource
        {
            id = "tv-on-missing-state", revision = "r1", steps = new List<ActivitySequenceStep>
            { reserveForPower, invalidPower, CompleteStep("finish") }
        });
        Assert.That(missingPowerState.isValid, Is.False);

        var reserveForCollection = new ActivitySequenceStep("reserve", ActivityPrimitiveIds.ReserveNearest,
            new[]
            {
                ActivitySequenceArgument.FromLiteral("useId", FlowValue.Text("dispenser.collect_mock")),
                ActivitySequenceArgument.FromLiteral("functionId", FlowValue.Enum("dispenser.collect_mock"))
            }, new[] { new ActivitySequenceResultBinding("targetToken", "dispenserTarget") }, success: "invoke");
        var invalidCollection = new ActivitySequenceStep("invoke", ActivityPrimitiveIds.InvokeFurniture,
            new[]
            {
                ActivitySequenceArgument.FromBinding("target", "dispenserTarget"),
                ActivitySequenceArgument.FromLiteral("functionId", FlowValue.Enum("dispenser.collect_mock")),
                ActivitySequenceArgument.FromLiteral("desiredState", FlowValue.Enum("On"))
            }, success: "finish");
        Assert.That(builder.Build(new ActivitySequenceSource
        {
            id = "collection-with-state", revision = "r1", steps = new List<ActivitySequenceStep>
            { reserveForCollection, invalidCollection, CompleteStep("finish") }
        }).isValid, Is.False);

        ActivitySequenceBuildResult arbitraryCheckpointEffect = builder.Build(new ActivitySequenceSource
        {
            id = "checkpoint-effect", revision = "r1",
            steps = new List<ActivitySequenceStep>
            {
                new ActivitySequenceStep("checkpoint", ActivityPrimitiveIds.Checkpoint,
                    new[]
                    {
                        ActivitySequenceArgument.FromLiteral("checkpointId", FlowValue.Text("sip_complete")),
                        ActivitySequenceArgument.FromLiteral("statDelta", FlowValue.Int64(100))
                    })
            }
        });
        Assert.That(arbitraryCheckpointEffect.isValid, Is.False);
    }

    [Test]
    public void SequenceBuilderRequiresExactlyOneSourceAndNormalizesComposerAndData()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { CompletePrimitive() });
        var source = DataSource(Sequence("proof", CompleteStep("end")));
        var composerSource = new ActivitySequenceSource
        {
            id = "proof",
            revision = "r1",
            composerId = "fixture.proof"
        };
        var builder = new ActivitySequenceBuilder(registry, new[] { new ProofComposer() });

        ActivitySequenceBuildResult fromData = builder.Build(source);
        ActivitySequenceBuildResult fromComposer = builder.Build(composerSource);

        Assert.That(fromData.isValid, Is.True, Diagnostics(fromData));
        Assert.That(fromComposer.isValid, Is.True, Diagnostics(fromComposer));
        AssertEquivalent(fromData.definition, fromComposer.definition);
        Assert.That(builder.Build(new ActivitySequenceSource
        {
            id = "ambiguous", revision = "r1", composerId = "fixture.proof",
            steps = new List<ActivitySequenceStep> { CompleteStep("end") }
        }).isValid, Is.False);
        Assert.That(builder.Build(new ActivitySequenceSource { id = "empty", revision = "r1" }).isValid,
            Is.False);
        Assert.That(builder.Build(new ActivitySequenceSource
        {
            id = "empty-and-composer", revision = "r1", composerId = "fixture.proof",
            steps = new List<ActivitySequenceStep>()
        }).isValid, Is.False);
    }

    [Test]
    public void SequenceBuilderReturnsAnImmutableSnapshotDetachedFromItsSource()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { CompletePrimitive() });
        var source = DataSource(Sequence("snapshot", CompleteStep("done")));
        ActivitySequenceBuildResult built = new ActivitySequenceBuilder(registry, null).Build(source);

        source.steps.Clear();

        Assert.That(built.isValid, Is.True, Diagnostics(built));
        Assert.That(built.definition.steps.Count, Is.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => ((IList<ActivitySequenceStep>)built.definition.steps).Add(CompleteStep("mutate")));
        Assert.That(typeof(ActivitySequenceStep).GetField("primitiveId").IsInitOnly, Is.True);
    }

    [Test]
    public void SequenceRegistryAndValidatorRejectDuplicateUnknownAndMalformedPrimitives()
    {
        Assert.Throws<ArgumentException>(() => new ActivityPrimitiveRegistry(new[]
        {
            CompletePrimitive(), CompletePrimitive()
        }));
        Assert.Throws<ArgumentException>(() => new ActivityPrimitiveDescriptor("wait", "1",
            ActivityPrimitiveOperationKind.Pure, "",
            new[] { new ActivityArgumentDescriptor("durationSeconds", "double", true, 0d, 301d) },
            new ActivityResultDescriptor[0]));

        var registry = new ActivityPrimitiveRegistry(new[]
        {
            new ActivityPrimitiveDescriptor("wait", "1", ActivityPrimitiveOperationKind.Pure,
                "", new[] { new ActivityArgumentDescriptor("durationSeconds", "double", true, 0d, 300d) },
                new ActivityResultDescriptor[0]),
            CompletePrimitive()
        });
        var validator = new ActivitySequenceValidator(registry);

        Assert.That(validator.Validate(Sequence("unknown", Step("x", "primitive.not_registered"))).isValid,
            Is.False);
        Assert.That(validator.Validate(Sequence("duplicate", CompleteStep("same"), CompleteStep("same"))).isValid,
            Is.False);
        Assert.That(validator.Validate(Sequence("missing-id", CompleteStep(""))).isValid, Is.False);
        Assert.That(validator.Validate(Sequence("missing-argument", Step("wait", "wait"))).isValid,
            Is.False);
        Assert.That(validator.Validate(Sequence("unknown-argument", StepWithArgument("wait", "wait", "durationSeconds",
            FlowValue.Double(1d), "unexpected"))).isValid, Is.False);
        Assert.That(validator.Validate(Sequence("type-mismatch", StepWithArgument("wait", "wait", "durationSeconds",
            FlowValue.Text("fast")))).isValid, Is.False);
        Assert.That(validator.Validate(Sequence("too-long", StepWithArgument("wait", "wait", "durationSeconds",
            FlowValue.Double(301d)))).isValid, Is.False);
    }

    [Test]
    public void SequenceValidatorChecksTypedResultBindingsAndForwardOnlyEdges()
    {
        var registry = new ActivityPrimitiveRegistry(new[]
        {
            new ActivityPrimitiveDescriptor("find", "1", ActivityPrimitiveOperationKind.Pure,
                "", new ActivityArgumentDescriptor[0],
                new[] { new ActivityResultDescriptor("target", "entity") }),
            new ActivityPrimitiveDescriptor("use", "1", ActivityPrimitiveOperationKind.Pure,
                "", new[] { new ActivityArgumentDescriptor("target", "entity", true) },
                new ActivityResultDescriptor[0]),
            CompletePrimitive()
        });
        var validator = new ActivitySequenceValidator(registry);
        var find = new ActivitySequenceStep("find", "find", null,
            new[] { new ActivitySequenceResultBinding("target", "selectedTarget") }, success: "use");
        var use = new ActivitySequenceStep("use", "use",
            new[] { ActivitySequenceArgument.FromBinding("target", "selectedTarget") }, success: "done");

        Assert.That(validator.Validate(Sequence("bound", find, use, CompleteStep("done"))).isValid,
            Is.True);
        Assert.That(validator.Validate(Sequence("forward-only",
            new ActivitySequenceStep("first", "find", success: "first"))).isValid, Is.False);
        Assert.That(validator.Validate(Sequence("missing-binding", new ActivitySequenceStep("use", "use",
            new[] { ActivitySequenceArgument.FromBinding("target", "notProduced") }))).isValid, Is.False);
        Assert.That(validator.Validate(Sequence("binding-type-mismatch", find,
            new ActivitySequenceStep("use", "use",
                new[] { ActivitySequenceArgument.FromLiteral("target", FlowValue.Text("not an entity")) }))).isValid,
            Is.False);
    }

    [Test]
    public void SequenceValidatorRejectsBindingWhenAnIncomingBranchSkipsItsProducer()
    {
        var registry = new ActivityPrimitiveRegistry(new[]
        {
            new ActivityPrimitiveDescriptor("branch", "1", ActivityPrimitiveOperationKind.Pure,
                "", new ActivityArgumentDescriptor[0], new ActivityResultDescriptor[0]),
            new ActivityPrimitiveDescriptor("produce", "1", ActivityPrimitiveOperationKind.Pure,
                "", new ActivityArgumentDescriptor[0], new[] { new ActivityResultDescriptor("seconds", "double", 0d, 300d) }),
            new ActivityPrimitiveDescriptor("wait", "1", ActivityPrimitiveOperationKind.Pure,
                "", new[] { new ActivityArgumentDescriptor("durationSeconds", "double", true, 0d, 300d) },
                new ActivityResultDescriptor[0])
        });
        var validator = new ActivitySequenceValidator(registry);
        var entry = new ActivitySequenceStep("entry", "branch", success: "produce", failure: "consume");
        var producer = new ActivitySequenceStep("produce", "produce",
            outputBindings: new[] { new ActivitySequenceResultBinding("seconds", "seconds") }, success: "consume");
        var consumer = new ActivitySequenceStep("consume", "wait",
            new[] { ActivitySequenceArgument.FromBinding("durationSeconds", "seconds") });

        Assert.That(validator.Validate(Sequence("branch-skip", entry, producer, consumer)).isValid, Is.False);
    }

    [Test]
    public void BoundWaitDurationsRequireProvenRangeAndRuntimeOutputValidation()
    {
        var boundedProducer = new ActivityPrimitiveDescriptor("produce", "1", ActivityPrimitiveOperationKind.Pure,
            "", new ActivityArgumentDescriptor[0], new[] { new ActivityResultDescriptor("seconds", "double", 0d, 300d) });
        var unboundedProducer = new ActivityPrimitiveDescriptor("produce-too-long", "1", ActivityPrimitiveOperationKind.Pure,
            "", new ActivityArgumentDescriptor[0], new[] { new ActivityResultDescriptor("seconds", "double", 0d, 301d) });
        var wait = new ActivityPrimitiveDescriptor("wait", "1", ActivityPrimitiveOperationKind.Pure,
            "", new[] { new ActivityArgumentDescriptor("durationSeconds", "double", true, 0d, 300d) },
            new ActivityResultDescriptor[0]);
        var validator = new ActivitySequenceValidator(new ActivityPrimitiveRegistry(new[]
        {
            boundedProducer, unboundedProducer, wait
        }));
        var step = new ActivitySequenceStep("wait", "wait",
            new[] { ActivitySequenceArgument.FromBinding("durationSeconds", "seconds") });

        Assert.That(validator.Validate(Sequence("bound-0-300",
            new ActivitySequenceStep("produce", "produce",
                outputBindings: new[] { new ActivitySequenceResultBinding("seconds", "seconds") }, success: "wait"),
            step)).isValid, Is.True);
        Assert.That(validator.Validate(Sequence("bound-301",
            new ActivitySequenceStep("produce", "produce-too-long",
                outputBindings: new[] { new ActivitySequenceResultBinding("seconds", "seconds") }, success: "wait"),
            step)).isValid, Is.False);

        var required = new[] { new ActivitySequenceResultBinding("seconds", "seconds") };
        Assert.That(validator.ValidatePrimitiveSuccessOutputs("produce", new[]
        {
            ActivitySequenceArgument.FromLiteral("seconds", FlowValue.Double(0d))
        }, required).isValid, Is.True);
        Assert.That(validator.ValidatePrimitiveSuccessOutputs("produce", new[]
        {
            ActivitySequenceArgument.FromLiteral("seconds", FlowValue.Double(300d))
        }, required).isValid, Is.True);
        Assert.That(validator.ValidatePrimitiveSuccessOutputs("produce", new[]
        {
            ActivitySequenceArgument.FromLiteral("seconds", FlowValue.Double(301d))
        }, required).isValid, Is.False);
    }

    [Test]
    public void SuccessfulPrimitiveOutputValidationRequiresSelectedResultsButPartialStatusesDoNot()
    {
        var producer = new ActivityPrimitiveDescriptor("produce", "1", ActivityPrimitiveOperationKind.Pure,
            "", new ActivityArgumentDescriptor[0],
            new[] { new ActivityResultDescriptor("seconds", "double", 0d, 300d) });
        var validator = new ActivitySequenceValidator(new ActivityPrimitiveRegistry(new[] { producer }));
        var selected = new[] { new ActivitySequenceResultBinding("seconds", "seconds") };

        Assert.That(validator.ValidatePrimitiveOutputs("produce", new ActivitySequenceArgument[0]).isValid,
            Is.True, "An accepted/running primitive may not have produced its selected result yet.");
        Assert.That(validator.ValidatePrimitiveSuccessOutputs("produce", new ActivitySequenceArgument[0], selected).isValid,
            Is.False, "Completed success must provide every result selected for binding.");
        Assert.That(validator.ValidatePrimitiveSuccessOutputs("produce", new[]
        {
            ActivitySequenceArgument.FromLiteral("seconds", FlowValue.Double(42d))
        }, selected).isValid, Is.True);
    }

    [Test]
    public void SequenceValidatorBoundsStepsAndRejectsBackwardTransitions()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { CompletePrimitive() });
        var validator = new ActivitySequenceValidator(registry);
        var tooMany = new List<ActivitySequenceStep>();
        for (int index = 0; index < 65; index++) tooMany.Add(CompleteStep("step" + index));

        Assert.That(validator.Validate(Sequence("too-many", tooMany)).isValid, Is.False);
        Assert.That(validator.Validate(Sequence("back-edge",
            new ActivitySequenceStep("first", "complete", success: "second"),
            new ActivitySequenceStep("second", "complete", failure: "first"))).isValid,
            Is.False);
        Assert.That(validator.Validate(Sequence("unreachable",
            CompleteStep("first"), CompleteStep("orphan"))).isValid, Is.False);
    }

    [Test]
    public void ExistingActivityExecutorEnumValuesAndLifecycleContractRemainStable()
    {
        Assert.That((int)ActivityExecutorKind.Movement, Is.EqualTo(0));
        Assert.That((int)ActivityExecutorKind.DrinkWater, Is.EqualTo(1));
        Assert.That(typeof(IActivityExecution).GetMethod("Start"), Is.Not.Null);
        Assert.That(typeof(IActivityExecution).GetMethod("Tick"), Is.Not.Null);
        Assert.That(typeof(IActivityExecution).GetMethod("Shutdown"), Is.Not.Null);
    }

    [Test]
    public void HostEffectRequestUsesFlowInvocationAndCompletedStepWithoutSeparateItemMutationFields()
    {
        var actionRef = new ContentRef("content", "grant-water", "r3", "abc");
        var request = new ScenarioHostEffectRequest(actionRef, "run-1", "flow-inv-4", "step-exec-9",
            "delivery", "delivery-exec-8");

        Assert.That(request.actionRef.id, Is.EqualTo("grant-water"));
        Assert.That(request.scenarioRunId, Is.EqualTo("run-1"));
        Assert.That(request.invocationId, Is.EqualTo("flow-inv-4"));
        Assert.That(request.stepExecutionId, Is.EqualTo("step-exec-9"));
        Assert.That(request.requiresCompletedStepId, Is.EqualTo("delivery"));
        Assert.That(request.completedStepExecutionId, Is.EqualTo("delivery-exec-8"));
        Assert.That(typeof(ScenarioHostEffectRequest).GetField("itemRef"), Is.Null);
        Assert.That(typeof(ScenarioHostEffectRequest).GetField("quantity"), Is.Null);
        Assert.That(typeof(ScenarioHostEffectRequest).GetField("actionInvocationId"), Is.Null);
        Assert.That(typeof(IScenarioHostEffectPort).GetMethod("Apply").ReturnType,
            Is.EqualTo(typeof(ScenarioHostEffectResult)));
        Assert.That(ScenarioHostEffectStatuses.Applied, Is.EqualTo("applied"));
        Assert.That(ScenarioHostEffectStatuses.Replayed, Is.EqualTo("replayed"));

        var applied = new ScenarioHostEffectResult("applied", "run-1", "flow-inv-4",
            "step-exec-9", "op-1", "receipt-1", 8L, "");
        Assert.That(applied.invocationId, Is.EqualTo("flow-inv-4"));
        Assert.That(applied.stepExecutionId, Is.EqualTo("step-exec-9"));
        Assert.Throws<ArgumentException>(() => new ScenarioHostEffectResult("success", "run-1",
            "flow-inv-4", "step-exec-9", "op-1", "receipt-1", 8L, ""));
        Assert.Throws<ArgumentException>(() => new ScenarioHostEffectResult("applied", "run-1",
            "flow-inv-4", "step-exec-9", "", "", 8L, ""));
    }

    [Test]
    public void PrimitiveAndCancellationContractsKeepFlowAndPhysicalActionIdentityDistinct()
    {
        var identity = new ActivityPrimitiveIdentity("run-1", "flow-inv-4", "flow-step-2",
            "activity-7", "sequence-step-3", "presentation-request-5");
        var cancel = new ActivitySequenceCancellation("run-1", "flow-inv-4", "flow-step-2",
            "activity-7", "requested");
        var cancelMethod = typeof(IActivitySequenceRunner).GetMethod("Cancel");

        Assert.That(identity.flowInvocationId, Is.EqualTo("flow-inv-4"));
        Assert.That(identity.actionInvocationId, Is.EqualTo("activity-7"));
        Assert.That(identity.stepExecutionId, Is.EqualTo("flow-step-2"));
        Assert.That(cancel.flowInvocationId, Is.EqualTo("flow-inv-4"));
        Assert.That(cancel.actionInvocationId, Is.EqualTo("activity-7"));
        Assert.That(cancelMethod.GetParameters().Length, Is.EqualTo(1));
        Assert.That(cancelMethod.GetParameters()[0].ParameterType, Is.EqualTo(typeof(ActivitySequenceCancellation)));
    }

    [Test]
    public void SequenceExecutorResolvesBindingsAndBoundsSynchronousWorkPerTick()
    {
        var registry = new ActivityPrimitiveRegistry(new[]
        {
            Primitive("produce", results: new[] { new ActivityResultDescriptor("value", "text") }),
            Primitive("consume", arguments: new[] { new ActivityArgumentDescriptor("text", "text", true) }),
            CompletePrimitive()
        });
        var steps = new List<ActivitySequenceStep>
        {
            new ActivitySequenceStep("produce-step", "produce",
                outputBindings: new[] { new ActivitySequenceResultBinding("value", "message") }, success: "consume-step"),
            new ActivitySequenceStep("consume-step", "consume",
                new[] { ActivitySequenceArgument.FromBinding("text", "message") }, success: "step-0")
        };
        for (int index = 0; index < 16; index++)
        {
            string id = "step-" + index;
            string next = index == 15 ? "finish-step" : "step-" + (index + 1);
            steps.Add(new ActivitySequenceStep(id, "complete", success: next));
        }
        steps.Add(new ActivitySequenceStep("finish-step", "complete"));
        var port = new FakeActivityPrimitivePort(request =>
        {
            IEnumerable<ActivitySequenceArgument> outputs = request.primitiveId == "produce"
                ? new[] { ActivitySequenceArgument.FromLiteral("value", FlowValue.Text("hello")) }
                : new ActivitySequenceArgument[0];
            return StartResult(request, ActivitySequenceStatuses.Completed, outputs);
        });
        var runner = new ActivitySequenceExecutor(registry, port);
        Assert.That(runner.Start(SequenceRequest(Sequence("executor-flow", steps), "action-1")).status,
            Is.EqualTo(ActivitySequenceStatuses.Accepted));

        ActivitySequenceView view = null;
        for (int tick = 0; tick < 20; tick++)
        {
            int before = port.starts.Count;
            view = runner.Tick(tick);
            Assert.That(port.starts.Count - before, Is.LessThanOrEqualTo(ActivitySequenceExecutor.MaximumOperationsPerTick));
            if (view.status == ActivitySequenceStatuses.Completed) break;
        }

        Assert.That(view.status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        ActivityPrimitiveRequest consume = port.starts.Single(item => item.primitiveId == "consume");
        Assert.That(consume.arguments.Single().value.literal.textValue, Is.EqualTo("hello"));
        Assert.That(consume.identity.scenarioRunId, Is.EqualTo("run-1"));
        Assert.That(consume.identity.flowInvocationId, Is.EqualTo("flow-1"));
        Assert.That(consume.identity.stepExecutionId, Is.EqualTo("flow-step-1"));
        Assert.That(consume.identity.actionInvocationId, Is.EqualTo("action-1"));
        Assert.That(consume.identity.sequenceStepId, Is.EqualTo("consume-step"));
    }

    [Test]
    public void SequenceExecutorPassesReservationProducedTargetTokenWithoutSceneSnapshotEntry()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActivitySequenceBuildResult built = catalog.CreateBuilder().Build(new ActivitySequenceSource
        {
            id = "reserved-target-flow",
            revision = "r1",
            steps = new List<ActivitySequenceStep>
            {
                new ActivitySequenceStep("reserve", ActivityPrimitiveIds.ReserveNearest,
                    new[] { ActivitySequenceArgument.FromLiteral("useId", FlowValue.Text("activity.watch_tv")) },
                    new[] { new ActivitySequenceResultBinding("targetToken", "selectedTarget") },
                    success: "move"),
                new ActivitySequenceStep("move", ActivityPrimitiveIds.MoveTo,
                    new[] { ActivitySequenceArgument.FromBinding("target", "selectedTarget") })
            }
        });
        Assert.That(built.isValid, Is.True, Diagnostics(built));

        var context = new ScenarioRuntimeSnapshot("scenario", "run-1", "turn-1", 1L, "",
            new string[0], new string[0],
            new[]
            {
                new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.ReserveNearest,
                    new[] { new ScenarioArgumentBinding("useId", new[] { "activity.watch_tv" }) })
            },
            new ScenarioObjectSnapshot[0], new string[0]);
        const string reservationToken = "reservation-token-1";
        var port = new FakeActivityPrimitivePort(request => request.primitiveId == ActivityPrimitiveIds.ReserveNearest
            ? StartResult(request, ActivitySequenceStatuses.Completed,
                new[] { ActivitySequenceArgument.FromLiteral("targetToken",
                    FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, reservationToken)) })
            : StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);

        Assert.That(runner.Start(SequenceRequest(built.definition, "action-reserved-target", context: context)).status,
            Is.EqualTo(ActivitySequenceStatuses.Accepted));
        ActivitySequenceView view = runner.Tick(0d);

        Assert.That(view.status, Is.EqualTo(ActivitySequenceStatuses.Completed), view.reasonCode);
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[]
        { ActivityPrimitiveIds.ReserveNearest, ActivityPrimitiveIds.MoveTo }));
        Assert.That(port.starts[1].arguments.Single().value.literal.entityValue.id, Is.EqualTo(reservationToken));
        Assert.That(context.objects.Count, Is.EqualTo(0));
    }

    [Test]
    public void SequenceExecutorRejectsLiteralTargetBeforePrimitiveDispatch()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);
        var definition = Sequence("literal-target-flow",
            StepWithArgument("move", ActivityPrimitiveIds.MoveTo, "target",
                FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, "literal-target-1")));

        ActivitySequenceStartResult result = runner.Start(SequenceRequest(definition, "action-literal-target"));

        Assert.That(result.status, Is.EqualTo(ActivitySequenceStatuses.Failed));
        Assert.That(port.starts.Count, Is.EqualTo(0));
    }

    [Test]
    public void SequenceExecutorRejectsUnknownTargetBindingBeforePrimitiveDispatch()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);
        var definition = Sequence("unknown-target-flow",
            new ActivitySequenceStep("move", ActivityPrimitiveIds.MoveTo,
                new[] { ActivitySequenceArgument.FromBinding("target", "notProduced") }));

        ActivitySequenceStartResult result = runner.Start(SequenceRequest(definition, "action-unknown-target"));

        Assert.That(result.status, Is.EqualTo(ActivitySequenceStatuses.Failed));
        Assert.That(port.starts.Count, Is.EqualTo(0));
    }

    [Test]
    public void SequenceExecutorRejectsTargetBindingFromOtherProducerBeforePrimitiveDispatch()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        var otherTargetProducer = new ActivityPrimitiveDescriptor("fixture.target", "1",
            ActivityPrimitiveOperationKind.Pure, "", new ActivityArgumentDescriptor[0],
            new[] { new ActivityResultDescriptor("targetToken", "entity",
                resultEntityKind: ActivitySequenceEntityKinds.ReservedTarget) });
        var registry = new ActivityPrimitiveRegistry(catalog.Primitives.All.Concat(new[] { otherTargetProducer }));
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed,
            new[] { ActivitySequenceArgument.FromLiteral("targetToken",
                FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, "foreign-token-1")) }));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("wrong-target-producer-flow",
            new ActivitySequenceStep("select", "fixture.target",
                outputBindings: new[] { new ActivitySequenceResultBinding("targetToken", "foreignTarget") }, success: "move"),
            new ActivitySequenceStep("move", ActivityPrimitiveIds.MoveTo,
                new[] { ActivitySequenceArgument.FromBinding("target", "foreignTarget") }));

        ActivitySequenceStartResult result = runner.Start(SequenceRequest(definition, "action-wrong-target-producer"));

        Assert.That(result.status, Is.EqualTo(ActivitySequenceStatuses.Failed));
        Assert.That(port.starts.Count, Is.EqualTo(0));
    }

    [Test]
    public void SequenceExecutorDoesNotDispatchWrongKindReservationOutput()
    {
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        ActivitySequenceBuildResult built = catalog.CreateBuilder().Build(new ActivitySequenceSource
        {
            id = "wrong-kind-reservation-flow", revision = "r1",
            steps = new List<ActivitySequenceStep>
            {
                new ActivitySequenceStep("reserve", ActivityPrimitiveIds.ReserveNearest,
                    new[] { ActivitySequenceArgument.FromLiteral("useId", FlowValue.Text("activity.watch_tv")) },
                    new[] { new ActivitySequenceResultBinding("targetToken", "selectedTarget") }, success: "move"),
                new ActivitySequenceStep("move", ActivityPrimitiveIds.MoveTo,
                    new[] { ActivitySequenceArgument.FromBinding("target", "selectedTarget") })
            }
        });
        Assert.That(built.isValid, Is.True, Diagnostics(built));
        var context = new ScenarioRuntimeSnapshot("scenario", "run-1", "turn-1", 1L, "",
            new string[0], new string[0],
            new[]
            {
                new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.ReserveNearest,
                    new[] { new ScenarioArgumentBinding("useId", new[] { "activity.watch_tv" }) })
            }, new ScenarioObjectSnapshot[0], new string[0]);
        var port = new FakeActivityPrimitivePort(request => request.primitiveId == ActivityPrimitiveIds.ReserveNearest
            ? StartResult(request, ActivitySequenceStatuses.Completed,
                new[] { ActivitySequenceArgument.FromLiteral("targetToken", FlowValue.Entity("furniture", "scene-id")) })
            : StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(catalog.Primitives, port);

        runner.Start(SequenceRequest(built.definition, "action-wrong-kind", context: context));
        ActivitySequenceView view = runner.Tick(0d);

        Assert.That(view.status, Is.EqualTo(ActivitySequenceStatuses.Failed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[]
        { ActivityPrimitiveIds.ReserveNearest }));
    }

    [Test]
    public void SequenceExecutorSubstitutesValidatedScenarioInputsBeforePrimitiveDispatch()
    {
        var registry = new ActivityPrimitiveRegistry(new[]
        {
            Primitive("consume", arguments: new[] { new ActivityArgumentDescriptor("target", "text", true) })
        });
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("input-flow", new ActivitySequenceStep("use-input", "consume",
            new[] { ActivitySequenceArgument.FromBinding("target", "targetInput") }));
        var request = SequenceRequest(definition, "action-input",
            new[] { ActivitySequenceArgument.FromLiteral("targetInput", FlowValue.Text("player")) });

        Assert.That(runner.Start(request).status, Is.EqualTo(ActivitySequenceStatuses.Accepted));
        Assert.That(port.starts.Single().arguments.Single().value.literal.textValue, Is.EqualTo("player"));
    }

    [Test]
    public void SequenceExecutorDoesNotBindValuesFromAcceptedOrRunningStartAcknowledgements()
    {
        var registry = new ActivityPrimitiveRegistry(new[]
        {
            Primitive("async-produce", results: new[] { new ActivityResultDescriptor("value", "text") }),
            Primitive("consume", arguments: new[] { new ActivityArgumentDescriptor("text", "text", true) })
        });
        var port = new FakeActivityPrimitivePort(request => request.primitiveId == "async-produce"
            ? StartResult(request, ActivitySequenceStatuses.Running,
                new[] { ActivitySequenceArgument.FromLiteral("value", FlowValue.Text("partial")) })
            : StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("partial-output-flow",
            new ActivitySequenceStep("produce", "async-produce",
                outputBindings: new[] { new ActivitySequenceResultBinding("value", "message") }, success: "consume"),
            new ActivitySequenceStep("consume", "consume",
                new[] { ActivitySequenceArgument.FromBinding("text", "message") }));
        runner.Start(SequenceRequest(definition, "action-partial"));
        Assert.That(port.starts.Count, Is.EqualTo(1));

        runner.CompletePrimitive(new ActivityPrimitiveCompletion(port.starts[0].identity,
            ActivitySequenceStatuses.Completed,
            new[] { ActivitySequenceArgument.FromLiteral("value", FlowValue.Text("final")) }, ""));
        runner.Tick(1d);

        Assert.That(port.starts.Single(item => item.primitiveId == "consume")
            .arguments.Single().value.literal.textValue, Is.EqualTo("final"));
    }

    [Test]
    public void SequenceExecutorRejectsStaleCallbacksAndMakesStartReplayIdempotent()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { Primitive("async"), CompletePrimitive() });
        var port = new FakeActivityPrimitivePort(request => request.primitiveId == "async"
            ? StartResult(request, ActivitySequenceStatuses.Running)
            : StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("async-flow",
            new ActivitySequenceStep("work", "async", success: "done"), CompleteStep("done"));
        var request = SequenceRequest(definition, "action-async");
        Assert.That(runner.Start(request).status, Is.EqualTo(ActivitySequenceStatuses.Accepted));
        var replay = SequenceRequest(definition, "action-async", context: request.context);
        Assert.That(runner.Start(replay).status, Is.EqualTo(ActivitySequenceStatuses.Running));
        Assert.That(port.starts.Count, Is.EqualTo(1));

        var conflicting = SequenceRequest(Sequence("async-flow", CompleteStep("different")), "action-async");
        Assert.That(runner.Start(conflicting).reasonCode, Is.EqualTo("sequence_identity_conflict"));
        ActivityPrimitiveIdentity active = port.starts[0].identity;
        var stale = new ActivityPrimitiveCompletion(new ActivityPrimitiveIdentity("run-1", "flow-1",
            "flow-step-1", "action-async", "work", "wrong-invocation"), ActivitySequenceStatuses.Completed,
            new ActivitySequenceArgument[0], "");
        Assert.That(runner.CompletePrimitive(stale).status, Is.EqualTo(ActivitySequenceStatuses.Running));

        var completion = new ActivityPrimitiveCompletion(active, ActivitySequenceStatuses.Completed,
            new ActivitySequenceArgument[0], "");
        Assert.That(runner.CompletePrimitive(completion).status, Is.EqualTo(ActivitySequenceStatuses.Running));
        runner.CompletePrimitive(completion);
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Count(item => item.primitiveId == "complete"), Is.EqualTo(1));
    }

    [Test]
    public void SequenceExecutorDoesNotReplaceSynchronousOutcomeWithConflictingCallback()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { Primitive("sync"), Primitive("success"), Primitive("failure") });
        var port = new FakeActivityPrimitivePort(request =>
            StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("sync-race-flow",
            new ActivitySequenceStep("work", "sync", success: "success-step", failure: "failure-step"),
            new ActivitySequenceStep("success-step", "success"),
            new ActivitySequenceStep("failure-step", "failure"));
        runner.Start(SequenceRequest(definition, "action-sync-race"));
        ActivityPrimitiveIdentity identity = port.starts[0].identity;

        runner.CompletePrimitive(new ActivityPrimitiveCompletion(identity, ActivitySequenceStatuses.Failed,
            new ActivitySequenceArgument[0], "conflicting"));
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[] { "sync", "success" }));
    }

    [Test]
    public void SequenceExecutorRoutesFailureAndCancellationOnlyAlongTheirDeclaredEdges()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { Primitive("begin"), Primitive("failure"), Primitive("cancel") });
        var port = new FakeActivityPrimitivePort(request => request.primitiveId == "begin"
            ? StartResult(request, ActivitySequenceStatuses.Failed)
            : StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("failure-flow",
            new ActivitySequenceStep("begin-step", "begin", success: "success-step", failure: "failure-step"),
            new ActivitySequenceStep("success-step", "failure"),
            new ActivitySequenceStep("failure-step", "failure"));

        runner.Start(SequenceRequest(definition, "action-failure"));
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[] { "begin", "failure" }));
    }

    [Test]
    public void SequenceExecutorRoutesCancellationToCancelEdge()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { Primitive("async"), Primitive("succeed"), Primitive("cancel") });
        var port = new FakeActivityPrimitivePort(
            request => request.primitiveId == "async"
                ? StartResult(request, ActivitySequenceStatuses.Running)
                : StartResult(request, ActivitySequenceStatuses.Completed),
            cancellation => new ActivityPrimitiveCompletion(cancellation.identity,
                ActivitySequenceStatuses.Cancelled, new ActivitySequenceArgument[0], "requested"));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("cancel-edge-flow",
            new ActivitySequenceStep("work", "async", success: "success-step", cancel: "cancel-step"),
            new ActivitySequenceStep("success-step", "succeed"),
            new ActivitySequenceStep("cancel-step", "cancel"));
        runner.Start(SequenceRequest(definition, "action-cancel-edge"));

        Assert.That(runner.Cancel(new ActivitySequenceCancellation("run-1", "flow-1", "flow-step-1",
            "action-cancel-edge", "requested")).status, Is.EqualTo(ActivitySequenceStatuses.Running));
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[] { "async", "cancel" }));
    }

    [Test]
    public void SequenceExecutorDoesNotPublishBindingsOrTakeSuccessEdgeWhenCompletedOutputIsMissing()
    {
        var registry = new ActivityPrimitiveRegistry(new[]
        {
            Primitive("produce", results: new[] { new ActivityResultDescriptor("value", "text") }),
            Primitive("consume", arguments: new[] { new ActivityArgumentDescriptor("text", "text", true) }),
            Primitive("fallback")
        });
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("missing-output-flow",
            new ActivitySequenceStep("produce-step", "produce",
                outputBindings: new[] { new ActivitySequenceResultBinding("value", "missing") },
                success: "consume-step", failure: "fallback-step"),
            new ActivitySequenceStep("consume-step", "consume",
                new[] { ActivitySequenceArgument.FromBinding("text", "missing") }),
            new ActivitySequenceStep("fallback-step", "fallback"));

        runner.Start(SequenceRequest(definition, "action-missing-output"));
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[] { "produce", "fallback" }));
    }

    [Test]
    public void SequenceExecutorRequiresSnapshotCapabilityAndAllowedArgumentsBeforeDispatch()
    {
        var guarded = new ActivityPrimitiveDescriptor("guarded", "1", ActivityPrimitiveOperationKind.Pure,
            "device.control", new[] { new ActivityArgumentDescriptor("target", "text", true) },
            new ActivityResultDescriptor[0]);
        var registry = new ActivityPrimitiveRegistry(new[] { guarded, Primitive("fallback") });
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        var context = new ScenarioRuntimeSnapshot("scenario", "run-1", "turn-1", 1L, "",
            new string[0], new string[0],
            new[] { new ScenarioCapabilitySnapshot("device.control",
                new[] { new ScenarioArgumentBinding("target", new[] { "tv" }) }) },
            new ScenarioObjectSnapshot[0], new string[0]);
        var definition = Sequence("capability-flow",
            new ActivitySequenceStep("try-device", "guarded",
                new[] { ActivitySequenceArgument.FromLiteral("target", FlowValue.Text("speaker")) },
                failure: "fallback-step"),
            new ActivitySequenceStep("fallback-step", "fallback"));

        runner.Start(SequenceRequest(definition, "action-capability", context: context));
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.starts.Select(item => item.primitiveId).ToArray(), Is.EqualTo(new[] { "fallback" }));
    }

    [Test]
    public void SequenceExecutorCancelsOutstandingPrimitiveOnceAndIgnoresLateCompletion()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { Primitive("async"), Primitive("cancel") });
        var port = new FakeActivityPrimitivePort(
            request => StartResult(request, ActivitySequenceStatuses.Running),
            cancellation => new ActivityPrimitiveCompletion(cancellation.identity,
                ActivitySequenceStatuses.Running, new ActivitySequenceArgument[0], ""));
        var runner = new ActivitySequenceExecutor(registry, port);
        runner.Start(SequenceRequest(Sequence("cancel-flow",
            new ActivitySequenceStep("work", "async", cancel: "cancel-step"),
            new ActivitySequenceStep("cancel-step", "cancel")), "action-cancel"));
        var cancellation = new ActivitySequenceCancellation("run-1", "flow-1", "flow-step-1", "action-cancel", "stop");

        Assert.That(runner.Cancel(cancellation).status, Is.EqualTo(ActivitySequenceStatuses.Running));
        runner.Cancel(cancellation);
        Assert.That(port.cancelCalls, Is.EqualTo(1));
        Assert.That(runner.Shutdown("shutdown").status, Is.EqualTo(ActivitySequenceStatuses.Cancelled));
        Assert.That(port.cancelCalls, Is.EqualTo(1));
        var late = new ActivityPrimitiveCompletion(port.starts[0].identity, ActivitySequenceStatuses.Completed,
            new ActivitySequenceArgument[0], "");
        Assert.That(runner.CompletePrimitive(late).status, Is.EqualTo(ActivitySequenceStatuses.Cancelled));
        Assert.That(port.starts.Count(item => item.primitiveId == "cancel"), Is.EqualTo(0));
    }

    [Test]
    public void SequenceExecutorShutdownBeforeStartPermanentlyClosesThatInvocation()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { CompletePrimitive() });
        var port = new FakeActivityPrimitivePort(request => StartResult(request, ActivitySequenceStatuses.Completed));
        var runner = new ActivitySequenceExecutor(registry, port);
        runner.Shutdown("owner_destroyed");

        ActivitySequenceStartResult start = runner.Start(SequenceRequest(
            Sequence("shutdown-flow", CompleteStep("end")), "action-shutdown"));
        Assert.That(start.status, Is.EqualTo(ActivitySequenceStatuses.Failed));
        Assert.That(start.reasonCode, Is.EqualTo("sequence_instance_shutdown"));
        Assert.That(port.starts.Count, Is.EqualTo(0));
    }

    [Test]
    public void SequenceExecutorRequestsBestEffortCleanupOnceWhenStartThrows()
    {
        var registry = new ActivityPrimitiveRegistry(new[] { Primitive("begin"), Primitive("fallback") });
        var port = new FakeActivityPrimitivePort(request =>
        {
            if (request.primitiveId == "begin") throw new InvalidOperationException("transport failure");
            return StartResult(request, ActivitySequenceStatuses.Completed);
        });
        var runner = new ActivitySequenceExecutor(registry, port);
        var definition = Sequence("start-failure-flow",
            new ActivitySequenceStep("begin", "begin", failure: "fallback"),
            new ActivitySequenceStep("fallback", "fallback"));

        runner.Start(SequenceRequest(definition, "action-start-failure"));
        Assert.That(port.cancelCalls, Is.EqualTo(1));
        Assert.That(runner.Tick(1d).status, Is.EqualTo(ActivitySequenceStatuses.Completed));
        Assert.That(port.cancelCalls, Is.EqualTo(1));
    }

    private static ActivityPrimitiveDescriptor CompletePrimitive()
    {
        return new ActivityPrimitiveDescriptor("complete", "1", ActivityPrimitiveOperationKind.Pure,
            "", new ActivityArgumentDescriptor[0], new ActivityResultDescriptor[0]);
    }

    private static ActivityPrimitiveDescriptor Primitive(string id,
        IEnumerable<ActivityArgumentDescriptor> arguments = null,
        IEnumerable<ActivityResultDescriptor> results = null)
    {
        return new ActivityPrimitiveDescriptor(id, "1", ActivityPrimitiveOperationKind.Pure, "", arguments, results);
    }

    private static ActivitySequenceRequest SequenceRequest(ActivitySequenceDefinition definition, string actionId,
        IEnumerable<ActivitySequenceArgument> arguments = null, ScenarioRuntimeSnapshot context = null)
    {
        context = context ?? new ScenarioRuntimeSnapshot("scenario", "run-1", "turn-1", 1L, "",
            new string[0], new string[0], new ScenarioCapabilitySnapshot[0], new ScenarioObjectSnapshot[0], new string[0]);
        return new ActivitySequenceRequest(definition, new ContentRef("actions", "action", "r1", "hash"),
            "run-1", "flow-1", "flow-step-1", actionId, context, arguments);
    }

    private static ScenarioDefinition TwoSequenceActions()
    {
        return new ScenarioDefinition
        {
            scenarioId = "scenario.two-sequence-actions",
            topicId = "topic.two-sequence-actions",
            entryStepId = "action-a",
            steps = new List<ScenarioStep>
            {
                new ScenarioStep { id = "action-a", kind = "action", capabilityId = ActivityCapabilityIds.ActivitySequence,
                    actionRef = new ContentRef("actions", "action.a", "revision", "hash"),
                    activitySequence = Sequence("action.a.sequence", CompleteStep("finish")),
                    successNextStepId = "action-b", failureNextStepId = "failed" },
                new ScenarioStep { id = "action-b", kind = "action", capabilityId = ActivityCapabilityIds.ActivitySequence,
                    actionRef = new ContentRef("actions", "action.b", "revision", "hash"),
                    activitySequence = Sequence("action.b.sequence", CompleteStep("finish")),
                    successNextStepId = "done", failureNextStepId = "failed" },
                new ScenarioStep { id = "done", kind = "end", outcome = "completed" },
                new ScenarioStep { id = "failed", kind = "end", outcome = "failed" }
            }
        };
    }

    private static ScenarioRuntimeSnapshot TwoSequenceContext()
    {
        return new ScenarioRuntimeSnapshot("scenario.two-sequence-actions", "run-two-sequence-actions", "turn", 1L, "",
            new[] { "topic.two-sequence-actions" }, new[] { "happy" },
            new[] { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]) },
            new ScenarioObjectSnapshot[0], new string[0]);
    }

    private static ActivityPrimitiveStartResult StartResult(ActivityPrimitiveRequest request, string status,
        IEnumerable<ActivitySequenceArgument> outputs = null)
    {
        return new ActivityPrimitiveStartResult(request.identity, status, outputs, "");
    }

    private static ActivitySequenceSource DataSource(ActivitySequenceDefinition definition)
    {
        return new ActivitySequenceSource
        {
            id = definition.id,
            revision = definition.revision,
            steps = definition.steps.ToList()
        };
    }

    private static ActivitySequenceDefinition Sequence(string id, params ActivitySequenceStep[] steps)
    {
        return Sequence(id, (IEnumerable<ActivitySequenceStep>)steps);
    }

    private static ActivitySequenceDefinition Sequence(string id, IEnumerable<ActivitySequenceStep> steps)
    {
        return new ActivitySequenceDefinition(id, "r1", steps);
    }

    private static ActivitySequenceStep CompleteStep(string id)
    {
        return new ActivitySequenceStep(id, "complete");
    }

    private static ActionRecord TraceAction(string scenario)
    {
        string function = scenario == "TurnOnTv" ? "power.set" :
            scenario == "FetchDrink" ? "dispenser.collect_mock" : string.Empty;
        string useId = scenario == "TurnOnTv" ? "furniture.watch_tv" :
            scenario == "LocateTv" ? "furniture.approach_tv" : "furniture.collect_drink";
        var rows = new List<ActionSequenceStepRecord>();
        rows.Add(SequenceRow(0, "reserve", ActivityPrimitiveIds.ReserveNearest,
            TextArgument("useId", useId), "[{\"resultName\":\"targetToken\",\"bindingId\":\"target\"}]", "move"));
        rows.Add(SequenceRow(1, "move", ActivityPrimitiveIds.MoveTo,
            BindingArgument("target"), "[]", scenario == "FetchDrink" ? "collect" : function.Length != 0 ? "apply" : "arrive"));
        if (scenario == "TurnOnTv")
        {
            rows.Add(SequenceRow(2, "apply", ActivityPrimitiveIds.InvokeFurniture,
                BindingAndFunction("power.set", true), "[]", "finish"));
            rows.Add(SequenceRow(3, "finish", ActivityPrimitiveIds.Complete, "[]", "[]", ""));
        }
        else if (scenario == "LocateTv")
        {
            rows.Add(SequenceRow(2, "arrive", ActivityPrimitiveIds.Bark,
                TextArgument("dialogueId", "dialogue.tv_arrival"), "[]", "finish"));
            rows.Add(SequenceRow(3, "finish", ActivityPrimitiveIds.Complete, "[]", "[]", ""));
        }
        else
        {
            rows.Add(SequenceRow(2, "collect", ActivityPrimitiveIds.InvokeFurniture,
                BindingAndFunction("dispenser.collect_mock", false), "[]", "return"));
            rows.Add(SequenceRow(3, "return", ActivityPrimitiveIds.MoveNearPlayer, "[]", "[]", "finish"));
            rows.Add(SequenceRow(4, "finish", ActivityPrimitiveIds.Complete, "[]", "[]", ""));
        }
        return new ActionRecord
        {
            id = "action." + scenario.ToLowerInvariant(), revision = new string('a', 64),
            semanticHash = new string('b', 64), implementationKind = "activity_sequence", sequenceSteps = rows
        };
    }

    private static ScenarioRuntimeSnapshot TraceContext(string scenario)
    {
        string useId = scenario == "TurnOnTv" ? "furniture.watch_tv" :
            scenario == "LocateTv" ? "furniture.approach_tv" : "furniture.collect_drink";
        var capabilities = new List<ScenarioCapabilitySnapshot>
        {
            new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.ReserveNearest,
                new[] { new ScenarioArgumentBinding("useId", new[] { useId }) }),
            new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.Complete, new ScenarioArgumentBinding[0])
        };
        if (scenario == "LocateTv")
            capabilities.Add(new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.Bark,
                new[] { new ScenarioArgumentBinding("dialogueId", new[] { "dialogue.tv_arrival" }) }));
        if (scenario == "FetchDrink")
            capabilities.Add(new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.MoveNearPlayer, new ScenarioArgumentBinding[0]));
        return new ScenarioRuntimeSnapshot("scenario." + scenario, "run-" + scenario.ToLowerInvariant(), "turn", 1L, "",
            new string[0], new string[0], capabilities, new ScenarioObjectSnapshot[0], new string[0]);
    }

    private static ActionSequenceStepRecord SequenceRow(int ordinal, string id, string primitive,
        string arguments, string outputs, string success)
    {
        return new ActionSequenceStepRecord
        {
            ordinal = ordinal, stepId = id, primitiveId = primitive, argumentsJson = arguments,
            outputsJson = outputs, successEdgeJson = "{\"nextStepId\":\"" + success + "\"}",
            failureEdgeJson = "{\"nextStepId\":\"\"}", cancelEdgeJson = "{\"nextStepId\":\"\"}",
            checkpointPolicy = "none"
        };
    }

    private static string BindingArgument(string name)
    { return "[{\"name\":\"" + name + "\",\"value\":{\"bindingId\":\"target\"}}]"; }

    private static string TextArgument(string name, string value)
    { return "[{\"name\":\"" + name + "\",\"value\":{\"type\":\"text\",\"literal\":\"" + value + "\"}}]"; }

    private static string BindingAndFunction(string function, bool power)
    {
        string args = "[{\"name\":\"target\",\"value\":{\"bindingId\":\"target\"}}," +
            "{\"name\":\"functionId\",\"value\":{\"type\":\"enum\",\"literal\":\"" + function + "\"}}";
        if (power) args += ",{\"name\":\"desiredState\",\"value\":{\"type\":\"enum\",\"literal\":\"On\"}}";
        return args + "]";
    }

    private static ActivitySequenceStep Step(string id, string primitiveId)
    {
        return new ActivitySequenceStep(id, primitiveId);
    }

    private static ActivitySequenceStep StepWithArgument(string id, string primitiveId, string name,
        FlowValue value, string actualName = null)
    {
        return new ActivitySequenceStep(id, primitiveId,
            new[] { ActivitySequenceArgument.FromLiteral(actualName ?? name, value) });
    }

    private static string Diagnostics(ActivitySequenceBuildResult result)
    {
        return string.Join(", ", result.diagnostics.Select(item => item.code + ":" + item.path));
    }

    private static void AssertEquivalent(ActivitySequenceDefinition left, ActivitySequenceDefinition right)
    {
        Assert.That(left.id, Is.EqualTo(right.id));
        Assert.That(left.revision, Is.EqualTo(right.revision));
        Assert.That(left.steps.Count, Is.EqualTo(right.steps.Count));
        for (int index = 0; index < left.steps.Count; index++)
        {
            Assert.That(left.steps[index].stepId, Is.EqualTo(right.steps[index].stepId));
            Assert.That(left.steps[index].primitiveId, Is.EqualTo(right.steps[index].primitiveId));
            Assert.That(left.steps[index].successNextStepId, Is.EqualTo(right.steps[index].successNextStepId));
            Assert.That(left.steps[index].failureNextStepId, Is.EqualTo(right.steps[index].failureNextStepId));
            Assert.That(left.steps[index].cancelNextStepId, Is.EqualTo(right.steps[index].cancelNextStepId));
            Assert.That(left.steps[index].checkpointPolicy, Is.EqualTo(right.steps[index].checkpointPolicy));
            Assert.That(left.steps[index].arguments.Count, Is.EqualTo(right.steps[index].arguments.Count));
            for (int argumentIndex = 0; argumentIndex < left.steps[index].arguments.Count; argumentIndex++)
            {
                ActivitySequenceArgument leftArgument = left.steps[index].arguments[argumentIndex];
                ActivitySequenceArgument rightArgument = right.steps[index].arguments[argumentIndex];
                Assert.That(leftArgument.name, Is.EqualTo(rightArgument.name));
                Assert.That(leftArgument.value.bindingId, Is.EqualTo(rightArgument.value.bindingId));
                Assert.That(leftArgument.value.literal == null ? null : leftArgument.value.literal.type,
                    Is.EqualTo(rightArgument.value.literal == null ? null : rightArgument.value.literal.type));
                Assert.That(leftArgument.value.literal == null ? null : leftArgument.value.literal.ToHostArgument(),
                    Is.EqualTo(rightArgument.value.literal == null ? null : rightArgument.value.literal.ToHostArgument()));
            }
        }
    }

    private sealed class ProofComposer : IActivitySequenceComposer
    {
        public string composerId { get { return "fixture.proof"; } }

        public ActivitySequenceDefinition Compose(ActivitySequenceSource source)
        {
            return Sequence(source.id, CompleteStep("end"));
        }
    }

    private sealed class FakeActivityPrimitivePort : IActivityPrimitivePort
    {
        private readonly Func<ActivityPrimitiveRequest, ActivityPrimitiveStartResult> onStart;
        private readonly Func<ActivityPrimitiveCancellation, ActivityPrimitiveCompletion> onCancel;
        public readonly List<ActivityPrimitiveRequest> starts = new List<ActivityPrimitiveRequest>();
        public int cancelCalls;

        public FakeActivityPrimitivePort(Func<ActivityPrimitiveRequest, ActivityPrimitiveStartResult> start,
            Func<ActivityPrimitiveCancellation, ActivityPrimitiveCompletion> cancel = null)
        {
            onStart = start;
            onCancel = cancel ?? (request => new ActivityPrimitiveCompletion(request.identity,
                ActivitySequenceStatuses.Cancelled, new ActivitySequenceArgument[0], ""));
        }

        public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
        {
            starts.Add(request);
            return onStart(request);
        }

        public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
        {
            cancelCalls++;
            return onCancel(request);
        }
    }

    private sealed class FakeSequenceRunner : IActivitySequenceRunner
    {
        public int startCalls;
        public ActivitySequenceRequest lastRequest;
        public bool completeOnTick;

        public ActivitySequenceStartResult Start(ActivitySequenceRequest request)
        {
            startCalls++;
            lastRequest = request;
            return new ActivitySequenceStartResult(ActivitySequenceStatuses.Accepted, request.scenarioRunId,
                request.flowInvocationId, request.stepExecutionId, request.actionInvocationId, "");
        }

        public ActivitySequenceView Tick(double nowSeconds)
        { return View(completeOnTick ? ActivitySequenceStatuses.Completed : ActivitySequenceStatuses.Running); }

        public ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion)
        { return View(ActivitySequenceStatuses.Running); }

        public ActivitySequenceView Cancel(ActivitySequenceCancellation cancellation)
        { return View(ActivitySequenceStatuses.Cancelled); }

        public ActivitySequenceView Shutdown(string reason)
        { return View(ActivitySequenceStatuses.Cancelled); }

        private ActivitySequenceView View(string status)
        {
            return new ActivitySequenceView(status, lastRequest.scenarioRunId, lastRequest.flowInvocationId,
                lastRequest.stepExecutionId, lastRequest.actionInvocationId, "work", "");
        }
    }

    private sealed class FakeSequenceResources : IActivitySequenceResourceOwner
    {
        public int releaseCount;
        public string lastActionInvocationId;
        public void ReleaseSequence(string actionInvocationId)
        { releaseCount++; lastActionInvocationId = actionInvocationId; }
    }

    private sealed class FakePollingOperation : IActivitySequencePollingOperation
    {
        public string PrimitiveId { get; private set; }
        public int pollCount;

        public FakePollingOperation(string id) { PrimitiveId = id; }

        public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
        { return new ActivityPrimitiveStartResult(request.identity, ActivitySequenceStatuses.Running, null, ""); }

        public ActivityPrimitiveCompletion Poll(ActivityPrimitiveIdentity identity, double nowSeconds)
        {
            pollCount++;
            return nowSeconds < 1d ? null : new ActivityPrimitiveCompletion(identity,
                ActivitySequenceStatuses.Completed, new ActivitySequenceArgument[0], "");
        }

        public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
        { return new ActivityPrimitiveCompletion(request.identity, ActivitySequenceStatuses.Cancelled,
            new ActivitySequenceArgument[0], request.reasonCode); }
    }

    private sealed class FakeBarkPresenter : IActivitySequenceBarkPresenter
    {
        private readonly Dictionary<string, Action<ActivityPrimitiveIdentity, bool>> callbacks =
            new Dictionary<string, Action<ActivityPrimitiveIdentity, bool>>(StringComparer.Ordinal);
        public bool accepted;
        public int startCount;
        public int cancelCount;

        public bool TryPresent(ActivityPrimitiveRequest request, Action<ActivityPrimitiveIdentity, bool> visibilityAcknowledgement)
        {
            startCount++;
            if (!accepted) return false;
            callbacks[request.identity.invocationId] = visibilityAcknowledgement;
            return true;
        }

        public bool Cancel(ActivityPrimitiveIdentity identity)
        {
            cancelCount++;
            callbacks.Remove(identity.invocationId);
            return true;
        }

        public void Complete(ActivityPrimitiveIdentity identity, bool visible)
        {
            Action<ActivityPrimitiveIdentity, bool> callback;
            if (callbacks.TryGetValue(identity.invocationId, out callback)) callback(identity, visible);
        }
    }
}
