using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using AlterOS.Topics;

namespace AlterOS.Conversation
{
    [Serializable]
    public sealed class ConversationCoveragePolicy
    {
        public int schemaVersion = 1;
        public List<ConversationTopicCoverage> topics = new List<ConversationTopicCoverage>();
    }

    [Serializable]
    public sealed class ConversationTopicCoverage
    {
        public string topicId = "";
        public bool allowUncoveredDiscussion;
        public List<ConversationScenarioCoverage> scenarios = new List<ConversationScenarioCoverage>();
    }

    [Serializable]
    public sealed class ConversationScenarioCoverage
    {
        public string scenarioId = "";
        public string coverageSummary = "";
        public string experienceKey = "";
    }

    [Serializable]
    public sealed class ConversationCoverageSnapshot
    {
        public int schemaVersion = 1;
        public List<ConversationTopicCoverageSnapshot> topics = new List<ConversationTopicCoverageSnapshot>();
    }

    [Serializable]
    public sealed class ConversationTopicCoverageSnapshot
    {
        public string topicId = "";
        public bool allowUncoveredDiscussion;
        public string authoredAvailability = "blocked";
        public List<ConversationScenarioCoverage> scenarios = new List<ConversationScenarioCoverage>();
    }

    public static class ConversationCoverage
    {
        private const int MaximumSummaryCharacters = 400;
        private const int MaximumExperienceKeyCharacters = 128;

        public static ConversationCoveragePolicy ParsePolicy(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > ConversationContractValidator.MaximumProtocolBytes)
                throw new FormatException("Coverage policy is empty or exceeds the protocol limit.");
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ConversationCoveragePolicy));
                    ConversationCoveragePolicy policy = serializer.ReadObject(stream) as ConversationCoveragePolicy;
                    if (policy == null) throw new FormatException("Coverage policy must be an object.");
                    return policy;
                }
            }
            catch (FormatException) { throw; }
            catch (Exception exception) { throw new FormatException("Malformed coverage policy.", exception); }
        }

        public static List<string> ValidatePolicy(ConversationCoveragePolicy policy,
            IReadOnlyDictionary<string, TopicDefinition> catalogue)
        {
            var errors = new List<string>();
            if (policy == null) { errors.Add("Coverage policy is required."); return errors; }
            if (policy.schemaVersion != 1) errors.Add("Unsupported coverage policy schemaVersion.");
            if (policy.topics == null || policy.topics.Count == 0)
            { errors.Add("Coverage policy must contain Topic entries."); return errors; }
            if (catalogue == null) { errors.Add("Topic catalogue is required to validate coverage."); return errors; }

            var topicIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversationTopicCoverage topic in policy.topics)
            {
                if (topic == null) { errors.Add("Coverage Topic entry cannot be null."); continue; }
                if (string.IsNullOrWhiteSpace(topic.topicId) || topic.topicId.Length > 128)
                { errors.Add("Coverage Topic ID is invalid."); continue; }
                if (!topicIds.Add(topic.topicId)) errors.Add("Coverage Topic IDs must be unique.");

                TopicDefinition definition;
                if (!catalogue.TryGetValue(topic.topicId, out definition) || definition == null)
                { errors.Add("Coverage references an unknown Topic: " + topic.topicId); continue; }
                if (TopicDefinitionValidator.Validate(definition).Count != 0)
                { errors.Add("Coverage references an invalid Topic: " + topic.topicId); continue; }
                if (topic.scenarios == null)
                { errors.Add("Coverage scenario list is required for " + topic.topicId + "."); continue; }
                if (topic.allowUncoveredDiscussion && topic.scenarios.Count != definition.scenarios.Count)
                    errors.Add("Every authored scenario needs coverage metadata for enabled Topic " + topic.topicId + ".");

                var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
                var experienceKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (ConversationScenarioCoverage scenario in topic.scenarios)
                {
                    if (scenario == null) { errors.Add("Coverage scenario entry cannot be null."); continue; }
                    if (string.IsNullOrWhiteSpace(scenario.scenarioId) || scenario.scenarioId.Length > 128)
                    { errors.Add("Coverage scenario ID is invalid."); continue; }
                    if (!scenarioIds.Add(scenario.scenarioId)) errors.Add("Coverage scenario IDs must be unique within " + topic.topicId + ".");
                    if (definition.scenarios == null || !definition.scenarios.Exists(item => item != null && item.scenarioId == scenario.scenarioId))
                        errors.Add("Coverage references an unknown scenario: " + topic.topicId + "/" + scenario.scenarioId);
                    if (string.IsNullOrWhiteSpace(scenario.coverageSummary) || scenario.coverageSummary.Length > MaximumSummaryCharacters)
                        errors.Add("Coverage summary is required and limited to 400 characters.");
                    if (!ValidExperienceKey(scenario.experienceKey)) errors.Add("Coverage experienceKey is invalid.");
                    else if (!experienceKeys.Add(scenario.experienceKey)) errors.Add("Coverage experienceKeys must be unique within " + topic.topicId + ".");
                }
            }
            return errors;
        }

        public static bool AllowsUncoveredDiscussion(ConversationCoveragePolicy policy, string topicId)
        {
            if (policy == null || policy.topics == null || string.IsNullOrWhiteSpace(topicId)) return false;
            foreach (ConversationTopicCoverage topic in policy.topics)
                if (topic != null && topic.topicId == topicId)
                    return topic.allowUncoveredDiscussion && topic.scenarios != null && topic.scenarios.Count > 0;
            return false;
        }

        public static ConversationCoverageSnapshot BuildSnapshot(IEnumerable<string> candidateTopicIds,
            ConversationCoveragePolicy policy, IReadOnlyDictionary<string, TopicDefinition> catalogue,
            IReadOnlyDictionary<string, TopicInstance> authoredInstances, TopicContext context, TopicHistory history,
            DateTimeOffset nowUtc, string channel)
        {
            if (candidateTopicIds == null || catalogue == null || authoredInstances == null || context == null || history == null)
                throw new ArgumentNullException("Coverage snapshot inputs are required.");
            if (channel != "direct" && channel != "phone") throw new ArgumentException("Unsupported conversation channel.", nameof(channel));
            if (policy != null && ValidatePolicy(policy, catalogue).Count != 0)
                throw new ArgumentException("Coverage policy is invalid.", nameof(policy));

            var snapshot = new ConversationCoverageSnapshot();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string topicId in candidateTopicIds)
            {
                TopicDefinition definition;
                if (string.IsNullOrWhiteSpace(topicId) || !seen.Add(topicId) || !catalogue.TryGetValue(topicId, out definition) || definition == null)
                    throw new ArgumentException("Coverage snapshot references a missing or duplicate Topic.", nameof(candidateTopicIds));

                ConversationTopicCoverage rule = FindTopic(policy, topicId);
                var entry = new ConversationTopicCoverageSnapshot
                {
                    topicId = topicId,
                    allowUncoveredDiscussion = AllowsUncoveredDiscussion(policy, topicId),
                    authoredAvailability = "blocked"
                };
                if (rule != null)
                    foreach (ConversationScenarioCoverage scenario in rule.scenarios)
                        entry.scenarios.Add(new ConversationScenarioCoverage
                        { scenarioId = scenario.scenarioId, coverageSummary = scenario.coverageSummary, experienceKey = scenario.experienceKey });

                TopicInstance instance;
                if (authoredInstances.TryGetValue(topicId, out instance) && instance != null &&
                    TopicScenarioSelector.SelectForConversation(definition, instance, context, history, nowUtc, 0, channel).SelectedScenario != null)
                    entry.authoredAvailability = "ready";
                snapshot.topics.Add(entry);
            }
            return snapshot;
        }

        private static ConversationTopicCoverage FindTopic(ConversationCoveragePolicy policy, string topicId)
        {
            if (policy == null || policy.topics == null) return null;
            return policy.topics.Find(topic => topic != null && topic.topicId == topicId);
        }

        private static bool ValidExperienceKey(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumExperienceKeyCharacters) return false;
            foreach (char character in value)
                if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_' || character == '-' || character == '.'))
                    return false;
            return true;
        }
    }
}
