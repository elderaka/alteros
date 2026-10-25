using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlterOS.Conversation;
using UnityEngine;

namespace AlterOS.Interaction
{
    [DefaultExecutionOrder(-32000)]
    public class ConversationSessionStore : MonoBehaviour
    {
        [SerializeField] private string saveFileName = "alteros_conversation_sessions.json";
        [SerializeField] private int maxTurnsPerSession = 120;
        [SerializeField] private float repetitionDecayHours = 12f;

        public ConversationSessionDatabase Database { get; private set; } = new ConversationSessionDatabase();
        public ConversationSessionRecord ActiveSession { get; private set; }
        public bool IsReady { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public string MigrationLegacyPath { get { return LegacyPath; } }
        public string MigrationDatabasePath { get { return DatabasePath; } }

        private SqliteConversationStore store;
        private long revision;
        private string LegacyPath => Path.Combine(Application.persistentDataPath, saveFileName);
        private string DatabasePath => Path.Combine(Application.persistentDataPath, "Data", "conversations.sqlite3");

        private void Awake() { Load(); }

        public ConversationSessionRecord BeginOrResumeSession(string sessionId, string sessionKind = "conversation")
        {
            if (!IsReady) return null;
            if (string.IsNullOrWhiteSpace(sessionId)) sessionId = Guid.NewGuid().ToString("N");
            ConversationSessionDatabase candidate = CloneDatabase(Database);
            ConversationSessionRecord record = candidate.sessions.Find(session =>
                session != null && string.Equals(session.sessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            string now = DateTime.UtcNow.ToString("o");
            if (record == null)
            {
                record = new ConversationSessionRecord
                {
                    sessionId = sessionId,
                    sessionKind = sessionKind,
                    startedAt = now,
                    lastActiveAt = now,
                    isActive = true
                };
                candidate.sessions.Add(record);
            }
            else
            {
                record.sessionKind = string.IsNullOrWhiteSpace(record.sessionKind) ? sessionKind : record.sessionKind;
                record.lastActiveAt = now;
                record.endedAt = string.Empty;
                record.isActive = true;
            }
            return Commit(candidate, record.sessionId) ? ActiveSession : null;
        }

        public ConversationSessionRecord StartNewSession(string sessionId, string sessionKind = "conversation")
        {
            if (ActiveSession != null) EndActiveSession();
            if (!IsReady || ActiveSession != null) return null;
            return BeginOrResumeSession(sessionId, sessionKind);
        }

        public void EndActiveSession()
        {
            if (!IsReady || ActiveSession == null) return;
            ConversationSessionDatabase candidate = CloneDatabase(Database);
            ConversationSessionRecord record = FindSession(candidate, ActiveSession.sessionId);
            if (record == null) { FailClosed("The active conversation session is missing from storage."); return; }
            record.endedAt = DateTime.UtcNow.ToString("o");
            record.isActive = false;
            if (record.routingState == null) record.routingState = new SessionRoutingState();
            record.routingState.activeMode = ActiveConversationMode.None;
            Commit(candidate, null);
        }

        public void RecordTurn(
            string speaker,
            string text,
            ConversationTurnSource source,
            string intentId = null,
            string emotion = null,
            string linkedGeneratedDialogueId = null,
            string translatedText = null)
        {
            if (!IsReady) return;
            ConversationSessionDatabase candidate = CloneDatabase(Database);
            ConversationSessionRecord session = ActiveSession == null ? null : FindSession(candidate, ActiveSession.sessionId);
            if (session == null)
            {
                string newSessionId = Guid.NewGuid().ToString("N");
                string now = DateTime.UtcNow.ToString("o");
                session = new ConversationSessionRecord
                {
                    sessionId = newSessionId,
                    sessionKind = "conversation",
                    startedAt = now,
                    lastActiveAt = now,
                    isActive = true
                };
                candidate.sessions.Add(session);
            }

            ConversationTurnRecord turn = new ConversationTurnRecord
            {
                turnId = Guid.NewGuid().ToString("N"),
                sessionId = session.sessionId,
                speaker = speaker,
                text = text,
                translatedText = translatedText,
                emotion = emotion,
                timestamp = DateTime.UtcNow.ToString("o"),
                source = source,
                intentId = intentId,
                linkedGeneratedDialogueId = linkedGeneratedDialogueId
            };
            session.turns = session.turns ?? new List<ConversationTurnRecord>();
            session.turns.Add(turn);
            session.lastActiveAt = turn.timestamp;
            int turnLimit = Math.Max(1, maxTurnsPerSession);
            if (session.turns.Count > turnLimit)
                session.turns.RemoveRange(0, session.turns.Count - turnLimit);
            Commit(candidate, session.sessionId, "session-turn:" + turn.turnId);
        }

        public void RecordDialogueSequence(
            AlterOS.Dialogue.DialogueSequence sequence,
            ConversationTurnSource source,
            string intentId = null,
            string generatedDialogueId = null)
        {
            if (sequence?.nodes == null) return;
            foreach (AlterOS.Dialogue.DialogueNode node in sequence.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.text)) continue;
                RecordTurn(node.speaker, node.text, source, intentId, node.GetExpressionCue(), generatedDialogueId, node.translatedText);
            }
        }

        public void SetRoutingState(ActiveConversationMode mode, string intentId = null, string generatedDialogueId = null, string lastAssistantSource = null)
        {
            if (!IsReady || ActiveSession == null) return;
            ConversationSessionDatabase candidate = CloneDatabase(Database);
            ConversationSessionRecord session = FindSession(candidate, ActiveSession.sessionId);
            if (session == null) { FailClosed("The active conversation session is missing from storage."); return; }
            if (session.routingState == null) session.routingState = new SessionRoutingState();
            session.routingState.activeMode = mode;
            session.routingState.activeIntentId = intentId;
            session.routingState.activeGeneratedDialogueId = generatedDialogueId;
            session.routingState.lastAssistantSource = lastAssistantSource;
            session.lastActiveAt = DateTime.UtcNow.ToString("o");
            Commit(candidate, session.sessionId);
        }

        public SessionRoutingState GetRoutingState()
        {
            return IsReady && ActiveSession != null && ActiveSession.routingState != null
                ? ActiveSession.routingState : new SessionRoutingState();
        }

        public int RegisterRepetition(string key, float decayHours = -1f)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(key)) return 0;
            ConversationSessionDatabase candidate = CloneDatabase(Database);
            RepetitionMemoryRecord record = GetOrCreateRepetition(candidate, key);
            DateTime now = DateTime.UtcNow;
            float resolvedDecay = decayHours > 0f ? decayHours : repetitionDecayHours;
            if (DateTime.TryParse(record.lastTriggeredAt, out DateTime last) && (now - last).TotalHours > resolvedDecay)
            {
                record.recentCount = 0;
                record.stageUsed = 0;
            }
            record.recentCount++;
            record.lastTriggeredAt = now.ToString("o");
            return Commit(candidate, ActiveSession == null ? null : ActiveSession.sessionId) ? record.recentCount : 0;
        }

        public int GetRepetitionCount(string key)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(key)) return 0;
            RepetitionMemoryRecord record = Database.repetitions.Find(item =>
                item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase));
            return record == null ? 0 : record.recentCount;
        }

        public void RememberAmbientLine(string lineId, string topic, string text)
        {
            if (!IsReady) return;
            ConversationSessionDatabase candidate = CloneDatabase(Database);
            candidate.lastAmbientLine = new AmbientMemoryRecord
            {
                lineId = lineId,
                topic = topic,
                text = text,
                triggeredAt = DateTime.UtcNow.ToString("o"),
                followUpConsumed = false
            };
            Commit(candidate, ActiveSession == null ? null : ActiveSession.sessionId);
        }

        public AmbientMemoryRecord GetLastAmbientLine()
        {
            return IsReady ? Database.lastAmbientLine : new AmbientMemoryRecord();
        }

        public void Load()
        {
            if (!RuntimeMigrationStartup.Ensure().isReady)
            {
                FailClosed("Runtime migration is not ready.");
                return;
            }
            IsReady = false;
            ActiveSession = null;
            LastError = string.Empty;
            try
            {
                store = new SqliteConversationStore(DatabasePath);
                ConversationSessionImportResult imported = ConversationSessionImporter.ImportOnce(LegacyPath, store);
                if (imported.State != "missing" && imported.State != "imported" && imported.State != "already_imported")
                {
                    FailClosed(string.IsNullOrEmpty(imported.Error) ? "Legacy conversation history needs recovery." : imported.Error);
                    return;
                }
                ConversationStoreLoadResult loaded = store.Load();
                if (!loaded.Success)
                {
                    FailClosed(loaded.Error);
                    return;
                }
                Database = loaded.Database;
                revision = loaded.Revision;
                // Stored isActive flags are history only; a Unity restart never resumes a live task.
                ActiveSession = null;
                IsReady = true;
                string sessionId = PlayerPrefs.GetString("AlterOS.Conversation.SessionId", string.Empty);
                string playerId = PlayerPrefs.GetString("AlterOS.PlayerMemory.PlayerId", string.Empty);
                if ((!string.IsNullOrWhiteSpace(sessionId) && !ImportIdentityAlias("AlterOS.Conversation.SessionId", sessionId)) ||
                    (!string.IsNullOrWhiteSpace(playerId) && !ImportIdentityAlias("AlterOS.PlayerMemory.PlayerId", playerId))) return;
            }
            catch (Exception exception)
            {
                FailClosed(exception.Message);
            }
        }

        public bool Save()
        {
            return IsReady && Commit(CloneDatabase(Database), ActiveSession == null ? null : ActiveSession.sessionId);
        }

        public bool ImportIdentityAlias(string key, string value)
        {
            if (!IsReady || store == null) return false;
            ConversationIdentityResult result = store.EnsureIdentity(key, value);
            if (result.State == "existing" || result.State == "imported") return true;
            FailClosed(string.IsNullOrEmpty(result.Error) ? "Conversation identity could not be saved." : result.Error);
            return false;
        }

        private bool Commit(ConversationSessionDatabase candidate, string activeSessionId, string operationId = null)
        {
            if (!IsReady || store == null) return false;
            ConversationStoreSaveResult result = store.SaveSnapshot(candidate, revision,
                string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("D") : operationId);
            if (!result.Success)
            {
                FailClosed(result.Error);
                return false;
            }
            Database = candidate;
            revision = result.Revision;
            ActiveSession = string.IsNullOrEmpty(activeSessionId) ? null : FindSession(Database, activeSessionId);
            LastError = string.Empty;
            return true;
        }

        private void FailClosed(string message)
        {
            IsReady = false;
            LastError = string.IsNullOrWhiteSpace(message) ? "Private conversation storage is unavailable." : message;
            Debug.LogWarning("[ConversationSessionStore] " + LastError);
        }

        private static ConversationSessionDatabase CloneDatabase(ConversationSessionDatabase source)
        {
            var clone = new ConversationSessionDatabase
            {
                sessions = new List<ConversationSessionRecord>(),
                repetitions = new List<RepetitionMemoryRecord>(),
                lastAmbientLine = CloneAmbient(source == null ? null : source.lastAmbientLine)
            };
            if (source == null) return clone;
            foreach (ConversationSessionRecord session in source.sessions ?? new List<ConversationSessionRecord>())
                if (session != null) clone.sessions.Add(CloneSession(session));
            foreach (RepetitionMemoryRecord repetition in source.repetitions ?? new List<RepetitionMemoryRecord>())
                if (repetition != null) clone.repetitions.Add(new RepetitionMemoryRecord
                { key = repetition.key, recentCount = repetition.recentCount, lastTriggeredAt = repetition.lastTriggeredAt, stageUsed = repetition.stageUsed });
            return clone;
        }

        private static ConversationSessionRecord CloneSession(ConversationSessionRecord session)
        {
            var clone = new ConversationSessionRecord
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
                    activeMode = session.routingState.activeMode,
                    activeGeneratedDialogueId = session.routingState.activeGeneratedDialogueId,
                    activeIntentId = session.routingState.activeIntentId,
                    lastAssistantSource = session.routingState.lastAssistantSource
                },
                turns = new List<ConversationTurnRecord>()
            };
            foreach (ConversationTurnRecord turn in session.turns ?? new List<ConversationTurnRecord>())
                if (turn != null) clone.turns.Add(new ConversationTurnRecord
                {
                    turnId = turn.turnId,
                    sessionId = turn.sessionId,
                    speaker = turn.speaker,
                    text = turn.text,
                    translatedText = turn.translatedText,
                    emotion = turn.emotion,
                    timestamp = turn.timestamp,
                    source = turn.source,
                    intentId = turn.intentId,
                    linkedGeneratedDialogueId = turn.linkedGeneratedDialogueId,
                    linkedAmbientLineId = turn.linkedAmbientLineId
                });
            return clone;
        }

        private static AmbientMemoryRecord CloneAmbient(AmbientMemoryRecord ambient)
        {
            return ambient == null ? new AmbientMemoryRecord() : new AmbientMemoryRecord
            {
                lineId = ambient.lineId,
                topic = ambient.topic,
                text = ambient.text,
                triggeredAt = ambient.triggeredAt,
                followUpConsumed = ambient.followUpConsumed
            };
        }

        private static ConversationSessionRecord FindSession(ConversationSessionDatabase database, string sessionId)
        {
            return database.sessions.Find(session => session != null &&
                string.Equals(session.sessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private static RepetitionMemoryRecord GetOrCreateRepetition(ConversationSessionDatabase database, string key)
        {
            RepetitionMemoryRecord record = database.repetitions.Find(item => item != null &&
                string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase));
            if (record != null) return record;
            record = new RepetitionMemoryRecord { key = key };
            database.repetitions.Add(record);
            return record;
        }
    }
}
