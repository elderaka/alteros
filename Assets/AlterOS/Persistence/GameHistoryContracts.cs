using System;
using System.Collections.Generic;

namespace AlterOS.Persistence
{
    public static class GameHistoryStatIds
    {
        public static bool IsSupported(string ownerKind, string statId)
        {
            if (string.IsNullOrWhiteSpace(ownerKind) || string.IsNullOrWhiteSpace(statId) || statId.Length > 128) return false;
            string id = statId.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            string prefix = ownerKind + ".";
            if (id.StartsWith(prefix, StringComparison.Ordinal)) id = id.Substring(prefix.Length);
            if (ownerKind == "alterra")
                return id == "bond_level" || id == "bond_xp" || id == "affinity" || id == "trust" || id == "comfort" ||
                    id == "energy" || id == "hydration" || id == "engagement" || id == "stimulation" || id == "attention" ||
                    id == "reassurance" || id == "boundary_strain" || id.StartsWith("emotion.", StringComparison.Ordinal);
            if (ownerKind == "player")
                return id == "credits" || id == "completed_tasks" || id == "focus_sessions_completed" || id == "streak" ||
                    id == "productivity_xp" || id == "reliability" || id == "focus_energy" || id == "stress" || id == "rest" ||
                    id == "task_progress" || id == "focus_progress";
            if (ownerKind == "environment")
                return id == "comfort" || id == "cleanliness" || id == "lighting" || id == "noise" || id == "temperature" ||
                    id == "coziness" || id == "personalization" || id == "focus_atmosphere";
            return ownerKind == "home" && (id == "condition" || id == "cleanliness" || id == "comfort");
        }
    }

    [Serializable]
    public sealed class GameEventHistoryContext
    {
        public string eventId;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string actionExecutionId;
        public string causeKind;
        public string causeReferenceId;
        public string simulationRuleId;
        public string simulationRuleVersion;
        public string intervalStartUtc;
        public string intervalEndUtc;
    }

    [Serializable]
    public sealed class GameStatChangeRecord
    {
        public string changeId;
        public string eventId;
        public string ownerKind;
        public string ownerId;
        public string statId;
        public bool beforeKnown;
        public float? beforeValue;
        public bool afterKnown;
        public float? afterValue;
        public float? appliedDelta;
        public bool aggregated;
        public long historySequence;
    }

    [Serializable]
    public sealed class GameActionCheckpointRecord
    {
        public string checkpointId;
        public string eventId;
        public string actionExecutionId;
        public string outcome;
        public string occurredAtUtc;
    }

    [Serializable]
    public sealed class GameAnswerRecord
    {
        public string answerId;
        public string eventId;
        public string stepExecutionId;
        public string answerKind;
        public string optionId;
        public string submittedAtUtc;
        public long historySequence;
    }

    [Serializable]
    public sealed class GameScenarioRunRecord
    {
        public string runId;
        public string scenarioId;
        public string startedAtUtc;
        public string endedAtUtc;
        public string outcome;
    }

    [Serializable]
    public sealed class GameFlowInvocationRecord
    {
        public string invocationId;
        public string runId;
        public string parentInvocationId;
        public string callExecutionId;
        public string flowId;
        public string flowStoreId;
        public string flowRevision;
        public string flowHash;
        public string startedAtUtc;
    }

    [Serializable]
    public sealed class GameStepExecutionRecord
    {
        public string executionId;
        public string invocationId;
        public string stepId;
        public int visitOrdinal;
        public string presentedAtUtc;
        public string outcome;
        public string completedAtUtc;
    }

    [Serializable]
    public sealed class GameScenarioHostEffectEvidenceResult
    {
        public string state;
        public string flowId;
        public string flowStoreId;
        public string flowRevision;
        public string flowHash;
        public string effectStepId;
        public string effectStepExecutionId;
        public string effectOutcome;
        public string completedStepId;
        public string completedStepExecutionId;
        public string completedStepOutcome;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class GameActionExecutionRecord
    {
        public string actionExecutionId;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string capabilityId;
        public string status;
        public string startedAtUtc;
        public string endedAtUtc;
    }

    public sealed class GameHistoryQuery
    {
        public long afterSequence;
        public int pageSize = 50;
    }

    [Serializable]
    public sealed class GameHistoryEvent
    {
        public long historySequence;
        public string eventId;
        public string operationId;
        public string kind;
        public string actorId;
        public string targetId;
        public string occurredAtUtc;
        public string recordedAtUtc;
        public int schemaVersion;
        public string payloadJson;
        public string runId;
        public string invocationId;
        public string stepExecutionId;
        public string actionExecutionId;
        public string causeKind;
        public string causeReferenceId;
        public string simulationRuleId;
        public string simulationRuleVersion;
        public string intervalStartUtc;
        public string intervalEndUtc;
    }

    public sealed class GameHistoryReader
    {
        private const int MaximumPageSize = 128;
        private readonly string databasePath;

        public GameHistoryReader(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A game database path is required.", "databasePath");
            this.databasePath = System.IO.Path.GetFullPath(databasePath);
        }

        public IList<GameHistoryEvent> ReadEvents(GameHistoryQuery query)
        {
            ValidateQuery(query);
            using (var connection = new SQLite.SQLiteConnection(databasePath, SQLite.SQLiteOpenFlags.ReadOnly))
            {
                RequireSchema2(connection);
                return connection.Query<GameHistoryEventRow>(
                    "SELECT e.history_sequence,e.event_id,e.operation_id,e.kind,e.actor_id,e.target_id,e.occurred_at_utc,e.recorded_at_utc,e.schema_version,e.payload_json," +
                    "c.run_id,c.invocation_id,c.step_execution_id,c.action_execution_id,c.cause_kind,c.cause_reference_id,c.simulation_rule_id,c.simulation_rule_version,c.interval_start_utc,c.interval_end_utc " +
                    "FROM game_events e LEFT JOIN event_context c ON c.event_id=e.event_id WHERE e.history_sequence>? ORDER BY e.history_sequence LIMIT ?",
                    query.afterSequence, query.pageSize).ConvertAll(row => new GameHistoryEvent
                    {
                        historySequence = row.history_sequence, eventId = row.event_id, operationId = row.operation_id,
                        kind = row.kind, actorId = row.actor_id, targetId = row.target_id,
                        occurredAtUtc = row.occurred_at_utc, recordedAtUtc = row.recorded_at_utc,
                        schemaVersion = row.schema_version, payloadJson = row.payload_json, runId = row.run_id,
                        invocationId = row.invocation_id, stepExecutionId = row.step_execution_id,
                        actionExecutionId = row.action_execution_id, causeKind = row.cause_kind,
                        causeReferenceId = row.cause_reference_id, simulationRuleId = row.simulation_rule_id,
                        simulationRuleVersion = row.simulation_rule_version, intervalStartUtc = row.interval_start_utc,
                        intervalEndUtc = row.interval_end_utc
                    });
            }
        }

        public IList<GameStatChangeRecord> ReadStatChanges(GameHistoryQuery query)
        {
            ValidateQuery(query);
            using (var connection = new SQLite.SQLiteConnection(databasePath, SQLite.SQLiteOpenFlags.ReadOnly))
            {
                RequireSchema2(connection);
                return connection.Query<GameStatChangeRow>(
                    "SELECT history_sequence,change_id,event_id,owner_kind,owner_id,stat_id,before_known,before_value,after_known,after_value,applied_delta,aggregated " +
                    "FROM stat_changes WHERE history_sequence>? ORDER BY history_sequence LIMIT ?",
                    query.afterSequence, query.pageSize).ConvertAll(row => new GameStatChangeRecord
                    {
                        historySequence = row.history_sequence, changeId = row.change_id, eventId = row.event_id,
                        ownerKind = row.owner_kind, ownerId = row.owner_id, statId = row.stat_id,
                        beforeKnown = row.before_known != 0, beforeValue = row.before_value,
                        afterKnown = row.after_known != 0, afterValue = row.after_value,
                        appliedDelta = row.applied_delta, aggregated = row.aggregated != 0
                    });
            }
        }

        public IList<GameAnswerRecord> ReadAnswers(GameHistoryQuery query)
        {
            ValidateQuery(query);
            using (var connection = new SQLite.SQLiteConnection(databasePath, SQLite.SQLiteOpenFlags.ReadOnly))
            {
                RequireSchema2(connection);
                return connection.Query<GameAnswerRow>(
                    "SELECT history_sequence,answer_id,event_id,step_execution_id,answer_kind,option_id,submitted_at_utc " +
                    "FROM answers WHERE history_sequence>? ORDER BY history_sequence LIMIT ?",
                    query.afterSequence, query.pageSize)
                    .ConvertAll(row => new GameAnswerRecord
                    {
                        historySequence = row.history_sequence,
                        answerId = row.answer_id, eventId = row.event_id, stepExecutionId = row.step_execution_id,
                        answerKind = row.answer_kind, optionId = row.option_id, submittedAtUtc = row.submitted_at_utc
                    });
            }
        }

        private static void ValidateQuery(GameHistoryQuery query)
        {
            if (query == null || query.afterSequence < 0 || query.pageSize < 1 || query.pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException("query", "History pages require a non-negative cursor and a size from 1 through 128.");
        }

        private static void RequireSchema2(SQLite.SQLiteConnection connection)
        {
            if (connection.ExecuteScalar<int>("PRAGMA user_version") != GameSchemaMigrations.CurrentVersion ||
                !GameSchemaMigrations.HasRequiredTables(connection))
                throw new InvalidOperationException("The game history schema is unavailable or requires migration.");
        }

        private sealed class GameHistoryEventRow
        {
            public long history_sequence { get; set; }
            public string event_id { get; set; }
            public string operation_id { get; set; }
            public string kind { get; set; }
            public string actor_id { get; set; }
            public string target_id { get; set; }
            public string occurred_at_utc { get; set; }
            public string recorded_at_utc { get; set; }
            public int schema_version { get; set; }
            public string payload_json { get; set; }
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

        private sealed class GameStatChangeRow
        {
            public long history_sequence { get; set; }
            public string change_id { get; set; }
            public string event_id { get; set; }
            public string owner_kind { get; set; }
            public string owner_id { get; set; }
            public string stat_id { get; set; }
            public int before_known { get; set; }
            public float? before_value { get; set; }
            public int after_known { get; set; }
            public float? after_value { get; set; }
            public float? applied_delta { get; set; }
            public int aggregated { get; set; }
        }

        private sealed class GameAnswerRow
        {
            public long history_sequence { get; set; }
            public string answer_id { get; set; }
            public string event_id { get; set; }
            public string step_execution_id { get; set; }
            public string answer_kind { get; set; }
            public string option_id { get; set; }
            public string submitted_at_utc { get; set; }
        }
    }
}
