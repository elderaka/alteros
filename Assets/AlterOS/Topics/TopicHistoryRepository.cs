using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace AlterOS.Topics
{
    [DataContract]
    internal sealed class TopicHistoryDocument
    {
        [DataMember(IsRequired = true)] public int schemaVersion;
        [DataMember(IsRequired = true)] public List<TopicHistoryRecord> records;
    }

    public sealed class TopicHistoryLoadResult
    {
        public bool Success { get; internal set; }
        public bool Missing { get; internal set; }
        public string Error { get; internal set; }
        public TopicHistory History { get; internal set; }
    }

    public sealed class TopicHistorySaveResult
    {
        public bool Success { get; internal set; }
        public string Error { get; internal set; }
    }

    // Single game-owned writer. Call on the same thread as history mutations.
    public static class TopicHistoryRepository
    {
        private const int MaximumBytes = 4 * 1024 * 1024;
        private const int MaximumRecords = 10000;

        public static TopicHistoryLoadResult Load(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                if (!File.Exists(path))
                {
                    if (Directory.Exists(path)) throw new IOException("History path is a directory.");
                    var empty = new TopicHistory();
                    empty.Saved(path, null);
                    return new TopicHistoryLoadResult { Success = true, Missing = true, History = empty };
                }
                byte[] bytes = ReadBounded(path);
                TopicHistory history = Decode(bytes);
                history.Saved(path, Fingerprint(bytes));
                return new TopicHistoryLoadResult { Success = true, History = history };
            }
            catch (Exception exception)
            {
                return new TopicHistoryLoadResult { Error = exception.Message };
            }
        }

        public static TopicHistorySaveResult Save(string path, TopicHistory history)
        {
            string temporary = null;
            try
            {
                if (history == null) throw new ArgumentNullException("history");
                path = Path.GetFullPath(path);
                bool exists = File.Exists(path);
                if (exists)
                {
                    byte[] current = ReadBounded(path);
                    Decode(current);
                    if (!string.Equals(history.StoragePath, path, StringComparison.OrdinalIgnoreCase) ||
                        history.StorageFingerprint != Fingerprint(current))
                        throw new IOException("History changed on disk; reload before saving.");
                }
                else if (Directory.Exists(path) ||
                    (string.Equals(history.StoragePath, path, StringComparison.OrdinalIgnoreCase) && history.StorageFingerprint != null))
                    throw new IOException("History target was removed or is not a file.");

                var document = new TopicHistoryDocument { schemaVersion = 2, records = new List<TopicHistoryRecord>(history.Records) };
                document.records.Sort((left, right) => StringComparer.Ordinal.Compare(left.instanceId, right.instanceId));
                byte[] bytes;
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(TopicHistoryDocument)).WriteObject(stream, document);
                    bytes = stream.ToArray();
                }
                Decode(bytes);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (exists) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                temporary = null;
                history.Saved(path, Fingerprint(bytes));
                return new TopicHistorySaveResult { Success = true };
            }
            catch (Exception exception)
            {
                return new TopicHistorySaveResult { Error = exception.Message };
            }
            finally
            {
                if (temporary != null)
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumBytes) throw new SerializationException("History is too large.");
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }

        private static TopicHistory Decode(byte[] bytes)
        {
            if (bytes.Length > MaximumBytes) throw new SerializationException("History is too large.");
            TopicHistoryDocument document;
            using (var stream = new MemoryStream(bytes, false))
                document = (TopicHistoryDocument)new DataContractJsonSerializer(typeof(TopicHistoryDocument)).ReadObject(stream);
            if (document == null || (document.schemaVersion != 1 && document.schemaVersion != 2) || document.records == null || document.records.Count > MaximumRecords)
                throw new SerializationException("Unsupported or invalid Topic history.");
            var history = new TopicHistory();
            foreach (TopicHistoryRecord record in document.records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.instanceId) || string.IsNullOrWhiteSpace(record.topicId) ||
                    !TopicReplayPolicy.IsValid(record.replayPolicy) || record.startedUtcTicks <= 0 ||
                    !ValidTicks(record.startedUtcTicks) || !ValidTicks(record.presentedUtcTicks) || !ValidTicks(record.completedUtcTicks) ||
                    !ValidTicks(record.consumedUtcTicks) || !ValidTicks(record.cooldownUntilUtcTicks) ||
                    (record.consumedUtcTicks != 0 && record.replayPolicy != TopicReplayPolicy.Once))
                    throw new SerializationException("Invalid Topic history record.");
                if (record.scenarioCompletions != null)
                {
                    if (record.scenarioCompletions.Count > 256) throw new SerializationException("Too many scenario completion records.");
                    var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (TopicScenarioCompletionRecord completion in record.scenarioCompletions)
                    {
                        if (completion == null || string.IsNullOrWhiteSpace(completion.scenarioId) || !scenarioIds.Add(completion.scenarioId) ||
                            completion.completionCount <= 0 || !ValidTicks(completion.lastCompletedUtcTicks) || completion.lastCompletedUtcTicks == 0)
                            throw new SerializationException("Invalid scenario completion record.");
                    }
                }
                history.Restore(record);
            }
            return history;
        }

        internal static TopicHistory DecodeLegacyBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            return Decode(bytes);
        }

        internal static string SerializeRecordForSql(TopicHistoryRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");
            return SerializeJson(typeof(TopicHistoryRecord), record);
        }

        internal static string SerializeCompletionForSql(TopicScenarioCompletionRecord completion)
        {
            if (completion == null) throw new ArgumentNullException("completion");
            return SerializeJson(typeof(TopicScenarioCompletionRecord), completion);
        }

        internal static TopicHistoryRecord DecodeRecordForSql(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new SerializationException("Topic record payload is missing.");
            byte[] wrapper = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"records\":[" + json + "]}");
            foreach (TopicHistoryRecord record in Decode(wrapper).Records) return record;
            throw new SerializationException("Topic record payload is empty.");
        }

        internal static TopicScenarioCompletionRecord DecodeCompletionForSql(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new SerializationException("Topic completion payload is missing.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), false))
                return (TopicScenarioCompletionRecord)new DataContractJsonSerializer(typeof(TopicScenarioCompletionRecord)).ReadObject(stream);
        }

        private static string SerializeJson(Type type, object value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(type).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static bool ValidTicks(long ticks) { return ticks >= 0 && ticks <= DateTime.MaxValue.Ticks; }
        private static string Fingerprint(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(bytes));
        }
    }
}
