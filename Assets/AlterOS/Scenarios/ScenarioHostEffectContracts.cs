using System;
using AlterOS.Content;

namespace AlterOS.Scenarios
{
    public static class ScenarioHostEffectStatuses
    {
        public const string Applied = "applied";
        public const string Replayed = "replayed";
        public const string Unavailable = "unavailable";
        public const string Conflict = "conflict";
        public const string Failed = "failed";
    }

    /// <summary>
    /// A trusted host effect for one completed Scenario occurrence. The Action ref owns
    /// the effect function, Item ref and authored quantity; this request is not a delta.
    /// </summary>
    public sealed class ScenarioHostEffectRequest
    {
        public readonly ContentRef actionRef;
        public readonly string scenarioRunId;
        public readonly string invocationId;
        public readonly string stepExecutionId;
        public readonly string requiresCompletedStepId;
        public readonly string completedStepExecutionId;

        public ScenarioHostEffectRequest(ContentRef action, string runId, string flowInvocationId,
            string effectStepExecutionId, string requiredDialogueStepId, string completedDialogueStepExecutionId)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.store) ||
                string.IsNullOrWhiteSpace(action.id) || string.IsNullOrWhiteSpace(action.revision))
                throw new ArgumentException("A pinned host-effect Action reference is required.", "action");
            Require(runId, "runId");
            Require(flowInvocationId, "flowInvocationId");
            Require(effectStepExecutionId, "effectStepExecutionId");
            Require(requiredDialogueStepId, "requiredDialogueStepId");
            Require(completedDialogueStepExecutionId, "completedDialogueStepExecutionId");

            actionRef = new ContentRef(action.store, action.id, action.revision, action.expectedHash);
            scenarioRunId = runId;
            invocationId = flowInvocationId; // In game history, invocationId is the Flow invocation.
            stepExecutionId = effectStepExecutionId;
            requiresCompletedStepId = requiredDialogueStepId;
            completedStepExecutionId = completedDialogueStepExecutionId;
        }

        private static void Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A correlated Scenario identity is required.", parameterName);
        }
    }

    public sealed class ScenarioHostEffectResult
    {
        public readonly string status;
        public readonly string scenarioRunId;
        public readonly string invocationId;
        public readonly string stepExecutionId;
        public readonly string operationId;
        public readonly string receiptId;
        public readonly long committedGameRevision;
        public readonly string reasonCode;

        public ScenarioHostEffectResult(string resultStatus, string runId, string flowInvocationId,
            string effectStepExecutionId, string operation, string receipt, long gameRevision, string reason)
        {
            if (!IsKnownStatus(resultStatus)) throw new ArgumentException("The host-effect status is not supported.", "resultStatus");
            Require(runId, "runId");
            Require(flowInvocationId, "flowInvocationId");
            Require(effectStepExecutionId, "effectStepExecutionId");
            if ((resultStatus == ScenarioHostEffectStatuses.Applied || resultStatus == ScenarioHostEffectStatuses.Replayed) &&
                (string.IsNullOrWhiteSpace(operation) || string.IsNullOrWhiteSpace(receipt)))
                throw new ArgumentException("A durable host-effect result requires its operation and receipt IDs.", "receipt");
            status = resultStatus;
            scenarioRunId = runId ?? "";
            invocationId = flowInvocationId ?? "";
            stepExecutionId = effectStepExecutionId ?? "";
            operationId = operation ?? "";
            receiptId = receipt ?? "";
            committedGameRevision = gameRevision;
            reasonCode = reason ?? "";
        }

        private static bool IsKnownStatus(string value)
        {
            return value == ScenarioHostEffectStatuses.Applied || value == ScenarioHostEffectStatuses.Replayed ||
                value == ScenarioHostEffectStatuses.Unavailable || value == ScenarioHostEffectStatuses.Conflict ||
                value == ScenarioHostEffectStatuses.Failed;
        }

        private static void Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A correlated Scenario identity is required.", parameterName);
        }
    }

    /// <summary>Behavior owns the Scenario boundary; DBMS supplies the Item-specific adapter behind it.</summary>
    public interface IScenarioHostEffectPort
    {
        ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request);
    }
}
