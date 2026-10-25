using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AlterOS.Authoring
{
    /// <summary>Exports explicit immutable authoring snapshots; export never writes back to the live store.</summary>
    public static class AuthoringJsonExporter
    {
        private const int MaximumDocuments = 10000;
        private const string ManifestName = "manifest.json";

        public static AuthoringResult<AuthoringExportReceipt> Export(AuthoringRepository repository, AuthoringExportRequest request)
        {
            string error;
            Guid expectedStoreId;
            if (repository == null || request == null || request.documentIds == null || request.documentIds.Count < 1 ||
                request.documentIds.Count > MaximumDocuments || !Guid.TryParse(request.expectedStoreId, out expectedStoreId) ||
                expectedStoreId.ToString("D") != request.expectedStoreId || request.expectedStoreRevision < 0 ||
                string.IsNullOrWhiteSpace(request.destinationRoot))
                return Failure("authoring_export_request_invalid", "A repository, explicit bounded selection, canonical expected store revision and trusted destination are required.");

            AuthoringResult<AuthoringSnapshot> snapshotResult = repository.ReadSnapshot(request.documentIds);
            if (!snapshotResult.IsReady)
                return AuthoringResult<AuthoringExportReceipt>.Failure(snapshotResult.status,
                    FirstCode(snapshotResult, "authoring_export_snapshot_failed"), "documentIds", FirstMessage(snapshotResult));
            AuthoringSnapshot snapshot = snapshotResult.value;
            if (snapshot.storeId != request.expectedStoreId || snapshot.storeRevision != request.expectedStoreRevision)
                return Failure(AuthoringStatuses.Conflict, "authoring_export_revision_conflict",
                    "The authoring store changed after this exact export selection was prepared.");

            string root;
            if (!TryResolveTrustedRoot(request.destinationRoot, out root, out error))
                return Failure("authoring_export_destination_invalid", error);
            string finalPath = Path.Combine(root, snapshot.snapshotHash);
            SortedDictionary<string, byte[]> files;
            byte[] manifestBytes;
            try { files = BuildExportFiles(snapshot, out manifestBytes); }
            catch (Exception exception) { return Failure("authoring_export_projection_failed", exception.Message); }

            if (Directory.Exists(finalPath) || File.Exists(finalPath))
            {
                if (!VerifyExportDirectory(finalPath, files, manifestBytes, out error))
                    return Failure(AuthoringStatuses.Conflict, "authoring_export_destination_conflict", error);
                return AuthoringResult<AuthoringExportReceipt>.Ready(ToReceipt(snapshot, finalPath, manifestBytes, true));
            }

            string staging = Path.Combine(root, ".authoring-export-" + Guid.NewGuid().ToString("N"));
            bool moved = false;
            try
            {
                Directory.CreateDirectory(staging);
                foreach (KeyValuePair<string, byte[]> file in files)
                    WriteContainedFile(staging, file.Key, file.Value);
                WriteContainedFile(staging, ManifestName, manifestBytes);
                if (!VerifyExportDirectory(staging, files, manifestBytes, out error))
                    return Failure("authoring_export_verification_failed", error);
                if (Directory.Exists(finalPath) || File.Exists(finalPath))
                {
                    if (!VerifyExportDirectory(finalPath, files, manifestBytes, out error))
                        return Failure(AuthoringStatuses.Conflict, "authoring_export_destination_conflict", error);
                    return AuthoringResult<AuthoringExportReceipt>.Ready(ToReceipt(snapshot, finalPath, manifestBytes, true));
                }
                Directory.Move(staging, finalPath);
                moved = true;
                return AuthoringResult<AuthoringExportReceipt>.Ready(ToReceipt(snapshot, finalPath, manifestBytes, false));
            }
            catch (Exception exception)
            { return Failure("authoring_export_write_failed", exception.Message); }
            finally
            {
                if (!moved && Directory.Exists(staging))
                {
                    string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string fullStaging = Path.GetFullPath(staging);
                    if (fullStaging.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(fullStaging).StartsWith(".authoring-export-", StringComparison.Ordinal))
                    {
                        try { Directory.Delete(fullStaging, true); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }

        /// <summary>Validates an export manifest and creates an explicit importer request for a new target store.</summary>
        public static AuthoringResult<AuthoringImportRequest> CreateImportRequest(string exportDirectory,
            string expectedTargetStoreId, long expectedTargetStoreRevision, string operationId)
        {
            Guid targetStoreId;
            if (string.IsNullOrWhiteSpace(exportDirectory) || !Guid.TryParse(expectedTargetStoreId, out targetStoreId) ||
                targetStoreId.ToString("D") != expectedTargetStoreId || expectedTargetStoreRevision < 0 ||
                !AuthoringRepository.IsOperationId(operationId))
                return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.Invalid,
                    "authoring_export_import_request_invalid", "request", "An export directory, canonical target store revision and import operation ID are required.");

            string directory;
            try { directory = Path.GetFullPath(exportDirectory); }
            catch (Exception exception) { return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.Invalid,
                "authoring_export_path_invalid", "exportDirectory", exception.Message); }
            JObject manifest;
            string error;
            if (!TryReadManifest(directory, out manifest, out error))
                return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                    "authoring_export_manifest_invalid", directory, error);

            JArray documents = manifest["documents"] as JArray;
            if (documents == null || documents.Count == 0 || documents.Count > MaximumDocuments)
                return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                    "authoring_export_inventory_invalid", directory, "The export manifest has no bounded document inventory.");
            string exportStoreId = (string)manifest["storeId"];
            long exportStoreRevision = (long)manifest["storeRevision"];
            string exportSnapshotHash = (string)manifest["snapshotHash"];
            var sources = new List<AuthoringImportSource>(documents.Count);
            var snapshotDocuments = new List<AuthoringDocument>(documents.Count);
            var documentIds = new HashSet<string>(StringComparer.Ordinal);
            var logicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var readablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            string previousDocumentId = string.Empty;
            int sourceIndex = 0;
            foreach (JToken token in documents)
            {
                sourceIndex++;
                JObject row = token as JObject;
                string documentId = row == null ? string.Empty : (string)row["documentId"];
                string kind = row == null ? string.Empty : (string)row["kind"];
                string sourceId = row == null ? string.Empty : (string)row["sourceId"];
                string logicalPath = row == null ? string.Empty : (string)row["logicalPath"];
                string originalPath = row == null ? string.Empty : (string)row["originalPath"];
                string readablePath = row == null ? string.Empty : (string)row["readablePath"];
                string revisionHash = row == null ? string.Empty : (string)row["revisionHash"];
                string readableHash = row == null ? string.Empty : (string)row["readableHash"];
                string expectedOriginalPath = "originals/doc-" + sourceIndex.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + ".json";
                string expectedReadablePath = "documents/" + logicalPath;
                if (row == null || row.Count != 8 || !AuthoringRepository.IsIdentifier(documentId) || !documentIds.Add(documentId) ||
                    (previousDocumentId.Length > 0 && StringComparer.Ordinal.Compare(previousDocumentId, documentId) >= 0) ||
                    !AuthoringContentKinds.IsSupported(kind) || (!string.IsNullOrEmpty(sourceId) && !AuthoringRepository.IsIdentifier(sourceId)) ||
                    !AuthoringRepository.IsLogicalPath(logicalPath) || !logicalPaths.Add(logicalPath) ||
                    !AuthoringRepository.IsLogicalPath(originalPath) || originalPath != expectedOriginalPath || !sourcePaths.Add(originalPath) ||
                    !AuthoringRepository.IsLogicalPath(readablePath) || readablePath != expectedReadablePath || !readablePaths.Add(readablePath) ||
                    !AuthoringRepository.IsSha256(revisionHash) || !AuthoringRepository.IsSha256(readableHash))
                    return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                        "authoring_export_inventory_invalid", directory, "The export manifest contains an invalid or duplicate document mapping.");
                previousDocumentId = documentId;
                string originalFullPath = Path.Combine(directory, originalPath.Replace('/', Path.DirectorySeparatorChar));
                byte[] originalBytes;
                try
                {
                    if (!TryResolveContainedFile(directory, originalPath, out originalFullPath) ||
                        new FileInfo(originalFullPath).Length < 1 || new FileInfo(originalFullPath).Length > 4 * 1024 * 1024)
                        return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                            "authoring_export_original_missing", originalPath, "The exact original source file is missing or unsafe.");
                    originalBytes = File.ReadAllBytes(originalFullPath);
                }
                catch (Exception exception)
                { return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                    "authoring_export_original_unavailable", originalPath, exception.Message); }
                if (AuthoringRepository.Hash(originalBytes) != revisionHash)
                    return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                        "authoring_export_original_hash_mismatch", originalPath, "The exact original bytes do not match their revision hash.");
                string readableFullPath;
                if (!TryResolveContainedFile(directory, readablePath, out readableFullPath) ||
                    AuthoringRepository.Hash(File.ReadAllBytes(readableFullPath)) != readableHash)
                    return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                        "authoring_export_readable_hash_mismatch", readablePath, "The readable snapshot file is missing, unsafe or differs from its manifest hash.");
                sources.Add(new AuthoringImportSource
                {
                    documentId = documentId,
                    kind = kind,
                    logicalPath = logicalPath,
                    sourcePath = originalPath,
                    sourceId = sourceId,
                    sourceHash = revisionHash
                });
                snapshotDocuments.Add(new AuthoringDocument
                {
                    documentId = documentId,
                    kind = kind,
                    sourceId = sourceId,
                    logicalPath = logicalPath,
                    revisionHash = revisionHash,
                    documentBytes = originalBytes
                });
                totalBytes = checked(totalBytes + originalBytes.Length);
            }
            var exportedSnapshot = new AuthoringSnapshot
            {
                storeId = exportStoreId,
                storeRevision = exportStoreRevision,
                snapshotHash = exportSnapshotHash,
                totalBytes = totalBytes,
                documents = snapshotDocuments
            };
            if (Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) != exportSnapshotHash ||
                !AuthoringRepository.ValidateSnapshot(exportedSnapshot, out error))
                return AuthoringResult<AuthoringImportRequest>.Failure(AuthoringStatuses.RecoveryRequired,
                    "authoring_export_snapshot_invalid", directory, string.IsNullOrEmpty(error) ? "The export folder name does not match its snapshot hash." : error);
            return AuthoringResult<AuthoringImportRequest>.Ready(new AuthoringImportRequest
            {
                sourceRoot = directory,
                sources = sources,
                expectedStoreId = expectedTargetStoreId,
                expectedStoreRevision = expectedTargetStoreRevision,
                operationId = operationId
            });
        }

        private static SortedDictionary<string, byte[]> BuildExportFiles(AuthoringSnapshot snapshot, out byte[] manifestBytes)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            var rows = new JArray();
            int index = 0;
            foreach (AuthoringDocument document in snapshot.documents.OrderBy(item => item.documentId, StringComparer.Ordinal))
            {
                index++;
                string originalPath = "originals/doc-" + index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + ".json";
                string readablePath = "documents/" + document.logicalPath;
                byte[] originalBytes = (byte[])document.documentBytes.Clone();
                byte[] readableBytes = ReadableJson(originalBytes);
                if (files.ContainsKey(originalPath) || files.ContainsKey(readablePath))
                    throw new InvalidDataException("Two authoring documents map to the same contained export path.");
                files.Add(originalPath, originalBytes);
                files.Add(readablePath, readableBytes);
                rows.Add(new JObject
                {
                    ["documentId"] = document.documentId,
                    ["kind"] = document.kind,
                    ["sourceId"] = document.sourceId ?? string.Empty,
                    ["logicalPath"] = document.logicalPath,
                    ["revisionHash"] = document.revisionHash,
                    ["originalPath"] = originalPath,
                    ["readablePath"] = readablePath,
                    ["readableHash"] = AuthoringRepository.Hash(readableBytes)
                });
            }
            var manifest = new JObject
            {
                ["schemaVersion"] = 1,
                ["storeId"] = snapshot.storeId,
                ["storeRevision"] = snapshot.storeRevision,
                ["snapshotHash"] = snapshot.snapshotHash,
                ["documents"] = rows
            };
            manifestBytes = new UTF8Encoding(false).GetBytes(manifest.ToString(Formatting.Indented) + "\n");
            return files;
        }

        private static byte[] ReadableJson(byte[] bytes)
        {
            string text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.StartsWith("\uFEFF", StringComparison.Ordinal)) text = text.Substring(1);
            using (var stringReader = new StringReader(text))
            using (var reader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 })
            {
                JToken token = JToken.ReadFrom(reader);
                if (reader.Read()) throw new InvalidDataException("Trailing JSON content is not accepted in an authoring export.");
                return new UTF8Encoding(false).GetBytes(token.ToString(Formatting.Indented) + "\n");
            }
        }

        private static bool TryResolveTrustedRoot(string path, out string root, out string error)
        {
            root = string.Empty;
            error = string.Empty;
            try { root = Path.GetFullPath(path); }
            catch (Exception exception) { error = exception.Message; return false; }
            if (!Directory.Exists(root)) { error = "The trusted export root must already exist."; return false; }
            string cursor = Path.GetPathRoot(root);
            string remainder = root.Substring(cursor.Length);
            foreach (string segment in remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                cursor = Path.Combine(cursor, segment);
                if (!Directory.Exists(cursor) || (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                { error = "The trusted export root contains a missing or reparse-point directory."; return false; }
            }
            return true;
        }

        private static void WriteContainedFile(string staging, string relativePath, byte[] bytes)
        {
            if (!AuthoringRepository.IsLogicalPath(relativePath)) throw new InvalidDataException("An export file path is unsafe.");
            string fullPath = Path.GetFullPath(Path.Combine(staging, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsContained(staging, fullPath)) throw new InvalidDataException("An export file escaped its staging directory.");
            string parent = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static bool VerifyExportDirectory(string directory, SortedDictionary<string, byte[]> files,
            byte[] manifestBytes, out string error)
        {
            error = string.Empty;
            try
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                { error = "The export destination is not a safe directory."; return false; }
                var expected = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
                expected.Add(ManifestName, manifestBytes);
                var actual = new HashSet<string>(StringComparer.Ordinal);
                var pending = new Stack<string>();
                pending.Push(directory);
                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    foreach (string childDirectory in Directory.GetDirectories(current))
                    {
                        if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                        { error = "The export contains a reparse-point directory."; return false; }
                        pending.Push(childDirectory);
                    }
                    foreach (string path in Directory.GetFiles(current))
                    {
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        { error = "The export contains a reparse-point file."; return false; }
                        string relative = path.Substring(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1)
                            .Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                        if (!actual.Add(relative)) { error = "The export contains duplicate logical file paths."; return false; }
                    }
                }
                if (actual.Count != expected.Count || !actual.SetEquals(expected.Keys))
                { error = "The export file inventory differs from its deterministic manifest."; return false; }
                foreach (KeyValuePair<string, byte[]> file in expected)
                {
                    string fullPath = Path.Combine(directory, file.Key.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.ReadAllBytes(fullPath).SequenceEqual(file.Value))
                    { error = "An export file differs from the exact requested snapshot: " + file.Key; return false; }
                }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static bool TryReadManifest(string directory, out JObject manifest, out string error)
        {
            manifest = null;
            error = string.Empty;
            try
            {
                string path = Path.Combine(directory, ManifestName);
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                    !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                { error = "The export or its manifest is missing or unsafe."; return false; }
                using (var reader = new JsonTextReader(File.OpenText(path)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 })
                {
                    manifest = JToken.ReadFrom(reader) as JObject;
                    if (reader.Read()) { error = "Trailing export manifest JSON is not accepted."; return false; }
                }
                Guid storeId;
                string snapshotHash = manifest == null ? string.Empty : (string)manifest["snapshotHash"];
                if (manifest == null || manifest.Count != 5 || (int?)manifest["schemaVersion"] != 1 ||
                    !Guid.TryParse((string)manifest["storeId"], out storeId) || storeId.ToString("D") != (string)manifest["storeId"] ||
                    !AuthoringRepository.IsSha256(snapshotHash) || (long?)manifest["storeRevision"] < 0 || manifest["documents"] == null)
                { error = "The export manifest header or schema is invalid."; return false; }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static bool IsContained(string root, string candidate)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveContainedFile(string root, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (!AuthoringRepository.IsLogicalPath(relativePath)) return false;
            try
            {
                string fullRoot = Path.GetFullPath(root);
                fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsContained(fullRoot, fullPath) || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0) return false;
                string cursor = fullRoot;
                string[] segments = relativePath.Split('/');
                for (int index = 0; index < segments.Length; index++)
                {
                    cursor = Path.Combine(cursor, segments[index]);
                    bool final = index == segments.Length - 1;
                    if (final ? !File.Exists(cursor) : !Directory.Exists(cursor)) return false;
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static AuthoringExportReceipt ToReceipt(AuthoringSnapshot snapshot, string directory,
            byte[] manifestBytes, bool reused)
        {
            return new AuthoringExportReceipt
            {
                storeId = snapshot.storeId,
                storeRevision = snapshot.storeRevision,
                snapshotHash = snapshot.snapshotHash,
                manifestHash = AuthoringRepository.Hash(manifestBytes),
                directoryPath = directory,
                documentCount = snapshot.documents.Count,
                reusedExistingExport = reused
            };
        }

        private static string FirstCode<T>(AuthoringResult<T> result, string fallback)
        { return result.diagnostics.Count == 0 ? fallback : result.diagnostics[0].code; }
        private static string FirstMessage<T>(AuthoringResult<T> result)
        { return result.diagnostics.Count == 0 ? "The exact authoring snapshot could not be read." : result.diagnostics[0].message; }

        private static AuthoringResult<AuthoringExportReceipt> Failure(string code, string message)
        { return AuthoringResult<AuthoringExportReceipt>.Failure(AuthoringStatuses.Invalid, code, "", message); }

        private static AuthoringResult<AuthoringExportReceipt> Failure(string status, string code, string message)
        { return AuthoringResult<AuthoringExportReceipt>.Failure(status, code, "", message); }
    }
}
