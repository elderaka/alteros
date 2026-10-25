using System;
using AlterOS.Dialogue;
using UnityEngine;

namespace AlterOS.UI
{
    [DisallowMultipleComponent]
    public class AlterOSGameSettings : MonoBehaviour
    {
        private const string MasterVolumeKey = "alteros.settings.master_volume";
        private const string TtsEnabledKey = "alteros.settings.tts_enabled";
        private const string InterfaceModeKey = "alteros.settings.interface_mode";
        private const float DefaultMasterVolume = 0.8f;

        public enum AlterOSInterfaceMode
        {
            Phone = 0,
            Desktop = 1
        }

        [SerializeField, Range(0f, 1f)] private float masterVolume = DefaultMasterVolume;
        [SerializeField] private bool ttsEnabled = true;
        [SerializeField] private AlterOSInterfaceMode interfaceMode = AlterOSInterfaceMode.Phone;

        public event Action<AlterOSGameSettings> Changed;

        public float MasterVolume => masterVolume;
        public bool TtsEnabled => ttsEnabled;
        public AlterOSInterfaceMode InterfaceMode => interfaceMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapSettings()
        {
            if (FindObjectOfType<AlterOSGameSettings>() != null)
            {
                return;
            }

            GameObject settingsObject = new GameObject("AlterOSGameSettings");
            DontDestroyOnLoad(settingsObject);
            settingsObject.AddComponent<AlterOSGameSettings>();
        }

        private void Awake()
        {
            Load();
            Apply();
        }

        public void SetMasterVolume(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(masterVolume, clamped))
            {
                return;
            }

            masterVolume = clamped;
            Save();
            Apply();
            Changed?.Invoke(this);
        }

        public void SetTtsEnabled(bool enabled)
        {
            if (ttsEnabled == enabled)
            {
                return;
            }

            ttsEnabled = enabled;
            Save();
            Apply();
            Changed?.Invoke(this);
        }

        public void SetInterfaceMode(AlterOSInterfaceMode mode)
        {
            if (interfaceMode == mode)
            {
                return;
            }

            interfaceMode = mode;
            Save();
            Changed?.Invoke(this);
        }

        public void ToggleTtsEnabled()
        {
            SetTtsEnabled(!ttsEnabled);
        }

        public void ToggleInterfaceMode()
        {
            SetInterfaceMode(interfaceMode == AlterOSInterfaceMode.Phone
                ? AlterOSInterfaceMode.Desktop
                : AlterOSInterfaceMode.Phone);
        }

        public void Load()
        {
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
            ttsEnabled = PlayerPrefs.GetInt(TtsEnabledKey, 1) != 0;
            interfaceMode = (AlterOSInterfaceMode)Mathf.Clamp(
                PlayerPrefs.GetInt(InterfaceModeKey, (int)AlterOSInterfaceMode.Phone),
                (int)AlterOSInterfaceMode.Phone,
                (int)AlterOSInterfaceMode.Desktop);
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
            PlayerPrefs.SetInt(TtsEnabledKey, ttsEnabled ? 1 : 0);
            PlayerPrefs.SetInt(InterfaceModeKey, (int)interfaceMode);
            PlayerPrefs.Save();
        }

        public void Apply()
        {
            AudioListener.volume = masterVolume;

            DialogueManager dialogueManager = FindObjectOfType<DialogueManager>();
            if (dialogueManager != null)
            {
                dialogueManager.SetSkipDialogueTts(!ttsEnabled);
            }
        }
    }
}
