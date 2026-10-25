using System.Collections.Generic;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    /// <summary>Existing Scenario request port with correlated, game-owned activity event access.</summary>
    public sealed class ScenarioActivityAdapter : IScenarioActivityPort
    {
        private readonly ActivityHost host;
        private readonly List<ActivityEvent> pendingTerminalEvents = new List<ActivityEvent>();

        public ScenarioActivityAdapter(ActivityHost activityHost)
        { host = activityHost; }

        public ActivityRequestResult TryRequest(ActivityRequest request, ScenarioRuntimeSnapshot context)
        { return host == null ? ActivityRequestResult.Unavailable("activity_host_unavailable") : host.Request(request, context); }

        public bool Stop(string activityId, string reason)
        { return host != null && host.Stop(activityId, reason); }

        public bool BeginExchange(string activityId, string exchangeId)
        { return host != null && host.BeginExchange(activityId, exchangeId); }

        public bool EndExchange(string activityId, string exchangeId)
        { return host != null && host.EndExchange(activityId, exchangeId); }

        /// <summary>Projects trusted terminal events onto the current running/suspended Scenario wait.</summary>
        public IReadOnlyList<ScenarioEvent> DrainScenarioEvents(ScenarioInstanceView running, ScenarioInstanceView suspended)
        {
            if (host != null)
            {
                foreach (ActivityEvent item in host.DrainEvents())
                    if (item != null && item.kind == ActivityEventKind.Terminal) pendingTerminalEvents.Add(item);
            }

            var projected = new List<ScenarioEvent>();
            foreach (ActivityEvent item in pendingTerminalEvents.ToArray())
            {
                if (Matches(suspended, item) && suspended.status == "suspended")
                {
                    projected.Add(new ScenarioEvent
                    {
                        kind = "activity_completed", runId = item.runId,
                        stepId = suspended.currentStepId, activityId = item.activityId
                    });
                    pendingTerminalEvents.Remove(item);
                    continue;
                }

                ScenarioStep step = FindCurrentStep(running);
                if (step != null && step.kind == "action" && step.activitySequence != null &&
                    Matches(running, item) && item.stepId == running.currentStepId &&
                    running.activeActivityId == item.activityId && running.status == "running")
                {
                    projected.Add(new ScenarioEvent
                    {
                        kind = "activity_result", runId = item.runId, stepId = step.id,
                        activityId = item.activityId, success = item.terminalReason == ActivityTerminalReason.Completed
                    });
                    pendingTerminalEvents.Remove(item);
                    continue;
                }
                if (step != null && step.kind == "wait" && step.conditionId == ActivityConditionIds.Terminal &&
                    Matches(running, item) && running.status == "running")
                {
                    projected.Add(new ScenarioEvent
                    {
                        kind = item.terminalReason == ActivityTerminalReason.Completed ? "condition_met" : "condition_failed",
                        runId = item.runId, stepId = step.id, activityId = item.activityId, conditionId = step.conditionId
                    });
                    pendingTerminalEvents.Remove(item);
                    continue;
                }

                if ((running != null && running.runId == item.runId && running.status == "ended") ||
                    (suspended != null && suspended.runId == item.runId && suspended.status == "ended"))
                    pendingTerminalEvents.Remove(item);
            }
            return projected.AsReadOnly();
        }

        /// <summary>Discards any late terminal records after the owning Scenario run has ended.</summary>
        public void ForgetRun(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return;
            pendingTerminalEvents.RemoveAll(item => item.runId == runId);
        }

        public ActivityView Current { get { return host == null ? null : host.Current; } }

        private static bool Matches(ScenarioInstanceView instance, ActivityEvent activity)
        { return instance != null && activity != null && instance.runId == activity.runId && instance.activeActivityId == activity.activityId; }

        private static ScenarioStep FindCurrentStep(ScenarioInstanceView instance)
        {
            return instance == null || instance.definition == null || instance.definition.steps == null
                ? null : instance.definition.steps.Find(step => step != null && step.id == instance.currentStepId);
        }
    }
}
