using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using SQLite;

namespace AlterOS.Content
{
    /// <summary>Owns one host-selected content database. It exposes snapshots/results, never raw connections.</summary>
    public sealed class ContentRepository
    {
        private const int MaximumPackageRecords = 10000;
        private const int MaximumJsonCharacters = 262144;
        private static readonly object WriterGate = new object();
        private readonly string databasePath;
        private readonly ContentStoreMode mode;
        private readonly int schemaVersion;

        public string DatabasePath { get { return databasePath; } }
        public string StoreId { get; private set; }
        public ContentStoreMode Mode { get { return mode; } }

        private ContentRepository(string path, ContentStoreMode storeMode, string storeId, int version)
        {
            databasePath = path;
            mode = storeMode;
            StoreId = storeId;
            schemaVersion = version;
        }

        public static ContentResult<ContentRepository> Open(string path, ContentStoreMode mode)
        { return Open(path, mode, null); }

        public static ContentResult<ContentRepository> Open(string path, ContentStoreMode mode, string requestedStoreId)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Failure<ContentRepository>(ContentStatuses.Invalid, "content_path_required", "", "A host-supplied content database path is required.");
            if (mode != ContentStoreMode.ReadOnly && mode != ContentStoreMode.ReadWrite)
                return Failure<ContentRepository>(ContentStatuses.Invalid, "content_mode_invalid", path, "The content store mode is unsupported.");
            Guid requestedGuid;
            if (!string.IsNullOrWhiteSpace(requestedStoreId) && !Guid.TryParse(requestedStoreId, out requestedGuid))
                return Failure<ContentRepository>(ContentStatuses.Invalid, "content_store_identity_invalid", path, "The requested content store identity must be a GUID.");

            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch (Exception exception)
            {
                return Failure<ContentRepository>(ContentStatuses.Invalid, "content_path_invalid", path, exception.Message);
            }

            lock (WriterGate)
            {
                if (!File.Exists(fullPath))
                {
                    if (mode == ContentStoreMode.ReadOnly)
                        return Failure<ContentRepository>(ContentStatuses.Missing, "content_store_missing", fullPath, "The requested read-only content store does not exist.");
                    return CreateNew(fullPath, mode, requestedStoreId);
                }

                if (mode == ContentStoreMode.ReadWrite)
                {
                    string migrationStatus;
                    string migrationCode;
                    string migrationMessage;
                    if (!UpgradeExisting(fullPath, out migrationStatus, out migrationCode, out migrationMessage))
                        return Failure<ContentRepository>(migrationStatus, migrationCode, fullPath, migrationMessage);
                }

                string storeId;
                string status;
                string code;
                string message;
                int schemaVersion;
                if (!InspectExisting(fullPath, mode, out storeId, out schemaVersion, out status, out code, out message))
                    return Failure<ContentRepository>(status, code, fullPath, message);
                if (!string.IsNullOrWhiteSpace(requestedStoreId) && !string.Equals(new Guid(storeId).ToString("D"),
                    new Guid(requestedStoreId).ToString("D"), StringComparison.OrdinalIgnoreCase))
                    return Failure<ContentRepository>(ContentStatuses.Incompatible, "content_store_identity_mismatch", fullPath,
                        "The existing content store identity does not match the requested manifest store.");
                return ContentResult<ContentRepository>.Ready(new ContentRepository(fullPath, mode, storeId, schemaVersion));
            }
        }

        public ContentResult<string> Stage(ContentPackage package)
        {
            if (mode != ContentStoreMode.ReadWrite)
                return Failure<string>(ContentStatuses.Invalid, "content_store_read_only", databasePath, "This content repository was opened read-only.");
            string validationError;
            if (!ValidatePackage(package, StoreId, out validationError))
                return Failure<string>(ContentStatuses.Invalid, "content_package_invalid", databasePath, validationError);
            if (package.buildManifest != null && !package.buildManifest.ValidateForPackage(package, out validationError))
                return Failure<string>(ContentStatuses.Invalid, "content_build_manifest_invalid", databasePath, validationError);

            lock (WriterGate)
            {
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(databasePath, ContentStoreMode.ReadWrite);
                    connection.BeginTransaction();
                    StagePackage(connection, package);
                    connection.Commit();
                    return ContentResult<string>.Ready(package.packageId);
                }
                catch (ContentConflictException exception)
                {
                    Rollback(connection);
                    return Failure<string>(ContentStatuses.Invalid, "content_revision_conflict", databasePath, exception.Message);
                }
                catch (SQLiteException exception)
                {
                    Rollback(connection);
                    return Failure<string>(ContentStatuses.Invalid, "content_constraint_failed", databasePath, exception.Message);
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return Failure<string>(ContentStatuses.Unavailable, "content_stage_failed", databasePath, exception.Message);
                }
                finally
                {
                    if (connection != null) connection.Close();
                }
            }
        }

        /// <summary>Stages one immutable vector only when its exact source Anchor and profile still match.</summary>
        public ContentResult<string> StageEmbedding(ContentRef anchorRef, EmbeddingProfileRecord profile, byte[] vector,
            string sourceHash)
        {
            if (mode != ContentStoreMode.ReadWrite)
                return Failure<string>(ContentStatuses.Invalid, "content_store_read_only", databasePath, "This content repository was opened read-only.");
            if (anchorRef == null || anchorRef.store != StoreId || !IsSha256(anchorRef.id) || anchorRef.revision != sourceHash ||
                anchorRef.expectedHash != sourceHash || !IsSha256(sourceHash))
                return Failure<string>(ContentStatuses.Invalid, "embedding_anchor_reference_invalid", "anchor", "The vector must identify the exact local Anchor source hash.");
            string profileCode, profileMessage;
            if (!ContentSemanticValidation.ValidateProfile(profile, out profileCode, out profileMessage))
                return Failure<string>(ContentStatuses.Invalid, profileCode, profile.id, profileMessage);
            string vectorCode, vectorMessage;
            if (!ContentSemanticValidation.ValidateVector(vector, profile, out vectorCode, out vectorMessage))
                return Failure<string>(ContentStatuses.Invalid, vectorCode, anchorRef.id, vectorMessage);

            lock (WriterGate)
            {
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(databasePath, ContentStoreMode.ReadWrite);
                    connection.BeginTransaction();
                    List<AnchorSourceRow> rows = connection.Query<AnchorSourceRow>(
                        "SELECT anchor_id,text_value,text_hash FROM anchors WHERE anchor_id=?", anchorRef.id);
                    if (rows.Count != 1 || rows[0].anchor_id != sourceHash || rows[0].text_hash != sourceHash ||
                        ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes(rows[0].text_value ?? string.Empty)) != sourceHash)
                    {
                        Rollback(connection);
                        return Failure<string>(ContentStatuses.Incompatible, "embedding_source_hash_mismatch", anchorRef.id,
                            "The exact Anchor source text changed or is not present in this content store.");
                    }
                    InsertEmbeddingProfile(connection, profile);
                    InsertEmbedding(connection, new EmbeddingRecord
                    { anchorId = anchorRef.id, profileId = profile.id, vector = (byte[])vector.Clone(), vectorHash = ContentSemanticValidation.Hash(vector) });
                    connection.Commit();
                    return ContentResult<string>.Ready(anchorRef.id);
                }
                catch (ContentConflictException exception)
                {
                    Rollback(connection);
                    return Failure<string>(ContentStatuses.Invalid, "embedding_immutable_conflict", anchorRef.id, exception.Message);
                }
                catch (SQLiteException exception)
                {
                    Rollback(connection);
                    return Failure<string>(ContentStatuses.Invalid, "embedding_stage_constraint_failed", anchorRef.id, exception.Message);
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return Failure<string>(ContentStatuses.Unavailable, "embedding_stage_failed", anchorRef.id, exception.Message);
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        /// <summary>Records host-supplied local Topic/embedding work with a stable idempotency key; it starts no provider worker.</summary>
        public ContentResult<ContentJobRecord> StageJob(string idempotencyKey, string kind, string sourceHash)
        {
            if (mode != ContentStoreMode.ReadWrite)
                return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_store_read_only", databasePath, "This content store was opened read-only.");
            if (!IsIdentifier(idempotencyKey) || (kind != "topic" && kind != "embedding") || !IsSha256(sourceHash))
                return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_job_request_invalid", idempotencyKey,
                    "A bounded idempotency key, supported local job kind and SHA-256 source hash are required.");
            string jobId = "job_" + ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes(idempotencyKey)).Substring(0, 48);
            lock (WriterGate)
            {
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(databasePath, ContentStoreMode.ReadWrite);
                    connection.BeginTransaction();
                    List<ContentJobRow> rows = connection.Query<ContentJobRow>(
                        "SELECT job_id,idempotency_key,kind,source_hash,state,error_code,attempts FROM jobs WHERE idempotency_key=?", idempotencyKey);
                    if (rows.Count > 0)
                    {
                        ContentJobRow existing = rows[0];
                        if (existing.kind != kind || existing.source_hash != sourceHash || existing.job_id != jobId)
                        {
                            Rollback(connection);
                            return Failure<ContentJobRecord>(ContentStatuses.Incompatible, "content_job_idempotency_conflict", idempotencyKey,
                                "A job idempotency key is already bound to different source data.");
                        }
                        connection.Commit();
                        return ContentResult<ContentJobRecord>.Ready(ToContentJob(existing));
                    }
                    connection.Execute("INSERT INTO jobs(job_id,idempotency_key,kind,source_hash,state,error_code,attempts) VALUES (?,?,?,?,?,?,?)",
                        jobId, idempotencyKey, kind, sourceHash, "staged", string.Empty, 0);
                    connection.Commit();
                    return ContentResult<ContentJobRecord>.Ready(new ContentJobRecord(jobId, idempotencyKey, kind, sourceHash, "staged", string.Empty, 0));
                }
                catch (SQLiteException exception)
                {
                    Rollback(connection);
                    return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_job_stage_constraint_failed", idempotencyKey, exception.Message);
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return Failure<ContentJobRecord>(ContentStatuses.Unavailable, "content_job_stage_failed", idempotencyKey, exception.Message);
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        /// <summary>Completes one staged local job idempotently; status does not publish content or imply playback readiness.</summary>
        public ContentResult<ContentJobRecord> CompleteJob(string idempotencyKey, bool succeeded, string errorCode)
        {
            if (mode != ContentStoreMode.ReadWrite)
                return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_store_read_only", databasePath, "This content store was opened read-only.");
            string terminalState = succeeded ? "validated" : "failed";
            string normalizedError = errorCode ?? string.Empty;
            if (!IsIdentifier(idempotencyKey) || normalizedError.Length > 64 ||
                (succeeded && normalizedError.Length != 0) || (!succeeded && !IsIdentifier(normalizedError)))
                return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_job_completion_invalid", idempotencyKey,
                    "A terminal job result needs a bounded idempotency key and an error code only when validation fails.");
            lock (WriterGate)
            {
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(databasePath, ContentStoreMode.ReadWrite);
                    connection.BeginTransaction();
                    List<ContentJobRow> rows = connection.Query<ContentJobRow>(
                        "SELECT job_id,idempotency_key,kind,source_hash,state,error_code,attempts FROM jobs WHERE idempotency_key=?", idempotencyKey);
                    if (rows.Count == 0)
                    {
                        Rollback(connection);
                        return Failure<ContentJobRecord>(ContentStatuses.Missing, "content_job_missing", idempotencyKey,
                            "The content job was not staged before completion.");
                    }
                    ContentJobRow current = rows[0];
                    if (current.state == terminalState && current.error_code == normalizedError)
                    {
                        connection.Commit();
                        return ContentResult<ContentJobRecord>.Ready(ToContentJob(current));
                    }
                    if (current.state != "staged" || current.attempts >= 32)
                    {
                        Rollback(connection);
                        return Failure<ContentJobRecord>(ContentStatuses.Incompatible, "content_job_state_conflict", idempotencyKey,
                            "Only a staged job below the retry bound can receive its first terminal result.");
                    }
                    int updated = connection.Execute("UPDATE jobs SET state=?,error_code=?,attempts=attempts+1 WHERE idempotency_key=? AND state='staged' AND attempts<32",
                        terminalState, normalizedError, idempotencyKey);
                    if (updated != 1)
                    {
                        Rollback(connection);
                        return Failure<ContentJobRecord>(ContentStatuses.Incompatible, "content_job_state_conflict", idempotencyKey,
                            "The job changed before its terminal result could be recorded.");
                    }
                    List<ContentJobRow> completed = connection.Query<ContentJobRow>(
                        "SELECT job_id,idempotency_key,kind,source_hash,state,error_code,attempts FROM jobs WHERE idempotency_key=?", idempotencyKey);
                    connection.Commit();
                    return ContentResult<ContentJobRecord>.Ready(ToContentJob(completed[0]));
                }
                catch (SQLiteException exception)
                {
                    Rollback(connection);
                    return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_job_complete_constraint_failed", idempotencyKey, exception.Message);
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return Failure<ContentJobRecord>(ContentStatuses.Unavailable, "content_job_complete_failed", idempotencyKey, exception.Message);
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        public ContentResult<ContentJobRecord> ReadJob(string idempotencyKey)
        {
            if (!IsIdentifier(idempotencyKey))
                return Failure<ContentJobRecord>(ContentStatuses.Invalid, "content_job_key_invalid", idempotencyKey, "A bounded job idempotency key is required.");
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<ContentJobRow> rows = connection.Query<ContentJobRow>(
                        "SELECT job_id,idempotency_key,kind,source_hash,state,error_code,attempts FROM jobs WHERE idempotency_key=?", idempotencyKey);
                    return rows.Count == 0
                        ? Failure<ContentJobRecord>(ContentStatuses.Missing, "content_job_missing", idempotencyKey, "The content job is not present.")
                        : ContentResult<ContentJobRecord>.Ready(ToContentJob(rows[0]));
                }
            }
            catch (Exception exception)
            { return Failure<ContentJobRecord>(ContentStatuses.Unavailable, "content_job_read_failed", idempotencyKey, exception.Message); }
        }

        private static ContentJobRecord ToContentJob(ContentJobRow row)
        { return new ContentJobRecord(row.job_id, row.idempotency_key, row.kind, row.source_hash, row.state, row.error_code, row.attempts); }

        /// <summary>Publishes a validated release head only after its exact semantic index snapshot has loaded.</summary>
        public ContentResult<long> Publish(ContentRelease release, string headName, long expectedHeadRevision,
            ContentSemanticSnapshot semanticSnapshot)
        {
            if (mode != ContentStoreMode.ReadWrite)
                return Failure<long>(ContentStatuses.Invalid, "content_store_read_only", databasePath, "This content repository was opened read-only.");
            if (release == null || !IsIdentifier(release.releaseId) || !IsBounded(headName, 64) || !IsIdentifier(headName) || expectedHeadRevision < 0)
                return Failure<long>(ContentStatuses.Invalid, "release_publish_request_invalid", headName, "A release, bounded head name and nonnegative expected revision are required.");
            if (semanticSnapshot == null)
                return Failure<long>(ContentStatuses.Unavailable, "release_index_not_ready", release.releaseId,
                    "The exact compatible semantic index snapshot has not been loaded for this release.");
            if (semanticSnapshot.ReleaseId != release.releaseId || semanticSnapshot.IndexFingerprint != release.indexFingerprint)
                return Failure<long>(ContentStatuses.Incompatible, "release_index_snapshot_mismatch", release.releaseId,
                    "The loaded semantic snapshot belongs to a different release or index fingerprint.");
            if (release.validatorVersion != ContentPackageValidator.ValidatorVersion)
                return Failure<long>(ContentStatuses.Incompatible, "release_not_validated", release.releaseId,
                    "Only releases compiled with the current host package validator can become a published head.");
            if (!ContentReleaseCompiler.HasValidatedCompilerAttestation(release))
                return Failure<long>(ContentStatuses.Incompatible, "release_validation_attestation_missing", release.releaseId,
                    "The release was not produced by the host validation/compiler path in this process.");

            ContentResult<ContentRelease> storedResult = ReadRelease(release.releaseId);
            if (!storedResult.IsReady)
            {
                ContentDiagnostic diagnostic = storedResult.diagnostics.FirstOrDefault();
                return Failure<long>(storedResult.status, diagnostic == null ? "release_missing" : diagnostic.code,
                    release.releaseId, diagnostic == null ? "The exact staged release is unavailable." : diagnostic.message);
            }
            ContentRelease storedRelease = storedResult.value;
            if (storedRelease.manifestHash != release.manifestHash || storedRelease.graphFingerprint != release.graphFingerprint ||
                storedRelease.indexFingerprint != release.indexFingerprint || storedRelease.validatorVersion != release.validatorVersion ||
                storedRelease.dependencies.Any(item => item.store != StoreId))
                return Failure<long>(ContentStatuses.Incompatible, "release_publish_mismatch", release.releaseId,
                    "The requested release differs from the staged, locally resolved validated closure.");

            lock (WriterGate)
            {
                SQLiteConnection connection = null;
                try
                {
                    connection = OpenConnection(databasePath, ContentStoreMode.ReadWrite);
                    connection.BeginTransaction();
                    List<ReleaseHeadRow> heads = connection.Query<ReleaseHeadRow>(
                        "SELECT head_name,release_id,head_revision FROM release_heads WHERE head_name=?", headName);
                    long nextRevision;
                    if (heads.Count == 0)
                    {
                        if (expectedHeadRevision != 0)
                        {
                            Rollback(connection);
                            return Failure<long>(ContentStatuses.Incompatible, "release_head_revision_conflict", headName,
                                "The expected initial head revision is stale because no matching head exists.");
                        }
                        nextRevision = 1;
                        connection.Execute("INSERT INTO release_heads(head_name,release_id,head_revision) VALUES (?,?,?)",
                            headName, release.releaseId, nextRevision);
                    }
                    else
                    {
                        long currentRevision = heads[0].head_revision;
                        if (currentRevision != expectedHeadRevision)
                        {
                            Rollback(connection);
                            return Failure<long>(ContentStatuses.Incompatible, "release_head_revision_conflict", headName,
                                "The published head changed after the caller read it; reload before retrying.");
                        }
                        nextRevision = checked(currentRevision + 1);
                        int updated = connection.Execute(
                            "UPDATE release_heads SET release_id=?,head_revision=? WHERE head_name=? AND head_revision=?",
                            release.releaseId, nextRevision, headName, expectedHeadRevision);
                        if (updated != 1)
                        {
                            Rollback(connection);
                            return Failure<long>(ContentStatuses.Incompatible, "release_head_revision_conflict", headName,
                                "Another writer changed the published head during compare-and-swap.");
                        }
                    }
                    connection.Commit();
                    return ContentResult<long>.Ready(nextRevision);
                }
                catch (SQLiteException exception)
                {
                    Rollback(connection);
                    return Failure<long>(ContentStatuses.Unavailable, "release_publish_failed", headName, exception.Message);
                }
                catch (Exception exception)
                {
                    Rollback(connection);
                    return Failure<long>(ContentStatuses.Unavailable, "release_publish_failed", headName, exception.Message);
                }
                finally { if (connection != null) connection.Close(); }
            }
        }

        public ContentResult<ReleaseHead> ReadReleaseHead(string headName)
        {
            if (!IsBounded(headName, 64) || !IsIdentifier(headName))
                return Failure<ReleaseHead>(ContentStatuses.Invalid, "release_head_name_invalid", headName,
                    "A bounded release head name is required.");
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<ReleaseHeadRow> rows = connection.Query<ReleaseHeadRow>(
                        "SELECT head_name,release_id,head_revision FROM release_heads WHERE head_name=?", headName);
                    if (rows.Count == 0)
                        return Failure<ReleaseHead>(ContentStatuses.Missing, "release_head_missing", headName,
                            "No release is published under this head name.");
                    return ContentResult<ReleaseHead>.Ready(new ReleaseHead
                    { headName = rows[0].head_name, releaseId = rows[0].release_id, revision = rows[0].head_revision });
                }
            }
            catch (Exception exception)
            {
                return Failure<ReleaseHead>(ContentStatuses.Unavailable, "release_head_read_failed", headName, exception.Message);
            }
        }

        public ContentResult<ContentRelease> ReadRelease(string releaseId)
        {
            if (!IsIdentifier(releaseId))
                return Failure<ContentRelease>(ContentStatuses.Invalid, "release_id_invalid", releaseId, "A bounded release ID is required.");
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<ReleaseRow> rows = connection.Query<ReleaseRow>(
                        "SELECT release_id,manifest_json,manifest_hash,validator_version,graph_fingerprint,index_fingerprint,row_hash FROM releases WHERE release_id=?",
                        releaseId);
                    if (rows.Count == 0)
                        return Failure<ContentRelease>(ContentStatuses.Missing, "release_missing", releaseId, "The exact requested release is not present.");
                    ReleaseRow row = rows[0];
                    if (ComputeHash(Encoding.UTF8.GetBytes(row.manifest_json)) != row.manifest_hash ||
                        HashFields(row.release_id, row.manifest_json, row.manifest_hash, row.validator_version,
                            row.graph_fingerprint, row.index_fingerprint) != row.row_hash)
                        return Failure<ContentRelease>(ContentStatuses.Incompatible, "release_manifest_hash_mismatch", releaseId, "The stored release manifest hash does not match its bytes.");
                    var release = new ContentRelease
                    {
                        releaseId = row.release_id,
                        manifestJson = row.manifest_json,
                        manifestHash = row.manifest_hash,
                        validatorVersion = row.validator_version,
                        graphFingerprint = row.graph_fingerprint,
                        indexFingerprint = row.index_fingerprint
                    };
                    foreach (ReleaseDependencyRow dependency in connection.Query<ReleaseDependencyRow>(
                        "SELECT kind,dependency_id,revision,store_id,expected_hash,row_hash FROM release_dependencies WHERE release_id=? ORDER BY kind,dependency_id,revision,store_id",
                        row.release_id))
                    {
                        if (HashFields(dependency.kind, dependency.dependency_id, dependency.revision,
                            dependency.store_id, dependency.expected_hash) != dependency.row_hash)
                            return Failure<ContentRelease>(ContentStatuses.Incompatible, "release_dependency_hash_mismatch", releaseId,
                                "A stored immutable release dependency does not match its row hash.");
                        release.dependencies.Add(new ContentReleaseDependency
                        {
                            kind = dependency.kind, id = dependency.dependency_id, revision = dependency.revision,
                            store = dependency.store_id, expectedHash = dependency.expected_hash
                        });
                    }
                    if (ComputeReleaseGraphFingerprint(release.dependencies) != release.graphFingerprint ||
                        !ValidateReleaseHeader(row.manifest_json, release, StoreId))
                        return Failure<ContentRelease>(ContentStatuses.Incompatible, "release_dependency_set_mismatch", releaseId,
                            "The stored release header or dependency set does not match its graph fingerprint.");
                    return ContentResult<ContentRelease>.Ready(release);
                }
            }
            catch (Exception exception)
            {
                return Failure<ContentRelease>(ContentStatuses.Unavailable, "release_read_failed", releaseId, exception.Message);
            }
        }

        /// <summary>Hydrates one exact compiled SQL release closure; retained source bytes are not consulted.</summary>
        internal ContentResult<ContentPackage> ReadCompiledPackageForRelease(ContentRelease requestedRelease,
            string packageId, string sourceManifestHash)
        {
            if (requestedRelease == null || string.IsNullOrWhiteSpace(requestedRelease.releaseId) ||
                !IsIdentifier(packageId) || !IsSha256(sourceManifestHash))
                return Failure<ContentPackage>(ContentStatuses.Invalid, "compiled_package_request_invalid", "release",
                    "An exact stored release, package ID and source-manifest hash are required.");

            ContentResult<ContentRelease> storedResult = ReadRelease(requestedRelease.releaseId);
            if (!storedResult.IsReady) return ForwardFailure<ContentRelease, ContentPackage>(storedResult, "compiled_release_unavailable");
            ContentRelease release = storedResult.value;
            if (!SameRelease(requestedRelease, release))
                return Failure<ContentPackage>(ContentStatuses.Incompatible, "compiled_release_reference_mismatch", release.releaseId,
                    "The requested release object differs from the exact immutable SQL release row.");
            if (release.dependencies == null || release.dependencies.Count == 0 || release.dependencies.Count > MaximumPackageRecords)
                return Failure<ContentPackage>(ContentStatuses.Incompatible, "compiled_release_dependencies_invalid", release.releaseId,
                    "The selected SQL release has no bounded dependency closure.");

            var package = new ContentPackage
            {
                packageId = packageId,
                storeId = StoreId,
                sourceManifestHash = sourceManifestHash
            };
            var dependencyKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentReleaseDependency dependency in release.dependencies)
            {
                if (dependency == null || dependency.store != StoreId || string.IsNullOrWhiteSpace(dependency.id) ||
                    !IsSha256(dependency.revision) || !IsSha256(dependency.expectedHash) ||
                    !dependencyKeys.Add(dependency.kind + "\0" + dependency.id + "\0" + dependency.revision))
                    return Failure<ContentPackage>(ContentStatuses.Incompatible, "compiled_release_dependency_invalid", release.releaseId,
                        "Every compiled dependency must be unique, local, and pinned to an exact hash.");

                var reference = new ContentRef(dependency.store, dependency.id, dependency.revision, dependency.expectedHash);
                switch (dependency.kind)
                {
                    case "topic":
                    {
                        ContentResult<TopicRecord> result = ReadTopic(reference);
                        if (!result.IsReady) return ForwardFailure<TopicRecord, ContentPackage>(result, "compiled_topic_unavailable");
                        package.topics.Add(result.value); break;
                    }
                    case "flow":
                    {
                        ContentResult<FlowRecord> result = ReadFlow(reference);
                        if (!result.IsReady) return ForwardFailure<FlowRecord, ContentPackage>(result, "compiled_flow_unavailable");
                        package.flows.Add(result.value); break;
                    }
                    case "dialogue":
                    {
                        ContentResult<DialogueRecord> result = ReadDialogue(reference);
                        if (!result.IsReady) return ForwardFailure<DialogueRecord, ContentPackage>(result, "compiled_dialogue_unavailable");
                        package.dialogues.Add(result.value); break;
                    }
                    case "text":
                    {
                        ContentResult<TextRecord> result = ReadText(reference);
                        if (!result.IsReady) return ForwardFailure<TextRecord, ContentPackage>(result, "compiled_text_unavailable");
                        package.texts.Add(result.value); break;
                    }
                    case "action":
                    {
                        ContentResult<ActionRecord> result = ReadAction(reference);
                        if (!result.IsReady) return ForwardFailure<ActionRecord, ContentPackage>(result, "compiled_action_unavailable");
                        package.actions.Add(result.value); break;
                    }
                    case "item":
                    {
                        ContentResult<ItemRecord> result = ReadItem(reference);
                        if (!result.IsReady) return ForwardFailure<ItemRecord, ContentPackage>(result, "compiled_item_unavailable");
                        package.items.Add(result.value); break;
                    }
                    case "furniture":
                    {
                        ContentResult<FurnitureDefinitionRecord> result = ReadFurnitureDefinition(reference);
                        if (!result.IsReady) return ForwardFailure<FurnitureDefinitionRecord, ContentPackage>(result, "compiled_furniture_unavailable");
                        package.furnitureDefinitions.Add(result.value); break;
                    }
                    case "rule":
                    {
                        ContentResult<RuleRecord> result = ReadRule(reference);
                        if (!result.IsReady) return ForwardFailure<RuleRecord, ContentPackage>(result, "compiled_rule_unavailable");
                        package.rules.Add(result.value); break;
                    }
                    case "anchor": case "anchor_link": case "embedding_profile": case "embedding":
                        break;
                    default:
                        return Failure<ContentPackage>(ContentStatuses.Incompatible, "compiled_release_dependency_kind_unsupported",
                            dependency.kind, "The selected release contains an unsupported compiled dependency kind.");
                }
            }

            ContentResult<ContentSemanticStoreData> semantic = ReadSemanticData(release);
            if (!semantic.IsReady) return ForwardFailure<ContentSemanticStoreData, ContentPackage>(semantic, "compiled_semantic_closure_unavailable");
            package.anchors = semantic.value.anchors;
            package.anchorLinks = semantic.value.links;
            package.embeddingProfiles = semantic.value.profiles;
            package.embeddings = semantic.value.embeddings;
            package.releases.Add(release);
            return ContentResult<ContentPackage>.Ready(package);
        }

        private static bool SameRelease(ContentRelease left, ContentRelease right)
        {
            if (left == null || right == null || left.releaseId != right.releaseId || left.manifestHash != right.manifestHash ||
                left.validatorVersion != right.validatorVersion || left.graphFingerprint != right.graphFingerprint ||
                left.indexFingerprint != right.indexFingerprint || left.dependencies == null || right.dependencies == null ||
                left.dependencies.Count != right.dependencies.Count) return false;
            string[] leftKeys = left.dependencies.Where(item => item != null)
                .Select(ReleaseDependencyKey).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            string[] rightKeys = right.dependencies.Where(item => item != null)
                .Select(ReleaseDependencyKey).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            return leftKeys.Length == left.dependencies.Count && rightKeys.Length == right.dependencies.Count &&
                leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
        }

        private static string ReleaseDependencyKey(ContentReleaseDependency item)
        { return string.Join("\0", item.kind ?? "", item.store ?? "", item.id ?? "", item.revision ?? "", item.expectedHash ?? ""); }

        private static ContentResult<TOut> ForwardFailure<TIn, TOut>(ContentResult<TIn> source, string fallbackCode)
        {
            ContentDiagnostic diagnostic = source == null || source.diagnostics == null ? null : source.diagnostics.FirstOrDefault();
            return ContentResult<TOut>.Failure(source == null ? ContentStatuses.Unavailable : source.status,
                diagnostic == null ? fallbackCode : diagnostic.code,
                diagnostic == null ? "compiled_release" : diagnostic.path,
                diagnostic == null ? "The exact compiled release dependency is unavailable." : diagnostic.message);
        }

        public ContentResult<ContentBuildManifest> ReadBuildManifest()
        {
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    string json = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key=?", "build_manifest_json");
                    string expectedHash = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key=?", "build_manifest_hash");
                    if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(expectedHash))
                        return Failure<ContentBuildManifest>(ContentStatuses.Missing, "content_build_manifest_missing", databasePath,
                            "The content database does not contain a complete build manifest.");
                    ContentBuildManifest manifest;
                    string error;
                    if (!ContentBuildManifest.TryParse(json, out manifest, out error) || manifest.Hash != expectedHash ||
                        !manifest.Validate(out error) || manifest.storeId != StoreId)
                        return Failure<ContentBuildManifest>(ContentStatuses.Incompatible, "content_build_manifest_invalid", databasePath,
                            string.IsNullOrEmpty(error) ? "The build manifest hash or store identity is invalid." : error);
                    return ContentResult<ContentBuildManifest>.Ready(manifest);
                }
            }
            catch (Exception exception)
            { return Failure<ContentBuildManifest>(ContentStatuses.Unavailable, "content_build_manifest_read_failed", databasePath, exception.Message); }
        }

        public ContentResult<List<LegacySourceRecord>> ReadLegacySources()
        {
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<LegacySourceRow> rows = connection.Query<LegacySourceRow>(
                        "SELECT source_kind,source_path,source_id,source_hash,payload_hash,content_id,content_revision,metadata_json,source_bytes,row_hash FROM legacy_sources ORDER BY source_kind,source_path");
                    var sources = new List<LegacySourceRecord>(rows.Count);
                    foreach (LegacySourceRow row in rows)
                    {
                        byte[] bytes = row.source_bytes ?? new byte[0];
                        string payloadHash = bytes.Length == 0 ? "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" : ComputeHash(bytes);
                        string rowHash = HashFields(row.source_kind, row.source_path, row.source_id, row.source_hash,
                            row.payload_hash, row.content_id, row.content_revision, row.metadata_json);
                        if (rowHash != row.row_hash || bytes.Length == 0 || payloadHash != row.payload_hash)
                            return Failure<List<LegacySourceRecord>>(ContentStatuses.Incompatible, "content_source_payload_invalid", row.source_path,
                                "A legacy source payload is absent or differs from its immutable payload hash.");
                        sources.Add(new LegacySourceRecord
                        {
                            kind = row.source_kind, relativePath = row.source_path, sourceId = row.source_id,
                            sourceHash = row.source_hash, payloadHash = row.payload_hash, contentId = row.content_id, contentRevision = row.content_revision,
                            metadataJson = row.metadata_json, sourceBytes = (byte[])bytes.Clone()
                        });
                    }
                    return ContentResult<List<LegacySourceRecord>>.Ready(sources);
                }
            }
            catch (Exception exception)
            { return Failure<List<LegacySourceRecord>>(ContentStatuses.Unavailable, "content_source_read_failed", databasePath, exception.Message); }
        }

        internal ContentResult<ContentSemanticStoreData> ReadSemanticData(ContentRelease requestedRelease)
        {
            if (requestedRelease == null || !IsIdentifier(requestedRelease.releaseId))
                return Failure<ContentSemanticStoreData>(ContentStatuses.Invalid, "semantic_release_invalid", "", "A pinned release is required.");
            ContentResult<ContentRelease> storedResult = ReadRelease(requestedRelease.releaseId);
            if (!storedResult.IsReady)
            {
                ContentDiagnostic diagnostic = storedResult.diagnostics.FirstOrDefault();
                return Failure<ContentSemanticStoreData>(storedResult.status, diagnostic == null ? "semantic_release_missing" : diagnostic.code,
                    requestedRelease.releaseId, diagnostic == null ? "The exact release is unavailable." : diagnostic.message);
            }
            ContentRelease storedRelease = storedResult.value;
            if (storedRelease.manifestHash != requestedRelease.manifestHash || storedRelease.graphFingerprint != requestedRelease.graphFingerprint ||
                storedRelease.indexFingerprint != requestedRelease.indexFingerprint || storedRelease.validatorVersion != requestedRelease.validatorVersion ||
                !SameDependencies(storedRelease.dependencies, requestedRelease.dependencies))
                return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_release_mismatch", requestedRelease.releaseId,
                    "The requested semantic snapshot does not match the exact stored release closure.");

            try
            {
                var data = new ContentSemanticStoreData();
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<ContentReleaseDependency> anchorDependencies = storedRelease.dependencies.Where(item => item.kind == "anchor").ToList();
                    if (anchorDependencies.Any(item => item.store != StoreId || item.id != item.revision || item.expectedHash != item.revision))
                        return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_reference_invalid", requestedRelease.releaseId,
                            "Semantic Anchors must be exact local content-addressed source revisions.");
                    var anchorRows = new Dictionary<string, AnchorRow>(StringComparer.Ordinal);
                    foreach (ContentReleaseDependency[] batch in Chunks(anchorDependencies, 160))
                    {
                        List<AnchorRow> rows = connection.Query<AnchorRow>("SELECT anchor_id,language,text_value,text_hash FROM anchors WHERE anchor_id IN (" +
                            InClause(batch.Length) + ")", batch.Select(item => (object)item.id).ToArray());
                        foreach (AnchorRow row in rows) anchorRows[row.anchor_id] = row;
                    }
                    foreach (ContentReleaseDependency dependency in anchorDependencies)
                    {
                        AnchorRow row;
                        if (!anchorRows.TryGetValue(dependency.id, out row) || row.anchor_id != row.text_hash || row.text_hash != dependency.expectedHash ||
                            ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes(row.text_value ?? string.Empty)) != row.text_hash)
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_hash_mismatch", dependency.id,
                                "A stored Anchor row differs from the exact release source hash.");
                        data.anchors.Add(new AnchorRecord { id = row.anchor_id, language = row.language, text = row.text_value, textHash = row.text_hash });
                    }

                    List<ContentReleaseDependency> linkDependencies = storedRelease.dependencies.Where(item => item.kind == "anchor_link").ToList();
                    if (linkDependencies.Any(item => item.store != StoreId))
                        return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_link_store_invalid", requestedRelease.releaseId,
                            "Anchor links must be stored in the release's local content store.");
                    var linkRows = new Dictionary<string, AnchorLinkRow>(StringComparer.Ordinal);
                    foreach (ContentReleaseDependency[] batch in Chunks(linkDependencies, 160))
                    {
                        List<AnchorLinkRow> rows = connection.Query<AnchorLinkRow>(
                            "SELECT link_id,anchor_id,owner_kind,owner_topic_id,owner_topic_revision,owner_flow_id,owner_flow_revision,owner_dialogue_id,owner_dialogue_revision,canonical_owner_id,canonical_revision,role,row_hash FROM anchor_links WHERE link_id IN (" +
                            InClause(batch.Length) + ")", batch.Select(item => (object)item.id).ToArray());
                        foreach (AnchorLinkRow row in rows) linkRows[row.link_id] = row;
                    }
                    foreach (ContentReleaseDependency dependency in linkDependencies)
                    {
                        AnchorLinkRow row;
                        if (!linkRows.TryGetValue(dependency.id, out row))
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_link_store_invalid", dependency.id,
                                "An exact released Anchor link is missing.");
                        string ownerId = row.owner_kind == "topic" ? row.owner_topic_id : row.owner_kind == "flow" ? row.owner_flow_id :
                            row.owner_kind == "dialogue" ? row.owner_dialogue_id : row.canonical_owner_id;
                        string ownerRevision = row.owner_kind == "topic" ? row.owner_topic_revision : row.owner_kind == "flow" ? row.owner_flow_revision :
                            row.owner_kind == "dialogue" ? row.owner_dialogue_revision : row.canonical_revision;
                        string rowHash = HashFields(row.anchor_id, row.owner_kind, ownerId, ownerRevision, row.role);
                        if (rowHash != row.row_hash || dependency.revision != rowHash || dependency.expectedHash != rowHash)
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_link_hash_mismatch", dependency.id,
                                "An Anchor link differs from its immutable release hash.");
                        data.links.Add(new AnchorLinkRecord { id = row.link_id, anchorId = row.anchor_id,
                            ownerKind = row.owner_kind, ownerId = ownerId, ownerRevision = ownerRevision, role = row.role });
                    }

                    List<ContentReleaseDependency> profileDependencies = storedRelease.dependencies.Where(item => item.kind == "embedding_profile").ToList();
                    if (profileDependencies.Any(item => item.store != StoreId))
                        return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_profile_store_invalid", requestedRelease.releaseId,
                            "Embedding profiles must be stored in the release's local content store.");
                    var profileRows = new Dictionary<string, EmbeddingProfileRow>(StringComparer.Ordinal);
                    foreach (ContentReleaseDependency[] batch in Chunks(profileDependencies, 160))
                    {
                        List<EmbeddingProfileRow> rows = connection.Query<EmbeddingProfileRow>(
                            "SELECT profile_id,model_id,model_revision,tokenizer_hash,pipeline_hash,dimensions,dtype,normalization,metric,profile_hash FROM embedding_profiles WHERE profile_id IN (" +
                            InClause(batch.Length) + ")", batch.Select(item => (object)item.id).ToArray());
                        foreach (EmbeddingProfileRow row in rows) profileRows[row.profile_id] = row;
                    }
                    foreach (ContentReleaseDependency dependency in profileDependencies)
                    {
                        EmbeddingProfileRow row;
                        if (!profileRows.TryGetValue(dependency.id, out row))
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_profile_missing", dependency.id,
                                "An exact released Embedding profile is missing.");
                        var profile = new EmbeddingProfileRecord { id = row.profile_id, modelId = row.model_id, modelRevision = row.model_revision,
                            tokenizerHash = row.tokenizer_hash, pipelineHash = row.pipeline_hash, dimensions = row.dimensions,
                            dtype = row.dtype, normalization = row.normalization, metric = row.metric };
                        string profileCode, profileMessage;
                        string profileHash = ContentSemanticValidation.ProfileFingerprint(profile);
                        if (!ContentSemanticValidation.ValidateProfile(profile, out profileCode, out profileMessage) || row.profile_hash != profileHash ||
                            dependency.revision != profileHash || dependency.expectedHash != profileHash)
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_profile_hash_mismatch", dependency.id,
                                "An Embedding profile differs from its immutable release fingerprint.");
                        data.profiles.Add(profile);
                    }

                    List<ContentReleaseDependency> embeddingDependencies = storedRelease.dependencies.Where(item => item.kind == "embedding").ToList();
                    var parsedEmbeddings = new List<Tuple<ContentReleaseDependency, string, string>>();
                    foreach (ContentReleaseDependency dependency in embeddingDependencies)
                    {
                        if (dependency.store != StoreId || string.IsNullOrEmpty(dependency.id) || dependency.id.Length <= 65 || dependency.id[64] != '.')
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_embedding_reference_invalid", dependency.id,
                                "An embedding reference must contain a fixed Anchor hash and exact profile ID.");
                        string anchorId = dependency.id.Substring(0, 64);
                        string profileId = dependency.id.Substring(65);
                        parsedEmbeddings.Add(Tuple.Create(dependency, anchorId, profileId));
                    }
                    var embeddingRows = new Dictionary<string, EmbeddingRow>(StringComparer.Ordinal);
                    foreach (IGrouping<string, Tuple<ContentReleaseDependency, string, string>> profileGroup in parsedEmbeddings.GroupBy(item => item.Item3, StringComparer.Ordinal))
                    {
                        foreach (Tuple<ContentReleaseDependency, string, string>[] batch in Chunks(profileGroup.ToList(), 160))
                        {
                            string sql = "SELECT anchor_id,profile_id,vector,vector_hash,vector_bytes FROM embeddings WHERE profile_id=? AND anchor_id IN (" +
                                InClause(batch.Length) + ")";
                            object[] arguments = new object[batch.Length + 1];
                            arguments[0] = profileGroup.Key;
                            for (int index = 0; index < batch.Length; index++) arguments[index + 1] = batch[index].Item2;
                            foreach (EmbeddingRow row in connection.Query<EmbeddingRow>(sql, arguments))
                                embeddingRows[row.anchor_id + "\0" + row.profile_id] = row;
                        }
                    }
                    foreach (Tuple<ContentReleaseDependency, string, string> requested in parsedEmbeddings)
                    {
                        ContentReleaseDependency dependency = requested.Item1;
                        EmbeddingRow row;
                        if (!embeddingRows.TryGetValue(requested.Item2 + "\0" + requested.Item3, out row))
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_embedding_missing", dependency.id,
                                "An exact released Anchor/profile vector is missing.");
                        if (row.vector == null || row.vector_bytes != row.vector.Length || ContentSemanticValidation.Hash(row.vector) != row.vector_hash ||
                            dependency.revision != row.vector_hash || dependency.expectedHash != row.vector_hash)
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_vector_hash_mismatch", dependency.id,
                                "Stored vector bytes differ from the exact release fingerprint.");
                        data.embeddings.Add(new EmbeddingRecord { anchorId = row.anchor_id, profileId = row.profile_id,
                            vector = row.vector, vectorHash = row.vector_hash });
                    }
                }

                var anchorIds = new HashSet<string>(data.anchors.Select(item => item.id), StringComparer.Ordinal);
                if (data.links.Any(item => !anchorIds.Contains(item.anchorId)))
                    return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_link_target_missing", requestedRelease.releaseId,
                        "A released Anchor link points outside the exact Anchor set.");
                var releaseKeys = new HashSet<string>(storedRelease.dependencies.Select(item => item.kind + "\0" + item.id + "\0" +
                    item.revision + "\0" + item.store), StringComparer.Ordinal);
                foreach (AnchorLinkRecord link in data.links)
                {
                    if (link.ownerKind == "topic" || link.ownerKind == "flow" || link.ownerKind == "dialogue")
                    {
                        string ownerKey = link.ownerKind + "\0" + link.ownerId + "\0" + link.ownerRevision + "\0" + StoreId;
                        if (!releaseKeys.Contains(ownerKey))
                            return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_anchor_owner_missing", link.id,
                                "A released Anchor link owner is outside the pinned dependency closure.");
                    }
                }
                var profileById = data.profiles.ToDictionary(item => item.id, item => item, StringComparer.Ordinal);
                foreach (EmbeddingRecord embedding in data.embeddings)
                {
                    EmbeddingProfileRecord profile;
                    if (!anchorIds.Contains(embedding.anchorId) || !profileById.TryGetValue(embedding.profileId, out profile))
                        return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, "semantic_embedding_dependency_missing", embedding.anchorId,
                            "A vector points outside its exact Anchor or profile dependencies.");
                    string code, message;
                    if (!ContentSemanticValidation.ValidateVector(embedding.vector, profile, out code, out message))
                        return Failure<ContentSemanticStoreData>(ContentStatuses.Incompatible, code, embedding.anchorId, message);
                }
                return ContentResult<ContentSemanticStoreData>.Ready(data);
            }
            catch (Exception exception)
            { return Failure<ContentSemanticStoreData>(ContentStatuses.Unavailable, "semantic_sql_read_failed", requestedRelease.releaseId, exception.Message); }
        }

        private static bool SameDependencies(IEnumerable<ContentReleaseDependency> left, IEnumerable<ContentReleaseDependency> right)
        {
            string[] leftKeys = (left ?? Enumerable.Empty<ContentReleaseDependency>()).Select(item => item.kind + "\0" + item.id + "\0" +
                item.revision + "\0" + item.store + "\0" + item.expectedHash).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            string[] rightKeys = (right ?? Enumerable.Empty<ContentReleaseDependency>()).Select(item => item.kind + "\0" + item.id + "\0" +
                item.revision + "\0" + item.store + "\0" + item.expectedHash).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
        }

        private static IEnumerable<T[]> Chunks<T>(IList<T> items, int maximum)
        {
            for (int offset = 0; offset < items.Count; offset += maximum)
            {
                int count = Math.Min(maximum, items.Count - offset);
                var batch = new T[count];
                for (int index = 0; index < count; index++) batch[index] = items[offset + index];
                yield return batch;
            }
        }

        private static string InClause(int count)
        { return string.Join(",", Enumerable.Repeat("?", count).ToArray()); }

        public ContentResult<DialogueRecord> ReadDialogue(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<DialogueRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    DialogueRow row = connection.Query<DialogueRow>(
                        "SELECT dialogue_id,revision,semantic_hash,row_hash FROM dialogues WHERE dialogue_id=? AND revision=?",
                        reference.id, reference.revision).FirstOrDefault();
                    if (row == null) return Failure<DialogueRecord>(ContentStatuses.Missing, "dialogue_revision_missing", reference.id,
                        "The exact Dialogue revision is not present.");
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[] { row.dialogue_id, row.revision, row.semantic_hash })))
                        return CompiledRowHashFailure<DialogueRecord>("dialogue", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<DialogueRecord>(ContentStatuses.Incompatible, "dialogue_hash_mismatch", reference.id,
                            "The Dialogue semantic hash does not match the supplied reference.");
                    var dialogue = new DialogueRecord { id = row.dialogue_id, revision = row.revision, semanticHash = row.semantic_hash };
                    foreach (BeatRow beat in connection.Query<BeatRow>(
                        "SELECT beat_id,sequence_order,text_id,text_revision,expression,presentation_json,row_hash FROM beats WHERE dialogue_id=? AND dialogue_revision=? ORDER BY sequence_order",
                        row.dialogue_id, row.revision))
                    {
                        string beatHash = HashFields(beat.beat_id, beat.sequence_order.ToString(CultureInfo.InvariantCulture),
                            beat.text_id, beat.text_revision, beat.expression, beat.presentation_json);
                        if (!RowHashMatches(beat.row_hash, beatHash)) return CompiledRowHashFailure<DialogueRecord>("beat", reference.id + "/" + beat.beat_id);
                        dialogue.beats.Add(new BeatRecord { id = beat.beat_id, sequenceOrder = beat.sequence_order,
                            textRef = new ContentRef(StoreId, beat.text_id, beat.text_revision), expression = beat.expression,
                            presentationJson = beat.presentation_json });
                    }
                    return ContentResult<DialogueRecord>.Ready(dialogue);
                }
            }
            catch (Exception exception) { return Failure<DialogueRecord>(ContentStatuses.Unavailable, "dialogue_read_failed", reference.id, exception.Message); }
        }

        public ContentResult<ActionRecord> ReadAction(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<ActionRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    string sql = schemaVersion >= 5
                        ? "SELECT action_id,revision,semantic_hash,capability_id,contract_json,implementation_kind,composer_id,function_id,arguments_json,row_hash FROM actions WHERE action_id=? AND revision=?"
                        : "SELECT action_id,revision,semantic_hash,capability_id,contract_json,'legacy_activity' AS implementation_kind,NULL AS composer_id,NULL AS function_id,NULL AS arguments_json,row_hash FROM actions WHERE action_id=? AND revision=?";
                    ActionRow row = connection.Query<ActionRow>(sql, reference.id, reference.revision).FirstOrDefault();
                    if (row == null) return Failure<ActionRecord>(ContentStatuses.Missing, "action_revision_missing", reference.id,
                        "The exact Action revision is not present.");
                    bool legacyAction = schemaVersion < 5 || row.implementation_kind == "legacy_activity";
                    string actionHash = legacyAction
                        ? HashObjectFields(new object[] { row.action_id, row.revision, row.semantic_hash, row.capability_id, row.contract_json })
                        : HashFields(row.action_id, row.revision, row.semantic_hash, row.capability_id, row.contract_json,
                            row.implementation_kind, row.composer_id ?? string.Empty, row.function_id ?? string.Empty, row.arguments_json ?? string.Empty);
                    if (!RowHashMatches(row.row_hash, actionHash)) return CompiledRowHashFailure<ActionRecord>("action", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<ActionRecord>(ContentStatuses.Incompatible, "action_hash_mismatch", reference.id,
                            "The Action semantic hash does not match the supplied reference.");
                    var action = new ActionRecord { id = row.action_id, revision = row.revision, semanticHash = row.semantic_hash,
                        capabilityId = row.capability_id, contractJson = row.contract_json,
                        implementationKind = row.implementation_kind ?? "legacy_activity", composerId = row.composer_id ?? string.Empty,
                        functionId = row.function_id ?? string.Empty, argumentsJson = row.arguments_json ?? string.Empty };
                    if (schemaVersion >= 5)
                        foreach (ActionSequenceStepRow step in connection.Query<ActionSequenceStepRow>(
                            "SELECT ordinal,step_id,primitive_id,arguments_json,outputs_json,success_edge_json,failure_edge_json,cancel_edge_json,checkpoint_policy,row_hash FROM action_sequence_steps WHERE action_id=? AND action_revision=? ORDER BY ordinal",
                            row.action_id, row.revision))
                        {
                            string sequenceStepHash = HashFields(step.ordinal.ToString(CultureInfo.InvariantCulture), step.step_id,
                                step.primitive_id, step.arguments_json, step.outputs_json, step.success_edge_json,
                                step.failure_edge_json, step.cancel_edge_json, step.checkpoint_policy);
                            if (!RowHashMatches(step.row_hash, sequenceStepHash))
                                return CompiledRowHashFailure<ActionRecord>("action_sequence_step", reference.id + "/" + step.step_id);
                            action.sequenceSteps.Add(new ActionSequenceStepRecord { ordinal = step.ordinal, stepId = step.step_id,
                                primitiveId = step.primitive_id, argumentsJson = step.arguments_json, outputsJson = step.outputs_json,
                                successEdgeJson = step.success_edge_json, failureEdgeJson = step.failure_edge_json,
                                cancelEdgeJson = step.cancel_edge_json, checkpointPolicy = step.checkpoint_policy });
                        }
                    return ContentResult<ActionRecord>.Ready(action);
                }
            }
            catch (Exception exception) { return Failure<ActionRecord>(ContentStatuses.Unavailable, "action_read_failed", reference.id, exception.Message); }
        }

        public ContentResult<RuleRecord> ReadRule(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<RuleRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    RuleRow row = connection.Query<RuleRow>(
                        "SELECT rule_id,revision,semantic_hash,expression_json,row_hash FROM rules WHERE rule_id=? AND revision=?",
                        reference.id, reference.revision).FirstOrDefault();
                    if (row == null) return Failure<RuleRecord>(ContentStatuses.Missing, "rule_revision_missing", reference.id,
                        "The exact Rule revision is not present.");
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[] { row.rule_id, row.revision, row.semantic_hash, row.expression_json })))
                        return CompiledRowHashFailure<RuleRecord>("rule", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<RuleRecord>(ContentStatuses.Incompatible, "rule_hash_mismatch", reference.id,
                            "The Rule semantic hash does not match the supplied reference.");
                    return ContentResult<RuleRecord>.Ready(new RuleRecord
                    { id = row.rule_id, revision = row.revision, semanticHash = row.semantic_hash, expressionJson = row.expression_json });
                }
            }
            catch (Exception exception) { return Failure<RuleRecord>(ContentStatuses.Unavailable, "rule_read_failed", reference.id, exception.Message); }
        }

        public ContentResult<ItemRecord> ReadItem(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<ItemRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            if (schemaVersion < 5) return Failure<ItemRecord>(ContentStatuses.Missing, "item_revision_missing", reference.id,
                "This v4 store predates typed Item definitions.");
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    ItemRow row = connection.Query<ItemRow>(
                        "SELECT item_id,revision,semantic_hash,display_text_id,display_text_revision,stack_limit,row_hash FROM items WHERE item_id=? AND revision=?",
                        reference.id, reference.revision).FirstOrDefault();
                    if (row == null) return Failure<ItemRecord>(ContentStatuses.Missing, "item_revision_missing", reference.id,
                        "The exact Item revision is not present.");
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[] { row.item_id, row.revision, row.semantic_hash,
                        row.display_text_id, row.display_text_revision, row.stack_limit })))
                        return CompiledRowHashFailure<ItemRecord>("item", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<ItemRecord>(ContentStatuses.Incompatible, "item_hash_mismatch", reference.id,
                            "The Item semantic hash does not match the supplied reference.");
                    return ContentResult<ItemRecord>.Ready(new ItemRecord { id = row.item_id, revision = row.revision,
                        semanticHash = row.semantic_hash, displayTextRef = new ContentRef(StoreId, row.display_text_id, row.display_text_revision),
                        stackLimit = row.stack_limit });
                }
            }
            catch (Exception exception) { return Failure<ItemRecord>(ContentStatuses.Unavailable, "item_read_failed", reference.id, exception.Message); }
        }

        public ContentResult<FurnitureDefinitionRecord> ReadFurnitureDefinition(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<FurnitureDefinitionRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            if (schemaVersion < 5) return Failure<FurnitureDefinitionRecord>(ContentStatuses.Missing, "furniture_revision_missing", reference.id,
                "This v4 store predates typed Furniture definitions.");
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    FurnitureRow row = connection.Query<FurnitureRow>(
                        "SELECT furniture_id,revision,semantic_hash,display_text_id,display_text_revision,definition_json,row_hash FROM furniture_definitions WHERE furniture_id=? AND revision=?",
                        reference.id, reference.revision).FirstOrDefault();
                    if (row == null) return Failure<FurnitureDefinitionRecord>(ContentStatuses.Missing, "furniture_revision_missing", reference.id,
                        "The exact Furniture definition revision is not present.");
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[] { row.furniture_id, row.revision, row.semantic_hash,
                        row.display_text_id, row.display_text_revision, row.definition_json })))
                        return CompiledRowHashFailure<FurnitureDefinitionRecord>("furniture", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<FurnitureDefinitionRecord>(ContentStatuses.Incompatible, "furniture_hash_mismatch", reference.id,
                            "The Furniture semantic hash does not match the supplied reference.");
                    var furniture = new FurnitureDefinitionRecord { id = row.furniture_id, revision = row.revision,
                        semanticHash = row.semantic_hash, displayTextRef = new ContentRef(StoreId, row.display_text_id, row.display_text_revision),
                        definitionJson = row.definition_json };
                    string bindingSql = schemaVersion >= 6
                        ? "SELECT function_id,point_id,use_id,function_contract_version,contract_json,row_hash FROM furniture_function_bindings WHERE furniture_id=? AND furniture_revision=? ORDER BY function_id,point_id,use_id"
                        : "SELECT function_id,'' AS point_id,'' AS use_id,function_contract_version,contract_json,row_hash FROM furniture_function_bindings WHERE furniture_id=? AND furniture_revision=? ORDER BY function_id";
                    foreach (FurnitureBindingRow binding in connection.Query<FurnitureBindingRow>(
                        bindingSql, row.furniture_id, row.revision))
                    {
                        string version = binding.function_contract_version.ToString(CultureInfo.InvariantCulture);
                        string bindingHash = HashFields(binding.function_id, binding.point_id, binding.use_id, version, binding.contract_json);
                        bool legacyMigratedHash = string.IsNullOrEmpty(binding.point_id) && string.IsNullOrEmpty(binding.use_id) &&
                            RowHashMatches(binding.row_hash, HashFields(binding.function_id, version, binding.contract_json));
                        if (!RowHashMatches(binding.row_hash, bindingHash) && !legacyMigratedHash)
                            return CompiledRowHashFailure<FurnitureDefinitionRecord>("furniture_function_binding", reference.id + "/" + binding.function_id);
                        furniture.functionBindings.Add(new FurnitureFunctionBindingRecord { functionId = binding.function_id,
                            pointId = binding.point_id, useId = binding.use_id,
                            functionContractVersion = binding.function_contract_version, contractJson = binding.contract_json });
                    }
                    return ContentResult<FurnitureDefinitionRecord>.Ready(furniture);
                }
            }
            catch (Exception exception) { return Failure<FurnitureDefinitionRecord>(ContentStatuses.Unavailable, "furniture_read_failed", reference.id, exception.Message); }
        }

        public ContentResult<TextRecord> ReadText(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<TextRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<TextRow> rows = connection.Query<TextRow>(
                        "SELECT text_id,revision,semantic_hash,row_hash FROM texts WHERE text_id=? AND revision=?",
                        reference.id, reference.revision);
                    if (rows.Count == 0)
                        return Failure<TextRecord>(ContentStatuses.Missing, "text_revision_missing", reference.id, "The exact Text revision is not present.");
                    TextRow row = rows[0];
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[] { row.text_id, row.revision, row.semantic_hash })))
                        return CompiledRowHashFailure<TextRecord>("text", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<TextRecord>(ContentStatuses.Incompatible, "text_hash_mismatch", reference.id, "The Text semantic hash does not match the supplied reference.");
                    var text = new TextRecord { id = row.text_id, revision = row.revision, semanticHash = row.semantic_hash };
                    foreach (TranslationRow translation in connection.Query<TranslationRow>(
                        "SELECT language,text_value,row_hash FROM translations WHERE text_id=? AND text_revision=? ORDER BY language",
                        row.text_id, row.revision))
                    {
                        if (!RowHashMatches(translation.row_hash, HashFields(translation.language, translation.text_value)))
                            return CompiledRowHashFailure<TextRecord>("translation", reference.id + "/" + translation.language);
                        text.translations.Add(new TranslationRecord { language = translation.language, text = translation.text_value });
                    }
                    return ContentResult<TextRecord>.Ready(text);
                }
            }
            catch (Exception exception)
            {
                return Failure<TextRecord>(ContentStatuses.Unavailable, "text_read_failed", reference.id, exception.Message);
            }
        }

        public ContentResult<TopicRecord> ReadTopic(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<TopicRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<TopicRow> rows = connection.Query<TopicRow>(
                        "SELECT topic_id,revision,semantic_hash,route_id,title_text_id,title_text_revision,prompt_text_id,prompt_text_revision,replay_policy,cooldown_seconds,row_hash FROM topics WHERE topic_id=? AND revision=?",
                        reference.id, reference.revision);
                    if (rows.Count == 0)
                        return Failure<TopicRecord>(ContentStatuses.Missing, "topic_revision_missing", reference.id, "The exact Topic revision is not present.");
                    TopicRow row = rows[0];
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[] { row.topic_id, row.revision, row.semantic_hash,
                        row.route_id, row.title_text_id, row.title_text_revision, row.prompt_text_id, row.prompt_text_revision,
                        row.replay_policy, row.cooldown_seconds })))
                        return CompiledRowHashFailure<TopicRecord>("topic", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<TopicRecord>(ContentStatuses.Incompatible, "topic_hash_mismatch", reference.id, "The Topic semantic hash does not match the supplied reference.");
                    var topic = new TopicRecord
                    {
                        id = row.topic_id, revision = row.revision, semanticHash = row.semantic_hash,
                        routeId = row.route_id, titleTextRef = new ContentRef(StoreId, row.title_text_id, row.title_text_revision),
                        promptTextRef = new ContentRef(StoreId, row.prompt_text_id, row.prompt_text_revision),
                        defaultReplayPolicy = row.replay_policy, cooldownSeconds = row.cooldown_seconds
                    };
                    foreach (TopicEntryRow entry in connection.Query<TopicEntryRow>(
                        "SELECT entry_id,flow_id,flow_revision,priority,base_weight,condition_json,modifiers_json,channels_json,consumption_node_id,row_hash FROM topic_entries WHERE topic_id=? AND topic_revision=? ORDER BY entry_id",
                        row.topic_id, row.revision))
                    {
                        string entryHash = HashFields(entry.entry_id, entry.flow_id, entry.flow_revision,
                            entry.priority.ToString(CultureInfo.InvariantCulture), entry.base_weight.ToString("R", CultureInfo.InvariantCulture),
                            entry.condition_json, entry.modifiers_json, entry.channels_json, entry.consumption_node_id);
                        if (!RowHashMatches(entry.row_hash, entryHash))
                            return CompiledRowHashFailure<TopicRecord>("topic_entry", reference.id + "/" + entry.entry_id);
                        topic.entries.Add(new TopicEntryRecord
                        {
                            id = entry.entry_id, flowRef = new ContentRef(StoreId, entry.flow_id, entry.flow_revision),
                            priority = entry.priority, baseWeight = entry.base_weight,
                            conditionJson = entry.condition_json, modifiersJson = entry.modifiers_json,
                            channelsJson = entry.channels_json,
                            consumptionNodeId = entry.consumption_node_id
                        });
                    }
                    return ContentResult<TopicRecord>.Ready(topic);
                }
            }
            catch (Exception exception)
            {
                return Failure<TopicRecord>(ContentStatuses.Unavailable, "topic_read_failed", reference.id, exception.Message);
            }
        }

        public ContentResult<FlowRecord> ReadFlow(ContentRef reference)
        {
            if (!ValidateLocalReference(reference, StoreId, out string error))
                return Failure<FlowRecord>(ContentStatuses.Invalid, "content_ref_invalid", databasePath, error);
            try
            {
                using (SQLiteConnection connection = OpenConnection(databasePath, mode))
                {
                    List<FlowRow> rows = connection.Query<FlowRow>(
                        "SELECT flow_id,revision,semantic_hash,kind,entry_step_id,row_hash FROM flows WHERE flow_id=? AND revision=?",
                        reference.id, reference.revision);
                    if (rows.Count == 0)
                        return Failure<FlowRecord>(ContentStatuses.Missing, "flow_revision_missing", reference.id, "The exact Flow revision is not present.");
                    FlowRow row = rows[0];
                    if (!RowHashMatches(row.row_hash, HashObjectFields(new object[]
                        { row.flow_id, row.revision, row.semantic_hash, row.kind, row.entry_step_id })))
                        return CompiledRowHashFailure<FlowRecord>("flow", reference.id);
                    if (!string.IsNullOrEmpty(reference.expectedHash) && reference.expectedHash != row.semantic_hash)
                        return Failure<FlowRecord>(ContentStatuses.Incompatible, "flow_hash_mismatch", reference.id, "The Flow semantic hash does not match the supplied reference.");
                    var flow = new FlowRecord
                    {
                        id = row.flow_id, revision = row.revision, semanticHash = row.semantic_hash,
                        kind = row.kind, entryStepId = row.entry_step_id
                    };
                    foreach (StepRow step in connection.Query<StepRow>(
                        "SELECT step_id,kind,dialogue_id,dialogue_revision,action_id,action_revision,called_flow_id,called_flow_revision,data_json,row_hash FROM steps WHERE flow_id=? AND flow_revision=? ORDER BY step_id",
                        row.flow_id, row.revision))
                    {
                        string stepHash = HashFields(step.step_id, step.kind, step.dialogue_id, step.dialogue_revision,
                            step.action_id, step.action_revision, step.called_flow_id, step.called_flow_revision, step.data_json);
                        if (!RowHashMatches(step.row_hash, stepHash))
                            return CompiledRowHashFailure<FlowRecord>("step", reference.id + "/" + step.step_id);
                        flow.steps.Add(new StepRecord
                        {
                            id = step.step_id, kind = step.kind,
                            dialogueRef = MakeRef(step.dialogue_id, step.dialogue_revision),
                            actionRef = MakeRef(step.action_id, step.action_revision),
                            calledFlowRef = MakeRef(step.called_flow_id, step.called_flow_revision),
                            dataJson = step.data_json
                        });
                    }
                    foreach (TransitionRow transition in connection.Query<TransitionRow>(
                        "SELECT transition_id,from_step_id,to_step_id,purpose,rule_id,rule_revision,row_hash FROM transitions WHERE flow_id=? AND flow_revision=? ORDER BY transition_id",
                        row.flow_id, row.revision))
                    {
                        string transitionHash = HashFields(transition.transition_id, transition.from_step_id, transition.to_step_id,
                            transition.purpose, transition.rule_id, transition.rule_revision);
                        if (!RowHashMatches(transition.row_hash, transitionHash))
                            return CompiledRowHashFailure<FlowRecord>("transition", reference.id + "/" + transition.transition_id);
                        flow.transitions.Add(new TransitionRecord
                        {
                            id = transition.transition_id, fromStepId = transition.from_step_id,
                            toStepId = transition.to_step_id, purpose = transition.purpose,
                            ruleRef = MakeRef(transition.rule_id, transition.rule_revision)
                        });
                    }
                    foreach (ChoiceRow choice in connection.Query<ChoiceRow>(
                        "SELECT step_id,choice_id,transition_id,value_json,label_text_id,label_text_revision,availability_rule_id,availability_rule_revision,hint_text_id,hint_text_revision,row_hash FROM choices WHERE flow_id=? AND flow_revision=? ORDER BY step_id,choice_id",
                        row.flow_id, row.revision))
                    {
                        string choiceHash = HashFields(choice.step_id, choice.choice_id, choice.transition_id, choice.value_json,
                            choice.label_text_id, choice.label_text_revision, choice.availability_rule_id, choice.availability_rule_revision,
                            choice.hint_text_id, choice.hint_text_revision);
                        if (!RowHashMatches(choice.row_hash, choiceHash))
                            return CompiledRowHashFailure<FlowRecord>("choice", reference.id + "/" + choice.step_id + "/" + choice.choice_id);
                        flow.choices.Add(new ChoiceRecord
                        {
                            stepId = choice.step_id, id = choice.choice_id, transitionId = choice.transition_id,
                            valueJson = choice.value_json,
                            labelTextRef = MakeRef(choice.label_text_id, choice.label_text_revision),
                            availabilityRuleRef = MakeRef(choice.availability_rule_id, choice.availability_rule_revision),
                            lockedHintTextRef = MakeRef(choice.hint_text_id, choice.hint_text_revision)
                        });
                    }
                    string contractError;
                    if (!ReadFlowContracts(connection, flow, out contractError))
                        return Failure<FlowRecord>(ContentStatuses.Incompatible, "compiled_row_hash_mismatch", reference.id, contractError);
                    return ContentResult<FlowRecord>.Ready(flow);
                }
            }
            catch (Exception exception)
            {
                return Failure<FlowRecord>(ContentStatuses.Unavailable, "flow_read_failed", reference.id, exception.Message);
            }
        }

        private static void StagePackage(SQLiteConnection connection, ContentPackage package)
        {
            foreach (TextRecord item in package.texts)
            {
                InsertRevision(connection, "texts", "text_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO texts(text_id,revision,semantic_hash,row_hash) VALUES (?,?,?,?)", item.id, item.revision, item.semanticHash);
                foreach (TranslationRecord translation in item.translations)
                {
                    string rowHash = HashFields(translation.language, translation.text);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM translations WHERE text_id=? AND text_revision=? AND language=?",
                        new object[] { item.id, item.revision, translation.language }, rowHash,
                        "INSERT INTO translations(text_id,text_revision,language,text_value,row_hash) VALUES (?,?,?,?,?)",
                        item.id, item.revision, translation.language, translation.text, rowHash);
                }
            }

            foreach (DialogueRecord item in package.dialogues)
                InsertRevision(connection, "dialogues", "dialogue_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO dialogues(dialogue_id,revision,semantic_hash,row_hash) VALUES (?,?,?,?)", item.id, item.revision, item.semanticHash);
            foreach (DialogueRecord item in package.dialogues)
            {
                foreach (BeatRecord beat in item.beats)
                {
                    string rowHash = HashFields(beat.id, beat.sequenceOrder.ToString(CultureInfo.InvariantCulture),
                        beat.textRef.id, beat.textRef.revision, beat.expression, beat.presentationJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM beats WHERE dialogue_id=? AND dialogue_revision=? AND beat_id=?",
                        new object[] { item.id, item.revision, beat.id }, rowHash,
                        "INSERT INTO beats(dialogue_id,dialogue_revision,beat_id,sequence_order,text_id,text_revision,expression,presentation_json,row_hash) VALUES (?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, beat.id, beat.sequenceOrder, beat.textRef.id, beat.textRef.revision,
                        beat.expression, beat.presentationJson, rowHash);
                }
            }

            foreach (ActionRecord item in package.actions)
                InsertAction(connection, item);
            foreach (ItemRecord item in package.items)
            {
                InsertRevision(connection, "items", "item_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO items(item_id,revision,semantic_hash,display_text_id,display_text_revision,stack_limit,row_hash) VALUES (?,?,?,?,?,?,?)",
                    item.id, item.revision, item.semanticHash, item.displayTextRef.id, item.displayTextRef.revision, item.stackLimit);
            }
            foreach (FurnitureDefinitionRecord item in package.furnitureDefinitions)
            {
                InsertRevision(connection, "furniture_definitions", "furniture_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO furniture_definitions(furniture_id,revision,semantic_hash,display_text_id,display_text_revision,definition_json,row_hash) VALUES (?,?,?,?,?,?,?)",
                    item.id, item.revision, item.semanticHash, item.displayTextRef.id, item.displayTextRef.revision, item.definitionJson);
                foreach (FurnitureFunctionBindingRecord binding in item.functionBindings)
                {
                    string rowHash = HashFields(binding.functionId, binding.pointId, binding.useId,
                        binding.functionContractVersion.ToString(CultureInfo.InvariantCulture), binding.contractJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM furniture_function_bindings WHERE furniture_id=? AND furniture_revision=? AND function_id=? AND point_id=? AND use_id=?",
                        new object[] { item.id, item.revision, binding.functionId, binding.pointId, binding.useId }, rowHash,
                        "INSERT INTO furniture_function_bindings(furniture_id,furniture_revision,function_id,point_id,use_id,function_contract_version,contract_json,row_hash) VALUES (?,?,?,?,?,?,?,?)",
                        item.id, item.revision, binding.functionId, binding.pointId, binding.useId,
                        binding.functionContractVersion, binding.contractJson, rowHash);
                }
            }
            foreach (RuleRecord item in package.rules)
                InsertRevision(connection, "rules", "rule_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO rules(rule_id,revision,semantic_hash,expression_json,row_hash) VALUES (?,?,?,?,?)",
                    item.id, item.revision, item.semanticHash, item.expressionJson);

            foreach (FlowRecord item in package.flows)
                InsertRevision(connection, "flows", "flow_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO flows(flow_id,revision,semantic_hash,kind,entry_step_id,row_hash) VALUES (?,?,?,?,?,?)",
                    item.id, item.revision, item.semanticHash, item.kind, item.entryStepId);
            foreach (FlowRecord item in package.flows)
            {
                foreach (StepRecord step in item.steps)
                {
                    string rowHash = HashFields(step.id, step.kind, RefId(step.dialogueRef), RefRevision(step.dialogueRef),
                        RefId(step.actionRef), RefRevision(step.actionRef), RefId(step.calledFlowRef), RefRevision(step.calledFlowRef), step.dataJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM steps WHERE flow_id=? AND flow_revision=? AND step_id=?",
                        new object[] { item.id, item.revision, step.id }, rowHash,
                        "INSERT INTO steps(flow_id,flow_revision,step_id,kind,dialogue_id,dialogue_revision,action_id,action_revision,called_flow_id,called_flow_revision,data_json,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, step.id, step.kind, RefId(step.dialogueRef), RefRevision(step.dialogueRef),
                        RefId(step.actionRef), RefRevision(step.actionRef), RefId(step.calledFlowRef), RefRevision(step.calledFlowRef),
                        step.dataJson, rowHash);
                }
                foreach (TransitionRecord transition in item.transitions)
                {
                    string rowHash = HashFields(transition.id, transition.fromStepId, transition.toStepId, transition.purpose,
                        RefId(transition.ruleRef), RefRevision(transition.ruleRef));
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM transitions WHERE flow_id=? AND flow_revision=? AND transition_id=?",
                        new object[] { item.id, item.revision, transition.id }, rowHash,
                        "INSERT INTO transitions(flow_id,flow_revision,transition_id,from_step_id,to_step_id,purpose,rule_id,rule_revision,row_hash) VALUES (?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, transition.id, transition.fromStepId, transition.toStepId, transition.purpose,
                        RefId(transition.ruleRef), RefRevision(transition.ruleRef), rowHash);
                }
                foreach (FlowParameterRecord parameter in item.parameters)
                {
                    string rowHash = HashFields(parameter.name, parameter.direction, parameter.valueType,
                        parameter.required ? "1" : "0", parameter.defaultValueJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM flow_parameters WHERE flow_id=? AND flow_revision=? AND direction=? AND name=?",
                        new object[] { item.id, item.revision, parameter.direction, parameter.name }, rowHash,
                        "INSERT INTO flow_parameters(flow_id,flow_revision,name,direction,value_type,required,default_value_json,row_hash) VALUES (?,?,?,?,?,?,?,?)",
                        item.id, item.revision, parameter.name, parameter.direction, parameter.valueType,
                        parameter.required ? 1 : 0, parameter.defaultValueJson, rowHash);
                }
                foreach (FlowVariableRecord variable in item.variables)
                {
                    string rowHash = HashFields(variable.name, variable.valueType, variable.mutable ? "1" : "0", variable.defaultValueJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM flow_variables WHERE flow_id=? AND flow_revision=? AND name=?",
                        new object[] { item.id, item.revision, variable.name }, rowHash,
                        "INSERT INTO flow_variables(flow_id,flow_revision,name,value_type,mutable,default_value_json,row_hash) VALUES (?,?,?,?,?,?,?)",
                        item.id, item.revision, variable.name, variable.valueType, variable.mutable ? 1 : 0,
                        variable.defaultValueJson, rowHash);
                }
                foreach (StepBindingRecord binding in item.bindings)
                {
                    string literalJson = binding.sourceKind == "literal" ? binding.literalJson : null;
                    string sourceName = binding.sourceKind == "local" || binding.sourceKind == "input" ? binding.sourceName : null;
                    string hostFactId = binding.sourceKind == "host_fact" ? binding.hostFactId : null;
                    string resultStepId = binding.sourceKind == "result" ? binding.resultStepId : null;
                    string resultName = binding.sourceKind == "result" ? binding.resultName : null;
                    string rowHash = HashFields(binding.stepId, binding.destinationName, binding.sourceKind, literalJson,
                        sourceName, hostFactId, resultStepId, resultName);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM step_bindings WHERE flow_id=? AND flow_revision=? AND step_id=? AND destination_name=?",
                        new object[] { item.id, item.revision, binding.stepId, binding.destinationName }, rowHash,
                        "INSERT INTO step_bindings(flow_id,flow_revision,step_id,destination_name,source_kind,literal_json,source_name,host_fact_id,result_step_id,result_name,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, binding.stepId, binding.destinationName, binding.sourceKind, literalJson,
                        sourceName, hostFactId, resultStepId, resultName, rowHash);
                }
                foreach (FlowExitRecord exit in item.exits)
                {
                    string rowHash = HashFields(exit.name, exit.contractJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM flow_exits WHERE flow_id=? AND flow_revision=? AND exit_name=?",
                        new object[] { item.id, item.revision, exit.name }, rowHash,
                        "INSERT INTO flow_exits(flow_id,flow_revision,exit_name,contract_json,row_hash) VALUES (?,?,?,?,?)",
                        item.id, item.revision, exit.name, exit.contractJson, rowHash);
                }
                foreach (ExitMappingRecord mapping in item.exitMappings)
                {
                    string rowHash = HashFields(mapping.callStepId, mapping.exitName, mapping.transitionId, mapping.outputMappingsJson);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM exit_mappings WHERE flow_id=? AND flow_revision=? AND call_step_id=? AND exit_name=?",
                        new object[] { item.id, item.revision, mapping.callStepId, mapping.exitName }, rowHash,
                        "INSERT INTO exit_mappings(flow_id,flow_revision,call_step_id,callee_flow_id,callee_revision,exit_name,transition_id,output_mappings_json,row_hash) VALUES (?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, mapping.callStepId, mapping.calleeFlowRef.id, mapping.calleeFlowRef.revision,
                        mapping.exitName, mapping.transitionId, mapping.outputMappingsJson, rowHash);
                }
                foreach (ChoiceRecord choice in item.choices)
                {
                    string rowHash = HashFields(choice.stepId, choice.id, choice.transitionId, choice.valueJson,
                        choice.labelTextRef.id, choice.labelTextRef.revision, RefId(choice.availabilityRuleRef),
                        RefRevision(choice.availabilityRuleRef), RefId(choice.lockedHintTextRef), RefRevision(choice.lockedHintTextRef));
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM choices WHERE flow_id=? AND flow_revision=? AND step_id=? AND choice_id=?",
                        new object[] { item.id, item.revision, choice.stepId, choice.id }, rowHash,
                        "INSERT INTO choices(flow_id,flow_revision,step_id,choice_id,transition_id,value_json,label_text_id,label_text_revision,availability_rule_id,availability_rule_revision,hint_text_id,hint_text_revision,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, choice.stepId, choice.id, choice.transitionId, choice.valueJson,
                        choice.labelTextRef.id, choice.labelTextRef.revision, RefId(choice.availabilityRuleRef),
                        RefRevision(choice.availabilityRuleRef), RefId(choice.lockedHintTextRef), RefRevision(choice.lockedHintTextRef), rowHash);
                }
            }

            foreach (TopicRecord item in package.topics)
                InsertRevision(connection, "topics", "topic_id", item.id, item.revision, item.semanticHash,
                    "INSERT INTO topics(topic_id,revision,semantic_hash,route_id,title_text_id,title_text_revision,prompt_text_id,prompt_text_revision,replay_policy,cooldown_seconds,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                    item.id, item.revision, item.semanticHash, item.routeId, item.titleTextRef.id, item.titleTextRef.revision,
                    item.promptTextRef.id, item.promptTextRef.revision, item.defaultReplayPolicy, item.cooldownSeconds);
            foreach (TopicRecord item in package.topics)
                foreach (TopicEntryRecord entry in item.entries)
                {
                    string rowHash = HashFields(entry.id, entry.flowRef.id, entry.flowRef.revision,
                        entry.priority.ToString(CultureInfo.InvariantCulture), entry.baseWeight.ToString("R", CultureInfo.InvariantCulture),
                        entry.conditionJson, entry.modifiersJson, entry.channelsJson, entry.consumptionNodeId);
                    InsertImmutableRow(connection,
                        "SELECT row_hash FROM topic_entries WHERE topic_id=? AND topic_revision=? AND entry_id=?",
                        new object[] { item.id, item.revision, entry.id }, rowHash,
                        "INSERT INTO topic_entries(topic_id,topic_revision,entry_id,flow_id,flow_revision,priority,base_weight,condition_json,modifiers_json,channels_json,consumption_node_id,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                        item.id, item.revision, entry.id, entry.flowRef.id, entry.flowRef.revision, entry.priority,
                        entry.baseWeight, entry.conditionJson, entry.modifiersJson, entry.channelsJson, entry.consumptionNodeId ?? string.Empty, rowHash);
                }

            foreach (AnchorRecord item in package.anchors)
                InsertAnchor(connection, item);
            foreach (EmbeddingProfileRecord item in package.embeddingProfiles)
                InsertEmbeddingProfile(connection, item);
            foreach (AnchorLinkRecord item in package.anchorLinks)
                InsertAnchorLink(connection, item);
            foreach (EmbeddingRecord item in package.embeddings)
                InsertEmbedding(connection, item);
            foreach (ContentRelease item in package.releases)
                InsertRelease(connection, item);
            foreach (ContentRelease item in package.releases)
                foreach (ContentReleaseDependency dependency in item.dependencies)
                    InsertReleaseDependency(connection, item.releaseId, dependency);
            foreach (LegacySourceRecord item in package.legacySources)
            {
                string rowHash = HashFields(item.kind, item.relativePath, item.sourceId, item.sourceHash,
                    item.payloadHash, item.contentId, item.contentRevision, item.metadataJson);
                InsertImmutableRow(connection,
                    "SELECT row_hash FROM legacy_sources WHERE source_kind=? AND source_path=? AND source_hash=?",
                    new object[] { item.kind, item.relativePath, item.sourceHash }, rowHash,
                    "INSERT INTO legacy_sources(source_kind,source_path,source_id,source_hash,payload_hash,content_id,content_revision,metadata_json,source_bytes,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?)",
                    item.kind, item.relativePath, item.sourceId, item.sourceHash, item.payloadHash, item.contentId, item.contentRevision,
                    item.metadataJson, item.sourceBytes ?? new byte[0], rowHash);
            }
            if (package.buildManifest != null)
            {
                SetImmutableMetadata(connection, "build_manifest_json", package.buildManifest.ToJson());
                SetImmutableMetadata(connection, "build_manifest_hash", package.buildManifest.Hash);
            }
        }

        private static bool ReadFlowContracts(SQLiteConnection connection, FlowRecord flow, out string error)
        {
            error = string.Empty;
            foreach (FlowParameterRow row in connection.Query<FlowParameterRow>(
                "SELECT name,direction,value_type,required,default_value_json,row_hash FROM flow_parameters WHERE flow_id=? AND flow_revision=? ORDER BY direction,name",
                flow.id, flow.revision))
            {
                if (!RowHashMatches(row.row_hash, HashFields(row.name, row.direction, row.value_type, row.required != 0 ? "1" : "0", row.default_value_json)))
                { error = "Flow parameter row hash mismatch: " + flow.id + "/" + row.name; return false; }
                flow.parameters.Add(new FlowParameterRecord { name = row.name, direction = row.direction, valueType = row.value_type,
                    required = row.required != 0, defaultValueJson = row.default_value_json });
            }
            foreach (FlowVariableRow row in connection.Query<FlowVariableRow>(
                "SELECT name,value_type,mutable,default_value_json,row_hash FROM flow_variables WHERE flow_id=? AND flow_revision=? ORDER BY name",
                flow.id, flow.revision))
            {
                if (!RowHashMatches(row.row_hash, HashFields(row.name, row.value_type, row.mutable != 0 ? "1" : "0", row.default_value_json)))
                { error = "Flow variable row hash mismatch: " + flow.id + "/" + row.name; return false; }
                flow.variables.Add(new FlowVariableRecord { name = row.name, valueType = row.value_type,
                    mutable = row.mutable != 0, defaultValueJson = row.default_value_json });
            }
            foreach (StepBindingRow row in connection.Query<StepBindingRow>(
                "SELECT step_id,destination_name,source_kind,literal_json,source_name,host_fact_id,result_step_id,result_name,row_hash FROM step_bindings WHERE flow_id=? AND flow_revision=? ORDER BY step_id,destination_name",
                flow.id, flow.revision))
            {
                string literalJson = row.source_kind == "literal" ? row.literal_json : null;
                string sourceName = row.source_kind == "local" || row.source_kind == "input" ? row.source_name : null;
                string hostFactId = row.source_kind == "host_fact" ? row.host_fact_id : null;
                string resultStepId = row.source_kind == "result" ? row.result_step_id : null;
                string resultName = row.source_kind == "result" ? row.result_name : null;
                if (!RowHashMatches(row.row_hash, HashFields(row.step_id, row.destination_name, row.source_kind,
                    literalJson, sourceName, hostFactId, resultStepId, resultName)))
                { error = "Flow binding row hash mismatch: " + flow.id + "/" + row.step_id + "/" + row.destination_name; return false; }
                flow.bindings.Add(new StepBindingRecord { stepId = row.step_id, destinationName = row.destination_name,
                    sourceKind = row.source_kind, literalJson = row.literal_json ?? string.Empty, sourceName = row.source_name ?? string.Empty,
                    hostFactId = row.host_fact_id ?? string.Empty, resultStepId = row.result_step_id ?? string.Empty,
                    resultName = row.result_name ?? string.Empty });
            }
            foreach (FlowExitRow row in connection.Query<FlowExitRow>(
                "SELECT exit_name,contract_json,row_hash FROM flow_exits WHERE flow_id=? AND flow_revision=? ORDER BY exit_name",
                flow.id, flow.revision))
            {
                if (!RowHashMatches(row.row_hash, HashFields(row.exit_name, row.contract_json)))
                { error = "Flow exit row hash mismatch: " + flow.id + "/" + row.exit_name; return false; }
                flow.exits.Add(new FlowExitRecord { name = row.exit_name, contractJson = row.contract_json });
            }
            foreach (ExitMappingRow row in connection.Query<ExitMappingRow>(
                "SELECT call_step_id,callee_flow_id,callee_revision,exit_name,transition_id,output_mappings_json,row_hash FROM exit_mappings WHERE flow_id=? AND flow_revision=? ORDER BY call_step_id,exit_name",
                flow.id, flow.revision))
            {
                if (!RowHashMatches(row.row_hash, HashFields(row.call_step_id, row.exit_name, row.transition_id, row.output_mappings_json)))
                { error = "Flow exit mapping row hash mismatch: " + flow.id + "/" + row.call_step_id + "/" + row.exit_name; return false; }
                flow.exitMappings.Add(new ExitMappingRecord { callStepId = row.call_step_id,
                    calleeFlowRef = new ContentRef(StoreIdFromConnection(connection), row.callee_flow_id, row.callee_revision),
                    exitName = row.exit_name, transitionId = row.transition_id, outputMappingsJson = row.output_mappings_json });
            }
            return true;
        }

        private static string StoreIdFromConnection(SQLiteConnection connection)
        {
            return connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key=?", "store_id") ?? string.Empty;
        }

        private static bool ValidatePackage(ContentPackage package, string storeId, out string error)
        {
            error = string.Empty;
            if (package == null || !IsIdentifier(package.packageId) || package.storeId != storeId)
            {
                error = "The package ID or store identity is invalid.";
                return false;
            }
            if (package.topics == null || package.flows == null || package.dialogues == null || package.texts == null ||
                package.actions == null || package.items == null || package.furnitureDefinitions == null ||
                package.rules == null || package.anchors == null || package.anchorLinks == null ||
                package.embeddingProfiles == null || package.embeddings == null || package.legacySources == null || package.releases == null)
            {
                error = "Package collections must be present, including empty collections.";
                return false;
            }
            if (!string.IsNullOrEmpty(package.sourceManifestHash) && !IsSha256(package.sourceManifestHash))
            {
                error = "The source manifest hash is invalid.";
                return false;
            }

            int count = package.topics.Count + package.flows.Count + package.dialogues.Count + package.texts.Count +
                package.actions.Count + package.items.Count + package.furnitureDefinitions.Count + package.rules.Count + package.anchors.Count + package.anchorLinks.Count +
                package.embeddingProfiles.Count + package.embeddings.Count + package.legacySources.Count + package.releases.Count;
            if (count > MaximumPackageRecords)
            {
                error = "The package exceeds the bounded record count.";
                return false;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextRecord item in package.texts)
            {
                if (item == null || !ValidateDefinition(item.id, item.revision, item.semanticHash) || item.translations == null)
                { error = "A Text record is missing a stable ID, immutable revision, hash or translation list."; return false; }
                if (!keys.Add("text\0" + item.id + "\0" + item.revision))
                { error = "The package repeats a Text revision."; return false; }
                if (item.translations.Count > 64) { error = "A Text has too many language rows."; return false; }
                var languages = new HashSet<string>(StringComparer.Ordinal);
                foreach (TranslationRecord translation in item.translations)
                    if (translation == null || !IsLanguage(translation.language) || translation.text == null || translation.text.Length > 8192 ||
                        !languages.Add(translation.language))
                    { error = "A Text translation has an invalid locale/value or duplicate language."; return false; }
            }

            keys.Clear();
            foreach (DialogueRecord item in package.dialogues)
            {
                if (item == null || !ValidateDefinition(item.id, item.revision, item.semanticHash) || item.beats == null || item.beats.Count == 0 || item.beats.Count > 16)
                { error = "A Dialogue record is invalid or outside the sixteen-Beat limit."; return false; }
                if (!keys.Add("dialogue\0" + item.id + "\0" + item.revision)) { error = "The package repeats a Dialogue revision."; return false; }
                var beatIds = new HashSet<string>(StringComparer.Ordinal);
                var orders = new HashSet<int>();
                foreach (BeatRecord beat in item.beats)
                    if (beat == null || !IsIdentifier(beat.id) || beat.sequenceOrder < 0 || !beatIds.Add(beat.id) || !orders.Add(beat.sequenceOrder) ||
                        !ValidateLocalReference(beat.textRef, storeId, out error) || !IsBounded(beat.expression, 64) || !IsJsonPayload(beat.presentationJson, 8192))
                    { if (string.IsNullOrEmpty(error)) error = "A Beat identity/order/presentation is invalid."; return false; }
            }

            keys.Clear();
            foreach (ActionRecord item in package.actions)
                if (item == null || !ValidateDefinition(item.id, item.revision, item.semanticHash) || !IsIdentifier(item.capabilityId) ||
                    !IsJsonPayload(item.contractJson, 16384) || !keys.Add("action\0" + item.id + "\0" + item.revision))
                { error = "An Action contract or immutable revision is invalid or duplicated."; return false; }

            keys.Clear();
            foreach (RuleRecord item in package.rules)
                if (item == null || !ValidateDefinition(item.id, item.revision, item.semanticHash) || !IsJsonPayload(item.expressionJson, 16384) ||
                    !keys.Add("rule\0" + item.id + "\0" + item.revision))
                { error = "A Rule expression or immutable revision is invalid or duplicated."; return false; }

            keys.Clear();
            foreach (FlowRecord item in package.flows)
            {
                if (item == null || !ValidateDefinition(item.id, item.revision, item.semanticHash) ||
                    (item.kind != "scenario" && item.kind != "subflow") || !IsIdentifier(item.entryStepId) || item.steps == null ||
                    item.transitions == null || item.choices == null || item.parameters == null || item.variables == null ||
                    item.bindings == null || item.exits == null || item.exitMappings == null || item.steps.Count == 0 || item.steps.Count > 64 ||
                    !keys.Add("flow\0" + item.id + "\0" + item.revision))
                { error = "A Flow is invalid or outside its graph/list bounds."; return false; }
                var stepIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (StepRecord step in item.steps)
                    if (step == null || !IsIdentifier(step.id) || !stepIds.Add(step.id) || !ValidateStep(step, storeId, out error))
                    { if (string.IsNullOrEmpty(error)) error = "A Flow Step is invalid or duplicated."; return false; }
                if (!stepIds.Contains(item.entryStepId)) { error = "A Flow entry Step is missing."; return false; }
                if (item.transitions.Count > 256 || item.choices.Count > 512 || item.parameters.Count > 32 || item.variables.Count > 16 ||
                    item.bindings.Count > 1024 || item.exits.Count > 64 || item.exitMappings.Count > 256)
                { error = "A Flow exceeds a bounded child-record count."; return false; }
                foreach (TransitionRecord transition in item.transitions)
                    if (transition == null || !IsIdentifier(transition.id) || !stepIds.Contains(transition.fromStepId) ||
                        !stepIds.Contains(transition.toStepId) || !IsIdentifier(transition.purpose) ||
                        transition.ruleRef != null && !ValidateLocalReference(transition.ruleRef, storeId, out error))
                    { if (string.IsNullOrEmpty(error)) error = "A Flow transition is invalid."; return false; }
                foreach (ChoiceRecord choice in item.choices)
                    if (choice == null || !stepIds.Contains(choice.stepId) || !IsIdentifier(choice.id) || !IsIdentifier(choice.transitionId) ||
                        !IsJsonPayload(choice.valueJson, 4096) || !ValidateLocalReference(choice.labelTextRef, storeId, out error) ||
                        choice.availabilityRuleRef != null && !ValidateLocalReference(choice.availabilityRuleRef, storeId, out error) ||
                        choice.lockedHintTextRef != null && !ValidateLocalReference(choice.lockedHintTextRef, storeId, out error))
                    { if (string.IsNullOrEmpty(error)) error = "A Choice or one of its references is invalid."; return false; }
                foreach (FlowParameterRecord parameter in item.parameters)
                    if (parameter == null || !IsIdentifier(parameter.name) || !IsParameterDirection(parameter.direction) ||
                        !IsValueType(parameter.valueType) || parameter.defaultValueJson != null && parameter.defaultValueJson.Length > 2048)
                    { error = "A Flow parameter is invalid."; return false; }
                foreach (FlowVariableRecord variable in item.variables)
                    if (variable == null || !IsIdentifier(variable.name) || !IsValueType(variable.valueType) ||
                        variable.defaultValueJson != null && variable.defaultValueJson.Length > 2048)
                    { error = "A Flow local variable is invalid."; return false; }
                foreach (StepBindingRecord binding in item.bindings)
                    if (binding == null || !stepIds.Contains(binding.stepId) || !IsIdentifier(binding.destinationName) ||
                        !IsBindingSource(binding.sourceKind, binding))
                    { error = "A Step binding must contain exactly one bounded typed source."; return false; }
                foreach (FlowExitRecord exit in item.exits)
                    if (exit == null || !IsIdentifier(exit.name) || !IsJsonPayload(exit.contractJson, 8192))
                    { error = "A named Flow exit is invalid."; return false; }
                foreach (ExitMappingRecord mapping in item.exitMappings)
                    if (mapping == null || !stepIds.Contains(mapping.callStepId) || mapping.calleeFlowRef == null ||
                        !ValidateLocalReference(mapping.calleeFlowRef, storeId, out error) || !IsIdentifier(mapping.exitName) ||
                        !IsIdentifier(mapping.transitionId) || !IsJsonPayload(mapping.outputMappingsJson, 8192))
                    { if (string.IsNullOrEmpty(error)) error = "A Subflow exit mapping is invalid."; return false; }
            }

            keys.Clear();
            foreach (TopicRecord item in package.topics)
            {
                if (item == null || !ValidateDefinition(item.id, item.revision, item.semanticHash) || !IsIdentifier(item.routeId) ||
                    item.entries == null || item.entries.Count > 256 || !IsReplayPolicy(item.defaultReplayPolicy) ||
                    !Finite(item.cooldownSeconds) || item.cooldownSeconds < 0d || item.cooldownSeconds > 31536000d ||
                    !ValidateLocalReference(item.titleTextRef, storeId, out error) || !ValidateLocalReference(item.promptTextRef, storeId, out error) ||
                    !keys.Add("topic\0" + item.id + "\0" + item.revision))
                { if (string.IsNullOrEmpty(error)) error = "A Topic definition is invalid or duplicated."; return false; }
                var entries = new HashSet<string>(StringComparer.Ordinal);
                foreach (TopicEntryRecord entry in item.entries)
                    if (entry == null || !IsIdentifier(entry.id) || !entries.Add(entry.id) || entry.flowRef == null ||
                        !ValidateLocalReference(entry.flowRef, storeId, out error) || !Finite(entry.baseWeight) || entry.baseWeight < 0d ||
                        entry.baseWeight > 1000000d || !IsJsonPayload(entry.conditionJson, 8192) || !IsJsonPayload(entry.modifiersJson, 8192) ||
                        !IsJsonPayload(entry.channelsJson, 256) ||
                        !IsBounded(entry.consumptionNodeId, 128))
                    { if (string.IsNullOrEmpty(error)) error = "A Topic entry is invalid."; return false; }
            }

            keys.Clear();
            foreach (LegacySourceRecord item in package.legacySources)
                if (item == null || !IsIdentifier(item.kind) || !IsSafeRelativeSourcePath(item.relativePath) ||
                    !IsIdentifier(item.sourceId) || !IsSha256(item.sourceHash) || !IsIdentifier(item.contentId) ||
                    !IsSha256(item.payloadHash) || !IsSha256(item.contentRevision) || !IsJsonPayload(item.metadataJson, 16384) || item.sourceBytes == null ||
                    item.sourceBytes.Length > 4 * 1024 * 1024 || ComputeHash(item.sourceBytes) != item.payloadHash ||
                    !keys.Add(item.kind + "\0" + item.relativePath))
                { error = "A legacy source mapping is invalid or duplicated."; return false; }

            keys.Clear();
            var anchorsById = new Dictionary<string, AnchorRecord>(StringComparer.Ordinal);
            foreach (AnchorRecord item in package.anchors)
                if (item == null || !IsSha256(item.id) || !IsLanguage(item.language) || string.IsNullOrEmpty(item.text) || item.text.Length > 2048 ||
                    !IsSha256(item.textHash) || item.id != item.textHash || ComputeHash(Encoding.UTF8.GetBytes(item.text)) != item.textHash ||
                    !keys.Add("anchor\0" + item.id))
                { error = "An Anchor identity, language, text or source hash is invalid."; return false; }
                else anchorsById.Add(item.id, item);
            keys.Clear();
            var profilesById = new Dictionary<string, EmbeddingProfileRecord>(StringComparer.Ordinal);
            foreach (EmbeddingProfileRecord item in package.embeddingProfiles)
            {
                string profileCode, profileMessage;
                if (!ContentSemanticValidation.ValidateProfile(item, out profileCode, out profileMessage) ||
                    !keys.Add("profile\0" + item.id))
                { error = "An embedding compatibility profile is invalid: " + profileMessage; return false; }
                profilesById.Add(item.id, item);
            }
            keys.Clear();
            foreach (EmbeddingRecord item in package.embeddings)
            {
                AnchorRecord anchor;
                EmbeddingProfileRecord profile;
                string vectorCode = string.Empty, vectorMessage = string.Empty;
                if (item == null || !IsSha256(item.anchorId) || !IsIdentifier(item.profileId) ||
                    !anchorsById.TryGetValue(item.anchorId, out anchor) || !profilesById.TryGetValue(item.profileId, out profile) ||
                    !IsSha256(item.vectorHash) || item.vector == null || ComputeHash(item.vector) != item.vectorHash ||
                    !ContentSemanticValidation.ValidateVector(item.vector, profile, out vectorCode, out vectorMessage) ||
                    !keys.Add("embedding\0" + item.anchorId + "\0" + item.profileId))
                { error = "An embedding vector is invalid, unowned or incompatible: " + (item == null ? "record missing" : vectorMessage); return false; }
            }
            foreach (AnchorLinkRecord item in package.anchorLinks)
                if (item == null || !IsIdentifier(item.id) || !IsSha256(item.anchorId) ||
                    (item.ownerKind != "topic" && item.ownerKind != "flow" && item.ownerKind != "dialogue" && item.ownerKind != "canonical") ||
                    !IsIdentifier(item.ownerId) || !IsSha256(item.ownerRevision) || !IsIdentifier(item.role))
                { error = "An anchor owner reference is invalid."; return false; }
            foreach (ContentRelease item in package.releases)
                if (item == null || !IsIdentifier(item.releaseId) || !IsJsonPayload(item.manifestJson, MaximumJsonCharacters) ||
                    !IsSha256(item.manifestHash) || !IsBounded(item.validatorVersion, 64) || !IsSha256(item.graphFingerprint) ||
                    !IsBounded(item.indexFingerprint, 128) || item.dependencies == null || item.dependencies.Count > MaximumPackageRecords)
                { error = "A release manifest or readiness fingerprint is invalid."; return false; }
            foreach (ContentRelease item in package.releases)
            {
                var dependencyKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (ContentReleaseDependency dependency in item.dependencies)
                    if (dependency == null || !IsIdentifier(dependency.kind) || !IsDependencyIdentifier(dependency.id) ||
                        !IsSha256(dependency.revision) || !IsIdentifier(dependency.store) || !IsSha256(dependency.expectedHash) ||
                        !dependencyKeys.Add(dependency.kind + "\0" + dependency.id + "\0" + dependency.revision + "\0" + dependency.store))
                    { error = "A release dependency is invalid or duplicated."; return false; }
            }

            return true;
        }

        private static bool ValidateStep(StepRecord step, string storeId, out string error)
        {
            error = string.Empty;
            if (!IsJsonPayload(step.dataJson, 16384)) { error = "Step payload is too large or empty."; return false; }
            if (step.kind == "dialogue") return IsOnlyReference(step, "dialogue", storeId, out error);
            if (step.kind == "ask" && step.dialogueRef != null) return IsOnlyReference(step, "dialogue", storeId, out error);
            if (step.kind == "action" || step.kind == "host_effect") return IsOnlyReference(step, "action", storeId, out error);
            if (step.kind == "subflow") return IsOnlyReference(step, "subflow", storeId, out error);
            return (step.kind == "ask" || step.kind == "wait" || step.kind == "branch" || step.kind == "end") &&
                step.dialogueRef == null && step.actionRef == null && step.calledFlowRef == null;
        }

        private static bool IsOnlyReference(StepRecord step, string kind, string storeId, out string error)
        {
            error = string.Empty;
            ContentRef selected = kind == "dialogue" ? step.dialogueRef : kind == "action" ? step.actionRef : step.calledFlowRef;
            if (selected == null || !ValidateLocalReference(selected, storeId, out error)) return false;
            if ((kind != "dialogue" && step.dialogueRef != null) || (kind != "action" && step.actionRef != null) ||
                (kind != "subflow" && step.calledFlowRef != null))
            { error = "A Step kind must use exactly its one supported typed reference."; return false; }
            return true;
        }

        private static bool ValidateLocalReference(ContentRef reference, string storeId, out string error)
        {
            error = string.Empty;
            if (reference == null || reference.store != storeId || !IsIdentifier(reference.id) || !IsSha256(reference.revision) ||
                !string.IsNullOrEmpty(reference.expectedHash) && !IsSha256(reference.expectedHash))
            {
                error = "A content reference must name a safe ID and exact revision in this store.";
                return false;
            }
            return true;
        }

        private static void InsertRevision(SQLiteConnection connection, string table, string idColumn, string id,
            string revision, string semanticHash, string insertSql, params object[] insertValues)
        {
            string rowHash = HashObjectFields(insertValues);
            string select = "SELECT row_hash FROM " + table + " WHERE " + idColumn + "=? AND revision=?";
            string existing = connection.ExecuteScalar<string>(select, id, revision);
            if (existing != null)
            {
                if (!string.Equals(existing, rowHash, StringComparison.Ordinal))
                    throw new ContentConflictException("Immutable revision " + id + "@" + revision + " has different row content.");
                return;
            }
            var values = new object[insertValues.Length + 1];
            Array.Copy(insertValues, values, insertValues.Length);
            values[values.Length - 1] = rowHash;
            connection.Execute(insertSql, values);
        }

        private static void InsertAction(SQLiteConnection connection, ActionRecord item)
        {
            bool legacy = string.IsNullOrEmpty(item.implementationKind) || item.implementationKind == "legacy_activity";
            object composer = string.IsNullOrEmpty(item.composerId) ? null : (object)item.composerId;
            object function = string.IsNullOrEmpty(item.functionId) ? null : (object)item.functionId;
            object arguments = string.IsNullOrEmpty(item.argumentsJson) ? null : (object)item.argumentsJson;
            string rowHash = legacy
                ? HashObjectFields(new object[] { item.id, item.revision, item.semanticHash, item.capabilityId, item.contractJson })
                : HashFields(item.id, item.revision, item.semanticHash, item.capabilityId, item.contractJson,
                    item.implementationKind, item.composerId, item.functionId, item.argumentsJson);
            string existing = connection.ExecuteScalar<string>("SELECT row_hash FROM actions WHERE action_id=? AND revision=?", item.id, item.revision);
            if (existing != null)
            {
                if (!string.Equals(existing, rowHash, StringComparison.Ordinal))
                    throw new ContentConflictException("Immutable Action revision " + item.id + "@" + item.revision + " has different row content.");
            }
            else
            {
                connection.Execute("INSERT INTO actions(action_id,revision,semantic_hash,capability_id,contract_json,row_hash,implementation_kind,composer_id,function_id,arguments_json) VALUES (?,?,?,?,?,?,?,?,?,?)",
                    item.id, item.revision, item.semanticHash, item.capabilityId, item.contractJson, rowHash,
                    legacy ? "legacy_activity" : item.implementationKind, composer, function, arguments);
            }

            foreach (ActionSequenceStepRecord step in item.sequenceSteps ?? new List<ActionSequenceStepRecord>())
            {
                string stepHash = HashFields(step.ordinal.ToString(CultureInfo.InvariantCulture), step.stepId, step.primitiveId,
                    step.argumentsJson, step.outputsJson, step.successEdgeJson, step.failureEdgeJson, step.cancelEdgeJson, step.checkpointPolicy);
                InsertImmutableRow(connection,
                    "SELECT row_hash FROM action_sequence_steps WHERE action_id=? AND action_revision=? AND step_id=?",
                    new object[] { item.id, item.revision, step.stepId }, stepHash,
                    "INSERT INTO action_sequence_steps(action_id,action_revision,ordinal,step_id,primitive_id,arguments_json,outputs_json,success_edge_json,failure_edge_json,cancel_edge_json,checkpoint_policy,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                    item.id, item.revision, step.ordinal, step.stepId, step.primitiveId, step.argumentsJson,
                    step.outputsJson, step.successEdgeJson, step.failureEdgeJson, step.cancelEdgeJson, step.checkpointPolicy, stepHash);
            }
        }

        private static void InsertImmutableRow(SQLiteConnection connection, string selectSql, object[] selectValues,
            string rowHash, string insertSql, params object[] insertValues)
        {
            string existing = connection.ExecuteScalar<string>(selectSql, selectValues);
            if (existing != null)
            {
                if (!string.Equals(existing, rowHash, StringComparison.Ordinal))
                    throw new ContentConflictException("An immutable child row was submitted with different content.");
                return;
            }
            connection.Execute(insertSql, insertValues);
        }

        private static void InsertAnchor(SQLiteConnection connection, AnchorRecord item)
        {
            string existing = connection.ExecuteScalar<string>("SELECT text_hash FROM anchors WHERE anchor_id=?", item.id);
            if (existing != null)
            {
                if (existing != item.textHash) throw new ContentConflictException("Immutable Anchor ID was reused with different text.");
                return;
            }
            connection.Execute("INSERT INTO anchors(anchor_id,language,text_value,text_hash) VALUES (?,?,?,?)",
                item.id, item.language, item.text, item.textHash);
        }

        private static void InsertAnchorLink(SQLiteConnection connection, AnchorLinkRecord item)
        {
            string rowHash = HashFields(item.anchorId, item.ownerKind, item.ownerId, item.ownerRevision, item.role);
            InsertImmutableRow(connection, "SELECT row_hash FROM anchor_links WHERE link_id=?", new object[] { item.id }, rowHash,
                "INSERT INTO anchor_links(link_id,anchor_id,owner_kind,owner_topic_id,owner_topic_revision,owner_flow_id,owner_flow_revision,owner_dialogue_id,owner_dialogue_revision,canonical_owner_id,canonical_revision,role,row_hash) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                item.id, item.anchorId,
                item.ownerKind,
                item.ownerKind == "topic" ? (object)item.ownerId : null,
                item.ownerKind == "topic" ? (object)item.ownerRevision : null,
                item.ownerKind == "flow" ? (object)item.ownerId : null,
                item.ownerKind == "flow" ? (object)item.ownerRevision : null,
                item.ownerKind == "dialogue" ? (object)item.ownerId : null,
                item.ownerKind == "dialogue" ? (object)item.ownerRevision : null,
                item.ownerKind == "canonical" ? (object)item.ownerId : null,
                item.ownerKind == "canonical" ? (object)item.ownerRevision : null,
                item.role, rowHash);
        }

        private static void InsertEmbeddingProfile(SQLiteConnection connection, EmbeddingProfileRecord item)
        {
            string profileHash = ContentSemanticValidation.ProfileFingerprint(item);
            string existing = connection.ExecuteScalar<string>("SELECT profile_hash FROM embedding_profiles WHERE profile_id=?", item.id);
            if (existing != null)
            {
                if (existing != profileHash) throw new ContentConflictException("Embedding profile ID was reused with an incompatible model fingerprint.");
                return;
            }
            connection.Execute("INSERT INTO embedding_profiles(profile_id,model_id,model_revision,tokenizer_hash,pipeline_hash,dimensions,dtype,normalization,metric,profile_hash) VALUES (?,?,?,?,?,?,?,?,?,?)",
                item.id, item.modelId, item.modelRevision, item.tokenizerHash, item.pipelineHash,
                item.dimensions, item.dtype, item.normalization, item.metric, profileHash);
        }

        private static void InsertEmbedding(SQLiteConnection connection, EmbeddingRecord item)
        {
            string existing = connection.ExecuteScalar<string>("SELECT vector_hash FROM embeddings WHERE anchor_id=? AND profile_id=?",
                item.anchorId, item.profileId);
            if (existing != null)
            {
                if (existing != item.vectorHash) throw new ContentConflictException("An immutable Anchor/profile vector was replaced.");
                return;
            }
            connection.Execute("INSERT INTO embeddings(anchor_id,profile_id,vector,vector_hash,vector_bytes) VALUES (?,?,?,?,?)",
                item.anchorId, item.profileId, item.vector, item.vectorHash, item.vector.Length);
        }

        private static void InsertRelease(SQLiteConnection connection, ContentRelease item)
        {
            string rowHash = HashFields(item.releaseId, item.manifestJson, item.manifestHash, item.validatorVersion,
                item.graphFingerprint, item.indexFingerprint);
            string existing = connection.ExecuteScalar<string>("SELECT row_hash FROM releases WHERE release_id=?", item.releaseId);
            if (existing != null)
            {
                if (existing != rowHash) throw new ContentConflictException("Immutable release ID was reused with different content.");
                return;
            }
            connection.Execute("INSERT INTO releases(release_id,manifest_json,manifest_hash,validator_version,graph_fingerprint,index_fingerprint,row_hash) VALUES (?,?,?,?,?,?,?)",
                item.releaseId, item.manifestJson, item.manifestHash, item.validatorVersion, item.graphFingerprint, item.indexFingerprint, rowHash);
        }

        private static void InsertReleaseDependency(SQLiteConnection connection, string releaseId, ContentReleaseDependency item)
        {
            string rowHash = HashFields(item.kind, item.id, item.revision, item.store, item.expectedHash);
            string existing = connection.ExecuteScalar<string>(
                "SELECT row_hash FROM release_dependencies WHERE release_id=? AND kind=? AND dependency_id=? AND revision=? AND store_id=?",
                releaseId, item.kind, item.id, item.revision, item.store);
            if (existing != null)
            {
                if (!string.Equals(existing, rowHash, StringComparison.Ordinal))
                    throw new ContentConflictException("An immutable release dependency was changed.");
                return;
            }
            connection.Execute("INSERT INTO release_dependencies(release_id,kind,dependency_id,revision,store_id,expected_hash,row_hash) VALUES (?,?,?,?,?,?,?)",
                releaseId, item.kind, item.id, item.revision, item.store, item.expectedHash, rowHash);
        }

        private static string ComputeReleaseGraphFingerprint(IEnumerable<ContentReleaseDependency> dependencies)
        {
            string[] keys = (dependencies ?? Enumerable.Empty<ContentReleaseDependency>())
                .OrderBy(item => item.kind, StringComparer.Ordinal).ThenBy(item => item.id, StringComparer.Ordinal)
                .ThenBy(item => item.revision, StringComparer.Ordinal).ThenBy(item => item.store, StringComparer.Ordinal)
                .ThenBy(item => item.expectedHash, StringComparer.Ordinal).Select(item =>
                    item.kind + "\0" + item.id + "\0" + item.revision + "\0" + item.store + "\0" + item.expectedHash).ToArray();
            return ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", keys)));
        }

        private static bool ValidateReleaseHeader(string json, ContentRelease release, string storeId)
        {
            try
            {
                JsonObject header = ContentJson.Parse(json) as JsonObject;
                if (header == null) return false;
                string releaseId = (string)header["releaseId"];
                string headerStore = (string)header["storeId"];
                string graphFingerprint = (string)header["graphFingerprint"];
                string indexFingerprint = (string)header["indexFingerprint"];
                int dependencyCount;
                int schemaVersion;
                if (!int.TryParse(header["dependencyCount"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dependencyCount) ||
                    !int.TryParse(header["schemaVersion"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out schemaVersion)) return false;
                return schemaVersion == 1 && releaseId == release.releaseId && headerStore == storeId &&
                    graphFingerprint == release.graphFingerprint && indexFingerprint == release.indexFingerprint &&
                    dependencyCount == release.dependencies.Count;
            }
            catch { return false; }
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value == "." || value == "..") return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':'))
                    return false;
            return true;
        }

        private static bool IsDependencyIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value == "." || value == "..") return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':')) return false;
            return true;
        }

        private static bool IsSafeRelativeSourcePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || Path.IsPathRooted(value)) return false;
            string normalized = value.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal)) return false;
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..") return false;
            return true;
        }

        private static bool ValidateDefinition(string id, string revision, string semanticHash)
        {
            return IsIdentifier(id) && IsSha256(revision) && IsSha256(semanticHash) &&
                string.Equals(revision, semanticHash, StringComparison.Ordinal);
        }

        private static bool IsLanguage(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2 || value.Length > 16) return false;
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z' || character >= '0' && character <= '9' || character == '-')) return false;
            return value[0] >= 'a' && value[0] <= 'z' && value[value.Length - 1] != '-';
        }

        private static bool IsParameterDirection(string value) { return value == "input" || value == "output"; }
        private static bool IsValueType(string value) { return value == "bool" || value == "int64" || value == "double" || value == "text" || value == "enum" || value == "entity"; }
        private static bool IsReplayPolicy(string value) { return value == "repeatable" || value == "once"; }
        private static bool IsBounded(string value, int maximum) { return value != null && value.Length <= maximum; }
        private static bool IsJsonPayload(string value, int maximum) { return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool IsBindingSource(string kind, StepBindingRecord binding)
        {
            if (kind == "literal") return !string.IsNullOrEmpty(binding.literalJson) && binding.sourceName == "" && binding.hostFactId == "" && binding.resultStepId == "" && binding.resultName == "";
            if (kind == "local" || kind == "input") return IsIdentifier(binding.sourceName) && binding.literalJson == "" && binding.hostFactId == "" && binding.resultStepId == "" && binding.resultName == "";
            if (kind == "host_fact") return IsIdentifier(binding.hostFactId) && binding.literalJson == "" && binding.sourceName == "" && binding.resultStepId == "" && binding.resultName == "";
            if (kind == "result") return IsIdentifier(binding.resultStepId) && IsIdentifier(binding.resultName) && binding.literalJson == "" && binding.sourceName == "" && binding.hostFactId == "";
            return false;
        }

        private static string RefId(ContentRef reference) { return reference == null ? null : reference.id; }
        private static string RefRevision(ContentRef reference) { return reference == null ? null : reference.revision; }

        private static string HashFields(params string[] values)
        {
            var builder = new StringBuilder();
            foreach (string value in values)
            {
                if (value == null) builder.Append("-1:");
                else builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
                builder.Append('|');
            }
            using (SHA256 hash = SHA256.Create()) return ToLowerHex(hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        private static void SetImmutableMetadata(SQLiteConnection connection, string key, string value)
        {
            string existing = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key=?", key);
            if (existing != null)
            {
                if (!string.Equals(existing, value, StringComparison.Ordinal))
                    throw new ContentConflictException("Immutable content metadata already exists with a different value: " + key);
                return;
            }
            ContentSchema.SetMetadata(connection, key, value);
        }

        private static string HashObjectFields(object[] values)
        {
            if (values == null) return HashFields((string)null);
            var fields = new string[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                object value = values[index];
                IFormattable formattable = value as IFormattable;
                fields[index] = value == null ? null : formattable == null
                    ? value.ToString()
                    : formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            return HashFields(fields);
        }

        private static string ComputeHash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return ToLowerHex(hash.ComputeHash(bytes));
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte item in bytes) builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f')) return false;
            return true;
        }

        private static ContentResult<ContentRepository> CreateNew(string path, ContentStoreMode mode, string requestedStoreId)
        {
            string directory = Path.GetDirectoryName(path);
            string temporaryPath = path + ".creating-" + Guid.NewGuid().ToString("N");
            Guid requestedGuid;
            string storeId = Guid.TryParse(requestedStoreId, out requestedGuid) ? requestedGuid.ToString("D") : Guid.NewGuid().ToString("D");
            try
            {
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var connection = new SQLiteConnection(temporaryPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create))
                {
                    ConfigureConnection(connection);
                    connection.BeginTransaction();
                    ContentSchema.CreateCurrent(connection, storeId);
                    connection.Commit();
                }
                if (File.Exists(path))
                    return Failure<ContentRepository>(ContentStatuses.Unavailable, "content_path_raced", path, "The database path appeared during creation.");
                File.Move(temporaryPath, path);
                return ContentResult<ContentRepository>.Ready(new ContentRepository(path, mode, storeId, ContentSchema.CurrentVersion));
            }
            catch (Exception exception)
            {
                return Failure<ContentRepository>(ContentStatuses.Unavailable, "content_create_failed", path, exception.Message);
            }
            finally
            {
                DeleteOwnedCreationFile(temporaryPath);
            }
        }

        private static bool UpgradeExisting(string path, out string status, out string code, out string message)
        {
            status = ContentStatuses.Unavailable;
            code = "content_migration_failed";
            message = "The content store migration failed.";
            try
            {
                using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite))
                {
                    ConfigureConnection(connection);
                    int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                    if (version == 4 && ContentSchema.HasVersion4Tables(connection))
                    {
                        ContentSchema.MigrateVersion4To5(connection);
                        version = 5;
                    }
                    if (version == 5 && ContentSchema.HasVersion5Tables(connection))
                        ContentSchema.MigrateVersion5To6(connection);
                }
                status = ContentStatuses.Ready;
                code = string.Empty;
                message = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return false;
            }
        }

        private static bool InspectExisting(string path, ContentStoreMode mode, out string storeId,
            out int schemaVersion, out string status, out string code, out string message)
        {
            storeId = string.Empty;
            schemaVersion = 0;
            status = ContentStatuses.Unavailable;
            code = "content_open_failed";
            message = "The content store could not be opened.";
            try
            {
                using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly))
                {
                    ConfigureConnection(connection);
                    int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                    if (version > ContentSchema.CurrentVersion)
                    {
                        status = ContentStatuses.Incompatible; code = "content_schema_future";
                        message = "The content store was created by a newer schema.";
                        return false;
                    }
                    bool currentSchema = version == ContentSchema.CurrentVersion && ContentSchema.HasRequiredTables(connection);
                    bool legacyReadOnlySchema = mode == ContentStoreMode.ReadOnly && version == 4 && ContentSchema.HasVersion4Tables(connection);
                    bool version5ReadOnlySchema = mode == ContentStoreMode.ReadOnly && version == 5 && ContentSchema.HasVersion5Tables(connection);
                    if (!currentSchema && !legacyReadOnlySchema && !version5ReadOnlySchema)
                    {
                        status = ContentStatuses.Incompatible; code = "content_schema_invalid";
                        message = "The existing content store has an unsupported or incomplete schema.";
                        return false;
                    }
                    storeId = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key=?", "store_id");
                    Guid parsed;
                    if (!Guid.TryParse(storeId, out parsed))
                    {
                        status = ContentStatuses.Invalid; code = "content_store_identity_invalid";
                        message = "The content store identity is missing or invalid.";
                        storeId = string.Empty;
                        return false;
                    }
                    schemaVersion = version;
                    status = ContentStatuses.Ready;
                    code = string.Empty;
                    message = string.Empty;
                    return true;
                }
            }
            catch (Exception exception)
            {
                status = ContentStatuses.Unavailable;
                code = "content_store_unavailable";
                message = exception.Message;
                storeId = string.Empty;
                return false;
            }
        }

        private static SQLiteConnection OpenConnection(string path, ContentStoreMode mode)
        {
            SQLiteOpenFlags flags = mode == ContentStoreMode.ReadOnly ? SQLiteOpenFlags.ReadOnly : SQLiteOpenFlags.ReadWrite;
            var connection = new SQLiteConnection(path, flags);
            try
            {
                ConfigureConnection(connection);
                return connection;
            }
            catch
            {
                connection.Close();
                throw;
            }
        }

        private static void ConfigureConnection(SQLiteConnection connection)
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
                throw new InvalidDataException("SQLite foreign-key enforcement could not be enabled.");
        }

        private static void Rollback(SQLiteConnection connection)
        {
            if (connection == null || !connection.IsInTransaction) return;
            try { connection.Rollback(); } catch { }
        }

        private static void DeleteOwnedCreationFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private ContentRef MakeRef(string id, string revision)
        {
            return string.IsNullOrEmpty(id) && string.IsNullOrEmpty(revision) ? null : new ContentRef(StoreId, id, revision);
        }

        private static ContentResult<T> Failure<T>(string status, string code, string path, string message)
        {
            return ContentResult<T>.Failure(status, code, path, message);
        }

        private static bool RowHashMatches(string storedHash, string computedHash)
        { return IsSha256(storedHash) && string.Equals(storedHash, computedHash, StringComparison.Ordinal); }

        private static ContentResult<T> CompiledRowHashFailure<T>(string kind, string id)
        {
            return Failure<T>(ContentStatuses.Incompatible, "compiled_row_hash_mismatch", id,
                "The compiled " + kind + " row body does not match its stored write-side row hash.");
        }

        private sealed class TextRow
        {
            public string text_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class DialogueRow
        {
            public string dialogue_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class BeatRow
        {
            public string beat_id { get; set; }
            public int sequence_order { get; set; }
            public string text_id { get; set; }
            public string text_revision { get; set; }
            public string expression { get; set; }
            public string presentation_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ActionRow
        {
            public string action_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string capability_id { get; set; }
            public string contract_json { get; set; }
            public string implementation_kind { get; set; }
            public string composer_id { get; set; }
            public string function_id { get; set; }
            public string arguments_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class RuleRow
        {
            public string rule_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string expression_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ActionSequenceStepRow
        {
            public int ordinal { get; set; }
            public string step_id { get; set; }
            public string primitive_id { get; set; }
            public string arguments_json { get; set; }
            public string outputs_json { get; set; }
            public string success_edge_json { get; set; }
            public string failure_edge_json { get; set; }
            public string cancel_edge_json { get; set; }
            public string checkpoint_policy { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ItemRow
        {
            public string item_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string display_text_id { get; set; }
            public string display_text_revision { get; set; }
            public long stack_limit { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class FurnitureRow
        {
            public string furniture_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string display_text_id { get; set; }
            public string display_text_revision { get; set; }
            public string definition_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class FurnitureBindingRow
        {
            public string function_id { get; set; }
            public string point_id { get; set; }
            public string use_id { get; set; }
            public int function_contract_version { get; set; }
            public string contract_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class TranslationRow
        {
            public string language { get; set; }
            public string text_value { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class TopicRow
        {
            public string topic_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string route_id { get; set; }
            public string title_text_id { get; set; }
            public string title_text_revision { get; set; }
            public string prompt_text_id { get; set; }
            public string prompt_text_revision { get; set; }
            public string replay_policy { get; set; }
            public double cooldown_seconds { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class TopicEntryRow
        {
            public string entry_id { get; set; }
            public string flow_id { get; set; }
            public string flow_revision { get; set; }
            public string condition_json { get; set; }
            public string modifiers_json { get; set; }
            public string channels_json { get; set; }
            public string consumption_node_id { get; set; }
            public int priority { get; set; }
            public double base_weight { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class FlowRow
        {
            public string flow_id { get; set; }
            public string revision { get; set; }
            public string semantic_hash { get; set; }
            public string kind { get; set; }
            public string entry_step_id { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class StepRow
        {
            public string step_id { get; set; }
            public string kind { get; set; }
            public string dialogue_id { get; set; }
            public string dialogue_revision { get; set; }
            public string action_id { get; set; }
            public string action_revision { get; set; }
            public string called_flow_id { get; set; }
            public string called_flow_revision { get; set; }
            public string data_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class TransitionRow
        {
            public string transition_id { get; set; }
            public string from_step_id { get; set; }
            public string to_step_id { get; set; }
            public string purpose { get; set; }
            public string rule_id { get; set; }
            public string rule_revision { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ChoiceRow
        {
            public string step_id { get; set; }
            public string choice_id { get; set; }
            public string transition_id { get; set; }
            public string value_json { get; set; }
            public string label_text_id { get; set; }
            public string label_text_revision { get; set; }
            public string availability_rule_id { get; set; }
            public string availability_rule_revision { get; set; }
            public string hint_text_id { get; set; }
            public string hint_text_revision { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class FlowParameterRow
        {
            public string name { get; set; }
            public string direction { get; set; }
            public string value_type { get; set; }
            public string default_value_json { get; set; }
            public int required { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class FlowVariableRow
        {
            public string name { get; set; }
            public string value_type { get; set; }
            public string default_value_json { get; set; }
            public int mutable { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class StepBindingRow
        {
            public string step_id { get; set; }
            public string destination_name { get; set; }
            public string source_kind { get; set; }
            public string literal_json { get; set; }
            public string source_name { get; set; }
            public string host_fact_id { get; set; }
            public string result_step_id { get; set; }
            public string result_name { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class FlowExitRow
        {
            public string exit_name { get; set; }
            public string contract_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ExitMappingRow
        {
            public string call_step_id { get; set; }
            public string callee_flow_id { get; set; }
            public string callee_revision { get; set; }
            public string exit_name { get; set; }
            public string transition_id { get; set; }
            public string output_mappings_json { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class AnchorSourceRow
        {
            public string anchor_id { get; set; }
            public string text_value { get; set; }
            public string text_hash { get; set; }
        }

        private sealed class AnchorRow
        {
            public string anchor_id { get; set; }
            public string language { get; set; }
            public string text_value { get; set; }
            public string text_hash { get; set; }
        }

        private sealed class AnchorLinkRow
        {
            public string link_id { get; set; }
            public string anchor_id { get; set; }
            public string owner_kind { get; set; }
            public string owner_topic_id { get; set; }
            public string owner_topic_revision { get; set; }
            public string owner_flow_id { get; set; }
            public string owner_flow_revision { get; set; }
            public string owner_dialogue_id { get; set; }
            public string owner_dialogue_revision { get; set; }
            public string canonical_owner_id { get; set; }
            public string canonical_revision { get; set; }
            public string role { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class EmbeddingProfileRow
        {
            public string profile_id { get; set; }
            public string model_id { get; set; }
            public string model_revision { get; set; }
            public string tokenizer_hash { get; set; }
            public string pipeline_hash { get; set; }
            public int dimensions { get; set; }
            public string dtype { get; set; }
            public string normalization { get; set; }
            public string metric { get; set; }
            public string profile_hash { get; set; }
        }

        private sealed class EmbeddingRow
        {
            public string anchor_id { get; set; }
            public string profile_id { get; set; }
            public byte[] vector { get; set; }
            public string vector_hash { get; set; }
            public int vector_bytes { get; set; }
        }

        private sealed class ContentJobRow
        {
            public string job_id { get; set; }
            public string idempotency_key { get; set; }
            public string kind { get; set; }
            public string source_hash { get; set; }
            public string state { get; set; }
            public string error_code { get; set; }
            public int attempts { get; set; }
        }

        private sealed class ReleaseRow
        {
            public string release_id { get; set; }
            public string manifest_json { get; set; }
            public string manifest_hash { get; set; }
            public string validator_version { get; set; }
            public string graph_fingerprint { get; set; }
            public string index_fingerprint { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ReleaseHeadRow
        {
            public string head_name { get; set; }
            public string release_id { get; set; }
            public long head_revision { get; set; }
        }

        private sealed class ReleaseDependencyRow
        {
            public string kind { get; set; }
            public string dependency_id { get; set; }
            public string revision { get; set; }
            public string store_id { get; set; }
            public string expected_hash { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class LegacySourceRow
        {
            public string source_kind { get; set; }
            public string source_path { get; set; }
            public string source_id { get; set; }
            public string source_hash { get; set; }
            public string payload_hash { get; set; }
            public string content_id { get; set; }
            public string content_revision { get; set; }
            public string metadata_json { get; set; }
            public byte[] source_bytes { get; set; }
            public string row_hash { get; set; }
        }

        private sealed class ContentConflictException : Exception
        {
            public ContentConflictException(string message) : base(message) { }
        }

        private sealed class ContentValidationException : Exception
        {
            public ContentValidationException(string message) : base(message) { }
        }
    }
}
