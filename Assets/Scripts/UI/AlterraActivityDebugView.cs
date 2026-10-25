using AlterOS.Activities;
using AlterOS.Conversation;
using AlterOS.Debugging;
using TMPro;
using UnityEngine;

namespace AlterOS.UI
{
    /// <summary>Binds diagnostic state to an Editor-authored world-space Canvas.</summary>
    public sealed class AlterraActivityDebugView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private ActivitySceneHost activityHost;
        [SerializeField] private DirectConversationBridge conversation;
        [SerializeField] private DialogueManager dialogue;
        [SerializeField] private Camera viewingCamera;
        private Canvas overlayCanvas;
        private DebugModeState debugMode;
        private float nextRefresh;

        private void Awake()
        {
            overlayCanvas = GetComponent<Canvas>();
            ApplyVisibility();
        }

        private void OnEnable()
        {
            if (debugMode != null) debugMode.Changed += OnDebugModeChanged;
            ApplyVisibility();
        }

        private void OnDisable()
        {
            if (debugMode != null) debugMode.Changed -= OnDebugModeChanged;
        }

        public void BindDebugModeState(DebugModeState sharedState)
        {
            if (debugMode == sharedState) return;
            if (debugMode != null) debugMode.Changed -= OnDebugModeChanged;
            debugMode = sharedState;
            if (isActiveAndEnabled && debugMode != null) debugMode.Changed += OnDebugModeChanged;
            ApplyVisibility();
        }

        private void OnDebugModeChanged(bool enabled)
        {
            ApplyVisibility();
            if (enabled) nextRefresh = 0f;
        }

        private void ApplyVisibility()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool visible = debugMode != null && debugMode.IsEnabled;
#else
            bool visible = false;
#endif
            if (overlayCanvas == null) overlayCanvas = GetComponent<Canvas>();
            if (overlayCanvas != null) overlayCanvas.enabled = visible;
        }

        private void LateUpdate()
        {
            if (!CanDisplay()) return;
            if (viewingCamera != null) transform.rotation = viewingCamera.transform.rotation;
            if (label == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            ActivityView action = activityHost != null && activityHost.Host != null ? activityHost.Host.Current : null;
            string state = dialogue != null && dialogue.IsActive ? "Talking" :
                action != null ? action.phase : activityHost != null && activityHost.Host != null ? "Idle" : "Initializing / unavailable";
            string text = "State: " + state + "\nAction: " + (action == null ? "none" : action.capabilityId + " / " + action.phase) +
                "\nScenario: " + (conversation == null ? "none" : conversation.ScenarioDebugState);
            if (label.text != text) label.text = text;
        }

        private bool CanDisplay()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return debugMode != null && debugMode.IsEnabled;
#else
            return false;
#endif
        }
    }
}
