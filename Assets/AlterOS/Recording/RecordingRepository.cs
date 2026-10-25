using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SQLite;

namespace AlterOS.Recording
{
    /// <summary>Private local capture store. It is constructed only after explicit Enable.</summary>
    public sealed class RecordingRepository
    {
        private const int SchemaVersion = 1;
        private const int MaximumPreviewBytes = 1048576;
        private readonly string databasePath;

        public string DatabasePath { get { return databasePath; } }

        public RecordingRepository(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A recording database path is required.", "path");
            databasePath = Path.GetFullPath(path);
            EnsureSchema();
        }

        internal string BeginSession(RecordingPolicy policy, DateTime nowUtc)
        {
            string sessionId = Guid.NewGuid().ToString("D");
            using (SQLiteConnection connection = Open())
            {
                connection.BeginTransaction();
                connection.Execute(
                    "INSERT OR IGNORE INTO recording_policy(policy_revision,metadata_scope,raw_text_scope,retention_days,byte_cap,record_cap,created_at_utc) VALUES (?,?,?,?,?,?,?)",
                    policy.policyRevision, policy.captureMetadata ? 1 : 0, policy.captureRawText ? 1 : 0,
                    policy.retentionDays, policy.byteCap, policy.recordCap, ToUtc(nowUtc));
                if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM recording_policy WHERE policy_revision=? AND metadata_scope=? AND raw_text_scope=? AND retention_days=? AND byte_cap=? AND record_cap=?",
                    policy.policyRevision, policy.captureMetadata ? 1 : 0, policy.captureRawText ? 1 : 0,
                    policy.retentionDays, policy.byteCap, policy.recordCap) != 1)
                {
                    connection.Rollback();
                    throw new InvalidDataException("A recording policy revision cannot be reused with different scope or limits.");
                }
                connection.Execute(
                    "INSERT INTO recording_sessions(session_id,started_at_utc,policy_revision,metadata_scope,raw_text_scope,retention_days,byte_cap,record_cap) VALUES (?,?,?,?,?,?,?,?)",
                    sessionId, ToUtc(nowUtc), policy.policyRevision, policy.captureMetadata ? 1 : 0,
                    policy.captureRawText ? 1 : 0, policy.retentionDays, policy.byteCap, policy.recordCap);
                connection.Commit();
            }
            return sessionId;
        }

        internal void EndSession(string sessionId, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            using (SQLiteConnection connection = Open())
            {
                connection.BeginTransaction();
                connection.Execute("UPDATE recording_sessions SET ended_at_utc=? WHERE session_id=? AND ended_at_utc IS NULL",
                    ToUtc(nowUtc), sessionId);
                InvalidateUnsent(connection, null);
                connection.Commit();
            }
        }

        internal RecordingResult Record(string sessionId, RecordingPolicy policy, InteractionObservation observation,
            string metadataJson, DateTime occurredAtUtc, DateTime nowUtc)
        {
            string recordId = Guid.NewGuid().ToString("D");
            string text = observation.rawText;
            string sourceEventId = policy.captureMetadata ? observation.sourceEventId : null;
            byte[] bytes = EncodeRecordPayload(observation.kind, sourceEventId, ToUtc(occurredAtUtc), metadataJson, text);
            string payloadHash = Hash(bytes);
            DateTime expiresAtUtc = nowUtc.AddDays(policy.retentionDays);
            using (SQLiteConnection connection = Open())
            {
                connection.BeginTransaction();
                List<SessionRow> sessions = connection.Query<SessionRow>(
                    "SELECT started_at_utc,ended_at_utc FROM recording_sessions WHERE session_id=?", sessionId);
                if (sessions.Count != 1 || !string.IsNullOrEmpty(sessions[0].ended_at_utc) ||
                    !InRange(ToUtc(occurredAtUtc), sessions[0].started_at_utc, ToUtc(nowUtc)))
                {
                    connection.Rollback();
                    return FailedRecord("invalid", "recording_interval_mismatch", "The observation falls outside the explicitly enabled recording interval.");
                }
                int recordCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM interaction_records WHERE session_id=?", sessionId);
                long byteCount = connection.ExecuteScalar<long>(
                    "SELECT COALESCE(SUM(byte_count),0) FROM interaction_records WHERE session_id=?", sessionId);
                if (recordCount >= policy.recordCap || byteCount + bytes.Length > policy.byteCap)
                {
                    connection.Rollback();
                    return FailedRecord("cap_reached", "recording_cap_reached", "The enabled recording reached its configured local cap.");
                }
                connection.Execute(
                    "INSERT INTO interaction_records(record_id,session_id,source_event_id,kind,occurred_at_utc,recorded_at_utc,metadata_json,raw_text,payload_hash,byte_count,expires_at_utc) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                    recordId, sessionId, sourceEventId, observation.kind, ToUtc(occurredAtUtc), ToUtc(nowUtc), metadataJson,
                    text, payloadHash, bytes.Length, ToUtc(expiresAtUtc));
                connection.Commit();
            }
            return new RecordingResult { state = "recorded", recordId = recordId, sessionId = sessionId };
        }

        internal int PruneExpired(DateTime nowUtc)
        {
            string now = ToUtc(nowUtc);
            using (SQLiteConnection connection = Open())
            {
                List<string> expired = connection.Query<IdRow>(
                    "SELECT record_id AS id FROM interaction_records WHERE expires_at_utc<=?", now)
                    .Select(row => row.id).ToList();
                if (expired.Count == 0) return 0;
                connection.BeginTransaction();
                foreach (string id in expired) DeleteRecordRows(connection, id);
                connection.Commit();
                return expired.Count;
            }
        }

        internal int DeleteRecords(IEnumerable<string> recordIds)
        {
            string[] ids = NormalizeIds(recordIds);
            if (ids.Length == 0) return 0;
            using (SQLiteConnection connection = Open())
            {
                connection.BeginTransaction();
                int deleted = 0;
                foreach (string id in ids)
                {
                    bool exists = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM interaction_records WHERE record_id=?", id) > 0;
                    if (!exists) continue;
                    DeleteRecordRows(connection, id);
                    deleted++;
                }
                connection.Commit();
                return deleted;
            }
        }

        internal ContributionPreviewResult CreatePreview(ContributionSelection selection, bool rawTextAllowed, DateTime nowUtc)
        {
            if (selection == null) return FailedPreview("invalid", "selection_required", "A contribution selection is required.");
            string validationError;
            if (!ValidateSelection(selection, out validationError))
                return FailedPreview("invalid", "selection_invalid", validationError);
            if (selection.includeRawText && !rawTextAllowed)
                return FailedPreview("scope_rejected", "text_scope_disabled", "Raw text is not enabled by the current recording policy.");

            string[] includes = NormalizeIds(selection.includeRecordIds);
            string[] excludes = NormalizeIds(selection.excludeRecordIds);
            if (includes.Intersect(excludes, StringComparer.Ordinal).Any())
                return FailedPreview("invalid", "selection_conflict", "A record cannot be explicitly included and excluded together.");

            using (SQLiteConnection connection = Open())
            {
                List<InteractionRecordRow> all = connection.Query<InteractionRecordRow>(
                    "SELECT r.record_id,r.session_id,r.source_event_id,r.kind,r.occurred_at_utc,r.recorded_at_utc,r.metadata_json,r.raw_text,r.payload_hash,r.byte_count," +
                    "s.metadata_scope,s.raw_text_scope FROM interaction_records r JOIN recording_sessions s ON s.session_id=r.session_id " +
                    "ORDER BY r.occurred_at_utc,r.record_id");
                var byId = all.ToDictionary(row => row.record_id, StringComparer.Ordinal);
                foreach (string includeId in includes)
                    if (!byId.ContainsKey(includeId)) return FailedPreview("invalid", "record_missing", "An explicitly selected record no longer exists.");
                foreach (string excludeId in excludes)
                    if (!byId.ContainsKey(excludeId)) return FailedPreview("invalid", "record_missing", "An excluded record no longer exists.");
                    else if (connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM interaction_record_decisions WHERE record_id=? AND decision='contributed'", excludeId) > 0)
                        return FailedPreview("invalid", "record_already_contributed", "A previously contributed record cannot be excluded from a later batch.");

                List<InteractionRecordRow> candidates = new List<InteractionRecordRow>();
                foreach (InteractionRecordRow row in all)
                {
                    bool explicitlyIncluded = includes.Contains(row.record_id, StringComparer.Ordinal);
                    if (excludes.Contains(row.record_id, StringComparer.Ordinal)) continue;
                    if (!explicitlyIncluded && !InRange(row.occurred_at_utc, selection)) continue;
                    if (!explicitlyIncluded && connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM interaction_record_decisions WHERE record_id=? AND decision IN ('excluded','contributed')",
                        row.record_id) > 0) continue;
                    candidates.Add(row);
                }
                if (candidates.Count == 0)
                    return FailedPreview("empty_selection", "selection_empty", "The preview selection contains no eligible records.");

                string previewId = Guid.NewGuid().ToString("D");
                var projected = candidates.Select(row => new ContributionRecordRow
                {
                    record_id = row.record_id,
                    record_hash = row.payload_hash,
                    projection_json = Project(row, selection.includeRawText),
                    projection_hash = Hash(Encoding.UTF8.GetBytes(Project(row, selection.includeRawText)))
                }).ToList();
                long byteCount = projected.Sum(row => (long)Encoding.UTF8.GetByteCount(row.projection_json));
                if (byteCount > MaximumPreviewBytes)
                    return FailedPreview("invalid", "preview_too_large", "The selected preview exceeds the one-megabyte local batch bound.");
                string selectionJson = SerializeSelection(selection, includes, excludes);
                string contentHash = ComputePreviewHash(selectionJson, selection.includeRawText, projected, excludes);

                connection.BeginTransaction();
                connection.Execute(
                    "INSERT INTO contributions(contribution_id,created_at_utc,purpose,status,content_hash,selection_json,include_raw_text) VALUES (?,?,?,?,?,?,?)",
                    previewId, ToUtc(nowUtc), "evaluation", "preview", contentHash, selectionJson, selection.includeRawText ? 1 : 0);
                int ordinal = 0;
                foreach (ContributionRecordRow row in projected)
                    connection.Execute(
                        "INSERT INTO contribution_records(contribution_id,record_id,ordinal,record_hash,projection_hash,projection_json) VALUES (?,?,?,?,?,?)",
                        previewId, row.record_id, ordinal++, row.record_hash, row.projection_hash, row.projection_json);
                foreach (string id in excludes)
                    connection.Execute("INSERT INTO contribution_exclusions(contribution_id,record_id) VALUES (?,?)", previewId, id);
                connection.Commit();
                return new ContributionPreviewResult
                {
                    state = "preview_ready", previewId = previewId, contentHash = contentHash,
                    recordCount = projected.Count, byteCount = byteCount, includesRawText = selection.includeRawText,
                    warning = selection.includeRawText || candidates.Any(row => !string.IsNullOrEmpty(row.metadata_json))
                        ? "Metadata and text may identify people. Review this exact batch before export." : string.Empty
                };
            }
        }

        internal ContributionResult Confirm(string previewId, string expectedHash, DateTime nowUtc)
        {
            if (!Guid.TryParse(previewId, out _) || string.IsNullOrWhiteSpace(expectedHash))
                return FailedContribution("invalid", "preview_identity_invalid", "A preview ID and expected hash are required.");
            using (SQLiteConnection connection = Open())
            {
                connection.BeginTransaction();
                List<ContributionRow> rows = connection.Query<ContributionRow>(
                    "SELECT contribution_id,status,content_hash,selection_json,include_raw_text FROM contributions WHERE contribution_id=?",
                    previewId);
                if (rows.Count == 0)
                {
                    connection.Rollback();
                    return FailedContribution("invalid", "preview_missing", "The contribution preview no longer exists.");
                }
                ContributionRow contribution = rows[0];
                if (contribution.status == "confirmed" || contribution.status == "exported")
                {
                    connection.Rollback();
                    return string.Equals(contribution.content_hash, expectedHash, StringComparison.Ordinal)
                        ? new ContributionResult { state = "duplicate", contributionId = previewId, contentHash = contribution.content_hash }
                        : FailedContribution("invalid", "preview_hash_changed", "The confirmed batch hash does not match.");
                }
                if (contribution.status != "preview" || !string.Equals(contribution.content_hash, expectedHash, StringComparison.Ordinal))
                {
                    connection.Rollback();
                    return FailedContribution("invalid", "preview_not_confirmable", "The preview is invalidated or its hash changed.");
                }

                List<ContributionRecordRow> records = connection.Query<ContributionRecordRow>(
                    "SELECT contribution_id,record_id,ordinal,record_hash,projection_hash,projection_json FROM contribution_records WHERE contribution_id=? ORDER BY ordinal",
                    previewId);
                string[] exclusions = connection.Query<IdRow>(
                    "SELECT record_id AS id FROM contribution_exclusions WHERE contribution_id=? ORDER BY record_id", previewId)
                    .Select(row => row.id).ToArray();
                bool sourceStable = records.All(row =>
                {
                    InteractionRecordRow source = connection.Query<InteractionRecordRow>(
                        "SELECT record_id,source_event_id,kind,occurred_at_utc,metadata_json,raw_text,payload_hash FROM interaction_records WHERE record_id=?",
                        row.record_id).FirstOrDefault();
                    return source != null && string.Equals(source.payload_hash, row.record_hash, StringComparison.Ordinal) &&
                        string.Equals(Hash(EncodeRecordPayload(source.kind, source.source_event_id, source.occurred_at_utc,
                            source.metadata_json, source.raw_text)), row.record_hash, StringComparison.Ordinal);
                });
                string recomputed = ComputePreviewHash(contribution.selection_json, contribution.include_raw_text != 0, records, exclusions);
                if (!sourceStable || !string.Equals(recomputed, contribution.content_hash, StringComparison.Ordinal))
                {
                    connection.Execute("UPDATE contributions SET status='invalidated' WHERE contribution_id=?", previewId);
                    connection.Execute("DELETE FROM contribution_records WHERE contribution_id=?", previewId);
                    connection.Execute("DELETE FROM contribution_exclusions WHERE contribution_id=?", previewId);
                    connection.Commit();
                    return FailedContribution("invalid", "source_changed", "A source record changed or disappeared; prepare and review a new preview.");
                }

                foreach (ContributionRecordRow row in records)
                    connection.Execute("INSERT OR REPLACE INTO interaction_record_decisions(record_id,decision,contribution_id) VALUES (?,'contributed',?)",
                        row.record_id, previewId);
                foreach (string id in exclusions)
                    connection.Execute("INSERT OR REPLACE INTO interaction_record_decisions(record_id,decision,contribution_id) VALUES (?,'excluded',?)",
                        id, previewId);
                connection.Execute("UPDATE contributions SET status='confirmed',confirmed_at_utc=? WHERE contribution_id=?",
                    ToUtc(nowUtc), previewId);
                connection.Commit();
                return new ContributionResult { state = "confirmed", contributionId = previewId, contentHash = contribution.content_hash };
            }
        }

        internal string ReadConfirmedExport(string contributionId, out string contentHash)
        {
            contentHash = null;
            using (SQLiteConnection connection = Open())
            {
                ContributionRow contribution = connection.Query<ContributionRow>(
                    "SELECT contribution_id,status,content_hash,selection_json,include_raw_text FROM contributions WHERE contribution_id=?",
                    contributionId).FirstOrDefault();
                if (contribution == null || (contribution.status != "confirmed" && contribution.status != "exported")) return null;
                List<ContributionRecordRow> rows = connection.Query<ContributionRecordRow>(
                    "SELECT contribution_id,record_id,ordinal,record_hash,projection_hash,projection_json FROM contribution_records WHERE contribution_id=? ORDER BY ordinal",
                    contributionId);
                contentHash = contribution.content_hash;
                string records = string.Join(",", rows.Select(row => row.projection_json).ToArray());
                return "{\"schemaVersion\":1,\"purpose\":\"evaluation\",\"contributionId\":" + JsonString(contributionId) +
                    ",\"contentHash\":" + JsonString(contentHash) + ",\"records\":[" + records + "]}";
            }
        }

        internal void MarkExported(string contributionId, DateTime nowUtc)
        {
            using (SQLiteConnection connection = Open())
                connection.Execute("UPDATE contributions SET status='exported',exported_at_utc=COALESCE(exported_at_utc,?) WHERE contribution_id=? AND status='confirmed'",
                    ToUtc(nowUtc), contributionId);
        }

        private void EnsureSchema()
        {
            bool existed = File.Exists(databasePath);
            if (File.Exists(databasePath) && new FileInfo(databasePath).Length == 0)
                throw new InvalidDataException("An existing empty recording database requires explicit recovery.");
            string directory = Path.GetDirectoryName(databasePath);
            if (string.IsNullOrEmpty(directory)) throw new IOException("The recording database parent directory is unavailable.");
            Directory.CreateDirectory(directory);
            using (SQLiteConnection connection = Open(create: !File.Exists(databasePath)))
            {
                int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                if (version > SchemaVersion) throw new InvalidDataException("The recording database uses a newer schema.");
                if (version == 0)
                {
                    if (existed && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table'") != 0)
                        throw new InvalidDataException("An unversioned non-empty recording database requires explicit recovery.");
                    connection.BeginTransaction();
                    connection.Execute("CREATE TABLE recording_policy (policy_revision TEXT PRIMARY KEY NOT NULL, metadata_scope INTEGER NOT NULL, raw_text_scope INTEGER NOT NULL, retention_days INTEGER NOT NULL, byte_cap INTEGER NOT NULL, record_cap INTEGER NOT NULL, created_at_utc TEXT NOT NULL)");
                    connection.Execute("CREATE TABLE recording_sessions (session_id TEXT PRIMARY KEY NOT NULL, started_at_utc TEXT NOT NULL, ended_at_utc TEXT NULL, policy_revision TEXT NOT NULL REFERENCES recording_policy(policy_revision), metadata_scope INTEGER NOT NULL, raw_text_scope INTEGER NOT NULL, retention_days INTEGER NOT NULL, byte_cap INTEGER NOT NULL, record_cap INTEGER NOT NULL)");
                    connection.Execute("CREATE TABLE interaction_records (record_id TEXT PRIMARY KEY NOT NULL, session_id TEXT NOT NULL REFERENCES recording_sessions(session_id), source_event_id TEXT NULL, kind TEXT NOT NULL, occurred_at_utc TEXT NOT NULL, recorded_at_utc TEXT NOT NULL, metadata_json TEXT NOT NULL, raw_text TEXT NULL, payload_hash TEXT NOT NULL, byte_count INTEGER NOT NULL, expires_at_utc TEXT NOT NULL)");
                    connection.Execute("CREATE INDEX interaction_records_time ON interaction_records(occurred_at_utc,record_id)");
                    connection.Execute("CREATE INDEX interaction_records_session ON interaction_records(session_id)");
                    connection.Execute("CREATE TABLE contributions (contribution_id TEXT PRIMARY KEY NOT NULL, created_at_utc TEXT NOT NULL, confirmed_at_utc TEXT NULL, exported_at_utc TEXT NULL, purpose TEXT NOT NULL CHECK(purpose='evaluation'), status TEXT NOT NULL CHECK(status IN ('preview','confirmed','exported','invalidated')), content_hash TEXT NOT NULL, selection_json TEXT NOT NULL, include_raw_text INTEGER NOT NULL CHECK(include_raw_text IN (0,1)))");
                    connection.Execute("CREATE TABLE contribution_records (contribution_id TEXT NOT NULL REFERENCES contributions(contribution_id), record_id TEXT NOT NULL REFERENCES interaction_records(record_id), ordinal INTEGER NOT NULL, record_hash TEXT NOT NULL, projection_hash TEXT NOT NULL, projection_json TEXT NOT NULL CHECK(length(projection_json)<=16384), PRIMARY KEY(contribution_id,record_id), UNIQUE(contribution_id,ordinal))");
                    connection.Execute("CREATE TABLE contribution_exclusions (contribution_id TEXT NOT NULL REFERENCES contributions(contribution_id), record_id TEXT NOT NULL REFERENCES interaction_records(record_id), PRIMARY KEY(contribution_id,record_id))");
                    connection.Execute("CREATE TABLE interaction_record_decisions (record_id TEXT PRIMARY KEY NOT NULL REFERENCES interaction_records(record_id), decision TEXT NOT NULL CHECK(decision IN ('excluded','contributed')), contribution_id TEXT NOT NULL REFERENCES contributions(contribution_id))");
                    connection.Execute("PRAGMA user_version=" + SchemaVersion);
                    connection.Commit();
                }
                else if (version != SchemaVersion || !HasRequiredTables(connection) ||
                    !string.Equals(connection.ExecuteScalar<string>("PRAGMA integrity_check"), "ok", StringComparison.OrdinalIgnoreCase) ||
                    connection.Query<ForeignKeyRow>("PRAGMA foreign_key_check").Count != 0)
                    throw new InvalidDataException("The recording database schema is incomplete or unsupported.");
            }
        }

        private SQLiteConnection Open(bool create = false)
        {
            SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex;
            if (create) flags |= SQLiteOpenFlags.Create;
            var connection = new SQLiteConnection(databasePath, flags);
            try
            {
                connection.BusyTimeout = TimeSpan.FromSeconds(2);
                connection.Execute("PRAGMA foreign_keys=ON");
                connection.Execute("PRAGMA synchronous=FULL");
                return connection;
            }
            catch
            {
                try { connection.Close(); } catch { }
                throw;
            }
        }

        private static bool HasRequiredTables(SQLiteConnection connection)
        {
            string[] names = { "recording_policy", "recording_sessions", "interaction_records", "contributions",
                "contribution_records", "contribution_exclusions", "interaction_record_decisions" };
            if (!names.All(name => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", name) == 1)) return false;
            return HasColumns(connection, "recording_policy", "policy_revision", "metadata_scope", "raw_text_scope", "retention_days", "byte_cap", "record_cap") &&
                HasColumns(connection, "recording_sessions", "session_id", "started_at_utc", "ended_at_utc", "policy_revision", "metadata_scope", "raw_text_scope") &&
                HasColumns(connection, "interaction_records", "record_id", "session_id", "source_event_id", "kind", "occurred_at_utc", "recorded_at_utc", "metadata_json", "raw_text", "payload_hash", "byte_count", "expires_at_utc") &&
                HasColumns(connection, "contributions", "contribution_id", "purpose", "status", "content_hash", "selection_json", "include_raw_text") &&
                HasColumns(connection, "contribution_records", "contribution_id", "record_id", "record_hash", "projection_hash", "projection_json") &&
                HasColumns(connection, "contribution_exclusions", "contribution_id", "record_id") &&
                HasColumns(connection, "interaction_record_decisions", "record_id", "decision", "contribution_id");
        }

        private static bool HasColumns(SQLiteConnection connection, string table, params string[] columns)
        {
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (ColumnRow row in connection.Query<ColumnRow>("PRAGMA table_info(" + table + ")")) actual.Add(row.name);
            return columns.All(actual.Contains);
        }

        private void InvalidateUnsent(SQLiteConnection connection, string recordId)
        {
            List<string> ids;
            if (recordId == null)
                ids = connection.Query<IdRow>("SELECT contribution_id AS id FROM contributions WHERE status IN ('preview','confirmed')")
                    .Select(row => row.id).ToList();
            else
                ids = connection.Query<IdRow>(
                    "SELECT DISTINCT c.contribution_id AS id FROM contributions c JOIN contribution_records r ON r.contribution_id=c.contribution_id WHERE r.record_id=? AND c.status IN ('preview','confirmed') " +
                    "UNION SELECT DISTINCT c.contribution_id AS id FROM contributions c JOIN contribution_exclusions x ON x.contribution_id=c.contribution_id WHERE x.record_id=? AND c.status IN ('preview','confirmed')",
                    recordId, recordId).Select(row => row.id).ToList();
            foreach (string id in ids)
            {
                connection.Execute("UPDATE contributions SET status='invalidated' WHERE contribution_id=?", id);
                connection.Execute("DELETE FROM interaction_record_decisions WHERE contribution_id=?", id);
                connection.Execute("DELETE FROM contribution_records WHERE contribution_id=?", id);
                connection.Execute("DELETE FROM contribution_exclusions WHERE contribution_id=?", id);
            }
        }

        private void DeleteRecordRows(SQLiteConnection connection, string recordId)
        {
            InvalidateUnsent(connection, recordId);
            List<string> dependent = connection.Query<IdRow>(
                "SELECT contribution_id AS id FROM contribution_records WHERE record_id=? UNION SELECT contribution_id AS id FROM contribution_exclusions WHERE record_id=?",
                recordId, recordId).Select(row => row.id).ToList();
            foreach (string contributionId in dependent)
                connection.Execute("UPDATE contributions SET status='invalidated' WHERE contribution_id=?", contributionId);
            connection.Execute("DELETE FROM contribution_records WHERE record_id=?", recordId);
            connection.Execute("DELETE FROM contribution_exclusions WHERE record_id=?", recordId);
            connection.Execute("DELETE FROM interaction_record_decisions WHERE record_id=?", recordId);
            connection.Execute("DELETE FROM interaction_records WHERE record_id=?", recordId);
        }

        private static bool ValidateSelection(ContributionSelection selection, out string error)
        {
            error = string.Empty;
            if (selection.fromUtc.HasValue && selection.fromUtc.Value.Kind != DateTimeKind.Utc ||
                selection.toUtc.HasValue && selection.toUtc.Value.Kind != DateTimeKind.Utc ||
                selection.fromUtc.HasValue && selection.toUtc.HasValue && selection.fromUtc.Value > selection.toUtc.Value)
            { error = "Contribution date bounds must be ordered UTC timestamps."; return false; }
            if (NormalizeIds(selection.includeRecordIds).Length != (selection.includeRecordIds == null ? 0 : selection.includeRecordIds.Count) ||
                NormalizeIds(selection.excludeRecordIds).Length != (selection.excludeRecordIds == null ? 0 : selection.excludeRecordIds.Count))
            { error = "Contribution record IDs must be unique, bounded UUIDs."; return false; }
            return true;
        }

        private static bool InRange(string value, ContributionSelection selection)
        {
            DateTimeOffset time;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) return false;
            return (!selection.fromUtc.HasValue || time.UtcDateTime >= selection.fromUtc.Value) &&
                (!selection.toUtc.HasValue || time.UtcDateTime <= selection.toUtc.Value);
        }

        private static bool InRange(string value, string startUtc, string endUtc)
        {
            DateTimeOffset actual, start, end;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out actual) &&
                DateTimeOffset.TryParse(startUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start) &&
                DateTimeOffset.TryParse(endUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end) &&
                actual >= start && actual <= end;
        }

        private static string Project(InteractionRecordRow row, bool includeText)
        {
            string text = includeText && row.raw_text_scope != 0 ? row.raw_text : null;
            return "{\"recordId\":" + JsonString(row.record_id) + ",\"sourceEventId\":" +
                (row.metadata_scope == 0 ? "null" : JsonString(row.source_event_id)) + ",\"kind\":" + JsonString(row.kind) +
                ",\"occurredAtUtc\":" + JsonString(row.occurred_at_utc) + ",\"recordedAtUtc\":" + JsonString(row.recorded_at_utc) +
                ",\"metadata\":" + row.metadata_json +
                (text == null ? string.Empty : ",\"text\":" + JsonString(text)) + "}";
        }

        private static string SerializeSelection(ContributionSelection selection, string[] includes, string[] excludes)
        {
            return "{\"fromUtc\":" + JsonString(selection.fromUtc.HasValue ? ToUtc(selection.fromUtc.Value) : null) +
                ",\"toUtc\":" + JsonString(selection.toUtc.HasValue ? ToUtc(selection.toUtc.Value) : null) +
                ",\"includeRawText\":" + (selection.includeRawText ? "true" : "false") +
                ",\"includeRecordIds\":[" + string.Join(",", includes.Select(JsonString).ToArray()) +
                "],\"excludeRecordIds\":[" + string.Join(",", excludes.Select(JsonString).ToArray()) + "]}";
        }

        private static string ComputePreviewHash(string selectionJson, bool includeText,
            IList<ContributionRecordRow> records, IEnumerable<string> exclusions)
        {
            var content = new StringBuilder();
            AppendPart(content, "alteros-contribution-preview-v1"); AppendPart(content, selectionJson);
            AppendPart(content, includeText ? "1" : "0");
            foreach (ContributionRecordRow record in records)
            {
                AppendPart(content, record.record_id); AppendPart(content, record.record_hash);
                AppendPart(content, record.projection_hash); AppendPart(content, record.projection_json);
            }
            foreach (string id in (exclusions ?? Enumerable.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal)) AppendPart(content, id);
            return Hash(Encoding.UTF8.GetBytes(content.ToString()));
        }

        private static void AppendPart(StringBuilder content, string value)
        {
            if (value == null) content.Append("-1:");
            else content.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
            content.Append('|');
        }

        internal static string JsonString(string value)
        {
            if (value == null) return "null";
            var result = new StringBuilder(value.Length + 2).Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 0x20) result.Append("\\u").Append(((int)character).ToString("x4"));
                        else result.Append(character);
                        break;
                }
            }
            return result.Append('"').ToString();
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static byte[] EncodeRecordPayload(string kind, string sourceEventId, string occurredAtUtc,
            string metadataJson, string rawText)
        {
            var content = new StringBuilder();
            AppendPart(content, kind); AppendPart(content, sourceEventId); AppendPart(content, occurredAtUtc);
            AppendPart(content, metadataJson); AppendPart(content, rawText);
            return Encoding.UTF8.GetBytes(content.ToString());
        }

        internal static string ToUtc(DateTime value)
        { return (value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture); }

        private static string[] NormalizeIds(IEnumerable<string> values)
        {
            string[] ids = (values ?? Enumerable.Empty<string>()).ToArray();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                Guid parsed;
                if (!Guid.TryParse(id, out parsed) || !unique.Add(id)) return new string[0];
            }
            return ids;
        }

        private static RecordingResult FailedRecord(string state, string code, string message)
        { return new RecordingResult { state = state, errorCode = code, message = message }; }

        private static ContributionPreviewResult FailedPreview(string state, string code, string message)
        { return new ContributionPreviewResult { state = state, errorCode = code, message = message, warning = string.Empty }; }

        private static ContributionResult FailedContribution(string state, string code, string message)
        { return new ContributionResult { state = state, errorCode = code, message = message }; }

        private static string[] NormalizeIds(IEnumerable<string> values, bool allowEmpty) { return NormalizeIds(values); }

        private sealed class IdRow { public string id { get; set; } }
        private sealed class ColumnRow { public string name { get; set; } }
        private sealed class ForeignKeyRow { public string table { get; set; } }
        private sealed class SessionRow
        {
            public string started_at_utc { get; set; }
            public string ended_at_utc { get; set; }
        }
        private sealed class ContributionRow
        {
            public string contribution_id { get; set; }
            public string status { get; set; }
            public string content_hash { get; set; }
            public string selection_json { get; set; }
            public int include_raw_text { get; set; }
        }
        private sealed class InteractionRecordRow
        {
            public string record_id { get; set; }
            public string session_id { get; set; }
            public string source_event_id { get; set; }
            public string kind { get; set; }
            public string occurred_at_utc { get; set; }
            public string recorded_at_utc { get; set; }
            public string metadata_json { get; set; }
            public string raw_text { get; set; }
            public string payload_hash { get; set; }
            public int byte_count { get; set; }
            public int metadata_scope { get; set; }
            public int raw_text_scope { get; set; }
        }
        private sealed class ContributionRecordRow
        {
            public string contribution_id { get; set; }
            public string record_id { get; set; }
            public int ordinal { get; set; }
            public string record_hash { get; set; }
            public string projection_hash { get; set; }
            public string projection_json { get; set; }
        }
    }
}
