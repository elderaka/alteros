using System;
using System.Collections.Generic;

namespace AlterOS.Memory
{
    public sealed class PlayerQuestionHistoryEntry
    {
        public readonly string intent, sessionId;
        public readonly DateTimeOffset presentedAtUtc;
        public PlayerQuestionHistoryEntry(string questionIntent, string session, DateTimeOffset presentedAt)
        { intent = questionIntent ?? ""; sessionId = session ?? ""; presentedAtUtc = presentedAt.ToUniversalTime(); }
    }

    public sealed class PlayerQuestionPolicyContext
    {
        public readonly DateTimeOffset nowUtc;
        public readonly string sessionId;
        public readonly bool memoryEnabled, exchangeCompleted;
        public readonly bool? focusAllowed, quiet;
        public readonly ISet<string> knownIntents, disabledIntents;
        public readonly IReadOnlyList<PlayerQuestionHistoryEntry> history;

        public PlayerQuestionPolicyContext(DateTimeOffset now, string session, bool hasConsent,
            bool? focus, bool? isQuiet, bool completedExchange, ISet<string> known,
            ISet<string> disabled, IReadOnlyList<PlayerQuestionHistoryEntry> questionHistory)
        {
            nowUtc = now.ToUniversalTime(); sessionId = session ?? ""; memoryEnabled = hasConsent;
            focusAllowed = focus; quiet = isQuiet; exchangeCompleted = completedExchange;
            knownIntents = new HashSet<string>(known ?? new HashSet<string>(), StringComparer.Ordinal);
            disabledIntents = new HashSet<string>(disabled ?? new HashSet<string>(), StringComparer.Ordinal);
            history = questionHistory ?? new PlayerQuestionHistoryEntry[0];
        }
    }

    /// <summary>Pure gate for optional profile questions; selection never acknowledges presentation.</summary>
    public static class PlayerQuestionPolicy
    {
        private static readonly string[] IntentOrder = { "preferred_name", "interest", "shared_activity_preference" };
        private static readonly TimeSpan Cooldown = TimeSpan.FromDays(7);
        private const double EligibleProbability = 0.15d;

        public static string Evaluate(PlayerQuestionPolicyContext context, double draw)
        {
            if (context == null || !context.memoryEnabled || context.focusAllowed != true || context.quiet != false ||
                !context.exchangeCompleted || string.IsNullOrWhiteSpace(context.sessionId) ||
                double.IsNaN(draw) || double.IsInfinity(draw) || draw < 0d || draw >= EligibleProbability)
                return string.Empty;
            foreach (PlayerQuestionHistoryEntry entry in context.history)
                if (entry != null && entry.sessionId == context.sessionId) return string.Empty;
            foreach (string intent in IntentOrder)
            {
                if (context.knownIntents.Contains(intent) || context.disabledIntents.Contains(intent) || IsCoolingDown(context, intent)) continue;
                return intent;
            }
            return string.Empty;
        }

        private static bool IsCoolingDown(PlayerQuestionPolicyContext context, string intent)
        {
            foreach (PlayerQuestionHistoryEntry entry in context.history)
            {
                if (entry == null || entry.intent != intent) continue;
                TimeSpan elapsed = context.nowUtc - entry.presentedAtUtc;
                if (elapsed < Cooldown) return true;
            }
            return false;
        }
    }
}
