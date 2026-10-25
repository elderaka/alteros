---
title: SQLite Authoring Cutover Verification
date: 2026-10-22
tags: [alteros, sqlite, authoring, migration, verification]
status: implementation-and-headless-verification-complete; unity-editor-acceptance-pending
---
# SQLite Authoring Cutover Verification

## Result

SQLite is now the editable authority for shared authored Dialogue and Workbench
drafts. The Workbench command host and Editor build entry both use the shared
revisioned repository. The current local store contains all 439 supported Dialogue
sources, and its explicit `default` build set selects those same 439 documents. A
deterministic tracked export exists for review and fresh-clone import.

Source and headless verification passed. The user's first Unity build reached
`ContentBuildProcessor.BuildAndImport` and failed closed on unregistered expression
names. The SQLite-authoritative selection has since been normalized and its latest
export passes the headless vocabulary/hash checks. After the correction, the user
reported that the Editor build works. No build log or installed artifact hash was
provided, so this is user-reported build success rather than independent artifact
inspection. No `SplashScene -> ModelTest` or player run has been reported.

The authoring database has schema version 1. The compiled-content database remains
schema v4. Release build-manifest v2 records authoring store identity and snapshot
hash while retaining v1 parsing.

## Implemented path

- `AuthoringRepository` provides explicit create/open, immutable UTF-8 revisions,
  JSON syntax-only CAS saves, stable source IDs, operation receipts, build-set CAS,
  bounded snapshots, online backup and fail-closed recovery statuses.
- `AuthoringJsonImporter` performs explicit hash-checked imports, preserves exact
  source bytes and mappings, and commits each import batch atomically. The command
  host scans only the configured Dialogue root when `authoring.import` is invoked.
- `AuthoringJsonExporter` writes deterministic hash-named exports with a manifest,
  readable UTF-8 JSON and a separate exact `originals/` directory. Fresh-store
  import is explicit; backup uses SQLite's consistent backup API.
- Workbench `draft.list/read/write` responses retain their existing wire shape while
  using SQL. `authoring.*` covers store initialization/info, import, typed CRUD,
  selection, export and backup. Missing stores return `not_initialized`; old draft
  JSON is not scanned or written.
- `ContentBuildProcessor` reads the configured SQLite store and the explicit
  `default` build set. It pins one snapshot and reads Generic routes/hash from those
  exact bytes, then uses the existing staged release verification and atomic import.
  There is no JSON build fallback.
- `.gitignore` excludes only `/Tools/ContentWorkbench/Workspace/`. The local DB and
  backups are ignored; the deterministic export remains tracked.

## Local migration state

- Database: `Tools/ContentWorkbench/Workspace/authoring.sqlite3` (ignored locally).
- Store ID: `fa023336-caee-4fa5-b611-442c9268a639`.
- Schema version 1, store revision 63, 439 imported Dialogue sources, 439 documents
  in the explicit `default` build set.
- Export snapshot: `02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27`.
- Export path: `Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`.
- Export tree size: 2,670,628 bytes across 878 data files plus the manifest (879 files total).
- GTE metadata `contentHash`: `fb2bd80b47d847c73b2884531f336af0a97f373f2000ea0a9c22ec17982a8888`.
- All original Dialogue JSON remains in place and unchanged by the import operation.

## Expression vocabulary correction

The host catalog contains exactly `angry`, `focused`, `gentle`, `happy`, `neutral`,
`playful`, `sad`, `shy`, `soft`, `surprised`, `thinking`, and `warm`. The SQLite
selection had 104 compiled presentation fields across 35 Generic and 26 Topic
documents using aliases outside that catalog. Those fields are now mapped as follows:

| Previous value | Registered preset |
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

No dialogue text, route, ID, anchor or vector changed. The GTE `contentHash` was
re-pinned to the exact current SQL Generic bytes (`fb2bd80b...`); its vectors and
tokenizer were reused because semantic anchor inputs were unchanged. Retained legacy
JSON and dated route snapshots remain historical import evidence and were not edited.
The compiler checks 928 selected Generic/Topic expression fields and finds zero
unregistered values. See the `A8-Expression-*` evidence in the implementation-plan
workspace.

## Fresh verification

Full command output is retained under
`.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/`; file names below
are relative to that directory except where the path is shown in full.

| Check | Result | Scope |
| --- | --- | --- |
| Portable Workbench build | Passed | Pinned native SQLite deployment, no Unity references; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A0-PortableHost-Build-2026-10-22-post-review.log` |
| Workbench command and interchange tests | 27/27 passed | Native SQLite round-trip, CAS, no JSON fallback, explicit import/selection, deterministic export, fresh import, backup reopen, backup-root rejection, and export-root junction rejection; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A0A3A5-Workbench-2026-10-22-post-review.log` |
| `Check-Authoring.ps1 -Group All` | 18/18 passed | Repository, import, interchange and online SQLite backup; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A1A2A5-Authoring-All-2026-10-22-post-review.log` |
| `Check-ContentData.ps1 -Group Authoring` | 6/6 passed | Snapshot projection, exact-byte bake, provenance, incomplete-draft rejection and Generic hash parity; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A1-ContentData-Authoring-2026-10-22-post-review.log` |
| `Check-ContentData.ps1 -Group Bake` | 11/11 passed | Full source/SQL parity, exact source bytes, routes and baked GTE vectors; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A2-ContentData-Bake-2026-10-22-post-review.log` |
| `Check-ContentData.ps1 -Group Publish` | 6/6 passed | Typed validation, dependency closure and release publication CAS; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A3-ContentData-Publish-2026-10-22-post-review.log` |
| `Check-ContentData.ps1 -Group Semantics` | 4/4 passed | Semantic import, exact profile hash, route parity and snapshot publication; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A4-ContentData-Semantics-2026-10-22-post-review.log` |
| Expression parity against registered presets | 928/928 selected Generic/Topic fields checked; zero unsupported | Current SQLite snapshot revision 63 and actual preset assets; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A8-Expression-Preset-Parity-2026-10-22.log` |
| `Check-ContentData.ps1 -Group Semantics` after normalization | 4/4 passed | Exact current SQLite export hash matches GTE metadata; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A8-ContentData-Semantics-2026-10-22-final.log` |
| `Check-ContentData.ps1 -Group Bake` after normalization | 11/11 passed | Full corpus bake and legacy JSON compatibility still pass; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A8-ContentData-Bake-2026-10-22-final.log` |
| `Check-ContentData.ps1 -Group BakeCommand` | Exit 0 | Full 439-source legacy compatibility command baked successfully using its pinned legacy content hash; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A8-BakeCommand-2026-10-22-final.log` |
| `Check-ScenarioRuntime.ps1` | 67/67 passed | Scenario compiler harness includes production Authoring sources, SQLite dependencies and the runtime netstandard facade; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A7-ScenarioRuntime-2026-10-22-final.log` |
| `Check-UnifiedConversation.ps1` | 197/197 passed | Conversation compiler harness includes production Authoring sources and loads the runtime netstandard facade for transcript tests; `.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A7-UnifiedConversation-2026-10-22-final.log` |
| Local store and export reconciliation | 439 documents, 439 selected | Revision 63; latest export has 878 data files plus manifest; expression correction/export evidence is in `A8-Expression-*.log` |

The full-source bake parity check covered 439 sources, 57 Topics, 322 Flows, 673
anchors, 57 routes across 24 local hours, and 768 vector dimensions. The GTE
metadata's Generic corpus hash now matches the exact latest SQLite export bytes.

The standalone host compiles the pinned sqlite-net source, matching Windows x64
native library, Newtonsoft and netstandard facade. Its assembly references contain
no UnityEngine or UnityEditor assembly. Build output includes CS0649 warnings for
DTO fields populated through JSON deserialization; compilation completed
successfully.

The final static review identified redirected-path risks in export import and the
Editor build processor. `AuthoringPathSafety` now rejects reparse-point components
for the configured export root, authoring database, and Editor staging/install/
backup paths. The export-root junction regression failed before the guard and passes
after it. The reviewer found no remaining static integration blocker. The user's
earlier Unity invocation reached the Editor build processor but failed before
installation on the then-current expression values. The corrected revision has not
been built/imported in Unity yet.

The Scenario and Unified Conversation source harnesses initially failed to compile
because they included `ContentBakeService` without the production `Authoring` source
set. Their red logs record the missing namespace and `AuthoringSnapshot` errors.
Both harnesses now compile those real sources and their SQLite dependencies and load
the runtime `netstandard` facade. The corrected runs pass 67/67 and 197/197; no tests or
production semantics were removed or stubbed.

## User-owned live Editor check

The user reports **Tools > AlterOS > Build Runtime SQL Content** now works against
the corrected SQLite selection. A later read-only inspection of
`Assets/StreamingAssets/Content/content.sqlite3` verified the installed artifact and
confirmed that its manifest snapshot matches authoring revision 63; see
[[2026-10-22 Full SQLite Cutover]] for the artifact hash and validation details. The
next runtime check is normal startup through `SplashScene -> ModelTest`, followed by
player-visible acceptance. No scene operation, Play Mode, player test, provider call,
credential read or embedding rebake was performed for that inspection.

## Historical baseline

Before the command-host cutover, the Workbench baseline had 15 failures and 2 passes
because its old compile selection omitted Newtonsoft and dependent Content/Recording
types. That baseline remains in
`.superpowers/sdd/SQLite Authoring Cutover Implementation Plan/A0-workbench-commands-baseline.log`;
it is not the current verification result.

## W5 shared proof release — 2026-10-24

The shared SQLite authoring store and installed SQL release now contain the W5
proof sources. The six-kind graphical Workbench is owned by Topic Playground; its
writer was released before this DBMS integration, so the new records use the same
revision/CAS repository and are available to that Workbench.

### Baseline and recovery

- Preflight matched store `fa023336-caee-4fa5-b611-442c9268a639`, schema 3,
  revision 63, default selection of 439 documents, selection hash
  `d5dfc6d8281d9c3c4b94d87eb16be22a75087c2894aa14e2c16010b06917398d`, and the
  retained snapshot hash `02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27`.
- Before writes, `AuthoringRepository.CreateBackup` made the online SQLite backup
  `Tools/ContentWorkbench/Workspace/backups/authoring-r63-20261024T1030292363635Z-398be44b8738425cb2eb82948e657038.sqlite3`
  (SHA-256 `150BB222B09F03F86F560E079063A37F3067694CA7F5CF7D78C7ACEDD76E51A0`).
- Installed release and its `.meta` were copied before replacement to
  `C:/Users/lauda/AppData/Local/Temp/AlterOS-W5-Shared-Backup-3207afa9aa1a452aad71a9c5616aba83/`.
  The database copy SHA-256 is
  `67033644122DBB06884B5479A3BBC0469B23C497FEC54D39B559ED83B416A31F`.

### Applied source delta

- The original 439 selected sources were retained. `greetings.json` received one
  CAS revision adding the `topic_start` choice for `topic.turn_on_tv`.
- Added 25 sources: three Topics and Scenario Flows; four bilingual Dialogues;
  three sequence Actions plus the pinned `player.item.grant` Action; the
  `item.water_glass` definition; all eight starter Furniture definitions plus
  `furniture.tv.test_variant`; and `furniture.catalogue` settings with four TV
  aliases, preserved relationship/capability rules, and dispenser aliases.
- Pinned `ContentRef.store` values were rebound to the live authoring store before
  compiling. The staged snapshot was compiled and opened before the CAS writes.
  After applying the sources and updating `default` through its expected selection
  hash, the live snapshot exactly matched the staged snapshot.

### Release and install evidence

- Final store revision: **90**; default selection: **464 documents**; selection
  hash: `3af94f251caa42b3e4e628c0c48a55727908aacc14e7b013f5e9d61c80ac72c1`.
- Snapshot hash:
  `00b81b2da9d976ca14cdeb6a6b30b144551aac2612d74a8276f35af6dc3c315d`.
  Exact recovery/export snapshot:
  `C:/Users/lauda/AppData/Local/Temp/alteros-w5-shared-99307d3bef51421dab33570643aa2cb6/shared-export/00b81b2da9d976ca14cdeb6a6b30b144551aac2612d74a8276f35af6dc3c315d/`.
- Offline local GTE bake: **1,368 anchors**, **768 dimensions**; metadata
  `contentHash` equals the final snapshot hash. No provider, key, or profile data
  was used.
- `ContentBakeService.BakeFromSnapshot` compiled the staged release, then
  `RuntimeContentStore.OpenTrustedRelease` opened its exact store/release/manifest
  pins. Release ID: `release_384d3ff91ba82f79e218fa70ece756a2ec0ae0678a440789`;
  build manifest hash:
  `53bcbe0c23d171b5c1d9747bd8ee957975883857cd437cbafd826cae30abc7fb`.
- The staged SQL file was atomically installed at
  `Assets/StreamingAssets/Content/content.sqlite3`. Its SHA-256 is
  `C6B14D7E736E70BDA8A0EBACEFE32D662EE97D5A68666E9E779687172312B8D7`, identical
  to the validated staged file. **Unity AssetDatabase import remains unconfirmed.**
  The AlterOS Editor is still blocked at “Recovering Scene Backups,” pending the
  human's Yes action; Furniture retains Editor ownership for its W4 identity rebind.
  A bridge refresh response and asset metadata lookup were returned, but do not
  establish that the new SQLite bytes were imported while the Editor is blocked.
  No further Editor refresh, recovery, test or restart was attempted.
- The temporary same-directory atomic swap backup was removed only after
  confirming that it matched the separate preserved pre-install backup.

### Remaining acceptance

No Play Mode or player-visible proof was run. Main/Furniture must complete the
Editor recovery, confirm the installed release import, and finish the pending W4
identity rebind/scene acceptance. After those gates clear, the human's live test
remains: turn on the TV, locate the TV with the exact bark, and fetch a drink
through the mock dispenser with the completed VN and authoritative Water glass
grant. The SQLite source editing Workbench remains owned by Topic Playground.
