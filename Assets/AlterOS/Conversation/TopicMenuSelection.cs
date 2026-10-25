using System;

namespace AlterOS.Conversation
{
    public static class TopicMenuSelection
    {
        public static bool IsEligible(string action, string topicId, Func<string, bool> isAvailable)
        {
            return !IsStartAction(action) || (isAvailable != null && !string.IsNullOrWhiteSpace(topicId) && isAvailable(topicId));
        }

        public static bool TryStart(string action, string topicId, Func<string, bool> isAvailable, Action<string> start)
        {
            if (!IsStartAction(action) || string.IsNullOrWhiteSpace(topicId) || isAvailable == null || start == null ||
                !isAvailable(topicId)) return false;
            start(topicId);
            return true;
        }

        private static bool IsStartAction(string action)
        { return string.Equals(action, "topic_start", StringComparison.OrdinalIgnoreCase); }
    }
}
