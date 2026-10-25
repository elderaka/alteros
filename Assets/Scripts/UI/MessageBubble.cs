using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PleiadesOS.UI
{
    public class MessageBubble : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI senderLabel;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Image background;

        public void SetMessage(string sender, string message, string emotion = null, string sentiment = null, string condition = null)
        {
            string label = sender;
            if (!string.IsNullOrEmpty(emotion)) label += $" [{emotion}]";
            if (!string.IsNullOrEmpty(sentiment)) label += $" | {sentiment}";
            if (!string.IsNullOrEmpty(condition)) label += $" ({condition})";

            if (senderLabel != null) senderLabel.text = label;
            if (messageText != null) messageText.text = message;
        }
    }
}
