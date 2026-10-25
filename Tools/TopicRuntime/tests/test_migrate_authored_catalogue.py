import json
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from migrate_authored_catalogue import build_catalogue, verify_staged_catalogue, write_catalogue


def source_dialogue(route, dialogue_id, anchors, start=-1, end=-1, priority=1.0):
    return {
        "schemaVersion": 1,
        "id": f"generic.{dialogue_id}",
        "source": "generic",
        "routing": {
            "routeId": route,
            "priority": priority,
            "startHour": start,
            "endHour": end,
            "examples": anchors,
            "requiredContext": [],
            "blockedContext": [],
        },
        "nodes": [
            {
                "id": dialogue_id + "_01",
                "speaker": "alterra",
                "text": "原文",
                "displayText": "Original text",
                "language": "japanese",
                "voice": "alterra",
                "audioKey": "",
                "nextId": "",
                "presentation": {"expression": "gentle", "animation": ""},
                "choices": [],
            }
        ],
    }


class CatalogueMigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / "Generic"
        self.root.mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def add(self, route, filename, dialogue):
        folder = self.root / route
        folder.mkdir(exist_ok=True)
        (folder / filename).write_text(json.dumps(dialogue, ensure_ascii=False), encoding="utf-8")

    def test_greeting_routes_keep_anchor_banks_separate_and_map_hour_gates(self):
        self.add("greeting_morning", "greeting_morning_001.json",
                 source_dialogue("greeting_morning", "greeting_morning_001", ["Good morning", "Hi"], 5, 12))
        self.add("greeting_morning", "greeting_morning_002.json",
                 source_dialogue("greeting_morning", "greeting_morning_002", ["Morning", "Hi"], 5, 12, 1.15))
        self.add("greeting_evening", "greeting_evening_001.json",
                 source_dialogue("greeting_evening", "greeting_evening_001", ["Good evening", "Hi"], 17, 22, 1.15))
        topics, mapping = build_catalogue(self.root)

        morning = topics["greeting_morning"]
        self.assertEqual(["Good morning", "Hi", "Morning"], morning["anchors"])
        self.assertEqual(2, len(morning["scenarios"]))
        self.assertEqual("timeWindow", morning["scenarios"][0]["condition"]["kind"])
        self.assertEqual((5, 12), (morning["scenarios"][0]["condition"]["startHour"], morning["scenarios"][0]["condition"]["endHour"]))
        self.assertGreater(morning["scenarios"][1]["priority"], morning["scenarios"][0]["priority"])
        self.assertNotIn("Good evening", morning["anchors"])
        self.assertIn("Good evening", topics["greeting_evening"]["anchors"])
        self.assertEqual(3, len(mapping))

    def test_dialogue_payload_is_preserved_and_defaults_are_repeatable(self):
        dialogue = source_dialogue("apology", "apology_001", ["I'm sorry"])
        self.add("apology", "apology_001.json", dialogue)
        topics, mapping = build_catalogue(self.root)
        topic = topics["apology"]
        self.assertEqual(dialogue["nodes"], topic["scenarios"][0]["nodes"])
        self.assertEqual("repeatable", topic["defaultReplayPolicy"])
        self.assertEqual(0, topic["cooldownSeconds"])
        self.assertEqual("apology_001", mapping[0]["scenarioId"])

    def test_unsupported_legacy_gates_fail_closed(self):
        dialogue = source_dialogue("apology", "apology_001", ["I'm sorry"])
        dialogue["routing"]["requiredContext"] = ["some.fact"]
        self.add("apology", "apology_001.json", dialogue)
        with self.assertRaisesRegex(ValueError, "requiredContext"):
            build_catalogue(self.root)

    def test_unknown_legacy_routing_fields_fail_closed(self):
        dialogue = source_dialogue("apology", "apology_001", ["I'm sorry"])
        dialogue["routing"]["futureGate"] = "unrecognized"
        self.add("apology", "apology_001.json", dialogue)
        with self.assertRaisesRegex(ValueError, "Unknown routing field"):
            build_catalogue(self.root)

    def test_source_route_identity_must_match_its_folder(self):
        self.add("apology", "apology_001.json", source_dialogue("farewell", "apology_001", ["Bye"]))
        with self.assertRaisesRegex(ValueError, "Folder route"):
            build_catalogue(self.root)

    def test_migration_mapping_has_one_stable_row_per_source(self):
        self.add("apology", "apology_001.json", source_dialogue("apology", "apology_001", ["Sorry"]))
        self.add("apology", "apology_002.json", source_dialogue("apology", "apology_002", ["My fault"]))
        topics, mapping = build_catalogue(self.root)
        self.assertEqual(2, len(mapping))
        self.assertEqual({"apology_001", "apology_002"}, {row["scenarioId"] for row in mapping})
        self.assertEqual({"apology"}, {row["topicId"] for row in mapping})
        self.assertEqual(2, len(topics["apology"]["scenarios"]))

    def test_staging_verifier_detects_lost_anchor_or_dialogue_payload(self):
        self.add("apology", "apology_001.json", source_dialogue("apology", "apology_001", ["Sorry"]))
        output = Path(self.temp.name) / "staged"
        write_catalogue(self.root, output)
        self.assertEqual((1, 1), verify_staged_catalogue(self.root, output))
        staged_file = output / "topics" / "apology.json"
        staged = json.loads(staged_file.read_text(encoding="utf-8"))
        staged["anchors"].clear()
        staged_file.write_text(json.dumps(staged), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "anchor"):
            verify_staged_catalogue(self.root, output)

    def test_verifier_rejects_cross_route_mapping_even_when_scenario_payload_matches(self):
        apology = source_dialogue("apology", "apology_001", ["shared"])
        farewell = source_dialogue("farewell", "farewell_001", ["shared"])
        farewell["nodes"] = apology["nodes"]
        self.add("apology", "apology_001.json", apology)
        self.add("farewell", "farewell_001.json", farewell)
        output = Path(self.temp.name) / "staged"
        write_catalogue(self.root, output)
        map_path = output / "migration-map.json"
        manifest = json.loads(map_path.read_text(encoding="utf-8"))
        row = next(row for row in manifest["mapping"] if row["legacyRouteId"] == "apology")
        row["topicId"] = "farewell"
        row["scenarioId"] = "farewell_001"
        row["stagedFile"] = "topics/farewell.json"
        map_path.write_text(json.dumps(manifest), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "route identity"):
            verify_staged_catalogue(self.root, output)


if __name__ == "__main__":
    unittest.main()
