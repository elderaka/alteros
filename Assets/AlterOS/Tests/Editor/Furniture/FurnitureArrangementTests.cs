using System.Linq;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureArrangementTests
    {
        [Test]
        public void FacingSeatsAreAvailableWithoutATable()
        {
            var result = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(false, false));
            Assert.AreEqual("available", result.state);
            Assert.AreEqual(2, result.roleBindings.Count);
            Assert.That(result.roleBindings.Any(binding => binding.roleId == "player" && binding.instanceId == "seat.a"), Is.True);
            Assert.That(result.roleBindings.Any(binding => binding.roleId == "alterra" && binding.instanceId == "seat.b"), Is.True);
            Assert.AreEqual(2, result.reservationPointRefs.Count);
        }

        [Test]
        public void UnrelatedTableDoesNotUnlockTableContext()
        {
            var result = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(true, false));
            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("missing_compatible_point", result.primaryReasonCode);
        }

        [Test]
        public void SharedTableBetweenTheSeatsUnlocksTableContext()
        {
            var result = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(true, true));
            Assert.AreEqual("available", result.state);
            Assert.That(result.roleBindings.Any(binding => binding.roleId == "table" && binding.instanceId == "table.unrelated"), Is.True);
            Assert.That(result.reservationPointRefs.Any(point => point.instanceId == "table.unrelated"), Is.False);
        }

        [Test]
        public void WrongFacingSeatIsUnavailable()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.instances.Single(instance => instance.instanceId == "seat.b").rotation = FurnitureArrangementTestData.Yaw(90);
            var result = FurnitureArrangementResolver.Evaluate(request);
            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("wrong_facing", result.primaryReasonCode);
        }

        [Test]
        public void SeatHeightMismatchIsUnavailable()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.instances.Single(instance => instance.instanceId == "seat.b").positionMeters.y = 0.5;
            var result = FurnitureArrangementResolver.Evaluate(request);
            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("height_mismatch", result.primaryReasonCode);
        }

        [Test]
        public void SeatsOnDifferentFloorsAreUnavailable()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.supports.Add(new FurnitureSupportSnapshot { supportId = "room.floor.upper", supportKind = "roomFloor", revision = "1" });
            request.world.instances.Single(instance => instance.instanceId == "seat.b").supportId = "room.floor.upper";
            var result = FurnitureArrangementResolver.Evaluate(request);
            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("support_mismatch", result.primaryReasonCode);
        }

        [Test]
        public void SeatMountedOnWallIsUnavailable()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.supports.Single(support => support.supportId == "room.floor").supportKind = "wallSurface";
            var result = FurnitureArrangementResolver.Evaluate(request);
            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("unsupported_support", result.primaryReasonCode);
        }

        [Test]
        public void BlockedApproachIsUnavailable()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.accessEvidence.Single(evidence => evidence.instanceId == "seat.b").state = "blocked";
            request.world.accessEvidence.Single(evidence => evidence.instanceId == "seat.b").pathClear = false;
            var result = FurnitureArrangementResolver.Evaluate(request);
            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("blocked_access", result.primaryReasonCode);
        }

        [Test]
        public void IncompleteAccessEvidenceRemainsUnknown()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.evidenceComplete = false;
            request.world.accessEvidence.Clear();
            var result = FurnitureArrangementResolver.Evaluate(request);
            Assert.AreEqual("unknown", result.state);
            Assert.AreEqual("evidence_unknown", result.primaryReasonCode);
        }

        [Test]
        public void KnownFacingFailureDoesNotBecomeUnknownWhenAccessDataIsIncomplete()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            request.world.instances.Single(instance => instance.instanceId == "seat.b").rotation = FurnitureArrangementTestData.Yaw(90);
            request.world.evidenceComplete = false;
            request.world.accessEvidence.Clear();

            var result = FurnitureArrangementResolver.Evaluate(request);

            Assert.AreEqual("unavailable", result.state);
            Assert.AreEqual("wrong_facing", result.primaryReasonCode);
        }

        [Test]
        public void TieBreakIsStableWhenWorldInputOrderingChanges()
        {
            var first = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateCompetingPairs(false));
            var second = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateCompetingPairs(true));
            Assert.AreEqual("available", first.state);
            Assert.AreEqual(first.arrangementId, second.arrangementId);
            Assert.AreEqual("seat.a", first.roleBindings.Single(binding => binding.roleId == "player").instanceId);
            Assert.AreEqual("seat.a", second.roleBindings.Single(binding => binding.roleId == "player").instanceId);
        }
    }
}
