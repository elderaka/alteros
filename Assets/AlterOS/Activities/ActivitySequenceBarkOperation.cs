using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.Activities
{
    /// <summary>Non-modal bark lifecycle. A sequence advances only after its presenter confirms visibility.</summary>
    public sealed class ActivitySequenceBarkOperation : IActivitySequencePollingOperation, IActivitySequenceResourceOperation
    {
        private sealed class PendingBark
        {
            public readonly ActivityPrimitiveIdentity identity;
            public ActivityPrimitiveCompletion completion;

            public PendingBark(ActivityPrimitiveIdentity primitiveIdentity) { identity = primitiveIdentity; }
        }

        private readonly IActivitySequenceBarkPresenter presenter;
        private readonly Dictionary<string, PendingBark> pending = new Dictionary<string, PendingBark>(StringComparer.Ordinal);

        public ActivitySequenceBarkOperation(IActivitySequenceBarkPresenter barkPresenter)
        { presenter = barkPresenter ?? throw new ArgumentNullException("barkPresenter"); }

        public string PrimitiveId { get { return ActivityPrimitiveIds.Bark; } }

        public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
        {
            if (request == null || request.identity == null || request.primitiveId != PrimitiveId)
                return StartResult(request, ActivitySequenceStatuses.Failed, "primitive_request_invalid");
            string key = IdentityKey(request.identity);
            if (pending.ContainsKey(key)) return StartResult(request, ActivitySequenceStatuses.Failed, "primitive_identity_conflict");
            ActivitySequenceArgument dialogue = request.arguments.FirstOrDefault(item => item != null && item.name == "dialogueId");
            if (dialogue == null || dialogue.value == null || dialogue.value.literal == null ||
                dialogue.value.literal.type != "text" || string.IsNullOrWhiteSpace(dialogue.value.literal.textValue))
                return StartResult(request, ActivitySequenceStatuses.Failed, "bark_dialogue_invalid");

            var entry = new PendingBark(request.identity);
            pending.Add(key, entry);
            bool accepted;
            try { accepted = presenter.TryPresent(request, (identity, visible) => Complete(key, entry, identity, visible)); }
            catch (Exception) { accepted = false; }
            if (!accepted)
            {
                pending.Remove(key);
                try { presenter.Cancel(request.identity); }
                catch (Exception) { }
                return StartResult(request, ActivitySequenceStatuses.Failed, "bark_not_presented");
            }
            return StartResult(request, ActivitySequenceStatuses.Running, string.Empty);
        }

        public ActivityPrimitiveCompletion Poll(ActivityPrimitiveIdentity identity, double nowSeconds)
        {
            if (identity == null) return null;
            string key = IdentityKey(identity);
            PendingBark entry;
            if (!pending.TryGetValue(key, out entry) || entry.completion == null) return null;
            pending.Remove(key);
            return entry.completion;
        }

        public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
        {
            if (request == null || request.identity == null)
                return new ActivityPrimitiveCompletion(null, ActivitySequenceStatuses.Failed,
                    new ActivitySequenceArgument[0], "primitive_cancel_invalid");
            string key = IdentityKey(request.identity);
            PendingBark entry;
            if (pending.TryGetValue(key, out entry) && SameIdentity(entry.identity, request.identity))
            {
                pending.Remove(key);
                try { presenter.Cancel(request.identity); }
                catch (Exception) { }
            }
            return new ActivityPrimitiveCompletion(request.identity, ActivitySequenceStatuses.Cancelled,
                new ActivitySequenceArgument[0], request.reasonCode);
        }

        public void ReleaseSequence(string actionInvocationId)
        {
            foreach (PendingBark entry in pending.Values.Where(item => item.identity.actionInvocationId == actionInvocationId).ToArray())
                Cancel(new ActivityPrimitiveCancellation(entry.identity, "sequence_released"));
        }

        private void Complete(string key, PendingBark expected, ActivityPrimitiveIdentity identity, bool visible)
        {
            PendingBark current;
            if (!SameIdentity(expected.identity, identity) || !pending.TryGetValue(key, out current) || !ReferenceEquals(current, expected))
                return;
            expected.completion = visible
                ? new ActivityPrimitiveCompletion(expected.identity, ActivitySequenceStatuses.Completed,
                    new[] { ActivitySequenceArgument.FromLiteral("delivered", FlowValue.Boolean(true)) }, "")
                : new ActivityPrimitiveCompletion(expected.identity, ActivitySequenceStatuses.Failed,
                    new ActivitySequenceArgument[0], "bark_not_presented");
        }

        private static bool SameIdentity(ActivityPrimitiveIdentity left, ActivityPrimitiveIdentity right)
        {
            return left != null && right != null && left.scenarioRunId == right.scenarioRunId &&
                left.flowInvocationId == right.flowInvocationId && left.stepExecutionId == right.stepExecutionId &&
                left.actionInvocationId == right.actionInvocationId && left.sequenceStepId == right.sequenceStepId &&
                left.invocationId == right.invocationId;
        }

        private static string IdentityKey(ActivityPrimitiveIdentity identity)
        {
            return string.Concat(identity.scenarioRunId, "\n", identity.flowInvocationId, "\n", identity.stepExecutionId,
                "\n", identity.actionInvocationId, "\n", identity.sequenceStepId, "\n", identity.invocationId);
        }

        private static ActivityPrimitiveStartResult StartResult(ActivityPrimitiveRequest request, string status, string reason)
        {
            return new ActivityPrimitiveStartResult(request == null ? null : request.identity, status,
                new ActivitySequenceArgument[0], reason);
        }
    }

    public interface IActivitySequenceBarkPresenter
    {
        bool TryPresent(ActivityPrimitiveRequest request, Action<ActivityPrimitiveIdentity, bool> visibilityAcknowledgement);
        bool Cancel(ActivityPrimitiveIdentity identity);
    }
}
