using System;
using System.Collections.Generic;

namespace AlterOS.Dialogue
{
    public sealed class DialoguePlayback
    {
        private readonly DialogueDefinition _dialogue;
        private readonly Dictionary<string, int> _nodeIndexes;
        private int _index;

        public DialoguePlayback(DialogueDefinition dialogue)
        {
            List<string> errors = DialogueValidator.Validate(dialogue);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(dialogue));

            _dialogue = dialogue;
            _nodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < dialogue.nodes.Count; index++)
                _nodeIndexes.Add(dialogue.nodes[index].id, index);

            _index = 0;
        }

        public CanonicalDialogueNode Current => IsComplete ? null : _dialogue.nodes[_index];
        public bool IsComplete => _index < 0;

        public CanonicalDialogueNode Continue()
        {
            if (IsComplete)
                return null;

            CanonicalDialogueNode current = Current;
            if (current.choices != null && current.choices.Count > 0)
                throw new InvalidOperationException("Choose an option before continuing.");

            if (!string.IsNullOrWhiteSpace(current.nextId))
                return MoveTo(current.nextId);

            _index++;
            if (_index >= _dialogue.nodes.Count)
                _index = -1;

            return Current;
        }

        public CanonicalDialogueNode Choose(int choiceIndex)
        {
            if (IsComplete)
                throw new InvalidOperationException("Dialogue is complete.");

            List<CanonicalDialogueChoice> choices = Current.choices;
            if (choices == null || choiceIndex < 0 || choiceIndex >= choices.Count)
                throw new ArgumentOutOfRangeException(nameof(choiceIndex));

            string nextId = choices[choiceIndex].nextId;
            if (string.IsNullOrWhiteSpace(nextId))
            {
                _index = -1;
                return null;
            }

            return MoveTo(nextId);
        }

        private CanonicalDialogueNode MoveTo(string nodeId)
        {
            if (!_nodeIndexes.TryGetValue(nodeId, out _index))
                throw new InvalidOperationException($"Dialogue changed after validation; node '{nodeId}' is missing.");

            return Current;
        }
    }
}
