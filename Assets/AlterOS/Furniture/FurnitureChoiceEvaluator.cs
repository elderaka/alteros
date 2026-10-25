using System.Collections.Generic;

namespace AlterOS.Furniture
{
    public sealed class FurnitureChoiceRequirement
    {
        public string choiceId;
        public string relationshipRuleId;
        public string unmetBehavior;
        public string safeReasonKey;
        public string expectedArrangementId;
        public string expectedRevisionToken;
    }

    public sealed class FurnitureChoiceEvaluation
    {
        public string state;
        public string reasonKey;
        public string boundArrangementId;
        public string boundRevisionToken;
        public List<FurnitureDiagnostic> developerDiagnostics = new List<FurnitureDiagnostic>();
    }

    public static class FurnitureChoiceEvaluator
    {
        public static FurnitureChoiceEvaluation Evaluate(FurnitureChoiceRequirement requirement, ArrangementEvaluation arrangement)
        {
            if (requirement == null || arrangement == null)
                return Result("locked", "choice.availability_unconfirmed", arrangement);

            if (requirement.unmetBehavior != "lock" && requirement.unmetBehavior != "hide")
                return Result("locked", "choice.availability_unconfirmed", arrangement);

            var isStale = (!string.IsNullOrEmpty(requirement.expectedArrangementId) && requirement.expectedArrangementId != arrangement.arrangementId) ||
                (!string.IsNullOrEmpty(requirement.expectedRevisionToken) && requirement.expectedRevisionToken != arrangement.revisionToken);
            if (isStale)
                return requirement.unmetBehavior == "hide"
                    ? Result("hidden", null, arrangement)
                    : Result("locked", "choice.stale_revision", arrangement);

            if (arrangement.relationshipRuleId != requirement.relationshipRuleId)
            {
                return requirement.unmetBehavior == "hide"
                    ? Result("hidden", null, arrangement)
                    : Result("locked", "choice.availability_unconfirmed", arrangement);
            }

            if (arrangement.state == "available")
                return Result("available", null, arrangement);

            if (requirement.unmetBehavior == "hide")
                return Result("hidden", null, arrangement);

            if (arrangement.state == "unknown")
                return Result("locked", "choice.availability_unconfirmed", arrangement);

            var reason = string.IsNullOrWhiteSpace(requirement.safeReasonKey)
                ? "choice.requirement_unavailable"
                : requirement.safeReasonKey;
            return Result("locked", reason, arrangement);
        }

        private static FurnitureChoiceEvaluation Result(string state, string reasonKey, ArrangementEvaluation arrangement)
        {
            return new FurnitureChoiceEvaluation
            {
                state = state,
                reasonKey = state == "hidden" ? null : reasonKey,
                boundArrangementId = arrangement == null ? null : arrangement.arrangementId,
                boundRevisionToken = arrangement == null ? null : arrangement.revisionToken,
                developerDiagnostics = arrangement == null || arrangement.developerDiagnostics == null
                    ? new List<FurnitureDiagnostic>()
                    : new List<FurnitureDiagnostic>(arrangement.developerDiagnostics)
            };
        }
    }
}
