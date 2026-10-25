using System;
using System.IO;
using System.Runtime.Serialization.Json;
using AlterOS.Furniture;
using AlterOS.Persistence;
using NUnit.Framework;
using SQLite;

namespace AlterOS.Tests.Persistence
{
    public sealed class FurniturePersistenceTests
    {
        private string root;
        private string databasePath;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS Furniture Persistence " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            databasePath = Path.Combine(root, "synthetic home.sqlite3");
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult initial = repository.Load();
            Assert.AreEqual(GamePersistenceStates.Committed, repository.Commit(new GameCommitRequest
            {
                operationId = Guid.NewGuid().ToString("D"),
                operationPayload = "{\"schemaVersion\":4}",
                expectedGameRevision = initial.gameRevision,
                progressionJson = "{\"schemaVersion\":4}"
            }).state);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SeedIsImportedOnceAndExactPoseAndOrphansRoundTrip()
        {
            var repository = new SqliteGameRepository(databasePath);
            string homeId = repository.Load().identity.homeId;
            int seedCalls = 0;
            var adapter = new FurniturePersistenceAdapter(repository, id =>
            {
                seedCalls++;
                return Layout(id, "seed-v1", new FurnitureVector3 { x = 1.23456789, y = 0.125, z = -9.75 });
            });

            FurniturePersistenceLoadResult seeded = adapter.LoadHome(homeId);
            Assert.AreEqual("loaded", seeded.state);
            Assert.AreEqual(1, seedCalls);
            Assert.AreEqual(1, seeded.layoutRevision);
            HomeFurnitureLayoutDocument edit = CloneLayout(seeded.document);
            edit.instances[0].positionMeters.x = 1.23456789;
            edit.instances[0].rotation = new FurnitureQuaternion { x = 0, y = 0.382683432365, z = 0, w = 0.923879532511 };
            edit.instances[0].support.supportId = "missing.support.remains.inert";

            FurniturePersistenceCommitResult committed = adapter.CommitLayout(
                Guid.NewGuid().ToString("D"), seeded.gameRevision, seeded.layoutRevision, edit);
            Assert.AreEqual(GamePersistenceStates.Committed, committed.state);
            Assert.AreEqual(2, committed.layoutRevision);

            int replacementSeedCalls = 0;
            var reopenedAdapter = new FurniturePersistenceAdapter(new SqliteGameRepository(databasePath), id =>
            {
                replacementSeedCalls++;
                return Layout(id, "seed-v2", new FurnitureVector3 { x = -10, y = 0, z = 0 });
            });
            FurniturePersistenceLoadResult resumed = reopenedAdapter.LoadHome(homeId);
            Assert.AreEqual("loaded", resumed.state);
            Assert.AreEqual(0, replacementSeedCalls);
            Assert.AreEqual(2, resumed.layoutRevision);
            Assert.AreEqual("seed-v1", resumed.document.seedVersion);
            Assert.AreEqual("furniture.missing.definition", resumed.document.instances[0].definitionId);
            Assert.AreEqual("missing.support.remains.inert", resumed.document.instances[0].support.supportId);
            Assert.AreEqual(1.23456789, resumed.document.instances[0].positionMeters.x, 0.000000001);
            Assert.AreEqual(0.382683432365, resumed.document.instances[0].rotation.y, 0.000000000001);
            Assert.AreEqual(0.923879532511, resumed.document.instances[0].rotation.w, 0.000000000001);
        }

        [Test]
        public void FailedLayoutCommitDoesNotPublishTheCandidate()
        {
            var repository = new SqliteGameRepository(databasePath);
            string homeId = repository.Load().identity.homeId;
            var adapter = new FurniturePersistenceAdapter(repository, id => Layout(id, "seed-v1", new FurnitureVector3()));
            FurniturePersistenceLoadResult before = adapter.LoadHome(homeId);
            HomeFurnitureLayoutDocument candidate = CloneLayout(before.document);
            candidate.instances[0].positionMeters.x = 3;
            using (var connection = new SQLiteConnection(databasePath))
                connection.Execute("CREATE TRIGGER synthetic_layout_abort BEFORE INSERT ON operation_receipts BEGIN SELECT RAISE(ABORT,'synthetic layout failure'); END");

            FurniturePersistenceCommitResult failed = adapter.CommitLayout(
                Guid.NewGuid().ToString("D"), before.gameRevision, before.layoutRevision, candidate);
            Assert.AreEqual(GamePersistenceStates.Unavailable, failed.state);

            FurniturePersistenceLoadResult after = new FurniturePersistenceAdapter(
                new SqliteGameRepository(databasePath), id => Layout(id, "seed-v2", new FurnitureVector3 { x = 8 })).LoadHome(homeId);
            Assert.AreEqual(before.layoutRevision, after.layoutRevision);
            Assert.AreEqual(0, after.document.instances[0].positionMeters.x);
            Assert.AreEqual("seed-v1", after.document.seedVersion);
        }

        private static HomeFurnitureLayoutDocument Layout(string homeId, string seedVersion, FurnitureVector3 position)
        {
            return new HomeFurnitureLayoutDocument
            {
                schemaVersion = 1,
                homeId = homeId,
                roomId = "room.synthetic",
                catalogueVersion = "1.0.0",
                seedVersion = seedVersion,
                coordinateFrame = "room_local_y_up_positive_z_forward",
                lengthUnit = "m",
                layoutRevision = 0,
                instances = new System.Collections.Generic.List<HomeFurnitureLayoutInstance>
                {
                    new HomeFurnitureLayoutInstance
                    {
                        instanceId = "furniture.synthetic.1",
                        definitionId = "furniture.missing.definition",
                        positionMeters = position,
                        rotation = new FurnitureQuaternion { x = 0, y = 0, z = 0, w = 1 },
                        scale = new FurnitureVector3 { x = 1, y = 1, z = 1 },
                        support = new FurnitureLayoutSupportReference
                        {
                            supportId = "room.synthetic.floor", supportKind = "roomFloor", supportFrameRevision = "1",
                            attachmentPointId = "origin", localPositionMeters = new FurnitureVector3(),
                            localRotation = new FurnitureQuaternion { x = 0, y = 0, z = 0, w = 1 }
                        },
                        enabled = true,
                        conditionState = "good",
                        resourceState = new System.Collections.Generic.List<FurnitureResourceState>(),
                        instanceRevision = 1
                    }
                }
            };
        }

        private static HomeFurnitureLayoutDocument CloneLayout(HomeFurnitureLayoutDocument document)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument));
                serializer.WriteObject(stream, document);
                stream.Position = 0;
                return (HomeFurnitureLayoutDocument)serializer.ReadObject(stream);
            }
        }
    }
}
