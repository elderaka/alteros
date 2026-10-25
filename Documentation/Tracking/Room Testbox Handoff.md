---
title: Room Testbox Handoff
date: 2026-10-19
updated: 2026-10-22
tags: [alteros, room, testbox, handoff]
status: furniture-implementation-ready-live-test-pending
---
# Room Testbox Handoff

## Room shell and dispenser baseline (2026-10-20)

The dimensions, NavMesh query and SHA below describe the shell/dispenser
checkpoint on 2026-10-20, not the latest scene hash. The 2026-10-22 integration
section below is the current ModelTest state, including the North wall opening,
furniture inventory/placement wiring and newer validation evidence.

At the user's direction, the previous Testbox environment was removed from
Assets/Scenes/ModelTest.unity and replaced with a neutral greybox shell.
Alterra (scene object Altera), Player, camera/light, dialogue/UI roots and
startup/build settings remain. The approved single WaterDispenser test fixture
was restored under RoomTestbox with its typed `needs.drink_water` use point and
the host's NavMesh, movement, hydration and activity-definition references.

- RoomShell/Floor: 8 m x 8 m x 0.2 m centered at (0,-0.1,0).
- Four 3 m high walls enclose the shell; there is no ceiling or doorway.
- WaterDispenser sits at `(-3.10,0,2.40)` with a primitive collider body and
  Approach/Pose/Use/Exit transforms. Its use point IDs are
  `testbox.water_dispenser` and `testbox.water_dispenser.drink`.
- Use-point local transforms: Approach `(0,0,-0.80); yaw 0`, Pose
  `(0,0,-0.70); yaw 0`, Use `(0,1.10,-0.35); yaw 0`, Exit
  `(0,0,-1.50); yaw 180`.
- Player and Altera retain their saved positions and scene components.
- ModelTest SHA-256 at that checkpoint: `A649D42F80DD29710489E7713AA825D5FFB0A94BC142DA067DAEA0A957DC90D7`.
- Unity Editor hierarchy confirms 9 scene roots, including Player, Altera and
  RoomTestbox; the room root has RoomShell and WaterDispenser. ActivitySceneHost
  has the three definitions, Altera motion/hydration references, one furniture
  use point, and assigned `Assets/AlterOS/Testbox/ModelTestNavMesh.asset`.
- The assigned NavMesh was rebuilt in Unity from 8 room/dispenser collider
  sources and contains 14 triangles. Editor queries sampled Altera, Player and
  the dispenser ApproachPoint; Altera-to-approach path status is complete.
- NavMesh asset SHA-256 at that checkpoint:
  `901B0A209DA439E514CA55FE2AADCDC72B4E74EA5844CF4FCAF356783A98FF03`.

The room shell and dispenser were authored and saved through Unity Editor.
This is editor/query evidence only: no Play Mode movement, interaction, drinking,
or gameplay acceptance has been run.

## Furniture inventory/placement integration (2026-10-22)

The approved furniture continuation is now wired in the existing `ModelTest`
scene. The `Player` has one `FurnitureInputController`; `RoomTestbox` has one
`FurnitureSceneAdapter` and `FurnitureHandlingController`, reusing the existing
`ActivitySceneHost` and `AlteraProgressionStore`. `RoomShell/Floor` owns the
`room.floor` support frame. The HUD Canvas contains the Editor-authored inventory
panel and the room contains the shared-state diagnostics view. The existing
`ActivityDebugOverlay` receives the same off-by-default DebugModeState.

`Player.Interact` is keyboard T (its existing gamepad binding remains). When not
holding furniture, E opens inventory and Q picks up the targeted placed item.
While holding, left mouse or Q confirms a valid placement; right mouse or E
returns the held item to inventory; wheel or Z/X rotates in the floor plane; C
toggles grid snapping; and F cancels/restores the prior pose. E inventory is
blocked while holding. F5 controls the sole shared `DebugModeState`. The visible
TBA tab is disabled. These controls are Editor-wired; live key dispatch remains
unverified.
The inventory and placement preview discover definitions from the canonical
`StreamingAssets/Furniture/catalogue.json`; no duplicate catalogue was created.
Both `room.floor` and the North opening support
`room.wall.north.opening.01` (`wallOpening`) are authored and referenced by the
handling controller. The opening is 1.8 m x 1.4 m, centered at room-local
`(0, 1.9, 4.1)`. New furniture uses placeholder visuals and is not installed as a
new ActivityHost use point.

Catalogue definition discovery and executable furniture discovery are separate.
The inventory lists catalogue definitions/counts; the room layout snapshot stores
stable placed-instance IDs and room-local XYZ pose/support data. `ActivitySceneHost`
uses its serialized `FurnitureUsePoint` references to build the current registry;
the registry's activity/edit claims are per exact registered use point. The
runtime visual proxy created for a placed item is not automatically registered
with an executor or capability. The TV's catalogue `tv.view_target` is viewing
geometry only, not a usable Inspect TV action. See
[[Furniture Inventory and Debug Design#Furniture discovery and action boundary]]
and [[Scripted Actions and Inspect TV Design]].

Current implementation and verification details, including hashes, are recorded
in [[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].
The Editor scene is saved clean. Furniture 84/84, Persistence 23/23 and focused
Unity EditMode checks 29/29 passed in the current follow-up. Scenario Runtime
67/67 and Unified Conversation 197/197 also pass on the current source. No Play
Mode, physical key dispatch, Game View review, restart persistence or
player-profile check was run. See [[Furniture Inventory and Debug Design]] and
[[Furniture Inventory and Placement Implementation Plan]] for the approved
controls and live acceptance boundary.

The current scene SHA-256 is recorded in the linked pre-live report; the SHA
shown in the 2026-10-20 shell/dispenser baseline above is historical.

## Previous fixture result (superseded 2026-10-20)

This earlier fixture is retained here as design/history only; it is no longer present in ModelTest.

Unity 6000.4.4f1, project root `E:/Pleiades/alteros`. The active render pipeline is Built-in; the fixture's materials use `Standard`.

## Previous room and layout

- Floor: 9 m x 9 m primitive Plane centered at `(0,0,0)`, with MeshCollider.
- Boundaries: side and far walls are 2.8 m high; short south wall segments leave a 5.5 m open entrance. Primitive wall colliders are present.
- Lighting/camera: retained the existing Directional Light and Player child `Main Camera`; no new camera, light, renderer, or runtime behavior was added.
- Player starts at `(-1.65,1.40,-3.10)`, facing `+Z`; Alterra starts at `(1.65,0.064,-3.10)`, facing `+Z`.
- Layout leaves open circulation between the entrance, study area, dispenser and couch/TV station. Furniture is simple colored primitive geometry.

| Fixture ID | Scene root | Position | Placeholder |
| --- | --- | --- | --- |
| `testbox.study_desk` | `StudyDesk [testbox.study_desk]` | `(-1.30,0,0.55)` | Blue desktop and four legs |
| `testbox.study_seat.player` | `PlayerSeat [testbox.study_seat.player]` | `(-1.85,0,-0.45)` | Cyan chair, adjacent to Alterra seat |
| `testbox.study_seat.alterra` | `AlterraSeat [testbox.study_seat.alterra]` | `(-0.75,0,-0.45)` | Green chair, adjacent to Player seat |
| `testbox.water_dispenser` | `WaterDispenser [testbox.water_dispenser]` | `(-3.10,0,2.40)` | Yellow cabinet, cyan bottle |
| `testbox.living_room.couch_tv` | `CouchTVStation [testbox.living_room.couch_tv]` | `(2.40,0,2.50)` | Purple couch, dark stand/screen with orange display |

## Previous point transforms

Each of the five furniture roots parents `ApproachPoint`, `PosePoint`, `UsePoint`, and `ExitPoint`. Small colored sphere visuals identify the point roles in the scene: green approach, blue pose, yellow use, red exit. Marker visuals have no colliders. Coordinates and rotations below are local to the named parent; yaw is around Y, with `0` facing local `+Z` and `180` facing local `-Z`.

| Parent ID | Approach (position; yaw) | Pose (position; yaw) | Use (position; yaw) | Exit (position; yaw) |
| --- | --- | --- | --- | --- |
| `testbox.study_desk` | `(0,0,-0.80); 0` | `(0,0,-0.70); 0` | `(0,0.84,-0.20); 0` | `(0,0,-1.50); 180` |
| `testbox.study_seat.player` | `(0,0,-0.80); 0` | `(0,0.47,0); 0` | `(0,0.82,0.65); 0` | `(0,0,-1.50); 180` |
| `testbox.study_seat.alterra` | `(0,0,-0.80); 0` | `(0,0.47,0); 0` | `(0,0.82,0.65); 0` | `(0,0,-1.50); 180` |
| `testbox.water_dispenser` | `(0,0,-0.80); 0` | `(0,0,-0.70); 0` | `(0,1.10,-0.35); 0` | `(0,0,-1.50); 180` |
| `testbox.living_room.couch_tv` | `(0,0,-1.20); 0` | `(0,0.47,0); 180` | `(0,0.58,-0.32); 180` | `(0,0,1.10); 0` |

The desk's seat pairing is documented by the adjacent player/Alterra seat IDs; the original room fixture has no desk-pairing schema or consumer. These are scene test markers only. The original room pass did not introduce a production furniture schema, activity controller, reservation framework or capability authority; M3 later added its scoped dispenser point, activity host and testbox NavMesh described below.

## Retained project assets from the previous fixture

- Assets/Scenes/ModelTest.unity — current room is the shell described above.
- The former environment furniture was removed; the approved dispenser point is restored as the only furniture use point in the new room.
- RoomTestbox's ActivitySceneHost now references its three definitions, Altera motion/hydration, one dispenser point, and the assigned NavMeshData.
- `Assets/AlterOS/Testbox/ModelTestNavMesh.asset` — current 14-triangle navigation mesh built by Unity Editor from 8 current room/dispenser collider sources. The 63-triangle mesh below is historical from the superseded layout.
- `Assets/AlterOS/Testbox/Activities/` — `ComeHere.asset`, `FollowMe.asset`, and `DrinkWater.asset` activity definitions with Unity-generated `.meta` files.
- `Assets/AlterOS/Testbox/Materials/` — `RoomFloor`, `RoomWall`, `DeskBlue`, `PlayerSeatCyan`, `AlterraSeatGreen`, `WaterYellow`, `CouchPurple`, `TVDark`, `TVOrange`, `PointApproach`, `PointPose`, `PointUse`, and `PointExit` materials, plus Unity-generated `.meta` files.
- `Assets/AlterOS/Testbox/RoomTestboxLayout.png` — positioned Editor camera capture of the layout; not Play Mode or gameplay proof.
- SHA-256: `ModelTest.unity` `F6DD4D0E93B62586490EC44A0D63896371CE555E13503A60F75A2C9E287A041C`; `ModelTestNavMesh.asset` `2D9D0BABD7BFF9D1DDC9716C42A4D8651D177438928F2D97D23525CF0CAEBDC4`; `RoomTestboxLayout.png` `8E71CCC528B17D185CE5A052C4055A5F03BDBCFE8D2613CC241064F58D894D48`.

## Previous fixture verification (historical)

Verified in Unity Editor on 2026-10-19:

- `ModelTest` saved cleanly and remained active; root count is 9.
- The old environment root is absent; Player and Alterra roots remain at the positions above.
- `RoomTestbox` has 71 transforms, 20 named point transforms and 24 colliders. No collider exists on any marker branch.
- The floor and wall primitives have colliders; all furniture pieces are primitive colliders.
- During original room fixture authoring, no project script was added or changed. No Unity test suite or Player/Play Mode run has been performed for M3.

The latest Unity Console query after the M3 conversation-hook refresh (2026-10-20) had zero errors. The earlier unrelated Scenario test errors in `Assets/AlterOS/Tests/Editor/Conversation/DirectConversationCoordinatorTests.cs` are historical and were not changed in this pass.

The screenshot is an Editor composition capture. It shows Alterra in the existing model's authoring pose; it does not establish animated/player-visible acceptance. Navigation, reachability, seating alignment with a runtime rig, furniture use, activities, player interaction and gameplay support remain unverified. The current UI and no-key startup behavior were not retested after the scene edit.

## Next acceptance check

In an approved safe Play Mode path, verify `SplashScene -> ModelTest`, room scale,
player view, Alterra grounding, collision/circulation, T interaction, E inventory
and held-item return, Q/mouse placement controls, movement/activity exchange and
Stop behavior using current prompts, restart persistence, and drink checkpoints.
Confirm E opens inventory only when furniture is not held before claiming
player-visible acceptance.


