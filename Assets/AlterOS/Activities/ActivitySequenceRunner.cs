using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AlterOS.Content;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    public static class ActivitySequenceStatuses
    {
        public const string Accepted = "accepted";
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Cancelled = "cancelled";
        public const string Failed = "failed";
    }

    /// <summary>Correlates a Flow step, physical Action and one sequence primitive without conflating IDs.</summary>
    public sealed class ActivityPrimitiveIdentity
    {
        public readonly string scenarioRunId;
        public readonly string flowInvocationId;
        public readonly string stepExecutionId;
        public readonly string actionInvocationId;
        public readonly string sequenceStepId;
        public readonly string invocationId;

        public ActivityPrimitiveIdentity(string runId, string flowId, string flowStepExecutionId,
            string physicalActionInvocationId, string sequenceStep, string primitiveInvocationId)
        {
            scenarioRunId = runId ?? "";
            flowInvocationId = flowId ?? "";
            stepExecutionId = flowStepExecutionId ?? "";
            actionInvocationId = physicalActionInvocationId ?? "";
            sequenceStepId = sequenceStep ?? "";
            invocationId = primitiveInvocationId ?? "";
        }
    }

    public sealed class ActivitySequenceRequest
    {
        public readonly ActivitySequenceDefinition definition;
        public readonly ContentRef actionDefinitionRef;
        public readonly string scenarioRunId;
        public readonly string flowInvocationId;
        public readonly string stepExecutionId;
        public readonly string actionInvocationId;
        public readonly string scenarioStepId;
        public readonly ScenarioRuntimeSnapshot context;
        public readonly ReadOnlyCollection<ActivitySequenceArgument> arguments;

        public ActivitySequenceRequest(ActivitySequenceDefinition sequence, ContentRef actionRef,
            string runId, string flowId, string flowStepExecutionId, string physicalActionInvocationId,
            ScenarioRuntimeSnapshot scenarioContext, IEnumerable<ActivitySequenceArgument> actionArguments = null,
            string scenarioStep = "")
        {
            definition = sequence;
            actionDefinitionRef = actionRef == null ? null : new ContentRef(
                actionRef.store, actionRef.id, actionRef.revision, actionRef.expectedHash);
            scenarioRunId = runId ?? "";
            flowInvocationId = flowId ?? "";
            stepExecutionId = flowStepExecutionId ?? "";
            actionInvocationId = physicalActionInvocationId ?? "";
            scenarioStepId = scenarioStep ?? "";
            context = scenarioContext;
            arguments = Array.AsReadOnly((actionArguments ?? new ActivitySequenceArgument[0])
                .Where(item => item != null).ToArray());
        }
    }

    public sealed class ActivityPrimitiveRequest
    {
        public readonly ActivityPrimitiveIdentity identity;
        public readonly ContentRef actionDefinitionRef;
        public readonly string primitiveId;
        public readonly ReadOnlyCollection<ActivitySequenceArgument> arguments;

        public ActivityPrimitiveRequest(ActivityPrimitiveIdentity primitiveIdentity, ContentRef actionRef,
            string id, IEnumerable<ActivitySequenceArgument> primitiveArguments)
        {
            identity = primitiveIdentity;
            actionDefinitionRef = actionRef == null ? null : new ContentRef(
                actionRef.store, actionRef.id, actionRef.revision, actionRef.expectedHash);
            primitiveId = id ?? "";
            arguments = Array.AsReadOnly((primitiveArguments ?? new ActivitySequenceArgument[0])
                .Where(item => item != null).ToArray());
        }
    }

    public sealed class ActivityPrimitiveStartResult
    {
        public readonly ActivityPrimitiveIdentity identity;
        public readonly string status;
        public readonly ReadOnlyCollection<ActivitySequenceArgument> outputs;
        public readonly string reasonCode;

        public ActivityPrimitiveStartResult(ActivityPrimitiveIdentity resultIdentity, string resultStatus,
            IEnumerable<ActivitySequenceArgument> resultOutputs, string reason)
        {
            identity = resultIdentity;
            status = resultStatus ?? "";
            outputs = Array.AsReadOnly((resultOutputs ?? new ActivitySequenceArgument[0])
                .Where(item => item != null).ToArray());
            reasonCode = reason ?? "";
        }
    }

    public sealed class ActivityPrimitiveCompletion
    {
        public readonly ActivityPrimitiveIdentity identity;
        public readonly string status;
        public readonly ReadOnlyCollection<ActivitySequenceArgument> outputs;
        public readonly string reasonCode;

        public ActivityPrimitiveCompletion(ActivityPrimitiveIdentity resultIdentity, string resultStatus,
            IEnumerable<ActivitySequenceArgument> resultOutputs, string reason)
        {
            identity = resultIdentity;
            status = resultStatus ?? "";
            outputs = Array.AsReadOnly((resultOutputs ?? new ActivitySequenceArgument[0])
                .Where(item => item != null).ToArray());
            reasonCode = reason ?? "";
        }
    }

    public sealed class ActivityPrimitiveCancellation
    {
        public readonly ActivityPrimitiveIdentity identity;
        public readonly string reasonCode;

        public ActivityPrimitiveCancellation(ActivityPrimitiveIdentity primitiveIdentity, string reason)
        {
            identity = primitiveIdentity;
            reasonCode = reason ?? "";
        }
    }

    public interface IActivityPrimitivePort
    {
        ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request);
        ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request);
    }

    public sealed class ActivitySequenceStartResult
    {
        public readonly string status;
        public readonly string scenarioRunId;
        public readonly string flowInvocationId;
        public readonly string stepExecutionId;
        public readonly string actionInvocationId;
        public readonly string reasonCode;

        public ActivitySequenceStartResult(string resultStatus, string runId, string flowId,
            string flowStepExecutionId, string physicalActionInvocationId, string reason)
        {
            status = resultStatus ?? "";
            scenarioRunId = runId ?? "";
            flowInvocationId = flowId ?? "";
            stepExecutionId = flowStepExecutionId ?? "";
            actionInvocationId = physicalActionInvocationId ?? "";
            reasonCode = reason ?? "";
        }
    }

    public sealed class ActivitySequenceView
    {
        public readonly string status;
        public readonly string scenarioRunId;
        public readonly string flowInvocationId;
        public readonly string stepExecutionId;
        public readonly string actionInvocationId;
        public readonly string sequenceStepId;
        public readonly string reasonCode;

        public ActivitySequenceView(string resultStatus, string runId, string flowId,
            string flowStepExecutionId, string physicalActionInvocationId, string currentSequenceStep,
            string reason)
        {
            status = resultStatus ?? "";
            scenarioRunId = runId ?? "";
            flowInvocationId = flowId ?? "";
            stepExecutionId = flowStepExecutionId ?? "";
            actionInvocationId = physicalActionInvocationId ?? "";
            sequenceStepId = currentSequenceStep ?? "";
            reasonCode = reason ?? "";
        }
    }

    public sealed class ActivitySequenceCancellation
    {
        public readonly string scenarioRunId;
        public readonly string flowInvocationId;
        public readonly string stepExecutionId;
        public readonly string actionInvocationId;
        public readonly string reasonCode;

        public ActivitySequenceCancellation(string runId, string flowId, string flowStepExecutionId,
            string physicalActionInvocationId, string reason)
        {
            scenarioRunId = runId ?? "";
            flowInvocationId = flowId ?? "";
            stepExecutionId = flowStepExecutionId ?? "";
            actionInvocationId = physicalActionInvocationId ?? "";
            reasonCode = reason ?? "";
        }
    }

    /// <summary>Portable lifecycle consumed by the exclusive ActivityHost adapter in W2.</summary>
    public interface IActivitySequenceRunner
    {
        ActivitySequenceStartResult Start(ActivitySequenceRequest request);
        ActivitySequenceView Tick(double nowSeconds);
        ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion);
        ActivitySequenceView Cancel(ActivitySequenceCancellation cancellation);
        ActivitySequenceView Shutdown(string reason);
    }
}
