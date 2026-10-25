using System.Linq;
using AlterOS.Debugging;
using AlterOS.Semantics;
using NUnit.Framework;

namespace AlterOS.Tests
{
    public sealed class ConversationRoutingDiagnosticsTests
    {
        [Test]
        public void DisplaysOnlyFiveProductionRankedMatchesWithAnchorsAndPercentages()
        {
            var view = new ConversationRoutingDiagnostics();
            view.Begin("Where is the TV?");
            view.SetCandidates(Enumerable.Range(1, 6).Select(i =>
                new DialogueRouteMatch("topic." + i, 0.95f - i * 0.01f, 1f, 2, "anchor " + i)).ToArray(), null);
            StringAssert.Contains("topic.1 | 94.0%", view.Report);
            StringAssert.Contains("Anchor: anchor 5", view.Report);
            StringAssert.DoesNotContain("topic.6", view.Report);
            StringAssert.Contains("NOT probability", view.Report);
            StringAssert.Contains("Where is the TV?", view.Report);
        }

        [Test]
        public void ExplicitSelectionHasNoInventedEmbeddingPercentageOrStaleAnchors()
        {
            var view = new ConversationRoutingDiagnostics();
            view.Begin("old input");
            view.SetCandidates(new[] { new DialogueRouteMatch("old.topic", 0.8f, 0.8f, 1, "old anchor") }, null);
            view.Begin("Turn on the TV");
            view.SetCandidates(null, "topic.turn_on_tv");
            StringAssert.Contains("GTE bypassed", view.Report);
            StringAssert.Contains("similarity: n/a", view.Report);
            StringAssert.DoesNotContain("old anchor", view.Report);
            StringAssert.DoesNotContain("0.0%", view.Report);
        }

        [Test]
        public void RetainsSelectionAndFailureAfterIdleAndBoundsRawInput()
        {
            var view = new ConversationRoutingDiagnostics();
            view.Begin(new string('x', 1000));
            view.Select("topic.locate_tv", "scenario.locate_tv", "authored");
            string line = view.Record("activity_finished", "primitive_capability_unavailable");
            view.Record("status", "scenario_completed");
            Assert.LessOrEqual(view.LastInput.Length, 203);
            StringAssert.Contains("topic.locate_tv", view.Report);
            StringAssert.Contains("primitive_capability_unavailable", line);
            StringAssert.Contains("primitive_capability_unavailable", view.LastFailure);
            StringAssert.Contains("ms", line);
        }
    }
}
