using NUnit.Framework;
using System;
using AlterOS.Content;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurniturePresentationTests
    {
        [Test]
        public void CollectMockStartsPollsAndCompletesWithFlowActionAndStepCorrelation()
        {
            var validator = new FakeContextValidator();
            var port = CreatePort(validator);
            FurniturePresentationRequest request = Request("presentation.a", "collect");

            FurniturePresentationResult started = port.Start(request);
            FurniturePresentationResult running = port.Poll(started.handle);
            FurniturePresentationResult completed = port.Poll(running.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Accepted, started.status);
            Assert.AreEqual(FurniturePresentationStatuses.Running, running.status);
            Assert.AreEqual(FurniturePresentationStatuses.Completed, completed.status);
            Assert.AreEqual("run.a", completed.handle.runId);
            Assert.AreEqual("flow.a", completed.handle.invocationId);
            Assert.AreEqual("action.turn_on_tv@1", completed.handle.actionDefinitionRef.revision);
            Assert.AreEqual("activity.a", completed.handle.activityId);
            Assert.AreEqual("collect", completed.handle.sequenceStepId);
            Assert.AreEqual("dispenser.a", completed.handle.instanceId);
            Assert.AreEqual("dispenser.a.collect", completed.handle.usePointId);
            Assert.AreEqual(5, completed.handle.usePointRevision);
        }

        [Test]
        public void DuplicateStartReturnsCurrentStatusAndChangedPayloadFails()
        {
            var port = CreatePort(new FakeContextValidator());
            FurniturePresentationRequest request = Request("presentation.a", "collect");
            FurniturePresentationResult started = port.Start(request);
            FurniturePresentationResult running = port.Poll(started.handle);

            FurniturePresentationResult duplicate = port.Start(Request("presentation.a", "collect"));
            FurniturePresentationRequest changed = Request("presentation.a", "different-step");
            FurniturePresentationResult conflict = port.Start(changed);

            Assert.AreEqual(FurniturePresentationStatuses.Running, duplicate.status);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, conflict.status);
            Assert.AreEqual("presentation_request_id_reused", conflict.diagnosticCode);
        }

        [Test]
        public void CancelledOperationStaysCancelledWhenPolledLate()
        {
            var port = CreatePort(new FakeContextValidator());
            FurniturePresentationResult started = port.Start(Request("presentation.a", "collect"));

            FurniturePresentationResult cancelled = port.Cancel(started.handle);
            FurniturePresentationResult latePoll = port.Poll(cancelled.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Cancelled, cancelled.status);
            Assert.AreEqual(FurniturePresentationStatuses.Cancelled, latePoll.status);
        }

        [Test]
        public void CancelAfterCompletionReturnsCompleted()
        {
            var port = CreatePort(new FakeContextValidator());
            FurniturePresentationResult started = port.Start(Request("presentation.a", "collect"));
            FurniturePresentationResult running = port.Poll(started.handle);
            FurniturePresentationResult completed = port.Poll(running.handle);

            FurniturePresentationResult cancel = port.Cancel(completed.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Completed, cancel.status);
        }

        [Test]
        public void MismatchedHandleCannotAdvanceOrCancelAnotherPresentation()
        {
            var registry = new FurniturePresentationRegistry(new FakeContextValidator());
            var operation = new CountingPresentationOperation();
            Assert.IsTrue(registry.Register(operation));
            FurniturePresentationRequest request = Request("presentation.a", "collect");
            request.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult started = registry.Start(request);
            FurniturePresentationHandle wrong = Copy(started.handle);
            wrong.usePointRevision++;

            FurniturePresentationResult rejectedPoll = registry.Poll(wrong);
            FurniturePresentationResult rejectedCancel = registry.Cancel(wrong);
            FurniturePresentationResult validPoll = registry.Poll(started.handle);
            FurniturePresentationResult validCancel = registry.Cancel(validPoll.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, rejectedPoll.status);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, rejectedCancel.status);
            Assert.AreEqual(FurniturePresentationStatuses.Running, validPoll.status);
            Assert.AreEqual(FurniturePresentationStatuses.Cancelled, validCancel.status);
            Assert.AreEqual(1, operation.cancelCallCount);
        }

        [Test]
        public void ValidatorRejectsStaleOrUnownedPresentationRequest()
        {
            var validator = new FakeContextValidator
            {
                result = new FurniturePresentationValidationResult
                {
                    allowed = false,
                    diagnosticCode = "stale_furniture_binding"
                }
            };
            var port = CreatePort(validator);

            FurniturePresentationResult result = port.Start(Request("presentation.a", "collect"));

            Assert.AreEqual(FurniturePresentationStatuses.Failed, result.status);
            Assert.AreEqual("stale_furniture_binding", result.diagnosticCode);
        }

        [Test]
        public void UnregisteredPresentationDescriptorFailsClosed()
        {
            var port = CreatePort(new FakeContextValidator());
            FurniturePresentationRequest request = Request("presentation.a", "collect");
            request.presentationDescriptorId = "power.set";

            FurniturePresentationResult result = port.Start(request);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, result.status);
            Assert.AreEqual("presentation_descriptor_unavailable", result.diagnosticCode);
        }

        [Test]
        public void DurablePowerFunctionCannotBeRegisteredAsPresentationOperation()
        {
            var registry = new FurniturePresentationRegistry(new FakeContextValidator());

            bool registered = registry.Register(new PowerAsPresentationOperation());

            Assert.IsFalse(registered);
        }

        [Test]
        public void DurableMutationOutcomeCannotEscapeThroughPresentationPort()
        {
            var registry = new FurniturePresentationRegistry(new FakeContextValidator());
            Assert.IsTrue(registry.Register(new AppliedResultPresentationOperation()));
            FurniturePresentationRequest request = Request("presentation.a", "collect");
            request.presentationDescriptorId = "test.presentation.applied";

            FurniturePresentationResult result = registry.Start(request);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, result.status);
            Assert.AreEqual("invalid_presentation_start_result", result.diagnosticCode);
        }

        [Test]
        public void FailedPresentationStartAttemptsCleanupAndReportsFailure()
        {
            var validator = new FakeContextValidator();
            var registry = new FurniturePresentationRegistry(validator);
            var operation = new ThrowingStartPresentationOperation();
            Assert.IsTrue(registry.Register(operation));
            FurniturePresentationRequest request = Request("presentation.a", "collect");
            request.presentationDescriptorId = operation.PresentationDescriptorId;

            FurniturePresentationResult result = registry.Start(request);
            FurniturePresentationRequest retryRequest = Request("presentation.a", "collect");
            retryRequest.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult retry = registry.Start(retryRequest);
            FurniturePresentationRequest changed = Request("presentation.a", "changed-step");
            changed.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult changedPayload = registry.Start(changed);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, result.status);
            Assert.AreEqual("presentation_start_failed", result.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, retry.status);
            Assert.AreEqual("presentation_start_failed", retry.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, changedPayload.status);
            Assert.AreEqual("presentation_request_id_reused", changedPayload.diagnosticCode);
            Assert.AreEqual(1, operation.startCallCount);
            Assert.AreEqual(1, operation.cancelCallCount);
        }

        [Test]
        public void FailedStartResultIsCleanedOnceAndItsDiagnosticIsReplayed()
        {
            var registry = new FurniturePresentationRegistry(new FakeContextValidator());
            var operation = new FailedStartResultPresentationOperation();
            Assert.IsTrue(registry.Register(operation));
            FurniturePresentationRequest request = Request("presentation.failed-start", "collect");
            request.presentationDescriptorId = operation.PresentationDescriptorId;

            FurniturePresentationResult failed = registry.Start(request);
            FurniturePresentationRequest retryRequest = Request("presentation.failed-start", "collect");
            retryRequest.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult retry = registry.Start(retryRequest);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, failed.status);
            Assert.AreEqual("synthetic_start_rejected", failed.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, retry.status);
            Assert.AreEqual("synthetic_start_rejected", retry.diagnosticCode);
            Assert.AreEqual(1, operation.startCallCount);
            Assert.AreEqual(1, operation.cancelCallCount);
        }

        [Test]
        public void FailedPollResultIsCleanedOnceReplayedAndReleasesTheOperationSlot()
        {
            var registry = new FurniturePresentationRegistry(new FakeContextValidator());
            var operation = new FailedPollPresentationOperation();
            Assert.IsTrue(registry.Register(operation));
            FurniturePresentationRequest request = Request("presentation.failed-poll", "collect");
            request.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult started = registry.Start(request);

            FurniturePresentationResult failed = registry.Poll(started.handle);
            FurniturePresentationResult retry = registry.Poll(started.handle);
            FurniturePresentationRequest next = Request("presentation.next", "next-step");
            next.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult admitted = registry.Start(next);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, failed.status);
            Assert.AreEqual("synthetic_poll_failed", failed.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, retry.status);
            Assert.AreEqual("synthetic_poll_failed", retry.diagnosticCode);
            Assert.AreEqual(1, operation.pollCallCount);
            Assert.AreEqual(1, operation.cancelCallCount);
            Assert.AreEqual(FurniturePresentationStatuses.Accepted, admitted.status);
            Assert.AreEqual(2, operation.startCallCount);
        }

        [Test]
        public void FailedCancelIsNotInvokedAgainDuringCleanup()
        {
            var registry = new FurniturePresentationRegistry(new FakeContextValidator());
            var operation = new ThrowingCancelPresentationOperation();
            Assert.IsTrue(registry.Register(operation));
            FurniturePresentationRequest request = Request("presentation.a", "collect");
            request.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult started = registry.Start(request);

            FurniturePresentationResult cancelled = registry.Cancel(started.handle);
            FurniturePresentationResult retryCancel = registry.Cancel(started.handle);
            FurniturePresentationRequest blockedRequest = Request("presentation.next", "next-step");
            blockedRequest.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult blocked = registry.Start(blockedRequest);

            Assert.AreEqual(FurniturePresentationStatuses.Running, cancelled.status);
            Assert.AreEqual("presentation_cleanup_pending", cancelled.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Running, retryCancel.status);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, blocked.status);
            Assert.AreEqual("presentation_operation_already_active", blocked.diagnosticCode);
            Assert.AreEqual(1, operation.cancelCallCount);
        }

        [Test]
        public void FailedPollCleanupPendingKeepsSlotUntilCancellationIsObserved()
        {
            var validator = new FakeContextValidator();
            var registry = new FurniturePresentationRegistry(validator);
            var operation = new PendingCancelAfterPollFailureOperation();
            Assert.IsTrue(registry.Register(operation));
            FurniturePresentationRequest request = Request("presentation.failed-poll", "collect");
            request.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult started = registry.Start(request);

            FurniturePresentationResult cleaning = registry.Poll(started.handle);
            FurniturePresentationRequest blockedRequest = Request("presentation.blocked", "next-step");
            blockedRequest.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult blocked = registry.Start(blockedRequest);
            validator.result = new FurniturePresentationValidationResult
            {
                allowed = false,
                diagnosticCode = "sequence_end_already_requested"
            };
            FurniturePresentationResult failed = registry.Poll(started.handle);
            validator.result = new FurniturePresentationValidationResult { allowed = true };
            FurniturePresentationRequest nextRequest = Request("presentation.next", "next-step");
            nextRequest.presentationDescriptorId = operation.PresentationDescriptorId;
            FurniturePresentationResult admitted = registry.Start(nextRequest);

            Assert.AreEqual(FurniturePresentationStatuses.Running, cleaning.status);
            Assert.AreEqual("presentation_cleanup_pending", cleaning.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, blocked.status);
            Assert.AreEqual("presentation_operation_already_active", blocked.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, failed.status);
            Assert.AreEqual("synthetic_poll_failed", failed.diagnosticCode);
            Assert.AreEqual(1, operation.cancelCallCount);
            Assert.AreEqual(FurniturePresentationStatuses.Accepted, admitted.status);
        }

        [Test]
        public void OnlyOneTransientPresentationCanBeActiveForOneSequence()
        {
            var port = CreatePort(new FakeContextValidator());
            FurniturePresentationResult started = port.Start(Request("presentation.a", "collect"));
            FurniturePresentationRequest secondStep = Request("presentation.b", "another_step");

            FurniturePresentationResult rejected = port.Start(secondStep);
            FurniturePresentationResult running = port.Poll(started.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, rejected.status);
            Assert.AreEqual("presentation_operation_already_active", rejected.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Running, running.status);
        }

        [Test]
        public void NewSequenceCannotReplaceAnActiveSequenceHandle()
        {
            var port = CreatePort(new FakeContextValidator());
            FurniturePresentationResult started = port.Start(Request("presentation.a", "collect"));
            FurniturePresentationRequest otherSequence = Request("presentation.b", "collect");
            otherSequence.activityId = "activity.b";
            otherSequence.runId = "run.b";
            otherSequence.invocationId = "flow.b";

            FurniturePresentationResult rejected = port.Start(otherSequence);
            FurniturePresentationResult running = port.Poll(started.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Failed, rejected.status);
            Assert.AreEqual("another_presentation_sequence_is_active", rejected.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Running, running.status);
        }

        [Test]
        public void MockPresentationRequiresItsSeparateCatalogueOptInAndExactReservation()
        {
            var scene = new FakeSceneTargetResolver();
            var reservations = new FakeReservationContextSource
            {
                resolve = request => new FurnitureFunctionReservationEvidence(request.activityId,
                    request.instanceId, request.usePointId, request.useId, request.usePointRevision)
            };
            var validator = new FurniturePresentationTargetValidator(
                FurnitureFunctionTestCatalogue.Create(), scene, reservations);
            var registry = new FurniturePresentationRegistry(validator);
            Assert.IsTrue(registry.Register(new DispenserCollectMockPresentation()));

            FurniturePresentationResult started = registry.Start(Request("presentation.bound", "collect"));
            FurniturePresentationResult running = registry.Poll(started.handle);
            FurniturePresentationResult completed = registry.Poll(running.handle);

            Assert.AreEqual(FurniturePresentationStatuses.Accepted, started.status);
            Assert.AreEqual(FurniturePresentationStatuses.Running, running.status);
            Assert.AreEqual(FurniturePresentationStatuses.Completed, completed.status);
            Assert.AreEqual("needs.drink_water", reservations.lastRequest.useId);
            Assert.AreEqual("drink", scene.lastCataloguePointId);
        }

        [Test]
        public void MockPresentationRejectsMissingSceneTargetAndWrongReservationOwner()
        {
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
            var validator = new FurniturePresentationTargetValidator(
                FurnitureFunctionTestCatalogue.Create(), scene, reservations);
            var registry = new FurniturePresentationRegistry(validator);
            var operation = new MockDispenserPresentationOperation();
            Assert.IsTrue(registry.Register(operation));

            FurniturePresentationResult missingScenePoint = registry.Start(Request("presentation.missing", "collect"));
            scene.result = new FurnitureFunctionTargetValidation { status = FurnitureFunctionTargetStatuses.Available };
            FurniturePresentationResult wrongOwner = registry.Start(Request("presentation.owner", "collect"));

            Assert.AreEqual(FurniturePresentationStatuses.Failed, missingScenePoint.status);
            Assert.AreEqual("furniture_point_unavailable", missingScenePoint.diagnosticCode);
            Assert.AreEqual(FurniturePresentationStatuses.Failed, wrongOwner.status);
            Assert.AreEqual("furniture_reservation_evidence_mismatch", wrongOwner.diagnosticCode);
            Assert.AreEqual(0, operation.startCallCount);
        }

        private static FurniturePresentationRegistry CreatePort(FakeContextValidator validator)
        {
            var registry = new FurniturePresentationRegistry(validator);
            Assert.IsTrue(registry.Register(new DispenserCollectMockPresentation()));
            return registry;
        }

        private static FurniturePresentationRequest Request(string requestId, string stepId)
        {
            return new FurniturePresentationRequest
            {
                presentationRequestId = requestId,
                presentationDescriptorId = FurniturePresentationDescriptorIds.DispenserCollectMock,
                runId = "run.a",
                invocationId = "flow.a",
                actionDefinitionRef = new ContentRef("compiled", "action.turn_on_tv", "action.turn_on_tv@1", "action-hash"),
                activityId = "activity.a",
                sequenceStepId = stepId,
                instanceId = "dispenser.a",
                definitionId = "furniture.water_dispenser",
                usePointId = "dispenser.a.collect",
                usePointRevision = 5
            };
        }

        private static FurniturePresentationHandle Copy(FurniturePresentationHandle source)
        {
            return new FurniturePresentationHandle
            {
                presentationRequestId = source.presentationRequestId,
                presentationDescriptorId = source.presentationDescriptorId,
                runId = source.runId,
                invocationId = source.invocationId,
                actionDefinitionRef = new ContentRef(source.actionDefinitionRef.store, source.actionDefinitionRef.id,
                    source.actionDefinitionRef.revision, source.actionDefinitionRef.expectedHash),
                activityId = source.activityId,
                sequenceStepId = source.sequenceStepId,
                instanceId = source.instanceId,
                definitionId = source.definitionId,
                usePointId = source.usePointId,
                usePointRevision = source.usePointRevision
            };
        }

        private sealed class FakeContextValidator : IFurniturePresentationContextValidator
        {
            public FurniturePresentationValidationResult result = new FurniturePresentationValidationResult { allowed = true };

            public FurniturePresentationValidationResult Validate(FurniturePresentationHandle handle,
                FurniturePresentationValidationPurpose purpose)
            { return result; }
        }

        private sealed class FakeSceneTargetResolver : IFurnitureFunctionSceneTargetResolver
        {
            public FurnitureFunctionTargetValidation result = new FurnitureFunctionTargetValidation
            { status = FurnitureFunctionTargetStatuses.Available };
            public string lastCataloguePointId;

            public FurnitureFunctionTargetValidation ValidateCurrent(FurnitureFunctionReservationRequest request,
                string cataloguePointId)
            {
                lastCataloguePointId = cataloguePointId;
                return result;
            }
        }

        private sealed class FakeReservationContextSource : IFurnitureFunctionReservationContextSource
        {
            public Func<FurnitureFunctionReservationRequest, FurnitureFunctionReservationEvidence> resolve;
            public FurnitureFunctionReservationRequest lastRequest;

            public FurnitureFunctionReservationEvidence ResolveCurrent(FurnitureFunctionReservationRequest request)
            {
                lastRequest = request;
                return resolve == null ? null : resolve(request);
            }
        }

        private sealed class PowerAsPresentationOperation : IFurniturePresentationOperation
        {
            public string PresentationDescriptorId { get { return FurnitureFunctionIds.PowerSet; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Completed }; }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Completed }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Completed }; }
        }

        private sealed class AppliedResultPresentationOperation : IFurniturePresentationOperation
        {
            public string PresentationDescriptorId { get { return "test.presentation.applied"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurnitureFunctionStatuses.Applied }; }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Completed }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled }; }
        }

        private sealed class ThrowingStartPresentationOperation : IFurniturePresentationOperation
        {
            public int startCallCount;
            public int cancelCallCount;
            public string PresentationDescriptorId { get { return "test.presentation.throwing_start"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            { startCallCount++; throw new System.InvalidOperationException("synthetic start failure"); }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Running }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            {
                cancelCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled };
            }
        }

        private sealed class FailedStartResultPresentationOperation : IFurniturePresentationOperation
        {
            public int startCallCount;
            public int cancelCallCount;
            public string PresentationDescriptorId { get { return "test.presentation.failed_start"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            {
                startCallCount++;
                return new FurniturePresentationResult
                {
                    handle = handle,
                    status = FurniturePresentationStatuses.Failed,
                    diagnosticCode = "synthetic_start_rejected"
                };
            }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Running }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            {
                cancelCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled };
            }
        }

        private sealed class FailedPollPresentationOperation : IFurniturePresentationOperation
        {
            public int startCallCount;
            public int pollCallCount;
            public int cancelCallCount;
            public string PresentationDescriptorId { get { return "test.presentation.failed_poll"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            {
                startCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Accepted };
            }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            {
                pollCallCount++;
                return new FurniturePresentationResult
                {
                    handle = handle,
                    status = FurniturePresentationStatuses.Failed,
                    diagnosticCode = "synthetic_poll_failed"
                };
            }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            {
                cancelCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled };
            }
        }

        private sealed class PendingCancelAfterPollFailureOperation : IFurniturePresentationOperation
        {
            public int startCallCount;
            public int pollCallCount;
            public int cancelCallCount;
            private bool cleanupRequested;
            public string PresentationDescriptorId { get { return "test.presentation.pending_cleanup"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            {
                startCallCount++;
                cleanupRequested = false;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Accepted };
            }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            {
                pollCallCount++;
                if (!cleanupRequested)
                    return new FurniturePresentationResult
                    {
                        handle = handle,
                        status = FurniturePresentationStatuses.Failed,
                        diagnosticCode = "synthetic_poll_failed"
                    };
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled };
            }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            {
                cancelCallCount++;
                cleanupRequested = true;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Running };
            }
        }

        private sealed class CountingPresentationOperation : IFurniturePresentationOperation
        {
            public int startCallCount;
            public int cancelCallCount;
            public string PresentationDescriptorId { get { return "test.presentation.counting"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            {
                startCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Accepted };
            }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Running }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            {
                cancelCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled };
            }
        }

        private sealed class MockDispenserPresentationOperation : IFurniturePresentationOperation
        {
            public int startCallCount;
            public string PresentationDescriptorId { get { return FurniturePresentationDescriptorIds.DispenserCollectMock; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            {
                startCallCount++;
                return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Accepted };
            }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Running }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Cancelled }; }
        }

        private sealed class ThrowingCancelPresentationOperation : IFurniturePresentationOperation
        {
            public int cancelCallCount;
            public string PresentationDescriptorId { get { return "test.presentation.throwing_cancel"; } }
            public FurniturePresentationResult Start(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Accepted }; }
            public FurniturePresentationResult Poll(FurniturePresentationHandle handle)
            { return new FurniturePresentationResult { handle = handle, status = FurniturePresentationStatuses.Running }; }
            public FurniturePresentationResult Cancel(FurniturePresentationHandle handle)
            {
                cancelCallCount++;
                throw new System.InvalidOperationException("synthetic cancel failure");
            }
        }
    }
}
