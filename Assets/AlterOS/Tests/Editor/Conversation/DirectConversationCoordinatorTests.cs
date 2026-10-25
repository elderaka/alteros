using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using AlterOS.Topics;
using AlterOS.Memory;
using AlterOS.Scenarios;
using SQLite;

namespace AlterOS.Tests
{
    public sealed class DirectConversationCoordinatorTests
    {
        [Test]
        public void OfflineSubmissionUsesPreparedAuthoredSelectionAndRecordsPresentedHistoryOnly()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(false) };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), null);
            ConversationSubmitResult result = coordinator.SubmitAsync("talk about cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("authored", result.plan.outcome);
            Assert.AreEqual("authored_match", result.statusCode);
            Assert.AreEqual(1, host.presentCount);
            Assert.IsFalse(result.usedProvider);
            Assert.AreEqual(1, coordinator.ReadRecent(12, 6000).utterances.Count);
        }

        [Test]
        public void FailedAuthoredPresentationReturnsRetryableFailureAndReleasesTurn()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(false), PresentError = new InvalidOperationException("topic_entry_unavailable") };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), null);
            var result = coordinator.SubmitAsync("talk about cats", CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsNull(result.plan);
            Assert.AreEqual("topic_entry_unavailable", result.statusCode);
            Assert.IsFalse(coordinator.IsTurnInFlight);
            host.PresentError = null;
            var retry = coordinator.SubmitAsync("talk about cats", CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("authored", retry.plan.outcome);
        }

        [Test]
        public void OnlineSubmissionUsesOneProviderCallAndRevalidatesBeforePresentation()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider { ResultFactory = requestId => ClarifyResult(requestId) };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("clarify", result.plan.outcome);
            Assert.AreEqual(1, provider.sendCount);
            Assert.AreEqual(1, host.revalidateCount);
            Assert.AreEqual(1, host.presentCount);
            Assert.IsTrue(result.usedProvider);
            Assert.AreEqual(1, provider.lastJob.candidates.cards.Count);
            Assert.AreEqual("cats", provider.lastJob.candidates.cards[0].topicId);
            Assert.AreEqual("そうなんだ。", result.plan.reply.nodes[0].text);
            Assert.AreEqual("I see.", result.plan.reply.nodes[0].displayText);
            Assert.AreEqual(1, host.Snapshot.catalogue.Count);
        }

        [Test]
        public void OnlineSubmissionUsesConfidentAuthoredMatchWithoutCallingProvider()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            host.Snapshot.candidates.cards[0].similarity = TopicCandidateRetriever.MinimumOnlineAuthoredSimilarity;
            var provider = new FakeProvider { ResultFactory = requestId => ClarifyResult(requestId) };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("talk about cats", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("authored", result.plan.outcome);
            Assert.AreEqual(0, provider.sendCount);
            Assert.IsFalse(result.usedProvider);
            Assert.AreEqual(1, host.presentCount);
        }

        [Test]
        public void ExplicitTopicSelectionUsesAuthoredResolverWithoutOnlineProviderCall()
        {
            string root = NewRoot();
            ConversationTurnSnapshot snapshot = Snapshot(true);
            snapshot.request.explicitTopicId = "cats";
            var host = new FakeHost { Snapshot = snapshot };
            var provider = new FakeProvider { ResultFactory = requestId => ClarifyResult(requestId) };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("menu label", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(0, provider.sendCount);
            Assert.IsFalse(result.usedProvider);
            Assert.AreEqual("authored", result.plan.outcome);
            Assert.AreEqual("cats", result.plan.topicId);
            Assert.AreEqual(1, host.presentCount);
        }

        [Test]
        public void OnlineRagUsesV4ScenarioTurnThroughTheDirectCoordinatorAndAcknowledgesOnlyPresentedBeats()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var provider = new FakeScenarioProvider();
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("scenario", result.plan.outcome);
            Assert.AreEqual(1, provider.scenarioSendCount);
            Assert.AreEqual(0, provider.legacySendCount);
            Assert.AreEqual("begin", provider.lastScenarioJob.operation);
            ScenarioEffect visibleMain = host.scenarioEffects.FindLast(effect => effect.type == "show_dialogue" && effect.stage == "main");
            Assert.IsNotNull(visibleMain);
            Assert.AreEqual(1, coordinator.ReadRecent(12, 6000).utterances.Count);
            IReadOnlyList<ScenarioEffect> afterVisible = coordinator.AcknowledgeScenarioBeat(visibleMain.stageId, visibleMain.beatId);
            Assert.IsTrue(afterVisible.Any(effect => effect.type == "turn_complete"));
            Assert.AreEqual(2, coordinator.ReadRecent(12, 6000).utterances.Count);
            Assert.IsEmpty(coordinator.AcknowledgeScenarioBeat(visibleMain.stageId, visibleMain.beatId));
            Assert.AreEqual(2, coordinator.ReadRecent(12, 6000).utterances.Count);
        }

        [Test]
        public void ScenarioCancellationAfterProviderReturnCannotAdmitOrPresentTheTurn()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var provider = new FakeScenarioProvider();
            var receipt = new TaskCompletionSource<ScenarioTurnReceipt>();
            var sent = new ManualResetEventSlim(false);
            provider.ScenarioReceiptTask = receipt.Task;
            provider.OnScenarioSend = () => sent.Set();
            var transcript = new ConversationTranscript(Path.Combine(root, "conversations.sqlite3"));
            var coordinator = new DirectConversationCoordinator(host, transcript, provider);

            Task<ConversationSubmitResult> pending = Task.Run(() =>
                coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsTrue(sent.Wait(TimeSpan.FromSeconds(2)), "Scenario provider did not start the request.");
            coordinator.CancelTurn();
            receipt.SetResult(FakeScenarioProvider.SuccessfulReceipt(provider.lastRequestId));
            ConversationSubmitResult result = pending.GetAwaiter().GetResult();

            Assert.AreEqual("cancelled", result.statusCode);
            Assert.IsFalse(host.scenarioEffects.Any(effect => effect.type == "show_dialogue"));
            Assert.IsFalse(coordinator.ReadPage("0", 20).events.Any(item => item.value.eventType == "response_received"));
        }

        [Test]
        public void ScenarioReceiptMustBeDurableBeforeGeneratedStagesArePresented()
        {
            string root = NewRoot();
            string transcriptPath = Path.Combine(root, "conversations.sqlite3");
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var provider = new FakeScenarioProvider
            {
                OnScenarioSend = () => BlockTranscriptEvent(transcriptPath, "response_received")
            };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(transcriptPath), provider);

            ConversationSubmitResult result;
            try { result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult(); }
            finally { ClearTranscriptBlock(transcriptPath); }

            Assert.AreEqual("transcript_unsaved", result.statusCode);
            Assert.IsFalse(result.transcriptDurable);
            Assert.IsFalse(host.scenarioEffects.Any(effect => effect.type == "show_dialogue"));
        }

        [Test]
        public void ReadyScenarioStageCanBePresentedWhileTerminalReceiptIsStillPending()
        {
            string root = NewRoot();
            string transcriptPath = Path.Combine(root, "conversations.sqlite3");
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var provider = new FakeScenarioProvider { StageFixtureNames = new[] { "main-cats.json" } };
            var receipt = new TaskCompletionSource<ScenarioTurnReceipt>();
            var sent = new ManualResetEventSlim(false);
            provider.ScenarioReceiptTask = receipt.Task;
            provider.OnScenarioSend = () => sent.Set();
            var transcript = new ConversationTranscript(transcriptPath);
            host.HasDurableScenarioResponse = () => transcript.ReadPage("0", 20).events
                .Any(item => item.value.eventType == "response_received");
            var coordinator = new DirectConversationCoordinator(host, transcript, provider);

            Task<ConversationSubmitResult> pending = Task.Run(() =>
                coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsTrue(sent.Wait(TimeSpan.FromSeconds(2)), "Scenario provider did not start the request.");
            coordinator.PumpScenarioTurn(host.ScenarioClockSeconds);

            Assert.IsTrue(host.scenarioEffects.Any(effect => effect.type == "show_dialogue" && effect.stage == "main"),
                "A ready main stage should not wait for the terminal receipt or another stage.");
            Assert.IsFalse(host.PresentedScenarioWithoutDurableResponse,
                "The response receipt must be durable before any generated stage is presented.");
            Assert.IsTrue(host.HasDurableScenarioResponse());
            ScenarioEffect visibleMain = host.scenarioEffects.Last(effect => effect.type == "show_dialogue" && effect.stage == "main");
            Assert.IsTrue(coordinator.RecordScenarioBeatPresented(visibleMain.stageId, visibleMain.beatId));
            coordinator.PumpScenarioTurn(host.ScenarioClockSeconds + 6d);
            Assert.IsFalse(host.scenarioEffects.Any(effect => effect.type == "show_dialogue" && effect.stage == "fallback"),
                "A durably recorded first-visible main beat must commit the opening without advancing the beat checkpoint.");

            receipt.SetResult(FakeScenarioProvider.SuccessfulReceipt(provider.lastRequestId));
            Assert.AreEqual("ready", pending.GetAwaiter().GetResult().statusCode);
        }

        [Test]
        public void FailureAfterDismissalDoesNotRetakePresentationOrCloseTheNextInteraction()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var provider = new FakeScenarioProvider();
            var failure = new TaskCompletionSource<ScenarioTurnReceipt>();
            var sent = new ManualResetEventSlim(false);
            provider.ScenarioReceiptTask = failure.Task;
            provider.OnScenarioSend = () => sent.Set();
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            Task<ConversationSubmitResult> pending = Task.Run(() =>
                coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsTrue(sent.Wait(TimeSpan.FromSeconds(2)), "Scenario provider did not start the request.");
            coordinator.DismissPresentation();
            int effectsAtDismiss = host.scenarioEffects.Count;
            failure.SetException(new InvalidOperationException("provider_unavailable"));
            ConversationSubmitResult result = pending.GetAwaiter().GetResult();

            Assert.AreEqual("provider_unavailable", result.statusCode);
            Assert.IsTrue(result.wasLate);
            CollectionAssert.DoesNotContain(host.scenarioEffects.Skip(effectsAtDismiss).Select(effect => effect.type), "cancel_turn");
        }

        [Test]
        public void FailedScenarioBeatTranscriptWriteCannotAdvanceOrLoseItsResumeCheckpoint()
        {
            string root = NewRoot();
            string transcriptPath = Path.Combine(root, "conversations.sqlite3");
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(transcriptPath), new FakeScenarioProvider());
            ConversationSubmitResult result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();
            ScenarioEffect visibleMain = host.scenarioEffects.FindLast(effect => effect.type == "show_dialogue" && effect.stage == "main");
            Assert.IsNotNull(visibleMain);
            BlockTranscriptEvent(transcriptPath, "node_presented");
            IReadOnlyList<ScenarioEffect> effects;
            try { effects = coordinator.AcknowledgeScenarioBeat(visibleMain.stageId, visibleMain.beatId); }
            finally { ClearTranscriptBlock(transcriptPath); }

            Assert.IsFalse(effects.Any(effect => effect.type == "turn_segment_complete"));
            Assert.IsFalse(host.scenarioEffects.SkipWhile(effect => effect != visibleMain)
                .Any(effect => effect.type == "turn_segment_complete"));
            coordinator.DismissPresentation();
            ScenarioEffect resumed = host.scenarioEffects.FindLast(effect => effect.type == "late_result_saved");
            Assert.IsNotNull(resumed, "A failed write must remain eligible for an explicit resume checkpoint.");
            Assert.AreEqual(0, resumed.beatIndex);
        }

        [Test]
        public void DurableFirstVisibleScenarioBeatCanAdvanceWithoutASecondTranscriptWrite()
        {
            string root = NewRoot();
            string transcriptPath = Path.Combine(root, "conversations.sqlite3");
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(transcriptPath), new FakeScenarioProvider());
            coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();
            ScenarioEffect visibleMain = host.scenarioEffects.FindLast(effect => effect.type == "show_dialogue" && effect.stage == "main");
            Assert.IsNotNull(visibleMain);
            Assert.IsTrue(coordinator.RecordScenarioBeatPresented(visibleMain.stageId, visibleMain.beatId));
            BlockTranscriptEvent(transcriptPath, "node_presented");
            IReadOnlyList<ScenarioEffect> effects;
            bool acknowledged;
            try { acknowledged = coordinator.TryAcknowledgeScenarioBeat(visibleMain.stageId, visibleMain.beatId, out effects); }
            finally { ClearTranscriptBlock(transcriptPath); }

            Assert.IsTrue(acknowledged, "A previously durable first-visible receipt must allow playback advance without re-writing it.");
            Assert.IsTrue(effects.Any(effect => effect.type == "turn_segment_complete"));
            Assert.IsFalse(host.scenarioEffects.Any(effect => effect.type == "cancel_turn"));
        }

        [Test]
        public void FailedScenarioStartupCannotPresentTheFiveSecondFallbackLater()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var provider = new FakeScenarioProvider { StartScenarioError = new InvalidOperationException("provider_unavailable") };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();
            coordinator.PumpScenarioTurn(100d);

            Assert.AreEqual("provider_unavailable", result.statusCode);
            Assert.IsFalse(host.scenarioEffects.Any(effect => effect.type == "show_dialogue"));
        }

        [Test]
        public void ScenarioFirstVisibleDeadlineIncludesSynchronousCaptureTime()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true), AdvanceClockOnCapture = 6d };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), new FakeScenarioProvider());

            ConversationSubmitResult result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("scenario", result.plan.outcome);
            Assert.IsTrue(host.scenarioEffects.Any(effect => effect.type == "show_dialogue" && effect.stage == "fallback"),
                "The five-second first-visible fallback should already be due after six seconds of synchronous capture.");
        }

        [Test]
        public void ScenarioPlayerAnswerIsWrittenThroughTheCoordinatorTranscriptOwner()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true) };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), new FakeScenarioProvider());
            ConversationSubmitResult result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();
            string requestId = result.responseId.Substring("response-".Length);

            Assert.IsTrue(coordinator.RecordScenarioPlayerAnswer("run-" + requestId, "favorite-trait", "playful", "They're playful"));

            ConversationHistorySnapshot history = coordinator.ReadRecent(12, 6000);
            Assert.AreEqual("player", history.utterances[history.utterances.Count - 1].speaker);
            Assert.AreEqual("They're playful", history.utterances[history.utterances.Count - 1].text);
        }

        [Test]
        public void RequiredMemoryPolicyDoesNotDowngradeWhenV4CannotBePrepared()
        {
            string root = NewRoot();
            var host = new FakeScenarioHost { Snapshot = Snapshot(true, true, false) };
            var provider = new FakeScenarioProvider();
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I like cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("memory_unavailable", result.statusCode);
            Assert.AreEqual(0, provider.scenarioSendCount);
            Assert.AreEqual(0, provider.legacySendCount);
            Assert.IsEmpty(host.scenarioEffects);
        }

        [Test]
        public void OnlineAuthoredFallbackFiltersWeakCandidatesButKeepsExplicitActiveTopic()
        {
            var candidates = new TopicCandidateSet { indexIdentity = "test-index", source = "local_gte" };
            candidates.cards.Add(new TopicCandidateCard { topicId = "weak", similarity = 0.72f });
            candidates.cards.Add(new TopicCandidateCard { topicId = "at", similarity = 0.85f });
            candidates.cards.Add(new TopicCandidateCard { topicId = "above", similarity = 0.91f });
            candidates.cards.Add(new TopicCandidateCard { topicId = "selected", similarity = 0.10f });

            TopicCandidateSet fallback = TopicCandidateRetriever.FilterForAuthoredFallback(candidates, "selected");

            CollectionAssert.AreEqual(new[] { "at", "above", "selected" }, fallback.cards.ConvertAll(card => card.topicId));
        }

        [Test]
        public void DismissedPresentationRetainsSuccessfulLateReplyWithoutTakingSuccessorSurface()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider();
            var completion = new TaskCompletionSource<ConversationProtocolMessage>();
            provider.ResultTask = completion.Task;
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            Task<ConversationSubmitResult> pending = coordinator.SubmitAsync("I had a strange day", CancellationToken.None);
            coordinator.DismissPresentation();
            completion.SetResult(ClarifyResult(provider.lastRequestId));
            ConversationSubmitResult result = pending.GetAwaiter().GetResult();

            Assert.IsTrue(result.wasLate);
            Assert.AreEqual(0, host.presentCount);
            ConversationTranscriptPage events = coordinator.ReadPage("0", 20);
            Assert.IsTrue(events.Success, events.Error);
            Assert.AreEqual("response_received", events.events[events.events.Count - 1].value.eventType);
            Assert.AreEqual(1, coordinator.ReadRecent(12, 6000).utterances.Count);
        }

        [Test]
        public void DuplicateInFlightSubmitIsRejectedWithoutSecondProviderRequest()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider();
            var completion = new TaskCompletionSource<ConversationProtocolMessage>();
            provider.ResultTask = completion.Task;
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            Task<ConversationSubmitResult> first = coordinator.SubmitAsync("first", CancellationToken.None);
            ConversationSubmitResult second = coordinator.SubmitAsync("second", CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("busy", second.statusCode);
            Assert.AreEqual(1, provider.sendCount);
            coordinator.CancelTurn();
            completion.SetCanceled();
            Assert.DoesNotThrow(() => first.GetAwaiter().GetResult());
            Assert.AreEqual(1, provider.sendCount);
        }

        [Test]
        public void ExplicitTurnCancellationIsRecordedAsCancelledInsteadOfTimeout()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider { WaitForCancellation = true };
            var transcript = new ConversationTranscript(Path.Combine(root, "conversations.sqlite3"));
            var coordinator = new DirectConversationCoordinator(host, transcript, provider);

            Task<ConversationSubmitResult> pending = coordinator.SubmitAsync("I had a strange day", CancellationToken.None);
            coordinator.CancelTurn();
            ConversationSubmitResult result = pending.GetAwaiter().GetResult();

            Assert.AreEqual("cancelled", result.statusCode);
            ConversationTranscriptPage page = coordinator.ReadPage("0", 10);
            Assert.IsTrue(page.Success, page.Error);
            Assert.AreEqual("pending", page.events[1].value.statusCode);
            Assert.AreEqual("cancelled", page.events[2].value.statusCode);
        }

        [Test]
        public void ProviderTimeoutRemainsDistinctFromExplicitCancellation()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider { ResultTask = Task.FromException<ConversationProtocolMessage>(new TimeoutException()) };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("provider_timeout", result.statusCode);
            Assert.AreEqual("unavailable", result.plan.outcome);
            Assert.IsFalse(result.usedProvider);
            Assert.AreEqual(0, host.presentCount);
        }

        [Test]
        public void CancelAfterProviderSuccessButBeforePresentationSuppressesTheReply()
        {
            string root = NewRoot();
            var host = new FakeHost
            {
                Snapshot = Snapshot(true),
                RevalidateEntered = new ManualResetEventSlim(false),
                ContinueRevalidate = new ManualResetEventSlim(false)
            };
            var provider = new FakeProvider { ResultFactory = ClarifyResult };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            Task<ConversationSubmitResult> pending = Task.Run(() => coordinator.SubmitAsync("I had a strange day", CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsTrue(host.RevalidateEntered.Wait(TimeSpan.FromSeconds(2)), "Provider result did not reach host revalidation.");
            coordinator.CancelTurn();
            host.ContinueRevalidate.Set();
            ConversationSubmitResult result = pending.GetAwaiter().GetResult();

            Assert.AreEqual("late_reply_saved", result.statusCode);
            Assert.IsTrue(result.wasLate);
            Assert.AreEqual(0, host.presentCount);
        }

        [Test]
        public void MissingKeyDoesNotUseWeakAuthoredMatchInOnlineMode()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider { StartError = new InvalidOperationException("credential_missing") };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("talk about cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("unavailable", result.plan.outcome);
            Assert.AreEqual("provider_auth", result.statusCode);
            Assert.AreEqual(0, provider.sendCount);
            Assert.AreEqual(0, host.presentCount);
        }

        [Test]
        public void ProviderStartupFailureEmitsSafeCopyableDiagnostics()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true) };
            var provider = new FakeProvider
            {
                StartError = new InvalidOperationException("helper_not_ready")
            };
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("provider_unavailable", result.statusCode);
            CollectionAssert.Contains(host.diagnostics, "route=rag");
            CollectionAssert.Contains(host.diagnostics, "provider_gate=allowed");
            CollectionAssert.Contains(host.diagnostics, "helper_start=begin");
            CollectionAssert.Contains(host.diagnostics, "provider_error=helper_not_ready");
            StringAssert.DoesNotContain("secret-marker", string.Join("\n", host.diagnostics));

            var untrustedHost = new FakeHost { Snapshot = Snapshot(true) };
            var untrustedProvider = new FakeProvider
            {
                StartError = new InvalidOperationException("helper_not_ready secret-marker")
            };
            var untrustedCoordinator = new DirectConversationCoordinator(untrustedHost,
                new ConversationTranscript(Path.Combine(root, "untrusted-conversations.sqlite3")), untrustedProvider);
            untrustedCoordinator.SubmitAsync("I had a strange day", CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.Contains(untrustedHost.diagnostics, "provider_error=provider_unavailable");
            StringAssert.DoesNotContain("secret-marker", string.Join("\n", untrustedHost.diagnostics));
        }

        [Test]
        public void InvalidOnlineJobIsDistinguishedBeforeHelperStartupWithoutLoggingExceptionText()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true, characterRules: "secret-marker" + new string('x', 9000)) };
            var provider = new FakeProvider();
            var coordinator = new DirectConversationCoordinator(host,
                new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("provider_unavailable", result.statusCode);
            CollectionAssert.Contains(host.diagnostics, "job_prepare=begin");
            CollectionAssert.Contains(host.diagnostics, "job_prepare=invalid");
            CollectionAssert.DoesNotContain(host.diagnostics, "helper_start=begin");
            Assert.AreEqual(0, provider.sendCount);
            StringAssert.DoesNotContain("secret-marker", string.Join("\n", host.diagnostics));
        }

        [Test]
        public void RequiredButUnavailableMemoryPolicyNeverFallsBackToAnUnfilteredOnlineTurn()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true, true, false) };
            var provider = new FakeProvider { ResultFactory = ClarifyResult };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("memory_unavailable", result.statusCode);
            Assert.AreEqual("unavailable", result.plan.outcome);
            Assert.AreEqual(0, provider.sendCount);
            Assert.AreEqual(0, host.presentCount);
        }

        [Test]
        public void RequiredMemoryTurnSyncsTranscriptAndUsesV3ProviderBeforePresentation()
        {
            string root = NewRoot();
            string persistent = Path.Combine(root, "persistent");
            new PlayerMemoryPolicyFlag(persistent).MarkRequired();
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            var host = new FakeHost { Snapshot = Snapshot(true, true, false) };
            using (var process = new CompanionProcessClient(python, "\"" + helper + "\" v3-turn",
                () => "synthetic-key-marker", TimeSpan.FromSeconds(3)))
            {
                var memory = new PlayerMemoryClient(process, "player-1", persistent);
                var coordinator = new DirectConversationCoordinator(host,
                    new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), process, memory);

                ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual("clarify", result.plan.outcome);
                Assert.IsTrue(result.usedProvider);
                Assert.AreEqual(1, host.presentCount);
            }
        }

        [Test]
        public async Task ConsentRevokedDuringProviderWaitDiscardsMigratedHistoryReply()
        { await MigratedTranscriptHistoryIsNotReplayedAndPendingReplyIsRejectedAfterMemoryPolicyChanges("consent"); }

        [Test]
        public async Task ForgottenMigratedSourceDuringProviderWaitDiscardsReplyButKeepsChatHistory()
        { await MigratedTranscriptHistoryIsNotReplayedAndPendingReplyIsRejectedAfterMemoryPolicyChanges("suppression"); }

        [Test]
        public void MigratedHistoryDoesNotEnableOnlineGenerationAndRemainsInChatHistory()
        {
            const string migratedSourceId = "migrated-player-source-001";
            string root = NewRoot();
            string legacyPath = Path.GetFullPath(Path.Combine("Tools", "Conversation", "Fixtures", "v2", "migrated-memory-history.jsonl"));
            string databasePath = Path.Combine(root, "conversations.sqlite3");
            ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(legacyPath,
                new SqliteTranscriptStore(databasePath));
            Assert.AreEqual("imported", imported.State);
            var transcript = new ConversationTranscript(databasePath);
            ConversationHistorySnapshot migrated = transcript.ReadRecent(12, 6000);
            Assert.IsTrue(migrated.Success, migrated.Error);
            IReadOnlyList<ConversationPresentedUtteranceV2> history = migrated.utterances.Select(item =>
                new ConversationPresentedUtteranceV2(item.speaker, item.source, item.responseId, item.nodeId, item.text, item.sourceId)).ToList();
            var host = new FakeHost { Snapshot = WithPresentedHistory(Snapshot(false, true, true), history) };
            var provider = new FakeProvider { ResultFactory = ClarifyResult };
            var coordinator = new DirectConversationCoordinator(host, transcript, provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("talk about cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(result.usedProvider);
            Assert.AreEqual(0, provider.sendCount, "an imported transcript must not enable online generation");
            ConversationHistorySnapshot afterTurn = coordinator.ReadRecent(12, 6000);
            Assert.IsTrue(afterTurn.Success, afterTurn.Error);
            Assert.IsTrue(afterTurn.utterances.Any(item => item.sourceId == migratedSourceId));
            Assert.IsTrue(afterTurn.utterances.Any(item => item.text == "talk about cats"),
                "ordinary chat history remains durable when optional memory/recording policy is unavailable");
        }

        private static async Task MigratedTranscriptHistoryIsNotReplayedAndPendingReplyIsRejectedAfterMemoryPolicyChanges(string change)
        {
            const string migratedSourceId = "migrated-player-source-001";
            string root = NewRoot();
            string legacyPath = Path.GetFullPath(Path.Combine("Tools", "Conversation", "Fixtures", "v2", "migrated-memory-history.jsonl"));
            string databasePath = Path.Combine(root, "conversations.sqlite3");
            ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(legacyPath,
                new SqliteTranscriptStore(databasePath));
            Assert.AreEqual("imported", imported.State);

            var transcript = new ConversationTranscript(databasePath);
            ConversationHistorySnapshot migrated = transcript.ReadRecent(12, 6000);
            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.AreEqual(migratedSourceId, migrated.utterances[0].sourceId);
            var history = migrated.utterances.Select(item => new ConversationPresentedUtteranceV2(
                item.speaker, item.source, item.responseId, item.nodeId, item.text, item.sourceId)).ToList();
            ConversationTurnSnapshot baseSnapshot = Snapshot(true, true, true);
            var snapshot = new ConversationTurnSnapshot(baseSnapshot.request, baseSnapshot.candidates, baseSnapshot.coverage,
                baseSnapshot.coveragePolicy, baseSnapshot.catalogue, baseSnapshot.authoredInstances, baseSnapshot.context,
                baseSnapshot.history, baseSnapshot.nowUtc, baseSnapshot.draw, baseSnapshot.allowedExpressions,
                baseSnapshot.characterRules, baseSnapshot.onlineEnabled, history, null, true, true, 1);
            var host = new FakeHost { Snapshot = snapshot };

            string persistent = Path.Combine(root, "persistent");
            new PlayerMemoryPolicyFlag(persistent).MarkRequired();
            string python = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", ".venv", "Scripts", "python.exe"));
            string helper = Path.GetFullPath(Path.Combine("Tools", "CompanionRuntime", "tests", "fake_sidecar.py"));
            string statePath = Path.Combine(root, "fake-memory-state.json");
            string startedPath = statePath + ".turn-started";
            string releasePath = statePath + ".turn-release";
            string turnPath = statePath + ".turn.json";
            string syncedSourcesPath = statePath + ".synced-sources.jsonl";
            File.WriteAllText(statePath, "{\"enabled\":true,\"revision\":1,\"suppressedSourceIds\":[]}");

            using (var process = new CompanionProcessClient(python,
                "\"" + helper + "\" v3-turn-deferred \"" + statePath + "\"",
                () => "synthetic-key-marker", TimeSpan.FromSeconds(3)))
            {
                var memory = new PlayerMemoryClient(process, "player-1", persistent);
                var coordinator = new DirectConversationCoordinator(host, transcript, process, memory);
                Task<ConversationSubmitResult> pendingTurn = coordinator.SubmitAsync("I like coffee", CancellationToken.None);
                bool providerStarted = await WaitForFileAsync(startedPath, TimeSpan.FromSeconds(5));
                if (!providerStarted)
                {
                    File.WriteAllText(releasePath, "release");
                    await AwaitWithinAsync(pendingTurn, TimeSpan.FromSeconds(15));
                    Assert.Fail("deferred synthetic provider did not receive the memory turn");
                }

                try
                {
                    string outboundTurn = File.ReadAllText(turnPath);
                    StringAssert.Contains("presentedHistorySourceIds", outboundTurn);
                    StringAssert.Contains(migratedSourceId, outboundTurn);
                    string[] syncedSources = File.Exists(syncedSourcesPath)
                        ? File.ReadAllLines(syncedSourcesPath).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                        : new string[0];
                    Assert.AreEqual(1, syncedSources.Length, "only the new request should be captured by the current memory consent");
                    Assert.IsFalse(syncedSources.Contains(migratedSourceId), "migrated chat history must not be replayed as new memory evidence");

                    string nextState = change == "consent"
                        ? "{\"enabled\":false,\"revision\":2,\"suppressedSourceIds\":[]}"
                        : "{\"enabled\":true,\"revision\":1,\"suppressedSourceIds\":[\"" + migratedSourceId + "\"]}";
                    File.WriteAllText(statePath, nextState);
                }
                finally { File.WriteAllText(releasePath, "release"); }

                ConversationSubmitResult result = await AwaitWithinAsync(pendingTurn, TimeSpan.FromSeconds(15));
                Assert.AreEqual("memory_unavailable", result.statusCode);
                Assert.AreEqual(0, host.presentCount);
                Assert.IsNull(result.plan);
                ConversationTranscriptPage events = coordinator.ReadPage("0", 20);
                Assert.IsTrue(events.events.Any(item => item.value.eventType == "request_received" && item.value.eventId == migratedSourceId));
                Assert.IsTrue(events.events.Any(item => item.value.eventType == "request_received" && item.value.text == "I like coffee"));
                Assert.IsFalse(events.events.Any(item => item.value.eventType == "response_received"),
                    "the stale provider result must not become a durable chat response");
                ConversationHistorySnapshot afterPolicyChange = coordinator.ReadRecent(12, 6000);
                Assert.IsTrue(afterPolicyChange.Success, afterPolicyChange.Error);
                Assert.IsTrue(afterPolicyChange.utterances.Any(item => item.sourceId == migratedSourceId),
                    "memory policy changes must not erase mandatory migrated chat history");
            }
        }

        private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!File.Exists(path))
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                TimeSpan delay = remaining < TimeSpan.FromMilliseconds(25)
                    ? remaining : TimeSpan.FromMilliseconds(25);
                await Task.Delay(delay);
            }
            return true;
        }

        private static async Task<T> AwaitWithinAsync<T>(Task<T> task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (!ReferenceEquals(completed, task))
                throw new TimeoutException("The deferred fake-provider turn did not complete within " + timeout + ".");
            return await task;
        }

        [Test]
        public void UnavailableGteIsReportedAsAnExplicitLocalFailure()
        {
            string root = NewRoot();
            var host = new FakeHost { CaptureError = new InvalidOperationException("semantic_unavailable") };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), null);

            ConversationSubmitResult result = coordinator.SubmitAsync("talk about cats", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("semantic_unavailable", result.statusCode);
            Assert.AreEqual(0, host.presentCount);
        }

        [Test]
        public void ChangedActiveResponseAfterProviderWaitStoresLateResultOnly()
        {
            string root = NewRoot();
            var host = new FakeHost { Snapshot = Snapshot(true), ChangeActiveResponseOnRevalidate = true };
            var provider = new FakeProvider { ResultFactory = ClarifyResult };
            var coordinator = new DirectConversationCoordinator(host, new ConversationTranscript(Path.Combine(root, "conversations.sqlite3")), provider);

            ConversationSubmitResult result = coordinator.SubmitAsync("I had a strange day", CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(result.wasLate);
            Assert.AreEqual(0, host.presentCount);
            Assert.AreEqual(1, provider.sendCount);
            Assert.AreEqual("response_received", coordinator.ReadPage("0", 20).events[2].value.eventType);
        }

        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "AlterOS-DirectCoordinator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void BlockTranscriptEvent(string databasePath, string eventType)
        {
            if (eventType != "response_received" && eventType != "node_presented")
                throw new ArgumentException("Unsupported synthetic transcript event type.", "eventType");
            using (var connection = new SQLiteConnection(databasePath))
                connection.Execute("CREATE TRIGGER synthetic_transcript_insert_failure BEFORE INSERT ON transcript_events WHEN NEW.event_type='" +
                    eventType + "' BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
        }

        private static void ClearTranscriptBlock(string databasePath)
        {
            using (var connection = new SQLiteConnection(databasePath))
                connection.Execute("DROP TRIGGER IF EXISTS synthetic_transcript_insert_failure");
        }

        private static ConversationTurnSnapshot Snapshot(bool online, bool memoryRequired = false,
            bool memoryAvailable = false, string channel = "direct",
            string characterRules = "Altera is warm and attentive.")
        {
            TopicDefinition topic = ConversationTestData.Topic("cats", ConversationTestData.Scenario("cats-001", 1));
            TopicInstance instance = TopicInstance.Create(topic, "authored-cats", "authored");
            var catalog = new Dictionary<string, TopicDefinition> { { topic.topicId, topic } };
            var instances = new Dictionary<string, TopicInstance> { { topic.topicId, instance } };
            var history = new TopicHistory();
            var context = new TopicContext();
            var candidates = new TopicCandidateSet { indexIdentity = "synthetic-gte-test", source = "synthetic" };
            candidates.cards.Add(new TopicCandidateCard { topicId = "cats", prompt = "Cats", examples = new List<string> { "cats" }, similarity = .8f, supportCount = 1 });
            candidates = TopicCandidateRetriever.PrepareForJudgment(candidates, catalog, instances, context, history, DateTimeOffset.UtcNow, "direct");
            var coverage = ConversationCoverage.BuildSnapshot(new[] { "cats" }, null, catalog, instances, context, history, DateTimeOffset.UtcNow, "direct");
            var request = new ConversationRequest { schemaVersion = 1, requestId = "request-host", sessionId = "one-companion", channel = channel, text = "test" };
            return new ConversationTurnSnapshot(request, candidates, coverage, null, catalog, instances, context, history,
                DateTimeOffset.UtcNow, .2d, new HashSet<string> { "joy", "thinking" }, characterRules, online,
                null, null, memoryRequired, memoryAvailable, memoryRequired ? 3 : 0);
        }

        private static ConversationTurnSnapshot WithPresentedHistory(ConversationTurnSnapshot source,
            IReadOnlyList<ConversationPresentedUtteranceV2> history)
        {
            return new ConversationTurnSnapshot(source.request, source.candidates, source.coverage, source.coveragePolicy,
                source.catalogue, source.authoredInstances, source.context, source.history, source.nowUtc, source.draw,
                source.allowedExpressions, source.characterRules, source.onlineEnabled, history, source.suppressedSourceIds,
                source.memoryPolicyRequired, source.memoryPolicyAvailable, source.memoryConsentRevision);
        }

        private static ConversationProtocolMessage ClarifyResult(string requestId)
        {
            string json = "{\"protocolVersion\":2,\"type\":\"result\",\"requestId\":\"" + requestId +
                "\",\"payload\":{\"judgment\":{\"kind\":\"clarify\",\"topicId\":\"\",\"referencedTopicIds\":[],\"reply\":{\"beats\":[{\"id\":\"beat-1\",\"text\":\"そうなんだ。\",\"displayText\":\"I see.\",\"language\":\"japanese\",\"expression\":\"joy\"}]},\"suggestedReplies\":[]},\"usage\":{\"inputTokens\":1,\"outputTokens\":1,\"totalTokens\":2,\"costUsd\":null},\"timing\":{\"providerMs\":1,\"totalMs\":2}}}";
            return ConversationProtocol.ParseEnvelope(json);
        }

        private sealed class FakeProvider : IDirectConversationProvider
        {
            public int sendCount;
            public string lastRequestId;
            public ConversationJobV2 lastJob;
            public Task<ConversationProtocolMessage> ResultTask;
            public Func<string, ConversationProtocolMessage> ResultFactory;
            public Exception StartError;
            public bool WaitForCancellation;
            public Task StartAsync(CancellationToken token) { if (StartError != null) throw StartError; return Task.FromResult(0); }
            public Task CancelAsync(string requestId) { return Task.FromResult(0); }
            public Task<ConversationProtocolMessage> SendTurnAsync(string requestId, ConversationJobV2 job, CancellationToken token)
            {
                sendCount++; lastRequestId = requestId; lastJob = job;
                if (WaitForCancellation)
                {
                    var cancelled = new TaskCompletionSource<ConversationProtocolMessage>();
                    token.Register(() => cancelled.TrySetCanceled());
                    return cancelled.Task;
                }
                return ResultTask ?? Task.FromResult(ResultFactory(requestId));
            }
        }

        private sealed class FakeScenarioProvider : IScenarioDirectConversationProvider
        {
            public int scenarioSendCount, legacySendCount;
            public string lastRequestId;
            public ScenarioTurnJobV4 lastScenarioJob;
            public Exception StartScenarioError;
            public Task<ScenarioTurnReceipt> ScenarioReceiptTask;
            public Action OnScenarioSend;
            public string[] StageFixtureNames = { "head-valid.json", "main-cats.json" };

            public Task StartAsync(CancellationToken token) { return Task.FromResult(0); }
            public Task CancelAsync(string requestId) { return Task.FromResult(0); }
            public Task<ConversationProtocolMessage> SendTurnAsync(string requestId, ConversationJobV2 job, CancellationToken token)
            { legacySendCount++; return Task.FromResult<ConversationProtocolMessage>(null); }
            public Task<ConversationProtocolMessage> SendMemoryTurnAsync(string requestId, ConversationJobV2 job,
                PlayerMemoryTurnPolicyV3 memory, CancellationToken token)
            { legacySendCount++; return Task.FromResult<ConversationProtocolMessage>(null); }
            public Task<ScenarioReadyV4> StartScenarioAsync(string playerId, string persistentDataPath,
                bool allowOnlineGeneration, CancellationToken token)
            {
                if (StartScenarioError != null) return Task.FromException<ScenarioReadyV4>(StartScenarioError);
                return Task.FromResult((ScenarioReadyV4)ScenarioTurnProtocol.ParseEnvelope(
                    "{\"protocolVersion\":4,\"type\":\"ready\",\"requestId\":\"ready-1\",\"payload\":{\"runtimeVersion\":\"test\",\"modelId\":\"synthetic\",\"capabilities\":{\"memory\":true,\"generation\":true,\"scenario-turn-v1\":true}}}").payload);
            }
            public Task<ScenarioTurnReceipt> SendScenarioTurnAsync(string requestId, ScenarioTurnJobV4 job,
                Action<ScenarioStageResult> onStage, CancellationToken token)
            {
                scenarioSendCount++; lastRequestId = requestId; lastScenarioJob = job;
                foreach (string fixtureName in StageFixtureNames) onStage(Stage(fixtureName, requestId));
                OnScenarioSend?.Invoke();
                return ScenarioReceiptTask ?? Task.FromResult(SuccessfulReceipt(requestId));
            }

            internal static ScenarioTurnReceipt SuccessfulReceipt(string requestId)
            {
                string receipt = "{\"protocolVersion\":4,\"type\":\"scenario_complete\",\"requestId\":\"" + requestId +
                    "\",\"payload\":{\"turnId\":\"" + requestId + "\",\"turnVersion\":1," +
                    "\"head\":{\"stage\":\"head\",\"status\":\"ready\",\"usage\":{\"inputTokens\":0,\"outputTokens\":0,\"totalTokens\":0,\"costUsd\":null}}," +
                    "\"main\":{\"stage\":\"main\",\"status\":\"ready\",\"usage\":{\"inputTokens\":0,\"outputTokens\":0,\"totalTokens\":0,\"costUsd\":null}}," +
                    "\"terminalReason\":\"completed\"}}";
                return (ScenarioTurnReceipt)ScenarioTurnProtocol.ParseEnvelope(receipt).payload;
            }

            private static ScenarioStageResult Stage(string fileName, string requestId)
            {
                string path = Path.Combine("Tools", "ScenarioRuntime", "Fixtures", "v4", fileName);
                string json = File.ReadAllText(path).Replace("turn-cats-1", requestId);
                return (ScenarioStageResult)ScenarioTurnProtocol.ParseEnvelope(json).payload;
            }
        }

        private sealed class FakeHost : IDirectConversationHost
        {
            public ConversationTurnSnapshot Snapshot;
            public readonly List<string> diagnostics = new List<string>();
            public int revalidateCount, presentCount;
            public Exception CaptureError;
            public Exception PresentError;
            public bool ChangeActiveResponseOnRevalidate;
            public ManualResetEventSlim RevalidateEntered;
            public ManualResetEventSlim ContinueRevalidate;
            public ConversationTurnSnapshot Capture(string text, string requestId)
            { if (CaptureError != null) throw CaptureError; return Snapshot.WithRequest(text, requestId); }
            public void Diagnostic(string stage, string code) { diagnostics.Add(stage + "=" + code); }
            public ConversationTurnSnapshot Revalidate(ConversationTurnSnapshot original)
            {
                if (RevalidateEntered != null)
                {
                    RevalidateEntered.Set();
                    ContinueRevalidate.Wait(TimeSpan.FromSeconds(2));
                }
                revalidateCount++;
                if (!ChangeActiveResponseOnRevalidate) return original;
                var request = new ConversationRequest
                {
                    schemaVersion = 1, requestId = original.request.requestId, sessionId = original.request.sessionId,
                    channel = original.request.channel, text = original.request.text,
                    activeResponseId = "successor-response", activeTopicId = "cats"
                };
                return new ConversationTurnSnapshot(request, original.candidates, original.coverage,
                    original.coveragePolicy, original.catalogue, original.authoredInstances, original.context,
                    original.history, original.nowUtc, original.draw, original.allowedExpressions,
                    original.characterRules, original.onlineEnabled, original.presentedHistory,
                    original.suppressedSourceIds, original.memoryPolicyRequired,
                    original.memoryPolicyAvailable, original.memoryConsentRevision);
            }
            public void Present(ConversationPresentation presentation)
            { if (PresentError != null) throw PresentError; presentCount++; }
            public void Status(string stableCode, bool unsaved) { }
        }

        private sealed class FakeScenarioHost : IScenarioDirectConversationHost
        {
            public ConversationTurnSnapshot Snapshot;
            public int revalidateCount, presentCount;
            public readonly List<ScenarioEffect> scenarioEffects = new List<ScenarioEffect>();
            public Func<bool> HasDurableScenarioResponse;
            public bool PresentedScenarioWithoutDurableResponse;
            public double scenarioClockSeconds = 10d;
            public double AdvanceClockOnCapture;
            public string ScenarioPlayerId => "player-1";
            public string ScenarioPersistentDataPath => Path.GetTempPath();
            public double ScenarioClockSeconds => scenarioClockSeconds;
            public ConversationTurnSnapshot Capture(string text, string requestId)
            { scenarioClockSeconds += AdvanceClockOnCapture; return Snapshot.WithRequest(text, requestId); }
            public ConversationTurnSnapshot Revalidate(ConversationTurnSnapshot original) { revalidateCount++; return original; }
            public ScenarioRuntimeSnapshot CaptureScenarioRuntime(ConversationTurnSnapshot snapshot, string turnId)
            {
                return new ScenarioRuntimeSnapshot("", "run-" + turnId, turnId, 1, "", new[] { "cats" },
                    new[] { "joy", "gentle", "thinking", "happy" }, new ScenarioCapabilitySnapshot[0],
                    new ScenarioObjectSnapshot[0], new string[0]);
            }
            public ScenarioRuntimeSnapshot RevalidateScenarioRuntime(ConversationTurnSnapshot original, ScenarioRuntimeSnapshot runtime)
            { return runtime; }
            public IReadOnlyList<ScenarioBeat> PrepareScenarioOpening(ConversationTurnSnapshot snapshot, ScenarioRuntimeSnapshot runtime)
            { return new[] { new ScenarioBeat { id = "fallback", text = "聞いてるよ。", displayText = "I'm listening.", language = "japanese", expression = "gentle" } }; }
            public void PresentScenarioEffects(IReadOnlyList<ScenarioEffect> effects, string responseId)
            {
                if (effects == null) return;
                if (effects.Any(effect => effect != null && effect.type == "show_dialogue" &&
                    (effect.stage == "head" || effect.stage == "main")) &&
                    HasDurableScenarioResponse != null && !HasDurableScenarioResponse())
                    PresentedScenarioWithoutDurableResponse = true;
                scenarioEffects.AddRange(effects);
            }
            public void Present(ConversationPresentation presentation) { presentCount++; }
            public void Status(string stableCode, bool unsaved) { }
            public void Diagnostic(string stage, string code) { }
        }
    }
}
