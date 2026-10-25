---
title: Startup and Loading
tags: [alteros, startup, navigation]
reviewed: 2026-10-15
---
# Startup and Loading

Related: [[../Home|Home]], [[Feature Register]], [[../SessionZero|Session Zero]].

This is a temporary PoC loading surface, not the final welcome/start screen.
Keep the readiness contract when replacing its appearance.

## Location index

| Owner | Location |
| --- | --- |
| First scene, Build Settings index 0 | `Assets/Scenes/SplashScene.unity` |
| Loading presentation | `Canvas` → `Background`, `Logo`, `StatusText`, `RetryButton/Label` in SplashScene |
| Scene controller | `Assets/Scripts/UI/SplashController.cs`, `PleiadesOS.UI.SplashController`, attached to `Canvas` |
| Model initialization and readiness | `Assets/AlterOS/Semantics/GteSemanticRuntime.cs` |
| Persistent runtime host | `AlterOS Semantic Runtime`, automatically created before the first scene, retained across scene changes |
| Model, tokenizer, route metadata and vectors | `Assets/Resources/AlterOS/Semantics/Models/` |
| Destination, Build Settings index 1 | `Assets/Scenes/ModelTest.unity` |
| Startup regression tests | `Assets/AlterOS/Tests/Editor/SplashLoadingTests.cs`, `StartupResponsivenessTests.cs`; `Assets/AlterOS/Tests/GteSemanticRuntimePlayModeTests.cs` |

The old controller namespace is retained to preserve scene references; it does
not imply a legacy gateway dependency. Change presentation composition through
the Unity Editor, not by editing scene YAML.

## Acceptance contract

- Render Splash while GTE loads resources, prepares dialogue routing, and warms inference.
- Show real stage labels rather than an invented percentage.
- Enter ModelTest only after semantic readiness, not after a branding timer.
- Keep failure on Splash with an explicit retry; never silently enter an unusable Topic surface.
- Disabling/destroying Splash while it is waiting or fading must prevent a future scene-load request.

`SplashController` retains the logo fade/minimum hold, then polls the existing
runtime. Its retry button is visible only for `Failed`; the runtime accepts a
retry only when failed and active, clears failed-attempt metadata, and starts
initialization again. Repeated clicks while loading cannot create parallel
workers. Successful readiness starts asynchronous scene loading into ModelTest.
An absent runtime displays a restart message and does not enter gameplay.

The controller uses scene-owned coroutines, stopped on disable, rather than
allowing a detached async continuation to request a scene after Splash is closed.
Submission of `LoadSceneAsync` is the commitment boundary: once submitted,
Unity does not offer cancellation of that load operation.
Startup yields a complete frame before requesting the model resources. The
runtime's raw stage names are translated into human-readable labels by the
Splash controller; no percentage or estimated duration is shown.

Loading presentation cannot eliminate a long individual Unity/GPU operation.
The previous warm-up check observed a 3.737-second frame hitch; this wrapper is
not a claim that every loading frame is smooth. It does not preload TTS or an
Agent model: those paths have separate readiness/implementation gaps.

## Verification

Verified on 2026-10-15: normal Splash → Ready → ModelTest; synthetic failure UI,
MCP-assisted Retry callback, duplicate-retry rejection, and cancellation while
waiting. Seven final loading Editor cases and all twelve first-party Play Mode
cases passed. The expanded suite also caught an unconsumed warm-up GPU readback;
it is now consumed/disposed before Ready, and retry plus subsequent routing pass.
See [[../SessionZero#Temporary readiness-gated Splash (2026-10-15)|Session Zero evidence]]
for exact jobs, captures, and automation limits. No standalone build or physical
retry-button click was tested. The full Topic/Agent/stat/audio slice is incomplete.
