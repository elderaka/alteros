using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using AlterOS.Content;
using AlterOS.Dialogue;

namespace AlterOS.Topics
{
    public static class TopicReplayPolicy
    {
        public const string Repeatable = "repeatable";
        public const string Once = "once";
        internal static bool IsValid(string value) { return value == Repeatable || value == Once; }
    }

    [Serializable]
    public sealed class TopicDefinition
    {
        public int schemaVersion = 1;
        public string topicId = "";
        public string menuLabel = "";
        public string prompt = "";
        public List<string> anchors = new List<string>();
        public string defaultReplayPolicy = TopicReplayPolicy.Repeatable;
        public double cooldownSeconds;
        public List<TopicScenario> scenarios = new List<TopicScenario>();
    }

    [Serializable]
    [DataContract]
    public sealed class TopicScenario
    {
        [DataMember(IsRequired = true)]
        public string scenarioId = "";
        [DataMember(IsRequired = true)]
        public int priority;
        [DataMember(IsRequired = true)]
        public double baseWeight = 1;
        [DataMember(IsRequired = true)]
        public TopicCondition condition;
        [DataMember(Name = "channels", IsRequired = false, EmitDefaultValue = false)]
        public List<string> channels = new List<string>();
        [DataMember(IsRequired = true)]
        public List<TopicWeightModifier> modifiers = new List<TopicWeightModifier>();
        [DataMember(IsRequired = true)]
        public string consumptionNodeId = "";
        [DataMember(Name = "scenarioRef", IsRequired = false, EmitDefaultValue = false)]
        public ContentRef scenarioRef;
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public List<CanonicalDialogueNode> nodes = new List<CanonicalDialogueNode>();
    }

    [Serializable]
    public sealed class TopicWeightModifier
    {
        public TopicCondition condition;
        public double amount;
    }

    [Serializable]
    public sealed class TopicInstance
    {
        public string instanceId = "";
        public string topicId = "";
        public string source = "authored";
        public string replayPolicy = TopicReplayPolicy.Repeatable;

        public static TopicInstance Create(TopicDefinition definition, string instanceId, string source, string replayOverride = null)
        {
            var instance = new TopicInstance
            {
                instanceId = instanceId,
                topicId = definition == null ? "" : definition.topicId,
                source = source,
                replayPolicy = replayOverride ?? (source == "generated" ? TopicReplayPolicy.Once :
                    definition == null ? null : definition.defaultReplayPolicy)
            };
            TopicDefinitionValidator.RequireValid(definition, instance);
            return instance;
        }
    }
}
