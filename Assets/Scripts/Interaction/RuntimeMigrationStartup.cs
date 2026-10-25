using System.IO;
using AlterOS.Content;
using AlterOS.Conversation;
using AlterOS.Persistence;
using UnityEngine;

namespace AlterOS.Interaction
{
    /// <summary>Builds the trusted startup migration request from host paths and current local settings.</summary>
    internal static class RuntimeMigrationStartup
    {
        private static readonly object Gate = new object();
        private static RuntimeMigrationManifest status;
        private static bool attempted;

        internal static RuntimeMigrationManifest Ensure()
        {
            lock (Gate)
            {
                if (attempted) return status ?? new RuntimeMigrationManifest { status = "recovery_required" };
                attempted = true;

                string streamingRoot = Application.streamingAssetsPath;
                string persistentRoot = Application.persistentDataPath;
                string dataRoot = Path.Combine(persistentRoot, "Data");
                ConversationSessionStore sessions = UnityEngine.Object.FindAnyObjectByType<ConversationSessionStore>();
                GeneratedDialogueArchive archive = UnityEngine.Object.FindAnyObjectByType<GeneratedDialogueArchive>();
                string conversationPath = sessions != null ? sessions.MigrationDatabasePath : Path.Combine(dataRoot, "conversations.sqlite3");
                string gamePath = Path.Combine(dataRoot, "alteros-game.sqlite3");
                string legacyGamePath = Path.Combine(persistentRoot, "alteros-game.sqlite3");
                string personalPath = archive != null ? archive.MigrationDatabasePath : Path.Combine(dataRoot, "personal-content.sqlite3");
                string contentPath = Path.Combine(streamingRoot, "Content", "content.sqlite3");
                string transcriptPath = Path.Combine(persistentRoot, "alteros-conversation-v2.jsonl");
                string topicPath = Path.Combine(persistentRoot, "alteros-topic-history-v2.json");
                string sessionsPath = sessions != null ? sessions.MigrationLegacyPath : Path.Combine(persistentRoot, "alteros_conversation_sessions.json");
                string archivePath = archive != null ? archive.MigrationManifestPath : Path.Combine(streamingRoot, "Dialogue", "Generated", "generated_dialogue_manifest.json");
                var configuration = new RuntimeMigrationConfiguration
                {
                    journalPath = Path.Combine(dataRoot, "runtime-migrations.sqlite3")
                };

                configuration.domains.Add(new RuntimeMigrationDomainRequest
                {
                    domain = "content", targetPath = contentPath, targetIdentity = "content:shipped", schemaVersion = 1,
                    validateOnResume = true,
                    prepare = () =>
                    {
                        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(streamingRoot);
                        return new RuntimeMigrationDomainResult
                        {
                            state = opened.IsReady ? "ready" : "failed",
                            sourceHash = opened.IsReady ? opened.value.BuildManifest.Hash : string.Empty,
                            targetIdentity = "content:shipped", schemaVersion = 1,
                            targetRevision = opened.IsReady ? 1 : 0,
                            receiptId = opened.IsReady ? opened.value.Release.releaseId : string.Empty,
                            error = opened.IsReady ? string.Empty : opened.status
                        };
                    }
                });
                configuration.domains.Add(new RuntimeMigrationDomainRequest
                {
                    domain = "topic_history", targetPath = gamePath, targetIdentity = "game:primary", schemaVersion = 3,
                    prepare = () =>
                    {
                        var store = new SqliteTopicHistoryStore(gamePath);
                        TopicHistoryImportResult imported = TopicHistoryDatabaseImporter.ImportOnce(legacyGamePath, store);
                        if (imported.State == "missing") imported = TopicHistoryImporter.ImportOnce(topicPath, store);
                        return new RuntimeMigrationDomainResult
                        {
                            state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "game:primary",
                            schemaVersion = 3, targetRevision = imported.Revision, receiptId = imported.OperationId, error = imported.Error
                        };
                    }
                });
                configuration.domains.Add(new RuntimeMigrationDomainRequest
                {
                    domain = "sessions", targetPath = conversationPath, targetIdentity = "conversations:primary", schemaVersion = 2,
                    prepare = () =>
                    {
                        ConversationSessionImportResult imported = ConversationSessionImporter.ImportOnce(sessionsPath, new SqliteConversationStore(conversationPath));
                        return new RuntimeMigrationDomainResult
                        {
                            state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "conversations:primary",
                            schemaVersion = 2, targetRevision = imported.Revision, receiptId = imported.OperationId, error = imported.Error
                        };
                    }
                });
                configuration.domains.Add(new RuntimeMigrationDomainRequest
                {
                    domain = "transcript", targetPath = conversationPath, targetIdentity = "conversations:primary", schemaVersion = 2,
                    prepare = () =>
                    {
                        ConversationTranscriptImportResult imported = TranscriptJsonlImporter.ImportOnce(transcriptPath, new SqliteTranscriptStore(conversationPath));
                        return new RuntimeMigrationDomainResult
                        {
                            state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "conversations:primary",
                            schemaVersion = 2, targetRevision = imported.Revision, receiptId = imported.OperationId, error = imported.Error
                        };
                    }
                });
                configuration.domains.Add(new RuntimeMigrationDomainRequest
                {
                    domain = "generated_archive", targetPath = personalPath, targetIdentity = "personal-content:primary", schemaVersion = 1,
                    prepare = () =>
                    {
                        GeneratedArchiveImportResult imported = GeneratedArchiveImporter.ImportOnce(archivePath, streamingRoot,
                            new SqliteGeneratedArchiveStore(personalPath));
                        return new RuntimeMigrationDomainResult
                        {
                            state = imported.State, sourceHash = imported.SourceHash, targetIdentity = "personal-content:primary",
                            schemaVersion = 1, receiptId = imported.OperationId, error = imported.Error
                        };
                    }
                });
                configuration.domains.Add(new RuntimeMigrationDomainRequest
                {
                    domain = "identity", targetPath = conversationPath, targetIdentity = "conversations:primary", schemaVersion = 2,
                    prepare = () =>
                    {
                        var store = new SqliteConversationStore(conversationPath);
                        string sessionId = PlayerPrefs.GetString("AlterOS.Conversation.SessionId", string.Empty);
                        string playerId = PlayerPrefs.GetString("AlterOS.PlayerMemory.PlayerId", string.Empty);
                        if (!EnsureIdentity(store, "AlterOS.Conversation.SessionId", sessionId) ||
                            !EnsureIdentity(store, "AlterOS.PlayerMemory.PlayerId", playerId))
                            return new RuntimeMigrationDomainResult { state = "recovery_required", targetIdentity = "conversations:primary", schemaVersion = 2, error = "A saved player identity conflicts with private SQL history." };
                        return new RuntimeMigrationDomainResult
                        {
                            state = string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(playerId) ? "ready_empty" : "imported",
                            targetIdentity = "conversations:primary", schemaVersion = 2,
                            receiptId = string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(playerId) ? "identity:empty" : "identity:aliases"
                        };
                    }
                });

                status = RuntimeMigrationCoordinator.Prepare(configuration);
                return status;
            }
        }

        private static bool EnsureIdentity(SqliteConversationStore store, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            ConversationIdentityResult result = store.EnsureIdentity(key, value);
            return result.State == "imported" || result.State == "existing";
        }
    }
}
