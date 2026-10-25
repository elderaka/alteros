---
title: Debug Mode
date: 2026-10-22
tags: [alteros, debug, contract]
status: implementation-ready-live-test-pending
---
# Shared Debug Mode

Related: [[Furniture Inventory and Debug Design]], [[Runtime Observability]],
[[Furniture Foundation Handoff]], [[../Verification/2026-10-20 E Interaction Menu and Activity Debug]].

## Approved contract

Human decision2026-10-21: **F5 toggles one game-wide Debug Mode flag**.
Consumers share its state; do not implement independent module flags or duplicate
F5 listeners. Inventory access and normal furniture placement work regardless of
Debug Mode; being in Debug Mode does not change their input mapping.

| Consumer | Debug Mode off | Debug Mode on |
| --- | --- | --- |
| Furniture supply | Finite test inventory, initially10 per definition | Unlimited furniture supply; display infinity rather than a fabricated large count |
| Inventory / placement | Available, respecting normal modal/held-item input ownership | Still available through the same controls |
| Alterra overhead diagnostic Canvas | Hidden | Shows the existing state/action/Scenario overlay when its build/platform permits it |
| Extra diagnostic views | Hidden | Functional-point cones, relationship evidence and detailed reasons may consume this flag |
| Placement safety and action permissions | Enforced | Still enforced |

The unlimited furniture decision supersedes the earlier draft proposal that Debug
Mode would not affect stock. It is a bounded furniture-supply cheat, not authority
to mint currency, change arbitrary stats, add executors, unlock all dialogue,
bypass collisions/support/reservations or permit provider calls. Validity remains
derived from the actual placement/relationship/host rules.

## Single owner and input integration

Provider-neutral state source now exists as `AlterOS.Debugging.DebugModeState`
under `Assets/AlterOS/Debugging/`. It defaults off and notifies only on real state
changes. The class has no Unity lifecycle; the scene must compose one instance and
pass it to the input, inventory and overlay consumers. Do not add another state
owner if a scene host or controller is implemented.

Exactly one application input owner handles F5 and updates that state. State
changes notify consumers; inventory, furniture and Alterra overlay must not each
poll a separate key or persist their own copy. The source state defaults off;
persistence/release-build exposure must be explicit in the implementation plan,
not silently inferred from being in the Unity Editor.

Keep build eligibility separate from runtime intent. The existing overhead view
currently hides itself outside Editor/development builds; that is not the shared
F5 flag and does not prove Debug Mode is implemented. Existing eligibility guards
can remain until release policy is explicitly changed. A hidden overlay's adapter
must still receive a later enable event; disabling its entire GameObject must not
make it permanently unreachable by the toggle.

Static debug UI composition remains Editor-authored Canvas/prefab content.
Runtime code binds visibility/state or positions genuinely dynamic diagnostics.
Whether relationship shimmer is also useful normal placement feedback remains a
presentation detail; diagnostic-only overlays must use the common flag.

## Finite inventory versus unlimited supply

Unlimited is a supply policy, not repeatedly setting the saved finite count to10,
max-int or infinity. Preserve ordinary finite counts when toggling F5. Mode
switches must not accidentally consume/refund the same held item twice, create
negative counts, remove an already placed object or alter a reserved execution.
Track provenance of a held item's finite-stock claim versus debug supply.

Human decision 2026-10-22: debug-created placed furniture remains in the home
after Debug Mode is turned off; toggling F5 does not delete it. The schema-2
inventory/layout transaction now preserves this behavior in source and synthetic
persistence checks; live profile/restart acceptance is still pending. See
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

Human decision 2026-10-22: returning placed debug-created furniture makes it
ordinary owned inventory stock, exactly once. This supersedes the coordinator's
earlier no-finite-credit recommendation. A held operation must still distinguish
a finite-stock reservation from unlimited supply so mode toggles cannot refund
an unreserved unit. F5 alone must not reset stock or erase an edited room.

Interrupted handling cancels to the last committed inventory/layout state. That
transaction/session path is implemented and Editor-wired through the existing
SQLite game owner. The same pre-live report distinguishes this evidence from
live profile/restart acceptance.

## Current evidence and implementation gate

`DebugModeState` remains the single off-by-default state. `FurnitureHandlingController`
owns the one ModelTest instance; `FurnitureInputController` owns the F5 action;
the same state is passed to inventory rows, `AlterraActivityDebugView`, and
`FurnitureDiagnosticsView`. The existing overlay's Editor/development-build guard
is preserved. Debug Mode changes supply display and diagnostics only; it does not
reset finite stock, reinterpret a held reservation, or bypass placement/activity
guards.

`Assets/InputSystem_Actions.inputactions` now remaps keyboard Interact E→T and
adds E inventory, held-item controls and F5 Debug Mode. The Editor-authored
ModelTest panel/preview/diagnostic prefabs and shared owner are wired. Portable
Furniture checks pass 84/84; focused Unity EditMode checks pass 29/29, including
the placement, inventory, edit-claim and ActivityHost checks. Current Editor
evidence is [[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live
Implementation]].
Physical key dispatch, Game View appearance, SplashScene-to-ModelTest and Play
Mode behavior remain unverified; no game-profile database was opened.
Do not create a separate workbench flag that impersonates the game's state;
synthetic preview context stays labelled.
