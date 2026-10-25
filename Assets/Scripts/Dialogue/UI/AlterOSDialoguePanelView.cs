using System.Collections;
using TMPro;
using UnityEngine;

namespace AlterOS.Dialogue.UI
{
    [DisallowMultipleComponent]
    public class AlterOSDialoguePanelView : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panelRoot;
        [SerializeField] private AlterOSGlassPanel bodyGlass;
        [SerializeField] private AlterOSGlassPanel nameplateGlass;

        [Header("Text")]
        [SerializeField] private TextMeshProUGUI speakerNameText;
        [SerializeField] private TextMeshProUGUI dialogueText;
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField] private Color speakerColor = new Color(0.96f, 0.96f, 1f, 1f);
        [SerializeField] private Color dialogueColor = new Color(0.94f, 0.95f, 1f, 1f);
        [SerializeField] private float speakerSize = 30f;
        [SerializeField] private float dialogueSize = 34f;

        [Header("Motion")]
        [SerializeField] private float showDuration = 0.26f;
        [SerializeField] private float hideDuration = 0.18f;
        [SerializeField] private Vector2 hiddenOffset = new Vector2(0f, -28f);
        [SerializeField] private AnimationCurve motionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Decoration")]
        [SerializeField] private RectTransform[] sparkleAccents;
        [SerializeField] private float sparklePulseSpeed = 1.8f;
        [SerializeField] private float sparklePulseScale = 0.08f;

        private Coroutine visibilityRoutine;
        private Vector2 shownAnchoredPosition;
        private bool cachedPosition;

        private void Awake()
        {
            CacheComponents();
            ApplySkin();
        }

        private void OnValidate()
        {
            CacheComponents();
            ApplySkin();
        }

        private void Update()
        {
            AnimateSparkles();
        }

        public void Show()
        {
            CacheComponents();
            gameObject.SetActive(true);
            StartVisibilityRoutine(true, showDuration);
        }

        public void Hide()
        {
            CacheComponents();
            StartVisibilityRoutine(false, hideDuration);
        }

        public void ShowImmediate()
        {
            CacheComponents();
            gameObject.SetActive(true);
            SetVisibility(1f);
        }

        public void HideImmediate()
        {
            CacheComponents();
            SetVisibility(0f);
            gameObject.SetActive(false);
        }

        public void ApplySkin()
        {
            if (speakerNameText != null)
            {
                speakerNameText.color = speakerColor;
                speakerNameText.fontSize = speakerSize;
                speakerNameText.enableWordWrapping = false;
                if (fontAsset != null) speakerNameText.font = fontAsset;
            }

            if (dialogueText != null)
            {
                dialogueText.color = dialogueColor;
                dialogueText.fontSize = dialogueSize;
                dialogueText.enableWordWrapping = true;
                dialogueText.alignment = TextAlignmentOptions.TopLeft;
                if (fontAsset != null) dialogueText.font = fontAsset;
            }

            if (bodyGlass != null)
            {
                bodyGlass.SetEmphasized(false);
            }

            if (nameplateGlass != null)
            {
                nameplateGlass.SetEmphasized(true);
            }
        }

        private void StartVisibilityRoutine(bool show, float duration)
        {
            if (visibilityRoutine != null)
            {
                StopCoroutine(visibilityRoutine);
            }

            if (!isActiveAndEnabled)
            {
                visibilityRoutine = null;
                SetVisibility(show ? 1f : 0f);
                if (!show)
                    gameObject.SetActive(false);
                return;
            }

            visibilityRoutine = StartCoroutine(VisibilityRoutine(show, Mathf.Max(0.01f, duration)));
        }

        private IEnumerator VisibilityRoutine(bool show, float duration)
        {
            float start = canvasGroup != null ? canvasGroup.alpha : (show ? 0f : 1f);
            float end = show ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = motionCurve != null ? motionCurve.Evaluate(Mathf.Clamp01(elapsed / duration)) : Mathf.Clamp01(elapsed / duration);
                SetVisibility(Mathf.Lerp(start, end, t));
                yield return null;
            }

            SetVisibility(end);
            visibilityRoutine = null;

            if (!show)
            {
                gameObject.SetActive(false);
            }
        }

        private void SetVisibility(float alpha)
        {
            CacheShownPosition();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
                canvasGroup.interactable = alpha > 0.99f;
                canvasGroup.blocksRaycasts = alpha > 0.99f;
            }

            if (panelRoot != null)
            {
                panelRoot.anchoredPosition = Vector2.Lerp(shownAnchoredPosition + hiddenOffset, shownAnchoredPosition, alpha);
            }
        }

        private void AnimateSparkles()
        {
            if (sparkleAccents == null || sparkleAccents.Length == 0)
            {
                return;
            }

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * sparklePulseSpeed) * sparklePulseScale;
            for (int i = 0; i < sparkleAccents.Length; i++)
            {
                if (sparkleAccents[i] != null)
                {
                    sparkleAccents[i].localScale = Vector3.one * pulse;
                }
            }
        }

        private void CacheComponents()
        {
            if (panelRoot == null) panelRoot = transform as RectTransform;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        private void CacheShownPosition()
        {
            if (cachedPosition || panelRoot == null)
            {
                return;
            }

            shownAnchoredPosition = panelRoot.anchoredPosition;
            cachedPosition = true;
        }
    }
}
