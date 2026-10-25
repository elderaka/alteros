using System;
using System.Collections.Generic;

namespace AlterOS.Persistence
{
    public static class GamePersistenceStates
    {
        public const string Ready = "ready";
        public const string Committed = "committed";
        public const string Duplicate = "duplicate";
        public const string StaleRevision = "stale_revision";
        public const string ConflictingOperation = "conflicting_operation";
        public const string RecoveryRequired = "recovery_required";
        public const string Unavailable = "unavailable";
        public const string Invalid = "invalid";
        public const string Missing = "missing";
        public const string Imported = "imported";
        public const string AlreadyImported = "already_imported";
        public const string NoSource = "no_source";
        public const string RecoveryStaged = "staged";
        public const string Recovered = "recovered";
    }

    [Serializable]
    public sealed class GameSaveIdentity
    {
        public string saveId;
        public string alterraId;
        public string homeId;
        public string accountId;
        public string legacyPlayerId;
    }

    [Serializable]
    public sealed class GameEventRecord
    {
        public string eventId;
        public string operationId;
        public string kind;
        public string actorId;
        public string targetId;
        public string occurredAtUtc;
        public string recordedAtUtc;
        public int schemaVersion = 1;
        public string payloadJson;
        public long historySequence;
    }

    [Serializable]
    public sealed class GameCommitRequest
    {
        public string operationId;
        public string operationPayload;
        public long expectedGameRevision;
        public long? expectedLayoutRevision;
        public string progressionJson;
        public string layoutJson;
        public List<GameEventRecord> events = new List<GameEventRecord>();
        public List<GameEventHistoryContext> eventContexts = new List<GameEventHistoryContext>();
        public List<GameStatChangeRecord> statChanges = new List<GameStatChangeRecord>();
        public List<GameActionCheckpointRecord> actionCheckpoints = new List<GameActionCheckpointRecord>();
        public List<GameAnswerRecord> answers = new List<GameAnswerRecord>();
        public List<GameScenarioRunRecord> scenarioRuns = new List<GameScenarioRunRecord>();
        public List<GameFlowInvocationRecord> flowInvocations = new List<GameFlowInvocationRecord>();
        public List<GameStepExecutionRecord> stepExecutions = new List<GameStepExecutionRecord>();
        public List<GameActionExecutionRecord> actionExecutions = new List<GameActionExecutionRecord>();
        public List<GameItemQuantityChangeRecord> itemQuantityChanges = new List<GameItemQuantityChangeRecord>();
        public string migrationSourceHash;
        public byte[] migrationSourceBytes;
    }

    [Serializable]
    public sealed class GameItemQuantityChangeRecord
    {
        public string changeId;
        public string eventId;
        public string ownerId;
        public string actionStoreId;
        public string actionId;
        public string actionRevision;
        public string actionExpectedHash;
        public string itemId;
        public string itemRevision;
        public long expectedQuantity;
        public long delta;
        public long maximumQuantity;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string requiresCompletedStepId;
        public string completedDeliveryStepExecutionId;
    }

    [Serializable]
    public sealed class GameItemQuantityView
    {
        public string itemId;
        public long quantity;
    }

    [Serializable]
    public sealed class GameItemInventoryResult
    {
        public string state;
        public string playerId;
        public long gameRevision;
        public List<GameItemQuantityView> items = new List<GameItemQuantityView>();
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class GameItemQuantityOperationResult
    {
        public string state;
        public string operationId;
        public GameCommitReceipt receipt;
        public GameItemQuantityChangeRecord change;
        public GameEventHistoryContext eventContext;
        public string errorCode;
        public string message;

        public bool IsFound { get { return state == "found" && receipt != null && change != null && eventContext != null; } }
    }

    [Serializable]
    public sealed class GameFurniturePowerOperationResult
    {
        public string state;
        public string operationId;
        public GameCommitReceipt receipt;
        public string instanceId;
        public string definitionId;
        public string usePointId;
        public string useId;
        public string reservationOwnerId;
        public int usePointRevision;
        public int desiredState;
        public long committedInstanceRevision;
        public long committedLayoutRevision;
        public long committedGameRevision;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string activityId;
        public string sequenceStepId;
        public long expectedInstanceRevision;
        public long expectedLayoutRevision;
        public long expectedGameRevision;
        public string errorCode;
        public string message;
        public GameEventHistoryContext eventContext;

        public bool IsFound { get { return state == "found" && receipt != null; } }
    }

    [Serializable]
    public sealed class GameCommitReceipt
    {
        public string operationId;
        public string payloadHash;
        public long gameRevision;
        public long layoutRevision;
        public string state;
    }

    [Serializable]
    public sealed class GameCommitResult
    {
        public string state;
        public GameCommitReceipt receipt;
        public string errorCode;
        public string message;

        public bool IsDurable
        {
            get { return state == GamePersistenceStates.Committed || state == GamePersistenceStates.Duplicate; }
        }
    }

    [Serializable]
    public sealed class GameLoadResult
    {
        public string state;
        public GameSaveIdentity identity;
        public long gameRevision;
        public long layoutRevision;
        public string progressionJson;
        public string layoutJson;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class GameMigrationReceipt
    {
        public string sourceHash;
        public string operationId;
        public string importedAtUtc;
        public long gameRevision;
    }

    [Serializable]
    public sealed class ProgressionImportResult
    {
        public string state;
        public string sourceHash;
        public int sourceSchemaVersion;
        public string unmappedFieldsStatus;
        public GameCommitResult commit;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class GameBackupResult
    {
        public string state;
        public string backupId;
        public string path;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class GameBackupValidationResult
    {
        public string state;
        public string backupId;
        public GameSaveIdentity identity;
        public long gameRevision;
        public long layoutRevision;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class GameRecoveryResult
    {
        public string state;
        public GameRecoveryCandidate candidate;
        public string errorCode;
        public string message;
    }

    public interface IGameRepository
    {
        string DatabasePath { get; }
        GameLoadResult Load();
        GameCommitResult Commit(GameCommitRequest request);
        GameItemInventoryResult ReadPlayerItemQuantities();
        GameScenarioHostEffectEvidenceResult ReadScenarioHostEffectEvidence(string runId, string invocationId,
            string effectStepExecutionId, string completedStepExecutionId);
        GameItemQuantityOperationResult FindItemQuantityOperation(string operationId);
        GameFurniturePowerOperationResult FindFurniturePowerOperation(string operationId);
        GameCommitReceipt FindReceipt(string operationId);
        GameMigrationReceipt FindMigration(string sourceHash);
        bool CreateConsistentBackup(string destinationPath);
        GameBackupValidationResult ValidateBackup(string path);
    }
}
