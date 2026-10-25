using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class ActivityHydrationStoreTests
    {
        private GameObject host;
        private object store;
        private object state;
        private string temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            temporaryRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AlterOS Hydration Store Test " + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            host = new GameObject("hydration-test");
            host.SetActive(false);
            System.Type type = StatsTestAccess.Find("AlterOS.Progression.AlteraProgressionStore");
            store = host.AddComponent(type);
            type.GetField("testPersistenceRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(store, temporaryRoot);
            type.GetField("saveImmediately", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(store, false);
            host.SetActive(true);
            object progression = type.GetProperty("State").GetValue(store, null);
            state = progression.GetType().GetField("altera").GetValue(progression);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }

        [Test]
        public void ActivityCheckpointClampsKnownHydrationToCatalogueRange()
        {
            SetState("hydration", 97f);
            SetState("hydrationKnown", true);
            bool applied = (bool)Invoke("TryApplyActivityHydrationCheckpoint", 10f);
            Assert.IsTrue(applied);
            Assert.AreEqual(100f, GetState("hydration"));
        }

        [Test]
        public void ActivityCheckpointDoesNotTurnUnknownHydrationIntoKnownZero()
        {
            SetState("hydration", 0f);
            SetState("hydrationKnown", false);
            bool applied = (bool)Invoke("TryApplyActivityHydrationCheckpoint", 10f);
            Assert.IsFalse(applied);
            Assert.IsFalse((bool)GetState("hydrationKnown"));
            Assert.AreEqual(0f, GetState("hydration"));
        }

        private object Invoke(string method, params object[] arguments)
        { return System.Array.Find(store.GetType().GetMethods(), candidate =>
            candidate.Name == method && candidate.GetParameters().Length == arguments.Length).Invoke(store, arguments); }

        private void SetState(string field, object value)
        { state.GetType().GetField(field).SetValue(state, value); }

        private object GetState(string field)
        { return state.GetType().GetField(field).GetValue(state); }
    }
}
