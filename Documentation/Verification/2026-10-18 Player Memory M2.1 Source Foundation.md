---
title: Player Memory M2.1 Source Foundation Verification
tags: [alteros, memory, verification, source-only]
updated: 2026-10-19
status: superseded-historical-checkpoint
---
# Player Memory M2.1 Source Foundation

> Historical checkpoint only. Its “not wired” statements describe the state on
> 2026-10-18 and are superseded by [[2026-10-19 Player Memory M2.1 Implementation]].

Related: [[Player Memory Design]], [[Player Memory Implementation Plan]],
[[Player Memory Handoff]], [[Unified Conversation M2 Handoff]].

## Verified

- `Tools/CompanionRuntime/.venv/Scripts/python.exe -m pytest tests -q` from
  `Tools/CompanionRuntime`: **100 passed**.
- `Tools/Conversation/Check-UnifiedConversation.ps1`: **87 passed, 0 failed**.
  The runner compiles the current conversation and memory C# source into a
  temporary assembly and executes its source tests. It is not a Unity Editor
  import/compile or Play Mode test. Existing CS0649 serialized DTO warnings were
  reported; there were no compiler errors.
- Repository/policy subset: **36 passed**; memory stdio/context subset:
  **10 passed**; memory-context/graph subset: **12 passed**.
- v3 protocol and repository cover keyless memory startup, local consent and
  source operations, strict payloads, v1→v3 SQLite migration, durable source
  suppression, session-bound evidence, gapped transcript sequence cursors,
  and v2 contract compatibility.
- Context source checks cover consent-revision fail-closed behavior, profile vs
  fictional scope, suppression of forgotten history, missing source-ID removal
  once memory policy is required, memory as untrusted prompt data (not persona
  rules), and local transcript sync mapping limited to player request events.
- A persistent nonpersonal policy marker is written before opting into memory
  or requesting a forget operation. The conversation coordinator will not
  submit an online turn when memory policy is required but not available; the
  prepared authored path remains usable.

## Not verified / not wired

- No live API key/provider request or TTS call was made; no spend was incurred.
- No Unity Editor import, EditMode run, scene/prefab wiring, Splash→ModelTest,
  first-E onboarding, Canvas visual review, or Play Mode acceptance was done.
  The earlier Editor bridge was reported stale/unresponsive; it was not closed,
  restarted, or modified here.
- The memory-aware context/prompt helper is not yet reached by stdio generation.
  There is no concrete production `IDirectConversationHost` currently reading
  consent/context, maintaining the opt-in transcript checkpoint, assigning the
  source IDs on outgoing history, and constructing a memory-aware turn request.
  Therefore online memory recall, proposal writes, and late-turn revalidation
  are not player-ready features yet.
- Task 4 proposal wrapper/question scheduling, Task 5 authored onboarding and
  Canvas controls, and Task 6 full timeline/package acceptance remain pending.

## Scope and evidence boundaries

All test records use synthetic data and temporary directories. No player memory
database, API key, user history, package manifest, Unity scene, prefab, `.meta`,
or TTS asset was changed. This report confirms source behavior only; do not
interpret it as the M2 Unity acceptance or a production memory feature sign-off.
