using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using AlterOS.Activities;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Linq;
using AlterOS.Content;
using AlterOS.Topics;

namespace AlterOS.Scenarios
{
    internal sealed class ContentFlowEnterResult
    {
        internal readonly ScenarioStep step;
        internal readonly bool ended;
        internal readonly string outcome;
        internal readonly string error;
        internal ContentFlowEnterResult(ScenarioStep projectedStep, bool isEnded, string result,
            string errorMessage)
        { step = projectedStep; ended = isEnded; outcome = result ?? string.Empty; error = errorMessage ?? string.Empty; }
    }

    /// <summary>Per-run Flow invocation frames. Definitions remain immutable and each call owns separate values.</summary>
    internal sealed class ContentFlowExecutionState
    {
        private const int MaximumAutomaticTransitions = 64;
        private readonly ContentExecutionPlan plan;
        private readonly List<FlowInvocationFrame> frames = new List<FlowInvocationFrame>();
        private readonly ScenarioDefinition rootProjection;
        private int invocationSequence;
        private int executionSequence;
        private string initializationError = string.Empty;

        internal ScenarioStep CurrentProjectedStep { get; private set; }
        internal FlowRecord CurrentFlow { get { return frames.Count == 0 ? null : frames[frames.Count - 1].flow; } }
        internal StepRecord CurrentRecord { get { return frames.Count == 0 ? null : FindStep(frames[frames.Count - 1].flow, frames[frames.Count - 1].currentStepId); } }
        internal string CurrentExecutionId { get { return CurrentProjectedStep == null ? string.Empty : CurrentProjectedStep.id; } }
        internal string CurrentFlowId { get { return CurrentFlow == null ? string.Empty : CurrentFlow.id; } }
        internal ContentRef CurrentFlowReference
        {
            get
            {
                FlowRecord flow = CurrentFlow;
                if (flow == null || plan == null || plan.release == null) return null;
                ContentReleaseDependency[] matches = (plan.release.dependencies ?? new List<ContentReleaseDependency>())
                    .Where(item => item != null && item.kind == "flow" && item.store == plan.package.storeId &&
                        item.id == flow.id && item.revision == flow.revision).ToArray();
                return matches.Length == 1 ? new ContentRef(matches[0].store, matches[0].id,
                    matches[0].revision, matches[0].expectedHash) : null;
            }
        }
        internal string CurrentParentInvocationId
        {
            get
            {
                FlowInvocationFrame frame = CurrentFrame;
                return frame == null || frame.parentInvocationId == 0 ? string.Empty :
                    "flow_inv_" + frame.parentInvocationId.ToString("D8", CultureInfo.InvariantCulture);
            }
        }
        internal string CurrentCallExecutionId { get { return CurrentFrame == null ? string.Empty : CurrentFrame.callExecutionId; } }
        internal int CurrentVisitOrdinal { get { return CurrentFrame == null ? 0 : CurrentFrame.visitOrdinal; } }
        internal string CurrentInvocationId
        {
            get
            {
                FlowInvocationFrame frame = CurrentFrame;
                return frame == null ? string.Empty : "flow_inv_" + frame.invocationId.ToString("D8", CultureInfo.InvariantCulture);
            }
        }
        internal ScenarioDefinition RootProjection { get { return rootProjection; } }

        internal ContentFlowExecutionState(ContentExecutionPlan executionPlan, string runId)
        {
            plan = executionPlan;
            rootProjection = new ScenarioDefinition
            { scenarioId = plan == null || plan.rootFlow == null ? string.Empty : plan.rootFlow.id, topicId = string.Empty };
            if (plan == null || plan.rootFlow == null || plan.context == null || plan.context.runId != runId)
            { initializationError = "The prepared Flow plan does not match the running Scenario identity."; return; }
            FlowInvocationFrame root = CreateFrame(plan.rootFlow, plan.initialInputs, 0, string.Empty, string.Empty, plan.context, out initializationError);
            if (root != null) frames.Add(root);
        }

        internal ContentFlowEnterResult Enter(ScenarioRuntimeSnapshot context, string branchValue)
        {
            if (!string.IsNullOrEmpty(initializationError)) return new ContentFlowEnterResult(null, false, "", initializationError);
            if (context == null || context.runId != plan.context.runId)
                return new ContentFlowEnterResult(null, false, "", "The current host snapshot belongs to a different Scenario run.");
            CurrentProjectedStep = null;
            int automaticTransitions = 0;
            while (frames.Count > 0 && automaticTransitions <= MaximumAutomaticTransitions)
            {
                FlowInvocationFrame frame = frames[frames.Count - 1];
                StepRecord step = FindStep(frame.flow, frame.currentStepId);
                if (step == null) return new ContentFlowEnterResult(null, false, "", "The exact current Flow Step is missing.");
                if (step.kind == "subflow")
                {
                    string error;
                    if (!PushSubflow(frame, step, context, out error)) return new ContentFlowEnterResult(null, false, "", error);
                    automaticTransitions++;
                    continue;
                }
                if (step.kind == "branch")
                {
                    string target = SelectBranch(step, branchValue);
                    if (string.IsNullOrEmpty(target)) return new ContentFlowEnterResult(null, false, "", "The Branch has no validated target.");
                    frame.currentStepId = target;
                    branchValue = string.Empty;
                    automaticTransitions++;
                    continue;
                }
                if (step.kind == "end")
                {
                    if (frames.Count == 1)
                        return new ContentFlowEnterResult(null, true, ReadStepOutcome(step), string.Empty);
                    string error;
                    if (!ReturnFromSubflow(frame, step, context, out error)) return new ContentFlowEnterResult(null, false, "", error);
                    automaticTransitions++;
                    continue;
                }

                string executionId = NextExecutionId();
                frame.visitOrdinal++;
                ScenarioStep projected = ProjectStep(frame, step, executionId, context, out string errorMessage);
                if (projected == null) return new ContentFlowEnterResult(null, false, "", errorMessage);
                CurrentProjectedStep = projected;
                rootProjection.steps.Clear();
                rootProjection.steps.Add(projected);
                rootProjection.entryStepId = projected.id;
                return new ContentFlowEnterResult(projected, false, string.Empty, string.Empty);
            }
            return new ContentFlowEnterResult(null, false, "", "Automatic Flow transitions exceeded the 64-Step turn bound.");
        }

        internal bool MoveToTransition(string purpose)
        {
            FlowInvocationFrame frame = CurrentFrame;
            StepRecord step = CurrentRecord;
            if (frame == null || step == null) return false;
            TransitionRecord transition = frame.flow.transitions.FirstOrDefault(item => item != null &&
                item.fromStepId == step.id && item.purpose == purpose);
            return transition != null && MoveTo(transition.toStepId);
        }

        internal bool HasTransition(string purpose)
        {
            FlowRecord flow = CurrentFlow;
            StepRecord step = CurrentRecord;
            return flow != null && step != null && flow.transitions.Any(item => item != null &&
                item.fromStepId == step.id && item.purpose == purpose);
        }

        internal bool MoveToChoice(string choiceId)
        {
            FlowInvocationFrame frame = CurrentFrame;
            StepRecord step = CurrentRecord;
            ChoiceRecord choice = frame == null || step == null ? null : ContentFlowAdapter.FindChoice(frame.flow, step.id, choiceId);
            TransitionRecord transition = choice == null ? null : ContentFlowAdapter.FindTransition(frame.flow, choice.transitionId);
            return transition != null && transition.fromStepId == step.id && MoveTo(transition.toStepId);
        }

        internal bool MoveToBranch(string value)
        {
            FlowInvocationFrame frame = CurrentFrame;
            StepRecord step = CurrentRecord;
            string target = step == null ? string.Empty : SelectBranch(step, value);
            return frame != null && !string.IsNullOrEmpty(target) && MoveTo(target);
        }

        internal bool StoreActionResults(IDictionary<string, FlowValue> supplied, ScenarioRuntimeSnapshot context,
            out string error)
        {
            error = string.Empty;
            FlowInvocationFrame frame = CurrentFrame;
            StepRecord step = CurrentRecord;
            if (frame == null || step == null || step.kind != "action") { error = "Action results arrived outside an Action Step."; return false; }
            ActionRecord action = ContentFlowAdapter.FindAction(plan.package, step.actionRef);
            Dictionary<string, string> resultTypes = ReadActionContractTypes(action, "results");
            if (action == null || resultTypes == null) { error = "The exact Action result contract is unavailable."; return false; }
            var pending = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            IDictionary<string, FlowValue> resultValues = supplied ?? new Dictionary<string, FlowValue>();
            foreach (KeyValuePair<string, FlowValue> pair in resultValues)
            {
                string type;
                if (!resultTypes.TryGetValue(pair.Key, out type) || pair.Value == null || pair.Value.type != type || !pair.Value.IsAvailableIn(context))
                { error = "The host supplied an undeclared, wrong-typed, or unavailable Action result."; return false; }
                pending[FlowBindingEvaluator.ResultKey(step.id, pair.Key)] = pair.Value;
            }
            JsonArray declared = ReadArray(ParseObject(action.contractJson), "results");
            foreach (JsonObject result in (declared ?? new JsonArray()).OfType<JsonObject>())
                if (ContentFlowAdapter.ReadBoolean(result, "required", true) &&
                    !resultValues.ContainsKey(ContentFlowAdapter.ReadString(result, "name")))
                { error = "The host omitted a required Action result."; return false; }
            foreach (KeyValuePair<string, FlowValue> pair in pending) frame.actionResults[pair.Key] = pair.Value;
            return true;
        }

        internal void RefreshForResume()
        { CurrentProjectedStep = null; }

        private FlowInvocationFrame CurrentFrame { get { return frames.Count == 0 ? null : frames[frames.Count - 1]; } }

        private bool MoveTo(string target)
        {
            FlowInvocationFrame frame = CurrentFrame;
            if (frame == null || FindStep(frame.flow, target) == null) return false;
            frame.currentStepId = target;
            CurrentProjectedStep = null;
            return true;
        }

        private bool PushSubflow(FlowInvocationFrame caller, StepRecord call, ScenarioRuntimeSnapshot context, out string error)
        {
            error = string.Empty;
            if (frames.Count >= ContentPackageValidator.MaximumSubflowDepth)
            { error = "Runtime Subflow depth exceeded the validated maximum."; return false; }
            FlowRecord callee = ContentFlowAdapter.FindFlow(plan.package, call.calledFlowRef);
            if (callee == null || callee.kind != "subflow" || !ReleaseContains("flow", callee.id, callee.revision, callee.semanticHash))
            { error = "The exact called Subflow is missing or outside the pinned release."; return false; }

            var inputs = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            foreach (FlowParameterRecord parameter in callee.parameters.Where(item => item != null && item.direction == "input"))
            {
                if (!string.IsNullOrEmpty(parameter.defaultValueJson))
                {
                    ContentResult<FlowValue> defaultValue = FlowValue.ParseJson(parameter.defaultValueJson, parameter.valueType, plan.vocabulary, context);
                    if (!defaultValue.IsReady) { error = "Subflow input default is invalid: " + parameter.name; return false; }
                    inputs[parameter.name] = defaultValue.value;
                }
            }
            foreach (StepBindingRecord binding in caller.flow.bindings.Where(item => item != null && item.stepId == call.id))
            {
                ContentResult<FlowValue> value = FlowBindingEvaluator.Evaluate(plan.package, caller.flow, call, binding,
                    caller.locals, caller.inputs, caller.actionResults, plan.vocabulary, context);
                if (!value.IsReady) { error = "Subflow input binding failed: " + FirstMessage(value.diagnostics); return false; }
                inputs[binding.destinationName] = value.value;
            }
            foreach (FlowParameterRecord parameter in callee.parameters.Where(item => item != null && item.direction == "input"))
                if (parameter.required && !inputs.ContainsKey(parameter.name))
                { error = "Required Subflow input is missing: " + parameter.name; return false; }
            if (inputs.Keys.Any(name => !callee.parameters.Any(item => item != null && item.direction == "input" && item.name == name)))
            { error = "The caller supplied an undeclared Subflow input."; return false; }
            if (inputs.Keys.Any(name => !callee.parameters.Any(item => item != null && item.direction == "input" && item.name == name)))
            { error = "The caller supplied an undeclared Subflow input."; return false; }

            FlowInvocationFrame nested = CreateFrame(callee, inputs, caller.invocationId, call.id,
                NextExecutionId(), context, out error);
            if (nested == null) return false;
            frames.Add(nested);
            CurrentProjectedStep = null;
            return true;
        }

        private FlowInvocationFrame CreateFrame(FlowRecord flow, IDictionary<string, FlowValue> inputs,
            int parentInvocationId, string callerStepId, string callExecutionId,
            ScenarioRuntimeSnapshot context, out string error)
        {
            error = string.Empty;
            var frame = new FlowInvocationFrame
            {
                flow = flow,
                currentStepId = flow.entryStepId,
                invocationId = ++invocationSequence,
                callerStepId = callerStepId ?? string.Empty,
                parentInvocationId = parentInvocationId,
                callExecutionId = callExecutionId ?? string.Empty,
                inputs = new Dictionary<string, FlowValue>(inputs ?? new Dictionary<string, FlowValue>(), StringComparer.Ordinal)
            };
            foreach (FlowVariableRecord variable in flow.variables ?? new List<FlowVariableRecord>())
            {
                if (variable == null || string.IsNullOrEmpty(variable.defaultValueJson)) continue;
                ContentResult<FlowValue> value = FlowValue.ParseJson(variable.defaultValueJson, variable.valueType, plan.vocabulary, context);
                if (!value.IsReady) { error = "Flow local default is invalid: " + variable.name; return null; }
                frame.locals[variable.name] = value.value;
            }
            return frame;
        }

        private ScenarioStep ProjectStep(FlowInvocationFrame frame, StepRecord source, string executionId,
            ScenarioRuntimeSnapshot context, out string error)
        {
            error = string.Empty;
            JsonObject data = ParseObject(source.dataJson);
            var result = new ScenarioStep { id = executionId, sourceStepId = source.id, kind = source.kind };
            if (source.kind == "dialogue" || source.kind == "ask")
            {
                DialogueRecord dialogue = ContentFlowAdapter.FindDialogue(plan.package, source.dialogueRef);
                if (dialogue == null || !ReleaseContains("dialogue", dialogue.id, dialogue.revision, dialogue.semanticHash))
                { error = "The exact pinned Dialogue is missing or outside the release."; return null; }
                var beats = new List<ScenarioBeat>();
                foreach (BeatRecord beat in dialogue.beats.OrderBy(item => item.sequenceOrder))
                {
                    ContentResult<ScenarioBeat> resolved = ContentFlowAdapter.ProjectBeat(plan, beat);
                    if (!resolved.IsReady) { error = FirstMessage(resolved.diagnostics); return null; }
                    beats.Add(resolved.value);
                }
                List<ChoiceRecord> dialogueChoices = frame.flow.choices.Where(item => item != null && item.stepId == source.id).ToList();
                if (source.kind == "dialogue" && dialogueChoices.Count > 0)
                {
                    if (beats.Count != 1) { error = "A Dialogue Step with choices must have exactly one prompt Beat."; return null; }
                    result.kind = "ask";
                    result.question = beats[0];
                    result.questionId = "content-choice-" + source.id;
                    result.options = ProjectChoices(frame.flow, source.id, context, out error);
                    if (!string.IsNullOrEmpty(error) || result.options.Count == 0)
                    { if (string.IsNullOrEmpty(error)) error = "No available Dialogue choice remains under the current host context."; return null; }
                    result.allowFreeText = false;
                    result.skipNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "skip");
                    result.cancelNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "cancel");
                }
                else if (source.kind == "dialogue")
                {
                    result.beats = beats;
                    result.nextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "next");
                    result.closeExchange = ContentFlowAdapter.ReadBoolean(data, "closeExchange");
                    if (result.beats.Count == 0 || string.IsNullOrEmpty(result.nextStepId))
                    { error = "The Dialogue Step has no resolved Beats or next Transition."; return null; }
                }
                else
                {
                    result.kind = "ask";
                    result.question = beats.FirstOrDefault();
                    result.questionId = ContentFlowAdapter.ReadString(data, "questionId", "content-question-" + source.id);
                    if (result.question == null) { error = "The Ask Step has no question Dialogue."; return null; }
                    result.options = ProjectChoices(frame.flow, source.id, context, out error);
                    if (!string.IsNullOrEmpty(error)) return null;
                    result.allowFreeText = ContentFlowAdapter.ReadBoolean(data, "allowFreeText");
                    result.freeTextNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "free_text");
                    result.skipNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "skip");
                    result.cancelNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "cancel");
                    if (source.kind == "ask" && (string.IsNullOrEmpty(result.skipNextStepId) || string.IsNullOrEmpty(result.cancelNextStepId)) ||
                        result.allowFreeText && string.IsNullOrEmpty(result.freeTextNextStepId))
                    { error = "The Ask Step is missing a required named transition."; return null; }
                }
            }
            else if (source.kind == "host_effect")
            {
                ActionRecord action = ContentFlowAdapter.FindAction(plan.package, source.actionRef);
                if (action == null || action.implementationKind != "host_effect" ||
                    !ReleaseContains("action", action.id, action.revision, action.semanticHash))
                { error = "The exact pinned host-effect Action is missing or outside the release."; return null; }
                result.actionRef = new ContentRef(source.actionRef.store, source.actionRef.id,
                    source.actionRef.revision, action.semanticHash);
                result.requiresCompletedStepId = ContentFlowAdapter.ReadString(data, "requiresCompletedStepId");
                result.successNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "success");
                result.failureNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "failure");
                if (string.IsNullOrWhiteSpace(result.requiresCompletedStepId) ||
                    string.IsNullOrEmpty(result.successNextStepId) || string.IsNullOrEmpty(result.failureNextStepId))
                { error = "The host-effect Step is missing its completed prerequisite or result Transition."; return null; }
            }
            else if (source.kind == "action")
            {
                ActionRecord action = ContentFlowAdapter.FindAction(plan.package, source.actionRef);
                if (action == null || !ReleaseContains("action", action.id, action.revision, action.semanticHash))
                { error = "The exact pinned Action is missing or outside the release."; return null; }
                result.capabilityId = action.capabilityId;
                result.successNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "success");
                result.failureNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "failure");
                result.arguments = ProjectActionArguments(frame, source, action, context, out error,
                    out List<ActivitySequenceArgument> sequenceArguments);
                if (!string.IsNullOrEmpty(error)) return null;
                if (action.implementationKind == "activity_sequence")
                {
                    ActivitySequenceBuildResult sequence = ActivitySequenceReleaseAdapter.Build(action,
                        plan.vocabulary.SequenceBuilder);
                    if (!sequence.isValid)
                    {
                        ContentDiagnostic diagnostic = sequence.diagnostics.FirstOrDefault();
                        error = diagnostic == null ? "The released Action sequence is invalid." : diagnostic.message;
                        return null;
                    }
                    result.actionRef = new ContentRef(source.actionRef.store, source.actionRef.id,
                        source.actionRef.revision, action.semanticHash);
                    result.activitySequence = sequence.definition;
                    result.activitySequenceArguments = sequenceArguments;
                }
                IReadOnlyList<string> actionErrors = ScenarioDefinitionValidator.ValidateActionStep(result, context);
                if (actionErrors.Count != 0) { error = string.Join(" ", actionErrors); return null; }
            }
            else if (source.kind == "wait")
            {
                result.activityId = ContentFlowAdapter.ReadString(data, "activityId");
                result.conditionId = ContentFlowAdapter.ReadString(data, "conditionId");
                result.objective = ContentFlowAdapter.ReadString(data, "objective");
                result.successNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "success");
                result.failureNextStepId = ContentFlowAdapter.ReadTransitionTarget(frame.flow, source.id, "failure");
                if (string.IsNullOrEmpty(result.conditionId) || !context.waitConditions.Contains(result.conditionId) ||
                    string.IsNullOrEmpty(result.successNextStepId) || string.IsNullOrEmpty(result.failureNextStepId))
                { error = "The Wait Step references a missing or unregistered condition/transition."; return null; }
            }
            else
            { error = "The Flow Step kind cannot be projected to the existing ScenarioSession."; return null; }
            return result;
        }

        private List<ScenarioQuestionOption> ProjectChoices(FlowRecord flow, string stepId,
            ScenarioRuntimeSnapshot context, out string error)
        {
            error = string.Empty;
            var result = new List<ScenarioQuestionOption>();
            foreach (ChoiceRecord choice in flow.choices.Where(item => item != null && item.stepId == stepId))
            {
                if (choice.availabilityRuleRef != null)
                {
                    ContentResult<ContentRuleEvaluation> availability = ContentRuleEvaluator.Evaluate(plan.package,
                        choice.availabilityRuleRef, plan.vocabulary, ToTopicContext(context));
                    if (!availability.IsReady || availability.value.truth != "True") continue;
                }
                TextRecord labelText = plan.package.texts.FirstOrDefault(item => item != null && choice.labelTextRef != null &&
                    item.id == choice.labelTextRef.id && item.revision == choice.labelTextRef.revision);
                ContentResult<ContentTextResolution> label = ContentLanguageResolver.Resolve(labelText,
                    plan.displayLanguage, plan.speechLanguage);
                if (!label.IsReady) { error = "An Ask option label has no display translation."; return result; }
                JsonObject valueObject = ParseObject(choice.valueJson);
                result.Add(new ScenarioQuestionOption
                {
                    optionId = choice.id,
                    label = label.value.displayText,
                    value = ContentFlowAdapter.ReadString(valueObject, "value", choice.id),
                    nextStepId = ContentFlowAdapter.FindTransition(flow, choice.transitionId)?.toStepId ?? string.Empty
                });
            }
            return result;
        }

        private List<ScenarioActionArgument> ProjectActionArguments(FlowInvocationFrame frame, StepRecord step,
            ActionRecord action, ScenarioRuntimeSnapshot context, out string error,
            out List<ActivitySequenceArgument> sequenceArguments)
        {
            error = string.Empty;
            sequenceArguments = new List<ActivitySequenceArgument>();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            JsonObject contract = ParseObject(action.contractJson);
            JsonArray legacyArguments = ReadArray(contract, "arguments");
            if (legacyArguments != null)
                foreach (JsonObject item in legacyArguments.OfType<JsonObject>())
                    values[ContentFlowAdapter.ReadString(item, "name")] = ReadJsonScalar(item, "value");

            JsonArray inputs = ReadArray(contract, "inputs");
            if (inputs != null)
            {
                foreach (JsonObject parameter in inputs.OfType<JsonObject>())
                {
                    string name = ContentFlowAdapter.ReadString(parameter, "name");
                    string type = ContentFlowAdapter.ReadString(parameter, "type");
                    StepBindingRecord binding = frame.flow.bindings.FirstOrDefault(item => item != null &&
                        item.stepId == step.id && item.destinationName == name);
                    FlowValue value = null;
                    if (binding != null)
                    {
                        ContentResult<FlowValue> resolved = FlowBindingEvaluator.Evaluate(plan.package, frame.flow, step,
                            binding, frame.locals, frame.inputs, frame.actionResults, plan.vocabulary, context);
                        if (!resolved.IsReady) { error = FirstMessage(resolved.diagnostics); return new List<ScenarioActionArgument>(); }
                        value = resolved.value;
                    }
                    else
                    {
                        JsonValue defaultJson;
                        if (parameter.TryGetValue("defaultValue", out defaultJson) && defaultJson != null && defaultJson.ToString() != "null")
                        {
                            ContentResult<FlowValue> parsed = FlowValue.ParseJson(defaultJson.ToString(), type, plan.vocabulary, context);
                            if (!parsed.IsReady) { error = FirstMessage(parsed.diagnostics); return new List<ScenarioActionArgument>(); }
                            value = parsed.value;
                        }
                    }
                    if (value == null)
                    {
                        if (ContentFlowAdapter.ReadBoolean(parameter, "required", true))
                        { error = "A required Action input is unavailable: " + name; return new List<ScenarioActionArgument>(); }
                        continue;
                    }
                    if (value.type != type || !value.IsAvailableIn(context))
                    { error = "An Action input value is incompatible or no longer host-authorized: " + name; return new List<ScenarioActionArgument>(); }
                    values[name] = value.ToHostArgument();
                    sequenceArguments.Add(ActivitySequenceArgument.FromLiteral(name, value));
                }
            }
            return values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ScenarioActionArgument { name = pair.Key, value = pair.Value }).ToList();
        }

        private bool ReturnFromSubflow(FlowInvocationFrame calleeFrame, StepRecord endStep,
            ScenarioRuntimeSnapshot context, out string error)
        {
            error = string.Empty;
            string exitName = ReadExitName(endStep);
            FlowExitRecord exit = calleeFrame.flow.exits.FirstOrDefault(item => item != null && item.name == exitName);
            if (exit == null) { error = "The Subflow end Step does not name one of its declared exits."; return false; }
            JsonArray outputs = ReadArray(ParseObject(exit.contractJson), "outputs");
            if (outputs == null) { error = "The Subflow exit contract is invalid."; return false; }
            FlowInvocationFrame caller = frames[frames.Count - 2];
            ExitMappingRecord mapping = caller.flow.exitMappings.FirstOrDefault(item => item != null && item.callStepId == calleeFrame.callerStepId &&
                item.exitName == exitName && item.calleeFlowRef != null && item.calleeFlowRef.id == calleeFrame.flow.id &&
                item.calleeFlowRef.revision == calleeFrame.flow.revision);
            if (mapping == null) { error = "The exact named Subflow exit mapping is missing."; return false; }
            TransitionRecord transition = ContentFlowAdapter.FindTransition(caller.flow, mapping.transitionId);
            if (transition == null || transition.fromStepId != calleeFrame.callerStepId)
            { error = "The caller return transition does not originate at the Subflow Step."; return false; }

            var produced = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            foreach (JsonObject output in outputs.OfType<JsonObject>())
            {
                string name = ContentFlowAdapter.ReadString(output, "name");
                string type = ContentFlowAdapter.ReadString(output, "type");
                string sourceName = ContentFlowAdapter.ReadString(output, "sourceName");
                bool required = ContentFlowAdapter.ReadBoolean(output, "required", true);
                FlowValue value;
                if (!calleeFrame.locals.TryGetValue(sourceName, out value) && !calleeFrame.inputs.TryGetValue(sourceName, out value))
                {
                    if (required) { error = "A required named Subflow output is not produced: " + name; return false; }
                    continue;
                }
                if (value.type != type || !value.IsAvailableIn(context))
                { error = "A Subflow output is wrong-typed or unavailable: " + name; return false; }
                produced.Add(name, value);
            }

            JsonArray outputMappings = ReadArray(mapping.outputMappingsJson);
            if (outputMappings == null) { error = "The caller Subflow output mapping is invalid."; return false; }
            var writes = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            foreach (JsonObject item in outputMappings.OfType<JsonObject>())
            {
                string source = ContentFlowAdapter.ReadString(item, "sourceName");
                string destination = ContentFlowAdapter.ReadString(item, "destinationName");
                FlowValue value;
                FlowVariableRecord local = caller.flow.variables.FirstOrDefault(variable => variable != null && variable.name == destination);
                if (!produced.TryGetValue(source, out value) || local == null || !local.mutable || local.valueType != value.type)
                { error = "The caller output target is undeclared or has the wrong type."; return false; }
                writes.Add(destination, value);
            }
            foreach (JsonObject output in outputs.OfType<JsonObject>())
            {
                string name = ContentFlowAdapter.ReadString(output, "name");
                if (ContentFlowAdapter.ReadBoolean(output, "required", true) && produced.ContainsKey(name) &&
                    !outputMappings.OfType<JsonObject>().Any(item => ContentFlowAdapter.ReadString(item, "sourceName") == name))
                { error = "A required Subflow output has not been mapped to a caller local."; return false; }
            }
            foreach (KeyValuePair<string, FlowValue> write in writes) caller.locals[write.Key] = write.Value;
            frames.RemoveAt(frames.Count - 1);
            caller.currentStepId = transition.toStepId;
            return true;
        }

        private bool ReleaseContains(string kind, string id, string revision, string hash)
        { return plan.release.dependencies.Any(item => item != null && item.kind == kind && item.id == id &&
            item.revision == revision && item.store == plan.package.storeId && item.expectedHash == hash); }

        private string SelectBranch(StepRecord step, string branchValue)
        {
            JsonObject data = ParseObject(step.dataJson);
            JsonArray cases = ReadArray(data, "cases");
            if (cases != null)
                foreach (JsonObject item in cases.OfType<JsonObject>())
                    if (ContentFlowAdapter.ReadString(item, "value") == (branchValue ?? string.Empty))
                        return ContentFlowAdapter.ReadString(item, "nextStepId");
            return ContentFlowAdapter.ReadString(data, "defaultNextStepId", ContentFlowAdapter.ReadTransitionTarget(CurrentFlow, step.id, "branch_default"));
        }

        private string ReadExitName(StepRecord step)
        {
            JsonObject data = ParseObject(step.dataJson);
            string name = ContentFlowAdapter.ReadString(data, "exitName");
            if (!string.IsNullOrEmpty(name)) return name;
            string outcome = ReadStepOutcome(step);
            if (CurrentFlow.exits.Any(item => item != null && item.name == outcome)) return outcome;
            return CurrentFlow.exits.Count == 1 ? CurrentFlow.exits[0].name : string.Empty;
        }

        private static string ReadStepOutcome(StepRecord step)
        { return ContentFlowAdapter.ReadString(ParseObject(step.dataJson), "outcome", "completed"); }

        private static JsonObject ParseObject(string json)
        { try { return ContentJson.Parse(json ?? "{}") as JsonObject; } catch { return null; } }

        private static JsonArray ReadArray(JsonObject value, string field)
        {
            try
            {
                JsonValue parsed = field == null ? ContentJson.Parse(value == null ? "[]" : value.ToString()) :
                    value != null && value.ContainsKey(field) ? value[field] : null;
                return parsed as JsonArray;
            }
            catch { return null; }
        }

        private static JsonArray ReadArray(string json)
        {
            try { return ContentJson.Parse(json ?? "[]") as JsonArray; }
            catch { return null; }
        }

        private static string ReadJsonScalar(JsonObject value, string field)
        {
            JsonValue item;
            if (value == null || !value.TryGetValue(field, out item) || item == null) return string.Empty;
            if (item.Type == JTokenType.String) return (string)item;
            return item.ToString();
        }

        private static Dictionary<string, string> ReadActionContractTypes(ActionRecord action, string field)
        {
            JsonObject contract = action == null ? null : ParseObject(action.contractJson);
            JsonArray members = ReadArray(contract, field);
            if (members == null) return new Dictionary<string, string>(StringComparer.Ordinal);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonObject member in members.OfType<JsonObject>())
            {
                string name = ContentFlowAdapter.ReadString(member, "name");
                string type = ContentFlowAdapter.ReadString(member, "type");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(type)) result[name] = type;
            }
            return result;
        }

        private static string FirstMessage(IEnumerable<ContentDiagnostic> diagnostics)
        {
            ContentDiagnostic first = diagnostics == null ? null : diagnostics.FirstOrDefault();
            return first == null ? "A content dependency could not be resolved." : first.message;
        }

        private string NextExecutionId()
        { return "flow_exec_" + (++executionSequence).ToString("D12", CultureInfo.InvariantCulture); }

        private static StepRecord FindStep(FlowRecord flow, string id)
        { return flow == null || flow.steps == null ? null : flow.steps.FirstOrDefault(item => item != null && item.id == id); }

        private static TopicContext ToTopicContext(ScenarioRuntimeSnapshot context)
        {
            var facts = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (context.hostFacts != null)
                foreach (KeyValuePair<string, bool> fact in context.hostFacts) facts[fact.Key] = fact.Value;
            var topicContext = new TopicContext { facts = facts, objectsComplete = true, localHour = -1 };
            if (context.objects != null)
                foreach (ScenarioObjectSnapshot item in context.objects)
                    if (item != null) topicContext.objects.Add(new TopicObjectSnapshot { id = item.objectId, kind = item.kind });
            return topicContext;
        }

        private sealed class FlowInvocationFrame
        {
            internal FlowRecord flow;
            internal string currentStepId, callerStepId, callExecutionId;
            internal int invocationId;
            internal int parentInvocationId;
            internal int visitOrdinal;
            internal Dictionary<string, FlowValue> inputs = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            internal readonly Dictionary<string, FlowValue> locals = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            internal readonly Dictionary<string, FlowValue> actionResults = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
        }
    }
}
