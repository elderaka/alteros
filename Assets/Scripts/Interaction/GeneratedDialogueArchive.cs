using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Content;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using UnityEngine;

namespace AlterOS.Interaction
{
    [Serializable]
    public class GeneratedDialogueArchiveDatabase
    {
        public List<GeneratedDialogueRecord> records = new List<GeneratedDialogueRecord>();
    }

    /// <summary>
    /// Unity-facing generated archive facade. Private SQL is authoritative; the old
    /// StreamingAssets JSON manifest and payload paths are import-only locators.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public class GeneratedDialogueArchive : MonoBehaviour
    {
        [SerializeField] private string manifestRelativePath = "Dialogue/Generated/generated_dialogue_manifest.json";

        public GeneratedDialogueArchiveDatabase Database { get; private set; } = new GeneratedDialogueArchiveDatabase();
        public bool IsReady { get; private set; }
        public string Error { get; private set; } = string.Empty;

        private SqliteGeneratedArchiveStore store;

        private string ManifestPath => Path.Combine(Application.streamingAssetsPath, manifestRelativePath);
        private string DatabasePath => Path.Combine(Application.persistentDataPath, "Data", "personal-content.sqlite3");
        public string MigrationManifestPath { get { return ManifestPath; } }
        public string MigrationDatabasePath { get { return DatabasePath; } }

        private void Awake() { Load(); }

        public GeneratedDialogueRecord RegisterGenerating(string prompt, string title = null, string originSessionId = null)
        {
            if (!IsReady) return null;
            string id = MakeStableId(prompt, title);
            GeneratedDialogueRecord record = GetOrCreate(id);
            record.title = string.IsNullOrWhiteSpace(title) ? record.title : title;
            record.sourcePrompt = prompt;
            record.originSessionId = originSessionId;
            record.createdAt = string.IsNullOrWhiteSpace(record.createdAt) ? DateTime.UtcNow.ToString("o") : record.createdAt;
            record.status = GeneratedDialogueStatus.Generating;
            return Persist(record, null) ? Clone(record) : null;
        }

        public GeneratedDialogueRecord RegisterReadyDialogue(string jsonPath, string json, string sourcePrompt = null, string originSessionId = null)
        {
            if (!IsReady) return null;
            DialogueSequence sequence = null;
            try { sequence = DialogueSequence.FromJson(json); }
            catch { }

            string topic = sequence?.metadata?.topic;
            string idSeed = string.IsNullOrWhiteSpace(sourcePrompt) ? (topic + "|" + jsonPath) : sourcePrompt;
            string id = MakeStableId(idSeed, topic);
            GeneratedDialogueRecord record = GetOrCreate(id);

            record.title = string.IsNullOrWhiteSpace(topic) ? record.title : topic;
            if (string.IsNullOrWhiteSpace(record.title)) record.title = Path.GetFileNameWithoutExtension(jsonPath);
            record.sourcePrompt = string.IsNullOrWhiteSpace(sourcePrompt) ? record.sourcePrompt : sourcePrompt;
            record.originSessionId = string.IsNullOrWhiteSpace(originSessionId) ? record.originSessionId : originSessionId;
            record.summary = BuildSummary(sequence);
            record.createdAt = string.IsNullOrWhiteSpace(record.createdAt) ? DateTime.UtcNow.ToString("o") : record.createdAt;
            record.status = GeneratedDialogueStatus.Ready;
            record.jsonPath = jsonPath;
            record.tags = ExtractTags(sequence);
            byte[] payload = string.IsNullOrWhiteSpace(json) ? null : Encoding.UTF8.GetBytes(json);
            return Persist(record, payload) ? Clone(record) : null;
        }

        public void MarkFailed(string prompt, string title = null)
        {
            if (!IsReady) return;
            GeneratedDialogueRecord record = GetOrCreate(MakeStableId(prompt, title));
            record.title = string.IsNullOrWhiteSpace(title) ? record.title : title;
            record.sourcePrompt = prompt;
            record.status = GeneratedDialogueStatus.Failed;
            Persist(record, null);
        }

        public List<GeneratedDialogueRecord> GetReadyRecords()
        {
            if (!IsReady) return new List<GeneratedDialogueRecord>();
            var result = new List<GeneratedDialogueRecord>();
            foreach (GeneratedDialogueRecord record in Database.records)
                if (record != null && record.status == GeneratedDialogueStatus.Ready && record.payloadState == "validated")
                    result.Add(Clone(record));
            return result;
        }

        public bool TryGetRecord(string generatedDialogueId, out GeneratedDialogueRecord record)
        {
            record = null;
            if (!IsReady || string.IsNullOrWhiteSpace(generatedDialogueId)) return false;
            GeneratedDialogueRecord found = Database.records.Find(item => item != null &&
                string.Equals(item.generatedDialogueId, generatedDialogueId, StringComparison.OrdinalIgnoreCase));
            if (found == null) return false;
            record = Clone(found);
            return true;
        }

        public bool TryFindReadyForInput(string normalizedInput, out GeneratedDialogueRecord record)
        {
            record = null;
            List<GeneratedDialogueRecord> ready = GetReadyRecords();
            if (ready.Count == 0 || string.IsNullOrWhiteSpace(normalizedInput)) return false;

            if (normalizedInput.Contains("prepared") || normalizedInput.Contains("topic") ||
                normalizedInput.Contains("what did you make") || normalizedInput.Contains("what did you find"))
            {
                record = ready[ready.Count - 1];
                return true;
            }

            int bestScore = 0;
            foreach (GeneratedDialogueRecord candidate in ready)
            {
                int score = ScoreRecord(candidate, normalizedInput);
                if (score > bestScore) { bestScore = score; record = candidate; }
            }
            return record != null && bestScore >= 2;
        }

        public bool TryLoadJson(GeneratedDialogueRecord record, out string json)
        {
            json = null;
            if (!IsReady || record == null || record.payloadState != "validated") return false;
            byte[] bytes;
            string payloadState;
            if (!store.TryReadPayload(record.generatedDialogueId, out bytes, out payloadState) || payloadState != "validated") return false;
            try { json = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { json = null; return false; }
            return !string.IsNullOrWhiteSpace(json);
        }

        public void MarkPlayed(string generatedDialogueId)
        {
            GeneratedDialogueRecord record;
            if (!TryGetRecord(generatedDialogueId, out record)) return;
            record.wasPlayed = true;
            record.lastPlayedAt = DateTime.UtcNow.ToString("o");
            Persist(record, null);
        }

        public void Load()
        {
            if (!RuntimeMigrationStartup.Ensure().isReady)
            {
                SetUnavailable("Runtime migration is not ready.");
                return;
            }
            IsReady = false;
            Error = string.Empty;
            Database = new GeneratedDialogueArchiveDatabase();
            try
            {
                store = new SqliteGeneratedArchiveStore(DatabasePath);
                if (!store.IsReady) { SetUnavailable(store.Error); return; }

                GeneratedArchiveImportResult import = GeneratedArchiveImporter.ImportOnce(
                    ManifestPath, Application.streamingAssetsPath, store);
                if (import == null || (import.State != "imported" && import.State != "already_imported" && import.State != "missing"))
                { SetUnavailable(import == null ? "Generated archive import returned no status." : import.Error); return; }

                RefreshFromSql();
                IsReady = true;
            }
            catch (Exception exception) { SetUnavailable(exception.Message); }
        }

        /// <summary>Compatibility refresh only; all mutations commit in their SQL operation.</summary>
        public void Save()
        {
            if (!IsReady || store == null) return;
            try { RefreshFromSql(); }
            catch (Exception exception) { SetUnavailable(exception.Message); }
        }

        private bool Persist(GeneratedDialogueRecord record, byte[] payload)
        {
            if (!IsReady || store == null || record == null) return false;
            GeneratedArchiveRecordData data;
            try { data = ToData(record); }
            catch (Exception exception) { Error = exception.Message; return false; }
            if (!store.SaveRecord(data, payload)) { Error = store.Error; return false; }
            try
            {
                RefreshFromSql();
                Error = string.Empty;
                return true;
            }
            catch (Exception exception) { SetUnavailable(exception.Message); return false; }
        }

        private void RefreshFromSql()
        {
            if (store == null || !store.IsReady) throw new InvalidOperationException(store == null ? "Private generated archive storage is unavailable." : store.Error);
            var next = new GeneratedDialogueArchiveDatabase();
            foreach (GeneratedArchiveRecordData value in store.ReadRecords()) next.records.Add(FromData(value));
            Database = next;
        }

        private void SetUnavailable(string error)
        {
            IsReady = false;
            Error = string.IsNullOrWhiteSpace(error) ? "Private generated archive storage is unavailable." : error;
            Debug.LogWarning("[GeneratedDialogueArchive] " + Error);
        }

        private GeneratedDialogueRecord GetOrCreate(string id)
        {
            GeneratedDialogueRecord existing = Database.records.Find(item => item != null &&
                string.Equals(item.generatedDialogueId, id, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return Clone(existing);
            return new GeneratedDialogueRecord { generatedDialogueId = id, createdAt = DateTime.UtcNow.ToString("o") };
        }

        private static GeneratedArchiveRecordData ToData(GeneratedDialogueRecord record)
        {
            return new GeneratedArchiveRecordData
            {
                generatedDialogueId = record.generatedDialogueId,
                originSessionId = record.originSessionId,
                title = record.title,
                sourcePrompt = record.sourcePrompt,
                summary = record.summary,
                createdAt = record.createdAt,
                lastPlayedAt = record.lastPlayedAt,
                statusCode = (int)record.status,
                wasPlayed = record.wasPlayed,
                surfacedToPlayer = record.surfacedToPlayer,
                tagsJson = GeneratedArchiveTagCodec.Serialize(record.tags),
                jsonPath = record.jsonPath
            };
        }

        private static GeneratedDialogueRecord FromData(GeneratedArchiveRecordData value)
        {
            return new GeneratedDialogueRecord
            {
                generatedDialogueId = value.generatedDialogueId,
                originSessionId = value.originSessionId,
                title = value.title,
                sourcePrompt = value.sourcePrompt,
                summary = value.summary,
                createdAt = value.createdAt,
                lastPlayedAt = value.lastPlayedAt,
                status = Enum.IsDefined(typeof(GeneratedDialogueStatus), value.statusCode)
                    ? (GeneratedDialogueStatus)value.statusCode : GeneratedDialogueStatus.Unknown,
                wasPlayed = value.wasPlayed,
                surfacedToPlayer = value.surfacedToPlayer,
                tags = GeneratedArchiveTagCodec.Deserialize(value.tagsJson),
                jsonPath = value.jsonPath,
                payloadState = value.payloadState,
                payloadHash = value.payloadHash,
                payloadDiagnostic = value.payloadDiagnostic
            };
        }

        private static GeneratedDialogueRecord Clone(GeneratedDialogueRecord value)
        {
            if (value == null) return null;
            return new GeneratedDialogueRecord
            {
                generatedDialogueId = value.generatedDialogueId, originSessionId = value.originSessionId,
                title = value.title, sourcePrompt = value.sourcePrompt, summary = value.summary,
                createdAt = value.createdAt, lastPlayedAt = value.lastPlayedAt, status = value.status,
                wasPlayed = value.wasPlayed, surfacedToPlayer = value.surfacedToPlayer,
                tags = value.tags == null ? new List<string>() : new List<string>(value.tags),
                jsonPath = value.jsonPath, payloadState = value.payloadState,
                payloadHash = value.payloadHash, payloadDiagnostic = value.payloadDiagnostic
            };
        }

        private static string MakeStableId(string prompt, string title)
        {
            string seed = (prompt ?? "") + "|" + (title ?? "");
            if (string.IsNullOrWhiteSpace(seed)) seed = Guid.NewGuid().ToString("N");
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(seed.Trim().ToLowerInvariant());
                byte[] hash = md5.ComputeHash(bytes);
                var builder = new StringBuilder("gd_");
                for (int i = 0; i < 8; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string BuildSummary(DialogueSequence sequence)
        {
            if (sequence?.nodes == null) return string.Empty;
            foreach (DialogueNode node in sequence.nodes)
            {
                if (node != null && !string.IsNullOrWhiteSpace(node.translatedText)) return node.translatedText;
                if (node != null && !string.IsNullOrWhiteSpace(node.text)) return node.text;
            }
            return string.Empty;
        }

        private static List<string> ExtractTags(DialogueSequence sequence)
        {
            var tags = new List<string>();
            if (sequence?.metadata?.tags != null)
                foreach (DialogueTag tag in sequence.metadata.tags)
                    if (tag != null && !string.IsNullOrWhiteSpace(tag.key))
                        tags.Add(string.IsNullOrWhiteSpace(tag.value) ? tag.key : tag.key + ":" + tag.value);
            if (sequence?.metadata?.topicSuggestions != null)
                foreach (TopicSuggestion suggestion in sequence.metadata.topicSuggestions)
                    if (!string.IsNullOrWhiteSpace(suggestion?.tag)) tags.Add(suggestion.tag);
            return tags;
        }

        private static int ScoreRecord(GeneratedDialogueRecord record, string normalizedInput)
        {
            int score = 0;
            string haystack = (record.title + " " + record.sourcePrompt + " " + record.summary + " " +
                string.Join(" ", record.tags ?? new List<string>())).ToLowerInvariant();
            foreach (string token in normalizedInput.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (token.Length >= 3 && haystack.Contains(token)) score++;
            return score;
        }
    }
}
