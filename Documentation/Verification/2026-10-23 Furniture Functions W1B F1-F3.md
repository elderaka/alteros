---
title: Furniture Functions W1B F1-F3
date: 2026-10-23
tags: [alteros, furniture, reusable-content, verification]
status: source-ready-integration-pending
---

# Furniture Functions W1B F1-F3 source verification

## Result

W1B implements the Furniture-owned F1-F3 source contracts after Main accepted
the W1A ports and handlers. It adds optional schema-1 function/presentation
opt-ins, fail-closed target validation, the public reservation-context seam,
and committed power-state projection. The canonical
`Assets/StreamingAssets/Furniture/catalogue.json` was not edited; SQL fixture and
catalogue integration remains in the later W5 window.

This is source evidence. DBMS durable power writes, Behavior's implementation of
the reservation-context source, scene wiring, and live presentation remain open.
The normal Unity refresh/compile observation is now error-free, though Unity tests
remain deferred until Main opens the next focused Editor window.

Round 1 review corrections are applied. The Unity-only bridge tests now resolve
`Assembly-CSharp` components with reflection instead of linking the predefined
bridge assembly from `AlterOS.Editor.Tests`. `useId` now reaches
`FurniturePowerCommand` and the fake store's immutable receipt payload. Before
that correction, the new same-operation/different-use regression returned
`replayed`; after the correction it returns the required `conflict`.

Round 2 also adds `using Object = UnityEngine.Object;` to the conditional Editor
test file, resolving the `System.Object`/`UnityEngine.Object` ambiguity reported
in review. The portable harness excludes that `UNITY_EDITOR` branch; the Unity
refresh has completed without Console errors, but the focused EditMode tests have
not been run.

DBMS review also required a shared single-definition validator and complete
Scenario provenance on power commands. `FurnitureCatalogueValidator` now exposes
`ValidateDefinition(FurnitureDefinition)`, which shares the catalogue's
per-definition rules without requiring starter entries or catalogue-wide
relationships, capabilities, or aliases. The power request and command carry the
containing Flow `stepExecutionId` independently of physical `activityId` and inner
`sequenceStepId`; missing identity fails closed, and changed Flow-step identity
conflicts on operation replay.

## Changes

- `Assets/AlterOS/Furniture/FurnitureCatalogue.cs` — optional `functionBindings`
  and `presentationBindings` typed descriptors. They identify a registered
  function/descriptor and its definition-local point/use pair.
- `Assets/AlterOS/Furniture/FurnitureCatalogueValidator.cs` and
  `FurnitureCatalogueRegistry.cs` — schema 1 remains valid when optional metadata
  is absent. Only the trusted `power.set` function and
  `dispenser.collect_mock` presentation descriptor are accepted. Unknown IDs,
  duplicate entries, and point/use mismatches fail closed and invalidate their
  definition. `ValidateDefinition(FurnitureDefinition)` shares the same rules
  with reusable-content import without requiring the full starter catalogue.
  Metadata does not install a handler.
- `Assets/AlterOS/Furniture/FurnitureFunctions.cs` — `FurnitureFunctionRequest`
  now requires the reserved `useId` and containing Flow `stepExecutionId`. The public context API is:

  ```csharp
  FurnitureFunctionReservationEvidence
      IFurnitureFunctionReservationContextSource.ResolveCurrent(
          FurnitureFunctionReservationRequest request)

  FurnitureFunctionTargetValidation
      IFurnitureFunctionSceneTargetResolver.ValidateCurrent(
          FurnitureFunctionReservationRequest request, string cataloguePointId)
  ```

  The query pins activity, instance, definition, physical use-point, use ID and
  point revision. Evidence returns the current activity, instance, use-point,
  use ID and point revision. It must be produced only after the owning Behavior
  adapter validates its existing `FurnitureBinding` with
  `FurnitureRegistry.IsValid(binding)`; a query is not proof.
- `Assets/AlterOS/Furniture/FurnitureFunctionTargetAdapter.cs` — durable and
  transient target validators check catalogue opt-in, re-resolve the scene point,
  select the point matched by the current scene alias when a definition has more
  than one compatible point, and compare the owner evidence. Missing proof is
  unavailable; mismatched proof is a conflict.
- `Assets/AlterOS/Furniture/FurniturePowerFunction.cs` — copies the validated
  `useId` and containing Flow `stepExecutionId` into `FurniturePowerCommand`. The
  `SetPower(FurniturePowerCommand)` port signature is unchanged; the typed command
  preserves exact use and Flow-step identity through durable validation and replay.
- `Assets/Scripts/Furniture/FurnitureSceneAdapter.cs` — implements the scene
  target resolver. It checks a unique active use point, supported use ID, point
  revision, current claim presence and catalogue alias mapping. It also exposes
  `ApplyCommittedPowerState` for a committed layout instance.
- `Assets/AlterOS/Furniture/FurniturePowerStateProjection.cs` — maps absent or
  unknown `device.power` to `Unknown`, known `0/1` to `Off/On`, and malformed or
  duplicate state to `Invalid`.
- `Assets/Scripts/Furniture/FurniturePowerStateView.cs` — applies the projected
  enum through per-renderer `MaterialPropertyBlock` values. It does not modify
  shared material assets.
- Tests cover optional schema-1 opt-ins, invalid descriptors, exact target and
  owner evidence, two synthetic TV definitions with per-instance fake state,
  transient mock opt-in, single-definition validation, useId and Flow-step command
  propagation, missing Flow-step rejection, changed-use and changed-Flow-step
  operation conflicts, and power projection. The EditMode file also contains
  regressions for real scene alias/claim/revision resolution and shared-material
  isolation; those conditional Unity tests were not run in this source window.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` includes the projection
  tests in the portable harness.
- `Documentation/Tracking/Furniture Functions Framework Plan.md` records the
  source status and downstream gates.

No ActivityHost, FurnitureRegistry, Scenario, Persistence, Content, Authoring,
Workbench, catalogue JSON, scene, prefab, serialized asset or `.meta` file was
hand-edited. The Unity Editor generated metadata for the four new scripts. The
Unity-only scene/view scripts and conditional EditMode tests were not compiled
by the portable harness.

## Validation

- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurnitureCatalogueTests`
  — **18 passed, 0 failed** after adding the shared single-definition seam.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurnitureFunctionTests`
  — **19 passed, 0 failed** after adding Flow-step correlation.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurniturePresentationTests`
  — **18 passed, 0 failed**.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter FurniturePowerStateProjectionTests`
  — **4 passed, 0 failed**.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` — **128 passed, 0 failed**
  before the Round 1 correction. Main asked not to repeat the accepted W1A suites;
  after the correction, the affected function suite and portable compilation
  passed **17/17**. Portable output has no UnityEngine or UnityEditor references.
- All runs report the existing `CS0649` warning for
  `ContentRelease.compilerAttestation` in the shared Content contract; no Content
  source was changed.
- Main's isolated import attempt initially showed two `CS0103` errors in
  `LegacyContentAdapter.cs` because Unity had not registered the new
  `ReusableContentDefinitionAdapter.cs`. A normal full-assets refresh generated
  its `.meta`, added the source to `AlterOS.Runtime.rsp`, and cleared the missing
  type error. Unity is now idle/ready with 0 Console errors; ModelTest remains
  clean and Play Mode is off. Per Main, Unity tests remain deferred until DBMS
  source fixes settle. No Play Mode or live acceptance was attempted.

Artifacts are under:

`E:\Pleiades\alteros\.superpowers\sdd\Furniture Inventory and Placement Implementation Plan\`

| Run | Artifact directory |
| --- | --- |
| Red changed-use regression | `489a76cc53f14d618a3300cb0fc744b3` |
| Catalogue focused | `8c9be2d74dd64a229fe893ef548aadc1` |
| Function focused after Round 1 | `caf2f1b430884f9aa5d0ba296dbc29be` |
| Catalogue focused after validator seam | `5b63d8667a1b432882ccdb498fa1deb5` |
| Function focused after Flow identity | `468ddac7d70c4c96b449089e73163c4a` |
| Presentation focused | `8232497741654f66b2520cd561351349` |
| Projection focused | `34620694db6243bd8b634f8d9e3d1ab1` |
| Full portable Furniture before Round 1 | `81f90d559e7f4cf083771c617903292e` |

## Remaining acceptance

- DBMS implements `IFurniturePowerStore` over its existing transaction/CAS/receipt
  owner and verifies instance/layout/game revisions, events and replay behavior.
  `FurniturePowerCommand.useId` and `stepExecutionId` are part of the exact command
  payload; DBMS was notified of these producer fields.
- Alterra Behavior implements `IFurnitureFunctionReservationContextSource` from
  the exact active `FurnitureBinding`; `FurnitureRegistry.IsValid(binding)` must
  succeed. Populate `FurnitureFunctionRequest.useId` from that binding and
  `stepExecutionId` from `ActivityPrimitiveIdentity.stepExecutionId`. Do not
  introduce another reservation registry.
- Later SQL fixture/catalogue work opts the appropriate TV and dispenser
  definitions in. The canonical JSON source remains unchanged in this W1B slice.
- Unity must import the new scripts and generate their `.meta` files. The
  Editor-authored view references, material support for `_FurniturePowerState`,
  caller from the committed layout refresh, and actual per-instance visual
  behavior remain unverified.
- Live TV/dispensers and inventory acceptance remain pending the DBMS synthetic
  persistence-root seam, Behavior/W2 integration, and Main's later isolated
  Editor release. No Play Mode or game-profile write was performed.

## W4 ModelTest Editor wiring — 2026-10-24

This dated W4 record supersedes the earlier pending scene/view wiring statements
in the W1B checkpoint above. W5 SQL authoring/release and live/player gates remain
open.

Main released the exclusive Furniture W4 Editor window after the compiled
catalogue consumer passed review and focused EditMode validation. Work targeted
`E:/Pleiades/alteros`, Unity `6000.4.4f1`, instance `alteros@48a268a4`, and only
the already-open `Assets/Scenes/ModelTest.unity`. The scene was clean before
editing and clean after the explicit save. The pre-W4 scene hash prefix captured
in-session (`77A01910EBD2639569DFB151E28D3A89DB94D62E50A4B911DD08`) matches Main's
full initial RCF baseline SHA-256
`77A01910EBD2639569DFB151E28D3A89DB94D62E50A4B911DD08C7D70CC5AD8B`; no separate
pre-edit backup file was created. The saved W4 scene SHA-256 is
`665BE154935EDCCEDD6DA2041230F21DB454FA781E614705AD3FD3EA78F5AA58`.

Only the existing `RoomTestbox` received fixture changes. Its nine scene roots,
Player, Altera, room shell, camera, light, canvases, startup wiring, floor,
supports, dispenser geometry and Navigation data reference were retained. The
existing `WaterDispenser` keeps instance `testbox.water_dispenser`, use point
`testbox.water_dispenser.drink`, and its original Approach/Pose/Use/Exit refs. Its
physical `supportedUseIds` now retain `needs.drink_water` and add `dispenser.collect`
for the mock presentation path; no drink, hydration, item or currency effect was
added. This changed the point's local revision; no revision value is hard-coded in
content.

`ActivitySequenceScenePrimitivePort` is attached to `RoomTestbox` and assigned to
the existing `ActivitySceneHost.sequencePrimitivePortComponent`. Its serialized
refs target that host, the current `FurnitureSceneAdapter`, Altera's existing
`AlteraProgressionStore`, `Altera/AmbientBubble`, room root `RoomTestbox`, and room
ID `room.testbox.modeltest`. The host's existing `UnityActivityMotion` remains on
Altera; its `navigationData` remains
`Assets/AlterOS/Testbox/ModelTestNavMesh.asset`. The host's `furnitureUsePoints`
array now contains exactly the dispenser and the two TV points. No second host,
registry, reservation system, or persistence owner was created.

The adapter's serialized source mode is explicitly `CompiledSql` and its legacy
TextAsset ref is null. The installed SQL release still lacks the new global
catalogue settings and `furniture.tv.test_variant` until W5, so runtime catalogue
availability remains gated there. No JSON/StreamingAssets fallback was selected
or used.

Two independent colored-box fixtures were added under `RoomTestbox`:

| Scene object | Position | Instance ID | Use point ID | Definition ID | Body material |
| --- | --- | --- | --- | --- | --- |
| `TV_Basic_FixtureA` | `(-1.6, 0, 2.7)` | `testbox.tv.basic.fixture_a` | `testbox.tv.basic.fixture_a.view_target` | `furniture.tv.basic` | `TVBasicBodyBlue.mat` |
| `TV_TestVariant_FixtureB` | `(1.6, 0, 2.7)` | `testbox.tv.test_variant.fixture_b` | `testbox.tv.test_variant.fixture_b.view_target` | `furniture.tv.test_variant` | `TVVariantBodyAmber.mat` |

Each has a floor-supported fixed-size TV-shaped body, a visible screen, its own
`FurnitureUsePoint`, `FurniturePlacedInstanceIdentity`, and
`FurniturePowerStateView`. Both points expose `tv.power` and `tv.viewing`; each
view binds only to its own screen Renderer. The screens share
`FurniturePowerStateFixture.mat`, whose
`Assets/AlterOS/Testbox/Materials/FurniturePowerStateFixture.shader` consumes
`_FurniturePowerState` with `Unknown=0`, `Off=1`, `On=2`, and `Invalid=3` colors.
The initial material/view state remains Unknown (`0`); neither TV was set to Off
or On. No per-TV runtime code branch or shared-material mutation was added.
The ShaderLab fixture is the only W4 source asset; no C# runtime or test source
was changed.

Unity generated the `.meta` files. Supporting assets and GUIDs:

- `FurniturePowerStateFixture.shader` — GUID `cc9e220dc048f7a4daa86cb3e2600005`
- `FurniturePowerStateFixture.mat` — GUID `ed32c3676c63fa24cb10c9bd0cd43b31`
- `TVBasicBodyBlue.mat` — GUID `8fdfe70dd79b6e248ac7f110919a8d8b`
- `TVVariantBodyAmber.mat` — GUID `9460cb05446a24445b5e2a841ef0cfbf`

W5 must publish one reviewed release containing global settings that preserve all
eight starters, existing relationship/capability rules, and both existing
dispenser aliases, plus these four TV aliases:

```json
[
  { "legacyId": "testbox.tv.basic.fixture_a", "definitionId": "furniture.tv.basic" },
  { "legacyId": "testbox.tv.basic.fixture_a.view_target", "definitionId": "furniture.tv.basic", "pointId": "view.target" },
  { "legacyId": "testbox.tv.test_variant.fixture_b", "definitionId": "furniture.tv.test_variant" },
  { "legacyId": "testbox.tv.test_variant.fixture_b.view_target", "definitionId": "furniture.tv.test_variant", "pointId": "view.target" }
]
```

Both TV definitions need a compatible fixed-scale `roomFloor` support, the same
`view.target` point (`tv.view_target`, local position `(0, 0.8, 0)`, local facing
`(0, 0, -1)`), and use IDs `tv.power` / `tv.viewing`. Each compiled
`FurnitureFunctionBindingDescriptor` needs `functionId=power.set`,
`pointId=view.target`, and `useId=tv.power`. The Workbench proof source may carry
its separate contract-version metadata; the compiled Furniture descriptor itself
contains those three fields. The dispenser definition must retain point `drink`,
add use ID `dispenser.collect`, and bind presentation descriptor
`dispenser.collect_mock` to `pointId=drink` / `useId=dispenser.collect`. That mock
presentation has no hydration or Item effect.

### W4 validation and limits

- Unity `manage_scene.validate` reported **0 issues**, **0 missing scripts**, and
  **0 broken prefabs**. Only `ModelTest` was loaded; it was explicitly saved and
  is clean. The final Editor is ready/idle, not compiling or updating, and remains
  in Edit Mode.
- A read-only NavMesh probe temporarily added and removed only its own
  `NavMeshDataInstance`, using the current agent filter `agentTypeID=0` and
  `areaMask=-1`. From sampled actor start `(1.65, 0.05, -3.10)`, complete paths
  (2 corners each) reached the dispenser/basic TV/variant TV approaches in
  `6.61m` / `5.71m` / `4.70m`. This verifies sampled reachability on the existing
  baked data, not runtime Agent movement or furniture-aware path carving. No
  NavMesh asset was rebuilt or saved.
- Focused EditMode job `0b6c3657b5504adf872f74a6ddb92a3f` ran only
  `FurnitureSceneAdapterEditorTests.PowerStateViewUsesPerRendererPropertyBlocksWithSharedMaterial`:
  **1 passed, 0 failed, 0 skipped**. The copied XML is
  `C:/Users/lauda/AppData/Local/Temp/AlterOS-W4-PowerView-TestResults.xml`.
- Material info confirms the screen shader/material property and default state
  `0`. Filtered Console queries found no shader errors; the final Console read
  contained one Log and no Error or Warning entries. An earlier broad MCP Altera
  component inspection emitted Editor serializer Error-category entries. Before
  saving, a safe serialized-field check confirmed all **93/93** Altera material
  slots still matched the clean saved prefab overrides; Player has no Renderers.
  No actor material references were reset or edited.
- No Play Mode, Player profile, private DB, provider, key, hydration, or Item grant
  path was used. Actual port host activation, movement, durable power commit,
  screen transition to On/Off, bark/TTS preference, VN, and grant acceptance remain
  live/player gates. The SQL global settings and selected fixture release remain
  W5 gates.

### W4 identity persistence fix — Round 1 paused at recovery prompt — 2026-10-24

The scoped W4 review
(C:\Users\lauda\AppData\Local\Temp\AlterOS-ReusableContent-2923fb7c68784921b3fe5ae85e522c24\reviews\W4-wiring-1\review.md)
has one Important finding: both TV identities reference a scene-embedded,
non-persistent MonoScript for the secondary FurniturePlacedInstanceIdentity
class in FurnitureSupportSurface.cs. Its proposed bounded repair is a
same-name script split, normal Unity import, Editor rebind of the two TVs, saved
scene reopen/readback, and one focused persistence regression. This is not a
reproduced scene-reopen failure.

The baseline snapshot is
C:\Users\lauda\AppData\Local\Temp\AlterOS-ReusableContent-2923fb7c68784921b3fe5ae85e522c24\reviews\W4-identity-fix-baseline.
Its ModelTest hash is
665BE154935EDCCEDD6DA2041230F21DB454FA781E614705AD3FD3EA78F5AA58; the
current saved scene hash still matches exactly. Before compilation, the Editor
had exactly one loaded scene, clean and active Assets/Scenes/ModelTest.unity
(nine roots); no other, dirty, or untitled scenes were present.

The test-first change currently present is only
Assets/AlterOS/Tests/Editor/Furniture/FurnitureSceneAdapterEditorTests.cs,
SHA-256
367040677D6DD33D91D9B951EC02D7AED70BE79D91CB4DED7028FAB2FCE0AB8F.
It adds PlacedIdentityResolvesToPersistentMonoScriptAsset, checking the
resolved class, matching imported script path, persistence, and GUID. Its
baseline test-file SHA-256 was
67D0AE857C7556D25E0761A1445321E536DAB63CDC8862F84A4BA407C1CA9533.
The Unity log reported Tundra build success (81.93 seconds, five items updated)
and copying AlterOS.Editor.Tests.dll, but no test job was started. Main later
superseded the red-first and per-change validation cadence: keep the focused
regression, make the mechanical matching-file move, then run one consolidated
validation pass at goal completion. FurnitureSupportSurface.cs now has SHA-256
C8CB6652014256BCFB2AC8EDD2D89DBD480D45F0114D56FD91643F9EFC017CEB, and its
existing .meta remains unchanged at
33CC466FEC66C4B52873A7D00CE38AE31A19F3583FA792790CD7C05EC5A2ADA8
(GUID 463999d55de91c94cb13b077a79a89b3). At the initial pause, no identity
script split or new .meta existed; see the dated update below.

The exact Unity 6000.4.4f1 Editor for E:\Pleiades\alteros (PID 45128)
remained at “Hold on... busy” for 5:05 after the compile/reload log stopped at
ModeService[none].LoadModes; its MCP instance disappeared. The log was
preserved at
C:\Users\lauda\AppData\Local\Temp\AlterOS-W4-Identity-Fix-Editor-20261024T060500Z.log
(SHA-256
5FB8330309A613FF2608F7C15C60169AB20C58CDEAC05F3691C9743FABBE0D2B).
Following the repository's exact-process recovery authorization, only that
verified Editor process was closed. No scene save or scene edit occurred during
the stall.

The same Unity version/project was reopened as PID 49036. Unity displayed a
scene-backup recovery prompt offering to copy and preserve backups under
Assets/_Recovery/. No No/discard choice was made; the recovery prompt is still
open and Assets/_Recovery/ did not yet exist at the last check. Supported
Computer Use attempts to choose Yes failed twice with
GetCursorPos failed: Access is denied. (0x80070005); Main independently
confirmed the same failure. No further UI attempts, Editor starts, or
batch-mode workaround are authorized for this window. The Editor input window
is released pending the human's preservation action. Do not use the Editor
until Main signals that the recovery prompt was handled and Unity is ready.

Still unperformed at the initial pause: normal import/new Unity .meta, Editor
component rebind preserving both distinct IDs/definition IDs, serialized GUID
readback, save/reopen, post-reopen reference/missing-script validation, and the
single consolidated focused regression/compile validation. No Play Mode was
entered. Preserve this partial test, the captured baseline, and recovery log
when resuming; release the completed result to the same W4 reviewer.

#### Source move performed while Editor remains blocked — 2026-10-24

After the recovery pause, Main authorized the mechanical source move before
Editor validation and set one consolidated validation pass for the completed
goal. The unchanged Pleiades.Furniture.FurniturePlacedInstanceIdentity class
(same namespace, type, serialized fields, properties, and Configure body) now
lives in Assets/Scripts/Furniture/FurniturePlacedInstanceIdentity.cs
(SHA-256 E3B27A502F70B6F3461ED32D9D3DDD01E25D0485EAD8602911CF1F186B4FFF2F).
FurnitureSupportSurface.cs retains its own class and the existing .meta/GUID.
No .meta was hand-created; the matching new script .meta is absent at this
checkpoint and must be generated by Unity import.

At the pause, no Unity API, test runner, scene, or UI action had yet been used
after this source move; the later final validation is below. At the last blocked
checkpoint, Unity PID 49036 showed the scene-backup prompt for Assets/_Recovery/
and Main's supported Computer Use retry also failed with GetCursorPos access
denied. Main's human-ready signal later arrived; the final Editor work is
recorded below.

### W4 identity persistence repair — final Editor handoff — 2026-10-24

Main authorized the matching-file move before Editor validation and replaced
the earlier red-first/repeated-check cadence with one consolidated final pass.
The existing regression was retained; no suite rerun or new review round was
started.

The matching script was imported normally by Unity. Unity generated
Assets/Scripts/Furniture/FurniturePlacedInstanceIdentity.cs.meta with GUID
8b5504fdb4cf573478c993938d33d32e and local file ID 11500000. The script resolves
as Pleiades.Furniture.FurniturePlacedInstanceIdentity, is persistent, and has
AssetDatabase path Assets/Scripts/Furniture/FurniturePlacedInstanceIdentity.cs.
The existing FurnitureSupportSurface meta/GUID remained unchanged.

Both saved TV components were rebound through the Editor (rebound=2), preserving:

| TV | Instance ID | Definition ID | Use-point ID | Screen renderer |
| --- | --- | --- | --- | --- |
| TV_Basic_FixtureA | testbox.tv.basic.fixture_a | furniture.tv.basic | testbox.tv.basic.fixture_a.view_target | its own Screen renderer |
| TV_TestVariant_FixtureB | testbox.tv.test_variant.fixture_b | furniture.tv.test_variant | testbox.tv.test_variant.fixture_b.view_target | its own Screen renderer |

The existing ActivitySceneHost remains assigned to its RoomTestbox
ActivitySequenceScenePrimitivePort and retains exactly three use points:
testbox.water_dispenser / testbox.water_dispenser.drink,
testbox.tv.basic.fixture_a / testbox.tv.basic.fixture_a.view_target, and
testbox.tv.test_variant.fixture_b / testbox.tv.test_variant.fixture_b.view_target.
No dispenser, other scene root, screen, or shared power-state material was
changed by the rebind.

ModelTest was saved, then reopened through the Editor after enumerating all
loaded scenes and confirming that only the clean ModelTest scene was loaded.
After reopen, both components' serialized m_Script fields resolve to the
persistent script asset and GUID 8b5504fdb4cf573478c993938d33d32e. The saved
scene contains the external GUID reference for both TVs. Final scene SHA-256:
EDC1F9B10E9F2BAE98E4F83175D61854D4ABD76EF0AC1CDBDD683AC4E032E7CB.

The single focused EditMode job
7cea7c04e60a48c0a8484c21eaad6fb8 ran
FurnitureSceneAdapterEditorTests.PlacedIdentityResolvesToPersistentMonoScriptAsset:
**1 passed, 0 failed, 0 skipped**. No broader suite was rerun. Scene validation
reported **0 issues, 0 missing scripts, 0 broken prefabs**. The Editor is idle,
not compiling or updating; exactly one clean ModelTest scene is loaded and
Play Mode is off. The filtered Console read found no Error or Warning entries.

Final source hashes: FurniturePlacedInstanceIdentity.cs
E3B27A502F70B6F3461ED32D9D3DDD01E25D0485EAD8602911CF1F186B4FFF2F;
FurniturePlacedInstanceIdentity.cs.meta
02DD0E632F506C261A59FEACDA314216A4BFAD0E5E9AB5F44BC030E0AC5E8232;
FurnitureSupportSurface.cs
C8CB6652014256BCFB2AC8EDD2D89DBD480D45F0114D56FD91643F9EFC017CEB; its
original meta 33CC466FEC66C4B52873A7D00CE38AE31A19F3583FA792790CD7C05EC5A2ADA8;
and FurnitureSceneAdapterEditorTests.cs
367040677D6DD33D91D9B951EC02D7AED70BE79D91CB4DED7028FAB2FCE0AB8F.

No Play Mode, player profile, provider, DBMS, or live furniture behavior was
tested. The completed W4 Editor result is handed directly to Main as requested;
there is no additional reviewer round in this scope.
