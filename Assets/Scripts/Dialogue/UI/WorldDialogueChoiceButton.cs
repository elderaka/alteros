using System;
using AlterOS.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlterOS.Dialogue.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public class WorldDialogueChoiceButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private AlterOSGlassPanel glassPanel;
        [SerializeField] private GameObject sparkle;
        [SerializeField] private float normalAlpha = 0.68f;
        [SerializeField] private float hoverAlpha = 1f;

        private Button button;
        private DialogueChoice choice;
        private Action<DialogueChoice, string> onSelected;

        private void Awake()
        {
            CacheComponents();
            SetHover(false);
        }

        public void Bind(DialogueChoice dialogueChoice, Action<DialogueChoice, string> selected)
        {
            CacheComponents();
            choice = dialogueChoice;
            onSelected = selected;

            if (label != null)
            {
                label.text = dialogueChoice != null ? dialogueChoice.label : string.Empty;
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(Select);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHover(false);
        }

        public void OnSelect(BaseEventData eventData)
        {
            SetHover(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            SetHover(false);
        }

        private void Select()
        {
            if (choice == null)
            {
                return;
            }

            onSelected?.Invoke(choice, choice.nextId);
        }

        private void SetHover(bool hover)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = hover ? hoverAlpha : normalAlpha;
            }

            if (glassPanel != null)
            {
                glassPanel.SetEmphasized(hover);
            }

            if (sparkle != null)
            {
                sparkle.SetActive(hover);
            }
        }

        private void CacheComponents()
        {
            if (button == null) button = GetComponent<Button>();
            if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (glassPanel == null) glassPanel = GetComponent<AlterOSGlassPanel>();
        }
    }
}
