from __future__ import annotations

import argparse
import json
import sys
import tkinter as tk
from dataclasses import asdict
from pathlib import Path
from tkinter import ttk

import numpy as np

from semantic_router import DEFAULT_CONTENT, MODELS, ROOT, SemanticBenchmark, load_route_examples, score_routes


DEFAULT_SILVER_CASES = ROOT / "Tools" / "SemanticLab" / "silver_cases.json"


def main() -> int:
    args = parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.gui:
        show_gui()
        return 0
    if args.evaluate:
        reports = [evaluate_model(model, args.evaluate, args.device, args.minimum, args.batch_size) for model in args.model]
        print(json.dumps(reports, indent=2, ensure_ascii=False) if args.json else "\n\n".join(format_evaluation(report) for report in reports))
        return 0
    if not args.query:
        raise SystemExit("--query is required unless --gui or --self-test is used.")

    reports = [run_model(model, args.query, args.runs, args.top, args.device, args.minimum, args.batch_size) for model in args.model]
    if args.json:
        print(json.dumps(reports, indent=2, ensure_ascii=False))
    else:
        for report in reports:
            print_report(report)
    return 0


def run_model(model: str, query: str, runs: int, top: int, device: str, minimum: float, batch_size: int = 16) -> dict:
    benchmark = SemanticBenchmark(model, device=device, batch_size=batch_size)
    try:
        results, timing = benchmark.run(query, runs=runs, minimum_similarity=minimum)
        selected = results if top == 0 else results[:top]
        return {
            "timing": timing.to_dict(),
            "winner_margin": results[0].score - results[1].score,
            "results": [asdict(result) for result in selected],
        }
    finally:
        benchmark.close()


def print_report(report: dict) -> None:
    timing = report["timing"]
    print(f"\n{timing['model']} on {timing['device']} | {timing['anchors']} anchors / {timing['routes']} routes")
    print(
        f"load {timing['model_load_ms']:.1f} ms | index {timing['index_ms']:.1f} ms "
        f"({'cache' if timing['cache_hit'] else 'built'}) | cold query {timing['cold_query_ms']:.1f} ms | "
        f"warm p50/p95 {timing['warm_query_median_ms']:.1f}/{timing['warm_query_p95_ms']:.1f} ms | "
        f"score {timing['scoring_median_ms']:.3f} ms | peak VRAM {timing['peak_vram_mb']:.0f} MB"
    )
    print(f"winner margin {report['winner_margin']:.4f}")
    for index, result in enumerate(report["results"], 1):
        print(
            f"{index:>2}. {result['route_id']:<30} score={result['score']:.4f} "
            f"best={result['best_similarity']:.4f} support={result['support_count']} "
            f"anchor={result['best_anchor']!r}"
        )


def evaluate_model(model: str, cases_path: Path, device: str, minimum: float, batch_size: int) -> dict:
    cases = json.loads(cases_path.read_text(encoding="utf-8"))
    benchmark = SemanticBenchmark(model, device=device, batch_size=batch_size)
    try:
        rankings, embedding_ms = benchmark.rank_many([case["text"] for case in cases], minimum)
        rows = []
        for case, ranking in zip(cases, rankings):
            expected = case["route"]
            predicted_rank = next((index for index, result in enumerate(ranking, 1) if result.route_id == expected), None)
            rows.append(
                {
                    **case,
                    "predicted": ranking[0].route_id,
                    "nearest_anchor_prediction": max(ranking, key=lambda result: result.best_similarity).route_id,
                    "expected_rank": predicted_rank,
                    "winner_margin": ranking[0].score - ranking[1].score,
                    "top3": [result.route_id for result in ranking[:3]],
                }
            )
        groups = sorted({case["group"] for case in cases})
        return {
            "model": benchmark.model_name,
            "device": benchmark.device,
            "cases": len(rows),
            "top1_accuracy": sum(row["expected_rank"] == 1 for row in rows) / len(rows),
            "nearest_anchor_accuracy": sum(row["nearest_anchor_prediction"] == row["route"] for row in rows) / len(rows),
            "top3_accuracy": sum(row["expected_rank"] is not None and row["expected_rank"] <= 3 for row in rows) / len(rows),
            "mean_embedding_ms_per_text": embedding_ms,
            "peak_vram_mb": torch_peak_vram(benchmark.device),
            "by_group": {
                group: {
                    "cases": sum(row["group"] == group for row in rows),
                    "top1_accuracy": sum(row["group"] == group and row["expected_rank"] == 1 for row in rows)
                    / sum(row["group"] == group for row in rows),
                }
                for group in groups
            },
            "misses": [row for row in rows if row["expected_rank"] != 1],
        }
    finally:
        benchmark.close()


def torch_peak_vram(device: str) -> float:
    if device != "cuda":
        return 0.0
    import torch

    return torch.cuda.max_memory_allocated() / 1_048_576


def format_evaluation(report: dict) -> str:
    lines = [
        f"{report['model']} on {report['device']} | {report['cases']} silver cases",
        f"supported top-1 {report['top1_accuracy']:.1%} | nearest-anchor top-1 {report['nearest_anchor_accuracy']:.1%} | "
        f"supported top-3 {report['top3_accuracy']:.1%} | "
        f"batch embedding {report['mean_embedding_ms_per_text']:.1f} ms/text | peak VRAM {report['peak_vram_mb']:.0f} MB",
        "groups: " + ", ".join(f"{name} {value['top1_accuracy']:.1%}" for name, value in report["by_group"].items()),
        f"misses ({len(report['misses'])}):",
    ]
    lines.extend(
        f"- {row['text']!r}: expected {row['route']}, got {row['predicted']} "
        f"(expected rank {row['expected_rank']}, margin {row['winner_margin']:.4f})"
        for row in report["misses"]
    )
    return "\n".join(lines)


def show_gui() -> None:
    root = tk.Tk()
    root.title("AlterOS Semantic Lab")
    root.geometry("1050x700")

    model = tk.StringVar(value="e5-small")
    query = tk.StringVar(value="ngl im kinda tired")
    status = tk.StringVar(value="Choose a model, enter text, then Run.")
    loaded: dict[str, object] = {"name": None, "benchmark": None}

    controls = ttk.Frame(root, padding=10)
    controls.pack(fill=tk.X)
    ttk.Label(controls, text="Model").grid(row=0, column=0, sticky=tk.W)
    ttk.Combobox(controls, textvariable=model, values=list(MODELS), width=24, state="readonly").grid(row=0, column=1, padx=8)
    ttk.Label(controls, text="Player text").grid(row=1, column=0, sticky=tk.W, pady=(8, 0))
    entry = ttk.Entry(controls, textvariable=query)
    entry.grid(row=1, column=1, columnspan=3, sticky=tk.EW, padx=8, pady=(8, 0))
    controls.columnconfigure(2, weight=1)

    output = tk.Text(root, wrap=tk.NONE, font=("Consolas", 10))
    output.pack(fill=tk.BOTH, expand=True, padx=10, pady=(0, 10))

    def run() -> None:
        status.set("Loading model and scoring all routes...")
        root.update_idletasks()
        try:
            if loaded["name"] != model.get():
                if loaded["benchmark"]:
                    loaded["benchmark"].close()
                loaded["name"] = model.get()
                loaded["benchmark"] = SemanticBenchmark(model.get())
            results, timing_value = loaded["benchmark"].run(query.get(), runs=10)
            report = {
                "timing": timing_value.to_dict(),
                "winner_margin": results[0].score - results[1].score,
                "results": [asdict(result) for result in results],
            }
            timing = report["timing"]
            lines = [
                f"Model: {timing['model']}",
                f"Device: {timing['device']}",
                f"Anchors/routes: {timing['anchors']}/{timing['routes']}",
                f"Load: {timing['model_load_ms']:.1f} ms",
                f"Index: {timing['index_ms']:.1f} ms ({'cache' if timing['cache_hit'] else 'built'})",
                f"Cold query: {timing['cold_query_ms']:.1f} ms",
                f"Warm query p50/p95: {timing['warm_query_median_ms']:.1f}/{timing['warm_query_p95_ms']:.1f} ms",
                f"Scoring: {timing['scoring_median_ms']:.3f} ms",
                f"Peak VRAM: {timing['peak_vram_mb']:.0f} MB",
                f"Winner margin: {report['winner_margin']:.4f}",
                "",
            ]
            lines.extend(
                f"{index:>2}. {result['route_id']:<30} score={result['score']:.4f} "
                f"best={result['best_similarity']:.4f} support={result['support_count']}  {result['best_anchor']}"
                for index, result in enumerate(report["results"], 1)
            )
            output.delete("1.0", tk.END)
            output.insert(tk.END, "\n".join(lines))
            status.set("Complete.")
        except Exception as error:
            output.delete("1.0", tk.END)
            output.insert(tk.END, f"{type(error).__name__}: {error}")
            status.set("Failed.")

    ttk.Button(controls, text="Run", command=run).grid(row=0, column=3, padx=8)
    ttk.Label(root, textvariable=status, padding=(10, 0, 10, 8)).pack(fill=tk.X)
    entry.bind("<Return>", lambda _: run())
    root.protocol("WM_DELETE_WINDOW", lambda: (loaded["benchmark"] and loaded["benchmark"].close(), root.destroy()))
    entry.focus_set()
    root.mainloop()


def self_test() -> None:
    vectors = np.asarray([[1.0, 0.0], [0.9, 0.1], [0.0, 1.0]], dtype=np.float32)
    vectors /= np.linalg.norm(vectors, axis=1, keepdims=True)
    results = score_routes(
        np.asarray([1.0, 0.0], dtype=np.float32),
        vectors,
        ["greeting", "greeting", "farewell"],
        ["hello", "hey", "bye"],
        0.45,
    )
    assert results[0].route_id == "greeting"
    assert results[0].support_count == 2
    cases = json.loads(DEFAULT_SILVER_CASES.read_text(encoding="utf-8"))
    _, route_ids, _ = load_route_examples(DEFAULT_CONTENT)
    expected_routes = set(route_ids)
    assert len(cases) == len(expected_routes) * 2
    assert {case["route"] for case in cases} == expected_routes
    assert all(sum(case["route"] == route for case in cases) == 2 for route in expected_routes)
    print("Semantic Lab self-check passed.")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Compare AlterOS dialogue-route embeddings and latency.")
    parser.add_argument("--model", action="append", choices=list(MODELS), default=[])
    parser.add_argument("--query")
    parser.add_argument("--evaluate", type=Path, nargs="?", const=DEFAULT_SILVER_CASES)
    parser.add_argument("--device", choices=["auto", "cpu", "cuda"], default="auto")
    parser.add_argument("--runs", type=int, default=10)
    parser.add_argument("--top", type=int, default=10, help="0 prints every route")
    parser.add_argument("--minimum", type=float, default=0.45)
    parser.add_argument("--batch-size", type=int, default=16)
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--gui", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if not args.model:
        args.model = ["e5-small"]
    return args


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        sys.exit(130)
