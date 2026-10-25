using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureFunctionTests
    {
        [Test]
        public void PowerSetSendsTheExactTypedCommandAndCommittedEvidence()
        {
            var store = new FakePowerStore();
            var registry = CreateRegistry(store, new FakeTargetValidator());
            FurnitureFunctionRequest request = PowerRequest("tv.a", "power.a", FurniturePowerState.On);

            FurnitureFunctionResult result = registry.Invoke(request);

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, result.status);
            Assert.AreEqual(request.instanceId, store.lastCommand.instanceId);
            Assert.AreEqual(request.definitionId, store.lastCommand.definitionId);
            Assert.AreEqual(request.usePointId, store.lastCommand.usePointId);
            Assert.AreEqual(request.useId, store.lastCommand.useId);
            Assert.AreEqual(request.usePointRevision, store.lastCommand.usePointRevision);
            Assert.AreEqual(request.expectedInstanceRevision, store.lastCommand.expectedInstanceRevision);
            Assert.AreEqual(request.expectedLayoutRevision, store.lastCommand.expectedLayoutRevision);
            Assert.AreEqual(request.expectedGameRevision, store.lastCommand.expectedGameRevision);
            Assert.AreEqual(request.runId, store.lastCommand.runId);
            Assert.AreEqual(request.invocationId, store.lastCommand.invocationId);
            Assert.AreEqual(request.stepExecutionId, store.lastCommand.stepExecutionId);
            Assert.AreEqual(request.activityId, store.lastCommand.activityId);
            Assert.AreEqual(request.sequenceStepId, store.lastCommand.sequenceStepId);
            Assert.AreEqual(request.operationId, store.lastCommand.operationId);
            Assert.AreEqual(FurniturePowerState.On, store.lastCommand.desiredState);
            Assert.AreEqual("tv.a", result.committedState.instanceId);
            Assert.IsTrue(result.committedState.powerKnown);
            Assert.AreEqual(FurniturePowerState.On, result.committedState.powerState);
        }

        [Test]
        public void MissingFlowStepExecutionIdentityFailsBeforePowerStore()
        {
            var store = new FakePowerStore();
            FurnitureFunctionRequest request = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            request.stepExecutionId = string.Empty;

            FurnitureFunctionResult result = CreateRegistry(store, new FakeTargetValidator()).Invoke(request);

            Assert.AreEqual(FurnitureFunctionStatuses.Failed, result.status);
            Assert.AreEqual("invalid_furniture_function_request", result.diagnosticCode);
            Assert.AreEqual(0, store.callCount);
        }

        [Test]
        public void UnregisteredFunctionCannotReachPowerStore()
        {
            var store = new FakePowerStore();
            var registry = CreateRegistry(store, new FakeTargetValidator());
            FurnitureFunctionRequest request = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            request.functionId = "dispenser.collect_mock";

            FurnitureFunctionResult result = registry.Invoke(request);

            Assert.AreEqual(FurnitureFunctionStatuses.Unavailable, result.status);
            Assert.AreEqual(0, store.callCount);
        }

        [Test]
        public void PresentationDescriptorCannotBeRegisteredAsDurableFunction()
        {
            var registry = new FurnitureFunctionRegistry(new FakeTargetValidator());

            bool registered = registry.Register(new PresentationAsFunctionHandler());

            Assert.IsFalse(registered);
        }

        [Test]
        public void StaleBindingCannotReachPowerStore()
        {
            var store = new FakePowerStore();
            var validator = new FakeTargetValidator
            {
                result = new FurnitureFunctionTargetValidation
                {
                    status = FurnitureFunctionTargetStatuses.Conflict,
                    diagnosticCode = "stale_furniture_binding"
                }
            };

            FurnitureFunctionResult result = CreateRegistry(store, validator)
                .Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, result.status);
            Assert.AreEqual("stale_furniture_binding", result.diagnosticCode);
            Assert.AreEqual(0, store.callCount);
        }

        [Test]
        public void UnsupportedDefinitionReturnsUnavailableBeforePowerStore()
        {
            var store = new FakePowerStore();
            var validator = new FakeTargetValidator
            {
                result = new FurnitureFunctionTargetValidation
                {
                    status = FurnitureFunctionTargetStatuses.Unavailable,
                    diagnosticCode = "furniture_function_not_supported"
                }
            };

            FurnitureFunctionResult result = CreateRegistry(store, validator)
                .Invoke(PowerRequest("chair.a", "chair.use", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.Unavailable, result.status);
            Assert.AreEqual("furniture_function_not_supported", result.diagnosticCode);
            Assert.AreEqual(0, store.callCount);
        }

        [Test]
        public void ExplicitOffFromUnknownStateCommitsKnownOff()
        {
            var store = new FakePowerStore();
            FurnitureFunctionResult result = CreateRegistry(store, new FakeTargetValidator())
                .Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.Off));

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, result.status);
            Assert.IsTrue(result.committedState.powerKnown);
            Assert.AreEqual(FurniturePowerState.Off, result.committedState.powerState);
            Assert.AreEqual(1, store.stateWriteCount);
            Assert.AreEqual(1, store.powerChangeEventCount);
        }

        [Test]
        public void AlreadyDesiredStateReturnsNoChangeWithoutWritingAgain()
        {
            var store = new FakePowerStore();
            store.SeedPower("tv.a", FurniturePowerState.On);

            FurnitureFunctionResult result = CreateRegistry(store, new FakeTargetValidator())
                .Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.NoChange, result.status);
            Assert.AreEqual(FurniturePowerState.On, result.committedState.powerState);
            Assert.AreEqual(0, store.stateWriteCount);
            Assert.AreEqual(0, store.powerChangeEventCount);
        }

        [Test]
        public void SameRegisteredPowerHandlerSupportsAnotherCompatibleDefinition()
        {
            var store = new FakePowerStore();
            var registry = CreateRegistry(store, new FakeTargetValidator());
            FurnitureFunctionRequest first = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            FurnitureFunctionRequest second = PowerRequest("tv.b", "power.b", FurniturePowerState.On);
            second.definitionId = "furniture.tv.synthetic";
            second.activityId = "activity.b";
            second.reservationOwnerId = "activity.b";
            second.runId = "run.b";
            second.invocationId = "flow.b";
            second.operationId = "operation.b";

            FurnitureFunctionResult firstResult = registry.Invoke(first);
            FurnitureFunctionResult secondResult = registry.Invoke(second);

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, firstResult.status);
            Assert.AreEqual(FurnitureFunctionStatuses.Applied, secondResult.status);
            Assert.AreEqual("tv.a", firstResult.committedState.instanceId);
            Assert.AreEqual("tv.b", secondResult.committedState.instanceId);
            Assert.AreEqual(2, store.stateWriteCount);
        }

        [Test]
        public void ReservationOwnerMismatchConflictsBeforeTargetValidation()
        {
            var validator = new FakeTargetValidator();
            var store = new FakePowerStore();
            FurnitureFunctionRequest request = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            request.reservationOwnerId = "activity.other";

            FurnitureFunctionResult result = CreateRegistry(store, validator).Invoke(request);

            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, result.status);
            Assert.AreEqual("furniture_reservation_owner_mismatch", result.diagnosticCode);
            Assert.AreEqual(0, validator.callCount);
            Assert.AreEqual(0, store.callCount);
        }

        [Test]
        public void SameOperationReplaysAndChangedPayloadConflictsWithoutSecondWrite()
        {
            var store = new FakePowerStore();
            var registry = CreateRegistry(store, new FakeTargetValidator());

            FurnitureFunctionResult first = registry.Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.On));
            FurnitureFunctionResult replay = registry.Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.On));
            FurnitureFunctionResult conflict = registry.Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.Off));

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, first.status);
            Assert.AreEqual(FurnitureFunctionStatuses.Replayed, replay.status);
            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, conflict.status);
            Assert.AreEqual(1, store.stateWriteCount);
            Assert.AreEqual(1, store.powerChangeEventCount);
        }

        [Test]
        public void SameOperationWithDifferentFlowStepExecutionConflictsWithoutSecondWrite()
        {
            var store = new FakePowerStore();
            var registry = CreateRegistry(store, new FakeTargetValidator());
            FurnitureFunctionRequest firstRequest = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            FurnitureFunctionRequest changedRequest = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            changedRequest.stepExecutionId = "flow-step.other";

            FurnitureFunctionResult first = registry.Invoke(firstRequest);
            FurnitureFunctionResult conflict = registry.Invoke(changedRequest);

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, first.status);
            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, conflict.status);
            Assert.AreEqual(1, store.stateWriteCount);
            Assert.AreEqual(1, store.powerChangeEventCount);
        }

        [Test]
        public void InvalidArgumentsOrCommittedEvidenceFailClosed()
        {
            var store = new FakePowerStore();
            var registry = CreateRegistry(store, new FakeTargetValidator());
            FurnitureFunctionRequest request = PowerRequest("tv.a", "power.a", FurniturePowerState.On);
            request.arguments = new UnsupportedArguments();

            FurnitureFunctionResult badArguments = registry.Invoke(request);
            Assert.AreEqual(FurnitureFunctionStatuses.Failed, badArguments.status);
            Assert.AreEqual(0, store.callCount);

            store.forcedResult = new FurnitureFunctionResult
            {
                status = FurnitureFunctionStatuses.Applied,
                operationId = "operation.a",
                committedState = State("tv.other", "furniture.tv.basic", FurniturePowerState.On)
            };
            FurnitureFunctionResult badEvidence = registry.Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.On));
            Assert.AreEqual(FurnitureFunctionStatuses.Failed, badEvidence.status);
            Assert.AreEqual("invalid_committed_state_evidence", badEvidence.diagnosticCode);
        }

        [Test]
        public void CommittedEvidenceMustNameTheRegisteredPowerResource()
        {
            var store = new FakePowerStore
            {
                forcedResult = new FurnitureFunctionResult
                {
                    status = FurnitureFunctionStatuses.Applied,
                    operationId = "operation.a",
                    committedState = State("tv.a", "furniture.tv.basic", FurniturePowerState.On)
                }
            };
            store.forcedResult.committedState.resourceId = "device.other";

            FurnitureFunctionResult result = CreateRegistry(store, new FakeTargetValidator())
                .Invoke(PowerRequest("tv.a", "power.a", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.Failed, result.status);
            Assert.AreEqual("invalid_committed_state_evidence", result.diagnosticCode);
        }

        [Test]
        public void RealTargetValidatorRequiresOptInSceneMatchAndCurrentOwnerProof()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureFunctionTestCatalogue.Create();
            var scene = new FakeSceneTargetResolver();
            var reservations = new FakeReservationContextSource
            {
                resolve = request => new FurnitureFunctionReservationEvidence(request.activityId,
                    request.instanceId, request.usePointId, request.useId, request.usePointRevision)
            };
            var store = new FakePowerStore();
            var registry = CreateRegistry(store,
                new FurnitureFunctionTargetValidator(catalogue, scene, reservations));

            FurnitureFunctionResult first = registry.Invoke(PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On));
            FurnitureFunctionRequest secondRequest = PowerRequest("tv.b", "use.tv.b.power", FurniturePowerState.On);
            secondRequest.definitionId = "furniture.tv.synthetic";
            secondRequest.activityId = "activity.b";
            secondRequest.reservationOwnerId = "activity.b";
            secondRequest.runId = "run.b";
            secondRequest.invocationId = "flow.b";
            secondRequest.operationId = "operation.b";
            FurnitureFunctionResult second = registry.Invoke(secondRequest);

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, first.status);
            Assert.AreEqual(FurnitureFunctionStatuses.Applied, second.status);
            Assert.AreEqual("view.target", scene.lastCataloguePointId);
            Assert.AreEqual("activity.turn_on_tv", reservations.lastRequest.useId);
            Assert.AreEqual(2, store.stateWriteCount);
            Assert.AreEqual(FurniturePowerState.On, store.PowerFor("tv.a"));
            Assert.AreEqual(FurniturePowerState.On, store.PowerFor("tv.b"));
        }

        [Test]
        public void TargetValidatorRejectsMissingSceneBindingAndMismatchedReservationEvidence()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureFunctionTestCatalogue.Create();
            var scene = new FakeSceneTargetResolver
            {
                result = new FurnitureFunctionTargetValidation
                {
                    status = FurnitureFunctionTargetStatuses.Unavailable,
                    diagnosticCode = "furniture_point_unavailable"
                }
            };
            var reservations = new FakeReservationContextSource
            {
                resolve = request => new FurnitureFunctionReservationEvidence("activity.other",
                    request.instanceId, request.usePointId, request.useId, request.usePointRevision)
            };
            var store = new FakePowerStore();
            var registry = CreateRegistry(store,
                new FurnitureFunctionTargetValidator(catalogue, scene, reservations));

            FurnitureFunctionResult missingScenePoint = registry.Invoke(PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On));
            scene.result = new FurnitureFunctionTargetValidation { status = FurnitureFunctionTargetStatuses.Available };
            FurnitureFunctionResult wrongOwner = registry.Invoke(PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On));
            reservations.resolve = request => new FurnitureFunctionReservationEvidence(request.activityId,
                request.instanceId, request.usePointId, request.useId, request.usePointRevision + 1);
            FurnitureFunctionResult staleRevision = registry.Invoke(PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.Unavailable, missingScenePoint.status);
            Assert.AreEqual("furniture_point_unavailable", missingScenePoint.diagnosticCode);
            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, wrongOwner.status);
            Assert.AreEqual("furniture_reservation_evidence_mismatch", wrongOwner.diagnosticCode);
            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, staleRevision.status);
            Assert.AreEqual("furniture_reservation_evidence_mismatch", staleRevision.diagnosticCode);
            Assert.AreEqual(0, store.callCount);
        }

        [Test]
        public void TargetValidatorUsesTheSceneResolvedPointWhenSeveralBindingsShareOneUse()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureFunctionTestCatalogue.Create();
            var scene = new FakeSceneTargetResolver { acceptedPointId = "view.target" };
            var reservations = new FakeReservationContextSource
            {
                resolve = request => new FurnitureFunctionReservationEvidence(request.activityId,
                    request.instanceId, request.usePointId, request.useId, request.usePointRevision)
            };
            var store = new FakePowerStore();
            var registry = CreateRegistry(store,
                new FurnitureFunctionTargetValidator(catalogue, scene, reservations));

            FurnitureFunctionResult result = registry.Invoke(PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, result.status);
            Assert.AreEqual("view.target", scene.lastCataloguePointId);
            Assert.AreEqual(1, store.stateWriteCount);
        }

        [Test]
        public void SameOperationWithAnotherAllowedUseOnTheSamePointConflicts()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureFunctionTestCatalogue.Create();
            var reservations = new FakeReservationContextSource
            {
                resolve = request => new FurnitureFunctionReservationEvidence(request.activityId,
                    request.instanceId, request.usePointId, request.useId, request.usePointRevision)
            };
            var store = new FakePowerStore();
            var registry = CreateRegistry(store,
                new FurnitureFunctionTargetValidator(catalogue, new FakeSceneTargetResolver(), reservations));
            FurnitureFunctionRequest firstRequest = PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On);
            FurnitureFunctionRequest changedUseRequest = PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On);
            changedUseRequest.useId = "activity.turn_on_tv_alternate";

            FurnitureFunctionResult first = registry.Invoke(firstRequest);
            FurnitureFunctionResult changedUse = registry.Invoke(changedUseRequest);

            Assert.AreEqual(FurnitureFunctionStatuses.Applied, first.status);
            Assert.AreEqual(FurnitureFunctionStatuses.Conflict, changedUse.status);
            Assert.AreEqual("operation_payload_conflict", changedUse.diagnosticCode);
            Assert.AreEqual(1, store.stateWriteCount);
        }

        [Test]
        public void CatalogueOptInDoesNotInstallTheTrustedHandler()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureFunctionTestCatalogue.Create();
            var reservations = new FakeReservationContextSource
            {
                resolve = request => new FurnitureFunctionReservationEvidence(request.activityId,
                    request.instanceId, request.usePointId, request.useId, request.usePointRevision)
            };
            var registry = new FurnitureFunctionRegistry(
                new FurnitureFunctionTargetValidator(catalogue, new FakeSceneTargetResolver(), reservations));

            FurnitureFunctionResult result = registry.Invoke(PowerRequest("tv.a", "use.tv.a.power", FurniturePowerState.On));

            Assert.AreEqual(FurnitureFunctionStatuses.Unavailable, result.status);
            Assert.AreEqual("furniture_function_unavailable", result.diagnosticCode);
            Assert.AreEqual(0, reservations.callCount);
        }

        private static FurnitureFunctionRegistry CreateRegistry(FakePowerStore store, IFurnitureFunctionTargetValidator validator)
        {
            var registry = new FurnitureFunctionRegistry(validator);
            Assert.IsTrue(registry.Register(new FurniturePowerFunction(store)));
            return registry;
        }

        private static FurnitureFunctionRequest PowerRequest(string instanceId, string usePointId, FurniturePowerState desiredState)
        {
            return new FurnitureFunctionRequest
            {
                functionId = FurnitureFunctionIds.PowerSet,
                instanceId = instanceId,
                definitionId = "furniture.tv.basic",
                usePointId = usePointId,
                useId = "activity.turn_on_tv",
                usePointRevision = 4,
                reservationOwnerId = "activity.a",
                expectedInstanceRevision = 7,
                expectedLayoutRevision = 11,
                expectedGameRevision = 23,
                runId = "run.a",
                invocationId = "flow.a",
                stepExecutionId = "flow-step.a",
                activityId = "activity.a",
                sequenceStepId = "turn_on",
                operationId = "operation.a",
                arguments = new FurniturePowerArguments { desiredState = desiredState }
            };
        }

        private static FurnitureFunctionStateEvidence State(string instanceId, string definitionId, FurniturePowerState powerState)
        {
            return new FurnitureFunctionStateEvidence
            {
                resourceId = FurniturePowerResourceIds.DevicePower,
                instanceId = instanceId,
                definitionId = definitionId,
                powerKnown = true,
                powerState = powerState,
                instanceRevision = 8,
                layoutRevision = 12,
                gameRevision = 24
            };
        }

        private sealed class UnsupportedArguments : FurnitureFunctionArguments { }

        private sealed class PresentationAsFunctionHandler : IFurnitureFunctionHandler<UnsupportedArguments>
        {
            public string FunctionId { get { return FurniturePresentationDescriptorIds.DispenserCollectMock; } }

            public FurnitureFunctionResult Invoke(FurnitureFunctionRequest request, UnsupportedArguments arguments)
            { return new FurnitureFunctionResult { status = FurnitureFunctionStatuses.Applied, operationId = request.operationId }; }
        }

        private sealed class FakeTargetValidator : IFurnitureFunctionTargetValidator
        {
            public int callCount;
            public FurnitureFunctionTargetValidation result = new FurnitureFunctionTargetValidation
            {
                status = FurnitureFunctionTargetStatuses.Available
            };

            public FurnitureFunctionTargetValidation Validate(FurnitureFunctionRequest request)
            { callCount++; return result; }
        }

        private sealed class FakeSceneTargetResolver : IFurnitureFunctionSceneTargetResolver
        {
            public FurnitureFunctionTargetValidation result = new FurnitureFunctionTargetValidation
            { status = FurnitureFunctionTargetStatuses.Available };
            public string lastCataloguePointId;
            public string acceptedPointId;

            public FurnitureFunctionTargetValidation ValidateCurrent(FurnitureFunctionReservationRequest request,
                string cataloguePointId)
            {
                lastCataloguePointId = cataloguePointId;
                if (!string.IsNullOrEmpty(acceptedPointId) && acceptedPointId != cataloguePointId)
                    return new FurnitureFunctionTargetValidation
                    { status = FurnitureFunctionTargetStatuses.Conflict, diagnosticCode = "furniture_binding_mismatch" };
                return result;
            }
        }

        private sealed class FakeReservationContextSource : IFurnitureFunctionReservationContextSource
        {
            public Func<FurnitureFunctionReservationRequest, FurnitureFunctionReservationEvidence> resolve;
            public int callCount;
            public FurnitureFunctionReservationRequest lastRequest;

            public FurnitureFunctionReservationEvidence ResolveCurrent(FurnitureFunctionReservationRequest request)
            {
                callCount++;
                lastRequest = request;
                return resolve == null ? null : resolve(request);
            }
        }

        private sealed class FakePowerStore : IFurniturePowerStore
        {
            private readonly Dictionary<string, FurnitureFunctionResult> receipts = new Dictionary<string, FurnitureFunctionResult>();
            private readonly Dictionary<string, FurniturePowerState> powerByInstance = new Dictionary<string, FurniturePowerState>();
            private readonly Dictionary<string, string> payloadByOperation = new Dictionary<string, string>();
            public FurniturePowerCommand lastCommand;
            public FurnitureFunctionResult forcedResult;
            public int callCount;
            public int stateWriteCount;
            public int powerChangeEventCount;

            public FurniturePowerState PowerFor(string instanceId)
            { return powerByInstance[instanceId]; }

            public void SeedPower(string instanceId, FurniturePowerState powerState)
            { powerByInstance[instanceId] = powerState; }

            public FurnitureFunctionResult SetPower(FurniturePowerCommand command)
            {
                callCount++;
                lastCommand = command;
                if (forcedResult != null) return forcedResult;

                string payload = command.runId + "|" + command.invocationId + "|" + command.stepExecutionId + "|" +
                    command.activityId + "|" +
                    command.sequenceStepId + "|" + command.reservationOwnerId + "|" + command.usePointId + "|" + command.useId + "|" +
                    command.usePointRevision + "|" + command.instanceId + "|" + command.definitionId + "|" +
                    command.expectedInstanceRevision + "|" + command.expectedLayoutRevision + "|" +
                    command.expectedGameRevision + "|" + (int)command.desiredState;
                FurnitureFunctionResult prior;
                if (receipts.TryGetValue(command.operationId, out prior))
                {
                    if (payloadByOperation[command.operationId] != payload)
                        return Result(FurnitureFunctionStatuses.Conflict, command, null, "operation_payload_conflict");
                    return Result(FurnitureFunctionStatuses.Replayed, command, prior.committedState, string.Empty);
                }

                FurniturePowerState current;
                bool known = powerByInstance.TryGetValue(command.instanceId, out current);
                string status;
                if (known && current == command.desiredState)
                    status = FurnitureFunctionStatuses.NoChange;
                else
                {
                    powerByInstance[command.instanceId] = command.desiredState;
                    stateWriteCount++;
                    powerChangeEventCount++;
                    status = FurnitureFunctionStatuses.Applied;
                }

                FurnitureFunctionResult committed = Result(status, command,
                    State(command.instanceId, command.definitionId, command.desiredState), string.Empty);
                receipts.Add(command.operationId, committed);
                payloadByOperation.Add(command.operationId, payload);
                return committed;
            }

            private static FurnitureFunctionResult Result(string status, FurniturePowerCommand command,
                FurnitureFunctionStateEvidence evidence, string code)
            {
                return new FurnitureFunctionResult
                {
                    status = status,
                    operationId = command.operationId,
                    committedState = evidence,
                    diagnosticCode = code
                };
            }
        }
    }

    internal static class FurnitureFunctionTestCatalogue
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        public static FurnitureCatalogueSnapshot Create()
        {
            FurnitureCatalogueSnapshot snapshot = FurnitureCatalogueRegistry.Parse(CreateJson());
            if (snapshot.diagnostics.Any(diagnostic => diagnostic.severity == "error"))
                throw new InvalidOperationException("Synthetic furniture function catalogue failed validation.");
            return snapshot;
        }

        public static string CreateJson()
        {
            FurnitureCatalogueDocument document;
            var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
            using (var input = File.OpenRead(CataloguePath))
                document = (FurnitureCatalogueDocument)serializer.ReadObject(input);

            FurnitureDefinition tv = document.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");
            tv.points.Single(point => point.pointId == "view.target").useIds.Add("activity.turn_on_tv");
            tv.points.Single(point => point.pointId == "view.target").useIds.Add("activity.turn_on_tv_alternate");
            tv.functionBindings = new List<FurnitureFunctionBindingDescriptor>
            {
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.target",
                    useId = "activity.turn_on_tv"
                },
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.target",
                    useId = "activity.turn_on_tv_alternate"
                },
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.backup",
                    useId = "activity.turn_on_tv"
                }
            };
            tv.points.Add(new FurnitureFunctionalPoint
            {
                pointId = "view.backup",
                pointTypeId = "tv.power_control",
                localPosition = new FurnitureVector3 { x = 0, y = 0.75, z = 0.1 },
                localFacing = new FurnitureVector3 { x = 0, y = 0, z = -1 },
                useIds = new List<string> { "activity.turn_on_tv" }
            });
            FurnitureDefinition syntheticTv = Clone(tv);
            syntheticTv.definitionId = "furniture.tv.synthetic";
            syntheticTv.displayNameKey = "furniture.tv.synthetic";
            document.definitions.Add(syntheticTv);

            FurnitureDefinition dispenser = document.definitions.Single(definition => definition.definitionId == "furniture.water_dispenser");
            dispenser.presentationBindings = new List<FurniturePresentationBindingDescriptor>
            {
                new FurniturePresentationBindingDescriptor
                {
                    descriptorId = FurniturePresentationDescriptorIds.DispenserCollectMock,
                    pointId = "drink",
                    useId = "needs.drink_water"
                }
            };

            document.legacyAliases.Add(new FurnitureLegacyAlias { legacyId = "tv.a", definitionId = "furniture.tv.basic" });
            document.legacyAliases.Add(new FurnitureLegacyAlias { legacyId = "use.tv.a.power", definitionId = "furniture.tv.basic", pointId = "view.target" });
            document.legacyAliases.Add(new FurnitureLegacyAlias { legacyId = "tv.b", definitionId = "furniture.tv.synthetic" });
            document.legacyAliases.Add(new FurnitureLegacyAlias { legacyId = "use.tv.b.power", definitionId = "furniture.tv.synthetic", pointId = "view.target" });
            document.legacyAliases.Add(new FurnitureLegacyAlias { legacyId = "dispenser.a", definitionId = "furniture.water_dispenser" });
            document.legacyAliases.Add(new FurnitureLegacyAlias { legacyId = "dispenser.a.collect", definitionId = "furniture.water_dispenser", pointId = "drink" });

            using (var output = new MemoryStream())
            {
                serializer.WriteObject(output, document);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static T Clone<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
