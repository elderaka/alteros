using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class HomeFurnitureRepositoryTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [Test]
        public void FirstLoadUsesOnlyTheSceneSeedAndEditedSaveSurvivesANewerSeed()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home.json");
                var seed = FurnitureLayoutTestData.Seed("seed-1");
                var first = HomeFurnitureRepository.LoadOrSeed(path, seed);
                Assert.AreEqual("loaded", first.state);
                Assert.AreEqual(1, first.document.schemaVersion);
                Assert.IsNull(first.document.inventory);
                Assert.AreEqual(2, first.document.instances.Count);
                Assert.AreEqual("seed-1", first.document.seedVersion);

                var edited = FurnitureLayoutTestData.Clone(first.document);
                edited.instances[0].positionMeters.x = -1.25;
                edited.layoutRevision++;
                var saved = HomeFurnitureRepository.Save(path, edited, first.contentHash);
                Assert.AreEqual("saved", saved.state);

                var newSeed = FurnitureLayoutTestData.Seed("seed-2");
                newSeed.instances.Add(FurnitureLayoutTestData.Instance("seed.new-chair", "furniture.seat.basic", new FurnitureVector3 { x = 2, y = 0, z = 2 }, FurnitureLayoutTestData.Identity(), "room.floor"));
                var resumed = HomeFurnitureRepository.LoadOrSeed(path, newSeed);
                Assert.AreEqual("loaded", resumed.state);
                Assert.AreEqual("seed-1", resumed.document.seedVersion);
                Assert.AreEqual(-1.25, resumed.document.instances.Single(item => item.instanceId == "seat.a").positionMeters.x);
                Assert.That(resumed.document.instances.Any(item => item.instanceId == "seed.new-chair"), Is.False);
            });
        }

        [Test]
        public void MissingDefinitionAndSupportStayAsCompleteInertOrphanRecords()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home.json");
                var document = FurnitureLayoutTestData.Seed("seed-1");
                var orphan = FurnitureLayoutTestData.Instance("orphan.window", "furniture.removed.window", new FurnitureVector3 { x = 1.2, y = 1.8, z = 3.9 }, FurnitureLayoutTestData.Yaw(27), "room.wall.removed.opening");
                orphan.conditionState = "worn";
                orphan.resourceState.Add(new FurnitureResourceState { resourceId = "paint", value = 0.35, known = true });
                document.instances.Add(orphan);
                var save = HomeFurnitureRepository.Save(path, document, null);
                Assert.AreEqual("saved", save.state);

                var loaded = HomeFurnitureRepository.Load(path, FurnitureCatalogueRegistry.Load(CataloguePath), FurnitureLayoutTestData.Evidence(FurnitureCatalogueRegistry.Load(CataloguePath)).supports);
                var savedOrphan = loaded.document.instances.Single(item => item.instanceId == "orphan.window");
                Assert.AreEqual("loaded", loaded.state);
                Assert.That(loaded.orphanInstanceIds.Contains("orphan.window"), Is.True);
                Assert.AreEqual("furniture.removed.window", savedOrphan.definitionId);
                Assert.AreEqual("room.wall.removed.opening", savedOrphan.support.supportId);
                Assert.AreEqual("worn", savedOrphan.conditionState);
                Assert.AreEqual(0.35, savedOrphan.resourceState.Single().value);
                Assert.AreEqual(1.2, savedOrphan.positionMeters.x);
            });
        }

        [Test]
        public void UnsupportedSchemaFailsClosedWithoutResettingTheSavedDocument()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home.json");
                var document = FurnitureLayoutTestData.Seed("seed-1");
                document.schemaVersion = 99;
                var original = FurnitureLayoutTestData.Serialize(document);
                File.WriteAllBytes(path, original);

                var loaded = HomeFurnitureRepository.LoadOrSeed(path, FurnitureLayoutTestData.Seed("seed-2"));

                Assert.AreEqual("invalid", loaded.state);
                Assert.AreEqual(99, loaded.document.schemaVersion);
                Assert.AreEqual(2, loaded.document.instances.Count);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            });
        }

        [Test]
        public void SchemaTwoRoundTripsStockAndPreservesUnregisteredDefinitionCounts()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home-v2.json");
                var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
                var document = FurnitureLayoutTestData.Seed("seed-1");
                document.schemaVersion = 2;
                document.inventory = FurnitureInventorySeed.CreateInitial(catalogue);
                document.inventory.Add(new FurnitureInventoryEntry { definitionId = "furniture.removed.old_chair", storedCount = 4 });

                var saved = HomeFurnitureRepository.Save(path, document, null);
                var loaded = HomeFurnitureRepository.Load(path);

                Assert.AreEqual("saved", saved.state);
                Assert.AreEqual("loaded", loaded.state);
                Assert.AreEqual(2, loaded.document.schemaVersion);
                Assert.AreEqual(2, loaded.document.instances.Count);
                Assert.AreEqual(9, loaded.document.inventory.Count);
                Assert.AreEqual(10, loaded.document.inventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount);
                Assert.AreEqual(4, loaded.document.inventory.Single(item => item.definitionId == "furniture.removed.old_chair").storedCount);
            });
        }

        [Test]
        public void SchemaTwoRejectsMissingNegativeAndDuplicateInventoryEntries()
        {
            WithTempDirectory(directory =>
            {
                var missing = FurnitureLayoutTestData.Seed("seed-1");
                missing.schemaVersion = 2;
                missing.inventory = null;
                var missingResult = HomeFurnitureRepository.Save(Path.Combine(directory, "missing.json"), missing, null);
                Assert.AreEqual("invalid", missingResult.state);
                Assert.AreEqual("missing_inventory", missingResult.diagnostic.code);

                var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
                var negative = FurnitureLayoutTestData.Seed("seed-1");
                negative.schemaVersion = 2;
                negative.inventory = FurnitureInventorySeed.CreateInitial(catalogue);
                negative.inventory[0].storedCount = -1;
                var negativeResult = HomeFurnitureRepository.Save(Path.Combine(directory, "negative.json"), negative, null);
                Assert.AreEqual("invalid", negativeResult.state);
                Assert.AreEqual("negative_stock", negativeResult.diagnostic.code);

                var duplicate = FurnitureLayoutTestData.Seed("seed-1");
                duplicate.schemaVersion = 2;
                duplicate.inventory = FurnitureInventorySeed.CreateInitial(catalogue);
                duplicate.inventory.Add(new FurnitureInventoryEntry
                {
                    definitionId = duplicate.inventory[0].definitionId,
                    storedCount = 2
                });
                var duplicateResult = HomeFurnitureRepository.Save(Path.Combine(directory, "duplicate.json"), duplicate, null);
                Assert.AreEqual("invalid", duplicateResult.state);
                Assert.AreEqual("duplicate_definition", duplicateResult.diagnostic.code);
            });
        }

        [Test]
        public void ActivityAndReservationStateAreNotSerializedForResume()
        {
            var document = FurnitureLayoutTestData.Seed("seed-1");

                var json = System.Text.Encoding.UTF8.GetString(FurnitureLayoutTestData.Serialize(document));

            Assert.That(json, Does.Not.Contain("activeActivity"));
            Assert.That(json, Does.Not.Contain("reservationOwner"));
            Assert.That(json, Does.Not.Contain("activityProgress"));
        }

        [Test]
        public void StaleSaveHashReturnsConflictAndLeavesDiskBytesUnchanged()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home.json");
                var document = FurnitureLayoutTestData.Seed("seed-1");
                var first = HomeFurnitureRepository.Save(path, document, null);
                var before = File.ReadAllBytes(path);
                var changed = FurnitureLayoutTestData.Seed("seed-2");

                var result = HomeFurnitureRepository.Save(path, changed, "stale-hash");

                Assert.AreEqual("conflict", result.state);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            });
        }

        [Test]
        public void PersistenceReadFailureLeavesExistingLayoutAndLiveSnapshotUntouched()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home.json");
                var document = FurnitureLayoutTestData.Seed("seed-1");
                var save = HomeFurnitureRepository.Save(path, document, null);
                var before = File.ReadAllBytes(path);
                var update = FurnitureLayoutTestData.Seed("seed-2");
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var result = HomeFurnitureRepository.Save(path, update, save.contentHash);
                    Assert.AreEqual("error", result.state);
                }
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
                Assert.AreEqual("seed-1", HomeFurnitureRepository.Load(path).document.seedVersion);
            });
        }

        [Test]
        public void RaisedChairWallWindowAndCarpetRoundTripWithoutLosingTransformOrSupport()
        {
            WithTempDirectory(directory =>
            {
                var path = Path.Combine(directory, "home.json");
                var fixturePath = Path.GetFullPath("Tools/FurnitureRuntime/Fixtures/windows-android-roundtrip-layout.json");
                var fixture = HomeFurnitureRepository.Load(fixturePath);
                Assert.AreEqual("loaded", fixture.state);
                var document = fixture.document;
                var first = HomeFurnitureRepository.Save(path, document, null);
                var loaded = HomeFurnitureRepository.Load(path);
                var secondBytes = FurnitureLayoutTestData.Serialize(loaded.document);
                var androidPath = Path.Combine(directory, "android-roundtrip.json");
                File.WriteAllBytes(androidPath, secondBytes);
                var roundTrip = HomeFurnitureRepository.Load(androidPath);
                var reverseBytes = FurnitureLayoutTestData.Serialize(roundTrip.document);
                var reversePath = Path.Combine(directory, "windows-reverse-roundtrip.json");
                File.WriteAllBytes(reversePath, reverseBytes);
                var reverse = HomeFurnitureRepository.Load(reversePath);

                Assert.AreEqual("saved", first.state);
                Assert.AreEqual("loaded", roundTrip.state);
                Assert.AreEqual("loaded", reverse.state);
                Assert.AreEqual(document.schemaVersion, reverse.document.schemaVersion);
                Assert.AreEqual(document.homeId, reverse.document.homeId);
                Assert.AreEqual(document.roomId, reverse.document.roomId);
                Assert.AreEqual(document.catalogueVersion, reverse.document.catalogueVersion);
                Assert.AreEqual(document.seedVersion, reverse.document.seedVersion);
                Assert.AreEqual(document.coordinateFrame, reverse.document.coordinateFrame);
                Assert.AreEqual(document.lengthUnit, reverse.document.lengthUnit);
                Assert.AreEqual(document.layoutRevision, reverse.document.layoutRevision);
                CollectionAssert.AreEquivalent(document.instances.Select(item => item.instanceId), reverse.document.instances.Select(item => item.instanceId));
                foreach (var expected in document.instances)
                {
                    var actual = reverse.document.instances.Single(item => item.instanceId == expected.instanceId);
                    Assert.AreEqual(expected.definitionId, actual.definitionId);
                    AssertTransformNear(expected.positionMeters, actual.positionMeters, 0.001);
                    AssertTransformNear(expected.scale, actual.scale, 0.001);
                    Assert.LessOrEqual(QuaternionDifferenceDegrees(expected.rotation, actual.rotation), 0.1);
                    Assert.AreEqual(expected.support.supportId, actual.support.supportId);
                    Assert.AreEqual(expected.support.supportKind, actual.support.supportKind);
                    Assert.AreEqual(expected.support.supportFrameRevision, actual.support.supportFrameRevision);
                    Assert.AreEqual(expected.support.attachmentPointId, actual.support.attachmentPointId);
                    AssertTransformNear(expected.support.localPositionMeters, actual.support.localPositionMeters, 0.001);
                    Assert.LessOrEqual(QuaternionDifferenceDegrees(expected.support.localRotation, actual.support.localRotation), 0.1);
                    Assert.AreEqual(expected.conditionState, actual.conditionState);
                    Assert.AreEqual(expected.enabled, actual.enabled);
                    Assert.AreEqual(expected.instanceRevision, actual.instanceRevision);
                    Assert.AreEqual(expected.resourceState.Count, actual.resourceState.Count);
                    for (var index = 0; index < expected.resourceState.Count; index++)
                    {
                        Assert.AreEqual(expected.resourceState[index].resourceId, actual.resourceState[index].resourceId);
                        Assert.AreEqual(expected.resourceState[index].known, actual.resourceState[index].known);
                        Assert.AreEqual(expected.resourceState[index].value, actual.resourceState[index].value, 0.001);
                    }
                }
                Assert.AreEqual(0.125, reverse.document.instances.Single(item => item.instanceId == "chair.raised").positionMeters.y, 0.001);
                Assert.AreEqual("room.wall.north.opening.01", reverse.document.instances.Single(item => item.instanceId == "window.mounted").support.supportId);
                Assert.AreEqual(0.72, reverse.document.instances.Single(item => item.instanceId == "chair.raised").resourceState.Single().value, 0.001);
                Assert.AreEqual(3, roundTrip.document.instances.Count);
            });
        }

        private static void WithTempDirectory(System.Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "alteros-furniture-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { action(directory); }
            finally
            {
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static void AssertTransformNear(FurnitureVector3 expected, FurnitureVector3 actual, double tolerance)
        {
            Assert.LessOrEqual(System.Math.Abs(expected.x - actual.x), tolerance);
            Assert.LessOrEqual(System.Math.Abs(expected.y - actual.y), tolerance);
            Assert.LessOrEqual(System.Math.Abs(expected.z - actual.z), tolerance);
        }

        private static double QuaternionDifferenceDegrees(FurnitureQuaternion expected, FurnitureQuaternion actual)
        {
            var expectedLength = System.Math.Sqrt(expected.x * expected.x + expected.y * expected.y + expected.z * expected.z + expected.w * expected.w);
            var actualLength = System.Math.Sqrt(actual.x * actual.x + actual.y * actual.y + actual.z * actual.z + actual.w * actual.w);
            var dot = System.Math.Abs(expected.x * actual.x + expected.y * actual.y + expected.z * actual.z + expected.w * actual.w) /
                (expectedLength * actualLength);
            dot = System.Math.Max(-1d, System.Math.Min(1d, dot));
            return 2d * System.Math.Acos(dot) * 180d / System.Math.PI;
        }
    }
}
