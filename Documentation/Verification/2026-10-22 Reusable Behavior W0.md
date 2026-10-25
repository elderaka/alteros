---
title: Reusable Behavior Framework W0
date: 2026-10-23
tags: [alteros, verification, implementation, activity-sequence, scenario]
status: w0-portable-contracts-released
---
# Reusable Behavior Framework — W0

W0 implemented the portable ActivitySequence foundation and public Scenario host-effect
port for the [[../Tracking/Reusable Content Framework Workflow]]. This releases the
Behavior-owned pure contracts to W1 DBMS/Furniture. It does not implement W2 host,
ScenarioSession, menu, furniture or persistence adapters and is not a Unity/player pass.

## Files changed

- `Assets/AlterOS/Activities/ActivityContracts.cs` — retains only the Unity-bound
  `IActivityMotion` interface.
- `Assets/AlterOS/Activities/ActivityExecutionContracts.cs` — the original BCL-only
  Activity enums, settings/context/view/event, `IActivityExecution`, factory and
  hydration checkpoint port, preserving namespace and serialized enum numbers.
- `Assets/AlterOS/Activities/ActivitySequenceDefinition.cs` — canonical primitive IDs,
  immutable normalized sequence/step/value/result-binding DTOs and source DTO.
- `Assets/AlterOS/Activities/ActivityPrimitiveRegistry.cs` — typed primitive and
  argument/result descriptors; duplicate IDs fail closed. The registered `wait`
  descriptor must bound `durationSeconds` within finite 0–300 seconds.
- `Assets/AlterOS/Activities/ActivitySequenceBuilder.cs` — one normalization/validation
  path for data `steps` or a trusted registered composer, with exactly-one-source
  enforcement.
- `Assets/AlterOS/Activities/ActivitySequenceValidator.cs` — bounded graph, IDs,
  primitive/argument/result-binding/type checks, forward-only transitions and
  reachability diagnostics.
- `Assets/AlterOS/Activities/ActivitySequenceRunner.cs` — portable invocation,
  cancellation, start/completion/result DTOs and `IActivitySequenceRunner` /
  `IActivityPrimitivePort` lifecycle contracts. It deliberately contains no W2
  `ActivityHost` adapter or runner implementation.
- `Assets/AlterOS/Scenarios/ScenarioHostEffectContracts.cs` — public
  `IScenarioHostEffectPort.Apply` request/result contract.
- `Tools/ScenarioRuntime/ActivitySequenceFlowTests.cs` — focused portable contract,
  builder, validator, and compatibility tests.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` — includes the portable W0
  production sources and sends generated compiler/test artifacts to the OS temp
  directory rather than another plan's workspace.

W0 made no changes to `.meta` files, scenes, prefabs, host/bridge code,
Content/Authoring/Persistence, Furniture-owned contracts, Workbench files, packages,
credentials, or profiles. Unity will generate metadata for the new scripts on its later
import.

## Public consumer contracts

Primitive/action sequence:

```csharp
new ActivityPrimitiveRegistry(IEnumerable<ActivityPrimitiveDescriptor> items)
bool ActivityPrimitiveRegistry.TryResolve(string primitiveId,
    out ActivityPrimitiveDescriptor descriptor)

new ActivitySequenceBuilder(ActivityPrimitiveRegistry primitives,
    IEnumerable<IActivitySequenceComposer> trustedComposers)
ActivitySequenceBuildResult ActivitySequenceBuilder.Build(ActivitySequenceSource source)
ActivitySequenceDefinition IActivitySequenceComposer.Compose(ActivitySequenceSource source)

new ActivitySequenceValidator(ActivityPrimitiveRegistry primitiveRegistry)
ActivitySequenceValidationResult ActivitySequenceValidator.Validate(
    ActivitySequenceDefinition definition)

public interface IActivityPrimitivePort
{
    ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request);
    ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request);
}

public interface IActivitySequenceRunner
{
    ActivitySequenceStartResult Start(ActivitySequenceRequest request);
    ActivitySequenceView Tick(double nowSeconds);
    ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion);
    ActivitySequenceView Cancel(ActivitySequenceCancellation cancellation);
    ActivitySequenceView Shutdown(string reason);
}
```

`ActivitySequenceSource` carries `id`, `revision`, and exactly one of `steps` or
registered `composerId`. The builder produces the same immutable normalized
`ActivitySequenceDefinition` for both forms and sends both through the same validator.
New Actions use `implementationKind=activity_sequence`; `host_effect` and migrated
`legacy_activity` are not sequence sources. DBMS owns the serialized Action schema,
content revisions, compiler and release projection.

The canonical `invoke_furniture` primitive is described by
`ActivityPrimitiveDescriptor.operationKind`: persistent Furniture mutation or
presentation. W0 adds no `IActivityPresentationPort`; W2 consumes Furniture's sole
contract:

```csharp
public interface IFurnitureFunctionPort
{
    FurnitureFunctionResult Invoke(FurnitureFunctionRequest request);
}

public interface IFurniturePresentationPort
{
    FurniturePresentationResult Start(FurniturePresentationRequest request);
    FurniturePresentationResult Poll(FurniturePresentationHandle handle);
    FurniturePresentationResult Cancel(FurniturePresentationHandle handle);
}
```

At the presentation adapter boundary:

| W0 identity | Furniture request/handle field |
| --- | --- |
| `scenarioRunId` | `runId` |
| `flowInvocationId` | `invocationId` |
| pinned Action `ContentRef` | `actionDefinitionRef` |
| `actionInvocationId` | `activityId` |
| `sequenceStepId` | `sequenceStepId` |
| primitive `invocationId` | `presentationRequestId` |
| exact Furniture target binding | instance/definition/use-point/point revision and reservation owner |

Scenario `stepExecutionId` remains in Behavior's parent Flow/history correlation. It
must not be substituted for Furniture's inner sequence-step ID. Preserve the Furniture
handle unchanged through Poll/Cancel and map result status names only; never infer a
durable furniture mutation from a presentation result.

Scenario effect:

```csharp
public interface IScenarioHostEffectPort
{
    ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request);
}
```

`ScenarioHostEffectRequest` contains pinned host-effect `actionRef`, `scenarioRunId`,
Flow `invocationId`, grant-step `stepExecutionId`, `requiresCompletedStepId`, and the
actual `completedStepExecutionId`. Its Action binding owns `player.item.grant`, the
pinned Item ref, and authored positive quantity. There is no request-level `itemRef`,
quantity delta, or physical Action invocation ID. `applied`/`replayed` results require
operation and receipt IDs and all result statuses retain run/Flow/step correlation.
DBMS owns the Item-specific adapter, receipt lookup, atomic quantity/event commit and
migrations behind this public port.

## Validation

Pre-change baseline: `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` passed **67/67**.
Focused TDD run after review fix: `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter ActivitySequence`
passed **10/10**. Final full run after review fix: `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`
passed **77/77**. The harness compiled the Activity/Scenario/Content/Authoring sources
with Unity 6000.4.4f1's bundled Roslyn compiler; existing CS0649 warnings for
JSON-populated legacy DTO fields remain. A static source check confirmed the new
W0 production files have no `UnityEngine` or `UnityEditor` references.

TDD red/green evidence included missing public API types before implementation,
rejected empty-plus-composer source, oversized wait descriptor, unreachable step,
mutable normalized output and missing primitive-cancellation identity. Review regressions
also prove that a binding skipped by an incoming branch is rejected and that a bound wait
requires a producer range within 0..300; runtime primitive outputs at 0 and 300 are accepted
while 301 is rejected. The final focused and full runs passed after these changes.

Unity Editor import/compile, EditMode tests, Play Mode, ActivityHost integration and
player behavior were not checked in W0, per Main's recovery instruction. These remain
for the later stable slice; source/headless success is not a Unity pass.

## Harness scratch-path caveat

The first pre-change baseline run used the old script default and left generated build
artifacts under
`.superpowers/sdd/Scenario Conversation Implementation Plan/scenario-csharp-ed067a64b54b44cfaec04f11d0dd2aa9`.
I did not delete or alter that other plan's workspace. `Check-ScenarioRuntime.ps1` now
uses a GUID-named system-temp directory, as shown by the focused and final test output.

The source-baseline self-check also found `Assets/AlterOS/Authoring/AuthoringContracts.cs`
differs from the recorded baseline. This is outside W0 and falls within the independently
authorized W1A DBMS Authoring window; W0 did not edit it. W2/bridge/Furniture/master
files checked against the baseline were unchanged.

## W0 release

W0 is released for Main's review and W1 DBMS/Furniture consumption of the signatures
above. Continue to keep `ActivityHost`, `ActivitySceneHost`, `ScenarioSession`, menu
bridges and all shared Content/Authoring/Persistence sources untouched until their
assigned windows. No W2 implementation has started here.

## Review Fix Round 1

The review identified two validation gaps: a result binding could be consumed after a
branch that skipped its producer, and a bound wait duration bypassed the literal 0..300
check. `ActivitySequenceValidator` now computes bindings guaranteed on every incoming
path through the forward-only graph. Results become available only on a step's success
edge; failure and cancel edges carry only bindings already available before that step;
merge points intersect the incoming binding sets. Transition and reachability checks now
use the actual success/failure/cancel contract only (no stale generic `nextStepId`).

`ActivityResultDescriptor` carries optional numeric bounds and allowed values. A binding
is accepted only when the producer's declared range is contained by the consumer argument's
constraints. Round 1 added runtime output value checks; Round 2 below separates partial
output checks from the completed-success gate that is required before binding results.

Review-fix validation: focused ActivitySequence suite **10/10**; full portable Scenario
suite **77/77**. Unity import/compile, EditMode/Play Mode, and W2 runner integration remain
unverified and are not claimed. The implementation is released for Main's scoped re-review.

## Review Fix Round 2

The second review found that output validation accepted an empty output list even when
the current sequence step selected a producer result for binding. The API now separates
partial/nonterminal output checking from completed-success checking:

- `ValidatePrimitiveOutputs(primitiveId, outputs)` validates any outputs supplied so far
  without requiring selected results. It can be used for accepted/running statuses.
- `ValidatePrimitiveSuccessOutputs(primitiveId, outputs, selectedBindings)` is the explicit
  completed-success gate. It validates output values and requires every result selected by
  the current step's `resultBindings` to be present and declared by the primitive. A null
  binding contract fails closed; an empty contract is valid when the step selected no results.

W2 must call `ValidatePrimitiveSuccessOutputs` only after completed success and before
publishing bindings/following the success edge. Accepted/running outcomes must not be sent
through the completed-success requirement; partial values, if any, use `ValidatePrimitiveOutputs`.

Round 2 TDD evidence: the added regression first failed to compile because the explicit
success API did not exist; after implementation, the focused ActivitySequence suite passed
**11/11** and the full portable Scenario suite passed **78/78**. Tests show a missing selected
result is rejected on completed success, a present selected result is accepted, and an empty
partial result remains valid for accepted/running. The prior 0/300 accepted and 301 rejected
output checks remain green. No Unity/Editor checks or W2 runtime integration were run.
Released for Main's scoped re-review.
