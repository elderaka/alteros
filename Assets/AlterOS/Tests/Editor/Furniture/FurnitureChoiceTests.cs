using System.Linq;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureChoiceTests
    {
        [Test]
        public void SatisfiedRequirementIsAvailableAndBindsArrangementRevision()
        {
            var arrangement = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(false, false));
            var requirement = new FurnitureChoiceRequirement { choiceId = "choice.sit", relationshipRuleId = "relationship.seat.face_pair", unmetBehavior = "lock", safeReasonKey = "choice.needs_seats" };

            var result = FurnitureChoiceEvaluator.Evaluate(requirement, arrangement);

            Assert.AreEqual("available", result.state);
            Assert.IsNull(result.reasonKey);
            Assert.AreEqual(arrangement.arrangementId, result.boundArrangementId);
            Assert.AreEqual(arrangement.revisionToken, result.boundRevisionToken);
        }

        [Test]
        public void LockedChoiceExposesOnlySafeAuthoredReasonKey()
        {
            var arrangement = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(true, false));
            var requirement = new FurnitureChoiceRequirement { choiceId = "choice.table", relationshipRuleId = "relationship.seat_pair.table_context", unmetBehavior = "lock", safeReasonKey = "choice.needs_usable_table" };

            var result = FurnitureChoiceEvaluator.Evaluate(requirement, arrangement);

            Assert.AreEqual("locked", result.state);
            Assert.AreEqual("choice.needs_usable_table", result.reasonKey);
        }

        [Test]
        public void HiddenChoiceDoesNotExposePlayerReason()
        {
            var arrangement = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(true, false));
            var requirement = new FurnitureChoiceRequirement { choiceId = "choice.hidden", relationshipRuleId = "relationship.seat_pair.table_context", unmetBehavior = "hide", safeReasonKey = "private.reason" };

            var result = FurnitureChoiceEvaluator.Evaluate(requirement, arrangement);

            Assert.AreEqual("hidden", result.state);
            Assert.IsNull(result.reasonKey);
        }

        [Test]
        public void UnknownEvidenceUsesGenericCopyOrHidesWithoutLeakingSpecificReason()
        {
            var arrangement = FurnitureArrangementResolver.Evaluate(FurnitureArrangementTestData.CreateSeatPair(false, false));
            arrangement.state = "unknown";
            arrangement.primaryReasonCode = "evidence_unknown";
            var requirement = new FurnitureChoiceRequirement { choiceId = "choice.unknown", relationshipRuleId = "relationship.seat.face_pair", unmetBehavior = "lock", safeReasonKey = "choice.needs_seats" };

            var locked = FurnitureChoiceEvaluator.Evaluate(requirement, arrangement);
            requirement.unmetBehavior = "hide";
            var hidden = FurnitureChoiceEvaluator.Evaluate(requirement, arrangement);

            Assert.AreEqual("locked", locked.state);
            Assert.AreEqual("choice.availability_unconfirmed", locked.reasonKey);
            Assert.AreEqual("hidden", hidden.state);
            Assert.IsNull(hidden.reasonKey);
        }

        [Test]
        public void StalePreStartBindingRequiresFreshEvaluationAndCannotAdvanceFromOldRevision()
        {
            var request = FurnitureArrangementTestData.CreateSeatPair(false, false);
            var captured = FurnitureArrangementResolver.Evaluate(request);
            var requirement = new FurnitureChoiceRequirement
            {
                choiceId = "choice.sit", relationshipRuleId = "relationship.seat.face_pair",
                unmetBehavior = "lock", safeReasonKey = "choice.needs_seats",
                expectedArrangementId = captured.arrangementId, expectedRevisionToken = captured.revisionToken
            };
            request.world.instances.Single(instance => instance.instanceId == "seat.b").instanceRevision = "2";
            var replacement = FurnitureArrangementResolver.Evaluate(request);

            var staleClick = FurnitureChoiceEvaluator.Evaluate(requirement, replacement);
            requirement.expectedArrangementId = replacement.arrangementId;
            requirement.expectedRevisionToken = replacement.revisionToken;
            var freshClick = FurnitureChoiceEvaluator.Evaluate(requirement, replacement);

            Assert.AreEqual("locked", staleClick.state);
            Assert.AreEqual("choice.stale_revision", staleClick.reasonKey);
            Assert.AreEqual("available", freshClick.state);
        }
    }
}
