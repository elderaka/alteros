from __future__ import annotations

import json
import sys
import time

from pathlib import Path


mode = sys.argv[1]
if mode == "restart-once":
    marker = Path(sys.argv[2])
    if not marker.exists():
        marker.write_text("first-start", encoding="utf-8")
        raise SystemExit(2)
bootstrap_line = sys.stdin.buffer.readline(256 * 1024 + 2)
bootstrap = json.loads(bootstrap_line)
if mode == "stderr-secret":
    sys.stderr.write(bootstrap["payload"]["apiKey"] + "\n")
    sys.stderr.flush()
if mode == "persona-unavailable":
    failure = {
        "protocolVersion": 2,
        "type": "error",
        "requestId": bootstrap["requestId"],
        "payload": {"code": "persona_unavailable", "retryable": False},
    }
    sys.stdout.buffer.write(json.dumps(failure, separators=(",", ":")).encode("utf-8") + b"\n")
    sys.stdout.buffer.flush()
    raise SystemExit(0)
if mode == "overlong":
    sys.stdout.buffer.write(b"x" * (256 * 1024 + 1) + b"\n")
    sys.stdout.buffer.flush()
    raise SystemExit(0)
if mode == "incomplete":
    sys.stdout.buffer.write(b"{\"protocolVersion\":2")
    sys.stdout.buffer.flush()
    raise SystemExit(0)

ready = {
    "protocolVersion": 1 if mode == "wrong-version" else (3 if mode.startswith("v3-") else 2),
    "type": "ready",
    "requestId": bootstrap["requestId"],
    "payload": ({"runtimeVersion": "test", "modelId": "test-model" if mode in {"v3-turn", "v3-turn-deferred"} else "",
                 "capabilities": {"memory": True, "generation": mode in {"v3-turn", "v3-turn-deferred"}}}
                if mode.startswith("v3-") else {"runtimeVersion": "test", "modelId": "test-model"}),
}
sys.stdout.buffer.write(json.dumps(ready, separators=(",", ":")).encode("utf-8") + b"\n")
sys.stdout.buffer.flush()
if mode == "stdin-closed":
    raise SystemExit(0)

for raw in sys.stdin:
    message = json.loads(raw)
    if message["type"] == "shutdown":
        raise SystemExit(0)
    if mode == "v3-hold-memory" and message["type"] == "memory":
        continue
    if mode == "v3-hold-memory" and message["type"] == "cancel":
        Path(sys.argv[2]).write_text(message["requestId"], encoding="utf-8")
        continue
    if mode == "v3-keyless" and message["type"] == "memory":
        payload = message["payload"]
        if payload["operation"] == "consent":
            enabled = payload["data"]["enabled"]
            body = {"status": "accepted", "consent": {"playerId": payload["playerId"],
                    "enabled": False if enabled is None else enabled,
                    "revision": payload["consentRevision"] if enabled is None else payload["consentRevision"] + 1}}
        elif payload["operation"] == "onboarding_read":
            body = {"status": "accepted", "stepId": "interest", "completed": False}
        else:
            body = {"status": "rejected", "reasonCode": "unsupported_operation"}
        result = {"protocolVersion": 3, "type": "result", "requestId": message["requestId"], "payload": body}
        sys.stdout.buffer.write(json.dumps(result, separators=(",", ":")).encode("utf-8") + b"\n")
        sys.stdout.buffer.flush()
        continue
    if mode in {"v3-turn", "v3-turn-deferred"} and message["type"] == "memory":
        payload = message["payload"]
        operation = payload["operation"]
        state = (json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
                 if mode == "v3-turn-deferred" else {"enabled": True, "revision": 1, "suppressedSourceIds": []})
        if operation == "consent":
            data = {"status": "accepted", "consent": {"playerId": payload["playerId"],
                    "enabled": state["enabled"], "revision": state["revision"]}}
        elif operation == "source_sync":
            data = {"status": "accepted", "lastSequence": payload["data"]["throughSequence"], "count": len(payload["data"]["events"])}
            if mode == "v3-turn-deferred":
                with Path(str(sys.argv[2]) + ".synced-sources.jsonl").open("a", encoding="utf-8") as synced:
                    for event in payload["data"]["events"]:
                        synced.write(event["evidence"]["sourceId"] + "\n")
        elif operation == "context":
            data = {"status": "accepted", "context": {"core": [], "current": [], "recalled": [],
                    "suppressedSourceIds": state["suppressedSourceIds"]}}
        else:
            data = {"status": "rejected", "reasonCode": "unsupported_operation"}
        response = {"protocolVersion": 3, "type": "result", "requestId": message["requestId"], "payload": data}
        sys.stdout.buffer.write(json.dumps(response, separators=(",", ":")).encode("utf-8") + b"\n")
        sys.stdout.buffer.flush()
        continue
    if message["type"] == "turn" and mode in {"v3-turn", "v3-turn-deferred"}:
        if mode == "v3-turn-deferred":
            control_path = Path(sys.argv[2])
            Path(str(control_path) + ".turn.json").write_text(json.dumps(message, ensure_ascii=False), encoding="utf-8")
            Path(str(control_path) + ".turn-started").write_text("started", encoding="utf-8")
            release_path = Path(str(control_path) + ".turn-release")
            deadline = time.monotonic() + 15
            while not release_path.exists() and time.monotonic() < deadline:
                time.sleep(0.01)
        conversation_result = {"judgment": {"kind": "clarify", "topicId": "", "referencedTopicIds": [],
                                "reply": {"beats": [{"id": "beat-1", "text": "そうだね。", "displayText": "I see.",
                                                     "language": "japanese", "expression": ""}]}, "suggestedReplies": []},
                               "usage": {"inputTokens": 1, "outputTokens": 1, "totalTokens": 2, "costUsd": None},
                               "timing": {"providerMs": 1, "totalMs": 2}}
        line = json.dumps({"protocolVersion": 3, "type": "result", "requestId": message["requestId"],
                           "payload": {"conversation": conversation_result}}, ensure_ascii=False, separators=(",", ":")).encode("utf-8") + b"\n"
        sys.stdout.buffer.write(line)
        sys.stdout.buffer.flush()
    elif message["type"] == "turn":
        if mode == "exit-mid-turn":
            raise SystemExit(2)
        if mode == "hold":
            continue
        fixture = Path(__file__).resolve().parents[2] / "Conversation" / "Fixtures" / "v2" / "result-valid.json"
        response = json.loads(fixture.read_text(encoding="utf-8"))
        response["requestId"] = message["requestId"]
        line = json.dumps(response, ensure_ascii=False, separators=(",", ":")).encode("utf-8") + b"\n"
        sys.stdout.buffer.write(line)
        sys.stdout.buffer.flush()
        if mode == "duplicate-result":
            sys.stdout.buffer.write(line)
            sys.stdout.buffer.flush()
