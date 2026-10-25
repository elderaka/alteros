namespace AlterOS.Semantics
{
    // Workbench needs the production result value, but excludes UnityEngine-backed route indexing.
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
}
