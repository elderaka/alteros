using System.Collections.Generic;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Scenarios;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Drives only exact release effects through bounded synthetic ports and the production ScenarioSession.</summary>
    internal static class WorkbenchPreviewFlowEffectDriver
    {
        public static IReadOnlyList<ScenarioEffect> Resolve(ScenarioSession session, ScenarioRuntimeSnapshot runtime,
            string runId, ContentPackage package, ContentRelease release, TrustedFixtureContext fixture,
            IReadOnlyList<ScenarioEffect> initial, WorkbenchSyntheticHostEffectPort hostPort,
            out List<object> activityRuns)
        {
            activityRuns = new List<object>();
            var output = new List<ScenarioEffect>();
            var pending = new Queue<ScenarioEffect>(initial ?? new ScenarioEffect[0]);
            int operations = 0;
            while (pending.Count > 0 && operations++ < ScenarioDefinitionValidator.MaximumSteps)
            {
                ScenarioEffect effect = pending.Dequeue();
                output.Add(effect);
                if (effect.type == "request_host_effect" && effect.hostEffectRequest != null)
                {
                    ScenarioHostEffectResult result = ScenarioHostEffectExecutor.Apply(hostPort, effect.hostEffectRequest);
                    foreach (ScenarioEffect next in session.Advance(new ScenarioEvent
                        { kind = "host_effect_result", runId = runId, stepId = effect.stepId, hostEffectResult = result }, runtime))
                        pending.Enqueue(next);
                    continue;
                }
                if (effect.type != "request_activity") continue;
                ActivitySequenceRequest request = effect.activitySequenceRequest;
                if (request == null)
                {
                    activityRuns.Add(new Dictionary<string, object>
                    { { "stepId", effect.stepId }, { "status", "pending_unregistered_legacy_activity" } });
                    continue;
                }

                IReadOnlyList<ScenarioEffect> accepted = session.Advance(new ScenarioEvent
                    { kind = "activity_accepted", runId = runId, stepId = effect.stepId, activityId = request.actionInvocationId }, runtime);
                foreach (ScenarioEffect next in accepted) pending.Enqueue(next);
                bool succeeded = WorkbenchFixtureActivitySequenceRunner.TryRun(request, package, release, fixture,
                    out ActivitySequenceView view, out string diagnostic, out string[] executed, out IList<object> trace);
                activityRuns.Add(new Dictionary<string, object>
                {
                    { "stepId", effect.stepId }, { "sequenceStatus", view == null ? "failed" : view.status },
                    { "reason", diagnostic }, { "executedPrimitives", executed },
                    { "syntheticTrace", trace }, { "syntheticOnly", true }
                });
                foreach (ScenarioEffect next in session.Advance(new ScenarioEvent
                    { kind = "activity_result", runId = runId, stepId = effect.stepId,
                      activityId = request.actionInvocationId, success = succeeded,
                      actionResults = new Dictionary<string, FlowValue>() }, runtime))
                    pending.Enqueue(next);
            }
            if (pending.Count > 0) output.Add(new ScenarioEffect("scenario_flow_failed", runId, "", "preview_transition_limit"));
            return output.AsReadOnly();
        }
    }
}
