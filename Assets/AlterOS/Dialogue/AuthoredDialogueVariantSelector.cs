using System;
using System.Collections.Generic;
using AlterOS.Stats;
using UnityEngine;

namespace AlterOS.Dialogue
{
    public readonly struct AuthoredDialogueVariantCandidate
    {
        public AuthoredDialogueVariantCandidate(string path, string json)
        {
            Path = path;
            Json = json;
        }

        public string Path { get; }
        public string Json { get; }
    }

    public enum AuthoredDialogueCandidateStatus
    {
        Invalid,
        RouteMismatch,
        IneligibleTimeWindow,
        IneligibleCondition,
        UnsupportedCondition,
        InvalidCondition,
        Selected,
        NotConsideredAfterSelection,
    }

    public sealed class AuthoredDialogueCandidateCheck
    {
        public string Path { get; internal set; }
        public AuthoredDialogueCandidateStatus Status { get; internal set; }
        public string Reason { get; internal set; }
        public StatEligibilityEvaluation Eligibility { get; internal set; }
    }

    public sealed class AuthoredDialogueSelectionResult
    {
        public DialogueDefinition SelectedDialogue { get; internal set; }
        public string SelectedPath { get; internal set; }
        public List<AuthoredDialogueCandidateCheck> Candidates { get; } = new List<AuthoredDialogueCandidateCheck>();
    }

    public static class AuthoredDialogueVariantSelector
    {
        public static AuthoredDialogueSelectionResult Select(
            string routeId,
            IEnumerable<AuthoredDialogueVariantCandidate> candidates,
            int localHour)
        {
            return Select(routeId, candidates, localHour, new StatSnapshot());
        }

        public static AuthoredDialogueSelectionResult Select(
            string routeId,
            IEnumerable<AuthoredDialogueVariantCandidate> candidates,
            int localHour,
            StatSnapshot stats)
        {
            var ordered = new List<AuthoredDialogueVariantCandidate>();
            if (candidates != null)
                ordered.AddRange(candidates);
            ordered.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.Ordinal));

            var result = new AuthoredDialogueSelectionResult();
            foreach (AuthoredDialogueVariantCandidate candidate in ordered)
            {
                var check = new AuthoredDialogueCandidateCheck { Path = candidate.Path };
                result.Candidates.Add(check);

                if (result.SelectedDialogue != null)
                {
                    check.Status = AuthoredDialogueCandidateStatus.NotConsideredAfterSelection;
                    check.Reason = "An earlier stable variant was eligible.";
                    continue;
                }

                DialogueDefinition dialogue;
                try
                {
                    dialogue = string.IsNullOrWhiteSpace(candidate.Json)
                        ? null
                        : JsonUtility.FromJson<DialogueDefinition>(candidate.Json);
                }
                catch (Exception exception)
                {
                    check.Status = AuthoredDialogueCandidateStatus.Invalid;
                    check.Reason = exception.Message;
                    continue;
                }

                List<string> errors = DialogueValidator.Validate(dialogue);
                if (errors.Count > 0)
                {
                    check.Status = AuthoredDialogueCandidateStatus.Invalid;
                    check.Reason = string.Join(" ", errors);
                    continue;
                }

                if (!string.Equals(dialogue.routing.routeId, routeId, StringComparison.OrdinalIgnoreCase))
                {
                    check.Status = AuthoredDialogueCandidateStatus.RouteMismatch;
                    check.Reason = $"Expected route '{routeId}', found '{dialogue.routing.routeId}'.";
                    continue;
                }

                if (dialogue.routing.startHour >= 0 &&
                    !CanonicalTimeWindow.IsWithin(dialogue.routing.startHour, dialogue.routing.endHour, localHour))
                {
                    check.Status = AuthoredDialogueCandidateStatus.IneligibleTimeWindow;
                    check.Reason = $"Eligible from hour {dialogue.routing.startHour} to {dialogue.routing.endHour} for the configured local hour.";
                    continue;
                }

                check.Eligibility = CanonicalDialogueEligibilityEvaluator.Evaluate(dialogue.routing, stats);
                if (check.Eligibility.Status != StatEligibilityStatus.Eligible)
                {
                    check.Status = CandidateStatusFor(check.Eligibility.Status);
                    check.Reason = check.Eligibility.Reason;
                    continue;
                }

                check.Status = AuthoredDialogueCandidateStatus.Selected;
                check.Reason = "First valid, matching variant eligible in stable path order.";
                result.SelectedDialogue = dialogue;
                result.SelectedPath = candidate.Path;
            }

            return result;
        }

        private static AuthoredDialogueCandidateStatus CandidateStatusFor(StatEligibilityStatus status)
        {
            switch (status)
            {
                case StatEligibilityStatus.Rejected:
                    return AuthoredDialogueCandidateStatus.IneligibleCondition;
                case StatEligibilityStatus.Unsupported:
                    return AuthoredDialogueCandidateStatus.UnsupportedCondition;
                case StatEligibilityStatus.Invalid:
                    return AuthoredDialogueCandidateStatus.InvalidCondition;
                default:
                    return AuthoredDialogueCandidateStatus.InvalidCondition;
            }
        }
    }
}
