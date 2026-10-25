"""Encode sentence probes offline; the C# ProbeRoutes command performs production routing."""
import argparse
import json
import os
from pathlib import Path

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"

from sentence_transformers import SentenceTransformer
import torch
from semantic_router import GTE_REVISION, GTE_CODE_REVISION


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--query", action="append", required=True)
    parser.add_argument("--expected-topic", action="append", default=[])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.expected_topic and len(args.expected_topic) != len(args.query):
        parser.error("Supply one --expected-topic per --query, or omit all expectations.")
    torch.set_num_threads(min(4, os.cpu_count() or 1))
    model = SentenceTransformer("Alibaba-NLP/gte-multilingual-base", device="cpu",
        revision=GTE_REVISION, model_kwargs={"code_revision": GTE_CODE_REVISION},
        trust_remote_code=True, local_files_only=True)
    vectors = model.encode(args.query, normalize_embeddings=True, convert_to_numpy=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"queries": [
        {"text": text, "expectedTopicId": args.expected_topic[index] if args.expected_topic else "",
         "vector": vectors[index].tolist()} for index, text in enumerate(args.query)]}), encoding="utf-8")
    print(f"Encoded {len(args.query)} offline sentence probes to {args.output}")


if __name__ == "__main__":
    main()
