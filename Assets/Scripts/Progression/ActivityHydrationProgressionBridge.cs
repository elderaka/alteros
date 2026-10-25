using AlterOS.Activities;
using AlterOS.Persistence;
using UnityEngine;

namespace AlterOS.Progression
{
    /// <summary>Trusted Assembly-CSharp bridge from activity grants to the legacy persisted stat owner.</summary>
    public sealed class ActivityHydrationProgressionBridge : MonoBehaviour, IHydrationCheckpointSink
    {
        [SerializeField] private AlteraProgressionStore progressionStore;
        private HydrationCheckpointSink sink;

        private void Awake() { EnsureSink(); }

        public bool Register(string activityId, System.Collections.Generic.IEnumerable<string> allowedCheckpointIds,
            float amountPerCheckpoint)
        { return EnsureSink() && sink.Register(activityId, allowedCheckpointIds, amountPerCheckpoint); }

        public bool TryApply(string activityId, string checkpointId)
        { return EnsureSink() && sink.TryApply(activityId, checkpointId); }

        public void Release(string activityId)
        { if (EnsureSink()) sink.Release(activityId); }

        private bool EnsureSink()
        {
            if (sink != null) return true;
            if (progressionStore == null) progressionStore = GetComponent<AlteraProgressionStore>();
            if (progressionStore == null) progressionStore = GetComponentInParent<AlteraProgressionStore>();
            if (progressionStore == null) return false;
            sink = new HydrationCheckpointSink((operationKey, amount) =>
                progressionStore.TryApplyActivityHydrationCheckpoint(GameOperationId.FromStableKey(operationKey), amount));
            return true;
        }
    }
}
