---
title: M3 Blocker Fixes
date: 2026-10-20
tags: [alteros, verification, m3]
status: historical-focused-editor-pass-full-suite-unconfirmed
---
# M3 blocker fixes

This is the dated 2026-10-20 blocker checkpoint. Its statements that the room
lacked the restored dispenser bindings or that no conversation-facing
`ActivityInteractionBridge` existed describe the earlier state and are superseded
by the current [[../Tracking/M3 Furniture and Activities Handoff]] and
[[../Tracking/Room Testbox Handoff]]. The 51/51 focused Activities result below
remains historical evidence; no M3 Play Mode/player acceptance is implied.

Related: [[../Tracking/M3 Furniture and Activities Handoff]], [[../Tracking/Scenario Conversation Handoff]].

## Confirmed causes and fixes

Verified Unity6000.4.4f1 instance `alteros@48a268a4`, project root
`E:/Pleiades/alteros`, initially on ModelTest, Play Mode off.

- Activities baseline job `3ddac2ed1e864998acd7a3d979d12606` reproduced48/50.
  Both hydration fixtures looked for hydration on `AlterOSProgressionState`,
  rather than its nested `altera` state. Fixed that selection in
  `Assets/AlterOS/Tests/Editor/Activities/ActivityHydrationStoreTests.cs`;
  existing production checkpoint assertions are unchanged.
- Console confirmed native `NavMeshPath` allocation in a MonoBehaviour field
  initializer at `UnityActivityMotion.cs:14`. New
  `UnityActivityMotionLifecycleTests.SerializedMotionPrefabReloadsWithoutConstructorException`
  reproduced the exact exception, job `868a933edb234f67afa9a3f15e59689b`.
  Fixed `Assets/AlterOS/Activities/UnityActivityMotion.cs` to allocate in Awake
  or guarded Editor path-query entry, never managed component construction.
- Regression round-trips a unique temporary prefab using Editor APIs and
  AssetDatabase cleanup. An earlier scene-based attempt hit the unsaved-untitled
  scene restriction and was replaced without saving/discarding user scenes.
  Initial filtered run discovered zero tests before import, not a valid pass.

## Verification

Current focused `AlterOS.Tests.Activities`: **51/51 passed, 0 skipped**, job
`f0dcde7e528847939a4bad44008f6aaa`, reported duration2.1375887 s. Includes
both hydration tests and real serialization regression. Local TestResults.xml
independently reports51/51. No temporary MotionLifecycle prefab remains.
Unity imported/compiled the fixes for this run; subsequent Console query showed
no remaining navigation constructor exception (the bridge classified its
test-result-save notice as an exception).

Full `AlterOS.Editor.Tests` job `dcf107dce187402caadc81a5776091c7` was started,
but result query timed out. Subsequent Editor-state query became unresponsive
and was abandoned. Full suite result and final Editor state are **unconfirmed**;
last available TestResults.xml still belongs to the focused51-test run.
No force-close/restart/batch bypass. No Play Mode, physical E, locomotion,
drinking animation, persistence round-trip, Splash transition, provider/TTS or
key access. No scene save; ModelTest SHA-256 is unchanged:
`368471B00C9CBF3C905719D71D7C5CC4A88B3DE12491DBC76C8B60010A33DEB1`.

## Actions exist; integration remains

`MovementActivity` implements Come here and Follow; `DrinkWaterActivity` owns
dispenser phases/checkpoints. ActivitySceneHost and ScenarioActivityAdapter
exist. Current room has cleared NavMeshData and dispenser bindings, so the
actions are unavailable there. Narrow source check also found production
`DirectConversationBridge` still supplies an empty capability list and no
binding from that path to `ActivitySceneHost.ScenarioPort`; no separate
ActivityInteractionBridge exists. Fixing tests does not finish player integration.

M3/Room Testbox next owns restoring the agreed dispenser/use-point and baking
navigation for the current shell, then coordinating host capability/result and
E/Stop wiring with the conversation owner. Furniture contract is already approved;
no design restart or model-permission bypass is needed. Check Editor health
normally before further mutation, preserving unsaved work and the current room.

Latest follow-up: the Unity MCP instance registry no longer lists
`alteros@48a268a4`; it lists only an unrelated project. No further Editor tools
were used after that read-only check. The correct ModelTest Editor must become
available before restoring the approved furniture layout, baking navigation,
or wiring scene references.
