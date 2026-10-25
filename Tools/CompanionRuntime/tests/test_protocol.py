import json
import os
import subprocess
import sys
import asyncio
import io
import queue
from pathlib import Path

import pytest

from alteros_companion.contracts import ContractError
from alteros_companion.protocol import decode_line, encode_message
from alteros_companion.provider import ProviderResult


def test_protocol_round_trips_utf8_envelope_as_one_compact_line() -> None:
    message = {
        "protocolVersion": 2,
        "type": "ready",
        "requestId": "boot-1",
        "payload": {"runtimeVersion": "0.1.0", "modelId": "deepseek/deepseek-v4.1-flash"},
    }

    line = encode_message(message)

    assert line.endswith(b"\n")
    assert b"\n" not in line[:-1]
    assert json.loads(line.decode("utf-8")) == message
    decoded = decode_line(line)
    assert decoded.type == "ready"
    assert decoded.requestId == "boot-1"
    assert decoded.payload.runtimeVersion == "0.1.0"


@pytest.mark.parametrize("line", [
    pytest.param(b"{}", id="missing-newline"),
    pytest.param(b"{}\n\n", id="extra-line"),
    pytest.param(b"\xff\n", id="invalid-utf8"),
    pytest.param(b" " * (256 * 1024 + 1) + b"\n", id="overlong"),
])
def test_protocol_rejects_incomplete_malformed_or_overlong_lines(line: bytes) -> None:
    with pytest.raises(ContractError):
        decode_line(line)


def test_protocol_rejects_a_valid_envelope_of_an_unsupported_version() -> None:
    with pytest.raises(ContractError):
        encode_message({"protocolVersion": 1, "type": "ready", "requestId": "boot-1", "payload": {"runtimeVersion": "0.1.0", "modelId": "x"}})


def test_stdio_entrypoint_bootstraps_and_shuts_down_without_logging_credentials() -> None:
    package_root = Path(__file__).resolve().parents[1]
    bootstrap = encode_message({"protocolVersion": 2, "type": "bootstrap", "requestId": "boot-1", "payload": {"apiKey": "synthetic-key-marker"}})
    shutdown = encode_message({"protocolVersion": 2, "type": "shutdown", "requestId": "stop-1", "payload": {}})
    environment = dict(os.environ)
    environment["PYTHONPATH"] = str(package_root / "src")
    environment["OPENROUTER_API_KEY"] = "inherited-environment-marker"

    result = subprocess.run(
        [sys.executable, "-m", "alteros_companion"],
        input=bootstrap + shutdown,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=package_root,
        env=environment,
        timeout=5,
        check=False,
    )

    assert result.returncode == 0
    assert result.stderr == b""
    assert b"synthetic-key-marker" not in result.stdout
    assert b"inherited-environment-marker" not in result.stdout
    ready = decode_line(result.stdout)
    assert ready.type == "ready"
    assert ready.requestId == "boot-1"


def test_stdio_entrypoint_exits_cleanly_when_parent_closes_input():
    package_root = Path(__file__).resolve().parents[1]
    result = subprocess.run(
        [sys.executable, "-m", "alteros_companion"],
        input=b"",
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=package_root,
        env={**os.environ, "PYTHONPATH": str(package_root / "src")},
        timeout=5,
        check=False,
    )

    assert result.returncode == 0
    assert result.stdout == b""
    assert result.stderr == b""


def test_stdio_entrypoint_bootstraps_and_shuts_down_without_provider_call() -> None:
    package_root = Path(__file__).resolve().parents[1]
    bootstrap = encode_message({"protocolVersion": 2, "type": "bootstrap", "requestId": "boot-1", "payload": {"apiKey": "synthetic-key-marker"}})
    shutdown = encode_message({"protocolVersion": 2, "type": "shutdown", "requestId": "stop-1", "payload": {}})

    result = subprocess.run(
        [sys.executable, "-m", "alteros_companion"],
        input=bootstrap + shutdown,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=package_root,
        env={**os.environ, "PYTHONPATH": str(package_root / "src")},
        timeout=5,
        check=False,
    )

    assert result.returncode == 0
    assert result.stderr == b""
    assert b"synthetic-key-marker" not in result.stdout
    lines = result.stdout.splitlines()
    assert len(lines) == 1
    ready = decode_line(lines[0] + b"\n")
    assert ready.type == "ready"


def test_stdio_turn_runs_the_injected_graph_and_returns_a_validated_result() -> None:
    from alteros_companion.__main__ import run_stdio
    from alteros_companion.contracts import ConversationJudgmentV2, TimingV2, UsageV2

    fixture_root = Path(__file__).resolve().parents[1].parent / "Conversation" / "Fixtures" / "v2"
    bootstrap = encode_message({"protocolVersion": 2, "type": "bootstrap", "requestId": "boot-1", "payload": {"apiKey": "synthetic-key-marker"}})
    turn = encode_message(json.loads((fixture_root / "turn-valid.json").read_text(encoding="utf-8")))
    judgment_data = json.loads((fixture_root / "result-valid.json").read_text(encoding="utf-8"))["payload"]["judgment"]
    fake = _FakeProvider(ProviderResult(
        judgment=ConversationJudgmentV2.model_validate(judgment_data, strict=True),
        usage=UsageV2(inputTokens=8, outputTokens=4, totalTokens=12, costUsd=None),
        timing=TimingV2(providerMs=5, totalMs=6),
    ))
    input_stream = _QueuedInput()
    output_stream = _QueuedOutput()

    async def run_turn():
        task = asyncio.create_task(run_stdio(input_stream, output_stream, provider=fake))
        input_stream.feed(bootstrap)
        input_stream.feed(turn)
        await asyncio.wait_for(output_stream.two_messages.wait(), timeout=5)
        input_stream.feed(encode_message({"protocolVersion": 2, "type": "shutdown", "requestId": "stop-1", "payload": {}}))
        return await asyncio.wait_for(task, timeout=5)

    exit_code = asyncio.run(run_turn())

    messages = [decode_line(line + b"\n") for line in bytes(output_stream.data).splitlines()]
    assert exit_code == 0
    assert [message.type for message in messages] == ["ready", "result"]
    assert messages[1].payload.judgment.reply.beats[0].displayText == "Some days are like that."
    assert fake.calls == 1
    assert fake.credential == "synthetic-key-marker"
    assert b"synthetic-key-marker" not in output_stream.data


def test_stdio_cancel_cancels_the_one_in_flight_graph_without_emitting_a_late_result() -> None:
    from alteros_companion.__main__ import run_stdio

    fixture_root = Path(__file__).resolve().parents[1].parent / "Conversation" / "Fixtures" / "v2"
    bootstrap = encode_message({"protocolVersion": 2, "type": "bootstrap", "requestId": "boot-1", "payload": {"apiKey": "synthetic-key-marker"}})
    turn = encode_message(json.loads((fixture_root / "turn-valid.json").read_text(encoding="utf-8")))
    cancel = encode_message({"protocolVersion": 2, "type": "cancel", "requestId": "request-v2-1", "payload": {}})
    shutdown = encode_message({"protocolVersion": 2, "type": "shutdown", "requestId": "stop-1", "payload": {}})
    input_stream = _QueuedInput()
    output_stream = _QueuedOutput()
    provider = _BlockingProvider()

    async def run_cancel():
        task = asyncio.create_task(run_stdio(input_stream, output_stream, provider=provider))
        input_stream.feed(bootstrap)
        await asyncio.wait_for(output_stream.one_message.wait(), timeout=5)
        input_stream.feed(turn)
        await asyncio.wait_for(provider.started.wait(), timeout=5)
        input_stream.feed(cancel)
        input_stream.feed(shutdown)
        return await asyncio.wait_for(task, timeout=5)

    assert asyncio.run(run_cancel()) == 0
    assert provider.calls == 1
    assert provider.cancelled
    assert len(output_stream.data.splitlines()) == 1


class _FakeProvider:
    def __init__(self, result: ProviderResult):
        self.result = result
        self.calls = 0
        self.credential = None

    async def generate(self, messages, credential):
        self.calls += 1
        self.credential = credential
        return self.result


class _BlockingProvider:
    def __init__(self) -> None:
        self.calls = 0
        self.cancelled = False
        self.started = asyncio.Event()

    async def generate(self, messages, credential):
        self.calls += 1
        self.started.set()
        try:
            await asyncio.Future()
        except asyncio.CancelledError:
            self.cancelled = True
            raise


class _QueuedInput:
    def __init__(self) -> None:
        self._lines: queue.Queue[bytes] = queue.Queue()

    def feed(self, line: bytes) -> None:
        self._lines.put(line)

    def readline(self, size: int = -1) -> bytes:
        line = self._lines.get(timeout=5)
        return line if size < 0 else line[:size]

    def close(self) -> None:
        self.feed(b"")


class _QueuedOutput:
    def __init__(self) -> None:
        self.data = bytearray()
        self.one_message = asyncio.Event()
        self.two_messages = asyncio.Event()

    def write(self, value: bytes) -> int:
        self.data.extend(value)
        if self.data.count(b"\n") >= 1:
            self.one_message.set()
        if self.data.count(b"\n") >= 2:
            self.two_messages.set()
        return len(value)

    def flush(self) -> None:
        pass
