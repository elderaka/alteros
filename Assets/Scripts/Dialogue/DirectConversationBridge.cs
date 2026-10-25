using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlterOS.Conversation;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Dialogue;
using AlterOS.Furniture;
using AlterOS.Interaction;
using AlterOS.Memory;
using AlterOS.Persistence;
using AlterOS.Recording;
using AlterOS.Security;
using AlterOS.Semantics;
using AlterOS.Scenarios;
using AlterOS.Topics;
using AlterOS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.Conversation
{
    [DisallowMultipleComponent]
    public sealed partial class DirectConversationBridge : MonoBehaviour, IScenarioDirectConversationHost
    {
        private const string OnlinePreference = "AlterOS.Conversation.OnlineEnabled";
        private const string SessionPreference = "AlterOS.Conversation.SessionId";
        private const string CharacterRules = "Speak as Altera in a direct, embodied VN exchange. Return concise ordered dialogue beats with Japanese speech and English display text. Be warm and human, but do not claim game actions, stat changes, external work, or completed tasks without authoritative evidence. This M2 path has no tools.";

        [Header("Authored Canvas Controls")]
        [SerializeField] private Toggle onlineGenerationToggle;
        [SerializeField] private TMP_Text onlineToggleLabel;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private DialogueManager dialogueManager;
        [Header("Development")]
        [SerializeField] private bool enableUnifiedConversation;
        [SerializeField] private bool enableEditorDevelopmentHelper;

        [NonSerialized] private ConversationTopicCatalogLoadResult topicCatalog;
        [NonSerialized] private TopicHistory topicHistory;
        [NonSerialized] private SqliteTopicHistoryStore topicHistoryStore;
        [NonSerialized] private ConversationTranscript transcript;
        [NonSerialized] private CompanionProcessClient processClient;
        [NonSerialized] private PlayerMemoryClient memoryClient;
        [NonSerialized] private DirectConversationCoordinator coordinator;
        [NonSerialized] private string sessionId;
        [NonSerialized] private string activeResponseId = string.Empty;
        [NonSerialized] private string scenarioTranscriptResponseId = string.Empty;
        [NonSerialized] private string activeTopicId = string.Empty;
        private ActivitySequenceCatalog runtimeSequenceCatalog;
        private ActivitySequenceBuilder runtimeSequenceBuilder;
        private ContentHostVocabulary runtimeVocabulary;
        private ContentHostVocabulary warmingVocabulary;
        private Task<ContentResult<ResolvedContentRelease>> contentWarmup;
        private bool preparingContent;
        private int presentationEpoch;
        [NonSerialized] private TopicPlaybackHandle activePlayback;
        [NonSerialized] private readonly Queue<ScenarioEffect> pendingScenarioDialogue = new Queue<ScenarioEffect>();
        [NonSerialized] private bool scenarioDialogueActive;
        [NonSerialized] private ScenarioConversationPresentationModel scenarioPresentation;
        [NonSerialized] private InteractionRecorder interactionRecorder;
        [NonSerialized] private ActivityInteractionBridge activityBridge;
        [NonSerialized] private ActivityGreetingMenu activityGreetingMenu;
        [NonSerialized] private string activityExchangeId = string.Empty;
        [NonSerialized] private string activityExchangeActivityId = string.Empty;
        [NonSerialized] private ScenarioRuntimeSnapshot scenarioPresentationRuntime;
        [NonSerialized] private ConversationTurnSnapshot activeScenarioSnapshot;
        [NonSerialized] private ConversationTurnSnapshot activeConversationSnapshot;
        [NonSerialized] private ScenarioRuntimeSnapshot scenarioOfferContext;
        [NonSerialized] private ScenarioStageResult completedScenarioStage;
        [NonSerialized] private string completedScenarioStepId = string.Empty;
        [NonSerialized] private ScenarioStageResult deferredScenarioStage;
        [NonSerialized] private string deferredScenarioStepId = string.Empty;
        [NonSerialized] private int deferredScenarioBeatIndex;
        [NonSerialized] private ScenarioTopicOffer pendingScenarioTopicOffer;
        [NonSerialized] private ScenarioConversationPresentationModel pendingScenarioTopicOfferOwner;
        [NonSerialized] private string legacyTopicHistoryPath;
        [NonSerialized] private string pendingExplicitTopicId = string.Empty;
        [NonSerialized] private bool onlineEnabled;
        [NonSerialized] private bool ready;
        [NonSerialized] private bool credentialRefreshPending;
        public bool IsReady => enableUnifiedConversation && ready && coordinator != null;
        public bool IsTurnInFlight => preparingContent || (coordinator != null && coordinator.IsTurnInFlight);
        public string ScenarioDebugState => scenarioPresentation == null ? "none" :
            (scenarioPresentation.Running != null ? scenarioPresentation.Running.definition.scenarioId + " / " +
                scenarioPresentation.Running.currentStepId :
             scenarioPresentation.Suspended != null ? "paused: " + scenarioPresentation.Suspended.definition.scenarioId : "none");

        private void Awake()
        {
            activityBridge = new ActivityInteractionBridge(FindObjectOfType<ActivitySceneHost>());
            if (dialogueManager == null) dialogueManager = GetComponent<DialogueManager>();
            if (onlineGenerationToggle == null)
                onlineGenerationToggle = transform.Find("InputPanel/OnlineGenerationControl")?.GetComponent<Toggle>();
            if (onlineToggleLabel == null)
                onlineToggleLabel = transform.Find("InputPanel/OnlineGenerationControl/Label")?.GetComponent<TMP_Text>();
            if (statusText == null)
                statusText = transform.Find("InputPanel/ConversationStatus")?.GetComponent<TMP_Text>();
            if (dialogueManager == null) dialogueManager = FindObjectOfType<DialogueManager>();
            if (dialogueManager != null) dialogueManager.directConversationBridge = this;
            if (onlineGenerationToggle != null) onlineGenerationToggle.gameObject.SetActive(enableUnifiedConversation);
            if (statusText != null) statusText.gameObject.SetActive(enableUnifiedConversation);

            onlineEnabled = PlayerPrefs.GetInt(OnlinePreference, 0) == 1;
            if (onlineGenerationToggle != null)
            {
                onlineGenerationToggle.SetIsOnWithoutNotify(onlineEnabled);
                onlineGenerationToggle.onValueChanged.AddListener(SetOnlineGenerationEnabled);
            }
            UpdateOnlineToggleLabel();

            sessionId = PlayerPrefs.GetString(SessionPreference, string.Empty);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(SessionPreference, sessionId);
                PlayerPrefs.Save();
            }

            if (!enableUnifiedConversation)
            {
                return;
            }

            if (!RuntimeMigrationStartup.Ensure().isReady)
            {
                SetStatus("migration_unavailable");
                return;
            }

            topicCatalog = ConversationTopicCatalog.LoadFromStreamingAssets();
            if (!topicCatalog.Success)
            {
                SetStatus("catalogue_unavailable");
                return;
            }

            string persistentRoot = Application.persistentDataPath;
            topicHistoryStore = new SqliteTopicHistoryStore(Path.Combine(persistentRoot, "Data", "alteros-game.sqlite3"));
            legacyTopicHistoryPath = Path.Combine(persistentRoot, "alteros-topic-history-v2.json");
            TopicHistoryImportResult import = TopicHistoryImporter.ImportOnce(legacyTopicHistoryPath, topicHistoryStore);
            if (import.State != "missing" && import.State != "imported" && import.State != "already_imported")
            {
                SetStatus("topic_history_unavailable");
                return;
            }
            TopicHistoryStoreLoadResult loadedHistory = topicHistoryStore.Load();
            if (!loadedHistory.Success)
            {
                SetStatus("topic_history_unavailable");
                return;
            }
            topicHistory = loadedHistory.History;
            transcript = ConversationTranscript.CreateInPersistentDataPath();
            if (!transcript.IsReady)
            {
                SetStatus("transcript_unavailable");
                return;
            }

            string helperPath = ResolveHelperPath();
            if (!string.IsNullOrEmpty(helperPath))
            {
                processClient = new CompanionProcessClient(helperPath, string.Empty,
                    OpenRouterCredentialStore.GetApiKey, TimeSpan.FromSeconds(15));
                string playerId = PlayerPrefs.GetString("AlterOS.PlayerMemory.PlayerId", string.Empty);
                if (string.IsNullOrWhiteSpace(playerId))
                {
                    playerId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("AlterOS.PlayerMemory.PlayerId", playerId);
                    PlayerPrefs.Save();
                }
                memoryClient = new PlayerMemoryClient(processClient, playerId, Application.persistentDataPath);
            }
            coordinator = new DirectConversationCoordinator(this, transcript, processClient, memoryClient);
            ready = true;
            SetStatus(string.Empty);
        }

        private void Start()
        {
            ConversationSessionStore sessionStore = FindObjectOfType<ConversationSessionStore>();
            if (sessionStore == null || !sessionStore.IsReady)
            {
                ready = false;
                SetStatus("conversation_history_unavailable");
                return;
            }
            string storedSessionId = PlayerPrefs.GetString(SessionPreference, string.Empty);
            string storedPlayerId = PlayerPrefs.GetString("AlterOS.PlayerMemory.PlayerId", string.Empty);
            if ((!string.IsNullOrWhiteSpace(storedSessionId) &&
                    !sessionStore.ImportIdentityAlias(SessionPreference, storedSessionId)) ||
                (!string.IsNullOrWhiteSpace(storedPlayerId) &&
                    !sessionStore.ImportIdentityAlias("AlterOS.PlayerMemory.PlayerId", storedPlayerId)))
            {
                ready = false;
                SetStatus("conversation_history_unavailable");
            }
            if (ready) EnsureContentWarmup();
        }

        private void OnEnable()
        {
            OpenRouterSettingsPanel.CredentialsChanged += OnCredentialsChanged;
        }

        private void OnDisable()
        {
            OpenRouterSettingsPanel.CredentialsChanged -= OnCredentialsChanged;
        }

        private void OnDestroy()
        {
            if (onlineGenerationToggle != null) onlineGenerationToggle.onValueChanged.RemoveListener(SetOnlineGenerationEnabled);
            if (coordinator != null) coordinator.CancelTurn();
            if (processClient != null)
            {
                processClient.StopAsync().GetAwaiter().GetResult();
                processClient.Dispose();
                processClient = null;
            }
        }

        private void Update()
        {
            coordinator?.PumpScenarioTurn(Time.realtimeSinceStartupAsDouble);
            PumpScenarioActivityEvents();
            activityBridge?.SetScenarioOwnerActive(scenarioPresentation != null && scenarioPresentation.Current.scenarioActive);
            NotifyAuthoredActivityFollowup();
            if (credentialRefreshPending && !IsTurnInFlight && processClient != null)
            {
                credentialRefreshPending = false;
                RefreshCredentialAsync();
            }
        }

        public void SetOnlineGenerationEnabled(bool enabled)
        {
            onlineEnabled = enabled;
            PlayerPrefs.SetInt(OnlinePreference, enabled ? 1 : 0);
            PlayerPrefs.Save();
            UpdateOnlineToggleLabel();
            SetStatus(enabled ? "online_enabled" : "online_disabled");
            if (!enabled && processClient != null)
            {
                if (IsTurnInFlight) coordinator.CancelTurn();
                StopHelperAsync();
            }
        }

        private void UpdateOnlineToggleLabel()
        {
            if (onlineToggleLabel != null)
                onlineToggleLabel.text = onlineEnabled ? "Online replies: ON" : "Online replies: OFF";
        }

        public bool IsCurrentResponse(string responseId)
        { return !string.IsNullOrWhiteSpace(responseId) && responseId == activeResponseId; }

        public void Submit(string text)
        {
            if (scenarioPresentation != null && scenarioPresentation.Current.acceptsFreeText)
            {
                ScenarioRuntimeSnapshot currentRuntime = CurrentScenarioRuntime(scenarioPresentationRuntime.turnId);
                ScenarioConversationPresentation current = scenarioPresentation.Current;
                if (!scenarioPresentation.CanSubmitFreeText(text, currentRuntime) ||
                    (authoredActivityScenario == null && (coordinator == null ||
                     !coordinator.RecordScenarioPlayerAnswer(current.runId, current.questionId, text, text)))) return;
                scenarioPresentationRuntime = currentRuntime;
                PresentScenarioContinuation(scenarioPresentation.SubmitFreeText(text, currentRuntime), scenarioTranscriptResponseId);
                return;
            }
            if (!IsReady)
            {
                dialogueManager?.ShowInputRetryState("Conversation is unavailable right now.");
                return;
            }
            SubmitAsync(text);
        }

        public void SubmitSuggestedReply(string responseId, string prompt)
        {
            if (!IsCurrentResponse(responseId))
            {
                dialogueManager?.ShowInputRetryState("That suggestion has expired. Please type a new message.");
                return;
            }
            Submit(prompt);
        }

        public void DismissPresentation()
        {
            presentationEpoch++;
            pendingExplicitTopicId = string.Empty;
            EndActivityExchange();
            pendingScenarioDialogue.Clear();
            scenarioDialogueActive = false;
            if (completedScenarioStage != null)
            {
                deferredScenarioStage = completedScenarioStage;
                deferredScenarioStepId = completedScenarioStepId;
                deferredScenarioBeatIndex = -1;
            }
            completedScenarioStage = null;
            completedScenarioStepId = string.Empty;
            coordinator?.DismissPresentation();
            scenarioPresentation?.DismissExchange();
            if (activePlayback != null && topicHistory != null && !activePlayback.IsFlowBacked)
            {
                topicHistory.Cancel(activePlayback);
                SaveTopicHistory();
            }
            activePlayback = null;
            activeResponseId = string.Empty;
            activeTopicId = string.Empty;
        }

        private async void SubmitAsync(string text)
        {
            if (IsTurnInFlight) { SetStatus("busy"); return; }
            BeginRoutingDiagnostic(text);
            int epoch = presentationEpoch;
            preparingContent = true;
            try
            {
                Task<ContentResult<ResolvedContentRelease>> warmup = EnsureContentWarmup();
                RecordRoutingDiagnostic("content", warmup.IsCompleted ? "prepared" : "warming");
                if (!warmup.IsCompleted) SetStatus("content_preparing");
                ContentResult<ResolvedContentRelease> warmed = await warmup;
                if (this == null || !isActiveAndEnabled || epoch != presentationEpoch) return;
                if (!warmed.IsReady) throw new InvalidOperationException("content_release_unavailable");
                RecordRoutingDiagnostic("content", "ready");
            }
            catch (Exception)
            {
                if (this != null && epoch == presentationEpoch)
                {
                    SetStatus("content_release_unavailable");
                    dialogueManager?.ShowInputRetryState(DisplayStatus("content_release_unavailable"));
                }
                return;
            }
            finally { preparingContent = false; }
            ConversationSubmitResult result = await coordinator.SubmitAsync(text, CancellationToken.None);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Diagnostic("turn_complete", result.statusCode);
            Diagnostic("turn_outcome", result.plan != null ? result.plan.outcome : "none");
            Diagnostic("provider_used", result.usedProvider ? "yes" : "no");
            Diagnostic("late", result.wasLate ? "yes" : "no");
#endif
            if (result.plan == null || result.plan.outcome == "unavailable")
            {
                if (!result.wasLate)
                {
                    SetStatus(result.statusCode);
                    string message = DisplayStatus(result.statusCode);
                    dialogueManager?.ShowInputRetryState(message);
                }
            }
            else if (!result.wasLate)
            {
                SetStatus(result.usedProvider ? "online_reply" : result.statusCode);
            }
            ScenarioTopicOffer completedOffer = pendingScenarioTopicOffer;
            ScenarioConversationPresentationModel offerOwner = pendingScenarioTopicOfferOwner;
            pendingScenarioTopicOffer = null;
            pendingScenarioTopicOfferOwner = null;
            if (completedOffer != null && result.plan != null && result.plan.outcome != "unavailable" &&
                offerOwner != null && scenarioOfferContext != null)
                offerOwner.CommitTopicOffer(completedOffer.offerId, scenarioOfferContext);
        }

        public ConversationTurnSnapshot Capture(string text, string requestId)
        {
            if (!ready || topicCatalog == null || topicHistory == null || transcript == null)
                throw new InvalidOperationException("catalogue_unavailable");
            GteSemanticRuntime semantic = GteSemanticRuntime.Current;
            if (semantic == null || semantic.State != GteSemanticState.Ready || string.IsNullOrWhiteSpace(semantic.IndexIdentity))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogSemanticUnavailable(semantic);
#endif
                throw new InvalidOperationException("semantic_unavailable");
            }
            bool explicitTopicSelection = !string.IsNullOrWhiteSpace(pendingExplicitTopicId);
            RecordRoutingDiagnostic("semantic", explicitTopicSelection ? "bypassed: explicit selection" : "query begin");
            DialogueRouteMatch[] matches = new DialogueRouteMatch[0];
            if (!explicitTopicSelection && !semantic.TryRoute(text, out matches,
                dialogueManager != null ? dialogueManager.semanticMinimumSimilarity : TopicFamilySemanticMatcher.DefaultMinimumSimilarity))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogSemanticUnavailable(semantic);
#endif
                RoutingDiagnostics.SetCandidates(null, null);
                throw new InvalidOperationException("semantic_unavailable");
            }

            RoutingDiagnostics.SetCandidates(matches, pendingExplicitTopicId);
            RecordRoutingDiagnostic("semantic", explicitTopicSelection ? "explicit selection ready" : "query complete");
            if (routingDebugModeOwner?.DebugMode?.IsEnabled == true) Debug.Log("[AlterOS Trace] " + RoutingDiagnostics.Report, this);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            TopicContext context = CreateContext(nowUtc);
            ConversationRequest request = NewRequest(text, requestId);
            if (pendingScenarioTopicOffer != null)
            {
                request.activeResponseId = scenarioTranscriptResponseId;
                request.activeTopicId = pendingScenarioTopicOffer.topicId;
            }
            TopicCandidateSet retrieved = TopicCandidateRetriever.FromRouteMatches(matches, semantic.IndexIdentity, topicCatalog.Catalogue);
            if (!string.IsNullOrEmpty(request.activeTopicId))
                IncludeExplicitTopic(retrieved, request.activeTopicId);
            if (!string.IsNullOrWhiteSpace(pendingExplicitTopicId))
            {
                request.explicitTopicId = pendingExplicitTopicId;
                IncludeExplicitTopic(retrieved, pendingExplicitTopicId);
                pendingExplicitTopicId = string.Empty;
            }
            TopicCandidateSet prepared = TopicCandidateRetriever.PrepareForJudgment(retrieved, topicCatalog.Catalogue,
                topicCatalog.AuthoredInstances, context, topicHistory, nowUtc, "direct");
            RecordRoutingDiagnostic("eligibility", string.Join(", ", prepared.cards.Take(5)
                .Select(card => card.topicId + "=" + card.authoredAvailability)));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(TopicSimilarityDiagnostics.Format(prepared, onlineEnabled,
                TopicCandidateRetriever.MinimumOnlineAuthoredSimilarity));
#endif
            ConversationCoverageSnapshot coverage = ConversationCoverage.BuildSnapshot(
                prepared.cards.Select(card => card.topicId), topicCatalog.CoveragePolicy, topicCatalog.Catalogue,
                topicCatalog.AuthoredInstances, context, topicHistory, nowUtc, "direct");
            ConversationHistorySnapshot recent = transcript.ReadRecent(ConversationProtocol.MaximumHistoryUtterances,
                ConversationProtocol.MaximumHistoryCharacters);
            if (!recent.Success) throw new InvalidOperationException("transcript_unavailable");
            activeConversationSnapshot = new ConversationTurnSnapshot(request, prepared, coverage, topicCatalog.CoveragePolicy,
                topicCatalog.Catalogue, topicCatalog.AuthoredInstances, context, topicHistory, nowUtc,
                UnityEngine.Random.value, AllowedExpressions(), CharacterRules, onlineEnabled,
                ToPresentedHistory(recent), null, memoryClient != null && memoryClient.MemoryPolicyRequired,
                memoryClient != null, 0);
            return activeConversationSnapshot;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static void LogSemanticUnavailable(GteSemanticRuntime semantic)
        {
            Debug.LogWarning("[AlterOS Topic Similarity] unavailable: runtime=" +
                (semantic == null ? "missing" : semantic.State.ToString()) +
                ", stage=" + (semantic != null ? semantic.StartupStage : "none") +
                ", anchors=" + (semantic != null ? semantic.AnchorCount : 0) +
                ", failure=" + (semantic != null ? semantic.FailureReason : "runtime_missing"));
        }
#endif

        private void IncludeExplicitTopic(TopicCandidateSet candidates, string topicId)
        {
            TopicCandidateRetriever.TryIncludeExplicitTopic(candidates, topicId, topicCatalog.Catalogue);
        }

        public ConversationTurnSnapshot Revalidate(ConversationTurnSnapshot original)
        {
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            TopicContext context = CreateContext(nowUtc);
            TopicCandidateSet prepared = TopicCandidateRetriever.PrepareForJudgment(original.candidates,
                topicCatalog.Catalogue, topicCatalog.AuthoredInstances, context, topicHistory, nowUtc, "direct");
            ConversationCoverageSnapshot coverage = ConversationCoverage.BuildSnapshot(
                prepared.cards.Select(card => card.topicId), topicCatalog.CoveragePolicy, topicCatalog.Catalogue,
                topicCatalog.AuthoredInstances, context, topicHistory, nowUtc, "direct");
            ConversationRequest request = NewRequest(original.request.text, original.request.requestId);
            request.activeResponseId = original.request.activeResponseId;
            request.activeTopicId = original.request.activeTopicId;
            request.explicitTopicId = original.request.explicitTopicId;
            activeConversationSnapshot = new ConversationTurnSnapshot(request, prepared, coverage, topicCatalog.CoveragePolicy,
                topicCatalog.Catalogue, topicCatalog.AuthoredInstances, context, topicHistory, nowUtc,
                original.draw, original.allowedExpressions, original.characterRules, original.onlineEnabled,
                original.presentedHistory, original.suppressedSourceIds, original.memoryPolicyRequired,
                original.memoryPolicyAvailable, original.memoryConsentRevision);
            return activeConversationSnapshot;
        }

        public string ScenarioPlayerId => memoryClient != null ? memoryClient.PlayerId : string.Empty;
        public string ScenarioPersistentDataPath => Application.persistentDataPath;
        public double ScenarioClockSeconds => Time.realtimeSinceStartupAsDouble;

        public ScenarioRuntimeSnapshot CaptureScenarioRuntime(ConversationTurnSnapshot snapshot, string turnId)
        {
            string[] availableTopics = AvailableTopicIds(snapshot.context, snapshot.nowUtc,
                snapshot.candidates.cards.Select(card => card.topicId)).ToArray();
            ISet<string> expressions = AllowedExpressions();
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects);
            scenarioPresentationRuntime = new ScenarioRuntimeSnapshot(string.Empty, "run-" + turnId, turnId,
                ScenarioContextRevision(availableTopics, expressions, capabilities), string.Empty, availableTopics, expressions,
                capabilities, objects,
                capabilities.Count == 0 ? new string[0] : new[] { ActivityConditionIds.Terminal });
            activeScenarioSnapshot = snapshot;
            return scenarioPresentationRuntime;
        }

        public ScenarioRuntimeSnapshot RevalidateScenarioRuntime(ConversationTurnSnapshot snapshot,
            ScenarioRuntimeSnapshot original)
        {
            if (snapshot == null || original == null) return null;
            ConversationTurnSnapshot current = Revalidate(snapshot);
            if (!string.Equals(current.request.sessionId, snapshot.request.sessionId, StringComparison.Ordinal) ||
                !string.Equals(current.request.activeResponseId, snapshot.request.activeResponseId, StringComparison.Ordinal)) return null;
            string[] availableTopics = AvailableTopicIds(current.context, current.nowUtc, original.knownTopicIds).ToArray();
            ISet<string> expressions = AllowedExpressions();
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects);
            return new ScenarioRuntimeSnapshot(original.scenarioId, original.runId, original.turnId,
                ScenarioContextRevision(availableTopics, expressions, capabilities), original.checkpointStepId,
                availableTopics,
                expressions, capabilities, objects,
                capabilities.Count == 0 ? new string[0] : new[] { ActivityConditionIds.Terminal },
                original.knownIntentIds, original.hostFacts.ToDictionary(item => item.Key, item => item.Value));
        }

        public IReadOnlyList<ScenarioBeat> PrepareScenarioOpening(ConversationTurnSnapshot snapshot,
            ScenarioRuntimeSnapshot runtime)
        {
            return new[]
            {
                new ScenarioBeat
                {
                    id = "local-opening", text = "少し待っていてね。", displayText = "Give me just a moment.",
                    language = "japanese", expression = runtime.allowedExpressions.Contains("thinking") ? "thinking" : ""
                }
            };
        }

        public void PresentScenarioEffects(IReadOnlyList<ScenarioEffect> effects, string responseId)
        {
            if (effects == null || dialogueManager == null) return;
            if (!string.IsNullOrWhiteSpace(responseId)) scenarioTranscriptResponseId = responseId;
            foreach (ScenarioEffect effect in effects)
            {
                if (effect == null) continue;
                RecordRoutingDiagnostic("scenario_" + effect.type,
                    "run=" + effect.runId + " step=" + effect.stepId + " outcome=" + effect.outcome);
                if (effect.type == "show_dialogue" && effect.beat != null)
                {
                    pendingScenarioDialogue.Enqueue(effect);
                    PresentNextScenarioDialogue(responseId);
                }
                else if (effect.type == "turn_segment_complete" && effect.stage == "main" && effect.scenarioStage != null)
                {
                    completedScenarioStage = effect.scenarioStage;
                    completedScenarioStepId = effect.stepId;
                    if (!scenarioDialogueActive) BeginPendingScenarioContinuation();
                }
                else if (effect.type == "late_result_saved" && effect.scenarioStage != null)
                {
                    deferredScenarioStage = effect.scenarioStage;
                    deferredScenarioStepId = effect.stepId;
                    deferredScenarioBeatIndex = effect.beatIndex;
                }
                else if (effect.type == "request_host_effect")
                    RequestScenarioHostEffect(effect, responseId);
                else if (effect.type == "cancel_turn")
                {
                    pendingScenarioDialogue.Clear();
                    scenarioDialogueActive = false;
                    completedScenarioStage = null;
                    completedScenarioStepId = string.Empty;
                    deferredScenarioStage = null;
                    deferredScenarioStepId = string.Empty;
                    deferredScenarioBeatIndex = 0;
                    dialogueManager.CloseScenarioExchange();
                }
                else if (effect.type == "turn_recovery") SetStatus("provider_unavailable");
            }
        }

        private void PresentNextScenarioDialogue(string responseId)
        {
            if (scenarioDialogueActive || pendingScenarioDialogue.Count == 0 || dialogueManager == null) return;
            ScenarioEffect effect = pendingScenarioDialogue.Dequeue();
            var sequence = new DialogueSequence();
            sequence.metadata.mode = "cinematic";
            sequence.metadata.topic = "Generated conversation";
            sequence.nodes.Add(new DialogueNode
            {
                id = effect.beat.id, speaker = string.IsNullOrEmpty(effect.beat.speaker) ? "altera" : effect.beat.speaker,
                text = effect.beat.text, translatedText = effect.beat.displayText, language = effect.beat.language,
                emotion = effect.beat.expression, voice = effect.beat.voice, audioPath = effect.beat.audioKey,
                presentation = new DialoguePresentationCue
                { expression = effect.beat.expression, animation = new DialogueAnimationCue { state = effect.beat.animation } }
            });
            string stageId = effect.stageId;
            string beatId = effect.beatId;
            scenarioDialogueActive = true;
            dialogueManager.StartValidatedSequence(sequence,
                node => coordinator?.RecordScenarioBeatPresented(stageId, beatId),
                () =>
                {
                    scenarioDialogueActive = false;
                    if (completedScenarioStage != null) BeginPendingScenarioContinuation();
                    else PresentNextScenarioDialogue(responseId);
                },
                node =>
                {
                    IReadOnlyList<ScenarioEffect> effects;
                    return coordinator != null && coordinator.TryAcknowledgeScenarioBeat(stageId, beatId, out effects);
                });
        }

        private void BeginPendingScenarioContinuation()
        {
            ScenarioStageResult stage = completedScenarioStage;
            string stepId = completedScenarioStepId;
            completedScenarioStage = null;
            completedScenarioStepId = string.Empty;
            if (stage != null) BeginScenarioContinuation(stage, stepId);
        }

        public void SelectScenarioAnswer(string stableOptionId)
        {
            if (scenarioPresentation == null || scenarioPresentationRuntime == null) return;
            ScenarioRuntimeSnapshot currentRuntime = CurrentScenarioRuntime(scenarioPresentationRuntime.turnId);
            ScenarioConversationPresentation current = scenarioPresentation.Current;
            ScenarioPresentationAnswerOption selected = current.answerOptions.FirstOrDefault(option => option.stableId == stableOptionId);
            if (selected == null || !scenarioPresentation.CanSelectAnswer(stableOptionId, currentRuntime) ||
                (authoredActivityScenario == null && (coordinator == null ||
                 !coordinator.RecordScenarioPlayerAnswer(current.runId, current.questionId, selected.optionId, selected.label)))) return;
            scenarioPresentationRuntime = currentRuntime;
            PresentScenarioContinuation(scenarioPresentation.SelectAnswer(stableOptionId, currentRuntime), scenarioTranscriptResponseId);
        }

        public void OpenScenarioFreeText()
        {
            if (scenarioPresentation == null || !scenarioPresentation.Current.acceptsFreeText || dialogueManager == null) return;
            dialogueManager.OpenScenarioAnswerInput(Submit);
        }

        public void SelectScenarioTopicOffer(string offerId, string displayedTurnId)
        {
            if (scenarioPresentation == null || scenarioOfferContext == null || scenarioOfferContext.turnId != displayedTurnId) return;
            ScenarioRuntimeSnapshot currentRuntime = CurrentScenarioRuntime(Guid.NewGuid().ToString("N"));
            IReadOnlyList<ScenarioTopicOffer> currentlyAvailable = scenarioPresentation.GetTopicOffers(currentRuntime);
            if (!currentlyAvailable.Any(item => item.offerId == offerId)) return;
            ScenarioTopicOfferSelection selection = scenarioPresentation.SelectTopicOffer(offerId, currentRuntime);
            if (selection.status != "selected" || selection.offer == null || string.IsNullOrEmpty(selection.offer.topicId) ||
                !IsReady || IsTurnInFlight) return;
            scenarioOfferContext = currentRuntime;
            pendingScenarioTopicOffer = selection.offer;
            pendingScenarioTopicOfferOwner = scenarioPresentation;
            SubmitAsync(selection.offer.label);
        }

        public void StopCurrentActivity(string expectedActivityId)
        {
            ActivityView current = activityBridge == null ? null : activityBridge.Current;
            if (current == null || current.isTerminal || current.activityId != expectedActivityId || !current.canPause) return;
            if (activityBridge.Stop(current.activityId, "player_stopped"))
            {
                activityExchangeId = string.Empty;
                activityExchangeActivityId = string.Empty;
                SetStatus("activity_stopped");
                dialogueManager?.CloseScenarioExchange();
            }
        }

        private IReadOnlyList<ActivityGreetingOption> AvailableGreetingActivities()
        {
            if (activityGreetingMenu == null)
            {
                ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
                ContentResult<RuntimeContentDocument> document = opened.IsReady
                    ? opened.value.ReadDocument("topic/activity_options.json") : null;
                try
                {
                    if (document == null || !document.IsReady)
                        throw new InvalidDataException("The pinned SQL activity menu is unavailable.");
                    activityGreetingMenu = JsonUtility.FromJson<ActivityGreetingMenu>(document.value.json) ?? new ActivityGreetingMenu();
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[DirectConversationBridge] Activity menu unavailable: " + error.Message);
                    activityGreetingMenu = new ActivityGreetingMenu();
                }
            }
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] ignored);
            ActivityView current = activityBridge == null ? null : activityBridge.Current;
            bool occupied = IsTurnInFlight || deferredScenarioStage != null || completedScenarioStage != null ||
                scenarioDialogueActive || (current != null && !current.isTerminal) ||
                (scenarioPresentation != null && scenarioPresentation.Current.scenarioActive);
            return activityGreetingMenu.Available(capabilities, occupied);
        }

        public bool IsGreetingActivityAvailable(string capabilityId)
        {
            return AvailableGreetingActivities().Any(item => item.capabilityId == capabilityId);
        }

        public bool IsGreetingTopicAvailable(string topicId)
        {
            if (!ready || topicCatalog == null || topicHistory == null || string.IsNullOrWhiteSpace(topicId)) return false;
            TopicDefinition definition;
            TopicInstance instance;
            if (!topicCatalog.Catalogue.TryGetValue(topicId, out definition) || definition == null ||
                !topicCatalog.AuthoredInstances.TryGetValue(topicId, out instance) || instance == null) return false;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TopicSelectionResult selection = TopicScenarioSelector.SelectForConversation(definition, instance,
                CreateContext(now), topicHistory, now, 0d, "direct");
            return selection.SelectedScenario != null;
        }

        /// <summary>Explicit menu selection enters the same authored Topic planner as a routed utterance.</summary>
        public void StartGreetingTopic(string topicId)
        {
            if (!IsGreetingTopicAvailable(topicId)) { SetStatus("topic_unavailable"); return; }
            TopicDefinition definition = topicCatalog.Catalogue[topicId];
            pendingExplicitTopicId = topicId;
            Submit(!string.IsNullOrWhiteSpace(definition.menuLabel) ? definition.menuLabel : definition.prompt);
        }

        public void StartGreetingActivity(string capabilityId)
        {
            // Revalidate at selection: a displayed menu is not a reservation or permission.
            ActivityGreetingOption option = AvailableGreetingActivities().FirstOrDefault(item => item.capabilityId == capabilityId);
            if (option == null) { SetStatus("activity_unavailable"); return; }
            if (capabilityId == ActivityCapabilityIds.FollowPlayer || capabilityId == ActivityCapabilityIds.DrinkWater)
            {
                StartAuthoredActivityScenario(capabilityId);
                return;
            }
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects);
            string runId = "activity-menu-" + Guid.NewGuid().ToString("N");
            const string stepId = "menu_action";
            var context = new ScenarioRuntimeSnapshot("", runId, runId, 0, stepId, null, null, capabilities, objects, null);
            var request = new ActivityRequest(runId, stepId, option.capabilityId,
                new[] { new ScenarioActionArgument { name = "target", value = option.target } });
            ActivityRequestResult result = activityBridge.TryRequest(request, context);
            SetStatus(result.status == "accepted" ? "activity_started" : "activity_unavailable");
            if (result.status == "accepted") dialogueManager?.CloseScenarioExchange();
        }

        public void AppendScenarioTopicOffers(DialogueSequence sequence)
        {
            if (sequence == null || sequence.nodes == null || sequence.nodes.Count == 0) return;
            DialogueNode finalNode = sequence.nodes.FirstOrDefault(node => node != null && node.includeTopicSuggestions);
            if (finalNode == null) return;
            if (finalNode.choices == null) finalNode.choices = new List<DialogueChoice>();
            DialogueNode favorNode = sequence.nodes.FirstOrDefault(node => node != null && node.id == "greetings_favor");
            ActivityView activeActivity = activityBridge == null ? null : activityBridge.Current;
            if (activeActivity != null && !activeActivity.isTerminal && activeActivity.canPause &&
                activeActivity.capabilityId == ActivityCapabilityIds.FollowPlayer)
            {
                if (favorNode != null)
                    favorNode.choices.Insert(0, new DialogueChoice { label = "Stop following", action = "activity_stop", value = activeActivity.activityId });
            }
            if (scenarioPresentation == null || scenarioPresentationRuntime == null) return;
            string turnId = Guid.NewGuid().ToString("N");
            ScenarioRuntimeSnapshot interaction = CurrentScenarioRuntime(turnId);
            IReadOnlyList<ScenarioTopicOffer> offers = scenarioPresentation.GetTopicOffers(interaction);
            scenarioOfferContext = interaction;
            foreach (ScenarioTopicOffer offer in offers)
                finalNode.choices.Add(new DialogueChoice
                {
                    label = offer.label, action = "scenario_topic_offer", value = offer.offerId,
                    conversationResponseId = turnId
                });
        }

        public bool TryResumeScenarioInteraction()
        {
            if (TryPresentAuthoredActivityFollowup()) return true;
            if (deferredScenarioStage != null)
            {
                ScenarioStageResult deferred = deferredScenarioStage;
                string stepId = deferredScenarioStepId;
                int beatIndex = deferredScenarioBeatIndex;
                deferredScenarioStage = null;
                deferredScenarioStepId = string.Empty;
                deferredScenarioBeatIndex = 0;
                string resumedTurnId = Guid.NewGuid().ToString("N");
                scenarioPresentationRuntime = CurrentScenarioRuntime(resumedTurnId);
                if (beatIndex < 0) BeginScenarioContinuation(deferred, stepId);
                else BeginDeferredScenarioContinuation(deferred, stepId, beatIndex);
                return true;
            }
            ActivityView activeActivity = activityBridge == null ? null : activityBridge.Current;
            if (activeActivity != null && !activeActivity.isTerminal)
            {
                if (!activeActivity.canTalk)
                {
                    SayActivityBusy(activeActivity);
                    return true;
                }
                activityExchangeActivityId = activeActivity.activityId;
                activityExchangeId = Guid.NewGuid().ToString("N");
                if (activityBridge.BeginExchange(activityExchangeActivityId, activityExchangeId))
                {
                    if (scenarioPresentation == null || scenarioPresentation.CurrentStepKind == "wait")
                        if (TryPresentActivityContext(activeActivity)) return true;
                }
                else
                {
                    activityExchangeId = string.Empty;
                    activityExchangeActivityId = string.Empty;
                }
            }
            if (scenarioPresentation == null || scenarioPresentationRuntime == null || !scenarioPresentation.Current.scenarioActive)
                return false;
            string turnId = Guid.NewGuid().ToString("N");
            ScenarioRuntimeSnapshot currentRuntime = CurrentScenarioRuntime(turnId);
            IReadOnlyList<ScenarioEffect> effects = scenarioPresentation.ResumeForInteraction(currentRuntime);
            if (effects.Count == 0) return false;
            scenarioPresentationRuntime = currentRuntime;
            PresentScenarioContinuation(effects, scenarioTranscriptResponseId);
            return true;
        }

        private void EndActivityExchange()
        {
            if (string.IsNullOrEmpty(activityExchangeId) || string.IsNullOrEmpty(activityExchangeActivityId)) return;
            activityBridge?.EndExchange(activityExchangeActivityId, activityExchangeId);
            activityExchangeId = string.Empty;
            activityExchangeActivityId = string.Empty;
        }

        private void BeginScenarioContinuation(ScenarioStageResult stage, string completedStepId)
        {
            if (stage.scenario == null || scenarioPresentationRuntime == null) return;
            scenarioPresentation = CreateScenarioPresentationModel();
            scenarioPresentation.ReplaceTopicOffers(stage.topicOffers, scenarioPresentationRuntime.turnId);
            IReadOnlyList<ScenarioEffect> effects = scenarioPresentation.StartAfterCompletedDialogue(
                stage.scenario, completedStepId, scenarioPresentationRuntime.runId, scenarioPresentationRuntime);
            PresentScenarioContinuation(effects, scenarioTranscriptResponseId);
        }

        private void BeginDeferredScenarioContinuation(ScenarioStageResult stage, string entryStepId, int nextBeatIndex)
        {
            if (stage?.scenario == null || scenarioPresentationRuntime == null) return;
            scenarioPresentation = CreateScenarioPresentationModel();
            scenarioPresentation.ReplaceTopicOffers(stage.topicOffers, scenarioPresentationRuntime.turnId);
            PresentScenarioContinuation(scenarioPresentation.StartAfterUnacknowledgedBeat(stage.scenario,
                entryStepId, nextBeatIndex, scenarioPresentationRuntime.runId, scenarioPresentationRuntime), scenarioTranscriptResponseId);
        }

        private ScenarioConversationPresentationModel CreateScenarioPresentationModel()
        {
            return new ScenarioConversationPresentationModel
            {
                HistoryObserver = ObserveScenarioHistory,
                RecordingObserver = RecordInteraction
            };
        }

        private void ObserveScenarioHistory(ScenarioHistoryObservation observation)
        {
            dialogueManager?.RecordScenarioHistoryObservation(observation);
            if (activePlayback == null || !activePlayback.IsFlowBacked || topicHistory == null || observation == null ||
                observation.kind != "scenario_step_completed") return;
            TopicPlaybackResult result = topicHistory.ReportFlowStepCompleted(activePlayback, observation, DateTimeOffset.UtcNow);
            if (result.RequiresSave) SaveTopicHistory();
        }

        private void PresentScenarioContinuation(IReadOnlyList<ScenarioEffect> effects, string responseId)
        {
            bool exchangeClosed = false;
            foreach (ScenarioEffect effect in effects ?? new ScenarioEffect[0])
            {
                if (effect == null) continue;
                RecordRoutingDiagnostic("scenario_" + effect.type,
                    "run=" + effect.runId + " step=" + effect.stepId + " outcome=" + effect.outcome);
                if (authoredActivityScenario != null && (effect.type == "show_dialogue" || effect.type == "ask_player"))
                {
                    pendingAuthoredActivityFollowup = effect;
                    authoredActivityBarkPending = true;
                    continue;
                }
                if (effect.type == "show_dialogue" && effect.step != null)
                    PresentScenarioStep(effect.step, effect.runId, responseId, false);
                else if (effect.type == "ask_player" && effect.step != null)
                    PresentScenarioStep(effect.step, effect.runId, responseId, true);
                else if (effect.type == "request_host_effect")
                    RequestScenarioHostEffect(effect, responseId);
                else if (effect.type == "return_prompt") SetStatus("scenario_return_prompt");
                else if (effect.type == "end_scenario")
                {
                    if (activePlayback != null && activePlayback.IsFlowBacked && topicHistory != null &&
                        activePlayback.FlowRunId == effect.runId)
                    {
                        TopicPlaybackResult completion = topicHistory.CompleteFlow(activePlayback,
                            activePlayback.FlowRunId, activePlayback.FlowInvocationId, effect.outcome, DateTimeOffset.UtcNow);
                        if (completion.RequiresSave) SaveTopicHistory();
                        activePlayback = null;
                    }
                    authoredActivityScenario = null;
                    pendingAuthoredActivityFollowup = null;
                    authoredActivityBarkPending = false;
                    activeTopicId = string.Empty;
                    activeResponseId = string.Empty;
                    activeScenarioSnapshot = null;
                    SetStatus("scenario_completed");
                    if (!exchangeClosed) dialogueManager?.CloseScenarioExchange();
                    exchangeClosed = true;
                }
                else if (effect.type == "close_exchange")
                {
                    SetStatus("scenario_completed");
                    if (!exchangeClosed) dialogueManager?.CloseScenarioExchange();
                    exchangeClosed = true;
                }
                else if (effect.type == "request_activity")
                    RequestScenarioActivity(effect, responseId);
            }
            activityBridge?.SetScenarioOwnerActive(scenarioPresentation != null && scenarioPresentation.Current.scenarioActive);
        }

        private void RequestScenarioHostEffect(ScenarioEffect effect, string responseId)
        {
            ScenarioHostEffectRequest request = effect == null ? null : effect.hostEffectRequest;
            if (request == null || scenarioPresentation == null || scenarioPresentationRuntime == null) return;
            ScenarioInstanceView running = scenarioPresentation.Running;
            if (running == null || running.runId != request.scenarioRunId || running.currentStepId != effect.stepId ||
                running.currentInvocationId != request.invocationId || running.currentStepExecutionId != request.stepExecutionId)
                return;
            ScenarioRuntimeSnapshot current = CurrentScenarioRuntime(scenarioPresentationRuntime.turnId);
            if (current == null) current = scenarioPresentationRuntime;
            if (current == null) return;

            RuntimeContentStore contentReader = RuntimeContentStore.Current;
            IScenarioHostEffectPort port = dialogueManager == null || dialogueManager.progressionStore == null || contentReader == null
                ? null
                : dialogueManager.progressionStore.CreateScenarioHostEffectPort(contentReader);
            ScenarioHostEffectResult result = ScenarioHostEffectExecutor.Apply(port, request);
            PresentScenarioContinuation(scenarioPresentation.Advance(new ScenarioEvent
            {
                kind = "host_effect_result", runId = effect.runId, stepId = effect.stepId, hostEffectResult = result
            }, current), responseId);
        }

        private void RequestScenarioActivity(ScenarioEffect effect, string responseId)
        {
            if (effect == null || (effect.activityRequest == null && effect.activitySequenceRequest == null) ||
                scenarioPresentation == null || scenarioPresentationRuntime == null) return;
            ScenarioRuntimeSnapshot current = CurrentScenarioRuntime(scenarioPresentationRuntime.turnId);
            if (current == null) return;
            if (effect.activitySequenceRequest != null)
            {
                ActivitySequenceRequest original = effect.activitySequenceRequest;
                var sequenceRequest = new ActivitySequenceRequest(original.definition, original.actionDefinitionRef,
                    original.scenarioRunId, original.flowInvocationId, original.stepExecutionId,
                    original.actionInvocationId, current, original.arguments, effect.stepId);
                var sequenceActionContext = new ScenarioRuntimeSnapshot(current.scenarioId, current.runId, current.turnId,
                    current.contextRevision, effect.stepId, current.knownTopicIds, current.allowedExpressions,
                    current.capabilities, current.objects, current.waitConditions, current.knownIntentIds,
                    current.hostFacts.ToDictionary(item => item.Key, item => item.Value));
                ActivityRequestResult sequenceResult = activityBridge == null
                    ? ActivityRequestResult.Unavailable("activity_host_unavailable")
                    : activityBridge.TryRequestSequence(sequenceRequest);
                scenarioPresentationRuntime = sequenceActionContext;
                PresentScenarioContinuation(scenarioPresentation.Advance(new ScenarioEvent
                {
                    kind = sequenceResult.status == "accepted" ? "activity_accepted" : "activity_result",
                    runId = effect.runId, stepId = effect.stepId, activityId = original.actionInvocationId,
                    success = sequenceResult.status == "accepted"
                }, sequenceActionContext), responseId);
                return;
            }
            var actionContext = new ScenarioRuntimeSnapshot(current.scenarioId, current.runId, current.turnId,
                current.contextRevision, effect.activityRequest.stepId, current.knownTopicIds, current.allowedExpressions,
                current.capabilities, current.objects, current.waitConditions, current.knownIntentIds, current.hostFacts.ToDictionary(item => item.Key, item => item.Value));
            ActivityRequestResult result = activityBridge == null
                ? ActivityRequestResult.Unavailable("activity_host_unavailable")
                : activityBridge.TryRequest(effect.activityRequest, actionContext);
            scenarioPresentationRuntime = actionContext;
            PresentScenarioContinuation(scenarioPresentation.Advance(new ScenarioEvent
            {
                kind = "activity_result", runId = effect.runId, stepId = effect.stepId,
                activityId = result.activityId, success = result.status == "accepted"
            }, actionContext), responseId);
        }

        private void PumpScenarioActivityEvents()
        {
            if (scenarioPresentation == null || scenarioPresentationRuntime == null || activityBridge == null) return;
            ScenarioInstanceView running = scenarioPresentation.Running;
            if (running == null || running.status != "running") return;
            ScenarioStep currentStep = running.definition == null || running.definition.steps == null ? null :
                running.definition.steps.FirstOrDefault(item => item != null && item.id == running.currentStepId);
            if (currentStep == null || currentStep.kind != "wait" &&
                !(currentStep.kind == "action" && currentStep.activitySequence != null)) return;
            foreach (ScenarioEvent activityEvent in activityBridge.DrainScenarioEvents(running, scenarioPresentation.Suspended))
            {
                ScenarioRuntimeSnapshot current = CurrentScenarioRuntime(scenarioPresentationRuntime.turnId);
                PresentScenarioContinuation(scenarioPresentation.Advance(activityEvent, current), scenarioTranscriptResponseId);
            }
        }

        private IReadOnlyList<ScenarioCapabilitySnapshot> CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects)
        {
            if (activityBridge == null) activityBridge = new ActivityInteractionBridge(FindObjectOfType<ActivitySceneHost>());
            return activityBridge.CaptureCapabilities(out objects);
        }

        private void PresentScenarioStep(ScenarioStep step, string runId, string responseId, bool ask)
        {
            if (dialogueManager == null) return;
            var sequence = new DialogueSequence();
            sequence.metadata.mode = "cinematic";
            sequence.metadata.topic = "Generated conversation";
            IEnumerable<ScenarioBeat> beats = ask ? (IEnumerable<ScenarioBeat>)new[] { step.question } : step.beats;
            var beatByNodeId = new Dictionary<string, ScenarioBeat>();
            foreach (ScenarioBeat beat in beats)
            {
                if (beat == null) continue;
                string nodeId = (step.id + "_" + beat.id);
                if (nodeId.Length > 64) nodeId = beat.id;
                beatByNodeId[nodeId] = beat;
                var node = new DialogueNode
                {
                    id = nodeId, speaker = string.IsNullOrEmpty(beat.speaker) ? "altera" : beat.speaker,
                    text = beat.text, translatedText = beat.displayText,
                    language = beat.language, emotion = beat.expression, voice = beat.voice, audioPath = beat.audioKey,
                    presentation = new DialoguePresentationCue
                    { expression = beat.expression, animation = new DialogueAnimationCue { state = beat.animation } }
                };
                if (ask && scenarioPresentation != null)
                {
                    foreach (ScenarioPresentationAnswerOption option in scenarioPresentation.Current.answerOptions)
                        node.choices.Add(new DialogueChoice { label = option.label, action = "scenario_answer", value = option.stableId });
                    if (scenarioPresentation.Current.acceptsFreeText)
                        node.choices.Add(new DialogueChoice { label = "I'll say something else", action = "scenario_free_text" });
                }
                sequence.nodes.Add(node);
            }
            if (sequence.nodes.Count == 0) return;
            ScenarioConversationPresentationModel presentation = scenarioPresentation;
            if (authoredActivityScenario != null)
            {
                dialogueManager.StartValidatedSequence(sequence, null,
                    ask ? (Action)null : () => PresentScenarioContinuation(presentation.CompleteDialogue(runId, step.id), responseId),
                    ask ? (Func<DialogueNode, bool>)null : node => node != null && beatByNodeId.TryGetValue(node.id, out ScenarioBeat beat) &&
                        presentation.AcknowledgeDialogueBeat(step.id, beat.id));
                return;
            }
            var durablyPresentedNodes = new HashSet<string>(StringComparer.Ordinal);
            dialogueManager.StartValidatedSequence(sequence,
                    node =>
                    {
                        ConversationAppendResult saved = coordinator?.AcknowledgePresented(sessionId, responseId, node.id,
                            "generated", "direct", "", runId, "Altera", node.text, node.translatedText, node.GetExpressionCue());
                        if (saved != null && saved.Status != ConversationAppendStatus.Unsaved)
                            durablyPresentedNodes.Add(node.id);
                    },
                ask ? (Action)null : () => PresentScenarioContinuation(
                    presentation?.CompleteDialogue(runId, step.id), responseId),
                    ask ? (Func<DialogueNode, bool>)null : node =>
                    {
                        if (node == null || !beatByNodeId.TryGetValue(node.id, out ScenarioBeat acknowledgedBeat)) return false;
                        if (!durablyPresentedNodes.Contains(node.id))
                        {
                            ConversationAppendResult saved = coordinator?.AcknowledgePresented(sessionId, responseId, node.id,
                                "generated", "direct", "", runId, "Altera", node.text, node.translatedText, node.GetExpressionCue());
                            if (saved == null || saved.Status == ConversationAppendStatus.Unsaved) return false;
                            durablyPresentedNodes.Add(node.id);
                        }
                        return presentation != null && presentation.AcknowledgeDialogueBeat(step.id, acknowledgedBeat.id);
                    });
            if (ask && scenarioPresentation != null && scenarioPresentation.Current.acceptsFreeText)
                dialogueManager.OpenScenarioAnswerInput(Submit);
        }

        private ScenarioRuntimeSnapshot CurrentScenarioRuntime(string turnId)
        {
            if (scenarioPresentationRuntime == null || (activeScenarioSnapshot == null && authoredActivityScenario == null)) return null;
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            string[] availableTopics = authoredActivityScenario != null ? new string[0] :
                AvailableTopicIds(CreateContext(nowUtc), nowUtc, scenarioPresentationRuntime.knownTopicIds).ToArray();
            ISet<string> expressions = AllowedExpressions();
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects);
            return new ScenarioRuntimeSnapshot(scenarioPresentationRuntime.scenarioId,
                scenarioPresentationRuntime.runId, turnId, ScenarioContextRevision(availableTopics, expressions, capabilities),
                scenarioPresentationRuntime.checkpointStepId, availableTopics,
                expressions, capabilities, objects,
                capabilities.Count == 0 ? new string[0] : new[] { ActivityConditionIds.Terminal },
                scenarioPresentationRuntime.knownIntentIds,
                scenarioPresentationRuntime.hostFacts.ToDictionary(item => item.Key, item => item.Value));
        }

        private IEnumerable<string> AvailableTopicIds(TopicContext context, DateTimeOffset nowUtc,
            IEnumerable<string> candidateTopicIds)
        {
            foreach (string topicId in (candidateTopicIds ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).Take(8))
            {
                TopicDefinition definition;
                TopicInstance instance;
                if (!topicCatalog.Catalogue.TryGetValue(topicId, out definition) || definition == null ||
                    !topicCatalog.AuthoredInstances.TryGetValue(topicId, out instance) || instance == null) continue;
                if (TopicScenarioSelector.SelectForConversation(definition, instance, context, topicHistory, nowUtc, 0d, "direct").SelectedScenario != null)
                    yield return topicId;
            }
        }

        private static long ScenarioContextRevision(IEnumerable<string> topics, IEnumerable<string> expressions,
            IEnumerable<ScenarioCapabilitySnapshot> capabilities = null)
        {
            string canonical = string.Join("\n", (topics ?? Enumerable.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal)) + "\n--\n" +
                string.Join("\n", (expressions ?? Enumerable.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal)) + "\n--\n" +
                string.Join("\n", (capabilities ?? Enumerable.Empty<ScenarioCapabilitySnapshot>())
                    .Where(item => item != null).Select(item => item.capabilityId).OrderBy(value => value, StringComparer.Ordinal));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                return BitConverter.ToInt64(digest, 0) & long.MaxValue;
            }
        }

        public void Present(ConversationPresentation presentation)
        {
            if (dialogueManager == null || presentation == null || presentation.plan == null) return;
            ConversationPlan plan = presentation.plan;
            RoutingDiagnostics.Select(plan.topicId, plan.scenarioId, plan.outcome);
            RecordRoutingDiagnostic("selected", plan.topicId + " / " + plan.scenarioId + " / " + plan.outcome);
            if (plan.outcome == "authored")
            {
                PresentAuthored(plan, presentation.responseId);
                return;
            }
            if (plan.outcome != "generated" && plan.outcome != "clarify") return;
            if (plan.reply == null || plan.reply.nodes == null || plan.reply.nodes.Count == 0) return;

            activeResponseId = presentation.responseId;
            activeTopicId = plan.topicId ?? string.Empty;
            activePlayback = null;
            DialogueSequence sequence = ConvertGeneratedReply(plan.reply, plan.suggestedReplies, presentation.responseId);
            dialogueManager.StartValidatedSequence(sequence, node => AcknowledgePresented(
                presentation.responseId, node, "generated", "", ""), () => { });
        }

        public void Status(string stableCode, bool unsaved)
        {
            SetStatus(unsaved ? "transcript_unsaved" : stableCode);
        }

        public void Diagnostic(string stage, string code)
        {
            RecordRoutingDiagnostic(stage, code);
            if (stage == "route")
                RecordInteraction(new InteractionObservation
                {
                    kind = "routing_decision", metadata = new Dictionary<string, string>
                    {
                        { "routeId", code ?? "unknown" }, { "version", "direct-conversation-v4" }, { "reason", code ?? "unknown" }
                    }, occurredAtUtc = DateTime.UtcNow.ToString("O")
                });
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[AlterOS Agent] stage=" + SafeDiagnosticToken(stage) + " code=" + SafeDiagnosticToken(code));
#endif
        }

        /// <summary>Caller registers this only after presenting the explicit recording opt-in.</summary>
        public void RegisterInteractionRecorder(InteractionRecorder recorder)
        {
            interactionRecorder = recorder;
            if (scenarioPresentation != null) scenarioPresentation.RecordingObserver = RecordInteraction;
        }

        private void RecordInteraction(InteractionObservation observation)
        {
            try { interactionRecorder?.Record(observation); }
            catch { /* Optional local recording never blocks conversation. */ }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static string SafeDiagnosticToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            int length = Math.Min(value.Length, 40);
            var safe = new char[length];
            for (int index = 0; index < length; index++)
            {
                char character = value[index];
                safe[index] = char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_';
            }
            return new string(safe);
        }
#endif

        private void PresentAuthored(ConversationPlan plan, string responseId)
        {
            TopicDefinition definition;
            TopicInstance instance;
            if (!topicCatalog.Catalogue.TryGetValue(plan.topicId, out definition) ||
                !topicCatalog.AuthoredInstances.TryGetValue(plan.topicId, out instance) || definition == null || instance == null)
            {
                throw new InvalidOperationException("authored_unavailable");
            }
            TopicScenario scenario = definition.scenarios.Find(item => item != null && item.scenarioId == plan.scenarioId);
            if (scenario == null) throw new InvalidOperationException("authored_unavailable");
            if (scenario.scenarioRef != null)
            {
                PresentFlowBackedTopicScenario(plan, responseId, definition, instance, scenario);
                return;
            }
            try
            {
                TopicPlaybackHandle handle = topicHistory.BeginPlayback(definition, instance, scenario.scenarioId, DateTimeOffset.UtcNow);
                activeResponseId = responseId;
                activeTopicId = definition.topicId;
                activePlayback = handle;
                SaveTopicHistory();
                DialogueSequence sequence = ConvertAuthoredScenario(scenario);
                dialogueManager.StartValidatedSequence(sequence,
                    node => AcknowledgeAuthoredNode(handle, responseId, definition, instance, node),
                    () => CompleteAuthoredScenario(handle));
            }
            catch (Exception) { throw new InvalidOperationException("authored_unavailable"); }
        }

        private void PresentFlowBackedTopicScenario(ConversationPlan plan, string responseId,
            TopicDefinition definition, TopicInstance instance, TopicScenario scenario)
        {
            ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!opened.IsReady) throw new InvalidOperationException("content_release_unavailable");
            RuntimeContentStore store = opened.value;
            ContentReleaseDependency[] dependencies = (store.Release.dependencies ?? new List<ContentReleaseDependency>())
                .Where(item => item != null && item.kind == "topic" && item.id == definition.topicId &&
                    item.store == store.Repository.StoreId).ToArray();
            if (dependencies.Length != 1) throw new InvalidOperationException("topic_entry_unavailable");
            ContentReleaseDependency dependency = dependencies[0];
            var topicRef = new ContentRef(dependency.store, dependency.id, dependency.revision, dependency.expectedHash);
            ContentResult<TopicRecord> topic = store.ReadTopic(topicRef);
            TopicEntryRecord[] entries = topic.IsReady
                ? (topic.value.entries ?? new List<TopicEntryRecord>()).Where(item => item != null && item.id == scenario.scenarioId).ToArray()
                : new TopicEntryRecord[0];
            if (entries.Length != 1 || !SameContentReference(store, entries[0].flowRef, scenario.scenarioRef))
                throw new InvalidOperationException("topic_entry_unavailable");

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            TopicContext topicContext = CreateContext(nowUtc);
            string runId = "topic-run-" + Guid.NewGuid().ToString("N");
            string[] availableTopics = AvailableTopicIds(topicContext, nowUtc, topicCatalog.Catalogue.Keys).ToArray();
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects);
            ISet<string> expressions = AllowedExpressions();
            var scenarioContext = new ScenarioRuntimeSnapshot(definition.topicId, runId, responseId,
                ScenarioContextRevision(availableTopics, expressions, capabilities), string.Empty, availableTopics,
                expressions, capabilities, objects,
                capabilities.Count == 0 ? new string[0] : new[] { ActivityConditionIds.Terminal },
                new string[0], topicContext.facts == null ? null : topicContext.facts.ToDictionary(item => item.Key, item => item.Value));
            ContentHostVocabulary vocabulary = CreateRuntimeContentVocabulary(topicContext, capabilities, expressions);
            ContentResult<ContentExecutionPlan> prepared = store.PrepareTopicEntry(topicRef, scenario.scenarioId,
                new Dictionary<string, FlowValue>(StringComparer.Ordinal), scenarioContext, vocabulary, "en", "ja");
            if (!prepared.IsReady)
            {
                foreach (ContentDiagnostic diagnostic in prepared.diagnostics) Diagnostic("topic_prepare", diagnostic.code);
                throw new InvalidOperationException("topic_entry_unavailable");
            }

            scenarioPresentation = CreateScenarioPresentationModel();
            scenarioPresentationRuntime = scenarioContext;
            activeScenarioSnapshot = activeConversationSnapshot;
            scenarioTranscriptResponseId = responseId;
            IReadOnlyList<ScenarioEffect> effects = scenarioPresentation.Start(prepared.value, runId, scenarioContext);
            ScenarioInstanceView running = scenarioPresentation.Running;
            if (running == null || effects.Any(item => item != null &&
                (item.type == "scenario_start_rejected" || item.type == "scenario_flow_failed")))
            {
                scenarioPresentation = null;
                scenarioPresentationRuntime = null;
                throw new InvalidOperationException("topic_entry_unavailable");
            }
            try
            {
                activePlayback = topicHistory.BeginFlowPlayback(definition, instance, scenario.scenarioId,
                    runId, running.currentInvocationId, nowUtc);
                SaveTopicHistory();
                activeResponseId = responseId;
                activeTopicId = definition.topicId;
                PresentScenarioContinuation(effects, responseId);
            }
            catch (Exception)
            {
                scenarioPresentation.WrapUp(scenarioContext);
                scenarioPresentation = null;
                scenarioPresentationRuntime = null;
                throw new InvalidOperationException("topic_entry_unavailable");
            }
        }

        private ContentHostVocabulary CreateRuntimeContentVocabulary(TopicContext context,
            IEnumerable<ScenarioCapabilitySnapshot> capabilities, IEnumerable<string> expressions)
        {
            if (runtimeSequenceCatalog == null)
            {
                runtimeSequenceCatalog = ActivitySequenceCatalogFactory.CreateDefault();
                runtimeSequenceBuilder = runtimeSequenceCatalog.CreateBuilder();
            }
            ActivitySequenceCatalog sequenceCatalog = runtimeSequenceCatalog;
            IEnumerable<string> actionIds = (capabilities ?? Enumerable.Empty<ScenarioCapabilitySnapshot>())
                .Where(item => item != null).Select(item => item.capabilityId)
                .Concat(sequenceCatalog.Primitives.All.Select(item => item.requiredCapabilityId))
                .Concat(ContentHostEffectFunctionRegistry.RegisteredCapabilityIds)
                .Concat(new[] { ActivityCapabilityIds.ActivitySequence, ActivityCapabilityIds.ComeHere,
                    ActivityCapabilityIds.FollowPlayer, ActivityCapabilityIds.DrinkWater });
            IEnumerable<string> enumValues = sequenceCatalog.Primitives.All.SelectMany(item => item.arguments)
                .SelectMany(item => item.allowedValues)
                .Concat(sequenceCatalog.Primitives.All.SelectMany(item => item.results).SelectMany(item => item.allowedValues))
                .Concat(Enum.GetNames(typeof(FurniturePowerState)))
                .Concat(new[] { FurnitureFunctionStatuses.Applied, FurnitureFunctionStatuses.NoChange,
                    FurnitureFunctionStatuses.Replayed, FurnitureFunctionStatuses.Unavailable,
                    FurnitureFunctionStatuses.Conflict, FurnitureFunctionStatuses.Failed,
                    FurniturePresentationStatuses.Completed, FurniturePresentationStatuses.Cancelled });
            IEnumerable<string> contextFacts = context == null || context.facts == null
                ? Enumerable.Empty<string>() : context.facts.Keys;
            var vocabulary = new ContentHostVocabulary(actionIds, expressions, enumValues, contextFacts,
                topicCatalog == null ? Enumerable.Empty<string>() : topicCatalog.Catalogue.Keys,
                runtimeSequenceBuilder);
            if (runtimeVocabulary == null || !runtimeVocabulary.HasSameRegistrationsAs(vocabulary))
                runtimeVocabulary = vocabulary;
            return runtimeVocabulary;
        }

        private Task<ContentResult<ResolvedContentRelease>> EnsureContentWarmup()
        {
            var opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!opened.IsReady)
                return Task.FromResult(ContentResult<ResolvedContentRelease>.Failure(opened.status,
                    "content_release_unavailable", "", "The installed content release could not open."));
            var capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] ignored);
            ContentHostVocabulary vocabulary = CreateRuntimeContentVocabulary(CreateContext(DateTimeOffset.UtcNow),
                capabilities, AllowedExpressions());
            if (contentWarmup == null || !ReferenceEquals(warmingVocabulary, vocabulary))
            {
                warmingVocabulary = vocabulary;
                // Pure SQL/validation work only. Capture all Unity-owned vocabulary on the main thread.
                contentWarmup = Task.Run(() => opened.value.WarmExecutionRelease(vocabulary));
            }
            return contentWarmup;
        }

        private static bool SameContentReference(RuntimeContentStore store, ContentRef left, ContentRef right)
        {
            if (store == null || left == null || right == null || left.store != right.store ||
                left.id != right.id || left.revision != right.revision) return false;
            // SQL may omit an optional hash that the source projection materializes.
            // Resolve BOTH references against the pinned release; a supplied wrong hash must still fail closed.
            return store.ReadFlow(left).IsReady && store.ReadFlow(right).IsReady;
        }

        private void AcknowledgeAuthoredNode(TopicPlaybackHandle handle, string responseId,
            TopicDefinition definition, TopicInstance instance, DialogueNode node)
        {
            TopicPlaybackResult report = topicHistory.ReportPresentedNode(handle, node.id, DateTimeOffset.UtcNow);
            if (report.Status != "Rejected")
            {
                AcknowledgePresented(responseId, node, "authored", definition.topicId, handle.ScenarioId);
                SaveTopicHistory();
            }
        }

        private void CompleteAuthoredScenario(TopicPlaybackHandle handle)
        {
            topicHistory.Complete(handle, DateTimeOffset.UtcNow);
            SaveTopicHistory();
            activePlayback = null;
            activeTopicId = string.Empty;
            activeResponseId = string.Empty;
        }

        private void AcknowledgePresented(string responseId, DialogueNode node, string source, string topicId, string scenarioId)
        {
            if (node == null || transcript == null) return;
            coordinator.AcknowledgePresented(sessionId, responseId, node.id ?? string.Empty, source, "direct",
                topicId, scenarioId, node.speaker ?? "Altera", node.text ?? string.Empty,
                string.IsNullOrEmpty(node.translatedText) ? node.text ?? string.Empty : node.translatedText,
                node.GetExpressionCue() ?? string.Empty);
        }

        private DialogueSequence ConvertAuthoredScenario(TopicScenario scenario)
        {
            var sequence = new DialogueSequence();
            sequence.metadata.mode = "cinematic";
            sequence.metadata.topic = scenario.scenarioId;
            foreach (CanonicalDialogueNode canonical in scenario.nodes)
            {
                var node = new DialogueNode
                {
                    id = canonical.id, speaker = "altera", text = canonical.text,
                    translatedText = canonical.displayText, language = "japanese", nextId = canonical.nextId,
                    presentation = new DialoguePresentationCue { expression = canonical.presentation != null ? canonical.presentation.expression : string.Empty }
                };
                if (canonical.choices != null)
                    foreach (CanonicalDialogueChoice choice in canonical.choices)
                        node.choices.Add(new DialogueChoice { label = choice.label, nextId = choice.nextId });
                sequence.nodes.Add(node);
            }
            return sequence;
        }

        private DialogueSequence ConvertGeneratedReply(ConversationReply reply,
            IList<SuggestedPlayerReply> suggestions, string responseId)
        {
            var sequence = new DialogueSequence();
            sequence.metadata.mode = "cinematic";
            sequence.metadata.topic = "Generated conversation";
            for (int index = 0; index < reply.nodes.Count; index++)
            {
                CanonicalDialogueNode canonical = reply.nodes[index];
                var node = new DialogueNode
                {
                    id = canonical.id, speaker = "altera", text = canonical.text,
                    translatedText = canonical.displayText, language = "japanese",
                    emotion = canonical.presentation != null ? canonical.presentation.expression : string.Empty,
                    presentation = new DialoguePresentationCue { expression = canonical.presentation != null ? canonical.presentation.expression : string.Empty },
                    nextId = canonical.nextId
                };
                if (index == reply.nodes.Count - 1 && suggestions != null)
                    foreach (SuggestedPlayerReply suggestion in suggestions)
                        node.choices.Add(new DialogueChoice
                        {
                            label = suggestion.label, action = "conversation_suggestion",
                            conversationPrompt = suggestion.prompt, conversationResponseId = responseId
                        });
                sequence.nodes.Add(node);
            }
            return sequence;
        }

        private ConversationRequest NewRequest(string text, string requestId)
        {
            return new ConversationRequest
            {
                schemaVersion = 1, requestId = requestId, sessionId = sessionId, channel = "direct", text = text,
                activeResponseId = activeResponseId, activeTopicId = activeTopicId
            };
        }

        private static TopicContext CreateContext(DateTimeOffset nowUtc)
        {
            return new TopicContext { localHour = DateTime.Now.Hour, objectsComplete = false };
        }

        private IReadOnlyList<ConversationPresentedUtteranceV2> ToPresentedHistory(ConversationHistorySnapshot recent)
        {
            var history = new List<ConversationPresentedUtteranceV2>();
            foreach (ConversationHistoryUtterance item in recent.utterances)
                history.Add(new ConversationPresentedUtteranceV2(item.speaker, item.source, item.responseId, item.nodeId, item.text, item.sourceId));
            return history;
        }

        private ISet<string> AllowedExpressions()
        {
            var expressions = new HashSet<string>(StringComparer.Ordinal);
            AlteraEmotionController controller = dialogueManager != null ? dialogueManager.emotionController : null;
            if (controller != null && controller.expressionPresets != null)
                foreach (AlteraExpressionPreset preset in controller.expressionPresets)
                    if (preset != null && !string.IsNullOrWhiteSpace(preset.expressionName)) expressions.Add(preset.expressionName);
            return expressions;
        }

        private void SaveTopicHistory()
        {
            if (topicHistory == null || !topicHistory.IsDirty) return;
            if (topicHistoryStore == null) { ready = false; SetStatus("topic_history_unsaved"); return; }
            TopicHistoryStoreSaveResult result = topicHistoryStore.Save(topicHistory, topicHistory.StorageRevision, Guid.NewGuid().ToString("D"));
            if (!result.Success)
            {
                ready = false;
                SetStatus("topic_history_unsaved");
            }
        }

        private void SetStatus(string code)
        {
            RecordRoutingDiagnostic("status", code);
            if (statusText != null) statusText.text = DisplayStatus(code);
        }

        private static string DisplayStatus(string code)
        {
            switch (code)
            {
                case "online_enabled": return "Generated replies enabled; provider use may incur cost.";
                case "online_disabled": return "Generated replies are off. Authored Topics remain available.";
                case "provider_auth": case "credential_missing": return "Add an OpenRouter key with P, or keep generated replies off.";
                case "provider_rate_limited": return "The provider is busy. Your authored Topics still work.";
                case "provider_payment_required": return "The provider rejected this request. Check your OpenRouter account.";
                case "provider_timeout": return "That reply took too long. Try again or choose an authored Topic.";
                case "provider_unavailable": case "provider_refusal": case "invalid_reply": return "A generated reply was unavailable. Please try again.";
                case "late_reply_saved": return "A late reply was saved, but was not opened in this interaction.";
                case "transcript_unsaved": return "This exchange could not be safely saved. No reply was started.";
                case "catalogue_unavailable": case "catalogue_invalid": case "coverage_invalid": return "Conversation content is unavailable.";
                case "semantic_unavailable": return "Local Topic matching is unavailable right now.";
                case "topic_history_unavailable": case "topic_history_unsaved": return "Topic history could not be saved safely.";
                case "conversation_history_unavailable": return "Conversation history could not be saved safely.";
                case "transcript_unavailable": return "Conversation history needs recovery before new turns can start.";
                case "migration_unavailable": return "Conversation data is not ready. Restart or recover it before continuing.";
                case "authored_unavailable": return "That authored Topic is not available right now.";
                case "topic_entry_unavailable": case "content_release_unavailable":
                    return "That Topic could not be started. Try another Topic or cancel to return.";
                case "offline": return "Generated replies are off. Choose an authored Topic or try another request.";
                case "authored_match": return "Strong semantic match; using the authored Topic.";
                case "scenario_completed": return "That little story is complete.";
                case "scenario_return_prompt": return "We can pick up our earlier conversation when you are ready.";
                case "busy": return "One conversation is already in progress.";
                case "content_preparing": return "Preparing local dialogue content...";
                case "ready": case "online_reply": return "";
                default: return string.IsNullOrWhiteSpace(code) ? "" : "Conversation is unavailable right now.";
            }
        }

        private string ResolveHelperPath()
        {
            string candidate;
#if UNITY_EDITOR
            if (!enableEditorDevelopmentHelper) return null;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            candidate = Path.Combine(projectRoot, "Build", "CompanionRuntimeV4", "AlterOSCompanion", "AlterOSCompanion.exe");
            if (!IsWithin(candidate, Path.Combine(projectRoot, "Build"))) return null;
#else
            string playerRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(playerRoot)) return null;
            candidate = Path.Combine(playerRoot, "CompanionRuntime", "AlterOSCompanion", "AlterOSCompanion.exe");
            if (!IsWithin(candidate, Path.Combine(playerRoot, "CompanionRuntime"))) return null;
#endif
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        private static bool IsWithin(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private void OnCredentialsChanged()
        {
            if (processClient == null || !onlineEnabled) return;
            if (IsTurnInFlight && !string.IsNullOrWhiteSpace(OpenRouterCredentialStore.GetApiKey())) credentialRefreshPending = true;
            else RefreshCredentialAsync();
        }

        private async void RefreshCredentialAsync()
        {
            try { await processClient.RefreshCredentialAsync(CancellationToken.None); }
            catch (Exception) { SetStatus("provider_unavailable"); }
        }

        private async void StopHelperAsync()
        {
            try { await processClient.StopAsync(); }
            catch (Exception) { SetStatus("provider_unavailable"); }
        }
    }
}
