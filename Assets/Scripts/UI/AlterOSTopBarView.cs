using System;
using TMPro;
using UnityEngine;

namespace AlterOS.UI
{
    public class AlterOSTopBarView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI timeText;
        [SerializeField] private TextMeshProUGUI periodText;
        [SerializeField] private TextMeshProUGUI dateText;
        [SerializeField] private TextMeshProUGUI weatherText;
        [SerializeField] private string weatherLabel = "Clear";
        [SerializeField] private float refreshSeconds = 15f;

        private float nextRefreshTime;

        private void OnEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            Refresh();
        }

        public void SetWeather(string value)
        {
            weatherLabel = string.IsNullOrWhiteSpace(value) ? "Clear" : value;
            Refresh();
        }

        public void Refresh()
        {
            DateTime now = DateTime.Now;
            if (timeText != null) timeText.text = now.ToString("HH:mm");
            if (periodText != null) periodText.text = GetPeriod(now.Hour);
            if (dateText != null) dateText.text = now.ToString("MMM d\nddd");
            if (weatherText != null) weatherText.text = weatherLabel;

            nextRefreshTime = Time.unscaledTime + Mathf.Max(1f, refreshSeconds);
        }

        private static string GetPeriod(int hour)
        {
            if (hour < 5) return "Night";
            if (hour < 12) return "Morning";
            if (hour < 17) return "Afternoon";
            if (hour < 20) return "Evening";
            return "Night";
        }
    }
}
