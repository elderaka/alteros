---
title: Furniture SQL Catalogue Bridge Plan
date: 2026-10-24
tags: [alteros, furniture, sqlite, workbench, implementation]
status: implementation-active
---
# Furniture SQL Catalogue Bridge Implementation Plan

> **For agentic workers:** use executing-plans in the existing DBMS, Furniture and
> Topic Playground domain sessions, task by task after Main's scoped START. No new
> domain sessions. Read the current execution ledger before taking source or Editor.

**Goal:** Make Workbench-authored Furniture definitions drive the actual runtime
catalogue from one pinned SQL release, preserving global catalogue invariants.

**Architecture:** A revisioned Furniture catalogue-settings source joins the same
authoring snapshot as individual definitions. Manifest schema3 carries its compiled
envelope without definition bodies and exact Furniture dependency refs/fingerprint.
Runtime joins hash-checked SQL definitions into one FurnitureCatalogueDocument; the
existing registry validates it. Scene composition explicitly selects compiled SQL.

**Tech Stack:** Existing C#/SQLite authoring/runtime, strict build manifest and
Furniture registry; existing Workbench metadata renderer. No new dependency.

**Spec:** [[Reusable Content Framework Workflow]], [[Furniture Functions Framework Plan]],
[[Content and Interaction ERD]], and the existing implementation ledger.
This is Main's technical choice under the human's delegated implementation authority,
not a new player feature or a claim that these APIs already exist.

## Global constraints

- Work only in E:/Pleiades/alteros, Unity6000.4.4f1. Preserve original JSON/assets.
- Keep six UI kinds. Catalogue settings are a Furniture source contract, not kind7.
- Shared AuthoringRepository/CAS/snapshot is the only editable authority. No raw SQL
  editor, unversioned metadata workaround, runtime source reimport or second registry.
- No provider/key/profile access; tests use explicit synthetic temp stores only.
- Main owns shared status notes. DBMS owns schema/manifest/readers, Furniture owns
  catalogue/scene consumer, Workbench owns Tools/ContentWorkbench/**. Agree on windows.
- No scene/prefab/meta edits outside the later exclusive Editor wiring window.
- No bulk commit/reset/worktree replacement in this unborn dirty checkout.

## Review focus

1. Missing settings/pins must fail closed in compiled mode, not fall back to JSON.
2. Settings and definitions must share one exact selected release; no latest lookup.
3. Global rules/units/aliases and all eight required starter definitions survive.
4. Changing settings or any referenced definition changes catalogue identity.
5. Migration and older manifest reads preserve existing authoring/releases and refs.

## Task 1: Revisioned settings and compiled catalogue facade — DBMS

**Files:** Assets/AlterOS/Authoring/ kind/schema/validator owners;
Assets/AlterOS/Content/ ContentContracts, ContentBuildManifest, compiler/baker/runtime
owners; Assets/AlterOS/Editor/ContentBuildProcessor.cs if needed;
Tools/ContentData/HeadlessContentDataTests.cs and focused authoring tests.

**Interfaces:**

- Add source kind `furniture_catalogue_settings` under Furniture. Source schema1,
  fixed singleton id `furniture.catalogue`, contains catalogueVersion, lengthUnit,
  angleUnit, coordinateConvention, relationshipRules, capabilities and legacyAliases.
  No definitions or definition body copies. Use existing Furniture DTO field shapes.
- Source enters normal revisioned documents/CAS/build-set snapshot; authoring DB
  schema2->3 extends its existing kind CHECK while preserving previous revisions.
  Compiled content DB remains schema6; manifest3 is a separate format version.
- Manifest schema3 adds compiled `furnitureCatalogue`: validated settings envelope,
  exact selected Furniture ContentRefs and canonical catalogue fingerprint. Source
  provenance remains in the existing manifest source closure. Keep v1/v2 reads.
- `RuntimeContentStore.ReadFurnitureCatalogue()` returns
  `ContentResult<CompiledFurnitureCatalogue>`. That result contains a detached
  `FurnitureCatalogueDocument document` and read-only `string fingerprint`, reading exact hash-checked SQL
  FurnitureDefinitionRecords and decoding their compiled definitionJson. No source
  importer/compiler at runtime; no public mutable package authority.
- Exact envelope fields, ordered refs and fingerprint participate in manifest hashing.
  Manifest refs must equal the selected release's Furniture dependency set. Settings
  source bytes must remain covered by the selected snapshot/source manifest.
- Releases without settings remain readable for existing non-catalogue consumers;
  ReadFurnitureCatalogue fails unavailable. Do not invent a default or auto-import.

- [x] Add red tests: settings create/read/edit/CAS and duplicate singleton rejection;
  previous authoring migration bytes retained; manifest1/2 compatibility; current
  catalogue fingerprint changes with settings/ref revisions; missing/unpinned/foreign/
  mismatched definitions, incompatible units and absent settings fail closed.
- [x] Implement the existing repository/manifest extension, lossless compiled joining
  and strict validation. Send exact new facade/source-shape API to Main/Furniture.
- [x] Green focused tests: legacy catalogue global-data parity, all eight starters,
  current definition hashes and no runtime import call. Update own producer evidence;
  release for one scoped review. No shared DB/publication or Editor mutations.

Task1 combined source accepted2026-10-24 after two fix rounds: strict nested settings,
private pinned metadata, legacy provenance and invariant numeric parsing. Latest owner
bridge4/4/Authoring8/8 and earlier focused compatibility suites are source evidence;
consumer/UI/Unity acceptance remains separate. See implementation ledger/review chain.

## Task 2: Same-owner runtime catalogue consumer — Furniture

**Files:** Assets/AlterOS/Furniture/FurnitureCatalogueRegistry.cs and focused tests;
Assets/Scripts/Furniture/FurnitureSceneAdapter.cs and its existing Editor tests.

**Interfaces:**

- Expose `FurnitureCatalogueRegistry.CreateSnapshot(FurnitureCatalogueDocument)`
  using the existing whole-catalogue validation/hash implementation (no second one).
  Add `CreateCompiledSnapshot(FurnitureCatalogueDocument, string fingerprint)` for
  SQL mode. It uses that same validation and canonical content hash, then derives
  snapshot.catalogueHash as SHA-256 UTF-8 of
  `compiled-furniture-catalogue-v1\n{canonicalContentHash}\n{fingerprint}`.
  Fingerprint must be a valid 64-character SHA-256 value. Legacy hashes are unchanged.
- Scene adapter gets explicit serialized source mode `LegacyJson` or `CompiledSql`.
  Legacy remains only explicit compatibility. CompiledSql consumes the facade above;
  missing/invalid release/settings returns diagnostics, never silent JSON fallback.
- All existing IFurnitureCatalogueSnapshotSource consumers use this SAME cached
  snapshot and registry; release identity changes invalidate it. Placement, function
  binding and revision/reservation checks are unchanged.
- The compiled fingerprint intentionally reaches existing catalogueHash consumers:
  changing an exact ref revision with identical decoded bodies still invalidates
  stale catalogue expectations. The canonical geometry/content hash alone is not
  the SQL catalogue identity. Do not overwrite catalogueVersion to fake provenance.

- [x] Add consumer test source: compiled settings+definition change alters visible
  catalogue identity; legacy TextAsset/path cannot satisfy missing compiled settings;
  aliases/rules/starter diagnostics retained; duplicate/invalid source fails closed.
- [x] Implement the smallest adapter and existing public snapshot factory. No scene
  writes until Main releases W4; no new reservation or persistence system.
- [x] Narrow factory producer released/reviewed2026-10-24: public CreateSnapshot and
  fingerprint-aware CreateCompiledSnapshot, same validation and legacy hash path.
  Owner reports24/24 focused portable tests; independent source review accepted.
  Scene consumer and actual Unity validation remain open.
- [x] Run focused source/Editor tests proportionately after ownership agreement;
  release exact API/evidence for scoped review and W4 serialized source selection.

Consumer source review accepted; actual Unity2026-10-24 compilation0errors and
focused catalogue24/24 plus adapter9/9 EditMode checks passed. No Play Mode or scene
changes in that window. Serialized CompiledSql selection remains W4 work.

## Task 3: Furniture settings authoring and selected fixture release — Workbench/Main

**Files:** Workbench schema/forms/commands/tests; existing W5 fixture/build tooling.

- [ ] Add Catalogue settings as a Furniture source-contract choice with producer
  validation and existing metadata controls. Preserve unknown fields, CAS, explicit
  snapshot pins and no automatic import/publication. Existing six kinds unchanged.
- [ ] Explicitly import retained canonical global data and all starter definitions
  through typed authoring commands for the selected W5 fixture snapshot. Preserve
  originals. Add TV power/mock dispenser opt-ins as reviewed authored data.
- [ ] Prove save/reopen/validate settings and exact reference selection; publish only
  via existing reviewed build/bake/install gate with consistent manifest/source pins.
- [ ] W4 selects CompiledSql through Editor-authored references. Normal startup and
  three PoCs later use the same release. No source-only gameplay acceptance claim.

## Main self-review / stopping boundary

Current2026-10-24: Task3 settings UI and selected release implementation delivered.
Workbench uses the existing Furniture kind/singleton contract; shared authoring
r90 includes all starters/global settings, TV variant/aliases and mock dispenser
opt-in. W5 pinned release is installed on disk; exact metadata/backups are in the
execution ledger. Unity identity import/rebind/save/reopen is complete. Actual
browser settings save/reopen/palette visuals remain unconfirmed. Historical checklists
above do not authorize another per-change validation/review wave.

The amendment fills a confirmed consumer gap in the approved six-kind workflow.
It does not add catalogue-rule gameplay, rug placement, mobile sync or an economy.
Keep storage/consumer/UI windows disjoint. A passing producer test is not completed
Workbench/runtime integration; overall goal still requires the existing W0-W5 gates.

Builder check2026-10-24 found only the catalogue identity propagation question. Main
settled it above with the compiled snapshot hash and wrapper fingerprint; no product
choice remains. The public factory can be a narrow independent Furniture producer
window while DBMS implements storage, before the full consumer/wiring task.
