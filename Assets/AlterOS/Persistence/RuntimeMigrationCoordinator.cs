using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;

namespace AlterOS.Persistence
{
    /// <summary>
    /// Reconciles independent runtime stores through a small local journal. Each owner
    /// still commits its own rows and receipt atomically; this file records progress,
    /// it does not pretend to provide a transaction across databases.
    /// </summary>
    public static class RuntimeMigrationCoordinator
    {
        private const int CurrentSchemaVersion = 1;
        private static readonly object Writer = new object();

        public static RuntimeMigrationManifest Prepare(RuntimeMigrationConfiguration configuration)
        { return Run(configuration); }

        public static RuntimeMigrationManifest Resume(RuntimeMigrationConfiguration configuration)
        { return Run(configuration); }

        public static RuntimeMigrationManifest ReadStatus(string journalPath)
        {
            if (string.IsNullOrWhiteSpace(journalPath)) return Failure("The runtime migration journal path is missing.");
            string fullPath;
            try { fullPath = Path.GetFullPath(journalPath); }
            catch (Exception exception) { return Failure(SafeMessage(exception)); }
            if (!File.Exists(fullPath)) return new RuntimeMigrationManifest { isReady = false, status = "missing", error = "The runtime migration journal has not been initialized." };
            lock (Writer)
            {
                try
                {
                    using (var journal = new RuntimeMigrationJournal(fullPath, false)) return journal.ReadManifest();
                }
                catch (Exception exception) { return Failure(SafeMessage(exception)); }
            }
        }

        private static RuntimeMigrationManifest Run(RuntimeMigrationConfiguration configuration)
        {
            string configurationError;
            if (!Validate(configuration, out configurationError)) return Failure(configurationError);
            string journalPath;
            try { journalPath = Path.GetFullPath(configuration.journalPath); }
            catch (Exception exception) { return Failure(SafeMessage(exception)); }

            lock (Writer)
            {
                RuntimeMigrationJournal journal = null;
                try
                {
                    journal = new RuntimeMigrationJournal(journalPath, true);
                    var missingCommittedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RuntimeMigrationDomainRequest request in configuration.domains)
                    {
                        RuntimeMigrationDomainStatus prior = journal.ReadDomain(request.domain);
                        bool wasCommitted = prior != null && (!string.IsNullOrWhiteSpace(prior.receiptId) ||
                            prior.status == "committed" || prior.status == "ready_empty");
                        if (wasCommitted && !File.Exists(request.targetPath)) missingCommittedTargets.Add(request.targetPath);
                    }

                    foreach (RuntimeMigrationDomainRequest request in configuration.domains)
                    {
                        RuntimeMigrationDomainStatus prior = journal.ReadDomain(request.domain);
                        bool hadReceipt = prior != null && !string.IsNullOrWhiteSpace(prior.receiptId);
                        bool wasCommitted = prior != null && (hadReceipt || prior.status == "committed" || prior.status == "ready_empty");
                        if (missingCommittedTargets.Contains(request.targetPath))
                        {
                            journal.WriteDomain(new RuntimeMigrationDomainStatus
                            {
                                domain = request.domain,
                                status = "recovery_required",
                                sourceHash = prior == null ? string.Empty : prior.sourceHash,
                                targetIdentity = prior == null ? request.targetIdentity : prior.targetIdentity,
                                schemaVersion = prior == null ? request.schemaVersion : prior.schemaVersion,
                                targetRevision = prior == null ? 0 : prior.targetRevision,
                                receiptId = prior == null ? string.Empty : prior.receiptId,
                                error = "A shared runtime migration target with committed data is missing; no owner may recreate it.",
                                updatedAtUtc = UtcNow()
                            });
                            continue;
                        }
                        if (wasCommitted && !string.Equals(prior.targetIdentity, request.targetIdentity, StringComparison.Ordinal))
                        {
                            journal.WriteDomain(new RuntimeMigrationDomainStatus
                            {
                                domain = request.domain,
                                status = "recovery_required",
                                sourceHash = prior.sourceHash,
                                targetIdentity = prior.targetIdentity,
                                schemaVersion = prior.schemaVersion,
                                targetRevision = prior.targetRevision,
                                receiptId = prior.receiptId,
                                error = "A committed runtime migration domain is pinned to a different target identity.",
                                updatedAtUtc = UtcNow()
                            });
                            continue;
                        }
                        if (prior != null && (prior.status == "committed" || prior.status == "ready_empty") &&
                            !request.validateOnResume)
                            continue;
                        journal.WriteDomain(new RuntimeMigrationDomainStatus
                        {
                            domain = request.domain,
                            status = "pending",
                            sourceHash = prior == null ? string.Empty : prior.sourceHash,
                            targetIdentity = prior == null || string.IsNullOrWhiteSpace(prior.targetIdentity)
                                ? request.targetIdentity : prior.targetIdentity,
                            schemaVersion = prior == null || prior.schemaVersion <= 0 ? request.schemaVersion : prior.schemaVersion,
                            targetRevision = prior == null ? 0 : prior.targetRevision,
                            receiptId = prior == null ? string.Empty : prior.receiptId,
                            error = string.Empty,
                            updatedAtUtc = UtcNow()
                        });

                        RuntimeMigrationDomainResult prepared;
                        try { prepared = request.prepare(); }
                        catch (Exception exception)
                        {
                            prepared = new RuntimeMigrationDomainResult { state = "failed", error = SafeMessage(exception) };
                        }
                        RuntimeMigrationDomainStatus status = ToStatus(request, prepared, prior);
                        if ((status.status == "committed" || status.status == "ready_empty") &&
                            !File.Exists(request.targetPath))
                        {
                            status.status = "recovery_required";
                            status.error = "The owner reported a durable migration but its target file is missing.";
                        }
                        journal.WriteDomain(status);
                    }

                    RuntimeMigrationManifest final = journal.ReadManifest();
                    bool ready = final.domains.Count == configuration.domains.Count &&
                        final.domains.All(item => item.status == "committed" || item.status == "ready_empty");
                    string error = ready ? string.Empty : string.Join("; ", final.domains
                        .Where(item => item.status != "committed" && item.status != "ready_empty")
                        .Select(item => item.domain + ":" + (string.IsNullOrWhiteSpace(item.error) ? item.status : item.error)).ToArray());
                    journal.WriteOverall(ready, ready ? "ready" : "recovery_required", error);
                    return journal.ReadManifest();
                }
                catch (Exception exception)
                {
                    return Failure(SafeMessage(exception));
                }
                finally { if (journal != null) journal.Dispose(); }
            }
        }

        private static RuntimeMigrationDomainStatus ToStatus(RuntimeMigrationDomainRequest request,
            RuntimeMigrationDomainResult result, RuntimeMigrationDomainStatus prior)
        {
            if (result == null) result = new RuntimeMigrationDomainResult { state = "failed", error = "Migration owner returned no status." };
            string state = result.state ?? string.Empty;
            string status = state == "imported" || state == "already_imported" || state == "existing" || state == "ready" || state == "committed"
                ? "committed"
                : state == "missing" || state == "ready_empty" ? "ready_empty"
                : state == "conflict" || state == "recovery_required" ? "recovery_required" : "failed";
            string sourceHash = string.IsNullOrWhiteSpace(result.sourceHash)
                ? (prior == null ? string.Empty : prior.sourceHash) : result.sourceHash;
            string receipt = string.IsNullOrWhiteSpace(result.receiptId)
                ? (prior == null ? string.Empty : prior.receiptId) : result.receiptId;
            int schemaVersion = result.schemaVersion > 0 ? result.schemaVersion : request.schemaVersion;
            string targetIdentity = string.IsNullOrWhiteSpace(result.targetIdentity)
                ? (request.targetIdentity ?? string.Empty) : result.targetIdentity;
            string error = status == "committed" || status == "ready_empty" ? string.Empty :
                (string.IsNullOrWhiteSpace(result.error) ? state : result.error);
            return new RuntimeMigrationDomainStatus
            {
                domain = request.domain,
                status = status,
                sourceHash = sourceHash,
                targetIdentity = targetIdentity,
                schemaVersion = schemaVersion,
                targetRevision = Math.Max(0, result.targetRevision),
                receiptId = receipt,
                error = error,
                updatedAtUtc = UtcNow()
            };
        }

        private static bool Validate(RuntimeMigrationConfiguration configuration, out string error)
        {
            error = string.Empty;
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.journalPath) || configuration.domains == null)
            { error = "Runtime migration configuration is incomplete."; return false; }
            if (configuration.domains.Count == 0)
            { error = "Runtime migration configuration has no required domains."; return false; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuntimeMigrationDomainRequest domain in configuration.domains)
            {
                if (domain == null || string.IsNullOrWhiteSpace(domain.domain) || !names.Add(domain.domain) ||
                    string.IsNullOrWhiteSpace(domain.targetPath) || string.IsNullOrWhiteSpace(domain.targetIdentity) ||
                    domain.schemaVersion < 1 || domain.prepare == null)
                { error = "Runtime migration domain request is invalid or duplicated."; return false; }
                try
                {
                    domain.targetPath = Path.GetFullPath(domain.targetPath);
                    if (!Path.IsPathRooted(domain.targetPath)) throw new InvalidDataException("Runtime migration target path must be host-resolved.");
                }
                catch (Exception exception) { error = "Runtime migration target path is invalid: " + SafeMessage(exception); return false; }
            }
            return true;
        }

        private static RuntimeMigrationManifest Failure(string error)
        { return new RuntimeMigrationManifest { isReady = false, status = "recovery_required", error = error ?? string.Empty }; }

        private static string UtcNow()
        { return DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture); }

        private static string SafeMessage(Exception exception)
        { return exception == null ? "Runtime migration failed." : exception.Message; }

        private sealed class RuntimeMigrationJournal : IDisposable
        {
            private readonly string path;

            internal RuntimeMigrationJournal(string path, bool createIfMissing)
            {
                this.path = path;
                bool existed = File.Exists(path);
                if (!createIfMissing && !existed) throw new FileNotFoundException("Runtime migration journal is missing.", path);
                string parent = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(parent)) throw new IOException("Runtime migration journal parent is unavailable.");
                if (createIfMissing) Directory.CreateDirectory(parent);
                if (existed && new FileInfo(path).Length == 0) throw new InvalidDataException("Existing empty migration journal requires recovery.");
                using (SQLiteConnection connection = OpenInitialization(createIfMissing && !existed))
                {
                    int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                    if (!existed && createIfMissing && version == 0)
                    {
                        connection.BeginTransaction();
                        connection.Execute("CREATE TABLE metadata(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL)");
                        connection.Execute("INSERT INTO metadata(key,value) VALUES ('database_domain','alteros.runtime_migration_journal')");
                        connection.Execute("CREATE TABLE runtime_migration_state(singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),revision INTEGER NOT NULL CHECK(revision>=0),status TEXT NOT NULL,updated_at_utc TEXT NOT NULL,error TEXT NOT NULL)");
                        connection.Execute("INSERT INTO runtime_migration_state(singleton_id,revision,status,updated_at_utc,error) VALUES (1,0,'not_started',?,'')", UtcNow());
                        connection.Execute("CREATE TABLE runtime_migration_domains(domain TEXT PRIMARY KEY NOT NULL,status TEXT NOT NULL,source_hash TEXT NOT NULL,target_identity TEXT NOT NULL,schema_version INTEGER NOT NULL CHECK(schema_version>=1),target_revision INTEGER NOT NULL CHECK(target_revision>=0),receipt_id TEXT NOT NULL,error TEXT NOT NULL,updated_at_utc TEXT NOT NULL)");
                        connection.Execute("PRAGMA user_version=1");
                        connection.Commit();
                    }
                    else if (version != CurrentSchemaVersion || !HasSchema(connection))
                        throw new InvalidDataException(version > CurrentSchemaVersion
                            ? "Runtime migration journal uses a newer schema."
                            : "Runtime migration journal schema is corrupt or unsupported.");
                }
            }

            internal RuntimeMigrationDomainStatus ReadDomain(string domain)
            {
                using (SQLiteConnection connection = Open(false))
                {
                    JournalDomainRow row = connection.Query<JournalDomainRow>(
                        "SELECT domain,status,source_hash,target_identity,schema_version,target_revision,receipt_id,error,updated_at_utc FROM runtime_migration_domains WHERE domain=?", domain).FirstOrDefault();
                    return row == null ? null : ToDomain(row);
                }
            }

            internal RuntimeMigrationManifest ReadManifest()
            {
                using (SQLiteConnection connection = Open(false))
                {
                    RuntimeMigrationStateRow state = connection.Query<RuntimeMigrationStateRow>(
                        "SELECT singleton_id,revision,status,updated_at_utc,error FROM runtime_migration_state WHERE singleton_id=1").FirstOrDefault();
                    if (state == null) throw new InvalidDataException("Runtime migration journal state row is missing.");
                    var manifest = new RuntimeMigrationManifest
                    {
                        schemaVersion = CurrentSchemaVersion,
                        revision = state.revision,
                        status = state.status,
                        updatedAtUtc = state.updated_at_utc,
                        error = state.error,
                        isReady = state.status == "ready"
                    };
                    foreach (JournalDomainRow row in connection.Query<JournalDomainRow>(
                        "SELECT domain,status,source_hash,target_identity,schema_version,target_revision,receipt_id,error,updated_at_utc FROM runtime_migration_domains ORDER BY domain"))
                        manifest.domains.Add(ToDomain(row));
                    return manifest;
                }
            }

            internal void WriteDomain(RuntimeMigrationDomainStatus value)
            {
                using (SQLiteConnection connection = Open(true))
                {
                    connection.BeginTransaction();
                    connection.Execute("INSERT OR REPLACE INTO runtime_migration_domains(domain,status,source_hash,target_identity,schema_version,target_revision,receipt_id,error,updated_at_utc) VALUES (?,?,?,?,?,?,?,?,?)",
                        value.domain, value.status ?? "failed", value.sourceHash ?? string.Empty, value.targetIdentity ?? string.Empty,
                        Math.Max(1, value.schemaVersion), Math.Max(0, value.targetRevision), value.receiptId ?? string.Empty,
                        value.error ?? string.Empty, value.updatedAtUtc ?? UtcNow());
                    connection.Execute("UPDATE runtime_migration_state SET revision=revision+1,status='running',updated_at_utc=?,error='' WHERE singleton_id=1", UtcNow());
                    connection.Commit();
                }
            }

            internal void WriteOverall(bool ready, string status, string error)
            {
                using (SQLiteConnection connection = Open(true))
                {
                    connection.Execute("UPDATE runtime_migration_state SET revision=revision+1,status=?,updated_at_utc=?,error=? WHERE singleton_id=1",
                        status, UtcNow(), error ?? string.Empty);
                }
            }

            private static bool HasSchema(SQLiteConnection connection)
            {
                string domain = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key='database_domain'");
                return string.Equals(domain, "alteros.runtime_migration_journal", StringComparison.Ordinal) &&
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('runtime_migration_state','runtime_migration_domains')") == 2 &&
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM runtime_migration_state WHERE singleton_id=1") == 1;
            }

            private SQLiteConnection Open(bool write)
            {
                SQLiteOpenFlags flags = write
                    ? SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex
                    : SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex;
                var connection = new SQLiteConnection(path, flags);
                connection.BusyTimeout = TimeSpan.FromSeconds(2);
                connection.Execute("PRAGMA foreign_keys=ON");
                if (write) connection.Execute("PRAGMA synchronous=FULL");
                return connection;
            }

            private SQLiteConnection OpenInitialization(bool create)
            {
                SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex;
                if (create) flags |= SQLiteOpenFlags.Create;
                var connection = new SQLiteConnection(path, flags);
                connection.BusyTimeout = TimeSpan.FromSeconds(2);
                connection.Execute("PRAGMA foreign_keys=ON");
                connection.Execute("PRAGMA synchronous=FULL");
                return connection;
            }

            public void Dispose() { }

            private static RuntimeMigrationDomainStatus ToDomain(JournalDomainRow row)
            {
                return new RuntimeMigrationDomainStatus
                {
                    domain = row.domain, status = row.status, sourceHash = row.source_hash,
                    targetIdentity = row.target_identity, schemaVersion = row.schema_version,
                    targetRevision = row.target_revision, receiptId = row.receipt_id,
                    error = row.error, updatedAtUtc = row.updated_at_utc
                };
            }

            private sealed class JournalDomainRow
            {
                public string domain { get; set; }
                public string status { get; set; }
                public string source_hash { get; set; }
                public string target_identity { get; set; }
                public int schema_version { get; set; }
                public long target_revision { get; set; }
                public string receipt_id { get; set; }
                public string error { get; set; }
                public string updated_at_utc { get; set; }
            }

            private sealed class RuntimeMigrationStateRow
            {
                public int singleton_id { get; set; }
                public long revision { get; set; }
                public string status { get; set; }
                public string updated_at_utc { get; set; }
                public string error { get; set; }
            }
        }
    }
}
