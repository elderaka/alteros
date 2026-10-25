---
title: Furniture Functions Framework Plan
date: 2026-10-22
tags:
  - unity
  - furniture
  - reusable-content
status: source-ready-integration-pending
---

# Furniture Functions Framework Implementation Plan

> **Status (2026-10-23):** Main issued W1B source START after accepting the W1A
> port/handler release. F1 catalogue metadata and validation, F2 Furniture-owned
> target adapters, and the F3 committed-state projection source are implemented
> and reported for review. A public single-definition validation seam shares the
> catalogue's geometry, category, support, point/use and binding rules without
> requiring starter-catalogue completeness. Power requests and durable commands
> preserve the containing Scenario Flow `stepExecutionId` separately from the
> physical `activityId` and inner `sequenceStepId`. The schema-1 catalogue remains
> backward compatible; absent opt-ins grant no function. Focused catalogue and
> function tests pass 18/18 and 19/19; the full suite was not rerun per Main's
> instruction to avoid repeating accepted tests.
> DBMS durable power storage, Behavior's live `FurnitureRegistry.IsValid` context
> implementation, and SQL fixture/catalogue integration remain pending. A normal
> Unity full-assets refresh registered the missing `ReusableContentDefinitionAdapter`
> source and cleared the compile errors; the Editor is idle with zero Console
> errors. Unity tests and Play Mode were not run. Scene view wiring and live
> acceptance remain pending. See [[../Verification/2026-10-23
> Furniture Functions W1B F1-F3]]. The master workflow and Main retain cross-domain
> ownership and later Editor release coordination.

> **Worker:** Use the `executing-plans` skill task by task after implementation
> START. Keep each work package with its named writer and use the shared Activity
> and persistence owners for their ports.

**Goal:** Add a small trusted furniture-function contract that lets validated
Actions invoke explicitly registered operations on one exact, reserved furniture
instance. Prove durable per-TV power state, target isolation and visible state
projection while keeping the shared Activity/Scenario runner, content definitions,
game save and player interaction owners intact.

**Architecture:** The Activity/Scenario host owns sequence lifecycle, actor movement,
reservation and continuation. Furniture owns typed furniture metadata, exact-target
binding, the registered furniture-function adapter and state presentation. DBMS owns
durable commands, item counts, events, receipts and migrations. The Workbench owns
authoring revisions and preview through its existing typed commands; Topic Playground
owns `Tools/ContentWorkbench/**`. Content may name a registered function and provide
its typed arguments, but cannot install a handler, choose arbitrary resource keys,
call reflected methods or write save data.

**Tech Stack:** Unity 6000.4.4f1, existing C# runtime and furniture catalogue,
ActivityHost/Scenario execution, `IGameRepository`/SQLite receipt path, and the
existing typed authoring and Workbench command host. No new package or runner.

**Spec:** [[Reusable Content Framework Workflow]],
[[Scripted Actions and Inspect TV Design]],
[[Scripted Actions and Inspect TV Implementation Plan]],
[[Furniture Mechanics and Catalogue Design]],
[[Content Workbench Shared Contract]], and
[[Multi-Agent Coordination]]. The workflow and Action plan own cross-domain
acceptance and execution order; this document scopes only the furniture boundary.

## Current source facts

Read-only source inspection on 2026-10-22 found:

- `Assets/AlterOS/Activities/ActivityContracts.cs` currently registers Movement
  and DrinkWater executor kinds. `Assets/AlterOS/Activities/ActivityHost.cs`
  statically pairs those with ComeHere/FollowPlayer and DrinkWater capabilities.
  `ActivitySceneHost.BuildHost` installs only Movement and DrinkWater factories.
  No general `ActivitySequence` or furniture-function invocation API exists in
  source.
- `Assets/AlterOS/Activities/FurnitureRegistry.cs` already owns exact
  `FurnitureUsePoint` reservations and `FurnitureBinding` identity. Use that one
  claim owner. Do not add a second reservation registry or bypass
  `FurnitureSceneAdapter` revalidation.
- `Assets/AlterOS/Furniture/FurnitureCatalogue.cs` has functional-point and
  capability descriptors. They describe geometry/use requirements; they do not
  register executable handlers. `furniture.tv.basic` currently has a viewing
  point and `activity.watch_tv` metadata, neither of which makes it executable.
- `FurnitureCatalogueRegistry.CreateSnapshot` currently accepts only catalogue
  schema 1 with the existing units and coordinate convention. Function metadata
  must preserve explicit schema-1 handling and fail closed on unsupported versions.
- `Assets/AlterOS/Furniture/HomeFurnitureLayout.cs` has generic per-instance
  `resourceState`, but no typed `device.power` contract. Existing layout commands
  handle placement transforms and removal, not a typed state mutation. The live
  world projection does not yet bind committed power to an instance's screen or
  indicator.
- `Assets/AlterOS/Persistence/GamePersistenceContracts.cs` currently sends
  progression/layout JSON, events and existing typed history collections through
  `GameCommitRequest`; `IFurnitureHomeStore` is adapted through the existing game
  repository. No general player Item model or item quantity/grant source was found.
  `HomeFurnitureLayoutDocument.inventory` is finite furniture stock, not a player
  Item inventory.
- `Assets/AlterOS/Authoring/AuthoringContracts.cs` builds `Supported` from all
  `SourceKindValues`, then adds `topic` and `scenario`. Thus
  `IsSupported` includes the listed generic dialogue, Topic/Scenario definitions
  and auxiliary source kinds. The general Furniture, Item and Action kinds are
  still absent. The Workbench contract reports its furniture catalogue read as
  unavailable. Do not enable CRUD or label these kinds supported until their
  typed authoring, validation and release paths exist.
- `Assets/Scripts/Furniture/FurnitureHandlingController.cs` currently permits a
  carpet to overlap placed furniture in either order. The named Editor regression
  `CarpetMayLayerWithFurnitureInEitherPlacementOrderButNotThroughWallsOrActors`
  checks both placement orders and wall/actor rejection. These are source/test
  facts only; this planning pass did not run that test.

These facts describe inspected source, not a compile, Editor, Play Mode or player
acceptance result. The registered-function, saved-state and visible-TV contracts
below remain proposed work.

## Scope and proof cases

Implement the Furniture-owned contracts needed by the master workflow's three
proofs:

| Proof | Furniture responsibility | Required terminal behavior |
| --- | --- | --- |
| Turn on TV | Resolve and reserve one compatible TV; invoke registered `power.set` against that exact instance; persist and project its power state | Exact TV commits On and visibly changes. The Scenario ends after successful power mutation; no required return or closing dialogue. |
| Locate TV | Supply a reachable exact target and reservation to the shared host | Host moves Alterra to it, delivers the authored non-modal `Hey, it's here` bark, then ends. No power mutation or forced return. A suppressed bark is not a delivered bark. |
| Fetch a drink | Supply a reachable dispenser target for the mock collection/presentation operation | The sequence performs the mock collection, returns to the player, completes the `Here, cheer!` VN, then separately grants one real `item.water_glass`. No Alterra hydration. |

The mock collection is not a durable dispenser-state mutation and does not grant an
Item. The master workflow fixes `dispenser.collect_mock` as a descriptor-classified
presentation operation. It starts, polls and cancels through the narrow transient
port below. Its operation result correlates to the active sequence and has the
statuses `accepted`, `running`, `completed`, `cancelled` and `failed`. It has no
durable `applied` result and never changes dispenser stock, Item quantity, hydration,
stats or currency. Alterra Behavior consumes the same request/handle/result shape;
it owns the containing ActivitySequence lifecycle, not a second presentation runner.

The registered power mutation follows the master Action plan's proposed contract:

```csharp
public interface IFurnitureFunctionPort
{
    FurnitureFunctionResult Invoke(FurnitureFunctionRequest request);
}

public sealed class FurnitureFunctionRequest
{
    public string functionId;
    public string instanceId;
    public string definitionId;
    public string usePointId;
    public string useId;
    public int usePointRevision;
    public string reservationOwnerId;
    public long expectedInstanceRevision;
    public long expectedLayoutRevision;
    public long expectedGameRevision;
    public string runId;
    public string invocationId;       // containing Flow invocation
    public string stepExecutionId;    // containing Scenario Flow step execution
    public string activityId;         // physical Activity/Action invocation
    public string sequenceStepId;     // inner ActivitySequence step
    public string operationId;
    public FurnitureFunctionArguments arguments;
}

public sealed class FurniturePowerCommand
{
    public string operationId;
    public string runId;
    public string invocationId;       // containing Flow invocation
    public string stepExecutionId;    // containing Scenario Flow step execution
    public string activityId;         // physical Activity/Action invocation
    public string sequenceStepId;     // inner ActivitySequence step
    public string instanceId;
    public string definitionId;
    public string useId;
    public long expectedInstanceRevision;
    public long expectedLayoutRevision;
    public long expectedGameRevision;
    public FurniturePowerState desiredState;
}

public interface IFurniturePresentationPort
{
    FurniturePresentationResult Start(FurniturePresentationRequest request);
    FurniturePresentationResult Poll(FurniturePresentationHandle handle);
    FurniturePresentationResult Cancel(FurniturePresentationHandle handle);
}

public sealed class FurniturePresentationRequest
{
    public string presentationRequestId;
    public string presentationDescriptorId; // fixed to dispenser.collect_mock
    public string runId;
    public string invocationId;       // containing Flow invocation
    public ContentRef actionDefinitionRef; // exact pinned Action revision
    public string activityId;         // physical Activity/Action invocation and reservation owner
    public string sequenceStepId;     // step within the ActivitySequence
    public string instanceId;
    public string definitionId;
    public string usePointId;
    public int usePointRevision;
}

public sealed class FurniturePresentationHandle
{
    public string presentationRequestId;
    public string presentationDescriptorId;
    public string runId;
    public string invocationId;
    public ContentRef actionDefinitionRef;
    public string activityId;
    public string sequenceStepId;
    public string instanceId;
    public string definitionId;
    public string usePointId;
    public int usePointRevision;
}

public sealed class FurniturePresentationResult
{
    public FurniturePresentationHandle handle;
    public string status;             // accepted|running|completed|cancelled|failed
    public string diagnosticCode;
}
```

The exact namespace/argument DTO names are planned interfaces, not current code.
Adapt the typed arguments to the final shared Action invocation contract instead of
creating a second argument system. Required function result statuses for persistent
power mutation are `applied`, `no_change`, `replayed`, `unavailable`, `conflict`,
and `failed`, plus committed state evidence and a safe diagnostic code. These are
not presentation statuses. The presentation handle is created once by the host for
the `invoke_furniture` step; retries of `Start` with the same ID and identical
payload return that operation's current result. `Start` may return `accepted`,
`running`, `completed` or `failed`; `Poll` returns the current status; `Cancel` may
return `running` while cancellation is pending, then `cancelled`, `completed` if
completion won the race, or `failed`. Every method matches the whole handle and the
one active ActivityHost sequence. Stale or mismatched handles cannot affect another
activity; late completions after cancellation are ignored. No presentation status
is persisted as a furniture mutation. The containing sequence maps operation
completion/failure/cancellation into its own lifecycle and cleanup.

Correlation is deliberately split: `invocationId` is the containing Flow
invocation, `stepExecutionId` is its Scenario Flow step execution,
`actionDefinitionRef` pins the authored Action revision, `activityId` identifies the
physical Activity/Action invocation (and is the furniture reservation owner), and
`sequenceStepId` identifies the inner ActivitySequence step. The persistent power
request and command carry both Flow IDs alongside the physical IDs so the durable
event and receipt preserve the originating step. Never substitute the inner
`sequenceStepId` for the containing `stepExecutionId`. The full
`presentationRequestId`/handle correlates start, poll and cancel to that exact
invocation and sequence step. The
presentation adapter/contract is Furniture-owned and BCL-only under the planned
`Assets/AlterOS/Furniture/FurniturePresentationContracts.cs`; Behavior owns only
the shared sequence that calls it.

### Power state and authority

- Register only the typed function `power.set` for this proof, with
  `desiredState = Off | On`. Store it as the known numeric resource
  `device.power` (`0 = Off`, `1 = On`) through the approved durable furniture
  state contract. Missing/unknown remains unknown, not Off; invalid known values
  fail closed.
- Do not repurpose `enabled` or `conditionState` as power. A catalogue/action
  declaration only opts a definition into a registered function; trusted C#
  registration plus exact binding, capability checks and persistence authorize
  execution. No model-name switch, reflection or caller-selected resource IDs.
- Separate the functional-point revision from the saved instance/layout/game
  revisions. Check the current exact instance, definition, use point, reservation
  owner, revisions and function compatibility at invocation and commit time.
- An already-On result is `no_change` and emits no power-change event. A retry of
  the same operation and payload returns `replayed`; reusing an operation ID with
  a different payload conflicts. A stale binding, removed target or unregistered
  function never becomes a successful no-op.
- The host may continue only after `applied`, `no_change` or an idempotent
  `replayed` result with valid committed evidence. Failure/conflict cannot produce
  a success line or turn the TV's view on. Once the power commit is durable, later
  cancellation does not roll it back.

### Placement and interaction constraints retained

- Snapped placement remains room-local and bounded by room/support/physics
  evidence. Rugs may layer under or over furniture in either placement order;
  walls and actors still block placement. Current source and a named regression
  already express this rule. Keep it separate from function authorization and
  include the existing regression in later live-placement acceptance.
- Existing user controls remain T for Player interaction, E for inventory when
  empty-handed/returning held furniture, and the single shared F5 Debug Mode
  owner. Furniture functions do not add listeners or change those bindings.
- The previously requested interaction cone/furniture shimmer is relationship
  presentation, not proof that a function can run. It remains an adjacent
  Furniture and Environment task, outside the reusable-content acceptance gates.
  Do not infer a relationship or shimmer merely from a function declaration.

## Architecture and ownership map

| Work | Writer and paths | Boundary |
| --- | --- | --- |
| Furniture function declarations/registry/handler | Furniture and Environment: planned `Assets/AlterOS/Furniture/FurnitureFunctions.cs`, `FurniturePowerFunction.cs`; existing catalogue DTO/validator/registry and `FurnitureSceneAdapter`; focused Editor tests | Typed metadata and exact furniture binding; no second Activity or persistence runner. |
| Activity sequence invocation/continuation | Alterra Behavior: its owned ActivityHost, Scenario, sequence and presentation files listed in the main Action plan | Calls the Furniture port through a host-owned adapter; owns sequence lifecycle and actor movement. |
| Durable furniture power / Item count grant | DBMS: its owned `FurniturePersistenceAdapter`, `GamePersistenceContracts`, `SqliteGameRepository` and migration/receipt path | Reuse current game writer, CAS, typed event and receipt. Item quantities use `GameCommitRequest.itemQuantityChanges` and the game-v4 `player_item_quantities` table. Behavior owns the public Scenario host-effect port. |
| Visible TV power state | Furniture and Environment: planned `Assets/Scripts/Furniture/FurniturePowerStateView.cs`, snapshot projection and Editor-authored test fixture bindings | Per-instance projection from durable state; static presentation references remain authored in Unity. Obtain exclusive Editor ownership before serialized changes. |
| Workbench Furniture/Item/Action adapters | Topic Playground only: all `Tools/ContentWorkbench/**` paths | Consume the shared typed commands, validation and preview contract. This plan does not edit Workbench files. |
| Cross-domain order and contract | Main: [[Reusable Content Framework Workflow]] and shared task coordination | Consolidates owner plans and authorizes implementation START. |

This table does not grant cross-owner write permission. Before touching a shared
Activity, Scenario, persistence, authoring or Editor path, confirm the sole writer,
exact release point and any pending shared-worktree changes in
[[Multi-Agent Coordination]].

## Global constraints

- Keep shared ActivityHost/Scenario lifecycle, dialogue/transcript ownership,
  SQLite writer, furniture claim registry, and player inventory ownership single.
  No parallel runner, transcript writer, save store or raw SQL path.
- Only trusted registered C# handlers perform function execution. Never deserialize
  or reflect arbitrary method/type names from authored data, content, or LLM output.
  Provider consent, capability checks and host authority remain unchanged.
- Preserve schema-1 furniture catalogue reads, home schema-2 layout/inventory,
  stable furniture IDs and unknown resource values. The planned DBMS game-v3→v4
  migration adds `player_item_quantities` for Items; it is not required merely to
  register furniture `device.power`. Any version change follows the DBMS-approved
  migration path; no live player-save migration in a synthetic proof.
- Furniture layout, room-local transforms, snapping, supports, collision/access
  checks and active-use reservations remain authoritative. The rug-layering rule
  does not permit overlap with walls or actors or bypass other safety checks.
- Preserve T interaction, E furniture inventory/held-item control, and the one
  shared F5 Debug Mode owner. Normal finite stock remains available with Debug
  Mode off; debug supply cannot skip reservation, placement or function checks.
- Static HUD/UI and furniture presentation structure must be Editor-authored on
  Canvas objects or prefabs. Runtime code may bind state to those authored
  references. No hand-edited Unity YAML/meta, package installation, secrets,
  provider spend, or unapproved Editor/Play Mode action.
- This note is the only deliverable in the planning wave. Main owns any shared
  roadmap/status update and implementation START coordination.

## Implementation sequence after explicit START

### F0 — Reconcile contracts before source changes

- [ ] Follow the consolidated contract and owner windows in
  [[Reusable Content Framework Workflow]]. No runtime work starts from this
  document alone; Main coordinates the explicit implementation START.
- [ ] Alterra Behavior consumes the exact `IFurniturePresentationPort` DTOs above.
  It owns ActivitySequence lifecycle and passes through Flow invocation, pinned
  Action, physical Activity and sequence-step correlation unchanged. Persistent
  power mutation continues to use the separate `FurnitureFunctionResult` contract.
- [ ] Reuse the consolidated Scenario effect boundary:
  `IScenarioHostEffectPort.Apply(ScenarioHostEffectRequest)`. Its request carries
  `actionRef`, `runId`, Flow `invocationId`, effect `stepExecutionId`, strict
  `requiresCompletedStepId`, and the exact completed delivery execution ID resolved
  by the host. The pinned host-effect Action owns `player.item.grant`, `itemRef`,
  and positive `quantity`; do not put mutable Item refs/counts on the port request.
  `ScenarioHostEffectResult` reports the stable effect outcome with the persistence
  receipt or failure code. Its DBMS adapter commits
  `GameCommitRequest.itemQuantityChanges` into the game-v4 `player_item_quantities`
  table with the existing receipt/transaction path.
  Do not invent a second Item writer or treat furniture stock as Items.
- [ ] Furniture/Environment confirms catalogue schema/version and Editor ownership.
  Topic Playground confirms the six-kind source/validation/preview integration
  contract before any `Tools/ContentWorkbench/**` work.

### F1 — Catalogue declaration and function registry

- [x] Add typed, stable function opt-in metadata to furniture definitions, separate
  from existing capability/relationship descriptors. Metadata names a registered
  function and compatible point/use contract; it never names a C# type, assembly,
  method, SQL statement or arbitrary resource key.
- [x] Preserve schema 1 as an optional, backwards-compatible metadata extension.
  Legacy definitions without `functionBindings` or `presentationBindings` remain
  accepted and unprivileged. Unsupported newer schema versions remain fail closed;
  no catalogue data file was rewritten.
- [x] Red tests cover legacy schema 1, valid power and mock presentation opt-ins,
  unknown function/descriptors, incompatible point/use metadata, duplicate bindings,
  and the rule that catalogue declaration alone never installs a handler.
  Invalid power argument types are covered by the handler tests and invalid known
  power values by the projection tests.

### F2 — Exact target and power mutation

- [x] W1A supplies `IFurnitureFunctionPort.Invoke(FurnitureFunctionRequest)` and
  the typed `power.set` handler. W1B adds a Furniture-owned validator that requires
  catalogue opt-in, a separate live-scene target resolver, and
  `IFurnitureFunctionReservationContextSource` evidence. The Behavior adapter must
  produce that evidence only after `FurnitureRegistry.IsValid(binding)` succeeds;
  Furniture compares activity, instance, definition, use-point, use ID and point
  revision. `useId` is copied into `FurniturePowerCommand` and participates in the
  store operation payload so a changed valid use cannot replay as identical.
- [x] Portable fake-context tests cover two compatible TV definitions, independent
  per-instance state, target opt-in, wrong owner, stale physical-point revision,
  unregistered handler, unavailable scene binding, unknown-to-Off, On/no-change,
  replay, payload conflict and a same-operation/different-allowed-use conflict.
- [x] The Furniture-owned ModelTest scene adapter now re-resolves the unique active
  point, point revision, use ID, claimed state and catalogue alias before allowing
  a function. Its Unity regression is authored but was not run in this W1B source
  window.
- [x] Furniture consumes the DBMS-owned typed `IFurniturePowerStore` contract and
  tests against a fake; it does not write raw SQL or implement another repository.
- [ ] DBMS instance/layout/game CAS and receipt behavior remains a separate owner
  acceptance gate.

### F3 — Durable power state and committed-state presentation

- [x] The master contract's power mapping is applied to existing home
  `resourceState`: `device.power`, known `0 = Off` / `1 = On`, absent = unknown.
  `IFurniturePowerStore.SetPower(FurniturePowerCommand)` remains the typed DBMS
  adapter over the existing game repository/CAS/operation receipt chain. A
  resource key alone does not justify a new table or layout schema bump.
- [ ] Atomically persist state, increment the target instance/layout revisions,
  append the allowlisted `furniture_power_changed` event, and record the operation
  receipt. Include Flow `invocationId` separately from physical `activityId` and
  inner `sequenceStepId` in the typed event context. Preserve unrelated furniture,
  furniture stock, Items and progression.
  If the selected store cannot meet those guarantees, stop for a coordinated design
  update before changing a schema.
- [x] Furniture source now maps committed resource state to `unknown`, `off`,
  `on` or `invalid`, and `FurniturePowerStateView` applies that value through a
  per-renderer `MaterialPropertyBlock`. `FurnitureSceneAdapter.ApplyCommittedPowerState`
  exposes the bridge for committed snapshots. The ModelTest/controller caller,
  renderer binding, and live screen shader remain pending W4; failed persistence
  cannot drive this view until a committed instance is supplied.
- [x] Portable tests cover unknown/missing values, known Off/On, malformed/duplicate
  power state and unrelated resources. A Unity shared-material isolation test is
  authored but not run.
- [ ] Red/green tests cover exact target persistence after reopen, stale game/
  layout/instance CAS, orphan/unsupported target, idempotent replay, payload
  conflict, already-On without a change event, rollback, unrelated-state retention,
  and transactional power persistence. Record source and Game View/player evidence
  separately.

### F4 — Sequence and mock collection integration

- [ ] Alterra Behavior connects invocation to its one shared ActivitySequence
  executor. Both the thin trusted C# composition and the typed data recipe must
  validate to the same sequence and invoke the same registered function. Furniture
  does not create a second Activity runner or own dialogue/Scenario continuation.
- [ ] Turn on TV invokes only `power.set(On)` for the exact reserved TV and ends
  on durable success. Locate TV only moves and correlates the actual authored bark.
  Fetch uses a separately registered mock presentation operation, then returns and
  completes its VN before the item grant. No failure, cancellation-before-grant or
  duplicate retry may produce an extra item.
- [ ] Dispatch `dispenser.collect_mock` through `invoke_furniture` using its
  presentation descriptor and the exact start/poll/cancel correlation contract
  above. It must not alter dispenser state or call
  `DrinkWaterActivity`, `IHydrationCheckpointSink`, stats or currency. It has no
  durable item effect. The later Scenario `host_effect` step references a pinned
  host-effect Action through `StepRecord.actionRef`; that Action binds
  `player.item.grant`, pinned `itemRef` and positive `quantity`. Strict v1 step data
  names `requiresCompletedStepId`; resolve the exact completed Dialogue execution
  in the same save/run/Flow invocation. Behavior calls
  `IScenarioHostEffectPort.Apply(ScenarioHostEffectRequest)` with the pinned action
  ref, `runId`, Flow `invocationId`, effect `stepExecutionId`,
  `requiresCompletedStepId` and `completedDeliveryStepExecutionId`. The DBMS
  adapter commits through `GameCommitRequest.itemQuantityChanges` to the
  game-v4 `player_item_quantities` table; retries use the existing stable receipt.
- [ ] Cover no eligible furniture, unreachable/busy target, stale binding,
  suppressed bark, failed mock presentation, return failure, VN failure/cancel,
  duplicate continuation, durable grant replay and a second synthetic Item to
  prove the grant is not hardcoded to Water glass.

### F5 — Workbench and authored content handoff

- [ ] Furniture gives Topic Playground stable function IDs, typed argument/result
  schemas, compatibility rules, safe diagnostics, sample fixtures and validation
  vectors. Topic Playground alone changes `Tools/ContentWorkbench/**`.
- [ ] Use DBMS's planned source kinds `dialogue_definition`,
  `scenario_definition`, `topic_definition`, `furniture_definition`,
  `item_definition` and `action_definition`; preserve existing `generic_dialogue`
  documents and IDs. The new Action v1 shared schema uses `activity_sequence`
  with exactly one of typed `steps` or trusted `composerId`, or `host_effect` with
  a registered `functionId` and typed arguments. Keep these as revisioned typed
  read/write/validate/build/preview paths; SQL JSON storage alone is not support.
  Keep furniture catalogue definitions separate from per-player layout and stock,
  and keep Item quantities separate from furniture inventory.
- [ ] Validate that invalid/unavailable functions are visible in diagnostics and
  preview but cannot compile as callable. Verify published immutable references
  pin the exact revisions; no direct compiled-row editing or raw SQL authoring.

### F6 — Integrated acceptance and evidence

- [ ] Run the exact focused source/EditMode/SQLite checks owned by each subsystem
  after implementation, then obtain the agreed exclusive Unity Editor window for
  scene/prefab bindings. Do not reset a scene or overwrite another writer's work.
- [ ] Live acceptance covers normal startup `SplashScene -> ModelTest`, known-Off
  TV -> Turn on TV -> committed and visible On, a second synthetic device proving
  independent state, Locate TV bark, Fetch drink -> visible return -> completed
  VN -> one durable Water glass, and retries/failures without duplicate effects.
- [ ] Handoff reports exact changed paths/owners, definition IDs and hashes,
  tests actually run, Editor/Play Mode observations, migration status, remaining
  gaps and the next acceptance check. Source tests or compilation never substitute
  for live player-visible evidence.

The rug-layering source rule and the interaction-cone/shimmer request are adjacent
Furniture and Environment compatibility/acceptance work, not a gate for the
reusable-content goal. The existing rug regression still needs to be run by the
placement owner when that scope is accepted.

## Acceptance gates and stopping conditions

Implementation is not complete until the master workflow's three proof cases all
pass through authored content and the shared runner, durable/receipt-backed Item
and furniture operations behave idempotently, state presentation reflects committed
truth, and the agreed Unity live checks are recorded. Do not mark catalogue metadata,
the function registry, or a successful compile as player acceptance.

Stop before writing shared files if a writer is already active, the DBMS port or
Item/migration contract conflicts with the proposed signatures, the catalogue
version cannot safely preserve current data, the exact target/reservation cannot be
revalidated, or the Unity Editor is owned by another session. Send the concrete
conflict and affected path to Main for one-writer reconciliation; do not create a
parallel implementation or silently change the approved three flows.

## Review Focus

- Does every call reach one exact, still-reserved instance and use point?
- Can data declare a function without granting code execution or save authority?
- Are operation identity, compare-and-set revisions, durable event and receipt
  correct across retries and failure?
- Do unknown power values remain unknown, and does visible state follow persisted
  state per instance?
- Are mock collection, Item grant, Alterra hydration and furniture power kept as
  distinct effects with distinct owners and checkpoints?
- Are schema 1, existing furniture counts/layout, player data, approved inventory
  keys and rug/actor/wall placement rules preserved?
- Do all six content kinds eventually share typed authoring/release/preview APIs,
  with Topic Playground the sole Workbench writer?
- Is evidence clearly split between source tests, SQLite reopen tests, Editor
  wiring and live player-visible behavior?
