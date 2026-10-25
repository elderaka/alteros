using AlterOS.Progression;
using TMPro;
using UnityEngine;

namespace AlterOS.UI
{
    public class AlterOSCreditsPillView : MonoBehaviour
    {
        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private TextMeshProUGUI creditsText;
        [SerializeField] private string emptyValue = "0";

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
            if (creditsText == null)
            {
                return;
            }

            if (progressionStore == null || progressionStore.State == null || progressionStore.State.player == null)
            {
                creditsText.text = emptyValue;
                return;
            }

            creditsText.text = progressionStore.State.player.credits.ToString("N0");
        }
    }
}
