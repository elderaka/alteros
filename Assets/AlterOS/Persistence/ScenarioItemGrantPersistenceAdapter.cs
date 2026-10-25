using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlterOS.Content;
using AlterOS.Scenarios;
using Newtonsoft.Json.Linq;

namespace AlterOS.Persistence
{
    /// <summary>Persists only the registered player.item.grant Action through the shared game repository.</summary>
    public sealed class ScenarioItemGrantPersistenceAdapter : IScenarioHostEffectPort
    {
        public const string RegisteredFunctionId = ContentHostEffectFunctionRegistry.PlayerItemGrant;
        public static IReadOnlyCollection<string> RegisteredCapabilityIds
        { get { return Array.AsReadOnly(new[] { RegisteredFunctionId }); } }
        private readonly IContentReleaseReader content;
        private readonly IGameRepository game;

        public ScenarioItemGrantPersistenceAdapter(IContentReleaseReader contentStore, IGameRepository gameRepository)
        {
            if (contentStore == null) throw new ArgumentNullException(nameof(contentStore));
            if (gameRepository == null) throw new ArgumentNullException(nameof(gameRepository));
            content = contentStore;
            game = gameRepository;
        }

        public ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request)
        {
            if (request == null) return Result(ScenarioHostEffectStatuses.Failed, null, null, null, 0, "host_effect_request_invalid");
            if (request.stepExecutionId == request.completedStepExecutionId)
                return Result(ScenarioHostEffectStatuses.Failed, request, null, null, 0, "scenario_effect_execution_identity_reused");
            string operationId = GameOperationId.FromStableKey("scenario-item-grant\n" + request.scenarioRunId + "\n" +
                request.invocationId + "\n" + request.stepExecutionId);

            GameScenarioHostEffectEvidenceResult evidence = game.ReadScenarioHostEffectEvidence(request.scenarioRunId,
                request.invocationId, request.stepExecutionId, request.completedStepExecutionId);
            if (evidence == null || evidence.state != "found" || evidence.effectStepId == evidence.completedStepId ||
                evidence.completedStepOutcome != "completed" || evidence.completedStepId == null ||
                evidence.completedStepId != request.requiresCompletedStepId ||
                evidence.effectStepExecutionId != request.stepExecutionId ||
                evidence.completedStepExecutionId != request.completedStepExecutionId)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0,
                    evidence == null || string.IsNullOrEmpty(evidence.errorCode) ? "scenario_effect_evidence_invalid" : evidence.errorCode);
            GameCommitReceipt existingReceipt = game.FindReceipt(operationId);
            if (existingReceipt != null)
            {
                GameItemQuantityOperationResult prior = game.FindItemQuantityOperation(operationId);
                GameLoadResult replayIdentity = game.Load();
                string expectedOwner = replayIdentity != null && replayIdentity.state == GamePersistenceStates.Ready && replayIdentity.identity != null
                    ? PlayerId(replayIdentity.identity) : string.Empty;
                return prior.IsFound && !string.IsNullOrWhiteSpace(expectedOwner) &&
                    MatchesReceiptReplay(prior, request, expectedOwner)
                    ? Result(ScenarioHostEffectStatuses.Replayed, request, operationId, existingReceipt.operationId,
                        existingReceipt.gameRevision, string.Empty)
                    : Result(ScenarioHostEffectStatuses.Conflict, request, operationId, existingReceipt.operationId,
                        existingReceipt.gameRevision, "host_effect_operation_conflict");
            }
            if (!string.IsNullOrEmpty(evidence.effectOutcome) && existingReceipt == null)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_execution_not_active");
            if (string.IsNullOrWhiteSpace(evidence.flowStoreId) || string.IsNullOrWhiteSpace(evidence.flowRevision) ||
                string.IsNullOrWhiteSpace(evidence.flowHash))
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_flow_pin_missing");
            ContentResult<FlowRecord> flowResult = content.ReadFlow(new ContentRef(evidence.flowStoreId,
                evidence.flowId, evidence.flowRevision, evidence.flowHash));
            if (!flowResult.IsReady) return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0,
                FirstCode(flowResult));
            FlowRecord flow = flowResult.value;
            if (flow == null || flow.kind != "scenario")
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_flow_invalid");
            StepRecord[] effectSteps = (flow.steps ?? new System.Collections.Generic.List<StepRecord>())
                .Where(step => step != null && step.id == evidence.effectStepId).ToArray();
            if (effectSteps.Length != 1 || effectSteps[0].kind != "host_effect" || effectSteps[0].actionRef == null ||
                !SameActionReference(effectSteps[0].actionRef, request.actionRef))
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_step_not_authorized");
            string requiredStepId;
            if (!TryReadHostEffectGate(effectSteps[0].dataJson, out requiredStepId) ||
                requiredStepId != request.requiresCompletedStepId || evidence.completedStepId == evidence.effectStepId)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_prerequisite_invalid");
            StepRecord[] prerequisites = (flow.steps ?? new System.Collections.Generic.List<StepRecord>())
                .Where(step => step != null && step.id == requiredStepId).ToArray();
            if (prerequisites.Length != 1 || prerequisites[0].kind != "dialogue" ||
                prerequisites[0].dialogueRef == null || evidence.completedStepId != prerequisites[0].id)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_prerequisite_not_dialogue");

            ContentResult<ActionRecord> actionResult = content.ReadAction(request.actionRef);
            if (!actionResult.IsReady) return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0,
                FirstCode(actionResult));
            ActionRecord action = actionResult.value;
            if (effectSteps[0].actionRef.expectedHash != action.semanticHash ||
                !string.IsNullOrEmpty(request.actionRef.expectedHash) && request.actionRef.expectedHash != action.semanticHash)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "scenario_effect_action_hash_mismatch");
            if (action.implementationKind != "host_effect" || action.functionId != RegisteredFunctionId)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "host_effect_function_unsupported");

            ContentRef itemRef;
            long quantity;
            string argumentError;
            if (!TryReadGrantArguments(action.argumentsJson, out itemRef, out quantity, out argumentError))
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "host_effect_arguments_invalid");
            ContentResult<ItemRecord> itemResult = content.ReadItem(itemRef);
            if (!itemResult.IsReady) return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0,
                FirstCode(itemResult));
            ItemRecord item = itemResult.value;
            if (quantity <= 0 || item.stackLimit <= 0)
                return Result(ScenarioHostEffectStatuses.Failed, request, operationId, null, 0, "item_grant_quantity_invalid");

            GameLoadResult current = game.Load();
            if (current == null || current.state != GamePersistenceStates.Ready || current.identity == null ||
                string.IsNullOrWhiteSpace(current.progressionJson))
                return Result(ScenarioHostEffectStatuses.Unavailable, request, operationId, null, 0,
                    current == null ? "game_load_unavailable" : current.errorCode);
            GameItemInventoryResult inventory = game.ReadPlayerItemQuantities();
            if (inventory == null || inventory.state != GamePersistenceStates.Ready || string.IsNullOrWhiteSpace(inventory.playerId))
                return Result(ScenarioHostEffectStatuses.Unavailable, request, operationId, null, current.gameRevision,
                    inventory == null ? "item_inventory_unavailable" : inventory.errorCode);
            if (inventory.playerId != PlayerId(current.identity))
                return Result(ScenarioHostEffectStatuses.Conflict, request, operationId, null, current.gameRevision,
                    "item_inventory_owner_mismatch");

            long before = inventory.items.FirstOrDefault(value => value.itemId == item.id)?.quantity ?? 0L;
            if (before < 0 || before > item.stackLimit || before > long.MaxValue - quantity || before + quantity > item.stackLimit)
                return Result(ScenarioHostEffectStatuses.Conflict, request, operationId, null, current.gameRevision,
                    "item_stack_limit_exceeded");

            string changeId = GameOperationId.FromStableKey(operationId + "\nitem-change");
            string eventId = GameOperationId.FromStableKey(operationId + "\nitem-event");
            string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var change = new GameItemQuantityChangeRecord
            {
                changeId = changeId,
                eventId = eventId,
                ownerId = inventory.playerId,
                actionStoreId = request.actionRef.store,
                actionId = action.id,
                actionRevision = action.revision,
                actionExpectedHash = action.semanticHash,
                itemId = item.id,
                itemRevision = item.revision,
                expectedQuantity = before,
                delta = quantity,
                maximumQuantity = item.stackLimit,
                runId = request.scenarioRunId,
                invocationId = request.invocationId,
                stepExecutionId = request.stepExecutionId,
                requiresCompletedStepId = request.requiresCompletedStepId,
                completedDeliveryStepExecutionId = request.completedStepExecutionId
            };
            var commit = new GameCommitRequest
            {
                operationId = operationId,
                operationPayload = "scenario-item-grant|" + request.actionRef.store + "/" + request.actionRef.id + "@" +
                    request.actionRef.revision + "#" + action.semanticHash + "|" +
                    item.id + "@" + item.revision + "|" + request.scenarioRunId + "|" + request.invocationId + "|" + request.stepExecutionId,
                expectedGameRevision = current.gameRevision,
                progressionJson = current.progressionJson
            };
            commit.events.Add(new GameEventRecord
            {
                eventId = eventId, operationId = operationId, kind = "item.quantity_changed",
                actorId = inventory.playerId, targetId = item.id, occurredAtUtc = now, recordedAtUtc = now,
                schemaVersion = 1, payloadJson = EventPayload(change, before + quantity)
            });
            commit.eventContexts.Add(new GameEventHistoryContext
            {
                eventId = eventId, runId = request.scenarioRunId, invocationId = request.invocationId,
                stepExecutionId = request.stepExecutionId, causeKind = "source_operation", causeReferenceId = operationId
            });
            commit.itemQuantityChanges.Add(change);

            GameCommitResult committed = game.Commit(commit);
            if (committed != null && committed.state == GamePersistenceStates.Committed && committed.receipt != null)
                return Result(ScenarioHostEffectStatuses.Applied, request, operationId, committed.receipt.operationId,
                    committed.receipt.gameRevision, string.Empty);
            if (committed != null && committed.state == GamePersistenceStates.Duplicate)
            {
                GameItemQuantityOperationResult prior = game.FindItemQuantityOperation(operationId);
                return prior.IsFound && Matches(prior, request, action, item, quantity, inventory.playerId)
                    ? Result(ScenarioHostEffectStatuses.Replayed, request, operationId, committed.receipt.operationId,
                        committed.receipt.gameRevision, string.Empty)
                    : Result(ScenarioHostEffectStatuses.Conflict, request, operationId,
                        committed.receipt == null ? operationId : committed.receipt.operationId,
                        committed.receipt == null ? 0 : committed.receipt.gameRevision, "host_effect_operation_conflict");
            }
            string reason = committed == null ? "item_grant_commit_unavailable" : committed.errorCode;
            string status = committed != null && (committed.state == GamePersistenceStates.ConflictingOperation ||
                committed.state == GamePersistenceStates.StaleRevision) ? ScenarioHostEffectStatuses.Conflict : ScenarioHostEffectStatuses.Failed;
            return Result(status, request, operationId, null, current.gameRevision, reason);
        }

        private static bool SameActionReference(ContentRef compiled, ContentRef requested)
        {
            return compiled != null && requested != null && compiled.store == requested.store &&
                compiled.id == requested.id && compiled.revision == requested.revision &&
                (string.IsNullOrEmpty(requested.expectedHash) || string.IsNullOrEmpty(compiled.expectedHash) ||
                    requested.expectedHash == compiled.expectedHash);
        }

        private static bool TryReadHostEffectGate(string json, out string requiredStepId)
        {
            requiredStepId = string.Empty;
            try
            {
                JObject data = JObject.Parse(json ?? string.Empty);
                if (data.Properties().Count() != 2 || data["schemaVersion"] == null ||
                    data["schemaVersion"].Type != JTokenType.Integer || (int)data["schemaVersion"] != 1 ||
                    data["requiresCompletedStepId"] == null || data["requiresCompletedStepId"].Type != JTokenType.String)
                    return false;
                requiredStepId = (string)data["requiresCompletedStepId"];
                return !string.IsNullOrWhiteSpace(requiredStepId);
            }
            catch { return false; }
        }

        private static bool Matches(GameItemQuantityOperationResult prior, ScenarioHostEffectRequest request,
            ActionRecord action, ItemRecord item, long quantity, string expectedOwner)
        {
            GameItemQuantityChangeRecord change = prior.change;
            return prior.receipt != null && prior.operationId != null && change != null && prior.eventContext != null &&
                change.changeId == GameOperationId.FromStableKey(prior.operationId + "\nitem-change") &&
                change.eventId == GameOperationId.FromStableKey(prior.operationId + "\nitem-event") &&
                change.ownerId == expectedOwner &&
                change.actionStoreId == request.actionRef.store && change.actionId == action.id &&
                change.actionRevision == action.revision && change.actionExpectedHash == action.semanticHash &&
                change.itemId == item.id && change.itemRevision == item.revision && change.delta == quantity &&
                change.runId == request.scenarioRunId && change.invocationId == request.invocationId &&
                change.stepExecutionId == request.stepExecutionId && change.requiresCompletedStepId == request.requiresCompletedStepId &&
                change.completedDeliveryStepExecutionId == request.completedStepExecutionId &&
                change.maximumQuantity == item.stackLimit && prior.eventContext.eventId == change.eventId &&
                prior.eventContext.runId == request.scenarioRunId && prior.eventContext.invocationId == request.invocationId &&
                prior.eventContext.stepExecutionId == request.stepExecutionId && prior.eventContext.causeKind == "source_operation" &&
                prior.eventContext.causeReferenceId == prior.operationId && action.functionId == RegisteredFunctionId;
        }

        private static bool MatchesReceiptReplay(GameItemQuantityOperationResult prior,
            ScenarioHostEffectRequest request, string expectedOwner)
        {
            GameItemQuantityChangeRecord change = prior.change;
            return prior.receipt != null && prior.operationId ==
                GameOperationId.FromStableKey("scenario-item-grant\n" + request.scenarioRunId + "\n" +
                    request.invocationId + "\n" + request.stepExecutionId) && change != null && prior.eventContext != null &&
                change.changeId == GameOperationId.FromStableKey(prior.operationId + "\nitem-change") &&
                change.eventId == GameOperationId.FromStableKey(prior.operationId + "\nitem-event") &&
                change.ownerId == expectedOwner && request.actionRef != null &&
                change.actionStoreId == request.actionRef.store && change.actionId == request.actionRef.id &&
                change.actionRevision == request.actionRef.revision &&
                IsExpectedActionHashMatch(request.actionRef.expectedHash, change.actionExpectedHash) && change.delta > 0 &&
                change.runId == request.scenarioRunId && change.invocationId == request.invocationId &&
                change.stepExecutionId == request.stepExecutionId &&
                change.requiresCompletedStepId == request.requiresCompletedStepId &&
                change.completedDeliveryStepExecutionId == request.completedStepExecutionId &&
                prior.eventContext.eventId == change.eventId && prior.eventContext.runId == request.scenarioRunId &&
                prior.eventContext.invocationId == request.invocationId && prior.eventContext.stepExecutionId == request.stepExecutionId &&
                prior.eventContext.causeKind == "source_operation" && prior.eventContext.causeReferenceId == prior.operationId;
        }

        private static bool IsExpectedActionHashMatch(string requestedHash, string storedHash)
        { return string.IsNullOrEmpty(requestedHash) || requestedHash == storedHash; }

        private static bool TryReadGrantArguments(string json, out ContentRef itemRef, out long quantity, out string error)
        {
            itemRef = null; quantity = 0; error = "The host-effect arguments are malformed.";
            try
            {
                JObject root = JObject.Parse(json);
                if (root.Properties().Count() != 2 || root["itemRef"] == null || root["quantity"] == null) return false;
                JObject reference = root["itemRef"] as JObject;
                if (reference == null || reference.Properties().Any(property => property.Name != "store" && property.Name != "id" &&
                    property.Name != "revision" && property.Name != "expectedHash")) return false;
                itemRef = new ContentRef((string)reference["store"], (string)reference["id"],
                    (string)reference["revision"], (string)reference["expectedHash"]);
                if (!long.TryParse(root["quantity"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity <= 0)
                    return false;
                error = string.Empty;
                return true;
            }
            catch { return false; }
        }

        private static string EventPayload(GameItemQuantityChangeRecord change, long after)
        {
            return "{\"changeId\":" + ContentJson.Quote(change.changeId) + ",\"actionStoreId\":" + ContentJson.Quote(change.actionStoreId) +
                ",\"actionId\":" + ContentJson.Quote(change.actionId) + ",\"actionRevision\":" + ContentJson.Quote(change.actionRevision) +
                ",\"actionExpectedHash\":" + ContentJson.Quote(change.actionExpectedHash) + ",\"itemId\":" + ContentJson.Quote(change.itemId) +
                ",\"itemRevision\":" + ContentJson.Quote(change.itemRevision) + ",\"before\":" + change.expectedQuantity.ToString(CultureInfo.InvariantCulture) +
                ",\"delta\":" + change.delta.ToString(CultureInfo.InvariantCulture) + ",\"after\":" + after.ToString(CultureInfo.InvariantCulture) +
                ",\"maximumQuantity\":" + change.maximumQuantity.ToString(CultureInfo.InvariantCulture) +
                ",\"requiresCompletedStepId\":" + ContentJson.Quote(change.requiresCompletedStepId) +
                ",\"completedDeliveryStepExecutionId\":" + ContentJson.Quote(change.completedDeliveryStepExecutionId) + "}";
        }

        private static string PlayerId(GameSaveIdentity identity)
        { return identity.legacyPlayerId ?? identity.accountId ?? identity.saveId; }

        private static string FirstCode<T>(ContentResult<T> result)
        { return result == null || result.diagnostics.Count == 0 ? "content_reference_unavailable" : result.diagnostics[0].code; }

        private static ScenarioHostEffectResult Result(string status, ScenarioHostEffectRequest request, string operationId,
            string receiptId, long revision, string reason)
        {
            return new ScenarioHostEffectResult(status,
                request == null ? "unknown" : request.scenarioRunId,
                request == null ? "unknown" : request.invocationId,
                request == null ? "unknown" : request.stepExecutionId,
                operationId, receiptId, revision, reason);
        }
    }
}
