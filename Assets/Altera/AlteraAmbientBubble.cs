using System.Collections;
using System.IO;
using AlterOS.Content;
using System.Threading.Tasks;
using AlterOS.Dialogue;
using AlterOS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS
{
    /// <summary>
    /// Plays a short ambient "spat" line from Altera — a floating WorldSpace
    /// speech bubble + 3D spatial audio — WITHOUT locking player movement.
    ///
    /// Usage:
    ///   ambientBubble.Say("空がきれいですね。", "The sky is so pretty today.");
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AlteraAmbientBubble : MonoBehaviour
    {
        // =============================================
        // INSPECTOR REFERENCES
        // =============================================

        [Header("UI References")]
        [Tooltip("The root WorldSpace Canvas object for the speech bubble.")]
        public Canvas bubbleCanvas;

        [Tooltip("TextMeshPro component displaying the (translated) text.")]
        public TextMeshProUGUI bubbleText;

        [Tooltip("Background image of the bubble (for fade animation).")]
        public Image bubbleBackground;

        [Header("Behaviour Settings")]
        [Tooltip("Time in seconds the bubble stays visible after audio finishes.")]
        public float lingerDuration = 1.5f;

        [Tooltip("Speed of the fade in/out animation.")]
        public float fadeSpeed = 4f;

        [Tooltip("The DialogueManager reference. Ambient lines are silenced during Cinematic mode.")]
        public DialogueManager dialogueManager;

        [Header("TTS Settings")]
        [Tooltip("Default voice language if not specified.")]
        public string defaultVoiceLanguage = "japanese";

        [Header("Filler Pools")]
        [Tooltip("Ambient filler pool used while waiting for dialogue generation.")]
        public string dialogueFillerPoolPath = "Dialogue/filler/filler_dialogue_pool.json";

        [Tooltip("Ambient filler pool used while waiting for TTS generation.")]
        public string ttsFillerPoolPath = "Dialogue/filler/filler_sequence_pool.json";

        // =============================================
        // PRIVATE STATE
        // =============================================

        private AudioSource _audioSource;
        private Coroutine _bubbleRoutine;
        private bool _isTalking = false;
        private string _acknowledgedPresentationId = string.Empty;
        private System.Action<bool> _presentationAcknowledgement;
        private readonly System.Collections.Generic.Dictionary<string, AmbientLinePool> _fillerPoolCache = new System.Collections.Generic.Dictionary<string, AmbientLinePool>();

        // =============================================
        // INIT
        // =============================================

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            // Configure for 3D spatial audio (proximity = quieter farther away)
            _audioSource.spatialBlend = 1.0f;   // Full 3D
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.minDistance = 1.5f;    // Full volume within 1.5m
            _audioSource.maxDistance = 8f;      // Silent beyond 8m
            _audioSource.playOnAwake = false;

            // Start hidden
            SetBubbleAlpha(0f);
            if (bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(false);
        }

        // =============================================
        // PUBLIC API
        // =============================================

        /// <summary>
        /// Plays an ambient one-liner with a floating speech bubble.
        /// The text sent to TTS is 'text' (Japanese). The bubble displays 'translatedText' (English).
        /// </summary>
        public void Say(string text, string translatedText = "", string language = "")
        {
            // Silenced during Cinematic dialogue mode
            if (ShouldSuppressAmbient())
            {
                Debug.Log("[AmbientBubble] Cinematic mode active — skipping ambient spat.");
                return;
            }

            // Don't interrupt herself
            if (_isTalking) return;

            if (_bubbleRoutine != null) StopCoroutine(_bubbleRoutine);
            _bubbleRoutine = StartCoroutine(AmbientRoutine(text, translatedText, language, true));
        }

        /// <summary>Starts a non-modal authored line and acknowledges it only after its bubble is visible.</summary>
        public bool TrySayWithAcknowledgement(string presentationId, string text, string translatedText,
            string language, System.Action<bool> onVisible)
        {
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(presentationId) || string.IsNullOrWhiteSpace(text) ||
                onVisible == null || bubbleCanvas == null || bubbleText == null || ShouldSuppressAmbient() || _isTalking)
                return false;

            _acknowledgedPresentationId = presentationId;
            _presentationAcknowledgement = onVisible;
            _bubbleRoutine = StartCoroutine(AmbientRoutine(text, translatedText, language, true, presentationId));
            return true;
        }

        /// <summary>Cancels only the acknowledged line with this exact presentation identity.</summary>
        public bool CancelAcknowledgedSay(string presentationId)
        {
            if (string.IsNullOrWhiteSpace(presentationId) || _acknowledgedPresentationId != presentationId) return false;
            StopSpat();
            return true;
        }

        public void SayDuringDialogue(string text, string translatedText = "", string language = "")
        {
            if (_isTalking) return;

            if (_bubbleRoutine != null) StopCoroutine(_bubbleRoutine);
            _bubbleRoutine = StartCoroutine(AmbientRoutine(text, translatedText, language, true));
        }

        public void SayDuringDialogueNoBubble(string text, string translatedText = "", string language = "")
        {
            if (_isTalking) return;

            if (_bubbleRoutine != null) StopCoroutine(_bubbleRoutine);
            _bubbleRoutine = StartCoroutine(AmbientRoutine(text, translatedText, language, false));
        }

        /// <summary>
        /// Picks a random filler line from the dialogue-generation pool.
        /// </summary>
        public Task PlayDialogueGenerationFiller()
        {
            return PlayFillerFromPool(dialogueFillerPoolPath);
        }

        /// <summary>
        /// Picks a random filler line from the TTS-generation pool.
        /// </summary>
        public Task PlayTtsGenerationFiller()
        {
            return PlayFillerFromPool(ttsFillerPoolPath, false);
        }

        public async Task<bool> IsTTSOnline()
        {
            await Task.Yield();
            return dialogueManager != null && dialogueManager.IsTtsConfigured;
        }

        /// <summary>
        /// Immediately stops any active ambient spat and hides the bubble.
        /// </summary>
        public void StopSpat()
        {
            System.Action<bool> interruptedAcknowledgement =
                string.IsNullOrEmpty(_acknowledgedPresentationId) ? null : _presentationAcknowledgement;
            // Clear the identity before stopping the coroutine: cancellation may re-enter
            // through the sequence callback, and the interrupted line must settle once.
            _acknowledgedPresentationId = string.Empty;
            _presentationAcknowledgement = null;
            if (_bubbleRoutine != null) StopCoroutine(_bubbleRoutine);
            if (_audioSource != null && _audioSource.isPlaying) _audioSource.Stop();
            
            SetBubbleAlpha(0f);
            if (bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(false);
            _isTalking = false;
            if (interruptedAcknowledgement != null)
            {
                try { interruptedAcknowledgement(false); }
                catch (System.Exception exception) { Debug.LogException(exception, this); }
            }
        }

        private void OnDisable() { StopSpat(); }

        /// <summary>Returns true if Altera is currently doing an ambient spat.</summary>
        public bool IsSpatting => _isTalking;

        public void PreloadFillerPool(string relativePath)
        {
            LoadFillerPool(relativePath);
        }

        private Task PlayFillerFromPool(string relativePath, bool showBubble = true)
        {
            if (ShouldSuppressAmbient())
            {
                return Task.CompletedTask;
            }

            AmbientLinePool pool = LoadFillerPool(relativePath);
            if (pool == null || pool.lines == null || pool.lines.Count == 0)
            {
                return Task.CompletedTask;
            }

            TriggerContext context = TriggerContext.CreateDefault();
            System.Collections.Generic.List<AmbientLine> eligible = new System.Collections.Generic.List<AmbientLine>();
            foreach (AmbientLine line in pool.lines)
            {
                if (TriggerConditionEvaluator.AreMet(pool.metadata.triggerConditions, context) &&
                    TriggerConditionEvaluator.AreMet(line.triggerConditions, context))
                {
                    eligible.Add(line);
                }
            }

            if (eligible.Count == 0)
            {
                return Task.CompletedTask;
            }

            AmbientLine selected = eligible[Random.Range(0, eligible.Count)];
            if (showBubble)
            {
                SayDuringDialogue(selected.text, selected.translatedText, selected.language);
            }
            else
            {
                SayDuringDialogueNoBubble(selected.text, selected.translatedText, selected.language);
            }
            return Task.CompletedTask;
        }

        private bool ShouldSuppressAmbient()
        {
            return dialogueManager != null && dialogueManager.BlocksPlayerControl;
        }

        private AmbientLinePool LoadFillerPool(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            if (_fillerPoolCache.TryGetValue(relativePath, out AmbientLinePool cachedPool) && cachedPool != null)
            {
                return cachedPool;
            }

            ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!opened.IsReady)
            {
                return null;
            }
            ContentResult<RuntimeContentDocument> document = opened.value.ReadDocument(relativePath);
            if (!document.IsReady) return null;
            AmbientLinePool pool = AmbientLinePool.FromJson(document.value.json);
            if (pool != null)
            {
                _fillerPoolCache[relativePath] = pool;
            }

            return pool;
        }

        // =============================================
        // CORE ROUTINE
        // =============================================

        private IEnumerator AmbientRoutine(string text, string translatedText, string language, bool showBubble,
            string acknowledgedPresentationId = "")
        {
            _isTalking = true;

            string displayText = !string.IsNullOrEmpty(translatedText) ? translatedText : text;
            if (showBubble)
            {
                if (bubbleText != null) bubbleText.text = displayText;
                if (bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(true);
                yield return StartCoroutine(FadeBubble(0f, 1f));
                AcknowledgePresentationVisibility(acknowledgedPresentationId);
            }
            else
            {
                SetBubbleAlpha(0f);
                if (bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(false);
            }

            // Fetch TTS (with disk cache)
            AudioClip clip = null;
            var fetchTask = FetchTTS(text, language);
            yield return new WaitUntil(() => fetchTask.IsCompleted);
            clip = fetchTask.Result;

            // Play audio
            if (clip != null)
            {
                _audioSource.clip = clip;
                _audioSource.Play();

                // Wait for audio to finish
                yield return new WaitWhile(() => _audioSource.isPlaying);
            }
            else
            {
                // No audio — show bubble for a few seconds based on text length
                float readTime = Mathf.Max(2f, displayText.Length * 0.05f);
                yield return new WaitForSeconds(readTime);
            }

            if (showBubble)
            {
                yield return new WaitForSeconds(lingerDuration);
                yield return StartCoroutine(FadeBubble(1f, 0f));
                if (bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(false);
            }

            _isTalking = false;
            if (!string.IsNullOrEmpty(acknowledgedPresentationId) && _acknowledgedPresentationId == acknowledgedPresentationId)
                _acknowledgedPresentationId = string.Empty;
        }

        private void AcknowledgePresentationVisibility(string presentationId)
        {
            if (string.IsNullOrEmpty(presentationId) || _acknowledgedPresentationId != presentationId) return;
            System.Action<bool> acknowledgement = _presentationAcknowledgement;
            _presentationAcknowledgement = null;
            bool visible = bubbleCanvas != null && bubbleCanvas.isActiveAndEnabled &&
                bubbleCanvas.gameObject.activeInHierarchy && bubbleText != null && bubbleText.gameObject.activeInHierarchy;
            if (acknowledgement != null) acknowledgement(visible);
        }

        // =============================================
        // FADE ANIMATION
        // =============================================

        private IEnumerator FadeBubble(float from, float to)
        {
            float elapsed = 0f;
            float duration = Mathf.Abs(to - from) / fadeSpeed;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(from, to, elapsed / duration);
                SetBubbleAlpha(alpha);
                yield return null;
            }
            SetBubbleAlpha(to);
        }

        private void SetBubbleAlpha(float alpha)
        {
            if (bubbleBackground != null)
            {
                var c = bubbleBackground.color;
                bubbleBackground.color = new Color(c.r, c.g, c.b, alpha);
            }
            if (bubbleText != null)
            {
                var c = bubbleText.color;
                bubbleText.color = new Color(c.r, c.g, c.b, alpha);
            }
        }

        // =============================================
        // TTS
        // =============================================

        private async Task<AudioClip> FetchTTS(string text, string language)
        {
            string lang = string.IsNullOrEmpty(language) ? defaultVoiceLanguage : language;

            // DialogueManager owns provider access and shared disk caching.
            if (dialogueManager == null)
            {
                return null;
            }

            Debug.Log("[AmbientBubble] Fetching TTS for ambient spat...");
            return await dialogueManager.SynthesizeTtsAsync(text, lang);
        }

    }
}
