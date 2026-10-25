from __future__ import annotations

import asyncio
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from alteros_companion.contracts import TimingV2, UsageV2, parse_envelope
from alteros_companion.memory_contracts import MemoryEvidence, MemoryProposal
from alteros_companion.memory_protocol import MemoryTurnPolicyV3
from alteros_companion.memory_repository import MemoryRepository
from alteros_companion.scenario_contracts import (
    ScenarioHeadContentV4,
    ScenarioMainContentV4,
    ScenarioRuntimeSnapshotV4,
    ScenarioStageEnvelopeV4,
    ScenarioTurnPayloadV4,
    parse_scenario_envelope,
)
from alteros_companion.scenario_generation import ScenarioStageProviderResult, generate_scenario_turn


ROOT = Path(__file__).resolve().parents[3]
FIXTURES = ROOT / "Tools" / "ScenarioRuntime" / "Fixtures" / "v4"
NOW = datetime(2026, 10, 18, 12, 0, tzinfo=timezone.utc)


def _job(*, online: bool = True, text: str = "Do you like cats?", memory=None) -> ScenarioTurnPayloadV4:
    conversation = parse_envelope((ROOT / "Tools" / "Conversation" / "Fixtures" / "v2" / "turn-valid.json").read_bytes()).payload
    conversation = conversation.model_copy(update={
        "request": conversation.request.model_copy(update={"text": text}),
        "presentedHistory": [],
    })
    runtime = ScenarioRuntimeSnapshotV4(
        scenarioId="", runId="run-scenario-1", turnId="scenario-turn-1", contextRevision=1,
        checkpointStepId="", knownTopicIds=["cats", "drawing_together", "tea"], knownIntentIds=[],
        allowedExpressions=["gentle", "thinking", "happy"], capabilities=[], objects=[], waitConditions=[],
    )
    return ScenarioTurnPayloadV4(turnId="scenario-turn-1", turnVersion=1, operation="begin", onlineEnabled=online,
        conversation=conversation, memory=memory, runtime=runtime, answer=None)


def _head() -> ScenarioHeadContentV4:
    stage = parse_scenario_envelope((FIXTURES / "head-valid.json").read_bytes()).payload
    return ScenarioHeadContentV4(beats=stage.beats)


def _main(**changes: Any) -> ScenarioMainContentV4:
    stage = parse_scenario_envelope((FIXTURES / "main-cats.json").read_bytes()).payload
    fields = {"scenario": stage.scenario, "segmentStepId": stage.segmentStepId, "topicOffers": stage.topicOffers,
              "memoryProposals": [], "questionProposal": None, "usedCallbackIds": []}
    fields.update(changes)
    return ScenarioMainContentV4(**fields)


def _provider_result(content: Any) -> ScenarioStageProviderResult:
    return ScenarioStageProviderResult(content=content,
        usage=UsageV2(inputTokens=2, outputTokens=3, totalTokens=5, costUsd=None),
        timing=TimingV2(providerMs=1, totalMs=1))


class _BarrierProvider:
    def __init__(self, *, main=None, head=None):
        self.results = {"head": _provider_result(head or _head()), "main": _provider_result(main or _main())}
        self.started: list[str] = []
        self.both_started = asyncio.Event()
        self.release = {"head": asyncio.Event(), "main": asyncio.Event()}
        self.cancelled: set[str] = set()

    async def generate_scenario_stage(self, messages, credential, stage):
        self.started.append(stage)
        if len(self.started) == 2:
            self.both_started.set()
        try:
            await self.release[stage].wait()
        except asyncio.CancelledError:
            self.cancelled.add(stage)
            raise
        return self.results[stage]


def test_head_and_main_requests_overlap_and_emit_in_completion_order() -> None:
    async def run():
        provider = _BarrierProvider()
        emitted: list[ScenarioStageEnvelopeV4] = []
        head_emitted = asyncio.Event()

        def emit(stage: ScenarioStageEnvelopeV4) -> None:
            emitted.append(stage)
            if stage.payload.stage == "head":
                head_emitted.set()

        task = asyncio.create_task(generate_scenario_turn(_job(), "synthetic-credential", emit, provider,
            persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))
        await asyncio.wait_for(provider.both_started.wait(), timeout=1)
        assert set(provider.started) == {"head", "main"}
        provider.release["head"].set()
        await asyncio.wait_for(head_emitted.wait(), timeout=1)
        assert [stage.payload.stage for stage in emitted] == ["head"]
        provider.release["main"].set()
        receipt = await task
        return provider, emitted, receipt

    provider, emitted, receipt = asyncio.run(run())
    assert provider.started == ["head", "main"]
    assert [stage.payload.stage for stage in emitted] == ["head", "main"]
    assert receipt.head.status == "ready"
    assert receipt.main.status == "ready"
    assert receipt.terminalReason == "completed"
    assert len(provider.started) == 2


def test_main_can_finish_before_head_without_waiting_for_head() -> None:
    async def run():
        provider = _BarrierProvider()
        emitted: list[str] = []
        main_emitted = asyncio.Event()
        task = asyncio.create_task(generate_scenario_turn(_job(), "synthetic-credential",
            lambda stage: (emitted.append(stage.payload.stage), main_emitted.set() if stage.payload.stage == "main" else None),
            provider, persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))
        await asyncio.wait_for(provider.both_started.wait(), timeout=1)
        provider.release["main"].set()
        await asyncio.wait_for(main_emitted.wait(), timeout=1)
        assert emitted == ["main"]
        provider.release["head"].set()
        receipt = await task
        return emitted, receipt

    emitted, receipt = asyncio.run(run())
    assert emitted == ["main", "head"]
    assert receipt.head.status == "ready" and receipt.main.status == "ready"


def test_disabled_online_or_missing_key_makes_zero_provider_calls() -> None:
    async def run(online: bool, credential: str):
        provider = _BarrierProvider()
        receipt = await generate_scenario_turn(_job(online=online), credential, lambda _: None, provider,
            persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json")
        return provider, receipt

    offline, offline_receipt = asyncio.run(run(False, "synthetic-credential"))
    no_key, no_key_receipt = asyncio.run(run(True, ""))
    assert offline.started == []
    assert no_key.started == []
    assert offline_receipt.head.status == "skipped"
    assert no_key_receipt.terminalReason == "failed"


def test_memory_required_without_repository_makes_zero_provider_calls() -> None:
    memory = MemoryTurnPolicyV3(playerId="player-test", consentRevision=1, nowUtc=NOW,
        currentPlayerSourceId="current-source", focusAllowed=None, quiet=None, explicitRecall=False,
        authorizedQuestionIntent=None, memoryPolicyRequired=True, presentedHistorySourceIds=())
    provider = _BarrierProvider()
    receipt = asyncio.run(generate_scenario_turn(_job(memory=memory), "synthetic-credential", lambda _: None, provider,
        persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))
    assert provider.started == []
    assert receipt.terminalReason == "failed"


def test_only_valid_main_stage_memory_proposal_is_persisted_once(tmp_path: Path) -> None:
    repository = MemoryRepository(tmp_path / "Memory" / "player.sqlite3", "player-test")
    consent = repository.set_consent(True, expected_revision=0, now_utc=NOW).consent
    evidence = MemoryEvidence(playerId="player-test", sessionId="session-1", sourceId="current-source",
        actor="player", scope="real_world_claim", sourceKind="profile_input", text="I enjoy green tea.",
        eventAtUtc=NOW, explicitlyConfirmed=True)
    repository.sync_sources(0, [(1, evidence)], consent, NOW)
    memory = MemoryTurnPolicyV3(playerId="player-test", consentRevision=consent.revision, nowUtc=NOW,
        currentPlayerSourceId="current-source", focusAllowed=True, quiet=False, explicitRecall=False,
        authorizedQuestionIntent="interest", memoryPolicyRequired=True, presentedHistorySourceIds=())
    proposal = MemoryProposal(category="interest", key="beverage.tea", value="green tea", scope="real_world_claim",
        sourceId="current-source", evidenceQuote="I enjoy green tea.")
    provider = _BarrierProvider(main=_main(memoryProposals=[proposal], questionProposal="interest"))
    provider.release["head"].set(); provider.release["main"].set()
    emitted: list[ScenarioStageEnvelopeV4] = []

    receipt = asyncio.run(generate_scenario_turn(_job(text="I enjoy green tea.", memory=memory),
        "synthetic-credential", emitted.append, provider, memory_repository=repository,
        persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))

    assert receipt.main.status == "ready"
    assert repository.list_page("", 10).items[0].value == "green tea"
    main = next(stage.payload for stage in emitted if stage.payload.stage == "main")
    assert main.questionProposal == "interest"
    assert len(main.memoryProposals) == 1
    assert len(repository.list_page("", 10).items) == 1


def test_rejected_main_proposal_batch_persists_none_of_its_proposals(tmp_path: Path) -> None:
    repository = MemoryRepository(tmp_path / "Memory" / "player.sqlite3", "player-test")
    consent = repository.set_consent(True, expected_revision=0, now_utc=NOW).consent
    eligible = MemoryEvidence(playerId="player-test", sessionId="session-1", sourceId="current-source",
        actor="player", scope="real_world_claim", sourceKind="profile_input", text="I enjoy green tea.",
        eventAtUtc=NOW, explicitlyConfirmed=True)
    ineligible = MemoryEvidence(playerId="player-test", sessionId="session-1", sourceId="other-source",
        actor="player", scope="real_world_claim", sourceKind="profile_input", text="I like coffee.",
        eventAtUtc=NOW, explicitlyConfirmed=True)
    repository.sync_sources(0, [(1, eligible), (2, ineligible)], consent, NOW)
    memory = MemoryTurnPolicyV3(playerId="player-test", consentRevision=consent.revision, nowUtc=NOW,
        currentPlayerSourceId="current-source", focusAllowed=True, quiet=False, explicitRecall=False,
        authorizedQuestionIntent=None, memoryPolicyRequired=True, presentedHistorySourceIds=())
    accepted = MemoryProposal(category="interest", key="beverage.tea", value="green tea", scope="real_world_claim",
        sourceId="current-source", evidenceQuote="I enjoy green tea.")
    rejected = MemoryProposal(category="interest", key="beverage.coffee", value="coffee", scope="real_world_claim",
        sourceId="other-source", evidenceQuote="I like coffee.")
    provider = _BarrierProvider(main=_main(memoryProposals=[accepted, rejected]))
    provider.release["head"].set(); provider.release["main"].set()

    receipt = asyncio.run(generate_scenario_turn(_job(text="I enjoy green tea.", memory=memory),
        "synthetic-credential", lambda _: None, provider, memory_repository=repository,
        persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))

    assert receipt.main.status == "failed"
    assert not repository.list_page("", 10).items


def test_failed_optional_head_does_not_fail_a_valid_main_response() -> None:
    provider = _BarrierProvider()
    provider.results["head"] = _provider_result({"beats": []})
    provider.release["head"].set(); provider.release["main"].set()
    emitted: list[ScenarioStageEnvelopeV4] = []

    receipt = asyncio.run(generate_scenario_turn(_job(), "synthetic-credential", emitted.append, provider,
        persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))

    assert receipt.head.status == "failed"
    assert receipt.main.status == "ready"
    assert receipt.terminalReason == "completed"
    assert [stage.payload.stage for stage in emitted] == ["main"]


def test_head_cannot_carry_memory_metadata_and_invalid_stage_is_not_retried() -> None:
    raw_head = {"beats": [_head().beats[0].model_dump(mode="json")], "memoryProposals": []}
    provider = _BarrierProvider()
    provider.results["head"] = _provider_result(raw_head)
    provider.release["head"].set(); provider.release["main"].set()
    emitted: list[ScenarioStageEnvelopeV4] = []
    receipt = asyncio.run(generate_scenario_turn(_job(), "synthetic-credential", emitted.append, provider,
        persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))
    assert provider.started.count("head") == 1
    assert receipt.head.status == "failed"
    assert all(stage.payload.stage != "head" for stage in emitted)


def test_cancellation_drains_both_stage_tasks_and_returns_terminal_receipt() -> None:
    async def run():
        provider = _BarrierProvider()
        emitted: list[ScenarioStageEnvelopeV4] = []
        task = asyncio.create_task(generate_scenario_turn(_job(), "synthetic-credential", emitted.append,
            provider, persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))
        await asyncio.wait_for(provider.both_started.wait(), timeout=1)
        task.cancel()
        receipt = await asyncio.wait_for(task, timeout=1)
        return provider, emitted, receipt

    provider, emitted, receipt = asyncio.run(run())
    assert provider.cancelled == {"head", "main"}
    assert emitted == []
    assert receipt.terminalReason == "cancelled"
    assert receipt.head.status == receipt.main.status == "cancelled"


def test_consent_change_while_stages_are_pending_invalidates_both_outputs(tmp_path: Path) -> None:
    async def run():
        repository = MemoryRepository(tmp_path / "Memory" / "player.sqlite3", "player-test")
        consent = repository.set_consent(True, expected_revision=0, now_utc=NOW).consent
        evidence = MemoryEvidence(playerId="player-test", sessionId="session-1", sourceId="current-source",
            actor="player", scope="real_world_claim", sourceKind="profile_input", text="I enjoy green tea.",
            eventAtUtc=NOW, explicitlyConfirmed=True)
        repository.sync_sources(0, [(1, evidence)], consent, NOW)
        policy = MemoryTurnPolicyV3(playerId="player-test", consentRevision=consent.revision, nowUtc=NOW,
            currentPlayerSourceId="current-source", focusAllowed=True, quiet=False, explicitRecall=False,
            authorizedQuestionIntent="interest", memoryPolicyRequired=True, presentedHistorySourceIds=())
        provider = _BarrierProvider(main=_main(memoryProposals=[MemoryProposal(category="interest", key="tea",
            value="green tea", scope="real_world_claim", sourceId="current-source",
            evidenceQuote="I enjoy green tea.")]))
        emitted: list[ScenarioStageEnvelopeV4] = []
        task = asyncio.create_task(generate_scenario_turn(_job(text="I enjoy green tea.", memory=policy),
            "synthetic-credential", emitted.append, provider, memory_repository=repository,
            persona_path=ROOT / "Tools" / "CompanionRuntime" / "src" / "alteros_companion" / "persona.json"))
        await asyncio.wait_for(provider.both_started.wait(), timeout=1)
        repository.set_consent(False, expected_revision=consent.revision, now_utc=NOW)
        provider.release["head"].set()
        await asyncio.sleep(0)
        provider.release["main"].set()
        receipt = await asyncio.wait_for(task, timeout=1)
        return repository, provider, emitted, receipt

    repository, provider, emitted, receipt = asyncio.run(run())
    assert provider.started == ["head", "main"]
    assert emitted == []
    assert receipt.terminalReason == "failed"
    assert repository.list_page("", 10).items == ()
