using System;
using System.Collections.Generic;
using SQLite;

namespace AlterOS.Authoring
{
    internal static class AuthoringSchema
    {
        internal const int CurrentVersion = 3;

        private static readonly string[] RequiredTables =
        {
            "metadata", "documents", "document_revisions", "operation_receipts",
            "import_receipts", "import_sources", "build_sets", "build_set_documents"
        };

        internal static void CreateCurrent(SQLiteConnection connection, string storeId)
        {
            connection.Execute("CREATE TABLE metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
            CreateDocumentsTable(connection);
            connection.Execute("CREATE TABLE document_revisions (document_id TEXT NOT NULL, revision_hash TEXT NOT NULL CHECK(length(revision_hash)=64), document_bytes BLOB NOT NULL CHECK(length(document_bytes) BETWEEN 1 AND 4194304), created_utc TEXT NOT NULL, PRIMARY KEY(document_id,revision_hash), FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE operation_receipts (operation_id TEXT PRIMARY KEY NOT NULL CHECK(length(operation_id) BETWEEN 1 AND 128), operation_kind TEXT NOT NULL CHECK(operation_kind IN ('document.save','build_set.save')), request_hash TEXT NOT NULL CHECK(length(request_hash)=64), result_status TEXT NOT NULL CHECK(result_status IN ('ready','invalid','conflict')), document_id TEXT NOT NULL DEFAULT '', revision_hash TEXT NOT NULL DEFAULT '', build_set_name TEXT NOT NULL DEFAULT '', code TEXT NOT NULL DEFAULT '', message TEXT NOT NULL DEFAULT '', result_payload TEXT NOT NULL DEFAULT '', store_revision INTEGER NOT NULL CHECK(store_revision>=0), created_utc TEXT NOT NULL)");
            connection.Execute("CREATE TABLE import_receipts (operation_id TEXT PRIMARY KEY NOT NULL CHECK(length(operation_id) BETWEEN 1 AND 128), request_hash TEXT NOT NULL CHECK(length(request_hash)=64), result_status TEXT NOT NULL CHECK(result_status IN ('ready','invalid','conflict')), source_count INTEGER NOT NULL CHECK(source_count>=0), store_revision INTEGER NOT NULL CHECK(store_revision>=0), code TEXT NOT NULL DEFAULT '', message TEXT NOT NULL DEFAULT '', created_utc TEXT NOT NULL)");
            connection.Execute("CREATE TABLE import_sources (operation_id TEXT NOT NULL, source_path TEXT NOT NULL CHECK(length(source_path) BETWEEN 1 AND 1024), source_hash TEXT NOT NULL CHECK(length(source_hash)=64), document_id TEXT NOT NULL, source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), original_bytes BLOB NOT NULL CHECK(length(original_bytes) BETWEEN 1 AND 4194304), PRIMARY KEY(operation_id,source_path), FOREIGN KEY(operation_id) REFERENCES import_receipts(operation_id) ON DELETE RESTRICT, FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE INDEX import_sources_document ON import_sources(document_id,operation_id)");
            connection.Execute("CREATE TABLE build_sets (name TEXT PRIMARY KEY NOT NULL CHECK(length(name) BETWEEN 1 AND 64), revision_hash TEXT NOT NULL CHECK(length(revision_hash)=64), updated_utc TEXT NOT NULL)");
            connection.Execute("CREATE TABLE build_set_documents (name TEXT NOT NULL, ordinal INTEGER NOT NULL CHECK(ordinal>=0), document_id TEXT NOT NULL, PRIMARY KEY(name,ordinal), UNIQUE(name,document_id), FOREIGN KEY(name) REFERENCES build_sets(name) ON DELETE RESTRICT, FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE RESTRICT)");

            SetMetadata(connection, "schema_version", CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetMetadata(connection, "store_id", storeId);
            SetMetadata(connection, "store_revision", "0");
            connection.Execute("PRAGMA user_version = " + CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        internal static void MigrateVersion1To2(SQLiteConnection connection)
        {
            int foreignKeysEnabled = connection.ExecuteScalar<int>("PRAGMA foreign_keys");
            if (foreignKeysEnabled != 1)
                throw new InvalidOperationException("Authoring migration requires foreign-key enforcement before the rebuild.");

            connection.Execute("PRAGMA foreign_keys=OFF");
            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 0)
                throw new InvalidOperationException("SQLite could not suspend foreign-key enforcement for the authoring table rebuild.");

            connection.BeginTransaction();
            try
            {
                int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                string schemaVersion = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key='schema_version'");
                if (version == 2 && schemaVersion == "2" &&
                    HasRequiredTables(connection))
                {
                    connection.Commit();
                    return;
                }
                if (version != 1 || schemaVersion != "1" || !HasRequiredTables(connection))
                    throw new InvalidOperationException("The authoring database is not a complete schema-v1 store.");

                connection.Execute("CREATE TEMP TABLE authoring_documents_v1 AS SELECT document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc FROM documents");
                connection.Execute("DROP TABLE documents");
                CreateDocumentsTableVersion2(connection);
                connection.Execute("INSERT INTO documents(document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc) " +
                    "SELECT document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc FROM authoring_documents_v1");
                connection.Execute("DROP TABLE authoring_documents_v1");

                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
                    throw new InvalidOperationException("The authoring migration would leave foreign-key violations.");

                SetMetadata(connection, "schema_version", "2");
                connection.Execute("PRAGMA user_version=2");
                connection.Commit();
            }
            catch
            {
                try { connection.Rollback(); } catch { }
                throw;
            }
            finally
            {
                connection.Execute("PRAGMA foreign_keys=ON");
            }

            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1 ||
                connection.ExecuteScalar<int>("PRAGMA user_version") != 2 || !HasRequiredTables(connection))
                throw new InvalidOperationException("The authoring schema-v2 migration did not leave a complete store.");
        }

        internal static void MigrateVersion2To3(SQLiteConnection connection)
        {
            int foreignKeysEnabled = connection.ExecuteScalar<int>("PRAGMA foreign_keys");
            if (foreignKeysEnabled != 1)
                throw new InvalidOperationException("Authoring migration requires foreign-key enforcement before the rebuild.");

            connection.Execute("PRAGMA foreign_keys=OFF");
            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 0)
                throw new InvalidOperationException("SQLite could not suspend foreign-key enforcement for the authoring table rebuild.");

            connection.BeginTransaction();
            try
            {
                int version = connection.ExecuteScalar<int>("PRAGMA user_version");
                string schemaVersion = connection.ExecuteScalar<string>("SELECT value FROM metadata WHERE key='schema_version'");
                if (version == CurrentVersion && schemaVersion == CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    HasRequiredTables(connection))
                {
                    connection.Commit();
                    return;
                }
                if (version != 2 || schemaVersion != "2" || !HasRequiredTables(connection))
                    throw new InvalidOperationException("The authoring database is not a complete schema-v2 store.");

                connection.Execute("CREATE TEMP TABLE authoring_documents_v2 AS SELECT document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc FROM documents");
                connection.Execute("DROP TABLE documents");
                CreateDocumentsTable(connection);
                connection.Execute("INSERT INTO documents(document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc) " +
                    "SELECT document_id,kind,source_id,logical_path,logical_path_key,head_hash,created_utc,updated_utc FROM authoring_documents_v2");
                connection.Execute("DROP TABLE authoring_documents_v2");

                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") != 0)
                    throw new InvalidOperationException("The authoring migration would leave foreign-key violations.");

                SetMetadata(connection, "schema_version", CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                connection.Execute("PRAGMA user_version=" + CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                connection.Commit();
            }
            catch
            {
                try { connection.Rollback(); } catch { }
                throw;
            }
            finally
            {
                connection.Execute("PRAGMA foreign_keys=ON");
            }

            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1 ||
                connection.ExecuteScalar<int>("PRAGMA user_version") != CurrentVersion || !HasRequiredTables(connection))
                throw new InvalidOperationException("The authoring schema-v3 migration did not leave a complete store.");
        }

        internal static bool HasRequiredTables(SQLiteConnection connection)
        {
            foreach (string table in RequiredTables)
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1)
                    return false;

            int userTables = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
            return userTables == RequiredTables.Length;
        }

        internal static void SetMetadata(SQLiteConnection connection, string key, string value)
        {
            connection.Execute("INSERT OR REPLACE INTO metadata(key,value) VALUES(?,?)", key, value);
        }

        private static void CreateDocumentsTable(SQLiteConnection connection)
        {
            connection.Execute("CREATE TABLE documents (document_id TEXT PRIMARY KEY NOT NULL CHECK(length(document_id) BETWEEN 1 AND 128), kind TEXT NOT NULL CHECK(kind IN ('generic_dialogue','topic_definition','scenario_definition','legacy_sequence','ambient_pool','activity_scenario_library','activity_greeting_menu','conversation_coverage','topic_migration_map','onboarding_steps','topic','scenario','dialogue_definition','furniture_definition','furniture_catalogue_settings','item_definition','action_definition')), source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), logical_path TEXT NOT NULL CHECK(length(logical_path) BETWEEN 1 AND 1024), logical_path_key TEXT NOT NULL UNIQUE CHECK(length(logical_path_key) BETWEEN 1 AND 2048), head_hash TEXT NOT NULL CHECK(length(head_hash)=64), created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL)");
            connection.Execute("CREATE INDEX documents_kind_id ON documents(kind,document_id)");
        }

        private static void CreateDocumentsTableVersion2(SQLiteConnection connection)
        {
            connection.Execute("CREATE TABLE documents (document_id TEXT PRIMARY KEY NOT NULL CHECK(length(document_id) BETWEEN 1 AND 128), kind TEXT NOT NULL CHECK(kind IN ('generic_dialogue','topic_definition','scenario_definition','legacy_sequence','ambient_pool','activity_scenario_library','activity_greeting_menu','conversation_coverage','topic_migration_map','onboarding_steps','topic','scenario','dialogue_definition','furniture_definition','item_definition','action_definition')), source_id TEXT NOT NULL DEFAULT '' CHECK(length(source_id)<=128), logical_path TEXT NOT NULL CHECK(length(logical_path) BETWEEN 1 AND 1024), logical_path_key TEXT NOT NULL UNIQUE CHECK(length(logical_path_key) BETWEEN 1 AND 2048), head_hash TEXT NOT NULL CHECK(length(head_hash)=64), created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL)");
            connection.Execute("CREATE INDEX documents_kind_id ON documents(kind,document_id)");
        }
    }
}
