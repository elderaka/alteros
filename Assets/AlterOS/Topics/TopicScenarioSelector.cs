using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Topics
{
    public sealed class TopicWeightTrace
    {
        public TopicConditionResult Condition { get; internal set; }
        public TopicTruth Truth { get { return Condition.Truth; } }
        public double AuthoredAmount { get; internal set; }
        public double AppliedAmount { get; internal set; }
    }

    public sealed class TopicCandidateTrace
    {
        public string ScenarioId { get; internal set; }
        public int Priority { get; internal set; }
        public double EffectiveWeight { get; internal set; }
        public string Reason { get; internal set; }
        public TopicConditionResult Gate { get; internal set; }
        public IReadOnlyList<TopicWeightTrace> Modifiers { get; internal set; }
    }

    public sealed class TopicSelectionResult
    {
        public TopicScenario SelectedScenario { get; internal set; }
        public string Reason { get; internal set; }
        public double Draw { get; internal set; }
        public int? WinningPriority { get; internal set; }
        public IReadOnlyList<TopicCandidateTrace> Candidates { get; internal set; }
    }

    public static class TopicScenarioSelector
    {
        public static TopicSelectionResult Select(TopicDefinition definition, TopicInstance instance,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw)
        { return SelectCore(definition, instance, context, history, nowUtc, draw, null, false); }

        public static TopicSelectionResult SelectForConversation(TopicDefinition definition, TopicInstance instance,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw, string channel)
        {
            if (channel != "direct" && channel != "phone") throw new ArgumentException("Unsupported conversation channel.", nameof(channel));
            return SelectCore(definition, instance, context, history, nowUtc, draw, channel, true);
        }

        private static TopicSelectionResult SelectCore(TopicDefinition definition, TopicInstance instance,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw, string channel, bool preferUnseen)
        {
            TopicDefinitionValidator.RequireValid(definition, instance);
            if (!TopicDefinitionValidator.Finite(draw) || draw < 0 || draw >= 1)
                throw new ArgumentOutOfRangeException(nameof(draw));
            if (history == null) throw new ArgumentNullException(nameof(history));
            // Validate context even when history prevents selection; invalid fixtures are not silent no-matches.
            TopicConditionEvaluator.Evaluate(null, context);
            string unavailable = history.Unavailability(instance, nowUtc);
            var ordered = new List<TopicScenario>(definition.scenarios);
            ordered.Sort((a, b) => string.Compare(a.scenarioId, b.scenarioId, StringComparison.Ordinal));
            var checks = new List<TopicCandidateTrace>();
            int? highest = null;
            foreach (TopicScenario scenario in ordered)
            {
                var modifiers = new List<TopicWeightTrace>();
                var trace = new TopicCandidateTrace
                { ScenarioId = scenario.scenarioId, Priority = scenario.priority, Modifiers = modifiers.AsReadOnly() };
                checks.Add(trace);
                if (unavailable != null) { trace.Reason = unavailable; continue; }
                if (channel != null && scenario.channels != null && scenario.channels.Count > 0 && !scenario.channels.Contains(channel))
                { trace.Reason = "WrongChannel"; continue; }
                trace.Gate = TopicConditionEvaluator.Evaluate(scenario.condition, context);
                if (trace.Gate.Truth != TopicTruth.True) { trace.Reason = "Gate " + trace.Gate.Truth; continue; }
                double weight = scenario.baseWeight;
                foreach (TopicWeightModifier modifier in scenario.modifiers)
                {
                    TopicConditionResult evaluated = TopicConditionEvaluator.Evaluate(modifier.condition, context);
                    double applied = evaluated.Truth == TopicTruth.True ? modifier.amount : 0;
                    modifiers.Add(new TopicWeightTrace { Condition = evaluated, AuthoredAmount = modifier.amount, AppliedAmount = applied });
                    weight += applied;
                    if (!TopicDefinitionValidator.Finite(weight)) throw new ArgumentException("Nonfinite accumulated weight.");
                }
                trace.EffectiveWeight = Math.Max(0, weight);
                if (trace.EffectiveWeight == 0) { trace.Reason = "ZeroWeight"; continue; }
                trace.Reason = "WeightedAlternative";
                if (!highest.HasValue || scenario.priority > highest.Value) highest = scenario.priority;
            }

            var result = new TopicSelectionResult
            { Reason = unavailable ?? "No selectable scenario", Draw = draw, WinningPriority = highest, Candidates = checks.AsReadOnly() };
            if (!highest.HasValue) return result;
            bool hasUnseen = preferUnseen && checks.Any(check => check.Priority == highest.Value && check.EffectiveWeight > 0 &&
                history.GetCompletionCount(instance.instanceId, check.ScenarioId) == 0);
            if (hasUnseen)
                foreach (TopicCandidateTrace check in checks)
                    if (check.Priority == highest.Value && check.EffectiveWeight > 0 && history.GetCompletionCount(instance.instanceId, check.ScenarioId) > 0)
                    { check.EffectiveWeight = 0; check.Reason = "PreviouslyCompleted"; }
            double total = 0;
            foreach (TopicCandidateTrace check in checks)
            {
                if (check.EffectiveWeight <= 0) continue;
                if (check.Priority < highest.Value) { check.Reason = "LowerPriority"; continue; }
                total += check.EffectiveWeight;
            }
            if (!TopicDefinitionValidator.Finite(total) || total <= 0) throw new ArgumentException("Invalid weight total.");
            double target = draw * total;
            TopicScenario last = null;
            foreach (TopicScenario scenario in ordered)
            {
                TopicCandidateTrace check = checks.Find(c => c.ScenarioId == scenario.scenarioId);
                if (check.Priority != highest.Value || check.EffectiveWeight <= 0) continue;
                last = scenario;
                if (target < check.EffectiveWeight) { result.SelectedScenario = scenario; break; }
                target -= check.EffectiveWeight;
            }
            // Absorb final-bit floating-point rounding, never choose a different priority tier.
            if (result.SelectedScenario == null) result.SelectedScenario = last;
            checks.Find(c => c.ScenarioId == result.SelectedScenario.scenarioId).Reason = "Selected";
            result.Reason = "Selected";
            return result;
        }
    }
}
