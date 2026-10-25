using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite;

namespace AlterOS.Authoring
{
    /// <summary>Transactional, revisioned authority for shared authored documents and Workbench drafts.</summary>
    public sealed class AuthoringRepository : IDisposable
    {
        private const int MaximumDocumentBytes = 4 * 1024 * 1024;
        private const int MaximumSelectionCount = 10000;
        private const long MaximumSnapshotBytes = 64L * 1024L * 1024L;
        private const int BusyTimeoutMilliseconds = 5000;

        private readonly object _gate = new object();
        private readonly string _databasePath;
        private readonly SQLiteConnection _connection;
        private readonly string _storeId;
        private bool _disposed;

        private AuthoringRepository(string databasePath, SQLiteConnection connection, string storeId)
        {
            _databasePath = databasePath;
            _connection = connection;
            _storeId = storeId;
        }

        public static AuthoringResult<AuthoringRepository> OpenExisting(string databasePath)
        {
            string fullPath;
            try
            {
                if (string.IsNullOrWhiteSpace(databasePath))
                    return Failure<AuthoringRepository>(AuthoringStatuses.Invalid, "database_path_required", "databasePath", "A host-configured authoring database path is required.");
                fullPath = Path.GetFullPath(databasePath);
            }
            catch (Exception exception)
            { return Failure<AuthoringRepository>(AuthoringStatuses.Invalid, "database_path_invalid", "databasePath", exception.Message); }

            if (Directory.Exists(fullPath))
                return Failure<AuthoringRepository>(AuthoringStatuses.Invalid, "database_path_is_directory", fullPath, "The configured authoring database path is a directory.");
            if (!File.Exists(fullPath))
                return Failure<AuthoringRepository>(AuthoringStatuses.NotInitialized, "authoring_store_missing", fullPath, "The authoring database does not exist. Initialize or recover it explicitly.");

            SQLiteConnection connection = null;
            try
            {
                connection = new SQLiteConnection(fullPath, SQLiteOpenFlags.ReadWrite, true);
                Configure(connection);

                string versionText = Metadata(connection, "schema_version");
                int schemaVersion;
                if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out schemaVersion) || schemaVersion < 1)
                    return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_metadata_invalid", fullPath, "The database does not contain a valid authoring schema version.");
                if (schemaVersion > AuthoringSchema.CurrentVersion)
                    return CloseAndFail(connection, AuthoringStatuses.UnsupportedSchema, "authoring_schema_future", fullPath, "The authoring database was created by a newer schema version.");

                if (schemaVersion == 1)
                {
                    if (connection.ExecuteScalar<int>("PRAGMA user_version") != 1 || !AuthoringSchema.HasRequiredTables(connection))
                        return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_incomplete", fullPath, "The schema-v1 authoring database is incomplete or its markers disagree.");
                    try { AuthoringSchema.MigrateVersion1To2(connection); }
                    catch (Exception exception)
                    { return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_migration_failed", fullPath, "The authoring schema-v1 migration failed: " + exception.Message); }
                    versionText = Metadata(connection, "schema_version");
                    if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out schemaVersion))
                        return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_migration_failed", fullPath, "The authoring schema version could not be read after migration.");
                }
                if (schemaVersion == 2)
                {
                    if (connection.ExecuteScalar<int>("PRAGMA user_version") != 2 || !AuthoringSchema.HasRequiredTables(connection))
                        return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_incomplete", fullPath, "The schema-v2 authoring database is incomplete or its markers disagree.");
                    try { AuthoringSchema.MigrateVersion2To3(connection); }
                    catch (Exception exception)
                    { return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_migration_failed", fullPath, "The authoring schema-v2 migration failed: " + exception.Message); }
                    versionText = Metadata(connection, "schema_version");
                    if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out schemaVersion))
                        return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_migration_failed", fullPath, "The authoring schema version could not be read after migration.");
                }
                if (schemaVersion != AuthoringSchema.CurrentVersion)
                    return CloseAndFail(connection, AuthoringStatuses.UnsupportedSchema, "authoring_schema_unsupported", fullPath, "This authoring schema version has no supported migration path.");

                int userVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
                if (userVersion != AuthoringSchema.CurrentVersion || !AuthoringSchema.HasRequiredTables(connection))
                    return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_schema_incomplete", fullPath, "The authoring schema marker and table inventory do not match.");

                string storeIdText = Metadata(connection, "store_id");
                Guid storeId;
                long storeRevision;
                if (!Guid.TryParse(storeIdText, out storeId) ||
                    !long.TryParse(Metadata(connection, "store_revision"), NumberStyles.None, CultureInfo.InvariantCulture, out storeRevision) || storeRevision < 0)
                    return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_store_identity_invalid", fullPath, "The authoring store identity or revision metadata is invalid.");

                if (!string.Equals(connection.ExecuteScalar<string>("PRAGMA quick_check(1)"), "ok", StringComparison.OrdinalIgnoreCase))
                    return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_integrity_check_failed", fullPath, "SQLite reported an integrity failure for the authoring database.");
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
                    return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_foreign_key_check_failed", fullPath, "The authoring database contains broken foreign-key references.");
                if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
                    return CloseAndFail(connection, AuthoringStatuses.RecoveryRequired, "authoring_foreign_keys_disabled", fullPath, "SQLite foreign-key enforcement could not be enabled.");

                var repository = new AuthoringRepository(fullPath, connection, storeId.ToString("D"));
                connection = null;
                return AuthoringResult<AuthoringRepository>.Ready(repository);
            }
            catch (Exception exception)
            {
                if (connection != null) connection.Dispose();
                return MapFailure<AuthoringRepository>(exception, fullPath, "authoring_open_failed");
            }
        }

        public static AuthoringResult<AuthoringRepository> CreateNew(string databasePath)
        {
            string fullPath;
            try
            {
                if (string.IsNullOrWhiteSpace(databasePath))
                    return Failure<AuthoringRepository>(AuthoringStatuses.Invalid, "database_path_required", "databasePath", "A host-configured authoring database path is required.");
                fullPath = Path.GetFullPath(databasePath);
            }
            catch (Exception exception)
            { return Failure<AuthoringRepository>(AuthoringStatuses.Invalid, "database_path_invalid", "databasePath", exception.Message); }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                return Failure<AuthoringRepository>(AuthoringStatuses.Conflict, "authoring_store_exists", fullPath, "CreateNew never replaces an existing file or directory.");
            string parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return Failure<AuthoringRepository>(AuthoringStatuses.Invalid, "database_parent_missing", fullPath, "Create the trusted database directory before initializing its authoring store.");

            string stagingPath = fullPath + ".authoring-create-" + Guid.NewGuid().ToString("N");
            string storeId = Guid.NewGuid().ToString("D");
            try
            {
                using (var connection = new SQLiteConnection(stagingPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create, true))
                {
                    Configure(connection);
                    connection.BeginTransaction();
                    try
                    {
                        AuthoringSchema.CreateCurrent(connection, storeId);
                        connection.Commit();
                    }
                    catch
                    {
                        Rollback(connection);
                        throw;
                    }
                }

                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    return Failure<AuthoringRepository>(AuthoringStatuses.Conflict, "authoring_store_exists", fullPath, "Another process initialized the authoring store first.");
                File.Move(stagingPath, fullPath);
                return OpenExisting(fullPath);
            }
            catch (Exception exception)
            { return MapFailure<AuthoringRepository>(exception, fullPath, "authoring_create_failed"); }
            finally { DeleteStagingFiles(stagingPath); }
        }

        public AuthoringResult<AuthoringDocumentPage> List(string kind, string cursor, int limit)
        {
            if (limit < 1 || limit > 100)
                return Failure<AuthoringDocumentPage>(AuthoringStatuses.Invalid, "authoring_page_limit_invalid", "limit", "Page limits must be between 1 and 100.");
            kind = kind ?? string.Empty;
            cursor = cursor ?? string.Empty;
            if (kind.Length > 0 && !AuthoringContentKinds.IsSupported(kind))
                return Failure<AuthoringDocumentPage>(AuthoringStatuses.Invalid, "authoring_kind_unsupported", "kind", "The requested authoring document kind is not registered.");

            string after = string.Empty;
            if (cursor.Length > 0 && !TryReadCursor(cursor, kind, limit, out after))
                return Failure<AuthoringDocumentPage>(AuthoringStatuses.Invalid, "authoring_cursor_invalid", "cursor", "The cursor is invalid or belongs to another store, kind or page size.");

            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringDocumentPage>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                try
                {
                    List<DocumentSummaryRow> rows = _connection.Query<DocumentSummaryRow>(
                        "SELECT document_id,kind,source_id,logical_path,head_hash AS revision_hash FROM documents WHERE (?='' OR kind=?) AND document_id>? ORDER BY document_id LIMIT ?",
                        kind, kind, after, limit + 1);
                    bool hasMore = rows.Count > limit;
                    var page = new AuthoringDocumentPage();
                    foreach (DocumentSummaryRow row in rows.Take(limit))
                        page.documents.Add(ToSummary(row));
                    if (hasMore && page.documents.Count > 0)
                        page.nextCursor = WriteCursor(kind, limit, page.documents[page.documents.Count - 1].documentId);
                    return AuthoringResult<AuthoringDocumentPage>.Ready(page);
                }
                catch (Exception exception) { return MapFailure<AuthoringDocumentPage>(exception, "", "authoring_list_failed"); }
            }
        }

        public AuthoringResult<AuthoringStoreInfo> ReadStoreInfo()
        {
            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringStoreInfo>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                try
                {
                    return AuthoringResult<AuthoringStoreInfo>.Ready(new AuthoringStoreInfo
                    {
                        storeId = _storeId,
                        schemaVersion = AuthoringSchema.CurrentVersion,
                        storeRevision = GetStoreRevision(_connection)
                    });
                }
                catch (Exception exception) { return MapFailure<AuthoringStoreInfo>(exception, "", "authoring_store_info_failed"); }
            }
        }

        public AuthoringResult<AuthoringDocument> Read(string documentId, string revisionHash)
        {
            if (!IsIdentifier(documentId) || (!string.IsNullOrEmpty(revisionHash) && !IsSha256(revisionHash)))
                return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_read_request_invalid", "documentId", "A stable document ID and an empty or SHA-256 revision are required.");
            revisionHash = revisionHash ?? string.Empty;

            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                try
                {
                    DocumentRow document = ReadDocumentRow(documentId);
                    if (document == null)
                        return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_document_missing", documentId, "The requested document does not exist.");
                    string selectedHash = revisionHash.Length == 0 ? document.head_hash : revisionHash;
                    RevisionRow revision = ReadRevisionRow(documentId, selectedHash);
                    if (revision == null)
                        return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_revision_missing", documentId, "The requested immutable revision does not exist.");
                    if (Hash(revision.document_bytes) != selectedHash)
                        return Failure<AuthoringDocument>(AuthoringStatuses.RecoveryRequired, "authoring_revision_hash_mismatch", documentId, "Stored revision bytes do not match their SHA-256 identity.");
                    return AuthoringResult<AuthoringDocument>.Ready(ToDocument(document, selectedHash, revision));
                }
                catch (Exception exception) { return MapFailure<AuthoringDocument>(exception, documentId, "authoring_read_failed"); }
            }
        }

        public AuthoringResult<AuthoringDocument> Save(AuthoringSaveRequest request)
        {
            string validationError;
            if (!ValidateSaveRequest(request, out validationError))
                return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_save_request_invalid", "request", validationError);
            if (!IsJsonDocument(request.documentBytes, out validationError))
                return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_json_invalid", request.logicalPath, validationError);

            byte[] requestHashBytes = HashSaveRequest(request);
            string requestHash = Hash(requestHashBytes);
            string pathKey = request.logicalPath.ToUpperInvariant();
            byte[] contentBytes = (byte[])request.documentBytes.Clone();
            string revisionHash = Hash(contentBytes);
            string requestedSourceId = ResolveSavedSourceId(request.kind, request.documentId, contentBytes);

            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringDocument>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                bool transaction = false;
                try
                {
                    _connection.BeginTransaction();
                    transaction = true;
                    ReserveWriteLock(_connection);
                    long currentStoreRevision = GetStoreRevision(_connection);

                    OperationReceiptRow prior = ReadOperationReceipt(request.operationId);
                    if (prior != null)
                    {
                        Rollback(_connection);
                        transaction = false;
                        if (prior.operation_kind != "document.save" || prior.request_hash != requestHash)
                            return Failure<AuthoringDocument>(AuthoringStatuses.Conflict, "authoring_operation_reused", request.operationId, "This operation ID already records a different request.");
                        return ReplaySaveReceipt(prior);
                    }

                    DocumentRow current = ReadDocumentRow(request.documentId);
                    DocumentRow pathOwner = ReadDocumentByPathKey(pathKey);
                    if (pathOwner != null && pathOwner.document_id != request.documentId)
                        return RecordSaveConflict(request, requestHash, currentStoreRevision, "authoring_path_conflict", "Another stable document ID already owns this logical path.");
                    if (current != null && (current.kind != request.kind ||
                        string.IsNullOrEmpty(request.expectedHash) || current.head_hash != request.expectedHash))
                        return RecordSaveConflict(request, requestHash, currentStoreRevision, "authoring_revision_conflict", "The document identity or expected revision no longer matches the stored head.");
                    if (current == null && !string.IsNullOrEmpty(request.expectedHash))
                        return RecordSaveConflict(request, requestHash, currentStoreRevision, "authoring_document_missing", "A nonempty expected revision requires an existing document.");

                    if (current != null && !AuthoringContentKinds.IsRawSourceKind(request.kind) &&
                        !string.IsNullOrEmpty(current.source_id) && !string.IsNullOrEmpty(requestedSourceId) &&
                        current.source_id != requestedSourceId)
                        return RecordSaveConflict(request, requestHash, currentStoreRevision, "authoring_source_identity_conflict",
                            "An existing stable source ID cannot be replaced by a different content ID.");

                    string sourceId = current != null && !string.IsNullOrEmpty(current.source_id)
                        ? current.source_id : requestedSourceId;

                    bool changed = current == null || current.head_hash != revisionHash || current.logical_path != request.logicalPath ||
                        current.source_id != sourceId;
                    string now = UtcNow();
                    if (current == null)
                    {
                        _connection.Execute("INSERT INTO documents(document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc) VALUES(?,?,?,?,?,?,?,?)",
                            request.documentId, request.kind, sourceId, request.logicalPath, pathKey, revisionHash, now, now);
                        _connection.Execute("INSERT INTO document_revisions(document_id,revision_hash,document_bytes,created_utc) VALUES(?,?,?,?)",
                            request.documentId, revisionHash, contentBytes, now);
                    }
                    else if (changed)
                    {
                        if (current.head_hash != revisionHash)
                            _connection.Execute("INSERT OR IGNORE INTO document_revisions(document_id,revision_hash,document_bytes,created_utc) VALUES(?,?,?,?)",
                                request.documentId, revisionHash, contentBytes, now);
                        _connection.Execute("UPDATE documents SET source_id=?,logical_path=?,logical_path_key=?,head_hash=?,updated_utc=? WHERE document_id=? AND head_hash=?",
                            sourceId, request.logicalPath, pathKey, revisionHash, now, request.documentId, current.head_hash);
                    }

                    long resultStoreRevision = currentStoreRevision;
                    if (changed)
                    {
                        resultStoreRevision = checked(currentStoreRevision + 1);
                        AuthoringSchema.SetMetadata(_connection, "store_revision", resultStoreRevision.ToString(CultureInfo.InvariantCulture));
                    }
                    DocumentRow savedDocument = ReadDocumentRow(request.documentId);
                    RevisionRow savedRevision = ReadRevisionRow(request.documentId, revisionHash);
                    AuthoringDocument savedValue = ToDocument(savedDocument, revisionHash, savedRevision);
                    InsertOperationReceipt(request.operationId, "document.save", requestHash, AuthoringStatuses.Ready,
                        request.documentId, revisionHash, "", "", "", EncodeDocumentResult(savedValue), resultStoreRevision);
                    _connection.Commit();
                    transaction = false;
                    return AuthoringResult<AuthoringDocument>.Ready(savedValue);
                }
                catch (Exception exception)
                {
                    if (transaction) Rollback(_connection);
                    return MapFailure<AuthoringDocument>(exception, request.logicalPath, "authoring_save_failed");
                }
            }
        }

        public AuthoringResult<AuthoringBuildSet> ReadBuildSet(string name)
        {
            if (!IsBuildSetName(name))
                return Failure<AuthoringBuildSet>(AuthoringStatuses.Invalid, "authoring_build_set_name_invalid", "name", "Build-set names must be 1..64 safe identifier characters.");
            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringBuildSet>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                try
                {
                    BuildSetRow row = ReadBuildSetRow(name);
                    if (row == null) return Failure<AuthoringBuildSet>(AuthoringStatuses.Invalid, "authoring_build_set_missing", name, "The named build set has not been created.");
                    return AuthoringResult<AuthoringBuildSet>.Ready(ToBuildSet(row, ReadBuildSetDocumentIds(name)));
                }
                catch (Exception exception) { return MapFailure<AuthoringBuildSet>(exception, name, "authoring_build_set_read_failed"); }
            }
        }

        public AuthoringResult<AuthoringBuildSet> SaveBuildSet(AuthoringBuildSetRequest request)
        {
            string validationError;
            if (!ValidateBuildSetRequest(request, out validationError))
                return Failure<AuthoringBuildSet>(AuthoringStatuses.Invalid, "authoring_build_set_request_invalid", "request", validationError);
            string requestHash = HashBuildSetRequest(request);
            string revisionHash = HashBuildSet(request.name, request.documentIds);

            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringBuildSet>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                bool transaction = false;
                try
                {
                    _connection.BeginTransaction();
                    transaction = true;
                    ReserveWriteLock(_connection);
                    long currentStoreRevision = GetStoreRevision(_connection);
                    OperationReceiptRow prior = ReadOperationReceipt(request.operationId);
                    if (prior != null)
                    {
                        Rollback(_connection);
                        transaction = false;
                        if (prior.operation_kind != "build_set.save" || prior.request_hash != requestHash)
                            return Failure<AuthoringBuildSet>(AuthoringStatuses.Conflict, "authoring_operation_reused", request.operationId, "This operation ID already records a different request.");
                        return ReplayBuildSetReceipt(prior);
                    }

                    BuildSetRow current = ReadBuildSetRow(request.name);
                    if (current == null && !string.IsNullOrEmpty(request.expectedHash))
                        return RecordBuildSetConflict(request, requestHash, currentStoreRevision, "authoring_build_set_missing", "A nonempty expected revision requires an existing build set.");
                    if (current != null && (string.IsNullOrEmpty(request.expectedHash) || current.revision_hash != request.expectedHash))
                        return RecordBuildSetConflict(request, requestHash, currentStoreRevision, "authoring_build_set_conflict", "The expected build-set revision no longer matches.");

                    foreach (string documentId in request.documentIds)
                        if (ReadDocumentRow(documentId) == null)
                            return RecordBuildSetConflict(request, requestHash, currentStoreRevision, "authoring_build_set_document_missing", "Every selected document must exist in this authoring store.");

                    bool changed = current == null || current.revision_hash != revisionHash;
                    string now = UtcNow();
                    long resultStoreRevision = currentStoreRevision;
                    if (changed)
                    {
                        if (current == null)
                            _connection.Execute("INSERT INTO build_sets(name,revision_hash,updated_utc) VALUES(?,?,?)", request.name, revisionHash, now);
                        else
                            _connection.Execute("UPDATE build_sets SET revision_hash=?,updated_utc=? WHERE name=? AND revision_hash=?", revisionHash, now, request.name, current.revision_hash);
                        _connection.Execute("DELETE FROM build_set_documents WHERE name=?", request.name);
                        for (int index = 0; index < request.documentIds.Count; index++)
                            _connection.Execute("INSERT INTO build_set_documents(name,ordinal,document_id) VALUES(?,?,?)", request.name, index, request.documentIds[index]);
                        resultStoreRevision = checked(currentStoreRevision + 1);
                        AuthoringSchema.SetMetadata(_connection, "store_revision", resultStoreRevision.ToString(CultureInfo.InvariantCulture));
                    }

                    AuthoringBuildSet result = new AuthoringBuildSet
                    {
                        name = request.name,
                        documentIds = request.documentIds.ToList(),
                        revisionHash = revisionHash,
                        updatedUtc = changed ? now : current.updated_utc
                    };
                    InsertOperationReceipt(request.operationId, "build_set.save", requestHash, AuthoringStatuses.Ready,
                        "", revisionHash, request.name, "", "", EncodeBuildSetResult(result), resultStoreRevision);
                    _connection.Commit();
                    transaction = false;
                    return AuthoringResult<AuthoringBuildSet>.Ready(result);
                }
                catch (Exception exception)
                {
                    if (transaction) Rollback(_connection);
                    return MapFailure<AuthoringBuildSet>(exception, request.name, "authoring_build_set_save_failed");
                }
            }
        }

        public AuthoringResult<AuthoringSnapshot> ReadSnapshot(IReadOnlyList<string> documentIds)
        {
            if (documentIds == null || documentIds.Count < 1 || documentIds.Count > MaximumSelectionCount)
                return Failure<AuthoringSnapshot>(AuthoringStatuses.Invalid, "authoring_snapshot_selection_invalid", "documentIds", "A snapshot requires an explicit selection of 1..10000 document IDs.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string documentId in documentIds)
                if (!IsIdentifier(documentId) || !seen.Add(documentId))
                    return Failure<AuthoringSnapshot>(AuthoringStatuses.Invalid, "authoring_snapshot_selection_invalid", "documentIds", "Snapshot IDs must be stable, supported and unique.");

            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringSnapshot>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                bool transaction = false;
                try
                {
                    _connection.BeginTransaction();
                    transaction = true;
                    long storeRevision = GetStoreRevision(_connection);
                    var heads = new Dictionary<string, SnapshotHeadRow>(StringComparer.Ordinal);
                    foreach (string[] batch in Batches(documentIds, 400))
                    {
                        string placeholders = string.Join(",", Enumerable.Repeat("?", batch.Length));
                        object[] args = batch.Cast<object>().ToArray();
                        List<SnapshotHeadRow> rows = _connection.Query<SnapshotHeadRow>(
                            "SELECT d.document_id,d.kind,d.source_id,d.logical_path,d.head_hash AS revision_hash,length(r.document_bytes) AS byte_length " +
                            "FROM documents d JOIN document_revisions r ON r.document_id=d.document_id AND r.revision_hash=d.head_hash " +
                            "WHERE d.document_id IN (" + placeholders + ")", args);
                        foreach (SnapshotHeadRow row in rows) heads[row.document_id] = row;
                    }
                    if (heads.Count != documentIds.Count)
                    {
                        Rollback(_connection);
                        transaction = false;
                        return Failure<AuthoringSnapshot>(AuthoringStatuses.Invalid, "authoring_snapshot_document_missing", "documentIds", "Every selected document must have a current head in this store.");
                    }

                    long totalBytes = heads.Values.Sum(row => (long)row.byte_length);
                    if (totalBytes > MaximumSnapshotBytes)
                    {
                        Rollback(_connection);
                        transaction = false;
                        return Failure<AuthoringSnapshot>(AuthoringStatuses.Invalid, "authoring_snapshot_too_large", "documentIds", "The selected snapshot exceeds the 64 MiB in-memory compilation bound.");
                    }

                    var revisions = new Dictionary<string, RevisionRow>(StringComparer.Ordinal);
                    foreach (string[] batch in Batches(documentIds, 400))
                    {
                        string placeholders = string.Join(",", Enumerable.Repeat("?", batch.Length));
                        object[] args = batch.Cast<object>().ToArray();
                        List<RevisionRow> rows = _connection.Query<RevisionRow>(
                            "SELECT r.document_id,r.revision_hash,r.document_bytes,r.created_utc FROM document_revisions r " +
                            "JOIN documents d ON d.document_id=r.document_id AND d.head_hash=r.revision_hash " +
                            "WHERE r.document_id IN (" + placeholders + ")", args);
                        foreach (RevisionRow row in rows) revisions[row.document_id] = row;
                    }

                    var documents = new List<AuthoringDocument>(documentIds.Count);
                    foreach (string documentId in documentIds)
                    {
                        SnapshotHeadRow head = heads[documentId];
                        RevisionRow revision = revisions[documentId];
                        if (Hash(revision.document_bytes) != head.revision_hash)
                        {
                            Rollback(_connection);
                            transaction = false;
                            return Failure<AuthoringSnapshot>(AuthoringStatuses.RecoveryRequired, "authoring_revision_hash_mismatch", documentId, "Stored revision bytes do not match their SHA-256 identity.");
                        }
                        documents.Add(new AuthoringDocument
                        {
                            documentId = head.document_id,
                            kind = head.kind,
                            sourceId = head.source_id,
                            logicalPath = head.logical_path,
                            revisionHash = head.revision_hash,
                            documentBytes = (byte[])revision.document_bytes.Clone(),
                            createdUtc = revision.created_utc
                        });
                    }

                    string snapshotHash = HashSnapshot(_storeId, documents);
                    _connection.Commit();
                    transaction = false;
                    return AuthoringResult<AuthoringSnapshot>.Ready(new AuthoringSnapshot
                    {
                        storeId = _storeId,
                        storeRevision = storeRevision,
                        snapshotHash = snapshotHash,
                        totalBytes = totalBytes,
                        documents = documents
                    });
                }
                catch (Exception exception)
                {
                    if (transaction) Rollback(_connection);
                    return MapFailure<AuthoringSnapshot>(exception, "documentIds", "authoring_snapshot_failed");
                }
            }
        }

        internal AuthoringResult<AuthoringImportReceipt> ReplayImportIfExists(string operationId, string requestHash)
        {
            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringImportReceipt>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                try
                {
                    ImportReceiptRow receipt = ReadImportReceipt(operationId);
                    if (receipt == null) return null;
                    if (receipt.request_hash != requestHash)
                        return Failure<AuthoringImportReceipt>(AuthoringStatuses.Conflict, "authoring_operation_reused", operationId, "This import operation ID already records a different inventory.");
                    return ReplayImportReceipt(receipt);
                }
                catch (Exception exception) { return MapFailure<AuthoringImportReceipt>(exception, operationId, "authoring_import_receipt_read_failed"); }
            }
        }

        internal AuthoringResult<AuthoringImportReceipt> ImportBatch(AuthoringImportRequest request,
            string requestHash, IReadOnlyList<PreparedAuthoringImportSource> sources)
        {
            if (request == null || sources == null || sources.Count == 0 || sources.Count > MaximumSelectionCount || !IsSha256(requestHash))
                return Failure<AuthoringImportReceipt>(AuthoringStatuses.Invalid, "authoring_import_batch_invalid", "request", "A bounded prepared inventory and request hash are required.");

            lock (_gate)
            {
                if (_disposed) return Failure<AuthoringImportReceipt>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                bool transaction = false;
                try
                {
                    _connection.BeginTransaction();
                    transaction = true;
                    ReserveWriteLock(_connection);
                    long currentStoreRevision = GetStoreRevision(_connection);

                    ImportReceiptRow prior = ReadImportReceipt(request.operationId);
                    if (prior != null)
                    {
                        Rollback(_connection);
                        transaction = false;
                        if (prior.request_hash != requestHash)
                            return Failure<AuthoringImportReceipt>(AuthoringStatuses.Conflict, "authoring_operation_reused", request.operationId, "This import operation ID already records a different inventory.");
                        return ReplayImportReceipt(prior);
                    }

                    if (_storeId != request.expectedStoreId)
                        return RecordImportConflict(request, requestHash, currentStoreRevision, "authoring_import_store_conflict", "The expected authoring store identity no longer matches.");
                    if (currentStoreRevision != request.expectedStoreRevision)
                        return RecordImportConflict(request, requestHash, currentStoreRevision, "authoring_import_revision_conflict", "The authoring store changed after this explicit inventory was prepared.");

                    var documentIds = new HashSet<string>(StringComparer.Ordinal);
                    var pathKeys = new HashSet<string>(StringComparer.Ordinal);
                    var currentById = new Dictionary<string, DocumentRow>(StringComparer.Ordinal);
                    var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (PreparedAuthoringImportSource source in sources)
                    {
                        if (source == null || !IsIdentifier(source.documentId) || !AuthoringContentKinds.IsSupported(source.kind) ||
                            !IsLogicalPath(source.logicalPath) || !IsSha256(source.sourceHash) ||
                            (!string.IsNullOrEmpty(source.sourceId) && !IsIdentifier(source.sourceId)) || source.sourceBytes == null ||
                            source.sourceBytes.Length == 0 || source.sourceBytes.Length > MaximumDocumentBytes ||
                            Hash(source.sourceBytes) != source.sourceHash || !documentIds.Add(source.documentId) ||
                            !pathKeys.Add(source.logicalPath.ToUpperInvariant()))
                            return RecordImportConflict(request, requestHash, currentStoreRevision, "authoring_import_inventory_invalid", "The prepared import contains an invalid or duplicate stable identity/path.");

                        DocumentRow current = ReadDocumentRow(source.documentId);
                        DocumentRow pathOwner = ReadDocumentByPathKey(source.logicalPath.ToUpperInvariant());
                        if (pathOwner != null && pathOwner.document_id != source.documentId)
                            return RecordImportConflict(request, requestHash, currentStoreRevision, "authoring_import_path_conflict", "Another document ID already owns a selected logical path.");
                        if (current != null && current.kind != source.kind)
                            return RecordImportConflict(request, requestHash, currentStoreRevision, "authoring_import_identity_conflict", "An existing document ID cannot be remapped to another content kind.");
                        if (current != null && !string.IsNullOrEmpty(current.source_id) && !string.IsNullOrEmpty(source.sourceId) &&
                            current.source_id != source.sourceId && !AuthoringContentKinds.IsRawSourceKind(source.kind))
                            return RecordImportConflict(request, requestHash, currentStoreRevision, "authoring_import_source_identity_conflict", "An existing stable source ID cannot be replaced by a different content ID.");
                        currentById[source.documentId] = current;
                        sourceIds[source.documentId] = current != null && !string.IsNullOrEmpty(current.source_id)
                            ? current.source_id : source.sourceId;
                    }

                    bool changed = false;
                    string now = UtcNow();
                    var mappings = new List<AuthoringImportMapping>(sources.Count);
                    foreach (PreparedAuthoringImportSource source in sources)
                    {
                        DocumentRow current = currentById[source.documentId];
                        string sourceId = sourceIds[source.documentId];
                        string pathKey = source.logicalPath.ToUpperInvariant();
                        if (current == null)
                        {
                            _connection.Execute("INSERT INTO documents(document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc) VALUES(?,?,?,?,?,?,?,?)",
                                source.documentId, source.kind, sourceId, source.logicalPath, pathKey, source.sourceHash, now, now);
                            _connection.Execute("INSERT INTO document_revisions(document_id,revision_hash,document_bytes,created_utc) VALUES(?,?,?,?)",
                                source.documentId, source.sourceHash, source.sourceBytes, now);
                            changed = true;
                        }
                        else if (current.head_hash != source.sourceHash || current.logical_path != source.logicalPath || current.source_id != sourceId)
                        {
                            if (current.head_hash != source.sourceHash)
                                _connection.Execute("INSERT OR IGNORE INTO document_revisions(document_id,revision_hash,document_bytes,created_utc) VALUES(?,?,?,?)",
                                    source.documentId, source.sourceHash, source.sourceBytes, now);
                            _connection.Execute("UPDATE documents SET source_id=?,logical_path=?,logical_path_key=?,head_hash=?,updated_utc=? WHERE document_id=? AND head_hash=?",
                                sourceId, source.logicalPath, pathKey, source.sourceHash, now, source.documentId, current.head_hash);
                            changed = true;
                        }
                        mappings.Add(new AuthoringImportMapping { sourcePath = source.logicalPath, sourceHash = source.sourceHash,
                            documentId = source.documentId, sourceId = sourceId });
                    }

                    long resultStoreRevision = currentStoreRevision;
                    if (changed)
                    {
                        resultStoreRevision = checked(currentStoreRevision + 1);
                        AuthoringSchema.SetMetadata(_connection, "store_revision", resultStoreRevision.ToString(CultureInfo.InvariantCulture));
                    }
                    _connection.Execute("INSERT INTO import_receipts(operation_id,request_hash,result_status,source_count,store_revision,code,message,created_utc) VALUES(?,?,?,?,?,?,?,?)",
                        request.operationId, requestHash, AuthoringStatuses.Ready, sources.Count, resultStoreRevision, "", "", now);
                    foreach (PreparedAuthoringImportSource source in sources)
                        _connection.Execute("INSERT INTO import_sources(operation_id,source_path,source_hash,document_id,source_id,original_bytes) VALUES(?,?,?,?,?,?)",
                            request.operationId, source.logicalPath, source.sourceHash, source.documentId, sourceIds[source.documentId], source.sourceBytes);
                    _connection.Commit();
                    transaction = false;
                    return AuthoringResult<AuthoringImportReceipt>.Ready(CreateImportReceipt(request.operationId, requestHash,
                        resultStoreRevision, mappings));
                }
                catch (Exception exception)
                {
                    if (transaction) Rollback(_connection);
                    return MapFailure<AuthoringImportReceipt>(exception, request.operationId, "authoring_import_commit_failed");
                }
            }
        }

        public AuthoringResult<string> CreateBackup(string trustedDestinationPath)
        {
            string destination;
            try
            {
                if (string.IsNullOrWhiteSpace(trustedDestinationPath))
                    return Failure<string>(AuthoringStatuses.Invalid, "authoring_backup_path_required", "destinationPath", "A host-trusted backup destination is required.");
                destination = Path.GetFullPath(trustedDestinationPath);
            }
            catch (Exception exception) { return Failure<string>(AuthoringStatuses.Invalid, "authoring_backup_path_invalid", "destinationPath", exception.Message); }

            if (string.Equals(destination, _databasePath, StringComparison.OrdinalIgnoreCase) || File.Exists(destination) || Directory.Exists(destination))
                return Failure<string>(AuthoringStatuses.Conflict, "authoring_backup_destination_exists", destination, "A backup never overwrites the live database or an existing destination.");
            string parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return Failure<string>(AuthoringStatuses.Invalid, "authoring_backup_parent_missing", destination, "Create the trusted backup directory before requesting a backup.");

            string stagingPath = destination + ".writing-" + Guid.NewGuid().ToString("N");
            lock (_gate)
            {
                if (_disposed) return Failure<string>(AuthoringStatuses.Invalid, "authoring_repository_closed", "", "The authoring repository is closed.");
                try
                {
                    BackupUtf8Path(_connection, stagingPath);
                    AuthoringResult<AuthoringRepository> verified = OpenExisting(stagingPath);
                    if (!verified.IsReady)
                        return Failure<string>(verified.status, "authoring_backup_verification_failed", destination, "The SQLite backup could not be reopened and verified.");
                    verified.value.Dispose();
                    if (File.Exists(destination))
                        return Failure<string>(AuthoringStatuses.Conflict, "authoring_backup_destination_exists", destination, "Another process created the backup destination first.");
                    File.Move(stagingPath, destination);
                    return AuthoringResult<string>.Ready(destination);
                }
                catch (Exception exception) { return MapFailure<string>(exception, destination, "authoring_backup_failed"); }
                finally { DeleteStagingFiles(stagingPath); }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _connection.Dispose();
            }
        }

        private AuthoringResult<AuthoringDocument> ReplaySaveReceipt(OperationReceiptRow receipt)
        {
            if (receipt.result_status != AuthoringStatuses.Ready)
                return Failure<AuthoringDocument>(receipt.result_status, receipt.code, receipt.document_id, receipt.message);
            AuthoringDocument result;
            RevisionRow revision = ReadRevisionRow(receipt.document_id, receipt.revision_hash);
            if (!TryDecodeDocumentResult(receipt.result_payload, out result) || result.documentId != receipt.document_id ||
                result.revisionHash != receipt.revision_hash || revision == null || Hash(revision.document_bytes) != receipt.revision_hash)
                return Failure<AuthoringDocument>(AuthoringStatuses.RecoveryRequired, "authoring_receipt_target_missing", receipt.document_id, "The recorded operation target or immutable revision is missing.");
            result.documentBytes = (byte[])revision.document_bytes.Clone();
            result.createdUtc = revision.created_utc;
            return AuthoringResult<AuthoringDocument>.Ready(result);
        }

        private AuthoringResult<AuthoringBuildSet> ReplayBuildSetReceipt(OperationReceiptRow receipt)
        {
            if (receipt.result_status != AuthoringStatuses.Ready)
                return Failure<AuthoringBuildSet>(receipt.result_status, receipt.code, receipt.build_set_name, receipt.message);
            AuthoringBuildSet result;
            if (!TryDecodeBuildSetResult(receipt.result_payload, out result))
                return Failure<AuthoringBuildSet>(AuthoringStatuses.RecoveryRequired, "authoring_receipt_invalid", receipt.build_set_name, "The recorded build-set outcome cannot be recovered.");
            return AuthoringResult<AuthoringBuildSet>.Ready(result);
        }

        private DocumentRow ReadDocumentRow(string documentId)
        { return _connection.Query<DocumentRow>("SELECT document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc FROM documents WHERE document_id=?", documentId).FirstOrDefault(); }

        private DocumentRow ReadDocumentByPathKey(string pathKey)
        { return _connection.Query<DocumentRow>("SELECT document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc FROM documents WHERE logical_path_key=?", pathKey).FirstOrDefault(); }

        private RevisionRow ReadRevisionRow(string documentId, string revisionHash)
        { return _connection.Query<RevisionRow>("SELECT document_id,revision_hash,document_bytes,created_utc FROM document_revisions WHERE document_id=? AND revision_hash=?", documentId, revisionHash).FirstOrDefault(); }

        private OperationReceiptRow ReadOperationReceipt(string operationId)
        { return _connection.Query<OperationReceiptRow>("SELECT operation_id,operation_kind,request_hash,result_status,document_id,revision_hash,build_set_name,code,message,result_payload,store_revision FROM operation_receipts WHERE operation_id=?", operationId).FirstOrDefault(); }

        private ImportReceiptRow ReadImportReceipt(string operationId)
        { return _connection.Query<ImportReceiptRow>("SELECT operation_id,request_hash,result_status,source_count,store_revision,code,message FROM import_receipts WHERE operation_id=?", operationId).FirstOrDefault(); }

        private List<ImportSourceRow> ReadImportSourceRows(string operationId)
        { return _connection.Query<ImportSourceRow>("SELECT source_path,source_hash,document_id,source_id FROM import_sources WHERE operation_id=? ORDER BY source_path", operationId); }

        private BuildSetRow ReadBuildSetRow(string name)
        { return _connection.Query<BuildSetRow>("SELECT name,revision_hash,updated_utc FROM build_sets WHERE name=?", name).FirstOrDefault(); }

        private List<string> ReadBuildSetDocumentIds(string name)
        { return _connection.Query<BuildSetDocumentRow>("SELECT document_id FROM build_set_documents WHERE name=? ORDER BY ordinal", name).Select(row => row.document_id).ToList(); }

        private void InsertOperationReceipt(string operationId, string operationKind, string requestHash, string resultStatus,
            string documentId, string revisionHash, string buildSetName, string code, string message, string resultPayload, long storeRevision)
        {
            _connection.Execute("INSERT INTO operation_receipts(operation_id,operation_kind,request_hash,result_status,document_id,revision_hash,build_set_name,code,message,result_payload,store_revision,created_utc) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                operationId, operationKind, requestHash, resultStatus, documentId ?? "", revisionHash ?? "", buildSetName ?? "",
                code ?? "", message ?? "", resultPayload ?? "", storeRevision, UtcNow());
        }

        private AuthoringResult<AuthoringImportReceipt> RecordImportConflict(AuthoringImportRequest request, string requestHash,
            long storeRevision, string code, string message)
        {
            _connection.Execute("INSERT INTO import_receipts(operation_id,request_hash,result_status,source_count,store_revision,code,message,created_utc) VALUES(?,?,?,?,?,?,?,?)",
                request.operationId, requestHash, AuthoringStatuses.Conflict, 0, storeRevision, code, message, UtcNow());
            _connection.Commit();
            return Failure<AuthoringImportReceipt>(AuthoringStatuses.Conflict, code, request.operationId, message);
        }

        private AuthoringResult<AuthoringImportReceipt> ReplayImportReceipt(ImportReceiptRow row)
        {
            if (row.result_status != AuthoringStatuses.Ready)
                return Failure<AuthoringImportReceipt>(row.result_status, row.code, row.operation_id, row.message);
            List<AuthoringImportMapping> mappings = ReadImportSourceRows(row.operation_id).Select(ToImportMapping).ToList();
            if (mappings.Count != row.source_count)
                return Failure<AuthoringImportReceipt>(AuthoringStatuses.RecoveryRequired, "authoring_import_receipt_incomplete", row.operation_id, "The import receipt and its retained source mapping are incomplete.");
            return AuthoringResult<AuthoringImportReceipt>.Ready(CreateImportReceipt(row.operation_id, row.request_hash, row.store_revision, mappings));
        }

        private AuthoringImportReceipt CreateImportReceipt(string operationId, string requestHash, long storeRevision,
            List<AuthoringImportMapping> mappings)
        {
            var receipt = new AuthoringImportReceipt
            {
                operationId = operationId,
                storeId = _storeId,
                storeRevision = storeRevision,
                sourceCount = mappings.Count,
                sources = mappings.OrderBy(item => item.sourcePath, StringComparer.Ordinal).ToList()
            };
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, requestHash);
                WriteString(writer, _storeId);
                writer.Write(storeRevision);
                writer.Write(receipt.sources.Count);
                foreach (AuthoringImportMapping mapping in receipt.sources)
                {
                    WriteString(writer, mapping.sourcePath);
                    WriteString(writer, mapping.sourceHash);
                    WriteString(writer, mapping.documentId);
                    WriteString(writer, mapping.sourceId);
                }
                writer.Flush();
                receipt.receiptHash = Hash(stream.ToArray());
            }
            return receipt;
        }

        private static AuthoringImportMapping ToImportMapping(PreparedAuthoringImportSource source)
        { return new AuthoringImportMapping { sourcePath = source.logicalPath, sourceHash = source.sourceHash, documentId = source.documentId, sourceId = source.sourceId }; }

        private static AuthoringImportMapping ToImportMapping(ImportSourceRow source)
        { return new AuthoringImportMapping { sourcePath = source.source_path, sourceHash = source.source_hash, documentId = source.document_id, sourceId = source.source_id }; }

        private AuthoringResult<AuthoringDocument> RecordSaveConflict(AuthoringSaveRequest request, string requestHash,
            long storeRevision, string code, string message)
        {
            InsertOperationReceipt(request.operationId, "document.save", requestHash, AuthoringStatuses.Conflict,
                request.documentId, "", "", code, message, "", storeRevision);
            _connection.Commit();
            return Failure<AuthoringDocument>(AuthoringStatuses.Conflict, code, request.logicalPath, message);
        }

        private AuthoringResult<AuthoringBuildSet> RecordBuildSetConflict(AuthoringBuildSetRequest request, string requestHash,
            long storeRevision, string code, string message)
        {
            InsertOperationReceipt(request.operationId, "build_set.save", requestHash, AuthoringStatuses.Conflict,
                "", "", request.name, code, message, "", storeRevision);
            _connection.Commit();
            return Failure<AuthoringBuildSet>(AuthoringStatuses.Conflict, code, request.name, message);
        }

        private static void Configure(SQLiteConnection connection)
        {
            connection.BusyTimeout = TimeSpan.FromMilliseconds(BusyTimeoutMilliseconds);
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.Execute("PRAGMA synchronous=FULL");
        }

        private static void ReserveWriteLock(SQLiteConnection connection)
        { connection.Execute("UPDATE metadata SET value=value WHERE key='store_revision'"); }

        private static void BackupUtf8Path(SQLiteConnection source, string destinationPath)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(destinationPath);
            Array.Resize(ref pathBytes, pathBytes.Length + 1);
            IntPtr destinationHandle = IntPtr.Zero;
            try
            {
                SQLite3.Result opened = SQLite3.Open(pathBytes, out destinationHandle,
                    (int)(SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex), null);
                if (opened != SQLite3.Result.OK)
                    throw new InvalidDataException("The UTF-8 SQLite backup destination could not be opened: " + opened + ".");

                var backupHandle = SQLite3.BackupInit(destinationHandle, "main", source.Handle, "main");
                if (object.Equals(backupHandle, IntPtr.Zero))
                    throw new InvalidDataException("The SQLite online backup could not be initialized.");

                SQLite3.Result step = SQLite3.Result.Error;
                SQLite3.Result finished = SQLite3.Result.Error;
                try { step = SQLite3.BackupStep(backupHandle, -1); }
                finally { finished = SQLite3.BackupFinish(backupHandle); }
                if (step != SQLite3.Result.Done || finished != SQLite3.Result.OK || SQLite3.GetResult(destinationHandle) != SQLite3.Result.OK)
                    throw new InvalidDataException("The SQLite online backup did not finish cleanly: step=" + step + ", finish=" + finished + ".");
            }
            finally
            {
                if (destinationHandle != IntPtr.Zero) SQLite3.Close(destinationHandle);
            }
        }

        private static string Metadata(SQLiteConnection connection, string key)
        { return connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key=?", key); }

        private static long GetStoreRevision(SQLiteConnection connection)
        {
            long revision;
            if (!long.TryParse(Metadata(connection, "store_revision"), NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision < 0)
                throw new InvalidDataException("The authoring store revision metadata is invalid.");
            return revision;
        }

        private static void Rollback(SQLiteConnection connection)
        { if (connection != null && connection.IsInTransaction) connection.Rollback(); }

        private static AuthoringResult<AuthoringRepository> CloseAndFail(SQLiteConnection connection, string status,
            string code, string path, string message)
        {
            if (connection != null) connection.Dispose();
            return Failure<AuthoringRepository>(status, code, path, message);
        }

        private static AuthoringDocumentSummary ToSummary(DocumentSummaryRow row)
        {
            return new AuthoringDocumentSummary
            {
                documentId = row.document_id,
                kind = row.kind,
                sourceId = row.source_id,
                logicalPath = row.logical_path,
                revisionHash = row.revision_hash
            };
        }

        private static AuthoringDocument ToDocument(DocumentRow document, string revisionHash, RevisionRow revision)
        {
            return new AuthoringDocument
            {
                documentId = document.document_id,
                kind = document.kind,
                sourceId = document.source_id,
                logicalPath = document.logical_path,
                revisionHash = revisionHash,
                documentBytes = (byte[])revision.document_bytes.Clone(),
                createdUtc = revision.created_utc
            };
        }

        private static AuthoringBuildSet ToBuildSet(BuildSetRow row, List<string> documentIds)
        {
            return new AuthoringBuildSet
            {
                name = row.name,
                documentIds = documentIds,
                revisionHash = row.revision_hash,
                updatedUtc = row.updated_utc
            };
        }

        private string WriteCursor(string kind, int limit, string lastId)
        {
            string payload = _storeId + "\n" + kind + "\n" + limit.ToString(CultureInfo.InvariantCulture) + "\n" + lastId;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private bool TryReadCursor(string cursor, string kind, int limit, out string lastId)
        {
            lastId = string.Empty;
            if (cursor.Length > 2048) return false;
            try
            {
                string base64 = cursor.Replace('-', '+').Replace('_', '/');
                while (base64.Length % 4 != 0) base64 += "=";
                string[] fields = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64)).Split(new[] { '\n' }, 4);
                int parsedLimit;
                if (fields.Length != 4 || fields[0] != _storeId || fields[1] != kind ||
                    !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out parsedLimit) || parsedLimit != limit ||
                    !IsIdentifier(fields[3])) return false;
                lastId = fields[3];
                return true;
            }
            catch { return false; }
        }

        private static bool ValidateSaveRequest(AuthoringSaveRequest request, out string error)
        {
            error = string.Empty;
            if (request == null || !IsIdentifier(request.documentId) || !AuthoringContentKinds.IsSupported(request.kind) ||
                !IsLogicalPath(request.logicalPath) || !IsOperationId(request.operationId) || request.documentBytes == null ||
                request.documentBytes.Length == 0 || request.documentBytes.Length > MaximumDocumentBytes ||
                (!string.IsNullOrEmpty(request.expectedHash) && !IsSha256(request.expectedHash)))
            { error = "A supported kind, stable ID, safe logical path, bounded UTF-8 JSON, operation ID and empty or SHA-256 expected hash are required."; return false; }
            if (request.kind == "furniture_catalogue_settings" &&
                (request.documentId != "furniture.catalogue" || request.logicalPath != "Furniture/catalogue-settings.json"))
            { error = "Furniture catalogue settings use the fixed singleton ID and logical path."; return false; }
            return true;
        }

        private static string ResolveSavedSourceId(string kind, string documentId, byte[] bytes)
        {
            if (AuthoringContentKinds.IsRawSourceKind(kind)) return documentId;
            if (kind == "furniture_catalogue_settings") return "furniture.catalogue";
            string identityField = kind == "generic_dialogue" ? "id" :
                kind == "topic_definition" || kind == "topic" ? "topicId" :
                kind == "scenario_definition" || kind == "scenario" ? "scenarioId" : string.Empty;
            if (identityField.Length == 0) return string.Empty;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                JObject root = JObject.Parse(json);
                JObject content = kind == "topic" || kind == "scenario" ? root["document"] as JObject : root;
                JToken value;
                if (content == null || !content.TryGetValue(identityField, out value) || value.Type != JTokenType.String) return string.Empty;
                string sourceId = (string)value;
                return IsIdentifier(sourceId) ? sourceId : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static bool ValidateBuildSetRequest(AuthoringBuildSetRequest request, out string error)
        {
            error = string.Empty;
            if (request == null || !IsBuildSetName(request.name) || !IsOperationId(request.operationId) ||
                request.documentIds == null || request.documentIds.Count < 1 || request.documentIds.Count > MaximumSelectionCount ||
                (!string.IsNullOrEmpty(request.expectedHash) && !IsSha256(request.expectedHash)))
            { error = "A safe set name, 1..10000 ordered IDs, operation ID and empty or SHA-256 expected hash are required."; return false; }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in request.documentIds)
                if (!IsIdentifier(id) || !unique.Add(id))
                { error = "Build-set document IDs must be stable and unique."; return false; }
            return true;
        }

        internal static bool IsJsonDocument(byte[] bytes, out string error)
        {
            error = string.Empty;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumDocumentBytes)
            { error = "JSON documents must be 1..4 MiB."; return false; }
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes);
                if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
                if (ContainsComment(json)) { error = "JSON comments are not accepted."; return false; }
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.MaxDepth = 64;
                    if (!reader.Read()) { error = "The JSON document is empty."; return false; }
                    JToken.ReadFrom(reader);
                    if (reader.Read()) { error = "Trailing JSON content is not accepted."; return false; }
                }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static bool ContainsComment(string text)
        {
            bool inString = false;
            bool escape = false;
            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (current == '\\') escape = true;
                    else if (current == '"') inString = false;
                    continue;
                }
                if (current == '"') { inString = true; continue; }
                if (current == '/' && index + 1 < text.Length && (text[index + 1] == '/' || text[index + 1] == '*')) return true;
            }
            return false;
        }

        internal static bool IsLogicalPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || path[0] == '/' || path[path.Length - 1] == '/' || path.IndexOf('\\') >= 0 ||
                path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0 || path.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) return false;
            string[] segments = path.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)) return false;
                if (segment.Any(char.IsControl)) return false;
                string stem = segment.Split('.')[0];
                if (IsWindowsDeviceName(stem)) return false;
            }
            return true;
        }

        private static bool IsWindowsDeviceName(string stem)
        {
            string value = (stem ?? string.Empty).TrimEnd(' ', '.').ToUpperInvariant();
            if (value == "CON" || value == "PRN" || value == "AUX" || value == "NUL") return true;
            if (value.Length == 4 && (value.StartsWith("COM", StringComparison.Ordinal) || value.StartsWith("LPT", StringComparison.Ordinal)) && value[3] >= '1' && value[3] <= '9') return true;
            return false;
        }

        internal static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '_' && character != '-' && character != '.' && character != ':') return false;
            return true;
        }

        internal static bool IsOperationId(string value)
        { return IsIdentifier(value) && value.Length <= 128; }

        private static bool IsBuildSetName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '_' && character != '-' && character != '.') return false;
            return true;
        }

        internal static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static byte[] HashSaveRequest(AuthoringSaveRequest request)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, request.documentId);
                WriteString(writer, request.kind);
                WriteString(writer, request.logicalPath);
                WriteString(writer, request.expectedHash ?? string.Empty);
                writer.Write(request.documentBytes.Length);
                writer.Write(request.documentBytes);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static string HashBuildSetRequest(AuthoringBuildSetRequest request)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, request.name);
                WriteString(writer, request.expectedHash ?? string.Empty);
                writer.Write(request.documentIds.Count);
                foreach (string id in request.documentIds) WriteString(writer, id);
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        private static string HashBuildSet(string name, IReadOnlyList<string> documentIds)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, name);
                writer.Write(documentIds.Count);
                foreach (string id in documentIds) WriteString(writer, id);
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        private static string HashSnapshot(string storeId, IEnumerable<AuthoringDocument> documents)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, storeId);
                foreach (AuthoringDocument document in documents.OrderBy(item => item.documentId, StringComparer.Ordinal))
                {
                    WriteString(writer, document.documentId);
                    WriteString(writer, document.kind);
                    WriteString(writer, document.sourceId);
                    WriteString(writer, document.logicalPath);
                    WriteString(writer, document.revisionHash);
                }
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string EncodeBuildSetResult(AuthoringBuildSet set)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, set.name);
                WriteString(writer, set.revisionHash);
                WriteString(writer, set.updatedUtc);
                writer.Write(set.documentIds.Count);
                foreach (string id in set.documentIds) WriteString(writer, id);
                writer.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static string EncodeDocumentResult(AuthoringDocument document)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, document.documentId);
                WriteString(writer, document.kind);
                WriteString(writer, document.sourceId);
                WriteString(writer, document.logicalPath);
                WriteString(writer, document.revisionHash);
                WriteString(writer, document.createdUtc);
                writer.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static bool TryDecodeDocumentResult(string value, out AuthoringDocument document)
        {
            document = null;
            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    document = new AuthoringDocument
                    {
                        documentId = ReadString(reader),
                        kind = ReadString(reader),
                        sourceId = ReadString(reader),
                        logicalPath = ReadString(reader),
                        revisionHash = ReadString(reader),
                        createdUtc = ReadString(reader)
                    };
                    return stream.Position == stream.Length && IsIdentifier(document.documentId) &&
                        AuthoringContentKinds.IsSupported(document.kind) && IsLogicalPath(document.logicalPath) &&
                        IsSha256(document.revisionHash) && (string.IsNullOrEmpty(document.sourceId) || IsIdentifier(document.sourceId));
                }
            }
            catch { document = null; return false; }
        }

        private static bool TryDecodeBuildSetResult(string value, out AuthoringBuildSet set)
        {
            set = null;
            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    set = new AuthoringBuildSet { name = ReadString(reader), revisionHash = ReadString(reader), updatedUtc = ReadString(reader) };
                    int count = reader.ReadInt32();
                    if (count < 1 || count > MaximumSelectionCount) return false;
                    for (int index = 0; index < count; index++) set.documentIds.Add(ReadString(reader));
                    return stream.Position == stream.Length && IsBuildSetName(set.name) && IsSha256(set.revisionHash);
                }
            }
            catch { set = null; return false; }
        }

        private static string ReadString(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 65536) throw new InvalidDataException("A recorded authoring value exceeds its bound.");
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        private static IEnumerable<string[]> Batches(IReadOnlyList<string> values, int maximum)
        {
            for (int offset = 0; offset < values.Count; offset += maximum)
            {
                int count = Math.Min(maximum, values.Count - offset);
                var batch = new string[count];
                for (int index = 0; index < count; index++) batch[index] = values[offset + index];
                yield return batch;
            }
        }

        private static string UtcNow()
        { return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture); }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        public static bool ValidateSnapshot(AuthoringSnapshot snapshot, out string error)
        {
            error = string.Empty;
            Guid storeId;
            if (snapshot == null || !Guid.TryParse(snapshot.storeId, out storeId) ||
                storeId.ToString("D") != snapshot.storeId || snapshot.storeRevision < 0 ||
                snapshot.documents == null || snapshot.documents.Count < 1 || snapshot.documents.Count > MaximumSelectionCount ||
                !IsSha256(snapshot.snapshotHash))
            { error = "A canonical store identity, nonnegative revision, snapshot hash and bounded explicit document selection are required."; return false; }

            var documentIds = new HashSet<string>(StringComparer.Ordinal);
            long totalBytes = 0;
            foreach (AuthoringDocument document in snapshot.documents)
            {
                string jsonError = string.Empty;
                if (document == null || !IsIdentifier(document.documentId) || !documentIds.Add(document.documentId) ||
                    !AuthoringContentKinds.IsSupported(document.kind) || !IsLogicalPath(document.logicalPath) ||
                    (!string.IsNullOrEmpty(document.sourceId) && !IsIdentifier(document.sourceId)) ||
                    document.documentBytes == null || document.documentBytes.Length == 0 ||
                    document.documentBytes.Length > MaximumDocumentBytes || !IsSha256(document.revisionHash) ||
                    Hash(document.documentBytes) != document.revisionHash || !IsJsonDocument(document.documentBytes, out jsonError))
                { error = "A selected snapshot document has an invalid identity, path, revision or JSON payload."; return false; }
                totalBytes = checked(totalBytes + document.documentBytes.Length);
                if (totalBytes > MaximumSnapshotBytes)
                { error = "The selected snapshot exceeds the 64 MiB in-memory compilation bound."; return false; }
            }
            if (snapshot.totalBytes != totalBytes)
            { error = "The selected snapshot byte count does not match its exact source documents."; return false; }
            if (HashSnapshot(snapshot.storeId, snapshot.documents) != snapshot.snapshotHash)
            { error = "The selected snapshot hash does not match its stable identities and revision hashes."; return false; }
            return true;
        }

        private static void DeleteStagingFiles(string path)
        {
            foreach (string candidate in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
            {
                try { if (File.Exists(candidate)) File.Delete(candidate); }
                catch { }
            }
        }

        private static AuthoringResult<T> Failure<T>(string status, string code, string path, string message)
        { return AuthoringResult<T>.Failure(status, code, path, message); }

        private static AuthoringResult<T> MapFailure<T>(Exception exception, string path, string fallbackCode)
        {
            SQLiteException sqlite = exception as SQLiteException;
            if (sqlite != null)
            {
                string result = sqlite.Result.ToString();
                if (result == "Busy" || result == "Locked")
                    return Failure<T>(AuthoringStatuses.Busy, "authoring_database_busy", path, "The authoring database remained locked after the five-second wait.");
                string message = sqlite.Message ?? string.Empty;
                if (result == "Corrupt" || result == "NotADatabase" || result == "IOError" || result == "CantOpen" ||
                    message.IndexOf("not a database", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("database disk image is malformed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Failure<T>(AuthoringStatuses.RecoveryRequired, "authoring_database_unavailable", path, "The authoring database could not be read or written safely.");
            }
            return Failure<T>(AuthoringStatuses.Invalid, fallbackCode, path, exception == null ? "The authoring operation failed." : exception.Message);
        }

        private sealed class DocumentRow
        {
            public string document_id { get; set; }
            public string kind { get; set; }
            public string source_id { get; set; }
            public string logical_path { get; set; }
            public string logical_path_key { get; set; }
            public string head_hash { get; set; }
            public string created_utc { get; set; }
            public string updated_utc { get; set; }
        }

        private sealed class DocumentSummaryRow
        {
            public string document_id { get; set; }
            public string kind { get; set; }
            public string source_id { get; set; }
            public string logical_path { get; set; }
            public string revision_hash { get; set; }
        }

        private sealed class RevisionRow
        {
            public string document_id { get; set; }
            public string revision_hash { get; set; }
            public byte[] document_bytes { get; set; }
            public string created_utc { get; set; }
        }

        private sealed class OperationReceiptRow
        {
            public string operation_id { get; set; }
            public string operation_kind { get; set; }
            public string request_hash { get; set; }
            public string result_status { get; set; }
            public string document_id { get; set; }
            public string revision_hash { get; set; }
            public string build_set_name { get; set; }
            public string code { get; set; }
            public string message { get; set; }
            public string result_payload { get; set; }
            public long store_revision { get; set; }
        }

        private sealed class BuildSetRow
        {
            public string name { get; set; }
            public string revision_hash { get; set; }
            public string updated_utc { get; set; }
        }

        private sealed class ImportReceiptRow
        {
            public string operation_id { get; set; }
            public string request_hash { get; set; }
            public string result_status { get; set; }
            public int source_count { get; set; }
            public long store_revision { get; set; }
            public string code { get; set; }
            public string message { get; set; }
        }

        private sealed class ImportSourceRow
        {
            public string source_path { get; set; }
            public string source_hash { get; set; }
            public string document_id { get; set; }
            public string source_id { get; set; }
        }

        private sealed class BuildSetDocumentRow { public string document_id { get; set; } }
        private sealed class SnapshotHeadRow
        {
            public string document_id { get; set; }
            public string kind { get; set; }
            public string source_id { get; set; }
            public string logical_path { get; set; }
            public string revision_hash { get; set; }
            public int byte_length { get; set; }
        }
    }
}
