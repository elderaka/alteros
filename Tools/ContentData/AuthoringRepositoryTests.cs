using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

internal static class AuthoringRepositoryTests
{
    private static Assembly _authoring;
    private static Type _repository;
    private static int _passed;
    private static int _failed;

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--writer") return Writer(args);
        if (args.Length > 1 && args[0] == "--reference-audit")
        {
            _authoring = Assembly.LoadFrom(Path.GetFullPath(args[1]));
            Run("standalone authoring assembly has no Unity runtime references", StandaloneAssemblyHasNoUnityReferences);
            Console.WriteLine("RESULT StandaloneReferences: " + _passed + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }
        if (args.Length > 0 && File.Exists(args[0]))
        {
            _authoring = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            _repository = _authoring.GetType("AlterOS.Authoring.AuthoringRepository", false);
        }
        string group = args.Length > 1 ? args[1] : "Repository";

        if (_repository == null)
        {
            Run("authoring repository API is available", delegate
            {
                throw new InvalidOperationException("AlterOS.Authoring.AuthoringRepository is not implemented.");
            });
        }
        else
        {
            if (group == "Repository" || group == "All")
            {
                Run("standalone authoring assembly has no Unity runtime references", StandaloneAssemblyHasNoUnityReferences);
                Run("CreateNew and OpenExisting have explicit non-rebuilding semantics", CreateAndOpen);
                Run("reopen preserves UTF-8 bytes and unknown fields", Utf8RoundTrip);
                Run("syntactically valid drafts save while malformed JSON does not write", DraftValidation);
                Run("stale revision writers conflict", StaleWriter);
                Run("operation retries are idempotent and changed retries conflict", OperationRetry);
                Run("embedded source IDs remain stable across document saves", SourceIdCannotChange);
                Run("failed transaction rolls back revision, head, receipt and store revision", RollbackAtomicity);
                Run("missing, corrupt and future stores fail closed", FailClosedStores);
                Run("list cursor binds kind, limit and store identity", CursorBinding);
                Run("build-set CAS and snapshot revisions remain pinned", BuildSetAndSnapshot);
                Run("Furniture catalogue settings are one revisioned singleton in selected snapshots", FurnitureCatalogueSettingsSingleton);
                Run("schema-v2 migration preserves authored revisions and introduces Furniture settings", MigrateVersion2To3PreservesRevisions);
                Run("two process writers cannot overwrite the same expected revision", ProcessWriters);
            }
            if (group == "Import" || group == "All")
            {
                Run("explicit import preserves every registered kind and original bytes", ImportAllKindsAndIdempotentRetry);
                Run("import rejects changed, malformed, duplicate and unsafe source inventories", ImportRejectsInvalidSources);
                Run("failed import rolls back every document and receipt together", ImportRollbackAtomicity);
                Run("stale import cannot overwrite a newer SQL edit", ImportStoreRevisionCas);
            }
            if (group == "Interchange" || group == "All")
            {
                Run("deterministic snapshot export preserves originals and explicitly imports into a fresh store", InterchangeRoundTrip);
                Run("authoring backup reopens as a consistent database snapshot", BackupReopens);
            }
            if (group != "Repository" && group != "Import" && group != "All")
                if (group != "Interchange") Run("requested authoring test group exists", delegate { throw new InvalidOperationException("Unknown test group: " + group); });
        }

        Console.WriteLine("RESULT Authoring " + group + ": " + _passed + " passed, " + _failed + " failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void StandaloneAssemblyHasNoUnityReferences()
    {
        string[] forbidden = _authoring.GetReferencedAssemblies()
            .Select(item => item.Name)
            .Where(name => name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert(forbidden.Length == 0, "Standalone authoring assembly references Unity: " + string.Join(",", forbidden));
    }

    private static int Writer(string[] args)
    {
        if (args.Length != 7 || !Environment.Is64BitProcess) return 2;
        try
        {
            _authoring = Assembly.LoadFrom(Path.GetFullPath(args[1]));
            _repository = _authoring.GetType("AlterOS.Authoring.AuthoringRepository", true);
            object repository = Value(StaticCall(_repository, "OpenExisting", args[2]));
            object request = New("AuthoringSaveRequest");
            Set(request, "documentId", args[3]);
            Set(request, "kind", "topic");
            Set(request, "logicalPath", "Drafts/topics/shared-doc.json");
            Set(request, "expectedHash", args[4]);
            Set(request, "operationId", args[5]);
            Set(request, "documentBytes", Convert.FromBase64String(args[6]));
            object result = Call(repository, "Save", request);
            string status = Status(result);
            Console.WriteLine(status);
            Dispose(repository);
            return status == "ready" || status == "conflict" ? 0 : 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetBaseException().Message);
            return 4;
        }
    }

    private static void CreateAndOpen()
    {
        WithRoot(delegate(string root)
        {
            string path = Path.Combine(root, "作者データ.sqlite3");
            AssertStatus(StaticCall(_repository, "OpenExisting", path), "not_initialized");
            Assert(!File.Exists(path), "OpenExisting must not create a missing database.");
            object created = Value(StaticCall(_repository, "CreateNew", path));
            Assert(File.Exists(path), "CreateNew must create the requested database.");
            Assert(created is IDisposable, "AuthoringRepository must implement IDisposable for bounded store lifetime.");
            VerifyConnectionConfiguration(created);
            object info = Value(Call(created, "ReadStoreInfo"));
            Assert(Convert.ToInt32(Get(info, "schemaVersion")) == 3 && Convert.ToInt64(Get(info, "storeRevision")) == 0,
                "New authoring stores must report the independent schema version and initial store revision.");
            Dispose(created);
            byte[] before = File.ReadAllBytes(path);
            AssertStatus(StaticCall(_repository, "CreateNew", path), "conflict");
            Assert(before.SequenceEqual(File.ReadAllBytes(path)), "CreateNew must not replace an existing database.");
            object reopened = Value(StaticCall(_repository, "OpenExisting", path));
            Assert(reopened != null, "OpenExisting should reopen a valid database.");
            VerifyConnectionConfiguration(reopened);
            Dispose(reopened);
        });
    }

    private static void Utf8RoundTrip()
    {
        WithRoot(delegate(string root)
        {
            string path = Path.Combine(root, "authoring.sqlite3");
            object repository = NewRepository(path);
            byte[] bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"unknown\":{\"言語\":\"日本語🦊\"}} ");
            object saved = Save(repository, "topic-opaque-01", "topic", "Drafts/topics/topic-opaque-01.json", "", "utf8-save", bytes);
            string hash = String(Get(Value(saved), "revisionHash"));
            Assert(Equal(bytes, Get(Value(saved), "documentBytes")), "Save result must retain the exact bytes.");
            Dispose(repository);
            repository = NewRepositoryExisting(path);
            object read = Value(Call(repository, "Read", "topic-opaque-01", ""));
            Assert(Equal(bytes, Get(read, "documentBytes")), "Reopen must retain exact UTF-8 bytes and unknown fields.");
            Assert(String(Get(read, "revisionHash")) == hash, "Reopen must retain the SHA-256 revision.");
            Dispose(repository);
        });
    }

    private static void DraftValidation()
    {
        WithRoot(delegate(string root)
        {
            object repository = NewRepository(Path.Combine(root, "authoring.sqlite3"));
            byte[] incomplete = Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"unknown\":true}");
            AssertStatus(Save(repository, "draft-1", "topic", "Drafts/topics/draft-1.json", "", "draft-save", incomplete), "ready");
            long before = StoreRevision(Path.Combine(root, "authoring.sqlite3"));
            object malformed = Save(repository, "draft-2", "topic", "Drafts/topics/draft-2.json", "", "malformed", Encoding.UTF8.GetBytes("{\"broken\":"));
            AssertStatus(malformed, "invalid");
            AssertStatus(Call(repository, "Read", "draft-2", ""), "invalid");
            Assert(StoreRevision(Path.Combine(root, "authoring.sqlite3")) == before, "Malformed JSON must not write a revision or advance the store.");
            Dispose(repository);
        });
    }

    private static void StaleWriter()
    {
        WithRoot(delegate(string root)
        {
            string path = Path.Combine(root, "authoring.sqlite3");
            object first = NewRepository(path);
            byte[] original = Encoding.UTF8.GetBytes("{\"value\":1}");
            object initial = Save(first, "doc-1", "topic", "Drafts/topics/doc-1.json", "", "initial", original);
            string expected = String(Get(Value(initial), "revisionHash"));
            object second = NewRepositoryExisting(path);
            byte[] editA = Encoding.UTF8.GetBytes("{\"value\":2}");
            byte[] editB = Encoding.UTF8.GetBytes("{\"value\":3}");
            AssertStatus(Save(first, "doc-1", "topic", "Drafts/topics/doc-1.json", expected, "writer-a", editA), "ready");
            AssertStatus(Save(second, "doc-1", "topic", "Drafts/topics/doc-1.json", expected, "writer-b", editB), "conflict");
            object current = Value(Call(second, "Read", "doc-1", ""));
            Assert(Equal(editA, Get(current, "documentBytes")), "A stale writer must not replace the current head.");
            Dispose(first);
            Dispose(second);
        });
    }

    private static void OperationRetry()
    {
        WithRoot(delegate(string root)
        {
            object repository = NewRepository(Path.Combine(root, "authoring.sqlite3"));
            byte[] bytes = Encoding.UTF8.GetBytes("{\"value\":1}");
            object first = Save(repository, "doc-1", "topic", "Drafts/topics/doc-1.json", "", "operation-1", bytes);
            object retry = Save(repository, "doc-1", "topic", "Drafts/topics/doc-1.json", "", "operation-1", bytes);
            AssertStatus(retry, "ready");
            Assert(String(Get(Value(first), "revisionHash")) == String(Get(Value(retry), "revisionHash")), "Same operation must return its recorded outcome.");
            AssertStatus(Save(repository, "doc-1", "topic", "Drafts/topics/doc-1.json", "", "operation-1", Encoding.UTF8.GetBytes("{\"value\":2}")), "conflict");
            Dispose(repository);
        });
    }

    private static void SourceIdCannotChange()
    {
        WithRoot(delegate(string root)
        {
            string databasePath = Path.Combine(root, "authoring.sqlite3");
            object repository = NewRepository(databasePath);
            byte[] firstBytes = Encoding.UTF8.GetBytes("{\"id\":\"generic.stable\"}");
            object first = Save(repository, "stable-document", "generic_dialogue", "Generic/stable.json", "", "stable-first", firstBytes);
            AssertStatus(first, "ready");
            string revisionHash = String(Get(Value(first), "revisionHash"));
            Assert(String(Get(Value(first), "sourceId")) == "generic.stable", "The first embedded source ID was not captured as stable metadata.");

            byte[] changedIdentity = Encoding.UTF8.GetBytes("{\"id\":\"generic.replaced\"}");
            object rejected = Save(repository, "stable-document", "generic_dialogue", "Generic/stable.json", revisionHash,
                "stable-replace", changedIdentity);
            AssertStatus(rejected, "conflict");
            object current = Value(Call(repository, "Read", "stable-document", ""));
            Assert(String(Get(current, "sourceId")) == "generic.stable" &&
                Equal(firstBytes, Get(current, "documentBytes")) && StoreRevision(databasePath) == 1,
                "A source ID replacement must not change the current document, bytes or store revision.");
            Dispose(repository);
        });
    }

    private static void RollbackAtomicity()
    {
        WithRoot(delegate(string root)
        {
            string path = Path.Combine(root, "authoring.sqlite3");
            object repository = NewRepository(path);
            byte[] beforeBytes = Encoding.UTF8.GetBytes("{\"value\":1}");
            object before = Save(repository, "doc-1", "topic", "Drafts/topics/doc-1.json", "", "initial", beforeBytes);
            string beforeHash = String(Get(Value(before), "revisionHash"));
            long beforeRevision = StoreRevision(path);
            SqlExec(path, "CREATE TRIGGER inject_receipt_failure BEFORE INSERT ON operation_receipts BEGIN SELECT RAISE(ABORT,'injected failure'); END");
            byte[] attempted = Encoding.UTF8.GetBytes("{\"value\":2}");
            object failed = Save(repository, "doc-1", "topic", "Drafts/topics/doc-1.json", beforeHash, "retry-after-failure", attempted);
            AssertStatus(failed, "invalid");
            Assert(StoreRevision(path) == beforeRevision, "A failed transaction must not advance store revision.");
            Assert(SqlScalar<int>(path, "SELECT COUNT(*) FROM document_revisions WHERE document_id='doc-1' AND revision_hash='" + Sha256(attempted) + "'") == 0, "The new immutable revision must roll back.");
            Assert(SqlScalar<int>(path, "SELECT COUNT(*) FROM operation_receipts WHERE operation_id='retry-after-failure'") == 0, "The receipt must roll back with the revision.");
            SqlExec(path, "DROP TRIGGER inject_receipt_failure");
            object retry = Save(repository, "doc-1", "topic", "Drafts/topics/doc-1.json", beforeHash, "retry-after-failure", attempted);
            AssertStatus(retry, "ready");
            object current = Value(Call(repository, "Read", "doc-1", ""));
            Assert(String(Get(current, "revisionHash")) == Sha256(attempted), "Successful retry must install the requested head.");
            Dispose(repository);
        });
    }

    private static void FailClosedStores()
    {
        WithRoot(delegate(string root)
        {
            string missing = Path.Combine(root, "missing.sqlite3");
            AssertStatus(StaticCall(_repository, "OpenExisting", missing), "not_initialized");
            Assert(!File.Exists(missing), "Missing open must not create a store.");
            string corrupt = Path.Combine(root, "corrupt.sqlite3");
            File.WriteAllBytes(corrupt, Encoding.UTF8.GetBytes("not a sqlite database"));
            byte[] before = File.ReadAllBytes(corrupt);
            AssertStatus(StaticCall(_repository, "OpenExisting", corrupt), "recovery_required");
            Assert(before.SequenceEqual(File.ReadAllBytes(corrupt)), "Corrupt database bytes must remain unchanged.");
            string future = Path.Combine(root, "future.sqlite3");
            CreateFutureDatabase(future);
            before = File.ReadAllBytes(future);
            AssertStatus(StaticCall(_repository, "OpenExisting", future), "unsupported_schema");
            Assert(before.SequenceEqual(File.ReadAllBytes(future)), "A future-schema database must remain unchanged.");
        });
    }

    private static void CursorBinding()
    {
        WithRoot(delegate(string root)
        {
            object repository = NewRepository(Path.Combine(root, "authoring.sqlite3"));
            for (int i = 1; i <= 3; i++) Save(repository, "doc-" + i, "topic", "Drafts/topics/doc-" + i + ".json", "", "list-" + i, Encoding.UTF8.GetBytes("{\"id\":" + i + "}"));
            object first = Value(Call(repository, "List", "topic", "", 2));
            Assert(Items(Get(first, "documents")).Count == 2, "First page should contain the requested bound.");
            string cursor = String(Get(first, "nextCursor"));
            Assert(!string.IsNullOrEmpty(cursor), "A further page should return a cursor.");
            object second = Value(Call(repository, "List", "topic", cursor, 2));
            Assert(Items(Get(second, "documents")).Count == 1, "Second page should contain the final document.");
            AssertStatus(Call(repository, "List", "scenario", cursor, 2), "invalid");
            AssertStatus(Call(repository, "List", "topic", cursor, 1), "invalid");
            AssertStatus(Call(repository, "List", "topic", "", 0), "invalid");
            object secondStore = NewRepository(Path.Combine(root, "another-authoring.sqlite3"));
            AssertStatus(Call(secondStore, "List", "topic", cursor, 2), "invalid");
            Dispose(secondStore);
            Dispose(repository);
        });
    }

    private static void BuildSetAndSnapshot()
    {
        WithRoot(delegate(string root)
        {
            object repository = NewRepository(Path.Combine(root, "authoring.sqlite3"));
            object first = Save(repository, "doc-a", "generic_dialogue", "Generic/a.json", "", "save-a", Encoding.UTF8.GetBytes("{\"id\":\"a\"}"));
            object second = Save(repository, "doc-b", "topic", "Drafts/topics/b.json", "", "save-b", Encoding.UTF8.GetBytes("{\"topicId\":\"b\"}"));
            AssertStatus(second, "ready");
            string firstHash = String(Get(Value(first), "revisionHash"));
            object setRequest = New("AuthoringBuildSetRequest");
            Set(setRequest, "name", "default");
            Set(setRequest, "documentIds", new[] { "doc-a", "doc-b" });
            Set(setRequest, "expectedHash", "");
            Set(setRequest, "operationId", "set-create");
            object setResult = Call(repository, "SaveBuildSet", setRequest);
            AssertStatus(setResult, "ready");
            string selectionHash = String(Get(Value(setResult), "revisionHash"));
            object readSet = Value(Call(repository, "ReadBuildSet", "default"));
            Assert(Items(Get(readSet, "documentIds")).Cast<string>().SequenceEqual(new[] { "doc-a", "doc-b" }), "Build-set order must be retained.");
            setRequest = New("AuthoringBuildSetRequest");
            Set(setRequest, "name", "default");
            Set(setRequest, "documentIds", new[] { "doc-b", "doc-a" });
            Set(setRequest, "expectedHash", selectionHash);
            Set(setRequest, "operationId", "set-update");
            AssertStatus(Call(repository, "SaveBuildSet", setRequest), "ready");
            setRequest = New("AuthoringBuildSetRequest");
            Set(setRequest, "name", "default");
            Set(setRequest, "documentIds", new[] { "doc-a" });
            Set(setRequest, "expectedHash", selectionHash);
            Set(setRequest, "operationId", "set-stale");
            AssertStatus(Call(repository, "SaveBuildSet", setRequest), "conflict");

            object snapshot = Value(Call(repository, "ReadSnapshot", (object)new[] { "doc-a", "doc-b" }));
            string snapshotHash = String(Get(snapshot, "snapshotHash"));
            Assert(Items(Get(snapshot, "documents")).Count == 2, "Snapshot must pin every selected document.");
            Assert(Items(Get(snapshot, "documents")).Cast<object>().Select(item => String(Get(item, "revisionHash"))).ToArray().Contains(firstHash), "Snapshot must pin the selected head revision.");
            object latestA = Value(Call(repository, "Read", "doc-a", ""));
            object changed = Save(repository, "doc-a", "generic_dialogue", "Generic/a.json", firstHash, "edit-after-snapshot",
                Encoding.UTF8.GetBytes("{\"id\":\"a\",\"edited\":true}"));
            AssertStatus(changed, "ready");
            Assert(String(Get(Items(Get(snapshot, "documents")).Cast<object>().First(item => String(Get(item, "documentId")) == "doc-a"), "revisionHash")) == firstHash, "Snapshot bytes and revision must not move after a later edit.");
            object nextSnapshot = Value(Call(repository, "ReadSnapshot", (object)new[] { "doc-a", "doc-b" }));
            Assert(String(Get(nextSnapshot, "snapshotHash")) != snapshotHash, "A changed head must produce a different snapshot hash.");
            Assert(String(Get(latestA, "revisionHash")) == firstHash, "Explicit revision reads remain immutable.");
            Dispose(repository);
        });
    }

    private static void FurnitureCatalogueSettingsSingleton()
    {
        WithRoot(delegate(string root)
        {
            Type kinds = _authoring.GetType("AlterOS.Authoring.AuthoringContentKinds", true);
            Assert((bool)StaticCall(kinds, "IsSupported", "furniture_catalogue_settings"),
                "Furniture catalogue settings must use the revisioned authoring document contract.");
            object repository = NewRepository(Path.Combine(root, "authoring.sqlite3"));
            byte[] initialBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"test-v1\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}");
            object saved = Save(repository, "furniture.catalogue", "furniture_catalogue_settings", "Furniture/catalogue-settings.json", "", "catalogue-settings-create", initialBytes);
            AssertStatus(saved, "ready");
            string firstHash = String(Get(Value(saved), "revisionHash"));
            object buildSetRequest = New("AuthoringBuildSetRequest");
            Set(buildSetRequest, "name", "default");
            Set(buildSetRequest, "expectedHash", "");
            Set(buildSetRequest, "operationId", "catalogue-build-set-create");
            Set(buildSetRequest, "documentIds", new List<string> { "furniture.catalogue" });
            object buildSet = Value(Call(repository, "SaveBuildSet", buildSetRequest));
            Assert(Items(Get(buildSet, "documentIds")).Cast<object>().Select(String).SequenceEqual(new[] { "furniture.catalogue" }),
                "The singleton settings source must be selectable by the ordinary named build set.");
            object snapshot = Value(Call(repository, "ReadSnapshot", new object[] { Get(buildSet, "documentIds") }));
            Assert(Items(Get(snapshot, "documents")).Cast<object>().Count() == 1,
                "The selected build snapshot must contain the singleton settings document.");

            byte[] editedBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalogueVersion\":\"test-v2\",\"lengthUnit\":\"m\",\"angleUnit\":\"deg\",\"coordinateConvention\":\"unity_local_y_up_positive_z_forward\",\"relationshipRules\":[],\"capabilities\":[],\"legacyAliases\":[]}");
            object edited = Save(repository, "furniture.catalogue", "furniture_catalogue_settings", "Furniture/catalogue-settings.json", firstHash, "catalogue-settings-edit", editedBytes);
            AssertStatus(edited, "ready");
            AssertStatus(Save(repository, "furniture.catalogue", "furniture_catalogue_settings", "Furniture/catalogue-settings.json", firstHash, "catalogue-settings-stale", initialBytes), "conflict");
            AssertStatus(Save(repository, "furniture.catalogue-duplicate", "furniture_catalogue_settings", "Furniture/catalogue-settings-duplicate.json", "", "catalogue-settings-duplicate", initialBytes), "invalid");
            object read = Value(Call(repository, "Read", "furniture.catalogue", ""));
            Assert(Equal(editedBytes, Get(read, "documentBytes")), "The singleton settings edit must reopen byte-for-byte.");
            Assert(String(Get(read, "revisionHash")) != firstHash, "Editing settings must create a new immutable CAS revision.");
            Dispose(repository);
        });
    }

    private static void MigrateVersion2To3PreservesRevisions()
    {
        WithRoot(delegate(string root)
        {
            string path = Path.Combine(root, "authoring-v2.sqlite3");
            byte[] bytes = Encoding.UTF8.GetBytes("{\"topicId\":\"preserved.topic\",\"unknown\":{\"keep\":true}}");
            string hash = Sha256(bytes);
            object connection = OpenSqlite(path, true);
            try
            {
                SqlExecOn(connection, "CREATE TABLE metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
                SqlExecOn(connection, "INSERT INTO metadata(key,value) VALUES('schema_version','2'),('store_id','11111111-1111-4111-8111-111111111111'),('store_revision','1')");
                SqlExecOn(connection, "CREATE TABLE documents (document_id TEXT PRIMARY KEY NOT NULL CHECK(length(document_id) BETWEEN 1 AND 128), kind TEXT NOT NULL CHECK(kind IN ('generic_dialogue','topic_definition','scenario_definition','legacy_sequence','ambient_pool','activity_scenario_library','activity_greeting_menu','conversation_coverage','topic_migration_map','onboarding_steps','topic','scenario','dialogue_definition','furniture_definition','item_definition','action_definition')), source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), logical_path TEXT NOT NULL CHECK(length(logical_path) BETWEEN 1 AND 1024), logical_path_key TEXT NOT NULL UNIQUE CHECK(length(logical_path_key) BETWEEN 1 AND 2048), head_hash TEXT NOT NULL CHECK(length(head_hash)=64), created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL)");
                SqlExecOn(connection, "CREATE INDEX documents_kind_id ON documents(kind,document_id)");
                SqlExecOn(connection, "CREATE TABLE document_revisions (document_id TEXT NOT NULL, revision_hash TEXT NOT NULL CHECK(length(revision_hash)=64), document_bytes BLOB NOT NULL CHECK(length(document_bytes) BETWEEN 1 AND 4194304), created_utc TEXT NOT NULL, PRIMARY KEY(document_id,revision_hash), FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
                SqlExecOn(connection, "CREATE TABLE operation_receipts (operation_id TEXT PRIMARY KEY NOT NULL CHECK(length(operation_id) BETWEEN 1 AND 128), operation_kind TEXT NOT NULL CHECK(operation_kind IN ('document.save','build_set.save')), request_hash TEXT NOT NULL CHECK(length(request_hash)=64), result_status TEXT NOT NULL CHECK(result_status IN ('ready','invalid','conflict')), document_id TEXT NOT NULL DEFAULT '', revision_hash TEXT NOT NULL DEFAULT '', build_set_name TEXT NOT NULL DEFAULT '', code TEXT NOT NULL DEFAULT '', message TEXT NOT NULL DEFAULT '', result_payload TEXT NOT NULL DEFAULT '', store_revision INTEGER NOT NULL CHECK(store_revision>=0), created_utc TEXT NOT NULL)");
                SqlExecOn(connection, "CREATE TABLE import_receipts (operation_id TEXT PRIMARY KEY NOT NULL CHECK(length(operation_id) BETWEEN 1 AND 128), request_hash TEXT NOT NULL CHECK(length(request_hash)=64), result_status TEXT NOT NULL CHECK(result_status IN ('ready','invalid','conflict')), source_count INTEGER NOT NULL CHECK(source_count>=0), store_revision INTEGER NOT NULL CHECK(store_revision>=0), code TEXT NOT NULL DEFAULT '', message TEXT NOT NULL DEFAULT '', created_utc TEXT NOT NULL)");
                SqlExecOn(connection, "CREATE TABLE import_sources (operation_id TEXT NOT NULL, source_path TEXT NOT NULL CHECK(length(source_path) BETWEEN 1 AND 1024), source_hash TEXT NOT NULL CHECK(length(source_hash)=64), document_id TEXT NOT NULL, source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), original_bytes BLOB NOT NULL CHECK(length(original_bytes) BETWEEN 1 AND 4194304), PRIMARY KEY(operation_id,source_path), FOREIGN KEY(operation_id) REFERENCES import_receipts(operation_id) ON DELETE RESTRICT, FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
                SqlExecOn(connection, "CREATE INDEX import_sources_document ON import_sources(document_id,operation_id)");
                SqlExecOn(connection, "CREATE TABLE build_sets (name TEXT PRIMARY KEY NOT NULL CHECK(length(name) BETWEEN 1 AND 64), revision_hash TEXT NOT NULL CHECK(length(revision_hash)=64), updated_utc TEXT NOT NULL)");
                SqlExecOn(connection, "CREATE TABLE build_set_documents (name TEXT NOT NULL, ordinal INTEGER NOT NULL CHECK(ordinal>=0), document_id TEXT NOT NULL, PRIMARY KEY(name,ordinal), UNIQUE(name,document_id), FOREIGN KEY(name) REFERENCES build_sets(name) ON DELETE RESTRICT, FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
                SqlExecOn(connection, "INSERT INTO documents(document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc) VALUES('topic-doc','topic','preserved.topic','Drafts/topics/preserved.json','DRAFTS/TOPICS/PRESERVED.JSON','" + hash + "','2026-10-24T00:00:00.0000000Z','2026-10-24T00:00:00.0000000Z')");
                SqlExecOn(connection, "INSERT INTO document_revisions(document_id,revision_hash,document_bytes,created_utc) VALUES('topic-doc','" + hash + "',X'" + BitConverter.ToString(bytes).Replace("-", string.Empty) + "','2026-10-24T00:00:00.0000000Z')");
                SqlExecOn(connection, "PRAGMA user_version=2");
            }
            finally { Dispose(connection); }

            object repository = NewRepositoryExisting(path);
            object info = Value(Call(repository, "ReadStoreInfo"));
            Assert(Convert.ToInt32(Get(info, "schemaVersion")) == 3, "Opening an authoring-v2 store must upgrade it to schema 3.");
            object read = Value(Call(repository, "Read", "topic-doc", ""));
            Assert(Equal(bytes, Get(read, "documentBytes")) && String(Get(read, "revisionHash")) == hash,
                "The v2 migration must preserve existing document revision bytes and hashes.");
            Dispose(repository);
        });
    }

    private static void ProcessWriters()
    {
        WithRoot(delegate(string root)
        {
            string path = Path.Combine(root, "authoring.sqlite3");
            object repository = NewRepository(path);
            byte[] initial = Encoding.UTF8.GetBytes("{\"value\":0}");
            object saved = Save(repository, "shared-doc", "topic", "Drafts/topics/shared-doc.json", "", "initial-process", initial);
            string expected = String(Get(Value(saved), "revisionHash"));
            Dispose(repository);
            string hostPath = Assembly.GetExecutingAssembly().Location;
            Assert(Environment.Is64BitProcess, "The standalone SQLite host must run in a 64-bit process for the packaged Windows x64 native library.");
            Process one = StartWriter(hostPath, _authoring.Location, path, expected, "writer-process-1", "{\"value\":1}");
            Process two = StartWriter(hostPath, _authoring.Location, path, expected, "writer-process-2", "{\"value\":2}");
            Assert(one.WaitForExit(30000) && two.WaitForExit(30000), "Concurrent writer processes must finish within 30 seconds.");
            string[] statuses = new[] { one.StandardOutput.ReadToEnd().Trim(), two.StandardOutput.ReadToEnd().Trim() };
            Assert(one.ExitCode == 0 && two.ExitCode == 0, "Writer process exit failed: " + one.StandardError.ReadToEnd() + " " + two.StandardError.ReadToEnd());
            Assert(statuses.Count(item => item == "ready") == 1 && statuses.Count(item => item == "conflict") == 1,
                "Exactly one process may commit the stale expected revision; statuses were " + string.Join(",", statuses));
        });
    }

    private static void ImportAllKindsAndIdempotentRetry()
    {
        WithRoot(delegate(string root)
        {
            string databasePath = Path.Combine(root, "authoring.sqlite3");
            string sourceRoot = Path.Combine(root, "legacy source 日本語");
            Directory.CreateDirectory(sourceRoot);
            object repository = NewRepository(databasePath);
            object info = StoreInfo(repository);
            List<ImportFixtureSource> sources = CreateAllImportSources(sourceRoot);
            object request = BuildImportRequest(sourceRoot, sources, String(Get(info, "storeId")), Convert.ToInt64(Get(info, "storeRevision")), "import-all-kinds");

            object imported = ImportOnce(repository, request);
            AssertStatus(imported, "ready");
            object receipt = Value(imported);
            Assert(Convert.ToInt32(Get(receipt, "sourceCount")) == sources.Count, "Every supported content and draft kind must be inventoried.");
            Assert(Convert.ToInt64(Get(receipt, "storeRevision")) == 1, "The batch must advance the shared store once.");
            Assert(Items(Get(receipt, "sources")).Count == sources.Count, "The receipt must preserve the source alias mapping.");
            foreach (ImportFixtureSource source in sources)
            {
                object document = Value(Call(repository, "Read", source.documentId, ""));
                Assert(Equal(source.documentBytes, Get(document, "documentBytes")), "Imported document bytes changed for " + source.kind + ".");
                Assert(String(Get(document, "sourceId")) == ExpectedImportedSourceId(source), "Imported stable source identity changed for " + source.kind + ".");
                Assert(Equal(source.documentBytes, File.ReadAllBytes(Path.Combine(sourceRoot, source.logicalPath.Replace('/', Path.DirectorySeparatorChar)))),
                    "Import must leave original JSON files byte-identical: " + source.logicalPath);
                byte[] original = SqlScalar<byte[]>(databasePath,
                    "SELECT original_bytes FROM import_sources WHERE operation_id=? AND source_path=?", "import-all-kinds", source.logicalPath);
                Assert(Equal(source.documentBytes, original), "Retained migration evidence changed for " + source.logicalPath + ".");
            }

            ImportFixtureSource raw = sources.First(item => item.kind == "legacy_sequence");
            object rawBefore = Value(Call(repository, "Read", raw.documentId, ""));
            string stableSourceId = String(Get(rawBefore, "sourceId"));
            string rawHash = String(Get(rawBefore, "revisionHash"));
            string renamedPath = "legacy/renamed-alias.json";
            object renamed = Save(repository, raw.documentId, raw.kind, renamedPath, rawHash, "rename-raw-source-alias", raw.documentBytes);
            AssertStatus(renamed, "ready");
            object renameReplay = Save(repository, raw.documentId, raw.kind, renamedPath, rawHash, "rename-raw-source-alias", raw.documentBytes);
            AssertStatus(renameReplay, "ready");
            Assert(String(Get(Value(renameReplay), "logicalPath")) == renamedPath && String(Get(Value(renameReplay), "sourceId")) == stableSourceId,
                "A retried alias rename must return the original saved alias and stable source identity.");
            object rawAfter = Value(Call(repository, "Read", raw.documentId, ""));
            Assert(String(Get(rawAfter, "logicalPath")) == renamedPath && String(Get(rawAfter, "sourceId")) == stableSourceId,
                "Renaming a logical-path alias must retain the stable document and source IDs.");

            ImportFixtureSource first = sources[0];
            File.Delete(Path.Combine(sourceRoot, first.logicalPath.Replace('/', Path.DirectorySeparatorChar)));
            object replay = ImportOnce(repository, request);
            AssertStatus(replay, "ready");
            Assert(StoreRevision(databasePath) == 2, "A receipt replay must not reread or rewrite source documents after the explicit alias rename.");
            Assert(Equal(first.documentBytes, Get(Value(Call(repository, "Read", first.documentId, "")), "documentBytes")),
                "Receipt replay must retain the original SQL revision after the JSON source disappears.");
            Dispose(repository);
        });
    }

    private static void ImportRejectsInvalidSources()
    {
        WithRoot(delegate(string root)
        {
            string databasePath = Path.Combine(root, "authoring.sqlite3");
            string sourceRoot = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceRoot);
            object repository = NewRepository(databasePath);
            object info = StoreInfo(repository);
            string storeId = String(Get(info, "storeId"));
            long storeRevision = Convert.ToInt64(Get(info, "storeRevision"));

            byte[] original = Encoding.UTF8.GetBytes("{\"unknown\":\"kept\"}");
            ImportFixtureSource changed = WriteImportSource(sourceRoot, "doc-changed", "topic", "Drafts/topic.json", original);
            object changedRequest = BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { changed }, storeId, storeRevision, "import-changed-bytes");
            byte[] changedBytes = Encoding.UTF8.GetBytes("{\"unknown\":\"modified\"}");
            File.WriteAllBytes(Path.Combine(sourceRoot, changed.logicalPath.Replace('/', Path.DirectorySeparatorChar)), changedBytes);
            AssertStatus(ImportOnce(repository, changedRequest), "invalid");

            byte[] truncated = Encoding.UTF8.GetBytes("{\"truncated\":");
            ImportFixtureSource malformed = WriteImportSource(sourceRoot, "doc-truncated", "scenario", "Drafts/truncated.json", truncated);
            AssertStatus(ImportOnce(repository, BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { malformed }, storeId, storeRevision, "import-truncated")), "invalid");

            ImportFixtureSource duplicateIdA = WriteImportSource(sourceRoot, "duplicate-id", "topic", "A/one.json", Encoding.UTF8.GetBytes("{}"));
            ImportFixtureSource duplicateIdB = WriteImportSource(sourceRoot, "duplicate-id", "scenario", "B/two.json", Encoding.UTF8.GetBytes("{}"));
            AssertStatus(ImportOnce(repository, BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { duplicateIdA, duplicateIdB }, storeId, storeRevision, "import-duplicate-id")), "invalid");

            ImportFixtureSource duplicatePathA = WriteImportSource(sourceRoot, "path-a", "topic", "Same/entry.json", Encoding.UTF8.GetBytes("{}"));
            ImportFixtureSource duplicatePathB = new ImportFixtureSource
            {
                documentId = "path-b", kind = "scenario", logicalPath = "same/ENTRY.json", documentBytes = duplicatePathA.documentBytes,
                sourceHash = duplicatePathA.sourceHash
            };
            AssertStatus(ImportOnce(repository, BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { duplicatePathA, duplicatePathB }, storeId, storeRevision, "import-duplicate-path")), "invalid");

            ImportFixtureSource unsafePath = new ImportFixtureSource
            {
                documentId = "unsafe-path", kind = "topic", logicalPath = "../outside.json",
                documentBytes = Encoding.UTF8.GetBytes("{}"), sourceHash = Sha256(Encoding.UTF8.GetBytes("{}"))
            };
            AssertStatus(ImportOnce(repository, BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { unsafePath }, storeId, storeRevision, "import-unsafe-path")), "invalid");

            Assert(StoreRevision(databasePath) == 0, "Rejected inventories must not advance the store revision.");
            Assert(SqlScalar<int>(databasePath, "SELECT COUNT(*) FROM documents") == 0, "Rejected inventories must not create any document heads.");
            Assert(SqlScalar<int>(databasePath, "SELECT COUNT(*) FROM import_receipts") == 0, "Rejected preflight requests must not leave partial receipts.");
            Dispose(repository);
        });
    }

    private static void ImportRollbackAtomicity()
    {
        WithRoot(delegate(string root)
        {
            string databasePath = Path.Combine(root, "authoring.sqlite3");
            string sourceRoot = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceRoot);
            object repository = NewRepository(databasePath);
            object info = StoreInfo(repository);
            ImportFixtureSource source = WriteImportSource(sourceRoot, "rollback-doc", "topic", "topic.json", Encoding.UTF8.GetBytes("{\"unknown\":true}"));
            object request = BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { source }, String(Get(info, "storeId")), 0, "import-retry-after-fault");

            SqlExec(databasePath, "CREATE TRIGGER inject_import_failure BEFORE INSERT ON import_sources BEGIN SELECT RAISE(ABORT,'injected failure'); END");
            AssertStatus(ImportOnce(repository, request), "invalid");
            Assert(StoreRevision(databasePath) == 0, "A failed import batch must not advance store revision.");
            Assert(SqlScalar<int>(databasePath, "SELECT COUNT(*) FROM documents") == 0, "A failed batch must roll back document heads.");
            Assert(SqlScalar<int>(databasePath, "SELECT COUNT(*) FROM document_revisions") == 0, "A failed batch must roll back immutable revisions.");
            Assert(SqlScalar<int>(databasePath, "SELECT COUNT(*) FROM import_receipts") == 0, "A failed batch must roll back its receipt.");
            SqlExec(databasePath, "DROP TRIGGER inject_import_failure");

            AssertStatus(ImportOnce(repository, request), "ready");
            Assert(StoreRevision(databasePath) == 1, "Retry after rollback must commit exactly one batch revision.");
            Dispose(repository);
        });
    }

    private static void ImportStoreRevisionCas()
    {
        WithRoot(delegate(string root)
        {
            string databasePath = Path.Combine(root, "authoring.sqlite3");
            string sourceRoot = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceRoot);
            object repository = NewRepository(databasePath);
            object info = StoreInfo(repository);
            ImportFixtureSource source = WriteImportSource(sourceRoot, "cas-doc", "topic", "topic.json", Encoding.UTF8.GetBytes("{\"value\":\"legacy\"}"));
            object firstRequest = BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { source }, String(Get(info, "storeId")), 0, "import-cas-first");
            AssertStatus(ImportOnce(repository, firstRequest), "ready");

            object current = Value(Call(repository, "Read", source.documentId, ""));
            string currentHash = String(Get(current, "revisionHash"));
            byte[] sqlEdit = Encoding.UTF8.GetBytes("{\"value\":\"sql-edit\"}");
            AssertStatus(Save(repository, source.documentId, source.kind, source.logicalPath, currentHash, "sql-edit-after-import", sqlEdit), "ready");

            object staleRequest = BuildImportRequest(sourceRoot, new List<ImportFixtureSource> { source }, String(Get(info, "storeId")), 1, "import-cas-stale");
            AssertStatus(ImportOnce(repository, staleRequest), "conflict");
            Assert(Equal(sqlEdit, Get(Value(Call(repository, "Read", source.documentId, "")), "documentBytes")),
                "A stale import must not overwrite a later SQL edit.");
            Dispose(repository);
        });
    }

    private static void InterchangeRoundTrip()
    {
        WithRoot(delegate(string root)
        {
            string sourcePath = Path.Combine(root, "source-store", "authoring.sqlite3");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
            object sourceRepository = NewRepository(sourcePath);
            byte[] draftBytes = Encoding.UTF8.GetBytes("{\"status\":\"draft\",\"document\":{\"topicId\":\"topic.export\",\"scenarios\":[]},\"unknown\":{\"unicode\":\"こんにちは\"}}");
            byte[] rawBytes = Encoding.UTF8.GetBytes("{\"metadata\":{\"note\":\"保留\"},\"nodes\":[]}");
            AssertStatus(Save(sourceRepository, "draft-doc", "topic", "Drafts/topics/export.json", "", "export-draft", draftBytes), "ready");
            AssertStatus(Save(sourceRepository, "raw-doc", "legacy_sequence", "legacy/export.json", "", "export-raw", rawBytes), "ready");
            object sourceInfo = StoreInfo(sourceRepository);

            string exportRootA = Path.Combine(root, "exports-a");
            string exportRootB = Path.Combine(root, "exports-b");
            Directory.CreateDirectory(exportRootA);
            Directory.CreateDirectory(exportRootB);
            object exportRequest = New("AuthoringExportRequest");
            Set(exportRequest, "documentIds", new[] { "raw-doc", "draft-doc" });
            Set(exportRequest, "expectedStoreId", String(Get(sourceInfo, "storeId")));
            Set(exportRequest, "expectedStoreRevision", Convert.ToInt64(Get(sourceInfo, "storeRevision")));
            Set(exportRequest, "destinationRoot", exportRootA);
            Type exporter = _authoring.GetType("AlterOS.Authoring.AuthoringJsonExporter", true);
            object firstExport = StaticCall(exporter, "Export", sourceRepository, exportRequest);
            AssertStatus(firstExport, "ready");
            object firstReceipt = Value(firstExport);
            string firstDirectory = String(Get(firstReceipt, "directoryPath"));
            Assert(Path.GetFileName(firstDirectory) == String(Get(firstReceipt, "snapshotHash")), "Exports must use their exact snapshot hash as the directory name.");
            Assert(Equal(draftBytes, File.ReadAllBytes(Path.Combine(firstDirectory, "originals", "doc-00001.json"))),
                "The exact complete draft wrapper bytes were not retained in the export.");
            Assert(Equal(rawBytes, File.ReadAllBytes(Path.Combine(firstDirectory, "originals", "doc-00002.json"))),
                "The exact raw source bytes were not retained in the export.");
            Assert(File.ReadAllText(Path.Combine(firstDirectory, "documents", "Drafts", "topics", "export.json"), Encoding.UTF8)
                .Contains("unicode"), "The readable export omitted unknown wrapper metadata.");

            Set(exportRequest, "destinationRoot", exportRootB);
            object secondExport = StaticCall(exporter, "Export", sourceRepository, exportRequest);
            AssertStatus(secondExport, "ready");
            string secondDirectory = String(Get(Value(secondExport), "directoryPath"));
            Assert(DirectoryTreesEqual(firstDirectory, secondDirectory), "The same exact snapshot did not produce a byte-identical deterministic export.");
            Set(exportRequest, "destinationRoot", exportRootA);
            object replay = StaticCall(exporter, "Export", sourceRepository, exportRequest);
            AssertStatus(replay, "ready");
            Assert(Convert.ToBoolean(Get(Value(replay), "reusedExistingExport")), "An identical hash-named export retry must reuse the verified existing directory.");

            string freshPath = Path.Combine(root, "fresh-store", "authoring.sqlite3");
            Directory.CreateDirectory(Path.GetDirectoryName(freshPath));
            object fresh = NewRepository(freshPath);
            object freshInfo = StoreInfo(fresh);
            object importRequestResult = StaticCall(exporter, "CreateImportRequest", firstDirectory,
                String(Get(freshInfo, "storeId")), Convert.ToInt64(Get(freshInfo, "storeRevision")), "explicit-fresh-import");
            AssertStatus(importRequestResult, "ready");
            object imported = ImportOnce(fresh, Value(importRequestResult));
            AssertStatus(imported, "ready");
            Assert(Equal(draftBytes, Get(Value(Call(fresh, "Read", "draft-doc", "")), "documentBytes")) &&
                Equal(rawBytes, Get(Value(Call(fresh, "Read", "raw-doc", "")), "documentBytes")) &&
                String(Get(Value(Call(fresh, "Read", "draft-doc", "")), "sourceId")) == "topic.export" &&
                String(Get(Value(Call(fresh, "Read", "raw-doc", "")), "sourceId")) == "raw-doc",
                "An explicit fresh-store import changed exact bytes, IDs or path aliases.");

            string conflictPath = Path.Combine(root, "conflict-store", "authoring.sqlite3");
            Directory.CreateDirectory(Path.GetDirectoryName(conflictPath));
            object conflictStore = NewRepository(conflictPath);
            byte[] conflictingDraft = Encoding.UTF8.GetBytes("{\"document\":{\"topicId\":\"topic.conflict\"}}");
            AssertStatus(Save(conflictStore, "draft-doc", "topic", "Drafts/topics/export.json", "", "conflict-target", conflictingDraft), "ready");
            object conflictInfo = StoreInfo(conflictStore);
            object conflictRequest = StaticCall(exporter, "CreateImportRequest", firstDirectory,
                String(Get(conflictInfo, "storeId")), Convert.ToInt64(Get(conflictInfo, "storeRevision")), "conflicting-import");
            AssertStatus(conflictRequest, "ready");
            AssertStatus(ImportOnce(conflictStore, Value(conflictRequest)), "conflict");
            Assert(Equal(conflictingDraft, Get(Value(Call(conflictStore, "Read", "draft-doc", "")), "documentBytes")) &&
                SqlScalar<int>(conflictPath, "SELECT COUNT(*) FROM documents") == 1,
                "A conflicting target must remain unchanged and the rest of the import batch must not be installed.");

            string readableFile = Path.Combine(firstDirectory, "documents", "Drafts", "topics", "export.json");
            File.WriteAllText(readableFile, "corrupt export", Encoding.UTF8);
            object damagedImportRequest = StaticCall(exporter, "CreateImportRequest", firstDirectory,
                String(Get(StoreInfo(fresh), "storeId")), Convert.ToInt64(Get(StoreInfo(fresh), "storeRevision")), "damaged-export-import");
            AssertStatus(damagedImportRequest, "recovery_required");
            Set(exportRequest, "destinationRoot", exportRootA);
            AssertStatus(StaticCall(exporter, "Export", sourceRepository, exportRequest), "conflict");

            string blockedRoot = Path.Combine(root, "blocked-exports");
            Directory.CreateDirectory(blockedRoot);
            string blockedPath = Path.Combine(blockedRoot, String(Get(firstReceipt, "snapshotHash")));
            File.WriteAllText(blockedPath, "preserve blocker", Encoding.UTF8);
            Set(exportRequest, "destinationRoot", blockedRoot);
            AssertStatus(StaticCall(exporter, "Export", sourceRepository, exportRequest), "conflict");
            Assert(Directory.GetFiles(blockedRoot).Length == 1, "A failed export must not leave an advertised or partial snapshot directory.");

            Dispose(conflictStore);
            Dispose(fresh);
            Dispose(sourceRepository);
        });
    }

    private static void BackupReopens()
    {
        WithRoot(delegate(string root)
        {
            string databasePath = Path.Combine(root, "backup-source", "authoring.sqlite3");
            string backupPath = Path.Combine(root, "backups", "authoring.sqlite3");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            object repository = NewRepository(databasePath);
            byte[] bytes = Encoding.UTF8.GetBytes("{\"id\":\"generic.backup\",\"unknown\":\"kept\"}");
            AssertStatus(Save(repository, "backup-doc", "generic_dialogue", "Generic/backup.json", "", "backup-save", bytes), "ready");
            AssertStatus(Call(repository, "CreateBackup", backupPath), "ready");
            object reopened = NewRepositoryExisting(backupPath);
            Assert(Equal(bytes, Get(Value(Call(reopened, "Read", "backup-doc", "")), "documentBytes")) &&
                String(Get(StoreInfo(repository), "storeId")) == String(Get(StoreInfo(reopened), "storeId")),
                "A consistent SQLite backup must reopen with the same exact revisions and store identity.");
            Dispose(reopened);
            Dispose(repository);
        });
    }

    private static bool DirectoryTreesEqual(string left, string right)
    {
        string[] leftFiles = Directory.GetFiles(left, "*", SearchOption.AllDirectories)
            .Select(path => path.Substring(left.Length + 1)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] rightFiles = Directory.GetFiles(right, "*", SearchOption.AllDirectories)
            .Select(path => path.Substring(right.Length + 1)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal)) return false;
        foreach (string relative in leftFiles)
            if (!Equal(File.ReadAllBytes(Path.Combine(left, relative)), File.ReadAllBytes(Path.Combine(right, relative)))) return false;
        return true;
    }

    private static object StoreInfo(object repository)
    {
        object result = Call(repository, "ReadStoreInfo");
        AssertStatus(result, "ready");
        return Value(result);
    }

    private static void VerifyConnectionConfiguration(object repository)
    {
        object connection = GetPrivate(repository, "_connection");
        Assert((TimeSpan)Get(connection, "BusyTimeout") == TimeSpan.FromSeconds(5), "Every repository connection must use the bounded five-second busy timeout.");
        Assert(SqlScalarOn<int>(connection, "PRAGMA foreign_keys") == 1, "Foreign-key enforcement must be enabled on every connection.");
        Assert(SqlScalarOn<int>(connection, "PRAGMA synchronous") == 2, "Durable authoring connections must use FULL synchronous commits.");
    }

    private static object GetPrivate(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        return field.GetValue(target);
    }

    private static T SqlScalarOn<T>(object connection, string sql)
    {
        MethodInfo method = connection.GetType().GetMethods().First(item => item.Name == "ExecuteScalar" && item.IsGenericMethod && item.GetParameters().Length == 2).MakeGenericMethod(typeof(T));
        return (T)Invoke(method, connection, new object[] { sql, new object[0] });
    }

    private static object ImportOnce(object repository, object request)
    {
        Type importer = _authoring.GetType("AlterOS.Authoring.AuthoringJsonImporter", true);
        return StaticCall(importer, "ImportOnce", repository, request);
    }

    private static object BuildImportRequest(string sourceRoot, List<ImportFixtureSource> sources, string storeId, long storeRevision, string operationId)
    {
        object request = New("AuthoringImportRequest");
        Set(request, "sourceRoot", sourceRoot);
        Set(request, "expectedStoreId", storeId);
        Set(request, "expectedStoreRevision", storeRevision);
        Set(request, "operationId", operationId);
        Type sourceType = _authoring.GetType("AlterOS.Authoring.AuthoringImportSource", true);
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(sourceType);
        IList typedSources = (IList)Activator.CreateInstance(listType);
        foreach (ImportFixtureSource source in sources)
        {
            object item = Activator.CreateInstance(sourceType);
            Set(item, "documentId", source.documentId);
            Set(item, "kind", source.kind);
            Set(item, "logicalPath", source.logicalPath);
            Set(item, "sourceHash", source.sourceHash);
            typedSources.Add(item);
        }
        Set(request, "sources", typedSources);
        return request;
    }

    private static List<ImportFixtureSource> CreateAllImportSources(string sourceRoot)
    {
        string[] kinds =
        {
            "generic_dialogue", "topic_definition", "scenario_definition", "legacy_sequence", "ambient_pool",
            "activity_scenario_library", "activity_greeting_menu", "conversation_coverage", "topic_migration_map",
            "onboarding_steps", "topic", "scenario"
        };
        var sources = new List<ImportFixtureSource>();
        for (int index = 0; index < kinds.Length; index++)
        {
            string kind = kinds[index];
            string documentId = "legacy-" + kind.Replace('_', '-') + "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string logicalPath = "legacy/" + kind + "/entry-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json";
            string identityField = kind == "generic_dialogue" ? "id" :
                (kind == "topic_definition" || kind == "topic") ? "topicId" :
                (kind == "scenario_definition" || kind == "scenario") ? "scenarioId" : string.Empty;
            string identity = identityField.Length == 0 ? string.Empty : "\"" + identityField + "\":\"" + documentId + "\",";
            string json = kind == "topic" || kind == "scenario"
                ? "{\"document\":{" + identity + "\"unknown\":{\"keep\":true}},\"layout\":{\"node\":\"stable-node\"}," +
                    "\"texts\":{\"en\":\"Hello\",\"ja\":\"こんにちは\",\"id\":\"Halo\"},\"outsideUnknown\":[1,2,3]}"
                : "{" + identity + "\"kind\":\"" + kind + "\",\"linkedIds\":[\"stable-link-1\"]," +
                    "\"texts\":{\"en\":\"Hello\",\"ja\":\"こんにちは\",\"id\":\"Halo\"},\"unknown\":{\"keep\":true,\"nested\":[1,2,3]}}";
            sources.Add(WriteImportSource(sourceRoot, documentId, kind, logicalPath, Encoding.UTF8.GetBytes(json)));
        }
        return sources;
    }

    private static string ExpectedImportedSourceId(ImportFixtureSource source)
    {
        if (source.kind == "legacy_sequence" || source.kind == "ambient_pool" || source.kind == "activity_scenario_library" ||
            source.kind == "activity_greeting_menu" || source.kind == "conversation_coverage" ||
            source.kind == "topic_migration_map" || source.kind == "onboarding_steps")
            return "legacy_" + Sha256(Encoding.UTF8.GetBytes(source.logicalPath)).Substring(0, 40);
        return source.documentId;
    }

    private static ImportFixtureSource WriteImportSource(string sourceRoot, string documentId, string kind, string logicalPath, byte[] bytes)
    {
        string fullPath = Path.Combine(sourceRoot, logicalPath.Replace('/', Path.DirectorySeparatorChar));
        string parent = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(fullPath, bytes);
        return new ImportFixtureSource
        {
            documentId = documentId,
            kind = kind,
            logicalPath = logicalPath,
            documentBytes = (byte[])bytes.Clone(),
            sourceHash = Sha256(bytes)
        };
    }

    private sealed class ImportFixtureSource
    {
        public string documentId;
        public string kind;
        public string logicalPath;
        public byte[] documentBytes;
        public string sourceHash;
    }

    private static Process StartWriter(string hostPath, string authoringAssembly, string databasePath, string expected, string operation, string json)
    {
        var info = new ProcessStartInfo
        {
            FileName = hostPath,
            WorkingDirectory = Path.GetDirectoryName(hostPath),
            Arguments = "--writer " + Quote(authoringAssembly) + " " + Quote(databasePath) +
                " shared-doc " + Quote(expected) + " " + Quote(operation) + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        return Process.Start(info);
    }

    private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

    private static object NewRepository(string path)
    {
        object result = StaticCall(_repository, "CreateNew", path);
        AssertStatus(result, "ready");
        return Value(result);
    }

    private static object NewRepositoryExisting(string path)
    {
        object result = StaticCall(_repository, "OpenExisting", path);
        AssertStatus(result, "ready");
        return Value(result);
    }

    private static object Save(object repository, string id, string kind, string logicalPath, string expectedHash, string operationId, byte[] bytes)
    {
        object request = New("AuthoringSaveRequest");
        Set(request, "documentId", id);
        Set(request, "kind", kind);
        Set(request, "logicalPath", logicalPath);
        Set(request, "expectedHash", expectedHash);
        Set(request, "operationId", operationId);
        Set(request, "documentBytes", bytes);
        return Call(repository, "Save", request);
    }

    private static object New(string name)
    {
        Type type = _authoring.GetType("AlterOS.Authoring." + name, true);
        return Activator.CreateInstance(type);
    }

    private static object StaticCall(Type type, string method, params object[] args)
    {
        MethodInfo selected = type.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(item => item.Name == method && item.GetParameters().Length == args.Length);
        if (selected == null) throw new MissingMethodException(type.FullName, method + " (" + args.Length + " args)");
        return Invoke(selected, null, args);
    }

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo selected = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(item => item.Name == method && item.GetParameters().Length == args.Length);
        if (selected == null) throw new MissingMethodException(target.GetType().FullName, method + " (" + args.Length + " args)");
        return Invoke(selected, target, args);
    }

    private static object Invoke(MethodInfo method, object target, object[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException exception) { throw exception.GetBaseException(); }
    }

    private static object Value(object result) { return Get(result, "value"); }
    private static string Status(object result) { return String(Get(result, "status")); }
    private static void AssertStatus(object result, string expected)
    { Assert(Status(result) == expected, "Expected status " + expected + ", got " + Status(result) + Diagnostics(result)); }

    private static string Diagnostics(object result)
    {
        object values = Get(result, "diagnostics");
        if (values == null) return string.Empty;
        return " [" + string.Join(";", Items(values).Cast<object>().Select(item => String(Get(item, "code")) + ":" + String(Get(item, "message")))) + "]";
    }

    private static object Get(object target, string name)
    {
        if (target == null) return null;
        Type type = target.GetType();
        FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field != null) return field.GetValue(target);
        PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property != null) return property.GetValue(target, null);
        throw new MissingMemberException(type.FullName, name);
    }

    private static void Set(object target, string name, object value)
    {
        Type type = target.GetType();
        FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field != null) { field.SetValue(target, value); return; }
        PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property != null) { property.SetValue(target, value, null); return; }
        throw new MissingMemberException(type.FullName, name);
    }

    private static string String(object value) { return value == null ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture); }
    private static ArrayList Items(object value)
    {
        var output = new ArrayList();
        IEnumerable items = value as IEnumerable;
        if (items != null) foreach (object item in items) output.Add(item);
        return output;
    }

    private static bool Equal(byte[] left, object right)
    { return left.SequenceEqual(right as byte[] ?? new byte[0]); }

    private static string Sha256(byte[] bytes)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static long StoreRevision(string path)
    { return Convert.ToInt64(SqlScalar<string>(path, "SELECT value FROM metadata WHERE key='store_revision'"), System.Globalization.CultureInfo.InvariantCulture); }

    private static T SqlScalar<T>(string path, string sql, params object[] arguments)
    {
        object connection = OpenSqlite(path, false);
        try
        {
            MethodInfo method = connection.GetType().GetMethods().First(item => item.Name == "ExecuteScalar" && item.IsGenericMethod && item.GetParameters().Length == 2).MakeGenericMethod(typeof(T));
            return (T)Invoke(method, connection, new object[] { sql, arguments ?? new object[0] });
        }
        finally { Dispose(connection); }
    }

    private static void SqlExec(string path, string sql)
    {
        object connection = OpenSqlite(path, false);
        try
        {
            MethodInfo method = connection.GetType().GetMethods().First(item => item.Name == "Execute" && item.GetParameters().Length == 2);
            Invoke(method, connection, new object[] { sql, new object[0] });
        }
        finally { Dispose(connection); }
    }

    private static object OpenSqlite(string path, bool create)
    {
        Type connectionType = _authoring.GetType("SQLite.SQLiteConnection", true);
        Type flagsType = _authoring.GetType("SQLite.SQLiteOpenFlags", true);
        object flags = Enum.Parse(flagsType, create ? "ReadWrite, Create" : "ReadWrite");
        ConstructorInfo constructor = connectionType.GetConstructor(new[] { typeof(string), flagsType, typeof(bool) });
        return constructor.Invoke(new[] { (object)path, flags, (object)true });
    }

    private static void CreateFutureDatabase(string path)
    {
        object connection = OpenSqlite(path, true);
        try
        {
            SqlExecOn(connection, "CREATE TABLE metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
            SqlExecOn(connection, "INSERT INTO metadata(key,value) VALUES('schema_version','99'),('store_id','11111111-1111-4111-8111-111111111111'),('store_revision','0')");
        }
        finally { Dispose(connection); }
    }

    private static void SqlExecOn(object connection, string sql)
    {
        MethodInfo method = connection.GetType().GetMethods().First(item => item.Name == "Execute" && item.GetParameters().Length == 2);
        Invoke(method, connection, new object[] { sql, new object[0] });
    }

    private static void Dispose(object target)
    {
        if (target == null) return;
        MethodInfo method = target.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method != null) Invoke(method, target, new object[0]);
    }

    private static void WithRoot(Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "AlterOS Authoring 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { body(root); }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { _failed++; Console.WriteLine("FAIL " + name + ": " + exception.GetBaseException().Message); Console.WriteLine(exception.GetBaseException().StackTrace); }
    }
}
