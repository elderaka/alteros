using System;
using System.Collections.Generic;
using AlterOS.Content;
using AlterOS.Dialogue;
using AlterOS.Stats;

namespace AlterOS.Topics
{
    public static class TopicDefinitionValidator
    {
        public const int MaximumDepth = 16;
        public const int MaximumConditionNodes = 256;
        public const double MaximumWeight = 1000000;

        public static List<string> Validate(TopicDefinition definition)
        {
            var errors = new List<string>();
            if (definition == null) { errors.Add("Topic definition is missing."); return errors; }
            if (definition.schemaVersion != 1) errors.Add("Unsupported Topic family schema version.");
            if (string.IsNullOrWhiteSpace(definition.topicId)) errors.Add("topicId is required.");
            if (!TopicReplayPolicy.IsValid(definition.defaultReplayPolicy)) errors.Add("Invalid replay policy.");
            if (!Finite(definition.cooldownSeconds) || definition.cooldownSeconds < 0 || definition.cooldownSeconds > 31536000)
                errors.Add("Cooldown must be finite, nonnegative and at most one year.");
            if (definition.anchors == null || !definition.anchors.Exists(a => !string.IsNullOrWhiteSpace(a)))
                errors.Add("Shared Topic anchor bank is required.");
            if (definition.scenarios == null || definition.scenarios.Count == 0 || definition.scenarios.Count > 256)
            { errors.Add("A Topic must have 1..256 scenarios."); return errors; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TopicScenario scenario in definition.scenarios)
            {
                if (scenario == null) { errors.Add("Null scenario."); continue; }
                if (string.IsNullOrWhiteSpace(scenario.scenarioId) || !ids.Add(scenario.scenarioId))
                    errors.Add("Scenario IDs must be nonempty and unique.");
                if (!Finite(scenario.baseWeight) || scenario.baseWeight < 0 || scenario.baseWeight > MaximumWeight)
                    errors.Add("Scenario baseWeight must be within 0..1000000.");
                if (scenario.channels != null)
                {
                    var channels = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string channel in scenario.channels)
                        if ((channel != "direct" && channel != "phone") || !channels.Add(channel))
                            errors.Add("Scenario channels must be a unique subset of direct and phone.");
                }
                errors.AddRange(ValidateCondition(scenario.condition));
                if (scenario.modifiers == null || scenario.modifiers.Count > 256) errors.Add("Invalid modifier list.");
                else foreach (TopicWeightModifier modifier in scenario.modifiers)
                {
                    if (modifier == null || modifier.condition == null) { errors.Add("Modifier condition required."); continue; }
                    if (!Finite(modifier.amount) || Math.Abs(modifier.amount) > MaximumWeight) errors.Add("Modifier amount exceeds bounds.");
                    errors.AddRange(ValidateCondition(modifier.condition));
                }
                if (scenario.scenarioRef != null)
                {
                    if (!ValidScenarioReference(scenario.scenarioRef))
                        errors.Add("scenarioRef must pin a local Scenario Flow by store, id, revision and expected hash.");
                    if (scenario.nodes != null && scenario.nodes.Count > 0)
                        errors.Add("A Topic entry must use either scenarioRef or inline nodes, not both.");
                    // The selected pinned Flow owns nodes and its checkpoint is resolved by the content compiler.
                    continue;
                }

                if (scenario.nodes == null || scenario.nodes.Count > 4096) { errors.Add("Invalid graph size."); continue; }
                // Project the shared bank into the existing graph validator; never duplicate its traversal rules.
                var graph = new DialogueDefinition
                {
                    id = scenario.scenarioId,
                    routing = new DialogueRouting { routeId = definition.topicId, examples = definition.anchors },
                    nodes = scenario.nodes
                };
                errors.AddRange(DialogueValidator.Validate(graph));
                if (!string.IsNullOrEmpty(scenario.consumptionNodeId) &&
                    !scenario.nodes.Exists(n => n != null && n.id == scenario.consumptionNodeId))
                    errors.Add("Consumption checkpoint must reference a graph node.");
            }
            return errors;
        }

        public static List<string> ValidateCondition(TopicCondition condition)
        {
            var errors = new List<string>(); int count = 0;
            if (condition != null) Check(condition, 1, false, ref count, errors);
            return errors;
        }

        private static void Check(TopicCondition rule, int depth, bool bound, ref int count, List<string> errors)
        {
            if (depth > MaximumDepth || ++count > MaximumConditionNodes) { errors.Add("Condition tree exceeds limits."); return; }
            if (rule == null) { errors.Add("Null condition child."); return; }
            if (!rule.expected && rule.kind != "fact" && rule.kind != "objectFlag")
                errors.Add("expected is only supported on boolean fact/property conditions; use not for other kinds.");
            int children = rule.children == null ? -1 : rule.children.Count;
            bool group = rule.kind == "all" || rule.kind == "any" || rule.kind == "not" || rule.kind == "object";
            if (group)
            {
                if (children < 0 || children > MaximumConditionNodes ||
                    (rule.kind != "object" && children == 0) || (rule.kind == "not" && children != 1))
                { errors.Add("Invalid condition children."); return; }
                if (rule.kind == "object" && (bound || string.IsNullOrWhiteSpace(rule.objectKind)))
                    errors.Add("Object binding requires a kind and cannot nest another binding.");
                if (!string.IsNullOrEmpty(rule.statId) || !string.IsNullOrEmpty(rule.operation) ||
                    !string.IsNullOrEmpty(rule.key) || rule.value != 0 || rule.startHour != -1 || rule.endHour != -1 ||
                    (rule.kind != "object" && !string.IsNullOrEmpty(rule.objectKind))) errors.Add("Misplaced group fields.");
                foreach (TopicCondition child in rule.children)
                {
                    if (count >= MaximumConditionNodes) { errors.Add("Condition tree exceeds limits."); break; }
                    Check(child, depth + 1, bound || rule.kind == "object", ref count, errors);
                }
                return;
            }
            if (children != 0 || !string.IsNullOrEmpty(rule.objectKind)) errors.Add("Leaf cannot contain children/objectKind.");
            switch (rule.kind)
            {
                case "stat":
                    var check = StatEligibilityEvaluator.Evaluate(new CanonicalStatEligibility
                    { all = new List<CanonicalStatCondition> { new CanonicalStatCondition
                        { statId = rule.statId, operation = rule.operation, value = rule.value } } }, new StatSnapshot());
                    if (check.Status == StatEligibilityStatus.Invalid || check.Status == StatEligibilityStatus.Unsupported)
                        errors.Add(check.Reason);
                    if (!string.IsNullOrEmpty(rule.key) || rule.startHour != -1 || rule.endHour != -1) errors.Add("Misplaced stat fields.");
                    break;
                case "timeWindow":
                    if (rule.startHour < 0 || rule.startHour > 23 || rule.endHour < 0 || rule.endHour > 23)
                        errors.Add("Time hours must be 0..23.");
                    if (!string.IsNullOrEmpty(rule.statId) || !string.IsNullOrEmpty(rule.key) || !string.IsNullOrEmpty(rule.operation) || rule.value != 0)
                        errors.Add("Misplaced time fields.");
                    break;
                case "fact": case "objectFlag": case "objectNumber":
                    if (string.IsNullOrWhiteSpace(rule.key)) errors.Add("Fact/property key required.");
                    if (rule.kind != "fact" && !bound) errors.Add("Object properties require an object binding.");
                    if (!string.IsNullOrEmpty(rule.statId) || rule.startHour != -1 || rule.endHour != -1) errors.Add("Misplaced property fields.");
                    if (rule.kind == "objectNumber")
                    {
                        if ((rule.operation != "at_most" && rule.operation != "at_least") || !Finite(rule.value))
                            errors.Add("Unsupported object-number operation/value.");
                    }
                    else if (!string.IsNullOrEmpty(rule.operation) || rule.value != 0) errors.Add("Misplaced boolean fields.");
                    break;
                default: errors.Add("Unsupported condition kind: " + rule.kind); break;
            }
        }

        internal static void RequireValid(TopicDefinition definition, TopicInstance instance)
        {
            var errors = Validate(definition);
            if (instance == null || string.IsNullOrWhiteSpace(instance.instanceId) || definition == null ||
                instance.topicId != definition.topicId || (instance.source != "authored" && instance.source != "generated") ||
                !TopicReplayPolicy.IsValid(instance.replayPolicy)) errors.Add("Invalid Topic instance identity/source/policy.");
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        }

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool ValidScenarioReference(ContentRef reference)
        {
            Guid store;
            return reference != null && Guid.TryParse(reference.store, out store) &&
                !string.IsNullOrWhiteSpace(reference.id) && reference.id.Length <= 128 &&
                IsSha256(reference.revision) && IsSha256(reference.expectedHash);
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'))) return false;
            return true;
        }
    }
}
