---
title: Furniture Foundation Handoff
date: 2026-10-22
tags: [alteros, furniture, handoff]
status: editor-wired-live-acceptance-pending
---
# Furniture Foundation Handoff

Related: [[Furniture Foundation Implementation Plan]],
[[Content Workbench Shared Contract]],
[[Furniture Mechanics and Catalogue Design]],
[[../Verification/2026-10-21 Furniture Foundation F4 Source]].

## Current status (2026-10-22)

This handoff's original F1–F4 and I1 checkpoints remain historical. I1–I5
inventory, placement, Debug Mode and SQLite persistence are now Editor-wired in
ModelTest; the current source/Editor evidence and live-test boundary are in
[[Furniture Inventory and Placement Implementation Plan]] and
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].
Do not use the earlier Canvas, schema-2 persistence, window-support or harness
blocker gaps below as current state.

## Delivered source

- F1: versioned v1 catalogue DTOs, validator, registry and eight canonical
  definitions at `Assets/StreamingAssets/Furniture/catalogue.json`.
- F2: portable relationship resolver, available/locked/hidden choice evaluation
  and atomic point reservation.
- F3: room-local save schema, CAS repository, revisioned placement commands and
  synthetic raised-chair/wall-window/carpet serialization fixture.
- F4 source: exact-point `FurnitureSceneAdapter`, `Player.Interact`
  `FurnitureInteractable`, revision-bound click capture, ActivityHost standalone
  request path and DirectConversationBridge Scenario-owner gate.

The only installed furniture activity executor remains **Ask Alterra to get water**.
The installed `needs.drink_water` executor moves Alterra and applies her existing
hydration only at sip checkpoints. It does not drink for the Player. Furniture
declarations do not install executors. Seating/rest/TV/window uses, physical
sitting, cameras, Player hydration and independent activity initiation remain
unavailable.

Furniture interaction requests use the same exclusive ActivityHost and exact use-point
reservation path. Their request identity is pinned to the binding. Same-identity
retry returns the original result; a competing request is unavailable while an
activity is active. Running/suspended Scenario ownership blocks standalone
requests. Standalone actions emit no Scenario checkpoint/terminal projection and
have no menu Drink return/bark/next-interaction follow-up or transcript write. The authored
menu Follow/Drink Scenarios retain their current behavior.

## Verification

- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`: 52 passed, 0 failed;
  portable output contains no UnityEngine/UnityEditor reference.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`: 67 passed, 0 failed.
- `Tools/Conversation/Check-UnifiedConversation.ps1`: 194 passed, 0 failed.
- Fresh Unity 6000.4.4f1 Roslyn source compilation passed for
  `AlterOS.Runtime`, `Assembly-CSharp` and `AlterOS.Editor.Tests`, using cached
  Bee response files and scratch output under `.superpowers/sdd/F4-source-compile/`.
  It reported existing obsolete-API warnings. It did not import into or replace
  live Editor assemblies.
- Unity Editor test execution, Play Mode, audio, visuals, live activity movement
  and startup acceptance were not verified.

## F4-specific acceptance (separate from the I1-I5 inventory slice)

Unity MCP's `mcpforunity://editor/state` read stalled and was stopped. No scene,
prefab, Canvas or `.meta` file was changed. When the correct Editor connection
is available:

1. Import and check Console, then run focused furniture, Activities/Scenario and
   greeting tests.
2. In the Editor, author the locked-choice tooltip and focus target on a Canvas
   prefab. A disabled choice must remain inspectable by pointer hover and keyboard/
   controller focus. Wire the adapter and choice template to the existing testbox
   without changing GUIDs or unsaved scenes. Keep all static layout/style authored.
3. Verify SplashScene→ModelTest and T on the dispenser says “Ask Alterra to
   get water”; confirm it uses the existing Alterra action and no Player hydration.
4. Verify scenario/menu ownership, busy request refusal, stale click/reconfirm,
   actual drink completion and the preserved return/bark/next-E Follow/Drink flow.
   Confirm no forced camera/cutscene and no duplicate completion effects.

The locked-choice tooltip/focus and actor-use paths above remain separate F4
acceptance items. The I1-I5 inventory controls/panel are wired, but their live
key/Game View and player-profile checks remain pending. Keep source, Editor
wiring and player evidence as separate status gates.

## Inventory/placement continuation source checkpoint (2026-10-22)

The approved I1 portable slice is now implemented on top of F1-F4:
schema-2 home inventory DTO/validation, 10-item starter seed, finite/debug supply
origin snapshot, transient handling session, move/place/return/cancel, combined
pose command and shared input policy/state source. The session preserves schema-1
read compatibility; it does not perform the SQLite schema-1-to-2 migration.
Schema-2 storage is not yet wired through `FurniturePersistenceAdapter`.

`Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` passes **82/82**, including
F1-F4 regression checks. The portable assembly has no UnityEngine/UnityEditor
references. The in-memory store covers lost-ack retry, duplicate receipt reload,
cancel/restart, finite stock conservation, debug return, overflow, stale evidence,
reservations and support dependents. This is not SQLite transaction evidence.

`DebugModeState` and `FurnitureInputPolicy` exist as pure source, but F5 still has
no Input System owner or shared scene instance. `Player.Interact` remains bound
to E; T/Q/E remapping, inventory/placement controls, Canvas UI, preview/diagnostic
prefabs, ModelTest wiring and physical key input are pending. No scene, prefab,
input asset or `.meta` was changed.

This I1-only checkpoint is historical; I2 and the authorized Editor wiring have
since been completed. See the current checkpoint below.

## Inventory/placement Editor integration checkpoint (2026-10-22)

I1 source passes 84/84 and I2 synthetic persistence passes 23/23. ModelTest now
has the T/E/Q/held furniture input mapping, one shared F5 DebugModeState owner,
an Editor-authored inventory/preview/diagnostics set, a floor support, and wired
controller references. Focused Unity EditMode checks pass 13/13. No Play Mode,
physical input, Game View, or player-profile acceptance was run.

At that initial Editor checkpoint, the focused set was 13/13; the North wall
opening and physics/edit-claim/UI checks were not yet included. The note that
Scenario/Unified harnesses stopped before tests was also temporary. Those gaps
were later closed by the separate owners: current checks are Scenario 67/67,
Unified 197/197, and focused Unity EditMode 29/29; the North opening support is
now authored. Preserve this checkpoint's counts and limitations as historical.
The current wiring, scene hash and pre-live boundary are in
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].
