using AlterOS.Debugging;
using Pleiades.Furniture;
using UnityEngine;

namespace AlterOS.Conversation
{
    public sealed partial class DirectConversationBridge
    {
        [SerializeField] private FurnitureHandlingController routingDebugModeOwner;
        public ConversationRoutingDiagnostics RoutingDiagnostics { get; } = new ConversationRoutingDiagnostics();

        public void RecordRoutingDiagnostic(string stage, string detail)
        {
            string line = RoutingDiagnostics.Record(stage, detail);
            if (routingDebugModeOwner != null && routingDebugModeOwner.DebugMode != null && routingDebugModeOwner.DebugMode.IsEnabled)
                Debug.Log("[AlterOS Trace] " + line, this);
        }

        private void BeginRoutingDiagnostic(string text)
        {
            RoutingDiagnostics.Begin(text);
            RecordRoutingDiagnostic("submit", string.IsNullOrWhiteSpace(pendingExplicitTopicId) ? "free text" : "explicit " + pendingExplicitTopicId);
            if (routingDebugModeOwner != null && routingDebugModeOwner.DebugMode != null && routingDebugModeOwner.DebugMode.IsEnabled)
                Debug.Log("[AlterOS Trace] input=" + RoutingDiagnostics.LastInput, this);
        }
    }
}
