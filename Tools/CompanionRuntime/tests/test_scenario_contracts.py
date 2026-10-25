from __future__ import annotations

import json
from pathlib import Path

import pytest

from alteros_companion.scenario_contracts import (
    ScenarioContractError,
    parse_scenario_envelope,
    validate_scenario_definition,
)
from alteros_companion.contracts import parse_envelope


FIXTURES = Path(__file__).resolve().parents[2] / "ScenarioRuntime" / "Fixtures" / "v4"


def read_fixture(name: str) -> bytes:
    return (FIXTURES / name).read_bytes()


def test_cat_and_synthetic_drawing_main_stages_parse_to_typed_scenarios() -> None:
    cats = parse_scenario_envelope(read_fixture("main-cats.json"))
    drawing = parse_scenario_envelope(read_fixture("main-drawing-simulated.json"))

    assert cats.payload.stage == "main"
    assert cats.payload.scenario.topicId == "cats"
    assert cats.payload.scenario.steps[1].kind == "ask"
    assert cats.payload.scenario.steps[2].beats[0].displayText == "Thanks for telling me."
    assert drawing.payload.scenario.topicId == "drawing_together"
    assert drawing.payload.scenario.steps[1].kind == "action"
    assert drawing.payload.scenario.steps[1].capabilityId == "fixture.activity.drawing_together"
    assert drawing.payload.memoryProposals == []
    assert drawing.payload.questionProposal is None
    assert drawing.payload.usedCallbackIds == []


def test_head_stage_is_bilingual_and_has_four_beat_limit() -> None:
    result = parse_scenario_envelope(read_fixture("head-valid.json"))
    assert result.payload.stage == "head"
    assert result.payload.beats[0].displayText == "Mm-hm."

    payload = json.loads(read_fixture("head-valid.json"))
    payload["payload"]["beats"] *= 5
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())


@pytest.mark.parametrize("mutation", ["duplicate_step", "missing_successor", "wrong_language", "unknown_capability", "ambiguous_object"])
def test_main_stage_rejects_invalid_scenario_graph_and_bindings(mutation: str) -> None:
    payload = json.loads(read_fixture("main-drawing-simulated.json"))
    steps = payload["payload"]["scenario"]["steps"]
    if mutation == "duplicate_step":
        steps.append(dict(steps[0]))
    elif mutation == "missing_successor":
        steps[1]["successNextStepId"] = "missing"
    elif mutation == "wrong_language":
        steps[0]["beats"][0]["language"] = "english"
        with pytest.raises(ScenarioContractError):
            parse_scenario_envelope(json.dumps(payload).encode())
        return
    else:
        stage = parse_scenario_envelope(read_fixture("main-drawing-simulated.json"))
        runtime = {
            "scenarioId": "",
            "runId": "run-fixture-1",
            "turnId": "turn-drawing-1",
            "contextRevision": 1,
            "checkpointStepId": "invite",
            "knownTopicIds": ["drawing_together"],
            "allowedExpressions": ["happy", "gentle", "thinking"],
            "capabilities": [{"capabilityId": "fixture.activity.drawing_together",
                               "argumentBindings": [{"name": "targetObjectId", "values": ["fixture-drawing-table"]}]}],
            "objects": [{"objectId": "fixture-drawing-table", "kind": "use_point"}],
            "waitConditions": ["fixture.player_at_adjacent_seat"],
        }
        if mutation == "unknown_capability":
            scenario = json.loads(read_fixture("main-drawing-simulated.json"))["payload"]["scenario"]
            scenario["steps"][1]["capabilityId"] = "arbitrary.external.write"
        else:
            runtime["objects"].append({"objectId": "fixture-drawing-table", "kind": "use_point"})
            scenario = json.loads(read_fixture("main-drawing-simulated.json"))["payload"]["scenario"]
        with pytest.raises(ScenarioContractError):
            validate_scenario_definition(scenario, runtime)
        return

    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())


def test_main_segment_has_sixteen_beat_limit() -> None:
    payload = json.loads(read_fixture("main-cats.json"))
    payload["payload"]["scenario"]["steps"][0]["beats"] *= 17
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())


def test_main_segment_must_be_the_scenario_entry_dialogue() -> None:
    payload = json.loads(read_fixture("main-cats.json"))
    payload["payload"]["segmentStepId"] = "closing"
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())


def test_scenario_accepts_sixty_four_steps_and_rejects_sixty_five() -> None:
    payload = json.loads(read_fixture("main-cats.json"))
    steps = payload["payload"]["scenario"]["steps"]
    steps.extend({"id": f"end-{index}", "kind": "end", "outcome": "completed", "closeExchange": True}
                 for index in range(4, 64))
    assert len(parse_scenario_envelope(json.dumps(payload).encode()).payload.scenario.steps) == 64
    steps.append({"id": "end-64", "kind": "end", "outcome": "completed", "closeExchange": True})
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())


def test_main_stage_accepts_three_topic_offers_and_rejects_four() -> None:
    payload = json.loads(read_fixture("main-cats.json"))
    payload["payload"]["topicOffers"] = [
        {"offerId": f"offer-{index}", "label": f"Topic {index}", "topicId": "cats", "intentId": ""}
        for index in range(3)
    ]
    assert len(parse_scenario_envelope(json.dumps(payload).encode()).payload.topicOffers) == 3
    payload["payload"]["topicOffers"].append(
        {"offerId": "offer-3", "label": "Topic 3", "topicId": "cats", "intentId": ""})
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())


def test_scenario_cannot_name_a_topic_when_host_supplied_no_known_topics() -> None:
    payload = json.loads(read_fixture("main-cats.json"))["payload"]["scenario"]
    runtime = {
        "scenarioId": "", "runId": "run-1", "turnId": "turn-cats-1", "contextRevision": 1,
        "checkpointStepId": "", "knownTopicIds": [], "knownIntentIds": [], "allowedExpressions": ["gentle", "thinking", "happy"],
        "capabilities": [], "objects": [], "waitConditions": [],
    }
    with pytest.raises(ScenarioContractError):
        validate_scenario_definition(payload, runtime)


def test_scenario_turn_is_v4_bound_and_keeps_legacy_conversation_job_shape() -> None:
    conversation = parse_envelope((Path(__file__).resolve().parents[2] / "Conversation" / "Fixtures" / "v2" / "turn-valid.json").read_bytes()).payload
    turn_id = "scenario-turn-1"
    runtime = {
        "scenarioId": "", "runId": "run-1", "turnId": turn_id, "contextRevision": 2,
        "checkpointStepId": "", "knownTopicIds": ["player_tired"], "allowedExpressions": ["gentle"],
        "knownIntentIds": [],
        "capabilities": [], "objects": [], "waitConditions": [],
    }
    message = {
        "protocolVersion": 4, "type": "scenario_turn", "requestId": turn_id,
        "payload": {"turnId": turn_id, "turnVersion": 1, "operation": "begin", "onlineEnabled": True,
                    "conversation": conversation.model_dump(mode="json"), "memory": None,
                    "runtime": runtime, "answer": None},
    }
    parsed = parse_scenario_envelope(json.dumps(message).encode())
    assert parsed.payload.turnId == turn_id
    assert parsed.payload.conversation.request.channel == "direct"

    message["payload"]["runtime"] = {**runtime, "turnId": "other-turn"}
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(message).encode())


def test_scenario_limits_and_protocol_are_closed() -> None:
    payload = json.loads(read_fixture("main-cats.json"))
    payload["payload"]["stage"] = "dessert"
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(json.dumps(payload).encode())

    duplicate = read_fixture("head-valid.json").replace(b'"stage": "head"', b'"stage": "head", "stage": "dessert"')
    with pytest.raises(ScenarioContractError):
        parse_scenario_envelope(duplicate)
