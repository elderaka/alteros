using System;
using System.IO;
using AlterOS.Persistence;
using NUnit.Framework;
using SQLite;

namespace AlterOS.Tests.Persistence
{
    public sealed class GameBackupTests
    {
        private string root;
        private string databasePath;
        private string backupRoot;
        private DateTime now;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS Game Backup Tests " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            databasePath = Path.Combine(root, "synthetic game.sqlite3");
            backupRoot = Path.Combine(root, "game only backups");
            now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void OpenWalBackupReopensAndContainsOnlyGameOwnedTables()
        {
            var repository = new SqliteGameRepository(databasePath);
            using (var connection = new SQLiteConnection(databasePath))
            {
                Assert.AreEqual("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode").ToLowerInvariant());
                connection.Execute("CREATE TABLE synthetic_game_value(value TEXT)");
                connection.Execute("INSERT INTO synthetic_game_value(value) VALUES (?)", "fixture 日本語");
            }

            GameBackupResult backup = Backups(repository).CreateBackup("test");
            Assert.AreEqual("created", backup.state);
            using (var copy = new SQLiteConnection(backup.path, SQLiteOpenFlags.ReadOnly))
            {
                Assert.AreEqual("ok", copy.ExecuteScalar<string>("PRAGMA integrity_check"));
                Assert.AreEqual("fixture 日本語", copy.ExecuteScalar<string>("SELECT value FROM synthetic_game_value"));
                Assert.AreEqual(0, copy.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND lower(name) LIKE '%memory%'"));
                Assert.AreEqual(0, copy.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND lower(name) LIKE '%transcript%'"));
                Assert.AreEqual(0, copy.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND lower(name) LIKE '%secret%'"));
            }
        }

        [Test]
        public void CheckpointsAreThrottledAndOnlyFiveRotateWhilePreMigrationRemains()
        {
            var repository = new SqliteGameRepository(databasePath);
            var backups = Backups(repository);
            Assert.AreEqual("created", backups.CreateBackup("pause").state);
            Assert.AreEqual("throttled", backups.CreateBackup("quit").state);
            for (int i = 1; i <= 5; i++)
            {
                now = now.AddMinutes(30);
                Assert.AreEqual("created", backups.CreateBackup("pause").state);
            }
            Assert.AreEqual(5, Directory.GetFiles(backupRoot, "checkpoint-*.sqlite3").Length);

            byte[] legacy = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":4,\"player\":{\"credits\":2}}");
            string source = Path.Combine(root, "legacy source.json");
            File.WriteAllBytes(source, legacy);
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(legacy)).Replace("-", string.Empty).ToLowerInvariant();
            Assert.AreEqual("created", backups.CreatePreMigrationBackup(source, hash, legacy).state);
            Assert.AreEqual(1, Directory.GetFiles(backupRoot, "pre-migration-*.sqlite3").Length);
            CollectionAssert.AreEqual(legacy, File.ReadAllBytes(Path.Combine(backupRoot, "pre-migration-" + hash + ".legacy.json")));
        }

        [Test]
        public void CorruptAndPathTraversalCandidatesAreRejectedBeforeRestore()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult current = repository.Load();
            repository.Commit(new GameCommitRequest
            {
                operationId = Guid.NewGuid().ToString("D"), expectedGameRevision = current.gameRevision,
                operationPayload = "{\"schemaVersion\":4,\"value\":1}",
                progressionJson = "{\"schemaVersion\":4,\"value\":1}"
            });
            var backups = Backups(repository);
            GameBackupResult backup = backups.CreateBackup("manual");
            Assert.AreEqual("invalid", backups.StageRecovery("..\\outside").state);

            string badPath = Path.Combine(backupRoot, "checkpoint-tampered.sqlite3");
            File.WriteAllBytes(badPath, new byte[] { 1, 2, 3, 4 });
            Assert.AreEqual(GamePersistenceStates.RecoveryRequired, backups.StageRecovery("checkpoint-tampered").state);
            Assert.AreEqual("{\"schemaVersion\":4,\"value\":1}", repository.Load().progressionJson);

            GameRecoveryResult staged = backups.StageRecovery(backup.backupId);
            Assert.AreEqual(GamePersistenceStates.RecoveryStaged, staged.state);
            Assert.AreEqual(GamePersistenceStates.Invalid, backups.ApplyRecovery(staged.candidate, false).state);
        }

        [Test]
        public void FailedCandidateReplacementRetainsOriginalAndRestoreNeverReplaysAnOperation()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult initial = repository.Load();
            string firstOperation = Guid.NewGuid().ToString("D");
            GameCommitRequest firstRequest = new GameCommitRequest
            {
                operationId = firstOperation, expectedGameRevision = initial.gameRevision,
                operationPayload = "{\"schemaVersion\":4,\"value\":1}",
                progressionJson = "{\"schemaVersion\":4,\"value\":1}"
            };
            Assert.AreEqual(GamePersistenceStates.Committed, repository.Commit(firstRequest).state);
            var backups = Backups(repository);
            GameBackupResult beforeSecond = backups.CreateBackup("checkpoint");
            GameLoadResult afterFirst = repository.Load();
            repository.Commit(new GameCommitRequest
            {
                operationId = Guid.NewGuid().ToString("D"), expectedGameRevision = afterFirst.gameRevision,
                operationPayload = "{\"schemaVersion\":4,\"value\":2}",
                progressionJson = "{\"schemaVersion\":4,\"value\":2}"
            });

            GameRecoveryResult staged = backups.StageRecovery(beforeSecond.backupId);
            Assert.AreEqual(GamePersistenceStates.RecoveryStaged, staged.state);
            var stagedPath = (string)typeof(GameRecoveryCandidate)
                .GetField("stagedPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(staged.candidate);
            File.WriteAllBytes(stagedPath, new byte[] { 4, 3, 2, 1 });
            GameRecoveryResult failed = backups.ApplyRecovery(staged.candidate, true);
            Assert.AreEqual(GamePersistenceStates.Unavailable, failed.state);
            Assert.AreEqual("{\"schemaVersion\":4,\"value\":2}", new SqliteGameRepository(databasePath).Load().progressionJson);

            GameCommitResult duplicate = new SqliteGameRepository(databasePath).Commit(firstRequest);
            Assert.AreEqual(GamePersistenceStates.Duplicate, duplicate.state);
            Assert.AreEqual(1, duplicate.receipt.gameRevision);
        }

        private GameBackupService Backups(IGameRepository repository)
        {
            return new GameBackupService(repository, backupRoot, () => now);
        }
    }
}
