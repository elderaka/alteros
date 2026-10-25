using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using SQLite;

namespace AlterOS.Persistence
{
    public sealed class SqliteGameRepository : IGameRepository
    {
        private const int MaximumSnapshotCharacters = 1048576;
        private const int MaximumEventPayloadCharacters = 16384;
        private const int MaximumEventsPerCommit = 64;
        internal static readonly object WriterGate = new object();
        private readonly string databasePath;
        private string readyState = GamePersistenceStates.Ready;
        private string readyErrorCode = string.Empty;
        private string readyMessage = string.Empty;

        public string DatabasePath { get { return databasePath; } }

        public SqliteGameRepository(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                this.databasePath = string.Empty;
                SetUnavailable(GamePersistenceStates.Invalid, "database_path_required", "A game database path is required.");
                return;
            }

            this.databasePath = Path.GetFullPath(databasePath);
            EnsureReady();
        }

        public GameLoadResult Load()
        {
            lock (WriterGate)
            {
                if (!EnsureReady()) return FailedLoad();
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        GameSaveIdentity identity = ReadIdentity(connection);
                        long gameRevision = ReadRevision(connection, "game_revision");
                        long layoutRevision = ReadRevision(connection, "layout_revision");
                        string progressionJson = connection.ExecuteScalar<string>(
                            "SELECT payload_json FROM game_snapshots WHERE domain=?", "progression");
                        string layoutJson = connection.ExecuteScalar<string>(
                            "SELECT payload_json FROM home_layout_snapshots WHERE home_id=?", identity.homeId);
                        return new GameLoadResult
                        {
                            state = GamePersistenceStates.Ready,
                            identity = identity,
                            gameRevision = gameRevision,
                            layoutRevision = layoutRevision,
                            progressionJson = progressionJson,
                            layoutJson = layoutJson
                        };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "load_failed");
                    return FailedLoad();
                }
            }
        }

        public GameItemInventoryResult ReadPlayerItemQuantities()
        {
            lock (WriterGate)
            {
                if (!EnsureReady())
                    return new GameItemInventoryResult { state = readyState, errorCode = readyErrorCode, message = readyMessage };
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        GameSaveIdentity identity = ReadIdentity(connection);
                        string playerId = PlayerOwnerId(identity);
                        if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128)
                            return new GameItemInventoryResult { state = GamePersistenceStates.RecoveryRequired,
                                errorCode = "player_identity_invalid", message = "The save has no valid player owner identity." };
                        var result = new GameItemInventoryResult
                        {
                            state = GamePersistenceStates.Ready,
                            playerId = playerId,
                            gameRevision = ReadRevision(connection, "game_revision")
                        };
                        foreach (PlayerItemQuantityRow row in connection.Query<PlayerItemQuantityRow>(
                            "SELECT item_id,quantity FROM player_item_quantities WHERE player_id=? ORDER BY item_id", playerId))
                        {
                            if (string.IsNullOrWhiteSpace(row.item_id) || row.quantity <= 0)
                                throw new InvalidDataException("The player Item inventory contains an invalid row.");
                            result.items.Add(new GameItemQuantityView { itemId = row.item_id, quantity = row.quantity });
                        }
                        return result;
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "item_inventory_read_failed");
                    return new GameItemInventoryResult { state = GamePersistenceStates.Unavailable,
                        errorCode = readyErrorCode, message = readyMessage };
                }
            }
        }

        public GameScenarioHostEffectEvidenceResult ReadScenarioHostEffectEvidence(string runId, string invocationId,
            string effectStepExecutionId, string completedStepExecutionId)
        {
            if (!IsBoundedHistoryId(runId) || !IsBoundedHistoryId(invocationId) ||
                !IsBoundedHistoryId(effectStepExecutionId) || !IsBoundedHistoryId(completedStepExecutionId) ||
                effectStepExecutionId == completedStepExecutionId)
                return new GameScenarioHostEffectEvidenceResult { state = "invalid", errorCode = "scenario_effect_identity_invalid" };
            lock (WriterGate)
            {
                if (!EnsureReady()) return new GameScenarioHostEffectEvidenceResult
                    { state = readyState, errorCode = readyErrorCode, message = readyMessage };
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        List<ScenarioHostEffectEvidenceRow> rows = connection.Query<ScenarioHostEffectEvidenceRow>(
                            "SELECT f.flow_id,f.flow_store_id,f.flow_revision,f.flow_hash,e.step_id AS effect_step_id,e.execution_id AS effect_step_execution_id,e.outcome AS effect_outcome,d.step_id AS completed_step_id,d.execution_id AS completed_step_execution_id,d.outcome AS completed_step_outcome " +
                            "FROM flow_invocations f JOIN step_executions e ON e.invocation_id=f.invocation_id " +
                            "JOIN step_executions d ON d.invocation_id=f.invocation_id " +
                            "WHERE f.run_id=? AND f.invocation_id=? AND e.execution_id=? AND d.execution_id=?",
                            runId, invocationId, effectStepExecutionId, completedStepExecutionId);
                        if (rows.Count != 1) return new GameScenarioHostEffectEvidenceResult
                            { state = rows.Count == 0 ? "missing" : "invalid", errorCode = rows.Count == 0 ? "scenario_effect_evidence_missing" : "scenario_effect_evidence_ambiguous" };
                        ScenarioHostEffectEvidenceRow row = rows[0];
                        if (!IsBoundedHistoryId(row.flow_id) || !IsBoundedHistoryId(row.effect_step_id) ||
                            !IsBoundedHistoryId(row.completed_step_id) || row.effect_step_id == row.completed_step_id ||
                            row.completed_step_outcome != "completed")
                            return new GameScenarioHostEffectEvidenceResult
                                { state = "invalid", errorCode = "scenario_effect_evidence_invalid" };
                        return new GameScenarioHostEffectEvidenceResult
                        {
                            state = "found", flowId = row.flow_id, flowStoreId = row.flow_store_id,
                            flowRevision = row.flow_revision, flowHash = row.flow_hash, effectStepId = row.effect_step_id,
                            effectStepExecutionId = row.effect_step_execution_id, effectOutcome = row.effect_outcome ?? string.Empty,
                            completedStepId = row.completed_step_id, completedStepExecutionId = row.completed_step_execution_id,
                            completedStepOutcome = row.completed_step_outcome
                        };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "scenario_effect_evidence_read_failed");
                    return new GameScenarioHostEffectEvidenceResult
                        { state = "unavailable", errorCode = "scenario_effect_evidence_read_failed", message = exception.Message };
                }
            }
        }

        public GameItemQuantityOperationResult FindItemQuantityOperation(string operationId)
        {
            Guid parsed;
            if (!Guid.TryParse(operationId, out parsed)) return FailedItemOperation("invalid", "invalid_operation_id",
                "A UUID operation ID is required.");
            lock (WriterGate)
            {
                if (!EnsureReady()) return FailedItemOperation(readyState, readyErrorCode, readyMessage);
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        GameCommitReceipt receipt = ReadReceipt(connection, operationId);
                        if (receipt == null) return new GameItemQuantityOperationResult { state = "missing", operationId = operationId };
                        List<GameItemEventRow> events = connection.Query<GameItemEventRow>(
                            "SELECT event_id,operation_id,kind,actor_id,target_id,schema_version,payload_json FROM game_events WHERE operation_id=? AND kind='item.quantity_changed' ORDER BY history_sequence",
                            operationId);
                        if (events.Count != 1) return FailedItemOperation("invalid", "item_operation_event_ambiguous",
                            "A committed Item operation must have exactly one matching quantity event.");
                        GameItemEventRow itemEvent = events[0];
                        if (itemEvent.operation_id != operationId || itemEvent.kind != "item.quantity_changed" ||
                            string.IsNullOrWhiteSpace(itemEvent.event_id) || itemEvent.schema_version != 1 ||
                            !IsBoundedHistoryId(itemEvent.actor_id) || !IsBoundedHistoryId(itemEvent.target_id))
                            return FailedItemOperation("invalid", "item_operation_event_invalid", "The committed Item event identity is malformed.");

                        ItemQuantityEventPayload payload;
                        try
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(itemEvent.payload_json ?? string.Empty);
                            using (var stream = new MemoryStream(bytes))
                            {
                                var serializer = new DataContractJsonSerializer(typeof(ItemQuantityEventPayload));
                                payload = (ItemQuantityEventPayload)serializer.ReadObject(stream);
                                if (stream.Position != stream.Length) payload = null;
                            }
                        }
                        catch { payload = null; }
                        if (payload == null || !IsBoundedHistoryId(payload.changeId) || !IsBoundedHistoryId(payload.actionId) ||
                            !IsSha256(payload.actionRevision) ||
                            !IsSha256(payload.itemRevision) || payload.before < 0 || payload.delta <= 0 ||
                            payload.maximumQuantity <= 0 || payload.before < 0 || payload.before > long.MaxValue - payload.delta ||
                            payload.before + payload.delta > payload.maximumQuantity ||
                            !IsBoundedHistoryId(payload.requiresCompletedStepId) ||
                            !IsBoundedHistoryId(payload.completedDeliveryStepExecutionId))
                            return FailedItemOperation("invalid", "item_operation_payload_invalid", "The committed Item event payload is malformed or outside its original quantity bounds.");

                        List<GameEventContextRow> contexts = connection.Query<GameEventContextRow>(
                            "SELECT event_id,run_id,invocation_id,step_execution_id,action_execution_id,cause_kind,cause_reference_id,simulation_rule_id,simulation_rule_version,interval_start_utc,interval_end_utc FROM event_context WHERE event_id=?",
                            itemEvent.event_id);
                        if (contexts.Count != 1) return FailedItemOperation("invalid", "item_operation_context_missing",
                            "A committed Item event must have exactly one execution context.");
                        GameEventContextRow contextRow = contexts[0];
                        if (!IsBoundedHistoryId(contextRow.run_id) || !IsBoundedHistoryId(contextRow.invocation_id) ||
                            !IsBoundedHistoryId(contextRow.step_execution_id) || contextRow.cause_kind != "source_operation" ||
                            contextRow.cause_reference_id != operationId || !string.IsNullOrEmpty(contextRow.action_execution_id))
                            return FailedItemOperation("invalid", "item_operation_context_invalid",
                                "The committed Item event context does not match the operation's Scenario execution.");

                        var change = new GameItemQuantityChangeRecord
                        {
                            changeId = payload.changeId, eventId = itemEvent.event_id, ownerId = itemEvent.actor_id,
                            actionStoreId = payload.actionStoreId, actionId = payload.actionId,
                            actionRevision = payload.actionRevision, actionExpectedHash = payload.actionExpectedHash,
                            itemId = itemEvent.target_id, itemRevision = payload.itemRevision,
                            expectedQuantity = payload.before, delta = payload.delta, maximumQuantity = payload.maximumQuantity,
                            runId = contextRow.run_id, invocationId = contextRow.invocation_id, stepExecutionId = contextRow.step_execution_id,
                            requiresCompletedStepId = payload.requiresCompletedStepId,
                            completedDeliveryStepExecutionId = payload.completedDeliveryStepExecutionId
                        };
                        if (string.IsNullOrWhiteSpace(change.itemId) || !ItemQuantityEventPayloadMatches(itemEvent.payload_json, change))
                            return FailedItemOperation("invalid", "item_operation_payload_mismatch", "The committed Item event did not match its reconstructed immutable operation.");
                        var context = new GameEventHistoryContext
                        {
                            eventId = contextRow.event_id, runId = contextRow.run_id, invocationId = contextRow.invocation_id,
                            stepExecutionId = contextRow.step_execution_id, actionExecutionId = contextRow.action_execution_id,
                            causeKind = contextRow.cause_kind, causeReferenceId = contextRow.cause_reference_id,
                            simulationRuleId = contextRow.simulation_rule_id, simulationRuleVersion = contextRow.simulation_rule_version,
                            intervalStartUtc = contextRow.interval_start_utc, intervalEndUtc = contextRow.interval_end_utc
                        };
                        return new GameItemQuantityOperationResult { state = "found", operationId = operationId,
                            receipt = receipt, change = change, eventContext = context };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "item_operation_read_failed");
                    return FailedItemOperation("unavailable", "item_operation_read_failed", exception.Message);
                }
            }
        }

        public GameFurniturePowerOperationResult FindFurniturePowerOperation(string operationId)
        {
            Guid parsed;
            if (!Guid.TryParse(operationId, out parsed)) return FailedFurniturePowerOperation("invalid", "invalid_operation_id", "A UUID operation ID is required.");
            lock (WriterGate)
            {
                if (!EnsureReady()) return FailedFurniturePowerOperation(readyState, readyErrorCode, readyMessage);
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        GameCommitReceipt receipt = ReadReceipt(connection, operationId);
                        if (receipt == null) return new GameFurniturePowerOperationResult { state = "missing", operationId = operationId };
                        List<GameItemEventRow> events = connection.Query<GameItemEventRow>(
                            "SELECT event_id,operation_id,kind,actor_id,target_id,schema_version,payload_json FROM game_events WHERE operation_id=? AND kind='furniture_power_changed' ORDER BY history_sequence",
                            operationId);
                        if (events.Count == 0)
                        {
                            int anyEvents = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM game_events WHERE operation_id=?", operationId);
                            return anyEvents == 0
                                ? new GameFurniturePowerOperationResult { state = "no_event", operationId = operationId, receipt = receipt }
                                : FailedFurniturePowerOperation("invalid", "furniture_power_event_mismatch",
                                    "The operation receipt belongs to a different event kind.");
                        }
                        if (events.Count != 1) return FailedFurniturePowerOperation("invalid", "furniture_power_event_ambiguous",
                            "A committed Furniture power operation must have exactly one matching event.");
                        GameItemEventRow itemEvent = events[0];
                        FurniturePowerEventPayload payload;
                        try
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(itemEvent.payload_json ?? string.Empty);
                            using (var stream = new MemoryStream(bytes))
                            {
                                var serializer = new DataContractJsonSerializer(typeof(FurniturePowerEventPayload));
                                payload = (FurniturePowerEventPayload)serializer.ReadObject(stream);
                                if (stream.Position != stream.Length) payload = null;
                            }
                        }
                        catch { payload = null; }
                        if (itemEvent.operation_id != operationId || itemEvent.kind != "furniture_power_changed" ||
                            itemEvent.schema_version != 1 || payload == null || itemEvent.target_id != payload.instanceId ||
                            !IsBoundedHistoryId(payload.instanceId) || !IsBoundedHistoryId(payload.definitionId) ||
                            !IsBoundedHistoryId(payload.usePointId) || !IsBoundedHistoryId(payload.useId) ||
                            !IsBoundedHistoryId(payload.reservationOwnerId) ||
                            !IsBoundedHistoryId(payload.runId) || !IsBoundedHistoryId(payload.invocationId) ||
                            !IsBoundedHistoryId(payload.stepExecutionId) ||
                            !IsBoundedHistoryId(payload.activityId) || !IsBoundedHistoryId(payload.sequenceStepId) ||
                            payload.usePointRevision < 0 || (payload.desiredState != 0 && payload.desiredState != 1) ||
                            payload.expectedInstanceRevision < 0 || payload.expectedLayoutRevision < 0 || payload.expectedGameRevision < 0 ||
                            payload.committedInstanceRevision != payload.expectedInstanceRevision + 1 ||
                            payload.committedLayoutRevision != payload.expectedLayoutRevision + 1 ||
                            payload.committedGameRevision != payload.expectedGameRevision + 1)
                            return FailedFurniturePowerOperation("invalid", "furniture_power_event_invalid",
                                "The committed Furniture power event is malformed or its CAS revisions are inconsistent.");
                        List<GameEventContextRow> contexts = connection.Query<GameEventContextRow>(
                            "SELECT event_id,run_id,invocation_id,step_execution_id,action_execution_id,cause_kind,cause_reference_id,simulation_rule_id,simulation_rule_version,interval_start_utc,interval_end_utc FROM event_context WHERE event_id=?",
                            itemEvent.event_id);
                        if (contexts.Count != 1)
                            return FailedFurniturePowerOperation("invalid", "furniture_power_event_context_missing",
                                "A Furniture power event must have exactly one typed Flow execution context.");
                        GameEventContextRow contextRow = contexts[0];
                        if (contextRow.event_id != itemEvent.event_id || contextRow.run_id != payload.runId ||
                            contextRow.invocation_id != payload.invocationId || contextRow.step_execution_id != payload.stepExecutionId ||
                            contextRow.cause_kind != "source_operation" || contextRow.cause_reference_id != operationId ||
                            !string.IsNullOrEmpty(contextRow.action_execution_id))
                            return FailedFurniturePowerOperation("invalid", "furniture_power_event_context_mismatch",
                                "The typed Furniture event context does not match its source operation and Flow Step.");
                        if (connection.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM step_executions s JOIN flow_invocations f ON f.invocation_id=s.invocation_id " +
                            "WHERE s.execution_id=? AND s.invocation_id=? AND f.run_id=?",
                            contextRow.step_execution_id, contextRow.invocation_id, contextRow.run_id) != 1)
                            return FailedFurniturePowerOperation("invalid", "furniture_power_event_ancestry_mismatch",
                                "The Furniture event context does not follow a durable run-to-invocation-to-step ancestry.");
                        var eventContext = new GameEventHistoryContext
                        {
                            eventId = contextRow.event_id, runId = contextRow.run_id, invocationId = contextRow.invocation_id,
                            stepExecutionId = contextRow.step_execution_id, actionExecutionId = contextRow.action_execution_id,
                            causeKind = contextRow.cause_kind, causeReferenceId = contextRow.cause_reference_id,
                            simulationRuleId = contextRow.simulation_rule_id, simulationRuleVersion = contextRow.simulation_rule_version,
                            intervalStartUtc = contextRow.interval_start_utc, intervalEndUtc = contextRow.interval_end_utc
                        };
                        return new GameFurniturePowerOperationResult
                        {
                            state = "found", operationId = operationId, receipt = receipt,
                            instanceId = payload.instanceId, definitionId = payload.definitionId, usePointId = payload.usePointId,
                            useId = payload.useId, usePointRevision = payload.usePointRevision, desiredState = payload.desiredState,
                            reservationOwnerId = payload.reservationOwnerId,
                            committedInstanceRevision = payload.committedInstanceRevision, committedLayoutRevision = payload.committedLayoutRevision,
                            committedGameRevision = payload.committedGameRevision, runId = payload.runId, invocationId = payload.invocationId,
                            stepExecutionId = payload.stepExecutionId, activityId = payload.activityId, sequenceStepId = payload.sequenceStepId,
                            eventContext = eventContext,
                            expectedInstanceRevision = payload.expectedInstanceRevision, expectedLayoutRevision = payload.expectedLayoutRevision,
                            expectedGameRevision = payload.expectedGameRevision
                        };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "furniture_power_operation_read_failed");
                    return FailedFurniturePowerOperation("unavailable", "furniture_power_operation_read_failed", exception.Message);
                }
            }
        }

        public GameCommitResult Commit(GameCommitRequest request)
        {
            string validationError;
            if (!ValidateRequest(request, out validationError))
                return FailedCommit(GamePersistenceStates.Invalid, "invalid_commit", validationError);

            string payloadHash = ComputeRequestHash(request);
            lock (WriterGate)
            {
                if (!EnsureReady()) return FailedCommit(readyState, readyErrorCode, readyMessage);
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(false);
                    connection.BeginTransaction();

                    GameCommitReceipt previous = ReadReceipt(connection, request.operationId);
                    if (previous != null)
                    {
                        connection.Rollback();
                        bool sameCurrentRequest = string.Equals(previous.payloadHash, payloadHash, StringComparison.Ordinal);
                        bool hasFlowInvocationRecords = (request.flowInvocations ?? new List<GameFlowInvocationRecord>()).Count > 0;
                        bool invocationPinsMatch = !hasFlowInvocationRecords || PersistedFlowPinsMatch(connection, request);
                        bool sameLegacyTypedRequest = HasTypedHistory(request) && PersistedFlowPinsMatch(connection, request) &&
                            string.Equals(previous.payloadHash, ComputeRequestHash(request, true, false), StringComparison.Ordinal);
                        bool sameLegacyEnvelope = !HasTypedHistory(request) && string.Equals(
                            ReadLegacyPayloadHash(connection, request.operationId), payloadHash, StringComparison.Ordinal);
                        if (sameCurrentRequest && invocationPinsMatch || sameLegacyTypedRequest || sameLegacyEnvelope)
                            return new GameCommitResult { state = GamePersistenceStates.Duplicate, receipt = previous };
                        return FailedCommit(GamePersistenceStates.ConflictingOperation, "operation_id_reused",
                            "The operation ID was already committed with a different payload.");
                    }

                    string historyError;
                    if (!ValidateHistoryReferences(connection, request, out historyError))
                    {
                        connection.Rollback();
                        return FailedCommit(GamePersistenceStates.Invalid, "invalid_history", historyError);
                    }

                    if (!string.IsNullOrEmpty(request.migrationSourceHash) &&
                        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM migration_receipts WHERE source_hash=?",
                            request.migrationSourceHash) > 0)
                    {
                        connection.Rollback();
                        return FailedCommit(GamePersistenceStates.ConflictingOperation, "migration_already_recorded",
                            "This legacy source already has a migration receipt.");
                    }

                    long currentGameRevision = ReadRevision(connection, "game_revision");
                    long currentLayoutRevision = ReadRevision(connection, "layout_revision");
                    if (request.expectedGameRevision != currentGameRevision ||
                        (request.expectedLayoutRevision.HasValue && request.expectedLayoutRevision.Value != currentLayoutRevision))
                    {
                        connection.Rollback();
                        return FailedCommit(GamePersistenceStates.StaleRevision, "stale_revision",
                            "The durable game or home layout changed since this request was prepared.");
                    }

                    bool itemRevisionStale;
                    string itemError;
                    if (!ApplyItemQuantityChanges(connection, request, out itemRevisionStale, out itemError))
                    {
                        connection.Rollback();
                        return FailedCommit(itemRevisionStale ? GamePersistenceStates.StaleRevision : GamePersistenceStates.Invalid,
                            itemRevisionStale ? "item_quantity_stale" : "item_quantity_write_failed", itemError);
                    }

                    long nextGameRevision = checked(currentGameRevision + 1);
                    long nextLayoutRevision = request.layoutJson == null
                        ? currentLayoutRevision : checked(currentLayoutRevision + 1);

                    connection.Execute(
                        "INSERT OR REPLACE INTO game_snapshots(domain,revision,payload_json) VALUES (?,?,?)",
                        "progression", nextGameRevision, request.progressionJson);
                    if (request.layoutJson != null)
                    {
                        GameSaveIdentity identity = ReadIdentity(connection);
                        connection.Execute(
                            "INSERT OR REPLACE INTO home_layout_snapshots(home_id,revision,payload_json) VALUES (?,?,?)",
                            identity.homeId, nextLayoutRevision, request.layoutJson);
                    }

                    DateTime recordedAt = DateTime.UtcNow;
                    long nextEventSequence = connection.ExecuteScalar<long>(
                        "SELECT COALESCE(MAX(history_sequence),0)+1 FROM game_events");
                    foreach (GameEventRecord item in request.events)
                    {
                        string recordedAtUtc = string.IsNullOrEmpty(item.recordedAtUtc)
                            ? recordedAt.ToString("O", CultureInfo.InvariantCulture) : item.recordedAtUtc;
                        connection.Execute(
                            "INSERT INTO game_events(event_id,operation_id,kind,actor_id,target_id,occurred_at_utc,recorded_at_utc,schema_version,payload_json,history_sequence) VALUES (?,?,?,?,?,?,?,?,?,?)",
                            item.eventId, request.operationId, item.kind, item.actorId ?? string.Empty,
                            item.targetId ?? string.Empty, item.occurredAtUtc, recordedAtUtc,
                            item.schemaVersion, item.payloadJson, nextEventSequence++);
                    }

                    foreach (GameScenarioRunRecord item in request.scenarioRuns ?? new List<GameScenarioRunRecord>())
                    {
                        if (!string.IsNullOrEmpty(item.startedAtUtc))
                        {
                            connection.Execute(
                                "INSERT OR IGNORE INTO scenario_runs(run_id,scenario_id,started_at_utc) VALUES (?,?,?)",
                                item.runId, item.scenarioId, item.startedAtUtc);
                            if (connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM scenario_runs WHERE run_id=? AND scenario_id=? AND started_at_utc=?",
                                item.runId, item.scenarioId, item.startedAtUtc) != 1)
                                throw new InvalidDataException("A Scenario run ID already belongs to different start data.");
                        }
                        if (!string.IsNullOrEmpty(item.endedAtUtc))
                        {
                            int updated = connection.Execute(
                                "UPDATE scenario_runs SET ended_at_utc=?,outcome=? WHERE run_id=? AND ended_at_utc IS NULL",
                                item.endedAtUtc, item.outcome, item.runId);
                            if (updated != 1 && connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM scenario_runs WHERE run_id=? AND ended_at_utc=? AND outcome=?",
                                item.runId, item.endedAtUtc, item.outcome) != 1)
                                throw new InvalidDataException("A Scenario run end conflicts with its durable result.");
                        }
                    }

                    foreach (GameFlowInvocationRecord item in request.flowInvocations ?? new List<GameFlowInvocationRecord>())
                    {
                        connection.Execute(
                            "INSERT OR IGNORE INTO flow_invocations(invocation_id,run_id,parent_invocation_id,call_execution_id,flow_id,started_at_utc,flow_store_id,flow_revision,flow_hash) VALUES (?,?,?,?,?,?,?,?,?)",
                            item.invocationId, item.runId, item.parentInvocationId, item.callExecutionId, item.flowId, item.startedAtUtc,
                            item.flowStoreId ?? string.Empty, item.flowRevision ?? string.Empty, item.flowHash ?? string.Empty);
                        if (connection.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM flow_invocations WHERE invocation_id=? AND run_id=? AND COALESCE(parent_invocation_id,'')=? AND COALESCE(call_execution_id,'')=? AND flow_id=? AND started_at_utc=? AND flow_store_id=? AND flow_revision=? AND flow_hash=?",
                            item.invocationId, item.runId, item.parentInvocationId ?? string.Empty, item.callExecutionId ?? string.Empty,
                            item.flowId, item.startedAtUtc, item.flowStoreId ?? string.Empty, item.flowRevision ?? string.Empty,
                            item.flowHash ?? string.Empty) != 1)
                            throw new InvalidDataException("A Flow invocation ID already belongs to different start data.");
                    }

                    foreach (GameStepExecutionRecord item in request.stepExecutions ?? new List<GameStepExecutionRecord>())
                    {
                        if (!string.IsNullOrEmpty(item.presentedAtUtc))
                        {
                            connection.Execute(
                                "INSERT OR IGNORE INTO step_executions(execution_id,invocation_id,step_id,visit_ordinal,presented_at_utc) VALUES (?,?,?,?,?)",
                                item.executionId, item.invocationId, item.stepId, item.visitOrdinal, item.presentedAtUtc);
                            if (connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM step_executions WHERE execution_id=? AND invocation_id=? AND step_id=? AND visit_ordinal=? AND presented_at_utc=?",
                                item.executionId, item.invocationId, item.stepId, item.visitOrdinal, item.presentedAtUtc) != 1)
                                throw new InvalidDataException("A Step execution ID already belongs to different presentation data.");
                        }
                        if (!string.IsNullOrEmpty(item.completedAtUtc))
                        {
                            int updated = connection.Execute(
                                "UPDATE step_executions SET outcome=?,completed_at_utc=? WHERE execution_id=? AND outcome IS NULL",
                                item.outcome, item.completedAtUtc, item.executionId);
                            if (updated != 1 && connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM step_executions WHERE execution_id=? AND outcome=? AND completed_at_utc=?",
                                item.executionId, item.outcome, item.completedAtUtc) != 1)
                                throw new InvalidDataException("A Step execution result conflicts with its durable history.");
                        }
                    }

                    foreach (GameActionExecutionRecord item in request.actionExecutions ?? new List<GameActionExecutionRecord>())
                    {
                        if (!string.IsNullOrEmpty(item.startedAtUtc))
                        {
                            connection.Execute(
                                "INSERT OR IGNORE INTO action_executions(action_execution_id,run_id,invocation_id,step_execution_id,capability_id,status,started_at_utc) VALUES (?,?,?,?,?,?,?)",
                                item.actionExecutionId, item.runId, item.invocationId, item.stepExecutionId, item.capabilityId,
                                item.status, item.startedAtUtc);
                            if (connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM action_executions WHERE action_execution_id=? AND run_id=? AND invocation_id=? AND COALESCE(step_execution_id,'')=? AND capability_id=? AND started_at_utc=?",
                                item.actionExecutionId, item.runId, item.invocationId, item.stepExecutionId ?? string.Empty,
                                item.capabilityId, item.startedAtUtc) != 1)
                                throw new InvalidDataException("An Action execution ID already belongs to different request data.");
                        }
                        if (!string.IsNullOrEmpty(item.endedAtUtc))
                        {
                            int updated = connection.Execute(
                                "UPDATE action_executions SET status=?,ended_at_utc=? WHERE action_execution_id=? AND ended_at_utc IS NULL",
                                item.status, item.endedAtUtc, item.actionExecutionId);
                            if (updated != 1 && connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM action_executions WHERE action_execution_id=? AND status=? AND ended_at_utc=?",
                                item.actionExecutionId, item.status, item.endedAtUtc) != 1)
                                throw new InvalidDataException("An Action execution result conflicts with its durable history.");
                        }
                    }

                    foreach (GameActionCheckpointRecord item in request.actionCheckpoints ?? new List<GameActionCheckpointRecord>())
                        connection.Execute(
                            "INSERT INTO action_checkpoints(checkpoint_id,event_id,action_execution_id,outcome,occurred_at_utc) VALUES (?,?,?,?,?)",
                            item.checkpointId, item.eventId, item.actionExecutionId, item.outcome, item.occurredAtUtc);

                    foreach (GameEventHistoryContext item in request.eventContexts ?? new List<GameEventHistoryContext>())
                        connection.Execute(
                            "INSERT INTO event_context(event_id,run_id,invocation_id,step_execution_id,action_execution_id,cause_kind,cause_reference_id,simulation_rule_id,simulation_rule_version,interval_start_utc,interval_end_utc) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                            item.eventId, item.runId, item.invocationId, item.stepExecutionId, item.actionExecutionId,
                            item.causeKind, item.causeReferenceId, item.simulationRuleId, item.simulationRuleVersion,
                            item.intervalStartUtc, item.intervalEndUtc);

                    foreach (GameStatChangeRecord item in request.statChanges ?? new List<GameStatChangeRecord>())
                        connection.Execute(
                            "INSERT INTO stat_changes(change_id,event_id,owner_kind,owner_id,stat_id,before_known,before_value,after_known,after_value,applied_delta,aggregated) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                            item.changeId, item.eventId, item.ownerKind, item.ownerId, item.statId,
                            item.beforeKnown ? 1 : 0, item.beforeValue, item.afterKnown ? 1 : 0,
                            item.afterValue, item.appliedDelta, item.aggregated ? 1 : 0);

                    foreach (GameAnswerRecord item in request.answers ?? new List<GameAnswerRecord>())
                        connection.Execute(
                            "INSERT INTO answers(answer_id,event_id,step_execution_id,answer_kind,option_id,submitted_at_utc) VALUES (?,?,?,?,?,?)",
                            item.answerId, item.eventId, item.stepExecutionId, item.answerKind, item.optionId, item.submittedAtUtc);

                    UpdateMeta(connection, "game_revision", nextGameRevision);
                    UpdateMeta(connection, "layout_revision", nextLayoutRevision);
                    connection.Execute(
                        "INSERT INTO operation_receipts(operation_id,payload_hash,legacy_payload_hash,game_revision,layout_revision,result_state) VALUES (?,?,?,?,?,?)",
                        request.operationId, payloadHash, ComputeRequestHash(request, false), nextGameRevision, nextLayoutRevision, GamePersistenceStates.Committed);

                    if (!string.IsNullOrEmpty(request.migrationSourceHash))
                    {
                        connection.Execute(
                            "INSERT INTO legacy_source_backups(source_hash,source_bytes) VALUES (?,?)",
                            request.migrationSourceHash, request.migrationSourceBytes);
                        connection.Execute(
                            "INSERT INTO migration_receipts(source_hash,operation_id,imported_at_utc,game_revision) VALUES (?,?,?,?)",
                            request.migrationSourceHash, request.operationId,
                            recordedAt.ToString("O", CultureInfo.InvariantCulture), nextGameRevision);
                    }

                    connection.Commit();
                    return new GameCommitResult
                    {
                        state = GamePersistenceStates.Committed,
                        receipt = new GameCommitReceipt
                        {
                            operationId = request.operationId,
                            payloadHash = payloadHash,
                            gameRevision = nextGameRevision,
                            layoutRevision = nextLayoutRevision,
                            state = GamePersistenceStates.Committed
                        }
                    };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction)
                    {
                        try { connection.Rollback(); }
                        catch { }
                    }
                    SetFailure(exception, "commit_failed");
                    return FailedCommit(readyState, readyErrorCode, readyMessage);
                }
                finally
                {
                    if (connection != null) connection.Close();
                }
            }
        }

        public GameMigrationReceipt FindMigration(string sourceHash)
        {
            if (string.IsNullOrWhiteSpace(sourceHash)) return null;
            lock (WriterGate)
            {
                if (!EnsureReady()) return null;
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        return connection.Query<GameMigrationRow>(
                            "SELECT source_hash,operation_id,imported_at_utc,game_revision FROM migration_receipts WHERE source_hash=?",
                            sourceHash).ConvertAll(row => new GameMigrationReceipt
                            {
                                sourceHash = row.source_hash,
                                operationId = row.operation_id,
                                importedAtUtc = row.imported_at_utc,
                                gameRevision = row.game_revision
                            }).FirstOrDefault();
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "migration_receipt_read_failed");
                    return null;
                }
            }
        }

        public GameCommitReceipt FindReceipt(string operationId)
        {
            Guid parsed;
            if (!Guid.TryParse(operationId, out parsed)) return null;
            lock (WriterGate)
            {
                if (!EnsureReady()) return null;
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                        return ReadReceipt(connection, operationId);
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "operation_receipt_read_failed");
                    return null;
                }
            }
        }

        public bool CreateConsistentBackup(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(databasePath)) return false;
            string fullDestination = Path.GetFullPath(destinationPath);
            if (string.Equals(fullDestination, databasePath, StringComparison.OrdinalIgnoreCase) || File.Exists(fullDestination))
                return false;

            lock (WriterGate)
            {
                if (!EnsureReady()) return false;
                try
                {
                    string directory = Path.GetDirectoryName(fullDestination);
                    if (string.IsNullOrEmpty(directory)) return false;
                    Directory.CreateDirectory(directory);
                    using (SQLiteConnection connection = OpenConnection(false))
                        BackupWithUtf8Path(connection, fullDestination);
                    return ValidateBackup(fullDestination).state == GamePersistenceStates.Ready;
                }
                catch (Exception exception)
                {
                    SetFailure(exception, "backup_failed");
                    return false;
                }
            }
        }

        public GameBackupValidationResult ValidateBackup(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return InvalidBackup("backup_missing", "The backup file does not exist.");
            if (new FileInfo(path).Length == 0)
                return InvalidBackup("backup_empty", "The backup file is empty.");

            try
            {
                using (var connection = new SQLiteConnection(Path.GetFullPath(path), SQLiteOpenFlags.ReadOnly))
                {
                    connection.BusyTimeout = TimeSpan.FromSeconds(1);
                    int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                    if (version > GameSchemaMigrations.CurrentVersion)
                        return InvalidBackup("future_schema", "The backup was created by a newer game schema.");
                    bool supportedSchema = version == 1
                        ? GameSchemaMigrations.HasVersion1Tables(connection)
                        : version == 2
                            ? GameSchemaMigrations.HasVersion2Tables(connection)
                            : version == 3
                                ? GameSchemaMigrations.HasVersion3Tables(connection)
                                : version == 4 ? GameSchemaMigrations.HasVersion4Tables(connection)
                                : version == GameSchemaMigrations.CurrentVersion && GameSchemaMigrations.HasRequiredTables(connection);
                    if (!supportedSchema)
                        return InvalidBackup("unsupported_schema", "The backup schema is incomplete or unsupported.");
                    if (!string.Equals(connection.ExecuteScalar<string>("PRAGMA integrity_check"), "ok", StringComparison.OrdinalIgnoreCase))
                        return InvalidBackup("integrity_check_failed", "SQLite reported an integrity error.");
                    GameSaveIdentity identity = ReadIdentity(connection);
                    if (!HasIdentity(identity)) return InvalidBackup("invalid_identity", "The backup identity is incomplete.");
                    string gameRevision = ReadMeta(connection, "game_revision");
                    string layoutRevision = ReadMeta(connection, "layout_revision");
                    long parsedGameRevision;
                    long parsedLayoutRevision;
                    if (!long.TryParse(gameRevision, NumberStyles.None, CultureInfo.InvariantCulture, out parsedGameRevision) ||
                        !long.TryParse(layoutRevision, NumberStyles.None, CultureInfo.InvariantCulture, out parsedLayoutRevision))
                        return InvalidBackup("invalid_revision", "The backup revision metadata is invalid.");
                    return new GameBackupValidationResult
                    {
                        state = GamePersistenceStates.Ready,
                        backupId = Path.GetFileNameWithoutExtension(path),
                        identity = identity,
                        gameRevision = parsedGameRevision,
                        layoutRevision = parsedLayoutRevision
                    };
                }
            }
            catch (Exception exception)
            {
                return InvalidBackup("backup_invalid", SafeMessage(exception));
            }
        }

        internal bool ReplaceFromBackup(string stagedPath)
        {
            if (string.IsNullOrWhiteSpace(stagedPath) || !File.Exists(stagedPath)) return false;
            lock (WriterGate)
            {
                GameBackupValidationResult staged = ValidateBackup(stagedPath);
                if (staged.state != GamePersistenceStates.Ready) return false;
                string liveTemp = databasePath + ".restore-temp";
                string replacedOriginal = databasePath + ".restore-original";
                try
                {
                    if (File.Exists(liveTemp)) File.Delete(liveTemp);
                    if (File.Exists(replacedOriginal)) File.Delete(replacedOriginal);
                    File.Copy(stagedPath, liveTemp, false);
                    var stagedRepository = new SqliteGameRepository(liveTemp);
                    GameLoadResult migrated = stagedRepository.Load();
                    GameBackupValidationResult upgraded = stagedRepository.ValidateBackup(liveTemp);
                    if (migrated.state != GamePersistenceStates.Ready || upgraded.state != GamePersistenceStates.Ready ||
                        !SameIdentity(staged.identity, upgraded.identity) || staged.gameRevision != upgraded.gameRevision ||
                        staged.layoutRevision != upgraded.layoutRevision)
                        return false;
                    DeleteSqliteSidecars(liveTemp);
                    if (File.Exists(databasePath)) File.Replace(liveTemp, databasePath, replacedOriginal, true);
                    else File.Move(liveTemp, databasePath);
                    if (File.Exists(databasePath + "-wal")) File.Delete(databasePath + "-wal");
                    if (File.Exists(databasePath + "-shm")) File.Delete(databasePath + "-shm");
                    if (File.Exists(replacedOriginal)) File.Delete(replacedOriginal);
                    readyState = GamePersistenceStates.Ready;
                    readyErrorCode = readyMessage = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    try
                    {
                        if (!File.Exists(databasePath) && File.Exists(replacedOriginal))
                            File.Move(replacedOriginal, databasePath);
                    }
                    catch { }
                    SetFailure(exception, "restore_failed");
                    return false;
                }
                finally
                {
                    if (File.Exists(liveTemp)) File.Delete(liveTemp);
                    DeleteSqliteSidecars(liveTemp);
                }
            }
        }

        private static void DeleteSqliteSidecars(string path)
        {
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
            if (File.Exists(path + "-journal")) File.Delete(path + "-journal");
        }

        private bool EnsureReady()
        {
            if (string.IsNullOrEmpty(databasePath)) return false;
            lock (WriterGate)
            {
                bool existed = File.Exists(databasePath);
                try
                {
                    string directory = Path.GetDirectoryName(databasePath);
                    if (string.IsNullOrEmpty(directory))
                        throw new IOException("The database parent directory is unavailable.");
                    Directory.CreateDirectory(directory);
                    if (existed && new FileInfo(databasePath).Length == 0)
                    {
                        SetUnavailable(GamePersistenceStates.RecoveryRequired, "empty_database", "An existing empty database requires recovery.");
                        return false;
                    }

                    using (SQLiteConnection connection = OpenConnection(!existed))
                    {
                        int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                        if (version > GameSchemaMigrations.CurrentVersion)
                        {
                            SetUnavailable(GamePersistenceStates.RecoveryRequired, "future_schema", "The game database uses a newer schema.");
                            return false;
                        }
                    if (version == GameSchemaMigrations.CurrentVersion)
                        {
                            if (!GameSchemaMigrations.HasRequiredTables(connection) || !HasIdentity(ReadIdentity(connection)))
                            {
                                SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The game database schema or identity is incomplete.");
                                return false;
                            }
                            ReadRevision(connection, "game_revision");
                            ReadRevision(connection, "layout_revision");
                        }
                        else if (version == 1)
                        {
                            if (!GameSchemaMigrations.HasVersion1Tables(connection) || !HasIdentity(ReadIdentity(connection)))
                            {
                                SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The schema 1 game database is incomplete or its identity is invalid.");
                                return false;
                            }
                        }
                        else if (version == 2)
                        {
                            if (!GameSchemaMigrations.HasVersion2Tables(connection) || !HasIdentity(ReadIdentity(connection)))
                            {
                                SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The schema 2 game database is incomplete or its identity is invalid.");
                                return false;
                            }
                        }
                        else if (version == 3)
                        {
                            if (!GameSchemaMigrations.HasVersion3Tables(connection) || !HasIdentity(ReadIdentity(connection)))
                            {
                                SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The schema 3 game database is incomplete or its identity is invalid.");
                                return false;
                            }
                        }
                        else if (version == 4)
                        {
                            if (!GameSchemaMigrations.HasVersion4Tables(connection) || !HasIdentity(ReadIdentity(connection)))
                            {
                                SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The schema 4 game database is incomplete or its identity is invalid.");
                                return false;
                            }
                        }
                        else if (version != 0)
                        {
                            SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The game database schema is incomplete or unsupported.");
                            return false;
                        }

                        string journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("SQLite WAL mode could not be enabled.");

                        if (version == 0)
                        {
                            connection.BeginTransaction();
                            GameSchemaMigrations.CreateVersion1(connection, new GameSaveIdentity
                            {
                                saveId = NewId(), alterraId = NewId(), homeId = NewId()
                            });
                            GameSchemaMigrations.MigrateVersion1To2(connection);
                            GameSchemaMigrations.MigrateVersion2To3(connection);
                            GameSchemaMigrations.MigrateVersion3To4(connection);
                            GameSchemaMigrations.MigrateVersion4To5(connection);
                            connection.Commit();
                        }
                        else if (version == 4)
                        {
                            connection.BeginTransaction();
                            GameSchemaMigrations.MigrateVersion4To5(connection);
                            connection.Commit();
                        }
                        else if (version == 1)
                        {
                            connection.BeginTransaction();
                            GameSchemaMigrations.MigrateVersion1To2(connection);
                            GameSchemaMigrations.MigrateVersion2To3(connection);
                            GameSchemaMigrations.MigrateVersion3To4(connection);
                            GameSchemaMigrations.MigrateVersion4To5(connection);
                            connection.Commit();
                        }
                        else if (version == 2)
                        {
                            connection.BeginTransaction();
                            GameSchemaMigrations.MigrateVersion2To3(connection);
                            GameSchemaMigrations.MigrateVersion3To4(connection);
                            GameSchemaMigrations.MigrateVersion4To5(connection);
                            connection.Commit();
                        }
                        else if (version == 3)
                        {
                            connection.BeginTransaction();
                            GameSchemaMigrations.MigrateVersion3To4(connection);
                            GameSchemaMigrations.MigrateVersion4To5(connection);
                            connection.Commit();
                        }
                        else if (version != GameSchemaMigrations.CurrentVersion || !GameSchemaMigrations.HasRequiredTables(connection))
                        {
                            SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The game database schema is incomplete or unsupported.");
                            return false;
                        }

                        if (!GameSchemaMigrations.HasRequiredTables(connection))
                        {
                            SetUnavailable(GamePersistenceStates.RecoveryRequired, "unsupported_schema", "The migrated game database schema is incomplete.");
                            return false;
                        }

                        if (!HasIdentity(ReadIdentity(connection)))
                        {
                            SetUnavailable(GamePersistenceStates.RecoveryRequired, "invalid_identity", "The game database identity is incomplete.");
                            return false;
                        }
                        ReadRevision(connection, "game_revision");
                        ReadRevision(connection, "layout_revision");
                    }

                    readyState = GamePersistenceStates.Ready;
                    readyErrorCode = readyMessage = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    SetFailure(exception, existed ? "database_open_failed" : "database_create_failed");
                    return false;
                }
            }
        }

        private SQLiteConnection OpenConnection(bool createIfMissing)
        {
            SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex;
            if (createIfMissing) flags |= SQLiteOpenFlags.Create;
            var connection = new SQLiteConnection(databasePath, flags);
            try
            {
                connection.BusyTimeout = TimeSpan.FromSeconds(2);
                connection.Execute("PRAGMA foreign_keys=ON");
                connection.Execute("PRAGMA synchronous=FULL");
                return connection;
            }
            catch
            {
                // OpenConnection can fail before returning its handle to a using block.
                // Close it here so a corrupt database remains readable for recovery/export.
                try { connection.Close(); }
                catch { }
                throw;
            }
        }

        private static void BackupWithUtf8Path(SQLiteConnection source, string destinationPath)
        {
            byte[] utf8Path = Encoding.UTF8.GetBytes(destinationPath + "\0");
            IntPtr destination = IntPtr.Zero;
            SQLite3.Result openResult = SQLite3.Open(utf8Path, out destination,
                (int)(SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex), null);
            if (openResult != SQLite3.Result.OK)
            {
                string message = destination == IntPtr.Zero ? openResult.ToString() : SQLite3.GetErrmsg(destination);
                if (destination != IntPtr.Zero) SQLite3.Close(destination);
                throw new IOException("SQLite could not open the backup destination: " + message);
            }

            IntPtr backup = IntPtr.Zero;
            try
            {
                SQLite3.BusyTimeout(destination, 2000);
                backup = SQLite3.BackupInit(destination, "main", source.Handle, "main");
                if (backup == IntPtr.Zero)
                    throw new IOException("SQLite could not initialize the online backup: " + SQLite3.GetErrmsg(destination));

                SQLite3.Result stepResult = SQLite3.BackupStep(backup, -1);
                SQLite3.Result finishResult = SQLite3.BackupFinish(backup);
                backup = IntPtr.Zero;
                if (stepResult != SQLite3.Result.Done || finishResult != SQLite3.Result.OK)
                    throw new IOException("SQLite online backup failed: step=" + stepResult + ", finish=" + finishResult + ".");
            }
            finally
            {
                if (backup != IntPtr.Zero) SQLite3.BackupFinish(backup);
                SQLite3.Result closeResult = SQLite3.Close(destination);
                if (closeResult != SQLite3.Result.OK)
                    throw new IOException("SQLite could not close the backup destination: " + closeResult + ".");
            }
        }

        private static bool ApplyItemQuantityChanges(SQLiteConnection connection, GameCommitRequest request,
            out bool stale, out string error)
        {
            stale = false;
            error = string.Empty;
            List<GameItemQuantityChangeRecord> changes = request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>();
            foreach (GameItemQuantityChangeRecord change in changes)
            {
                PlayerItemQuantityRow current = connection.Query<PlayerItemQuantityRow>(
                    "SELECT item_id,quantity FROM player_item_quantities WHERE player_id=? AND item_id=?",
                    change.ownerId, change.itemId).FirstOrDefault();
                long quantity = current == null ? 0 : current.quantity;
                if (quantity != change.expectedQuantity)
                {
                    stale = true;
                    error = "The durable Item quantity changed since this grant was prepared.";
                    return false;
                }
            }

            foreach (GameItemQuantityChangeRecord change in changes)
            {
                long nextQuantity = change.expectedQuantity + change.delta;
                int changed = change.expectedQuantity == 0
                    ? connection.Execute("INSERT INTO player_item_quantities(player_id,item_id,quantity) VALUES (?,?,?)",
                        change.ownerId, change.itemId, nextQuantity)
                    : connection.Execute("UPDATE player_item_quantities SET quantity=? WHERE player_id=? AND item_id=? AND quantity=?",
                        nextQuantity, change.ownerId, change.itemId, change.expectedQuantity);
                if (changed != 1)
                {
                    stale = true;
                    error = "The durable Item quantity changed while the grant was committing.";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateRequest(GameCommitRequest request, out string error)
        {
            error = string.Empty;
            Guid operation;
            if (request == null || !Guid.TryParse(request.operationId, out operation))
            {
                error = "A host-created UUID operation ID is required.";
                return false;
            }
            if (request.expectedGameRevision < 0 || string.IsNullOrEmpty(request.progressionJson) ||
                request.progressionJson.Length > MaximumSnapshotCharacters)
            {
                error = "A non-empty progression snapshot within the supported size limit is required.";
                return false;
            }
            if (string.IsNullOrEmpty(request.operationPayload) || request.operationPayload.Length > MaximumEventPayloadCharacters)
            {
                error = "A bounded host-authored operation payload is required for idempotency.";
                return false;
            }
            int progressionSchema;
            if (!TryReadProgressionSchema(request.progressionJson, out progressionSchema) ||
                request.migrationSourceHash == null && progressionSchema != 4 ||
                request.migrationSourceHash != null && (progressionSchema < 0 || progressionSchema > 4))
            {
                error = "Progression JSON is invalid or its schema version is unsupported.";
                return false;
            }
            if ((request.layoutJson == null) != !request.expectedLayoutRevision.HasValue ||
                request.expectedLayoutRevision.HasValue && request.expectedLayoutRevision.Value < 0 ||
                request.layoutJson != null && request.layoutJson.Length > MaximumSnapshotCharacters)
            {
                error = "Layout data and its expected revision must be supplied together and within the supported size limit.";
                return false;
            }
            if ((request.migrationSourceHash == null) != (request.migrationSourceBytes == null) ||
                request.migrationSourceHash != null && !IsSha256(request.migrationSourceHash))
            {
                error = "Migration source hash and original bytes must be supplied together.";
                return false;
            }

            List<GameEventRecord> events = request.events ?? new List<GameEventRecord>();
            if (events.Count > MaximumEventsPerCommit)
            {
                error = "A commit contains too many host events.";
                return false;
            }
            var eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameEventRecord item in events)
            {
                Guid eventId;
                if (item == null || !Guid.TryParse(item.eventId, out eventId) ||
                    !eventIds.Add(item.eventId) || !string.Equals(item.operationId, request.operationId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(item.kind) || item.kind.Length > 128 || item.schemaVersion <= 0 ||
                    string.IsNullOrEmpty(item.occurredAtUtc) || string.IsNullOrEmpty(item.payloadJson) ||
                    item.payloadJson.Length > MaximumEventPayloadCharacters ||
                    !IsUtcTimestamp(item.occurredAtUtc) ||
                    !string.IsNullOrEmpty(item.recordedAtUtc) && !IsUtcTimestamp(item.recordedAtUtc))
                {
                    error = "Every event must have a unique UUID, matching operation, UTC timestamps, bounded payload, and valid schema version.";
                    return false;
                }
            }

            List<GameItemQuantityChangeRecord> itemChanges = request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>();
            var itemChangeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameItemQuantityChangeRecord change in itemChanges)
            {
                Guid changeId;
                if (change == null || !Guid.TryParse(change.changeId, out changeId) || !itemChangeIds.Add(change.changeId) ||
                    string.IsNullOrWhiteSpace(change.ownerId) || change.ownerId.Length > 128 ||
                    string.IsNullOrWhiteSpace(change.itemId) || change.itemId.Length > 128 ||
                    !IsSha256(change.itemRevision) || change.expectedQuantity < 0 || change.delta <= 0 ||
                    change.maximumQuantity <= 0 || change.delta > change.maximumQuantity ||
                    (!string.IsNullOrEmpty(change.actionStoreId) || !string.IsNullOrEmpty(change.actionExpectedHash)) &&
                        (string.IsNullOrWhiteSpace(change.actionStoreId) || !IsBoundedHistoryId(change.actionStoreId) ||
                            !IsSha256(change.actionExpectedHash)) ||
                    change.expectedQuantity > long.MaxValue - change.delta ||
                    change.expectedQuantity > change.maximumQuantity - change.delta ||
                    string.IsNullOrWhiteSpace(change.runId) || !IsBoundedHistoryId(change.runId) ||
                    string.IsNullOrWhiteSpace(change.invocationId) || !IsBoundedHistoryId(change.invocationId) ||
                    string.IsNullOrWhiteSpace(change.stepExecutionId) || !IsBoundedHistoryId(change.stepExecutionId) ||
                    string.IsNullOrWhiteSpace(change.requiresCompletedStepId) || !IsBoundedHistoryId(change.requiresCompletedStepId) ||
                    string.IsNullOrWhiteSpace(change.completedDeliveryStepExecutionId) || !IsBoundedHistoryId(change.completedDeliveryStepExecutionId) ||
                    !itemKeys.Add(change.ownerId + "\0" + change.itemId))
                {
                    error = "Item quantity changes require unique IDs, a bounded owner/Item ref, positive in-limit arithmetic, and Flow execution identity.";
                    return false;
                }
            }

            if (!ValidateHistoryRequest(request, events, out error)) return false;
            return true;
        }

        private static bool ValidateHistoryRequest(GameCommitRequest request, List<GameEventRecord> events, out string error)
        {
            error = string.Empty;
            List<GameEventHistoryContext> contexts = request.eventContexts ?? new List<GameEventHistoryContext>();
            List<GameStatChangeRecord> changes = request.statChanges ?? new List<GameStatChangeRecord>();
            List<GameActionCheckpointRecord> checkpoints = request.actionCheckpoints ?? new List<GameActionCheckpointRecord>();
            List<GameAnswerRecord> answers = request.answers ?? new List<GameAnswerRecord>();
            List<GameScenarioRunRecord> runs = request.scenarioRuns ?? new List<GameScenarioRunRecord>();
            List<GameFlowInvocationRecord> invocations = request.flowInvocations ?? new List<GameFlowInvocationRecord>();
            List<GameStepExecutionRecord> steps = request.stepExecutions ?? new List<GameStepExecutionRecord>();
            List<GameActionExecutionRecord> actions = request.actionExecutions ?? new List<GameActionExecutionRecord>();
            if (contexts.Count > MaximumEventsPerCommit || checkpoints.Count > MaximumEventsPerCommit ||
                answers.Count > MaximumEventsPerCommit || runs.Count > MaximumEventsPerCommit ||
                invocations.Count > MaximumEventsPerCommit || steps.Count > 128 || actions.Count > MaximumEventsPerCommit ||
                changes.Count > 128 || (request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>()).Count > 128)
            {
                error = "A commit contains too many event contexts, action checkpoints, answers, or stat changes.";
                return false;
            }

            var eventById = new Dictionary<string, GameEventRecord>(StringComparer.OrdinalIgnoreCase);
            var eventIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < events.Count; i++)
            {
                eventById.Add(events[i].eventId, events[i]);
                eventIndex.Add(events[i].eventId, i);
            }

            var contextByEvent = new Dictionary<string, GameEventHistoryContext>(StringComparer.OrdinalIgnoreCase);
            foreach (GameEventHistoryContext context in contexts)
            {
                if (context == null || string.IsNullOrWhiteSpace(context.eventId) || !eventById.ContainsKey(context.eventId) ||
                    contextByEvent.ContainsKey(context.eventId) ||
                    !IsBoundedHistoryId(context.runId) || !IsBoundedHistoryId(context.invocationId) ||
                    !IsBoundedHistoryId(context.stepExecutionId) || !IsBoundedHistoryId(context.actionExecutionId))
                {
                    error = "History context must uniquely reference a commit event and use bounded opaque correlation IDs.";
                    return false;
                }
                contextByEvent.Add(context.eventId, context);

                if (string.IsNullOrEmpty(context.causeKind))
                {
                    if (!string.IsNullOrEmpty(context.causeReferenceId) || !string.IsNullOrEmpty(context.simulationRuleId) ||
                        !string.IsNullOrEmpty(context.simulationRuleVersion) || !string.IsNullOrEmpty(context.intervalStartUtc) ||
                        !string.IsNullOrEmpty(context.intervalEndUtc))
                    {
                        error = "Cause details cannot be supplied without a typed cause kind.";
                        return false;
                    }
                    continue;
                }

                if (context.causeKind == "source_operation")
                {
                    if (!string.Equals(context.causeReferenceId, request.operationId, StringComparison.Ordinal))
                    {
                        error = "A source-operation cause must reference this host operation.";
                        return false;
                    }
                }
                else if (context.causeKind == "prior_event")
                {
                    int sourceIndex;
                    if (string.IsNullOrWhiteSpace(context.causeReferenceId) ||
                        string.Equals(context.causeReferenceId, context.eventId, StringComparison.OrdinalIgnoreCase) ||
                        eventIndex.TryGetValue(context.causeReferenceId, out sourceIndex) && sourceIndex >= eventIndex[context.eventId])
                    {
                        error = "An event cause must point to an earlier event and cannot create a self or forward cycle.";
                        return false;
                    }
                }
                else if (context.causeKind == "action_checkpoint")
                {
                    if (string.IsNullOrWhiteSpace(context.causeReferenceId) || string.IsNullOrWhiteSpace(context.actionExecutionId))
                    {
                        error = "An action cause must name a confirmed checkpoint and its action execution.";
                        return false;
                    }
                }
                else if (context.causeKind == "simulation_checkpoint")
                {
                    DateTimeOffset start, end;
                    if (!IsBoundedHistoryId(context.simulationRuleId) || !IsBoundedHistoryId(context.simulationRuleVersion) ||
                        !IsUtcTimestamp(context.intervalStartUtc) || !IsUtcTimestamp(context.intervalEndUtc) ||
                        !DateTimeOffset.TryParse(context.intervalStartUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start) ||
                        !DateTimeOffset.TryParse(context.intervalEndUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end) || end <= start ||
                        !string.IsNullOrEmpty(context.causeReferenceId))
                    {
                        error = "A simulation cause requires a rule/version and a positive bounded UTC interval.";
                        return false;
                    }
                }
                else
                {
                    error = "The history cause kind is unsupported.";
                    return false;
                }
            }

            foreach (GameItemQuantityChangeRecord change in request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>())
            {
                GameEventRecord changeEvent;
                GameEventHistoryContext context;
                if (!eventById.TryGetValue(change.eventId ?? string.Empty, out changeEvent) ||
                    changeEvent.kind != "item.quantity_changed" || changeEvent.targetId != change.itemId ||
                    !ItemQuantityEventPayloadMatches(changeEvent.payloadJson, change) ||
                    !contextByEvent.TryGetValue(change.eventId, out context) ||
                    context.runId != change.runId || context.invocationId != change.invocationId ||
                    context.stepExecutionId != change.stepExecutionId || context.causeKind != "source_operation" ||
                    context.causeReferenceId != request.operationId)
                {
                    error = "An Item quantity change must match its one typed event and Flow execution context.";
                    return false;
                }
            }

            var checkpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameActionCheckpointRecord checkpoint in checkpoints)
            {
                GameEventRecord checkpointEvent;
                if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.checkpointId) || !IsBoundedHistoryId(checkpoint.checkpointId) ||
                    !checkpointIds.Add(checkpoint.checkpointId) || !eventById.TryGetValue(checkpoint.eventId ?? string.Empty, out checkpointEvent) ||
                    checkpointEvent.kind != "action_checkpoint_confirmed" || !IsBoundedHistoryId(checkpoint.actionExecutionId) ||
                    (checkpoint.outcome != "succeeded" && checkpoint.outcome != "failed" && checkpoint.outcome != "cancelled" && checkpoint.outcome != "deferred") ||
                    !IsUtcTimestamp(checkpoint.occurredAtUtc))
                {
                    error = "Action checkpoints must be host-confirmed events with a bounded ID, outcome, action, and UTC time.";
                    return false;
                }
            }

            var changeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameStatChangeRecord change in changes)
            {
                GameEventHistoryContext context;
                if (change == null || !Guid.TryParse(change.changeId, out _) || !changeIds.Add(change.changeId) ||
                    !eventById.ContainsKey(change.eventId ?? string.Empty) || !contextByEvent.TryGetValue(change.eventId, out context) ||
                    string.IsNullOrEmpty(context.causeKind) || !IsSupportedOwnerKind(change.ownerKind) ||
                    string.IsNullOrWhiteSpace(change.ownerId) || !IsBoundedHistoryId(change.ownerId) || !IsSupportedStatId(change.ownerKind, change.statId) ||
                    change.beforeKnown != change.beforeValue.HasValue || change.afterKnown != change.afterValue.HasValue ||
                    change.beforeKnown && !IsFinite(change.beforeValue.Value) || change.afterKnown && !IsFinite(change.afterValue.Value) ||
                    change.beforeKnown && change.afterKnown && (!change.appliedDelta.HasValue || !IsFinite(change.appliedDelta.Value) ||
                        Math.Abs((double)change.appliedDelta.Value - (change.afterValue.Value - change.beforeValue.Value)) > 0.0001) ||
                    (!change.beforeKnown || !change.afterKnown) && change.appliedDelta.HasValue ||
                    change.aggregated && context.causeKind != "simulation_checkpoint")
                {
                    error = "Stat history must describe a supported owner/stat, preserve unknowns, and record the actual applied delta.";
                    return false;
                }
            }

            var answerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameAnswerRecord answer in answers)
            {
                GameEventHistoryContext context;
                if (answer == null || !Guid.TryParse(answer.answerId, out _) || !answerIds.Add(answer.answerId) ||
                    !eventById.ContainsKey(answer.eventId ?? string.Empty) ||
                    !contextByEvent.TryGetValue(answer.eventId, out context) ||
                    !string.Equals(context.stepExecutionId, answer.stepExecutionId, StringComparison.Ordinal) ||
                    (answer.answerKind != "option" && answer.answerKind != "free_text" && answer.answerKind != "skip" && answer.answerKind != "cancel") ||
                    answer.answerKind == "option" && string.IsNullOrWhiteSpace(answer.optionId) ||
                    answer.answerKind != "option" && !string.IsNullOrEmpty(answer.optionId) ||
                    !IsUtcTimestamp(answer.submittedAtUtc))
                {
                    error = "An answer must reference its confirmed event and Step execution, with only an allowed answer kind and optional choice ID.";
                    return false;
                }
            }

            var runIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameScenarioRunRecord run in runs)
            {
                if (run == null || string.IsNullOrWhiteSpace(run.runId) || !IsBoundedHistoryId(run.runId) || !runIds.Add(run.runId) ||
                    !IsBoundedHistoryId(run.scenarioId) || string.IsNullOrWhiteSpace(run.scenarioId) ||
                    string.IsNullOrEmpty(run.startedAtUtc) && string.IsNullOrEmpty(run.endedAtUtc) ||
                    !string.IsNullOrEmpty(run.startedAtUtc) && !IsUtcTimestamp(run.startedAtUtc) ||
                    !string.IsNullOrEmpty(run.endedAtUtc) && (!IsUtcTimestamp(run.endedAtUtc) || string.IsNullOrWhiteSpace(run.outcome)) ||
                    string.IsNullOrEmpty(run.endedAtUtc) && !string.IsNullOrEmpty(run.outcome))
                {
                    error = "Scenario run history must have a bounded ID, scenario, and valid start or end outcome.";
                    return false;
                }
            }

            var invocationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameFlowInvocationRecord invocation in invocations)
            {
                bool hasAnyPinPart = invocation != null && (!string.IsNullOrEmpty(invocation.flowStoreId) ||
                    !string.IsNullOrEmpty(invocation.flowRevision) || !string.IsNullOrEmpty(invocation.flowHash));
                if (invocation == null || string.IsNullOrWhiteSpace(invocation.invocationId) || !IsBoundedHistoryId(invocation.invocationId) ||
                    !invocationIds.Add(invocation.invocationId) || string.IsNullOrWhiteSpace(invocation.runId) ||
                    !IsBoundedHistoryId(invocation.runId) || string.IsNullOrWhiteSpace(invocation.flowId) ||
                    !IsBoundedHistoryId(invocation.flowId) || !IsBoundedHistoryId(invocation.parentInvocationId) ||
                    !IsBoundedHistoryId(invocation.callExecutionId) || !IsUtcTimestamp(invocation.startedAtUtc) ||
                    hasAnyPinPart && (string.IsNullOrWhiteSpace(invocation.flowStoreId) || !IsBoundedHistoryId(invocation.flowStoreId) ||
                        !IsSha256(invocation.flowRevision) || !IsSha256(invocation.flowHash)))
                {
                    error = "Flow invocation history must use bounded IDs and a UTC start time.";
                    return false;
                }
            }

            var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameStepExecutionRecord step in steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.executionId) || !IsBoundedHistoryId(step.executionId) ||
                    !stepIds.Add(step.executionId) || string.IsNullOrWhiteSpace(step.invocationId) ||
                    !IsBoundedHistoryId(step.invocationId) || string.IsNullOrWhiteSpace(step.stepId) ||
                    !IsBoundedHistoryId(step.stepId) || step.visitOrdinal <= 0 ||
                    string.IsNullOrEmpty(step.presentedAtUtc) && string.IsNullOrEmpty(step.completedAtUtc) ||
                    !string.IsNullOrEmpty(step.presentedAtUtc) && !IsUtcTimestamp(step.presentedAtUtc) ||
                    !string.IsNullOrEmpty(step.completedAtUtc) && (!IsUtcTimestamp(step.completedAtUtc) || string.IsNullOrWhiteSpace(step.outcome)) ||
                    string.IsNullOrEmpty(step.completedAtUtc) && !string.IsNullOrEmpty(step.outcome))
                {
                    error = "Step execution history must identify a bounded visit and valid presentation or completion time.";
                    return false;
                }
            }

            var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameActionExecutionRecord action in actions)
            {
                bool validStatus = action != null && (action.status == "requested" || action.status == "succeeded" ||
                    action.status == "failed" || action.status == "cancelled" || action.status == "deferred");
                if (action == null || string.IsNullOrWhiteSpace(action.actionExecutionId) || !IsBoundedHistoryId(action.actionExecutionId) ||
                    !actionIds.Add(action.actionExecutionId) || string.IsNullOrWhiteSpace(action.runId) || !IsBoundedHistoryId(action.runId) ||
                    string.IsNullOrWhiteSpace(action.invocationId) || !IsBoundedHistoryId(action.invocationId) ||
                    !IsBoundedHistoryId(action.stepExecutionId) || string.IsNullOrWhiteSpace(action.capabilityId) ||
                    !IsBoundedHistoryId(action.capabilityId) || !validStatus ||
                    !string.IsNullOrEmpty(action.startedAtUtc) && !IsUtcTimestamp(action.startedAtUtc) ||
                    !string.IsNullOrEmpty(action.endedAtUtc) && !IsUtcTimestamp(action.endedAtUtc) ||
                    action.status == "requested" && (string.IsNullOrEmpty(action.startedAtUtc) || !string.IsNullOrEmpty(action.endedAtUtc)) ||
                    action.status != "requested" && string.IsNullOrEmpty(action.endedAtUtc))
                {
                    error = "Action execution history must carry a typed status, capability, and bounded execution IDs.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedOwnerKind(string value)
        {
            return value == "alterra" || value == "player" || value == "home" || value == "environment";
        }

        private static bool IsSupportedStatId(string ownerKind, string statId)
        {
            return GameHistoryStatIds.IsSupported(ownerKind, statId);
        }

        private static bool IsBoundedHistoryId(string value)
        {
            return string.IsNullOrEmpty(value) || (!string.IsNullOrWhiteSpace(value) && value.Length <= 128);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool ValidateHistoryReferences(SQLiteConnection connection, GameCommitRequest request, out string error)
        {
            error = string.Empty;
            List<GameEventRecord> events = request.events ?? new List<GameEventRecord>();
            List<GameActionCheckpointRecord> checkpoints = request.actionCheckpoints ?? new List<GameActionCheckpointRecord>();
            GameSaveIdentity identity = ReadIdentity(connection);
            string playerOwnerId = PlayerOwnerId(identity);
            foreach (GameStatChangeRecord change in request.statChanges ?? new List<GameStatChangeRecord>())
            {
                string expectedOwner = change.ownerKind == "alterra" ? identity.alterraId :
                    change.ownerKind == "player" ? playerOwnerId : identity.homeId;
                if (!string.Equals(change.ownerId, expectedOwner, StringComparison.Ordinal))
                {
                    error = "The stat owner does not match the current save identity for its typed owner kind.";
                    return false;
                }
            }
            foreach (GameItemQuantityChangeRecord change in request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>())
            {
                if (!string.Equals(change.ownerId, playerOwnerId, StringComparison.Ordinal))
                {
                    error = "The Item quantity owner does not match the current save's player identity.";
                    return false;
                }
                if (!IsBoundedHistoryId(change.actionId) || !IsSha256(change.actionRevision))
                {
                    error = "An Item grant must pin the exact host-effect Action revision that authorized it.";
                    return false;
                }
                if (!HasFlowInvocationForRun(connection, request, change.invocationId, change.runId))
                {
                    error = "An Item grant must reference the matching durable Scenario run and Flow invocation.";
                    return false;
                }
                if (!HasStepExecution(connection, request, change.stepExecutionId, change.invocationId, change.runId, null, false))
                {
                    error = "An Item grant must reference its presented Flow step execution.";
                    return false;
                }
                if (!HasStepExecution(connection, request, change.completedDeliveryStepExecutionId, change.invocationId,
                    change.runId, change.requiresCompletedStepId, true))
                {
                    error = "The required Dialogue step has not completed in this Scenario run and Flow invocation.";
                    return false;
                }
            }
            foreach (GameScenarioRunRecord run in request.scenarioRuns ?? new List<GameScenarioRunRecord>())
            {
                if (!string.IsNullOrEmpty(run.startedAtUtc)) continue;
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scenario_runs WHERE run_id=?", run.runId) != 1)
                { error = "A Scenario end must reference a previously recorded run."; return false; }
            }
            foreach (GameFlowInvocationRecord invocation in request.flowInvocations ?? new List<GameFlowInvocationRecord>())
            {
                bool stagedRun = (request.scenarioRuns ?? new List<GameScenarioRunRecord>()).Any(item => item.runId == invocation.runId &&
                    !string.IsNullOrEmpty(item.startedAtUtc));
                if (!stagedRun && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scenario_runs WHERE run_id=?", invocation.runId) != 1)
                { error = "A Flow invocation must reference a durable Scenario run."; return false; }
                if (!string.IsNullOrEmpty(invocation.parentInvocationId))
                {
                    bool stagedParent = (request.flowInvocations ?? new List<GameFlowInvocationRecord>())
                        .Any(item => item.invocationId == invocation.parentInvocationId);
                    if (!stagedParent && connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM flow_invocations WHERE invocation_id=?", invocation.parentInvocationId) != 1)
                    { error = "A nested Flow invocation must reference a durable parent invocation."; return false; }
                }
            }
            foreach (GameStepExecutionRecord step in request.stepExecutions ?? new List<GameStepExecutionRecord>())
            {
                bool stagedInvocation = (request.flowInvocations ?? new List<GameFlowInvocationRecord>())
                    .Any(item => item.invocationId == step.invocationId);
                if (!stagedInvocation && connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM flow_invocations WHERE invocation_id=?", step.invocationId) != 1)
                { error = "A Step execution must reference a durable Flow invocation."; return false; }
                if (string.IsNullOrEmpty(step.presentedAtUtc) && connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM step_executions WHERE execution_id=?", step.executionId) != 1)
                { error = "A Step completion must reference a previously presented execution."; return false; }
            }
            foreach (GameActionExecutionRecord action in request.actionExecutions ?? new List<GameActionExecutionRecord>())
            {
                bool stagedRun = (request.scenarioRuns ?? new List<GameScenarioRunRecord>()).Any(item => item.runId == action.runId &&
                    !string.IsNullOrEmpty(item.startedAtUtc));
                bool stagedInvocation = (request.flowInvocations ?? new List<GameFlowInvocationRecord>()).Any(item => item.invocationId == action.invocationId);
                if (!stagedRun && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scenario_runs WHERE run_id=?", action.runId) != 1 ||
                    !stagedInvocation && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM flow_invocations WHERE invocation_id=?", action.invocationId) != 1)
                { error = "An Action execution must reference its durable Scenario and Flow invocation."; return false; }
                if (string.IsNullOrEmpty(action.startedAtUtc) && connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM action_executions WHERE action_execution_id=?", action.actionExecutionId) != 1)
                { error = "An Action result must reference a previously requested execution."; return false; }
                if (!string.IsNullOrEmpty(action.stepExecutionId))
                {
                    bool stagedStep = (request.stepExecutions ?? new List<GameStepExecutionRecord>()).Any(item => item.executionId == action.stepExecutionId);
                    if (!stagedStep && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM step_executions WHERE execution_id=?", action.stepExecutionId) != 1)
                    { error = "An Action execution must reference its durable Step visit."; return false; }
                }
            }
            foreach (GameEventHistoryContext context in request.eventContexts ?? new List<GameEventHistoryContext>())
            {
                if (!string.IsNullOrEmpty(context.runId))
                {
                    bool stagedRun = (request.scenarioRuns ?? new List<GameScenarioRunRecord>()).Any(item => item.runId == context.runId &&
                        !string.IsNullOrEmpty(item.startedAtUtc));
                    if (!stagedRun && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scenario_runs WHERE run_id=?", context.runId) != 1)
                    { error = "Event context references a missing Scenario run."; return false; }
                }
                if (!string.IsNullOrEmpty(context.invocationId))
                {
                    bool stagedInvocation = (request.flowInvocations ?? new List<GameFlowInvocationRecord>()).Any(item => item.invocationId == context.invocationId);
                    if (!stagedInvocation && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM flow_invocations WHERE invocation_id=?", context.invocationId) != 1)
                    { error = "Event context references a missing Flow invocation."; return false; }
                }
                if (!string.IsNullOrEmpty(context.stepExecutionId))
                {
                    bool stagedStep = (request.stepExecutions ?? new List<GameStepExecutionRecord>()).Any(item => item.executionId == context.stepExecutionId &&
                        !string.IsNullOrEmpty(item.presentedAtUtc));
                    if (!stagedStep && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM step_executions WHERE execution_id=?", context.stepExecutionId) != 1)
                    { error = "Event context references a missing Step execution."; return false; }
                }
                if (!string.IsNullOrEmpty(context.actionExecutionId))
                {
                    bool stagedAction = (request.actionExecutions ?? new List<GameActionExecutionRecord>())
                        .Any(item => item.actionExecutionId == context.actionExecutionId && !string.IsNullOrEmpty(item.startedAtUtc));
                    if (!stagedAction && connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM action_executions WHERE action_execution_id=?", context.actionExecutionId) != 1)
                    { error = "Event context references a missing Action execution."; return false; }
                }
                if (context.causeKind == "prior_event")
                {
                    bool sameCommitEarlier = false;
                    int causeIndex = -1, eventIndex = -1;
                    for (int i = 0; i < events.Count; i++)
                    {
                        if (string.Equals(events[i].eventId, context.causeReferenceId, StringComparison.OrdinalIgnoreCase)) causeIndex = i;
                        if (string.Equals(events[i].eventId, context.eventId, StringComparison.OrdinalIgnoreCase)) eventIndex = i;
                    }
                    sameCommitEarlier = causeIndex >= 0 && causeIndex < eventIndex;
                    bool persisted = connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM game_events WHERE event_id=?", context.causeReferenceId) > 0;
                    if (!sameCommitEarlier && !persisted)
                    {
                        error = "A prior-event cause must reference a durable earlier event.";
                        return false;
                    }
                }
                else if (context.causeKind == "action_checkpoint")
                {
                    GameActionCheckpointRecord staged = checkpoints.Find(item =>
                        string.Equals(item.checkpointId, context.causeReferenceId, StringComparison.OrdinalIgnoreCase));
                    bool confirmed = staged != null && staged.outcome == "succeeded" &&
                        string.Equals(staged.actionExecutionId, context.actionExecutionId, StringComparison.Ordinal);
                    if (confirmed)
                    {
                        int checkpointEventIndex = -1, effectEventIndex = -1;
                        for (int i = 0; i < events.Count; i++)
                        {
                            if (string.Equals(events[i].eventId, staged.eventId, StringComparison.OrdinalIgnoreCase)) checkpointEventIndex = i;
                            if (string.Equals(events[i].eventId, context.eventId, StringComparison.OrdinalIgnoreCase)) effectEventIndex = i;
                        }
                        confirmed = checkpointEventIndex < effectEventIndex;
                    }
                    if (!confirmed)
                    {
                        int count = connection.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM action_checkpoints WHERE checkpoint_id=? AND action_execution_id=? AND outcome='succeeded'",
                            context.causeReferenceId, context.actionExecutionId);
                        confirmed = count == 1;
                    }
                    if (confirmed)
                    {
                        GameActionExecutionRecord stagedAction = (request.actionExecutions ?? new List<GameActionExecutionRecord>()).FirstOrDefault(item =>
                            string.Equals(item.actionExecutionId, context.actionExecutionId, StringComparison.OrdinalIgnoreCase));
                        bool succeeded = stagedAction != null && stagedAction.status == "succeeded";
                        if (!succeeded)
                            succeeded = connection.ExecuteScalar<int>(
                                "SELECT COUNT(*) FROM action_executions WHERE action_execution_id=? AND status='succeeded'",
                                context.actionExecutionId) == 1;
                        confirmed = succeeded;
                    }
                    if (!confirmed)
                    {
                        error = "The referenced action checkpoint is absent, unsuccessful, or belongs to another action.";
                        return false;
                    }
                }
                GameEventRecord typedEvent = events.FirstOrDefault(item =>
                    string.Equals(item.eventId, context.eventId, StringComparison.Ordinal));
                if (typedEvent != null && typedEvent.kind == "furniture_power_changed" &&
                    (string.IsNullOrWhiteSpace(context.stepExecutionId) ||
                     !HasStepExecution(connection, request, context.stepExecutionId, context.invocationId,
                        context.runId, null, false)))
                {
                    error = "A Furniture power event must reference a Step execution under its actual Scenario run and invocation.";
                    return false;
                }
            }
            return true;
        }

        private static bool HasFlowInvocationForRun(SQLiteConnection connection, GameCommitRequest request,
            string invocationId, string runId)
        {
            GameFlowInvocationRecord staged = (request.flowInvocations ?? new List<GameFlowInvocationRecord>())
                .FirstOrDefault(item => string.Equals(item.invocationId, invocationId, StringComparison.Ordinal));
            if (staged != null) return string.Equals(staged.runId, runId, StringComparison.Ordinal);
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM flow_invocations WHERE invocation_id=? AND run_id=?", invocationId, runId) == 1;
        }

        private static bool HasStepExecution(SQLiteConnection connection, GameCommitRequest request,
            string executionId, string invocationId, string runId, string requiredStepId, bool requireCompleted)
        {
            if (!HasFlowInvocationForRun(connection, request, invocationId, runId)) return false;
            GameStepExecutionRecord staged = (request.stepExecutions ?? new List<GameStepExecutionRecord>())
                .FirstOrDefault(item => string.Equals(item.executionId, executionId, StringComparison.Ordinal));
            if (staged != null)
            {
                if (!string.Equals(staged.invocationId, invocationId, StringComparison.Ordinal) ||
                    !string.IsNullOrEmpty(requiredStepId) && !string.Equals(staged.stepId, requiredStepId, StringComparison.Ordinal) ||
                    requireCompleted && (staged.outcome != "completed" || string.IsNullOrEmpty(staged.completedAtUtc)))
                    return false;
                if (!string.IsNullOrEmpty(staged.presentedAtUtc)) return true;
            }
            string sql = "SELECT COUNT(*) FROM step_executions s JOIN flow_invocations f ON f.invocation_id=s.invocation_id " +
                "WHERE s.execution_id=? AND s.invocation_id=? AND f.run_id=?";
            if (!string.IsNullOrEmpty(requiredStepId)) sql += " AND s.step_id=?";
            // A completion-only staged record is applied later in this same transaction.
            // Here, prove its durable presentation identity and parent run; require an
            // already-completed row only when the request carries no staged completion.
            if (requireCompleted && staged == null) sql += " AND s.outcome='completed' AND s.completed_at_utc IS NOT NULL";
            int count = string.IsNullOrEmpty(requiredStepId)
                ? connection.ExecuteScalar<int>(sql, executionId, invocationId, runId)
                : connection.ExecuteScalar<int>(sql, executionId, invocationId, runId, requiredStepId);
            return count == 1;
        }

        private static bool ItemQuantityEventPayloadMatches(string payloadJson, GameItemQuantityChangeRecord change)
        {
            if (string.IsNullOrEmpty(payloadJson) || change.expectedQuantity > long.MaxValue - change.delta) return false;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payloadJson);
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ItemQuantityEventPayload));
                    var payload = (ItemQuantityEventPayload)serializer.ReadObject(stream);
                    return stream.Position == stream.Length && payload != null && payload.changeId == change.changeId &&
                        payload.actionStoreId == change.actionStoreId && payload.actionExpectedHash == change.actionExpectedHash &&
                        payload.actionId == change.actionId && payload.actionRevision == change.actionRevision &&
                        payload.itemId == change.itemId &&
                        payload.itemRevision == change.itemRevision && payload.before == change.expectedQuantity &&
                        payload.delta == change.delta && payload.after == change.expectedQuantity + change.delta &&
                        payload.maximumQuantity == change.maximumQuantity &&
                        payload.requiresCompletedStepId == change.requiresCompletedStepId &&
                        payload.completedDeliveryStepExecutionId == change.completedDeliveryStepExecutionId;
                }
            }
            catch { return false; }
        }

        private static bool HasTypedHistory(GameCommitRequest request)
        {
            return request != null && ((request.eventContexts != null && request.eventContexts.Count > 0) ||
                (request.statChanges != null && request.statChanges.Count > 0) ||
                (request.actionCheckpoints != null && request.actionCheckpoints.Count > 0) ||
                (request.answers != null && request.answers.Count > 0) ||
                (request.scenarioRuns != null && request.scenarioRuns.Count > 0) ||
                (request.flowInvocations != null && request.flowInvocations.Count > 0) ||
                (request.stepExecutions != null && request.stepExecutions.Count > 0) ||
                (request.actionExecutions != null && request.actionExecutions.Count > 0) ||
                (request.itemQuantityChanges != null && request.itemQuantityChanges.Count > 0));
        }

        private static string ComputeRequestHash(GameCommitRequest request, bool includeHistory = true)
        { return ComputeRequestHash(request, includeHistory, true); }

        private static string ComputeRequestHash(GameCommitRequest request, bool includeHistory, bool includeContentReferences)
        {
            var content = new StringBuilder();
            bool hasHistory = includeHistory && HasTypedHistory(request);
            if (hasHistory)
            {
                bool hasContentReferences = includeContentReferences &&
                    ((request.flowInvocations ?? new List<GameFlowInvocationRecord>()).Any(item => item != null && (!string.IsNullOrEmpty(item.flowStoreId) ||
                        !string.IsNullOrEmpty(item.flowRevision) || !string.IsNullOrEmpty(item.flowHash))) ||
                     (request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>()).Any(item => item != null &&
                        (!string.IsNullOrEmpty(item.actionStoreId) || !string.IsNullOrEmpty(item.actionExpectedHash))));
                AppendPart(content, hasContentReferences ? "alteros-game-history-hash-v3" : "alteros-game-history-hash-v2");
            }
            AppendPart(content, request.operationPayload);
            AppendPart(content, request.migrationSourceHash);
            AppendPart(content, request.migrationSourceBytes == null ? null : Convert.ToBase64String(request.migrationSourceBytes));
            // A retry may receive a fresh audit event ID/time; its host-authored effect payload must still match.
            foreach (GameEventRecord item in request.events ?? new List<GameEventRecord>())
            {
                AppendPart(content, item.operationId);
                AppendPart(content, item.kind);
                AppendPart(content, item.actorId);
                AppendPart(content, item.targetId);
                AppendPart(content, item.schemaVersion.ToString(CultureInfo.InvariantCulture));
                AppendPart(content, item.payloadJson);
            }
            if (hasHistory)
            {
                foreach (GameEventHistoryContext item in request.eventContexts ?? new List<GameEventHistoryContext>())
                {
                    AppendPart(content, item.eventId); AppendPart(content, item.runId); AppendPart(content, item.invocationId);
                    AppendPart(content, item.stepExecutionId); AppendPart(content, item.actionExecutionId);
                    AppendPart(content, item.causeKind); AppendPart(content, item.causeReferenceId);
                    AppendPart(content, item.simulationRuleId); AppendPart(content, item.simulationRuleVersion);
                    AppendPart(content, item.intervalStartUtc); AppendPart(content, item.intervalEndUtc);
                }
                foreach (GameStatChangeRecord item in request.statChanges ?? new List<GameStatChangeRecord>())
                {
                    AppendPart(content, item.changeId); AppendPart(content, item.eventId); AppendPart(content, item.ownerKind);
                    AppendPart(content, item.ownerId); AppendPart(content, item.statId);
                    AppendPart(content, item.beforeKnown ? "1" : "0"); AppendPart(content, FloatPart(item.beforeValue));
                    AppendPart(content, item.afterKnown ? "1" : "0"); AppendPart(content, FloatPart(item.afterValue));
                    AppendPart(content, FloatPart(item.appliedDelta)); AppendPart(content, item.aggregated ? "1" : "0");
                }
                foreach (GameActionCheckpointRecord item in request.actionCheckpoints ?? new List<GameActionCheckpointRecord>())
                {
                    AppendPart(content, item.checkpointId); AppendPart(content, item.eventId); AppendPart(content, item.actionExecutionId);
                    AppendPart(content, item.outcome); AppendPart(content, item.occurredAtUtc);
                }
                foreach (GameAnswerRecord item in request.answers ?? new List<GameAnswerRecord>())
                {
                    AppendPart(content, item.answerId); AppendPart(content, item.eventId); AppendPart(content, item.stepExecutionId);
                    AppendPart(content, item.answerKind); AppendPart(content, item.optionId); AppendPart(content, item.submittedAtUtc);
                }
                foreach (GameScenarioRunRecord item in request.scenarioRuns ?? new List<GameScenarioRunRecord>())
                {
                    AppendPart(content, item.runId); AppendPart(content, item.scenarioId);
                    AppendPart(content, item.startedAtUtc); AppendPart(content, item.endedAtUtc); AppendPart(content, item.outcome);
                }
                foreach (GameFlowInvocationRecord item in request.flowInvocations ?? new List<GameFlowInvocationRecord>())
                {
                    AppendPart(content, item.invocationId); AppendPart(content, item.runId); AppendPart(content, item.parentInvocationId);
                    AppendPart(content, item.callExecutionId); AppendPart(content, item.flowId); AppendPart(content, item.startedAtUtc);
                    if (includeContentReferences && (!string.IsNullOrEmpty(item.flowStoreId) ||
                        !string.IsNullOrEmpty(item.flowRevision) || !string.IsNullOrEmpty(item.flowHash)))
                    {
                        AppendPart(content, item.flowStoreId); AppendPart(content, item.flowRevision); AppendPart(content, item.flowHash);
                    }
                }
                foreach (GameStepExecutionRecord item in request.stepExecutions ?? new List<GameStepExecutionRecord>())
                {
                    AppendPart(content, item.executionId); AppendPart(content, item.invocationId); AppendPart(content, item.stepId);
                    AppendPart(content, item.visitOrdinal.ToString(CultureInfo.InvariantCulture)); AppendPart(content, item.presentedAtUtc);
                    AppendPart(content, item.outcome); AppendPart(content, item.completedAtUtc);
                }
                foreach (GameActionExecutionRecord item in request.actionExecutions ?? new List<GameActionExecutionRecord>())
                {
                    AppendPart(content, item.actionExecutionId); AppendPart(content, item.runId); AppendPart(content, item.invocationId);
                    AppendPart(content, item.stepExecutionId); AppendPart(content, item.capabilityId); AppendPart(content, item.status);
                    AppendPart(content, item.startedAtUtc); AppendPart(content, item.endedAtUtc);
                }
                foreach (GameItemQuantityChangeRecord item in request.itemQuantityChanges ?? new List<GameItemQuantityChangeRecord>())
                {
                    AppendPart(content, item.changeId); AppendPart(content, item.eventId); AppendPart(content, item.ownerId);
                    AppendPart(content, item.actionId); AppendPart(content, item.actionRevision);
                    if (includeContentReferences && (!string.IsNullOrEmpty(item.actionStoreId) || !string.IsNullOrEmpty(item.actionExpectedHash)))
                    {
                        AppendPart(content, item.actionStoreId); AppendPart(content, item.actionExpectedHash);
                    }
                    AppendPart(content, item.itemId); AppendPart(content, item.itemRevision);
                    AppendPart(content, item.expectedQuantity.ToString(CultureInfo.InvariantCulture));
                    AppendPart(content, item.delta.ToString(CultureInfo.InvariantCulture));
                    AppendPart(content, item.maximumQuantity.ToString(CultureInfo.InvariantCulture));
                    AppendPart(content, item.runId); AppendPart(content, item.invocationId); AppendPart(content, item.stepExecutionId);
                    AppendPart(content, item.requiresCompletedStepId); AppendPart(content, item.completedDeliveryStepExecutionId);
                }
            }
            using (SHA256 sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(content.ToString())));
        }

        private static string ReadLegacyPayloadHash(SQLiteConnection connection, string operationId)
        {
            try
            {
                return connection.ExecuteScalar<string>(
                    "SELECT legacy_payload_hash FROM operation_receipts WHERE operation_id=?", operationId);
            }
            catch { return null; }
        }

        private static bool PersistedFlowPinsMatch(SQLiteConnection connection, GameCommitRequest request)
        {
            foreach (GameFlowInvocationRecord invocation in request.flowInvocations ?? new List<GameFlowInvocationRecord>())
            {
                List<FlowInvocationPinRow> rows = connection.Query<FlowInvocationPinRow>(
                    "SELECT flow_store_id,flow_revision,flow_hash FROM flow_invocations WHERE invocation_id=?",
                    invocation.invocationId);
                if (rows.Count != 1 || rows[0].flow_store_id != (invocation.flowStoreId ?? string.Empty) ||
                    rows[0].flow_revision != (invocation.flowRevision ?? string.Empty) ||
                    rows[0].flow_hash != (invocation.flowHash ?? string.Empty)) return false;
            }
            return true;
        }

        private static string FloatPart(float? value)
        {
            return value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : null;
        }

        private static void AppendPart(StringBuilder builder, string value)
        {
            if (value == null) builder.Append("-1:");
            else builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
            builder.Append('|');
        }

        private static GameCommitReceipt ReadReceipt(SQLiteConnection connection, string operationId)
        {
            List<GameReceiptRow> rows = connection.Query<GameReceiptRow>(
                "SELECT operation_id,payload_hash,game_revision,layout_revision,result_state FROM operation_receipts WHERE operation_id=?",
                operationId);
            if (rows.Count == 0) return null;
            GameReceiptRow row = rows[0];
            return new GameCommitReceipt
            {
                operationId = row.operation_id,
                payloadHash = row.payload_hash,
                gameRevision = row.game_revision,
                layoutRevision = row.layout_revision,
                state = row.result_state
            };
        }

        private static GameSaveIdentity ReadIdentity(SQLiteConnection connection)
        {
            return new GameSaveIdentity
            {
                saveId = ReadMeta(connection, "save_id"),
                alterraId = ReadMeta(connection, "alterra_id"),
                homeId = ReadMeta(connection, "home_id"),
                accountId = EmptyToNull(ReadMeta(connection, "account_id")),
                legacyPlayerId = EmptyToNull(ReadMeta(connection, "legacy_player_id"))
            };
        }

        private static long ReadRevision(SQLiteConnection connection, string key)
        {
            string value = ReadMeta(connection, key);
            long revision;
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision < 0)
                throw new InvalidDataException("The " + key + " metadata is invalid.");
            return revision;
        }

        private static string ReadMeta(SQLiteConnection connection, string key)
        {
            List<GameMetaRow> rows = connection.Query<GameMetaRow>("SELECT value FROM game_meta WHERE key=?", key);
            return rows.Count == 0 ? null : rows[0].value;
        }

        private static void UpdateMeta(SQLiteConnection connection, string key, long value)
        {
            int updated = connection.Execute("UPDATE game_meta SET value=? WHERE key=?",
                value.ToString(CultureInfo.InvariantCulture), key);
            if (updated != 1) throw new InvalidDataException("Required game metadata is missing: " + key);
        }

        private static bool HasIdentity(GameSaveIdentity identity)
        {
            Guid saveId, alterraId, homeId;
            return identity != null && Guid.TryParse(identity.saveId, out saveId) &&
                Guid.TryParse(identity.alterraId, out alterraId) && Guid.TryParse(identity.homeId, out homeId);
        }

        private static bool SameIdentity(GameSaveIdentity left, GameSaveIdentity right)
        {
            return left != null && right != null &&
                string.Equals(left.saveId, right.saveId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.alterraId, right.alterraId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.homeId, right.homeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.accountId ?? string.Empty, right.accountId ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(left.legacyPlayerId ?? string.Empty, right.legacyPlayerId ?? string.Empty, StringComparison.Ordinal);
        }

        private static string NewId() { return Guid.NewGuid().ToString("D"); }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }

        private static bool IsUtcTimestamp(string value)
        {
            DateTimeOffset timestamp;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out timestamp) && timestamp.Offset == TimeSpan.Zero;
        }

        private static bool TryReadProgressionSchema(string json, out int schemaVersion)
        {
            schemaVersion = -1;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), false))
                {
                    var header = new DataContractJsonSerializer(typeof(ProgressionHeader))
                        .ReadObject(stream) as ProgressionHeader;
                    if (header == null) return false;
                    schemaVersion = header.schemaVersion;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string PlayerOwnerId(GameSaveIdentity identity)
        { return identity.legacyPlayerId ?? identity.accountId ?? identity.saveId; }

        private static string EmptyToNull(string value) { return string.IsNullOrEmpty(value) ? null : value; }

        private static string ToLowerHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private void SetFailure(Exception exception, string fallbackCode)
        {
            var sqlite = exception as SQLiteException;
            if (sqlite != null && (sqlite.Result == SQLite3.Result.Corrupt || sqlite.Result == SQLite3.Result.NonDBFile))
                SetUnavailable(GamePersistenceStates.RecoveryRequired, sqlite.Result.ToString(), SafeMessage(exception));
            else if (sqlite != null && (sqlite.Result == SQLite3.Result.Busy || sqlite.Result == SQLite3.Result.Locked ||
                sqlite.Result == SQLite3.Result.ReadOnly || sqlite.Result == SQLite3.Result.CannotOpen))
                SetUnavailable(GamePersistenceStates.Unavailable, sqlite.Result.ToString(), SafeMessage(exception));
            else
                SetUnavailable(GamePersistenceStates.Unavailable, fallbackCode, SafeMessage(exception));
        }

        private void SetUnavailable(string state, string code, string message)
        {
            readyState = state;
            readyErrorCode = code ?? string.Empty;
            readyMessage = message ?? string.Empty;
        }

        private GameLoadResult FailedLoad()
        {
            return new GameLoadResult
            {
                state = readyState,
                errorCode = readyErrorCode,
                message = readyMessage
            };
        }

        private static GameCommitResult FailedCommit(string state, string code, string message)
        {
            return new GameCommitResult { state = state, errorCode = code, message = message };
        }

        private static GameBackupValidationResult InvalidBackup(string code, string message)
        {
            return new GameBackupValidationResult
            {
                state = GamePersistenceStates.RecoveryRequired,
                errorCode = code,
                message = message
            };
        }

        private static string SafeMessage(Exception exception)
        {
            if (exception == null) return "Unknown persistence error.";
            string message = exception.GetType().Name + ": " + exception.Message;
            return exception.InnerException == null
                ? message
                : message + " (" + exception.InnerException.GetType().Name + ": " + exception.InnerException.Message + ")";
        }

        private sealed class GameMetaRow
        {
            public string value { get; set; }
        }

        private sealed class GameReceiptRow
        {
            public string operation_id { get; set; }
            public string payload_hash { get; set; }
            public long game_revision { get; set; }
            public long layout_revision { get; set; }
            public string result_state { get; set; }
        }

        private sealed class GameMigrationRow
        {
            public string source_hash { get; set; }
            public string operation_id { get; set; }
            public string imported_at_utc { get; set; }
            public long game_revision { get; set; }
        }

        private sealed class PlayerItemQuantityRow
        {
            public string item_id { get; set; }
            public long quantity { get; set; }
        }

        [DataContract]
        private sealed class ProgressionHeader
        {
            [DataMember(Name = "schemaVersion")] public int schemaVersion;
        }

        [DataContract]
        private sealed class ItemQuantityEventPayload
        {
            [DataMember(Name = "changeId")] public string changeId;
            [DataMember(Name = "actionStoreId")] public string actionStoreId;
            [DataMember(Name = "actionId")] public string actionId;
            [DataMember(Name = "actionRevision")] public string actionRevision;
            [DataMember(Name = "actionExpectedHash")] public string actionExpectedHash;
            [DataMember(Name = "itemId")] public string itemId;
            [DataMember(Name = "itemRevision")] public string itemRevision;
            [DataMember(Name = "before")] public long before;
            [DataMember(Name = "delta")] public long delta;
            [DataMember(Name = "after")] public long after;
            [DataMember(Name = "maximumQuantity")] public long maximumQuantity;
            [DataMember(Name = "requiresCompletedStepId")] public string requiresCompletedStepId;
            [DataMember(Name = "completedDeliveryStepExecutionId")] public string completedDeliveryStepExecutionId;
        }

        private static GameItemQuantityOperationResult FailedItemOperation(string state, string code, string message)
        { return new GameItemQuantityOperationResult { state = state, errorCode = code, message = message }; }

        private static GameFurniturePowerOperationResult FailedFurniturePowerOperation(string state, string code, string message)
        { return new GameFurniturePowerOperationResult { state = state, errorCode = code, message = message }; }

        private sealed class ScenarioHostEffectEvidenceRow
        {
            public string flow_id { get; set; }
            public string flow_store_id { get; set; }
            public string flow_revision { get; set; }
            public string flow_hash { get; set; }
            public string effect_step_id { get; set; }
            public string effect_step_execution_id { get; set; }
            public string effect_outcome { get; set; }
            public string completed_step_id { get; set; }
            public string completed_step_execution_id { get; set; }
            public string completed_step_outcome { get; set; }
        }

        private sealed class FlowInvocationPinRow
        {
            public string flow_store_id { get; set; }
            public string flow_revision { get; set; }
            public string flow_hash { get; set; }
        }

        private sealed class GameItemEventRow
        {
            public string event_id { get; set; }
            public string operation_id { get; set; }
            public string kind { get; set; }
            public string actor_id { get; set; }
            public string target_id { get; set; }
            public int schema_version { get; set; }
            public string payload_json { get; set; }
        }

        private sealed class GameEventContextRow
        {
            public string event_id { get; set; }
            public string run_id { get; set; }
            public string invocation_id { get; set; }
            public string step_execution_id { get; set; }
            public string action_execution_id { get; set; }
            public string cause_kind { get; set; }
            public string cause_reference_id { get; set; }
            public string simulation_rule_id { get; set; }
            public string simulation_rule_version { get; set; }
            public string interval_start_utc { get; set; }
            public string interval_end_utc { get; set; }
        }

        [DataContract]
        private sealed class FurniturePowerEventPayload
        {
            [DataMember(Name = "instanceId")] public string instanceId;
            [DataMember(Name = "definitionId")] public string definitionId;
            [DataMember(Name = "usePointId")] public string usePointId;
            [DataMember(Name = "useId")] public string useId;
            [DataMember(Name = "reservationOwnerId")] public string reservationOwnerId;
            [DataMember(Name = "usePointRevision")] public int usePointRevision;
            [DataMember(Name = "desiredState")] public int desiredState;
            [DataMember(Name = "runId")] public string runId;
            [DataMember(Name = "invocationId")] public string invocationId;
            [DataMember(Name = "stepExecutionId")] public string stepExecutionId;
            [DataMember(Name = "activityId")] public string activityId;
            [DataMember(Name = "sequenceStepId")] public string sequenceStepId;
            [DataMember(Name = "expectedInstanceRevision")] public long expectedInstanceRevision;
            [DataMember(Name = "expectedLayoutRevision")] public long expectedLayoutRevision;
            [DataMember(Name = "expectedGameRevision")] public long expectedGameRevision;
            [DataMember(Name = "committedInstanceRevision")] public long committedInstanceRevision;
            [DataMember(Name = "committedLayoutRevision")] public long committedLayoutRevision;
            [DataMember(Name = "committedGameRevision")] public long committedGameRevision;
        }
    }
}
