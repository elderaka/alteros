using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AlterOS.Persistence;

namespace AlterOS.Persistence
{
    public sealed class GameRecoveryCandidate
    {
        internal string stagedPath;
        public string backupId;
        public GameSaveIdentity identity;
        public long gameRevision;
        public long layoutRevision;
    }

    public sealed class GameBackupService
    {
        private static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(30);
        private const int RotatingCheckpointCount = 5;
        private readonly object gate = new object();
        private readonly IGameRepository repository;
        private readonly string backupRoot;
        private readonly string stagingRoot;
        private readonly Func<DateTime> utcNow;

        public GameBackupService(IGameRepository repository, string backupRoot, Func<DateTime> utcNow = null)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrWhiteSpace(backupRoot)) throw new ArgumentException("A trusted backup directory is required.", nameof(backupRoot));
            this.repository = repository;
            this.backupRoot = Path.GetFullPath(backupRoot);
            stagingRoot = Path.Combine(this.backupRoot, "staging");
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            Directory.CreateDirectory(this.backupRoot);
        }

        public GameBackupResult CreateBackup(string reason)
        {
            if (!IsSafeReason(reason))
                return FailedBackup(GamePersistenceStates.Invalid, "invalid_backup_reason", "Backup reason contains unsupported characters.");

            lock (gate)
            {
                DateTime now = AsUtc(utcNow());
                string markerPath = Path.Combine(backupRoot, "checkpoint-last-utc.txt");
                DateTime previous;
                if (File.Exists(markerPath) && DateTime.TryParse(File.ReadAllText(markerPath), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out previous))
                {
                    previous = AsUtc(previous);
                    if (now < previous || now - previous < CheckpointInterval)
                        return FailedBackup("throttled", "checkpoint_interval", "A rotating checkpoint was created within the last 30 minutes.");
                }

                string backupId = "checkpoint-" + now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string path = Path.Combine(backupRoot, backupId + ".sqlite3");
                if (!repository.CreateConsistentBackup(path))
                    return FailedBackup(GamePersistenceStates.Unavailable, "checkpoint_failed", "SQLite could not create a consistent game-only backup.");

                try
                {
                    File.SetLastWriteTimeUtc(path, now);
                    File.WriteAllText(markerPath, now.ToString("O", CultureInfo.InvariantCulture));
                    RotateCheckpoints();
                    return new GameBackupResult { state = "created", backupId = backupId, path = path };
                }
                catch (Exception exception)
                {
                    return FailedBackup(GamePersistenceStates.Unavailable, "checkpoint_retention_failed", exception.Message, backupId, path);
                }
            }
        }

        public GameBackupResult CreatePreMigrationBackup(string sourcePath, string sourceHash, byte[] sourceBytes)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !IsSha256(sourceHash) || sourceBytes == null)
                return FailedBackup(GamePersistenceStates.Invalid, "invalid_migration_backup", "A legacy source path, SHA-256 and source bytes are required.");
            try
            {
                byte[] currentSource = File.ReadAllBytes(sourcePath);
                if (!BytesEqual(currentSource, sourceBytes) || !string.Equals(HashFile(sourcePath), sourceHash, StringComparison.OrdinalIgnoreCase))
                    return FailedBackup(GamePersistenceStates.RecoveryRequired, "legacy_source_changed", "The legacy source changed while migration was being prepared.");
            }
            catch (Exception exception)
            {
                return FailedBackup(GamePersistenceStates.Unavailable, "legacy_source_unavailable", exception.Message);
            }

            lock (gate)
            {
                string backupId = "pre-migration-" + sourceHash.ToLowerInvariant();
                string databasePath = Path.Combine(backupRoot, backupId + ".sqlite3");
                string sourceBackupPath = Path.Combine(backupRoot, backupId + ".legacy.json");
                if (File.Exists(databasePath) && File.Exists(sourceBackupPath))
                    return new GameBackupResult { state = "created", backupId = backupId, path = databasePath };

                string tempDatabase = databasePath + ".tmp-" + Guid.NewGuid().ToString("N");
                string tempSource = sourceBackupPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    if (!repository.CreateConsistentBackup(tempDatabase))
                    {
                        GameBackupValidationResult diagnostic = repository.ValidateBackup(tempDatabase);
                        return FailedBackup(GamePersistenceStates.Unavailable, "pre_migration_backup_failed",
                            "SQLite could not create the pre-migration checkpoint (" + diagnostic.errorCode + ": " +
                            diagnostic.message + ").");
                    }
                    File.WriteAllBytes(tempSource, sourceBytes);
                    if (!string.Equals(HashFile(tempSource), sourceHash, StringComparison.OrdinalIgnoreCase))
                        return FailedBackup(GamePersistenceStates.Unavailable, "pre_migration_copy_mismatch", "The retained legacy source copy did not match the source hash.");
                    if (!File.Exists(databasePath)) File.Move(tempDatabase, databasePath);
                    if (!File.Exists(sourceBackupPath)) File.Move(tempSource, sourceBackupPath);
                    return new GameBackupResult { state = "created", backupId = backupId, path = databasePath };
                }
                catch (Exception exception)
                {
                    return FailedBackup(GamePersistenceStates.Unavailable, "pre_migration_backup_failed", exception.Message, backupId, databasePath);
                }
                finally
                {
                    if (File.Exists(tempDatabase)) File.Delete(tempDatabase);
                    if (File.Exists(tempSource)) File.Delete(tempSource);
                }
            }
        }

        public GameRecoveryResult StageRecovery(string backupId)
        {
            string sourcePath;
            if (!TryGetBackupPath(backupId, out sourcePath))
                return FailedRecovery(GamePersistenceStates.Invalid, "invalid_backup_id", "Recovery accepts a known backup ID, not a filesystem path.");

            GameBackupValidationResult source = repository.ValidateBackup(sourcePath);
            if (source.state != GamePersistenceStates.Ready)
                return FailedRecovery(GamePersistenceStates.RecoveryRequired, source.errorCode, source.message);

            GameLoadResult current = repository.Load();
            if (current.state != GamePersistenceStates.Ready || !SameIdentity(current.identity, source.identity))
                return FailedRecovery(GamePersistenceStates.RecoveryRequired, "backup_identity_mismatch", "The candidate does not belong to the current local save identity.");

            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(stagingRoot);
                    string stagedPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N") + ".sqlite3");
                    File.Copy(sourcePath, stagedPath, false);
                    GameBackupValidationResult staged = repository.ValidateBackup(stagedPath);
                    if (staged.state != GamePersistenceStates.Ready || !SameIdentity(current.identity, staged.identity))
                    {
                        File.Delete(stagedPath);
                        return FailedRecovery(GamePersistenceStates.RecoveryRequired, "staged_backup_invalid", "The staged candidate failed validation.");
                    }

                    return new GameRecoveryResult
                    {
                        state = GamePersistenceStates.RecoveryStaged,
                        candidate = new GameRecoveryCandidate
                        {
                            stagedPath = stagedPath,
                            backupId = backupId,
                            identity = staged.identity,
                            gameRevision = staged.gameRevision,
                            layoutRevision = staged.layoutRevision
                        }
                    };
                }
                catch (Exception exception)
                {
                    return FailedRecovery(GamePersistenceStates.Unavailable, "recovery_stage_failed", exception.Message);
                }
            }
        }

        public GameRecoveryResult ApplyRecovery(GameRecoveryCandidate candidate, bool explicitlyApproved)
        {
            if (!explicitlyApproved)
                return FailedRecovery(GamePersistenceStates.Invalid, "approval_required", "Recovery requires explicit approval.");
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.stagedPath) ||
                !IsWithin(candidate.stagedPath, stagingRoot) || !File.Exists(candidate.stagedPath))
                return FailedRecovery(GamePersistenceStates.Invalid, "invalid_candidate", "Only a candidate staged by this service can be applied.");
            if (!(repository is SqliteGameRepository sqliteRepository))
                return FailedRecovery(GamePersistenceStates.Unavailable, "unsupported_repository", "This repository does not support staged replacement.");

            GameLoadResult current = repository.Load();
            if (current.state != GamePersistenceStates.Ready || !SameIdentity(current.identity, candidate.identity))
                return FailedRecovery(GamePersistenceStates.RecoveryRequired, "backup_identity_mismatch", "The current save identity changed after staging.");

            string safetyId = "recovery-before-" + AsUtc(utcNow()).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string safetyPath = Path.Combine(backupRoot, safetyId + ".sqlite3");
            if (!repository.CreateConsistentBackup(safetyPath))
                return FailedRecovery(GamePersistenceStates.Unavailable, "safety_backup_failed", "The current database could not be retained before recovery.");

            lock (gate)
            {
                if (!sqliteRepository.ReplaceFromBackup(candidate.stagedPath))
                    return FailedRecovery(GamePersistenceStates.Unavailable, "recovery_replace_failed",
                        "The staged candidate could not replace the live database; the original was retained.");
                GameLoadResult recovered = repository.Load();
                if (recovered.state != GamePersistenceStates.Ready || !SameIdentity(candidate.identity, recovered.identity) ||
                    recovered.gameRevision != candidate.gameRevision || recovered.layoutRevision != candidate.layoutRevision)
                {
                    sqliteRepository.ReplaceFromBackup(safetyPath);
                    return FailedRecovery(GamePersistenceStates.RecoveryRequired, "recovery_verification_failed",
                        "The staged database failed its post-replacement check; the retained original was restored.");
                }
                try { File.Delete(candidate.stagedPath); }
                catch { }
                return new GameRecoveryResult
                {
                    state = GamePersistenceStates.Recovered,
                    candidate = candidate
                };
            }
        }

        private void RotateCheckpoints()
        {
            var checkpoints = Directory.GetFiles(backupRoot, "checkpoint-*.sqlite3")
                .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            foreach (string path in checkpoints.Skip(RotatingCheckpointCount)) File.Delete(path);
        }

        private bool TryGetBackupPath(string backupId, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(backupId) || backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                backupId.Contains("..") || backupId.Contains("/") || backupId.Contains("\\")) return false;
            if (!(backupId.StartsWith("checkpoint-", StringComparison.Ordinal) ||
                backupId.StartsWith("pre-migration-", StringComparison.Ordinal) ||
                backupId.StartsWith("recovery-before-", StringComparison.Ordinal))) return false;
            string candidate = Path.GetFullPath(Path.Combine(backupRoot, backupId + ".sqlite3"));
            if (!IsWithin(candidate, backupRoot) || !File.Exists(candidate)) return false;
            path = candidate;
            return true;
        }

        private static bool IsWithin(string path, string parent)
        {
            string fullPath = Path.GetFullPath(path);
            string fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameIdentity(GameSaveIdentity left, GameSaveIdentity right)
        {
            return left != null && right != null && left.saveId == right.saveId &&
                left.alterraId == right.alterraId && left.homeId == right.homeId;
        }

        private static bool IsSafeReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 32) return false;
            foreach (char value in reason)
                if (!(char.IsLetterOrDigit(value) || value == '-' || value == '_')) return false;
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static DateTime AsUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static GameBackupResult FailedBackup(string state, string code, string message, string backupId = null, string path = null)
        {
            return new GameBackupResult { state = state, errorCode = code, message = message, backupId = backupId, path = path };
        }

        private static GameRecoveryResult FailedRecovery(string state, string code, string message)
        {
            return new GameRecoveryResult { state = state, errorCode = code, message = message };
        }
    }
}
