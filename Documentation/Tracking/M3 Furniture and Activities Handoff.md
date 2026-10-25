---
title: M3 Furniture and Activities Handoff
date: 2026-10-19
updated: 2026-10-22
tags: [alteros, m3, activities, handoff]
status: editor-compiled-scene-wired-playmode-acceptance-pending
---
# M3 furniture and activities handoff

Related: [[M3 Furniture and Activities Design]],
[[M3 Furniture and Activities Implementation Plan]],
[[../Verification/2026-10-19 M3 Activity Source Check]],
[[Room Testbox Handoff]], [[Scenario Conversation Handoff]],
[[Furniture Inventory and Debug Design]],
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

## Current cross-slice status (2026-10-22)

The 2026-10-20 scene hash, room layout and key-specific acceptance statements
below are historical baselines. Current ModelTest wiring and evidence are in
[[Room Testbox Handoff]] and the linked furniture pre-live report. Furniture and
placement remain separate from the M3 activity executors: catalogue definitions
and runtime visual proxies do not install ActivityHost use-points or executors.
Only the scene-registered functional points and host-registered capabilities are
available to activity requests. General discovery for runtime-placed furniture
remains future work; see [[Furniture Inventory and Debug Design]].

Keyboard `Player.Interact` is now T; E opens inventory when empty-handed and
returns a held furniture item. Historical physical-E references below describe
the earlier input binding. This is Editor input wiring, not a live key or gameplay
pass. Current Scenario and Unified Conversation source checks pass 67/67 and
197/197; live M3 activity and player acceptance remain pending.

The dated M3 Drink follow-up's next-interaction closing remains scoped to that
existing activity path. It is distinct from the newer approved Inspect TV rule:
after successful return, that action's owning Scenario opens its authored closing
automatically. The Inspect TV rule requires safe presentation acquisition,
same-run correlation/deduplication and stale-callback rejection, and remains
pending implementation; see the main-owned
[[Scripted Actions and Inspect TV Design]] and
[[Scripted Actions and Inspect TV Implementation Plan]].

## Source checkpoint

Historical source checkpoint 2026-10-21: [[../Verification/2026-10-21 Activity Scenario Followups]] and
[[Topic Scenario Activity Technical Reference]] supersede standalone menu actions:
Follow/Drink now start real authored Scenarios, contextual `Player.Interact`
controls replace the greeting, and drinking chains a return walk before
bark/next-interaction closing. The M3 source 56/56 is a dated checkpoint; current
Scenario/Unified counts and remaining live acceptance are listed above.

- Added the typed activity host/configuration and exclusive request ownership, fixed-destination Come here, ongoing Follow me, Unity NavMesh movement adapter, scene-host composition, typed furniture use-points, exact reservations, Drink water phases, and idempotent hydration checkpoints.
- Added the narrow `AlteraProgressionStore` hydration field/known flag and `ActivityHydrationProgressionBridge`. New state initializes hydration to 50 as known; old saves without hydration metadata remain unknown. There is no hydration decay/tick or HUD work.
- Added `ScenarioActivityAdapter` over the existing `IScenarioActivityPort`. It keeps acceptance separate from terminal results, correlates wait events by run and activity ID, only satisfies `m3.activity_terminal`, maps non-completion terminal reasons to a failed wait, and retains the same activity owner for `Player.Interact` exchange open/close and Stop calls.
- Added isolated `come_here.json`, `follow_me.json`, and `drink_water.json` definitions under `Assets/AlterOS/Testbox/Scenarios/`. Their test-only Topic ID is `m3_testbox`; they are outside `StreamingAssets` and do not modify production catalogues or anchors.
- `ActivitySceneHost` loads its authored `NavMeshData`, waits for a bound agent before building capabilities, and derives the allowed movement/drink capabilities from its registered definitions and reachable typed use points. `UnityActivityMotion` projects the elevated Player root to the NavMesh and measures follow distance on the ground plane.
- The conversation-facing `ActivityInteractionBridge` sits on the existing scene-host boundary. `DirectConversationBridge` captures only host-owned capabilities/objects, submits `request_activity` with the action step checkpoint, feeds accepted/unavailable results into the same Scenario presentation model, and drains correlated terminal events during wait steps. Existing `Player.Interact` opens a safe activity exchange; keyboard T is the current mapping, while the live key remains unverified. The existing greeting Canvas can offer Stop following during a pausable Follow activity. No generated provider/tool schema or transcript owner changed.
- The `ComeHere`, `FollowMe`, and `DrinkWater` assets, movement/hydration source, NavMesh asset, and Altera components remain in the project.

## Prior M3 Editor evidence (superseded room)

- [Check-M3.ps1](</E:/Pleiades/alteros/.superpowers/sdd/M3 Furniture and Activities Implementation Plan/Check-M3.ps1>) passed **48/48** checks on 2026-10-19 using minimal Unity API and JSON test shims. This exercises source policy and `ScenarioSession`, not Unity runtime behavior.
- That 48/48 harness predates the 2026-10-20 player-ground projection and deferred host startup updates; those changes imported into Unity but were not rerun through EditMode.
- The Unity Roslyn compiler compiled activity/Scenario source against Unity 6000.4.4f1 Core and AIModule without errors. A separate syntax compile of the progression bridge against existing assemblies completed with duplicate-type warnings because the modified source was compiled beside the previous project DLL.
- PowerShell parsed and checked all three fixture definitions for unique steps, required routes, bilingual dialogue, supported activity capabilities and the isolated test Topic.
- Read-only ModelTest checks found `m_NavMeshData: null`, no NavMeshAgent, and an Animator prefab override with `m_ApplyRootMotion: 1`. Existing animation assets are idle/facial-expression clips; no drinking clip was found by filename.
- Unity Editor generated `Assets/AlterOS/Testbox/ModelTestNavMesh.asset` from the 24 enabled room collider sources. The resulting 63-triangle mesh resolved Altera's start, the elevated Player root projected to the floor, and the dispenser ApproachPoint; `NavMesh.CalculatePath` reported a complete route from Altera to the dispenser.

## Unity Editor scene state (2026-10-20, superseded room snapshot)

- At the user's direction, the former room environment and furniture children were removed from ModelTest and replaced with a neutral 8 m x 8 m floor and four 3 m walls. Player, Altera, camera/light, dialogue/UI roots, build settings and startup order remain. The approved WaterDispenser test fixture was then restored under RoomTestbox at `(-3.10, 0, 2.40)` with a collider body, typed `needs.drink_water` use point, and the approved Approach/Pose/Use/Exit marker transforms. No `.meta` files were hand-written.
- ModelTest is saved with 9 roots. `RoomTestbox` has its three definitions, Altera `UnityActivityMotion`, hydration sink, the typed dispenser use point, and `Assets/AlterOS/Testbox/ModelTestNavMesh.asset` assigned. The existing NavMesh asset was updated in place from 8 room/dispenser collider sources, yielding 14 triangles. Editor queries resolved Altera, the elevated Player root, and dispenser approach to the mesh; `NavMesh.CalculatePath` reported a complete Altera-to-dispenser route. Scene is saved and clean.
- Unity 6000.4.4f1 script compilation after the conversation hook and scene wiring completed with zero Console errors. The editor was idle, outside Play Mode, and ModelTest remained the active scene. This is compile and editor-query evidence only.
- SHA-256: `ModelTest.unity` `A649D42F80DD29710489E7713AA825D5FFB0A94BC142DA067DAEA0A957DC90D7`; `ModelTestNavMesh.asset` `901B0A209DA439E514CA55FE2AADCDC72B4E74EA5844CF4FCAF356783A98FF03`; `ActivitySceneHost.cs` `7DEF6224B0EED6C11FC63D200BE9432B6A0623CC145A04DBD2EB8050003D2105`; `DirectConversationBridge.cs` `F1F5BF8840C3D50C067267200E0021BB8BEC93841C1D2D29A8EEAD9BA8EB3E45`; `ScenarioConversationPresentationModel.cs` `2E72ED0EE53865DEDF6AA7169DA57E7866BD06F37884402C36E136968A869FC7`; `DialogueManager.cs` `8EF5E3D0F2E0058D471D2DEF438148DA1E98A040383ECDD0512FE42C5E5CE9F7`.
- Initial Unity EditMode baseline ran 50 `AlterOS.Tests.Activities` tests: 48 passed and two hydration fixtures failed; the lifecycle exception also appeared on ModelTest load. Both blockers were fixed and the latest focused Activities result is 51/51; see the later blocker-fix checkpoint.
- No Play Mode movement/collision/root-motion test, furniture availability action, drinking animation, progression persistence round-trip, E input/Stop UI, or SplashScene→ModelTest acceptance. The previous 63-triangle/24-source path query applies to the superseded room; the current shell-specific proof is the 14-triangle/8-source query above.
- Existing animation assets appear limited to idle/facial-expression clips; the actor retains `applyRootMotion=true`, so visual locomotion and drinking remain unproven. The code compiles, but no live Scenario action/result, E pause/resume, Stop choice, or terminal wait transition has been observed in Play Mode.
- The user explicitly approved including the narrow presentation hook in M3 on 2026-10-20. Do not broaden this into new Scenario DTOs, provider protocol changes, TTS, or general furniture relationship discovery.

## Next acceptance steps

Historical E-menu contract: [[../Verification/2026-10-20 E Interaction Menu and Activity Debug]]
supersedes the flat action shortcut menu: fixed entrances, three prioritized/random
topic slots, Favor→Follow/Stop and Together→input. Editor-authored debug Canvas
above Alterra and host lifecycle Console notifications added. Focused Unity63/63;
live UI/log/movement acceptance still pending.

Historical greeting-choice follow-up: [[../Verification/2026-10-20 Activity Greeting Choices]]
records three local action shortcuts using the existing E menu/template. Current
focused Activities EditMode56/56 includes five new availability/menu tests.
Actual menu layout, selection/motion and Stop still require player review.

1. In an approved safe Play Mode path, verify live capability capture, Come here/Follow requests, accepted/unavailable action branches, wait-event correlation, `Player.Interact` pause/resume (keyboard T in the current mapping), and Stop following.
2. Verify dispenser reservation/invalidation, Drink checkpoints, safe stop, hydration persistence and truthful completion/failure dialogue.
3. Review player-visible locomotion/drinking and normal `SplashScene → ModelTest` startup; verify E opens inventory only in its approved furniture context before marking M3 accepted.

## Later blocker-fix checkpoint (2026-10-20)

Earlier48/50 and constructor-error entries above are historical baseline.
Main session fixed native NavMeshPath construction during serialization and the
hydration fixtures' incorrect top-level state selection. Current focused Unity
Activities tests pass **51/51**, including the new red/green prefab regression.
See [[../Verification/2026-10-20 M3 Blocker Fixes]]. Full Editor test result
timed out. The source/scene/bridge changes and 2026-10-20 Editor evidence above
supersede the prior unassigned-room and empty-capability statements.

