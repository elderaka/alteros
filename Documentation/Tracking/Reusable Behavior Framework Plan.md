---
title: Reusable Behavior Framework Plan
date: 2026-10-22
tags: [alteros, planning, scenario, actions, activity-sequence, effects]
status: planning-only-awaiting-main-consolidation
---
# Reusable Behavior Framework Plan

> Planning-only owner proposal for the [[Reusable Content Framework Workflow]].
> No source/runtime/content/database/scene changes or tests were performed for this
> note. Main must consolidate this with the Furniture, DBMS and Workbench owner plans
> and obtain a separate implementation START before any code work.

## Goal and architecture

Give the existing Activity/Scenario host a small portable, typed sequence layer that
can run either a thin trusted C# composition or an authored data recipe through the
same validator, registry and executor. Reuse one `ActivityHost`, one `ScenarioSession`,
one conversation/presentation owner, one furniture claim owner and the DBMS game-state
writer. A new recipe using registered primitives must not need a new per-object switch
or a second execution engine.

Keep three authority layers distinct:

1. A Scenario `action` step requests a revision-pinned Action/sequence from the host.
   The Scenario owns continuation and selects its own success/failure/cancel edges.
2. The canonical ActivitySequence primitive `invoke_furniture` dispatches through a
   trusted descriptor to a registered persistent function or presentation operation
   for the exact reserved instance. Its typed result is not the sequence lifecycle.
3. A Scenario `host_effect` step asks a trusted host-effect port to apply one pinned,
   validated Action definition (for the proof, registered `player.item.grant`). The game/persistence authority performs the write and
   returns a durable receipt. It is not a furniture function, an Activity, a UI callback
   or an LLM-authored delta.

The sequence core and DTOs stay BCL-only. Unity adapters provide actor motion,
presentation and live furniture discovery. DBMS supplies the durable Item/power
adapters. Sidecar/provider contracts do not change in this slice.

## Current source seams and gaps (read-only inspection, 2026-10-22)

- `Assets/AlterOS/Activities/ActivityContracts.cs` defines `IActivityExecution`
  (`View`, `Start`, `Tick`, `Stop`, exchange suspend/resume, `Shutdown`, and
  `DrainEvents`) but also includes Unity `Vector3` in `IActivityMotion`. Preserve its
  lifecycle and serialized `ActivityExecutorKind` values; if the portable sequence
  needs a clean host build, split only pure DTO/lifecycle contracts into
  `ActivityExecutionContracts.cs` and keep Unity motion behind an adapter.
- `Assets/AlterOS/Activities/ActivityHost.cs` is the single exclusive request owner.
  `Request(ActivityRequest, ScenarioRuntimeSnapshot)` uses `(runId, stepId)` request
  identity and the configured factories, but `Supports` and `ActivitySceneHost` capture
  are currently Movement/DrinkWater-specific. Extend that registry-backed seam;
  do not add a TV host or parallel runner.
- `Assets/AlterOS/Scenarios/ScenarioSession.cs` currently turns `action` steps into
  `ActivityRequest` and advances on `activity_result`. The presentation model owns
  delivery/completion events. The typed `host_effect` step and its result continuation
  do not exist.
- Compiled `StepRecord` already has `kind`, `actionRef`, `calledFlowRef` and `dataJson`;
  `ActionRecord` has `capabilityId` and `contractJson`. Content refs and Subflow
  invocation frames are release-pinned. The current package validator accepts only
  registered Action IDs and an explicit step-kind/field vocabulary. Do not put raw
  method names, SQL, or permissive effect JSON in `dataJson`.
- `ContentHostVocabulary` currently registers Action IDs; it is not yet the proposed
  primitive/function/effect descriptor registry. `ContentPackageValidator.ValidatorVersion`
  is `content-validator-v1`; compiled content schema is v4. Extend versioned validation
  deliberately and preserve old `action` requests/releases.
- `Assets/AlterOS/Activities/ActivityGreetingMenu.cs` filters hard-coded Movement and
  Drink targets. `DirectConversationBridge.StartGreetingActivity` has a direct
  capability-to-Scenario/Activity path distinct from free-text Topic selection. Neither
  is yet the required canonical menu/free-text Topic -> Scenario entry path.
- `DrinkWaterActivity` owns Alterra's hydration checkpoints. It must not be reused
  for player Fetch. `GameCommitRequest` has progression/layout/events/checkpoint/receipt
  collections, while current player progression has no general Item stack/count.
  `GameOperationId.FromStableKey(string)` exists, but DBMS owns any Item representation,
  transaction and schema/migration decision.

These are source findings, not compile, test, Play Mode or acceptance results.

## Proposed portable contracts

Exact namespaces and final names are coordinated proposals, not existing APIs. Keep
these DTOs independent of `UnityEngine`, SQLite, provider SDKs and Workbench code.

```csharp
public interface IActivityPrimitiveRegistry
{
    bool TryResolve(string primitiveId, out ActivityPrimitiveDescriptor descriptor);
}

public sealed class ActivityPrimitiveDescriptor
{
    public string primitiveId;
    public string version;
    public string operationKind; // pure | furniture_function | presentation
    public IReadOnlyList<ActivityArgumentDescriptor> arguments;
    public IReadOnlyList<ActivityResultDescriptor> results;
    public string requiredCapabilityId;
}

public sealed class ActivitySequenceDefinition
{
    public string id;
    public string revision;
    public IReadOnlyList<ActivitySequenceStep> steps;
    public int maxStepCount;
}

public interface IActivitySequenceBuilder
{
    ActivitySequenceDefinition Build(ActivitySequenceBuildContext context);
}

public interface IActivitySequenceValidator
{
    ActivitySequenceValidationResult Validate(ActivitySequenceDefinition definition,
        IActivityPrimitiveRegistry primitives);
}

public interface IActivityPrimitivePort
{
    ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request);
    ActivityPrimitiveResult Cancel(ActivityPrimitiveCancellation cancellation);
}

public sealed class ActivityPrimitiveRequest
{
    public string scenarioRunId, flowInvocationId, stepExecutionId;
    public string actionInvocationId, sequenceStepId, invocationId, primitiveId;
    public TargetBinding target;
    public IReadOnlyDictionary<string, ActivityValue> arguments;
}

public sealed class ActivityPrimitiveStartResult
{
    public string status; // accepted | running | completed | unavailable | conflict | failed
    public string scenarioRunId, flowInvocationId, stepExecutionId;
    public string actionInvocationId, sequenceStepId, invocationId;
    public IReadOnlyDictionary<string, ActivityValue> outputs;
    public string reasonCode;
}

public sealed class ActivityPrimitiveResult
{
    public string status; // accepted | running | completed | cancelled | failed
    public string scenarioRunId, flowInvocationId, stepExecutionId;
    public string actionInvocationId, sequenceStepId, invocationId;
    public IReadOnlyDictionary<string, ActivityValue> outputs;
    public string reasonCode;
}

public sealed class ActivityPrimitiveCompletion
{
    public string scenarioRunId, flowInvocationId, stepExecutionId;
    public string actionInvocationId, sequenceStepId, invocationId;
    public string status; // completed | cancelled | failed
    public IReadOnlyDictionary<string, ActivityValue> outputs;
    public string reasonCode;
}

public sealed class ActivityPrimitiveCancellation
{
    public string scenarioRunId, actionInvocationId, sequenceStepId, invocationId;
    public string reasonCode;
}

public interface IActivitySequenceRunner
{
    ActivitySequenceStartResult Start(ActivitySequenceRequest request);
    ActivitySequenceView Tick(double nowSeconds);
    ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion);
    ActivitySequenceView Cancel(string runId, string stepId, string reason);
    ActivitySequenceView Shutdown(string reason);
}
```

`ActivityPrimitiveStartResult` returns only a start acknowledgment/outcome; a later
asynchronous terminal callback is an `ActivityPrimitiveCompletion` passed to
`CompletePrimitive`. Both DTOs carry the full run/Flow invocation/step execution/
physical Action/sequence-step/primitive invocation tuple, and the runner rejects stale
or mismatched tuples. A synchronous primitive may return `completed` immediately.

`invoke_furniture` adapts to Furniture's sole transient contract
`IFurniturePresentationPort.Start/Poll/Cancel` for descriptor-classified presentation
operations; this behavior plan does not declare a parallel presentation interface or
DTO schema. Map fields without dropping correlation: Furniture `runId` receives the
Scenario run; Furniture `invocationId` receives the Flow invocation; its
`actionDefinitionRef` receives the exact pinned Action; `activityId` receives the
physical `actionInvocationId`; `sequenceStepId` remains the inner sequence step; and
`presentationRequestId` receives the primitive `invocationId`. Scenario
`stepExecutionId` remains in Behavior's parent execution correlation and is not
substituted for Furniture's sequence step. Exact instance,
definition, use-point, point revision and reservation-owner fields pass from the target
binding. Preserve the Furniture handle unchanged for `Poll` and `Cancel`. Adapt its
`accepted/running/completed/cancelled/failed` result to the existing generic
`ActivityPrimitiveStartResult` or `ActivityPrimitiveCompletion` consumed by
`CompletePrimitive`; a stale/mismatched handle cannot advance the sequence.
`dispenser.collect_mock` returns mock presentation completion only and has no durable
furniture or Item effect.

`ActivitySequenceStep` is a typed record (`stepId`, registered `primitiveId`, bounded
typed args, typed output bindings, next/success/failure/cancel targets and optional
checkpoint policy). Bindings refer only to named outputs/validated Scenario inputs,
not reflection, arbitrary expressions, member paths or actor claims. `ActivitySequence`
may use a bounded set of portable primitives (`reserve_nearest`, `move_to`, `face`,
`bark`, `wait`, `invoke_furniture`, `move_near_player`, `checkpoint`, `complete`).
The descriptor registry declares their arguments, result types, bounds, capabilities
and version. `operationKind` separates persistent `IFurnitureFunctionPort` mutations
from Furniture `IFurniturePresentationPort` operations; `invoke_furniture` dispatches only through
the descriptor and cannot infer authority from a string argument. Use at most 64 steps,
finite configured waits no greater than 300 seconds,
and reject loops/recursion; player wait/checkpoint behavior belongs to Scenario policy,
not an unbounded primitive wait.

`IActivitySequenceBuilder` is invoked only for a registered `composerId`. The builder
must return a definition that passes the same `IActivitySequenceValidator` as an
authored data recipe. Exactly one source is present (`steps` or registered composer),
never both or neither. A C# class composes data; it does not execute independently or
skip validation. The same immutable validated sequence and runner handle both forms.

`ActivitySequenceRequest` carries pinned Action `ContentRef`, `scenarioRunId`,
`flowInvocationId`, Scenario `stepExecutionId`, `actionInvocationId`, trusted actor/
Scenario capability snapshot and typed arguments. `flowInvocationId` and
`stepExecutionId` identify durable Scenario/Flow history. Physical Action and primitive
invocation IDs are distinct and are never substituted into game-history or Item-effect
identity. Request replay with the same identity and payload returns the existing
sequence/result; the same identity with a different payload is a conflict.
`ActivitySequenceStartResult`
and `ActivitySequenceView` own only `accepted/running/completed/cancelled/failed`,
current sequence step and safe reason code. The existing `ActivityHost` remains the
sole exclusive owner and creates/adapts one lifecycle execution for a running
sequence; it rejects a second conflicting request while a Scenario/activity owns it.

The Unity-facing ports are narrow interfaces implemented in `ActivitySceneHost` and
Furniture-owned adapters:

```csharp
public interface IActivityTargetPort
{
    TargetResolution ResolveAndReserve(ResolveTargetRequest request);
    bool Revalidate(TargetBinding binding);
    void Release(TargetBinding binding);
}

public interface IFurnitureFunctionPort
{
    FurnitureFunctionResult Invoke(FurnitureFunctionRequest request);
}

public interface IScenarioHostEffectPort
{
    ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request);
}
```

Target bindings carry the exact object/instance ID, definition ID, functional-point
ID, reservation owner and applicable point/layout/instance revision snapshot. Resolve
and reserve must reuse the current Furniture registry/claim owner, and every invoke/
continuation revalidates the binding. Unavailable, busy, stale, unreachable or unknown
targets remain distinct outcomes; do not collapse unknown into “not present.”

For durable `power.set`, call the Furniture-owned `IFurnitureFunctionPort` contract
from [[Furniture Functions Framework Plan]]. Its mutation result statuses are
`applied`, `no_change`, `replayed`, `unavailable`, `conflict`, `failed`, with committed
state evidence and safe diagnostic. The sequence lifecycle remains separate. For
`dispenser.collect_mock`, the Furniture-owned descriptor classifies it as a presentation
operation and dispatches through `IFurniturePresentationPort.Start/Poll/Cancel`. The
Furniture handle correlates `accepted/running/completed/cancelled/failed` against the
active sequence using the mapping above. It is explicitly mock-labelled, changes no dispenser supply, Item quantity,
Alterra hydration, stats or currency, and never reports Furniture's durable mutation
status `applied` as proof of presentation completion.

The generic Item effect/Scenario port proposal, aligned with DBMS's provisional
2026-10-22 reply, is:

```csharp
public sealed class ScenarioHostEffectRequest
{
    public ContentRef actionRef;
    public string scenarioRunId;
    public string invocationId; // Flow invocation, never physical Action invocation
    public string stepExecutionId;
    public string requiresCompletedStepId;
    public string completedStepExecutionId;
    public long expectedGameRevision;
}

public sealed class ScenarioHostEffectResult
{
    public string status; // applied | replayed | unavailable | conflict | failed
    public string scenarioRunId, invocationId, stepExecutionId;
    public string operationId;
    public string receiptId;
    public long committedGameRevision;
    public string safeReasonCode;
}
```

This is the Behavior-owned public pure port `IScenarioHostEffectPort.Apply`; DBMS
supplies and owns the concrete Item stack/count store, transaction, event, receipt and
adapter. The host-effect Scenario Step's existing `actionRef` resolves a pinned Action
definition binding registered `player.item.grant` to an exact Item ref and positive
authored quantity (the proof fixes it to `item.water_glass`, quantity 1); `functionId`,
Item ref and quantity are resolved from that pinned trusted Action, not supplied as
permission by the request. Do not add a
duplicate `StepRecord.itemRef`. Strict version-1 step data contains only
`requiresCompletedStepId`; the compiler validates that prior Dialogue step and the
Action's Item/Text closure. The runtime resolves the actual completed execution of
that step and passes it with Scenario run ID, Flow `invocationId`, distinct
`stepExecutionId` and Action ref. Physical Action `actionInvocationId` and sequence/
primitive invocation IDs remain separate and are not persistence identity. Provider/
model output cannot choose arbitrary effects or mint amount. DBMS's provisional adapter extends `GameCommitRequest` with typed
`GameItemQuantityChangeRecord` and atomically validates matching save/run/Flow
invocation and completed dialogue prerequisite before committing quantity, event,
event context and receipt. Final field names remain DBMS/Main-owned.

The consolidated master sets a planned `ItemRecord` for `item.water_glass` in compiled
content schema v5, with localized `TextRecord` ref and `stackLimit = 99`, plus additive
game schema v3-to-v4 table `player_item_quantities(player_id,item_id,quantity > 0)`;
absent row means zero. The proposed owner identity is
`legacyPlayerId ?? accountId ?? saveId`; progression JSON remains unchanged. These are
implementation defaults, not current source facts. Behavior must not edit DBMS files
or guess the migration/API. Its idempotency identity is stable over save ID, grant kind,
Scenario run ID, Flow invocation ID, Scenario step-execution ID, Action ID, and Item ID/revision via
the DBMS-approved operation/receipt mechanism. Same occurrence retries do not add a
second glass; a distinct new run/step occurrence may grant again.

## Scenario and content representation

Keep authored behavior composable while preserving legacy flows:

| Layer | Proposed representation | Authority and result |
| --- | --- | --- |
| Scenario `action` step | Existing kind with a pinned Action ref; legacy compiled Actions migrate to `implementationKind=legacy_activity` | `ScenarioSession` asks the existing Activity port/host to start the validated `activity_sequence`, then advances on correlated physical Action terminal result |
| Sequence primitive | Canonical `invoke_furniture` plus registered descriptor/function ID, typed args and exact target binding | Furniture handler validates function/definition/use-point/instance/revisions/claim; dispatches persistent `power.set` or a descriptor-classified presentation operation |
| Scenario `host_effect` step | New explicit kind with pinned `actionRef`; strict version-1 step `dataJson` contains `requiresCompletedStepId` only | Host resolves `implementationKind=host_effect` and the registered `player.item.grant` Action's Item/quantity binding; DBMS validates actual completed execution and commits with receipt; no UI callback or duplicate `StepRecord.itemRef` |
| Dialogue | Existing pinned `dialogueRef`/Subflow reference and completion event | Scenario advances only after the existing presentation owner confirms completion, not enqueue/show start |
| Topic entry | Existing Topic `flowRef`/Scenario reference and selected revision | Same selector/eligibility and ready release for menu and free text |

New `action_definition` schemaVersion1 has stable `id`, `capabilityId`,
`implementationKind`, and descriptor-validated data. `implementationKind=activity_sequence`
requires exactly one source (`steps` XOR registered `composerId`); both normalize to the
same validated sequence. `implementationKind=host_effect` accepts only a registered
`functionId` and typed arguments. `legacy_activity` is reserved for migrated compiled
Actions; new source cannot claim it. Do not introduce `data_recipe` or a second
`scriptId` discriminator.

Proposed runtime fields for Scenario `action`: `actionRef`, typed input bindings,
`successNextStepId`, `failureNextStepId`, `cancelNextStepId`. New `host_effect` uses
`actionRef` and strict version-1 step data `requiresCompletedStepId`; its referenced
host-effect Action binds typed Item ref and quantity. The Action is not itself
permission: the installed host registry must recognize `player.item.grant`, and the
selected release must contain the referenced Item.
Validator rejects absent/unpinned refs, unregistered descriptors, invalid typed
bindings, duplicate IDs, impossible transitions, effect before required delivery
completion and invalid quantities. It never accepts executable C#, method names or a
free-form `effectJson` as permission.

The exact producer decisions are now in [[Reusable Content Framework Workflow#Consolidated producer contract decisions]].
Scenario host-effect uses existing `StepRecord.actionRef`; strict version-1 `dataJson`
names only `requiresCompletedStepId`; no duplicate mutable `StepRecord.itemRef`. DBMS
owns content schema v5 and game schema v4 migrations. Keep prior releases
compatible/explicitly diagnosable and add validator/contract version plus round-trip
tests before publishing. New step semantics must not reinterpret legacy `action` steps.

## Required three flow compositions

All are full Topic-to-Scenario flows, not direct furniture commands. Anchors are
authored on the Topic and baked from the exact selected SQL snapshot/release; no string
shortcut, hardcoded phrase branch or provider requirement. Existing favor menu must
resolve the same Topic and eligible Scenario as free-text selection.

### Turn on TV

Topic anchor set includes required typo `Can you hlep me turn on the tv` and corrected
`Can you help me turn on the tv?`. `Can you do me a favor?` then `Turn on the TV` is an
explicit menu choice for the exact same canonical Topic; the menu is not permission
bypass and must not call an Activity directly.

```text
dialogue(opening)
action(actionRef = pinned turn-on-TV sequence)
    reserve_nearest(capability = compatible registered TV function/use-point)
    move_to(exact target binding)
    invoke_furniture(functionId = power.set, desiredState = On)
    require result = applied | no_change | valid replayed committed-On
end
```

Terminal success occurs immediately after the exact target has durable known-On state
and its view projects committed state. No mandatory move-near-player or closing VN.
Missing/busy/stale/unreachable TV follows an authored truthful failure path; it must not
claim success or turn the view On. An already-On TV is `no_change`, not an extra event.

### Locate TV

Topic anchor `Where is the tv?` starts the same Scenario selection/session framework,
but not the power operation:

```text
action(actionRef = pinned locate-TV sequence)
    reserve_nearest(capability = reachable TV approach)
    move_to(exact target binding)
    bark(authored localized dialogue/text ref = "Hey, it's here")
    require correlated bark-delivered result
end
```

No power read/write is needed to claim arrival; no return is forced. If bark delivery is
suppressed, do not record it as delivered. No eligible TV or a failed move takes an
authored truthful fallback, not a success bark.

### Fetch a drink

Topic anchor `Can you fetch me a drink?` runs:

```text
action(actionRef = pinned fetch-drink sequence)
    reserve_nearest(capability = reachable dispenser collection use-point)
    move_to(exact target binding)
    invoke_furniture(functionId = dispenser.collect_mock)
        require correlated mock-presentation completion
    move_near_player()
    complete
dialogue(delivery VN = "Here, cheer!")
host_effect(actionRef = pinned Action binding player.item.grant,
            data = { version: 1, requiresCompletedStepId: "delivery" })
    require applied | replayed receipt
end
```

The grant happens only after the dialogue owner's completed event. Queued, opened or
visible-but-not-completed dialogue does not authorize it. Failure/cancel/quit before
the host-effect commit yields no item; cancellation after durable commit does not
rollback it. Retrying the same run/step cannot mint another glass; a new legitimate
Scenario occurrence can. `collect_mock` grants no Item and does not call
`DrinkWaterActivity`/hydration checkpoints. No drink count, thirst or other stat change
is implied for Alterra.

## Target/result binding, checkpoints and interruption

- Every result has an operation correlation chain: `ScenarioRunId`, `ScenarioStepId`,
  unique `InvocationId`, `SequenceStepId`, exact target binding where relevant, and
  durable operation/receipt ID only for persistent effects. Reject out-of-order,
  stale, wrong-target and wrong-run callbacks.
- Validate the entire sequence and required primitive/function descriptors before
  beginning. Revalidate capability, target reservation and relevant revisions before
  each effect. Release reservations exactly once on terminal/cancel/failure/shutdown.
- Activity host owns accepted/running/terminal lifecycle and `CanTalk`/`CanPause`
  projection. Scenario owns dialogue/action/effect order and transition edges. Furniture
  owns its function mutation status. DBMS owns committed state and receipt. UI only
  presents events; it does not commit effects.
- Persist a checkpoint before a durable side effect and a durable receipt atomically
  with that effect. A resumed run queries the receipt before re-executing. No duplicate
  Item or furniture state event on replay. Pending movement/mock presentation may be
  cancelled and retried only after the host resolves its invocation identity.
- Scenario cancel/failure/quit before commit follows its explicit cancel/failure edge
  or ends cleanly with reservation release and no effect. After a confirmed commit,
  retain the receipt and committed state; never claim rollback. Unknown commit outcome
  is reconciled by operation receipt before another attempt. A stale callback cannot
  advance a different run.
- Pose safeguard remains outside these action flows: only after both the owning
  interaction and Scenario end may the +30-second stale-pose timer restore default
  pose. It never restores actor/player position, forces a return, or releases
  safety-critical reservations late. New ownership invalidates the old timer; an active
  Scenario blocks it even with dialogue UI closed. Fetch's return is its explicit
  `move_near_player` primitive; TV and Locate end at their own declared terminal state.

## Canonical menu and free-text dispatch

Add one pure `ScenarioEntryResolver` (proposed) used by both the existing direct
conversation router and `ActivityGreetingMenu`:

```csharp
public interface IScenarioEntryResolver
{
    ScenarioEntryResolution Resolve(ContentRef topicRef, ScenarioLaunchContext context);
}

public sealed class ScenarioLaunchRequest
{
    public ContentRef topicRef;
    public ContentRef scenarioRef;
    public string entryId;
    public string selectedBy; // free_text | explicit_menu
    public IReadOnlyDictionary<string, string> validatedInputs;
}
```

Free text first passes through normal Topic routing; menu options identify a Topic ref
and optional label only. Both then call the same resolver, eligibility checks, pinned
Topic/Scenario entry and `ScenarioSession.Start(ContentExecutionPlan, runId, context)`.
No `capabilityId -> StartGreetingActivity -> standalone Activity` fast path may bypass
the Scenario. The selector remains authored-first; no new LLM call is needed for the
three fixtures. The exact anchor phrase set and negative phrases are written/baked by
the Topic owner from the selected SQL snapshot; the Behavior tests assert that the menu
and anchor routes produce the same pinned Scenario ref/run contract.

## Ownership and sequential writer windows after START

Use Main's consolidated windows from the current [[Reusable Content Framework Workflow#Writer windows after an explicit implementation START]]. These are sequential
ownership gates, not permission to edit early:

| Window | Sole writer | Behavior dependency/release check |
| --- | --- | --- |
| W0 | Behavior: portable Activity/Scenario contracts, sequence DTO/descriptors and pure tests | Keep portable contracts Unity-free; Furniture/DBMS acknowledge exact signatures before release |
| W1 | DBMS owns shared Authoring/Content/Persistence models, validators, migrations/readers/writers; Furniture owns only its function/state adapters | Behavior does not edit shared core files; use DBMS's lossless source/release and persistence APIs |
| W2 | Behavior: ActivityHost/ScenarioSession integration, launch resolver and conversation/menu bridges | Consume released W1 APIs; prove three pure traces, C#/data parity, cancel/replay/late callback and completed-VN effect behavior |
| W3 | Topic Playground alone owns every `Tools/ContentWorkbench/**` path | Behavior supplies stable pure contracts/fixtures; does not edit Workbench source/tests |
| W4 | Furniture owns the acknowledged Unity Editor/scene/prefab presentation window | Behavior and DBMS are read-only during Editor work |
| W5 | Main coordinates selected synthetic fixture bake/publication and startup/gameplay acceptance | Explicit release selection and evidence ledger; no private profile/provider access inferred |

Before W0, each owner acknowledges exact proposed paths; this table is not a live
lock. Main owns shared status notes and the final cross-owner reconciliation.

### Behavior-proposed source boundary

| Work | Proposed Behavior-owned paths | Owner boundary |
| --- | --- | --- |
| Portable lifecycle/contracts | `Assets/AlterOS/Activities/ActivityExecutionContracts.cs` (split only BCL types currently in `ActivityContracts.cs`) | ActivityHost keeps exclusive lifecycle; Unity `IActivityMotion` remains an adapter |
| Sequence definition/build/validation/registry | `ActivitySequenceDefinition.cs`, `ActivityPrimitiveRegistry.cs`, `ActivitySequenceValidator.cs`, `ActivitySequenceBuilder.cs` under `Assets/AlterOS/Activities/` | No per-content switches; same descriptors in C# and data path |
| Runner and ActivityHost integration | `ActivitySequenceRunner.cs`; focused change in `ActivityHost.cs`, `ActivitySceneHost.cs` | Preserve old Movement/Drink execution and event compatibility |
| Scenario host-effect DTO/dispatcher | `Assets/AlterOS/Scenarios/ScenarioHostEffectContracts.cs`, focused `ScenarioSession.cs` / compiled-flow adapter changes | DBMS owns concrete inventory port/persistence; no raw SQL here |
| Canonical Scenario launch | `Assets/AlterOS/Topics/ScenarioEntryResolver.cs`; thin bridge/menu adapters after owner approval | Topic/menu selection and UI stay with their current owners; no second runner |
| Focused tests | `Assets/AlterOS/Tests/Editor/Activities/ActivitySequence*Tests.cs`, `.../Scenarios/ScenarioHostEffect*Tests.cs`, `ScenarioEntryResolverTests.cs` | Pure tests where possible; Editor integration tests for Unity ports |

Furniture owns `Assets/AlterOS/Furniture/FurnitureFunctions.cs`, typed function
registry/handlers and state presentation. DBMS owns game persistence, Item count,
receipts and any migration. Topic Playground alone owns every
`Tools/ContentWorkbench/**` file and its command/UI/test adapters. Main owns shared
contract/status/docs consolidation and grants implementation START. Do not start a
shared Activity/Scenario/Editor file until the actual sole writer and handoff window
are acknowledged.

## Bite-sized implementation/test plan (after consolidated START)

1. **W0 — Pure contracts first.** Add sequence DTOs, descriptors, builder, registry and
   validator; write red/green BCL tests for duplicate/missing IDs, invalid/unknown
   argument types, unsupported capabilities/functions, excessive steps/waits, illegal
   transitions, source exactly-one rule, and equivalent composer/data definitions.
   Confirm legacy Movement/Drink requests still map to their existing factories.
2. **W0 — Host-independent lifecycle contract.** Finalize the ActivityHost adapter
   surface and exact owner signatures; do not modify shared host/Scenario integration
   until W2. Test identity-level contracts with fakes in the portable harness.
3. **W1 — Furniture/DBMS handlers.** Furniture implements target/function/presentation
   handlers; DBMS implements the typed durable Item/power adapters and migrations.
   Use fake/pure contract tests for exact binding, revisions, power results, mock
   non-mutation, transactional receipt/replay and completed-step prerequisite. Behavior
   does not edit shared Content/Authoring/Persistence files during W1. Add a
   `FurniturePresentationCorrelationAdapterTests` check that the sole port's full
   mapping survives Start/Poll/Cancel: Furniture `runId` <- Scenario run,
   `invocationId` <- Flow invocation, pinned `actionDefinitionRef`, physical
   `activityId`, inner `sequenceStepId`, primitive `presentationRequestId`, exact
   target and use-point revision. Verify Behavior retains its Scenario
   `stepExecutionId` separately and declares no second presentation interface/DTO
   schema.
4. **W2 — Host lifecycle adapter.** Adapt one validated sequence to the existing exclusive
   ActivityHost request/event stream. Test idempotent `(scenarioRunId, flowInvocationId,
   stepExecutionId, actionInvocationId)`
   same-payload replay, changed-payload conflict, concurrent owner rejection, stale
   callbacks, interruption/quit, cleanup/release once, and bounded tick progression.
5. **W2 — Scenario action/effect integration.** Add the explicit Scenario `host_effect` contract,
   validator and result transition. Fake the DBMS port and test receipt before retry,
   operation-ID payload conflict, exactly one quantity +1 on duplicate, separate new
   run yields another +1, no grant before VN-completed, no grant on cancellation/failure
   before effect, durable commit survives later cancel, unknown commit reconciles from
   receipt, and invalid/unregistered Action function/Item/quantity fails closed.
6. **W2 — Menu/Topic parity.** Test exact typo and corrected TV anchor route through the same
   pinned Topic/Scenario entry as `Can you do me a favor?` -> `Turn on the TV`. Assert
   one `ScenarioSession` run, no direct Activity path, no duplicate transcript writer,
   and truthful no-resource / busy / stale fallbacks. Bake only from an explicitly
   selected SQL snapshot and compare its route mapping/hash in the test fixture; never
   make exact strings a runtime bypass.
7. **W2/W3/W5 — Flow traces and gated handoff.** Feed all three flows a synthetic snapshot
   and assert exact ordered trace/terminal: TV On then end (no return/closing VN);
   Locate arrival -> delivered bark -> end (power unchanged); Fetch mock collect ->
   explicit return -> automatic VN completed -> receipt-backed `item.water_glass` +1
   -> end. Add a second non-TV Action and a second Item definition to show no hardcoded
   executor/reward path. Only after pure suites pass should Behavior hand to Furniture/
   DBMS/Workbench for adapter tests and Main's approved Unity acceptance.

Proposed focused commands after implementation (not run in this planning task):

```powershell
# Portable Workbench contract/preview regression after owner API integration
Tools/ContentWorkbench/build-core.ps1
python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_commands.py' -v

# Focused Unity EditMode tests after re-import/compile; use the installed Unity
# 6000.4.4f1 Editor path and an isolated project process, then inspect test XML/log.
Unity.exe -batchmode -projectPath E:\Pleiades\alteros -runTests -testPlatform EditMode `
  -testFilter 'ActivitySequence;ScenarioHostEffect;ScenarioEntryResolver' `
  -testResults <isolated-temp-path> -logFile <isolated-temp-path> -quit
```

The exact Unity executable/test filter/paths must be confirmed from the current host
and test assembly names before execution. Commands above are proposals, not run results.
Later runtime acceptance must separately use `SplashScene -> ModelTest`, a safe
synthetic profile and explicit Unity/visual/player evidence. Headless preview is not
Play Mode proof.

## Remaining implementation gates

1. **Action/effect representation:** follow the consolidated master contract:
   `activity_sequence` uses `steps` XOR registered `composerId`; `host_effect` uses a
   registered `functionId` and typed arguments; existing compiled Actions become
   `legacy_activity` and new source cannot claim it. Scenario host-effect uses
   `StepRecord.actionRef` plus strict version-1 `requiresCompletedStepId` data; no
   duplicate `StepRecord.itemRef` or free-form effect authorization.
2. **Function result split:** follow the consolidated master contract:
   `dispenser.collect_mock` is a descriptor-classified presentation operation using
   Furniture's correlated `IFurniturePresentationPort`; canonical primitive ID is
   `invoke_furniture`. Persistent mutation statuses remain
   `applied/no_change/replayed/unavailable/conflict/failed`; sequence lifecycle remains
   `accepted/running/completed/cancelled/failed`.
3. **Invocation/receipt identity:** DBMS provisionally proposes
   `GameItemQuantityChangeRecord` in the existing transactional commit/receipt path,
   with `GameOperationId` bound to save/run/Flow invocation/Scenario stepExecutionId/
   Action/Item identity. Physical Action and sequence invocation IDs are separate.
   DBMS owns durable state/event layout and migration; Main confirms final payload and
   completed-step prerequisite.
4. **Canonical entry wiring:** W2 Behavior owns the resolver and bridge/menu adapter
   integration; it consumes the released Topic selector/entry and selected snapshot.
   It must not duplicate Topic routing or use a direct Activity shortcut.
5. **Source handoff:** obey W0-W5 above. Before W0 and each later window, confirm exact
   paths and explicit owner acknowledgment; the table is not itself a live file lock.
   DBMS retains shared Authoring/Content/Persistence schema/compiler/validator files;
   Topic Playground retains every Workbench file.
6. **Validation:** run only after an approved implementation start. This planning
   pass did not run tests, build, Unity import/Editor, provider, database, profile or
   scene operations.

