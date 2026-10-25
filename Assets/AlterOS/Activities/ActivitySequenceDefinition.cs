using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.Activities
{
    public static class ActivityPrimitiveIds
    {
        public const string ReserveNearest = "reserve_nearest";
        public const string MoveTo = "move_to";
        public const string Face = "face";
        public const string Bark = "bark";
        public const string Wait = "wait";
        public const string InvokeFurniture = "invoke_furniture";
        public const string MoveNearPlayer = "move_near_player";
        public const string Checkpoint = "checkpoint";
        public const string Complete = "complete";
    }

    public enum ActivityPrimitiveOperationKind
    {
        Pure = 0,
        FurnitureFunction = 1,
        Presentation = 2
    }

    public enum ActivitySequenceCheckpointPolicy
    {
        None = 0,
        Before = 1,
        After = 2
    }

    /// <summary>Immutable normalized sequence consumed by one registered execution path.</summary>
    public sealed class ActivitySequenceDefinition
    {
        public readonly string id;
        public readonly string revision;
        public readonly ReadOnlyCollection<ActivitySequenceStep> steps;

        public ActivitySequenceDefinition(string sequenceId, string sequenceRevision,
            IEnumerable<ActivitySequenceStep> sequenceSteps)
        {
            id = sequenceId ?? "";
            revision = sequenceRevision ?? "";
            steps = Array.AsReadOnly((sequenceSteps ?? new ActivitySequenceStep[0])
                .Select(CopyStep).ToArray());
        }

        private static ActivitySequenceStep CopyStep(ActivitySequenceStep step)
        {
            return step == null ? null : new ActivitySequenceStep(step.stepId, step.primitiveId,
                step.arguments, step.resultBindings, step.successNextStepId,
                step.failureNextStepId, step.cancelNextStepId, step.checkpointPolicy);
        }
    }

    /// <summary>Activity-sequence source inside action_definition; exactly steps XOR composerId.</summary>
    public sealed class ActivitySequenceSource
    {
        public string id = "";
        public string revision = "";
        public string composerId = "";
        public List<ActivitySequenceArgument> composerArguments = new List<ActivitySequenceArgument>();
        public List<ActivitySequenceStep> steps;
    }

    public sealed class ActivitySequenceStep
    {
        public readonly string stepId;
        public readonly string primitiveId;
        public readonly ReadOnlyCollection<ActivitySequenceArgument> arguments;
        public readonly ReadOnlyCollection<ActivitySequenceResultBinding> resultBindings;
        public readonly string successNextStepId;
        public readonly string failureNextStepId;
        public readonly string cancelNextStepId;
        public readonly ActivitySequenceCheckpointPolicy checkpointPolicy;

        public ActivitySequenceStep(string id, string primitive,
            IEnumerable<ActivitySequenceArgument> stepArguments = null,
            IEnumerable<ActivitySequenceResultBinding> outputBindings = null,
            string success = "", string failure = "", string cancel = "",
            ActivitySequenceCheckpointPolicy checkpoint = ActivitySequenceCheckpointPolicy.None)
        {
            stepId = id ?? "";
            primitiveId = primitive ?? "";
            arguments = Array.AsReadOnly((stepArguments ?? new ActivitySequenceArgument[0])
                .Where(item => item != null).Select(item => new ActivitySequenceArgument(item.name,
                    item.value == null ? null : new ActivitySequenceValue(item.value.literal, item.value.bindingId))).ToArray());
            resultBindings = Array.AsReadOnly((outputBindings ?? new ActivitySequenceResultBinding[0])
                .Where(item => item != null).Select(item => new ActivitySequenceResultBinding(item.resultName, item.bindingId)).ToArray());
            successNextStepId = success ?? "";
            failureNextStepId = failure ?? "";
            cancelNextStepId = cancel ?? "";
            checkpointPolicy = checkpoint;
        }
    }

    /// <summary>A literal FlowValue or a reference to a named result binding; never an expression.</summary>
    public sealed class ActivitySequenceValue
    {
        public readonly FlowValue literal;
        public readonly string bindingId;

        public ActivitySequenceValue(FlowValue value, string binding)
        {
            literal = value;
            bindingId = binding ?? "";
        }

        public static ActivitySequenceValue FromLiteral(FlowValue value)
        {
            return new ActivitySequenceValue(value, "");
        }

        public static ActivitySequenceValue FromBinding(string id)
        {
            return new ActivitySequenceValue(null, id);
        }
    }

    public sealed class ActivitySequenceArgument
    {
        public readonly string name;
        public readonly ActivitySequenceValue value;

        public ActivitySequenceArgument(string argumentName, ActivitySequenceValue argumentValue)
        {
            name = argumentName ?? "";
            value = argumentValue;
        }

        public static ActivitySequenceArgument FromLiteral(string argumentName, FlowValue literal)
        {
            return new ActivitySequenceArgument(argumentName, ActivitySequenceValue.FromLiteral(literal));
        }

        public static ActivitySequenceArgument FromBinding(string argumentName, string bindingId)
        {
            return new ActivitySequenceArgument(argumentName, ActivitySequenceValue.FromBinding(bindingId));
        }
    }

    public sealed class ActivitySequenceResultBinding
    {
        public readonly string resultName;
        public readonly string bindingId;

        public ActivitySequenceResultBinding(string result, string binding)
        {
            resultName = result ?? "";
            bindingId = binding ?? "";
        }
    }
}
