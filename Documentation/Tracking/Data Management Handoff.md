---
title: Data Management Handoff
date: 2026-10-21
tags: [alteros, persistence, handoff]
status: implementation-in-progress
---
# Data Management Handoff

Related: [[Data Management Design]], [[Data Management Implementation Plan]],
[[../Verification/2026-10-21 Data Management Prerequisites]], [[Room Testbox Handoff]].

## Current ownership

This session owns the approved desktop D0-D6 foundation. Stop after the desktop
implementation for human review. Do not add inventory/gifting gameplay, new need
algorithms, account/cloud/mobile/billing services, provider/TTS calls, or migrate
memory, transcript, Topic history, or generated archives.

## D0 baseline (2026-10-21)

- Project root: `E:/Pleiades/alteros`, Unity `6000.4.4f1`, target shown by Unity as
  `StandaloneWindows64`. Active Editor instance is `alteros@48a268a4`; live code
  probe confirmed the same root, ModelTest active, not playing, and not compiling.
- `com.gilzoide.sqlite-net` 1.3.2 is commit-pinned in manifest/lock at
  `08248bd5884d8eb932a837aa56d4ff456daf913f`, MIT. The supplied Editor smoke
  record reports 7/7 native SQLite assertions. A fresh PowerShell probe loaded the
  package's Windows x64 native DLL and passed 6/6 checks on a unique synthetic
  temporary database: open/create, parameterized Unicode text, rollback, backup,
  close/reopen, and native version lookup (SQLite 3.50.1). The Editor plugin
  importer enables Standalone Windows x64.
- This verifies the Windows native binary outside Unity and the managed binding in
  the Editor separately. A built Windows player and its managed/backend loading
  remain unverified; Android remains unverified. No real player database, memory
  database, credentials, or provider were accessed.
- Fresh Editor Console error query returned 0 entries before implementation. The
  checkout has no resolvable Git `HEAD` and `git ls-files` lists 0 files; no shared
  files are staged or committed. Keep an explicit changed-file list here.

## Existing owners and integration seams

- `Assets/Scripts/Progression/AlteraProgressionStore.cs` retains the schema-4
  progression DTOs and stat/flag/task APIs. It now initializes through the game
  repository/importer, gates publication and mutation on readiness, and exposes
  durable revision/status. Do not treat source integration as Unity wiring proof.
- `ProgressionTickSystem` calls `ApplyStatChanges(..., false)` then
  `RefreshDerivedState`; its existing tick/decay rates are out of scope. The
  serialized `saveImmediately` setting remains for compatibility; tick mutations
  are dirty-checkpointed without changing decay rates.
- `DialogueEffectApplier` accepts effects only for a file-backed Generic route
  selected through `TryPlayAuthoredRoute` after catalogue lookup and canonical
  validation. Public `PlayCinematic`/`PlayFromJson`, generic legacy `Play(path)`,
  local sequence choices, and test-JSON starts are untrusted. The store applies a
  12-change/5-unit allowlist and rejects credits/reward/memory effects. Activity
  hydration uses its separate bounded host checkpoint.
- `Assets/AlterOS/Furniture/HomeFurnitureLayout.cs` owns the layout DTOs and exact
  metre/quaternion/support fields. `HomeFurnitureRepository` validates, clones,
  hashes, seeds and saves portable files. The game-DB adapter consumes those APIs;
  the Room Testbox files and scene remain untouched.
- `PlayerMemoryClient`/sidecar repository, `ConversationTranscript`, Topic history,
  generated dialogue archive and BYOK credential storage retain their owners.
  Game backups include only the game database and migration copy; they do not
  change privacy flags or provide memory-restore suppression guarantees.

## Execution ledger

- D0: Complete. Unity identity/state and dependency were recorded above; six fresh
  synthetic Windows x64 native checks passed. Player and Android remain open.
- D1: Implemented `GamePersistenceContracts`, version-1 schema migration, serialized
  SQLite repository, parameterized transactions, separate game/layout revisions,
  operation payload receipts, host events, future/corrupt fail-closed handling, and
  UTF-8-path consistent backups.
- D2: Implemented schema-4 one-time importer with source hash/bytes, migration
  receipt, pre-migration database/source copies, retry detection, and conflict/future
  schema rejection. It never deletes the source JSON.
- D3: Integrated `AlteraProgressionStore` and audited tick, dialogue, hydration,
  task, stat, and DialogueManager effect boundaries. Schema-4 snapshots are shape-
  validated before normalization; incomplete or malformed inputs preserve their
  exact durable bytes and require recovery. Discrete operations commit detached
  candidates before publishing; continuous state is dirty-checkpointed. Only
  canonically validated, catalogue-selected file-backed routes can enable dialogue
  effects; public/provider and legacy file/sequence starts stay untrusted. The host
  allowlist is count-, key-, and magnitude-bounded and cannot mutate credits.
- D4: Added the home-layout adapter over the existing DTO/validator, separate
  revisions, one-time seed behavior, and exact pose/orphan retention. No inventory
  gameplay, Room Testbox edits, or scene changes.
- D5: Added throttled rotating game-only backups and staged recovery with identity,
  integrity, revision, and explicit-approval checks. Personal data repositories
  remain out of scope and outside these backups.
- D6 headless evidence (2026-10-21): `Tools/Persistence/Check-GamePersistence.ps1`
  compiled the production persistence sources with Unity 6000.4.4f1's bundled Mono
  compiler and ran **7/7 synthetic groups** in an isolated PowerShell process using
  the actual SQLite-net managed binding and Windows x64 native plugin. Coverage:
  repository/idempotency/stale revisions; atomic rollback; future/corrupt bytes;
  Unicode one-time migration/source backup; dirty scheduler/retry; furniture seed,
  pose and orphan retention; consistent backup/staging/approved restore.
- Source-only progression/integration compile via
  `Tools/Persistence/Check-ProgressionSourceCompile.ps1` succeeded with 0 errors,
  52 warnings. Warnings are from source types shadowing cached Assembly-CSharp and
  AlterOS.Runtime types, existing `FindObjectOfType` deprecations, and one assigned-
  but-unused legacy field. Its synthetic Mono probes passed for complete/incomplete
  schema-4 validation, malformed headers, bounded dialogue changes, and reward/
  credit rejection. A source-policy probe confirms only catalogue-selected,
  canonically validated file-backed routes can enable effects; public JSON, generic
  file playback, local choices, and test starts remain untrusted. This cached-
  assembly compile is not Unity project import or compilation.
- Static scan of 439 dialogue JSON files found 240 stat/effect entries, a maximum
  of 10 combined changes at one node, and no contract violations against the new
  allowlist. This is source-content evidence, not runtime playback proof.
- Added Editor regressions for incomplete schema-4 preservation, malformed schema
  headers, bounded dialogue effects, provider sequence-effect rejection, untrusted
  public/dynamic/generic/local sequence starts, and the validated authored-route
  permission path. The new route-regression source compiled with Unity's bundled
  compiler against cached Unity/NUnit assemblies, but that is not Unity test-assembly
  compilation or test execution. They have not run because the Unity bridge timed
  out; headless 7/7 does not cover them.
- Code review found and the source closed three issues: partial schema-4 snapshots
  were normalized, dialogue effects lacked bounded host policy, and dynamic AI JSON
  could reach the authored-effects path. Follow-up review found no remaining
  source-level bypass and identified missing coverage for generic/local starts;
  those cases are now included. No concrete receipt-ordering, backup-scope, or
  staged-recovery defect was found.
- Unity source import and script compilation passed on 2026-10-22 under Unity
  `6000.4.4f1`; the latest error-filtered Console query returned zero entries and
  both `AlterOS.Runtime.dll` and `AlterOS.Editor.Tests.dll` were rebuilt. The
  coordinator's focused EditMode job `711636fd43d046a98d1c56ae1cfab778` executed
  28 tests; seven failed in `ProgressionStorePersistenceTests` because its inactive
  GameObject fixture did not invoke `Awake`. Test-only setup now calls the public
  `Load()` method and assigns the store explicitly to `DialogueEffectApplier`; the
  correction awaits rerun. Failed job evidence was preserved. No real game database,
  credentials, memory, transcript, provider, or player profile was inspected.
- Windows standalone player, Android, Unity EditMode, Play Mode, and
  `SplashScene -> ModelTest` persistence acceptance remain open. Source foundation
  is ready for human review; do not mark DM-01 runtime-complete.
- Ruling: The plan's original dependency-gate paragraphs still said package approval
  and installation were pending. The 2026-10-21 prerequisite evidence and direct
  user instruction established installation/approval as complete before
  implementation. That evidence remains authoritative over the stale plan wording.
  Cost if wrong: the implementation would rely on a package the user had not
  approved.

## Final handoff checklist

Changed files: `Assets/AlterOS/Persistence/` (eight runtime files),
`Assets/Scripts/Progression/` (four persistence integration files),
`Assets/Scripts/Dialogue/DialogueManager.cs` and `DialogueRouter.cs`,
`Assets/AlterOS/Activities/HydrationCheckpointSink.cs`,
`Assets/AlterOS/Tests/Editor/Persistence/` (seven Editor test files),
`Assets/AlterOS/Tests/Editor/Activities/ActivityHydrationStoreTests.cs`,
`Tools/Persistence/` (synthetic test runner, headless harness, and source compile
check), `Assets/AlterOS/Tests/Editor/DialogueRouteRecoveryTests.cs`,
`Assets/StreamingAssets/Dialogue/system/stat_change_test.json`, plus TODO,
Design, Plan, Handoff, Architecture, UnityProjectContext, State and Effects, Feature
Register, Home navigation, and the dated Verification note. Only Unity created the
one imported test `.meta`; no Unity YAML or `.meta` file was hand-edited. The
remaining new files need normal Editor import to receive Unity metadata.

Next acceptance after source review: reconnect/import in Unity, confirm the Console
and compile the Editor tests; run them with synthetic roots. Then run the Windows
standalone player against an isolated synthetic profile and verify normal
`SplashScene -> ModelTest` persistence in Play Mode without provider use. Android
and full personal-memory recovery remain separate work.

## P6 additive game-history extension (2026-10-22)

The approved full SQLite cutover extends the D0-D6 boundary without replacing its
single game writer. `GameSchemaMigrations` now upgrades schema 1 to schema 2 atomically;
the existing `SqliteGameRepository.Commit` transaction owns typed event contexts, stat
changes, Scenario runs, Flow invocations, Step executions, answers and Action execution
checkpoints. `variable_values` is present but intentionally not written. Version-1
request hashes remain valid, and schema-1 backup recovery upgrades only a copied staged
database before replacement. The source backup stays unchanged.

Fresh synthetic evidence: `Tools/Persistence/Check-GamePersistence.ps1` **8/8**,
including v1 upgrade/receipt replay, migration rollback, v1 staged backup restore,
bounded history reads, action-cause checks, stat-owner/clamp/unknown semantics and
atomic sidecar rollback. `Check-ProgressionSourceCompile.ps1` passed the source compile
and existing policy probes with 0 errors / 52 warnings. `Check-ContentData.ps1 -Group
Flows` passed **7/7**, including Scenario history callback identities and observer
failure isolation. Logs are under `.superpowers/sdd/Content Data Integration
Implementation Plan/P6-*.log`.

These are source/headless results only. Unity import/Editor tests, Play Mode, player
behavior and a real profile migration were not run. Memory, transcripts, Topic history
and generated archive are not merged into game backups; their later SQL owners remain
separate in the full-cutover plan.

## Full SQLite cutover follow-up (2026-10-22)

Topic history now shares the canonical progression database at
`<persistentDataPath>/Data/alteros-game.sqlite3`. The earlier F2 root-level Topic SQL
store is a read-only migration source; `TopicHistoryDatabaseImporter` preserves its
source receipt, operation rows and backup bytes, and can promote later Topic operations
when the canonical DB contains only the same JSON seed. It validates the complete
schema-v3 table inventory and all source backup rows; unknown tables, unrelated
backups, divergent history, or non-Topic legacy game data fail closed. The root source
is not deleted.

Final synthetic suites: persistence 15/15, unified conversation 197/197, Companion Runtime
171 passed/2 skipped, Topic 33/33,
Playground 13/13, Scenario 67/67, furniture 52/52, ContentData Repository/Bake/Publish/
Flows/Semantics 8/11/6/7/4. The bundle includes the managed SQLite-net binding and
Windows x64 native plugin. No Unity import/player acceptance, Android check or real
profile migration ran; use [[Full SQLite Cutover Implementation Plan]] for the staged
content artifact identity and remaining Editor acceptance.

The transcript-to-memory boundary regression now verifies SQL-imported source-ID
alignment across `DirectConversationCoordinator` and the deferred provider wait. A
consent revoke or new suppression discards the pending result; ordinary chat history
remains in the transcript. The companion repository tests also reject new memory
writes and old-source replay after policy changes.
