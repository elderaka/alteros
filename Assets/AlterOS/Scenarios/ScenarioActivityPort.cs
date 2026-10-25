using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Scenarios
{
    public sealed class ActivityRequest
    {
        public readonly string runId, stepId, capabilityId;
        public readonly IReadOnlyList<ScenarioActionArgument> arguments;
        public ActivityRequest(string run, string step, string capability, IEnumerable<ScenarioActionArgument> args)
        {
            runId = run ?? ""; stepId = step ?? ""; capabilityId = capability ?? "";
            arguments = Array.AsReadOnly((args ?? new ScenarioActionArgument[0])
                .Where(item => item != null).Select(item => new ScenarioActionArgument { name = item.name, value = item.value }).ToArray());
        }
    }

    public sealed class ActivityRequestResult
    {
        public readonly string status, activityId, reason;
        private ActivityRequestResult(string result, string id, string error)
        { status = result; activityId = id ?? ""; reason = error ?? ""; }
        public static ActivityRequestResult Accepted(string authoritativeActivityId)
        { return string.IsNullOrWhiteSpace(authoritativeActivityId) ? Unavailable("invalid_activity_id") : new ActivityRequestResult("accepted", authoritativeActivityId, ""); }
        public static ActivityRequestResult Unavailable(string reasonCode)
        { return new ActivityRequestResult("unavailable", "", string.IsNullOrWhiteSpace(reasonCode) ? "unavailable" : reasonCode); }
    }

    /// <summary>The only boundary through which a Scenario can request a game-owned activity.</summary>
    public interface IScenarioActivityPort
    {
        ActivityRequestResult TryRequest(ActivityRequest request, ScenarioRuntimeSnapshot context);
    }
}
