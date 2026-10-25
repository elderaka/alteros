using System;
using System.Collections.Generic;
using AlterOS.Stats;

namespace AlterOS.Topics
{
    [Serializable]
    public sealed class TopicCondition
    {
        public string kind = "";
        public List<TopicCondition> children = new List<TopicCondition>();
        public string statId = "";
        public string operation = "";
        public float value;
        public string key = "";
        public bool expected = true;
        public int startHour = -1;
        public int endHour = -1;
        public string objectKind = "";
    }

    public enum TopicTruth { False, True, Unknown }

    public sealed class TopicContext
    {
        public StatSnapshot stats = new StatSnapshot();
        public int localHour = -1;
        public Dictionary<string, bool> facts = new Dictionary<string, bool>(StringComparer.Ordinal);
        public bool objectsComplete;
        public List<TopicObjectSnapshot> objects = new List<TopicObjectSnapshot>();
    }

    public sealed class TopicObjectSnapshot
    {
        public string id = "";
        public string kind = "";
        public Dictionary<string, bool> flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<string, double> numbers = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    public sealed class TopicConditionResult
    {
        public TopicTruth Truth { get; private set; }
        public string Reason { get; private set; }
        public string BoundObjectId { get; private set; }
        public IReadOnlyList<TopicConditionResult> Children { get; private set; }

        internal TopicConditionResult(TopicTruth truth, string reason, string boundObjectId,
            List<TopicConditionResult> children = null)
        {
            Truth = truth;
            Reason = reason;
            BoundObjectId = boundObjectId;
            Children = (children ?? new List<TopicConditionResult>()).AsReadOnly();
        }
    }
}
