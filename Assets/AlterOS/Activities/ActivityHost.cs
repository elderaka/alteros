using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    /// <summary>Owns one physical activity at a time and makes Scenario requests idempotent.</summary>
    public sealed class ActivityHost
    {
        private sealed class RequestIdentity : IEquatable<RequestIdentity>
        {
            private readonly string runId;
            private readonly string stepId;
            private readonly string flowInvocationId;
            private readonly string stepExecutionId;
            private readonly string actionInvocationId;

            public RequestIdentity(string run, string step, string flow = "", string execution = "", string action = "")
            { runId = run ?? ""; stepId = step ?? ""; flowInvocationId = flow ?? ""; stepExecutionId = execution ?? ""; actionInvocationId = action ?? ""; }

            public bool Equals(RequestIdentity other)
            {
                return other != null && runId == other.runId && stepId == other.stepId &&
                    flowInvocationId == other.flowInvocationId && stepExecutionId == other.stepExecutionId &&
                    actionInvocationId == other.actionInvocationId;
            }

            public override bool Equals(object obj) { return Equals(obj as RequestIdentity); }
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (runId.GetHashCode() * 397) ^ stepId.GetHashCode();
                    hash = (hash * 397) ^ flowInvocationId.GetHashCode();
                    hash = (hash * 397) ^ stepExecutionId.GetHashCode();
                    return (hash * 397) ^ actionInvocationId.GetHashCode();
                }
            }
        }

        private sealed class RequestRecord
        {
            public readonly string payload;
            public readonly ActivityRequestResult result;
            public readonly ActivityExecutionContext context;
            public readonly IActivityExecution execution;
            public readonly bool scenarioManaged;
            public readonly HashSet<string> checkpoints = new HashSet<string>(StringComparer.Ordinal);
            public ActivityView outcome;

            public RequestRecord(string requestPayload, ActivityRequestResult requestResult,
                ActivityExecutionContext executionContext, IActivityExecution activityExecution, bool scenarioOwned = true)
            {
                payload = requestPayload;
                result = requestResult;
                context = executionContext;
                execution = activityExecution;
                scenarioManaged = scenarioOwned;
            }
        }

        private readonly Dictionary<string, ActivitySettings> definitions = new Dictionary<string, ActivitySettings>(StringComparer.Ordinal);
        private readonly Dictionary<ActivityExecutorKind, ActivityExecutionFactory> factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>();
        private readonly Dictionary<RequestIdentity, RequestRecord> requests = new Dictionary<RequestIdentity, RequestRecord>();
        private readonly Dictionary<string, RequestRecord> activities = new Dictionary<string, RequestRecord>(StringComparer.Ordinal);
        private readonly List<ActivityEvent> pendingEvents = new List<ActivityEvent>();
        private readonly Func<string> createActivityId;
        private IActivityExecution activeExecution;
        private RequestRecord activeRecord;
        private double nowSeconds;

        public ActivityView Current { get { return activeExecution == null ? null : activeExecution.View; } }
        public event Action<ActivityEvent> LifecycleChanged;
        public IReadOnlyList<string> SupportedCapabilityIds
        { get { return Array.AsReadOnly(definitions.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray()); } }

        public ActivityHost(IEnumerable<ActivitySettings> supportedDefinitions,
            IDictionary<ActivityExecutorKind, ActivityExecutionFactory> executorFactories,
            Func<string> activityIdFactory = null)
        {
            if (supportedDefinitions == null) throw new ArgumentNullException("supportedDefinitions");
            if (executorFactories == null) throw new ArgumentNullException("executorFactories");
            createActivityId = activityIdFactory ?? (() => Guid.NewGuid().ToString("N"));

            foreach (KeyValuePair<ActivityExecutorKind, ActivityExecutionFactory> factory in executorFactories)
            {
                if (!Enum.IsDefined(typeof(ActivityExecutorKind), factory.Key) || factory.Value == null)
                    throw new ArgumentException("Executor factories must have a supported kind and non-null factory.", "executorFactories");
                factories.Add(factory.Key, factory.Value);
            }

            foreach (ActivitySettings source in supportedDefinitions)
            {
                if (source == null) throw new ArgumentException("Activity definitions cannot contain null.", "supportedDefinitions");
                ActivitySettings copy = source.Copy();
                if (!Supports(copy.capabilityId, copy.executorKind))
                    throw new ArgumentException("The capability ID and executor kind are not a supported M3 pair.", "supportedDefinitions");
                if (!factories.ContainsKey(copy.executorKind))
                    throw new ArgumentException("No usable executor is registered for " + copy.capabilityId + ".", "executorFactories");
                if (definitions.ContainsKey(copy.capabilityId))
                    throw new ArgumentException("Duplicate activity capability ID: " + copy.capabilityId, "supportedDefinitions");
                definitions.Add(copy.capabilityId, copy);
            }
        }

        public ActivityRequestResult Request(ActivityRequest request, ScenarioRuntimeSnapshot context)
        { return RequestCore(request, context, true, null, null, null, null); }

        /// <summary>Starts a local furniture E action on this same host without creating a Scenario event stream.</summary>
        public ActivityRequestResult RequestFurnitureInteraction(ActivityRequest request, ScenarioRuntimeSnapshot context)
        { return RequestCore(request, context, false, null, null, null, null); }

        /// <summary>Starts one release-pinned sequence through this same exclusive ActivityHost.</summary>
        public ActivityRequestResult RequestSequence(ActivitySequenceRequest sequenceRequest)
        {
            if (sequenceRequest == null || sequenceRequest.context == null ||
                string.IsNullOrWhiteSpace(sequenceRequest.scenarioStepId) ||
                string.IsNullOrWhiteSpace(sequenceRequest.actionInvocationId) ||
                sequenceRequest.context.runId != sequenceRequest.scenarioRunId)
                return ActivityRequestResult.Unavailable("invalid_sequence_request");

            ScenarioRuntimeSnapshot context = WithSequenceCapability(sequenceRequest.context, sequenceRequest.scenarioStepId);
            var request = new ActivityRequest(sequenceRequest.scenarioRunId, sequenceRequest.scenarioStepId,
                ActivityCapabilityIds.ActivitySequence, new ScenarioActionArgument[0]);
            var identity = new RequestIdentity(sequenceRequest.scenarioRunId, string.Empty,
                sequenceRequest.flowInvocationId, sequenceRequest.stepExecutionId, sequenceRequest.actionInvocationId);
            return RequestCore(request, context, true, sequenceRequest, identity,
                sequenceRequest.actionInvocationId, SequencePayload(sequenceRequest));
        }

        /// <summary>Routes an asynchronous primitive completion only to the active sequence owner.</summary>
        public bool CompletePrimitive(ActivityPrimitiveCompletion completion)
        {
            ActivitySequenceExecution execution = activeExecution as ActivitySequenceExecution;
            if (execution == null) return false;
            try
            {
                execution.CompletePrimitive(completion);
                CollectEvents(execution, activeRecord);
                return true;
            }
            catch (Exception)
            {
                FailExecution(execution, activeRecord, "sequence_completion_failed");
                return false;
            }
        }

        private ActivityRequestResult RequestCore(ActivityRequest request, ScenarioRuntimeSnapshot context, bool scenarioManaged,
            ActivitySequenceRequest sequenceRequest, RequestIdentity identityOverride, string forcedActivityId, string payloadOverride)
        {
            if (request == null) return ActivityRequestResult.Unavailable("invalid_activity_request");
            if (string.IsNullOrWhiteSpace(request.runId) || string.IsNullOrWhiteSpace(request.stepId))
                return ActivityRequestResult.Unavailable("invalid_request_identity");

            RequestIdentity identity = identityOverride ?? new RequestIdentity(request.runId, request.stepId);
            string payload = payloadOverride ?? Payload(request);
            RequestRecord previous;
            if (requests.TryGetValue(identity, out previous))
                return previous.payload == payload ? previous.result : ActivityRequestResult.Unavailable("request_identity_conflict");

            ActivitySettings settings;
            if (context == null || context.runId != request.runId || context.checkpointStepId != request.stepId)
                return RememberUnavailable(identity, payload, "scenario_context_mismatch", scenarioManaged);
            if (!definitions.TryGetValue(request.capabilityId, out settings))
                return RememberUnavailable(identity, payload, "capability_unavailable", scenarioManaged);

            string argumentError = ValidateArguments(request, context, settings);
            if (argumentError.Length != 0) return RememberUnavailable(identity, payload, argumentError, scenarioManaged);
            if (activeExecution != null) return RememberUnavailable(identity, payload, "activity_busy", scenarioManaged);

            string id = forcedActivityId ?? createActivityId();
            if (string.IsNullOrWhiteSpace(id) || activities.ContainsKey(id))
                return RememberUnavailable(identity, payload, "invalid_activity_id", scenarioManaged);

            var executionContext = new ActivityExecutionContext(id, request, context, settings, sequenceRequest);
            IActivityExecution execution = null;
            try
            {
                execution = factories[settings.executorKind](executionContext);
                if (execution == null) return RememberUnavailable(identity, payload, "executor_unavailable", scenarioManaged);
                execution.Start(nowSeconds);
                if (!ViewMatches(execution.View, executionContext))
                {
                    SafeShutdown(execution, "invalid_executor_state");
                    return RememberUnavailable(identity, payload, "executor_start_failed", scenarioManaged);
                }
            }
            catch (Exception)
            {
                if (execution != null) SafeShutdown(execution, "executor_start_failed");
                return RememberUnavailable(identity, payload, "executor_start_failed", scenarioManaged);
            }

            ActivityRequestResult accepted = ActivityRequestResult.Accepted(id);
            var record = new RequestRecord(payload, accepted, executionContext, execution, scenarioManaged);
            requests.Add(identity, record);
            activities.Add(id, record);
            activeExecution = execution;
            activeRecord = record;
            var acceptedEvent = new ActivityEvent(ActivityEventKind.Accepted, id, request.runId, request.stepId,
                execution.View.phase, ActivityTerminalReason.None, "");
            if (scenarioManaged) pendingEvents.Add(acceptedEvent);
            LifecycleChanged?.Invoke(acceptedEvent);
            try { CollectEvents(execution, record); }
            catch (Exception) { FailExecution(execution, record, "executor_event_failed"); }
            return accepted;
        }

        public void Tick(double monotonicNowSeconds)
        {
            if (double.IsNaN(monotonicNowSeconds) || double.IsInfinity(monotonicNowSeconds) || monotonicNowSeconds < nowSeconds)
                throw new ArgumentOutOfRangeException("monotonicNowSeconds", "Activity time must be finite and monotonic.");
            nowSeconds = monotonicNowSeconds;
            if (activeExecution == null) return;

            IActivityExecution execution = activeExecution;
            RequestRecord record = activeRecord;
            try
            {
                execution.Tick(nowSeconds);
                CollectEvents(execution, record);
            }
            catch (Exception)
            {
                FailExecution(execution, record, "executor_tick_failed");
            }
        }

        public bool Stop(string activityId, string reason)
        {
            if (!Owns(activityId)) return false;
            IActivityExecution execution = activeExecution;
            RequestRecord record = activeRecord;
            try
            {
                bool accepted = execution.Stop(reason ?? "cancelled", nowSeconds);
                CollectEvents(execution, record);
                return accepted;
            }
            catch (Exception)
            {
                FailExecution(execution, record, "executor_stop_failed");
                return false;
            }
        }

        public bool BeginExchange(string activityId, string exchangeId)
        {
            if (!Owns(activityId) || string.IsNullOrWhiteSpace(exchangeId)) return false;
            try
            {
                activeExecution.BeginExchange(exchangeId, nowSeconds);
                CollectEvents(activeExecution, activeRecord);
                return true;
            }
            catch (Exception)
            {
                FailExecution(activeExecution, activeRecord, "executor_exchange_failed");
                return false;
            }
        }

        public bool EndExchange(string activityId, string exchangeId)
        {
            if (!Owns(activityId) || string.IsNullOrWhiteSpace(exchangeId)) return false;
            try
            {
                activeExecution.EndExchange(exchangeId, nowSeconds);
                CollectEvents(activeExecution, activeRecord);
                return true;
            }
            catch (Exception)
            {
                FailExecution(activeExecution, activeRecord, "executor_exchange_failed");
                return false;
            }
        }

        public void Shutdown(string reason)
        {
            if (activeExecution == null) return;
            IActivityExecution execution = activeExecution;
            RequestRecord record = activeRecord;
            try
            {
                execution.Shutdown(reason ?? "interrupted_by_quit", nowSeconds);
                CollectEvents(execution, record);
                if (ReferenceEquals(activeExecution, execution))
                    Finalize(record, execution.View, ActivityTerminalReason.InterruptedByQuit, "interrupted_by_quit");
            }
            catch (Exception)
            {
                FailExecution(execution, record, "executor_shutdown_failed");
            }
        }

        public bool TryGetOutcome(string activityId, out ActivityView outcome)
        {
            outcome = null;
            RequestRecord record;
            if (string.IsNullOrWhiteSpace(activityId) || !activities.TryGetValue(activityId, out record)) return false;
            outcome = record.outcome ?? (ReferenceEquals(activeRecord, record) ? record.execution.View : null);
            return outcome != null;
        }

        public IReadOnlyList<ActivityEvent> DrainEvents()
        {
            ActivityEvent[] result = pendingEvents.ToArray();
            pendingEvents.Clear();
            return Array.AsReadOnly(result);
        }

        private ActivityRequestResult RememberUnavailable(RequestIdentity identity, string payload, string reason, bool scenarioManaged)
        {
            ActivityRequestResult result = ActivityRequestResult.Unavailable(reason);
            requests.Add(identity, new RequestRecord(payload, result, null, null, scenarioManaged));
            return result;
        }

        private string ValidateArguments(ActivityRequest request, ScenarioRuntimeSnapshot context, ActivitySettings settings)
        {
            ScenarioCapabilitySnapshot capability = null;
            int capabilityCount = 0;
            foreach (ScenarioCapabilitySnapshot candidate in context.capabilities)
            {
                if (candidate == null || candidate.capabilityId != request.capabilityId) continue;
                capability = candidate;
                capabilityCount++;
            }
            if (capabilityCount == 0) return "capability_not_allowed";
            if (capabilityCount != 1) return "invalid_capability_snapshot";

            var bindings = new Dictionary<string, ScenarioArgumentBinding>(StringComparer.Ordinal);
            foreach (ScenarioArgumentBinding binding in capability.argumentBindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.name) || bindings.ContainsKey(binding.name))
                    return "invalid_capability_snapshot";
                bindings.Add(binding.name, binding);
            }
            if (request.arguments.Count != bindings.Count) return "invalid_arguments";

            var supplied = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ScenarioActionArgument argument in request.arguments)
            {
                if (argument == null || string.IsNullOrWhiteSpace(argument.name) || supplied.ContainsKey(argument.name))
                    return "invalid_arguments";
                supplied.Add(argument.name, argument.value ?? "");
            }
            if (supplied.Count != bindings.Count) return "invalid_arguments";

            foreach (KeyValuePair<string, ScenarioArgumentBinding> item in bindings)
            {
                string value;
                if (!supplied.TryGetValue(item.Key, out value) || string.IsNullOrWhiteSpace(value)) return "invalid_arguments";
                if (!item.Value.values.Contains(value, StringComparer.Ordinal)) return "invalid_arguments";
                if (settings.executorKind == ActivityExecutorKind.Movement && (item.Key != "target" || value != "player"))
                    return "invalid_arguments";
                if (settings.executorKind == ActivityExecutorKind.DrinkWater && item.Key == "target" && value != "available_dispenser")
                {
                    bool isKnownUsePoint = context.objects.Any(target => target != null &&
                        target.objectId == value && (target.kind == "use_point" || target.kind == "furniture"));
                    if (!isKnownUsePoint) return "unknown_target_object";
                }
            }
            return "";
        }

        private void CollectEvents(IActivityExecution execution, RequestRecord record)
        {
            IReadOnlyList<ActivityEvent> emitted = execution.DrainEvents();
            if (emitted != null)
            {
                foreach (ActivityEvent item in emitted)
                {
                    if (item == null || item.activityId != record.context.activityId ||
                        item.runId != record.context.request.runId || item.stepId != record.context.request.stepId) continue;
                    if (item.kind == ActivityEventKind.Terminal)
                    {
                        ActivityTerminalReason terminal = item.terminalReason == ActivityTerminalReason.None
                            ? ActivityTerminalReason.Failed : item.terminalReason;
                        Finalize(record, execution.View, terminal, item.phase);
                        continue;
                    }
                    if (item.kind == ActivityEventKind.Checkpoint &&
                        (string.IsNullOrWhiteSpace(item.checkpointId) || !record.checkpoints.Add(item.checkpointId))) continue;
                    if (record.scenarioManaged) pendingEvents.Add(item);
                }
            }

            ActivityView view = execution.View;
            if (view != null && view.isTerminal) Finalize(record, view, view.terminalReason, view.phase);
        }

        private void FailExecution(IActivityExecution execution, RequestRecord record, string phase)
        {
            SafeShutdown(execution, phase);
            if (record.outcome == null) Finalize(record, execution.View, ActivityTerminalReason.Failed, phase);
        }

        private void SafeShutdown(IActivityExecution execution, string reason)
        {
            try { execution.Shutdown(reason, nowSeconds); }
            catch (Exception) { }
        }

        private void Finalize(RequestRecord record, ActivityView source, ActivityTerminalReason reason, string phase)
        {
            if (record == null || record.outcome != null) return;
            ActivityView previous = source ?? (ReferenceEquals(activeRecord, record) ? activeExecution.View : null);
            record.outcome = new ActivityView(record.context.activityId, record.context.request.runId,
                record.context.request.stepId, record.context.settings.capabilityId,
                string.IsNullOrWhiteSpace(phase) ? "terminal" : phase, reason,
                previous == null || previous.canTalk, previous == null || previous.canPause);
            if (ReferenceEquals(activeRecord, record))
            {
                activeExecution = null;
                activeRecord = null;
            }
            var terminalEvent = new ActivityEvent(ActivityEventKind.Terminal, record.context.activityId,
                record.context.request.runId, record.context.request.stepId, record.outcome.phase, reason, "");
            if (record.scenarioManaged) pendingEvents.Add(terminalEvent);
            LifecycleChanged?.Invoke(terminalEvent);
        }

        private bool Owns(string activityId)
        {
            return activeExecution != null && activeRecord != null &&
                string.Equals(activeRecord.context.activityId, activityId, StringComparison.Ordinal);
        }

        private static bool ViewMatches(ActivityView view, ActivityExecutionContext context)
        {
            return view != null && view.activityId == context.activityId && view.runId == context.request.runId &&
                view.stepId == context.request.stepId && view.capabilityId == context.settings.capabilityId && !view.isTerminal;
        }

        private static string Payload(ActivityRequest request)
        {
            string[] values = request.arguments.Select(argument =>
                (argument == null ? "" : argument.name ?? "") + "\u001f" + (argument == null ? "" : argument.value ?? ""))
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return (request.capabilityId ?? "") + "\u001e" + string.Join("\u001e", values);
        }

        private static ScenarioRuntimeSnapshot WithSequenceCapability(ScenarioRuntimeSnapshot source, string stepId)
        {
            var capabilities = new List<ScenarioCapabilitySnapshot>(source.capabilities);
            if (!capabilities.Any(item => item != null && item.capabilityId == ActivityCapabilityIds.ActivitySequence))
                capabilities.Add(new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]));
            return new ScenarioRuntimeSnapshot(source.scenarioId, source.runId, source.turnId, source.contextRevision,
                stepId, source.knownTopicIds, source.allowedExpressions, capabilities, source.objects, source.waitConditions,
                source.knownIntentIds, source.hostFacts.ToDictionary(item => item.Key, item => item.Value));
        }

        private static string SequencePayload(ActivitySequenceRequest request)
        {
            var payload = new System.Text.StringBuilder();
            Append(payload, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.store);
            Append(payload, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.id);
            Append(payload, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.revision);
            Append(payload, request.actionDefinitionRef == null ? "" : request.actionDefinitionRef.expectedHash);
            Append(payload, request.definition == null ? "" : request.definition.id);
            Append(payload, request.definition == null ? "" : request.definition.revision);
            Append(payload, request.context == null ? "" : request.context.contextRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            IEnumerable<ActivitySequenceStep> sequenceSteps = request.definition == null
                ? (IEnumerable<ActivitySequenceStep>)new ActivitySequenceStep[0] : request.definition.steps;
            foreach (ActivitySequenceStep step in sequenceSteps)
            {
                Append(payload, step.stepId); Append(payload, step.primitiveId);
                Append(payload, step.successNextStepId); Append(payload, step.failureNextStepId);
                Append(payload, step.cancelNextStepId); Append(payload, ((int)step.checkpointPolicy).ToString());
                foreach (ActivitySequenceArgument argument in step.arguments)
                {
                    Append(payload, argument.name);
                    Append(payload, argument.value == null ? "" : argument.value.bindingId);
                    Append(payload, argument.value == null || argument.value.literal == null ? "" : argument.value.literal.type);
                    Append(payload, argument.value == null || argument.value.literal == null ? "" : argument.value.literal.ToHostArgument());
                }
                foreach (ActivitySequenceResultBinding binding in step.resultBindings)
                { Append(payload, binding.resultName); Append(payload, binding.bindingId); }
            }
            foreach (ActivitySequenceArgument argument in request.arguments)
            {
                Append(payload, argument.name);
                Append(payload, argument.value == null ? "" : argument.value.bindingId);
                Append(payload, argument.value == null || argument.value.literal == null ? "" : argument.value.literal.type);
                Append(payload, argument.value == null || argument.value.literal == null ? "" : argument.value.literal.ToHostArgument());
            }
            return payload.ToString();
        }

        private static void Append(System.Text.StringBuilder builder, string value)
        { value = value ?? ""; builder.Append(value.Length).Append(':').Append(value); }

        private static bool Supports(string capabilityId, ActivityExecutorKind executorKind)
        {
            if (executorKind == ActivityExecutorKind.Movement)
                return capabilityId == ActivityCapabilityIds.ComeHere || capabilityId == ActivityCapabilityIds.FollowPlayer;
            if (executorKind == ActivityExecutorKind.ActivitySequence)
                return capabilityId == ActivityCapabilityIds.ActivitySequence;
            return executorKind == ActivityExecutorKind.DrinkWater && capabilityId == ActivityCapabilityIds.DrinkWater;
        }
    }
}
