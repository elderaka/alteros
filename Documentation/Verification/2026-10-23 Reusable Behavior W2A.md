---
title: Reusable Behavior W2A Portable Sequence Executor
tags: [alteros, verification, activities]
updated: 2026-10-23
---
# Reusable Behavior W2A — portable sequence executor

## Scope

This slice adds one plain-C# `ActivitySequenceExecutor` implementing the released
`IActivitySequenceRunner` contract. One executor instance owns exactly one sequence
invocation and is intended to be created/hosted by the existing `ActivityHost`; it is not
a second game-wide lifecycle owner. It dispatches only through the injected
`IActivityPrimitivePort`. No concrete scene, movement, furniture, grant, persistence or
`ActivityHost` adapter was added.

## Execution contract

- `Start` validates correlation IDs, the pinned Action reference, run-bound Scenario
  snapshot, normalized sequence and typed input values before dispatch. Action input
  literal values may satisfy step bindings by matching the binding ID; they are converted
  into literals before the shared sequence validator runs. Unused/duplicate inputs and
  input/output binding collisions fail closed.
- The current primitive descriptor's required capability must appear exactly once in the
  trusted Scenario snapshot. Its argument names/count and values must match the snapshot's
  allowlisted bindings before the port is called.
- Primitive requests preserve the full correlation tuple: Scenario run, Flow invocation,
  Flow step execution, physical Action invocation, sequence step and primitive invocation.
  Primitive IDs are unique within the Action invocation (`actionInvocationId:stepId`).
- Result bindings are resolved to literal `FlowValue`s only after a completed-success
  result passes `ValidatePrimitiveSuccessOutputs`. Accepted/running acknowledgements can
  carry validated partial outputs, but those values are never bound. A missing/invalid
  selected output follows the failure edge without publishing any binding.
- Success, failure and cancellation follow only their matching forward edge. An absent
  success edge completes; absent failure/cancel edges terminate as failed/cancelled.
- `Tick` rejects non-finite or backwards time and performs at most
  `MaximumOperationsPerTick` (8) primitive dispatch/completion steps per call. It does not
  own timers or poll the port; an asynchronous port delivers a correlated terminal callback
  through `CompletePrimitive`.
- Exact same-identity/same-payload `Start` replay returns the current invocation state.
  Same-identity changed payload and any second invocation on the same executor are
  rejected. Stale, duplicate and conflicting callbacks cannot mutate current state.
- `Cancel` requests cleanup at most once for the active primitive and follows its cancel
  outcome edge when cancellation completes. `Shutdown` is idempotent, requests best-effort
  cleanup once when needed, closes the invocation and ignores late callbacks. A start
  exception/invalid start acknowledgement also triggers one best-effort cancel request.

## Files

- `Assets/AlterOS/Activities/ActivitySequenceExecutor.cs` — pure one-invocation executor.
- `Tools/ScenarioRuntime/ActivitySequenceFlowTests.cs` — fake-port lifecycle, binding,
  branch, identity, idempotency, cleanup, capability, tick-bound and input-resolution tests.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` — includes the executor in portable
  source compilation.

## Validation

- Test-first evidence: the executor tests initially failed compilation because the runner
  implementation was absent. The shutdown-before-start regression later failed because a
  stopped, never-started instance accepted a new sequence; the guard now rejects that start.
- Focused: `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter ActivitySequence`
  passed **23/23**.
- Full portable suite: `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` passed **90/90**.
  The harness compiles the portable source with Unity 6000.4.4f1's bundled Roslyn; existing
  CS0649 warnings from legacy JSON-populated DTOs remain.
- Static inspection: the executor contains no `UnityEngine`/`UnityEditor` dependency.

## Limits and next gate

The fake port is only a contract test double. No real primitive handler or furniture
reservation proof is integrated; W1B Content/adapters remain owned by DBMS, and furniture
operations remain owned by Furniture. No Unity import/compile, EditMode, Play Mode or player
acceptance was run. Checkpoint policy metadata is preserved but has no host checkpoint
callback in the released runner interface; live execution must not claim checkpoint
effects until the authorized host integration defines and verifies them. The W2 runner
integration window remains gated until Main releases the remaining producer contracts.

## Review

Read-only reviewer: `/root/w2a_runner_review`. The report was delivered in the reviewer
mailbox; no separate review file was created. Verbatim final report:

```text
No concrete Critical, Important, or Minor findings. The scoped code and tests cover the stated executor requirements. I did not run tests or Unity, and made no changes.
```
