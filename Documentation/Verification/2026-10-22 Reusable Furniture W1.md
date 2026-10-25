---
title: Reusable Furniture W1
date: 2026-10-23
tags: [alteros, reusable-content, furniture, verification]
status: source-ready-integration-pending
---

# Reusable Furniture W1A — pure function and presentation contracts

## Result

Furniture W1A source is ready for Main's review. It adds the portable typed
function and transient presentation contracts, the registered `power.set`
handler and the `dispenser.collect_mock` lifecycle mock. The durable and transient
result classes remain separate. The mock changes no furniture stock, Item count,
hydration, stats, currency or saved state.

This is a source-level producer release only. It does not wire the contracts into
ActivityHost/ActivitySequence, add a DBMS persistence adapter, project TV state in
the Unity scene, or establish live PoC readiness.

## Changed paths

- `Assets/AlterOS/Furniture/FurnitureFunctions.cs` — typed request/result and
  committed state evidence; trusted handler registry; binding validation port;
  durable result statuses.
- `Assets/AlterOS/Furniture/FurniturePowerFunction.cs` — `power.set`, typed
  Off/On arguments/command, `IFurniturePowerStore`, and fixed resource ID
  `device.power` (`Off=0`, `On=1`). It validates store outcome, target and known
  committed-state evidence.
- `Assets/AlterOS/Furniture/FurniturePresentationContracts.cs` — the single
  `IFurniturePresentationPort.Start/Poll/Cancel` contract, pinned request/handle
  correlation, descriptor registry, context-validation port and transient
  `dispenser.collect_mock` handler. It reports only
  `accepted/running/completed/cancelled/failed`.
- `Assets/AlterOS/Tests/Editor/Furniture/FurnitureFunctionTests.cs` — fake-store
  and binding tests for exact target/revisions/correlation, typed state, unknown
  versus explicit Off, no-change, replay, changed-payload conflict, unsupported
  function/definition, and invalid result evidence.
- `Assets/AlterOS/Tests/Editor/Furniture/FurniturePresentationTests.cs` — tests
  for full Flow/Action/sequence-step/target correlation, start/poll/cancel,
  replay and payload mismatch, stale handles, active-sequence ownership, cleanup
  on failed start/cancel, and rejection of durable mutation statuses.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` — includes the two focused
  Furniture test files and compiles the existing `ContentRef` implementation
  (`ContentContracts`, `ContentBuildManifest`, `ContentJson`) for the presentation
  DTOs. The portable check still verifies the output has no UnityEngine or
  UnityEditor references.

No `Assets/AlterOS/Activities`, `Assets/AlterOS/Scenarios`, `Assets/AlterOS/Content`,
`Assets/AlterOS/Authoring`, `Assets/AlterOS/Persistence`, Workbench, scene, prefab,
view or other serialized file was changed. The shared Content sources are read as
compiler inputs by the portable harness only.

## Exact interfaces

### Durable function

```csharp
FurnitureFunctionResult IFurnitureFunctionPort.Invoke(FurnitureFunctionRequest request)
FurnitureFunctionResult IFurniturePowerStore.SetPower(FurniturePowerCommand command)
```

`FurnitureFunctionRequest` pins function ID; instance, definition and use-point
IDs; use-point revision; reservation owner; expected instance/layout/game
revisions; run ID; Flow `invocationId`; physical `activityId`; sequence-step ID;
stable `operationId`; and typed arguments. `reservationOwnerId` must equal the
physical `activityId`. The injected validator owns exact point, compatibility,
revision and claim checks; the registry owns no claims.

`FurniturePowerCommand` carries the same target/revision and Flow/Action/step
identity plus typed desired Off/On state. The store supplies only
`applied/no_change/replayed/unavailable/conflict/failed`. A successful status
requires matching known committed evidence for the exact `device.power` target
and requested state. Store exceptions, wrong operation IDs, unknown statuses or
mismatched evidence fail closed. The registry will not register
`dispenser.collect_mock` as a durable function.

### Transient presentation

```csharp
FurniturePresentationResult IFurniturePresentationPort.Start(FurniturePresentationRequest request)
FurniturePresentationResult IFurniturePresentationPort.Poll(FurniturePresentationHandle handle)
FurniturePresentationResult IFurniturePresentationPort.Cancel(FurniturePresentationHandle handle)
```

The immutable correlation handle carries `presentationRequestId`, descriptor
ID, `runId`, Flow `invocationId`, pinned `ContentRef actionDefinitionRef`,
physical `activityId`, inner `sequenceStepId`, exact instance/definition/use-point
IDs and use-point revision. The context validator checks the active sequence and
the appropriate target/claim rules. Repeating Start with the same request and
payload returns its current status; changed payloads and mismatched handles fail
without advancing or cancelling the stored operation. Only one presentation may
be active for an Activity ID. Failed Start/Poll results retain their request
identity and original diagnostic for replay. Cleanup invokes Cancel at most once;
if cancellation is pending, the operation slot remains occupied until Poll
observes `cancelled` or `completed`. Cleanup polling can finish after the active
sequence validator starts denying the request. ActivitySequence remains the only
sequence lifecycle owner.

The mock advances `accepted -> running -> completed` through its poll contract;
Cancel returns `cancelled` for active work and `completed` if completion already
won. A failed start attempts cancellation cleanup. The W1A mock produces no
visible animation and makes no stock or game-state change; Unity presentation
remains a later adapter.

## Validation

- Baseline before W1A: the previously run synthetic Furniture suite passed
  **84/84**; it was not rerun in this recovery, as directed.
- Focused command:
  `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurnitureFunctionTests`
  — **12 passed, 0 failed**.
- Focused command:
  `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurniturePresentationTests`
  — **16 passed, 0 failed**.
- The function tests are `PowerSetSendsTheExactTypedCommandAndCommittedEvidence`,
  `UnregisteredFunctionCannotReachPowerStore`,
  `PresentationDescriptorCannotBeRegisteredAsDurableFunction`,
  `StaleBindingCannotReachPowerStore`,
  `UnsupportedDefinitionReturnsUnavailableBeforePowerStore`,
  `ExplicitOffFromUnknownStateCommitsKnownOff`,
  `AlreadyDesiredStateReturnsNoChangeWithoutWritingAgain`,
  `SameRegisteredPowerHandlerSupportsAnotherCompatibleDefinition`,
  `ReservationOwnerMismatchConflictsBeforeTargetValidation`,
  `SameOperationReplaysAndChangedPayloadConflictsWithoutSecondWrite`,
  `InvalidArgumentsOrCommittedEvidenceFailClosed`, and
  `CommittedEvidenceMustNameTheRegisteredPowerResource`.
- The presentation tests are `CollectMockStartsPollsAndCompletesWithFlowActionAndStepCorrelation`,
  `DuplicateStartReturnsCurrentStatusAndChangedPayloadFails`,
  `CancelledOperationStaysCancelledWhenPolledLate`, `CancelAfterCompletionReturnsCompleted`,
  `MismatchedHandleCannotAdvanceOrCancelAnotherPresentation`,
  `ValidatorRejectsStaleOrUnownedPresentationRequest`,
  `UnregisteredPresentationDescriptorFailsClosed`,
  `DurablePowerFunctionCannotBeRegisteredAsPresentationOperation`,
  `DurableMutationOutcomeCannotEscapeThroughPresentationPort`,
  `FailedPresentationStartAttemptsCleanupAndReportsFailure`,
  `FailedStartResultIsCleanedOnceAndItsDiagnosticIsReplayed`,
  `FailedPollResultIsCleanedOnceReplayedAndReleasesTheOperationSlot`,
  `FailedCancelIsNotInvokedAgainDuringCleanup`,
  `FailedPollCleanupPendingKeepsSlotUntilCancellationIsObserved`,
  `OnlyOneTransientPresentationCanBeActiveForOneSequence`, and
  `NewSequenceCannotReplaceAnActiveSequenceHandle`.
- Both runs compiled the portable Furniture core and test assembly and reported
  no UnityEngine/UnityEditor references. Test compilation reports the existing
  `CS0649` warning for `ContentRelease.compilerAttestation` in the shared
  Content contracts; it is unrelated to these changes and was not suppressed.
- Test artifacts:
  `E:\Pleiades\alteros\.superpowers\sdd\Furniture Inventory and Placement Implementation Plan\3701df973011403fb9a3fd9998161017`
  (initial 12-test function run),
  `E:\Pleiades\alteros\.superpowers\sdd\Furniture Inventory and Placement Implementation Plan\0969288c74514443bd06ce243c35bc88`
  (final 12-test function run), and
  `E:\Pleiades\alteros\.superpowers\sdd\Furniture Inventory and Placement Implementation Plan\1c8e83b30e2f4c4e8baab01f28de14fc`
  (final 16-test presentation run).

The first Round 1 red run exposed a test setup issue: it kept the fake context
validator denying requests when the test attempted a new Start after cleanup.
The regression now restores validation only after verifying cleanup completed,
then confirms the released slot admits the next operation. Final focused Furniture
presentation verification is green.

The initial red checks caught and fixed two cross-class registration errors
(`dispenser.collect_mock` as a durable function and `power.set` as a presentation
descriptor), plus missing presentation cleanup after a thrown start and duplicate
cancel attempts after a thrown cancel.

## Ownership and remaining dependencies

- Furniture releases only the three pure Furniture source files and the focused
  test/harness paths above. The new source contains no Unity lifecycle or scene
  bindings.
- Behavior W0 still owns ActivitySequence invocation and consumes the same
  `IFurniturePresentationPort`; no Activities/Scenario source was changed here.
- DBMS still owns `IFurniturePowerStore` persistence, CAS, event and operation
  receipts. No SQLite/game-save file or Item storage was touched.
- Furniture W4 still owns Editor-authored TV state view, functional TV point,
  NavMesh/input readiness and ModelTest wiring after Main's acknowledgement.
  The new `.cs.meta` files are not present yet and must be generated by Unity during
  the acknowledged Editor window; none was hand-authored. Unity Editor import/compile,
  scene/prefab serialization, Play Mode, Game View and live PoC checks were not run.
- Preserve Main's baseline Console observations—no-valid-NavMesh and
  `IsPointerOverGameObject()` input callback messages—for W4 comparison. This
  W1A run did not query or clear the Console and makes no claim about those logs.
