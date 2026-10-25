using System;
using System.IO;
using System.Reflection;
using System.Text;
using AlterOS.Persistence;
using NUnit.Framework;
using SQLite;
using UnityEngine;

namespace AlterOS.Tests.Persistence
{
    public sealed class ProgressionStorePersistenceTests
    {
        private string root;
        private GameObject host;
        private object store;
        private Type storeType;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS Progression Store " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void LegacyImportPersistsLocallyAndSurvivesSyntheticRestart()
        {
            byte[] legacy = Encoding.UTF8.GetBytes(CreateFullSchema4Fixture());
            File.WriteAllBytes(Path.Combine(root, "alteros_progression.json"), legacy);
            CreateStore();
            Assert.IsTrue(Get<bool>("IsInitialized"));
            Assert.AreEqual(77, GetStateField("player", "credits"));
            Assert.IsFalse((bool)GetStateField("altera", "hydrationKnown"));
            CollectionAssert.AreEqual(legacy, File.ReadAllBytes(Path.Combine(root, "alteros_progression.json")));

            UnityEngine.Object.DestroyImmediate(host);
            host = null;
            CreateStore();
            Assert.IsTrue(Get<bool>("IsInitialized"));
            Assert.AreEqual(77, GetStateField("player", "credits"));
            Assert.IsFalse((bool)GetStateField("altera", "hydrationKnown"));
            Assert.AreEqual("legacy.fixture", GetStateField("flags", "0", "key"));
        }

        [Test]
        public void CorruptDatabaseDoesNotPublishDefaultsOrReplaceLegacyBytes()
        {
            string databasePath = Path.Combine(root, "Data", "alteros-game.sqlite3");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            byte[] corrupt = { 0, 1, 2, 255, 4, 5 };
            File.WriteAllBytes(databasePath, corrupt);
            byte[] legacy = Encoding.UTF8.GetBytes("{\"schemaVersion\":4,\"player\":{\"credits\":9}}");
            File.WriteAllBytes(Path.Combine(root, "alteros_progression.json"), legacy);

            CreateStore();
            Assert.IsFalse(Get<bool>("IsInitialized"));
            Assert.IsNull(GetState());
            Assert.AreEqual("recovery_required", Get<string>("PersistenceStatus"));
            Assert.IsFalse((bool)Invoke("TryApplyActivityHydrationCheckpoint", Guid.NewGuid().ToString("D"), 5f));
            CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(databasePath));
            CollectionAssert.AreEqual(legacy, File.ReadAllBytes(Path.Combine(root, "alteros_progression.json")));
        }

        [Test]
        public void FailedDiscreteCheckpointDoesNotLeakIntoLaterSave()
        {
            CreateStore();
            SetStateField("altera", "hydrationKnown", true);
            SetStateField("altera", "hydration", 50f);
            Invoke("Save");
            string databasePath = Path.Combine(root, "Data", "alteros-game.sqlite3");
            using (var connection = new SQLiteConnection(databasePath))
                connection.Execute("CREATE TRIGGER synthetic_checkpoint_abort BEFORE INSERT ON operation_receipts BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");

            bool applied = (bool)Invoke("TryApplyActivityHydrationCheckpoint", Guid.NewGuid().ToString("D"), 10f);
            Assert.IsFalse(applied);
            Assert.AreEqual(50f, GetStateField("altera", "hydration"));
            using (var connection = new SQLiteConnection(databasePath))
                connection.Execute("DROP TRIGGER synthetic_checkpoint_abort");
            Invoke("Save");

            UnityEngine.Object.DestroyImmediate(host);
            host = null;
            CreateStore();
            Assert.AreEqual(50f, GetStateField("altera", "hydration"));
        }

        [Test]
        public void IncompleteSchema4SnapshotFailsClosedAndPreservesExactJson()
        {
            string databasePath = Path.Combine(root, "Data", "alteros-game.sqlite3");
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult initial = repository.Load();
            const string incomplete = "{\"schemaVersion\":4,\"player\":{\"credits\":5}}";
            GameCommitResult seeded = repository.Commit(new GameCommitRequest
            {
                operationId = Guid.NewGuid().ToString("D"),
                operationPayload = "synthetic-incomplete-schema4",
                expectedGameRevision = initial.gameRevision,
                progressionJson = incomplete
            });
            Assert.AreEqual(GamePersistenceStates.Committed, seeded.state);
            long revision = seeded.receipt.gameRevision;

            CreateStore();

            Assert.IsFalse(Get<bool>("IsInitialized"));
            Assert.IsNull(GetState());
            Assert.AreEqual("recovery_required", Get<string>("PersistenceStatus"));
            GameLoadResult after = repository.Load();
            Assert.AreEqual(revision, after.gameRevision);
            Assert.AreEqual(incomplete, after.progressionJson);
        }

        [Test]
        public void MalformedSchemaHeaderFailsClosedAndPreservesExactJson()
        {
            string databasePath = Path.Combine(root, "Data", "alteros-game.sqlite3");
            var repository = new SqliteGameRepository(databasePath);
            GameLoadResult initial = repository.Load();
            const string malformed = "{\"schemaVersion\":\"four\",\"player\":{\"credits\":5}}";
            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Execute("INSERT INTO game_snapshots(domain,revision,payload_json) VALUES (?,?,?)",
                    "progression", 1, malformed);
                connection.Execute("UPDATE game_meta SET value=? WHERE key=?", "1", "game_revision");
            }

            CreateStore();

            Assert.IsFalse(Get<bool>("IsInitialized"));
            Assert.IsNull(GetState());
            Assert.AreEqual("recovery_required", Get<string>("PersistenceStatus"));
            GameLoadResult after = repository.Load();
            Assert.AreEqual(initial.gameRevision + 1, after.gameRevision);
            Assert.AreEqual(malformed, after.progressionJson);
        }

        [Test]
        public void DialogueEffectsRejectCreditsAndUnboundedDeltasButAllowSmallChange()
        {
            CreateStore();
            long revision = Get<long>("DurableGameRevision");
            object credit = CreateRecord("AlterOS.Dialogue.StatDelta", new System.Collections.Generic.Dictionary<string, object>
            {
                ["target"] = "player", ["stat"] = "credits", ["amount"] = 25f, ["op"] = "add"
            });
            object unbounded = CreateRecord("AlterOS.Dialogue.StatDelta", new System.Collections.Generic.Dictionary<string, object>
            {
                ["target"] = "altera", ["stat"] = "affinity", ["amount"] = 1000000f, ["op"] = "add"
            });

            object creditResult = Invoke("TryApplyDialogueChanges", Guid.NewGuid().ToString("D"),
                CreateList("AlterOS.Dialogue.StatDelta", credit), null);
            object unboundedResult = Invoke("TryApplyDialogueChanges", Guid.NewGuid().ToString("D"),
                CreateList("AlterOS.Dialogue.StatDelta", unbounded), null);
            Assert.AreEqual(GamePersistenceStates.Invalid, creditResult.GetType().GetField("state").GetValue(creditResult));
            Assert.AreEqual(GamePersistenceStates.Invalid, unboundedResult.GetType().GetField("state").GetValue(unboundedResult));
            Assert.AreEqual(revision, Get<long>("DurableGameRevision"));
            Assert.AreEqual(0, GetStateField("player", "credits"));
            Assert.AreEqual(0f, GetStateField("altera", "affinity"));

            object bounded = CreateRecord("AlterOS.Dialogue.StatDelta", new System.Collections.Generic.Dictionary<string, object>
            {
                ["target"] = "altera", ["stat"] = "affinity", ["amount"] = 0.5f, ["op"] = "add"
            });
            object validResult = Invoke("TryApplyDialogueChanges", Guid.NewGuid().ToString("D"),
                CreateList("AlterOS.Dialogue.StatDelta", bounded), null);
            Assert.AreEqual(GamePersistenceStates.Committed, validResult.GetType().GetField("state").GetValue(validResult));
            Assert.AreEqual(0.5f, GetStateField("altera", "affinity"));
        }

        [Test]
        public void UntrustedSequenceCannotApplyMechanicalEffects()
        {
            CreateStore();
            long revision = Get<long>("DurableGameRevision");
            System.Type sequenceType = StatsTestAccess.Find("AlterOS.Dialogue.DialogueSequence");
            object sequence = Activator.CreateInstance(sequenceType);
            object metadata = sequenceType.GetField("metadata").GetValue(sequence);
            object delta = CreateRecord("AlterOS.Dialogue.StatDelta", new System.Collections.Generic.Dictionary<string, object>
            {
                ["target"] = "altera", ["stat"] = "affinity", ["amount"] = 0.5f, ["op"] = "add"
            });
            metadata.GetType().GetField("statChanges").SetValue(metadata, CreateList("AlterOS.Dialogue.StatDelta", delta));

            System.Type applierType = StatsTestAccess.Find("AlterOS.Progression.DialogueEffectApplier");
            object applier = host.AddComponent(applierType);
            applierType.GetField("progressionStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(applier, store);
            System.Reflection.MethodInfo apply = applierType.GetMethod("TryApplySequence",
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(string), sequenceType, typeof(bool) }, null);
            bool applied = (bool)apply.Invoke(applier, new object[] { Guid.NewGuid().ToString("D"), sequence, false });

            Assert.IsFalse(applied);
            Assert.AreEqual(0f, GetStateField("altera", "affinity"));
            Assert.AreEqual(revision, Get<long>("DurableGameRevision"));
        }

        private void CreateStore()
        {
            host = new GameObject("synthetic-progression-store");
            host.SetActive(false);
            storeType = StatsTestAccess.Find("AlterOS.Progression.AlteraProgressionStore");
            store = host.AddComponent(storeType);
            storeType.GetField("testPersistenceRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(store, root);
            storeType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public).Invoke(store, null);
        }

        private object Invoke(string method, params object[] arguments)
        {
            return Array.Find(storeType.GetMethods(), candidate => candidate.Name == method &&
                candidate.GetParameters().Length == arguments.Length).Invoke(store, arguments);
        }

        private T Get<T>(string property)
        {
            return (T)storeType.GetProperty(property).GetValue(store, null);
        }

        private object GetState()
        {
            return storeType.GetProperty("State").GetValue(store, null);
        }

        private object GetStateField(string field)
        {
            return GetState().GetType().GetField(field).GetValue(GetState());
        }

        private object GetStateField(string outerField, string index, string innerField)
        {
            var list = (System.Collections.IList)GetStateField(outerField);
            object value = list[int.Parse(index)];
            return value.GetType().GetField(innerField).GetValue(value);
        }

        private object GetStateField(string outerField, string innerField)
        {
            object outer = GetStateField(outerField);
            return outer.GetType().GetField(innerField).GetValue(outer);
        }

        private void SetStateField(string outerField, string field, object value)
        {
            object state = GetStateField(outerField);
            state.GetType().GetField(field).SetValue(state, value);
        }

        private static object CreateRecord(string typeName, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Type type = StatsTestAccess.Find(typeName);
            object value = Activator.CreateInstance(type);
            foreach (var field in fields) type.GetField(field.Key).SetValue(value, field.Value);
            return value;
        }

        private static object CreateList(string typeName, params object[] values)
        {
            System.Type elementType = StatsTestAccess.Find(typeName);
            System.Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
            var result = (System.Collections.IList)Activator.CreateInstance(listType);
            foreach (object value in values) result.Add(value);
            return result;
        }

        private static string CreateFullSchema4Fixture()
        {
            System.Type stateType = StatsTestAccess.Find("AlterOS.Progression.AlterOSProgressionState");
            object state = Activator.CreateInstance(stateType);
            stateType.GetField("lastUpdatedUtc").SetValue(state, string.Empty);
            object player = stateType.GetField("player").GetValue(state);
            player.GetType().GetField("credits").SetValue(player, 77);
            player.GetType().GetField("currentMode").SetValue(player, "free");
            object altera = stateType.GetField("altera").GetValue(state);
            altera.GetType().GetField("hydration").SetValue(altera, 0f);
            altera.GetType().GetField("hydrationKnown").SetValue(altera, false);

            object flags = stateType.GetField("flags").GetValue(state);
            System.Type flagType = StatsTestAccess.Find("AlterOS.Progression.ProgressionFlag");
            object flag = Activator.CreateInstance(flagType);
            flagType.GetField("key").SetValue(flag, "legacy.fixture");
            flagType.GetField("value").SetValue(flag, "kept");
            ((System.Collections.IList)flags).Add(flag);
            return JsonUtility.ToJson(state);
        }
    }
}
