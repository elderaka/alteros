using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Interaction;
using SQLite;

namespace AlterOS.Conversation
{
    // Private to this install. It is separate from game checkpoints and player memory.
    public sealed class SqliteConversationStore
    {
        private const int CurrentSchemaVersion = 2;
        private const int MaximumSessions = 10000;
        private const int MaximumTurnsPerSession = 10000;
        private const int MaximumSourceBytes = 32 * 1024 * 1024;
        private static object WriterGate { get { return ConversationDatabaseGate.Writer; } }
        private readonly string databasePath;
        private string readyError;

        public string DatabasePath { get { return databasePath; } }
        public bool IsReady { get { lock (WriterGate) return EnsureReady(); } }
        public string Error { get { lock (WriterGate) return readyError ?? string.Empty; } }

        public SqliteConversationStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A private conversation database path is required.", "databasePath");
            this.databasePath = Path.GetFullPath(databasePath);
            lock (WriterGate) EnsureReady();
        }

        public ConversationStoreLoadResult Load()
        {
            lock (WriterGate)
            {
                if (!EnsureReady()) return new ConversationStoreLoadResult { Error = readyError };
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        var database = new ConversationSessionDatabase();
                        long revision = ReadRevision(connection);
                        List<SessionRow> sessions = connection.Query<SessionRow>(
                            "SELECT session_id,session_kind,started_at,ended_at,last_active_at,title,summary,is_active,active_mode,active_generated_dialogue_id,active_intent_id,last_assistant_source FROM conversation_sessions ORDER BY started_at,session_id");
                        if (sessions.Count > MaximumSessions) throw new InvalidDataException("Conversation database contains too many sessions.");
                        foreach (SessionRow row in sessions)
                        {
                            if (!Enum.IsDefined(typeof(ActiveConversationMode), row.active_mode))
                                throw new InvalidDataException("Conversation routing mode is invalid.");
                            var session = new ConversationSessionRecord
                            {
                                sessionId = row.session_id,
                                sessionKind = row.session_kind,
                                startedAt = row.started_at,
                                endedAt = row.ended_at,
                                lastActiveAt = row.last_active_at,
                                title = row.title,
                                summary = row.summary,
                                isActive = row.is_active != 0,
                                routingState = new SessionRoutingState
                                {
                                    activeMode = (ActiveConversationMode)row.active_mode,
                                    activeGeneratedDialogueId = row.active_generated_dialogue_id,
                                    activeIntentId = row.active_intent_id,
                                    lastAssistantSource = row.last_assistant_source
                                },
                                turns = new List<ConversationTurnRecord>()
                            };
                            List<TurnRow> turns = connection.Query<TurnRow>(
                                "SELECT turn_id,session_id,speaker,text,translated_text,emotion,timestamp,source,intent_id,linked_generated_dialogue_id,linked_ambient_line_id FROM conversation_turns WHERE session_id=? ORDER BY turn_ordinal",
                                session.sessionId);
                            if (turns.Count > MaximumTurnsPerSession) throw new InvalidDataException("Conversation session contains too many turns.");
                            foreach (TurnRow turn in turns)
                            {
                                if (!Enum.IsDefined(typeof(ConversationTurnSource), turn.source))
                                    throw new InvalidDataException("Conversation turn source is invalid.");
                                session.turns.Add(new ConversationTurnRecord
                                {
                                    turnId = turn.turn_id,
                                    sessionId = turn.session_id,
                                    speaker = turn.speaker,
                                    text = turn.text,
                                    translatedText = turn.translated_text,
                                    emotion = turn.emotion,
                                    timestamp = turn.timestamp,
                                    source = (ConversationTurnSource)turn.source,
                                    intentId = turn.intent_id,
                                    linkedGeneratedDialogueId = turn.linked_generated_dialogue_id,
                                    linkedAmbientLineId = turn.linked_ambient_line_id
                                });
                            }
                            database.sessions.Add(session);
                        }

                        foreach (RepetitionRow row in connection.Query<RepetitionRow>(
                            "SELECT key,recent_count,last_triggered_at,stage_used FROM repetition_records ORDER BY key COLLATE NOCASE"))
                            database.repetitions.Add(new RepetitionMemoryRecord
                            { key = row.key, recentCount = row.recent_count, lastTriggeredAt = row.last_triggered_at, stageUsed = row.stage_used });

                        AmbientRow ambient = connection.Query<AmbientRow>(
                            "SELECT line_id,topic,text,triggered_at,follow_up_consumed FROM ambient_history WHERE singleton_id=1").FirstOrDefault();
                        database.lastAmbientLine = ambient == null ? new AmbientMemoryRecord() : new AmbientMemoryRecord
                        {
                            lineId = ambient.line_id,
                            topic = ambient.topic,
                            text = ambient.text,
                            triggeredAt = ambient.triggered_at,
                            followUpConsumed = ambient.follow_up_consumed != 0
                        };
                        return new ConversationStoreLoadResult { Success = true, Revision = revision, Database = database };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception);
                    return new ConversationStoreLoadResult { Error = readyError };
                }
            }
        }

        public ConversationStoreSaveResult SaveSnapshot(ConversationSessionDatabase database, long expectedRevision, string operationId)
        {
            string payloadHash;
            try
            {
                Validate(database);
                payloadHash = ComputePayloadHash(database);
            }
            catch (Exception exception)
            {
                return new ConversationStoreSaveResult { State = "invalid", Error = SafeMessage(exception) };
            }
            if (expectedRevision < 0 || string.IsNullOrWhiteSpace(operationId) || operationId.Length > 128)
                return new ConversationStoreSaveResult { State = "invalid", Error = "A nonnegative expected revision and bounded operation ID are required." };

            lock (WriterGate)
            {
                if (!EnsureReady()) return new ConversationStoreSaveResult { State = "unavailable", Error = readyError };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(false);
                    connection.BeginTransaction();
                    OperationRow prior = connection.Query<OperationRow>(
                        "SELECT operation_id,payload_hash,revision FROM conversation_operations WHERE operation_id=?", operationId).FirstOrDefault();
                    if (prior != null)
                    {
                        long actualRevision = ReadRevision(connection);
                        connection.Rollback();
                        if (!string.Equals(prior.payload_hash, payloadHash, StringComparison.Ordinal))
                            return new ConversationStoreSaveResult { State = "conflict", Revision = actualRevision, Error = "The operation ID already names different conversation data." };
                        if (actualRevision != prior.revision)
                            return new ConversationStoreSaveResult { State = "conflict", Revision = actualRevision,
                                Error = "This session save was already committed, but newer conversation data exists; reload before continuing." };
                        return new ConversationStoreSaveResult { Success = true, State = "duplicate", Revision = prior.revision };
                    }
                    long currentRevision = ReadRevision(connection);
                    if (currentRevision != expectedRevision)
                    {
                        connection.Rollback();
                        return new ConversationStoreSaveResult { State = "conflict", Revision = currentRevision, Error = "Conversation data changed; reload before saving." };
                    }
                    long nextRevision = checked(currentRevision + 1);
                    WriteSnapshot(connection, database);
                    connection.Execute("UPDATE conversation_state SET revision=? WHERE singleton_id=1 AND revision=?", nextRevision, currentRevision);
                    connection.Execute("INSERT INTO conversation_operations(operation_id,payload_hash,revision) VALUES (?,?,?)",
                        operationId, payloadHash, nextRevision);
                    connection.Commit();
                    return new ConversationStoreSaveResult { Success = true, State = "saved", Revision = nextRevision };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction)
                        try { connection.Rollback(); } catch { }
                    SetFailure(exception);
                    return new ConversationStoreSaveResult { State = "unavailable", Error = readyError };
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        public ConversationStoreSaveResult AppendTurn(ConversationTurnRecord turn, int maxTurnsPerSession,
            long expectedRevision, string operationId)
        {
            if (turn == null || maxTurnsPerSession <= 0) return new ConversationStoreSaveResult { State = "invalid", Error = "A turn and positive session turn cap are required." };
            ConversationStoreLoadResult loaded = Load();
            if (!loaded.Success) return new ConversationStoreSaveResult { State = "unavailable", Error = loaded.Error };
            if (loaded.Revision != expectedRevision) return new ConversationStoreSaveResult { State = "conflict", Revision = loaded.Revision, Error = "Conversation data changed; reload before appending." };
            ConversationSessionRecord session = loaded.Database.sessions.Find(item => string.Equals(item.sessionId, turn.sessionId, StringComparison.OrdinalIgnoreCase));
            if (session == null) return new ConversationStoreSaveResult { State = "invalid", Revision = loaded.Revision, Error = "The turn's conversation session does not exist." };
            session.turns.Add(CloneTurn(turn));
            session.lastActiveAt = turn.timestamp;
            if (session.turns.Count > maxTurnsPerSession)
                session.turns.RemoveRange(0, session.turns.Count - maxTurnsPerSession);
            return SaveSnapshot(loaded.Database, expectedRevision, operationId);
        }

        public ConversationStoreSaveResult UpdateSession(ConversationSessionRecord session, long expectedRevision, string operationId)
        {
            if (session == null) return new ConversationStoreSaveResult { State = "invalid", Error = "A conversation session is required." };
            ConversationStoreLoadResult loaded = Load();
            if (!loaded.Success) return new ConversationStoreSaveResult { State = "unavailable", Error = loaded.Error };
            if (loaded.Revision != expectedRevision) return new ConversationStoreSaveResult { State = "conflict", Revision = loaded.Revision, Error = "Conversation data changed; reload before updating." };
            int index = loaded.Database.sessions.FindIndex(item => string.Equals(item.sessionId, session.sessionId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return new ConversationStoreSaveResult { State = "invalid", Revision = loaded.Revision, Error = "The conversation session does not exist." };
            loaded.Database.sessions[index] = CloneSession(session);
            return SaveSnapshot(loaded.Database, expectedRevision, operationId);
        }

        public ConversationStoreSaveResult UpsertRepetition(RepetitionMemoryRecord repetition, long expectedRevision, string operationId)
        {
            if (repetition == null || string.IsNullOrWhiteSpace(repetition.key)) return new ConversationStoreSaveResult { State = "invalid", Error = "A repetition key is required." };
            ConversationStoreLoadResult loaded = Load();
            if (!loaded.Success) return new ConversationStoreSaveResult { State = "unavailable", Error = loaded.Error };
            if (loaded.Revision != expectedRevision) return new ConversationStoreSaveResult { State = "conflict", Revision = loaded.Revision, Error = "Conversation data changed; reload before updating." };
            int index = loaded.Database.repetitions.FindIndex(item => string.Equals(item.key, repetition.key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) loaded.Database.repetitions.Add(CloneRepetition(repetition));
            else loaded.Database.repetitions[index] = CloneRepetition(repetition);
            return SaveSnapshot(loaded.Database, expectedRevision, operationId);
        }

        public ConversationStoreSaveResult SetAmbient(AmbientMemoryRecord ambient, long expectedRevision, string operationId)
        {
            ConversationStoreLoadResult loaded = Load();
            if (!loaded.Success) return new ConversationStoreSaveResult { State = "unavailable", Error = loaded.Error };
            if (loaded.Revision != expectedRevision) return new ConversationStoreSaveResult { State = "conflict", Revision = loaded.Revision, Error = "Conversation data changed; reload before updating." };
            loaded.Database.lastAmbientLine = CloneAmbient(ambient);
            return SaveSnapshot(loaded.Database, expectedRevision, operationId);
        }

        public ConversationIdentityResult EnsureIdentity(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || value == null || value.Length > 256)
                return new ConversationIdentityResult { State = "invalid", Error = "An identity key and bounded value are required." };
            lock (WriterGate)
            {
                if (!EnsureReady()) return new ConversationIdentityResult { State = "unavailable", Error = readyError };
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        IdentityRow row = connection.Query<IdentityRow>("SELECT identity_key,identity_value FROM identity_aliases WHERE identity_key=?", key).FirstOrDefault();
                        if (row != null)
                            return string.Equals(row.identity_value, value, StringComparison.Ordinal)
                                ? new ConversationIdentityResult { State = "existing", Value = value }
                                : new ConversationIdentityResult { State = "conflict", Value = row.identity_value, Error = "A different identity is already canonical for this key." };
                        connection.Execute("INSERT INTO identity_aliases(identity_key,identity_value) VALUES (?,?)", key, value);
                        return new ConversationIdentityResult { State = "imported", Value = value };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception);
                    return new ConversationIdentityResult { State = "unavailable", Error = readyError };
                }
            }
        }

        internal ConversationSessionImportResult ImportLegacy(string sourceHash, byte[] sourceBytes, ConversationSessionDatabase database)
        {
            if (database == null || sourceBytes == null || sourceBytes.Length > MaximumSourceBytes ||
                !IsSha256(sourceHash) || !string.Equals(Sha256(sourceBytes), sourceHash, StringComparison.Ordinal))
                return new ConversationSessionImportResult { State = "recovery_required", SourceHash = sourceHash ?? string.Empty, Error = "The legacy source hash or snapshot is invalid." };
            try { Validate(database); }
            catch (Exception exception) { return new ConversationSessionImportResult { State = "recovery_required", SourceHash = sourceHash, Error = SafeMessage(exception) }; }

            lock (WriterGate)
            {
                if (!EnsureReady()) return new ConversationSessionImportResult { State = "recovery_required", SourceHash = sourceHash, Error = readyError };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(false);
                    connection.BeginTransaction();
                    ImportRow prior = connection.Query<ImportRow>("SELECT domain,source_hash,operation_id,imported_at_utc,record_count FROM history_imports WHERE domain='sessions'").FirstOrDefault();
                    long currentRevision = ReadRevision(connection);
                    if (prior != null)
                    {
                        connection.Rollback();
                        bool same = string.Equals(prior.source_hash, sourceHash, StringComparison.Ordinal);
                        return new ConversationSessionImportResult { State = same ? "already_imported" : "conflict",
                            SourceHash = prior.source_hash, OperationId = prior.operation_id, RecordCount = prior.record_count, Revision = currentRevision,
                            Error = same ? string.Empty : "A different session source already owns the private SQL history." };
                    }
                    int sessionCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM conversation_sessions");
                    int operationCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM conversation_operations");
                    if (currentRevision != 0 || sessionCount != 0 || operationCount != 0)
                    {
                        connection.Rollback();
                        return new ConversationSessionImportResult { State = "conflict", SourceHash = sourceHash, Revision = currentRevision,
                            Error = "Private SQL session data exists without a matching import receipt." };
                    }
                    WriteSnapshot(connection, database);
                    long nextRevision = 1;
                    connection.Execute("UPDATE conversation_state SET revision=1 WHERE singleton_id=1 AND revision=0");
                    connection.Execute("INSERT INTO history_source_backups(source_hash,source_bytes) VALUES (?,?)", sourceHash, sourceBytes);
                    string operationId = "session-import:" + sourceHash;
                    connection.Execute("INSERT INTO history_imports(domain,source_hash,operation_id,imported_at_utc,record_count) VALUES ('sessions',?,?,?,?)",
                        sourceHash, operationId, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), database.sessions.Count);
                    connection.Commit();
                    return new ConversationSessionImportResult { State = "imported", SourceHash = sourceHash, OperationId = operationId,
                        RecordCount = database.sessions.Count, Revision = nextRevision };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { }
                    SetFailure(exception);
                    return new ConversationSessionImportResult { State = "recovery_required", SourceHash = sourceHash, Error = readyError };
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        internal ConversationSessionImportResult CheckImportState()
        {
            lock (WriterGate)
            {
                if (!EnsureReady()) return new ConversationSessionImportResult { State = "recovery_required", Error = readyError };
                try
                {
                    using (SQLiteConnection connection = OpenConnection(false))
                    {
                        ImportRow receipt = connection.Query<ImportRow>("SELECT domain,source_hash,operation_id,imported_at_utc,record_count FROM history_imports WHERE domain='sessions'").FirstOrDefault();
                        long revision = ReadRevision(connection);
                        int sessions = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM conversation_sessions");
                        int operations = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM conversation_operations");
                        if (receipt != null) return new ConversationSessionImportResult { State = "already_imported", SourceHash = receipt.source_hash,
                            OperationId = receipt.operation_id, RecordCount = receipt.record_count, Revision = revision };
                        if (revision != 0 || sessions != 0 || operations != 0)
                            return new ConversationSessionImportResult { State = "conflict", Revision = revision,
                                Error = "Private SQL session data exists without a migration receipt." };
                        return new ConversationSessionImportResult { State = "missing", Revision = 0 };
                    }
                }
                catch (Exception exception)
                {
                    SetFailure(exception);
                    return new ConversationSessionImportResult { State = "recovery_required", Error = readyError };
                }
            }
        }

        internal ConversationSessionImportResult ImportMissing()
        {
            lock (WriterGate)
            {
                if (!EnsureReady()) return new ConversationSessionImportResult { State = "recovery_required", Error = readyError };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(false);
                    connection.BeginTransaction();
                    ImportRow receipt = connection.Query<ImportRow>("SELECT domain,source_hash,operation_id,imported_at_utc,record_count FROM history_imports WHERE domain='sessions'").FirstOrDefault();
                    long revision = ReadRevision(connection);
                    int sessions = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM conversation_sessions");
                    int operations = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM conversation_operations");
                    if (receipt != null)
                    {
                        connection.Rollback();
                        return new ConversationSessionImportResult { State = "already_imported", SourceHash = receipt.source_hash,
                            OperationId = receipt.operation_id, RecordCount = receipt.record_count, Revision = revision };
                    }
                    if (revision != 0 || sessions != 0 || operations != 0)
                    {
                        connection.Rollback();
                        return new ConversationSessionImportResult { State = "conflict", Revision = revision,
                            Error = "Private SQL session data exists without a migration receipt." };
                    }
                    const string missingHash = "missing:sessions";
                    const string operationId = "session-import:missing";
                    connection.Execute("INSERT INTO history_imports(domain,source_hash,operation_id,imported_at_utc,record_count) VALUES ('sessions',?,?,?,0)",
                        missingHash, operationId, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    connection.Commit();
                    return new ConversationSessionImportResult { State = "missing", SourceHash = missingHash,
                        OperationId = operationId, RecordCount = 0, Revision = 0 };
                }
                catch (Exception exception)
                {
                    if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { }
                    SetFailure(exception);
                    return new ConversationSessionImportResult { State = "recovery_required", Error = readyError };
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        private bool EnsureReady()
        {
            bool existed = File.Exists(databasePath);
            try
            {
                string parent = Path.GetDirectoryName(databasePath);
                if (string.IsNullOrEmpty(parent)) throw new IOException("Conversation database parent is unavailable.");
                Directory.CreateDirectory(parent);
                if (existed && new FileInfo(databasePath).Length == 0)
                    throw new InvalidDataException("An existing empty conversation database requires recovery.");
                using (SQLiteConnection connection = OpenConnection(!existed))
                {
                    int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                    if (!existed && version == 0)
                    {
                        string journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Private conversation WAL mode could not be enabled.");
                        connection.BeginTransaction();
                        CreateSchema1(connection);
                        MigrateVersion1To2(connection);
                        connection.Commit();
                    }
                    else if (version == 1 && HasSchema1(connection))
                    {
                        connection.BeginTransaction();
                        MigrateVersion1To2(connection);
                        connection.Commit();
                        if (!HasSchema2(connection))
                            throw new InvalidDataException("The private transcript migration is incomplete.");
                    }
                    else if (version != CurrentSchemaVersion || !HasSchema2(connection))
                        throw new InvalidDataException(version > CurrentSchemaVersion
                            ? "The private conversation database uses a newer schema."
                            : "The private conversation database schema is incomplete or unsupported.");
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

        private SQLiteConnection OpenConnection(bool createIfMissing)
        {
            SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex;
            if (createIfMissing) flags |= SQLiteOpenFlags.Create;
            var connection = new SQLiteConnection(databasePath, flags);
            connection.BusyTimeout = TimeSpan.FromSeconds(2);
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.Execute("PRAGMA synchronous=FULL");
            return connection;
        }

        private static void CreateSchema1(SQLiteConnection connection)
        {
            connection.Execute("CREATE TABLE metadata(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL)");
            connection.Execute("INSERT INTO metadata(key,value) VALUES ('database_domain','alteros.private_conversations')");
            connection.Execute("CREATE TABLE conversation_state(singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),revision INTEGER NOT NULL CHECK(revision>=0))");
            connection.Execute("INSERT INTO conversation_state(singleton_id,revision) VALUES (1,0)");
            connection.Execute("CREATE TABLE conversation_sessions(session_id TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,session_kind TEXT NULL,started_at TEXT NULL,ended_at TEXT NULL,last_active_at TEXT NULL,title TEXT NULL,summary TEXT NULL,is_active INTEGER NOT NULL CHECK(is_active IN (0,1)),active_mode INTEGER NOT NULL CHECK(active_mode BETWEEN 0 AND 3),active_generated_dialogue_id TEXT NULL,active_intent_id TEXT NULL,last_assistant_source TEXT NULL)");
            connection.Execute("CREATE INDEX conversation_sessions_last_active ON conversation_sessions(last_active_at)");
            connection.Execute("CREATE TABLE conversation_turns(turn_id TEXT PRIMARY KEY NOT NULL,session_id TEXT NOT NULL COLLATE NOCASE REFERENCES conversation_sessions(session_id) ON DELETE CASCADE,turn_ordinal INTEGER NOT NULL CHECK(turn_ordinal>=0),speaker TEXT NULL,text TEXT NULL,translated_text TEXT NULL,emotion TEXT NULL,timestamp TEXT NULL,source INTEGER NOT NULL CHECK(source BETWEEN 0 AND 4),intent_id TEXT NULL,linked_generated_dialogue_id TEXT NULL,linked_ambient_line_id TEXT NULL,UNIQUE(session_id,turn_ordinal))");
            connection.Execute("CREATE INDEX conversation_turns_session_order ON conversation_turns(session_id,turn_ordinal)");
            connection.Execute("CREATE TABLE repetition_records(key TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,recent_count INTEGER NOT NULL CHECK(recent_count>=0),last_triggered_at TEXT NULL,stage_used INTEGER NOT NULL)");
            connection.Execute("CREATE TABLE ambient_history(singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),line_id TEXT NULL,topic TEXT NULL,text TEXT NULL,triggered_at TEXT NULL,follow_up_consumed INTEGER NOT NULL CHECK(follow_up_consumed IN (0,1)))");
            connection.Execute("INSERT INTO ambient_history(singleton_id,follow_up_consumed) VALUES (1,0)");
            connection.Execute("CREATE TABLE history_imports(domain TEXT PRIMARY KEY NOT NULL,source_hash TEXT NOT NULL UNIQUE,operation_id TEXT NOT NULL UNIQUE,imported_at_utc TEXT NOT NULL,record_count INTEGER NOT NULL CHECK(record_count>=0))");
            connection.Execute("CREATE TABLE history_source_backups(source_hash TEXT PRIMARY KEY NOT NULL,source_bytes BLOB NOT NULL)");
            connection.Execute("CREATE TABLE conversation_operations(operation_id TEXT PRIMARY KEY NOT NULL,payload_hash TEXT NOT NULL,revision INTEGER NOT NULL CHECK(revision>0))");
            connection.Execute("CREATE TABLE identity_aliases(identity_key TEXT PRIMARY KEY NOT NULL,identity_value TEXT NOT NULL)");
            connection.Execute("PRAGMA user_version=1");
        }

        private static void MigrateVersion1To2(SQLiteConnection connection)
        {
            connection.Execute("CREATE TABLE transcript_state(singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),next_sequence INTEGER NOT NULL CHECK(next_sequence>=1))");
            connection.Execute("INSERT INTO transcript_state(singleton_id,next_sequence) VALUES (1,1)");
            connection.Execute("CREATE TABLE transcript_events(" +
                "sequence INTEGER PRIMARY KEY CHECK(sequence>0),event_id TEXT NOT NULL UNIQUE,event_type TEXT NOT NULL CHECK(event_type IN ('request_received','response_received','node_presented','turn_status'))," +
                "session_id TEXT NOT NULL,request_id TEXT NOT NULL,response_id TEXT NOT NULL,node_id TEXT NOT NULL,source TEXT NOT NULL,channel TEXT NOT NULL," +
                "topic_id TEXT NOT NULL,scenario_id TEXT NOT NULL,judgment_kind TEXT NOT NULL,speaker TEXT NOT NULL,text TEXT NOT NULL CHECK(length(text)<=2048)," +
                "display_text TEXT NOT NULL CHECK(length(display_text)<=600),expression TEXT NOT NULL CHECK(length(expression)<=64)," +
                "response_json TEXT NOT NULL CHECK(length(CAST(response_json AS BLOB))<=131072),status_code TEXT NOT NULL,timestamp_utc TEXT NOT NULL)");
            connection.Execute("CREATE INDEX transcript_events_session_sequence ON transcript_events(session_id,sequence)");
            connection.Execute("CREATE INDEX transcript_events_recent_utterances ON transcript_events(sequence DESC,event_type)");
            connection.Execute("CREATE TABLE transcript_source_backup_chunks(" +
                "source_hash TEXT NOT NULL,chunk_index INTEGER NOT NULL CHECK(chunk_index>=0),chunk_bytes BLOB NOT NULL," +
                "PRIMARY KEY(source_hash,chunk_index))");
            connection.Execute("PRAGMA user_version=2");
        }

        private static bool HasSchema1(SQLiteConnection connection)
        {
            string[] tables = { "metadata", "conversation_state", "conversation_sessions", "conversation_turns", "repetition_records", "ambient_history", "history_imports", "history_source_backups", "conversation_operations", "identity_aliases" };
            foreach (string table in tables)
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1) return false;
            return HasColumns(connection, "metadata", "key", "value") &&
                HasColumns(connection, "conversation_state", "singleton_id", "revision") &&
                HasColumns(connection, "conversation_sessions", "session_id", "session_kind", "started_at", "ended_at", "last_active_at", "title", "summary", "is_active", "active_mode", "active_generated_dialogue_id", "active_intent_id", "last_assistant_source") &&
                HasColumns(connection, "conversation_turns", "turn_id", "session_id", "turn_ordinal", "speaker", "text", "translated_text", "emotion", "timestamp", "source", "intent_id", "linked_generated_dialogue_id", "linked_ambient_line_id") &&
                HasColumns(connection, "repetition_records", "key", "recent_count", "last_triggered_at", "stage_used") &&
                HasColumns(connection, "ambient_history", "singleton_id", "line_id", "topic", "text", "triggered_at", "follow_up_consumed") &&
                HasColumns(connection, "history_imports", "domain", "source_hash", "operation_id", "imported_at_utc", "record_count") &&
                HasColumns(connection, "history_source_backups", "source_hash", "source_bytes") &&
                HasColumns(connection, "conversation_operations", "operation_id", "payload_hash", "revision") &&
                HasColumns(connection, "identity_aliases", "identity_key", "identity_value");
        }

        private static bool HasSchema2(SQLiteConnection connection)
        {
            return HasSchema1(connection) &&
                HasColumns(connection, "transcript_state", "singleton_id", "next_sequence") &&
                HasColumns(connection, "transcript_events", "sequence", "event_id", "event_type", "session_id", "request_id", "response_id", "node_id",
                    "source", "channel", "topic_id", "scenario_id", "judgment_kind", "speaker", "text", "display_text", "expression", "response_json", "status_code", "timestamp_utc") &&
                HasColumns(connection, "transcript_source_backup_chunks", "source_hash", "chunk_index", "chunk_bytes");
        }

        private static bool HasColumns(SQLiteConnection connection, string table, params string[] columns)
        {
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (TableInfoRow row in connection.Query<TableInfoRow>("PRAGMA table_info(" + table + ")")) actual.Add(row.name);
            foreach (string column in columns) if (!actual.Contains(column)) return false;
            return true;
        }

        private static long ReadRevision(SQLiteConnection connection)
        {
            StateRow state = connection.Query<StateRow>("SELECT singleton_id,revision FROM conversation_state WHERE singleton_id=1").FirstOrDefault();
            if (state == null || state.revision < 0) throw new InvalidDataException("Private conversation revision is missing or invalid.");
            return state.revision;
        }

        private static void WriteSnapshot(SQLiteConnection connection, ConversationSessionDatabase database)
        {
            connection.Execute("DELETE FROM conversation_sessions");
            connection.Execute("DELETE FROM repetition_records");
            foreach (ConversationSessionRecord session in database.sessions)
            {
                SessionRoutingState routing = session.routingState ?? new SessionRoutingState();
                connection.Execute("INSERT INTO conversation_sessions(session_id,session_kind,started_at,ended_at,last_active_at,title,summary,is_active,active_mode,active_generated_dialogue_id,active_intent_id,last_assistant_source) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                    session.sessionId, session.sessionKind, session.startedAt, session.endedAt, session.lastActiveAt, session.title, session.summary,
                    session.isActive ? 1 : 0, (int)routing.activeMode, routing.activeGeneratedDialogueId, routing.activeIntentId, routing.lastAssistantSource);
                int ordinal = 0;
                foreach (ConversationTurnRecord turn in session.turns)
                    connection.Execute("INSERT INTO conversation_turns(turn_id,session_id,turn_ordinal,speaker,text,translated_text,emotion,timestamp,source,intent_id,linked_generated_dialogue_id,linked_ambient_line_id) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                        turn.turnId, session.sessionId, ordinal++, turn.speaker, turn.text, turn.translatedText, turn.emotion, turn.timestamp,
                        (int)turn.source, turn.intentId, turn.linkedGeneratedDialogueId, turn.linkedAmbientLineId);
            }
            foreach (RepetitionMemoryRecord repetition in database.repetitions)
                connection.Execute("INSERT INTO repetition_records(key,recent_count,last_triggered_at,stage_used) VALUES (?,?,?,?)",
                    repetition.key, repetition.recentCount, repetition.lastTriggeredAt, repetition.stageUsed);
            AmbientMemoryRecord ambient = database.lastAmbientLine ?? new AmbientMemoryRecord();
            connection.Execute("UPDATE ambient_history SET line_id=?,topic=?,text=?,triggered_at=?,follow_up_consumed=? WHERE singleton_id=1",
                ambient.lineId, ambient.topic, ambient.text, ambient.triggeredAt, ambient.followUpConsumed ? 1 : 0);
        }

        private static void Validate(ConversationSessionDatabase database)
        {
            if (database == null) throw new InvalidDataException("Conversation snapshot is missing.");
            database.sessions = database.sessions ?? new List<ConversationSessionRecord>();
            database.repetitions = database.repetitions ?? new List<RepetitionMemoryRecord>();
            if (database.sessions.Count > MaximumSessions) throw new InvalidDataException("Too many conversation sessions.");
            var sessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var turnIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversationSessionRecord session in database.sessions)
            {
                if (session == null || string.IsNullOrWhiteSpace(session.sessionId) || session.sessionId.Length > 128 || !sessionIds.Add(session.sessionId))
                    throw new InvalidDataException("Conversation session identity is invalid or duplicated.");
                if (session.routingState != null && !Enum.IsDefined(typeof(ActiveConversationMode), session.routingState.activeMode))
                    throw new InvalidDataException("Conversation routing mode is invalid.");
                if (session.turns == null) session.turns = new List<ConversationTurnRecord>();
                if (session.turns.Count > MaximumTurnsPerSession) throw new InvalidDataException("Conversation session contains too many turns.");
                foreach (ConversationTurnRecord turn in session.turns)
                {
                    if (turn == null || string.IsNullOrWhiteSpace(turn.turnId) || turn.turnId.Length > 128 || !turnIds.Add(turn.turnId) ||
                        !string.Equals(turn.sessionId, session.sessionId, StringComparison.OrdinalIgnoreCase) ||
                        !Enum.IsDefined(typeof(ConversationTurnSource), turn.source))
                        throw new InvalidDataException("Conversation turn identity, ownership, or source is invalid.");
                }
            }
            var repetitionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RepetitionMemoryRecord repetition in database.repetitions)
                if (repetition == null || string.IsNullOrWhiteSpace(repetition.key) || repetition.key.Length > 256 || !repetitionKeys.Add(repetition.key) ||
                    repetition.recentCount < 0)
                    throw new InvalidDataException("Conversation repetition record is invalid or duplicated.");
        }

        private static string ComputePayloadHash(ConversationSessionDatabase database)
        {
            var builder = new StringBuilder();
            foreach (ConversationSessionRecord session in database.sessions.OrderBy(item => item.sessionId, StringComparer.OrdinalIgnoreCase))
            {
                Add(builder, session.sessionId); Add(builder, session.sessionKind); Add(builder, session.startedAt); Add(builder, session.endedAt);
                Add(builder, session.lastActiveAt); Add(builder, session.title); Add(builder, session.summary); Add(builder, session.isActive ? "1" : "0");
                SessionRoutingState routing = session.routingState ?? new SessionRoutingState();
                Add(builder, ((int)routing.activeMode).ToString(CultureInfo.InvariantCulture)); Add(builder, routing.activeGeneratedDialogueId);
                Add(builder, routing.activeIntentId); Add(builder, routing.lastAssistantSource);
                foreach (ConversationTurnRecord turn in session.turns)
                {
                    Add(builder, turn.turnId); Add(builder, turn.sessionId); Add(builder, turn.speaker); Add(builder, turn.text);
                    Add(builder, turn.translatedText); Add(builder, turn.emotion); Add(builder, turn.timestamp);
                    Add(builder, ((int)turn.source).ToString(CultureInfo.InvariantCulture)); Add(builder, turn.intentId);
                    Add(builder, turn.linkedGeneratedDialogueId); Add(builder, turn.linkedAmbientLineId);
                }
                Add(builder, null);
            }
            foreach (RepetitionMemoryRecord repetition in database.repetitions.OrderBy(item => item.key, StringComparer.OrdinalIgnoreCase))
            { Add(builder, repetition.key); Add(builder, repetition.recentCount.ToString(CultureInfo.InvariantCulture)); Add(builder, repetition.lastTriggeredAt); Add(builder, repetition.stageUsed.ToString(CultureInfo.InvariantCulture)); }
            AmbientMemoryRecord ambient = database.lastAmbientLine ?? new AmbientMemoryRecord();
            Add(builder, ambient.lineId); Add(builder, ambient.topic); Add(builder, ambient.text); Add(builder, ambient.triggeredAt); Add(builder, ambient.followUpConsumed ? "1" : "0");
            return Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static void Add(StringBuilder builder, string value)
        { if (value == null) builder.Append("-1:"); else builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value); builder.Append('|'); }

        private static ConversationSessionRecord CloneSession(ConversationSessionRecord value)
        {
            return new ConversationSessionRecord
            {
                sessionId = value.sessionId, sessionKind = value.sessionKind, startedAt = value.startedAt, endedAt = value.endedAt,
                lastActiveAt = value.lastActiveAt, title = value.title, summary = value.summary, isActive = value.isActive,
                routingState = value.routingState == null ? new SessionRoutingState() : new SessionRoutingState
                { activeMode = value.routingState.activeMode, activeGeneratedDialogueId = value.routingState.activeGeneratedDialogueId,
                    activeIntentId = value.routingState.activeIntentId, lastAssistantSource = value.routingState.lastAssistantSource },
                turns = value.turns == null ? new List<ConversationTurnRecord>() : value.turns.Select(CloneTurn).ToList()
            };
        }

        private static ConversationTurnRecord CloneTurn(ConversationTurnRecord value)
        {
            return new ConversationTurnRecord { turnId = value.turnId, sessionId = value.sessionId, speaker = value.speaker, text = value.text,
                translatedText = value.translatedText, emotion = value.emotion, timestamp = value.timestamp, source = value.source,
                intentId = value.intentId, linkedGeneratedDialogueId = value.linkedGeneratedDialogueId, linkedAmbientLineId = value.linkedAmbientLineId };
        }

        private static RepetitionMemoryRecord CloneRepetition(RepetitionMemoryRecord value)
        { return new RepetitionMemoryRecord { key = value.key, recentCount = value.recentCount, lastTriggeredAt = value.lastTriggeredAt, stageUsed = value.stageUsed }; }

        private static AmbientMemoryRecord CloneAmbient(AmbientMemoryRecord value)
        { return value == null ? new AmbientMemoryRecord() : new AmbientMemoryRecord { lineId = value.lineId, topic = value.topic, text = value.text, triggeredAt = value.triggeredAt, followUpConsumed = value.followUpConsumed }; }

        private static string Sha256(byte[] bytes)
        { using (SHA256 hash = SHA256.Create()) return ToHex(hash.ComputeHash(bytes)); }

        private static string ToHex(byte[] bytes)
        { var builder = new StringBuilder(bytes.Length * 2); foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return builder.ToString(); }

        private static bool IsSha256(string value)
        { if (value == null || value.Length != 64) return false; foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; return true; }

        private void SetFailure(Exception exception) { readyError = SafeMessage(exception); }
        private static string SafeMessage(Exception exception) { return exception == null ? "Private conversation storage failed." : exception.Message; }

        private sealed class StateRow { public int singleton_id { get; set; } public long revision { get; set; } }
        private sealed class SessionRow
        { public string session_id { get; set; } public string session_kind { get; set; } public string started_at { get; set; } public string ended_at { get; set; } public string last_active_at { get; set; } public string title { get; set; } public string summary { get; set; } public int is_active { get; set; } public int active_mode { get; set; } public string active_generated_dialogue_id { get; set; } public string active_intent_id { get; set; } public string last_assistant_source { get; set; } }
        private sealed class TurnRow
        { public string turn_id { get; set; } public string session_id { get; set; } public string speaker { get; set; } public string text { get; set; } public string translated_text { get; set; } public string emotion { get; set; } public string timestamp { get; set; } public int source { get; set; } public string intent_id { get; set; } public string linked_generated_dialogue_id { get; set; } public string linked_ambient_line_id { get; set; } }
        private sealed class RepetitionRow { public string key { get; set; } public int recent_count { get; set; } public string last_triggered_at { get; set; } public int stage_used { get; set; } }
        private sealed class AmbientRow { public int singleton_id { get; set; } public string line_id { get; set; } public string topic { get; set; } public string text { get; set; } public string triggered_at { get; set; } public int follow_up_consumed { get; set; } }
        private sealed class OperationRow { public string operation_id { get; set; } public string payload_hash { get; set; } public long revision { get; set; } }
        private sealed class ImportRow { public string domain { get; set; } public string source_hash { get; set; } public string operation_id { get; set; } public string imported_at_utc { get; set; } public int record_count { get; set; } }
        private sealed class IdentityRow { public string identity_key { get; set; } public string identity_value { get; set; } }
        private sealed class TableInfoRow { public string name { get; set; } }
    }
}
