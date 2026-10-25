using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Memory;

namespace AlterOS.Tests
{
    public sealed class PlayerOnboardingSessionTests
    {
        [Test]
        public void NewPlayerStartsWithConsentAndNoConsentSkipsProfileQuestions()
        {
            var session = new PlayerOnboardingSession("", false, false, false, null);
            Assert.AreEqual("consent", session.CurrentStepId);
            Assert.IsTrue(session.AcknowledgeConsentChoice(false, true));
            Assert.AreEqual("complete", session.CurrentStepId);
            Assert.IsTrue(session.IsComplete);
        }

        [Test]
        public void OnlyDurablyAcknowledgedAnswersAdvanceAndPreserveUnicodeName()
        {
            var session = new PlayerOnboardingSession("", false, true, true, null);
            Assert.AreEqual("preferred_name", session.CurrentStepId);
            Assert.IsFalse(session.AcknowledgeAnswer("  Aki 🌸  ", false));
            Assert.AreEqual("preferred_name", session.CurrentStepId);
            Assert.IsTrue(session.AcknowledgeAnswer("  Aki 🌸  ", true));
            Assert.AreEqual("Aki 🌸", session.PreferredName);
            Assert.AreEqual("interest", session.CurrentStepId);
        }

        [Test]
        public void SavedProgressResumesAtPendingStepAndSkipsKnownFields()
        {
            var known = new HashSet<string> { "preferred_name" };
            var resumed = new PlayerOnboardingSession("interest", false, true, true, known);
            Assert.AreEqual("interest", resumed.CurrentStepId);
            Assert.IsTrue(resumed.AcknowledgeAnswer("cozy games", true));
            Assert.AreEqual("shared_activity_preference", resumed.CurrentStepId);

            var savedBeforeProgress = new PlayerOnboardingSession("preferred_name", false, true, true, known);
            Assert.AreEqual("interest", savedBeforeProgress.CurrentStepId);
        }

        [Test]
        public void RevokedConsentCannotResumeAnUnfinishedProfileQuestion()
        {
            var resumed = new PlayerOnboardingSession("interest", false, true, false,
                new HashSet<string> { "preferred_name" });
            Assert.IsTrue(resumed.IsComplete);
            Assert.AreEqual("complete", resumed.CurrentStepId);
        }

        [Test]
        public void EveryPromptCanBeSkippedAndCompletionDoesNotReplay()
        {
            var session = new PlayerOnboardingSession("", false, true, true, null);
            Assert.IsTrue(session.AcknowledgeSkip(true));
            Assert.AreEqual("interest", session.CurrentStepId);
            Assert.IsTrue(session.AcknowledgeSkip(true));
            Assert.AreEqual("shared_activity_preference", session.CurrentStepId);
            Assert.IsTrue(session.AcknowledgeSkip(true));
            Assert.AreEqual("complete", session.CurrentStepId);
            Assert.IsTrue(session.AcknowledgeComplete(true));

            var reloaded = new PlayerOnboardingSession("complete", true, true, true, null);
            Assert.IsTrue(reloaded.IsComplete);
        }

        [Test]
        public void InvalidOrOversizedProfileAnswerDoesNotAdvance()
        {
            var session = new PlayerOnboardingSession("", false, true, true, null);
            Assert.IsFalse(session.AcknowledgeAnswer("   ", true));
            Assert.IsFalse(session.AcknowledgeAnswer(new string('x', 81), true));
            Assert.AreEqual("preferred_name", session.CurrentStepId);
        }

        [Test]
        public void OnboardingCursorCanBeReadThroughTheKeylessMemoryProtocol()
        {
            string wire = PlayerMemoryProtocol.SerializeOnboardingReadEnvelope("read-onboarding", "player-1", 0,
                System.DateTimeOffset.Parse("2026-10-18T12:00:00Z"));
            PlayerMemoryCommandV3 command = PlayerMemoryProtocol.ParseEnvelope(wire).payload as PlayerMemoryCommandV3;
            Assert.IsNotNull(command);
            Assert.AreEqual("onboarding_read", command.operation);
        }

        [Test]
        public void OwnedKeylessHelperReturnsSavedOnboardingCursorWithoutReadingCredential()
        {
            string python = System.IO.Path.GetFullPath(System.IO.Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = System.IO.Path.GetFullPath(System.IO.Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            bool credentialRead = false;
            using (var process = new AlterOS.Conversation.CompanionProcessClient(python, "\"" + helper + "\" v3-keyless",
                () => { credentialRead = true; return "must-not-be-read"; }, System.TimeSpan.FromSeconds(3)))
            {
                var client = new PlayerMemoryClient(process, "player-1", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AlterOS-onboarding-read"));
                client.StartAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                PlayerMemoryResultV3 progress = client.GetOnboardingProgressAsync(0,
                    System.DateTimeOffset.Parse("2026-10-18T12:00:00Z"), System.Threading.CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual("interest", progress.stepId);
                Assert.IsFalse(progress.completed.Value);
                Assert.IsFalse(credentialRead);
            }
        }
    }
}
