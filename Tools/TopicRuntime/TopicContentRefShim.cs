using System;

// Portable Topic source-check seam for the ContentRef owned by the compiled Content assembly.
namespace AlterOS.Content
{
    public static class ContentStatuses
    {
        public const string Ready = "ready";
        public const string Invalid = "invalid";
        public const string Missing = "missing";
        public const string Incompatible = "incompatible";
    }

    public sealed class ContentResult<T>
    {
        public readonly string status;
        public readonly T value;
        public bool IsReady { get { return status == ContentStatuses.Ready; } }

        private ContentResult(string resultStatus, T resultValue)
        { status = resultStatus; value = resultValue; }

        public static ContentResult<T> Ready(T value)
        { return new ContentResult<T>(ContentStatuses.Ready, value); }

        public static ContentResult<T> Failure(string status, string code, string path, string message)
        { return new ContentResult<T>(status, default(T)); }
    }

    public sealed class ContentRelease
    {
        public System.Collections.Generic.List<ContentReleaseDependency> dependencies =
            new System.Collections.Generic.List<ContentReleaseDependency>();
    }

    public sealed class ContentReleaseDependency
    {
        public string kind = string.Empty;
        public string id = string.Empty;
        public string revision = string.Empty;
        public string store = string.Empty;
        public string expectedHash = string.Empty;
    }

    [Serializable]
    public sealed class ContentRef
    {
        public string store = string.Empty;
        public string id = string.Empty;
        public string revision = string.Empty;
        public string expectedHash = string.Empty;

        public ContentRef() { }

        public ContentRef(string storeId, string contentId, string contentRevision, string hash = "")
        {
            store = storeId ?? string.Empty;
            id = contentId ?? string.Empty;
            revision = contentRevision ?? string.Empty;
            expectedHash = hash ?? string.Empty;
        }
    }
}
