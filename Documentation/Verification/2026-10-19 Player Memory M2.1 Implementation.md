---
title: Player Memory M2.1 Implementation Verification
tags: [alteros, memory, verification, source-only]
updated: 2026-10-19
status: source-and-package-verified-runtime-pending
---
# Player Memory M2.1 Implementation Verification

Related: [[Player Memory Design]], [[Player Memory Implementation Plan]],
[[Player Memory Handoff]], [[Unified Conversation M2 Handoff]],
[[2026-10-18 Conversation Smoke]]. This note supersedes the earlier
[[2026-10-18 Player Memory M2.1 Source Foundation]] checkpoint.

## Result

Tasks 1–3 have source implementation and regression coverage. Task 4 has the
bounded structured response and tested pure policy, but proactive question
history/settings are not wired and pending memory proposals have no confirmation
surface. Task 5 has bilingual authored onboarding data, a tested pure session
model, and durable progress reads, but the first-E interception and Canvas
settings/profile controls are not wired. Task 6's synthetic source/package
checks pass. **M2.1 is not player-ready or fully complete.**

Raw transcript player messages are now stored as `unclassified`, not as
real-world facts. A model proposal that cannot prove eligible source scope is
dropped without blocking an otherwise valid VN response. It is not persisted or
shown for confirmation. Fictional/in-game scope stays distinct from real-world
profile context.

## Verified checks (2026-10-19)

| Check | Result | Evidence boundary |
| --- | --- | --- |
| `Tools/Conversation/Check-PlayerMemory.ps1` | Python **129 passed**; C# conversation/memory source runner **106 passed, 0 failed** | Python + actual production C# sources compiled into the source runner's temporary assembly; not Unity Editor compilation |
| `Tools/TopicRuntime/Check-TopicRuntime.ps1` | **33 passed, 0 failed** | Headless C# source/tests |
| `Tools/TopicPlayground/Check-TopicPlayground.ps1` | **13 passed, 0 failed** | Headless C# source/tests; no GTE inference or Editor window parity |
| `Tools/TopicRuntime/Validate-TopicCatalogue.ps1` | **57 families, 161 scenarios, 161 migration rows validated** | Authored JSON validation only |
| Windows onedir package smoke | Keyless v3 bootstrap; write onboarding cursor; clean shutdown; relaunch and read cursor from a path containing spaces | Fresh packaged `.exe`, temporary synthetic DB, no provider call or developer Python used by the child |

The C# source compiler emitted existing `CS0649` warnings for JSON-deserialized
DTO fields; it reported no errors. The PyInstaller build completed. Its warning
report lists optional/platform-conditional imports (including `tzdata`); the
Windows keyless/package smoke passed. This does not prove packaged generation.

Fresh helper:

- Path: `Build/CompanionRuntimeM2Task6FinalAcceptance/AlterOSCompanion/AlterOSCompanion.exe`
- SHA-256: `8CD51A333C2D7C5BB9E9FFCFC42D68FD412A489D7A442591A49806B9DF4EFB40`
- Size: 12,874,660 bytes
- Build work files: `Build/CompanionRuntimeM2Task6FinalWork/`

The previous `CompanionRuntimeM2Task4Acceptance` and `CompanionRuntimeM2Task6Acceptance`
directories were preserved. The Editor development path now prefers the new
FinalAcceptance package, then falls back to the earlier package paths; Unity
has not recompiled this path change.

## Recovery and persistence coverage

The synthetic/source suites cover:

- Offline authored selection and missing-key fallback without a billable turn.
- Duplicate in-flight submission, explicit cancel versus provider timeout, and
  cancellation racing with post-provider presentation.
- Dismissal/successor presentation: successful late output stays recorded but
  does not reopen or take over the successor surface.
- Transcript paging, more than 120 turns, duplicate event IDs/acknowledgments,
  partial final lines, restart behavior, and safe corrupt-file handling.
- Helper exit/restart/clean stop; old helper is not treated as memory-capable.
- Consent revocation and forgotten-memory suppression while a fake turn is
  pending; no unsafe v2 downgrade when memory policy is required.
- Recent activity expires from automatic current context but remains historical;
  preference correction supersedes only the matching key; forgetting plus
  restart/source replay does not restore the record.
- Fictional wizard scope, rejected unconfirmed game activity, idempotent
  question acknowledgement, and rejection of an unauthorized memory callback.
- Memory context limits: at most 4 core, 3 current, 3 recalled entries and
  3,000 combined value/source characters.
- Synthetic size probe (8 long profile preferences): SQLite file 135,168
  bytes; 3 records returned; 2,388 combined value/source characters; serialized
  context 3,632 UTF-8 bytes. This is a small synthetic fixture, not a player-save
  or large-history performance benchmark.

These use synthetic providers, DBs and transcripts. They do not reproduce actual
Unity process lifetime, Canvas presentation, or real provider responses.

## Remaining gates

- Unity instance discovery succeeded, but a **read-only** Unity Console request
  timed out. No Editor import/compile, EditMode job, scene save, batch-mode
  workaround, Play Mode, visual check, or player test was performed. The Editor
  was not closed or restarted.
- `PlayerOnboardingSession` and `first_meeting.json` are not called from the
  first explicit E interaction. There is no onboarding Canvas or profile/
  memory/settings panel yet; TMP rich-text safety and disclosure presentation
  therefore remain unverified.
- `PlayerQuestionPolicy` is pure and tested but not called by the live turn
  coordinator. The coordinator leaves `authorizedQuestionIntent` empty because
  focus/quiet are unknown and question settings/history are not supplied. No
  unsolicited personal questions are enabled.
- Invalid/unclassified model memory proposals are safely dropped; pending
  proposal RAM expiry, confirmation, dismiss, and UI lifecycle are not built.
- No OpenRouter request or TTS call was made here. The prior authorized M2 smoke
  documented DigitalOcean structured replies with `content: null`; that provider
  issue remains unresolved, so generated dialogue must not be claimed accepted.
  No new spend authorization was given or used.
- The human's requested hands-on `Splash -> ModelTest -> E -> authored/generated
  dialogue/follow-up` test has not been run by this agent and remains the final
  player acceptance step, after Canvas/onboarding wiring and provider diagnosis.

## Next acceptance sequence

1. Recover the correct Editor without discarding its open content; allow normal
   import/compile and inspect the Console. Then run focused and full EditMode
   tests through Unity, not only the headless source harness.
2. Wire the authored first-E onboarding and Canvas settings/profile controls
   through the Editor; keep memory opt-in separate from online-generation
   consent and disclose that forgetting memory does not erase chat JSONL.
3. Complete question-history/settings and pending-proposal confirmation wiring,
   then rerun `Tools/Conversation/Check-PlayerMemory.ps1` and regressions.
4. Resolve the provider's null structured-content behavior only under separate
   user-approved test spend; do not loosen VN validation or silently change
   provider routing.
5. The user performs the final player-facing flow with their own OpenRouter
   key. Record each result separately from source, package, and Editor evidence.
