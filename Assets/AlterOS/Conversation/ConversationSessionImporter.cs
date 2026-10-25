using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Interaction;

namespace AlterOS.Conversation
{
    public static class ConversationSessionImporter
    {
        private const int MaximumSourceBytes = 32 * 1024 * 1024;

        public static ConversationSessionImportResult ImportOnce(string legacyPath, SqliteConversationStore store)
        {
            if (store == null) return new ConversationSessionImportResult { State = "recovery_required", Error = "The private conversation store is unavailable." };
            ConversationSessionImportResult beforeRead = store.CheckImportState();
            if (beforeRead.State != "missing") return beforeRead;
            if (string.IsNullOrWhiteSpace(legacyPath)) return store.ImportMissing();

            string fullPath;
            try { fullPath = Path.GetFullPath(legacyPath); }
            catch (Exception exception) { return Failure(exception); }
            if (!File.Exists(fullPath))
            {
                if (Directory.Exists(fullPath)) return new ConversationSessionImportResult { State = "recovery_required", Error = "The legacy session path is a directory." };
                return store.ImportMissing();
            }

            byte[] sourceBytes;
            try { sourceBytes = ReadSnapshot(fullPath); }
            catch (Exception exception) { return Failure(exception); }
            string sourceHash = Sha256(sourceBytes);

            ConversationSessionDatabase database;
            try { database = Decode(sourceBytes); }
            catch (Exception exception)
            { return new ConversationSessionImportResult { State = "recovery_required", SourceHash = sourceHash, Error = SafeMessage(exception) }; }
            return store.ImportLegacy(sourceHash, sourceBytes, database);
        }

        private static ConversationSessionDatabase Decode(byte[] sourceBytes)
        {
            if (sourceBytes == null || sourceBytes.Length == 0 || sourceBytes.Length > MaximumSourceBytes)
                throw new SerializationException("Legacy conversation sessions are empty or exceed the import limit.");
            LegacyDatabaseDto document;
            using (var stream = new MemoryStream(sourceBytes, false))
                document = (LegacyDatabaseDto)new DataContractJsonSerializer(typeof(LegacyDatabaseDto)).ReadObject(stream);
            if (document == null || (document.schemaVersion != 0 && document.schemaVersion != 1))
                throw new SerializationException("Unsupported legacy conversation session schema.");

            var result = new ConversationSessionDatabase
            {
                sessions = new List<ConversationSessionRecord>(),
                repetitions = new List<RepetitionMemoryRecord>(),
                lastAmbientLine = document.lastAmbientLine == null ? new AmbientMemoryRecord() : new AmbientMemoryRecord
                {
                    lineId = document.lastAmbientLine.lineId,
                    topic = document.lastAmbientLine.topic,
                    text = document.lastAmbientLine.text,
                    triggeredAt = document.lastAmbientLine.triggeredAt,
                    followUpConsumed = document.lastAmbientLine.followUpConsumed
                }
            };

            foreach (LegacySessionDto session in document.sessions ?? new List<LegacySessionDto>())
            {
                if (session == null) throw new SerializationException("Legacy conversation session entry is null.");
                if (session.routingState != null && !Enum.IsDefined(typeof(ActiveConversationMode), session.routingState.activeMode))
                    throw new SerializationException("Legacy conversation routing mode is unsupported.");
                var mapped = new ConversationSessionRecord
                {
                    sessionId = session.sessionId,
                    sessionKind = session.sessionKind,
                    startedAt = session.startedAt,
                    endedAt = session.endedAt,
                    lastActiveAt = session.lastActiveAt,
                    title = session.title,
                    summary = session.summary,
                    isActive = session.isActive,
                    routingState = session.routingState == null ? new SessionRoutingState() : new SessionRoutingState
                    {
                        activeMode = (ActiveConversationMode)session.routingState.activeMode,
                        activeGeneratedDialogueId = session.routingState.activeGeneratedDialogueId,
                        activeIntentId = session.routingState.activeIntentId,
                        lastAssistantSource = session.routingState.lastAssistantSource
                    },
                    turns = new List<ConversationTurnRecord>()
                };
                foreach (LegacyTurnDto turn in session.turns ?? new List<LegacyTurnDto>())
                {
                    if (turn == null || !Enum.IsDefined(typeof(ConversationTurnSource), turn.source))
                        throw new SerializationException("Legacy conversation turn source is unsupported.");
                    mapped.turns.Add(new ConversationTurnRecord
                    {
                        turnId = turn.turnId,
                        sessionId = turn.sessionId,
                        speaker = turn.speaker,
                        text = turn.text,
                        translatedText = turn.translatedText,
                        emotion = turn.emotion,
                        timestamp = turn.timestamp,
                        source = (ConversationTurnSource)turn.source,
                        intentId = turn.intentId,
                        linkedGeneratedDialogueId = turn.linkedGeneratedDialogueId,
                        linkedAmbientLineId = turn.linkedAmbientLineId
                    });
                }
                result.sessions.Add(mapped);
            }

            foreach (LegacyRepetitionDto repetition in document.repetitions ?? new List<LegacyRepetitionDto>())
            {
                if (repetition == null) throw new SerializationException("Legacy repetition entry is null.");
                result.repetitions.Add(new RepetitionMemoryRecord
                { key = repetition.key, recentCount = repetition.recentCount, lastTriggeredAt = repetition.lastTriggeredAt, stageUsed = repetition.stageUsed });
            }
            return result;
        }

        private static byte[] ReadSnapshot(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumSourceBytes) throw new InvalidDataException("Legacy conversation sessions exceed the import limit.");
                using (var memory = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (memory.Length + read > MaximumSourceBytes) throw new InvalidDataException("Legacy conversation sessions exceed the import limit.");
                        memory.Write(buffer, 0, read);
                    }
                    return memory.ToArray();
                }
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (byte value in hash.ComputeHash(bytes)) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static ConversationSessionImportResult Failure(Exception exception)
        { return new ConversationSessionImportResult { State = "recovery_required", Error = SafeMessage(exception) }; }
        private static string SafeMessage(Exception exception)
        { return exception == null ? "Conversation session import failed." : exception.Message; }

        [DataContract]
        private sealed class LegacyDatabaseDto
        {
            [DataMember] public int schemaVersion;
            [DataMember] public List<LegacySessionDto> sessions;
            [DataMember] public List<LegacyRepetitionDto> repetitions;
            [DataMember] public LegacyAmbientDto lastAmbientLine;
        }

        [DataContract]
        private sealed class LegacySessionDto
        {
            [DataMember] public string sessionId;
            [DataMember] public string sessionKind;
            [DataMember] public string startedAt;
            [DataMember] public string endedAt;
            [DataMember] public string lastActiveAt;
            [DataMember] public string title;
            [DataMember] public string summary;
            [DataMember] public bool isActive;
            [DataMember] public LegacyRoutingDto routingState;
            [DataMember] public List<LegacyTurnDto> turns;
        }

        [DataContract]
        private sealed class LegacyRoutingDto
        {
            [DataMember] public int activeMode;
            [DataMember] public string activeGeneratedDialogueId;
            [DataMember] public string activeIntentId;
            [DataMember] public string lastAssistantSource;
        }

        [DataContract]
        private sealed class LegacyTurnDto
        {
            [DataMember] public string turnId;
            [DataMember] public string sessionId;
            [DataMember] public string speaker;
            [DataMember] public string text;
            [DataMember] public string translatedText;
            [DataMember] public string emotion;
            [DataMember] public string timestamp;
            [DataMember] public int source;
            [DataMember] public string intentId;
            [DataMember] public string linkedGeneratedDialogueId;
            [DataMember] public string linkedAmbientLineId;
        }

        [DataContract]
        private sealed class LegacyRepetitionDto
        {
            [DataMember] public string key;
            [DataMember] public int recentCount;
            [DataMember] public string lastTriggeredAt;
            [DataMember] public int stageUsed;
        }

        [DataContract]
        private sealed class LegacyAmbientDto
        {
            [DataMember] public string lineId;
            [DataMember] public string topic;
            [DataMember] public string text;
            [DataMember] public string triggeredAt;
            [DataMember] public bool followUpConsumed;
        }
    }
}
