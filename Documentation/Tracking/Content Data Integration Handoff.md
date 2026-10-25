---
title: Content Data Integration Handoff
date: 2026-10-21
tags: [alteros, persistence, content, sqlite, handoff]
status: implementation-in-progress
---
# Content Data Integration Handoff

Related: [[Content Data Integration Implementation Plan]],
[[Full SQLite Cutover Implementation Plan]], [[Content and Interaction ERD]], [[Data Management Handoff]],
[[../Verification/2026-10-21 Data Management Foundation]].

## Scope and authority

This session extends the reviewed D0-D6 game-data foundation under the P1-P7 plan
and its 2026-10-22 full SQLite cutover amendment. The deliverable is SQL runtime
authority for validated authored content/embeddings, Topic history, conversation
sessions/repetition and transcripts, plus private generated archive records. JSON
remains retained migration evidence and explicit import/export input; SQLite is the
editable authoring authority and normal runtime readers consume validated SQL
releases. Runtime read/write paths must not use a permanent dual store or silent
fallback.

Explicit exclusions remain: merging personal memory or credentials; account/mobile/
cloud/billing; upload endpoint, training job, provider/TTS spend, new inventory or
Action executor, and new stat algorithm. Python continues to own player-memory
consent/suppression; DPAPI/environment continues to own credentials. Recording
begins disabled and has no backfill. A local export is not delivery, and evaluation
permission is not training permission.

The physical direction and fixed caps are in the implementation plan. The ERD is
the logical relationship authority. If a source format cannot be represented without
loss, the compiler must return named diagnostics and preserve its original bytes/hash
in staging; it must not silently publish a partial projection.

## P0 baseline and current owners

### Existing game SQLite authority

`Assets/AlterOS/Persistence/SqliteGameRepository.cs` and
`GameSchemaMigrations.cs` currently expose schema version 1 with:

- `game_meta`, `game_snapshots`, and `home_layout_snapshots`;
- the sole `game_events` writer;
- `operation_receipts`, `migration_receipts`, and `legacy_source_backups`.

`GameCommitRequest` commits optional progression/layout snapshots, existing
`GameEventRecord` rows, operation receipt and migration-source evidence in one
transaction. V1 request hashes and receipt identity are compatibility-critical.
`AlteraProgressionStore` imports existing schema-4 progression JSON once and owns
validated progression operations/checkpoints. `FurniturePersistenceAdapter` reuses
the existing layout DTO/repository, preserving layout revisions and exact poses.
`GameBackupService` checkpoints only the game database; schema-2 validation must
preserve v1 backups and upgrade staged copies before recovery replacement.

No relational content graph, run history, stat-change causality or optional recording
tables exist in D0-D6.

### Authored content and current adapters

| Existing owner | Current source/runtime role | Integration boundary |
| --- | --- | --- |
| `Assets/StreamingAssets/Dialogue/Generic/<routeId>/` | 161 retained `DialogueDefinition` JSONs across 57 route folders; route IDs and hashes remain import/provenance evidence | The explicit authoring import captured these exact source bytes in SQLite. Edit through the authoring repository; do not dual-edit or use this legacy tree as the Editor build source. |
| `Assets/StreamingAssets/Dialogue/Topics/topics/` | Retained canonical `TopicDefinition` source files, currently 57; migration map and coverage are validated during import/build | The compiled SQL release is the runtime source. Preserve Topic/scenario IDs, conditions, channels, modifiers, weights and consumption semantics. |
| Logical release document `topic/activity_scenarios.json` (import source: `Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json`) | Two host-authored `AuthoredActivityScenario` entries, each wrapping an executable `ScenarioDefinition` with registered capability IDs | Runtime reads this document from `RuntimeContentStore`; the legacy JSON remains import/test evidence. Preserve host-authored wrapper/permission semantics; content cannot create a new executor. |
| Legacy `Assets/StreamingAssets/Dialogue/<category>/` JSONs | Existing `DialogueSequence`, ambient and test formats consumed by `DialogueRouter`/`DialogueManager` | Explicit adapters only; preserve current wire/provider formats and distinguish executable Scenario from `TopicScenario`. |
| `Assets/AlterOS/Dialogue/DialogueValidator.cs` | Canonical dialogue schema validator | Reuse validation and report rejected/unknown fields instead of dropping them. |
| `Assets/AlterOS/Topics/TopicDefinitionValidator.cs`, `TopicConditionEvaluator.cs` | Typed Topic/Rule-like tree validation with true/false/unknown and diagnostics | P3 adapter reuses current supported leaf semantics; unknown remains denied/unknown. |
| `Assets/AlterOS/Scenarios/ScenarioDefinitionValidator.cs`, `ScenarioSession.cs`, `AuthoredScenarioAdapter.cs` | Validated executable graph and existing one-running/one-suspended lifecycle; Topic conversion is an explicit adapter | Extend this runtime boundary only; no parallel live manager, auto-resume, or new executors. |
| `Assets/AlterOS/Activities/ScenarioActivityPort.cs` and registered activity hosts | Host checks capabilities, arguments, targets, acceptance and completion | Keep runtime permission revalidation even after compile-time validation. |
| `Assets/AlterOS/Editor/TopicPlaygroundSession.cs` and `Tools/TopicPlayground/` | Detached/local authoring and synthetic preview/CLI | Use trusted catalogue/validator entry points when possible; do not overwrite the separate Content Workbench UI/server. |

### Existing semantic, history, and privacy owners

- `RuntimeContentStore` loads authored documents and baked semantic vectors from the
  pinned SQL release. GTE model/tokenizer remain Unity `Resources`; preserve current
  profile compatibility, ranking, tie behavior, candidate IDs, RAG gate and authored
  fallback. No runtime rebake or paid inference was used for the staged artifact.
- `TopicHistoryRepository` is now a legacy/editor codec; production Topic history
  uses game schema 3. The canonical game DB is
  `<persistentDataPath>/Data/alteros-game.sqlite3`; startup promotes any earlier
  root-level Topic SQL receipt/rows into it before importing legacy JSON. `ConversationSessionStore`
  and `ConversationTranscript` use schema 2 in the private conversations DB; JSON and
  JSONL are import-only. `GeneratedDialogueArchive` uses private content schema 1.
  `DirectConversationBridge` retains opaque PlayerPrefs IDs and connects these owners.
- `Tools/CompanionRuntime/.../memory_repository.py` owns the separate Python SQLite
  player-memory database and its consent/forget policy. Provider credentials remain
  in DPAPI/environment storage. Neither domain is read or merged here.
- Optional `recording_sessions`, `interaction_records`, `contributions` and
  `contribution_records` are new and disabled by default. They cannot replace required
  `game_events`, memory or transcript ownership.

The full cutover changes storage without changing logical owners: authored content and
vectors use a compiled read-only SQL release; Topic history shares game schema 3;
sessions/repetition/transcript use a separate private conversations database; generated
archive metadata/payloads use separate private SQL. The runtime migration journal records
independent owner receipts; it does not provide cross-database ACID. The coordinator
remains sole transcript writer, Topic runtime owns consumption, the game host owns
effects, and Python remains the memory consent/suppression authority. No memory or
credential merge is included.

## Baseline checks (refreshed 2026-10-22)

| Existing check | Result |
| --- | --- |
| `Tools/Persistence/Check-GamePersistence.ps1 -Group All` | Exit 0; 15/15 synthetic checks passed using Unity 6000.4.4f1 bundled Mono, SQLite-net and the Windows x64 native plugin. Includes Topic root-DB promotion, game schema-1 upgrade, archive/session/transcript imports and lost-target recovery. |
| `Tools/Persistence/Check-ProgressionSourceCompile.ps1` | Exit 0; persistence/progression/dialogue/bridge source compile plus schema, effect and source trust-policy probes. Existing source-list/type-conflict warnings remain. |
| `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` | Exit 0; 67 passed / 0 failed. |
| `Tools/TopicRuntime/Check-TopicRuntime.ps1` | Exit 0; 33 passed / 0 failed. |
| `Tools/TopicPlayground/Check-TopicPlayground.ps1` | Exit 0; 13 passed / 0 failed; Editor draw-precision source check passed. |
| `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` | Exit 0; 52 passed / 0 failed; portable assembly has no Unity references. |
| `Tools/ContentData/Check-ContentData.ps1 -Group Repository` | Exit 0; 8 passed / 0 failed against the installed binding. |
| `Tools/ContentData/Check-ContentData.ps1 -Group Bake` | Exit 0; 11 passed / 0 failed, including full 439-source file/SQL eligibility parity for 57 routes across 24 local hours. |
| `Tools/ContentData/Check-ContentData.ps1 -Group InspectArtifact -ArtifactPath <staging-db>` | Exit 0; release source/hash/dependency closure reopened; 6,041 dependencies, 673 embeddings, 57 routes at 768 dimensions; SQLite integrity `ok`, 0 FK violations. |

Complete fresh output logs are under `.superpowers/sdd/Content Data Integration
Implementation Plan/` using the `F0-...-2026-10-22.log`, `P1-Final-Repository-2026-10-22.log`,
`P2-Final-Bake-2026-10-22.log`, and authored-corpus bake/inspect names.

Unity MCP previously listed one running instance `alteros@48a268a4`, matching this
project and Unity 6000.4.4f1. On 2026-10-22, fresh read-only requests for the instance
list, project info and editor state did not return within roughly 40 seconds and were
stopped. At this baseline checkpoint no Editor action was issued, no scene was saved
or discarded, and the Editor was not closed. A later authorized source compile and the
current acceptance gates are recorded under Current status and next step; headless
results alone do not imply Unity acceptance.

The checkout has no resolvable Git `HEAD`; `git status` reports all project files as
untracked. To preserve the existing D0-D6 foundation, no reset/stage/commit/worktree
copy will be used. The explicit changed-file ledger below, this plan's scratch ledger,
and source review are the evidence for this run; there is no reliable Git diff base.

## Review-focus risks

1. Shared revisions must rebuild dependent callers while active runs keep their
   immutable pinned closure.
2. Subflow visits need separate invocation/step IDs and isolated variables/results;
   retries must not duplicate physical or stat effects.
3. Opt-out/delete must invalidate unsent contribution snapshots without touching
   required gameplay history or sending optional data to a model/provider.
4. Cross-store references must name exact store/ID/revision/hash and fail closed on
   missing or incompatible vectors; never fall through to another user or latest.
5. Schema-2 migration/recovery must preserve v1 snapshots/events/receipt hashes,
   backups and old requests while staging all upgrades before replacement.

## Changed-file ledger

| Phase | Files/areas changed | Evidence |
| --- | --- | --- |
| P0/F0 | This handoff; ignored plan ledger/logs under `.superpowers/sdd/` | Six refreshed baseline suites above; Unity state probe timed out. |
| P1 | `Assets/AlterOS/Content/ContentContracts.cs`, `ContentSchema.cs`, `ContentRepository.cs`; `Tools/ContentData/Check-ContentData.ps1`, `HeadlessContentDataTests.cs` | Real-binding repository suite: 8/8; exact command and log above. |
| P2 | `LegacyContentAdapter.cs`, `ContentLanguageResolver.cs`, `ContentReleaseCompiler.cs`, `ContentBakeService.cs`; `Tools/ContentData/contentdata.ps1` and expanded harness; P1 schema/repository extensions | Bake 7/7, repository 8/8; full authored staging artifact 9,678,848 bytes; InspectArtifact verified 4,689 dependencies. Activity wrappers and other legacy formats remain F1 gaps. |
| P3 | `ContentPackageValidator.cs`, `ContentRuleEvaluator.cs`, `ContentReleaseCompiler.cs`, `ContentRepository.cs`, `ContentContracts.cs`, `ContentSchema.cs`; `Tools/ContentData/HeadlessContentDataTests.cs` | Publish 6/6, Repository 8/8, Bake 7/7 against schema v2; exact logs under `.superpowers/sdd/Content Data Integration Implementation Plan/`. Same-path source revisions and CAS heads covered. |
| P4 | `FlowValue.cs`, `FlowBindingEvaluator.cs`, `ContentFlowAdapter.cs`, `ContentFlowExecutionState.cs`, `ScenarioSession.cs`, `ScenarioDefinition.cs`, `ScenarioDefinitionValidator.cs`, `ScenarioConversationPresentationModel.cs`, `DirectConversationBridge.cs`, `LegacyContentAdapter.cs`, `ContentLanguageResolver.cs`, and `Tools/ContentData/HeadlessContentDataTests.cs` | Flows 6/6; ScenarioRuntime 67/67; TopicRuntime 33/33; Furniture 52/52; Publish 6/6; Repository 8/8; Bake 7/7. Headless/source evidence only. |
| P5 | `ContentSemanticIndex.cs`, `GteSemanticAssetImporter.cs`, `ContentRepository.cs`, `ContentPackageValidator.cs`, `ContentReleaseCompiler.cs`, `ContentContracts.cs`, `Tools/ContentData/Check-ContentData.ps1`, and `HeadlessContentDataTests.cs` | Semantics 3/3; Publish 6/6; synthetic source/hash, vector validation, GTE ranking, local-anchor and exact-snapshot publication checks; local idempotent job receipts. No production route switch. |
| P6 | `GameHistoryContracts.cs`, `SqliteGameRepository.cs`, `GameSchemaMigrations.cs`, progression/Scenario hooks and headless harnesses | Schema 1 upgrade, atomic schema 2 history writes, bounded readers, receipt hash compatibility, v1 backup staging and typed Scenario/stat fixtures. No Editor/player acceptance. |
| P7 | `Assets/AlterOS/Recording/`, Scenario and DirectConversationBridge opt-in hooks, `Tools/Recording/README.md`, headless harnesses | Explicit opt-in local store, bounded capture, immutable preview/confirm/export; synthetic Recording group and Scenario Flows 7/7. No UI or transport. |
| F1 | Content compiler/adapters, runtime store, production readers and `Tools/ContentData/` | Fresh 439-source artifact staged at `content-full-cutover-2026-10-22.sqlite3`; 57 Topics, 322 Flows, 928 Dialogues, 1,258 Texts, 6,041 dependencies, 673 embeddings. Inspector verifies exact release/build/source hashes, 57-route/768-dimension index, SQLite integrity and 0 FK violations. Editor import remains open. |
| F2 | Game schema 3, `SqliteTopicHistoryStore`, JSON/SQL relocation importers, `DirectConversationBridge` | Topic v1/v2 imports, receipts, bytes and operation revisions pass; old root-level Topic SQL promotes into the shared `Data/alteros-game.sqlite3` store without changing the source file. Persistence suite includes schema-1 game upgrade. |
| F3/F4 | `SqliteConversationStore`, session/transcript importers, facades and coordinator | Conversation schema 2 stores sessions, bounded turns, repetition, ambient state, transcript events and chunked JSONL source backup. Receipt-first retries, edited source, interleaved writes, duplicate event identity, read-only failure and paging pass. |
| F5 | `GeneratedDialogueArchive`, `GeneratedArchiveImporter`, private archive schema 1 | Stable IDs/aliases, manifest/payload bytes and hashes, unsafe/missing/invalid payload diagnostics, unvalidated Ready state, rollback/retry and SQL runtime writes pass synthetic checks. No imported payload becomes routable from status alone. |
| F6 | `RuntimeMigrationCoordinator`, migration manifest, `RuntimeMigrationStartup` and private-store startup facades | Synthetic coordinator covers schema-1 game upgrade, Topic SQL promotion, Topic JSON fallback, sessions, transcript, generated archive, identity aliases, crash/resume, changed target identity, committed shared-target loss and restart without legacy reread. Unity startup execution remains unverified. |
| F7 | `Tools/ContentData`, persistence, conversation, Topic, Scenario, Playground and furniture harnesses | Persistence15/15, unified197/197, Companion Runtime171 passed/2 skipped, Topic33/33, Playground13/13, Scenario67/67, furniture52/52; ContentData Repository8/8, Bake11/11, Publish6/6, Flows7/7, Semantics4/4; full file/SQL route parity 57×24. Unity import/player acceptance remains open. |

## P1 repository result (2026-10-22)

`Assets/AlterOS/Content/ContentContracts.cs`, `ContentSchema.cs`, and
`ContentRepository.cs` initially provided the version-1 content store schema, immutable
typed references, package staging, release reads, and fail-closed validation. The
P1 schema began with 25 focused relational tables; P2 added `legacy_sources` and
normalized `release_dependencies`, bringing it to 27 tables. P3 raised the current
schema to version 2 for same-path source revision retention.
Topic entries preserve channels and consumption checkpoints; Ask Steps can pin their
question Dialogue. Row-level persistence hashes supplement semantic revision hashes
so reusing an ID/revision with different stored fields is rejected.

`Tools/ContentData/Check-ContentData.ps1 -Group Repository` compiled the full
Content source set against the installed SQLite-net binding and ran its synthetic
temporary-store harness through the child PowerShell host. Result: exit 0, 8 passed,
0 failed. Full output is retained at
`.superpowers/sdd/Content Data Integration Implementation Plan/P1-Final-Repository-2026-10-22.log`.

## P2 legacy bake and localization result (2026-10-22)

`LegacyContentAdapter.Import` reads only explicit source-manifest entries under a
trusted root, verifies byte hashes, rejects traversal/reparse paths and unknown JSON
fields, then projects Generic DialogueDefinition, canonical TopicDefinition/
TopicScenario, and executable ScenarioDefinition records. It preserves source IDs,
paths/hashes, route metadata, replay/cooldown/conditions/modifiers/channels/consumption,
flow transitions/choices, Text, presentation, and host capability strings as data;
SQL presence does not grant an Action permission.

`ContentLanguageResolver` maps `english`/`japanese` to `en`/`ja`, tries English for a
missing display translation, reports unavailable when English is absent, and leaves
speech blank when its requested language is missing. `ContentReleaseCompiler` returns
an immutable release candidate and deterministic fingerprints; it does not publish a
head. `ContentBakeService` and `Tools/ContentData/contentdata.ps1 bake --manifest
<trusted-manifest> --output <staging-db>` create a new staging artifact outside
StreamingAssets and the source root, preserve existing outputs, and stage only after
import/compile succeeds.

The full authored bake covered 218 explicitly named files: 161 Generic definitions
across 57 routes and 57 promoted Topic definitions. It produced 57 Topics, 322 Flows,
928 Dialogues, 1,258 Texts, 673 Anchors, and 4,689 exact release-dependency rows in a
9,678,848-byte staging database. The `Bake` harness passed 7/7; repository passed 8/8;
`InspectArtifact` reopened the complete corpus, verified release hashes/dependency
closure, SQLite integrity, and foreign keys (0 violations).

Two executable activity Scenarios in `Dialogue/topic/activity_scenarios.json`,
`migration-map.json`/coverage metadata, legacy DialogueSequence/ambient/greeting/menu/
bark/system/test formats, and baked GTE vectors were not part of this P2 manifest. F1
must inventory and project those consumers before runtime SQL cutover. The staged
artifact is not shipped at `StreamingAssets/Content/content.sqlite3`, and no production
route switch or Unity acceptance is claimed.

## P3 typed validation, closure and publication result (2026-10-22)

`ContentPackageValidator` now validates exact local typed references, registered Action
capabilities and expressions, Topic conditions/channels/modifiers, anchor owners,
shared Rule trees, Flow parameters/locals/bindings, named Subflow exits, and bounded
recursive graphs. Rule evaluation reuses `TopicConditionEvaluator`; missing or
unregistered facts remain `Unknown`, and choice projection fails closed to hidden
unless the host proves availability. Action bindings/results use bounded typed
contract members; result reads require the source Action Step to dominate the consumer.

`ContentReleaseCompiler.ResolveDependencies(package, hostVocabulary)` returns the
validated exact package closure. The validated Compile overload stamps the release
with the current validator version; `ContentRepository.Publish` requires that
host-compiled object and the immutable semantic snapshot loaded for the exact release
and index fingerprint, checks the staged release closure, and updates `release_heads`
with compare-and-swap. `ReadRelease` verifies release-row, header, dependency-row and
complete graph hashes. Stale heads, missing/incompatible releases and unavailable
semantic snapshots leave the prior head untouched. Semantic readiness is separate
from TTS readiness.

The same-path revision test stages two hashes at one source path, preserves both
`legacy_sources` mappings, and confirms the first release still pins its original Flow
revision. The schema was raised from v1 to v2 for that key change; old staged v1
artifacts remain untouched and are explicitly incompatible, so F1 must rebuild them.
No persistent runtime content artifact was replaced.

Fresh results on 2026-10-22: `-Group Publish` exit 0, 6 passed; `-Group Repository`
exit 0, 8 passed; `-Group Bake` exit 0, 7 passed. Logs:
`P3-Publish-GREEN-2026-10-22.log`, `P3-Repository-GREEN-2026-10-22.log`, and
`P3-Bake-GREEN-2026-10-22.log` under the plan workspace. These compile and exercise
the real installed SQLite-net binding with synthetic temporary stores; Unity import,
Editor tests, and player acceptance remain separate.

## P4 typed Subflow execution result (2026-10-22)

`ContentFlowAdapter.Resolve` verifies the exact released closure and snapshots the
validated Flow records. `Prepare` checks typed input values and returns the pinned
execution plan. The existing `ScenarioSession` now owns the execution frames and
projects one current Step at a time through its existing presenter and activity
contracts. Frame locals and Action results stay per invocation; Action requests still
pass the existing host capability checks at runtime. Named Subflow outputs map
atomically into mutable caller locals; cancelled exits preserve defaults. Each Step
visit gets a unique execution identity while retaining its authored `sourceStepId`.

Fresh source checks on 2026-10-22: Flows exit 0 (**6/6**), ScenarioRuntime exit 0
(**67/67**), TopicRuntime exit 0 (**33/33**), Furniture exit 0 (**52/52** and portable
assembly has no Unity references), Publish exit 0 (**6/6**), Repository exit 0
(**8/8**), and Bake exit 0 (**7/7**). The Flow group covers typed entity validation,
shared Dialogue with independent continuations, independent invocation inputs,
accepted/declined/cancelled nested returns, required and typed Action results, result
binding, duplicate delivery, suspension/resume, and Quit. Logs are retained as
`P4-Final-*-2026-10-22.log` plus the latest expanded Flow run
`P4-Flows-FrameIsolation-GREEN-2026-10-22.log` in the ignored plan workspace.

The first fresh Bake rerun failed because the P4 resolver overload made an old
unqualified reflection lookup ambiguous; the harness now selects the exact
repository overload. The rerun and final Bake check pass. This remains headless/source
evidence: Unity import, Editor tests and SplashScene → ModelTest acceptance are not
verified. Production content still does not select SQLite as its runtime authority.

## P5 semantic index and incremental embedding result (2026-10-22)

`GteSemanticAssetImporter.ImportInto` imports the existing schema-v1 metadata and
little-endian float32 vectors after checking the expected source-content hash, model
identity, vector length, profile, finite values, normalization, host route vocabulary,
and text-addressed Anchor ownership. It reads supplied bytes only; it runs no model or
inference. `ContentRepository.StageEmbedding` validates the exact local Anchor source
hash and immutable profile before adding one vector. Supplied content work can be
recorded through the local `jobs` table with idempotent `StageJob`/`CompleteJob`/`ReadJob`
calls; no provider worker or automatic job runner was added.

`ContentSemanticIndex.Load(repository, release, profileId)` reloads the exact immutable
release from SQL and checks every Anchor, Anchor link, embedding profile, vector hash,
dimension, normalization rule, and index fingerprint. It creates an immutable shared
array snapshot for the existing route score and Topic-family matcher. The snapshot
keeps route/text links and local-only Topic anchors; caller vector access returns a
copy. `ContentRepository.Publish` now requires this exact loaded snapshot, so a SQL
release head cannot imply semantic readiness or TTS readiness. The existing `jobs`
table now has local idempotent StageJob/CompleteJob/ReadJob APIs for host-supplied
Topic/embedding work. Those records do not run a worker or grant routing permission.

The existing baked GTE assets produced **673 unique Anchors**, **678 route links**,
**57 routes**, and **768 dimensions**; the vector payload is 2,082,816 bytes and the
metadata is 56,694 bytes. One repeated `still awake` text has two slightly different
float payloads (cosine **0.999999461** and source-profile norms 1.0000356 / 0.9999529).
The importer stores the first metadata occurrence for the shared Anchor and rejects
repeated vectors below cosine 0.99999. This preserves exact candidate order on the
current corpus; the maximum route score delta measured against the old file vectors is
**0.000022650**, below the 0.000030 fixture tolerance. Four other repeated texts have
byte-identical vectors. The audit is retained at
`P5-GTE-DuplicateVectors-2026-10-22.log`.

Fresh checks: `-Group Semantics` exit 0 (**3/3**) and `-Group Publish` exit 0 (**6/6**).
The same synthetic corpus benchmark used 15 repeated samples after the first process
open. First database open was **4.297 ms**; warm open p50/p95 **3.300/4.566 ms**;
file index first/warm load **27.082 / 23.076/40.957 ms**; SQL warm index load
**81.418/116.725 ms**; file/SQL exact-ID lookup **0.066/2.395 vs 0.048/0.380 μs**;
file/SQL route rank **0.763/5.745 vs 1.600/1.869 ms**; retained managed-memory
delta **2,325,544 vs 2,926,080 bytes**. SQL loading and ranking are slower for this
corpus; the transfer makes no speed claim. The operating-system page cache was not
reset, so these are first-process and warm measurements rather than cold-disk results;
managed-memory deltas are not process peak measurements. Full output is in
`P5-Semantics-Jobs-GREEN-2026-10-22.log`.

The current `GteSemanticRuntime` and live router still use the Resources path. The
staged schema-v1 artifact was not replaced, no v2 full-corpus SQL artifact was built
or shipped, and no Unity import/compile, Editor tests, player route parity or
SplashScene → ModelTest acceptance ran. F1 must rebuild the old artifact into a new
schema-v2 output, wire this importer into the Editor build path, and switch production
consumers only after full format coverage and parity.

## P6 additive history and stat causality (2026-10-22)

Game schema 2 upgrades schema-1 databases atomically and extends the existing game
transaction/writer. New physical tables include `scenario_runs`, `flow_invocations`,
`step_executions`, `variable_values`, `action_executions`, `action_checkpoints`,
`event_context`, `stat_changes`, and `answers`. `variable_values` remains empty; no
runtime locals are written or restored. History rows use typed owner/cause fields,
stable sequences and non-cascading references. Free-text answers are represented only
by their kind; accepted choice IDs may be retained.

`GameHistoryReader` pages events, stat changes and answers with a maximum page size of
128. Schema-1 request receipt hashes remain unchanged for requests with no new history
fields. History-bearing hashes are versioned, and a retained base hash allows an older
request shape to replay its receipt. The same commit stores state, events, context,
actual stat values/deltas, execution history and receipts. Supported source boundaries
now include dialogue stat changes, activity hydration, aggregated needs checkpoints,
and best-effort Scenario presentation callbacks. Optional callback failures do not stop
Scenario progression; effect-bearing commits require their history in the same
transaction.

Schema-1 databases migrate on open. A synthetic DDL collision proves migration rollback
leaves version 1 with no partial history tables. Schema-1 backups validate without
mutation, and recovery upgrades a copied staging file before replacement. The original
backup remains byte-for-byte unchanged.

Fresh checks: `Tools/Persistence/Check-GamePersistence.ps1` **8/8** (including v1
migration, receipt replay, rollback, backup restore, history paging, causality and stat
validation); `Check-ProgressionSourceCompile.ps1` completed with 0 errors / 52 warnings;
`Tools/ContentData/Check-ContentData.ps1 -Group Flows` **7/7** including Scenario
history observer deduplication/failure isolation. Logs: `P6-GamePersistence-Final-GREEN-2026-10-22.log`,
`P6-Progression-Final-GREEN-2026-10-22.log`, and `P6-Flows-Final-GREEN-2026-10-22.log`.
No Unity import, Editor tests, Play Mode, player acceptance or real-profile migration
was run.

## P7 consent-bound local recording (2026-10-22)

The private recording store is separate from game history and opens only after a valid
policy is explicitly passed to `InteractionRecorder.Enable`. Construction/default state
does not create a database. Metadata and raw-text scopes are independent; only bounded,
known record kinds/fields are accepted. The recorder applies per-session time, byte and
record caps, prunes expired rows, and Disable invalidates unsent previews/batches.
Scenario hooks are caller-registered only and record presented options, chosen answers,
opted-in submitted text, acknowledged beats, Action outcomes and route category. No
recorder instance is assigned or enabled by startup.

The contribution preview freezes membership and allowlisted projections under a content
hash. Confirmation requires the same hash and unchanged source rows. Export writes only
the confirmed local snapshot to a new trusted path; it does not mark delivery or training
permission. Delete/prune invalidates affected unsent batches and never deletes game
events. The database contains no sender or server receipt authority.

Fresh checks: `Check-GamePersistence.ps1 -Group Recording` passed; the Flows suite passed
**7/7**, including option/answer/beat recording callbacks and observer-failure isolation.
See [the recording README](../../Tools/Recording/README.md). Player-facing Canvas opt-in
and Contribute UI, network transport, server receipt/withdrawal policy, Unity tests and
player acceptance remain unimplemented.

## Current status and next step

P0, P1, F0, P2, P3, P4, P5, P6, P7 and F1-F6 are source/headless complete. F7
source verification and documentation are complete. The root-level Topic SQL store
is promoted into the shared `<persistentDataPath>/Data/alteros-game.sqlite3` database;
the canonical database remains the progression/home/Topic owner. Sessions/transcript,
generated archive and the migration journal are separate private SQL stores. Memory
and credentials remain separate owners.

The current 439-source SQL release is installed at
`Assets/StreamingAssets/Content/content.sqlite3`. Read-only integrity, foreign-key
and trusted-release inspection passed on 2026-10-22, and the release-manifest
snapshot hash matches authoring revision 63. See
[[../Verification/2026-10-22 Full SQLite Cutover]] for the release ID and hashes.
Unity source import and script compilation passed on 2026-10-22 with zero error entries in the latest
error-filtered Console query. The first focused EditMode job executed 28 tests and
reported seven fixture failures; the test-only lifecycle setup has been corrected and
awaits coordinator rerun. A later combined conversation EditMode job
`37e843576850489aa8b2fd6d0caeec12` stalled; test-job polling lost its connection
(`WinError 10053`) and no result is available. The deferred tests now use async NUnit
methods with bounded waits; the BCL/fake-host coordinator class passes 30/30 through
the headless runner. The user's subsequent Editor build failed closed at
`expression_not_registered`. The selected SQLite content is now corrected: 104
compiled expression fields across 61 documents map to the 12 actual registered
presets, and current-export headless checks pass. The local store is revision 63 at
`Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`.
The GTE metadata fingerprint matches that export; vectors/tokenizer and retained
legacy JSON were not changed. The earlier staged artifact predates this correction
and is historical, not the installed release. The installed artifact was inspected
read-only and matches the corrected revision-63 snapshot. Normal startup/player
acceptance and real-profile migration are still unverified. The initial mismatch was 104 expression fields across 61 selected docs
using 12 names absent from the actual preset catalog (`angry`, `focused`, `gentle`,
`happy`, `neutral`, `playful`, `sad`, `shy`, `soft`, `surprised`, `thinking`, `warm`).
The chosen mappings are recorded in [[../Verification/2026-10-22 SQLite Authoring Core]].
Retained legacy JSON and its dated route snapshots intentionally preserve the
pre-cutover names as import evidence. No real profile, key, provider or online
generation was used. Do not equate headless/source compile evidence with Unity test
or player acceptance.
