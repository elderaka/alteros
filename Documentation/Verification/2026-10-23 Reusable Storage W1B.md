# Reusable Storage W1B Verification

**Date:** 2026-10-23  
**Status:** W1B source slice accepted after Content round three and Persistence round five reviews. Unity Editor acceptance remains pending.

## Implemented

- Content schema v6 migrates v4 through an explicit v5 state and adds the Scenario
  `host_effect` Step kind plus Furniture function binding point/use keys. Writable
  stores migrate v4→v5→v6; read-only v4 and v5 stores remain readable. The installed
  original v4 artifact is unchanged, and v4 Actions read as `legacy_activity`.
- `ReusableContentDefinitionAdapter` validates/projects independent Dialogue,
  Item, Furniture, host-effect Action, and lossless activity-sequence Action
  documents. Owned or pinned Text remains exclusive; source bytes and immutable
  source revisions remain retained.
- Action sequence import, authoring validation and package revalidation use the
  trusted `ActivitySequenceCatalogFactory`; composer arguments are typed literals,
  composers are validated before use, and composer/data sources lower through the
  same normalized rows. The Editor vocabulary derives sequence capabilities from
  that catalog and includes `player.item.grant` from the host registration.
- Scenario sources retain reusable pinned Dialogue/Action references without
  copying content. A strict v1 `host_effect` Step must pin a host-effect Action and
  name a dominating completed Dialogue Step. Two Scenarios can share the same
  exact Dialogue and Action revisions.
- Furniture source validation uses `FurnitureCatalogueValidator.ValidateDefinition`.
  Full definition JSON preserves `functionBindings` and `presentationBindings`; SQL
  function contract rows preserve `(functionId,pointId,useId)` and registered v1
  contract metadata. TV and dispenser bindings are covered by synthetic fixtures.
- `ContentRepository` stages and reads typed Dialogue, Action, Item, and Furniture
  revisions. Action sequence rows retain ordinal, IDs, typed arguments, outputs,
  success/failure/cancel edges, and checkpoint policy. Release dependencies include
  exact Item/Furniture revisions and independent Dialogue revisions. Runtime typed
  reads enforce the pinned release closure.
- `ScenarioItemGrantPersistenceAdapter` implements `IScenarioHostEffectPort` for
  the registered `player.item.grant` function. It reads the actual run/invocation/
  effect-step/completed-step evidence, resolves exactly one Flow from the selected
  release, and verifies the compiled host_effect Step, exact Action ref and Dialogue
  prerequisite before applying. It enforces the Item stack limit, writes
  count/event/receipt through the shared game commit, and reconstructs exact durable
  operations after restart. Negative cases cover unrelated Flow/Action, non-Dialogue
  prerequisite and same execution identity.
- `FurniturePowerPersistenceAdapter` writes `device.power` through
  `FurniturePersistenceAdapter` layout CAS and the shared game receipt/event. It
  preserves absent state as unknown, stores known Off/On as 0/1, pins `useId` and
  the exact power binding in the event, and records the Flow `stepExecutionId`
  separately from physical `activityId` and inner `sequenceStepId`. A known-state
  `no_change` writes an operation receipt with no event or layout update; exact
  retries replay after restart, and changed payloads conflict.
- `ContentBuildProcessor` now derives Generic route anchors and exact Topic-owned
  anchors from the selected SQL snapshot, invokes the existing SemanticLab GTE
  baker with offline-only model access, and imports metadata schema v2. Topic links
  are reused; no Topic-to-Generic wrapper is created. Metadata schema v1 remains
  supported for the existing compiled release.

## Verification

- `Tools/ContentData/Check-ContentData.ps1 -Group Authoring` — **8 passed**.
- `-Group Bake` — **11 passed**; the current 439-source SQL corpus and baked GTE
  vectors opened as one pinned release.
- `-Group Publish` — **6 passed**.
- `-Group Flows` — **7 passed**.
- `-Group ReusableDefinitions` — **6 passed**, covering trusted composer normalization,
  unregistered composer/primitive rejection, reusable Scenario references and host
  effect gates, Furniture TV/dispenser mapping, and typed SQLite round-trips.
- `-Group ContentSchemaMigration` — **1 passed**, covering original v4 preservation,
  v4→v5→v6 migration, read-only v4/v5 compatibility, writable v5→v6 migration, and
  rollback of a deliberately invalid foreign-key migration fixture.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group FurniturePower` — **1 passed**
  with the bundled Unity Mono compiler, SQLite-net, and synthetic SQLite save. It
  covers typed event-context round-trip, missing/mismatched context rejection,
  CAS/replay, no-change receipt replay after restart, changed-payload conflict, and
  event-failure rollback.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group ItemMigration` — **1 passed**,
  including a real schema-v4 game fixture upgrading to schema v5 while preserving old
  invocations with blank/unpinned content references.
- `Tools/Persistence/Check-GamePersistence.ps1 -Group All` — **29 passed** using the
  bundled Unity Mono compiler, SQLite-net and Windows x64 native plugin.
- `Tools/ContentData/Check-ContentData.ps1` focused groups passed after round two:
  Authoring **8/8**, Bake **11/11**, Publish **6/6**, Flows **7/7**,
  ReusableDefinitions **6/6**, ContentSchemaMigration **1/1**. The bake reopened
  all 439 sources as one pinned release; content schema v4→v5→v6 compatibility and
  rollback checks remained green.
- Round-two regressions cover exact `bark(dialogueId:text)` Dialogue and Text closure,
  the registered function-to-capability binding, Ask option/skip/cancel rejection as
  a host-effect prerequisite, schema-v5 Furniture reads without v6-only binding
  columns, exact invocation Flow store/revision/hash pins, receipt-first replay after
  Flow ID replacement, and crossed run/invocation/step Furniture ancestry rejection
  on both write and replay paths.
- Round-three regressions cover exact Flow Step reference hydration, a real compiled
  SQL release opened through `RuntimeContentStore` and applied through the grant
  adapter, Action store/ID/revision/hash replay mismatches, exact replay after release
  replacement, versioned pin-aware history hashes, and bounded exact-pin compatibility
  for old typed receipts.
- `Tools/Persistence/Check-ProgressionSourceCompile.ps1` was attempted for the
  scene-wired history writer. After fixing its duplicate Newtonsoft reference, it
  still fails on eight source-versus-`Library/ScriptAssemblies` type collisions in
  existing Content/Furniture code. The changed `AlteraProgressionStore` lines emit
  no compile errors, but this harness does not establish a full progression compile.
- The test harnesses emit existing CS0649 DTO-field warnings; the focused groups
  above compiled and passed with exit code 0.
- The earlier W1B checkpoint recorded GTE anchor tests 2/2 and Semantics 4/4.
  They were not rerun in this producer-fix round; the ContentBuildProcessor still
  uses the same selected-snapshot GTE input path.
- An earlier broad Python unit invocation that imported the full SemanticLab ML
  stack produced no output for more than 90 seconds and was stopped. Lightweight
  input-validation tests passed then without loading the model stack.

## Remaining acceptance

- Main-session scoped re-review of the three round-three persistence findings. Source
  changes are not marked released until that review accepts them.
- Unity Editor compilation/import of the current project. No Unity Editor was opened
  for this W1B verification.
- The selected-snapshot GTE model bake was not run. Its build path forces Hugging
  Face/Transformers offline mode; actual model inference remains pending.
- The user’s Play Mode/live game test remains separate. No user profile, player
  save, Workbench file, provider, or external database was read or written here.

## Round-two producer fixes

- Activity-sequence bark validation requires one literal Dialogue ID that resolves
  uniquely in the selected package. Validated Dialogue Beat/Text closure is included
  in the compiled dependency set; missing and duplicate revisions fail closed.
- Host-effect function IDs require their matching installed capability registration.
  A Dialogue capability cannot authorize `player.item.grant`; the power function is
  included in the same registry.
- Scenario host effects require a dominating completed Dialogue Step. Ask Steps are
  rejected even when every option, skip, and cancel path reaches the effect.
- Read-only compiled-content schema-v5 Furniture reads project empty point/use values
  instead of selecting columns that exist only in schema v6; migration leaves those
  values unmapped.
- Game-history SQLite schema v5 stores exact Flow store ID, revision, and hash on new
  invocations. Existing schema-v4 invocation rows migrate with blank pins and cannot
  authorize new grants. Receipt replay validates the stored request/evidence before
  consulting the current release. Content and game-history schemas are distinct.
- Furniture power writes and replay reads now prove the durable run→invocation→step
  ancestry. A crossed tuple leaves layout, event, context, receipt, and game revision
  untouched.

## Round-three persistence fixes

- `RuntimeContentStore.ReadFlow(ContentRef)` now validates and hydrates each Step's
  pinned Action/Dialogue references using the selected release, including expected
  hash checks. The adapter's Action hash authorization remains in place.
- New Item grant event receipts include the effective Action ContentRef: store, ID,
  revision and semantic hash. Receipt-first retries compare those immutable values;
  an omitted request hash remains optional, while any supplied hash must match.
- New typed history hashes include Flow pin fields and Action reference provenance.
  Version-2 receipt compatibility is allowed only when durable Flow pins equal the
  retry request; changed supplied pins conflict.

## Round-four legacy hash guard

- Typed-history duplicate replay now requires persisted Flow pin equality whenever
  the retry supplies invocation records, including the `sameCurrentRequest` branch.
  This closes the case where clearing all three supplied pins reproduced an older
  v2 hash and bypassed the compatibility guard.
- The focused fixture converts a pinned receipt to its historical v2 hash, confirms
  exact pinned replay, rejects changes to each pin separately, rejects clearing all
  three pins, and confirms exact replay still succeeds after each rejection.
- A separate genuinely unpinned legacy invocation still replays its exact v2 typed
  request successfully.

## Round-four verification

- `Tools/Persistence/Check-GamePersistence.ps1 -Group FurniturePower` — **1 passed**;
  the group compiles persistence sources and runs the v2 compatibility, changed-pin,
  clear-all, unknown-pin replay and accepted power regressions against synthetic SQL.
- No full suite rerun was needed for this one-branch guard. No Unity Editor or live
  data was used.

## Final scoped review

- Content round three accepted the four scoped content findings; Persistence round
  five accepted the final legacy-hash guard with no remaining findings.
- The combined W1B DBMS source slice is released. No additional DBMS implementation
  is authorized by this handoff; Unity/player proof and the required W3 graphical
  editor remain separate work.

## Exact API handoff

- `ContentRepository.ReadDialogue(ContentRef)`, `ReadAction(ContentRef)`,
  `ReadItem(ContentRef)`, and `ReadFurnitureDefinition(ContentRef)`.
- `ContentAuthoringValidator.Validate(kind,bytes)` and default importer use the
  trusted ActivitySequence catalog. Explicit hosts can use
  `LegacyContentAdapter.ImportDocumentsWithSequenceBuilder`.
- `RuntimeContentStore` implements `IContentReleaseReader`; typed Action, Item and
  exact-unique Flow reads require references included in the selected release.
- `IGameRepository.ReadScenarioHostEffectEvidence` resolves the durable Flow pin,
  effect/completed Step IDs and execution IDs from the same run/invocation.
- `ScenarioItemGrantPersistenceAdapter(IContentReleaseReader, IGameRepository)`
  implements `IScenarioHostEffectPort.Apply`.
- `IGameRepository.FindItemQuantityOperation(operationId)` returns the exact
  persisted Item change, event context, and receipt or fails closed.
- `FurniturePersistenceAdapter.CommitPowerNoChange` uses the existing game
  operation receipt without writing a fake event or layout snapshot.
- `FurniturePowerPersistenceAdapter(IGameRepository, FurniturePersistenceAdapter)`
  implements `IFurniturePowerStore.SetPower`; `IGameRepository.FindFurniturePowerOperation`
  resolves immutable power replay evidence, including `useId`.
- `FurniturePersistenceAdapter.CommitPowerState(...)` commits the typed
  `furniture_power_changed` event with the candidate home layout in the same CAS
  transaction.

The six-kind graphical editor remains a required W3 task owned by the Workbench
owner; this DBMS handoff does not implement or mark that UI complete.

The SQLite editing workflow remains W3-owned by Topic Playground. This report does
not claim the Workbench task is complete or change its ownership.
