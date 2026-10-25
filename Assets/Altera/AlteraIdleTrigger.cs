using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AlterOS.Dialogue;

namespace AlterOS
{
    /// <summary>
    /// Randomly triggers ambient "spat" lines on a timer.
    /// Picks a random line from an assigned pool or the pinned SQL content release.
    /// </summary>
    public class AlteraIdleTrigger : MonoBehaviour
    {
        // =============================================
        // INSPECTOR
        // =============================================

        [Header("References")]
        public DialogueRouter dialogueRouter;

        [Header("Timing")]
        [Tooltip("Minimum seconds between ambient spats.")]
        public float minInterval = 60f;

        [Tooltip("Maximum seconds between ambient spats.")]
        public float maxInterval = 180f;

        [Tooltip("If true, plays a random line shortly after the game starts.")]
        public bool playOnStart = true;

        [Tooltip("Delay in seconds before the first ambient line fires.")]
        public float startDelay = 10f;

        [Header("Idle Line Pool")]
        [Tooltip("Optional logical path in the shipped SQL content release (e.g. 'idle/idle_pool.json')")]
        public string poolJsonPath = "idle/idle_pool.json";

        [Tooltip("Manual override pool. If empty, the script loads the pinned SQL pool and keeps repetition in private session state.")]
        public List<AmbientLine> idleLines = new List<AmbientLine>();

        // =============================================
        // PRIVATE STATE
        // =============================================

        private Coroutine _timerRoutine;
        private AmbientLinePool _loadedPool;

        // =============================================
        // LIFECYCLE
        // =============================================

        private void Start()
        {
            if (dialogueRouter == null)
            {
                Debug.LogWarning("[IdleTrigger] No AlteraDialogueRouter assigned!");
                return;
            }

            LoadPool();
            _timerRoutine = StartCoroutine(IdleTimerRoutine());
        }

        private void LoadPool()
        {
            if (idleLines != null && idleLines.Count > 0)
            {
                // Use manual list
                _loadedPool = new AmbientLinePool { lines = idleLines };
                return;
            }

            if (!string.IsNullOrEmpty(poolJsonPath))
            {
                if (dialogueRouter.TryLoadJson(poolJsonPath, out string json))
                {
                    _loadedPool = AmbientLinePool.FromJson(json);
                    RestoreRuntimeRepetition();
                    Debug.Log($"[IdleTrigger] Loaded SQL pool from: {poolJsonPath} ({_loadedPool.lines.Count} lines)");
                }
                else
                {
                    Debug.LogError($"[IdleTrigger] Pool is not in the pinned SQL content release: {poolJsonPath}");
                }
            }
        }

        private void RestoreRuntimeRepetition()
        {
            var sessions = dialogueRouter == null ? null : dialogueRouter.conversationSessionStore;
            if (sessions == null || sessions.Database?.repetitions == null || _loadedPool?.lines == null) return;
            foreach (AmbientLine line in _loadedPool.lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.id)) continue;
                string key = RepetitionKey(line.id);
                line.playCount = sessions.GetRepetitionCount(key);
                var record = sessions.Database.repetitions.FirstOrDefault(item => item != null && item.key == key);
                if (record != null && System.DateTimeOffset.TryParse(record.lastTriggeredAt, out System.DateTimeOffset last))
                    line.lastPlayTime = last.ToUnixTimeSeconds();
            }
        }

        // =============================================
        // CORE TIMER
        // =============================================

        private IEnumerator IdleTimerRoutine()
        {
            // Initial delay
            float sDelay = playOnStart ? startDelay : Random.Range(minInterval, maxInterval);
            while (sDelay > 0)
            {
                if (dialogueRouter != null && dialogueRouter.dialogueManager != null && !dialogueRouter.dialogueManager.BlocksPlayerControl)
                    sDelay -= Time.deltaTime;
                yield return null;
            }

            while (true)
            {
                TriggerRandomLine();

                float wait = Random.Range(minInterval, maxInterval);
                while (wait > 0)
                {
                    // Only count down if dialogue is NOT active
                    if (dialogueRouter != null && dialogueRouter.dialogueManager != null && !dialogueRouter.dialogueManager.BlocksPlayerControl)
                        wait -= Time.deltaTime;
                    
                    yield return null;
                }
            }
        }

        // =============================================
        // PUBLIC API
        // =============================================

        /// <summary>Manually trigger a random line from the pool with weighted priority and cooldowns.</summary>
        public void TriggerRandomLine()
        {
            if (dialogueRouter != null && dialogueRouter.dialogueManager != null && dialogueRouter.dialogueManager.BlocksPlayerControl)
            {
                return;
            }

            // The authored pool is immutable; this reloads its SQL projection and private repetition state.
            if (idleLines == null || idleLines.Count == 0) LoadPool();

            if (_loadedPool == null || _loadedPool.lines == null || _loadedPool.lines.Count == 0) return;

            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            TriggerContext triggerContext = TriggerContext.CreateDefault();
            List<AmbientLine> candidates = new List<AmbientLine>();
            List<float> weights = new List<float>();
            float totalWeight = 0f;

            // Find the "Latest Fresh Topic"
            string latestFreshTopic = "";
            for (int i = _loadedPool.lines.Count - 1; i >= 0; i--)
            {
                var line = _loadedPool.lines[i];
                if (line.isGenerated && line.playCount == 0 && !string.IsNullOrEmpty(line.topic))
                {
                    latestFreshTopic = line.topic;
                    break;
                }
            }

            foreach (var line in _loadedPool.lines)
            {
                if (!TriggerConditionEvaluator.AreMet(_loadedPool.metadata.triggerConditions, triggerContext))
                {
                    continue;
                }

                if (!TriggerConditionEvaluator.AreMet(line.triggerConditions, triggerContext))
                {
                    continue;
                }

                // Calculate cooldown
                // Generated lines have 7x cooldown (minInterval * 7) if they've been played once.
                float cooldownSeconds = line.isGenerated ? minInterval * 7 : minInterval;
                
                if (line.playCount > 0 && (now - line.lastPlayTime) < cooldownSeconds)
                {
                    continue; // Still on cooldown
                }

                // Calculate Weight
                float weight = 1.0f;
                if (line.isGenerated && line.playCount == 0)
                {
                    // Prioritize the latest fresh topic specifically (60% boost)
                    if (line.topic == latestFreshTopic)
                    {
                        weight = 1.6f;
                    }
                    else
                    {
                        // Other fresh generated lines (from older topics) still get a slight boost or stay normal?
                        // User said: "If topic A exhausted, then topic B is prioritized."
                        // So Topic B only becomes 1.6x once Topic A is gone.
                        weight = 1.0f; 
                    }
                }
                
                candidates.Add(line);
                weights.Add(weight);
                totalWeight += weight;
            }

            if (candidates.Count == 0)
            {
                Debug.Log("[IdleTrigger] All lines are on cooldown. Skipping this interval.");
                return;
            }

            // Weighted Random Selection
            float r = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            AmbientLine selected = candidates[0];

            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += weights[i];
                if (r <= cumulative)
                {
                    selected = candidates[i];
                    break;
                }
            }

            // Update Metadata
            var sessions = dialogueRouter == null ? null : dialogueRouter.conversationSessionStore;
            if (sessions == null)
            {
                selected.playCount++;
                selected.lastPlayTime = now;
            }
            else
            {
                string key = RepetitionKey(selected.id);
                selected.playCount = sessions.RegisterRepetition(key, float.MaxValue);
                var record = sessions.Database.repetitions.FirstOrDefault(item => item != null && item.key == key);
                selected.lastPlayTime = record != null && System.DateTimeOffset.TryParse(record.lastTriggeredAt, out System.DateTimeOffset last)
                    ? last.ToUnixTimeSeconds() : now;
            }

            Debug.Log($"[IdleTrigger] Selected: {selected.id} (topic: {selected.topic}, plays: {selected.playCount})");
            dialogueRouter.PlayAmbientLine(selected.text, selected.translatedText, selected.language);

        }

        private static string RepetitionKey(string lineId) { return "ambient_line:" + lineId; }

        /// <summary>
        /// Immediately stops any active ambient spat and hides the bubble.
        /// </summary>
        public void StopSpat()
        {
            dialogueRouter.StopAmbientLine();
        }

        /// <summary>Manually trigger a specific line by index.</summary>
        public void TriggerLine(int index)
        {
            if (_loadedPool == null || index < 0 || index >= _loadedPool.lines.Count) return;
            var line = _loadedPool.lines[index];
            dialogueRouter.PlayAmbientLine(line.text, line.translatedText, line.language);
        }

        /// <summary>Trigger a specific line by searching its text.</summary>
        public void TriggerLine(string textKey)
        {
            if (_loadedPool == null) return;
            var line = _loadedPool.lines.Find(l => l.text == textKey || l.translatedText == textKey || l.id == textKey);
            if (line != null) dialogueRouter.PlayAmbientLine(line.text, line.translatedText, line.language);
        }
    }
}
