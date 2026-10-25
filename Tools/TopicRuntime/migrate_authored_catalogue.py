"""Build an additive Topic-family staging copy from authored dialogue JSON."""

from __future__ import annotations

import argparse
import json
import math
import re
from pathlib import Path


SAFE_ID = re.compile(r"^[a-z0-9_.-]+$")
LEGACY_ROUTING_FIELDS = {
    "routeId", "priority", "startHour", "endHour", "examples",
    "requiredContext", "blockedContext", "eligibility",
}


def _read_sources(source_root: Path) -> list[tuple[Path, dict]]:
    files = sorted(source_root.rglob("*.json"))
    if not files:
        raise ValueError(f"No authored dialogue JSON found under {source_root}")
    result = []
    dialogue_ids = set()
    for path in files:
        try:
            dialogue = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError) as error:
            raise ValueError(f"Cannot parse {path}: {error}") from error
        relative = path.relative_to(source_root).as_posix()
        if not isinstance(dialogue, dict) or dialogue.get("schemaVersion") != 1:
            raise ValueError(f"Unsupported legacy schema: {relative}")
        if dialogue.get("source") != "generic":
            raise ValueError(f"Only generic authored sources are in scope: {relative}")
        route = (dialogue.get("routing") or {}).get("routeId")
        routing = dialogue.get("routing") or {}
        unknown_fields = set(routing) - LEGACY_ROUTING_FIELDS
        if unknown_fields:
            raise ValueError(f"Unknown routing field(s) {', '.join(sorted(unknown_fields))}: {relative}")
        if not isinstance(route, str) or not SAFE_ID.fullmatch(route):
            raise ValueError(f"Invalid legacy routeId: {relative}")
        if path.parent.name != route:
            raise ValueError(f"Folder route does not match routing.routeId: {relative}")
        dialogue_id = dialogue.get("id")
        if not isinstance(dialogue_id, str) or not dialogue_id or dialogue_id in dialogue_ids:
            raise ValueError(f"Missing or duplicate legacy dialogue id: {relative}")
        dialogue_ids.add(dialogue_id)
        result.append((path, dialogue))
    return result


def build_catalogue(source_root: Path) -> tuple[dict[str, dict], list[dict]]:
    """Convert source records without changing dialogue payloads or losing gates."""
    source_root = Path(source_root).resolve()
    sources = _read_sources(source_root)
    priorities = set()
    for path, dialogue in sources:
        routing = dialogue.get("routing") or {}
        priority = routing.get("priority")
        if isinstance(priority, bool) or not isinstance(priority, (int, float)) or not math.isfinite(priority):
            raise ValueError(f"Invalid legacy priority: {path}")
        priorities.add(float(priority))
    priority_rank = {value: rank for rank, value in enumerate(sorted(priorities))}

    topics: dict[str, dict] = {}
    mapping: list[dict] = []
    for path, dialogue in sources:
        relative = path.relative_to(source_root).as_posix()
        routing = dialogue["routing"]
        route = routing["routeId"]
        # Preserve each existing route's semantic anchor boundary until scenarios support local anchors.
        topic_id = route

        required = routing.get("requiredContext") or []
        blocked = routing.get("blockedContext") or []
        eligibility = routing.get("eligibility")
        if required or blocked or eligibility is not None:
            fields = []
            if required:
                fields.append("requiredContext")
            if blocked:
                fields.append("blockedContext")
            if eligibility is not None:
                fields.append("eligibility")
            raise ValueError(f"Unsupported legacy gate field(s) {', '.join(fields)} require explicit mapping: {relative}")

        start, end = routing.get("startHour", -1), routing.get("endHour", -1)
        if (start, end) == (-1, -1):
            condition = None
        elif (isinstance(start, bool) or isinstance(end, bool) or
              not isinstance(start, int) or not isinstance(end, int) or
              not 0 <= start <= 23 or not 0 <= end <= 23):
            raise ValueError(f"Unsupported or malformed legacy time window: {relative}")
        else:
            condition = {
                "kind": "timeWindow", "children": [], "statId": "", "operation": "",
                "value": 0, "key": "", "expected": True,
                "startHour": start, "endHour": end, "objectKind": "",
            }

        anchors = routing.get("examples")
        if not isinstance(anchors, list) or not anchors or any(not isinstance(a, str) or not a.strip() for a in anchors):
            raise ValueError(f"Missing or malformed semantic anchors: {relative}")
        nodes = dialogue.get("nodes")
        if not isinstance(nodes, list) or not nodes:
            raise ValueError(f"Missing dialogue graph: {relative}")

        topic = topics.setdefault(topic_id, {
            "schemaVersion": 1,
            "topicId": topic_id,
            # The source format has no authored menu label or prompt; keep them blank for editorial review.
            "menuLabel": "",
            "prompt": "",
            "anchors": [],
            "defaultReplayPolicy": "repeatable",
            "cooldownSeconds": 0,
            "scenarios": [],
        })
        anchor_set = set(topic["anchors"])
        for anchor in anchors:
            if anchor not in anchor_set:
                topic["anchors"].append(anchor)
                anchor_set.add(anchor)

        scenario_id = path.stem
        if any(existing["scenarioId"] == scenario_id for existing in topic["scenarios"]):
            raise ValueError(f"Scenario ID collision in {topic_id}: {scenario_id}")
        legacy_priority = float(routing["priority"])
        topic["scenarios"].append({
            "scenarioId": scenario_id,
            "priority": priority_rank[legacy_priority],
            "baseWeight": 1,
            "condition": condition,
            "modifiers": [],
            "consumptionNodeId": "",
            "nodes": nodes,
        })
        mapping.append({
            "sourceFile": relative,
            "legacyDialogueId": dialogue["id"],
            "legacyRouteId": route,
            "topicId": topic_id,
            "scenarioId": scenario_id,
            "legacyPriority": legacy_priority,
            "scenarioPriority": priority_rank[legacy_priority],
            "legacyTimeWindow": None if condition is None else {"startHour": start, "endHour": end},
            "stagedFile": f"topics/{topic_id}.json",
        })

    return dict(sorted(topics.items())), mapping


def write_catalogue(source_root: Path, output_root: Path) -> tuple[int, int]:
    topics, mapping = build_catalogue(source_root)
    output_root = Path(output_root).resolve()
    if output_root.exists():
        existing_files = [path for path in output_root.rglob("*") if path.is_file()]
        children = list(output_root.iterdir())
        if existing_files or len(children) != 1 or children[0].name != "topics" or not children[0].is_dir():
            raise FileExistsError(f"Refusing to overwrite existing staging output: {output_root}")
    topic_dir = output_root / "topics"
    topic_dir.mkdir(parents=True, exist_ok=True)
    for topic_id, topic in topics.items():
        target = topic_dir / f"{topic_id}.json"
        target.write_text(json.dumps(topic, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    manifest = {
        "schemaVersion": 1,
        "status": "staged-not-published",
        "sourceRoot": "Assets/StreamingAssets/Dialogue/Generic",
        "sourceFileCount": len(mapping),
        "topicCount": len(topics),
        "priorityLevels": [row["legacyPriority"] for row in mapping],
        "mapping": mapping,
    }
    manifest["priorityLevels"] = sorted({row["legacyPriority"] for row in mapping})
    (output_root / "migration-map.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    verify_staged_catalogue(source_root, output_root)
    return len(mapping), len(topics)


def verify_staged_catalogue(source_root: Path, output_root: Path) -> tuple[int, int]:
    """Check source coverage and exact graph/anchor preservation from the map."""
    sources = _read_sources(Path(source_root).resolve())
    expected_topics, _ = build_catalogue(Path(source_root).resolve())
    output_root = Path(output_root)
    manifest = json.loads((output_root / "migration-map.json").read_text(encoding="utf-8"))
    mapping = manifest.get("mapping")
    if not isinstance(mapping, list) or len(mapping) != len(sources):
        raise ValueError("Migration map must have exactly one row for each source file")
    by_source = {row.get("sourceFile"): row for row in mapping}
    if len(by_source) != len(mapping):
        raise ValueError("Migration map repeats a source file")
    if set(by_source) != {path.relative_to(Path(source_root).resolve()).as_posix() for path, _ in sources}:
        raise ValueError("Migration map does not cover the source file set exactly")

    definitions = {}
    for path in (output_root / "topics").glob("*.json"):
        definition = json.loads(path.read_text(encoding="utf-8"))
        if definition.get("topicId") in definitions or path.stem != definition.get("topicId"):
            raise ValueError(f"Duplicate or filename-mismatched Topic definition: {path}")
        definitions[definition["topicId"]] = definition
    if len(definitions) != manifest.get("topicCount") or len(definitions) != len(expected_topics):
        raise ValueError("Staged Topic file count does not match the source families")

    priorities = sorted({float((dialogue.get("routing") or {}).get("priority")) for _, dialogue in sources})
    rank = {value: index for index, value in enumerate(priorities)}
    if manifest.get("priorityLevels") != priorities:
        raise ValueError("Migration map priorityLevels do not match the source")
    for topic_id, expected in expected_topics.items():
        if definitions[topic_id].get("anchors") != expected["anchors"]:
            raise ValueError(f"Staged anchor bank differs from the exact source union: {topic_id}")
    for path, dialogue in sources:
        relative = path.relative_to(Path(source_root).resolve()).as_posix()
        row = by_source[relative]
        source_route = dialogue["routing"]["routeId"]
        if row.get("topicId") != source_route or row.get("legacyRouteId") != source_route or row.get("scenarioId") != path.stem:
            raise ValueError(f"Legacy route identity or scenario filename changed in mapping: {relative}")
        definition = definitions.get(row.get("topicId"))
        if definition is None or row.get("stagedFile") != f"topics/{row.get('topicId')}.json":
            raise ValueError(f"Missing staged Topic target for {relative}")
        if row.get("legacyRouteId") != dialogue["routing"]["routeId"] or row.get("legacyDialogueId") != dialogue["id"]:
            raise ValueError(f"Legacy identity changed in mapping for {relative}")
        scenarios = [scenario for scenario in definition.get("scenarios", []) if scenario.get("scenarioId") == row.get("scenarioId")]
        if len(scenarios) != 1:
            raise ValueError(f"Expected exactly one mapped scenario for {relative}")
        scenario = scenarios[0]
        if scenario.get("nodes") != dialogue["nodes"]:
            raise ValueError(f"Dialogue node payload changed during migration: {relative}")
        if not set(dialogue["routing"]["examples"]).issubset(set(definition.get("anchors", []))):
            raise ValueError(f"A source anchor is missing from its staged Topic: {relative}")
        source_priority = float(dialogue["routing"]["priority"])
        if row.get("legacyPriority") != source_priority or row.get("scenarioPriority") != rank[source_priority] or scenario.get("priority") != rank[source_priority]:
            raise ValueError(f"Priority mapping changed for {relative}")
        start, end = dialogue["routing"].get("startHour", -1), dialogue["routing"].get("endHour", -1)
        condition = scenario.get("condition")
        if start < 0 and end < 0:
            if condition is not None:
                raise ValueError(f"Unexpected scenario condition: {relative}")
        elif not isinstance(condition, dict) or (condition.get("kind"), condition.get("startHour"), condition.get("endHour")) != ("timeWindow", start, end):
            raise ValueError(f"Time window changed during migration: {relative}")
        if definition.get("defaultReplayPolicy") != "repeatable" or scenario.get("baseWeight") != 1 or scenario.get("consumptionNodeId"):
            raise ValueError(f"Unexpected replay/weight/checkpoint policy: {relative}")
    return len(sources), len(definitions)


def main() -> None:
    project_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=project_root / "Assets/StreamingAssets/Dialogue/Generic")
    parser.add_argument("--output", type=Path, default=project_root / "Tools/TopicRuntime/StagedCatalogue")
    parser.add_argument("--verify", action="store_true", help="Check existing staged output without changing files")
    args = parser.parse_args()
    if args.verify:
        source_count, topic_count = verify_staged_catalogue(args.source, args.output)
        print(f"PASS: preserved {source_count} source dialogues across {topic_count} Topic families")
        return
    source_count, topic_count = write_catalogue(args.source, args.output)
    print(f"Staged {source_count} source files as {topic_count} Topic families under {args.output}")


if __name__ == "__main__":
    main()
