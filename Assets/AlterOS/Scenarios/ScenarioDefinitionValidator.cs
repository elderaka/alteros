using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Conversation;

namespace AlterOS.Scenarios
{
    public static class ScenarioDefinitionValidator
    {
        public const int MaximumSteps = 64;
        public const int MaximumDialogueBeats = 16;
        public const int MaximumQuestionOptions = 8;
        public const int MaximumHeadBeats = 4;
        public const int MaximumTopicOffers = 3;

        public static IReadOnlyList<string> Validate(ScenarioDefinition definition, ScenarioRuntimeSnapshot context)
        {
            var errors = new List<string>();
            if (definition == null) { errors.Add("Scenario definition is required."); return errors.AsReadOnly(); }
            if (context != null) errors.AddRange(ValidateRuntime(context));
            Require(definition.entryStepId, "entryStepId", 64, errors);
            if (definition.scenarioId != null && definition.scenarioId.Length > 128) errors.Add("scenarioId exceeds its limit.");
            if (definition.topicId == null || definition.topicId.Length > 128) errors.Add("topicId is invalid.");
            if (definition.steps == null || definition.steps.Count == 0 || definition.steps.Count > MaximumSteps)
            { errors.Add("Scenario must contain one to sixty-four steps."); return errors.AsReadOnly(); }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioStep step in definition.steps)
            {
                if (step == null) { errors.Add("Scenario step cannot be null."); continue; }
                Require(step.id, "step.id", 64, errors);
                if (!ids.Add(step.id ?? "")) errors.Add("Scenario step IDs must be unique.");
                ValidateStep(step, context, errors);
            }
            if (!ids.Contains(definition.entryStepId ?? "")) errors.Add("Scenario entry step does not exist.");
            foreach (ScenarioStep step in definition.steps)
                foreach (string target in Successors(step))
                    if (!ids.Contains(target ?? "")) errors.Add("Scenario successor does not exist.");
            if (context != null && !string.IsNullOrEmpty(definition.topicId) &&
                !context.knownTopicIds.Contains(definition.topicId)) errors.Add("Scenario Topic is outside the host context.");
            return errors.AsReadOnly();
        }

        public static IReadOnlyList<string> ValidateRuntime(ScenarioRuntimeSnapshot context)
        {
            var errors = new List<string>();
            if (context == null) { errors.Add("Runtime snapshot is required."); return errors.AsReadOnly(); }
            if (string.IsNullOrWhiteSpace(context.runId) || string.IsNullOrWhiteSpace(context.turnId) || context.contextRevision < 0)
                errors.Add("Scenario runtime identity is invalid.");
            if (context.knownTopicIds.Distinct(StringComparer.Ordinal).Count() != context.knownTopicIds.Count ||
                context.knownIntentIds.Distinct(StringComparer.Ordinal).Count() != context.knownIntentIds.Count ||
                context.allowedExpressions.Distinct(StringComparer.Ordinal).Count() != context.allowedExpressions.Count ||
                context.waitConditions.Distinct(StringComparer.Ordinal).Count() != context.waitConditions.Count)
                errors.Add("Scenario runtime allowlists contain duplicate IDs.");
            if (context.objects.GroupBy(item => item == null ? "" : item.objectId, StringComparer.Ordinal).Any(group => group.Count() != 1))
                errors.Add("Runtime object binding is ambiguous.");
            if (context.capabilities.GroupBy(item => item == null ? "" : item.capabilityId, StringComparer.Ordinal).Any(group => group.Count() != 1))
                errors.Add("Runtime capability is ambiguous.");
            foreach (ScenarioCapabilitySnapshot capability in context.capabilities)
            {
                if (capability == null) { errors.Add("Runtime capability cannot be null."); continue; }
                if (capability.argumentBindings.GroupBy(binding => binding == null ? "" : binding.name, StringComparer.Ordinal).Any(group => group.Count() != 1))
                    errors.Add("Runtime capability argument bindings are ambiguous.");
            }
            return errors.AsReadOnly();
        }

        public static IReadOnlyList<string> ValidateStage(ScenarioStageResult stage, ScenarioRuntimeSnapshot context)
        {
            var errors = new List<string>();
            if (stage == null) { errors.Add("Scenario stage is required."); return errors.AsReadOnly(); }
            if (stage.stage == "head")
            {
                if (stage.beats == null || stage.beats.Count < 1 || stage.beats.Count > MaximumHeadBeats)
                    errors.Add("Head must contain one to four beats.");
                ValidateBeats(stage.beats, context, errors);
                if (stage.scenario != null || (stage.topicOffers != null && stage.topicOffers.Count != 0))
                    errors.Add("Head stage cannot contain a Scenario or Topic offers.");
            }
            else if (stage.stage == "main")
            {
                errors.AddRange(Validate(stage.scenario, context));
                if (stage.topicOffers == null || stage.topicOffers.Count > MaximumTopicOffers)
                    errors.Add("Main stage allows at most three Topic offers.");
                else
                {
                    var offers = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ScenarioTopicOffer offer in stage.topicOffers)
                    {
                        if (offer == null) { errors.Add("Topic offer cannot be null."); continue; }
                        Require(offer.offerId, "offer.offerId", 64, errors);
                        Require(offer.label, "offer.label", 80, errors);
                        if (HasNonEnglishScript(offer.label)) errors.Add("Topic offer label must use the English display script.");
                        if (!offers.Add(offer.offerId ?? "")) errors.Add("Topic offer IDs must be unique.");
                        if (string.IsNullOrEmpty(offer.topicId) == string.IsNullOrEmpty(offer.intentId))
                            errors.Add("Topic offer must select exactly one Topic or intent.");
                    }
                }
                ScenarioStep segment = stage.scenario != null && stage.scenario.steps != null
                    ? stage.scenario.steps.Find(step => step != null && step.id == stage.segmentStepId) : null;
                if (segment == null || segment.kind != "dialogue" || stage.segmentStepId != stage.scenario.entryStepId)
                    errors.Add("Main stage must identify the Scenario entry dialogue segment.");
            }
            else errors.Add("Unsupported Scenario stage.");
            return errors.AsReadOnly();
        }

        private static void ValidateStep(ScenarioStep step, ScenarioRuntimeSnapshot context, List<string> errors)
        {
            switch (step.kind)
            {
                case "dialogue":
                    if (step.beats == null || step.beats.Count < 1 || step.beats.Count > MaximumDialogueBeats)
                        errors.Add("Dialogue step must contain one to sixteen beats.");
                    ValidateBeats(step.beats, context, errors);
                    Require(step.nextStepId, "dialogue.nextStepId", 64, errors);
                    break;
                case "ask":
                    Require(step.questionId, "ask.questionId", 64, errors);
                    ValidateBeat(step.question, context, errors);
                    if (step.options == null || step.options.Count > MaximumQuestionOptions) errors.Add("Ask step allows at most eight options.");
                    else
                    {
                        var options = new HashSet<string>(StringComparer.Ordinal);
                        foreach (ScenarioQuestionOption option in step.options)
                        {
                            if (option == null) { errors.Add("Question option cannot be null."); continue; }
                            Require(option.optionId, "option.optionId", 64, errors);
                            Require(option.label, "option.label", 80, errors);
                            Require(option.value, "option.value", 256, errors);
                            Require(option.nextStepId, "option.nextStepId", 64, errors);
                            if (!options.Add(option.optionId ?? "")) errors.Add("Question option IDs must be unique.");
                        }
                    }
                    if (step.allowFreeText) Require(step.freeTextNextStepId, "ask.freeTextNextStepId", 64, errors);
                    Require(step.skipNextStepId, "ask.skipNextStepId", 64, errors);
                    Require(step.cancelNextStepId, "ask.cancelNextStepId", 64, errors);
                    break;
                case "action":
                    Require(step.capabilityId, "action.capabilityId", 128, errors);
                    Require(step.successNextStepId, "action.successNextStepId", 64, errors);
                    Require(step.failureNextStepId, "action.failureNextStepId", 64, errors);
                    ValidateAction(step, context, errors);
                    break;
                case "wait":
                    Require(step.activityId, "wait.activityId", 128, errors);
                    Require(step.conditionId, "wait.conditionId", 128, errors);
                    Require(step.objective, "wait.objective", 240, errors);
                    Require(step.successNextStepId, "wait.successNextStepId", 64, errors);
                    Require(step.failureNextStepId, "wait.failureNextStepId", 64, errors);
                    if (context != null && !context.waitConditions.Contains(step.conditionId)) errors.Add("Wait condition is not host-allowlisted.");
                    break;
                case "branch":
                    Require(step.sourceId, "branch.sourceId", 64, errors);
                    Require(step.defaultNextStepId, "branch.defaultNextStepId", 64, errors);
                    if (step.cases == null || step.cases.Count == 0 || step.cases.Count > 16) errors.Add("Branch case count is invalid.");
                    else
                    {
                        var cases = new HashSet<string>(StringComparer.Ordinal);
                        foreach (ScenarioBranchCase item in step.cases)
                        {
                            if (item == null) { errors.Add("Branch case cannot be null."); continue; }
                            Require(item.value, "branch.value", 128, errors);
                            Require(item.nextStepId, "branch.nextStepId", 64, errors);
                            if (!cases.Add(item.value ?? "")) errors.Add("Branch values must be unique.");
                        }
                    }
                    break;
                case "end":
                    if (step.outcome != "completed" && step.outcome != "player_ended" && step.outcome != "failed")
                        errors.Add("End outcome is unsupported.");
                    break;
                default: errors.Add("Unsupported Scenario step kind."); break;
            }
        }

        private static void ValidateAction(ScenarioStep step, ScenarioRuntimeSnapshot context, List<string> errors)
        {
            if (context == null) return;
            ScenarioCapabilitySnapshot capability = context.capabilities.FirstOrDefault(item => item != null && item.capabilityId == step.capabilityId);
            if (capability == null) { errors.Add("Action capability is not host-allowlisted."); return; }
            if (context.capabilities.Count(item => item != null && item.capabilityId == step.capabilityId) != 1)
                errors.Add("Action capability binding is ambiguous.");
            if (step.arguments == null || step.arguments.Count > 8) { errors.Add("Action argument count is invalid."); return; }
            if (step.arguments.GroupBy(arg => arg == null ? "" : arg.name, StringComparer.Ordinal).Any(group => group.Count() != 1))
                errors.Add("Action argument names must be unique.");
            var suppliedNames = new HashSet<string>(step.arguments.Where(item => item != null).Select(item => item.name), StringComparer.Ordinal);
            var allowedNames = new HashSet<string>(capability.argumentBindings.Where(item => item != null).Select(item => item.name), StringComparer.Ordinal);
            if (!suppliedNames.SetEquals(allowedNames)) errors.Add("Action arguments do not match the host capability contract.");
            foreach (ScenarioActionArgument argument in step.arguments)
            {
                if (argument == null) { errors.Add("Action argument cannot be null."); continue; }
                ScenarioArgumentBinding binding = capability.argumentBindings.FirstOrDefault(item => item != null && item.name == argument.name);
                if (binding == null || !binding.values.Contains(argument.value)) errors.Add("Action argument is not host-bound.");
                if (argument.name == "targetObjectId" && context.objects.Count(item => item != null && item.objectId == argument.value) != 1)
                    errors.Add("Action target object is missing or ambiguous.");
            }
        }

        internal static IReadOnlyList<string> ValidateActionStep(ScenarioStep step, ScenarioRuntimeSnapshot context)
        {
            var errors = new List<string>();
            if (step == null || step.kind != "action") errors.Add("An Action Step is required.");
            else ValidateAction(step, context, errors);
            return errors.AsReadOnly();
        }

        private static IEnumerable<string> Successors(ScenarioStep step)
        {
            if (step == null) yield break;
            switch (step.kind)
            {
                case "dialogue": yield return step.nextStepId; break;
                case "ask":
                    if (step.options != null) foreach (ScenarioQuestionOption option in step.options) if (option != null) yield return option.nextStepId;
                    if (step.allowFreeText) yield return step.freeTextNextStepId;
                    yield return step.skipNextStepId; yield return step.cancelNextStepId; break;
                case "action": case "wait": yield return step.successNextStepId; yield return step.failureNextStepId; break;
                case "branch":
                    if (step.cases != null) foreach (ScenarioBranchCase item in step.cases) if (item != null) yield return item.nextStepId;
                    yield return step.defaultNextStepId; break;
            }
        }

        private static void ValidateBeats(IEnumerable<ScenarioBeat> beats, ScenarioRuntimeSnapshot context, List<string> errors)
        { if (beats != null) foreach (ScenarioBeat beat in beats) ValidateBeat(beat, context, errors); }

        private static void ValidateBeat(ScenarioBeat beat, ScenarioRuntimeSnapshot context, List<string> errors)
        {
            if (beat == null) { errors.Add("Scenario beat cannot be null."); return; }
            Require(beat.id, "beat.id", 64, errors);
            if (string.IsNullOrEmpty(beat.text))
            {
                if (string.IsNullOrWhiteSpace(beat.displayText) || beat.displayText.Length > 600)
                    errors.Add("A speech-silent Scenario beat still requires bounded display text.");
            }
            else Require(beat.text, "beat.text", 600, errors);
            Require(beat.displayText, "beat.displayText", 600, errors);
            if (beat.language != "japanese") errors.Add("Scenario speech language must be japanese.");
            if (beat.expression != null && beat.expression.Length > 64) errors.Add("Beat expression exceeds its limit.");
            if (beat.voice != null && beat.voice.Length > 64) errors.Add("Beat voice exceeds its limit.");
            if (beat.audioKey != null && beat.audioKey.Length > 512) errors.Add("Beat audio key exceeds its limit.");
            if (beat.animation != null && beat.animation.Length > 128) errors.Add("Beat animation exceeds its limit.");
            if (beat.speaker != null && beat.speaker.Length > 64) errors.Add("Beat speaker exceeds its limit.");
            if (!string.IsNullOrEmpty(beat.expression) && context != null && !context.allowedExpressions.Contains(beat.expression))
                errors.Add("Beat expression is not host-allowlisted.");
        }

        private static void Require(string value, string field, int maximum, List<string> errors)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) errors.Add(field + " must be nonempty and within its limit."); }

        private static bool HasNonEnglishScript(string value)
        {
            if (value == null) return true;
            foreach (char character in value)
                if (char.IsSurrogate(character) || (char.IsLetter(character) && character > '\u024F')) return true;
            return false;
        }
    }
}
