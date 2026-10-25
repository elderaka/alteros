using AlterOS.Conversation;
using NUnit.Framework;

namespace AlterOS.Tests.Conversation
{
    public sealed class TopicSimilarityDiagnosticsTests
    {
        [Test]
        public void Format_ReportsAuthoredGateSeparatelyFromRagCandidates()
        {
            var candidates = new TopicCandidateSet();
            candidates.cards.Add(new TopicCandidateCard
            {
                topicId = "cats", similarity = 0.91f, authoredAvailability = "ready"
            });
            candidates.cards.Add(new TopicCandidateCard
            {
                topicId = "coffee", similarity = 0.72f, authoredAvailability = "blocked"
            });

            string report = TopicSimilarityDiagnostics.Format(candidates, true, 0.85f);

            StringAssert.Contains("mode=online decision=AUTHORED", report);
            StringAssert.Contains("cats similarity=0.910 availability=ready providerCandidate=yes", report);
            StringAssert.Contains("coffee similarity=0.720 availability=blocked providerCandidate=yes", report);
        }
    }
}
