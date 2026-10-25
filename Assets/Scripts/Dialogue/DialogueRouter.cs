using System.Collections.Generic;
using UnityEngine;
using AlterOS.Content;
using AlterOS.Dialogue;
using AlterOS.Interaction;
using AlterOS.Progression;
using AlterOS.Stats;

namespace AlterOS
{
    /// <summary>
    /// Single entry point for dialogue playback in AlterOS.
    /// Routes JSON content to either cinematic dialogue or ambient bubble playback.
    /// </summary>
    public class DialogueRouter : MonoBehaviour
    {
        [Header("References")]
        public DialogueManager dialogueManager;
        public AlteraAmbientBubble ambientBubble;
        public ConversationSessionStore conversationSessionStore;
        public AlteraProgressionStore progressionStore;

        [Header("Path Settings")]
        [Tooltip("Base folder inside StreamingAssets where all dialogue JSONs live.")]
        public string dialogueRoot = "Dialogue";

        private readonly Dictionary<string, string> _jsonCache = new Dictionary<string, string>();
        private AuthoredDialogueRouteCatalog _authoredRouteCatalog;
        private RuntimeContentStore _runtimeContentStore;

        private void Awake()
        {
            ContentResult<RuntimeContentStore> content = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (content.IsReady)
                _runtimeContentStore = content.value;
            else
                Debug.LogError("[Router] Authored SQL content is unavailable: " +
                    (content.diagnostics.Count == 0 ? content.status : content.diagnostics[0].code));

            if (conversationSessionStore == null)
            {
                conversationSessionStore = GetComponent<ConversationSessionStore>();
            }

            if (progressionStore == null)
            {
                progressionStore = GetComponent<AlteraProgressionStore>();
            }

            if (conversationSessionStore == null)
            {
                conversationSessionStore = FindObjectOfType<ConversationSessionStore>();
            }

            if (progressionStore == null)
            {
                progressionStore = FindObjectOfType<AlteraProgressionStore>();
            }

            if (conversationSessionStore == null)
            {
                conversationSessionStore = gameObject.AddComponent<ConversationSessionStore>();
            }

            if (progressionStore == null)
            {
                progressionStore = gameObject.AddComponent<AlteraProgressionStore>();
            }
        }

        public void Play(string relativePath)
        {
            if (!TryGetJson(relativePath, out string json))
            {
                return;
            }
            string mode = PeekMode(json);

            if (mode == "cinematic" || ContainsNodes(json))
            {
                RouteAsCinematic(json);
            }
            else if (mode == "ambient" || ContainsLines(json))
            {
                RouteAsAmbient(json);
            }
            else
            {
                Debug.LogWarning($"[Router] Could not determine mode for: {relativePath}. Skipping.");
            }
        }

        public void PlayCinematic(DialogueSequence sequence)
        {
            TryPlayCinematic(sequence, allowGameEffects: false);
        }

        private bool TryPlayCinematic(DialogueSequence sequence, bool allowGameEffects)
        {
            if (sequence == null || sequence.nodes == null || sequence.nodes.Count == 0)
            {
                return false;
            }

            if (dialogueManager == null)
            {
                Debug.LogError("[Router] DialogueManager not assigned!");
                return false;
            }

            if (!AreSequenceConditionsMet(sequence))
            {
                Debug.Log("[Router] Cinematic sequence trigger conditions were not met. Skipping.");
                return false;
            }

            if (allowGameEffects)
            {
                dialogueManager.StartAuthoredSequence(sequence);
            }
            else
            {
                dialogueManager.StartSequence(sequence);
            }

            return dialogueManager.IsActive;
        }

        public bool TryLoadSequence(string relativePath, out DialogueSequence sequence)
        {
            sequence = null;
            if (!TryGetJson(relativePath, out string json))
            {
                return false;
            }

            if (!ContainsNodes(json))
            {
                Debug.LogWarning($"[Router] Requested sequence path has no nodes: {relativePath}");
                return false;
            }

            sequence = DialogueSequence.FromJson(json);
            return sequence != null &&
                   sequence.nodes != null &&
                   sequence.nodes.Count > 0 &&
                   AreSequenceConditionsMet(sequence);
        }

        public bool TryLoadJson(string relativePath, out string json)
        {
            return TryGetJson(relativePath, out json);
        }

        public bool TryPlayAuthoredRoute(string routeId)
        {
            if (dialogueManager == null || string.IsNullOrWhiteSpace(routeId))
            {
                return false;
            }

            EnsureAuthoredRouteCatalog();
            List<AuthoredDialogueVariantCandidate> candidates = new List<AuthoredDialogueVariantCandidate>();
            foreach (string sequencePath in _authoredRouteCatalog.GetSequencePaths(routeId))
            {
                TryGetJson(sequencePath, out string json);
                candidates.Add(new AuthoredDialogueVariantCandidate(sequencePath, json));
            }

            TriggerContext context = progressionStore != null
                ? progressionStore.BuildTriggerContext()
                : TriggerContext.CreateDefault();
            StatSnapshot stats = CompanionStatSnapshotBuilder.Build(
                progressionStore != null ? progressionStore.State?.altera : null);
            AuthoredDialogueSelectionResult selection = AuthoredDialogueVariantSelector.Select(
                routeId,
                candidates,
                context.localHour,
                stats);
            if (selection.SelectedDialogue == null || !TryGetJson(selection.SelectedPath, out string selectedJson))
                return false;

            if (!TryConvertCanonicalSequence(selectedJson, out DialogueSequence selectedSequence))
                return false;

            return string.Equals(selectedSequence.metadata?.topic, routeId, System.StringComparison.OrdinalIgnoreCase) &&
                   TryPlayCinematic(selectedSequence, allowGameEffects: true);
        }

        public void PlayAmbientPool(AmbientLinePool pool)
        {
            if (ambientBubble == null || pool == null || pool.lines == null || pool.lines.Count == 0)
            {
                return;
            }

            AmbientLine line = PickEligibleAmbientLine(pool);
            if (line == null)
            {
                return;
            }

            conversationSessionStore?.RememberAmbientLine(line.id, line.topic, line.text);
            ambientBubble.Say(line.text, line.translatedText, line.language);
        }

        public void PlayAmbientLine(string text, string translatedText = "", string language = "japanese")
        {
            if (ambientBubble == null) return;
            ambientBubble.Say(text, translatedText, language);
        }

        public void StopAmbientLine()
        {
            if (ambientBubble != null) ambientBubble.StopSpat();
        }

        public void PlayFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[Router] PlayFromJson called with empty JSON.");
                return;
            }

            string mode = PeekMode(json);

            if (mode == "cinematic" || ContainsNodes(json))
            {
                Debug.Log("[Router] Live AI response -> CINEMATIC");
                RouteAsCinematic(json);
            }
            else if (mode == "ambient" || ContainsLines(json))
            {
                Debug.Log("[Router] Live AI response -> AMBIENT");
                RouteAsAmbient(json);
            }
            else
            {
                Debug.LogWarning("[Router] Could not determine mode from live AI JSON. Check Altera's output.");
                Debug.LogWarning($"[Router] Received: {json.Substring(0, Mathf.Min(200, json.Length))}...");
            }
        }

        public void Preload(string relativePath)
        {
            TryGetJson(relativePath, out _);
        }

        private void RouteAsCinematic(string json)
        {
            if (dialogueManager == null)
            {
                Debug.LogError("[Router] DialogueManager not assigned!");
                return;
            }

            DialogueSequence sequence = DialogueSequence.FromJson(json);
            if (sequence == null || sequence.nodes == null || sequence.nodes.Count == 0)
            {
                Debug.LogWarning("[Router] Cinematic JSON has no nodes.");
                return;
            }

            if (!AreSequenceConditionsMet(sequence))
            {
                Debug.Log("[Router] Cinematic JSON trigger conditions were not met. Skipping.");
                return;
            }

            Debug.Log($"[Router] Routing as CINEMATIC ({sequence.nodes.Count} nodes).");
            dialogueManager.StartSequence(sequence);
        }

        private void EnsureAuthoredRouteCatalog()
        {
            if (_authoredRouteCatalog != null)
            {
                return;
            }
            _authoredRouteCatalog = AuthoredDialogueRouteCatalog.FromDocuments(
                _runtimeContentStore == null ? null : _runtimeContentStore.ReadDocuments(ContentSourceKinds.GenericDialogue, "Generic/"));
        }

        private bool TryConvertCanonicalSequence(string json, out DialogueSequence sequence)
        {
            sequence = null;
            DialogueDefinition canonical;
            try
            {
                canonical = JsonUtility.FromJson<DialogueDefinition>(json);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[Router] Failed to parse authored dialogue: {exception.Message}");
                return false;
            }

            List<string> errors = DialogueValidator.Validate(canonical);
            if (errors.Count > 0)
            {
                Debug.LogWarning($"[Router] Authored dialogue is invalid: {string.Join(" ", errors)}");
                return false;
            }

            sequence = new DialogueSequence();
            sequence.metadata.mode = "cinematic";
            sequence.metadata.topic = canonical.routing.routeId;
            sequence.metadata.nlp.intentId = canonical.routing.routeId;
            if (canonical.routing.startHour >= 0 && canonical.routing.endHour >= 0)
            {
                sequence.metadata.triggerConditions.Add(new TriggerCondition
                {
                    type = "time_range",
                    minValue = canonical.routing.startHour,
                    maxValue = canonical.routing.endHour
                });
            }

            foreach (CanonicalDialogueNode canonicalNode in canonical.nodes)
            {
                DialogueNode node = new DialogueNode
                {
                    id = canonicalNode.id,
                    speaker = canonicalNode.speaker,
                    text = canonicalNode.text,
                    translatedText = canonicalNode.displayText,
                    language = canonicalNode.language,
                    audioPath = canonicalNode.audioKey,
                    nextId = canonicalNode.nextId,
                    emotion = canonicalNode.presentation != null ? canonicalNode.presentation.expression : string.Empty,
                    presentation = new DialoguePresentationCue
                    {
                        expression = canonicalNode.presentation != null ? canonicalNode.presentation.expression : string.Empty,
                        animation = new DialogueAnimationCue
                        {
                            state = canonicalNode.presentation != null ? canonicalNode.presentation.animation : string.Empty
                        }
                    },
                    choices = new List<DialogueChoice>()
                };

                if (canonicalNode.choices != null)
                {
                    foreach (CanonicalDialogueChoice canonicalChoice in canonicalNode.choices)
                    {
                        if (canonicalChoice == null)
                        {
                            continue;
                        }

                        node.choices.Add(new DialogueChoice
                        {
                            label = canonicalChoice.label,
                            nextId = canonicalChoice.nextId
                        });
                    }
                }

                sequence.nodes.Add(node);
            }

            return sequence.nodes.Count > 0;
        }

        private bool AreSequenceConditionsMet(DialogueSequence sequence)
        {
            TriggerContext context = progressionStore != null
                ? progressionStore.BuildTriggerContext()
                : TriggerContext.CreateDefault();
            return TriggerConditionEvaluator.AreMet(sequence?.metadata?.triggerConditions, context);
        }

        private void RouteAsAmbient(string json)
        {
            if (ambientBubble == null)
            {
                Debug.LogError("[Router] AlteraAmbientBubble not assigned!");
                return;
            }

            AmbientLinePool pool = AmbientLinePool.FromJson(json);
            if (pool == null || pool.lines == null || pool.lines.Count == 0)
            {
                Debug.LogWarning("[Router] Ambient JSON has no lines.");
                return;
            }

            AmbientLine line = PickEligibleAmbientLine(pool);
            if (line == null)
            {
                Debug.LogWarning("[Router] Ambient JSON has no eligible lines for current trigger conditions.");
                return;
            }

            Debug.Log($"[Router] Routing as AMBIENT -> '{line.id}'.");
            conversationSessionStore?.RememberAmbientLine(line.id, line.topic, line.text);
            ambientBubble.Say(line.text, line.translatedText, line.language);
        }

        private AmbientLine PickEligibleAmbientLine(AmbientLinePool pool)
        {
            TriggerContext context = progressionStore != null
                ? progressionStore.BuildTriggerContext()
                : TriggerContext.CreateDefault();
            if (!TriggerConditionEvaluator.AreMet(pool.metadata.triggerConditions, context))
            {
                return null;
            }

            List<AmbientLine> eligible = new List<AmbientLine>();
            foreach (AmbientLine line in pool.lines)
            {
                if (TriggerConditionEvaluator.AreMet(line.triggerConditions, context))
                {
                    eligible.Add(line);
                }
            }

            if (eligible.Count == 0)
            {
                return null;
            }

            return eligible[Random.Range(0, eligible.Count)];
        }

        private string PeekMode(string json)
        {
            if (json.Contains("\"mode\": \"cinematic\"") || json.Contains("\"mode\":\"cinematic\""))
                return "cinematic";
            if (json.Contains("\"mode\": \"ambient\"") || json.Contains("\"mode\":\"ambient\""))
                return "ambient";
            return "";
        }

        private bool ContainsNodes(string json) => json.Contains("\"nodes\"");
        private bool ContainsLines(string json) => json.Contains("\"lines\"");

        private bool TryGetJson(string relativePath, out string json)
        {
            if (_jsonCache.TryGetValue(relativePath, out json) && !string.IsNullOrEmpty(json))
            {
                return true;
            }

            if (_runtimeContentStore == null)
            {
                ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
                if (!opened.IsReady)
                {
                    Debug.LogError("[Router] SQL content release is unavailable; file fallback is disabled: " +
                        (opened.diagnostics.Count == 0 ? opened.status : opened.diagnostics[0].code));
                    json = null;
                    return false;
                }
                _runtimeContentStore = opened.value;
            }

            ContentResult<RuntimeContentDocument> document = _runtimeContentStore.ReadDocument(relativePath);
            if (!document.IsReady || string.IsNullOrEmpty(document.value.json))
            {
                Debug.LogError("[Router] SQL content path is unavailable: " + relativePath + " (" +
                    (document.diagnostics.Count == 0 ? document.status : document.diagnostics[0].code) + ")");
                json = null;
                return false;
            }
            json = document.value.json;
            _jsonCache[relativePath] = json;
            return !string.IsNullOrEmpty(json);
        }
    }
}
