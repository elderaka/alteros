# Furniture Foundation Implementation Plan

## Current status (2026-10-22)

F1-F4 source and the later I1-I5 inventory/placement continuation are implemented;
ModelTest Editor integration and its current evidence are in
[[Furniture Foundation Handoff]], [[Room Testbox Handoff]] and
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].
The F4 source-readiness section below is a historical checkpoint; its import,
scene/UI-wiring and window-support gaps have since been superseded. Live key,
Game View, restart and player acceptance remain pending.

> **For agentic workers:** Use `executing-plans` to implement task-by-task in the existing Room Testbox session. Steps use checkbox syntax. Human authorized finishing these plans and assigning implementation2026-10-21; no further design-only hold applies to this bounded slice.

**Goal:** Implement the shared furniture registry/resolver and safe layout/interaction foundation consumed by the workbench, preserving the current dispenser.

**Architecture:** Portable C# core owns definitions, geometry/eligibility, reservations and saved layouts. Thin Unity adapters supply real evidence and `Player.Interact` presentation (keyboard T in the current mapping); they do not grant unsupported activities. Workbench consumes the same core through the shared contract.

**Tech Stack:** Existing C#/.NET facilities, Unity6000.4.4f1 for scene integration, no new packages.

**Spec:** `Documentation/Tracking/Furniture Mechanics and Catalogue Design.md`; `Content Workbench Shared Contract.md` is the cross-session interface fence.

## Global constraints

- Work only in `E:/Pleiades/alteros`; preserve unrelated/shared edits; no broad staging or initial whole-repo commit.
- Separate furniture; 3D proximity plus facing/height/access; independent functional points, deterministic selection.
- Room-local metre XYZ, normalized quaternion, support identity and v1 fixed unit scale; preserve orphan instances.
- Home save owns layout/state; scenes seed defaults only. Do not auto-resume activities/reservations.
- Player joins manually with T through `Player.Interact`; stale choice needs another click; no active rebind.
- Static UI/scene/prefab wiring via Editor only. No YAML/meta edits or provider/TTS spend.
- No broad furnishing UI, bed/TV/window behaviors, seated cameras or complete sitting executor in this foundation. Advertise only implemented actions; authoring may simulate the proposed sit-together capability explicitly.

## Review focus

1. Definition visible in catalogue but no executor: must not become a live capability (F1/F4).
2. Two-seat contention and moved supports: no partial claims or stale arrangement execution (F2/F3).
3. Interrupted save or stale edits: previous live layout/file remains authoritative (F3).
4. Unknown evidence and hidden requirements: no fabricated availability or leaked tooltip (F2/F4).
5. Existing dispenser/Follow/Drink and `Player.Interact` control regressions: preserve behavior and ownership (F4).

## Ownership and files

Own new `Assets/AlterOS/Furniture/` portable files; catalogue at
`Assets/StreamingAssets/Furniture/catalogue.json`; tests under
`Assets/AlterOS/Tests/Editor/Furniture/`; Unity adapters under
`Assets/Scripts/Furniture/`. Preserve GUIDs of existing use points, definitions,
NavMesh and actor/UI. Existing `Assets/AlterOS/Activities/FurnitureRegistry.cs`
remains compatible via an adapter rather than a destructive replacement.
Do not write `Tools/ContentWorkbench/` (Topic Playground owns it).

## F1 — Versioned registry and automatic discovery

**Files:** create `FurnitureCatalogue.cs`, `FurnitureCatalogueValidator.cs`,
`FurnitureCatalogueRegistry.cs` under the portable folder; canonical JSON;
`FurnitureCatalogueTests.cs`; `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`.

**Interfaces:** exactly the catalogue DTOs and validator/Load signatures in the
shared contract. Registry snapshots include definitions, rules, capabilities,
version/hash and diagnostics. No UnityEngine types in portable core.

- [x] Write failing tests: valid v1 includes exactly eight stable starter IDs from the spec; duplicate IDs, unknown major version, NaN/nonfinite geometry and broken references reject; an added valid chair variant appears after reload without UI/manifest edits; identical data gives a stable hash.
- [x] Run `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter Catalogue`; capture expected failures before implementation.
- [x] Implement typed records, strict validation, deterministic snapshot/hash, explicit aliases for the existing dispenser IDs. Keep parse/validation errors inspectable and fail closed.
- [x] Repeat checks to green, then full furniture suite. Add a reflection/dependency check that portable output has no Unity dependency.
- [x] Mark F1 interface ready in this plan with exact paths/version/evidence. Topic Playground can discover it from disk; do not change shared shape silently.

### F1 interface readiness — 2026-10-21

- **Ready API:** `AlterOS.Furniture.FurnitureCatalogueValidator.Validate(FurnitureCatalogueDocument)` and `FurnitureCatalogueRegistry.Load(string cataloguePath)` match the shared contract. `FurnitureCatalogueSnapshot` exposes the pinned fields plus additive `legacyAliases`, now documented in the shared contract.
- **Schema:** catalogue schema v1 at `Assets/StreamingAssets/Furniture/catalogue.json`, version `1.0.0`; exactly eight stable starter definitions, four relationship rules, five descriptive capability descriptors, and explicit dispenser legacy aliases.
- **Source:** `Assets/AlterOS/Furniture/FurnitureCatalogue.cs`, `FurnitureCatalogueValidator.cs`, `FurnitureCatalogueRegistry.cs`. Invalid records are excluded from usable snapshot collections and preserved as stable path/code diagnostics; unsupported schema/preamble fails closed. Registry loading performs explicit reload; there is no background watcher or executor grant.
- **Verification:** `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1 -Filter Catalogue` — 9 passed, 0 failed; full furniture source suite — 9 passed, 0 failed; reflection check found no `UnityEngine`/`UnityEditor` references. Before implementation, the expected red compile reported the missing `AlterOS.Furniture` namespace/types. This is source harness evidence; Unity import/EditMode and runtime are not yet verified.
- **Test source:** `Assets/AlterOS/Tests/Editor/Furniture/FurnitureCatalogueTests.cs`; no `.meta` file was hand-created. Unity will own generated asset metadata on import.

## F2 — Relationships, locked-choice evaluation and atomic reservations

**Files:** create `FurnitureArrangementResolver.cs`, `FurnitureChoiceEvaluator.cs`,
`FurnitureReservationSet.cs`; matching `FurnitureArrangementTests.cs`,
`FurnitureChoiceTests.cs`, `FurnitureReservationTests.cs`.

**Interfaces:** shared Evaluate signatures; `FurnitureReservationSet.TryAcquire(string ownerId, ArrangementEvaluation arrangement, FurnitureWorldSnapshot current)`
returns typed `FurnitureReservationResult` with success/reason/claim IDs;
`Release(string ownerId)` is idempotent. World snapshot and numeric primitives
live in portable core; no Unity Physics calls in the resolver.

- [x] Red tests: facing seats without table allow base sitting; unrelated table does not unlock table context; correct shared table does; wrong facing/height/wall/floor/access reject; incomplete evidence is unknown; stable tie-break regardless of input ordering.
- [x] Red tests: blocked second claim rolls back first; point occupancy differs from object-wide occupancy; contextual-only table is not reserved; stale revision/owner cannot steal claims; double release is harmless.
- [x] Red tests: lock/hide outcomes, hidden has no player reason, unknown uses generic safe copy, stale pre-start click cannot advance and requires a fresh click even with another valid arrangement.
- [x] Run `Check-FurnitureRuntime.ps1`, then implement rules/choice evaluation/reservations from the spec. Set documented fixture tolerances, not per-room constants.
- [x] Green full suite; publish F2 shapes/fixtures/version in the shared contract. No content-derived capability authority.

### F2 interface readiness — 2026-10-21

- **Ready APIs:** `FurnitureArrangementResolver.Evaluate(FurnitureArrangementRequest)`, `FurnitureChoiceEvaluator.Evaluate(FurnitureChoiceRequirement, ArrangementEvaluation)`, `FurnitureReservationSet.TryAcquire(string, ArrangementEvaluation, FurnitureWorldSnapshot)` and idempotent `Release(string)` match the shared contract.
- **Behavior:** 3D transform/facing/height/support/access checks, deterministic pair/table binding, explicit unknown evidence, safe lock/hide choices, selection-time stale revision gate, and atomic point-level reservations. Context-only table evidence is not claimed. No physical sitting executor or capability grant is included.
- **Verification:** F2 targeted source runs passed arrangement11/11, choice5/5, reservation5/5. Final full `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` passed 30/30 and the portable assembly has no UnityEngine/UnityEditor references.
- **Consumer boundary:** F1/F2 core APIs are ready for the workbench to import. Synthetic evidence remains authoring preview data only; runtime capability publication still belongs to installed executors/host policy.

## F3 — Saved layouts and revisioned arrangement commands

**Files:** `HomeFurnitureLayout.cs`, `HomeFurnitureRepository.cs`,
`FurnitureLayoutCommands.cs`, tests `FurnitureLayoutTests.cs` and
`HomeFurnitureRepositoryTests.cs`; synthetic fixtures under `Tools/FurnitureRuntime/Fixtures/`.

**Interfaces:** `HomeFurnitureRepository.Load(string path)` -> typed load result;
`Save(string path, HomeFurnitureLayoutDocument document, string expectedHash)` -> typed saved/conflict/error result;
`FurnitureLayoutCommands.Apply(FurnitureLayoutCommand command, FurnitureLayoutSnapshot current, FurniturePlacementEvidence evidence)` -> typed result/new snapshot.
Commands are `place|move|rotate|remove` with expected revisions and support refs.

- [x] Red tests: first-load seed only; edited saves survive new seeds; missing definition/support preserves inert complete orphan; unknown schema fails without resetting layout; active activities/reservations never serialized for resume.
- [x] Red tests: stale command or reserved point rejects without mutation; persistence failure retains old disk/live snapshot; structural-invalid rejected but structurally safe relationship-invalid placement accepted; carpet is not automatically a navigation obstacle.
- [x] Red round-trip fixture: same IDs/relationships after Windows/Android-style serialization round trip; <=0.001m position, <=0.1degree rotation and <=0.001scale differences, including raised and wall-mounted furniture. Label serialization proof, not Android runtime proof.
- [x] Implement pure transactions, backup-safe/atomic persistence and explicit migration aliases; no automatic edited-save migration/reset. Synthetic evidence never substitutes for live collision proof.
- [x] Run all furniture tests; add failure-injection checks. Update handoff with save ownership and unresolved migration-presentation scope (deferred).

### F3 interface readiness — 2026-10-21

- **Ready APIs:** `HomeFurnitureRepository.Load/LoadOrSeed/Save` and `FurnitureLayoutCommands.Apply` are implemented. Command results carry an uncommitted snapshot plus the original expected save hash; publish only after `Save` returns `saved`.
- **Save schema:** `HomeFurnitureLayoutDocument` v1 stores room-local XYZ metres, normalized quaternion, fixed unit scale, support ID/kind/frame revision/attachment point/local pose, persistent condition/resource state and revisions. Active activity/reservation state is absent.
- **Safety:** first load imports only the scene seed; existing saves ignore newer seed data. Missing definitions/supports remain complete inert orphans. Stale hashes/revisions and reserved point edits reject; save errors preserve the prior file. Room bounds, support frame pose and blocking-furniture overlap are checked. Relationship-invalid but structurally safe placement is retained; carpet is nonblocking.
- **Fixture:** `Tools/FurnitureRuntime/Fixtures/windows-android-roundtrip-layout.json` covers raised/rotated chair, named wall-opening window and carpet; tests compare both serialization directions within 0.001m/0.1deg/0.001 scale. Synthetic serialization evidence only.
- **Verification:** `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` full suite passes 43/43; reflection check reports no UnityEngine/UnityEditor references. No home-save service, Unity scene adapter, Android runtime or player behavior is wired/verified.

## F4 — Narrow Unity integration and furniture E adapter

**Files:** new `FurnitureSceneAdapter.cs`, `FurnitureInteractable.cs` under
`Assets/Scripts/Furniture/`; focused compatibility changes to existing use-point/
scene host only when needed. Editor-authored choice/tooltip template changes and
`FurnitureInteractionTests.cs`; existing activity tests remain intact.

**Interfaces:** implement existing `Pleiades.Player.IPlayerInteractable` using
host-derived options bound to exact actor/use/arrangement/revision. Player actions,
asking Alterra and shared activities are distinct, never silently substitute actors.
Only installed executors are selectable. Unsupported seating/rest/TV use stays
unavailable; do not claim physically sitting has been implemented by the adapter.

- [x] Red tests: old dispenser IDs/use still resolve; declaration without executor cannot run; player E on furniture resolves stable instance; locked never executes/advances; stale available choice refreshes; hidden reason never enters player UI. Portable policy tests pass; Unity tests are authored and compile, but are not yet executed in Editor.
- [x] Test binding/revalidation through the existing activity owner and E input interface. Do not create another activity runner or transcript writer. Tests cover actor/instance/point/catalogue/revision, click request identity, exclusive activity ownership and no Scenario-event leakage; Editor tests remain unexecuted.
- [x] Implement thin real evidence capture, revision invalidation and safe active-use protection. Scene capture distinguishes current reachable use points from missing/unavailable evidence. Conversation ownership blocks standalone E while a Scenario is running or suspended; the existing host rejects competing activity requests.
- [ ] Through correct Unity Editor only, author reusable Canvas button/tooltip/focus presentation and wire only required existing testbox fixture. Preserve unsaved scenes and other sessions' changes. Runtime code binds/positions only.
- [x] Compile current runtime, Assembly-CSharp and Editor test sources against cached Unity 6000.4.4f1 Bee references into ignored scratch outputs; existing obsolete-API warnings remain. This does not update live Unity assemblies.
- [ ] Import in Unity, check Console and execute focused furniture, Activities/Scenario and greeting Editor tests. If MCP stalls, stop repeated requests and use source fallback; do not force-close or discard scenes.
- [ ] If safe live verification is available, use no-provider/TTS-off checks for E prompt/options and preserved dispenser flow. Normal acceptance is Splash→ModelTest; report movement/camera/UI proof separately from compile and simulated tests.

## Finish and handoff

- [x] Run full furniture source command plus existing M3/Scenario source suites; report every failure and checks not run. Furniture52/52, Scenario67/67 and Unified Conversation194/194 passed; Editor tests were compiled but not executed.
- [x] Update this plan, a focused `Furniture Foundation Handoff.md`, verification note and concise TODO status. Preserve workbench ownership and name F1/F2 readiness, actual supported schema/executors, wiring and runtime gaps separately.
- [ ] No runtime furnishing editor, physical seating/camera work, automatic catalogue reload, edited-save migration UI, cloud sync or paid calls beyond this plan. Propose those next, do not silently implement them.

Self-review2026-10-21: shared signatures/paths and four ownership slices checked
against the spec. First gate F1/F2 intentionally unblocks workbench before Unity
acceptance. Existing shared checkout is intentional; no automatic branch/commit
creation or broad worktree cleanup is authorized.

### F4 source readiness — historical checkpoint (2026-10-21)

The source-only F4 findings below remain valid for that dated checkpoint. The
current Editor-wired room state supersedes its pending scene/UI/window-support
gaps; live player acceptance remains open.

- **Ready paths:** `Assets/AlterOS/Furniture/FurnitureInteractionPolicy.cs`,
  `Assets/Scripts/Furniture/FurnitureSceneAdapter.cs`,
  `Assets/Scripts/Furniture/FurnitureInteractable.cs`,
  `Assets/AlterOS/Activities/ActivitySceneHost.cs`,
  `Assets/AlterOS/Activities/ActivityHost.cs`,
  `Assets/Scripts/Player/IPlayerInteractable.cs`,
  `Assets/Scripts/Player/PlayerLookInteraction.cs`, and
  `Assets/Scripts/Dialogue/DirectConversationBridge.cs`.
- **Actor/ownership:** the current installed action is `needs.drink_water` run by
  Alterra, so E reads “Ask Alterra to get water.” It does not apply Player
  hydration. The exact point, scene capability, point revision, catalogue hash,
  actor and request identity are checked before forwarding to the same
  `ActivityHost`. New request IDs cannot overlap an active activity. Running or
  suspended Scenario ownership blocks standalone requests. Same request identity
  returns the original idempotent result.
- **Scenario effects:** standalone E requests do not enter the Scenario event
  queue and do not receive menu Drink's return-walk/readiness-bark/next-E
  orchestration or transcript writing. The authored Follow/Drink menu Scenario
  remains the path for that complete behavior; activity effects still come only
  from the existing executor and sip checkpoints.
- **Verification:** full portable furniture suite **52 passed, 0 failed**;
  Scenario Runtime and Unified Conversation source suites both exited 0. Current
  `AlterOS.Runtime`, `Assembly-CSharp` and `AlterOS.Editor.Tests` sources compiled
  with Unity 6000.4.4f1 cached Bee references into `.superpowers/sdd/F4-source-compile/`;
  only existing obsolete-API warnings were reported. The custom compile did not
  update Unity assemblies or constitute Editor import/test execution.
- **Pending:** Unity MCP's `mcpforunity://editor/state` read stalled and was
  stopped. No scene/prefab/Canvas changes were made. Locked-option hover and
  keyboard/controller focus tooltip target, scene references, Unity Editor test
  results, Console import, Play Mode E interaction and Splash-to-ModelTest
  acceptance remain open. Do not call F4/player interaction complete yet.
