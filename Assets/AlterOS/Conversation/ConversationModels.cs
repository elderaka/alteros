using System;
using System.Collections.Generic;

namespace AlterOS.Interaction
{
    [Serializable]
    public sealed class ConversationSessionDatabase
    {
        public List<ConversationSessionRecord> sessions = new List<ConversationSessionRecord>();
        public List<RepetitionMemoryRecord> repetitions = new List<RepetitionMemoryRecord>();
        public AmbientMemoryRecord lastAmbientLine = new AmbientMemoryRecord();
    }

    public enum ConversationTurnSource
    {
        Unknown,
        LocalDialogue,
        PreparedBranch,
        Replay,
        Player
    }

    public enum GeneratedDialogueStatus
    {
        Unknown,
        Generating,
        Ready,
        Failed,
        Archived
    }

    public enum ActiveConversationMode
    {
        None,
        Light,
        Deep,
        PreparedBranch
    }

    [Serializable]
    public sealed class SessionRoutingState
    {
        public ActiveConversationMode activeMode = ActiveConversationMode.None;
        public string activeGeneratedDialogueId;
        public string activeIntentId;
        public string lastAssistantSource;
    }

    [Serializable]
    public sealed class ConversationTurnRecord
    {
        public string turnId;
        public string sessionId;
        public string speaker;
        public string text;
        public string translatedText;
        public string emotion;
        public string timestamp;
        public ConversationTurnSource source = ConversationTurnSource.Unknown;
        public string intentId;
        public string linkedGeneratedDialogueId;
        public string linkedAmbientLineId;
    }

    [Serializable]
    public sealed class ConversationSessionRecord
    {
        public string sessionId;
        public string sessionKind = "companion";
        public string startedAt;
        public string endedAt;
        public string lastActiveAt;
        public string title;
        public string summary;
        public bool isActive;
        public SessionRoutingState routingState = new SessionRoutingState();
        public List<ConversationTurnRecord> turns = new List<ConversationTurnRecord>();
    }

    [Serializable]
    public sealed class GeneratedDialogueRecord
    {
        public string generatedDialogueId;
        public string originSessionId;
        public string title;
        public string sourcePrompt;
        public string summary;
        public string createdAt;
        public string lastPlayedAt;
        public GeneratedDialogueStatus status = GeneratedDialogueStatus.Unknown;
        public bool wasPlayed;
        public bool surfacedToPlayer;
        public List<string> tags = new List<string>();
        public string jsonPath;
        public string payloadState;
        public string payloadHash;
        public string payloadDiagnostic;
    }

    [Serializable]
    public sealed class AmbientMemoryRecord
    {
        public string lineId;
        public string topic;
        public string text;
        public string triggeredAt;
        public bool followUpConsumed;
    }

    [Serializable]
    public sealed class RepetitionMemoryRecord
    {
        public string key;
        public int recentCount;
        public string lastTriggeredAt;
        public int stageUsed;
    }
}
