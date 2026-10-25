# AlterOS shared roadmap

Updated: 2026-10-24. Read this alongside `AGENTS.md`,
[Documentation/Home.md](Documentation/Home.md), and
[the cross-session coordination note](Documentation/Tracking/Multi-Agent%20Coordination.md). This is the cross-session work
queue, not a second feature register or proof of implementation. Keep detailed
contracts and evidence in the linked notes. Update status and the next step
when taking or handing off work; do not mark complete from design approval alone.

Current-status precedence: Active work and the linked latest handoffs supersede
older milestone baselines below. Historical E refers to the earlier interaction
binding; current wiring is T for `Player.Interact` and E for inventory/held furniture.
Physical T/E acceptance remains open. See the
[2026-10-22 documentation reconciliation](Documentation/Verification/2026-10-22%20Documentation%20Reconciliation.md).

## Active work

- **F5 routing HUD2026-10-25:** implementing top-left Canvas/prefab display of last
  input, selected Topic/Scenario, top5 actual matches/IDs/anchors/similarity%, last
  transition and retained failure. Shared flag only; explicit buttons show GTE
  bypassed/n-a rather than invented scores. Bounded in-memory state; extra Console
  traces only when F5 is on. No routing/provider/persistence changes. Saved to
  ModelTest and prefab through Editor; reopened wiring and shared-flag off/on/off
  check passed. Focused formatter suite3/3. Normal gameplay visual/input and actual
  PoC movement remain unverified; do not rerun these completed HUD checks.

- **Current movement blocker2026-10-25:** imported sequence now starts, immediately
  fails primitive_capability_unavailable, returns Idle. Shared executor compared
  total primitive args against only permission-bound args; optional functionId
  made furniture reserve fail. Source correction preserves all binding checks;
  focused installed regression now asserts first actual port dispatch and denial
  without permission. Imported; single focused Unity job passed2/2, all3 PoCs
  crossed the dispatch boundary. Release preparation cold9583ms vs cached0ms;
  not total response latency. No additional suites run.
  This is not yet physical movement or full PoC acceptance. See latest ledger.

- **Latest human failure2026-10-25 20:xx:** typed Locate/Fetch still replayed TV,
  buttons were very slow, Action terminated sequence_start_rejected. Main source
  slice now clears completed Topic/response binding, reuses equivalent runtime
  vocabulary/release cache, warms pure SQL validation off the Unity frame thread,
  and materializes validated Action hashes during Flow projection. Added ONE
  focused regression class crossing installed Topic -> Scenario -> Action executor
  plus binding/cache checks (not yet run/imported). Last state was PlayMode;
  latest bridge reports NO Editor instances. No further tests/retries or readiness
  claims until import is available. Preserve scenes/profile/provider boundaries.

- **Human failure correction2026-10-25 (actual UI):** all3 typed PoCs still failed,
  and the TV favor choice stranded the menu. Console confirms correct authored
  selection; Main found the bridge rejecting optional-hash differences between
  source and compiled Topic entry references. Source now resolves both against the
  pinned release, propagates startup failures to retry, fixes offline success
  status, and closes a stranded exchange when retry is cancelled. Furniture input
  now queues controls for LateUpdate instead of querying pointer state inside an
  InputAction callback. Unity imported the changes and one focused regression job
  passed3/3; actual player reproduction remains pending. Earlier selector4/4 and
  these focused tests are NOT a UI/gameplay pass. See latest ledger.

- **Execution cadence2026-10-24:** finish cohesive goals before one consolidated
  check. No per-change tests, repeated review rounds or validation-only detours.
  Existing accepted foundations are not restarted; the execution ledger owns
  remaining implementation and exact external blockers.

- [ ] **Reusable Content Framework — live routing repaired/verified; physical action acceptance remains.**
  Latest2026-10-25 18:21: imported fallback is running on CPU. One saved live Unity
  GTE -> installedSQL -> eligibility/planner -> exact Scenario-flow check passed4/4
  (three phrases plus typo). No conversation/profile/provider effects exercised.
  Routing reminder deleted; do not rerun this accepted gate. Remaining recorded
  blockers/gaps: NavMesh/movement, input/UI and end-to-end action/item acceptance.
  Earlier pending/failed routing checkpoints below are retained as history, not
  current state. Exact report and scope are in the execution ledger.
  Human's three sentence probes failed. Main found two legacy filters excluding
  new SQL Topic anchors and catalogue documents; fixed both without rebaking or
  lowering confidence. Installed-release CLI using real local-GTE query vectors
  now selects the correct authored Topic and pinned Scenario for all3 phrases plus
  typo. Normal Unity import/in-scene retest remains open. The old Playground GUI
  reads legacy Generic JSON/fixtures and is explicitly NOT this acceptance gate;
  use its new `-Query` SQL CLI path. See the latest ledger correction below.
  Continuation2026-10-25: Unity initially responded in ModelTest PlayMode with no
  compilation/update; runtime DLL postdates fix. One live-encoder routing probe
  and one result-history retrieval timed out; result unknown, not another pass.
  Console also reports missing NavMesh and pointer-over-UI warnings. No profile,
  provider, scene or PlayMode changes; do not restart pending probe on timeout.
  Latest08:18 continuation recovered that probe as FAILED, not pending: live GPU
  emitted768 zeros for every query despite valid SQL vectors/tokenization. Same
  Unity model on CPU returns valid vector/correct Locate topic. Shared runtime/
  Playground invalid-vector guard and CPU fallback implemented/source-compiled;
  actual Unity import/fallback check is waiting for existing PlayMode to end.
  Keep scene/session untouched; no new CLI/bake/schema or broad test/review work.
  [Master workflow](Documentation/Tracking/Reusable%20Content%20Framework%20Workflow.md)
  preserves all six Workbench kinds and three proofs: turn on TV then end; locate
  TV/bark then end; fetch drink/return/dialogue/give one Water glass. The TV case
  no longer mandates returning to the player; Fetch proves that generic capability.
  Behavior/Furniture/DBMS/Topic Playground delivered aligned domain plans; main
  recorded the shared contracts, sequential W0-W5 writer windows and acceptance
  gates. W0, W1A, Furniture W1B, transport and the pure executor passed scoped
  review2026-10-23. DBMS implemented the round-two producer-contract fixes and passed
  the full 29-case persistence suite plus focused ContentData groups. Main's two
  scoped re-reviews accepted Content and the final persistence guard (Persistence5).
  Main released full W2 host/Scenario/menu integration to the existing Behavior owner;
  accepted foundations are not restarted. DBMS Topic-reference/runtime-facade and
  compiled-row integrity extension passed review; DBMS is idle. W2's first source
  review repairs passed; Unity compilation is clean. Focused Editor capability/
  ownership/registry/synthetic-root checks pass. The narrow ambient/test-lifecycle
  repair passed source review and the focused Editor rerun (ambient4/4, ownership2/2);
  the Start-method warning is absent from fresh compilation. Actual Play Mode
  fade/lifecycle behavior remains unverified.
  Topic Playground's W3B editing and W3C reference frontend fixes passed review;
  W3D previews/settings/registered sequence editor are now implemented, with the
  normal host rebuilt and focused goal-level checks passed. Actual browser settings
  save/reopen and sequence-palette visual acceptance remain unverified after an
  automation timeout; no duplicate review loop is scheduled.
  DBMS's compiled-SQL catalogue producer passed review after strictness, authority,
  provenance and locale repairs. Furniture's compiled snapshot factory also passed;
  its compiled scene consumer is implemented; settings UI is now delivered.
  See [bridge plan](Documentation/Tracking/Furniture%20SQL%20Catalogue%20Bridge%20Plan.md).
  W5 is installed on disk: shared authoring r90/464 selected documents and1,368
  locally baked GTE vectors. Backups and exact release pins are in the ledger.
  W4 identity import/rebind/save/reopen is complete; both TVs reference the persistent
  script GUID. Final focused regression1/1, scene validation0issues/missing/broken,
  Editor ready/idle and filtered Console clean. Actual browser visual gaps and live
  player acceptance remain explicit; do not restart accepted tests/source reviews.
  The human has now approved continuous implementation until only live PoC tests
  remain. [Execution ledger](Documentation/Verification/2026-10-22%20Reusable%20Content%20Implementation.md)
  owns current windows, evidence and recovery. No live/player pass is implied.

- [ ] **SQLite authoring — graphical Workbench and W5 authored integration delivered; live acceptance remains.**
  [Plan](Documentation/Tracking/SQLite%20Authoring%20Cutover%20Implementation%20Plan.md),
  [approved boundary](Documentation/Tracking/Content%20and%20Interaction%20ERD.md),
  [core verification](Documentation/Verification/2026-10-22%20SQLite%20Authoring%20Core.md).
  Workbench draft and authoring commands now use the pinned SQLite repository; the
  Editor build entry reads the explicit `default` snapshot. The local workspace has
  the retained439-source baseline plus25 new W5 sources and a greeting CAS edit:
  revision90,464 selected documents, snapshot
  `00b81b2da9d976ca14cdeb6a6b30b144551aac2612d74a8276f35af6dc3c315d`.
  Revision63 remains backed up; W5 recovery/export paths and installed identity are
  recorded in the execution ledger and core verification note.
  The 104 compiled expression fields across 61 selected documents now use the 12
  registered preset names, and validation finds no unsupported compiled expression.
  Workbench 27/27, Scenario 67/67, Unified Conversation 197/197, Authoring All 18/18,
  and ContentData Authoring/Bake/Publish/Semantics 6/11/6/4 pass. The GTE metadata
  pins the SQL Generic byte hash; vectors/tokenizer and retained legacy JSON are
  unchanged. Read-only inspection on 2026-10-22 verified the installed
  `Assets/StreamingAssets/Content/content.sqlite3` artifact's integrity and trusted
  release, and its manifest snapshot matches authoring revision 63 (SHA-256
  `67033644122DBB06884B5479A3BBC0469B23C497FEC54D39B559ED83B416A31F`). This is
  not Unity startup/player acceptance. The approved Workbench direction is a
  Flow-first graph with selected-step writing inspector and shared typed commands for
  human UI and agents. Six-kind graphical editing/settings/sequence composition and
  production-backed previews are implemented; normal host rebuilt. The historical
  r63 artifact/hash above is superseded by W5 installed bytes
  `C6B14D7E736E70BDA8A0EBACEFE32D662EE97D5A68666E9E779687172312B8D7`.
  Unity import/identity wiring is complete; browser settings/palette visuals remain
  unconfirmed after the automation timeout.
  Preserve original JSON. Final runtime gate remains `SplashScene -> ModelTest`
  and live player acceptance, not more repeated source-suite runs.

- [ ] **Full SQLite runtime migration — source/headless cutover and Unity source compilation verified; Editor acceptance and real-profile migration remain.**
  [Cutover execution amendment](Documentation/Tracking/Full%20SQLite%20Cutover%20Implementation%20Plan.md)
  extends the earlier P plan to authored/runtime content, generated archive, vectors,
  Topic consumption, sessions/repetition and transcript. SQLite is now the editable
  authoring source; retained JSON is explicit import/export evidence, not a competing
  writer. Preserve memory's SQL
  owner, secure storage, source IDs/forget policy and original legacy bytes. No actual
  P2 baked 218 authored Generic/Topic files into a 9.68 MB staging artifact with
  4,689 verified dependency rows. A fresh full-corpus artifact now covers 439 Dialogue
  sources and reopens with 6,041 dependency rows, 57 Topics, 322 Flows, 673 embeddings,
  and a 57-route/768-dimension semantic index. P3 validates typed Rules/flows, pins exact
  closure, and publishes CAS release heads. P4 typed Subflow execution is complete
  in the existing ScenarioSession owner. P5 semantic index storage and exact-snapshot
  publication, schema-2 game history and consent-bound local recording APIs are source-complete.
  F1 uses SQL for authored readers; F2 migrates Topic history into game schema 3; F3/F4
  store sessions, repetition, ambient state and transcripts in private conversation schema 2;
  F5 stores generated archive data in separate private schema 1; F6 journals the independent
  migrations before conversation/archive facades become ready. Persistence checks pass 15/15;
  unified conversation 197/197, Topic 33/33, Playground 13/13, Scenario 67/67, furniture
  52/52, and ContentData Repository/Bake/Publish/Flows/Semantics pass 8/11/6/7/4.
  The earlier staged artifact predates expression normalization. The current installed
  release was independently reopened read-only on 2026-10-22: SQLite integrity and
  foreign-key checks pass, the trusted release opens, and its authoring snapshot hash
  matches revision 63. See [full cutover verification](Documentation/Verification/2026-10-22%20Full%20SQLite%20Cutover.md).
  Unity source import/script compilation passed on 2026-10-22 with zero Console errors.
  Focused EditMode job 711636fd43d046a98d1c56ae1cfab778 reported 28/28 tests and
  seven failures in the persistence test fixture; the test-only lifecycle correction
  is in place and awaits coordinator rerun. Combined deferred-consent job
  37e843576850489aa8b2fd6d0caeec12 stalled in Unity; the two deferred tests now use
  async NUnit methods, and the safe headless coordinator suite passes 30/30. Unity
  was force-restarted with explicit user approval; focused reruns remain pending.
  The earlier Editor build failed closed at `expression_not_registered`. The SQLite
  build selection now contains only registered preset names; headless current-export
  checks pass. Focused Unity tests, `SplashScene -> ModelTest`, player acceptance and
  real-profile migration remain open. Artifact inspection is not runtime acceptance.

- [ ] **Content/interaction integration — continuous implementation under the full SQLite cutover amendment.**
  [Execution plan](Documentation/Tracking/Content%20Data%20Integration%20Implementation%20Plan.md)
  was coordinator-written/self-reviewed and sent as START2026-10-21 to thread
  01a15e76-daeb-7451-982f-95a510093fd3. The 2026-10-22 cutover amendment explicitly
  adds Topic-consumption, sessions/repetition, transcript, generated archive and
  embeddings to SQL migration; preserve separate memory and credential owners. P0/F0/
  P1/P2/P3/P4/P5/P6/P7 and F1-F6 are source/headless implemented; F7 source verification
  is recorded. Unity source import and compilation passed on 2026-10-22. The installed
  SQL release now passes read-only artifact inspection and matches authoring revision
  63. Next, rerun focused synthetic Editor tests, verify normal startup from that
  release, then perform synthetic player acceptance without touching a real profile.
  [Proposed storage/tables](Documentation/Tracking/Dialogue%20and%20Semantic%20Storage%20Design.md).
  [Consolidated ERD](Documentation/Tracking/Content%20and%20Interaction%20ERD.md)
  owns the approved model/relationships and content/history/contribution boundaries,
  with post-report owner mapping. Foundation report reviewed2026-10-21; detailed
  physical direction/adapters now belong to the integration execution plan.
  SQLite-backed Workbench is the current implementation. Production routing must use the
  compiled SQL release after format coverage, parity and Unity acceptance; no silent
  fallback. Require measured loading/query costs. This extends D0-D6 without rewriting
  its receipt, backup, or single-writer compatibility contracts.

- [ ] **Data management — D1-D6 plus game schema-3 Topic and private conversation schema-2 migrations implemented; Unity acceptance pending.**
  [Design](Documentation/Tracking/Data%20Management%20Design.md),
  [D0-D6 desktop foundation plan + session prompt](Documentation/Tracking/Data%20Management%20Implementation%20Plan.md).
  Continuous SQLite persistence, progression migration, furniture home-save adapter
  and game-only recovery first. P6 adds same-writer history/stat causality with v1
  receipt and staged-backup compatibility. F2 preserves Topic receipts/bytes through
  schema 1→2→3 and validates v2 backups; F3/F4 keep sessions/repetition/transcript
  rows in a separate private database. Python memory remains a separate owner;
  no accounts/mobile/cloud/billing or inventory gameplay implementation here.
  Approved SQLite-net1.3.2 is commit-pinned and installed; no real data migrated.
  [Fresh prerequisite check](Documentation/Verification/2026-10-21%20Data%20Management%20Prerequisites.md)
  and [foundation evidence](Documentation/Verification/2026-10-21%20Data%20Management%20Foundation.md):
  as of 2026-10-21, the persistence suite was 14/14 and Topic runtime 33/33 with the real
  SQLite-net binding and packaged Windows x64 native plugin. The full Unity source
  compile now passes. The current content SQL release is installed and passed
  read-only integrity/trusted-release inspection; this does not establish startup or
  player acceptance. A focused Editor job exposed a test lifecycle setup issue now
  corrected in test-only code. Player build/runtime, Play Mode, Android and actual
  user-profile migration remain unverified. See
  [handoff](Documentation/Tracking/Data%20Management%20Handoff.md).

- [ ] **Furniture inventory / placement input / shared Debug Mode — I1-I5 implementation is ready for the live test; player acceptance remains.**
  [Decision note](Documentation/Tracking/Furniture%20Inventory%20and%20Debug%20Design.md)
  records the approved 10 additional starting units per type, finite stock outside
  F5 Debug Mode, T world interaction, E inventory, Q pickup/place, held E/right
  return, wheel/Z/X rotate, C snap toggle, and F cancel. ModelTest now has the
  shared F5 owner, floor and North wall-opening support, inventory/preview/diagnostic
  prefabs, shared furniture activity/edit claims, and wired controllers. The
  canonical pre-live record reports Furniture84/84, SQLite persistence23/23,
  focused Unity EditMode29/29, Scenario Runtime67/67 and Unified Conversation197/197.
  ModelTest validation found zero scene issues and the scene was saved clean at that
  checkpoint. These are recorded results, not fresh checks from this reconciliation.
  No Play Mode, physical key, Game View, restart or
  player-profile check was run. Current hashes, evidence and the live-test boundary
  are in [the pre-live verification](Documentation/Verification/2026-10-22%20Furniture%20Inventory%20I4-I5%20Pre-Live%20Implementation.md).
  [Plan](Documentation/Tracking/Furniture%20Inventory%20and%20Placement%20Implementation%20Plan.md);
  next is the live implementation test through an approved isolated or
  human-approved path.

- [ ] **Furniture foundation + standalone content workbench — furniture wiring is complete; graphical Workbench phases and player acceptance remain.**
  [Shared interface](Documentation/Tracking/Content%20Workbench%20Shared%20Contract.md),
  [Room plan](Documentation/Tracking/Furniture%20Foundation%20Implementation%20Plan.md),
  [Workbench plan](Documentation/Tracking/Content%20Workbench%20Implementation%20Plan.md).
  Furniture and Environment owns registry/resolver/reservations/layout and the narrow
  scene adapter; the existing Workbench plan owns Flow-first content authoring.
  Furniture F1-F4 and I1-I5 source/editor wiring, including the home-save adapter,
  shared ActivityHost bridge, inventory/preview/diagnostic UI and ModelTest scene
  references, are in place. The canonical pre-live note records Furniture84/84,
  persistence23/23, Scenario67/67, Unified197/197 and focused Unity EditMode29/29;
  these are recorded evidence, not new tests from this reconciliation. Physical T/E,
  Game View, Play Mode, restart and player acceptance remain open.
  Workbench W1 typed command backend exists; graphical Flow-first W2-W3, preview/parity
  W4 and acceptance W5 remain open under the approved plan. Workbench must not duplicate
  furniture logic or invent availability. No provider spend, full room furnishing editor,
  physical seating
  executor/cameras, production publication or cloud sync in this first slice.

- [x] **Furniture catalogue / arrangement design — refined proposal ready; bounded execution now authorized above.**
  [Design and handoff](Documentation/Tracking/Furniture%20Mechanics%20and%20Catalogue%20Design.md):
  separate furniture, deterministic proximity/facing/height/access relationships,
  table-optional sitting together, host-evaluated locked choices, and atomic
  multi-point reservations. Human decisions resolve player-led seating, home-save
  ownership, and stale-choice refresh with a new click. The expanded proposal
  defines all eight starter furniture IDs/point semantics, placement/move/rotate/
  remove revision commands, active-use protection, save seeding/orphan handling,
  a room-local metre/quaternion format, Windows/Android round-trip fixture, and
  register-once catalogue/tool discovery. Builder check closed the support and
  scale contract gaps; all v1 starter items use fixed unit scale and stable generic
  support identities. The shared contract resolves explicit Reload for the
  workbench v1 and no edited-save auto-migration; player-facing migration UI is
  deferred. F4 wiring/home-save integration is complete; physical input and player
  acceptance remain open.
  Runtime implementation is tracked by the bounded plans above, not this design item.

- [ ] **Activity Scenario follow-ups — source checks pass; Unity/player acceptance pending.**
  [Evidence](Documentation/Verification/2026-10-21%20Activity%20Scenario%20Followups.md),
  [technical contract](Documentation/Tracking/Topic%20Scenario%20Activity%20Technical%20Reference.md).
  Follow/Drink now own authored Scenarios; earlier contextual-E follow-ups describe
  the old binding, while current source uses T for interaction and E for inventory.
  Drink→return→bark→next-interaction closing and a host-gated future initiative seam
  remain. M3 stubbed56/56, Scenario67/67; Unity job result timed out. Check physical
  T/E controls, return and closing.

- [ ] **Greeting and activity debug UI — Editor wiring is recorded; physical input and player check pending.**
  [Evidence](Documentation/Verification/2026-10-20%20E%20Interaction%20Menu%20and%20Activity%20Debug.md):
  fixed root entrances, Favor→Follow/Stop, Together→Drink/input, three LLM-first/random
  authored topics; above-Alterra Canvas state/action/Scenario label and lifecycle
  Console logs. Unity focused63/63 at that historical checkpoint. Current input is
  T for `Player.Interact` and E for inventory/held furniture; verify physical T/E,
  actual menu, action/Stop and debug readability in Play Mode. No new provider/TTS
  call authorized.

- [ ] **Scenario conversation redesign — Tasks 1–9 source/package checks pass; current Unity acceptance is inconclusive; human gameplay acceptance remains.**
  [Design](Documentation/Tracking/Scenario%20Conversation%20Design.md) records
  Topic/Scenario/Activity ownership, lifecycle, parallel Appetizer (five-second
  first-visible fallback target), Ask Player versus next-interaction offers and Dessert.
  Full read-only implementation baseline and builder review completed
  2026-10-19; approved defaults clarify that Dessert is main-response closing,
  not a separate stage. Tasks 1–7 now have source implementation; Task 7 adds
  authored Topic conversion and a detached Scenario Playground/fixture CLI.
  [Plan](Documentation/Tracking/Scenario%20Conversation%20Implementation%20Plan.md),
  [handoff](Documentation/Tracking/Scenario%20Conversation%20Handoff.md).
  Task 8 now connects the negotiated v4 path through the existing coordinator
  and sole transcript writer, queues/presents VN beats through existing Canvas
  templates, and wires question answers/free text, `Player.Interact` (T) resume,
  and next-interaction offers.
  Fresh review fixes cover cancellation races, response-receipt durability,
  continuation dismissal checkpoints, the submit-time fallback clock, and the
  RAG-candidate/authored-fallback split plus v4-only Editor helper resolution.
  Recorded source checks: Scenario67/67, Unified Conversation197/197, Topic Playground
  13/13, Python171 passed/2 skipped (latest full suite; earlier packaged keyless
  v3/v4 smoke evidence remains in the 2026-10-20 verification note), and catalogue validation 57 families/161
  scenarios/161 rows. The
  prior focused Unity EditMode result predates these fixes; the latest Editor
  test request timed out, so current Unity compile/EditMode is unverified.
  Still pending: human Splash->ModelTest->T gameplay, visual question/offer/
  return-prompt review, and measured first-visible timing. No provider/TTS spend
  was made or authorized.

- [x] **Furniture and Environment room fixture — authored in ModelTest; gameplay acceptance pending.**
  [Handoff](Documentation/Tracking/Room%20Testbox%20Handoff.md). Replaced only
  the prior environment root with a 9 m x 9 m open-front room, primitive study
  desk/paired seats, water dispenser, couch/TV, colliders, and 20 furniture-
  parented approach/pose/use/exit transforms. Player and Alterra, player camera,
  light, UI roots and startup/build order remain. Marker hierarchy is not a
  production schema; navigation, activity use and player-visible acceptance
  remain unverified. Unity Console showed unrelated Scenario source errors.

- [ ] **Topic Playground — additive family/scenario integration in source; acceptance partial.**
  [Handoff](Documentation/Tracking/Topic%20Runtime%20Playground%20Handoff.md),
  [design handoff](Documentation/Tracking/Topic%20Playground%20Handoff.md).
  Legacy route/stat-context-v1 behavior remains. New semantic-family and
  direct-family test modes call the shared Topic selector/history session,
  with detached stats/facts/objects, deterministic draw/time, lifecycle actions,
  JSON/window input parity fixture and opt-in temp-only scratch persistence.
  The 2026-10-22 repaint fix moves the 611 MB model-file SHA-256 to a cached,
  cancellable background worker, invalidated only when Unity imports/deletes/
  moves that model asset; pending/failure state is visible, including in the
  CLI report. `Check-TopicPlayground.ps1 -Filter TopicPlayground` passes 15/15;
  Unity source compilation has zero errors and the focused fingerprint EditMode
  test passes 1/1. See
  [dated verification](Documentation/Verification/2026-10-22%20Topic%20Playground%20Responsiveness.md).
  No live GTE inference/model hashing, actual repaint profiler comparison,
  visual review, or CLI/window report parity was run. Stop before gameplay
  loader or production catalogue integration.
- [ ] **Stat catalogue — foundation source implemented; Unity validation pending.**
  [Starter catalogue](Documentation/Tracking/Stat%20Catalogue.md),
  [stats/mood decisions](Documentation/Tracking/Stats%20and%20Mood.md).
  Set meanings, IDs, ranges, recovery/decay, conditions and migration policy;
  provide an explicit draft contract for Playground fixtures, not fake runtime support.
  Current review: [Stats Revamp Audit](Documentation/Tracking/Stats%20Revamp%20Audit.md).
  [Foundation plan](Documentation/Tracking/Stats%20Foundation%20Implementation%20Plan.md)
  was approved and implemented for nine Alterra IDs, snapshots and a read-only
  legacy adapter plus shared numeric eligibility. Supplemental source checks
  pass 31/31; that dated extension's Unity validation remained pending, not a
  current instruction to rerun a full suite. Later import/compile and the separate
  fingerprint test do not prove all stats tests passed. [Evidence
  and Playground handoff](Documentation/Verification/2026-10-16%20Stats%20Foundation.md).
  Full persistence/simulation is not implemented. Player hidden signals remain
  [separate contextual evidence](Documentation/Tracking/Player%20Context%20Evidence.md), not default-filled stats.

## Foundations and presentation

- [ ] **Topic scenario selector and instance lifecycle — new source foundation implemented; integration pending.**
  [Selection/replay rules](Documentation/Tracking/Topic%20Scenario%20Selection.md),
  [source audit](Documentation/Tracking/Topic%20Scenario%20Refactor%20Audit.md).
  Shared anchors, compound gates, priority-then-weight, whole-instance consumption;
  coordinate shared selector/context edits with the Playground session.
  [Runtime-only plan](Documentation/Tracking/Topic%20Runtime%20Implementation%20Plan.md)
  approved and executed with completion-time cooldown. New pure runtime has
  31/31 supplemental source checks; existing stats/Playground source checks
  remain 31/31. After bridge recovery and current-source import, the full
  `AlterOS.Editor.Tests` suite passed105/105 (see Topic runtime evidence).
  [Catalogue handoff](Documentation/Tracking/Topic%20Runtime%20Catalogue%20Handoff.md)
  and [Playground handoff](Documentation/Tracking/Topic%20Runtime%20Playground%20Handoff.md)
  define separate next tasks. No live-loader switch, corpus migration or UI
  integration is included in this foundation.
- [ ] **Emotional simulation:** event-to-axis rules, mixed context, baseline
  recovery, stable thresholds. [Stats and Mood](Documentation/Tracking/Stats%20and%20Mood.md).
- [ ] **Mood display:** locked layered summary/details/spectra and supporting
  palette; implementation pending. Author final flavor copy and Canvas UI.
- [ ] **Activity runtime:** Come here/Follow/Drink phases, correlated Scenario
  events and sip checkpoints exist in source; live acceptance and broader activity
  simulation remain. See the M3 and Activity Scenario follow-up entries above.
  The [checkpoint/pose design amendment](Documentation/Tracking/Scenario%20Conversation%20Design.md#scripted-activity-checkpoints-and-pose-safeguard-2026-10-22)
  records readable mid-Activity dialogue branches and a 30-second post-end pose
  safeguard; neither is an implemented extension or a new execution assignment.
  [Action Framework Foundation plan](Documentation/Tracking/Scripted%20Actions%20and%20Inspect%20TV%20Implementation%20Plan.md)
  is prepared for review with Alterra Behavior as the conversation owner and DBMS
  storage dependencies, under the new approved design details; implementation remains
  unstarted. Owner consultation is incorporated: Turn on TV replaces passive Check tv;
  typed furniture functions, per-instance persistent Off/On state and visible binding
  are planned through the existing game writer. Thin C# composition and data recipes
  share execution; automatic Scenario closing remains. Prior Inspect/LLM Planning
  wording is historical, and the plan filename is retained for existing links.
  Main purpose is reusable Workbench preparation, not TV mechanics: the plan now
  includes portable shared execution and descriptor-driven validation/headless preview.
  Full Dialogue/Scenario/Topic/Furniture/Item/Action editor readiness is indexed in
  [the shared contract](Documentation/Tracking/Content%20Workbench%20Shared%20Contract.md#workbench-wide-target-and-readiness-2026-10-22).
  See the current design/plan; no source implementation START has been sent for this
  extension.
- [ ] **Furniture/use-points:** portable catalogue/resolver/reservations/layout and
  narrow scene adapter are implemented and wired; current Unity synthetic/EditMode
  evidence is in the pre-live note. Physical input, Play Mode and player evidence remain.
- [x] **Replacement room foundation:** ModelTest shell and approved dispenser/
  NavMesh wiring are recorded in the M3 handoff. Earlier room-layout snapshots
  are historical, not current proof of every furniture instance or live navigation.
- [ ] **Expression vocabulary:** supported expression/gesture/gaze IDs,
  state prerequisites, activity compatibility and neutral fallback.
- [ ] **Contextual greetings:** priority queue, expiry, consume-on-presentation
  policy, repetition and busy acknowledgement; later conversation stays player-led.

## Conversation vertical slice

- [x] **Earlier flat M3 E-menu action shortcuts — superseded, not player-accepted.**
  [Evidence](Documentation/Verification/2026-10-20%20Activity%20Greeting%20Choices.md).
  Historical flat Come here/Follow me/Drink water menu is replaced by the fixed
  root/favor/together contract in Active work above. Earlier Editor56/56 and
  conversation source188 passed/6 failed remain historical, not new acceptance.

- [ ] **M3 — Topic/Scenario integration with furniture and activities (conversation hook and saved dispenser/NavMesh wiring in place; Play Mode acceptance pending).**
  [Approved design](Documentation/Tracking/M3%20Furniture%20and%20Activities%20Design.md):
  first reusable activities are Come here, Follow me and Drink water, with
  offline authored Scenario fixtures. Human design approval2026-10-19;
  [implementation plan](Documentation/Tracking/M3%20Furniture%20and%20Activities%20Implementation%20Plan.md)
  approved and START sent2026-10-19 to Furniture and Environment Session
  (`01a15376-d6f0-7c41-b9e5-fcdee723e87b`, resolved host local). The
  [handoff](Documentation/Tracking/M3%20Furniture%20and%20Activities%20Handoff.md)
  records reusable host/movement/furniture/drink/hydration seams, correlated
  Scenario projection, and three isolated Testbox fixtures. The external
  stubbed policy/Scenario harness passes 48/48. On 2026-10-20 the user directed
  replacing the ModelTest room while keeping Player and Altera. The prior room
  furniture/architecture was removed and a neutral 8 m x 8 m shell saved;
  dialogue/UI, camera/light, actor roots and startup settings remain. On
  2026-10-20 the approved dispenser was restored with its typed use-point,
  Altera motion/hydration links, and the existing NavMesh asset was rebuilt and
  assigned. Unity Editor queries resolved Altera, Player, and the dispenser
  approach and a complete Altera-to-dispenser path. Unity source compilation
  completed with zero Console errors after the activity conversation hook was
  added. The Unity EditMode Activity group initially ran48/50; main-session
  fixes now pass51/51 including a
  new serialization regression. Native path-construction and hydration fixture
  blockers are fixed; full Editor result timed out.
  [Evidence](Documentation/Verification/2026-10-20%20M3%20Blocker%20Fixes.md).
  `DirectConversationBridge` now captures host-owned capabilities and typed
  objects, sends `request_activity` through the host, advances correlated
  results, drains terminal wait events, and routes `Player.Interact` (T) exchanges/
  Follow Stop through the same activity owner; E is reserved for inventory/held-furniture
  handling. No Play Mode, physical T/E input, live furniture action, movement,
  persistence, or SplashScene→ModelTest acceptance has been run.
  Furniture and Environment Session also owns the separately approved inventory/placement I1–I6 plan
  dispatched on 2026-10-22; see Active work. Dispatch is not runtime acceptance.
  Complete Unity/player acceptance for the conversation-to-activity path. Build
  on the Scenario conversation foundation and
  [room fixture](Documentation/Tracking/Room%20Testbox%20Handoff.md).
  The original dispenser-only M3 slice excludes desk participant pairing/cameras;
  the later inventory/placement plan separately adds furnishing controls, not
  sitting executors, house construction or cameras.
  User reordered milestones2026-10-19; phone/history/optional TTS move to M4.
  Furniture extension approved: 3D candidate proximity plus typed geometry/access
  checks, optional safe authored overrides and decorative invalid layouts.
  General chair/desk/TV discovery remains later integration; dispenser-only M3
  does not implicitly expand into a furnishing system.

- [ ] **Player memory M2.1 — memory-aware source path integrated; onboarding/question controls and Unity/player acceptance pending.**
  [Design](Documentation/Tracking/Player%20Memory%20Design.md),
  [implementation plan](Documentation/Tracking/Player%20Memory%20Implementation%20Plan.md),
  [handoff](Documentation/Tracking/Player%20Memory%20Handoff.md),
  [verification](Documentation/Verification/2026-10-19%20Player%20Memory%20M2.1%20Implementation.md).
  Tasks 1–3 have source implementation/regressions, including transcript
  checkpointing, v3 turn context, consent/suppression revalidation and no unsafe
  v2 downgrade. The structured proposal wrapper and pure question policy exist;
  question-history/settings integration and pending-proposal review remain.
  Authored bilingual onboarding JSON, a headless session model and durable
  progress reads exist, but first-E and Canvas controls are not wired. Fresh
  source/package checks: Python129/129, C#106/106, Topic Runtime33/33,
  Playground13/13, catalogue57/161; onedir helper rebuilt and keyless restart
  smoke passed. Unity read-only Console timed out; no Editor compile/UI/Play
  Mode or player acceptance. No new provider/TTS call. Prior M2 runtime and the
  DigitalOcean null-content issue remain open.

- [ ] **Unified conversation M2 — source integration implemented; runtime acceptance pending, live provider spend separately gated.**
  [2026-10-18 smoke](Documentation/Verification/2026-10-18%20Conversation%20Smoke.md):
  Python119/119, conversation source97/97, catalogue57/161; two authorized
  synthetic OpenRouter text requests succeeded (reported total USD0.0003091632).
  Focused Unity test job started but its result timed out; no Play Mode or TTS.
  This supplements, not replaces, the historical evidence below. 2026-10-19
  recovery regressions additionally pass in the 106/106 conversation/memory
  source suite; latest Unity Console access timed out and no Play Mode was run.
  [M2 implementation plan](Documentation/Tracking/Unified%20Conversation%20M2%20Implementation%20Plan.md).
  Direct-channel sidecar/provider integration, JP speech + EN display, coverage
  pilot, persistent history and Canvas online toggle are implemented in source
  and the saved `ModelTest` scene. The toggle defaults off; the direct VN path
  is text-only and never requests TTS. Approval/clarifications are in the plan.
  Final source checks after review fixes: C#72/72, Python42/42,
  Playground13/13, Topic Runtime33/33, promoted Topics57/161. The previous Unity
  compile succeeded after moving the HUD installer; current Console-diagnostics
  edits passed source checks109/109 and Unity reimport rebuilt `Assembly-CSharp`
  without the reported preprocessor error. Two unrelated Topic Playground
  serialization-depth errors remain. Focused M2 EditMode16/16 predates final fixes.
  The full 190-test Editor request stalled the Unity process before a result,
  so full EditMode, no-key Play Mode, Splash→E acceptance, visual UI review,
  and latency/performance measurements remain pending. At that earlier check,
  no key/provider call occurred; the later bounded smoke is recorded below.
  **2026-10-19 reply-format fix:** provider diagnostics isolated a schema
  mismatch. The project contract is now `reply.beats` (not `reply.nodes`);
  authored Topic `nodes` remain unchanged. Prompt, Python/Unity validators,
  fixtures, and transcript serialization are aligned. Latest Python suite:
  140 passed, 1 skipped; C# source checks115/115. One capped synthetic provider
  smoke passed the exact beat contract. See
  [verification](Documentation/Verification/2026-10-19%20Agent%20Reply%20Schema%20Fix.md).
  Follow-up: suggested-reply `label` and `prompt` now follow English `displayText`;
  Unity drops non-Latin suggestions while preserving the valid reply, and the
  Editor helper is rebuilt. See
  [language evidence](Documentation/Verification/2026-10-19%20Suggested%20Reply%20Language.md).

- [x] **Unified conversation M1 — headless source foundation implemented; player integration remains pending.**
  [Approved direction](Documentation/Tracking/Unified%20Conversation%20Design.md),
  [implementation plan](Documentation/Tracking/Unified%20Conversation%20Implementation%20Plan.md),
  [handoff and evidence](Documentation/Tracking/Unified%20Conversation%20M1%20Handoff.md).
  Replaces the player-facing Topic/Agent switch with authored-first,
  retrieval-assisted conversation. M1 contracts, channel gates, scenario
  completion evidence, top-8 candidate retrieval/preparation, unseen-first
  authored selection, synthetic judgment planner and four production-planner
  fixtures are in source. Headless checks pass30/30; Playground13/13; Topic
  runtime33/33; staged catalogue57 families/161 scenarios. Unity EditMode149/150: the sole failure
  is the unrelated `HeadlessValidatorInvokesTheProductionTopicValidator`
  expectation; no Play Mode. That is historical M1 evidence. M2/M2.1 and v4
  source integration now exist; their acceptance gates are tracked above, not
  a request to implement the sidecar/consent/coordinator again.

- [ ] **Unified conversation M2 acceptance — implementation integrated; Editor/runtime proof pending.**
  See [M2 handoff](Documentation/Tracking/Unified%20Conversation%20M2%20Handoff.md)
  and [verification](Documentation/Verification/2026-10-17%20Unified%20Conversation%20M2.md).
  Final source suites pass (C#72/72, Python42/42, Playground13/13, Topic
  Runtime33/33); focused Unity EditMode16/16 predates two final review fixes.
  The full Unity Editor request hung.
  Confirm a responsive correct-project Editor, rerun focused corrected tests,
  then use an isolated profile for Splash→interaction→offline-authored acceptance
  and visually inspect Canvas controls. Do not run a broad suite or touch a real
  profile to bypass the current isolation/content-release blockers. Keep
  online off. A paid OpenRouter check needs separate explicit spend approval.

- [ ] **M4 — Phone/shared history UI:** one session and channel-specific delivery;
  transcript foundation and late-response handling are in M2.
- [ ] **M4 — Optional TTS acceptance:** M2 direct generated VN path is text-only;
  TTS controls, synthesis, cache reuse and failure/audio checks remain future work.
- [ ] **Focus/initiative gate:** authoritative interruption protection shared
  by conversation, optional need offers and speech.

See [Live journal](Documentation/Tracking/Live%20Conversation%20Journal.md),
[Feature Register](Documentation/Tracking/Feature%20Register.md) and
[Session Zero](Documentation/SessionZero.md) for direction versus evidence.
The Playground is the immediate tooling priority; do not make the full
simulation a prerequisite for a first Live reply.

## Later, not prerequisites for the Playground

- [ ] **External socializing / learned Topics — explore, not implement yet.**
  [Brainstorm Inbox](Documentation/Tracking/Brainstorm%20Inbox.md): counterpart
  choices, consent/privacy/moderation, bounded costs, sourced candidate Topics
  and ready-only personal publication. No autonomous network access approved.
- [ ] **Agent tools:** select actual capabilities, schemas, consent,
  preconditions, authoritative outcomes and idempotency.
  [Agent Tools](Documentation/Tracking/Agent%20Tools.md).
- [ ] **Personalization:** supported/correctable memories, background dialogue
  generation, deterministic validation and ready-only publication.
- [ ] **Player sentiment:** evaluated model, uncertainty/expiry, disclosure and
  permission boundaries. Explicit focus/consent always overrides inference.
- [ ] **House expansion and cosmetics:** construction, shop/economy, cosmetic
  ownership/equipping and later migrations. Basic furniture inventory/placement
  is already approved and assigned in Active work, not wholly deferred here.

## Working rules

- Work only in `E:\Pleiades\alteros`; `pleiades.os` is archive-only.
- Preserve other sessions' edits. This file does not authorize messaging or
  interrupting another session, nor confirm its current status.
- No LLM-authored arbitrary stat deltas/currency. Game systems own effects.
- Private chats, personal generated content and credentials stay out of shared docs.
- Report source, compile/test, visual, physical-input and audio evidence separately.

## Change log

- 2026-10-22: Reconciled stale foundation/milestone status, current SQLite storage
  ownership, input approval versus implementation and focused acceptance gates.
  See the documentation reconciliation note; no new runtime or provider proof.

- 2026-10-22: Removed synchronous full-model SHA-256 from Topic Playground repaint;
  source checks 15/15, Unity compilation zero errors, focused EditMode 1/1. Live
  GTE, actual repaint profiling, visual review and CLI/window parity remain open.
- 2026-10-20: Completed Scenario conversation Task 8 v4 coordinator/UI-template
  integration and Task 9 source/package checks; fixed fresh-review findings for
  cancellation, durable receipts, continuation resume, and submit-time deadline.
  Scenario67/67, Unified194/194, Playground13/13, Python173 passed (including
  packaged keyless v4/v3 smokes); latest Unity EditMode request timed out, so current
  Unity status is unverified. Human Splash->ModelTest->E, visual, TTS/provider,
  and first-visible timing acceptance remain pending.
- 2026-10-19: Updated the M2.1 source/package status after the v3 turn path,
  unclassified-memory safety guard, onboarding progress read/session model,
  timeout recovery regression and side-by-side helper build. Python129/129,
  C#106/106, Topic Runtime33/33, Playground13/13, catalogue57/161. First-E and
  Canvas memory controls, Unity/runtime/player acceptance, question scheduling,
  pending proposal review and provider acceptance remain open.

- 2026-10-16: Extended Topic Playground with detached, catalogue-driven
  synthetic context and shared v1 numeric eligibility; source checks 31/31.
  Unity compile/import, new Editor tests, visual review, real GTE and command/
  window parity remain pending due stale bridge; unsaved scene preserved.
- 2026-10-16: Integrated synthetic Topic family/scenario selection and
  occurrence lifecycle into the shared Playground session; Playground compile
  plus focused source tests 13/13 and stat-context regression 31/31. Unity
  import, GTE inference, visual review and actual CLI/window parity remain
  pending; active scene preserved.
- 2026-10-16: Added shared Brainstorm Inbox, external-socializing exploration,
  probabilistic Topic direction and unresolved adequate-self-care policy.
- 2026-10-16: Established shared roadmap from the activity/stats/Live brainstorm;
  recorded other-session Playground ownership and local stat-catalogue discussion.

