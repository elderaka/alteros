from __future__ import annotations

import asyncio
import json
import queue
from pathlib import Path
from typing import Any

from alteros_companion.scenario_contracts import ScenarioHeadContentV4, ScenarioMainContentV4
from alteros_companion.scenario_generation import ScenarioStageProviderResult
from alteros_companion.__main__ import run_stdio
from alteros_companion.contracts import TimingV2, UsageV2


ROOT = Path(__file__).resolve().parents[3]
FIXTURES = ROOT / "Tools" / "ScenarioRuntime" / "Fixtures" / "v4"


def _line(message: dict[str, Any]) -> bytes:
    return json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8") + b"\n"


def _turn() -> dict[str, Any]:
    from alteros_companion.contracts import parse_envelope
    from alteros_companion.scenario_contracts import ScenarioRuntimeSnapshotV4, ScenarioTurnPayloadV4

    fixture = ROOT / "Tools" / "Conversation" / "Fixtures" / "v2" / "turn-valid.json"
    conversation = parse_envelope(fixture.read_bytes()).payload
    payload = ScenarioTurnPayloadV4(
        turnId="turn-stdio-1", turnVersion=1, operation="begin", onlineEnabled=True,
        conversation=conversation,
        memory=None,
        runtime=ScenarioRuntimeSnapshotV4(
            scenarioId="", runId="run-stdio-1", turnId="turn-stdio-1", contextRevision=1,
            checkpointStepId="", knownTopicIds=["cats"], knownIntentIds=[],
            allowedExpressions=["gentle", "thinking", "happy"], capabilities=[], objects=[], waitConditions=[],
        ),
        answer=None,
    )
    return {"protocolVersion": 4, "type": "scenario_turn", "requestId": "turn-stdio-1",
            "payload": payload.model_dump(mode="json")}


def _provider_result(content: Any) -> ScenarioStageProviderResult:
    return ScenarioStageProviderResult(content=content,
        usage=UsageV2(inputTokens=2, outputTokens=3, totalTokens=5, costUsd=None),
        timing=TimingV2(providerMs=1, totalMs=1))


class _QueuedInput:
    def __init__(self) -> None:
        self.lines: queue.Queue[bytes] = queue.Queue()

    def feed(self, line: bytes) -> None:
        self.lines.put(line)

    def readline(self, size: int = -1) -> bytes:
        line = self.lines.get(timeout=5)
        return line if size < 0 else line[:size]

    def close(self) -> None:
        self.feed(b"")


class _QueuedOutput:
    def __init__(self) -> None:
        self.data = bytearray()
        self.one_message = asyncio.Event()
        self.four_messages = asyncio.Event()
        self.two_messages = asyncio.Event()

    def write(self, value: bytes) -> int:
        self.data.extend(value)
        count = self.data.count(b"\n")
        if count >= 1:
            self.one_message.set()
        if count >= 2:
            self.two_messages.set()
        if count >= 4:
            self.four_messages.set()
        return len(value)

    def flush(self) -> None:
        pass


class _Provider:
    def __init__(self) -> None:
        head = json.loads((FIXTURES / "head-valid.json").read_text(encoding="utf-8"))["payload"]
        main = json.loads((FIXTURES / "main-cats.json").read_text(encoding="utf-8"))["payload"]
        self.results = {"head": ScenarioHeadContentV4(beats=head["beats"]),
                        "main": ScenarioMainContentV4(scenario=main["scenario"],
                            segmentStepId=main["segmentStepId"], topicOffers=main["topicOffers"],
                            memoryProposals=[], questionProposal=None, usedCallbackIds=[])}
        self.calls: list[str] = []

    async def generate_scenario_stage(self, messages, credential, stage):
        self.calls.append(stage)
        return _provider_result(self.results[stage])


class _BarrierProvider(_Provider):
    def __init__(self) -> None:
        super().__init__()
        self.both_started = asyncio.Event()
        self.release = {"head": asyncio.Event(), "main": asyncio.Event()}
        self.cancelled: set[str] = set()

    async def generate_scenario_stage(self, messages, credential, stage):
        self.calls.append(stage)
        if len(self.calls) == 2:
            self.both_started.set()
        try:
            await self.release[stage].wait()
        except asyncio.CancelledError:
            self.cancelled.add(stage)
            raise
        return _provider_result(self.results[stage])


def test_v4_bootstrap_advertises_scenario_capability_and_streams_both_stages(tmp_path: Path) -> None:
    async def run() -> tuple[list[dict[str, Any]], _Provider]:
        provider = _Provider()
        source, sink = _QueuedInput(), _QueuedOutput()
        bootstrap = {"protocolVersion": 4, "type": "bootstrap", "requestId": "boot-v4",
                     "payload": {"apiKey": "synthetic-test-key", "playerId": "player-test",
                                 "memoryRoot": str(tmp_path / "Memory" / "player-memory.sqlite3")}}
        turn = _turn()
        task = asyncio.create_task(run_stdio(source, sink, provider=provider,
            persona_path=str(ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json")))
        source.feed(_line(bootstrap))
        await asyncio.wait_for(sink.one_message.wait(), timeout=2)
        source.feed(_line(turn))
        try:
            await asyncio.wait_for(sink.four_messages.wait(), timeout=2)
        except TimeoutError:
            if task.done():
                await task
            raise AssertionError(bytes(sink.data))
        source.feed(_line({"protocolVersion": 4, "type": "shutdown", "requestId": "shutdown-v4", "payload": {}}))
        assert await asyncio.wait_for(task, timeout=2) == 0
        return [json.loads(line) for line in bytes(sink.data).splitlines()], provider

    messages, provider = asyncio.run(run())
    ready = messages[0]
    assert ready["protocolVersion"] == 4 and ready["type"] == "ready"
    assert ready["payload"]["capabilities"]["scenario-turn-v1"] is True
    assert {message["payload"]["stage"] for message in messages if message["type"] == "scenario_stage"} == {"head", "main"}
    assert messages[-1]["type"] == "scenario_complete"
    assert provider.calls == ["head", "main"]


def test_v4_bootstrap_keeps_keyless_v3_memory_commands_available(tmp_path: Path) -> None:
    async def run() -> list[dict[str, Any]]:
        source, sink = _QueuedInput(), _QueuedOutput()
        bootstrap = {"protocolVersion": 4, "type": "bootstrap", "requestId": "boot-v4-memory",
                     "payload": {"apiKey": "", "playerId": "player-test",
                                 "memoryRoot": str(tmp_path / "Memory" / "player-memory.sqlite3")}}
        consent = {"protocolVersion": 3, "type": "memory", "requestId": "memory-consent-read",
                   "payload": {"operation": "consent", "playerId": "player-test", "consentRevision": 0,
                               "nowUtc": "2026-10-20T12:00:00Z", "data": {"enabled": None}}}
        task = asyncio.create_task(run_stdio(source, sink, provider=_Provider(),
            persona_path=str(ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json")))
        source.feed(_line(bootstrap))
        await asyncio.wait_for(sink.one_message.wait(), timeout=2)
        source.feed(_line(consent))
        await asyncio.wait_for(sink.two_messages.wait(), timeout=2)
        source.feed(_line({"protocolVersion": 4, "type": "shutdown", "requestId": "shutdown-v4-memory", "payload": {}}))
        assert await asyncio.wait_for(task, timeout=2) == 0
        return [json.loads(line) for line in bytes(sink.data).splitlines()]

    messages = asyncio.run(run())
    assert messages[0]["protocolVersion"] == 4 and messages[0]["type"] == "ready"
    assert messages[1]["protocolVersion"] == 3 and messages[1]["type"] == "result"
    assert messages[1]["payload"]["status"] == "accepted"
    assert messages[1]["payload"]["consent"]["enabled"] is False


def test_keyless_v4_bootstrap_keeps_scenario_local_and_does_not_call_provider(tmp_path: Path) -> None:
    async def run() -> tuple[list[dict[str, Any]], _Provider]:
        provider = _Provider()
        source, sink = _QueuedInput(), _QueuedOutput()
        bootstrap = {"protocolVersion": 4, "type": "bootstrap", "requestId": "boot-v4",
                     "payload": {"apiKey": "", "playerId": "player-test",
                                 "memoryRoot": str(tmp_path / "Memory" / "player-memory.sqlite3")}}
        turn = _turn()
        turn["payload"]["onlineEnabled"] = False
        task = asyncio.create_task(run_stdio(source, sink, provider=provider,
            persona_path=str(ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json")))
        source.feed(_line(bootstrap))
        await asyncio.wait_for(sink.one_message.wait(), timeout=2)
        source.feed(_line(turn))
        await asyncio.wait_for(sink.two_messages.wait(), timeout=2)
        source.feed(_line({"protocolVersion": 4, "type": "shutdown", "requestId": "shutdown-v4", "payload": {}}))
        assert await asyncio.wait_for(task, timeout=2) == 0
        return [json.loads(line) for line in bytes(sink.data).splitlines()], provider

    messages, provider = asyncio.run(run())
    assert messages[0]["payload"]["capabilities"]["scenario-turn-v1"] is True
    assert provider.calls == []
    complete = next(message for message in messages if message["type"] == "scenario_complete")
    assert complete["payload"]["head"]["status"] == "skipped"
    assert complete["payload"]["terminalReason"] == "failed"


def test_v4_cancel_and_eof_drain_both_provider_tasks(tmp_path: Path) -> None:
    async def exercise(use_cancel: bool) -> tuple[_BarrierProvider, list[dict[str, Any]]]:
        provider = _BarrierProvider()
        source, sink = _QueuedInput(), _QueuedOutput()
        bootstrap = {"protocolVersion": 4, "type": "bootstrap", "requestId": "boot-v4",
                     "payload": {"apiKey": "synthetic-test-key", "playerId": "player-test",
                                 "memoryRoot": str(tmp_path / ("cancel" if use_cancel else "eof") / "Memory" / "player-memory.sqlite3")}}
        task = asyncio.create_task(run_stdio(source, sink, provider=provider,
            persona_path=str(ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json")))
        source.feed(_line(bootstrap))
        await asyncio.wait_for(sink.one_message.wait(), timeout=2)
        source.feed(_line(_turn()))
        await asyncio.wait_for(provider.both_started.wait(), timeout=2)
        if use_cancel:
            source.feed(_line({"protocolVersion": 4, "type": "cancel", "requestId": "turn-stdio-1", "payload": {}}))
            source.feed(_line({"protocolVersion": 4, "type": "shutdown", "requestId": "shutdown-v4", "payload": {}}))
            assert await asyncio.wait_for(task, timeout=2) == 0
        else:
            source.feed(b"")
            assert await asyncio.wait_for(task, timeout=2) == 0
        return provider, [json.loads(line) for line in bytes(sink.data).splitlines()]

    cancelled_provider, cancelled_messages = asyncio.run(exercise(True))
    eof_provider, eof_messages = asyncio.run(exercise(False))
    assert cancelled_provider.cancelled == {"head", "main"}
    assert eof_provider.cancelled == {"head", "main"}
    terminal = next(message for message in cancelled_messages if message["type"] == "scenario_complete")
    assert terminal["payload"]["terminalReason"] == "cancelled"
    assert not any(message["type"] == "scenario_stage" for message in eof_messages)
