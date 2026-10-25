---
title: Reusable Content Storage and Items Plan
date: 2026-10-22
tags: [alteros, planning, sqlite, authoring, content, items, persistence]
status: in-progress-w1b
---

# Reusable Content Storage and Items Plan

> Approved implementation plan for the Reusable Content Framework. W1A is released;
> W1B round-three fixes and focused headless verification are complete, awaiting scoped persistence re-review. The required SQLite editing UI remains
> owned by Topic Playground under W3; this DBMS handoff does not mark that editor complete.

## Current implementation status — 2026-10-23

- W1A is complete and independently released; see
  `Documentation/Verification/2026-10-22 Reusable Storage W1.md`.
- W1B producer-contract fixes are implemented; scoped re-review has not yet released
  the source. Compiled-content schema v6 adds the `host_effect` Scenario Step and
  Furniture point/use contract keys. Writable stores migrate v4 through v5 to v6;
  read-only v4/v5 stores remain readable. The original v4 artifact is unchanged.
- ActivitySequence source validation and projection use the trusted
  `ActivitySequenceCatalogFactory`; composer arguments and data-authored steps share
  normalized rows. Scenario sources preserve reusable pinned Dialogue/Action refs;
  `host_effect` v1 gates resolve only to a dominating Dialogue Step. Furniture
  source validation uses `FurnitureCatalogueValidator.ValidateDefinition`, preserves
  function/presentation bindings, and persists exact point/use contract mappings.
- The Editor host vocabulary uses the same trusted catalog and registered
  `player.item.grant` capability list. Item grants require durable Flow/Step evidence
  and an exact release-pinned compiled Flow/Action. Furniture power events carry
  typed Flow execution context; `no_change` operations use receipt-only commits.
- Current round-three focused headless checks pass: ReusableDefinitions 6/6,
  Flows 7/7, and Persistence All 29/29. The SQL release-to-grant path hydrates
  exact Step references and preserves Action ContentRef provenance through replay.
  Authoring 8/8, Bake 11/11, Publish 6/6 and ContentSchemaMigration 1/1 remain
  passing from the round-two verification. Game-history schema v5 pins each new
  invocation's Flow store/revision/hash;
  schema-v4 rows remain unpinned and cannot authorize new grants. Exact commands and limits are in
  the dated W1B report.
- Main-session scoped re-review, Unity Editor compile/import, offline selected-snapshot
  GTE model bake, and the user's live game tests remain open. No Unity Editor, user
  profile, Workbench file, provider, shared authoring DB, or player save was touched.

## Goal and ownership

Provide durable, revisioned storage contracts for the six shared content kinds—
Dialogue, Scenario, Topic, Furniture, Item, and Action—then add the smallest real
player Item inventory path needed to prove a Scenario can grant one Water glass.
Keep authoring SQLite, compiled release SQLite, and per-save game SQLite as three
separate stores with explicit boundaries.

DBMS owns the authoring/content projection, compiled-release schema, and game
quantity persistence described here. Topic Playground owns all
Tools/ContentWorkbench files, commands, service, and editing UI. Alterra Behavior
owns Scenario step and host-effect composition. Furniture and Environment owns
furniture definitions, function semantics, and the typed power-state representation.
Implementation must use acknowledged writer windows from the master workflow.

The Workbench must let a person create, edit, save, validate, and preview SQLite-
backed documents. It must use the shared authoring interfaces below; direct SQL
editing or a second JSON database is not the authoring workflow.

## Verified source state on 2026-10-22

- AuthoringSchema is version 1 with eight tables. Its documents.kind CHECK excludes
  independent dialogue_definition plus general Furniture, Item, and Action
  documents. AuthoringRepository already has
  List(kind,cursor,limit), Read(documentId,revisionHash), Save(request) with
  expected-hash compare-and-swap and operation receipts, build-set reads/writes,
  and explicit ReadSnapshot(documentIds).
- Authoring documents are retained as original bounded JSON bytes and revision
  hashes. The repository is a storage boundary, not a per-kind visual editor or a
  substitute for typed content validation.
- ContentSchema is version 4. Its actions table stores capabilityId/contractJson
  rows, and LegacyContentAdapter currently derives ActionRecord IDs from each
  Scenario and step. There are no Item or Furniture tables/models in the compiled
  package. Existing ContentRef pins store, ID, revision, and expected hash.
- ContentBakeService.BakeFromSnapshot consumes an explicit AuthoringSnapshot.
  ContentRepository.Stage/Publish/ReadRelease and RuntimeContentStore's trusted
  release opening already provide stage, explicit head publication, pinned read,
  and runtime release selection. Do not publish during authoring save or bake.
- GameSchemaMigrations is version 3. GameCommitRequest requires the current
  progression JSON and expected game revision; it can atomically write optional
  home layout, events, typed histories, and operation_receipts. It has no item
  quantity change. Progression JSON is schema 4 and is currently written by several
  typed owners.
- The current player-owner convention in ValidateHistoryReferences is
  legacyPlayerId ?? accountId ?? saveId. Keep item counts scoped to that owner.
- Furniture stock is FurnitureInventoryEntry(definitionId,storedCount) inside a
  home layout. It is not the player's Item inventory. Furniture instance state is
  likewise stored through HomeFurnitureLayout and FurniturePersistenceAdapter, not
  through a new item-specific writer.
- No item.water_glass definition, player item quantity model, or grant transaction
  exists. The exact accepted behavior and no-runtime-start boundary are in
  [[Reusable Content Framework Workflow]].

These are source observations only; they are not build or gameplay evidence.

## Three storage boundaries

| Store | Authority | Selection and mutation rule |
| --- | --- | --- |
| Authoring SQLite | Editable source documents, revision history, operations, build sets | Save a document by expectedHash and stable operationId; preserve source bytes and CAS conflicts |
| Compiled content SQLite | Immutable typed rows and dependency closure for each published release | Bake from one captured snapshot; stage, validate, then publish an explicit release head with expected head revision |
| Per-save game SQLite | Progression/layout snapshots, events, histories, receipts, and player item counts | One host-created commit writes item count, event/context, and receipt in the existing transaction with expected game revision |

An authoring save does not select a runtime release. A release never stores player
quantities. A game save never becomes a second editable content catalogue.

## Six-kind mapping and stable references

Keep the established source kinds for Topic and Scenario. Add
dialogue_definition, furniture_definition, item_definition, and action_definition
as source kinds. Keep existing generic_dialogue documents editable/importable under
their current shape and IDs; do not convert or delete them. The Workbench may
display the six human-facing names, but each row resolves through one canonical
kind registry. Retain topic and scenario draft kinds for backward compatibility;
they are draft wrappers, not alternate compiled schemas. Keep legacy/raw source
kinds importable and read-only unless their own workflow explicitly supports editing.

| Workbench kind | Authoring document kind and source | Compiled representation | Runtime lookup |
| --- | --- | --- | --- |
| Dialogue | new dialogue_definition JSON for independent dialogue with no required Topic/anchor; existing generic_dialogue JSON remains unchanged; nested Scenario dialogue normalizes through the same adapters | TextRecord, TranslationRecord, DialogueRecord, BeatRecord | ReadText plus typed ReadDialogue(ContentRef) using the pinned ref |
| Scenario | scenario_definition JSON; legacy scenario variants remain explicit imports | FlowRecord, StepRecord, transitions, choices, shared Dialogue refs, ActionRecord refs | ContentRepository.ReadFlow and the shared Scenario resolver |
| Topic | topic_definition JSON; topic/scenario drafts normalize on build | TopicRecord and TopicEntryRecord with pinned Flow refs; existing anchors and links | ContentRepository.ReadTopic and the selected release's route/anchor indexes |
| Furniture | new furniture_definition JSON; import the existing Furniture catalogue explicitly without rewriting its source | FurnitureDefinitionRecord plus registered-function bindings, localized text refs, and release dependencies | Pinned Furniture definition lookup; function IDs resolve only in the installed host registry |
| Item | new item_definition JSON | ItemRecord, localized display TextRecord ref, stack limit, immutable revision | ContentRepository.ReadItem(ContentRef) from the selected release |
| Action | new action_definition JSON | Existing ActionRecord with implementationKind activity_sequence or host_effect; lossless normalized sequence steps or a pinned host-effect function and typed arguments | New typed ReadAction(ContentRef); only installed registered composers, primitives, and functions can execute |

Do not derive new reusable Action IDs from Scenario/step IDs. Keep the current
LegacyContentAdapter behavior for legacy Scenario source so old references remain
valid, but authored Action documents have their own stable IDs and may be referenced
by multiple flows. Do not rename or delete the existing actions table: it already
holds valid compiled action rows.

New Dialogue documents contain ordered beats with text/expression and no mandatory
route or anchor. A dialogue_definition referenced from multiple Scenarios is the
same pinned Dialogue revision, not copied dialogue content. For each new Dialogue
beat and Furniture/Item display label, accept exactly one of (a) an owned localized
Text object with a stable child ID and translations[{language,text}], or (b) one
complete pinned Text ContentRef. Never accept both or neither. Owned Text compiles
to the same texts/translations rows and release closure as a pinned reference, so a
new Item label does not require a prior published release.

Use stable, kind-qualified IDs for new definitions, including item.water_glass.
Do not rewrite old IDs merely to add a prefix. ContentRef remains the cross-document
reference shape; every new Scenario, Action sequence/host-effect, Furniture binding, and Item
effect must resolve against the same store and immutable selected revision. The
release dependency closure records the kind, ID, revision, store, and expected hash;
cycles, missing refs, stale hashes, and unregistered functions fail validation.

### Shared read, write, validate, select, and release interfaces

Keep the current authoring repository methods as the single persistence API:

- Read: AuthoringRepository.List(kind,cursor,limit) and
  Read(documentId,revisionHash).
- Write: Save(AuthoringSaveRequest) with documentId, kind, logicalPath,
  expectedHash, exact documentBytes, and operationId. Updates add an immutable
  document_revisions row; conflicting expectedHash returns conflict and does not
  replace another writer's head.
- Select: SaveBuildSet(AuthoringBuildSetRequest), ReadBuildSet(name), then
  ReadSnapshot(the explicit document ID selection) to capture exact head hashes.
  A build set is a mutable named selection; the captured snapshot and its hash are
  what a particular bake consumes.
- Validate: add one typed source validator entry point,
  ContentAuthoringValidator.Validate(kind,documentBytes), and extend the existing
  ContentPackageValidator/ContentFlowAdapter to validate the normalized package,
  refs, registered-function vocabulary, and transitive dependencies. SQLite Save
  remains byte-preserving and CAS-based; invalid drafts may be saved, but an invalid
  snapshot cannot bake or publish.
- Bake: retain ContentBakeService.BakeFromSnapshot(snapshot,outputPath,semanticInput).
  Extend its projection/compiler to recognize the four new kinds and typed shared
  refs; never recapture mutable authoring heads midway through one bake.
- Stage and release: retain ContentRepository.Stage(package),
  Publish(release,headName,expectedHeadRevision,...), ReadRelease(releaseId),
  and ReadReleaseHead(headName). Add typed ReadDialogue, ReadAction,
  ReadFurnitureDefinition, and ReadItem(ContentRef) methods that enforce the
  requested revision/hash.
- Runtime select: retain RuntimeContentStore.OpenTrustedRelease(configuration)
  with the expected store, release ID, and manifest hash. Runtime must not follow an
  authoring head or silently select the newest release.

The Workbench's list/read/save/validate/build-set/bake/publish commands should call
these same interfaces. Topic Playground owns its command and UI implementation; no
second set of validators or direct table mutations belongs in that UI.

## Minimal real Item definition

Add a versioned item_definition document with a stable ID and localized display
name reference. The initial fixture is:

- ID: item.water_glass.
- Display name: Water glass, through the existing TextRecord/TranslationRecord
  system rather than a duplicated string in every Scenario.
- Stack limit: 99 for the minimal reusable quantity contract; the Fetch fixture
  grants quantity 1.
- No hydration, thirst, currency, equipment, crafting, or food effects are implied
  by the Item definition.

Proposed compiled ItemRecord fields are id, revision, semanticHash, displayTextRef,
and stackLimit. Add an items table keyed by (item_id,revision), with semantic hash,
text ID/revision, positive signed-64-bit stack limit, and row hash. Link the text ref with
the same composite-FK pattern used by dialogues and beats. The Item validator
requires a stable ID, valid display Text ref, a positive signed-64-bit stack limit,
and no unknown
effect fields. item.water_glass must appear in the selected immutable release before
an effect can grant it.

The new source JSON documents are versioned independently of SQLite:

- dialogue_definition: schemaVersion 1, stable id, ordered beats, and for each beat
  exactly one owned localized text or pinned textRef plus the expression field.
  It has no implicit Topic, route, or anchor property.
- item_definition: schemaVersion 1, id, exactly one owned localized displayName or
  pinned displayNameRef, and stackLimit. The fixture uses item.water_glass and 99.
- action_definition: schemaVersion 1, id, capabilityId, implementationKind, and
  descriptor-validated data. activity_sequence requires exactly one of ordered
  steps or a trusted registered composerId; both lower into the same sequence.
  host_effect accepts only a registered functionId and typed arguments. New source
  cannot claim legacy_activity.
- furniture_definition: schemaVersion 1, id, exactly one owned localized
  displayName or pinned displayNameRef, the validated existing FurnitureDefinition
  fields (category, scale/bounds/support/navigation policy and points), plus
  bindings to registered function IDs. Import the present catalogue explicitly;
  do not change its bytes as an implicit migration.

For Action activity_sequence steps, preserve stable stepId, primitiveId, typed
arguments, result bindings, success/failure/cancel edges, and checkpoint policy.
Canonical primitive IDs are reserve_nearest, move_to, face, bark, wait,
invoke_furniture, move_near_player, checkpoint, and complete. Descriptor versions
define their typed arguments/results. Do not store only ordinal and functionId.

The content DB's proposed v5 tables keep stable identity and immutable revision
keys. Fields below are names/types to implement with the repository's current
SQLite-net conventions; row_hash covers canonical stored values:

    items(item_id TEXT NOT NULL CHECK(length(item_id)<=128),
      revision TEXT NOT NULL CHECK(length(revision)=64),
      semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64),
      display_text_id TEXT NOT NULL, display_text_revision TEXT NOT NULL,
      stack_limit INTEGER NOT NULL CHECK(stack_limit > 0),
      row_hash TEXT NOT NULL CHECK(length(row_hash)=64),
      PRIMARY KEY(item_id,revision),
      FOREIGN KEY(display_text_id,display_text_revision)
        REFERENCES texts(text_id,revision))

    furniture_definitions(
      furniture_id TEXT NOT NULL CHECK(length(furniture_id)<=128),
      revision TEXT NOT NULL CHECK(length(revision)=64),
      semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64),
      display_text_id TEXT NOT NULL, display_text_revision TEXT NOT NULL,
      definition_json TEXT NOT NULL, row_hash TEXT NOT NULL CHECK(length(row_hash)=64),
      PRIMARY KEY(furniture_id,revision),
      FOREIGN KEY(display_text_id,display_text_revision)
        REFERENCES texts(text_id,revision))

    furniture_function_bindings(
      furniture_id TEXT NOT NULL, furniture_revision TEXT NOT NULL,
      function_id TEXT NOT NULL CHECK(length(function_id)<=128),
      function_contract_version INTEGER NOT NULL CHECK(function_contract_version>0),
      contract_json TEXT NOT NULL CHECK(length(contract_json)<=16384),
      row_hash TEXT NOT NULL CHECK(length(row_hash)=64),
      PRIMARY KEY(furniture_id,furniture_revision,function_id),
      FOREIGN KEY(furniture_id,furniture_revision)
        REFERENCES furniture_definitions(furniture_id,revision))

    ALTER TABLE actions ADD COLUMN implementation_kind TEXT NOT NULL
      DEFAULT 'legacy_activity'
      CHECK(implementation_kind IN
        ('legacy_activity','activity_sequence','host_effect'))
    ALTER TABLE actions ADD COLUMN composer_id TEXT NULL
    ALTER TABLE actions ADD COLUMN function_id TEXT NULL
    ALTER TABLE actions ADD COLUMN arguments_json TEXT NULL

    action_sequence_steps(action_id TEXT NOT NULL, action_revision TEXT NOT NULL,
      ordinal INTEGER NOT NULL CHECK(ordinal>=0),
      step_id TEXT NOT NULL CHECK(length(step_id)<=128),
      primitive_id TEXT NOT NULL CHECK(length(primitive_id)<=128),
      arguments_json TEXT NOT NULL CHECK(length(arguments_json)<=16384),
      outputs_json TEXT NOT NULL CHECK(length(outputs_json)<=16384),
      success_edge_json TEXT NOT NULL CHECK(length(success_edge_json)<=4096),
      failure_edge_json TEXT NOT NULL CHECK(length(failure_edge_json)<=4096),
      cancel_edge_json TEXT NOT NULL CHECK(length(cancel_edge_json)<=4096),
      checkpoint_policy TEXT NOT NULL CHECK(length(checkpoint_policy)<=64),
      row_hash TEXT NOT NULL CHECK(length(row_hash)=64),
      PRIMARY KEY(action_id,action_revision,step_id),
      UNIQUE(action_id,action_revision,ordinal),
      FOREIGN KEY(action_id,action_revision) REFERENCES actions(action_id,revision))

Implementation-specific constraints are checked by the package validator as well
as bounded SQL fields: legacy_activity preserves existing capabilityId/contractJson
rows; activity_sequence has exactly one source—steps or a trusted registered
composerId—and the compiled Action stores the same validated normalized sequence
either way; host_effect has a registered functionId and typed arguments, with no
sequence or composer. The v5 data model and source adapter must not introduce
data_recipe as a second executor or scriptId as another discriminator.

For each owned localized Text object, generate the stable child Text ID and compile
translations to the existing TextRecord/TranslationRecord tables. For each pinned
Text ContentRef, preserve its exact store/revision/hash and add the dependency
closure. Ensure source validation enforces exactly one option before compilation.
Furniture-owned field needs must remain in the existing validated typed definition;
do not add an unvalidated JSON escape hatch.

An Action with implementationKind=host_effect may bind the registered function
player.item.grant to the pinned Item ref and authored fixed quantity. The Scenario
host—not a model output—resolves that Action and invokes the host-effect port. The quantity is validated as a
positive integer and checked against the Item's stack limit. LLM response text,
tool arguments, arbitrary stat deltas, and untrusted JSON never provide authority
to mint an item.

## Per-player quantity and generic Scenario grant contract

Keep quantities in a normalized table in the existing per-save game SQLite:

    player_item_quantities(player_id TEXT, item_id TEXT, quantity INTEGER,
      PRIMARY KEY(player_id,item_id), CHECK(length(player_id) BETWEEN 1 AND 128),
      CHECK(length(item_id) BETWEEN 1 AND 128), CHECK(quantity > 0))

A missing row means quantity zero. Persist only positive rows; a later consume
feature may delete a row at zero. Furniture inventory remains in home_layout JSON
with definitionId/storedCount and is not read, converted, or overwritten by this
table.

Propose these typed storage contracts in GamePersistenceContracts.cs:

- GameItemQuantityChangeRecord: changeId, eventId, ownerId, itemId,
  itemRevision, expectedQuantity, delta, maximumQuantity,
  runId, invocationId (the Flow invocation), stepExecutionId (the grant
  Flow step execution), requiresCompletedStepId (the authored Dialogue step ID),
  and completedDeliveryStepExecutionId (the exact completed execution).
- Add List<GameItemQuantityChangeRecord> itemQuantityChanges to
  GameCommitRequest. It is a typed host request, not a new progression JSON field.
- GameItemQuantityView: itemId and quantity.
- GameItemInventoryResult: state, playerId, gameRevision, and bounded item views.
- Add ReadPlayerItemQuantities() to IGameRepository, returning the inventory
  result for the current identity's established player owner.

DBMS owns validation and persistence of these records. The Scenario host effect
step uses the existing typed StepRecord.actionRef to the immutable host-effect
Action. Its strict version-1 dataJson names requiresCompletedStepId; it does not
add a redundant mutable StepRecord.itemRef. The Action pins itemRef and positive
authored quantity. Behavior exposes only the public pure IScenarioHostEffectPort
for this execution path; the Item-specific persistence adapter sits behind that
port. It resolves the exact completed Dialogue execution, not only the definition
ID, after the shared presenter reports successful delivery. No persistence adapter
accepts a model-originated free-form grant.

For the Fetch proof, the completed delivery step must be durably completed before
the count changes. Validate that completedDeliveryStepExecutionId resolves to an
outcome=completed step whose step_id equals requiresCompletedStepId and whose
run_id and Flow invocation match the grant. It may be staged as completed in the
same GameCommitRequest if Behavior commits delivery and grant together. The host
effect is called only on the presenter's successful terminal result, not when
dialogue is queued or opened. Collection failure, return failure, cancel before
the grant, or dialogue delivery failure produces no quantity change. After the
grant commits, cancellation does not reverse it.

### One transaction and retry identity

For one grant, the trusted host constructs a GameCommitRequest with current
progressionJson unchanged, expectedGameRevision from Load(), one typed item change,
one game event (kind item.quantity_changed), its GameEventHistoryContext, and the
host-authored operationPayload. SqliteGameRepository.Commit must do all of the
following inside its existing SQLite transaction:

1. Check operation_receipts first. Same operation ID and request hash returns
   duplicate and the prior receipt without incrementing quantity. Reuse with a
   different request returns conflicting_operation.
2. Check expectedGameRevision and item expectedQuantity. Check ownerId equals
   legacyPlayerId ?? accountId ?? saveId, item ID/revision is syntactically valid,
   delta is positive, and checked(expectedQuantity + delta) is no greater than
   both the pinned Item stackLimit passed by the trusted host and SQLite's signed
   64-bit integer maximum. Reject stale count, stack limit, or overflow before
   mutation.
3. Require the typed change's eventId to refer to exactly one request event, and
   require its history context to match runId, Flow invocationId, and grant
   stepExecutionId. Store before/delta/after/item revision,
   requiresCompletedStepId, and completedDeliveryStepExecutionId in the event
   payload; event_context correlates the event to the grant Flow step. Use
   cause_kind=source_operation and the
   stable operation ID as cause_reference_id unless a shared workflow change
   approves a new cause kind.
4. Insert/update player_item_quantities, insert the event/context and other typed
   histories, increment game revision, and insert operation_receipts in that same
   transaction. Any validation/SQL failure rolls everything back.

No separate item-change ledger is required for this first slice: game_events is
the durable before/delta/after audit row, event_context correlates it to the
Scenario, and operation_receipts make the operation replay-safe. If a later query
needs a typed history table, propose it separately rather than maintaining two
independent ledgers now.

Build operation IDs deterministically from immutable save ID plus the stable
grant tuple: grant kind, run ID, Flow invocation ID, grant Flow step execution ID,
host-effect Action store/ID/revision, selected release ID, and Item store/ID/revision.
Use
GameOperationId.FromStableKey, which already derives a stable UUID from SHA-256.
Do not substitute physical Action invocation or sequence-step IDs for Flow history
identity. Retries of that same Scenario step reuse that ID and immutable grant
payload. A new legitimate Scenario occurrence has a new run/step identity and may
grant another glass; do not add a global once-only entitlement or dedupe by
Scenario ID alone.

Operation request hashing must include every item change field and a canonical
host operation payload containing the complete Action and Item ContentRefs, selected
release ID/manifest hash, authored quantity, and Flow correlation IDs. Keep the event
ID stable for retries or ensure the existing
request hash continues to intentionally ignore only audit event ID/time, not item
identity, amount, owner, expected quantity, requiresCompletedStepId, or Flow
correlation. Receipt lookup precedes stale-revision rejection. A retry keeps the
original immutable grant payload; an uncommitted stale attempt can rebase its
expected revision/count only after FindReceipt confirms there is no receipt for
that operation ID. Document the choice in a regression test.

## Existing Furniture power-state persistence

Do not add a second TV state database. FurniturePersistenceAdapter.Commit already
submits a candidate HomeFurnitureLayoutDocument, operation ID, expected game
revision, and expected layout revision through the same game repository transaction
that records its event. Use that path for the registered TV power function.

Use the existing numeric furniture resource state with resourceId=device.power:
known value 0 is Off, known value 1 is On, and an absent/unknown resource remains
unknown. Do not add a new HomeFurnitureLayout field or layout schema version for
this registered resource. The TV On event and durable layout resource update must
commit together through FurniturePersistenceAdapter.Commit; the registered
power.set function uses IFurnitureFunctionPort and reports
applied/no_change/replayed/unavailable/conflict/failed with committed evidence;
it rereads the committed layout on success. This does not change the unrelated
conditionState or finite furniture stock.

## Schema versions and loss-safe migration

| Database/document | Current | Proposed | Migration and compatibility |
| --- | ---: | ---: | --- |
| Authoring SQLite | 1 | 2 | Add dialogue_definition, furniture_definition, item_definition, and action_definition to the documents.kind CHECK by a transactional table rebuild; copy document heads, revision hashes/bytes, receipts, imports, build sets, and all join rows byte-for-byte; require foreign_key_check to return no violations before commit. Preserve the eight-table model. |
| Compiled content SQLite | 4 | 5 | Additive DDL for items, furniture_definitions, furniture_function_bindings, Action implementation metadata, and lossless action_sequence_steps; preserve all v4 rows, release IDs, row hashes, heads, and old references. Old published releases remain unchanged and readable. |
| Game save SQLite | 5 | 5 | Schema v4→v5 adds flow_store_id, flow_revision, and flow_hash to flow_invocations. Existing invocations keep blank pins and cannot authorize new grants; preserve history and never synthesize a Flow reference. |
| Progression JSON | 4 | 4 | No change. Never put item quantities into progressionJson; typed progression writers must not erase them. |
| Home furniture layout JSON | Current supported versions 1/2 | 1/2 | device.power is stored through the existing typed resourceState; no layout schema migration is required. |
| Content build manifest | 2 | 2 | Existing generic source and coverage lists already record source kinds. No field changes are required for these records; bump only if implementation actually changes the manifest contract. |

Authoring v1→v2 must account for SQLite foreign keys from document_revisions,
import_sources, and build_set_documents. Execute the rebuild under the repository's
single-writer lock and a transaction: disable FK enforcement before BEGIN, copy the
documents rows to a temporary table, drop/recreate documents with the expanded
CHECK and index, restore every row unchanged, require foreign_key_check to return no
violations, then commit and re-enable FK enforcement. If the engine cannot guarantee
this sequence, fail closed and preserve the original DB for an explicit export and
reopen; never create a partially migrated authoring store. Update
AuthoringSchema.HasRequiredTables/version checks to accept only complete v2 after
migration.

Content v4→v5 must add rather than rewrite normalized rows. Keep actions rows as
legacy_activity by adding implementation_kind with that compatibility default;
add action_sequence_steps keyed to existing (action_id,revision), plus Item and
Furniture typed tables. Update ContentSchema.RequiredTables/HasRequiredTables,
ContentRepository staging/read/hash code, ContentPackageValidator, and release
dependency validation in one writer window. Existing v4 release heads must not be
silently advanced. Runtime with a v4 release sees no Item row and returns a typed
missing result; it does not claim the Item grant is available.

Game v3→v4 adds only player_item_quantities and updates CurrentVersion,
HasRequiredTables, and SqliteGameRepository.EnsureReady. Older v1/v2/v3 saves
continue through the existing migration chain and then v3→v4. Keep the migration
transactional and idempotent; do not rebuild or replace progression/layout rows.
Update TopicHistoryImporter’s current-schema preservation list so Topic-history
reconciliation cannot drop player_item_quantities. Keep its expected legacy-v3
source table set unchanged: that set validates old external snapshots, not the new
current schema. Add a migration test proving an existing item row survives any
non-Item history import/reconcile path.

Game v4→v5 adds the exact invocation Flow store ID, immutable revision, and content
hash. The migration is additive and transactional. Old rows receive empty strings;
these cannot authorize new host effects. New invocations may be unpinned only for
legacy in-memory Scenario execution; that evidence also fails closed for grants.
Persisted `furniture_power_changed` writes and replay reads require the durable
run→invocation→step relationship, not merely independent row existence.

## Proposed file map and signatures

### Authoring and compiled content — DBMS writer

- Assets/AlterOS/Authoring/AuthoringContracts.cs: add canonical source kinds
  dialogue_definition, furniture_definition, item_definition, and
  action_definition; expose them through SourceKinds/AllKinds and supported-kind
  validation. Add typed validation result DTO only if the current Workbench
  contract cannot carry ContentDiagnostic data.
- Assets/AlterOS/Authoring/AuthoringSchema.cs: CurrentVersion=2,
  MigrateVersion1To2, expanded kind CHECK for all four new kinds, and migration
  verification.
- Assets/AlterOS/Authoring/AuthoringRepository.cs: keep existing List/Read/Save,
  ReadSnapshot, SaveBuildSet and operation/CAS behavior; make schema opening run
  the v1→v2 migration under the existing writer lock.
- Assets/AlterOS/Authoring/AuthoringContentSourceProjection.cs: extend the existing
  kind projection for independent Dialogue, Furniture, Item, and reusable Action
  documents while keeping exact source bytes/revision provenance.
- Assets/AlterOS/Authoring/ContentAuthoringValidator.cs: add the deterministic
  ContentAuthoringValidator.Validate(kind,documentBytes) entry point and stable
  JSON-path diagnostics; no Unity or provider dependency.
- Assets/AlterOS/Content/ContentContracts.cs: add ItemRecord and
  FurnitureDefinitionRecord/FurnitureFunctionBindingRecord. Extend ActionRecord
  with implementationKind, composerId, functionId, argumentsJson, and lossless
  ActionSequenceStepRecord collection while preserving the old capabilityId/
  contractJson fields for v4/legacy rows.
- Extend ContentPackage with items and furnitureDefinitions; DialogueRecord remains
  in its existing dialogues collection and ActionRecord remains in actions.
  ItemRecord fields are id, revision, semanticHash, compiled displayTextRef, and
  stackLimit. FurnitureDefinitionRecord fields are id, revision, semanticHash,
  compiled displayTextRef, and a canonical validated payload using existing
  FurnitureDefinition fields; function bindings hold functionId, contract version,
  and bounded contract JSON. ActionRecord adds implementationKind, composerId,
  functionId, argumentsJson, and sequenceSteps. ActionSequenceStepRecord fields
  retain ordinal, stepId, primitiveId, argumentsJson, outputsJson (typed result
  bindings), the
  success/failure/cancel edge objects, and checkpointPolicy. Function/composer
  implementations remain registered host code, never method names from content.
- Assets/AlterOS/Content/ContentSchema.cs: CurrentVersion=5; add items,
  furniture_definitions, furniture_function_bindings, and action_sequence_steps;
  add implementation_kind (default legacy_activity), composer_id, function_id, and
  arguments_json to actions; migrate v4→v5.
- Assets/AlterOS/Content/ContentPackageValidator.cs: validate stable IDs, revisions,
  owned-Text-vs-pinned-Text XOR, bounded arguments/results/edges, known primitive /
  composer/function IDs, Item refs, and transitive dependencies. Enforce the
  activity_sequence steps-vs-composer XOR and host_effect-only function arguments.
  For a host_effect Flow step, validate StepRecord.actionRef, strict
  requiresCompletedStepId to a Dialogue step in that Flow, and the Action's pinned
  Item/Text dependency closure. Reject legacy_activity in new sources, arbitrary
  reflection, unknown functions, cycles, duplicate IDs, dangling refs, and
  cross-release mutable heads.
- Assets/AlterOS/Content/LegacyContentAdapter.cs: preserve existing projections;
  add explicit projections for the four new source kinds. New Action documents
  compile to reusable IDs; legacy generic_dialogue documents and per-step
  ActionRecord projections keep their existing shape and deterministic identity.
- Assets/AlterOS/Content/ContentBakeService.cs and
  ContentReleaseCompiler.cs: compile only the explicit snapshot's typed rows and
  dependency closure; hash the new rows in deterministic order.
- Assets/AlterOS/Content/ContentRepository.cs: stage/write/read typed rows, enforce
  exact ContentRef revisions, and include new row hashes in release integrity.
  Add ReadDialogue(ContentRef), ReadAction(ContentRef), ReadItem(ContentRef), and
  ReadFurnitureDefinition(ContentRef) methods following existing typed reads.
- Assets/AlterOS/Content/ContentFlowAdapter.cs: include Action sequence/host-effect,
  Dialogue, Item, and Furniture function refs in the release dependency closure;
  keep unknown refs blocked.
- Assets/AlterOS/Content/RuntimeContentStore.cs: expose selected-release typed
  reads without changing trusted release selection semantics.
- Tools/ContentData/Check-ContentData.ps1 harness: add migration/validation cases
  under existing Authoring, Repository, Bake, and Publish groups; do not update
  authored production content in this planning slice.

Proposed authoring validator signature:

    AuthoringValidationResult ContentAuthoringValidator.Validate(
      string kind, byte[] documentBytes)

It returns diagnostics with JSON paths and stable error codes. Source validation
must be deterministic and provider-free. ContentPackageValidator remains the
compiled closure validator; do not fork it in the Workbench.

### Per-save game quantity — DBMS writer

- Assets/AlterOS/Persistence/GamePersistenceContracts.cs: add
  GameItemQuantityChangeRecord, GameItemQuantityView, GameItemInventoryResult;
  add itemQuantityChanges to GameCommitRequest; add
  ReadPlayerItemQuantities() to IGameRepository.
- Assets/AlterOS/Persistence/GameSchemaMigrations.cs: CurrentVersion=4,
  MigrateVersion3To4 and player_item_quantities table checks.
- Assets/AlterOS/Persistence/SqliteGameRepository.cs: include typed changes in
  request validation/hash, identity ownership checks, step/event cross-reference
  validation, item reads, checked count writes, stale baseline and stack-limit
  rejection, and event/context persistence in the existing transaction.
- Assets/AlterOS/Persistence/TopicHistoryImporter.cs: preserve the new current
  table during unrelated Topic-history reconciliation; retain legacy-v3 fixture
  validation as-is.
- Assets/AlterOS/Persistence/ScenarioItemGrantPersistenceAdapter.cs: add the
  internal Item-specific host-effect writer behind IScenarioHostEffectPort; accept
  only a host-resolved pinned Item/action binding and typed execution identity.
  It builds the existing GameCommitRequest; it is not another public runner.
- Assets/Scripts/Progression/AlteraProgressionStore.cs: if a new inventory reader
  facade is needed, expose it from the existing SqliteGameRepository instance and
  same persistentDataPath. Do not create another DB connection owner or save file.
- Assets/AlterOS/Scenarios/ScenarioHostEffectContracts.cs: Behavior owns the pure
  public IScenarioHostEffectPort.Apply contract and host-effect Scenario step. Its
  StepRecord uses actionRef and strict version-1 data requiresCompletedStepId; it
  resolves the exact completed Dialogue execution before dispatching the DBMS
  adapter. No UI, Dialogue callback, Furniture function, or LLM writes quantities.
- Tools/Persistence/Check-GamePersistence.ps1 and its headless harness: add the
  v3→v4 migration, item read/write, stale/duplicate/rollback, and importer-preserve
  cases. Existing synthetic-data isolation remains mandatory.

Proposed typed change in GameCommitRequest is a host request; after validation it
updates player_item_quantities and its matching game_event inside the existing
Commit call. The query returns zero for an absent row but lists only stored
positive quantities. No direct SQL access is exposed to Scenario or Workbench.

The generic public Scenario port is proposed for Behavior to own in
Assets/AlterOS/Scenarios/ScenarioHostEffectContracts.cs:

    public interface IScenarioHostEffectPort
    {
      ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request);
    }

ScenarioHostEffectRequest carries actionRef, scenarioRunId, invocationId (Flow
invocation), stepExecutionId (effect Flow step), requiresCompletedStepId, and
completedStepExecutionId for the exact completed Dialogue execution. The DBMS
adapter maps scenarioRunId to storage runId and completedStepExecutionId to
completedDeliveryStepExecutionId; these are naming adapters, not new execution
identities. The request does not carry a mutable itemRef or amount: the pinned
host-effect Action owns those. For player.item.grant, the DBMS Item-specific adapter
behind this port validates the selected-release Action/Item refs, obtains the
current game snapshot/revision, derives the stable operation ID, and calls
IGameRepository.Commit with the typed change. The result reports the stable host
effect outcome plus the persistence receipt/failure code. Behavior owns the pure
port and Scenario step; DBMS owns GameCommitRequest validation, SQL mutation,
receipt, and read query. This split prevents a second public Scenario runner or
inventory writer.

### Owner-specific non-DBMS work

- Alterra Behavior defines exact Scenario grant step and presenter completion
  signal, then maps the pinned Action/Item refs and run/invocation/step IDs to the
  storage record. It owns tests proving no call before completed delivery.
- Furniture and Environment owns the registered TV/dispenser function and
  presentation adapters. TV uses device.power through FurniturePersistenceAdapter.
  dispenser.collect_mock uses Furniture's IFurniturePresentationPort Start/Poll/
  Cancel lifecycle, changes no supply/hydration, and grants no Item.
- Topic Playground owns Tools/ContentWorkbench/**. Its editing forms and agent
  commands call the shared AuthoringRepository and validator; no DBMS changes that
  directory while it has the acknowledged writer window.
- Main owns shared contract reconciliation, sequencing, and the eventual explicit
  START. This plan does not send or imply START.

## Implementation slices with red/green acceptance

These are future implementation tasks, not tests run in this planning session.

1. **Lock the cross-owner DTOs.** Compare Behavior's grant port and Furniture's
   function/state contracts to GameItemQuantityChangeRecord and ContentRef. Red:
   compile/design fixture cannot bind item ID, pinned revision, or execution IDs.
   Green: one command has a host-only path to storage and the function cannot grant.
2. **Authoring v2 migration and kind round-trip.** Red: creating a Dialogue,
   Furniture, Item, or Action doc is rejected by the v1 CHECK; forced/failed
   migration must leave v1 data readable. Green: open/migrate v1, list/read/save
   each new kind, restart, compare every original document/revision/receipt/build
   set byte-for-byte, and pass foreign_key_check. Run
   .\Tools\ContentData\Check-ContentData.ps1 -Group Authoring.
3. **Typed six-kind projection and v5 compiled schema.** Red: an independent
   dialogue_definition or item.water_glass with owned Text cannot compile before a
   prior release; a pinned Text ref, host_effect Action, or activity_sequence loses
   a dependency, edge, checkpoint, or hash; a v4 release changes on upgrade. Green:
   bake twice from the same explicit snapshot to identical hashes; prove new
   Dialogue has no Topic/anchor requirement; prove every sequence field round-trips;
   keep existing generic_dialogue shape/IDs; resolve each kind; preserve and read an
   old v4 release without claiming new rows. Run
   .\Tools\ContentData\Check-ContentData.ps1 -Group Repository,
   .\Tools\ContentData\Check-ContentData.ps1 -Group Bake, and
   .\Tools\ContentData\Check-ContentData.ps1 -Group Publish.
4. **v3→v4 save migration.** Red: upgrade fixture lacks item table, changes an
   existing game/layout revision, or alters progression bytes. Green: old v3
   synthetic save opens with empty item inventory while progression, layout,
   events, typed history, receipts, identity, and revisions remain exact. Run
   .\Tools\Persistence\Check-GamePersistence.ps1 -Group ItemMigration (new harness
   group).
5. **Atomic quantity write and read.** Red: grant updates count without matching
   event/context/receipt, or writes any component when event validation fails.
   Green: one committed item_quantity_changed event, matching Scenario context,
   receipt, and count are visible together; ReadPlayerItemQuantities returns the
   new positive count. Run
   .\Tools\Persistence\Check-GamePersistence.ps1 -Group ItemQuantities (new group).
6. **Authorization, limits, retries, and stale state.** Red cases: unknown Item or
   release revision, owner mismatch, zero/negative delta, maxStack exceed, Int64
   overflow, unknown action function, incomplete/mismatched
   requiresCompletedStepId, reused operation ID with changed payload, stale game
   revision, stale expected quantity, injected SQL/event failure. Green: each fails
   closed with no partial count/event/receipt; same committed step returns duplicate
   and adds nothing; a new run/Flow-invocation/step grants once.
   Run the same ItemQuantities group, including repository-level rollback checks.
7. **Topic-history and furniture integration.** Red: Topic history reconcile drops
   item rows, or TV state updates bypass layout/event CAS. Green: unrelated Topic
   import preserves item counts; a TV On operation writes device.power known=1 and
   its event through FurniturePersistenceAdapter in one commit; absent resource is
   still unknown and no layout schema bump occurs. Run
   .\Tools\Persistence\Check-GamePersistence.ps1 -Group All and
   .\Tools\FurnitureRuntime\Check-FurnitureRuntime.ps1 only after that owner adds
   the typed state fixture.
8. **Scenario acceptance after all owner slices.** Prove Fetch begins at zero,
   executes the mock collection without Alterra hydration, returns, completes the
   delivered “Here, cheer!” dialogue, then commits exactly +1 Water glass. Test
   cancellation/failure before grant, same-step replay, and a second legitimate
   run. Also verify the independent TV power and locate flows do not mutate Item
   counts. Run
   .\Tools\ScenarioRuntime\Check-ScenarioRuntime.ps1 and the approved Unity
   acceptance separately; neither is covered by this DBMS plan.

The PowerShell checks above are proposed future commands. No test, compile, build,
Unity, database, profile, or provider command was run for this note.

## Consolidated producer contracts and writer windows

The following are coordinator defaults from the master workflow, not claims that
the contracts or runtime are implemented. They resolve the earlier design questions
in this note:

- New Dialogue uses dialogue_definition and has no implicit Topic/route/anchor;
  existing generic_dialogue remains unchanged. Beat text and definition labels
  support exactly one owned localized Text object or one pinned Text ContentRef.
- New Actions use schemaVersion 1 and implementationKind activity_sequence or
  host_effect. Old compiled rows migrate as legacy_activity. An activity_sequence
  has exactly one source—steps or a trusted composerId—and both compile to one
  lossless normalized sequence. A host_effect is only a registered function plus
  typed arguments. No data_recipe executor or scriptId discriminator is added.
- Sequence storage retains stepId, primitiveId, typed arguments, outputs/result
  bindings, success/failure/cancel edges, and checkpoint policy. Step.actionRef pins the
  Action; strict version-1 dataJson requires requiresCompletedStepId. The pinned
  host_effect Action owns itemRef and quantity.
- invocationId in events/effect requests is the Flow invocation. stepExecutionId
  is the Scenario Flow step execution. Physical Action invocation and sequence-step
  IDs remain separately typed and never replace those IDs for Item idempotency.
- Furniture power uses resourceState resourceId=device.power: known 0 is Off,
  known 1 is On, and absence is unknown. This needs no new HomeFurnitureLayout
  field/version. The registered power function persists through the existing
  FurniturePersistenceAdapter.Commit CAS/event path.
- stackLimit 99 is the minimal Item fixture default, not a broad economy rule.

Follow the master writer windows after an explicit implementation START:

| Window | Owner and storage-plan work | Release gate and DBMS boundary |
| --- | --- | --- |
| W0 | Behavior publishes portable Activities/Scenario contracts, sequence DTOs/descriptors, IScenarioHostEffectPort.Apply and pure tests | Contracts build without Unity; Furniture and DBMS acknowledge signatures. DBMS does not edit Behavior's shared core files. |
| W1 | DBMS alone writes Authoring/Content/Persistence contracts, validators, migrations, typed readers/writers, and ScenarioItemGrantPersistenceAdapter. Furniture separately writes only its function/presentation/state adapters. | Lossless source/sequence/release round-trips, owned/pinned Text XOR, all four new authoring kinds, synthetic migrations and atomic transaction checks. Behavior supplies requirements/tests and consumes the released interfaces; no shared-core edits during this window. |
| W2 | Behavior integrates ActivityHost/ScenarioSession, launch resolver and existing dialogue/menu bridges | Three traces, C#/data parity, cancellation/replay/late callback and completed-VN effect checks against released DBMS/Furniture APIs. |
| W3 | Topic Playground owns every Tools/ContentWorkbench file, adapter, service, UI and test | Six real create/read/edit/validate/preview paths and UI/CLI parity; synthetic preview is read-only. |
| W4 | Furniture owns the acknowledged exclusive Unity Editor/scene/prefab presentation window; other owners consult read-only | Import/compile plus focused Editor evidence; no concurrent Editor owner. |
| W5 | Main coordinates selected synthetic fixture bake/publication and startup/gameplay acceptance | Explicit reviewed release selection and requirement ledger; no inferred private-profile/provider access. |

Before W0, owners acknowledge the exact file paths in their plans; this table is
not a live writer lock. DBMS owns shared Authoring/Content schema/compiler/validator
files even where Behavior supplies the requirements. Each window needs an explicit
handoff/release. No implementation starts from this planning note alone.

No external dependency is needed: use the existing SQLite-net, content hashing,
CAS, event, and receipt infrastructure. Do not add a database package or embed a
second editor/runtime.

## Rollback and stop conditions

Before any later migration, back up the authoring/content/game SQLite database
through its existing safe backup path and verify the backup. Apply each schema
migration transactionally; do not delete or overwrite source JSON, published
releases, home furniture stock, or player save data. If the migration cannot prove
revision/byte/foreign-key preservation, stop before schema-version change and retain
the original database for recovery. A failed Item grant must leave count, event,
history context, game revision, and operation receipt unchanged.

The current work is this documentation plan only. It does not make dialogues
editable by itself and it does not establish that SQLite authoring or Item grants
are implemented. The next gate is main's consolidation and explicit source START,
with the Workbench owner implementing the human editing path against these shared
contracts.
