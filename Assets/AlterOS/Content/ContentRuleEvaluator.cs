using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Topics;

namespace AlterOS.Content
{
    /// <summary>Evaluates validated shared Rules with host facts and preserves Unknown as a distinct result.</summary>
    public static class ContentRuleEvaluator
    {
        public static ContentResult<ContentRuleEvaluation> Evaluate(ContentPackage package, ContentRef ruleReference,
            ContentHostVocabulary hostVocabulary, TopicContext context)
        {
            if (package == null || hostVocabulary == null || context == null)
                return ContentResult<ContentRuleEvaluation>.Failure(ContentStatuses.Invalid,
                    "rule_evaluation_context_missing", "rule", "Package, host vocabulary and context are required.");

            List<ContentDiagnostic> diagnostics;
            TopicCondition condition = ContentPackageValidator.ResolveRule(package, ruleReference, out diagnostics);
            if (condition == null)
                return Failure<ContentRuleEvaluation>(diagnostics, "rule_unavailable", "The exact validated Rule could not be resolved.");

            try
            {
                TopicConditionResult result = TopicConditionEvaluator.Evaluate(condition, HostFilteredContext(context, hostVocabulary));
                return ContentResult<ContentRuleEvaluation>.Ready(new ContentRuleEvaluation(
                    ContentStatuses.Ready, result.Truth.ToString(), result.Reason));
            }
            catch (Exception exception)
            {
                return ContentResult<ContentRuleEvaluation>.Failure(ContentStatuses.Invalid,
                    "rule_evaluation_invalid", ruleReference == null ? "rule" : ruleReference.id, exception.Message);
            }
        }

        public static ContentResult<ContentChoiceEvaluation> EvaluateChoice(ContentPackage package, ChoiceRecord choice,
            ContentHostVocabulary hostVocabulary, TopicContext context)
        {
            if (package == null || choice == null || hostVocabulary == null || context == null)
                return ContentResult<ContentChoiceEvaluation>.Failure(ContentStatuses.Invalid,
                    "choice_evaluation_context_missing", "choice", "Package, Choice, host vocabulary and context are required.");

            bool belongsToPackage = package.flows != null && package.flows.Any(flow => flow != null && flow.choices != null &&
                flow.choices.Any(item => item != null && item.id == choice.id && item.stepId == choice.stepId &&
                    item.transitionId == choice.transitionId && item.valueJson == choice.valueJson));
            if (!belongsToPackage || !IsLocalText(package, choice.labelTextRef))
                return ContentResult<ContentChoiceEvaluation>.Failure(ContentStatuses.Invalid,
                    "choice_reference_invalid", choice.id, "The Choice must belong to the package and pin an available label Text.");

            if (choice.availabilityRuleRef == null)
                return ContentResult<ContentChoiceEvaluation>.Ready(new ContentChoiceEvaluation(
                    ContentStatuses.Ready, ContentChoiceStates.Available, "True", null));

            ContentResult<ContentRuleEvaluation> evaluated = Evaluate(package, choice.availabilityRuleRef, hostVocabulary, context);
            if (!evaluated.IsReady)
                return Failure<ContentChoiceEvaluation>(evaluated.diagnostics,
                    evaluated.status == ContentStatuses.Invalid ? "choice_rule_invalid" : "choice_rule_unavailable",
                    "The Choice availability Rule could not be evaluated.", evaluated.status);

            ContentRuleEvaluation result = evaluated.value;
            if (result.truth == "True")
                return ContentResult<ContentChoiceEvaluation>.Ready(new ContentChoiceEvaluation(
                    ContentStatuses.Ready, ContentChoiceStates.Available, result.truth, null));
            if (result.truth == "False" && choice.lockedHintTextRef != null)
            {
                if (!IsLocalText(package, choice.lockedHintTextRef))
                    return ContentResult<ContentChoiceEvaluation>.Failure(ContentStatuses.Invalid,
                        "choice_locked_hint_missing", choice.id, "A locked Choice hint must pin an available Text revision.");
                return ContentResult<ContentChoiceEvaluation>.Ready(new ContentChoiceEvaluation(
                    ContentStatuses.Ready, ContentChoiceStates.Locked, result.truth, choice.lockedHintTextRef));
            }
            return ContentResult<ContentChoiceEvaluation>.Ready(new ContentChoiceEvaluation(
                ContentStatuses.Ready, ContentChoiceStates.Hidden, result.truth, null));
        }

        private static TopicContext HostFilteredContext(TopicContext source, ContentHostVocabulary vocabulary)
        {
            var result = new TopicContext
            {
                stats = source.stats,
                localHour = source.localHour,
                objectsComplete = source.objectsComplete,
                objects = source.objects ?? new List<TopicObjectSnapshot>(),
                facts = new Dictionary<string, bool>(StringComparer.Ordinal)
            };
            if (source.facts != null)
                foreach (KeyValuePair<string, bool> fact in source.facts)
                    if (vocabulary.IsContextFactAllowed(fact.Key)) result.facts.Add(fact.Key, fact.Value);
            return result;
        }

        private static bool IsLocalText(ContentPackage package, ContentRef reference)
        {
            if (reference == null || reference.store != package.storeId || string.IsNullOrEmpty(reference.id) ||
                string.IsNullOrEmpty(reference.revision) || package.texts == null) return false;
            TextRecord text = package.texts.FirstOrDefault(item => item != null && item.id == reference.id && item.revision == reference.revision);
            return text != null && (string.IsNullOrEmpty(reference.expectedHash) || reference.expectedHash == text.semanticHash);
        }

        private static ContentResult<T> Failure<T>(IEnumerable<ContentDiagnostic> diagnostics, string fallbackCode,
            string fallbackMessage, string status = null)
        {
            ContentDiagnostic first = diagnostics == null ? null : diagnostics.FirstOrDefault();
            return ContentResult<T>.Failure(status ?? ContentStatuses.Invalid,
                first == null ? fallbackCode : first.code,
                first == null ? "" : first.path,
                first == null ? fallbackMessage : first.message);
        }
    }
}
