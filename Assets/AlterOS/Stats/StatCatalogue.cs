using System;
using System.Collections.Generic;

namespace AlterOS.Stats
{
    public sealed class StatDefinition
    {
        private readonly string id;
        private readonly float minimum;
        private readonly float maximum;

        internal StatDefinition(string id, float minimum, float maximum)
        {
            this.id = id;
            this.minimum = minimum;
            this.maximum = maximum;
        }

        public string Id { get { return id; } }
        public float Minimum { get { return minimum; } }
        public float Maximum { get { return maximum; } }
    }

    public static class StatCatalogue
    {
        private static readonly IReadOnlyList<StatDefinition> definitions = Array.AsReadOnly(new[]
        {
            new StatDefinition("alterra.energy", 0f, 100f),
            new StatDefinition("alterra.hydration", 0f, 100f),
            new StatDefinition("alterra.satiety", 0f, 100f),
            new StatDefinition("alterra.engagement", 0f, 100f),
            new StatDefinition("alterra.connection", 0f, 100f),
            new StatDefinition("alterra.physical_comfort", 0f, 100f),
            new StatDefinition("alterra.emotion.valence", -1f, 1f),
            new StatDefinition("alterra.emotion.activation", -1f, 1f),
            new StatDefinition("alterra.emotion.control", -1f, 1f)
        });

        public static IReadOnlyList<StatDefinition> Definitions { get { return definitions; } }

        public static bool TryGet(string id, out StatDefinition definition)
        {
            foreach (StatDefinition candidate in definitions)
            {
                if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }
    }
}
