using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Memory;

namespace AlterOS.Tests
{
    public sealed class PlayerMemoryProtocolTests
    {
        [Test]
        public void MemoryBootstrapSupportsAnEmptyKeyAndHostOwnedDatabasePath()
        {
            string root = Path.Combine(Path.GetTempPath(), "MemoryProtocolFixture", "Memory", "player-memory.sqlite3");
            string wire = PlayerMemoryProtocol.SerializeBootstrapEnvelope("boot-memory", "", "player-1", root);
            ConversationProtocolMessage parsed = ConversationProtocol.ParseEnvelope(wire);
            PlayerMemoryBootstrapV3 bootstrap = parsed.payload as PlayerMemoryBootstrapV3;

            Assert.AreEqual(3, parsed.protocolVersion);
            Assert.AreEqual("bootstrap", parsed.type);
            Assert.IsNotNull(bootstrap);
            Assert.AreEqual("", bootstrap.apiKey);
            Assert.AreEqual("player-1", bootstrap.playerId);
            Assert.AreEqual(Path.GetFullPath(root), bootstrap.memoryRoot);
        }

        [Test]
        public void MemoryCommandKeepsPathAndCredentialsOutOfItsPayload()
        {
            var evidence = new PlayerMemoryEvidence
            {
                playerId = "player-1", sessionId = "session-1", sourceId = "source-1", actor = "player", scope = "real_world_claim",
                sourceKind = "player_message", text = "I like green tea", eventAtUtc = DateTimeOffset.Parse("2026-10-18T11:58:00Z"),
                explicitlyConfirmed = false
            };
            var proposal = new PlayerMemoryProposal
            {
                category = "interest", key = "beverage.tea", value = "green tea", scope = "real_world_claim",
                sourceId = "source-1", evidenceQuote = "I like green tea"
            };
            string wire = PlayerMemoryProtocol.SerializeProfileSetEnvelope("profile-1", "player-1", 1,
                DateTimeOffset.Parse("2026-10-18T12:00:00Z"), proposal, evidence);
            ConversationProtocolMessage parsed = ConversationProtocol.ParseEnvelope(wire);
            PlayerMemoryCommandV3 command = parsed.payload as PlayerMemoryCommandV3;

            Assert.IsNotNull(command);
            Assert.AreEqual("profile_set", command.operation);
            Assert.AreEqual("player-1", command.playerId);
            Assert.AreEqual(1, command.consentRevision);
            PlayerMemoryProfileSetDataV3 data = command.data as PlayerMemoryProfileSetDataV3;
            Assert.IsNotNull(data);
            Assert.AreEqual("beverage.tea", data.proposal.key);
            Assert.AreEqual("I like green tea", data.evidence.text);
            Assert.AreEqual("session-1", data.evidence.sessionId);
            Assert.IsFalse(wire.Contains("memoryRoot"));
            Assert.IsFalse(wire.Contains("apiKey"));

            string readConsent = PlayerMemoryProtocol.SerializeConsentReadEnvelope("consent-read", "player-1", 1,
                DateTimeOffset.Parse("2026-10-18T12:00:00Z"));
            PlayerMemoryConsentDataV3 read = (PlayerMemoryConsentDataV3)((PlayerMemoryCommandV3)
                PlayerMemoryProtocol.ParseEnvelope(readConsent).payload).data;
            Assert.IsNull(read.enabled);
        }

        [Test]
        public void SourceSyncAllowsSequenceGapsForNonContentRows()
        {
            var evidence = new PlayerMemoryEvidence
            {
                playerId = "player-1", sessionId = "session-1", sourceId = "node-3", actor = "player",
                scope = "real_world_claim", sourceKind = "player_message", text = "I like tea"
            };
            string wire = PlayerMemoryProtocol.SerializeSourceSyncEnvelope("sync-1", "player-1", 1,
                DateTimeOffset.Parse("2026-10-18T12:00:00Z"), 0, 3,
                new[] { new PlayerMemorySourceSyncEvent(3, evidence) });
            PlayerMemoryCommandV3 command = PlayerMemoryProtocol.ParseEnvelope(wire).payload as PlayerMemoryCommandV3;
            Assert.IsNotNull(command);
            Assert.AreEqual("source_sync", command.operation);
        }

        [Test]
        public void MemoryTurnEnvelopeKeepsV2ConversationShapeAndCarriesSeparateSourceIds()
        {
            string fixture = File.ReadAllText(Path.Combine("Tools", "Conversation", "Fixtures", "v2", "turn-valid.json"));
            ConversationJobV2 job = ConversationProtocol.ParseEnvelope(fixture).payload as ConversationJobV2;
            Assert.IsNotNull(job);
            var policy = new PlayerMemoryTurnPolicyV3("player-1", 2, DateTimeOffset.Parse("2026-10-18T12:00:00Z"),
                "request-current", null, null, false, "", true, new[] { "history-source" });

            string wire = PlayerMemoryProtocol.SerializeMemoryTurnEnvelope("memory-turn", job, policy);
            ConversationProtocolMessage parsed = ConversationProtocol.ParseEnvelope(wire);
            PlayerMemoryTurnV3 turn = parsed.payload as PlayerMemoryTurnV3;

            Assert.AreEqual(3, parsed.protocolVersion);
            Assert.AreEqual("turn", parsed.type);
            Assert.IsNotNull(turn);
            Assert.AreEqual(job.request.text, turn.conversation.request.text);
            Assert.AreEqual("request-current", turn.memory.currentPlayerSourceId);
            Assert.AreEqual("history-source", turn.memory.presentedHistorySourceIds[0]);
            Assert.IsFalse(wire.Contains("\"sourceId\":\"history-source\""));
        }

        [Test]
        public void MemoryTurnRejectsMisalignedSourceMetadata()
        {
            string fixture = File.ReadAllText(Path.Combine("Tools", "Conversation", "Fixtures", "v2", "turn-valid.json"));
            ConversationJobV2 job = ConversationProtocol.ParseEnvelope(fixture).payload as ConversationJobV2;
            var policy = new PlayerMemoryTurnPolicyV3("player-1", 2, DateTimeOffset.Parse("2026-10-18T12:00:00Z"),
                "request-current", null, null, false, "", true, new string[0]);

            Assert.Throws<FormatException>(() => PlayerMemoryProtocol.SerializeMemoryTurnEnvelope("memory-turn", job, policy));
        }

        [Test]
        public void MemoryTurnResultParsesTheOriginalTypedConversationContract()
        {
            const string wire = "{\"protocolVersion\":3,\"type\":\"result\",\"requestId\":\"memory-result\",\"payload\":{\"conversation\":{" +
                "\"judgment\":{\"kind\":\"clarify\",\"topicId\":\"\",\"referencedTopicIds\":[],\"reply\":{\"beats\":[{\"id\":\"b1\",\"text\":\"そうだね。\",\"displayText\":\"I see.\",\"language\":\"japanese\",\"expression\":\"\"}]},\"suggestedReplies\":[]}," +
                "\"usage\":{\"inputTokens\":1,\"outputTokens\":1,\"totalTokens\":2,\"costUsd\":null},\"timing\":{\"providerMs\":1,\"totalMs\":2}}}}";
            ConversationProtocolMessage parsed = ConversationProtocol.ParseEnvelope(wire);

            Assert.AreEqual(3, parsed.protocolVersion);
            Assert.IsInstanceOf<ConversationResultV2>(parsed.payload);
            Assert.AreEqual("clarify", ((ConversationResultV2)parsed.payload).judgment.kind);
        }

        [Test]
        public void ExplicitConfirmationCanEstablishScopeForPreviouslyUnclassifiedChat()
        {
            var proposal = new PlayerMemoryProposal
            {
                category = "interest", key = "beverage.tea", value = "tea", scope = "real_world_claim",
                sourceId = "chat-1", evidenceQuote = "I like tea"
            };
            var evidence = new PlayerMemoryEvidence
            {
                playerId = "player-1", sessionId = "session-1", sourceId = "chat-1", actor = "player",
                scope = "unclassified", sourceKind = "player_message", text = "I like tea", eventAtUtc = DateTimeOffset.Parse("2026-10-18T11:58:00Z")
            };
            var confirmation = new PlayerMemoryEvidence
            {
                playerId = "player-1", sessionId = "session-1", sourceId = "confirmation-1", actor = "player",
                scope = "real_world_claim", sourceKind = "player_confirmation", text = "Yes, remember that", eventAtUtc = DateTimeOffset.Parse("2026-10-18T11:59:00Z"),
                explicitlyConfirmed = true
            };

            string wire = PlayerMemoryProtocol.SerializeConfirmEnvelope("confirm-1", "player-1", 1,
                DateTimeOffset.Parse("2026-10-18T12:00:00Z"), proposal, evidence, confirmation);
            PlayerMemoryCommandV3 parsed = PlayerMemoryProtocol.ParseEnvelope(wire).payload as PlayerMemoryCommandV3;

            Assert.IsNotNull(parsed);
            Assert.AreEqual("confirm", parsed.operation);
            Assert.AreEqual("unclassified", ((PlayerMemoryConfirmDataV3)parsed.data).evidence.scope);
        }

        [Test]
        public void OwnedHelperSendsMemoryAwareV3TurnAndRejectsV2Downgrade()
        {
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            string fixture = File.ReadAllText(Path.Combine("Tools", "Conversation", "Fixtures", "v2", "turn-valid.json"));
            ConversationJobV2 job = ConversationProtocol.ParseEnvelope(fixture).payload as ConversationJobV2;
            using (var process = new CompanionProcessClient(python, "\"" + helper + "\" v3-turn", () => "synthetic-key-marker", TimeSpan.FromSeconds(3)))
            {
                PlayerMemoryReadyV3 ready = process.StartMemoryAsync("player-1", Path.GetTempPath(), true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(ready.generationAvailable);
                var policy = new PlayerMemoryTurnPolicyV3("player-1", 1, DateTimeOffset.Parse("2026-10-18T12:00:00Z"),
                    "request-1:request", false, false, false, "", true, new[] { "source-1" });
                ConversationProtocolMessage reply = process.SendMemoryTurnAsync("memory-turn-1", job, policy, CancellationToken.None)
                    .GetAwaiter().GetResult();

                Assert.AreEqual(3, reply.protocolVersion);
                Assert.IsInstanceOf<ConversationResultV2>(reply.payload);
                InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await process.SendTurnAsync("unsafe-v2", job, CancellationToken.None));
                Assert.AreEqual("memory_policy_required", error.Message);
            }
        }

        [Test]
        public void SharedSyntheticFixtureMatchesTheCSharpV3Contract()
        {
            string wire = File.ReadAllText(Path.Combine("Tools", "Conversation", "Fixtures", "memory-v3", "profile-set.json"));
            ConversationProtocolMessage parsed = ConversationProtocol.ParseEnvelope(wire);
            PlayerMemoryCommandV3 command = parsed.payload as PlayerMemoryCommandV3;
            Assert.IsNotNull(command);
            Assert.AreEqual("profile_set", command.operation);
            Assert.AreEqual("fixture-player", command.playerId);
        }

        [Test]
        public void MemoryContextResultParsesTypedRecordsAndSuppressionIds()
        {
            const string wire = "{\"protocolVersion\":3,\"type\":\"result\",\"requestId\":\"context-1\",\"payload\":{\"status\":\"accepted\",\"context\":{" +
                "\"core\":[{\"memoryId\":\"m1\",\"playerId\":\"player-1\",\"category\":\"preferred_name\",\"key\":\"profile.name\",\"value\":\"Ari\",\"actor\":\"player\",\"scope\":\"real_world_claim\",\"sourceKind\":\"profile_input\",\"sourceId\":\"source-1\",\"sourceText\":\"Ari\",\"eventAtUtc\":null,\"recordedAtUtc\":\"2026-10-18T12:00:00Z\",\"validUntilUtc\":null,\"freshUntilUtc\":null,\"status\":\"active\",\"supersedesId\":null}]," +
                "\"current\":[],\"recalled\":[],\"suppressedSourceIds\":[\"forgotten-source\"]}}}";
            PlayerMemoryResultV3 result = PlayerMemoryProtocol.ParseEnvelope(wire).payload as PlayerMemoryResultV3;
            Assert.IsNotNull(result);
            Assert.AreEqual("Ari", result.context.core[0].value);
            Assert.AreEqual("forgotten-source", result.context.suppressedSourceIds[0]);
        }

        [Test]
        public void MemoryProtocolRejectsDuplicateUnknownAndWrongVersionEnvelopeFields()
        {
            Assert.Throws<FormatException>(() => PlayerMemoryProtocol.ParseEnvelope(
                "{\"protocolVersion\":3,\"protocolVersion\":3,\"type\":\"shutdown\",\"requestId\":\"x\",\"payload\":{}}"));
            Assert.Throws<FormatException>(() => PlayerMemoryProtocol.ParseEnvelope(
                "{\"protocolVersion\":3,\"type\":\"shutdown\",\"requestId\":\"x\",\"payload\":{},\"extra\":true}"));
            Assert.Throws<FormatException>(() => PlayerMemoryProtocol.ParseEnvelope(
                "{\"protocolVersion\":2,\"type\":\"shutdown\",\"requestId\":\"x\",\"payload\":{}}"));
        }

        [Test]
        public void ExistingV2ConversationProtocolRemainsSupported()
        {
            ConversationProtocolMessage parsed = ConversationProtocol.ParseEnvelope(
                ConversationProtocol.SerializeControlEnvelope("shutdown", "stop-v2"));
            Assert.AreEqual(2, parsed.protocolVersion);
            Assert.AreEqual("shutdown", parsed.type);
        }

        [Test]
        public void OwnedProcessCanBootstrapMemoryOnlyAndRunOneTypedCommandWithoutApiKey()
        {
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            Assert.IsTrue(File.Exists(python));
            Assert.IsTrue(File.Exists(helper));
            string root = Path.Combine(Path.GetTempPath(), "AlterOS-memory-client-test");
            bool credentialResolverCalled = false;
            using (var process = new CompanionProcessClient(python, "\"" + helper + "\" v3-keyless", () =>
            { credentialResolverCalled = true; return "synthetic-secret-marker"; }, TimeSpan.FromSeconds(3)))
            {
                var client = new PlayerMemoryClient(process, "player-1", root);
                PlayerMemoryReadyV3 ready = client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                PlayerMemoryResultV3 saved = client.SetConsentAsync(true, 0, DateTimeOffset.Parse("2026-10-18T12:00:00Z"),
                    CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsTrue(ready.memoryAvailable);
                Assert.IsFalse(ready.generationAvailable);
                Assert.IsFalse(credentialResolverCalled, "Keyless memory startup must not read a saved provider key.");
                Assert.AreEqual("accepted", saved.status);
                Assert.AreEqual(1, saved.consent.revision);
            }
        }

        [Test]
        public void MemoryClientDoesNotTreatAnOldV2HelperAsMemoryCapable()
        {
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            bool credentialResolverCalled = false;
            using (var process = new CompanionProcessClient(python, "\"" + helper + "\" valid", () =>
            { credentialResolverCalled = true; return "old-helper-test-key"; }, TimeSpan.FromSeconds(3)))
            {
                var client = new PlayerMemoryClient(process, "player-1", Path.GetTempPath());
                InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await client.StartAsync(CancellationToken.None));
                Assert.AreEqual("helper_not_ready", error.Message);
                Assert.IsFalse(credentialResolverCalled);
            }
        }

        [Test]
        public void CancellingMemoryRequestSendsItsV3RequestIdentityToTheOwnedHelper()
        {
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            string marker = Path.Combine(Path.GetTempPath(), "AlterOS-memory-cancel-" + Guid.NewGuid().ToString("N"));
            using (var process = new CompanionProcessClient(python, "\"" + helper + "\" v3-hold-memory \"" + marker + "\"", () => "", TimeSpan.FromSeconds(3)))
            {
                var client = new PlayerMemoryClient(process, "player-1", Path.GetTempPath());
                client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(100);
                    Assert.ThrowsAsync<TaskCanceledException>(async () => await client.SetConsentAsync(true, 0,
                        DateTimeOffset.Parse("2026-10-18T12:00:00Z"), cancellation.Token));
                }
            }
            Assert.IsTrue(File.Exists(marker), "The owned helper must receive the cancellation request.");
            File.Delete(marker);
        }
    }
}
