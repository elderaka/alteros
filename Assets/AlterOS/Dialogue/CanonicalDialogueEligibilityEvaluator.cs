using System.Collections.Generic;
using AlterOS.Stats;

namespace AlterOS.Dialogue
{
    public static class CanonicalDialogueEligibilityEvaluator
    {
        public static StatEligibilityEvaluation Evaluate(DialogueRouting routing, StatSnapshot snapshot)
        {
            if (routing == null)
            {
                var invalid = new List<StatConditionEvaluation>
                {
                    new StatConditionEvaluation(
                        "routing",
                        string.Empty,
                        false,
                        0f,
                        0f,
                        StatConditionOutcome.Invalid,
                        "Canonical dialogue routing is missing.")
                };
                return StatEligibilityEvaluation.FromConditions(invalid);
            }

            StatEligibilityEvaluation numeric = StatEligibilityEvaluator.Evaluate(routing.eligibility, snapshot);
            var conditions = new List<StatConditionEvaluation>(numeric.Conditions);
            AddUnsupportedTags(conditions, "requiredContext", routing.requiredContext);
            AddUnsupportedTags(conditions, "blockedContext", routing.blockedContext);
            return StatEligibilityEvaluation.FromConditions(conditions);
        }

        private static void AddUnsupportedTags(
            List<StatConditionEvaluation> conditions,
            string field,
            List<string> values)
        {
            if (values == null)
                return;

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    conditions.Add(StatEligibilityEvaluator.UnsupportedContext("routing." + field, value));
            }
        }
    }
}
