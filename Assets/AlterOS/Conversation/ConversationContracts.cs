using System;
using System.Collections.Generic;
using AlterOS.Dialogue;

namespace AlterOS.Conversation
{
    [Serializable]
    public sealed class ConversationRequest
    {
        public int schemaVersion = 1;
        public string requestId = "";
        public string sessionId = "";
        public string channel = "direct";
        public string text = "";
        public string activeResponseId = "";
        public string activeTopicId = "";
        public string explicitTopicId = "";
    }

    [Serializable]
    public sealed class TopicCandidateCard
    {
        public string topicId = "";
        public string prompt = "";
        public List<string> examples = new List<string>();
        public float similarity;
        public int supportCount;
        public string authoredAvailability = "unchecked";
    }

    [Serializable]
    public sealed class TopicCandidateSet
    {
        public string indexIdentity = "";
        public string source = "synthetic";
        public List<TopicCandidateCard> cards = new List<TopicCandidateCard>();
    }

    [Serializable]
    public sealed class ConversationJudgment
    {
        public int schemaVersion = 1;
        public string requestId = "";
        public string kind = "";
        public string topicId = "";
        public List<string> referencedTopicIds = new List<string>();
        public ConversationReply reply;
        public List<SuggestedPlayerReply> suggestedReplies = new List<SuggestedPlayerReply>();
    }

    [Serializable]
    public sealed class ConversationReply
    {
        public List<CanonicalDialogueNode> nodes = new List<CanonicalDialogueNode>();
    }

    [Serializable]
    public sealed class SuggestedPlayerReply
    {
        public string label = "";
        public string prompt = "";
    }

    [Serializable]
    public sealed class ConversationPlan
    {
        public string requestId = "";
        public string outcome = "unavailable";
        public string topicId = "";
        public string scenarioId = "";
        public ConversationReply reply;
        public List<SuggestedPlayerReply> suggestedReplies = new List<SuggestedPlayerReply>();
        public string diagnosticCode = "";
        public bool isNovelCandidate;
    }
}
