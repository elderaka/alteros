using System;
using AlterOS.Interaction;

namespace AlterOS.Conversation
{
    internal static class ConversationDatabaseGate
    {
        internal static readonly object Writer = new object();
    }

    public sealed class ConversationStoreLoadResult
    {
        public bool Success { get; internal set; }
        public long Revision { get; internal set; }
        public ConversationSessionDatabase Database { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class ConversationStoreSaveResult
    {
        public bool Success { get; internal set; }
        public string State { get; internal set; }
        public long Revision { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class ConversationSessionImportResult
    {
        public string State { get; internal set; }
        public string SourceHash { get; internal set; }
        public string OperationId { get; internal set; }
        public int RecordCount { get; internal set; }
        public long Revision { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class ConversationIdentityResult
    {
        public string State { get; internal set; }
        public string Value { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class ConversationTranscriptImportResult
    {
        public string State { get; internal set; }
        public string SourceHash { get; internal set; }
        public string OperationId { get; internal set; }
        public long RecordCount { get; internal set; }
        public long Revision { get; internal set; }
        public string Error { get; internal set; }
    }

    public interface IConversationTranscriptStore
    {
        ConversationAppendResult Append(ConversationTranscriptEvent value);
        ConversationTranscriptPage ReadPage(string cursor, int count);
        ConversationHistorySnapshot ReadRecent(int maxUtterances, int maxCharacters);
    }
}
