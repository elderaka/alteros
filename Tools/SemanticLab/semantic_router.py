from __future__ import annotations

import gc
import hashlib
import json
import statistics
import time
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import torch
from sentence_transformers import SentenceTransformer


MODELS = {
    "minilm-l6": "sentence-transformers/all-MiniLM-L6-v2",
    "multilingual-minilm": "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2",
    "e5-small": "intfloat/multilingual-e5-small",
    "gte-multilingual-base": "Alibaba-NLP/gte-multilingual-base",
    "qwen-0.6b": "Qwen/Qwen3-Embedding-0.6B",
}

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONTENT = ROOT / "Assets" / "StreamingAssets" / "Dialogue" / "Generic"
DEFAULT_CACHE = ROOT / ".semantic-cache"
QWEN_INSTRUCTION = "Given a casual player message, retrieve examples with the same companion dialogue intent"
GTE_REVISION = "9bbca17d9273fd0d03d5725c7a4b0f6b45142062"
GTE_CODE_REVISION = "40ced75c3017eb27626c9d4ea981bde21a2662f4"


@dataclass(frozen=True)
class RouteScore:
    route_id: str
    score: float
    best_similarity: float
    support_count: int
    best_anchor: str


@dataclass(frozen=True)
class BenchmarkTiming:
    model: str
    device: str
    anchors: int
    routes: int
    cache_hit: bool
    model_load_ms: float
    index_ms: float
    cold_query_ms: float
    warm_query_median_ms: float
    warm_query_p95_ms: float
    scoring_median_ms: float
    peak_vram_mb: float

    def to_dict(self) -> dict:
        return asdict(self)


def load_route_examples(content_root: Path) -> tuple[list[str], list[str], str]:
    by_route: dict[str, set[str]] = {}
    digest = hashlib.sha256()

    for path in sorted(content_root.rglob("*.json")):
        raw = path.read_bytes()
        digest.update(path.relative_to(content_root).as_posix().encode())
        digest.update(raw)
        dialogue = json.loads(raw)
        route_id = dialogue["routing"]["routeId"].strip()
        examples = by_route.setdefault(route_id, set())
        examples.update(text.strip() for text in dialogue["routing"]["examples"] if text.strip())

    anchors: list[str] = []
    route_ids: list[str] = []
    for route_id in sorted(by_route):
        for text in sorted(by_route[route_id], key=str.casefold):
            route_ids.append(route_id)
            anchors.append(text)

    if not anchors:
        raise ValueError(f"No routing examples found under {content_root}")

    return anchors, route_ids, digest.hexdigest()


def score_routes(
    query: np.ndarray,
    vectors: np.ndarray,
    route_ids: list[str],
    anchors: list[str],
    minimum_similarity: float,
) -> list[RouteScore]:
    similarities = vectors @ query
    grouped: dict[str, list[int]] = {}
    for index, route_id in enumerate(route_ids):
        grouped.setdefault(route_id, []).append(index)

    results: list[RouteScore] = []
    for route_id, indexes in grouped.items():
        ranked = sorted(indexes, key=similarities.__getitem__, reverse=True)[:3]
        scores = [float(similarities[index]) for index in ranked]
        weights = [1.0 / (index + 1) for index in range(len(scores))]
        supported = sum(score * weight for score, weight in zip(scores, weights)) / sum(weights)
        score = supported + 0.40 * (scores[0] - supported)
        results.append(
            RouteScore(
                route_id=route_id,
                score=score,
                best_similarity=scores[0],
                support_count=sum(value >= minimum_similarity for value in scores),
                best_anchor=anchors[ranked[0]],
            )
        )

    return sorted(results, key=lambda result: result.score, reverse=True)


class SemanticBenchmark:
    def __init__(
        self,
        model_name: str,
        device: str = "auto",
        content_root: Path = DEFAULT_CONTENT,
        cache_root: Path = DEFAULT_CACHE,
        batch_size: int = 16,
        anchor_records: list[dict] | None = None,
        source_hash: str | None = None,
    ) -> None:
        self.model_name = MODELS.get(model_name, model_name)
        self.device = "cuda" if device == "auto" and torch.cuda.is_available() else device
        if self.device == "auto":
            self.device = "cpu"
        self.batch_size = batch_size
        if anchor_records is None:
            self.anchors, self.route_ids, content_hash = load_route_examples(content_root)
        else:
            self.anchors = [str(item["text"]) for item in anchor_records]
            self.route_ids = [str(item.get("ownerId", item.get("routeId", ""))) for item in anchor_records]
            if not self.anchors or len(self.anchors) != len(self.route_ids) or not source_hash:
                raise ValueError("Explicit semantic anchor records require texts, owner IDs and a source hash")
            content_hash = source_hash
        self.route_count = len(set(self.route_ids))
        revision = GTE_REVISION if "gte-multilingual-base" in self.model_name else None
        cache_key = hashlib.sha256(f"{self.model_name}:{revision}:{content_hash}:v2".encode()).hexdigest()[:16]
        self.cache_path = cache_root / f"{cache_key}.npz"

        if self.device == "cuda":
            torch.cuda.reset_peak_memory_stats()

        started = time.perf_counter()
        kwargs = {"dtype": torch.float16} if self.device == "cuda" else {}
        if revision:
            kwargs["code_revision"] = GTE_CODE_REVISION
        self.model = SentenceTransformer(
            self.model_name,
            device=self.device,
            model_kwargs=kwargs,
            revision=revision,
            trust_remote_code=revision is not None,
        )
        self.model_load_ms = elapsed_ms(started)

        started = time.perf_counter()
        self.cache_hit = self.cache_path.exists()
        if self.cache_hit:
            with np.load(self.cache_path, allow_pickle=False) as cached:
                self.vectors = cached["vectors"]
        else:
            self.vectors = self._encode(self.anchors, query=False)
            self.cache_path.parent.mkdir(parents=True, exist_ok=True)
            np.savez_compressed(self.cache_path, vectors=self.vectors)
        self.index_ms = elapsed_ms(started)

    def run(self, text: str, runs: int = 10, minimum_similarity: float = 0.45) -> tuple[list[RouteScore], BenchmarkTiming]:
        if not text.strip():
            raise ValueError("Input text is empty.")
        if runs < 1:
            raise ValueError("runs must be at least 1.")

        started = time.perf_counter()
        query = self._encode([text], query=True)[0]
        cold_query_ms = elapsed_ms(started)

        query_times: list[float] = []
        scoring_times: list[float] = []
        results: list[RouteScore] = []
        for _ in range(runs):
            started = time.perf_counter()
            query = self._encode([text], query=True)[0]
            query_times.append(elapsed_ms(started))

            started = time.perf_counter()
            results = score_routes(query, self.vectors, self.route_ids, self.anchors, minimum_similarity)
            scoring_times.append(elapsed_ms(started))

        peak_vram = torch.cuda.max_memory_allocated() / 1_048_576 if self.device == "cuda" else 0.0
        timing = BenchmarkTiming(
            model=self.model_name,
            device=self.device,
            anchors=len(self.anchors),
            routes=self.route_count,
            cache_hit=self.cache_hit,
            model_load_ms=self.model_load_ms,
            index_ms=self.index_ms,
            cold_query_ms=cold_query_ms,
            warm_query_median_ms=statistics.median(query_times),
            warm_query_p95_ms=percentile(query_times, 0.95),
            scoring_median_ms=statistics.median(scoring_times),
            peak_vram_mb=peak_vram,
        )
        return results, timing

    def rank_many(self, texts: list[str], minimum_similarity: float = 0.45) -> tuple[list[list[RouteScore]], float]:
        if not texts or any(not text.strip() for text in texts):
            raise ValueError("Evaluation texts must not be empty.")
        started = time.perf_counter()
        queries = self._encode(texts, query=True)
        embedding_ms_per_text = elapsed_ms(started) / len(texts)
        return [
            score_routes(query, self.vectors, self.route_ids, self.anchors, minimum_similarity)
            for query in queries
        ], embedding_ms_per_text

    def close(self) -> None:
        del self.model
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

    def _encode(self, texts: list[str], query: bool) -> np.ndarray:
        prepared = prepare_texts(self.model_name, texts, query)
        vectors = self.model.encode(
            prepared,
            batch_size=self.batch_size,
            convert_to_numpy=True,
            normalize_embeddings=True,
            show_progress_bar=len(prepared) > self.batch_size,
        )
        synchronize(self.device)
        return vectors.astype(np.float32, copy=False)


def prepare_texts(model_name: str, texts: list[str], query: bool) -> list[str]:
    if "multilingual-e5" in model_name:
        prefix = "query" if query else "passage"
        return [f"{prefix}: {text}" for text in texts]
    if "Qwen3-Embedding" in model_name and query:
        return [f"Instruct: {QWEN_INSTRUCTION}\nQuery: {text}" for text in texts]
    return texts


def synchronize(device: str) -> None:
    if device == "cuda":
        torch.cuda.synchronize()


def elapsed_ms(started: float) -> float:
    return (time.perf_counter() - started) * 1000


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, round((len(ordered) - 1) * fraction))]
