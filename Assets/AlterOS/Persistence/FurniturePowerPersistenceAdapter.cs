using System;
using System.Globalization;
using System.Linq;
using AlterOS.Furniture;
using Newtonsoft.Json;

namespace AlterOS.Persistence
{
    /// <summary>Persists Furniture device power through home-layout CAS and the shared operation receipt.</summary>
    public sealed class FurniturePowerPersistenceAdapter : IFurniturePowerStore
    {
        private readonly IGameRepository game;
        private readonly FurniturePersistenceAdapter furniture;

        public FurniturePowerPersistenceAdapter(IGameRepository repository, FurniturePersistenceAdapter furnitureAdapter)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (furnitureAdapter == null) throw new ArgumentNullException(nameof(furnitureAdapter));
            game = repository;
            furniture = furnitureAdapter;
        }

        public FurnitureFunctionResult SetPower(FurniturePowerCommand command)
        {
            if (!Validate(command)) return Failure(command, "furniture_power_command_invalid");
            GameCommitReceipt receipt = game.FindReceipt(command.operationId);
            if (receipt != null)
            {
                GameFurniturePowerOperationResult prior = game.FindFurniturePowerOperation(command.operationId);
                if (prior.IsFound && Matches(prior, command))
                    return Result(command, FurnitureFunctionStatuses.Replayed, prior.desiredState,
                        prior.committedInstanceRevision, prior.committedLayoutRevision, prior.committedGameRevision);
                if (prior != null && prior.state == "no_event") return ReplayNoChange(command);
                return Failure(command, "furniture_power_operation_conflict", FurnitureFunctionStatuses.Conflict);
            }

            GameLoadResult save = game.Load();
            if (save == null || save.state != GamePersistenceStates.Ready || save.identity == null ||
                string.IsNullOrWhiteSpace(save.identity.homeId))
                return Failure(command, save == null ? "game_load_unavailable" : save.errorCode ?? "game_load_unavailable");
            if (save.gameRevision != command.expectedGameRevision)
                return Failure(command, "stale_game_revision", FurnitureFunctionStatuses.Conflict);

            FurniturePersistenceLoadResult current = furniture.LoadHome(save.identity.homeId);
            if (current.state != "loaded" || current.document == null)
                return Failure(command, current.errorCode ?? "home_layout_unavailable", FurnitureFunctionStatuses.Unavailable);
            if (current.gameRevision != command.expectedGameRevision || current.layoutRevision != command.expectedLayoutRevision)
                return Failure(command, "stale_layout_revision", FurnitureFunctionStatuses.Conflict);

            HomeFurnitureLayoutInstance instance = current.document.instances.FirstOrDefault(item => item != null && item.instanceId == command.instanceId);
            if (instance == null || instance.definitionId != command.definitionId)
                return Failure(command, "furniture_instance_binding_mismatch", FurnitureFunctionStatuses.Conflict);
            if (instance.instanceRevision != command.expectedInstanceRevision)
                return Failure(command, "stale_instance_revision", FurnitureFunctionStatuses.Conflict);
            FurnitureResourceState[] powerEntries = (instance.resourceState ?? new System.Collections.Generic.List<FurnitureResourceState>())
                .Where(item => item != null && item.resourceId == FurniturePowerResourceIds.DevicePower).ToArray();
            if (powerEntries.Length > 1) return Failure(command, "furniture_power_state_ambiguous");
            if (powerEntries.Length == 1 && powerEntries[0].known &&
                (!Finite(powerEntries[0].value) || (powerEntries[0].value != 0d && powerEntries[0].value != 1d)))
                return Failure(command, "furniture_power_state_invalid");
            if (powerEntries.Length == 1 && powerEntries[0].known && powerEntries[0].value == (double)command.desiredState)
            {
                FurniturePersistenceCommitResult noChange = furniture.CommitPowerNoChange(command.operationId,
                    current.gameRevision, save.progressionJson, NoChangeOperationPayload(command));
                if (noChange.state == GamePersistenceStates.Committed)
                    return Result(command, FurnitureFunctionStatuses.NoChange, (int)command.desiredState,
                        instance.instanceRevision, current.layoutRevision, noChange.gameRevision);
                if (noChange.state == GamePersistenceStates.Duplicate)
                    return Result(command, FurnitureFunctionStatuses.Replayed, (int)command.desiredState,
                        instance.instanceRevision, current.layoutRevision, noChange.gameRevision);
                return Failure(command, noChange.errorCode ?? "furniture_power_no_change_commit_failed",
                    noChange.state == GamePersistenceStates.ConflictingOperation || noChange.state == GamePersistenceStates.StaleRevision
                        ? FurnitureFunctionStatuses.Conflict : FurnitureFunctionStatuses.Unavailable);
            }

            HomeFurnitureLayoutDocument candidate = HomeFurnitureRepository.CloneDocument(current.document);
            HomeFurnitureLayoutInstance changed = candidate.instances.First(item => item.instanceId == command.instanceId);
            if (changed.resourceState == null) changed.resourceState = new System.Collections.Generic.List<FurnitureResourceState>();
            FurnitureResourceState state = changed.resourceState.FirstOrDefault(item => item != null && item.resourceId == FurniturePowerResourceIds.DevicePower);
            if (state == null)
            {
                state = new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower };
                changed.resourceState.Add(state);
            }
            state.value = (double)command.desiredState;
            state.known = true;
            changed.instanceRevision = checked(changed.instanceRevision + 1);
            long committedLayoutRevision = checked(current.layoutRevision + 1);
            long committedGameRevision = checked(current.gameRevision + 1);
            string payload = EventPayload(command, changed.instanceRevision, committedLayoutRevision, committedGameRevision);
            string operationPayload = "furniture_power_changed|" + payload;
            FurniturePersistenceCommitResult committed = furniture.CommitPowerState(command.operationId,
                command.expectedGameRevision, command.expectedLayoutRevision, candidate, command.instanceId, payload,
                new GameEventHistoryContext
                {
                    eventId = GameOperationId.FromStableKey(command.operationId + "\nevent"),
                    runId = command.runId, invocationId = command.invocationId, stepExecutionId = command.stepExecutionId,
                    causeKind = "source_operation", causeReferenceId = command.operationId
                }, operationPayload);
            if (committed.state == GamePersistenceStates.Committed)
                return Result(command, FurnitureFunctionStatuses.Applied, (int)command.desiredState,
                    changed.instanceRevision, committed.layoutRevision, committed.gameRevision);
            if (committed.state == GamePersistenceStates.Duplicate)
            {
                GameFurniturePowerOperationResult prior = game.FindFurniturePowerOperation(command.operationId);
                return prior.IsFound && Matches(prior, command)
                    ? Result(command, FurnitureFunctionStatuses.Replayed, prior.desiredState,
                        prior.committedInstanceRevision, prior.committedLayoutRevision, prior.committedGameRevision)
                    : Failure(command, "furniture_power_operation_conflict", FurnitureFunctionStatuses.Conflict);
            }
            return Failure(command, committed.errorCode ?? "furniture_power_commit_failed",
                committed.state == GamePersistenceStates.StaleRevision ? FurnitureFunctionStatuses.Conflict : FurnitureFunctionStatuses.Unavailable);
        }

        private static bool Matches(GameFurniturePowerOperationResult prior, FurniturePowerCommand command)
        {
            return prior.operationId == command.operationId && prior.instanceId == command.instanceId &&
                prior.definitionId == command.definitionId && prior.usePointId == command.usePointId && prior.useId == command.useId &&
                prior.reservationOwnerId == command.reservationOwnerId &&
                prior.usePointRevision == command.usePointRevision && prior.desiredState == (int)command.desiredState &&
                prior.runId == command.runId && prior.invocationId == command.invocationId &&
                prior.stepExecutionId == command.stepExecutionId && prior.activityId == command.activityId &&
                prior.sequenceStepId == command.sequenceStepId && prior.expectedInstanceRevision == command.expectedInstanceRevision &&
                prior.expectedLayoutRevision == command.expectedLayoutRevision && prior.expectedGameRevision == command.expectedGameRevision;
        }

        private static string EventPayload(FurniturePowerCommand command, long instanceRevision,
            long layoutRevision, long gameRevision)
        {
            return "{\"instanceId\":" + Quote(command.instanceId) + ",\"definitionId\":" + Quote(command.definitionId) +
                ",\"usePointId\":" + Quote(command.usePointId) + ",\"useId\":" + Quote(command.useId) +
                ",\"usePointRevision\":" + command.usePointRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"reservationOwnerId\":" + Quote(command.reservationOwnerId) +
                ",\"desiredState\":" + ((int)command.desiredState).ToString(CultureInfo.InvariantCulture) +
                ",\"runId\":" + Quote(command.runId) + ",\"invocationId\":" + Quote(command.invocationId) +
                ",\"stepExecutionId\":" + Quote(command.stepExecutionId) +
                ",\"activityId\":" + Quote(command.activityId) + ",\"sequenceStepId\":" + Quote(command.sequenceStepId) +
                ",\"expectedInstanceRevision\":" + command.expectedInstanceRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedLayoutRevision\":" + command.expectedLayoutRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedGameRevision\":" + command.expectedGameRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"committedInstanceRevision\":" + instanceRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"committedLayoutRevision\":" + layoutRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"committedGameRevision\":" + gameRevision.ToString(CultureInfo.InvariantCulture) + "}";
        }

        private static string NoChangeOperationPayload(FurniturePowerCommand command)
        {
            return "furniture_power_no_change|" + EventPayload(command, command.expectedInstanceRevision,
                command.expectedLayoutRevision, command.expectedGameRevision);
        }

        private FurnitureFunctionResult ReplayNoChange(FurniturePowerCommand command)
        {
            GameLoadResult current = game.Load();
            if (current == null || current.state != GamePersistenceStates.Ready || string.IsNullOrEmpty(current.progressionJson))
                return Failure(command, current == null ? "game_load_unavailable" : current.errorCode ?? "game_load_unavailable",
                    FurnitureFunctionStatuses.Unavailable);
            FurniturePersistenceCommitResult replay = furniture.CommitPowerNoChange(command.operationId,
                current.gameRevision, current.progressionJson, NoChangeOperationPayload(command));
            if (replay.state == GamePersistenceStates.Duplicate)
                return Result(command, FurnitureFunctionStatuses.Replayed, (int)command.desiredState,
                    command.expectedInstanceRevision, replay.layoutRevision, replay.gameRevision);
            return Failure(command, replay.errorCode ?? "furniture_power_operation_conflict",
                replay.state == GamePersistenceStates.ConflictingOperation || replay.state == GamePersistenceStates.StaleRevision
                    ? FurnitureFunctionStatuses.Conflict : FurnitureFunctionStatuses.Unavailable);
        }

        private static bool Validate(FurniturePowerCommand command)
        {
            Guid operation;
            return command != null && Guid.TryParse(command.operationId, out operation) &&
                Bounded(command.runId) && Bounded(command.invocationId) && Bounded(command.activityId) &&
                Bounded(command.stepExecutionId) && Bounded(command.sequenceStepId) && Bounded(command.instanceId) && Bounded(command.definitionId) &&
                Bounded(command.usePointId) && Bounded(command.useId) && Bounded(command.reservationOwnerId) &&
                command.usePointRevision >= 0 && command.expectedInstanceRevision >= 0 &&
                command.expectedLayoutRevision >= 0 && command.expectedGameRevision >= 0 &&
                Enum.IsDefined(typeof(FurniturePowerState), command.desiredState);
        }

        private static bool Bounded(string value)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= 128; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static string Quote(string value) { return JsonConvert.ToString(value ?? string.Empty); }

        private static FurnitureFunctionResult Result(FurniturePowerCommand command, string status, int desiredState,
            long instanceRevision, long layoutRevision, long gameRevision)
        {
            return new FurnitureFunctionResult
            {
                status = status, operationId = command.operationId,
                committedState = new FurnitureFunctionStateEvidence
                {
                    resourceId = FurniturePowerResourceIds.DevicePower, instanceId = command.instanceId,
                    definitionId = command.definitionId, powerKnown = true, powerState = (FurniturePowerState)desiredState,
                    instanceRevision = instanceRevision, layoutRevision = layoutRevision, gameRevision = gameRevision
                }
            };
        }

        private static FurnitureFunctionResult Failure(FurniturePowerCommand command, string code,
            string status = null)
        {
            return new FurnitureFunctionResult { status = status ?? FurnitureFunctionStatuses.Failed,
                operationId = command == null ? string.Empty : command.operationId, diagnosticCode = code ?? "furniture_power_failed" };
        }
    }
}
