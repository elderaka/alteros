using System;
using System.Collections.Generic;
using AlterOS.Stats;
using AlterOS.Topics;

namespace AlterOS.Dialogue
{
    /// <summary>Converts validated synthetic Playground inputs into the new Topic runtime context.</summary>
    public static class TopicPlaygroundTopicContextAdapter
    {
        public static TopicContext Create(TopicPlaygroundContext source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var context = new TopicContext
            {
                stats = source.CopyStats(),
                localHour = source.LocalHour,
                objectsComplete = source.ObjectsComplete,
            };
            foreach (SyntheticFactInput fact in source.CopyFactInputs())
                if (fact.isKnown)
                    context.facts.Add(fact.key, fact.value);

            foreach (SyntheticObjectInput input in source.CopyObjectInputs())
            {
                var snapshot = new TopicObjectSnapshot { id = input.id, kind = input.kind };
                foreach (SyntheticObjectFlagInput flag in input.flags)
                    if (flag.isKnown)
                        snapshot.flags.Add(flag.key, flag.value);
                foreach (SyntheticObjectNumberInput number in input.numbers)
                    if (number.isKnown)
                        snapshot.numbers.Add(number.key, number.value);
                context.objects.Add(snapshot);
            }
            return context;
        }
    }

    public sealed class TopicPlaygroundTopicRun
    {
        public TopicDefinition Definition { get; internal set; }
        public TopicInstance Instance { get; internal set; }
        public TopicSelectionResult Selection { get; internal set; }
        public TopicPlaybackHandle Handle { get; internal set; }
        public DialoguePlayback Playback { get; internal set; }
    }

    /// <summary>Shared detached selection and lifecycle adapter used by Playground surfaces.</summary>
    public sealed class TopicPlaygroundTopicHarness
    {
        private readonly TopicHistory history;
        private TopicPlaygroundTopicRun activeRun;

        public TopicPlaygroundTopicHarness() : this(new TopicHistory()) { }

        public TopicPlaygroundTopicHarness(TopicHistory history)
        {
            this.history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public TopicHistory History { get { return history; } }
        public bool CanStartAnotherOccurrence { get { return activeRun == null || activeRun.Handle == null || activeRun.Handle.closed; } }

        public TopicPlaygroundTopicRun Select(
            TopicDefinition definition,
            TopicInstance instance,
            TopicContext context,
            DateTimeOffset nowUtc,
            double draw)
        {
            if (!CanStartAnotherOccurrence)
                throw new InvalidOperationException("A Topic occurrence is active. Acknowledge, complete, cancel, or fail it before selecting another.");

            TopicSelectionResult selection = TopicScenarioSelector.Select(definition, instance, context, history, nowUtc, draw);
            var run = new TopicPlaygroundTopicRun
            {
                Definition = definition,
                Instance = instance,
                Selection = selection,
            };
            if (selection.SelectedScenario != null)
            {
                TopicScenario scenario = selection.SelectedScenario;
                run.Playback = new DialoguePlayback(new DialogueDefinition
                {
                    id = definition.topicId + "." + scenario.scenarioId,
                    source = DialogueDefinition.GenericSource,
                    routing = new DialogueRouting { routeId = definition.topicId, examples = definition.anchors },
                    nodes = new List<CanonicalDialogueNode>(scenario.nodes),
                });
                run.Handle = history.BeginPlayback(definition, instance, scenario.scenarioId, nowUtc);
            }
            activeRun = run;
            return run;
        }

        public CanonicalDialogueNode Continue(TopicPlaygroundTopicRun run)
        {
            RequireRun(run);
            return run.Playback.Continue();
        }

        public CanonicalDialogueNode Choose(TopicPlaygroundTopicRun run, int choiceIndex)
        {
            RequireRun(run);
            return run.Playback.Choose(choiceIndex);
        }

        public TopicPlaybackResult Present(TopicPlaygroundTopicRun run, string nodeId, DateTimeOffset nowUtc)
        {
            RequireRun(run);
            return history.ReportPresentedNode(run.Handle, nodeId, nowUtc);
        }

        public TopicPlaybackResult Complete(TopicPlaygroundTopicRun run, DateTimeOffset nowUtc)
        {
            RequireRun(run);
            return history.Complete(run.Handle, nowUtc);
        }

        public TopicPlaybackResult Cancel(TopicPlaygroundTopicRun run)
        {
            RequireRun(run);
            return history.Cancel(run.Handle);
        }

        public TopicPlaybackResult Fail(TopicPlaygroundTopicRun run)
        {
            RequireRun(run);
            return history.Fail(run.Handle);
        }

        private void RequireRun(TopicPlaygroundTopicRun run)
        {
            if (run == null || run.Handle == null || !ReferenceEquals(activeRun, run))
                throw new InvalidOperationException("No active selected Topic occurrence is available for this lifecycle action.");
        }
    }
}
