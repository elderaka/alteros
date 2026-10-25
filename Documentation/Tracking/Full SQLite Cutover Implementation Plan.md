---
title: Full SQLite Cutover Implementation Plan
date: 2026-10-22
tags: [alteros, implementation, sqlite, migration, cutover]
status: source-unity-compile-verified-editor-acceptance-open
---
# Full SQLite cutover implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: executing-plans. The human chose the
> existing Database Management System session and continuous implementation with
> a final report. Execute this amendment and the retained P tasks, not another
> scope-planning round. Preserve unfinished P1 work and verify it before claiming green.

**Goal:** Make SQLite the actual runtime authority for dialogue/content/embeddings,
Topic consumption, conversation sessions and chat transcripts, rather than leaving
the new repository as an optional sidecar to file-based runtime stores.

**Architecture:** Reuse approved P1-P7 content/runtime/history work and D0-D6 game
transactions. Add explicit history/archive importers and switch existing callers to
SQL-backed facades. Keep private conversations physically separate from game-only
recovery; preserve existing memory SQLite/forget ownership and credential protection.

**Tech stack:** Installed pinned SQLite-net1.3.2, C#, Unity6000.4.4f1 and existing
headless PowerShell/Mono harnesses. No new DB package or model/provider calls.

**Spec:** [[Content and Interaction ERD]], [[Data Management Design]],
[[Content Data Integration Implementation Plan]], [[Player Memory Design]].

## Authority and precedence

CONTINUE implementation delivered2026-10-22 to existing Database Management System
thread01a15e76-daeb-7451-982f-95a510093fd3 (local), retaining its model/reasoning.
App acknowledged task delivery; this is not an implementation/test completion claim.

On2026-10-22 the human explicitly confirmed that full migration includes chat
transcripts, conversation-session history and Topic-consumption history, in addition
to runtime content/embeddings. This plan supersedes older P-plan exclusions of
those domains and its optional-runtime-only deliverable. It does NOT authorize
merging personal memory or credentials, cloud services, telemetry capture/upload,
training, altered gameplay permissions or destructive source deletion.

Preserve logical owners even when storage changes: coordinator remains the sole
transcript writer; Topic runtime owns consumption; game host owns effects; memory
owner controls consent/suppression. SQL file co-location never changes permissions.

## Execution order (one run, no routine approval stops)

F0 baseline -> resume P1 to verified green -> P2/P3/P4/P5/P6/P7 from the earlier
plan -> F1/F2/F3/F4/F5/F6/F7 below. P8's optional-source/default-legacy requirement
is replaced by F1/F6; retain its parity/workbench boundary. P9's verification/report
requirements are retained and expanded by F7. Do not restart completed tasks or
mark a whole phase complete based on a truncated terminal output.

No intermediate request for consent to routine tests/migration code. Actual user
data access, unsafe destruction, new security authority or an incompatible contract
still requires a stop. Stale Unity blocks live import/acceptance, not independent
source/headless implementation. Report partial acceptance honestly at the end.

## Global constraints

- E:\Pleiades\alteros only; preserve shared changes. No resets/bulk staging/hand
  editing Unity YAML/meta. Editor owns scene/prefab/imported asset composition.
- Automated tests use synthetic temporary profiles, never read actual player
  databases/transcripts/archives/keys or activate paid services. Code may implement
  normal first-use migration; do not run it against a live player profile here.
- Historical scope of this runtime migration retained JSON as the editable authoring
  source. The human approved SQL authoring on 2026-10-22; the separate
  [[SQLite Authoring Cutover Implementation Plan]] supersedes that target wording,
  and is implemented with a local 439-source store/export and headless verification.
  Unity Editor acceptance remains pending. JSON is retained import/export and
  evidence only; normal content/history operations use SQL without a permanent
  dual writer or hidden filesystem fallback.
- Model/tokenizer binaries, media/TTS files, Unity assets and secure credentials
  remain files/platform storage. Full SQLite migration does not mean storing every
  byte of the application in SQL.
- Existing personal memory remains Memory/player-memory.sqlite3 owned by Python;
  never merge/copy/suppress its rules. SQL history migration must preserve exact
  source identities, consent checks and forgetting behavior that feed this owner.
- Device/UI preferences may remain PlayerPrefs. Stable conversation/player identity
  previously in prefs is imported once/mapped to SQL; do not create a new Alterra,
  reset existing playerId or silently turn memory/online consent on.
- Original legacy files retained byte-for-byte, not deleted or overwritten after
  import. In-app runtime no longer updates them. Compatibility codecs exist only
  for deliberate import/export/tests, not competing production save authorities.
- Missing/corrupt/future SQL after cutover means explicit recovery/unavailable,
  not silent reconstruction from a stale legacy file or loss of newer history.
- Optional recording remains off, no backfill of newly migrated historical chats
  into analytics, no Contribute network transport or implied training permission.

## Review focus

1. Mixed importer crash: restart must not overwrite already committed newer data
   or duplicate imported turns/Topic counts. Corrupt source is not an empty source.
2. Transcript identity/order: old memory source IDs, event IDs, sequence numbers,
   duplicate payload semantics, paging and forget suppression survive migration.
3. Content trust/cutover: authored metadata is host-assigned after validation;
   generated rows cannot gain file-backed authored effect permissions by moving to SQL.
4. Private data recovery: conversation imports/backups never enter game-only backup
   or shared repo; no old plaintext data gets silently reread after logical deletion.
5. Missing artifacts/unsupported legacy formats: report named coverage gaps and
   block unsafe cutover, never silently omit content or fall back to random JSON.

## Store map and compatibility

| Runtime domain | SQL authority | Existing owner/API to adapt |
| --- | --- | --- |
| Stats/home/game events/receipts | Data/alteros-game.sqlite3, existing schema2 after P6 then schema3 | IGameRepository and existing sole Commit writer |
| Topic occurrence/consumption/completion/cooldown | Game schema3 tables | TopicHistory / TopicHistoryRepository facade; no replay-policy changes |
| Authored Generic/Topics/menus/activity/ambient/test content | SQLite authoring repository -> compiled read-only `content.sqlite3` release | Existing content loaders/Scenario validators; retained JSON is explicit import/export evidence |
| Anchor metadata/vectors | Same compiled authored content and personal overlay | Existing in-memory ranker/host gates; no new similarity algorithm |
| Generated archive metadata and validated generated dialogue | Data/personal-content.sqlite3 | GeneratedDialogueArchive facade; draft/status not automatic playback permission |
| Conversation sessions/repetitions/ambient recent history | Data/conversations.sqlite3 schema1 | ConversationSessionStore facade; bounded-session semantics unchanged |
| Transcript events/sequence/cursors | Same private conversations DB | ConversationTranscript facade + sole coordinator writer |
| Personal memory | Existing Memory/player-memory.sqlite3 | Same Python repository/consent/source suppression |
| Secure keys and device settings | Existing secure storage/PlayerPrefs where appropriate | Never plain SQL secrets or wholesale PlayerPrefs migration |

Private conversations DB has metadata, conversation_sessions, conversation_turns,
repetition_records, ambient_history, transcript_events, history_imports and
history_source_backups. These are private logical histories, NOT a second game_events
writer, contribution journal or new memory repository. Game schema3 adds
topic_occurrences, topic_scenario_completions and topic_history_imports using the
existing identity/receipt/migration machinery. Keep opaque legacy IDs; no UUID
requirement applied retrospectively to valid source identities.

Normalized private rows retain source payload/schema as bounded validated fields
where exact semantics cannot yet be projected. Do not compress an entire JSONL into
one blob and claim indexed conversation migration. Transcript rows expose current
fields, sequence and identity; event equality/duplicate detection reuses current rules.

## F0 — Freeze and reconcile current work

- [x] Inspect current P1 files/tests without overwriting them. Record its actual
  pass/fail after a fresh complete run, retained temp artifacts and source inventory.
- [x] In integration handoff, update the domain ownership table above with current
  exact APIs/callers. This is implementation inventory, not a new planning phase.
- [x] Run baseline focused suites named in the P plan; record existing Unity bridge
  failure separately. Preserve the D0-D6 original schema/receipt/backup guarantees.

## Implementation checkpoint 2026-10-22

P0/F0/P1/P2/P3/P4/P5/P6/P7 and F1-F6 source/headless work are implemented. Production
authored readers select the compiled SQL release; game schema 3 owns Topic occurrence
history; private conversation schema 2 owns sessions, repetition, ambient state and
transcript; private content schema 1 owns generated archive metadata and payloads.
`RuntimeMigrationStartup` runs the trusted per-domain journal before the session and
archive facades. Latest evidence: persistence 15/15, unified conversation 197/197,
Topic 33/33, Playground 13/13, Scenario 67/67, furniture 52/52, and ContentData
Repository 8/8, Bake 11/11, Publish 6/6, Flows 7/7, Semantics 4/4; Companion Runtime
171 passed, 2 skipped. A fresh 439-source
SQL release is staged and inspected. Unity source import and script compilation now
pass on 2026-10-22; focused Editor tests, staged-content import/player acceptance and
real-profile migration remain open. The coordinator's first focused EditMode job had
seven persistence fixture failures among 28 tests; a test-only lifecycle correction
is in place for rerun. The user's first staged catalogue build/import failed closed
on unregistered expressions. Those 104 compiled fields have since been standardized
in the SQLite authoring selection to the 12 registered presets; updated-export
headless Semantics and Bake checks pass. Unity has not retried the build against that
revision. See [[Content Data Integration Handoff]] and the dated verification note.

## F1 — Full runtime content authority and build artifact

**Files:** existing Content/ compiler/repository/adapters; ConversationTopicCatalog,
DialogueRouter, DialogueManager, DirectConversationBridge, GteSemanticRuntime,
AuthoredActivityScenario loader and callers found by search. New focused
Content/RuntimeContentStore.cs, Content/ContentBuildManifest.cs,
Editor/ContentBuildProcessor.cs; tests in Editor/ContentData/ and headless harness.

**Interfaces:** RuntimeContentStore.OpenTrustedRelease(configuration) returns a
validated pinned release; ReadLegacySequence/ReadCanonicalTopic/ReadScenario/ReadAnchors
project existing public contracts from SQL without runtime source-file reads.
ContentBuildManifest lists each source kind/path/hash, stable mappings and coverage.

- [x] RED tests covering every currently supported content source format: Generic
  DialogueDefinition, promoted TopicDefinition, activity Scenario wrappers, legacy
  DialogueSequence, greeting/menu/ambient/bark/system/test content and coverage/map
  metadata actually used by consumers. Preserve all represented fields, IDs,
  conditions, channels, actions/trust, priorities, weights and replay semantics.
- [x] Reuse P2 adapters; source-root inventory drives build manifest. Unknown or
  lossy formats produce named diagnostics and prevent an asserted full cutover;
  never silently skip them or equate archive manifest with dialogue content.
- [x] Add production resolver accepting current logical content IDs/relative path
  aliases mapped into release records. No literal file-open fallback in normal
  callers. Distinguish authored and private namespaces; no generated trust escalation.
- [x] Import baked GTE metadata/float32 vector bytes at build time, not model
  inference or rebake. Runtime reads SQL and supplies the same compatible in-memory
  arrays/ranking. Stop loading authoritative anchor assets from Resources after cutover;
  model/tokenizer assets may remain Resources as before.
- [x] Produce and inspect the full-corpus SQL artifact in task staging. The 2026-10-22
  artifact covers 439 sources and reopens with 6,041 dependencies, 57 Topics, 322
  Flows, 673 embeddings, and a 57-route/768-dimension index; see the F1 bake/inspect
  logs and artifact hash in the verification note.
- [x] Build/import through **Tools > AlterOS > Build Runtime SQL Content** after
  normalizing the current SQLite revision. The installed
  `Assets/StreamingAssets/Content/content.sqlite3` release passed read-only integrity,
  foreign-key and trusted-release inspection on 2026-10-22; its manifest snapshot
  matches authoring revision 63. The earlier attempt failed closed at
  `expression_not_registered`; expression fields were then standardized to the
  registered presets. This inspection does not establish startup/player acceptance.
  No shell-generated Unity YAML/meta or external binary patch was used.
- [x] Ensure ordinary startup automatically selects shipped SQL release with an
  explicit unavailable/recovery state when absent. Legacy mode remains import/test
  tooling only, not a user-facing switch or silent runtime rescue path.
- [x] Run source-level file-versus-SQL parity for the fresh corpus, localization,
  57 Generic routes across all 24 hours, candidate eligibility, source coverage,
  GTE vectors and the host Action allowlist. Runtime reads now use the SQL source.
- [ ] Run the normal Unity startup and player-visible route/effect failure checks
  after Editor import. Source parity is not player acceptance.

## F2 — Topic history one-time import and SQL cutover

**Files:** Topics/TopicHistoryRepository.cs, TopicHistory.cs, DirectConversationBridge
history initialization/save callers; new Persistence/SqliteTopicHistoryStore.cs,
TopicHistoryImporter.cs and prior-root SQL relocator; extend GameSchemaMigrations and
game tests.

**Interfaces:** SqliteTopicHistoryStore.Load()/Save(history,expectedRevision,
operationId) returns current-equivalent load/save outcomes with revisions.
Importer.ImportOnce(legacyPath,store) accepts host-only path and returns a durable
receipt plus missing/imported/already_imported/recovery_required result.

- [x] RED v1/v2 Topic history fixtures: exact instance/topic IDs, replayPolicy,
  start/present/complete/consumed/cooldown times, per-Scenario counters and unknown
  optional fields preserved; >1 completion/cancel/once behavior unchanged.
- [x] Import complete validated records with source bytes/hash/receipt in one game
  transaction; initialize missing history only when source and SQL domain genuinely
  absent. Partial/malformed/future source preserves bytes and fails closed.
- [x] Reuse internal decoder as a legacy codec, not a separately implemented weak
  parser. Existing SQL/newer data without a matching import receipt yields conflict,
  not overwrite or speculative merge. Receipt replay cannot re-consume or reset counts.
- [x] Update runtime Load/Save call sites to SQL; old JSON path is importer input
  only. On revision conflict keep unsaved status and require reload. No file dual-write.
- [x] Test migration restart/replay, edited/removed old file after success, retained
  source bytes and schema1->2->3 upgrade with old operation receipts/backup compatibility.
- [x] Consolidate the earlier root-level Topic database into the canonical
  `Data/alteros-game.sqlite3`; synthetic relocation rejects unknown user tables and
  unrelated source-backup rows without importing into the canonical target.

## F3 — Conversation sessions and repetition state

**Files:** Interaction/ConversationSessionStore.cs, ConversationModels.cs and audited
callers; new Conversation/ConversationStorageContracts.cs,
Conversation/SqliteConversationStore.cs, Conversation/ConversationSessionImporter.cs;
tests and Tools/ContentData/Check-FullMigration.ps1 (real native binding).

**Interfaces:** private store operations load/list sessions, append bounded turn,
update/end session and repetition/ambient state with explicit expected revision and
stable operation ID. ConversationSessionStore retains its public presentation facade.

- [x] RED synthetic legacy session JSON with multiple sessions/turns, linked generated
  IDs, timestamps, modes/routing flags, repetition decay state and last ambient line.
  Reopen SQL yields the same public read results and bounded-turn policy.
- [x] Implement private schema/import receipts and raw-source backups. Legacy
  session is history, not permission to resume an active Scenario after restart.
- [x] Preserve current facade lifecycle but replace file writes/default-on-corruption
  with explicit repository readiness/failure. Detached candidates -> durable commit
  -> publish for state-changing operations; don't silently display unsaved success.
- [x] Map existing conversation/session/player identity keys once without changing
  their values; SQL becomes canonical for these imported identities. Device settings
  and existing consent flags retain their owners until explicitly migrated separately.
- [x] Test independent sessions paging/caps, duplicate operations, interleaved
  transcript use of the same private DB, malformed/future/conflicting import, source
  untouched and failed persistence not corrupting active in-memory state.

## F4 — Transcript SQLite authority with source identity parity

**Files:** Conversation/ConversationTranscript.cs and coordinator constructors/factory
callers; new Conversation/SqliteTranscriptStore.cs,
Conversation/TranscriptJsonlImporter.cs; Memory/PlayerMemoryTranscriptSync.cs and
MemoryConversationHistoryFilter.cs only for storage-neutral reads/IDs if required.

**Interfaces:** IConversationTranscriptStore.Append(event), ReadPage(cursor,count),
ReadRecent(maxUtterances,maxCharacters) retain current result/status and paging
semantics. ConversationTranscript is the facade over SQL; legacy JSONL codec is
import/export only. Update all constructor/factory call sites and tests, rather than
letting a path-based old constructor silently keep a file writer active.

- [x] RED fixture with request/response/presented/status events: preserve original
  event IDs, sequence, source IDs, request/response/node identities, channel/source,
  bilingual text, expression, response payload, UTC times and pending/late/cancelled
  distinctions. Imported pagination/recent-context equal legacy valid-reader output.
- [x] Streaming bounded import, no full lifetime transcript in RAM; validate using
  existing record rules. Import transaction/receipt covers the complete file snapshot.
  Detect concurrent source change and retry or return conflict before publication.
  Invalid/truncated tail fails closed by current semantics: no silent prefix import,
  repair/deletion or treating it as an empty transcript. Preserve source bytes.
- [x] Duplicate event ID/same semantic payload returns prior sequence; changed
  payload conflicts. Sequence allocation and append atomic. Existing cursor values
  continue to identify the same boundary; do not renumber imported events.
- [x] Keep coordinator sole append owner; SQL read/export tools cannot append
  fabricated messages or re-trigger providers. Source IDs supplied to memory retain
  exact meanings; profile facts remain player-authored evidence, not all utterances.
- [x] Consent-enabled transcript sync preserves source IDs, disabled sync emits no
  events, and forgotten transcript sources are filtered from prepared history.
- [x] Combined memory-suppression and transcript-import/provider-await regression:
  the C# coordinator imports the shared synthetic JSONL into SQL and uses a deferred
  fake helper to test consent revoke and forgetting while pending; stale replies are
  neither appended nor presented, source IDs remain aligned, and migrated history is
  not synced as fresh memory evidence. Companion deferred-provider tests use a temp
  Python SQLite memory DB to verify no new memory write and no source replay after
  forgetting. Offline migrated history does not enable online generation, and chat
  history remains durable independently of memory/recording policy. No live key,
  provider or extraction call is used.
- [x] Restart, crash/retry, empty/missing source, invalid sequence/record, read-only
  DB, concurrent append and original-file retention tests. Runtime never rereads a
  stale JSONL after SQL import/delete/failure; no permanent dual writer.

## F5 — Generated archive content and reference migration

**Files:** Interaction/GeneratedDialogueArchive.cs, its record models and audited
consumers; new Content/GeneratedArchiveImporter.cs; private content repository;
tests with synthetic archive and per-record payload files.

- [x] RED fixtures preserve generated IDs/title/sourcePrompt/status/timestamps,
  routing/source metadata and linked session references. Ready status alone never
  grants schema validation, embedding readiness, Action permissions or publication.
- [x] Import metadata and available JSON payloads into the private content database
  jobs using exact old-ID aliases. Resolve only host-trusted contained paths; reject
  traversal/foreign profile paths. Missing payloads remain unavailable with retained
  metadata/diagnostic; don't fabricate content or silently drop the record.
- [x] Future runtime reads/writes use private SQL; jsonPath becomes a legacy import
  locator or explicit export, not the runtime authority. Keep source bytes/hash and
  one-time receipts. Private invalid/unindexed drafts remain non-routable.
- [x] Keep exact old generated IDs and session references without regenerating identities.
  Idempotent imports across the separate stores use explicit per-domain receipts and
  a bootstrap migration journal; no pretend cross-file ACID transaction. Activate
  routing only after all required referenced domains are committed/validated.
- [x] Test crash after a store commits, restart reconciliation, duplicate IDs,
  unavailable/corrupt payload, authored/private collision and generated-stat trust.

## F6 — Cutover journal, recovery and whole-flow parity

**Files:** new Persistence/RuntimeMigrationCoordinator.cs,
Persistence/RuntimeMigrationManifest.cs; `Assets/Scripts/Interaction/RuntimeMigrationStartup.cs`,
session/archive facade readiness, existing GameBackupService validation and content-store seam.

**Interfaces:** RuntimeMigrationCoordinator.Prepare(profileRoot,configuration) /
Resume() / ReadStatus() use trusted host configuration, per-domain source hashes,
target identity/schema/revision and completed receipt pointers. No model-selected
paths or general database administration tools.

- [x] Synthetic coordinator fixture includes an existing game schema-1 database,
  generated archive, Topic JSON, sessions JSON, transcript JSONL and identity alias.
  A crash after the Topic owner commits resumes from its SQL receipt; subsequent
  corrupt legacy sources are not reread. Content SQL is validated in the separate
  full-corpus build/inspection harness. Consent and suppression remain with their
  separate owners and are not copied or restored by this coordinator.
- [x] Distinguish missing sources from malformed/conflicting sources and committed
  imports from empty stores. Preserve exact source originals privately and require
  every configured domain before conversation or personal archive facades become
  ready. Offline authored SQL remains independently available through its own reader.
- [x] Each owner commits its rows and receipt atomically; the coordinator journals
  independent progress and resumes committed stages. Changed target identities and
  lost shared committed targets require recovery and never trigger legacy replay.
- [x] Game-only backups remain game-only; conversation sources/backups are not copied
  into game backup or shared docs. Import originals are private migration evidence,
  not advertised full recoverable backups. No automatic memory/conversation restore
  until latest suppression/deletion policy can be reapplied; fail closed on missing
  policy. Preserve the existing approved retention/deletion lifecycle boundaries.
- [x] Compare file and SQL sources for authored route/candidate decisions at every
  hour across the 57 Generic routes. Coverage, EN/JA and ID/EN projection, GTE index,
  and supported host Actions are checked. Full scene-to-player flow remains pending.
- [x] Scan runtime file reads/writes/Resources anchor loads and classify remaining
  each remaining use classified as authoring/import/export/asset/secure setting or
  actual migration gap. Tests may keep legacy codecs deliberately; production may not
  silently select them. No claim of full migration if runtime consumers still bypass SQL.

## F7 — Complete verification and final report

- [x] Run fresh ContentData groups, full persistence including RuntimeMigration,
  progression source compile, Scenario, Topic, Playground, furniture and conversation/
  player-memory suites. Capture full logs in task workspace; read final exits/counts,
  not a truncated fragment. Report baseline/new failures explicitly.
- [x] Verify the bundled SQLite-net managed binding and Windows x64 native plugin,
  foreign keys, UTF-8 paths, schema/receipt upgrades, private-store isolation, retry
  boundaries and fault-injection crash points with synthetic data. Root-level Topic
  relocation also verifies exact v3 user-table inventory and backup ownership.
- [x] Pin Unity instance/project and complete normal source import/script compilation:
  Unity `6000.4.4f1`, `alteros@48a268a4`; the error-filtered Console returned zero
  entries and both `AlterOS.Runtime.dll` and `AlterOS.Editor.Tests.dll` were rebuilt.
  This is compile evidence only; the compile pass did not start Unity tests or Play
  Mode. Subsequent coordinator Editor jobs and their current status are in the
  verification note. The moved DTO file retained its GUID, and Unity generated
  metadata for `ContentJson.cs`.
- [ ] Rerun focused synthetic Editor tests before broader suites, build/import the
  current SQLite snapshot, then perform normal
  `SplashScene -> ModelTest` acceptance using a synthetic profile only; no real save/key.
  The first job executed 28/28 tests with seven fixture failures and is preserved; the
  fixture-only lifecycle change awaits rerun. A combined deferred-consent Unity job
  `37e843576850489aa8b2fd6d0caeec12` stalled and is unverified; the async test-only
  correction passes the headless coordinator suite 30/30. Unity MCP polling then lost
  its connection (`WinError 10053`); no cancellation action is available. Request safe
  Editor recovery before another Unity job. If bridge status remains stale, report the
  acceptance gate rather than claiming gameplay or falling back to JSON.
- [x] Build process produces a full SQL catalogue and startup selects SQL by design.
  The installed artifact passed read-only inspection and matches the current authoring
  snapshot; see [[../Verification/2026-10-22 Full SQLite Cutover]].
- [x] Update plan/ERD/architecture/context/TODO/handoff with real authoritative owners,
  legacy import-only paths, retention/privacy gaps and schemas. New verification file:
  Documentation/Verification/2026-10-22 Full SQLite Cutover.md (actual date if later).
- [x] Final report distinguishes: source cutover implemented, actual synthetic
  synthetic domains, partial/unrepresented source formats, remaining runtime file
  consumers, tests/evidence, Unity/player acceptance and user-profile migration NOT
  executed. Do not claim source fixtures prove all player data safe or all platforms.
- [x] Send one concise report to the human and coordinating thread
  01a06388-281d-7601-a8e1-c47d7cca0c37 (local), as already human-authorized.

## Plan self-review

Each expanded domain now has import/cutover/compatibility tests and a named owner;
memory credentials and media aren't treated as generic SQL rows. F4 pins memory
source identity/forgetting, F6 pins cross-store retry/recovery and F1 removes optional
SQL-only delivery. Remaining UI/upload/training/mobile work stays excluded. No new
real-profile access, source deletion or unapproved provider call is needed to finish
the implementation. Preserve P1 work; this amendment overrides stale exclusions,
not the already-tested foundation's durability/security contracts.
