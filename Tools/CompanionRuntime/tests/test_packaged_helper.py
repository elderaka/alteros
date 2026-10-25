from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import tempfile

import pytest


def test_packaged_helper_keyless_memory_write_restart_and_read():
    configured = os.environ.get("ALTEROS_PACKAGED_HELPER", "").strip()
    if not configured:
        pytest.skip("Set ALTEROS_PACKAGED_HELPER to run the optional packaged-binary smoke test.")
    executable = Path(configured)
    assert executable.is_file()

    with tempfile.TemporaryDirectory(prefix="AlterOS memory path with spaces ") as root:
        memory_root = str(Path(root) / "Memory" / "player-memory.sqlite3")
        bootstrap = {"protocolVersion": 3, "type": "bootstrap", "requestId": "boot",
            "payload": {"apiKey": "", "playerId": "synthetic-player", "memoryRoot": memory_root}}
        save = {"protocolVersion": 3, "type": "memory", "requestId": "save", "payload": {
            "operation": "onboarding", "playerId": "synthetic-player", "consentRevision": 0,
            "nowUtc": "2026-10-18T12:00:00Z", "data": {"stepId": "interest", "completed": False}}}
        read = {"protocolVersion": 3, "type": "memory", "requestId": "read", "payload": {
            "operation": "onboarding_read", "playerId": "synthetic-player", "consentRevision": 0,
            "nowUtc": "2026-10-18T12:00:00Z", "data": {}}}
        shutdown = {"protocolVersion": 3, "type": "shutdown", "requestId": "stop", "payload": {}}

        def run(messages):
            completed = subprocess.run([str(executable)], input=b"".join(
                (json.dumps(message, separators=(",", ":")) + "\n").encode("utf-8") for message in messages),
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30, check=False)
            assert completed.returncode == 0
            assert completed.stderr == b""
            return [json.loads(line) for line in completed.stdout.splitlines()]

        first = run((bootstrap, save, shutdown))
        second = run((bootstrap, read, shutdown))

    assert first[0]["payload"]["capabilities"] == {"memory": True, "generation": False}
    assert first[1]["payload"]["status"] == "accepted"
    assert second[1]["payload"]["stepId"] == "interest"
    assert second[1]["payload"]["completed"] is False


def test_packaged_helper_supports_v4_scenario_readiness_without_a_key():
    configured = os.environ.get("ALTEROS_PACKAGED_HELPER", "").strip()
    if not configured:
        pytest.skip("Set ALTEROS_PACKAGED_HELPER to run the optional packaged-binary smoke test.")
    executable = Path(configured)
    assert executable.is_file()

    with tempfile.TemporaryDirectory(prefix="AlterOS scenario path with spaces ") as root:
        bootstrap = {"protocolVersion": 4, "type": "bootstrap", "requestId": "boot-v4",
            "payload": {"apiKey": "", "playerId": "synthetic-player",
                        "memoryRoot": str(Path(root) / "Memory" / "player-memory.sqlite3")}}
        shutdown = {"protocolVersion": 4, "type": "shutdown", "requestId": "stop-v4", "payload": {}}
        completed = subprocess.run([str(executable)], input=b"".join(
            (json.dumps(message, separators=(",", ":")) + "\n").encode("utf-8")
            for message in (bootstrap, shutdown)), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            timeout=30, check=False)

    assert completed.returncode == 0
    assert completed.stderr == b""
    messages = [json.loads(line) for line in completed.stdout.splitlines()]
    assert messages[0]["protocolVersion"] == 4
    assert messages[0]["type"] == "ready"
    assert messages[0]["payload"]["capabilities"] == {
        "memory": True, "generation": False, "scenario-turn-v1": True}
