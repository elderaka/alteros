"""Validate stable semantic anchor ownership projected from one SQL snapshot."""

from __future__ import annotations

import json
from pathlib import Path


def load_snapshot_anchor_records(path: Path) -> tuple[list[dict], str]:
    request = json.loads(path.read_text(encoding="utf-8"))
    snapshot_hash = request.get("snapshotHash")
    if request.get("schemaVersion") != 1 or not isinstance(snapshot_hash, str) or len(snapshot_hash) != 64:
        raise ValueError("Snapshot anchor input must include schemaVersion 1 and an exact snapshotHash")
    if any(character not in "0123456789abcdef" for character in snapshot_hash):
        raise ValueError("The selected SQL snapshot hash must be lowercase SHA-256")
    records = request.get("anchors")
    if not isinstance(records, list) or not records:
        raise ValueError("The selected SQL snapshot contains no semantic anchors")
    accepted: list[dict] = []
    seen: set[tuple[str, str, str, str, str]] = set()
    for record in records:
        fields = {"ownerKind", "ownerId", "ownerRevision", "role", "text"}
        if not isinstance(record, dict) or set(record) != fields:
            raise ValueError("Each selected-snapshot anchor must have exactly ownerKind, ownerId, ownerRevision, role and text")
        if record["ownerKind"] not in {"canonical", "topic"} or not all(
            isinstance(record[field], str) and record[field].strip()
            for field in ("ownerId", "ownerRevision", "role", "text")
        ):
            raise ValueError("A selected-snapshot anchor has malformed owner identity or text")
        if len(record["text"]) > 2048 or len(record["ownerId"]) > 128 or len(record["role"]) > 128:
            raise ValueError("A selected-snapshot anchor exceeds supported owner or text bounds")
        key = (record["ownerKind"], record["ownerId"], record["ownerRevision"], record["role"], record["text"])
        if key in seen:
            raise ValueError("The selected-snapshot anchor list contains a duplicate owner/role/text record")
        seen.add(key)
        accepted.append(record)
    accepted.sort(key=lambda item: (item["ownerKind"], item["ownerId"], item["ownerRevision"], item["role"], item["text"]))
    return accepted, snapshot_hash
