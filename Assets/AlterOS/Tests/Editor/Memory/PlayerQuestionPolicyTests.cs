using System;
using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Memory;

namespace AlterOS.Tests
{
    public sealed class PlayerQuestionPolicyTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");

        private static PlayerQuestionPolicyContext Context(IReadOnlyList<PlayerQuestionHistoryEntry> history = null,
            ISet<string> known = null, ISet<string> disabled = null, bool? focus = true, bool? quiet = false,
            bool memory = true, bool completed = true, string session = "session-1")
        {
            return new PlayerQuestionPolicyContext(Now, session, memory, focus, quiet, completed,
                known ?? new HashSet<string>(), disabled ?? new HashSet<string>(),
                history ?? new PlayerQuestionHistoryEntry[0]);
        }

        [Test]
        public void EligibleExchangeCanProposeOneMissingAllowlistedIntentAtFixedProbability()
        {
            string selected = PlayerQuestionPolicy.Evaluate(Context(), 0.14d);
            Assert.AreEqual("preferred_name", selected);
            Assert.AreEqual("", PlayerQuestionPolicy.Evaluate(Context(), 0.15d));
        }

        [Test]
        public void UnknownFocusQuietStateAndIncompleteExchangeBlockProactiveQuestions()
        {
            Assert.AreEqual("", PlayerQuestionPolicy.Evaluate(Context(focus: null), 0.01d));
            Assert.AreEqual("", PlayerQuestionPolicy.Evaluate(Context(quiet: null), 0.01d));
            Assert.AreEqual("", PlayerQuestionPolicy.Evaluate(Context(completed: false), 0.01d));
            Assert.AreEqual("", PlayerQuestionPolicy.Evaluate(Context(memory: false), 0.01d));
        }

        [Test]
        public void KnownDisabledAndRecentlyAskedIntentsAreSkippedWithoutMutatingHistory()
        {
            var history = new[] { new PlayerQuestionHistoryEntry("preferred_name", "prior-session", Now.AddDays(-2)) };
            var known = new HashSet<string> { "preferred_name" };
            var disabled = new HashSet<string> { "interest" };
            string result = PlayerQuestionPolicy.Evaluate(Context(history, known, disabled), 0.01d);
            Assert.AreEqual("shared_activity_preference", result);
            Assert.AreEqual(1, history.Length);
        }

        [Test]
        public void SameSessionAndSevenDayCooldownPreventRepeatedPersonalQuestions()
        {
            var sameSession = new[] { new PlayerQuestionHistoryEntry("interest", "session-1", Now.AddDays(-10)) };
            var cooldown = new[] { new PlayerQuestionHistoryEntry("preferred_name", "prior-session", Now.AddDays(-6)) };
            Assert.AreEqual("", PlayerQuestionPolicy.Evaluate(Context(sameSession), 0.01d));
            Assert.AreEqual("interest", PlayerQuestionPolicy.Evaluate(Context(cooldown), 0.01d));
        }
    }
}
