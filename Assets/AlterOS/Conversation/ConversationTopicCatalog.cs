using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using AlterOS.Content;
using AlterOS.Topics;
using UnityEngine;

namespace AlterOS.Conversation
{
    [DataContract]
    internal sealed class ConversationTopicMigrationMap
    {
        [DataMember(IsRequired = true)] public int schemaVersion;
        [DataMember(IsRequired = true)] public string status;
        [DataMember(IsRequired = true)] public int topicCount;
        [DataMember(IsRequired = true)] public List<ConversationTopicMigrationRow> mapping;
    }

    [DataContract]
    internal sealed class ConversationTopicMigrationRow
    {
        [DataMember(IsRequired = true)] public string sourceFile;
        [DataMember(IsRequired = true)] public string legacyDialogueId;
        [DataMember(IsRequired = true)] public string legacyRouteId;
        [DataMember(IsRequired = true)] public string topicId;
        [DataMember(IsRequired = true)] public string scenarioId;
        [DataMember(IsRequired = true)] public string stagedFile;
    }

    public sealed class ConversationTopicCatalogLoadResult
    {
        public readonly bool Success;
        public readonly string Error;
        public readonly IReadOnlyDictionary<string, TopicDefinition> Catalogue;
        public readonly IReadOnlyDictionary<string, TopicInstance> AuthoredInstances;
        public readonly ConversationCoveragePolicy CoveragePolicy;
        internal ConversationTopicCatalogLoadResult(bool success, string error, IDictionary<string, TopicDefinition> catalogue,
            IDictionary<string, TopicInstance> instances, ConversationCoveragePolicy policy)
        {
            Success = success; Error = error ?? string.Empty;
            Catalogue = new Dictionary<string, TopicDefinition>(catalogue ?? new Dictionary<string, TopicDefinition>(), StringComparer.Ordinal);
            AuthoredInstances = new Dictionary<string, TopicInstance>(instances ?? new Dictionary<string, TopicInstance>(), StringComparer.Ordinal);
            CoveragePolicy = policy;
        }
    }

    /// <summary>Loads only the promoted, validated Topic payload; no GTE worker or route index is created here.</summary>
    public static class ConversationTopicCatalog
    {
        public static ConversationTopicCatalogLoadResult LoadFromStreamingAssets()
        {
            ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!opened.IsReady)
                return Failure(opened.diagnostics.Count == 0 ? opened.status : opened.diagnostics[0].code);
            return Load(opened.value);
        }

        public static ConversationTopicCatalogLoadResult Load(RuntimeContentStore store)
        {
            var definitions = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal);
            var instances = new Dictionary<string, TopicInstance>(StringComparer.Ordinal);
            if (store == null) return Failure("catalogue_unavailable");
            try
            {
                var migratedDefinitions = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal);
                foreach (RuntimeContentDocument document in store.ReadDocuments(ContentSourceKinds.TopicDefinition))
                {
                    TopicDefinition definition = ReadJson<TopicDefinition>(document.json);
                    string fileName = Path.GetFileNameWithoutExtension(document.relativePath);
                    if (definition == null || fileName != definition.topicId ||
                        TopicDefinitionValidator.Validate(definition).Count != 0 || definitions.ContainsKey(definition.topicId))
                        return Failure("catalogue_invalid");
                    definitions.Add(definition.topicId, definition);
                    instances.Add(definition.topicId, TopicInstance.Create(definition, definition.topicId, "authored"));
                    if (document.relativePath.StartsWith("Topics/topics/", StringComparison.Ordinal))
                        migratedDefinitions.Add(definition.topicId, definition);
                }

                ContentResult<RuntimeContentDocument> mapDocument = store.ReadDocument("Topics/migration-map.json");
                ContentResult<RuntimeContentDocument> coverageDocument = store.ReadDocument("ConversationCoverage.json");
                if (!mapDocument.IsReady || !coverageDocument.IsReady || definitions.Count == 0)
                    return Failure("catalogue_unavailable");
                ConversationTopicMigrationMap map = ReadJson<ConversationTopicMigrationMap>(mapDocument.value.json);
                string mapError = ValidateMigrationMap(map, migratedDefinitions);
                if (mapError != null) return Failure(mapError);
                ConversationCoveragePolicy policy = ConversationCoverage.ParsePolicy(coverageDocument.value.json);
                if (ConversationCoverage.ValidatePolicy(policy, definitions).Count != 0) return Failure("coverage_invalid");
                return new ConversationTopicCatalogLoadResult(true, string.Empty, definitions, instances, policy);
            }
            catch (Exception) { return Failure("catalogue_invalid"); }
        }

        public static ConversationTopicCatalogLoadResult Load(string root, string coveragePath)
        {
            var definitions = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal);
            var instances = new Dictionary<string, TopicInstance>(StringComparer.Ordinal);
            try
            {
                string topicsPath = Path.Combine(root, "topics");
                string manifestPath = Path.Combine(root, "migration-map.json");
                if (!Directory.Exists(topicsPath) || !File.Exists(manifestPath) || !File.Exists(coveragePath))
                    return Failure("catalogue_unavailable");

                foreach (string path in Directory.GetFiles(topicsPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
                {
                    TopicDefinition definition = Read<TopicDefinition>(path);
                    if (definition == null || Path.GetFileNameWithoutExtension(path) != definition.topicId ||
                        TopicDefinitionValidator.Validate(definition).Count != 0 || definitions.ContainsKey(definition.topicId))
                        return Failure("catalogue_invalid");
                    definitions.Add(definition.topicId, definition);
                    instances.Add(definition.topicId, TopicInstance.Create(definition, definition.topicId, "authored"));
                }

                ConversationTopicMigrationMap map = Read<ConversationTopicMigrationMap>(manifestPath);
                string mapError = ValidateMigrationMap(map, definitions);
                if (mapError != null) return Failure(mapError);
                ConversationCoveragePolicy policy = ConversationCoverage.ParsePolicy(File.ReadAllText(coveragePath));
                if (ConversationCoverage.ValidatePolicy(policy, definitions).Count != 0) return Failure("coverage_invalid");
                return new ConversationTopicCatalogLoadResult(true, string.Empty, definitions, instances, policy);
            }
            catch (Exception) { return Failure("catalogue_invalid"); }
        }

        private static ConversationTopicCatalogLoadResult Failure(string code)
        { return new ConversationTopicCatalogLoadResult(false, code, null, null, null); }

    private static T Read<T>(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

    private static T ReadJson<T>(string json)
    {
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(json ?? string.Empty);
        using (var stream = new MemoryStream(bytes))
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }

        private static string ValidateMigrationMap(ConversationTopicMigrationMap map,
            IReadOnlyDictionary<string, TopicDefinition> definitions)
        {
            if (map == null || map.schemaVersion != 1 || map.status != "staged-not-published" ||
                map.mapping == null || definitions == null || map.topicCount != definitions.Count)
                return "catalogue_invalid";
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var scenarios = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversationTopicMigrationRow row in map.mapping)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.sourceFile) || string.IsNullOrWhiteSpace(row.topicId) ||
                    string.IsNullOrWhiteSpace(row.scenarioId) || string.IsNullOrWhiteSpace(row.legacyRouteId) ||
                    string.IsNullOrWhiteSpace(row.legacyDialogueId) || !sources.Add(row.sourceFile) || row.topicId != row.legacyRouteId)
                    return "catalogue_invalid";
                string normalized = row.sourceFile.Replace('\\', '/');
                string[] parts = normalized.Split('/');
                if (parts.Length < 2 || parts[parts.Length - 2] != row.legacyRouteId ||
                    Path.GetFileNameWithoutExtension(parts[parts.Length - 1]) != row.scenarioId ||
                    row.stagedFile != "topics/" + row.topicId + ".json") return "catalogue_invalid";
                TopicDefinition definition;
                if (!definitions.TryGetValue(row.topicId, out definition) ||
                    !definition.scenarios.Exists(scenario => scenario != null && scenario.scenarioId == row.scenarioId) ||
                    !scenarios.Add(row.topicId + "/" + row.scenarioId)) return "catalogue_invalid";
            }
            int scenarioCount = definitions.Values.Sum(definition => definition.scenarios.Count);
            return scenarios.Count == scenarioCount ? null : "catalogue_invalid";
        }
    }
}
