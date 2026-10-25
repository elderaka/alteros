using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pleiades.Furniture
{
    public sealed class FurnitureInventoryRow : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        private string definitionId;
        private Action<string> selected;

        public void Bind(string id, string displayName, string count, bool available, Action<string> onSelected)
        {
            definitionId = id ?? string.Empty;
            selected = onSelected;
            if (label != null) label.text = (displayName ?? definitionId) + "     " + (count ?? "0");
            if (button == null) return;
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
            button.interactable = available;
        }

        private void OnClick()
        {
            selected?.Invoke(definitionId);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnClick);
        }
    }
}
