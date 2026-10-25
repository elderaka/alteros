using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlterOS.Dialogue;
using AlterOS.Memory;
using AlterOS.Scenarios;
using AlterOS.Topics;

namespace AlterOS.Conversation
{
    public interface IDirectConversationProvider
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task<ConversationProtocolMessage> SendTurnAsync(string requestId, ConversationJobV2 job, CancellationToken cancellationToken);
        Task CancelAsync(string requestId);
    }

    public interface IMemoryAwareDirectConversationProvider : IDirectConversationProvider
    {
        Task<ConversationProtocolMessage> SendMemoryTurnAsync(string requestId, ConversationJobV2 job,
            PlayerMemoryTurnPolicyV3 memory, CancellationToken cancellationToken);
    }

    public interface IScenarioDirectConversationProvider : IDirectConversationProvider
    {
        Task<ScenarioReadyV4> StartScenarioAsync(string playerId, string persistentDataPath,
            bool allowOnlineGeneration, CancellationToken cancellationToken);
        Task<ScenarioTurnReceipt> SendScenarioTurnAsync(string requestId, ScenarioTurnJobV4 job,
            Action<ScenarioStageResult> onStage, CancellationToken cancellationToken);
    }

    public interface IDirectConversationHost
    {
        ConversationTurnSnapshot Capture(string text, string requestId);
        ConversationTurnSnapshot Revalidate(ConversationTurnSnapshot original);
        void Present(ConversationPresentation presentation);
        void Status(string stableCode, bool unsaved);
        void Diagnostic(string stage, string code);
    }

    public interface IScenarioDirectConversationHost : IDirectConversationHost
    {
        string ScenarioPlayerId { get; }
        string ScenarioPersistentDataPath { get; }
        double ScenarioClockSeconds { get; }
        ScenarioRuntimeSnapshot CaptureScenarioRuntime(ConversationTurnSnapshot snapshot, string turnId);
        ScenarioRuntimeSnapshot RevalidateScenarioRuntime(ConversationTurnSnapshot snapshot, ScenarioRuntimeSnapshot original);
        IReadOnlyList<ScenarioBeat> PrepareScenarioOpening(ConversationTurnSnapshot snapshot, ScenarioRuntimeSnapshot runtime);
        void PresentScenarioEffects(IReadOnlyList<ScenarioEffect> effects, string responseId);
    }

    public sealed class ConversationTurnSnapshot
    {
        public readonly ConversationRequest request;
        public readonly TopicCandidateSet candidates;
        public readonly ConversationCoverageSnapshot coverage;
        public readonly ConversationCoveragePolicy coveragePolicy;
        public readonly IReadOnlyDictionary<string, TopicDefinition> catalogue;
        public readonly IReadOnlyDictionary<string, TopicInstance> authoredInstances;
        public readonly TopicContext context;
        public readonly TopicHistory history;
        public readonly DateTimeOffset nowUtc;
        public readonly double draw;
        public readonly ISet<string> allowedExpressions;
        public readonly IReadOnlyList<ConversationPresentedUtteranceV2> presentedHistory;
        public readonly ISet<string> suppressedSourceIds;
        public readonly bool memoryPolicyRequired, memoryPolicyAvailable;
        public readonly int memoryConsentRevision;
        public readonly string characterRules;
        public readonly bool onlineEnabled;

        public ConversationTurnSnapshot(ConversationRequest turnRequest, TopicCandidateSet preparedCandidates,
            ConversationCoverageSnapshot coverageSnapshot, ConversationCoveragePolicy policy,
            IReadOnlyDictionary<string, TopicDefinition> topicCatalogue, IReadOnlyDictionary<string, TopicInstance> instances,
            TopicContext topicContext, TopicHistory topicHistory, DateTimeOffset capturedAtUtc, double selectionDraw,
            ISet<string> expressionAllowlist, string rules, bool useOnline,
            IReadOnlyList<ConversationPresentedUtteranceV2> recentPresentedHistory = null,
            ISet<string> forgottenSources = null, bool requireMemoryPolicy = false,
            bool hasMemoryPolicy = false, int consentRevision = 0)
        {
            request = turnRequest; candidates = preparedCandidates; coverage = coverageSnapshot; coveragePolicy = policy;
            catalogue = topicCatalogue; authoredInstances = instances; context = topicContext; history = topicHistory;
            nowUtc = capturedAtUtc; draw = selectionDraw; allowedExpressions = expressionAllowlist;
            characterRules = rules; onlineEnabled = useOnline;
            presentedHistory = recentPresentedHistory ?? new List<ConversationPresentedUtteranceV2>();
            suppressedSourceIds = forgottenSources ?? new HashSet<string>(StringComparer.Ordinal);
            memoryPolicyRequired = requireMemoryPolicy;
            memoryPolicyAvailable = hasMemoryPolicy;
            memoryConsentRevision = consentRevision;
        }

        public ConversationJobV2 BuildJob(ISet<string> forgottenSources = null, bool requireSourceIds = false)
        {
            IReadOnlyList<ConversationPresentedUtteranceV2> safeHistory = MemoryConversationHistoryFilter.Filter(
                presentedHistory, MergeSuppressed(suppressedSourceIds, forgottenSources), memoryPolicyRequired || requireSourceIds);
            return ConversationProtocol.BuildJob(request, candidates, coverage, safeHistory,
                characterRules, allowedExpressions);
        }

        private static ISet<string> MergeSuppressed(ISet<string> first, ISet<string> second)
        {
            var merged = new HashSet<string>(StringComparer.Ordinal);
            if (first != null) foreach (string source in first) merged.Add(source);
            if (second != null) foreach (string source in second) merged.Add(source);
            return merged;
        }

        public ConversationTurnSnapshot WithRequest(string text, string requestId)
        {
            return new ConversationTurnSnapshot(new ConversationRequest
            {
                schemaVersion = 1, requestId = requestId, sessionId = request.sessionId,
                channel = request.channel, text = text, activeResponseId = request.activeResponseId,
                activeTopicId = request.activeTopicId, explicitTopicId = request.explicitTopicId
            }, candidates, coverage, coveragePolicy, catalogue, authoredInstances, context, history,
                nowUtc, draw, allowedExpressions, characterRules, onlineEnabled, presentedHistory,
                suppressedSourceIds, memoryPolicyRequired, memoryPolicyAvailable, memoryConsentRevision);
        }
    }

    public sealed class ConversationPresentation
    {
        public readonly ConversationPlan plan;
        public readonly string responseId;
        public readonly bool isLate, transcriptDurable;
        internal ConversationPresentation(ConversationPlan conversationPlan, string id, bool late, bool durable)
        { plan = conversationPlan; responseId = id; isLate = late; transcriptDurable = durable; }
    }

    public sealed class ConversationSubmitResult
    {
        public readonly string statusCode, responseId;
        public readonly ConversationPlan plan;
        public readonly bool usedProvider, wasLate, transcriptDurable;
        internal ConversationSubmitResult(string status, string id, ConversationPlan conversationPlan,
            bool online, bool late, bool durable)
        { statusCode = status; responseId = id ?? string.Empty; plan = conversationPlan; usedProvider = online; wasLate = late; transcriptDurable = durable; }
    }

    /// <summary>One-in-flight direct conversation orchestration; game state and presentation remain host-owned.</summary>
    public sealed class DirectConversationCoordinator
    {
        private readonly IDirectConversationHost host;
        private readonly ConversationTranscript transcript;
        private readonly IDirectConversationProvider provider;
        private readonly PlayerMemoryClient memoryClient;
        private readonly object stateGate = new object();
        private readonly ScenarioTurnCoordinator scenarioDelivery = new ScenarioTurnCoordinator();
        private readonly ConcurrentQueue<ScenarioStageResult> pendingScenarioStages = new ConcurrentQueue<ScenarioStageResult>();
        private readonly HashSet<string> recordedScenarioResponses = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> durablyPresentedScenarioBeatIds = new HashSet<string>(StringComparer.Ordinal);
        private ConversationTurnSnapshot activeScenarioSnapshot;
        private ScenarioRuntimeSnapshot activeScenarioRuntime;
        private string activeScenarioResponseId = string.Empty;
        private string activeScenarioRequestId = string.Empty;
        private bool scenarioStagePumpEnabled;
        private bool scenarioResponseReceiptUnsaved;
        private int activeScenarioPresentationGeneration;
        private int inFlight;
        private int presentationGeneration;
        private CancellationTokenSource activeCancellation;
        private string activeRequestId = string.Empty;
        private bool explicitCancellationRequested;

        private sealed class MemoryPreparation
        {
            public ConversationJobV2 job;
            public PlayerMemoryTurnPolicyV3 policy;
            public ISet<string> suppressedSourceIds;
        }

        public bool IsTurnInFlight => Volatile.Read(ref inFlight) != 0;

        public DirectConversationCoordinator(IDirectConversationHost turnHost, ConversationTranscript authoritativeTranscript,
            IDirectConversationProvider conversationProvider, PlayerMemoryClient localMemoryClient = null)
        {
            host = turnHost ?? throw new ArgumentNullException(nameof(turnHost));
            transcript = authoritativeTranscript ?? throw new ArgumentNullException(nameof(authoritativeTranscript));
            provider = conversationProvider;
            memoryClient = localMemoryClient;
        }

        public async Task<ConversationSubmitResult> SubmitAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2048)
                return new ConversationSubmitResult("invalid_job", string.Empty, null, false, false, false);
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
                return new ConversationSubmitResult("busy", string.Empty, null, false, false, false);

            string requestId = Guid.NewGuid().ToString("N");
            int capturedPresentationGeneration = Volatile.Read(ref presentationGeneration);
            double scenarioSubmitSeconds = (host as IScenarioDirectConversationHost)?.ScenarioClockSeconds ?? 0d;
            var localCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            localCancellation.CancelAfter(TimeSpan.FromSeconds(60));
            lock (stateGate)
            {
                activeCancellation = localCancellation;
                activeRequestId = requestId;
                explicitCancellationRequested = false;
            }
            ConversationTurnSnapshot snapshot = null;
            bool transcriptDurable = true;
            bool usedProvider = false;
            string status = "offline";
            try
            {
                snapshot = host.Capture(text, requestId);
                if (snapshot == null || snapshot.request == null || snapshot.request.requestId != requestId)
                    return new ConversationSubmitResult("invalid_job", string.Empty, null, false, false, false);
                ConversationAppendResult receipt = transcript.Append(ConversationTranscriptEvent.RequestReceived(
                    snapshot.request.sessionId, requestId, requestId + ":request", snapshot.request.channel, text, DateTimeOffset.UtcNow));
                if (receipt.Status == ConversationAppendStatus.Unsaved)
                {
                    host.Status("transcript_unsaved", true);
                    return new ConversationSubmitResult("transcript_unsaved", string.Empty, null, false, false, false);
                }
                ConversationAppendResult pending = transcript.Append(ConversationTranscriptEvent.TurnStatus(snapshot.request.sessionId, requestId, "",
                    requestId + ":pending", "pending", DateTimeOffset.UtcNow));
                if (pending.Status == ConversationAppendStatus.Unsaved)
                {
                    host.Status("transcript_unsaved", true);
                    return new ConversationSubmitResult("transcript_unsaved", string.Empty, null, false, false, false);
                }

                ConversationPlan plan;
                ConversationJudgmentV2 judgment = null;
                ConversationResultV2 providerResult = null;
                MemoryPreparation memoryPreparation = null;
                bool explicitTopicSelection = !string.IsNullOrWhiteSpace(snapshot.request.explicitTopicId);
                bool confidentAuthoredMatch = snapshot.onlineEnabled && !explicitTopicSelection && string.IsNullOrEmpty(snapshot.request.activeTopicId) &&
                    snapshot.candidates.cards.Any(card =>
                    card != null && card.authoredAvailability == "ready" &&
                    card.similarity >= TopicCandidateRetriever.MinimumOnlineAuthoredSimilarity);
                host.Diagnostic("route", !snapshot.onlineEnabled ? "offline" : confidentAuthoredMatch ? "authored" : "rag");
                if (confidentAuthoredMatch) status = "authored_match";
                bool providerAllowed = snapshot.onlineEnabled && provider != null && !confidentAuthoredMatch && !explicitTopicSelection;
                host.Diagnostic("provider_gate", !snapshot.onlineEnabled ? "offline" : confidentAuthoredMatch ?
                    "authored_bypass" : providerAllowed ? "allowed" : "provider_missing");
                if (snapshot.onlineEnabled && !providerAllowed && !confidentAuthoredMatch && !explicitTopicSelection)
                    status = "provider_unavailable";
                bool scenarioPath = provider is IScenarioDirectConversationProvider && host is IScenarioDirectConversationHost;
                if (providerAllowed && snapshot.memoryPolicyRequired &&
                    (memoryClient == null || (!scenarioPath && !(provider is IMemoryAwareDirectConversationProvider))))
                {
                    providerAllowed = false;
                    status = "memory_unavailable";
                    host.Diagnostic("memory_gate", "unavailable");
                }
                if (providerAllowed && scenarioPath)
                    return await SubmitScenarioTurnAsync(snapshot, requestId, receipt.sequence - 1,
                        capturedPresentationGeneration, scenarioSubmitSeconds, cancellationToken, localCancellation.Token);
                if (providerAllowed)
                {
                    try
                    {
                        ConversationJobV2 job;
                        if (snapshot.memoryPolicyRequired)
                        {
                            host.Diagnostic("memory_prepare", "begin");
                            memoryPreparation = await PrepareMemoryTurnAsync(snapshot, receipt.sequence - 1,
                                localCancellation.Token);
                            host.Diagnostic("memory_prepare", "ready");
                            job = memoryPreparation.job;
                        }
                        else
                        {
                            host.Diagnostic("job_prepare", "begin");
                            try { job = snapshot.BuildJob(); }
                            catch (FormatException)
                            {
                                host.Diagnostic("job_prepare", "invalid");
                                throw;
                            }
                            catch (Exception)
                            {
                                host.Diagnostic("job_prepare", "failed");
                                throw;
                            }
                            host.Diagnostic("job_prepare", "ready");
                        }
                        host.Diagnostic("helper_start", "begin");
                        await provider.StartAsync(localCancellation.Token);
                        host.Diagnostic("helper_start", "ready");
                        host.Diagnostic("provider_request", "sent");
                        ConversationProtocolMessage message = memoryPreparation != null
                            ? await ((IMemoryAwareDirectConversationProvider)provider).SendMemoryTurnAsync(requestId,
                                job, memoryPreparation.policy, localCancellation.Token)
                            : await provider.SendTurnAsync(requestId, job, localCancellation.Token);
                        if (message == null || !string.Equals(message.requestId, requestId, StringComparison.Ordinal))
                            throw new InvalidOperationException("provider_unavailable");
                        if (message.type == "error")
                        {
                            ConversationErrorV2 error = message.payload as ConversationErrorV2;
                            PlayerMemoryErrorV3 memoryError = message.payload as PlayerMemoryErrorV3;
                            status = error != null ? error.code : memoryError != null ? memoryError.code : "provider_unavailable";
                            host.Diagnostic("provider_response", "error");
                            host.Diagnostic("provider_error", SafeProviderDiagnosticError(new InvalidOperationException(status)));
                            if (error != null && !string.IsNullOrEmpty(error.diagnosticCode))
                                host.Diagnostic("provider_detail", error.diagnosticCode);
                        }
                        else if (message.type == "result")
                        {
                            providerResult = message.payload as ConversationResultV2;
                            if (providerResult == null)
                            {
                                status = "invalid_reply";
                                host.Diagnostic("provider_error", status);
                            }
                            else
                            {
                                judgment = providerResult.judgment;
                                usedProvider = true;
                                status = "ready";
                                host.Diagnostic("provider_response", "result");
                            }
                        }
                        else
                        {
                            status = "provider_unavailable";
                            host.Diagnostic("provider_response", "unexpected");
                            host.Diagnostic("provider_error", status);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        bool turnCancelled;
                        lock (stateGate) turnCancelled = explicitCancellationRequested;
                        bool cancelled = cancellationToken.IsCancellationRequested || turnCancelled;
                        host.Diagnostic("provider_error", cancelled ? "cancelled" : "provider_timeout");
                        if (cancelled) AppendStatus(snapshot, requestId, "cancelled");
                        return new ConversationSubmitResult(cancelled ? "cancelled" : "provider_timeout", string.Empty, null, false, false, false);
                    }
                    catch (Exception exception)
                    {
                        status = SafeProviderError(exception);
                        host.Diagnostic("provider_error", SafeProviderDiagnosticError(exception));
                    }
                }

                if (usedProvider && memoryPreparation != null &&
                    !await RevalidateMemoryTurnAsync(snapshot, memoryPreparation, localCancellation.Token))
                {
                    host.Status("memory_unavailable", false);
                    return new ConversationSubmitResult("memory_unavailable", string.Empty, null, true, false, false);
                }

                ConversationTurnSnapshot current = host.Revalidate(snapshot);
                bool sameConversation = SameConversation(snapshot, current);
                if (!sameConversation) current = snapshot;
                if (usedProvider)
                {
                    ConversationJobV2 currentJob;
                    try { currentJob = current.BuildJob(memoryPreparation?.suppressedSourceIds,
                        memoryPreparation != null); }
                    catch (Exception) { currentJob = null; }
                    plan = currentJob == null ? new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = "InvalidHostContext" } :
                        UnifiedConversationPlanner.PlanV2(requestId, currentJob, judgment, current.coveragePolicy,
                            current.catalogue, current.authoredInstances, current.context, current.history,
                            DateTimeOffset.UtcNow, current.draw, current.allowedExpressions);
                    if (providerResult != null && plan.outcome != "unavailable")
                    {
                        string responseId = judgment.kind == "continue" ? snapshot.request.activeResponseId : "response-" + requestId;
                        ConversationAppendResult saved = transcript.Append(ConversationTranscriptEvent.ResponseReceived(
                            snapshot.request.sessionId, requestId, responseId, requestId + ":response", judgment.kind,
                            ConversationProtocol.SerializeJudgmentForTranscript(judgment), DateTimeOffset.UtcNow));
                        transcriptDurable = saved.Status != ConversationAppendStatus.Unsaved;
                        if (!transcriptDurable) host.Status("transcript_unsaved", true);
                    }
                }
                else
                {
                    string explicitSelection = string.IsNullOrWhiteSpace(snapshot.request.explicitTopicId)
                        ? snapshot.request.activeTopicId : snapshot.request.explicitTopicId;
                    TopicCandidateSet localCandidates = snapshot.onlineEnabled
                        ? TopicCandidateRetriever.FilterForAuthoredFallback(snapshot.candidates, explicitSelection)
                        : snapshot.candidates;
                    plan = UnifiedConversationPlanner.Plan(snapshot.request, localCandidates, null,
                        snapshot.catalogue, snapshot.authoredInstances, snapshot.context, snapshot.history,
                        DateTimeOffset.UtcNow, snapshot.draw, snapshot.allowedExpressions);
                }

                bool late = capturedPresentationGeneration != Volatile.Read(ref presentationGeneration) || !sameConversation;
                string resultResponseId = usedProvider && judgment != null && judgment.kind == "continue"
                    ? snapshot.request.activeResponseId : "response-" + requestId;
                if (plan.outcome == "unavailable")
                {
                    host.Status(status == "ready" ? "provider_unavailable" : status, !transcriptDurable);
                    return new ConversationSubmitResult(status == "ready" ? "provider_unavailable" : status,
                        resultResponseId, plan, usedProvider, late, transcriptDurable);
                }
                if (late)
                {
                    if (usedProvider) host.Status("late_reply_saved", !transcriptDurable);
                    return new ConversationSubmitResult("late_reply_saved", resultResponseId, plan, usedProvider, true, transcriptDurable);
                }

                if (!transcriptDurable) host.Status("transcript_unsaved", true);
                if (!usedProvider && status == "offline") status = "authored_match";
                host.Present(new ConversationPresentation(plan, resultResponseId, false, transcriptDurable));
                return new ConversationSubmitResult(status, resultResponseId, plan, usedProvider, false, transcriptDurable);
            }
            catch (Exception exception)
            {
                string code = SafeHostError(exception);
                host.Diagnostic("turn_failure", code);
                host.Status(code, false);
                return new ConversationSubmitResult(code, string.Empty, null, usedProvider, false, false);
            }
            finally
            {
                lock (stateGate)
                {
                    if (activeRequestId == requestId)
                    {
                        activeRequestId = string.Empty;
                        activeCancellation = null;
                        explicitCancellationRequested = false;
                    }
                }
                localCancellation.Dispose();
                Interlocked.Exchange(ref inFlight, 0);
            }
        }

        public void DismissPresentation()
        {
            Interlocked.Increment(ref presentationGeneration);
            DispatchScenarioEffects(scenarioDelivery.Dismiss());
            durablyPresentedScenarioBeatIds.Clear();
        }

        public void CancelTurn()
        {
            CancellationTokenSource cancellation;
            string requestId;
            lock (stateGate)
            {
                cancellation = activeCancellation;
                requestId = activeRequestId;
                if (cancellation != null)
                {
                    explicitCancellationRequested = true;
                    Interlocked.Increment(ref presentationGeneration);
                }
            }
            if (cancellation == null) return;
            cancellation.Cancel();
            DispatchScenarioEffects(scenarioDelivery.Cancel());
            durablyPresentedScenarioBeatIds.Clear();
            if (provider != null && !string.IsNullOrEmpty(requestId))
            {
                try { provider.CancelAsync(requestId); }
                catch (Exception) { }
            }
        }

        public ConversationAppendResult AcknowledgePresented(string sessionId, string responseId, string nodeId,
            string source, string channel, string topicId, string scenarioId, string speaker,
            string japaneseText, string englishDisplayText, string expression)
        {
            string eventId = StablePresentationId(responseId, nodeId);
            ConversationAppendResult result = transcript.Append(ConversationTranscriptEvent.NodePresented(
                sessionId, responseId, nodeId, eventId, source, channel, topicId ?? "", scenarioId ?? "",
                speaker, japaneseText, englishDisplayText, expression ?? "", DateTimeOffset.UtcNow));
            if (result.Status == ConversationAppendStatus.Unsaved) host.Status("transcript_unsaved", true);
            return result;
        }

        public bool RecordScenarioBeatPresented(string stageId, string beatId)
        {
            string stage, stepId, eventId;
            ScenarioBeat beat;
            if (!scenarioDelivery.TryGetVisibleBeat(stageId, beatId, out stage, out stepId, out eventId, out beat))
                return false;

            string responseId = activeScenarioResponseId;
            string sessionId = activeScenarioSnapshot?.request?.sessionId ?? string.Empty;
            ConversationAppendResult saved = transcript.Append(ConversationTranscriptEvent.NodePresented(
                sessionId, responseId, beat.id, eventId, "generated", "direct", "", activeScenarioRuntime?.runId ?? "",
                "Altera", beat.text, beat.displayText, beat.expression, DateTimeOffset.UtcNow));
            if (saved.Status == ConversationAppendStatus.Unsaved)
            {
                host.Status("transcript_unsaved", true);
                return false;
            }
            if (!scenarioDelivery.MarkBeatVisible(stageId, beatId)) return false;
            durablyPresentedScenarioBeatIds.Add(eventId);
            return true;
        }

        public IReadOnlyList<ScenarioEffect> AcknowledgeScenarioBeat(string stageId, string beatId)
        {
            IReadOnlyList<ScenarioEffect> effects;
            TryAcknowledgeScenarioBeat(stageId, beatId, out effects);
            return effects;
        }

        public bool TryAcknowledgeScenarioBeat(string stageId, string beatId, out IReadOnlyList<ScenarioEffect> effects)
        {
            string stage, stepId, eventId;
            ScenarioBeat beat;
            effects = new ScenarioEffect[0];
            if (!scenarioDelivery.TryGetVisibleBeat(stageId, beatId, out stage, out stepId, out eventId, out beat)) return false;
            if (!durablyPresentedScenarioBeatIds.Contains(eventId) && !RecordScenarioBeatPresented(stageId, beatId)) return false;
            effects = scenarioDelivery.AcknowledgeBeat(stageId, beatId);
            durablyPresentedScenarioBeatIds.Remove(eventId);
            DispatchScenarioEffects(effects);
            return true;
        }

        public bool RecordScenarioPlayerAnswer(string runId, string questionId, string answerId, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > ConversationProtocol.MaximumNodeCharacters ||
                activeScenarioSnapshot?.request == null || activeScenarioRuntime == null ||
                activeScenarioRuntime.runId != runId || string.IsNullOrWhiteSpace(questionId)) return false;
            string identity = runId + "\n" + questionId + "\n" + (answerId ?? "") + "\n" + Guid.NewGuid().ToString("N");
            string requestId = activeScenarioRequestId + "-answer-" + StableHash(identity).Substring(0, 24);
            ConversationAppendResult saved = transcript.Append(ConversationTranscriptEvent.RequestReceived(
                activeScenarioSnapshot.request.sessionId, requestId, requestId + ":request", "direct", text, DateTimeOffset.UtcNow));
            if (saved.Status == ConversationAppendStatus.Unsaved) host.Status("transcript_unsaved", true);
            return saved.Status != ConversationAppendStatus.Unsaved;
        }

        public void PumpScenarioTurn(double nowSeconds)
        {
            DispatchScenarioEffects(scenarioDelivery.Tick(nowSeconds));
            if (!scenarioStagePumpEnabled) return;
            ScenarioStageResult stage;
            while (pendingScenarioStages.TryDequeue(out stage))
            {
                IScenarioDirectConversationHost scenarioHost = host as IScenarioDirectConversationHost;
                ScenarioRuntimeSnapshot current = scenarioHost == null || activeScenarioSnapshot == null ? null :
                    scenarioHost.RevalidateScenarioRuntime(activeScenarioSnapshot, activeScenarioRuntime);
                if (stage != null && stage.status == "ready" && current != null && activeScenarioRuntime != null &&
                    current.runId == activeScenarioRuntime.runId && current.turnId == activeScenarioRuntime.turnId &&
                    current.contextRevision == activeScenarioRuntime.contextRevision &&
                    !EnsureScenarioResponseDurable(activeScenarioSnapshot, activeScenarioRequestId))
                {
                    AbortScenarioDelivery(activeScenarioPresentationGeneration);
                    return;
                }
                DispatchScenarioEffects(scenarioDelivery.Receive(stage, current));
            }
        }

        private async Task<ConversationSubmitResult> SubmitScenarioTurnAsync(ConversationTurnSnapshot snapshot,
            string requestId, long sequenceBeforeRequest, int capturedPresentationGeneration, double submittedAtSeconds,
            CancellationToken callerCancellationToken, CancellationToken cancellationToken)
        {
            var scenarioProvider = (IScenarioDirectConversationProvider)provider;
            var scenarioHost = (IScenarioDirectConversationHost)host;
            activeScenarioSnapshot = snapshot;
            activeScenarioResponseId = "response-" + requestId;
            activeScenarioRequestId = requestId;
            activeScenarioPresentationGeneration = capturedPresentationGeneration;
            scenarioResponseReceiptUnsaved = false;
            scenarioStagePumpEnabled = !snapshot.memoryPolicyRequired;
            durablyPresentedScenarioBeatIds.Clear();
            activeScenarioRuntime = scenarioHost.CaptureScenarioRuntime(snapshot, requestId);
            IReadOnlyList<ScenarioBeat> opening = scenarioHost.PrepareScenarioOpening(snapshot, activeScenarioRuntime);
            var input = new ScenarioTurnInput(requestId, requestId, 1, activeScenarioRuntime, opening);
            DispatchScenarioEffects(scenarioDelivery.Begin(input, submittedAtSeconds));
            try
            {
                host.Diagnostic("scenario_start", "begin");
                ScenarioReadyV4 ready = await scenarioProvider.StartScenarioAsync(scenarioHost.ScenarioPlayerId,
                    scenarioHost.ScenarioPersistentDataPath, snapshot.onlineEnabled, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (ready == null || !ready.scenarioTurnAvailable || !ready.memoryAvailable)
                    throw new InvalidOperationException("memory_unavailable");
                if (!ready.generationAvailable) throw new InvalidOperationException("provider_auth");
                host.Diagnostic("scenario_start", "ready");

                MemoryPreparation memoryPreparation = null;
                ConversationJobV2 job;
                PlayerMemoryTurnV3 memoryTurn = null;
                if (snapshot.memoryPolicyRequired)
                {
                    memoryPreparation = await PrepareMemoryTurnAsync(snapshot, sequenceBeforeRequest,
                        cancellationToken, helperAlreadyStarted: true);
                    cancellationToken.ThrowIfCancellationRequested();
                    job = memoryPreparation.job;
                    memoryTurn = new PlayerMemoryTurnV3(memoryPreparation.job, memoryPreparation.policy);
                }
                else job = snapshot.BuildJob();

                var scenarioJob = new ScenarioTurnJobV4(requestId, 1, "begin", snapshot.onlineEnabled,
                    job, memoryTurn, activeScenarioRuntime, null);
                host.Diagnostic("scenario_request", "sent");
                ScenarioTurnReceipt receipt = await scenarioProvider.SendScenarioTurnAsync(requestId, scenarioJob,
                    result => pendingScenarioStages.Enqueue(result), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (memoryPreparation != null)
                {
                    bool memoryValid = await RevalidateMemoryTurnAsync(snapshot, memoryPreparation, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!memoryValid) throw new InvalidOperationException("memory_unavailable");
                }

                if (receipt == null || receipt.terminalReason != "completed" || receipt.main.status != "ready")
                {
                    AbortScenarioDelivery(capturedPresentationGeneration);
                    string error = receipt != null && receipt.terminalReason == "cancelled" ? "cancelled" : "provider_unavailable";
                    AppendStatus(snapshot, requestId, error == "cancelled" ? "cancelled" : "failed");
                    bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                    return new ConversationSubmitResult(error, activeScenarioResponseId,
                        new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = error },
                        false, lateFailure, false);
                }

                if (scenarioResponseReceiptUnsaved)
                {
                    bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                    return new ConversationSubmitResult("transcript_unsaved", activeScenarioResponseId,
                        new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = "transcript_unsaved" },
                        true, lateFailure, false);
                }

                // Required-memory turns remain gated until their consent/context revision has been revalidated.
                scenarioStagePumpEnabled = true;
                PumpScenarioTurn(scenarioHost.ScenarioClockSeconds);
                if (scenarioResponseReceiptUnsaved)
                {
                    bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                    return new ConversationSubmitResult("transcript_unsaved", activeScenarioResponseId,
                        new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = "transcript_unsaved" },
                        true, lateFailure, false);
                }
                if (!EnsureScenarioResponseDurable(snapshot, requestId))
                {
                    AbortScenarioDelivery(capturedPresentationGeneration);
                    bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                    return new ConversationSubmitResult("transcript_unsaved", activeScenarioResponseId,
                        new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = "transcript_unsaved" },
                        true, lateFailure, false);
                }

                // A provider may ignore cancellation and race a successful return. Check again before the terminal receipt.
                cancellationToken.ThrowIfCancellationRequested();
                scenarioStagePumpEnabled = true;
                PumpScenarioTurn(scenarioHost.ScenarioClockSeconds);
                if (scenarioResponseReceiptUnsaved)
                {
                    bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                    return new ConversationSubmitResult("transcript_unsaved", activeScenarioResponseId,
                        new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = "transcript_unsaved" },
                        true, lateFailure, false);
                }
                ScenarioRuntimeSnapshot currentRuntime = scenarioHost.RevalidateScenarioRuntime(snapshot, activeScenarioRuntime);
                DispatchScenarioEffects(scenarioDelivery.Receive(receipt, currentRuntime));

                ConversationTurnSnapshot current = host.Revalidate(snapshot);
                bool sameConversation = SameConversation(snapshot, current);
                bool late = capturedPresentationGeneration != Volatile.Read(ref presentationGeneration) || !sameConversation;
                cancellationToken.ThrowIfCancellationRequested();
                if (late)
                {
                    host.Status("late_reply_saved", false);
                    return new ConversationSubmitResult("late_reply_saved", activeScenarioResponseId,
                        new ConversationPlan { requestId = requestId, outcome = "scenario" }, true, true, true);
                }
                return new ConversationSubmitResult("ready", activeScenarioResponseId,
                    new ConversationPlan { requestId = requestId, outcome = "scenario" }, true, false, true);
            }
            catch (OperationCanceledException)
            {
                bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                AbortScenarioDelivery(capturedPresentationGeneration);
                bool cancelled = callerCancellationToken.IsCancellationRequested || WasExplicitCancellationRequested();
                string error = cancelled ? "cancelled" : "provider_timeout";
                host.Diagnostic("scenario_error", error);
                AppendStatus(snapshot, requestId, cancelled ? "cancelled" : "failed");
                return new ConversationSubmitResult(error, string.Empty,
                    new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = error },
                    false, lateFailure, false);
            }
            catch (Exception exception)
            {
                bool lateFailure = IsScenarioPresentationLate(capturedPresentationGeneration);
                AbortScenarioDelivery(capturedPresentationGeneration);
                string error = SafeProviderError(exception);
                host.Diagnostic("scenario_error", error);
                AppendStatus(snapshot, requestId, "failed");
                return new ConversationSubmitResult(error, string.Empty,
                    new ConversationPlan { requestId = requestId, outcome = "unavailable", diagnosticCode = error },
                    false, lateFailure, false);
            }
        }

        private bool EnsureScenarioResponseDurable(ConversationTurnSnapshot snapshot, string requestId)
        {
            if (snapshot == null || snapshot.request == null || string.IsNullOrWhiteSpace(activeScenarioResponseId)) return false;
            if (recordedScenarioResponses.Contains(activeScenarioResponseId)) return true;
            string summary = "{\"turnId\":\"" + requestId + "\",\"turnVersion\":1}";
            ConversationAppendResult saved = transcript.Append(ConversationTranscriptEvent.ResponseReceived(
                snapshot.request.sessionId, requestId, activeScenarioResponseId, requestId + ":scenario-response",
                "scenario", summary, DateTimeOffset.UtcNow));
            if (saved.Status == ConversationAppendStatus.Unsaved)
            {
                scenarioResponseReceiptUnsaved = true;
                host.Status("transcript_unsaved", true);
                return false;
            }
            recordedScenarioResponses.Add(activeScenarioResponseId);
            return true;
        }

        private bool IsScenarioPresentationLate(int capturedGeneration)
        { return capturedGeneration != Volatile.Read(ref presentationGeneration); }

        private bool WasExplicitCancellationRequested()
        {
            lock (stateGate) return explicitCancellationRequested;
        }

        private void AbortScenarioDelivery(int capturedPresentationGeneration)
        {
            scenarioStagePumpEnabled = false;
            while (pendingScenarioStages.TryDequeue(out _)) { }
            IReadOnlyList<ScenarioEffect> effects = scenarioDelivery.Cancel();
            if (!IsScenarioPresentationLate(capturedPresentationGeneration)) DispatchScenarioEffects(effects);
        }

        private void DispatchScenarioEffects(IReadOnlyList<ScenarioEffect> effects)
        {
            if (effects == null || effects.Count == 0) return;
            foreach (ScenarioEffect effect in effects)
            {
                if (effect == null) continue;
                if (effect.type == "turn_complete" && recordedScenarioResponses.Add("delivered:" + effect.runId))
                {
                    string sessionId = activeScenarioSnapshot?.request?.sessionId ?? string.Empty;
                    ConversationAppendResult saved = transcript.Append(ConversationTranscriptEvent.TurnStatus(sessionId, activeScenarioRequestId,
                        activeScenarioResponseId, activeScenarioRequestId + ":delivered", "delivered", DateTimeOffset.UtcNow));
                    if (saved.Status == ConversationAppendStatus.Unsaved) host.Status("transcript_unsaved", true);
                }
            }
            IScenarioDirectConversationHost scenarioHost = host as IScenarioDirectConversationHost;
            scenarioHost?.PresentScenarioEffects(effects, activeScenarioResponseId);
        }

        private async Task<MemoryPreparation> PrepareMemoryTurnAsync(ConversationTurnSnapshot snapshot,
            long sequenceBeforeRequest, CancellationToken cancellationToken, bool helperAlreadyStarted = false)
        {
            if (!helperAlreadyStarted)
            {
                PlayerMemoryReadyV3 ready = await memoryClient.StartForGenerationAsync(cancellationToken);
                if (ready == null || !ready.memoryAvailable || !ready.generationAvailable)
                    throw new InvalidOperationException(ready != null && !ready.generationAvailable ? "provider_auth" : "memory_unavailable");
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PlayerMemoryResultV3 consentResult = await memoryClient.GetConsentAsync(0, now, cancellationToken);
            if (consentResult == null || consentResult.status != "accepted" || consentResult.consent == null)
                throw new InvalidOperationException("memory_unavailable");
            PlayerMemoryConsentV3 consent = consentResult.consent;

            PlayerMemoryResultV3 checkpoint = await memoryClient.SyncSourcesAsync(0, sequenceBeforeRequest,
                new PlayerMemorySourceSyncEvent[0], consent.revision, now, cancellationToken);
            if (checkpoint == null || checkpoint.status != "accepted" || !checkpoint.lastSequence.HasValue)
                throw new InvalidOperationException("memory_unavailable");
            long cursor = checkpoint.lastSequence.Value;
            bool caughtUp = false;
            for (int pageIndex = 0; pageIndex < 64; pageIndex++)
            {
                ConversationTranscriptPage page = transcript.ReadPage(cursor.ToString(CultureInfo.InvariantCulture), 32);
                if (page == null || !page.Success) throw new InvalidOperationException("memory_unavailable");
                if (page.events.Count == 0) { caughtUp = true; break; }
                PlayerMemoryTranscriptSyncBatch batch = PlayerMemoryTranscriptSync.BuildPage(page, memoryClient.PlayerId,
                    consent.enabled, cursor);
                if (batch.throughSequence <= cursor) throw new InvalidOperationException("memory_unavailable");
                PlayerMemoryResultV3 synced = await memoryClient.SyncSourcesAsync(cursor, batch.throughSequence,
                    batch.events, consent.revision, now, cancellationToken);
                if (synced == null || synced.status != "accepted" || !synced.lastSequence.HasValue ||
                    synced.lastSequence.Value < batch.throughSequence)
                    throw new InvalidOperationException("memory_unavailable");
                cursor = synced.lastSequence.Value;
            }
            if (!caughtUp) throw new InvalidOperationException("memory_unavailable");

            PlayerMemoryResultV3 contextResult = await memoryClient.GetContextAsync(snapshot.request.text,
                consent.revision, now, cancellationToken);
            if (contextResult == null || contextResult.status != "accepted" || contextResult.context == null)
                throw new InvalidOperationException("memory_unavailable");
            var suppressed = new HashSet<string>(contextResult.context.suppressedSourceIds, StringComparer.Ordinal);
            ConversationJobV2 job = snapshot.BuildJob(suppressed, true);
            var policy = new PlayerMemoryTurnPolicyV3(consent.playerId, consent.revision, now,
                snapshot.request.requestId + ":request", null, null, false, "", true,
                job.presentedHistory.Select(item => item.sourceId));
            return new MemoryPreparation { job = job, policy = policy, suppressedSourceIds = suppressed };
        }

        private async Task<bool> RevalidateMemoryTurnAsync(ConversationTurnSnapshot snapshot,
            MemoryPreparation preparation, CancellationToken cancellationToken)
        {
            if (memoryClient == null || !memoryClient.MemoryPolicyRequired) return false;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PlayerMemoryResultV3 consent = await memoryClient.GetConsentAsync(0, now, cancellationToken);
            if (consent == null || consent.status != "accepted" || consent.consent == null ||
                consent.consent.revision != preparation.policy.consentRevision) return false;
            PlayerMemoryResultV3 context = await memoryClient.GetContextAsync(snapshot.request.text,
                consent.consent.revision, now, cancellationToken);
            if (context == null || context.status != "accepted" || context.context == null) return false;
            var currentSuppressed = new HashSet<string>(context.context.suppressedSourceIds, StringComparer.Ordinal);
            return preparation.suppressedSourceIds.SetEquals(currentSuppressed);
        }

        public ConversationHistorySnapshot ReadRecent(int maxUtterances, int maxCharacters)
        { return transcript.ReadRecent(maxUtterances, maxCharacters); }

        public ConversationTranscriptPage ReadPage(string cursor, int count)
        { return transcript.ReadPage(cursor, count); }

        private void AppendStatus(ConversationTurnSnapshot snapshot, string requestId, string value)
        {
            if (snapshot == null || snapshot.request == null) return;
            transcript.Append(ConversationTranscriptEvent.TurnStatus(snapshot.request.sessionId, requestId, "",
                requestId + ":" + value, value, DateTimeOffset.UtcNow));
        }

        private static bool SameConversation(ConversationTurnSnapshot original, ConversationTurnSnapshot current)
        {
            return original != null && current != null && original.request != null && current.request != null &&
                original.request.sessionId == current.request.sessionId &&
                original.request.activeResponseId == current.request.activeResponseId &&
                original.request.activeTopicId == current.request.activeTopicId &&
                original.request.explicitTopicId == current.request.explicitTopicId;
        }

        private static string SafeProviderError(Exception exception)
        {
            if (exception is TimeoutException) return "provider_timeout";
            string code = exception is InvalidOperationException ? exception.Message : string.Empty;
            switch (code)
            {
                case "credential_missing": return "provider_auth";
                case "provider_auth": case "provider_payment_required": case "provider_rate_limited":
                case "provider_timeout": case "provider_refusal": case "provider_unavailable":
                case "helper_missing": case "helper_start_failed": case "helper_not_ready": case "memory_unavailable":
                    return code.StartsWith("helper_", StringComparison.Ordinal) ? "provider_unavailable" : code;
                default: return "provider_unavailable";
            }
        }

        private static string SafeProviderDiagnosticError(Exception exception)
        {
            if (exception is TimeoutException) return "provider_timeout";
            string code = exception is InvalidOperationException ? exception.Message : string.Empty;
            switch (code)
            {
                case "credential_missing": return "provider_auth";
                case "helper_missing": case "helper_start_failed": case "helper_not_ready":
                case "memory_unavailable": case "provider_auth": case "provider_payment_required":
                case "provider_rate_limited": case "provider_timeout": case "provider_refusal":
                case "provider_unavailable": case "invalid_reply":
                    return code;
                default: return "provider_unavailable";
            }
        }

        private static string SafeHostError(Exception exception)
        {
            string code = exception is InvalidOperationException ? exception.Message : string.Empty;
            return code == "semantic_unavailable" || code == "catalogue_unavailable" ||
                code == "catalogue_invalid" || code == "coverage_invalid" || code == "transcript_unavailable" ||
                code == "authored_unavailable" || code == "topic_entry_unavailable" || code == "content_release_unavailable"
                ? code : "invalid_job";
        }

        private static string StablePresentationId(string responseId, string nodeId)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes((responseId ?? "") + "\0" + (nodeId ?? "")));
                var builder = new StringBuilder("presented-");
                for (int index = 0; index < 16; index++) builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string StableHash(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
        }
    }
}
