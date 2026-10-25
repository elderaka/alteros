---
title: Furniture Inventory I3-I5 Editor Integration
date: 2026-10-22
tags: [alteros, furniture, inventory, debug, verification]
status: historical-editor-checkpoint-superseded
---
# Furniture inventory I3-I5 Editor integration

This is the initial wiring checkpoint before I4-I5 physics, edit-claim and UI
verification. Its remaining-gap list is historical; current implementation state
is [[2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

## Implemented

- `FurnitureSceneAdapter` reads the canonical `StreamingAssets/Furniture/catalogue.json`
  through `Application.streamingAssetsPath` when no importable `TextAsset` is
  assigned. This avoids copying the catalogue into a second asset. An Editor
  smoke check parsed all 8 definitions with 0 validation errors.
- `InputSystem_Actions.inputactions` now maps `Player.Interact` from keyboard E
  to T while preserving its gamepad binding. It adds Q pickup/confirm, E
  inventory/return, left/right mouse confirm/return, Z/X and wheel rotation, C
  snap toggle, F cancel, and the single F5 Debug Mode action.
- Editor-authored prefabs and materials:
  `Assets/AlterOS/Testbox/Prefabs/FurnitureInventoryPanel.prefab`,
  `FurnitureInventoryRow.prefab`, `FurniturePlacementPreview.prefab`,
  `FurnitureDiagnostics.prefab`, and three `Furniture*` preview/diagnostic
  materials under `Assets/AlterOS/Testbox/Materials/`.
- `ModelTest` wires one handling controller and scene adapter to the existing
  RoomTestbox `ActivitySceneHost` and `AlteraProgressionStore`. The floor is the
  authored `room.floor` support. The Player owns one input listener. The inventory
  panel is under the existing HUD Canvas; the TBA tab is visible and disabled.
  The existing `ActivityDebugOverlay` and the extra diagnostics consume the same
  off-by-default `DebugModeState` instance.
- A blocked placement keeps its current target pose for the red preview. The floor
  footprint faces upward. Inventory labels humanize catalogue keys (for example,
  `seat.basic` becomes `Seat Basic`) rather than displaying localization keys.

## Verification

- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`: **84 passed, 0 failed**;
  portable output has no UnityEngine/UnityEditor references.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group All`: **23 passed** using
  synthetic temporary roots, the pinned SQLite-net binding and the Windows x64
  native plugin.
- Unity 6000.4.4f1 EditMode: **13 passed, 0 failed** across the focused scene-seed,
  Debug Mode, input-policy, handling and furniture-persistence tests (jobs
  `150ffc9463ed48408c779ae012a230d3` and `8b9219fdaccc4cb883ef12d2d60bbaa9`).
- Current Unity instance was `alteros@48a268a4`, project root
  `E:/Pleiades/alteros`. `ModelTest` is saved clean with 9 roots; no import,
  compilation, or test job is active. The final Editor inspection confirmed the
  serialized references and action paths. No Play Mode, physical key press,
  visual Game View, restart, or game-profile database check was run.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` and
  `Tools/Conversation/Check-UnifiedConversation.ps1` each exit before tests
  because `ContentBakeService.cs` refers to `AlterOS.Authoring.AuthoringSnapshot`
  but those harness source lists omit the Authoring sources. **Zero tests ran**
  in those two suites. The authoring owner was notified; this furniture task did
  not modify those harnesses.

## Remaining acceptance

- The current Testbox has floor support only. The catalogue's `wallOpening` window
  support has no authored opening/support surface in this room, so window placement
  is not accepted.
- New non-scene furniture uses placeholder cube visuals and does not receive new
  `FurnitureUsePoint` executors. The existing dispenser remains the only installed
  furniture activity point.
- Unity physics placement tests, actual key dispatch, inventory appearance and
  interactions in Game View, busy-activity refusal during a live session, and
  restart persistence remain unverified. Do not run the normal startup path until
  an isolated profile or human-approved player acceptance path is available.
- On platforms where StreamingAssets is packaged as a URL rather than a filesystem
  directory, the synchronous catalogue fallback needs a platform-specific loader;
  this pass verified Windows Editor only.

## SHA-256

| Asset | SHA-256 |
| --- | --- |
| `Assets/Scenes/ModelTest.unity` | `67A0B0BB61D91286D1C6BF5D546B6C9EEFC9CE32C16306A89863519EA09A9720` |
| `Assets/InputSystem_Actions.inputactions` | `BF3A0B5C6E2B1A0373B0F003765875CE4AD0B1880254B3BE534CDD0C5DCA5B0A` |
| `FurnitureInventoryPanel.prefab` | `08DF153C50264EA43293E6D2DC70F3C89F005BC5F2E5F64150932830334B4BDB` |
| `FurnitureInventoryRow.prefab` | `1BFA6F9B8FE423BF8A525177AD7614939A1C0885D5901436961DC6688ABFAA7A` |
| `FurniturePlacementPreview.prefab` | `997658A1A30C300079F6DABF346C9D5D62D592EFD66DD4139042F3461E90CF58` |
| `FurnitureDiagnostics.prefab` | `810CB75BBC9F9BF5E3B7EA7064C007C2FBD53A45A870F013BC36D1A972559B3F` |
| `FurnitureSceneAdapter.cs` | `576381A3F620D2A4786DA49410233608A4551F0BA0286A1E5DB59A3BBD1CDCD9` |

The main session received the suite blocker and current handoff; no database,
authoring, provider, or player-profile work was performed here. The exclusive
Editor window was released with the scene saved clean and the Editor idle.
