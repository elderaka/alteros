from __future__ import annotations

from pathlib import Path

from alteros_companion.contracts import parse_envelope
from alteros_companion.prompts import MEMORY_SYSTEM_CONTRACT, build_prompt


ROOT = Path(__file__).resolve().parents[3]


def _job():
    fixture = ROOT / "Tools" / "Conversation" / "Fixtures" / "v2" / "turn-valid.json"
    return parse_envelope(fixture.read_bytes()).payload


def test_prompt_names_the_exact_judgment_and_dialogue_wire_fields() -> None:
    system = build_prompt(_job())[0]["content"]

    for field in ('"topicId"', '"referencedTopicIds"', '"reply"', '"beats"',
                  '"suggestedReplies"', '"displayText"', '"language"', '"expression"'):
        assert field in system
    assert "Do not use aliases such as `nodes` or `suggestions`" in system
    assert "Each suggested reply must be an object with exactly `label` and `prompt`" in system
    assert "Suggestion text must use the display language; the current display language is English." in system


def test_memory_prompt_names_the_exact_wrapper_fields() -> None:
    assert all(field in MEMORY_SYSTEM_CONTRACT for field in (
        "`judgment`", "`memoryProposals`", "`questionProposal`", "`usedCallbackIds`"
    ))
    assert "Do not add a `memory` field." in MEMORY_SYSTEM_CONTRACT
