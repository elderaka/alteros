---
title: Six Kind Workbench Plan
date: 2026-10-22
tags: [alteros, planning, workbench, authoring, content]
status: planning-only-awaiting-w0-w2-release-and-w3-start
---
# Six-Kind Workbench Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` task-by-task in the
> existing Topic Playground session only after main consolidates the owner contracts
> and the human issues an implementation START. Do not create a replacement session.
> This plan itself authorizes no source, content, database, scene, package, provider,
> or Unity work.

**Goal:** Provide a real human and developer-agent workflow to create, edit, validate,
and synthetically preview Dialogue, Scenario, Topic, Furniture, Item, and Action
content through the existing SQLite-backed authoring authority.

**Architecture:** A metadata-driven browser UI and the existing JSON CLI use one
versioned Workbench command host. That host calls the shared authoring repository,
trusted domain descriptors/validators, and pure production preview code with detached
synthetic ports; it does not contain a second content database, validator, executor,
or capability registry. Only the existing validated build/install gate publishes a
pinned release.

**Tech Stack:** Existing Unity 6000.4.4f1/C# pure source subset, pinned SQLite-net
host, PowerShell CLI, Python standard-library loopback service, and plain HTML/CSS/JS.
No new package, framework, provider, Unity runtime dependency, or arbitrary C# loader.

**Spec:** [[Reusable Content Framework Workflow]], [[Content Workbench Shared Contract]],
[[Content and Interaction ERD]], [[SQLite Authoring Cutover Implementation Plan]],
[[Content Workbench Implementation Plan]], [[Scripted Actions and Inspect TV Implementation Plan]],
and [[Multi-Agent Coordination]]. The workflow is the current proof contract; older
TV-return and auto-close assumptions do not override it.

## Global Constraints

- Work only in `E:\Pleiades\alteros`; `pleiades.os` is archive-only.
- Topic Playground owns every future edit under `Tools/ContentWorkbench/**`, including
  command host, build/CLI, backend, HTTP service, UI, fixtures, and tests. Agree on
  one writer for each window; idle status is not a lock.
- DBMS owns `Assets/AlterOS/Authoring/**`, shared content compiler/repository/release
  schema, game persistence and Item authority. Furniture and Environment owns furniture
  definitions/functions, exact-target resolution and scene adapters. Alterra Behavior
  owns Scenario/Action execution and its checkpoints. Main resolves shared contracts.
- SQLite remains the only editable authored-content authority. JSON is explicit,
  bounded import/export only. Never edit the live SQLite file directly, mirror it to
  a second JSON draft store, auto-import on open, or silently use legacy files.
- UI and CLI save only revisioned draft/source documents through typed commands with
  create-only/CAS semantics. Validation, snapshot compilation, publication, and runtime
  installation remain separate gates.
- Use real shared validators and pure runtime logic. Any missing/unsupported provider,
  kind, function, effect, or service is visibly unavailable; never return synthetic
  green results from authored self-declared permissions.
- Static game UI remains Canvas/prefab-authored. This is a separate local authoring
  tool; it must not construct Unity HUDs, edit scenes/prefabs, or require a live Editor.
- Agent access is the same typed command boundary as the human UI. It grants no raw SQL,
  arbitrary file path, provider key, executable, arbitrary C# or game/player-state write.
- Preserve stable IDs, exact references, unknown optional data, translations, revision
  history, existing exports/source bytes, and concurrent edits. Never hard-delete
  referenced/published definitions or cascade through release/history dependencies.
- Preview uses explicit synthetic fixtures, fake time, deterministic draw and detached
  furniture/inventory/action ports. It may read-only resolve and pin an exact saved
  source revision through `AuthoringRepository`; it never writes the shared authoring
  store, player save, runtime release, network, provider, TTS, or actual scene. Only
  detached fixture state may change during preview.
- No test, build, Editor, profile, database write, provider/key, or package operation
  is authorized by this planning note. The project being unborn/untracked is not a
  planning blocker; do not stage broadly or replace the existing checkout.

## Review Focus

1. **A kind is advertised before its source/schema/runtime adapter exists.** Test that
   unavailable kinds return `unavailable` with an owner-specific reason and that the
   UI cannot create, validate, preview, or select them.
2. **Concurrent or stale saves lose a writer's data.** Test create-only writes, exact
   `expectedHash` CAS, operation retries, conflict display/reload, and preservation of
   both local edits and the newer stored revision.
3. **A clone changes shared IDs/references or deletion breaks callers.** Test owned-ID
   remapping, preserved shared references, dependency checks, selected-release checks,
   and non-destructive retirement.
4. **Preview or metadata grants behavior.** Test unknown function/effect, stale target,
   missing context and failed primitive as unavailable/unknown/failed; only trusted
   registries supply callable names and the preview writes synthetic state only.
5. **UI and agent results diverge or expose local services.** Test identical normalized
   command traces through CLI and HTTP, plus bad origin/Host, missing token, CSRF,
   oversized payload, path traversal and inert authored text.

---

## Current Source Snapshot (read-only, 2026-10-22)

These are source observations, not new test/build evidence. The Workbench source and
SQLite workspace were inspected without running tests/builds or opening Unity. The
local `Tools/ContentWorkbench/Workspace/authoring.sqlite3` exists and was not read or
modified. The [[Reusable Content Framework Workflow]] now consolidates all four
planning reports and writer windows. Those owner notes remain plans, not implementation.
Main's latest direction is to use the named six-kind contract below while not treating
the DBMS storage/Item mapping as implementation-frozen until its reissued concrete API
is confirmed against that contract.

| Area | Present source support | Workbench gap / honest current state |
| --- | --- | --- |
| Command protocol and SQL | `CoreHost/WorkbenchCommandHost.cs`, `WorkbenchCommands.cs`, `WorkbenchAuthoringStore.cs`, `WorkbenchDraftStore.cs`; one UTF-8 JSON request/response, protocol v1, 1 MiB request bound; typed `authoring.list/read/write`, build-set, import/export/backup and compatibility `draft.*` use `AuthoringRepository` | No `schema.describe`, safe clone/retire command, HTTP service, browser UI, or general six-kind adapter API |
| Dialogue | `generic_dialogue` remains an existing editable/importable legacy shape; master planning adds `dialogue_definition` for new reusable Dialogue sources | No visual Dialogue editor or new source adapter yet. Preserve legacy IDs/bytes; do not convert or create a second Dialogue store |
| Scenario | `scenario_definition` and draft wrapper `scenario`; production `ScenarioDefinitionValidator` and `ScenarioSession` are reused by `WorkbenchPreview` | Scenario graph UI is absent. Do not confuse `ScenarioDefinition`/Flow with a `TopicScenario` entry |
| Topic | `topic_definition` and draft wrapper `topic`; `TopicDefinitionValidator`, shared selector/history are used in preview | Current preview is direct-family/synthetic, not proof of GTE semantic matching. No visual Topic editor or semantic bake in Workbench |
| Furniture | Pure catalogue/validator/resolver sources exist under `Assets/AlterOS/Furniture/`; the master contract names `furniture_definition` and typed `power.set` | No source/Workbench definition adapter yet. `WorkbenchCommands` returns `furniture_core_unavailable` for `catalogue.read` and `arrangement.evaluate` |
| Item | Furniture stock exists as `FurnitureInventoryEntry`, not as general Item inventory; master planning adds `item_definition` plus a separate per-save quantity store | No general Item definition, quantity store, grant transaction, or Workbench adapter exists in source |
| Action | Compiled schema v4 has existing `ActionRecord` metadata (`capabilityId`, `contractJson`); current Activity host supports Movement/Drink executors | Master planning adds `action_definition` schema v1, but reusable sequence/host-effect runtime and Workbench adapter are not implemented |
| Validation/preview | `CoreHost/WorkbenchPreview.cs` accepts only `kind: topic|scenario`, and calls their production validators and shared selector/session with trusted fixture context | Dialogue/Furniture/Item/Action validation and preview are unsupported. Do not present schema parsing as semantic validation |
| UI/service | Existing `build-core.ps1` and `workbench.ps1` produce/run a standalone host without Unity DLL references | No `server.py` or `web/` tree exists; W2/W3 are not implemented |

Furniture catalogue schema 1 and existing capability metadata (including
`activity.watch_tv`) do not install executable functions. The master contract now
specifies `power.set` through `IFurnitureFunctionPort` and typed known numeric resource
`device.power` (0=Off, 1=On); missing remains unknown, and this typed resource alone
does not require a home-layout version bump. `dispenser.collect_mock` is a separate
descriptor-classified presentation operation, not a persistent furniture mutation or
Item grant. A Workbench descriptor must distinguish these contracts from current
source support and may advertise execution only after the trusted handlers land.

`Assets/AlterOS/Authoring/AuthoringContracts.cs` and `AuthoringSchema.cs` currently
enumerate the source/draft kinds, and the SQL `documents.kind` CHECK constraint is
explicit. Authoring SQLite is v1, compiled content v4, game save v3, and progression
JSON v4 in source. `GameCommitRequest` has progression/layout JSON and typed events,
checkpoints, Scenario/Flow/Step/Action execution records, but no first-class Item
quantity field. The master planning migration is authoring v1->v2, content v4->v5,
and game v3->v4; progression JSON remains v4. These are planned migrations, not
source support or Workbench commands.

The shared contract documents W1/headless tests at 27/27; this planning pass did not
rerun them. `catalogue.read` returning `furniture_core_unavailable`, the kind limit on
`content.validate`/`preview.run`, and the missing browser files are confirmed by source.
No unrelated test failure was investigated or newly verified.

## Contract and Version Plan

### One path from editor/agent to authored release

```text
Human browser UI or developer-agent CLI
  -> existing protocol-v1 Workbench command host
  -> AuthoringRepository CAS revisions + trusted domain descriptors/validators
  -> detached shared-runtime preview with synthetic ports
  -> explicit selected SQL snapshot -> existing validated Editor build/install gate
```

No UI/editor code writes SQL, emits runtime JSON, or calls production functions. The
UI and CLI use the same operation names, payload validators and command response.

### UI kinds versus storage kinds

The six UI `kindId` values are stable, lowercase names: `dialogue`, `scenario`,
`topic`, `furniture`, `item`, `action`. They are not assumed to be SQL `kind` values.
`schema.describe` returns the actual source kind mapping and availability.

| UI kind | Canonical master planning mapping | Boundary |
| --- | --- | --- |
| `dialogue` | New `dialogue_definition`; existing `generic_dialogue` remains the legacy editable/importable shape | New Dialogue has no implicit Topic, route or anchor; preserve legacy Generic IDs/bytes and do not convert/delete them |
| `scenario` | Existing `scenario_definition` (complete source) and `scenario` (draft wrapper) | Uses ScenarioDefinition/Flow validator and runner, never a TopicScenario sub-entry as a standalone Scenario |
| `topic` | Existing `topic_definition` and `topic` wrapper | Anchors/entries/reference fields come from the selected source snapshot; semantic matching is not faked |
| `furniture` | `furniture_definition` | Typed Furniture definition and registered-function bindings; placed instances/home layout remain game state |
| `item` | `item_definition` | Item definition, localized display Text and stack limit; player quantity/grant stays in game save |
| `action` | `action_definition` schemaVersion 1 | Stable reusable Action ID; descriptor-validated `implementationKind` data, no executable supplied C# |

The six UI kind IDs are frozen. The source-kind column records the master's canonical
planning names: `dialogue_definition`, `furniture_definition`, `item_definition`, and
`action_definition` are the four additions; preserve established `scenario_definition`,
`topic_definition`, and legacy `generic_dialogue`/`scenario`/`topic` compatibility.
Confirm the DBMS storage/Item API against these names during W1 before freezing the
loader's source-kind mapping or advertising a new kind. Do not invent alternatives or
claim availability before the typed APIs land in source.

The master plans authoring SQLite v1->v2 for the four new source kinds, compiled
content v4->v5, and game save v3->v4 with a transactional `player_item_quantities`
table. Progression JSON stays v4. Content build manifest stays v2 unless its contract
actually changes. Home layout remains versions 1/2 for the typed `device.power`
resource; registering that resource alone does not bump layout schema. These migrations
and rollback/compatibility checks belong to DBMS/Furniture; this Workbench plan neither
implements nor runs them. Preserve existing rows, hashes, releases, source bytes and
legacy IDs.

Keep command `protocolVersion: 1` additive. Add a distinct common
`descriptorVersion: 1` for `schema.describe`; each returned descriptor also carries
its producer `schemaVersion` and `validatorVersion`/`contractVersion`. These versions
are not the SQLite authoring schema or compiled-content schema. The master plans
compiled schema v5 and explicit v4 compatibility; Workbench only consumes the
descriptor/compiler APIs and never advances a release itself.

### Shared descriptor and command shapes (proposed Workbench API)

`schema.describe` is new and separate from `catalogue.read` (furniture data). Request:

```json
{"protocolVersion":1,"requestId":"schema-action-1","operation":"schema.describe","payload":{"kindId":"action"}}
```

Response result carries `descriptorVersion`, `kindId`, `displayName`,
`sourceKinds`, `schemaVersion`, `validatorVersion`, `available`, `unavailableReason`,
capabilities (`list`, `read`, `create`, `edit`, `validate`, `preview`, `clone`,
`retire`), form sections/fields, typed references, graph/step descriptors and
registered operation/function/effect descriptors. `available=false` has a diagnostic;
it is never inferred from a database row accepting arbitrary JSON.

Each field descriptor carries a stable `fieldId`, canonical path, display-text key,
value type/widget, required/nullable/read-only flags, validated bounds/enums, and
reference target/revision policy where relevant. Supported generic controls include
plain/multiline text, localized Text reference, bounded scalar, enum, typed reference,
condition/rule, ordered child list and graph port. The descriptor includes ownership
(`ownedChildId` versus external `ContentRef`) and clone policy; UI code does not infer
ownership from property names. Only explicit metadata can enable a control. Unknown
fields are preserved on round trip and shown read-only until a real schema owner
supports them.

`authoring.list/read/write` remain the one persistence API and use the existing
`AuthoringRepository.List(kind,cursor,limit)`, `Read(documentId,revisionHash)` and
`Save(AuthoringSaveRequest)` contracts. List/read/write operate on actual storage
kinds from descriptors. Incomplete drafts remain readable and diagnosable. Writes
retain `expectedHash` and stable `operationId`; the UI never auto-initializes the
database, imports JSON, writes `default` selection, or publishes on save.

The DBMS source API plan defines `ContentAuthoringValidator.Validate(kind,
documentBytes)` as the one deterministic source validator; Workbench
`content.validate` delegates to it and maps its stable JSON-path diagnostics without
copying rules. The planned typed repository reads are `ReadDialogue(ContentRef)`,
`ReadAction(ContentRef)`, `ReadFurnitureDefinition(ContentRef)`, and
`ReadItem(ContentRef)` from the selected release. These names are planned interfaces;
Workbench capability remains unavailable until implemented and consumed from source.

New independent source documents use per-document `schemaVersion: 1` (not the SQLite
schema version): `dialogue_definition` has stable `id` and ordered beats without an
implicit Topic/route/anchor; `item_definition` has stable `id`, one localized display
Text or pinned `displayNameRef`, and positive `stackLimit` (99 is only the Water-glass
fixture default); `furniture_definition` adds stable identity, localized display and
validated Furniture fields/function bindings; `action_definition` uses the
`implementationKind` contract below. Scenario and Topic retain their current source
schemas and wrappers.

Extend `content.validate` and `preview.run` without changing their envelope. Keep
`kindId` as the canonical discriminator for new callers. Retain the existing `kind`
alias for legacy Topic/Scenario `content.validate` and `preview.run` requests. If both
are present, accept only equivalent values; a mismatch returns `invalid`, stable code
`kind_alias_conflict`, path `/kind`. New kinds use `kindId`. Do not change the existing
storage-kind `kind` fields on `authoring.list` or `authoring.write`.

Keep existing `topic`/`scenario` payloads working; new accepted `kindId` values are only
enabled by a real adapter. Both may consume a saved `documentId` + exact
`revisionHash` or an unsaved editor `document` explicitly labelled synthetic. Every
preview request supplies `fixtureId`; time/draw/events are explicit and bounded.
Responses state `fixtureOnly`, `routingMode`, resolved snapshot/revisions, ordered
trace, result/effect status, and diagnostics with stable code and JSON path. A Topic
direct-family fixture is labelled direct and never proves semantic routing.

Interim `unavailable` results are the safe behavior while owner adapters are missing,
but they do not satisfy the six-kind goal. Final acceptance requires exactly the six
`kindId`s above, each advertising real `create`, `read`, `edit`, `validate`, and
`preview` support, then passing each kind through browser and CLI create/read/edit/
validate/preview round trips with matching revision IDs and normalized diagnostics/
traces. A test over an empty or partial “supplied kinds” list is not acceptance.
Furniture, Item, and Action remain blocked until their producer storage/runtime APIs
land; the final six-kind assertion must stay an explicit failing gate until then.

Add typed `authoring.clone` only after DBMS/domain descriptors define owned-ID maps:

```json
{"protocolVersion":1,"requestId":"clone-1","operation":"authoring.clone","payload":{"sourceDocumentId":"...","sourceRevisionHash":"...","newDocumentId":"...","newLogicalPath":"...","newDomainId":"...","operationId":"..."}}
```

It creates a new document/revision with create-only semantics, remaps the definition
ID and owned child IDs once, preserves external shared references by default, and
returns an explicit ID map/dependency summary. “Make independent copy” of a shared
Text/Dialogue dependency is a separate explicit typed operation. Never clone by
blindly copying JSON in JavaScript.

V1 offers soft `authoring.retire` only if DBMS supplies an atomic CAS/dependency
contract: it prevents new selection/use but keeps immutable revisions and existing
pinned releases readable. Hard delete is unavailable for referenced, selected,
published, or historical content; the first Workbench release has no purge button.
If DBMS has no safe retirement API, descriptor capability is false. Do not invent a
`retired` field or delete SQL rows from this task.

### Shared forms, graph and localization

- Catalogue browser begins from `authoring.list`; opening an item reads its exact
  revision through `authoring.read`. Filters, labels and editable fields come from
  descriptors. “Reload catalogue” replaces only an unpinned view; current edit state
  is retained or an explicit discard is confirmed.
- A single Flow-first graph component renders `ScenarioDefinition` steps and edges;
  Topic is an entry/anchor/selection inspector which references Scenario Flows. The
  selected-step writing inspector uses descriptors for Dialogue, question, branch,
  action, wait, effect and terminal-step fields. No second graph per content kind.
- Dialogue beat text and Dialogue/Furniture/Item display names use exactly one of an
  owned localized Text child or a complete pinned Text ContentRef, never both/neither.
  Owned Text has a stable child ID and `translations[{language,text}]`, compiling to
  the existing Text/Translation rows and dependency closure. The child editor is shared
  and is not a seventh content kind. English/Japanese are initial language defaults,
  not fixed columns; display and speech language remain distinct. No auto-translation,
  hidden effect text, or TTS call.
- Furniture and Item use reusable metadata-driven forms and typed reference pickers;
  their live registry/host status is displayed separately from authoring-valid status.
  Inventory quantity is never an editable Item-definition field. Item creation does
  not require an already-published display Text: an owned localized Text child or a
  complete pinned Text ContentRef satisfies the definition.
- Action editor uses schemaVersion1 fields `id`, `capabilityId`, `implementationKind`
  and descriptor-validated data. `activity_sequence` contains exactly one of ordered
  typed `steps` or trusted `composerId`; both normalize through the same production
  validator/executor. `host_effect` contains only a registered `functionId` and typed
  arguments. `legacy_activity` is reserved for migrated compiled Actions; new source
  cannot claim it. Do not add `scriptId` or `data_recipe` as a second discriminator.
  The descriptor drives canonical primitive IDs `reserve_nearest`, `move_to`, `face`,
  `bark`, `wait`, `invoke_furniture`, `move_near_player`, `checkpoint`, and `complete`,
  plus typed arguments/results, success/failure/cancel edges, and checkpoint policy.
  C# composition is selected as a registered `composerId`, never edited or compiled in
  the Workbench.
- Keep persistent furniture functions distinct from presentation operations. `power.set`
  uses `IFurnitureFunctionPort` mutation outcomes; `dispenser.collect_mock` uses the
  descriptor-classified presentation port and its accepted/running/completed/cancelled/
  failed lifecycle. The latter changes no supply, Item quantity, hydration, stats or
  currency. `invoke_furniture` dispatches through that metadata; there is one Activity
  executor, not a second Workbench lifecycle engine.
- A Scenario Item grant is a version1 `host_effect` step whose existing `actionRef`
  pins a trusted Action binding `player.item.grant` to an exact Item ContentRef and
  positive quantity. Step data contains only `requiresCompletedStepId`; do not add
  `StepRecord.itemRef` or a free-form effect JSON field. Preview uses the same
  `IScenarioHostEffectPort.Apply` contract with detached fixture state.
- Adding another furniture definition, Item definition or Action recipe using already
  registered descriptors must require no new `workbench.js` branch or per-definition
  factory. A genuinely new executable function still requires its owning trusted C#
  implementation/registry change, not just a Workbench declaration.

## Three Proof Flows Through the Same Workbench

These are synthetic authored examples for the Workbench and eventual integration
acceptance, not current API calls or production fixtures. All free-text routes use
the real semantic model/anchor snapshot when that gate is tested; no exact-string
route shortcut or LLM call is allowed.

| Fixture | Shared definitions and ordered Scenario intent | Synthetic preview and later acceptance |
| --- | --- | --- |
| Turn on TV | Topic anchor accepts both `Can you hlep me turn on the tv` and corrected `Can you help me turn on the tv?`; existing favor submenu targets the same Topic. Opening VN -> pinned `activity_sequence` Action -> `reserve_nearest`/`move_to` exact eligible TV -> `invoke_furniture(power.set, On)` -> terminal success | Preview: exact synthetic target binding, simulated function result and terminal trace. Later runtime gate: exact TV instance commits On and its view reflects committed state. Scenario ends there; no required return or dialogue after success |
| Locate TV | Topic anchor `Where is the tv?`; Scenario uses the pinned `activity_sequence` Action with `reserve_nearest`, `move_to`, and authored bark `Hey, it's here` -> end | Preview: correlated simulated arrival + bark acknowledgement, unchanged state and terminal trace. Later runtime gate: move to the TV, bark, then end without power change or forced return |
| Fetch a drink | Topic anchor `Can you fetch me a drink?`; pinned `activity_sequence` Action -> `invoke_furniture(dispenser.collect_mock)` through presentation port -> return to player -> complete VN step `delivery` with `Here, cheer!` -> Scenario `host_effect` step with pinned `actionRef` to `player.item.grant`, pinned `item.water_glass` ref and quantity 1; step data names `requiresCompletedStepId: delivery` | Preview: labelled mock presentation markers, return, completed Dialogue prerequisite and synthetic quantity +1. Later runtime gate: one durable Water glass grant; cancel/fail before grant means none, retry after commit is idempotent. Fetching does not call Alterra's Drink Water Activity or hydrate her |

The Workbench preview shows simulated reservation/function/movement markers and a
synthetic inventory delta; it never claims a real NavMesh/animation/persistence event.
Its parity tests assert traces, not real game commits. Here `invocationId` means Flow
invocation; Scenario `stepExecutionId` and physical Action `actionInvocationId` remain
distinct. The grant requires the actual completed Dialogue step execution, not merely
its definition or queued/presented status. The eventual isolated gameplay proof is a
separate owner gate requiring committed TV state/visible view, no power
change in Locate TV, and the real host-authorized quantity +1 after Fetch dialogue
completion. A second compatible TV definition/instance, a non-TV Action, and a second
synthetic Item grant prove reusable authoring instead of per-TV or Water-glass code.

## Owner Contracts Required Before Implementation

The consolidated workflow has settled the product/storage boundaries. No cross-task
messages are sent by this plan. Remaining dependencies are source implementation,
versioned DTOs and tests, not a request to reopen those product decisions.

| Owner | Source-backed handoff required before Workbench W3 can claim support |
| --- | --- |
| Alterra Behavior | W0/W2 pure sources for `ActivitySequenceDefinition`, builder/descriptor registry, validator/executor, `ScenarioHostEffectRequest/Result`, and `IScenarioHostEffectPort.Apply`. Implement strict `requiresCompletedStepId`, actual completed delivery execution, distinct Flow `invocationId`/Scenario `stepExecutionId`/physical `actionInvocationId`, and deterministic C#/sequence parity tests. No parallel Workbench executor |
| Furniture and Environment | W1 typed function/descriptor and presentation-port sources. Persistent `IFurnitureFunctionPort.Invoke` returns `applied/no_change/replayed/unavailable/conflict/failed` plus committed evidence. The `device.power` known numeric resource is 0=Off/1=On, absent is unknown, and does not itself bump layout schema. `dispenser.collect_mock` uses its separate presentation lifecycle and has no durable Item/stat effect |
| DBMS | W1 source implementation for authoring SQLite 1->2, the four new source kinds, typed validation/projection, compiled content 4->5, game save 3->4 and `player_item_quantities`; preserve progression JSON4 and layout1/2. The reissued plan proposes `ContentAuthoringValidator.Validate(kind,documentBytes)`, `GameCommitRequest.itemQuantityChanges`, `IGameRepository.ReadPlayerItemQuantities()` and typed `GameItemQuantityChangeRecord`/view/result DTOs. Grant must atomically commit quantity/event/context/receipt; same operation ID+payload returns the prior receipt before stale-revision rejection, while payload reuse conflicts. Confirm these source-owned names in the W1 implementation; Workbench consumes them and never touches player persistence |
| Main | Keep the six UI kinds and master planning mapping aligned, reconcile any implementation-level API drift, and acknowledge W3 after W0-W2 release. Main owns W5 publication/startup/player acceptance coordination |

Topic Playground consumes these producer contracts and owns only Workbench adapters,
wire/UI/CLI, synthetic fixtures and its tests. If any producer contract is unavailable,
the corresponding Workbench capability remains visibly unavailable. No parallel
sequence engine, furniture registry, Item store, or second AuthoringRepository.

## Implementation Tasks (not started)

Execute sequentially in the existing Topic Playground session. The owner-contract
gate is a real dependency: tests may use explicitly synthetic descriptor fixtures,
but a mock cannot make production support report `available`.

### Master writer-window alignment after explicit START

| Window | Sole writer | Workbench dependency |
| --- | --- | --- |
| W0 | Behavior: portable Activity/Scenario contracts and pure tests | Consume released BCL-only sequence and `IScenarioHostEffectPort` APIs |
| W1 | DBMS: authoring/content/game schema, validators, migrations and typed storage; Furniture separately owns its function/state adapters | Consume actual six-kind source mappings, descriptors, Item grant, registered Furniture functions and validated release projection |
| W2 | Behavior: host/Scenario/presenter integration | Consume exact Action/Scenario execution and completed-Dialogue effect semantics |
| W3 | Topic Playground: all `Tools/ContentWorkbench/**` | This plan's only implementation window; one acknowledged writer after W0-W2 releases |
| W4 | Furniture: acknowledged exclusive Unity Editor/scene/prefab presentation | No Workbench-owned Editor work; read-only consultation only |
| W5 | Main: reviewed release selection and isolated startup/gameplay acceptance | Report Workbench fixture evidence separately from published/live proof |

These windows are not active locks or START. The Workbench owner waits for main's
explicit W3 acknowledgement; no idle status or existing artifact implies ownership.

### Task 0: Freeze cross-owner inputs and one-writer window

**Files:** Read only `Documentation/Tracking/Reusable Behavior Framework Plan.md`,
`Furniture Functions Framework Plan.md`, `Reusable Content Storage and Items Plan.md`,
the current Shared Contract/ERD, and the owner-provided source/API lists. Main owns the
master workflow and shared summaries; this session owns only its named Workbench plan
at this stage.

**Interfaces:** Output one reviewed contract matrix: UI kind -> storage kind -> owner
validator/registry -> BCL source set -> descriptor/schema/validator version -> fixture
and test command. Match the master versions (authoring 1->2, content 4->5, game 3->4,
progression JSON4 unchanged, layout1/2 unless Furniture later adds a separate typed
field) and keep command protocol v1 additive. Compare DBMS's proposed Item API to the
actual W1 source contract before advertising the four added kinds.

- [ ] Read the four named owner plans and master consolidation; keep the six UI IDs
      fixed and use the master source names as the W1 mapping target.
- [ ] Check the actual DBMS W1 APIs/migrations and Behavior/Furniture source contracts
      against those names before freezing the loader mapping or declaring any new
      descriptor available; report mismatches to Main instead of inventing a mapping
      or adding a Workbench-local schema.
- [ ] Wait for main's explicit W3 writer acknowledgement after W0-W2 release; no
      self-granted `Tools/ContentWorkbench/**` window.
- [ ] Fail closed for any missing owner result. Do not write bridge stubs into
      `Assets/**` or alter the master workflow/ERD.

### Task 1: Add truthful kind discovery and typed ID/reference-safe authoring

**Files:** Modify `Tools/ContentWorkbench/CoreHost/WorkbenchCommands.cs`,
`WorkbenchAuthoringStore.cs`, and `build-core.ps1`; add
`CoreHost/WorkbenchSchemaCommands.cs`. Tests: extend
`tests/test_commands.py` and `tests/test_authoring_sqlite.py`; fixtures under
`Fixtures/contracts/` only when clearly marked synthetic.

**Interfaces:** `WorkbenchCommands.Handle(object rawRequest, WorkbenchEnvironment
environment) -> object` remains the single protocol-v1 dispatcher.
`schema.describe({kindId}) -> WorkbenchResult` returns descriptor v1. Existing
`authoring.list/read/write` keep their envelope and delegate to the same
`AuthoringRepository`. New `authoring.clone` consumes source document/revision,
new repository/domain IDs, logical path and operation ID. `authoring.retire` is
advertised only when the DBMS typed API is available.

- [ ] Add red command tests: six `kindId` values return owner descriptor or explicit
      `unavailable`; current missing Action/Item/Furniture adapters never look editable;
      supported fields/refs exactly match their real provider descriptors.
- [ ] Add a separate final acceptance test that asserts the discovered kind set equals
      `{dialogue, scenario, topic, furniture, item, action}` and that every descriptor
      advertises create/read/edit/validate/preview. It intentionally remains a failing
      integration gate until each producer adapter is real; do not filter out missing
      kinds or infer coverage from a partial fixture list.
- [ ] Add red storage tests for each of the six required kinds: create/read/edit/reopen
      through a temporary SQL DB; stale `expectedHash` conflicts; unknown metadata and
      localization survive; no direct SQL/file store is involved. Missing DBMS source
      kinds keep the full-kind gate pending, not green.
- [ ] Pin the new-source schema fixtures: `dialogue_definition`, `item_definition`,
      `furniture_definition`, and `action_definition` are schemaVersion1. A Dialogue
      beat/definition label and Furniture/Item display name has exactly one owned
      localized Text or complete pinned Text ContentRef; Item stackLimit is positive
      (99 only for the Water-glass fixture). `activity_sequence` Action must have
      `steps` XOR trusted `composerId`; `host_effect` must use only its registered
      `functionId` and typed args; new source cannot claim `legacy_activity` or supply
      `scriptId`/`data_recipe`.
- [ ] Add red clone/retire safety tests: new IDs are unique, only owned IDs remap,
      shared refs remain pinned, referenced/selected/published content cannot be
      deleted, and a conflict retains both revisions. Missing DBMS retirement support
      makes the UI capability unavailable rather than adding row deletion.
- [ ] Run `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_commands.py'`
      and `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_authoring_sqlite.py'`;
      expect the new tests to fail for missing descriptor/safe-copy behavior.
- [ ] Implement the descriptor/command glue and typed policies using owner APIs. Keep
      SQLite open/Save/transactions in `AuthoringRepository`; never duplicate them in
      `WorkbenchAuthoringStore` or the browser.
- [ ] Rebuild with `Tools/ContentWorkbench/build-core.ps1`; rerun the same tests.
      Existing kinds stay compatible; unsupported additions remain unavailable.

### Task 2: Extend shared validation and synthetic preview to six kinds

**Files:** Keep `CoreHost/WorkbenchPreview.cs` as dispatcher and existing
Topic/Scenario path; add focused `WorkbenchDialoguePreview.cs`,
`WorkbenchFurniturePreview.cs`, `WorkbenchItemPreview.cs`, and
`WorkbenchActionPreview.cs` adapters only where a production pure contract exists.
Extend `tests/test_commands.py`; add deterministic per-kind fixtures under
`Fixtures/`.

**Interfaces:** `content.validate({kindId,document|documentId+revisionHash,fixtureId})`
and `preview.run({kindId,document|documentId+revisionHash,fixtureId,instanceId,
nowUtc,draw,events})` return the existing response envelope plus bounded stable-code
diagnostics. Adapters call production validators and selectors/executors; their
synthetic context comes only from a named trusted fixture, not the document under test.

- [ ] Red tests: invalid schema/reference/action/effect/translation paths return stable
      errors with JSON paths; unknown/unsupported values visibly fail; a request cannot
      self-authorize functions, item grants, fixture capabilities or target IDs.
- [ ] Red compatibility tests keep legacy `{kind:"topic"}` and `{kind:"scenario"}`
      requests working. `kindId` is canonical; conflicting `{kind,kindId}` returns
      `kind_alias_conflict` at `/kind`. `authoring.list/write.kind` continues to mean
      storage kind and must not be reinterpreted as a UI kind.
- [ ] Red preview tests cover Dialogue beats/choices, Topic selection vs direct-family
      labeling, Scenario transitions/lifecycle, Furniture capability/target results,
      Item definition and synthetic grant trace, and Action primitive outcome/failure/
      cancellation. No real `GameCommitRequest`, live host, provider, TTS or filesystem
      write may occur.
- [ ] Exercise the same canonical `invoke_furniture` primitive for persistent
      `power.set` and descriptor-classified presentation `dispenser.collect_mock`, but
      keep their result schemas distinct. The Scenario `host_effect` preview resolves
      pinned `actionRef` to `player.item.grant`, requires its named completed Dialogue
      execution, and never accepts a duplicate `StepRecord.itemRef` or effect JSON.
      Preserve Flow invocation ID, Scenario stepExecutionId and physical Action
      actionInvocationId as distinct trace fields.
- [ ] Assert that queue/open is not completed delivery: the synthetic grant trace
      includes `completedDeliveryStepExecutionId` only after the actual Dialogue
      terminal acknowledgement and keeps it correlated to the required
      `requiresCompletedStepId`. Same-step replay remains idempotent; a separate
      legitimate run/step may grant again. Database receipt-before-stale and atomic
      quantity/event/context/receipt behavior is verified by DBMS-owned tests, not a
      Workbench-side persistence implementation.
- [ ] Add a full-kind gate asserting all six kinds can both validate and preview through
      their real adapters. An `unavailable` response is a correct interim safety result,
      but is not a passing final-support assertion.
- [ ] Pin each fixture's source revision, `fixtureId`, time, draw and event order. Assert
      candidate rejection/unknown reasons, selected definition revisions, object
      binding, operation/result identity, item count delta and final state.
- [ ] Run focused `test_commands.py` and `test_authoring_sqlite.py`, then
      `python -m unittest discover -s Tools/ContentWorkbench/tests`; observe the new
      preview cases fail for missing kind adapters before implementing them.
- [ ] Implement only glue to shared production APIs. Keep Topic semantic matching
      honest: direct-family fixture is not GTE evidence; new anchors need a selected
      SQL snapshot bake/index gate outside preview.
- [ ] Rerun the focused and full Workbench suites and require green. Run producer-owned
      `Tools/ContentData/Check-ContentData.ps1 -Group Authoring`, `Bake`, `Publish`,
      `Semantics`, and Behavior/Furniture pure tests only after their interfaces land;
      report each owner's exact results separately.

### Task 3: Add the loopback UI service and shared agent entry point

**Files:** Add `Tools/ContentWorkbench/server.py`, `launch.ps1`, and
`tests/test_server.py`; preserve `workbench.ps1` and the C# host as the sole command
implementation. Update `build-core.ps1` only for explicit BCL-only producer files.

**Interfaces:** `POST /api/command` accepts exactly the protocol-v1 command envelope
and invokes the same executable used by `workbench.ps1` through fixed
`subprocess.Popen(argv, shell=False, stdin=PIPE, stdout=PIPE, stderr=PIPE)` arguments.
The Python server sends strict UTF-8 bytes with a 1 MiB request limit and concurrently
drains/caps raw stdout at 8 MiB and stderr at 64 KiB; it decodes only after successful
bounded collection. A finite 30-second child timeout applies. Output overflow or
timeout terminates and reaps the child, returning an unavailable diagnostic. The
existing `workbench.ps1` keeps its current .NET `ProcessStartInfo` implementation
unchanged. `GET /api/catalogue` may
expose the Furniture `catalogue.read` snapshot when available; it is never a substitute
for `schema.describe` or `authoring.list`. Static routes serve only allowlisted `web/`
assets.

- [ ] Red tests for bind-address/ephemeral port, random per-launch session token,
      exact Host/Origin, CSRF-protected writes, invalid content type, 1 MiB body bound,
      path traversal, unknown static route, inert dialogue HTML, timeout/host exit and
      useful unavailable diagnostics. No shell or user-selected executable.
- [ ] Test with the built host and a temporary SQL workspace; opening the UI calls
      `authoring.info` only. It never calls `authoring.initialize/import`, writes the
      shared `Workspace/authoring.sqlite3`, scans JSON, or changes the build set.
- [ ] Run `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_server.py'`
      and the `test_commands.py` suite before implementation; expect missing route/auth
      behavior to fail. A keyless launch/stop touches synthetic storage only and
      requires no Editor or network access.
- [ ] Implement the stdlib loopback transport. Agent users keep using
      `workbench.ps1` with the same commands; neither UI nor HTTP gains a privileged
      alternate operation path.
- [ ] Rerun `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_server.py'`
      and `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_commands.py'`;
      require all security and parity assertions green.

### Task 4: Build the real Flow-first six-kind browser editor

**Files:** Add `web/index.html`, `web/workbench.css`, `web/workbench.js`,
`web/graph-editor.js`, `web/draft-editor.js`, and `web/playground.js`; add or extend
`tests/test_ui_commands.py` for command-path parity. Keep all authoring controls in
these local Workbench assets, not Unity Canvas.

**Interfaces:** All UI calls use `/api/command`; catalogue/read/edit/save/validate/
preview use the same `schema.describe`, `authoring.*`, `content.validate` and
`preview.run` operations used by agent CLI. A generic field renderer consumes
`WorkbenchKindDescriptor`; one graph component handles Flow structure and one
selected-step inspector handles typed fields.

- [ ] Red UI-path tests: list/open/create/save/reopen each available kind; edit a
      Dialogue translation, Scenario edge, Topic anchor/reference, Furniture function
      choice, Item definition and Action typed step through metadata controls; unsupported
      fields survive unchanged; validation navigates to the exact failing field/step.
- [ ] Keep a final browser acceptance matrix separate from interim availability tests:
      assert exactly the six required kinds and that each can create/edit/read/validate/
      preview; exercise each operation through browser and agent CLI and compare returned
      revision IDs, diagnostics and normalized traces. Do not pass by iterating only
      over currently available kinds.
- [ ] Red state tests: stale CAS shows a compare/reload conflict without dropping the
      local draft; clone has an explicit ID/dependency summary; retire preserves history;
      referenced/published delete is absent or rejected. Add local undo/redo and dirty
      navigation warning from the existing W3 contract.
- [ ] Red extensibility test: register a second synthetic Furniture/Item/Action
      descriptor/definition in the trusted fixture; it appears in lists and controls
      without a kind/model-specific `workbench.js` branch. It must remain labeled
      synthetic until backed by real owner registries.
- [ ] Implement Flow-first navigation plus metadata-driven forms, typed pickers,
      bilingual text, supported function/step palette, locked/unavailable reasons,
      validation errors and detached preview trace. Do not ship a static mockup that
      cannot save/reopen through SQL commands.
- [ ] Run `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_ui_commands.py'`
      and the full Python suite. Use an available real browser for catalogue -> edit ->
      validate -> preview -> save -> reopen, keyboard/viewport/error/conflict review;
      if unavailable, record visual acceptance as pending (do not install a package).

### Task 5: Prove the three reusable flows and parity, then hand off

**Files:** Only `Tools/ContentWorkbench/Fixtures/` and its tests under the acknowledged
Workbench writer window. The three production-domain definitions and runtime state
remain with Behavior/Furniture/DBMS owners. Main owns shared TODO/Home/Feature Register/
ERD and final consolidated status.

**Interfaces:** Author each fixture once through browser commands and once through
`workbench.ps1`; both use the same SQL temp-store contract, descriptors, validators,
runtime test ports, revision pins, time and draw. Compare normalized selected IDs,
diagnostics, object identity, step trace, result/effect status and persisted draft hash.

- [ ] Final content-kind gate asserts all six descriptors advertise real create/edit/
      read/validate/preview support, then performs browser and CLI round trips for each.
      Compare exact IDs/revisions plus normalized validation diagnostics and preview
      traces. Any producer `unavailable` adapter keeps overall acceptance blocked; it
      may not be removed from the expected set.
- [ ] Red parity assertions fail if UI/CLI choose different definitions, gate reasons,
      return/dialogue order, function result, operation receipt, quantity delta or end
      state; no comparison of “both returned ok” alone.
- [ ] Run the new parity tests against the current host to observe the missing UI route
      and typed-kind behavior before adding the complete synthetic flows.
- [ ] Author each proof fixture through the UI and `workbench.ps1`, using only isolated
      SQL and host-provided synthetic ports. Turn on TV uses both exact required phrases
      and the existing favor reference; assert semantic routing separately from direct-
      family preview, exact synthetic target binding, terminal function trace, and no
      return step. Do not claim a real TV commit from Workbench preview.
- [ ] Locate TV trace asserts the exact bark follows simulated arrival, no power
      operation occurs, and no return is forced. Fetch trace asserts explicit mock
      take-drink marker, return, automatic `Here, cheer!` VN completion, then one
      synthetic host-authorized `item.water_glass` grant with run/step operation ID.
      Exercise replay, cancellation before/after grant, failed return, and no Alterra
      hydration. Check second TV/independent instance, non-TV Action, and second
      synthetic Item through existing forms without UI code changes.
- [ ] Hand the eventual committed-TV, actual movement/bark, and durable Item-quantity
      player checks to the owner-coordinated P4 acceptance gate. Include the post-end
      pose safeguard only after both interaction and Scenario end; it must never
      teleport an actor, force a return, or release a safety-critical reservation late.
      This Workbench task does not run GTE inference, Unity, a live function, or a
      player profile.
- [ ] Run `Tools/ContentWorkbench/build-core.ps1`,
      `python -m unittest discover -s Tools/ContentWorkbench/tests`, affected producer
      source suites, and `Tools/ContentData/Check-ContentData.ps1 -Group Authoring`,
      `Bake`, `Publish`, and `Semantics` where applicable. Check the built
      host's reference table contains no Unity assemblies. Report actual browser UI and
      CLI parity separately from pure preview, compiled release, Unity, and gameplay.
- [ ] Stop before real content publication/runtime installation unless a separate
      reviewed release gate authorizes it. Final handoff lists exact owned files,
      protocol/descriptor/content schema versions, test commands/results, remaining
      owner contracts and the later `SplashScene -> ModelTest` isolated acceptance.
      Main updates shared summary pages; this session does not edit them concurrently.

## Remaining Owner Implementation Evidence

The product boundaries and planning mappings above are settled by the master workflow.
The DBMS note now gives a concrete storage/Item design, and Behavior/Furniture notes
specify matching ports. These are still plans; the following source evidence must land
in their assigned windows before W3 can report a kind as supported. No cross-task
questions are sent by this plan.

- **DBMS W1:** implement the canonical source kinds and authoring v1->v2 migration,
  byte-preserving CAS repository, `ContentAuthoringValidator.Validate(kind,
  documentBytes)`, compiled content v4->v5 typed reads/dependency closure, and game
  v3->v4 `player_item_quantities`. The plan proposes `GameCommitRequest.itemQuantityChanges`,
  `IGameRepository.ReadPlayerItemQuantities()`, and typed `GameItemQuantityChangeRecord`/
  view/result DTOs. Final public field names are DBMS-owned; Workbench must consume the
  released interfaces, not duplicate them.
- **Behavior W0/W2:** implement the BCL-only `activity_sequence` definition/descriptor,
  validator/executor and C#/composer parity; canonical `invoke_furniture` primitive;
  Scenario `host_effect` via public pure `IScenarioHostEffectPort.Apply`; and the
  presenter's actual completed delivery step. New Action source has `id`,
  `capabilityId`, and `implementationKind`; `activity_sequence` uses `steps` XOR
  trusted `composerId`, `host_effect` uses registered `functionId` plus typed args,
  and migrated compiled records alone use `legacy_activity`. No `scriptId`,
  `data_recipe`, duplicate `StepRecord.itemRef`, or free-form effect authority.
- **Furniture W1/W4:** implement the registered `power.set` descriptor/handler through
  `IFurnitureFunctionPort`, with committed mutation evidence separate from sequence
  lifecycle; use typed `device.power` known0=Off/known1=On, absent=unknown, without a
  layout-version bump for that resource alone. Implement `dispenser.collect_mock` as a
  presentation-port lifecycle (`accepted/running/completed/cancelled/failed`) which
  grants nothing and changes no durable furniture/Item/player state.
- **Main W3/W5:** acknowledge the W3 Workbench writer window only after W0-W2 APIs are
  released, then later coordinate pinned-release selection and isolated runtime gates.
  The Workbench never publishes or performs W4 Editor work itself.

## Planning Completion and Stop Condition

This plan is complete as a Workbench proposal, not as implementation or project-goal
acceptance. It is aligned to the master workflow's consolidated producer contracts and
W0-W5 writer windows. The DBMS kind/storage/Item APIs and Behavior/Furniture runtime
ports remain source-implementation gates; W3 must wait for W0-W2 release and Main's
explicit writer acknowledgement. Human approval and a separate implementation START
are still required.
Until then this note is the only owned write from this task. No tests/builds/Editor,
HTTP listener, database operation, player/profile access, provider call, package change,
scene/content edit, inter-session message, or shared-page update was made.
