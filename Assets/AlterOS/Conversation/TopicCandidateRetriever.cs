using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Semantics;
using AlterOS.Topics;

namespace AlterOS.Conversation
{
    public static class TopicCandidateRetriever
    {
        public const int MaximumCandidates = 8;
        public const int MaximumExamplesPerCandidate = 3;
        public const float MinimumOnlineAuthoredSimilarity = 0.85f;

        public static bool TryIncludeExplicitTopic(TopicCandidateSet candidates, string topicId,
            IReadOnlyDictionary<string, TopicDefinition> catalogue)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(topicId) || catalogue == null) return false;
            if (candidates.cards.Any(card => card != null && card.topicId == topicId)) return true;
            TopicDefinition definition;
            if (!catalogue.TryGetValue(topicId, out definition) || definition == null) return false;
            if (candidates.cards.Count >= MaximumCandidates) candidates.cards.RemoveAt(candidates.cards.Count - 1);
            candidates.cards.Insert(0, new TopicCandidateCard
            {
                topicId = topicId, prompt = definition.prompt ?? string.Empty, examples = new List<string>(),
                similarity = 0f, supportCount = 0, authoredAvailability = "unchecked"
            });
            return true;
        }

        /// <summary>Confidence-gates local fallback only; online RAG receives the ranked candidate set.</summary>
        public static TopicCandidateSet FilterForAuthoredFallback(TopicCandidateSet candidates, string hostSelectedTopicId)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var result = new TopicCandidateSet { indexIdentity = candidates.indexIdentity, source = candidates.source };
            foreach (TopicCandidateCard card in candidates.cards)
                if (card != null && (card.similarity >= MinimumOnlineAuthoredSimilarity || card.topicId == hostSelectedTopicId))
                    result.cards.Add(card);
            return result;
        }

        /// <summary>Adapts already-ranked matches from the warmed production index without another embedding/model worker.</summary>
        public static TopicCandidateSet FromRouteMatches(DialogueRouteMatch[] matches, string indexIdentity,
            IReadOnlyDictionary<string, TopicDefinition> catalogue)
        {
            if (string.IsNullOrWhiteSpace(indexIdentity) || indexIdentity.Length > 256 || catalogue == null)
                throw new ArgumentException("Route index identity and Topic catalogue are required.");
            var result = new TopicCandidateSet { indexIdentity = indexIdentity, source = "local_gte" };
            if (matches != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (DialogueRouteMatch match in matches)
                {
                    if (result.cards.Count == MaximumCandidates) break;
                    TopicDefinition definition;
                    if (string.IsNullOrWhiteSpace(match.RouteId)) throw new ArgumentException("GTE returned an empty route ID.", nameof(matches));
                    if (!seen.Add(match.RouteId)) continue;
                    if (!catalogue.TryGetValue(match.RouteId, out definition) || definition == null || definition.topicId != match.RouteId)
                        throw new ArgumentException("GTE route is missing from the promoted Topic catalogue: " + match.RouteId, nameof(matches));
                    var examples = new List<string>();
                    if (!string.IsNullOrWhiteSpace(match.BestAnchor)) examples.Add(match.BestAnchor);
                    result.cards.Add(new TopicCandidateCard
                    {
                        topicId = match.RouteId, prompt = definition.prompt ?? "", examples = examples,
                        similarity = match.BestSimilarity, supportCount = match.SupportCount, authoredAvailability = "unchecked"
                    });
                }
            }
            List<string> errors = ConversationContractValidator.ValidateCandidateSet(result);
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(matches));
            return result;
        }

        public static TopicCandidateSet Retrieve(float[] query, string queryIndexIdentity,
            IEnumerable<TopicFamilyAnchorVector> anchors, string anchorIndexIdentity,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, string source)
        {
            if (string.IsNullOrWhiteSpace(queryIndexIdentity) || queryIndexIdentity.Length > 256 ||
                !string.Equals(queryIndexIdentity, anchorIndexIdentity, StringComparison.Ordinal))
                throw new ArgumentException("Query and anchor indexes must have the same nonempty identity.");
            if (source != "local_gte" && source != "synthetic") throw new ArgumentException("Unsupported candidate source.", nameof(source));
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));

            var anchorList = anchors == null ? new List<TopicFamilyAnchorVector>() : new List<TopicFamilyAnchorVector>(anchors);
            TopicFamilySemanticMatch[] ranked = TopicFamilySemanticMatcher.Rank(query, anchorList);
            var cards = new List<TopicCandidateCard>(Math.Min(MaximumCandidates, ranked.Length));
            for (int index = 0; index < ranked.Length && cards.Count < MaximumCandidates; index++)
            {
                TopicFamilySemanticMatch match = ranked[index];
                TopicDefinition definition;
                if (!catalogue.TryGetValue(match.TopicId, out definition) || definition == null || definition.topicId != match.TopicId)
                    throw new ArgumentException("Retrieved Topic ID is absent from the supplied catalogue: " + match.TopicId, nameof(catalogue));
                List<TopicFamilyAnchorVector> topicAnchors = anchorList.Where(anchor => anchor != null && anchor.TopicId == match.TopicId)
                    .OrderByDescending(anchor => Similarity(query, anchor.Embedding))
                    .ThenBy(anchor => anchor.Anchor, StringComparer.Ordinal).ToList();
                var examples = new List<string>(MaximumExamplesPerCandidate);
                foreach (TopicFamilyAnchorVector anchor in topicAnchors)
                {
                    if (!examples.Contains(anchor.Anchor, StringComparer.Ordinal)) examples.Add(anchor.Anchor);
                    if (examples.Count == MaximumExamplesPerCandidate) break;
                }
                cards.Add(new TopicCandidateCard
                {
                    topicId = match.TopicId,
                    prompt = definition.prompt ?? "",
                    examples = examples,
                    similarity = match.BestSimilarity,
                    supportCount = match.SupportCount,
                    authoredAvailability = "unchecked",
                });
            }
            var result = new TopicCandidateSet { indexIdentity = queryIndexIdentity, source = source, cards = cards };
            List<string> errors = ConversationContractValidator.ValidateCandidateSet(result);
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(anchors));
            return result;
        }

        public static TopicCandidateSet PrepareForJudgment(TopicCandidateSet retrieved,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, IReadOnlyDictionary<string, TopicInstance> authoredInstances,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, string channel)
        {
            List<string> errors = ConversationContractValidator.ValidateCandidateSet(retrieved, false);
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(retrieved));
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (authoredInstances == null) throw new ArgumentNullException(nameof(authoredInstances));
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (channel != "direct" && channel != "phone") throw new ArgumentException("Unsupported conversation channel.", nameof(channel));

            var prepared = new TopicCandidateSet { indexIdentity = retrieved.indexIdentity, source = retrieved.source };
            foreach (TopicCandidateCard card in retrieved.cards)
            {
                var copy = new TopicCandidateCard
                {
                    topicId = card.topicId, prompt = card.prompt, similarity = card.similarity,
                    supportCount = card.supportCount, authoredAvailability = "blocked",
                    examples = new List<string>(card.examples),
                };
                TopicDefinition definition;
                TopicInstance instance;
                if (catalogue.TryGetValue(card.topicId, out definition) && authoredInstances.TryGetValue(card.topicId, out instance) && definition != null && instance != null)
                {
                    TopicSelectionResult selection = TopicScenarioSelector.SelectForConversation(definition, instance, context, history, nowUtc, 0d, channel);
                    if (selection.SelectedScenario != null) copy.authoredAvailability = "ready";
                }
                prepared.cards.Add(copy);
            }
            return prepared;
        }

        private static float Similarity(float[] left, float[] right)
        {
            double dot = 0, leftNorm = 0, rightNorm = 0;
            for (int index = 0; index < left.Length; index++)
            {
                dot += (double)left[index] * right[index];
                leftNorm += (double)left[index] * left[index];
                rightNorm += (double)right[index] * right[index];
            }
            return (float)(dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)));
        }
    }
}
