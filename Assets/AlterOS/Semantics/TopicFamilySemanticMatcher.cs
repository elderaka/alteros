using System;
using System.Collections.Generic;

namespace AlterOS.Semantics
{
    public sealed class TopicFamilyAnchorVector
    {
        public string TopicId { get; set; }
        public string Anchor { get; set; }
        public float[] Embedding { get; set; }
    }

    public sealed class TopicFamilySemanticMatch
    {
        public string TopicId { get; internal set; }
        public float Score { get; internal set; }
        public float BestSimilarity { get; internal set; }
        public int SupportCount { get; internal set; }
        public string BestAnchor { get; internal set; }
    }

    /// <summary>Stage one only: rank Topic families by shared anchors before scenario selection.</summary>
    public static class TopicFamilySemanticMatcher
    {
        public const float DefaultMinimumSimilarity = TopicRoutePolicy.DefaultMinimumSimilarity;

        public static TopicFamilySemanticMatch[] Rank(float[] query, IEnumerable<TopicFamilyAnchorVector> anchors,
            float minimumSimilarity = DefaultMinimumSimilarity)
        {
            if (query == null || query.Length == 0)
                throw new ArgumentException("Query embedding is empty.", nameof(query));
            if (float.IsNaN(minimumSimilarity) || float.IsInfinity(minimumSimilarity))
                throw new ArgumentOutOfRangeException(nameof(minimumSimilarity));

            double queryNorm = Norm(query);
            if (queryNorm <= 0)
                throw new ArgumentException("Query embedding has no magnitude.", nameof(query));

            var byTopic = new Dictionary<string, List<KeyValuePair<TopicFamilyAnchorVector, float>>>(StringComparer.Ordinal);
            if (anchors != null)
            {
                foreach (TopicFamilyAnchorVector anchor in anchors)
                {
                    if (anchor == null || string.IsNullOrWhiteSpace(anchor.TopicId) || string.IsNullOrWhiteSpace(anchor.Anchor) ||
                        anchor.Embedding == null || anchor.Embedding.Length != query.Length)
                        throw new ArgumentException("Family anchors require an ID, text, and matching embeddings.", nameof(anchors));
                    double anchorNorm = Norm(anchor.Embedding);
                    if (anchorNorm <= 0)
                        throw new ArgumentException("Family anchor embedding has no magnitude.", nameof(anchors));

                    double dot = 0;
                    for (int index = 0; index < query.Length; index++)
                        dot += (double)query[index] * anchor.Embedding[index];
                    float similarity = (float)(dot / (queryNorm * anchorNorm));
                    List<KeyValuePair<TopicFamilyAnchorVector, float>> topicAnchors;
                    if (!byTopic.TryGetValue(anchor.TopicId, out topicAnchors))
                    {
                        topicAnchors = new List<KeyValuePair<TopicFamilyAnchorVector, float>>();
                        byTopic.Add(anchor.TopicId, topicAnchors);
                    }
                    topicAnchors.Add(new KeyValuePair<TopicFamilyAnchorVector, float>(anchor, similarity));
                }
            }

            var matches = new List<TopicFamilySemanticMatch>(byTopic.Count);
            foreach (KeyValuePair<string, List<KeyValuePair<TopicFamilyAnchorVector, float>>> entry in byTopic)
            {
                float best = float.NegativeInfinity;
                int support = 0;
                string bestAnchor = string.Empty;
                foreach (KeyValuePair<TopicFamilyAnchorVector, float> anchor in entry.Value)
                {
                    if (anchor.Value > best || (anchor.Value == best && string.Compare(anchor.Key.Anchor, bestAnchor, StringComparison.Ordinal) < 0))
                    {
                        best = anchor.Value;
                        bestAnchor = anchor.Key.Anchor;
                    }
                    if (anchor.Value >= minimumSimilarity)
                        support++;
                }
                matches.Add(new TopicFamilySemanticMatch
                {
                    TopicId = entry.Key,
                    Score = best,
                    BestSimilarity = best,
                    SupportCount = support,
                    BestAnchor = bestAnchor,
                });
            }

            matches.Sort((left, right) =>
            {
                int byScore = right.Score.CompareTo(left.Score);
                return byScore != 0 ? byScore : string.Compare(left.TopicId, right.TopicId, StringComparison.Ordinal);
            });
            return matches.ToArray();
        }

        static double Norm(float[] vector)
        {
            double sum = 0;
            for (int index = 0; index < vector.Length; index++)
            {
                if (float.IsNaN(vector[index]) || float.IsInfinity(vector[index]))
                    throw new ArgumentException("Embedding contains a nonfinite value.");
                sum += (double)vector[index] * vector[index];
            }
            return Math.Sqrt(sum);
        }
    }
}
