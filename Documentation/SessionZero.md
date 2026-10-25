# Session Zero: feature inventory and readiness

Audit: 2026-10-14. Tooling follow-up: 2026-10-15. Playground follow-up: 2026-10-16.
Project: `E:\Pleiades\alteros`, Unity `6000.4.4f1`.
Git has no commit yet; the project is an untracked initial snapshot.

Reconciled: 2026-10-22, from current source and handoffs only. Historical audit
findings and dated evidence below do not revoke later approvals. The current
source includes M2/M2.1/v4 conversation, bounded M3 activity hooks and SQL runtime
stores; it is not missing an Agent-mode switch. The release at
`Assets/StreamingAssets/Content/content.sqlite3` is present and passed read-only
integrity, foreign-key and trusted-release inspection on 2026-10-22; its manifest
snapshot matches authoring revision 63. Corrected focused Editor tests, normal startup,
and player acceptance remain open. SQL authoring and furniture inventory/placement remain
separate active work. Furniture I1-I5 implementation is in place with physics
fixtures and wall-opening support; physical input and player acceptance remain open.
See [[Verification/2026-10-22 Full SQLite Cutover]] for the superseding artifact check.
The earlier [[Verification/2026-10-22 Documentation Reconciliation]] records the
previous absent-artifact state and remains historical. Also see the current
[[Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

Input distinction: `Player.Interact` is now keyboard T, while E opens or returns
furniture according to the held-item state; physical key input is not yet verified.
Use current AGENTS.md for Editor
recovery, including authorized recovery of a verified hang; stale MCP alone is
not proof. This reconciliation performed no Editor/provider/player test; artifact
inspection was read-only.

## Assessment

Activity follow-ups2026-10-21: [[Verification/2026-10-21 Activity Scenario Followups]]
records local Scenario ownership, the then-current contextual-E binding,
drink→return→bark→next-interaction closing and a disabled-by-default
independent-conversation policy seam. Current input is T for `Player.Interact` and E
for inventory/held furniture. M3 stubbed56/56 and Scenario67/67; physical T/E and
player acceptance remain pending.

Historical E-menu/debug follow-up2026-10-20:
[[Verification/2026-10-20 E Interaction Menu and Activity Debug]] records fixed
greeting branches, prioritized/random topic slots, activity start/finish Console
notifications and an Editor-authored debug Canvas above Alterra. Unity focused
checks63/63 at that checkpoint; the later T/E remap is wired, but physical T/E,
Play Mode, log and visual acceptance remain open.

M3 blocker follow-up2026-10-20: [[Verification/2026-10-20 M3 Blocker Fixes]]
records lifecycle/test-fixture fixes and focused Unity Activities51/51. Full
Editor result timed out; no new Play Mode/startup/physical activity acceptance.

The authored and optional generated conversation paths have source/scene wiring.
Session Zero remains incomplete because current release readiness, stat/focus
coverage and runtime acceptance are unresolved, not because a Topic/Agent mode
switch needs implementing. The earlier Editor connectivity blocker was resolved
on 2026-10-15; see the follow-up below.
The original audit did not enter Play Mode, make provider calls, change packages,
save scenes, or implement fixes. A 2026-10-15 retry subsequently demonstrated
startup and an input-assisted authored Topic path; full runtime and audio
acceptance remain incomplete. See the retry evidence below.

Session Zero precedes milestone one. A disabled legacy Agent option is not the
current planner and proves neither unified conversation failure nor success.

Design update (2026-10-16): the previous Topic/Agent switch acceptance target
is superseded by [[Tracking/Unified Conversation Design]]. M1's deterministic
headless foundation is implemented; this does not change the historical
player-visible evidence above or prove an integrated conversation. See the
[[Tracking/Unified Conversation M1 Handoff]] for exact M1 checks and the current
EditMode limitation.

M2 implementation update (2026-10-17): the unified direct-conversation source
path is now wired in `ModelTest` behind a scene gate and an online toggle that
defaults off. The staged 57-family catalogue was promoted byte-for-byte and
validated; a hidden packaged Python/LangGraph/OpenRouter helper, strict bilingual
reply contract, transcript, coordinator, authored playback, and suggested-reply
binding are present. Unity compiled with zero C# errors and the focused M2
EditMode tests passed16/16 before two final review fixes. The current C# source
suite passes72/72, including regressions for subsequent-turn speaker
normalization, explicit cancellation status, and cancellation racing with
presentation. The full190-test EditMode
request left the Unity
process unresponsive before returning results; no Play Mode, visual check,
startup/E interaction, generated provider result, or paid call is claimed. See
[[Verification/2026-10-17 Unified Conversation M2]] and the current
[[Tracking/Unified Conversation M2 Handoff]].

M2.1 memory update (2026-10-19): the actual v3 turn path is now connected in
source, with post-checkpoint transcript synchronization, separate source IDs,
bounded local context, consent/suppression revalidation and no unsafe v2
downgrade. Raw transcript messages remain unclassified; unsafe memory proposals
are dropped without blocking a valid reply. Headless acceptance now passes
Python129/129 and C#106/106, plus the fresh packaged keyless restart smoke.
Bilingual first-meeting data and a pure onboarding session model exist, but no
first-E interception or memory/settings Canvas controls are wired. Proactive
personal questions and pending-proposal confirmation are not active. The Unity
read-only Console call timed out, so no Unity compile/EditMode/Play Mode or
player-facing claim is made. See [[Tracking/Player Memory Handoff]] and
[[Verification/2026-10-19 Player Memory M2.1 Implementation]].

Scenario Conversation implementation update (2026-10-20): Tasks 1–9 source and
package checks pass. The v4 Scenario path uses the existing direct conversation
coordinator/transcript owner; ready stages can be pumped as they arrive, before
the terminal receipt, after a durable response receipt is appended. Required-
memory turns stay gated until context revalidation. The five-second fallback
clock begins at submit entry, including synchronous capture. First-visible
transcript recording is separate from the player-advance checkpoint, so a
partly typed beat resumes instead of being skipped. Late failures do not reclaim
a dismissed conversation surface, and transcript retry IDs ignore attempt-time
differences. Checks: Scenario67/67, Unified194/194, Topic Playground13/13,
Python173/173 with packaged keyless v3/v4 smoke. The latest Unity EditMode request
timed out; the previous 7/7 result predates these fixes, so current Unity
compile/EditMode is unverified. No scene or Room Testbox asset was changed.

This remains source/package evidence, not current Unity or player-visible
acceptance. The latest Editor test request timed out; preserve its ModelTest
state and follow current AGENTS.md for verified-hang recovery. Earlier NavMeshPath
constructor and recursive Topic-condition serialization diagnostics were seen
on the earlier Editor run. Splash->ModelTest->E, visible answer/offer/return UI,
first-visible timing, provider quality, and TTS remain unverified; no provider/
TTS request was made. Return prompt currently surfaces only as a status. See
[[Tracking/Scenario Conversation Handoff]] and
[[Verification/2026-10-20 Scenario Conversation M2.1 Implementation]].

ModelTest room update (2026-10-20): at the user's direction, the previous
environment/furniture was removed and replaced with an 8 m x 8 m neutral shell.
Player, Altera, camera/light, dialogue/UI roots and startup settings remain.
That initial replacement temporarily removed furniture points/NavMeshData. Later
M3 wiring restored the dispenser, typed use point, motion/hydration references and
assigned rebuilt NavMesh data; see the M3 handoff for the latest snapshot.
The initial Unity Activity EditMode run was 48/50 with two hydration fixture
failures and a UnityActivityMotion NavMeshPath lifecycle exception. The main
session fixed both; focused Activities EditMode now passes 51/51, including a
prefab serialization regression. The full Editor suite timed out and the Unity
MCP registry at that checkpoint no longer listed AlterOS. Later recovery follows
current AGENTS.md; this historical outage is not a current restart prohibition.
Room navigation/furniture and conversation integration are assigned and have
source/Editor wiring evidence, but actual player activity execution remains
unverified. No Play Mode or Splash->ModelTest acceptance was run here. See
[[Tracking/Room Testbox Handoff]], [[Tracking/M3 Furniture and Activities Handoff]],
and [[Verification/2026-10-20 M3 Blocker Fixes]].

## Feature inventory

"Implemented" below means source/configuration evidence, not a Play Mode pass.

| Feature | Implementation and wiring | Remaining proof or gap |
| --- | --- | --- |
| Normal startup | Build Settings enable `SplashScene` then `ModelTest`; `SplashController` loads `ModelTest` | Launch from Splash and observe transition; opening ModelTest directly is insufficient |
| Companion avatar and presentation | VRM/UniGLTF `0.131.0` embedded packages, avatar, emotion, tracking, blink, and lip-sync code exist | Visual pose, expressions, lip-sync, and camera behavior not exercised |
| T interaction / E inventory | `Player.Interact` binds keyboard T and `FurnitureInputController` owns E inventory/held return; PlayerInput and raycast bridge are Editor-wired | Verify physical T/E, prompt, range/collider, greeting, inventory and control restoration in Play Mode |
| Authored/VN dialogue | `DialogueManager` and `DialogueRouter` run nodes, choices, presentation, HUD/camera/control state; required Topic input references are assigned | Repeated conversations, cancellation, branch endings, and visible choices not exercised |
| Free-text conversation | Greeting's `Let's Chat!` opens Canvas input. In saved `ModelTest`, the unified bridge is enabled; the online toggle defaults off and the path reuses warmed GTE, then selects an eligible authored Topic locally or uses the BYOK helper when explicitly enabled | Source/coordinator and promoted catalogue checks pass; player-visible input, offline authored reply and online result remain unverified because the full Editor run hung before a result |
| Local embedding runtime | Pinned GTE FP16 ONNX, tokenizer, metadata, vectors, startup host, warm inference, and ranking exist | Actual Unity warm-up/inference test not run in this audit |
| Generic dialogue content | 57 routes, 161 canonical variants; 678 anchors with 768-dimensional vectors | Routing accuracy and player-visible selection not newly benchmarked |
| Intent/Tone/Topic catalogue | Reviewed documentation exists under `Documentation/Semantics` | Runtime still uses a flat route index, not the approved catalogue/local-anchor architecture |
| Authored topic suggestions | `TopicOptionCatalog` is the sole owner and is attached to DialogueCanvas | Some prompt-based suggestions lead to unavailable background generation |
| Topic/Agent selection | The old mode enum/API remains as an unused compatibility seam; the current direct path has no Topic/Agent switch | Unified routing is implemented in source; no claim that every scene/legacy caller has migrated |
| Unified generated conversation | BYOK OpenRouter sidecar, one bounded LangGraph request, Pydantic validation, request/response binding, late-result handling and JP/EN VN mapping are implemented; online consent defaults off | Python42/42, C# coordinator/protocol source checks and focused Editor16/16; full Editor run hung and no generated text was displayed in Play Mode; no tool calls or paid request |
| Existing stats | Progression store contains energy, engagement, stimulation, attention, reassurance, comfort, affinity, trust, bond, player and environment values | These existing meters are not the complete approved needs/activity design |
| Stat display | HUD shell has mood, level, and credits references | Status-card affinity/hearts fields are unassigned; no full needs/stat panel |
| Passive stat simulation | `ProgressionTickSystem` and `StatMoodResolver` source exist | No ticker attachment in scene/prefab assets or runtime creation was found |
| Hunger/hydration and shared care | Approved design | M3 adds the known/unknown hydration field and bounded checkpoint path; Editor wired the hydration bridge into ModelTest, but checkpoint persistence/runtime acceptance remain pending; satiety and shared care remain absent |
| Stat effects | Host-approved validated content in a pinned SQL release and bounded activity checkpoints own permitted effects; public/provider/legacy starts remain untrusted | Source boundaries and receipt/checkpoint tests exist; generalized needs/economy simulation and live effects remain unverified |
| Ambient speech | Idle/proximity triggers are scene-wired and check dialogue locks | Focus/quiet/initiative policy is not integrated |
| Focus session | HUD includes a functional timer widget in source and serialized references | Running state stays private to the widget; it does not publish protected activity state |
| Tasks | HUD reads progression tasks and supports completion; local task APIs exist | Empty progression falls back to example tasks; no Agent task workflow |
| Cosmetic economy | Credits field, reward handling, and display exist | No complete shop, purchase validation, ownership, or cosmetic equip loop |
| TTS | Existing OpenRouter speech adapter remains; M2's validated direct VN path is explicitly text-only and skips synthesis/prefetch | TTS remains untested; M2 made no TTS request and does not add phone/history audio |
| Prebaked speech | 322 audio files exist | Only 2 of 6 greeting speech nodes match current cache keys; audio presence is not complete coverage |
| BYOK | P-key Canvas settings panel plus shared DPAPI/env store exist; [[Verification/2026-10-16 BYOK Setup]] records wiring/compilation | Visual/secure-storage/live acceptance remains separate; no key or profile was inspected by this reconciliation |
| Session/history persistence | Coordinator remains the sole transcript writer through SQL facade; private conversations store owns sessions, repetition, ambient state and transcript; legacy JSON/JSONL are retained import sources | Bounded import/ID parity is source/headless checked; current Unity restart/migration and phone-history UI acceptance remain open; local SQL is not encrypted at rest |
| Background adaptation | Generated archive metadata and prepared content scaffolding exist | No LLM worker, supported-memory updater, validation/publication pipeline, or active generated playback |
| Personalized routing | Approved per-user overlay design | No runtime overlay indexing/selection |
| Sentiment analysis | Approved supplementary model role | No classifier or judge integration; no model selected |
| Initiative/interludes | Approved player ceiling, focus priority, and consent design | No attention gate or suspend/resume activity controller |

## MCP installation follow-up (2026-10-15)

Installed `com.coplaydev.unity-mcp` from its pinned `v10.1.0` Git package. Unity
resolved the package and generated the lock entry. The existing Codex stdio
server was already pinned to `10.1.0`; its configuration was left unchanged.

Live MCP verification confirmed:

- Instance `alteros@48a268a4`, project root `E:/Pleiades/alteros`, port `6400`.
- Active `Assets/Scenes/SplashScene.unity`, build index 0, clean and loaded;
  hierarchy inspection succeeded.
- Compilation/import idle and Play Mode off, using a read-only CodeDom probe.
- Console error query returned zero entries; the Console was not cleared.

The automatic dynamic-compiler probe disconnected once; explicit `codedom`
worked. Idle cached state can report an old snapshot despite successful live
queries. No Play Mode, gameplay test, scene save, or paid provider call was made.
Session Zero's player-visible acceptance checks remain pending.

## Slice 0A runtime attempt (2026-10-15)

Scope: normal Splash startup, then E interaction and authored Topic playback.
Result: blocked before reaching ModelTest; no dialogue acceptance was exercised.

- Pinned live instance `alteros@48a268a4`; project root and Unity version matched.
  SplashScene was clean, compilation/import idle, target StandaloneWindows64.
  Git still had an untracked initial project; no code changes preceded the run.
- Baseline Console error query returned zero entries. Existing obsolete
  object-finder warnings were retained; the Console was not cleared.
- Entered Play Mode from Splash through MCP. Initial runtime queries encountered
  reload/timeouts. The Editor log subsequently reported GTE ready on GPUCompute
  with 678 anchors and 57 routes. This proves initialization completed once,
  not acceptable warm-up latency; no precise duration was measured.
- Live probes then showed SplashScene, frame 8, game time about 0.14 seconds,
  Play Mode on, pause off, time scale 1. A later bounded player-loop request
  produced frame 9 / about 0.16 seconds, then repeated probes saw no advance.
  `Application.runInBackground` was false; GPU was NVIDIA GeForce GTX 1650.
- Windows capture did not expose a usable Editor surface, and activation failed
  with `GetCursorPos failed: Access is denied. (0x80070005)`. Desktop lock/session
  visibility is not confirmed. No further desktop input was attempted.
- MCP captured `Captures/SessionZero/0a-splash-observed.png`, showing the early
  Splash surface. This is an engine screenshot, not proof of an accessible
  physical Game View or functioning E input.
- Post-start Console contained native `JobTempAlloc` lifetime exceptions and
  allocation-deletion errors, with a retained-panel repaint stack on the latter.
  These were newly observed during this run, not introduced by a code edit.
  Their relationship to warm-up and frame starvation remains unproven.
- Exited Play Mode through MCP. No scene save, package change, production-code
  edit, credential write, or provider request was performed.

Next: make the Windows desktop accessible and the Unity Game View visible,
repeat from Splash, and compare frame progress and Console output. Investigate
remaining startup/native-allocation failures before changing dialogue code.
Do not bypass Splash or force scene loading and count it as normal-startup proof.

## Slice 0A retry - input-assisted Topic proof (2026-10-15)

Result: partial runtime proof, not a complete slice pass. No production code or
scene/prefab changes were made. Play Mode was stopped after the test.

### Observed path

1. Started from clean SplashScene using the same pinned live MCP instance.
   The retained baseline included allocation errors from the prior attempt;
   the Console was not manually cleared.
2. Unity became unresponsive during GTE startup again. After warm-up reported
   Ready on GPUCompute, the real Splash controller transitioned to ModelTest.
   A live probe showed frame 751 / game time about 7.71 seconds and later probes
   continued advancing. This was not a direct ModelTest load. Warm-up wall time
   was not precisely instrumented and remains a responsiveness problem.
3. Desktop capture showed the companion/room/HUD. Maximized the floating Game
   window for inspection. Native input automation activated the main Editor and
   lost Game View focus; an unsuccessful mouse drag and E press are not proof
   that physical mouse/keyboard controls work or fail in ordinary player use.
4. Applied a temporary runtime-only player yaw toward Alterra (no scene save).
   The normal raycast found Altera's collider about 0.99 m away; the interaction
   component reported Altera as its current target. Visual E-prompt acceptance
   remains unverified.
5. Focused Game View and queued E via Unity Input System, then queued release.
   This exercised the existing E binding -> PlayerInput -> OnInteract -> greeting
   path, rather than invoking Interact directly. The greeting and camera change
   were visibly captured; player-control lock was active.
6. Queued Space through Input System; greeting menu text and choices appeared.
   `Let's Chat!` existed as an active button but was outside the initially
   visible portion of the world-choice list. Its scrolling/accessibility path
   was not tested.
7. Invoked that existing button's onClick callback, assigned synthetic text
   `ngl im kinda tired` to the actual Topic field, and invoked the submit
   button callback. This is UI-callback-assisted testing, not OS typing proof.
8. The running local semantic path selected `player_tired`; node
   `player_tired_001_01` visibly presented "You are tired. Mm, I heard that
   properly." A queued advance reached node 2, whose authored choices are
   `Rest first.` and `Tiny step, then rest.` Natural completion was not tested.

### Limitations and failures

- Missing Animator parameters were logged from AlteraEmotionController, including
  startup and dialogue expression cues. Examples: hashes -1541085712,
  -1091730487, and 1630420858. Source/Animator contract needs investigation;
  no animation fix was attempted.
- HUD task labels overlap; placeholder buttons/white blocks and inconsistent
  Altera/alterra speaker labels remain visible. These are observed presentation
  defects, not repaired in this pass.
- MCP became intermittently slow, timed out, and disconnected once during later
  probes. A subsequent scene query succeeded, and MCP stop exited Play Mode.
- TTS was unconfigured and the UI showed TTS Off. Logs included cached-only
  acknowledgement playback; this does not prove voice quality, real synthesis,
  cache reuse, or a strictly silent TTS-Off contract. No provider call was made.
- Unknown input, time-ineligible routing, cancellation/control restoration,
  full branching completion, and repeated re-entry were not exercised. The
  previously documented eligibility-success bug remains unresolved.

Evidence screenshots (repository-relative, outside the Obsidian vault):

- `Captures/SessionZero/0a-retry-e-greeting.png`
- `Captures/SessionZero/0a-retry-greeting-menu.png`
- `Captures/SessionZero/0a-retry-tired-topic.png`

Next: address startup responsiveness and Animator contract failures with focused
reproductions; validate actual player input and visible choice access; finish
fallback, eligibility, cancellation, and completion checks. Do not mark 0A or
Session Zero complete from this successful single known-input route.

## Startup and expression corrections (2026-10-15)

See [[Verification/2026-10-15 Startup and Expressions]] for cause, changed
behavior, red/green regressions, test job references, runtime screenshot, and
remaining limitations. Missing body-trigger commands are corrected and the
normal greeting/menu recheck had zero Console errors. GTE initialization now
yields through loading and warm-up and releases its worker on failure. The final
one-layer PlayMode run passed all 11 tests but still observed a 3.737-second
startup frame; responsiveness remains an acceptance gap, not a completed polish
claim. The eight Editor regression cases also passed again after the final tune.

## Original tool and evidence baseline (2026-10-14)

| Check | Result | Scope limitation |
| --- | --- | --- |
| Unity installation/project process | Editor process was open on `E:\Pleiades\alteros` | Open process does not prove active scene or Editor readiness |
| Unity MCP | Server/tool namespace exists; instances resource returned zero sessions | No bridge was detected in project packages; no live Console, scene query, or Game View automation available |
| Compilation artifacts | Runtime, test, and bridge DLLs generated on 2026-10-14 after audited source timestamps | Not a fresh build or live Console inspection |
| Current Editor log | Zero `error CS...` matches; compile/deprecation warnings and domain reload evidence exist | Log snapshot is not proof of an error-free runtime Console |
| Scene script metadata | 7 unique Splash script GUIDs and 53 ModelTest script GUIDs resolve across Assets, PackageCache, and embedded Packages | Serialized GUID resolution does not prove prefab import or visible behavior |
| ModelTest local references | Zero dangling internal fileID references after including stripped prefab records | Optional null fields and stale serialized fields still need lifecycle/UI inspection |
| Dialogue JSON parse | 377 files parsed; zero syntax errors | Does not validate every legacy content rule |
| Canonical structure | 161 variants checked for schema/source/route folder, examples, node IDs, and branch targets; zero errors | No end-to-end playback test |
| GTE index | 678 x 768 x 4 = 2,082,816 bytes, matching vector file; all indexed routes have authored content | No new inference/latency measurement |
| Existing pure test methods | 10 passed, 0 failed: mode policy, authored catalog, credential precedence, speech payload | Invoked from current compiled test DLL using a temporary Mono reflection probe; not Unity Test Runner execution |
| Semantic Lab CLI | `uv run --no-sync --offline --project Tools/SemanticLab python Tools/SemanticLab/semantic_lab.py --self-test` passed | Scoring/evaluation-fixture self-check only; no model download/inference or GUI test |
| Standalone dialogue check | Compilation of `Tools/DialogueCoreCheck.cs` failed with obsolete `DialogueNode`/`DialogueChoice` types | Failure is in this helper outside Assets, not a demonstrated Unity game compilation failure |
| Unity GTE PlayMode test | `StartupPrewarmsAndRoutes` exists | Not run; no startup/input/camera/audio smoke test covers the complete Session Zero |

No credentials were printed or sent. The absent developer-shell environment key
does not establish what the already-running Editor inherited. The expected
encrypted key file was checked for existence only. Temporary diagnostic files
were removed; source, scenes, settings, and packages were preserved.

## Original priority findings (2026-10-14 baseline)

Historical findings below explain the audit. SZ-01's missing mode-selection work
is superseded by the unified M2/v4 source path; do not create a switch from it.
The latest handoffs and current checklist own remaining implementation/acceptance.

### SZ-01 - Live Agent and selection unavailable

Severity: High. Confidence: Confirmed source/configuration.

`DialogueManager.AgentModeAvailable` is false; `TrySetConversationMode` has no
UI callers. `AlterOSHudShell.navButtons` is empty and Chat/Assistant buttons
have the same tab value. `SelectTab` does not dispatch conversation modes.

Impact: Session Zero cannot prove either requested mode using the player UI.
Next: implement the minimum real Agent turn path and explicit selection through
the Editor. Validate no-key/unavailable behavior and real BYOK conversation.
Size: Medium/Large. This audit does not implement it.

### SZ-02 - Full stat inspection and ticking incomplete

Severity: Medium. Confidence: Confirmed serialized/source coverage; runtime
activity remains unobserved.

`AlteraProgressionStore` is attached, but the HUD only exposes limited state.
`AlterOSStatusCardView` has unassigned mood/level/affinity fields and no heart
references; HUD shell supplies mood/level separately. No serialized or runtime
creation path for `ProgressionTickSystem` was found.

Impact: a stat store is not proof that players can inspect all stats or that
needs update over time. Next: provide auditable stat inspection, explicitly
compose the intended tick owner, and test changes/save-reload. Size: Small/Medium.

### SZ-03 - A rejected semantic route can leave input busy

Severity: High. Confidence: Confirmed control-flow gap; symptom not reproduced.

The original audit found that `TryPlayAuthoredRoute` returned true even when
`PlayCinematic` rejected time eligibility. The semantic caller then skipped
fallback. Six first catalog variants have time windows, and selection only
considered the first stable filename.

Impact: potential stuck "Listening..."/dialogue control state for an out-of-time
route. The 2026-10-15 cleanup now reports actual playback success and tries
sorted variants until one is eligible, otherwise permitting fallback. Rejection
and input cancellation regression tests reproduced the old failure and passed
after the changes. See [[Verification/2026-10-15 Topic Recovery and Canvas]] for
runtime evidence and remaining checks.

### SZ-04 - Test helper is stale

Severity: Medium. Confidence: Confirmed compiler failure.

`Tools/DialogueCoreCheck.cs` still uses names replaced by
`CanonicalDialogueNode` and `CanonicalDialogueChoice`. The isolated compiler
probe exited 1. Next: update this helper or replace it with the maintained
dialogue-core test route, then run its original assertions. Size: Small.

### SZ-05 - Conversation recording is disconnected

Severity: Medium. Confidence: Confirmed caller search.

`ConversationSessionStore.RecordTurn`, `RecordDialogueSequence`, and session
start/end APIs have no external callers in the audited code. Ambient memory and
repetition tracking exist, but are not a transcript of Topic conversations.

Impact: persistence cannot yet supply the real interaction history required by
adaptation or mode continuity. Next: record actual submitted/presented turns,
not unvisited graph branches, and validate mode transitions/reload. Size: Medium.

### SZ-06 - Voice and packaged cache readiness incomplete

Severity: Medium. Confidence: Confirmed source/file evidence; provider unknown.

Only two greeting-node keys match prebaked audio. There is no BYOK settings
surface, and generated cache writes target StreamingAssets. Foreground and
prefetch synthesis do not share an in-flight request.

Impact: incomplete voice coverage, possible duplicate paid synthesis, and a
packaged-build path limitation. Next: expose credential setup, validate one
real speech/cache hit and failure fallback, and move generated cache storage
behind the approved speech owner. Size: Medium.

## Session Zero acceptance checklist

| ID | Acceptance criterion | Audit status |
| --- | --- | --- |
| S0-01 | Launch Splash, reach ModelTest, and see a usable companion/player scene | Normal transition/room/avatar rechecked after startup changes; final one-layer warm-up has a measured 3.737 s hitch; UI/input acceptance still incomplete |
| S0-02 | Look at Alterra, see the current interaction prompt, interact and finish a greeting | Current wiring is T interaction and E inventory/held furniture; physical keys, prompt, full greeting and control restoration remain unverified in the live game |
| S0-03 | Submit a known Topic phrase; play an eligible authored route | Prior UI-callback proof: tired slang -> `player_tired` nodes 1/2. Shared confidence/time/variant rules and authored traversal now have first-party Editor coverage (36/36 suite passed); final-version live GTE query, natural interaction and full branch completion remain unverified. Eligibility gap SZ-03 is closed in shared source selection, not yet re-proved in a fresh player run. |
| S0-04 | Unknown/uncertain input uses fallback; cancel/end restores controls | Assisted fallback (temporary 0.99 threshold), missing-fallback recovery, Cancel and recognized Dynamic-update Escape cleared routing/input locks; physical input and naturally timed full branch completion remain player checks; see [[Verification/2026-10-15 Topic Recovery and Canvas]] |
| S0-05 | Explicitly opt into online conversation and receive valid VN beats without a Topic/Agent switch | Source/scene path exists; current release/startup/provider/player acceptance remains pending |
| S0-06 | Missing key/provider failure is explicit and preserves a usable Topic path | Not run; no-key seams exist |
| S0-07 | Inspect authoritative existing stats, observe intended changes, and reload persisted state | Blocked for full inspection/ticking; not run |
| S0-08 | Play cached speech, request one uncached clip, reuse cache, and recover from speech failure | Not run; partial cached coverage and no provider proof |
| S0-09 | Focus/quiet mode suppresses optional ambient/interlude takeover while Agent help continues | Blocked; approved policy is not integrated |
| S0-10 | Record actual request/response/presentation lifecycle and preserve continuity across channels/activities | SQL transcript/import and coordinator source exist; current Unity lifecycle/restart/forget acceptance remains pending; phone UI is M4 |
| S0-11 | No blocking runtime Console errors throughout the normal startup scenario | Bounded startup/greeting/menu recheck had 0 errors after trigger fix; full scenario incomplete, earlier native allocation fault not separately closed |

Keep Topic/cached-or-text validation and Agent validation as distinct steps, but
the full Session Zero is not complete until the required Agent path is real.
Sentiment and generated-topic publication are inventoried here; they must not
be presented as functioning features. The focus gate must remain usable without
the optional sentiment inference.

## Milestone one - recorded, after Session Zero

Approved target:

1. Talk freely and start a study session through a permitted game action.
2. Offer/accept a shared water break under the initiative/attention rules.
3. Apply fixed game-owned activity effects once, and record a supported preference.
4. Prepare one validated personalized Topic scene during low activity.
5. Encounter it later through Topic routing with speech under the chosen policy.

Accepted starting behavior: stable authored character identity; initial study
and water-break activities; explicit initial meters/time-away rules; sourced,
correctable memories with dependent-content invalidation; short personalized
scenes using canonical topics; bounded speech wait/text fallback; bounded
foreground/background work. Numerical balancing, model/framework choice, and
cosmetic-shop details remain implementation/evaluation decisions.

## Recommended order

1. Coordinate active SQL-authoring and furniture workers; current root TODO owns assignments.
2. Rerun focused corrected tests through a responsive Editor; verify startup consumes the installed SQL release.
3. Establish a detached whole-startup profile before agent-run Splash -> ModelTest acceptance; do not touch the real profile to bypass this gap.
4. Recheck unified dialogue, storage readiness, cancellation/continuation and bounded activities; do not reimplement M2/BYOK or add a mode switch.
5. Complete outstanding memory/stat/focus UI and later M4 phone/TTS acceptance under separate scopes and explicit provider-spend approval.
6. Begin further study/adaptation work after its prerequisites are actually accepted, not simply compiled.

## Temporary readiness-gated Splash (2026-10-15)

Location/replacement index: [[Tracking/Startup and Loading]]. The existing
Splash now waits for semantic `Ready` before submitting the ModelTest load;
the branding hold is no longer the readiness condition. Stage text and a
failed-only Retry button are wired in the Splash scene through the Editor.
No new packages or model/provider calls were added.

Bounded live checks:

- Launched from Splash, observed `Loading` with visible preparing text, then
  ModelTest with `Ready`, 678 anchors and 57 routes; Console error query was empty.
- Installed a synthetic failed state on the persistent runtime without changing
  model assets, returned to Splash, and observed the failure text and Retry button.
- Invoked the actual button's click callback through MCP (not a physical click).
  State immediately became Loading, the failure cleared, a second retry was
  rejected, and only one runtime host existed. It subsequently reached Ready
  and automatically returned to ModelTest.
- In a separate synthetic failure hold, disabled the waiting Splash controller,
  then restored runtime Ready. The scene remained Splash after readiness,
  confirming no future load request from that disabled controller. This does
  not cancel an already-submitted asynchronous scene load.

Captures under `Captures/SessionZero/`: `0a-loading-splash.png`,
`0a-loading-failure-retry.png`, and `0a-loading-ready-modeltest-4.png`.
A later retry warm-up capture timed out; it is not counted as visual proof.
Automation temporarily enabled runtime `Application.runInBackground` because
the unfocused Editor stopped at frame 2; it was restored before leaving Play
Mode. No Player Settings change was made. One normal initialization still
logged a 4.352-second warm-up step: this is a loading wrapper, not a hitch fix
or a controlled cold-start benchmark. Full slice 0A remains incomplete.

Regression evidence: initial loading-gate/retry tests failed against the old
controller/runtime, then all 14 Editor tests passed (`e7784883e6f343968121156f41c1fef7`).
After review, inactive-host tests were named explicitly and extended to Failed;
the final seven loading tests passed (`a5d8158cd4d740eaa878672b01271fa4`). An active
runtime retry regression was added to cover guards without the inactive-host
confounder. The first expanded Play Mode run (`b169c7fd662945e0895b9fcd3258a071`)
passed retry recovery but failed the subsequent existing route test with
`Cannot access the data as it is not available` in GPU readback.

Installed Inference Engine 2.6.1 source shows that `ReadbackRequest` retains a
pending download until consumed. Warm-up waited for completion but never
consumed it, allowing pooled GPU tensor data to retain an expired request.
The initializer now clones/disposes the warm-up readback before reporting Ready.
This is a lifecycle correction, not an inference-speed optimization.

After that correction, the full first-party Play Mode suite passed 12/12
(`559cf4c6724440c9b7342656747ac238`, 32.099 seconds reported test duration), including
active retry recovery and the subsequent existing route test. No standalone
build or physical retry click was tested. Unity was left stopped on clean Splash.
