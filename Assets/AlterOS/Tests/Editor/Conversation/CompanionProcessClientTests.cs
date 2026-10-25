using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using AlterOS.Conversation;

namespace AlterOS.Tests
{
    public sealed class CompanionProcessClientTests
    {
        [Test]
        public void ManagedChildUsesValidatedProtocolAndStopsThroughOwnedProcess()
        {
            using (CompanionProcessClient client = CreateClient("stderr-secret"))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                ConversationProtocolMessage result = client.SendTurnAsync("request-v2-1", job, CancellationToken.None).GetAwaiter().GetResult();
                Assert.AreEqual("result", result.type);
                Assert.AreEqual("beat-1", ((ConversationResultV2)result.payload).judgment.reply.beats[0].id);
                client.StopAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void ManagedChildRejectsInvalidReadyOrLine()
        {
            foreach (string mode in new[] { "wrong-version", "overlong", "incomplete" })
                using (CompanionProcessClient client = CreateClient(mode))
                    Assert.ThrowsAsync<InvalidOperationException>(async () => await client.StartAsync(CancellationToken.None));
        }

        [Test]
        public void StartupSurfacesTheStablePersonaSetupError()
        {
            using (CompanionProcessClient client = CreateClient("persona-unavailable"))
            {
                InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.StartAsync(CancellationToken.None));
                Assert.AreEqual("persona_unavailable", error.Message);
            }
        }

        [Test]
        public void ChildExitDuringTurnFailsWithoutAutomaticResend()
        {
            using (CompanionProcessClient client = CreateClient("exit-mid-turn"))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendTurnAsync("request-v2-1", job, CancellationToken.None));
            }
        }

        [Test]
        public void ManagedChildRejectsDuplicateResponseBeforeTheNextTurnCanUseIt()
        {
            using (CompanionProcessClient client = CreateClient("duplicate-result"))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                Assert.AreEqual("result", client.SendTurnAsync("request-v2-1", job, CancellationToken.None).GetAwaiter().GetResult().type);
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendTurnAsync("request-v2-2", job, CancellationToken.None));
            }
        }

        [Test]
        public void CancellationSendsLocalCancelAndDoesNotResendTheTurn()
        {
            using (CompanionProcessClient client = CreateClient("hold"))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                Task<ConversationProtocolMessage> pending = client.SendTurnAsync("request-v2-1", job, CancellationToken.None);
                client.CancelAsync("request-v2-1").GetAwaiter().GetResult();
                Assert.ThrowsAsync<TaskCanceledException>(async () => await pending);
            }
        }

        [Test]
        public void FailedHelperCanBeStartedAgainWithoutReplayingItsFirstTurn()
        {
            string marker = Path.Combine(Path.GetTempPath(), "alteros helper test " + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (CompanionProcessClient client = CreateClient("restart-once", "\"" + marker + "\""))
                {
                    Assert.ThrowsAsync<InvalidOperationException>(async () => await client.StartAsync(CancellationToken.None));
                    client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                    client.StopAsync().GetAwaiter().GetResult();
                }
            }
            finally { if (File.Exists(marker)) File.Delete(marker); }
        }

        [Test]
        public void ChildClosingInputAfterReadyFailsTheTurnInsteadOfHanging()
        {
            using (CompanionProcessClient client = CreateClient("stdin-closed"))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendTurnAsync("request-v2-1", job, CancellationToken.None));
            }
        }

        [Test]
        public void CredentialRemovalStopsTheHelperAndDoesNotRestartOrResend()
        {
            string credential = "process-test-secret-marker";
            using (var client = new CompanionProcessClient(PythonPath(), HelperArguments("stderr-secret"), () => credential, TimeSpan.FromSeconds(3)))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                credential = "";
                client.RefreshCredentialAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendTurnAsync("request-v2-1", job, CancellationToken.None));
            }
        }

        [Test]
        public void SavingAReplacementCredentialDuringATurnWaitsForTheActiveTurnToEnd()
        {
            string credential = "first-process-secret-marker";
            using (var client = new CompanionProcessClient(PythonPath(), HelperArguments("hold"), () => credential, TimeSpan.FromSeconds(3)))
            {
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConversationJobV2 job = (ConversationJobV2)ConversationProtocol.ParseEnvelope(Fixture("turn-valid.json")).payload;
                Task<ConversationProtocolMessage> pending = client.SendTurnAsync("request-v2-1", job, CancellationToken.None);
                credential = "replacement-process-secret-marker";
                client.RefreshCredentialAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsFalse(pending.IsCompleted);
                client.CancelAsync("request-v2-1").GetAwaiter().GetResult();
                Assert.ThrowsAsync<TaskCanceledException>(async () => await pending);
            }
        }

        private static CompanionProcessClient CreateClient(string mode, string extraArguments = "")
        {
            string python = PythonPath();
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            Assert.IsTrue(File.Exists(python), "Repo-local test Python is required.");
            Assert.IsTrue(File.Exists(helper), "Fake sidecar fixture is required.");
            return new CompanionProcessClient(python, "\"" + helper + "\" " + mode + " " + extraArguments, () => "process-test-secret-marker", TimeSpan.FromSeconds(3));
        }

        private static string PythonPath()
        { return Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe")); }

        private static string HelperArguments(string mode)
        { return "\"" + Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py")) + "\" " + mode; }

        private static string Fixture(string name)
        { return File.ReadAllText(Path.Combine("Tools", "Conversation", "Fixtures", "v2", name)); }
    }
}
