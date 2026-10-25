using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AlterOS.Authoring
{
    /// <summary>Explicit, hash-checked import of named JSON sources into the authoring store.</summary>
    public static class AuthoringJsonImporter
    {
        private const int MaximumSources = 10000;
        private const int MaximumSourceBytes = 4 * 1024 * 1024;

        public static AuthoringResult<AuthoringImportReceipt> ImportOnce(AuthoringRepository repository, AuthoringImportRequest request)
        {
            string error = string.Empty;
            if (repository == null || !ValidateRequest(request, out error))
                return AuthoringResult<AuthoringImportReceipt>.Failure(AuthoringStatuses.Invalid,
                    "authoring_import_request_invalid", "request", repository == null ? "An initialized authoring repository is required." : error);

            string requestHash = RequestHash(request);
            AuthoringResult<AuthoringImportReceipt> replay = repository.ReplayImportIfExists(request.operationId, requestHash);
            if (replay != null) return replay;

            string root;
            if (!TryResolveRoot(request.sourceRoot, out root, out error))
                return AuthoringResult<AuthoringImportReceipt>.Failure(AuthoringStatuses.Invalid,
                    "authoring_import_root_invalid", "sourceRoot", error);

            var prepared = new List<PreparedAuthoringImportSource>(request.sources.Count);
            foreach (AuthoringImportSource source in request.sources.OrderBy(item => item.logicalPath, StringComparer.Ordinal))
            {
                string sourcePath = string.IsNullOrEmpty(source.sourcePath) ? source.logicalPath : source.sourcePath;
                string fullPath;
                if (!TryResolveSource(root, sourcePath, out fullPath, out error))
                    return AuthoringResult<AuthoringImportReceipt>.Failure(AuthoringStatuses.Invalid,
                        "authoring_import_path_invalid", sourcePath, error);

                byte[] bytes;
                if (!TryReadSource(fullPath, source.sourceHash, out bytes, out error))
                    return AuthoringResult<AuthoringImportReceipt>.Failure(AuthoringStatuses.Invalid,
                        "authoring_import_source_invalid", source.logicalPath, error);

                string derivedSourceId = ResolveSourceId(source.kind, source.logicalPath, source.documentId, bytes);
                if (!string.IsNullOrEmpty(source.sourceId) && !AuthoringContentKinds.IsRawSourceKind(source.kind) &&
                    source.sourceId != derivedSourceId)
                    return AuthoringResult<AuthoringImportReceipt>.Failure(AuthoringStatuses.Invalid,
                        "authoring_import_source_identity_invalid", source.logicalPath,
                        "The explicit stable source ID does not match the ID embedded in the named document.");

                prepared.Add(new PreparedAuthoringImportSource
                {
                    documentId = source.documentId,
                    kind = source.kind,
                    sourceId = string.IsNullOrEmpty(source.sourceId)
                        ? derivedSourceId : source.sourceId,
                    logicalPath = source.logicalPath,
                    sourceHash = source.sourceHash,
                    sourceBytes = bytes
                });
            }

            return repository.ImportBatch(request, requestHash, prepared);
        }

        private static string ResolveSourceId(string kind, string logicalPath, string documentId, byte[] bytes)
        {
            if (AuthoringContentKinds.IsRawSourceKind(kind))
                return "legacy_" + AuthoringRepository.Hash(Encoding.UTF8.GetBytes(logicalPath)).Substring(0, 40);
            if (kind == "furniture_catalogue_settings") return "furniture.catalogue";

            string field = kind == "generic_dialogue" ? "id" :
                (kind == "topic_definition" || kind == "topic") ? "topicId" :
                (kind == "scenario_definition" || kind == "scenario") ? "scenarioId" : string.Empty;
            if (field.Length == 0) return string.Empty;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                JObject root = JObject.Parse(json);
                JObject identity = root;
                if (kind == "topic" || kind == "scenario") identity = root["document"] as JObject;
                string sourceId = identity == null ? string.Empty : (string)identity[field];
                return AuthoringRepository.IsIdentifier(sourceId) ? sourceId : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static bool ValidateRequest(AuthoringImportRequest request, out string error)
        {
            error = string.Empty;
            Guid storeId;
            if (request == null || string.IsNullOrWhiteSpace(request.sourceRoot) || request.sources == null ||
                request.sources.Count < 1 || request.sources.Count > MaximumSources ||
                !Guid.TryParse(request.expectedStoreId, out storeId) ||
                storeId.ToString("D") != request.expectedStoreId || request.expectedStoreRevision < 0 ||
                !AuthoringRepository.IsOperationId(request.operationId))
            { error = "A trusted source root, 1..10000 explicit sources, canonical store GUID, nonnegative expected revision and operation ID are required."; return false; }

            var documentIds = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AuthoringImportSource source in request.sources)
            {
                string sourcePath = source == null || string.IsNullOrEmpty(source.sourcePath) ?
                    (source == null ? string.Empty : source.logicalPath) : source.sourcePath;
                if (source != null && !string.IsNullOrEmpty(source.sourceId) && !AuthoringRepository.IsIdentifier(source.sourceId))
                { error = "A supplied stable source ID is malformed."; return false; }
                if (source == null || !AuthoringRepository.IsIdentifier(source.documentId) ||
                    !AuthoringContentKinds.IsSupported(source.kind) || !AuthoringRepository.IsLogicalPath(source.logicalPath) ||
                    !AuthoringRepository.IsSha256(source.sourceHash) || !documentIds.Add(source.documentId) ||
                    !paths.Add(source.logicalPath.ToUpperInvariant()) || !AuthoringRepository.IsLogicalPath(sourcePath) ||
                    !sourcePaths.Add(sourcePath))
                { error = "Every imported record needs a unique stable document ID, registered kind, safe unique logical path and lowercase SHA-256."; return false; }
                if (source.kind == "furniture_catalogue_settings" &&
                    (source.documentId != "furniture.catalogue" || source.logicalPath != "Furniture/catalogue-settings.json"))
                { error = "Furniture catalogue settings use the fixed singleton ID and logical path."; return false; }
            }
            return true;
        }

        private static bool TryResolveRoot(string sourceRoot, out string root, out string error)
        {
            root = string.Empty;
            error = string.Empty;
            try { root = Path.GetFullPath(sourceRoot); }
            catch (Exception exception) { error = exception.Message; return false; }
            if (!Directory.Exists(root)) { error = "The explicit import source root does not exist."; return false; }
            return !HasReparseComponent(root, true, out error);
        }

        private static bool TryResolveSource(string root, string logicalPath, out string fullPath, out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;
            if (!AuthoringRepository.IsLogicalPath(logicalPath))
            { error = "A rooted, traversal, reparse or unsafe source path is not permitted."; return false; }
            try { fullPath = Path.GetFullPath(Path.Combine(root, logicalPath.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception exception) { error = exception.Message; return false; }

            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { error = "The resolved source path escapes the trusted root."; return false; }

            string cursor = root;
            string[] segments = logicalPath.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                cursor = Path.Combine(cursor, segments[index]);
                bool last = index == segments.Length - 1;
                if (last ? !File.Exists(cursor) : !Directory.Exists(cursor))
                { error = "A named source path is missing or is not the expected file/directory type."; return false; }
                FileAttributes attributes;
                try { attributes = File.GetAttributes(cursor); }
                catch (Exception exception) { error = exception.Message; return false; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { error = "Reparse points are not permitted in import source paths."; return false; }
            }
            return true;
        }

        private static bool HasReparseComponent(string path, bool includeTarget, out string error)
        {
            error = string.Empty;
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) { error = "The source root must be absolute."; return true; }
            string cursor = root;
            string remainder = fullPath.Substring(root.Length);
            string[] segments = remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            int count = includeTarget ? segments.Length : Math.Max(0, segments.Length - 1);
            for (int index = 0; index < count; index++)
            {
                cursor = Path.Combine(cursor, segments[index]);
                if (!Directory.Exists(cursor)) { error = "A source root path component does not exist."; return true; }
                try
                {
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    { error = "A reparse point cannot be used as the trusted import source root."; return true; }
                }
                catch (Exception exception) { error = exception.Message; return true; }
            }
            return false;
        }

        private static bool TryReadSource(string fullPath, string expectedHash, out byte[] bytes, out string error)
        {
            bytes = null;
            error = string.Empty;
            try
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
                {
                    if (stream.Length < 1 || stream.Length > MaximumSourceBytes)
                    { error = "An imported JSON source must be 1..4 MiB."; return false; }
                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) { error = "The source changed or ended while it was being read."; return false; }
                        offset += read;
                    }
                    if (stream.ReadByte() != -1) { error = "The source changed size while it was being read."; return false; }
                }
            }
            catch (Exception exception) { error = exception.Message; return false; }

            if (AuthoringRepository.Hash(bytes) != expectedHash)
            { error = "The source bytes do not match the explicitly approved SHA-256 inventory."; return false; }
            if (!AuthoringRepository.IsJsonDocument(bytes, out error)) return false;
            return true;
        }

        private static string RequestHash(AuthoringImportRequest request)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, request.expectedStoreId);
                writer.Write(request.expectedStoreRevision);
                AuthoringImportSource[] ordered = request.sources.OrderBy(item => item.logicalPath, StringComparer.Ordinal)
                    .ThenBy(item => item.documentId, StringComparer.Ordinal).ToArray();
                writer.Write(ordered.Length);
                foreach (AuthoringImportSource source in ordered)
                {
                    WriteString(writer, source.documentId);
                    WriteString(writer, source.kind);
                    WriteString(writer, source.logicalPath);
                    WriteString(writer, string.IsNullOrEmpty(source.sourcePath) ? source.logicalPath : source.sourcePath);
                    WriteString(writer, source.sourceId);
                    WriteString(writer, source.sourceHash);
                }
                writer.Flush();
                return AuthoringRepository.Hash(stream.ToArray());
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }
}
