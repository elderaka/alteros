namespace AlterOS.Scenarios
{
    /// <summary>Minimal, host-confirmed Scenario history; free text and local variables are excluded.</summary>
    public sealed class ScenarioHistoryObservation
    {
        public string operationKey;
        public string kind;
        public string runId;
        public string scenarioId;
        public string invocationId;
        public string parentInvocationId;
        public string callExecutionId;
        public string flowId;
        public string flowStoreId;
        public string flowRevision;
        public string flowHash;
        public string stepExecutionId;
        public int visitOrdinal;
        public string actionExecutionId;
        public string capabilityId;
        public string stepId;
        public string answerKind;
        public string answerOptionId;
        public string outcome;
        public string occurredAtUtc;
        public bool invocationStarted;
    }
}
