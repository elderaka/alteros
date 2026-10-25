using System;

namespace AlterOS.Furniture
{
    public static class FurniturePowerResourceIds
    {
        public const string DevicePower = "device.power";
    }

    public enum FurniturePowerState
    {
        Off = 0,
        On = 1
    }

    [Serializable]
    public sealed class FurniturePowerArguments : FurnitureFunctionArguments
    {
        public FurniturePowerState desiredState;
    }

    [Serializable]
    public sealed class FurniturePowerCommand
    {
        public string operationId;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string activityId;
        public string sequenceStepId;
        public string instanceId;
        public string definitionId;
        public string usePointId;
        public string useId;
        public int usePointRevision;
        public string reservationOwnerId;
        public long expectedInstanceRevision;
        public long expectedLayoutRevision;
        public long expectedGameRevision;
        public FurniturePowerState desiredState;
    }

    public interface IFurniturePowerStore
    {
        FurnitureFunctionResult SetPower(FurniturePowerCommand command);
    }

    /// <summary>Typed power mutation. The injected store owns CAS, durable receipts and the power event.</summary>
    public sealed class FurniturePowerFunction : IFurnitureFunctionHandler<FurniturePowerArguments>
    {
        private readonly IFurniturePowerStore store;

        public FurniturePowerFunction(IFurniturePowerStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
        }

        public string FunctionId { get { return FurnitureFunctionIds.PowerSet; } }

        public FurnitureFunctionResult Invoke(FurnitureFunctionRequest request, FurniturePowerArguments arguments)
        {
            if (request == null || arguments == null || !Enum.IsDefined(typeof(FurniturePowerState), arguments.desiredState))
                return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Failed,
                    request == null ? string.Empty : request.operationId, "invalid_power_arguments");

            var command = new FurniturePowerCommand
            {
                operationId = request.operationId,
                runId = request.runId,
                invocationId = request.invocationId,
                stepExecutionId = request.stepExecutionId,
                activityId = request.activityId,
                sequenceStepId = request.sequenceStepId,
                instanceId = request.instanceId,
                definitionId = request.definitionId,
                usePointId = request.usePointId,
                useId = request.useId,
                usePointRevision = request.usePointRevision,
                reservationOwnerId = request.reservationOwnerId,
                expectedInstanceRevision = request.expectedInstanceRevision,
                expectedLayoutRevision = request.expectedLayoutRevision,
                expectedGameRevision = request.expectedGameRevision,
                desiredState = arguments.desiredState
            };

            FurnitureFunctionResult result;
            try { result = store.SetPower(command); }
            catch (Exception) { return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Failed, command.operationId, "furniture_power_store_failed"); }
            if (result == null || result.operationId != command.operationId)
                return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Failed, command.operationId, "invalid_power_store_result");

            if (result.status == FurnitureFunctionStatuses.Applied || result.status == FurnitureFunctionStatuses.NoChange ||
                result.status == FurnitureFunctionStatuses.Replayed)
            {
                FurnitureFunctionStateEvidence state = result.committedState;
                if (state == null || state.resourceId != FurniturePowerResourceIds.DevicePower || !state.powerKnown ||
                    state.instanceId != command.instanceId ||
                    state.definitionId != command.definitionId || state.powerState != command.desiredState ||
                    state.instanceRevision < 0 || state.layoutRevision < 0 || state.gameRevision < 0)
                    return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Failed, command.operationId,
                        "invalid_committed_state_evidence");
                return result;
            }

            if (result.status == FurnitureFunctionStatuses.Unavailable || result.status == FurnitureFunctionStatuses.Conflict ||
                result.status == FurnitureFunctionStatuses.Failed)
                return new FurnitureFunctionResult
                {
                    status = result.status,
                    operationId = command.operationId,
                    diagnosticCode = FurnitureFunctionRegistry.SafeCode(result.diagnosticCode, "furniture_power_store_failed")
                };

            return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Failed, command.operationId, "invalid_power_store_status");
        }
    }
}
