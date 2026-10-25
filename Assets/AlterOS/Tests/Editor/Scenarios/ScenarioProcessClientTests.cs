using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Memory;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioProcessClientTests
    {
        [Test]
        public void OwnedHelperKeepsOneTurnUntilBothStagesAndTerminalReceiptArrive()
        {
            using (CompanionProcessClient client = CreateClient("stages"))
            {
                ScenarioReadyV4 ready = client.StartScenarioAsync("player-test", Path.GetTempPath(), true,
                    CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsTrue(ready.scenarioTurnAvailable);
                ScenarioTurnJobV4 job = TurnJob("turn-stdio-1");
                var stageOrder = new List<string>();
                using (var firstStage = new ManualResetEventSlim())
                {
                    Task<ScenarioTurnReceipt> pending = client.SendScenarioTurnAsync("turn-stdio-1", job, stage =>
                    {
                        lock (stageOrder) stageOrder.Add(stage.stage);
                        firstStage.Set();
                    }, CancellationToken.None);
                    Assert.IsTrue(firstStage.Wait(TimeSpan.FromSeconds(2)));
                    Assert.IsFalse(pending.IsCompleted, "A stage must not release the in-flight turn.");
                    Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await client.SendScenarioTurnAsync("turn-stdio-2", TurnJob("turn-stdio-2"), _ => { }, CancellationToken.None));
                    ScenarioTurnReceipt receipt = pending.GetAwaiter().GetResult();
                    Assert.AreEqual("completed", receipt.terminalReason);
                    Assert.AreEqual("ready", receipt.head.status);
                    Assert.AreEqual("ready", receipt.main.status);
                    CollectionAssert.AreEqual(new[] { "head", "main" }, stageOrder);
                }
            }
        }

        [Test]
        public void CancellingScenarioTurnStopsTheOwnedStagePumpWithoutResending()
        {
            string marker = Path.Combine(Path.GetTempPath(), "alteros scenario cancel " + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (CompanionProcessClient client = CreateClient("hold", "\"" + marker + "\""))
                {
                    client.StartScenarioAsync("player-test", Path.GetTempPath(), true, CancellationToken.None).GetAwaiter().GetResult();
                    using (var cancellation = new CancellationTokenSource())
                    {
                        Task<ScenarioTurnReceipt> pending = client.SendScenarioTurnAsync("turn-stdio-1", TurnJob("turn-stdio-1"),
                            _ => { }, cancellation.Token);
                        cancellation.Cancel();
                        Assert.ThrowsAsync<TaskCanceledException>(async () => await pending);
                    }
                    Assert.IsTrue(SpinWait.SpinUntil(() => FileContains(marker, "turn-stdio-1"), TimeSpan.FromSeconds(2)));
                }
            }
            finally { if (File.Exists(marker)) File.Delete(marker); }
        }

        [Test]
        public void OwnedHelperRejectsWrongParentVersionAndDuplicateStageSafely()
        {
            foreach (string mode in new[] { "bad-parent", "wrong-version", "duplicate-stage", "oversized-stage" })
            {
                using (CompanionProcessClient client = CreateClient(mode))
                {
                    client.StartScenarioAsync("player-test", Path.GetTempPath(), true, CancellationToken.None).GetAwaiter().GetResult();
                    Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendScenarioTurnAsync(
                        "turn-stdio-1", TurnJob("turn-stdio-1"), _ => { }, CancellationToken.None));
                }
            }
        }

        [Test]
        public void HelperWithoutScenarioCapabilityCannotStartScenarioTurn()
        {
            using (CompanionProcessClient client = CreateClient("missing-capability"))
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.StartScenarioAsync(
                    "player-test", Path.GetTempPath(), true, CancellationToken.None));
        }

        [Test]
        public void ScenarioHelperKeepsLocalMemoryCommandsAvailableOnItsOwnedProcess()
        {
            using (CompanionProcessClient client = CreateClient("memory-commands"))
            {
                client.StartScenarioAsync("player-test", Path.GetTempPath(), false, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var memory = new PlayerMemoryClient(client, "player-test", Path.GetTempPath());
                PlayerMemoryResultV3 consent = memory.GetConsentAsync(0, DateTimeOffset.UtcNow,
                    CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual("accepted", consent.status);
                Assert.IsNotNull(consent.consent);
                Assert.IsFalse(consent.consent.enabled);
            }
        }

        [Test]
        public void ShutdownDuringScenarioTurnFailsPendingRequestAndStopsOwnedChild()
        {
            using (CompanionProcessClient client = CreateClient("hold"))
            {
                client.StartScenarioAsync("player-test", Path.GetTempPath(), true, CancellationToken.None).GetAwaiter().GetResult();
                Task<ScenarioTurnReceipt> pending = client.SendScenarioTurnAsync("turn-stdio-1", TurnJob("turn-stdio-1"),
                    _ => { }, CancellationToken.None);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await client.StopAsync();
                    await pending;
                });
            }
        }

        [Test]
        public void ScenarioTurnTimeoutCancelsTheHelperRequestWithoutResending()
        {
            string marker = Path.Combine(Path.GetTempPath(), "alteros scenario timeout " + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (CompanionProcessClient client = CreateClient("hold", "\"" + marker + "\"", TimeSpan.FromMilliseconds(100)))
                {
                    client.StartScenarioAsync("player-test", Path.GetTempPath(), true, CancellationToken.None).GetAwaiter().GetResult();
                    Assert.ThrowsAsync<TimeoutException>(async () => await client.SendScenarioTurnAsync(
                        "turn-stdio-1", TurnJob("turn-stdio-1"), _ => { }, CancellationToken.None));
                    Assert.IsTrue(SpinWait.SpinUntil(() => FileContains(marker, "turn-stdio-1"), TimeSpan.FromSeconds(2)));
                }
            }
            finally { if (File.Exists(marker)) File.Delete(marker); }
        }

        private static CompanionProcessClient CreateClient(string mode, string extraArguments = "", TimeSpan? responseTimeout = null)
        {
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_scenario_sidecar.py"));
            Assert.IsTrue(File.Exists(python), "Repo-local test Python is required.");
            Assert.IsTrue(File.Exists(helper), "Fake Scenario sidecar fixture is required.");
            string arguments = "\"" + helper + "\" " + mode + " " + extraArguments;
            return responseTimeout.HasValue
                ? new CompanionProcessClient(python, arguments, () => "synthetic-scenario-process-key", TimeSpan.FromSeconds(3), responseTimeout.Value)
                : new CompanionProcessClient(python, arguments, () => "synthetic-scenario-process-key", TimeSpan.FromSeconds(3));
        }

        private static ScenarioTurnJobV4 TurnJob(string turnId)
        {
            ConversationJobV2 conversation = (ConversationJobV2)ConversationProtocol.ParseEnvelope(
                File.ReadAllText(Path.Combine("Tools", "Conversation", "Fixtures", "v2", "turn-valid.json"))).payload;
            string conversationEnvelope = ConversationProtocol.SerializeTurnEnvelope(turnId, conversation);
            int payloadStart = conversationEnvelope.IndexOf("\"payload\":", StringComparison.Ordinal) + "\"payload\":".Length;
            string conversationPayload = conversationEnvelope.Substring(payloadStart, conversationEnvelope.Length - payloadStart - 1);
            string runtime = "{\"scenarioId\":\"\",\"runId\":\"run-process-1\",\"turnId\":\"" + turnId + "\",\"contextRevision\":1," +
                "\"checkpointStepId\":\"\",\"knownTopicIds\":[\"cats\"],\"knownIntentIds\":[]," +
                "\"allowedExpressions\":[\"gentle\",\"thinking\",\"happy\"],\"capabilities\":[],\"objects\":[],\"waitConditions\":[]}";
            string wire = "{\"protocolVersion\":4,\"type\":\"scenario_turn\",\"requestId\":\"" + turnId + "\",\"payload\":{" +
                "\"turnId\":\"" + turnId + "\",\"turnVersion\":1,\"operation\":\"begin\",\"onlineEnabled\":true," +
                "\"conversation\":" + conversationPayload + ",\"memory\":null,\"runtime\":" + runtime + ",\"answer\":null}}";
            return (ScenarioTurnJobV4)ScenarioTurnProtocol.ParseEnvelope(wire).payload;
        }

        private static bool FileContains(string path, string expected)
        {
            try { return File.ReadAllText(path) == expected; }
            catch (IOException) { return false; }
        }
    }
}
