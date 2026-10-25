using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    /// <summary>Runs one validated sequence invocation; ActivityHost owns its lifetime.</summary>
    public sealed class ActivitySequenceExecutor : IActivitySequenceRunner
    {
        public const int MaximumOperationsPerTick = 8;

        private sealed class SequenceBinding
        {
            public readonly FlowValue value;
            public readonly string producerPrimitiveId;
            public readonly string resultName;
            public readonly ActivityResultDescriptor resultDescriptor;

            public SequenceBinding(FlowValue boundValue, string producer, ActivityResultDescriptor result)
            {
                value = boundValue;
                producerPrimitiveId = producer;
                resultName = result == null ? string.Empty : result.name;
                resultDescriptor = result;
            }
        }

        private readonly ActivityPrimitiveRegistry primitives;
        private readonly ActivitySequenceValidator validator;
        private readonly IActivityPrimitivePort port;
        private readonly Dictionary<string, int> stepIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, SequenceBinding> bindings = new Dictionary<string, SequenceBinding>(StringComparer.Ordinal);

        private ActivitySequenceRequest acceptedRequest;
        private ActivitySequenceDefinition definition;
        private ActivitySequenceStep currentStep;
        private ActivityPrimitiveIdentity activeIdentity;
        private ActivityPrimitiveCompletion pendingCompletion;
        private ActivityPrimitiveCompletion lastCompletion;
        private ActivitySequenceView cachedView;
        private string status = ActivitySequenceStatuses.Failed;
        private string reason = "sequence_not_started";
        private int currentIndex;
        private bool started;
        private bool terminal;
        private bool primitiveOutstanding;
        private bool needsDispatch;
        private bool cleanupRequested;
        private bool hasTicked;
        private double lastTickSeconds;

        public ActivitySequenceExecutor(ActivityPrimitiveRegistry registry, IActivityPrimitivePort primitivePort)
        {
            primitives = registry ?? throw new ArgumentNullException("registry");
            validator = new ActivitySequenceValidator(registry);
            port = primitivePort ?? throw new ArgumentNullException("primitivePort");
        }

        public ActivitySequenceStartResult Start(ActivitySequenceRequest request)
        {
            if (terminal && !started) return FailedStart(request, "sequence_instance_shutdown");
            if (started)
            {
                if (SameIdentity(acceptedRequest, request))
                    return SamePayload(acceptedRequest, request)
                        ? CurrentStartResult()
                        : FailedStart(request, "sequence_identity_conflict");
                return FailedStart(request, "sequence_instance_already_started");
            }

            string invalid = ValidateRequest(request);
            if (invalid.Length > 0) return FailedStart(request, invalid);

            ActivitySequenceDefinition prepared;
            string preparationError;
            if (!PrepareDefinition(request, out prepared, out preparationError))
                return FailedStart(request, preparationError);

            ActivitySequenceValidationResult validation = validator.Validate(prepared);
            if (!validation.isValid)
                return FailedStart(request, validation.diagnostics[0].code);

            acceptedRequest = CopyRequest(request);
            definition = prepared;
            for (int index = 0; index < definition.steps.Count; index++)
                stepIndices.Add(definition.steps[index].stepId, index);
            currentIndex = 0;
            currentStep = definition.steps[0];
            started = true;
            terminal = false;
            status = ActivitySequenceStatuses.Accepted;
            reason = "";

            DispatchCurrentStep();
            return new ActivitySequenceStartResult(ActivitySequenceStatuses.Accepted,
                request.scenarioRunId, request.flowInvocationId, request.stepExecutionId,
                request.actionInvocationId, reason);
        }

        public ActivitySequenceView Tick(double nowSeconds)
        {
            if (!started) return View("sequence_not_started");
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) ||
                (hasTicked && nowSeconds < lastTickSeconds))
                return View("sequence_tick_time_invalid");

            hasTicked = true;
            lastTickSeconds = nowSeconds;
            int operations = 0;
            while (!terminal && operations < MaximumOperationsPerTick)
            {
                if (pendingCompletion != null)
                {
                    ActivityPrimitiveCompletion completion = pendingCompletion;
                    pendingCompletion = null;
                    HandleCompletion(completion);
                    operations++;
                    continue;
                }
                if (primitiveOutstanding || !needsDispatch) break;

                needsDispatch = false;
                DispatchCurrentStep();
                operations++;
                if (primitiveOutstanding && pendingCompletion == null) break;
            }
            return View();
        }

        public ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion)
        {
            if (completion == null) return View("primitive_completion_missing");
            if (terminal || !started) return DuplicateOrStale(completion);
            if (!primitiveOutstanding || !IdentityEquals(activeIdentity, completion.identity))
                return DuplicateOrStale(completion);
            if (pendingCompletion != null)
                return SameCompletion(pendingCompletion, completion)
                    ? View()
                    : View("primitive_completion_conflict");
            if (completion.status == ActivitySequenceStatuses.Accepted || completion.status == ActivitySequenceStatuses.Running)
                return View("primitive_completion_not_terminal");
            if (!IsTerminalPrimitiveStatus(completion.status))
                return View("primitive_completion_status_invalid");

            pendingCompletion = CopyCompletion(completion);
            return TickWithoutClockAdvance();
        }

        public ActivitySequenceView Cancel(ActivitySequenceCancellation cancellation)
        {
            if (cancellation == null) return View("sequence_cancellation_missing");
            if (!started || terminal) return View();
            if (!CancellationMatches(acceptedRequest, cancellation)) return View("sequence_cancellation_stale");
            if (cleanupRequested) return View();
            if (pendingCompletion != null)
            {
                ActivityPrimitiveCompletion finished = pendingCompletion;
                pendingCompletion = null;
                HandleCompletion(finished);
                if (terminal) return View();
                if (!primitiveOutstanding)
                {
                    SetTerminal(ActivitySequenceStatuses.Cancelled, cancellation.reasonCode);
                    return View();
                }
            }
            if (!primitiveOutstanding || activeIdentity == null)
            {
                SetTerminal(ActivitySequenceStatuses.Cancelled, cancellation.reasonCode);
                return View();
            }

            cleanupRequested = true;
            ActivityPrimitiveCompletion result;
            try
            {
                result = port.Cancel(new ActivityPrimitiveCancellation(activeIdentity, cancellation.reasonCode));
            }
            catch
            {
                SetTerminal(ActivitySequenceStatuses.Failed, "primitive_cancel_failed");
                return View();
            }
            if (result == null || !IdentityEquals(activeIdentity, result.identity))
            {
                SetTerminal(ActivitySequenceStatuses.Failed, "primitive_cancel_identity_mismatch");
                return View();
            }
            if (result.status == ActivitySequenceStatuses.Accepted || result.status == ActivitySequenceStatuses.Running)
                return View();
            if (!IsTerminalPrimitiveStatus(result.status))
            {
                SetTerminal(ActivitySequenceStatuses.Failed, "primitive_cancel_status_invalid");
                return View();
            }
            pendingCompletion = CopyCompletion(result);
            return TickWithoutClockAdvance();
        }

        public ActivitySequenceView Shutdown(string shutdownReason)
        {
            if (terminal) return View();
            if (pendingCompletion != null)
            {
                ActivityPrimitiveCompletion finished = pendingCompletion;
                pendingCompletion = null;
                HandleCompletion(finished);
                if (terminal) return View();
                if (!primitiveOutstanding)
                {
                    SetTerminal(ActivitySequenceStatuses.Cancelled,
                        string.IsNullOrWhiteSpace(shutdownReason) ? "shutdown" : shutdownReason);
                    return View();
                }
            }
            if (primitiveOutstanding && activeIdentity != null && !cleanupRequested)
                RequestBestEffortCleanup(activeIdentity,
                    string.IsNullOrWhiteSpace(shutdownReason) ? "shutdown" : shutdownReason);
            pendingCompletion = null;
            primitiveOutstanding = false;
            activeIdentity = null;
            SetTerminal(ActivitySequenceStatuses.Cancelled,
                string.IsNullOrWhiteSpace(shutdownReason) ? "shutdown" : shutdownReason);
            return View();
        }

        private ActivitySequenceView TickWithoutClockAdvance()
        {
            if (pendingCompletion != null)
            {
                ActivityPrimitiveCompletion completion = pendingCompletion;
                pendingCompletion = null;
                HandleCompletion(completion);
            }
            return View();
        }

        private void DispatchCurrentStep()
        {
            if (terminal || currentStep == null) return;
            reason = "";
            cleanupRequested = false;
            ActivityPrimitiveIdentity identity = CreateIdentity(currentStep);
            ActivityPrimitiveDescriptor descriptor;
            if (!primitives.TryResolve(currentStep.primitiveId, out descriptor))
            {
                QueueSyntheticFailure(identity, "primitive_unregistered");
                return;
            }
            ActivitySequenceArgument[] resolvedArguments;
            string resolutionError;
            if (!ResolveArguments(currentStep, descriptor, out resolvedArguments, out resolutionError))
            {
                QueueSyntheticFailure(identity, resolutionError);
                return;
            }
            if (!HasCapability(descriptor.requiredCapabilityId, resolvedArguments))
            {
                QueueSyntheticFailure(identity, "primitive_capability_unavailable");
                return;
            }

            var primitiveRequest = new ActivityPrimitiveRequest(identity,
                acceptedRequest.actionDefinitionRef, currentStep.primitiveId, resolvedArguments);
            ActivityPrimitiveStartResult startResult;
            try { startResult = port.Start(primitiveRequest); }
            catch
            {
                RequestBestEffortCleanup(identity, "primitive_start_failed");
                QueueSyntheticFailure(identity, "primitive_start_failed");
                return;
            }
            if (startResult == null || !IdentityEquals(identity, startResult.identity))
            {
                RequestBestEffortCleanup(identity, "primitive_start_identity_mismatch");
                QueueSyntheticFailure(identity, "primitive_start_identity_mismatch");
                return;
            }

            if (startResult.status == ActivitySequenceStatuses.Accepted ||
                startResult.status == ActivitySequenceStatuses.Running)
            {
                ActivitySequenceValidationResult partial = validator.ValidatePrimitiveOutputs(
                    currentStep.primitiveId, startResult.outputs);
                if (!partial.isValid)
                {
                    primitiveOutstanding = true;
                    activeIdentity = identity;
                    RequestBestEffortCleanup(identity, "primitive_output_invalid");
                    SetTerminal(ActivitySequenceStatuses.Failed, "primitive_output_invalid");
                    return;
                }
                activeIdentity = identity;
                primitiveOutstanding = true;
                status = ActivitySequenceStatuses.Running;
                return;
            }

            if (!IsTerminalPrimitiveStatus(startResult.status))
            {
                RequestBestEffortCleanup(identity, "primitive_start_status_invalid");
                QueueSyntheticFailure(identity, "primitive_start_status_invalid");
                return;
            }
            activeIdentity = identity;
            primitiveOutstanding = true;
            pendingCompletion = new ActivityPrimitiveCompletion(identity, startResult.status,
                startResult.outputs, startResult.reasonCode);
            status = ActivitySequenceStatuses.Running;
        }

        private void QueueSyntheticFailure(ActivityPrimitiveIdentity identity, string failureReason)
        {
            activeIdentity = identity;
            primitiveOutstanding = true;
            pendingCompletion = new ActivityPrimitiveCompletion(identity, ActivitySequenceStatuses.Failed,
                new ActivitySequenceArgument[0], failureReason);
            status = ActivitySequenceStatuses.Running;
        }

        private void RequestBestEffortCleanup(ActivityPrimitiveIdentity identity, string cleanupReason)
        {
            if (cleanupRequested) return;
            cleanupRequested = true;
            try { port.Cancel(new ActivityPrimitiveCancellation(identity, cleanupReason)); }
            catch { }
        }

        private void HandleCompletion(ActivityPrimitiveCompletion completion)
        {
            if (!primitiveOutstanding || !IdentityEquals(activeIdentity, completion.identity)) return;
            primitiveOutstanding = false;
            activeIdentity = null;
            lastCompletion = CopyCompletion(completion);

            if (completion.status == ActivitySequenceStatuses.Completed)
            {
                ActivitySequenceValidationResult outputs = validator.ValidatePrimitiveSuccessOutputs(
                    currentStep.primitiveId, completion.outputs, currentStep.resultBindings);
                if (!outputs.isValid)
                {
                    FollowFailure("primitive_output_invalid");
                    return;
                }
                ActivityPrimitiveDescriptor producerDescriptor;
                if (!primitives.TryResolve(currentStep.primitiveId, out producerDescriptor))
                {
                    FollowFailure("primitive_output_invalid");
                    return;
                }
                foreach (ActivitySequenceResultBinding binding in currentStep.resultBindings)
                {
                    ActivitySequenceArgument output = completion.outputs.First(item => item.name == binding.resultName);
                    ActivityResultDescriptor resultDescriptor = producerDescriptor.results
                        .FirstOrDefault(item => item.name == binding.resultName);
                    if (resultDescriptor == null)
                    {
                        FollowFailure("primitive_output_invalid");
                        return;
                    }
                    bindings[binding.bindingId] = new SequenceBinding(output.value.literal,
                        producerDescriptor.primitiveId, resultDescriptor);
                }
                FollowEdge(currentStep.successNextStepId, ActivitySequenceStatuses.Completed, completion.reasonCode);
                return;
            }
            if (completion.status == ActivitySequenceStatuses.Failed)
            {
                FollowFailure(completion.reasonCode);
                return;
            }
            FollowEdge(currentStep.cancelNextStepId, ActivitySequenceStatuses.Cancelled, completion.reasonCode);
        }

        private void FollowFailure(string failureReason)
        {
            FollowEdge(currentStep.failureNextStepId, ActivitySequenceStatuses.Failed,
                string.IsNullOrWhiteSpace(failureReason) ? "primitive_failed" : failureReason);
        }

        private void FollowEdge(string target, string terminalStatus, string outcomeReason)
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                currentIndex = stepIndices[target];
                currentStep = definition.steps[currentIndex];
                needsDispatch = true;
                status = ActivitySequenceStatuses.Running;
                reason = outcomeReason ?? "";
                return;
            }
            SetTerminal(terminalStatus, outcomeReason);
        }

        private bool ResolveArguments(ActivitySequenceStep step, ActivityPrimitiveDescriptor stepDescriptor,
            out ActivitySequenceArgument[] resolved, out string error)
        {
            var values = new List<ActivitySequenceArgument>(step.arguments.Count);
            foreach (ActivitySequenceArgument argument in step.arguments)
            {
                ActivityArgumentDescriptor expected = stepDescriptor.arguments.FirstOrDefault(item => item.name == argument.name);
                if (expected == null)
                {
                    resolved = new ActivitySequenceArgument[0];
                    error = "sequence_argument_unregistered";
                    return false;
                }
                ActivitySequenceValue value = argument.value;
                FlowValue literal = value == null ? null : value.literal;
                SequenceBinding binding = null;
                if (literal == null && value != null && bindings.TryGetValue(value.bindingId, out binding))
                    literal = binding.value;
                if (literal == null)
                {
                    resolved = new ActivitySequenceArgument[0];
                    error = "sequence_binding_missing";
                    return false;
                }
                if (!literal.IsAvailableIn(acceptedRequest.context) &&
                    !IsReservedTargetBinding(binding, expected, literal))
                {
                    resolved = new ActivitySequenceArgument[0];
                    error = "sequence_entity_unavailable";
                    return false;
                }
                values.Add(ActivitySequenceArgument.FromLiteral(argument.name, literal));
            }
            resolved = values.ToArray();
            error = "";
            return true;
        }

        private static bool IsReservedTargetBinding(SequenceBinding binding,
            ActivityArgumentDescriptor expected, FlowValue value)
        {
            return binding != null && expected != null && value != null && value.entityValue != null &&
                expected.requiredProducerPrimitiveId == ActivityPrimitiveIds.ReserveNearest &&
                expected.entityKind == ActivitySequenceEntityKinds.ReservedTarget &&
                binding.producerPrimitiveId == ActivityPrimitiveIds.ReserveNearest &&
                binding.resultName == "targetToken" && binding.resultDescriptor != null &&
                binding.resultDescriptor.valueType == "entity" &&
                binding.resultDescriptor.entityKind == ActivitySequenceEntityKinds.ReservedTarget &&
                value.type == "entity" && value.entityValue.kind == ActivitySequenceEntityKinds.ReservedTarget;
        }

        private bool PrepareDefinition(ActivitySequenceRequest request,
            out ActivitySequenceDefinition prepared, out string error)
        {
            var inputs = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            foreach (ActivitySequenceArgument argument in request.arguments)
            {
                if (argument == null || !ActivitySequenceIdentifiers.IsValid(argument.name) || argument.value == null ||
                    argument.value.literal == null || !string.IsNullOrEmpty(argument.value.bindingId) ||
                    inputs.ContainsKey(argument.name))
                {
                    prepared = null;
                    error = "sequence_input_invalid";
                    return false;
                }
                inputs.Add(argument.name, argument.value.literal);
                if (!argument.value.literal.IsAvailableIn(request.context))
                {
                    prepared = null;
                    error = "sequence_input_unavailable";
                    return false;
                }
            }

            var outputBindingIds = new HashSet<string>(request.definition.steps
                .Where(step => step != null).SelectMany(step => step.resultBindings)
                .Where(binding => binding != null).Select(binding => binding.bindingId), StringComparer.Ordinal);
            if (inputs.Keys.Any(outputBindingIds.Contains))
            {
                prepared = null;
                error = "sequence_input_binding_conflict";
                return false;
            }

            var usedInputs = new HashSet<string>(StringComparer.Ordinal);
            var steps = new List<ActivitySequenceStep>(request.definition.steps.Count);
            foreach (ActivitySequenceStep step in request.definition.steps)
            {
                if (step == null)
                {
                    prepared = null;
                    error = "sequence_step_invalid";
                    return false;
                }
                var args = new List<ActivitySequenceArgument>(step.arguments.Count);
                foreach (ActivitySequenceArgument argument in step.arguments)
                {
                    FlowValue input;
                    if (argument.value != null && string.IsNullOrEmpty(argument.value.literal?.type) &&
                        inputs.TryGetValue(argument.value.bindingId, out input))
                    {
                        usedInputs.Add(argument.value.bindingId);
                        args.Add(ActivitySequenceArgument.FromLiteral(argument.name, input));
                    }
                    else args.Add(argument);
                }
                steps.Add(new ActivitySequenceStep(step.stepId, step.primitiveId, args, step.resultBindings,
                    step.successNextStepId, step.failureNextStepId, step.cancelNextStepId, step.checkpointPolicy));
            }
            if (usedInputs.Count != inputs.Count)
            {
                prepared = null;
                error = "sequence_input_unused";
                return false;
            }

            prepared = new ActivitySequenceDefinition(request.definition.id, request.definition.revision, steps);
            error = "";
            return true;
        }

        private string ValidateRequest(ActivitySequenceRequest request)
        {
            if (request == null || request.definition == null) return "sequence_request_missing";
            if (!ValidId(request.scenarioRunId) || !ValidId(request.flowInvocationId) ||
                !ValidId(request.stepExecutionId) || !ValidId(request.actionInvocationId)) return "sequence_identity_invalid";
            if (request.context == null || request.context.runId != request.scenarioRunId) return "sequence_context_mismatch";
            ContentRef action = request.actionDefinitionRef;
            if (action == null || !ValidId(action.store) || !ValidId(action.id) ||
                !ValidId(action.revision) || string.IsNullOrWhiteSpace(action.expectedHash)) return "sequence_action_ref_invalid";
            return "";
        }

        private ActivityPrimitiveIdentity CreateIdentity(ActivitySequenceStep step)
        {
            return new ActivityPrimitiveIdentity(acceptedRequest.scenarioRunId,
                acceptedRequest.flowInvocationId, acceptedRequest.stepExecutionId,
                acceptedRequest.actionInvocationId, step.stepId,
                acceptedRequest.actionInvocationId + ":" + step.stepId);
        }

        private bool HasCapability(string capabilityId, IEnumerable<ActivitySequenceArgument> arguments)
        {
            if (string.IsNullOrWhiteSpace(capabilityId)) return true;
            ScenarioCapabilitySnapshot capability = null;
            int matches = 0;
            foreach (ScenarioCapabilitySnapshot candidate in acceptedRequest.context.capabilities)
            {
                if (candidate == null || candidate.capabilityId != capabilityId) continue;
                capability = candidate;
                matches++;
            }
            if (matches != 1) return false;

            ActivitySequenceArgument[] supplied = (arguments ?? new ActivitySequenceArgument[0]).ToArray();
            if (supplied.Length != capability.argumentBindings.Count) return false;
            foreach (ScenarioArgumentBinding binding in capability.argumentBindings)
            {
                if (binding == null) return false;
                ActivitySequenceArgument argument = supplied.FirstOrDefault(item => item != null && item.name == binding.name);
                if (argument == null || argument.value == null || argument.value.literal == null ||
                    !binding.values.Contains(argument.value.literal.ToHostArgument(), StringComparer.Ordinal)) return false;
            }
            return true;
        }

        private void SetTerminal(string terminalStatus, string terminalReason)
        {
            terminal = true;
            needsDispatch = false;
            primitiveOutstanding = false;
            activeIdentity = null;
            pendingCompletion = null;
            status = terminalStatus;
            reason = terminalReason ?? "";
        }

        private ActivitySequenceView DuplicateOrStale(ActivityPrimitiveCompletion completion)
        {
            if (lastCompletion != null && IdentityEquals(lastCompletion.identity, completion.identity))
                return SameCompletion(lastCompletion, completion) ? View() : View("primitive_completion_conflict");
            return View("primitive_completion_stale");
        }

        private ActivitySequenceView View(string reasonOverride = null)
        {
            string run = acceptedRequest == null ? "" : acceptedRequest.scenarioRunId;
            string flow = acceptedRequest == null ? "" : acceptedRequest.flowInvocationId;
            string stepExecution = acceptedRequest == null ? "" : acceptedRequest.stepExecutionId;
            string action = acceptedRequest == null ? "" : acceptedRequest.actionInvocationId;
            string sequenceStep = currentStep == null ? "" : currentStep.stepId;
            if (reasonOverride != null)
                return new ActivitySequenceView(status, run, flow, stepExecution, action,
                    sequenceStep, reasonOverride);
            if (cachedView == null || cachedView.status != status || cachedView.scenarioRunId != run ||
                cachedView.flowInvocationId != flow || cachedView.stepExecutionId != stepExecution ||
                cachedView.actionInvocationId != action || cachedView.sequenceStepId != sequenceStep ||
                cachedView.reasonCode != reason)
                cachedView = new ActivitySequenceView(status, run, flow, stepExecution, action,
                    sequenceStep, reason);
            return cachedView;
        }

        private ActivitySequenceStartResult CurrentStartResult()
        {
            return new ActivitySequenceStartResult(status, acceptedRequest.scenarioRunId,
                acceptedRequest.flowInvocationId, acceptedRequest.stepExecutionId,
                acceptedRequest.actionInvocationId, reason);
        }

        private static ActivitySequenceStartResult FailedStart(ActivitySequenceRequest request, string code)
        {
            return new ActivitySequenceStartResult(ActivitySequenceStatuses.Failed,
                request == null ? "" : request.scenarioRunId,
                request == null ? "" : request.flowInvocationId,
                request == null ? "" : request.stepExecutionId,
                request == null ? "" : request.actionInvocationId, code);
        }

        private static bool ValidId(string value) { return ActivitySequenceIdentifiers.IsValid(value); }

        private static bool IsTerminalPrimitiveStatus(string value)
        {
            return value == ActivitySequenceStatuses.Completed || value == ActivitySequenceStatuses.Failed ||
                value == ActivitySequenceStatuses.Cancelled;
        }

        private static bool CancellationMatches(ActivitySequenceRequest request, ActivitySequenceCancellation cancellation)
        {
            return request.scenarioRunId == cancellation.scenarioRunId &&
                request.flowInvocationId == cancellation.flowInvocationId &&
                request.stepExecutionId == cancellation.stepExecutionId &&
                request.actionInvocationId == cancellation.actionInvocationId;
        }

        private static bool SameIdentity(ActivitySequenceRequest left, ActivitySequenceRequest right)
        {
            return left != null && right != null && left.scenarioRunId == right.scenarioRunId &&
                left.flowInvocationId == right.flowInvocationId && left.stepExecutionId == right.stepExecutionId &&
                left.actionInvocationId == right.actionInvocationId;
        }

        private static bool SamePayload(ActivitySequenceRequest left, ActivitySequenceRequest right)
        {
            return SameIdentity(left, right) && SameRef(left.actionDefinitionRef, right.actionDefinitionRef) &&
                SameDefinition(left.definition, right.definition) && SameContext(left.context, right.context) &&
                SameArguments(left.arguments, right.arguments);
        }

        private static bool SameRef(ContentRef left, ContentRef right)
        {
            return left != null && right != null && left.store == right.store && left.id == right.id &&
                left.revision == right.revision && left.expectedHash == right.expectedHash;
        }

        private static bool SameDefinition(ActivitySequenceDefinition left, ActivitySequenceDefinition right)
        {
            if (left == null || right == null || left.id != right.id || left.revision != right.revision ||
                left.steps.Count != right.steps.Count) return false;
            for (int index = 0; index < left.steps.Count; index++)
            {
                ActivitySequenceStep a = left.steps[index];
                ActivitySequenceStep b = right.steps[index];
                if (a == null || b == null || a.stepId != b.stepId || a.primitiveId != b.primitiveId ||
                    a.successNextStepId != b.successNextStepId || a.failureNextStepId != b.failureNextStepId ||
                    a.cancelNextStepId != b.cancelNextStepId || a.checkpointPolicy != b.checkpointPolicy ||
                    !SameArguments(a.arguments, b.arguments) || a.resultBindings.Count != b.resultBindings.Count) return false;
                for (int binding = 0; binding < a.resultBindings.Count; binding++)
                    if (a.resultBindings[binding].bindingId != b.resultBindings[binding].bindingId ||
                        a.resultBindings[binding].resultName != b.resultBindings[binding].resultName) return false;
            }
            return true;
        }

        private static bool SameArguments(IReadOnlyList<ActivitySequenceArgument> left,
            IReadOnlyList<ActivitySequenceArgument> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                ActivitySequenceArgument a = left[index];
                ActivitySequenceArgument b = right[index];
                if (a == null || b == null || a.name != b.name || !SameValue(a.value, b.value)) return false;
            }
            return true;
        }

        private static bool SameValue(ActivitySequenceValue left, ActivitySequenceValue right)
        {
            if (left == null || right == null || left.bindingId != right.bindingId) return left == right;
            return SameFlowValue(left.literal, right.literal);
        }

        private static bool SameFlowValue(FlowValue left, FlowValue right)
        {
            if (left == null || right == null || left.type != right.type || left.boolValue != right.boolValue ||
                left.int64Value != right.int64Value || left.doubleValue != right.doubleValue || left.textValue != right.textValue)
                return left == right;
            if (left.entityValue == null || right.entityValue == null) return left.entityValue == right.entityValue;
            return left.entityValue.kind == right.entityValue.kind && left.entityValue.id == right.entityValue.id;
        }

        private static bool SameContext(ScenarioRuntimeSnapshot left, ScenarioRuntimeSnapshot right)
        {
            if (left == null || right == null || left.scenarioId != right.scenarioId || left.runId != right.runId ||
                left.turnId != right.turnId || left.contextRevision != right.contextRevision ||
                left.checkpointStepId != right.checkpointStepId ||
                !SameStrings(left.knownTopicIds, right.knownTopicIds) ||
                !SameStrings(left.knownIntentIds, right.knownIntentIds) ||
                !SameStrings(left.allowedExpressions, right.allowedExpressions) ||
                !SameStrings(left.waitConditions, right.waitConditions) ||
                left.objects.Count != right.objects.Count || left.capabilities.Count != right.capabilities.Count ||
                left.hostFacts.Count != right.hostFacts.Count) return false;
            for (int index = 0; index < left.objects.Count; index++)
                if (left.objects[index] == null || right.objects[index] == null ||
                    left.objects[index].kind != right.objects[index].kind || left.objects[index].objectId != right.objects[index].objectId) return false;
            for (int index = 0; index < left.capabilities.Count; index++)
            {
                ScenarioCapabilitySnapshot a = left.capabilities[index];
                ScenarioCapabilitySnapshot b = right.capabilities[index];
                if (a == null || b == null || a.capabilityId != b.capabilityId ||
                    a.argumentBindings.Count != b.argumentBindings.Count) return false;
                for (int binding = 0; binding < a.argumentBindings.Count; binding++)
                {
                    ScenarioArgumentBinding leftBinding = a.argumentBindings[binding];
                    ScenarioArgumentBinding rightBinding = b.argumentBindings[binding];
                    if (leftBinding == null || rightBinding == null || leftBinding.name != rightBinding.name ||
                        !SameStrings(leftBinding.values, rightBinding.values)) return false;
                }
            }
            foreach (KeyValuePair<string, bool> fact in left.hostFacts)
            {
                bool value;
                if (!right.hostFacts.TryGetValue(fact.Key, out value) || value != fact.Value) return false;
            }
            return true;
        }

        private static bool SameStrings(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            return left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal);
        }

        private static bool IdentityEquals(ActivityPrimitiveIdentity left, ActivityPrimitiveIdentity right)
        {
            return left != null && right != null && left.scenarioRunId == right.scenarioRunId &&
                left.flowInvocationId == right.flowInvocationId && left.stepExecutionId == right.stepExecutionId &&
                left.actionInvocationId == right.actionInvocationId && left.sequenceStepId == right.sequenceStepId &&
                left.invocationId == right.invocationId;
        }

        private static bool SameCompletion(ActivityPrimitiveCompletion left, ActivityPrimitiveCompletion right)
        {
            return left.status == right.status && left.reasonCode == right.reasonCode &&
                IdentityEquals(left.identity, right.identity) && SameArguments(left.outputs, right.outputs);
        }

        private static ActivityPrimitiveCompletion CopyCompletion(ActivityPrimitiveCompletion completion)
        {
            return new ActivityPrimitiveCompletion(completion.identity, completion.status,
                completion.outputs, completion.reasonCode);
        }

        private static ActivitySequenceRequest CopyRequest(ActivitySequenceRequest request)
        {
            return new ActivitySequenceRequest(request.definition, request.actionDefinitionRef,
                request.scenarioRunId, request.flowInvocationId, request.stepExecutionId,
                request.actionInvocationId, request.context, request.arguments);
        }
    }
}
