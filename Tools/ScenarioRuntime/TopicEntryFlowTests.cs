using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Topics;
using AlterOS.Scenarios;
using NUnit.Framework;

public sealed class TopicEntryFlowTests
{
    [Test]
    public void TopicEntryCanReferenceReusableFlowWithoutInlineNodesAndKeepOwnEntryIdentity()
    {
        TopicDefinition definition = Family();
        TopicScenario entry = definition.scenarios[0];
        entry.scenarioRef = new ContentRef("11111111-1111-1111-1111-111111111111", "scenario.shared",
            new string('a', 64), new string('b', 64));
        entry.nodes.Clear();
        entry.consumptionNodeId = "delivery";

        List<string> errors = TopicDefinitionValidator.Validate(definition);

        Assert.That(errors, Is.Empty, string.Join(" ", errors));
        Assert.That(entry.scenarioId, Is.EqualTo("entry.first-run"));
        Assert.That(entry.scenarioRef.id, Is.EqualTo("scenario.shared"));
    }

    [Test]
    public void TopicEntryRejectsAmbiguousOrIncompleteScenarioSources()
    {
        TopicDefinition definition = Family();
        TopicScenario entry = definition.scenarios[0];
        entry.scenarioRef = new ContentRef("store", "scenario.shared", "revision", "hash");
        Assert.That(TopicDefinitionValidator.Validate(definition), Is.Not.Empty);

        entry.nodes.Clear();
        entry.scenarioRef = new ContentRef("", "", "", "");
        Assert.That(TopicDefinitionValidator.Validate(definition), Is.Not.Empty);
    }

    [Test]
    public void FlowBackedTopicEntriesKeepEntryWeightsAndCanReuseOneScenarioFlow()
    {
        TopicDefinition definition = Family();
        TopicScenario first = definition.scenarios[0];
        first.scenarioRef = FlowReference("scenario.shared");
        first.nodes.Clear();
        first.baseWeight = 3d;
        TopicScenario second = new TopicScenario
        {
            scenarioId = "entry.second", priority = first.priority, baseWeight = 1d,
            scenarioRef = FlowReference("scenario.shared"), condition = null,
            modifiers = new List<TopicWeightModifier>(), nodes = new List<AlterOS.Dialogue.CanonicalDialogueNode>()
        };
        definition.scenarios.Add(second);
        TopicInstance instance = TopicInstance.Create(definition, "occurrence", "authored");

        TopicSelectionResult selected = TopicScenarioSelector.SelectForConversation(definition, instance,
            new TopicContext(), new TopicHistory(), new System.DateTimeOffset(2026, 10, 23, 12, 0, 0,
                System.TimeSpan.Zero), 0.75d, "direct");

        Assert.That(selected.SelectedScenario.scenarioId, Is.EqualTo("entry.second"));
        Assert.That(selected.SelectedScenario.scenarioRef.id, Is.EqualTo("scenario.shared"));
    }

    [Test]
    public void FlowBackedHistoryConsumesOnlyCompletedCheckpointAndCompletesSameEntry()
    {
        TopicDefinition definition = Family();
        TopicScenario entry = definition.scenarios[0];
        entry.scenarioRef = FlowReference("scenario.shared");
        entry.nodes.Clear();
        entry.consumptionNodeId = "delivery";
        definition.defaultReplayPolicy = TopicReplayPolicy.Once;
        TopicInstance instance = TopicInstance.Create(definition, "flow-occurrence", "authored");
        var history = new TopicHistory();
        System.DateTimeOffset now = new System.DateTimeOffset(2026, 10, 23, 12, 0, 0, System.TimeSpan.Zero);
        TopicPlaybackHandle handle = history.BeginFlowPlayback(definition, instance, entry.scenarioId,
            "run-flow", "flow-invocation", now);
        ScenarioHistoryObservation opening = StepObservation("run-flow", "flow-invocation", entry.scenarioId,
            "opening", "execution-opening");
        ScenarioHistoryObservation delivery = StepObservation("run-flow", "flow-invocation", entry.scenarioId,
            "delivery", "execution-delivery");

        Assert.That(history.ReportFlowStepCompleted(handle, opening, now.AddSeconds(1)).Status, Is.EqualTo("Applied"));
        Assert.That(history.IsConsumed(instance.instanceId), Is.False);
        Assert.That(history.ReportFlowStepCompleted(handle,
            StepObservation("run-flow", "different-invocation", entry.scenarioId, "delivery", "wrong-invocation"),
            now.AddSeconds(2)).Status, Is.EqualTo("Rejected"));
        Assert.That(history.ReportFlowStepCompleted(handle, delivery, now.AddSeconds(2)).Status, Is.EqualTo("Applied"));
        Assert.That(history.IsConsumed(instance.instanceId), Is.True);
        Assert.That(history.ReportFlowStepCompleted(handle, delivery, now.AddSeconds(3)).Status, Is.EqualTo("Duplicate"));
        Assert.That(history.CompleteFlow(handle, "run-flow", "flow-invocation", "player_ended", now.AddSeconds(4)).Status,
            Is.EqualTo("Applied"));
        Assert.That(history.IsConsumed(instance.instanceId), Is.True, "A reached Once checkpoint remains consumed after a later cancel.");
        Assert.That(history.GetCompletionCount(instance.instanceId, entry.scenarioId), Is.EqualTo(0));

        TopicInstance completedInstance = TopicInstance.Create(definition, "flow-occurrence-completed", "authored");
        TopicPlaybackHandle completed = history.BeginFlowPlayback(definition, completedInstance, entry.scenarioId,
            "run-completed", "flow-invocation-completed", now.AddSeconds(5));
        Assert.That(history.CompleteFlow(completed, "run-completed", "flow-invocation-completed", "completed",
            now.AddSeconds(6)).Status, Is.EqualTo("Applied"));
        Assert.That(history.CompleteFlow(completed, "run-completed", "flow-invocation-completed", "completed",
            now.AddSeconds(7)).Status, Is.EqualTo("Duplicate"));
        Assert.That(history.GetCompletionCount(completedInstance.instanceId, entry.scenarioId), Is.EqualTo(1));

        TopicInstance cancelledInstance = TopicInstance.Create(definition, "flow-occurrence-cancelled", "authored");
        TopicPlaybackHandle cancelled = history.BeginFlowPlayback(definition, cancelledInstance, entry.scenarioId,
            "run-cancelled", "flow-invocation-cancelled", now.AddSeconds(5));
        Assert.That(history.CompleteFlow(cancelled, "run-cancelled", "flow-invocation-cancelled", "failed",
            now.AddSeconds(6)).Status, Is.EqualTo("Applied"));
        Assert.That(history.IsConsumed(cancelledInstance.instanceId), Is.False);
        Assert.That(history.GetCompletionCount(cancelledInstance.instanceId, entry.scenarioId), Is.EqualTo(0));
    }

    [Test]
    public void TopicReferenceResolverUsesOneExactDependencyFromSelectedRelease()
    {
        string store = "11111111-1111-1111-1111-111111111111";
        var release = new ContentRelease
        {
            releaseId = "release",
            dependencies = new List<ContentReleaseDependency>
            {
                new ContentReleaseDependency { kind = "topic", store = store, id = "topic.tv", revision = new string('a',64), expectedHash = new string('b',64) },
                new ContentReleaseDependency { kind = "flow", store = store, id = "scenario.tv", revision = new string('c',64), expectedHash = new string('d',64) }
            }
        };

        ContentResult<ContentRef> resolved = TopicScenarioContentResolver.ResolveTopicReference(release, store, "topic.tv");
        ContentResult<ContentRef> missing = TopicScenarioContentResolver.ResolveTopicReference(release, store, "topic.missing");
        release.dependencies.Add(new ContentReleaseDependency { kind = "topic", store = store, id = "topic.tv",
            revision = new string('e',64), expectedHash = new string('f',64) });
        ContentResult<ContentRef> ambiguous = TopicScenarioContentResolver.ResolveTopicReference(release, store, "topic.tv");

        Assert.That(resolved.IsReady, Is.True);
        Assert.That(resolved.value.revision, Is.EqualTo(new string('a',64)));
        Assert.That(missing.IsReady, Is.False);
        Assert.That(ambiguous.IsReady, Is.False);
    }

    private static TopicDefinition Family()
    {
        var definition = new TopicDefinition { topicId = "topic.first-run" };
        definition.anchors.Add("Could you turn on the TV?");
        definition.scenarios.Add(new TopicScenario
        {
            scenarioId = "entry.first-run", baseWeight = 1,
            condition = null, modifiers = new List<TopicWeightModifier>(),
            nodes = new List<AlterOS.Dialogue.CanonicalDialogueNode>
            { new AlterOS.Dialogue.CanonicalDialogueNode { id = "start", text = "On it." } }
        });
        return definition;
    }

    private static ContentRef FlowReference(string id)
    {
        return new ContentRef("11111111-1111-1111-1111-111111111111", id,
            new string('a', 64), new string('b', 64));
    }

    private static ScenarioHistoryObservation StepObservation(string runId, string invocationId,
        string scenarioId, string stepId, string executionId)
    {
        return new ScenarioHistoryObservation
        {
            kind = "scenario_step_completed", runId = runId, invocationId = invocationId,
            scenarioId = scenarioId, flowId = "scenario.shared", stepId = stepId, stepExecutionId = executionId
        };
    }
}
