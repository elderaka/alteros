---
title: Furniture Inventory I4-I5 Pre-Live Implementation
date: 2026-10-22
tags: [alteros, furniture, inventory, placement, verification]
status: implementation-ready-live-test-pending
---
# Furniture inventory I4-I5 pre-live implementation

This follow-up completes the placement physics, shared activity/edit claims,
inventory UI checks, and ModelTest window support left open in the earlier
[[2026-10-22 Furniture Inventory I3-I5 Editor Integration]] checkpoint.

## Implemented

- `FurnitureRegistry` now owns activity-use and furniture-edit claims for the
  same exact use point. Beginning a move takes an edit claim; the point stays
  active but new activity reservations and direct options are blocked. Exact
  claim tokens are released on successful place/return, failed begin, cancel,
  and controller disable. ModelTest still uses the existing `ActivityHost`.
- Placement overlap evidence now allows the `carpet` catalogue category to
  layer over recognized furniture, and allows other furniture over a carpet.
  Room geometry and actor colliders still block placement. Orphan/unknown
  colliders remain blocking because their furniture kind cannot be resolved.
- ModelTest's North wall has a 1.8 m x 1.4 m aperture centered at room-local
  `(0, 1.9, 4.1)`. Its Editor-authored trigger surface is identified as
  `room.wall.north.opening.01` / `wallOpening`. The handling controller references
  both this support and `room.floor`.
- Inventory EditMode coverage now checks dynamic definition discovery, zero
  stock, row selection/close, close without selection, missing row references,
  disabled TBA tab, and keyboard navigation. Existing interaction tests cover a
  declared action without an installed executor; input-policy tests cover UI
  click-through suppression.

## Verification

- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`: **84 passed, 0 failed**;
  portable output has no UnityEngine/UnityEditor references.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group All`: **23 passed** using
  isolated synthetic roots and the existing SQLite binding/native plugin.
- Unity 6000.4.4f1 EditMode: **29 passed, 0 failed** across
  `FurniturePlacementTests`, `FurnitureInventoryUiTests`,
  `FurnitureRegistryEditClaimTests`, `ActivityHostTests`, and
  `FurnitureInteractableEditorTests`.
- Unity compiled with no errors. `ModelTest` is active, saved clean, and has 9
  roots. `manage_scene validate` reported 0 issues. The serialized support list
  resolves to the floor and North opening components.
- The inventory UI tests verify row navigation and state changes, but do not
  establish Game View layout or pixel-level appearance.
- Current `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` passes **67/67** and
  `Tools/Conversation/Check-UnifiedConversation.ps1` passes **197/197**. An
  earlier checkpoint stopped before tests because the harness source lists
  omitted `AlterOS.Authoring.AuthoringSnapshot`; the current owner fixed that
  separately. Those harness files were not changed in this furniture work.

## Live acceptance still pending

No Play Mode, physical key dispatch, SplashScene-to-ModelTest startup, Game View
review, restart persistence, busy-live-activity check, or player-profile access
was performed. The next step is the live implementation test with an approved
isolated profile or a human-approved test path. The Editor scene remains ready
and saved for that check.

## 2026-10-23 live-check attempt

- Confirmed the connected Editor is Unity 6000.4.4f1 on `E:/Pleiades/alteros`,
  with `Assets/Scenes/ModelTest.unity` active, clean, and outside Play Mode.
- Read the existing Console baseline without clearing it: zero error and warning
  entries. Build Settings contain `SplashScene` at index 0 and `ModelTest` at
  index 1.
- Did not enter Play Mode. `AlteraProgressionStore.Awake()` calls `Load()`, and
  `EnsurePersistence()` opens the game database under
  `Application.persistentDataPath/Data/alteros-game.sqlite3`. The only synthetic
  root override is the private Editor-only, nonserialized `testPersistenceRoot`
  field. Existing EditMode tests set it by reflection on an inactive test object
  before invoking `Load()` directly; ModelTest has no serialized root override
  before its normal `Awake()` path.
- The isolated-profile gate was therefore unmet. No profile database or legacy
  save file was opened or written, no provider/key was accessed, and no
  scene/prefab/serialized asset was changed. A persistence-owner-approved test
  root seam or another isolated test path is required before live acceptance.
  Main has assigned DBMS a minimal Editor-only pre-Awake synthetic-root seam;
  the retest remains pending that release and Main's isolated Editor window.

## SHA-256

| Asset | SHA-256 |
| --- | --- |
| `Assets/Scenes/ModelTest.unity` | `77A01910EBD2639569DFB151E28D3A89DB94D62E50A4B911DD08C7D70CC5AD8B` |
| `Assets/AlterOS/Activities/FurnitureRegistry.cs` | `1708129E409DB28E2940730F494C0B8850101524FCDDA6E1CFFE23ED5216EE47` |
| `Assets/AlterOS/Activities/ActivitySceneHost.cs` | `EEA5AAA09779F8CA24754B459DE9A89B2FDB8336D9849590927800F0BAA788C6` |
| `Assets/Scripts/Furniture/FurnitureSceneAdapter.cs` | `C6E2E2830F1E3F6E58A2CC1C016417B32AEC8C2BD32AA737E0CED7849D9BF95F` |
| `Assets/Scripts/Furniture/FurnitureHandlingController.cs` | `41E0BC41CC864CB577346A1C3211FAC737846EACDDD0348AE8138E9E51A47986` |
| `Assets/Scripts/Furniture/FurnitureInventoryPanel.cs` | `9D2A42F6A4BE5F291DB2EF9F234D9974CD39C09C5E6E86E3D7F9A2C9DFE2E4E7` |
