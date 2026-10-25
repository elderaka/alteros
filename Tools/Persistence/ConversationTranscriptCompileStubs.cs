// Compile-only conversation shapes for transcript-to-memory mapping checks.
namespace AlterOS.Conversation
{
    public sealed class ConversationPresentedUtteranceV2
    {
        public readonly string speaker, source, responseId, nodeId, text, sourceId;
        public ConversationPresentedUtteranceV2(string utteranceSpeaker, string utteranceSource, string id, string node,
            string utteranceText, string transcriptSourceId = "")
        { speaker = utteranceSpeaker; source = utteranceSource; responseId = id ?? ""; nodeId = node ?? ""; text = utteranceText; sourceId = transcriptSourceId ?? ""; }
    }

    public sealed class ConversationJobV2 { }

    public static class ConversationProtocol
    {
        public const int MaximumResultBytes = 128 * 1024;
    }
}

namespace AlterOS.Memory
{
    public static class PlayerMemoryProtocol
    {
        public const int MaximumSourceSyncBytes = 16 * 1024;
    }
}
