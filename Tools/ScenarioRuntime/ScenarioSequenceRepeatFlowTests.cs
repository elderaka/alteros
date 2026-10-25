using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Scenarios;
using NUnit.Framework;

public sealed class ScenarioSequenceRepeatFlowTests
{
    [Test]
    public void CompiledFlowRebindsSecondSequenceAndRoutesUnavailableSecondAction()
    {
        ScenarioRuntimeSnapshot context = Context("run-compiled-sequences");
        ContentExecutionPlan plan = PreparedTwoSequencePlan(context);
        var session = new ScenarioSession();

        ScenarioEffect first = session.Start(plan, context.runId, context).Single();
        string firstStepId = first.stepId;
        string firstActionId = first.activitySequenceRequest.actionInvocationId;
        session.Advance(new ScenarioEvent { kind = "activity_accepted", runId = context.runId,
            stepId = firstStepId, activityId = firstActionId }, context);
        ScenarioEffect second = session.Advance(new ScenarioEvent { kind = "activity_result", runId = context.runId,
            stepId = firstStepId, activityId = firstActionId, success = true }, context).Single();
        string secondStepId = second.stepId;
        string secondActionId = second.activitySequenceRequest.actionInvocationId;

        Assert.That(secondActionId, Is.Not.EqualTo(firstActionId));
        Assert.That(session.Running.activeActivityId, Is.EqualTo(secondActionId));
        session.Advance(new ScenarioEvent { kind = "activity_accepted", runId = context.runId,
            stepId = secondStepId, activityId = firstActionId }, context);
        session.Advance(new ScenarioEvent { kind = "activity_result", runId = context.runId,
            stepId = secondStepId, activityId = firstActionId, success = true }, context);
        Assert.That(session.Running.activeActivityId, Is.EqualTo(secondActionId));

        IReadOnlyList<ScenarioEffect> failed = session.Advance(new ScenarioEvent
        {
            kind = "activity_result", runId = context.runId, stepId = secondStepId,
            activityId = secondActionId, success = false
        }, context);

        Assert.That(failed.Single(item => item.type == "end_scenario").outcome, Is.EqualTo("failed"));
    }

    private static ContentExecutionPlan PreparedTwoSequencePlan(ScenarioRuntimeSnapshot context)
    {
        string storeId = Guid.NewGuid().ToString("D");
        var package = new ContentPackage
        { packageId = "two-sequence-actions", storeId = storeId, sourceManifestHash = Hash("manifest") };
        string actionRevision = Hash("action.sequence");
        package.actions.Add(new ActionRecord
        {
            id = "action.sequence",
            revision = actionRevision,
            semanticHash = actionRevision,
            capabilityId = ActivityCapabilityIds.ActivitySequence,
            implementationKind = "activity_sequence",
            contractJson = "{\"steps\":[{\"stepId\":\"finish\",\"primitiveId\":\"complete\",\"arguments\":[],\"resultBindings\":[],\"successNextStepId\":\"\",\"failureNextStepId\":\"\",\"cancelNextStepId\":\"\",\"checkpointPolicy\":\"none\"}]}",
            sequenceSteps = new List<ActionSequenceStepRecord>
            {
                new ActionSequenceStepRecord
                {
                    ordinal = 0,
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
        });

        string flowRevision = Hash("scenario.two-sequence-actions");
        var flow = new FlowRecord
        { id = "scenario.two-sequence-actions", revision = flowRevision, semanticHash = flowRevision,
            kind = "scenario", entryStepId = "action-a" };
        var actionRef = new ContentRef(storeId, "action.sequence", actionRevision, actionRevision);
        flow.steps.Add(new StepRecord { id = "action-a", kind = "action", actionRef = actionRef, dataJson = "{}" });
        flow.steps.Add(new StepRecord { id = "action-b", kind = "action", actionRef = actionRef, dataJson = "{}" });
        flow.steps.Add(new StepRecord { id = "done", kind = "end", dataJson = "{\"outcome\":\"completed\"}" });
        flow.steps.Add(new StepRecord { id = "failed", kind = "end", dataJson = "{\"outcome\":\"failed\"}" });
        flow.transitions.Add(new TransitionRecord { id = "a-success", fromStepId = "action-a", toStepId = "action-b", purpose = "success" });
        flow.transitions.Add(new TransitionRecord { id = "a-failure", fromStepId = "action-a", toStepId = "failed", purpose = "failure" });
        flow.transitions.Add(new TransitionRecord { id = "b-success", fromStepId = "action-b", toStepId = "done", purpose = "success" });
        flow.transitions.Add(new TransitionRecord { id = "b-failure", fromStepId = "action-b", toStepId = "failed", purpose = "failure" });
        package.flows.Add(flow);
        const string sourcePath = "scenario/scenario.two-sequence-actions.json";
        package.legacySources.Add(new LegacySourceRecord
        {
            kind = ContentSourceKinds.ScenarioDefinition,
            relativePath = sourcePath,
            sourceId = flow.id,
            sourceHash = Hash("source:" + sourcePath),
            contentId = flow.id,
            contentRevision = flow.revision,
            metadataJson = "{}"
        });

        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        var vocabulary = new ContentHostVocabulary(new[] { ActivityCapabilityIds.ActivitySequence },
            new string[0], new string[0], new string[0], new string[0], catalog.CreateBuilder());
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package, vocabulary);
        Assert.That(compiled.IsReady, Is.True, string.Join("; ", compiled.diagnostics.Select(item =>
            item.code + " at " + item.path + ": " + item.message)));
        ContentResult<ResolvedContentRelease> resolved = ContentFlowAdapter.Resolve(compiled.package,
            compiled.release, vocabulary);
        Assert.That(resolved.IsReady, Is.True);
        ContentResult<ContentExecutionPlan> prepared = ContentFlowAdapter.Prepare(resolved.value,
            new ContentRef(storeId, flow.id, flow.revision, flow.semanticHash),
            new Dictionary<string, FlowValue>(), context);
        Assert.That(prepared.IsReady, Is.True);
        return prepared.value;
    }

    private static ScenarioRuntimeSnapshot Context(string runId)
    {
        return new ScenarioRuntimeSnapshot("scenario.two-sequence-actions", runId, "turn", 1L, "",
            new string[0], new string[0], new[]
            { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]) },
            new ScenarioObjectSnapshot[0], new string[0]);
    }

    private static string Hash(string value)
    {
        using (SHA256 hash = SHA256.Create())
            return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
    }
}
