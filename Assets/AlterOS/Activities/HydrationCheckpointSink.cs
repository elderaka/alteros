using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Activities
{
    /// <summary>Game-authorized, per-execution checkpoint allowlist with duplicate suppression.</summary>
    public sealed class HydrationCheckpointSink : IHydrationCheckpointSink
    {
        private sealed class Grant
        {
            public readonly HashSet<string> allowed;
            public readonly float amount;
            public Grant(IEnumerable<string> checkpoints, float value)
            { allowed = new HashSet<string>(checkpoints, StringComparer.Ordinal); amount = value; }
        }

        private readonly Dictionary<string, Grant> grants = new Dictionary<string, Grant>(StringComparer.Ordinal);
        private readonly HashSet<string> applied = new HashSet<string>(StringComparer.Ordinal);
        private readonly Func<string, float, bool> applyHydration;

        public HydrationCheckpointSink(Func<float, bool> gameOwnedHydrationWriter)
        {
            if (gameOwnedHydrationWriter == null) throw new ArgumentNullException("gameOwnedHydrationWriter");
            applyHydration = (operationKey, amount) => gameOwnedHydrationWriter(amount);
        }

        public HydrationCheckpointSink(Func<string, float, bool> gameOwnedHydrationWriter)
        { applyHydration = gameOwnedHydrationWriter ?? throw new ArgumentNullException("gameOwnedHydrationWriter"); }

        public bool Register(string activityId, IEnumerable<string> allowedCheckpointIds, float amountPerCheckpoint)
        {
            if (string.IsNullOrWhiteSpace(activityId) || allowedCheckpointIds == null ||
                amountPerCheckpoint <= 0f || amountPerCheckpoint > 100f || float.IsNaN(amountPerCheckpoint) ||
                float.IsInfinity(amountPerCheckpoint) || grants.ContainsKey(activityId) ||
                applied.Any(key => key.StartsWith(activityId + "\u001f", StringComparison.Ordinal))) return false;

            string[] checkpoints = allowedCheckpointIds.ToArray();
            if (checkpoints.Length == 0 || checkpoints.Any(string.IsNullOrWhiteSpace) ||
                checkpoints.Distinct(StringComparer.Ordinal).Count() != checkpoints.Length) return false;
            grants.Add(activityId, new Grant(checkpoints, amountPerCheckpoint));
            return true;
        }

        public bool TryApply(string activityId, string checkpointId)
        {
            if (string.IsNullOrWhiteSpace(activityId) || string.IsNullOrWhiteSpace(checkpointId)) return false;
            string key = activityId + "\u001f" + checkpointId;
            if (applied.Contains(key)) return true;
            Grant grant;
            if (!grants.TryGetValue(activityId, out grant) || !grant.allowed.Contains(checkpointId)) return false;
            if (!applyHydration(key, grant.amount)) return false;
            applied.Add(key);
            return true;
        }

        public void Release(string activityId)
        {
            if (!string.IsNullOrWhiteSpace(activityId)) grants.Remove(activityId);
        }
    }
}
