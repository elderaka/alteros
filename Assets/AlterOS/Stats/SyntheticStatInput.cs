using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlterOS.Stats
{
    [Serializable]
    public sealed class SyntheticStatInput
    {
        public string statId = string.Empty;
        public bool isKnown;
        public float value;
    }

    [Serializable]
    public sealed class SyntheticFactInput
    {
        public string key = string.Empty;
        public bool isKnown;
        public bool value;
    }

    [Serializable]
    public sealed class SyntheticObjectFlagInput
    {
        public string key = string.Empty;
        public bool isKnown;
        public bool value;
    }

    [Serializable]
    public sealed class SyntheticObjectNumberInput
    {
        public string key = string.Empty;
        public bool isKnown;
        public double value;
    }

    [Serializable]
    public sealed class SyntheticObjectInput
    {
        public string id = string.Empty;
        public string kind = string.Empty;
        public List<SyntheticObjectFlagInput> flags = new List<SyntheticObjectFlagInput>();
        public List<SyntheticObjectNumberInput> numbers = new List<SyntheticObjectNumberInput>();
    }

    public static class SyntheticStatSnapshotBuilder
    {
        public static bool TryBuild(
            IEnumerable<SyntheticStatInput> inputs,
            out StatSnapshot snapshot,
            out List<string> errors)
        {
            snapshot = new StatSnapshot();
            errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;

            if (inputs != null)
            {
                foreach (SyntheticStatInput input in inputs)
                {
                    if (input == null)
                    {
                        errors.Add("stats[" + index + "] is null.");
                        index++;
                        continue;
                    }

                    StatDefinition definition;
                    if (!StatCatalogue.TryGet(input.statId, out definition))
                    {
                        errors.Add("stats[" + index + "] has unknown stat ID: " + (input.statId ?? "<null>"));
                        index++;
                        continue;
                    }

                    if (!seen.Add(input.statId))
                    {
                        errors.Add("stats[" + index + "] duplicates stat ID: " + input.statId);
                        index++;
                        continue;
                    }

                    if (input.isKnown)
                    {
                        string error;
                        if (!snapshot.TrySet(input.statId, input.value, out error))
                            errors.Add("stats[" + index + "]: " + error);
                    }

                    index++;
                }
            }

            if (errors.Count == 0)
                return true;

            snapshot = new StatSnapshot();
            return false;
        }
    }
}

namespace AlterOS.Dialogue
{
    [Serializable]
    public sealed class TopicPlaygroundScenario
    {
        public const string GenericRouteMode = "generic-route";
        public const string SemanticFamilyMode = "semantic-family";
        public const string DirectFamilyMode = "direct-family";

        public string query;
        public int localHour = -1;
        public string fixtureSet = string.Empty;
        public string topicMode = "generic-route";
        public string topicFixtureSet = "topic-runtime-v1";
        public string topicFamilyId = string.Empty;
        public string instanceId = "playground-occurrence-1";
        public string source = "authored";
        public string replayPolicy = string.Empty;
        public double draw;
        public string selectionTimeUtc = "2026-10-16T12:00:00Z";
        public string scratchHistoryPath = string.Empty;
        public List<AlterOS.Stats.SyntheticStatInput> stats = new List<AlterOS.Stats.SyntheticStatInput>();
        public List<AlterOS.Stats.SyntheticFactInput> facts = new List<AlterOS.Stats.SyntheticFactInput>();
        public bool objectsComplete;
        public List<AlterOS.Stats.SyntheticObjectInput> objects = new List<AlterOS.Stats.SyntheticObjectInput>();
        public string expectedRouteId;
        public List<TopicPlaygroundScenarioStep> steps = new List<TopicPlaygroundScenarioStep>();

        public static bool TryParseSelectionTimeUtc(string value, out DateTimeOffset parsed)
        {
            parsed = default(DateTimeOffset);
            if (string.IsNullOrWhiteSpace(value))
                return false;

            bool explicitZone = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            int timeStart = value.IndexOf('T');
            if (!explicitZone && timeStart >= 0)
            {
                for (int index = timeStart + 1; index < value.Length; index++)
                {
                    if (value[index] == '+' || value[index] == '-')
                    {
                        explicitZone = true;
                        break;
                    }
                }
            }

            return explicitZone && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out parsed);
        }

        public static bool TryNormalizeTopicMode(string value, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, GenericRouteMode, StringComparison.Ordinal))
            {
                normalized = GenericRouteMode;
                return true;
            }
            if (string.Equals(value, SemanticFamilyMode, StringComparison.Ordinal) ||
                string.Equals(value, DirectFamilyMode, StringComparison.Ordinal))
            {
                normalized = value;
                return true;
            }
            normalized = string.Empty;
            return false;
        }
    }

    [Serializable]
    public sealed class TopicPlaygroundScenarioStep
    {
        public string action;
        public int index;
        public string instanceId = string.Empty;
        public double secondsAfterStart;
    }
}

namespace AlterOS.Stats
{
    /// <summary>Detached synthetic context shared by the Topic Playground surfaces.</summary>
    public sealed class TopicPlaygroundContext
    {
        private readonly int localHour;
        private readonly string fixtureSet;
        private readonly StatSnapshot stats;
        private readonly bool objectsComplete;
        private readonly List<SyntheticFactInput> factInputs;
        private readonly List<SyntheticObjectInput> objectInputs;

        internal TopicPlaygroundContext(
            int localHour,
            string fixtureSet,
            StatSnapshot stats,
            bool objectsComplete,
            List<SyntheticFactInput> factInputs,
            List<SyntheticObjectInput> objectInputs)
        {
            this.localHour = localHour;
            this.fixtureSet = fixtureSet ?? string.Empty;
            this.stats = stats.Clone();
            this.objectsComplete = objectsComplete;
            this.factInputs = CloneFacts(factInputs);
            this.objectInputs = CloneSyntheticObjects(objectInputs);
        }

        public int LocalHour { get { return localHour; } }
        public string FixtureSet { get { return fixtureSet; } }
        public bool ObjectsComplete { get { return objectsComplete; } }

        public bool TryGet(string statId, out float value)
        {
            return stats.TryGet(statId, out value);
        }

        public StatSnapshot CopyStats()
        {
            return stats.Clone();
        }

        public List<SyntheticStatInput> CopyStatInputs()
        {
            var inputs = new List<SyntheticStatInput>(StatCatalogue.Definitions.Count);
            foreach (StatDefinition definition in StatCatalogue.Definitions)
            {
                float value;
                bool isKnown = stats.TryGet(definition.Id, out value);
                inputs.Add(new SyntheticStatInput
                {
                    statId = definition.Id,
                    isKnown = isKnown,
                    value = isKnown ? value : 0f,
                });
            }
            return inputs;
        }

        public List<SyntheticFactInput> CopyFactInputs()
        {
            return CloneFacts(factInputs);
        }

        public List<SyntheticObjectInput> CopyObjectInputs()
        {
            return CloneSyntheticObjects(objectInputs);
        }

        static List<SyntheticFactInput> CloneFacts(List<SyntheticFactInput> source)
        {
            var copy = new List<SyntheticFactInput>();
            if (source != null)
                foreach (SyntheticFactInput input in source)
                    copy.Add(input == null ? null : new SyntheticFactInput { key = input.key, isKnown = input.isKnown, value = input.value });
            return copy;
        }

        static List<SyntheticObjectInput> CloneSyntheticObjects(List<SyntheticObjectInput> source)
        {
            var copy = new List<SyntheticObjectInput>();
            if (source != null)
            {
                foreach (SyntheticObjectInput input in source)
                {
                    if (input == null)
                    {
                        copy.Add(null);
                        continue;
                    }
                    var clone = new SyntheticObjectInput { id = input.id, kind = input.kind };
                    if (input.flags != null)
                        foreach (SyntheticObjectFlagInput flag in input.flags)
                            clone.flags.Add(flag == null ? null : new SyntheticObjectFlagInput
                                { key = flag.key, isKnown = flag.isKnown, value = flag.value });
                    if (input.numbers != null)
                        foreach (SyntheticObjectNumberInput number in input.numbers)
                            clone.numbers.Add(number == null ? null : new SyntheticObjectNumberInput
                                { key = number.key, isKnown = number.isKnown, value = number.value });
                    copy.Add(clone);
                }
            }
            return copy;
        }

    }

    /// <summary>Shared validation boundary for Playground window and scenario inputs.</summary>
    public static class TopicPlaygroundContextBuilder
    {
        public static bool TryBuild(
            int localHour,
            IEnumerable<SyntheticStatInput> stats,
            out TopicPlaygroundContext context,
            out List<string> errors)
        {
            return TryBuild(localHour, stats, string.Empty, out context, out errors);
        }

        public static bool TryBuild(
            int localHour,
            IEnumerable<SyntheticStatInput> stats,
            string fixtureSet,
            out TopicPlaygroundContext context,
            out List<string> errors)
        {
            return TryBuild(localHour, stats, fixtureSet, null, false, null, out context, out errors);
        }

        public static bool TryBuild(
            int localHour,
            IEnumerable<SyntheticStatInput> stats,
            string fixtureSet,
            IEnumerable<SyntheticFactInput> factInputs,
            bool objectsComplete,
            IEnumerable<SyntheticObjectInput> objectInputs,
            out TopicPlaygroundContext context,
            out List<string> errors)
        {
            context = null;
            errors = new List<string>();
            if (localHour < -1 || localHour > 23)
                errors.Add("localHour must be -1 (unknown) or an integer from 0 through 23.");

            if (!string.IsNullOrEmpty(fixtureSet) &&
                !string.Equals(fixtureSet, "stat-context-v1", StringComparison.Ordinal))
                errors.Add("fixtureSet must be empty (authored catalogue only) or 'stat-context-v1'.");

            StatSnapshot snapshot;
            List<string> statErrors;
            bool statsValid = SyntheticStatSnapshotBuilder.TryBuild(stats, out snapshot, out statErrors);
            errors.AddRange(statErrors);

            List<string> factErrors;
            List<SyntheticFactInput> detachedFacts = CloneFacts(factInputs);
            bool factsValid = ValidateFacts(detachedFacts, out factErrors);
            errors.AddRange(factErrors);

            List<string> objectErrors;
            List<SyntheticObjectInput> detachedObjects = CloneSyntheticObjects(objectInputs);
            bool objectsValid = ValidateObjects(detachedObjects, out objectErrors);
            errors.AddRange(objectErrors);

            if (errors.Count != 0 || !statsValid || !factsValid || !objectsValid)
                return false;

            context = new TopicPlaygroundContext(localHour, fixtureSet, snapshot, objectsComplete,
                detachedFacts, detachedObjects);
            return true;
        }

        static List<SyntheticFactInput> CloneFacts(IEnumerable<SyntheticFactInput> inputs)
        {
            var copy = new List<SyntheticFactInput>();
            if (inputs != null)
                foreach (SyntheticFactInput input in inputs)
                    copy.Add(input == null ? null : new SyntheticFactInput { key = input.key, isKnown = input.isKnown, value = input.value });
            return copy;
        }

        static List<SyntheticObjectInput> CloneSyntheticObjects(IEnumerable<SyntheticObjectInput> inputs)
        {
            var copy = new List<SyntheticObjectInput>();
            if (inputs != null)
            {
                foreach (SyntheticObjectInput input in inputs)
                {
                    if (input == null)
                    {
                        copy.Add(null);
                        continue;
                    }
                    var clone = new SyntheticObjectInput { id = input.id, kind = input.kind };
                    if (input.flags != null)
                        foreach (SyntheticObjectFlagInput flag in input.flags)
                            clone.flags.Add(flag == null ? null : new SyntheticObjectFlagInput
                                { key = flag.key, isKnown = flag.isKnown, value = flag.value });
                    if (input.numbers != null)
                        foreach (SyntheticObjectNumberInput number in input.numbers)
                            clone.numbers.Add(number == null ? null : new SyntheticObjectNumberInput
                                { key = number.key, isKnown = number.isKnown, value = number.value });
                    copy.Add(clone);
                }
            }
            return copy;
        }

        static bool ValidateFacts(IEnumerable<SyntheticFactInput> inputs, out List<string> errors)
        {
            errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            if (inputs != null)
            {
                foreach (SyntheticFactInput input in inputs)
                {
                    if (input == null || string.IsNullOrWhiteSpace(input.key))
                        errors.Add("facts[" + index + "] requires a nonempty key.");
                    else if (!seen.Add(input.key))
                        errors.Add("facts[" + index + "] duplicates key: " + input.key);
                    index++;
                }
            }
            return errors.Count == 0;
        }

        static bool ValidateObjects(IEnumerable<SyntheticObjectInput> inputs, out List<string> errors)
        {
            errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int objectIndex = 0;
            if (inputs != null)
            {
                foreach (SyntheticObjectInput input in inputs)
                {
                    if (objectIndex >= 1024)
                    {
                        errors.Add("objects exceeds the Topic runtime limit of 1024 snapshots.");
                        break;
                    }
                    if (input == null || string.IsNullOrWhiteSpace(input.id) || string.IsNullOrWhiteSpace(input.kind))
                    {
                        errors.Add("objects[" + objectIndex + "] requires a nonempty ID and kind.");
                        objectIndex++;
                        continue;
                    }
                    if (!ids.Add(input.id))
                    {
                        errors.Add("objects[" + objectIndex + "] duplicates ID: " + input.id);
                        objectIndex++;
                        continue;
                    }

                    var flagKeys = new HashSet<string>(StringComparer.Ordinal);
                    int flagIndex = 0;
                    if (input.flags != null)
                    {
                        foreach (SyntheticObjectFlagInput flag in input.flags)
                        {
                            if (flag == null || string.IsNullOrWhiteSpace(flag.key))
                                errors.Add("objects[" + objectIndex + "].flags[" + flagIndex + "] requires a nonempty key.");
                            else if (!flagKeys.Add(flag.key))
                                errors.Add("objects[" + objectIndex + "].flags[" + flagIndex + "] duplicates key: " + flag.key);
                            flagIndex++;
                        }
                    }

                    var numberKeys = new HashSet<string>(StringComparer.Ordinal);
                    int numberIndex = 0;
                    if (input.numbers != null)
                    {
                        foreach (SyntheticObjectNumberInput number in input.numbers)
                        {
                            if (number == null || string.IsNullOrWhiteSpace(number.key))
                                errors.Add("objects[" + objectIndex + "].numbers[" + numberIndex + "] requires a nonempty key.");
                            else if (!numberKeys.Add(number.key))
                                errors.Add("objects[" + objectIndex + "].numbers[" + numberIndex + "] duplicates key: " + number.key);
                            else if (number.isKnown)
                            {
                                if (double.IsNaN(number.value) || double.IsInfinity(number.value))
                                    errors.Add("objects[" + objectIndex + "].numbers[" + numberIndex + "] must be finite when known.");
                            }
                            numberIndex++;
                        }
                    }

                    objectIndex++;
                }
            }
            return errors.Count == 0;
        }
    }
}
