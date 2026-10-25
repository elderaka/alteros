using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.UI
{
    public class AlterOSAssistantStatusPill : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Image statusDot;
        [SerializeField] private Color readyColor = new Color(0.42f, 0.95f, 0.58f, 1f);
        [SerializeField] private Color busyColor = new Color(1f, 0.76f, 0.38f, 1f);
        [SerializeField] private Color offlineColor = new Color(0.68f, 0.72f, 0.8f, 1f);

        public void SetReady()
        {
            SetStatus("Assistant Ready", readyColor);
        }

        public void SetBusy(string label = "Assistant Thinking")
        {
            SetStatus(label, busyColor);
        }

        public void SetOffline()
        {
            SetStatus("Assistant Offline", offlineColor);
        }

        private void OnEnable()
        {
            if (statusText != null && string.IsNullOrWhiteSpace(statusText.text))
            {
                SetReady();
            }
        }

        private void SetStatus(string label, Color dotColor)
        {
            if (statusText != null) statusText.text = label;
            if (statusDot != null) statusDot.color = dotColor;
        }
    }
}
