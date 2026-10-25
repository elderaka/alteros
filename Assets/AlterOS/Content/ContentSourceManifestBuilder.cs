using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    /// <summary>Creates an explicit hash-pinned manifest for every supported authored Dialogue JSON file.</summary>
    public static class ContentSourceManifestBuilder
    {
        public static ContentResult<string> ComputeGenericCorpusHash(string genericRoot)
        {
            if (string.IsNullOrWhiteSpace(genericRoot))
                return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_root_required", "generic_source", "A Generic content root is required.");
            string root;
            try { root = Path.GetFullPath(genericRoot); }
            catch (Exception exception)
            { return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_root_invalid", "generic_source", exception.Message); }
            if (!Directory.Exists(root))
                return ContentResult<string>.Failure(ContentStatuses.Missing, "generic_content_root_missing", root, "The Generic content root does not exist.");
            try
            {
                List<string> files = EnumerateJsonFiles(root).OrderBy(item => RelativePath(root, item), StringComparer.Ordinal).ToList();
                if (files.Count == 0)
                    return ContentResult<string>.Failure(ContentStatuses.Missing, "generic_content_empty", root, "The Generic content root contains no JSON source files.");
                using (SHA256 hash = SHA256.Create())
                {
                    foreach (string file in files)
                    {
                        byte[] pathBytes = Encoding.UTF8.GetBytes(RelativePath(root, file));
                        byte[] contentBytes = File.ReadAllBytes(file);
                        hash.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);
                        hash.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return ContentResult<string>.Ready(ToHex(hash.Hash));
                }
            }
            catch (Exception exception)
            { return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_hash_failed", root, exception.Message); }
        }

        /// <summary>Computes the existing GTE profile hash from exact selected SQLite source bytes.</summary>
        public static ContentResult<string> ComputeGenericCorpusHashFromDocuments(IEnumerable<ContentSourceDocument> documents)
        {
            if (documents == null)
                return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_sources_required", "generic_source", "An explicit SQL source selection is required.");
            try
            {
                var ordered = new List<KeyValuePair<string, byte[]>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ContentSourceDocument document in documents)
                {
                    if (document == null || document.kind != ContentSourceKinds.GenericDialogue) continue;
                    string path = (document.logicalPath ?? string.Empty).Replace('\\', '/');
                    if (!path.StartsWith("Generic/", StringComparison.Ordinal) || path.Length <= "Generic/".Length)
                        return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_path_invalid", path, "A Generic source must retain its Generic/ logical path.");
                    string relative = path.Substring("Generic/".Length);
                    if (!seen.Add(relative))
                        return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_path_duplicate", relative, "The selected SQL snapshot contains a duplicate Generic path.");
                    byte[] bytes = document.utf8Bytes;
                    if (bytes.Length == 0 || !string.Equals(Hash(bytes), document.hash, StringComparison.Ordinal))
                        return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_hash_invalid", path, "A Generic source differs from its immutable source hash.");
                    ordered.Add(new KeyValuePair<string, byte[]>(relative, bytes));
                }
                if (ordered.Count == 0)
                    return ContentResult<string>.Failure(ContentStatuses.Missing, "generic_content_empty", "generic_source", "The selected authoring snapshot contains no Generic sources.");
                ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
                using (SHA256 hash = SHA256.Create())
                {
                    foreach (KeyValuePair<string, byte[]> item in ordered)
                    {
                        byte[] pathBytes = Encoding.UTF8.GetBytes(item.Key);
                        hash.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);
                        hash.TransformBlock(item.Value, 0, item.Value.Length, item.Value, 0);
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return ContentResult<string>.Ready(ToHex(hash.Hash));
                }
            }
            catch (Exception exception)
            { return ContentResult<string>.Failure(ContentStatuses.Invalid, "generic_content_hash_failed", "generic_source", exception.Message); }
        }

        public static ContentResult<ContentSourceManifest> Create(string sourceRoot, string packageId, string storeId)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(storeId))
                return Failure("content_source_root_invalid", "A source root, package ID and store ID are required.");
            string root;
            Guid parsedStore;
            try { root = Path.GetFullPath(sourceRoot); parsedStore = new Guid(storeId); }
            catch (Exception exception) { return Failure("content_source_root_invalid", exception.Message); }
            if (!Directory.Exists(root)) return Failure("content_source_root_missing", "The authored Dialogue source root does not exist: " + root);
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    return Failure("content_source_root_reparse_point", "A reparse point cannot be used as the authored Dialogue source root.");
                List<string> paths = EnumerateJsonFiles(root);
                if (paths.Count == 0) return Failure("content_source_inventory_empty", "The authored Dialogue source root has no JSON content.");

                var manifest = new ContentSourceManifest { packageId = packageId, storeId = parsedStore.ToString("D"), sourceRoot = root };
                foreach (string path in paths.OrderBy(item => RelativePath(root, item), StringComparer.Ordinal))
                {
                    string relative = RelativePath(root, path);
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024)
                        return Failure("content_source_size_invalid", "An authored source is empty or exceeds four MiB: " + relative);
                    JsonValue document;
                    try { document = ContentJson.Parse(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')); }
                    catch (Exception exception) { return Failure("content_source_json_invalid", relative + ": " + exception.Message); }
                    string kind, error;
                    if (!TryClassify(relative, document, out kind, out error))
                        return Failure("content_source_format_unsupported", relative + ": " + error);
                    manifest.sources.Add(new ContentSourceFile { kind = kind, relativePath = relative, sha256 = Hash(bytes) });
                }
                return ContentResult<ContentSourceManifest>.Ready(manifest);
            }
            catch (Exception exception) { return Failure("content_source_inventory_failed", exception.Message); }
        }

        private static List<string> EnumerateJsonFiles(string root)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string child in Directory.GetDirectories(directory).OrderByDescending(item => item, StringComparer.Ordinal))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("A content source directory cannot be a reparse point: " + child);
                    pending.Push(child);
                }
                foreach (string file in Directory.GetFiles(directory, "*.json").OrderBy(item => item, StringComparer.Ordinal))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("A content source cannot be a reparse point: " + file);
                    files.Add(file);
                }
            }
            return files;
        }

        private static bool TryClassify(string relative, JsonValue document, out string kind, out string error)
        {
            kind = string.Empty;
            error = string.Empty;
            string path = relative.Replace('\\', '/');
            if (path.StartsWith("Generic/", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.GenericDialogue;
            else if (path.StartsWith("Topics/topics/", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.TopicDefinition;
            else if (path.Equals("Topics/migration-map.json", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.TopicMigrationMap;
            else if (path.Equals("ConversationCoverage.json", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.ConversationCoverage;
            else if (path.Equals("topic/activity_scenarios.json", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.ActivityScenarioLibrary;
            else if (path.Equals("topic/activity_options.json", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.ActivityGreetingMenu;
            else if (path.Equals("Onboarding/first_meeting.json", StringComparison.OrdinalIgnoreCase)) kind = ContentSourceKinds.OnboardingSteps;
            else
            {
                JsonObject root = document as JsonObject;
                if (root == null) { error = "the JSON root must be an object."; return false; }
                if (root.ContainsKey("metadata") && root.ContainsKey("nodes")) kind = ContentSourceKinds.LegacySequence;
                else if (root.ContainsKey("metadata") && root.ContainsKey("lines")) kind = ContentSourceKinds.AmbientPool;
                else { error = "no registered authored source adapter matches this path and JSON shape."; return false; }
            }

            JsonObject item = document as JsonObject;
            if (item == null) { error = "the selected source format requires a JSON object."; return false; }
            if (kind == ContentSourceKinds.GenericDialogue && !(item.ContainsKey("routing") && item.ContainsKey("nodes") && item.ContainsKey("source")))
                error = "Generic Dialogue requires routing, source and nodes.";
            else if (kind == ContentSourceKinds.TopicDefinition && !(item.ContainsKey("topicId") && item.ContainsKey("scenarios")))
                error = "TopicDefinition requires topicId and scenarios.";
            else if (kind == ContentSourceKinds.TopicMigrationMap && !(item.ContainsKey("mapping") && item.ContainsKey("topicCount")))
                error = "Topic migration map requires mapping and topicCount.";
            else if (kind == ContentSourceKinds.ConversationCoverage && !(item.ContainsKey("schemaVersion") && item.ContainsKey("topics")))
                error = "Conversation coverage requires schemaVersion and topics.";
            else if (kind == ContentSourceKinds.ActivityScenarioLibrary && !(item.ContainsKey("entries") && item["entries"] is JsonArray))
                error = "Activity Scenario library requires an entries array.";
            else if (kind == ContentSourceKinds.ActivityGreetingMenu && !(item.ContainsKey("options") && item["options"] is JsonArray))
                error = "Activity greeting menu requires an options array.";
            else if (kind == ContentSourceKinds.OnboardingSteps && !(item.ContainsKey("id") && item.ContainsKey("steps") && item["steps"] is JsonArray))
                error = "Onboarding steps require an ID and steps array.";
            else if (kind == ContentSourceKinds.LegacySequence && !(item["metadata"] is JsonObject && item["nodes"] is JsonArray))
                error = "Legacy DialogueSequence requires metadata and nodes.";
            else if (kind == ContentSourceKinds.AmbientPool && !(item["metadata"] is JsonObject && item["lines"] is JsonArray))
                error = "Ambient pool requires metadata and lines.";
            return error.Length == 0;
        }

        private static string RelativePath(string root, string path)
        { return path.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1).Replace('\\', '/'); }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return ToHex(hash.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static ContentResult<ContentSourceManifest> Failure(string code, string message)
        { return ContentResult<ContentSourceManifest>.Failure(ContentStatuses.Invalid, code, "content_sources", message); }
    }
}
