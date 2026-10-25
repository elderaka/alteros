using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using SQLite;

namespace AlterOS.Content
{
    /// <summary>Private SQL representation of one generated archive record.</summary>
    public sealed class GeneratedArchiveRecordData
    {
        public string generatedDialogueId;
        public string originSessionId;
        public string title;
        public string sourcePrompt;
        public string summary;
        public string createdAt;
        public string lastPlayedAt;
        public int statusCode;
        public bool wasPlayed;
        public bool surfacedToPlayer;
        public string tagsJson = "[]";
        public string jsonPath;
        public long recordOrdinal;
        public string payloadState;
        public string payloadDiagnostic;
        public string payloadHash;
        public byte[] payloadBytes;
    }

    public sealed class GeneratedArchiveImportResult
    {
        public string State;
        public string Error;
        public string SourceHash;
        public string OperationId;
        public int RecordCount;
        public int PayloadCount;
    }

    public static class GeneratedArchiveTagCodec
    {
        public static string Serialize(IList<string> tags)
        {
            var values = new JsonArray();
            if (tags != null)
                foreach (string tag in tags)
                {
                    if (tag == null || tag.Length > 256) throw new InvalidDataException("Generated archive tag is invalid.");
                    values.Add(new JsonPrimitive(tag));
                }
            if (values.Count > 256) throw new InvalidDataException("Generated archive tag count exceeds its limit.");
            return ContentJson.SerializeCompact(values);
        }

        public static List<string> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            JsonArray values = ContentJson.Parse(json) as JsonArray;
            if (values == null || values.Count > 256) throw new InvalidDataException("Generated archive tag data is invalid.");
            var tags = new List<string>(values.Count);
            foreach (JsonValue value in values)
            {
                if (value.Type != JTokenType.String) throw new InvalidDataException("Generated archive tag data contains a non-string value.");
                string tag = (string)value;
                if (tag.Length > 256) throw new InvalidDataException("Generated archive tag exceeds its limit.");
                tags.Add(tag);
            }
            return tags;
        }
    }

    /// <summary>
    /// The user-specific archive store. It is intentionally separate from authored
    /// StreamingAssets content, game state, and conversation/transcript history.
    /// </summary>
    public sealed class SqliteGeneratedArchiveStore
    {
        private const int CurrentSchemaVersion = 1;
        private const int MaximumPayloadBytes = 2 * 1024 * 1024;
        private static readonly object Writer = new object();
        private readonly string databasePath;
        private string readyError;

        public string DatabasePath { get { return databasePath; } }
        public bool IsReady { get { lock (Writer) return EnsureReady(); } }
        public string Error { get { lock (Writer) return readyError ?? string.Empty; } }

        public SqliteGeneratedArchiveStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A private generated archive database path is required.", "databasePath");
            this.databasePath = Path.GetFullPath(databasePath);
            lock (Writer) EnsureReady();
        }

        public bool HasLegacyImportReceipt()
        {
            lock (Writer)
            {
                if (!EnsureReady()) return false;
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM generated_archive_imports WHERE domain='generated_archive'") == 1;
                }
                catch (Exception exception) { readyError = SafeMessage(exception); return false; }
            }
        }

        public List<GeneratedArchiveRecordData> ReadRecords()
        {
            lock (Writer)
            {
                if (!EnsureReady()) throw new InvalidOperationException(readyError);
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        List<GeneratedArchiveRecordRow> rows = connection.Query<GeneratedArchiveRecordRow>(
                            "SELECT generated_dialogue_id,origin_session_id,title,source_prompt,summary,created_at,last_played_at,status_code,was_played,surfaced_to_player,tags_json,json_path,record_ordinal,payload_state,payload_diagnostic,payload_hash FROM generated_archive_records ORDER BY record_ordinal,generated_dialogue_id");
                        return rows.Select(ToRecord).ToList();
                    }
                }
                catch (Exception exception)
                {
                    readyError = SafeMessage(exception);
                    throw new InvalidOperationException("Private generated archive records could not be read.", exception);
                }
            }
        }

        public bool TryReadPayload(string generatedDialogueId, out byte[] payloadBytes, out string payloadState)
        {
            payloadBytes = null;
            payloadState = string.Empty;
            if (string.IsNullOrWhiteSpace(generatedDialogueId)) return false;
            lock (Writer)
            {
                if (!EnsureReady()) return false;
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        GeneratedArchivePayloadRow row = connection.Query<GeneratedArchivePayloadRow>(
                            "SELECT payload_bytes,payload_state FROM generated_archive_records WHERE generated_dialogue_id=?",
                            generatedDialogueId).FirstOrDefault();
                        if (row == null) return false;
                        payloadBytes = row.payload_bytes == null ? null : (byte[])row.payload_bytes.Clone();
                        payloadState = row.payload_state ?? string.Empty;
                        return payloadBytes != null;
                    }
                }
                catch (Exception exception) { readyError = SafeMessage(exception); return false; }
            }
        }

        /// <summary>Persists runtime changes only after a complete SQL transaction commits.</summary>
        public bool SaveRecord(GeneratedArchiveRecordData value, byte[] payloadBytes)
        {
            string error;
            if (!ValidateRecord(value, out error)) { lock (Writer) readyError = error; return false; }
            if (payloadBytes != null && payloadBytes.Length > MaximumPayloadBytes)
            { lock (Writer) readyError = "Generated dialogue payload exceeds its storage limit."; return false; }

            lock (Writer)
            {
                if (!EnsureReady()) return false;
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(true);
                    connection.BeginTransaction();
                    GeneratedArchiveRecordRow prior = ReadRecord(connection, value.generatedDialogueId);
                    long ordinal = prior == null ? ReadNextOrdinal(connection) : prior.record_ordinal;
                    byte[] nextPayload = payloadBytes == null ? (prior == null ? null : prior.payload_bytes) : (byte[])payloadBytes.Clone();
                    string nextState = payloadBytes == null
                        ? (prior == null ? "missing" : prior.payload_state)
                        : (payloadBytes.Length == 0 ? "invalid_payload" : "unvalidated");
                    string nextHash = payloadBytes == null
                        ? (prior == null ? string.Empty : prior.payload_hash)
                        : GeneratedArchiveImporter.ComputeSha256(payloadBytes);
                    string nextDiagnostic = payloadBytes == null
                        ? (prior == null ? string.Empty : prior.payload_diagnostic)
                        : string.Empty;
                    InsertOrUpdate(connection, value, ordinal, nextState, nextDiagnostic, nextHash, nextPayload);
                    connection.Execute("INSERT OR IGNORE INTO generated_archive_aliases(legacy_id,generated_dialogue_id) VALUES (?,?)",
                        value.generatedDialogueId, value.generatedDialogueId);
                    connection.Execute("UPDATE generated_archive_state SET revision=revision+1 WHERE singleton_id=1");
                    connection.Commit();
                    readyError = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    readyError = SafeMessage(exception);
                    return false;
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        internal GeneratedArchiveImportResult ImportLegacy(string sourceHash, byte[] manifestBytes,
            IList<GeneratedArchiveRecordData> records, int payloadCount)
        {
            if (!GeneratedArchiveImporter.IsSha256(sourceHash) || manifestBytes == null || records == null || payloadCount < 0)
                return Failure("recovery_required", "Generated archive import metadata is invalid.");
            lock (Writer)
            {
                if (!EnsureReady()) return Failure("recovery_required", readyError);
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(true);
                    connection.BeginTransaction();
                    GeneratedArchiveImportRow prior = connection.Query<GeneratedArchiveImportRow>(
                        "SELECT domain,source_hash,operation_id,record_count,payload_count FROM generated_archive_imports WHERE domain='generated_archive'").FirstOrDefault();
                    if (prior != null)
                    {
                        connection.Rollback();
                        bool same = string.Equals(prior.source_hash, sourceHash, StringComparison.Ordinal);
                        return new GeneratedArchiveImportResult { State = same ? "already_imported" : "conflict",
                            SourceHash = prior.source_hash, OperationId = prior.operation_id,
                            RecordCount = prior.record_count, PayloadCount = prior.payload_count,
                            Error = same ? string.Empty : "A different legacy generated archive already owns this private SQL domain." };
                    }
                    if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM generated_archive_records") != 0)
                    {
                        connection.Rollback();
                        return Failure("conflict", "Private generated archive rows exist without a matching import receipt.");
                    }

                    InsertSourceBackup(connection, sourceHash, manifestBytes);
                    foreach (GeneratedArchiveRecordData record in records)
                    {
                        string error;
                        if (!ValidateRecord(record, out error)) throw new InvalidDataException(error);
                        InsertOrUpdate(connection, record, record.recordOrdinal, record.payloadState,
                            record.payloadDiagnostic ?? string.Empty, record.payloadHash ?? string.Empty, record.payloadBytes);
                        connection.Execute("INSERT INTO generated_archive_aliases(legacy_id,generated_dialogue_id) VALUES (?,?)",
                            record.generatedDialogueId, record.generatedDialogueId);
                        if (record.payloadBytes != null)
                            connection.Execute("INSERT INTO generated_archive_import_payloads(source_hash,legacy_id,payload_hash,payload_bytes,payload_state) VALUES (?,?,?,?,?)",
                                sourceHash, record.generatedDialogueId, record.payloadHash ?? string.Empty, record.payloadBytes, record.payloadState ?? "unvalidated");
                    }
                    string operationId = "generated-archive-import:" + sourceHash;
                    connection.Execute("INSERT INTO generated_archive_imports(domain,source_hash,operation_id,imported_at_utc,record_count,payload_count) VALUES ('generated_archive',?,?,?,?,?)",
                        sourceHash, operationId, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), records.Count, payloadCount);
                    connection.Execute("UPDATE generated_archive_state SET revision=revision+1 WHERE singleton_id=1");
                    connection.Commit();
                    readyError = string.Empty;
                    return new GeneratedArchiveImportResult { State = "imported", SourceHash = sourceHash,
                        OperationId = operationId, RecordCount = records.Count, PayloadCount = payloadCount };
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    readyError = SafeMessage(exception);
                    return Failure("recovery_required", readyError);
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        internal GeneratedArchiveImportResult ImportMissing()
        {
            lock (Writer)
            {
                if (!EnsureReady()) return Failure("recovery_required", readyError);
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(true);
                    connection.BeginTransaction();
                    GeneratedArchiveImportRow prior = connection.Query<GeneratedArchiveImportRow>(
                        "SELECT domain,source_hash,operation_id,record_count,payload_count FROM generated_archive_imports WHERE domain='generated_archive'").FirstOrDefault();
                    if (prior != null)
                    {
                        connection.Rollback();
                        return new GeneratedArchiveImportResult { State = "already_imported", SourceHash = prior.source_hash,
                            OperationId = prior.operation_id, RecordCount = prior.record_count, PayloadCount = prior.payload_count };
                    }
                    if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM generated_archive_records") != 0)
                    {
                        connection.Rollback();
                        return Failure("conflict", "Private generated archive rows exist without a matching import receipt.");
                    }
                    const string missingHash = "missing";
                    const string operationId = "generated-archive-import:missing";
                    connection.Execute("INSERT INTO generated_archive_imports(domain,source_hash,operation_id,imported_at_utc,record_count,payload_count) VALUES ('generated_archive',?,?,?,0,0)",
                        missingHash, operationId, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                    connection.Commit();
                    return new GeneratedArchiveImportResult { State = "missing", SourceHash = missingHash,
                        OperationId = operationId, RecordCount = 0, PayloadCount = 0 };
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    readyError = SafeMessage(exception);
                    return Failure("recovery_required", readyError);
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        internal GeneratedArchiveImportResult ExistingImport()
        {
            lock (Writer)
            {
                if (!EnsureReady()) return Failure("recovery_required", readyError);
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        GeneratedArchiveImportRow prior = connection.Query<GeneratedArchiveImportRow>(
                            "SELECT domain,source_hash,operation_id,record_count,payload_count FROM generated_archive_imports WHERE domain='generated_archive'").FirstOrDefault();
                        return prior == null ? null : new GeneratedArchiveImportResult { State = "already_imported",
                            SourceHash = prior.source_hash, OperationId = prior.operation_id,
                            RecordCount = prior.record_count, PayloadCount = prior.payload_count };
                    }
                }
                catch (Exception exception) { readyError = SafeMessage(exception); return Failure("recovery_required", readyError); }
            }
        }

        private bool EnsureReady()
        {
            bool existed = File.Exists(databasePath);
            try
            {
                string parent = Path.GetDirectoryName(databasePath);
                if (string.IsNullOrWhiteSpace(parent)) throw new IOException("Private generated archive database parent is unavailable.");
                Directory.CreateDirectory(parent);
                if (existed && new FileInfo(databasePath).Length == 0)
                    throw new InvalidDataException("An existing empty generated archive database requires recovery.");
                using (SQLiteConnection connection = OpenInitializationConnection(!existed))
                {
                    int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                    if (!existed && version == 0)
                    {
                        connection.BeginTransaction();
                        CreateSchema(connection);
                        connection.Commit();
                    }
                    else if (version != CurrentSchemaVersion || !HasSchema(connection))
                        throw new InvalidDataException(version > CurrentSchemaVersion
                            ? "Private generated archive database uses a newer schema."
                            : "Private generated archive database schema is incomplete or unsupported.");
                }
                readyError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                readyError = SafeMessage(exception);
                return false;
            }
        }

        private SQLiteConnection OpenInitializationConnection(bool createIfMissing)
        {
            SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex;
            if (createIfMissing) flags |= SQLiteOpenFlags.Create;
            var connection = new SQLiteConnection(databasePath, flags);
            connection.BusyTimeout = TimeSpan.FromSeconds(2);
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.Execute("PRAGMA synchronous=FULL");
            return connection;
        }

        private SQLiteConnection OpenConnection(bool write)
        {
            SQLiteOpenFlags flags = write
                ? SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex
                : SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex;
            var connection = new SQLiteConnection(databasePath, flags);
            connection.BusyTimeout = TimeSpan.FromSeconds(2);
            connection.Execute("PRAGMA foreign_keys=ON");
            if (write) connection.Execute("PRAGMA synchronous=FULL");
            return connection;
        }

        private static void CreateSchema(SQLiteConnection connection)
        {
            connection.Execute("CREATE TABLE metadata(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL)");
            connection.Execute("INSERT INTO metadata(key,value) VALUES ('database_domain','alteros.private_generated_archive')");
            connection.Execute("CREATE TABLE generated_archive_state(singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),revision INTEGER NOT NULL CHECK(revision>=0))");
            connection.Execute("INSERT INTO generated_archive_state(singleton_id,revision) VALUES (1,0)");
            connection.Execute("CREATE TABLE generated_archive_records(generated_dialogue_id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,origin_session_id TEXT NULL,title TEXT NULL,source_prompt TEXT NULL,summary TEXT NULL,created_at TEXT NULL,last_played_at TEXT NULL,status_code INTEGER NOT NULL CHECK(status_code BETWEEN 0 AND 4),was_played INTEGER NOT NULL CHECK(was_played IN (0,1)),surfaced_to_player INTEGER NOT NULL CHECK(surfaced_to_player IN (0,1)),tags_json TEXT NOT NULL,json_path TEXT NULL,record_ordinal INTEGER NOT NULL CHECK(record_ordinal>=0),payload_state TEXT NOT NULL,payload_diagnostic TEXT NOT NULL,payload_hash TEXT NOT NULL,payload_bytes BLOB NULL)");
            connection.Execute("CREATE INDEX generated_archive_status_order ON generated_archive_records(status_code,record_ordinal)");
            connection.Execute("CREATE INDEX generated_archive_origin_session ON generated_archive_records(origin_session_id)");
            connection.Execute("CREATE TABLE generated_archive_aliases(legacy_id TEXT PRIMARY KEY NOT NULL COLLATE BINARY,generated_dialogue_id TEXT NOT NULL COLLATE NOCASE REFERENCES generated_archive_records(generated_dialogue_id))");
            connection.Execute("CREATE TABLE generated_archive_imports(domain TEXT PRIMARY KEY NOT NULL,source_hash TEXT NOT NULL UNIQUE,operation_id TEXT NOT NULL UNIQUE,imported_at_utc TEXT NOT NULL,record_count INTEGER NOT NULL CHECK(record_count>=0),payload_count INTEGER NOT NULL CHECK(payload_count>=0))");
            connection.Execute("CREATE TABLE generated_archive_manifest_backup_chunks(source_hash TEXT NOT NULL,chunk_index INTEGER NOT NULL CHECK(chunk_index>=0),chunk_bytes BLOB NOT NULL,PRIMARY KEY(source_hash,chunk_index))");
            connection.Execute("CREATE TABLE generated_archive_import_payloads(source_hash TEXT NOT NULL,legacy_id TEXT NOT NULL COLLATE BINARY,payload_hash TEXT NOT NULL,payload_bytes BLOB NOT NULL,payload_state TEXT NOT NULL,PRIMARY KEY(source_hash,legacy_id))");
            connection.Execute("PRAGMA user_version=1");
        }

        private static bool HasSchema(SQLiteConnection connection)
        {
            string domain = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key='database_domain'");
            return string.Equals(domain, "alteros.private_generated_archive", StringComparison.Ordinal) &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('generated_archive_records','generated_archive_aliases','generated_archive_imports','generated_archive_manifest_backup_chunks','generated_archive_import_payloads','generated_archive_state')") == 6 &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM generated_archive_state WHERE singleton_id=1") == 1;
        }

        private static GeneratedArchiveRecordRow ReadRecord(SQLiteConnection connection, string id)
        {
            return connection.Query<GeneratedArchiveRecordRow>(
                "SELECT generated_dialogue_id,origin_session_id,title,source_prompt,summary,created_at,last_played_at,status_code,was_played,surfaced_to_player,tags_json,json_path,record_ordinal,payload_state,payload_diagnostic,payload_hash,payload_bytes FROM generated_archive_records WHERE generated_dialogue_id=?",
                id).FirstOrDefault();
        }

        private static long ReadNextOrdinal(SQLiteConnection connection)
        { return connection.ExecuteScalar<long>("SELECT COALESCE(MAX(record_ordinal),-1)+1 FROM generated_archive_records"); }

        private static void InsertOrUpdate(SQLiteConnection connection, GeneratedArchiveRecordData value, long ordinal,
            string payloadState, string payloadDiagnostic, string payloadHash, byte[] payloadBytes)
        {
            GeneratedArchiveRecordRow prior = ReadRecord(connection, value.generatedDialogueId);
            if (prior == null)
                connection.Execute("INSERT INTO generated_archive_records(generated_dialogue_id,origin_session_id,title,source_prompt,summary,created_at,last_played_at,status_code,was_played,surfaced_to_player,tags_json,json_path,record_ordinal,payload_state,payload_diagnostic,payload_hash,payload_bytes) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                    value.generatedDialogueId, value.originSessionId, value.title, value.sourcePrompt, value.summary,
                    value.createdAt, value.lastPlayedAt, value.statusCode, value.wasPlayed ? 1 : 0, value.surfacedToPlayer ? 1 : 0,
                    string.IsNullOrWhiteSpace(value.tagsJson) ? "[]" : value.tagsJson, value.jsonPath, ordinal,
                    string.IsNullOrWhiteSpace(payloadState) ? "missing" : payloadState, payloadDiagnostic ?? string.Empty,
                    payloadHash ?? string.Empty, payloadBytes);
            else
                connection.Execute("UPDATE generated_archive_records SET origin_session_id=?,title=?,source_prompt=?,summary=?,created_at=?,last_played_at=?,status_code=?,was_played=?,surfaced_to_player=?,tags_json=?,json_path=?,payload_state=?,payload_diagnostic=?,payload_hash=?,payload_bytes=? WHERE generated_dialogue_id=?",
                    value.originSessionId, value.title, value.sourcePrompt, value.summary, value.createdAt, value.lastPlayedAt,
                    value.statusCode, value.wasPlayed ? 1 : 0, value.surfacedToPlayer ? 1 : 0,
                    string.IsNullOrWhiteSpace(value.tagsJson) ? "[]" : value.tagsJson, value.jsonPath,
                    string.IsNullOrWhiteSpace(payloadState) ? "missing" : payloadState, payloadDiagnostic ?? string.Empty, payloadHash ?? string.Empty,
                    payloadBytes, value.generatedDialogueId);
        }

        private static void InsertSourceBackup(SQLiteConnection connection, string sourceHash, byte[] source)
        {
            const int chunkBytes = 64 * 1024;
            int chunkIndex = 0;
            for (int offset = 0; offset < source.Length; offset += chunkBytes)
            {
                int count = Math.Min(chunkBytes, source.Length - offset);
                var chunk = new byte[count];
                Buffer.BlockCopy(source, offset, chunk, 0, count);
                connection.Execute("INSERT INTO generated_archive_manifest_backup_chunks(source_hash,chunk_index,chunk_bytes) VALUES (?,?,?)",
                    sourceHash, chunkIndex++, chunk);
            }
            if (chunkIndex == 0)
                connection.Execute("INSERT INTO generated_archive_manifest_backup_chunks(source_hash,chunk_index,chunk_bytes) VALUES (?,0,?)", sourceHash, new byte[0]);
        }

        private static bool ValidateRecord(GeneratedArchiveRecordData value, out string error)
        {
            error = string.Empty;
            if (value == null || string.IsNullOrWhiteSpace(value.generatedDialogueId) || value.generatedDialogueId.Length > 256)
            { error = "Generated archive record has no valid stable ID."; return false; }
            if (value.statusCode < 0 || value.statusCode > 4)
            { error = "Generated archive record uses an unsupported status code."; return false; }
            if (value.recordOrdinal < 0)
            { error = "Generated archive record order is invalid."; return false; }
            if ((value.title ?? string.Empty).Length > 2048 || (value.sourcePrompt ?? string.Empty).Length > 32768 ||
                (value.summary ?? string.Empty).Length > 16384 || (value.tagsJson ?? string.Empty).Length > 65536 ||
                (value.jsonPath ?? string.Empty).Length > 2048)
            { error = "Generated archive metadata exceeds its storage limit."; return false; }
            if (value.payloadBytes != null && value.payloadBytes.Length > MaximumPayloadBytes)
            { error = "Generated dialogue payload exceeds its storage limit."; return false; }
            return true;
        }

        private static GeneratedArchiveRecordData ToRecord(GeneratedArchiveRecordRow row)
        {
            return new GeneratedArchiveRecordData
            {
                generatedDialogueId = row.generated_dialogue_id, originSessionId = row.origin_session_id,
                title = row.title, sourcePrompt = row.source_prompt, summary = row.summary,
                createdAt = row.created_at, lastPlayedAt = row.last_played_at, statusCode = row.status_code,
                wasPlayed = row.was_played != 0, surfacedToPlayer = row.surfaced_to_player != 0,
                tagsJson = row.tags_json, jsonPath = row.json_path, recordOrdinal = row.record_ordinal,
                payloadState = row.payload_state, payloadDiagnostic = row.payload_diagnostic, payloadHash = row.payload_hash,
                payloadBytes = row.payload_bytes == null ? null : (byte[])row.payload_bytes.Clone()
            };
        }

        private static void Rollback(SQLiteConnection connection)
        { if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { } }

        private static GeneratedArchiveImportResult Failure(string state, string error)
        { return new GeneratedArchiveImportResult { State = state, Error = error ?? string.Empty }; }

        private static string SafeMessage(Exception exception)
        { return exception == null ? "Generated archive storage failed." : exception.Message; }

        private sealed class GeneratedArchiveRecordRow
        {
            public string generated_dialogue_id { get; set; }
            public string origin_session_id { get; set; }
            public string title { get; set; }
            public string source_prompt { get; set; }
            public string summary { get; set; }
            public string created_at { get; set; }
            public string last_played_at { get; set; }
            public int status_code { get; set; }
            public int was_played { get; set; }
            public int surfaced_to_player { get; set; }
            public string tags_json { get; set; }
            public string json_path { get; set; }
            public long record_ordinal { get; set; }
            public string payload_state { get; set; }
            public string payload_diagnostic { get; set; }
            public string payload_hash { get; set; }
            public byte[] payload_bytes { get; set; }
        }

        private sealed class GeneratedArchivePayloadRow
        {
            public byte[] payload_bytes { get; set; }
            public string payload_state { get; set; }
        }

        private sealed class GeneratedArchiveImportRow
        {
            public string domain { get; set; }
            public string source_hash { get; set; }
            public string operation_id { get; set; }
            public int record_count { get; set; }
            public int payload_count { get; set; }
        }
    }
}
