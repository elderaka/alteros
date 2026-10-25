import json
import tempfile
import unittest
from pathlib import Path

from snapshot_anchors import load_snapshot_anchor_records


class SnapshotAnchorInputTests(unittest.TestCase):
    def test_sorts_exact_canonical_and_topic_owners(self):
        records = [
            {"ownerKind": "topic", "ownerId": "topic.tv", "ownerRevision": "b" * 64,
             "role": "anchor_0000", "text": "Turn on the TV"},
            {"ownerKind": "canonical", "ownerId": "generic.tv", "ownerRevision": "a" * 64,
             "role": "gte_route", "text": "TV"},
        ]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "anchors.json"
            path.write_text(json.dumps({"schemaVersion": 1, "snapshotHash": "c" * 64, "anchors": records}), encoding="utf-8")
            loaded, digest = load_snapshot_anchor_records(path)
        self.assertEqual(digest, "c" * 64)
        self.assertEqual([item["ownerKind"] for item in loaded], ["canonical", "topic"])

    def test_rejects_duplicate_and_malformed_owner_records(self):
        record = {"ownerKind": "topic", "ownerId": "topic.tv", "ownerRevision": "b" * 64,
                  "role": "anchor_0000", "text": "Turn on the TV"}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "anchors.json"
            path.write_text(json.dumps({"schemaVersion": 1, "snapshotHash": "c" * 64,
                                        "anchors": [record, record]}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate"):
                load_snapshot_anchor_records(path)
            path.write_text(json.dumps({"schemaVersion": 1, "snapshotHash": "c" * 64,
                                        "anchors": [{**record, "ownerKind": "generic"}]}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "malformed"):
                load_snapshot_anchor_records(path)


if __name__ == "__main__":
    unittest.main()
