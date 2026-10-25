using System.Collections.Generic;
using SQLite;

namespace AlterOS.Content
{
    internal static class ContentSchema
    {
        // Version 2 changes legacy_sources identity to include source_hash so a
        // changed file at the same relative path can remain alongside old closures.
        internal const int CurrentVersion = 6;

        private static readonly string[] RequiredVersion4Tables =
        {
            "metadata", "topics", "topic_entries", "flows", "steps", "transitions", "choices",
            "dialogues", "beats", "texts", "translations", "actions", "rules", "flow_parameters",
            "flow_variables", "step_bindings", "flow_exits", "exit_mappings", "anchors", "anchor_links",
            "embedding_profiles", "embeddings", "releases", "release_dependencies", "release_heads", "jobs", "legacy_sources"
        };

        private static readonly string[] RequiredTables =
        {
            "metadata", "topics", "topic_entries", "flows", "steps", "transitions", "choices",
            "dialogues", "beats", "texts", "translations", "actions", "rules", "flow_parameters",
            "flow_variables", "step_bindings", "flow_exits", "exit_mappings", "anchors", "anchor_links",
            "embedding_profiles", "embeddings", "releases", "release_dependencies", "release_heads", "jobs", "legacy_sources",
            "items", "furniture_definitions", "furniture_function_bindings", "action_sequence_steps"
        };

        internal static void CreateCurrent(SQLiteConnection connection, string storeId)
        {
            connection.Execute("CREATE TABLE metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
            connection.Execute("CREATE TABLE texts (text_id TEXT NOT NULL CHECK(length(text_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(text_id,revision))");
            connection.Execute("CREATE TABLE translations (text_id TEXT NOT NULL, text_revision TEXT NOT NULL, language TEXT NOT NULL CHECK(length(language) BETWEEN 2 AND 16), text_value TEXT NOT NULL CHECK(length(text_value)<=8192), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(text_id,text_revision,language), FOREIGN KEY(text_id,text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE dialogues (dialogue_id TEXT NOT NULL CHECK(length(dialogue_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(dialogue_id,revision))");
            connection.Execute("CREATE TABLE beats (dialogue_id TEXT NOT NULL, dialogue_revision TEXT NOT NULL, beat_id TEXT NOT NULL CHECK(length(beat_id)<=128), sequence_order INTEGER NOT NULL CHECK(sequence_order>=0), text_id TEXT NOT NULL, text_revision TEXT NOT NULL, expression TEXT NOT NULL DEFAULT '' CHECK(length(expression)<=64), presentation_json TEXT NOT NULL DEFAULT '{}', row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(dialogue_id,dialogue_revision,beat_id), UNIQUE(dialogue_id,dialogue_revision,sequence_order), FOREIGN KEY(dialogue_id,dialogue_revision) REFERENCES dialogues(dialogue_id,revision) ON DELETE RESTRICT, FOREIGN KEY(text_id,text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE actions (action_id TEXT NOT NULL CHECK(length(action_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), capability_id TEXT NOT NULL CHECK(length(capability_id)<=128), contract_json TEXT NOT NULL CHECK(length(contract_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), implementation_kind TEXT NOT NULL DEFAULT 'legacy_activity' CHECK(implementation_kind IN ('legacy_activity','activity_sequence','host_effect')), composer_id TEXT NULL CHECK(composer_id IS NULL OR length(composer_id)<=128), function_id TEXT NULL CHECK(function_id IS NULL OR length(function_id)<=128), arguments_json TEXT NULL CHECK(arguments_json IS NULL OR length(arguments_json)<=16384), PRIMARY KEY(action_id,revision))");
            CreateTypedReusableContentTables(connection, CurrentVersion);
            connection.Execute("CREATE TABLE rules (rule_id TEXT NOT NULL CHECK(length(rule_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), expression_json TEXT NOT NULL CHECK(length(expression_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(rule_id,revision))");
            connection.Execute("CREATE TABLE flows (flow_id TEXT NOT NULL CHECK(length(flow_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), kind TEXT NOT NULL CHECK(kind IN ('scenario','subflow')), entry_step_id TEXT NOT NULL CHECK(length(entry_step_id)<=128), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,revision), FOREIGN KEY(flow_id,revision,entry_step_id) REFERENCES steps(flow_id,flow_revision,step_id) DEFERRABLE INITIALLY DEFERRED)");
            connection.Execute("CREATE TABLE steps (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, step_id TEXT NOT NULL CHECK(length(step_id)<=128), kind TEXT NOT NULL CHECK(kind IN ('dialogue','action','host_effect','subflow','ask','wait','branch','end')), dialogue_id TEXT, dialogue_revision TEXT, action_id TEXT, action_revision TEXT, called_flow_id TEXT, called_flow_revision TEXT, data_json TEXT NOT NULL DEFAULT '{}' CHECK(length(data_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,step_id), FOREIGN KEY(flow_id,flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED, FOREIGN KEY(dialogue_id,dialogue_revision) REFERENCES dialogues(dialogue_id,revision) ON DELETE RESTRICT, FOREIGN KEY(action_id,action_revision) REFERENCES actions(action_id,revision) ON DELETE RESTRICT, FOREIGN KEY(called_flow_id,called_flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT, CHECK((kind IN ('dialogue','ask') AND dialogue_id IS NOT NULL AND dialogue_revision IS NOT NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL) OR (kind='ask' AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL) OR (kind IN ('action','host_effect') AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NOT NULL AND action_revision IS NOT NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL) OR (kind='subflow' AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NOT NULL AND called_flow_revision IS NOT NULL) OR (kind IN ('wait','branch','end') AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL)))");
            connection.Execute("CREATE INDEX steps_flow_order ON steps(flow_id,flow_revision,step_id)");
            connection.Execute("CREATE TABLE transitions (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, transition_id TEXT NOT NULL CHECK(length(transition_id)<=128), from_step_id TEXT NOT NULL, to_step_id TEXT NOT NULL, purpose TEXT NOT NULL CHECK(length(purpose)<=32), rule_id TEXT, rule_revision TEXT, row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,transition_id), FOREIGN KEY(flow_id,flow_revision,from_step_id) REFERENCES steps(flow_id,flow_revision,step_id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED, FOREIGN KEY(flow_id,flow_revision,to_step_id) REFERENCES steps(flow_id,flow_revision,step_id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED, FOREIGN KEY(rule_id,rule_revision) REFERENCES rules(rule_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE INDEX transitions_from_step ON transitions(flow_id,flow_revision,from_step_id)");
            connection.Execute("CREATE TABLE choices (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, step_id TEXT NOT NULL, choice_id TEXT NOT NULL CHECK(length(choice_id)<=128), transition_id TEXT NOT NULL, value_json TEXT NOT NULL DEFAULT '{}' CHECK(length(value_json)<=4096), label_text_id TEXT NOT NULL, label_text_revision TEXT NOT NULL, availability_rule_id TEXT, availability_rule_revision TEXT, hint_text_id TEXT, hint_text_revision TEXT, row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,step_id,choice_id), FOREIGN KEY(flow_id,flow_revision,step_id) REFERENCES steps(flow_id,flow_revision,step_id) ON DELETE RESTRICT, FOREIGN KEY(flow_id,flow_revision,transition_id) REFERENCES transitions(flow_id,flow_revision,transition_id) ON DELETE RESTRICT, FOREIGN KEY(label_text_id,label_text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT, FOREIGN KEY(availability_rule_id,availability_rule_revision) REFERENCES rules(rule_id,revision) ON DELETE RESTRICT, FOREIGN KEY(hint_text_id,hint_text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE flow_parameters (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, name TEXT NOT NULL CHECK(length(name)<=128), direction TEXT NOT NULL CHECK(direction IN ('input','output')), value_type TEXT NOT NULL CHECK(value_type IN ('bool','int64','double','text','enum','entity')), required INTEGER NOT NULL CHECK(required IN (0,1)), default_value_json TEXT, row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,direction,name), FOREIGN KEY(flow_id,flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE flow_variables (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, name TEXT NOT NULL CHECK(length(name)<=128), value_type TEXT NOT NULL CHECK(value_type IN ('bool','int64','double','text','enum','entity')), mutable INTEGER NOT NULL CHECK(mutable IN (0,1)), default_value_json TEXT, row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,name), FOREIGN KEY(flow_id,flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE step_bindings (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, step_id TEXT NOT NULL, destination_name TEXT NOT NULL CHECK(length(destination_name)<=128), source_kind TEXT NOT NULL CHECK(source_kind IN ('literal','local','input','host_fact','result')), literal_json TEXT, source_name TEXT, host_fact_id TEXT, result_step_id TEXT, result_name TEXT, row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,step_id,destination_name), FOREIGN KEY(flow_id,flow_revision,step_id) REFERENCES steps(flow_id,flow_revision,step_id) ON DELETE RESTRICT, CHECK((source_kind='literal' AND literal_json IS NOT NULL AND source_name IS NULL AND host_fact_id IS NULL AND result_step_id IS NULL AND result_name IS NULL) OR (source_kind IN ('local','input') AND literal_json IS NULL AND source_name IS NOT NULL AND host_fact_id IS NULL AND result_step_id IS NULL AND result_name IS NULL) OR (source_kind='host_fact' AND literal_json IS NULL AND source_name IS NULL AND host_fact_id IS NOT NULL AND result_step_id IS NULL AND result_name IS NULL) OR (source_kind='result' AND literal_json IS NULL AND source_name IS NULL AND host_fact_id IS NULL AND result_step_id IS NOT NULL AND result_name IS NOT NULL)))");
            connection.Execute("CREATE TABLE flow_exits (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, exit_name TEXT NOT NULL CHECK(length(exit_name)<=128), contract_json TEXT NOT NULL DEFAULT '{}' CHECK(length(contract_json)<=8192), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,exit_name), FOREIGN KEY(flow_id,flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE exit_mappings (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, call_step_id TEXT NOT NULL, callee_flow_id TEXT NOT NULL, callee_revision TEXT NOT NULL, exit_name TEXT NOT NULL, transition_id TEXT NOT NULL, output_mappings_json TEXT NOT NULL DEFAULT '[]' CHECK(length(output_mappings_json)<=8192), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,call_step_id,exit_name), FOREIGN KEY(flow_id,flow_revision,call_step_id) REFERENCES steps(flow_id,flow_revision,step_id) ON DELETE RESTRICT, FOREIGN KEY(callee_flow_id,callee_revision,exit_name) REFERENCES flow_exits(flow_id,flow_revision,exit_name) ON DELETE RESTRICT, FOREIGN KEY(flow_id,flow_revision,transition_id) REFERENCES transitions(flow_id,flow_revision,transition_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE topics (topic_id TEXT NOT NULL CHECK(length(topic_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), route_id TEXT NOT NULL CHECK(length(route_id)<=128), title_text_id TEXT NOT NULL, title_text_revision TEXT NOT NULL, prompt_text_id TEXT NOT NULL, prompt_text_revision TEXT NOT NULL, replay_policy TEXT NOT NULL CHECK(replay_policy IN ('repeatable','once')), cooldown_seconds REAL NOT NULL CHECK(cooldown_seconds>=0 AND cooldown_seconds<=31536000), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(topic_id,revision), FOREIGN KEY(title_text_id,title_text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT, FOREIGN KEY(prompt_text_id,prompt_text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE INDEX topics_route ON topics(route_id,topic_id,revision)");
            connection.Execute("CREATE TABLE topic_entries (topic_id TEXT NOT NULL, topic_revision TEXT NOT NULL, entry_id TEXT NOT NULL CHECK(length(entry_id)<=128), flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, priority INTEGER NOT NULL, base_weight REAL NOT NULL CHECK(base_weight>=0 AND base_weight<=1000000), condition_json TEXT NOT NULL DEFAULT '{}' CHECK(length(condition_json)<=8192), modifiers_json TEXT NOT NULL DEFAULT '[]' CHECK(length(modifiers_json)<=8192), channels_json TEXT NOT NULL DEFAULT '[]' CHECK(length(channels_json)<=256), consumption_node_id TEXT NOT NULL DEFAULT '' CHECK(length(consumption_node_id)<=128), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(topic_id,topic_revision,entry_id), FOREIGN KEY(topic_id,topic_revision) REFERENCES topics(topic_id,revision) ON DELETE RESTRICT, FOREIGN KEY(flow_id,flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE anchors (anchor_id TEXT PRIMARY KEY NOT NULL CHECK(length(anchor_id)=64), language TEXT NOT NULL CHECK(length(language) BETWEEN 2 AND 16), text_value TEXT NOT NULL CHECK(length(text_value)<=2048), text_hash TEXT NOT NULL CHECK(length(text_hash)=64))");
            connection.Execute("CREATE TABLE anchor_links (link_id TEXT PRIMARY KEY NOT NULL CHECK(length(link_id)<=128), anchor_id TEXT NOT NULL, owner_kind TEXT NOT NULL CHECK(owner_kind IN ('topic','flow','dialogue','canonical')), owner_topic_id TEXT, owner_topic_revision TEXT, owner_flow_id TEXT, owner_flow_revision TEXT, owner_dialogue_id TEXT, owner_dialogue_revision TEXT, canonical_owner_id TEXT, canonical_revision TEXT, role TEXT NOT NULL CHECK(length(role)<=32), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), FOREIGN KEY(anchor_id) REFERENCES anchors(anchor_id) ON DELETE RESTRICT, FOREIGN KEY(owner_topic_id,owner_topic_revision) REFERENCES topics(topic_id,revision) ON DELETE RESTRICT, FOREIGN KEY(owner_flow_id,owner_flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT, FOREIGN KEY(owner_dialogue_id,owner_dialogue_revision) REFERENCES dialogues(dialogue_id,revision) ON DELETE RESTRICT, CHECK((owner_kind='topic' AND owner_topic_id IS NOT NULL AND owner_topic_revision IS NOT NULL AND owner_flow_id IS NULL AND owner_flow_revision IS NULL AND owner_dialogue_id IS NULL AND owner_dialogue_revision IS NULL AND canonical_owner_id IS NULL AND canonical_revision IS NULL) OR (owner_kind='flow' AND owner_topic_id IS NULL AND owner_topic_revision IS NULL AND owner_flow_id IS NOT NULL AND owner_flow_revision IS NOT NULL AND owner_dialogue_id IS NULL AND owner_dialogue_revision IS NULL AND canonical_owner_id IS NULL AND canonical_revision IS NULL) OR (owner_kind='dialogue' AND owner_topic_id IS NULL AND owner_topic_revision IS NULL AND owner_flow_id IS NULL AND owner_flow_revision IS NULL AND owner_dialogue_id IS NOT NULL AND owner_dialogue_revision IS NOT NULL AND canonical_owner_id IS NULL AND canonical_revision IS NULL) OR (owner_kind='canonical' AND owner_topic_id IS NULL AND owner_topic_revision IS NULL AND owner_flow_id IS NULL AND owner_flow_revision IS NULL AND owner_dialogue_id IS NULL AND owner_dialogue_revision IS NULL AND canonical_owner_id IS NOT NULL AND canonical_revision IS NOT NULL)))");
            connection.Execute("CREATE TABLE embedding_profiles (profile_id TEXT PRIMARY KEY NOT NULL CHECK(length(profile_id)<=128), model_id TEXT NOT NULL CHECK(length(model_id)<=128), model_revision TEXT NOT NULL CHECK(length(model_revision)<=128), tokenizer_hash TEXT NOT NULL CHECK(length(tokenizer_hash)=64), pipeline_hash TEXT NOT NULL CHECK(length(pipeline_hash)=64), dimensions INTEGER NOT NULL CHECK(dimensions BETWEEN 1 AND 8192), dtype TEXT NOT NULL CHECK(dtype='float32-le'), normalization TEXT NOT NULL CHECK(normalization IN ('none','l2')), metric TEXT NOT NULL CHECK(metric IN ('cosine','dot','l2')), profile_hash TEXT NOT NULL CHECK(length(profile_hash)=64))");
            connection.Execute("CREATE TABLE embeddings (anchor_id TEXT NOT NULL, profile_id TEXT NOT NULL, vector BLOB NOT NULL, vector_hash TEXT NOT NULL CHECK(length(vector_hash)=64), vector_bytes INTEGER NOT NULL CHECK(vector_bytes>=4), PRIMARY KEY(anchor_id,profile_id), FOREIGN KEY(anchor_id) REFERENCES anchors(anchor_id) ON DELETE RESTRICT, FOREIGN KEY(profile_id) REFERENCES embedding_profiles(profile_id) ON DELETE RESTRICT, CHECK(length(vector)=vector_bytes))");
            connection.Execute("CREATE TABLE releases (release_id TEXT PRIMARY KEY NOT NULL CHECK(length(release_id)<=128), manifest_json TEXT NOT NULL CHECK(length(manifest_json)<=262144), manifest_hash TEXT NOT NULL CHECK(length(manifest_hash)=64), validator_version TEXT NOT NULL CHECK(length(validator_version)<=64), graph_fingerprint TEXT NOT NULL CHECK(length(graph_fingerprint)=64), index_fingerprint TEXT NOT NULL CHECK(length(index_fingerprint)<=128), row_hash TEXT NOT NULL CHECK(length(row_hash)=64))");
            connection.Execute("CREATE TABLE release_dependencies (release_id TEXT NOT NULL, kind TEXT NOT NULL CHECK(length(kind)<=32), dependency_id TEXT NOT NULL CHECK(length(dependency_id)<=256), revision TEXT NOT NULL CHECK(length(revision)=64), store_id TEXT NOT NULL CHECK(length(store_id)<=128), expected_hash TEXT NOT NULL CHECK(length(expected_hash)=64), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(release_id,kind,dependency_id,revision,store_id), FOREIGN KEY(release_id) REFERENCES releases(release_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE INDEX release_dependencies_lookup ON release_dependencies(release_id,kind,dependency_id)");
            connection.Execute("CREATE TABLE release_heads (head_name TEXT PRIMARY KEY NOT NULL CHECK(length(head_name)<=64), release_id TEXT NOT NULL, head_revision INTEGER NOT NULL CHECK(head_revision>=0), FOREIGN KEY(release_id) REFERENCES releases(release_id) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE jobs (job_id TEXT PRIMARY KEY NOT NULL CHECK(length(job_id)<=128), idempotency_key TEXT NOT NULL UNIQUE CHECK(length(idempotency_key)<=128), kind TEXT NOT NULL CHECK(length(kind)<=32), source_hash TEXT NOT NULL CHECK(length(source_hash)=64), state TEXT NOT NULL CHECK(state IN ('staged','validated','failed','cancelled')), error_code TEXT NOT NULL DEFAULT '' CHECK(length(error_code)<=64), attempts INTEGER NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 32))");
            connection.Execute("CREATE TABLE legacy_sources (source_kind TEXT NOT NULL CHECK(length(source_kind)<=32), source_path TEXT NOT NULL CHECK(length(source_path)<=512), source_id TEXT NOT NULL CHECK(length(source_id)<=128), source_hash TEXT NOT NULL CHECK(length(source_hash)=64), payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64), content_id TEXT NOT NULL CHECK(length(content_id)<=128), content_revision TEXT NOT NULL CHECK(length(content_revision)=64), metadata_json TEXT NOT NULL CHECK(length(metadata_json)<=16384), source_bytes BLOB NOT NULL CHECK(length(source_bytes)<=4194304), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(source_kind,source_path,source_hash))");
            connection.Execute("CREATE INDEX legacy_sources_identity ON legacy_sources(source_kind,source_id)");

            SetMetadata(connection, "schema_version", CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetMetadata(connection, "store_id", storeId);
            connection.Execute("PRAGMA user_version=" + CurrentVersion);
        }

        internal static bool HasRequiredTables(SQLiteConnection connection)
        {
            foreach (string table in RequiredTables)
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1)
                    return false;
            return HasColumns(connection, "actions", "implementation_kind", "composer_id", "function_id", "arguments_json") &&
                HasColumns(connection, "furniture_function_bindings", "point_id", "use_id");
        }

        internal static bool HasVersion4Tables(SQLiteConnection connection)
        {
            foreach (string table in RequiredVersion4Tables)
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1)
                    return false;
            return HasColumns(connection, "actions", "action_id", "revision", "semantic_hash", "capability_id", "contract_json", "row_hash");
        }

        internal static bool HasVersion5Tables(SQLiteConnection connection)
        {
            foreach (string table in RequiredTables)
                if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) != 1)
                    return false;
            return HasColumns(connection, "actions", "implementation_kind", "composer_id", "function_id", "arguments_json") &&
                HasColumns(connection, "furniture_function_bindings", "function_id", "function_contract_version", "contract_json");
        }

        internal static void MigrateVersion4To5(SQLiteConnection connection)
        {
            if (connection.ExecuteScalar<int>("PRAGMA user_version") != 4 || !HasVersion4Tables(connection))
                throw new System.InvalidOperationException("The content database is not a complete schema-4 store.");
            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
                throw new System.InvalidOperationException("SQLite foreign-key enforcement must be enabled before the content migration.");

            connection.BeginTransaction();
            try
            {
                connection.Execute("ALTER TABLE actions ADD COLUMN implementation_kind TEXT NOT NULL DEFAULT 'legacy_activity' CHECK(implementation_kind IN ('legacy_activity','activity_sequence','host_effect'))");
                connection.Execute("ALTER TABLE actions ADD COLUMN composer_id TEXT NULL CHECK(composer_id IS NULL OR length(composer_id)<=128)");
                connection.Execute("ALTER TABLE actions ADD COLUMN function_id TEXT NULL CHECK(function_id IS NULL OR length(function_id)<=128)");
                connection.Execute("ALTER TABLE actions ADD COLUMN arguments_json TEXT NULL CHECK(arguments_json IS NULL OR length(arguments_json)<=16384)");
                CreateTypedReusableContentTables(connection, 5);
                connection.Execute("UPDATE metadata SET value=? WHERE key='schema_version'", "5");
                connection.Execute("PRAGMA user_version=5");
                if (connection.Query<ForeignKeyViolation>("PRAGMA foreign_key_check").Count != 0)
                    throw new System.InvalidOperationException("The content migration introduced a foreign-key violation.");
                connection.Commit();
            }
            catch
            {
                if (connection.IsInTransaction) connection.Rollback();
                throw;
            }
        }

        private static void CreateTypedReusableContentTables(SQLiteConnection connection, int schemaVersion)
        {
            connection.Execute("CREATE TABLE items (item_id TEXT NOT NULL CHECK(length(item_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), display_text_id TEXT NOT NULL, display_text_revision TEXT NOT NULL CHECK(length(display_text_revision)=64), stack_limit INTEGER NOT NULL CHECK(stack_limit>0), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(item_id,revision), FOREIGN KEY(display_text_id,display_text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE furniture_definitions (furniture_id TEXT NOT NULL CHECK(length(furniture_id)<=128), revision TEXT NOT NULL CHECK(length(revision)=64), semantic_hash TEXT NOT NULL CHECK(length(semantic_hash)=64), display_text_id TEXT NOT NULL, display_text_revision TEXT NOT NULL CHECK(length(display_text_revision)=64), definition_json TEXT NOT NULL CHECK(length(definition_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(furniture_id,revision), FOREIGN KEY(display_text_id,display_text_revision) REFERENCES texts(text_id,revision) ON DELETE RESTRICT)");
            if (schemaVersion >= 6)
                connection.Execute("CREATE TABLE furniture_function_bindings (furniture_id TEXT NOT NULL, furniture_revision TEXT NOT NULL CHECK(length(furniture_revision)=64), function_id TEXT NOT NULL CHECK(length(function_id)<=128), point_id TEXT NOT NULL CHECK(length(point_id)<=128), use_id TEXT NOT NULL CHECK(length(use_id)<=128), function_contract_version INTEGER NOT NULL CHECK(function_contract_version>0), contract_json TEXT NOT NULL CHECK(length(contract_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(furniture_id,furniture_revision,function_id,point_id,use_id), FOREIGN KEY(furniture_id,furniture_revision) REFERENCES furniture_definitions(furniture_id,revision) ON DELETE RESTRICT)");
            else
                connection.Execute("CREATE TABLE furniture_function_bindings (furniture_id TEXT NOT NULL, furniture_revision TEXT NOT NULL CHECK(length(furniture_revision)=64), function_id TEXT NOT NULL CHECK(length(function_id)<=128), function_contract_version INTEGER NOT NULL CHECK(function_contract_version>0), contract_json TEXT NOT NULL CHECK(length(contract_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(furniture_id,furniture_revision,function_id), FOREIGN KEY(furniture_id,furniture_revision) REFERENCES furniture_definitions(furniture_id,revision) ON DELETE RESTRICT)");
            connection.Execute("CREATE TABLE action_sequence_steps (action_id TEXT NOT NULL, action_revision TEXT NOT NULL CHECK(length(action_revision)=64), ordinal INTEGER NOT NULL CHECK(ordinal>=0), step_id TEXT NOT NULL CHECK(length(step_id)<=128), primitive_id TEXT NOT NULL CHECK(length(primitive_id)<=128), arguments_json TEXT NOT NULL CHECK(length(arguments_json)<=16384), outputs_json TEXT NOT NULL CHECK(length(outputs_json)<=16384), success_edge_json TEXT NOT NULL CHECK(length(success_edge_json)<=4096), failure_edge_json TEXT NOT NULL CHECK(length(failure_edge_json)<=4096), cancel_edge_json TEXT NOT NULL CHECK(length(cancel_edge_json)<=4096), checkpoint_policy TEXT NOT NULL CHECK(length(checkpoint_policy)<=64), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(action_id,action_revision,step_id), UNIQUE(action_id,action_revision,ordinal), FOREIGN KEY(action_id,action_revision) REFERENCES actions(action_id,revision) ON DELETE RESTRICT)");
        }

        internal static void MigrateVersion5To6(SQLiteConnection connection)
        {
            if (connection.ExecuteScalar<int>("PRAGMA user_version") != 5 || !HasVersion5Tables(connection))
                throw new System.InvalidOperationException("The content database is not a complete schema-5 store.");
            if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
                throw new System.InvalidOperationException("SQLite foreign-key enforcement must be enabled before the content migration.");

            connection.Execute("PRAGMA foreign_keys=OFF");
            try
            {
                connection.BeginTransaction();
                try
                {
                    connection.Execute("CREATE TABLE steps_v6 (flow_id TEXT NOT NULL, flow_revision TEXT NOT NULL, step_id TEXT NOT NULL CHECK(length(step_id)<=128), kind TEXT NOT NULL CHECK(kind IN ('dialogue','action','host_effect','subflow','ask','wait','branch','end')), dialogue_id TEXT, dialogue_revision TEXT, action_id TEXT, action_revision TEXT, called_flow_id TEXT, called_flow_revision TEXT, data_json TEXT NOT NULL DEFAULT '{}' CHECK(length(data_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(flow_id,flow_revision,step_id), FOREIGN KEY(flow_id,flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED, FOREIGN KEY(dialogue_id,dialogue_revision) REFERENCES dialogues(dialogue_id,revision) ON DELETE RESTRICT, FOREIGN KEY(action_id,action_revision) REFERENCES actions(action_id,revision) ON DELETE RESTRICT, FOREIGN KEY(called_flow_id,called_flow_revision) REFERENCES flows(flow_id,revision) ON DELETE RESTRICT, CHECK((kind IN ('dialogue','ask') AND dialogue_id IS NOT NULL AND dialogue_revision IS NOT NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL) OR (kind='ask' AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL) OR (kind IN ('action','host_effect') AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NOT NULL AND action_revision IS NOT NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL) OR (kind='subflow' AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NOT NULL AND called_flow_revision IS NOT NULL) OR (kind IN ('wait','branch','end') AND dialogue_id IS NULL AND dialogue_revision IS NULL AND action_id IS NULL AND action_revision IS NULL AND called_flow_id IS NULL AND called_flow_revision IS NULL)))");
                    connection.Execute("INSERT INTO steps_v6 SELECT * FROM steps");
                    connection.Execute("DROP TABLE steps");
                    connection.Execute("ALTER TABLE steps_v6 RENAME TO steps");
                    connection.Execute("CREATE INDEX steps_flow_order ON steps(flow_id,flow_revision,step_id)");

                    connection.Execute("CREATE TABLE furniture_function_bindings_v6 (furniture_id TEXT NOT NULL, furniture_revision TEXT NOT NULL CHECK(length(furniture_revision)=64), function_id TEXT NOT NULL CHECK(length(function_id)<=128), point_id TEXT NOT NULL DEFAULT '' CHECK(length(point_id)<=128), use_id TEXT NOT NULL DEFAULT '' CHECK(length(use_id)<=128), function_contract_version INTEGER NOT NULL CHECK(function_contract_version>0), contract_json TEXT NOT NULL CHECK(length(contract_json)<=16384), row_hash TEXT NOT NULL CHECK(length(row_hash)=64), PRIMARY KEY(furniture_id,furniture_revision,function_id,point_id,use_id), FOREIGN KEY(furniture_id,furniture_revision) REFERENCES furniture_definitions(furniture_id,revision) ON DELETE RESTRICT)");
                    connection.Execute("INSERT INTO furniture_function_bindings_v6(furniture_id,furniture_revision,function_id,point_id,use_id,function_contract_version,contract_json,row_hash) SELECT furniture_id,furniture_revision,function_id,'','',function_contract_version,contract_json,row_hash FROM furniture_function_bindings");
                    connection.Execute("DROP TABLE furniture_function_bindings");
                    connection.Execute("ALTER TABLE furniture_function_bindings_v6 RENAME TO furniture_function_bindings");
                    connection.Execute("UPDATE metadata SET value='6' WHERE key='schema_version'");
                    connection.Execute("PRAGMA user_version=6");
                    if (connection.Query<ForeignKeyViolation>("PRAGMA foreign_key_check").Count != 0)
                        throw new System.InvalidOperationException("The content migration introduced a foreign-key violation.");
                    connection.Commit();
                }
                catch
                {
                    if (connection.IsInTransaction) connection.Rollback();
                    throw;
                }
            }
            finally
            {
                connection.Execute("PRAGMA foreign_keys=ON");
            }
        }

        private static bool HasColumns(SQLiteConnection connection, string table, params string[] columns)
        {
            var actual = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (ColumnRow row in connection.Query<ColumnRow>("PRAGMA table_info(" + table + ")")) actual.Add(row.name);
            foreach (string column in columns) if (!actual.Contains(column)) return false;
            return true;
        }

        private sealed class ColumnRow { public string name { get; set; } }
        private sealed class ForeignKeyViolation { public string table { get; set; } }

        internal static void SetMetadata(SQLiteConnection connection, string key, string value)
        {
            connection.Execute("INSERT INTO metadata(key,value) VALUES (?,?)", key, value ?? string.Empty);
        }
    }
}
