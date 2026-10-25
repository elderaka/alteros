using AlterOS.Activities;
using AlterOS.Conversation;
using Pleiades.Furniture;
using TMPro;
using UnityEngine;

namespace AlterOS.UI
{
    /// <summary>Content binding only. The Canvas/prefab owns all static presentation.</summary>
    public sealed class ConversationRoutingDebugView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private FurnitureHandlingController debugModeOwner;
        [SerializeField] private DirectConversationBridge conversation;
        [SerializeField] private ActivitySceneHost activityHost;
        [SerializeField] private ActivitySequenceScenePrimitivePort primitivePort;
        private Canvas overlay;
        private ActivityHost observedHost;

        private void Awake() { overlay = GetComponent<Canvas>(); if (overlay != null) overlay.enabled = false; }
        private void OnEnable() { if (primitivePort != null) primitivePort.Diagnostic += OnPrimitiveDiagnostic; }
        private void OnDisable()
        {
            if (primitivePort != null) primitivePort.Diagnostic -= OnPrimitiveDiagnostic;
            if (observedHost != null) observedHost.LifecycleChanged -= OnActivityChanged;
            observedHost = null;
            if (overlay != null) overlay.enabled = false;
        }

        private void LateUpdate()
        {
            ActivityHost host = activityHost != null ? activityHost.Host : null;
            if (host != observedHost)
            {
                if (observedHost != null) observedHost.LifecycleChanged -= OnActivityChanged;
                observedHost = host;
                if (observedHost != null) observedHost.LifecycleChanged += OnActivityChanged;
            }
            bool visible = debugModeOwner != null && debugModeOwner.DebugMode != null && debugModeOwner.DebugMode.IsEnabled;
            if (overlay != null) overlay.enabled = visible;
            if (!visible || label == null || conversation == null) return;
            string report = conversation.RoutingDiagnostics.Report;
            if (label.text != report) label.text = report;
        }

        private void OnPrimitiveDiagnostic(string detail) { conversation?.RecordRoutingDiagnostic("primitive", detail); }
        private void OnActivityChanged(ActivityEvent item)
        {
            conversation?.RecordRoutingDiagnostic("activity_" + item.kind,
                "run=" + item.runId + " action=" + item.activityId + " phase=" + item.phase + " outcome=" + item.terminalReason);
        }
    }
}
