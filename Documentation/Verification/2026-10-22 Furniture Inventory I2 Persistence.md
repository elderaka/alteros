---
title: Furniture Inventory I2 Persistence Verification
date: 2026-10-22
tags: [alteros, furniture, persistence, verification]
status: historical-persistence-checkpoint-superseded
---
# Furniture Inventory I2 Persistence Verification

This I2 headless checkpoint is superseded for current Editor integration status
by [[2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]]. The 23-test
synthetic SQLite evidence below remains valid; ModelTest wiring, current focused
EditMode coverage and live-test limits are in the later report.

## Scope

I2 connects the schema-2 furniture home document to the existing SQLite game
repository. It upgrades schema-1 homes once, stores inventory/layout and causal
events in one existing game commit, and fails closed on invalid or future home
schemas. All database checks used disposable synthetic SQLite files.

## Changes

- `Assets/AlterOS/Furniture/FurnitureInventory.cs`: added a complete eight-item
  starter seed for first persistent inventory initialization.
- `Assets/AlterOS/Persistence/FurniturePersistenceAdapter.cs`: implements the
  portable `IFurnitureHomeStore`; upgrades schema-1 documents on first read;
  initializes fresh scene seeds; validates stock/layout conservation; records
  furniture placed/moved/returned and inventory-initialized events in the same
  `IGameRepository.Commit`; duplicate receipts reload current durable truth.
- `Tools/Persistence/Check-GamePersistence.ps1`: includes the schema-2 inventory
  source in the existing portable persistence compile.
- `Tools/Persistence/HeadlessPersistenceTests.cs`: adds synthetic SQLite checks
  for schema upgrade/reopen, exact placed-record preservation, placement/move/
  return events, backup inventory, forced rollback, lost acknowledgement,
  duplicate replay, stale revisions, future-schema refusal and stock guards.
- `Assets/AlterOS/Tests/Editor/Persistence/FurnitureInventoryPersistenceTests.cs`:
  adds focused Unity EditMode coverage for one-time upgrade and the store bridge.

No game DB DDL, `SqliteGameRepository`, or separate database was added. Furniture
schema-1 home documents do not carry external migration source bytes; their
placed IDs, transforms, supports and resource state are preserved in the
schema-2 document, and the existing game backup contains the complete committed
schema-2 snapshot. Existing external-import byte backup behavior is untouched.

## Validation

- Before the migration implementation, `Tools/Persistence/Check-GamePersistence.ps1
  -Group FurnitureSchemaUpgrade` failed at the expected schema-version assertion.
- Before causal event/replay/transaction work, focused groups failed at the
  expected missing event, latest-truth and atomic rollback assertions.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group All`: **23 passed, 0 failed**.
  This compiled against Unity 6000.4.4f1 bundled Mono and ran the real SQLite-net
  binding with its Windows x64 native plugin in the runner's unique temp root.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`: **82 passed, 0 failed**;
  its portable output contains no `UnityEngine` or `UnityEditor` references.

## Remaining

The new Editor NUnit test file has not yet been run in Unity. Input remapping,
scene wiring, Canvas/prefab authoring, physics placement, visual feedback,
Play Mode, restart acceptance and SplashScene-to-ModelTest acceptance are still
pending; no real profile was read or written.
