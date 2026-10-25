using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using AlterOS.Topics;

namespace AlterOS.Tools
{
    [DataContract]
    public sealed class TopicCatalogueManifest
    {
        [DataMember(IsRequired = true)] public int schemaVersion;
        [DataMember(IsRequired = true)] public string status;
        [DataMember(IsRequired = true)] public string sourceRoot;
        [DataMember(IsRequired = true)] public int sourceFileCount;
        [DataMember(IsRequired = true)] public int topicCount;
        [DataMember(IsRequired = true)] public List<double> priorityLevels;
        [DataMember(IsRequired = true)] public List<TopicCatalogueMappingRow> mapping;
    }

    [DataContract]
    public sealed class TopicCatalogueMappingRow
    {
        [DataMember(IsRequired = true)] public string sourceFile;
        [DataMember(IsRequired = true)] public string legacyDialogueId;
        [DataMember(IsRequired = true)] public string legacyRouteId;
        [DataMember(IsRequired = true)] public string topicId;
        [DataMember(IsRequired = true)] public string scenarioId;
        [DataMember(IsRequired = true)] public double legacyPriority;
        [DataMember(IsRequired = true)] public int scenarioPriority;
        [DataMember(IsRequired = true)] public string stagedFile;
    }

    public static class TopicCatalogueValidator
    {
        public static List<string> ValidateFile(string path)
        {
            try
            {
                TopicDefinition definition = Read<TopicDefinition>(path);
                return TopicDefinitionValidator.Validate(definition);
            }
            catch (Exception error)
            {
                return new List<string> { "Cannot deserialize Topic JSON: " + error.Message };
            }
        }

        public static string ValidateDirectory(string root)
        {
            var errors = new List<string>();
            string topicsPath = Path.Combine(root, "topics");
            string manifestPath = Path.Combine(root, "migration-map.json");
            if (!Directory.Exists(topicsPath) || !File.Exists(manifestPath))
                return "ERROR: expected topics/ and migration-map.json under " + root;

            TopicCatalogueManifest manifest;
            try { manifest = Read<TopicCatalogueManifest>(manifestPath); }
            catch (Exception error) { return "ERROR: cannot read migration map: " + error.Message; }
            if (manifest.schemaVersion != 1 || manifest.status != "staged-not-published" ||
                manifest.mapping == null || manifest.priorityLevels == null)
                return "ERROR: unsupported or incomplete migration map.";

            string[] files = Directory.GetFiles(topicsPath, "*.json", SearchOption.TopDirectoryOnly);
            if (files.Length != manifest.topicCount || manifest.sourceFileCount != manifest.mapping.Count)
                errors.Add("Manifest counts do not match staged files/mapping rows.");

            var definitions = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal);
            foreach (string file in files.OrderBy(p => p, StringComparer.Ordinal))
            {
                List<string> fileErrors = ValidateFile(file);
                if (fileErrors.Count > 0)
                {
                    errors.Add(Path.GetFileName(file) + ": " + string.Join(" ", fileErrors));
                    continue;
                }
                TopicDefinition definition = Read<TopicDefinition>(file);
                if (Path.GetFileNameWithoutExtension(file) != definition.topicId ||
                    definitions.ContainsKey(definition.topicId))
                    errors.Add(Path.GetFileName(file) + ": topicId must uniquely match its filename.");
                else definitions.Add(definition.topicId, definition);
            }

            var sourceFiles = new HashSet<string>(StringComparer.Ordinal);
            var mappedScenarios = new HashSet<string>(StringComparer.Ordinal);
            foreach (TopicCatalogueMappingRow row in manifest.mapping)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.sourceFile) ||
                    string.IsNullOrWhiteSpace(row.legacyDialogueId) || string.IsNullOrWhiteSpace(row.legacyRouteId) ||
                    string.IsNullOrWhiteSpace(row.scenarioId) || !sourceFiles.Add(row.sourceFile))
                { errors.Add("Migration map contains an empty or duplicate source row."); continue; }
                string normalizedSource = row.sourceFile.Replace('\\', '/');
                string[] sourceParts = normalizedSource.Split('/');
                string sourceStem = Path.GetFileNameWithoutExtension(sourceParts[sourceParts.Length - 1]);
                if (row.topicId != row.legacyRouteId || sourceParts.Length < 2 ||
                    sourceParts[sourceParts.Length - 2] != row.legacyRouteId || row.scenarioId != sourceStem)
                { errors.Add(row.sourceFile + ": route/filename identity does not match its staged Topic/scenario."); continue; }
                TopicDefinition definition;
                string expectedFile = "topics/" + row.topicId + ".json";
                if (row.stagedFile != expectedFile || !definitions.TryGetValue(row.topicId, out definition))
                { errors.Add(row.sourceFile + ": mapped Topic file is missing or inconsistent."); continue; }
                if (!mappedScenarios.Add(row.topicId + "/" + row.scenarioId) ||
                    !definition.scenarios.Any(s => s.scenarioId == row.scenarioId))
                    errors.Add(row.sourceFile + ": mapped scenario is missing or duplicated.");
            }
            int scenarioCount = definitions.Values.Sum(d => d.scenarios.Count);
            if (mappedScenarios.Count != scenarioCount)
                errors.Add("Every staged scenario must have exactly one source mapping row.");

            if (errors.Count > 0) return "ERROR: " + string.Join(Environment.NewLine, errors);
            return "PASS: " + files.Length + " Topic families, " + scenarioCount +
                " scenarios, " + sourceFiles.Count + " mapped legacy files validated by TopicDefinitionValidator.";
        }

        private static T Read<T>(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
