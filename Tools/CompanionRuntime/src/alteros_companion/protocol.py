"""Bounded UTF-8 line framing around the strict v2 message contracts."""

from __future__ import annotations

import json

from .contracts import MAX_ENVELOPE_BYTES, ContractError, ParsedEnvelope, parse_envelope
from .memory_protocol import MemoryEnvelopeV3, MemoryProtocolError, decode_memory_line
from .scenario_contracts import ScenarioContractError
from .scenario_protocol import ScenarioBootstrapEnvelopeV4, ScenarioControlEnvelopeV4, ScenarioTurnEnvelopeV4, decode_scenario_line


def decode_line(line: bytes) -> ParsedEnvelope | MemoryEnvelopeV3 | ScenarioBootstrapEnvelopeV4 | ScenarioControlEnvelopeV4 | ScenarioTurnEnvelopeV4:
    if not line or len(line) > MAX_ENVELOPE_BYTES + 2 or not line.endswith(b"\n"):
        raise ContractError("invalid protocol line framing")
    body = line[:-1]
    if body.endswith(b"\r"):
        body = body[:-1]
    if not body or len(body) > MAX_ENVELOPE_BYTES:
        raise ContractError("invalid protocol line size")
    try:
        header = json.loads(body.decode("utf-8", errors="strict"))
        if isinstance(header, dict) and header.get("protocolVersion") == 4:
            try:
                return decode_scenario_line(line)
            except ScenarioContractError:
                raise ContractError("invalid protocol message") from None
        if isinstance(header, dict) and header.get("protocolVersion") == 3:
            try:
                return decode_memory_line(line)
            except MemoryProtocolError:
                raise ContractError("invalid protocol message") from None
    except (UnicodeError, json.JSONDecodeError):
        pass
    return parse_envelope(body)


def encode_message(message: dict[str, object]) -> bytes:
    try:
        encoded = json.dumps(message, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8", errors="strict")
        if not encoded or len(encoded) > MAX_ENVELOPE_BYTES:
            raise ContractError("invalid protocol message size")
        parse_envelope(encoded)
        return encoded + b"\n"
    except ContractError:
        raise
    except (UnicodeError, TypeError, ValueError) as exc:
        raise ContractError("invalid protocol message") from None
