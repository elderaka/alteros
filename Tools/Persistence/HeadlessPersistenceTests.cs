using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlterOS.Furniture;
using AlterOS.Interaction;
using AlterOS.Conversation;
using AlterOS.Persistence;
using AlterOS.Recording;
using AlterOS.Memory;
using AlterOS.Topics;
using AlterOS.Content;
using SQLite;

public static class HeadlessPersistenceTests
{
    private static int passed;

    public static int Main(string[] args)
    {
        if (args == null || args.Length != 2)
        {
            Console.Error.WriteLine("Expected a synthetic test directory and a group name.");
            return 2;
        }

        string root = Path.GetFullPath(args[0]);
        string group = args[1] ?? "All";
        Directory.CreateDirectory(root);
        try
        {
            if (group == "All" || group == "Repository")
            {
                Run("identity, snapshots, idempotency, and stale revision", () => RepositoryBehavior(root));
                Run("transaction rollback for state, event, and receipt", () => AtomicRollback(root));
                Run("future schema and corrupt bytes fail closed", () => InvalidDatabasePreservation(root));
                Run("one-time Unicode legacy import and source backup", () => MigrationBehavior(root));
            }
            if (group == "All" || group == "ItemMigration")
                Run("schema 3 migration preserves save data and initializes empty Item quantities", () => ItemMigrationBehavior(root));
            if (group == "All" || group == "ItemQuantities")
                Run("Item grant atomically records count/event/receipt and deduplicates one Flow step", () => ItemQuantityGrantBehavior(root));
            if (group == "All" || group == "ItemCompletionAtomic")
                Run("completion-only Dialogue update and Item grant commit against earlier presentation", () => ItemCompletionAtomicBehavior(root));
            if (group == "All" || group == "ItemQuantitiesValidation")
                Run("Item grants reject limits, overflow, missing delivery and stale quantity", () => ItemQuantityValidationBehavior(root));
            if (group == "All" || group == "ItemQuantitiesRollback")
                Run("failed Item event insert rolls back count and receipt", () => ItemQuantityRollbackBehavior(root));
            if (group == "All" || group == "Scheduler")
                Run("five-second dirty scheduler and failure retry", SchedulerBehavior);
            if (group == "All" || group == "Furniture")
                Run("home seed, layout revision, exact pose, and orphan preservation", () => FurnitureBehavior(root));
            if (group == "All" || group == "FurniturePower")
                Run("Furniture power persists known state through layout CAS and pins useId for replay", () => FurniturePowerBehavior(root));
            if (group == "All" || group == "FurnitureSchemaUpgrade")
                Run("schema-one home initializes inventory once without losing furniture", () => FurnitureSchemaUpgradeBehavior(root));
            if (group == "All" || group == "FurnitureEvents")
                Run("place, move, and return commit causal events and backup inventory", () => FurnitureEventsBehavior(root));
            if (group == "All" || group == "FurnitureDuplicateReplay")
                Run("duplicate furniture operation reloads latest durable home", () => FurnitureDuplicateReplayBehavior(root));
            if (group == "All" || group == "FurnitureAtomicRollback")
                Run("failed furniture event transaction preserves stock and layout", () => FurnitureAtomicRollbackBehavior(root));
            if (group == "All" || group == "FurnitureLostAcknowledgement")
                Run("lost inventory-upgrade acknowledgement does not reseed on reopen", () => FurnitureLostAcknowledgementBehavior(root));
            if (group == "All" || group == "FurnitureStaleRevision")
                Run("stale furniture revisions cannot overwrite or emit events", () => FurnitureStaleRevisionBehavior(root));
            if (group == "All" || group == "FurnitureFutureSchema")
                Run("future home-layout schema fails closed without reseeding", () => FurnitureFutureSchemaBehavior(root));
            if (group == "All" || group == "FurnitureStockGuard")
                Run("home store rejects stock edits without one matching furniture operation", () => FurnitureStockGuardBehavior(root));
            if (group == "All" || group == "Backup")
                Run("consistent game-only checkpoints and staged recovery", () => BackupBehavior(root));
            if (group == "All" || group == "History")
                Run("schema 1 migration, atomic typed history, paging, and backup upgrade", () => HistoryBehavior(root));
            if (group == "All" || group == "Recording")
                Run("recording consent, bounded local capture, immutable contribution and deletion boundary", () => RecordingBehavior(root));
            if (group == "All" || group == "TopicHistory")
                Run("Topic history SQLite import, replay, conflict and schema upgrade", () => TopicHistorySqliteBehavior(root));
            if (group == "All" || group == "TopicDatabaseRelocation")
                Run("earlier root-level Topic SQL state promotes into the shared game database", () => TopicHistoryDatabaseRelocationBehavior(root));
            if (group == "All" || group == "ConversationSessions")
                Run("private session import, normalized reopen, revision conflict and source retention", () => ConversationSessionSqliteBehavior(root));
            if (group == "All" || group == "Transcript")
                Run("private transcript import, source identity, paging, recent context and atomic append", () => TranscriptSqliteBehavior(root));
            if (group == "All" || group == "GeneratedArchive")
                Run("private generated archive migration, payload containment, raw preservation and fail-closed writes", () => GeneratedArchiveSqliteBehavior(root));
            if (group == "All" || group == "RuntimeMigration")
                Run("per-domain migration resume, shared game promotion, target identity and lost-target recovery", () => RuntimeMigrationCoordinatorBehavior(root));
            if (passed == 0) throw new ArgumentException("Unknown or empty persistence test group: " + group);
            Console.WriteLine("RESULT: " + passed + " headless persistence tests passed for " + group + ".");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL after " + passed + " tests: " + exception);
            return 1;
        }
    }

    private static void RecordingBehavior(string root)
    {
        string directory = Path.Combine(root, "recording");
        string recordingPath = Path.Combine(directory, "private recording.sqlite3");
        string exportRoot = Path.Combine(directory, "exports");
        DateTime now = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        var recorder = new InteractionRecorder(recordingPath, () => now);
        RecordingResult off = recorder.Record(Observation("submitted_input", "r0", "draft", null));
        Require(off.state == "disabled" && !File.Exists(recordingPath), "default-off recording created a database or accepted an event");
        Require(recorder.Enable(new RecordingPolicy { policyRevision = "invalid", retentionDays = 31,
            byteCap = 0, recordCap = 0, captureMetadata = true }).state == "invalid" && !File.Exists(recordingPath),
            "invalid consent policy created a recording database");

        RecordingPolicy policy = Policy("metadata-v1", captureText: false, recordCap: 3, byteCap: 8192);
        Require(recorder.Enable(policy).state == "enabled" && File.Exists(recordingPath), "explicit metadata policy did not enable local recording");
        Require(CountRows(recordingPath, "interaction_records") == 0, "enabling recording backfilled prior interactions");
        Require(recorder.Record(Observation("submitted_input", "pre-enable", null, null,
            "2026-10-21T23:59:59Z")).state == "invalid",
            "an observation from before the enabled interval was accepted");
        RecordingResult first = recorder.Record(Observation("submitted_input", "r1", null,
            new Dictionary<string, string> { { "scenarioId", "scenario.synthetic" }, { "stepId", "ask.favorite" }, { "inputKind", "option" } }));
        Require(first.state == "recorded", "metadata-only submitted input was not recorded");
        Require(recorder.Record(Observation("submitted_input", "r-secret", "private",
            new Dictionary<string, string> { { "scenarioId", "scenario.synthetic" } })).state == "scope_rejected",
            "raw text was stored while text scope was disabled");
        Require(recorder.Record(Observation("unknown_kind", "r-unknown", null, null)).state == "invalid",
            "unknown interaction kind was accepted");
        Require(recorder.Record(Observation("submitted_input", "r-secret-field", null,
            new Dictionary<string, string> { { "scenarioId", "scenario.synthetic" }, { "apiKey", "secret" } })).state == "invalid",
            "a credential-like/unknown field was accepted");

        ContributionSelection firstSelection = new ContributionSelection { includeRawText = false };
        ContributionPreview contributions = recorder.Contributions;
        ContributionPreviewResult preview1 = contributions.Create(firstSelection);
        Require(preview1.state == "preview_ready" && preview1.recordCount == 1 && preview1.warning.Length > 0,
            "default contribution preview did not select the single uncontributed record or warn about identifying data");
        RecordingResult second = recorder.Record(Observation("answer_selected", "r2", null,
            new Dictionary<string, string> { { "scenarioId", "scenario.synthetic" }, { "stepId", "ask.favorite" }, { "answerId", "favorite.blue" } }));
        Require(second.state == "recorded", "recording stopped before the explicit cap");
        Require(contributions.Confirm(preview1.previewId, "wrong-hash").state == "invalid",
            "a contribution with the wrong preview hash was confirmed");
        ContributionResult confirmed1 = contributions.Confirm(preview1.previewId, preview1.contentHash);
        Require(confirmed1.state == "confirmed" && contributions.Confirm(preview1.previewId, preview1.contentHash).state == "duplicate",
            "contribution confirmation was not immutable and idempotent");
        Require(CountRows(recordingPath, "contribution_records", "contribution_id='" + preview1.previewId + "'") == 1,
            "a record created after preview was silently added to the confirmed batch");

        RecordingResult third = recorder.Record(Observation("beat_presented", "r3", null,
            new Dictionary<string, string> { { "scenarioId", "scenario.synthetic" }, { "stepId", "line.goodbye" }, { "beatId", "beat.1" } }));
        Require(third.state == "recorded", "a valid authored beat observation was rejected");
        Require(recorder.Record(Observation("feedback", "r4", null,
            new Dictionary<string, string> { { "kind", "positive" } })).state == "cap_reached",
            "the per-session record cap did not stop additional capture");
        ContributionSelection selectionWithExclusion = new ContributionSelection
        {
            excludeRecordIds = new List<string> { second.recordId }
        };
        ContributionPreviewResult preview2 = contributions.Create(selectionWithExclusion);
        Require(preview2.state == "preview_ready" && preview2.recordCount == 1,
            "the contribution exclusion did not freeze the selected membership");
        Require(contributions.Confirm(preview2.previewId, preview2.contentHash).state == "confirmed",
            "a contribution containing the approved non-excluded record did not confirm");
        Require(contributions.Create(new ContributionSelection()).recordCount == 0,
            "default selection included previously contributed or explicitly excluded records");
        ContributionPreviewResult explicitOld = contributions.Create(new ContributionSelection
        { includeRecordIds = new List<string> { second.recordId } });
        Require(explicitOld.state == "preview_ready" && explicitOld.recordCount == 1,
            "an older excluded record could not be explicitly selected again");

        string gamePath = Path.Combine(directory, "game.sqlite3");
        var gameRepository = new SqliteGameRepository(gamePath);
        GameLoadResult game = gameRepository.Load();
        GameCommitRequest gameRequest = Request(game.gameRevision, Guid.NewGuid().ToString("D"), "{\"schemaVersion\":4}", "game-event");
        gameRequest.events.Add(new GameEventRecord { eventId = Guid.NewGuid().ToString("D"), operationId = gameRequest.operationId,
            kind = "synthetic_game_fact", actorId = "host", targetId = "fixture", occurredAtUtc = "2026-10-22T00:00:01Z", payloadJson = "{}" });
        Require(gameRepository.Commit(gameRequest).state == GamePersistenceStates.Committed, "game-history deletion fixture did not commit");
        Require(recorder.DeleteRecords(new[] { first.recordId, second.recordId, third.recordId }).state == "deleted" &&
            CountRows(recordingPath, "interaction_records") == 0 &&
            CountRows(gamePath, "game_events") == 1,
            "deleting optional recording affected permanent game history");
        Require(contributions.Confirm(explicitOld.previewId, explicitOld.contentHash).state == "invalid",
            "deleting a source record did not invalidate its pending contribution preview");
        Require(recorder.Disable().state == "disabled" && recorder.Record(Observation("answer_selected", "after-off", null, null)).state == "disabled",
            "disable did not immediately stop capture");

        DateTime textNow = now.AddDays(2);
        string textPath = Path.Combine(directory, "text-recording.sqlite3");
        var textRecorder = new InteractionRecorder(textPath, () => textNow);
        Require(textRecorder.Enable(Policy("text-v1", captureText: true, recordCap: 4, byteCap: 8192)).state == "enabled",
            "explicit text scope did not enable");
        RecordingResult textRecord = textRecorder.Record(Observation("submitted_input", "text-r1", "private synthetic phrase",
            new Dictionary<string, string> { { "inputKind", "free_text" } }, textNow.ToString("O")));
        Require(textRecord.state == "recorded", "authorized raw input was not recorded locally");
        ContributionPreviewResult textPreview = textRecorder.Contributions.Create(new ContributionSelection { includeRawText = false });
        Require(textPreview.state == "preview_ready" && !textPreview.includesRawText,
            "text content was included without explicit preview selection");
        ContributionResult textContribution = textRecorder.Contributions.Confirm(textPreview.previewId, textPreview.contentHash);
        string exportPath = Path.Combine(exportRoot, "metadata-only.json");
        ContributionExportResult exported = textRecorder.Contributions.ExportConfirmed(textContribution.contributionId, exportPath);
        Require(exported.state == "exported" && !File.ReadAllText(exportPath).Contains("private synthetic phrase") &&
            CountRows(textPath, "contributions", "status='uploaded' OR status='delivered'") == 0,
            "export included unselected text or recorded fictional delivery");

        ContributionPreviewResult explicitTextPreview = textRecorder.Contributions.Create(new ContributionSelection
        { includeRecordIds = new List<string> { textRecord.recordId }, includeRawText = true });
        Require(explicitTextPreview.state == "preview_ready" && explicitTextPreview.includesRawText &&
            textRecorder.Contributions.Confirm(explicitTextPreview.previewId, explicitTextPreview.contentHash).state == "confirmed",
            "explicitly reviewed text was not included in a frozen preview");
        ContributionExportResult explicitTextExport = textRecorder.Contributions.ExportConfirmed(
            explicitTextPreview.previewId, Path.Combine(exportRoot, "text-approved.json"));
        Require(explicitTextExport.state == "exported" && File.ReadAllText(explicitTextExport.path).Contains("private synthetic phrase"),
            "an explicitly selected text projection was not exported from its confirmed snapshot");

        string textRecord2 = textRecorder.Record(Observation("submitted_input", "text-r2", "another private phrase",
            new Dictionary<string, string> { { "inputKind", "free_text" } }, textNow.ToString("O"))).recordId;
        ContributionPreviewResult pending = textRecorder.Contributions.Create(new ContributionSelection
        { includeRecordIds = new List<string> { textRecord2 }, includeRawText = true });
        Require(pending.state == "preview_ready" && pending.includesRawText && pending.warning.Length > 0,
            "explicit text preview did not retain a warning and exact membership");
        ContributionResult pendingConfirmed = textRecorder.Contributions.Confirm(pending.previewId, pending.contentHash);
        Require(textRecorder.Disable().state == "disabled" && CountRows(textPath, "contributions", "status='invalidated'") == 1 &&
            textRecorder.Contributions.ExportConfirmed(pendingConfirmed.contributionId, Path.Combine(exportRoot, "revoked.json")).state == "disabled",
            "disabling recording did not invalidate an unsent confirmed batch");
        Require(!File.ReadAllText(exportPath).Contains("uploaded"), "local export claimed a delivery receipt");

        DateTime expiredNow = now.AddDays(3);
        textRecorder = new InteractionRecorder(textPath, () => expiredNow);
        Require(textRecorder.Enable(Policy("text-v1", captureText: true, recordCap: 4, byteCap: 8192)).state == "enabled" &&
            CountRows(textPath, "interaction_records") == 0,
            "expired recording records were not pruned on the next explicit enable");
        textRecorder.Disable();

        string byteCapPath = Path.Combine(directory, "byte-cap.sqlite3");
        var byteCapRecorder = new InteractionRecorder(byteCapPath, () => now);
        Require(byteCapRecorder.Enable(Policy("byte-cap-v1", captureText: false, recordCap: 8, byteCap: 1)).state == "enabled" &&
            byteCapRecorder.Record(Observation("answer_selected", "byte-cap-record", null,
                new Dictionary<string, string> { { "answerId", "choice.no" } })).state == "cap_reached" &&
            CountRows(byteCapPath, "interaction_records") == 0,
            "the byte cap did not reject an oversized record atomically");
        byteCapRecorder.Disable();

        string editedPath = Path.Combine(directory, "edited-source.sqlite3");
        var editedRecorder = new InteractionRecorder(editedPath, () => now);
        editedRecorder.Enable(Policy("edit-v1", captureText: false, recordCap: 4, byteCap: 8192));
        string editedRecordId = editedRecorder.Record(Observation("answer_selected", "edit-source", null,
            new Dictionary<string, string> { { "answerId", "choice.one" } })).recordId;
        ContributionPreviewResult editedPreview = editedRecorder.Contributions.Create(new ContributionSelection());
        using (var connection = new SQLiteConnection(editedPath))
            connection.Execute("UPDATE interaction_records SET metadata_json=? WHERE record_id=?", "{\"answerId\":\"changed\"}", editedRecordId);
        Require(editedRecorder.Contributions.Confirm(editedPreview.previewId, editedPreview.contentHash).state == "invalid",
            "a source edit after preview reused the old confirmation hash");
        editedRecorder.Disable();
    }

    private static void ItemMigrationBehavior(string root)
    {
        string directory = Path.Combine(root, "Item migration");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "schema three.sqlite3");
        string saveId = Guid.NewGuid().ToString("D");
        string alterraId = Guid.NewGuid().ToString("D");
        string homeId = Guid.NewGuid().ToString("D");
        string operationId = Guid.NewGuid().ToString("D");
        string eventId = Guid.NewGuid().ToString("D");
        const string progression = "{\"schemaVersion\":4,\"legacy\":\"preserved\"}";
        CreateVersionThreeFixture(path, saveId, alterraId, homeId, operationId, eventId, progression);

        string layoutJson = "{\"schemaVersion\":2,\"instances\":[{\"instanceId\":\"desk.saved\"}]}";
        using (var connection = new SQLiteConnection(path))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 3, "the synthetic migration input is not schema 3");
            connection.Execute("INSERT INTO home_layout_snapshots(home_id,revision,payload_json) VALUES (?,?,?)", homeId, 2, layoutJson);
            connection.Execute("UPDATE game_meta SET value='2' WHERE key='layout_revision'");
            connection.Execute("UPDATE topic_history_state SET revision=1 WHERE singleton_id=1");
            connection.Execute("INSERT INTO topic_history_operations(operation_id,payload_hash,revision) VALUES ('topic-v3-op',?,1)", LegacyRequestHash("topic-v3-payload"));
            connection.Execute("INSERT INTO migration_receipts(source_hash,operation_id,imported_at_utc,game_revision) VALUES (?,?,?,1)",
                LegacyRequestHash("legacy-source"), "legacy-migration", "2026-10-22T00:00:00Z");
        }

        var repository = new SqliteGameRepository(path);
        GameLoadResult loaded = repository.Load();
        Require(loaded.state == GamePersistenceStates.Ready && loaded.identity.saveId == saveId &&
            loaded.identity.alterraId == alterraId && loaded.identity.homeId == homeId &&
            loaded.gameRevision == 1 && loaded.layoutRevision == 2 && loaded.progressionJson == progression &&
            loaded.layoutJson == layoutJson,
            "schema 3 migration changed save identity, snapshots, or revisions");
        Require(repository.FindReceipt(operationId) != null && repository.FindMigration(LegacyRequestHash("legacy-source")) != null,
            "schema 3 migration lost a game operation or migration receipt");
        IList<GameHistoryEvent> events = new GameHistoryReader(path).ReadEvents(new GameHistoryQuery { pageSize = 10 });
        Require(events.Count == 1 && events[0].eventId == eventId && events[0].payloadJson == "{\"legacy\":true}",
            "schema 3 migration changed the retained event");

        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite, true))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5,
                "opening a schema 3 save did not advance it to schema 5");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='player_item_quantities'") == 1,
            "schema 5 did not retain player_item_quantities");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM player_item_quantities") == 0,
            "a schema 3 save was given inferred Item quantities");
            Require(connection.ExecuteScalar<long>("SELECT revision FROM topic_history_state WHERE singleton_id=1") == 1 &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM topic_history_operations WHERE operation_id='topic-v3-op'") == 1,
                "schema 3 migration changed retained Topic history");
        }

        MethodInfo readItems = typeof(SqliteGameRepository).GetMethod("ReadPlayerItemQuantities");
        Require(readItems != null, "IGameRepository/SqliteGameRepository lacks ReadPlayerItemQuantities");
        object inventory = readItems.Invoke(repository, null);
        Type inventoryType = inventory.GetType();
        Require((string)inventoryType.GetField("state").GetValue(inventory) == GamePersistenceStates.Ready &&
            ((System.Collections.ICollection)inventoryType.GetField("items").GetValue(inventory)).Count == 0,
            "a migrated save did not open with an empty readable Item inventory");

        string legacyPath = Path.Combine(directory, "schema four with unpinned invocation.sqlite3");
        CreateVersionThreeFixture(legacyPath, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), progression);
        using (var connection = new SQLiteConnection(legacyPath, SQLiteOpenFlags.ReadWrite, true))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.BeginTransaction();
            Type migration = typeof(SqliteGameRepository).Assembly.GetType("AlterOS.Persistence.GameSchemaMigrations", true);
            MethodInfo version3To4 = migration.GetMethod("MigrateVersion3To4", BindingFlags.Static | BindingFlags.NonPublic);
            version3To4.Invoke(null, new object[] { connection });
            connection.Execute("INSERT INTO scenario_runs(run_id,scenario_id,started_at_utc) VALUES ('legacy-run','legacy-scenario','2026-10-23T00:00:00Z')");
            connection.Execute("INSERT INTO flow_invocations(invocation_id,run_id,flow_id,started_at_utc) VALUES ('legacy-invocation','legacy-run','flow.same-id','2026-10-23T00:00:01Z')");
            connection.Commit();
        }
        GameLoadResult legacyUpgraded = new SqliteGameRepository(legacyPath).Load();
        Require(legacyUpgraded.state == GamePersistenceStates.Ready,
            "a real schema 4 database did not upgrade to schema 5: " + legacyUpgraded.state + "/" + legacyUpgraded.errorCode + " " + legacyUpgraded.message);
        using (var connection = new SQLiteConnection(legacyPath, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5 &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM flow_invocations WHERE invocation_id='legacy-invocation' AND flow_store_id='' AND flow_revision='' AND flow_hash=''") == 1,
                "schema 4 migration fabricated a content pin for a legacy invocation");
    }

    private static void CreateVersionThreeFixture(string path, string saveId, string alterraId, string homeId,
        string operationId, string eventId, string progression)
    {
        CreateVersionOneFixture(path, saveId, alterraId, homeId, operationId, eventId, progression, "legacy-retry-v3");
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite, true))
        {
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.BeginTransaction();
            try
            {
                Type migration = typeof(SqliteGameRepository).Assembly.GetType("AlterOS.Persistence.GameSchemaMigrations", true);
                MethodInfo version1To2 = migration.GetMethod("MigrateVersion1To2", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo version2To3 = migration.GetMethod("MigrateVersion2To3", BindingFlags.Static | BindingFlags.NonPublic);
                Require(version1To2 != null && version2To3 != null, "the existing v1/v2 migration methods are unavailable to the synthetic fixture");
                version1To2.Invoke(null, new object[] { connection });
                version2To3.Invoke(null, new object[] { connection });
                connection.Commit();
            }
            catch
            {
                if (connection.IsInTransaction) connection.Rollback();
                throw;
            }
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 3,
                "the synthetic fixture builder did not produce a schema 3 save");
        }
    }

    private static void ItemQuantityGrantBehavior(string root)
    {
        string path = Path.Combine(root, "Item quantities", "grant.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        string ownerId = initial.identity.legacyPlayerId ?? initial.identity.accountId ?? initial.identity.saveId;
        string operationId = Guid.NewGuid().ToString("D");
        GameCommitRequest first = ItemGrantRequest(initial.gameRevision, ownerId, operationId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));

        GameCommitResult committed = repository.Commit(first);
        Require(committed.state == GamePersistenceStates.Committed,
            "a valid completed-Dialogue Item grant did not commit: " + committed.state + "/" + committed.errorCode + " " + committed.message);
        GameItemInventoryResult inventory = repository.ReadPlayerItemQuantities();
        Require(inventory.state == GamePersistenceStates.Ready && inventory.playerId == ownerId && inventory.items.Count == 1 &&
            inventory.items[0].itemId == "item.water_glass" && inventory.items[0].quantity == 1,
            "the committed Item count was not readable as exactly one Water glass");
        Require(repository.FindReceipt(operationId) != null && CountRows(path, "game_events", "kind='item.quantity_changed'") == 1 &&
            CountRows(path, "event_context", "cause_kind='source_operation'") == 1,
            "the committed Item grant lacks its event context or operation receipt");
        GameItemQuantityOperationResult originalOperation = repository.FindItemQuantityOperation(operationId);
        Require(originalOperation.IsFound && originalOperation.change.expectedQuantity == 0 &&
            originalOperation.change.changeId == first.itemQuantityChanges[0].changeId &&
            originalOperation.change.actionId == first.itemQuantityChanges[0].actionId &&
            originalOperation.change.actionRevision == first.itemQuantityChanges[0].actionRevision &&
            originalOperation.change.eventId == first.itemQuantityChanges[0].eventId &&
            originalOperation.eventContext.invocationId == first.itemQuantityChanges[0].invocationId,
            "the committed Item operation could not be reconstructed from its receipt, event, and execution context");

        Require(repository.Commit(first).state == GamePersistenceStates.Duplicate,
            "replaying the same Flow step did not return duplicate");
        inventory = repository.ReadPlayerItemQuantities();
        Require(inventory.items.Count == 1 && inventory.items[0].quantity == 1 &&
            CountRows(path, "game_events", "kind='item.quantity_changed'") == 1,
            "replaying one Flow step incremented the Item quantity or duplicated its event");

        GameLoadResult afterFirst = repository.Load();
        GameCommitRequest secondOccurrence = ItemGrantRequest(afterFirst.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), 1);
        Require(repository.Commit(secondOccurrence).state == GamePersistenceStates.Committed,
            "a separate valid Scenario occurrence could not grant the same Item again");
        inventory = repository.ReadPlayerItemQuantities();
        Require(inventory.items.Count == 1 && inventory.items[0].quantity == 2,
            "a separate valid Flow invocation did not produce exactly one additional Item");
        GameItemQuantityOperationResult afterRestart = new SqliteGameRepository(path).FindItemQuantityOperation(operationId);
        Require(afterRestart.IsFound && afterRestart.change.expectedQuantity == 0 && afterRestart.change.delta == 1 &&
            afterRestart.change.changeId == first.itemQuantityChanges[0].changeId,
            "post-restart lookup derived the original expected quantity from the newer inventory count or lost operation identity");

        first.itemQuantityChanges[0].delta = 2;
        first.events[0].payloadJson = ItemQuantityEventJson(first.itemQuantityChanges[0], 0, 2, 2);
        Require(repository.Commit(first).state == GamePersistenceStates.ConflictingOperation,
            "reusing a receipt with changed Item payload was not rejected");
        inventory = repository.ReadPlayerItemQuantities();
        Require(inventory.items.Count == 1 && inventory.items[0].quantity == 2,
            "a conflicting operation changed the Item quantity");
    }

    private static void ItemQuantityValidationBehavior(string root)
    {
        string path = Path.Combine(root, "Item quantities", "validation.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        string ownerId = initial.identity.legacyPlayerId ?? initial.identity.accountId ?? initial.identity.saveId;

        GameCommitRequest mismatchedEvent = ItemGrantRequest(initial.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        mismatchedEvent.events[0].payloadJson = "{\"itemId\":\"item.other\"}";
        Require(repository.Commit(mismatchedEvent).state == GamePersistenceStates.Invalid,
            "an Item event payload that disagrees with the typed quantity change was accepted");
        Require(repository.ReadPlayerItemQuantities().items.Count == 0 && CountRows(path, "game_events") == 0 &&
            repository.FindReceipt(mismatchedEvent.operationId) == null,
            "a mismatched Item event payload wrote a count, event, or receipt");

        GameCommitRequest wrongRun = ItemGrantRequest(initial.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        wrongRun.itemQuantityChanges[0].runId = Guid.NewGuid().ToString("D");
        Require(repository.Commit(wrongRun).state == GamePersistenceStates.Invalid,
            "an Item grant whose completed Dialogue belongs to another Scenario run was accepted");

        GameCommitRequest wrongStep = ItemGrantRequest(initial.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        wrongStep.stepExecutions[0].stepId = "dialogue.other";
        Require(repository.Commit(wrongStep).state == GamePersistenceStates.Invalid,
            "an Item grant whose execution is not the required Dialogue step was accepted");
        Require(repository.ReadPlayerItemQuantities().items.Count == 0 && CountRows(path, "game_events") == 0 &&
            repository.FindReceipt(wrongRun.operationId) == null && repository.FindReceipt(wrongStep.operationId) == null,
            "a wrong-run or wrong-step rejection wrote an Item, event, or receipt");

        GameCommitRequest overLimit = ItemGrantRequest(initial.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), 0, 100, 99);
        Require(repository.Commit(overLimit).state == GamePersistenceStates.Invalid,
            "an Item grant above the pinned stack limit was accepted");
        Require(repository.ReadPlayerItemQuantities().items.Count == 0 && CountRows(path, "game_events") == 0 &&
            repository.FindReceipt(overLimit.operationId) == null,
            "a stack-limit rejection wrote an Item, event, or receipt");

        GameCommitRequest overflow = ItemGrantRequest(repository.Load().gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), long.MaxValue, 1, long.MaxValue);
        Require(repository.Commit(overflow).state == GamePersistenceStates.Invalid,
            "an Int64 Item quantity overflow was accepted");

        GameCommitRequest missingDelivery = ItemGrantRequest(repository.Load().gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        missingDelivery.itemQuantityChanges[0].completedDeliveryStepExecutionId = Guid.NewGuid().ToString("D");
        Require(repository.Commit(missingDelivery).state == GamePersistenceStates.Invalid,
            "an Item grant without the completed required Dialogue execution was accepted");
        Require(repository.ReadPlayerItemQuantities().items.Count == 0 && CountRows(path, "game_events") == 0 &&
            repository.FindReceipt(missingDelivery.operationId) == null,
            "a missing-Dialogue rejection wrote an Item, event, or receipt");

        GameCommitRequest first = ItemGrantRequest(repository.Load().gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        Require(repository.Commit(first).state == GamePersistenceStates.Committed,
            "a valid grant fixture for the stale-count case did not commit");
        GameCommitRequest staleQuantity = ItemGrantRequest(repository.Load().gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), 0, 1, 99);
        Require(repository.Commit(staleQuantity).state == GamePersistenceStates.StaleRevision,
            "a stale expected Item quantity was accepted");
        Require(repository.ReadPlayerItemQuantities().items.Single().quantity == 1 &&
            CountRows(path, "game_events", "kind='item.quantity_changed'") == 1 &&
            repository.FindReceipt(staleQuantity.operationId) == null,
            "a stale Item count wrote a partial event, receipt, or quantity");
    }

    private static void ItemQuantityRollbackBehavior(string root)
    {
        string path = Path.Combine(root, "Item quantities", "rollback.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        string ownerId = initial.identity.legacyPlayerId ?? initial.identity.accountId ?? initial.identity.saveId;
        GameCommitRequest first = ItemGrantRequest(initial.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        Require(repository.Commit(first).state == GamePersistenceStates.Committed,
            "the rollback fixture's initial grant did not commit");

        GameLoadResult before = repository.Load();
        GameCommitRequest failing = ItemGrantRequest(before.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), 1, 1, 99);
        using (var connection = new SQLiteConnection(path))
            connection.Execute("CREATE TRIGGER reject_item_event BEFORE INSERT ON game_events WHEN NEW.kind='item.quantity_changed' BEGIN SELECT RAISE(ABORT,'synthetic Item event failure'); END");
        GameCommitResult result = repository.Commit(failing);
        Require(!result.IsDurable, "a failing Item event transaction reported durable success");
        GameLoadResult after = repository.Load();
        Require(after.gameRevision == before.gameRevision && repository.ReadPlayerItemQuantities().items.Single().quantity == 1 &&
            CountRows(path, "game_events", "kind='item.quantity_changed'") == 1 && repository.FindReceipt(failing.operationId) == null,
            "the failed Item event transaction leaked the quantity, event, revision, or receipt");
    }

    private static void ItemCompletionAtomicBehavior(string root)
    {
        string path = Path.Combine(root, "Item completion atomic", "completion.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        string ownerId = initial.identity.legacyPlayerId ?? initial.identity.accountId ?? initial.identity.saveId;
        string runId = Guid.NewGuid().ToString("D");
        string invocationId = Guid.NewGuid().ToString("D");
        string deliveryExecutionId = Guid.NewGuid().ToString("D");
        string grantExecutionId = Guid.NewGuid().ToString("D");

        GameCommitRequest presentation = ItemGrantRequest(initial.gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), runId, invocationId, grantExecutionId);
        presentation.events.Clear();
        presentation.eventContexts.Clear();
        presentation.itemQuantityChanges.Clear();
        presentation.stepExecutions[0].executionId = deliveryExecutionId;
        foreach (GameStepExecutionRecord step in presentation.stepExecutions)
        {
            step.outcome = null;
            step.completedAtUtc = null;
        }
        GameCommitResult presented = repository.Commit(presentation);
        Require(presented.state == GamePersistenceStates.Committed &&
            CountRows(path, "step_executions", "outcome IS NULL") == 2,
            "the Dialogue and grant-step presentation fixture did not persist as incomplete visits");

        GameCommitRequest completion = ItemGrantRequest(repository.Load().gameRevision, ownerId,
            Guid.NewGuid().ToString("D"), runId, invocationId, grantExecutionId);
        completion.scenarioRuns.Clear();
        completion.flowInvocations.Clear();
        completion.stepExecutions[0].executionId = deliveryExecutionId;
        completion.itemQuantityChanges[0].completedDeliveryStepExecutionId = deliveryExecutionId;
        completion.events[0].payloadJson = ItemQuantityEventJson(completion.itemQuantityChanges[0], 0, 1, 1);
        foreach (GameStepExecutionRecord step in completion.stepExecutions)
            step.presentedAtUtc = null;

        using (var connection = new SQLiteConnection(path))
            connection.Execute("CREATE TRIGGER reject_completion_item_event BEFORE INSERT ON game_events WHEN NEW.kind='item.quantity_changed' BEGIN SELECT RAISE(ABORT,'synthetic combined completion failure'); END");
        GameCommitResult rejected = repository.Commit(completion);
        int unfinishedDeliveryCount;
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly))
            unfinishedDeliveryCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM step_executions WHERE execution_id=? AND invocation_id=? AND step_id=? AND outcome IS NULL AND completed_at_utc IS NULL",
                deliveryExecutionId, invocationId, "delivery-dialogue");
        Require(!rejected.IsDurable && unfinishedDeliveryCount == 1 && repository.ReadPlayerItemQuantities().items.Count == 0 &&
            CountRows(path, "game_events", "kind='item.quantity_changed'") == 0 &&
            repository.FindReceipt(completion.operationId) == null,
            "a failed combined completion/grant transaction left a completed Dialogue, Item, event, or receipt");
        using (var connection = new SQLiteConnection(path)) connection.Execute("DROP TRIGGER reject_completion_item_event");

        GameCommitResult committed = repository.Commit(completion);
        Require(committed.state == GamePersistenceStates.Committed,
            "a completion-only Dialogue result and Item grant failed to commit atomically: " +
            committed.state + "/" + committed.errorCode + " " + committed.message);
        GameItemInventoryResult inventory = repository.ReadPlayerItemQuantities();
        int completedDeliveryCount;
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly))
            completedDeliveryCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM step_executions WHERE execution_id=? AND invocation_id=? AND step_id=? AND outcome='completed' AND completed_at_utc=?",
                deliveryExecutionId, invocationId, "delivery-dialogue", completion.stepExecutions[0].completedAtUtc);
        Require(inventory.items.Count == 1 && inventory.items[0].quantity == 1 && completedDeliveryCount == 1 &&
            CountRows(path, "game_events", "kind='item.quantity_changed'") == 1 &&
            repository.FindReceipt(completion.operationId) != null,
            "completion plus grant did not durably write the exact Dialogue result, Item count, event, and receipt");
    }

    private static GameCommitRequest ItemGrantRequest(long expectedGameRevision, string ownerId,
        string operationId, string runId, string invocationId, string grantStepExecutionId,
        long expectedQuantity = 0, long delta = 1, long maximumQuantity = 99)
    {
        const string itemId = "item.water_glass";
        const string itemRevision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string deliveryExecutionId = Guid.NewGuid().ToString("D");
        const string now = "2026-10-22T00:00:00Z";
        var request = Request(expectedGameRevision, operationId, "{\"schemaVersion\":4}",
            "scenario-item-grant|" + runId + "|" + invocationId + "|" + grantStepExecutionId + "|" + itemId + "|" + itemRevision);
        string eventId = Guid.NewGuid().ToString("D");
        var change = new GameItemQuantityChangeRecord
        {
            changeId = Guid.NewGuid().ToString("D"),
            actionId = "action.grant_water", actionRevision = new string('b', 64),
            itemId = itemId, itemRevision = itemRevision, expectedQuantity = expectedQuantity, delta = delta,
            maximumQuantity = maximumQuantity,
            requiresCompletedStepId = "delivery-dialogue", completedDeliveryStepExecutionId = deliveryExecutionId
        };
        request.events.Add(new GameEventRecord
        {
            eventId = eventId, operationId = operationId, kind = "item.quantity_changed",
            actorId = ownerId, targetId = itemId, occurredAtUtc = now,
            payloadJson = ItemQuantityEventJson(change, expectedQuantity, delta,
                expectedQuantity > long.MaxValue - delta ? (long?)null : expectedQuantity + delta)
        });
        request.eventContexts.Add(new GameEventHistoryContext
        {
            eventId = eventId, runId = runId, invocationId = invocationId,
            stepExecutionId = grantStepExecutionId, causeKind = "source_operation", causeReferenceId = operationId
        });
        request.scenarioRuns.Add(new GameScenarioRunRecord
        { runId = runId, scenarioId = "scenario.fetch_drink", startedAtUtc = now });
        request.flowInvocations.Add(new GameFlowInvocationRecord
        { invocationId = invocationId, runId = runId, flowId = "flow.fetch_drink", startedAtUtc = now });
        request.stepExecutions.Add(new GameStepExecutionRecord
        {
            executionId = deliveryExecutionId, invocationId = invocationId, stepId = "delivery-dialogue",
            visitOrdinal = 1, presentedAtUtc = now, outcome = "completed", completedAtUtc = now
        });
        request.stepExecutions.Add(new GameStepExecutionRecord
        {
            executionId = grantStepExecutionId, invocationId = invocationId, stepId = "give-water-glass",
            visitOrdinal = 1, presentedAtUtc = now, outcome = "completed", completedAtUtc = now
        });
        change.eventId = eventId;
        change.ownerId = ownerId;
        change.runId = runId;
        change.invocationId = invocationId;
        change.stepExecutionId = grantStepExecutionId;
        change.maximumQuantity = maximumQuantity;
        request.itemQuantityChanges.Add(change);
        return request;
    }

    private static string ItemQuantityEventJson(GameItemQuantityChangeRecord change, long before, long delta, long? after)
    {
        return "{\"changeId\":\"" + change.changeId + "\",\"actionId\":\"" + change.actionId +
            "\",\"actionRevision\":\"" + change.actionRevision + "\",\"itemId\":\"" + change.itemId + "\",\"itemRevision\":\"" + change.itemRevision +
            "\",\"before\":" + before.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"delta\":" + delta.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"after\":" + (after.HasValue ? after.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "\"overflow\"") +
            ",\"maximumQuantity\":" + change.maximumQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"requiresCompletedStepId\":\"" + change.requiresCompletedStepId +
            "\",\"completedDeliveryStepExecutionId\":\"" + change.completedDeliveryStepExecutionId + "\"}";
    }

    private static void HistoryBehavior(string root)
    {
        string directory = Path.Combine(root, "history migration");
        Directory.CreateDirectory(directory);
        string legacyPath = Path.Combine(directory, "schema one.sqlite3");
        string backupPath = Path.Combine(directory, "checkpoint-schema-one.sqlite3");
        string operationId = Guid.NewGuid().ToString("D");
        string eventId = Guid.NewGuid().ToString("D");
        string saveId = Guid.NewGuid().ToString("D");
        string alterraId = Guid.NewGuid().ToString("D");
        string homeId = Guid.NewGuid().ToString("D");
        const string progression = "{\"schemaVersion\":4,\"legacy\":\"preserved\"}";
        const string operationPayload = "legacy-retry";
        CreateVersionOneFixture(legacyPath, saveId, alterraId, homeId, operationId, eventId, progression, operationPayload);
        File.Copy(legacyPath, backupPath);
        byte[] backupBytes = File.ReadAllBytes(backupPath);

        GameBackupValidationResult backupValidation = new SqliteGameRepository(legacyPath).ValidateBackup(backupPath);
        Require(backupValidation.state == GamePersistenceStates.Ready, "schema 1 backup was not accepted for staged upgrade");
        GameLoadResult migrated = new SqliteGameRepository(legacyPath).Load();
        Require(migrated.state == GamePersistenceStates.Ready && migrated.identity.saveId == saveId &&
            migrated.identity.alterraId == alterraId && migrated.identity.homeId == homeId &&
            migrated.gameRevision == 1 && migrated.progressionJson == progression,
            "schema 1 migration did not preserve identity, state, or revision");
        Require(BytesEqual(backupBytes, File.ReadAllBytes(backupPath)), "validating/migrating the DB changed the original schema 1 backup");

        var legacyRetry = Request(1, operationId, progression, operationPayload);
        Require(new SqliteGameRepository(legacyPath).Commit(legacyRetry).state == GamePersistenceStates.Duplicate,
            "legacy v1 operation receipt hash did not survive the schema upgrade");
        var migratedReader = new GameHistoryReader(legacyPath);
        IList<GameHistoryEvent> originalEvents = migratedReader.ReadEvents(new GameHistoryQuery { pageSize = 1 });
        Require(originalEvents.Count == 1 && originalEvents[0].eventId == eventId && originalEvents[0].payloadJson == "{\"legacy\":true}",
            "legacy game event did not survive the schema upgrade");

        string atomicPath = Path.Combine(directory, "interrupted.sqlite3");
        CreateVersionOneFixture(atomicPath, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), progression, "seed");
        using (var connection = new SQLiteConnection(atomicPath))
            connection.Execute("CREATE TABLE stat_changes (synthetic_blocker INTEGER)");
        Require(new SqliteGameRepository(atomicPath).Load().state != GamePersistenceStates.Ready,
            "interrupted schema migration unexpectedly succeeded");
        using (var connection = new SQLiteConnection(atomicPath))
        {
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 1, "interrupted migration advanced user_version");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='event_context'") == 0,
                "interrupted migration left partial history tables");
            connection.Execute("DROP TABLE stat_changes");
        }
        Require(new SqliteGameRepository(atomicPath).Load().state == GamePersistenceStates.Ready,
            "atomic migration did not recover after the blocker was removed");

        string livePath = Path.Combine(directory, "restore target.sqlite3");
        CreateVersionOneFixture(livePath, saveId, alterraId, homeId, operationId, eventId, progression, operationPayload);
        Require(new SqliteGameRepository(livePath).Load().state == GamePersistenceStates.Ready, "restore target did not migrate");
        string backupRoot = Path.Combine(directory, "recovery backups");
        Directory.CreateDirectory(backupRoot);
        string sourceBackup = Path.Combine(backupRoot, "checkpoint-schema-one.sqlite3");
        File.Copy(backupPath, sourceBackup);
        byte[] sourceBackupBytes = File.ReadAllBytes(sourceBackup);
        var recoveryRepository = new SqliteGameRepository(livePath);
        var backupService = new GameBackupService(recoveryRepository, backupRoot);
        GameRecoveryResult staged = backupService.StageRecovery("checkpoint-schema-one");
        Require(staged.state == GamePersistenceStates.RecoveryStaged, "schema 1 checkpoint did not stage for schema 2 restore");
        GameRecoveryResult restored = backupService.ApplyRecovery(staged.candidate, true);
        Require(restored.state == GamePersistenceStates.Recovered && new SqliteGameRepository(livePath).Load().gameRevision == 1,
            "staged schema 1 checkpoint was not upgraded before restore");
        Require(BytesEqual(sourceBackupBytes, File.ReadAllBytes(sourceBackup)), "restoring schema 1 modified the original backup bytes");

        string historyPath = Path.Combine(directory, "history writes.sqlite3");
        var repository = new SqliteGameRepository(historyPath);
        GameLoadResult initial = repository.Load();
        string historyOperationId = Guid.NewGuid().ToString("D");
        string historyEventId = Guid.NewGuid().ToString("D");
        GameCommitRequest request = Request(initial.gameRevision, historyOperationId,
            "{\"schemaVersion\":4,\"history\":true}", "history-op");
        request.events.Add(new GameEventRecord
        {
            eventId = historyEventId, operationId = historyOperationId, kind = "simulation_checkpoint",
            actorId = "alterra", targetId = "progression", occurredAtUtc = "2026-10-22T00:00:00Z",
            payloadJson = "{\"intervalMinutes\":5}"
        });
        request.eventContexts.Add(new GameEventHistoryContext
        {
            eventId = historyEventId, runId = "run.synthetic", invocationId = "invoke.synthetic",
            causeKind = "simulation_checkpoint", simulationRuleId = "needs-decay", simulationRuleVersion = "2",
            intervalStartUtc = "2026-10-21T23:55:00Z", intervalEndUtc = "2026-10-22T00:00:00Z"
        });
        request.statChanges.Add(new GameStatChangeRecord
        {
            changeId = Guid.NewGuid().ToString("D"), eventId = historyEventId, ownerKind = "environment",
            ownerId = initial.identity.homeId, statId = "environment.cleanliness", beforeKnown = true,
            beforeValue = 20f, afterKnown = true, afterValue = 18f, appliedDelta = -2f, aggregated = true
        });
        request.statChanges.Add(new GameStatChangeRecord
        {
            changeId = Guid.NewGuid().ToString("D"), eventId = historyEventId, ownerKind = "player",
            ownerId = initial.identity.saveId, statId = "player.focus_energy", beforeKnown = false,
            afterKnown = false, appliedDelta = null
        });
        request.scenarioRuns.Add(new GameScenarioRunRecord
        { runId = "run.synthetic", scenarioId = "scenario.synthetic", startedAtUtc = "2026-10-21T23:55:00Z" });
        request.flowInvocations.Add(new GameFlowInvocationRecord
        { invocationId = "invoke.synthetic", runId = "run.synthetic", flowId = "flow.synthetic", startedAtUtc = "2026-10-21T23:55:00Z" });
        Require(repository.Commit(request).state == GamePersistenceStates.Committed, "typed simulation history commit failed");
        Require(repository.Commit(request).state == GamePersistenceStates.Duplicate, "duplicate checkpoint changed history or reapplied state");
        GameCommitRequest compatibleHistoryRetry = Request(initial.gameRevision, historyOperationId,
            "{\"schemaVersion\":4,\"history\":true}", "history-op");
        compatibleHistoryRetry.events.Add(request.events[0]);
        Require(repository.Commit(compatibleHistoryRetry).state == GamePersistenceStates.Duplicate,
            "a legacy-style retry did not match the retained base hash of a history-bearing operation");
        GameCommitRequest changedHistory = Request(initial.gameRevision, historyOperationId,
            "{\"schemaVersion\":4,\"history\":true}", "history-op");
        changedHistory.events.Add(request.events[0]);
        changedHistory.eventContexts.Add(request.eventContexts[0]);
        changedHistory.statChanges.Add(new GameStatChangeRecord
        {
            changeId = request.statChanges[0].changeId, eventId = historyEventId, ownerKind = "environment",
            ownerId = initial.identity.homeId, statId = "environment.cleanliness", beforeKnown = true,
            beforeValue = 20f, afterKnown = true, afterValue = 17f, appliedDelta = -3f, aggregated = true
        });
        changedHistory.statChanges.Add(request.statChanges[1]);
        Require(repository.Commit(changedHistory).state == GamePersistenceStates.ConflictingOperation,
            "changed stat history reused an operation ID without conflict");

        IList<GameHistoryEvent> page1 = new GameHistoryReader(historyPath).ReadEvents(new GameHistoryQuery { pageSize = 1 });
        Require(page1.Count == 1 && page1[0].runId == "run.synthetic" && page1[0].causeKind == "simulation_checkpoint",
            "typed event context did not round trip through the bounded reader");
        IList<GameStatChangeRecord> changes = new GameHistoryReader(historyPath).ReadStatChanges(new GameHistoryQuery { pageSize = 8 });
        Require(changes.Count == 2 && changes[0].ownerKind == "environment" && changes[0].appliedDelta == -2f &&
            changes[0].aggregated && changes[1].ownerKind == "player" && !changes[1].beforeKnown && changes[1].appliedDelta == null,
            "typed non-actor stat owners, clamped delta, or unknown semantics did not round trip");
        bool pageBounded = false;
        try { new GameHistoryReader(historyPath).ReadEvents(new GameHistoryQuery { pageSize = 129 }); }
        catch (ArgumentOutOfRangeException) { pageBounded = true; }
        Require(pageBounded, "history reader accepted an unbounded page");

        string checkpointOperationId = Guid.NewGuid().ToString("D");
        string checkpointEventId = Guid.NewGuid().ToString("D");
        string effectEventId = Guid.NewGuid().ToString("D");
        string checkpointId = Guid.NewGuid().ToString("D");
        const string actionExecutionId = "action.synthetic.1";
        GameCommitRequest checkpointRequest = Request(repository.Load().gameRevision, checkpointOperationId,
            "{\"schemaVersion\":4,\"action\":true}", "confirmed-action");
        checkpointRequest.events.Add(new GameEventRecord { eventId = checkpointEventId, operationId = checkpointOperationId,
            kind = "action_checkpoint_confirmed", actorId = "host", targetId = "activity", occurredAtUtc = "2026-10-22T00:03:00Z", payloadJson = "{}" });
        checkpointRequest.events.Add(new GameEventRecord { eventId = effectEventId, operationId = checkpointOperationId,
            kind = "activity_completed", actorId = "host", targetId = "activity", occurredAtUtc = "2026-10-22T00:03:01Z", payloadJson = "{}" });
        checkpointRequest.actionCheckpoints.Add(new GameActionCheckpointRecord
        {
            checkpointId = checkpointId, eventId = checkpointEventId, actionExecutionId = actionExecutionId,
            outcome = "succeeded", occurredAtUtc = "2026-10-22T00:03:00Z"
        });
        checkpointRequest.eventContexts.Add(new GameEventHistoryContext
        {
            eventId = effectEventId, runId = "run.synthetic", invocationId = "invoke.2",
            stepExecutionId = "step.2", actionExecutionId = actionExecutionId,
            causeKind = "action_checkpoint", causeReferenceId = checkpointId
        });
        checkpointRequest.flowInvocations.Add(new GameFlowInvocationRecord
        {
            invocationId = "invoke.2", runId = "run.synthetic", parentInvocationId = "invoke.synthetic",
            callExecutionId = "call.synthetic", flowId = "subflow.synthetic", startedAtUtc = "2026-10-22T00:02:59Z"
        });
        checkpointRequest.stepExecutions.Add(new GameStepExecutionRecord
        {
            executionId = "step.2", invocationId = "invoke.2", stepId = "activity", visitOrdinal = 1,
            presentedAtUtc = "2026-10-22T00:02:59Z"
        });
        checkpointRequest.actionExecutions.Add(new GameActionExecutionRecord
        {
            actionExecutionId = actionExecutionId, runId = "run.synthetic", invocationId = "invoke.2",
            stepExecutionId = "step.2", capabilityId = "activity.exercise", status = "succeeded",
            startedAtUtc = "2026-10-22T00:03:00Z", endedAtUtc = "2026-10-22T00:03:01Z"
        });
        checkpointRequest.statChanges.Add(new GameStatChangeRecord
        {
            changeId = Guid.NewGuid().ToString("D"), eventId = effectEventId, ownerKind = "alterra",
            ownerId = initial.identity.alterraId, statId = "alterra.energy", beforeKnown = true,
            beforeValue = 40f, afterKnown = true, afterValue = 41f, appliedDelta = 1f
        });
        Require(repository.Commit(checkpointRequest).state == GamePersistenceStates.Committed,
            "confirmed action checkpoint did not atomically authorize its effect history");
        IList<GameHistoryEvent> nextPage = new GameHistoryReader(historyPath).ReadEvents(
            new GameHistoryQuery { afterSequence = page1[0].historySequence, pageSize = 2 });
        Require(nextPage.Count == 2 && nextPage[0].kind == "action_checkpoint_confirmed" &&
            nextPage[1].causeKind == "action_checkpoint" && nextPage[1].actionExecutionId == actionExecutionId,
            "keyset event paging or action-checkpoint causality did not round trip");

        string answerOperationId = Guid.NewGuid().ToString("D");
        string optionAnswerEventId = Guid.NewGuid().ToString("D");
        string freeTextAnswerEventId = Guid.NewGuid().ToString("D");
        GameCommitRequest answersRequest = Request(repository.Load().gameRevision, answerOperationId,
            "{\"schemaVersion\":4,\"answers\":true}", "accepted-answers");
        answersRequest.events.Add(new GameEventRecord { eventId = optionAnswerEventId, operationId = answerOperationId,
            kind = "scenario_answer_accepted", actorId = "player", targetId = "choice.yes", occurredAtUtc = "2026-10-22T00:07:00Z", payloadJson = "{}" });
        answersRequest.events.Add(new GameEventRecord { eventId = freeTextAnswerEventId, operationId = answerOperationId,
            kind = "scenario_answer_accepted", actorId = "player", targetId = "ask.free_text", occurredAtUtc = "2026-10-22T00:07:01Z", payloadJson = "{}" });
        answersRequest.eventContexts.Add(new GameEventHistoryContext { eventId = optionAnswerEventId, runId = "run.synthetic",
            invocationId = "invoke.2", stepExecutionId = "step.answer.1", causeKind = "source_operation", causeReferenceId = answerOperationId });
        answersRequest.eventContexts.Add(new GameEventHistoryContext { eventId = freeTextAnswerEventId, runId = "run.synthetic",
            invocationId = "invoke.2", stepExecutionId = "step.answer.2", causeKind = "source_operation", causeReferenceId = answerOperationId });
        answersRequest.stepExecutions.Add(new GameStepExecutionRecord { executionId = "step.answer.1", invocationId = "invoke.2",
            stepId = "ask.favor", visitOrdinal = 2, presentedAtUtc = "2026-10-22T00:06:59Z" });
        answersRequest.stepExecutions.Add(new GameStepExecutionRecord { executionId = "step.answer.2", invocationId = "invoke.2",
            stepId = "ask.note", visitOrdinal = 3, presentedAtUtc = "2026-10-22T00:06:59Z" });
        answersRequest.answers.Add(new GameAnswerRecord { answerId = Guid.NewGuid().ToString("D"), eventId = optionAnswerEventId,
            stepExecutionId = "step.answer.1", answerKind = "option", optionId = "choice.yes", submittedAtUtc = "2026-10-22T00:07:00Z" });
        answersRequest.answers.Add(new GameAnswerRecord { answerId = Guid.NewGuid().ToString("D"), eventId = freeTextAnswerEventId,
            stepExecutionId = "step.answer.2", answerKind = "free_text", submittedAtUtc = "2026-10-22T00:07:01Z" });
        Require(repository.Commit(answersRequest).state == GamePersistenceStates.Committed, "accepted answer history did not commit");
        IList<GameAnswerRecord> savedAnswers = new GameHistoryReader(historyPath).ReadAnswers(new GameHistoryQuery { pageSize = 8 });
        Require(savedAnswers.Count == 2 && savedAnswers[0].answerKind == "option" && savedAnswers[0].optionId == "choice.yes" &&
            savedAnswers[1].answerKind == "free_text" && savedAnswers[1].optionId == null,
            "answer history did not retain selected IDs while excluding free-text content");

        GameCommitRequest stale = Request(initial.gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"history\":true}", "stale-history");
        string staleEventId = Guid.NewGuid().ToString("D");
        stale.events.Add(new GameEventRecord { eventId = staleEventId, operationId = stale.operationId,
            kind = "simulation_checkpoint", actorId = "host", targetId = "test", occurredAtUtc = "2026-10-22T00:01:00Z", payloadJson = "{}" });
        stale.eventContexts.Add(new GameEventHistoryContext { eventId = staleEventId, causeKind = "simulation_checkpoint",
            simulationRuleId = "needs-decay", simulationRuleVersion = "2", intervalStartUtc = "2026-10-22T00:00:00Z",
            intervalEndUtc = "2026-10-22T00:01:00Z" });
        Require(repository.Commit(stale).state == GamePersistenceStates.StaleRevision, "stale history request was not rejected");
        Require(new GameHistoryReader(historyPath).ReadEvents(new GameHistoryQuery { pageSize = 8 }).Count == 5,
            "stale request leaked event history");

        GameCommitRequest invalid = Request(repository.Load().gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"history\":true}", "invalid-cause");
        string invalidEventId = Guid.NewGuid().ToString("D");
        invalid.events.Add(new GameEventRecord { eventId = invalidEventId, operationId = invalid.operationId,
            kind = "effect", actorId = "host", targetId = "x", occurredAtUtc = "2026-10-22T00:02:00Z", payloadJson = "{}" });
        invalid.eventContexts.Add(new GameEventHistoryContext { eventId = invalidEventId, causeKind = "prior_event", causeReferenceId = invalidEventId });
        invalid.statChanges.Add(new GameStatChangeRecord { changeId = Guid.NewGuid().ToString("D"), eventId = invalidEventId,
            ownerKind = "actor", ownerId = "model", statId = "alterra.energy", beforeKnown = true,
            beforeValue = 50f, afterKnown = true, afterValue = 52f, appliedDelta = 2f });
        Require(repository.Commit(invalid).state == GamePersistenceStates.Invalid, "invalid owner/self-causal cycle was accepted");
        Require(new GameHistoryReader(historyPath).ReadStatChanges(new GameHistoryQuery { pageSize = 8 }).Count == 3,
            "invalid history changed persisted stat history");

        GameCommitRequest invalidOwner = Request(repository.Load().gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"history\":true}", "invalid-owner");
        string invalidOwnerEventId = Guid.NewGuid().ToString("D");
        invalidOwner.events.Add(new GameEventRecord { eventId = invalidOwnerEventId, operationId = invalidOwner.operationId,
            kind = "effect", actorId = "host", targetId = "x", occurredAtUtc = "2026-10-22T00:04:00Z", payloadJson = "{}" });
        invalidOwner.eventContexts.Add(new GameEventHistoryContext
        { eventId = invalidOwnerEventId, causeKind = "source_operation", causeReferenceId = invalidOwner.operationId });
        invalidOwner.statChanges.Add(new GameStatChangeRecord { changeId = Guid.NewGuid().ToString("D"), eventId = invalidOwnerEventId,
            ownerKind = "actor", ownerId = "model", statId = "alterra.energy", beforeKnown = true,
            beforeValue = 50f, afterKnown = true, afterValue = 51f, appliedDelta = 1f });
        Require(repository.Commit(invalidOwner).state == GamePersistenceStates.Invalid, "unsupported stat owner kind was accepted");

        GameCommitRequest missingInterval = Request(repository.Load().gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"history\":true}", "missing-interval");
        string missingIntervalEventId = Guid.NewGuid().ToString("D");
        missingInterval.events.Add(new GameEventRecord { eventId = missingIntervalEventId, operationId = missingInterval.operationId,
            kind = "simulation_checkpoint", actorId = "host", targetId = "test", occurredAtUtc = "2026-10-22T00:05:00Z", payloadJson = "{}" });
        missingInterval.eventContexts.Add(new GameEventHistoryContext
        { eventId = missingIntervalEventId, causeKind = "simulation_checkpoint", simulationRuleId = "rule", simulationRuleVersion = "1" });
        Require(repository.Commit(missingInterval).state == GamePersistenceStates.Invalid, "simulation checkpoint without an interval was accepted");

        GameCommitRequest missingActionCheckpoint = Request(repository.Load().gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"history\":true}", "missing-action-checkpoint");
        string missingActionEventId = Guid.NewGuid().ToString("D");
        missingActionCheckpoint.events.Add(new GameEventRecord { eventId = missingActionEventId, operationId = missingActionCheckpoint.operationId,
            kind = "effect", actorId = "host", targetId = "test", occurredAtUtc = "2026-10-22T00:06:00Z", payloadJson = "{}" });
        missingActionCheckpoint.eventContexts.Add(new GameEventHistoryContext { eventId = missingActionEventId,
            actionExecutionId = "action.missing", causeKind = "action_checkpoint", causeReferenceId = "checkpoint.missing" });
        Require(repository.Commit(missingActionCheckpoint).state == GamePersistenceStates.Invalid,
            "action effect without a durable successful checkpoint was accepted");
        Require(new GameHistoryReader(historyPath).ReadEvents(new GameHistoryQuery { pageSize = 8 }).Count == 5 &&
            new GameHistoryReader(historyPath).ReadStatChanges(new GameHistoryQuery { pageSize = 8 }).Count == 3,
            "invalid owner/cause/checkpoint validation changed history");

        string schemaTwoPath = Path.Combine(directory, "schema-two upgrade.sqlite3");
        string schemaTwoBackup = Path.Combine(directory, "schema-two checkpoint.sqlite3");
        var schemaTwoRepository = new SqliteGameRepository(schemaTwoPath);
        GameLoadResult schemaTwoInitial = schemaTwoRepository.Load();
        string schemaTwoOperation = Guid.NewGuid().ToString("D");
        byte[] migrationBytes = Encoding.UTF8.GetBytes("synthetic schema-2 migration backup");
        string migrationHash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            migrationHash = BitConverter.ToString(sha.ComputeHash(migrationBytes)).Replace("-", string.Empty).ToLowerInvariant();
        GameCommitRequest schemaTwoSeed = Request(schemaTwoInitial.gameRevision, schemaTwoOperation,
            "{\"schemaVersion\":4,\"schemaTwo\":true}", "schema-two-seed");
        schemaTwoSeed.migrationSourceHash = migrationHash;
        schemaTwoSeed.migrationSourceBytes = migrationBytes;
        Require(schemaTwoRepository.Commit(schemaTwoSeed).state == GamePersistenceStates.Committed,
            "schema-2 upgrade fixture did not create its original operation and migration receipts");
        Require(schemaTwoRepository.CreateConsistentBackup(schemaTwoBackup), "schema-2 upgrade fixture backup failed");
        DemoteGameSchema3To2(schemaTwoBackup);
        Require(schemaTwoRepository.ValidateBackup(schemaTwoBackup).state == GamePersistenceStates.Ready,
            "a valid schema-2 game backup was rejected by the schema-3 reader");
        DemoteGameSchema3To2(schemaTwoPath);
        GameLoadResult schemaTwoMigrated = new SqliteGameRepository(schemaTwoPath).Load();
        GameCommitResult schemaTwoRetry = new SqliteGameRepository(schemaTwoPath).Commit(schemaTwoSeed);
        Require(schemaTwoMigrated.state == GamePersistenceStates.Ready,
            "schema-2-to-5 upgrade did not open: " + schemaTwoMigrated.state + "/" + schemaTwoMigrated.errorCode + " " + schemaTwoMigrated.message);
        Require(schemaTwoRetry.state == GamePersistenceStates.Duplicate,
            "schema-2-to-5 upgrade changed the existing operation receipt: " + schemaTwoRetry.state + "/" + schemaTwoRetry.errorCode + " " + schemaTwoRetry.message);
        Require(new SqliteGameRepository(schemaTwoPath).FindMigration(migrationHash) != null,
            "schema-2-to-4 upgrade lost the migration receipt");
        Require(BytesEqual(migrationBytes, ReadBytes(schemaTwoPath,
                "SELECT source_bytes FROM legacy_source_backups WHERE source_hash=?", migrationHash)),
            "schema-2-to-4 upgrade changed exact source backup bytes");
        using (var connection = new SQLiteConnection(schemaTwoPath, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5 &&
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='topic_history_imports'") == 1,
                "schema-2 migration did not atomically establish game schema 4");
    }

    private static void TopicHistorySqliteBehavior(string root)
    {
        string directory = Path.Combine(root, "topic history sqlite");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "alteros-game.sqlite3");
        string sourcePath = Path.Combine(directory, "alteros-topic-history-v2.json");
        long started = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long presented = started + TimeSpan.FromMinutes(1).Ticks;
        long completed = presented + TimeSpan.FromMinutes(2).Ticks;
        long cooldown = completed + TimeSpan.FromHours(1).Ticks;
        string fixture = "{\"schemaVersion\":2,\"records\":[{\"instanceId\":\"instance-α\",\"topicId\":\"topic.synthetic\",\"replayPolicy\":\"once\",\"startedUtcTicks\":" + started +
            ",\"presentedUtcTicks\":" + presented + ",\"completedUtcTicks\":" + completed + ",\"consumedUtcTicks\":" + completed +
            ",\"cooldownUntilUtcTicks\":" + cooldown + ",\"scenarioCompletions\":[{\"scenarioId\":\"scenario.synthetic\",\"completionCount\":2,\"lastCompletedUtcTicks\":" + completed +
            ",\"futureCompletionField\":\"preserve-child\"}],\"futureRecordField\":{\"label\":\"preserve-parent\"}}]}";
        byte[] sourceBytes = Encoding.UTF8.GetBytes(fixture);
        File.WriteAllBytes(sourcePath, sourceBytes);

        var store = new SqliteTopicHistoryStore(databasePath);
        TopicHistoryImportResult imported = TopicHistoryImporter.ImportOnce(sourcePath, store);
        Require(imported.State == "imported" && imported.SourceHash.Length == 64,
            "the complete v2 history source was not imported with a SHA-256 receipt");
        Require(File.ReadAllBytes(sourcePath).Length == sourceBytes.Length &&
            Encoding.UTF8.GetString(File.ReadAllBytes(sourcePath)) == fixture,
            "the legacy Topic source was edited or removed during import");
        Require(ReadBytes(databasePath, "SELECT source_bytes FROM legacy_source_backups WHERE source_hash=?", imported.SourceHash).Length == sourceBytes.Length,
            "the exact Topic source bytes were not backed up in SQL");
        Require(CountRows(databasePath, "topic_occurrences") == 1 && CountRows(databasePath, "topic_scenario_completions") == 1 &&
            CountRows(databasePath, "topic_history_imports") == 1,
            "normalized Topic occurrence, completion, or import receipt rows are missing");

        TopicHistoryStoreLoadResult loaded = new SqliteTopicHistoryStore(databasePath).Load();
        Require(loaded.Success && loaded.Revision == 1 && loaded.History.IsConsumed("instance-α") &&
            loaded.History.GetCompletionCount("instance-α", "scenario.synthetic") == 2 &&
            loaded.History.GetLastCompletedAt("instance-α", "scenario.synthetic").Value.UtcDateTime.Ticks == completed,
            "reopened SQL history changed consumption, scenario counters, or completion timestamps");
        string storedRecord = ReadText(databasePath, "SELECT record_json FROM topic_occurrences WHERE instance_id=?", "instance-α");
        string storedCompletion = ReadText(databasePath, "SELECT payload_json FROM topic_scenario_completions WHERE instance_id=? AND scenario_id=?", "instance-α", "scenario.synthetic");
        Require(storedRecord.Contains("futureRecordField") && storedRecord.Contains("preserve-parent") &&
            storedCompletion.Contains("futureCompletionField") && storedCompletion.Contains("preserve-child"),
            "unknown optional record or completion fields were discarded by the SQL import");

        TopicHistoryImportResult replayed = TopicHistoryImporter.ImportOnce(sourcePath, store);
        Require(replayed.State == "already_imported" && new SqliteTopicHistoryStore(databasePath).Load().Revision == 1,
            "receipt replay duplicated or reset Topic history");

        string operationId = Guid.NewGuid().ToString("D");
        TopicHistoryStoreSaveResult saved = store.Save(loaded.History, loaded.Revision, operationId);
        TopicHistoryStoreSaveResult duplicate = store.Save(loaded.History, loaded.Revision, operationId);
        Require(saved.Success && saved.State == "saved" && saved.Revision == 2 &&
            duplicate.Success && duplicate.State == "duplicate" && duplicate.Revision == 2,
            "SQL Topic save was not revisioned and idempotent by operation ID");
        TopicHistoryStoreSaveResult stale = store.Save(loaded.History, loaded.Revision, Guid.NewGuid().ToString("D"));
        Require(!stale.Success && stale.State == "conflict" && new SqliteTopicHistoryStore(databasePath).Load().Revision == 2,
            "a stale Topic history revision overwrote newer SQL state");
        TopicHistoryStoreSaveResult nextSave = store.Save(loaded.History, 2, Guid.NewGuid().ToString("D"));
        TopicHistoryStoreSaveResult oldReplay = store.Save(loaded.History, 1, operationId);
        Require(nextSave.Success && nextSave.Revision == 3 && !oldReplay.Success && oldReplay.State == "conflict" &&
            new SqliteTopicHistoryStore(databasePath).Load().Revision == 3,
            "replaying an old Topic operation after a newer save hid the newer revision");

        byte[] editedTopicSource = Encoding.UTF8.GetBytes("{broken-topic-source");
        File.WriteAllBytes(sourcePath, editedTopicSource);
        TopicHistoryImportResult edited = TopicHistoryImporter.ImportOnce(sourcePath, store);
        Require(edited.State == "already_imported" && BytesEqual(editedTopicSource, File.ReadAllBytes(sourcePath)) &&
            new SqliteTopicHistoryStore(databasePath).Load().Revision == 3 &&
            new SqliteTopicHistoryStore(databasePath).Load().History.GetCompletionCount("instance-α", "scenario.synthetic") == 2,
            "a post-cutover Topic source was reread or overwrote newer SQL history");
        File.Delete(sourcePath);
        Require(TopicHistoryImporter.ImportOnce(sourcePath, store).State == "already_imported",
            "removing a successfully imported legacy file caused SQL history to be reconstructed or treated as new");

        string v1DatabasePath = Path.Combine(directory, "v1.sqlite3");
        string v1SourcePath = Path.Combine(directory, "history-v1.json");
        File.WriteAllText(v1SourcePath, "{\"schemaVersion\":1,\"records\":[{\"instanceId\":\"repeatable-v1\",\"topicId\":\"topic.v1\",\"replayPolicy\":\"repeatable\",\"startedUtcTicks\":" + started +
            ",\"presentedUtcTicks\":0,\"completedUtcTicks\":0,\"consumedUtcTicks\":0,\"cooldownUntilUtcTicks\":0}]}", new UTF8Encoding(false));
        var v1Store = new SqliteTopicHistoryStore(v1DatabasePath);
        TopicHistoryImportResult v1Import = TopicHistoryImporter.ImportOnce(v1SourcePath, v1Store);
        Require(v1Import.State == "imported" && new SqliteTopicHistoryStore(v1DatabasePath).Load().History != null &&
            !new SqliteTopicHistoryStore(v1DatabasePath).Load().History.IsConsumed("repeatable-v1"),
            "a valid schema-1 Topic source did not retain repeatable semantics");

        string malformedPath = Path.Combine(directory, "malformed.json");
        string malformedBytes = "{\"schemaVersion\":99,\"records\":[";
        File.WriteAllText(malformedPath, malformedBytes, new UTF8Encoding(false));
        string malformedDatabasePath = Path.Combine(directory, "malformed.sqlite3");
        var malformedStore = new SqliteTopicHistoryStore(malformedDatabasePath);
        TopicHistoryImportResult malformed = TopicHistoryImporter.ImportOnce(malformedPath, malformedStore);
        Require(malformed.State == "recovery_required" && File.ReadAllText(malformedPath) == malformedBytes &&
            CountRows(malformedDatabasePath, "topic_occurrences") == 0 && CountRows(malformedDatabasePath, "topic_history_imports") == 0,
            "malformed or future Topic history was truncated, treated as empty, or partially imported");

        string conflictDatabasePath = Path.Combine(directory, "sql-without-import-receipt.sqlite3");
        var conflictStore = new SqliteTopicHistoryStore(conflictDatabasePath);
        TopicHistoryStoreLoadResult emptySql = conflictStore.Load();
        Require(conflictStore.Save(emptySql.History, emptySql.Revision, Guid.NewGuid().ToString("D")).Success,
            "could not seed a SQL-only Topic history conflict fixture");
        string conflictSourcePath = Path.Combine(directory, "conflicting-legacy.json");
        File.WriteAllBytes(conflictSourcePath, sourceBytes);
        Require(TopicHistoryImporter.ImportOnce(conflictSourcePath, conflictStore).State == "conflict" &&
            new SqliteTopicHistoryStore(conflictDatabasePath).Load().Revision == 1 && CountRows(conflictDatabasePath, "topic_occurrences") == 0,
            "legacy import overwrote SQL history that had no matching import receipt");

        string missingDatabasePath = Path.Combine(directory, "missing.sqlite3");
        var missingStore = new SqliteTopicHistoryStore(missingDatabasePath);
        string missingSourcePath = Path.Combine(directory, "not-present.json");
        TopicHistoryImportResult missing = TopicHistoryImporter.ImportOnce(missingSourcePath, missingStore);
        Require(missing.State == "missing" && new SqliteTopicHistoryStore(missingDatabasePath).Load().Revision == 0 &&
            CountRows(missingDatabasePath, "topic_history_imports") == 1,
            "a genuinely absent legacy Topic domain was not durably marked empty");
        File.WriteAllBytes(missingSourcePath, sourceBytes);
        Require(TopicHistoryImporter.ImportOnce(missingSourcePath, missingStore).State == "already_imported" &&
            CountRows(missingDatabasePath, "topic_occurrences") == 0,
            "a later stale Topic source was re-read after the empty-domain cutover receipt");
    }

    private static void TopicHistoryDatabaseRelocationBehavior(string root)
    {
        string directory = Path.Combine(root, "topic database relocation");
        Directory.CreateDirectory(directory);
        string legacyDatabasePath = Path.Combine(directory, "legacy-root-game.sqlite3");
        string canonicalDatabasePath = Path.Combine(directory, "Data", "alteros-game.sqlite3");
        string sourcePath = Path.Combine(directory, "alteros-topic-history-v2.json");
        File.WriteAllText(sourcePath, "{\"schemaVersion\":2,\"records\":[]}", new UTF8Encoding(false));
        var legacyStore = new SqliteTopicHistoryStore(legacyDatabasePath);
        TopicHistoryImportResult imported = TopicHistoryImporter.ImportOnce(sourcePath, legacyStore);
        Require(imported.State == "imported", "legacy root-level Topic database fixture did not import");
        TopicHistoryStoreLoadResult legacySnapshot = legacyStore.Load();
        TopicHistoryStoreSaveResult laterSave = legacyStore.Save(legacySnapshot.History, legacySnapshot.Revision, "legacy-topic-operation");
        Require(laterSave.Success && laterSave.Revision == 2, "legacy Topic SQL fixture did not advance beyond its source import");
        DemoteGameSchema4To3(legacyDatabasePath);
        byte[] legacyDatabaseBytes = File.ReadAllBytes(legacyDatabasePath);

        var canonicalStore = new SqliteTopicHistoryStore(canonicalDatabasePath);
        TopicHistoryImportResult canonicalImport = TopicHistoryImporter.ImportOnce(sourcePath, canonicalStore);
        Require(canonicalImport.State == "imported" && new SqliteTopicHistoryStore(canonicalDatabasePath).Load().Revision == 1,
            "canonical game fixture did not import the same JSON source before SQL relocation");

        TopicHistoryImportResult relocated = TopicHistoryDatabaseImporter.ImportOnce(legacyDatabasePath, canonicalStore);
        TopicHistoryStoreLoadResult reopened = new SqliteTopicHistoryStore(canonicalDatabasePath).Load();
        Require(relocated.State == "imported" && relocated.SourceHash == imported.SourceHash && reopened.Success &&
            reopened.Revision == 2 && CountRows(canonicalDatabasePath, "topic_history_operations") == 1 &&
            BytesEqual(legacyDatabaseBytes, File.ReadAllBytes(legacyDatabasePath)),
            "the prior root-level SQL receipt and newer Topic revision were not promoted into the shared game database");

        string unexpectedTablePath = Path.Combine(directory, "legacy-with-extra-table.sqlite3");
        var unexpectedTableStore = new SqliteTopicHistoryStore(unexpectedTablePath);
        Require(TopicHistoryImporter.ImportOnce(sourcePath, unexpectedTableStore).State == "imported",
            "could not seed the unknown-table relocation fixture");
        DemoteGameSchema4To3(unexpectedTablePath);
        using (var connection = new SQLiteConnection(unexpectedTablePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
        {
            connection.Execute("CREATE TABLE unrelated_profile_data (profile_id TEXT PRIMARY KEY, payload TEXT NOT NULL)");
            connection.Execute("INSERT INTO unrelated_profile_data(profile_id,payload) VALUES (?,?)", "profile-1", "preserve me");
        }
        string unexpectedTableTargetPath = Path.Combine(directory, "unexpected-table-target.sqlite3");
        TopicHistoryImportResult unexpectedTable = TopicHistoryDatabaseImporter.ImportOnce(
            unexpectedTablePath, new SqliteTopicHistoryStore(unexpectedTableTargetPath));
        Require(unexpectedTable.State == "recovery_required" &&
            new SqliteTopicHistoryStore(unexpectedTableTargetPath).Load().Revision == 0 &&
            CountRows(unexpectedTableTargetPath, "topic_history_imports") == 0,
            "an unknown user table was ignored during Topic-only database relocation");

        string mixedItemStatePath = Path.Combine(directory, "legacy-with-item-state.sqlite3");
        var mixedItemStore = new SqliteTopicHistoryStore(mixedItemStatePath);
        Require(TopicHistoryImporter.ImportOnce(sourcePath, mixedItemStore).State == "imported",
            "could not seed the mixed Item state relocation fixture");
        using (var connection = new SQLiteConnection(mixedItemStatePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
            connection.Execute("INSERT INTO player_item_quantities(player_id,item_id,quantity) VALUES (?,?,?)",
                "player.synthetic", "item.water_glass", 1);
        string mixedItemTargetPath = Path.Combine(directory, "mixed-item-target.sqlite3");
        TopicHistoryImportResult mixedItem = TopicHistoryDatabaseImporter.ImportOnce(
            mixedItemStatePath, new SqliteTopicHistoryStore(mixedItemTargetPath));
        Require(mixedItem.State == "recovery_required" &&
            CountRows(mixedItemTargetPath, "topic_history_imports") == 0,
            "a v4 game database containing Item state was accepted as a Topic-only relocation source");

        string unexpectedBackupPath = Path.Combine(directory, "legacy-with-extra-backup.sqlite3");
        var unexpectedBackupStore = new SqliteTopicHistoryStore(unexpectedBackupPath);
        Require(TopicHistoryImporter.ImportOnce(sourcePath, unexpectedBackupStore).State == "imported",
            "could not seed the unrelated-backup relocation fixture");
        DemoteGameSchema4To3(unexpectedBackupPath);
        using (var connection = new SQLiteConnection(unexpectedBackupPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
            connection.Execute("INSERT INTO legacy_source_backups(source_hash,source_bytes) VALUES (?,?)", new string('0', 64), Encoding.UTF8.GetBytes("unrelated backup"));
        string unexpectedBackupTargetPath = Path.Combine(directory, "unexpected-backup-target.sqlite3");
        TopicHistoryImportResult unexpectedBackup = TopicHistoryDatabaseImporter.ImportOnce(
            unexpectedBackupPath, new SqliteTopicHistoryStore(unexpectedBackupTargetPath));
        Require(unexpectedBackup.State == "recovery_required" &&
            new SqliteTopicHistoryStore(unexpectedBackupTargetPath).Load().Revision == 0 &&
            CountRows(unexpectedBackupTargetPath, "topic_history_imports") == 0,
            "an unrelated legacy source backup was ignored during Topic-only database relocation");
    }

    private static void ConversationSessionSqliteBehavior(string root)
    {
        string directory = Path.Combine(root, "conversation session sqlite");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "private conversations.sqlite3");
        string sourcePath = Path.Combine(directory, "alteros_conversation_sessions.json");
        const string fixture = @"{""sessions"":[
            {""sessionId"":""session-α"",""sessionKind"":""conversation"",""startedAt"":""2026-10-01T00:00:00Z"",""endedAt"":"""",""lastActiveAt"":""2026-10-01T00:05:00Z"",""title"":""Synthetic title"",""summary"":""Synthetic summary"",""isActive"":true,""routingState"":{""activeMode"":2,""activeGeneratedDialogueId"":""generated-42"",""activeIntentId"":""intent.synthetic"",""lastAssistantSource"":""prepared""},""turns"":[
                {""turnId"":""turn-1"",""sessionId"":""session-α"",""speaker"":""Player"",""text"":""hello"",""translatedText"":""こんにちは"",""emotion"":""calm"",""timestamp"":""2026-10-01T00:01:00Z"",""source"":4,""intentId"":""intent.ask"",""linkedGeneratedDialogueId"":""generated-42"",""linkedAmbientLineId"":""ambient-7""},
                {""turnId"":""turn-2"",""sessionId"":""session-α"",""speaker"":""Altera"",""text"":""welcome"",""translatedText"":""ようこそ"",""emotion"":""happy"",""timestamp"":""2026-10-01T00:02:00Z"",""source"":1,""intentId"":""intent.ask"",""linkedGeneratedDialogueId"":""generated-42"",""linkedAmbientLineId"":""""}]},
            {""sessionId"":""session-2"",""sessionKind"":""phone"",""startedAt"":""2026-10-02T00:00:00Z"",""endedAt"":"""",""lastActiveAt"":""2026-10-02T00:00:00Z"",""title"":"""",""summary"":"""",""isActive"":false,""routingState"":{""activeMode"":0,""activeGeneratedDialogueId"":"""",""activeIntentId"":"""",""lastAssistantSource"":""""},""turns"":[{""turnId"":""turn-3"",""sessionId"":""session-2"",""speaker"":""Altera"",""text"":""call end"",""translatedText"":"""",""emotion"":"""",""timestamp"":""2026-10-02T00:02:00Z"",""source"":2,""intentId"":"""",""linkedGeneratedDialogueId"":"""",""linkedAmbientLineId"":""""}]}],
            ""repetitions"":[{""key"":""ambient_line:ambient-7"",""recentCount"":4,""lastTriggeredAt"":""2026-10-01T00:03:00Z"",""stageUsed"":2}],
            ""lastAmbientLine"":{""lineId"":""ambient-7"",""topic"":""weather"",""text"":""Rain is easing."",""triggeredAt"":""2026-10-01T00:03:00Z"",""followUpConsumed"":true}}";        byte[] sourceBytes = Encoding.UTF8.GetBytes(fixture);
        File.WriteAllBytes(sourcePath, sourceBytes);

        var store = new SqliteConversationStore(databasePath);
        int privateSchemaVersion;
        using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
            privateSchemaVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
        string privateTables = ReadText(databasePath, "SELECT group_concat(name, ',') FROM sqlite_master WHERE type='table'");
        Require(string.IsNullOrEmpty(store.Error) && privateSchemaVersion == 2,
            "private conversation database did not initialize: " + store.Error + "; version=" + privateSchemaVersion + "; tables=" + privateTables);
        ConversationIdentityResult mappedIdentity = store.EnsureIdentity("AlterOS.Conversation.SessionId", "session-α");
        ConversationIdentityResult replayedIdentity = store.EnsureIdentity("AlterOS.Conversation.SessionId", "session-α");
        ConversationIdentityResult conflictingIdentity = store.EnsureIdentity("AlterOS.Conversation.SessionId", "different-session");
        Require(mappedIdentity.State == "imported" && replayedIdentity.State == "existing" &&
            conflictingIdentity.State == "conflict" &&
            ReadText(databasePath, "SELECT identity_value FROM identity_aliases WHERE identity_key=?", "AlterOS.Conversation.SessionId") == "session-α",
            "existing PlayerPrefs identity was not mapped once without allowing replacement");
        ConversationSessionImportResult imported = ConversationSessionImporter.ImportOnce(sourcePath, store);
        Require(imported.State == "imported" && imported.SourceHash.Length == 64 && imported.RecordCount == 2,
            "the complete legacy session and repetition snapshot did not import with a durable receipt: state=" + imported.State + ", error=" + imported.Error + ", records=" + imported.RecordCount);
        Require(BytesEqual(sourceBytes, ReadBytes(databasePath,
            "SELECT source_bytes FROM history_source_backups WHERE source_hash=?", imported.SourceHash)) &&
            BytesEqual(sourceBytes, File.ReadAllBytes(sourcePath)),
            "private migration evidence did not preserve the exact legacy session bytes");
        Require(CountRows(databasePath, "conversation_sessions") == 2 && CountRows(databasePath, "conversation_turns") == 3 &&
            CountRows(databasePath, "repetition_records") == 1 && CountRows(databasePath, "ambient_history") == 1 &&
            CountRows(databasePath, "history_imports") == 1,
            "private session, turn, repetition, ambient, or import rows were not normalized into SQL");

        ConversationStoreLoadResult reopened = new SqliteConversationStore(databasePath).Load();
        ConversationSessionRecord session = reopened.Database.sessions.Find(item => item.sessionId == "session-α");
        Require(reopened.Success && reopened.Revision == 1 && session != null && session.sessionKind == "conversation" &&
            session.startedAt == "2026-10-01T00:00:00Z" && session.lastActiveAt == "2026-10-01T00:05:00Z" &&
            session.title == "Synthetic title" && session.summary == "Synthetic summary" && session.isActive &&
            session.routingState.activeMode == ActiveConversationMode.Deep &&
            session.routingState.activeGeneratedDialogueId == "generated-42" && session.routingState.activeIntentId == "intent.synthetic" &&
            session.routingState.lastAssistantSource == "prepared" && session.turns.Count == 2 &&
            session.turns[0].turnId == "turn-1" && session.turns[0].source == ConversationTurnSource.Player &&
            session.turns[0].translatedText == "こんにちは" && session.turns[0].linkedAmbientLineId == "ambient-7" &&
            session.turns[1].linkedGeneratedDialogueId == "generated-42" &&
            reopened.Database.repetitions[0].recentCount == 4 && reopened.Database.repetitions[0].stageUsed == 2 &&
            reopened.Database.lastAmbientLine.lineId == "ambient-7" && reopened.Database.lastAmbientLine.followUpConsumed,
            "SQL reopen changed legacy session identity, routing, bilingual turns, links, repetition decay, or ambient state");

        string operationId = Guid.NewGuid().ToString("D");
        ConversationStoreSaveResult saved = store.SaveSnapshot(reopened.Database, reopened.Revision, operationId);
        ConversationStoreSaveResult duplicate = store.SaveSnapshot(reopened.Database, reopened.Revision, operationId);
        Require(saved.Success && saved.State == "saved" && saved.Revision == 2 &&
            duplicate.Success && duplicate.State == "duplicate" && duplicate.Revision == 2,
            "private session snapshot writes were not revisioned and idempotent");
        ConversationStoreSaveResult stale = store.SaveSnapshot(reopened.Database, reopened.Revision, Guid.NewGuid().ToString("D"));
        Require(!stale.Success && stale.State == "conflict" && new SqliteConversationStore(databasePath).Load().Revision == 2,
            "a stale private session snapshot overwrote newer SQL state");
        var appendedTurn = new ConversationTurnRecord
        {
            turnId = "turn-4", sessionId = "session-α", speaker = "Altera", text = "bounded third turn",
            translatedText = "", timestamp = "2026-10-01T00:06:00Z", source = ConversationTurnSource.LocalDialogue
        };
        ConversationStoreSaveResult append = store.AppendTurn(appendedTurn, 2, 2, "append-turn-4");
        ConversationStoreLoadResult capped = new SqliteConversationStore(databasePath).Load();
        ConversationSessionRecord cappedSession = capped.Database.sessions.Find(item => item.sessionId == "session-α");
        Require(append.Success && capped.Revision == 3 && cappedSession.turns.Count == 2 &&
            cappedSession.turns[0].turnId == "turn-2" && cappedSession.turns[1].turnId == "turn-4",
            "bounded SQL append did not keep the newest turns in stable order");
        cappedSession.title = "Updated in SQL";
        ConversationStoreSaveResult updatedSession = store.UpdateSession(cappedSession, capped.Revision, "update-session-alpha");
        ConversationStoreLoadResult afterSessionUpdate = new SqliteConversationStore(databasePath).Load();
        RepetitionMemoryRecord repetition = afterSessionUpdate.Database.repetitions[0];
        repetition.stageUsed = 3;
        ConversationStoreSaveResult updatedRepetition = store.UpsertRepetition(repetition, afterSessionUpdate.Revision, "update-repetition");
        ConversationStoreLoadResult afterRepetitionUpdate = new SqliteConversationStore(databasePath).Load();
        afterRepetitionUpdate.Database.lastAmbientLine.followUpConsumed = false;
        ConversationStoreSaveResult updatedAmbient = store.SetAmbient(afterRepetitionUpdate.Database.lastAmbientLine,
            afterRepetitionUpdate.Revision, "update-ambient-history");
        ConversationStoreLoadResult reopenedUpdates = new SqliteConversationStore(databasePath).Load();
        Require(updatedSession.Success && updatedRepetition.Success && updatedAmbient.Success &&
            reopenedUpdates.Database.sessions.Find(item => item.sessionId == "session-α").title == "Updated in SQL" &&
            reopenedUpdates.Database.repetitions[0].stageUsed == 3 && !reopenedUpdates.Database.lastAmbientLine.followUpConsumed,
            "session, repetition, or ambient updates did not commit through the revisioned SQL facade");
        ConversationStoreSaveResult oldSaveReplay = store.SaveSnapshot(reopened.Database, reopened.Revision, operationId);
        Require(!oldSaveReplay.Success && oldSaveReplay.State == "conflict" && oldSaveReplay.Revision == reopenedUpdates.Revision,
            "replaying an old session operation after newer updates hid the current revision");
        Require(ConversationSessionImporter.ImportOnce(sourcePath, store).State == "already_imported",
            "restarting the session importer duplicated imported turns or repetition counts");
        byte[] editedSessionSource = Encoding.UTF8.GetBytes("{broken-session-source");
        File.WriteAllBytes(sourcePath, editedSessionSource);
        ConversationSessionImportResult editedSessionImport = ConversationSessionImporter.ImportOnce(sourcePath, store);
        Require(editedSessionImport.State == "already_imported" && BytesEqual(editedSessionSource, File.ReadAllBytes(sourcePath)) &&
            new SqliteConversationStore(databasePath).Load().Revision == reopenedUpdates.Revision,
            "a post-cutover session source was reread or overwrote newer private SQL history");

        string corruptPath = Path.Combine(directory, "corrupt-sessions.json");
        string corruptText = "{\"sessions\":[";
        File.WriteAllText(corruptPath, corruptText, new UTF8Encoding(false));
        string corruptDb = Path.Combine(directory, "corrupt.sqlite3");
        var corruptStore = new SqliteConversationStore(corruptDb);
        ConversationSessionImportResult corrupt = ConversationSessionImporter.ImportOnce(corruptPath, corruptStore);
        Require(corrupt.State == "recovery_required" && File.ReadAllText(corruptPath) == corruptText &&
            CountRows(corruptDb, "conversation_sessions") == 0 && CountRows(corruptDb, "history_imports") == 0,
            "corrupt session JSON was reset, truncated, or partially imported");

        string futurePath = Path.Combine(directory, "future-sessions.json");
        string futureText = "{\"schemaVersion\":2,\"sessions\":[],\"repetitions\":[],\"lastAmbientLine\":{}}";
        File.WriteAllText(futurePath, futureText, new UTF8Encoding(false));
        string futureDb = Path.Combine(directory, "future.sqlite3");
        ConversationSessionImportResult future = ConversationSessionImporter.ImportOnce(futurePath,
            new SqliteConversationStore(futureDb));
        Require(future.State == "recovery_required" && File.ReadAllText(futurePath) == futureText &&
            CountRows(futureDb, "conversation_sessions") == 0 && CountRows(futureDb, "history_imports") == 0,
            "a future session schema was accepted as empty or altered during import");

        string missingDb = Path.Combine(directory, "missing.sqlite3");
        var missingStore = new SqliteConversationStore(missingDb);
        string missingSessionPath = Path.Combine(directory, "absent.json");
        Require(ConversationSessionImporter.ImportOnce(missingSessionPath, missingStore).State == "missing" &&
            new SqliteConversationStore(missingDb).Load().Revision == 0 && CountRows(missingDb, "history_imports", "domain='sessions'") == 1,
            "a genuinely missing legacy session source did not leave a durable empty-domain receipt");
        File.WriteAllText(missingSessionPath, fixture, new UTF8Encoding(false));
        Require(ConversationSessionImporter.ImportOnce(missingSessionPath, missingStore).State == "already_imported" &&
            CountRows(missingDb, "conversation_sessions") == 0,
            "a later stale session source was re-read after the empty-domain cutover receipt");
    }

    private static void TranscriptSqliteBehavior(string root)
    {
        string directory = Path.Combine(root, "transcript sqlite");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "private conversations.sqlite3");
        string sourcePath = Path.Combine(directory, "alteros-conversation-v2.jsonl");
        DateTimeOffset now = new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);
        var records = new List<ConversationTranscriptEvent>
        {
            ConversationTranscriptEvent.RequestReceived("session-α", "request-1", "event-request", "phone", "cats are lovely", now),
            ConversationTranscriptEvent.ResponseReceived("session-α", "request-1", "response-1", "event-response", "scenario",
                "{\"kind\":\"scenario\",\"reply\":{\"beats\":[]}}", now.AddSeconds(1)),
            ConversationTranscriptEvent.NodePresented("session-α", "response-1", "node-1", "event-presented", "authored", "phone",
                "topic.cats", "scenario.cats", "Altera", "猫が好きです", "I like cats", "happy", now.AddSeconds(2)),
            ConversationTranscriptEvent.TurnStatus("session-α", "request-1", "response-1", "event-pending", "pending", now.AddSeconds(3)),
            ConversationTranscriptEvent.TurnStatus("session-α", "request-1", "response-1", "event-late", "late", now.AddSeconds(4)),
            ConversationTranscriptEvent.TurnStatus("session-α", "request-1", "response-1", "event-cancelled", "cancelled", now.AddSeconds(5))
        };
        byte[] sourceBytes = SerializeTranscriptFixture(records);
        File.WriteAllBytes(sourcePath, sourceBytes);

        var store = new SqliteTranscriptStore(databasePath);
        ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(sourcePath, store);
        Require(imported.State == "imported" && imported.RecordCount == 6 && imported.SourceHash.Length == 64,
            "the full JSONL source did not import with its event identity and source hash");
        Require(CountRows(databasePath, "transcript_events") == 6 &&
            CountRows(databasePath, "history_imports", "domain='transcript'") == 1 &&
            BytesEqual(sourceBytes, ReadBytes(databasePath,
                "SELECT chunk_bytes FROM transcript_source_backup_chunks WHERE source_hash=? AND chunk_index=0", imported.SourceHash)) &&
            BytesEqual(sourceBytes, File.ReadAllBytes(sourcePath)),
            "transcript rows, exact source-byte backup or source-file retention failed");
        using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
            Require(connection.ExecuteScalar<long>("SELECT next_sequence FROM transcript_state WHERE singleton_id=1") == 7,
                "imported transcript cursor did not retain its next sequence");

        var reopened = new SqliteTranscriptStore(databasePath);
        ConversationTranscriptPage page = reopened.ReadPage("0", 10);
        ConversationTranscriptPage tail = reopened.ReadPage("4", 10);
        ConversationHistorySnapshot recent = reopened.ReadRecent(12, 6000);
        Require(page.Success && page.events.Count == 6 && page.events[0].sequence == 1 &&
            page.events[0].value.eventId == "event-request" && page.events[0].value.channel == "phone" &&
            page.events[0].value.timestampUtc == records[0].timestampUtc &&
            page.events[1].value.responseJson == records[1].responseJson &&
            page.events[2].value.text == "猫が好きです" && page.events[2].value.displayText == "I like cats" &&
            page.events[2].value.expression == "happy" && page.events[2].value.source == "authored" &&
            page.events[3].value.statusCode == "pending" && page.events[4].value.statusCode == "late" &&
            page.events[5].value.statusCode == "cancelled" && tail.events.Count == 2 && tail.events[0].sequence == 5 &&
            tail.nextCursor == "6" && recent.Success && recent.utterances.Count == 2 &&
            recent.utterances[0].sourceId == "event-request" && recent.utterances[1].sourceId == "event-presented",
            "SQL reopen changed transcript identity, order, metadata, status distinctions, paging, or recent context");

        PlayerMemoryTranscriptSyncBatch memoryBatch = PlayerMemoryTranscriptSync.BuildPage(page, "player-synthetic", true, 0);
        PlayerMemoryTranscriptSyncBatch deniedMemoryBatch = PlayerMemoryTranscriptSync.BuildPage(page, "player-synthetic", false, 0);
        var providerHistory = new List<ConversationPresentedUtteranceV2>
        {
            new ConversationPresentedUtteranceV2("Player", "player", "", "", "cats are lovely", "event-request"),
            new ConversationPresentedUtteranceV2("Altera", "authored", "response-1", "node-1", "猫が好きです", "event-presented"),
            new ConversationPresentedUtteranceV2("Altera", "authored", "response-2", "node-2", "no source id", "")
        };
        IReadOnlyList<ConversationPresentedUtteranceV2> unsuppressed = MemoryConversationHistoryFilter.Filter(
            providerHistory, new HashSet<string>(StringComparer.Ordinal), true);
        IReadOnlyList<ConversationPresentedUtteranceV2> afterForget = MemoryConversationHistoryFilter.Filter(
            providerHistory, new HashSet<string>(StringComparer.Ordinal) { "event-request" }, true);
        Require(memoryBatch.events.Count == 1 && memoryBatch.events[0].evidence.sourceId == "event-request" &&
            deniedMemoryBatch.events.Count == 0 && unsuppressed.Count == 2 && afterForget.Count == 1 &&
            afterForget[0].sourceId == "event-presented",
            "SQL transcript import changed consent-bound memory sync identity or replayed a suppressed source into context");

        var sessionStore = new SqliteConversationStore(databasePath);
        ConversationStoreLoadResult sessionSnapshot = sessionStore.Load();
        sessionSnapshot.Database.sessions.Add(new ConversationSessionRecord
        {
            sessionId = "interleaved-session", sessionKind = "conversation", startedAt = "2026-10-22T01:00:00Z",
            lastActiveAt = "2026-10-22T01:00:00Z", isActive = false, routingState = new SessionRoutingState()
        });
        ConversationStoreSaveResult sessionSaved = sessionStore.SaveSnapshot(sessionSnapshot.Database,
            sessionSnapshot.Revision, "interleaved-session-save");
        Require(sessionSaved.Success && sessionSaved.Revision == 1 &&
            new SqliteTranscriptStore(databasePath).ReadPage("0", 10).events.Count == 6,
            "session SQL writes collided with imported transcript sequence or rows");

        ConversationAppendResult duplicate = reopened.Append(ConversationTranscriptEvent.RequestReceived(
            "session-α", "request-1", "event-request", "phone", "cats are lovely", now.AddMinutes(1)));
        ConversationAppendResult conflict = reopened.Append(ConversationTranscriptEvent.RequestReceived(
            "session-α", "request-1", "event-request", "phone", "different payload", now));
        ConversationAppendResult appended = reopened.Append(ConversationTranscriptEvent.TurnStatus(
            "session-α", "request-2", "", "event-next", "failed", now.AddMinutes(2)));
        Require(duplicate.Status == ConversationAppendStatus.Duplicate && duplicate.sequence == 1 &&
            conflict.Status == ConversationAppendStatus.Unsaved && conflict.Error == "event_id_conflict" &&
            appended.Status == ConversationAppendStatus.Appended && appended.sequence == 7 &&
            new SqliteTranscriptStore(databasePath).ReadPage("6", 10).events[0].value.eventId == "event-next",
            "transcript append retry, changed-payload conflict, or cursor continuation was not atomic");

        string concurrentDb = Path.Combine(directory, "concurrent-appends.sqlite3");
        var concurrentStoreA = new SqliteTranscriptStore(concurrentDb);
        var concurrentStoreB = new SqliteTranscriptStore(concurrentDb);
        var startTogether = new ManualResetEventSlim(false);
        Task<ConversationAppendResult> appendA = Task.Run(() =>
        {
            startTogether.Wait();
            return concurrentStoreA.Append(ConversationTranscriptEvent.RequestReceived(
                "session-concurrent", "request-a", "event-concurrent-a", "direct", "one", now));
        });
        Task<ConversationAppendResult> appendB = Task.Run(() =>
        {
            startTogether.Wait();
            return concurrentStoreB.Append(ConversationTranscriptEvent.RequestReceived(
                "session-concurrent", "request-b", "event-concurrent-b", "direct", "two", now.AddSeconds(1)));
        });
        startTogether.Set();
        Task.WaitAll(appendA, appendB);
        ConversationTranscriptPage concurrentPage = new SqliteTranscriptStore(concurrentDb).ReadPage("0", 10);
        Require(appendA.Result.Status == ConversationAppendStatus.Appended && appendB.Result.Status == ConversationAppendStatus.Appended &&
            appendA.Result.sequence != appendB.Result.sequence && concurrentPage.events.Count == 2 &&
            concurrentPage.events[0].sequence == 1 && concurrentPage.events[1].sequence == 2,
            "concurrent transcript appends duplicated or skipped a sequence");
        Require(TranscriptJsonlImporter.ImportOnce(sourcePath, reopened).State == "already_imported" &&
            new SqliteTranscriptStore(databasePath).ReadPage("0", 10).events.Count == 7,
            "a restarted transcript import duplicated or reset newer SQL events");
        byte[] editedSource = Encoding.UTF8.GetBytes("{\"schemaVersion\":1");
        File.WriteAllBytes(sourcePath, editedSource);
        ConversationTranscriptImportResult editedImport = TranscriptJsonlImporter.ImportOnce(sourcePath, reopened);
        Require(editedImport.State == "already_imported" && CountRows(databasePath, "transcript_events") == 7 &&
            BytesEqual(editedSource, File.ReadAllBytes(sourcePath)),
            "a post-cutover transcript source was reread or overwrote imported/newer SQL events");
        File.Delete(sourcePath);
        Require(TranscriptJsonlImporter.ImportOnce(sourcePath, reopened).State == "already_imported" &&
            new SqliteTranscriptStore(databasePath).ReadPage("0", 10).events.Count == 7,
            "removing the imported JSONL source caused transcript SQL to be reconstructed or reset");

        string conflictDb = Path.Combine(directory, "unreceipted-events.sqlite3");
        var conflictStore = new SqliteTranscriptStore(conflictDb);
        Require(conflictStore.Append(records[0]).Status == ConversationAppendStatus.Appended,
            "could not create a SQL transcript without an import receipt");
        string conflictSource = Path.Combine(directory, "conflicting.jsonl");
        File.WriteAllBytes(conflictSource, sourceBytes);
        Require(TranscriptJsonlImporter.ImportOnce(conflictSource, conflictStore).State == "conflict" &&
            conflictStore.ReadPage("0", 10).events.Count == 1,
            "legacy transcript import overwrote SQL events with no matching import receipt");
        Require(new SqliteConversationStore(databasePath).Load().Revision == 1 &&
            new SqliteTranscriptStore(databasePath).ReadPage("6", 10).events[0].value.eventId == "event-next",
            "interleaved transcript append changed the session revision or lost its row");

        string partialPath = Path.Combine(directory, "partial.jsonl");
        string partialText = Encoding.UTF8.GetString(SerializeTranscriptFixture(new[] { records[0] })) + "{\"schemaVersion\":1";
        byte[] partialBytes = Encoding.UTF8.GetBytes(partialText);
        File.WriteAllBytes(partialPath, partialBytes);
        string partialDb = Path.Combine(directory, "partial.sqlite3");
        ConversationTranscriptImportResult partial = TranscriptJsonlImporter.ImportOnce(partialPath,
            new SqliteTranscriptStore(partialDb));
        Require(partial.State == "recovery_required" && BytesEqual(partialBytes, File.ReadAllBytes(partialPath)) &&
            CountRows(partialDb, "transcript_events") == 0 && CountRows(partialDb, "history_imports") == 0,
            "a truncated transcript tail was treated as an empty or valid-prefix import");

        string rollbackPath = Path.Combine(directory, "rollback.jsonl");
        File.WriteAllBytes(rollbackPath, sourceBytes);
        string rollbackDb = Path.Combine(directory, "rollback.sqlite3");
        new SqliteTranscriptStore(rollbackDb);
        using (var connection = new SQLiteConnection(rollbackDb))
            connection.Execute("CREATE TRIGGER fail_second_transcript_insert BEFORE INSERT ON transcript_events WHEN NEW.sequence=2 BEGIN SELECT RAISE(ABORT,'synthetic import failure'); END");
        ConversationTranscriptImportResult rollback = TranscriptJsonlImporter.ImportOnce(rollbackPath,
            new SqliteTranscriptStore(rollbackDb));
        Require(rollback.State == "recovery_required" && CountRows(rollbackDb, "transcript_events") == 0 &&
            CountRows(rollbackDb, "transcript_source_backup_chunks") == 0 && CountRows(rollbackDb, "history_imports") == 0 &&
            BytesEqual(sourceBytes, File.ReadAllBytes(rollbackPath)),
            "a failed mid-import transaction left transcript rows, receipt or source backup behind");

        string readOnlyDb = Path.Combine(directory, "readonly.sqlite3");
        var readOnlySeed = new SqliteTranscriptStore(readOnlyDb);
        Require(readOnlySeed.Append(records[0]).Status == ConversationAppendStatus.Appended,
            "could not seed a read-only transcript fixture");
        File.SetAttributes(readOnlyDb, File.GetAttributes(readOnlyDb) | FileAttributes.ReadOnly);
        ConversationAppendResult readOnlyAppend;
        try
        {
            readOnlyAppend = new SqliteTranscriptStore(readOnlyDb).Append(records[1]);
        }
        finally { File.SetAttributes(readOnlyDb, FileAttributes.Normal); }
        Require(readOnlyAppend.Status == ConversationAppendStatus.Unsaved &&
            new SqliteTranscriptStore(readOnlyDb).ReadPage("0", 10).events.Count == 1,
            "a read-only transcript database acknowledged an unsaved append or changed prior history");

        string readOnlyImportDb = Path.Combine(directory, "readonly-import.sqlite3");
        var readOnlyImportStore = new SqliteTranscriptStore(readOnlyImportDb);
        File.SetAttributes(readOnlyImportDb, File.GetAttributes(readOnlyImportDb) | FileAttributes.ReadOnly);
        ConversationTranscriptImportResult readOnlyImport;
        try { readOnlyImport = TranscriptJsonlImporter.ImportOnce(rollbackPath, readOnlyImportStore); }
        finally { File.SetAttributes(readOnlyImportDb, FileAttributes.Normal); }
        int readOnlyEvents = CountRows(readOnlyImportDb, "transcript_events");
        int readOnlyBackups = CountRows(readOnlyImportDb, "transcript_source_backup_chunks");
        int readOnlyReceipts = CountRows(readOnlyImportDb, "history_imports");
        ConversationTranscriptImportResult readOnlyRetry = TranscriptJsonlImporter.ImportOnce(rollbackPath,
            new SqliteTranscriptStore(readOnlyImportDb));
        Require(readOnlyImport.State == "recovery_required" && readOnlyEvents == 0 && readOnlyBackups == 0 &&
            readOnlyReceipts == 0 && readOnlyRetry.State == "imported",
            "read-only import state=" + readOnlyImport.State + " error=" + readOnlyImport.Error + " rows=" + readOnlyEvents +
            " backups=" + readOnlyBackups + " receipts=" + readOnlyReceipts + " retry=" + readOnlyRetry.State +
            " retryError=" + readOnlyRetry.Error);

        string changingSource = Path.Combine(directory, "changing-source.jsonl");
        byte[] initialSource = SerializeTranscriptFixture(new[] { records[0] });
        byte[] changedSource = SerializeTranscriptFixture(new[] { records[0], records[1] });
        File.WriteAllBytes(changingSource, initialSource);
        string changingDb = Path.Combine(directory, "changing-source.sqlite3");
        var changingStore = new SqliteTranscriptStore(changingDb);
        int sourceReads = 0;
        Func<string, Stream> changingReader = _ =>
        {
            sourceReads++;
            return new MemoryStream(sourceReads == 1 ? initialSource : changedSource, false);
        };
        ConversationTranscriptImportResult changingImport = ImportTranscriptWithReader(changingSource, changingStore, changingReader);
        Require(sourceReads == 2 && changingImport.State == "recovery_required" &&
            CountRows(changingDb, "transcript_events") == 0 &&
            CountRows(changingDb, "transcript_source_backup_chunks") == 0 && CountRows(changingDb, "history_imports") == 0 &&
            NextTranscriptSequence(changingDb) == 1 && BytesEqual(initialSource, File.ReadAllBytes(changingSource)),
            "a changed second-pass source committed mixed transcript rows, backup chunks, sequence, or receipt");
        Require(TranscriptJsonlImporter.ImportOnce(changingSource, changingStore).State == "imported" &&
            CountRows(changingDb, "transcript_events") == 1 && CountRows(changingDb, "history_imports") == 1,
            "the source snapshot conflict left a durable receipt or prevented a clean retry");

        string schemaOnePath = Path.Combine(directory, "conversation schema-one.sqlite3");
        var schemaOneSeed = new SqliteConversationStore(schemaOnePath);
        ConversationStoreLoadResult schemaOneState = schemaOneSeed.Load();
        schemaOneState.Database.sessions.Add(new ConversationSessionRecord
        { sessionId = "schema-one-session", sessionKind = "conversation", startedAt = "2026-10-22T00:00:00Z", isActive = false });
        Require(schemaOneSeed.SaveSnapshot(schemaOneState.Database, 0, "schema-one-session-save").Success,
            "could not seed a private schema-1 migration fixture");
        DemoteConversationSchema2To1(schemaOnePath);
        var upgradedPrivate = new SqliteConversationStore(schemaOnePath);
        ConversationStoreLoadResult upgradedPrivateSnapshot = upgradedPrivate.Load();
        Require(upgradedPrivateSnapshot.Success && upgradedPrivateSnapshot.Revision == 1 &&
            upgradedPrivateSnapshot.Database.sessions.Count == 1 &&
            new SqliteTranscriptStore(schemaOnePath).ReadPage("0", 10).Success &&
            new SqliteTranscriptStore(schemaOnePath).ReadPage("0", 10).events.Count == 0,
            "private schema-1-to-2 migration lost sessions or failed to initialize an empty transcript sequence");

        string emptyPath = Path.Combine(directory, "empty.jsonl");
        File.WriteAllBytes(emptyPath, new byte[0]);
        string emptyDb = Path.Combine(directory, "empty.sqlite3");
        ConversationTranscriptImportResult emptyImport = TranscriptJsonlImporter.ImportOnce(emptyPath,
            new SqliteTranscriptStore(emptyDb));
        Require(emptyImport.State == "imported" && emptyImport.RecordCount == 0 &&
            CountRows(emptyDb, "history_imports", "domain='transcript'") == 1 &&
            CountRows(emptyDb, "transcript_source_backup_chunks", "length(chunk_bytes)=0") == 1 &&
            new SqliteTranscriptStore(emptyDb).ReadPage("0", 10).events.Count == 0,
            "an existing empty JSONL source was confused with a missing transcript or lacked its exact backup receipt");

        string missingDb = Path.Combine(directory, "missing.sqlite3");
        var missingStore = new SqliteTranscriptStore(missingDb);
        string missingTranscriptPath = Path.Combine(directory, "missing.jsonl");
        Require(TranscriptJsonlImporter.ImportOnce(missingTranscriptPath, missingStore).State == "missing" &&
            missingStore.ReadPage("0", 10).Success && missingStore.ReadPage("0", 10).events.Count == 0 &&
            CountRows(missingDb, "history_imports", "domain='transcript'") == 1,
            "a genuinely missing transcript source was not distinguished and durably marked from an empty JSONL import");
        File.WriteAllBytes(missingTranscriptPath, sourceBytes);
        Require(TranscriptJsonlImporter.ImportOnce(missingTranscriptPath, missingStore).State == "already_imported" &&
            new SqliteTranscriptStore(missingDb).ReadPage("0", 10).events.Count == 0,
            "a later stale transcript source was re-read after the empty-domain cutover receipt");
    }

    private static ConversationTranscriptImportResult ImportTranscriptWithReader(string sourcePath,
        SqliteTranscriptStore store, Func<string, Stream> reader)
    {
        MethodInfo method = typeof(TranscriptJsonlImporter).GetMethod("ImportOnce",
            BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(string), typeof(SqliteTranscriptStore), typeof(Func<string, Stream>) }, null);
        Require(method != null, "the transcript importer lacks a deterministic source-reader seam for snapshot-change regression");
        return (ConversationTranscriptImportResult)method.Invoke(null, new object[] { sourcePath, store, reader });
    }

    private static long NextTranscriptSequence(string databasePath)
    {
        using (var connection = new SQLiteConnection(databasePath))
            return connection.ExecuteScalar<long>("SELECT next_sequence FROM transcript_state WHERE singleton_id=1");
    }

    private static void GeneratedArchiveSqliteBehavior(string root)
    {
        string directory = Path.Combine(root, "generated archive sqlite");
        string streamingRoot = Path.Combine(directory, "StreamingAssets");
        string archiveRoot = Path.Combine(streamingRoot, "Dialogue", "Generated", "archive");
        Directory.CreateDirectory(archiveRoot);
        byte[] validPayload = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"metadata\":{\"topic\":\"Synthetic Archive\",\"routing\":{\"routeId\":\"private.synthetic\"}},\"nodes\":[]}");
        string validPayloadPath = Path.Combine(archiveRoot, "ready.json");
        File.WriteAllBytes(validPayloadPath, validPayload);
        byte[] invalidEncodingPayload = new byte[] { 0xff, 0xfe, 0xfd };
        File.WriteAllBytes(Path.Combine(archiveRoot, "invalid.json"), invalidEncodingPayload);
        string outsidePath = Path.Combine(directory, "outside.json");
        File.WriteAllText(outsidePath, "{\"shouldNotRead\":true}", new UTF8Encoding(false));

        string manifestPath = Path.Combine(archiveRoot, "generated_dialogue_manifest.json");
        var fixture = new GeneratedArchiveFixtureDatabase
        {
            records = new List<GeneratedArchiveFixtureRecord>
            {
                new GeneratedArchiveFixtureRecord
                {
                    generatedDialogueId = "Legacy-Ready-α", originSessionId = "legacy-session-7", title = "Synthetic ready",
                    sourcePrompt = "Tell a story about a blue bird", summary = "A blue bird meets a river.",
                    createdAt = "2026-10-20T12:00:00Z", lastPlayedAt = "2026-10-21T12:00:00Z", status = 2,
                    wasPlayed = true, surfacedToPlayer = true, tags = new[] { "bird", "river" },
                    jsonPath = "Dialogue/Generated/archive/ready.json"
                },
                new GeneratedArchiveFixtureRecord
                {
                    generatedDialogueId = "legacy-missing", originSessionId = "legacy-session-missing", title = "Payload missing",
                    sourcePrompt = "A missing payload fixture", createdAt = "2026-10-19T00:00:00Z", status = 2,
                    tags = new[] { "missing" }, jsonPath = "Dialogue/Generated/archive/not-there.json"
                },
                new GeneratedArchiveFixtureRecord
                {
                    generatedDialogueId = "legacy-traversal", originSessionId = "legacy-session-traversal", title = "Unsafe locator",
                    sourcePrompt = "A traversal fixture", createdAt = "2026-10-18T00:00:00Z", status = 2,
                    tags = new[] { "unsafe" }, jsonPath = "../outside.json"
                },
                new GeneratedArchiveFixtureRecord
                {
                    generatedDialogueId = "legacy-invalid-utf8", originSessionId = "legacy-session-invalid", title = "Invalid payload",
                    sourcePrompt = "Invalid payload fixture", createdAt = "2026-10-17T00:00:00Z", status = 2,
                    tags = new[] { "invalid" }, jsonPath = "Dialogue/Generated/archive/invalid.json"
                }
            }
        };
        byte[] manifestBytes = SerializeGeneratedArchiveFixture(fixture);
        File.WriteAllBytes(manifestPath, manifestBytes);

        string databasePath = Path.Combine(directory, "Data", "personal-content.sqlite3");
        var store = new SqliteGeneratedArchiveStore(databasePath);
        GeneratedArchiveImportResult imported = GeneratedArchiveImporter.ImportOnce(manifestPath, streamingRoot, store);
        Require(imported.State == "imported" && imported.RecordCount == 4 && imported.PayloadCount == 2 &&
            imported.SourceHash.Length == 64 && BytesEqual(manifestBytes, File.ReadAllBytes(manifestPath)),
            "the synthetic generated archive did not migrate with its exact manifest source identity: state=" +
            imported.State + " error=" + imported.Error + " records=" + imported.RecordCount + " payloads=" + imported.PayloadCount +
            " hash=" + imported.SourceHash);

        List<GeneratedArchiveRecordData> rows = store.ReadRecords();
        GeneratedArchiveRecordData ready = rows.Find(item => item.generatedDialogueId == "Legacy-Ready-α");
        GeneratedArchiveRecordData missing = rows.Find(item => item.generatedDialogueId == "legacy-missing");
        GeneratedArchiveRecordData traversal = rows.Find(item => item.generatedDialogueId == "legacy-traversal");
        GeneratedArchiveRecordData invalid = rows.Find(item => item.generatedDialogueId == "legacy-invalid-utf8");
        Require(rows.Count == 4 && ready != null && ready.originSessionId == "legacy-session-7" &&
            ready.title == "Synthetic ready" && ready.sourcePrompt == "Tell a story about a blue bird" &&
            ready.summary == "A blue bird meets a river." && ready.createdAt == "2026-10-20T12:00:00Z" &&
            ready.lastPlayedAt == "2026-10-21T12:00:00Z" && ready.statusCode == 2 && ready.wasPlayed &&
            ready.surfacedToPlayer && ready.tagsJson.Contains("river") && ready.jsonPath == "Dialogue/Generated/archive/ready.json" &&
            missing != null && missing.payloadState == "missing" && traversal != null && traversal.payloadState == "unsafe_path" &&
            invalid != null && invalid.payloadState == "invalid_encoding",
            "generated archive metadata, linked session IDs, legacy aliases or unavailable payload diagnostics were lost");
        byte[] payloadCopy;
        string payloadState;
        Require(store.TryReadPayload("Legacy-Ready-α", out payloadCopy, out payloadState) &&
            payloadState == "unvalidated" && BytesEqual(validPayload, payloadCopy) &&
            store.TryReadPayload("legacy-invalid-utf8", out payloadCopy, out payloadState) &&
            payloadState == "invalid_encoding" && BytesEqual(invalidEncodingPayload, payloadCopy) &&
            ReadText(databasePath, "SELECT generated_dialogue_id FROM generated_archive_aliases WHERE legacy_id=?", "Legacy-Ready-α") == "Legacy-Ready-α" &&
            BytesEqual(manifestBytes, ReadBytes(databasePath,
                "SELECT chunk_bytes FROM generated_archive_manifest_backup_chunks WHERE source_hash=? AND chunk_index=0", imported.SourceHash)) &&
            BytesEqual(validPayload, ReadBytes(databasePath,
                "SELECT payload_bytes FROM generated_archive_import_payloads WHERE source_hash=? AND legacy_id=?", imported.SourceHash, "Legacy-Ready-α")),
            "a ready status authorized payload validation or exact legacy manifest/payload evidence was not retained");

        File.Delete(manifestPath);
        GeneratedArchiveImportResult replayed = GeneratedArchiveImporter.ImportOnce(manifestPath, streamingRoot, store);
        Require(replayed.State == "already_imported" && store.ReadRecords().Count == 4,
            "a committed SQL archive reread a deleted legacy manifest or duplicated the migration");

        var runtimeRecord = new GeneratedArchiveRecordData
        {
            generatedDialogueId = "runtime-ready-unvalidated", title = "Runtime draft", sourcePrompt = "new synthetic draft",
            createdAt = "2026-10-22T00:00:00Z", statusCode = 2, tagsJson = "[]", payloadState = "unvalidated"
        };
        byte[] runtimePayload = Encoding.UTF8.GetBytes("{\"metadata\":{\"topic\":\"Runtime draft\"},\"nodes\":[]}");
        Require(store.SaveRecord(runtimeRecord, runtimePayload) && store.TryReadPayload(runtimeRecord.generatedDialogueId,
            out payloadCopy, out payloadState) && payloadState == "unvalidated" && BytesEqual(runtimePayload, payloadCopy),
            "a new generated record did not persist through the private SQL authority");

        File.SetAttributes(databasePath, File.GetAttributes(databasePath) | FileAttributes.ReadOnly);
        bool readOnlyWrite;
        try
        {
            runtimeRecord.title = "must not publish";
            readOnlyWrite = store.SaveRecord(runtimeRecord, Encoding.UTF8.GetBytes("{\"changed\":true}"));
        }
        finally { File.SetAttributes(databasePath, FileAttributes.Normal); }
        GeneratedArchiveRecordData persisted = store.ReadRecords().Find(item => item.generatedDialogueId == runtimeRecord.generatedDialogueId);
        Require(!readOnlyWrite && persisted != null && persisted.title == "Runtime draft" &&
            store.TryReadPayload(runtimeRecord.generatedDialogueId, out payloadCopy, out payloadState) &&
            BytesEqual(runtimePayload, payloadCopy),
            "a failed SQL write was acknowledged or published mutated archive state in memory");

        string duplicateManifest = Path.Combine(archiveRoot, "duplicate.json");
        File.WriteAllBytes(duplicateManifest, SerializeGeneratedArchiveFixture(new GeneratedArchiveFixtureDatabase
        {
            records = new List<GeneratedArchiveFixtureRecord>
            {
                new GeneratedArchiveFixtureRecord { generatedDialogueId = "dup", status = 2, tags = new string[0] },
                new GeneratedArchiveFixtureRecord { generatedDialogueId = "DUP", status = 3, tags = new string[0] }
            }
        }));
        string duplicateDb = Path.Combine(directory, "duplicate.sqlite3");
        GeneratedArchiveImportResult duplicate = GeneratedArchiveImporter.ImportOnce(duplicateManifest, streamingRoot,
            new SqliteGeneratedArchiveStore(duplicateDb));
        Require(duplicate.State == "recovery_required" && CountRows(duplicateDb, "generated_archive_records") == 0 &&
            CountRows(duplicateDb, "generated_archive_imports") == 0,
            "case-folded duplicate legacy IDs partially imported or received a durable receipt");

        string rollbackManifest = Path.Combine(archiveRoot, "rollback-manifest.json");
        File.WriteAllBytes(rollbackManifest, manifestBytes);
        string rollbackDb = Path.Combine(directory, "rollback.sqlite3");
        var rollbackStore = new SqliteGeneratedArchiveStore(rollbackDb);
        using (var connection = new SQLiteConnection(rollbackDb))
            connection.Execute("CREATE TRIGGER fail_archive_record BEFORE INSERT ON generated_archive_records BEGIN SELECT RAISE(ABORT,'synthetic archive import failure'); END");
        GeneratedArchiveImportResult rollback = GeneratedArchiveImporter.ImportOnce(rollbackManifest, streamingRoot, rollbackStore);
        Require(rollback.State == "recovery_required" && CountRows(rollbackDb, "generated_archive_records") == 0 &&
            CountRows(rollbackDb, "generated_archive_aliases") == 0 && CountRows(rollbackDb, "generated_archive_imports") == 0 &&
            CountRows(rollbackDb, "generated_archive_manifest_backup_chunks") == 0 && CountRows(rollbackDb, "generated_archive_import_payloads") == 0 &&
            BytesEqual(manifestBytes, File.ReadAllBytes(rollbackManifest)),
            "a failed archive import committed metadata, aliases, source/payload backup, receipt, or changed its source");
        using (var connection = new SQLiteConnection(rollbackDb)) connection.Execute("DROP TRIGGER fail_archive_record");
        Require(GeneratedArchiveImporter.ImportOnce(rollbackManifest, streamingRoot, rollbackStore).State == "imported" &&
            CountRows(rollbackDb, "generated_archive_records") == 4 && CountRows(rollbackDb, "generated_archive_imports") == 1,
            "a failed archive import left a durable receipt or blocked a clean retry");
    }

    private static void RuntimeMigrationCoordinatorBehavior(string root)
    {
        string directory = Path.Combine(root, "runtime migration coordinator");
        string sources = Path.Combine(directory, "legacy");
        string data = Path.Combine(directory, "Data");
        Directory.CreateDirectory(sources);
        Directory.CreateDirectory(data);
        string contentPath = Path.Combine(directory, "content.sqlite3");
        string gamePath = Path.Combine(data, "alteros-game.sqlite3");
        string legacyTopicDatabasePath = Path.Combine(directory, "alteros-game.sqlite3");
        string conversationPath = Path.Combine(data, "conversations.sqlite3");
        string personalPath = Path.Combine(data, "personal-content.sqlite3");
        string topicSource = Path.Combine(sources, "topic-history.json");
        string sessionsSource = Path.Combine(sources, "sessions.json");
        string transcriptSource = Path.Combine(sources, "transcript.jsonl");
        string archiveSource = Path.Combine(sources, "generated_dialogue_manifest.json");
        string journalPath = Path.Combine(data, "runtime-migrations.sqlite3");
        File.WriteAllText(topicSource, "{\"schemaVersion\":2,\"records\":[]}", new UTF8Encoding(false));
        File.WriteAllText(sessionsSource, "{\"schemaVersion\":1,\"sessions\":[]}", new UTF8Encoding(false));
        File.WriteAllBytes(transcriptSource, new byte[0]);
        File.WriteAllText(archiveSource, "{\"records\":[]}", new UTF8Encoding(false));
        CreateVersionOneFixture(gamePath, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"legacy\":\"game-schema-one\"}", "coordinator-upgrade");
        int contentPrepareCalls = 0;
        int topicPrepareCalls = 0;
        int conversationDomainCalls = 0;

        var configuration = new RuntimeMigrationConfiguration { journalPath = journalPath };
        configuration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "content", targetPath = contentPath, targetIdentity = "release:synthetic-1", schemaVersion = 1,
            validateOnResume = true,
            prepare = () =>
            {
                contentPrepareCalls++;
                File.WriteAllBytes(contentPath, new byte[] { 1, 2, 3 });
                return new RuntimeMigrationDomainResult { state = "ready", targetIdentity = "release:synthetic-1", schemaVersion = 1, targetRevision = 1, receiptId = "release:synthetic-1", sourceHash = "content-hash" };
            }
        });
        configuration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "topic_history", targetPath = gamePath, targetIdentity = "game:synthetic-1", schemaVersion = 4,
            prepare = () =>
            {
                topicPrepareCalls++;
                if (topicPrepareCalls == 1)
                {
                    TopicHistoryImportResult legacyImport = TopicHistoryImporter.ImportOnce(topicSource, new SqliteTopicHistoryStore(legacyTopicDatabasePath));
                    if (legacyImport.State == "imported") DemoteGameSchema4To3(legacyTopicDatabasePath);
                }
                TopicHistoryImportResult relocated = TopicHistoryDatabaseImporter.ImportOnce(
                    legacyTopicDatabasePath, new SqliteTopicHistoryStore(gamePath));
                TopicHistoryImportResult imported = relocated.State == "missing" ?
                    TopicHistoryImporter.ImportOnce(topicSource, new SqliteTopicHistoryStore(gamePath)) : relocated;
                if (topicPrepareCalls == 1 && imported.State == "imported") throw new IOException("synthetic interruption after the owner committed");
                return new RuntimeMigrationDomainResult { state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "game:synthetic-1", schemaVersion = 4, targetRevision = imported.Revision, receiptId = imported.OperationId, error = imported.Error };
            }
        });
        configuration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "sessions", targetPath = conversationPath, targetIdentity = "conversations:synthetic-1", schemaVersion = 2,
            prepare = () =>
            {
                conversationDomainCalls++;
                ConversationSessionImportResult imported = ConversationSessionImporter.ImportOnce(sessionsSource, new SqliteConversationStore(conversationPath));
                return new RuntimeMigrationDomainResult { state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "conversations:synthetic-1", schemaVersion = 2, targetRevision = imported.Revision, receiptId = imported.OperationId, error = imported.Error };
            }
        });
        configuration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "transcript", targetPath = conversationPath, targetIdentity = "conversations:synthetic-1", schemaVersion = 2,
            prepare = () =>
            {
                conversationDomainCalls++;
                ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(transcriptSource, new SqliteTranscriptStore(conversationPath));
                return new RuntimeMigrationDomainResult { state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "conversations:synthetic-1", schemaVersion = 2, targetRevision = imported.Revision, receiptId = imported.OperationId, error = imported.Error };
            }
        });
        configuration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "generated_archive", targetPath = personalPath, targetIdentity = "personal-content:synthetic-1", schemaVersion = 1,
            prepare = () =>
            {
                GeneratedArchiveImportResult imported = GeneratedArchiveImporter.ImportOnce(archiveSource, directory, new SqliteGeneratedArchiveStore(personalPath));
                return new RuntimeMigrationDomainResult { state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "personal-content:synthetic-1", schemaVersion = 1, receiptId = imported.OperationId, error = imported.Error };
            }
        });
        configuration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "identity", targetPath = conversationPath, targetIdentity = "conversations:synthetic-1", schemaVersion = 2,
            prepare = () =>
            {
                ConversationIdentityResult identity = new SqliteConversationStore(conversationPath).EnsureIdentity("AlterOS.Conversation.SessionId", "session-synthetic");
                return new RuntimeMigrationDomainResult { state = identity.State, targetIdentity = "conversations:synthetic-1", schemaVersion = 2, receiptId = "identity:session-synthetic", error = identity.Error };
            }
        });

        RuntimeMigrationManifest interrupted = RuntimeMigrationCoordinator.Prepare(configuration);
        Require(!interrupted.isReady && interrupted.status == "recovery_required" &&
            interrupted.domains.Find(item => item.domain == "topic_history").status == "failed" &&
            interrupted.domains.Find(item => item.domain == "sessions").status == "committed" &&
            interrupted.domains.Find(item => item.domain == "transcript").status == "committed" &&
            interrupted.domains.Find(item => item.domain == "generated_archive").status == "committed",
            "a committed owner interruption did not leave independent migration domains resumable");

        byte[] legacyTopicDatabaseBytes = File.ReadAllBytes(legacyTopicDatabasePath);
        File.WriteAllText(topicSource, "{broken after committed receipt", new UTF8Encoding(false));
        File.WriteAllText(sessionsSource, "{broken after committed receipt", new UTF8Encoding(false));
        File.WriteAllText(transcriptSource, "{broken after committed receipt", new UTF8Encoding(false));
        File.WriteAllText(archiveSource, "{broken after committed receipt", new UTF8Encoding(false));
        RuntimeMigrationManifest resumed = RuntimeMigrationCoordinator.Resume(configuration);
        Require(resumed.isReady && resumed.status == "ready" && contentPrepareCalls == 2 && topicPrepareCalls == 2 && conversationDomainCalls == 2 &&
            BytesEqual(legacyTopicDatabaseBytes, File.ReadAllBytes(legacyTopicDatabasePath)) &&
            resumed.domains.Find(item => item.domain == "topic_history").status == "committed" &&
            resumed.domains.Find(item => item.domain == "identity").status == "committed",
            "restart did not reconcile durable import receipts without rereading edited legacy sources: ready=" + resumed.isReady +
            ", status=" + resumed.status + ", topicCalls=" + topicPrepareCalls + ", conversationCalls=" + conversationDomainCalls +
            ", topic=" + resumed.domains.Find(item => item.domain == "topic_history").status + "/" + resumed.domains.Find(item => item.domain == "topic_history").error +
            ", sessions=" + resumed.domains.Find(item => item.domain == "sessions").status + "/" + resumed.domains.Find(item => item.domain == "sessions").error +
            ", transcript=" + resumed.domains.Find(item => item.domain == "transcript").status + "/" + resumed.domains.Find(item => item.domain == "transcript").error +
            ", archive=" + resumed.domains.Find(item => item.domain == "generated_archive").status + "/" + resumed.domains.Find(item => item.domain == "generated_archive").error);
        int gameSchemaVersion;
        using (var connection = new SQLiteConnection(gamePath, SQLiteOpenFlags.ReadOnly))
            gameSchemaVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
        Require(RuntimeMigrationCoordinator.ReadStatus(journalPath).revision == resumed.revision && gameSchemaVersion == 5 &&
            CountRows(gamePath, "topic_history_imports") == 1 && CountRows(conversationPath, "history_imports") == 2 &&
            CountRows(personalPath, "generated_archive_imports") == 1,
            "the journal did not preserve committed per-domain receipts and targets");

        RuntimeMigrationDomainRequest topicDomain = configuration.domains[1];
        topicDomain.targetIdentity = "game:unexpected-replacement";
        int callsBeforeMismatch = topicPrepareCalls;
        RuntimeMigrationManifest mismatch = RuntimeMigrationCoordinator.Resume(configuration);
        Require(!mismatch.isReady && mismatch.domains.Find(item => item.domain == "topic_history").status == "recovery_required" &&
            topicPrepareCalls == callsBeforeMismatch,
            "a committed domain was silently rebound to a different target identity");
        topicDomain.targetIdentity = "game:synthetic-1";

        int conversationCallsBeforeLoss = conversationDomainCalls;
        File.Delete(conversationPath);
        RuntimeMigrationManifest lostTarget = RuntimeMigrationCoordinator.Resume(configuration);
        Require(!lostTarget.isReady &&
            lostTarget.domains.Find(item => item.domain == "sessions").status == "recovery_required" &&
            lostTarget.domains.Find(item => item.domain == "transcript").status == "recovery_required" &&
            lostTarget.domains.Find(item => item.domain == "identity").status == "recovery_required" &&
            conversationDomainCalls == conversationCallsBeforeLoss && !File.Exists(conversationPath),
            "a missing committed private target was recreated or legacy history was replayed");

        string sharedTarget = Path.Combine(directory, "shared-target.sqlite3");
        string sharedJournal = Path.Combine(directory, "shared-target-migrations.sqlite3");
        int incompleteTargetCalls = 0;
        var sharedConfiguration = new RuntimeMigrationConfiguration { journalPath = sharedJournal };
        sharedConfiguration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "committed_owner", targetPath = sharedTarget, targetIdentity = "shared:stable", schemaVersion = 1,
            prepare = () =>
            {
                File.WriteAllText(sharedTarget, "committed");
                return new RuntimeMigrationDomainResult { state = "committed", targetIdentity = "shared:stable", schemaVersion = 1, receiptId = "owner:receipt" };
            }
        });
        sharedConfiguration.domains.Add(new RuntimeMigrationDomainRequest
        {
            domain = "incomplete_owner", targetPath = sharedTarget, targetIdentity = "shared:stable", schemaVersion = 1,
            prepare = () =>
            {
                incompleteTargetCalls++;
                if (incompleteTargetCalls > 1) File.WriteAllText(sharedTarget, "must not be recreated");
                return new RuntimeMigrationDomainResult { state = "failed", targetIdentity = "shared:stable", schemaVersion = 1, error = "synthetic owner failure" };
            }
        });
        RuntimeMigrationCoordinator.Prepare(sharedConfiguration);
        File.Delete(sharedTarget);
        RuntimeMigrationManifest sharedLoss = RuntimeMigrationCoordinator.Resume(sharedConfiguration);
        Require(!sharedLoss.isReady && sharedLoss.domains.Find(item => item.domain == "committed_owner").status == "recovery_required" &&
            sharedLoss.domains.Find(item => item.domain == "incomplete_owner").status == "recovery_required" &&
            incompleteTargetCalls == 1 && !File.Exists(sharedTarget),
            "an incomplete domain recreated a shared SQL target after a sibling's committed target was lost");
    }

    private static byte[] SerializeGeneratedArchiveFixture(GeneratedArchiveFixtureDatabase fixture)
    {
        using (var output = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(GeneratedArchiveFixtureDatabase)).WriteObject(output, fixture);
            return output.ToArray();
        }
    }

    private static byte[] SerializeTranscriptFixture(IList<ConversationTranscriptEvent> events)
    {
        using (var output = new MemoryStream())
        {
            for (int index = 0; index < events.Count; index++)
            {
                ConversationTranscriptEvent value = events[index];
                var record = new TranscriptFixtureRecord
                {
                    schemaVersion = 1, sequence = index + 1, eventId = value.eventId, eventType = value.eventType,
                    sessionId = value.sessionId, requestId = value.requestId, responseId = value.responseId, nodeId = value.nodeId,
                    source = value.source, channel = value.channel, topicId = value.topicId, scenarioId = value.scenarioId,
                    judgmentKind = value.judgmentKind, speaker = value.speaker, text = value.text, displayText = value.displayText,
                    expression = value.expression, responseJson = value.responseJson, statusCode = value.statusCode, timestampUtc = value.timestampUtc
                };
                new DataContractJsonSerializer(typeof(TranscriptFixtureRecord)).WriteObject(output, record);
                output.WriteByte((byte)'\n');
            }
            return output.ToArray();
        }
    }

    [DataContract]
    private sealed class TranscriptFixtureRecord
    {
        [DataMember(Order = 1)] public int schemaVersion;
        [DataMember(Order = 2)] public long sequence;
        [DataMember(Order = 3)] public string eventId;
        [DataMember(Order = 4)] public string eventType;
        [DataMember(Order = 5)] public string sessionId;
        [DataMember(Order = 6)] public string requestId;
        [DataMember(Order = 7)] public string responseId;
        [DataMember(Order = 8)] public string nodeId;
        [DataMember(Order = 9)] public string source;
        [DataMember(Order = 10)] public string channel;
        [DataMember(Order = 11)] public string topicId;
        [DataMember(Order = 12)] public string scenarioId;
        [DataMember(Order = 13)] public string judgmentKind;
        [DataMember(Order = 14)] public string speaker;
        [DataMember(Order = 15)] public string text;
        [DataMember(Order = 16)] public string displayText;
        [DataMember(Order = 17)] public string expression;
        [DataMember(Order = 18)] public string responseJson;
        [DataMember(Order = 19)] public string statusCode;
        [DataMember(Order = 20)] public string timestampUtc;
    }

    private static void CreateVersionOneFixture(string path, string saveId, string alterraId, string homeId,
        string operationId, string eventId, string progression, string operationPayload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (File.Exists(path)) File.Delete(path);
        using (var connection = new SQLiteConnection(path))
        {
            connection.Execute("CREATE TABLE game_meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
            connection.Execute("CREATE TABLE game_snapshots (domain TEXT PRIMARY KEY NOT NULL, revision INTEGER NOT NULL, payload_json TEXT NOT NULL)");
            connection.Execute("CREATE TABLE home_layout_snapshots (home_id TEXT PRIMARY KEY NOT NULL, revision INTEGER NOT NULL, payload_json TEXT NOT NULL)");
            connection.Execute("CREATE TABLE game_events (event_id TEXT PRIMARY KEY NOT NULL, operation_id TEXT NOT NULL, kind TEXT NOT NULL, actor_id TEXT NOT NULL, target_id TEXT NOT NULL, occurred_at_utc TEXT NOT NULL, recorded_at_utc TEXT NOT NULL, schema_version INTEGER NOT NULL, payload_json TEXT NOT NULL)");
            connection.Execute("CREATE INDEX game_events_operation_id ON game_events(operation_id)");
            connection.Execute("CREATE TABLE operation_receipts (operation_id TEXT PRIMARY KEY NOT NULL, payload_hash TEXT NOT NULL, game_revision INTEGER NOT NULL, layout_revision INTEGER NOT NULL, result_state TEXT NOT NULL)");
            connection.Execute("CREATE TABLE migration_receipts (source_hash TEXT PRIMARY KEY NOT NULL, operation_id TEXT NOT NULL, imported_at_utc TEXT NOT NULL, game_revision INTEGER NOT NULL)");
            connection.Execute("CREATE TABLE legacy_source_backups (source_hash TEXT PRIMARY KEY NOT NULL, source_bytes BLOB NOT NULL)");
            connection.Execute("INSERT INTO game_meta(key,value) VALUES ('save_id',?),('alterra_id',?),('home_id',?),('account_id',''),('legacy_player_id',''),('game_revision','1'),('layout_revision','0')", saveId, alterraId, homeId);
            connection.Execute("INSERT INTO game_snapshots(domain,revision,payload_json) VALUES ('progression',1,?)", progression);
            connection.Execute("INSERT INTO game_events(event_id,operation_id,kind,actor_id,target_id,occurred_at_utc,recorded_at_utc,schema_version,payload_json) VALUES (?,?, 'legacy_event','host','fixture','2026-10-21T00:00:00Z','2026-10-21T00:00:01Z',1,'{\"legacy\":true}')", eventId, operationId);
            connection.Execute("INSERT INTO operation_receipts(operation_id,payload_hash,game_revision,layout_revision,result_state) VALUES (?,?,?,?,?)",
                operationId, LegacyRequestHash(operationPayload), 1, 0, GamePersistenceStates.Committed);
            connection.Execute("PRAGMA user_version=1");
        }
    }

    private static void DemoteGameSchema3To2(string path)
    {
        using (var connection = new SQLiteConnection(path))
        {
            // Current saves are schema 4. Remove every additive table newer than schema 2
            // so this fixture models an actual schema-2 backup instead of a mixed store.
            connection.Execute("DROP TABLE player_item_quantities");
            connection.Execute("DROP TABLE topic_scenario_completions");
            connection.Execute("DROP TABLE topic_occurrences");
            connection.Execute("DROP TABLE topic_history_imports");
            connection.Execute("DROP TABLE topic_history_operations");
            connection.Execute("DROP TABLE topic_history_state");
            RebuildLegacyFlowInvocations(connection);
            connection.Execute("PRAGMA user_version=2");
        }
    }

    private static void DemoteGameSchema4To3(string path)
    {
        using (var connection = new SQLiteConnection(path))
        {
            connection.Execute("DROP TABLE player_item_quantities");
            RebuildLegacyFlowInvocations(connection);
            connection.Execute("PRAGMA user_version=3");
        }
    }

    private static void RebuildLegacyFlowInvocations(SQLiteConnection connection)
    {
        int hasPinColumns = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM pragma_table_info('flow_invocations') WHERE name IN ('flow_store_id','flow_revision','flow_hash')");
        if (hasPinColumns == 0) return;
        connection.Execute("PRAGMA foreign_keys=OFF");
        connection.Execute("CREATE TABLE flow_invocations_demote AS SELECT invocation_id,run_id,parent_invocation_id,call_execution_id,flow_id,started_at_utc FROM flow_invocations");
        connection.Execute("DROP TABLE flow_invocations");
        connection.Execute("CREATE TABLE flow_invocations (invocation_id TEXT PRIMARY KEY NOT NULL, run_id TEXT NOT NULL REFERENCES scenario_runs(run_id), parent_invocation_id TEXT NULL REFERENCES flow_invocations(invocation_id), call_execution_id TEXT NULL, flow_id TEXT NOT NULL, started_at_utc TEXT NOT NULL)");
        connection.Execute("INSERT INTO flow_invocations(invocation_id,run_id,parent_invocation_id,call_execution_id,flow_id,started_at_utc) SELECT invocation_id,run_id,parent_invocation_id,call_execution_id,flow_id,started_at_utc FROM flow_invocations_demote");
        connection.Execute("DROP TABLE flow_invocations_demote");
        connection.Execute("CREATE INDEX flow_invocations_run_id ON flow_invocations(run_id)");
    }

    private static void DemoteConversationSchema2To1(string path)
    {
        using (var connection = new SQLiteConnection(path))
        {
            connection.Execute("DROP TABLE transcript_source_backup_chunks");
            connection.Execute("DROP TABLE transcript_events");
            connection.Execute("DROP TABLE transcript_state");
            connection.Execute("PRAGMA user_version=1");
        }
    }

    private static string LegacyRequestHash(string operationPayload)
    {
        var content = new StringBuilder();
        AppendHashPart(content, operationPayload);
        AppendHashPart(content, null);
        AppendHashPart(content, null);
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(content.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void AppendHashPart(StringBuilder content, string value)
    {
        if (value == null) content.Append("-1:");
        else content.Append(value.Length).Append(':').Append(value);
        content.Append('|');
    }

    private static void RepositoryBehavior(string root)
    {
        string path = Path.Combine(root, "repository", "synthetic game.sqlite3");
        var repository = new SqliteGameRepository(path);
        GameLoadResult first = repository.Load();
        Require(first.state == GamePersistenceStates.Ready,
            "fresh database load state=" + first.state + ", code=" + first.errorCode + ", message=" + first.message);
        Require(first.identity != null && Guid.TryParse(first.identity.saveId, out _), "fresh save ID was not created");
        Require(Guid.TryParse(first.identity.alterraId, out _) && Guid.TryParse(first.identity.homeId, out _), "identity IDs are not UUIDs");

        string json = "{\"schemaVersion\":4,\"altera\":{\"hydration\":0,\"hydrationKnown\":false},\"customStats\":[{\"key\":\"known.unknown\",\"value\":7}],\"extensionField\":\"preserved\"}";
        GameCommitRequest original = Request(first.gameRevision, Guid.NewGuid().ToString("D"), json, "snapshot:" + json);
        GameCommitResult committed = repository.Commit(original);
        Require(committed.state == GamePersistenceStates.Committed && committed.receipt.gameRevision == 1, "initial snapshot commit failed");
        Require(repository.Commit(original).state == GamePersistenceStates.Duplicate, "same operation was not idempotent");

        GameCommitRequest conflicting = Request(first.gameRevision, original.operationId,
            "{\"schemaVersion\":4,\"extensionField\":\"changed\"}", "snapshot:changed");
        Require(repository.Commit(conflicting).state == GamePersistenceStates.ConflictingOperation, "reused operation ID with changed payload was accepted");

        GameCommitRequest stale = Request(first.gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4}", "snapshot:stale");
        Require(repository.Commit(stale).state == GamePersistenceStates.StaleRevision, "stale revision was accepted");

        GameLoadResult reopened = new SqliteGameRepository(path).Load();
        Require(reopened.identity.saveId == first.identity.saveId && reopened.identity.alterraId == first.identity.alterraId &&
            reopened.identity.homeId == first.identity.homeId, "identity changed across reopen");
        Require(reopened.progressionJson == json && reopened.progressionJson.Contains("\"hydrationKnown\":false") &&
            reopened.progressionJson.Contains("\"extensionField\":\"preserved\""), "snapshot fields did not round trip");
    }

    private static void AtomicRollback(string root)
    {
        string path = Path.Combine(root, "rollback", "synthetic game.sqlite3");
        var repository = new SqliteGameRepository(path);
        GameLoadResult before = repository.Load();
        using (var connection = new SQLiteConnection(path))
            connection.Execute("CREATE TRIGGER synthetic_abort BEFORE INSERT ON operation_receipts BEGIN SELECT RAISE(ABORT,'synthetic rollback'); END");

        string operationId = Guid.NewGuid().ToString("D");
        var request = Request(before.gameRevision, operationId, "{\"schemaVersion\":4}", "checkpoint:synthetic");
        request.events.Add(new GameEventRecord
        {
            eventId = Guid.NewGuid().ToString("D"), operationId = operationId, kind = "synthetic_event",
            actorId = "host", targetId = "fixture", occurredAtUtc = "2026-10-21T00:00:00Z",
            payloadJson = "{\"fixture\":true}"
        });
        request.eventContexts.Add(new GameEventHistoryContext
        {
            eventId = request.events[0].eventId, causeKind = "source_operation", causeReferenceId = operationId
        });
        request.statChanges.Add(new GameStatChangeRecord
        {
            changeId = Guid.NewGuid().ToString("D"), eventId = request.events[0].eventId,
            ownerKind = "environment", ownerId = before.identity.homeId, statId = "environment.cleanliness",
            beforeKnown = true, beforeValue = 50f, afterKnown = true, afterValue = 49f, appliedDelta = -1f
        });
        Require(repository.Commit(request).state == GamePersistenceStates.Unavailable, "injected commit failure was not surfaced");
        GameLoadResult after = new SqliteGameRepository(path).Load();
        Require(after.gameRevision == before.gameRevision && after.progressionJson == null, "snapshot leaked through failed transaction");
        using (var connection = new SQLiteConnection(path))
        {
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM game_events") == 0, "event leaked through failed transaction");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM event_context") == 0, "event context leaked through failed transaction");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM stat_changes") == 0, "stat history leaked through failed transaction");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM operation_receipts") == 0, "receipt leaked through failed transaction");
        }
    }

    private static void InvalidDatabasePreservation(string root)
    {
        string futurePath = Path.Combine(root, "invalid", "future.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(futurePath));
        using (var connection = new SQLiteConnection(futurePath)) connection.Execute("PRAGMA user_version=999");
        byte[] futureBytes = File.ReadAllBytes(futurePath);
        Require(new SqliteGameRepository(futurePath).Load().state == GamePersistenceStates.RecoveryRequired, "future schema was accepted");
        Require(BytesEqual(futureBytes, File.ReadAllBytes(futurePath)), "future database bytes changed");

        string corruptPath = Path.Combine(root, "invalid", "corrupt.sqlite3");
        byte[] corruptBytes = { 0, 1, 2, 3, 4, 255, 0 };
        File.WriteAllBytes(corruptPath, corruptBytes);
        Require(new SqliteGameRepository(corruptPath).Load().state == GamePersistenceStates.RecoveryRequired, "corrupt database was accepted");
        Require(BytesEqual(corruptBytes, File.ReadAllBytes(corruptPath)), "corrupt database bytes changed");
    }

    private static void MigrationBehavior(string root)
    {
        string directory = Path.Combine(root, "migration 日本語");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "legacy progression with spaces.json");
        string databasePath = Path.Combine(directory, "new game.sqlite3");
        string backupRoot = Path.Combine(directory, "private backups");
        byte[] source = Encoding.UTF8.GetBytes("{\"schemaVersion\":4,\"player\":{\"credits\":77},\"altera\":{\"hydration\":0,\"hydrationKnown\":false},\"customStats\":[{\"target\":\"altera\",\"key\":\"custom.fixture\",\"value\":3}],\"unknownExtension\":\"保留\"}");
        File.WriteAllBytes(sourcePath, source);
        var repository = new SqliteGameRepository(databasePath);
        var importer = new ProgressionJsonImporter(new GameBackupService(repository, backupRoot));
        ProgressionImportResult first = importer.ImportOnce(sourcePath, repository);
        Require(first.state == GamePersistenceStates.Imported, "legacy import failed: " + first.errorCode + ": " + first.message);
        Require(BytesEqual(source, File.ReadAllBytes(sourcePath)), "legacy source file changed");
        Require(BytesEqual(source, File.ReadAllBytes(Path.Combine(backupRoot, "pre-migration-" + first.sourceHash + ".legacy.json"))),
            "legacy backup bytes changed");
        Require(repository.Load().progressionJson.Contains("\"credits\":77") &&
            repository.Load().progressionJson.Contains("\"hydrationKnown\":false"), "known schema-4 data was lost");
        long revision = repository.Load().gameRevision;
        Require(importer.ImportOnce(sourcePath, repository).state == GamePersistenceStates.AlreadyImported, "legacy source was imported twice");
        Require(repository.Load().gameRevision == revision, "retry changed the revision");

        string futurePath = Path.Combine(directory, "future.json");
        File.WriteAllText(futurePath, "{\"schemaVersion\":5}");
        Require(importer.ImportOnce(futurePath, repository).state == GamePersistenceStates.RecoveryRequired, "future progression schema was accepted");
    }

    private static void SchedulerBehavior()
    {
        int writes = 0;
        DateTime start = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        var scheduler = new GamePersistenceScheduler(() => new GameCommitResult
        {
            state = ++writes == 1 ? GamePersistenceStates.Unavailable : GamePersistenceStates.Committed
        }, () => start);
        scheduler.MarkDirty();
        Require(!scheduler.Flush() && scheduler.IsDirty && scheduler.Status == "unavailable", "failed flush cleared dirty state");
        Require(scheduler.Flush() && !scheduler.IsDirty, "retry did not commit dirty state");

        scheduler.MarkDirty();
        Require(!scheduler.Tick(start.AddSeconds(4)), "checkpoint ran before its five-second interval");
        Require(scheduler.Tick(start.AddSeconds(5)), "checkpoint did not run at five seconds");
        Require(writes == 3, "clean scheduler wrote unexpectedly");
    }

    private static void FurnitureBehavior(string root)
    {
        string path = Path.Combine(root, "furniture", "synthetic home.sqlite3");
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        GameCommitResult initialized = repository.Commit(Request(initial.gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4}", "new-game"));
        Require(initialized.state == GamePersistenceStates.Committed,
            "fixture game init failed: " + initialized.state + "/" + initialized.errorCode + " " + initialized.message);
        string homeId = repository.Load().identity.homeId;
        int seedCalls = 0;
        var adapter = new FurniturePersistenceAdapter(repository, id =>
        {
            seedCalls++;
            return Layout(id, "seed-v1", new FurnitureVector3 { x = 1.23456789, y = 0.125, z = -4.5 });
        });
        FurniturePersistenceLoadResult seeded = adapter.LoadHome(homeId);
        Require(seeded.state == "loaded" && seeded.layoutRevision == 1 && seedCalls == 1, "initial home seed failed");

        HomeFurnitureLayoutDocument candidate = seeded.document;
        candidate.instances[0].positionMeters.x = 3.14159265;
        candidate.instances[0].rotation = new FurnitureQuaternion { x = 0, y = 0.382683432365, z = 0, w = 0.923879532511 };
        candidate.instances[0].support.supportId = "missing.support.preserved";
        FurniturePersistenceCommitResult commit = adapter.CommitLayout(Guid.NewGuid().ToString("D"),
            seeded.gameRevision, seeded.layoutRevision, candidate);
        Require(commit.state == GamePersistenceStates.Committed && commit.layoutRevision == 2, "layout commit failed");

        int newSeedCalls = 0;
        var reopened = new FurniturePersistenceAdapter(new SqliteGameRepository(path), id =>
        {
            newSeedCalls++;
            return Layout(id, "seed-v2", new FurnitureVector3 { x = -99, y = 0, z = 0 });
        });
        FurniturePersistenceLoadResult saved = reopened.LoadHome(homeId);
        Require(saved.state == "loaded" && newSeedCalls == 0, "newer seed replaced saved layout");
        Require(saved.document.seedVersion == "seed-v1" && saved.document.instances[0].definitionId == "missing.definition.preserved" &&
            saved.document.instances[0].support.supportId == "missing.support.preserved", "orphan records were not preserved");
        Require(Math.Abs(saved.document.instances[0].positionMeters.x - 3.14159265) < 0.000000001 &&
            Math.Abs(saved.document.instances[0].rotation.w - 0.923879532511) < 0.000000000001, "exact pose did not round trip");
    }

    private static void FurniturePowerBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-power", "synthetic home.sqlite3");
        SqliteGameRepository repository = CreateInitializedGame(path);
        GameLoadResult flowSeed = repository.Load();
        var flowHistory = new GameCommitRequest
        {
            operationId = Guid.NewGuid().ToString("D"), operationPayload = "furniture-power-flow-context",
            expectedGameRevision = flowSeed.gameRevision, progressionJson = flowSeed.progressionJson
        };
        flowHistory.scenarioRuns.Add(new GameScenarioRunRecord
        { runId = "run.synthetic", scenarioId = "scenario.synthetic", startedAtUtc = "2026-10-23T00:00:00Z" });
        flowHistory.flowInvocations.Add(new GameFlowInvocationRecord
        { invocationId = "invocation.synthetic", runId = "run.synthetic", flowId = "flow.synthetic",
            flowStoreId = "store.synthetic", flowRevision = new string('a', 64), flowHash = new string('b', 64),
            startedAtUtc = "2026-10-23T00:00:00Z" });
        flowHistory.stepExecutions.Add(new GameStepExecutionRecord
        { executionId = "flow-step.power", invocationId = "invocation.synthetic", stepId = "effect", visitOrdinal = 1,
            presentedAtUtc = "2026-10-23T00:00:01Z" });
        flowHistory.stepExecutions.Add(new GameStepExecutionRecord
        { executionId = "flow-step.delivery", invocationId = "invocation.synthetic", stepId = "delivery", visitOrdinal = 1,
            presentedAtUtc = "2026-10-23T00:00:02Z", outcome = "completed", completedAtUtc = "2026-10-23T00:00:03Z" });
        Require(repository.Commit(flowHistory).state == GamePersistenceStates.Committed, "Furniture Flow execution fixture failed");
        MethodInfo hashRequest = typeof(SqliteGameRepository).GetMethod("ComputeRequestHash",
            BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(GameCommitRequest), typeof(bool), typeof(bool) }, null);
        string legacyTypedHash = (string)hashRequest.Invoke(null, new object[] { flowHistory, true, false });
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite))
            connection.Execute("UPDATE operation_receipts SET payload_hash=? WHERE operation_id=?", legacyTypedHash, flowHistory.operationId);
        Require(repository.Commit(flowHistory).state == GamePersistenceStates.Duplicate,
            "an exact historical v2 typed-history hash did not replay when its stored Flow pin matched");
        GameFlowInvocationRecord pinnedInvocation = flowHistory.flowInvocations[0];
        string pinnedStore = pinnedInvocation.flowStoreId, pinnedRevision = pinnedInvocation.flowRevision, pinnedHash = pinnedInvocation.flowHash;
        foreach (Action changePin in new Action[]
        {
            () => pinnedInvocation.flowStoreId = "store.changed",
            () => pinnedInvocation.flowRevision = new string('c', 64),
            () => pinnedInvocation.flowHash = new string('d', 64)
        })
        {
            changePin();
            Require(repository.Commit(flowHistory).state == GamePersistenceStates.ConflictingOperation,
                "a v2 hash compatibility retry accepted a changed supplied Flow pin");
            pinnedInvocation.flowStoreId = pinnedStore; pinnedInvocation.flowRevision = pinnedRevision; pinnedInvocation.flowHash = pinnedHash;
        }
        pinnedInvocation.flowStoreId = string.Empty;
        pinnedInvocation.flowRevision = string.Empty;
        pinnedInvocation.flowHash = string.Empty;
        Require(repository.Commit(flowHistory).state == GamePersistenceStates.ConflictingOperation,
            "clearing all Flow pin fields bypassed the durable-pin check through the current-hash path");
        pinnedInvocation.flowStoreId = pinnedStore; pinnedInvocation.flowRevision = pinnedRevision; pinnedInvocation.flowHash = pinnedHash;
        Require(repository.Commit(flowHistory).state == GamePersistenceStates.Duplicate,
            "the original pinned invocation stopped replaying after a rejected clear-all-pins retry");

        GameLoadResult beforeUnknownPinHistory = repository.Load();
        var unknownPinHistory = new GameCommitRequest
        {
            operationId = Guid.NewGuid().ToString("D"), operationPayload = "legacy-unknown-flow-pin-history",
            expectedGameRevision = beforeUnknownPinHistory.gameRevision, progressionJson = beforeUnknownPinHistory.progressionJson
        };
        unknownPinHistory.scenarioRuns.Add(new GameScenarioRunRecord
        { runId = "run.legacy-unpinned", scenarioId = "scenario.legacy", startedAtUtc = "2026-10-23T00:01:00Z" });
        unknownPinHistory.flowInvocations.Add(new GameFlowInvocationRecord
        { invocationId = "invocation.legacy-unpinned", runId = "run.legacy-unpinned", flowId = "flow.legacy", startedAtUtc = "2026-10-23T00:01:01Z" });
        unknownPinHistory.stepExecutions.Add(new GameStepExecutionRecord
        { executionId = "step.legacy-unpinned", invocationId = "invocation.legacy-unpinned", stepId = "legacy-step",
            visitOrdinal = 1, presentedAtUtc = "2026-10-23T00:01:02Z" });
        Require(repository.Commit(unknownPinHistory).state == GamePersistenceStates.Committed &&
            repository.Commit(unknownPinHistory).state == GamePersistenceStates.Duplicate,
            "a genuinely unpinned legacy invocation lost exact typed-history replay compatibility");
        GameScenarioHostEffectEvidenceResult flowEvidence = repository.ReadScenarioHostEffectEvidence("run.synthetic",
            "invocation.synthetic", "flow-step.power", "flow-step.delivery");
        Require(flowEvidence.state == "found" && flowEvidence.flowId == "flow.synthetic" &&
            flowEvidence.flowStoreId == "store.synthetic" && flowEvidence.flowRevision == new string('a', 64) &&
            flowEvidence.flowHash == new string('b', 64) && flowEvidence.effectStepExecutionId == "flow-step.power" &&
            flowEvidence.completedStepExecutionId == "flow-step.delivery" &&
            flowEvidence.effectStepId == "effect" && flowEvidence.completedStepId == "delivery" &&
            flowEvidence.completedStepOutcome == "completed" &&
            repository.ReadScenarioHostEffectEvidence("wrong.run", "invocation.synthetic", "flow-step.power", "flow-step.delivery").state == "missing",
            "durable Flow execution evidence did not require one correlated effect and a completed prerequisite Step");
        GameLoadResult beforeOtherRun = repository.Load();
        var otherRun = new GameCommitRequest
        {
            operationId = Guid.NewGuid().ToString("D"), operationPayload = "furniture-cross-run-fixture",
            expectedGameRevision = beforeOtherRun.gameRevision, progressionJson = beforeOtherRun.progressionJson
        };
        otherRun.scenarioRuns.Add(new GameScenarioRunRecord
        { runId = "run.other", scenarioId = "scenario.synthetic", startedAtUtc = "2026-10-23T00:00:04Z" });
        Require(repository.Commit(otherRun).state == GamePersistenceStates.Committed,
            "the crossed-run fixture could not be persisted");
        string homeId = repository.Load().identity.homeId;
        var layoutStore = new FurniturePersistenceAdapter(repository, id => Layout(id, "seed-v1", new FurnitureVector3()));
        FurniturePersistenceLoadResult seeded = layoutStore.LoadHome(homeId);
        Require(seeded.state == "loaded" && seeded.document.instances.Single().resourceState.All(item => item.resourceId != "device.power"),
            "the Furniture power fixture home did not load with absent power still unknown");
        var power = new FurniturePowerPersistenceAdapter(repository, layoutStore);
        var command = new FurniturePowerCommand
        {
            operationId = Guid.NewGuid().ToString("D"), runId = "run.synthetic", invocationId = "invocation.synthetic",
            stepExecutionId = "flow-step.power", activityId = "activity.locate", sequenceStepId = "sequence.power", instanceId = seeded.document.instances[0].instanceId,
            definitionId = seeded.document.instances[0].definitionId, usePointId = "screen", useId = "interact",
            usePointRevision = 3, reservationOwnerId = "activity.locate", expectedInstanceRevision = seeded.document.instances[0].instanceRevision,
            expectedLayoutRevision = seeded.layoutRevision, expectedGameRevision = seeded.gameRevision, desiredState = FurniturePowerState.On
        };
        var crossedTuple = new FurniturePowerCommand
        {
            operationId = Guid.NewGuid().ToString("D"), runId = "run.other", invocationId = command.invocationId,
            stepExecutionId = command.stepExecutionId, activityId = command.activityId, sequenceStepId = command.sequenceStepId,
            instanceId = command.instanceId, definitionId = command.definitionId, usePointId = command.usePointId,
            useId = command.useId, usePointRevision = command.usePointRevision, reservationOwnerId = command.reservationOwnerId,
            expectedInstanceRevision = command.expectedInstanceRevision, expectedLayoutRevision = command.expectedLayoutRevision,
            expectedGameRevision = command.expectedGameRevision, desiredState = command.desiredState
        };
        long beforeCrossedTupleEvents = CountRows(path, "game_events", "kind='furniture_power_changed'");
        FurnitureFunctionResult crossedResult = power.SetPower(crossedTuple);
        Require(crossedResult.status != FurnitureFunctionStatuses.Applied && repository.FindReceipt(crossedTuple.operationId) == null &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == beforeCrossedTupleEvents &&
            repository.Load().gameRevision == command.expectedGameRevision,
            "a cross-run invocation/step tuple changed Furniture state or wrote an event/receipt");
        FurnitureFunctionResult first = power.SetPower(command);
        Require(first.status == FurnitureFunctionStatuses.Applied && first.committedState != null && first.committedState.powerKnown &&
            first.committedState.powerState == FurniturePowerState.On && first.committedState.instanceRevision == command.expectedInstanceRevision + 1,
            "the Furniture power command did not commit explicit known On state");
        GameLoadResult saved = repository.Load();
        FurniturePersistenceLoadResult reopened = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(homeId);
        FurnitureResourceState savedPower = reopened.document.instances.Single().resourceState.Single(item => item.resourceId == "device.power");
        Require(savedPower.known && savedPower.value == 1d && reopened.document.schemaVersion == seeded.document.schemaVersion &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "Furniture power was not stored as device.power known=1 in the shared layout/event transaction");
        GameFurniturePowerOperationResult durableOperation = repository.FindFurniturePowerOperation(command.operationId);
        Require(durableOperation.IsFound && durableOperation.stepExecutionId == command.stepExecutionId &&
            durableOperation.eventContext != null && durableOperation.eventContext.runId == command.runId &&
            durableOperation.eventContext.invocationId == command.invocationId && durableOperation.eventContext.stepExecutionId == command.stepExecutionId &&
            durableOperation.eventContext.causeKind == "source_operation" && durableOperation.eventContext.causeReferenceId == command.operationId,
            "the Furniture event did not round-trip its distinct Flow Step execution context");
        FurnitureFunctionResult replay = power.SetPower(command);
        Require(replay.status == FurnitureFunctionStatuses.Replayed && replay.committedState.layoutRevision == command.expectedLayoutRevision + 1 &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "exact Furniture power replay did not return its stored receipt without another write");
        FurniturePowerCommand changedUse = new FurniturePowerCommand
        {
            operationId = command.operationId, runId = command.runId, invocationId = command.invocationId,
            stepExecutionId = command.stepExecutionId, activityId = command.activityId, sequenceStepId = command.sequenceStepId, instanceId = command.instanceId,
            definitionId = command.definitionId, usePointId = command.usePointId, useId = "different-use",
            usePointRevision = command.usePointRevision, reservationOwnerId = command.reservationOwnerId,
            expectedInstanceRevision = command.expectedInstanceRevision, expectedLayoutRevision = command.expectedLayoutRevision,
            expectedGameRevision = command.expectedGameRevision, desiredState = command.desiredState
        };
        Require(power.SetPower(changedUse).status == FurnitureFunctionStatuses.Conflict &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "reusing a power operation ID with a changed useId did not fail closed");

        FurniturePersistenceLoadResult beforeFailureLayout = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(homeId);
        string failingOperation = Guid.NewGuid().ToString("D");
        var failing = new FurniturePowerCommand
        {
            operationId = failingOperation, runId = command.runId, invocationId = command.invocationId,
            stepExecutionId = command.stepExecutionId, activityId = "activity.locate", sequenceStepId = "sequence.power", instanceId = command.instanceId,
            definitionId = command.definitionId, usePointId = command.usePointId, useId = command.useId,
            usePointRevision = command.usePointRevision, reservationOwnerId = command.reservationOwnerId,
            expectedInstanceRevision = beforeFailureLayout.document.instances[0].instanceRevision,
            expectedLayoutRevision = beforeFailureLayout.layoutRevision, expectedGameRevision = beforeFailureLayout.gameRevision,
            desiredState = FurniturePowerState.Off
        };
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite))
            connection.Execute("CREATE TRIGGER reject_furniture_power BEFORE INSERT ON game_events WHEN NEW.kind='furniture_power_changed' BEGIN SELECT RAISE(ABORT,'synthetic Furniture power event failure'); END");
        FurnitureFunctionResult failed = power.SetPower(failing);
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite)) connection.Execute("DROP TRIGGER reject_furniture_power");
        FurniturePersistenceLoadResult afterFailureLayout = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(homeId);
        Require(failed.status == FurnitureFunctionStatuses.Unavailable &&
            afterFailureLayout.document.instances[0].resourceState.Single(item => item.resourceId == "device.power").value == 1d &&
            afterFailureLayout.layoutRevision == beforeFailureLayout.layoutRevision && repository.FindReceipt(failingOperation) == null &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "a failed Furniture power event partially changed the resource, layout, or receipt");
        Require(saved.state == GamePersistenceStates.Ready, "the committed power operation left the game store unavailable");

        GameLoadResult beforeNoChange = repository.Load();
        var noChange = new FurniturePowerCommand
        {
            operationId = Guid.NewGuid().ToString("D"), runId = command.runId, invocationId = command.invocationId,
            stepExecutionId = command.stepExecutionId, activityId = command.activityId, sequenceStepId = command.sequenceStepId,
            instanceId = command.instanceId, definitionId = command.definitionId, usePointId = command.usePointId,
            useId = command.useId, usePointRevision = command.usePointRevision, reservationOwnerId = command.reservationOwnerId,
            expectedInstanceRevision = afterFailureLayout.document.instances[0].instanceRevision,
            expectedLayoutRevision = afterFailureLayout.layoutRevision, expectedGameRevision = beforeNoChange.gameRevision,
            desiredState = FurniturePowerState.On
        };
        FurnitureFunctionResult noChangeResult = power.SetPower(noChange);
        Require(noChangeResult.status == FurnitureFunctionStatuses.NoChange &&
            repository.FindReceipt(noChange.operationId) != null &&
            repository.FindFurniturePowerOperation(noChange.operationId).state == "no_event" &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "an already-On request did not save a receipt without creating a power-change event");
        var restartedPower = new FurniturePowerPersistenceAdapter(new SqliteGameRepository(path),
            new FurniturePersistenceAdapter(new SqliteGameRepository(path)));
        FurnitureFunctionResult noChangeReplay = restartedPower.SetPower(noChange);
        Require(noChangeReplay.status == FurnitureFunctionStatuses.Replayed &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "an exact no-change retry after restart did not replay its receipt without an event");
        var changedNoChange = new FurniturePowerCommand
        {
            operationId = noChange.operationId, runId = noChange.runId, invocationId = noChange.invocationId,
            stepExecutionId = noChange.stepExecutionId, activityId = noChange.activityId, sequenceStepId = noChange.sequenceStepId,
            instanceId = noChange.instanceId, definitionId = noChange.definitionId, usePointId = noChange.usePointId,
            useId = noChange.useId, usePointRevision = noChange.usePointRevision, reservationOwnerId = noChange.reservationOwnerId,
            expectedInstanceRevision = noChange.expectedInstanceRevision, expectedLayoutRevision = noChange.expectedLayoutRevision,
            expectedGameRevision = noChange.expectedGameRevision, desiredState = FurniturePowerState.Off
        };
        Require(restartedPower.SetPower(changedNoChange).status == FurnitureFunctionStatuses.Conflict &&
            CountRows(path, "game_events", "kind='furniture_power_changed'") == 1,
            "a changed no-change payload reused an operation ID after restart");
        string eventId;
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite))
        {
            eventId = connection.ExecuteScalar<string>("SELECT event_id FROM game_events WHERE operation_id=? AND kind='furniture_power_changed'", command.operationId);
            connection.Execute("UPDATE event_context SET run_id='run.other' WHERE event_id=?", eventId);
            connection.Execute("UPDATE game_events SET payload_json=REPLACE(payload_json,'\"runId\":\"run.synthetic\"','\"runId\":\"run.other\"') WHERE event_id=?", eventId);
        }
        Require(repository.FindFurniturePowerOperation(command.operationId).state == "invalid",
            "a self-consistent Furniture event context with crossed ancestry was accepted");
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite))
        {
            connection.Execute("UPDATE event_context SET run_id='run.synthetic' WHERE event_id=?", eventId);
            connection.Execute("UPDATE game_events SET payload_json=REPLACE(payload_json,'\"runId\":\"run.other\"','\"runId\":\"run.synthetic\"') WHERE event_id=?", eventId);
            connection.Execute("UPDATE event_context SET step_execution_id=? WHERE event_id=?", "flow-step.delivery", eventId);
        }
        Require(repository.FindFurniturePowerOperation(command.operationId).state == "invalid",
            "a Furniture event with a mismatched typed Flow Step context was accepted");
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite))
        {
            connection.Execute("UPDATE event_context SET step_execution_id=? WHERE event_id=?", command.stepExecutionId, eventId);
            connection.Execute("DELETE FROM event_context WHERE event_id=?", eventId);
        }
        Require(repository.FindFurniturePowerOperation(command.operationId).state == "invalid",
            "a Furniture event missing its typed Flow Step context was accepted");
    }

    private static void FurnitureSchemaUpgradeBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-schema-upgrade", "synthetic home.sqlite3");
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        Require(repository.Commit(Request(initial.gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4}", "new-game")).state == GamePersistenceStates.Committed, "fixture game init failed");
        GameLoadResult current = repository.Load();
        HomeFurnitureLayoutDocument legacy = Layout(current.identity.homeId, "legacy-seed", new FurnitureVector3 { x = 1.23456789, y = 0.125, z = -4.5 });
        legacy.layoutRevision = checked((int)current.layoutRevision + 1);
        Require(repository.Commit(new GameCommitRequest
        {
            operationId = Guid.NewGuid().ToString("D"), operationPayload = "legacy-layout",
            expectedGameRevision = current.gameRevision, expectedLayoutRevision = current.layoutRevision,
            progressionJson = current.progressionJson, layoutJson = SerializeLayout(legacy)
        }).state == GamePersistenceStates.Committed, "schema-one fixture layout commit failed");

        int seedCalls = 0;
        var adapter = new FurniturePersistenceAdapter(repository, id =>
        {
            seedCalls++;
            return Layout(id, "replacement-seed", new FurnitureVector3());
        });
        FurniturePersistenceLoadResult upgraded = adapter.LoadHome(current.identity.homeId);
        Require(upgraded.state == "loaded" && upgraded.document.schemaVersion == 2, "schema-one home was not durably upgraded");
        Require(upgraded.document.inventory != null && upgraded.document.inventory.Count == 8 &&
            upgraded.document.inventory.All(entry => entry != null && entry.storedCount == 10), "initial furniture stock was not seeded exactly");
        Require(upgraded.layoutRevision == 2 && seedCalls == 0, "upgrade did not advance once or called the new-home seed provider");
        Require(upgraded.document.instances.Count == 1 && upgraded.document.instances[0].instanceId == "instance.synthetic" &&
            upgraded.document.instances[0].definitionId == "missing.definition.preserved" &&
            upgraded.document.instances[0].support.supportId == "room.synthetic.floor" &&
            Math.Abs(upgraded.document.instances[0].positionMeters.x - 1.23456789) < 0.000000001,
            "schema upgrade changed the existing placed furniture record");
        Require(CountRows(path, "game_events", "kind='home_inventory_initialized'") == 1,
            "inventory initialization event was not committed with the home update");

        long gameRevision = upgraded.gameRevision;
        FurniturePersistenceLoadResult reopened = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(current.identity.homeId);
        Require(reopened.state == "loaded" && reopened.layoutRevision == upgraded.layoutRevision && reopened.gameRevision == gameRevision &&
            reopened.document.inventory.Count == 8 && reopened.document.inventory.All(entry => entry.storedCount == 10) &&
            CountRows(path, "game_events", "kind='home_inventory_initialized'") == 1,
            "reopen repeated inventory initialization or changed the committed home");
    }

    private static void FurnitureEventsBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-events", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        string homeId = repository.Load().identity.homeId;
        var adapter = new FurniturePersistenceAdapter(repository, id =>
        {
            HomeFurnitureLayoutDocument seed = Layout(id, "seed-v1", new FurnitureVector3());
            seed.instances[0].definitionId = "furniture.seat.basic";
            return seed;
        });
        FurniturePersistenceLoadResult seeded = adapter.LoadHome(homeId);
        Require(seeded.state == "loaded" && seeded.document.schemaVersion == 2, "new home seed did not create schema two");

        HomeFurnitureLayoutDocument placedCandidate = CloneLayout(seeded.document);
        FurnitureInventoryEntry seats = InventoryEntry(placedCandidate, "furniture.seat.basic");
        seats.storedCount--;
        HomeFurnitureLayoutInstance placedPiece = CloneInstance(placedCandidate.instances[0]);
        placedPiece.instanceId = "furniture.instance.placed";
        placedPiece.definitionId = "furniture.seat.basic";
        placedPiece.positionMeters.x = 2;
        placedCandidate.instances.Add(placedPiece);
        FurniturePersistenceCommitResult placed = adapter.CommitLayout(Guid.NewGuid().ToString("D"), seeded.gameRevision,
            seeded.layoutRevision, placedCandidate);
        Require(placed.state == GamePersistenceStates.Committed && placed.document.inventory.Any(entry => entry.definitionId == "furniture.seat.basic" && entry.storedCount == 9),
            "placing furniture did not consume stock with the layout");
        Require(CountRows(path, "game_events", "kind='furniture_placed'") == 1 &&
            EventTarget(path, "furniture_placed") == placedPiece.instanceId, "placement event was not causal");

        HomeFurnitureLayoutDocument movedCandidate = CloneLayout(placed.document);
        HomeFurnitureLayoutInstance movedPiece = movedCandidate.instances.Single(instance => instance.instanceId == placedPiece.instanceId);
        movedPiece.positionMeters.x = 3;
        FurniturePersistenceCommitResult moved = adapter.CommitLayout(Guid.NewGuid().ToString("D"), placed.gameRevision,
            placed.layoutRevision, movedCandidate);
        Require(moved.state == GamePersistenceStates.Committed && CountRows(path, "game_events", "kind='furniture_moved'") == 1 &&
            EventTarget(path, "furniture_moved") == placedPiece.instanceId, "move event was not causal");

        HomeFurnitureLayoutDocument returnedCandidate = CloneLayout(moved.document);
        returnedCandidate.instances.RemoveAll(instance => instance.instanceId == placedPiece.instanceId);
        InventoryEntry(returnedCandidate, "furniture.seat.basic").storedCount++;
        FurniturePersistenceCommitResult returned = adapter.CommitLayout(Guid.NewGuid().ToString("D"), moved.gameRevision,
            moved.layoutRevision, returnedCandidate);
        Require(returned.state == GamePersistenceStates.Committed && returned.document.instances.All(instance => instance.instanceId != placedPiece.instanceId) &&
            returned.document.inventory.Any(entry => entry.definitionId == "furniture.seat.basic" && entry.storedCount == 10),
            "return did not atomically exchange the placed piece for one inventory unit");
        Require(CountRows(path, "game_events", "kind='furniture_returned'") == 1 &&
            EventTarget(path, "furniture_returned") == placedPiece.instanceId, "return event was not causal");

        var backups = new GameBackupService(repository, Path.Combine(root, "furniture-events", "backups"),
            () => new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc));
        GameBackupResult backup = backups.CreateBackup("furniture-inventory");
        Require(backup.state == "created", "schema-two home checkpoint was not created");
        FurniturePersistenceLoadResult restored = new FurniturePersistenceAdapter(new SqliteGameRepository(backup.path)).LoadHome(homeId);
        Require(restored.state == "loaded" && restored.document.schemaVersion == 2 && restored.document.inventory.Any(entry =>
            entry.definitionId == "furniture.seat.basic" && entry.storedCount == 10), "game backup omitted confirmed inventory state");
    }

    private static void FurnitureDuplicateReplayBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-duplicate", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        string homeId = repository.Load().identity.homeId;
        var adapter = new FurniturePersistenceAdapter(repository, id => Layout(id, "seed-v1", new FurnitureVector3()));
        FurniturePersistenceLoadResult before = adapter.LoadHome(homeId);
        HomeFurnitureLayoutDocument firstCandidate = CloneLayout(before.document);
        firstCandidate.instances[0].positionMeters.x = 2;
        string firstOperationId = Guid.NewGuid().ToString("D");
        FurniturePersistenceCommitResult first = adapter.CommitLayout(firstOperationId, before.gameRevision,
            before.layoutRevision, firstCandidate);
        Require(first.state == GamePersistenceStates.Committed, "first move did not commit");

        HomeFurnitureLayoutDocument secondCandidate = CloneLayout(first.document);
        secondCandidate.instances[0].positionMeters.x = 3;
        FurniturePersistenceCommitResult second = adapter.CommitLayout(Guid.NewGuid().ToString("D"), first.gameRevision,
            first.layoutRevision, secondCandidate);
        Require(second.state == GamePersistenceStates.Committed, "second move did not commit");

        FurniturePersistenceCommitResult replay = adapter.CommitLayout(firstOperationId, before.gameRevision,
            before.layoutRevision, firstCandidate);
        Require(replay.state == GamePersistenceStates.Duplicate && replay.document != null &&
            replay.document.layoutRevision == second.layoutRevision && replay.gameRevision == second.gameRevision &&
            Math.Abs(replay.document.instances[0].positionMeters.x - 3) < 0.000000001,
            "duplicate operation returned its old candidate instead of current durable truth");
        Require(CountRows(path, "game_events", "kind='furniture_moved'") == 2,
            "duplicate replay emitted another durable movement event");
    }

    private static void FurnitureAtomicRollbackBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-rollback", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        string homeId = repository.Load().identity.homeId;
        var adapter = new FurniturePersistenceAdapter(repository, id => Layout(id, "seed-v1", new FurnitureVector3()));
        FurniturePersistenceLoadResult before = adapter.LoadHome(homeId);
        HomeFurnitureLayoutDocument candidate = CloneLayout(before.document);
        InventoryEntry(candidate, "furniture.seat.basic").storedCount--;
        HomeFurnitureLayoutInstance placed = CloneInstance(candidate.instances[0]);
        placed.instanceId = "furniture.instance.rollback";
        placed.definitionId = "furniture.seat.basic";
        candidate.instances.Add(placed);
        using (var connection = new SQLiteConnection(path))
            connection.Execute("CREATE TRIGGER synthetic_furniture_event_abort BEFORE INSERT ON game_events WHEN NEW.kind='furniture_placed' BEGIN SELECT RAISE(ABORT,'synthetic furniture event failure'); END");

        FurniturePersistenceCommitResult failed = adapter.CommitLayout(Guid.NewGuid().ToString("D"), before.gameRevision,
            before.layoutRevision, candidate);
        Require(failed.state == GamePersistenceStates.Unavailable, "forced event failure did not fail the transaction");
        FurniturePersistenceLoadResult after = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(homeId);
        Require(after.state == "loaded" && after.gameRevision == before.gameRevision && after.layoutRevision == before.layoutRevision &&
            after.document.instances.All(instance => instance.instanceId != placed.instanceId) &&
            InventoryEntry(after.document, "furniture.seat.basic").storedCount == 10 &&
            CountRows(path, "game_events", "kind='furniture_placed'") == 0,
            "failed event transaction published layout, inventory or event state");
    }

    private static void FurnitureLostAcknowledgementBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-lost-ack", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        GameLoadResult current = repository.Load();
        HomeFurnitureLayoutDocument legacy = Layout(current.identity.homeId, "legacy-seed", new FurnitureVector3());
        legacy.layoutRevision = current.layoutRevision + 1;
        Require(repository.Commit(new GameCommitRequest
        {
            operationId = Guid.NewGuid().ToString("D"), operationPayload = "legacy-layout",
            expectedGameRevision = current.gameRevision, expectedLayoutRevision = current.layoutRevision,
            progressionJson = current.progressionJson, layoutJson = SerializeLayout(legacy)
        }).state == GamePersistenceStates.Committed, "schema-one fixture layout commit failed");

        var lostAck = new LoseInventoryUpgradeAckRepository(repository);
        var adapter = new FurniturePersistenceAdapter(lostAck, id => Layout(id, "replacement-seed", new FurnitureVector3()));
        FurniturePersistenceLoadResult first = adapter.LoadHome(current.identity.homeId);
        Require(first.state == GamePersistenceStates.Unavailable, "synthetic lost acknowledgement was not surfaced");

        FurniturePersistenceLoadResult reopened = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(current.identity.homeId);
        Require(reopened.state == "loaded" && reopened.document.schemaVersion == 2 && reopened.document.inventory.Count == 8 &&
            reopened.document.inventory.All(entry => entry.storedCount == 10) &&
            CountRows(path, "game_events", "kind='home_inventory_initialized'") == 1,
            "reopen reseeded or lost an acknowledged inventory initialization");
    }

    private static void FurnitureStaleRevisionBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-stale", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        string homeId = repository.Load().identity.homeId;
        var adapter = new FurniturePersistenceAdapter(repository, id => Layout(id, "seed-v1", new FurnitureVector3()));
        FurniturePersistenceLoadResult before = adapter.LoadHome(homeId);
        HomeFurnitureLayoutDocument firstCandidate = CloneLayout(before.document);
        firstCandidate.instances[0].positionMeters.x = 2;
        FurniturePersistenceCommitResult first = adapter.CommitLayout(Guid.NewGuid().ToString("D"), before.gameRevision,
            before.layoutRevision, firstCandidate);
        Require(first.state == GamePersistenceStates.Committed, "fixture move did not commit");

        HomeFurnitureLayoutDocument staleCandidate = CloneLayout(before.document);
        staleCandidate.instances[0].positionMeters.x = 9;
        FurniturePersistenceCommitResult stale = adapter.CommitLayout(Guid.NewGuid().ToString("D"), before.gameRevision,
            before.layoutRevision, staleCandidate);
        FurniturePersistenceLoadResult after = new FurniturePersistenceAdapter(new SqliteGameRepository(path)).LoadHome(homeId);
        Require(stale.state == GamePersistenceStates.StaleRevision && after.state == "loaded" &&
            after.layoutRevision == first.layoutRevision && Math.Abs(after.document.instances[0].positionMeters.x - 2) < 0.000000001 &&
            CountRows(path, "game_events", "kind='furniture_moved'") == 1,
            "stale furniture write overwrote the latest layout or emitted a durable event");
    }

    private static void FurnitureFutureSchemaBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-future-schema", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        GameLoadResult current = repository.Load();
        HomeFurnitureLayoutDocument future = Layout(current.identity.homeId, "future-seed", new FurnitureVector3());
        future.schemaVersion = 3;
        future.layoutRevision = current.layoutRevision + 1;
        Require(repository.Commit(new GameCommitRequest
        {
            operationId = Guid.NewGuid().ToString("D"), operationPayload = "future-home-layout",
            expectedGameRevision = current.gameRevision, expectedLayoutRevision = current.layoutRevision,
            progressionJson = current.progressionJson, layoutJson = SerializeLayout(future)
        }).state == GamePersistenceStates.Committed, "future-schema fixture commit failed");
        var adapter = new FurniturePersistenceAdapter(repository, id => throw new InvalidOperationException("future schema must not seed"));

        FurniturePersistenceLoadResult result = adapter.LoadHome(current.identity.homeId);
        Require(result.state == GamePersistenceStates.RecoveryRequired && result.errorCode == "unsupported_schema_version" &&
            CountRows(path, "game_events", "kind='home_inventory_initialized'") == 0,
            "future home schema was reset, upgraded or accepted");
        HomeFurnitureLayoutDocument overwrite = Layout(current.identity.homeId, "older-seed", new FurnitureVector3());
        overwrite.schemaVersion = 2;
        overwrite.layoutRevision = current.layoutRevision + 1;
        overwrite.inventory = FurnitureInventorySeed.CreateInitial();
        FurniturePersistenceCommitResult overwriteAttempt = adapter.CommitLayout(Guid.NewGuid().ToString("D"),
            current.gameRevision + 1, current.layoutRevision + 1, overwrite);
        Require(overwriteAttempt.state == GamePersistenceStates.RecoveryRequired,
            "layout commit overwrote a future home schema that the reader rejected");
    }

    private static void FurnitureStockGuardBehavior(string root)
    {
        string path = Path.Combine(root, "furniture-stock-guard", "synthetic home.sqlite3");
        var repository = CreateInitializedGame(path);
        string homeId = repository.Load().identity.homeId;
        var adapter = new FurniturePersistenceAdapter(repository, id =>
        {
            HomeFurnitureLayoutDocument seed = Layout(id, "seed-v1", new FurnitureVector3());
            seed.instances[0].definitionId = "furniture.seat.basic";
            return seed;
        });
        FurniturePersistenceLoadResult before = adapter.LoadHome(homeId);

        HomeFurnitureLayoutDocument stockOnly = CloneLayout(before.document);
        InventoryEntry(stockOnly, "furniture.seat.basic").storedCount++;
        FurniturePersistenceCommitResult minted = adapter.CommitLayout(Guid.NewGuid().ToString("D"), before.gameRevision,
            before.layoutRevision, stockOnly);
        Require(minted.state == GamePersistenceStates.Invalid, "inventory-only stock edit was accepted");

        HomeFurnitureLayoutDocument overdraw = CloneLayout(before.document);
        InventoryEntry(overdraw, "furniture.seat.basic").storedCount -= 2;
        HomeFurnitureLayoutInstance placed = CloneInstance(overdraw.instances[0]);
        placed.instanceId = "furniture.instance.overdraw";
        placed.definitionId = "furniture.seat.basic";
        overdraw.instances.Add(placed);
        FurniturePersistenceCommitResult overdrawn = adapter.CommitLayout(Guid.NewGuid().ToString("D"), before.gameRevision,
            before.layoutRevision, overdraw);
        Require(overdrawn.state == GamePersistenceStates.Invalid, "placement spent more than one stock unit");

        HomeFurnitureLayoutDocument uncreditedReturn = CloneLayout(before.document);
        uncreditedReturn.instances.RemoveAll(instance => instance.instanceId == "instance.synthetic");
        FurniturePersistenceCommitResult uncredited = adapter.CommitLayout(Guid.NewGuid().ToString("D"), before.gameRevision,
            before.layoutRevision, uncreditedReturn);
        Require(uncredited.state == GamePersistenceStates.Invalid, "return removed furniture without crediting one stock unit");
        FurniturePersistenceLoadResult after = adapter.LoadHome(homeId);
        Require(after.state == "loaded" && after.gameRevision == before.gameRevision && after.layoutRevision == before.layoutRevision &&
            InventoryEntry(after.document, "furniture.seat.basic").storedCount == 10 && after.document.instances.Count == 1,
            "rejected stock changes altered durable home state");
    }

    private static SqliteGameRepository CreateInitializedGame(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        Require(repository.Commit(Request(initial.gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4}", "new-game")).state == GamePersistenceStates.Committed, "fixture game init failed");
        return repository;
    }

    private static FurnitureInventoryEntry InventoryEntry(HomeFurnitureLayoutDocument document, string definitionId)
    {
        FurnitureInventoryEntry entry = document.inventory.FirstOrDefault(item => item.definitionId == definitionId);
        if (entry == null) throw new InvalidOperationException("Fixture inventory is missing " + definitionId + ".");
        return entry;
    }

    private static HomeFurnitureLayoutDocument CloneLayout(HomeFurnitureLayoutDocument document)
    {
        return (HomeFurnitureLayoutDocument)new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument))
            .ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(SerializeLayout(document))));
    }

    private static HomeFurnitureLayoutInstance CloneInstance(HomeFurnitureLayoutInstance instance)
    {
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(HomeFurnitureLayoutInstance));
            serializer.WriteObject(stream, instance);
            stream.Position = 0;
            return (HomeFurnitureLayoutInstance)serializer.ReadObject(stream);
        }
    }

    private static string EventTarget(string path, string kind)
    {
        using (var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly))
            return connection.ExecuteScalar<string>("SELECT target_id FROM game_events WHERE kind=? ORDER BY rowid DESC LIMIT 1", kind);
    }

    private static void BackupBehavior(string root)
    {
        string directory = Path.Combine(root, "backup");
        string path = Path.Combine(directory, "synthetic game.sqlite3");
        string backupRoot = Path.Combine(directory, "private game backups");
        Directory.CreateDirectory(directory);
        var repository = new SqliteGameRepository(path);
        GameLoadResult initial = repository.Load();
        string operationId = Guid.NewGuid().ToString("D");
        string json1 = "{\"schemaVersion\":4,\"value\":1}";
        GameCommitRequest firstRequest = Request(initial.gameRevision, operationId, json1, "operation-1");
        Require(repository.Commit(firstRequest).state == GamePersistenceStates.Committed, "initial backup fixture commit failed");
        DateTime now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        var backups = new GameBackupService(repository, backupRoot, () => now);
        GameBackupResult checkpoint = backups.CreateBackup("checkpoint");
        Require(checkpoint.state == "created" && repository.ValidateBackup(checkpoint.path).state == GamePersistenceStates.Ready, "consistent checkpoint failed");
        Require(backups.CreateBackup("pause").state == "throttled", "30-minute checkpoint limit was not enforced");

        GameLoadResult current = repository.Load();
        Require(repository.Commit(Request(current.gameRevision, Guid.NewGuid().ToString("D"),
            "{\"schemaVersion\":4,\"value\":2}", "operation-2")).state == GamePersistenceStates.Committed, "second state commit failed");
        GameRecoveryResult staged = backups.StageRecovery(checkpoint.backupId);
        Require(staged.state == GamePersistenceStates.RecoveryStaged, "valid backup did not stage");
        Require(backups.ApplyRecovery(staged.candidate, false).state == GamePersistenceStates.Invalid, "recovery applied without approval");
        GameRecoveryResult applied = backups.ApplyRecovery(staged.candidate, true);
        Require(applied.state == GamePersistenceStates.Recovered, "approved staged recovery failed: " + applied.message);
        Require(repository.Load().progressionJson == json1, "recovery did not restore the staged revision");
        GameCommitResult duplicate = repository.Commit(firstRequest);
        Require(duplicate.state == GamePersistenceStates.Duplicate && duplicate.receipt.gameRevision == 1,
            "restored receipt replayed as a new success");

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var connection = new SQLiteConnection(checkpoint.path, SQLiteOpenFlags.ReadOnly))
            foreach (var row in connection.Query<TableRow>("SELECT name FROM sqlite_master WHERE type='table'")) tables.Add(row.name);
        Require(!tables.Contains("memory") && !tables.Contains("chat") && !tables.Contains("credentials"), "backup included an excluded owner table");
        Require(backups.StageRecovery("..\\outside").state == GamePersistenceStates.Invalid, "path traversal backup ID was accepted");
    }

    private static HomeFurnitureLayoutDocument Layout(string homeId, string seedVersion, FurnitureVector3 position)
    {
        return new HomeFurnitureLayoutDocument
        {
            schemaVersion = 1, homeId = homeId, roomId = "room.synthetic", catalogueVersion = "1.0.0",
            seedVersion = seedVersion, coordinateFrame = "room_local_y_up_positive_z_forward", lengthUnit = "m",
            instances = new List<HomeFurnitureLayoutInstance>
            {
                new HomeFurnitureLayoutInstance
                {
                    instanceId = "instance.synthetic", definitionId = "missing.definition.preserved",
                    positionMeters = position,
                    rotation = new FurnitureQuaternion { x = 0, y = 0, z = 0, w = 1 },
                    scale = new FurnitureVector3 { x = 1, y = 1, z = 1 },
                    support = new FurnitureLayoutSupportReference
                    {
                        supportId = "room.synthetic.floor", supportKind = "roomFloor", supportFrameRevision = "1",
                        attachmentPointId = "origin", localPositionMeters = new FurnitureVector3(),
                        localRotation = new FurnitureQuaternion { x = 0, y = 0, z = 0, w = 1 }
                    },
                    enabled = true, conditionState = "good", resourceState = new List<FurnitureResourceState>(), instanceRevision = 1
                }
            }
        };
    }

    private static string SerializeLayout(HomeFurnitureLayoutDocument document)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument)).WriteObject(stream, document);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static GameCommitRequest Request(long revision, string operationId, string progression, string payload)
    {
        return new GameCommitRequest
        {
            operationId = operationId, operationPayload = payload,
            expectedGameRevision = revision, progressionJson = progression
        };
    }

    private static RecordingPolicy Policy(string revision, bool captureText, int recordCap, long byteCap)
    {
        return new RecordingPolicy
        {
            policyRevision = revision, captureMetadata = true, captureRawText = captureText,
            retentionDays = 1, byteCap = byteCap, recordCap = recordCap
        };
    }

    private static InteractionObservation Observation(string kind, string sourceId, string rawText,
        Dictionary<string, string> metadata = null, string occurredAtUtc = "2026-10-22T00:00:00Z")
    {
        return new InteractionObservation
        {
            kind = kind, sourceEventId = sourceId, rawText = rawText,
            metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
            occurredAtUtc = occurredAtUtc
        };
    }

    private static int CountRows(string databasePath, string table, string where = "1=1")
    {
        using (var connection = new SQLiteConnection(databasePath))
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM " + table + " WHERE " + where);
    }

    private static byte[] ReadBytes(string databasePath, string sql, params object[] args)
    {
        using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
            return connection.ExecuteScalar<byte[]>(sql, args);
    }

    private static string ReadText(string databasePath, string sql, params object[] args)
    {
        using (var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadOnly))
            return connection.ExecuteScalar<string>(sql, args);
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TableRow
    {
        public string name { get; set; }
    }

    private sealed class LoseInventoryUpgradeAckRepository : IGameRepository
    {
        private readonly IGameRepository inner;

        public LoseInventoryUpgradeAckRepository(IGameRepository inner) { this.inner = inner; }
        public string DatabasePath { get { return inner.DatabasePath; } }
        public GameLoadResult Load() { return inner.Load(); }
        public GameItemInventoryResult ReadPlayerItemQuantities() { return inner.ReadPlayerItemQuantities(); }
        public GameScenarioHostEffectEvidenceResult ReadScenarioHostEffectEvidence(string runId, string invocationId,
            string effectStepExecutionId, string completedStepExecutionId)
        { return inner.ReadScenarioHostEffectEvidence(runId, invocationId, effectStepExecutionId, completedStepExecutionId); }
        public GameItemQuantityOperationResult FindItemQuantityOperation(string operationId) { return inner.FindItemQuantityOperation(operationId); }
        public GameFurniturePowerOperationResult FindFurniturePowerOperation(string operationId) { return inner.FindFurniturePowerOperation(operationId); }
        public GameCommitResult Commit(GameCommitRequest request)
        {
            GameCommitResult result = inner.Commit(request);
            if (result.state == GamePersistenceStates.Committed && request.events.Any(item => item.kind == "home_inventory_initialized"))
                return new GameCommitResult { state = GamePersistenceStates.Unavailable, errorCode = "synthetic_ack_lost" };
            return result;
        }
        public GameCommitReceipt FindReceipt(string operationId) { return inner.FindReceipt(operationId); }
        public GameMigrationReceipt FindMigration(string sourceHash) { return inner.FindMigration(sourceHash); }
        public bool CreateConsistentBackup(string destinationPath) { return inner.CreateConsistentBackup(destinationPath); }
        public GameBackupValidationResult ValidateBackup(string path) { return inner.ValidateBackup(path); }
    }

    [DataContract]
    private sealed class GeneratedArchiveFixtureDatabase
    {
        [DataMember(Name = "records", Order = 1)] public List<GeneratedArchiveFixtureRecord> records;
    }

    [DataContract]
    private sealed class GeneratedArchiveFixtureRecord
    {
        [DataMember(Name = "generatedDialogueId", Order = 1)] public string generatedDialogueId;
        [DataMember(Name = "originSessionId", Order = 2)] public string originSessionId;
        [DataMember(Name = "title", Order = 3)] public string title;
        [DataMember(Name = "sourcePrompt", Order = 4)] public string sourcePrompt;
        [DataMember(Name = "summary", Order = 5)] public string summary;
        [DataMember(Name = "createdAt", Order = 6)] public string createdAt;
        [DataMember(Name = "lastPlayedAt", Order = 7)] public string lastPlayedAt;
        [DataMember(Name = "status", Order = 8)] public int status;
        [DataMember(Name = "wasPlayed", Order = 9)] public bool wasPlayed;
        [DataMember(Name = "surfacedToPlayer", Order = 10)] public bool surfacedToPlayer;
        [DataMember(Name = "tags", Order = 11)] public string[] tags;
        [DataMember(Name = "jsonPath", Order = 12)] public string jsonPath;
    }
}
