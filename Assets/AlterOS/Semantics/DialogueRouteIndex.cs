using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlterOS.Semantics
{
    public readonly struct DialogueRouteMatch
    {
        public DialogueRouteMatch(string routeId, float score, float bestSimilarity, int supportCount, string bestAnchor)
        {
            RouteId = routeId;
            Score = score;
            BestSimilarity = bestSimilarity;
            SupportCount = supportCount;
            BestAnchor = bestAnchor;
        }

        public string RouteId { get; }
        public float Score { get; }
        public float BestSimilarity { get; }
        public int SupportCount { get; }
        public string BestAnchor { get; }
    }

    public sealed class DialogueRouteIndex
    {
        const int SupportedSchemaVersion = 1;
        const int NeighborsPerRoute = 3;

        readonly Anchor[] m_Anchors;
        readonly float[] m_Vectors;
        readonly Dictionary<string, List<int>> m_IndicesByRoute;

        DialogueRouteIndex(Anchor[] anchors, float[] vectors, int dimension)
        {
            if (anchors == null || anchors.Length == 0)
                throw new ArgumentException("Anchor metadata is empty.");
            if (dimension <= 0 || vectors.Length != anchors.Length * dimension)
                throw new ArgumentException("Anchor vector data does not match metadata.");

            Dimension = dimension;
            m_Anchors = anchors;
            m_Vectors = vectors;
            m_IndicesByRoute = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var index = 0; index < anchors.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(anchors[index].routeId) || string.IsNullOrWhiteSpace(anchors[index].text))
                    throw new ArgumentException("Anchor metadata contains an empty route or text.");
                if (!m_IndicesByRoute.TryGetValue(anchors[index].routeId, out var indexes))
                {
                    indexes = new List<int>();
                    m_IndicesByRoute.Add(anchors[index].routeId, indexes);
                }
                indexes.Add(index);
            }
        }

        public int Dimension { get; }
        public int AnchorCount => m_Anchors.Length;
        public int RouteCount => m_IndicesByRoute.Count;

        public static DialogueRouteIndex Load(TextAsset metadataAsset, TextAsset vectorAsset)
        {
            if (metadataAsset == null || vectorAsset == null)
                throw new ArgumentNullException(nameof(metadataAsset), "GTE route index assets are missing.");

            var metadata = JsonUtility.FromJson<AnchorCatalog>(metadataAsset.text);
            if (metadata == null || metadata.schemaVersion != SupportedSchemaVersion)
                throw new InvalidOperationException("Unsupported GTE route index metadata.");

            var vectorBytes = vectorAsset.bytes;
            if (vectorBytes.Length % sizeof(float) != 0)
                throw new InvalidOperationException("GTE route vectors are not float32 data.");
            var vectors = new float[vectorBytes.Length / sizeof(float)];
            Buffer.BlockCopy(vectorBytes, 0, vectors, 0, vectorBytes.Length);
            return new DialogueRouteIndex(metadata.anchors, vectors, metadata.dimension);
        }

        public DialogueRouteMatch[] Rank(float[] query, float minimumSimilarity = 0.45f)
        {
            if (query == null || query.Length != Dimension)
                throw new ArgumentException("Query vector dimension does not match the route index.");

            var similarities = new float[m_Anchors.Length];
            for (var anchorIndex = 0; anchorIndex < m_Anchors.Length; anchorIndex++)
            {
                var offset = anchorIndex * Dimension;
                var similarity = 0f;
                for (var dimension = 0; dimension < Dimension; dimension++)
                    similarity += m_Vectors[offset + dimension] * query[dimension];
                similarities[anchorIndex] = similarity;
            }

            var matches = new List<DialogueRouteMatch>(m_IndicesByRoute.Count);
            foreach (var pair in m_IndicesByRoute)
            {
                var nearest = new List<int>(NeighborsPerRoute);
                foreach (var anchorIndex in pair.Value)
                {
                    var insertAt = 0;
                    while (insertAt < nearest.Count && similarities[nearest[insertAt]] >= similarities[anchorIndex])
                        insertAt++;
                    if (insertAt >= NeighborsPerRoute)
                        continue;
                    nearest.Insert(insertAt, anchorIndex);
                    if (nearest.Count > NeighborsPerRoute)
                        nearest.RemoveAt(NeighborsPerRoute);
                }

                var supported = 0f;
                var weights = 0f;
                var supportCount = 0;
                for (var neighbor = 0; neighbor < nearest.Count; neighbor++)
                {
                    var similarity = similarities[nearest[neighbor]];
                    var weight = 1f / (neighbor + 1);
                    supported += similarity * weight;
                    weights += weight;
                    if (similarity >= minimumSimilarity)
                        supportCount++;
                }
                supported /= weights;
                var best = similarities[nearest[0]];
                var score = supported + 0.40f * (best - supported);
                matches.Add(new DialogueRouteMatch(pair.Key, score, best, supportCount, m_Anchors[nearest[0]].text));
            }

            matches.Sort((left, right) => right.Score.CompareTo(left.Score));
            return matches.ToArray();
        }

        [Serializable]
        sealed class AnchorCatalog
        {
            public int schemaVersion;
            public int dimension;
            public Anchor[] anchors;
        }

        [Serializable]
        sealed class Anchor
        {
            public string routeId;
            public string text;
        }
    }
}
