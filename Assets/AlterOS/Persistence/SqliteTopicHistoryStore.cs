using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Topics;
using SQLite;

namespace AlterOS.Persistence
{
    public sealed class TopicHistoryStoreLoadResult
    {
        public bool Success { get; internal set; }
        public long Revision { get; internal set; }
        public TopicHistory History { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class TopicHistoryStoreSaveResult
    {
        public bool Success { get; internal set; }
        public string State { get; internal set; }
        public long Revision { get; internal set; }
        public string Error { get; internal set; }
    }

    // Topic history shares the game-owned database and writer boundary. It never writes
    // game_events or changes the game revision: this store only owns Topic consumption.
    public sealed class SqliteTopicHistoryStore
    {
        private const int MaximumRecords = 10000;
        private const int MaximumPayloadBytes = 4 * 1024 * 1024;
        private readonly string databasePath;
        private readonly SqliteGameRepository gameRepository;

        public string DatabasePath { get { return databasePath; } }

        public SqliteTopicHistoryStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A game database path is required.", "databasePath");
            this.databasePath = Path.GetFullPath(databasePath);
            gameRepository = new SqliteGameRepository(this.databasePath);
        }

        public TopicHistoryStoreLoadResult Load()
        {
            lock (SqliteGameRepository.WriterGate)
            {
                string error;
                if (!EnsureGameReady(out error)) return new TopicHistoryStoreLoadResult { Error = error };
                try
                {
                    using (SQLiteConnection connection = OpenConnection())
                    {
                        long revision = ReadRevision(connection);
                        var history = new TopicHistory();
                        List<TopicOccurrenceRow> rows = connection.Query<TopicOccurrenceRow>(
                            "SELECT instance_id,record_json FROM topic_occurrences ORDER BY instance_id");
                        if (rows.Count > MaximumRecords) throw new InvalidDataException("Topic history contains too many records.");
                        foreach (TopicOccurrenceRow row in rows)
                        {
                            TopicHistoryRecord record = TopicHistoryRepository.DecodeRecordForSql(row.record_json);
                            if (record == null || !string.Equals(record.instanceId, row.instance_id, StringComparison.Ordinal))
                                throw new InvalidDataException("Topic history row identity does not match its payload.");
                            ValidateCompletionProjection(connection, record);
                            history.Restore(record);
                        }
                        history.SavedSql(databasePath, revision);
                        return new TopicHistoryStoreLoadResult { Success = true, Revision = revision, History = history };
                    }
                }
                catch (Exception exception)
                {
                    return new TopicHistoryStoreLoadResult { Error = SafeMessage(exception) };
                }
            }
        }

        public TopicHistoryStoreSaveResult Save(TopicHistory history, long expectedRevision, string operationId)
        {
            if (history == null || expectedRevision < 0 || string.IsNullOrWhiteSpace(operationId) || operationId.Length > 256)
                return new TopicHistoryStoreSaveResult { State = "invalid", Error = "A history, nonnegative expected revision, and bounded operation ID are required." };

            List<TopicHistoryRecord> records;
            byte[] payloadBytes;
            string payloadHash;
            try
            {
                records = history.Records.OrderBy(record => record.instanceId, StringComparer.Ordinal).ToList();
                if (records.Count > MaximumRecords) throw new InvalidDataException("Topic history contains too many records.");
                var canonical = new StringBuilder();
                foreach (TopicHistoryRecord record in records)
                {
                    ValidateRecordForWrite(record);
                    string json = TopicHistoryRepository.SerializeRecordForSql(record);
                    canonical.Append(json.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(json).Append('\n');
                }
                payloadBytes = Encoding.UTF8.GetBytes(canonical.ToString());
                if (payloadBytes.Length > MaximumPayloadBytes) throw new InvalidDataException("Topic history exceeds its storage limit.");
                payloadHash = Sha256(payloadBytes);
            }
            catch (Exception exception)
            {
                return new TopicHistoryStoreSaveResult { State = "invalid", Error = SafeMessage(exception) };
            }

            lock (SqliteGameRepository.WriterGate)
            {
                string error;
                if (!EnsureGameReady(out error)) return new TopicHistoryStoreSaveResult { State = "unavailable", Error = error };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection();
                    connection.BeginTransaction();
                    TopicHistoryOperationRow prior = connection.Query<TopicHistoryOperationRow>(
                        "SELECT operation_id,payload_hash,revision FROM topic_history_operations WHERE operation_id=?", operationId).FirstOrDefault();
                    if (prior != null)
                    {
                        long replayedAtRevision = ReadRevision(connection);
                        connection.Rollback();
                        if (!string.Equals(prior.payload_hash, payloadHash, StringComparison.Ordinal))
                            return new TopicHistoryStoreSaveResult { State = "conflict", Error = "The operation ID already names different Topic history." };
                        if (replayedAtRevision != prior.revision)
                            return new TopicHistoryStoreSaveResult { State = "conflict", Revision = replayedAtRevision,
                                Error = "This Topic save was already committed, but newer history exists; reload before continuing." };
                        history.SavedSql(databasePath, prior.revision);
                        return new TopicHistoryStoreSaveResult { Success = true, State = "duplicate", Revision = prior.revision };
                    }

                    long currentRevision = ReadRevision(connection);
                    if (currentRevision != expectedRevision)
                    {
                        connection.Rollback();
                        return new TopicHistoryStoreSaveResult { State = "conflict", Revision = currentRevision, Error = "Topic history changed; reload before saving." };
                    }

                    long nextRevision = checked(currentRevision + 1);
                    connection.Execute("DELETE FROM topic_occurrences");
                    foreach (TopicHistoryRecord record in records)
                        InsertRecord(connection, record);
                    connection.Execute("UPDATE topic_history_state SET revision=? WHERE singleton_id=1 AND revision=?", nextRevision, currentRevision);
                    connection.Execute("INSERT INTO topic_history_operations(operation_id,payload_hash,revision) VALUES (?,?,?)",
                        operationId, payloadHash, nextRevision);
                    connection.Commit();
                    history.SavedSql(databasePath, nextRevision);
                    return new TopicHistoryStoreSaveResult { Success = true, State = "saved", Revision = nextRevision };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction)
                        try { connection.Rollback(); } catch { }
                    return new TopicHistoryStoreSaveResult { State = "unavailable", Error = SafeMessage(exception) };
                }
                finally
                {
                    if (connection != null) connection.Close();
                }
            }
        }

        internal TopicHistoryImportResult ImportLegacy(string sourceHash, byte[] sourceBytes, TopicHistory history)
        {
            if (history == null || sourceBytes == null || !IsSha256(sourceHash) || !string.Equals(Sha256(sourceBytes), sourceHash, StringComparison.Ordinal))
                return new TopicHistoryImportResult { State = "recovery_required", SourceHash = sourceHash ?? string.Empty, Error = "The Topic source hash or decoded history is invalid." };

            List<TopicHistoryRecord> records;
            try
            {
                records = history.Records.OrderBy(record => record.instanceId, StringComparer.Ordinal).ToList();
                if (records.Count > MaximumRecords) throw new InvalidDataException("Topic history contains too many records.");
                foreach (TopicHistoryRecord record in records) ValidateRecordForWrite(record);
            }
            catch (Exception exception)
            {
                return new TopicHistoryImportResult { State = "recovery_required", SourceHash = sourceHash, Error = SafeMessage(exception) };
            }

            lock (SqliteGameRepository.WriterGate)
            {
                string error;
                if (!EnsureGameReady(out error)) return new TopicHistoryImportResult { State = "recovery_required", SourceHash = sourceHash, Error = error };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection();
                    connection.BeginTransaction();
                    TopicHistoryImportRow prior = connection.Query<TopicHistoryImportRow>(
                        "SELECT source_hash,operation_id,imported_at_utc,record_count FROM topic_history_imports WHERE singleton_id=1").FirstOrDefault();
                    long currentRevision = ReadRevision(connection);
                    if (prior != null)
                    {
                        connection.Rollback();
                        return new TopicHistoryImportResult
                        {
                            State = string.Equals(prior.source_hash, sourceHash, StringComparison.Ordinal) ? "already_imported" : "conflict",
                            SourceHash = prior.source_hash,
                            OperationId = prior.operation_id,
                            RecordCount = prior.record_count,
                            Revision = currentRevision,
                            Error = string.Equals(prior.source_hash, sourceHash, StringComparison.Ordinal) ? string.Empty : "Another Topic source already owns this SQL history."
                        };
                    }

                    int existingRecords = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_occurrences");
                    int existingOperations = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_operations");
                    if (currentRevision != 0 || existingRecords != 0 || existingOperations != 0)
                    {
                        connection.Rollback();
                        return new TopicHistoryImportResult { State = "conflict", SourceHash = sourceHash, Revision = currentRevision,
                            Error = "Topic SQL history already exists without a matching import receipt." };
                    }

                    foreach (TopicHistoryRecord record in records) InsertRecord(connection, record);
                    long nextRevision = 1;
                    connection.Execute("UPDATE topic_history_state SET revision=? WHERE singleton_id=1 AND revision=0", nextRevision);
                    int hasBackup = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM legacy_source_backups WHERE source_hash=?", sourceHash);
                    if (hasBackup == 0)
                        connection.Execute("INSERT INTO legacy_source_backups(source_hash,source_bytes) VALUES (?,?)", sourceHash, sourceBytes);
                    else
                    {
                        byte[] priorBytes = connection.ExecuteScalar<byte[]>("SELECT source_bytes FROM legacy_source_backups WHERE source_hash=?", sourceHash);
                        if (!ByteArraysEqual(priorBytes, sourceBytes)) throw new InvalidDataException("The same Topic source hash is already attached to different source bytes.");
                    }
                    string operationId = "topic-history-import:" + sourceHash;
                    connection.Execute("INSERT INTO topic_history_imports(singleton_id,source_hash,operation_id,imported_at_utc,record_count) VALUES (1,?,?,?,?)",
                        sourceHash, operationId, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), records.Count);
                    connection.Commit();
                    history.SavedSql(databasePath, nextRevision);
                    return new TopicHistoryImportResult { State = "imported", SourceHash = sourceHash, OperationId = operationId,
                        RecordCount = records.Count, Revision = nextRevision };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction)
                        try { connection.Rollback(); } catch { }
                    return new TopicHistoryImportResult { State = "recovery_required", SourceHash = sourceHash, Error = SafeMessage(exception) };
                }
                finally
                {
                    if (connection != null) connection.Close();
                }
            }
        }

        internal TopicHistoryImportResult CheckImportState()
        {
            lock (SqliteGameRepository.WriterGate)
            {
                string error;
                if (!EnsureGameReady(out error)) return new TopicHistoryImportResult { State = "recovery_required", Error = error };
                try
                {
                    using (SQLiteConnection connection = OpenConnection())
                    {
                        TopicHistoryImportRow receipt = connection.Query<TopicHistoryImportRow>(
                            "SELECT source_hash,operation_id,imported_at_utc,record_count FROM topic_history_imports WHERE singleton_id=1").FirstOrDefault();
                        long revision = ReadRevision(connection);
                        int rows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_occurrences");
                        int operations = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_operations");
                        if (receipt != null) return new TopicHistoryImportResult { State = "already_imported", SourceHash = receipt.source_hash,
                            OperationId = receipt.operation_id, RecordCount = receipt.record_count, Revision = revision };
                        if (revision != 0 || rows != 0 || operations != 0)
                            return new TopicHistoryImportResult { State = "conflict", Revision = revision,
                                Error = "Topic SQL history exists without a migration receipt." };
                        return new TopicHistoryImportResult { State = "missing", Revision = 0 };
                    }
                }
                catch (Exception exception) { return new TopicHistoryImportResult { State = "recovery_required", Error = SafeMessage(exception) }; }
            }
        }

        internal TopicHistoryImportResult ImportMissing()
        {
            lock (SqliteGameRepository.WriterGate)
            {
                string error;
                if (!EnsureGameReady(out error)) return new TopicHistoryImportResult { State = "recovery_required", Error = error };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection();
                    connection.BeginTransaction();
                    TopicHistoryImportRow receipt = connection.Query<TopicHistoryImportRow>(
                        "SELECT source_hash,operation_id,imported_at_utc,record_count FROM topic_history_imports WHERE singleton_id=1").FirstOrDefault();
                    long revision = ReadRevision(connection);
                    int rows = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_occurrences");
                    int operations = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_operations");
                    if (receipt != null)
                    {
                        connection.Rollback();
                        return new TopicHistoryImportResult { State = "already_imported", SourceHash = receipt.source_hash,
                            OperationId = receipt.operation_id, RecordCount = receipt.record_count, Revision = revision };
                    }
                    if (revision != 0 || rows != 0 || operations != 0)
                    {
                        connection.Rollback();
                        return new TopicHistoryImportResult { State = "conflict", Revision = revision,
                            Error = "Topic SQL history exists without a migration receipt." };
                    }
                    const string missingHash = "missing:topic-history";
                    const string operationId = "topic-history-import:missing";
                    connection.Execute("INSERT INTO topic_history_imports(singleton_id,source_hash,operation_id,imported_at_utc,record_count) VALUES (1,?,?,?,0)",
                        missingHash, operationId, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    connection.Commit();
                    return new TopicHistoryImportResult { State = "missing", SourceHash = missingHash, OperationId = operationId, Revision = 0, RecordCount = 0 };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { }
                    return new TopicHistoryImportResult { State = "recovery_required", Error = SafeMessage(exception) };
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        private bool EnsureGameReady(out string error)
        {
            GameLoadResult loaded = gameRepository.Load();
            if (loaded.state == GamePersistenceStates.Ready) { error = string.Empty; return true; }
            error = string.IsNullOrWhiteSpace(loaded.message) ? "The game SQL store is unavailable." : loaded.message;
            return false;
        }

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            connection.BusyTimeout = TimeSpan.FromSeconds(2);
            connection.Execute("PRAGMA foreign_keys=ON");
            return connection;
        }

        private static long ReadRevision(SQLiteConnection connection)
        {
            TopicHistoryStateRow state = connection.Query<TopicHistoryStateRow>(
                "SELECT singleton_id,revision FROM topic_history_state WHERE singleton_id=1").FirstOrDefault();
            if (state == null || state.revision < 0) throw new InvalidDataException("Topic history state is missing or invalid.");
            return state.revision;
        }

        private static void InsertRecord(SQLiteConnection connection, TopicHistoryRecord record)
        {
            string recordJson = TopicHistoryRepository.SerializeRecordForSql(record);
            connection.Execute("INSERT INTO topic_occurrences(instance_id,topic_id,replay_policy,started_utc_ticks,presented_utc_ticks,completed_utc_ticks,consumed_utc_ticks,cooldown_until_utc_ticks,record_json) VALUES (?,?,?,?,?,?,?,?,?)",
                record.instanceId, record.topicId, record.replayPolicy, record.startedUtcTicks, record.presentedUtcTicks,
                record.completedUtcTicks, record.consumedUtcTicks, record.cooldownUntilUtcTicks, recordJson);
            foreach (TopicScenarioCompletionRecord completion in record.scenarioCompletions ?? new List<TopicScenarioCompletionRecord>())
                connection.Execute("INSERT INTO topic_scenario_completions(instance_id,scenario_id,completion_count,last_completed_utc_ticks,payload_json) VALUES (?,?,?,?,?)",
                    record.instanceId, completion.scenarioId, completion.completionCount, completion.lastCompletedUtcTicks,
                    TopicHistoryRepository.SerializeCompletionForSql(completion));
        }

        private static void ValidateCompletionProjection(SQLiteConnection connection, TopicHistoryRecord record)
        {
            List<TopicCompletionRow> rows = connection.Query<TopicCompletionRow>(
                "SELECT instance_id,scenario_id,completion_count,last_completed_utc_ticks,payload_json FROM topic_scenario_completions WHERE instance_id=? ORDER BY scenario_id",
                record.instanceId);
            List<TopicScenarioCompletionRecord> payloads = record.scenarioCompletions ?? new List<TopicScenarioCompletionRecord>();
            if (rows.Count != payloads.Count) throw new InvalidDataException("Topic completion projections do not match their record payload.");
            foreach (TopicCompletionRow row in rows)
            {
                TopicScenarioCompletionRecord payload = TopicHistoryRepository.DecodeCompletionForSql(row.payload_json);
                if (payload == null || !string.Equals(payload.scenarioId, row.scenario_id, StringComparison.Ordinal) ||
                    payload.completionCount != row.completion_count || payload.lastCompletedUtcTicks != row.last_completed_utc_ticks ||
                    !payloads.Any(item => item.scenarioId == row.scenario_id && item.completionCount == row.completion_count &&
                        item.lastCompletedUtcTicks == row.last_completed_utc_ticks))
                    throw new InvalidDataException("Topic completion row does not match its payload.");
            }
        }

        private static void ValidateRecordForWrite(TopicHistoryRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.instanceId) || string.IsNullOrWhiteSpace(record.topicId) ||
                !TopicReplayPolicy.IsValid(record.replayPolicy) || record.startedUtcTicks <= 0 ||
                record.startedUtcTicks > DateTime.MaxValue.Ticks || record.presentedUtcTicks < 0 || record.presentedUtcTicks > DateTime.MaxValue.Ticks ||
                record.completedUtcTicks < 0 || record.completedUtcTicks > DateTime.MaxValue.Ticks ||
                record.consumedUtcTicks < 0 || record.consumedUtcTicks > DateTime.MaxValue.Ticks ||
                record.cooldownUntilUtcTicks < 0 || record.cooldownUntilUtcTicks > DateTime.MaxValue.Ticks ||
                (record.consumedUtcTicks != 0 && record.replayPolicy != TopicReplayPolicy.Once))
                throw new InvalidDataException("Invalid Topic history record.");
            var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
            if (record.scenarioCompletions != null)
            {
                if (record.scenarioCompletions.Count > 256) throw new InvalidDataException("Too many Topic completion rows.");
                foreach (TopicScenarioCompletionRecord completion in record.scenarioCompletions)
                    if (completion == null || string.IsNullOrWhiteSpace(completion.scenarioId) || !scenarioIds.Add(completion.scenarioId) ||
                        completion.completionCount <= 0 || completion.lastCompletedUtcTicks <= 0 || completion.lastCompletedUtcTicks > DateTime.MaxValue.Ticks)
                        throw new InvalidDataException("Invalid Topic completion record.");
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return ToHex(hash.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        private static string SafeMessage(Exception exception)
        { return exception == null ? "Topic SQL storage failed." : exception.Message; }

        private sealed class TopicHistoryStateRow { public int singleton_id { get; set; } public long revision { get; set; } }
        private sealed class TopicOccurrenceRow { public string instance_id { get; set; } public string record_json { get; set; } }
        private sealed class TopicCompletionRow
        {
            public string instance_id { get; set; }
            public string scenario_id { get; set; }
            public int completion_count { get; set; }
            public long last_completed_utc_ticks { get; set; }
            public string payload_json { get; set; }
        }
        private sealed class TopicHistoryImportRow
        {
            public string source_hash { get; set; }
            public string operation_id { get; set; }
            public string imported_at_utc { get; set; }
            public int record_count { get; set; }
        }
        private sealed class TopicHistoryOperationRow
        { public string operation_id { get; set; } public string payload_hash { get; set; } public long revision { get; set; } }
    }
}
