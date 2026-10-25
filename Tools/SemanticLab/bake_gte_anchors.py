"""Bake the current Generic dialogue anchors into the Unity GTE index format."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

from semantic_router import GTE_CODE_REVISION, GTE_REVISION, SemanticBenchmark, load_route_examples
from snapshot_anchors import load_snapshot_anchor_records


def main() -> None:
    args = parse_args()
    anchor_records = None
    if args.snapshot_input is None:
        anchors, route_ids, content_hash = load_route_examples(args.content)
        metadata_schema = 1
    else:
        anchor_records, content_hash = load_snapshot_anchor_records(args.snapshot_input)
        anchors = [item["text"] for item in anchor_records]
        route_ids = [item["ownerId"] for item in anchor_records]
        metadata_schema = 2
    benchmark = SemanticBenchmark("gte-multilingual-base", device=args.device, content_root=args.content,
                                  cache_root=args.cache_root,
                                  anchor_records=anchor_records, source_hash=content_hash if anchor_records is not None else None)
    try:
        if benchmark.anchors != anchors or benchmark.route_ids != route_ids:
            raise RuntimeError("Loaded anchor corpus changed while baking.")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(benchmark.vectors.astype("<f4", copy=False).tobytes())
        metadata = {
            "schemaVersion": metadata_schema,
            "model": "Alibaba-NLP/gte-multilingual-base",
            "modelRevision": GTE_REVISION,
            "codeRevision": GTE_CODE_REVISION,
            "dimension": int(benchmark.vectors.shape[1]),
            "contentHash": content_hash,
            "anchors": anchor_records if anchor_records is not None else [
                {"routeId": route_id, "text": text} for route_id, text in zip(route_ids, anchors)
            ],
        }
        args.metadata.write_text(json.dumps(metadata, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"Baked {len(anchors)} anchors ({metadata['dimension']} dimensions) to {args.output}")
    finally:
        benchmark.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--content", type=Path, default=Path(__file__).resolve().parents[2] / "Assets/StreamingAssets/Dialogue/Generic")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--metadata", type=Path, required=True)
    parser.add_argument("--snapshot-input", type=Path)
    parser.add_argument("--cache-root", type=Path, default=Path(__file__).resolve().parent / "cache")
    parser.add_argument("--device", choices=["auto", "cpu", "cuda"], default="auto")
    return parser.parse_args()


if __name__ == "__main__":
    main()
