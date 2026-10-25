"""Export the pinned GTE encoder to a fixed-shape Unity/Sentis ONNX model."""

from __future__ import annotations

import argparse
from pathlib import Path

import torch
import torch.nn.functional as F
from sentence_transformers import SentenceTransformer


MODEL_ID = "Alibaba-NLP/gte-multilingual-base"
MODEL_REVISION = "9bbca17d9273fd0d03d5725c7a4b0f6b45142062"
CODE_REVISION = "40ced75c3017eb27626c9d4ea981bde21a2662f4"


class GteClsEncoder(torch.nn.Module):
    def __init__(self, encoder: torch.nn.Module) -> None:
        super().__init__()
        self.encoder = encoder

    def forward(self, input_ids: torch.Tensor, attention_mask: torch.Tensor) -> torch.Tensor:
        output = self.encoder(
            input_ids=input_ids,
            attention_mask=attention_mask,
            return_dict=True,
            unpad_inputs=False,
        )
        return F.normalize(output.last_hidden_state[:, 0, :], p=2, dim=-1)


def main() -> None:
    args = parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    st_model = SentenceTransformer(
        MODEL_ID,
        revision=MODEL_REVISION,
        trust_remote_code=True,
        model_kwargs={"code_revision": CODE_REVISION},
        device="cpu",
    )
    tokenizer = st_model.tokenizer
    encoded = tokenizer(
        ["warm up"],
        max_length=args.max_length,
        padding="max_length",
        truncation=True,
        return_tensors="pt",
    )
    encoder = GteClsEncoder(st_model[0].auto_model.eval()).eval()
    with torch.no_grad():
        torch.onnx.export(
            encoder,
            (encoded["input_ids"], encoded["attention_mask"]),
            args.output,
            input_names=["input_ids", "attention_mask"],
            output_names=["embedding"],
            opset_version=15,
            dynamo=False,
        )
    print(f"Exported {MODEL_ID}@{MODEL_REVISION} to {args.output}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-length", type=int, default=128)
    return parser.parse_args()


if __name__ == "__main__":
    main()
