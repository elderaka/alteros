using AlterOS.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.UI
{
    public class AlterOSStatusCardView : MonoBehaviour
    {
        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private Image portraitImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI moodText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI affinityText;
        [SerializeField] private Image[] heartImages;
        [SerializeField] private Color filledHeartColor = new Color(1f, 0.52f, 0.66f, 1f);
        [SerializeField] private Color emptyHeartColor = new Color(0.78f, 0.82f, 0.9f, 0.45f);
        [SerializeField] private string companionName = "Altera";

        private void Awake()
        {
            if (progressionStore == null)
            {
                progressionStore = FindObjectOfType<AlteraProgressionStore>();
            }

            Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (nameText != null) nameText.text = companionName;

            if (progressionStore == null || progressionStore.State == null || progressionStore.State.altera == null)
            {
                if (moodText != null) moodText.text = "Calm";
                if (levelText != null) levelText.text = "Lv. 1";
                if (affinityText != null) affinityText.text = "Affinity 0";
                SetHearts(3);
                return;
            }

            AlteraStatsState altera = progressionStore.State.altera;
            if (moodText != null) moodText.text = ToTitle(altera.mood);
            if (levelText != null) levelText.text = $"Lv. {altera.bondLevel}";
            if (affinityText != null) affinityText.text = $"Affinity {altera.affinity:0}";

            int filledHearts = Mathf.Clamp(Mathf.CeilToInt(altera.affinity / 20f), 0, heartImages != null ? heartImages.Length : 0);
            SetHearts(filledHearts);
        }

        private void SetHearts(int filledCount)
        {
            if (heartImages == null)
            {
                return;
            }

            for (int i = 0; i < heartImages.Length; i++)
            {
                if (heartImages[i] != null)
                {
                    heartImages[i].color = i < filledCount ? filledHeartColor : emptyHeartColor;
                }
            }
        }

        private static string ToTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Calm";
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
        }
    }
}
