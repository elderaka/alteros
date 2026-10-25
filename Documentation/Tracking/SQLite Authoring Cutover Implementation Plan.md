---
title: SQLite Authoring Cutover Implementation Plan
date: 2026-10-22
tags: [alteros, sqlite, authoring, implementation]
status: implementation-complete-headless-verified; unity-editor-acceptance-pending
---
# SQLite Authoring Cutover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans for the
> existing-session execution method, or subagent-driven-development if the human
> chooses it. Steps use checkbox syntax. The human approved this plan and an
> implementation START was dispatched on 2026-10-22 to Database Management System
> (`01a15e76-daeb-7451-982f-95a510093fd3`). Dispatch is not implementation or acceptance evidence.

**Goal:** Make SQLite the sole editable authority for shared authored dialogue
and Workbench drafts, with JSON restricted to explicit import/export and retained
migration evidence.

**Architecture:** Add a bounded authoring repository; preserve incomplete draft
documents without granting them runtime trust. Compile an exact SQL revision
snapshot through the existing relational content compiler, validators and release
publication machinery. Runtime reads only a validated immutable release; saving a
draft never publishes or installs it.

**Tech Stack:** C#, Unity 6000.4.4f1, installed pinned SQLite-net 1.3.2/native
SQLite, existing Newtonsoft and Workbench command host/PowerShell/Python tests.
No new database package, SDK, server, provider or editor UI framework.

**Spec:** [[Content and Interaction ERD#SQLite authoring authority approved 2026-10-22]],
[[Content Workbench Shared Contract]], [[Full SQLite Cutover Implementation Plan]].

## Global constraints

- Work in `E:/Pleiades/alteros`; preserve shared changes, source bytes, stable
  content IDs, languages, references, enum values and existing GUIDs. No JSON,
  scene, prefab, `.meta` or legacy source deletion is authorized by this plan.
- This is authored dialogue/storage tooling, not a universal conversion of project
  settings, furniture assets, models, audio, secrets, private memories or save files.
  Inventory and furniture remain with their existing owners.
- Current JSON-authoring rules in older plans describe the implementation being
  replaced. Report the SQL source/headless cutover as implemented and the Unity
  Editor/player acceptance as pending; never present headless validation as gameplay.
- No real player profile, credentials, API/TTS spend, live inference or embedding
  rebake. Synthetic data only. Existing checked-in embedding bytes may be reused
  only when their exact model/pipeline/content profile still matches.
- No automatic JSON scanning/import on save, build or recovery after cutover. A
  missing/corrupt/future authoring DB requires setup/recovery, not legacy replay.
- Keep published normalized Topics/Flows/Dialogues/Beats/Texts/Actions/Rules and
  exact dependency closure. Bounded draft envelopes in SQL do not replace those
  relational runtime tables with one opaque release blob.
- Keep Workbench UTF-8 stdin/stdout, request IDs, bounds, hash-CAS conflict behavior,
  unknown-field preservation, path/reparse protection and unavailable diagnostics.
- Build-time use of Unity's installed compiler is allowed. The built Workbench
  host and SQLite dependencies must run without UnityEngine/UnityEditor DLLs or an
  open Editor. Do not copy Mono's `System.Json` to conceal a portability failure.
- Serialize Unity bridge ownership with the other sessions. Check current root,
  scene/dirty state and jobs; do not run a broad suite or enter ordinary Play Mode.
- No broad staging/commit/reset: this shared checkout has no usable baseline HEAD.
  Record per-task file/evidence checkpoints instead of claiming commits were made.

## Review focus

1. Interrupted import, duplicate source identities and changed legacy bytes must
   not leave a partial-authority DB or replay over newer edits (A2).
2. Two editors, stale expected hashes and database lock timeouts must not lose
   either author's work; receipts/revisions stay atomic (A1/A3).
3. Invalid drafts, unknown fields and unsupported emotional/tool IDs must remain
   editable but never acquire publication or gameplay permissions (A1/A4).
4. An export/fresh-clone import must preserve stable IDs, languages, links, exact
   retained originals and auditable content hashes without creating dual writers (A2/A5).
5. Concurrent edits during compile, stale vectors and interrupted install must
   preserve a pinned release, with no hidden filesystem fallback (A4/A6).

## File and interface map

The paths/defaults below are coordinator implementation choices, not additional
human decisions. Review them with the plan before execution.

- `Assets/AlterOS/Authoring/AuthoringContracts.cs`: typed requests, records,
  snapshots, diagnostics and results; no Unity lifecycle/API calls.
- `Assets/AlterOS/Authoring/AuthoringSchema.cs`: authoring schema version 1,
  separate from the existing compiled-content schema (source currently says 4;
  verify the current baseline before execution, do not downgrade it).
- `Assets/AlterOS/Authoring/AuthoringRepository.cs`: SQLite transactions, bounded
  reads, immutable document revisions, current heads and consistent backup.
- `Assets/AlterOS/Authoring/AuthoringJsonImporter.cs` and
  `AuthoringJsonExporter.cs`: explicit contained filesystem interchange only.
- `Assets/AlterOS/Authoring/AuthoringContentSourceProjection.cs`: project selected
  complete source records or supported Workbench wrappers into compiler input,
  without modifying their stored authoring revision or discarding unknown metadata.
- `Tools/ContentWorkbench/CoreHost/WorkbenchDraftStore.cs`: existing command facade
  over the shared SQL repository, not another store or validator.
- `WorkbenchCommandHost.cs`, `WorkbenchCommands.cs`, `build-core.ps1`,
  `workbench.ps1`: trusted DB configuration, commands and portable packaging.
- `Assets/AlterOS/Content/LegacyContentAdapter.cs`, `ContentBakeService.cs`,
  `ContentSourceManifestBuilder.cs`, `ContentBuildManifest.cs` and
  `Assets/AlterOS/Editor/ContentBuildProcessor.cs`: add snapshot/byte-source entry
  points and consume SQL for builds. Keep filesystem entry points import/test-only.
- Tests: `Tools/ContentWorkbench/tests/test_authoring_sqlite.py`,
  `test_authoring_interchange.py`, existing `test_commands.py`, and a focused
  `Tools/ContentData/Check-Authoring.ps1` linking the real shared code.

Default local editable DB: `Tools/ContentWorkbench/Workspace/authoring.sqlite3`,
outside `Assets/StreamingAssets`. Ignore its DB/journal/backup workspace in Git.
Tracked readable snapshots go to `Content/AuthoringExports/<snapshot-hash>/`.
Fresh clones explicitly import a selected snapshot; they do not auto-hydrate or
silently choose a latest export. JSON wire messages remain the command protocol.

Repository namespace: `AlterOS.Authoring`. Public result convention:
`AuthoringResult<T>` has `status`, `value`, and bounded code/path/message diagnostics.
Statuses: `ready`, `not_initialized`, `invalid`, `conflict`, `busy`,
`recovery_required`, `unsupported_schema`.

Core signatures:

```csharp
AuthoringResult<AuthoringRepository> AuthoringRepository.OpenExisting(string databasePath);
AuthoringResult<AuthoringRepository> AuthoringRepository.CreateNew(string databasePath);
AuthoringResult<AuthoringDocumentPage> List(string kind, string cursor, int limit);
AuthoringResult<AuthoringDocument> Read(string documentId, string revisionHash);
AuthoringResult<AuthoringDocument> Save(AuthoringSaveRequest request);
AuthoringResult<AuthoringBuildSet> ReadBuildSet(string name);
AuthoringResult<AuthoringBuildSet> SaveBuildSet(AuthoringBuildSetRequest request);
AuthoringResult<AuthoringSnapshot> ReadSnapshot(IReadOnlyList<string> documentIds);
AuthoringResult<string> CreateBackup(string trustedDestinationPath);
AuthoringResult<AuthoringImportReceipt> AuthoringJsonImporter.ImportOnce(AuthoringRepository repository, AuthoringImportRequest request);
AuthoringResult<AuthoringExportReceipt> AuthoringJsonExporter.Export(AuthoringRepository repository, AuthoringExportRequest request);
```

`OpenExisting` never creates/rebuilds a missing store. `CreateNew` refuses an
existing file. `Read` with an empty revision selects the current head; compile
always supplies exact snapshot revisions. A page limit is 1..100; cursors bind
the query/store. `AuthoringBuildSet` is a named ordered selection of document IDs
with a CAS revision hash, not a publication. `AuthoringBuildSetRequest` supplies
name, documentIds, expectedHash and operationId. The name `default` is the Editor
build selection. Creation/import may set it only through an explicit request;
new experimental drafts are never automatically enrolled.

A snapshot requires an explicit nonempty selection of at most 10,000 IDs, pins all
heads within one read transaction, and hashes sorted ID/kind/logical-path/revision
tuples. It includes store identity, snapshot hash and immutable document revision
bytes, capped at 64 MiB total for this slice; over-budget fails explicitly. Do not
return all snapshot bytes in the 1 MiB command response. Subsequent compilation
uses those pinned bytes, never current heads. A larger corpus needs an explicitly
planned streaming/package split, not an unbounded allocation.

`AuthoringSaveRequest`: documentId, kind, logicalPath, expectedHash, documentBytes,
operationId. `expectedHash` empty means create-only; existing writes require the
exact stored hash. `operationId` is required and bounded to 128 characters; retry
with the same request returns the same outcome, different bytes conflict.
Document IDs are stable opaque IDs, not editable labels or paths. Import must
preserve content IDs inside documents and source aliases; renaming a display/path
alias never regenerates those IDs.

## A0 — Prove the portable SQLite boundary

- [x] Capture current source/schema/package versions and Workbench tests before
  changing source. Coordinate file ownership with the Playground session.
- [x] Add a failing `test_sqlite_roundtrip_runs_without_unity_dependencies`:
  launch a tiny host in a temporary Unicode directory with only its declared
  deployment files; create/read/reopen SQL and inspect managed assembly references.
  Assert no UnityEngine/UnityEditor dependency and no installed-Editor search at run time.
- [x] Link the pinned package's engine-independent `Runtime/sqlite-net/SQLite.cs`
  into the standalone build and ship its matching Windows x64 native library with
  existing licenses. Do not include `SQLiteAsset`/Unity async extensions, fork the
  package, add a second provider, or load the Unity-dependent package assembly into
  Workbench. Unity continues using the installed assembly for the same shared store.
- [x] Run `Tools/ContentWorkbench/build-core.ps1` and the new Python test with the
  existing local Python runtime. Pass requires a real native round trip in the
  isolated directory; block on an unresolved portability dependency.

## A1 — Transactional drafts and immutable revisions

- [x] Add red repository cases: reopen retains hashes/UTF-8/unknown fields; invalid
  but well-formed drafts save; malformed JSON rejects without a write; two stale
  writers conflict; interrupted write rolls back head/revision/receipt together;
  duplicate operation retry is idempotent; missing/corrupt/future DB fails closed.
- [x] Implement the core signatures above. Tables: `metadata`, `documents`
  (document ID, typed kind, stable source ID, unique logical path, head hash), `document_revisions`
  (ID/hash, immutable bytes, SHA-256, UTC creation time), `operation_receipts`,
  `import_receipts`, `import_sources`, `build_sets`, `build_set_documents`. Restrict
  cascade deletion; FK checks on every
  connection, synchronous durable commits and a bounded 5-second busy timeout.
- [x] Support the ten exact `ContentSourceKinds` already defined by
  `LegacyContentAdapter.cs`, plus distinct Workbench draft kinds `topic` and
  `scenario`. Preserve existing complete wrapper bytes/unknown fields; impose
  4 MiB per imported content document and retain the existing 1 MiB Workbench wire
  and draft-write bounds. A draft save is not semantic validation or publication.
- [x] Run `Tools/ContentData/Check-Authoring.ps1 -Group Repository`; require all
  named cases to pass with the real SQLite binding, including two process writers.

## A2 — Explicit lossless JSON imports

- [x] Add red fixtures for every supported source kind, legacy draft wrappers,
  EN/JA and ID/EN text, unknown fields, duplicate IDs/paths, truncated JSON, changed
  source bytes, repeated import and injected mid-transaction failure. Assert all
  original files remain byte-identical and current SQL edits never get overwritten.
- [x] `AuthoringImportRequest` supplies a host-trusted source root, explicit
  inventory of kind/logicalPath/hash, expected store identity/revision and operation
  ID. Exports may supply separate contained `sourcePath` and stable `sourceId`;
  stored logical aliases remain unchanged. Validate contained non-reparse paths and every file before committing one
  batch. Save exact original bytes/hash and alias mapping with its receipt.
  Same receipt retries do not reread changed files; a deliberate later import is
  a new operation with CAS, not implicit synchronization.
- [x] Extend the command envelope with `authoring.initialize` and `authoring.import`;
  keep initialization and import explicit. Paths are confined to configured shared
  source/export roots, never player-data/secret directories. Missing is not empty.
- [x] Run `Check-Authoring.ps1 -Group Import` and
  `Tools/CompanionRuntime/.venv/Scripts/python.exe -m unittest discover -s Tools/ContentWorkbench/tests -p test_authoring_interchange.py -v`.
  Inventory the current corpus without assuming the historical 439 count is immutable.

## A3 — Switch Workbench and agent commands to SQL

- [x] Add red command cases: create/read/list/write/reopen through SQL, existing
  expectedHash conflict semantics, invalid draft inspection, unknown-field and graph
  layout preservation, and a changed/deleted old JSON draft having no effect after
  import. A missing SQL DB must return `not_initialized`, never scan old draft files.
- [x] Add a host-owned `AuthoringDatabasePath` to `WorkbenchEnvironment`; preserve
  `draft.list`, `draft.read`, `draft.write` request/result shapes through the shared
  repository. Add `authoring.list/read/write` for supported complete documents;
  arbitrary SQL, arbitrary filesystem writes and tool registration are not commands.
  Keep both command families backed by one repository and one typed kind registry.
- [x] Remove filesystem draft persistence from normal Workbench operations; keep it
  in the explicit importer/exporter only. UI/CLI retain the same command owner and
  validators. No new UI/server feature is required by this storage slice.
- [x] Run `build-core.ps1`, existing `test_commands.py` and
  `test_authoring_sqlite.py`. Require native SQL use and no legacy draft file writes,
  not a mock store or a JSON-file mirror.

## A4 — Compile pinned SQL snapshots, not source folders

- [x] Add red pipeline tests: save then snapshot then edit while compiling; the
  compiler must use pinned revisions. Changed/deleted JSON files cannot affect the
  build. Invalid expressions/actions/graphs, incomplete drafts and stale embedding
  profiles block publication; failure leaves an existing published artifact intact.
- [x] Add `authoring.selection.read/write` with the named build-set CAS APIs above.
  Pin `default` before a build. Test that an unselected incomplete draft does not
  block or enter the release, a selected incomplete draft blocks, and a stale
  selection writer cannot silently replace another author's selection.
- [x] Define `ContentSourceDocument` (kind, logicalPath, sourceId, hash, immutable
  UTF-8 bytes) and `LegacyContentAdapter.ImportDocuments(packageId, storeId,
  IReadOnlyList<ContentSourceDocument>)`. Reuse the actual parsers/conversion rules;
  make `Import(ContentSourceManifest)` an explicit filesystem adapter around it.
  `AuthoringContentSourceProjection.Project(AuthoringDocument)` returns
  `AuthoringResult<ContentSourceDocument>`: complete content kinds preserve their
  bytes; Workbench `topic`/`scenario` extract their existing `document` member and
  map to `topic_definition`/`scenario_definition`. Keep wrapper/layout/unknown data
  in the original authoring revision and export; do not reinterpret unsupported
  kinds or promote a document merely because JSON parsing succeeds.
- [x] Add `ContentBakeService.BakeFromSnapshot(AuthoringSnapshot snapshot,
  string outputPath, ContentSemanticBuildInput semanticInput)` using repository
  revision bytes and the existing compiler/validators. Record the authoring store
  identity and snapshot hash in build provenance; retain exact source byte mappings.
  Reuse versioned releases, hashes, dependency rows and semantic snapshot checks.
- [x] Change the Editor build entry to consume the configured SQL authoring store;
  read its explicit `default` build set, never initialize/import it automatically.
  ReadGenericRouteIds and generic content hashing must consume snapshot byte inputs,
  not original files. Stage and verify output, then use the
  existing Editor-owned atomic installation/import path. No filesystem-only build
  fallback. Keep host vocabulary validation strict; the initial expression aliases
  were standardized in A8 against the actual registered presets.
- [x] Run `Check-ContentData.ps1 -Group Authoring`, `Bake`, `Publish` and `Semantics`.
  The authoring snapshot group owns the pinned-bake checks; the standalone authoring
  script has no separate `Publish` group. Parity covered IDs, text, conditions,
  dependencies and routing across all existing routes/local hours. Reuse cached
  vectors only on exact profile/content match; do not rebake vectors here.

## A5 — Deterministic interchange and recoverable authoring

- [x] Add red round-trip cases: deterministic export twice, fresh explicit import,
  equivalent content/links/languages, original byte recovery, conflicting target,
  unsafe path/case collision, interrupted export and a consistent SQL backup/reopen.
- [x] Export an exact snapshot to a new contained hash-named directory with a
  versioned manifest, stable order, UTF-8 JSON, IDs and SHA-256 values. Keep exact
  imported bytes available separately from canonical readable exports; do not claim
  formatting normalization preserves original bytes. Never overwrite an existing
  export or live DB; incomplete exports are not advertised as committed snapshots.
- [x] Add `authoring.export` and `authoring.backup` through trusted destination
  configuration. Use a consistent SQLite backup, not copying a live DB/WAL blindly.
  Missing/corrupt canonical DB requires explicit recovery; exports never auto-merge.
  Document fresh-clone bootstrap and which export represents committed project work.
- [x] Run `Check-Authoring.ps1 -Group All`, interchange Python tests and
  source-control hygiene checks. Update `.gitignore` only for the owned workspace,
  not tracked JSON snapshots or unrelated user content.

## A6 — Acceptance and handoff

- [x] Run fresh Workbench command/interchange tests, all Authoring groups and affected
  ContentData Authoring/Bake/Publish/Semantics checks; retain exact failures/skips.
- [x] Prove the standalone host can edit/reopen/export/import with no Unity runtime
  DLLs, no open Editor requirement and no network/provider credential.
- [ ] Through the correct responsive Editor, compile and test the SQL build source
  seam using synthetic data. Coordinate exclusive bridge access. Do not infer
  `SplashScene -> ModelTest` acceptance from this; full runtime migration still has
  separate safe-profile and gameplay acceptance gates.
- [x] Audit normal authoring/build callsites: no hidden JSON scan/write fallback,
  source IDs/conditions/languages preserved, release pins stable and invalid drafts
  non-routable. Preserve originals; source deletion is a separate future approval.
- [x] Update ERD/shared contract/Architecture/UnityProjectContext/Feature Register/
  Home/TODO and this plan with actual owners, schemas, CLI examples, exports, setup,
  backup/recovery and limits. Report UI availability separately from CLI storage.

## Plan self-review

Coverage: A1 owns revisions/CAS/draft safety; A2 owns all-format migration and
original bytes; A3 owns the single editable command authority; A4 owns validation,
SQL-only compilation and immutable publication; A5 owns reproducibility/recovery;
A0/A6 prove deployment and integration boundaries. Review also caught and covered
the explicit build-set boundary (unselected partial drafts do not enter builds),
snapshot memory bounds, and Workbench-wrapper-to-source projection without a
filesystem round trip. All five review-focus cases have named test owners.
The A8 preset mapping is documented separately from runtime/player acceptance; no
model/provider cost or private-data authority is added.

Human review approved this plan; implementation START was delivered on 2026-10-22
to the existing Database Management System session
(`01a15e76-daeb-7451-982f-95a510093fd3`). That dispatch was coordination context,
not implementation evidence. All source/headless items are now checked; the single
remaining unchecked step is user-run Unity Editor acceptance.

## Implementation checkpoint — 2026-10-22

Implementation is complete through source and headless validation. Workbench
`draft.*` commands now preserve their wire shape over the shared SQL repository;
`authoring.*` commands provide initialize/info/import/list/read/write, build-set
selection, export and backup. JSON migration is explicit and confined to the
configured Dialogue or hash-named export roots. Missing stores do not scan old drafts.
The Editor build entry opens only the configured store, pins the explicit `default`
selection, derives route IDs and the semantic-profile hash from the snapshot bytes,
then uses the existing staged SQL verification and atomic asset installation path.
There is no JSON build fallback. Compiled-content schema remains v4.

The local schema-v1 store `fa023336-caee-4fa5-b611-442c9268a639` is at store revision
2 with 439 imported sources and a 439-document `default` build set. The explicit
review export is
`Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`
at authoring store revision 63.
All original Dialogue JSON remains untouched. The local SQLite database and its
backups stay under the ignored Workbench workspace.

Fresh checks after the review fixes: Workbench command/interchange suite 27/27;
`Check-Authoring.ps1 -Group All` 18/18; ContentData Authoring 6/6, Bake 11/11,
Publish 6/6 and Semantics 4/4; the affected Scenario and Unified Conversation
harnesses now also compile and pass 67/67 and 197/197. Both harnesses include the
production Authoring source set required by `ContentBakeService`; Unified Conversation
and Scenario load the runtime `netstandard` facade required by their SQLite
dependencies. The added junction regression verifies that export
import rejects a redirected configured export root before reading it. Shared path
checks also guard the Editor authoring database and staged/install/backup files.
The Editor-specific checks still need Unity compilation and the user-run build step.
The portable x64 host includes the pinned sqlite-net source, matching native library
and licenses, Newtonsoft and netstandard facade; its assembly references contain no
UnityEngine/UnityEditor assemblies. The actual full-source import replayed against
the same hashes, and the exact GTE profile check covered 439 sources, 57 Topics,
322 Flows, 673 anchors, 57 routes across 24 local hours and 768 dimensions.

Ruling: use `Check-ContentData.ps1 -Group Authoring` plus the affected Bake/Publish/
Semantics groups for the authoring publication seam instead of adding a duplicate
`Check-Authoring.ps1 -Group Publish`. The authoring script has no such group, while
ContentData Authoring already executes the production snapshot compiler and
provenance checks. Cost if wrong: a downstream tool expecting that exact standalone
group name would need a thin wrapper.

## A8 — Standardize compiled expressions against the host presets

The user's Editor build reached `ContentBuildProcessor.BuildAndImport` and failed
closed with `expression_not_registered`. The 12 expressions actually registered by
`Assets/Altera/Presets` are `angry`, `focused`, `gentle`, `happy`, `neutral`,
`playful`, `sad`, `shy`, `soft`, `surprised`, `thinking`, and `warm`. The 104
compiled presentation fields across 35 Generic and 26 Topic documents were
standardized in SQLite as follows:

| Old value | Registered value |
| --- | --- |
| `calm` | `neutral` |
| `curious` | `thinking` |
| `excited` | `happy` |
| `flustered` | `shy` |
| `guarded` | `focused` |
| `hurt` | `sad` |
| `proud` | `warm` |
| `soft_guarded` | `soft` |
| `steady` | `focused` |
| `teasing` | `playful` |
| `withdrawn` | `sad` |
| `worried` | `gentle` |

No text, route, ID, anchor or vector was changed. Only the GTE metadata
`contentHash` was refreshed to the new exact SQLite Generic byte hash
`fb2bd80b47d847c73b2884531f336af0a97f373f2000ea0a9c22ec17982a8888`; vectors and
tokenizer were reused because semantic anchor inputs did not change. Retained legacy
JSON and its dated route snapshots remain unchanged. The exported revision-63
snapshot is `Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`.

Verification: the actual preset parity check examined 928 selected Generic/Topic
expression fields and found none outside the 12 registered names. The export
round-trip and Authoring All passed; current-SQL Semantics passed 4/4, Bake passed
11/11, and the legacy `BakeCommand` passed with its original pinned JSON hash. Logs
are `A8-Expression-Standardization-2026-10-22.log`,
`A8-Expression-Preset-Parity-2026-10-22.log`,
`A8-ContentData-Semantics-2026-10-22-final.log`,
`A8-ContentData-Bake-2026-10-22-final.log`, and
`A8-BakeCommand-2026-10-22-final.log` under this plan's `.superpowers/sdd/` folder.

The user reports **Tools > AlterOS > Build Runtime SQL Content** now works against
revision 63. A later read-only inspection independently reopened the installed
`Assets/StreamingAssets/Content/content.sqlite3` release, verified its integrity and
trusted release, and confirmed that the build-manifest snapshot hash matches current
authoring revision 63. See [[../Verification/2026-10-22 Full SQLite Cutover]] for the
artifact hash and inspection evidence. The remaining live check is normal startup
through `SplashScene -> ModelTest`; no player run, real profile, credentials, provider
call or embedding rebake was performed here.
