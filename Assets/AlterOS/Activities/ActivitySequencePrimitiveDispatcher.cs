using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.Activities
{
    public interface IActivitySequencePrimitiveOperation
    {
        string PrimitiveId { get; }
        ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request);
        ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request);
    }

    public interface IActivitySequencePollingOperation : IActivitySequencePrimitiveOperation
    {
        ActivityPrimitiveCompletion Poll(ActivityPrimitiveIdentity identity, double nowSeconds);
    }

    public interface IActivitySequenceResourceOperation : IActivitySequencePrimitiveOperation
    {
        void ReleaseSequence(string actionInvocationId);
    }

    public interface IActivitySequenceResourceOwner
    {
        void ReleaseSequence(string actionInvocationId);
    }

    public interface IActivitySequencePollingPort : IActivityPrimitivePort, IActivitySequenceResourceOwner
    {
        ActivityPrimitiveCompletion[] Tick(double nowSeconds);
    }

    /// <summary>Generic primitive handler dispatcher; ActivityHost/SequenceExecutor retain lifecycle ownership.</summary>
    public sealed class ActivitySequencePrimitiveDispatcher : IActivitySequencePollingPort
    {
        private sealed class Invocation
        {
            public readonly ActivityPrimitiveRequest request;
            public readonly IActivitySequencePrimitiveOperation operation;
            public readonly string payload;
            public ActivityPrimitiveStartResult start;

            public Invocation(ActivityPrimitiveRequest primitiveRequest,
                IActivitySequencePrimitiveOperation primitiveOperation, string requestPayload,
                ActivityPrimitiveStartResult startResult)
            { request = primitiveRequest; operation = primitiveOperation; payload = requestPayload; start = startResult; }
        }

        private readonly Dictionary<string, IActivitySequencePrimitiveOperation> operations =
            new Dictionary<string, IActivitySequencePrimitiveOperation>(StringComparer.Ordinal);
        private readonly Dictionary<string, Invocation> records = new Dictionary<string, Invocation>(StringComparer.Ordinal);
        private readonly Dictionary<string, Invocation> active = new Dictionary<string, Invocation>(StringComparer.Ordinal);
        private bool hasTicked;
        private double lastTick;

        public ActivitySequencePrimitiveDispatcher(IEnumerable<IActivitySequencePrimitiveOperation> primitiveOperations)
        {
            foreach (IActivitySequencePrimitiveOperation operation in primitiveOperations ?? Enumerable.Empty<IActivitySequencePrimitiveOperation>())
                if (operation == null || string.IsNullOrWhiteSpace(operation.PrimitiveId) || operations.ContainsKey(operation.PrimitiveId))
                    throw new ArgumentException("Primitive operations must have unique non-empty IDs.", "primitiveOperations");
                else operations.Add(operation.PrimitiveId, operation);
        }

        public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
        {
            if (request == null || request.identity == null || string.IsNullOrWhiteSpace(request.identity.invocationId) ||
                string.IsNullOrWhiteSpace(request.primitiveId)) return Failure(request, "primitive_request_invalid");
            string key = IdentityKey(request.identity);
            string payload = Payload(request);
            Invocation previous;
            if (records.TryGetValue(key, out previous))
                return previous.payload == payload ? previous.start : Failure(request, "primitive_identity_conflict");
            IActivitySequencePrimitiveOperation operation;
            if (!operations.TryGetValue(request.primitiveId, out operation)) return Failure(request, "primitive_unavailable");

            ActivityPrimitiveStartResult result;
            try { result = operation.Start(request); }
            catch (Exception) { return Failure(request, "primitive_start_failed"); }
            if (result == null || !SameIdentity(request.identity, result.identity))
            {
                try { operation.Cancel(new ActivityPrimitiveCancellation(request.identity, "primitive_start_identity_mismatch")); }
                catch (Exception) { }
                return Failure(request, "primitive_start_identity_mismatch");
            }
            var invocation = new Invocation(request, operation, payload, result);
            records.Add(key, invocation);
            if (result.status == ActivitySequenceStatuses.Accepted || result.status == ActivitySequenceStatuses.Running)
                active.Add(key, invocation);
            return result;
        }

        public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
        {
            if (request == null || request.identity == null) return new ActivityPrimitiveCompletion(null,
                ActivitySequenceStatuses.Failed, new ActivitySequenceArgument[0], "primitive_cancel_invalid");
            string key = IdentityKey(request.identity);
            Invocation invocation;
            if (!active.TryGetValue(key, out invocation))
                return new ActivityPrimitiveCompletion(request.identity, ActivitySequenceStatuses.Cancelled,
                    new ActivitySequenceArgument[0], request.reasonCode);
            ActivityPrimitiveCompletion result;
            try { result = invocation.operation.Cancel(request); }
            catch (Exception) { result = null; }
            if (result == null || !SameIdentity(request.identity, result.identity))
                result = new ActivityPrimitiveCompletion(request.identity, ActivitySequenceStatuses.Failed,
                    new ActivitySequenceArgument[0], "primitive_cancel_failed");
            if (IsTerminal(result.status)) active.Remove(key);
            return result;
        }

        public ActivityPrimitiveCompletion[] Tick(double nowSeconds)
        {
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) || hasTicked && nowSeconds < lastTick)
                throw new ArgumentOutOfRangeException("nowSeconds", "Primitive dispatcher time must be finite and monotonic.");
            hasTicked = true; lastTick = nowSeconds;
            var completed = new List<ActivityPrimitiveCompletion>();
            foreach (KeyValuePair<string, Invocation> pair in active.ToArray())
            {
                IActivitySequencePollingOperation polling = pair.Value.operation as IActivitySequencePollingOperation;
                if (polling == null) continue;
                ActivityPrimitiveCompletion result;
                try { result = polling.Poll(pair.Value.request.identity, nowSeconds); }
                catch (Exception) { result = new ActivityPrimitiveCompletion(pair.Value.request.identity,
                    ActivitySequenceStatuses.Failed, new ActivitySequenceArgument[0], "primitive_poll_failed"); }
                if (result == null) continue;
                if (!SameIdentity(pair.Value.request.identity, result.identity))
                    result = new ActivityPrimitiveCompletion(pair.Value.request.identity, ActivitySequenceStatuses.Failed,
                        new ActivitySequenceArgument[0], "primitive_poll_identity_mismatch");
                if (!IsTerminal(result.status)) continue;
                active.Remove(pair.Key);
                completed.Add(result);
            }
            return completed.ToArray();
        }

        public void ReleaseSequence(string actionInvocationId)
        {
            if (string.IsNullOrWhiteSpace(actionInvocationId)) return;
            KeyValuePair<string, Invocation>[] sequenceRecords = records.Where(item =>
                item.Value.request.identity.actionInvocationId == actionInvocationId).ToArray();
            var releasedOperations = new HashSet<IActivitySequenceResourceOperation>();
            foreach (KeyValuePair<string, Invocation> pair in sequenceRecords)
            {
                IActivitySequenceResourceOperation resourceOperation = pair.Value.operation as IActivitySequenceResourceOperation;
                if (resourceOperation != null && releasedOperations.Add(resourceOperation))
                {
                    try { resourceOperation.ReleaseSequence(actionInvocationId); }
                    catch (Exception) { }
                }
                active.Remove(pair.Key);
                records.Remove(pair.Key);
            }
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
            var value = new System.Text.StringBuilder();
            Append(value, identity.scenarioRunId); Append(value, identity.flowInvocationId); Append(value, identity.stepExecutionId);
            Append(value, identity.actionInvocationId); Append(value, identity.sequenceStepId); Append(value, identity.invocationId);
            return value.ToString();
        }

        private static string Payload(ActivityPrimitiveRequest request)
        {
            var value = new System.Text.StringBuilder();
            Append(value, request.primitiveId);
            Append(value, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.store);
            Append(value, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.id);
            Append(value, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.revision);
            Append(value, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.expectedHash);
            foreach (ActivitySequenceArgument argument in request.arguments)
            {
                Append(value, argument.name); Append(value, argument.value == null ? "" : argument.value.bindingId);
                Append(value, argument.value == null || argument.value.literal == null ? "" : argument.value.literal.type);
                Append(value, argument.value == null || argument.value.literal == null ? "" : argument.value.literal.ToHostArgument());
            }
            return value.ToString();
        }

        private static void Append(System.Text.StringBuilder value, string part)
        { part = part ?? ""; value.Append(part.Length).Append(':').Append(part); }

        private static bool IsTerminal(string status)
        { return status == ActivitySequenceStatuses.Completed || status == ActivitySequenceStatuses.Cancelled || status == ActivitySequenceStatuses.Failed; }

        private static ActivityPrimitiveStartResult Failure(ActivityPrimitiveRequest request, string code)
        { return new ActivityPrimitiveStartResult(request == null ? null : request.identity, ActivitySequenceStatuses.Failed,
            new ActivitySequenceArgument[0], code); }
    }
}
