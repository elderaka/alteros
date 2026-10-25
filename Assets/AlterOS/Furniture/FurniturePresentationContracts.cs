using System;
using System.Collections.Generic;
using AlterOS.Content;

namespace AlterOS.Furniture
{
    public static class FurniturePresentationDescriptorIds
    {
        public const string DispenserCollectMock = "dispenser.collect_mock";
    }

    public static class FurniturePresentationStatuses
    {
        public const string Accepted = "accepted";
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Cancelled = "cancelled";
        public const string Failed = "failed";
    }

    public enum FurniturePresentationValidationPurpose
    {
        Start = 0,
        Poll = 1,
        Cancel = 2
    }

    [Serializable]
    public sealed class FurniturePresentationRequest
    {
        public string presentationRequestId;
        public string presentationDescriptorId;
        public string runId;
        public string invocationId;
        public ContentRef actionDefinitionRef;
        public string activityId;
        public string sequenceStepId;
        public string instanceId;
        public string definitionId;
        public string usePointId;
        public int usePointRevision;
    }

    [Serializable]
    public sealed class FurniturePresentationHandle
    {
        public string presentationRequestId;
        public string presentationDescriptorId;
        public string runId;
        public string invocationId;
        public ContentRef actionDefinitionRef;
        public string activityId;
        public string sequenceStepId;
        public string instanceId;
        public string definitionId;
        public string usePointId;
        public int usePointRevision;
    }

    [Serializable]
    public sealed class FurniturePresentationResult
    {
        public FurniturePresentationHandle handle;
        public string status;
        public string diagnosticCode;
    }

    public sealed class FurniturePresentationValidationResult
    {
        public bool allowed;
        public string diagnosticCode = string.Empty;
    }

    public interface IFurniturePresentationContextValidator
    {
        // Start checks the exact point claim; Poll and Cancel check the one owning active sequence.
        FurniturePresentationValidationResult Validate(FurniturePresentationHandle handle,
            FurniturePresentationValidationPurpose purpose);
    }

    public interface IFurniturePresentationPort
    {
        FurniturePresentationResult Start(FurniturePresentationRequest request);
        FurniturePresentationResult Poll(FurniturePresentationHandle handle);
        FurniturePresentationResult Cancel(FurniturePresentationHandle handle);
    }

    public interface IFurniturePresentationOperation
    {
        string PresentationDescriptorId { get; }
        FurniturePresentationResult Start(FurniturePresentationHandle handle);
        FurniturePresentationResult Poll(FurniturePresentationHandle handle);
        FurniturePresentationResult Cancel(FurniturePresentationHandle handle);
    }

    /// <summary>Transient descriptor registry. It tracks presentation calls only; ActivitySequence owns sequence lifecycle.</summary>
    public sealed class FurniturePresentationRegistry : IFurniturePresentationPort
    {
        private sealed class OperationRecord
        {
            public FurniturePresentationHandle handle;
            public IFurniturePresentationOperation operation;
            public FurniturePresentationResult result;
            public bool cancellationRequested;
            public bool cleanupPending;
            public string failureDiagnosticCode = string.Empty;
        }

        private const int MaximumRequestsPerSequence = 64;
        private readonly IFurniturePresentationContextValidator contextValidator;
        private readonly Dictionary<string, IFurniturePresentationOperation> operations =
            new Dictionary<string, IFurniturePresentationOperation>(StringComparer.Ordinal);
        private readonly Dictionary<string, OperationRecord> records =
            new Dictionary<string, OperationRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> activeRequestByActivity =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private string currentActivityId = string.Empty;

        public FurniturePresentationRegistry(IFurniturePresentationContextValidator contextValidator)
        {
            if (contextValidator == null) throw new ArgumentNullException("contextValidator");
            this.contextValidator = contextValidator;
        }

        public bool Register(IFurniturePresentationOperation operation)
        {
            if (operation == null || string.IsNullOrWhiteSpace(operation.PresentationDescriptorId) ||
                operation.PresentationDescriptorId == FurnitureFunctionIds.PowerSet ||
                operations.ContainsKey(operation.PresentationDescriptorId)) return false;
            operations.Add(operation.PresentationDescriptorId, operation);
            return true;
        }

        public FurniturePresentationResult Start(FurniturePresentationRequest request)
        {
            FurniturePresentationHandle handle = CreateHandle(request);
            string invalidCode;
            if (!ValidateHandle(handle, out invalidCode)) return Failed(handle, invalidCode);

            OperationRecord existing;
            if (records.TryGetValue(handle.presentationRequestId, out existing))
            {
                return FurniturePresentationHandles.Equals(existing.handle, handle)
                    ? CopyResult(existing.result)
                    : Failed(handle, "presentation_request_id_reused");
            }

            IFurniturePresentationOperation operation;
            if (!operations.TryGetValue(handle.presentationDescriptorId, out operation))
                return Failed(handle, "presentation_descriptor_unavailable");

            FurniturePresentationValidationResult validation = ValidateContext(handle,
                FurniturePresentationValidationPurpose.Start);
            if (!validation.allowed) return Failed(handle, SafeCode(validation.diagnosticCode, "furniture_binding_unavailable"));

            if (!PrepareSequence(handle.activityId)) return Failed(handle, "another_presentation_sequence_is_active");
            string activeRequest;
            if (activeRequestByActivity.TryGetValue(handle.activityId, out activeRequest))
                return Failed(handle, "presentation_operation_already_active");
            if (CountRecordsForActivity(handle.activityId) >= MaximumRequestsPerSequence)
                return Failed(handle, "presentation_sequence_operation_limit");

            FurniturePresentationResult result;
            try { result = operation.Start(FurniturePresentationHandles.Copy(handle)); }
            catch (Exception)
            {
                return RecordStartFailure(handle, operation, "presentation_start_failed");
            }
            if (!IsAllowedStatus(result == null ? null : result.status,
                FurniturePresentationValidationPurpose.Start) ||
                !FurniturePresentationHandles.Equals(result.handle, handle))
                return RecordStartFailure(handle, operation, "invalid_presentation_start_result");
            if (result.status == FurniturePresentationStatuses.Failed)
                return RecordStartFailure(handle, operation, SafeCode(result.diagnosticCode, "presentation_start_failed"));

            result = CopyResult(result);

            records.Add(handle.presentationRequestId, new OperationRecord
            {
                handle = FurniturePresentationHandles.Copy(handle),
                operation = operation,
                result = result
            });
            if (IsActiveStatus(result.status)) activeRequestByActivity[handle.activityId] = handle.presentationRequestId;
            return CopyResult(result);
        }

        public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
        {
            OperationRecord record;
            if (!TryGetRecord(handle, out record)) return Failed(handle, "presentation_handle_unavailable");
            if (!FurniturePresentationHandles.Equals(record.handle, handle)) return Failed(handle, "presentation_handle_mismatch");
            if (IsTerminalStatus(record.result.status)) return CopyResult(record.result);

            if (!record.cleanupPending && !record.cancellationRequested)
            {
                FurniturePresentationValidationResult validation = ValidateContext(handle,
                    FurniturePresentationValidationPurpose.Poll);
                if (!validation.allowed)
                    return BeginFailureCleanup(record, SafeCode(validation.diagnosticCode, "presentation_sequence_unavailable"));
            }

            FurniturePresentationResult result;
            try { result = record.operation.Poll(FurniturePresentationHandles.Copy(record.handle)); }
            catch (Exception) { return BeginFailureCleanup(record, "presentation_poll_failed"); }
            if (!IsValidResult(result, record.handle, FurniturePresentationValidationPurpose.Poll))
                return BeginFailureCleanup(record, "invalid_presentation_result");
            if (record.cleanupPending) return CompleteOrContinueCleanup(record, result);
            if (result.status == FurniturePresentationStatuses.Failed)
                return BeginFailureCleanup(record, SafeCode(result.diagnosticCode, "presentation_poll_failed"));
            if (record.cancellationRequested)
            {
                if (result.status == FurniturePresentationStatuses.Cancelled || result.status == FurniturePresentationStatuses.Completed)
                    return CompleteTerminal(record, result);
                record.result = Pending(record.handle, "presentation_cancel_pending");
                activeRequestByActivity[record.handle.activityId] = record.handle.presentationRequestId;
                return CopyResult(record.result);
            }
            return StoreOperationResult(record, result);
        }

        public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
        {
            OperationRecord record;
            if (!TryGetRecord(handle, out record)) return Failed(handle, "presentation_handle_unavailable");
            if (!FurniturePresentationHandles.Equals(record.handle, handle)) return Failed(handle, "presentation_handle_mismatch");
            if (IsTerminalStatus(record.result.status)) return CopyResult(record.result);
            if (record.cleanupPending || record.cancellationRequested) return CopyResult(record.result);

            FurniturePresentationValidationResult validation = ValidateContext(handle,
                FurniturePresentationValidationPurpose.Cancel);
            if (!validation.allowed)
                return BeginFailureCleanup(record, SafeCode(validation.diagnosticCode, "presentation_sequence_unavailable"));

            FurniturePresentationResult result;
            record.cancellationRequested = true;
            try { result = record.operation.Cancel(FurniturePresentationHandles.Copy(record.handle)); }
            catch (Exception) { return BeginFailureCleanup(record, "presentation_cancel_failed"); }
            if (!IsValidResult(result, record.handle, FurniturePresentationValidationPurpose.Cancel))
                return BeginFailureCleanup(record, "invalid_presentation_cancel_result");
            if (result.status == FurniturePresentationStatuses.Failed)
                return BeginFailureCleanup(record, SafeCode(result.diagnosticCode, "presentation_cancel_failed"));
            if (result.status == FurniturePresentationStatuses.Cancelled || result.status == FurniturePresentationStatuses.Completed)
                return CompleteTerminal(record, result);

            record.result = CopyResult(result);
            activeRequestByActivity[record.handle.activityId] = record.handle.presentationRequestId;
            return CopyResult(record.result);
        }

        private FurniturePresentationResult RecordStartFailure(FurniturePresentationHandle handle,
            IFurniturePresentationOperation operation, string diagnosticCode)
        {
            var record = new OperationRecord
            {
                handle = FurniturePresentationHandles.Copy(handle),
                operation = operation,
                result = Failed(handle, diagnosticCode),
                failureDiagnosticCode = SafeCode(diagnosticCode, "presentation_start_failed")
            };
            records.Add(handle.presentationRequestId, record);
            return BeginFailureCleanup(record, record.failureDiagnosticCode);
        }

        private FurniturePresentationResult BeginFailureCleanup(OperationRecord record, string diagnosticCode)
        {
            if (string.IsNullOrWhiteSpace(record.failureDiagnosticCode))
                record.failureDiagnosticCode = SafeCode(diagnosticCode, "presentation_failed");

            if (record.cleanupPending)
                return CopyResult(record.result);

            if (record.cancellationRequested)
                return SetCleanupPending(record);

            record.cancellationRequested = true;
            FurniturePresentationResult cancelResult = null;
            try { cancelResult = record.operation.Cancel(FurniturePresentationHandles.Copy(record.handle)); }
            catch (Exception) { }

            if (IsCleanupComplete(cancelResult, record.handle))
                return CompleteFailure(record);
            return SetCleanupPending(record);
        }

        private FurniturePresentationResult CompleteOrContinueCleanup(OperationRecord record, FurniturePresentationResult result)
        {
            if (IsCleanupComplete(result, record.handle))
                return CompleteFailure(record);
            return SetCleanupPending(record);
        }

        private FurniturePresentationResult SetCleanupPending(OperationRecord record)
        {
            record.cleanupPending = true;
            record.result = Pending(record.handle, "presentation_cleanup_pending");
            activeRequestByActivity[record.handle.activityId] = record.handle.presentationRequestId;
            return CopyResult(record.result);
        }

        private FurniturePresentationResult StoreOperationResult(OperationRecord record, FurniturePresentationResult result)
        {
            record.result = CopyResult(result);
            if (IsTerminalStatus(record.result.status)) activeRequestByActivity.Remove(record.handle.activityId);
            else activeRequestByActivity[record.handle.activityId] = record.handle.presentationRequestId;
            return CopyResult(record.result);
        }

        private FurniturePresentationResult CompleteTerminal(OperationRecord record, FurniturePresentationResult result)
        {
            record.result = CopyResult(result);
            activeRequestByActivity.Remove(record.handle.activityId);
            return CopyResult(record.result);
        }

        private FurniturePresentationResult CompleteFailure(OperationRecord record)
        {
            record.cleanupPending = false;
            record.result = Failed(record.handle, record.failureDiagnosticCode);
            activeRequestByActivity.Remove(record.handle.activityId);
            return CopyResult(record.result);
        }

        private static bool IsValidResult(FurniturePresentationResult result, FurniturePresentationHandle expected,
            FurniturePresentationValidationPurpose purpose)
        {
            return result != null && FurniturePresentationHandles.Equals(result.handle, expected) && IsAllowedStatus(result.status, purpose);
        }

        private static bool IsCleanupComplete(FurniturePresentationResult result, FurniturePresentationHandle expected)
        {
            return result != null && FurniturePresentationHandles.Equals(result.handle, expected) &&
                (result.status == FurniturePresentationStatuses.Cancelled || result.status == FurniturePresentationStatuses.Completed);
        }

        private static FurniturePresentationResult Pending(FurniturePresentationHandle handle, string diagnosticCode)
        {
            return new FurniturePresentationResult
            {
                handle = FurniturePresentationHandles.Copy(handle),
                status = FurniturePresentationStatuses.Running,
                diagnosticCode = diagnosticCode
            };
        }

        private FurniturePresentationValidationResult ValidateContext(FurniturePresentationHandle handle,
            FurniturePresentationValidationPurpose purpose)
        {
            try
            {
                return contextValidator.Validate(FurniturePresentationHandles.Copy(handle), purpose) ??
                    new FurniturePresentationValidationResult { allowed = false, diagnosticCode = "presentation_validation_failed" };
            }
            catch (Exception)
            {
                return new FurniturePresentationValidationResult { allowed = false, diagnosticCode = "presentation_validation_failed" };
            }
        }

        private bool PrepareSequence(string activityId)
        {
            if (currentActivityId == activityId) return true;
            if (activeRequestByActivity.Count > 0) return false;
            records.Clear();
            activeRequestByActivity.Clear();
            currentActivityId = activityId;
            return true;
        }

        private int CountRecordsForActivity(string activityId)
        {
            int count = 0;
            foreach (OperationRecord record in records.Values)
                if (record.handle.activityId == activityId) count++;
            return count;
        }

        private bool TryGetRecord(FurniturePresentationHandle handle, out OperationRecord record)
        {
            record = null;
            return handle != null && !string.IsNullOrWhiteSpace(handle.presentationRequestId) &&
                records.TryGetValue(handle.presentationRequestId, out record);
        }

        private static FurniturePresentationHandle CreateHandle(FurniturePresentationRequest request)
        {
            if (request == null) return null;
            return new FurniturePresentationHandle
            {
                presentationRequestId = request.presentationRequestId,
                presentationDescriptorId = request.presentationDescriptorId,
                runId = request.runId,
                invocationId = request.invocationId,
                actionDefinitionRef = FurniturePresentationHandles.Copy(request.actionDefinitionRef),
                activityId = request.activityId,
                sequenceStepId = request.sequenceStepId,
                instanceId = request.instanceId,
                definitionId = request.definitionId,
                usePointId = request.usePointId,
                usePointRevision = request.usePointRevision
            };
        }

        private static bool ValidateHandle(FurniturePresentationHandle handle, out string diagnosticCode)
        {
            diagnosticCode = "invalid_presentation_request";
            if (handle == null || string.IsNullOrWhiteSpace(handle.presentationRequestId) ||
                string.IsNullOrWhiteSpace(handle.presentationDescriptorId) || string.IsNullOrWhiteSpace(handle.runId) ||
                string.IsNullOrWhiteSpace(handle.invocationId) || string.IsNullOrWhiteSpace(handle.activityId) ||
                string.IsNullOrWhiteSpace(handle.sequenceStepId) || string.IsNullOrWhiteSpace(handle.instanceId) ||
                string.IsNullOrWhiteSpace(handle.definitionId) || string.IsNullOrWhiteSpace(handle.usePointId) ||
                handle.usePointRevision < 0 || !FurniturePresentationHandles.IsPinned(handle.actionDefinitionRef))
                return false;
            diagnosticCode = string.Empty;
            return true;
        }

        private static bool IsAllowedStatus(string status, FurniturePresentationValidationPurpose purpose)
        {
            if (purpose == FurniturePresentationValidationPurpose.Start)
                return status == FurniturePresentationStatuses.Accepted || status == FurniturePresentationStatuses.Running ||
                    status == FurniturePresentationStatuses.Completed || status == FurniturePresentationStatuses.Failed;
            if (purpose == FurniturePresentationValidationPurpose.Cancel)
                return status == FurniturePresentationStatuses.Running || status == FurniturePresentationStatuses.Cancelled ||
                    status == FurniturePresentationStatuses.Completed || status == FurniturePresentationStatuses.Failed;
            return status == FurniturePresentationStatuses.Accepted || status == FurniturePresentationStatuses.Running ||
                status == FurniturePresentationStatuses.Completed || status == FurniturePresentationStatuses.Cancelled ||
                status == FurniturePresentationStatuses.Failed;
        }

        private static bool IsActiveStatus(string status)
        { return status == FurniturePresentationStatuses.Accepted || status == FurniturePresentationStatuses.Running; }

        private static bool IsTerminalStatus(string status)
        { return status == FurniturePresentationStatuses.Completed || status == FurniturePresentationStatuses.Cancelled || status == FurniturePresentationStatuses.Failed; }

        private static FurniturePresentationResult Failed(FurniturePresentationHandle handle, string code)
        {
            return new FurniturePresentationResult
            {
                handle = FurniturePresentationHandles.Copy(handle),
                status = FurniturePresentationStatuses.Failed,
                diagnosticCode = SafeCode(code, "presentation_failed")
            };
        }

        private static FurniturePresentationResult CopyResult(FurniturePresentationResult result)
        {
            return result == null ? null : new FurniturePresentationResult
            {
                handle = FurniturePresentationHandles.Copy(result.handle),
                status = result.status,
                diagnosticCode = result.diagnosticCode ?? string.Empty
            };
        }

        private static string SafeCode(string value, string fallback)
        { return string.IsNullOrWhiteSpace(value) ? fallback : value; }
    }

    /// <summary>W1A lifecycle stand-in only; W4 must report real presentation completion from its Unity adapter.</summary>
    /// <remarks>This mock has no furniture, Item, hydration, stat or currency store.</remarks>
    public sealed class DispenserCollectMockPresentation : IFurniturePresentationOperation
    {
        private FurniturePresentationHandle currentHandle;
        private string currentStatus = string.Empty;
        private int pollCount;

        public string PresentationDescriptorId { get { return FurniturePresentationDescriptorIds.DispenserCollectMock; } }

        public FurniturePresentationResult Start(FurniturePresentationHandle handle)
        {
            if (!FurniturePresentationHandles.IsValid(handle)) return Result(handle, FurniturePresentationStatuses.Failed, "invalid_presentation_handle");
            if (currentHandle != null && (currentStatus == FurniturePresentationStatuses.Accepted || currentStatus == FurniturePresentationStatuses.Running))
                return Result(handle, FurniturePresentationStatuses.Failed, "presentation_operation_already_active");

            currentHandle = FurniturePresentationHandles.Copy(handle);
            currentStatus = FurniturePresentationStatuses.Accepted;
            pollCount = 0;
            return Result(currentHandle, currentStatus, string.Empty);
        }

        public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
        {
            if (!FurniturePresentationHandles.Equals(currentHandle, handle)) return Result(handle, FurniturePresentationStatuses.Failed, "presentation_handle_mismatch");
            if (currentStatus == FurniturePresentationStatuses.Accepted)
            {
                pollCount = 1;
                currentStatus = FurniturePresentationStatuses.Running;
            }
            else if (currentStatus == FurniturePresentationStatuses.Running && pollCount == 1)
            {
                pollCount = 2;
                currentStatus = FurniturePresentationStatuses.Completed;
            }
            return Result(currentHandle, currentStatus, string.Empty);
        }

        public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
        {
            if (!FurniturePresentationHandles.Equals(currentHandle, handle)) return Result(handle, FurniturePresentationStatuses.Failed, "presentation_handle_mismatch");
            if (currentStatus == FurniturePresentationStatuses.Accepted || currentStatus == FurniturePresentationStatuses.Running)
                currentStatus = FurniturePresentationStatuses.Cancelled;
            return Result(currentHandle, currentStatus, string.Empty);
        }

        private static FurniturePresentationResult Result(FurniturePresentationHandle handle, string status, string code)
        {
            return new FurniturePresentationResult
            {
                handle = FurniturePresentationHandles.Copy(handle),
                status = status,
                diagnosticCode = code ?? string.Empty
            };
        }
    }

    internal static class FurniturePresentationHandles
    {
        public static FurniturePresentationHandle Copy(FurniturePresentationHandle handle)
        {
            return handle == null ? null : new FurniturePresentationHandle
            {
                presentationRequestId = handle.presentationRequestId,
                presentationDescriptorId = handle.presentationDescriptorId,
                runId = handle.runId,
                invocationId = handle.invocationId,
                actionDefinitionRef = Copy(handle.actionDefinitionRef),
                activityId = handle.activityId,
                sequenceStepId = handle.sequenceStepId,
                instanceId = handle.instanceId,
                definitionId = handle.definitionId,
                usePointId = handle.usePointId,
                usePointRevision = handle.usePointRevision
            };
        }

        public static ContentRef Copy(ContentRef reference)
        {
            return reference == null ? null : new ContentRef(reference.store, reference.id, reference.revision, reference.expectedHash);
        }

        public static bool IsPinned(ContentRef reference)
        {
            return reference != null && !string.IsNullOrWhiteSpace(reference.store) &&
                !string.IsNullOrWhiteSpace(reference.id) && !string.IsNullOrWhiteSpace(reference.revision) &&
                !string.IsNullOrWhiteSpace(reference.expectedHash);
        }

        public static bool IsValid(FurniturePresentationHandle handle)
        {
            return handle != null && !string.IsNullOrWhiteSpace(handle.presentationRequestId) &&
                !string.IsNullOrWhiteSpace(handle.presentationDescriptorId) && !string.IsNullOrWhiteSpace(handle.runId) &&
                !string.IsNullOrWhiteSpace(handle.invocationId) && !string.IsNullOrWhiteSpace(handle.activityId) &&
                !string.IsNullOrWhiteSpace(handle.sequenceStepId) && !string.IsNullOrWhiteSpace(handle.instanceId) &&
                !string.IsNullOrWhiteSpace(handle.definitionId) && !string.IsNullOrWhiteSpace(handle.usePointId) &&
                handle.usePointRevision >= 0 && IsPinned(handle.actionDefinitionRef);
        }

        public static bool Equals(FurniturePresentationHandle left, FurniturePresentationHandle right)
        {
            return left != null && right != null &&
                left.presentationRequestId == right.presentationRequestId &&
                left.presentationDescriptorId == right.presentationDescriptorId &&
                left.runId == right.runId && left.invocationId == right.invocationId &&
                ContentRefEquals(left.actionDefinitionRef, right.actionDefinitionRef) &&
                left.activityId == right.activityId && left.sequenceStepId == right.sequenceStepId &&
                left.instanceId == right.instanceId && left.definitionId == right.definitionId &&
                left.usePointId == right.usePointId && left.usePointRevision == right.usePointRevision;
        }

        private static bool ContentRefEquals(ContentRef left, ContentRef right)
        {
            return left != null && right != null && left.store == right.store && left.id == right.id &&
                left.revision == right.revision && left.expectedHash == right.expectedHash;
        }
    }
}
