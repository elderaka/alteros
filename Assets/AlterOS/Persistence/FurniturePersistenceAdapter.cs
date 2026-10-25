using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Furniture;

namespace AlterOS.Persistence
{
    public sealed class FurniturePersistenceLoadResult
    {
        public string state;
        public HomeFurnitureLayoutDocument document;
        public long gameRevision;
        public long layoutRevision;
        public IReadOnlyList<string> orphanInstanceIds;
        public IReadOnlyList<FurnitureDiagnostic> diagnostics;
        public string errorCode;
        public string message;
    }

    public sealed class FurniturePersistenceCommitResult
    {
        public string state;
        public HomeFurnitureLayoutDocument document;
        public long gameRevision;
        public long layoutRevision;
        public GameCommitResult commit;
        public string errorCode;
        public string message;
    }

    public sealed class FurniturePersistenceAdapter : IFurnitureHomeStore
    {
        private readonly IGameRepository repository;
        private readonly Func<string, HomeFurnitureLayoutDocument> seedProvider;

        public FurniturePersistenceAdapter(IGameRepository repository,
            Func<string, HomeFurnitureLayoutDocument> seedProvider = null)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            this.repository = repository;
            this.seedProvider = seedProvider;
        }

        public FurnitureHomeReadResult Read()
        {
            GameLoadResult current = repository.Load();
            if (current.state != GamePersistenceStates.Ready)
                return new FurnitureHomeReadResult { state = current.state, errorCode = current.errorCode, message = current.message };
            if (current.identity == null || string.IsNullOrWhiteSpace(current.identity.homeId))
                return new FurnitureHomeReadResult { state = GamePersistenceStates.RecoveryRequired, errorCode = "home_identity_missing" };

            FurniturePersistenceLoadResult result = LoadHome(current.identity.homeId);
            return new FurnitureHomeReadResult
            {
                state = result.state,
                errorCode = result.errorCode,
                message = result.message,
                document = result.document,
                gameRevision = result.gameRevision,
                layoutRevision = result.layoutRevision
            };
        }

        public FurnitureHomeCommitResult Commit(string operationId, long expectedGameRevision,
            long expectedLayoutRevision, HomeFurnitureLayoutDocument candidate)
        {
            FurniturePersistenceCommitResult result = CommitLayout(operationId, expectedGameRevision,
                expectedLayoutRevision, candidate);
            return new FurnitureHomeCommitResult
            {
                state = result.state,
                errorCode = result.errorCode,
                message = result.message,
                durable = result.commit != null && result.commit.IsDurable,
                document = result.document,
                gameRevision = result.gameRevision,
                layoutRevision = result.layoutRevision
            };
        }

        public FurniturePersistenceLoadResult LoadHome(string homeId)
        {
            GameLoadResult current = repository.Load();
            if (current.state != GamePersistenceStates.Ready)
                return LoadFailure(current.state, current.errorCode, current.message);
            if (string.IsNullOrWhiteSpace(homeId) || current.identity.homeId != homeId)
                return LoadFailure(GamePersistenceStates.Invalid, "home_identity_mismatch", "The requested home does not match the local save identity.");
            if (current.progressionJson == null)
                return LoadFailure(GamePersistenceStates.RecoveryRequired, "progression_not_initialized", "Home state cannot be published before progression initialization.");

            if (current.layoutJson != null)
                return ReadExistingHome(current, homeId, 0);
            if (seedProvider == null)
                return LoadFailure(GamePersistenceStates.Missing, "home_seed_missing", "No saved home layout or host seed is available.");

            HomeFurnitureLayoutDocument seed;
            try { seed = seedProvider(homeId); }
            catch (Exception exception) { return LoadFailure(GamePersistenceStates.Invalid, "home_seed_failed", exception.Message); }
            if (seed == null || seed.homeId != homeId)
                return LoadFailure(GamePersistenceStates.Invalid, "invalid_home_seed", "The host seed must belong to the requested home.");

            try { seed = PrepareNewHomeSeed(seed); }
            catch (Exception exception) { return LoadFailure(GamePersistenceStates.Invalid, "home_seed_invalid", exception.Message); }

            FurniturePersistenceCommitResult commit = CommitLayout(
                Guid.NewGuid().ToString("D"), current.gameRevision, current.layoutRevision, seed, "home_layout_seeded");
            if (commit.state != GamePersistenceStates.Committed)
            {
                if (commit.state == GamePersistenceStates.StaleRevision)
                {
                    GameLoadResult latest = repository.Load();
                    if (latest.state == GamePersistenceStates.Ready && latest.layoutJson != null)
                        return ReadExistingHome(latest, homeId, 0);
                }
                return LoadFailure(commit.state, commit.errorCode, commit.message);
            }
            GameLoadResult seeded = repository.Load();
            if (seeded.state != GamePersistenceStates.Ready)
                return LoadFailure(seeded.state, seeded.errorCode, seeded.message);
            return ReadExistingHome(seeded, homeId, 0);
        }

        public FurniturePersistenceCommitResult CommitLayout(string operationId, long expectedGameRevision,
            long expectedLayoutRevision, HomeFurnitureLayoutDocument validatedLayout)
        {
            return CommitLayout(operationId, expectedGameRevision, expectedLayoutRevision, validatedLayout, null, null);
        }

        public FurniturePersistenceCommitResult CommitPowerState(string operationId, long expectedGameRevision,
            long expectedLayoutRevision, HomeFurnitureLayoutDocument validatedLayout, string instanceId, string powerEventPayload,
            GameEventHistoryContext eventContext, string operationPayload)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(powerEventPayload) || eventContext == null ||
                string.IsNullOrWhiteSpace(operationPayload))
                return FailedCommit(GamePersistenceStates.Invalid, "furniture_power_event_invalid", "A target instance, source context, payload, and idempotency payload are required.");
            return CommitLayout(operationId, expectedGameRevision, expectedLayoutRevision, validatedLayout,
                "furniture_power_changed", new FurnitureEventDescriptor
                {
                    kind = "furniture_power_changed", targetId = instanceId, payloadJson = powerEventPayload
                }, eventContext, operationPayload);
        }

        public FurniturePersistenceCommitResult CommitPowerNoChange(string operationId, long expectedGameRevision,
            string progressionJson, string operationPayload)
        {
            if (string.IsNullOrWhiteSpace(operationId) || expectedGameRevision < 0 || string.IsNullOrWhiteSpace(progressionJson) ||
                string.IsNullOrWhiteSpace(operationPayload))
                return FailedCommit(GamePersistenceStates.Invalid, "furniture_power_no_change_invalid", "A stable operation, current progression snapshot, and command identity are required.");
            GameCommitResult commit = repository.Commit(new GameCommitRequest
            {
                operationId = operationId, operationPayload = operationPayload,
                expectedGameRevision = expectedGameRevision, progressionJson = progressionJson
            });
            return new FurniturePersistenceCommitResult
            {
                state = commit == null ? GamePersistenceStates.Unavailable : commit.state,
                gameRevision = commit != null && commit.receipt != null ? commit.receipt.gameRevision : expectedGameRevision,
                layoutRevision = commit != null && commit.receipt != null ? commit.receipt.layoutRevision : 0,
                commit = commit,
                errorCode = commit == null ? "furniture_power_no_change_unavailable" : commit.errorCode,
                message = commit == null ? "The no-change receipt could not be stored." : commit.message
            };
        }

        private FurniturePersistenceCommitResult CommitLayout(string operationId, long expectedGameRevision,
            long expectedLayoutRevision, HomeFurnitureLayoutDocument validatedLayout, string eventKind,
            FurnitureEventDescriptor eventOverride = null, GameEventHistoryContext eventContext = null,
            string operationPayload = null)
        {
            GameLoadResult current = repository.Load();
            if (current.state != GamePersistenceStates.Ready)
                return FailedCommit(current.state, current.errorCode, current.message);
            if (current.progressionJson == null || expectedGameRevision < 0 || expectedLayoutRevision < 0)
                return FailedCommit(GamePersistenceStates.RecoveryRequired, "invalid_home_revision", "Progression and non-negative revisions are required before saving a home.");
            if (current.identity == null || string.IsNullOrWhiteSpace(current.identity.homeId) || current.identity.homeId != validatedLayout?.homeId)
                return FailedCommit(GamePersistenceStates.Invalid, "home_identity_mismatch", "The layout must belong to the current local home.");
            if (current.layoutJson != null)
            {
                FurniturePersistenceLoadResult existing = ReadLayout(current, current.identity.homeId);
                if (existing.state != "loaded")
                    return FailedCommit(GamePersistenceStates.RecoveryRequired, existing.errorCode, existing.message);
            }

            HomeFurnitureLayoutDocument candidate;
            try
            {
                candidate = HomeFurnitureRepository.CloneDocument(validatedLayout);
                candidate.layoutRevision = checked(expectedLayoutRevision + 1);
            }
            catch (Exception exception)
            {
                return FailedCommit(GamePersistenceStates.Invalid, "layout_clone_failed", exception.Message);
            }

            FurnitureDiagnostic blocking = HomeFurnitureRepository.ValidateDocument(candidate)
                .FirstOrDefault(diagnostic => diagnostic.severity == "error");
            if (blocking != null)
                return FailedCommit(GamePersistenceStates.Invalid, blocking.code, blocking.message);

            if (current.layoutJson != null)
            {
                string transitionError = ValidateInventoryTransition(current.layoutJson, candidate, eventKind);
                if (transitionError != null)
                    return FailedCommit(GamePersistenceStates.Invalid, "invalid_stock_layout_transition", transitionError);
            }

            string layoutJson;
            try { layoutJson = Serialize(candidate); }
            catch (Exception exception) { return FailedCommit(GamePersistenceStates.Invalid, "layout_serialize_failed", exception.Message); }

            FurnitureEventDescriptor eventDescriptor = eventOverride ?? (eventKind == null
                ? DescribeFurnitureChange(current.layoutJson, candidate)
                : DescribeHomeChange(eventKind, candidate));
            string now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            var request = new GameCommitRequest
            {
                operationId = operationId,
                operationPayload = operationPayload ?? "home-layout:" + Hash(layoutJson),
                expectedGameRevision = expectedGameRevision,
                expectedLayoutRevision = expectedLayoutRevision,
                progressionJson = current.progressionJson,
                layoutJson = layoutJson
            };
            request.events.Add(new GameEventRecord
            {
                eventId = eventContext == null ? Guid.NewGuid().ToString("D") : eventContext.eventId,
                operationId = operationId,
                kind = eventDescriptor.kind,
                actorId = "host",
                targetId = eventDescriptor.targetId,
                occurredAtUtc = now,
                recordedAtUtc = now,
                schemaVersion = 1,
                payloadJson = eventDescriptor.payloadJson
            });
            if (eventContext != null) request.eventContexts.Add(eventContext);

            GameCommitResult committed = repository.Commit(request);
            if (committed.state == GamePersistenceStates.Duplicate)
            {
                GameLoadResult latest = repository.Load();
                if (latest.state != GamePersistenceStates.Ready)
                    return FailedCommit(latest.state, latest.errorCode, latest.message);
                FurniturePersistenceLoadResult durableHome = ReadLayout(latest, candidate.homeId);
                if (durableHome.state != "loaded")
                    return FailedCommit(GamePersistenceStates.RecoveryRequired, durableHome.errorCode, durableHome.message);
                return new FurniturePersistenceCommitResult
                {
                    state = GamePersistenceStates.Duplicate,
                    document = durableHome.document,
                    gameRevision = durableHome.gameRevision,
                    layoutRevision = durableHome.layoutRevision,
                    commit = committed
                };
            }
            if (!committed.IsDurable)
                return new FurniturePersistenceCommitResult
                {
                    state = committed.state,
                    commit = committed,
                    errorCode = committed.errorCode,
                    message = committed.message
                };

            return new FurniturePersistenceCommitResult
            {
                state = committed.state,
                document = candidate,
                gameRevision = committed.receipt.gameRevision,
                layoutRevision = committed.receipt.layoutRevision,
                commit = committed
            };
        }

        private static FurniturePersistenceLoadResult ReadLayout(GameLoadResult current, string homeId)
        {
            try
            {
                HomeFurnitureLayoutDocument document = Deserialize(current.layoutJson);
                if (document == null || document.homeId != homeId || document.layoutRevision != current.layoutRevision)
                    return LoadFailure(GamePersistenceStates.RecoveryRequired, "layout_revision_mismatch", "The saved layout does not match its durable identity or revision.");
                IReadOnlyList<FurnitureDiagnostic> diagnostics = HomeFurnitureRepository.ValidateDocument(document);
                FurnitureDiagnostic blocking = diagnostics.FirstOrDefault(diagnostic => diagnostic.severity == "error");
                if (blocking != null) return LoadFailure(GamePersistenceStates.RecoveryRequired, blocking.code, blocking.message);
                return new FurniturePersistenceLoadResult
                {
                    state = "loaded",
                    document = document,
                    gameRevision = current.gameRevision,
                    layoutRevision = current.layoutRevision,
                    orphanInstanceIds = new List<string>().AsReadOnly(),
                    diagnostics = diagnostics
                };
            }
            catch (Exception exception)
            {
                return LoadFailure(GamePersistenceStates.RecoveryRequired, "layout_json_invalid", exception.Message);
            }
        }

        private FurniturePersistenceLoadResult ReadExistingHome(GameLoadResult current, string homeId, int retry)
        {
            FurniturePersistenceLoadResult loaded = ReadLayout(current, homeId);
            if (loaded.state != "loaded" || loaded.document.schemaVersion != 1) return loaded;

            HomeFurnitureLayoutDocument upgraded;
            try
            {
                upgraded = HomeFurnitureRepository.CloneDocument(loaded.document);
                upgraded.schemaVersion = 2;
                upgraded.inventory = FurnitureInventorySeed.CreateInitial();
            }
            catch (Exception exception)
            {
                return LoadFailure(GamePersistenceStates.Invalid, "home_inventory_upgrade_failed", exception.Message);
            }

            FurniturePersistenceCommitResult commit = CommitLayout(Guid.NewGuid().ToString("D"),
                current.gameRevision, current.layoutRevision, upgraded, "home_inventory_initialized");
            if (commit.state == GamePersistenceStates.Committed)
            {
                GameLoadResult latest = repository.Load();
                if (latest.state != GamePersistenceStates.Ready)
                    return LoadFailure(latest.state, latest.errorCode, latest.message);
                return ReadLayout(latest, homeId);
            }

            if (commit.state == GamePersistenceStates.StaleRevision && retry == 0)
            {
                GameLoadResult latest = repository.Load();
                if (latest.state == GamePersistenceStates.Ready && latest.layoutJson != null)
                    return ReadExistingHome(latest, homeId, 1);
            }
            return LoadFailure(commit.state, commit.errorCode, commit.message);
        }

        private static HomeFurnitureLayoutDocument PrepareNewHomeSeed(HomeFurnitureLayoutDocument seed)
        {
            HomeFurnitureLayoutDocument candidate = HomeFurnitureRepository.CloneDocument(seed);
            if (candidate.schemaVersion == 1)
                candidate.schemaVersion = 2;
            candidate.inventory = FurnitureInventorySeed.CreateInitial();
            return candidate;
        }

        private sealed class FurnitureEventDescriptor
        {
            public string kind;
            public string targetId;
            public string payloadJson;
        }

        private static FurnitureEventDescriptor DescribeHomeChange(string kind, HomeFurnitureLayoutDocument document)
        {
            return new FurnitureEventDescriptor
            {
                kind = kind,
                targetId = document.homeId,
                payloadJson = "{\"layoutRevision\":" + document.layoutRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"instanceCount\":" + document.instances.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"inventoryEntryCount\":" + (document.inventory == null ? 0 : document.inventory.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"
            };
        }

        private static FurnitureEventDescriptor DescribeFurnitureChange(string previousLayoutJson,
            HomeFurnitureLayoutDocument candidate)
        {
            HomeFurnitureLayoutDocument previous = null;
            if (!string.IsNullOrWhiteSpace(previousLayoutJson))
            {
                try { previous = Deserialize(previousLayoutJson); }
                catch (Exception) { }
            }
            if (previous == null || previous.instances == null)
                return DescribeHomeChange("home_layout_committed", candidate);

            var previousById = previous.instances.Where(instance => instance != null)
                .ToDictionary(instance => instance.instanceId, StringComparer.Ordinal);
            var candidateById = candidate.instances.Where(instance => instance != null)
                .ToDictionary(instance => instance.instanceId, StringComparer.Ordinal);
            List<HomeFurnitureLayoutInstance> added = candidate.instances.Where(instance => instance != null && !previousById.ContainsKey(instance.instanceId)).ToList();
            List<HomeFurnitureLayoutInstance> removed = previous.instances.Where(instance => instance != null && !candidateById.ContainsKey(instance.instanceId)).ToList();
            if (added.Count == 1 && removed.Count == 0)
                return DescribeFurnitureChange("furniture_placed", added[0], candidate);
            if (removed.Count == 1 && added.Count == 0)
                return DescribeFurnitureChange("furniture_returned", removed[0], candidate);
            if (added.Count == 0 && removed.Count == 0)
            {
                List<HomeFurnitureLayoutInstance> moved = candidate.instances.Where(instance => instance != null && previousById.ContainsKey(instance.instanceId) &&
                    HasPoseChanged(previousById[instance.instanceId], instance)).ToList();
                if (moved.Count == 1) return DescribeFurnitureChange("furniture_moved", moved[0], candidate);
            }
            return DescribeHomeChange("home_layout_committed", candidate);
        }

        private static FurnitureEventDescriptor DescribeFurnitureChange(string kind,
            HomeFurnitureLayoutInstance instance, HomeFurnitureLayoutDocument candidate)
        {
            FurnitureInventoryEntry stock = candidate.inventory == null ? null : candidate.inventory.FirstOrDefault(entry => entry != null && entry.definitionId == instance.definitionId);
            return new FurnitureEventDescriptor
            {
                kind = kind,
                targetId = instance.instanceId,
                payloadJson = "{\"layoutRevision\":" + candidate.layoutRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"instanceId\":" + QuoteJson(instance.instanceId) +
                    ",\"definitionId\":" + QuoteJson(instance.definitionId) +
                    (stock == null ? string.Empty : ",\"storedCount\":" + stock.storedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "}"
            };
        }

        private static string ValidateInventoryTransition(string previousLayoutJson,
            HomeFurnitureLayoutDocument candidate, string eventKind)
        {
            HomeFurnitureLayoutDocument previous;
            try { previous = Deserialize(previousLayoutJson); }
            catch (Exception exception) { return "The saved home layout could not be read: " + exception.Message; }
            if (previous == null) return "The saved home layout is missing.";
            if (previous.schemaVersion == 1 && candidate.schemaVersion == 2 && eventKind == "home_inventory_initialized")
                return null;
            if (previous.schemaVersion != 2 || candidate.schemaVersion != 2)
                return "Only an explicit schema-one inventory initialization may change the home inventory schema.";

            var previousCounts = InventoryCounts(previous.inventory);
            var candidateCounts = InventoryCounts(candidate.inventory);
            List<string> changedDefinitions = previousCounts.Keys.Union(candidateCounts.Keys, StringComparer.Ordinal)
                .Where(definitionId => GetCount(previousCounts, definitionId) != GetCount(candidateCounts, definitionId)).ToList();

            var previousIds = new HashSet<string>(previous.instances.Where(instance => instance != null).Select(instance => instance.instanceId), StringComparer.Ordinal);
            var candidateIds = new HashSet<string>(candidate.instances.Where(instance => instance != null).Select(instance => instance.instanceId), StringComparer.Ordinal);
            List<HomeFurnitureLayoutInstance> added = candidate.instances.Where(instance => instance != null && !previousIds.Contains(instance.instanceId)).ToList();
            List<HomeFurnitureLayoutInstance> removed = previous.instances.Where(instance => instance != null && !candidateIds.Contains(instance.instanceId)).ToList();
            if (added.Count == 1 && removed.Count == 0)
            {
                if (changedDefinitions.Count == 0) return null;
                if (changedDefinitions.Count != 1 || changedDefinitions[0] != added[0].definitionId)
                    return "A placement may change only its matching stored item count.";
                int delta = GetCount(candidateCounts, added[0].definitionId) - GetCount(previousCounts, added[0].definitionId);
                return delta == 0 || delta == -1 ? null : "A placement may consume at most one matching stored item.";
            }
            if (removed.Count == 1 && added.Count == 0)
            {
                if (changedDefinitions.Count != 1 || changedDefinitions[0] != removed[0].definitionId)
                    return "Returning a placed item must credit exactly one matching stored item.";
                int delta = GetCount(candidateCounts, removed[0].definitionId) - GetCount(previousCounts, removed[0].definitionId);
                return delta == 1 ? null : "Returning a placed item must credit exactly one matching stored item.";
            }
            if (added.Count != 0 || removed.Count != 0) return "One home commit may place or return only one furniture instance.";
            if (changedDefinitions.Count == 0) return null;
            return "Stored counts may change only with one matching placement or return.";
        }

        private static Dictionary<string, int> InventoryCounts(IEnumerable<FurnitureInventoryEntry> entries)
        {
            return (entries ?? Enumerable.Empty<FurnitureInventoryEntry>()).Where(entry => entry != null)
                .ToDictionary(entry => entry.definitionId, entry => entry.storedCount, StringComparer.Ordinal);
        }

        private static int GetCount(IDictionary<string, int> counts, string definitionId)
        {
            int value;
            return counts.TryGetValue(definitionId, out value) ? value : 0;
        }

        private static bool HasPoseChanged(HomeFurnitureLayoutInstance left, HomeFurnitureLayoutInstance right)
        {
            return !SameVector(left.positionMeters, right.positionMeters) || !SameQuaternion(left.rotation, right.rotation) ||
                !SameVector(left.scale, right.scale) || !SameSupport(left.support, right.support);
        }

        private static bool SameVector(FurnitureVector3 left, FurnitureVector3 right)
        {
            return left != null && right != null && left.x == right.x && left.y == right.y && left.z == right.z;
        }

        private static bool SameQuaternion(FurnitureQuaternion left, FurnitureQuaternion right)
        {
            return left != null && right != null && left.x == right.x && left.y == right.y && left.z == right.z && left.w == right.w;
        }

        private static bool SameSupport(FurnitureLayoutSupportReference left, FurnitureLayoutSupportReference right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return left.supportId == right.supportId && left.supportKind == right.supportKind &&
                left.supportFrameRevision == right.supportFrameRevision && left.attachmentPointId == right.attachmentPointId &&
                SameVector(left.localPositionMeters, right.localPositionMeters) && SameQuaternion(left.localRotation, right.localRotation);
        }

        private static string QuoteJson(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < ' ') builder.Append("\\u").Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }

        private static string Serialize(HomeFurnitureLayoutDocument document)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument)).WriteObject(stream, document);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes) result.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static HomeFurnitureLayoutDocument Deserialize(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);
            using (var stream = new MemoryStream(bytes, false))
                return (HomeFurnitureLayoutDocument)new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument)).ReadObject(stream);
        }

        private static FurniturePersistenceLoadResult LoadFailure(string state, string code, string message)
        {
            return new FurniturePersistenceLoadResult
            {
                state = state,
                errorCode = code,
                message = message,
                orphanInstanceIds = new List<string>().AsReadOnly(),
                diagnostics = new List<FurnitureDiagnostic>().AsReadOnly()
            };
        }

        private static FurniturePersistenceCommitResult FailedCommit(string state, string code, string message)
        {
            return new FurniturePersistenceCommitResult { state = state, errorCode = code, message = message };
        }
    }
}
