# M3 Furniture and Activities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Come here, Follow me and Drink water reusable host-owned activities exercised by offline authored Scenarios in the existing room.

**Architecture:** Inspector-authored definitions supply configuration, plain C# executions own per-attempt state, and one Unity host binds movement/furniture/animation. A narrow adapter implements the existing Scenario activity port; only correlated game events advance Scenarios or apply hydration. Existing conversation presentation remains the sole dialogue runner.

**Tech Stack:** Unity6000.4.4f1, C#, installed Unity AI module, current NUnit/Editor test assembly and existing Scenario contracts; no Python/provider changes or new packages.

**Spec:** [[M3 Furniture and Activities Design]], approved by the human2026-10-19. Read it fully with `AGENTS.md`, `TODO.md`, [[Room Testbox Handoff]], and the Scenario worker's latest handoff before execution. Human approved handoff/START2026-10-19; sent to Room Testbox (`01a15376-d6f0-7c41-b9e5-fcdee723e87b`, resolved host local). Future furniture ownership does not expand this implementation scope.

## Global Constraints

- Work only in `E:\Pleiades\alteros`; archive checkout is read-only. Preserve parallel source, unsaved scenes and untracked files. There is currently no Git HEAD: no whole-tree staging/baseline commit or destructive reset. Record task-owned paths/checks; commit only with a separately established safe focused policy.
- Come here snapshots its destination; Follow keeps a live target until stopped. Defaults: 1.5 m stop distance, 2.2 m follow-resume distance, minimum0.3 s repath interval and at most one bounded route-recovery attempt. Inspector tuning may change values after measured room checks, not semantics.
- One physical movement owner; one running plus one suspended Scenario. E suspension is not Scenario suspension. Quitting cleans up and never restores active execution next launch.
- Register only real usable capabilities; never expose a synthetic movement/effect implementation as production availability. No arbitrary stat deltas, coordinates, code or provider internals in action requests.
- No paid calls, key reads, TTS, auto-generation or v4 wire expansion. Authored fixtures need neither Appetizer nor network. Japanese speech plus English display remains required; preserve existing authored catalogue/anchors.
- Editor owns scene/prefab/material/config assets and references. No hand-written YAML/meta or runtime-generated static HUD. Preserve Splash→ModelTest, player/camera/UI roots. **2026-10-20 user override:** replace the ModelTest room while keeping Player and Altera; the current neutral shell has no furniture use points or assigned NavMeshData. Earlier no-room-rebuild guidance is superseded. The approved furniture contract stands; restore its intended fixture layout and then rebuild navigation.
- Inspect installed navigation and clips. Prefer core `UnityEngine.AI` navigation and existing animation facilities; no AI Navigation package is installed in the inspected manifest. If usable bake/navigation requires a new package or unavailable tooling, stop that integration gate and ask rather than substitute obstacle-blind movement.
- Full needs simulation, autonomous scheduling, seated cameras, desk joining, phone/M4, home building, persistence of activities and new content publication remain out of scope. Detached stat-fixture proof is not real stat/persistence acceptance.

## Review Focus

1. E close arrives after Stop/replacement: it must not restart the old movement owner (Tasks2/6).
2. Player stands off navigable ground or across an obstacle: no false arrival, clipping or silent teleport (Tasks2/3).
3. Dispenser moves/disables during use: release ownership and apply no unearned hydration (Tasks4/5).
4. Duplicate request, animation callback or completion event: no second execution, repeated hydration or double Scenario transition (Tasks1/5/6).
5. An indefinite Follow Scenario needs in-place E/Stop without replacing itself or consuming the suspended slot (Tasks6/7).

## File and contract map

New runtime files live under `Assets/AlterOS/Activities/` (namespace `AlterOS.Activities`); tests under `Assets/AlterOS/Tests/Editor/Activities/`. Keep files focused, not one generic action interpreter.

| File | Responsibility |
| --- | --- |
| `ActivityContracts.cs` | Typed execution/view/outcome, motion and effect ports |
| `ActivityDefinition.cs` | Inspector-authored capability/config asset; no mutable execution state |
| `ActivityHost.cs` | Plain C# request/ownership/idempotency/event lifecycle |
| `MovementActivity.cs` | Come/follow policies using a shared motion port |
| `UnityActivityMotion.cs` | Unity navigation adapter, not dialogue or request policy |
| `FurnitureUsePoint.cs`, `FurnitureRegistry.cs` | Authored instance bindings, reachability filtering and reservations |
| `DrinkWaterActivity.cs`, `HydrationCheckpointSink.cs` | Drink phases and host-authorized idempotent recovery |
| `ScenarioActivityAdapter.cs` | Existing `IScenarioActivityPort` and correlated result projection |
| `ActivitySceneHost.cs` | Unity lifecycle/composition, motion tick and cleanup |
| `Editor/ActivityNavigationBaker.cs` | Only if required: narrowly scoped Editor use of core NavMesh build facilities |
| `Assets/Scripts/Interaction/ActivityInteractionBridge.cs` | Existing scene interaction/presentation hookup outside runtime assembly if legacy references require it |

New authored assets/fixtures: `Assets/AlterOS/Testbox/Activities/`, `Assets/AlterOS/Testbox/Scenarios/{come_here,follow_me,drink_water}.json`; never place synthetic generated records in StreamingAssets. Dedicated tests use the existing `AlterOS.Editor.Tests` assembly. Do not make `AlterOS.Runtime` reference Assembly-CSharp to access legacy progression/presentation.

### Shared new interfaces (implementation plan decisions)

- `ActivitySettings` holds immutable copied values from `ActivityDefinition`: capability ID, stop/resume/repath/timeout settings, drink checkpoints and phase durations. Reject invalid distances/durations/unknown executors at host registration.
- `ActivityHost.Request(ActivityRequest request, ScenarioRuntimeSnapshot context) -> ActivityRequestResult`; `Tick(double nowSeconds) -> void`; `Stop(string activityId, string reason) -> bool`; `BeginExchange(string activityId, string exchangeId) -> bool`; `EndExchange(string activityId, string exchangeId) -> bool`; `Shutdown(string reason) -> void`.
- `ActivityHost.Current -> ActivityView`; `DrainEvents() -> IReadOnlyList<ActivityEvent>`. Immutable view/event fields include activity/run/step IDs, phase, terminal reason and CanTalk/CanPause. Input clock is monotonic, not wall time.
- `IActivityMotion.TryResolveNearPlayer(float separation, out UnityEngine.Vector3 destination) -> bool`; `TryMoveTo(Vector3 destination) -> bool`; `Suspend()`, `Resume()`, `Clear()`, `FacePlayer()`; read-only `PlayerAvailable`, `DistanceToPlayer`, `HasArrived`, `IsBlocked`. Adapter owns path pending/partial-path handling; tests supply fake observations.
- `FurnitureRegistry.TryReserve(string useId, string requestedObjectId, string activityId, out FurnitureBinding binding) -> bool`; `IsValid(FurnitureBinding binding) -> bool`; `Release(string activityId) -> void`. Binding freezes instance/use-point IDs, authored transform revision and reservation owner.
- `HydrationCheckpointSink.TryApply(string activityId, string checkpointId) -> bool`; its host-only registration binds the permitted checkpoint schedule/magnitude. It must not accept model-selected stat IDs/deltas. Snapshot reading is a separate read-only output.

These are proposed new local C# APIs, not existing protocol fields. Small internal helper types may follow adjacent code style; public names/signatures above must remain consistent across tasks. Any necessary shared Scenario changes require its owner's agreement after final handoff.

## Task 1: Typed configuration and exclusive execution ownership

**Files:** Create `ActivityContracts.cs`, `ActivityDefinition.cs`, `ActivityHost.cs`; test `ActivityHostTests.cs`.

**Interfaces:** Produces the shared host/config/view/event contracts. Capability IDs: `movement.come_here`, `movement.follow_player`, `needs.drink_water`. Request arguments: movement requires `target=player`; drinking accepts `target=available_dispenser` or one snapshot-allowed concrete object ID. Reject extra/duplicate arguments. `(runId, stepId)` is the accepted-request identity; same identity with changed payload is rejected, never replayed as different work.

- [x] Write `DuplicateRequestReturnsSameTicketAndNoSecondStart`, `ChangedPayloadSameIdentityRejected`, `SecondActivityReturnsBusy`, `SharedDefinitionHasIndependentExecutionState`, and `UnknownArgumentDoesNotStart`; assert counts/ticket identity and no effects. Include duplicate-after-terminal behavior: return the original ticket/outcome without rerunning.
- [x] Run focused source RED before implementation and observe the expected missing-contract/behavior failures; do not treat a compiler failure from unrelated Scenario work as the regression's red phase.
- [x] Implement registration validation, per-attempt identity/ownership, immutable settings copies and explicit terminal cleanup. Keep executors injectable, with test fakes outside production.
- [ ] Rerun focused tests; assert shutdown twice releases once, unknown/stale Stop changes nothing and every terminal event is emitted once. Record focused path checkpoint.

## Task 2: Reusable snapshot movement and ongoing following

**Files:** Create `MovementActivity.cs`; extend `ActivityHost.cs`; tests `MovementActivityTests.cs`.

**Interfaces:** Consumes `IActivityMotion` and clock; host drives the executor through Request/Tick/Stop and exchange APIs. Internal executor methods can remain private; clients must not bypass the host.

- [x] Write `ComeHereDoesNotChaseMovedPlayer` (destination remains the accepted snapshot), `AlreadyNearCompletesWithoutMove`, `FollowStopsAt1Point5AndResumesBeyond2Point2`, `FollowCatchUpIsNotTerminal`, and `RepathIsNotFasterThanPoint3Seconds` using fake clock/motion.
- [x] Add `LateExchangeCloseCannotResurrectStoppedFollow`, `RepeatedExchangeOpenIsIdempotent`, `BlockedPathRetriesOnceThenFails`, `MissingPlayerFailsWithoutArrival`; pin exactly one clear/terminal event and no teleport request.
- [x] Run source tests red, then implement movement policies, E pause/face/resume with matching exchange/owner identity, and configurable watchdogs. Proposed stalled-progress timeout10 s; navigation acquisition timeout3 s; Follow waiting-near-player never times out merely for waiting. Reset stalled watchdog appropriately after real movement or dialogue suspension.
- [x] Run focused source tests green alongside existing host tests. Source harness verifies Come here retains its destination across E and Follow resumes only the matching live owner; Unity EditMode remains unrun.

## Task 3: Real navigation adapter and isolated room wiring

**Files:** Create `UnityActivityMotion.cs`, `ActivitySceneHost.cs`, optional `Editor/ActivityNavigationBaker.cs`; create Editor-authored activity assets in Testbox/Activities; narrowly edit ModelTest through Editor. Test `UnityActivityMotionTests.cs` where fake adapters can isolate policy; real movement proof remains Play Mode.

**Interfaces:** Implements `IActivityMotion`; SceneHost ticks ActivityHost on main thread with monotonic time and shuts it down on disable/scene exit. Serialize actor/player/navigation references and definition assets, not scene-name lookups or hardcoded world positions.

- [x] Inspect Scenario ownership and actor motion/physics source; confirm correct Unity project/instance, active ModelTest scene and user-safe import state before scene mutation. No force recovery used.
- [ ] Write adapter tests for pending/partial/invalid route not implying arrival and missing player/agent failing closed. Run red then implement existing core AI facilities. Calculate complete navigable paths and safe target sampling; arrival needs non-pending route plus distance/velocity conditions, not only requested destination equality.
- [x] Through Editor, configure only necessary navigation data and actor adapter. A scoped `ModelTestNavMesh.asset` was built from the room's 24 colliders; no runtime room construction, teleport navigation or package addition.
- [x] Inspect Console/compile state and saved references. The current M3 references validate; unrelated Scenario test-assembly errors remain.
- [ ] Safely test Come here/Follow in Play Mode if feasible. Record collision, root-motion compatibility, follow E suspension and unreachable samples separately from Editor path queries and source checks.

## Task 4: Minimal furniture binding and reservation

**Approved furniture extension2026-10-19:** read the spec's furniture contract
extension. Keep shared capability/point configuration separate from per-instance
state and per-point claims. A failed functional arrangement can remain decoration;
authored overrides cannot bypass safety. A future relationship resolver uses a
3D sphere candidate search followed by typed facing/height/reach/obstruction checks,
cached/invalidation-aware and bound to exact points at activity start. Do not
implement the general chair/desk/TV resolver, seating, placement UI or multi-point
transactions in this dispenser-only task; do not bake in assumptions that every
furniture instance has one slot or that proximity alone grants usability.

**Files:** Create `FurnitureUsePoint.cs`, `FurnitureRegistry.cs`; test `FurnitureRegistryTests.cs`; Editor-wire existing `testbox.water_dispenser` and four marker transforms.

**Interfaces:** Registry methods from shared contracts. Use ID `drink_water`; snapshot-visible furniture object ID `testbox.water_dispenser`. One occupant at this point; optional required object ID cannot silently fall back. Movement adapter supplies actual reachability checks.

- [ ] Write `ResolveUsesSupportedUseNotName`, `NearestReachableThenStableIdWins`, `ExplicitUnavailableObjectDoesNotSubstitute`, `DuplicateUsePointIdsFailRegistration`, `MovedOrDisabledBindingInvalidates`, `ReservationReleasedOnceOnEveryTerminalReason`.
- [ ] Run red; implement minimal registration, deterministic selection, identity/revision invalidation and ownership. No shop, inventory, seat pairing, global scheduler or furniture inheritance tree.
- [x] Wire the dispenser through Editor to the four authored markers; the Editor route query reaches its approach point.
- [ ] Verify marker orientations/pose with the actual rig. Collision-safe alignment and real availability remain unverified. Mark fixture supply explicitly always-available; do not claim water inventory.
- [ ] Run green; demonstrate absent/disabled/busy fixtures return unavailable and create no movement/effects. Save only owned assets/scene changes, noting any blocked Editor gate.

## Task 5: Drinking phases and authoritative hydration checkpoints

**Files:** Create `DrinkWaterActivity.cs`, `HydrationCheckpointSink.cs`; test `DrinkWaterActivityTests.cs`, `HydrationCheckpointTests.cs`; minimal `Assets/AlterOS/Stats/ActivityHydrationState.cs` only if no existing writable hydration owner is found. Update a legacy snapshot bridge only where required and after all callers are inspected.

**Interfaces:** Drink uses the registry and motion port, emits host events, and calls `TryApply` only on valid checkpoints. New fallback `ActivityHydrationState` exposes `TryGet(out float hydration)` and a host-only validated write path; use catalogue bounds0–100. Unknown remains unknown until explicitly initialized, not silently zero-filled.

- [ ] Inspect actual stat authority first: current `CompanionStatSnapshotBuilder` maps only energy/engagement. Reuse a genuine hydration owner if present; otherwise add only a session-owned hydration value and read-only snapshot overlay, with explicit developer fixture seed30. Preserve unrelated/unknown stats; do not persist or present this seed as a full needs simulation. Record that durability remains out of scope.
- [ ] Write `NoRecoveryBeforeSip`, `DuplicateSipAppliesOnce`, `HydrationClampsTo100`, `UnknownHydrationDoesNotBecomeZero`, `CancelAfterSipRetainsOnlyEarnedRecovery`, `InvalidatedFixtureAppliesNoLaterSip`, `QuitWithoutAnimationCallbackCleansUp`.
- [ ] Run red; implement reserve/approach/align/prepare/sip/exit phases. Proposed fixture tuning: two checkpoints of +10 hydration each; no completion bonus. Host-owned active-execution validation and checkpoint membership precede effect application. Invalidated/cancelled tickets cannot mint later recovery, even with a fresh checkpoint ID.
- [ ] Audit available clips/prop attachments. Use a suitable existing clip or a clearly labelled simulated drink-phase mode in developer fixtures. The simulated mode never advertises production drinking readiness; full visual acceptance stays blocked without a real pose/clip. Safe normal cancellation waits only until the next checkpoint/exit, with bounded watchdog; quit/failure force cleanup without reward.
- [ ] Run green and actual stat snapshot observation where wired. E during unsafe sip uses at most one cooldown-controlled non-modal busy acknowledgement and later player-led greeting; no normal dialogue mid-sip or auto modal. Inspect prop/path/reservation cleanup across repeated attempts.

## Task 6: Scenario adapter, outcome mapping and E ownership

**Files:** Create `ScenarioActivityAdapter.cs`, `Assets/Scripts/Interaction/ActivityInteractionBridge.cs`; tests `ScenarioActivityAdapterTests.cs`, `ActivityInteractionTests.cs`. Shared Scenario/presentation file edits are narrow and coordinated, not owned by this plan by default.

**Interfaces:** Adapter implements existing `IScenarioActivityPort.TryRequest`. Host acceptance projects one existing `activity_result`; terminal projects one correlated wait-condition result. Condition ID `m3.activity_terminal` is registered in the host snapshot. Event activity ID must equal the accepted ticket. Stop/failure/quit are not fabricated successful drink/arrival outcomes.

- [ ] Read completed Scenario worker code/handoff before binding. Write `AcceptedIsNotCompleted`, `LateTerminalDoesNotAdvanceReplacementRun`, `DuplicateCompletionAdvancesOnce`, `FollowStopClosesOwnOccurrence`, `EConversationDoesNotRequestAnotherScenario` and `DialogueClosedAfterDiversionDoesNotResumeOldOwner`.
- [ ] Run red; implement port adapter and event projection using final public Scenario APIs. Only game-owned host events can satisfy waits; reject terminal events from dialogue/provider output. Prefer fixture branch outcomes when supported; otherwise encode Stop/failure through declared failed-condition branch plus host reason, without changing protocol semantics.
- [ ] Integrate repeated E exchanges while an authored Scenario is waiting via the existing presenter/runner; same run/checkpoint remains active. Stop following is a validated local option, not free text that needs an LLM. If existing presenter cannot support this, coordinate the smallest hook with its owner and add a regression—do not create a parallel dialogue UI or swap the running Scenario just to talk.
- [ ] Run green; verify scenario diversion pause/end releases ownership and can resume from a safe host checkpoint without duplicating hydration. Never restart an old accepted-request key for a resumed attempt: if reexecution is necessary, use a fresh host attempt identity via agreed Scenario checkpoint semantics, not a silent deduplication bypass. Block unresolved shared-API changes rather than editing around another worker.

## Task 7: Three offline authored Scenario fixtures

**Files:** Create the three Testbox/Scenarios JSON files, `ActivityScenarioFixtureTests.cs`, and a narrow Editor loader/launcher `Assets/AlterOS/Activities/Editor/ActivityScenarioTestWindow.cs`. Reuse existing Scenario fixture serializer/presenter, not authored Topic loader or a new schema. Use existing Canvas templates for runtime choices; add serialized controls only in Editor if necessary.

**Interfaces:** Launcher loads strict `ScenarioDefinition`, creates real host availability snapshots and calls existing `ScenarioSession.Start`. It must not insert real capabilities when only simulated executors exist. Semantic Topic identity must be valid and separate from test Scenario IDs; reference existing compatible IDs where available, otherwise use explicit isolated test catalogue IDs that never publish to production.

- [ ] Write `AllFixturesValidateAgainstHostCapabilities`, `DeclineStartsNoActivity`, `FollowEStopRemainsSameRun`, `UnavailableDrinkFollowsTruthfulFailure`, `CompletionClosingWaitsForNextE`, and `RepeatFixtureUsesFreshRunAndActivityIds`.
- [ ] Run red then author small bilingual dialogue sequences with supported expressions, questions, action request, close-exchange/wait and truthful endings. Authored context lines rotate with no immediate repeat and a configurable cooldown; no per-E generation or TTS. Failure/arrival closing waits for E; no forced camera reopen.
- [ ] Explicit developer launch is enough for mechanical acceptance. Keep fixtures out of StreamingAssets and anchors unchanged. If adding natural-language routing later, use a small labelled phrase set and separate publish/migration approval; do not claim routing coverage from this fixture launcher.
- [ ] Run green through real ScenarioSession and fake ports for headless tests, then real room port in safe Play Mode. Record where labelled simulation stops and production movement/effects actually begin. No Appetizer request needed.

## Task 8: Regression, runtime acceptance and handoff

**Files:** Create `Documentation/Tracking/M3 Furniture and Activities Handoff.md`, dated verification under `Documentation/Verification/`; update this plan, [[Feature Register]], [[../UnityProjectContext]], [[../SessionZero]], root TODO and maintenance notes for actual changes only.

- [ ] Run focused activity tests through `AlterOS.Editor.Tests` using available Unity MCP test tooling; record test job/results and Console separately. Run `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`, `Tools/Conversation/Check-UnifiedConversation.ps1`, `Tools/TopicRuntime/Check-TopicRuntime.ps1`, `Tools/TopicRuntime/Validate-TopicCatalogue.ps1` and `Tools/TopicPlayground/Check-TopicPlayground.ps1` from the project root after inspecting arguments/current source. Expected: all selected tests pass with exact counts reported, catalogue baseline57 families/161 scenarios preserved. Source harnesses must execute Task-returning tests correctly or exclude async tests with explicit evidence gaps, not silently pass them.
- [ ] Safely check normal SplashScene→ModelTest→E, all three fixtures, blocked route/removed dispenser, repeated E/Stop/cancel, quit cleanup, hydration values, actor collision/animation and input/camera restoration. Preserve unsaved content and do not force-close a stuck Editor. User physical-input acceptance remains separate from tool-invoked UI tests.
- [x] Record source counts/failures, Editor path/configuration evidence, scene/asset hashes and known gaps. No current-frame-rate/latency/audio guarantee from compilation. No API call required or authorized.
- [ ] Stop after this slice. No automatic phone/TTS, desk cameras, autonomous scheduler, catalogue migration or extra action capability. If gates remain blocked, label source/scene/visual/real-effect status individually and hand off the next acceptance check.

## Self-review / execution boundary

Spec mapping: ownership/config→Task1; come/follow/E/watchdogs→Tasks2/3/6;
furniture→Task4; hydration/phase safety→Task5; authored offline Scenarios→Task7;
integration preservation/evidence→Tasks3/6/8. Each Review Focus item has a named
regression. Signatures in the shared contract map are the same names consumed by
later tasks. Definition state is never execution state. No alternative v4 schema
or provider service is introduced.

Execution method selected: Room Testbox user-owned session; human approval and
START dispatched2026-10-19. M3 may begin non-conflicting source tests while waiting for the Scenario
handoff, but must not mutate its active source/API or shared Editor ownership.


