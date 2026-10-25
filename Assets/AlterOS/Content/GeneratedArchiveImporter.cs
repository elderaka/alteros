using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    /// <summary>One-time import of the previous JSON archive into the private content database.</summary>
    public static class GeneratedArchiveImporter
    {
        private const int MaximumManifestBytes = 2 * 1024 * 1024;
        private const int MaximumPayloadBytes = 2 * 1024 * 1024;
        private const long MaximumAggregatePayloadBytes = 64L * 1024L * 1024L;
        private const int MaximumRecords = 4096;

        public static GeneratedArchiveImportResult ImportOnce(string legacyManifestPath, string trustedPayloadRoot,
            SqliteGeneratedArchiveStore store)
        {
            if (store == null) return Failure("recovery_required", "The private generated archive store is unavailable.");
            if (!store.IsReady) return Failure("recovery_required", store.Error);

            // A committed receipt makes SQL authoritative. Do not touch an old source again,
            // including to detect edits that the application is no longer allowed to consume.
            GeneratedArchiveImportResult existing = store.ExistingImport();
            if (existing != null) return existing;
            if (string.IsNullOrWhiteSpace(trustedPayloadRoot) || string.IsNullOrWhiteSpace(legacyManifestPath))
                return Failure("recovery_required", "The host did not provide trusted legacy archive paths.");

            try
            {
                string root = Path.GetFullPath(trustedPayloadRoot);
                string manifestPath;
                string pathError;
                if (!TryResolveContainedPath(root, legacyManifestPath, out manifestPath, out pathError))
                    return Failure("recovery_required", "Legacy archive manifest path is unsafe: " + pathError);
                if (Directory.Exists(manifestPath))
                    return Failure("recovery_required", "Legacy archive manifest path is a directory.");
                if (!File.Exists(manifestPath)) return store.ImportMissing();

                byte[] manifestBytes = ReadBoundedFile(manifestPath, MaximumManifestBytes);
                string manifestText = new UTF8Encoding(false, true).GetString(manifestBytes);
                JsonValue parsed = ContentJson.Parse(manifestText);
                if (!(parsed is JsonObject)) return Failure("recovery_required", "Legacy generated archive manifest is not a JSON object.");

                LegacyGeneratedArchive manifest = DeserializeManifest(manifestBytes);
                if (manifest == null || manifest.records == null)
                    return Failure("recovery_required", "Legacy generated archive manifest has no records array.");
                if (manifest.records.Count > MaximumRecords)
                    return Failure("recovery_required", "Legacy generated archive record count exceeds its import limit.");

                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var records = new List<GeneratedArchiveRecordData>(manifest.records.Count);
                long aggregatePayloadBytes = 0;
                int payloadCount = 0;
                for (int index = 0; index < manifest.records.Count; index++)
                {
                    LegacyGeneratedDialogueRecord legacy = manifest.records[index];
                    if (legacy == null || string.IsNullOrWhiteSpace(legacy.generatedDialogueId) ||
                        legacy.generatedDialogueId.Length > 256 || legacy.status < 0 || legacy.status > 4 ||
                        !ids.Add(legacy.generatedDialogueId))
                        return Failure("recovery_required", "Legacy archive contains an invalid record or duplicate stable ID.");

                    var record = new GeneratedArchiveRecordData
                    {
                        generatedDialogueId = legacy.generatedDialogueId,
                        originSessionId = legacy.originSessionId,
                        title = legacy.title,
                        sourcePrompt = legacy.sourcePrompt,
                        summary = legacy.summary,
                        createdAt = legacy.createdAt,
                        lastPlayedAt = legacy.lastPlayedAt,
                        statusCode = legacy.status,
                        wasPlayed = legacy.wasPlayed,
                        surfacedToPlayer = legacy.surfacedToPlayer,
                        tagsJson = SerializeTags(legacy.tags),
                        jsonPath = legacy.jsonPath,
                        recordOrdinal = index,
                        payloadState = string.IsNullOrWhiteSpace(legacy.jsonPath) ? "missing" : "missing",
                        payloadDiagnostic = string.IsNullOrWhiteSpace(legacy.jsonPath) ? "No legacy payload locator was recorded." : string.Empty,
                        payloadHash = string.Empty
                    };

                    if (!string.IsNullOrWhiteSpace(legacy.jsonPath))
                    {
                        string payloadPath;
                        if (!TryResolveContainedPath(root, legacy.jsonPath, out payloadPath, out pathError))
                        {
                            record.payloadState = "unsafe_path";
                            record.payloadDiagnostic = pathError;
                        }
                        else if (!File.Exists(payloadPath) || Directory.Exists(payloadPath))
                        {
                            record.payloadState = "missing";
                            record.payloadDiagnostic = "The referenced legacy payload file is missing.";
                        }
                        else
                        {
                            try
                            {
                                byte[] payload = ReadBoundedFile(payloadPath, MaximumPayloadBytes);
                                aggregatePayloadBytes += payload.Length;
                                if (aggregatePayloadBytes > MaximumAggregatePayloadBytes)
                                    return Failure("recovery_required", "Generated archive payload bytes exceed the aggregate import limit.");
                                record.payloadBytes = payload;
                                record.payloadHash = ComputeSha256(payload);
                                payloadCount++;
                                string payloadText = string.Empty;
                                try { payloadText = new UTF8Encoding(false, true).GetString(payload); }
                                catch (DecoderFallbackException exception)
                                {
                                    record.payloadState = "invalid_encoding";
                                    record.payloadDiagnostic = SafeMessage(exception);
                                }
                                if (record.payloadState != "invalid_encoding")
                                {
                                    try
                                    {
                                        JsonValue payloadRoot = ContentJson.Parse(payloadText);
                                        if (!(payloadRoot is JsonObject))
                                        {
                                            record.payloadState = "invalid_json";
                                            record.payloadDiagnostic = "The payload JSON root is not an object.";
                                        }
                                        else
                                        {
                                            // Syntactic JSON is not dialogue schema, embedding, or publication validation.
                                            record.payloadState = "unvalidated";
                                            record.payloadDiagnostic = "Payload is preserved but has not passed the host dialogue validator.";
                                        }
                                    }
                                    catch (Exception exception)
                                    {
                                        record.payloadState = "invalid_json";
                                        record.payloadDiagnostic = SafeMessage(exception);
                                    }
                                }
                            }
                            catch (InvalidDataException exception)
                            {
                                return Failure("recovery_required", SafeMessage(exception));
                            }
                            catch (Exception exception)
                            {
                                record.payloadState = "unavailable";
                                record.payloadDiagnostic = SafeMessage(exception);
                            }
                        }
                    }

                    records.Add(record);
                }

                return store.ImportLegacy(ComputeSha256(manifestBytes), manifestBytes, records, payloadCount);
            }
            catch (Exception exception)
            {
                return Failure("recovery_required", SafeMessage(exception));
            }
        }

        internal static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (byte value in sha.ComputeHash(bytes ?? new byte[0]))
                    builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static LegacyGeneratedArchive DeserializeManifest(byte[] bytes)
        {
            var serializer = new DataContractJsonSerializer(typeof(LegacyGeneratedArchive),
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 100000, UseSimpleDictionaryFormat = true });
            using (var input = new MemoryStream(bytes, false))
                return serializer.ReadObject(input) as LegacyGeneratedArchive;
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > maximumBytes) throw new InvalidDataException("Legacy generated archive file exceeds its import limit.");
                var bytes = new byte[checked((int)stream.Length)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new EndOfStreamException("Legacy generated archive file changed while it was being read.");
                    offset += count;
                }
                if (stream.ReadByte() != -1) throw new IOException("Legacy generated archive file changed while it was being read.");
                return bytes;
            }
        }

        private static bool TryResolveContainedPath(string trustedRoot, string locator, out string fullPath, out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(locator)) { error = "The path is empty."; return false; }
                string normalizedLocator = locator.Replace('\\', '/');
                if (normalizedLocator.Split('/').Any(segment => segment == ".." || segment == "."))
                { error = "Path traversal segments are not allowed."; return false; }
                string root = Path.GetFullPath(trustedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (root.Length == 0) root = Path.GetPathRoot(Path.GetFullPath(trustedRoot));
                fullPath = Path.IsPathRooted(locator)
                    ? Path.GetFullPath(locator)
                    : Path.GetFullPath(Path.Combine(root, locator));
                string rootPrefix = root + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                { error = "The path is outside the host-selected StreamingAssets root."; return false; }
                if (HasReparsePoint(fullPath))
                { error = "Reparse-point paths are not allowed for imported archive files."; return false; }
                return true;
            }
            catch (Exception exception) { error = SafeMessage(exception); return false; }
        }

        private static bool HasReparsePoint(string fullPath)
        {
            string current = Path.GetPathRoot(fullPath);
            string remainder = fullPath.Substring(current.Length);
            string[] segments = remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) continue;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            return false;
        }

        private static string SerializeTags(IList<string> tags)
        { return GeneratedArchiveTagCodec.Serialize(tags); }

        private static GeneratedArchiveImportResult Failure(string state, string error)
        { return new GeneratedArchiveImportResult { State = state, Error = error ?? string.Empty }; }

        private static string SafeMessage(Exception exception)
        { return exception == null ? "Generated archive import failed." : exception.Message; }

        [DataContract]
        private sealed class LegacyGeneratedArchive
        { [DataMember(Name = "records", Order = 1)] public List<LegacyGeneratedDialogueRecord> records; }

        [DataContract]
        private sealed class LegacyGeneratedDialogueRecord
        {
            [DataMember(Name = "generatedDialogueId", Order = 1)] public string generatedDialogueId;
            [DataMember(Name = "originSessionId", Order = 2)] public string originSessionId;
            [DataMember(Name = "title", Order = 3)] public string title;
            [DataMember(Name = "sourcePrompt", Order = 4)] public string sourcePrompt;
            [DataMember(Name = "summary", Order = 5)] public string summary;
            [DataMember(Name = "createdAt", Order = 6)] public string createdAt;
            [DataMember(Name = "lastPlayedAt", Order = 7)] public string lastPlayedAt;
            [DataMember(Name = "status", Order = 8)] public int status;
            [DataMember(Name = "wasPlayed", Order = 9)] public bool wasPlayed;
            [DataMember(Name = "surfacedToPlayer", Order = 10)] public bool surfacedToPlayer;
            [DataMember(Name = "tags", Order = 11)] public List<string> tags;
            [DataMember(Name = "jsonPath", Order = 12)] public string jsonPath;
        }
    }
}
