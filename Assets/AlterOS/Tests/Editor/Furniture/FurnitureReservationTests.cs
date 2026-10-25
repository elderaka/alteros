using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureReservationTests
    {
        [Test]
        public void FailedSecondPointClaimRollsBackTheFirstClaim()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            var arrangement = FurnitureArrangementResolver.Evaluate(request);
            request.world.occupiedPointRefs.Add(FurnitureArrangementTestData.Ref("seat.b", "seat"));
            var reservations = new FurnitureReservationSet();
            var failed = reservations.TryAcquire("owner.blocked", arrangement, request.world);
            var singlePoint = new ArrangementEvaluation
            {
                state = "available", arrangementId = "arrangement.single", relationshipRuleId = "relationship.seat.face_pair",
                revisionVector = arrangement.revisionVector,
                reservationPointRefs = new List<FurniturePointReference> { FurnitureArrangementTestData.Ref("seat.a", "seat") }
            };
            var afterRollback = reservations.TryAcquire("owner.next", singlePoint, request.world);
            Assert.IsFalse(failed.success);
            Assert.AreEqual("point_occupied", failed.reasonCode);
            Assert.IsTrue(afterRollback.success);
            Assert.AreEqual(1, afterRollback.claimIds.Count);
        }

        [Test]
        public void ReservationsArePointScopedNotObjectScoped()
        {
            var request = FurnitureArrangementTestData.CreateSharedInstancePoints();
            var reservations = new FurnitureReservationSet();
            var first = SinglePoint("chair.shared", "seat.a");
            var second = SinglePoint("chair.shared", "seat.b");
            var firstResult = reservations.TryAcquire("owner.a", first, request.world);
            var secondResult = reservations.TryAcquire("owner.b", second, request.world);
            Assert.IsTrue(firstResult.success);
            Assert.IsTrue(secondResult.success);
            Assert.AreEqual(1, firstResult.claimIds.Count);
            Assert.AreEqual(1, secondResult.claimIds.Count);
        }

        [Test]
        public void ContextualTableEvidenceDoesNotReserveTheTable()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(true, true);
            request.relationshipRuleId = "relationship.seat_pair.table_context";
            var arrangement = FurnitureArrangementResolver.Evaluate(request);
            var result = new FurnitureReservationSet().TryAcquire("owner.table-context", arrangement, request.world);
            Assert.IsTrue(result.success);
            Assert.That(result.claimIds.Any(claim => claim.StartsWith("table.unrelated/", System.StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void ChangedInstanceRevisionCannotBeClaimedFromStaleEvaluation()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            var arrangement = FurnitureArrangementResolver.Evaluate(request);
            request.world.instances.Single(instance => instance.instanceId == "seat.b").instanceRevision = "2";
            var result = new FurnitureReservationSet().TryAcquire("owner.stale", arrangement, request.world);
            Assert.IsFalse(result.success);
            Assert.AreEqual("stale_revision", result.reasonCode);
            Assert.IsEmpty(result.claimIds);
        }

        [Test]
        public void DifferentOwnerCannotStealClaimsAndReleaseIsIdempotent()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            var arrangement = FurnitureArrangementResolver.Evaluate(request);
            var reservations = new FurnitureReservationSet();
            var first = reservations.TryAcquire("owner.first", arrangement, request.world);
            var conflict = reservations.TryAcquire("owner.second", arrangement, request.world);
            reservations.Release("owner.first");
            reservations.Release("owner.first");
            var afterRelease = reservations.TryAcquire("owner.second", arrangement, request.world);
            Assert.IsTrue(first.success);
            Assert.IsFalse(conflict.success);
            Assert.AreEqual("point_occupied", conflict.reasonCode);
            Assert.IsTrue(afterRelease.success);
        }

        private static ArrangementEvaluation SinglePoint(string instanceId, string pointId)
        {
            return new ArrangementEvaluation
            {
                state = "available", arrangementId = "arrangement." + pointId,
                relationshipRuleId = "relationship.seat.face_pair",
                revisionVector = new List<FurnitureRevisionStamp> { new FurnitureRevisionStamp { entityId = "instance:" + instanceId, revision = "1" } },
                reservationPointRefs = new List<FurniturePointReference> { FurnitureArrangementTestData.Ref(instanceId, pointId) }
            };
        }
    }
}
