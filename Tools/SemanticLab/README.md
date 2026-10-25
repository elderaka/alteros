# AlterOS Semantic Lab

## Installed SQL sentence gate

The older Playground Generic-JSON mode is **not** an acceptance gate for new
SQL Topic/Scenario entries. For a real sentence-to-selected-Scenario check, run
these commands from PowerShell 7 at the repository root (local cached GTE only;
no provider, profile, SQL writes, or Unity close required):

Single-command Playground entry:

```powershell
.\Tools\TopicPlayground\topic-playground.ps1 -Query "Where is the tv?" -ExpectedTopicId topic.locate_tv
```

Equivalent two-stage form (keeps encoded queries available for a fast rerun):

```powershell
$probe = Join-Path $env:TEMP 'alteros-route-probe.json'
.\Tools\SemanticLab\.venv\Scripts\python.exe Tools/SemanticLab/encode_route_probes.py --query "Where is the tv?" --expected-topic topic.locate_tv --output $probe
.\Tools\ContentData\Check-ContentData.ps1 -Group ProbeRoutes -ArtifactPath Assets/StreamingAssets/Content/content.sqlite3 -ManifestPath $probe
```

Repeat `--query` and `--expected-topic` for a batch, or omit expectations to inspect
arbitrary sentences. The encoder uses the pinned local model. The gate opens the
exact installed release read-only and calls production `RankRoutes`, conversation
catalogue/candidate eligibility, local planner and pinned Scenario flow resolution.
JSON includes release/index identity, scores, availability, selected Topic/Scenario
and pass/fail; mismatched expectations exit nonzero. It does not execute physical
actions or prove the Unity/Sentis encoder, animation or presentation works.

A standalone embedding benchmark GUI and CLI. It reads the real Generic dialogue JSON, embeds routing examples, ranks routes, and reports model/query/scoring time plus peak CUDA memory. It does not run the game's confidence fallback, time eligibility, authored variant selection, or node traversal.

For the game's actual authored Topic path, use the Unity Topic Playground. It shares route confidence, time-window eligibility, stable variant selection, and canonical dialogue traversal with runtime code. Its only synthetic context input is local hour; stat gates are not implemented.

Open the Editor window from `AlterOS > Dialogue > Topic Playground`. For a headless scenario, close the AlterOS Unity Editor first, then run:

```powershell
.\Tools\TopicPlayground\topic-playground.ps1 -Scenario .\Tools\TopicPlayground\sample-scenario.json
```

The batch command launches Unity 6000.4.4f1 without graphics or a room scene and prints one JSON result. Scenario fields are `query` (required), `localHour` (`-1` for unknown, otherwise `0`-`23`), optional `expectedRouteId`, and optional ordered `steps` (`{"action":"continue"}` or `{"action":"choose","index":0}`; choice indexes are zero-based). Leave `expectedRouteId` empty for exploratory/silver probes rather than treating an unreviewed label as golden. The Editor window shows route score, best similarity, support count and fallback reason; explicit threshold explanation and same-variant restart are still gaps. The command and sample are implemented but the batch run and window/CLI parity have not yet been verified.

## Run

From the repository root:

```powershell
uv sync --project Tools/SemanticLab
uv run --project Tools/SemanticLab python Tools/SemanticLab/semantic_lab.py --gui
```

Agent-friendly CLI:

```powershell
uv run --project Tools/SemanticLab python Tools/SemanticLab/semantic_lab.py `
  --model e5-small --model qwen-0.6b `
  --query "ngl im kinda tired" --runs 20 --top 10 --json
```

Run the balanced silver evaluation against every Topic Dialogue route:

```powershell
uv run --project Tools/SemanticLab python Tools/SemanticLab/semantic_lab.py `
  --model gte-multilingual-base --evaluate
```

Export and convert the pinned GTE encoder for Unity/Sentis (development machine only):

```powershell
uv sync --project Tools/SemanticLab --extra export
uv run --project Tools/SemanticLab --extra export python Tools/SemanticLab/export_gte_onnx.py `
  --output Tools/SemanticLab/gte-multilingual-base.onnx
uv run --project Tools/SemanticLab --extra export python Tools/SemanticLab/convert_onnx_fp16.py `
  Tools/SemanticLab/gte-multilingual-base.onnx `
  Assets/Resources/AlterOS/Semantics/Models/gte-multilingual-base-fp16.onnx
uv run --project Tools/SemanticLab python Tools/SemanticLab/bake_gte_anchors.py `
  --output Assets/Resources/AlterOS/Semantics/Models/gte-anchors-vectors.bytes `
  --metadata Assets/Resources/AlterOS/Semantics/Models/gte-anchors-metadata.json
```

When the Unity project is open, the same bake can be started from the main toolbar: `GTE` → `Rebake anchors (auto device)`, `CPU`, or `CUDA`. The toolbar runs the baker asynchronously and refreshes the two generated Unity assets when it completes. `Reimport baked anchor assets` is available when the files already exist and only need to be imported again.

Models are downloaded on first use. Anchor embeddings are cached under `.semantic-cache`; delete that directory only when you intentionally want to remeasure index construction. `--top 0` prints every route, and `--batch-size 8` is the conservative Qwen setting for a 4 GB GTX 1650.

The displayed route score combines its three nearest authored examples. `best` is the closest individual example; `support` is how many of those three clear `--minimum`; winner margin is the gap between first and second place. These are diagnostics, not final production acceptance thresholds.

## GTX 1650 baseline

Measured 2026-09-03 on a 4 GB NVIDIA GeForce GTX 1650, driver 592.82, CUDA 13.0, FP16, 678 anchors, and 57 routes:

| Model | Warm query p50/p95 | Route scoring | Peak VRAM | First index build |
| --- | ---: | ---: | ---: | ---: |
| Qwen3-Embedding-0.6B, batch 8 | 223.2 / 224.9 ms | 1.275 ms | 1153 MB | 27.3 s |
| multilingual-e5-small, batch 16 | 16.5 / 18.7 ms | 0.729 ms | 236 MB | 3.0 s |
| gte-multilingual-base, batch 8 | 57.1 / 71.9 ms | 1.260 ms | 612 MB | 5.5 s |

Cached model loading was about 11 seconds for E5/Qwen and 6.1 seconds for GTE on this machine. The first query after startup varied from 0.24 to 1.61 seconds, so production use should warm the model once before accepting input.

All three models routed `ngl im kinda tired` to `player_tired`. None reliably routed the English/Indonesian code-switch `gue capek banget today` from the current English-only anchors. Add representative multilingual/slang evaluation phrases before choosing a production model.

## Silver evaluation

`silver_cases.json` contains 114 agent-authored unseen probes: one English and one colloquial Indonesian example for each of the 57 routes. They are provisional labels, deliberately separate from routing anchors, with zero exact anchor overlap.

| Model | Top-1 | Top-3 | English | Indonesian | Batch embedding | Peak VRAM |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| gte-multilingual-base | **80.7%** | **90.4%** | 82.5% | **78.9%** | 14–18 ms/text | 617 MB |
| Qwen3-Embedding-0.6B | 75.4% | 87.7% | **84.2%** | 66.7% | 129.7 ms/text | 1184 MB |
| multilingual-e5-small | 62.3% | 83.3% | 59.6% | 64.9% | **3.5 ms/text** | 236 MB |

GTE's supported-neighbor score reached 80.7% top-1; plain nearest-anchor routing reached 79.8%. The remaining errors cluster around missing Indonesian slang, overlapping intentions (`ask_status`/`ask_activity`, `player_stressed`/`player_tired`), deterministic inputs such as punctuation and keyboard mash, and repetition routes that should be context-gated. The silver set is a development compass, not a publishable model benchmark.

[GTE multilingual base](https://huggingface.co/Alibaba-NLP/gte-multilingual-base) requires repository-provided custom model code. Both its model revision and the referenced `Alibaba-NLP/new-impl` code revision are pinned in `semantic_router.py`; Transformers stays on the compatible 4.x line.
