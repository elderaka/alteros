using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace AlterOS.Persistence
{
    public sealed class ProgressionJsonImporter
    {
        private const int SupportedProgressionSchema = 4;
        private const int MaximumLegacyBytes = 1048576;
        private readonly GameBackupService backupService;

        public ProgressionJsonImporter(GameBackupService backupService)
        {
            if (backupService == null) throw new ArgumentNullException(nameof(backupService));
            this.backupService = backupService;
        }

        public ProgressionImportResult ImportOnce(string sourcePath, IGameRepository repository)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return new ProgressionImportResult { state = GamePersistenceStates.NoSource };

            byte[] sourceBytes;
            string sourceJson;
            string sourceHash;
            int schemaVersion;
            try
            {
                sourceBytes = File.ReadAllBytes(sourcePath);
                if (sourceBytes.Length > MaximumLegacyBytes)
                    throw new InvalidDataException("Legacy progression JSON exceeds the supported size limit.");
                sourceHash = Hash(sourceBytes);
                sourceJson = new UTF8Encoding(false, true).GetString(sourceBytes).TrimStart('\uFEFF');
                schemaVersion = ReadSchemaVersion(sourceBytes);
            }
            catch (Exception exception)
            {
                return Failed(GamePersistenceStates.RecoveryRequired, "legacy_json_invalid", SafeMessage(exception));
            }

            GameMigrationReceipt migration = repository.FindMigration(sourceHash);
            GameLoadResult current = repository.Load();
            if (current.state != GamePersistenceStates.Ready)
                return Failed(current.state, current.errorCode, current.message, sourceHash, schemaVersion);
            if (migration != null)
            {
                if (current.progressionJson == null)
                    return Failed(GamePersistenceStates.RecoveryRequired, "migration_snapshot_missing",
                        "A migration receipt exists but its progression snapshot is missing.", sourceHash, schemaVersion);
                return new ProgressionImportResult
                {
                    state = GamePersistenceStates.AlreadyImported,
                    sourceHash = sourceHash,
                    sourceSchemaVersion = schemaVersion,
                    unmappedFieldsStatus = "source_copy_retained_unmapped_fields_not_inventoried"
                };
            }
            if (current.progressionJson != null)
                return Failed(GamePersistenceStates.RecoveryRequired, "legacy_source_conflicts_with_database",
                    "A populated game database and an unreceipted legacy source both exist; automatic import stopped.", sourceHash, schemaVersion);

            GameBackupResult backup = backupService.CreatePreMigrationBackup(sourcePath, sourceHash, sourceBytes);
            if (backup.state != "created")
                return Failed(backup.state, backup.errorCode, backup.message, sourceHash, schemaVersion);
            if (!File.Exists(sourcePath) || !string.Equals(Hash(File.ReadAllBytes(sourcePath)), sourceHash, StringComparison.OrdinalIgnoreCase))
                return Failed(GamePersistenceStates.RecoveryRequired, "legacy_source_changed",
                    "The legacy source changed after its pre-migration backup was prepared.", sourceHash, schemaVersion);

            string operationId = Guid.NewGuid().ToString("D");
            string now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            var request = new GameCommitRequest
            {
                operationId = operationId,
                operationPayload = "migration:" + sourceHash,
                expectedGameRevision = current.gameRevision,
                progressionJson = sourceJson,
                migrationSourceHash = sourceHash,
                migrationSourceBytes = sourceBytes
            };
            request.events.Add(new GameEventRecord
            {
                eventId = Guid.NewGuid().ToString("D"),
                operationId = operationId,
                kind = "progression_legacy_imported",
                actorId = "host",
                targetId = current.identity.saveId,
                occurredAtUtc = now,
                recordedAtUtc = now,
                schemaVersion = 1,
                payloadJson = "{\"sourceHash\":\"" + sourceHash + "\",\"sourceSchemaVersion\":" + schemaVersion + "}"
            });

            GameCommitResult commit = repository.Commit(request);
            if (commit.state == GamePersistenceStates.Committed)
            {
                return new ProgressionImportResult
                {
                    state = GamePersistenceStates.Imported,
                    sourceHash = sourceHash,
                    sourceSchemaVersion = schemaVersion,
                    unmappedFieldsStatus = "source_copy_retained_unmapped_fields_not_inventoried",
                    commit = commit
                };
            }

            GameMigrationReceipt committedReceipt = repository.FindMigration(sourceHash);
            GameLoadResult after = repository.Load();
            if (committedReceipt != null && after.state == GamePersistenceStates.Ready && after.progressionJson != null)
            {
                return new ProgressionImportResult
                {
                    state = GamePersistenceStates.AlreadyImported,
                    sourceHash = sourceHash,
                    sourceSchemaVersion = schemaVersion,
                    unmappedFieldsStatus = "source_copy_retained_unmapped_fields_not_inventoried",
                    commit = commit
                };
            }

            return Failed(commit.state, commit.errorCode, commit.message, sourceHash, schemaVersion, commit);
        }

        private static int ReadSchemaVersion(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidDataException("Legacy progression JSON is empty.");
            using (var stream = new MemoryStream(bytes, false))
            {
                var serializer = new DataContractJsonSerializer(typeof(LegacyProgressionHeader));
                var header = serializer.ReadObject(stream) as LegacyProgressionHeader;
                if (header == null) throw new InvalidDataException("Legacy progression JSON did not contain an object.");
                if (header.schemaVersion < 0 || header.schemaVersion > SupportedProgressionSchema)
                    throw new InvalidDataException("Legacy progression schema " + header.schemaVersion + " is not supported.");
                return header.schemaVersion;
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var hex = new StringBuilder(64);
                foreach (byte value in sha.ComputeHash(bytes)) hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static ProgressionImportResult Failed(string state, string code, string message,
            string sourceHash = null, int schemaVersion = 0, GameCommitResult commit = null)
        {
            return new ProgressionImportResult
            {
                state = state,
                sourceHash = sourceHash,
                sourceSchemaVersion = schemaVersion,
                commit = commit,
                errorCode = code,
                message = message,
                unmappedFieldsStatus = sourceHash == null ? null : "source_copy_retained_unmapped_fields_not_inventoried"
            };
        }

        private static string SafeMessage(Exception exception)
        {
            return exception == null ? "Legacy progression input is invalid." : exception.Message;
        }

        [DataContract]
        private sealed class LegacyProgressionHeader
        {
            [DataMember(Name = "schemaVersion", Order = 0)] public int schemaVersion;
        }
    }
}
