import hashlib
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


class AuthoringInterchangeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="alteros-authoring-export-日本語-")
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

    @classmethod
    def make_project(cls, name):
        root = Path(cls.temp.name) / name
        marker = root / "Assets" / "AlterOS" / "Topics" / "TopicDefinition.cs"
        marker.parent.mkdir(parents=True, exist_ok=True)
        marker.write_text("test root marker", encoding="utf-8")
        host_dir = root / "Tools" / "ContentWorkbench" / "bin"
        host_dir.mkdir(parents=True, exist_ok=True)
        for item in cls.host_bin.iterdir():
            if item.is_file():
                shutil.copy2(item, host_dir / item.name)
        return root

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
        return response

    def test_deterministic_export_fresh_import_original_bytes_and_verified_backup(self):
        source = self.make_project("source_日本語")
        initialized = self.call(source, "authoring.initialize")
        self.assertEqual("ok", initialized["status"], initialized["diagnostics"])
        document = {
            "id": "generic.roundtrip",
            "source": "generic",
            "routing": {"routeId": "test.roundtrip"},
            "nodes": [{"id": "start", "text": "こんにちは", "displayText": "Hello"}],
            "unknownMetadata": {"preserve": True, "labels": ["日本語", "English"]},
        }
        saved = self.call(source, "authoring.write", {
            "documentId": "generic-roundtrip",
            "kind": "generic_dialogue",
            "logicalPath": "Generic/test-roundtrip/entry.json",
            "document": document,
            "expectedHash": None,
            "operationId": "write-roundtrip-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", saved["status"], saved["diagnostics"])
        document_id = saved["result"]["documentId"]
        selected = self.call(source, "authoring.selection.write", {
            "name": "default", "documentIds": [document_id], "expectedHash": None,
            "operationId": "select-roundtrip-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", selected["status"], selected["diagnostics"])
        info = self.call(source, "authoring.info")["result"]["store"]
        export_payload = {
            "documentIds": [document_id],
            "expectedStoreId": info["storeId"],
            "expectedStoreRevision": info["storeRevision"],
        }
        first = self.call(source, "authoring.export", export_payload)
        self.assertEqual("ok", first["status"], first["diagnostics"])
        receipt = first["result"]["export"]
        export_dir = Path(receipt["directoryPath"])
        original_bytes = (export_dir / "originals" / "doc-00001.json").read_bytes()
        self.assertEqual(saved["result"]["revisionHash"], hashlib.sha256(original_bytes).hexdigest())

        repeated = self.call(source, "authoring.export", export_payload)
        self.assertEqual("ok", repeated["status"], repeated["diagnostics"])
        self.assertTrue(repeated["result"]["export"]["reusedExistingExport"])
        self.assertEqual((export_dir / "manifest.json").read_bytes(),
                         (Path(repeated["result"]["export"]["directoryPath"]) / "manifest.json").read_bytes())
        self.assertEqual(original_bytes,
                         (Path(repeated["result"]["export"]["directoryPath"]) / "originals" / "doc-00001.json").read_bytes())

        clone = self.make_project("fresh-clone_日本語")
        clone_export_root = clone / "Content" / "AuthoringExports"
        clone_export_root.mkdir(parents=True)
        shutil.copytree(export_dir, clone_export_root / receipt["snapshotHash"])
        self.assertEqual("ok", self.call(clone, "authoring.initialize")["status"])
        clone_info = self.call(clone, "authoring.info")["result"]["store"]
        import_operation_id = "import-export-" + uuid.uuid4().hex
        import_payload = {
            "mode": "export", "snapshotHash": receipt["snapshotHash"],
            "expectedStoreId": clone_info["storeId"], "expectedStoreRevision": clone_info["storeRevision"],
            "operationId": import_operation_id,
        }
        imported = self.call(clone, "authoring.import", import_payload)
        self.assertEqual("ok", imported["status"], imported["diagnostics"])
        self.assertEqual(1, imported["result"]["receipt"]["sourceCount"])
        replayed = self.call(clone, "authoring.import", import_payload)
        self.assertEqual("ok", replayed["status"], replayed["diagnostics"])
        self.assertEqual(imported["result"]["receipt"]["receiptHash"], replayed["result"]["receipt"]["receiptHash"])
        restored = self.call(clone, "authoring.read", {"documentId": document_id})
        self.assertEqual("ok", restored["status"], restored["diagnostics"])
        self.assertEqual(document, restored["result"]["document"])
        self.assertEqual(saved["result"]["revisionHash"], restored["result"]["contentHash"])

        conflict = self.call(clone, "authoring.import", {
            "mode": "export", "snapshotHash": receipt["snapshotHash"],
            "expectedStoreId": clone_info["storeId"], "expectedStoreRevision": clone_info["storeRevision"],
            "operationId": "import-again-" + uuid.uuid4().hex,
        })
        self.assertEqual("conflict", conflict["status"])
        self.assertEqual(1, self.call(clone, "authoring.info")["result"]["store"]["storeRevision"])

        backup = self.call(source, "authoring.backup")
        self.assertEqual("ok", backup["status"], backup["diagnostics"])
        backup_path = Path(backup["result"]["backup"]["backupPath"])
        recovery = self.make_project("backup-reopen_日本語")
        recovery_db = recovery / "Tools" / "ContentWorkbench" / "Workspace" / "authoring.sqlite3"
        recovery_db.parent.mkdir(parents=True)
        shutil.copy2(backup_path, recovery_db)
        recovered = self.call(recovery, "authoring.info")
        self.assertEqual("ok", recovered["status"], recovered["diagnostics"])
        self.assertEqual(info["storeId"], recovered["result"]["store"]["storeId"])
        self.assertEqual(info["storeRevision"], recovered["result"]["store"]["storeRevision"])

    def test_export_command_rejects_untrusted_hash_as_a_path(self):
        root = self.make_project("unsafe-export-日本語")
        self.assertEqual("ok", self.call(root, "authoring.initialize")["status"])
        info = self.call(root, "authoring.info")["result"]["store"]
        result = self.call(root, "authoring.import", {
            "mode": "export", "snapshotHash": "../../outside",
            "expectedStoreId": info["storeId"], "expectedStoreRevision": 0,
            "operationId": "unsafe-import-" + uuid.uuid4().hex,
        })
        self.assertEqual("invalid", result["status"])
        self.assertIn("authoring_export_hash_invalid", [item["code"] for item in result["diagnostics"]])
        self.assertFalse((Path(self.temp.name) / "outside").exists())

    def test_export_import_rejects_a_junctioned_configured_export_root(self):
        source = self.make_project("junction-export-source_日本語")
        self.assertEqual("ok", self.call(source, "authoring.initialize")["status"])
        document = {"id": "generic.junction", "source": "generic", "routing": {"routeId": "junction"}, "nodes": []}
        saved = self.call(source, "authoring.write", {
            "documentId": "generic-junction", "kind": "generic_dialogue",
            "logicalPath": "Generic/junction/entry.json", "document": document,
            "expectedHash": None, "operationId": "junction-doc-" + uuid.uuid4().hex,
        })
        document_id = saved["result"]["documentId"]
        self.assertEqual("ok", self.call(source, "authoring.selection.write", {
            "name": "default", "documentIds": [document_id], "expectedHash": None,
            "operationId": "junction-selection-" + uuid.uuid4().hex,
        })["status"])
        store = self.call(source, "authoring.info")["result"]["store"]
        exported = self.call(source, "authoring.export", {
            "documentIds": [document_id], "expectedStoreId": store["storeId"],
            "expectedStoreRevision": store["storeRevision"],
        })["result"]["export"]

        target = self.make_project("junction-export-target_日本語")
        outside = Path(self.temp.name) / "outside-export-root"
        outside.mkdir()
        shutil.copytree(exported["directoryPath"], outside / exported["snapshotHash"])
        content_root = target / "Content"
        content_root.mkdir()
        export_root = content_root / "AuthoringExports"
        safe_link = str(export_root).replace("'", "''")
        safe_target = str(outside).replace("'", "''")
        create = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
             "New-Item -ItemType Junction -Path '{}' -Target '{}' | Out-Null".format(safe_link, safe_target)],
            capture_output=True, text=True, timeout=10,
        )
        self.assertEqual(0, create.returncode, create.stderr)
        try:
            self.assertEqual("ok", self.call(target, "authoring.initialize")["status"])
            info = self.call(target, "authoring.info")["result"]["store"]
            rejected = self.call(target, "authoring.import", {
                "mode": "export", "snapshotHash": exported["snapshotHash"],
                "expectedStoreId": info["storeId"], "expectedStoreRevision": 0,
                "operationId": "junction-import-" + uuid.uuid4().hex,
            })
            self.assertEqual("invalid", rejected["status"])
            self.assertIn("authoring_export_path_rejected", [item["code"] for item in rejected["diagnostics"]])
            self.assertEqual(0, self.call(target, "authoring.info")["result"]["store"]["storeRevision"])
        finally:
            cleanup = subprocess.run(
                ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
                 "[IO.Directory]::Delete('{}')".format(safe_link)],
                capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(0, cleanup.returncode, cleanup.stderr)


if __name__ == "__main__":
    unittest.main()
