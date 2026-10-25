using System.IO;
using System.Linq;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureLayoutTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [Test]
        public void StaleInstanceRevisionRejectsCommandWithoutMutatingCurrentLayout()
        {
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            var before = current.document.instances[0].positionMeters.x;
            var command = new FurnitureLayoutCommand
            {
                operation = "move", homeId = "home.test", roomId = "room.test",
                instanceId = "seat.a", expectedInstanceRevision = 0,
                positionMeters = new FurnitureVector3 { x = -1, y = 0, z = 0 }, rotation = FurnitureLayoutTestData.Yaw(90)
            };

            var result = FurnitureLayoutCommands.Apply(command, current, FurnitureLayoutTestData.Evidence(FurnitureCatalogueRegistry.Load(CataloguePath)));

            Assert.IsFalse(result.success);
            Assert.AreEqual("stale_revision", result.reasonCode);
            Assert.AreEqual(before, current.document.instances[0].positionMeters.x);
        }

        [Test]
        public void ReservedFurniturePointRejectsMoveWithoutMutation()
        {
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            var evidence = FurnitureLayoutTestData.Evidence(FurnitureCatalogueRegistry.Load(CataloguePath));
            evidence.reservedPointRefs.Add(FurnitureArrangementTestData.Ref("seat.a", "seat"));
            var command = new FurnitureLayoutCommand
            {
                operation = "move", homeId = "home.test", roomId = "room.test", instanceId = "seat.a",
                expectedInstanceRevision = 1, positionMeters = new FurnitureVector3 { x = -1, y = 0, z = 0 }, rotation = FurnitureLayoutTestData.Yaw(90)
            };

            var result = FurnitureLayoutCommands.Apply(command, current, evidence);

            Assert.IsFalse(result.success);
            Assert.AreEqual("point_in_use", result.reasonCode);
            Assert.AreEqual(-0.7, current.document.instances[0].positionMeters.x);
        }

        [Test]
        public void StructuralOverlapRejectsButRelationshipInvalidPlacementIsAllowed()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            var evidence = FurnitureLayoutTestData.Evidence(catalogue);
            var overlap = FurnitureLayoutCommands.Apply(
                FurnitureLayoutTestData.Place("seat.overlap", "furniture.seat.basic", new FurnitureVector3 { x = -0.7, y = 0, z = 0 }, FurnitureLayoutTestData.Yaw(90), "room.floor"),
                current, evidence);
            Assert.IsFalse(overlap.success);
            Assert.AreEqual("structural_overlap", overlap.reasonCode);

            var command = FurnitureLayoutTestData.Place("table.far", "furniture.table.general", new FurnitureVector3 { x = 2.7, y = 0, z = 2.7 }, FurnitureLayoutTestData.Identity(), "room.floor");
            var placed = FurnitureLayoutCommands.Apply(command, current, evidence);
            Assert.IsTrue(placed.success);
            var relation = FurnitureArrangementResolver.Evaluate(new FurnitureArrangementRequest
            {
                catalogue = catalogue, world = FurnitureLayoutTestData.ToWorld(placed.snapshot.document, catalogue),
                relationshipRuleId = "relationship.seat_pair.table_context"
            });
            Assert.AreEqual("unavailable", relation.state);
            Assert.AreEqual("missing_compatible_point", relation.primaryReasonCode);
        }

        [Test]
        public void CarpetCanOverlapFurnitureWithoutBecomingANavigationObstacle()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            var command = FurnitureLayoutTestData.Place("carpet.under-chair", "furniture.carpet.basic", new FurnitureVector3 { x = -0.7, y = 0, z = 0 }, FurnitureLayoutTestData.Identity(), "room.floor");

            var result = FurnitureLayoutCommands.Apply(command, current, FurnitureLayoutTestData.Evidence(catalogue));

            Assert.IsTrue(result.success);
            Assert.That(result.snapshot.document.instances.Any(item => item.instanceId == "carpet.under-chair"), Is.True);
        }

        [Test]
        public void AppliedCommandReturnsUncommittedSnapshotAndOriginalCompareAndSetHash()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            current.contentHash = "saved-hash-v1";
            var command = FurnitureLayoutTestData.Place("table.new", "furniture.table.general", new FurnitureVector3 { x = 2.7, y = 0, z = 2.7 }, FurnitureLayoutTestData.Identity(), "room.floor");

            var result = FurnitureLayoutCommands.Apply(command, current, FurnitureLayoutTestData.Evidence(catalogue));

            Assert.IsTrue(result.success);
            Assert.IsNull(result.snapshot.contentHash);
            Assert.AreEqual("saved-hash-v1", result.expectedSaveHash);
            Assert.AreEqual(2, current.document.instances.Count);
        }

        [Test]
        public void SchemaTwoInventorySurvivesAPlacementCommandUnchanged()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var document = FurnitureLayoutTestData.Seed("seed-1");
            document.schemaVersion = 2;
            document.inventory = FurnitureInventorySeed.CreateInitial(catalogue);
            document.inventory.Single(item => item.definitionId == "furniture.table.general").storedCount = 7;
            var current = FurnitureLayoutTestData.Snapshot(document);
            var before = current.document.inventory.Select(item => item.definitionId + ":" + item.storedCount).ToArray();
            var command = FurnitureLayoutTestData.Place("table.saved-stock", "furniture.table.general",
                new FurnitureVector3 { x = 2.7, y = 0, z = 2.7 }, FurnitureLayoutTestData.Identity(), "room.floor");

            var result = FurnitureLayoutCommands.Apply(command, current, FurnitureLayoutTestData.Evidence(catalogue));

            Assert.IsTrue(result.success);
            CollectionAssert.AreEqual(before, result.snapshot.document.inventory.Select(item => item.definitionId + ":" + item.storedCount).ToArray());
            CollectionAssert.AreEqual(before, current.document.inventory.Select(item => item.definitionId + ":" + item.storedCount).ToArray());
        }

        [Test]
        public void TransformCommandAppliesPositionRotationAndSupportAsOneCandidate()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            var original = current.document.instances.Single(item => item.instanceId == "seat.a");
            var command = new FurnitureLayoutCommand
            {
                operation = "transform", homeId = "home.test", roomId = "room.test", instanceId = "seat.a",
                expectedInstanceRevision = 1,
                positionMeters = new FurnitureVector3 { x = -2.7, y = 0, z = -2.7 },
                rotation = FurnitureLayoutTestData.Identity(),
                support = new FurnitureLayoutSupportReference
                {
                    supportId = "room.floor", supportKind = "roomFloor", supportFrameRevision = "1",
                    attachmentPointId = "origin", localPositionMeters = new FurnitureVector3 { x = -2.7, y = 0, z = -2.7 },
                    localRotation = FurnitureLayoutTestData.Identity()
                }
            };

            var result = FurnitureLayoutCommands.Apply(command, current, FurnitureLayoutTestData.Evidence(catalogue));

            Assert.IsTrue(result.success);
            var moved = result.snapshot.document.instances.Single(item => item.instanceId == "seat.a");
            Assert.AreEqual(-2.7, moved.positionMeters.x, 0.001);
            Assert.AreEqual(-2.7, moved.positionMeters.z, 0.001);
            Assert.AreEqual(0, moved.rotation.y, 0.001);
            Assert.AreEqual(-2.7, moved.support.localPositionMeters.x, 0.001);
            Assert.AreEqual(2, moved.instanceRevision);
            Assert.AreEqual(-0.7, original.positionMeters.x, 0.001);
            Assert.AreEqual(1, current.document.layoutRevision);
        }

        [Test]
        public void WallPlacementMustAgreeWithNamedSupportFrameAndAttachmentPose()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var current = FurnitureLayoutTestData.Snapshot(FurnitureLayoutTestData.Seed("seed-1"));
            var evidence = FurnitureLayoutTestData.Evidence(catalogue);
            evidence.supports.Single(support => support.supportId == "room.wall.north.opening.01").positionMeters =
                new FurnitureVector3 { x = 0, y = 0, z = 3.4 };
            evidence.supports.Single(support => support.supportId == "room.wall.north.opening.01").rotation = FurnitureLayoutTestData.Identity();
            var correct = FurnitureLayoutTestData.Place("window.correct", "furniture.window.basic",
                new FurnitureVector3 { x = 0, y = 1.5, z = 3.9 }, FurnitureLayoutTestData.Identity(), "room.wall.north.opening.01");
            correct.newInstance.support.localPositionMeters = new FurnitureVector3 { x = 0, y = 1.5, z = 0.5 };
            var accepted = FurnitureLayoutCommands.Apply(correct, current, evidence);
            Assert.IsTrue(accepted.success);

            var command = FurnitureLayoutTestData.Place("window.new", "furniture.window.basic",
                new FurnitureVector3 { x = 0, y = 1.5, z = 3.7 }, FurnitureLayoutTestData.Identity(), "room.wall.north.opening.01");
            command.newInstance.support.localPositionMeters = new FurnitureVector3 { x = 0, y = 1.5, z = 0.5 };

            var result = FurnitureLayoutCommands.Apply(command, current, evidence);

            Assert.IsFalse(result.success);
            Assert.AreEqual("support_pose_mismatch", result.reasonCode);
            Assert.AreEqual(2, current.document.instances.Count);
        }
    }
}
