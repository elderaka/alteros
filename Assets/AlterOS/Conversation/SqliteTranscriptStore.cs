using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SQLite;

namespace AlterOS.Conversation
{
    // The coordinator remains the sole append owner. This class exposes only validated
    // transcript events, bounded reads, and the deliberate legacy importer.
    public sealed class SqliteTranscriptStore : IConversationTranscriptStore
    {
        private readonly string databasePath;
        private readonly SqliteConversationStore conversationStore;
        private string readyError;

        public string DatabasePath { get { return databasePath; } }
        public bool IsReady { get { lock (ConversationDatabaseGate.Writer) return EnsureReady(); } }
        public string Error { get { lock (ConversationDatabaseGate.Writer) return readyError ?? string.Empty; } }

        public SqliteTranscriptStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A private conversation database path is required.", "databasePath");
            this.databasePath = Path.GetFullPath(databasePath);
            conversationStore = new SqliteConversationStore(this.databasePath);
            lock (ConversationDatabaseGate.Writer) EnsureReady();
        }

        public ConversationAppendResult Append(ConversationTranscriptEvent value)
        {
            if (value == null) return new ConversationAppendResult(ConversationAppendStatus.Unsaved, 0, "invalid_event");
            try { ConversationTranscript.EncodeLegacyLine(ConversationTranscriptDiskRecord.From(1, value)); }
            catch (Exception) { return new ConversationAppendResult(ConversationAppendStatus.Unsaved, 0, "invalid_event"); }

            lock (ConversationDatabaseGate.Writer)
            {
                if (!EnsureReady()) return new ConversationAppendResult(ConversationAppendStatus.Unsaved, 0, "storage_unavailable");
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection();
                    connection.BeginTransaction();
                    TranscriptEventRow prior = connection.Query<TranscriptEventRow>(
                        "SELECT sequence,event_id,event_type,session_id,request_id,response_id,node_id,source,channel,topic_id,scenario_id,judgment_kind,speaker,text,display_text,expression,response_json,status_code,timestamp_utc FROM transcript_events WHERE event_id=?",
                        value.eventId).FirstOrDefault();
                    if (prior != null)
                    {
                        connection.Rollback();
                        ConversationTranscriptEvent existing = ToEvent(prior);
                        return value.HasSamePayload(existing)
                            ? new ConversationAppendResult(ConversationAppendStatus.Duplicate, prior.sequence, string.Empty)
                            : new ConversationAppendResult(ConversationAppendStatus.Unsaved, prior.sequence, "event_id_conflict");
                    }

                    long next = ReadNextSequence(connection);
                    InsertEvent(connection, next, value);
                    int updated = connection.Execute("UPDATE transcript_state SET next_sequence=? WHERE singleton_id=1 AND next_sequence=?",
                        checked(next + 1), next);
                    if (updated != 1) throw new InvalidDataException("Transcript sequence changed during append.");
                    connection.Commit();
                    return new ConversationAppendResult(ConversationAppendStatus.Appended, next, string.Empty);
                }
                catch (FormatException) { Rollback(connection); return new ConversationAppendResult(ConversationAppendStatus.Unsaved, 0, "transcript_corrupt"); }
                catch (Exception) { Rollback(connection); return new ConversationAppendResult(ConversationAppendStatus.Unsaved, 0, "storage_unavailable"); }
                finally { if (connection != null) connection.Close(); }
            }
        }

        public ConversationTranscriptPage ReadPage(string cursor, int count)
        {
            long afterSequence;
            if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out afterSequence) || afterSequence < 0 || count < 1 || count > 100)
                return new ConversationTranscriptPage(false, "invalid_page", "0", new ConversationTranscriptRecord[0]);
            lock (ConversationDatabaseGate.Writer)
            {
                if (!EnsureReady()) return new ConversationTranscriptPage(false, "storage_unavailable", afterSequence.ToString(CultureInfo.InvariantCulture), new ConversationTranscriptRecord[0]);
                try
                {
                    using (SQLiteConnection connection = OpenConnection())
                    {
                        List<TranscriptEventRow> rows = connection.Query<TranscriptEventRow>(
                            "SELECT sequence,event_id,event_type,session_id,request_id,response_id,node_id,source,channel,topic_id,scenario_id,judgment_kind,speaker,text,display_text,expression,response_json,status_code,timestamp_utc FROM transcript_events WHERE sequence>? ORDER BY sequence LIMIT ?",
                            afterSequence, count);
                        var result = new List<ConversationTranscriptRecord>(rows.Count);
                        foreach (TranscriptEventRow row in rows) result.Add(new ConversationTranscriptRecord(row.sequence, ToEvent(row)));
                        long next = result.Count == 0 ? afterSequence : result[result.Count - 1].sequence;
                        return new ConversationTranscriptPage(true, string.Empty, next.ToString(CultureInfo.InvariantCulture), result);
                    }
                }
                catch (FormatException) { return new ConversationTranscriptPage(false, "transcript_corrupt", afterSequence.ToString(CultureInfo.InvariantCulture), new ConversationTranscriptRecord[0]); }
                catch (Exception) { return new ConversationTranscriptPage(false, "storage_unavailable", afterSequence.ToString(CultureInfo.InvariantCulture), new ConversationTranscriptRecord[0]); }
            }
        }

        public ConversationHistorySnapshot ReadRecent(int maxUtterances, int maxCharacters)
        {
            if (maxUtterances < 1 || maxUtterances > 12 || maxCharacters < 1 || maxCharacters > 6000)
                return new ConversationHistorySnapshot(false, "invalid_page", new ConversationHistoryUtterance[0]);
            lock (ConversationDatabaseGate.Writer)
            {
                if (!EnsureReady()) return new ConversationHistorySnapshot(false, "storage_unavailable", new ConversationHistoryUtterance[0]);
                try
                {
                    using (SQLiteConnection connection = OpenConnection())
                    {
                        var newest = new List<ConversationHistoryUtterance>(maxUtterances);
                        int characters = 0;
                        long beforeSequence = long.MaxValue;
                        bool done = false;
                        while (!done && newest.Count < maxUtterances)
                        {
                            List<TranscriptEventRow> rows = connection.Query<TranscriptEventRow>(
                                "SELECT sequence,event_id,event_type,session_id,request_id,response_id,node_id,source,channel,topic_id,scenario_id,judgment_kind,speaker,text,display_text,expression,response_json,status_code,timestamp_utc FROM transcript_events WHERE sequence<? ORDER BY sequence DESC LIMIT 64",
                                beforeSequence);
                            if (rows.Count == 0) break;
                            foreach (TranscriptEventRow row in rows)
                            {
                                beforeSequence = row.sequence;
                                ConversationTranscriptEvent value = ToEvent(row);
                                string text = value.eventType == "request_received" ? value.text :
                                    value.eventType == "node_presented" ? value.text : null;
                                if (text == null || text.Length > maxCharacters) continue;
                                if (newest.Count >= maxUtterances || characters + text.Length > maxCharacters)
                                { done = true; break; }
                                newest.Add(new ConversationHistoryUtterance(row.sequence,
                                    value.eventType == "request_received" ? "player" : value.speaker,
                                    value.eventType == "request_received" ? "player" : value.source,
                                    value.responseId, value.nodeId, text, value.eventId));
                                characters += text.Length;
                            }
                        }
                        newest.Reverse();
                        return new ConversationHistorySnapshot(true, string.Empty, newest);
                    }
                }
                catch (FormatException) { return new ConversationHistorySnapshot(false, "transcript_corrupt", new ConversationHistoryUtterance[0]); }
                catch (Exception) { return new ConversationHistorySnapshot(false, "storage_unavailable", new ConversationHistoryUtterance[0]); }
            }
        }

        internal ConversationTranscriptImportResult ImportLegacy(string sourcePath, TranscriptSourceSnapshot source,
            Func<string, Stream> sourceReader)
        {
            if (source == null || sourceReader == null || !IsSha256(source.sourceHash) || source.byteCount < 0 || source.recordCount < 0 || source.nextSequence != source.recordCount + 1)
                return new ConversationTranscriptImportResult { State = "recovery_required", Error = "Transcript source validation metadata is invalid." };
            lock (ConversationDatabaseGate.Writer)
            {
                if (!EnsureReady()) return new ConversationTranscriptImportResult { State = "recovery_required", Error = readyError };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection();
                    connection.BeginTransaction();
                    ImportReceiptRow prior = connection.Query<ImportReceiptRow>(
                        "SELECT domain,source_hash,operation_id,imported_at_utc,record_count FROM history_imports WHERE domain='transcript'").FirstOrDefault();
                    long currentNext = ReadNextSequence(connection);
                    int currentEvents = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM transcript_events");
                    if (prior != null)
                    {
                        connection.Rollback();
                        bool same = string.Equals(prior.source_hash, source.sourceHash, StringComparison.Ordinal);
                        return new ConversationTranscriptImportResult
                        {
                            State = same ? "already_imported" : "conflict",
                            SourceHash = prior.source_hash,
                            OperationId = prior.operation_id,
                            RecordCount = prior.record_count,
                            Revision = currentNext - 1,
                            Error = same ? string.Empty : "A different transcript source already owns this SQL history."
                        };
                    }
                    if (currentNext != 1 || currentEvents != 0)
                    {
                        connection.Rollback();
                        return new ConversationTranscriptImportResult { State = "conflict", Revision = currentNext - 1,
                            Error = "SQL transcript events exist without a matching import receipt." };
                    }

                    int chunkIndex = 0;
                    TranscriptJsonlImporter.CopyAndVerify(sourcePath, source,
                        record => InsertEvent(connection, record.sequence, record.ToEvent()),
                        (bytes, count) =>
                        {
                            var chunk = new byte[count];
                            Buffer.BlockCopy(bytes, 0, chunk, 0, count);
                            connection.Execute("INSERT INTO transcript_source_backup_chunks(source_hash,chunk_index,chunk_bytes) VALUES (?,?,?)",
                                source.sourceHash, chunkIndex++, chunk);
                        }, sourceReader);
                    if (chunkIndex == 0)
                        connection.Execute("INSERT INTO transcript_source_backup_chunks(source_hash,chunk_index,chunk_bytes) VALUES (?,?,?)",
                            source.sourceHash, 0, new byte[0]);
                    connection.Execute("UPDATE transcript_state SET next_sequence=? WHERE singleton_id=1 AND next_sequence=1", source.nextSequence);
                    string operationId = "transcript-import:" + source.sourceHash;
                    connection.Execute("INSERT INTO history_imports(domain,source_hash,operation_id,imported_at_utc,record_count) VALUES ('transcript',?,?,?,?)",
                        source.sourceHash, operationId, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), checked((int)source.recordCount));
                    connection.Commit();
                    return new ConversationTranscriptImportResult { State = "imported", SourceHash = source.sourceHash,
                        OperationId = operationId, RecordCount = source.recordCount, Revision = source.nextSequence - 1 };
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return new ConversationTranscriptImportResult { State = "recovery_required", SourceHash = source.sourceHash, Error = SafeMessage(exception) };
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        internal ConversationTranscriptImportResult CheckImportState()
        {
            lock (ConversationDatabaseGate.Writer)
            {
                if (!EnsureReady()) return new ConversationTranscriptImportResult { State = "recovery_required", Error = readyError };
                try
                {
                    using (SQLiteConnection connection = OpenConnection())
                    {
                        ImportReceiptRow receipt = connection.Query<ImportReceiptRow>(
                            "SELECT domain,source_hash,operation_id,imported_at_utc,record_count FROM history_imports WHERE domain='transcript'").FirstOrDefault();
                        long next = ReadNextSequence(connection);
                        int events = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM transcript_events");
                        if (receipt != null) return new ConversationTranscriptImportResult { State = "already_imported", SourceHash = receipt.source_hash,
                            OperationId = receipt.operation_id, RecordCount = receipt.record_count, Revision = next - 1 };
                        if (next != 1 || events != 0)
                            return new ConversationTranscriptImportResult { State = "conflict", Revision = next - 1,
                                Error = "SQL transcript events exist without an import receipt." };
                        return new ConversationTranscriptImportResult { State = "missing", Revision = 0 };
                    }
                }
                catch (Exception exception) { return new ConversationTranscriptImportResult { State = "recovery_required", Error = SafeMessage(exception) }; }
            }
        }

        internal ConversationTranscriptImportResult ImportMissing()
        {
            lock (ConversationDatabaseGate.Writer)
            {
                if (!EnsureReady()) return new ConversationTranscriptImportResult { State = "recovery_required", Error = readyError };
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection();
                    connection.BeginTransaction();
                    ImportReceiptRow receipt = connection.Query<ImportReceiptRow>(
                        "SELECT domain,source_hash,operation_id,imported_at_utc,record_count FROM history_imports WHERE domain='transcript'").FirstOrDefault();
                    long next = ReadNextSequence(connection);
                    int events = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM transcript_events");
                    if (receipt != null)
                    {
                        connection.Rollback();
                        return new ConversationTranscriptImportResult { State = "already_imported", SourceHash = receipt.source_hash,
                            OperationId = receipt.operation_id, RecordCount = receipt.record_count, Revision = next - 1 };
                    }
                    if (next != 1 || events != 0)
                    {
                        connection.Rollback();
                        return new ConversationTranscriptImportResult { State = "conflict", Revision = next - 1,
                            Error = "SQL transcript events exist without an import receipt." };
                    }
                    const string missingHash = "missing:transcript";
                    const string operationId = "transcript-import:missing";
                    connection.Execute("INSERT INTO history_imports(domain,source_hash,operation_id,imported_at_utc,record_count) VALUES ('transcript',?,?,?,0)",
                        missingHash, operationId, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    connection.Commit();
                    return new ConversationTranscriptImportResult { State = "missing", SourceHash = missingHash,
                        OperationId = operationId, RecordCount = 0, Revision = 0 };
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return new ConversationTranscriptImportResult { State = "recovery_required", Error = SafeMessage(exception) };
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        private bool EnsureReady()
        {
            if (!conversationStore.IsReady)
            { readyError = string.IsNullOrEmpty(conversationStore.Error) ? "Private conversation storage is unavailable." : conversationStore.Error; return false; }
            readyError = string.Empty;
            return true;
        }

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            connection.BusyTimeout = TimeSpan.FromSeconds(2);
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.Execute("PRAGMA synchronous=FULL");
            return connection;
        }

        private static long ReadNextSequence(SQLiteConnection connection)
        {
            TranscriptStateRow state = connection.Query<TranscriptStateRow>(
                "SELECT singleton_id,next_sequence FROM transcript_state WHERE singleton_id=1").FirstOrDefault();
            if (state == null || state.next_sequence < 1) throw new FormatException("Transcript sequence state is missing or invalid.");
            return state.next_sequence;
        }

        private static void InsertEvent(SQLiteConnection connection, long sequence, ConversationTranscriptEvent value)
        {
            ConversationTranscript.EncodeLegacyLine(ConversationTranscriptDiskRecord.From(sequence, value));
            connection.Execute("INSERT INTO transcript_events(sequence,event_id,event_type,session_id,request_id,response_id,node_id,source,channel,topic_id,scenario_id,judgment_kind,speaker,text,display_text,expression,response_json,status_code,timestamp_utc) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                sequence, value.eventId, value.eventType, value.sessionId, value.requestId, value.responseId, value.nodeId, value.source,
                value.channel, value.topicId, value.scenarioId, value.judgmentKind, value.speaker, value.text, value.displayText,
                value.expression, value.responseJson, value.statusCode, value.timestampUtc);
        }

        private static ConversationTranscriptEvent ToEvent(TranscriptEventRow row)
        {
            var disk = new ConversationTranscriptDiskRecord
            {
                schemaVersion = 1, sequence = row.sequence, eventId = row.event_id, eventType = row.event_type,
                sessionId = row.session_id, requestId = row.request_id, responseId = row.response_id, nodeId = row.node_id,
                source = row.source, channel = row.channel, topicId = row.topic_id, scenarioId = row.scenario_id,
                judgmentKind = row.judgment_kind, speaker = row.speaker, text = row.text, displayText = row.display_text,
                expression = row.expression, responseJson = row.response_json, statusCode = row.status_code, timestampUtc = row.timestamp_utc
            };
            return disk.ToEvent();
        }

        private static bool IsSha256(string value)
        { if (value == null || value.Length != 64) return false; foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; return true; }
        private static void Rollback(SQLiteConnection connection)
        { if (connection != null && connection.IsInTransaction) try { connection.Rollback(); } catch { } }
        private static string SafeMessage(Exception exception) { return exception == null ? "Transcript storage failed." : exception.Message; }

        private sealed class TranscriptStateRow { public int singleton_id { get; set; } public long next_sequence { get; set; } }
        private sealed class ImportReceiptRow { public string domain { get; set; } public string source_hash { get; set; } public string operation_id { get; set; } public string imported_at_utc { get; set; } public int record_count { get; set; } }
        private sealed class TranscriptEventRow
        {
            public long sequence { get; set; } public string event_id { get; set; } public string event_type { get; set; } public string session_id { get; set; }
            public string request_id { get; set; } public string response_id { get; set; } public string node_id { get; set; } public string source { get; set; }
            public string channel { get; set; } public string topic_id { get; set; } public string scenario_id { get; set; } public string judgment_kind { get; set; }
            public string speaker { get; set; } public string text { get; set; } public string display_text { get; set; } public string expression { get; set; }
            public string response_json { get; set; } public string status_code { get; set; } public string timestamp_utc { get; set; }
        }
    }
}
