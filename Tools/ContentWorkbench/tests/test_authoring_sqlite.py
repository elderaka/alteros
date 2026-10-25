import json
import shutil
import subprocess
import tempfile
import unittest
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
WORKBENCH = ROOT / "Tools" / "ContentWorkbench"
BUILD_SCRIPT = WORKBENCH / "build-core.ps1"
HOST_EXE_NAME = "AlterOS.ContentWorkbench.exe"
GENERIC_SOURCE = ROOT / "Assets" / "StreamingAssets" / "Dialogue" / "Generic" / "you_there" / "you_there_001.json"


class AuthoringSqliteTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="alteros-authoring-日本語-")
        cls.addClassCleanup(cls.temp.cleanup)
        cls.host_bin = Path(cls.temp.name) / "isolated-host"
        build = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(BUILD_SCRIPT),
             "-OutputDirectory", str(cls.host_bin)],
            cwd=str(ROOT), capture_output=True, text=True, timeout=180,
        )
        cls.build_output = build.stdout + build.stderr
        cls.build_error = "" if build.returncode == 0 and (cls.host_bin / HOST_EXE_NAME).is_file() else "isolated build-core.ps1 failed or omitted the Workbench host:\n{}".format(cls.build_output)
        if cls.build_error:
            raise RuntimeError(cls.build_error)

    def setUp(self):
        self.sandbox = Path(self.temp.name) / uuid.uuid4().hex
        self.make_project(self.sandbox)

    @classmethod
    def make_project(cls, root):
        marker = root / "Assets" / "AlterOS" / "Topics" / "TopicDefinition.cs"
        marker.parent.mkdir(parents=True, exist_ok=True)
        marker.write_text("test root marker", encoding="utf-8")
        host_dir = root / "Tools" / "ContentWorkbench" / "bin"
        host_dir.mkdir(parents=True, exist_ok=True)
        if cls.host_bin.exists():
            for item in cls.host_bin.iterdir():
                if item.is_file():
                    shutil.copy2(item, host_dir / item.name)
        (root / "Tools" / "ContentWorkbench" / "Fixtures" / "sit-together").mkdir(parents=True, exist_ok=True)

    def call(self, root, operation, payload=None):
        if self.build_error:
            self.fail(self.build_error)
        host = root / "Tools" / "ContentWorkbench" / "bin" / HOST_EXE_NAME
        request = {
            "protocolVersion": 1,
            "requestId": "test-" + uuid.uuid4().hex,
            "operation": operation,
            "payload": payload or {},
        }
        process = subprocess.run(
            [str(host)], input=json.dumps(request, ensure_ascii=False).encode("utf-8"),
            cwd=str(root), capture_output=True, timeout=20,
        )
        self.assertEqual(0, process.returncode, process.stderr.decode("utf-8", errors="replace"))
        response = json.loads(process.stdout.decode("utf-8"))
        self.assertEqual(request["requestId"], response["requestId"])
        self.assertEqual(1, response["protocolVersion"])
        return response

    def initialize(self, root=None):
        root = root or self.sandbox
        result = self.call(root, "authoring.initialize")
        self.assertEqual("ok", result["status"], result["diagnostics"])
        return result["result"]["store"]

    @staticmethod
    def topic_document(topic_id):
        return {
            "topicId": topic_id,
            "title": "座って話そう",
            "scenarios": [],
            "futureMetadata": {"reviewerNote": "残しておく", "languagePairs": ["ja", "en"]},
        }

    def test_missing_store_never_reads_or_writes_legacy_draft_files(self):
        draft = self.sandbox / "Tools" / "ContentWorkbench" / "Drafts" / "topics" / "legacy.json"
        draft.parent.mkdir(parents=True)
        original = b'{"workbenchSchemaVersion":1,"kind":"topic","id":"legacy","document":{"private":"old"}}'
        draft.write_bytes(original)

        read = self.call(self.sandbox, "draft.read", {"kind": "topic", "id": "legacy"})
        write = self.call(self.sandbox, "draft.write", {
            "kind": "topic", "id": "legacy", "document": self.topic_document("topic.legacy"), "expectedHash": None,
        })
        export = self.call(self.sandbox, "authoring.export", {
            "documentIds": ["legacy"], "expectedStoreId": "fa023336-caee-4fa5-b611-442c9268a639",
            "expectedStoreRevision": 0,
        })

        self.assertEqual("not_initialized", read["status"])
        self.assertEqual("not_initialized", write["status"])
        self.assertEqual("not_initialized", export["status"])
        self.assertEqual(original, draft.read_bytes())
        self.assertFalse((self.sandbox / "Tools" / "ContentWorkbench" / "Workspace").exists())
        self.assertFalse((self.sandbox / "Content" / "AuthoringExports").exists())

    def test_sql_draft_round_trip_reopen_and_expected_hash_conflict(self):
        self.initialize()
        draft_id = "draft-" + uuid.uuid4().hex
        document = self.topic_document("topic." + draft_id)
        created = self.call(self.sandbox, "draft.write", {
            "kind": "topic", "id": draft_id, "document": document, "editorState": {"selectedStep": "start"},
            "expectedHash": None,
        })
        self.assertEqual("ok", created["status"], created["diagnostics"])
        content_hash = created["result"]["contentHash"]

        loaded = self.call(self.sandbox, "draft.read", {"kind": "topic", "id": draft_id})
        self.assertEqual("ok", loaded["status"], loaded["diagnostics"])
        self.assertEqual(document, loaded["result"]["document"])
        self.assertEqual({"selectedStep": "start"}, loaded["result"]["editorState"])
        self.assertEqual(content_hash, loaded["result"]["contentHash"])

        changed = dict(document, title="New label")
        saved = self.call(self.sandbox, "draft.write", {
            "kind": "topic", "id": draft_id, "document": changed, "editorState": {"selectedStep": "start"},
            "expectedHash": content_hash,
        })
        conflict = self.call(self.sandbox, "draft.write", {
            "kind": "topic", "id": draft_id, "document": document, "expectedHash": content_hash,
        })
        self.assertEqual("ok", saved["status"])
        self.assertEqual("conflict", conflict["status"])
        self.assertFalse((self.sandbox / "Tools" / "ContentWorkbench" / "Drafts").exists())

    def test_initialize_refuses_to_replace_existing_database(self):
        self.initialize()
        database = self.sandbox / "Tools" / "ContentWorkbench" / "Workspace" / "authoring.sqlite3"
        before = database.read_bytes()
        again = self.call(self.sandbox, "authoring.initialize")
        self.assertEqual("conflict", again["status"])
        self.assertEqual(before, database.read_bytes())

    def test_complete_authoring_commands_preserve_revisions_unknown_fields_and_cas(self):
        self.initialize()
        document = {
            "id": "generic.sql-command",
            "source": "generic",
            "routing": {"routeId": "sql.command"},
            "nodes": [],
            "texts": {"ja": "こんにちは", "en": "Hello"},
            "futureFields": {"unknown": ["preserve", 17]},
        }
        created = self.call(self.sandbox, "authoring.write", {
            "documentId": "generic-sql-command", "kind": "generic_dialogue",
            "logicalPath": "Generic/sql-command/entry.json", "document": document,
            "expectedHash": None, "operationId": "sql-command-create-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", created["status"], created["diagnostics"])
        first_hash = created["result"]["revisionHash"]

        listed = self.call(self.sandbox, "authoring.list", {"kind": "generic_dialogue", "limit": 10})
        self.assertEqual("ok", listed["status"], listed["diagnostics"])
        self.assertIn("generic-sql-command", [item["documentId"] for item in listed["result"]["documents"]])
        loaded = self.call(self.sandbox, "authoring.read", {"documentId": "generic-sql-command"})
        self.assertEqual(document, loaded["result"]["document"])

        changed = dict(document, texts={"ja": "更新", "en": "Updated"})
        updated = self.call(self.sandbox, "authoring.write", {
            "documentId": "generic-sql-command", "kind": "generic_dialogue",
            "logicalPath": "Generic/sql-command/entry.json", "document": changed,
            "expectedHash": first_hash, "operationId": "sql-command-update-" + uuid.uuid4().hex,
        })
        stale = self.call(self.sandbox, "authoring.write", {
            "documentId": "generic-sql-command", "kind": "generic_dialogue",
            "logicalPath": "Generic/sql-command/entry.json", "document": document,
            "expectedHash": first_hash, "operationId": "sql-command-stale-" + uuid.uuid4().hex,
        })
        old_revision = self.call(self.sandbox, "authoring.read", {
            "documentId": "generic-sql-command", "revisionHash": first_hash,
        })
        self.assertEqual("ok", updated["status"])
        self.assertEqual("conflict", stale["status"])
        self.assertEqual(document, old_revision["result"]["document"])

    def test_explicit_legacy_import_and_build_selection(self):
        self.initialize()
        source_root = self.sandbox / "Assets" / "StreamingAssets" / "Dialogue"
        source = source_root / "Generic" / "you_there" / "you_there_001.json"
        source.parent.mkdir(parents=True)
        shutil.copy2(GENERIC_SOURCE, source)
        store = self.call(self.sandbox, "authoring.info")["result"]["store"]
        imported = self.call(self.sandbox, "authoring.import", {
            "mode": "legacy", "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "operationId": "legacy-import-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", imported["status"], imported["diagnostics"])
        self.assertEqual(1, imported["result"]["receipt"]["sourceCount"])
        source_rows = imported["result"]["receipt"]["sources"]
        document_ids = [item["documentId"] for item in source_rows]
        imported_document = self.call(self.sandbox, "authoring.read", {"documentId": document_ids[0]})
        imported_hash = imported_document["result"]["contentHash"]
        source.unlink()
        persisted = self.call(self.sandbox, "authoring.read", {"documentId": document_ids[0]})
        self.assertEqual(imported_hash, persisted["result"]["contentHash"])

        missing_selection = self.call(self.sandbox, "authoring.selection.read", {"name": "default"})
        self.assertEqual("invalid", missing_selection["status"])
        selection = self.call(self.sandbox, "authoring.selection.write", {
            "name": "default", "documentIds": document_ids, "expectedHash": None,
            "operationId": "select-import-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", selection["status"], selection["diagnostics"])
        self.assertEqual(document_ids, selection["result"]["selection"]["documentIds"])
        stale_selection = self.call(self.sandbox, "authoring.selection.write", {
            "name": "default", "documentIds": document_ids, "expectedHash": None,
            "operationId": "stale-select-" + uuid.uuid4().hex,
        })
        self.assertEqual("conflict", stale_selection["status"])
        self.assertEqual(document_ids,
                         self.call(self.sandbox, "authoring.selection.read", {"name": "default"})["result"]["selection"]["documentIds"])

    def test_sql_store_initializes_and_round_trips_with_only_declared_host_files(self):
        self.initialize()
        host_dir = self.sandbox / "Tools" / "ContentWorkbench" / "bin"
        self.assertTrue((host_dir / "gilzoide-sqlite-net.dll").exists())
        self.assertTrue((host_dir / "Newtonsoft.Json.dll").exists())
        assembly = host_dir / HOST_EXE_NAME
        escaped = str(assembly).replace("'", "''")
        check = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
             "$a=[Reflection.Assembly]::ReflectionOnlyLoadFrom('{}'); $a.GetReferencedAssemblies() | % {{$_.Name}}".format(escaped)],
            capture_output=True, text=True, timeout=10,
        )
        self.assertEqual(0, check.returncode, check.stderr)
        references = {line.strip() for line in check.stdout.splitlines() if line.strip()}
        self.assertFalse({name for name in references if name.startswith("UnityEngine") or name.startswith("UnityEditor")}, references)
        self.assertTrue((self.sandbox / "Tools" / "ContentWorkbench" / "Workspace" / "authoring.sqlite3").is_file())


if __name__ == "__main__":
    unittest.main()
