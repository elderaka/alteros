using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Topics;
using SQLite;

namespace AlterOS.Persistence
{
    public sealed class TopicHistoryImportResult
    {
        public string State { get; internal set; }
        public string SourceHash { get; internal set; }
        public string OperationId { get; internal set; }
        public int RecordCount { get; internal set; }
        public long Revision { get; internal set; }
        public string Error { get; internal set; }
    }

    // Explicit, one-time importer. Production callers pass the known legacy locator;
    // no database value or authored content can select a filesystem path.
    public static class TopicHistoryImporter
    {
        private const int MaximumSourceBytes = 4 * 1024 * 1024;

        public static TopicHistoryImportResult ImportOnce(string legacyPath, SqliteTopicHistoryStore store)
        {
            if (store == null) return new TopicHistoryImportResult { State = "recovery_required", Error = "The Topic SQL store is unavailable." };
            TopicHistoryImportResult beforeRead = store.CheckImportState();
            if (beforeRead.State != "missing") return beforeRead;
            if (string.IsNullOrWhiteSpace(legacyPath)) return store.ImportMissing();

            string fullPath;
            try { fullPath = Path.GetFullPath(legacyPath); }
            catch (Exception exception) { return Failure(exception); }

            if (!File.Exists(fullPath))
            {
                if (Directory.Exists(fullPath)) return new TopicHistoryImportResult { State = "recovery_required", Error = "The Topic history source path is a directory." };
                return store.ImportMissing();
            }

            byte[] sourceBytes;
            try { sourceBytes = ReadSnapshot(fullPath); }
            catch (Exception exception) { return Failure(exception); }

            string sourceHash = Sha256(sourceBytes);
            TopicHistory history;
            try { history = TopicHistoryRepository.DecodeLegacyBytes(sourceBytes); }
            catch (Exception exception)
            {
                return new TopicHistoryImportResult { State = "recovery_required", SourceHash = sourceHash, Error = SafeMessage(exception) };
            }
            return store.ImportLegacy(sourceHash, sourceBytes, history);
        }

        private static byte[] ReadSnapshot(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumSourceBytes) throw new InvalidDataException("Topic history source is too large.");
                using (var memory = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (memory.Length + read > MaximumSourceBytes) throw new InvalidDataException("Topic history source is too large.");
                        memory.Write(buffer, 0, read);
                    }
                    return memory.ToArray();
                }
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (byte value in hash.ComputeHash(bytes)) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static TopicHistoryImportResult Failure(Exception exception)
        { return new TopicHistoryImportResult { State = "recovery_required", Error = SafeMessage(exception) }; }

        private static string SafeMessage(Exception exception)
        { return exception == null ? "Topic history import failed." : exception.Message; }
    }

    /// <summary>Moves Topic rows from the earlier root-level game database into the shared game database.</summary>
    public static class TopicHistoryDatabaseImporter
    {
        private static readonly HashSet<string> ExpectedSchemaV3Tables = new HashSet<string>(new[]
        {
            "game_meta", "game_snapshots", "home_layout_snapshots", "game_events", "operation_receipts",
            "migration_receipts", "legacy_source_backups", "scenario_runs", "flow_invocations", "step_executions",
            "variable_values", "action_executions", "event_context", "action_checkpoints", "stat_changes", "answers",
            "topic_history_state", "topic_occurrences", "topic_scenario_completions", "topic_history_imports", "topic_history_operations"
        }, StringComparer.Ordinal);

        private static readonly string[] NonTopicTables =
        {
            "game_snapshots", "home_layout_snapshots", "game_events", "operation_receipts", "migration_receipts",
            "scenario_runs", "flow_invocations", "step_executions", "variable_values", "action_executions",
            "event_context", "action_checkpoints", "stat_changes", "answers", "player_item_quantities"
        };

        public static TopicHistoryImportResult ImportOnce(string legacyDatabasePath, SqliteTopicHistoryStore target)
        {
            if (target == null) return Failure("recovery_required", "The canonical game store is unavailable.");
            TopicHistoryImportResult targetState = target.CheckImportState();
            if (targetState.State != "missing" && targetState.State != "already_imported") return targetState;
            if (string.IsNullOrWhiteSpace(legacyDatabasePath)) return targetState.State == "already_imported" ? targetState : Missing();

            string sourcePath;
            try { sourcePath = Path.GetFullPath(legacyDatabasePath); }
            catch (Exception exception) { return Failure("recovery_required", SafeMessage(exception)); }
            if (string.Equals(sourcePath, target.DatabasePath, StringComparison.OrdinalIgnoreCase))
                return Failure("conflict", "The legacy and canonical game database paths are the same.");
            if (Directory.Exists(sourcePath)) return Failure("recovery_required", "The legacy game database path is a directory.");
            if (!File.Exists(sourcePath)) return targetState.State == "already_imported" ? targetState : Missing();

            TopicDatabaseSnapshot snapshot;
            try { snapshot = ReadSnapshot(sourcePath, true); }
            catch (Exception exception) { return Failure("recovery_required", SafeMessage(exception)); }
            if (snapshot == null) return targetState.State == "already_imported" ? targetState : Missing();
            if (targetState.State == "already_imported")
            {
                TopicDatabaseSnapshot canonical;
                try { canonical = ReadSnapshot(target.DatabasePath, false); }
                catch (Exception exception) { return Failure("recovery_required", SafeMessage(exception)); }
                return ReconcileExisting(target, targetState, snapshot, canonical);
            }

            lock (SqliteGameRepository.WriterGate)
            {
                targetState = target.CheckImportState();
                if (targetState.State == "already_imported")
                {
                    TopicDatabaseSnapshot canonical;
                    try { canonical = ReadSnapshot(target.DatabasePath, false); }
                    catch (Exception exception) { return Failure("recovery_required", SafeMessage(exception)); }
                    return ReconcileExisting(target, targetState, snapshot, canonical);
                }
                if (targetState.State != "missing") return targetState;

                SQLiteConnection connection = null;
                try
                {
                    connection = new SQLiteConnection(target.DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
                    connection.BusyTimeout = TimeSpan.FromSeconds(2);
                    connection.Execute("PRAGMA foreign_keys=ON");
                    connection.Execute("PRAGMA synchronous=FULL");
                    connection.BeginTransaction();
                    if (connection.ExecuteScalar<int>("SELECT revision FROM topic_history_state WHERE singleton_id=1") != 0 ||
                        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_occurrences") != 0 ||
                        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_operations") != 0 ||
                        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_imports") != 0)
                    {
                        connection.Rollback();
                        return Failure("conflict", "Canonical Topic history changed during the database relocation.");
                    }

                    foreach (OccurrenceRow row in snapshot.occurrences)
                        connection.Execute("INSERT INTO topic_occurrences(instance_id,topic_id,replay_policy,started_utc_ticks,presented_utc_ticks,completed_utc_ticks,consumed_utc_ticks,cooldown_until_utc_ticks,record_json) VALUES (?,?,?,?,?,?,?,?,?)",
                            row.instance_id, row.topic_id, row.replay_policy, row.started_utc_ticks, row.presented_utc_ticks,
                            row.completed_utc_ticks, row.consumed_utc_ticks, row.cooldown_until_utc_ticks, row.record_json);
                    foreach (CompletionRow row in snapshot.completions)
                        connection.Execute("INSERT INTO topic_scenario_completions(instance_id,scenario_id,completion_count,last_completed_utc_ticks,payload_json) VALUES (?,?,?,?,?)",
                            row.instance_id, row.scenario_id, row.completion_count, row.last_completed_utc_ticks, row.payload_json);
                    foreach (OperationRow row in snapshot.operations)
                        connection.Execute("INSERT INTO topic_history_operations(operation_id,payload_hash,revision) VALUES (?,?,?)",
                            row.operation_id, row.payload_hash, row.revision);
                    connection.Execute("INSERT INTO topic_history_imports(singleton_id,source_hash,operation_id,imported_at_utc,record_count) VALUES (1,?,?,?,?)",
                        snapshot.sourceHash, snapshot.operationId, snapshot.importedAtUtc, snapshot.recordCount);
                    if (snapshot.sourceBytes != null)
                    {
                        byte[] existing = connection.ExecuteScalar<byte[]>("SELECT source_bytes FROM legacy_source_backups WHERE source_hash=?", snapshot.sourceHash);
                        if (existing != null && !ByteArraysEqual(existing, snapshot.sourceBytes))
                            throw new InvalidDataException("Canonical game backup bytes conflict with the legacy Topic source.");
                        if (existing == null)
                            connection.Execute("INSERT INTO legacy_source_backups(source_hash,source_bytes) VALUES (?,?)", snapshot.sourceHash, snapshot.sourceBytes);
                    }
                    connection.Execute("UPDATE topic_history_state SET revision=? WHERE singleton_id=1", snapshot.revision);
                    connection.Commit();
                    return new TopicHistoryImportResult
                    {
                        State = "imported", SourceHash = snapshot.sourceHash, OperationId = snapshot.operationId,
                        RecordCount = snapshot.recordCount, Revision = snapshot.revision
                    };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { }
                    return Failure("recovery_required", SafeMessage(exception));
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        private static TopicHistoryImportResult ReconcileExisting(SqliteTopicHistoryStore target,
            TopicHistoryImportResult targetState, TopicDatabaseSnapshot source, TopicDatabaseSnapshot canonical)
        {
            if (canonical == null || canonical.sourceHash != source.sourceHash || canonical.operationId != source.operationId ||
                canonical.recordCount != source.recordCount || !ByteArraysEqual(canonical.sourceBytes, source.sourceBytes))
                return Failure("conflict", "Canonical and legacy game databases do not share the same Topic source receipt.");
            if (canonical.revision == source.revision)
                return SameTopicRows(canonical, source) ? targetState : Failure("conflict", "Canonical and legacy Topic snapshots diverged at the same revision.");
            if (canonical.revision > source.revision)
                return HasOperationPrefix(source.operations, canonical.operations) ? targetState :
                    Failure("conflict", "Canonical Topic history does not contain the legacy operation receipts.");
            if (canonical.revision == 1 && canonical.operations.Count == 0 && MatchesLegacySeed(canonical))
                return PromoteInitialSnapshot(target, source);
            return Failure("conflict", "The legacy Topic database is newer than a canonical database with later Topic changes.");
        }

        private static TopicHistoryImportResult PromoteInitialSnapshot(SqliteTopicHistoryStore target, TopicDatabaseSnapshot source)
        {
            lock (SqliteGameRepository.WriterGate)
            {
                TopicHistoryImportResult current = target.CheckImportState();
                if (current.State != "already_imported" || current.SourceHash != source.sourceHash)
                    return Failure("conflict", "Canonical Topic history changed while reconciling the earlier database.");
                SQLiteConnection connection = null;
                try
                {
                    connection = new SQLiteConnection(target.DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
                    connection.BusyTimeout = TimeSpan.FromSeconds(2);
                    connection.Execute("PRAGMA foreign_keys=ON");
                    connection.Execute("PRAGMA synchronous=FULL");
                    connection.BeginTransaction();
                    if (connection.ExecuteScalar<long>("SELECT revision FROM topic_history_state WHERE singleton_id=1") != 1 ||
                        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_operations") != 0)
                    {
                        connection.Rollback();
                        return Failure("conflict", "Canonical Topic history advanced during database reconciliation.");
                    }
                    TopicImportRow receipt = connection.Query<TopicImportRow>(
                        "SELECT source_hash,operation_id,imported_at_utc,record_count FROM topic_history_imports WHERE singleton_id=1").FirstOrDefault();
                    if (receipt == null || receipt.source_hash != source.sourceHash || receipt.operation_id != source.operationId ||
                        receipt.record_count != source.recordCount)
                    {
                        connection.Rollback();
                        return Failure("conflict", "Canonical Topic import receipt changed during database reconciliation.");
                    }
                    connection.Execute("DELETE FROM topic_scenario_completions");
                    connection.Execute("DELETE FROM topic_occurrences");
                    InsertSnapshotRows(connection, source);
                    connection.Execute("UPDATE topic_history_state SET revision=? WHERE singleton_id=1", source.revision);
                    connection.Commit();
                    return new TopicHistoryImportResult
                    {
                        State = "imported", SourceHash = source.sourceHash, OperationId = source.operationId,
                        RecordCount = source.recordCount, Revision = source.revision
                    };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { }
                    return Failure("recovery_required", SafeMessage(exception));
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        private static void InsertSnapshotRows(SQLiteConnection connection, TopicDatabaseSnapshot snapshot)
        {
            foreach (OccurrenceRow row in snapshot.occurrences)
                connection.Execute("INSERT INTO topic_occurrences(instance_id,topic_id,replay_policy,started_utc_ticks,presented_utc_ticks,completed_utc_ticks,consumed_utc_ticks,cooldown_until_utc_ticks,record_json) VALUES (?,?,?,?,?,?,?,?,?)",
                    row.instance_id, row.topic_id, row.replay_policy, row.started_utc_ticks, row.presented_utc_ticks,
                    row.completed_utc_ticks, row.consumed_utc_ticks, row.cooldown_until_utc_ticks, row.record_json);
            foreach (CompletionRow row in snapshot.completions)
                connection.Execute("INSERT INTO topic_scenario_completions(instance_id,scenario_id,completion_count,last_completed_utc_ticks,payload_json) VALUES (?,?,?,?,?)",
                    row.instance_id, row.scenario_id, row.completion_count, row.last_completed_utc_ticks, row.payload_json);
            foreach (OperationRow row in snapshot.operations)
                connection.Execute("INSERT INTO topic_history_operations(operation_id,payload_hash,revision) VALUES (?,?,?)",
                    row.operation_id, row.payload_hash, row.revision);
        }

        private static bool HasOperationPrefix(IList<OperationRow> source, IList<OperationRow> canonical)
        {
            foreach (OperationRow operation in source)
                if (!canonical.Any(item => item.operation_id == operation.operation_id && item.payload_hash == operation.payload_hash && item.revision == operation.revision))
                    return false;
            return true;
        }

        private static bool SameTopicRows(TopicDatabaseSnapshot left, TopicDatabaseSnapshot right)
        {
            return SameOccurrences(left.occurrences, right.occurrences) && SameCompletions(left.completions, right.completions) &&
                left.operations.Count == right.operations.Count && HasOperationPrefix(left.operations, right.operations);
        }

        private static bool MatchesLegacySeed(TopicDatabaseSnapshot snapshot)
        {
            if (snapshot == null || snapshot.sourceBytes == null) return false;
            try
            {
                TopicHistory history = TopicHistoryRepository.DecodeLegacyBytes(snapshot.sourceBytes);
                List<TopicHistoryRecord> records = history.Records.OrderBy(item => item.instanceId, StringComparer.Ordinal).ToList();
                if (records.Count != snapshot.occurrences.Count) return false;
                int completionIndex = 0;
                var expectedCompletions = new List<CompletionRow>();
                for (int index = 0; index < records.Count; index++)
                {
                    TopicHistoryRecord record = records[index];
                    OccurrenceRow actual = snapshot.occurrences[index];
                    if (actual.instance_id != record.instanceId || actual.record_json != TopicHistoryRepository.SerializeRecordForSql(record)) return false;
                    foreach (TopicScenarioCompletionRecord completion in (record.scenarioCompletions ?? new List<TopicScenarioCompletionRecord>()).OrderBy(item => item.scenarioId, StringComparer.Ordinal))
                        expectedCompletions.Add(new CompletionRow
                        {
                            instance_id = record.instanceId, scenario_id = completion.scenarioId,
                            completion_count = completion.completionCount, last_completed_utc_ticks = completion.lastCompletedUtcTicks,
                            payload_json = TopicHistoryRepository.SerializeCompletionForSql(completion)
                        });
                }
                if (expectedCompletions.Count != snapshot.completions.Count) return false;
                for (; completionIndex < expectedCompletions.Count; completionIndex++)
                {
                    CompletionRow expected = expectedCompletions[completionIndex], actual = snapshot.completions[completionIndex];
                    if (expected.instance_id != actual.instance_id || expected.scenario_id != actual.scenario_id ||
                        expected.completion_count != actual.completion_count || expected.last_completed_utc_ticks != actual.last_completed_utc_ticks ||
                        expected.payload_json != actual.payload_json) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static bool SameOccurrences(IList<OccurrenceRow> left, IList<OccurrenceRow> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                OccurrenceRow a = left[index], b = right[index];
                if (a.instance_id != b.instance_id || a.topic_id != b.topic_id || a.replay_policy != b.replay_policy ||
                    a.started_utc_ticks != b.started_utc_ticks || a.presented_utc_ticks != b.presented_utc_ticks ||
                    a.completed_utc_ticks != b.completed_utc_ticks || a.consumed_utc_ticks != b.consumed_utc_ticks ||
                    a.cooldown_until_utc_ticks != b.cooldown_until_utc_ticks || a.record_json != b.record_json) return false;
            }
            return true;
        }

        private static bool SameCompletions(IList<CompletionRow> left, IList<CompletionRow> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                CompletionRow a = left[index], b = right[index];
                if (a.instance_id != b.instance_id || a.scenario_id != b.scenario_id || a.completion_count != b.completion_count ||
                    a.last_completed_utc_ticks != b.last_completed_utc_ticks || a.payload_json != b.payload_json) return false;
            }
            return true;
        }

        private static TopicDatabaseSnapshot ReadSnapshot(string sourcePath, bool requireTopicOnly)
        {
            using (var connection = new SQLiteConnection(sourcePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex))
            {
                connection.BusyTimeout = TimeSpan.FromSeconds(2);
                connection.BeginTransaction();
                try
                {
                    int expectedVersion = requireTopicOnly ? 3 : GameSchemaMigrations.CurrentVersion;
                    bool hasExpectedSchema = requireTopicOnly
                        ? GameSchemaMigrations.HasVersion3Tables(connection)
                        : GameSchemaMigrations.HasRequiredTables(connection);
                    if (connection.ExecuteScalar<int>("PRAGMA user_version") != expectedVersion || !hasExpectedSchema)
                        throw new InvalidDataException("The legacy game database schema cannot be relocated automatically.");
                    if (requireTopicOnly)
                    {
                        ValidateTopicOnlyTableInventory(connection);
                        foreach (string table in NonTopicTables)
                            if (HasTable(connection, table) && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM " + table) != 0)
                                throw new InvalidDataException("The legacy game database contains non-Topic game data and requires recovery.");
                    }

                    TopicStateRow state = connection.Query<TopicStateRow>(
                        "SELECT singleton_id,revision FROM topic_history_state WHERE singleton_id=1").FirstOrDefault();
                    if (state == null) throw new InvalidDataException("The legacy Topic history state row is missing.");
                    TopicImportRow receipt = connection.Query<TopicImportRow>(
                        "SELECT source_hash,operation_id,imported_at_utc,record_count FROM topic_history_imports WHERE singleton_id=1").FirstOrDefault();
                    List<OccurrenceRow> occurrences = connection.Query<OccurrenceRow>(
                        "SELECT instance_id,topic_id,replay_policy,started_utc_ticks,presented_utc_ticks,completed_utc_ticks,consumed_utc_ticks,cooldown_until_utc_ticks,record_json FROM topic_occurrences ORDER BY instance_id");
                    List<CompletionRow> completions = connection.Query<CompletionRow>(
                        "SELECT instance_id,scenario_id,completion_count,last_completed_utc_ticks,payload_json FROM topic_scenario_completions ORDER BY instance_id,scenario_id");
                    List<OperationRow> operations = connection.Query<OperationRow>(
                        "SELECT operation_id,payload_hash,revision FROM topic_history_operations ORDER BY revision,operation_id");
                    List<BackupRow> backups = receipt == null ? new List<BackupRow>() : connection.Query<BackupRow>(
                        "SELECT source_hash,source_bytes FROM legacy_source_backups WHERE source_hash=?", receipt.source_hash);

                    if (receipt == null)
                    {
                        int backupCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM legacy_source_backups");
                        if (state.revision == 0 && occurrences.Count == 0 && completions.Count == 0 && operations.Count == 0 &&
                            (backupCount == 0 || !requireTopicOnly))
                        { connection.Commit(); return null; }
                        throw new InvalidDataException("Legacy Topic rows exist without an import receipt.");
                    }
                    if (string.IsNullOrWhiteSpace(receipt.source_hash) || string.IsNullOrWhiteSpace(receipt.operation_id) ||
                        receipt.record_count != occurrences.Count || state.revision < 0 ||
                        operations.Any(item => item.revision <= 0 || item.revision > state.revision || !IsSha256(item.payload_hash)))
                        throw new InvalidDataException("The legacy Topic receipt or operation rows are invalid.");

                    int sourceBackupCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM legacy_source_backups");
                    if (requireTopicOnly && sourceBackupCount != (receipt.source_hash == "missing:topic-history" ? 0 : 1))
                        throw new InvalidDataException("The legacy Topic database contains unrelated source backups and requires recovery.");

                    foreach (OccurrenceRow row in occurrences)
                    {
                        TopicHistoryRecord record = TopicHistoryRepository.DecodeRecordForSql(row.record_json);
                        if (record == null || record.instanceId != row.instance_id || record.topicId != row.topic_id ||
                            record.replayPolicy != row.replay_policy || record.startedUtcTicks != row.started_utc_ticks ||
                            record.presentedUtcTicks != row.presented_utc_ticks || record.completedUtcTicks != row.completed_utc_ticks ||
                            record.consumedUtcTicks != row.consumed_utc_ticks || record.cooldownUntilUtcTicks != row.cooldown_until_utc_ticks)
                            throw new InvalidDataException("A legacy Topic occurrence projection is invalid.");
                        List<CompletionRow> projected = completions.Where(item => item.instance_id == row.instance_id).ToList();
                        if (projected.Count != (record.scenarioCompletions ?? new List<TopicScenarioCompletionRecord>()).Count)
                            throw new InvalidDataException("A legacy Topic completion projection is incomplete.");
                        foreach (CompletionRow completion in projected)
                        {
                            TopicScenarioCompletionRecord payload = TopicHistoryRepository.DecodeCompletionForSql(completion.payload_json);
                            if (payload == null || payload.scenarioId != completion.scenario_id || payload.completionCount != completion.completion_count ||
                                payload.lastCompletedUtcTicks != completion.last_completed_utc_ticks ||
                                !(record.scenarioCompletions ?? new List<TopicScenarioCompletionRecord>()).Any(item => item.scenarioId == completion.scenario_id &&
                                    item.completionCount == completion.completion_count && item.lastCompletedUtcTicks == completion.last_completed_utc_ticks))
                                throw new InvalidDataException("A legacy Topic completion projection is invalid.");
                        }
                    }

                    byte[] sourceBytes = null;
                    if (receipt.source_hash != "missing:topic-history")
                    {
                        if (!IsSha256(receipt.source_hash) || backups.Count != 1 || backups[0].source_hash != receipt.source_hash)
                            throw new InvalidDataException("The legacy Topic source backup is missing or mismatched.");
                        sourceBytes = backups[0].source_bytes;
                        if (sourceBytes == null || Sha256(sourceBytes) != receipt.source_hash)
                            throw new InvalidDataException("The legacy Topic source backup hash is invalid.");
                    }
                    else if (backups.Count != 0) throw new InvalidDataException("An empty Topic receipt has an unexpected source backup.");

                    connection.Commit();
                    return new TopicDatabaseSnapshot
                    {
                        sourceHash = receipt.source_hash, operationId = receipt.operation_id,
                        importedAtUtc = receipt.imported_at_utc, recordCount = receipt.record_count, revision = state.revision,
                        occurrences = occurrences, completions = completions, operations = operations, sourceBytes = sourceBytes
                    };
                }
                catch { if (connection.IsInTransaction) try { connection.Rollback(); } catch { } throw; }
            }
        }

        private static TopicHistoryImportResult Missing()
        { return new TopicHistoryImportResult { State = "missing", SourceHash = string.Empty, OperationId = string.Empty }; }
        private static TopicHistoryImportResult Failure(string state, string error)
        { return new TopicHistoryImportResult { State = state, Error = error ?? string.Empty }; }
        private static bool IsSha256(string value)
        { return value != null && value.Length == 64 && value.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f'); }

        private static void ValidateTopicOnlyTableInventory(SQLiteConnection connection)
        {
            List<TableNameRow> rows = connection.Query<TableNameRow>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT GLOB 'sqlite_*' ORDER BY name");
            var actual = new HashSet<string>(rows.Select(item => item.name), StringComparer.Ordinal);
            if (actual.Count != ExpectedSchemaV3Tables.Count || actual.Any(name => !ExpectedSchemaV3Tables.Contains(name)))
                throw new InvalidDataException("The legacy game database has an unexpected user-table inventory and requires recovery.");
        }

        private static bool HasTable(SQLiteConnection connection, string table)
        { return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) == 1; }

        private static string Sha256(byte[] bytes)
        { using (SHA256 hash = SHA256.Create()) return string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))); }
        private static bool ByteArraysEqual(byte[] left, byte[] right)
        { return left != null && right != null && left.Length == right.Length && !left.Where((value, index) => value != right[index]).Any(); }
        private static string SafeMessage(Exception exception)
        { return exception == null ? "Topic database relocation failed." : exception.Message; }

        private sealed class TopicStateRow { public int singleton_id { get; set; } public long revision { get; set; } }
        private sealed class TableNameRow { public string name { get; set; } }
        private sealed class TopicDatabaseSnapshot
        {
            public string sourceHash, operationId, importedAtUtc;
            public int recordCount;
            public long revision;
            public List<OccurrenceRow> occurrences;
            public List<CompletionRow> completions;
            public List<OperationRow> operations;
            public byte[] sourceBytes;
        }
        private sealed class TopicImportRow { public string source_hash { get; set; } public string operation_id { get; set; } public string imported_at_utc { get; set; } public int record_count { get; set; } }
        private sealed class OccurrenceRow
        {
            public string instance_id { get; set; } public string topic_id { get; set; } public string replay_policy { get; set; }
            public long started_utc_ticks { get; set; } public long presented_utc_ticks { get; set; } public long completed_utc_ticks { get; set; }
            public long consumed_utc_ticks { get; set; } public long cooldown_until_utc_ticks { get; set; } public string record_json { get; set; }
        }
        private sealed class CompletionRow
        { public string instance_id { get; set; } public string scenario_id { get; set; } public int completion_count { get; set; } public long last_completed_utc_ticks { get; set; } public string payload_json { get; set; } }
        private sealed class OperationRow { public string operation_id { get; set; } public string payload_hash { get; set; } public long revision { get; set; } }
        private sealed class BackupRow { public string source_hash { get; set; } public byte[] source_bytes { get; set; } }
    }
}
