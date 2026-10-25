using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Furniture;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Uses the production sequence executor with detached fixture-owned movement, bark, and Furniture ports.</summary>
    internal static class WorkbenchFixtureActivitySequenceRunner
    {
        public static bool TryRun(ActivitySequenceRequest request, ContentPackage package, ContentRelease release,
            TrustedFixtureContext fixture, out ActivitySequenceView view, out string diagnostic,
            out string[] executed, out IList<object> trace)
        {
            view = null; diagnostic = string.Empty; executed = new string[0]; trace = new List<object>();
            if (request == null || request.definition == null || package == null || release == null || fixture == null)
            { diagnostic = "sequence_request_missing"; return false; }

            var port = new FixturePrimitivePort(request, package, release, fixture);
            var executor = new ActivitySequenceExecutor(ActivitySequenceCatalogFactory.CreateDefault().Primitives, port);
            ActivitySequenceStartResult started = executor.Start(request);
            view = executor.Tick(0d);
            int ticks = 0;
            while (view.status != ActivitySequenceStatuses.Completed && view.status != ActivitySequenceStatuses.Failed &&
                view.status != ActivitySequenceStatuses.Cancelled && ticks++ < ActivitySequenceValidator.MaximumStepCount)
                view = executor.Tick(0d);
            port.ReleaseSequence(request.actionInvocationId);
            executed = port.Executed.ToArray();
            trace = port.Trace.AsReadOnly();
            if (started.status != ActivitySequenceStatuses.Accepted || view.status != ActivitySequenceStatuses.Completed)
            {
                diagnostic = string.IsNullOrWhiteSpace(view.reasonCode) ? "sequence_fixture_execution_failed" : view.reasonCode;
                return false;
            }
            return true;
        }

        private sealed class FixturePrimitivePort : IActivityPrimitivePort
        {
            private readonly ActivitySequenceRequest sequence;
            private readonly ContentPackage package;
            private readonly ContentRelease release;
            private readonly TrustedFixtureContext fixture;
            private readonly Dictionary<string, WorkbenchPreviewFurnitureInstance> reservations = new Dictionary<string, WorkbenchPreviewFurnitureInstance>(StringComparer.Ordinal);
            private readonly Dictionary<string, FurnitureFunctionResult> powerResults = new Dictionary<string, FurnitureFunctionResult>(StringComparer.Ordinal);
            private readonly Dictionary<string, FurniturePresentationHandle> activePresentations = new Dictionary<string, FurniturePresentationHandle>(StringComparer.Ordinal);
            private FurnitureCatalogueSnapshot catalogue;
            private FurnitureFunctionTargetValidator functionTargets;
            private FurniturePresentationTargetValidator presentationTargets;
            private FurnitureFunctionRegistry functions;
            private FurniturePresentationRegistry presentations;
            private PowerStore powerStore;

            public readonly List<string> Executed = new List<string>();
            public readonly List<object> Trace = new List<object>();

            public FixturePrimitivePort(ActivitySequenceRequest request, ContentPackage selectedPackage,
                ContentRelease selectedRelease, TrustedFixtureContext trustedFixture)
            { sequence = request; package = selectedPackage; release = selectedRelease; fixture = trustedFixture; }

            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            {
                if (request == null || request.identity == null) return Failed(request, "primitive_request_invalid");
                Executed.Add(request.primitiveId);
                switch (request.primitiveId)
                {
                    case ActivityPrimitiveIds.ReserveNearest: return ReserveNearest(request);
                    case ActivityPrimitiveIds.MoveTo: return MoveTo(request);
                    case ActivityPrimitiveIds.Face: return Face(request);
                    case ActivityPrimitiveIds.Bark: return Bark(request);
                    case ActivityPrimitiveIds.Wait: return Wait(request);
                    case ActivityPrimitiveIds.InvokeFurniture: return InvokeFurniture(request);
                    case ActivityPrimitiveIds.MoveNearPlayer: return MoveNearPlayer(request);
                    case ActivityPrimitiveIds.Checkpoint: return Checkpoint(request);
                    case ActivityPrimitiveIds.Complete: return Complete(request);
                    default: return Failed(request, "fixture_primitive_not_allowed:" + request.primitiveId);
                }
            }

            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
            {
                if (request == null || request.identity == null)
                    return new ActivityPrimitiveCompletion(null, ActivitySequenceStatuses.Failed,
                        new ActivitySequenceArgument[0], "primitive_cancel_invalid");
                ReleaseReservation(request.identity.actionInvocationId);
                if (activePresentations.TryGetValue(request.identity.actionInvocationId, out FurniturePresentationHandle activeHandle))
                {
                    if (presentations != null) presentations.Cancel(activeHandle);
                    activePresentations.Remove(request.identity.actionInvocationId);
                }
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "cancel" }, { "reason", request.reasonCode }, { "syntheticOnly", true } });
                return new ActivityPrimitiveCompletion(request.identity, ActivitySequenceStatuses.Cancelled,
                    new ActivitySequenceArgument[0], "synthetic_fixture_cancelled");
            }

            private ActivityPrimitiveStartResult ReserveNearest(ActivityPrimitiveRequest request)
            {
                if (!TryGetFurnitureRuntime(out string error)) return Failed(request, error);
                string useId = StringArgument(request, "useId");
                string functionId = EnumArgument(request, "functionId");
                if (string.IsNullOrWhiteSpace(useId) || string.IsNullOrWhiteSpace(functionId)) return Failed(request, "reserve_arguments_invalid");

                foreach (WorkbenchPreviewFurnitureInstance instance in fixture.PreviewFurniture
                    .Where(item => item.available && !item.occupied && item.useId == useId &&
                        (string.IsNullOrEmpty(item.reservationOwnerId) || item.reservationOwnerId == request.identity.actionInvocationId))
                    .OrderBy(item => item.distanceToPlayer)
                    .ThenBy(item => item.instanceId, StringComparer.Ordinal)
                    .ThenBy(item => item.usePointId, StringComparer.Ordinal))
                {
                    if (!SupportsBinding(instance.definitionId, instance.usePointId, useId, functionId)) continue;
                    string priorOwner = instance.reservationOwnerId;
                    instance.reservationOwnerId = request.identity.actionInvocationId;
                    if (!ValidateExactTarget(request, instance, useId, functionId, out string validation))
                    { instance.reservationOwnerId = priorOwner; continue; }
                    reservations[request.identity.actionInvocationId] = instance;
                    Trace.Add(new Dictionary<string, object>
                    {
                        { "operation", "reserve_nearest" }, { "status", "reserved" },
                        { "instanceId", instance.instanceId }, { "definitionId", instance.definitionId },
                        { "usePointId", instance.usePointId }, { "useId", useId }, { "functionId", functionId },
                        { "distanceToPlayer", instance.distanceToPlayer },
                        { "syntheticOnly", true }
                    });
                    return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("targetToken",
                        FlowValue.Entity(ActivitySequenceEntityKinds.ReservedTarget, instance.instanceId)) }, "fixture_target_reserved");
                }
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "reserve_nearest" }, { "status", "unavailable" }, { "useId", useId },
                      { "functionId", functionId }, { "reason", "no_exact_fixture_target" }, { "syntheticOnly", true } });
                return Failed(request, "furniture_target_unavailable");
            }

            private ActivityPrimitiveStartResult MoveTo(ActivityPrimitiveRequest request)
            {
                WorkbenchPreviewFurnitureInstance target = Target(request);
                if (target == null) return Failed(request, "reserved_target_unavailable");
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "move_to" }, { "instanceId", target.instanceId }, { "arrived", true }, { "syntheticOnly", true } });
                return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("arrived", FlowValue.Boolean(true)) },
                    "synthetic_arrival");
            }

            private ActivityPrimitiveStartResult Face(ActivityPrimitiveRequest request)
            {
                WorkbenchPreviewFurnitureInstance target = Target(request);
                if (target == null) return Failed(request, "reserved_target_unavailable");
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "face" }, { "instanceId", target.instanceId }, { "syntheticOnly", true } });
                return Completed(request, new ActivitySequenceArgument[0], "synthetic_facing");
            }

            private ActivityPrimitiveStartResult Bark(ActivityPrimitiveRequest request)
            {
                string dialogueId = StringArgument(request, "dialogueId");
                if (!TryResolveDialogue(dialogueId, out List<object> dialogueBeats)) return Failed(request, "bark_dialogue_not_in_release");
                var presenter = new FixtureBarkPresenter(fixture, Trace, dialogueId, dialogueBeats);
                var operation = new ActivitySequenceBarkOperation(presenter);
                ActivityPrimitiveStartResult started = operation.Start(request);
                if (started == null || started.status != ActivitySequenceStatuses.Running) return started ?? Failed(request, "bark_start_failed");
                ActivityPrimitiveCompletion completion = operation.Poll(request.identity, 0d);
                if (completion == null) return Failed(request, "bark_acknowledgement_pending");
                return completion.status == ActivitySequenceStatuses.Completed
                    ? Completed(request, completion.outputs, completion.reasonCode)
                    : Failed(request, completion.reasonCode);
            }

            private ActivityPrimitiveStartResult Wait(ActivityPrimitiveRequest request)
            {
                FlowValue duration = Literal(request, "durationSeconds");
                if (duration == null || duration.type != "double") return Failed(request, "wait_duration_invalid");
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "wait" }, { "durationSeconds", duration.doubleValue }, { "syntheticOnly", true } });
                return Completed(request, new ActivitySequenceArgument[0], "synthetic_wait_elapsed");
            }

            private ActivityPrimitiveStartResult InvokeFurniture(ActivityPrimitiveRequest request)
            {
                if (!TryGetFurnitureRuntime(out string error)) return Failed(request, error);
                WorkbenchPreviewFurnitureInstance target = Target(request);
                if (target == null) return Failed(request, "reserved_target_unavailable");
                string functionId = EnumArgument(request, "functionId");
                if (functionId == FurnitureFunctionIds.PowerSet)
                    return InvokePower(request, target);
                if (functionId == FurniturePresentationDescriptorIds.DispenserCollectMock)
                    return InvokePresentation(request, target);
                return Failed(request, "furniture_function_not_fixture_safe");
            }

            private ActivityPrimitiveStartResult InvokePower(ActivityPrimitiveRequest request, WorkbenchPreviewFurnitureInstance target)
            {
                string desired = EnumArgument(request, "desiredState");
                FurniturePowerState desiredState;
                if (!Enum.TryParse(desired, false, out desiredState)) return Failed(request, "power_state_invalid");
                var furnitureRequest = CreateFunctionRequest(request, target,
                    new FurniturePowerArguments { desiredState = desiredState });
                FurnitureFunctionResult result = functions.Invoke(furnitureRequest);
                if (result == null || result.status != FurnitureFunctionStatuses.Applied &&
                    result.status != FurnitureFunctionStatuses.NoChange && result.status != FurnitureFunctionStatuses.Replayed)
                {
                    Trace.Add(new Dictionary<string, object>
                        { { "operation", "power.set" }, { "instanceId", target.instanceId },
                          { "status", result == null ? "failed" : result.status },
                          { "reason", result == null ? "function_result_missing" : result.diagnosticCode }, { "syntheticOnly", true } });
                    return Failed(request, result == null ? "power_function_result_missing" : result.diagnosticCode);
                }
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "power.set" }, { "instanceId", target.instanceId },
                      { "status", result.status }, { "powerState", desiredState.ToString() },
                      { "instanceRevision", target.instanceRevision }, { "gameRevision", fixture.PreviewGameRevision },
                      { "syntheticOnly", true } });
                return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("functionStatus", FlowValue.Enum(result.status)) },
                    result.diagnosticCode);
            }

            private ActivityPrimitiveStartResult InvokePresentation(ActivityPrimitiveRequest request, WorkbenchPreviewFurnitureInstance target)
            {
                string requestId = request.identity.invocationId;
                var handle = new FurniturePresentationHandle
                {
                    presentationRequestId = requestId,
                    presentationDescriptorId = FurniturePresentationDescriptorIds.DispenserCollectMock,
                    runId = request.identity.scenarioRunId,
                    invocationId = request.identity.invocationId,
                    actionDefinitionRef = request.actionDefinitionRef,
                    activityId = request.identity.actionInvocationId,
                    sequenceStepId = request.identity.sequenceStepId,
                    instanceId = target.instanceId,
                    definitionId = target.definitionId,
                    usePointId = target.usePointId,
                    usePointRevision = target.usePointRevision
                };
                var presentationRequest = new FurniturePresentationRequest
                {
                    presentationRequestId = requestId,
                    presentationDescriptorId = FurniturePresentationDescriptorIds.DispenserCollectMock,
                    runId = request.identity.scenarioRunId,
                    invocationId = request.identity.invocationId,
                    actionDefinitionRef = request.actionDefinitionRef,
                    activityId = request.identity.actionInvocationId,
                    sequenceStepId = request.identity.sequenceStepId,
                    instanceId = target.instanceId,
                    definitionId = target.definitionId,
                    usePointId = target.usePointId,
                    usePointRevision = target.usePointRevision
                };
                FurniturePresentationResult result = presentations.Start(presentationRequest);
                if (result != null && result.handle != null && result.status != FurniturePresentationStatuses.Failed)
                    activePresentations[request.identity.actionInvocationId] = result.handle;
                int polls = 0;
                while (result != null && result.status != FurniturePresentationStatuses.Completed &&
                    result.status != FurniturePresentationStatuses.Failed && result.status != FurniturePresentationStatuses.Cancelled && polls++ < 4)
                    result = presentations.Poll(result.handle ?? handle);
                if (result == null || result.status != FurniturePresentationStatuses.Completed)
                {
                    activePresentations.Remove(request.identity.actionInvocationId);
                    Trace.Add(new Dictionary<string, object>
                        { { "operation", "dispenser.collect_mock" }, { "instanceId", target.instanceId },
                          { "status", result == null ? "failed" : result.status },
                          { "reason", result == null ? "presentation_result_missing" : result.diagnosticCode },
                          { "pollCount", polls }, { "syntheticOnly", true } });
                    return Failed(request, result == null ? "presentation_result_missing" : result.diagnosticCode);
                }
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "dispenser.collect_mock" }, { "instanceId", target.instanceId },
                      { "status", result.status }, { "pollCount", polls }, { "syntheticOnly", true },
                      { "itemOrStatMutation", false } });
                activePresentations.Remove(request.identity.actionInvocationId);
                return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("functionStatus", FlowValue.Enum("completed")) },
                    "synthetic_presentation_completed");
            }

            private ActivityPrimitiveStartResult MoveNearPlayer(ActivityPrimitiveRequest request)
            {
                if (!fixture.PreviewReturnAvailable) return Failed(request, "fixture_return_unavailable");
                WorkbenchPreviewFurnitureInstance target = reservations.TryGetValue(request.identity.actionInvocationId, out WorkbenchPreviewFurnitureInstance found)
                    ? found : null;
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "move_near_player" }, { "arrivedNearPlayer", true },
                      { "reservedTargetId", target == null ? "" : target.instanceId }, { "syntheticOnly", true } });
                return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("arrived", FlowValue.Boolean(true)) },
                    "synthetic_return_arrival");
            }

            private ActivityPrimitiveStartResult Checkpoint(ActivityPrimitiveRequest request)
            {
                string checkpoint = StringArgument(request, "checkpointId");
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "checkpoint" }, { "checkpointId", checkpoint }, { "recorded", true }, { "syntheticOnly", true } });
                return Completed(request, new[] { ActivitySequenceArgument.FromLiteral("recorded", FlowValue.Boolean(true)) },
                    "synthetic_checkpoint");
            }

            private ActivityPrimitiveStartResult Complete(ActivityPrimitiveRequest request)
            {
                ReleaseReservation(request.identity.actionInvocationId);
                Trace.Add(new Dictionary<string, object>
                    { { "operation", "complete" }, { "syntheticOnly", true } });
                return Completed(request, new ActivitySequenceArgument[0], "synthetic_sequence_completed");
            }

            private bool TryGetFurnitureRuntime(out string error)
            {
                error = string.Empty;
                if (catalogue != null) return true;
                if (!WorkbenchFurniturePreview.TryCreateCatalogue(package, out catalogue, out error)) return false;
                var scene = new WorkbenchFurniturePreview.FixtureSceneResolver(fixture);
                var reservation = new WorkbenchFurniturePreview.FixtureReservationSource(fixture);
                functionTargets = new FurnitureFunctionTargetValidator(catalogue, scene, reservation);
                presentationTargets = new FurniturePresentationTargetValidator(catalogue, scene, reservation);
                functions = new FurnitureFunctionRegistry(functionTargets);
                powerStore = new PowerStore(fixture, powerResults);
                if (!functions.Register(new FurniturePowerFunction(powerStore)))
                { error = "preview_power_function_registration_failed"; return false; }
                presentations = new FurniturePresentationRegistry(presentationTargets);
                if (!presentations.Register(new DispenserCollectMockPresentation()))
                { error = "preview_presentation_registration_failed"; return false; }
                return true;
            }

            private bool SupportsBinding(string definitionId, string pointId, string useId, string functionId)
            {
                FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item != null && item.definitionId == definitionId);
                if (definition == null || !(definition.points ?? new List<FurnitureFunctionalPoint>()).Any(point =>
                    point != null && point.pointId == pointId && point.useIds != null && point.useIds.Contains(useId, StringComparer.Ordinal)))
                    return false;
                if (functionId == FurnitureFunctionIds.PowerSet)
                    return (definition.functionBindings ?? new List<FurnitureFunctionBindingDescriptor>()).Any(binding =>
                        binding != null && binding.functionId == functionId && binding.pointId == pointId && binding.useId == useId);
                if (functionId == FurniturePresentationDescriptorIds.DispenserCollectMock)
                    return (definition.presentationBindings ?? new List<FurniturePresentationBindingDescriptor>()).Any(binding =>
                        binding != null && binding.descriptorId == functionId && binding.pointId == pointId && binding.useId == useId);
                return false;
            }

            private bool ValidateExactTarget(ActivityPrimitiveRequest request, WorkbenchPreviewFurnitureInstance instance,
                string useId, string functionId, out string diagnostic)
            {
                diagnostic = string.Empty;
                if (functionId == FurnitureFunctionIds.PowerSet)
                {
                    FurnitureFunctionTargetValidation validation = functionTargets.Validate(CreateFunctionRequest(request,
                        instance, new FurniturePowerArguments { desiredState = instance.powerKnown && instance.powerState == "On"
                            ? FurniturePowerState.On : FurniturePowerState.Off }));
                    diagnostic = validation == null ? "furniture_target_validation_missing" : validation.diagnosticCode;
                    return validation != null && validation.status == FurnitureFunctionTargetStatuses.Available;
                }
                FurniturePresentationValidationResult presentation = presentationTargets.Validate(new FurniturePresentationHandle
                {
                    presentationRequestId = "preview-reserve-check", presentationDescriptorId = functionId,
                    runId = sequence.scenarioRunId, invocationId = sequence.flowInvocationId,
                    actionDefinitionRef = sequence.actionDefinitionRef, activityId = request.identity.actionInvocationId,
                    sequenceStepId = request.identity.sequenceStepId, instanceId = instance.instanceId,
                    definitionId = instance.definitionId, usePointId = instance.usePointId,
                    usePointRevision = instance.usePointRevision
                }, FurniturePresentationValidationPurpose.Start);
                diagnostic = presentation == null ? "presentation_target_validation_missing" : presentation.diagnosticCode;
                return presentation != null && presentation.allowed && useId == instance.useId;
            }

            private FurnitureFunctionRequest CreateFunctionRequest(ActivityPrimitiveRequest request,
                WorkbenchPreviewFurnitureInstance target, FurnitureFunctionArguments arguments)
            {
                return new FurnitureFunctionRequest
                {
                    functionId = FurnitureFunctionIds.PowerSet, instanceId = target.instanceId,
                    definitionId = target.definitionId, usePointId = target.usePointId, useId = target.useId,
                    usePointRevision = target.usePointRevision, reservationOwnerId = target.reservationOwnerId,
                    expectedInstanceRevision = target.instanceRevision, expectedLayoutRevision = fixture.PreviewLayoutRevision,
                    expectedGameRevision = fixture.PreviewGameRevision, runId = request.identity.scenarioRunId,
                    invocationId = request.identity.invocationId, stepExecutionId = request.identity.stepExecutionId,
                    activityId = request.identity.actionInvocationId, sequenceStepId = request.identity.sequenceStepId,
                    operationId = request.identity.invocationId, arguments = arguments
                };
            }

            private WorkbenchPreviewFurnitureInstance Target(ActivityPrimitiveRequest request)
            {
                if (!reservations.TryGetValue(request.identity.actionInvocationId, out WorkbenchPreviewFurnitureInstance target)) return null;
                ActivitySequenceArgument targetArgument = request.arguments.FirstOrDefault(item => item != null && item.name == "target");
                FlowEntityReference entity = targetArgument == null || targetArgument.value == null || targetArgument.value.literal == null
                    ? null : targetArgument.value.literal.entityValue;
                return entity != null && entity.kind == ActivitySequenceEntityKinds.ReservedTarget && entity.id == target.instanceId &&
                    target.reservationOwnerId == request.identity.actionInvocationId ? target : null;
            }

            private bool TryResolveDialogue(string dialogueId, out List<object> beats)
            {
                beats = new List<object>();
                DialogueRecord dialogue = (package.dialogues ?? new List<DialogueRecord>()).FirstOrDefault(item => item != null && item.id == dialogueId);
                if (dialogue == null || !release.dependencies.Any(dependency => dependency != null && dependency.kind == "dialogue" &&
                    dependency.store == package.storeId && dependency.id == dialogue.id && dependency.revision == dialogue.revision &&
                    dependency.expectedHash == dialogue.semanticHash)) return false;
                foreach (BeatRecord beat in dialogue.beats ?? new List<BeatRecord>())
                {
                    TextRecord text = (package.texts ?? new List<TextRecord>()).FirstOrDefault(item => item != null &&
                        beat != null && beat.textRef != null && item.id == beat.textRef.id && item.revision == beat.textRef.revision &&
                        item.semanticHash == beat.textRef.expectedHash);
                    if (text == null || !release.dependencies.Any(dependency => dependency != null && dependency.kind == "text" &&
                        dependency.store == package.storeId && dependency.id == text.id && dependency.revision == text.revision &&
                        dependency.expectedHash == text.semanticHash)) return false;
                    ContentResult<ContentTextResolution> localized = ContentLanguageResolver.Resolve(text, "en", "ja");
                    if (!localized.IsReady) return false;
                    beats.Add(new Dictionary<string, object>
                        { { "beatId", beat.id }, { "displayText", localized.value.displayText },
                          { "speechText", localized.value.speechText }, { "expression", beat.expression } });
                }
                return beats.Count > 0;
            }

            private string StringArgument(ActivityPrimitiveRequest request, string name)
            { FlowValue value = Literal(request, name); return value == null ? string.Empty : value.textValue; }
            private string EnumArgument(ActivityPrimitiveRequest request, string name)
            { FlowValue value = Literal(request, name); return value == null ? string.Empty : value.textValue; }
            private FlowValue Literal(ActivityPrimitiveRequest request, string name)
            {
                ActivitySequenceArgument value = request.arguments.FirstOrDefault(item => item != null && item.name == name);
                return value == null || value.value == null ? null : value.value.literal;
            }

            private ActivityPrimitiveStartResult Completed(ActivityPrimitiveRequest request,
                IEnumerable<ActivitySequenceArgument> outputs, string reason)
            { return new ActivityPrimitiveStartResult(request.identity, ActivitySequenceStatuses.Completed, outputs, reason); }
            private ActivityPrimitiveStartResult Failed(ActivityPrimitiveRequest request, string reason)
            { return new ActivityPrimitiveStartResult(request == null ? null : request.identity,
                ActivitySequenceStatuses.Failed, new ActivitySequenceArgument[0], reason); }

            private void ReleaseReservation(string activityId)
            {
                if (!reservations.TryGetValue(activityId, out WorkbenchPreviewFurnitureInstance instance)) return;
                if (instance.reservationOwnerId == activityId) instance.reservationOwnerId = string.Empty;
                reservations.Remove(activityId);
            }

            public void ReleaseSequence(string activityId)
            {
                if (activePresentations.TryGetValue(activityId, out FurniturePresentationHandle activeHandle) && presentations != null)
                    presentations.Cancel(activeHandle);
                activePresentations.Remove(activityId);
                ReleaseReservation(activityId);
            }

            private sealed class FixtureBarkPresenter : IActivitySequenceBarkPresenter
            {
                private readonly TrustedFixtureContext fixture;
                private readonly IList<object> trace;
                private readonly string dialogueId;
                private readonly List<object> beats;
                public FixtureBarkPresenter(TrustedFixtureContext trustedFixture, IList<object> operationTrace,
                    string id, List<object> exactBeats)
                { fixture = trustedFixture; trace = operationTrace; dialogueId = id; beats = exactBeats; }

                public bool TryPresent(ActivityPrimitiveRequest request, Action<ActivityPrimitiveIdentity, bool> acknowledgement)
                {
                    if (!fixture.PreviewBarkVisible) return false;
                    trace.Add(new Dictionary<string, object>
                        { { "operation", "bark" }, { "dialogueId", dialogueId }, { "beats", beats },
                          { "visible", true }, { "syntheticAcknowledgement", true } });
                    acknowledgement(request.identity, true);
                    return true;
                }

                public bool Cancel(ActivityPrimitiveIdentity identity)
                {
                    trace.Add(new Dictionary<string, object>
                        { { "operation", "bark_cancel" }, { "dialogueId", dialogueId }, { "syntheticOnly", true } });
                    return true;
                }
            }

            private sealed class PowerStore : IFurniturePowerStore
            {
                private readonly TrustedFixtureContext fixture;
                private readonly IDictionary<string, FurnitureFunctionResult> operations;
                public PowerStore(TrustedFixtureContext trustedFixture, IDictionary<string, FurnitureFunctionResult> receipts)
                { fixture = trustedFixture; operations = receipts; }

                public FurnitureFunctionResult SetPower(FurniturePowerCommand command)
                {
                    if (command == null) return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Failed, "", "invalid_power_command");
                    if (operations.TryGetValue(command.operationId, out FurnitureFunctionResult prior)) return prior;
                    WorkbenchPreviewFurnitureInstance instance = fixture.PreviewFurniture.FirstOrDefault(item => item.instanceId == command.instanceId);
                    if (instance == null || instance.definitionId != command.definitionId || instance.usePointId != command.usePointId ||
                        instance.useId != command.useId || instance.usePointRevision != command.usePointRevision ||
                        instance.reservationOwnerId != command.activityId || command.expectedInstanceRevision != instance.instanceRevision ||
                        command.expectedLayoutRevision != fixture.PreviewLayoutRevision || command.expectedGameRevision != fixture.PreviewGameRevision)
                        return FurnitureFunctionRegistry.Failure(FurnitureFunctionStatuses.Conflict, command.operationId, "fixture_power_revision_or_reservation_conflict");
                    bool isNoChange = instance.powerKnown &&
                        (command.desiredState == FurniturePowerState.On ? instance.powerState == "On" : instance.powerState == "Off");
                    if (!isNoChange)
                    {
                        instance.powerKnown = true;
                        instance.powerState = command.desiredState == FurniturePowerState.On ? "On" : "Off";
                        instance.instanceRevision++;
                        fixture.PreviewGameRevision++;
                    }
                    var evidence = new FurnitureFunctionStateEvidence
                    {
                        resourceId = FurniturePowerResourceIds.DevicePower,
                        instanceId = instance.instanceId, definitionId = instance.definitionId,
                        powerKnown = true, powerState = command.desiredState,
                        instanceRevision = instance.instanceRevision,
                        layoutRevision = fixture.PreviewLayoutRevision, gameRevision = fixture.PreviewGameRevision
                    };
                    var result = new FurnitureFunctionResult
                    {
                        status = isNoChange ? FurnitureFunctionStatuses.NoChange : FurnitureFunctionStatuses.Applied,
                        operationId = command.operationId, committedState = evidence,
                        diagnosticCode = "synthetic_fixture_power_change"
                    };
                    operations[command.operationId] = result;
                    return result;
                }
            }
        }
    }
}
