using System;
using System.IO;
using AlterOS.Persistence;
using NUnit.Framework;
using SQLite;

namespace AlterOS.Tests.Persistence
{
    public sealed class GameRepositoryTests
    {
        private string root;
        private string databasePath;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS Game Repository Tests " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            databasePath = Path.Combine(root, "synthetic game.sqlite3");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void FreshIdentitySurvivesReopenAndUnknownHydrationStaysUnknown()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult first = repository.Load();
            Assert.AreEqual(GamePersistenceStates.Ready, first.state);
            Assert.IsNotNull(first.identity.saveId);
            Assert.IsNull(first.progressionJson);

            string progression = "{\"schemaVersion\":4,\"altera\":{\"hydration\":0,\"hydrationKnown\":false,\"vendorField\":\"kept\"},\"customStats\":[{\"target\":\"altera\",\"key\":\"unknown\",\"value\":3}]}";
            GameCommitResult committed = repository.Commit(Request(first, Guid.NewGuid().ToString("D"), progression));
            Assert.AreEqual(GamePersistenceStates.Committed, committed.state);

            GameLoadResult reopened = new SqliteGameRepository(databasePath).Load();
            Assert.AreEqual(first.identity.saveId, reopened.identity.saveId);
            Assert.AreEqual(first.identity.alterraId, reopened.identity.alterraId);
            Assert.AreEqual(first.identity.homeId, reopened.identity.homeId);
            Assert.AreEqual(progression, reopened.progressionJson);
            StringAssert.Contains("\"hydrationKnown\":false", reopened.progressionJson);
            StringAssert.Contains("\"vendorField\":\"kept\"", reopened.progressionJson);
        }

        [Test]
        public void StateEventAndReceiptRollbackTogetherWhenCommitFails()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult before = repository.Load();
            using (var connection = new SQLiteConnection(databasePath))
                connection.Execute("CREATE TRIGGER synthetic_abort BEFORE INSERT ON operation_receipts BEGIN SELECT RAISE(ABORT,'synthetic pre-commit failure'); END");

            string operationId = Guid.NewGuid().ToString("D");
            GameCommitRequest request = Request(before, operationId, "{\"schemaVersion\":4}");
            request.events.Add(new GameEventRecord
            {
                eventId = Guid.NewGuid().ToString("D"), operationId = operationId, kind = "synthetic",
                actorId = "host", targetId = "alterra", occurredAtUtc = "2026-10-21T00:00:00.0000000Z",
                payloadJson = "{\"source\":\"test\"}"
            });
            GameCommitResult failed = repository.Commit(request);
            Assert.AreEqual(GamePersistenceStates.Unavailable, failed.state);

            GameLoadResult after = new SqliteGameRepository(databasePath).Load();
            Assert.AreEqual(before.gameRevision, after.gameRevision);
            Assert.IsNull(after.progressionJson);
            using (var connection = new SQLiteConnection(databasePath))
            {
                Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM game_events"));
                Assert.AreEqual(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM operation_receipts"));
            }
        }

        [Test]
        public void ReceiptIsCheckedBeforeStaleRevisionAndConflictingReuseIsRejected()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult initial = repository.Load();
            string operationId = Guid.NewGuid().ToString("D");
            GameCommitRequest original = Request(initial, operationId, "{\"schemaVersion\":4,\"value\":1}");
            GameCommitResult first = repository.Commit(original);
            Assert.AreEqual(GamePersistenceStates.Committed, first.state);

            GameCommitResult retry = repository.Commit(original);
            Assert.AreEqual(GamePersistenceStates.Duplicate, retry.state);
            Assert.AreEqual(first.receipt.gameRevision, retry.receipt.gameRevision);

            GameCommitRequest changed = Request(initial, operationId, "{\"schemaVersion\":4,\"value\":2}");
            Assert.AreEqual(GamePersistenceStates.ConflictingOperation, repository.Commit(changed).state);

            GameCommitRequest stale = Request(initial, Guid.NewGuid().ToString("D"), "{\"schemaVersion\":4,\"value\":3}");
            Assert.AreEqual(GamePersistenceStates.StaleRevision, repository.Commit(stale).state);
            Assert.AreEqual(first.receipt.gameRevision, repository.Load().gameRevision);
            Assert.AreEqual("{\"schemaVersion\":4,\"value\":1}", repository.Load().progressionJson);
        }

        [Test]
        public void LockedWriterReturnsUnavailableWithoutAdvancingTheRevision()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult before = repository.Load();
            using (var blocker = new SQLiteConnection(databasePath))
            {
                blocker.Execute("BEGIN EXCLUSIVE");
                GameCommitResult failed = repository.Commit(Request(before, Guid.NewGuid().ToString("D"), "{\"schemaVersion\":4}"));
                Assert.AreEqual(GamePersistenceStates.Unavailable, failed.state);
                blocker.Execute("ROLLBACK");
            }
            Assert.AreEqual(before.gameRevision, repository.Load().gameRevision);
        }

        [Test]
        public void FutureSchemaAndCorruptBytesAreRejectedWithoutChangingTheSource()
        {
            string futurePath = Path.Combine(root, "future.sqlite3");
            using (var connection = new SQLiteConnection(futurePath))
                connection.Execute("PRAGMA user_version=999");
            byte[] futureBefore = File.ReadAllBytes(futurePath);
            Assert.AreEqual(GamePersistenceStates.RecoveryRequired, new SqliteGameRepository(futurePath).Load().state);
            CollectionAssert.AreEqual(futureBefore, File.ReadAllBytes(futurePath));

            string corruptPath = Path.Combine(root, "corrupt.sqlite3");
            byte[] corruptBytes = { 0, 1, 2, 3, 4, 5, 255, 0, 9 };
            File.WriteAllBytes(corruptPath, corruptBytes);
            Assert.AreEqual(GamePersistenceStates.RecoveryRequired, new SqliteGameRepository(corruptPath).Load().state);
            CollectionAssert.AreEqual(corruptBytes, File.ReadAllBytes(corruptPath));
        }

        private static GameCommitRequest Request(GameLoadResult current, string operationId, string progressionJson)
        {
            return new GameCommitRequest
            {
                operationId = operationId,
                operationPayload = progressionJson,
                expectedGameRevision = current.gameRevision,
                progressionJson = progressionJson
            };
        }
    }
}
