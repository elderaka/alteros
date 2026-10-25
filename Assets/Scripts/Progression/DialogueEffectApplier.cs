using System;
using AlterOS.Dialogue;
using AlterOS.Persistence;
using UnityEngine;

namespace AlterOS.Progression
{
    public class DialogueEffectApplier : MonoBehaviour
    {
        [SerializeField] private AlteraProgressionStore progressionStore;

        private void Awake()
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

        public void ApplyNode(DialogueNode node)
        {
            TryApplyNode(Guid.NewGuid().ToString("D"), node);
        }

        public void ApplyChoice(DialogueChoice choice)
        {
            TryApplyChoice(Guid.NewGuid().ToString("D"), choice);
        }

        public void ApplySequence(DialogueSequence sequence)
        {
            TryApplySequence(Guid.NewGuid().ToString("D"), sequence);
        }

        public bool TryApplyNode(string operationId, DialogueNode node)
        {
            return TryApplyNode(operationId, node, false);
        }

        internal bool TryApplyNode(string operationId, DialogueNode node, bool authoredContent)
        {
            return node == null || TryApply(operationId, node.statChanges, node.effects, authoredContent);
        }

        public bool TryApplyChoice(string operationId, DialogueChoice choice)
        {
            return TryApplyChoice(operationId, choice, false);
        }

        internal bool TryApplyChoice(string operationId, DialogueChoice choice, bool authoredContent)
        {
            return choice == null || TryApply(operationId, choice.statChanges, choice.effects, authoredContent);
        }

        public bool TryApplySequence(string operationId, DialogueSequence sequence)
        {
            return TryApplySequence(operationId, sequence, false);
        }

        internal bool TryApplySequence(string operationId, DialogueSequence sequence, bool authoredContent)
        {
            return sequence?.metadata == null || TryApply(operationId,
                sequence.metadata.statChanges, sequence.metadata.effects, authoredContent);
        }

        public static string CreateOperationId(string operationScope)
        {
            return GameOperationId.FromStableKey(operationScope);
        }

        private bool TryApply(string operationId, System.Collections.Generic.List<StatDelta> statChanges,
            System.Collections.Generic.List<DialogueEffect> effects, bool authoredContent)
        {
            bool hasChanges = statChanges != null && statChanges.Count > 0 || effects != null && effects.Count > 0;
            if (!hasChanges) return true;
            if (!authoredContent) return false;
            if (progressionStore == null) return false;
            GameCommitResult result = progressionStore.TryApplyDialogueChanges(operationId, statChanges, effects);
            return result.IsDurable;
        }
    }
}
