#if UNITY_EDITOR
using System;
using System.IO;
using AlterOS.Progression;
using UnityEditor;
using UnityEngine;

namespace AlterOS.Editor.Persistence
{
    /// <summary>Schedules a safe synthetic save root for the next Play Mode session.</summary>
    [InitializeOnLoad]
    public static class ProgressionTestPersistenceRoot
    {
        private const string SessionRootKey = "AlterOS.TestPersistenceRoot.NextPlayMode";
        private const string SessionTokenKey = "AlterOS.TestPersistenceRoot.SessionToken";
        private const string InvalidRootSentinel = "!invalid-editor-test-persistence-root!";
        private const string RootMarkerName = ".alteros-editor-test-root";

        static ProgressionTestPersistenceRoot()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RestorePendingRoot();
        }

        public static string PrepareForNextPlayMode(string absoluteTemporaryRoot)
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionRootKey, string.Empty)))
                throw new InvalidOperationException("A synthetic persistence root is already scheduled for Play Mode.");
            try
            {
                string root = AlteraProgressionStore.SetEditorTestPersistenceRoot(absoluteTemporaryRoot);
                string token = Guid.NewGuid().ToString("N");
                File.WriteAllText(Path.Combine(root, RootMarkerName), token, new System.Text.UTF8Encoding(false));
                SessionState.SetString(SessionRootKey, root);
                SessionState.SetString(SessionTokenKey, token);
                return root;
            }
            catch
            {
                // Preserve the failed opt-in across an Editor domain reload so an
                // unsafe test request cannot silently fall back to the player save.
                AlteraProgressionStore.MarkEditorTestPersistenceRootUnavailable();
                SessionState.SetString(SessionRootKey, InvalidRootSentinel);
                SessionState.EraseString(SessionTokenKey);
                throw;
            }
        }

        public static void CancelPendingPlayModeRoot()
        {
            SessionState.EraseString(SessionRootKey);
            SessionState.EraseString(SessionTokenKey);
            AlteraProgressionStore.ClearEditorTestPersistenceRoot();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RestoreBeforeSceneAwake()
        { RestorePendingRoot(); }

        private static void RestorePendingRoot()
        {
            string root = SessionState.GetString(SessionRootKey, string.Empty);
            if (string.IsNullOrEmpty(root)) return;
            string token = SessionState.GetString(SessionTokenKey, string.Empty);
            if (root == InvalidRootSentinel || string.IsNullOrEmpty(token))
            {
                AlteraProgressionStore.MarkEditorTestPersistenceRootUnavailable();
                Debug.LogError("The scheduled synthetic persistence root is invalid; progression startup will fail closed.");
                return;
            }
            try { AlteraProgressionStore.RestoreEditorTestPersistenceRoot(root, token); }
            catch (Exception exception)
            {
                AlteraProgressionStore.MarkEditorTestPersistenceRootUnavailable();
                Debug.LogError("The scheduled synthetic persistence root is unsafe or unavailable; progression startup will fail closed. " + exception.Message);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) CancelPendingPlayModeRoot();
        }
    }
}
#endif
