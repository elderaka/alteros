using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using AlterOS.Content;
using AlterOS.Scenarios;
using NUnit.Framework;

public sealed class ScenarioHostEffectFlowTests
{
    [Test]
    public void HostEffectWaitsForDeliveredDialogueAndRequiresCorrelatedDurableResult()
    {
        ScenarioRuntimeSnapshot context = Context("run-host-effect");
        ContentExecutionPlan plan = PreparedPlan(context);
        var session = new ScenarioSession();

        IReadOnlyList<ScenarioEffect> opening = session.Start(plan, context.runId, context);
        string dialogueExecutionId = session.Running.currentStepExecutionId;
        string dialogueStepId = session.Running.currentStepId;
        IReadOnlyList<ScenarioEffect> effect = session.Advance(new ScenarioEvent
        {
            kind = "dialogue_complete", runId = context.runId, stepId = dialogueStepId
        }, context);

        Assert.That(effect.Any(item => item.type == "request_host_effect"), Is.True,
            string.Join(";", effect.Select(item => item.type + ":" + item.outcome)));
        ScenarioHostEffectRequest request = effect.Single(item => item.type == "request_host_effect").hostEffectRequest;
        Assert.That(opening.Single().type, Is.EqualTo("show_dialogue"));
        Assert.That(request.actionRef.id, Is.EqualTo("action.grant-water"));
        Assert.That(request.requiresCompletedStepId, Is.EqualTo("greet"));
        Assert.That(request.completedStepExecutionId, Is.EqualTo(dialogueExecutionId));
        Assert.That(request.scenarioRunId, Is.EqualTo(context.runId));

        IReadOnlyList<ScenarioEffect> stale = session.Advance(new ScenarioEvent
        {
            kind = "host_effect_result", runId = context.runId, stepId = session.Running.currentStepId,
            hostEffectResult = new ScenarioHostEffectResult(ScenarioHostEffectStatuses.Applied,
                context.runId, "wrong-flow", session.Running.currentStepExecutionId, "op", "receipt", 2, "")
        }, context);
        Assert.That(stale, Is.Empty);

        IReadOnlyList<ScenarioEffect> completed = session.Advance(new ScenarioEvent
        {
            kind = "host_effect_result", runId = context.runId, stepId = session.Running.currentStepId,
            hostEffectResult = new ScenarioHostEffectResult(ScenarioHostEffectStatuses.Applied,
                request.scenarioRunId, request.invocationId, request.stepExecutionId, "op", "receipt", 2, "")
        }, context);
        Assert.That(completed.Single(item => item.type == "end_scenario").outcome, Is.EqualTo("completed"));
    }

    [Test]
    public void MissingPersistencePortReturnsCorrelatedUnavailableWithoutApplyingEffect()
    {
        ScenarioHostEffectRequest request = new ScenarioHostEffectRequest(
            new ContentRef("store", "action.grant-water", new string('a', 64), new string('a', 64)),
            "run", "run::flow", "run::grant-exec", "greet", "run::greet-exec");

        ScenarioHostEffectResult result = ScenarioHostEffectExecutor.Apply(null, request);

        Assert.That(result.status, Is.EqualTo(ScenarioHostEffectStatuses.Unavailable));
        Assert.That(result.scenarioRunId, Is.EqualTo(request.scenarioRunId));
        Assert.That(result.invocationId, Is.EqualTo(request.invocationId));
        Assert.That(result.stepExecutionId, Is.EqualTo(request.stepExecutionId));
        Assert.That(result.reasonCode, Is.EqualTo("host_effect_port_unavailable"));
    }

    private static ContentExecutionPlan PreparedPlan(ScenarioRuntimeSnapshot context)
    {
        string storeId = Guid.NewGuid().ToString("D");
        var package = new ContentPackage { packageId = "host-effect-fixture", storeId = storeId, sourceManifestHash = Hash("manifest") };
        string textRevision = Hash("text.greet");
        var text = new TextRecord { id = "text.greet", revision = textRevision, semanticHash = textRevision };
        text.translations.Add(new TranslationRecord { language = "en", text = "Here, cheer!" });
        package.texts.Add(text);
        string dialogueRevision = Hash("dialogue.greeting");
        var dialogue = new DialogueRecord { id = "dialogue.greeting", revision = dialogueRevision, semanticHash = dialogueRevision };
        dialogue.beats.Add(new BeatRecord
        {
            id = "greet.beat", sequenceOrder = 0,
            textRef = new ContentRef(storeId, text.id, text.revision, text.semanticHash),
            presentationJson = "{\"speaker\":\"alterra\",\"voice\":\"alterra\",\"audioKey\":\"\",\"animation\":\"\"}"
        });
        package.dialogues.Add(dialogue);
        string itemRevision = Hash("item.water");
        string itemTextRevision = Hash("text.water");
        var itemText = new TextRecord { id = "text.water", revision = itemTextRevision, semanticHash = itemTextRevision };
        itemText.translations.Add(new TranslationRecord { language = "en", text = "Water glass" });
        package.texts.Add(itemText);
        package.items.Add(new ItemRecord { id = "item.water", revision = itemRevision, semanticHash = itemRevision,
            displayTextRef = new ContentRef(storeId, itemText.id, itemText.revision, itemText.semanticHash), stackLimit = 9 });
        string actionRevision = Hash("action.grant-water");
        string actionArguments = "{\"itemRef\":{\"store\":\"" + storeId + "\",\"id\":\"item.water\",\"revision\":\"" + itemRevision + "\",\"expectedHash\":\"" + itemRevision + "\"},\"quantity\":1}";
        package.actions.Add(new ActionRecord
        {
            id = "action.grant-water", revision = actionRevision, semanticHash = actionRevision,
            capabilityId = ContentHostEffectFunctionRegistry.PlayerItemGrant, contractJson = "{}",
            implementationKind = "host_effect", functionId = ContentHostEffectFunctionRegistry.PlayerItemGrant,
            argumentsJson = actionArguments
        });
        string flowRevision = Hash("scenario.grant-proof");
        var flow = new FlowRecord { id = "scenario.grant-proof", revision = flowRevision, semanticHash = flowRevision,
            kind = "scenario", entryStepId = "greet" };
        flow.steps.Add(new StepRecord { id = "greet", kind = "dialogue",
            dialogueRef = new ContentRef(storeId, dialogue.id, dialogue.revision, dialogue.semanticHash), dataJson = "{}" });
        flow.steps.Add(new StepRecord { id = "grant", kind = "host_effect",
            actionRef = new ContentRef(storeId, "action.grant-water", actionRevision, actionRevision),
            dataJson = "{\"schemaVersion\":1,\"requiresCompletedStepId\":\"greet\"}" });
        flow.steps.Add(new StepRecord { id = "done", kind = "end", dataJson = "{\"outcome\":\"completed\"}" });
        flow.steps.Add(new StepRecord { id = "failed", kind = "end", dataJson = "{\"outcome\":\"failed\"}" });
        flow.transitions.Add(new TransitionRecord { id = "greet-next", fromStepId = "greet", toStepId = "grant", purpose = "next" });
        flow.transitions.Add(new TransitionRecord { id = "grant-success", fromStepId = "grant", toStepId = "done", purpose = "success" });
        flow.transitions.Add(new TransitionRecord { id = "grant-failure", fromStepId = "grant", toStepId = "failed", purpose = "failure" });
        package.flows.Add(flow);
        string sourcePath = "scenario/scenario.grant-proof.json";
        package.legacySources.Add(new LegacySourceRecord { kind = ContentSourceKinds.ScenarioDefinition,
            relativePath = sourcePath, sourceId = flow.id, sourceHash = Hash("source:" + sourcePath),
            contentId = flow.id, contentRevision = flow.revision, metadataJson = "{}" });

        var vocabulary = new ContentHostVocabulary(new[] { ContentHostEffectFunctionRegistry.PlayerItemGrant },
            new string[0], new string[0], new string[0], new string[0]);
        CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package, vocabulary);
        Assert.That(compiled.IsReady, Is.True, string.Join("; ", compiled.diagnostics.Select(item =>
            item.code + " at " + item.path + ": " + item.message)));
        ContentResult<ResolvedContentRelease> resolved = ContentFlowAdapter.Resolve(compiled.package, compiled.release, vocabulary);
        Assert.That(resolved.IsReady, Is.True);
        ContentResult<ContentExecutionPlan> prepared = ContentFlowAdapter.Prepare(resolved.value,
            new ContentRef(storeId, flow.id, flow.revision, flow.semanticHash),
            new Dictionary<string, FlowValue>(), context);
        Assert.That(prepared.IsReady, Is.True);
        return prepared.value;
    }

    private static string Hash(string value)
    {
        using (SHA256 hash = SHA256.Create())
            return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
    }

    private static ScenarioRuntimeSnapshot Context(string runId)
    {
        return new ScenarioRuntimeSnapshot("scenario.grant-proof", runId, "turn", 1L, "",
            new string[0], new string[0], new ScenarioCapabilitySnapshot[0],
            new ScenarioObjectSnapshot[0], new string[0]);
    }
}
