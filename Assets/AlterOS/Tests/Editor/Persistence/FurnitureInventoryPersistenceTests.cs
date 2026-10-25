using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using AlterOS.Furniture;
using AlterOS.Persistence;
using NUnit.Framework;
using SQLite;

namespace AlterOS.Tests.Persistence
{
    public sealed class FurnitureInventoryPersistenceTests
    {
        private string root;
        private string databasePath;
        private string homeId;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS Furniture Inventory Persistence " + Guid.NewGuid().ToString("N"));
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

            GameLoadResult current = repository.Load();
            homeId = current.identity.homeId;
            HomeFurnitureLayoutDocument legacy = Layout(homeId);
            legacy.layoutRevision = current.layoutRevision + 1;
            Assert.AreEqual(GamePersistenceStates.Committed, repository.Commit(new GameCommitRequest
            {
                operationId = Guid.NewGuid().ToString("D"),
                operationPayload = "legacy-layout",
                expectedGameRevision = current.gameRevision,
                expectedLayoutRevision = current.layoutRevision,
                progressionJson = current.progressionJson,
                layoutJson = Serialize(legacy)
            }).state);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SchemaOneHomeInitializesInventoryOnceAndPreservesPlacedFurniture()
        {
            var repository = new SqliteGameRepository(databasePath);
            int seedCalls = 0;
            var adapter = new FurniturePersistenceAdapter(repository, id =>
            {
                seedCalls++;
                return Layout(id);
            });

            FurniturePersistenceLoadResult upgraded = adapter.LoadHome(homeId);
            Assert.AreEqual("loaded", upgraded.state);
            Assert.AreEqual(2, upgraded.document.schemaVersion);
            Assert.AreEqual(2, upgraded.layoutRevision);
            Assert.AreEqual(0, seedCalls);
            Assert.AreEqual("instance.synthetic", upgraded.document.instances[0].instanceId);
            Assert.AreEqual("furniture.missing.definition", upgraded.document.instances[0].definitionId);
            Assert.AreEqual("room.synthetic.floor", upgraded.document.instances[0].support.supportId);
            Assert.AreEqual(1.23456789, upgraded.document.instances[0].positionMeters.x, 0.000000001);
            Assert.AreEqual(8, upgraded.document.inventory.Count);
            Assert.That(upgraded.document.inventory.All(entry => entry != null && entry.storedCount == 10));

            FurniturePersistenceLoadResult reopened = new FurniturePersistenceAdapter(
                new SqliteGameRepository(databasePath)).LoadHome(homeId);
            Assert.AreEqual("loaded", reopened.state);
            Assert.AreEqual(upgraded.gameRevision, reopened.gameRevision);
            Assert.AreEqual(upgraded.layoutRevision, reopened.layoutRevision);
            Assert.AreEqual(8, reopened.document.inventory.Count);
            Assert.That(reopened.document.inventory.All(entry => entry != null && entry.storedCount == 10));
            using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
                Assert.AreEqual(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM game_events WHERE kind='home_inventory_initialized'"));
        }

        [Test]
        public void AdapterImplementsHomeStoreAndDuplicateReturnsCurrentDurableSnapshot()
        {
            IFurnitureHomeStore store = new FurniturePersistenceAdapter(new SqliteGameRepository(databasePath));
            FurnitureHomeReadResult before = store.Read();
            Assert.AreEqual("loaded", before.state);
            Assert.AreEqual(2, before.document.schemaVersion);

            HomeFurnitureLayoutDocument firstCandidate = Clone(before.document);
            firstCandidate.instances[0].positionMeters.x = 2;
            string firstOperationId = Guid.NewGuid().ToString("D");
            FurnitureHomeCommitResult first = store.Commit(firstOperationId, before.gameRevision,
                before.layoutRevision, firstCandidate);
            Assert.AreEqual(GamePersistenceStates.Committed, first.state);
            Assert.IsTrue(first.durable);

            HomeFurnitureLayoutDocument secondCandidate = Clone(first.document);
            secondCandidate.instances[0].positionMeters.x = 3;
            FurnitureHomeCommitResult second = store.Commit(Guid.NewGuid().ToString("D"), first.gameRevision,
                first.layoutRevision, secondCandidate);
            Assert.AreEqual(GamePersistenceStates.Committed, second.state);

            FurnitureHomeCommitResult replay = store.Commit(firstOperationId, before.gameRevision,
                before.layoutRevision, firstCandidate);
            Assert.AreEqual(GamePersistenceStates.Duplicate, replay.state);
            Assert.IsTrue(replay.durable);
            Assert.AreEqual(second.layoutRevision, replay.layoutRevision);
            Assert.AreEqual(3, replay.document.instances[0].positionMeters.x);
        }

        private static HomeFurnitureLayoutDocument Layout(string id)
        {
            return new HomeFurnitureLayoutDocument
            {
                schemaVersion = 1,
                homeId = id,
                roomId = "room.synthetic",
                catalogueVersion = "1.0.0",
                seedVersion = "legacy-seed",
                coordinateFrame = "room_local_y_up_positive_z_forward",
                lengthUnit = "m",
                layoutRevision = 0,
                instances = new System.Collections.Generic.List<HomeFurnitureLayoutInstance>
                {
                    new HomeFurnitureLayoutInstance
                    {
                        instanceId = "instance.synthetic",
                        definitionId = "furniture.missing.definition",
                        positionMeters = new FurnitureVector3 { x = 1.23456789, y = 0.125, z = -4.5 },
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

        private static string Serialize(HomeFurnitureLayoutDocument document)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument)).WriteObject(stream, document);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static HomeFurnitureLayoutDocument Clone(HomeFurnitureLayoutDocument document)
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
