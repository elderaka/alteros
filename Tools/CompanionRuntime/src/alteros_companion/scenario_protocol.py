"""Version 4 stdio bootstrap and control framing for Scenario turns."""

from __future__ import annotations

import json
from typing import Annotated, Any, Literal, Union

from pydantic import Field, ValidationError

from .memory_protocol import MemoryBootstrapV3, StrictMemoryModel
from .scenario_contracts import MAX_SCENARIO_ENVELOPE_BYTES, ScenarioContractError, ScenarioTurnEnvelopeV4, parse_scenario_envelope


class ScenarioBootstrapEnvelopeV4(StrictMemoryModel):
    protocolVersion: Literal[4]
    type: Literal["bootstrap"]
    requestId: str = Field(min_length=1, max_length=128)
    payload: MemoryBootstrapV3


class ScenarioControlEnvelopeV4(StrictMemoryModel):
    protocolVersion: Literal[4]
    type: Literal["cancel", "shutdown"]
    requestId: str = Field(min_length=1, max_length=128)
    payload: dict[str, Any]

    @classmethod
    def validate_control(cls, value: Any) -> "ScenarioControlEnvelopeV4":
        result = cls.model_validate(value, strict=True)
        if result.payload:
            raise ValueError("control payload must be empty")
        return result


ScenarioInboundV4 = Annotated[
    Union[ScenarioBootstrapEnvelopeV4, ScenarioControlEnvelopeV4, ScenarioTurnEnvelopeV4],
    Field(discriminator="type"),
]


def decode_scenario_line(line: bytes) -> ScenarioBootstrapEnvelopeV4 | ScenarioControlEnvelopeV4 | ScenarioTurnEnvelopeV4:
    if not line or len(line) > MAX_SCENARIO_ENVELOPE_BYTES + 2 or not line.endswith(b"\n"):
        raise ScenarioContractError("invalid Scenario protocol framing")
    body = line[:-1]
    if body.endswith(b"\r"):
        body = body[:-1]
    try:
        header = json.loads(body.decode("utf-8", errors="strict"), object_pairs_hook=_unique_object)
        if not isinstance(header, dict) or header.get("protocolVersion") != 4:
            raise ScenarioContractError("unsupported Scenario protocol version")
        if header.get("type") == "scenario_turn":
            return parse_scenario_envelope(body)
        if header.get("type") == "bootstrap":
            return ScenarioBootstrapEnvelopeV4.model_validate(header, strict=True)
        if header.get("type") in {"cancel", "shutdown"}:
            return ScenarioControlEnvelopeV4.validate_control(header)
        raise ScenarioContractError("unsupported Scenario protocol message")
    except ScenarioContractError:
        raise
    except (UnicodeError, json.JSONDecodeError, ValidationError, TypeError, ValueError):
        raise ScenarioContractError("invalid Scenario protocol message") from None


def encode_scenario_message(message: dict[str, Any]) -> bytes:
    try:
        encoded = json.dumps(message, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8", errors="strict")
        if not encoded or len(encoded) > MAX_SCENARIO_ENVELOPE_BYTES:
            raise ScenarioContractError("invalid Scenario protocol message size")
        return encoded + b"\n"
    except ScenarioContractError:
        raise
    except (UnicodeError, TypeError, ValueError):
        raise ScenarioContractError("invalid Scenario protocol message") from None


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ScenarioContractError("duplicate Scenario protocol property")
        result[key] = value
    return result
