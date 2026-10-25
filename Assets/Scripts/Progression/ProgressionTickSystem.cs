using System.Collections;
using System.Collections.Generic;
using AlterOS.Dialogue;
using UnityEngine;

namespace AlterOS.Progression
{
    public class ProgressionTickSystem : MonoBehaviour
    {
        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private bool runWhileEnabled = true;
        [SerializeField] private float tickSeconds = 60f;
        [SerializeField] private float energyRecoveryPerMinute = 0.35f;
        [SerializeField] private float engagementDecayPerMinute = 0.2f;
        [SerializeField] private float attentionDecayPerMinute = 0.15f;
        [SerializeField] private float stimulationDecayPerMinute = 0.1f;
        [SerializeField] private float cleanlinessDecayPerMinute = 0.02f;
        [SerializeField] private float focusEnergyRecoveryPerMinute = 0.2f;

        private Coroutine tickRoutine;
        private System.DateTime? lastTickUtc;

        private void OnEnable()
        {
            if (progressionStore == null)
            {
                progressionStore = FindObjectOfType<AlteraProgressionStore>();
            }

            if (runWhileEnabled)
            {
                tickRoutine = StartCoroutine(TickRoutine());
            }
        }

        private void OnDisable()
        {
            if (tickRoutine != null)
            {
                StopCoroutine(tickRoutine);
                tickRoutine = null;
            }
            lastTickUtc = null;
        }

        public void TickNow()
        {
            float intervalSeconds = Mathf.Max(1f, tickSeconds);
            ApplyTick(intervalSeconds / 60f, intervalSeconds);
        }

        private IEnumerator TickRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(Mathf.Max(1f, tickSeconds));
            while (enabled)
            {
                yield return wait;
                float intervalSeconds = Mathf.Max(1f, tickSeconds);
                ApplyTick(intervalSeconds / 60f, intervalSeconds);
            }
        }

        private void ApplyTick(float minutes, float intervalSeconds)
        {
            if (progressionStore == null || !progressionStore.IsInitialized || progressionStore.State == null)
            {
                return;
            }

            List<StatDelta> deltas = new List<StatDelta>();

            if (progressionStore.GetStat("altera", "energy") < 75f)
            {
                deltas.Add(new StatDelta { target = "altera", stat = "energy", amount = energyRecoveryPerMinute * minutes, op = "add" });
            }

            deltas.Add(new StatDelta { target = "altera", stat = "engagement", amount = -engagementDecayPerMinute * minutes, op = "add" });
            deltas.Add(new StatDelta { target = "altera", stat = "attention", amount = -attentionDecayPerMinute * minutes, op = "add" });
            deltas.Add(new StatDelta { target = "altera", stat = "stimulation", amount = -stimulationDecayPerMinute * minutes, op = "add" });
            deltas.Add(new StatDelta { target = "environment", stat = "cleanliness", amount = -cleanlinessDecayPerMinute * minutes, op = "add" });

            if (!string.Equals(progressionStore.State.player.currentMode, "focus", System.StringComparison.OrdinalIgnoreCase))
            {
                deltas.Add(new StatDelta { target = "player", stat = "focus_energy", amount = focusEnergyRecoveryPerMinute * minutes, op = "add" });
            }

            System.DateTime endUtc = System.DateTime.UtcNow;
            System.DateTime startUtc = lastTickUtc ?? endUtc.AddSeconds(-intervalSeconds);
            lastTickUtc = endUtc;
            string operationId = AlterOS.Persistence.GameOperationId.FromStableKey(
                "progression-tick:" + startUtc.ToString("O") + ":" + endUtc.ToString("O"));
            AlterOS.Persistence.GameCommitResult result = progressionStore.TryApplySimulationCheckpoint(
                operationId, deltas, "progression-needs-decay", "1", startUtc, endUtc);
            if (!result.IsDurable)
                Debug.LogWarning($"[ProgressionTickSystem] Simulation checkpoint was not committed ({result.state}).");
        }
    }
}
