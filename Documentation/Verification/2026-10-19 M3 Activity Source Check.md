---
title: M3 activity and ModelTest integration check
date: 2026-10-20
tags: [alteros, m3, activities, verification]
status: room-replaced-furniture-and-navigation-pending
---
# M3 activity and ModelTest integration check — 2026-10-20

Related: [[../Tracking/M3 Furniture and Activities Design]],
[[../Tracking/M3 Furniture and Activities Implementation Plan]],
[[../Tracking/Room Testbox Handoff]], [[../SessionZero]].

## Changes in this checkpoint

- Added typed activity configuration/contracts and exclusive `ActivityHost` ownership.
- Added reusable fixed-destination Come here and ongoing Follow policies, plus a Unity NavMesh motion adapter and scene-host composition source.
- Added authored furniture use-points, deterministic exact-point reservation, timed Drink water phases, and per-activity hydration checkpoint grants.
- Added known/unknown hydration state and an activity-only persisted progression writer/bridge. New progression files initialize hydration to 50 as known; old serialized saves with no hydration metadata remain unknown.
- Added source tests for host, movement, furniture, drinking and hydration. Scenario source and Editor presentation files were left untouched; ScenarioActivityAdapter implements the existing request port, exposes Stop/E-exchange controls, and projects correlated terminal events into Scenario wait/suspended events. Three isolated M3 fixture definitions are authored and exercised through ScenarioSession with fake activities. Unity JsonUtility import and UI behavior remain unverified.

## Evidence

- Unity bundled Mono/NUnit external headless harness: **48/48** passed with minimal Unity API and JSON stubs (10 host, 12 movement, 5 furniture, 3 Drink, 3 hydration-sink, 3 Scenario adapter/projection, 12 offline fixture/session checks). Fixture JSON was read through a .NET test shim, not Unity JsonUtility. Rerun with `.superpowers/sdd/M3 Furniture and Activities Implementation Plan/Check-M3.ps1`; output: `task-1-7-fixture-scenario-tests.txt`. It checks source policy only; NavMesh, actual Unity Transform semantics and progression persistence remain unverified.
- Unity Roslyn source compilation against Unity 6000.4.4f1 Core and AIModule plus current Scenario DTO source completed without errors for runtime activity classes. Inspector-assigned fields produce CS0649 warnings in this standalone compile.
- A separate Roslyn syntax compile including the progression bridge completed without errors against the installed Unity assemblies and existing compiled assemblies. Duplicate-type warnings are expected from compiling changed sources beside the prior project DLLs; this is not a Unity project compile.
- Unity built-in `UnityEngine.AIModule.dll` exists. `Assets/Scenes/ModelTest.unity` has `m_NavMeshData: null`; no authored NavMesh data or NavMeshAgent wiring was found in read-only searches.
- Static scene serialization shows the actor's existing Animator prefab override has `m_ApplyRootMotion: 1`. Existing `Assets/Altera/Animation/*.anim` assets are idle/facial-expression clips; no drink-specific clip was found by filename search. This is a movement/visual integration gate, not proof that root motion and a NavMeshAgent can coexist safely.

## Not verified

- User confirmed Unity import was safe because ModelTest had no unsaved scene edits. Unity 6000.4.4f1 imported the scripts. ModelTest now has a typed dispenser point, three activity definitions, a NavMeshData asset, NavMeshAgent/motion/hydration components, and an ActivitySceneHost with references assigned. The room scene saved cleanly.
- Unity Editor path checks resolved Altera, the elevated Player root and dispenser ApproachPoint; a complete route query passed. This is Editor NavMesh evidence, not Play Mode movement or collision evidence.
- Unity Editor generated `Assets/AlterOS/Testbox/ModelTestNavMesh.asset` from 24 enabled ModelTest room colliders, with expanded bounds around the floor. It contains 63 triangles. Editor `NavMesh.SamplePosition` resolved Altera, the Player root at y=1.4, and the existing dispenser approach point. `NavMesh.CalculatePath` from Altera's start to the dispenser returned `PathComplete`.
- Created `ComeHere.asset`, `FollowMe.asset`, and `DrinkWater.asset`; Editor checks confirm all three settings validate. `ActivitySceneHost` on `RoomTestbox` references those definitions, the NavMeshData, `UnityActivityMotion`, the dispenser use point, and `ActivityHydrationProgressionBridge`. The motion adapter points to Altera's `NavMeshAgent` and Player root. Altera's serialized Rigidbody now matches the existing `AlteraInteractable.Awake()` non-pushable state (`isKinematic=true`, `useGravity=false`).
- No Unity EditMode/Test Runner or Play Mode run. Editor path queries do not prove live agent movement, collision avoidance, animation or activity effects. No visual drinking clip, E input/stop UI, persistence round trip, or Splash-to-ModelTest acceptance.
- The latest Unity Console read shows three unrelated in-progress Scenario test compilation errors in `DirectConversationCoordinatorTests.cs`: missing `AcknowledgeScenarioBeat` at lines 90/93 and unsupported `Exists` on `IReadOnlyList<ScenarioEffect>` at line 91. The Scenario-owned files were left untouched. The Editor reports compilation idle and ModelTest saved cleanly.
- Three offline M3 fixture definitions are authored and exercised through ScenarioSession with fake activities. Unity JsonUtility import, presentation/next-E behavior and player-facing Scenario integration remain unverified. Scenario presentation is owned by its separate worker. No provider, TTS or key access.
- Unity-dependent furniture, Drink and progression bridge tests are written but not executed.

## Next checks

1. Let the Scenario owner resolve its test compilation errors, then run focused Unity EditMode checks.
2. Validate agent motion, collision, actor root motion and furniture use in safe Play Mode.
3. Verify E/Scenario presentation and `SplashScene → ModelTest` startup; Scenario presentation remains a separate approval boundary.

## Follow-up (2026-10-20): ModelTest room replacement

- At the user's direction, removed the former RoomTestbox architecture and furniture from `Assets/Scenes/ModelTest.unity`; created an 8 m x 8 m floor and four 3 m walls through the Unity Editor. Preserved Player, Altera, camera/light, dialogue/UI roots, startup/build settings, and the `RoomTestbox` ActivitySceneHost root.
- Cleared `ActivitySceneHost.furnitureUsePoints` and `navigationData`. Its activity definitions and Altera movement/hydration references remain, but no navigation or furniture-dependent capability is scene-available. The old 63-triangle `ModelTestNavMesh.asset` remains on disk, unassigned.
- Unity Editor saved ModelTest and verified 9 scene roots; RoomTestbox contains RoomShell with five collider-backed primitives. Player and Altera roots remain present. ModelTest SHA-256: `368471B00C9CBF3C905719D71D7C5CC4A88B3DE12491DBC76C8B60010A33DEB1`.
- Initial Unity EditMode baseline `AlterOS.Tests.Activities`: 50 total, 48 passed, 2 failed. Both failures were `ActivityHydrationStoreTests` NullReferenceExceptions in test fixture `SetState` before the production checkpoint method. These blockers were fixed in the later checkpoint below.
- No NavMesh rebake, Play Mode, room visuals/materials, movement/collision, E/Scenario presentation, activity effects/persistence, or SplashScene→ModelTest acceptance was verified. The earlier path query and furniture binding apply only to the superseded room.
- Next: discuss furniture-dependent action ownership and use-point behavior; then author the agreed furniture, rebake navigation from the current room, fix the test/lifecycle failures, and complete Play Mode acceptance.

## Blocker-fix follow-up (2026-10-20)

The main session corrected the hydration fixtures to read the nested
`State.altera` object and moved native NavMeshPath allocation out of the
MonoBehaviour initializer. A real temporary-prefab serialization test first
reproduced the constructor exception, then passed after the fix. Focused Unity
Activities EditMode now passes **51/51**, including that regression. See
[[2026-10-20 M3 Blocker Fixes]].

The full `AlterOS.Editor.Tests` query timed out and a subsequent Editor-state
query returned stale data; the Unity process still reports running, but editor
health is unconfirmed. Do not force-close/restart or use a batch bypass. ModelTest
hash remains `368471B00C9CBF3C905719D71D7C5CC4A88B3DE12491DBC76C8B60010A33DEB1`;
no scene save or Play Mode run followed the blocker fixes.

The approved furniture contract remains, but current ModelTest has no furniture
points or NavMeshData. `DirectConversationBridge` supplies an empty capability
snapshot and no ActivityInteractionBridge binds to `ActivitySceneHost.ScenarioPort`.
Restore the approved fixture layout, rebake navigation, and wire capability/result
and E/Stop handling before advertising any activity.











