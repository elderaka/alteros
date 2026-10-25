using System;
using System.Collections.Generic;
using AlterOS.Dialogue;
using AlterOS.Stats;

namespace AlterOS.Topics
{
    public static class TopicConditionEvaluator
    {
        public static TopicConditionResult Evaluate(TopicCondition rule, TopicContext context)
        {
            List<string> errors = TopicDefinitionValidator.ValidateCondition(rule);
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
            if (context == null || context.localHour < -1 || context.localHour > 23)
                throw new ArgumentException("Context/hour is invalid.");
            if (context.objectsComplete && context.objects == null)
                throw new ArgumentException("Complete inventory must supply a list, including an empty list.");
            if (context.objects != null)
            {
                if (context.objects.Count > 1024) throw new ArgumentException("Object snapshot limit exceeded.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (TopicObjectSnapshot item in context.objects)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.id) || string.IsNullOrWhiteSpace(item.kind) || !ids.Add(item.id))
                        throw new ArgumentException("Objects require unique IDs and kinds.");
                    if (item.numbers != null)
                        foreach (double number in item.numbers.Values)
                            if (!TopicDefinitionValidator.Finite(number)) throw new ArgumentException("Nonfinite object property.");
                }
            }
            return Visit(rule, context, null);
        }

        private static TopicConditionResult Visit(TopicCondition rule, TopicContext context, TopicObjectSnapshot bound)
        {
            string binding = bound == null ? null : bound.id;
            if (rule == null) return Result(TopicTruth.True, "No extra condition.", binding);
            switch (rule.kind)
            {
                case "all": case "any": case "not":
                    var children = new List<TopicConditionResult>();
                    foreach (TopicCondition child in rule.children) children.Add(Visit(child, context, bound));
                    TopicTruth truth = Combine(children, rule.kind == "any");
                    if (rule.kind == "not") truth = children[0].Truth == TopicTruth.Unknown ? TopicTruth.Unknown :
                        children[0].Truth == TopicTruth.True ? TopicTruth.False : TopicTruth.True;
                    return new TopicConditionResult(truth, rule.kind, binding, children);
                case "stat":
                    StatConditionEvaluation evaluated = StatEligibilityEvaluator.Evaluate(new CanonicalStatEligibility
                    { all = new List<CanonicalStatCondition> { new CanonicalStatCondition
                        { statId = rule.statId, operation = rule.operation, value = rule.value } } }, context.stats).Conditions[0];
                    return Result(evaluated.Outcome == StatConditionOutcome.UnknownValue ? TopicTruth.Unknown :
                        evaluated.Outcome == StatConditionOutcome.Satisfied ? TopicTruth.True : TopicTruth.False,
                        evaluated.Reason, binding);
                case "timeWindow":
                    if (context.localHour == -1) return Result(TopicTruth.Unknown, "Local hour is unknown.", binding);
                    return Result(CanonicalTimeWindow.IsWithin(rule.startHour, rule.endHour, context.localHour)
                        ? TopicTruth.True : TopicTruth.False, "Local hour window.", binding);
                case "fact":
                    return Boolean(context.facts, rule, binding);
                case "objectFlag":
                    return Boolean(bound.flags, rule, binding);
                case "objectNumber":
                    double value;
                    if (bound.numbers == null || !bound.numbers.TryGetValue(rule.key, out value))
                        return Result(TopicTruth.Unknown, "Missing object number: " + rule.key, binding);
                    bool matches = rule.operation == "at_most" ? value <= rule.value : value >= rule.value;
                    return Result(matches ? TopicTruth.True : TopicTruth.False, "Object number: " + rule.key, binding);
                case "object":
                    return Bind(rule, context);
                default: throw new ArgumentException("Unvalidated condition kind.");
            }
        }

        private static TopicConditionResult Bind(TopicCondition rule, TopicContext context)
        {
            var objects = new List<TopicObjectSnapshot>();
            if (context.objects != null)
                foreach (TopicObjectSnapshot item in context.objects)
                    if (item.kind == rule.objectKind) objects.Add(item);
            objects.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
            var attempts = new List<TopicConditionResult>();
            bool unknown = !context.objectsComplete;
            foreach (TopicObjectSnapshot item in objects)
            {
                var properties = new List<TopicConditionResult>();
                foreach (TopicCondition child in rule.children) properties.Add(Visit(child, context, item));
                TopicTruth truth = Combine(properties, false);
                attempts.Add(new TopicConditionResult(truth, "Object " + item.id, item.id, properties));
                if (truth == TopicTruth.True)
                    return new TopicConditionResult(TopicTruth.True, "Bound " + rule.objectKind, item.id, attempts);
                if (truth == TopicTruth.Unknown) unknown = true;
            }
            return new TopicConditionResult(unknown ? TopicTruth.Unknown : TopicTruth.False,
                "No proven matching object: " + rule.objectKind, null, attempts);
        }

        private static TopicConditionResult Boolean(Dictionary<string, bool> facts, TopicCondition rule, string binding)
        {
            bool value;
            if (facts == null || !facts.TryGetValue(rule.key, out value))
                return Result(TopicTruth.Unknown, "Missing fact/property: " + rule.key, binding);
            return Result(value == rule.expected ? TopicTruth.True : TopicTruth.False, "Fact/property: " + rule.key, binding);
        }

        private static TopicTruth Combine(List<TopicConditionResult> children, bool any)
        {
            bool unknown = false;
            foreach (TopicConditionResult child in children)
            {
                if (any && child.Truth == TopicTruth.True) return TopicTruth.True;
                if (!any && child.Truth == TopicTruth.False) return TopicTruth.False;
                if (child.Truth == TopicTruth.Unknown) unknown = true;
            }
            return unknown ? TopicTruth.Unknown : any ? TopicTruth.False : TopicTruth.True;
        }

        private static TopicConditionResult Result(TopicTruth truth, string reason, string binding)
        { return new TopicConditionResult(truth, reason, binding); }
    }
}
