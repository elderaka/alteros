---
title: Furniture Foundation F4 Source
date: 2026-10-21
tags: [alteros, verification, furniture, f4]
status: historical-source-checkpoint-superseded
---
# Furniture Foundation F4 Source

Related: [[../Tracking/Furniture Foundation Implementation Plan]],
[[../Tracking/Furniture Foundation Handoff]],
[[../Tracking/Content Workbench Shared Contract]],
[[2026-10-21 Activity Scenario Followups]].

Historical F4 source checkpoint. The keyboard mapping and editor-wiring gaps
below reflect 2026-10-21 evidence; keyboard `Player.Interact` is now mapped to T,
and the I1-I5 Editor integration is recorded in the current
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].
The source/compile counts below remain valid for that dated run only.

## Scope and behavior

F4 adds a thin Player E adapter over the shared furniture catalogue and exact
scene use points. The installed dispenser action is `needs.drink_water`, whose
executor is Alterra. The E prompt is **Ask Alterra to get water**. Actor binding
does not turn this into Player drinking or Player hydration.

The option requires a valid catalogue mapping, exact active `FurnitureUsePoint`,
installed capability, reachable approach, current point revision and a current
click binding. The binding includes actor, capability, definition, instance,
functional point, use-point ID, point revision, catalogue hash and one generated
request identity. The identity remains stable across E target refresh while the
binding remains current, then rotates after a click or point revision change.
Stale bindings require a refreshed target and another click.

Requests enter the existing `ActivityHost` through
`ActivitySceneHost.TryRequestFurnitureUse`. They have a unique furniture run ID
derived from the binding request ID and an exact target capability binding.
Same-identity retry returns the previous result; a competing request while the
host is active fails as busy. `DirectConversationBridge` updates the host's
Scenario-owner gate from the presentation model's running-or-suspended state.
Standalone furniture requests emit no Scenario accepted/checkpoint/terminal
event queue entries, do not write transcripts, and have no Drink menu follow-up.
Their result cannot be confused with menu Drink's completion→Come here→readiness
bark→next-E closing.

The shared menu Scenario capability retains `available_dispenser` for current
authored content and adds exact reachable use-point IDs for explicit bindings.
Capability values originate in the scene host and active use-point list; catalogue
descriptions do not install an executor. Existing Follow/Drink assets were not
edited. Sip checkpoint effects remain owned by the existing `DrinkWaterActivity`.

## Checks run

- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`: **52 passed, 0 failed**;
  portable assembly has no UnityEngine/UnityEditor dependency.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`: **67 passed, 0 failed**.
- `Tools/Conversation/Check-UnifiedConversation.ps1`: **194 passed, 0 failed**.
- Unity 6000.4.4f1 Roslyn compiled fresh `AlterOS.Runtime`, `Assembly-CSharp`
  and `AlterOS.Editor.Tests` sources using cached `Library/Bee/artifacts/1900b0aE.dag`
  response inputs. New furniture source/test files were explicitly added to the
  response inputs. Outputs were redirected to ignored
  `.superpowers/sdd/F4-source-compile/02f6df58b63d4f91be3021499484c094/`.
  Compilation passed with existing obsolete Unity API warnings. This does not
  update Unity's assemblies or establish Unity import/test execution.
- A fresh source review found the E token was regenerated every frame and the legacy
  `Interact` method could recapture at click time. Both were corrected: capture
  now holds the request identity while its point revision is current, and legacy
  `Interact` only uses an existing captured token. Recompiled after the fix.
  The reviewer rechecked both corrections and found no remaining concrete issue
  in those paths.
- Unity `mcpforunity://editor/state` retrieval stalled; the read was stopped.
  No Unity scene/prefab/Canvas/meta edits were made. Editor tests, pointer and
  controller tooltip/focus behavior, Play Mode, E movement, audio and
  SplashScene→ModelTest remain unverified.

## Next gate

Import in the correct Unity 6000.4.4f1 Editor, inspect Console and run the
compiled Editor tests. Author and wire the Canvas choice template through the
Editor, with a separate focus/hover target for disabled locked choices. Preserve
the existing room, serialized GUIDs and M3 Follow/Drink flow. Then check aimed E,
busy Scenario/activity refusal, stale click/reconfirm and the SplashScene startup
path. This report is source/compiler evidence only; it is not a player pass.
