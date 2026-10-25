using System;
using System.Collections.Generic;

namespace AlterOS.Memory
{
    /// <summary>Removes forgotten transcript sources before recent dialogue reaches a provider.</summary>
    public static class MemoryConversationHistoryFilter
    {
        public static IReadOnlyList<Conversation.ConversationPresentedUtteranceV2> Filter(
            IEnumerable<Conversation.ConversationPresentedUtteranceV2> history,
            ISet<string> suppressedSourceIds, bool memoryPolicyRequired)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            var result = new List<Conversation.ConversationPresentedUtteranceV2>();
            foreach (Conversation.ConversationPresentedUtteranceV2 item in history)
            {
                if (item == null) continue;
                string sourceId = item.sourceId ?? string.Empty;
                if (memoryPolicyRequired && sourceId.Length == 0) continue;
                if (sourceId.Length > 0 && suppressedSourceIds != null && suppressedSourceIds.Contains(sourceId)) continue;
                result.Add(item);
            }
            return result.AsReadOnly();
        }
    }
}
