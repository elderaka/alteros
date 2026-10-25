using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterOS.Tests.Persistence
{
    public sealed class ProgressionTestPersistenceRootTests
    {
        private string root;
        private string secondaryRoot;
        private string junctionParent;
        private string junctionPath;
        private string junctionTarget;
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AlterOS-Test-Progression-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            CancelPreparedRoot();
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (!string.IsNullOrEmpty(secondaryRoot) && Directory.Exists(secondaryRoot)) Directory.Delete(secondaryRoot, true);
            if (!string.IsNullOrEmpty(junctionPath) && Directory.Exists(junctionPath)) Directory.Delete(junctionPath, false);
            if (!string.IsNullOrEmpty(junctionTarget) && Directory.Exists(junctionTarget)) Directory.Delete(junctionTarget, true);
            if (!string.IsNullOrEmpty(junctionParent) && Directory.Exists(junctionParent)) Directory.Delete(junctionParent, true);
        }

        [Test]
        public void EditorScopeSetBeforeAwakeRoutesSceneStartupToSyntheticRoot()
        {
            PrepareForNextPlayMode(root);
            host = new GameObject("synthetic-progression-root-test");
            host.SetActive(false);
            Type storeType = FindStoreType();
            object store = host.AddComponent(storeType);
            RestorePendingRoot(); // Same-domain Play Mode entry with domain reload disabled.
            storeType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public).Invoke(store, null);

            Assert.IsTrue((bool)storeType.GetProperty("IsInitialized").GetValue(store, null));
            Assert.AreEqual("durable", storeType.GetProperty("PersistenceStatus").GetValue(store, null));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Data", "alteros-game.sqlite3")));

            ClearRuntimeRoot();
            RestorePendingRoot();
            Assert.AreEqual(Path.GetFullPath(root), GetPersistentRoot(storeType, store));
            var reopened = new GameObject("synthetic-progression-root-reload-test");
            reopened.SetActive(false);
            object reopenedStore = reopened.AddComponent(storeType);
            storeType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public).Invoke(reopenedStore, null);
            Assert.IsTrue((bool)storeType.GetProperty("IsInitialized").GetValue(reopenedStore, null));
            Assert.AreEqual("durable", storeType.GetProperty("PersistenceStatus").GetValue(reopenedStore, null));
            UnityEngine.Object.DestroyImmediate(reopened);
        }

        [Test]
        public void EditorScopeRejectsMissingRelativeAndPlayerProfileRoots()
        {
            Assert.Throws<ArgumentException>(() => PrepareForNextPlayMode(null));
            Assert.Throws<InvalidOperationException>(GetPersistentRootWithoutAwake);
            ClearRuntimeRoot();
            LogAssert.Expect(LogType.Error, "The scheduled synthetic persistence root is invalid; progression startup will fail closed.");
            RestorePendingRoot(); // Failed opt-in must remain blocked after a simulated domain reload.
            Assert.Throws<InvalidOperationException>(GetPersistentRootWithoutAwake);
            CancelPreparedRoot();
            Assert.Throws<ArgumentException>(() => PrepareForNextPlayMode("relative-test-save"));
            Assert.Throws<InvalidOperationException>(GetPersistentRootWithoutAwake);
            CancelPreparedRoot();
            Assert.Throws<ArgumentException>(() => PrepareForNextPlayMode(Application.persistentDataPath));
            Assert.Throws<InvalidOperationException>(GetPersistentRootWithoutAwake);
            CancelPreparedRoot();
            Assert.IsFalse(Directory.Exists(root));
        }

        [Test]
        public void EditorScopeRejectsNonemptyAndNestedRoots()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "keep.txt"), "do not touch");
            Assert.Throws<InvalidOperationException>(() => PrepareForNextPlayMode(root));
            Assert.AreEqual("do not touch", File.ReadAllText(Path.Combine(root, "keep.txt")));
            Assert.Throws<InvalidOperationException>(GetPersistentRootWithoutAwake);
            CancelPreparedRoot();

            secondaryRoot = Path.Combine(Path.GetTempPath(), "AlterOS-Test-Progression-" + Guid.NewGuid().ToString("N"));
            string emptyRoot = Path.Combine(secondaryRoot, "child");
            PrepareForNextPlayMode(emptyRoot);
            string nestedRoot = Path.Combine(Path.GetTempPath(), "AlterOS-Test-Progression-" + Guid.NewGuid().ToString("N"));
            Assert.Throws<InvalidOperationException>(() => PrepareForNextPlayMode(nestedRoot));
            CancelPreparedRoot();
            secondaryRoot = null;
            if (Directory.Exists(Path.GetDirectoryName(emptyRoot))) Directory.Delete(Path.GetDirectoryName(emptyRoot), true);
        }

        [Test]
        public void EditorScopeRejectsARealJunctionInsideTheTemporaryDirectory()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("The junction fixture requires Windows Editor.");
            junctionParent = Path.Combine(Path.GetTempPath(), "AlterOS-Test-Reparse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(junctionParent);
            junctionTarget = Path.Combine(junctionParent, "target");
            junctionPath = Path.Combine(junctionParent, "junction");
            Directory.CreateDirectory(junctionTarget);
            File.WriteAllText(Path.Combine(junctionTarget, "keep.txt"), "target stays intact");

            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + junctionPath + "\" \"" + junctionTarget + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0) Assert.Ignore("Windows did not permit creation of the synthetic junction fixture.");
            }

            Assert.Throws<ArgumentException>(() => PrepareForNextPlayMode(junctionPath));
            Assert.AreEqual("target stays intact", File.ReadAllText(Path.Combine(junctionTarget, "keep.txt")));
            CancelPreparedRoot();
        }

        [Test]
        public void WithoutEditorScopePersistentRootStillUsesUnityDefault()
        {
            host = new GameObject("inactive-persistence-root-probe");
            host.SetActive(false);
            Type storeType = FindStoreType();
            object store = host.AddComponent(storeType);
            MethodInfo getRoot = storeType.GetMethod("GetPersistentRoot", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(getRoot);
            Assert.AreEqual(Application.persistentDataPath, getRoot.Invoke(store, null));
        }

        [Test]
        public void ReturningToEditModeClearsTheNextPlayModeRoot()
        {
            PrepareForNextPlayMode(root);
            Type helper = FindEditorHelper();
            Type stateType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                stateType = assembly.GetType("UnityEditor.PlayModeStateChange", false);
                if (stateType != null) break;
            }
            Assert.IsNotNull(stateType);
            object enteredEditMode = Enum.Parse(stateType, "EnteredEditMode");
            helper.GetMethod("OnPlayModeStateChanged", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { enteredEditMode });

            Assert.AreEqual(Application.persistentDataPath, GetPersistentRootWithoutAwakeValue());
            string nextRoot = Path.Combine(Path.GetTempPath(), "AlterOS-Test-Progression-" + Guid.NewGuid().ToString("N"));
            PrepareForNextPlayMode(nextRoot);
            CancelPreparedRoot();
            if (Directory.Exists(nextRoot)) Directory.Delete(nextRoot, true);
        }

        private static void PrepareForNextPlayMode(string path)
        {
            MethodInfo prepare = FindEditorHelper().GetMethod("PrepareForNextPlayMode", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(prepare, "Editor-only pre-Awake persistence helper is missing.");
            try { prepare.Invoke(null, new object[] { path }); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }

        private static void CancelPreparedRoot()
        {
            MethodInfo cancel = FindEditorHelper().GetMethod("CancelPendingPlayModeRoot", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(cancel, "Editor-only persistence helper cannot clear its session root.");
            cancel.Invoke(null, null);
        }

        private static void ClearRuntimeRoot()
        {
            MethodInfo clear = FindStoreType().GetMethod("ClearEditorTestPersistenceRoot", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(clear);
            clear.Invoke(null, null);
        }

        private static void RestorePendingRoot()
        {
            MethodInfo restore = FindEditorHelper().GetMethod("RestorePendingRoot", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(restore, "Editor helper cannot restore a validated root after domain reload.");
            restore.Invoke(null, null);
        }

        private static Type FindEditorHelper()
        {
            const string typeName = "AlterOS.Editor.Persistence.ProgressionTestPersistenceRoot";
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null) return type;
            }
            Assert.Fail("Editor-only progression persistence helper is not loaded.");
            return null;
        }

        private static Type FindStoreType()
        {
            const string typeName = "AlterOS.Progression.AlteraProgressionStore";
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null) return type;
            }
            Assert.Fail("AlteraProgressionStore is not loaded.");
            return null;
        }

        private static void GetPersistentRootWithoutAwake()
        { GetPersistentRootWithoutAwakeValue(); }

        private static object GetPersistentRootWithoutAwakeValue()
        {
            var inactive = new GameObject("inactive-persistence-root-failure-probe");
            inactive.SetActive(false);
            try
            {
                Type storeType = FindStoreType();
                object store = inactive.AddComponent(storeType);
                MethodInfo getRoot = storeType.GetMethod("GetPersistentRoot", BindingFlags.Instance | BindingFlags.NonPublic);
                try { return getRoot.Invoke(store, null); }
                catch (TargetInvocationException exception) { throw exception.InnerException; }
            }
            finally { UnityEngine.Object.DestroyImmediate(inactive); }
        }

        private static object GetPersistentRoot(Type storeType, object store)
        {
            MethodInfo getRoot = storeType.GetMethod("GetPersistentRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(getRoot);
            return getRoot.Invoke(store, null);
        }
    }
}
