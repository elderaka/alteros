namespace AlterOS.Scenarios
{
    /// <summary>Fail-closed invocation boundary for the durable host-effect port.</summary>
    public static class ScenarioHostEffectExecutor
    {
        public static ScenarioHostEffectResult Apply(IScenarioHostEffectPort port, ScenarioHostEffectRequest request)
        {
            if (request == null) return null;
            if (port == null)
                return Result(request, ScenarioHostEffectStatuses.Unavailable, "host_effect_port_unavailable");

            ScenarioHostEffectResult result;
            try { result = port.Apply(request); }
            catch { return Result(request, ScenarioHostEffectStatuses.Failed, "host_effect_port_failed"); }
            if (result == null || result.scenarioRunId != request.scenarioRunId ||
                result.invocationId != request.invocationId || result.stepExecutionId != request.stepExecutionId)
                return Result(request, ScenarioHostEffectStatuses.Failed, "host_effect_result_mismatch");
            return result;
        }

        private static ScenarioHostEffectResult Result(ScenarioHostEffectRequest request, string status, string reason)
        {
            return new ScenarioHostEffectResult(status, request.scenarioRunId, request.invocationId,
                request.stepExecutionId, string.Empty, string.Empty, 0L, reason);
        }
    }
}
