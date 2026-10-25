using System;
using System.Collections;
using AlterOS.Security;
using Pleiades.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AlterOS.UI
{
    [DisallowMultipleComponent]
    public sealed class OpenRouterSettingsPanel : MonoBehaviour
    {
        private const string KeyCheckUrl = "https://openrouter.ai/api/v1/key";

        public static event Action CredentialsChanged;

        [Header("Authored Canvas UI")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_InputField apiKeyInput;
        [SerializeField] private TMP_Text credentialStatusText;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button testButton;
        [SerializeField] private Button clearButton;
        [SerializeField] private Button closeButton;

        private PlayerController playerController;
        private DialogueManager dialogueManager;
        private Coroutine connectionCheck;
        private UnityWebRequest activeRequest;
        private bool cursorLockedBeforeOpen;
        private bool cursorVisibleBeforeOpen;
        private bool ownsCursor;

        private void Awake()
        {
            playerController = FindAnyObjectByType<PlayerController>();
            dialogueManager = FindAnyObjectByType<DialogueManager>();
            BindButtons();
            if (panelRoot != null) panelRoot.SetActive(false);
            RefreshCredentialStatus();
        }

        private void OnDestroy()
        {
            StopConnectionCheck();
            UnbindButtons();
            ReleaseCursor();
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            if (panelRoot != null && panelRoot.activeSelf)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    Close();
                    return;
                }

                if (Keyboard.current.pKey.wasPressedThisFrame && !IsKeyFieldFocused()) Close();
                return;
            }

            if (Keyboard.current.pKey.wasPressedThisFrame &&
                (dialogueManager == null || !dialogueManager.BlocksPlayerControl) &&
                !IsAnyInputFieldFocused())
            {
                Open();
            }
        }

        public void Open()
        {
            if (panelRoot == null || panelRoot.activeSelf) return;
            if (dialogueManager != null && dialogueManager.BlocksPlayerControl) return;

            cursorLockedBeforeOpen = Cursor.lockState == CursorLockMode.Locked;
            cursorVisibleBeforeOpen = Cursor.visible;
            ownsCursor = true;
            panelRoot.SetActive(true);
            resultText.text = string.Empty;
            RefreshCredentialStatus();

            if (playerController != null) playerController.SetExternalHudCursorMode(true);
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            EventSystem.current?.SetSelectedGameObject(apiKeyInput != null ? apiKeyInput.gameObject : closeButton.gameObject);
            apiKeyInput?.ActivateInputField();
        }

        public void Close()
        {
            StopConnectionCheck();
            if (apiKeyInput != null) apiKeyInput.text = string.Empty;
            if (panelRoot != null) panelRoot.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            ReleaseCursor();
            RefreshCredentialStatus();
        }

        public void SaveKey()
        {
            string key = apiKeyInput != null ? apiKeyInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                SetResult("Paste an OpenRouter API key first.");
                return;
            }

            if (!OpenRouterCredentialStore.SaveUserApiKey(key))
            {
                SetResult("Could not save the key securely on this platform.");
                return;
            }

            apiKeyInput.text = string.Empty;
            SetResult("Key saved securely.");
            RefreshCredentialStatus();
            CredentialsChanged?.Invoke();
        }

        public void ClearSavedKey()
        {
            try
            {
                OpenRouterCredentialStore.ClearUserApiKey();
                if (apiKeyInput != null) apiKeyInput.text = string.Empty;
                SetResult("Saved key removed. Environment overrides are unchanged.");
                CredentialsChanged?.Invoke();
            }
            catch
            {
                SetResult("Could not remove the saved key.");
            }

            RefreshCredentialStatus();
        }

        public void TestConnection()
        {
            if (connectionCheck != null) return;

            string draftKey = apiKeyInput != null ? apiKeyInput.text.Trim() : string.Empty;
            string key = string.IsNullOrEmpty(draftKey) ? OpenRouterCredentialStore.GetApiKey() : draftKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                SetResult("Add an API key before testing the connection.");
                return;
            }

            if (saveButton != null) saveButton.interactable = false;
            if (testButton != null) testButton.interactable = false;
            SetResult("Checking the key with OpenRouter…");
            connectionCheck = StartCoroutine(CheckConnection(key));
        }

        private IEnumerator CheckConnection(string key)
        {
            using (var request = UnityWebRequest.Get(KeyCheckUrl))
            {
                activeRequest = request;
                request.timeout = 15;
                request.SetRequestHeader("Authorization", "Bearer " + key);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                    SetResult("OpenRouter accepted the key. No text or speech was generated.");
                else if (request.responseCode == 401)
                    SetResult("OpenRouter rejected the key. Check it and try again.");
                else if (request.responseCode == 403)
                    SetResult("OpenRouter denied this key's access.");
                else if (request.responseCode > 0)
                    SetResult("Could not verify the key (HTTP " + request.responseCode + ").");
                else
                    SetResult("Could not reach OpenRouter. Check your connection and try again.");
            }

            activeRequest = null;
            connectionCheck = null;
            if (saveButton != null) saveButton.interactable = true;
            if (testButton != null) testButton.interactable = true;
        }

        private void RefreshCredentialStatus()
        {
            if (credentialStatusText == null) return;

            switch (OpenRouterCredentialStore.GetStatus())
            {
                case OpenRouterCredentialStatus.EnvironmentOverride:
                    credentialStatusText.text = "An environment key is active and takes priority over the saved key.";
                    break;
                case OpenRouterCredentialStatus.Saved:
                    credentialStatusText.text = "OpenRouter key saved for this Windows user.";
                    break;
                case OpenRouterCredentialStatus.SavedUnavailable:
                    credentialStatusText.text = "The saved key could not be read on this user account. You can replace it.";
                    break;
                default:
                    credentialStatusText.text = "No OpenRouter key is configured.";
                    break;
            }
        }

        private void BindButtons()
        {
            if (saveButton != null) saveButton.onClick.AddListener(SaveKey);
            if (testButton != null) testButton.onClick.AddListener(TestConnection);
            if (clearButton != null) clearButton.onClick.AddListener(ClearSavedKey);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void UnbindButtons()
        {
            if (saveButton != null) saveButton.onClick.RemoveListener(SaveKey);
            if (testButton != null) testButton.onClick.RemoveListener(TestConnection);
            if (clearButton != null) clearButton.onClick.RemoveListener(ClearSavedKey);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }

        private void StopConnectionCheck()
        {
            if (connectionCheck == null) return;
            if (activeRequest != null) activeRequest.Abort();
            activeRequest = null;
            StopCoroutine(connectionCheck);
            connectionCheck = null;
            if (saveButton != null) saveButton.interactable = true;
            if (testButton != null) testButton.interactable = true;
        }

        private bool IsKeyFieldFocused()
        {
            return apiKeyInput != null && apiKeyInput.isFocused;
        }

        private static bool IsAnyInputFieldFocused()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private void ReleaseCursor()
        {
            if (!ownsCursor) return;
            ownsCursor = false;
            if (playerController != null) playerController.SetExternalHudCursorMode(false);
            else
            {
                Cursor.lockState = cursorLockedBeforeOpen ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = cursorVisibleBeforeOpen;
            }
        }

        private void SetResult(string message)
        {
            if (resultText != null) resultText.text = message;
        }
    }
}
