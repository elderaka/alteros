---
title: Furniture Inventory I1 Source Verification
date: 2026-10-22
tags: [alteros, furniture, inventory, verification]
status: historical-source-checkpoint-superseded
---
# Furniture Inventory I1 source verification

This I1-only source checkpoint is superseded for current integration status by
[[2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]]. The source
count and limitations below describe the earlier source-only run; I1-I5 Editor
wiring and the current live-test boundary are recorded in the later report.

## Scope

Implemented the portable schema-2 home inventory model and I1 handling session
on top of the existing F1-F4 furniture core. The pure DebugModeState and
FurnitureInputPolicy are also implemented and tested. This is headless source
evidence only; the SQLite adapter, Editor import/tests, scene wiring and player
input are not complete.

## Changed source

- `Assets/AlterOS/Furniture/FurnitureInventory.cs`: per-definition stock DTO,
  fixed starter seed, validation, and the `IFurnitureHomeStore` seam/result DTOs.
- `Assets/AlterOS/Furniture/HomeFurnitureLayout.cs` and
  `HomeFurnitureRepository.cs`: optional schema-2 inventory field, schema-1
  compatibility, schema-2 validation and lossless JSON clone/round-trip.
- `Assets/AlterOS/Furniture/FurnitureHandlingSession.cs`: transient selection,
  move/place/return/cancel, finite stock reservation display, Debug Mode supply
  origin snapshot, revalidation, checked returns and durable-receipt retry.
- `Assets/AlterOS/Furniture/FurnitureLayoutCommands.cs`: combined position,
  rotation and support transform command.
- `Assets/AlterOS/Furniture/FurnitureInputPolicy.cs` and
  `Assets/AlterOS/Debugging/DebugModeState.cs`: pure contextual input mapping and
  off-by-default shared state with idempotent notifications.
- Focused tests in `Assets/AlterOS/Tests/Editor/Furniture/` and the
  `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` test/source list.

## Validation

- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurnitureHandling`:
  17 passed, 0 failed.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`:
  82 passed, 0 failed. It also confirms the portable furniture output has no
  `UnityEngine` or `UnityEditor` references.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group All` passed 15/15 on the
  pre-integration baseline using synthetic temporary databases. That run predates
  schema-2 source changes; the persistence runner still needs the coordinated
  source-list and test additions before it can serve as post-change evidence.

Coverage includes 10 initial units per starter definition, late definitions at
zero stock, schema-1 read compatibility, schema-2 count validation and orphan
retention, finite/debug placement and return, F5 transitions during holds,
cancel and restart truth, move/cancel, busy reservations, attached dependents,
stale source/catalogue/layout, invalid placement, overflow, lost commit
acknowledgement and duplicate receipt replay. The receipt/replay checks use an
in-memory store; SQLite atomicity and schema-1-to-2 migration remain I2.

## Remaining evidence and next step

`Assets/InputSystem_Actions.inputactions` still binds Player.Interact to E;
T/Q/E/F5 are not wired to the pure policy/state. No Input System listener, Canvas,
preview prefab/material, ModelTest reference or scene was authored. `DebugModeState`
has no shared scene composition owner yet. No Unity Editor import or tests,
physics/visual check, Play Mode or SplashScene-to-ModelTest run occurred.

Next, complete the coordinated I2 integration through the existing
`FurniturePersistenceAdapter` and its synthetic persistence harness. The root is
coordinating the adapter/harness ownership window; no game DB schema or SQLite
writer change is currently justified.
