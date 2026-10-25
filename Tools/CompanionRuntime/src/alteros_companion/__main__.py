"""Private stdio entry point for the managed AlterOS helper."""

from __future__ import annotations

import asyncio
import os
import sqlite3
import sys
from typing import BinaryIO, Any

from .contracts import ContractError
from .memory_contracts import MemoryConsent, MemoryOperationResult
from .memory_protocol import (
    MemoryBootstrapV3,
    MemoryCommandV3,
    MemoryEnvelopeV3,
    MemoryProtocolError,
    MemoryTurnDataV3,
    encode_memory_message,
)
from .memory_repository import MemoryRepository, MemoryStorageError
from .prompts import PromptSetupError, load_persona
from .protocol import decode_line, encode_message
from .provider import ProviderResult
from .scenario_generation import ScenarioTurnReceipt, generate_scenario_turn, scenario_receipt_message
from .scenario_protocol import ScenarioBootstrapEnvelopeV4, ScenarioControlEnvelopeV4, encode_scenario_message
from .scenario_contracts import ScenarioTurnEnvelopeV4


MAX_LINE_BYTES = 256 * 1024 + 1


async def _read_input(stream: BinaryIO, queue: asyncio.Queue[bytes]) -> None:
    while True:
        line = await asyncio.to_thread(stream.readline, MAX_LINE_BYTES + 1)
        await queue.put(line)
        if not line:
            return


def _write(stream: BinaryIO, message: dict[str, Any]) -> None:
    stream.write(encode_message(message))
    stream.flush()


def _write_memory(stream: BinaryIO, message: dict[str, Any]) -> None:
    stream.write(encode_memory_message(message))
    stream.flush()


def _write_scenario(stream: BinaryIO, message: dict[str, Any]) -> None:
    stream.write(encode_scenario_message(message))
    stream.flush()


def handle_memory_command(command: MemoryCommandV3, repository: MemoryRepository) -> dict[str, Any]:
    """Dispatch only explicit local memory operations; never invokes the provider."""
    if command.playerId != repository.player_id:
        return MemoryOperationResult(status="rejected", reasonCode="wrong_player").model_dump(mode="json")
    try:
        current = repository.get_consent()
    except (MemoryStorageError, OSError, sqlite3.Error):
        return MemoryOperationResult(status="error", reasonCode="memory_unavailable").model_dump(mode="json")
    read_only_consent = command.operation == "consent" and command.data.enabled is None
    if command.consentRevision != current.revision and not read_only_consent:
        return MemoryOperationResult(status="rejected", reasonCode="consent_revision_conflict").model_dump(mode="json")
    try:
        if command.operation == "consent":
            if command.data.enabled is None:
                return {"status": "accepted", "consent": current.model_dump(mode="json")}
            result = repository.set_consent(command.data.enabled, expected_revision=command.consentRevision,
                                            now_utc=command.nowUtc)
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "source_sync":
            result = repository.sync_sources(command.data.afterSequence,
                                             [(item.sequence, item.evidence) for item in command.data.events],
                                             current, command.nowUtc, through_sequence=command.data.throughSequence)
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "profile_set":
            result = repository.upsert(command.data.proposal, command.data.evidence, current, command.nowUtc)
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "confirm":
            result = repository.confirm(command.data.proposal, command.data.evidence,
                                        command.data.confirmation, current, command.nowUtc)
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "context":
            context = repository.read_context(command.data.query, current, command.nowUtc)
            return {"status": "accepted", "context": context.model_dump(mode="json")}
        if command.operation == "list":
            page = repository.list_page(command.data.cursor, command.data.limit,
                                         include_superseded=command.data.includeSuperseded)
            return {"status": page.status, "page": page.model_dump(mode="json")}
        if command.operation == "forget":
            result = repository.forget(command.data.memoryId, command.nowUtc)
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "onboarding":
            result = repository.save_onboarding_progress(command.data.stepId, command.data.completed, command.nowUtc)
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "onboarding_read":
            result = repository.get_onboarding_progress()
            return result.model_dump(mode="json", exclude_none=True)
        if command.operation == "question_ack":
            if not current.enabled:
                return MemoryOperationResult(status="rejected", reasonCode="memory_disabled").model_dump(mode="json")
            result = repository.record_question_ack(command.data.intent, command.data.sessionId,
                                                    command.data.eventId, command.data.presentedAtUtc)
            return result.model_dump(mode="json", exclude_none=True)
    except (MemoryStorageError, OSError, sqlite3.Error):
        return MemoryOperationResult(status="error", reasonCode="memory_unavailable").model_dump(mode="json")
    return MemoryOperationResult(status="rejected", reasonCode="unsupported_operation").model_dump(mode="json")


def _error_message(request_id: str, code: str, retryable: bool,
                   diagnostic_code: str | None = None) -> dict[str, Any]:
    payload = {"code": code, "retryable": retryable}
    if diagnostic_code is not None:
        payload["diagnosticCode"] = diagnostic_code
    return {
        "protocolVersion": 2,
        "type": "error",
        "requestId": request_id,
        "payload": payload,
    }


def _memory_error_message(request_id: str, code: str) -> dict[str, Any]:
    return {"protocolVersion": 3, "type": "error", "requestId": request_id,
            "payload": {"code": code, "retryable": False}}


def _memory_turn_message(request_id: str, execution: Any) -> dict[str, Any]:
    if execution.errorCode:
        return _memory_error_message(request_id, execution.errorCode)
    result = execution.result
    if result is None:
        return _memory_error_message(request_id, "internal_error")
    if result.error is not None:
        allowed = {"invalid_job", "schema_unsupported", "provider_auth", "provider_payment_required",
                   "provider_rate_limited", "provider_unavailable", "provider_timeout", "provider_refusal",
                   "persona_unavailable", "invalid_reply", "internal_error"}
        return _memory_error_message(request_id, result.error.code if result.error.code in allowed else "provider_unavailable")
    if result.judgment is None:
        return _memory_error_message(request_id, "internal_error")
    return {
        "protocolVersion": 3,
        "type": "result",
        "requestId": request_id,
        "payload": {"conversation": {
            "judgment": result.judgment.model_dump(mode="json"),
            "usage": result.usage.model_dump(mode="json"),
            "timing": result.timing.model_dump(mode="json"),
        }},
    }


def _result_message(request_id: str, result: ProviderResult) -> dict[str, Any]:
    if result.error is not None:
        return _error_message(request_id, result.error.code, result.error.retryable,
                              result.error.diagnosticCode)
    if result.judgment is None:
        return _error_message(request_id, "internal_error", False)
    return {
        "protocolVersion": 2,
        "type": "result",
        "requestId": request_id,
        "payload": {
            "judgment": result.judgment.model_dump(mode="json"),
            "usage": result.usage.model_dump(mode="json"),
            "timing": result.timing.model_dump(mode="json"),
        },
    }


async def _cancel(task: asyncio.Task[Any] | None) -> None:
    if task is None or task.done():
        return
    task.cancel()
    await asyncio.gather(task, return_exceptions=True)


async def run_stdio(input_stream: BinaryIO | None = None, output_stream: BinaryIO | None = None,
                    provider: Any | None = None, persona_path: str | None = None) -> int:
    source = input_stream or sys.stdin.buffer
    sink = output_stream or sys.stdout.buffer
    messages: asyncio.Queue[bytes] = asyncio.Queue()
    reader_task = asyncio.create_task(_read_input(source, messages))
    next_input = asyncio.create_task(messages.get())
    credential: str | None = None
    memory_repository: MemoryRepository | None = None
    protocol_version = 2
    generation_ready = False
    active_request_id: str | None = None
    active_task: asyncio.Task[Any] | None = None
    active_scenario_task: asyncio.Task[ScenarioTurnReceipt] | None = None
    active_scenario_job = None
    active_memory_turn = False
    try:
        while True:
            wait_for: set[asyncio.Task[Any]] = {next_input}
            if active_task is not None:
                wait_for.add(active_task)
            if active_scenario_task is not None:
                wait_for.add(active_scenario_task)
            done, _ = await asyncio.wait(wait_for, return_when=asyncio.FIRST_COMPLETED)

            if active_task is not None and active_task in done:
                request_id = active_request_id or "unknown-request"
                try:
                    result = active_task.result()
                    if active_memory_turn:
                        _write_memory(sink, _memory_turn_message(request_id, result))
                    else:
                        _write(sink, _result_message(request_id, result))
                except asyncio.CancelledError:
                    pass
                except Exception:
                    if active_memory_turn:
                        _write_memory(sink, _memory_error_message(request_id, "internal_error"))
                    else:
                        _write(sink, _error_message(request_id, "internal_error", False))
                active_task = None
                active_request_id = None
                active_memory_turn = False

            if active_scenario_task is not None and active_scenario_task in done:
                request_id = active_request_id or "unknown-request"
                try:
                    receipt = active_scenario_task.result()
                    _write_scenario(sink, scenario_receipt_message(active_scenario_job, receipt))
                except asyncio.CancelledError:
                    pass
                except Exception:
                    _write_scenario(sink, {"protocolVersion": 4, "type": "error", "requestId": request_id,
                                           "payload": {"code": "internal_error", "retryable": False}})
                active_scenario_task = None
                active_scenario_job = None
                active_request_id = None

            if next_input not in done:
                continue

            line = next_input.result()
            next_input = asyncio.create_task(messages.get())
            if not line:
                await _cancel(active_task)
                await _cancel(active_scenario_task)
                return 0
            if len(line) > MAX_LINE_BYTES or not line.endswith(b"\n"):
                await _cancel(active_task)
                await _cancel(active_scenario_task)
                return 2
            try:
                message = decode_line(line)
            except (ContractError, MemoryProtocolError):
                await _cancel(active_task)
                await _cancel(active_scenario_task)
                return 2

            if message.type == "bootstrap":
                if active_task is not None:
                    await _cancel(active_task)
                    active_task = None
                    active_request_id = None
                if active_scenario_task is not None:
                    await _cancel(active_scenario_task)
                    active_scenario_task = None
                    active_scenario_job = None
                    active_request_id = None
                if isinstance(message, ScenarioBootstrapEnvelopeV4):
                    bootstrap = message.payload
                    try:
                        memory_repository = MemoryRepository(bootstrap.memoryRoot, bootstrap.playerId)
                        supplied_key = bootstrap.apiKey.get_secret_value().strip()
                        generation_ready = False
                        if supplied_key:
                            try:
                                load_persona(persona_path)
                                credential = supplied_key
                                generation_ready = True
                            except PromptSetupError:
                                credential = None
                        else:
                            credential = None
                        supplied_key = None
                        protocol_version = 4
                        _write_scenario(sink, {"protocolVersion": 4, "type": "ready", "requestId": message.requestId,
                            "payload": {"runtimeVersion": "0.3.0",
                                        "modelId": "deepseek/deepseek-v4.1-flash" if generation_ready else "",
                                        "capabilities": {"memory": True, "generation": generation_ready,
                                                         "scenario-turn-v1": True}}})
                    except (MemoryStorageError, MemoryProtocolError, OSError):
                        memory_repository = None
                        credential = None
                        generation_ready = False
                        _write_scenario(sink, {"protocolVersion": 4, "type": "error", "requestId": message.requestId,
                                               "payload": {"code": "memory_unavailable", "retryable": False}})
                    bootstrap = None
                    message = None
                    continue
                if isinstance(message, MemoryEnvelopeV3):
                    bootstrap = message.payload
                    if not isinstance(bootstrap, MemoryBootstrapV3):
                        _write_memory(sink, _memory_error_message(message.requestId, "invalid_job"))
                        bootstrap = None
                        message = None
                        continue
                    try:
                        memory_repository = MemoryRepository(bootstrap.memoryRoot, bootstrap.playerId)
                        supplied_key = bootstrap.apiKey.get_secret_value().strip()
                        generation_ready = False
                        if supplied_key:
                            try:
                                load_persona(persona_path)
                                credential = supplied_key
                                generation_ready = True
                            except PromptSetupError:
                                credential = None
                        else:
                            credential = None
                        supplied_key = None
                        protocol_version = 3
                        _write_memory(sink, {
                            "protocolVersion": 3,
                            "type": "ready",
                            "requestId": message.requestId,
                            "payload": {"runtimeVersion": "0.2.0", "modelId": "deepseek/deepseek-v4.1-flash" if generation_ready else "",
                                        "capabilities": {"memory": True, "generation": generation_ready}},
                        })
                    except (MemoryStorageError, MemoryProtocolError, OSError):
                        memory_repository = None
                        credential = None
                        generation_ready = False
                        _write_memory(sink, _memory_error_message(message.requestId, "memory_unavailable"))
                    bootstrap = None
                    message = None
                    continue
                try:
                    load_persona(persona_path)
                except PromptSetupError:
                    _write(sink, _error_message(message.requestId, "persona_unavailable", False))
                    credential = None
                    generation_ready = False
                    continue
                credential = message.payload.apiKey.get_secret_value()
                memory_repository = None
                generation_ready = True
                protocol_version = 2
                _write(sink, {
                    "protocolVersion": 2,
                    "type": "ready",
                    "requestId": message.requestId,
                    "payload": {"runtimeVersion": "0.1.0", "modelId": "deepseek/deepseek-v4.1-flash"},
                })
            elif message.type == "memory":
                if not isinstance(message, MemoryEnvelopeV3) or not isinstance(message.payload, MemoryCommandV3):
                    _write_memory(sink, _memory_error_message(message.requestId, "schema_unsupported"))
                elif protocol_version not in {3, 4} or memory_repository is None:
                    _write_memory(sink, _memory_error_message(message.requestId, "memory_unavailable"))
                else:
                    result = handle_memory_command(message.payload, memory_repository)
                    _write_memory(sink, {"protocolVersion": 3, "type": "result", "requestId": message.requestId,
                                         "payload": result})
            elif message.type == "turn":
                if message.protocolVersion == 3:
                    if not isinstance(message, MemoryEnvelopeV3) or not isinstance(message.payload, MemoryTurnDataV3):
                        _write_memory(sink, _memory_error_message(message.requestId, "schema_unsupported"))
                    elif protocol_version != 3 or memory_repository is None:
                        _write_memory(sink, _memory_error_message(message.requestId, "memory_unavailable"))
                    elif credential is None or not generation_ready:
                        _write_memory(sink, _memory_error_message(message.requestId, "provider_auth"))
                    elif active_task is not None:
                        _write_memory(sink, _memory_error_message(message.requestId, "invalid_job"))
                    else:
                        os.environ.pop("LANGSMITH_API_KEY", None)
                        os.environ.pop("LANGCHAIN_API_KEY", None)
                        os.environ["LANGCHAIN_TRACING_V2"] = "false"
                        os.environ["LANGSMITH_TRACING"] = "false"
                        from .graph import generate_memory_turn
                        active_request_id = message.requestId
                        active_memory_turn = True
                        active_task = asyncio.create_task(generate_memory_turn(message.payload, memory_repository,
                            credential, provider=provider, persona_path=persona_path))
                    continue
                if credential is None:
                    _write(sink, _error_message(message.requestId, "provider_auth", False))
                elif active_task is not None:
                    _write(sink, _error_message(message.requestId, "invalid_job", False))
                else:
                    os.environ.pop("LANGSMITH_API_KEY", None)
                    os.environ.pop("LANGCHAIN_API_KEY", None)
                    os.environ["LANGCHAIN_TRACING_V2"] = "false"
                    os.environ["LANGSMITH_TRACING"] = "false"
                    from .graph import generate
                    active_request_id = message.requestId
                    active_memory_turn = False
                    active_task = asyncio.create_task(generate(message.payload, credential, provider=provider, persona_path=persona_path))
            elif message.type == "scenario_turn" and isinstance(message, ScenarioTurnEnvelopeV4):
                if protocol_version != 4:
                    _write_scenario(sink, {"protocolVersion": 4, "type": "error", "requestId": message.requestId,
                                           "payload": {"code": "schema_unsupported", "retryable": False}})
                elif active_task is not None or active_scenario_task is not None:
                    _write_scenario(sink, {"protocolVersion": 4, "type": "error", "requestId": message.requestId,
                                           "payload": {"code": "invalid_job", "retryable": False}})
                else:
                    os.environ.pop("LANGSMITH_API_KEY", None)
                    os.environ.pop("LANGCHAIN_API_KEY", None)
                    os.environ["LANGCHAIN_TRACING_V2"] = "false"
                    os.environ["LANGSMITH_TRACING"] = "false"
                    active_request_id = message.requestId
                    active_scenario_job = message.payload
                    active_scenario_task = asyncio.create_task(generate_scenario_turn(
                        message.payload, credential or "", lambda stage: _write_scenario(sink, stage.model_dump(mode="json")),
                        provider=provider, memory_repository=memory_repository, persona_path=persona_path))
            elif message.type == "cancel":
                if message.requestId == active_request_id:
                    if active_scenario_task is not None:
                        await _cancel(active_scenario_task)
                        if active_scenario_task.done() and active_scenario_job is not None:
                            try:
                                receipt = active_scenario_task.result()
                                _write_scenario(sink, scenario_receipt_message(active_scenario_job, receipt))
                            except (asyncio.CancelledError, Exception):
                                pass
                        active_scenario_task = None
                        active_scenario_job = None
                    else:
                        await _cancel(active_task)
                        active_task = None
                    active_request_id = None
            elif message.type == "shutdown":
                await _cancel(active_task)
                await _cancel(active_scenario_task)
                credential = None
                return 0
    finally:
        await _cancel(active_task)
        await _cancel(active_scenario_task)
        next_input.cancel()
        reader_task.cancel()
        try:
            source.close()
        except Exception:
            pass
        await asyncio.gather(next_input, reader_task, return_exceptions=True)
        credential = None
        memory_repository = None


def main() -> int:
    os.environ.pop("OPENROUTER_API_KEY", None)
    os.environ.pop("LANGSMITH_API_KEY", None)
    os.environ.pop("LANGCHAIN_API_KEY", None)
    os.environ["LANGCHAIN_TRACING_V2"] = "false"
    os.environ["LANGSMITH_TRACING"] = "false"
    return asyncio.run(run_stdio(persona_path=os.environ.get("ALTEROS_PERSONA_PATH")))


if __name__ == "__main__":
    raise SystemExit(main())
