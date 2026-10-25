"""Strict v4 Scenario definitions and stage envelopes for the local sidecar."""

from __future__ import annotations

import json
from typing import Annotated, Any, Literal, Union

from pydantic import BaseModel, ConfigDict, Field, ValidationError, model_validator

from .contracts import ConversationJobV2
from .memory_contracts import MemoryProposal
from .memory_protocol import MemoryTurnPolicyV3


MAX_SCENARIO_ENVELOPE_BYTES = 256 * 1024
MAX_SCENARIO_RESULT_BYTES = 128 * 1024
MAX_SCENARIO_TURN_BYTES = 64 * 1024
MAX_SCENARIO_STEPS = 64
MAX_DIALOGUE_BEATS = 16
MAX_HEAD_BEATS = 4
MAX_QUESTION_OPTIONS = 8
MAX_TOPIC_OFFERS = 3


class ScenarioContractError(ValueError):
    """Sanitized malformed Scenario or v4 transport contract."""


class StrictScenarioModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, frozen=True)


class ScenarioBeatV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    text: str = Field(min_length=1, max_length=600)
    displayText: str = Field(min_length=1, max_length=600)
    language: Literal["japanese"]
    expression: str = Field(max_length=64)


class ScenarioDialogueStepV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    kind: Literal["dialogue"]
    beats: list[ScenarioBeatV4] = Field(min_length=1, max_length=MAX_DIALOGUE_BEATS)
    nextStepId: str = Field(min_length=1, max_length=64)
    closeExchange: bool

    @model_validator(mode="after")
    def unique_beats(self) -> "ScenarioDialogueStepV4":
        if len({beat.id for beat in self.beats}) != len(self.beats):
            raise ValueError("duplicate dialogue beat ID")
        return self


class ScenarioQuestionOptionV4(StrictScenarioModel):
    optionId: str = Field(min_length=1, max_length=64)
    label: str = Field(min_length=1, max_length=80)
    value: str = Field(min_length=1, max_length=256)
    nextStepId: str = Field(min_length=1, max_length=64)


class ScenarioAskStepV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    kind: Literal["ask"]
    questionId: str = Field(min_length=1, max_length=64)
    question: ScenarioBeatV4
    options: list[ScenarioQuestionOptionV4] = Field(max_length=MAX_QUESTION_OPTIONS)
    allowFreeText: bool
    freeTextNextStepId: str = Field(min_length=1, max_length=64)
    skipNextStepId: str = Field(min_length=1, max_length=64)
    cancelNextStepId: str = Field(min_length=1, max_length=64)

    @model_validator(mode="after")
    def unique_options(self) -> "ScenarioAskStepV4":
        if len({option.optionId for option in self.options}) != len(self.options):
            raise ValueError("duplicate answer option ID")
        return self


class ScenarioActionStepV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    kind: Literal["action"]
    capabilityId: str = Field(min_length=1, max_length=128)
    arguments: dict[str, str] = Field(max_length=8)
    successNextStepId: str = Field(min_length=1, max_length=64)
    failureNextStepId: str = Field(min_length=1, max_length=64)


class ScenarioWaitStepV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    kind: Literal["wait"]
    activityId: str = Field(min_length=1, max_length=128)
    conditionId: str = Field(min_length=1, max_length=128)
    objective: str = Field(min_length=1, max_length=240)
    successNextStepId: str = Field(min_length=1, max_length=64)
    failureNextStepId: str = Field(min_length=1, max_length=64)


class ScenarioBranchCaseV4(StrictScenarioModel):
    value: str = Field(min_length=1, max_length=128)
    nextStepId: str = Field(min_length=1, max_length=64)


class ScenarioBranchStepV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    kind: Literal["branch"]
    sourceId: str = Field(min_length=1, max_length=64)
    cases: list[ScenarioBranchCaseV4] = Field(min_length=1, max_length=16)
    defaultNextStepId: str = Field(min_length=1, max_length=64)

    @model_validator(mode="after")
    def unique_cases(self) -> "ScenarioBranchStepV4":
        if len({case.value for case in self.cases}) != len(self.cases):
            raise ValueError("duplicate branch case")
        return self


class ScenarioEndStepV4(StrictScenarioModel):
    id: str = Field(min_length=1, max_length=64)
    kind: Literal["end"]
    outcome: Literal["completed", "player_ended", "failed"]
    closeExchange: bool


ScenarioStepV4 = Annotated[
    Union[
        ScenarioDialogueStepV4,
        ScenarioAskStepV4,
        ScenarioActionStepV4,
        ScenarioWaitStepV4,
        ScenarioBranchStepV4,
        ScenarioEndStepV4,
    ],
    Field(discriminator="kind"),
]


def _successors(step: ScenarioStepV4) -> tuple[str, ...]:
    if step.kind == "dialogue":
        return (step.nextStepId,)
    if step.kind == "ask":
        return tuple(option.nextStepId for option in step.options) + (
            step.freeTextNextStepId, step.skipNextStepId, step.cancelNextStepId,
        )
    if step.kind == "action" or step.kind == "wait":
        return (step.successNextStepId, step.failureNextStepId)
    if step.kind == "branch":
        return tuple(case.nextStepId for case in step.cases) + (step.defaultNextStepId,)
    return ()


class ScenarioDefinitionV4(StrictScenarioModel):
    scenarioId: str = Field(max_length=128)
    topicId: str = Field(max_length=128)
    entryStepId: str = Field(min_length=1, max_length=64)
    steps: list[ScenarioStepV4] = Field(min_length=1, max_length=MAX_SCENARIO_STEPS)

    @model_validator(mode="after")
    def validate_graph(self) -> "ScenarioDefinitionV4":
        ids = [step.id for step in self.steps]
        if len(set(ids)) != len(ids):
            raise ValueError("duplicate step ID")
        known = set(ids)
        if self.entryStepId not in known:
            raise ValueError("entry step does not exist")
        for step in self.steps:
            if any(next_id not in known for next_id in _successors(step)):
                raise ValueError("step successor does not exist")
            if step.kind == "branch" and step.sourceId not in known:
                raise ValueError("branch source step does not exist")
        return self


class ScenarioObjectSnapshotV4(StrictScenarioModel):
    objectId: str = Field(min_length=1, max_length=128)
    kind: str = Field(min_length=1, max_length=64)


class ScenarioArgumentBindingV4(StrictScenarioModel):
    name: str = Field(min_length=1, max_length=64)
    values: list[str] = Field(min_length=1, max_length=32)


class ScenarioCapabilitySnapshotV4(StrictScenarioModel):
    capabilityId: str = Field(min_length=1, max_length=128)
    argumentBindings: list[ScenarioArgumentBindingV4] = Field(max_length=8)


class ScenarioRuntimeSnapshotV4(StrictScenarioModel):
    scenarioId: str = Field(max_length=128)
    runId: str = Field(min_length=1, max_length=128)
    turnId: str = Field(min_length=1, max_length=128)
    contextRevision: int = Field(ge=0)
    checkpointStepId: str = Field(max_length=64)
    knownTopicIds: list[str] = Field(max_length=8)
    knownIntentIds: list[str] = Field(max_length=32)
    allowedExpressions: list[str] = Field(max_length=64)
    capabilities: list[ScenarioCapabilitySnapshotV4] = Field(max_length=32)
    objects: list[ScenarioObjectSnapshotV4] = Field(max_length=128)
    waitConditions: list[str] = Field(max_length=64)

    @model_validator(mode="after")
    def unique_host_bindings(self) -> "ScenarioRuntimeSnapshotV4":
        if len(set(self.knownTopicIds)) != len(self.knownTopicIds) or len(set(self.knownIntentIds)) != len(self.knownIntentIds):
            raise ValueError("duplicate Topic or intent binding")
        if len(set(self.allowedExpressions)) != len(self.allowedExpressions) or len(set(self.waitConditions)) != len(self.waitConditions):
            raise ValueError("duplicate runtime allowlist entry")
        if len({item.capabilityId for item in self.capabilities}) != len(self.capabilities):
            raise ValueError("duplicate runtime capability")
        if len({item.objectId for item in self.objects}) != len(self.objects):
            raise ValueError("ambiguous runtime object binding")
        for capability in self.capabilities:
            names = [binding.name for binding in capability.argumentBindings]
            if len(set(names)) != len(names):
                raise ValueError("duplicate capability argument binding")
        return self


class ScenarioTopicOfferV4(StrictScenarioModel):
    offerId: str = Field(min_length=1, max_length=64)
    label: str = Field(min_length=1, max_length=80)
    topicId: str = Field(max_length=128)
    intentId: str = Field(max_length=128)

    @model_validator(mode="after")
    def exactly_one_target(self) -> "ScenarioTopicOfferV4":
        if bool(self.topicId) == bool(self.intentId):
            raise ValueError("offer must select exactly one Topic or intent")
        if any(ord(character) > 0x024F or 0xD800 <= ord(character) <= 0xDFFF for character in self.label):
            raise ValueError("Topic offer label is outside the English display script")
        return self


class ScenarioHeadContentV4(StrictScenarioModel):
    beats: list[ScenarioBeatV4] = Field(min_length=1, max_length=MAX_HEAD_BEATS)

    @model_validator(mode="after")
    def unique_beats(self) -> "ScenarioHeadContentV4":
        if len({beat.id for beat in self.beats}) != len(self.beats):
            raise ValueError("duplicate head beat ID")
        return self


class ScenarioMainContentV4(StrictScenarioModel):
    scenario: ScenarioDefinitionV4
    segmentStepId: str = Field(min_length=1, max_length=64)
    topicOffers: list[ScenarioTopicOfferV4] = Field(max_length=MAX_TOPIC_OFFERS)
    memoryProposals: list[MemoryProposal] = Field(max_length=3)
    questionProposal: Literal["preferred_name", "interest", "shared_activity_preference"] | None
    usedCallbackIds: list[str] = Field(max_length=1)

    @model_validator(mode="after")
    def valid_initial_segment(self) -> "ScenarioMainContentV4":
        if len({offer.offerId for offer in self.topicOffers}) != len(self.topicOffers):
            raise ValueError("duplicate Topic offer")
        if len(set(self.usedCallbackIds)) != len(self.usedCallbackIds):
            raise ValueError("duplicate memory callback ID")
        step = next((item for item in self.scenario.steps if item.id == self.segmentStepId), None)
        if step is None or step.kind != "dialogue" or self.segmentStepId != self.scenario.entryStepId:
            raise ValueError("main stage must name the Scenario entry dialogue segment")
        return self


class ScenarioUsageV4(StrictScenarioModel):
    inputTokens: int = Field(ge=0)
    outputTokens: int = Field(ge=0)
    totalTokens: int = Field(ge=0)
    costUsd: float | None

    @model_validator(mode="after")
    def consistent_totals(self) -> "ScenarioUsageV4":
        if self.totalTokens < max(self.inputTokens, self.outputTokens):
            raise ValueError("Scenario usage totals are inconsistent")
        if self.costUsd is not None and self.costUsd < 0:
            raise ValueError("Scenario cost cannot be negative")
        return self


class ScenarioHeadStagePayloadV4(StrictScenarioModel):
    turnId: str = Field(min_length=1, max_length=128)
    turnVersion: int = Field(ge=1)
    stageId: str = Field(min_length=1, max_length=128)
    stage: Literal["head"]
    status: Literal["ready", "failed", "cancelled"]
    usage: ScenarioUsageV4
    beats: list[ScenarioBeatV4] = Field(min_length=1, max_length=MAX_HEAD_BEATS)

    @model_validator(mode="after")
    def unique_beats(self) -> "ScenarioHeadStagePayloadV4":
        if len({beat.id for beat in self.beats}) != len(self.beats):
            raise ValueError("duplicate head beat ID")
        return self


class ScenarioMainStagePayloadV4(StrictScenarioModel):
    turnId: str = Field(min_length=1, max_length=128)
    turnVersion: int = Field(ge=1)
    stageId: str = Field(min_length=1, max_length=128)
    stage: Literal["main"]
    status: Literal["ready", "failed", "cancelled"]
    usage: ScenarioUsageV4
    scenario: ScenarioDefinitionV4
    segmentStepId: str = Field(min_length=1, max_length=64)
    topicOffers: list[ScenarioTopicOfferV4] = Field(max_length=MAX_TOPIC_OFFERS)
    memoryProposals: list[MemoryProposal] = Field(default_factory=list, max_length=3)
    questionProposal: Literal["preferred_name", "interest", "shared_activity_preference"] | None = None
    usedCallbackIds: list[str] = Field(default_factory=list, max_length=1)

    @model_validator(mode="after")
    def valid_initial_segment(self) -> "ScenarioMainStagePayloadV4":
        if len({offer.offerId for offer in self.topicOffers}) != len(self.topicOffers):
            raise ValueError("duplicate Topic offer")
        if len(set(self.usedCallbackIds)) != len(self.usedCallbackIds):
            raise ValueError("duplicate memory callback ID")
        step = next((item for item in self.scenario.steps if item.id == self.segmentStepId), None)
        if step is None or step.kind != "dialogue" or self.segmentStepId != self.scenario.entryStepId:
            raise ValueError("main stage must name the Scenario entry dialogue segment")
        return self


ScenarioStagePayloadV4 = Annotated[
    Union[ScenarioHeadStagePayloadV4, ScenarioMainStagePayloadV4],
    Field(discriminator="stage"),
]


class ScenarioStageEnvelopeV4(StrictScenarioModel):
    protocolVersion: Literal[4]
    type: Literal["scenario_stage"]
    requestId: str = Field(min_length=1, max_length=128)
    payload: ScenarioStagePayloadV4

    @model_validator(mode="after")
    def bind_parent_id(self) -> "ScenarioStageEnvelopeV4":
        if self.payload.turnId != self.requestId:
            raise ValueError("stage is not bound to its parent turn")
        return self


class ScenarioStageSummaryV4(StrictScenarioModel):
    stage: Literal["head", "main"]
    status: Literal["ready", "failed", "cancelled", "skipped"]
    usage: ScenarioUsageV4


class ScenarioCompletePayloadV4(StrictScenarioModel):
    turnId: str = Field(min_length=1, max_length=128)
    turnVersion: int = Field(ge=1)
    head: ScenarioStageSummaryV4
    main: ScenarioStageSummaryV4
    terminalReason: Literal["completed", "cancelled", "superseded", "timed_out", "failed"]

    @model_validator(mode="after")
    def stage_roles_match(self) -> "ScenarioCompletePayloadV4":
        if self.head.stage != "head" or self.main.stage != "main":
            raise ValueError("terminal stage summaries are mislabelled")
        return self


class ScenarioCompleteEnvelopeV4(StrictScenarioModel):
    protocolVersion: Literal[4]
    type: Literal["scenario_complete"]
    requestId: str = Field(min_length=1, max_length=128)
    payload: ScenarioCompletePayloadV4

    @model_validator(mode="after")
    def bind_parent_id(self) -> "ScenarioCompleteEnvelopeV4":
        if self.payload.turnId != self.requestId:
            raise ValueError("terminal receipt is not bound to its parent turn")
        return self


class ScenarioAnswerV4(StrictScenarioModel):
    questionId: str = Field(min_length=1, max_length=64)
    answerId: str = Field(max_length=64)
    freeText: str = Field(max_length=2048)

    @model_validator(mode="after")
    def answer_is_one_form(self) -> "ScenarioAnswerV4":
        if bool(self.answerId) == bool(self.freeText):
            raise ValueError("answer must be exactly one option ID or free text")
        return self


class ScenarioTurnPayloadV4(StrictScenarioModel):
    turnId: str = Field(min_length=1, max_length=128)
    turnVersion: int = Field(ge=1)
    operation: Literal["begin", "answer", "continue"]
    onlineEnabled: bool
    conversation: ConversationJobV2
    memory: MemoryTurnPolicyV3 | None
    runtime: ScenarioRuntimeSnapshotV4
    answer: ScenarioAnswerV4 | None

    @model_validator(mode="after")
    def operation_fields_match(self) -> "ScenarioTurnPayloadV4":
        if self.runtime.turnId != self.turnId:
            raise ValueError("runtime turn binding does not match")
        if (self.operation == "answer") != (self.answer is not None):
            raise ValueError("answer payload does not match operation")
        return self


class ScenarioTurnEnvelopeV4(StrictScenarioModel):
    protocolVersion: Literal[4]
    type: Literal["scenario_turn"]
    requestId: str = Field(min_length=1, max_length=128)
    payload: ScenarioTurnPayloadV4

    @model_validator(mode="after")
    def bind_parent_id(self) -> "ScenarioTurnEnvelopeV4":
        if self.payload.turnId != self.requestId:
            raise ValueError("turn is not bound to its request")
        return self


def validate_scenario_definition(definition: ScenarioDefinitionV4 | dict[str, Any],
                                 runtime: ScenarioRuntimeSnapshotV4 | dict[str, Any]) -> ScenarioDefinitionV4:
    try:
        scenario_data = definition.model_dump(mode="python") if isinstance(definition, ScenarioDefinitionV4) else definition
        runtime_data = runtime.model_dump(mode="python") if isinstance(runtime, ScenarioRuntimeSnapshotV4) else runtime
        scenario = ScenarioDefinitionV4.model_validate(scenario_data, strict=True)
        snapshot = ScenarioRuntimeSnapshotV4.model_validate(runtime_data, strict=True)
    except ValidationError:
        raise ScenarioContractError("invalid Scenario definition or runtime snapshot") from None

    if scenario.topicId and scenario.topicId not in snapshot.knownTopicIds:
        raise ScenarioContractError("Scenario Topic is outside supplied context")
    if len({item.objectId for item in snapshot.objects}) != len(snapshot.objects):
        raise ScenarioContractError("runtime object binding is ambiguous")
    if len({item.capabilityId for item in snapshot.capabilities}) != len(snapshot.capabilities):
        raise ScenarioContractError("runtime capability is ambiguous")
    if len(set(snapshot.allowedExpressions)) != len(snapshot.allowedExpressions):
        raise ScenarioContractError("duplicate expression capability")

    objects = {item.objectId for item in snapshot.objects}
    capabilities = {item.capabilityId: item for item in snapshot.capabilities}
    for step in scenario.steps:
        if step.kind == "dialogue":
            beats = step.beats
        elif step.kind == "ask":
            beats = [step.question]
        else:
            beats = []
        for beat in beats:
            if beat.expression and beat.expression not in snapshot.allowedExpressions:
                raise ScenarioContractError("Scenario expression is not available")
        if step.kind == "action":
            capability = capabilities.get(step.capabilityId)
            if capability is None:
                raise ScenarioContractError("Scenario capability is not available")
            bindings = {item.name: item.values for item in capability.argumentBindings}
            if len(bindings) != len(capability.argumentBindings) or set(step.arguments) != set(bindings):
                raise ScenarioContractError("Scenario action arguments are not allowlisted")
            for name, value in step.arguments.items():
                if value not in bindings[name]:
                    raise ScenarioContractError("Scenario action argument is not bound")
                if name == "targetObjectId" and value not in objects:
                    raise ScenarioContractError("Scenario target object is unavailable")
        if step.kind == "wait" and step.conditionId not in snapshot.waitConditions:
            raise ScenarioContractError("Scenario wait condition is not available")
    return scenario


def _object_from_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ScenarioContractError("duplicate JSON property")
        result[key] = value
    return result


def _reject_constant(_: str) -> None:
    raise ScenarioContractError("non-finite JSON number")


def parse_scenario_envelope(raw: bytes | str) -> ScenarioTurnEnvelopeV4 | ScenarioStageEnvelopeV4 | ScenarioCompleteEnvelopeV4:
    try:
        encoded = raw.encode("utf-8", errors="strict") if isinstance(raw, str) else raw
        if not isinstance(encoded, bytes) or not encoded or len(encoded) > MAX_SCENARIO_ENVELOPE_BYTES:
            raise ScenarioContractError("invalid Scenario envelope size")
        value = json.loads(encoded.decode("utf-8", errors="strict"), object_pairs_hook=_object_from_pairs,
                           parse_constant=_reject_constant)
        if not isinstance(value, dict):
            raise ScenarioContractError("Scenario envelope must be an object")
        message_type = value.get("type")
        if message_type == "scenario_turn" and len(encoded) > MAX_SCENARIO_TURN_BYTES:
            raise ScenarioContractError("Scenario turn exceeds its byte limit")
        if message_type in {"scenario_stage", "scenario_complete"} and len(encoded) > MAX_SCENARIO_RESULT_BYTES:
            raise ScenarioContractError("Scenario result exceeds its byte limit")
        if message_type == "scenario_turn":
            return ScenarioTurnEnvelopeV4.model_validate(value, strict=True)
        if message_type == "scenario_stage":
            return ScenarioStageEnvelopeV4.model_validate(value, strict=True)
        if message_type == "scenario_complete":
            return ScenarioCompleteEnvelopeV4.model_validate(value, strict=True)
        raise ScenarioContractError("unsupported Scenario envelope type")
    except ScenarioContractError:
        raise
    except (UnicodeError, json.JSONDecodeError, ValidationError, TypeError, ValueError):
        raise ScenarioContractError("invalid Scenario envelope") from None
