"""Convert an exported ONNX encoder to FP16 while preserving integer inputs."""

from __future__ import annotations

import argparse
from pathlib import Path

import onnx
from onnxconverter_common import float16


def main() -> None:
    args = parse_args()
    model = onnx.load(args.source)
    converted = float16.convert_float_to_float16(model, keep_io_types=True)
    onnx.checker.check_model(converted)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(converted, args.output)
    print(f"Converted {args.source} to {args.output}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    return parser.parse_args()


if __name__ == "__main__":
    main()
