---
title: Data Management Implementation Plan
date: 2026-10-21
tags: [alteros, persistence, implementation, handoff]
status: source-implemented-unity-validation-pending
---
# Data Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans for the delegated
> implementation session. Use subagent-driven-development only if the human chooses
> that execution method. Steps use checkbox syntax for tracking.

**Goal:** Deliver D1 desktop-owned continuous SQLite persistence for existing
progression and the furniture home-save adapter, with safe migration and recovery.

**Architecture:** Pure game-owned persistence contracts plus a narrow SQLite
adapter; no dependency on Python, an LLM, internet or account login. Preserve
existing memory/transcript ownership. Future online services are documented seams,
not mock services masquerading as implemented sync.

**Tech Stack:** C#, Unity 6000.4.4f1, SQLite through a separately verified binding,
existing dedicated Editor test assemblies and PowerShell source harness patterns.

**Spec:** [[Data Management Design]]. Read both before executing.

## Execution state (2026-10-21)

The D1-D6 desktop source foundation is implemented and passes the synthetic
headless persistence suite described in [[Data Management Handoff]]. Unity has
not imported/compiled the full change or run the new Editor test assembly: the
Unity MCP bridge timed out. Treat implementation as ready for source review, not
as Editor, Play Mode, Windows player, Android, or gameplay acceptance. The
individual acceptance steps below remain the detailed checklist; Unity-only and
cross-suite checks stay open.

## Global constraints

- Work only in `E:\Pleiades\alteros`; preserve shared changes and serialized IDs.
- No database/credential access in the real player profile for automated tests.
- No paid calls, accounts/backend/mobile implementation or new gameplay mechanics.
- No memory DB/transcript migration; no automatic source deletion or reset-on-error.
- Static UI uses Editor-authored Canvas/prefabs; never edit YAML or meta files.
- Keep mutation rules in current owners; persistence is not permission to accept
  arbitrary LLM stat deltas, add gifting mechanics or redesign needs/emotions.
- Do not stage the shared tree or create commits without an established scoped
  repository workflow. The branch/worktree state must be inspected first.
- Provider selection below is a real gate, not authorization to install an arbitrary
  package or claim Android readiness from Editor success.

## Review focus

1. Abrupt exit during commit/migration: old or new complete state, never a mixture.
2. Retry after lost acknowledgement: no duplicate event, item or effect.
3. Corrupt/future save: no default overwrite; explicit recovery-required state.
4. Unsupported/missing furniture definitions: preserve orphan records and exact pose.
5. Privacy after restore: game-only backup excludes memory/secrets; recall cannot
   become enabled by restoring stale consent or an obsolete privacy policy.

## Scope and dependency gate

Installation follow-up2026-10-21: human approved the concrete binding, UPM installed
the pinned version, and seven real native SQLite Editor smoke assertions passed.
The dependency approval gate is resolved. A separate synthetic Windows x64 native
probe passed. Keep Unity player-build/runtime and Android checks explicit; do not
repeat approval or claim Android acceptance.
Use `Tools/Persistence/SqliteBindingSmoke.cs.txt` for the reproducible Editor probe.

Human approved this plan and the concrete binding on 2026-10-21. Fresh prerequisite
evidence is in [[../Verification/2026-10-21 Data Management Prerequisites]].
`com.gilzoide.sqlite-net`1.3.2 is installed from commit
`08248bd5884d8eb932a837aa56d4ff456daf913f`; it declares MIT and includes the
Windows x64 native binary. Unity Editor compilation and seven native smoke
assertions passed. D0 continues to track player runtime/managed loading and Android
separately. Do not repeat dependency approval or route game saves through the Python
memory sidecar.

This plan does NOT implement full inventory/gifting, preference evolution, online
accounts, synchronization, billing, complete personal-data backup/restore, arbitrary
SQL tools, new offline simulation or Scenario resume after quit.

## Proposed file boundaries

- `Assets/AlterOS/Persistence/GamePersistenceContracts.cs`: detached envelopes,
  results, identity/revision/receipt and repository interface.
- `Assets/AlterOS/Persistence/SqliteGameRepository.cs`: parameterized transactions
  and serialized writer; focused helpers below rather than a monolith.
- `Assets/AlterOS/Persistence/GameSchemaMigrations.cs`: schema version gates.
- `Assets/AlterOS/Persistence/ProgressionJsonImporter.cs`: one-time legacy import.
- `Assets/AlterOS/Persistence/GameBackupService.cs`: consistent game-only backup
  and staged recovery.
- `Assets/AlterOS/Persistence/GamePersistenceScheduler.cs`: clock-injected dirty
  checkpoint/pause/quit policy, no MonoBehaviour unless actual lifecycle required.
- `Assets/AlterOS/Persistence/FurniturePersistenceAdapter.cs`: existing furniture
  DTO/command integration, not a new resolver or layout format.
- `Assets/AlterOS/Tests/Editor/Persistence/`: corresponding focused test classes.
- `Tools/Persistence/Check-GamePersistence.ps1`: focused keyless test entry point.
- Modify `Assets/Scripts/Progression/AlteraProgressionStore.cs` and audited mutation
  callers only to delegate durable ownership and expose failure correctly.
- Reuse existing test assemblies; let Unity create assets/meta normally.

## D0 — Fresh inventory and binding proof

- [ ] Read root instructions/TODO, design, progression Save/mutation callers,
  furniture layout command APIs, memory repository and current package/native DLLs.
  Record exact owners, file names, assembly boundaries and affected callers in
  `Documentation/Tracking/Data Management Handoff.md`.
- [ ] Select the minimal binding as described by the gate. If none is usable,
  stop before installing and request approval with exact dependency evidence.
- [ ] In a synthetic temporary directory, test open/create, parameter binding,
  rollback, reopen, backup and native library loading without the Python sidecar.
- [ ] Verify Unity Editor and Windows player compatibility separately; record
  Android as unverified unless actually tested. Failure of the binding blocks
  subsequent implementation, not ordinary existing game startup.

## D1 — Repository identity and transactional foundation

**Produces:** `IGameRepository.Load(): GameLoadResult` and
`Commit(GameCommitRequest request): GameCommitResult` plus consistent backup hook.
Result states: ready/committed/duplicate/stale_revision/conflicting_operation/
recovery_required/unavailable. Requests are host-created, never model-provided.
`GameCommitRequest` includes operationId, expectedGameRevision, complete validated
progression envelope, optional layout document and meaningful host events.
`GameLoadResult` provides stable identity, committed revision and detached snapshot.

- [ ] Write failing tests in `GameRepositoryTests.cs`: fresh IDs stable after
  reopen; unknown hydration preserved; commit state/event/receipt atomic; injected
  pre-commit failure changes nothing; same ID/payload returns same receipt; changed
  payload with reused ID fails; stale revision unchanged; later operations ordered.
- [ ] Run focused tests and capture expected failures before implementing.
- [ ] Implement contract/schema/repository: separate metadata, current progression
  snapshot, home layout snapshot, host events and operation receipts. Reuse existing
  DTO serialization for complete snapshot payloads rather than inventing an EAV
  stat system. Event fields: eventId, operationId, kind, actor/target IDs,
  occurredAtUtc, recordedAtUtc and schema-versioned bounded payload. No per-frame log.
- [ ] New IDs use UUIDs once; existing playerId maps separately without rekeying
  memory. One game DB path under the trusted persistent root. AccountId stays null
  in this slice; no local account-binding API simulates authenticated identity.
- [ ] Re-run focused tests; assert future schema, locked/read-only DB and corrupt
  bytes yield explicit failure without replacing original bytes or defaulting.

## D2 — Safe progression migration

Review regression for D1: check receipt before stale-revision rejection; retry an
old operation after newer commits and return the original receipt. Interleave stat
checkpoints/layout changes and preserve unchanged domains with distinct game/layout
revision names, not a single overloaded expectedRevision.

**Consumes:** D1. **Produces:**
`ProgressionJsonImporter.ImportOnce(string sourcePath, IGameRepository repository)`
returning imported/already_imported/no_source/recovery_required.

- [ ] Add failing `ProgressionMigrationTests.cs`: all schema-4 fields survive,
  custom/unknown fields supported by existing DTOs preserved, hydrationKnown false
  stays unknown, gameplay credits stay gameplay credits, source bytes unchanged,
  second startup never reimports; malformed/future input stops; crash/retry atomic.
- [ ] Capture failures, then implement staged validation, source SHA-256 receipt,
  pre-migration backup and single transaction import. If both populated new DB and
  legacy source exist without a valid receipt, report conflict rather than guessing.
  Unsupported extra legacy fields must be preserved in original backup and reported,
  not described as fully migrated without verification.
- [ ] Re-run tests including paths containing spaces and non-ASCII characters.
  Never point tests at Application.persistentDataPath or inspect the player's DB.

## D3 — Continuous persistence integration

D2 startup regression: a crash after metadata bootstrap but before import resumes
legacy detection. Never publish defaults or accept mutations until initialized;
an existing empty DB cannot bypass malformed legacy JSON.

**Consumes:** D1/D2. **Produces:**
`GamePersistenceScheduler.MarkDirty()`, `Tick(DateTime utcNow)`, `Flush()` and
an observable durable/dirty/unavailable status for existing presentation to bind.

- [ ] Write failing `GamePersistenceSchedulerTests.cs`: dirty checkpoints within
  five seconds, no write when clean, backwards clock does not indefinitely defer
  writes, pause flush, duplicate flush no effect, failure leaves dirty and retryable.
- [ ] Add progression integration tests: loading uses SQLite after migration,
  existing game APIs retain semantics, all tick/custom/flag/task mutation paths
  mark dirty, discrete action commit failure cannot report durable success, restarting
  without Python/network restores data. Preserve existing unknown-stat behavior.
- [ ] Implement minimal scheduler and store delegation. Audit all callers before
  changing public Save signatures; preserve compatibility while making failure
  available to transactional callers. No new public arbitrary-write/model API.
  Do not use UI Changed notification as proof a durable commit succeeded.
- [ ] Preserve existing tick rates/decay; no offline catch-up algorithm added.
  Document maximum uncommitted continuous progress window and pause/quit limitations.
- [ ] Run focused tests and existing progression/activity suites. Wire any actual
  lifecycle/serialized references through Unity Editor only, preserving unsaved work.

## D4 — Home-save adapter without inventory expansion

D3 failure regression: discrete effects use detached candidate -> commit -> publish.
Rejecting a reward/action must leave live state unchanged, including after the next
successful continuous checkpoint. A Save wrapper alone is insufficient when callers
already mutate public State before saving.

**Consumes:** existing `HomeFurnitureLayoutDocument`/layout validation and D1.
**Produces:** `FurniturePersistenceAdapter.LoadHome(string homeId)` and
`CommitLayout(string operationId, long expectedGameRevision, long expectedLayoutRevision,
HomeFurnitureLayoutDocument validatedLayout)` returning existing-compatible results.

- [ ] Add failing `FurniturePersistenceTests.cs`: first-load seed once; exact
  metres/quaternion/support and revisions round-trip; newer seed does not replace
  saved layout; missing definitions remain inert preserved orphans; failed commit
  does not publish changed arrangement; no reservation/active Scenario auto-resume.
- [ ] Implement adapter after validation by existing host/geometry rules. No second
  JSON writable save authority. Existing portable file helpers may remain export/
  test tools; production ownership must be explicit. Coordinate touched APIs with
  Room Testbox, not by replacing its in-flight changes.
- [ ] Re-run focused tests and `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`.
  No inventory supplies, debug-item conversion, placement UI or gift effects here.

## D5 — Recovery and privacy isolation

Existing consent/suppression is inside the memory DB, not an external ledger.
Do not claim restore-proof personal-memory recovery exists. Game backups may still
contain personal task/event text; keep them private and outside automatic AI input.

**Consumes:** D1 consistent snapshot hook. **Produces:**
`GameBackupService.CreateBackup(reason)` and `StageRecovery(backupId)` with
validated candidates; application only through explicit recovery approval, not a
routine player-facing Load workflow. No arbitrary imported path execution.

- [ ] Add failing `GameBackupTests.cs`: backup during writes reopens consistently;
  five rotating checkpoints/max one per 30 minutes; pre-migration backup retained;
  corrupt/incompatible candidate rejected before live replacement; failed replace
  retains original; pending receipts cannot be silently replayed as new successes.
- [ ] Add privacy-isolation assertions: archives contain game data only, no API
  keys/personal-memory DB/chat; restoring never changes consent PlayerPrefs,
  memory-policy flags or suppression files. Tampered archive path traversal rejected.
- [ ] Implement bounded retention and consistent SQLite backup (not naive main-file
  copying). Verify staged candidate integrity/version/identity before presenting it
  as recoverable. Stop writes during replacement; keep recoverable original.
- [ ] Document existing memory suppression API boundary from source. Do not build a
  second privacy ledger in game DB. Full personal-memory recovery remains blocked
  until current external suppression can be reapplied and tested by its owner.
- [ ] Run focused tests and `Tools/Conversation/Check-PlayerMemory.ps1` to ensure
  privacy/repository behavior remains intact; all tests use synthetic data/keyless.

## D6 — Verification and handoff

- [ ] Provide `Tools/Persistence/Check-GamePersistence.ps1` that runs the real binding
  and focused tests without Unity open, internet or Python agent process. If native
  loading/toolchain prevents this, report the limitation, not a stubbed SQLite pass.
- [ ] Run focused persistence, furniture, conversation/memory and Topic suites;
  record exact commands/counts and pre-existing failures in
  `Documentation/Verification/2026-10-21 Data Management Foundation.md` (use actual
  execution date if different). Run focused Unity import/compile/EditMode when
  reachable; never force-close Editor or discard unsaved scenes.
- [ ] When authorized/available, normal Splash -> ModelTest acceptance: existing
  progression restored, host-owned change persists across normal restart, no
  personal key/provider used. Synthetic corruption/failure tests stay outside
  the real save. Record Play Mode and player acceptance separately.
- [ ] Update handoff, Home/Feature Register/TODO, Architecture and
  UnityProjectContext when ownership/packages actually change. Include schema,
  binding/license/platform evidence, migration/resume steps and untouched domains.
- [ ] Stop after desktop foundation; user reviews before inventory consumers,
  memory migration, account linking, cloud sync or mobile implementation.

## Ready-to-paste session prompt

Implement the reviewed Data Management Implementation Plan in
E:\Pleiades\alteros, reading AGENTS.md, TODO.md and Data Management Design first.
You own desktop persistence D0-D6, not inventory gameplay, cloud/mobile/accounts,
provider billing or a memory/transcript rewrite. Execute, rather than replanning
the product. D0's SQLite binding approval is a hard dependency gate if no usable
existing binding exists: return one concrete recommendation before installing.
Preserve shared work and existing IDs; keep gameplay saving independent of Python.
Use synthetic data for migration/failure/recovery tests, never player files or keys.
Coordinate furniture APIs without overwriting Room Testbox changes. No provider
spend, hand-edited Unity YAML/meta or forced Editor recovery. Finish with exact
verification evidence, unresolved gaps and the next acceptance check.

## Plan review

Self-reviewed against the design: D1/D2 identity/transactions/migration; D3
continuous saving; D4 home ownership; D5 recovery/privacy isolation. Accounts,
mobile, paid services, broader item mechanics and full memory restoration remain
explicit roadmap contracts rather than unimplemented promises inside D1.
Dependency selection is resolved by the installed binding; remaining D0 platform
checks are named above and in the prerequisite evidence, not implied passes.
This document is a plan, not an implementation dispatch or a passing test report.
