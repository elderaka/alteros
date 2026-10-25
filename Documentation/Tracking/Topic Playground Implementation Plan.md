---
title: Topic Playground Implementation Plan
tags: [alteros, plan, dialogue, testing]
updated: 2026-10-16
status: in-progress
---
# Topic Playground Implementation Plan

> Executes [[Topic Playground Handoff]].

**Goal:** Preserve the legacy route/stat-context-v1 Playground and add isolated natural-language Topic-family → scenario testing through an Editor window and headless command.

**Architecture:** Legacy route confidence, time-window/numeric eligibility and variant choice stay unchanged. New-family Stage1 matches GTE query vectors against shared family anchors; Stage2 calls `TopicScenarioSelector`, `TopicConditionEvaluator` and `TopicHistory` through one shared Playground session/harness. Window and CLI use the same detached context builder and scenario-step executor. Synthetic new-family fixtures stay outside StreamingAssets; direct family mode is labeled as isolation testing, not semantic-routing evidence.

**Tech stack:** Unity 6000.4.4f1, C#, Unity Sentis, existing tokenizer and baked GTE resources, Unity Editor IMGUI, PowerShell batch launcher.

**Spec:** [[Topic Playground Handoff]]; architecture: [[../Architecture]]; implementation map: [[../UnityProjectContext]].

## Global constraints

- Work only in `E:\Pleiades\alteros`; the sibling `pleiades.os` checkout is archive-only.
- Do not change scene/prefab/serialized YAML or package dependencies.
- Use Generic authored JSON, existing shipped GTE/index and existing semantic threshold/fallback behavior.
- Do not present unsupported numeric stat, mood, or arbitrary context gates as implemented.
- GUI and command share production route/eligibility/variant/traversal logic; inference backend and fingerprint are reported.
- No room scene, player save, remote LLM, TTS, private dialogue, or authored route file is mutated.
- Playground controls enumerate `StatCatalogue`; unknown is absent from StatSnapshot, never zero-filled.
- The optional canonical eligibility contract is version 1, AND-combined inclusive `at_most` / `at_least` comparisons against catalogue IDs. Unsupported versions, operators, common `any`/`none`/`not` forms and existing string context requirements must be reported.
- Production routing builds a read-only snapshot through `CompanionStatSnapshotBuilder`; only energy and engagement are presently known. Hidden player evidence and environment fields are not StatSnapshot inputs.
- Emotion axes are synthetic condition inputs only; do not add update rules, thresholds, decay or activity effects.

## Review focus

- Below-confidence or unsupported GTE results must take the real configured fallback and explain why.
- Hour-window boundary, overnight range, and unknown-hour behavior must match current runtime condition evaluation.
- Invalid, mismatched, or ineligible first variants must not hide a later eligible stable variant.
- Malformed/missing Generic content, model, tokenizer, or baked index must fail visibly rather than return a synthetic success.
- Branch choices, cancellation/reset, repeated query, and headless process exit must not retain stale session state or hang.
- An unset stat must reject a condition as unknown, while explicit `0` remains a real value; non-finite values and out-of-catalogue values must fail validation.
- Editor and scenario inputs must produce identical validation and selected-variant diagnostics; the fixture opt-in must not leak into the authored Generic catalogue.

## Tasks

### Task 1: Shared deterministic selection rules

**Files:** create `Assets/AlterOS/Semantics/TopicRoutePolicy.cs`,
`Assets/AlterOS/Dialogue/CanonicalTimeWindow.cs`,
`Assets/AlterOS/Dialogue/AuthoredDialogueVariantSelector.cs`; modify
`Assets/Scripts/Dialogue/DialogueManager.cs`, `DialogueRouter.cs`, and
`Assets/Scripts/Dialogue/DialogueData.cs`; test under
`Assets/AlterOS/Tests/`.

- [x] Add tests for confidence rejection/acceptance, time-window boundaries, and ineligible-first/eligible-next selection.
- [x] Implement shared policies and diagnostics; route manager/router through the same rules without changing thresholds, fallback route, or stable path ordering.
- [x] Run the focused first-party Editor suite: 36 passed, 0 failed (2026-10-16). A fresh live GTE query is still pending.

### Task 2: Shared inference helper and Editor session

**Files:** create `Assets/AlterOS/Semantics/GteSemanticQueryEncoder.cs`;
modify `GteSemanticRuntime.cs`; add Editor assembly boundary if required;
create `TopicPlaygroundSession.cs` and `TopicPlaygroundWindow.cs` beneath
`Assets/AlterOS/Editor/`.

- [ ] Confirm the existing GTE runtime routing test passes after sharing tokenization/padding/readback preparation; the current Unity bridge went stale before this could be run.
- [x] Implement session loading of the current model/tokenizer/index/Generic JSON, route decision, fallback, diagnostics, and canonical branch traversal.
- [x] Add the Editor window with input, synthetic supported hour, response/choice controls, top matches and per-variant reasons; it does not load a room scene.
- [x] Unity Editor compiled the scripts for the focused test run; Console error query returned zero before the bridge became stale.

### Task 3: Headless scenario command and documentation

**Files:** create `Tools/TopicPlayground/topic-playground.ps1`, scenario
schema/sample, and the batch-mode Editor command; update handoff, feature
register, Session Zero, Semantic Lab README or Home links as appropriate.

- [ ] Run the sample scenario in Unity batch mode with no graphics; verify structured output, chosen route/variant/node and exit code. The launcher refuses while the active Editor is open.
- [x] Implement choice/continue steps and optional expected-route assertions through the same session API as the window; fixture traversal is covered by the Editor tests.
- [x] Run relevant Editor tests (36/36 passed); live inference, batch execution, and cross-surface agreement remain unverified.

### Task 4: Stat-foundation context and canonical numeric eligibility

**Files:** modify `Assets/AlterOS/Dialogue/DialogueDefinition.cs`,
`AuthoredDialogueVariantSelector.cs`, `Assets/Scripts/Dialogue/DialogueRouter.cs`,
the Playground Editor window/session/command, and the supplemental source-check
script; add the shared eligibility/context types under `Assets/AlterOS/Stats/`
and `Assets/AlterOS/Dialogue/`, tests under `Assets/AlterOS/Tests/Editor/`, and
isolated fixture JSON under `Tools/TopicPlayground/Fixtures/stat-context-v1/`.

- [x] Add source tests first for `SyntheticStatSnapshotBuilder` and the shared evaluator: unknown versus zero; inclusive boundaries; non-finite/out-of-range inputs; failed/unsupported conditions; and all-conditions behavior. Supplemental NUnit harness observed RED before implementation.
- [x] Add optional `routing.eligibility = { schemaVersion: 1, all: [{ statId, operation, value }] }`; support only inclusive `at_most` and `at_least`. Missing set means no numeric gate; unknown values reject; unsupported version/operator/string context tags are visible and never pass silently.
- [x] Extend `AuthoredDialogueVariantSelector` to call the same evaluator in production and Playground. Production passes a read-only snapshot from the legacy adapter; the Playground passes only its synthetic snapshot.
- [x] Add clearly marked stat-gate fixtures outside StreamingAssets; EditMode tests target the real canonical selector. The real GTE route/index remains unchanged. Unity execution of these new tests is pending.
- [x] Generate controls from `StatCatalogue`, expose known/unknown separately, add reset and labelled synthetic presets, and default local hour to unknown without reading the wall clock. Visual review is pending.
- [x] Parse scenario stat inputs through the same context builder and include condition diagnostics in Editor and JSON output. Reject unknown fixture-set IDs.
- [x] Add tests for fixture isolation, context-dependent variants, authored fallback and scenario-JSON versus equivalent window-input selection/diagnostics; supplemental suite passes 31/31. These new EditMode tests and actual GTE-backed CLI/window parity remain unverified because the Editor bridge is stale; do not close/discard its unsaved scene.

### Task 5: Evidence and catalogue handoff

**Files:** update `Documentation/Verification/2026-10-16 Stats Foundation.md`,
`Documentation/Tracking/Topic Playground Handoff.md`, `Feature Register.md`,
`Stat Catalogue.md`, `UnityProjectContext.md`, and `TODO.md`.

- [x] Record schema version, supported operators, unknown/unsupported behavior, production legacy mapping, fixture paths, tests run, and Unity/visual checks still blocked.
- [x] Keep Player Context Evidence separate; no player signals were added to the UI or StatSnapshot API. No anchors were rebaked.

**Execution choice:** implement inline in this session, as requested by the handoff prompt. No commits or broad repository cleanup.

### Task 6: Additive Topic family/scenario Playground integration

**Files:** modify the existing Playground session, window and command; add the
shared Topic context adapter/harness and family anchor matcher; extend synthetic
scenario/context DTOs; add isolated `topic-runtime-v1` fixtures and focused
tests/check script; update the Playground handoff, roadmap and feature register.

- [x] Keep `Assets/AlterOS/Topics` read-only and preserve the old generic route
  and stat-context-v1 scenario contract.
- [x] Add shared-anchor semantic family matching (maximum cosine, current 0.45
  minimum, deterministic ID tie order), visibly distinct from direct family
  selection. No semantic anchors were rebaked.
- [x] Convert detached synthetic stats/facts/object snapshots into the existing
  `TopicContext`; preserve unknown separately from zero/false and keep player
  evidence outside Alterra stats.
- [x] Route window and CLI through the same `QueryTopicFamilySteps`, harness,
  selection diagnostics and `ExecuteFamilyScenarioStep` lifecycle path.
- [x] Add deterministic family fixture and batch scenario format for context,
  UTC time, draw, presentation acknowledgement, completion, cancel/fail, retry
  and new occurrence. Scratch persistence is opt-in and constrained to a
  dedicated OS temp folder; failed writes stay pending.
- [x] Add 10 supplemental source tests for unknown/zero, invalid inputs,
  object binding, weighted selection/modifiers, lifecycle, cooldown,
  occurrences, failed save and JSON/window DTO parity.
- [ ] Reconnect the correct Unity Editor, import current scripts, run focused
  EditMode/full Editor suites, then execute the same parity scenario in window
  and CLI. Report live GTE, visual inspection and actual surface parity
  separately. Preserve the open scene; stop before gameplay integration.

**2026-10-16 source evidence:** `Tools/TopicPlayground/Check-TopicPlayground.ps1`
compiles changed runtime and Editor source and passes13/13 tests;
`Check-Stats.ps1` remains31/31; `Check-TopicRuntime.ps1` reruns33/33;
the core folder was not modified in this task. The Unity Editor state remained
stale with external changes dirty, so no Unity import, Test Runner, visual, GTE
inference, or actual CLI/window execution was attempted.
