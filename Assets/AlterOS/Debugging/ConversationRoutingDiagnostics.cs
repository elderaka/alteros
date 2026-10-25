using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using AlterOS.Semantics;

namespace AlterOS.Debugging
{
    /// <summary>Bounded, transient observation only; never participates in routing or persistence.</summary>
    public sealed class ConversationRoutingDiagnostics
    {
        private readonly Stopwatch elapsed = new Stopwatch();
        private string candidates = "No query yet.";
        public string LastInput { get; private set; } = "none";
        public string Selected { get; private set; } = "none";
        public string LastEvent { get; private set; } = "waiting";
        public string LastFailure { get; private set; } = "none";
        public string Report { get; private set; } = "ROUTING DEBUG\nNo input yet.";
        public int Turn { get; private set; }

        public void Begin(string input)
        {
            Turn++;
            elapsed.Restart();
            LastInput = SingleLine(input, 200);
            Selected = "none";
            LastEvent = "submitted";
            LastFailure = "none";
            candidates = "Waiting for routing...";
            Refresh();
        }

        public void SetCandidates(DialogueRouteMatch[] matches, string explicitTopicId)
        {
            if (!string.IsNullOrWhiteSpace(explicitTopicId))
                candidates = "Explicit selection - GTE bypassed\nTopic: " + SingleLine(explicitTopicId, 128) +
                    "\nAnchor: n/a | similarity: n/a";
            else
            {
                var text = new StringBuilder("TOP 5 - routing similarity, NOT probability");
                int rank = 0;
                foreach (DialogueRouteMatch match in (matches ?? new DialogueRouteMatch[0]).Take(5))
                {
                    float similarity = match.Score;
                    string percent = float.IsNaN(similarity) || float.IsInfinity(similarity) ? "invalid" :
                        (Math.Max(-1f, Math.Min(1f, similarity)) * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    text.Append('\n').Append(++rank).Append(". ").Append(SingleLine(match.RouteId, 128))
                        .Append(" | ").Append(percent).Append("\n   Anchor: ").Append(SingleLine(match.BestAnchor, 110));
                }
                if (rank == 0) text.Append("\nNo semantic candidates.");
                candidates = text.ToString();
            }
            Refresh();
        }

        public void Select(string topicId, string scenarioId, string outcome)
        {
            Selected = SingleLine(topicId + " / " + scenarioId + " / " + outcome, 220);
            Refresh();
        }

        public string Record(string stage, string detail)
        {
            LastEvent = "#" + Turn + " +" + elapsed.ElapsedMilliseconds + "ms " +
                SingleLine(stage, 60) + " | " + SingleLine(detail, 240);
            if ((stage ?? "").IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (detail ?? "").IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (detail ?? "").IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (detail ?? "").IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0)
                LastFailure = LastEvent;
            Refresh();
            return LastEvent;
        }

        private void Refresh()
        {
            Report = "ROUTING DEBUG [F5]\nInput: " + LastInput + "\nSelected: " + Selected +
                "\n" + candidates + "\nLast: " + LastEvent + "\nFailure: " + LastFailure;
        }

        private static string SingleLine(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            string result = new string(value.Take(maximum).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            return value.Length > maximum ? result + "..." : result;
        }
    }
}
