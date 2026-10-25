using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Conversation;

namespace AlterOS.Scenarios
{
    public enum DiversionDecision
    {
        Pause,
        End,
        BackOut,
        ReplaceSuspended
    }

    public sealed class ScenarioEvent
    {
        public string kind = "";
        public string runId = "";
        public string stepId = "";
        public string questionId = "";
        public string answerId = "";
        public string freeText = "";
        public string value = "";
        public string activityId = "";
        public string conditionId = "";
        public string participant = "";
        public long contextRevision;
        public double elapsedSeconds;
        public bool success;
        public bool canTalk;
        public bool? focusAllowed;
        public bool quiet;
        public Dictionary<string, AlterOS.Content.FlowValue> actionResults = new Dictionary<string, AlterOS.Content.FlowValue>(StringComparer.Ordinal);
        public ScenarioHostEffectResult hostEffectResult;
    }

    public sealed class ScenarioEffect
    {
        public readonly string type, runId, stepId, outcome, stage, stageId, beatId, eventId;
        public readonly int beatIndex;
        public readonly ScenarioStep step;
        public readonly ScenarioBeat beat;
        public readonly ScenarioStageResult scenarioStage;
        public readonly ActivityRequest activityRequest;
        public readonly ActivitySequenceRequest activitySequenceRequest;
        public readonly ScenarioHostEffectRequest hostEffectRequest;
        internal ScenarioEffect(string effectType, string run, string stepIdentity, string result = "",
            ScenarioStep currentStep = null, ActivityRequest request = null, string stageKind = "", string stageIdentity = "",
            string beatIdentity = "", string transcriptEventId = "", ScenarioBeat dialogueBeat = null,
            ScenarioStageResult stageResult = null, int stageBeatIndex = 0,
            ActivitySequenceRequest sequenceRequest = null, ScenarioHostEffectRequest hostEffect = null)
        {
            type = effectType ?? ""; runId = run ?? ""; stepId = stepIdentity ?? ""; outcome = result ?? "";
            step = currentStep; activityRequest = request; stage = stageKind ?? ""; stageId = stageIdentity ?? "";
            beatId = beatIdentity ?? ""; eventId = transcriptEventId ?? ""; beat = dialogueBeat; scenarioStage = stageResult;
            beatIndex = stageBeatIndex;
            activitySequenceRequest = sequenceRequest;
            hostEffectRequest = hostEffect;
        }
    }

    public sealed class ScenarioInstanceView
    {
        public readonly string runId, currentStepId, currentInvocationId, currentParentInvocationId, currentCallExecutionId,
            currentStepExecutionId, currentFlowId, currentFlowStoreId, currentFlowRevision, currentFlowHash, status, outcome, activeActivityId;
        public readonly int currentVisitOrdinal;
        public readonly bool returnPromptPending;
        public readonly ScenarioDefinition definition;
        internal ScenarioInstanceView(ScenarioInstanceState state)
        {
            runId = state.runId; currentStepId = state.currentStepId; status = state.status;
            currentInvocationId = state.CurrentInvocationId; currentStepExecutionId = state.currentStepExecutionId;
            currentParentInvocationId = state.CurrentParentInvocationId; currentCallExecutionId = state.CurrentCallExecutionId;
            currentFlowId = state.CurrentFlowId; currentVisitOrdinal = state.CurrentVisitOrdinal;
            AlterOS.Content.ContentRef flowReference = state.CurrentFlowReference;
            currentFlowStoreId = flowReference == null ? string.Empty : flowReference.store;
            currentFlowRevision = flowReference == null ? string.Empty : flowReference.revision;
            currentFlowHash = flowReference == null ? string.Empty : flowReference.expectedHash;
            outcome = state.outcome; activeActivityId = state.activeActivityId; returnPromptPending = state.returnPromptPending;
            definition = state.definition;
        }
    }

    internal sealed class ScenarioInstanceState
    {
        internal readonly string runId;
        internal readonly ScenarioDefinition definition;
        internal readonly ContentFlowExecutionState contentFlow;
        internal string currentStepId, currentStepExecutionId = "", status = "running", outcome = "", activeActivityId = "", activityParticipant = "";
        internal readonly Dictionary<string, string> completedDialogueExecutions = new Dictionary<string, string>(StringComparer.Ordinal);
        internal int stepExecutionSequence;
        internal bool returnPromptPending, returnPromptPresented;
        internal ScenarioInstanceState(string id, ScenarioDefinition scenario)
        { runId = id; definition = scenario; currentStepId = scenario.entryStepId; }

        internal ScenarioInstanceState(string id, AlterOS.Content.ContentExecutionPlan plan)
        {
            runId = id;
            contentFlow = new ContentFlowExecutionState(plan, id);
            definition = contentFlow.RootProjection;
            currentStepId = definition.entryStepId;
        }

        internal ScenarioStep CurrentStep
        { get { return contentFlow != null ? contentFlow.CurrentProjectedStep : definition.steps.Find(item => item != null && item.id == currentStepId); } }

        internal string CurrentInvocationId
        {
            get { return runId + "::" + (contentFlow == null ? "root" : contentFlow.CurrentInvocationId); }
        }
        internal string CurrentParentInvocationId
        {
            get
            {
                string parent = contentFlow == null ? string.Empty : contentFlow.CurrentParentInvocationId;
                return string.IsNullOrEmpty(parent) ? string.Empty : runId + "::" + parent;
            }
        }
        internal string CurrentCallExecutionId
        {
            get
            {
                string call = contentFlow == null ? string.Empty : contentFlow.CurrentCallExecutionId;
                return string.IsNullOrEmpty(call) ? string.Empty : runId + "::" + call;
            }
        }
        internal string CurrentFlowId { get { return contentFlow == null ? definition.scenarioId : contentFlow.CurrentFlowId; } }
        internal AlterOS.Content.ContentRef CurrentFlowReference { get { return contentFlow == null ? null : contentFlow.CurrentFlowReference; } }
        internal int CurrentVisitOrdinal { get { return contentFlow == null ? stepExecutionSequence : contentFlow.CurrentVisitOrdinal; } }

        internal void BeginStepExecution(ScenarioStep step)
        {
            if (step == null) { currentStepExecutionId = string.Empty; return; }
            string execution = contentFlow == null
                ? "step_" + (++stepExecutionSequence).ToString("D8", System.Globalization.CultureInfo.InvariantCulture)
                : contentFlow.CurrentExecutionId;
            if (contentFlow != null) stepExecutionSequence++;
            currentStepExecutionId = runId + "::" + execution;
        }

        internal ScenarioInstanceView View { get { return new ScenarioInstanceView(this); } }
    }

    /// <summary>Pure in-memory lifecycle for one running and one suspended Scenario occurrence.</summary>
    public sealed class ScenarioSession
    {
        private const int MaximumAutomaticTransitions = 64;
        private ScenarioInstanceState running;
        private ScenarioInstanceState suspended;
        private readonly ScenarioQuestionState question = new ScenarioQuestionState();

        public ScenarioInstanceView Running { get { return running == null ? null : running.View; } }
        public ScenarioInstanceView Suspended { get { return suspended == null ? null : suspended.View; } }
        public ScenarioTopicOffers TopicOffers { get; } = new ScenarioTopicOffers();

        public IReadOnlyList<ScenarioEffect> Start(ScenarioDefinition definition, string runId, ScenarioRuntimeSnapshot context)
        {
            if (running != null) return One(new ScenarioEffect("scenario_already_running", runId, ""));
            if (definition == null || string.IsNullOrWhiteSpace(runId) || context == null || context.runId != runId ||
                ScenarioDefinitionValidator.Validate(definition, context).Count != 0)
                return One(new ScenarioEffect("scenario_start_rejected", runId, "", "invalid_definition"));
            running = new ScenarioInstanceState(runId, definition);
            question.Clear();
            return EnterCurrent(running, context, "");
        }

        public IReadOnlyList<ScenarioEffect> Start(AlterOS.Content.ContentExecutionPlan plan, string runId,
            ScenarioRuntimeSnapshot context)
        {
            if (running != null) return One(new ScenarioEffect("scenario_already_running", runId, ""));
            if (plan == null || string.IsNullOrWhiteSpace(runId) || context == null || plan.context == null ||
                context.runId != runId || plan.context.runId != runId || plan.context.contextRevision != context.contextRevision)
                return One(new ScenarioEffect("scenario_start_rejected", runId, "", "invalid_content_plan"));
            running = new ScenarioInstanceState(runId, plan);
            question.Clear();
            return EnterCurrent(running, context, "");
        }

        public IReadOnlyList<ScenarioEffect> StartAfterCompletedDialogue(ScenarioDefinition definition,
            string completedStepId, string runId, ScenarioRuntimeSnapshot context)
        {
            if (running != null) return One(new ScenarioEffect("scenario_already_running", runId, ""));
            if (definition == null || string.IsNullOrWhiteSpace(runId) || context == null || context.runId != runId ||
                definition.entryStepId != completedStepId || ScenarioDefinitionValidator.Validate(definition, context).Count != 0)
                return One(new ScenarioEffect("scenario_start_rejected", runId, "", "invalid_checkpoint"));
            ScenarioStep completed = definition.steps.Find(item => item != null && item.id == completedStepId && item.kind == "dialogue");
            if (completed == null) return One(new ScenarioEffect("scenario_start_rejected", runId, "", "invalid_checkpoint"));
            running = new ScenarioInstanceState(runId, definition) { currentStepId = completed.nextStepId };
            question.Clear();
            return EnterCurrent(running, context, "");
        }

        public IReadOnlyList<ScenarioEffect> Advance(ScenarioEvent input, ScenarioRuntimeSnapshot context)
        {
            var effects = new List<ScenarioEffect>();
            if (input == null || context == null) return effects.AsReadOnly();
            if (input.kind == "time_advanced") return effects.AsReadOnly();
            if (input.kind == "continue_suspended") return ResumeSuspended(input, context);
            if (input.kind == "wrap_up")
            {
                if (running != null && input.runId == running.runId && context.runId == running.runId)
                {
                    effects.Add(new ScenarioEffect("close_exchange", running.runId, running.currentStepId));
                    EndRunning("player_ended", effects);
                    return effects.AsReadOnly();
                }
                if (suspended != null && input.runId == suspended.runId && context.runId == suspended.runId)
                {
                    effects.Add(new ScenarioEffect("close_exchange", suspended.runId, suspended.currentStepId));
                    EndInstance(suspended, "player_ended", effects);
                    suspended = null; question.Clear();
                    return effects.AsReadOnly();
                }
                return effects.AsReadOnly();
            }
            if (suspended != null && input.runId == suspended.runId && context.runId != suspended.runId) return effects.AsReadOnly();
            if (running != null && input.runId == running.runId && context.runId != running.runId) return effects.AsReadOnly();
            if (suspended != null && input.runId == suspended.runId &&
                (input.kind == "activity_completed" || input.kind == "player_interaction" || input.kind == "can_talk_available"))
            {
                AdvanceSuspendedPresence(input, context, effects);
                return effects.AsReadOnly();
            }
            if (running == null)
            {
                AdvanceSuspendedPresence(input, context, effects);
                return effects.AsReadOnly();
            }
            if (input.runId != running.runId) return effects.AsReadOnly();
            ScenarioStep current = running.CurrentStep;
            if (current == null || (!string.IsNullOrEmpty(input.stepId) && input.stepId != current.id)) return effects.AsReadOnly();
            if (running.contentFlow != null) return AdvanceContentFlow(input, context, current);

            string next = "";
            string branchValue = "";
            if (current.kind == "dialogue" && input.kind == "dialogue_complete")
            {
                RecordCompletedDialogue(running, current);
                if (current.closeExchange) effects.Add(new ScenarioEffect("close_exchange", running.runId, current.id));
                next = current.nextStepId;
            }
            else if (current.kind == "ask" && (input.kind == "answer" || input.kind == "skip" || input.kind == "cancel"))
            {
                if ((input.kind == "skip" && !running.contentFlow.HasTransition("skip")) ||
                    (input.kind == "cancel" && !running.contentFlow.HasTransition("cancel"))) return effects.AsReadOnly();
                QuestionAnswerResult answer = input.kind == "answer"
                    ? question.Answer(input.questionId, input.answerId, input.freeText, input.contextRevision)
                    : input.kind == "skip" ? question.Skip(input.questionId, input.contextRevision) : question.Cancel(input.questionId, input.contextRevision);
                if (!answer.accepted) return effects.AsReadOnly();
                next = answer.nextStepId;
                branchValue = answer.answerId.Length == 0 ? answer.kind : answer.answerId;
            }
            else if (current.kind == "action" && current.activitySequence != null && input.kind == "activity_accepted")
            {
                if (string.IsNullOrWhiteSpace(input.activityId) ||
                    !string.IsNullOrEmpty(running.activeActivityId) && running.activeActivityId != input.activityId)
                    return effects.AsReadOnly();
                running.activeActivityId = input.activityId;
                return effects.AsReadOnly();
            }
            else if (current.kind == "action" && input.kind == "activity_result")
            {
                if (current.activitySequence != null && input.activityId != running.activeActivityId)
                    return effects.AsReadOnly();
                bool accepted = input.success && !string.IsNullOrWhiteSpace(input.activityId);
                if (accepted)
                {
                    running.activeActivityId = input.activityId;
                    running.activityParticipant = NormalizeParticipant(input.participant);
                    next = current.successNextStepId;
                }
                else next = current.failureNextStepId;
            }
            else if (current.kind == "host_effect" && input.kind == "host_effect_result")
            {
                ScenarioHostEffectResult result = input.hostEffectResult;
                if (!IsCurrentHostEffectResult(running, current, result)) return effects.AsReadOnly();
                next = result.status == ScenarioHostEffectStatuses.Applied || result.status == ScenarioHostEffectStatuses.Replayed
                    ? current.successNextStepId : current.failureNextStepId;
            }
            else if (current.kind == "wait" && (input.kind == "condition_met" || input.kind == "condition_failed"))
            {
                string expectedActivity = string.IsNullOrEmpty(running.activeActivityId) ? current.activityId : running.activeActivityId;
                if (input.activityId != expectedActivity || input.conditionId != current.conditionId) return effects.AsReadOnly();
                next = input.kind == "condition_met" ? current.successNextStepId : current.failureNextStepId;
            }
            else if (current.kind == "branch" && input.kind == "branch_value")
            {
                branchValue = input.value ?? "";
                next = SelectBranch(current, branchValue);
            }
            else return effects.AsReadOnly();

            running.currentStepId = next;
            effects.AddRange(EnterCurrent(running, context, branchValue));
            return effects.AsReadOnly();
        }

        public IReadOnlyList<ScenarioEffect> RequestDiversion(ScenarioDefinition nextDefinition, string nextRunId,
            DiversionDecision? decision, ScenarioRuntimeSnapshot context)
        {
            if (context == null || nextDefinition == null || string.IsNullOrWhiteSpace(nextRunId) || context.runId != nextRunId ||
                ScenarioDefinitionValidator.Validate(nextDefinition, context).Count != 0)
                return One(new ScenarioEffect("diversion_rejected", nextRunId, "", "invalid_definition"));
            if (running == null)
                return Start(nextDefinition, nextRunId, context);
            if (!decision.HasValue) return One(new ScenarioEffect("confirm_diversion", running.runId, running.currentStepId));
            if (decision.Value == DiversionDecision.BackOut)
                return One(new ScenarioEffect("diversion_backed_out", running.runId, running.currentStepId));

            var effects = new List<ScenarioEffect>();
            if (decision.Value == DiversionDecision.Pause)
            {
                if (suspended != null) return One(new ScenarioEffect("suspended_slot_full", running.runId, running.currentStepId));
                SuspendRunning(effects);
            }
            else if (decision.Value == DiversionDecision.End)
            {
                effects.Add(new ScenarioEffect("close_exchange", running.runId, running.currentStepId));
                EndRunning("player_ended", effects);
            }
            else if (decision.Value == DiversionDecision.ReplaceSuspended)
            {
                if (suspended == null) return One(new ScenarioEffect("no_suspended_scenario", running.runId, running.currentStepId));
                EndInstance(suspended, "player_ended", effects);
                suspended = null;
                if (running != null) SuspendRunning(effects);
            }
            else return One(new ScenarioEffect("diversion_rejected", running.runId, running.currentStepId));

            effects.AddRange(Start(nextDefinition, nextRunId, context));
            return effects.AsReadOnly();
        }

        public IReadOnlyList<ScenarioEffect> Quit()
        {
            var effects = new List<ScenarioEffect>();
            if (running != null) EndInstance(running, "interrupted_by_quit", effects);
            if (suspended != null) EndInstance(suspended, "interrupted_by_quit", effects);
            running = null; suspended = null; question.Clear(); TopicOffers.Clear();
            return effects.AsReadOnly();
        }

        private IReadOnlyList<ScenarioEffect> ResumeSuspended(ScenarioEvent input, ScenarioRuntimeSnapshot context)
        {
            if (running != null || suspended == null || input.runId != suspended.runId || context.runId != suspended.runId) return new ScenarioEffect[0];
            running = suspended; suspended = null; running.status = "running";
            if (running.contentFlow != null) running.contentFlow.RefreshForResume();
            return EnterCurrent(running, context, "");
        }

        private void AdvanceSuspendedPresence(ScenarioEvent input, ScenarioRuntimeSnapshot context, List<ScenarioEffect> effects)
        {
            if (suspended == null || input.runId != suspended.runId) return;
            if (input.kind == "activity_completed")
            {
                if (!string.IsNullOrEmpty(suspended.activeActivityId) && input.activityId != suspended.activeActivityId) return;
                suspended.activeActivityId = "";
                string participant = NormalizeParticipant(string.IsNullOrEmpty(input.participant) ? suspended.activityParticipant : input.participant);
                if (participant == "self_care") return;
                suspended.returnPromptPending = true;
                if (participant == "alterra" && CanPresentReturnPrompt(input)) AddReturnPrompt(suspended, effects);
            }
            else if ((input.kind == "player_interaction" || input.kind == "can_talk_available") && suspended.returnPromptPending && CanPresentReturnPrompt(input))
                AddReturnPrompt(suspended, effects);
        }

        private static bool CanPresentReturnPrompt(ScenarioEvent input)
        { return input.canTalk && input.focusAllowed == true && !input.quiet; }

        private static void AddReturnPrompt(ScenarioInstanceState state, List<ScenarioEffect> effects)
        {
            if (state.returnPromptPresented) return;
            state.returnPromptPresented = true;
            effects.Add(new ScenarioEffect("return_prompt", state.runId, state.currentStepId));
        }

        private IReadOnlyList<ScenarioEffect> EnterCurrent(ScenarioInstanceState state, ScenarioRuntimeSnapshot context, string branchValue)
        {
            if (state.contentFlow != null) return EnterContentFlow(state, context, branchValue);
            var effects = new List<ScenarioEffect>();
            int transitions = 0;
            while (state == running && transitions <= MaximumAutomaticTransitions)
            {
                ScenarioStep step = state.CurrentStep;
                if (step == null) { EndRunning("failed", effects); break; }
                state.BeginStepExecution(step);
                if (step.kind == "dialogue")
                {
                    effects.Add(new ScenarioEffect("show_dialogue", state.runId, step.id, "", step));
                    break;
                }
                if (step.kind == "ask")
                {
                    if (!question.Begin(step, state.runId, context.contextRevision)) { EndRunning("failed", effects); break; }
                    effects.Add(new ScenarioEffect("ask_player", state.runId, step.id, "", step));
                    break;
                }
                if (step.kind == "action")
                {
                    if (step.activitySequence != null)
                    {
                        var sequenceRequest = new ActivitySequenceRequest(step.activitySequence, step.actionRef,
                            state.runId, state.CurrentInvocationId, state.currentStepExecutionId,
                            Guid.NewGuid().ToString("N"), context, step.activitySequenceArguments, step.id);
                        state.activeActivityId = sequenceRequest.actionInvocationId;
                        effects.Add(new ScenarioEffect("request_activity", state.runId, step.id, "", step,
                            sequenceRequest: sequenceRequest));
                        break;
                    }
                    var request = new ActivityRequest(state.runId, step.id, step.capabilityId, step.arguments);
                    effects.Add(new ScenarioEffect("request_activity", state.runId, step.id, "", step, request));
                    break;
                }
                if (step.kind == "host_effect")
                {
                    ScenarioHostEffectRequest request;
                    if (!TryCreateHostEffectRequest(state, step, out request))
                    {
                        state.currentStepId = step.failureNextStepId;
                        transitions++;
                        continue;
                    }
                    effects.Add(new ScenarioEffect("request_host_effect", state.runId, step.id, "", step,
                        hostEffect: request));
                    break;
                }
                if (step.kind == "wait")
                {
                    effects.Add(new ScenarioEffect("wait_for_condition", state.runId, step.id, "", step));
                    break;
                }
                if (step.kind == "branch")
                {
                    state.currentStepId = SelectBranch(step, branchValue);
                    branchValue = "";
                    transitions++;
                    continue;
                }
                if (step.kind == "end")
                {
                    if (step.closeExchange) effects.Add(new ScenarioEffect("close_exchange", state.runId, step.id));
                    EndRunning(step.outcome, effects);
                    break;
                }
                EndRunning("failed", effects);
                break;
            }
            if (state == running && transitions > MaximumAutomaticTransitions) EndRunning("failed", effects);
            return effects.AsReadOnly();
        }

        private IReadOnlyList<ScenarioEffect> EnterContentFlow(ScenarioInstanceState state,
            ScenarioRuntimeSnapshot context, string branchValue)
        {
            var effects = new List<ScenarioEffect>();
            ContentFlowEnterResult entry = state.contentFlow.Enter(context, branchValue);
            if (!string.IsNullOrEmpty(entry.error))
            {
                effects.Add(new ScenarioEffect("scenario_flow_failed", state.runId, state.currentStepId, entry.error));
                EndRunning("failed", effects);
                return effects.AsReadOnly();
            }
            if (entry.ended)
            {
                EndRunning(entry.outcome, effects);
                return effects.AsReadOnly();
            }
            ScenarioStep step = entry.step;
            if (step == null) { EndRunning("failed", effects); return effects.AsReadOnly(); }
            state.BeginStepExecution(step);
            state.currentStepId = step.id;
            if (step.kind == "dialogue")
                effects.Add(new ScenarioEffect("show_dialogue", state.runId, step.id, "", step));
            else if (step.kind == "ask")
            {
                if (!question.Begin(step, state.runId, context.contextRevision)) { EndRunning("failed", effects); return effects.AsReadOnly(); }
                effects.Add(new ScenarioEffect("ask_player", state.runId, step.id, "", step));
            }
            else if (step.kind == "action")
            {
                if (step.activitySequence != null)
                {
                    var sequenceRequest = new ActivitySequenceRequest(step.activitySequence, step.actionRef,
                        state.runId, state.CurrentInvocationId, state.currentStepExecutionId,
                        Guid.NewGuid().ToString("N"), context, step.activitySequenceArguments, step.id);
                    state.activeActivityId = sequenceRequest.actionInvocationId;
                    effects.Add(new ScenarioEffect("request_activity", state.runId, step.id, "", step,
                        sequenceRequest: sequenceRequest));
                    return effects.AsReadOnly();
                }
                var request = new ActivityRequest(state.runId, step.id, step.capabilityId, step.arguments);
                effects.Add(new ScenarioEffect("request_activity", state.runId, step.id, "", step, request));
            }
            else if (step.kind == "host_effect")
            {
                ScenarioHostEffectRequest request;
                if (TryCreateHostEffectRequest(state, step, out request))
                    effects.Add(new ScenarioEffect("request_host_effect", state.runId, step.id, "", step, hostEffect: request));
                else
                {
                    if (state.contentFlow.MoveToTransition("failure"))
                        effects.AddRange(EnterCurrent(state, context, ""));
                    else
                    {
                        effects.Add(new ScenarioEffect("scenario_flow_failed", state.runId, step.id, "host_effect_prerequisite_missing"));
                        EndRunning("failed", effects);
                    }
                }
            }
            else if (step.kind == "wait")
                effects.Add(new ScenarioEffect("wait_for_condition", state.runId, step.id, "", step));
            else EndRunning("failed", effects);
            return effects.AsReadOnly();
        }

        private IReadOnlyList<ScenarioEffect> AdvanceContentFlow(ScenarioEvent input,
            ScenarioRuntimeSnapshot context, ScenarioStep current)
        {
            var effects = new List<ScenarioEffect>();
            bool advanced = false;
            if (current.kind == "dialogue" && input.kind == "dialogue_complete")
            {
                RecordCompletedDialogue(running, current);
                if (current.closeExchange) effects.Add(new ScenarioEffect("close_exchange", running.runId, current.id));
                advanced = running.contentFlow.MoveToTransition("next");
            }
            else if (current.kind == "ask" && (input.kind == "answer" || input.kind == "skip" || input.kind == "cancel"))
            {
                QuestionAnswerResult answer = input.kind == "answer"
                    ? question.Answer(input.questionId, input.answerId, input.freeText, input.contextRevision)
                    : input.kind == "skip" ? question.Skip(input.questionId, input.contextRevision) : question.Cancel(input.questionId, input.contextRevision);
                if (!answer.accepted) return effects.AsReadOnly();
                if (answer.kind == "option") advanced = running.contentFlow.MoveToChoice(answer.answerId);
                else advanced = running.contentFlow.MoveToTransition(answer.kind == "free_text" ? "free_text" : answer.kind);
            }
            else if (current.kind == "action" && current.activitySequence != null && input.kind == "activity_accepted")
            {
                if (string.IsNullOrWhiteSpace(input.activityId) ||
                    !string.IsNullOrEmpty(running.activeActivityId) && running.activeActivityId != input.activityId)
                    return effects.AsReadOnly();
                running.activeActivityId = input.activityId;
                return effects.AsReadOnly();
            }
            else if (current.kind == "action" && input.kind == "activity_result")
            {
                if (current.activitySequence != null && input.activityId != running.activeActivityId)
                    return effects.AsReadOnly();
                IReadOnlyList<string> actionErrors = ScenarioDefinitionValidator.ValidateActionStep(current, context);
                bool accepted = input.success && !string.IsNullOrWhiteSpace(input.activityId) && actionErrors.Count == 0;
                if (accepted)
                {
                    string error;
                    if (!running.contentFlow.StoreActionResults(input.actionResults, context, out error))
                        accepted = false;
                }
                if (accepted)
                {
                    running.activeActivityId = input.activityId;
                    running.activityParticipant = NormalizeParticipant(input.participant);
                    advanced = running.contentFlow.MoveToTransition("success");
                }
                else advanced = running.contentFlow.MoveToTransition("failure");
            }
            else if (current.kind == "host_effect" && input.kind == "host_effect_result")
            {
                ScenarioHostEffectResult result = input.hostEffectResult;
                if (!IsCurrentHostEffectResult(running, current, result)) return effects.AsReadOnly();
                advanced = running.contentFlow.MoveToTransition(
                    result.status == ScenarioHostEffectStatuses.Applied || result.status == ScenarioHostEffectStatuses.Replayed
                        ? "success" : "failure");
            }
            else if (current.kind == "wait" && (input.kind == "condition_met" || input.kind == "condition_failed"))
            {
                string expectedActivity = string.IsNullOrEmpty(running.activeActivityId) ? current.activityId : running.activeActivityId;
                if (input.activityId != expectedActivity || input.conditionId != current.conditionId) return effects.AsReadOnly();
                advanced = running.contentFlow.MoveToTransition(input.kind == "condition_met" ? "success" : "failure");
            }
            else if (current.kind == "branch" && input.kind == "branch_value")
                advanced = running.contentFlow.MoveToBranch(input.value);
            else return effects.AsReadOnly();

            if (!advanced)
            {
                EndRunning("failed", effects);
                return effects.AsReadOnly();
            }
            effects.AddRange(EnterCurrent(running, context, ""));
            return effects.AsReadOnly();
        }

        private static string SelectBranch(ScenarioStep step, string value)
        {
            ScenarioBranchCase match = step.cases == null ? null : step.cases.Find(item => item != null && item.value == value);
            return match == null ? step.defaultNextStepId : match.nextStepId;
        }

        private void SuspendRunning(List<ScenarioEffect> effects)
        {
            if (running == null) return;
            running.status = "suspended";
            if (!string.IsNullOrEmpty(running.activeActivityId))
                effects.Add(new ScenarioEffect("release_activity", running.runId, running.currentStepId, running.activeActivityId));
            suspended = running; running = null; question.Clear();
        }

        private void EndRunning(string outcome, List<ScenarioEffect> effects)
        {
            if (running == null) return;
            EndInstance(running, outcome, effects); running = null; question.Clear();
        }

        private static void EndInstance(ScenarioInstanceState instance, string outcome, List<ScenarioEffect> effects)
        {
            if (instance == null || instance.status == "ended") return;
            if (!string.IsNullOrEmpty(instance.activeActivityId))
                effects.Add(new ScenarioEffect("release_activity", instance.runId, instance.currentStepId, instance.activeActivityId));
            instance.status = "ended"; instance.outcome = outcome;
            effects.Add(new ScenarioEffect("end_scenario", instance.runId, instance.currentStepId, outcome));
        }

        private static string NormalizeParticipant(string value)
        {
            return value == "alterra" || value == "player" || value == "together" || value == "self_care" ? value : "together";
        }

        private static void RecordCompletedDialogue(ScenarioInstanceState state, ScenarioStep step)
        {
            if (state == null || step == null || step.kind != "dialogue") return;
            string sourceStepId = string.IsNullOrWhiteSpace(step.sourceStepId) ? step.id : step.sourceStepId;
            state.completedDialogueExecutions[CompletionKey(state.CurrentInvocationId, sourceStepId)] = state.currentStepExecutionId;
        }

        private static bool TryCreateHostEffectRequest(ScenarioInstanceState state, ScenarioStep step,
            out ScenarioHostEffectRequest request)
        {
            request = null;
            if (state == null || step == null || step.kind != "host_effect" || step.actionRef == null ||
                string.IsNullOrWhiteSpace(step.requiresCompletedStepId)) return false;
            string completedExecutionId;
            if (!state.completedDialogueExecutions.TryGetValue(
                CompletionKey(state.CurrentInvocationId, step.requiresCompletedStepId), out completedExecutionId)) return false;
            try
            {
                request = new ScenarioHostEffectRequest(step.actionRef, state.runId, state.CurrentInvocationId,
                    state.currentStepExecutionId, step.requiresCompletedStepId, completedExecutionId);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static bool IsCurrentHostEffectResult(ScenarioInstanceState state, ScenarioStep step,
            ScenarioHostEffectResult result)
        {
            return state != null && step != null && result != null && step.kind == "host_effect" &&
                result.scenarioRunId == state.runId && result.invocationId == state.CurrentInvocationId &&
                result.stepExecutionId == state.currentStepExecutionId;
        }

        private static string CompletionKey(string invocationId, string sourceStepId)
        { return (invocationId ?? string.Empty) + "\0" + (sourceStepId ?? string.Empty); }

        private static IReadOnlyList<ScenarioEffect> One(ScenarioEffect effect)
        { return new[] { effect }; }
    }
}
