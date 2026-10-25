using System.Globalization;
using System.Text;

namespace AlterOS.Conversation
{
    public static class TopicSimilarityDiagnostics
    {
        public static string Format(TopicCandidateSet candidates, bool onlineEnabled, float authoredGate)
        {
            int count = candidates?.cards?.Count ?? 0;
            bool confidentReadyMatch = false;
            if (candidates?.cards != null)
            {
                foreach (TopicCandidateCard card in candidates.cards)
                {
                    if (card != null && card.authoredAvailability == "ready" && card.similarity >= authoredGate)
                    {
                        confidentReadyMatch = true;
                        break;
                    }
                }
            }

            string decision = !onlineEnabled ? "LOCAL_TOPIC" : confidentReadyMatch ? "AUTHORED" : "RAG";
            var report = new StringBuilder("[AlterOS Topic Similarity] mode=")
                .Append(onlineEnabled ? "online" : "offline")
                .Append(" decision=").Append(decision)
                .Append(" authoredGate=").Append(authoredGate.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" candidates=").Append(count);

            if (count == 0)
                return report.AppendLine().Append("  no ranked Topic candidates").ToString();

            int displayed = 0;
            foreach (TopicCandidateCard card in candidates.cards)
            {
                if (card == null || displayed == TopicCandidateRetriever.MaximumCandidates) continue;
                report.AppendLine().Append("  ").Append(card.topicId)
                    .Append(" similarity=").Append(card.similarity.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(" availability=").Append(card.authoredAvailability)
                    .Append(" providerCandidate=")
                    .Append(!onlineEnabled ? "n/a" : "yes");
                displayed++;
            }

            return report.ToString();
        }
    }
}
