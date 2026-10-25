namespace AlterOS.Semantics
{
    public enum TopicRouteFallbackReason
    {
        None,
        NoMatches,
        BelowMinimumSimilarity,
        NoSupportingAnchors,
    }

    public readonly struct TopicRouteDecision
    {
        public TopicRouteDecision(string selectedRouteId, bool usedFallback, TopicRouteFallbackReason reason)
        {
            SelectedRouteId = selectedRouteId;
            UsedFallback = usedFallback;
            Reason = reason;
        }

        public string SelectedRouteId { get; }
        public bool UsedFallback { get; }
        public TopicRouteFallbackReason Reason { get; }
    }

    public static class TopicRoutePolicy
    {
        public const float DefaultMinimumSimilarity = 0.45f;
        public const string DefaultFallbackRouteId = "unclear_short";

        public static TopicRouteDecision Decide(
            DialogueRouteMatch[] matches,
            float minimumSimilarity,
            string fallbackRouteId)
        {
            if (matches == null || matches.Length == 0)
                return Fallback(fallbackRouteId, TopicRouteFallbackReason.NoMatches);

            DialogueRouteMatch best = matches[0];
            if (best.BestSimilarity < minimumSimilarity)
                return Fallback(fallbackRouteId, TopicRouteFallbackReason.BelowMinimumSimilarity);
            if (best.SupportCount <= 0)
                return Fallback(fallbackRouteId, TopicRouteFallbackReason.NoSupportingAnchors);

            return new TopicRouteDecision(best.RouteId, false, TopicRouteFallbackReason.None);
        }

        static TopicRouteDecision Fallback(string fallbackRouteId, TopicRouteFallbackReason reason)
        {
            return new TopicRouteDecision(fallbackRouteId, true, reason);
        }
    }
}
