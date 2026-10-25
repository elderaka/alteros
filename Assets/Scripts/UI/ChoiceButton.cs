using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PleiadesOS.UI
{
    public class ChoiceButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private Image background;

        private string _value;
        private Action<string, string> _onClicked;

        private void Start()
        {
            if (button != null)
                button.onClick.AddListener(OnBtnClicked);
        }

        public void Initialize(string label, string value, Action<string, string> onClicked)
        {
            if (labelText != null) labelText.text = label;
            _value = value;
            _onClicked = onClicked;
        }

        private void OnBtnClicked()
        {
            _onClicked?.Invoke(labelText.text, _value);
        }
    }
}
