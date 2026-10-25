---
title: Furniture Inventory and Placement Implementation Plan
date: 2026-10-22
tags: [alteros, furniture, inventory, implementation]
status: implementation-ready-live-test-pending
---
# Furniture Inventory and Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans in the existing
> Room Testbox session. Steps use checkbox syntax. Human review approved the plan;
> implementation START was delivered on 2026-10-22 to
> `01a15376-d6f0-7c41-b9e5-fcdee723e87b`. I1 portable source and I2 SQLite
> persistence are implemented; I3-I5 Editor integration and source/Editor checks
> are complete. I6 Play Mode, key dispatch and player-visible acceptance remain.
> Do not restart F1–F4.

**Goal:** Deliver furniture inventory, safe held-preview placement, durable home
ownership and the shared F5 Debug Mode in the existing room fixture.

**Architecture:** Keep catalogue/resolver/reservations/layout commands in the
existing portable furniture core. A small handling session owns temporary preview
and stock reservations; only confirmed changes enter the existing SQLite game
transaction. Editor-authored prefabs/Canvas and thin scene/input adapters present
the state without creating a second activity runner, save writer or catalogue.

**Tech Stack:** Unity 6000.4.4f1, C#, installed Input System, existing SQLite-net
game repository, Canvas/TMP, existing source harness and Unity EditMode tests.

**Spec:** [[Furniture Inventory and Debug Design]], [[Debug Mode]],
[[Furniture Mechanics and Catalogue Design]], [[Furniture Foundation Handoff]],
[[Data Management Handoff]], [[Content Workbench Shared Contract]].

## Global constraints

- Work in `E:/Pleiades/alteros`; preserve shared changes, GUIDs and original data.
  No YAML/meta edits, packages, broad cleanup, automatic commits or worktree reset.
- Room Testbox (`01a15376-d6f0-7c41-b9e5-fcdee723e87b`) is the intended implementer.
  Coordinate persistence file ownership with Database Management System through
  root before changing shared files; it is executing a separate authoring cutover.
  Do not write `Tools/ContentWorkbench/` or dialogue authoring/compiler files.
- All persistence tests use disposable synthetic roots. No real profile reads or
  migrations, provider credentials/calls, model loading, embeddings or TTS spend.
- Reuse `FurnitureCatalogueRegistry`, `FurnitureLayoutCommands`, reservations,
  `FurniturePersistenceAdapter`, `IGameRepository` and the existing `ActivityHost`.
  No live JSON save fallback or separate furniture database.
- Furniture catalogue declarations do not install actions. A TV viewing point is
  not a registered ActivityHost use point or executor, and a runtime placement
  proxy does not automatically become one. See [[Furniture Inventory and Debug
  Design]] and the approved
  [[Scripted Actions and Inspect TV Design]]. Physical sitting/cameras, new
  bed/TV/window activities, gifting, other item categories and cloud sync are out.
- Scene/prefab/input-asset composition changes use the Editor; static appearance
  is Canvas/prefab-authored. Runtime may bind lists and move a preview/diagnostics.
- Serialize Editor access with other sessions. Stale MCP alone is not a hang;
  recovery follows current AGENTS.md. Do not run broad suites or ordinary Play Mode
  against the user's profile. Report safe-startup test limitations explicitly.
- Catalogue remains `Assets/StreamingAssets/Furniture/catalogue.json` and its one
  registry; explicit Reload is the tools contract. This plan does not migrate
  furniture definition authoring to SQL or implement Workbench discovery itself.

## Review focus

1. Duplicate input/retry and a crash around commit cannot create/loss an owned
   item or leave both inventory and placed copies (I1/I2).
2. F5 switching during a hold cannot reinterpret a finite reservation as free
   supply or grant a refund that was never reserved (I1/I3).
3. A parent support, occupied point or stale placement changes before confirmation
   must reject safely, without stopping an activity or swapping targets (I1/I4).
4. Loading old/unknown schema or a removed definition cannot silently reset the
   room, reseed stock or discard an orphan (I2).
5. Text entry, UI clicks and held-state commands cannot also trigger world talk,
   place a piece or toggle a second debug listener (I3/I5).

## Approved rules and coordinator implementation choices

Approved: 10 additional stored items per definition at initial test inventory
setup, existing room pieces separately owned, no cap/refill. Existing moves cost
no stock. Returning a placed instance adds one item, including debug-created
pieces. Debug supply creates without consuming finite stock; F5 does not delete
pieces or reset counts. Busy/reserved pieces cannot be edited. Unfinished holds
cancel on quit/crash/reload to the last committed state.

Coordinator choices, reviewable with this plan:

- A fresh unlimited-supply preview is not owned until successfully placed.
  Cancelling/returning that unplaced preview creates no finite stock. After a
  successful placement it behaves as ordinary owned furniture; no permanent
  debug-origin restriction or second inventory is needed.
- Hold state is transient, not persisted. Keep the committed source piece/stock
  authoritative until confirmation; only a ghost changes pose. Reserve its points
  against activity use while moving. Cancel releases transient claims. A crash
  therefore needs no compensating stock transaction or auto-resumed hold.
- Store furniture inventory with the existing home layout aggregate in
  `IGameRepository`'s `layoutJson` column, not a new database or general item schema.
  Home layout schema 2 adds `inventory` (stable definition IDs + nonnegative stored
  counts). Newer schema fails closed; old schema 1 upgrades explicitly, preserving
  all placed data and adding initial stock exactly once in a durable transaction.
  Keep existing game database schema unless actual DDL changes are justified.
- Seed only the eight approved starter definitions when initializing inventory.
  Later catalogue additions are discoverable with zero stock, not a silent new
  grant. Preserve unavailable/removed definition stock and placed orphans.
- Baseline editable placement settings: 0.25 m grid, 15 degree rotation, snap on;
  Debug Mode starts off. These are coordinator defaults, not claimed human values.
  Catalogue mounting/support rules override floor rotation for wall/surface items.
- Reject editing supports with attached dependents in this slice rather than
  silently moving/removing children. No implicit multi-object move implementation.

## File and interface map

Existing portable ownership: `Assets/AlterOS/Furniture/`.
Add focused `FurnitureInventory.cs` DTO/validation,
`FurnitureHandlingSession.cs` transient state and `FurnitureInputPolicy.cs` modal
dispatch. Extend `HomeFurnitureLayout.cs`, `HomeFurnitureRepository.cs` and
`FurnitureLayoutCommands.cs` narrowly for lossless schema/combined-pose changes.

Existing persistence owner: `Assets/AlterOS/Persistence/FurniturePersistenceAdapter.cs`.
Keep `LoadHome(string homeId)` and
`CommitLayout(string operationId, long expectedGameRevision, long expectedLayoutRevision,
HomeFurnitureLayoutDocument validatedLayout)` as the durable home seam. Schema 2
inventory travels with that document and its one receipt; no second stock commit.
Add `FurnitureHomeStoreAdapter.cs` only as a thin bridge if required to keep the
portable session free from SQLite/Unity dependencies.

New portable seam in `FurnitureInventory.cs`:

```csharp
public interface IFurnitureHomeStore {
    FurnitureHomeReadResult Read();
    FurnitureHomeCommitResult Commit(string operationId, long expectedGameRevision,
        long expectedLayoutRevision, HomeFurnitureLayoutDocument candidate);
}
```

Read result: state/error code, exact detached document, game/layout revisions.
Commit result: state/error code, durable flag, document and receipt revisions.
The bridge maps existing ready/committed/duplicate/stale/recovery states; duplicate
operations reload durable truth rather than returning a newly calculated candidate.

`FurnitureHandlingSession(IFurnitureHomeStore store, FurnitureCatalogueSnapshot catalogue)`:
`BeginFromInventory(string definitionId, bool debugEnabled)`,
`BeginMove(string instanceId, FurniturePlacementEvidence evidence)`,
`UpdatePreview(FurniturePlacementPose pose, FurniturePlacementEvidence evidence)`,
`Confirm(string operationId, FurniturePlacementEvidence evidence)`,
`ReturnToInventory(string operationId, FurniturePlacementEvidence evidence)`,
`Cancel()` return `FurnitureHandlingResult`. `Current` exposes a detached
`FurnitureHandlingView` (idle/holding/unavailable, definition/instance, source kind,
pose, validity/reason and available counts). Define these DTOs with the session;
pose includes room-local position, quaternion and support reference. No raw UI
mutation of the view. Snapshot supply origin when beginning, not when confirming.

New `Assets/AlterOS/Debugging/DebugModeState.cs`: `bool IsEnabled { get; }`,
`event Action<bool> Changed`, `SetEnabled(bool enabled)`, `Toggle()`.
One session/app instance is passed to all consumers; no statics plus scene-local
copy, persisted module booleans, or duplicate owners.

Scene adapters under `Assets/Scripts/Furniture/`: `FurnitureHandlingController.cs`
(host wiring), `FurniturePlacementPreview.cs` (evidence/ghost),
`FurnitureInputController.cs` (single input dispatch), `FurnitureInventoryPanel.cs`
(Canvas binding). Reuse existing `FurnitureSceneAdapter` and Player world input.
Use existing `Assets/Scripts/UI/AlterraActivityDebugView.cs` for the overhead view.

Editor-created assets: `Assets/AlterOS/Testbox/Prefabs/FurnitureInventoryPanel.prefab`,
`FurnitureInventoryRow.prefab`, `FurniturePlacementPreview.prefab`,
`FurnitureDiagnostics.prefab`, and required materials/settings in that Testbox
folder. Wire only the existing fixture/ModelTest after checking scene ownership.

## I1 — Pure ownership and handling session

Tests: add `FurnitureInventoryTests.cs`, `FurnitureHandlingTests.cs` under
`Assets/AlterOS/Tests/Editor/Furniture/`; include them in the existing headless
runner's explicit test list. Use an in-memory `IFurnitureHomeStore` test double.

- [x] Write failing cases: 10 -> begin finite preview displays 9 available but
  durable stock stays 10; cancel returns display to 10; confirm stores 9 + one
  instance; return stores 10 + no instance; move/cancel changes no ownership.
  Test duplicate confirm/return, zero stock, overflow, bad IDs, new debug preview
  cancel, placed debug return, and both F5 transitions during a finite/debug hold.
- [x] Run `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurnitureHandling`;
  require the intended new assertion failures, not a broken existing dependency.
- [x] Implement the session/interfaces and checked counts. Revalidate catalogue,
  source instance/revisions, supports and reservations at confirm/return. Candidate
  layout plus stock commit once; publish only durable success. Retain rejected
  preview safely for retry/cancel, with stable operation identity on uncertain I/O.
  Test removed/moved target, changed catalogue, newly occupied point, duplicate
  operation after session recreation and support-with-dependent refusal.
- [x] Run the targeted tests and full Furniture source suite; require zero failed
  and nonzero executed tests. Record exact counts and changed files, not a commit.

## I2 — SQLite home integration, upgrade and recovery

Files: existing home DTO/validator and persistence adapter; add
`Assets/AlterOS/Tests/Editor/Persistence/FurnitureInventoryPersistenceTests.cs`.
Extend `Tools/Persistence/Check-GamePersistence.ps1` discovery if necessary.

- [x] Write failing synthetic-native tests for atomic stock+layout+event receipt,
  forced precommit failure, reopen after commit-before-ack, same-operation retry,
  stale game/layout revision, schema-1 upgrade, schema-2 reopen and future-schema
  rejection. Preserve instance IDs/poses/support/resource state and original
  migration bytes through the established backup/receipt mechanism.
- [x] Run `Tools/Persistence/Check-GamePersistence.ps1`; verify the new cases fail
  specifically because inventory integration is absent.
- [x] Implement the narrow home schema upgrade and bridge through the existing
  repository writer. One-time inventory initialization is revision/idempotency
  protected; returning debug-created placed pieces credits ordinary stock once.
  Add causal furniture placed/moved/returned events in the same transaction;
  preview movement emits no durable history flood. Backups must include schema-2
  home inventory, and validation must reject unsupported versions without reset.
- [x] Prove reboot/crash simulation: new-preview and move-preview leave the prior
  committed state intact; confirmed operations survive. Repeat load/recovery
  cannot reseed; missing definitions retain counts/orphans. Run Persistence plus
  existing Furniture suites and record results. No real save migration is allowed.

## I3 — Shared debug state and contextual input

Files: shared debug owner, `FurnitureInputPolicy.cs`, input/controller adapters,
existing `PlayerLookInteraction.cs`, and Editor-managed `Assets/InputSystem_Actions.inputactions`.
Tests: `DebugModeStateTests.cs`, `FurnitureInputPolicyTests.cs` in the Editor tests.

- [x] Write tests for idempotent state notification, a single F5 transition,
  state-off default, finite counts untouched, text/modal suppression and exactly
  one intent per input. Pin the complete approved T/Q/E, held mouse/Q/E, wheel/Z/X,
  C toggle and F cancel mapping; T cannot talk while holding.
- [x] Run focused source tests, confirm expected failures, then implement one
  dispatch boundary. Keep existing Player world-interaction execution behind T;
  do not leave an old E listener active. Bind/remap the input asset in the Editor
  rather than serializing it by hand. Explicitly audit other T/Q/E/F5 consumers.
- [x] Integrate one DebugModeState with supply and overhead/diagnostic visibility.
  Preserve existing build guards; hidden consumers must still receive enabling
  events. Mode changes never reinterpret an already-held stock claim.
- [x] Run source tests to green and inspect Editor input actions/listeners. Report
  source mapping separately from tested actual key presses.

## I4 — Real placement, supports and activity protection

Files: scene/controller/preview adapters plus narrow changes to existing
`FurnitureSceneAdapter.cs`; `FurniturePlacementTests.cs` under Editor/Furniture.

- [x] Write tests for valid floor placement, wall-window mounting, carpet overlap
  policy, support mismatch, collision/room bounds, actor overlap, blocked access,
  stale support revision, existing held-source self-collider exclusion, busy
  furniture and attached dependents. Geometry-valid furniture may remain decor
  even when a chair/table relationship is invalid.
- [x] Run targeted tests and confirm intended failures; implement current physics
  evidence feeding `FurnitureLayoutCommands`, not a parallel placement validator.
  Confirm move+rotation+support together as one candidate/commit, not intermediate
  durable positions. Keep the original authoritative pose while the ghost moves.
  A transient editing claim prevents new activities using the held source.
- [x] Author ghost/footprint/material assets in the Editor. Preview uses actual
  bounds/support orientation, green/red plus readable reason, adjustable snap and
  rotation settings. Cones and relationship shimmer use actual resolved points,
  are Debug Mode gated and never change capability eligibility.
- [x] Run focused Unity physics tests with synthetic fixtures; verify claims are
  released on cancel/disable/failure. Recheck existing Drink/Follow owner guards
  without invoking providers or inventing unsupported actions.

2026-10-22 implementation note: ModelTest has floor support, room-bounds and
physics-overlap evidence, a red/green bounds ghost and footprint, snap/rotation
settings, and the existing ActivityHost busy guard. The portable tests cover the
combined transform and stock/session policy. The North wall now has an authored
1.8 m x 1.4 m opening centered at room-local `(0, 1.9, 4.1)` with the named
`room.wall.north.opening.01` `wallOpening` support frame. Edit claims share
`FurnitureRegistry` with activity reservations; the preview keeps the committed
source pose while it moves. Rugs may overlap recognized furniture in either
placement order; room geometry and actors remain blocking colliders. Synthetic
Unity physics/claim checks pass; no Play Mode or player acceptance is claimed.

## I5 — Canvas inventory and testbox integration

Files: inventory panel/row prefabs, UI binder, existing debug Canvas and only the
required scene references. Tests: `FurnitureInventoryUiTests.cs`.

- [x] Author a names-only Furniture list and inert visible TBA tab in the Editor.
  Rows come from one validated catalogue snapshot; show counts or infinity, and
  clear unavailable reasons. No hardcoded per-definition UI or runtime layout.
- [x] Bind open/select/close to the handling session. Selecting closes inventory
  into preview; the same click must not also confirm placement. E opens inventory
  with either F5 state; while holding E returns instead. UI/text ownership blocks
  underlying world input and restores prior cursor/control state on close.
- [x] Add tests for newly registered definition discovery, zero-stock row,
  unsupported action despite an owned object, cancelled selection, missing prefab
  references, keyboard navigation, inert TBA and click-through suppression. Run
  focused EditMode checks and inspect authored layout in Game View separately.
- [x] Wire the existing fixture only after correct-project and dirty-scene checks.
  Reuse the existing progression/repository owner; no new default-root store or
  startup reset. Inspect Console and compile; save only intended assets/scenes.

## I6 — Acceptance and handoff

- [x] Run Furniture, Persistence, Scenario Runtime and Unified Conversation source
  suites; use their existing scripts under Tools and record exact commands/counts.
  Confirm no duplicate catalogue, database owner, activity runner or F5 listener.
- [x] Run focused Unity inventory/input/placement tests, not the broad suite.
  Inspect Console and prefab references after import. Report Editor evidence
  separately from cached-reference compilation or headless simulations.
- [ ] If a detached whole-startup profile seam exists, verify SplashScene ->
  ModelTest -> T interaction, E inventory, placement/move/return/cancel, restart
  persistence, busy rejection and repeated F5 toggles with no providers/TTS. If
  startup still targets the real profile, do not enter Play Mode: name the missing
  isolation seam and hand the player acceptance checklist to the human.
- [x] Update this plan, existing Furniture Foundation Handoff, dated verification,
  Feature Register, UnityProjectContext, Home/TODO and input-facing documentation.
  Keep authored dialogue vocabulary/import blockers separate. No feature-complete
  claim merely because source tests compile/pass.

2026-10-22 initial I6 checkpoint: Furniture passed 84/84 and Persistence passed 23/23.
Scenario Runtime and Unified Conversation initially did not reach their tests:
both source harnesses stopped compiling `ContentBakeService.cs` because their
source lists omitted `AlterOS.Authoring.AuthoringSnapshot`. The initial Unity EditMode 13/13 run did not
include Unity physics or Player input dispatch; the follow-up coverage is recorded
below. No Play Mode or player-profile check was run.

## Execution checkpoint — 2026-10-22

This first I1 source checkpoint is retained as a dated baseline. Its 82/82 test
count and then-open Editor/persistence gaps are superseded by the I4-I5 pre-live
checkpoint below; no earlier test result has been rewritten.

I1 portable ownership/handling is implemented: schema-2 inventory DTO/validation,
transient holding, finite/debug supply origin snapshot, move/place/return/cancel,
checked stock changes, detached views, revision/catalogue/support/reservation
revalidation, and receipt-safe retry behavior. Targeted FurnitureHandling passed
17/17 and the full `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` suite passed
82/82 with no UnityEngine/UnityEditor references. The test store is in-memory and
does not prove SQLite integration. The suite also preserves and exercises the
existing F1–F4 source behavior.

The shared DebugModeState and contextual input policy are implemented/tested;
F5 Input System ownership, input remapping, overlay binding, Canvas prefabs and
ModelTest wiring are in place. The latest related Unity EditMode set passes
29/29, including physics, edit-claim lifecycle, inventory UI and ActivityHost
guards. Physical key input and Game View remain for live acceptance.

I2 is implemented through the existing SQLite game writer. Schema-one homes
initialize the eight starter counts exactly once; placement, move, return and
initialization events commit atomically with the schema-two layout. Stock
transitions are validated, duplicate receipts reload current durable truth, and
future layouts fail closed. `Tools/Persistence/Check-GamePersistence.ps1 -Group
All` passed 23/23 with the SQLite-net binding and native Windows plugin; the full
portable Furniture suite passed 84/84. See [[../Verification/2026-10-22 Furniture
Inventory I2 Persistence]], the initial [[../Verification/2026-10-22 Furniture
Inventory I3-I5 Editor Integration]], and the current [[../Verification/2026-10-22
Furniture Inventory I4-I5 Pre-Live Implementation]].

Current Editor checkpoint: `alteros@48a268a4`, ModelTest active and saved clean,
scene validation returned 0 issues, compilation completed without errors, and no
test/import job is active. Editor-authored panel/preview/diagnostic prefabs, input
actions, floor/window support, and controller references are saved. Furniture
84/84, Persistence 23/23 and focused Unity EditMode checks 29/29 pass. No Play
Mode, physical key, visual Game View, restart or player-profile acceptance was run.

## Self-review and execution handoff

Coverage: I1 owns conservation and transient recovery, I2 owns durable home
transactions/upgrade/backup, I3 owns single debug/input state, I4 owns actual
placement and activity protection, I5 owns Canvas composition/discovery, I6 owns
evidence and integration gaps. Review-focus cases all have named tests above.
The existing 52-test furniture foundation is a baseline report, not a fresh run.

Human review approved the plan and coordinator choices on 2026-10-22; START was
delivered to the existing Room Testbox session
(`01a15376-d6f0-7c41-b9e5-fcdee723e87b`). I1-I5 implementation is now recorded
with evidence. I6's live Play Mode/input/Game View/player acceptance remains the
next step; this handoff does not claim that acceptance.

Current follow-up: Scenario Runtime passes 67/67 and Unified Conversation passes
197/197 after the separately owned harness source lists were updated. These
checks were rerun as-is; the furniture work did not edit their harnesses.

## Pre-live implementation checkpoint — 2026-10-22

I4/I5 are complete through Editor integration and focused EditMode coverage.
`Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` passes 84/84 and
`Tools/Persistence/Check-GamePersistence.ps1 -Group All` passes 23/23. The
focused Unity EditMode set passes 29/29 across placement, inventory UI, edit
claims, ActivityHost and furniture interaction tests. ModelTest has floor and
North opening supports, is saved clean, and passes scene validation with zero
issues. No Play Mode or player-visible acceptance was run. Current evidence and
hashes are in [[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live
Implementation]].

Ruling: the approved inventory remains a names/counts list with status copy and
keyboard-navigable buttons; no new per-row tooltip text was invented. Cost if
wrong: players receive less detail than a future tooltip design might provide.
