using System;
using System.Collections.Generic;
using AlterOS.Dialogue;
using AlterOS.Topics;

namespace AlterOS.Scenarios
{
    /// <summary>Projects one already-selected authored Topic graph into the Scenario runner's execution shape.</summary>
    public static class AuthoredScenarioAdapter
    {
        public static ScenarioDefinition Convert(TopicDefinition topic, TopicScenario scenario)
        {
            if (topic == null) throw new ArgumentNullException(nameof(topic));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (topic.scenarios == null || !topic.scenarios.Exists(item => item != null && item.scenarioId == scenario.scenarioId))
                throw new ArgumentException("The selected scenario does not belong to the supplied Topic.", nameof(scenario));
            if (scenario.nodes == null || scenario.nodes.Count == 0)
                throw new ArgumentException("An authored Scenario requires at least one Topic node.", nameof(scenario));

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CanonicalDialogueNode node in scenario.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.id) || !nodeIds.Add(node.id))
                    throw new ArgumentException("Authored Topic nodes must have unique, nonempty IDs.", nameof(scenario));
                if (node.speaker != "alterra")
                    throw new ArgumentException("The Scenario runner currently supports Alterra-authored Topic beats only.", nameof(scenario));
                if (node.language != "japanese")
                    throw new ArgumentException("Authored Scenario conversion requires Japanese speech with optional English display text.", nameof(scenario));
            }

            string completedId = UniqueId("scenario-end", nodeIds);
            string playerEndedId = UniqueId("scenario-player-ended", nodeIds, completedId);
            var definition = new ScenarioDefinition
            {
                scenarioId = scenario.scenarioId,
                topicId = topic.topicId,
                entryStepId = scenario.nodes[0].id,
            };

            for (int index = 0; index < scenario.nodes.Count; index++)
            {
                CanonicalDialogueNode node = scenario.nodes[index];
                string implicitNext = index + 1 < scenario.nodes.Count ? scenario.nodes[index + 1].id : string.Empty;
                string next = string.IsNullOrWhiteSpace(node.nextId) ? implicitNext : node.nextId;
                if (!string.IsNullOrEmpty(next) && !nodeIds.Contains(next))
                    throw new ArgumentException("Topic node '" + node.id + "' points to unknown nextId '" + next + "'.", nameof(scenario));

                if (node.choices != null && node.choices.Count > 0)
                {
                    var ask = new ScenarioStep
                    {
                        id = node.id,
                        kind = "ask",
                        questionId = node.id,
                        question = ToBeat(node),
                        options = new List<ScenarioQuestionOption>(),
                        allowFreeText = false,
                        skipNextStepId = playerEndedId,
                        cancelNextStepId = playerEndedId,
                    };
                    for (int choiceIndex = 0; choiceIndex < node.choices.Count; choiceIndex++)
                    {
                        CanonicalDialogueChoice choice = node.choices[choiceIndex];
                        if (choice == null || string.IsNullOrWhiteSpace(choice.label))
                            throw new ArgumentException("Topic choices must have nonempty labels.", nameof(scenario));
                        string target = string.IsNullOrWhiteSpace(choice.nextId) ? completedId : choice.nextId;
                        if (!string.IsNullOrEmpty(choice.nextId) && !nodeIds.Contains(choice.nextId))
                            throw new ArgumentException("Topic choice points to unknown nextId '" + choice.nextId + "'.", nameof(scenario));
                        ask.options.Add(new ScenarioQuestionOption
                        {
                            optionId = DerivedId(node.id, "-option-" + choiceIndex),
                            label = choice.label,
                            value = choice.label,
                            nextStepId = target,
                        });
                    }
                    definition.steps.Add(ask);
                }
                else
                {
                    definition.steps.Add(new ScenarioStep
                    {
                        id = node.id,
                        kind = "dialogue",
                        beats = new List<ScenarioBeat> { ToBeat(node) },
                        nextStepId = string.IsNullOrEmpty(next) ? completedId : next,
                        closeExchange = string.IsNullOrEmpty(next),
                    });
                }
            }

            definition.steps.Add(new ScenarioStep { id = completedId, kind = "end", outcome = "completed", closeExchange = true });
            definition.steps.Add(new ScenarioStep { id = playerEndedId, kind = "end", outcome = "player_ended", closeExchange = true });
            return definition;
        }

        private static ScenarioBeat ToBeat(CanonicalDialogueNode node)
        {
            return new ScenarioBeat
            {
                id = node.id,
                speaker = node.speaker,
                text = node.text,
                displayText = string.IsNullOrWhiteSpace(node.displayText) ? node.text : node.displayText,
                language = node.language,
                expression = node.presentation == null ? string.Empty : node.presentation.expression,
                voice = node.voice,
                audioKey = node.audioKey,
                animation = node.presentation == null ? string.Empty : node.presentation.animation,
            };
        }

        private static string UniqueId(string candidate, HashSet<string> nodeIds, string other = null)
        {
            string value = candidate;
            int suffix = 1;
            while (nodeIds.Contains(value) || value == other)
                value = candidate + "-" + suffix++;
            return value;
        }

        private static string DerivedId(string sourceId, string suffix)
        {
            int prefixLength = Math.Max(1, 64 - suffix.Length);
            string prefix = sourceId.Length > prefixLength ? sourceId.Substring(0, prefixLength) : sourceId;
            return prefix + suffix;
        }
    }
}
