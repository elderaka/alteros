using System.Collections.Generic;
using UnityEngine;

namespace AlterOS.Content
{
    public sealed class ContentBuildManifest
    {
        public string Hash { get { return string.Empty; } }
    }

    public sealed class ContentRelease
    {
        public string releaseId;
    }

    public sealed class RuntimeContentStore
    {
        public ContentBuildManifest BuildManifest;
        public ContentRelease Release;
        public static ContentResult<RuntimeContentStore> OpenShippedRelease(string root)
        { return new ContentResult<RuntimeContentStore>(); }
    }

    public sealed class ContentResult<T>
    {
        public bool IsReady;
        public T value;
        public string status;
    }
}

namespace AlterOS.Dialogue
{
    public sealed class DialogueSequence
    {
        public List<DialogueNode> nodes = new List<DialogueNode>();
    }

    public sealed class DialogueNode
    {
        public string speaker;
        public string text;
        public string translatedText;
        public string GetExpressionCue() { return string.Empty; }
    }
}

namespace AlterOS.Interaction
{
    public sealed class GeneratedDialogueArchive : MonoBehaviour
    {
        public string MigrationManifestPath { get { return string.Empty; } }
        public string MigrationDatabasePath { get { return string.Empty; } }
    }
}
