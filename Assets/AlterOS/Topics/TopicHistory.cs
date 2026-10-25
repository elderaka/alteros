using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using AlterOS.Dialogue;
using AlterOS.Scenarios;

namespace AlterOS.Topics
{
    [DataContract]
    internal sealed class TopicHistoryRecord : IExtensibleDataObject
    {
        [DataMember(IsRequired = true)] public string instanceId;
        [DataMember(IsRequired = true)] public string topicId;
        [DataMember(IsRequired = true)] public string replayPolicy;
        [DataMember(IsRequired = true)] public long startedUtcTicks;
        [DataMember(IsRequired = true)] public long presentedUtcTicks;
        [DataMember(IsRequired = true)] public long completedUtcTicks;
        [DataMember(IsRequired = true)] public long consumedUtcTicks;
        [DataMember(IsRequired = true)] public long cooldownUntilUtcTicks;
        [DataMember(Name = "scenarioCompletions", EmitDefaultValue = false)] public List<TopicScenarioCompletionRecord> scenarioCompletions;
        public ExtensionDataObject ExtensionData { get; set; }
    }

    [DataContract]
    internal sealed class TopicScenarioCompletionRecord : IExtensibleDataObject
    {
        [DataMember(IsRequired = true)] public string scenarioId;
        [DataMember(IsRequired = true)] public int completionCount;
        [DataMember(IsRequired = true)] public long lastCompletedUtcTicks;
        public ExtensionDataObject ExtensionData { get; set; }
    }

    public sealed class TopicHistory
    {
        private readonly Dictionary<string, TopicHistoryRecord> records = new Dictionary<string, TopicHistoryRecord>(StringComparer.Ordinal);
        private TopicPlaybackHandle active;
        private long revision;
        private long savedRevision;
        public bool IsDirty { get { return revision != savedRevision; } }
        internal string StoragePath;
        internal string StorageFingerprint;
        public long StorageRevision { get; private set; }
        internal IEnumerable<TopicHistoryRecord> Records { get { return records.Values; } }
        internal void Restore(TopicHistoryRecord record) { records.Add(record.instanceId, record); }
        internal void Saved(string path, string fingerprint) { StoragePath = path; StorageFingerprint = fingerprint; savedRevision = revision; }
        internal void SavedSql(string path, long storageRevision)
        { StoragePath = path; StorageFingerprint = null; StorageRevision = storageRevision; savedRevision = revision; }

        public TopicPlaybackHandle BeginPlayback(TopicDefinition definition, TopicInstance instance, string scenarioId, DateTimeOffset nowUtc)
        {
            TopicDefinitionValidator.RequireValid(definition, instance);
            if (nowUtc.UtcDateTime.Ticks == 0) throw new ArgumentException("Playback time must be after the minimum instant.");
            string unavailable = Unavailability(instance, nowUtc);
            if (unavailable != null) throw new InvalidOperationException(unavailable);
            TopicScenario scenario = definition.scenarios.Find(s => s.scenarioId == scenarioId);
            if (scenario == null) throw new ArgumentException("Scenario does not belong to the Topic.");
            if (scenario.scenarioRef != null) throw new InvalidOperationException("Flow-backed entries must use BeginFlowPlayback.");
            if (active != null) active.closed = true;
            var handle = new TopicPlaybackHandle(this, instance, scenario, definition.cooldownSeconds, nowUtc);
            TopicHistoryRecord record;
            if (!records.TryGetValue(instance.instanceId, out record))
            {
                record = new TopicHistoryRecord { instanceId = instance.instanceId, topicId = instance.topicId, replayPolicy = instance.replayPolicy };
                records.Add(record.instanceId, record);
            }
            record.startedUtcTicks = nowUtc.UtcDateTime.Ticks;
            revision++;
            active = handle;
            return handle;
        }

        public TopicPlaybackHandle BeginFlowPlayback(TopicDefinition definition, TopicInstance instance,
            string scenarioId, string runId, string invocationId, DateTimeOffset nowUtc)
        {
            TopicDefinitionValidator.RequireValid(definition, instance);
            if (nowUtc.UtcDateTime.Ticks == 0) throw new ArgumentException("Playback time must be after the minimum instant.");
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(invocationId))
                throw new ArgumentException("A flow-backed Topic entry requires its exact Scenario run and invocation.");
            string unavailable = Unavailability(instance, nowUtc);
            if (unavailable != null) throw new InvalidOperationException(unavailable);
            TopicScenario scenario = definition.scenarios.Find(item => item != null && item.scenarioId == scenarioId);
            if (scenario == null || scenario.scenarioRef == null)
                throw new ArgumentException("The selected Topic entry is not Flow-backed.", nameof(scenarioId));
            if (active != null) active.closed = true;
            var handle = new TopicPlaybackHandle(this, instance, scenario, definition.cooldownSeconds,
                nowUtc, runId, invocationId);
            StartRecord(instance, scenario, nowUtc);
            active = handle;
            return handle;
        }

        public TopicPlaybackResult ReportPresentedNode(TopicPlaybackHandle handle, string nodeId, DateTimeOffset nowUtc)
        {
            if (!OwnsActive(handle) || handle.flowBacked || nowUtc < handle.lastTime || string.IsNullOrEmpty(nodeId)) return Result("Rejected", handle, false);
            if (nodeId == handle.lastNode)
            {
                // Also fences timestamps for legitimate self-loop deliveries. No second durable effect.
                handle.lastTime = nowUtc;
                return Result("Duplicate", handle, false);
            }
            if (!handle.nextNodes.Contains(nodeId)) return Result("Rejected", handle, false);
            TopicNodeStep step = handle.steps[nodeId];
            TopicHistoryRecord record = records[handle.InstanceId];
            if (handle.lastNode == null) record.presentedUtcTicks = nowUtc.UtcDateTime.Ticks;
            handle.lastNode = nodeId;
            handle.lastTime = nowUtc;
            handle.nextNodes = step.next;
            handle.canComplete = step.canComplete;
            revision++;
            if (handle.ReplayPolicy == TopicReplayPolicy.Once && nodeId == handle.checkpoint && record.consumedUtcTicks == 0)
                record.consumedUtcTicks = nowUtc.UtcDateTime.Ticks;
            return Result("Applied", handle, true);
        }

        public TopicPlaybackResult ReportFlowStepCompleted(TopicPlaybackHandle handle,
            ScenarioHistoryObservation observation, DateTimeOffset nowUtc)
        {
            if (!OwnsActive(handle) || !handle.flowBacked || observation == null ||
                observation.kind != "scenario_step_completed" || observation.runId != handle.flowRunId ||
                observation.invocationId != handle.flowInvocationId || observation.flowId != handle.flowDefinitionId ||
                string.IsNullOrWhiteSpace(observation.stepExecutionId) || string.IsNullOrWhiteSpace(observation.stepId) ||
                nowUtc < handle.lastTime)
                return Result("Rejected", handle, false);
            if (!handle.flowCompletedExecutionIds.Add(observation.stepExecutionId))
            {
                handle.lastTime = nowUtc;
                return Result("Duplicate", handle, false);
            }
            handle.lastTime = nowUtc;
            TopicHistoryRecord record = records[handle.InstanceId];
            if (handle.ReplayPolicy == TopicReplayPolicy.Once && observation.stepId == handle.checkpoint &&
                record.consumedUtcTicks == 0)
            {
                record.consumedUtcTicks = nowUtc.UtcDateTime.Ticks;
                revision++;
                return Result("Applied", handle, true);
            }
            return Result("Applied", handle, false);
        }

        public TopicPlaybackResult CompleteFlow(TopicPlaybackHandle handle, string runId, string invocationId,
            string outcome, DateTimeOffset nowUtc)
        {
            if (handle != null && handle.owner == this && handle.flowBacked && handle.completed &&
                runId == handle.flowRunId && invocationId == handle.flowInvocationId && outcome == "completed")
                return Result("Duplicate", handle, false);
            if (!OwnsActive(handle) || !handle.flowBacked || runId != handle.flowRunId ||
                invocationId != handle.flowInvocationId || nowUtc < handle.lastTime)
                return Result("Rejected", handle, false);
            if (outcome == "completed")
            {
                handle.canComplete = true;
                return Complete(handle, nowUtc);
            }
            if (outcome == "failed" || outcome == "player_ended" || outcome == "interrupted_by_quit")
                return Close(handle);
            return Result("Rejected", handle, false);
        }

        public TopicPlaybackResult Complete(TopicPlaybackHandle handle, DateTimeOffset nowUtc)
        {
            if (handle != null && handle.owner == this && handle.completed) return Result("Duplicate", handle, false);
            if (!OwnsActive(handle) || !handle.canComplete || nowUtc < handle.lastTime) return Result("Rejected", handle, false);
            DateTimeOffset until;
            try { until = nowUtc.AddSeconds(handle.cooldownSeconds); }
            catch (ArgumentOutOfRangeException) { return Result("Rejected", handle, false); }
            TopicHistoryRecord record = records[handle.InstanceId];
            record.completedUtcTicks = nowUtc.UtcDateTime.Ticks;
            RecordScenarioCompletion(record, handle.ScenarioId, nowUtc.UtcDateTime.Ticks);
            revision++;
            record.cooldownUntilUtcTicks = until.UtcDateTime.Ticks;
            if (handle.ReplayPolicy == TopicReplayPolicy.Once && record.consumedUtcTicks == 0)
                record.consumedUtcTicks = nowUtc.UtcDateTime.Ticks;
            handle.lastTime = nowUtc;
            handle.completed = true;
            handle.closed = true;
            active = null;
            return Result("Applied", handle, true);
        }

        public TopicPlaybackResult Cancel(TopicPlaybackHandle handle) { return Close(handle); }
        public TopicPlaybackResult Fail(TopicPlaybackHandle handle) { return Close(handle); }

        private TopicPlaybackResult Close(TopicPlaybackHandle handle)
        {
            if (!OwnsActive(handle)) return Result("Rejected", handle, false);
            handle.closed = true;
            active = null;
            return Result("Applied", handle, false);
        }

        private bool OwnsActive(TopicPlaybackHandle handle)
        { return handle != null && handle.owner == this && ReferenceEquals(active, handle) && !handle.closed; }

        private TopicPlaybackResult Result(string status, TopicPlaybackHandle handle, bool requiresSave)
        {
            return new TopicPlaybackResult(status,
                handle != null && handle.owner == this && IsConsumed(handle.InstanceId), IsDirty);
        }

        public bool IsConsumed(string instanceId)
        {
            TopicHistoryRecord record;
            return instanceId != null && records.TryGetValue(instanceId, out record) && record.consumedUtcTicks != 0;
        }

        public int GetCompletionCount(string instanceId, string scenarioId)
        {
            TopicScenarioCompletionRecord completion = FindCompletion(instanceId, scenarioId);
            return completion == null ? 0 : completion.completionCount;
        }

        public DateTimeOffset? GetLastCompletedAt(string instanceId, string scenarioId)
        {
            TopicScenarioCompletionRecord completion = FindCompletion(instanceId, scenarioId);
            return completion == null ? (DateTimeOffset?)null : new DateTimeOffset(completion.lastCompletedUtcTicks, TimeSpan.Zero);
        }

        private TopicScenarioCompletionRecord FindCompletion(string instanceId, string scenarioId)
        {
            TopicHistoryRecord record;
            if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(scenarioId) || !records.TryGetValue(instanceId, out record) || record.scenarioCompletions == null)
                return null;
            return record.scenarioCompletions.Find(entry => entry.scenarioId == scenarioId);
        }

        private static void RecordScenarioCompletion(TopicHistoryRecord record, string scenarioId, long completedTicks)
        {
            if (record.scenarioCompletions == null) record.scenarioCompletions = new List<TopicScenarioCompletionRecord>();
            TopicScenarioCompletionRecord completion = record.scenarioCompletions.Find(entry => entry.scenarioId == scenarioId);
            if (completion == null)
            {
                completion = new TopicScenarioCompletionRecord { scenarioId = scenarioId };
                record.scenarioCompletions.Add(completion);
            }
            if (completion.completionCount < int.MaxValue) completion.completionCount++;
            completion.lastCompletedUtcTicks = completedTicks;
        }

        private void StartRecord(TopicInstance instance, TopicScenario scenario, DateTimeOffset nowUtc)
        {
            TopicHistoryRecord record;
            if (!records.TryGetValue(instance.instanceId, out record))
            {
                record = new TopicHistoryRecord { instanceId = instance.instanceId, topicId = instance.topicId, replayPolicy = instance.replayPolicy };
                records.Add(record.instanceId, record);
            }
            record.startedUtcTicks = nowUtc.UtcDateTime.Ticks;
            revision++;
        }

        internal string Unavailability(TopicInstance instance, DateTimeOffset nowUtc)
        {
            TopicHistoryRecord record;
            if (!records.TryGetValue(instance.instanceId, out record)) return null;
            if (record.topicId != instance.topicId || record.replayPolicy != instance.replayPolicy)
                throw new ArgumentException("Instance identity cannot be reused for a different Topic/policy.");
            if (record.consumedUtcTicks != 0) return "Consumed";
            if (record.cooldownUntilUtcTicks > nowUtc.UtcDateTime.Ticks) return "CoolingDown";
            return null;
        }
    }

    public sealed class TopicPlaybackResult
    {
        public string Status { get; private set; }
        public bool IsConsumed { get; private set; }
        public bool RequiresSave { get; private set; }
        internal TopicPlaybackResult(string status, bool consumed, bool requiresSave)
        { Status = status; IsConsumed = consumed; RequiresSave = requiresSave; }
    }

    internal sealed class TopicNodeStep
    {
        internal List<string> next = new List<string>();
        internal bool canComplete;
    }

    public sealed class TopicPlaybackHandle
    {
        public string AttemptId { get; private set; }
        public string InstanceId { get; private set; }
        public string ScenarioId { get; private set; }
        public string ReplayPolicy { get; private set; }
        public bool IsFlowBacked { get { return flowBacked; } }
        public string FlowRunId { get { return flowRunId; } }
        public string FlowInvocationId { get { return flowInvocationId; } }
        internal readonly TopicHistory owner;
        internal readonly string checkpoint;
        internal readonly double cooldownSeconds;
        internal readonly bool flowBacked;
        internal readonly string flowRunId;
        internal readonly string flowInvocationId;
        internal readonly string flowDefinitionId;
        internal readonly HashSet<string> flowCompletedExecutionIds = new HashSet<string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, TopicNodeStep> steps = new Dictionary<string, TopicNodeStep>(StringComparer.Ordinal);
        internal List<string> nextNodes;
        internal string lastNode;
        internal DateTimeOffset lastTime;
        internal bool canComplete;
        internal bool closed;
        internal bool completed;

        internal TopicPlaybackHandle(TopicHistory owner, TopicInstance instance, TopicScenario scenario, double cooldown, DateTimeOffset now)
        {
            this.owner = owner; checkpoint = scenario.consumptionNodeId; cooldownSeconds = cooldown;
            AttemptId = Guid.NewGuid().ToString("N"); InstanceId = instance.instanceId;
            ScenarioId = scenario.scenarioId; ReplayPolicy = instance.replayPolicy; lastTime = now;
            flowBacked = false; flowRunId = string.Empty; flowInvocationId = string.Empty; flowDefinitionId = string.Empty;
            nextNodes = new List<string> { scenario.nodes[0].id };
            for (int index = 0; index < scenario.nodes.Count; index++)
            {
                CanonicalDialogueNode node = scenario.nodes[index];
                var step = new TopicNodeStep();
                if (node.choices != null && node.choices.Count > 0)
                {
                    foreach (CanonicalDialogueChoice choice in node.choices)
                        if (string.IsNullOrWhiteSpace(choice.nextId)) step.canComplete = true;
                        else step.next.Add(choice.nextId);
                }
                else if (!string.IsNullOrWhiteSpace(node.nextId)) step.next.Add(node.nextId);
                else if (index + 1 < scenario.nodes.Count) step.next.Add(scenario.nodes[index + 1].id);
                else step.canComplete = true;
                steps.Add(node.id, step);
            }
        }

        internal TopicPlaybackHandle(TopicHistory owner, TopicInstance instance, TopicScenario scenario,
            double cooldown, DateTimeOffset now, string runId, string invocationId)
        {
            this.owner = owner; checkpoint = scenario.consumptionNodeId; cooldownSeconds = cooldown;
            AttemptId = Guid.NewGuid().ToString("N"); InstanceId = instance.instanceId;
            ScenarioId = scenario.scenarioId; ReplayPolicy = instance.replayPolicy; lastTime = now;
            flowBacked = true; flowRunId = runId ?? string.Empty; flowInvocationId = invocationId ?? string.Empty;
            flowDefinitionId = scenario.scenarioRef == null ? string.Empty : scenario.scenarioRef.id;
            nextNodes = new List<string>();
        }
    }
}
