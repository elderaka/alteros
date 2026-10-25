using System;
using System.Collections.Generic;
using AlterOS.Content;
using AlterOS.Progression;
using UnityEngine;

namespace AlterOS.Dialogue
{
    /// <summary>
    /// Builds the runtime topic option menu from authored topic suggestions.
    /// Prepared dialogue records remain unavailable until their playback path exists.
    /// </summary>
    public class TopicOptionCatalog : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private AlteraProgressionStore progressionStore;

        [Header("Authored Options")]
        [SerializeField] private bool includeAuthoredOptions = true;
        [SerializeField] private string dialogueRoot = "Dialogue";
        [SerializeField] private string authoredTopicOptionsPath = "topic/topic_options.json";

        [Header("Runtime Options")]
        [SerializeField] private int defaultOptionLimit = 4;

        private DialogueSequence authoredOptionsSequence;
        private bool authoredOptionsLoaded;

        private void Awake()
        {
            ResolveReferences();
        }

        public List<DialogueChoice> BuildChoices(int limit = 0, string tagFilter = null)
        {
            ResolveReferences();

            int effectiveLimit = limit > 0 ? limit : Mathf.Max(1, defaultOptionLimit);
            TriggerContext context = progressionStore != null
                ? progressionStore.BuildTriggerContext()
                : TriggerContext.CreateDefault();

            List<TopicSuggestion> suggestions = new List<TopicSuggestion>();
            Dictionary<string, int> indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (includeAuthoredOptions)
            {
                foreach (TopicSuggestion suggestion in GetAuthoredSuggestions())
                {
                    AddSuggestion(suggestions, indexByKey, suggestion, context, tagFilter);
                }
            }

            suggestions.Sort(CompareSuggestions);

            List<DialogueChoice> choices = new List<DialogueChoice>();
            for (int i = 0; i < suggestions.Count && choices.Count < effectiveLimit; i++)
            {
                DialogueChoice choice = ToChoice(suggestions[i]);
                if (choice != null)
                {
                    choices.Add(choice);
                }
            }

            return choices;
        }

        private void ResolveReferences()
        {
            if (progressionStore == null)
            {
                progressionStore = GetComponent<AlteraProgressionStore>();
            }

            if (progressionStore == null)
            {
                progressionStore = FindObjectOfType<AlteraProgressionStore>();
            }

        }

        private List<TopicSuggestion> GetAuthoredSuggestions()
        {
            if (!authoredOptionsLoaded)
            {
                authoredOptionsLoaded = true;
                authoredOptionsSequence = LoadAuthoredOptions();
            }

            return authoredOptionsSequence?.metadata?.topicSuggestions ?? new List<TopicSuggestion>();
        }

        private DialogueSequence LoadAuthoredOptions()
        {
            string relativePath = string.IsNullOrWhiteSpace(authoredTopicOptionsPath)
                ? "topic/topic_options.json"
                : authoredTopicOptionsPath;

            try
            {
                ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
                if (!opened.IsReady)
                {
                    Debug.LogWarning("[TopicOptionCatalog] Authored SQL content is unavailable: " +
                        (opened.diagnostics.Count == 0 ? opened.status : opened.diagnostics[0].code));
                    return null;
                }
                ContentResult<RuntimeContentDocument> document = opened.value.ReadDocument(relativePath);
                if (!document.IsReady)
                {
                    Debug.LogWarning("[TopicOptionCatalog] Authored topic options missing from SQL release: " + relativePath);
                    return null;
                }
                return DialogueSequence.FromJson(document.value.json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TopicOptionCatalog] Failed to load authored topic options: {ex.Message}");
                return null;
            }
        }

        private static void AddSuggestion(
            List<TopicSuggestion> suggestions,
            Dictionary<string, int> indexByKey,
            TopicSuggestion suggestion,
            TriggerContext context,
            string tagFilter)
        {
            if (suggestion == null || !IsEligible(suggestion, context, tagFilter))
            {
                return;
            }

            string key = BuildSuggestionKey(suggestion);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (indexByKey.TryGetValue(key, out int existingIndex))
            {
                if (existingIndex >= 0 &&
                    existingIndex < suggestions.Count &&
                    suggestions[existingIndex] != null &&
                    suggestions[existingIndex].priority >= suggestion.priority)
                {
                    return;
                }

                suggestions[existingIndex] = suggestion;
                return;
            }

            indexByKey[key] = suggestions.Count;
            suggestions.Add(suggestion);
        }

        private static bool IsEligible(TopicSuggestion suggestion, TriggerContext context, string tagFilter)
        {
            if (!TriggerConditionEvaluator.AreMet(suggestion.triggerConditions, context))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(tagFilter))
            {
                return true;
            }

            if (string.Equals(suggestion.tag, tagFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (suggestion.tags == null)
            {
                return false;
            }

            foreach (DialogueTag tag in suggestion.tags)
            {
                if (tag == null)
                {
                    continue;
                }

                if (string.Equals(tag.key, tagFilter, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag.value, tagFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static DialogueChoice ToChoice(TopicSuggestion suggestion)
        {
            if (suggestion == null)
            {
                return null;
            }

            string action = suggestion.action;
            string value = "";

            if (string.IsNullOrWhiteSpace(action))
            {
                if (!string.IsNullOrWhiteSpace(suggestion.generatedDialogueId))
                {
                    action = "play_generated_dialogue";
                    value = suggestion.generatedDialogueId;
                }
                else if (!string.IsNullOrWhiteSpace(suggestion.sequencePath))
                {
                    action = "play_local_sequence";
                    value = suggestion.sequencePath;
                }
                else if (!string.IsNullOrWhiteSpace(suggestion.prompt))
                {
                    action = "request_topic_prompt";
                    value = suggestion.prompt;
                }
            }
            else if (!string.IsNullOrWhiteSpace(suggestion.generatedDialogueId))
            {
                value = suggestion.generatedDialogueId;
            }
            else if (!string.IsNullOrWhiteSpace(suggestion.sequencePath))
            {
                value = suggestion.sequencePath;
            }
            else if (!string.IsNullOrWhiteSpace(suggestion.prompt))
            {
                value = suggestion.prompt;
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                return null;
            }

            string label = !string.IsNullOrWhiteSpace(suggestion.label)
                ? suggestion.label
                : !string.IsNullOrWhiteSpace(suggestion.prompt)
                    ? suggestion.prompt
                    : "Topic";

            return new DialogueChoice
            {
                label = label,
                action = action,
                value = value,
                tags = suggestion.tags != null ? new List<DialogueTag>(suggestion.tags) : new List<DialogueTag>()
            };
        }

        private static string BuildSuggestionKey(TopicSuggestion suggestion)
        {
            if (!string.IsNullOrWhiteSpace(suggestion.id))
            {
                return "id:" + suggestion.id.Trim();
            }

            if (!string.IsNullOrWhiteSpace(suggestion.generatedDialogueId))
            {
                return "generated:" + suggestion.generatedDialogueId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(suggestion.sequencePath))
            {
                return "sequence:" + suggestion.sequencePath.Trim();
            }

            if (!string.IsNullOrWhiteSpace(suggestion.prompt))
            {
                return "prompt:" + suggestion.prompt.Trim();
            }

            return string.IsNullOrWhiteSpace(suggestion.label) ? "" : "label:" + suggestion.label.Trim();
        }

        private static int CompareSuggestions(TopicSuggestion left, TopicSuggestion right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int priorityCompare = right.priority.CompareTo(left.priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return string.Compare(left.label, right.label, StringComparison.OrdinalIgnoreCase);
        }

    }
}
