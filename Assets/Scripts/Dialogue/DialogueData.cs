using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JSON-serializable dialogue format for AlterOS.
/// Supports both baked content and dynamic LLM responses.
/// 
/// Full JSON example:
/// {
///   "metadata": {
///     "mode": "cinematic",
///     "topic": "Morning Greeting",
///     "timestamp": "2026-05-10T08:30:00Z",
///     "mood": "happy",
///     "topicSuggestions": [
///       { "label": "Weather", "prompt": "Talk about today's weather.", "tag": "smalltalk" },
///       { "label": "Sleep", "prompt": "Ask how well the player slept.", "tag": "checkin" }
///     ],
///     "tags": [
///       { "key": "scene", "value": "morning" },
///       { "key": "tone", "value": "warm" }
///     ],
///     "nlp": {
///       "intentId": "greeting_morning",
///       "patterns": ["good morning", "morning"],
///       "keywords": ["morning", "hello"],
///       "priority": 1
///     },
///     "statChanges": [
///       { "target": "altera", "stat": "affinity", "amount": 1, "op": "add" }
///     ],
///     "effects": [
///       { "type": "flag", "key": "seen_morning_greeting", "value": "true", "amount": 0 }
///     ],
///     "repetitionPolicy": {
///       "key": "morning_greeting",
///       "softLimit": 3,
///       "annoyedLimit": 6,
///       "penaltyAmount": 0.5,
///       "decayHours": 24
///     },
///     "triggerConditions": [
///       { "type": "time_range", "minValue": 5, "maxValue": 11, "invert": false }
///     ]
///   },
///   "nodes": [
///     {
///       "id": "greet_01",
///       "speaker": "altera",
///       "text": "おはよう！今日も一緒に頑張ろうね。",
///       "translatedText": "Good morning! Let's do our best together today.",
///       "emotion": "happy",
///       "language": "japanese",
///       "audioPath": "Dialogue/greet_01.wav",
///       "choices": [
///         {
///           "label": "Good morning!",
///           "action": "",
///           "nextId": "greet_02",
///           "tags": [],
///           "statChanges": [],
///           "effects": [],
///           "repetitionPolicy": { "key": "", "softLimit": 0, "annoyedLimit": 0, "penaltyAmount": 0, "decayHours": 0 },
///           "value": ""
///         },
///         {
///           "label": "I'm sleepy...",
///           "action": "open_topic_input",
///           "nextId": "greet_03",
///           "tags": [ { "key": "tone", "value": "tired" } ],
///           "statChanges": [ { "target": "player", "stat": "energy", "amount": -1, "op": "add" } ],
///           "effects": [],
///           "repetitionPolicy": { "key": "", "softLimit": 0, "annoyedLimit": 0, "penaltyAmount": 0, "decayHours": 0 },
///           "value": "sleepy"
///         }
///       ],
///       "nextId": "",
///       "tags": [ { "key": "scene", "value": "greeting" } ],
///       "statChanges": [],
///       "effects": [],
///       "repetitionPolicy": { "key": "", "softLimit": 0, "annoyedLimit": 0, "penaltyAmount": 0, "decayHours": 0 }
///     }
///   ]
/// }
/// </summary>
namespace AlterOS.Dialogue
{
    [Serializable]
    public class TriggerCondition
    {
        public string type;
        public string key;
        public string stringValue;
        public string target;
        public string stat;
        public float minValue;
        public float maxValue;
        public bool invert;
    }

    [Serializable]
    public class TriggerStatValue
    {
        public string target;
        public string stat;
        public float value;
    }

    [Serializable]
    public class TriggerContext
    {
        public int localHour = -1;
        public float playerDistance = -1f;
        public int bondLevel = -1;
        public float affinity = -1f;
        public float trust = -1f;
        public float comfort = -1f;
        public float energy = -1f;
        public float engagement = -1f;
        public float stimulation = -1f;
        public float attention = -1f;
        public float reassurance = -1f;
        public string mood;
        public string availability;
        public string routeType;
        public float environmentComfort = -1f;
        public float environmentCleanliness = -1f;
        public float environmentCoziness = -1f;
        public float environmentFocusAtmosphere = -1f;
        public string roomMood;
        public List<string> flags = new List<string>();
        public List<DialogueTag> flagValues = new List<DialogueTag>();
        public List<DialogueTag> unlocks = new List<DialogueTag>();
        public List<TriggerStatValue> stats = new List<TriggerStatValue>();

        public static TriggerContext CreateDefault()
        {
            return new TriggerContext
            {
                localHour = DateTime.Now.Hour
            };
        }

        public bool HasFlag(string flag)
        {
            return !string.IsNullOrEmpty(flag) &&
                   flags != null &&
                   flags.Exists(existing => string.Equals(existing, flag, StringComparison.OrdinalIgnoreCase));
        }

        public bool FlagEquals(string key, string value)
        {
            return TagEquals(flagValues, key, value);
        }

        public bool HasUnlock(string key)
        {
            return TagExists(unlocks, key);
        }

        public bool UnlockEquals(string key, string value)
        {
            return TagEquals(unlocks, key, value);
        }

        public void SetStat(string target, string stat, float value)
        {
            string normalizedTarget = NormalizeKey(string.IsNullOrWhiteSpace(target) ? "altera" : target);
            string normalizedStat = NormalizeKey(stat);
            if (string.IsNullOrWhiteSpace(normalizedStat))
            {
                return;
            }

            if (stats == null)
            {
                stats = new List<TriggerStatValue>();
            }

            TriggerStatValue existing = stats.Find(item =>
                item != null &&
                string.Equals(NormalizeKey(item.target), normalizedTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeKey(item.stat), normalizedStat, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.value = value;
                return;
            }

            stats.Add(new TriggerStatValue
            {
                target = normalizedTarget,
                stat = normalizedStat,
                value = value
            });
        }

        public bool TryGetStat(string target, string stat, out float value)
        {
            value = 0f;
            string normalizedTarget = NormalizeKey(string.IsNullOrWhiteSpace(target) ? "altera" : target);
            string normalizedStat = NormalizeKey(stat);
            if (stats == null || string.IsNullOrWhiteSpace(normalizedStat))
            {
                return false;
            }

            TriggerStatValue existing = stats.Find(item =>
                item != null &&
                string.Equals(NormalizeKey(item.target), normalizedTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeKey(item.stat), normalizedStat, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return false;
            }

            value = existing.value;
            return true;
        }

        private static bool TagExists(List<DialogueTag> tags, string key)
        {
            if (tags == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return tags.Exists(tag => tag != null && string.Equals(tag.key, key, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TagEquals(List<DialogueTag> tags, string key, string value)
        {
            if (tags == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string expected = string.IsNullOrWhiteSpace(value) ? "true" : value;
            DialogueTag existing = tags.Find(tag => tag != null && string.Equals(tag.key, key, StringComparison.OrdinalIgnoreCase));
            return existing != null && string.Equals(existing.value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key)
                ? string.Empty
                : key.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        }
    }

    public static class TriggerConditionEvaluator
    {
        public static bool AreMet(List<TriggerCondition> conditions, TriggerContext context = null)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return true;
            }

            if (context == null)
            {
                context = TriggerContext.CreateDefault();
            }

            foreach (TriggerCondition condition in conditions)
            {
                if (!Evaluate(condition, context))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Evaluate(TriggerCondition condition, TriggerContext context)
        {
            if (condition == null || string.IsNullOrEmpty(condition.type))
            {
                return true;
            }

            bool result;
            switch (condition.type)
            {
                case "time_range":
                    result = EvaluateTimeRange(condition, context.localHour);
                    break;
                case "flag_equals":
                    result = EvaluateFlag(condition, context);
                    break;
                case "flag_value_equals":
                    result = context.FlagEquals(condition.key, condition.stringValue);
                    break;
                case "unlock_exists":
                    result = context.HasUnlock(condition.key);
                    break;
                case "unlock_equals":
                case "unlock_value_equals":
                    result = context.UnlockEquals(condition.key, condition.stringValue);
                    break;
                case "player_distance_max":
                    result = context.playerDistance >= 0f && context.playerDistance <= condition.maxValue;
                    break;
                case "player_distance_min":
                    result = context.playerDistance >= 0f && context.playerDistance >= condition.minValue;
                    break;
                case "bond_level_min":
                    result = context.bondLevel >= 0 && context.bondLevel >= condition.minValue;
                    break;
                case "affinity_min":
                    result = context.affinity >= 0f && context.affinity >= condition.minValue;
                    break;
                case "trust_min":
                    result = context.trust >= 0f && context.trust >= condition.minValue;
                    break;
                case "comfort_min":
                    result = context.comfort >= 0f && context.comfort >= condition.minValue;
                    break;
                case "comfort_max":
                    result = context.comfort >= 0f && context.comfort <= condition.maxValue;
                    break;
                case "energy_min":
                    result = context.energy >= 0f && context.energy >= condition.minValue;
                    break;
                case "energy_max":
                    result = context.energy >= 0f && context.energy <= condition.maxValue;
                    break;
                case "engagement_min":
                    result = context.engagement >= 0f && context.engagement >= condition.minValue;
                    break;
                case "stimulation_min":
                    result = context.stimulation >= 0f && context.stimulation >= condition.minValue;
                    break;
                case "stimulation_max":
                    result = context.stimulation >= 0f && context.stimulation <= condition.maxValue;
                    break;
                case "attention_min":
                    result = context.attention >= 0f && context.attention >= condition.minValue;
                    break;
                case "attention_max":
                    result = context.attention >= 0f && context.attention <= condition.maxValue;
                    break;
                case "reassurance_min":
                    result = context.reassurance >= 0f && context.reassurance >= condition.minValue;
                    break;
                case "reassurance_max":
                    result = context.reassurance >= 0f && context.reassurance <= condition.maxValue;
                    break;
                case "mood_equals":
                    result = string.Equals(context.mood, condition.stringValue, StringComparison.OrdinalIgnoreCase);
                    break;
                case "availability_equals":
                    result = string.Equals(context.availability, condition.stringValue, StringComparison.OrdinalIgnoreCase);
                    break;
                case "environment_comfort_min":
                    result = context.environmentComfort >= 0f && context.environmentComfort >= condition.minValue;
                    break;
                case "environment_cleanliness_min":
                    result = context.environmentCleanliness >= 0f && context.environmentCleanliness >= condition.minValue;
                    break;
                case "environment_coziness_min":
                    result = context.environmentCoziness >= 0f && context.environmentCoziness >= condition.minValue;
                    break;
                case "environment_focus_atmosphere_min":
                    result = context.environmentFocusAtmosphere >= 0f && context.environmentFocusAtmosphere >= condition.minValue;
                    break;
                case "room_mood_equals":
                    result = string.Equals(context.roomMood, condition.stringValue, StringComparison.OrdinalIgnoreCase);
                    break;
                case "stat_min":
                    result = EvaluateStatMin(condition, context);
                    break;
                case "stat_max":
                    result = EvaluateStatMax(condition, context);
                    break;
                case "stat_range":
                    result = EvaluateStatRange(condition, context);
                    break;
                case "route_equals":
                    result = string.Equals(context.routeType, condition.stringValue, StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    result = true;
                    break;
            }

            return condition.invert ? !result : result;
        }

        private static bool EvaluateFlag(TriggerCondition condition, TriggerContext context)
        {
            if (!string.IsNullOrWhiteSpace(condition.stringValue) &&
                !string.Equals(condition.stringValue, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(condition.stringValue, "false", StringComparison.OrdinalIgnoreCase))
            {
                return context.FlagEquals(condition.key, condition.stringValue);
            }

            return context.HasFlag(condition.key) == string.Equals(condition.stringValue, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EvaluateStatMin(TriggerCondition condition, TriggerContext context)
        {
            return context.TryGetStat(condition.target, ResolveStatKey(condition), out float value) && value >= condition.minValue;
        }

        private static bool EvaluateStatMax(TriggerCondition condition, TriggerContext context)
        {
            return context.TryGetStat(condition.target, ResolveStatKey(condition), out float value) && value <= condition.maxValue;
        }

        private static bool EvaluateStatRange(TriggerCondition condition, TriggerContext context)
        {
            return context.TryGetStat(condition.target, ResolveStatKey(condition), out float value) &&
                   value >= condition.minValue &&
                   value <= condition.maxValue;
        }

        private static string ResolveStatKey(TriggerCondition condition)
        {
            return string.IsNullOrWhiteSpace(condition.stat) ? condition.key : condition.stat;
        }

        private static bool EvaluateTimeRange(TriggerCondition condition, int localHour)
        {
            return CanonicalTimeWindow.IsWithin(condition.minValue, condition.maxValue, localHour);
        }
    }

    [Serializable]
    public class TopicSuggestion
    {
        /// <summary>Stable id for dedupe/unlock references. Optional but recommended for authored topic options.</summary>
        public string id;
        /// <summary>Short label shown to the player.</summary>
        public string label;

        /// <summary>Natural-language topic prompt to send back to generation.</summary>
        public string prompt;

        /// <summary>Optional style tag or mood hint for UI/routing later.</summary>
        public string tag;

        /// <summary>Optional ready generated dialogue id. If set, the option replays that dialogue instead of generating.</summary>
        public string generatedDialogueId;

        /// <summary>Optional local prebaked sequence path relative to StreamingAssets/Dialogue.</summary>
        public string sequencePath;

        /// <summary>Optional action override. Empty means request_topic_prompt for prompt options.</summary>
        public string action;

        /// <summary>Higher priority suggestions appear first when the menu is capped.</summary>
        public int priority;

        /// <summary>Eligibility conditions for surfacing this topic option.</summary>
        public List<TriggerCondition> triggerConditions = new List<TriggerCondition>();

        /// <summary>Flexible metadata for grouping, analytics, and future topic UI filters.</summary>
        public List<DialogueTag> tags = new List<DialogueTag>();
    }

    [Serializable]
    public class DialogueTag
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class NlpRoutePolicy
    {
        public float minConfidence;
        public bool allowFuzzy = true;
        public bool allowOneWord;
        public bool boundaryPriority;
        public bool creativeIfObjectPresent;
    }

    [Serializable]
    public class NlpIntentMetadata
    {
        public string intentId;
        public string brain;
        public string category;
        public List<string> patterns = new List<string>();
        public List<string> keywords = new List<string>();
        public List<string> singleTokens = new List<string>();
        public List<string> aliases = new List<string>();
        public List<string> concepts = new List<string>();
        public List<string> negativePatterns = new List<string>();
        public float priority = 1f;
        public NlpRoutePolicy routePolicy = new NlpRoutePolicy();
    }

    [Serializable]
    public class StatDelta
    {
        /// <summary>Target state bucket: "altera", "player", or "environment".</summary>
        public string target;

        /// <summary>Stat key, e.g. "affinity", "trust", "credits".</summary>
        public string stat;

        /// <summary>Amount used by the operation.</summary>
        public float amount;

        /// <summary>Operation: "add", "set", "min", or "max". Empty means "add".</summary>
        public string op;
    }

    [Serializable]
    public class DialogueEffect
    {
        /// <summary>Effect type, e.g. "flag", "unlock", "reward", "task", or "memory".</summary>
        public string type;
        public string key;
        public string value;
        public float amount;
    }

    [Serializable]
    public class RepetitionPolicy
    {
        public string key;
        public int softLimit;
        public int annoyedLimit;
        public float penaltyAmount;
        public float decayHours;
    }

    [Serializable]
    public class DialogueAnimationCue
    {
        /// <summary>Animator state name to play or cross-fade to.</summary>
        public string state;
        /// <summary>Animator trigger parameter to fire.</summary>
        public string trigger;
        /// <summary>Animator bool parameter to set before the node plays.</summary>
        public string boolParameter;
        public bool boolValue;
        /// <summary>Animator float parameter to set before the node plays.</summary>
        public string floatParameter;
        public float floatValue;
        /// <summary>Animator int parameter to set before the node plays.</summary>
        public string intParameter;
        public int intValue;
        /// <summary>Animator layer index. Negative values mean the default layer.</summary>
        public int layer = -1;
        /// <summary>Cross-fade duration in seconds when state is provided. Zero means play immediately.</summary>
        public float transitionSeconds = 0.15f;
        public bool HasCue =>
            !string.IsNullOrWhiteSpace(state) ||
            !string.IsNullOrWhiteSpace(trigger) ||
            !string.IsNullOrWhiteSpace(boolParameter) ||
            !string.IsNullOrWhiteSpace(floatParameter) ||
            !string.IsNullOrWhiteSpace(intParameter);
    }

    [Serializable]
    public class DialoguePresentationCue
    {
        /// <summary>Preferred expression cue. Falls back to DialogueNode.emotion when empty.</summary>
        public string expression;
        /// <summary>Optional pose id for future pose/gesture systems.</summary>
        public string pose;
        /// <summary>Optional gaze target id for future look-at behavior.</summary>
        public string gazeTarget;
        /// <summary>Optional body/gesture animation cue for this node.</summary>
        public DialogueAnimationCue animation = new DialogueAnimationCue();
        /// <summary>Extra presentation-specific tags for future systems.</summary>
        public List<DialogueTag> cues = new List<DialogueTag>();
    }

    [Serializable]
    public class DialogueMetadata
    {
        /// <summary>
        /// Routing mode: "cinematic" or "ambient".
        /// Cinematic = locks player, full panel, requires input.
        /// Ambient = floating world-space bubble, no player interruption.
        /// </summary>
        public string mode;
        /// <summary>Summary of the conversation (e.g., 'The Strawberry Debate').</summary>
        public string topic;
        /// <summary>ISO timestamp of when this was generated.</summary>
        public string timestamp;
        /// <summary>The emotional context of the scene.</summary>
        public string mood;
        /// <summary>Optional generated ideas for what the next interaction could talk about.</summary>
        public List<TopicSuggestion> topicSuggestions = new List<TopicSuggestion>();
        /// <summary>Flexible content tags used for filtering, routing, and UI later.</summary>
        public List<DialogueTag> tags = new List<DialogueTag>();
        /// <summary>Optional local NLP metadata for intent-routed content.</summary>
        public NlpIntentMetadata nlp = new NlpIntentMetadata();
        /// <summary>Optional stat changes applied when the sequence-level effect is consumed.</summary>
        public List<StatDelta> statChanges = new List<StatDelta>();
        /// <summary>Optional non-stat effects such as flags, unlocks, rewards, or memories.</summary>
        public List<DialogueEffect> effects = new List<DialogueEffect>();
        /// <summary>Optional repetition behavior policy for local-routed content.</summary>
        public RepetitionPolicy repetitionPolicy = new RepetitionPolicy();
        /// <summary>Optional trigger requirements. Empty means the content can play anytime.</summary>
        public List<TriggerCondition> triggerConditions = new List<TriggerCondition>();
    }

    [Serializable]
    public class DialogueSequence
    {
        public DialogueMetadata metadata = new DialogueMetadata();
        public List<DialogueNode> nodes = new List<DialogueNode>();

        public static DialogueSequence FromJson(string json)
        {
            return JsonUtility.FromJson<DialogueSequence>(json);
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }

    /// <summary>
    /// A single ambient one-liner used by AlteraAmbientBubble.
    /// These play without locking player movement.
    /// </summary>
    [Serializable]
    public class AmbientLine
    {
        public string id;
        /// <summary>Text sent to TTS (original language, e.g. Japanese).</summary>
        public string text;
        /// <summary>Subtitle text displayed in the bubble (e.g. English translation).</summary>
        public string translatedText;
        /// <summary>"japanese" or "english".</summary>
        public string language;
        
        // --- Priority & Cooldown Metadata ---
        public int playCount;
        public long lastPlayTime; // Unix timestamp in seconds
        public bool isGenerated;
        public string topic;
        public List<TriggerCondition> triggerConditions = new List<TriggerCondition>();
        public List<DialogueTag> tags = new List<DialogueTag>();
        public NlpIntentMetadata nlp = new NlpIntentMetadata();
        public List<StatDelta> statChanges = new List<StatDelta>();
        public List<DialogueEffect> effects = new List<DialogueEffect>();
        public RepetitionPolicy repetitionPolicy = new RepetitionPolicy();
    }

    /// <summary>
    /// A pool of ambient one-liners. The AlteraIdleTrigger picks randomly from this.
    /// JSON format: { "metadata": {..., "mode": "ambient"}, "lines": [...] }
    /// </summary>
    [Serializable]
    public class AmbientLinePool
    {
        public DialogueMetadata metadata = new DialogueMetadata();
        public List<AmbientLine> lines = new List<AmbientLine>();

        public static AmbientLinePool FromJson(string json)
        {
            return JsonUtility.FromJson<AmbientLinePool>(json);
        }
    }


    [Serializable]
    public class DialogueNode
    {
        /// <summary>Unique identifier for this node. Used for branching.</summary>
        public string id;

        /// <summary>Who is speaking: "altera", "player", "system".</summary>
        public string speaker;

        /// <summary>The dialogue text to send to TTS (and display if translatedText is empty).</summary>
        public string text;

        /// <summary>Optional: If set, this text is displayed in the UI while 'text' is sent to the TTS.</summary>
        public string translatedText;

        /// <summary>Optional expression cue. presentation.expression takes precedence when present.</summary>
        public string emotion;

        /// <summary>Optional authored voice identity retained by content adapters.</summary>
        public string voice;

        /// <summary>Scalable presentation metadata for expression, pose, gaze, and animation cues.</summary>
        public DialoguePresentationCue presentation = new DialoguePresentationCue();

        /// <summary>"japanese" or "english" — determines which cloned voice to use for TTS.</summary>
        public string language;

        /// <summary>
        /// Path to a pre-rendered audio file (relative to StreamingAssets).
        /// If empty, the DialogueManager will request TTS from the cloud.
        /// </summary>
        public string audioPath;

        /// <summary>
        /// Optional player choices displayed after this node.
        /// If empty, the sequence auto-advances to the next node in the list.
        /// </summary>
        public List<DialogueChoice> choices = new List<DialogueChoice>();

        /// <summary>When true, eligible topic suggestions are injected into this node's choice list at runtime.</summary>
        public bool includeTopicSuggestions;

        /// <summary>Optional cap for injected topic suggestions. Zero means use the runtime default.</summary>
        public int topicSuggestionLimit;

        /// <summary>Optional tag filter for injected topic suggestions.</summary>
        public string topicSuggestionTag;

        /// <summary>
        /// If no choices are present, auto-advance to this node ID.
        /// If empty, advances to the next node in the array sequentially.
        /// </summary>
        public string nextId;

        /// <summary>Flexible content tags used for filtering, routing, and analytics later.</summary>
        public List<DialogueTag> tags = new List<DialogueTag>();

        /// <summary>Optional node eligibility conditions. Empty means the node can play anytime.</summary>
        public List<TriggerCondition> triggerConditions = new List<TriggerCondition>();

        /// <summary>Node-level stat changes. This is the primary granularity for relationship effects.</summary>
        public List<StatDelta> statChanges = new List<StatDelta>();

        /// <summary>Node-level effects such as flags, unlocks, rewards, and memory hooks.</summary>
        public List<DialogueEffect> effects = new List<DialogueEffect>();

        /// <summary>Optional repetition policy applied when this node represents a repeatable local beat.</summary>
        public RepetitionPolicy repetitionPolicy = new RepetitionPolicy();

        /// <summary>Whether this node has pre-rendered audio or needs cloud TTS.</summary>
        public bool HasPrerenderedAudio => !string.IsNullOrEmpty(audioPath);

        /// <summary>Whether this node presents player choices.</summary>
        public bool HasChoices => (choices != null && choices.Count > 0) || includeTopicSuggestions;

        public string GetExpressionCue()
        {
            return presentation != null && !string.IsNullOrWhiteSpace(presentation.expression)
                ? presentation.expression
                : emotion;
        }

        public DialogueAnimationCue GetAnimationCue()
        {
            return presentation != null && presentation.animation != null && presentation.animation.HasCue
                ? presentation.animation
                : null;
        }
    }

    [Serializable]
    public class DialogueChoice
    {
        /// <summary>Text displayed on the choice button.</summary>
        public string label;

        /// <summary>Optional special UI action, e.g. "open_topic_input".</summary>
        public string action;

        /// <summary>The node ID to jump to when this choice is selected.</summary>
        public string nextId;

        /// <summary>Flexible content tags used for filtering, routing, and analytics later.</summary>
        public List<DialogueTag> tags = new List<DialogueTag>();

        /// <summary>Optional choice eligibility conditions. Empty means the choice can appear anytime.</summary>
        public List<TriggerCondition> triggerConditions = new List<TriggerCondition>();

        /// <summary>Node-level stat changes. This is the primary granularity for relationship effects.</summary>
        public List<StatDelta> statChanges = new List<StatDelta>();

        /// <summary>Node-level effects such as flags, unlocks, rewards, and memory hooks.</summary>
        public List<DialogueEffect> effects = new List<DialogueEffect>();

        /// <summary>Optional repetition policy applied when this node represents a repeatable local beat.</summary>
        public RepetitionPolicy repetitionPolicy = new RepetitionPolicy();

        /// <summary>Optional value passed to a future provider-neutral dialogue workflow.</summary>
        public string value;

        /// <summary>Untrusted turn text bound to a host-owned generated response for optional next-turn suggestions.</summary>
        public string conversationPrompt;

        /// <summary>Host response identity; stale suggested replies are rejected when another response is active.</summary>
        public string conversationResponseId;
    }
}
