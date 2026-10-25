using System;
using System.IO;
using System.Text;
using AlterOS.Persistence;
using NUnit.Framework;
using SQLite;

namespace AlterOS.Tests.Persistence
{
    public sealed class ProgressionMigrationTests
    {
        private string root;
        private string databasePath;
        private string backupRoot;
        private string sourcePath;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS Migration 日本語 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            databasePath = Path.Combine(root, "new game.sqlite3");
            backupRoot = Path.Combine(root, "private backups");
            sourcePath = Path.Combine(root, "legacy progression 日本語.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SchemaFourFieldsAndExactSourceBytesSurviveOneTimeImport()
        {
            byte[] source = Encoding.UTF8.GetBytes("{\"schemaVersion\":4,\"player\":{\"credits\":77,\"currentMode\":\"free\"},\"altera\":{\"hydration\":0,\"hydrationKnown\":false},\"flags\":[{\"key\":\"fixture\",\"value\":\"kept\"}],\"unlocks\":[{\"key\":\"unlock.fixture\",\"value\":\"true\"}],\"customStats\":[{\"target\":\"altera\",\"key\":\"custom.fixture\",\"value\":4}],\"tasks\":[{\"id\":\"task.fixture\",\"label\":\"Japanese 日本語\",\"completed\":false,\"source\":\"test\",\"createdUtc\":\"2026-10-21T00:00:00Z\",\"completedUtc\":\"\"}],\"unsupportedFixtureField\":{\"value\":\"retained\"}}");
            File.WriteAllBytes(sourcePath, source);
            var repository = new SqliteGameRepository(databasePath);
            var importer = Importer(repository);

            ProgressionImportResult imported = importer.ImportOnce(sourcePath, repository);
            Assert.AreEqual(GamePersistenceStates.Imported, imported.state);
            Assert.AreEqual("source_copy_retained_unmapped_fields_not_inventoried", imported.unmappedFieldsStatus);
            CollectionAssert.AreEqual(source, File.ReadAllBytes(sourcePath));
            CollectionAssert.AreEqual(source, ReadLegacyBackup(repository, imported.sourceHash));
            StringAssert.Contains("\"credits\":77", repository.Load().progressionJson);
            StringAssert.Contains("\"hydrationKnown\":false", repository.Load().progressionJson);
            StringAssert.Contains("\"unsupportedFixtureField\"", repository.Load().progressionJson);

            long revision = repository.Load().gameRevision;
            ProgressionImportResult second = importer.ImportOnce(sourcePath, repository);
            Assert.AreEqual(GamePersistenceStates.AlreadyImported, second.state);
            Assert.AreEqual(revision, repository.Load().gameRevision);
        }

        [Test]
        public void EmptyDatabaseBootstrapStillImportsLegacySourceOnRetry()
        {
            var repository = new SqliteGameRepository(databasePath);
            Assert.IsNull(repository.Load().progressionJson);
            File.WriteAllText(sourcePath, "{\"schemaVersion\":4,\"player\":{\"credits\":12}}");

            ProgressionImportResult first = Importer(repository).ImportOnce(sourcePath, repository);
            Assert.AreEqual(GamePersistenceStates.Imported, first.state);

            var reopened = new SqliteGameRepository(databasePath);
            ProgressionImportResult retry = Importer(reopened).ImportOnce(sourcePath, reopened);
            Assert.AreEqual(GamePersistenceStates.AlreadyImported, retry.state);
            Assert.AreEqual("{\"schemaVersion\":4,\"player\":{\"credits\":12}}", reopened.Load().progressionJson);
        }

        [TestCase("{broken", "legacy_json_invalid")]
        [TestCase("{\"schemaVersion\":5,\"player\":{\"credits\":999}}", "legacy_json_invalid")]
        public void MalformedOrFutureSourceFailsClosedAndKeepsOriginalBytes(string json, string expectedCode)
        {
            byte[] source = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(sourcePath, source);
            var repository = new SqliteGameRepository(databasePath);

            ProgressionImportResult result = Importer(repository).ImportOnce(sourcePath, repository);
            Assert.AreEqual(GamePersistenceStates.RecoveryRequired, result.state);
            Assert.AreEqual(expectedCode, result.errorCode);
            Assert.IsNull(repository.Load().progressionJson);
            CollectionAssert.AreEqual(source, File.ReadAllBytes(sourcePath));
        }

        [Test]
        public void PopulatedDatabaseConflictingWithUnreceiptedLegacySourceIsNotOverwritten()
        {
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult initial = repository.Load();
            Assert.AreEqual(GamePersistenceStates.Committed,
                repository.Commit(new GameCommitRequest
                {
                    operationId = Guid.NewGuid().ToString("D"),
                    operationPayload = "fixture:initial-populated-save",
                    expectedGameRevision = initial.gameRevision,
                    progressionJson = "{\"schemaVersion\":4,\"player\":{\"credits\":31}}"
                }).state);
            File.WriteAllText(sourcePath, "{\"schemaVersion\":4,\"player\":{\"credits\":88}}");

            ProgressionImportResult result = Importer(repository).ImportOnce(sourcePath, repository);
            Assert.AreEqual(GamePersistenceStates.RecoveryRequired, result.state);
            Assert.AreEqual("legacy_source_conflicts_with_database", result.errorCode);
            StringAssert.Contains("\"credits\":31", repository.Load().progressionJson);
        }

        private ProgressionJsonImporter Importer(IGameRepository repository)
        {
            return new ProgressionJsonImporter(new GameBackupService(repository, backupRoot));
        }

        private static byte[] ReadLegacyBackup(IGameRepository repository, string sourceHash)
        {
            using (var connection = new SQLiteConnection(repository.DatabasePath))
                return connection.ExecuteScalar<byte[]>("SELECT source_bytes FROM legacy_source_backups WHERE source_hash=?", sourceHash);
        }
    }
}
