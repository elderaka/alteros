from __future__ import annotations

import json
import sys
import time
from pathlib import Path


mode = sys.argv[1]
marker = Path(sys.argv[2]) if len(sys.argv) > 2 else None
bootstrap = json.loads(sys.stdin.buffer.readline(256 * 1024 + 2))
request_id = bootstrap["requestId"]
sys.stdout.buffer.write((json.dumps({
    "protocolVersion": 4, "type": "ready", "requestId": request_id,
    "payload": {"runtimeVersion": "test", "modelId": "test-model",
                "capabilities": {"memory": True, "generation": True, "scenario-turn-v1": mode != "missing-capability"}},
}, separators=(",", ":")) + "\n").encode())
sys.stdout.buffer.flush()

for raw in sys.stdin.buffer:
    message = json.loads(raw)
    if message["type"] == "shutdown":
        raise SystemExit(0)
    if message["type"] == "cancel":
        if marker:
            marker.write_text(message["requestId"], encoding="utf-8")
        continue
    if message["type"] == "memory" and mode == "memory-commands":
        request = message["payload"]
        sys.stdout.buffer.write((json.dumps({
            "protocolVersion": 3, "type": "result", "requestId": message["requestId"],
            "payload": {"status": "accepted", "consent": {
                "playerId": request["playerId"], "enabled": False, "revision": 0}},
        }, separators=(",", ":")) + "\n").encode())
        sys.stdout.buffer.flush()
        continue
    if message["type"] != "scenario_turn":
        continue

    turn_id = message["requestId"]
    version = message["payload"]["turnVersion"]
    assert message["protocolVersion"] == 4
    assert set(message["payload"]) == {"turnId", "turnVersion", "operation", "onlineEnabled", "conversation", "memory", "runtime", "answer"}
    assert message["payload"]["runtime"]["turnId"] == turn_id
    assert "apiKey" not in json.dumps(message["payload"])
    if mode == "oversized-stage":
        sys.stdout.buffer.write(b"x" * (256 * 1024 + 1) + b"\n")
        sys.stdout.buffer.flush()
        raise SystemExit(0)
    fixture_root = Path(__file__).resolve().parents[2] / "ScenarioRuntime" / "Fixtures" / "v4"
    for stage in ("head", "main"):
        if mode == "hold":
            continue
        result = json.loads((fixture_root / ("head-valid.json" if stage == "head" else "main-cats.json")).read_text(encoding="utf-8"))
        if mode == "wrong-version":
            result["protocolVersion"] = 3
        if mode == "bad-parent":
            result["payload"]["turnId"] = "other-parent"
        result["requestId"] = turn_id
        if mode != "bad-parent":
            result["payload"]["turnId"] = turn_id
        result["payload"]["turnVersion"] = version
        result["payload"]["stageId"] = turn_id[:112] + "-" + stage
        sys.stdout.buffer.write((json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8"))
        sys.stdout.buffer.flush()
        if mode == "duplicate-stage" and stage == "head":
            sys.stdout.buffer.write((json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8"))
            sys.stdout.buffer.flush()
            raise SystemExit(0)
        if stage == "head":
            time.sleep(0.2)
    if mode != "hold":
        terminal = {
            "protocolVersion": 4, "type": "scenario_complete", "requestId": turn_id,
            "payload": {"turnId": turn_id, "turnVersion": version,
                        "head": {"stage": "head", "status": "ready", "usage": {"inputTokens": 1, "outputTokens": 1, "totalTokens": 2, "costUsd": None}},
                        "main": {"stage": "main", "status": "ready", "usage": {"inputTokens": 1, "outputTokens": 1, "totalTokens": 2, "costUsd": None}},
                        "terminalReason": "completed"},
        }
        sys.stdout.buffer.write((json.dumps(terminal, separators=(",", ":")) + "\n").encode("utf-8"))
        sys.stdout.buffer.flush()
