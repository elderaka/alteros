import json
import sqlite3
import shutil
import subprocess
import tempfile
import unittest
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
WORKBENCH = ROOT / "Tools" / "ContentWorkbench"
BUILD_SCRIPT = WORKBENCH / "build-core.ps1"
HOST_EXE_NAME = "AlterOS.ContentWorkbench.exe"
HOST_CONTEXTS = WORKBENCH / "Fixtures" / "sit-together" / "host-contexts.json"
TOPIC_FIXTURE = ROOT / "Tools" / "TopicPlayground" / "Fixtures" / "topic-runtime-v1" / "Topics" / "beverage.drink.json"


class WorkbenchCommandTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="alteros-workbench-tests-")
        cls.addClassCleanup(cls.temp.cleanup)
        cls.host_bin = Path(cls.temp.name) / "isolated-host"
        cls.build_output = ""
        if BUILD_SCRIPT.exists():
            build = subprocess.run(
                ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(BUILD_SCRIPT),
                 "-OutputDirectory", str(cls.host_bin)],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                timeout=120,
            )
            cls.build_output = build.stdout + build.stderr
            if build.returncode != 0 or not (cls.host_bin / HOST_EXE_NAME).is_file():
                raise RuntimeError("Isolated build failed or omitted the Workbench host:\n" + cls.build_output)
            cls.host_build_error = ""
        else:
            raise RuntimeError("build-core.ps1 is missing.")

        cls.sandbox = Path(cls.temp.name) / "sandbox"
        cls.host = cls.sandbox / "Tools" / "ContentWorkbench" / "bin" / HOST_EXE_NAME
        (cls.sandbox / "Assets" / "AlterOS" / "Topics").mkdir(parents=True)
        (cls.sandbox / "Assets" / "AlterOS" / "Topics" / "TopicDefinition.cs").write_text("test root marker", encoding="utf-8")
        cls.host.parent.mkdir(parents=True)
        if cls.host_bin.exists():
            for item in cls.host_bin.iterdir():
                if item.is_file():
                    shutil.copy2(item, cls.host.parent / item.name)
        fixture_copy = cls.sandbox / "Tools" / "ContentWorkbench" / "Fixtures" / "sit-together" / "host-contexts.json"
        fixture_copy.parent.mkdir(parents=True)
        if HOST_CONTEXTS.exists():
            shutil.copy2(HOST_CONTEXTS, fixture_copy)

    def setUp(self):
        self.created_drafts = []
        database = self.sandbox / "Tools" / "ContentWorkbench" / "Workspace" / "authoring.sqlite3"
        if not database.exists():
            initialized = self.call("authoring.initialize")
            self.assertEqual("ok", initialized["status"], initialized["diagnostics"])

    def call(self, operation, payload=None, request_id=None, protocol_version=1):
        if self.host_build_error:
            self.fail("Workbench host build is unavailable:\n" + self.host_build_error)
        if not self.host.exists():
            self.fail("Built command host is missing: {}".format(self.host))
        request = {
            "protocolVersion": protocol_version,
            "requestId": request_id or "test-" + uuid.uuid4().hex,
            "operation": operation,
            "payload": payload or {},
        }
        try:
            process = subprocess.run(
                [str(self.host)],
                input=json.dumps(request, ensure_ascii=False).encode("utf-8"),
                cwd=str(self.sandbox),
                capture_output=True,
                timeout=10,
            )
        except OSError as error:
            self.fail("Command host did not launch: {}".format(error))
        stdout = process.stdout.decode("utf-8")
        stderr = process.stderr.decode("utf-8", errors="replace")
        self.assertEqual(process.returncode, 0, stderr)
        try:
            response = json.loads(stdout)
        except json.JSONDecodeError as error:
            self.fail("Command host did not emit one JSON response: {}\nstdout={!r}\nstderr={!r}".format(error, stdout, stderr))
        self.assertEqual(response.get("requestId"), request["requestId"])
        self.assertEqual(response.get("protocolVersion"), 1)
        self.assertIsInstance(response.get("diagnostics"), list)
        return response

    @staticmethod
    def topic_document(topic_id):
        document = json.loads(TOPIC_FIXTURE.read_text(encoding="utf-8"))
        document["topicId"] = topic_id
        document["menuLabel"] = "座って話そう"
        node = document["scenarios"][0]["nodes"][0]
        node["text"] = "一緒に座って話そう。"
        node["displayText"] = "Let's sit and talk together."
        document["futureMetadata"] = {"reviewerNote": "残しておく", "flags": ["draft", "bilingual"]}
        return document

    def create_draft_id(self, kind):
        draft_id = "test-" + uuid.uuid4().hex
        self.created_drafts.append((kind, draft_id))
        return draft_id

    def test_unknown_operation_fails_closed_and_echoes_request_id(self):
        response = self.call("route.player_text", {}, request_id="unknown-op-contract")
        self.assertEqual("invalid", response["status"])
        self.assertIn("unsupported_operation", [item["code"] for item in response["diagnostics"]])

    def test_unsupported_protocol_version_fails_closed(self):
        response = self.call("catalogue.read", {}, protocol_version=2)
        self.assertEqual("invalid", response["status"])
        self.assertIn("unsupported_protocol_version", [item["code"] for item in response["diagnostics"]])

    def test_furniture_operations_are_unavailable_without_room_f1_f2(self):
        catalogue = self.call("catalogue.read", {"registryPath": "../../outside/catalogue.json"})
        arrangement = self.call("arrangement.evaluate", {"fixtureId": "sit-together/no-table"})
        self.assertEqual("unavailable", catalogue["status"])
        self.assertEqual("unavailable", arrangement["status"])
        self.assertIn("furniture_core_unavailable", [item["code"] for item in catalogue["diagnostics"]])
        self.assertIn("furniture_core_unavailable", [item["code"] for item in arrangement["diagnostics"]])

    def test_topic_round_trip_preserves_japanese_english_and_unknown_metadata_with_cas(self):
        draft_id = self.create_draft_id("topic")
        document = self.topic_document("topic.test." + draft_id)
        created = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": document, "expectedHash": None})
        self.assertEqual("ok", created["status"], created["diagnostics"])
        self.assertEqual({"kind", "id", "contentHash"}, set(created["result"]))
        first_hash = created["result"]["contentHash"]

        loaded = self.call("draft.read", {"kind": "topic", "id": draft_id})
        self.assertEqual("ok", loaded["status"])
        actual = loaded["result"]["document"]
        self.assertEqual("一緒に座って話そう。", actual["scenarios"][0]["nodes"][0]["text"])
        self.assertEqual("Let's sit and talk together.", actual["scenarios"][0]["nodes"][0]["displayText"])
        self.assertEqual(document["futureMetadata"], actual["futureMetadata"])
        self.assertEqual(first_hash, loaded["result"]["contentHash"])

        changed = dict(actual)
        changed["menuLabel"] = "Update with current hash"
        saved = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": changed, "expectedHash": first_hash})
        self.assertEqual("ok", saved["status"])
        conflict = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": document, "expectedHash": first_hash})
        self.assertEqual("conflict", conflict["status"])
        after = self.call("draft.read", {"kind": "topic", "id": draft_id})
        self.assertEqual("Update with current hash", after["result"]["document"]["menuLabel"])

    def test_draft_list_returns_stable_metadata_without_document_text(self):
        ids = [self.create_draft_id("topic") for _ in range(2)]
        for draft_id in ids:
            document = self.topic_document("topic.test." + draft_id)
            self.assertEqual("ok", self.call("draft.write", {"kind": "topic", "id": draft_id, "document": document, "expectedHash": None})["status"])
        response = self.call("draft.list", {"kind": "topic"})
        self.assertEqual("ok", response["status"])
        rows = [item for item in response["result"]["drafts"] if item["id"] in ids]
        self.assertEqual(sorted(ids), [item["id"] for item in rows])
        self.assertTrue(all("contentHash" in item and "document" not in item for item in rows))

    def test_draft_write_serializes_concurrent_cli_writers(self):
        draft_id = self.create_draft_id("topic")
        original = self.topic_document("topic.test." + draft_id)
        created = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": original, "expectedHash": None})
        self.assertEqual("ok", created["status"])
        changed_documents = [dict(original, menuLabel="writer A"), dict(original, menuLabel="writer B")]
        with ThreadPoolExecutor(max_workers=2) as pool:
            futures = [pool.submit(self.call, "draft.write", {
                "kind": "topic", "id": draft_id, "document": changed,
                "expectedHash": created["result"]["contentHash"],
            }) for changed in changed_documents]
            responses = [future.result(timeout=20) for future in futures]
        self.assertEqual(["conflict", "ok"], sorted(item["status"] for item in responses))

    def test_semantically_invalid_draft_remains_readable_and_validate_reports_error(self):
        draft_id = self.create_draft_id("topic")
        document = self.topic_document("topic.test." + draft_id)
        document["schemaVersion"] = 99
        written = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": document, "expectedHash": None})
        self.assertEqual("ok", written["status"])
        validation = self.call("content.validate", {"kind": "topic", "document": document, "fixtureId": "sit-together/no-table"})
        self.assertEqual("invalid", validation["status"])
        self.assertTrue(validation["diagnostics"])
        read_back = self.call("draft.read", {"kind": "topic", "id": draft_id})
        self.assertEqual(99, read_back["result"]["document"]["schemaVersion"])

    def test_scenario_validation_uses_trusted_fixture_and_points_to_invalid_edge(self):
        scenario = {
            "scenarioId": "scenario.test.sit_together",
            "topicId": "topic.sit_together",
            "entryStepId": "invite",
            "steps": [
                {
                    "id": "invite",
                    "kind": "dialogue",
                    "beats": [{"id": "invite.line", "text": "一緒に座ろう。", "displayText": "Want to sit together?", "language": "japanese", "expression": "gentle"}],
                    "nextStepId": "not-a-step",
                },
                {"id": "end", "kind": "end", "outcome": "completed"},
            ],
        }
        response = self.call("content.validate", {"kind": "scenario", "document": scenario, "fixtureId": "sit-together/no-table"})
        self.assertEqual("invalid", response["status"])
        missing_edge = [item for item in response["diagnostics"] if item["code"] == "scenario_missing_successor"]
        self.assertEqual(1, len(missing_edge))
        self.assertEqual("/steps/invite/nextStepId", missing_edge[0]["path"])

    def test_draft_cannot_self_authorize_capability_missing_from_fixture(self):
        scenario = {
            "scenarioId": "scenario.test.invented_action",
            "topicId": "topic.sit_together",
            "entryStepId": "act",
            "steps": [
                {"id": "act", "kind": "action", "capabilityId": "made_up.by_draft", "arguments": [], "successNextStepId": "done", "failureNextStepId": "done"},
                {"id": "done", "kind": "end", "outcome": "completed"},
            ],
        }
        response = self.call("content.validate", {"kind": "scenario", "document": scenario, "fixtureId": "sit-together/no-table"})
        self.assertEqual("invalid", response["status"])
        self.assertIn("scenario_capability_not_allowlisted", [item["code"] for item in response["diagnostics"]])

    def test_preview_uses_real_scenario_session_with_fixture_owned_context(self):
        scenario = {
            "scenarioId": "scenario.test.preview",
            "topicId": "topic.sit_together",
            "entryStepId": "invite",
            "steps": [
                {
                    "id": "invite",
                    "kind": "dialogue",
                    "beats": [{"id": "invite.line", "text": "一緒に座ろう。", "displayText": "Want to sit together?", "language": "japanese", "expression": "gentle"}],
                    "nextStepId": "end",
                    "closeExchange": True,
                },
                {"id": "end", "kind": "end", "outcome": "completed", "closeExchange": True},
            ],
        }
        response = self.call("preview.run", {
            "kind": "scenario",
            "document": scenario,
            "fixtureId": "sit-together/no-table",
            "events": [{"kind": "dialogue_complete", "stepId": "invite", "atSeconds": 1}],
        })
        self.assertEqual("ok", response["status"], response["diagnostics"])
        self.assertEqual("sit-together/no-table", response["result"]["fixtureId"])
        self.assertTrue(response["result"]["furniture"]["fixtureOnly"])
        self.assertEqual("unavailable", response["result"]["furniture"]["integration"])
        self.assertEqual(2, len(response["result"]["reports"]))
        self.assertEqual("invite", response["result"]["reports"][0]["currentStepId"])
        self.assertEqual(0, response["result"]["providerCalls"])
        self.assertEqual(0, response["result"]["ttsCalls"])

    def test_topic_preview_uses_shared_selector_and_history_with_supplied_draw_and_time(self):
        definition = self.topic_document("topic.sit_together")
        response = self.call("preview.run", {
            "kind": "topic",
            "document": definition,
            "fixtureId": "sit-together/no-table",
            "instanceId": "workbench-topic-occurrence-001",
            "source": "authored",
            "draw": 0.0,
            "nowUtc": "2026-10-21T12:00:00Z",
            "events": [
                {"action": "present", "nodeId": "start", "atUtc": "2026-10-21T12:00:01Z"},
                {"action": "complete", "atUtc": "2026-10-21T12:00:02Z"},
            ],
        })
        self.assertEqual("ok", response["status"], response["diagnostics"])
        result = response["result"]
        self.assertEqual("casual_a", result["selectedScenarioId"])
        self.assertEqual(0.0, result["draw"])
        self.assertEqual(1, result["winningPriority"])
        self.assertEqual("direct synthetic family; not semantic routing", result["routingMode"])
        self.assertEqual("Applied", result["events"][0]["status"])
        self.assertEqual("Applied", result["events"][1]["status"])
        self.assertTrue(result["historyDirty"], "in-memory mutations are dirty but are not a durable player save")
        self.assertEqual("detached-memory-only; no player save", result.get("historyPersistence"))

    def test_invalid_legacy_json_is_ignored_by_initialized_sql_authority(self):
        draft_id = self.create_draft_id("topic")
        target = self.sandbox / "Tools" / "ContentWorkbench" / "Drafts" / "topics" / (draft_id + ".json")
        target.parent.mkdir(parents=True, exist_ok=True)
        old_bytes = b'{"unfinished":'
        target.write_bytes(old_bytes)
        read_result = self.call("draft.read", {"kind": "topic", "id": draft_id})
        self.assertEqual("invalid", read_result["status"])
        overwrite = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": self.topic_document("topic.test." + draft_id), "expectedHash": None})
        self.assertEqual("ok", overwrite["status"])
        self.assertEqual(old_bytes, target.read_bytes())

    def test_sqlite_write_lock_preserves_previous_draft_revision(self):
        draft_id = self.create_draft_id("topic")
        original = self.topic_document("topic.test." + draft_id)
        created = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": original, "expectedHash": None})
        self.assertEqual("ok", created["status"], created["diagnostics"])
        database = self.sandbox / "Tools" / "ContentWorkbench" / "Workspace" / "authoring.sqlite3"
        lock = sqlite3.connect(str(database), timeout=2, isolation_level=None)
        lock.execute("BEGIN EXCLUSIVE")
        try:
            changed = dict(original)
            changed["menuLabel"] = "must not replace while SQLite is locked"
            failure = self.call("draft.write", {"kind": "topic", "id": draft_id, "document": changed, "expectedHash": created["result"]["contentHash"]})
        finally:
            lock.rollback()
            lock.close()
        self.assertIn(failure["status"], {"busy", "conflict"}, failure["diagnostics"])
        current = self.call("draft.read", {"kind": "topic", "id": draft_id})
        self.assertEqual(original, current["result"]["document"])

    def test_backup_rejects_symlinked_destination_root(self):
        workspace = self.sandbox / "Tools" / "ContentWorkbench" / "Workspace"
        backup_root = workspace / "backups"
        outside = self.sandbox / "outside-backups"
        outside.mkdir()
        safe_link = str(backup_root).replace("'", "''")
        safe_target = str(outside).replace("'", "''")
        create = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
             "New-Item -ItemType Junction -Path '{}' -Target '{}' | Out-Null".format(safe_link, safe_target)],
            capture_output=True, text=True, timeout=10,
        )
        self.assertEqual(0, create.returncode, create.stderr)
        try:
            response = self.call("authoring.backup")
            self.assertEqual("invalid", response["status"])
            self.assertIn("authoring_backup_path_rejected", [item["code"] for item in response["diagnostics"]])
            self.assertEqual([], list(outside.iterdir()))
        finally:
            cleanup = subprocess.run(
                ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
                 "[IO.Directory]::Delete('{}')".format(safe_link)],
                capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(0, cleanup.returncode, cleanup.stderr)

    def test_path_safe_ids_reject_traversal_without_writing_outside_drafts(self):
        response = self.call("draft.write", {
            "kind": "topic",
            "id": "../../outside",
            "document": self.topic_document("topic.test.traversal"),
            "expectedHash": None,
        })
        self.assertEqual("invalid", response["status"])
        escaped = self.sandbox / "Tools" / "ContentWorkbench" / "outside.json"
        self.assertFalse(escaped.exists())

    def test_host_reference_table_has_no_unity_assemblies(self):
        escaped_host = str(self.host).replace("'", "''")
        script = "$assembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom('{}'); $assembly.GetReferencedAssemblies() | ForEach-Object {{ $_.Name }}".format(escaped_host)
        inspection = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-Command", script],
            capture_output=True,
            text=True,
            timeout=10,
        )
        self.assertEqual(0, inspection.returncode, inspection.stderr)
        references = {line.strip() for line in inspection.stdout.splitlines() if line.strip()}
        self.assertFalse({name for name in references if name.startswith("UnityEngine") or name.startswith("UnityEditor")}, references)

    def test_powershell_cli_uses_the_same_json_command_host(self):
        request = json.loads((WORKBENCH / "Fixtures" / "requests" / "unsupported-operation.json").read_text(encoding="utf-8"))
        cli = WORKBENCH / "workbench.ps1"
        process = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(cli)],
            input=json.dumps(request, ensure_ascii=False).encode("utf-8"),
            cwd=str(self.sandbox),
            capture_output=True,
            timeout=10,
        )
        self.assertEqual(0, process.returncode, process.stderr.decode("utf-8", errors="replace"))
        response = json.loads(process.stdout.decode("utf-8"))
        self.assertEqual("fixture-unsupported-operation", response["requestId"])
        self.assertEqual("invalid", response["status"])
        self.assertIn("unsupported_operation", [item["code"] for item in response["diagnostics"]])

    def test_symlink_or_junction_authoring_workspace_is_rejected_without_reading_outside(self):
        workspace = self.sandbox / "Tools" / "ContentWorkbench" / "Workspace"
        outside = self.sandbox / "outside-authoring"
        outside.mkdir(parents=True)
        database = workspace / "authoring.sqlite3"
        outside_file = outside / "authoring.sqlite3"
        original = database.read_bytes()
        outside_file.write_bytes(original)
        shutil.rmtree(workspace)
        safe_link = str(workspace).replace("'", "''")
        safe_target = str(outside).replace("'", "''")
        create = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
             "New-Item -ItemType Junction -Path '{}' -Target '{}' | Out-Null".format(safe_link, safe_target)],
            capture_output=True,
            text=True,
            timeout=10,
        )
        self.assertEqual(0, create.returncode, create.stderr)
        try:
            response = self.call("authoring.info")
            self.assertEqual("invalid", response["status"])
            self.assertIn("authoring_path_rejected", [item["code"] for item in response["diagnostics"]])
            self.assertEqual(original, outside_file.read_bytes())
        finally:
            cleanup = subprocess.run(
                ["powershell.exe", "-NoLogo", "-NoProfile", "-Command",
                 "[IO.Directory]::Delete('{}')".format(safe_link)],
                capture_output=True,
                text=True,
                timeout=10,
            )
            self.assertEqual(0, cleanup.returncode, cleanup.stderr)
            workspace.mkdir(parents=True, exist_ok=True)
            shutil.copy2(outside_file, workspace / "authoring.sqlite3")
            self.assertEqual(original, outside_file.read_bytes(), "removing the junction must not remove its target")


if __name__ == "__main__":
    unittest.main()
