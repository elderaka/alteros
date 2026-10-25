using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlterOS.Stats
{
    [Serializable]
    public sealed class CanonicalStatEligibility
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public List<CanonicalStatCondition> all = new List<CanonicalStatCondition>();
        // Declared only so JsonUtility cannot silently discard these common unsupported rule forms.
        public List<CanonicalStatCondition> any;
        public List<CanonicalStatCondition> none;
        public CanonicalStatEligibility not;
    }

    [Serializable]
    public sealed class CanonicalStatCondition
    {
        public string statId = string.Empty;
        public string operation = string.Empty;
        public float value;
    }

    public enum StatEligibilityStatus
    {
        Eligible,
        Rejected,
        Unsupported,
        Invalid,
    }

    public enum StatConditionOutcome
    {
        Satisfied,
        Failed,
        UnknownValue,
        Unsupported,
        Invalid,
    }

    public sealed class StatConditionEvaluation
    {
        private readonly string statId;
        private readonly string operation;
        private readonly bool hasActualValue;
        private readonly float actualValue;
        private readonly float threshold;
        private readonly StatConditionOutcome outcome;
        private readonly string reason;

        internal StatConditionEvaluation(
            string statId,
            string operation,
            bool hasActualValue,
            float actualValue,
            float threshold,
            StatConditionOutcome outcome,
            string reason)
        {
            this.statId = statId;
            this.operation = operation;
            this.hasActualValue = hasActualValue;
            this.actualValue = actualValue;
            this.threshold = threshold;
            this.outcome = outcome;
            this.reason = reason;
        }

        public string StatId { get { return statId; } }
        public string Operation { get { return operation; } }
        public bool HasActualValue { get { return hasActualValue; } }
        public float ActualValue { get { return actualValue; } }
        public float Threshold { get { return threshold; } }
        public StatConditionOutcome Outcome { get { return outcome; } }
        public string Reason { get { return reason; } }
    }

    public sealed class StatEligibilityEvaluation
    {
        private readonly StatEligibilityStatus status;
        private readonly IReadOnlyList<StatConditionEvaluation> conditions;
        private readonly string reason;

        internal StatEligibilityEvaluation(
            StatEligibilityStatus status,
            List<StatConditionEvaluation> conditions,
            string reason)
        {
            this.status = status;
            this.conditions = conditions.AsReadOnly();
            this.reason = reason;
        }

        public StatEligibilityStatus Status { get { return status; } }
        public IReadOnlyList<StatConditionEvaluation> Conditions { get { return conditions; } }
        public string Reason { get { return reason; } }

        internal static StatEligibilityEvaluation FromConditions(List<StatConditionEvaluation> conditions)
        {
            StatEligibilityStatus status = StatEligibilityStatus.Eligible;
            for (int index = 0; index < conditions.Count; index++)
            {
                switch (conditions[index].Outcome)
                {
                    case StatConditionOutcome.Unsupported:
                        status = StatEligibilityStatus.Unsupported;
                        break;
                    case StatConditionOutcome.Invalid:
                        if (status != StatEligibilityStatus.Unsupported)
                            status = StatEligibilityStatus.Invalid;
                        break;
                    case StatConditionOutcome.Failed:
                    case StatConditionOutcome.UnknownValue:
                        if (status == StatEligibilityStatus.Eligible)
                            status = StatEligibilityStatus.Rejected;
                        break;
                }
            }

            var reasons = new List<string>();
            foreach (StatConditionEvaluation condition in conditions)
                if (!string.IsNullOrWhiteSpace(condition.Reason))
                    reasons.Add(condition.Reason);

            return new StatEligibilityEvaluation(status, conditions, string.Join(" ", reasons));
        }
    }

    public static class StatEligibilityEvaluator
    {
        public static StatEligibilityEvaluation Evaluate(CanonicalStatEligibility eligibility, StatSnapshot snapshot)
        {
            var evaluations = new List<StatConditionEvaluation>();
            if (eligibility == null)
                return StatEligibilityEvaluation.FromConditions(evaluations);

            if (eligibility.schemaVersion != CanonicalStatEligibility.CurrentSchemaVersion)
            {
                evaluations.Add(new StatConditionEvaluation(
                    "routing.eligibility",
                    "schemaVersion",
                    false,
                    0f,
                    CanonicalStatEligibility.CurrentSchemaVersion,
                    StatConditionOutcome.Unsupported,
                    "Unsupported numeric eligibility schema version " + eligibility.schemaVersion + "."));
                return StatEligibilityEvaluation.FromConditions(evaluations);
            }

            if (eligibility.all == null)
            {
                evaluations.Add(new StatConditionEvaluation(
                    "routing.eligibility.all",
                    "conditions",
                    false,
                    0f,
                    0f,
                    StatConditionOutcome.Invalid,
                    "Numeric eligibility 'all' must be a list."));
                return StatEligibilityEvaluation.FromConditions(evaluations);
            }

            foreach (CanonicalStatCondition condition in eligibility.all)
                evaluations.Add(EvaluateCondition(condition, snapshot));

            if (eligibility.any != null && eligibility.any.Count > 0)
                evaluations.Add(UnsupportedRule("routing.eligibility.any", "any"));
            if (eligibility.none != null && eligibility.none.Count > 0)
                evaluations.Add(UnsupportedRule("routing.eligibility.none", "none"));
            if (eligibility.not != null)
                evaluations.Add(UnsupportedRule("routing.eligibility.not", "not"));

            return StatEligibilityEvaluation.FromConditions(evaluations);
        }

        private static StatConditionEvaluation UnsupportedRule(string field, string operation)
        {
            return new StatConditionEvaluation(
                field,
                operation,
                false,
                0f,
                0f,
                StatConditionOutcome.Unsupported,
                "Unsupported numeric eligibility rule '" + operation + "'; only the versioned 'all' list is currently supported.");
        }

        internal static StatConditionEvaluation UnsupportedContext(string field, string value)
        {
            return new StatConditionEvaluation(
                field,
                "context_tag",
                false,
                0f,
                0f,
                StatConditionOutcome.Unsupported,
                "Canonical " + field + " tag '" + value + "' is not supported by the versioned numeric context contract.");
        }

        private static StatConditionEvaluation EvaluateCondition(CanonicalStatCondition condition, StatSnapshot snapshot)
        {
            if (condition == null)
                return new StatConditionEvaluation(
                    "routing.eligibility.all",
                    string.Empty,
                    false,
                    0f,
                    0f,
                    StatConditionOutcome.Invalid,
                    "Numeric eligibility contains a null condition.");

            StatDefinition definition;
            if (!StatCatalogue.TryGet(condition.statId, out definition))
                return new StatConditionEvaluation(
                    condition.statId,
                    condition.operation,
                    false,
                    0f,
                    condition.value,
                    StatConditionOutcome.Unsupported,
                    "Unsupported stat ID in numeric condition: " + (condition.statId ?? "<null>") + ".");

            bool atMost = string.Equals(condition.operation, "at_most", StringComparison.Ordinal);
            bool atLeast = string.Equals(condition.operation, "at_least", StringComparison.Ordinal);
            if (!atMost && !atLeast)
                return new StatConditionEvaluation(
                    condition.statId,
                    condition.operation,
                    false,
                    0f,
                    condition.value,
                    StatConditionOutcome.Unsupported,
                    "Unsupported numeric condition operator '" + (condition.operation ?? "<null>") + "'.");

            if (float.IsNaN(condition.value) || float.IsInfinity(condition.value) ||
                condition.value < definition.Minimum || condition.value > definition.Maximum)
            {
                return new StatConditionEvaluation(
                    condition.statId,
                    condition.operation,
                    false,
                    0f,
                    condition.value,
                    StatConditionOutcome.Invalid,
                    "Condition threshold for " + condition.statId + " must be finite and within catalogue bounds.");
            }

            float actualValue;
            if (snapshot == null || !snapshot.TryGet(condition.statId, out actualValue))
            {
                string relation = atMost ? "≤ " : "≥ ";
                return new StatConditionEvaluation(
                    condition.statId,
                    condition.operation,
                    false,
                    0f,
                    condition.value,
                    StatConditionOutcome.UnknownValue,
                    condition.statId + " is unknown; a known value " + relation + Format(condition.value) + " is required.");
            }

            bool satisfied = atMost ? actualValue <= condition.value : actualValue >= condition.value;
            string requirement = atMost ? "≤ " : "≥ ";
            string reason = condition.statId + " = " + Format(actualValue) +
                            (satisfied ? " satisfies " : " does not satisfy ") +
                            requirement + Format(condition.value) + ".";
            return new StatConditionEvaluation(
                condition.statId,
                condition.operation,
                true,
                actualValue,
                condition.value,
                satisfied ? StatConditionOutcome.Satisfied : StatConditionOutcome.Failed,
                reason);
        }

        private static string Format(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }
    }
}
