using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using AlterOS.Recording;

namespace AlterOS.Scenarios
{
    public sealed class ScenarioPresentationAnswerOption
    {
        public readonly string stableId, optionId, label;
        internal ScenarioPresentationAnswerOption(string stable, string option, string text)
        { stableId = stable; optionId = option; label = text; }
    }

    public sealed class ScenarioConversationPresentation
    {
        public readonly string runId, stepId, questionId, status;
        public readonly ScenarioBeat question;
        public readonly ReadOnlyCollection<ScenarioPresentationAnswerOption> answerOptions;
        public readonly bool exchangeOpen, scenarioActive, returnPromptAvailable, acceptsFreeText;

        internal ScenarioConversationPresentation(string run, string step, string questionIdentity, string state,
            ScenarioBeat prompt, IEnumerable<ScenarioPresentationAnswerOption> options,
            bool isExchangeOpen, bool isScenarioActive, bool hasReturnPrompt, bool canAcceptFreeText)
        {
            runId = run ?? string.Empty; stepId = step ?? string.Empty; questionId = questionIdentity ?? string.Empty;
            status = state ?? string.Empty; question = prompt;
            answerOptions = Array.AsReadOnly((options ?? Enumerable.Empty<ScenarioPresentationAnswerOption>()).ToArray());
            exchangeOpen = isExchangeOpen; scenarioActive = isScenarioActive; returnPromptAvailable = hasReturnPrompt;
            acceptsFreeText = canAcceptFreeText;
        }
    }

    /// <summary>Projects authoritative Scenario effects into a small, identity-bound UI state.</summary>
    public sealed class ScenarioConversationPresentationModel
    {
        private readonly ScenarioSession session = new ScenarioSession();
        private ScenarioRuntimeSnapshot context;
        private string runId = string.Empty, stepId = string.Empty, questionId = string.Empty;
        private ScenarioBeat question;
        private readonly List<ScenarioPresentationAnswerOption> answerOptions = new List<ScenarioPresentationAnswerOption>();
        private bool exchangeOpen, returnPromptAvailable;
        private string checkpointedDialogueStepId = string.Empty;
        private int nextUnacknowledgedDialogueBeat;
        private readonly HashSet<string> startedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> endedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> observedStepExecutions = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> observedInvocations = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ScenarioHistoryObservation> lastRunContext = new Dictionary<string, ScenarioHistoryObservation>(StringComparer.Ordinal);

        public Action<ScenarioHistoryObservation> HistoryObserver { get; set; }
        public Action<InteractionObservation> RecordingObserver { get; set; }

        public ScenarioConversationPresentation Current => new ScenarioConversationPresentation(runId, stepId, questionId,
            Status(), question, answerOptions, exchangeOpen, session.Running != null || session.Suspended != null,
            returnPromptAvailable, AcceptsFreeText());

        public ScenarioInstanceView Running { get { return session.Running; } }
        public ScenarioInstanceView Suspended { get { return session.Suspended; } }
        public string CurrentStepKind
        {
            get
            {
                ScenarioInstanceView active = session.Running;
                ScenarioStep current = active == null || active.definition == null || active.definition.steps == null
                    ? null : active.definition.steps.FirstOrDefault(item => item != null && item.id == active.currentStepId);
                return current == null ? string.Empty : current.kind;
            }
        }

        public IReadOnlyList<ScenarioEffect> Start(ScenarioDefinition definition, string runIdentity, ScenarioRuntimeSnapshot snapshot)
        {
            context = snapshot; runId = runIdentity ?? string.Empty; exchangeOpen = false; returnPromptAvailable = false;
            IReadOnlyList<ScenarioEffect> effects = session.Start(definition, runId, snapshot);
            RecordStarted(definition == null ? string.Empty : definition.scenarioId, runId,
                effects.Any(effect => effect != null && effect.type == "end_scenario"));
            return Apply(effects);
        }

        public IReadOnlyList<ScenarioEffect> Start(AlterOS.Content.ContentExecutionPlan plan,
            string runIdentity, ScenarioRuntimeSnapshot snapshot)
        {
            context = snapshot; runId = runIdentity ?? string.Empty; exchangeOpen = false; returnPromptAvailable = false;
            checkpointedDialogueStepId = string.Empty; nextUnacknowledgedDialogueBeat = 0;
            IReadOnlyList<ScenarioEffect> effects = session.Start(plan, runId, snapshot);
            RecordStarted(plan == null || plan.rootFlow == null ? string.Empty : plan.rootFlow.id, runId,
                effects.Any(effect => effect != null && effect.type == "end_scenario"));
            return Apply(effects);
        }

        public IReadOnlyList<ScenarioEffect> StartAfterCompletedDialogue(ScenarioDefinition definition,
            string completedStepId, string runIdentity, ScenarioRuntimeSnapshot snapshot)
        {
            context = snapshot; runId = runIdentity ?? string.Empty; exchangeOpen = false; returnPromptAvailable = false;
            IReadOnlyList<ScenarioEffect> effects = session.StartAfterCompletedDialogue(definition, completedStepId, runId, snapshot);
            RecordStarted(definition == null ? string.Empty : definition.scenarioId, runId,
                effects.Any(effect => effect != null && effect.type == "end_scenario"));
            return Apply(effects);
        }

        public IReadOnlyList<ScenarioEffect> StartAfterUnacknowledgedBeat(ScenarioDefinition definition,
            string entryStepId, int nextBeatIndex, string runIdentity, ScenarioRuntimeSnapshot snapshot)
        {
            context = snapshot; runId = runIdentity ?? string.Empty; exchangeOpen = false; returnPromptAvailable = false;
            ScenarioStep entry = definition == null || definition.steps == null ? null :
                definition.steps.FirstOrDefault(item => item != null && item.id == entryStepId && item.kind == "dialogue");
            if (entry == null || definition.entryStepId != entryStepId || entry.beats == null ||
                nextBeatIndex < 0 || nextBeatIndex >= entry.beats.Count)
                return Apply(new[] { new ScenarioEffect("scenario_start_rejected", runId, entryStepId, "invalid_resume_checkpoint") });
            var resumed = new ScenarioDefinition
            {
                scenarioId = definition.scenarioId, topicId = definition.topicId, entryStepId = definition.entryStepId,
                steps = new List<ScenarioStep>(definition.steps)
            };
            int entryIndex = resumed.steps.IndexOf(entry);
            resumed.steps[entryIndex] = new ScenarioStep
            {
                id = entry.id, kind = entry.kind, beats = entry.beats.Skip(nextBeatIndex).ToList(),
                nextStepId = entry.nextStepId, closeExchange = entry.closeExchange
            };
            IReadOnlyList<ScenarioEffect> effects = session.Start(resumed, runId, snapshot);
            RecordStarted(definition.scenarioId, runId,
                effects.Any(effect => effect != null && effect.type == "end_scenario"));
            return Apply(effects);
        }

        public IReadOnlyList<ScenarioEffect> CompleteDialogue(string expectedRunId, string expectedStepId)
        {
            if (!Matches(expectedRunId, expectedStepId, context)) return Empty();
            ScenarioInstanceView active = session.Running;
            ScenarioStep current = active == null || active.definition == null ? null :
                active.definition.steps.FirstOrDefault(item => item != null && item.id == expectedStepId);
            if (current == null || current.kind != "dialogue" || current.beats == null ||
                checkpointedDialogueStepId != expectedStepId || nextUnacknowledgedDialogueBeat < current.beats.Count)
                return Empty();
            IReadOnlyList<ScenarioEffect> effects = session.Advance(
                new ScenarioEvent { kind = "dialogue_complete", runId = runId, stepId = stepId }, context);
            if (effects.Count > 0) RecordStepCompleted(active, "completed");
            return Apply(effects);
        }

        public bool AcknowledgeDialogueBeat(string expectedStepId, string beatId)
        {
            ScenarioInstanceView active = session.Running;
            if (active == null || active.runId != runId || active.currentStepId != expectedStepId) return false;
            ScenarioStep current = active.definition.steps.FirstOrDefault(item => item != null && item.id == expectedStepId);
            if (current == null || current.kind != "dialogue" || current.beats == null) return false;
            if (checkpointedDialogueStepId != expectedStepId)
            {
                checkpointedDialogueStepId = expectedStepId;
                nextUnacknowledgedDialogueBeat = 0;
            }
            int index = current.beats.FindIndex(beat => beat != null && beat.id == beatId);
            if (index < 0 || index > nextUnacknowledgedDialogueBeat) return false;
            if (index == nextUnacknowledgedDialogueBeat)
            {
                nextUnacknowledgedDialogueBeat++;
                RecordForContribution("beat_presented", new Dictionary<string, string>
                {
                    { "scenarioId", active.definition.scenarioId }, { "stepId", expectedStepId },
                    { "beatId", beatId }, { "index", index.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                }, null);
            }
            return true;
        }

        public IReadOnlyList<ScenarioEffect> SelectAnswer(string stableOptionId, ScenarioRuntimeSnapshot currentContext)
        {
            ScenarioPresentationAnswerOption selected = answerOptions.FirstOrDefault(option => option.stableId == stableOptionId);
            if (selected == null || !Matches(runId, stepId, currentContext) || string.IsNullOrEmpty(questionId)) return Empty();
            ScenarioInstanceView active = session.Running;
            IReadOnlyList<ScenarioEffect> effects = session.Advance(new ScenarioEvent
            {
                kind = "answer", runId = runId, stepId = stepId, questionId = questionId,
                answerId = selected.optionId, contextRevision = currentContext.contextRevision
            }, currentContext);
            if (effects.Count > 0)
            {
                RecordAnswer(active, "option", selected.optionId);
                RecordForContribution("answer_selected", new Dictionary<string, string>
                {
                    { "scenarioId", active.definition.scenarioId }, { "stepId", active.currentStepId },
                    { "answerId", selected.optionId }
                }, null);
            }
            return Apply(effects);
        }

        public IReadOnlyList<ScenarioEffect> SubmitFreeText(string text, ScenarioRuntimeSnapshot currentContext)
        {
            if (string.IsNullOrWhiteSpace(text) || !Matches(runId, stepId, currentContext) || string.IsNullOrEmpty(questionId)) return Empty();
            ScenarioInstanceView active = session.Running;
            IReadOnlyList<ScenarioEffect> effects = session.Advance(new ScenarioEvent
            {
                kind = "answer", runId = runId, stepId = stepId, questionId = questionId,
                freeText = text, contextRevision = currentContext.contextRevision
            }, currentContext);
            if (effects.Count > 0)
            {
                RecordAnswer(active, "free_text", string.Empty);
                RecordForContribution("submitted_input", new Dictionary<string, string>
                {
                    { "scenarioId", active.definition.scenarioId }, { "stepId", active.currentStepId },
                    { "inputKind", "free_text" }
                }, text);
            }
            return Apply(effects);
        }

        public IReadOnlyList<ScenarioEffect> WrapUp(ScenarioRuntimeSnapshot currentContext)
        {
            if (currentContext == null || currentContext.runId != runId) return Empty();
            return Apply(session.Advance(new ScenarioEvent { kind = "wrap_up", runId = runId }, currentContext));
        }

        public IReadOnlyList<ScenarioEffect> Advance(ScenarioEvent input, ScenarioRuntimeSnapshot currentContext)
        {
            if (input == null || currentContext == null || currentContext.runId != runId) return Empty();
            context = currentContext;
            ScenarioInstanceView active = session.Running;
            IReadOnlyList<ScenarioEffect> effects = session.Advance(input, currentContext);
            if (input.kind == "activity_result" && effects.Count > 0)
                RecordActivityResult(active, input.success ? "succeeded" : "failed", input.activityId);
            return Apply(effects);
        }

        public void DismissExchange()
        {
            exchangeOpen = false;
            answerOptions.Clear();
            question = null;
        }

        public bool ReplaceTopicOffers(IEnumerable<ScenarioTopicOffer> offers, string generatedDuringTurnId)
        { return session.TopicOffers.Replace(offers, generatedDuringTurnId); }

        public ScenarioTopicOfferSelection SelectTopicOffer(string offerId, ScenarioRuntimeSnapshot currentContext)
        { return session.TopicOffers.Select(offerId, currentContext); }

        public bool CommitTopicOffer(string offerId, ScenarioRuntimeSnapshot currentContext)
        { return session.TopicOffers.CommitSelection(offerId, currentContext); }

        public bool CanSelectAnswer(string stableOptionId, ScenarioRuntimeSnapshot currentContext)
        { return answerOptions.Any(option => option.stableId == stableOptionId) && Matches(runId, stepId, currentContext) && !string.IsNullOrEmpty(questionId); }

        public bool CanSubmitFreeText(string text, ScenarioRuntimeSnapshot currentContext)
        { return !string.IsNullOrWhiteSpace(text) && text.Length <= 600 && AcceptsFreeText() && Matches(runId, stepId, currentContext) && !string.IsNullOrEmpty(questionId); }

        public IReadOnlyList<ScenarioTopicOffer> GetTopicOffers(ScenarioRuntimeSnapshot currentContext)
        { return session.TopicOffers.ForInteraction(currentContext); }

        public bool ReopenForInteraction(ScenarioRuntimeSnapshot currentContext)
        { return ResumeForInteraction(currentContext).Count != 0; }

        public IReadOnlyList<ScenarioEffect> ResumeForInteraction(ScenarioRuntimeSnapshot currentContext)
        {
            if (currentContext == null || currentContext.runId != runId) return Empty();
            context = currentContext;
            ScenarioInstanceView active = session.Running;
            if (active != null)
            {
                ScenarioStep current = active.definition.steps.Find(item => item != null && item.id == active.currentStepId);
                if (current == null) return Empty();
                if (current.kind == "dialogue")
                {
                    int startAt = checkpointedDialogueStepId == current.id ? nextUnacknowledgedDialogueBeat : 0;
                    if (current.beats == null || startAt < 0 || startAt > current.beats.Count) return Empty();
                    if (startAt == current.beats.Count)
                        return Apply(session.Advance(new ScenarioEvent { kind = "dialogue_complete", runId = runId,
                            stepId = current.id }, currentContext));
                    ScenarioStep remaining = new ScenarioStep
                    {
                        id = current.id, kind = current.kind, beats = current.beats.Skip(startAt).ToList(),
                        nextStepId = current.nextStepId, closeExchange = current.closeExchange
                    };
                    return Apply(new[] { new ScenarioEffect("show_dialogue", runId, current.id, "", remaining) });
                }
                else if (current.kind == "ask")
                    return Apply(new[] { new ScenarioEffect("ask_player", runId, current.id, "", current) });
                else return Empty();
            }
            if (session.Suspended != null && session.Suspended.returnPromptPending)
            {
                returnPromptAvailable = true; exchangeOpen = true;
                return new[] { new ScenarioEffect("return_prompt", session.Suspended.runId, session.Suspended.currentStepId) };
            }
            return Empty();
        }

        private IReadOnlyList<ScenarioEffect> Apply(IReadOnlyList<ScenarioEffect> effects)
        {
            foreach (ScenarioEffect effect in effects ?? Empty())
            {
                if (effect == null) continue;
                if (effect.type == "show_dialogue" || effect.type == "ask_player" ||
                    effect.type == "request_activity" || effect.type == "wait_for_condition")
                    RecordStepPresented(effect);
                else if (effect.type == "end_scenario")
                    RecordEnded(effect);
                switch (effect.type)
                {
                    case "show_dialogue":
                        if (stepId != effect.stepId)
                        {
                            checkpointedDialogueStepId = effect.stepId;
                            nextUnacknowledgedDialogueBeat = 0;
                        }
                        stepId = effect.stepId; questionId = string.Empty; question = null; answerOptions.Clear();
                        exchangeOpen = true; returnPromptAvailable = false; break;
                    case "ask_player":
                        stepId = effect.stepId; questionId = effect.step == null ? string.Empty : effect.step.questionId;
                        question = effect.step == null ? null : effect.step.question; answerOptions.Clear();
                        if (effect.step != null && effect.step.options != null)
                            foreach (ScenarioQuestionOption option in effect.step.options)
                                if (option != null) answerOptions.Add(new ScenarioPresentationAnswerOption(
                                    StableOptionId(runId, stepId, questionId, option.optionId), option.optionId, option.label));
                        exchangeOpen = true; returnPromptAvailable = false; break;
                    case "close_exchange": exchangeOpen = false; break;
                    case "end_scenario": exchangeOpen = false; questionId = string.Empty; question = null; answerOptions.Clear(); break;
                    case "return_prompt": returnPromptAvailable = true; break;
                    case "request_activity": case "wait_for_condition":
                        stepId = effect.stepId; exchangeOpen = false; questionId = string.Empty; question = null; answerOptions.Clear(); break;
                }
            }
            return effects ?? Empty();
        }

        private void RecordStarted(string scenarioId, string owningRunId, bool endedImmediately)
        {
            ScenarioInstanceView active = session.Running;
            if ((active == null || active.runId != owningRunId) && !endedImmediately) return;
            if (!startedRuns.Add(owningRunId)) return;
            ScenarioHistoryObservation observation = CreateObservation("scenario_started", owningRunId, scenarioId,
                active == null ? string.Empty : active.currentInvocationId, string.Empty,
                active == null ? string.Empty : active.currentStepId, string.Empty, string.Empty, string.Empty,
                active == null ? string.Empty : active.currentFlowId, string.Empty, string.Empty, 0,
                active == null ? string.Empty : active.currentFlowStoreId,
                active == null ? string.Empty : active.currentFlowRevision,
                active == null ? string.Empty : active.currentFlowHash);
            observation.invocationStarted = active != null && observedInvocations.Add(active.currentInvocationId);
            lastRunContext[owningRunId] = observation;
            Observe(observation);
        }

        private void RecordStepPresented(ScenarioEffect effect)
        {
            ScenarioInstanceView active = session.Running;
            if (active == null || active.runId != effect.runId || string.IsNullOrEmpty(active.currentStepExecutionId) ||
                !observedStepExecutions.Add(active.currentStepExecutionId)) return;
            string sourceStepId = effect.step == null || string.IsNullOrEmpty(effect.step.sourceStepId)
                ? effect.stepId : effect.step.sourceStepId;
            string kind = effect.type == "request_activity" ? "scenario_action_requested" : "scenario_step_presented";
            ScenarioHistoryObservation observation = CreateObservation(kind, effect.runId,
                active.definition == null ? string.Empty : active.definition.scenarioId, active.currentInvocationId,
                active.currentStepExecutionId, sourceStepId, string.Empty, string.Empty, string.Empty,
                active.currentFlowId, active.currentParentInvocationId, active.currentCallExecutionId, active.currentVisitOrdinal,
                active.currentFlowStoreId, active.currentFlowRevision, active.currentFlowHash);
            observation.invocationStarted = observedInvocations.Add(active.currentInvocationId);
            if (effect.type == "request_activity")
            {
                observation.actionExecutionId = effect.runId + "::action::" + active.currentStepExecutionId;
                observation.capabilityId = effect.activityRequest == null ? string.Empty : effect.activityRequest.capabilityId;
            }
            if (effect.type == "ask_player" && effect.step != null)
            {
                string[] optionIds = (effect.step.options ?? new List<ScenarioQuestionOption>())
                    .Where(option => option != null).Select(option => option.optionId).ToArray();
                RecordForContribution("presented_options", new Dictionary<string, string>
                {
                    { "scenarioId", active.definition.scenarioId }, { "stepId", sourceStepId },
                    { "optionIds", string.Join("|", optionIds) }, { "availability", "presented" },
                    { "order", string.Join("|", Enumerable.Range(0, optionIds.Length).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()) }
                }, null);
            }
            lastRunContext[effect.runId] = observation;
            Observe(observation);
        }

        private void RecordAnswer(ScenarioInstanceView active, string answerKind, string optionId)
        {
            if (active == null) return;
            string step = active.definition == null ? active.currentStepId : active.currentStepId;
            ScenarioHistoryObservation observation = CreateObservation("scenario_answer_accepted", active.runId,
                active.definition == null ? string.Empty : active.definition.scenarioId, active.currentInvocationId,
                active.currentStepExecutionId, step, answerKind, optionId, string.Empty,
                active.currentFlowId, active.currentParentInvocationId, active.currentCallExecutionId, active.currentVisitOrdinal,
                active.currentFlowStoreId, active.currentFlowRevision, active.currentFlowHash);
            lastRunContext[active.runId] = observation;
            Observe(observation);
        }

        private void RecordActivityResult(ScenarioInstanceView active, string outcome, string activityId)
        {
            if (active == null) return;
            ScenarioHistoryObservation observation = CreateObservation("scenario_activity_result", active.runId,
                active.definition == null ? string.Empty : active.definition.scenarioId, active.currentInvocationId,
                active.currentStepExecutionId, active.currentStepId, string.Empty, string.Empty, outcome,
                active.currentFlowId, active.currentParentInvocationId, active.currentCallExecutionId, active.currentVisitOrdinal,
                active.currentFlowStoreId, active.currentFlowRevision, active.currentFlowHash);
            observation.actionExecutionId = active.runId + "::action::" + active.currentStepExecutionId;
            ScenarioStep current = active.definition == null || active.definition.steps == null ? null :
                active.definition.steps.FirstOrDefault(item => item != null && item.id == active.currentStepId);
            observation.capabilityId = current == null ? string.Empty : current.capabilityId;
            lastRunContext[active.runId] = observation;
            Observe(observation);
            RecordForContribution("action_result", new Dictionary<string, string>
            {
                { "actionId", observation.actionExecutionId }, { "capabilityId", observation.capabilityId }, { "outcome", outcome }
            }, null);
        }

        private void RecordStepCompleted(ScenarioInstanceView active, string outcome)
        {
            if (active == null) return;
            ScenarioHistoryObservation observation = CreateObservation("scenario_step_completed", active.runId,
                active.definition == null ? string.Empty : active.definition.scenarioId, active.currentInvocationId,
                active.currentStepExecutionId, active.currentStepId, string.Empty, string.Empty, outcome,
                active.currentFlowId, active.currentParentInvocationId, active.currentCallExecutionId, active.currentVisitOrdinal,
                active.currentFlowStoreId, active.currentFlowRevision, active.currentFlowHash);
            lastRunContext[active.runId] = observation;
            Observe(observation);
        }

        private void RecordEnded(ScenarioEffect effect)
        {
            if (!endedRuns.Add(effect.runId)) return;
            ScenarioHistoryObservation previous;
            lastRunContext.TryGetValue(effect.runId, out previous);
            ScenarioHistoryObservation observation = CreateObservation("scenario_ended", effect.runId,
                previous == null ? string.Empty : previous.scenarioId,
                previous == null ? string.Empty : previous.invocationId,
                previous == null ? string.Empty : previous.stepExecutionId,
                effect.stepId, string.Empty, string.Empty, effect.outcome,
                previous == null ? string.Empty : previous.flowId,
                previous == null ? string.Empty : previous.parentInvocationId,
                previous == null ? string.Empty : previous.callExecutionId,
                previous == null ? 0 : previous.visitOrdinal,
                previous == null ? string.Empty : previous.flowStoreId,
                previous == null ? string.Empty : previous.flowRevision,
                previous == null ? string.Empty : previous.flowHash);
            lastRunContext[effect.runId] = observation;
            Observe(observation);
        }

        private static ScenarioHistoryObservation CreateObservation(string kind, string owningRunId, string owningScenarioId,
            string invocation, string execution, string sourceStepId, string answerKind, string optionId, string outcome,
            string flowId, string parentInvocation, string callExecution, int visitOrdinal,
            string flowStoreId = "", string flowRevision = "", string flowHash = "")
        {
            string key = kind + ":" + owningRunId + ":" + execution + ":" + answerKind + ":" + optionId + ":" + outcome;
            return new ScenarioHistoryObservation
            {
                operationKey = key, kind = kind, runId = owningRunId, scenarioId = owningScenarioId,
                invocationId = invocation, parentInvocationId = parentInvocation, callExecutionId = callExecution,
                flowId = flowId, flowStoreId = flowStoreId, flowRevision = flowRevision, flowHash = flowHash,
                stepExecutionId = execution, visitOrdinal = visitOrdinal, stepId = sourceStepId,
                answerKind = answerKind, answerOptionId = optionId, outcome = outcome,
                occurredAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        private void Observe(ScenarioHistoryObservation observation)
        {
            try { HistoryObserver?.Invoke(observation); }
            catch { /* Optional diagnostics must never interrupt Scenario flow. */ }
        }

        private void RecordForContribution(string kind, Dictionary<string, string> metadata, string text)
        {
            try
            {
                RecordingObserver?.Invoke(new InteractionObservation
                {
                    kind = kind, metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    rawText = text, occurredAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            catch { /* Optional contribution capture must never interrupt Scenario flow. */ }
        }

        private bool Matches(string expectedRunId, string expectedStepId, ScenarioRuntimeSnapshot currentContext)
        {
            return currentContext != null && currentContext.runId == runId && currentContext.contextRevision == context?.contextRevision &&
                   expectedRunId == runId && expectedStepId == stepId && session.Running != null && session.Running.runId == runId;
        }

        private string Status()
        {
            if (session.Running == null && session.Suspended == null) return "idle";
            if (returnPromptAvailable) return "return_prompt";
            if (!exchangeOpen) return "background";
            return question == null ? "dialogue" : "ask";
        }

        private bool AcceptsFreeText()
        {
            ScenarioInstanceView active = session.Running;
            if (active == null) return false;
            ScenarioStep current = active.definition.steps.FirstOrDefault(item => item != null && item.id == active.currentStepId);
            return current != null && current.kind == "ask" && current.allowFreeText;
        }

        private static string StableOptionId(string run, string step, string questionIdentity, string option)
        {
            var value = new StringBuilder();
            AppendPart(value, run); AppendPart(value, step); AppendPart(value, questionIdentity); AppendPart(value, option);
            return value.ToString();
        }

        private static void AppendPart(StringBuilder value, string part)
        { part = part ?? string.Empty; value.Append(part.Length).Append(':').Append(part); }

        private static IReadOnlyList<ScenarioEffect> Empty() { return new ScenarioEffect[0]; }
    }
}
