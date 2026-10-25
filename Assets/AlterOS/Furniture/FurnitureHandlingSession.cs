using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Furniture
{
    public sealed class FurniturePlacementPose
    {
        public FurnitureVector3 positionMeters;
        public FurnitureQuaternion rotation;
        public FurnitureLayoutSupportReference support;
    }

    public sealed class FurnitureHandlingResult
    {
        public bool success;
        public string state;
        public string reasonCode;
    }

    public sealed class FurnitureHandlingView
    {
        public string state;
        public string definitionId;
        public string instanceId;
        public string sourceKind;
        public FurniturePlacementPose pose;
        public bool valid;
        public string reasonCode;
        public int availableCount;
        public bool unlimitedSupply;
        public IReadOnlyList<FurnitureInventoryEntry> availableInventory;
    }

    public sealed class FurnitureHandlingSession
    {
        private sealed class Hold
        {
            public bool isNewInventoryItem;
            public string sourceKind;
            public string definitionId;
            public string instanceId;
            public long expectedInstanceRevision;
            public long expectedGameRevision;
            public long expectedLayoutRevision;
            public string operationId;
            public HomeFurnitureLayoutDocument pendingCandidate;
            public FurniturePlacementPose previewPose;
            public bool previewValid;
            public string reasonCode;
        }

        private readonly IFurnitureHomeStore store;
        private readonly FurnitureCatalogueSnapshot catalogue;
        private HomeFurnitureLayoutDocument document;
        private long gameRevision;
        private long layoutRevision;
        private string unavailableReason;
        private string selectedDefinitionId;
        private Hold hold;

        public FurnitureHandlingSession(IFurnitureHomeStore store, FurnitureCatalogueSnapshot catalogue)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            this.store = store;
            this.catalogue = catalogue;
            ReadDurableState();
        }

        public FurnitureHandlingView Current { get { return CreateView(); } }

        public FurnitureHandlingResult BeginFromInventory(string definitionId, bool debugEnabled)
        {
            if (!IsAvailable()) return Failure(unavailableReason ?? "home_unavailable", "unavailable");
            if (hold != null) return Failure("already_holding", "conflict");
            if (!IsValidCatalogue()) return Failure("catalogue_unavailable", "unavailable");
            if (!FurnitureInventoryValidator.IsValidDefinitionId(definitionId) ||
                !catalogue.definitions.Any(definition => definition != null && definition.definitionId == definitionId))
                return Failure("unknown_definition", "rejected");

            int stock = GetStoredCount(document, definitionId);
            if (!debugEnabled && stock <= 0) return Failure("insufficient_stock", "rejected");
            selectedDefinitionId = definitionId;
            hold = new Hold
            {
                isNewInventoryItem = true,
                sourceKind = debugEnabled ? "debug_supply" : "finite_inventory",
                definitionId = definitionId,
                instanceId = "furniture." + Guid.NewGuid().ToString("N"),
                expectedGameRevision = gameRevision,
                expectedLayoutRevision = layoutRevision,
                previewValid = false,
                reasonCode = "placement_pending"
            };
            return Success("holding");
        }

        public FurnitureHandlingResult BeginMove(string instanceId, FurniturePlacementEvidence evidence)
        {
            if (!IsAvailable()) return Failure(unavailableReason ?? "home_unavailable", "unavailable");
            if (hold != null) return Failure("already_holding", "conflict");
            FurnitureHandlingResult evidenceResult = ValidateEvidence(evidence);
            if (!evidenceResult.success) return evidenceResult;
            if (evidence.catalogue.catalogueHash != catalogue.catalogueHash)
                return Failure("catalogue_changed", "stale_revision");

            HomeFurnitureLayoutInstance instance = document.instances.FirstOrDefault(item => item != null && item.instanceId == instanceId);
            if (instance == null) return Failure("instance_missing", "rejected");
            if (!catalogue.definitions.Any(definition => definition != null && definition.definitionId == instance.definitionId))
                return Failure("missing_definition", "rejected");
            if (IsReserved(instance.instanceId, evidence.reservedPointRefs)) return Failure("point_in_use", "rejected");
            if (HasAttachedDependents(instance.instanceId)) return Failure("has_dependents", "rejected");

            selectedDefinitionId = instance.definitionId;
            hold = new Hold
            {
                isNewInventoryItem = false,
                sourceKind = "placed_furniture",
                definitionId = instance.definitionId,
                instanceId = instance.instanceId,
                expectedInstanceRevision = instance.instanceRevision,
                expectedGameRevision = gameRevision,
                expectedLayoutRevision = layoutRevision,
                previewPose = PoseFrom(instance),
                previewValid = true,
                reasonCode = string.Empty
            };
            return Success("holding");
        }

        public FurnitureHandlingResult UpdatePreview(FurniturePlacementPose pose, FurniturePlacementEvidence evidence)
        {
            if (hold == null) return Failure("no_hold", "rejected");
            if (pose == null) return InvalidateHold("invalid_transform");
            hold.previewPose = ClonePose(pose);

            FurnitureHandlingResult evidenceResult = ValidateEvidence(evidence);
            if (!evidenceResult.success) return InvalidateHold(evidenceResult.reasonCode);
            if (evidence.catalogue.catalogueHash != catalogue.catalogueHash)
                return InvalidateHold("catalogue_changed");

            FurnitureLayoutCommandResult evaluation = EvaluatePreview(hold, pose, evidence);
            if (evaluation.success)
            {
                hold.previewValid = true;
                hold.reasonCode = string.Empty;
                return Success("preview_valid");
            }

            hold.previewValid = false;
            hold.reasonCode = evaluation.reasonCode;
            return new FurnitureHandlingResult { success = true, state = "preview_invalid", reasonCode = evaluation.reasonCode };
        }

        public FurnitureHandlingResult Confirm(string operationId, FurniturePlacementEvidence evidence)
        {
            if (hold == null) return Failure("no_hold", "rejected");
            if (string.IsNullOrWhiteSpace(operationId)) return Failure("invalid_operation_id", "rejected");
            if (hold.pendingCandidate != null)
            {
                if (hold.operationId != operationId) return Failure("operation_identity_conflict", "conflict");
                return CommitCandidate(operationId, HomeFurnitureRepository.CloneDocument(hold.pendingCandidate));
            }
            if (!hold.previewValid)
                return Failure(IsPlacementReason(hold.reasonCode) ? "invalid_placement" : hold.reasonCode ?? "invalid_placement", "rejected");

            FurnitureHandlingResult fresh = RefreshForCommit(evidence);
            if (!fresh.success) return fresh;
            FurnitureLayoutCommandResult applied = EvaluatePreview(hold, hold.previewPose, evidence);
            if (!applied.success)
            {
                hold.previewValid = false;
                hold.reasonCode = applied.reasonCode;
                return Failure(IsPlacementReason(applied.reasonCode) ? "invalid_placement" : applied.reasonCode, "rejected");
            }

            HomeFurnitureLayoutDocument candidate = applied.snapshot.document;
            if (hold.isNewInventoryItem)
            {
                FurnitureInventoryEntry entry = FindOrAddInventoryEntry(candidate, hold.definitionId);
                if (hold.sourceKind == "finite_inventory")
                {
                    if (entry.storedCount <= 0) return Failure("insufficient_stock", "stale_revision");
                    entry.storedCount--;
                }
                SortInventory(candidate);
            }
            FurnitureDiagnostic blocking = HomeFurnitureRepository.ValidateDocument(candidate)
                .FirstOrDefault(diagnostic => diagnostic.severity == "error");
            if (blocking != null) return Failure(blocking.code, "invalid");
            hold.operationId = operationId;
            hold.pendingCandidate = HomeFurnitureRepository.CloneDocument(candidate);
            return CommitCandidate(operationId, candidate);
        }

        public FurnitureHandlingResult ReturnToInventory(string operationId, FurniturePlacementEvidence evidence)
        {
            if (hold == null) return Failure("no_hold", "rejected");
            if (hold.isNewInventoryItem)
            {
                hold = null;
                return Success("returned_to_inventory");
            }
            if (string.IsNullOrWhiteSpace(operationId)) return Failure("invalid_operation_id", "rejected");
            if (hold.pendingCandidate != null)
            {
                if (hold.operationId != operationId) return Failure("operation_identity_conflict", "conflict");
                return CommitCandidate(operationId, HomeFurnitureRepository.CloneDocument(hold.pendingCandidate));
            }

            FurnitureHandlingResult fresh = RefreshForCommit(evidence);
            if (!fresh.success) return fresh;
            var command = new FurnitureLayoutCommand
            {
                operation = "remove",
                homeId = document.homeId,
                roomId = document.roomId,
                instanceId = hold.instanceId,
                expectedInstanceRevision = hold.expectedInstanceRevision
            };
            FurnitureLayoutCommandResult removed = FurnitureLayoutCommands.Apply(command,
                new FurnitureLayoutSnapshot { document = document, contentHash = string.Empty }, evidence);
            if (!removed.success) return Failure(removed.reasonCode, removed.state);

            HomeFurnitureLayoutDocument candidate = removed.snapshot.document;
            FurnitureInventoryEntry entry = FindOrAddInventoryEntry(candidate, hold.definitionId);
            if (entry.storedCount == int.MaxValue) return Failure("stock_overflow", "rejected");
            entry.storedCount++;
            SortInventory(candidate);
            FurnitureDiagnostic blocking = HomeFurnitureRepository.ValidateDocument(candidate)
                .FirstOrDefault(diagnostic => diagnostic.severity == "error");
            if (blocking != null) return Failure(blocking.code, "invalid");
            hold.operationId = operationId;
            hold.pendingCandidate = HomeFurnitureRepository.CloneDocument(candidate);
            return CommitCandidate(operationId, candidate);
        }

        public FurnitureHandlingResult Cancel()
        {
            if (hold == null) return Failure("no_hold", "rejected");
            if (hold.pendingCandidate != null)
            {
                FurnitureHomeReadResult durable;
                try { durable = store.Read(); }
                catch (Exception) { return Failure("home_reload_failed", "unavailable"); }
                if (!IsValidRead(durable)) return Failure("home_reload_failed", "unavailable");
                AdoptRead(durable);
            }
            hold = null;
            return Success("cancelled");
        }

        private FurnitureLayoutCommandResult EvaluatePreview(Hold activeHold, FurniturePlacementPose pose,
            FurniturePlacementEvidence evidence)
        {
            var command = new FurnitureLayoutCommand
            {
                operation = activeHold.isNewInventoryItem ? "place" : "transform",
                homeId = document.homeId,
                roomId = document.roomId,
                instanceId = activeHold.instanceId,
                expectedInstanceRevision = activeHold.expectedInstanceRevision,
                positionMeters = CloneVector(pose.positionMeters),
                rotation = CloneQuaternion(pose.rotation),
                support = HomeFurnitureRepository.CloneSupport(pose.support)
            };
            if (activeHold.isNewInventoryItem)
            {
                FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item != null && item.definitionId == activeHold.definitionId);
                if (definition == null) return InvalidCommand("missing_definition");
                command.newInstance = new HomeFurnitureLayoutInstance
                {
                    instanceId = activeHold.instanceId,
                    definitionId = activeHold.definitionId,
                    positionMeters = CloneVector(pose.positionMeters),
                    rotation = CloneQuaternion(pose.rotation),
                    scale = CloneVector(definition.defaultScale),
                    support = HomeFurnitureRepository.CloneSupport(pose.support),
                    enabled = true,
                    conditionState = "good",
                    resourceState = new List<FurnitureResourceState>(),
                    instanceRevision = 0
                };
            }

            return FurnitureLayoutCommands.Apply(command,
                new FurnitureLayoutSnapshot { document = document, contentHash = string.Empty }, evidence);
        }

        private FurnitureHandlingResult RefreshForCommit(FurniturePlacementEvidence evidence)
        {
            FurnitureHandlingResult evidenceResult = ValidateEvidence(evidence);
            if (!evidenceResult.success) return InvalidateHold(evidenceResult.reasonCode, evidenceResult.state);
            if (evidence.catalogue.catalogueHash != catalogue.catalogueHash)
                return InvalidateHold("catalogue_changed", "stale_revision");

            FurnitureHomeReadResult latest;
            try { latest = store.Read(); }
            catch (Exception) { return Failure("home_read_failed", "unavailable"); }
            if (!IsValidRead(latest)) return InvalidateHold(latest == null ? "home_unavailable" : latest.errorCode ?? latest.state, "unavailable");
            if (latest.gameRevision != hold.expectedGameRevision || latest.layoutRevision != hold.expectedLayoutRevision)
            {
                AdoptRead(latest);
                return InvalidateHold("stale_revision", "stale_revision");
            }

            AdoptRead(latest);
            if (!hold.isNewInventoryItem)
            {
                HomeFurnitureLayoutInstance source = document.instances.FirstOrDefault(item => item != null && item.instanceId == hold.instanceId);
                if (source == null) return InvalidateHold("instance_missing", "stale_revision");
                if (source.instanceRevision != hold.expectedInstanceRevision) return InvalidateHold("stale_revision", "stale_revision");
                if (IsReserved(source.instanceId, evidence.reservedPointRefs)) return InvalidateHold("point_in_use", "rejected");
                if (HasAttachedDependents(source.instanceId)) return InvalidateHold("has_dependents", "rejected");
            }
            else if (hold.sourceKind == "finite_inventory" && GetStoredCount(document, hold.definitionId) <= 0)
            {
                return InvalidateHold("insufficient_stock", "stale_revision");
            }
            return Success("ready");
        }

        private FurnitureHandlingResult CommitCandidate(string operationId, HomeFurnitureLayoutDocument candidate)
        {
            FurnitureHomeCommitResult result;
            try
            {
                result = store.Commit(operationId, hold.expectedGameRevision, hold.expectedLayoutRevision, candidate);
            }
            catch (Exception)
            {
                return Failure("home_commit_failed", "unavailable");
            }
            if (result == null) return Failure("home_commit_failed", "unavailable");

            if (result.state == "duplicate")
            {
                FurnitureHomeReadResult durable;
                try { durable = store.Read(); }
                catch (Exception) { return Failure("home_reload_failed", "unavailable"); }
                if (!IsValidRead(durable)) return Failure("home_reload_failed", "unavailable");
                AdoptRead(durable);
                hold = null;
                return Success("duplicate");
            }
            if (!result.durable) return Failure(result.errorCode ?? result.state ?? "home_commit_failed", result.state ?? "unavailable");

            if (result.document == null)
            {
                FurnitureHomeReadResult durable;
                try { durable = store.Read(); }
                catch (Exception) { return Failure("home_reload_failed", "unavailable"); }
                if (!IsValidRead(durable)) return Failure("home_reload_failed", "unavailable");
                AdoptRead(durable);
            }
            else
            {
                var committed = new FurnitureHomeReadResult
                {
                    state = "loaded", document = result.document,
                    gameRevision = result.gameRevision, layoutRevision = result.layoutRevision
                };
                if (!IsValidRead(committed)) return Failure("invalid_committed_home", "recovery_required");
                AdoptRead(committed);
            }
            hold = null;
            return Success(result.state ?? "committed");
        }

        private void ReadDurableState()
        {
            FurnitureHomeReadResult result;
            try { result = store.Read(); }
            catch (Exception) { unavailableReason = "home_read_failed"; return; }
            if (!IsValidRead(result))
            {
                unavailableReason = result == null ? "home_unavailable" : result.errorCode ?? result.state ?? "home_unavailable";
                return;
            }
            AdoptRead(result);
        }

        private bool IsValidRead(FurnitureHomeReadResult result)
        {
            if (result == null || (result.state != "loaded" && result.state != "ready") || result.document == null)
                return false;
            if (result.document.schemaVersion != 2 || result.document.layoutRevision != result.layoutRevision) return false;
            if (HomeFurnitureRepository.ValidateDocument(result.document).Any(diagnostic => diagnostic.severity == "error")) return false;
            return result.gameRevision >= 0 && result.layoutRevision >= 0;
        }

        private void AdoptRead(FurnitureHomeReadResult result)
        {
            document = HomeFurnitureRepository.CloneDocument(result.document);
            gameRevision = result.gameRevision;
            layoutRevision = result.layoutRevision;
            unavailableReason = null;
        }

        private bool IsAvailable()
        {
            return document != null && unavailableReason == null;
        }

        private bool IsValidCatalogue()
        {
            return catalogue.schemaVersion == 1 && !catalogue.diagnostics.Any(diagnostic => diagnostic.severity == "error");
        }

        private static FurnitureHandlingResult ValidateEvidence(FurniturePlacementEvidence evidence)
        {
            if (evidence == null || evidence.catalogue == null) return Failure("invalid_evidence", "rejected");
            if (!evidence.evidenceComplete) return Failure("evidence_unknown", "unknown");
            return Success("ready");
        }

        private bool HasAttachedDependents(string instanceId)
        {
            return document.instances.Any(instance => instance != null && instance.instanceId != instanceId &&
                instance.support != null && (instance.support.supportId == instanceId || instance.support.supportId == "furniture:" + instanceId));
        }

        private static bool IsReserved(string instanceId, IEnumerable<FurniturePointReference> refs)
        {
            return (refs ?? Enumerable.Empty<FurniturePointReference>()).Any(reference => reference != null && reference.instanceId == instanceId);
        }

        private static FurniturePlacementPose PoseFrom(HomeFurnitureLayoutInstance instance)
        {
            return new FurniturePlacementPose
            {
                positionMeters = CloneVector(instance.positionMeters),
                rotation = CloneQuaternion(instance.rotation),
                support = HomeFurnitureRepository.CloneSupport(instance.support)
            };
        }

        private static FurniturePlacementPose ClonePose(FurniturePlacementPose pose)
        {
            return pose == null ? null : new FurniturePlacementPose
            {
                positionMeters = CloneVector(pose.positionMeters),
                rotation = CloneQuaternion(pose.rotation),
                support = HomeFurnitureRepository.CloneSupport(pose.support)
            };
        }

        private static FurnitureVector3 CloneVector(FurnitureVector3 value)
        {
            return value == null ? null : new FurnitureVector3 { x = value.x, y = value.y, z = value.z };
        }

        private static FurnitureQuaternion CloneQuaternion(FurnitureQuaternion value)
        {
            return value == null ? null : new FurnitureQuaternion { x = value.x, y = value.y, z = value.z, w = value.w };
        }

        private FurnitureHandlingResult InvalidateHold(string reason, string state = "preview_invalid")
        {
            if (hold != null)
            {
                hold.previewValid = false;
                hold.reasonCode = reason ?? "invalid_preview";
            }
            return Failure(reason ?? "invalid_preview", state);
        }

        private static bool IsPlacementReason(string reason)
        {
            return reason == "placement_pending" || reason == "outside_room" || reason == "structural_overlap" ||
                reason == "unsupported_support" || reason == "stale_support" || reason == "support_pose_mismatch" ||
                reason == "invalid_transform" || reason == "invalid_scale" || reason == "missing_definition";
        }

        private static FurnitureHandlingResult Failure(string reason, string state)
        {
            return new FurnitureHandlingResult { success = false, state = state ?? "rejected", reasonCode = reason ?? string.Empty };
        }

        private static FurnitureHandlingResult Success(string state)
        {
            return new FurnitureHandlingResult { success = true, state = state ?? string.Empty, reasonCode = string.Empty };
        }

        private static int GetStoredCount(HomeFurnitureLayoutDocument source, string definitionId)
        {
            FurnitureInventoryEntry entry = (source == null ? null : source.inventory ?? new List<FurnitureInventoryEntry>())
                .FirstOrDefault(item => item != null && item.definitionId == definitionId);
            return entry == null ? 0 : entry.storedCount;
        }

        private static FurnitureInventoryEntry FindOrAddInventoryEntry(HomeFurnitureLayoutDocument target, string definitionId)
        {
            if (target.inventory == null) target.inventory = new List<FurnitureInventoryEntry>();
            FurnitureInventoryEntry entry = target.inventory.FirstOrDefault(item => item != null && item.definitionId == definitionId);
            if (entry != null) return entry;
            entry = new FurnitureInventoryEntry { definitionId = definitionId, storedCount = 0 };
            target.inventory.Add(entry);
            return entry;
        }

        private static void SortInventory(HomeFurnitureLayoutDocument target)
        {
            target.inventory = (target.inventory ?? new List<FurnitureInventoryEntry>())
                .OrderBy(item => item == null ? string.Empty : item.definitionId, StringComparer.Ordinal).ToList();
        }

        private FurnitureHandlingView CreateView()
        {
            List<FurnitureInventoryEntry> entries = (document == null ? new List<FurnitureInventoryEntry>() : document.inventory ?? new List<FurnitureInventoryEntry>())
                .Where(item => item != null)
                .Select(item => new FurnitureInventoryEntry { definitionId = item.definitionId, storedCount = item.storedCount })
                .ToList();
            if (hold != null && hold.sourceKind == "finite_inventory")
            {
                FurnitureInventoryEntry reserved = entries.FirstOrDefault(item => item.definitionId == hold.definitionId);
                if (reserved != null && reserved.storedCount > 0) reserved.storedCount--;
            }
            string displayDefinitionId = hold == null ? selectedDefinitionId : hold.definitionId;
            int availableCount = string.IsNullOrEmpty(displayDefinitionId) ? 0 : GetStoredCount(document, displayDefinitionId);
            if (hold != null && hold.sourceKind == "finite_inventory" && availableCount > 0) availableCount--;
            return new FurnitureHandlingView
            {
                state = hold != null ? "holding" : IsAvailable() ? "idle" : "unavailable",
                definitionId = displayDefinitionId ?? string.Empty,
                instanceId = hold == null ? string.Empty : hold.instanceId,
                sourceKind = hold == null ? string.Empty : hold.sourceKind,
                pose = hold == null ? null : ClonePose(hold.previewPose),
                valid = hold != null && hold.previewValid,
                reasonCode = hold == null ? unavailableReason ?? string.Empty : hold.reasonCode ?? string.Empty,
                availableCount = availableCount,
                unlimitedSupply = hold != null && hold.sourceKind == "debug_supply",
                availableInventory = entries.AsReadOnly()
            };
        }

        private static FurnitureLayoutCommandResult InvalidCommand(string reason)
        {
            return new FurnitureLayoutCommandResult
            {
                success = false,
                state = "rejected",
                reasonCode = reason,
                diagnostic = new FurnitureDiagnostic(reason, "error", "command", "Furniture command could not be evaluated.")
            };
        }
    }
}
