namespace AlterOS.Dialogue
{
    public enum ConversationMode
    {
        Topic,
        Agent
    }

    public static class ConversationModePolicy
    {
        public static ConversationMode Default => ConversationMode.Topic;

        public static bool CanSelect(ConversationMode mode, bool agentAvailable)
        {
            return mode == ConversationMode.Topic || agentAvailable;
        }
    }
}
