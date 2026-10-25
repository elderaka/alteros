using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Furniture;
using AlterOS.Persistence;
using AlterOS.Progression;
using Pleiades.Furniture;
using UnityEngine;

namespace AlterOS.Activities
{
    /// <summary>Scene-facing primitive composition for the existing exclusive ActivityHost.</summary>
    public sealed class ActivitySequenceScenePrimitivePort : MonoBehaviour, IActivitySequencePollingPort
    {
        [SerializeField] private ActivitySceneHost activityHost;
        [SerializeField] private FurnitureSceneAdapter furnitureSceneAdapter;
        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private AlteraAmbientBubble ambientBubble;
        [SerializeField] private string roomId = "room.testbox.modeltest";
        [SerializeField] private Transform roomRoot;
        [SerializeField, Min(0.25f)] private float nearPlayerSeparation = 1.2f;
        [SerializeField, Min(1f)] private float movementTimeoutSeconds = 120f;

        private sealed class TargetRecord
        {
            public readonly string ownerId;
            public readonly FurnitureBinding binding;
            public TargetRecord(string owner, FurnitureBinding target) { ownerId = owner; binding = target; }
        }

        private readonly Dictionary<string, TargetRecord> targets = new Dictionary<string, TargetRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> knownActionInvocations = new HashSet<string>(StringComparer.Ordinal);
        private ActivitySequencePrimitiveDispatcher dispatcher;
        private double nowSeconds;
        public event Action<string> Diagnostic;
        private void ReportDiagnostic(string detail) { Diagnostic?.Invoke(detail); }

        private void Awake()
        {
            nowSeconds = Time.realtimeSinceStartupAsDouble;
            if (activityHost == null) activityHost = GetComponent<ActivitySceneHost>();
            if (furnitureSceneAdapter == null) furnitureSceneAdapter = GetComponent<FurnitureSceneAdapter>();
            if (progressionStore == null) progressionStore = GetComponent<AlteraProgressionStore>();
            if (ambientBubble == null) ambientBubble = GetComponentInChildren<AlteraAmbientBubble>(true);
            dispatcher = new ActivitySequencePrimitiveDispatcher(new IActivitySequencePrimitiveOperation[]
            {
                new ReserveNearestOperation(this),
                new SceneMovementOperation(this, ActivityPrimitiveIds.MoveTo),
                new FaceOperation(this),
                new ActivitySequenceBarkOperation(new AmbientBarkPresenter(this)),
                new WaitOperation(this),
                new FurnitureInvokeOperation(this),
                new SceneMovementOperation(this, ActivityPrimitiveIds.MoveNearPlayer),
                new CompleteOperation()
            });
        }

        ActivityPrimitiveStartResult IActivityPrimitivePort.Start(ActivityPrimitiveRequest request)
        {
            if (!isActiveAndEnabled || dispatcher == null) return Failure(request, "primitive_port_unavailable");
            if (request != null && request.identity != null && !string.IsNullOrWhiteSpace(request.identity.actionInvocationId))
                knownActionInvocations.Add(request.identity.actionInvocationId);
            ActivityPrimitiveStartResult result = dispatcher.Start(request);
            Diagnostic?.Invoke((request == null ? "unknown" : request.primitiveId) + " start=" + result.status +
                " reason=" + result.reasonCode + " invocation=" + (request?.identity?.invocationId ?? "none"));
            return result;
        }

        public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
        {
            ActivityPrimitiveCompletion result = dispatcher == null ? Cancelled(request) : dispatcher.Cancel(request);
            Diagnostic?.Invoke("cancel=" + result.status + " reason=" + result.reasonCode + " invocation=" + (result.identity?.invocationId ?? "none"));
            return result;
        }

        public ActivityPrimitiveCompletion[] Tick(double currentTimeSeconds)
        {
            nowSeconds = currentTimeSeconds;
            ActivityPrimitiveCompletion[] results = dispatcher == null ? new ActivityPrimitiveCompletion[0] : dispatcher.Tick(currentTimeSeconds);
            foreach (ActivityPrimitiveCompletion result in results)
                Diagnostic?.Invoke("finish=" + result.status + " reason=" + result.reasonCode + " invocation=" + (result.identity?.invocationId ?? "none"));
            return results;
        }

        public void ReleaseSequence(string actionInvocationId)
        {
            if (dispatcher != null) dispatcher.ReleaseSequence(actionInvocationId);
            knownActionInvocations.Remove(actionInvocationId ?? string.Empty);
            foreach (string token in targets.Where(item => item.Value.ownerId == actionInvocationId).Select(item => item.Key).ToArray())
                targets.Remove(token);
        }

        private void OnDisable()
        {
            foreach (string actionId in knownActionInvocations.ToArray()) ReleaseSequence(actionId);
        }

        private bool TryReserve(string useId, string functionId, string actionInvocationId,
            ActivityPrimitiveIdentity identity, out string token)
        {
            token = string.Empty;
            if (activityHost == null || furnitureSceneAdapter == null || string.IsNullOrWhiteSpace(actionInvocationId)) return false;
            FurnitureCatalogueSnapshot catalogue = furnitureSceneAdapter.Catalogue;
            if (catalogue == null || catalogue.diagnostics.Any(item => item.severity == "error")) return false;
            Func<FurnitureUsePoint, bool> eligibility = point => IsFunctionEligible(catalogue, point, useId, functionId);
            FurnitureBinding binding;
            if (!activityHost.TryReserveSequenceFurniture(useId, "", actionInvocationId, eligibility, out binding))
            {
                Diagnostic?.Invoke("reserve failed: no eligible/reachable point use=" + useId + " function=" + functionId);
                return false;
            }
            token = "target." + identity.invocationId;
            if (targets.ContainsKey(token))
            {
                activityHost.ReleaseSequenceFurniture(actionInvocationId);
                return false;
            }
            targets.Add(token, new TargetRecord(actionInvocationId, binding));
            Diagnostic?.Invoke("reserved furniture=" + binding.point.InstanceId + " point=" + binding.point.UsePointId +
                " use=" + useId + " function=" + functionId);
            return true;
        }

        private static bool IsFunctionEligible(FurnitureCatalogueSnapshot catalogue, FurnitureUsePoint point,
            string useId, string functionId)
        {
            if (point == null || !FurnitureInteractionPolicy.TryResolvePoint(catalogue, point.InstanceId,
                point.UsePointId, string.Empty, string.Empty, out string definitionId, out string cataloguePointId)) return false;
            FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item != null && item.definitionId == definitionId);
            if (definition == null) return false;
            if (string.IsNullOrWhiteSpace(functionId)) return true;
            if (functionId == FurnitureFunctionIds.PowerSet)
                return (definition.functionBindings ?? new List<FurnitureFunctionBindingDescriptor>()).Any(item => item != null &&
                    item.functionId == functionId && item.pointId == cataloguePointId && item.useId == useId);
            if (functionId == FurniturePresentationDescriptorIds.DispenserCollectMock)
                return (definition.presentationBindings ?? new List<FurniturePresentationBindingDescriptor>()).Any(item => item != null &&
                    item.descriptorId == functionId && item.pointId == cataloguePointId && item.useId == useId);
            return false;
        }

        private bool TryResolveTarget(ActivityPrimitiveRequest request, out FurnitureBinding binding)
        {
            binding = null;
            FlowValue target = Value(request, "target");
            if (target == null || target.type != "entity" || target.entityValue == null) return false;
            TargetRecord record;
            if (!targets.TryGetValue(target.entityValue.id, out record) || record.ownerId != request.identity.actionInvocationId ||
                activityHost == null || !activityHost.IsCurrentSequenceFurnitureBinding(record.binding)) return false;
            binding = record.binding;
            return true;
        }

        private FurnitureFunctionReservationEvidence ResolveReservation(FurnitureFunctionReservationRequest request)
        {
            if (request == null || activityHost == null) return null;
            TargetRecord record = targets.Values.FirstOrDefault(item => item.ownerId == request.activityId &&
                item.binding.instanceId == request.instanceId && item.binding.usePointId == request.usePointId &&
                item.binding.useId == request.useId && item.binding.CapturedRevision == request.usePointRevision);
            if (record == null || !activityHost.IsCurrentSequenceFurnitureBinding(record.binding)) return null;
            return new FurnitureFunctionReservationEvidence(request.activityId, request.instanceId,
                request.usePointId, request.useId, request.usePointRevision);
        }

        private Func<string, HomeFurnitureLayoutDocument> CreateSceneSeedProvider()
        {
            return homeId => furnitureSceneAdapter == null || roomRoot == null ? null :
                furnitureSceneAdapter.CreateHomeLayoutSeed(homeId, roomId, roomRoot);
        }

        private static ActivitySequenceArgument FunctionStatus(string status)
        { return ActivitySequenceArgument.FromLiteral("functionStatus", FlowValue.Enum(status)); }

        private static ActivityPrimitiveStartResult Completed(ActivityPrimitiveRequest request,
            IEnumerable<ActivitySequenceArgument> outputs = null, string reason = "")
        { return new ActivityPrimitiveStartResult(request.identity, ActivitySequenceStatuses.Completed, outputs, reason); }

        private static ActivityPrimitiveStartResult Running(ActivityPrimitiveRequest request)
        { return new ActivityPrimitiveStartResult(request.identity, ActivitySequenceStatuses.Running,
            new ActivitySequenceArgument[0], string.Empty); }

        private static ActivityPrimitiveStartResult Failure(ActivityPrimitiveRequest request, string reason)
        { return new ActivityPrimitiveStartResult(request == null ? null : request.identity, ActivitySequenceStatuses.Failed,
            new ActivitySequenceArgument[0], reason); }

        private static ActivityPrimitiveCompletion Cancelled(ActivityPrimitiveCancellation request)
        { return new ActivityPrimitiveCompletion(request == null ? null : request.identity, ActivitySequenceStatuses.Cancelled,
            new ActivitySequenceArgument[0], request == null ? "primitive_cancel_invalid" : request.reasonCode); }

        private static FlowValue Value(ActivityPrimitiveRequest request, string name)
        {
            ActivitySequenceArgument argument = request == null ? null : request.arguments.FirstOrDefault(item => item != null && item.name == name);
            return argument == null || argument.value == null ? null : argument.value.literal;
        }

        private static string Text(ActivityPrimitiveRequest request, string name)
        {
            FlowValue value = Value(request, name);
            return value == null || value.type != "text" ? string.Empty : value.textValue;
        }

        private static string Enum(ActivityPrimitiveRequest request, string name)
        {
            FlowValue value = Value(request, name);
            return value == null || value.type != "enum" ? string.Empty : value.textValue;
        }

        private static ActivityPrimitiveCompletion Completion(ActivityPrimitiveIdentity identity,
            string status, string reason, IEnumerable<ActivitySequenceArgument> outputs = null)
        { return new ActivityPrimitiveCompletion(identity, status, outputs, reason); }

        private static string IdentityKey(ActivityPrimitiveIdentity identity)
        {
            return string.Concat(identity.scenarioRunId, "\n", identity.flowInvocationId, "\n", identity.stepExecutionId,
                "\n", identity.actionInvocationId, "\n", identity.sequenceStepId, "\n", identity.invocationId);
        }

        private sealed class ReserveNearestOperation : IActivitySequenceResourceOperation
        {
            private readonly ActivitySequenceScenePrimitivePort owner;
            public ReserveNearestOperation(ActivitySequenceScenePrimitivePort port) { owner = port; }
            public string PrimitiveId { get { return ActivityPrimitiveIds.ReserveNearest; } }

            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            {
                string useId = Text(request, "useId");
                FlowValue selectedFunction = Value(request, "functionId");
                string functionId = selectedFunction == null ? string.Empty : selectedFunction.textValue;
                if (string.IsNullOrWhiteSpace(useId) || selectedFunction != null && selectedFunction.type != "enum")
                    return Failure(request, "reservation_arguments_invalid");
                string token;
                if (!owner.TryReserve(useId, functionId, request.identity.actionInvocationId, request.identity, out token))
                    return Failure(request, "furniture_reservation_unavailable");
                return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("targetToken",
                    FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, token)) });
            }

            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
            { return Cancelled(request); }

            public void ReleaseSequence(string actionInvocationId)
            {
                if (owner.activityHost != null) owner.activityHost.ReleaseSequenceFurniture(actionInvocationId);
                foreach (string token in owner.targets.Where(item => item.Value.ownerId == actionInvocationId).Select(item => item.Key).ToArray())
                    owner.targets.Remove(token);
            }
        }

        private sealed class SceneMovementOperation : IActivitySequencePollingOperation, IActivitySequenceResourceOperation
        {
            private sealed class Moving
            {
                public ActivityPrimitiveIdentity identity;
                public FurnitureBinding target;
                public double startedAt;
            }

            private readonly ActivitySequenceScenePrimitivePort owner;
            private readonly string primitiveId;
            private readonly Dictionary<string, Moving> active = new Dictionary<string, Moving>(StringComparer.Ordinal);

            public SceneMovementOperation(ActivitySequenceScenePrimitivePort port, string id)
            { owner = port; primitiveId = id; }
            public string PrimitiveId { get { return primitiveId; } }

            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            {
                var moving = new Moving { identity = request.identity, startedAt = owner.nowSeconds };
                bool started;
                if (primitiveId == ActivityPrimitiveIds.MoveNearPlayer)
                    started = owner.activityHost != null && owner.activityHost.TryResolveSequenceNearPlayer(owner.nearPlayerSeparation, out _);
                else
                {
                    if (!owner.TryResolveTarget(request, out moving.target) || moving.target.point.Approach == null)
                        return Failure(request, "reserved_target_unavailable");
                    started = owner.activityHost.TryMoveSequenceTo(moving.target.point.Approach.position);
                }
                if (!started) return Failure(request, "movement_unavailable");
                owner.ReportDiagnostic("movement started primitive=" + primitiveId + " target=" +
                    (moving.target == null ? "player" : moving.target.point.UsePointId));
                active.Add(IdentityKey(request.identity), moving);
                return Running(request);
            }

            public ActivityPrimitiveCompletion Poll(ActivityPrimitiveIdentity identity, double currentSeconds)
            {
                Moving moving;
                string key = IdentityKey(identity);
                if (!active.TryGetValue(key, out moving) || owner.activityHost == null) return null;
                if (moving.target != null && !owner.activityHost.IsCurrentSequenceFurnitureBinding(moving.target))
                    return Finish(moving, ActivitySequenceStatuses.Failed, "reserved_target_changed");
                if (owner.activityHost.SequenceMovementArrived)
                    return Finish(moving, ActivitySequenceStatuses.Completed, string.Empty,
                        new[] { ActivitySequenceArgument.FromLiteral("arrived", FlowValue.Boolean(true)) });
                if (owner.activityHost.SequenceMovementBlocked || currentSeconds - moving.startedAt >= owner.movementTimeoutSeconds)
                    return Finish(moving, ActivitySequenceStatuses.Failed, "movement_blocked");
                return null;
            }

            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
            {
                Moving moving;
                if (request != null && request.identity != null && active.TryGetValue(IdentityKey(request.identity), out moving))
                    Finish(moving, ActivitySequenceStatuses.Cancelled, request.reasonCode);
                else if (owner.activityHost != null) owner.activityHost.ClearSequenceMovement();
                return Cancelled(request);
            }

            public void ReleaseSequence(string actionInvocationId)
            {
                foreach (Moving moving in active.Values.Where(item => item.identity.actionInvocationId == actionInvocationId).ToArray())
                    Finish(moving, ActivitySequenceStatuses.Cancelled, "sequence_released");
            }

            private ActivityPrimitiveCompletion Finish(Moving moving, string status, string reason,
                IEnumerable<ActivitySequenceArgument> outputs = null)
            {
                active.Remove(IdentityKey(moving.identity));
                if (owner.activityHost != null) owner.activityHost.ClearSequenceMovement();
                return Completion(moving.identity, status, reason, outputs);
            }
        }

        private sealed class FaceOperation : IActivitySequencePrimitiveOperation
        {
            private readonly ActivitySequenceScenePrimitivePort owner;
            public FaceOperation(ActivitySequenceScenePrimitivePort port) { owner = port; }
            public string PrimitiveId { get { return ActivityPrimitiveIds.Face; } }
            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            {
                FurnitureBinding binding;
                if (!owner.TryResolveTarget(request, out binding) || binding.point.Use == null || owner.activityHost == null)
                    return Failure(request, "reserved_target_unavailable");
                owner.activityHost.FaceSequencePosition(binding.point.Use.position);
                return Completed(request);
            }
            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request) { return Cancelled(request); }
        }

        private sealed class WaitOperation : IActivitySequencePollingOperation, IActivitySequenceResourceOperation
        {
            private sealed class WaitRecord { public ActivityPrimitiveIdentity identity; public double endsAt; }
            private readonly ActivitySequenceScenePrimitivePort owner;
            private readonly Dictionary<string, WaitRecord> pending = new Dictionary<string, WaitRecord>(StringComparer.Ordinal);
            public WaitOperation(ActivitySequenceScenePrimitivePort port) { owner = port; }
            public string PrimitiveId { get { return ActivityPrimitiveIds.Wait; } }

            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            {
                FlowValue duration = Value(request, "durationSeconds");
                if (duration == null || duration.type != "double" || duration.doubleValue < 0d || duration.doubleValue > 300d)
                    return Failure(request, "wait_duration_invalid");
                pending.Add(IdentityKey(request.identity), new WaitRecord
                { identity = request.identity, endsAt = owner.nowSeconds + duration.doubleValue });
                return Running(request);
            }

            public ActivityPrimitiveCompletion Poll(ActivityPrimitiveIdentity identity, double currentSeconds)
            {
                WaitRecord wait;
                string key = IdentityKey(identity);
                if (!pending.TryGetValue(key, out wait) || currentSeconds < wait.endsAt) return null;
                pending.Remove(key);
                return Completion(identity, ActivitySequenceStatuses.Completed, string.Empty);
            }

            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
            {
                if (request != null && request.identity != null) pending.Remove(IdentityKey(request.identity));
                return Cancelled(request);
            }

            public void ReleaseSequence(string actionInvocationId)
            {
                foreach (string key in pending.Where(item => item.Value.identity.actionInvocationId == actionInvocationId)
                    .Select(item => item.Key).ToArray()) pending.Remove(key);
            }
        }

        private sealed class FurnitureInvokeOperation : IActivitySequencePollingOperation, IActivitySequenceResourceOperation
        {
            private sealed class Invocation
            {
                public ActivityPrimitiveIdentity identity;
                public FurniturePresentationRegistry presentationPort;
                public FurniturePresentationHandle presentationHandle;
            }

            private readonly ActivitySequenceScenePrimitivePort owner;
            private readonly Dictionary<string, Invocation> active = new Dictionary<string, Invocation>(StringComparer.Ordinal);
            public FurnitureInvokeOperation(ActivitySequenceScenePrimitivePort port) { owner = port; }
            public string PrimitiveId { get { return ActivityPrimitiveIds.InvokeFurniture; } }

            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            {
                FurnitureBinding binding;
                string functionId = Enum(request, "functionId");
                if (!owner.TryResolveTarget(request, out binding) || string.IsNullOrWhiteSpace(functionId) ||
                    owner.furnitureSceneAdapter == null) return Failure(request, "furniture_invocation_unavailable");
                if (functionId == FurnitureFunctionIds.PowerSet) return InvokePower(request, binding);
                if (functionId == FurniturePresentationDescriptorIds.DispenserCollectMock)
                    return StartPresentation(request, binding);
                return Failure(request, "furniture_function_unavailable");
            }

            private ActivityPrimitiveStartResult InvokePower(ActivityPrimitiveRequest request, FurnitureBinding binding)
            {
                string desired = Enum(request, "desiredState");
                FurniturePowerState desiredState;
                if (!System.Enum.TryParse(desired, false, out desiredState) || !System.Enum.IsDefined(typeof(FurniturePowerState), desiredState))
                    return Failure(request, "power_state_invalid");
                FurnitureCatalogueSnapshot catalogue = owner.furnitureSceneAdapter.Catalogue;
                if (catalogue == null || owner.progressionStore == null) return Failure(request, "furniture_power_unavailable");
                Func<string, HomeFurnitureLayoutDocument> seed = owner.CreateSceneSeedProvider();
                IFurnitureHomeStore homeStore = owner.progressionStore.CreateFurnitureHomeStore(seed);
                FurnitureHomeReadResult current = homeStore == null ? null : homeStore.Read();
                HomeFurnitureLayoutInstance instance = current == null || current.document == null || current.document.instances == null
                    ? null : current.document.instances.FirstOrDefault(item => item != null && item.instanceId == binding.instanceId);
                if (current == null || current.state != GamePersistenceStates.Ready || instance == null ||
                    current.gameRevision < 0 || current.document.layoutRevision < 0 || instance.instanceRevision < 0)
                    return Failure(request, "furniture_power_state_unavailable");
                IFurniturePowerStore store = owner.progressionStore.CreateFurniturePowerStore(seed);
                if (store == null) return Failure(request, "furniture_power_store_unavailable");
                var reservationSource = new ReservationContextSource(owner);
                var validator = new FurnitureFunctionTargetValidator(catalogue, owner.furnitureSceneAdapter, reservationSource);
                var functions = new FurnitureFunctionRegistry(validator);
                if (!functions.Register(new FurniturePowerFunction(store))) return Failure(request, "furniture_power_handler_unavailable");
                string operationKey = string.Join("\n", new[] { "furniture-power-set", request.identity.scenarioRunId,
                    request.identity.flowInvocationId, request.identity.stepExecutionId,
                    request.identity.actionInvocationId, request.identity.sequenceStepId });
                string operationId = GameOperationId.FromStableKey(operationKey);
                var functionRequest = new FurnitureFunctionRequest
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    instanceId = binding.instanceId,
                    definitionId = ResolveDefinitionId(catalogue, binding),
                    usePointId = binding.usePointId,
                    useId = binding.useId,
                    usePointRevision = binding.CapturedRevision,
                    reservationOwnerId = binding.activityId,
                    expectedInstanceRevision = instance.instanceRevision,
                    expectedLayoutRevision = current.document.layoutRevision,
                    expectedGameRevision = current.gameRevision,
                    runId = request.identity.scenarioRunId,
                    invocationId = request.identity.flowInvocationId,
                    stepExecutionId = request.identity.stepExecutionId,
                    activityId = request.identity.actionInvocationId,
                    sequenceStepId = request.identity.sequenceStepId,
                    operationId = operationId,
                    arguments = new FurniturePowerArguments { desiredState = desiredState }
                };
                FurnitureFunctionResult result = functions.Invoke(functionRequest);
                if (result == null || result.status != FurnitureFunctionStatuses.Applied &&
                    result.status != FurnitureFunctionStatuses.NoChange && result.status != FurnitureFunctionStatuses.Replayed)
                    return Failure(request, result == null ? "furniture_power_failed" : result.diagnosticCode);

                FurnitureHomeReadResult committed = homeStore.Read();
                HomeFurnitureLayoutInstance committedInstance = committed == null || committed.document == null || committed.document.instances == null
                    ? null : committed.document.instances.FirstOrDefault(item => item != null && item.instanceId == binding.instanceId);
                if (committed != null && committed.state == GamePersistenceStates.Ready && committedInstance != null)
                {
                    FurniturePlacedInstanceIdentity visualIdentity = binding.point.GetComponentInParent<FurniturePlacedInstanceIdentity>();
                    if (visualIdentity != null && visualIdentity.InstanceId == binding.instanceId)
                        owner.furnitureSceneAdapter.ApplyCommittedPowerState(visualIdentity.gameObject, committedInstance);
                }
                return Completed(request, new[] { FunctionStatus(result.status) });
            }

            private ActivityPrimitiveStartResult StartPresentation(ActivityPrimitiveRequest request, FurnitureBinding binding)
            {
                FurnitureCatalogueSnapshot catalogue = owner.furnitureSceneAdapter.Catalogue;
                if (catalogue == null) return Failure(request, "furniture_catalogue_unavailable");
                var validator = new FurniturePresentationTargetValidator(catalogue, owner.furnitureSceneAdapter,
                    new ReservationContextSource(owner));
                var port = new FurniturePresentationRegistry(validator);
                if (!port.Register(new DispenserCollectMockPresentation())) return Failure(request, "presentation_unavailable");
                var presentationRequest = new FurniturePresentationRequest
                {
                    presentationRequestId = request.identity.invocationId,
                    presentationDescriptorId = FurniturePresentationDescriptorIds.DispenserCollectMock,
                    runId = request.identity.scenarioRunId,
                    invocationId = request.identity.flowInvocationId,
                    actionDefinitionRef = request.actionDefinitionRef,
                    activityId = request.identity.actionInvocationId,
                    sequenceStepId = request.identity.sequenceStepId,
                    instanceId = binding.instanceId,
                    definitionId = ResolveDefinitionId(catalogue, binding),
                    usePointId = binding.usePointId,
                    usePointRevision = binding.CapturedRevision
                };
                FurniturePresentationResult started = port.Start(presentationRequest);
                if (started == null || started.status != FurniturePresentationStatuses.Accepted &&
                    started.status != FurniturePresentationStatuses.Running) return Failure(request,
                        started == null ? "presentation_start_failed" : started.diagnosticCode);
                active.Add(IdentityKey(request.identity), new Invocation
                { identity = request.identity, presentationPort = port, presentationHandle = started.handle });
                return Running(request);
            }

            public ActivityPrimitiveCompletion Poll(ActivityPrimitiveIdentity identity, double nowSeconds)
            {
                Invocation invocation;
                string key = IdentityKey(identity);
                if (!active.TryGetValue(key, out invocation)) return null;
                FurniturePresentationResult result = invocation.presentationPort.Poll(invocation.presentationHandle);
                if (result == null || result.status == FurniturePresentationStatuses.Accepted ||
                    result.status == FurniturePresentationStatuses.Running) return null;
                active.Remove(key);
                if (result.status == FurniturePresentationStatuses.Completed)
                    return Completion(identity, ActivitySequenceStatuses.Completed, string.Empty,
                        new[] { FunctionStatus("completed") });
                if (result.status == FurniturePresentationStatuses.Cancelled)
                    return Completion(identity, ActivitySequenceStatuses.Cancelled, result.diagnosticCode);
                return Completion(identity, ActivitySequenceStatuses.Failed, result.diagnosticCode);
            }

            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
            {
                if (request != null && request.identity != null)
                {
                    Invocation invocation;
                    if (active.TryGetValue(IdentityKey(request.identity), out invocation))
                    {
                        FurniturePresentationResult result = invocation.presentationPort.Cancel(invocation.presentationHandle);
                        if (result != null && (result.status == FurniturePresentationStatuses.Cancelled ||
                            result.status == FurniturePresentationStatuses.Completed)) active.Remove(IdentityKey(request.identity));
                    }
                }
                return Cancelled(request);
            }

            public void ReleaseSequence(string actionInvocationId)
            {
                foreach (Invocation invocation in active.Values.Where(item => item.identity.actionInvocationId == actionInvocationId).ToArray())
                    Cancel(new ActivityPrimitiveCancellation(invocation.identity, "sequence_released"));
            }

            private static string ResolveDefinitionId(FurnitureCatalogueSnapshot catalogue, FurnitureBinding binding)
            {
                string definitionId, pointId;
                return FurnitureInteractionPolicy.TryResolvePoint(catalogue, binding.instanceId, binding.usePointId,
                    string.Empty, string.Empty, out definitionId, out pointId) ? definitionId : string.Empty;
            }
        }

        private sealed class ReservationContextSource : IFurnitureFunctionReservationContextSource
        {
            private readonly ActivitySequenceScenePrimitivePort owner;
            public ReservationContextSource(ActivitySequenceScenePrimitivePort port) { owner = port; }
            public FurnitureFunctionReservationEvidence ResolveCurrent(FurnitureFunctionReservationRequest request)
            { return owner.ResolveReservation(request); }
        }

        private sealed class AmbientBarkPresenter : IActivitySequenceBarkPresenter
        {
            private readonly ActivitySequenceScenePrimitivePort owner;
            public AmbientBarkPresenter(ActivitySequenceScenePrimitivePort port) { owner = port; }

            public bool TryPresent(ActivityPrimitiveRequest request, Action<ActivityPrimitiveIdentity, bool> acknowledgement)
            {
                RuntimeContentStore runtime = RuntimeContentStore.Current;
                string dialogueId = Text(request, "dialogueId");
                if (owner.ambientBubble == null || runtime == null || string.IsNullOrWhiteSpace(dialogueId)) return false;
                ContentReleaseDependency[] dialoguePins = runtime.Release.dependencies.Where(item => item != null &&
                    item.kind == "dialogue" && item.id == dialogueId && item.store == runtime.Repository.StoreId).ToArray();
                if (dialoguePins.Length != 1) return false;
                ContentReleaseDependency dialoguePin = dialoguePins[0];
                ContentResult<DialogueRecord> dialogue = runtime.ReadDialogue(new ContentRef(dialoguePin.store,
                    dialoguePin.id, dialoguePin.revision, dialoguePin.expectedHash));
                if (!dialogue.IsReady || dialogue.value.beats == null || dialogue.value.beats.Count != 1) return false;
                BeatRecord beat = dialogue.value.beats[0];
                if (beat == null || beat.textRef == null) return false;
                ContentReleaseDependency[] textPins = runtime.Release.dependencies.Where(item => item != null &&
                    item.kind == "text" && item.id == beat.textRef.id && item.revision == beat.textRef.revision &&
                    item.store == beat.textRef.store && (string.IsNullOrEmpty(beat.textRef.expectedHash) ||
                    item.expectedHash == beat.textRef.expectedHash)).ToArray();
                if (textPins.Length != 1) return false;
                ContentReleaseDependency textPin = textPins[0];
                ContentResult<ContentTextResolution> resolved = ContentLanguageResolver.Resolve(runtime.Repository,
                    new ContentRef(textPin.store, textPin.id, textPin.revision, textPin.expectedHash), "en", "ja");
                if (!resolved.IsReady) return false;
                string spoken = string.IsNullOrWhiteSpace(resolved.value.speechText)
                    ? resolved.value.displayText : resolved.value.speechText;
                string language = string.IsNullOrWhiteSpace(resolved.value.speechText)
                    ? resolved.value.displayLanguage : resolved.value.speechLanguage;
                ActivityPrimitiveIdentity identity = request.identity;
                return owner.ambientBubble.TrySayWithAcknowledgement(identity.invocationId, spoken,
                    resolved.value.displayText, language, visible => acknowledgement(identity, visible));
            }

            public bool Cancel(ActivityPrimitiveIdentity identity)
            { return owner.ambientBubble != null && identity != null && owner.ambientBubble.CancelAcknowledgedSay(identity.invocationId); }
        }

        private sealed class CompleteOperation : IActivitySequencePrimitiveOperation
        {
            public string PrimitiveId { get { return ActivityPrimitiveIds.Complete; } }
            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request) { return Completed(request); }
            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request) { return Cancelled(request); }
        }
    }
}
