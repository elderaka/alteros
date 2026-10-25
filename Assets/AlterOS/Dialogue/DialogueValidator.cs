using System;
using System.Collections.Generic;

namespace AlterOS.Dialogue
{
    public static class DialogueValidator
    {
        public static List<string> Validate(DialogueDefinition dialogue)
        {
            List<string> errors = new List<string>();
            if (dialogue == null)
            {
                errors.Add("Dialogue is null.");
                return errors;
            }

            if (dialogue.schemaVersion != DialogueDefinition.CurrentSchemaVersion)
                errors.Add($"Unsupported schemaVersion '{dialogue.schemaVersion}'.");

            if (string.IsNullOrWhiteSpace(dialogue.id))
                errors.Add("Dialogue id is required.");

            if (dialogue.source != DialogueDefinition.GenericSource &&
                dialogue.source != DialogueDefinition.PersonalizedSource)
                errors.Add($"Dialogue source must be 'generic' or 'personalized', not '{dialogue.source}'.");

            if (dialogue.routing == null)
            {
                errors.Add("Dialogue routing is required.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dialogue.routing.routeId))
                    errors.Add("Dialogue routeId is required.");
                if (!HasText(dialogue.routing.examples))
                    errors.Add("At least one routing example is required.");

                bool hasStartHour = dialogue.routing.startHour >= 0;
                bool hasEndHour = dialogue.routing.endHour >= 0;
                if (hasStartHour != hasEndHour || dialogue.routing.startHour > 23 || dialogue.routing.endHour > 23)
                    errors.Add("Routing hours must both be unset or between 0 and 23.");
            }

            if (dialogue.nodes == null || dialogue.nodes.Count == 0)
            {
                errors.Add("At least one dialogue node is required.");
                return errors;
            }

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < dialogue.nodes.Count; index++)
            {
                CanonicalDialogueNode node = dialogue.nodes[index];
                if (node == null)
                {
                    errors.Add($"Node {index} is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.id))
                    errors.Add($"Node {index} has no id.");
                else if (!nodeIds.Add(node.id))
                    errors.Add($"Duplicate node id '{node.id}'.");

                if (node.speaker != "alterra" && node.speaker != "player" && node.speaker != "system")
                    errors.Add($"Node '{node.id}' has unsupported speaker '{node.speaker}'.");

                if (string.IsNullOrWhiteSpace(node.text))
                    errors.Add($"Node '{node.id}' has no text.");

                if (node.choices != null && node.choices.Count > 0 && !string.IsNullOrWhiteSpace(node.nextId))
                    errors.Add($"Node '{node.id}' cannot have both choices and nextId.");
            }

            foreach (CanonicalDialogueNode node in dialogue.nodes)
            {
                if (node == null)
                    continue;

                ValidateTarget(node.id, "nextId", node.nextId, nodeIds, errors);
                if (node.choices == null)
                    continue;

                for (int index = 0; index < node.choices.Count; index++)
                {
                    CanonicalDialogueChoice choice = node.choices[index];
                    if (choice == null)
                    {
                        errors.Add($"Node '{node.id}' choice {index} is null.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(choice.label))
                        errors.Add($"Node '{node.id}' choice {index} has no label.");

                    ValidateTarget(node.id, $"choice {index}", choice.nextId, nodeIds, errors);
                }
            }

            return errors;
        }

        private static bool HasText(List<string> values)
        {
            if (values == null)
                return false;

            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return true;

            return false;
        }

        private static void ValidateTarget(
            string nodeId,
            string field,
            string targetId,
            HashSet<string> nodeIds,
            List<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(targetId) && !nodeIds.Contains(targetId))
                errors.Add($"Node '{nodeId}' {field} targets missing node '{targetId}'.");
        }
    }
}
