using System.Collections;
using AlterOS.Semantics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace PleiadesOS.UI
{
    /// <summary>
    /// Splash screen controller.
    /// Temporary loading surface. See Documentation/Tracking/Startup and Loading.md.
    /// Starts ModelTest only after the semantic runtime is ready.
    /// 
    /// Setup:
    ///   - Attach to a root GameObject in SplashScene
    ///   - Assign logo (Image) and statusText (TMP_Text) in Inspector
    ///   - Logo should start with alpha=0 for fade-in effect
    /// </summary>
    public class SplashController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image    logo;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button retryButton;

        [Header("Timing")]
        [SerializeField] private float fadeInDuration  = 0.8f;
        [SerializeField] private float holdDuration    = 1.2f;
        [SerializeField] private float fadeOutDuration = 0.5f;

        private void Start()
        {
            StartCoroutine(RunSplashSequence());
        }

        private void OnEnable()
        {
            if (retryButton != null)
            {
                retryButton.gameObject.SetActive(false);
                retryButton.onClick.AddListener(RetryLoading);
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (retryButton != null)
                retryButton.onClick.RemoveListener(RetryLoading);
        }

        private void RetryLoading()
        {
            GteSemanticRuntime.Current?.TryRetryInitialization();
        }

        private IEnumerator RunSplashSequence()
        {
            // Start fully transparent
            if (logo != null)
                SetAlpha(logo, 0f);
            if (statusText != null)
                statusText.text = "Preparing AlterOS...";

            // Phase 1: Fade in logo
            yield return FadeImage(logo, 0f, 1f, fadeInDuration);

            // Phase 2: Prepare the next surface while displaying
            yield return new WaitForSecondsRealtime(holdDuration);
            var runtime = GteSemanticRuntime.Current;
            if (runtime == null)
            {
                if (statusText != null)
                    statusText.text = "Semantic runtime unavailable. Please restart AlterOS.";
                yield break;
            }
            yield return WaitForSemanticRuntime(runtime);
            if (runtime == null || runtime.State != GteSemanticState.Ready)
                yield break;

            // Phase 3: Fade out and enter the current post-splash surface.
            if (statusText != null)
                statusText.text = "Ready!";
            yield return FadeImage(logo, 1f, 0f, fadeOutDuration);

            yield return SceneManager.LoadSceneAsync("ModelTest");
        }

        private IEnumerator WaitForSemanticRuntime(GteSemanticRuntime runtime)
        {
            while (runtime != null && runtime.State != GteSemanticState.Ready)
            {
                bool failed = runtime.State == GteSemanticState.Failed;
                if (retryButton != null)
                    retryButton.gameObject.SetActive(failed);
                if (statusText != null)
                    statusText.text = failed
                        ? "Couldn't prepare dialogue. Retry loading, or check the Console for details."
                        : LoadingMessage(runtime.StartupStage);
                yield return null;
            }
            if (retryButton != null)
                retryButton.gameObject.SetActive(false);
        }

        private static string LoadingMessage(string stage)
        {
            switch (stage)
            {
                case "resource requests":
                case "model loading":
                case "worker creation":
                    return "Loading language model...";
                case "tokenizer parsing":
                case "route index":
                    return "Preparing dialogue...";
                case "warm-up scheduling":
                case "GPU/CPU readback":
                    return "Warming up language model...";
                default:
                    return "Preparing AlterOS...";
            }
        }

        private IEnumerator FadeImage(Image image, float from, float to, float duration)
        {
            if (image == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Smooth ease-in-out
                t = t * t * (3f - 2f * t);
                SetAlpha(image, Mathf.Lerp(from, to, t));
                yield return null;
            }
            SetAlpha(image, to);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            var c = image.color;
            c.a = alpha;
            image.color = c;
        }
    }
}
