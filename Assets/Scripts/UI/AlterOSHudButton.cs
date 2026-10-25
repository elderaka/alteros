using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public class AlterOSHudButton : MonoBehaviour
    {
        [SerializeField] private AlterOSHudTab tab = AlterOSHudTab.Assistant;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image icon;
        [SerializeField] private Image selectionDot;
        [SerializeField] private Color normalColor = new Color(0.86f, 0.88f, 0.95f, 0.72f);
        [SerializeField] private Color selectedColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.55f, 0.58f, 0.65f, 0.42f);

        private Button button;
        private AlterOSHudShell shell;

        public AlterOSHudTab Tab => tab;

        private void Awake()
        {
            CacheComponents();
            button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }
        }

        public void Bind(AlterOSHudShell owner)
        {
            shell = owner;
        }

        public void SetSelected(bool selected)
        {
            Color color = button != null && !button.interactable ? disabledColor : (selected ? selectedColor : normalColor);
            if (label != null) label.color = color;
            if (icon != null) icon.color = color;
            if (selectionDot != null) selectionDot.enabled = selected;
        }

        private void HandleClick()
        {
            shell?.SelectTab(tab);
        }

        private void CacheComponents()
        {
            if (button == null) button = GetComponent<Button>();
            if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
        }
    }
}
