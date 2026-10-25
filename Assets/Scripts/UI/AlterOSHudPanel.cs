using System.Collections;
using UnityEngine;

namespace AlterOS.UI
{
    [DisallowMultipleComponent]
    public class AlterOSHudPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panelRoot;
        [SerializeField] private bool hideGameObject = true;
        [SerializeField] private float fadeDuration = 0.18f;
        [SerializeField] private Vector2 hiddenOffset = Vector2.zero;
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Coroutine fadeRoutine;
        private Vector2 shownPosition;
        private bool cachedPosition;

        private void Awake()
        {
            CacheComponents();
            CachePosition();
        }

        public void SetVisible(bool visible, bool immediate = false)
        {
            CacheComponents();
            CachePosition();

            if (visible)
            {
                gameObject.SetActive(true);
            }

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }

            if (immediate || !isActiveAndEnabled)
            {
                ApplyVisibility(visible ? 1f : 0f);
                if (!visible && hideGameObject)
                {
                    gameObject.SetActive(false);
                }

                return;
            }

            fadeRoutine = StartCoroutine(FadeRoutine(visible));
        }

        private IEnumerator FadeRoutine(bool visible)
        {
            float start = canvasGroup != null ? canvasGroup.alpha : (visible ? 0f : 1f);
            float end = visible ? 1f : 0f;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, fadeDuration);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = fadeCurve != null ? fadeCurve.Evaluate(Mathf.Clamp01(elapsed / duration)) : Mathf.Clamp01(elapsed / duration);
                ApplyVisibility(Mathf.Lerp(start, end, t));
                yield return null;
            }

            ApplyVisibility(end);
            fadeRoutine = null;

            if (!visible && hideGameObject)
            {
                gameObject.SetActive(false);
            }
        }

        private void ApplyVisibility(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
                canvasGroup.interactable = alpha > 0.99f;
                canvasGroup.blocksRaycasts = alpha > 0.99f;
            }

            if (panelRoot != null)
            {
                panelRoot.anchoredPosition = Vector2.Lerp(shownPosition + hiddenOffset, shownPosition, alpha);
            }
        }

        private void CacheComponents()
        {
            if (panelRoot == null) panelRoot = transform as RectTransform;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        private void CachePosition()
        {
            if (cachedPosition || panelRoot == null)
            {
                return;
            }

            shownPosition = panelRoot.anchoredPosition;
            cachedPosition = true;
        }
    }
}
