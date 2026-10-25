---
title: Furniture Inventory and Debug Design
date: 2026-10-22
tags: [alteros, furniture, inventory, input, debug, design]
status: implementation-ready-live-test-pending
---
# Furniture inventory and debug design

Related: [[Furniture Mechanics and Catalogue Design]],
[[Furniture Foundation Implementation Plan]], [[Furniture Foundation Handoff]],
[[Scripted Actions and Inspect TV Design]],
[[Scripted Actions and Inspect TV Implementation Plan]].
This records human corrections in the main session after the Room Testbox
inventory handoff. It supersedes the handoff's earlier E/Q/R/T mappings.
Design and plan approved: I1-I5 implementation is complete and ModelTest is
ready for the live implementation test. Current source/editor evidence and the
remaining live-test boundary are in [[../Verification/2026-10-22 Furniture
Inventory I4-I5 Pre-Live Implementation]].
The 2026-10-22 main-session approval also locks the recovery, initial-stock,
debug-return, placement-safety and scope recommendations below. Execution is
tracked by [[Furniture Inventory and Placement Implementation Plan]].

## Approved controls

| Context | Input | Intent |
| --- | --- | --- |
| Not holding furniture | T | Interact with the world/Alterra through `Player.Interact`; replaces the earlier keyboard E binding |
| Not holding furniture | Q | Pick up the targeted placed furniture |
| Not holding furniture | E | Open inventory |
| Global debug input | F5 | Toggle the one shared Debug Mode flag; see [[Debug Mode]] |
| Holding furniture | Left mouse or Q | Confirm valid placement |
| Holding furniture | Right mouse or E | Return the held item to inventory |
| Holding furniture | Mouse wheel or Z/X | Rotate the preview in the floor plane |
| Holding furniture | C | Toggle grid snapping on/off, not a one-shot snap |
| Holding furniture | F | Cancel a move and restore the existing piece to its prior position; a new inventory item returns to inventory |

While holding, ordinary world/Alterra interactions are blocked. The placement
preview remains active. E must dispatch only the held-item operation in this
state; it must not also open inventory. Confirmation, return and undo must likewise
be mutually exclusive per input press. Dialogue/text input and other existing
modal ownership must not receive these as simultaneous world commands.

Rotation increments, grid spacing, scrolling direction and snapshot retention
are implementation details to propose; do not claim that numeric settings were
approved. Floor rotation does not erase a window's required mounting orientation.
The user chose T for talking; binding updates must include prompts/tutorials/tests
and preserve existing interaction behavior behind the newly assigned input.

## Approved inventory and stock semantics

With Debug Mode off: finite **10 starting items per furniture type**, for testing,
not a capacity cap or an automatic refill. With Debug Mode on: **unlimited furniture
supply** through the same inventory. See [[Debug Mode]] for the canonical flag,
overlay ownership and remaining finite/debug transaction questions. Table and
study table are separate catalogue definitions.
Stock is keyed by stable definition ID and discovered from the shared registry.

Human decisions 2026-10-22: furniture created through unlimited Debug Mode remains
placed after Debug Mode is turned off. Returning a placed debug-created chair
to inventory makes it ordinary owned stock, exactly once. No permanently separate
debug inventory is required. Unlimited supply never resets finite counts; the
held operation still tracks whether it reserved finite stock. See [[Debug Mode]].

- Taking a new item for placement holds one unit; it becomes unavailable to a
  second selection. Confirming placement consumes that unit into a placed instance.
- Cancelling/returning a new preview restores that same unit exactly once; no loss
  or duplication on repeated input, save failure, reload or exception.
- Picking up an existing placed piece does not change inventory count. This is
  moving the same owned instance, not purchasing or creating another one.
- Confirming its new placement leaves stock unchanged. F restores its captured
  original position/state, with stock unchanged.
- Right mouse/E returns that existing instance to inventory and adds one unit,
  exactly once, as it leaves the layout. It does not also remain placed.
- Invalid placement consumes nothing. Removing/returning must reject unsafe
  reserved/in-use changes using the same host guards as layout editing.

These stock and handling rules are implemented by the schema-2 inventory/session
and SQLite persistence path. The test inventory starts with 10 additional stored
items per definition; existing room furniture is separately owned. Seed once only,
not every boot/open/toggle. Source and synthetic persistence evidence are recorded
in [[../Verification/2026-10-22 Furniture Inventory I1 Source]] and
[[../Verification/2026-10-22 Furniture Inventory I2 Persistence]]; Editor wiring
is covered by the current pre-live report below. This is not live profile evidence.

Quit/crash/reload cancels unfinished handling: restore an existing piece to its
last committed pose, or release a new item's stock reservation. Persist confirmed
inventory and layout changes together through the existing SQLite game owner;
failed persistence cannot publish only half of a transfer. Active/reserved pieces
cannot be picked up or moved; explain the refusal rather than stopping Alterra.

Catalogue-defined floor/wall/surface support, adjustable grid/rotation increments,
collisions and reservations remain enforced in both supply modes. New sitting,
sleeping and TV executors/cameras, gifting and general-purpose item inventory are
outside this implementation. Catalogue, furniture inventory, placement, save/load,
shared Debug Mode and existing interaction wiring are in scope.

## Earlier Room Testbox brainstorm to preserve

The delegated handoff described grid/held-preview placement, a real-size square
footprint beneath the object, transparent green for valid and red for invalid,
a simple inventory list without sprites, Furniture tab plus inert visible TBA
tab, and diagnostic cone/combination shimmer. Main session has confirmed the
controls, stock and shared F5 flag above. The inventory panel, placement preview,
diagnostic view and durable layout/stock transaction path are now implemented and
wired in ModelTest. Their live Game View and physical-input appearance remain
unverified; see the current pre-live report.

Keep geometry meaning honest: a geometrically safe but relationship-invalid
placement can remain decoration. Blocking safety uses the placement result;
combination shimmer uses actual resolver matches, not mere radius overlap or
global ownership of a table. Color feedback also needs a readable validity reason.

## Debug Mode — approved2026-10-21

[[Debug Mode]] is the single authoritative flag contract. F5 toggles it. Normal
inventory/pickup/place controls and validity preview work with it off; turning it
on enables unlimited furniture supply, Alterra's existing overhead diagnostic
overlay and extra diagnostic views. It does not bypass safety/permissions or
reset the saved finite inventory. This replaces the earlier diagnostics-only
proposal. All modules must consume the same owner, not implement their own flag.

## Implementation gate and evidence

Room Testbox furniture F1–F4 remains the separate arrangement/interaction
foundation. The bounded [[Furniture Inventory and Placement Implementation Plan]]
is approved. Portable Furniture checks pass 84/84, synthetic persistence checks
pass 23/23, and the current focused Unity EditMode checks pass 29/29. ModelTest
has floor and North wall-opening support, shared activity/edit claims, and the
approved inventory/placement UI. No Play Mode, physical key, Game View, restart,
or game-profile acceptance is claimed. See the dated
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

## Furniture discovery and action boundary

The catalogue, room layout and executable-use registries answer different
questions. `FurnitureSceneAdapter` loads definitions from the canonical
`Assets/StreamingAssets/Furniture/catalogue.json`; inventory rows discover those
definitions and counts. This does not mean an instance is placed or executable.
`HomeFurnitureLayoutDocument` stores placed instances by stable instance and
definition IDs, room-local `positionMeters` (x/y/z), rotation, scale and a named
support reference. That layout snapshot is persisted through the existing SQLite
game owner.

Executable furniture use remains tied to an authored `FurnitureUsePoint` with
stable instance/use-point IDs, supported use IDs and approach/pose/use/exit
transforms. `ActivitySceneHost` takes its points from its serialized
`furnitureUsePoints` array when it builds the shared `FurnitureRegistry`;
activity-use reservations and furniture-edit claims share the same per-use-point
claim set, so either blocks the other on that exact registered point.
The room adapter can seed layout data from authored points, but there is no
general live registration path for every furniture instance created from layout.

`FurnitureHandlingController` creates runtime furniture as a visual proxy from a
catalogue definition. That proxy is not automatically a functional point and does
not gain an executor, capability, or claim merely by being in inventory or in the
room. In particular, the TV catalogue's `tv.view_target` is viewing geometry, not
a `FurnitureUsePoint` use ID or an Inspect TV executor. The approved
[[Scripted Actions and Inspect TV Design]] and its plan need a separately reviewed
dynamic functional-point discovery/registration contract before placed TV can be
treated as available. No InspectTV executor, new use ID, dynamic registration or
scene action was added by the inventory work.

That design now approves an automatic authored Scenario closing after a successful
Inspect TV return. The owning Scenario/host must acquire presentation safely,
correlate and deduplicate the same live run, and discard stale callbacks; the
Action itself does not open full dialogue. This is approved design only, pending
the separately owned implementation plan.
