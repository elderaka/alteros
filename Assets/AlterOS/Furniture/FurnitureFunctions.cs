using System;
using System.Collections.Generic;

namespace AlterOS.Furniture
{
    /// <summary>Runtime-safe discovery boundary for the scene-owned Furniture catalogue adapter.</summary>
    public interface IFurnitureCatalogueSnapshotSource
    {
        FurnitureCatalogueSnapshot Catalogue { get; }
    }

    public static class FurnitureFunctionIds
    {
        public const string PowerSet = "power.set";
    }

    public static class FurnitureFunctionStatuses
    {
        public const string Applied = "applied";
        public const string NoChange = "no_change";
        public const string Replayed = "replayed";
        public const string Unavailable = "unavailable";
        public const string Conflict = "conflict";
        public const string Failed = "failed";
    }

    public static class FurnitureFunctionTargetStatuses
    {
        public const string Available = "available";
        public const string Unavailable = "unavailable";
        public const string Conflict = "conflict";
    }

    [Serializable]
    public abstract class FurnitureFunctionArguments
    {
    }

    [Serializable]
    public sealed class FurnitureFunctionRequest
    {
        public string functionId;
        public string instanceId;
        public string definitionId;
        public string usePointId;
        public string useId;
        public int usePointRevision;
        public string reservationOwnerId;
        public long expectedInstanceRevision;
        public long expectedLayoutRevision;
        public long expectedGameRevision;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string activityId;
        public string sequenceStepId;
        public string operationId;
        public FurnitureFunctionArguments arguments;
    }

    [Serializable]
    public sealed class FurnitureFunctionStateEvidence
    {
        public string resourceId;
        public string instanceId;
        public string definitionId;
        public bool powerKnown;
        public FurniturePowerState powerState;
        public long instanceRevision;
        public long layoutRevision;
        public long gameRevision;
    }

    [Serializable]
    public sealed class FurnitureFunctionResult
    {
        public string status;
        public string operationId;
        public FurnitureFunctionStateEvidence committedState;
        public string diagnosticCode;
    }

    public sealed class FurnitureFunctionTargetValidation
    {
        public string status = FurnitureFunctionTargetStatuses.Unavailable;
        public string diagnosticCode = string.Empty;
    }

    public interface IFurnitureFunctionTargetValidator
    {
        // The adapter checks the current catalogue opt-in, exact point binding, revision and claim owner.
        FurnitureFunctionTargetValidation Validate(FurnitureFunctionRequest request);
    }

    /// <summary>Exact physical binding query sent to the current Activity-owned claim source.</summary>
    public sealed class FurnitureFunctionReservationRequest
    {
        public readonly string activityId;
        public readonly string instanceId;
        public readonly string definitionId;
        public readonly string usePointId;
        public readonly string useId;
        public readonly int usePointRevision;

        public FurnitureFunctionReservationRequest(string activity, string instance, string definition,
            string usePoint, string use, int pointRevision)
        {
            activityId = activity ?? string.Empty;
            instanceId = instance ?? string.Empty;
            definitionId = definition ?? string.Empty;
            usePointId = usePoint ?? string.Empty;
            useId = use ?? string.Empty;
            usePointRevision = pointRevision;
        }
    }

    /// <summary>Evidence emitted only after the current owner validates its FurnitureRegistry binding.</summary>
    public sealed class FurnitureFunctionReservationEvidence
    {
        public readonly string activityId;
        public readonly string instanceId;
        public readonly string usePointId;
        public readonly string useId;
        public readonly int usePointRevision;

        public FurnitureFunctionReservationEvidence(string activity, string instance, string usePoint,
            string use, int pointRevision)
        {
            activityId = activity ?? string.Empty;
            instanceId = instance ?? string.Empty;
            usePointId = usePoint ?? string.Empty;
            useId = use ?? string.Empty;
            usePointRevision = pointRevision;
        }
    }

    public interface IFurnitureFunctionReservationContextSource
    {
        // Return null unless the current FurnitureRegistry owner verifies this exact live binding.
        FurnitureFunctionReservationEvidence ResolveCurrent(FurnitureFunctionReservationRequest request);
    }

    public interface IFurnitureFunctionSceneTargetResolver
    {
        // Re-resolve the scene point and catalogue point separately from the claim-owner proof.
        FurnitureFunctionTargetValidation ValidateCurrent(FurnitureFunctionReservationRequest request,
            string cataloguePointId);
    }

    public interface IFurnitureFunctionPort
    {
        FurnitureFunctionResult Invoke(FurnitureFunctionRequest request);
    }

    public interface IFurnitureFunctionHandler<TArguments> where TArguments : FurnitureFunctionArguments
    {
        string FunctionId { get; }
        FurnitureFunctionResult Invoke(FurnitureFunctionRequest request, TArguments arguments);
    }

    /// <summary>Trusted C# allowlist for durable furniture mutations. Presentation IDs are registered elsewhere.</summary>
    public sealed class FurnitureFunctionRegistry : IFurnitureFunctionPort
    {
        private interface IRegisteredHandler
        {
            string FunctionId { get; }
            FurnitureFunctionResult Invoke(FurnitureFunctionRequest request);
        }

        private sealed class RegisteredHandler<TArguments> : IRegisteredHandler where TArguments : FurnitureFunctionArguments
        {
            private readonly string functionId;
            private readonly IFurnitureFunctionHandler<TArguments> handler;

            public RegisteredHandler(string functionId, IFurnitureFunctionHandler<TArguments> handler)
            { this.functionId = functionId; this.handler = handler; }

            public string FunctionId { get { return functionId; } }

            public FurnitureFunctionResult Invoke(FurnitureFunctionRequest request)
            {
                TArguments arguments = request.arguments as TArguments;
                if (arguments == null) return Failure(FurnitureFunctionStatuses.Failed, request.operationId, "invalid_function_arguments");
                return handler.Invoke(request, arguments);
            }
        }

        private readonly IFurnitureFunctionTargetValidator targetValidator;
        private readonly Dictionary<string, IRegisteredHandler> handlers = new Dictionary<string, IRegisteredHandler>(StringComparer.Ordinal);

        public FurnitureFunctionRegistry(IFurnitureFunctionTargetValidator targetValidator)
        {
            if (targetValidator == null) throw new ArgumentNullException("targetValidator");
            this.targetValidator = targetValidator;
        }

        public bool Register<TArguments>(IFurnitureFunctionHandler<TArguments> handler)
            where TArguments : FurnitureFunctionArguments
        {
            if (handler == null) return false;
            string functionId = handler.FunctionId;
            if (string.IsNullOrWhiteSpace(functionId) || functionId == FurniturePresentationDescriptorIds.DispenserCollectMock ||
                handlers.ContainsKey(functionId)) return false;
            handlers.Add(functionId, new RegisteredHandler<TArguments>(functionId, handler));
            return true;
        }

        public FurnitureFunctionResult Invoke(FurnitureFunctionRequest request)
        {
            string validationCode;
            string invalidStatus;
            if (!ValidateRequest(request, out invalidStatus, out validationCode))
                return Failure(invalidStatus, request == null ? string.Empty : request.operationId, validationCode);

            IRegisteredHandler handler;
            if (!handlers.TryGetValue(request.functionId, out handler))
                return Failure(FurnitureFunctionStatuses.Unavailable, request.operationId, "furniture_function_unavailable");

            FurnitureFunctionTargetValidation target;
            try { target = targetValidator.Validate(request); }
            catch (Exception) { return Failure(FurnitureFunctionStatuses.Failed, request.operationId, "furniture_target_validation_failed"); }
            if (target == null)
                return Failure(FurnitureFunctionStatuses.Failed, request.operationId, "furniture_target_validation_failed");
            if (target.status == FurnitureFunctionTargetStatuses.Unavailable)
                return Failure(FurnitureFunctionStatuses.Unavailable, request.operationId, SafeCode(target.diagnosticCode, "furniture_function_unavailable"));
            if (target.status == FurnitureFunctionTargetStatuses.Conflict)
                return Failure(FurnitureFunctionStatuses.Conflict, request.operationId, SafeCode(target.diagnosticCode, "furniture_binding_conflict"));
            if (target.status != FurnitureFunctionTargetStatuses.Available)
                return Failure(FurnitureFunctionStatuses.Failed, request.operationId, "invalid_furniture_target_validation");

            FurnitureFunctionResult result;
            try { result = handler.Invoke(request); }
            catch (Exception) { return Failure(FurnitureFunctionStatuses.Failed, request.operationId, "furniture_function_failed"); }
            if (result == null || !IsKnownStatus(result.status) || result.operationId != request.operationId)
                return Failure(FurnitureFunctionStatuses.Failed, request.operationId, "invalid_furniture_function_result");
            return result;
        }

        private static bool ValidateRequest(FurnitureFunctionRequest request, out string status, out string code)
        {
            status = FurnitureFunctionStatuses.Failed;
            code = "invalid_furniture_function_request";
            if (request == null || string.IsNullOrWhiteSpace(request.functionId) ||
                string.IsNullOrWhiteSpace(request.instanceId) || string.IsNullOrWhiteSpace(request.definitionId) ||
                string.IsNullOrWhiteSpace(request.usePointId) || string.IsNullOrWhiteSpace(request.useId) ||
                string.IsNullOrWhiteSpace(request.runId) ||
                string.IsNullOrWhiteSpace(request.invocationId) || string.IsNullOrWhiteSpace(request.stepExecutionId) ||
                string.IsNullOrWhiteSpace(request.activityId) ||
                string.IsNullOrWhiteSpace(request.sequenceStepId) || string.IsNullOrWhiteSpace(request.operationId) ||
                request.arguments == null || request.usePointRevision < 0 || request.expectedInstanceRevision < 0 ||
                request.expectedLayoutRevision < 0 || request.expectedGameRevision < 0)
                return false;
            if (string.IsNullOrWhiteSpace(request.reservationOwnerId) || request.reservationOwnerId != request.activityId)
            {
                status = FurnitureFunctionStatuses.Conflict;
                code = "furniture_reservation_owner_mismatch";
                return false;
            }
            status = string.Empty;
            code = string.Empty;
            return true;
        }

        private static bool IsKnownStatus(string status)
        {
            return status == FurnitureFunctionStatuses.Applied || status == FurnitureFunctionStatuses.NoChange ||
                status == FurnitureFunctionStatuses.Replayed || status == FurnitureFunctionStatuses.Unavailable ||
                status == FurnitureFunctionStatuses.Conflict || status == FurnitureFunctionStatuses.Failed;
        }

        internal static FurnitureFunctionResult Failure(string status, string operationId, string diagnosticCode)
        {
            return new FurnitureFunctionResult
            {
                status = status,
                operationId = operationId ?? string.Empty,
                diagnosticCode = SafeCode(diagnosticCode, "furniture_function_failed")
            };
        }

        internal static string SafeCode(string value, string fallback)
        { return string.IsNullOrWhiteSpace(value) ? fallback : value; }
    }
}
