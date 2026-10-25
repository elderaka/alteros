---
title: Topic Playground responsiveness verification
tags: [alteros, verification, topic-playground, performance]
updated: 2026-10-22
status: source-and-focused-editor-tests-passed
---
# Topic Playground responsiveness verification — 2026-10-22

## Scope

Fix the Editor repaint hitch caused by a display property reading and hashing
the pinned 611,121,320-byte GTE ONNX file. Preserve routing, model selection,
anchor/index identity, and query/load behavior. Work was limited to the Topic
Playground session/window/command, its source checks, and tracking notes.

## Root cause

`TopicPlaygroundWindow.OnGUI` displayed `TopicPlaygroundSession.ModelFingerprint`.
That getter synchronously opened and SHA-256 hashed
`Assets/Resources/AlterOS/Semantics/Models/gte-multilingual-base-fp16.onnx` on
every repaint, including layout and mouse events. The file is 611,121,320 bytes.

## Change

- Added `TopicPlaygroundModelFingerprint`: a cached status/digest with a
  cancellable `ThreadPool` worker, streaming SHA-256, stable-file checks, and
  single-flight invalidation. The UI getter does no file I/O or hashing.
- The exact model asset import/delete/move callback invalidates the cached
  fingerprint. An obsolete in-flight result is discarded before the latest
  generation is published. Streams and hash objects are disposed on every path.
- The window displays calculating, digest, or failure status and repaints when
  the cached value changes. The command keeps the existing `modelFingerprint`
  field and adds `modelFingerprintStatus`; an unavailable digest is not
  substituted with status text.
- GTE loading and query callbacks were inspected. They remain initiated by
  explicit UI actions and advanced from `EditorApplication.update`, not by
  repaint. Their existing synchronous model-load/warm-up/inference steps were
  not changed and may still cause a separate frame hitch.

## Evidence

- RED: the focused source test failed because the fingerprint cache did not yet
  exist.
- GREEN: `Tools/TopicPlayground/Check-TopicPlayground.ps1 -Filter
  TopicPlayground` — **15 passed, 0 failed**. This compiles the Playground
  runtime/Editor/command sources and covers repeated cached-display reads while
  a temporary file is exclusively locked, file invalidation/re-hash, visible
  hash failure, and recovery retry. Existing nullable JSON DTO `CS0649`
  warnings were emitted by the source compiler.
- Unity 6000.4.4f1, active project `E:\Pleiades\alteros`: refreshed assets and
  scripts; current Console has **0 errors**. It showed 26 warnings in unrelated
  existing scripts (obsolete object-find APIs, TMP wrapping API, and an unused
  field).
- Focused Unity EditMode job
`47bb00ad67ac46a8bff31432167393a8` —
  `AlterOS.Tests.TopicPlaygroundFingerprintEditModeTests.CachedDisplayInvalidationAndFailureStayNonBlocking` —
  **1 passed, 0 failed**. It uses temporary files and checks successful hashing,
  cached reads while locked, invalidation, missing-file error visibility, and
  retry after the file returns.
- The correct Editor remained on `Assets/Scenes/ModelTest.unity`; no scene,
  prefab, dialogue content, anchor, index, or user save was changed. No GTE
  inference, actual 611 MB hash, rebake, provider call, or batch-mode run occurred.

## Not verified / next check

No visual inspection or live EditorWindow repaint/Profiler comparison was run.
The tests prove the displayed fingerprint accessor stays on cached state and
does not reopen the file; they are not a measured end-to-end frame-time result.
Actual GTE inference and window/CLI report parity remain separate acceptance
gates. Do not load/warm GTE solely to measure metadata; the next normal
Playground use can visually confirm the status transitions without changing
routing behavior.
