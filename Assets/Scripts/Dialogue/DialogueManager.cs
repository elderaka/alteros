using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using AlterOS.Dialogue;
using AlterOS.Dialogue.UI;
using AlterOS.Content;
using AlterOS.Progression;
using AlterOS.Scenarios;
using AlterOS.Semantics;
using AlterOS.Conversation;
using AlterOS.Tts;
using AlterOS.UI;
using AlterOS;
using Pleiades.Player;
using UnityEngine.EventSystems;

/// <summary>
/// Core dialogue sequencer for AlterOS.
/// Handles: JSON parsing, typewriter text display, emotion routing,
/// TTS audio queue with background pre-fetching, and player choices.
/// 
/// Attach to a UI Canvas GameObject. Wire up the UI references in the Inspector.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The TextMeshPro text element for the dialogue box.")]
    public TextMeshProUGUI dialogueText;

    [Tooltip("The speaker name label.")]
    public TextMeshProUGUI speakerNameText;

    [Tooltip("Parent panel for the entire dialogue box. Hidden when inactive.")]
    public GameObject dialoguePanel;

    [Tooltip("Container for player choice buttons.")]
    public Transform choiceContainer;

    [Tooltip("Prefab for a single choice button. Must have a Button + TextMeshProUGUI child.")]
    public GameObject choiceButtonPrefab;

    [Tooltip("Optional world-space presenter for physically placed dialogue choices near Altera.")]
    public WorldDialogueChoicePresenter worldChoicePresenter;

    [Tooltip("If true and World Choice Presenter is assigned, choices appear in world space instead of the screen-space choice container.")]
    public bool useWorldSpaceChoices = true;

    [Tooltip("Optional camera director for animated dialogue camera blends.")]
    public DialogueCameraDirector dialogueCameraDirector;

    [Tooltip("Optional styled/animated view controller for the dialogue panel.")]
    public AlterOSDialoguePanelView dialoguePanelView;

    [Tooltip("Optional HUD shell. DialogueManager hides/restores room HUD around dialogue.")]
    public AlterOSHudShell hudShell;

    [Tooltip("Optional 'click to continue' indicator.")]
    public GameObject continueIndicator;

    [Tooltip("Optional toggle button shown on the dialogue panel for skipping TTS.")]
    public Button skipTtsToggleButton;

    [Tooltip("Optional label for the Skip TTS toggle button.")]
    public TextMeshProUGUI skipTtsToggleLabel;

    [Header("Typewriter Settings")]
    [Tooltip("Characters per second for the typewriter effect.")]
    public float typewriterSpeed = 30f;

    [Tooltip("If true, sync typewriter speed to audio duration instead of fixed CPS.")]
    public bool syncTypewriterToAudio = true;

    [Header("Dialogue Mode")]
    [Tooltip("The Player Controller, used to lock movement during dialogue.")]
    public PlayerController playerController;

    [Tooltip("Altera's root transform, optionally used for cinematic facing.")]
    public Transform alteraTransform;

    [Tooltip("Enable the older cinematic face-focus behavior during dialogue.")]
    public bool useCinematicDialogueFocus = false;

    [Tooltip("If cinematic focus is enabled, Altera rotates toward the player during dialogue.")]
    public bool rotateAlteraDuringDialogue = false;

    [Tooltip("FOV to zoom to during dialogue when cinematic focus is enabled.")]
    [Range(10f, 60f)] public float dialogueZoomFOV = 30f;

    [Tooltip("Speed of the cinematic camera rotation and zoom when cinematic focus is enabled.")]
    public float cinematicSmoothness = 8f;

    [Header("Topic Input")]
    [Tooltip("Dialogue router used for local sequence topic choices.")]
    public DialogueRouter dialogueRouter;

    [Tooltip("Optional unified conversation bridge for free-text turns. When present, it owns routing and provider choice.")]
    public AlterOS.Conversation.DirectConversationBridge directConversationBridge;

    [Tooltip("Runtime catalog that injects authored/generated topic options into eligible dialogue nodes.")]
    public TopicOptionCatalog topicOptionCatalog;

    [Tooltip("Centered panel shown when the player chooses a custom topic.")]
    public GameObject topicInputPanel;

    [Tooltip("Input field for 'I want to talk about ____'.")]
    public TMP_InputField topicInputField;

    [Tooltip("Optional submit button for the topic input panel.")]
    public Button topicSubmitButton;

    [Tooltip("Optional cancel button for the topic input panel.")]
    public Button topicCancelButton;

    [Tooltip("Fallback placeholder text for the topic input field.")]
    public string topicInputPlaceholder = "I want to talk about...";

    [Header("Conversation Mode")]
    [Tooltip("Current free-text conversation mode. Topic is the only available mode until the agent harness is connected.")]
    [SerializeField] private ConversationMode conversationMode = ConversationModePolicy.Default;

    public ConversationMode CurrentConversationMode => conversationMode;

    /// <summary>
    /// Agent mode is intentionally unavailable until a provider-neutral agent harness exists.
    /// Keep the capability check here so a future UI cannot accidentally bypass it.
    /// </summary>
    public bool AgentModeAvailable => false;

    public bool TrySetConversationMode(ConversationMode mode)
    {
        if (!ConversationModePolicy.CanSelect(mode, AgentModeAvailable))
        {
            Debug.LogWarning("[DialogueManager] Agent conversation mode is not available yet.");
            return false;
        }

        conversationMode = mode;
        return true;
    }

    [Header("Semantic Topic Routing")]
    [Tooltip("Minimum best-anchor similarity required before free text starts authored dialogue.")]
    [Range(0f, 1f)] public float semanticMinimumSimilarity = TopicRoutePolicy.DefaultMinimumSimilarity;

    [Tooltip("Maximum unscaled seconds to wait for the local semantic model to warm up.")]
    public float semanticRouteTimeoutSeconds = 60f;

    [Tooltip("Authored route played when free text is uncertain or cannot be loaded.")]
    public string semanticFallbackRouteId = TopicRoutePolicy.DefaultFallbackRouteId;

    [Header("Language Settings")]
    [Tooltip("Fallback voice language if none is specified in JSON.")]
    public string defaultVoiceLanguage = "japanese";

    [Tooltip("Fallback text translation language. Set to 'english' if you want translated subtitles.")]
    public string defaultTextLanguage = "english";

    [Header("TTS Settings")]
    [Tooltip("How many nodes ahead to pre-fetch TTS audio.")]
    public int prefetchAhead = 4;

    [Tooltip("Whether cloud-generated TTS should be cached to the pruneable runtime folder StreamingAssets/TTSCache.")]
    public bool cacheCloudTtsToDisk = true;

    [Tooltip("Soft cap for the runtime TTS cache in megabytes. Prebaked/default dialogue in StreamingAssets/TTSPrebaked is never pruned.")]
    public int maxTtsCacheMegabytes = 512;

    [Tooltip("Delay between in-dialogue TTS retry attempts when a node has not finished warming yet.")]
    public float ttsRetryDelaySeconds = 1.25f;

    [Tooltip("If enabled, dialogue will advance as text-only without waiting for TTS.")]
    public bool skipDialogueTts = false;

    [Tooltip("Label shown when dialogue TTS is enabled.")]
    public string skipTtsOffLabel = "TTS: On";

    [Tooltip("Label shown when dialogue TTS is skipped.")]
    public string skipTtsOnLabel = "TTS: Off";

    [Tooltip("If enabled, play a cached-only acknowledgement voice clip when Skip TTS is on and the current line has no cached voice.")]
    public bool useCachedSkipTtsAcknowledgements = true;

    [Tooltip("Ambient-style filler pool used for cached-only Skip TTS acknowledgements.")]
    public string skipTtsAcknowledgementPoolPath = "Dialogue/filler/filler_skiptts_pool.json";

    [Header("Component References")]
    [Tooltip("The AlteraTTS component on the Altera GameObject.")]
    public TTSSystem alteraTTS;

    [Tooltip("The AlteraEmotionController on the Altera GameObject.")]
    public AlteraEmotionController emotionController;

    [Tooltip("Optional Animator used by dialogue presentation.animation cues.")]
    public Animator dialogueAnimator;

    [Tooltip("The AlteraAmbientBubble component for ambient spat testing.")]
    public AlteraAmbientBubble ambientBubble;

    [Tooltip("Optional effect applier for node-level stat changes and flags.")]
    public DialogueEffectApplier effectApplier;

    [Tooltip("Optional progression store used for node and choice trigger conditions.")]
    public AlteraProgressionStore progressionStore;

    [Header("Debug Testing")]
    [Tooltip("Path to a dialogue JSON file relative to StreamingAssets (e.g., 'Dialogue/greet/hello.json').")]
    public string testDialogueFile = "Dialogue/greet/greet_morning_first_01.json";

    [HideInInspector] public string[] availableTestFiles;
    [HideInInspector] public int selectedTestFileIndex = 0;

    // --- Internal State ---
    private DialogueSequence _currentSequence;
    private string _activeEffectScope;
    private bool _activeSequenceAllowsGameEffects;
    private Dictionary<string, DialogueNode> _nodeLookup = new Dictionary<string, DialogueNode>();
    private int _currentIndex = -1;
    private bool _isTyping = false;
    private bool _isWaitingForInput = false;
    private bool _skipRequested = false;
    private string _fullText = "";
    private Coroutine _typewriterCoroutine;
    private bool _isTopicInputOpen = false;
    private bool _externalControlLock = false;
    private int _activeNodeFetchVersion = 0;
    private Action<string> _textInputSubmitAction;
    private Action _textInputCancelAction;
    private bool _releaseControlOnInputSubmit = true;
    private Action _onSequenceCompleted;
    private Action<DialogueNode> _onValidatedNodePresented;
    private Func<DialogueNode, bool> _onValidatedNodeAcknowledged;
    private Action<DialogueNode> _stagedValidatedNodeCallback;
    private Func<DialogueNode, bool> _stagedValidatedNodeAcknowledgedCallback;
    private bool _forceTextOnlyForValidatedSequence;
    private bool _startValidatedTextOnly;
    private bool _stagedValidatedTextOnly;
    private Action _pendingSequenceExitCompletion;
    private bool _invokePendingSequenceExitCompletion;
    private Coroutine _semanticTopicCoroutine;
    private AlterOSHudMode _hudModeBeforeDialogue = AlterOSHudMode.Room;
    private bool _hasHudModeBeforeDialogue;
    public bool IsTtsServiceUnavailable { get; private set; }

    public bool IsTtsConfigured => alteraTTS != null && alteraTTS.HasOpenRouterApiKey;

    // --- Audio Queue ---
    // Pre-fetched audio clips indexed by node ID
    private Dictionary<string, AudioClip> _audioCache = new Dictionary<string, AudioClip>();
    private HashSet<string> _audioFetching = new HashSet<string>();
    private bool _isFetchingAudio = false;

    private void Awake()
    {
        ResetDialogueVisuals(true);
        RefreshSkipTtsButtonVisuals();

        HideDialoguePanelImmediate();

        if (topicInputPanel != null)
        {
            topicInputPanel.SetActive(false);
        }

        if (effectApplier == null)
        {
            effectApplier = GetComponent<DialogueEffectApplier>();
        }

        if (dialogueRouter == null)
        {
            dialogueRouter = GetComponent<DialogueRouter>();
        }

        if (dialogueRouter == null)
        {
            dialogueRouter = FindObjectOfType<DialogueRouter>();
        }

        if (directConversationBridge == null)
        {
            directConversationBridge = GetComponent<AlterOS.Conversation.DirectConversationBridge>();
        }

        if (topicOptionCatalog == null)
        {
            topicOptionCatalog = GetComponent<TopicOptionCatalog>();
        }

        if (topicOptionCatalog == null)
        {
            topicOptionCatalog = FindObjectOfType<TopicOptionCatalog>();
        }

        if (topicOptionCatalog == null)
        {
            topicOptionCatalog = gameObject.AddComponent<TopicOptionCatalog>();
        }

        if (effectApplier == null)
        {
            effectApplier = FindObjectOfType<DialogueEffectApplier>();
        }

        if (effectApplier == null)
        {
            if (GetComponent<AlteraProgressionStore>() == null && FindObjectOfType<AlteraProgressionStore>() == null)
            {
                gameObject.AddComponent<AlteraProgressionStore>();
            }

            effectApplier = gameObject.AddComponent<DialogueEffectApplier>();
        }

        if (progressionStore == null)
        {
            progressionStore = GetComponent<AlteraProgressionStore>();
        }

        if (progressionStore == null)
        {
            progressionStore = FindObjectOfType<AlteraProgressionStore>();
        }
    }

    // =============================================
    // PUBLIC API
    // =============================================

    public void RecordScenarioHistoryObservation(ScenarioHistoryObservation observation)
    {
        if (progressionStore != null) progressionStore.TryRecordScenarioHistoryObservation(observation);
    }

    /// <summary>
    /// Start a dialogue sequence from a JSON string.
    /// This public path treats content as untrusted and rejects mechanical effects.
    /// </summary>
    public void StartSequence(string json, Action onComplete = null)
    {
        StartSequenceInternal(DialogueSequence.FromJson(json), onComplete, false);
    }

    /// <summary>
    /// Start a dialogue sequence from a pre-loaded DialogueSequence object.
    /// </summary>
    public void StartSequence(DialogueSequence sequence, Action onComplete = null)
    {
        StartSequenceInternal(sequence, onComplete, false);
    }

    /// <summary>
    /// Start a canonical authored route after DialogueRouter has selected and validated its file-backed definition.
    /// Callers must not use this for caller-supplied objects or dynamic JSON.
    /// </summary>
    internal void StartAuthoredSequence(DialogueSequence sequence, Action onComplete = null)
    {
        StartSequenceInternal(sequence, onComplete, true);
    }

    private void StartSequenceInternal(DialogueSequence sequence, Action onComplete, bool authoredContent)
    {
        _onValidatedNodePresented = _startValidatedTextOnly ? _stagedValidatedNodeCallback : null;
        _onValidatedNodeAcknowledged = _startValidatedTextOnly ? _stagedValidatedNodeAcknowledgedCallback : null;
        bool textOnly = _startValidatedTextOnly && _stagedValidatedTextOnly;
        _forceTextOnlyForValidatedSequence = textOnly;
        _startValidatedTextOnly = false;
        _stagedValidatedNodeCallback = null;
        _stagedValidatedNodeAcknowledgedCallback = null;
        _stagedValidatedTextOnly = false;
        CloseTopicInput();
        EnterHudMode(AlterOSHudMode.Dialogue);
        if (ambientBubble != null) ambientBubble.StopSpat();
        ResetDialogueVisuals(true);
        _currentSequence = sequence;
        BuildNodeLookup();
        _currentIndex = -1;
        _activeNodeFetchVersion++;
        _onSequenceCompleted = onComplete;
        _activeEffectScope = Guid.NewGuid().ToString("D");
        _activeSequenceAllowsGameEffects = authoredContent;
        if (effectApplier != null && !effectApplier.TryApplySequence(
            EffectOperationId("sequence"), _currentSequence, _activeSequenceAllowsGameEffects))
        {
            Debug.LogWarning("[DialogueManager] Sequence effects were not committed; the sequence was not presented.");
            ClearActiveSequenceWithoutExitingDialogue();
            return;
        }

        ShowDialoguePanel();
        RefreshSkipTtsButtonVisuals();
        if (playerController != null)
        {
            Transform dialogueTarget = useCinematicDialogueFocus ? alteraTransform : null;
            float dialogueFov = dialogueCameraDirector != null ? dialogueCameraDirector.DialogueFov : useCinematicDialogueFocus ? dialogueZoomFOV : playerController.PlayerCamera != null ? playerController.PlayerCamera.fieldOfView : dialogueZoomFOV;
            float smoothness = useCinematicDialogueFocus ? cinematicSmoothness : 0f;
            playerController.SetDialogueMode(true, dialogueFov, dialogueTarget, smoothness);
        }
        if (dialogueCameraDirector != null)
        {
            if (playerController != null) playerController.SetExternalCameraControl(true);
            dialogueCameraDirector.EnterDialogue(alteraTransform, () =>
            {
                if (playerController != null) playerController.SetExternalCameraControl(false);
            });
        }

        PrefetchAudioBatch(0);
        AdvanceToNextNode();
    }

    public void StartValidatedSequence(DialogueSequence sequence, Action<DialogueNode> onNodePresented, Action onComplete = null,
        Func<DialogueNode, bool> onNodeAcknowledged = null)
    {
        _stagedValidatedNodeCallback = onNodePresented;
        _stagedValidatedNodeAcknowledgedCallback = onNodeAcknowledged;
        _stagedValidatedTextOnly = true;
        _startValidatedTextOnly = true;
        StartSequence(sequence, onComplete);
    }

    /// <summary>
    /// Feed a single dynamic node from a provider-neutral runtime response.
    /// Appends to the current sequence and immediately plays it.
    /// </summary>
    public void AppendAndPlayNode(DialogueNode node)
    {
        if (_currentSequence == null)
        {
            _currentSequence = new DialogueSequence();
            BuildNodeLookup();
            ShowDialoguePanel();
        }

        _currentSequence.nodes.Add(node);
        if (!string.IsNullOrEmpty(node.id)) _nodeLookup[node.id] = node;

        _currentIndex = _currentSequence.nodes.Count - 1;
        PresentNode(node, false);
    }

    public void SaveToArchive(string customTopic = "")
    {
        if (_currentSequence == null) return;

        // Ensure directory exists
        string archiveDir = Path.Combine(Application.streamingAssetsPath, "Archives");
        if (!Directory.Exists(archiveDir)) Directory.CreateDirectory(archiveDir);

        // Update metadata
        if (!string.IsNullOrEmpty(customTopic)) _currentSequence.metadata.topic = customTopic;
        _currentSequence.metadata.timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Save file
        string safeTopic = string.IsNullOrEmpty(_currentSequence.metadata.topic) ? "convo" : _currentSequence.metadata.topic.Replace(" ", "_");
        string filename = $"{_currentSequence.metadata.timestamp}_{safeTopic}.json";
        string fullPath = Path.Combine(archiveDir, filename);

        File.WriteAllText(fullPath, _currentSequence.ToJson());
        Debug.Log($"[DialogueManager] Conversation archived to: {fullPath}");
    }

    /// <summary>
    /// Checks if the dialogue system is currently active.
    /// </summary>
    public bool IsActive => _currentSequence != null && _currentIndex >= 0;
    public bool IsInputOpen => _isTopicInputOpen;
    public bool IsExternalControlLocked => _externalControlLock;
    public bool IsTyping => _isTyping;
    public bool IsWaitingForInput => _isWaitingForInput;
    public bool HasPendingSequenceExitCompletion => _pendingSequenceExitCompletion != null || _invokePendingSequenceExitCompletion;
    public bool BlocksPlayerControl => IsActive || _isTopicInputOpen || _semanticTopicCoroutine != null || _externalControlLock;

    public void SetExternalControlLock(bool locked)
    {
        _externalControlLock = locked;
        if (playerController == null)
        {
            return;
        }

        if (locked)
        {
            playerController.SetDialogueMode(true, playerController.PlayerCamera != null ? playerController.PlayerCamera.fieldOfView : dialogueZoomFOV, null, 0f);
        }
        else if (!IsActive && !_isTopicInputOpen)
        {
            playerController.SetDialogueMode(false);
        }
    }

    // =============================================
    // CORE SEQUENCING
    // =============================================

    private void BuildNodeLookup()
    {
        _nodeLookup.Clear();
        foreach (var node in _currentSequence.nodes)
        {
            if (!string.IsNullOrEmpty(node.id))
                _nodeLookup[node.id] = node;
        }
    }

    private void AdvanceToNextNode()
    {
        _currentIndex++;

        if (_currentIndex >= _currentSequence.nodes.Count)
        {
            EndSequence();
            return;
        }

        var node = _currentSequence.nodes[_currentIndex];
        PresentNode(node);
    }

    private void JumpToNode(string nodeId)
    {
        if (_nodeLookup.TryGetValue(nodeId, out var node))
        {
            // Find its index in the list
            int idx = _currentSequence.nodes.IndexOf(node);
            if (idx >= 0) _currentIndex = idx;
            PresentNode(node);
        }
        else
        {
            Debug.LogWarning($"[DialogueManager] Node ID '{nodeId}' not found! Advancing linearly.");
            AdvanceToNextNode();
        }
    }

    private void PresentNode(DialogueNode node)
    {
        PresentNode(node, _activeSequenceAllowsGameEffects);
    }

    private void PresentNode(DialogueNode node, bool authoredContent)
    {
        if (node == null)
        {
            AdvanceToNextNode();
            return;
        }

        if (!AreTriggerConditionsMet(node.triggerConditions))
        {
            AdvanceToNextNode();
            return;
        }

        if (effectApplier != null && !effectApplier.TryApplyNode(
            EffectOperationId("node:" + _currentIndex + ":" + (node.id ?? string.Empty)), node, authoredContent))
        {
            Debug.LogWarning("[DialogueManager] Node effects were not committed; the dialogue was ended before presenting the node.");
            EndSequence(false);
            return;
        }

        ClearChoices();
        _isWaitingForInput = false;
        _activeNodeFetchVersion++;

        // 1. Apply expression/animation presentation cues.
        ApplyPresentationCues(node);

        // 2. Set speaker name
        if (speakerNameText != null)
        {
            speakerNameText.text = node.speaker == "altera" ? "Altera" : 
                                   node.speaker == "player" ? "You" : node.speaker;
        }

        if (string.Equals(node.speaker, "player", StringComparison.OrdinalIgnoreCase))
        {
            PrefetchAudioBatch(_currentIndex + 1);
            PlayAudioAndTypewriter(node, null);
            return;
        }

        if (_forceTextOnlyForValidatedSequence || skipDialogueTts)
        {
            TryPlayNodeWithCachedTtsOrText(node);
            return;
        }

        // 3. Play audio (pre-rendered or cloud TTS)
        AudioClip clip = null;
        if (node.HasPrerenderedAudio)
        {
            // Load from StreamingAssets
            StartCoroutine(LoadStreamingAudio(node.audioPath, loadedClip =>
            {
                PlayAudioAndTypewriter(node, loadedClip);
            }));
            return; // Wait for coroutine
        }
        else if (_audioCache.TryGetValue(node.id ?? "", out clip))
        {
            // Pre-fetched from cloud — use immediately!
            _audioCache.Remove(node.id);
            PlayAudioAndTypewriter(node, clip);
        }
        else
        {
            // Not pre-fetched yet. Fetch now and play when ready.
            // Only show dots in the dialogue box if we are mid-sequence (_currentIndex > 0)
            FetchAndPlayTTS(node, _currentIndex > 0);
        }

        // Trigger next batch of pre-fetches
        PrefetchAudioBatch(_currentIndex + 1);
    }

    private bool AreTriggerConditionsMet(List<TriggerCondition> conditions)
    {
        TriggerContext context = progressionStore != null
            ? progressionStore.BuildTriggerContext()
            : TriggerContext.CreateDefault();
        return TriggerConditionEvaluator.AreMet(conditions, context);
    }

    private void ApplyPresentationCues(DialogueNode node)
    {
        if (node == null)
        {
            return;
        }

        string expression = node.GetExpressionCue();
        if (emotionController != null && !string.IsNullOrEmpty(expression))
        {
            emotionController.SetExpression(expression);
        }

        DialogueAnimationCue animationCue = node.GetAnimationCue();
        if (dialogueAnimator != null && animationCue != null)
        {
            ApplyAnimationCue(animationCue);
        }
    }

    private void ApplyAnimationCue(DialogueAnimationCue cue)
    {
        if (cue == null || !cue.HasCue || dialogueAnimator == null)
        {
            return;
        }

        int layer = cue.layer < 0 ? 0 : cue.layer;
        if (!string.IsNullOrWhiteSpace(cue.boolParameter))
        {
            dialogueAnimator.SetBool(cue.boolParameter, cue.boolValue);
        }

        if (!string.IsNullOrWhiteSpace(cue.floatParameter))
        {
            dialogueAnimator.SetFloat(cue.floatParameter, cue.floatValue);
        }

        if (!string.IsNullOrWhiteSpace(cue.intParameter))
        {
            dialogueAnimator.SetInteger(cue.intParameter, cue.intValue);
        }

        if (!string.IsNullOrWhiteSpace(cue.trigger))
        {
            dialogueAnimator.SetTrigger(cue.trigger);
        }

        if (string.IsNullOrWhiteSpace(cue.state))
        {
            return;
        }

        if (cue.transitionSeconds > 0f)
        {
            dialogueAnimator.CrossFadeInFixedTime(cue.state, cue.transitionSeconds, layer);
        }
        else
        {
            dialogueAnimator.Play(cue.state, layer);
        }
    }

    private void PlayAudioAndTypewriter(DialogueNode node, AudioClip clip)
    {
        // Play audio
        if (clip != null && alteraTTS != null)
        {
            var audioSource = alteraTTS.GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
            }
        }

        // Start typewriter, synced to audio length if available
        string displayText = !string.IsNullOrEmpty(node.translatedText) ? node.translatedText : node.text;
        float duration = clip != null && syncTypewriterToAudio ? clip.length : displayText.Length / typewriterSpeed;
        
        StartTypewriter(displayText, duration, () =>
        {
            // After typewriter finishes
            if (node.HasChoices)
            {
                DisplayChoices(node);
            }
            else
            {
                // Show "click to continue" and wait
                _isWaitingForInput = true;
                if (continueIndicator != null) continueIndicator.SetActive(true);
            }
        }, () => _onValidatedNodePresented?.Invoke(node));
    }

    private void TryPlayNodeWithCachedTtsOrText(DialogueNode node)
    {
        if (node == null)
        {
            return;
        }

        if (node.HasPrerenderedAudio)
        {
            StartCoroutine(LoadStreamingAudio(node.audioPath, loadedClip =>
            {
                PlayAudioAndTypewriter(node, loadedClip);
            }));
            return;
        }

        if (_audioCache.TryGetValue(node.id ?? string.Empty, out AudioClip prefetchedClip))
        {
            _audioCache.Remove(node.id);
            PlayAudioAndTypewriter(node, prefetchedClip);
            return;
        }

        _ = LoadCachedTtsAndPlayOrFallback(node);
    }

    private void EndSequence(bool invokeCompletionCallback = true)
    {
        _pendingSequenceExitCompletion = _onSequenceCompleted;
        _invokePendingSequenceExitCompletion = invokeCompletionCallback;
        _onSequenceCompleted = null;
        CloseTopicInput(false);
        _currentSequence = null;
        _activeEffectScope = null;
        _activeSequenceAllowsGameEffects = false;
        _currentIndex = -1;
        _onValidatedNodePresented = null;
        _onValidatedNodeAcknowledged = null;
        _forceTextOnlyForValidatedSequence = false;
        _audioCache.Clear();
        _audioFetching.Clear();
        _isFetchingAudio = false;
        _activeNodeFetchVersion++;
        ResetDialogueVisuals(true);

        HideDialoguePanel();
        if (dialogueCameraDirector != null)
        {
            if (playerController != null) playerController.SetExternalCameraControl(true);
            dialogueCameraDirector.ExitDialogue(FinishDialogueCameraExit);
        }
        else
        {
            FinishDialogueCameraExit();
        }
        if (emotionController != null) emotionController.SetExpression_Default();

        Debug.Log("[DialogueManager] Sequence complete.");
    }

    private void FinishDialogueCameraExit()
    {
        if (playerController != null)
        {
            playerController.SetExternalCameraControl(false);
            if (!_externalControlLock)
            {
                playerController.SetDialogueMode(false);
            }
        }

        RestoreHudMode();
        InvokePendingSequenceExitCompletion();
    }

    private void InvokePendingSequenceExitCompletion()
    {
        Action completion = _pendingSequenceExitCompletion;
        bool shouldInvoke = _invokePendingSequenceExitCompletion;
        _pendingSequenceExitCompletion = null;
        _invokePendingSequenceExitCompletion = false;

        if (shouldInvoke)
        {
            completion?.Invoke();
        }
    }

    // =============================================
    // INPUT HANDLING
    // =============================================

    private void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if ((IsActive || _isTopicInputOpen || _semanticTopicCoroutine != null || (directConversationBridge != null && directConversationBridge.IsTurnInFlight)) &&
            keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            DismissActiveSequence();
            return;
        }

        if (_isTopicInputOpen)
        {
            var topicKeyboard = UnityEngine.InputSystem.Keyboard.current;
            if (topicKeyboard != null)
            {
                if (topicKeyboard.enterKey.wasPressedThisFrame || topicKeyboard.numpadEnterKey.wasPressedThisFrame)
                {
                    SubmitTopicInput();
                }
            }

            return;
        }

        if (!IsActive) return;

        // Smoothly rotate Altera to face the Player during dialogue
        if (useCinematicDialogueFocus && rotateAlteraDuringDialogue && alteraTransform != null && playerController != null)
        {
            Vector3 direction = playerController.transform.position - alteraTransform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                alteraTransform.rotation = Quaternion.Slerp(alteraTransform.rotation, targetRot, Time.deltaTime * cinematicSmoothness);
            }
        }

        // Click or Space to advance / skip typewriter
        bool inputPressed = false;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) inputPressed = true;
        if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) inputPressed = true;

        if (inputPressed)
        {
            if (_isTyping)
            {
                // Skip to end of typewriter
                _skipRequested = true;
            }
            else if (_isWaitingForInput)
            {
                var currentNode = _currentSequence.nodes[_currentIndex];
                if (_onValidatedNodeAcknowledged != null && !_onValidatedNodeAcknowledged(currentNode))
                {
                    _isWaitingForInput = true;
                    if (continueIndicator != null) continueIndicator.SetActive(true);
                    return;
                }
                _isWaitingForInput = false;
                if (continueIndicator != null) continueIndicator.SetActive(false);
                if (!string.IsNullOrEmpty(currentNode.nextId))
                {
                    if (currentNode.nextId.ToLower() == "end")
                    {
                        EndSequence();
                    }
                    else
                    {
                        JumpToNode(currentNode.nextId);
                    }
                }
                else
                {
                    AdvanceToNextNode();
                }
            }
        }
    }

    // =============================================
    // TYPEWRITER
    // =============================================

    private void StartTypewriter(string text, float duration, Action onComplete, Action onFirstVisible = null)
    {
        if (_typewriterCoroutine != null) StopCoroutine(_typewriterCoroutine);
        _typewriterCoroutine = StartCoroutine(TypewriterRoutine(text, duration, onComplete, onFirstVisible));
    }

    private IEnumerator TypewriterRoutine(string text, float duration, Action onComplete, Action onFirstVisible)
    {
        _fullText = text;
        _isTyping = true;
        _skipRequested = false;

        if (dialogueText != null) dialogueText.text = "";

        int charCount = text.Length;
        float elapsed = 0f;
        bool reportedVisible = false;

        for (int i = 0; i < charCount; i++)
        {
            if (_skipRequested)
            {
                if (dialogueText != null) dialogueText.text = text;
                break;
            }

            if (dialogueText != null) dialogueText.text = text.Substring(0, i + 1);
            if (!reportedVisible)
            {
                reportedVisible = true;
                onFirstVisible?.Invoke();
            }

            // Calculate delay per character based on total duration
            float delay = duration / charCount;
            elapsed += delay;
            yield return new WaitForSeconds(delay);
        }

        _isTyping = false;
        _skipRequested = false;
        onComplete?.Invoke();
    }

    // =============================================
    // CHOICES
    // =============================================

    private void DisplayChoices(DialogueNode node)
    {
        ClearChoices();
        List<DialogueChoice> eligibleChoices = GetEligibleChoices(node);
        if (eligibleChoices.Count == 0)
        {
            if (node != null && !string.IsNullOrWhiteSpace(node.nextId))
            {
                if (string.Equals(node.nextId, "end", StringComparison.OrdinalIgnoreCase))
                    EndSequence();
                else
                    JumpToNode(node.nextId);
            }
            else
            {
                AdvanceToNextNode();
            }
            return;
        }

        if (useWorldSpaceChoices && worldChoicePresenter != null)
        {
            worldChoicePresenter.ShowChoices(eligibleChoices, HandleChoiceSelection);
            if (dialogueCameraDirector != null)
            {
                DialogueCameraDirector.DialogueShot shot = worldChoicePresenter.ActiveSide == WorldDialogueChoicePresenter.ChoiceSide.Right
                    ? DialogueCameraDirector.DialogueShot.ChoicesRight
                    : DialogueCameraDirector.DialogueShot.ChoicesLeft;
                dialogueCameraDirector.ReframeDialogue(alteraTransform, shot);
            }
            return;
        }

        if (choiceContainer == null || choiceButtonPrefab == null) return;

        foreach (var choice in eligibleChoices)
        {
            var btnObj = Instantiate(choiceButtonPrefab, choiceContainer);
            var tmpText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null) tmpText.text = choice.label;

            var button = btnObj.GetComponent<Button>();
            if (button != null)
            {
                DialogueChoice capturedChoice = choice;
                string targetId = choice.nextId;
                button.onClick.AddListener(() =>
                {
                    HandleChoiceSelection(capturedChoice, targetId);
                });
            }
        }
    }

    private List<DialogueChoice> GetEligibleChoices(DialogueNode node)
    {
        List<DialogueChoice> eligible = new List<DialogueChoice>();
        if (node == null)
        {
            return eligible;
        }

        if (node.choices != null)
        {
            foreach (DialogueChoice choice in node.choices)
            {
                if (choice != null && AreTriggerConditionsMet(choice.triggerConditions) &&
                    (!string.Equals(choice.action, "activity_start", StringComparison.OrdinalIgnoreCase) ||
                     (directConversationBridge != null && directConversationBridge.IsGreetingActivityAvailable(choice.value))) &&
                    TopicMenuSelection.IsEligible(choice.action, choice.value,
                        topicId => directConversationBridge != null && directConversationBridge.IsGreetingTopicAvailable(topicId)))
                {
                    eligible.Add(choice);
                }
            }
        }

        AddTopicSuggestionChoices(node, eligible);
        return eligible;
    }

    private void AddTopicSuggestionChoices(DialogueNode node, List<DialogueChoice> eligible)
    {
        if (node == null || !node.includeTopicSuggestions || topicOptionCatalog == null)
        {
            return;
        }

        List<DialogueChoice> authored = topicOptionCatalog.BuildChoices(int.MaxValue, node.topicSuggestionTag);
        List<DialogueChoice> suggested = eligible.FindAll(choice => choice.action == "scenario_topic_offer");
        eligible.RemoveAll(choice => choice.action == "scenario_topic_offer");
        List<DialogueChoice> topicChoices = GreetingTopicSelection.Select(suggested, authored,
            BuildChoiceDedupeKey, node.topicSuggestionLimit > 0 ? node.topicSuggestionLimit : 3,
            count => UnityEngine.Random.Range(0, count));

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DialogueChoice choice in eligible)
        {
            string key = BuildChoiceDedupeKey(choice);
            if (!string.IsNullOrWhiteSpace(key))
            {
                seen.Add(key);
            }
        }

        int insertIndex = eligible.Count;
        for (int i = 0; i < eligible.Count; i++)
        {
            if (string.Equals(eligible[i]?.action, "open_topic_input", StringComparison.OrdinalIgnoreCase))
            {
                insertIndex = i;
                break;
            }
        }

        foreach (DialogueChoice choice in topicChoices)
        {
            string key = BuildChoiceDedupeKey(choice);
            if (string.IsNullOrWhiteSpace(key) || seen.Contains(key))
            {
                continue;
            }

            eligible.Insert(insertIndex, choice);
            insertIndex++;
            seen.Add(key);
        }
    }

    private static string BuildChoiceDedupeKey(DialogueChoice choice)
    {
        if (choice == null)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(choice.action) || !string.IsNullOrWhiteSpace(choice.value))
        {
            return $"{choice.action}|{choice.value}";
        }

        return choice.label ?? "";
    }

    private string EffectOperationId(string action)
    {
        return DialogueEffectApplier.CreateOperationId((_activeEffectScope ?? "missing-scope") + ":" + (action ?? string.Empty));
    }

    private void HandleChoiceSelection(DialogueChoice choice, string targetId)
    {
        string choiceKey = choice == null ? "null" :
            _currentIndex + ":" + (choice.action ?? string.Empty) + ":" + (choice.nextId ?? string.Empty) + ":" +
            (choice.value ?? string.Empty) + ":" + (choice.label ?? string.Empty);
        if (effectApplier != null && !effectApplier.TryApplyChoice(
            EffectOperationId("choice:" + choiceKey), choice, _activeSequenceAllowsGameEffects))
        {
            Debug.LogWarning("[DialogueManager] Choice effects were not committed; the choice remains available for retry.");
            return;
        }
        ClearChoices(true);

        if (choice != null && string.Equals(choice.action, "open_topic_input", StringComparison.OrdinalIgnoreCase))
        {
            OpenTopicInput();
            return;
        }

        if (choice != null && string.Equals(choice.action, "request_topic_prompt", StringComparison.OrdinalIgnoreCase))
        {
            SubmitConversationText(choice.value);
            return;
        }

        if (choice != null && string.Equals(choice.action, "conversation_suggestion", StringComparison.OrdinalIgnoreCase))
        {
            SubmitSuggestedConversationReply(choice.conversationResponseId, choice.conversationPrompt);
            return;
        }

        if (choice != null && string.Equals(choice.action, "scenario_answer", StringComparison.OrdinalIgnoreCase))
        {
            directConversationBridge?.SelectScenarioAnswer(choice.value);
            return;
        }

        if (choice != null && string.Equals(choice.action, "scenario_free_text", StringComparison.OrdinalIgnoreCase))
        {
            directConversationBridge?.OpenScenarioFreeText();
            return;
        }

        if (choice != null && string.Equals(choice.action, "scenario_topic_offer", StringComparison.OrdinalIgnoreCase))
        {
            directConversationBridge?.SelectScenarioTopicOffer(choice.value, choice.conversationResponseId);
            return;
        }

        if (TopicMenuSelection.TryStart(choice == null ? null : choice.action,
            choice == null ? null : choice.value,
            topicId => directConversationBridge != null && directConversationBridge.IsGreetingTopicAvailable(topicId),
            topicId => directConversationBridge.StartGreetingTopic(topicId)))
            return;

        if (choice != null && string.Equals(choice.action, "activity_start", StringComparison.OrdinalIgnoreCase))
        {
            directConversationBridge?.StartGreetingActivity(choice.value);
            return;
        }

        if (choice != null && string.Equals(choice.action, "activity_stop", StringComparison.OrdinalIgnoreCase))
        {
            directConversationBridge?.StopCurrentActivity(choice.value);
            return;
        }

        if (choice != null && string.Equals(choice.action, "activity_continue", StringComparison.OrdinalIgnoreCase))
        {
            directConversationBridge?.ContinueCurrentActivity(choice.value);
            return;
        }

        if (choice != null && string.Equals(choice.action, "play_generated_dialogue", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"[DialogueManager] Could not play generated dialogue '{choice.value}'.");
            AdvanceToNextNode();
            return;
        }

        if (choice != null && string.Equals(choice.action, "play_local_sequence", StringComparison.OrdinalIgnoreCase))
        {
            if (TryPlayLocalSequenceChoice(choice.value))
            {
                return;
            }

            Debug.LogWarning($"[DialogueManager] Could not play local sequence '{choice.value}'.");
            AdvanceToNextNode();
            return;
        }

        if (!string.IsNullOrEmpty(targetId))
            JumpToNode(targetId);
        else
            AdvanceToNextNode();
    }

    private bool TryPlayLocalSequenceChoice(string sequencePath)
    {
        if (dialogueRouter == null || string.IsNullOrWhiteSpace(sequencePath))
        {
            return false;
        }

        if (!dialogueRouter.TryLoadSequence(sequencePath, out DialogueSequence sequence))
        {
            return false;
        }

        StartSequence(sequence);
        return true;
    }

    private void OpenTopicInput()
    {
        if (topicInputPanel == null || topicInputField == null)
        {
            Debug.LogWarning("[DialogueManager] Topic input requested but topic input UI is not assigned.");
            return;
        }

        OpenTextInput(topicInputPlaceholder, HandleTopicInputSubmitted, null, false);
    }

    public void OpenScenarioAnswerInput(Action<string> onSubmit)
    {
        OpenTextInput("Type your answer...", onSubmit, null, false);
    }

    public void ShowInputBusy(string message)
    {
        if (topicInputPanel == null || topicInputField == null)
        {
            return;
        }

        _isTopicInputOpen = false;
        _textInputSubmitAction = null;
        _textInputCancelAction = null;
        topicInputPanel.SetActive(true);
        topicInputField.text = string.IsNullOrWhiteSpace(message) ? "Waiting..." : message;
        topicInputField.interactable = false;
    }

    public void SubmitTopicInput()
    {
        if (!_isTopicInputOpen || topicInputField == null)
        {
            return;
        }

        string topic = topicInputField.text != null ? topicInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(topic))
        {
            topicInputField.ActivateInputField();
            return;
        }

        Action<string> submitAction = _textInputSubmitAction;
        bool releaseControl = _releaseControlOnInputSubmit;
        _textInputSubmitAction = null;
        _textInputCancelAction = null;
        CloseTopicInput(releaseControl);
        submitAction?.Invoke(topic);
    }

    private void HandleTopicInputSubmitted(string topic)
    {
        SubmitConversationText(topic);
    }

    public void SubmitSuggestedConversationReply(string responseId, string prompt)
    {
        if (directConversationBridge == null || !directConversationBridge.IsReady ||
            !directConversationBridge.IsCurrentResponse(responseId))
        {
            ShowInputRetryState("That suggestion has expired. Please type a new message.");
            return;
        }
        SubmitConversationText(prompt);
    }

    private void SubmitConversationText(string text)
    {
        ClearActiveSequenceWithoutExitingDialogue();
        ShowInputBusy("Listening...");
        if (playerController != null)
        {
            playerController.SetDialogueMode(true, playerController.PlayerCamera != null ? playerController.PlayerCamera.fieldOfView : dialogueZoomFOV, null, 0f);
        }

        if (_semanticTopicCoroutine != null)
        {
            StopCoroutine(_semanticTopicCoroutine);
        }

        if (directConversationBridge != null && directConversationBridge.IsReady)
        {
            directConversationBridge.Submit(text);
            return;
        }

        _semanticTopicCoroutine = StartCoroutine(RouteTopicWithSemantics(text));
    }

    public void ShowInputRetryState(string message)
    {
        if (topicInputPanel == null || topicInputField == null) return;
        // A failed menu launch has already cleared its choices. Cancel must close
        // that stranded exchange, not merely hide the retry field over it.
        OpenTextInput(topicInputPlaceholder, HandleTopicInputSubmitted, CloseScenarioExchange, false);
        topicInputField.text = string.Empty;
        TMP_Text placeholder = topicInputField.placeholder as TMP_Text;
        if (placeholder != null && !string.IsNullOrWhiteSpace(message)) placeholder.text = message;
        topicInputField.ActivateInputField();
    }

    private IEnumerator RouteTopicWithSemantics(string topic)
    {
        // Store the coroutine handle before a ready model can finish synchronously.
        yield return null;
        float remaining = Mathf.Max(0.1f, semanticRouteTimeoutSeconds);
        while (GteSemanticRuntime.Current == null || GteSemanticRuntime.Current.State == GteSemanticState.Loading)
        {
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                PlaySemanticFallback("GTE did not finish warming up in time.");
                yield break;
            }

            yield return null;
        }

        GteSemanticRuntime semanticRuntime = GteSemanticRuntime.Current;
        DialogueRouteMatch[] matches = null;
        try
        {
            if (semanticRuntime.State != GteSemanticState.Ready ||
                !semanticRuntime.TryRoute(topic, out matches, semanticMinimumSimilarity))
            {
                PlaySemanticFallback("GTE was unavailable or could not rank the input.");
                yield break;
            }
        }
        catch (System.Exception exception)
        {
            PlaySemanticFallback($"GTE route inference failed: {exception.Message}");
            yield break;
        }

        TopicRouteDecision routeDecision = TopicRoutePolicy.Decide(matches, semanticMinimumSimilarity, semanticFallbackRouteId);
        if (routeDecision.UsedFallback)
        {
            PlaySemanticFallback($"Semantic routing selected fallback: {routeDecision.Reason}.");
            yield break;
        }

        if (dialogueRouter == null || !dialogueRouter.TryPlayAuthoredRoute(routeDecision.SelectedRouteId))
        {
            PlaySemanticFallback($"No eligible authored dialogue is available for route '{routeDecision.SelectedRouteId}'.");
            yield break;
        }

        _semanticTopicCoroutine = null;
    }

    private void PlaySemanticFallback(string reason)
    {
        Debug.LogWarning($"[DialogueManager] Semantic topic routing fell back: {reason}");
        _semanticTopicCoroutine = null;
        if (dialogueRouter == null || !dialogueRouter.TryPlayAuthoredRoute(semanticFallbackRouteId))
        {
            CloseTopicInput();
        }
    }

    private void OpenTextInput(string placeholderText, Action<string> onSubmit, Action onCancel, bool releaseControlOnSubmit)
    {
        if (topicInputPanel == null || topicInputField == null)
        {
            Debug.LogWarning("[DialogueManager] Text input requested but topic input UI is not assigned.");
            return;
        }

        _isTopicInputOpen = true;
        _textInputSubmitAction = onSubmit;
        _textInputCancelAction = onCancel;
        _releaseControlOnInputSubmit = releaseControlOnSubmit;
        if (playerController != null)
        {
            Transform dialogueTarget = useCinematicDialogueFocus ? alteraTransform : null;
            float dialogueFov = dialogueCameraDirector != null
                ? dialogueCameraDirector.DialogueFov
                : useCinematicDialogueFocus ? dialogueZoomFOV : playerController.PlayerCamera != null ? playerController.PlayerCamera.fieldOfView : dialogueZoomFOV;
            float smoothness = useCinematicDialogueFocus ? cinematicSmoothness : 0f;
            playerController.SetDialogueMode(true, dialogueFov, dialogueTarget, smoothness);
        }

        if (dialogueCameraDirector != null)
        {
            if (playerController != null) playerController.SetExternalCameraControl(true);
            dialogueCameraDirector.EnterDialogue(alteraTransform, DialogueCameraDirector.DialogueShot.Center, () =>
            {
                if (playerController != null) playerController.SetExternalCameraControl(false);
            });
        }

        topicInputPanel.SetActive(true);
        topicInputField.text = string.Empty;
        topicInputField.interactable = true;

        TMP_Text placeholder = topicInputField.placeholder as TMP_Text;
        if (placeholder != null)
        {
            placeholder.text = placeholderText;
        }

        EventSystem.current?.SetSelectedGameObject(topicInputField.gameObject);
        topicInputField.ActivateInputField();
    }

    public void DismissActiveSequence()
    {
        directConversationBridge?.DismissPresentation();
        if (_semanticTopicCoroutine != null)
        {
            StopCoroutine(_semanticTopicCoroutine);
            _semanticTopicCoroutine = null;
        }

        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }

        _isTyping = false;
        _skipRequested = false;
        _isWaitingForInput = false;
        ClearChoices();
        CloseTopicInput(false);
        EndSequence(false);
    }

    public void CloseScenarioExchange()
    {
        if (_semanticTopicCoroutine != null)
        {
            StopCoroutine(_semanticTopicCoroutine);
            _semanticTopicCoroutine = null;
        }
        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }
        _isTyping = false;
        _skipRequested = false;
        _isWaitingForInput = false;
        ClearChoices();
        CloseTopicInput(false);
        EndSequence(false);
    }

    public void CloseTopicInput()
    {
        CloseTopicInput(true);
    }

    private void CloseTopicInput(bool releaseControl)
    {
        Action cancelAction = _textInputCancelAction;
        _isTopicInputOpen = false;
        _textInputSubmitAction = null;
        _textInputCancelAction = null;
        _releaseControlOnInputSubmit = true;

        if (topicInputField != null)
        {
            topicInputField.DeactivateInputField();
            topicInputField.interactable = true;
        }

        if (topicInputPanel != null)
        {
            topicInputPanel.SetActive(false);
        }

        if (releaseControl && !IsActive && !_externalControlLock)
        {
            if (dialogueCameraDirector != null)
            {
                if (playerController != null) playerController.SetExternalCameraControl(true);
                dialogueCameraDirector.ExitDialogue(FinishDialogueCameraExit);
            }
            else
            {
                FinishDialogueCameraExit();
            }
        }

        cancelAction?.Invoke();
    }

    private void ClearActiveSequenceWithoutExitingDialogue()
    {
        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }

        _isTyping = false;
        _skipRequested = false;
        _isWaitingForInput = false;
        _currentSequence = null;
        _activeEffectScope = null;
        _activeSequenceAllowsGameEffects = false;
        _currentIndex = -1;
        _audioCache.Clear();
        _audioFetching.Clear();
        _isFetchingAudio = false;
        _activeNodeFetchVersion++;
        _onSequenceCompleted = null;
        ResetDialogueVisuals(true);
        HideDialoguePanel();
        if (playerController != null)
        {
            playerController.SetExternalCameraControl(false);
            playerController.SetDialogueMode(false);
        }

        RestoreHudMode();
    }

    public async Task<int> WarmSequenceAudio(DialogueSequence sequence, int nodeCount)
    {
        if (sequence == null || sequence.nodes == null || sequence.nodes.Count == 0 || nodeCount <= 0)
        {
            return 0;
        }

        if (skipDialogueTts)
        {
            return Mathf.Min(nodeCount, sequence.nodes.Count);
        }

        int warmedCount = 0;
        int targetCount = Mathf.Min(nodeCount, sequence.nodes.Count);

        for (int i = 0; i < targetCount; i++)
        {
            DialogueNode node = sequence.nodes[i];
            if (node == null || string.IsNullOrWhiteSpace(node.text))
            {
                continue;
            }

            if (node.HasPrerenderedAudio)
            {
                warmedCount++;
                continue;
            }

            string language = string.IsNullOrEmpty(node.language) ? defaultVoiceLanguage : node.language;
            AudioClip clip = await FetchCloudTTS(node.text, language);
            if (clip == null)
            {
                if (skipDialogueTts || IsTtsServiceUnavailable)
                {
                    return targetCount;
                }

                continue;
            }

            warmedCount++;
            Destroy(clip);
        }

        return warmedCount;
    }

    private void ClearChoices()
    {
        ClearChoices(false);
    }

    private void ClearChoices(bool reframeToCenter)
    {
        if (worldChoicePresenter != null)
        {
            worldChoicePresenter.ClearChoices();
        }

        if (choiceContainer != null)
        {
            foreach (Transform child in choiceContainer)
                Destroy(child.gameObject);
        }

        if (reframeToCenter && dialogueCameraDirector != null)
        {
            dialogueCameraDirector.ReframeDialogue(alteraTransform, DialogueCameraDirector.DialogueShot.Center);
        }
    }

    private void ShowDialoguePanel()
    {
        if (dialoguePanelView != null)
        {
            dialoguePanelView.Show();
            return;
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(true);
        }
    }

    private void HideDialoguePanel()
    {
        if (dialoguePanelView != null)
        {
            dialoguePanelView.Hide();
            return;
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    private void HideDialoguePanelImmediate()
    {
        if (dialoguePanelView != null)
        {
            dialoguePanelView.HideImmediate();
            return;
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    private void EnterHudMode(AlterOSHudMode mode)
    {
        if (hudShell == null)
        {
            return;
        }

        if (!_hasHudModeBeforeDialogue)
        {
            _hudModeBeforeDialogue = hudShell.CurrentMode;
            _hasHudModeBeforeDialogue = true;
        }

        hudShell.SetMode(mode);
    }

    private void RestoreHudMode()
    {
        if (hudShell == null)
        {
            return;
        }

        AlterOSHudMode restoreMode = _hasHudModeBeforeDialogue ? _hudModeBeforeDialogue : AlterOSHudMode.Room;
        _hasHudModeBeforeDialogue = false;
        hudShell.SetMode(restoreMode);
        hudShell.Refresh();
    }

    public void ToggleSkipDialogueTts()
    {
        skipDialogueTts = !skipDialogueTts;
        ApplySkipTtsStateChange();
    }

    public void SetSkipDialogueTts(bool enabled)
    {
        if (skipDialogueTts == enabled)
        {
            return;
        }

        skipDialogueTts = enabled;
        ApplySkipTtsStateChange();
    }

    private void ResetDialogueVisuals(bool clearChoices)
    {
        if (dialogueText != null)
        {
            dialogueText.text = string.Empty;
        }

        if (speakerNameText != null)
        {
            speakerNameText.text = string.Empty;
        }

        if (continueIndicator != null)
        {
            continueIndicator.SetActive(false);
        }

        if (clearChoices)
        {
            ClearChoices();
        }
    }

    private void ApplySkipTtsStateChange()
    {
        _activeNodeFetchVersion++;
        _isFetchingAudio = false;
        RefreshSkipTtsButtonVisuals();

        if (_forceTextOnlyForValidatedSequence) return;

        if (!skipDialogueTts || !IsActive || _isTopicInputOpen || _isTyping || _isWaitingForInput)
        {
            return;
        }

        if (_currentSequence == null || _currentIndex < 0 || _currentIndex >= _currentSequence.nodes.Count)
        {
            return;
        }

        DialogueNode currentNode = _currentSequence.nodes[_currentIndex];
        if (currentNode != null)
        {
            PlayAudioAndTypewriter(currentNode, null);
        }
    }

    private void RefreshSkipTtsButtonVisuals()
    {
        if (skipTtsToggleButton != null)
            skipTtsToggleButton.gameObject.SetActive(!_forceTextOnlyForValidatedSequence);
        if (skipTtsToggleLabel != null)
        {
            skipTtsToggleLabel.text = skipDialogueTts ? skipTtsOnLabel : skipTtsOffLabel;
            skipTtsToggleLabel.gameObject.SetActive(!_forceTextOnlyForValidatedSequence);
        }
    }

    private void OnEnable()
    {
        if (skipTtsToggleButton != null)
        {
            skipTtsToggleButton.onClick.RemoveListener(ToggleSkipDialogueTts);
            skipTtsToggleButton.onClick.AddListener(ToggleSkipDialogueTts);
        }

        if (topicSubmitButton != null)
        {
            topicSubmitButton.onClick.RemoveListener(SubmitTopicInput);
            topicSubmitButton.onClick.AddListener(SubmitTopicInput);
        }

        if (topicCancelButton != null)
        {
            topicCancelButton.onClick.RemoveListener(DismissActiveSequence);
            topicCancelButton.onClick.AddListener(DismissActiveSequence);
        }

        if (topicInputField != null)
        {
            topicInputField.onSubmit.RemoveListener(OnTopicInputSubmitted);
            topicInputField.onSubmit.AddListener(OnTopicInputSubmitted);
        }

        RefreshSkipTtsButtonVisuals();
    }

    private void OnDisable()
    {
        if (_semanticTopicCoroutine != null)
        {
            StopCoroutine(_semanticTopicCoroutine);
            _semanticTopicCoroutine = null;
        }

        if (skipTtsToggleButton != null)
        {
            skipTtsToggleButton.onClick.RemoveListener(ToggleSkipDialogueTts);
        }

        if (topicSubmitButton != null)
        {
            topicSubmitButton.onClick.RemoveListener(SubmitTopicInput);
        }

        if (topicCancelButton != null)
        {
            topicCancelButton.onClick.RemoveListener(DismissActiveSequence);
        }

        if (topicInputField != null)
        {
            topicInputField.onSubmit.RemoveListener(OnTopicInputSubmitted);
        }
    }

    private void OnTopicInputSubmitted(string _)
    {
        SubmitTopicInput();
    }

    // =============================================
    // TTS AUDIO — BACKGROUND PRE-FETCHING
    // =============================================

    /// <summary>
    /// Pre-fetch TTS audio for the next N nodes in the background.
    /// This is the core of the "zero-latency illusion" — while the player reads
    /// the current bubble, the next 4 bubbles are already generating on Kaggle.
    /// </summary>
    private void PrefetchAudioBatch(int startIndex)
    {
        if (_currentSequence == null || skipDialogueTts || _forceTextOnlyForValidatedSequence) return;

        for (int i = startIndex; i < Mathf.Min(startIndex + prefetchAhead, _currentSequence.nodes.Count); i++)
        {
            var node = _currentSequence.nodes[i];

            // Skip if already cached, already fetching, has pre-rendered audio, or has no ID
            if (string.IsNullOrEmpty(node.id)) continue;
            if (node.HasPrerenderedAudio) continue;
            if (_audioCache.ContainsKey(node.id)) continue;
            if (_audioFetching.Contains(node.id)) continue;
            if (string.IsNullOrEmpty(node.text)) continue;

            // Fire and forget — fetch in background
            _ = FetchTTSToCache(node);
        }
    }

    private async Task FetchTTSToCache(DialogueNode node)
    {
        string nodeId = node.id;
        _audioFetching.Add(nodeId);

        try
        {
            string lang = string.IsNullOrEmpty(node.language) ? defaultVoiceLanguage : node.language;
            var clip = await FetchCloudTTS(node.text, lang);
            if (clip != null && !_audioCache.ContainsKey(nodeId))
            {
                _audioCache[nodeId] = clip;
                Debug.Log($"[DialogueManager] Pre-fetched audio for node '{nodeId}'");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DialogueManager] Pre-fetch failed for '{nodeId}': {ex.Message}");
        }
        finally
        {
            _audioFetching.Remove(nodeId);
        }
    }

    private async Task LoadCachedTtsAndPlayOrFallback(DialogueNode node)
    {
        int requestVersion = _activeNodeFetchVersion;
        string language = string.IsNullOrEmpty(node.language) ? defaultVoiceLanguage : node.language;
        AudioClip clip = await LoadCachedLocalTtsOnly(node.text, language);

        if (clip == null && useCachedSkipTtsAcknowledgements)
        {
            clip = await LoadCachedSkipTtsAcknowledgement(language);
        }

        if (this == null || !IsActive || !IsCurrentNode(node) || requestVersion != _activeNodeFetchVersion)
        {
            if (clip != null)
            {
                Destroy(clip);
            }

            return;
        }

        PlayAudioAndTypewriter(node, clip);
    }

    private async void FetchAndPlayTTS(DialogueNode node, bool showDots = true)
    {
        int requestVersion = _activeNodeFetchVersion;
        _isFetchingAudio = true;

        // Play thinking filler while waiting for this specific node's audio
        if (!skipDialogueTts && ambientBubble != null && await ambientBubble.IsTTSOnline())
        {
            await ambientBubble.PlayTtsGenerationFiller();
        }

        if (showDots && !skipDialogueTts)
        {
            StartCoroutine(LoadingAnimationRoutine());
        }

        string lang = string.IsNullOrEmpty(node.language) ? defaultVoiceLanguage : node.language;
        AudioClip clip = null;

        while (this != null && IsActive && IsCurrentNode(node) && requestVersion == _activeNodeFetchVersion)
        {
            if (skipDialogueTts)
            {
                break;
            }

            clip = await FetchCloudTTS(node.text, lang);
            if (clip != null)
            {
                break;
            }

            Debug.LogWarning($"[DialogueManager] TTS not ready for node '{node.id}'. Retrying in {ttsRetryDelaySeconds:0.##} seconds.");
            await Task.Delay(Mathf.RoundToInt(Mathf.Max(0.1f, ttsRetryDelaySeconds) * 1000f));
        }

        _isFetchingAudio = false;

        if (this == null || !IsActive || !IsCurrentNode(node) || requestVersion != _activeNodeFetchVersion)
        {
            if (clip != null)
            {
                Destroy(clip);
            }

            return;
        }

        if (clip == null)
        {
            if (skipDialogueTts || IsTtsServiceUnavailable)
            {
                PlayAudioAndTypewriter(node, null);
                return;
            }

            Debug.LogWarning($"[DialogueManager] Aborted waiting for node '{node.id}' because the sequence changed or closed.");
            return;
        }

        if (skipDialogueTts)
        {
            if (clip != null)
            {
                Destroy(clip);
            }

            PlayAudioAndTypewriter(node, null);
            return;
        }

        PlayAudioAndTypewriter(node, clip);

        // Continue pre-fetching
        PrefetchAudioBatch(_currentIndex + 1);
    }

    private bool IsCurrentNode(DialogueNode node)
    {
        if (_currentSequence == null || node == null || _currentIndex < 0 || _currentIndex >= _currentSequence.nodes.Count)
        {
            return false;
        }

        DialogueNode activeNode = _currentSequence.nodes[_currentIndex];
        if (ReferenceEquals(activeNode, node))
        {
            return true;
        }

        return !string.IsNullOrEmpty(activeNode?.id) &&
               !string.IsNullOrEmpty(node.id) &&
               string.Equals(activeNode.id, node.id, StringComparison.Ordinal);
    }

    private IEnumerator LoadingAnimationRoutine()
    {
        int dots = 1;
        while (_isFetchingAudio)
        {
            if (dialogueText != null)
            {
                dialogueText.text = new string('.', dots);
            }
            dots = (dots % 3) + 1;
            yield return new WaitForSeconds(0.3f);
        }
    }

    private async Task<AudioClip> LoadCachedLocalTtsOnly(string text, string language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string voiceId = language.ToLower() == "english" ? "altera_en" : "altera_jp";
        string cacheKey = $"{voiceId}_{GetMd5Hash(text)}";
        string prebakedDir = Path.Combine(Application.streamingAssetsPath, TTSCacheUtility.PrebakedDirectoryName);
        string cacheDir = Path.Combine(Application.streamingAssetsPath, TTSCacheUtility.RuntimeCacheDirectoryName);

        if (!TTSCacheUtility.TryFindCachedClipInDirectories(cacheKey, out string cachePath, out AudioType cacheType, prebakedDir, cacheDir))
        {
            return null;
        }

        Debug.Log($"[DialogueManager] Loading cached-only TTS: {cachePath}");
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + cachePath, cacheType))
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.Success)
            {
                return DownloadHandlerAudioClip.GetContent(req);
            }
        }

        return null;
    }

    private async Task<AudioClip> LoadCachedSkipTtsAcknowledgement(string language)
    {
        List<AmbientLine> candidates = LoadSkipTtsAcknowledgementPool();
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        while (candidates.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, candidates.Count);
            AmbientLine candidate = candidates[index];
            candidates.RemoveAt(index);

            if (candidate == null || string.IsNullOrWhiteSpace(candidate.text))
            {
                continue;
            }

            string clipLanguage = string.IsNullOrWhiteSpace(candidate.language) ? language : candidate.language;
            AudioClip clip = await LoadCachedLocalTtsOnly(candidate.text, clipLanguage);
            if (clip != null)
            {
                Debug.Log($"[DialogueManager] Using cached Skip TTS acknowledgement: {candidate.text}");
                return clip;
            }
        }

        return null;
    }

    private List<AmbientLine> LoadSkipTtsAcknowledgementPool()
    {
        if (string.IsNullOrWhiteSpace(skipTtsAcknowledgementPoolPath))
        {
            return null;
        }

        try
        {
            ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!opened.IsReady) return null;
            ContentResult<RuntimeContentDocument> document = opened.value.ReadDocument(skipTtsAcknowledgementPoolPath);
            if (!document.IsReady) return null;
            string json = document.value.json;
            AmbientLinePool pool = AmbientLinePool.FromJson(json);
            if (pool == null || pool.lines == null || pool.lines.Count == 0)
            {
                return null;
            }

            TriggerContext context = TriggerContext.CreateDefault();
            List<AmbientLine> eligible = new List<AmbientLine>();
            foreach (AmbientLine line in pool.lines)
            {
                if (TriggerConditionEvaluator.AreMet(pool.metadata.triggerConditions, context) &&
                    TriggerConditionEvaluator.AreMet(line.triggerConditions, context))
                {
                    eligible.Add(line);
                }
            }

            return eligible;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DialogueManager] Failed to load Skip TTS acknowledgement pool: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sends a request to the configured TTS provider, saves it to disk cache, and returns an AudioClip.
    /// </summary>
    private async Task<AudioClip> FetchCloudTTS(string text, string language)
    {
        if (alteraTTS == null)
        {
            Debug.LogError("[DialogueManager] AlteraTTS reference is missing!");
            return null;
        }

        IsTtsServiceUnavailable = false;

        // altera_jp is the only MVP voice. altera_en used only if language is explicitly "english".
        string voiceId = language.ToLower() == "english" ? "altera_en" : "altera_jp";
        // --- DISK CACHE CHECK ---
        string prebakedDir = Path.Combine(Application.streamingAssetsPath, TTSCacheUtility.PrebakedDirectoryName);
        string cacheDir = Path.Combine(Application.streamingAssetsPath, TTSCacheUtility.RuntimeCacheDirectoryName);
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

        string hash = GetMd5Hash(text);
        string cacheKey = $"{voiceId}_{hash}";

        if (TTSCacheUtility.TryFindCachedClipInDirectories(cacheKey, out string cachePath, out AudioType cacheType, prebakedDir, cacheDir))
        {
            Debug.Log($"[DialogueManager] Loading TTS from local disk cache: {cachePath}");
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + cachePath, cacheType))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result == UnityWebRequest.Result.Success)
                    return DownloadHandlerAudioClip.GetContent(req);
            }
        }
        // ------------------------

        if (!alteraTTS.HasOpenRouterApiKey)
        {
            IsTtsServiceUnavailable = true;
            SetSkipDialogueTts(true);
            return null;
        }

        Debug.Log($"[DialogueManager] Fetching TTS from OpenRouter for '{voiceId}'...");
        OpenRouterTtsResult result = await alteraTTS.SynthesizeAsync(text, language);
        if (result?.clip != null)
        {
            if (cacheCloudTtsToDisk && result.audioData != null && result.audioData.Length > 0)
            {
                string writePath = TTSCacheUtility.BuildCachePath(cacheDir, cacheKey, ".mp3");
                File.WriteAllBytes(writePath, result.audioData);
                TTSCacheUtility.PruneOldCacheFiles(cacheDir, maxTtsCacheMegabytes * 1024L * 1024L);
                Debug.Log($"[DialogueManager] Saved TTS to cache: {writePath}");
            }

            return result.clip;
        }

        IsTtsServiceUnavailable = true;
        SetSkipDialogueTts(true);
        return null;
    }

    public Task<AudioClip> SynthesizeTtsAsync(string text, string language)
    {
        if (skipDialogueTts) return Task.FromResult<AudioClip>(null);
        return FetchCloudTTS(text, language);
    }

    private string GetMd5Hash(string input)
    {
        using (MD5 md5Hash = MD5.Create())
        {
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < data.Length; i++) sBuilder.Append(data[i].ToString("x2"));
            return sBuilder.ToString();
        }
    }

    // =============================================
    // STREAMING ASSETS LOADER
    // =============================================

    private IEnumerator LoadStreamingAudio(string relativePath, Action<AudioClip> onLoaded)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        AudioType audioType = TTSCacheUtility.GetAudioTypeFromPath(fullPath);

        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + fullPath, audioType))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onLoaded?.Invoke(DownloadHandlerAudioClip.GetContent(request));
            }
            else
            {
                Debug.LogError($"[DialogueManager] Failed to load '{relativePath}': {request.error}");
                onLoaded?.Invoke(null);
            }
        }
    }

    // =============================================
    // CONTEXT MENU TESTING
    // =============================================

    [ContextMenu("Test Sample Dialogue")]
    public void TestSampleDialogue()
    {
        string sampleJson = @"{
            ""nodes"": [
                {
                    ""id"": ""test_01"",
                    ""speaker"": ""altera"",
                    ""text"": ""おはよう！今日も一緒に頑張ろうね。"",
                    ""translatedText"": ""Good morning! Let's do our best together today."",
                    ""emotion"": ""happy"",
                    ""language"": ""japanese"",
                    ""audioPath"": """",
                    ""choices"": [],
                    ""nextId"": ""test_02""
                },
                {
                    ""id"": ""test_02"",
                    ""speaker"": ""altera"",
                    ""text"": ""今日は何がしたいですか？"",
                    ""translatedText"": ""What would you like to do today?"",
                    ""emotion"": ""neutral"",
                    ""language"": ""japanese"",
                    ""audioPath"": """",
                    ""choices"": [
                        { ""label"": ""一緒にコードを書こう！"", ""nextId"": ""test_code"", ""value"": ""code"" },
                        { ""label"": ""ただお話ししたいな。"", ""nextId"": ""test_chat"", ""value"": ""chat"" }
                    ],
                    ""nextId"": """"
                },
                {
                    ""id"": ""test_code"",
                    ""speaker"": ""altera"",
                    ""text"": ""やったー！一緒にデバッグするの大好きです。プロジェクトを開きますね！"",
                    ""translatedText"": ""Yay! I love debugging with you. Let me pull up your project!"",
                    ""emotion"": ""happy"",
                    ""language"": ""japanese"",
                    ""audioPath"": """",
                    ""choices"": [],
                    ""nextId"": ""end""
                },
                {
                    ""id"": ""test_chat"",
                    ""speaker"": ""altera"",
                    ""text"": ""えへへ、そう言ってくれると思ってました。それで…今日はどんな一日でしたか？"",
                    ""translatedText"": ""Ehehe, I was hoping you would say that. So... how was your day?"",
                    ""emotion"": ""happy"",
                    ""language"": ""japanese"",
                    ""audioPath"": """",
                    ""choices"": [],
                    ""nextId"": ""end""
                }
            ]
        }";

        StartSequence(sampleJson);
    }

    [ContextMenu("Play Test Dialogue File")]
    public void PlayTestDialogueFile()
    {
        if (string.IsNullOrEmpty(testDialogueFile))
        {
            Debug.LogError("[DialogueManager] No test dialogue file specified!");
            return;
        }

        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
        if (!opened.IsReady)
        {
            Debug.LogError("[DialogueManager] SQL content release is unavailable: " +
                (opened.diagnostics.Count == 0 ? opened.status : opened.diagnostics[0].code));
            return;
        }
        ContentResult<RuntimeContentDocument> document = opened.value.ReadDocument(testDialogueFile);
        if (!document.IsReady)
        {
            Debug.LogError("[DialogueManager] Test path is not in the SQL content release: " + testDialogueFile);
            return;
        }
        string json = document.value.json;
        Debug.Log($"[DialogueManager] Loading test dialogue from: {testDialogueFile}");

        // Detect if this is a pool or a sequence
        if (json.Contains("\"lines\"") && json.Contains("\"metadata\""))
        {
            // It's an Ambient Pool!
            AmbientLinePool pool = AmbientLinePool.FromJson(json);
            if (pool.lines.Count > 0 && ambientBubble != null)
            {
                var randomLine = pool.lines[UnityEngine.Random.Range(0, pool.lines.Count)];
                Debug.Log($"[DialogueManager] Testing Ambient Pool. Playing random line: {randomLine.id}");
                ambientBubble.Say(randomLine.text, randomLine.translatedText, randomLine.language);
            }
            else
            {
                Debug.LogWarning("[DialogueManager] Ambient pool is empty or bubble is missing!");
            }
        }
        else
        {
            // Treat as a standard Cinematic Sequence
            StartSequence(json);
        }
    }

    [ContextMenu("Refresh Dialogue List")]
    public void RefreshDialogueList()
    {
        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
        if (!opened.IsReady)
        {
            Debug.LogError("[DialogueManager] SQL content release is unavailable: " +
                (opened.diagnostics.Count == 0 ? opened.status : opened.diagnostics[0].code));
            return;
        }
        availableTestFiles = opened.value.BuildManifest.sources.Select(item => "Dialogue/" + item.path)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        Debug.Log($"[DialogueManager] Scanned {availableTestFiles.Length} dialogue files.");
    }
}
