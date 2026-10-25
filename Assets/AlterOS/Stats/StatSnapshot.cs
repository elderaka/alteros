using System;
using System.Collections.Generic;

namespace AlterOS.Stats
{
    /// <summary>A detached set of known values; missing values are never default-filled.</summary>
    public sealed class StatSnapshot
    {
        private readonly Dictionary<string, float> values = new Dictionary<string, float>(StringComparer.Ordinal);

        public bool TrySet(string id, float value, out string error)
        {
            StatDefinition definition;
            if (!StatCatalogue.TryGet(id, out definition))
            {
                error = "Unknown stat ID: " + (id ?? "<null>");
                return false;
            }

            if (float.IsNaN(value) || float.IsInfinity(value) ||
                value < definition.Minimum || value > definition.Maximum)
            {
                error = "Value must be finite and within the catalogue bounds for " + id;
                return false;
            }

            values[id] = value;
            error = null;
            return true;
        }

        public bool TryGet(string id, out float value)
        {
            value = 0f;
            return id != null && values.TryGetValue(id, out value);
        }

        public StatSnapshot Clone()
        {
            var clone = new StatSnapshot();
            foreach (KeyValuePair<string, float> pair in values)
                clone.values.Add(pair.Key, pair.Value);
            return clone;
        }
    }
}
