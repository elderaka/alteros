---
title: Startup and Expression Fix Verification
date: 2026-10-15
tags: [alteros, verification, startup]
status: partial
---
# Startup and Expression Fix Verification

Related: [[SessionZero]], [[UnityProjectContext]], [[Tracking/Feature Register]].

## Scope and result

Fixed missing body-animation trigger commands and replaced the all-at-once GTE
initializer with cooperative loading/warm-up. Startup responsiveness is improved
but not hitch-free; slice 0A remains incomplete. No scene, prefab, animation
controller, package, dialogue JSON, model, anchor, or threshold changes were made.

Environment: AlterOS, Unity 6000.4.4f1, Windows Editor/DX12, GTX 1650,
`alteros@48a268a4`. Project still has no Git commit; unrelated untracked files
were preserved. No provider request or credential change was made.

## Cause and changes

`GteSemanticRuntime.Initialize` previously yielded once, then synchronously
loaded resources, parsed tokenizer JSON, created the worker, scheduled the entire
model, and blocked on readback in one iterator step. The regression reproduced
that contract failure in about 48 seconds. A cached-stage probe measured tokenizer
parsing at 2.893 s, model loading at 0.611 s, and worker construction at 0.063 s;
those samples are diagnostic, not cold-start budgets.

The corrected initializer uses `Resources.LoadAsync`, managed tokenizer parsing
in a task (no Unity asset access off-thread), staged model/worker preparation,
one iterable scheduling layer per frame, and readback completion polling before
Ready. The existing foreground embedding/routing API is unchanged and still
synchronous. Errors still set Failed with a reason; iterator/tensor and worker
resources are released. A longest-step diagnostic identifies remaining hitches.

The ModelTest Animator exposes Trigger parameters `Default`, `Joy`, `Surprised`,
and `Angry`, while the emotion controller previously sent names such as neutral,
happy, curious, and gentle directly. All three expression/reset paths now prefer
an existing case-insensitive Trigger match, then map neutral to Default and happy
to Joy. Unsupported facial-only expressions keep their blend-shape/head/gaze
behavior without inventing a body trigger. Non-trigger parameters are not used.

## Regression evidence

| Check | Before | After |
| --- | --- | --- |
| Six expression cases: neutral/happy/surprised/angry, facial-only gentle, return-to-neutral | 6 failed against original code | 6 passed |
| Initializer yields during loading instead of finishing all work in its second step | Failed against original code | Passed |
| Failed warm-up releases persistent worker | Failed after forced real backend disposal | Passed after failure cleanup |
| Entire first-party PlayMode assembly | Prior coverage was not a full Unity Test Runner run | 11 passed, 0 failed, 0 skipped |

Fault injection deliberately disposes a real worker before scheduling; its
expected initialization error is asserted, not suppressed. It does not indicate
an unexpected production failure. Tests use an inactive temporary GTE host and
an in-memory AnimatorController; fixtures clean themselves up.

Test job references (MCP session evidence; durable observations are recorded here):

- Animator RED: `b9dc113b352641409dbcb6de6a4f5153`.
- Initializer RED: `8c22bc1798c44e9cbb1b92691734376f`.
- Failure cleanup RED: `5c391c55b8264450ad55d8ff1b4d4dae`.
- Final eight-case EditMode GREEN: `ecaeb8a3a9004c5391e2ba2673dc1cf4`.
- Final one-layer PlayMode GREEN: `1a85f15113b644ed84238e07941aa31c`.

## Measured startup limits

| Configuration / run | Observed loading frames | Observed wall wait | Largest observed frame | Longest initializer step |
| --- | --- | --- | --- | --- |
| Cooperative, five layers per frame | 337 | 20.23 s | 7.347 s | 6.201 s, warm-up scheduling |
| Final, one layer per frame | 908 | 17.47 s | 3.737 s | 3.730 s, warm-up scheduling |

Both runs passed the real GTE warm-up/routing test: 678 anchors, 57 routes,
GPUCompute, tired slang selects `player_tired`. Editor focus/cache conditions
differed; these are not a controlled speedup benchmark or GTX 1650 production
performance guarantee. Individual scheduling work, model preparation, allocation,
and Editor reload/import can still stall. No maximum frame-time acceptance budget
was approved, and no claim of smooth startup is made.

## Normal-scene runtime proof

With the fixes and five-layer configuration, started the real SplashScene and
observed its transition to ModelTest. A probe reported frame 1030, game time
20.19 seconds, GTE Ready, and neutral preset. Runtime-only yaw setup and a queued
Input System E event opened the authored greeting, then queued Space reached
the greeting menu. Console error queries returned zero entries at startup and
after expression cues, without manual clearing. The greeting visibly rendered:

`Captures/SessionZero/0a-fixed-e-greeting.png`

Final one-layer tuning was validated through the full PlayMode test assembly,
not a second complete physical-input/scene presentation audit. Play Mode was
stopped; runtime yaw was not saved. Facial animation quality and every possible
expression/controller combination were not visually certified.

## Focused review and remaining work

A read-only independent review found the failed-worker retention issue; its
reproduction was added and the cleanup fixed. It found no Animator mapping
correctness issue. The review also identified the limited performance assertion:
the early-yield regression is not proof that every later frame meets a budget.
Actual frame timings above retain that limitation explicitly.

Still open: single-operation warm-up hitches, ordinary mouse/keyboard and prompt
visibility proof, clipped/overlapping UI, time-ineligible route success bug,
fallback, full branching completion, cancellation/control restoration, speech,
and all later Session Zero Agent/stat/focus requirements. No player build,
cross-platform run, sustained GPU-memory measurement, or active-GPU cancellation
stress test was performed. The earlier native-allocation errors are not declared
fixed merely because this bounded scene run had no Console errors.
