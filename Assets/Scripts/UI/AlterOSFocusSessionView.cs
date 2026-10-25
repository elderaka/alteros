using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.UI
{
    public class AlterOSFocusSessionView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI buttonLabel;
        [SerializeField] private Image progressFill;
        [SerializeField] private Button toggleButton;
        [SerializeField] private float sessionMinutes = 25f;
        [SerializeField] private string idleStatus = "Pomodoro";
        [SerializeField] private string runningStatus = "remaining";

        private float remainingSeconds;
        private bool running;

        private void Awake()
        {
            remainingSeconds = sessionMinutes * 60f;
            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(Toggle);
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(Toggle);
            }
        }

        private void Update()
        {
            if (!running)
            {
                return;
            }

            remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.unscaledDeltaTime);
            if (remainingSeconds <= 0f)
            {
                running = false;
            }

            Refresh();
        }

        public void Toggle()
        {
            running = !running;
            if (remainingSeconds <= 0f)
            {
                remainingSeconds = sessionMinutes * 60f;
            }

            Refresh();
        }

        public void ResetSession()
        {
            running = false;
            remainingSeconds = sessionMinutes * 60f;
            Refresh();
        }

        private void Refresh()
        {
            int minutes = Mathf.FloorToInt(remainingSeconds / 60f);
            int seconds = Mathf.FloorToInt(remainingSeconds % 60f);
            if (timerText != null) timerText.text = $"{minutes:00}:{seconds:00}";
            if (statusText != null) statusText.text = running ? runningStatus : idleStatus;
            if (buttonLabel != null) buttonLabel.text = running ? "End Session" : "Start Focus";

            if (progressFill != null)
            {
                float total = Mathf.Max(1f, sessionMinutes * 60f);
                progressFill.fillAmount = 1f - remainingSeconds / total;
            }
        }
    }
}
