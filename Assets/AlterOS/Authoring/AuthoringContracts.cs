using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AlterOS.Authoring
{
    public static class AuthoringStatuses
    {
        public const string Ready = "ready";
        public const string NotInitialized = "not_initialized";
        public const string Invalid = "invalid";
        public const string Conflict = "conflict";
        public const string Busy = "busy";
        public const string RecoveryRequired = "recovery_required";
        public const string UnsupportedSchema = "unsupported_schema";
    }

    /// <summary>Shared kind registry for the authored content and Workbench drafts.</summary>
    public static class AuthoringContentKinds
    {
        private static readonly ReadOnlyCollection<string> SourceKindValues = Array.AsReadOnly(new[]
        {
            "generic_dialogue", "topic_definition", "scenario_definition", "legacy_sequence",
            "ambient_pool", "activity_scenario_library", "activity_greeting_menu",
            "conversation_coverage", "topic_migration_map", "onboarding_steps",
            "dialogue_definition", "furniture_definition", "furniture_catalogue_settings", "item_definition", "action_definition"
        });

        private static readonly HashSet<string> Supported = new HashSet<string>(SourceKindValues, StringComparer.Ordinal)
        {
            "topic", "scenario", "furniture_catalogue_settings"
        };

        public static IReadOnlyList<string> SourceKinds { get { return SourceKindValues; } }

        public static IReadOnlyList<string> AllKinds
        {
            get
            {
                var values = new List<string>(SourceKindValues) { "topic", "scenario" };
                return values.AsReadOnly();
            }
        }

        public static bool IsSupported(string kind) { return !string.IsNullOrEmpty(kind) && Supported.Contains(kind); }

        public static bool IsRawSourceKind(string kind)
        {
            return kind == "legacy_sequence" || kind == "ambient_pool" || kind == "activity_scenario_library" ||
                kind == "activity_greeting_menu" || kind == "conversation_coverage" ||
                kind == "topic_migration_map" || kind == "onboarding_steps";
        }
    }

    public sealed class AuthoringDiagnostic
    {
        public string code = string.Empty;
        public string path = string.Empty;
        public string message = string.Empty;
    }

    public sealed class AuthoringResult<T>
    {
        public string status = AuthoringStatuses.Invalid;
        public T value;
        public List<AuthoringDiagnostic> diagnostics = new List<AuthoringDiagnostic>();

        public bool IsReady { get { return status == AuthoringStatuses.Ready; } }

        public static AuthoringResult<T> Ready(T value)
        { return new AuthoringResult<T> { status = AuthoringStatuses.Ready, value = value }; }

        public static AuthoringResult<T> Failure(string status, string code, string path, string message)
        {
            return new AuthoringResult<T>
            {
                status = status,
                diagnostics = new List<AuthoringDiagnostic>
                {
                    new AuthoringDiagnostic
                    {
                        code = Bound(code, 96),
                        path = Bound(path, 512),
                        message = Bound(message, 512)
                    }
                }
            };
        }

        private static string Bound(string value, int maximum)
        {
            value = value ?? string.Empty;
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }

    public sealed class AuthoringSaveRequest
    {
        public string documentId = string.Empty;
        public string kind = string.Empty;
        public string logicalPath = string.Empty;
        public string expectedHash = string.Empty;
        public byte[] documentBytes = new byte[0];
        public string operationId = string.Empty;
    }

    public class AuthoringDocumentSummary
    {
        public string documentId = string.Empty;
        public string kind = string.Empty;
        public string sourceId = string.Empty;
        public string logicalPath = string.Empty;
        public string revisionHash = string.Empty;
    }

    public sealed class AuthoringDocument : AuthoringDocumentSummary
    {
        public byte[] documentBytes = new byte[0];
        public string createdUtc = string.Empty;
    }

    public sealed class AuthoringDocumentPage
    {
        public List<AuthoringDocumentSummary> documents = new List<AuthoringDocumentSummary>();
        public string nextCursor = string.Empty;
    }

    public sealed class AuthoringBuildSetRequest
    {
        public string name = string.Empty;
        public IReadOnlyList<string> documentIds = Array.Empty<string>();
        public string expectedHash = string.Empty;
        public string operationId = string.Empty;
    }

    public sealed class AuthoringBuildSet
    {
        public string name = string.Empty;
        public List<string> documentIds = new List<string>();
        public string revisionHash = string.Empty;
        public string updatedUtc = string.Empty;
    }

    public sealed class AuthoringSnapshot
    {
        public string storeId = string.Empty;
        public long storeRevision;
        public string snapshotHash = string.Empty;
        public long totalBytes;
        public List<AuthoringDocument> documents = new List<AuthoringDocument>();
    }

    public sealed class AuthoringStoreInfo
    {
        public string storeId = string.Empty;
        public int schemaVersion;
        public long storeRevision;
    }

    public sealed class AuthoringImportSource
    {
        public string documentId = string.Empty;
        public string kind = string.Empty;
        public string logicalPath = string.Empty;
        public string sourcePath = string.Empty;
        public string sourceId = string.Empty;
        public string sourceHash = string.Empty;
    }

    public sealed class AuthoringImportRequest
    {
        public string sourceRoot = string.Empty;
        public IReadOnlyList<AuthoringImportSource> sources = Array.Empty<AuthoringImportSource>();
        public string expectedStoreId = string.Empty;
        public long expectedStoreRevision;
        public string operationId = string.Empty;
    }

    public sealed class AuthoringExportRequest
    {
        public IReadOnlyList<string> documentIds = Array.Empty<string>();
        public string expectedStoreId = string.Empty;
        public long expectedStoreRevision;
        public string destinationRoot = string.Empty;
    }

    public sealed class AuthoringExportReceipt
    {
        public string storeId = string.Empty;
        public long storeRevision;
        public string snapshotHash = string.Empty;
        public string manifestHash = string.Empty;
        public string directoryPath = string.Empty;
        public int documentCount;
        public bool reusedExistingExport;
    }

    public sealed class AuthoringImportMapping
    {
        public string sourcePath = string.Empty;
        public string sourceHash = string.Empty;
        public string documentId = string.Empty;
        public string sourceId = string.Empty;
    }

    public sealed class AuthoringImportReceipt
    {
        public string operationId = string.Empty;
        public string storeId = string.Empty;
        public long storeRevision;
        public int sourceCount;
        public string receiptHash = string.Empty;
        public List<AuthoringImportMapping> sources = new List<AuthoringImportMapping>();
    }

    internal sealed class PreparedAuthoringImportSource
    {
        public string documentId = string.Empty;
        public string kind = string.Empty;
        public string sourceId = string.Empty;
        public string logicalPath = string.Empty;
        public string sourceHash = string.Empty;
        public byte[] sourceBytes = new byte[0];
    }
}
