# Topic Scenario Reference Producer

Date: 2026-10-23

## Implemented

- Topic source entries may point to one exact local `scenario_definition` Flow with `scenarioRef` (`store`, `id`, `revision`, and `expectedHash`). The entry's `scenarioId` remains its independent, unique Topic-entry identity.
- The importer resolves references only after the referenced Scenario has been projected into the same package. Missing, foreign-store, wrong-revision, and mismatched-hash references fail closed. Inline `nodes` entries retain the existing projection path; an entry cannot combine inline nodes and `scenarioRef`.
- `RuntimeContentStore.ReadTopic(ContentRef)` reads only a Topic pinned in the selected SQL release. `PrepareTopicEntry(...)` resolves the SQL entry's exact Flow reference and checkpoint, hydrates the selected compiled SQL dependency closure through `ContentRepository`'s typed readers, validates that package and calls `ContentFlowAdapter.Resolve` and `Prepare`. Runtime preparation never imports retained JSON sources or recompiles an authored package.
- `ContentRepository.ReadCompiledPackageForRelease(...)` verifies that its release handle matches the stored immutable release, reads every exact Topic/Flow/Dialogue/Text/Action/Item/Furniture/Rule dependency by store, ID, revision and expected hash, loads the release's semantic rows, and rejects unsupported dependency kinds. Each typed parent and child row read into the closure is checked against the existing write-side `HashFields`/`HashObjectFields` row-hash formula before its detached DTO is returned. `ContentFlowAdapter` keeps detached Topic, Flow, Action, Item and Furniture records in the execution snapshot, including normalized Action sequence fields and host-effect metadata.
- `AlteraProgressionStore` exposes typed factories for the Item host-effect port and Furniture power store. They return null until the progression store is initialized, mutable, and has its repository; the host-effect factory also requires an explicit content-release reader. Both use the existing private `IGameRepository`; the power adapter reuses the established `FurniturePersistenceAdapter` pattern.

## Evidence

- Test-first regressions first reproduced `unsupported_legacy_field`, successful preparation despite a corrupted compiled SQL Flow row, and loss of `activity_sequence` metadata, Action rows, Item, and Furniture from the execution snapshot.
- The focused Bake group passed 13/13. It covers shared Topic entry IDs, inline compatibility, missing/foreign/stale/hash/both-source rejection, malformed SQL structure, valid-looking retained-label SQL mutations to Translation text, Action arguments, sequence rows and a Step/Transition edge, and referenced Scenario preparation with real compiled `activity_sequence` and `host_effect` Actions plus Item/Furniture closure.
- `ReusableDefinitions` passes 7/7, including the detached snapshot regression. `Check-ProgressionSourceCompile` is still blocked before its synthetic factory probe by source-versus-stale-`Library/ScriptAssemblies` type-identity errors and the in-progress `DialogueManager` reference to `TopicMenuSelection`. The factory probe remains pending a normal source import and focused EditMode run; no cached assemblies or Behavior-owned files were changed.
- `ContentSchemaMigration` passed 1/1; read-only schema-5 Furniture rows and migrated legacy bindings remain compatible with their original write-side hash formulas.
- 2026-10-24 row-integrity fix: before row verification, the valid-looking Translation SQL mutation was accepted (Bake 12/13). After verification, Bake passes 13/13 with unchanged row/release/source labels rejecting Translation, parent Action arguments, normalized sequence-row arguments, and a consistent Step/Transition redirection. The original unmodified compiled release still prepares successfully.
- The content-data compile harness includes `ActivitySequenceRunner.cs` and `ActivitySequenceReleaseAdapter.cs`, which the current `ScenarioSession` source references.

## Limits

This is source/compiler and synthetic SQL evidence. Unity Editor import, Play Mode, and player-visible dialogue were not run. The passing Bake run emitted existing `CS0649` warnings for reflection-deserialized DTO fields. The separate progression compile is pending the Behavior-owned source errors above; the new bridge probe uses only a temporary synthetic database path and does not invoke gameplay operations.

## Review

On 2026-10-24, the scoped TopicRefs-API-3 Content review accepted the combined reusable Scenario reference and compiled-row integrity changes. The progression-factory probe remains for the later Unity/W4 synthetic-root check; the stale source-versus-Library compile harness is not treated as that acceptance evidence.
