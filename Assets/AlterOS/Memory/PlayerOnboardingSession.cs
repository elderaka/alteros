using System;
using System.Collections.Generic;

namespace AlterOS.Memory
{
    /// <summary>Pure, acknowledgement-driven first-meeting progress. UI and persistence stay host-owned.</summary>
    public sealed class PlayerOnboardingSession
    {
        private readonly HashSet<string> completedFields;

        public string CurrentStepId { get; private set; }
        public string PreferredName { get; private set; } = string.Empty;
        public string Interest { get; private set; } = string.Empty;
        public string SharedActivityPreference { get; private set; } = string.Empty;
        public bool IsComplete { get; private set; }

        public PlayerOnboardingSession(string persistedStepId, bool persistedStepCompleted,
            bool consentSelected, bool memoryEnabled, ISet<string> savedFields)
        {
            completedFields = new HashSet<string>(savedFields ?? new HashSet<string>(), StringComparer.Ordinal);
            if (persistedStepCompleted && (persistedStepId == "complete" || persistedStepId == "skipped"))
            {
                CurrentStepId = "complete";
                IsComplete = true;
                return;
            }

            if (consentSelected && !memoryEnabled)
            {
                CurrentStepId = "complete";
                IsComplete = true;
                return;
            }

            if (!string.IsNullOrEmpty(persistedStepId))
            {
                string step = ValidateStep(persistedStepId);
                CurrentStepId = persistedStepCompleted || completedFields.Contains(step)
                    ? NextAfter(step)
                    : step;
            }
            else if (!consentSelected)
            {
                CurrentStepId = "consent";
            }
            else if (!memoryEnabled)
            {
                CurrentStepId = "complete";
                IsComplete = true;
            }
            else
            {
                CurrentStepId = NextMissingField();
            }
        }

        /// <returns>True only when the caller confirms both answer/choice and progress were saved.</returns>
        public bool AcknowledgeConsentChoice(bool enableMemory, bool saved)
        {
            if (CurrentStepId != "consent" || !saved) return false;
            if (!enableMemory)
            {
                CurrentStepId = "complete";
                IsComplete = true;
                return true;
            }
            CurrentStepId = NextMissingField();
            return true;
        }

        public bool AcknowledgeAnswer(string answer, bool saved)
        {
            if (!saved || string.IsNullOrWhiteSpace(answer)) return false;
            string value = answer.Trim();
            int maximum = CurrentStepId == "preferred_name" ? 80 : 512;
            if (value.Length > maximum) return false;

            switch (CurrentStepId)
            {
                case "preferred_name": PreferredName = value; break;
                case "interest": Interest = value; break;
                case "shared_activity_preference": SharedActivityPreference = value; break;
                default: return false;
            }

            completedFields.Add(CurrentStepId);
            CurrentStepId = NextMissingField();
            return true;
        }

        public bool AcknowledgeSkip(bool progressSaved)
        {
            if (!progressSaved) return false;
            if (CurrentStepId == "consent")
            {
                CurrentStepId = "complete";
                IsComplete = true;
                return true;
            }
            if (CurrentStepId == "complete" || IsComplete) return false;
            string skipped = CurrentStepId;
            completedFields.Add(skipped);
            CurrentStepId = NextMissingField();
            return true;
        }

        public bool AcknowledgeComplete(bool progressSaved)
        {
            if (CurrentStepId != "complete" || !progressSaved) return false;
            IsComplete = true;
            return true;
        }

        private string NextMissingField()
        {
            return FirstMissingFrom(0);
        }

        private string NextAfter(string stepId)
        {
            switch (stepId)
            {
                case "consent": return FirstMissingFrom(0);
                case "preferred_name": return FirstMissingFrom(1);
                case "interest": return FirstMissingFrom(2);
                case "shared_activity_preference": return "complete";
                case "skipped": case "complete": return "complete";
                default: throw new ArgumentException("Unknown onboarding step.", nameof(stepId));
            }
        }

        private string FirstMissingFrom(int index)
        {
            string[] orderedFields = { "preferred_name", "interest", "shared_activity_preference" };
            for (int i = index; i < orderedFields.Length; i++)
                if (!completedFields.Contains(orderedFields[i])) return orderedFields[i];
            return "complete";
        }

        private static string ValidateStep(string stepId)
        {
            if (stepId != "consent" && stepId != "preferred_name" && stepId != "interest" &&
                stepId != "shared_activity_preference" && stepId != "complete" && stepId != "skipped")
                throw new ArgumentException("Unknown onboarding step.", nameof(stepId));
            return stepId;
        }
    }
}
