using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.Dialogue.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public class AlterOSGlassPanel : MonoBehaviour
    {
        [Header("Glass")]
        [SerializeField] private Color baseColor = new Color(0.36f, 0.42f, 0.55f, 0.42f);
        [SerializeField] private Color emphasizedColor = new Color(0.48f, 0.50f, 0.72f, 0.58f);
        [SerializeField] private bool useEmphasisPulse;
        [SerializeField] private float pulseSpeed = 1.5f;
        [SerializeField] private float pulseAmount = 0.08f;

        [Header("Outline")]
        [SerializeField] private bool manageOutline = true;
        [SerializeField] private Color outlineColor = new Color(0.84f, 0.84f, 1f, 0.72f);
        [SerializeField] private Vector2 outlineDistance = new Vector2(1.5f, -1.5f);

        [Header("Shadow")]
        [SerializeField] private bool manageShadow = true;
        [SerializeField] private Color shadowColor = new Color(0.05f, 0.06f, 0.10f, 0.34f);
        [SerializeField] private Vector2 shadowDistance = new Vector2(0f, -8f);

        private Image image;
        private Outline outline;
        private Shadow shadow;
        private bool emphasized;

        private void Awake()
        {
            CacheComponents();
            Apply();
        }

        private void OnEnable()
        {
            CacheComponents();
            Apply();
        }

        private void OnValidate()
        {
            CacheComponents();
            Apply();
        }

        private void Update()
        {
            if (!useEmphasisPulse || !emphasized || image == null)
            {
                return;
            }

            Color color = emphasizedColor;
            color.a = Mathf.Clamp01(emphasizedColor.a + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount);
            image.color = color;
        }

        public void SetEmphasized(bool value)
        {
            emphasized = value;
            Apply();
        }

        private void CacheComponents()
        {
            if (image == null) image = GetComponent<Image>();
            if (manageOutline && outline == null) outline = GetComponent<Outline>();
            if (manageShadow && shadow == null)
            {
                Shadow[] shadows = GetComponents<Shadow>();
                for (int i = 0; i < shadows.Length; i++)
                {
                    if (!(shadows[i] is Outline))
                    {
                        shadow = shadows[i];
                        break;
                    }
                }
            }
        }

        private void Apply()
        {
            if (image != null)
            {
                image.color = emphasized ? emphasizedColor : baseColor;
                image.raycastTarget = true;
            }

            if (manageOutline && outline != null)
            {
                outline.effectColor = outlineColor;
                outline.effectDistance = outlineDistance;
                outline.useGraphicAlpha = true;
            }

            if (manageShadow && shadow != null)
            {
                shadow.effectColor = shadowColor;
                shadow.effectDistance = shadowDistance;
                shadow.useGraphicAlpha = true;
            }
        }
    }
}
