---
title: Content Data Integration Implementation Plan
date: 2026-10-21
tags: [alteros, implementation, sqlite, dialogue, erd]
status: delegated-for-continuous-implementation
---
# Content Data Integration Implementation Plan

> [!important] Full runtime migration amendment2026-10-22
> The human explicitly approved chat transcript, session/repetition and Topic-history
> migration as part of full SQLite cutover. Read [[Full SQLite Cutover Implementation Plan]]
> first: it supersedes this plan's old additive-only/optional-runtime/no-history-migration
> exclusions and replaces P8's keep-default-legacy requirement. Retain P1-P7 technical
> tasks and P9 evidence; execute the amendment's ordered F tasks without restarting
> completed work. Personal-memory SQLite and secure credentials stay separately owned.

> **For agentic workers:** REQUIRED SUB-SKILL: executing-plans. The human chose the
> existing Database Management System session as implementer. Execute the ordered
> tasks continuously, recording evidence and reporting at the end, not another
> product-planning round. Subagents only where the human or applicable skill permits.

**Goal:** Implement the approved content/execution/history ERD as a tested additive
SQLite integration, plus a disabled-by-default local recording/contribution framework.

**Architecture:** Reuse the D0-D6 game repository, operation receipts, identity,
backup and sole game-event writer. Separate versioned authored/personal content
stores from game state; compile existing JSON into a relational projection and
resolve immutable releases into the existing Scenario/Activity host boundary.
No wholesale merge of SQLite, JSONL, PlayerPrefs or secure-storage purposes.

**Tech Stack:** C#, Unity6000.4.4f1, installed SQLite-net1.3.2 pinned to
08248bd5884d8eb932a837aa56d4ff456daf913f; existing PowerShell/Mono source harnesses.
No new database/vector runtime/agent framework/package.

**Spec:** [[Content and Interaction ERD]] and [[Dialogue and Semantic Storage Design]].
Foundation evidence: [[Data Management Handoff]],
[[../Verification/2026-10-21 Data Management Foundation]].

## Global constraints

- Work in E:\Pleiades\alteros; preserve concurrent edits, IDs and prior contracts.
- Inspect fresh source before each integration; original report is not a clean Git
  baseline. Do not bulk stage/reset, overwrite shared work, or edit Unity YAML/meta.
- No real player DB/profile/credential access or provider/model/TTS spending. Use
  synthetic roots for all tests, import/migration and gameplay verification.
- No memory/transcript/Topic-history/PlayerPrefs migration, account/mobile/cloud,
  transport endpoint, training job, new gameplay Action, new stat rates or inventory.
- Existing saved key is not online-generation/capture/contribution consent.
- No player capture enabled by default or retroactive history extraction. A new
  consent service cannot reuse memory consent as recording/contribution permission.
- This phase originally kept JSON/Workbench as editable input and SQL as its
  projection. The approved 2026-10-22 authoring target is now SQL-only editing and
  its Workbench/Editor cutover is implemented; see
  [[SQLite Authoring Cutover Implementation Plan]]. Preserve the separate runtime
  release, game-history and private-data ownership described by this plan.
- Shared schema and existing public/provider formats remain supported via adapters;
  never reinterpret TopicScenario as executable ScenarioDefinition.
- Registered game executors and existing permission boundaries remain authority.
  Content/model output never mints currency, grants tools or supplies arbitrary effects.
- Preserve whole-Topic occurrence/replay semantics, one running/one suspended slot,
  no automatic Scenario resume after quit and real checkpoint-based Action outcomes.
- Static UI is Canvas/prefab authored. This run adds no new product UI: recording
  opt-in/preview/export is tested through bounded headless APIs. Player-facing settings
  and Contribute button are a separately gated Canvas integration, not falsely done.
- Production routing stays on its existing selected path until parity and Unity
  import/runtime acceptance. New path must be explicitly injectable for fixtures;
  do not silently switch default source from headless success alone.

## Review focus

1. Shared revisions: dependency closure rebuilds; active runs never see new definitions.
2. Subflow loops/retries: each visit has separate invocation/execution identity,
   no variable or Action result leakage and no doubled physical/stat effects.
3. Privacy races: opt-out/delete invalidates pending contribution bytes; required
   game history remains intact and no model/provider receives optional data implicitly.
4. Mixed stores: missing/wrong-revision/wrong-model references fail closed, never
   fall through to latest content, another user's DB or an incompatible vector space.
5. Migration/recovery: new game schema preserves v1 snapshots/receipts/events and
   backs up consistently; optional journals/content require separate recovery policy.

## Delivery boundaries and acceptance

START sent2026-10-21 to Database Management System thread
01a15e76-daeb-7451-982f-95a510093fd3 (local). The worker retains its GPT-6 Luna
extra-high settings. Coordinator verified report-back thread identity; task delivery
was acknowledged by the app. No implementation pass is implied by successful dispatch.

One continuous run implements P0-P9 below, with TDD per task and a final report.
No pause for routine progress approval. Local implementation decisions follow the
fixed contracts below and are recorded in the handoff. Stop only for a genuine
permission/security/destructive-data blocker or irreconcilable contract conflict.
If Unity is stale, finish independent source/headless work, report failed/unrun
Unity gates and leave production cutover disabled; do not force-close the Editor.

The deliverable is the local tested model/repositories/compiler/runtime adapter,
not live paid generation or a contribution backend. Distinguish implementation,
live wiring, production cutover and player acceptance in the report.

## Fixed physical direction for this run

### Stores and versions

- Game DB remains Data/alteros-game.sqlite3; migrate schema1 -> 2 atomically after
  a consistent backup. Keep all existing tables and GameEventRecord fields/API.
- Compiled authored output: caller-supplied staging path outside StreamingAssets,
  read-only at runtime. Personal content path: Data/personal-content.sqlite3 under
  host-supplied trusted persistent root. New content schema starts at1.
- Optional recording DB: Data/interaction-recording.sqlite3, lazy-created only by
  explicit recording enable, not merely opening a save. New schema1. No device sync.
- Opening a future/corrupt store never deletes/recreates it or silently imports defaults.
  UTF-8 paths and foreign_keys=ON on every connection; one serialized writer per store.
- Content definitions use `(id TEXT, revision TEXT)` PK; revision is SHA-256 of a
  canonical, sorted semantic payload/dependency references, excluding timestamps.
  IDs stay stable/namespaced; do not invent replacements for legacy identities.
- Runtime IDs are host UUIDs; opaque existing turn/Topic IDs retain explicit mappings.
  Values are bounded discriminated records: missing/value/unknown, then supported
  bool/int64/finite-double/text/enum/entity-reference. Entity kind and ID are validated.

### Content tables and references

Keep focused Content/ files, not one giant repository. SQL CHECK/FK constraints plus
host schema validation are required; polymorphic binding/target slots use exactly-one
typed source/target checks, not free-form executable strings.

| Table | Primary key / required relationships |
| --- | --- |
| metadata | key; schema/build identity, no dialogue or personal content |
| topics | id, revision; stable route mapping, title/prompt Text refs, replay/cooldown |
| topic_entries | topic_id, topic_revision, entry_id; exact Flow ref, priority/weight/modifiers, condition |
| flows | id, revision; kind=scenario/subflow, entry_step_id |
| steps | flow_id, flow_revision, step_id; kind, mutually exclusive Dialogue/Action/called-Flow refs and existing kind-specific data |
| transitions | Flow key, transition_id; source/target within same Flow revision; named purpose |
| choices | Flow key, Step ID, choice_id; parent ask Step, outgoing transition, answer value, Text/hint refs and availability condition |
| dialogues | id, revision; reusable sequence only |
| beats | Dialogue key, beat_id; unique order, Text ref and supported expression/presentation |
| texts | id, revision; immutable shared text identity |
| translations | Text key, language; text value, unique owner/language |
| actions | id, revision; registered capability key, versioned bounded typed input/result/requirement contract JSON |
| rules | id, revision; bounded typed all/any/not tree or supported existing leaf; no script/SQL |
| flow_parameters | Flow key, direction, name; supported type/required/default, names unambiguous |
| flow_variables | Flow key, name; supported type/default, no parameter collision |
| step_bindings | Flow key, Step ID, destination parameter; exactly one literal/local/input/host-fact/earlier-result typed source |
| flow_exits | Subflow key, exit_name; output contract |
| exit_mappings | Caller Flow/Step key, exit_name; exact caller transition and validated output-to-local mapping |
| anchors | immutable anchor_id; language/text/text hash; no private chat embedding by default |
| anchor_links | link_id; anchor and exactly one supported typed owner/revision with role |
| embedding_profiles | profile_id; exact model/tokenizer/pipeline hash, dimensions/dtype/normalization/metric |
| embeddings | anchor_id, profile_id; finite float32 little-endian BLOB, vector hash |
| releases | release_id; immutable typed dependency manifest JSON/hash, validator version, graph/index fingerprints |
| release_heads | head_name; published release_id and expected-head revision for CAS publication |
| jobs | job_id; private store only, idempotency key/type/source hash/state/error/attempt count; no automatic provider workers |

Rule trees, type contracts and manifest structures remain bounded validated JSON,
not a separate generic condition-node/EAV parameter engine. Titles/labels/hints use
Text FKs. Local references use real composite FKs; cross-store refs are structured
store namespace/id/revision + expected hash resolved by trusted host, never SQL FKs
across files. Anchor links with local/canonical owner checks use the existing vocabulary
registry. TTS file paths/keys are not required fields for dialogue publication.

Release manifest has explicit arrays of exact Topic/Flow/Dialogue/Text/Action/Rule
refs, anchor-link set and one compatible embedding profile/index fingerprint.

Implementation ruling2026-10-22: full-corpus dependency JSON measured600377
characters, beyond the262144-character bounded header. Exact typed dependencies
therefore live in immutable release_dependencies rows with per-row hashes and a
set graph fingerprint; manifest_json is the bounded header, not the full closure.
Consumers must use ContentRepository.ReadRelease to reconstruct/verify both, never
interpret header-only JSON as complete membership. This refinement preserves exact
revisions/readiness, not permission to omit dependencies or raise bounds.
legacy_sources identity includes source_kind/source_path/source_hash so edited
same-path sources retain earlier mappings for pinned releases. Incompatible early
staging artifacts require explicit rebuild/version handling, not silent mutation.
No mutable latest lookup during execution. Host resolver validates entire closure;
SQL rows alone or a published string never authorize execution. Changing shared
dependencies mechanically recompiles affected parent revisions. Head update is CAS
after compiler, policy and in-memory index readiness checks; old head survives failure.

### Game schema2 and optional recording schema

Reuse game_events as the durable event root. Add typed context/cause sidecar tables
instead of a second events writer or changing historical event meaning:
scenario_runs, flow_invocations, step_executions, variable_values, answers,
action_executions, event_context, stat_changes. Per-run content refs pin store/release/
definition revisions. Action execution may omit Step for autonomous/standalone work.
event_context PK event_id references game_events; context IDs nullable, cause fields
are separately host-validated. stat_changes PK change_id references game_events and
typed owner (Alterra/player/home/environment), stat ID, known flags and actual values.
Use typed owner validation, not actor-only keys. Existing historical events without
new context remain legal and labelled unclassified, not backfilled with invented facts.

Continuous stat changes may aggregate per existing persistence checkpoint interval;
record simulator rule/version and actual interval/value, not one record per frame.
No extra decay/catch-up algorithm. Log only confirmed changes that have real evidence;
do not synthesize per-stat history from arbitrary old snapshot diffs or fabricate causes.
Atomic discrete commits include state, game event, context/stat rows and receipt in
the same game DB transaction. Add optional typed history fields to GameCommitRequest;
extend request hashing and the existing Commit validation rather than open a second
writer/connection transaction. Receipt lookup precedes stale-revision rejection.

Recording schema: recording_sessions, interaction_records, contributions,
contribution_records, recording_policy. Policy persists explicit capture scope/version
and enable intervals; memory/online consent flags are not reused. New recording is
off unless enabled explicitly. Contribution snapshots own approved projected bytes
and their hash, not live pointers that can change after confirmation. No HTTP sender.
Server receipts can only be recorded through a trusted transport boundary which
remains unimplemented; tests use a named fake receipt explicitly, never real delivery.

### Bounded defaults (implementation choices, not new product claims)

- Honor current existing Scenario bounds: max64 active Steps and64 automatic
  transitions, 16 beats per dialogue/8 choices unless existing validators are stricter.
  Max Subflow nesting8, Rule depth8, max256 Rule nodes, max16 parameters/locals per
  direction/Flow. Validate the expanded graph budget; no recursive calls/Rule cycles.
- Optional text outputs/variables <=2048 chars; expression/operator/enum/action
  vocabularies are host allowlists. Max128 char generated technical IDs, preserving
  supported existing ID bounds. Unknown/missing required input fails closed.
- Recording policy must be explicitly supplied when enabled: retention days1..30,
  byte cap1..32MiB and record cap1..10000; fixtures use7days/8MiB/5000records.
  No production enable/default consent inferred from these fixture settings.
- Raw-text scope is independent of metadata scope; submitted excerpts <=2048 chars,
  each record <=16KiB, preview/export batch <=1MiB. Truncation is labelled, not hidden.
  Reject explicit secrets fields, credentials, keystrokes and unknown record kinds;
  free text may still contain personal information and needs preview/review.
- No contribution transport, automatic capture retrofill or training authorization.
  Pending-delete/opt-out invalidates prepared batches; keep no raw-text shadow copies
  in required game history or game-only backups. Minimal receipt/hash metadata may
  remain with explicit disclosure; no forensic erasure promise.

## File ownership map

New focused sources under `Assets/AlterOS/Content/`: ContentContracts.cs,
ContentSchema.cs, ContentRepository.cs, ContentPackageValidator.cs,
ContentReleaseCompiler.cs, ContentLanguageResolver.cs, ContentSemanticIndex.cs,
LegacyContentAdapter.cs, ContentFlowAdapter.cs, ContentStoreSelection.cs.

New flow support under `Assets/AlterOS/Scenarios/`: FlowValue.cs,
FlowBindingEvaluator.cs, SubflowExecution.cs and a focused ScenarioSession partial
for new content execution. Preserve the existing ScenarioSession owner/API and old
formats; do not create a competing live Scenario manager.

New history sources under Persistence/: GameHistoryContracts.cs,
GameHistorySchema.cs, GameHistoryReader.cs. Modify existing contracts/schema/Commit
and backup validation deliberately. New recording sources under Recording/:
RecordingContracts.cs, InteractionRecorder.cs, ContributionPreview.cs,
RecordingRepository.cs. Test classes mirror these responsibilities in Editor tests.

Headless Tools/ContentData/: Check-ContentData.ps1, ContentDataCommand.cs,
HeadlessContentDataTests.cs, Fixtures/ (synthetic only), README.md.
Extend Tools/Persistence/ tests for game schema2. No new npm/Python service.
If a named new file needs a focused split, record it; no mass source reorganization.

## P0 — Baseline and ownership gate

- [ ] Run existing focused game persistence/source compilation, Scenario, Topic,
  Playground and furniture suites using their current scripts. Read exits/results;
  log pre-existing errors, not assumed passes. Do not reimplement D0-D6.
- [ ] Compare current source to the fixed mapping above. Write
  `Documentation/Tracking/Content Data Integration Handoff.md` with exact existing
  owners, changed-file ledger and review-focus risks. No parallel planning task.
- [ ] Probe pinned Unity import/Console once when available. If unavailable, keep
  headless steps moving; don't hide the original foundation's open Unity acceptance.

## P1 — Content schema, immutable references and repository

**Produces:** `ContentRepository.Open(path, mode)`, `ReadRelease(releaseId)`,
`ReadTopic(ContentRef)`, `ReadFlow(ContentRef)`, `Stage(ContentPackage)`;
DTOs ContentRef(store,id,revision), ContentPackage, ContentRelease, ContentDiagnostic.
Return explicit ready/invalid/missing/incompatible/unavailable statuses, not defaults.

Pin shared API types in ContentContracts.cs: ContentStoreMode(ReadOnly,ReadWrite),
ContentResult<T>(status,value,diagnostics), ContentHostVocabulary (registered Action,
expression, enum, context-fact and canonical owner allowlists), ContentRef and the
package/release DTOs. Open(string,ContentStoreMode) returns ContentResult<ContentRepository>;
ReadRelease(string) returns ContentResult<ContentRelease>; ReadTopic/ReadFlow(ContentRef)
return ContentResult<TopicRecord>/ContentResult<FlowRecord>; Stage(ContentPackage)
returns ContentResult<string> (staged package identity). Later tasks use these same
types, not independent JSON dictionaries with incompatible field names.

- [ ] First create failing repository tests: UTF-8 path, actual SQLite schema,
  composite FK violations, immutable revision conflict, wrong-kind Step, duplicate
  beat order/language, read-only author store, corrupt/future DB unchanged and
  rejected cross-user/path reference. Show RED before implementation.
- [ ] Implement schema/repository and detached contracts; parameterized SQL, trusted
  roots and explicit transactions; enforce FK/check constraints and allowed bounds.
- [ ] Add Check-ContentData.ps1 by extending the real-binding harness pattern, not
  source-string-only assertions or a mocked database. Provide `-Group Repository`.
- [ ] Run focused checks GREEN; schema hash/read-back IDs stable across reopen.

## P2 — Legacy bake and localization, one editable source

**Produces:** `LegacyContentAdapter.Import(sourceManifest): ContentPackage`,
`ContentLanguageResolver.Resolve(TextRef, displayLanguage, speechLanguage)` and
`ContentReleaseCompiler.Compile(package): CompiledReleaseResult`.

SourceManifest is a host-created list of source kind, relative path, expected hash
and trusted source root; no blanket recursive inclusion of every file in Tools or
player archives. CompiledReleaseResult carries status, immutable release and
diagnostics. Hash semantic graphs without their own computed parent revision fields
to avoid a circular hash definition. Language aliases japanese/english map explicitly
to ja/en at the adapter; stored locale keys never silently reinterpret old fields.

- [x] Add failing fixtures for current Generic schema, canonical TopicDefinition /
  TopicScenario and executable ScenarioDefinition. Preserve exact source ID mapping,
  conditions, labels/prompts, priority/weights, replay/consumption node, choices,
  expressions, action trust and unknown flags. Reject unsupported semantics with
  named diagnostics; do not silently omit nodes or label partial import complete.
- [x] Preserve original source bytes/hash in a staging manifest for unsupported
  fields; authoring JSON stays untouched. Derive stable IDs for extracted sequences
  without merging identical wording that might have different future ownership.
- [x] Test EN/JA default, ID/EN and EN/ID translations; missing display -> EN,
  absent EN -> unavailable, missing speech -> silent; no API calls or voice fallback.
- [x] Implement full package projection and explicit old-format adapters. Current
  speech/display wire schema remains supported; add generic localization projection
  at the presentation boundary only where data exists. Language support is not TTS proof.
- [x] Add `-Group Bake` and headless command `bake --manifest <trusted-manifest>
  --output <staging-db>`; no arbitrary model-supplied paths. Fixture round-trip must
  preserve gameplay-relevant semantics. Fresh corpus coverage/counts are measured,
  never copied from an old 57/161 snapshot.

## P3 — Rules, shared dependencies and publication

**Produces:** `ContentPackageValidator.Validate(package, hostVocabulary)`,
`ContentReleaseCompiler.ResolveDependencies(package)`,
`ContentRepository.Publish(compiledRelease, expectedHeadRevision)`.

- [x] RED tests: missing required input/output/exit, local name collision, wrong
  binding type, dangling Rule, recursive calls/Rule cycles, nesting/expanded limits,
  all/any/not unknown truth, hidden/locked/available choices, capability escalation.
- [x] Reuse existing TopicConditionEvaluator/eligibility semantics as supported
  leaf adapters, not duplicated SQL gates. New Rule refs resolve to validated typed
  trees; unsupported facts remain unknown/denied, not false interpreted as absence.
- [x] RED release tests: source edits recompile parent revisions; old run snapshot
  unchanged; incompatible exit change rejected; CAS conflict retains old head;
  failed index readiness doesn't publish; orphan removal cannot break retained refs.
- [x] Implement deterministic closure/hash/snapshot pinning. Make independent copy
  is explicit new ID; Used by is a repository query/CLI, not UI rebuilding here.
- [x] Run `-Group Publish` GREEN, including missing cross-store revision and allowed
  shared dependency without duplication of its mutable execution state.

## P4 — Typed Subflow execution in the existing runtime boundary

**Produces:** `ContentFlowAdapter.Prepare(resolvedRelease, flowRef, inputs, context)`
returning a validated pinned ContentExecutionPlan. Extend ScenarioSession with
`Start(ContentExecutionPlan plan, string runId, ScenarioRuntimeSnapshot context)`;
existing Advance/Quit/diversion/presentation/ActivityHost contracts stay authoritative.

ContentExecutionPlan contains pinned resolved release/Flow, validated typed initial
inputs and invocation declarations; it does not hold Unity objects or grant
capabilities. Prepare returns ContentResult<ContentExecutionPlan>. FlowValue and
Host-context resolution are shared by P3 validation and P4 execution. Only runtime
host snapshots authorize resolved entity/action arguments, even for authored content.

- [x] RED tests: two callers reuse one dialogue and continue to different Steps;
  nested invitation returns accepted/declined/cancelled correctly; explicit output
  mapping; cancel exposes no partial outputs; frame values isolated; repeated Step
  visit has distinct execution identity; unknown/missing/wrong entity use rejected.
- [x] Implement invocation frames, typed bindings, named exits and existing-style
  Scenario effects. Keep flow-local data in frames, not shared definitions or global
  stats. Use existing ask/action/wait/lifecycle adapters and trusted host evidence.
- [x] Test one running/one suspended, action acceptance vs completion, pause retains
  locals, quit ends execution without autoresume, retry doesn't duplicate effects,
  old legacy Start path/IDs remain compatible. Preserve runtime Action permission
  checks even when compile-time Rule previously passed.
- [x] Run `-Group Flows`, existing ScenarioRuntime/TopicRuntime/Furniture suites.
  No new executor/action, actor animation or furniture logic added by this task.

## P5 — Embeddings and incremental semantic publication

**Produces:** `ContentSemanticIndex.Load(release, profile)` and immutable matching
snapshot, plus `StageEmbedding(anchorRef, profile, bytes, sourceHash)`.

- [x] RED tests: current baked float32 vectors round-trip/rank equivalently;
  language/anchor links preserved, local-only anchors supported; wrong dimension,
  NaN/Infinity, normalization/profile/source-hash mismatch rejects without replacing
  last valid index. SQL publication cannot imply loaded index/TTS readiness.
- [x] Import existing baked metadata/vectors from verified GteSemanticAssetPaths
  locations; no model loading/rebake/new inference required. Load into current
  DialogueRouteIndex/TopicFamilySemanticMatcher or a minimal shared-array adapter;
  preserve existing ranking math, ties, RAG candidates and authored fallback gate.
- [x] Jobs can stage/validate supplied synthetic generated Topic/embedding data;
  no provider worker, whole chat embedding or automatic calls. Partial generated
  release atomically promotes only when complete compatible dependencies/index ready.
- [x] Run `-Group Semantics`; compare cold/warm DB open, ID lookup, index load and
  ranking timings/memory versus current file path on same data/hardware. Report
  corpus, sample count, p50/p95 and limitations; performance improvement is not a gate
  to fabricate. Do not hash a huge model per query/GUI repaint.

## P6 — Additive history and stat causality on the existing writer

**Produces:** schema2 migration and optional GameCommitRequest typed history payloads,
`GameHistoryReader.ReadEvents(query)`/`ReadStatChanges(query)` with bounded pages.

- [x] RED tests: v1 upgrade retains exact state/identity/receipts/events, pre-upgrade
  backup/reopen, migration interruption atomic, future schema fail-closed; same ID/
  same semantic history retry original receipt, changed payload conflict, stale
  request unchanged. Add history fields to deterministic request hashing.
  Preserve the v1 hash for requests with no new history fields so retries of old
  receipts remain duplicates after upgrade; version new history-bearing hashing.
  Restoring a v1 backup into a v2 installation upgrades/validates the staged copy
  before replacement, never rewrites the original backup or silently downgrades live DB.
- [x] Test non-actor environment/player stat owner, actual clamped delta and unknown
  preservation, invalid cause/checkpoint rejected, source-event cycles rejected,
  aggregated interval and rule/version, duplicate checkpoint no repeated effect.
- [x] Implement history sidecars/context/stat rows in existing Commit transaction.
  Existing event payloads stay valid; no new parallel event writer or direct LLM
  stat/history API. Emit causal history from existing real mutation boundaries only,
  reuse trusted operation/action IDs, and never infer old unseen effects retroactively.
- [x] Add run/invocation/Step/answer hooks where the current Scenario/Activity host
  confirms state. An unavailable optional diagnostic journal must not block gameplay;
  mandatory effect history in a game transaction fails atomically with that effect.
  Local cleanup cannot cascade-delete permanent facts. No automatic resume on load.
- [x] Run Check-GamePersistence.ps1 and `-Group History`; update backup validation
  for schema2 and explicitly document independent content/recording recovery limits.

### P6 implementation (2026-10-22)

Schema 2 is an additive migration from game schema 1. It preserves existing snapshots,
identity, events and operation receipts; adds stable event/stat/answer sequences,
typed event causes and stat owners, Scenario runs/Flow invocations/Step executions,
accepted answers, Action execution/checkpoint rows, and an intentionally unwritten
`variable_values` table. Foreign keys do not cascade-delete durable history. Legacy
requests retain the exact version-1 payload hash; new history-bearing requests use a
versioned semantic hash plus a retained compatibility hash for a caller replaying an
older request shape.

`GameHistoryReader` exposes bounded event, stat-change and answer pages. `Commit`
stores snapshots, events, contexts, stat changes, execution records, answers, and
receipts in one existing transaction. V1 backups validate read-only; recovery upgrades
the copied staging file before replacement and keeps the source backup byte-for-byte
unchanged. Failed DDL rolls back the whole migration and leaves `user_version=1`.

Existing progression boundaries now submit actual clamped before/after deltas for
dialogue effects, activity hydration and the current aggregated needs tick. The
Scenario presentation host emits best-effort start/Step/answer/Action observations;
free-text content and local variable values are never stored. A failed optional
observation does not block Scenario flow. No migration invents past history, resumes
unfinished runs, or changes memory/recording permissions.

Fresh evidence: `Check-GamePersistence.ps1` **8/8** including the History group,
`Check-ProgressionSourceCompile.ps1` source compile/probes passed (0 errors, 52
warnings), and `Check-ContentData.ps1 -Group Flows` **7/7** including journal callback
deduplication and failure isolation. Logs: `P6-GamePersistence-Final-GREEN-2026-10-22.log`,
`P6-Progression-Final-GREEN-2026-10-22.log`, and
`P6-Flows-Final-GREEN-2026-10-22.log`.

These are synthetic headless/source checks. Unity Editor import/compilation, Editor
tests, Play Mode, player-visible Scenario behavior and an actual profile upgrade were
not run. P7 remains next; the production content router still uses Resources until F1.

## P7 — Consent-bound local recording and manual contribution framework

**Produces:** `InteractionRecorder.Enable(RecordingPolicy)` / `Disable()` /
`Record(InteractionObservation)`; `ContributionPreview.Create(selection)` /
`Confirm(previewId, expectedHash)` / `ExportConfirmed(contributionId, trustedPath)`.
`Record` requires active policy and permitted field scope; no network transport.

- [x] RED tests: default-off/no DB, no retroactive capture, exact enabled interval,
  metadata vs text scope, unknown kind/secrets-field rejection, caps/TTL pruning,
  delete does not affect game events, disable stops new collection immediately.
- [x] Add narrow source hooks only for accepted submitted input, actual presented
  options/availability/order, actual chosen answer, acknowledged beats and trusted
  Action/routing results. Do not add another transcript writer or infer sentiment.
  Hook registration is explicit; no recording consent inferred from game initialization.
- [x] RED contribution tests: preview allows date range/exclusions, default selects
  uncontributed records but includes older excluded records on explicit selection;
  immutable projected-byte/hash snapshot; new/edited text after preview forces review;
  repeated confirmation same batch; disabled/delete/revoke invalidates unsent batch;
  export never marks uploaded/delivered; no new records silently included on retry.
- [x] Implement local preview/confirmation/export only, with preview warning that
  text/metadata may identify users and permitted purpose fixed to evaluation unless
  separately authorized. Training permission is absent. No HTTP endpoint/client,
  no background uploads. Record fake/test receipts only through a synthetic adapter;
  production no sender means no delivered receipts.
- [x] Run `-Group Recording`; README names missing player-facing Canvas opt-in /
  Contribute UI and server-side withdrawal policy as not implemented/acceptance gaps.
  Never claim real contribution feature complete from local API/export tests.

### P7 implementation (2026-10-22)

Added an isolated private recording database under the explicitly supplied path. The
`InteractionRecorder` constructor does not create it; a valid explicit policy passed to
`Enable` is required. Metadata and raw-text permissions remain separate, accepted kinds
and fields are allowlisted, raw text is limited to accepted submitted input, and active
session time/byte/record caps are enforced. Disable stops writes immediately and
invalidates pending/confirmed local batches. Expired records are pruned on enable, capture
and preview. Deletion is confined to the recording database and never touches
`game_events`.

Scenario hooks are optional and caller-registered. They cover presented option IDs/order,
accepted choice IDs, opted-in submitted free text, acknowledged beats, trusted Action
results and route classification. No recorder is created or enabled by startup. No raw
text is copied into permanent game history.

`ContributionPreview` freezes selected record projections, exact membership, exclusions
and hash. Default selection skips contributed/excluded rows; explicit inclusion can
reconsider an excluded old row. Confirmation checks the expected hash and source hashes;
edits/deletion/disable invalidate unsent batches. Export writes only a confirmed local
projection to a new trusted path and never records upload/delivery/training permission.

Fresh evidence: `Check-GamePersistence.ps1 -Group Recording` passed the synthetic group,
including default-off/no file, policy validation, exact session scope, raw-text separation,
unknown/secret field rejection, byte/record caps, expiry, deletion isolation, immutable
preview/export, explicit text inclusion, and no fictional delivery receipt. The Scenario
Flows suite passed **7/7** with recording-observer assertions. Logs:
`P7-Recording-Final2-GREEN-2026-10-22.log` and
`P7-Scenario-Recording-Hooks-GREEN-3-2026-10-22.log`. See [the recording README](../../Tools/Recording/README.md) for
the missing Canvas opt-in/Contribute UI and server-side withdrawal policy.

No recorder UI, network transport, upload worker, server receipt or server withdrawal
policy exists. Unity Editor import/tests, Play Mode and player consent/export acceptance
remain unverified. Recording stays disabled by default.

## P8 — Wiring boundary, parity and integration regression

**Produces:** explicit `ContentStoreSelection` injection path and headless commands
`validate`, `bake`, `inspect`, `used-by`, `preview`, `recording-preview`,
`recording-export`. No database paths selected by generated text, shell execution
or production publish through these tools without explicit trusted configuration.

- [ ] Test same labeled phrase/context set and synthetic host snapshots on file and
  DB paths: same candidate IDs, conditions, highest-priority tier/weight inputs,
  whole-occurrence consumption and failure behavior. No fake GTE scores.
- [ ] Use existing ContentWorkbench trusted catalogue/validator entry points where
  feasible; do not overwrite its worker-owned UI/server or create another catalogue.
  Supply repository API/CLI handoff instead of broad workbench redesign.
- [ ] Keep production selection legacy until full parity and Unity acceptance;
  source-integrate an explicitly supplied DB release for synthetic tests. Missing
  source cannot silently cross stores or grant authored-effect trust to generated data.
- [ ] Run full new suite plus existing persistence, progression source compile,
  ScenarioRuntime, TopicRuntime, TopicPlayground, furniture, conversation/memory
  suites. Preserve and name baseline failures rather than disable checks.

## P9 — Review, evidence and return report

- [ ] Review the final changed-file set against this plan and the five Review Focus
  cases. Run the smallest existing regressions reproducing any real review finding,
  fix with RED/GREEN and rerun affected/full source suite. Do not expand into polish.
- [ ] Attempt pinned Unity normal import/compile and focused Editor tests. No full
  190-test run without focused success, no force-close/batch bypass of unsaved Editor.
  If stale, finish source work and report exact pending gates, not runtime success.
- [ ] Windows player/PlayMode verification only against isolated synthetic root.
  Normal SplashScene -> ModelTest remains acceptance; direct ModelTest is not it.
  No real player profile, provider or credential use. Android remains unverified
  unless a separately available safe build/device test is actually run.
- [ ] Update [[Content and Interaction ERD]] with implemented table/API mapping vs
  deferred surfaces, TODO/Feature Register/Home/Architecture/UnityProjectContext,
  integration handoff and dated Verification note. No duplication of per-player data.
- [ ] Final report: actual schema changes, retained owners, migration plan/result,
  exact tests/counts/artifacts, timings and limits, source vs Unity/player proof,
  unwired/blocked surfaces and next acceptance. Explicitly say production cutover,
  opt-in UI, server upload and training state. Do not call all persistence migrated.
- [ ] Report directly to the human in this session. The user also authorizes one
  concise report back to coordinating thread01a06388-281d-7601-a8e1-c47d7cca0c37
  if that thread identity is verified accessible; otherwise do not guess a recipient,
  leave the report here and in the shared handoff for coordinator retrieval.

Coordinator thread identity above was verified from CODEX_THREAD_ID in the planning
session, not guessed from a visual URL. The human explicitly requested report-back.

## Self-review and execution request

Coverage: P1 physical/revision groundwork; P2 schema/localization adapters; P3
Rules/releases; P4 reusable runtime; P5 embeddings; P6 history; P7 optional local
recording; P8 parity/wiring boundary; P9 review/evidence. No hidden second planning
phase, remote transport, production data merge or new gameplay mechanics required.
Fixtures/caps/manifest encoding are planner choices recorded explicitly above.
The existing worker implements continuously through the bounded source deliverable;
unavailable Unity is an acceptance gap, not an excuse to abandon independent tasks.
