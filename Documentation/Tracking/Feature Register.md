---
title: Feature Register
tags: [alteros, register, features]
reviewed: 2026-10-23
---
# Feature Register

2026-10-25 debug tooling: top-left F5 routing Canvas/prefab is saved in ModelTest,
with last input, selected Topic/Scenario, top5 IDs/anchors/similarity%, elapsed
trace and retained failure. Shared flag only; Console trace gated on F5. Focused
formatter3/3 and reopened wiring/synthetic visibility checks passed. Normal game
appearance/input/physical activities remain unverified. See [[Debug Mode]].

2026-10-25 permission follow-up: prior slice and shared primitive permission fix
are imported; one focused Unity job passed2/2, all3 installed PoCs cross first
operation dispatch and missing permission still blocks. Release preparation
9583ms cold/0ms cached; not total latency. Physical actions remain unverified.

2026-10-25 follow-up failure: completed Topic binding incorrectly overrode new
matches, fresh vocabularies defeated execution-release caching, and projected
empty Action hashes failed executor startup. Repairs and two focused regression
tests are SOURCE ONLY pending import; no new latency/UI/action acceptance yet.
Latest MCP reports no connected Editor. See execution ledger before resuming.

2026-10-25 actual UI correction: the human reproduced all3 PoC submission failures
and a stuck TV favor menu despite correct route selection. Main repaired optional
Flow-reference hash comparison, startup error propagation/retry cancellation,
offline result status and InputAction/EventSystem warning source. Source changes
do not close UI/action acceptance; see the latest execution ledger.

2026-10-22 reconciliation: source import/compilation passed during runtime SQL
work; the installed content artifact passed read-only integrity/trusted-release
inspection and matches authoring revision 63. Corrected focused Editor tests, normal
startup and player acceptance remain open. SQLite authoring source/cutover is
implemented and headlessly verified; the approved Flow-first Workbench editor remains
open through W2-W5. Furniture F4 and I1-I5 are Editor-wired on ModelTest. The current
input map is T for `Player.Interact`, E for inventory/held furniture; physical
T/E and player acceptance remain open. See the current
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

Related: [[Home]], [[SessionZero]], [[Design Decisions]], [[Runtime Observability]].

These are documentation snapshots. Evidence comes from the 2026-10-14 audit,
2026-10-15 MCP follow-up, and bounded slice 0A runtime attempt recorded in
[[SessionZero]]. Keep design, implementation, scene wiring, and player-visible
verification separate; an attempted startup is not a dialogue pass.

| ID | Feature | Implementation / wiring | Runtime evidence | Source / next gate |
| --- | --- | --- | --- | --- |
| RCF-01 | Reusable six-kind content framework and Workbench | Workbench/framework/W5 delivered. SQL route/catalogue filters repaired; invalid-vector guard/CPU fallback imported. Actual live Unity CPU encoder plus installedSQL selection/eligibility/planner/pinned-flow gate passed4/4 on2026-10-25 18:21 | All3 proof phrases plus typo resolve expected authored Topics and ready Scenario flows,60routes/60Topics. Report persisted; routing heartbeat deleted. This is NOT dialogue UI/movement/item/full-startup acceptance. Recorded NavMesh/input gaps and browser settings/palette visuals remain unresolved; no broad reruns | [[Reusable Content Framework Workflow]], [[Furniture SQL Catalogue Bridge Plan]], [[Six Kind Workbench Plan]], [[../Verification/2026-10-22 Reusable Content Implementation]]; latest ledger owns exact live-routing evidence/remaining gaps |
| SQL-AUTH | SQLite-owned authored content and drafts | W1 typed Workbench commands and Editor build use the shared repository, explicit import/export/backup and pinned `default` selection; local revision-63, 439-source store/export are present; reparse-point guards cover export and Editor build paths. Approved UI direction is a Flow-first graph/selected-step inspector sharing typed commands with agents; W2-W5 remain open | Workbench27/27; Scenario67/67; Unified Conversation197/197; Authoring All18/18; ContentData Authoring6/6, Bake11/11, Publish6/6, Semantics4/4. 104 compiled expression fields now use registered presets. Installed artifact integrity/trusted-release inspection passed; manifest matches authoring revision 63. Startup/player acceptance remains open | [[SQLite Authoring Cutover Implementation Plan]], [[Content Workbench Shared Contract]], [[Content Workbench Implementation Plan]], [[Content and Interaction ERD#SQLite authoring authority approved 2026-10-22]], [[../Verification/2026-10-22 SQLite Authoring Core]], [[../Verification/2026-10-22 Full SQLite Cutover]]; verify `SplashScene -> ModelTest` |
| SQL-FULL | Full runtime content/history SQLite cutover | F1-F6 source cutover, private generated archive SQL, and trusted startup migration journal are implemented; Python memory and credentials remain separate owners | Persistence 15/15; unified197/197; Companion Runtime 171 passed/2 skipped; Topic33/33; Playground13/13; Scenario67/67; furniture52/52; ContentData Repository/Bake/Publish/Flows/Semantics 8/11/6/7/4. Installed authored release passed read-only inspection and matches revision 63. Focused Editor tests, startup/player acceptance and real-profile migration remain open | [[Full SQLite Cutover Implementation Plan]], [[Content Data Integration Handoff]], [[../Verification/2026-10-22 Full SQLite Cutover]]; next `SplashScene -> ModelTest` and synthetic acceptance |
| ERD-01 | Shared content, execution/history and opt-in recording model | Content/compiler/Flow/semantic/game-history foundations plus SQL owners for Topic consumption, sessions/repetition/transcript and generated archive; default capture remains off | Synthetic SQL/native-binding and source parity checks pass. Installed catalogue artifact passes read-only integrity/trusted-release inspection; Unity startup/player acceptance remains open | [[Content and Interaction ERD]], [[Content Data Integration Implementation Plan]], [[Content Data Integration Handoff]], [[../Verification/2026-10-22 Full SQLite Cutover]] |
| DM-01 | Continuous local game persistence and device authority | D1-D6, schema-3 Topic migration, schema-2 private sessions/repetition/transcript, and per-domain recovery journal; memory remains a separate owner | Persistence 15/15 and Topic33/33 pass with the real binding and packaged Windows x64 native plugin. Unity source import/compilation passed on 2026-10-22; focused Editor tests, Play Mode, player, Android and real-profile migration remain unverified | [[Data Management Design]], [[Data Management Implementation Plan]], [[Data Management Handoff]], [[../Verification/2026-10-21 Data Management Prerequisites]], [[../Verification/2026-10-21 Data Management Foundation]], [[../Verification/2026-10-22 Full SQLite Cutover]] |
| F-01 | Startup and T interaction / E inventory | Temporary Splash gates ModelTest on GTE readiness, displays stages, offers retry | Normal Splash → Ready → ModelTest and synthetic failure/retry rechecked; T interaction and E inventory are wired, physical T/E input unverified | [[Startup and Loading]]; [[Verification/2026-10-15 Startup and Expressions]]; [[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]]; S0-01/02 |
| F-02 | Authored/VN playback | Input Cancel ends the greeting; Escape dismisses routing waits; Canvas/prefab layouts corrected | Six greeting options and Topic input visibly checked; assisted Cancel/Escape released locks; full naturally timed branch completion and physical input remain player checks | [[../Verification/2026-10-15 Topic Recovery and Canvas]]; S0-02/04 |
| F-03 | Topic routing | Sorted eligible variants and actual playback-success result replace first-file assumed success | Talk callback routed tired slang; forced-confidence fallback, blocked-first/eligible-second, all-blocked and missing-fallback fixtures passed in Play Mode | [[Dialogue Catalogue]]; [[../Verification/2026-10-15 Topic Recovery and Canvas]]; S0-03/04 |
| F-04 | Semantic catalogues | Runtime route index and promoted Topics load from the pinned SQL release; only model/tokenizer inference assets remain in Resources | Installed release has 673 embeddings, 57 routes and 768 dimensions; source parity and read-only artifact inspection pass. Unity source compile passed; startup and player route acceptance remain unverified | [[Semantic Index]], [[Dialogue and Semantic Storage Design]], [[Content Data Integration Handoff]], [[Full SQLite Cutover Implementation Plan]], [[../Verification/2026-10-22 Full SQLite Cutover]] |
| F-05 | Conversation selection | Legacy mode API remains unused; unified authored-first M2/v4 source is integrated without a player-facing Topic/Agent switch | Source/scene evidence exists; current release/startup/player acceptance remains pending | [[Unified Conversation Design]], [[Unified Conversation M2 Handoff]]; S0-05 |
| F-06 | Unified conversation | M2/M2.1 source path plus negotiated v4 Scenario RAG caller; existing host coordinator is the sole transcript writer; bilingual VN beats, keyless offline helper/memory, opt-in online generation, authored-first shortcut at >=0.850, and development Console similarity diagnostics | 2026-10-20 current source: Unified194/194 and Python173/173 with packaged keyless v3/v4 smokes; Scenario67/67. RAG receives top-eight candidates below the authored gate. Current v4 helper/provider failure returns unavailable without local authored or active-Topic fallback; follow-up gap recorded. Editor resolver uses only the rebuilt v4 helper folder. Unity compile after the M3 hook has zero Console errors; latest Unity EditMode request timed out, and there is no current Play Mode, visible timing, provider, or TTS proof; known NavMesh/recursive-serialization diagnostics remain as earlier findings | [[Unified Conversation Design]], [[Unified Conversation M2 Handoff]], [[Player Memory Handoff]], [[Scenario Conversation Handoff]], [[../Verification/2026-10-20 Scenario Conversation M2.1 Implementation]]; human Splash->ModelTest->E acceptance, no provider/TTS spend |
| F-19 | Unified conversation M1 foundation | Provider-neutral contracts, strict parsing/validation, optional channels, scenario completion counters/history v2, GTE candidate retrieval/preparation, authored-first unseen selector and injected-judgment planner; not wired to gameplay | Headless30/30, fixture planner4/4, Playground13/13, Topic runtime33/33, catalogue57 families/161 scenarios pass; Unity EditMode149/150 with one unrelated headless-validator expectation failure; no Play Mode/provider proof | [[Unified Conversation Design]], [[Unified Conversation M1 Handoff]], [[../Verification/2026-10-16 Unified Conversation Foundation]] |
| F-07 | Needs, progression, stat view | Legacy store/tick/resolver plus nine-ID catalogue, validated snapshots, energy/engagement and known-only hydration adapter, bounded game-owned hydration checkpoint writer; no hydration ticking/UI | M3 source imported and hydration bridge wired; checkpoint persistence/runtime proof and full loop remain incomplete | [[../Verification/2026-10-16 Stats Foundation]], [[../Verification/2026-10-19 M3 Activity Source Check]], [[Stat Catalogue]], [[State and Effects]]; S0-07 |
| F-08 | Ambient speech | Existing idle/proximity path wired | Pending; focus policy not integrated | [[State and Effects]]; S0-09 |
| F-09 | Focus and initiative gate | Timer UI exists; shared interruption gate absent | Blocked | [[Design Decisions]]; S0-09 |
| F-10 | TTS and BYOK | P-key Canvas credential settings plus DPAPI/env store exist; saving credentials enables neither speech nor generation. Current speech source still defaults to Qwen; provider replacement is not proven implemented | BYOK compile/wiring documented; current visual, secure-storage/live API and speech/cache acceptance remain separate. Direct generated VN path is text-only; optional TTS is M4 | [[../Verification/2026-10-16 BYOK Setup]], [[Live Conversation Journal]]; `Assets/Scripts/TTS/TTSSystem.cs`, `Assets/AlterOS/Tts/`, `Assets/AlterOS/Security/`; S0-06/08 |
| F-11 | Conversation persistence | Private SQL schema-2 session/repetition/transcript store and one-time JSON/JSONL importers are implemented; Topic occurrence uses game schema 3. The startup journal gates session/archive facades; the transcript coordinator remains the sole writer | Persistence 15/15 and unified conversation 197/197; deferred provider tests cover imported source IDs, revoke/forget races and retained chat history; no Unity/player persistence acceptance | `Assets/Scripts/Interaction/ConversationSessionStore.cs`, [[Full SQLite Cutover Implementation Plan]]; Editor import/player acceptance |
| F-12 | Background adaptation | Private SQL generated archive stores metadata/payloads behind migration gates; background generator and ready-only personal overlay pipeline remain absent | Storage source exists; no generated publication/indexing/speech/playback acceptance follows from an archive row | [[Dialogue Catalogue#Generated content]], [[Full SQLite Cutover Implementation Plan]] |
| F-13 | Sentiment | Separate player evidence contract documented; no classifier, evidence runtime or Playground integration | Not implemented; hidden signals are not the nine Alterra stats | [[Player Context Evidence]], [[Design Decisions]] |
| F-14 | Cosmetic economy | Credit fields exist, but dialogue/provider effects cannot change credits; validated host economy operation and shop/ownership loop remain absent | Not verified | [[State and Effects]], [[Data Management Handoff]] |
| F-15 | Unity MCP | Package imported and live instance connected | Live scene and Console reads passed; not gameplay | [[SessionZero#MCP installation follow-up (2026-10-15)]] |
| F-16 | Runtime explanation tools | Proposed, not implemented | Not tested | [[Runtime Observability]] |
| F-17 | Topic Playground | Shared Editor/CLI session supports legacy GTE route/stat-context-v1 and isolated new-family Stage1 shared-anchor matching + Stage2 canonical scenario selection/history; detached Scenario preview and fixture CLI reuse the Scenario session. Model-file SHA-256 is streamed in a cancellable background worker, with cached status and exact-asset invalidation | Playground source 15/15; Unity script compilation 0 errors; focused fingerprint EditMode 1/1 (2026-10-22). Live GTE, visual/repaint profiling, and actual CLI/window report parity remain pending | [[Topic Runtime Playground Handoff]], [[Scenario Conversation Handoff]], [[Topic Playground Handoff]], [[../Verification/2026-10-22 Topic Playground Responsiveness]] |
| F-18 | Topic family/scenario runtime | Additive plain-C# family schema, compound/object gates, priority/weights, occurrence lifecycle and metadata repository; now consumed by the Playground adapter, not yet production gameplay/catalogue loader | Core runtime source checks33/33; Playground integration checks13/13; no new-family GTE/Editor/player proof | [[../Verification/2026-10-16 Topic Runtime]], [[Topic Runtime Catalogue Handoff]], [[Topic Runtime Playground Handoff]] |
| F-20 | Player memory M2.1 | SQLite repository/keyless protocol, provenance and scope policy, source checkpoint sync, bounded v3 memory-aware turns, consent/forget suppression, same-response structured proposals, authored onboarding data/session model; first-interaction and Canvas controls are not wired | Python129/129, C#106/106, synthetic lifecycle/callback and packaged keyless restart tests pass. No Unity compile/import/EditMode, Canvas, Play Mode, actual provider, or user test | [[Player Memory Design]], [[Player Memory Implementation Plan]], [[Player Memory Handoff]], [[../Verification/2026-10-19 Player Memory M2.1 Implementation]]; finish settings/question/proposal UI wiring and final acceptance |
| F-21 | Scenario orchestration | Strict schema/validation, lifecycle/questions/offers, permission-limited action port, authored adapter, detached Playground, provider-neutral v4 route through DirectConversationCoordinator, and the approved M3 movement/drink host bridge | Scenario67/67; Unified197/197. M3 hook compiles and room capabilities are editor-wired; current Unity focused EditMode remains unverified after review fixes. No live action, drawing capability, or player-visible acceptance | [[Scenario Conversation Design]], [[Scenario Conversation Implementation Plan]], [[Scenario Conversation Handoff]], [[M3 Furniture and Activities Handoff]]; human Splash->ModelTest->T check; preserve Furniture and Environment state |
| F-22 | Parallel Appetizer delivery | Head/main v4 stages feed the existing single transcript owner as they arrive; ready stages can display before terminal receipt, only after durable response receipt; submit-time clock, local fallback, stale-context fence, and player-advance acknowledgement integrated | Source regressions cover pending-terminal streaming, cancellation, durable receipt, deadline and failed-beat checkpoint. Actual first-visible frame measurement and provider quality remain unverified; latest Unity EditMode request timed out | [[Scenario Conversation Design]], [[Scenario Conversation Implementation Plan]], [[Scenario Conversation Handoff]]; measure during a separately approved human gameplay pass |
| F-23 | Ask Player / next-interaction offers / main closing | Bound Ask state, free-text permission, stable Canvas-template choice IDs, next-interaction offers, `Player.Interact` resume-before-greeting precedence, and main-close/wrap-up lifecycle wired in source | Scenario67/67 and Unified197/197; continuation resumes from first unacknowledged beat after player advance, and transcript retries are idempotent. No current visual/player interaction proof; return prompt remains status-only | [[Scenario Conversation Design]], [[Scenario Conversation Implementation Plan]], [[Scenario Conversation Handoff]]; human review of question/free-text/offers/reopen/return flow |
| F-24 | Reusable activities and furniture use points | M3 source implements exclusive Come here/Follow/Drink executions, NavMesh adapter, exact use-point claims, correlated Scenario result/wait projection, a narrow existing-bridge conversation hook, and three isolated fixtures; hydration uses a known-only persisted progression field. ModelTest has the approved dispenser, use point, host links, and assigned NavMesh asset | Stubbed source/Scenario harness48/48; focused Unity Activities EditMode51/51 including hydration fixture and serialization regression fixes; 2026-10-20 Unity compile has zero Console errors; current Editor queries resolve Alterra/Player/dispenser approach and a complete Alterra-to-dispenser path. Full Editor suite timed out; no Play Mode or player acceptance | [[M3 Furniture and Activities Design]], [[M3 Furniture and Activities Implementation Plan]], [[M3 Furniture and Activities Handoff]], [[../Verification/2026-10-20 M3 Blocker Fixes]], [[Room Testbox Handoff]]; verify live requests, T interaction pause/resume, Stop following, Drink checkpoints/persistence, and SplashScene→ModelTest |
| F-25 | Furniture catalogue and arrangement foundation | F1 catalogue/validator/registry/eight definitions; F2 seat/table resolver, safe choices and atomic point claims; F3 revisioned layout/save commands; F4 actor-bound Player interaction through the exclusive ActivityHost. Only “Ask Alterra to get water” is executable; no physical sitting or Player hydration | Historical F1-F4 source report was 52/52. Current combined Furniture source suite is 84/84; F4 still has no live player acceptance | [[Furniture Foundation Implementation Plan]], [[Content Workbench Shared Contract]], [[Furniture Mechanics and Catalogue Design]], [[../Verification/2026-10-21 Furniture Foundation F4 Source]]; use current F-26 input/acceptance status below |
| F-26 | Furniture inventory, placement and shared Debug Mode | Schema-2 transactional home inventory and home-save adapter; 10 additional initial items per starter definition; finite stock or shared F5 unlimited supply; T interaction, Q pickup/place, E inventory/held return and rotation controls; Editor-authored names/count panel, floor/window ghost support and diagnostics wired to ModelTest | Furniture84/84, persistence23/23, Scenario67/67, Unified197/197, focused Unity EditMode29/29. ModelTest is saved clean and scene validation found 0 issues. These are recorded pre-live results, not fresh checks. Physical T/E, Game View, Play Mode, restart and player acceptance remain open | [[Furniture Inventory and Debug Design]], [[Furniture Inventory and Placement Implementation Plan]], [[Debug Mode]], [[Multi-Agent Coordination]], [[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]]; user live acceptance is next |

M3 verification follow-up2026-10-20: F-24's earlier48/50 and constructor exception
are superseded by [[../Verification/2026-10-20 M3 Blocker Fixes]]: **51/51 focused
Unity Activities tests pass** after native path lifecycle and hydration fixture
fixes. Full Editor result timed out. The later M3 handoff update records saved
dispenser/NavMesh wiring and the compiled conversation hook; live action and physical
input acceptance remain unverified.

## Historical implementation checkpoints

Shared debug decision2026-10-21: [[Debug Mode]] owns F5, unlimited furniture
supply, Alterra overhead overlay visibility and the single-state-owner rule.
[[Furniture Inventory and Debug Design]] owns corrected T/Q/E/held controls.
Decision follow-up 2026-10-22: placed debug-created furniture remains after F5 is
off and returns as ordinary inventory stock; interrupted handling cancels to the
last committed state, with 10 initial stored items additional to existing room
pieces. [[Furniture Inventory and Placement Implementation Plan]] was approved and
dispatched as implementation START to the Furniture and Environment Session on 2026-10-22; dispatch alone
does not establish implementation, wiring or runtime acceptance.
At this I1 checkpoint, portable inventory/handling source was implemented and
`Check-FurnitureRuntime` passed 82/82, including the existing F1-F4 checks. Pure `DebugModeState` and
`FurnitureInputPolicy` are covered in that suite. The SQLite home adapter/schema-1
upgrade, F5 Input System owner, input-action remap, Canvas/prefab wiring, ModelTest
scene integration, Editor tests and runtime/player acceptance remain pending;
source tests do not establish those gates. See [[../Verification/2026-10-22 Furniture
Inventory I1 Source]].

Furniture/authoring foundation2026-10-21: [[Furniture Mechanics and Catalogue Design]]
consolidates placement-based combinations, optional table-dependent content,
available/locked/hidden choices and hover tooltips. F1/F2/F3 portable source is
implemented and source-checked; Unity integration, home-save adapter, presentation
and player behavior remain pending as tracked in [[Furniture Foundation Implementation Plan]].

Activity Scenario follow-up2026-10-21: [[../Verification/2026-10-21 Activity Scenario Followups]]
adds local Scenario ownership, contextual E, return walking and deferred closing.
Future independent speech is host-gated/disabled. Stubbed M3 56/56 and Scenario
source67/67; Unity result timed out, player acceptance pending.
[[Topic Scenario Activity Technical Reference]] owns the implementation map.

E interaction follow-up2026-10-20: [[../Verification/2026-10-20 E Interaction Menu and Activity Debug]]
supersedes flat root activity buttons with fixed entrances, Favor→Follow/Stop,
Together→input and three LLM-first/random-authored topic slots. Activity lifecycle
logs and above-Alterra Editor-authored debug Canvas are wired. Focused Unity63/63;
live visual/physical/log acceptance remains pending.

E-menu extension2026-10-20: [[../Verification/2026-10-20 Activity Greeting Choices]]
adds host-gated local Come here/Follow me/Drink water buttons with click-time
revalidation and no network call. Activities EditMode56/56; broader conversation
source checks188 passed/6 failed. No new visual/click/physical-action acceptance.

M3 design follow-up2026-10-19: [[M3 Furniture and Activities Design]] is the
approved design for reusable movement/furniture activities and authored Scenario
fixtures (Come here, Follow me, Drink water). [[M3 Furniture and Activities Implementation Plan]]
was human-approved and STARTed2026-10-19. On 2026-10-20 the user directed a
ModelTest room replacement while preserving Player and Altera. The room is a
neutral shell with the approved dispenser use point and assigned NavMeshData.
Earlier 63-triangle path evidence applies only to the superseded room; current
14-triangle shell evidence is in [[M3 Furniture and Activities Handoff]] and
[[Room Testbox Handoff]]. Live movement, furniture ownership, pose/stat effects
and player-facing Scenario behavior remain unverified.
Phone/history/optional TTS are now M4.
Furniture extension approved2026-10-19: typed functional points, automatic 3D
candidate discovery plus relationship-specific checks, safe overrides and
decorative-invalid placement; contract only, no implemented relationship resolver.

Conversation redesign follow-up2026-10-19: [[Scenario Conversation Design]] owns
the approved target and bounds for **F-21 Scenario orchestration**, **F-22
Parallel Appetizer delivery**, and **F-23 Ask Player / next-interaction Topic offers /
main closing**. Tasks 1–7 now have source implementation; production
presentation/capability wiring and final packaging remain open under
[[Scenario Conversation Implementation Plan]]. Existing F-06/F-18 player/runtime
evidence is not upgraded by these source results. Dessert is main-response
closing content, never another generation stage.

## Updating a feature

Keep its stable ID. Record the owner/source, dependencies, implementation and wiring separately, most recent check date, evidence type, failing behavior, and next acceptance criterion. A compile pass is not a gameplay pass. Large feature specifications should link out to their own document rather than expanding this table indefinitely.
