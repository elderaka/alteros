# Reusable Storage W1A Verification

**Date:** 2026-10-23  
**Status:** W1A source implementation complete, fix-round independently reviewed, and released. W1B implementation is authorized in the current main-session turn.

## Implemented

- Authoring SQLite schema v2 recognizes `dialogue_definition`, `furniture_definition`, `item_definition`, and `action_definition`. Opening a supported v1 database migrates its document table transactionally while preserving existing documents, revisions, receipts, source backups, and build-set links. The authoring repository opens the migrated store and fails closed when its version/table markers are inconsistent.
- Game-save SQLite schema v4 adds `player_item_quantities`, keyed by player and item. The v3-to-v4 migration is additive and initializes no fabricated inventory rows.
- `GameCommitRequest` and `IGameRepository` expose typed quantity changes and inventory reads. A grant validates owner/item/quantity bounds, expected quantity, request identity, and the matching item event. Scenario delivery must reference the completed Dialogue step execution in the same Flow invocation and run. The count, event, operation receipt, and revisions commit in one transaction; duplicate operations do not grant twice, and failed writes roll back together.
- Completion validation merges a staged completion-only Dialogue result with its already-persisted presentation, requiring the exact execution, Dialogue step, Flow invocation, and Scenario run. This supports completing the dialogue and granting the Item atomically without resending presentation data.
- Topic database relocation now treats the historical v3 root database as its exact source format while reading the canonical game store at current schema v4. Unexpected tables and Item-bearing v4 databases are rejected as Topic-only relocation sources. The v3 inventory allowlist remains unchanged.
- Synthetic migration/transaction fixtures now model actual v1/v2/v3 inputs, rather than demoting a current database while leaving its newer Item table behind.

## Files

- `Assets/AlterOS/Authoring/AuthoringContracts.cs`
- `Assets/AlterOS/Authoring/AuthoringSchema.cs`
- `Assets/AlterOS/Authoring/AuthoringRepository.cs`
- `Assets/AlterOS/Persistence/GamePersistenceContracts.cs`
- `Assets/AlterOS/Persistence/GameSchemaMigrations.cs`
- `Assets/AlterOS/Persistence/SqliteGameRepository.cs`
- `Assets/AlterOS/Persistence/TopicHistoryImporter.cs`
- `Tools/ContentData/HeadlessContentDataTests.cs`
- `Tools/Persistence/HeadlessPersistenceTests.cs`

## Verification

- Initial `Tools/Persistence/Check-GamePersistence.ps1 -Group All` — **27 passed**, using Unity 6000.4.4f1's bundled Mono compiler, the real SQLite-net binding, and the Windows x64 native plugin in an isolated PowerShell process.
- Fix-round regression: the new completion-only Dialogue plus Item grant case first failed with `invalid_history`, then passed after the staged/persisted execution check was corrected. Wrong-run, wrong-step, and transaction-rollback guards passed.
- Final `Tools/Persistence/Check-GamePersistence.ps1 -Group All` — **28 passed**, including the new completion-plus-grant regression.
- `Tools/ContentData/Check-ContentData.ps1 -Group Authoring` — **7 passed**, including v1 migration/preservation, four-kind support, exact source-byte projection, pinned snapshot compilation, and incomplete-draft blocking.
- Focused `History`, `TopicDatabaseRelocation`, and `RuntimeMigration` groups passed while resolving outdated synthetic fixtures and v3/v4 expectations; the final full persistence run passed after those fixes.
- The Authoring check emitted CS0649 warnings for legacy DTO fields populated by serialization; it completed successfully.

## Handoff boundary

W1A covers the database contracts, migrations, repository transaction, history relocation compatibility, and portable synthetic tests above. The independent fix-round review found no remaining Critical, Important, or Minor issues and released W1A. The concrete Scenario item-grant persistence adapter, Content sequence projection/validators, and Editor build integration are outside W1A and belong to W1B. The Workbench/Editor authoring workflow is coordinated by the Workbench owner and remains part of the overall SQLite editing deliverable.

No Unity Editor import/build or Play Mode acceptance was performed in this slice. After main reviews and releases W1B, the relevant next acceptance is the documented Unity editor/build workflow, followed by the user's live test.
