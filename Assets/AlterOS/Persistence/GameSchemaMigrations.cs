using SQLite;

namespace AlterOS.Persistence
{
    internal static class GameSchemaMigrations
    {
        internal const int CurrentVersion = 5;
        private const int Version1 = 1;
        private const int Version2 = 2;
        private const int Version3 = 3;
        private const int Version4 = 4;
        private const int Version5 = 5;

        internal static void CreateVersion1(SQLiteConnection connection, GameSaveIdentity identity)
        {
            connection.Execute("CREATE TABLE game_meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
            connection.Execute("CREATE TABLE game_snapshots (domain TEXT PRIMARY KEY NOT NULL, revision INTEGER NOT NULL, payload_json TEXT NOT NULL)");
            connection.Execute("CREATE TABLE home_layout_snapshots (home_id TEXT PRIMARY KEY NOT NULL, revision INTEGER NOT NULL, payload_json TEXT NOT NULL)");
            connection.Execute("CREATE TABLE game_events (event_id TEXT PRIMARY KEY NOT NULL, operation_id TEXT NOT NULL, kind TEXT NOT NULL, actor_id TEXT NOT NULL, target_id TEXT NOT NULL, occurred_at_utc TEXT NOT NULL, recorded_at_utc TEXT NOT NULL, schema_version INTEGER NOT NULL, payload_json TEXT NOT NULL)");
            connection.Execute("CREATE INDEX game_events_operation_id ON game_events(operation_id)");
            connection.Execute("CREATE TABLE operation_receipts (operation_id TEXT PRIMARY KEY NOT NULL, payload_hash TEXT NOT NULL, game_revision INTEGER NOT NULL, layout_revision INTEGER NOT NULL, result_state TEXT NOT NULL)");
            connection.Execute("CREATE TABLE migration_receipts (source_hash TEXT PRIMARY KEY NOT NULL, operation_id TEXT NOT NULL, imported_at_utc TEXT NOT NULL, game_revision INTEGER NOT NULL)");
            connection.Execute("CREATE TABLE legacy_source_backups (source_hash TEXT PRIMARY KEY NOT NULL, source_bytes BLOB NOT NULL)");

            SetMeta(connection, "save_id", identity.saveId);
            SetMeta(connection, "alterra_id", identity.alterraId);
            SetMeta(connection, "home_id", identity.homeId);
            SetMeta(connection, "account_id", string.Empty);
            SetMeta(connection, "legacy_player_id", string.Empty);
            SetMeta(connection, "game_revision", "0");
            SetMeta(connection, "layout_revision", "0");
            connection.Execute("PRAGMA user_version = " + Version1);
        }

        internal static bool HasRequiredTables(SQLiteConnection connection)
        {
            return HasVersion4Tables(connection) &&
                HasColumns(connection, "flow_invocations", "flow_store_id", "flow_revision", "flow_hash");
        }

        internal static bool HasVersion4Tables(SQLiteConnection connection)
        {
            return HasVersion3Tables(connection) &&
                HasColumns(connection, "player_item_quantities", "player_id", "item_id", "quantity") &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='player_item_quantities'") == 1;
        }

        internal static bool HasVersion3Tables(SQLiteConnection connection)
        {
            return HasVersion2Tables(connection) &&
                HasColumns(connection, "topic_history_state", "singleton_id", "revision") &&
                HasColumns(connection, "topic_occurrences", "instance_id", "topic_id", "replay_policy", "started_utc_ticks",
                    "presented_utc_ticks", "completed_utc_ticks", "consumed_utc_ticks", "cooldown_until_utc_ticks", "record_json") &&
                HasColumns(connection, "topic_scenario_completions", "instance_id", "scenario_id", "completion_count", "last_completed_utc_ticks", "payload_json") &&
                HasColumns(connection, "topic_history_imports", "singleton_id", "source_hash", "operation_id", "imported_at_utc", "record_count") &&
                HasColumns(connection, "topic_history_operations", "operation_id", "payload_hash", "revision");
        }

        internal static bool HasVersion2Tables(SQLiteConnection connection)
        {
            if (!HasVersion1Tables(connection)) return false;
            string[] required =
            {
                "scenario_runs", "flow_invocations", "step_executions", "variable_values", "action_executions",
                "event_context", "action_checkpoints", "stat_changes", "answers"
            };
            foreach (string table in required)
            {
                if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1)
                    return false;
            }
            return HasColumns(connection, "game_events", "history_sequence") &&
                HasColumns(connection, "operation_receipts", "legacy_payload_hash") &&
                HasColumns(connection, "scenario_runs", "run_id", "scenario_id", "started_at_utc", "ended_at_utc", "outcome") &&
                HasColumns(connection, "flow_invocations", "invocation_id", "run_id", "parent_invocation_id", "call_execution_id", "flow_id", "started_at_utc") &&
                HasColumns(connection, "step_executions", "execution_id", "invocation_id", "step_id", "visit_ordinal", "presented_at_utc", "outcome", "completed_at_utc") &&
                HasColumns(connection, "variable_values", "invocation_id", "name", "value_type", "value_json", "updated_at_utc") &&
                HasColumns(connection, "action_executions", "action_execution_id", "run_id", "invocation_id", "step_execution_id", "capability_id", "status", "started_at_utc", "ended_at_utc") &&
                HasColumns(connection, "event_context", "event_id", "run_id", "invocation_id", "step_execution_id", "action_execution_id", "cause_kind", "cause_reference_id") &&
                HasColumns(connection, "action_checkpoints", "checkpoint_id", "event_id", "action_execution_id", "outcome", "occurred_at_utc") &&
                HasColumns(connection, "stat_changes", "history_sequence", "change_id", "event_id", "owner_kind", "owner_id", "stat_id", "before_known", "before_value", "after_known", "after_value", "applied_delta", "aggregated") &&
                HasColumns(connection, "answers", "history_sequence", "answer_id", "event_id", "step_execution_id", "answer_kind", "option_id", "submitted_at_utc") &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='game_events_history_sequence'") == 1;
        }

        internal static bool HasVersion1Tables(SQLiteConnection connection)
        {
            string[] required =
            {
                "game_meta", "game_snapshots", "home_layout_snapshots", "game_events",
                "operation_receipts", "migration_receipts", "legacy_source_backups"
            };
            foreach (string table in required)
            {
                if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1)
                    return false;
            }
            return true;
        }

        internal static void MigrateVersion1To2(SQLiteConnection connection)
        {
            connection.Execute("ALTER TABLE operation_receipts ADD COLUMN legacy_payload_hash TEXT NULL");
            connection.Execute("ALTER TABLE game_events ADD COLUMN history_sequence INTEGER NOT NULL DEFAULT 0");
            connection.Execute("UPDATE game_events SET history_sequence=rowid");
            connection.Execute("CREATE UNIQUE INDEX game_events_history_sequence ON game_events(history_sequence)");
            connection.Execute("CREATE TABLE event_context (" +
                "event_id TEXT PRIMARY KEY NOT NULL REFERENCES game_events(event_id)," +
                "run_id TEXT NULL, invocation_id TEXT NULL, step_execution_id TEXT NULL, action_execution_id TEXT NULL," +
                "cause_kind TEXT NULL CHECK(cause_kind IS NULL OR cause_kind IN ('source_operation','action_checkpoint','prior_event','simulation_checkpoint'))," +
                "cause_reference_id TEXT NULL, simulation_rule_id TEXT NULL, simulation_rule_version TEXT NULL," +
                "interval_start_utc TEXT NULL, interval_end_utc TEXT NULL," +
                "CHECK((cause_kind IS NULL AND cause_reference_id IS NULL) OR cause_kind IS NOT NULL))");
            connection.Execute("CREATE INDEX event_context_run_id ON event_context(run_id)");
            connection.Execute("CREATE INDEX event_context_invocation_id ON event_context(invocation_id)");
            connection.Execute("CREATE TABLE scenario_runs (" +
                "run_id TEXT PRIMARY KEY NOT NULL, scenario_id TEXT NOT NULL, started_at_utc TEXT NOT NULL," +
                "ended_at_utc TEXT NULL, outcome TEXT NULL," +
                "CHECK((ended_at_utc IS NULL AND outcome IS NULL) OR (ended_at_utc IS NOT NULL AND outcome IS NOT NULL)))");
            connection.Execute("CREATE TABLE flow_invocations (" +
                "invocation_id TEXT PRIMARY KEY NOT NULL, run_id TEXT NOT NULL REFERENCES scenario_runs(run_id)," +
                "parent_invocation_id TEXT NULL REFERENCES flow_invocations(invocation_id), call_execution_id TEXT NULL, flow_id TEXT NOT NULL, started_at_utc TEXT NOT NULL)");
            connection.Execute("CREATE INDEX flow_invocations_run_id ON flow_invocations(run_id)");
            connection.Execute("CREATE TABLE step_executions (" +
                "execution_id TEXT PRIMARY KEY NOT NULL, invocation_id TEXT NOT NULL REFERENCES flow_invocations(invocation_id)," +
                "step_id TEXT NOT NULL, visit_ordinal INTEGER NOT NULL CHECK(visit_ordinal>0), presented_at_utc TEXT NOT NULL," +
                "outcome TEXT NULL, completed_at_utc TEXT NULL, UNIQUE(invocation_id,step_id,visit_ordinal)," +
                "CHECK((outcome IS NULL AND completed_at_utc IS NULL) OR (outcome IS NOT NULL AND completed_at_utc IS NOT NULL)))");
            connection.Execute("CREATE INDEX step_executions_invocation_id ON step_executions(invocation_id)");
            connection.Execute("CREATE TABLE variable_values (" +
                "invocation_id TEXT NOT NULL REFERENCES flow_invocations(invocation_id), name TEXT NOT NULL," +
                "value_type TEXT NOT NULL, value_json TEXT NOT NULL CHECK(length(value_json)<=2048), updated_at_utc TEXT NOT NULL," +
                "PRIMARY KEY(invocation_id,name))");
            connection.Execute("CREATE TABLE action_executions (" +
                "action_execution_id TEXT PRIMARY KEY NOT NULL, run_id TEXT NOT NULL REFERENCES scenario_runs(run_id)," +
                "invocation_id TEXT NOT NULL REFERENCES flow_invocations(invocation_id)," +
                "step_execution_id TEXT NULL REFERENCES step_executions(execution_id), capability_id TEXT NOT NULL," +
                "status TEXT NOT NULL CHECK(status IN ('requested','succeeded','failed','cancelled','deferred'))," +
                "started_at_utc TEXT NOT NULL, ended_at_utc TEXT NULL)");
            connection.Execute("CREATE INDEX action_executions_run_id ON action_executions(run_id)");
            connection.Execute("CREATE TABLE action_checkpoints (" +
                "checkpoint_id TEXT PRIMARY KEY NOT NULL, event_id TEXT NOT NULL REFERENCES game_events(event_id)," +
                "action_execution_id TEXT NOT NULL, outcome TEXT NOT NULL CHECK(outcome IN ('succeeded','failed','cancelled','deferred'))," +
                "occurred_at_utc TEXT NOT NULL)");
            connection.Execute("CREATE INDEX action_checkpoints_execution_id ON action_checkpoints(action_execution_id)");
            connection.Execute("CREATE TABLE stat_changes (" +
                "history_sequence INTEGER PRIMARY KEY AUTOINCREMENT, change_id TEXT NOT NULL UNIQUE," +
                "event_id TEXT NOT NULL REFERENCES game_events(event_id)," +
                "owner_kind TEXT NOT NULL CHECK(owner_kind IN ('alterra','player','home','environment'))," +
                "owner_id TEXT NOT NULL, stat_id TEXT NOT NULL," +
                "before_known INTEGER NOT NULL CHECK(before_known IN (0,1)), before_value REAL NULL," +
                "after_known INTEGER NOT NULL CHECK(after_known IN (0,1)), after_value REAL NULL, applied_delta REAL NULL," +
                "aggregated INTEGER NOT NULL CHECK(aggregated IN (0,1))," +
                "CHECK((before_known=0 AND before_value IS NULL) OR (before_known=1 AND before_value IS NOT NULL))," +
                "CHECK((after_known=0 AND after_value IS NULL) OR (after_known=1 AND after_value IS NOT NULL))," +
                "CHECK((before_known=1 AND after_known=1 AND applied_delta IS NOT NULL) OR " +
                "(before_known=0 OR after_known=0) AND applied_delta IS NULL))");
            connection.Execute("CREATE INDEX stat_changes_event_id ON stat_changes(event_id)");
            connection.Execute("CREATE TABLE answers (" +
                "history_sequence INTEGER PRIMARY KEY AUTOINCREMENT, answer_id TEXT NOT NULL UNIQUE," +
                "event_id TEXT NOT NULL REFERENCES game_events(event_id), step_execution_id TEXT NOT NULL REFERENCES step_executions(execution_id)," +
                "answer_kind TEXT NOT NULL CHECK(answer_kind IN ('option','free_text','skip','cancel'))," +
                "option_id TEXT NULL, submitted_at_utc TEXT NOT NULL," +
                "CHECK((answer_kind='option' AND option_id IS NOT NULL AND length(option_id)>0) OR " +
                "(answer_kind!='option' AND option_id IS NULL)))");
            connection.Execute("CREATE INDEX answers_step_execution_id ON answers(step_execution_id)");
            connection.Execute("PRAGMA user_version = 2");
        }

        internal static void MigrateVersion2To3(SQLiteConnection connection)
        {
            connection.Execute("CREATE TABLE topic_history_state (singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1), revision INTEGER NOT NULL CHECK(revision>=0))");
            connection.Execute("INSERT INTO topic_history_state(singleton_id,revision) VALUES (1,0)");
            connection.Execute("CREATE TABLE topic_occurrences (" +
                "instance_id TEXT PRIMARY KEY NOT NULL, topic_id TEXT NOT NULL, replay_policy TEXT NOT NULL CHECK(replay_policy IN ('repeatable','once'))," +
                "started_utc_ticks INTEGER NOT NULL CHECK(started_utc_ticks>0), presented_utc_ticks INTEGER NOT NULL CHECK(presented_utc_ticks>=0)," +
                "completed_utc_ticks INTEGER NOT NULL CHECK(completed_utc_ticks>=0), consumed_utc_ticks INTEGER NOT NULL CHECK(consumed_utc_ticks>=0)," +
                "cooldown_until_utc_ticks INTEGER NOT NULL CHECK(cooldown_until_utc_ticks>=0), record_json TEXT NOT NULL CHECK(length(record_json)<=1048576)," +
                "CHECK(consumed_utc_ticks=0 OR replay_policy='once'))");
            connection.Execute("CREATE INDEX topic_occurrences_topic_id ON topic_occurrences(topic_id)");
            connection.Execute("CREATE TABLE topic_scenario_completions (" +
                "instance_id TEXT NOT NULL REFERENCES topic_occurrences(instance_id) ON DELETE CASCADE, scenario_id TEXT NOT NULL," +
                "completion_count INTEGER NOT NULL CHECK(completion_count>0), last_completed_utc_ticks INTEGER NOT NULL CHECK(last_completed_utc_ticks>0)," +
                "payload_json TEXT NOT NULL CHECK(length(payload_json)<=262144), PRIMARY KEY(instance_id,scenario_id))");
            connection.Execute("CREATE TABLE topic_history_imports (" +
                "singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1), source_hash TEXT NOT NULL UNIQUE, operation_id TEXT NOT NULL UNIQUE," +
                "imported_at_utc TEXT NOT NULL, record_count INTEGER NOT NULL CHECK(record_count>=0))");
            connection.Execute("CREATE TABLE topic_history_operations (" +
                "operation_id TEXT PRIMARY KEY NOT NULL, payload_hash TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>0))");
            connection.Execute("PRAGMA user_version = " + Version3);
        }

        internal static void MigrateVersion3To4(SQLiteConnection connection)
        {
            if (connection.ExecuteScalar<int>("PRAGMA user_version") != Version3 || !HasVersion3Tables(connection))
                throw new System.InvalidOperationException("The game database is not a complete schema-v3 store.");
            connection.Execute("CREATE TABLE player_item_quantities (" +
                "player_id TEXT NOT NULL CHECK(length(player_id) BETWEEN 1 AND 128)," +
                "item_id TEXT NOT NULL CHECK(length(item_id) BETWEEN 1 AND 128)," +
                "quantity INTEGER NOT NULL CHECK(quantity>0)," +
                "PRIMARY KEY(player_id,item_id))");
            connection.Execute("PRAGMA user_version = " + Version4);
        }

        internal static void MigrateVersion4To5(SQLiteConnection connection)
        {
            if (connection.ExecuteScalar<int>("PRAGMA user_version") != Version4 || !HasVersion4Tables(connection))
                throw new System.InvalidOperationException("The game database is not a complete schema-v4 store.");
            // Legacy invocations deliberately remain unpinned. New host effects fail closed for them.
            connection.Execute("ALTER TABLE flow_invocations ADD COLUMN flow_store_id TEXT NOT NULL DEFAULT ''");
            connection.Execute("ALTER TABLE flow_invocations ADD COLUMN flow_revision TEXT NOT NULL DEFAULT ''");
            connection.Execute("ALTER TABLE flow_invocations ADD COLUMN flow_hash TEXT NOT NULL DEFAULT ''");
            connection.Execute("PRAGMA user_version = " + Version5);
        }

        internal static void SetMeta(SQLiteConnection connection, string key, string value)
        {
            connection.Execute("INSERT INTO game_meta(key,value) VALUES (?,?)", key, value ?? string.Empty);
        }

        private static bool HasColumns(SQLiteConnection connection, string table, params string[] columns)
        {
            var actual = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (ColumnRow row in connection.Query<ColumnRow>("PRAGMA table_info(" + table + ")")) actual.Add(row.name);
            foreach (string column in columns) if (!actual.Contains(column)) return false;
            return true;
        }

        private sealed class ColumnRow
        {
            public string name { get; set; }
        }
    }
}
