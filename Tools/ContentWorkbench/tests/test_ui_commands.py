import json
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
import uuid
from pathlib import Path

WORKBENCH = Path(__file__).resolve().parents[1]
ROOT = WORKBENCH.parents[1]
BUILD_SCRIPT = WORKBENCH / "build-core.ps1"
HOST_CONTEXTS = WORKBENCH / "Fixtures" / "sit-together" / "host-contexts.json"
sys.path.insert(0, str(WORKBENCH))
from server import create_server


KIND_MAP = {
    "dialogue": ("dialogue_definition", "generic_dialogue"),
    "scenario": ("scenario_definition",),
    "topic": ("topic_definition",),
    "furniture": ("furniture_definition",),
    "item": ("item_definition",),
    "action": ("action_definition",),
}


class WorkbenchEditorCommandTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="alteros-workbench-editor-")
        cls.addClassCleanup(cls.temp.cleanup)
        cls.project = Path(cls.temp.name) / "sandbox"
        (cls.project / "Assets" / "AlterOS" / "Topics").mkdir(parents=True)
        (cls.project / "Assets" / "AlterOS" / "Topics" / "TopicDefinition.cs").write_text("synthetic root marker", encoding="utf-8")
        expression_dir = cls.project / "Assets" / "Altera" / "Presets"
        expression_dir.mkdir(parents=True)
        shutil.copy2(ROOT / "Assets" / "Altera" / "Presets" / "happy.asset", expression_dir / "happy.asset")
        fixture_dir = cls.project / "Tools" / "ContentWorkbench" / "Fixtures" / "sit-together"
        fixture_dir.mkdir(parents=True)
        shutil.copy2(HOST_CONTEXTS, fixture_dir / "host-contexts.json")
        cls.host_dir = cls.project / "Tools" / "ContentWorkbench" / "CoreHost" / "bin"
        build = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(BUILD_SCRIPT), "-OutputDirectory", str(cls.host_dir)],
            cwd=str(ROOT), capture_output=True, text=True, timeout=180,
        )
        if build.returncode != 0:
            raise AssertionError("Temporary Workbench host compile failed:\n" + build.stdout + build.stderr)
        cls.host = cls.host_dir / "AlterOS.ContentWorkbench.exe"
        cls.server = create_server(
            [str(cls.host)], cls.project, WORKBENCH / "web", token="editor-test-token",
            port=0, request_timeout=5, max_concurrent=1,
        )
        cls.server_thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.server_thread.start()
        cls.http_base = "http://127.0.0.1:{}".format(cls.server.server_port)

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.server_thread.join(timeout=2)

    def setUp(self):
        database = self.project / "Tools" / "ContentWorkbench" / "Workspace" / "authoring.sqlite3"
        if database.exists():
            database.unlink()
        result = self.call_cli("authoring.initialize", {})
        self.assertEqual("ok", result["status"], result["diagnostics"])

    def request(self, operation, payload, request_id=None):
        return {
            "protocolVersion": 1,
            "requestId": request_id or "ui-test-" + uuid.uuid4().hex,
            "operation": operation,
            "payload": payload,
        }

    def call_cli(self, operation, payload, request_id=None):
        request = self.request(operation, payload, request_id)
        process = subprocess.run(
            [str(self.host)], input=json.dumps(request, ensure_ascii=False).encode("utf-8"),
            cwd=str(self.project), capture_output=True, timeout=10,
        )
        self.assertEqual(0, process.returncode, process.stderr.decode("utf-8", errors="replace"))
        return json.loads(process.stdout.decode("utf-8"))

    def call_http(self, operation, payload, request_id=None):
        request = self.request(operation, payload, request_id)
        message = urllib.request.Request(
            self.http_base + "/api/command",
            data=json.dumps(request, ensure_ascii=False).encode("utf-8"),
            method="POST",
            headers={
                "Content-Type": "application/json",
                "Origin": self.http_base,
                "X-Workbench-Token": "editor-test-token",
            },
        )
        with urllib.request.urlopen(message, timeout=10) as response:
            return json.loads(response.read().decode("utf-8"))

    def resolve_source(self, kind, document_id, document, target_kind, missing_id):
        saved = self.call_cli("authoring.write", {
            "kind": kind, "documentId": document_id, "logicalPath": "References/{}.json".format(document_id),
            "document": document, "expectedHash": None, "operationId": "missing-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", saved["status"], saved["diagnostics"])
        store = self.call_cli("authoring.info", {})["result"]["store"]
        result = self.call_cli("references.list", {
            "targetKind": target_kind,
            "sourcePins": [{"documentId": document_id, "revisionHash": saved["result"]["contentHash"]}],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
        })
        self.assertNotEqual("ok", result["status"], "Missing selected-package dependency was advertised as resolved: " + missing_id)
        return result["diagnostics"]

    @staticmethod
    def furniture_document(spec):
        definition_id = spec["id"]
        text_id = "text." + definition_id.split(".", 1)[1].replace(".", "_")
        functions = spec.get("functions", [])
        presentations = spec.get("presentations", [])
        use_ids = spec.get("useIds", [])
        return {
            "schemaVersion": 1,
            "id": definition_id,
            "displayName": {"id": text_id, "translations": [{"language": "en", "text": definition_id}]},
            "definition": {
                "definitionId": definition_id, "displayNameKey": text_id,
                "category": spec["category"], "scalePolicy": "fixed",
                "defaultScale": {"x": 1, "y": 1, "z": 1},
                "localBounds": {"minimum": {"x": -0.5, "y": 0, "z": -0.5}, "maximum": {"x": 0.5, "y": 1, "z": 0.5}},
                "supportKinds": ["roomFloor"], "blocksNavigation": True,
                "points": [{"pointId": "point.use", "pointTypeId": "use", "localPosition": {"x": 0, "y": 0, "z": 0},
                    "localFacing": {"x": 0, "y": 0, "z": 1}, "useIds": use_ids}] if use_ids else [],
                "functionBindings": [{key: binding[key] for key in ("functionId", "pointId", "useId")} for binding in functions],
                "presentationBindings": [{key: binding[key] for key in ("descriptorId", "pointId", "useId")} for binding in presentations],
            },
            "functions": functions,
        }

    def save_preview_source(self, kind, document_id, logical_path, document):
        saved = self.call_cli("authoring.write", {
            "kind": kind, "documentId": document_id, "logicalPath": logical_path,
            "document": document, "expectedHash": None, "operationId": "preview-source-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", saved["status"], saved["diagnostics"])
        return {"documentId": document_id, "revisionHash": saved["result"]["contentHash"]}

    def resolve_preview_reference(self, target_kind, pins):
        store = self.call_cli("authoring.info", {})["result"]["store"]
        result = self.call_cli("references.list", {"targetKind": target_kind, "sourcePins": pins,
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]})
        self.assertEqual("ok", result["status"], result["diagnostics"])
        return next(item["ref"] for item in result["result"]["references"] if item["targetKind"] == target_kind)

    def save_preview_bark(self, dialogue_id, display_text, speech_text="静かな夜ですね。"):
        return self.save_preview_source("dialogue_definition", dialogue_id,
            "Dialogue/{}.json".format(dialogue_id), {"schemaVersion": 1, "id": dialogue_id, "beats": [{
                "beatId": "beat.open", "text": {"id": "text." + dialogue_id.replace(".", "_"),
                    "translations": [{"language": "en", "text": display_text}, {"language": "ja", "text": speech_text}]},
                "expression": "", "presentation": {},
            }]})

    @staticmethod
    def sequence_literal(name, value_type, literal):
        return {"name": name, "value": {"type": value_type, "literal": literal}}

    @staticmethod
    def sequence_binding(name, binding_id):
        return {"name": name, "value": {"bindingId": binding_id}}

    @classmethod
    def sequence_step(cls, step_id, primitive_id, arguments=None, outputs=None, success="", failure="", cancel=""):
        return {"stepId": step_id, "primitiveId": primitive_id, "arguments": arguments or [],
            "resultBindings": outputs or [], "successNextStepId": success, "failureNextStepId": failure,
            "cancelNextStepId": cancel, "checkpointPolicy": "none"}

    def save_sequence_action(self, action_id, steps):
        return self.save_preview_source("action_definition", action_id, "Action/{}.json".format(action_id), {
            "schemaVersion": 1, "id": action_id, "capabilityId": "scenario.activity_sequence",
            "implementationKind": "activity_sequence", "data": {"steps": steps},
        })

    def save_preview_scenario(self, scenario_id, action_ref, dependency_pins, failure_text="No matching fixture target."):
        scenario_pin = self.save_preview_source("scenario_definition", scenario_id,
            "Scenario/{}.json".format(scenario_id), {
                "scenarioId": scenario_id, "topicId": "topic." + scenario_id.split(".", 1)[1],
                "entryStepId": "step.action", "steps": [
                    {"id": "step.action", "kind": "action", "actionRef": action_ref,
                     "successNextStepId": "step.end", "failureNextStepId": "step.fallback"},
                    {"id": "step.fallback", "kind": "dialogue", "beats": [{"id": "beat.fallback", "speaker": "alterra",
                        "text": failure_text, "displayText": failure_text, "language": "japanese", "expression": ""}],
                     "nextStepId": "step.failed"},
                    {"id": "step.failed", "kind": "end", "outcome": "failed"},
                    {"id": "step.end", "kind": "end", "outcome": "completed"},
                ],
            })
        return scenario_pin, self.resolve_preview_reference("scenario", list(dependency_pins) + [scenario_pin])

    def save_preview_furniture_catalogue(self):
        fixture = json.loads((WORKBENCH / "Fixtures" / "proof-flows" / "furniture.json").read_text(encoding="utf-8"))
        settings = dict(fixture["settings"])
        settings.pop("definitions", None)
        pins = [self.save_preview_source("furniture_catalogue_settings", "furniture.catalogue",
            "Furniture/catalogue-settings.json", settings)]
        for definition in fixture["definitions"]:
            pins.append(self.save_preview_source("furniture_definition", definition["id"],
                "Furniture/Definitions/{}.json".format(definition["id"]), self.furniture_document(definition)))
        store = self.call_cli("authoring.info", {})["result"]["store"]
        return pins, store

    def test_reference_candidates_reject_item_with_missing_required_text(self):
        store = self.call_cli("authoring.info", {})["result"]["store"]
        diagnostics = self.resolve_source("item_definition", "item.missing_text", {
            "schemaVersion": 1, "id": "item.missing_text", "displayNameRef": {
                "store": store["storeId"], "id": "text.not_selected", "revision": "a" * 64, "expectedHash": "a" * 64,
            }, "stackLimit": 1,
        }, "item", "text.not_selected")
        self.assertIn("text_dependency_missing", [item["code"] for item in diagnostics])

    def test_reference_candidates_reject_scenario_with_missing_dialogue(self):
        store = self.call_cli("authoring.info", {})["result"]["store"]
        diagnostics = self.resolve_source("scenario_definition", "scenario.missing_dialogue", {
            "scenarioId": "scenario.missing_dialogue", "topicId": "topic.fixture", "entryStepId": "step.dialogue",
            "steps": [
                {"id": "step.dialogue", "kind": "dialogue", "dialogueRef": {
                    "store": store["storeId"], "id": "dialogue.not_selected", "revision": "a" * 64, "expectedHash": "a" * 64,
                }, "nextStepId": "step.end"},
                {"id": "step.end", "kind": "end", "outcome": "completed"},
            ],
        }, "scenario", "dialogue.not_selected")
        self.assertIn("dialogue_dependency_missing", [item["code"] for item in diagnostics])

    def test_reference_candidates_reject_scenario_with_missing_action(self):
        store = self.call_cli("authoring.info", {})["result"]["store"]
        diagnostics = self.resolve_source("scenario_definition", "scenario.missing_action", {
            "scenarioId": "scenario.missing_action", "topicId": "topic.fixture", "entryStepId": "step.action",
            "steps": [
                {"id": "step.action", "kind": "action", "actionRef": {
                    "store": store["storeId"], "id": "action.not_selected", "revision": "a" * 64, "expectedHash": "a" * 64,
                }, "successNextStepId": "step.end", "failureNextStepId": "step.end"},
                {"id": "step.end", "kind": "end", "outcome": "completed"},
            ],
        }, "scenario", "action.not_selected")
        self.assertIn("action_dependency_missing", [item["code"] for item in diagnostics])

    def test_reference_candidates_reject_host_effect_action_with_missing_item(self):
        store = self.call_cli("authoring.info", {})["result"]["store"]
        diagnostics = self.resolve_source("action_definition", "action.missing_item", {
            "schemaVersion": 1, "id": "action.missing_item", "capabilityId": "player.item.grant",
            "implementationKind": "host_effect", "data": {
                "functionId": "player.item.grant", "arguments": {
                    "itemRef": {"store": store["storeId"], "id": "item.not_selected", "revision": "a" * 64, "expectedHash": "a" * 64},
                    "quantity": 1,
                },
            },
        }, "action", "item.not_selected")
        self.assertIn("item_dependency_missing", [item["code"] for item in diagnostics])

    def test_discovery_is_exact_six_kinds_and_ui_cli_use_the_same_descriptors(self):
        descriptors = []
        for kind_id, source_kinds in KIND_MAP.items():
            payload = {"kindId": kind_id}
            cli = self.call_cli("schema.describe", payload, "describe-" + kind_id)
            browser = self.call_http("schema.describe", payload, "describe-" + kind_id)
            self.assertEqual(cli, browser)
            self.assertEqual("ok", cli["status"], cli["diagnostics"])
            descriptor = cli["result"]["descriptor"]
            self.assertEqual(kind_id, descriptor["kindId"])
            self.assertEqual(list(source_kinds), descriptor["sourceKinds"])
            if kind_id == "furniture":
                self.assertEqual(["furniture_definition", "furniture_catalogue_settings"], descriptor["referenceSourceKinds"])
            self.assertTrue(descriptor["available"])
            self.assertTrue(descriptor["capabilities"]["create"])
            self.assertTrue(descriptor["capabilities"]["read"])
            self.assertTrue(descriptor["capabilities"]["edit"])
            self.assertTrue(descriptor["capabilities"]["validate"])
            descriptors.append(kind_id)
        self.assertEqual(["dialogue", "scenario", "topic", "furniture", "item", "action"], descriptors)

    def test_independent_dialogue_validation_uses_kind_id_and_reports_producer_path(self):
        dialogue = {
            "schemaVersion": 1,
            "id": "dialogue.editor_test",
            "beats": [{
                "beatId": "beat.opening",
                "text": {
                    "id": "text.editor_test",
                    "translations": [
                        {"language": "en", "text": "Hello"},
                        {"language": "ja", "text": "こんにちは"},
                    ],
                },
            }],
        }
        cli = self.call_cli("content.validate", {"kindId": "dialogue", "document": dialogue}, "validate-dialogue")
        browser = self.call_http("content.validate", {"kindId": "dialogue", "document": dialogue}, "validate-dialogue")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        self.assertTrue(cli["result"]["valid"])
        self.assertEqual("dialogue_definition", cli["result"]["sourceKind"])

    def test_all_six_kind_validators_are_reached_through_kind_id(self):
        for kind_id, source_kinds in KIND_MAP.items():
            payload = {"kindId": kind_id, "document": {}}
            cli = self.call_cli("content.validate", payload, "invalid-" + kind_id)
            browser = self.call_http("content.validate", payload, "invalid-" + kind_id)
            self.assertEqual(cli, browser)
            self.assertEqual(source_kinds[0], cli["result"]["sourceKind"])
            self.assertNotEqual("content_kind_invalid", cli["diagnostics"][0]["code"])

    def test_save_read_edit_and_stale_cas_cover_all_source_kinds_without_losing_unknown_fields(self):
        for kind_id, source_kinds in KIND_MAP.items():
            source_kind = source_kinds[0]
            document_id = "editor." + kind_id
            logical_path = "Workbench/{}/{}.json".format(kind_id, document_id)
            document = {"schemaVersion": 1, "id": document_id, "extensionMetadata": {"keep": "original"}}
            create_payload = {
                "kind": source_kind,
                "documentId": document_id,
                "logicalPath": logical_path,
                "document": document,
                "expectedHash": None,
                "operationId": "create-" + kind_id,
            }
            created = self.call_cli("authoring.write", create_payload)
            self.assertEqual("ok", created["status"], created["diagnostics"])
            self.assertEqual("ok", self.call_http("authoring.write", create_payload)["status"])
            first_hash = created["result"]["contentHash"]

            read_payload = {"documentId": document_id, "revisionHash": first_hash}
            read = self.call_cli("authoring.read", read_payload)
            self.assertEqual("ok", read["status"], read["diagnostics"])
            self.assertEqual(document, read["result"]["document"])

            edited = json.loads(json.dumps(read["result"]["document"]))
            edited["editorNote"] = "changed"
            update_payload = dict(create_payload)
            update_payload.update(document=edited, expectedHash=first_hash, operationId="edit-" + kind_id)
            updated = self.call_cli("authoring.write", update_payload)
            self.assertEqual("ok", updated["status"], updated["diagnostics"])
            latest = self.call_cli("authoring.read", {"documentId": document_id})
            self.assertEqual("ok", latest["status"])
            self.assertEqual("original", latest["result"]["document"]["extensionMetadata"]["keep"])
            self.assertEqual("changed", latest["result"]["document"]["editorNote"])

            stale_payload = dict(create_payload)
            stale_payload.update(document={"schemaVersion": 1, "id": document_id, "lost": True},
                                 expectedHash=first_hash, operationId="stale-" + kind_id)
            stale = self.call_cli("authoring.write", stale_payload)
            self.assertEqual("conflict", stale["status"], stale["diagnostics"])
            unchanged = self.call_cli("authoring.read", {"documentId": document_id})
            self.assertEqual(updated["result"]["contentHash"], unchanged["result"]["contentHash"])

    def test_schema_descriptor_exposes_activity_catalog_controls_not_a_local_primitive_list(self):
        result = self.call_cli("schema.describe", {"kindId": "action"})
        self.assertEqual("ok", result["status"], result["diagnostics"])
        descriptor = result["result"]["descriptor"]
        primitive_ids = [item["primitiveId"] for item in descriptor["actionCatalog"]["primitives"]]
        composer_ids = [item["composerId"] for item in descriptor["actionCatalog"]["composers"]]
        self.assertIn("reserve_nearest", primitive_ids)
        self.assertIn("invoke_furniture", primitive_ids)
        self.assertIn("sequence.wait_then_complete.v1", composer_ids)
        reserve = next(item for item in descriptor["actionCatalog"]["primitives"] if item["primitiveId"] == "reserve_nearest")
        self.assertTrue(any(argument["name"] == "useId" and argument["required"] for argument in reserve["arguments"]))

    def test_topic_scenario_reference_is_released_as_a_typed_field(self):
        descriptor = self.call_cli("schema.describe", {"kindId": "topic"})["result"]["descriptor"]
        self.assertFalse(any(item["fieldId"] == "scenarioRef" for item in descriptor["deferredFields"]))
        self.assertNotIn("scenarioRef", descriptor["documentTemplate"])
        scenarios = next(field for section in descriptor["sections"] for field in section["fields"] if field["fieldId"] == "scenarios")
        self.assertEqual("array", scenarios["control"])
        scenario_ref = next(field for field in scenarios["fields"] if field["fieldId"] == "scenarioRef")
        self.assertEqual("scenario", scenario_ref["targetKind"])
        self.assertEqual("reference", scenario_ref["control"])

    def test_producer_descriptors_expose_all_contentref_fields_to_shared_typed_controls(self):
        dialogue = self.call_cli("schema.describe", {"kindId": "dialogue"})["result"]["descriptor"]
        beats = next(field for section in dialogue["sections"] for field in section["fields"] if field["fieldId"] == "beats")
        beat_text = next(field for field in beats["fields"] if field["fieldId"] == "text")
        self.assertEqual("localizedText", beat_text["control"])
        self.assertEqual("text", beat_text["referenceKind"])

        item = self.call_cli("schema.describe", {"kindId": "item"})["result"]["descriptor"]
        item_text = next(field for section in item["sections"] for field in section["fields"] if field["fieldId"] == "displayName")
        self.assertEqual("text", item_text["referenceKind"])
        furniture = self.call_cli("schema.describe", {"kindId": "furniture"})["result"]["descriptor"]
        furniture_text = next(field for section in furniture["sections"] for field in section["fields"] if field["fieldId"] == "displayName")
        self.assertEqual("definition.displayNameKey", furniture_text["mirrorPath"])
        self.assertEqual("text", furniture_text["referenceKind"])

        scenario = self.call_cli("schema.describe", {"kindId": "scenario"})["result"]["descriptor"]
        steps = next(field for section in scenario["sections"] for field in section["fields"] if field["fieldId"] == "steps")
        action_ref = next(field for field in steps["fields"] if field["fieldId"] == "actionRef")
        self.assertEqual("reference", action_ref["control"])
        self.assertEqual("action", action_ref["targetKind"])

        action = self.call_cli("schema.describe", {"kindId": "action"})["result"]["descriptor"]
        action_fields = [field for section in action["sections"] for field in section["fields"]]
        item_ref = next(field for field in action_fields if field["path"] == "data.arguments.itemRef")
        self.assertEqual("reference", item_ref["control"])
        self.assertEqual("item", item_ref["targetKind"])
        self.assertEqual("data.functionId", item_ref["visibleWhenPath"])
        dialogue_argument = next(reference for reference in action["references"] if reference["targetKind"] == "dialogue")
        self.assertIn("data.steps[].arguments[].value", dialogue_argument["paths"])
        self.assertEqual("dialogueId", dialogue_argument["argumentName"])

    def test_importer_reference_list_is_pinned_and_identical_over_cli_and_http(self):
        scenario = {
            "scenarioId": "scenario.reference_fixture", "topicId": "topic.reference_fixture",
            "entryStepId": "step.end", "steps": [{"id": "step.end", "kind": "end", "outcome": "completed"}],
        }
        scenario_id = "source.scenario.reference_fixture"
        selected_pins = []
        saved = self.call_cli("authoring.write", {
            "kind": "scenario", "documentId": scenario_id, "logicalPath": "Workbench/scenario/reference_fixture.json",
            "document": {"workbenchSchemaVersion": 1, "kind": "scenario", "id": scenario_id, "document": scenario},
            "expectedHash": None, "operationId": "create-scenario-ref-fixture",
        })
        self.assertEqual("ok", saved["status"], saved["diagnostics"])
        selected_pins.append({"documentId": scenario_id, "revisionHash": saved["result"]["contentHash"]})
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"targetKind": "scenario", "sourcePins": selected_pins, "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]}
        cli = self.call_cli("references.list", payload, "resolve-ref-fixture")
        browser = self.call_http("references.list", payload, "resolve-ref-fixture")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual(1, len(result["sourcePins"]))
        self.assertEqual(64, len(result["snapshotHash"]))
        scenario_ref = next(item for item in result["references"] if item["targetKind"] == "scenario")
        self.assertEqual("scenario.reference_fixture", scenario_ref["ref"]["id"])
        self.assertEqual(scenario_ref["ref"]["revision"], scenario_ref["ref"]["expectedHash"])
        self.assertEqual(scenario_id, scenario_ref["ownerSources"][0]["documentId"])
        self.assertEqual("scenario_definition", scenario_ref["ownerSources"][0]["sourceKind"])
        self.assertEqual(selected_pins[0]["revisionHash"], scenario_ref["ownerSources"][0]["revisionHash"])
        topic_id = "source.topic.reference_fixture"
        topic = {
            "schemaVersion": 1, "topicId": "topic.reference_fixture", "menuLabel": "Test", "prompt": "Test",
            "anchors": ["test"], "defaultReplayPolicy": "repeatable", "cooldownSeconds": 0,
            "scenarios": [{"scenarioId": "topic.entry.fixture", "priority": 1, "baseWeight": 1,
                           "condition": None, "modifiers": [], "consumptionNodeId": "", "scenarioRef": scenario_ref["ref"]}],
        }
        topic_saved = self.call_cli("authoring.write", {
            "kind": "topic", "documentId": topic_id, "logicalPath": "Workbench/topic/reference_fixture.json",
            "document": {"workbenchSchemaVersion": 1, "kind": "topic", "id": topic_id, "document": topic},
            "expectedHash": None, "operationId": "create-topic-ref-fixture",
        })
        self.assertEqual("ok", topic_saved["status"], topic_saved["diagnostics"])
        store = self.call_cli("authoring.info", {})["result"]["store"]
        topic_payload = {"sourcePins": selected_pins + [{"documentId": topic_id, "revisionHash": topic_saved["result"]["contentHash"]}],
                         "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]}
        topic_import = self.call_cli("references.list", dict(topic_payload, targetKind="topic"))
        self.assertEqual("ok", topic_import["status"], topic_import["diagnostics"])
        compiled_topic = next(item for item in topic_import["result"]["references"]
                              if item["targetKind"] == "topic" and item["ref"]["id"] == "topic.reference_fixture")
        self.assertEqual("topic.entry.fixture", compiled_topic["topicEntries"][0]["entryId"])
        self.assertEqual("scenario.reference_fixture", compiled_topic["topicEntries"][0]["flowRef"]["id"])
        stale = dict(topic_payload, targetKind="scenario")
        stale["sourcePins"] = [{"documentId": scenario_id, "revisionHash": "f" * 64}, topic_payload["sourcePins"][1]]
        rejected = self.call_cli("references.list", stale)
        self.assertEqual("conflict", rejected["status"])
        self.assertEqual("reference_source_revision_changed", rejected["diagnostics"][0]["code"])

    def test_importer_reference_list_fails_closed_for_missing_selected_dependency(self):
        store = self.call_cli("authoring.info", {})["result"]["store"]
        result = self.call_cli("references.list", {
            "targetKind": "scenario",
            "sourcePins": [{"documentId": "missing.source", "revisionHash": "a" * 64}],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
        })
        self.assertEqual("invalid", result["status"])
        self.assertEqual("authoring_snapshot_document_missing", result["diagnostics"][0]["code"])

    def test_mixed_topic_scenario_action_dialogue_text_closure_resolves_without_publication(self):
        dialogue_id = "dialogue.closure_fixture"
        action_id = "action.closure_fixture"
        scenario_id = "scenario.closure_fixture"
        topic_id = "topic.closure_fixture"
        documents = [
            ("dialogue_definition", dialogue_id, {
                "schemaVersion": 1, "id": dialogue_id, "beats": [{
                    "beatId": "beat.hello", "text": {"id": "text.closure_fixture", "translations": [
                        {"language": "en", "text": "Hello there."}
                    ]}, "expression": "", "presentation": {},
                }],
            }),
            ("action_definition", action_id, {
                "schemaVersion": 1, "id": action_id, "capabilityId": "power.set", "implementationKind": "host_effect",
                "data": {"functionId": "power.set", "arguments": {"desiredState": "On"}},
            }),
        ]
        pins = []
        for kind, document_id, document in documents:
            saved = self.call_cli("authoring.write", {
                "kind": kind, "documentId": document_id, "logicalPath": "References/{}.json".format(document_id),
                "document": document, "expectedHash": None, "operationId": "closure-" + uuid.uuid4().hex,
            })
            self.assertEqual("ok", saved["status"], saved["diagnostics"])
            pins.append({"documentId": document_id, "revisionHash": saved["result"]["contentHash"]})
        store = self.call_cli("authoring.info", {})["result"]["store"]
        base = {"sourcePins": pins, "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]}
        dialogue_result = self.call_cli("references.list", dict(base, targetKind="dialogue"))
        action_result = self.call_cli("references.list", dict(base, targetKind="action"))
        self.assertEqual("ok", dialogue_result["status"], dialogue_result["diagnostics"])
        self.assertEqual("ok", action_result["status"], action_result["diagnostics"])
        dialogue_ref = dialogue_result["result"]["references"][0]["ref"]
        action_ref = action_result["result"]["references"][0]["ref"]

        scenario = {
            "scenarioId": scenario_id, "topicId": topic_id, "entryStepId": "step.dialogue",
            "steps": [
                {"id": "step.dialogue", "kind": "dialogue", "dialogueRef": dialogue_ref, "nextStepId": "step.action"},
                {"id": "step.action", "kind": "action", "actionRef": action_ref,
                 "successNextStepId": "step.end", "failureNextStepId": "step.end"},
                {"id": "step.end", "kind": "end", "outcome": "completed"},
            ],
        }
        scenario_saved = self.call_cli("authoring.write", {
            "kind": "scenario_definition", "documentId": scenario_id, "logicalPath": "References/{}.json".format(scenario_id),
            "document": scenario, "expectedHash": None, "operationId": "closure-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", scenario_saved["status"], scenario_saved["diagnostics"])
        pins.append({"documentId": scenario_id, "revisionHash": scenario_saved["result"]["contentHash"]})
        store = self.call_cli("authoring.info", {})["result"]["store"]
        base = {"sourcePins": pins, "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]}
        scenario_result = self.call_cli("references.list", dict(base, targetKind="scenario"))
        self.assertEqual("ok", scenario_result["status"], scenario_result["diagnostics"])
        scenario_ref = scenario_result["result"]["references"][0]["ref"]

        topic = {
            "schemaVersion": 1, "topicId": topic_id, "menuLabel": "Closure fixture", "prompt": "Closure fixture",
            "anchors": ["closure fixture"], "defaultReplayPolicy": "repeatable", "cooldownSeconds": 0,
            "scenarios": [{"scenarioId": "entry.closure_fixture", "priority": 1, "baseWeight": 1,
                           "condition": None, "modifiers": [], "consumptionNodeId": "", "scenarioRef": scenario_ref}],
        }
        topic_saved = self.call_cli("authoring.write", {
            "kind": "topic_definition", "documentId": topic_id, "logicalPath": "References/{}.json".format(topic_id),
            "document": topic, "expectedHash": None, "operationId": "closure-" + uuid.uuid4().hex,
        })
        self.assertEqual("ok", topic_saved["status"], topic_saved["diagnostics"])
        pins.append({"documentId": topic_id, "revisionHash": topic_saved["result"]["contentHash"]})
        store = self.call_cli("authoring.info", {})["result"]["store"]
        base = {"sourcePins": pins, "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]}

        topic_result = self.call_cli("references.list", dict(base, targetKind="topic"))
        text_result = self.call_cli("references.list", dict(base, targetKind="text"))
        self.assertEqual("ok", topic_result["status"], topic_result["diagnostics"])
        self.assertEqual("ok", text_result["status"], text_result["diagnostics"])
        compiled_topic = topic_result["result"]["references"][0]
        self.assertEqual("entry.closure_fixture", compiled_topic["topicEntries"][0]["entryId"])
        self.assertEqual(scenario_id, compiled_topic["topicEntries"][0]["flowRef"]["id"])
        self.assertTrue(any(item["ref"]["id"] == "text.closure_fixture" for item in text_result["result"]["references"]))
        self.assertFalse(any(item.get("published", False) for item in topic_result["result"]["references"]))

    def test_all_six_kinds_remain_in_the_preview_acceptance_gate(self):
        observed = []
        for kind_id in KIND_MAP:
            descriptor = self.call_cli("schema.describe", {"kindId": kind_id})["result"]["descriptor"]
            observed.append(kind_id)
            self.assertTrue(descriptor["capabilities"]["preview"],
                            "{} is omitted from the six-kind preview acceptance gate".format(kind_id))
        self.assertEqual(["dialogue", "scenario", "topic", "furniture", "item", "action"], observed)

    def test_item_preview_projects_the_production_definition_without_touching_inventory(self):
        document = {
            "schemaVersion": 1,
            "id": "item.preview_water_glass",
            "displayName": {"id": "text.preview_water_glass", "translations": [
                {"language": "en", "text": "Water glass"},
                {"language": "ja", "text": "水のグラス"},
            ]},
            "stackLimit": 4,
        }
        payload = {"kindId": "item", "document": document, "fixtureId": "sit-together/no-table"}
        cli = self.call_cli("preview.run", payload, "preview-item-definition")
        browser = self.call_http("preview.run", payload, "preview-item-definition")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        self.assertEqual("item", cli["result"]["kindId"])
        self.assertTrue(cli["result"]["fixtureOnly"])
        self.assertEqual("item.preview_water_glass", cli["result"]["item"]["id"])
        self.assertEqual(4, cli["result"]["item"]["stackLimit"])
        self.assertEqual("Water glass", cli["result"]["item"]["displayName"])
        self.assertEqual([], cli["result"]["item"]["fixtureQuantityChanges"])

        saved = self.call_cli("authoring.write", {
            "kind": "item_definition", "documentId": document["id"],
            "logicalPath": "Preview/Items/{}.json".format(document["id"]),
            "document": document, "expectedHash": None, "operationId": "save-preview-item",
        })
        self.assertEqual("ok", saved["status"], saved["diagnostics"])
        store = self.call_cli("authoring.info", {})["result"]["store"]
        saved_payload = {
            "kindId": "item", "documentId": document["id"], "revisionHash": saved["result"]["contentHash"],
            "sourcePins": [{"documentId": document["id"], "revisionHash": saved["result"]["contentHash"]}],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "sit-together/no-table",
        }
        cli_saved = self.call_cli("preview.run", saved_payload, "preview-saved-item")
        http_saved = self.call_http("preview.run", saved_payload, "preview-saved-item")
        self.assertEqual(cli_saved, http_saved)
        self.assertEqual("ok", cli_saved["status"], cli_saved["diagnostics"])
        self.assertFalse(cli_saved["result"]["sourceSnapshot"]["rootIsSynthetic"])
        self.assertEqual(saved["result"]["contentHash"], cli_saved["result"]["sourceSnapshot"]["rootRevision"])

    def test_dialogue_preview_uses_shared_linear_playback_and_localized_text(self):
        document = {
            "schemaVersion": 1,
            "id": "dialogue.preview_two_beats",
            "beats": [
                {"beatId": "beat.open", "text": {"id": "text.preview_open", "translations": [
                    {"language": "en", "text": "Ready?"}, {"language": "ja", "text": "準備はいい？"}],}, "expression": "", "presentation": {}},
                {"beatId": "beat.done", "text": {"id": "text.preview_done", "translations": [
                    {"language": "en", "text": "Let's begin."}, {"language": "ja", "text": "始めよう。"}],}, "expression": "", "presentation": {}},
            ],
        }
        payload = {"kindId": "dialogue", "document": document, "fixtureId": "sit-together/no-table",
            "displayLanguage": "en", "speechLanguage": "ja", "events": [{"action": "continue"}]}
        cli = self.call_cli("preview.run", payload, "preview-dialogue-playback")
        browser = self.call_http("preview.run", payload, "preview-dialogue-playback")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("dialogue", result["kindId"])
        self.assertEqual("beat.done", result["currentBeat"]["id"])
        self.assertEqual("始めよう。", result["currentBeat"]["text"])
        self.assertEqual("Let's begin.", result["currentBeat"]["displayText"])
        self.assertEqual(0, result["providerCalls"])
        self.assertEqual(0, result["ttsCalls"])

    def test_furniture_preview_uses_the_production_catalogue_and_exact_synthetic_target(self):
        pins, store = self.save_preview_furniture_catalogue()
        root_pin = next(item for item in pins if item["documentId"] == "furniture.tv.basic")
        payload = {
            "kindId": "furniture", "documentId": root_pin["documentId"], "revisionHash": root_pin["revisionHash"],
            "sourcePins": pins, "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/furniture",
        }
        cli = self.call_cli("preview.run", payload, "preview-furniture-catalogue")
        browser = self.call_http("preview.run", payload, "preview-furniture-catalogue")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("furniture", result["kindId"])
        self.assertEqual("furniture.tv.basic", result["definition"]["id"])
        self.assertEqual("fixture.tv.main", result["targets"][0]["instanceId"])
        self.assertEqual("power.set", result["targets"][0]["functions"][0]["functionId"])
        self.assertEqual("available", result["targets"][0]["functions"][0]["status"])
        self.assertEqual("Off", result["targets"][0]["currentState"])
        self.assertEqual([], result["functionResults"], "inspection alone must not invoke or mutate a Furniture function")

    def test_furniture_settings_are_edited_and_saved_within_the_existing_furniture_kind(self):
        describe_payload = {"kindId": "furniture", "sourceKind": "furniture_catalogue_settings"}
        cli_descriptor = self.call_cli("schema.describe", describe_payload, "furniture-settings-describe")
        http_descriptor = self.call_http("schema.describe", describe_payload, "furniture-settings-describe")
        self.assertEqual(cli_descriptor, http_descriptor)
        descriptor = cli_descriptor["result"]["descriptor"]
        self.assertEqual("furniture", descriptor["kindId"])
        self.assertEqual(["furniture_definition", "furniture_catalogue_settings"], descriptor["sourceKinds"])
        contract = next(item for item in descriptor["sourceContracts"] if item["kind"] == "furniture_catalogue_settings")
        self.assertEqual("catalogueVersion", contract["identityField"])
        self.assertEqual("furniture.catalogue", contract["fixedDocumentId"])
        self.assertEqual("Furniture/catalogue-settings.json", contract["fixedLogicalPath"])
        self.assertFalse(descriptor["capabilities"]["preview"], "settings are a source dependency, not a Furniture instance preview root")
        self.assertFalse(descriptor["graph"]["enabled"])
        self.assertTrue(any(section["id"] == "relationships" for section in descriptor["sections"]))
        self.assertTrue(any(section["id"] == "capabilities" for section in descriptor["sections"]))

        settings = descriptor["documentTemplate"]
        settings["catalogueVersion"] = "workbench-test-v1"
        validation_payload = {"kindId": "furniture", "sourceKind": "furniture_catalogue_settings", "document": settings}
        validation = self.call_cli("content.validate", validation_payload, "furniture-settings-validate")
        self.assertEqual(validation, self.call_http("content.validate", validation_payload, "furniture-settings-validate"))
        self.assertEqual("ok", validation["status"], validation["diagnostics"])
        self.assertTrue(validation["result"]["valid"], validation["result"].get("diagnostics", []))
        first_payload = {
            "kind": "furniture_catalogue_settings", "documentId": contract["fixedDocumentId"],
            "logicalPath": contract["fixedLogicalPath"], "document": settings,
            "expectedHash": None, "operationId": "save-workbench-furniture-settings"}
        first = self.call_cli("authoring.write", first_payload, "furniture-settings-create")
        self.assertEqual(first, self.call_http("authoring.write", first_payload, "furniture-settings-create"))
        self.assertEqual("ok", first["status"], first["diagnostics"])
        saved = self.call_cli("authoring.read", {"documentId": "furniture.catalogue"})
        http_saved = self.call_http("authoring.read", {"documentId": "furniture.catalogue"}, "furniture-settings-read")
        self.assertEqual(saved["result"], http_saved["result"])
        self.assertEqual(settings, saved["result"]["document"])
        self.assertEqual(first["result"]["contentHash"], saved["result"]["contentHash"])

        edited = dict(settings, catalogueVersion="workbench-test-v2")
        second = self.call_cli("authoring.write", {
            "kind": "furniture_catalogue_settings", "documentId": contract["fixedDocumentId"],
            "logicalPath": contract["fixedLogicalPath"], "document": edited,
            "expectedHash": first["result"]["contentHash"], "operationId": "edit-workbench-furniture-settings"})
        self.assertEqual("ok", second["status"], second["diagnostics"])
        stale = self.call_cli("authoring.write", {
            "kind": "furniture_catalogue_settings", "documentId": contract["fixedDocumentId"],
            "logicalPath": contract["fixedLogicalPath"], "document": settings,
            "expectedHash": first["result"]["contentHash"], "operationId": "stale-workbench-furniture-settings"})
        self.assertEqual("conflict", stale["status"])
        reopened = self.call_cli("authoring.read", {"documentId": "furniture.catalogue"})
        self.assertEqual(edited, reopened["result"]["document"], "a stale write must preserve the latest settings revision")

    def test_topic_tv_proof_runs_exact_power_action_without_return_or_other_furniture_mutation(self):
        furniture_pins, _ = self.save_preview_furniture_catalogue()
        bark_pins = [
            self.save_preview_bark("dialogue.tv.approach", "Let's check the TV."),
            self.save_preview_bark("dialogue.tv.arrival", "Okay, this is the TV."),
        ]
        b = self.sequence_binding
        l = self.sequence_literal
        s = self.sequence_step
        action_steps = [
            s("step.reserve", "reserve_nearest", [l("useId", "text", "tv.power"), l("functionId", "enum", "power.set")],
              [{"resultName": "targetToken", "bindingId": "target"}], "step.bark_start"),
            s("step.bark_start", "bark", [l("dialogueId", "text", "dialogue.tv.approach")], success="step.move"),
            s("step.move", "move_to", [b("target", "target")], success="step.face"),
            s("step.face", "face", [b("target", "target")], success="step.bark_arrival"),
            s("step.bark_arrival", "bark", [l("dialogueId", "text", "dialogue.tv.arrival")], success="step.power"),
            s("step.power", "invoke_furniture", [b("target", "target"), l("functionId", "enum", "power.set"),
              l("desiredState", "enum", "On")], success="step.wait"),
            s("step.wait", "wait", [l("durationSeconds", "double", 2.0)], success="step.complete"),
            s("step.complete", "complete"),
        ]
        action_pin = self.save_sequence_action("action.preview.tv_on", action_steps)
        dependency_pins = furniture_pins + bark_pins + [action_pin]
        action_ref = self.resolve_preview_reference("action", dependency_pins)
        scenario_pin, scenario_ref = self.save_preview_scenario("scenario.preview.tv_on", action_ref, dependency_pins)
        topic = {"schemaVersion": 1, "topicId": "topic.preview.tv_on", "menuLabel": "Turn on TV",
            "prompt": "Can you turn on the TV?", "anchors": ["turn on the TV"],
            "defaultReplayPolicy": "repeatable", "cooldownSeconds": 0,
            "scenarios": [{"scenarioId": "entry.tv_on", "priority": 1, "baseWeight": 1,
                "condition": None, "modifiers": [], "consumptionNodeId": "", "scenarioRef": scenario_ref}]}
        source_pins = dependency_pins + [scenario_pin]
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "topic", "document": topic, "sourcePins": source_pins,
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/proof", "instanceId": "preview.tv_on", "source": "authored", "draw": 0.2,
            "nowUtc": "2026-10-24T08:00:00Z", "displayLanguage": "en", "speechLanguage": "en", "events": []}
        cli = self.call_cli("preview.run", payload, "preview-tv-on-proof")
        browser = self.call_http("preview.run", payload, "preview-tv-on-proof")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("entry.tv_on", result["selectedScenarioId"])
        run = result["startActivityRuns"][0]
        self.assertEqual("completed", run["sequenceStatus"], run)
        operations = [entry["operation"] for entry in run["syntheticTrace"]]
        self.assertEqual(["reserve_nearest", "bark", "move_to", "face", "bark", "power.set", "wait", "complete"], operations)
        self.assertNotIn("move_near_player", operations, "the TV recipe must not force a return")
        self.assertEqual("Off", next(item["powerState"] for item in result["fixtureFurniture"] if item["instanceId"] == "fixture.tv.main"))
        self.assertEqual("On", next(item["powerState"] for item in result["fixtureFurniture"] if item["instanceId"] == "fixture.tv.second"))
        power_change = next(item for item in run["syntheticTrace"] if item["operation"] == "power.set")
        self.assertEqual("fixture.tv.second", power_change["instanceId"])
        self.assertTrue(all(not item["reservationOwnerId"] for item in result["fixtureFurniture"]))
        self.assertEqual("Off", next(item["powerState"] for item in result["fixtureFurniture"] if item["instanceId"] == "fixture.window.main"))
        self.assertEqual([], result["syntheticHostEffectChanges"])

    def test_same_sequence_runtime_targets_a_non_tv_definition_without_a_furniture_specific_branch(self):
        furniture_pins, _ = self.save_preview_furniture_catalogue()
        s = self.sequence_step
        l = self.sequence_literal
        b = self.sequence_binding
        action_steps = [
            s("step.reserve", "reserve_nearest", [l("useId", "text", "window.power"), l("functionId", "enum", "power.set")],
              [{"resultName": "targetToken", "bindingId": "target"}], "step.move"),
            s("step.move", "move_to", [b("target", "target")], success="step.invoke"),
            s("step.invoke", "invoke_furniture", [b("target", "target"), l("functionId", "enum", "power.set"),
              l("desiredState", "enum", "On")], success="step.complete"),
            s("step.complete", "complete"),
        ]
        action_pin = self.save_sequence_action("action.preview.window_on", action_steps)
        pins = furniture_pins + [action_pin]
        action_ref = self.resolve_preview_reference("action", pins)
        scenario = {"scenarioId": "scenario.preview.window_on", "topicId": "topic.preview.window_on",
            "entryStepId": "step.action", "steps": [
                {"id": "step.action", "kind": "action", "actionRef": action_ref,
                 "successNextStepId": "step.end", "failureNextStepId": "step.failed"},
                {"id": "step.end", "kind": "end", "outcome": "completed"},
                {"id": "step.failed", "kind": "end", "outcome": "failed"},
            ]}
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "scenario", "document": scenario, "sourcePins": pins,
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/proof", "events": []}
        cli = self.call_cli("preview.run", payload, "preview-non-tv-function")
        browser = self.call_http("preview.run", payload, "preview-non-tv-function")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("completed", result["flowOutcome"], result)
        self.assertEqual("completed", result["startActivityRuns"][0]["sequenceStatus"])
        self.assertEqual("fixture.window.main", next(item["instanceId"] for item in result["startActivityRuns"][0]["syntheticTrace"]
            if item["operation"] == "power.set"))
        self.assertEqual("On", next(item["powerState"] for item in result["fixtureFurniture"] if item["instanceId"] == "fixture.window.main"))
        self.assertEqual("Off", next(item["powerState"] for item in result["fixtureFurniture"] if item["instanceId"] == "fixture.tv.second"))

    def test_locate_sequence_arrives_then_presents_the_exact_bark_without_furniture_effects(self):
        furniture_pins, _ = self.save_preview_furniture_catalogue()
        bark_pin = self.save_preview_bark("dialogue.locate.arrival", "Hey, it's here.")
        s = self.sequence_step
        l = self.sequence_literal
        b = self.sequence_binding
        action_pin = self.save_sequence_action("action.preview.locate", [
            s("step.reserve", "reserve_nearest", [l("useId", "text", "tv.power"), l("functionId", "enum", "power.set")],
              [{"resultName": "targetToken", "bindingId": "target"}], "step.move"),
            s("step.move", "move_to", [b("target", "target")], success="step.bark"),
            s("step.bark", "bark", [l("dialogueId", "text", "dialogue.locate.arrival")], success="step.complete"),
            s("step.complete", "complete"),
        ])
        dependencies = furniture_pins + [bark_pin, action_pin]
        action_ref = self.resolve_preview_reference("action", dependencies)
        scenario_pin, scenario_ref = self.save_preview_scenario("scenario.preview.locate", action_ref, dependencies)
        topic = {"schemaVersion": 1, "topicId": "topic.preview.locate", "menuLabel": "Locate",
            "prompt": "Where is the TV?", "anchors": ["where is the TV"], "defaultReplayPolicy": "repeatable", "cooldownSeconds": 0,
            "scenarios": [{"scenarioId": "entry.locate", "priority": 1, "baseWeight": 1,
                "condition": None, "modifiers": [], "consumptionNodeId": "", "scenarioRef": scenario_ref}]}
        pins = dependencies + [scenario_pin]
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "topic", "document": topic, "sourcePins": pins,
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/proof", "instanceId": "preview.locate", "source": "authored", "draw": 0,
            "nowUtc": "2026-10-24T08:00:00Z", "events": []}
        cli = self.call_cli("preview.run", payload, "preview-locate-proof")
        browser = self.call_http("preview.run", payload, "preview-locate-proof")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        trace = result["startActivityRuns"][0]["syntheticTrace"]
        self.assertEqual(["reserve_nearest", "move_to", "bark", "complete"], [item["operation"] for item in trace])
        self.assertEqual("fixture.tv.second", trace[0]["instanceId"], "Locate must reserve the available TV, not return to the player")
        self.assertEqual("fixture.tv.second", trace[1]["instanceId"])
        self.assertEqual("dialogue.locate.arrival", trace[2]["dialogueId"])
        self.assertEqual("Hey, it's here.", trace[2]["beats"][0]["displayText"])
        self.assertFalse(any(item["operation"] == "power.set" for item in trace))
        self.assertFalse(any(item["operation"] == "move_near_player" for item in trace))
        self.assertTrue(all(not item["reservationOwnerId"] for item in result["fixtureFurniture"]))
        self.assertEqual("Off", next(item["powerState"] for item in result["fixtureFurniture"] if item["instanceId"] == "fixture.tv.second"))
        self.assertEqual([], result["syntheticHostEffectChanges"])

    def test_topic_fetch_returns_before_cheerful_vn_ack_then_grants_water_once(self):
        furniture_pins, _ = self.save_preview_furniture_catalogue()
        s = self.sequence_step
        l = self.sequence_literal
        b = self.sequence_binding
        action_steps = [
            s("step.reserve", "reserve_nearest", [l("useId", "text", "dispenser.collect"),
              l("functionId", "enum", "dispenser.collect_mock")], [{"resultName": "targetToken", "bindingId": "target"}], "step.move"),
            s("step.move", "move_to", [b("target", "target")], success="step.collect"),
            s("step.collect", "invoke_furniture", [b("target", "target"), l("functionId", "enum", "dispenser.collect_mock")], success="step.return"),
            s("step.return", "move_near_player", success="step.complete"),
            s("step.complete", "complete"),
        ]
        sequence_pin = self.save_sequence_action("action.preview.fetch", action_steps)
        cheerful_pin = self.save_preview_source("dialogue_definition", "dialogue.fetch.cheerful",
            "Dialogue/dialogue.fetch.cheerful.json", {"schemaVersion": 1, "id": "dialogue.fetch.cheerful", "beats": [{
                "beatId": "beat.cheerful", "text": {"id": "text.fetch.cheerful", "translations": [
                    {"language": "en", "text": "Here, I brought you water."},
                    {"language": "ja", "text": "はい、お水を持ってきました。"}]},
                "expression": "happy", "presentation": {},
            }]})
        item_pin = self.save_preview_source("item_definition", "item.water_glass", "Item/item.water_glass.json", {
            "schemaVersion": 1, "id": "item.water_glass",
            "displayName": {"id": "text.water_glass", "translations": [{"language": "en", "text": "Water glass"}]},
            "stackLimit": 5,
        })
        grant_item_ref = self.resolve_preview_reference("item", [item_pin])
        grant_pin = self.save_preview_source("action_definition", "action.preview.grant_water",
            "Action/action.preview.grant_water.json", {"schemaVersion": 1, "id": "action.preview.grant_water",
                "capabilityId": "player.item.grant", "implementationKind": "host_effect",
                "data": {"functionId": "player.item.grant", "arguments": {"itemRef": grant_item_ref, "quantity": 1}}})
        sequence_ref = self.resolve_preview_reference("action", furniture_pins + [sequence_pin])
        scenario_id = "scenario.preview.fetch"
        scenario_pin = self.save_preview_source("scenario_definition", scenario_id,
            "Scenario/{}.json".format(scenario_id), {"scenarioId": scenario_id, "topicId": "topic.preview.fetch",
                "entryStepId": "step.fetch", "steps": [
                    {"id": "step.fetch", "kind": "action", "actionRef": sequence_ref,
                     "successNextStepId": "step.cheerful", "failureNextStepId": "step.failed"},
                    {"id": "step.cheerful", "kind": "dialogue", "dialogueRef": self.resolve_preview_reference("dialogue", [cheerful_pin]),
                     "nextStepId": "step.grant"},
                    {"id": "step.grant", "kind": "host_effect", "actionRef": self.resolve_preview_reference("action", [item_pin, grant_pin]),
                     "successNextStepId": "step.end", "failureNextStepId": "step.failed",
                     "data": {"schemaVersion": 1, "requiresCompletedStepId": "step.cheerful"}},
                    {"id": "step.end", "kind": "end", "outcome": "completed"},
                    {"id": "step.failed", "kind": "end", "outcome": "failed"},
                ]})
        scenario_ref = self.resolve_preview_reference("scenario", furniture_pins + [sequence_pin, cheerful_pin, item_pin, grant_pin, scenario_pin])
        topic = {"schemaVersion": 1, "topicId": "topic.preview.fetch", "menuLabel": "Fetch water", "prompt": "Let's get water",
            "anchors": ["fetch water"], "defaultReplayPolicy": "once", "cooldownSeconds": 0,
            "scenarios": [{"scenarioId": "entry.fetch", "priority": 1, "baseWeight": 1,
                "condition": None, "modifiers": [], "consumptionNodeId": "step.cheerful", "scenarioRef": scenario_ref}]}
        pins = furniture_pins + [sequence_pin, cheerful_pin, item_pin, grant_pin, scenario_pin]
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "topic", "document": topic, "sourcePins": pins,
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/proof", "instanceId": "preview.fetch", "source": "authored", "draw": 0.1,
            "nowUtc": "2026-10-24T08:00:00Z", "displayLanguage": "en", "speechLanguage": "en",
            "events": [{"action": "continue", "atUtc": "2026-10-24T08:00:01Z"},
                {"action": "reselect", "atUtc": "2026-10-24T08:00:02Z"}]}
        cli = self.call_cli("preview.run", payload, "preview-fetch-water-proof")
        browser = self.call_http("preview.run", payload, "preview-fetch-water-proof")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        run = result["startActivityRuns"][0]
        operations = [item["operation"] for item in run["syntheticTrace"]]
        self.assertEqual(["reserve_nearest", "move_to", "dispenser.collect_mock", "move_near_player", "complete"], operations)
        collect_index = operations.index("dispenser.collect_mock")
        self.assertLess(collect_index, operations.index("move_near_player"), "the mock collection finishes before the explicit return")
        self.assertTrue(run["syntheticTrace"][collect_index]["itemOrStatMutation"] is False)
        self.assertEqual("happy", result["events"][0]["acknowledgedDialogue"]["expression"])
        self.assertEqual("Here, I brought you water.", result["events"][0]["acknowledgedDialogue"]["displayText"])
        self.assertEqual("completed", result["events"][0]["flowOutcome"])
        self.assertEqual(1, next(item["quantity"] for item in result["fixtureItemQuantities"] if item["itemId"] == "item.water_glass"))
        self.assertEqual(1, len(result["syntheticHostEffectChanges"]))
        self.assertTrue(result["syntheticHostEffectChanges"][0]["syntheticOnly"])
        self.assertTrue(result["isConsumed"])
        self.assertEqual("", result["events"][1]["selectedScenarioId"])
        self.assertEqual("Consumed", result["events"][1]["candidates"][0]["reason"])

        cancelled_payload = dict(payload)
        cancelled_payload["instanceId"] = "preview.fetch.cancelled"
        cancelled_payload["events"] = [{"action": "cancel", "atUtc": "2026-10-24T08:00:01Z"},
            {"action": "reselect", "atUtc": "2026-10-24T08:00:02Z"}]
        cancelled = self.call_cli("preview.run", cancelled_payload, "preview-fetch-cancelled")
        self.assertEqual("ok", cancelled["status"], cancelled["diagnostics"])
        self.assertEqual([], cancelled["result"]["syntheticHostEffectChanges"])
        self.assertEqual(0, next(item["quantity"] for item in cancelled["result"]["fixtureItemQuantities"] if item["itemId"] == "item.water_glass"))
        self.assertFalse(cancelled["result"]["isConsumed"], "cancel before the authored VN checkpoint must not consume or grant")
        self.assertEqual("entry.fetch", cancelled["result"]["events"][1]["selectedScenarioId"],
            "a cancelled, unconsumed occurrence remains selectable in the detached history")

        empty_payload = dict(payload)
        empty_payload["instanceId"] = "preview.fetch.no_resources"
        empty_payload["fixtureId"] = "workbench/no-resources"
        empty_payload["events"] = []
        no_resources = self.call_cli("preview.run", empty_payload, "preview-fetch-no-resources")
        self.assertEqual("ok", no_resources["status"], no_resources["diagnostics"])
        self.assertEqual("failed", no_resources["result"]["startActivityRuns"][0]["sequenceStatus"])
        self.assertEqual("unavailable", no_resources["result"]["startActivityRuns"][0]["syntheticTrace"][0]["status"])
        self.assertEqual([], no_resources["result"]["syntheticHostEffectChanges"])
        self.assertEqual(0, next(item["quantity"] for item in no_resources["result"]["fixtureItemQuantities"] if item["itemId"] == "item.water_glass"))
        self.assertTrue(all(not item["reservationOwnerId"] for item in no_resources["result"]["fixtureFurniture"]))

    def test_action_preview_uses_the_production_sequence_adapter_and_executor_in_safe_fixture(self):
        action = {
            "schemaVersion": 1, "id": "action.preview.wait", "capabilityId": "scenario.activity_sequence",
            "implementationKind": "activity_sequence", "data": {
                "composerId": "sequence.wait_then_complete.v1",
                "composerArguments": [{"name": "durationSeconds", "value": {"type": "double", "literal": 0.25}}],
            },
        }
        payload = {"kindId": "action", "document": action, "fixtureId": "workbench/action-basic"}
        cli = self.call_cli("preview.run", payload, "preview-action-sequence")
        browser = self.call_http("preview.run", payload, "preview-action-sequence")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("action", result["kindId"])
        self.assertEqual("synthetic_completed", result["action"]["executionStatus"])
        self.assertEqual("completed", result["action"]["sequenceStatus"])
        self.assertEqual(["wait", "complete"], [step["stepId"] for step in result["action"]["sequence"]["steps"]])
        self.assertEqual(0, result["providerCalls"])

    def test_action_host_effect_preview_reports_inspection_without_duplicate_status_failure(self):
        item = {"schemaVersion": 1, "id": "item.preview_grant", "displayName": {
            "id": "text.preview_grant", "translations": [{"language": "en", "text": "Glass"}]}, "stackLimit": 3}
        item_pin = self.save_preview_source("item_definition", item["id"], "Item/{}.json".format(item["id"]), item)
        item_ref = self.resolve_preview_reference("item", [item_pin])
        action = {"schemaVersion": 1, "id": "action.preview_grant", "capabilityId": "player.item.grant",
            "implementationKind": "host_effect", "data": {"functionId": "player.item.grant",
                "arguments": {"itemRef": item_ref, "quantity": 1}}}
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "action", "document": action, "sourcePins": [item_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/action-basic"}
        cli = self.call_cli("preview.run", payload, "preview-action-host-effect")
        browser = self.call_http("preview.run", payload, "preview-action-host-effect")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        self.assertEqual("not_executed_host_effect", cli["result"]["action"]["executionStatus"])
        self.assertEqual("player.item.grant", cli["result"]["action"]["functionId"])
        self.assertEqual([], cli["result"]["action"].get("syntheticTrace", []), "host-effect preview must remain inspection-only")

    def test_topic_flow_preview_uses_selector_history_and_scenario_session(self):
        dialogue_id, scenario_id, topic_id = "dialogue.topic_fixture", "scenario.topic_fixture", "topic.topic_fixture"
        dialogue = {"schemaVersion": 1, "id": dialogue_id, "beats": [{
            "beatId": "beat.open", "text": {"id": "text.topic_fixture", "translations": [{"language": "en", "text": "A quiet evening."}]},
            "expression": "", "presentation": {},
        }]}
        dialogue_pin = self.save_preview_source("dialogue_definition", dialogue_id,
            "Dialogue/{}.json".format(dialogue_id), dialogue)
        store = self.call_cli("authoring.info", {})["result"]["store"]
        references = self.call_cli("references.list", {
            "targetKind": "dialogue", "sourcePins": [dialogue_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
        })
        self.assertEqual("ok", references["status"], references["diagnostics"])
        dialogue_ref = next(item["ref"] for item in references["result"]["references"] if item["targetKind"] == "dialogue")
        scenario = {"scenarioId": scenario_id, "topicId": topic_id, "entryStepId": "step.open", "steps": [
            {"id": "step.open", "kind": "dialogue", "dialogueRef": dialogue_ref, "nextStepId": "step.followup"},
            {"id": "step.followup", "kind": "dialogue", "dialogueRef": dialogue_ref, "nextStepId": "step.end"},
            {"id": "step.end", "kind": "end", "outcome": "completed"},
        ]}
        scenario_pin = self.save_preview_source("scenario_definition", scenario_id,
            "Scenario/{}.json".format(scenario_id), scenario)
        store = self.call_cli("authoring.info", {})["result"]["store"]
        references = self.call_cli("references.list", {
            "targetKind": "scenario", "sourcePins": [dialogue_pin, scenario_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
        })
        self.assertEqual("ok", references["status"], references["diagnostics"])
        scenario_ref = next(item["ref"] for item in references["result"]["references"] if item["targetKind"] == "scenario")
        topic = {"schemaVersion": 1, "topicId": topic_id, "menuLabel": "Quiet", "prompt": "Quiet",
            "anchors": ["quiet evening"], "defaultReplayPolicy": "repeatable", "cooldownSeconds": 60,
            "scenarios": [{"scenarioId": "entry.topic_fixture", "priority": 2, "baseWeight": 1,
                "condition": None, "modifiers": [], "consumptionNodeId": "", "scenarioRef": scenario_ref}]}
        topic_pin = self.save_preview_source("topic_definition", topic_id,
            "Topic/{}.json".format(topic_id), topic)
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "topic", "documentId": topic_pin["documentId"], "revisionHash": topic_pin["revisionHash"],
            "sourcePins": [dialogue_pin, scenario_pin, topic_pin], "expectedStoreId": store["storeId"],
            "expectedStoreRevision": store["storeRevision"], "fixtureId": "sit-together/no-table",
            "instanceId": "preview.topic_fixture", "source": "authored", "draw": 0.25,
            "nowUtc": "2026-10-24T08:00:00Z", "displayLanguage": "en", "speechLanguage": "en",
            "events": [
                {"action": "continue", "atUtc": "2026-10-24T08:00:01Z"},
                {"action": "continue", "atUtc": "2026-10-24T08:00:02Z"},
                {"action": "reselect", "atUtc": "2026-10-24T08:00:03Z"},
                {"action": "new_occurrence", "instanceId": "preview.topic_fixture.next", "atUtc": "2026-10-24T08:00:04Z"},
            ]}
        cli = self.call_cli("preview.run", payload, "preview-topic-flow")
        browser = self.call_http("preview.run", payload, "preview-topic-flow")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("direct synthetic family; not semantic routing", result["routingMode"])
        self.assertEqual("entry.topic_fixture", result["selectedScenarioId"])
        self.assertEqual("A quiet evening.", result["events"][0]["acknowledgedDialogue"]["displayText"])
        self.assertEqual("applied", result["events"][0]["historyStatus"].lower())
        self.assertEqual("ended", result["events"][1]["flowStatus"])
        self.assertEqual("completed", result["events"][1]["flowOutcome"])
        self.assertFalse(result["isConsumed"])
        self.assertEqual(1, result["scenarioCompletionCount"])
        self.assertEqual("", result["events"][2]["selectedScenarioId"])
        self.assertEqual("CoolingDown", result["events"][2]["candidates"][0]["reason"])
        self.assertEqual("entry.topic_fixture", result["events"][3]["selectedScenarioId"])
        self.assertFalse(result["events"][3]["isConsumed"])
        self.assertEqual(0, result["providerCalls"])

    def test_scenario_flow_preview_uses_the_pinned_release_and_shared_session(self):
        action_pin = self.save_preview_source("action_definition", "action.preview.sequence",
            "Action/action.preview.sequence.json", {
                "schemaVersion": 1, "id": "action.preview.sequence", "capabilityId": "scenario.activity_sequence",
                "implementationKind": "activity_sequence", "data": {
                    "composerId": "sequence.wait_then_complete.v1",
                    "composerArguments": [{"name": "durationSeconds", "value": {"type": "double", "literal": 0.25}}],
                },
            })
        store = self.call_cli("authoring.info", {})["result"]["store"]
        action_refs = self.call_cli("references.list", {"targetKind": "action", "sourcePins": [action_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]})
        self.assertEqual("ok", action_refs["status"], action_refs["diagnostics"])
        action_ref = next(item["ref"] for item in action_refs["result"]["references"] if item["targetKind"] == "action")
        scenario = {"scenarioId": "scenario.preview.inline", "topicId": "topic.preview.inline",
            "entryStepId": "step.open", "steps": [
                {"id": "step.open", "kind": "dialogue", "beats": [{"id": "beat.open", "speaker": "alterra",
                    "text": "静かな夜ですね。", "displayText": "Hello from the shared Scenario session.",
                    "language": "japanese", "expression": ""}], "nextStepId": "step.action"},
                {"id": "step.action", "kind": "action", "actionRef": action_ref,
                    "successNextStepId": "step.end", "failureNextStepId": "step.failed"},
                {"id": "step.end", "kind": "end", "outcome": "completed"},
                {"id": "step.failed", "kind": "end", "outcome": "failed"},
            ]}
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "scenario", "document": scenario, "fixtureId": "workbench/action-basic",
            "sourcePins": [action_pin], "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "events": [{"action": "continue"}]}
        cli = self.call_cli("preview.run", payload, "preview-scenario-session")
        browser = self.call_http("preview.run", payload, "preview-scenario-session")
        self.assertEqual(cli, browser, "CLI/HTTP parity mismatch: {} != {}".format(cli, browser))
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("scenario", result["kindId"])
        self.assertEqual("Hello from the shared Scenario session.", result["events"][0]["acknowledgedDialogue"]["displayText"])
        self.assertEqual("completed", result["flowOutcome"], result)
        self.assertEqual("completed", result["events"][0]["activityRuns"][0]["sequenceStatus"])
        self.assertEqual(["wait", "complete"], result["events"][0]["activityRuns"][0]["executedPrimitives"])
        self.assertEqual(0, result["providerCalls"])

    def test_scenario_host_effect_uses_exact_item_reference_and_only_mutates_fixture_copy(self):
        dialogue_id, item_id = "dialogue.grant_fixture", "item.water_glass"
        scenario_id, action_id = "scenario.grant_fixture", "action.grant_fixture"
        dialogue_pin = self.save_preview_source("dialogue_definition", dialogue_id,
            "Dialogue/{}.json".format(dialogue_id), {"schemaVersion": 1, "id": dialogue_id, "beats": [{
                "beatId": "beat.ready", "text": {"id": "text.grant_fixture", "translations": [
                    {"language": "en", "text": "Here is your water."}]}, "expression": "", "presentation": {},
            }]})
        item_pin = self.save_preview_source("item_definition", item_id, "Item/{}.json".format(item_id), {
            "schemaVersion": 1, "id": item_id,
            "displayName": {"id": "text.water_glass", "translations": [{"language": "en", "text": "Water glass"}]},
            "stackLimit": 5,
        })
        store = self.call_cli("authoring.info", {})["result"]["store"]
        item_refs = self.call_cli("references.list", {"targetKind": "item", "sourcePins": [item_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]})
        self.assertEqual("ok", item_refs["status"], item_refs["diagnostics"])
        item_ref = next(item["ref"] for item in item_refs["result"]["references"] if item["targetKind"] == "item")
        action_pin = self.save_preview_source("action_definition", action_id, "Action/{}.json".format(action_id), {
            "schemaVersion": 1, "id": action_id, "capabilityId": "player.item.grant", "implementationKind": "host_effect",
            "data": {"functionId": "player.item.grant", "arguments": {"itemRef": item_ref, "quantity": 1}},
        })
        store = self.call_cli("authoring.info", {})["result"]["store"]
        action_refs = self.call_cli("references.list", {"targetKind": "action", "sourcePins": [item_pin, action_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]})
        self.assertEqual("ok", action_refs["status"], action_refs["diagnostics"])
        action_ref = next(item["ref"] for item in action_refs["result"]["references"] if item["targetKind"] == "action")
        dialogue_refs = self.call_cli("references.list", {"targetKind": "dialogue", "sourcePins": [dialogue_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"]})
        dialogue_ref = next(item["ref"] for item in dialogue_refs["result"]["references"] if item["targetKind"] == "dialogue")
        scenario_pin = self.save_preview_source("scenario_definition", scenario_id, "Scenario/{}.json".format(scenario_id), {
            "scenarioId": scenario_id, "topicId": "topic.grant_fixture", "entryStepId": "step.dialogue", "steps": [
                {"id": "step.dialogue", "kind": "dialogue", "dialogueRef": dialogue_ref, "nextStepId": "step.grant"},
                {"id": "step.grant", "kind": "host_effect", "actionRef": action_ref,
                 "successNextStepId": "step.done", "failureNextStepId": "step.failed",
                 "data": {"schemaVersion": 1, "requiresCompletedStepId": "step.dialogue"}},
                {"id": "step.done", "kind": "end", "outcome": "completed"},
                {"id": "step.failed", "kind": "end", "outcome": "failed"},
            ],
        })
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "scenario", "documentId": scenario_pin["documentId"], "revisionHash": scenario_pin["revisionHash"],
            "sourcePins": [dialogue_pin, item_pin, action_pin, scenario_pin], "expectedStoreId": store["storeId"],
            "expectedStoreRevision": store["storeRevision"], "fixtureId": "workbench/furniture",
            "events": [{"action": "continue"}]}
        cli = self.call_cli("preview.run", payload, "preview-synthetic-item-grant")
        browser = self.call_http("preview.run", payload, "preview-synthetic-item-grant")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("completed", result["flowOutcome"])
        self.assertEqual(1, next(item["quantity"] for item in result["fixtureItemQuantities"] if item["itemId"] == item_id))
        self.assertEqual(1, len(result["syntheticHostEffectChanges"]))
        self.assertTrue(result["syntheticHostEffectChanges"][0]["syntheticOnly"])
        self.assertFalse(result["syntheticHostEffectChanges"][0]["durable"])
        self.assertEqual(0, result["providerCalls"])

    def test_synthetic_host_effect_port_reuses_the_exact_second_item_reference(self):
        item_id, dialogue_id, action_id = "item.herbal_tea", "dialogue.tea_fixture", "action.tea_fixture"
        item_pin = self.save_preview_source("item_definition", item_id, "Item/{}.json".format(item_id), {
            "schemaVersion": 1, "id": item_id,
            "displayName": {"id": "text.herbal_tea", "translations": [{"language": "en", "text": "Herbal tea"}]},
            "stackLimit": 5,
        })
        item_ref = self.resolve_preview_reference("item", [item_pin])
        dialogue_pin = self.save_preview_bark(dialogue_id, "I brought tea.")
        dialogue_ref = self.resolve_preview_reference("dialogue", [dialogue_pin])
        action_pin = self.save_preview_source("action_definition", action_id, "Action/{}.json".format(action_id), {
            "schemaVersion": 1, "id": action_id, "capabilityId": "player.item.grant", "implementationKind": "host_effect",
            "data": {"functionId": "player.item.grant", "arguments": {"itemRef": item_ref, "quantity": 1}},
        })
        action_ref = self.resolve_preview_reference("action", [item_pin, action_pin])
        scenario = {"scenarioId": "scenario.tea_fixture", "topicId": "topic.tea_fixture", "entryStepId": "step.cheerful", "steps": [
            {"id": "step.cheerful", "kind": "dialogue", "dialogueRef": dialogue_ref, "nextStepId": "step.grant"},
            {"id": "step.grant", "kind": "host_effect", "actionRef": action_ref,
             "successNextStepId": "step.end", "failureNextStepId": "step.failed",
             "data": {"schemaVersion": 1, "requiresCompletedStepId": "step.cheerful"}},
            {"id": "step.end", "kind": "end", "outcome": "completed"},
            {"id": "step.failed", "kind": "end", "outcome": "failed"},
        ]}
        store = self.call_cli("authoring.info", {})["result"]["store"]
        payload = {"kindId": "scenario", "document": scenario, "sourcePins": [item_pin, dialogue_pin, action_pin],
            "expectedStoreId": store["storeId"], "expectedStoreRevision": store["storeRevision"],
            "fixtureId": "workbench/proof", "events": [{"action": "continue"}]}
        cli = self.call_cli("preview.run", payload, "preview-second-item-grant")
        browser = self.call_http("preview.run", payload, "preview-second-item-grant")
        self.assertEqual(cli, browser)
        self.assertEqual("ok", cli["status"], cli["diagnostics"])
        result = cli["result"]
        self.assertEqual("item.herbal_tea", result["syntheticHostEffectChanges"][0]["itemId"])
        self.assertEqual(1, next(item["quantity"] for item in result["fixtureItemQuantities"] if item["itemId"] == item_id))
        self.assertEqual(0, next(item["quantity"] for item in result["fixtureItemQuantities"] if item["itemId"] == "item.water_glass"))

    def test_source_validation_never_claims_release_readiness_for_unresolved_text_reference(self):
        document = {
            "schemaVersion": 1,
            "id": "item.unresolved_text_fixture",
            "displayNameRef": {
                "store": "11111111-1111-4111-8111-111111111111",
                "id": "text.not_in_selected_release",
                "revision": "a" * 64,
                "expectedHash": "b" * 64,
            },
            "stackLimit": 1,
        }
        result = self.call_cli("content.validate", {"kindId": "item", "document": document})
        self.assertEqual("ok", result["status"], result["diagnostics"])
        self.assertTrue(result["result"]["valid"])
        self.assertFalse(result["result"]["readyForPublishedRelease"])
        self.assertEqual("source", result["result"]["validationScope"])


if __name__ == "__main__":
    unittest.main()
