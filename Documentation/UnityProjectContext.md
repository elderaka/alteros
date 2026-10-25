# AlterOS Unity Project Context

Last reviewed: 2026-10-22

Actual UI correction2026-10-25: correct semantic selection did not imply playback.
The bridge rejected equivalent pinned Flow refs when the compiled entry omitted
optional expectedHash. Both refs now resolve through RuntimeContentStore before
comparison; startup failure returns to retry rather than silently succeeding.
Offline authored success no longer displays a generic error; cancelling retry
closes a stranded exchange. Furniture controls are deferred to LateUpdate for
current EventSystem pointer state. Import and gameplay evidence are tracked in
the latest reusable-content ledger; no new gameplay pass is claimed here.

Latest routing evidence2026-10-25 18:21: fallback is imported and live
GteSemanticRuntime reports CPU/Ready. The single four-sentence live-encoder /
installed-SQL candidate/eligibility/planner/pinned-flow check passed4/4,60routes /
60Topics. See execution ledger for full persisted report. No dialogue presentation,
movement, item effects or full startup exercised; NavMesh/input gaps remain.
This supersedes the pending-import checkpoint immediately below.

Routing continuation2026-10-25: after the SQL route/catalogue filters were fixed,
live Unity GTE still returned768 allzero values on its active GPUCompute worker
(Direct3D12); valid tokenization/SQL vectors, same model on CPU selects Locate TV
at1.000409. GteSemanticQueryEncoder now validates vectors and falls back from invalid
GPU output to the existing CPU backend; runtime/Playground and warm-up share this
policy. Source compile passed, but actual import/runtime fallback remains pending
because the existing ModelTest PlayMode session is preserved. No gameplay pass;
missing NavMesh and pointer-over-UI warnings remain separate recorded gaps.

Reusable-content implementation update2026-10-24: shared authoring r90 selects464
documents; the installed pinned content release includes1,368x768 local GTE vectors,
three authored proof scenarios and compiled Furniture global settings/definitions.
The standalone six-kind SQLite Workbench/normal host supports editing/settings and
registered Action sequence composition/preview. ModelTest has the sequence port,
dispenser and independent basic/variant TVs; their identities now reference persistent
FurniturePlacedInstanceIdentity script GUID8b5504fdb4cf573478c993938d33d32e after
Editor save/reopen. Final focused identity regression1/1 and scene validation0issues;
Editor ready, filtered Console clean. This supersedes older r63 artifact absence/
pending-wiring claims, not live player acceptance. See the reusable-content execution
ledger for exact installed pins, backups, remaining browser visuals and live checks.

Full SQLite cutover follow-up 2026-10-22: SQLite-net 1.3.2 remains pinned to
`08248bd5884d8eb932a837aa56d4ff456daf913f`. The shared game database at
`<persistentDataPath>/Data/alteros-game.sqlite3` owns progression, home layouts and
schema-3 Topic history. `<persistentDataPath>/Data/conversations.sqlite3` owns
sessions, repetition, ambient state and transcript events; `personal-content.sqlite3`
owns generated archive metadata/payloads. A separate migration journal records
per-domain readiness and gates session/archive facades. Python memory and DPAPI
credentials remain separate owners. Persistence passes 15 synthetic checks. The full
439-source SQL artifact is installed at `Assets/StreamingAssets/Content/content.sqlite3`;
on 2026-10-22 it passed read-only SQLite integrity, foreign-key and trusted-release
inspection, and its authoring snapshot hash matched current revision 63. Unity source
import/script compilation passed on 2026-10-22. The focused persistence fixture
correction and deferred conversation test correction await focused Unity rechecks. The
Editor was force-recovered with user approval; that recovery does not prove corrected
tests pass. Startup/player acceptance and real-profile migration were not run.
See [[Tracking/Full SQLite Cutover Implementation Plan]],
[[Tracking/Content Data Integration Handoff]], and
[[Verification/2026-10-22 Full SQLite Cutover]].

Separate authoring cutover checkpoint 2026-10-22: `Assets/AlterOS/Authoring/`
provides authoring schema v1, revision/CAS storage, explicit JSON import/export,
online backup and pinned-snapshot compilation. The Workbench draft and authoring
commands now use that SQLite repository; `ContentBuildProcessor` reads the configured
`Tools/ContentWorkbench/Workspace/authoring.sqlite3` and its explicit `default` build
set. The local store is initialized at revision 63 with 439 imported Dialogue sources
and 439 selected documents. Its deterministic review export is
`Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`.
The host compiles against the pinned SQLite source/native library and has no
UnityEngine/UnityEditor assembly references. Workbench/repository/content checks pass
headlessly; Unity source import/compilation has passed and the installed release
matches the current authoring snapshot. Compiled content schema remains v4. Typed
Typed Workbench CRUD commands exist. The approved human/agent editing direction is
a Flow-first graph with selected-step writing inspector and shared typed commands;
graphical W2-W3, preview/parity W4 and acceptance W5 remain open in the existing plan.
See [[Tracking/Content Workbench Shared Contract]] and
[[Tracking/Content Workbench Implementation Plan]]. Also see
[[Tracking/SQLite Authoring Cutover Implementation Plan]] and
[[Verification/2026-10-22 SQLite Authoring Core]].

This is the current implementation snapshot. `Documentation/Architecture.md`
describes the product direction and target boundaries; this document records
what is actually present in the Unity repository today.

The [Session Zero inventory](SessionZero.md) is the current feature/readiness
matrix. Its 2026-10-14 audit inspected source, serialized references, content,
logs/artifacts, and diagnostic tests; it did not enter Play Mode. M2 direct
conversation is now source-integrated, but player-visible acceptance remains
incomplete (the full Editor test request stalled; no Play Mode was run). A
2026-10-19 read-only Console request also timed out; this follow-up made no
Editor/player-facing claim.

## Project identity

Activity integration follow-up2026-10-21: `DirectConversationBridge` is now a
partial class; its `ActivityScenarios` file binds host-authored wrappers under
`StreamingAssets/Dialogue/topic/activity_scenarios.json` to the existing Scenario
runner and activity host. No scene/startup/package change. See
[[Tracking/Topic Scenario Activity Technical Reference]] and the linked verification
for current evidence and limitations.

- Unity `6000.4.4f1` (`360f97ecca93`)
- Built-in Render Pipeline: no URP/HDRP package is declared and
  `ProjectSettings/GraphicsSettings.asset` has no custom render pipeline asset
- Input System package `1.19.0`; project input handler is set to the new Input
  System
- No networking package or agent-framework package is declared
- No repository commit exists yet; this checkout is an uncommitted initial
  project snapshot

## Startup and scenes

Enabled Build Settings scenes, in order:

1. `Assets/Scenes/SplashScene.unity`
2. `Assets/Scenes/ModelTest.unity`

The normal startup path is therefore `SplashScene -> ModelTest`. `ModelTest`
contains the current companion interaction surface, dialogue bridge, semantic
runtime consumers, generated-dialogue archive, and TTS references. There is no
separate title, login, or chat scene in the current Build Settings.

Splash is now a temporary readiness-gated loading surface, not a fixed-duration
transition. It displays GTE stages and offers failed-initialization retry;
ModelTest loads only after GTE is ready. The exact scene, hierarchy, controller,
model directory, and replacement contract are indexed in
[[Tracking/Startup and Loading]]. This is not the final welcome-screen design
and does not guarantee hitch-free model initialization.

## Runtime boundaries

### `Assets/AlterOS/AlterOS.Runtime`

The `AlterOS.Runtime` assembly is the new plain-C#/provider-neutral layer. It
contains:

- canonical dialogue definitions, validation, playback, and authored route
  catalog loading
- GTE semantic runtime and route-index ranking
- OpenRouter TTS request construction
- Windows credential storage through native DPAPI

It references Unity Inference Engine, its tokenizer package, and the installed
`Gilzoide.SqliteNet` assembly. Content JSON uses the already-installed Unity Newtonsoft
package through `ContentJson`; no new package or framework DLL was added. The assembly
is not the entire game runtime yet.

The same assembly now contains the game-owned persistence contracts, SQLite
repository/schema, migration importer, checkpoint/recovery services, and furniture
home-save adapter under `Assets/AlterOS/Persistence/`. Unity source import and script
compilation passed on 2026-10-22; focused Editor tests and staged content import remain
open.

The plain serializable conversation DTOs now live in
`Assets/AlterOS/Conversation/ConversationModels.cs` under the runtime assembly. Their
namespace, serialized field names, enum values and original `.meta` GUID are preserved;
scene-wired `ConversationSessionStore` and bridge code remain in `Assembly-CSharp`.
`TopicHistory.StorageRevision` is readable across this boundary while SQL remains its
writer. The Editor content build processor reads preset names through `SerializedObject`
instead of referencing the scene-side `AlteraExpressionPreset` class.

### `Assembly-CSharp` under `Assets/Scripts/` and `Assets/Altera/`

The current Unity bridge remains in the default assembly. Important owners are:

- `AlteraProgressionStore` plus the progression tick/effect bridges: SQLite-backed
  progression initialization, commit-before-publish discrete changes, and dirty
  continuous snapshots. Schema-4 snapshots are checked before normalization.
  Dialogue effects require a Generic route selected from the pinned SQL content
  release and validated against the canonical schema, plus the host's bounded
  allowlist. Public/provider and legacy sequence entry points reject effects.
  Unity source import/compilation is verified; focused Editor tests and live acceptance
  remain open.
- `DialogueManager`: dialogue UI/presentation, sequencing, choices, TTS cache
  lookup, TTS prefetch, and wait/fallback behavior
- `DialogueRouter`: loads authored documents from the pinned SQL release, validates
  canonical routes, and sends cinematic or ambient content to the corresponding presenter
- `TTSSystem`: audio playback plus the OpenRouter provider seam and BYOK API
- `AlteraAmbientBubble`: ambient speech presentation, delegating provider access
  back through `DialogueManager`
- `ConversationSessionStore`: SQL-backed sessions, bounded turns, repetition and
  ambient state under `Data/conversations.sqlite3`; old session JSON is import-only
- `GeneratedDialogueArchive`: private SQL-backed lifecycle metadata and retained
  payloads under `Data/personal-content.sqlite3`; it does not generate dialogue or TTS
- `DirectConversationBridge`: M2 direct text dispatch, staged/promoted Topic
  catalog and coverage loading, GTE-match adaptation, host context revalidation,
  authored playback evidence, opt-in sidecar generation, and M2.1 local memory
  client/source checkpoint synchronization. Online toggle defaults off. The
  negotiated v4 Scenario RAG route now feeds the existing DialogueManager
  sequence/choice/text-entry templates; actual TTS behavior/quality remains
  unverified. First-E memory onboarding and Canvas memory controls remain
   unwired. In Editor, the opt-in helper resolver uses only
   `Build/CompanionRuntimeV4/AlterOSCompanion/`; it does not fall back to
   historical acceptance builds.
- `DirectConversationCoordinator` remains the only owner/writer for M2 direct
  transcript events and v4 stage admission. `ScenarioTurnCoordinator` handles
  stage ordering, submit-time fallback, context fences, and acknowledgement;
  the bridge pumps queued stages from Unity `Update`. Ask choices and session-
  local next-interaction offers are identity-bound. Drawing remains unsupported.
  The later M3 bridge captures actual ActivityHost capabilities/objects for bounded
  movement and Drink; synthetic Playground activities do not grant live permission.
- `ConversationTranscript`: append-only SQL rows in the private conversations
  database; `DirectConversationCoordinator` remains the only production writer.
  The old JSONL is one-time import evidence. It is not encrypted at rest or a memory
  retrieval service. Player transcript messages sync as unclassified evidence;
  the model cannot assert them as real-world profile facts without a separate
  authoritative scope/confirmation path.

Session Zero wiring gaps confirmed by source/reference inspection:

- `AgentModeAvailable` remains false and `TrySetConversationMode` has no callers;
  this is a superseded compatibility seam, not the current route selector.
- `AlterOSHudShell.navButtons` is empty; Chat and Assistant tabs do not switch
  conversation modes.
- Progression storage exists, but no attachment/runtime creation of the passive
  tick component was found. HUD mood/level/credits are not a full stat view.
- Legacy session turn-recording APIs remain read-only for the M2 path. The new
  direct coordinator writes requests, validated responses and acknowledged
  nodes through `ConversationTranscript` only.
- The time-ineligible route's assumed-success gap was corrected on 2026-10-15:
  the router tries sorted variants and returns actual playback success. Current
  verification is recorded in [[Verification/2026-10-15 Topic Recovery and Canvas]].

Static HUD, input and choice layouts are authored under the scene Canvas objects
and in UI prefabs, not runtime HUD builders. Dynamic task rows instantiate the
authored task template into a scrollable layout. The 2026-10-15 cleanup note
indexes the exact hierarchy and template locations. Unwired placeholder room
navigation is hidden rather than advertised as a functioning mode selector.

This is a transitional boundary. New systems should land in focused
`Assets/AlterOS/` services where possible; do not mass-migrate the existing
scene-wired bridge as unrelated work.

## Game and private persistence (source implementation, 2026-10-22)

- Database: `<Application.persistentDataPath>/Data/alteros-game.sqlite3`.
- Game schema 3 stores Topic occurrences, per-Scenario completions, source backups
  and one-time receipts alongside progression and layout data. The earlier root-level
  Topic SQL file is read-only migration input; its rows/receipts are promoted into
  this shared database and the source file is retained.
- `SqliteGameRepository` owns game metadata, progression and home-layout snapshots,
  host event records, idempotency receipts, and migration-source receipts. Schema
  migrations are versioned; mutation SQL is parameterized and commits are serialized.
- `ProgressionJsonImporter` imports the existing JSON source once, keeps its source
  bytes, records the SHA-256 receipt, and refuses malformed/future or conflicting
  data. The source file is not deleted.
- `AlteraProgressionStore` does not publish defaults until initialization succeeds.
  Discrete effects commit detached candidates before publication; continuous values
  use a five-second dirty checkpoint and best-effort pause/quit flush.
- `DialogueManager` marks only the validated catalogue route start as authored.
  Provider JSON, caller-supplied sequences, generic legacy file paths, local
  sequence choices, and test JSON use the untrusted start path. The store
  independently rejects credit changes, unsupported IDs, non-add operations, and
  out-of-range deltas.
- `FurniturePersistenceAdapter` now keeps the inventory and layout in one
  transactional home aggregate over the existing SQLite game owner. Schema-one
  homes initialize eight starter stock entries once; placement/move/return events
  are causal, stock conservation is validated, and duplicate receipts reload
  current durable truth. The synthetic Persistence suite passes 23/23; no real
  profile was migrated.
- `GameBackupService` creates game-only consistent checkpoints, retains a
  pre-migration copy, stages and validates candidates, and requires explicit
  approval to apply recovery. Conversation sessions/transcript, generated archive,
  Python memory and credentials remain outside this database.
- Private conversation schema 2 stores normalized sessions, bounded turns,
  repetition, ambient state, transcript events and JSONL source backup chunks.
  Private generated archive schema 1 stores records, exact payload bytes, aliases,
  source manifest chunks and validation diagnostics.
- `RuntimeMigrationCoordinator` journals per-domain readiness before conversation
  and generated archive facades become usable. Missing committed shared targets fail
  closed and never trigger legacy replay.
- `Tools/Persistence/Check-GamePersistence.ps1` compiles the production persistence
  sources with Unity's bundled compiler and runs 15 synthetic checks against the
  packaged Windows x64 native plugin. All headless groups pass. Unity source
  import/compilation is verified; the installed authored SQL release passed read-only
  artifact inspection. Persistence fixture and deferred conversation tests await
  focused Unity reruns, while Play Mode/player/Android behavior remain unverified. See
  [[Tracking/Full SQLite Cutover Implementation Plan]].

## Dialogue and routing status

Conversation entry remains intentional T interaction, free-text input from the
greeting choice, and unsolicited ambience. `ModelTest` now routes that input
through the unified M2 bridge without a Topic/Agent switch. The online toggle is
authored on the existing Dialogue Canvas and defaults off. Offline input uses
the promoted Topics and warmed GTE; online generation uses the local sidecar
only after the toggle and a BYOK key are present. This source/scene wiring has
not yet passed a player-visible Play Mode check.

`Assets/InputSystem_Actions.inputactions` now binds Player.Interact to keyboard T
while preserving its gamepad binding; E opens inventory when empty-handed and
returns a held piece while placing. ModelTest has one `FurnitureInputController`,
one shared off-by-default `DebugModeState`, inventory/preview/diagnostics prefabs,
and floor plus North wall-opening support. `FurnitureHandlingController` obtains
`IFurnitureHomeStore` from `AlteraProgressionStore`, which supplies the transactional
SQLite `FurniturePersistenceAdapter`; persistence checks are recorded 23/23. The
scene adapter reads the canonical furniture JSON from StreamingAssets, so no second
catalogue is installed. The 2026-10-22 pre-live record reports Furniture84/84,
Persistence23/23, and focused Unity EditMode29/29. Synthetic physics,
activity-claim and UI behavior are covered; physical input, Game View, restart
and player acceptance remain open; see
[[Tracking/Furniture Inventory and Placement Implementation Plan]] and
[[Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]]. The
SQLite authoring backend is migrated and verified; current Scenario/Unified
conversation source suites are recorded at 67/67 and 197/197 after their harnesses
include the production Authoring sources; no fresh suite was run in this reconciliation.
The graphical Workbench phases W2-W5 remain open; see
[[Tracking/Content Workbench Implementation Plan]]. These source results are not
Unity player acceptance.

- Generic corpus: 57 route folders and 161 canonical dialogue JSON variants
  under `Assets/StreamingAssets/Dialogue/Generic/`
- Staged SQL semantic index: 673 embeddings, 57 routes, 768 dimensions; the full
  release also contains 439 Dialogue sources and 6,041 exact dependencies.
- `GteSemanticRuntime` obtains its route index from the SQL release, then loads only
  the model and tokenizer from `Resources`, performs a warm inference, and exposes
  route ranking after reaching `Ready`.
- Its 2026-10-15 startup path loads resources asynchronously, parses managed
  tokenizer JSON off-thread, yields between preparation stages, schedules one
  warm-up layer per frame, and polls readback before Ready. Foreground routing
  remains synchronous. Single scheduling operations can still hitch; see
  [[Verification/2026-10-15 Startup and Expressions]] for measured limits.
- `AlteraEmotionController` resolves optional body triggers against the actual
  Animator parameters, including neutral -> Default and happy -> Joy aliases.
  Unsupported facial-only cues no longer emit nonexistent body-trigger commands.
- Free-text authored routing is wired through the topic input path and falls
  back to the authored `unclear_short` route when GTE is unavailable or below
  threshold
- The human-review catalogue is in `Documentation/Semantics/`; it defines the
  starter Intent, Tone, and Topic vocabulary
- The staged release pins exact source and GTE hashes; runtime ranking still uses
  the existing host matcher and gates.
- Authored topic suggestions now have one owner: `TopicOptionCatalog`. The
  direct duplicate injection path was removed from `AlteraInteractable`.
- Prepared/generated topic records are intentionally not surfaced in the menu
  until their playback path exists.

## Approved design versus implementation

The architecture baseline approved on 2026-10-14 has three model roles:
local embedding, sentiment evidence, and BYOK LLM generation/tool requests.
TTS remains a separate speech service. GTE and the TTS provider seam are
implemented; sentiment analysis and the live/background LLM harness are not.
The sentiment implementation and orchestration framework remain undecided.

Stats, relationship, and economy effects remain game-owned. The approved
initiative ceiling and focus/interruption policy still need harness integration;
the current focus timer does not publish a shared interruptibility signal.

## Generated dialogue status

The archive and menu surfaces exist, but the background generation workflow is
not wired. Archive persistence is private SQL-backed:

- `GeneratedDialogueArchive` imports the old manifest and available payloads once
  into `Data/personal-content.sqlite3`, then persists lifecycle metadata there.
  `jsonPath` is an import locator; payload bytes and import diagnostics are retained.
- Imported `Ready` status does not make a payload routable. The payload must pass
  the host dialogue validator, and the current archive facade keeps unvalidated
  records unavailable.
- `TopicOptionCatalog` currently surfaces authored topic choices only; prepared
  generated records remain unavailable until their playback path exists
- `DialogueManager` currently warns that background topic generation is not
  available and does not play generated-dialogue choices
- There is no `AdaptationWorker`, LLM client, generated-branch validator
  pipeline, or personalized semantic overlay in the Unity client

Treat generated dialogue as scaffolding, not a functioning feature, until that
workflow is implemented and validated end to end.

## TTS status

- Prebaked and runtime cache lookup is implemented by `TTSCacheUtility`.
- New provider audio is requested from OpenRouter using
  `qwen/qwen-audio-3.0-tts-plus` and MP3 output.
- 322 prebaked audio files exist, but only 2 of the greeting's 6 speech nodes
  match the current cache keys. Voice coverage and live provider access remain
  unverified.
- `DialogueManager` writes runtime audio to `StreamingAssets/TTSCache`, keeps a
  512 MB soft cap, and prefetches the next four dialogue nodes.
- If a requested clip is still being generated, `FetchAndPlayTTS` shows the
  loading animation and retries every 1.25 seconds.
- `OPENROUTER_API_KEY` overrides the stored key during development. Windows
  user keys are encrypted with current-user DPAPI. P opens the Canvas-authored
  OpenRouter settings panel; [[Verification/2026-10-16 BYOK Setup]] distinguishes
  compile/wiring evidence from pending visual, secure-storage and live checks.
  Saving credentials alone enables neither generation nor speech.

Known TTS risks:

- Runtime writes under `StreamingAssets` may be read-only in packaged builds;
  move generated cache storage to `Application.persistentDataPath` before a
  release build.
- Prefetch and foreground fetch do not share an in-flight task, so a node can
  be requested twice under unlucky timing.
- Cache keys currently use the language voice ID and text hash, not provider
  model/voice revision.

## Editor and tools

- CoplayDev Unity MCP `10.1.0` was installed on 2026-10-15 through the pinned Git
  package in `Packages/manifest.json`. It matches the existing Codex stdio server;
  the Codex configuration was not changed.
- Historical 2026-10-15 probe: instance `alteros@48a268a4` connected on port `6400`. MCP confirmed project
  root `E:/Pleiades/alteros`, active `SplashScene` (build index 0), compilation and
  import idle, and Play Mode off. Scene hierarchy reads succeeded; the Console
  error query returned zero entries without clearing it. This is Editor/tooling
  proof, not startup, dialogue, or audio acceptance.
- Read-only dynamic probes worked with the explicit `codedom` compiler. The
  automatic compiler probe disconnected once. Cached `editor/state` snapshots
  can age while idle; successful live queries are stronger connectivity evidence.
- 10 existing pure test methods passed through a temporary Mono probe against
  current compiled assemblies. This was not Unity Test Runner/PlayMode execution.
- Semantic Lab's offline scoring/fixture self-check passed. The separate
  `Tools/DialogueCoreCheck.cs` failed an isolated compile because it still uses
  pre-migration node/choice type names.

- `Assets/AlterOS/Editor/GteSemanticToolbar.cs` adds the `AlterOS/Semantics/GTE`
  toolbar menu for asynchronous GTE rebakes and asset reimport.
- `Assets/AlterOS/Editor/TopicPlaygroundWindow.cs` adds the
  `AlterOS/Dialogue/Topic Playground` Editor window; `Tools/TopicPlayground/`
  provides a no-graphics Unity batch scenario launcher. Both exercise shared
  production confidence, time-window, versioned numeric eligibility,
  stable-variant selection and canonical traversal. Detached context is built
  from `StatCatalogue`; local hour defaults unknown; synthetic fixtures are
  opt-in and outside StreamingAssets. Supplemental source checks pass 31/31.
  The prior 36/36 Editor suite predates this extension. The 2026-10-22 fingerprint
  fix moves model hashing from repaint into a cached cancellable worker; source
  15/15, Unity compile zero errors and focused fingerprint EditMode 1/1 were
  reported. Fresh GTE inference, actual repaint profiling, visual review and
  Editor/command parity remain unverified; separate model-load stalls are not
  proven fixed. See [[Verification/2026-10-22 Topic Playground Responsiveness]].
- `Tools/SemanticLab/` contains the standalone anchor-baking and benchmark
  tooling; it does not launch Unity and does not execute authored dialogue.

## Tests and validation

The `AlterOS.Tests` assembly references `AlterOS.Runtime` and contains:

- authored route-catalog tests
- a GTE semantic PlayMode test
- OpenRouter credential resolution tests
- OpenRouter TTS payload tests

`AlterOS.Editor.Tests` adds expression-trigger, cooperative-startup, and failed
worker cleanup regressions. Eight Editor cases and eleven runtime-assembly
PlayMode cases passed on 2026-10-15; both suites passed after the final one-layer
tuning. Normal Splash -> ModelTest -> input-assisted greeting
was separately observed. This does not prove the entire Session Zero, audio
quality, provider latency, physical input, or all visual presentation.

## Next safe work

1. Respect SQL-authoring and furniture worker ownership in TODO; do not duplicate
   implementations or compete for Editor access.
2. Rerun focused corrected Editor tests and verify migration/source-ID/memory
   boundaries with synthetic fixtures.
3. Run isolated Splash -> ModelTest acceptance. Ordinary startup still uses the real
   persistent path; no whole-startup isolation pass is claimed.
4. Finish memory UI, Workbench integration and M4 phone/TTS in their own scopes.
   Existing TTS cache-path/in-flight risks above remain deferred, not fixed.

## Sources inspected

- `AGENTS.md`
- `README.md`
- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Assets/AlterOS/AlterOS.Runtime.asmdef`
- `Assets/AlterOS/Semantics/GteSemanticRuntime.cs`
- `Assets/AlterOS/Semantics/DialogueRouteIndex.cs`
- `Assets/Scripts/Dialogue/DialogueManager.cs`
- `Assets/Scripts/Dialogue/DialogueRouter.cs`
- `Assets/Scripts/Audio/TTSCacheUtility.cs`
- `Assets/Scripts/Interaction/GeneratedDialogueArchive.cs`
- `Assets/Scripts/Interaction/ConversationSessionStore.cs`
