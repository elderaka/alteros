---
title: Unified Conversation M1 Handoff
tags: [alteros, handoff, conversation]
updated: 2026-10-16
status: implemented-source
---
# Unified Conversation M1 Handoff

Related: [[Unified Conversation Design]], [[Unified Conversation Implementation Plan]],
[[Design Decisions]], [[Feature Register]], [[../Verification/2026-10-16 Unified Conversation Foundation]].

## Result

M1's deterministic C# foundation is implemented. The conversation direction is
authored-first, retrieval-assisted, and independent of the former player-facing
Topic/Agent switch. This remains a source-level foundation, not a live playable
conversation. The production DialogueManager/Topic loader does not call this API.

- Strict provider-neutral JSON contracts reject unknown/duplicate properties,
  malformed shapes, unlisted Topics, invalid channels and out-of-bound VN
  output. Live beats use ordered Alterra nodes; optional replies are separate
  `label`/`prompt` pairs. The layer executes no action or effect fields.
- Topic scenarios can opt into `direct`/`phone`; missing metadata means either
  channel, preserving existing staged and authored JSON.
- `TopicCandidateRetriever` uses the shared local semantic matcher, compares the
  caller's query/index identities, returns at most eight families and up to
  three real anchors per family, then prepares current authored availability.
  It does not load GTE, call a provider, bake vectors or infer novelty from an
  empty result.
- Conversation selection rechecks host eligibility, keeps priority above
  weights, prefers unseen scenarios within the highest eligible priority tier,
  and repeats that tier when it has no unseen scenario. Existing `Select` stays
  unchanged for current callers.
- Completing a scenario records its count and last-completed UTC. Cancel,
  partial playback and a consumed checkpoint do not imply full completion.
  Repeatability/once consumption remain separate. History saves as schema v2;
  v1 data loads without inventing which scenario was completed and is migrated
  only on a later successful save.
- The pure planner accepts synthetic/host judgments and produces authored,
  generated, clarification or unavailable plans. It does not call LLMs, mutate
  Topic history, publish generated catalogue entries, touch the scene, speak,
  or write game state. Known-but-uncovered generation is deliberately deferred
  with `KnownTopicCoverageDeferred`.

## Files and surfaces

- Runtime: `Assets/AlterOS/Conversation/`, plus focused changes in
  `Assets/AlterOS/Topics/`.
- Tests: `Assets/AlterOS/Tests/Editor/Conversation/`.
- Headless runner and synthetic cases: `Tools/Conversation/`.
- Shared status/design: `TODO.md`, `Documentation/Architecture.md`,
  `Documentation/SessionZero.md`, `Documentation/Tracking/Design Decisions.md`,
  and `Documentation/Tracking/Feature Register.md`.

No scenes, prefabs, authored dialogue files, API credentials, provider settings,
Python packages or runtime model assets were changed. Unity generated the new
`.meta` files during its normal script refresh. The user's Topic Playground
window and `ModelTest` scene were left open; no Play Mode was entered.

## Verification

| Check | Result | Limit |
| --- | --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Conversation/Check-UnifiedConversation.ps1` | **30 passed, 0 failed** | Compiles the relevant source with Unity's bundled Roslyn and runs focused NUnit checks; compiler reports existing unused-field warnings in `DialogueRouteIndex` and fixture DTOs. Includes regression cases for malformed DTOs, generated-reference gating, actual once-only continuation, and unseen-selection draws. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TopicPlayground/Check-TopicPlayground.ps1` | **13 passed, 0 failed** | Supplemental headless source checks. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TopicRuntime/Validate-TopicCatalogue.ps1` | **57 Topic families, 161 scenarios, 161 mapped files accepted** | Validates staged catalogue with production `TopicDefinitionValidator`; does not publish it. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TopicRuntime/Check-TopicRuntime.ps1` | **33 passed, 0 failed** | Fresh regression run; the previously reported local `netstandard` runner failure no longer reproduces. |
| Unity refresh/compile request | Editor returned idle, not compiling; no C# compile errors in captured Console | Existing obsolete-API warnings and two recursive-condition serialization-depth warnings from the open Topic Playground window remain. No scene was saved. |
| Unity `AlterOS.Editor.Tests` EditMode | **149 passed, 1 failed, 0 skipped / 150 total** | The only failure is `TopicRuntime.TopicCatalogueJsonTests.HeadlessValidatorInvokesTheProductionTopicValidator`: its Unity test assembly expects the validator type from `Tools/TopicRuntime`, which is not part of that assembly. This slice did not change that test or relocate the tool. The separate PowerShell catalogue validation passes. |

The Unity bridge briefly disconnected during script compilation and the EditMode
run, then reconnected. Afterward the Editor was idle in `ModelTest`, not playing,
with no external changes pending. The Topic Playground window remained open.
The test result file was written outside the repository at
`C:\Users\lauda\AppData\LocalLow\DefaultCompany\alteros\TestResults.xml`.

## Final review and fixes

A fresh read-only code review found no critical issues and identified five
important issues. All five were addressed before handoff:

1. Generated continuation/clarification/novel plans now resolve every declared
   Topic reference. Blocked or missing additional references are rejected; a
   novel branch also checks the host active Topic even if the judge omits it.
   Only the host-bound active Topic in a valid continuation skips authored replay
   availability, while it must still resolve to a current catalogue definition
   and instance.
2. Public in-memory DTO validation now returns errors rather than dereferencing
   null reference/card collections or a null following node.
3. Missing host expression allowlists reject nonempty cues during semantic
   validation; JSON parsing remains structural and leaves allowlist membership
   to the host.
4. Unseen-preference tests now use draws that ordinary weighted selection would
   send to an already completed scenario.
5. The consumed-continuation test now consumes the exact once-only authored
   instance supplied to the planner, and confirms authored selection is blocked.

All newly added focused tests pass in both the headless harness and the Unity
EditMode suite. After the final clarification-reference assertion was added, its
Unity planner regression was rerun independently and passed 1/1. The one
remaining full-suite Unity failure is the pre-existing tools-only-validator
expectation described above.

Not verified: Play Mode/player-visible conversation; live GTE query through
this planner; model/provider quality, calls or cost; Python/Pydantic protocol
parity; process/credential handoff; shared phone history/UI; TTS; speech timing;
activity actions; generated Topic publication/dedup; runtime latency. The headless
runner proves only deterministic production-source behavior.

## Decisions for the next owner

1. Treat M1 as the conversation-policy seam, not an integrated feature. Decide
   and plan M2 separately before connecting an LLM client or changing the
   production loader.
2. M2 must distinguish a genuinely uncovered existing Topic from a blocked or
   consumed authored experience before permitting generated text for it.
3. Continue is host-bound conversational continuity. The planner allows a
   validated continuation after authored replay consumption; M2 must validate
   its active response/session identity and expiry. It never restarts the Topic
   introduction or repeats activity effects.
4. The hosted embedding experiment remains optional. M1 accepts GTE-compatible
   supplied vectors; no hosted index or model was selected.
5. Preserve the measured Unity limitation above. Do not mark the lone Unity
   failure resolved merely because the separate headless catalogue check passes.

## Source SHA-256

Runtime source snapshot for this handoff:

| File | SHA-256 |
| --- | --- |
| `Assets/AlterOS/Conversation/ConversationContracts.cs` | `b78dbef4f2dcca91107f69b74d766082d80353d5f3c97ec2dc3ced66fd4d2a76` |
| `Assets/AlterOS/Conversation/ConversationContractValidator.cs` | `f52ffafd49bdfbe45bab00834969dc5f47760bb7e3a1f6e3fe03eeaef891b93f9` |
| `Assets/AlterOS/Conversation/TopicCandidateRetriever.cs` | `2692b8ed02d55f0aea0fc398e4f7f25b1c81cd71b8f3cc8f6db1989e2c5ce5fa` |
| `Assets/AlterOS/Conversation/UnifiedConversationPlanner.cs` | `e2c809b5ece27b551d4c75c487aad3e4f18f0a8a2a7f44bf0941db72a3b91a45` |
| `Assets/AlterOS/Topics/TopicDefinition.cs` | `d6a9bf2849c4d394ad3c5969b6c8d8e656053e73f28adc8dca3656e35ab95033` |
| `Assets/AlterOS/Topics/TopicDefinitionValidator.cs` | `ef1c7aba8a576f487bf288a0fe423941899e09fe952fcf5ad2fe19149e8b1006` |
| `Assets/AlterOS/Topics/TopicScenarioSelector.cs` | `05e641ca68d556b799ca8ca1592cd66cfa5d1a3bd14c3c8a423737693e6beadf` |
| `Assets/AlterOS/Topics/TopicHistory.cs` | `ec036bedf30efa33e958b6e20d3a34bd9ed0d7b29189eb135611f9c0923fa296` |
| `Assets/AlterOS/Topics/TopicHistoryRepository.cs` | `abc142d78cc8e09796f4c662cdbe21a31f4fd7c74d663d05d247e76295bd0ffb` |

No commit or clean Git baseline exists in this project checkout. Changes remain
uncommitted in the user's untracked initial repository snapshot.

## Message for the main agent

```text
M1's source foundation is implemented in the shared AlterOS repo. Start with `Documentation/Tracking/Unified Conversation M1 Handoff.md`. The final reviewer found no critical issues; all five important findings were fixed with regression tests. Verification: conversation30/30, Topic Playground13/13, Topic runtime33/33, catalogue57 families/161 scenarios, Unity EditMode149/150. The sole Unity failure remains `HeadlessValidatorInvokesTheProductionTopicValidator`, which expects a Tools-only validator in the Unity test assembly. Editor is idle in ModelTest, not playing, and no scene was saved. No provider calls occurred. Production loader/UI is still unwired. This checkout has no Git commit/baseline; review the focused source paths and don't stage the whole untracked repository. Please make a bounded M2 plan before wiring a sidecar. Preserve the open Topic Playground window and ModelTest scene.
```
