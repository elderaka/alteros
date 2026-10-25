---
title: Agent Reply Beat Contract 2026-10-19
date: 2026-10-19
tags: [alteros, verification, conversation]
status: source-verified-live-smoke
---

# Agent reply beat contract — 2026-10-19

## Cause

The pinned DigitalOcean completion was valid JSON and stopped normally. Its
judgment used `reply.beats`, while the implementation had incorrectly made
`reply.nodes` the v2 wire contract. The model had also used aliases for
`suggestedReplies` and an extra `memory` field on an ordinary turn. The earlier
diagnostic isolated those schema errors; project direction confirms `beats` is
the intended generated-reply key. Authored Topic JSON continues to use `nodes`.

## Changes

- Changed the generated v2 wire contract to `reply.beats` in both strict
  validators and transcript serialization; each validated beat maps to an
  internal canonical dialogue node for playback.
- Added the exact judgment and VN beat field names/shape to the system contract;
  documented the separate four-field wrapper for memory-aware turns.
- Kept strict Pydantic and host judgment validation. No schema aliases or
  permissive parsing were introduced.
- Added allowlisted, value-free provider diagnostics for response JSON,
  envelope/termination, judgment schema, and usage metadata. The smoke-only
  schema summary reports field paths and error types, never rejected values.
- Unity's protocol allowlist accepts the new diagnostic categories.
- Editor helper resolution now prefers the rebuilt
  `Build/CompanionRuntimeM2ReplyContract` package. Earlier packages were
  preserved.

## Evidence

- A capped smoke against the corrected `beats` contract first exposed that
  `suggestedReplies` items needed an explicit `{label, prompt}` shape too. After
  clarifying that prompt rule, the final synthetic request passed strict parsing
  and host validation: `novel_candidate`, two beats, HTTP 200, 915 input tokens,
  122 completion tokens, reported USD 0.000176817, 1.975 s provider duration.
  No player history, TTS, retries, or raw model text were used/logged.
- Full Python suite: **140 passed, 1 skipped**.
- Unified Conversation C# source harness: **114 passed, 0 failed**. This is a
  supplemental source harness, not Unity Editor import or Play Mode proof.
- The side-by-side Windows onedir build completed with PyInstaller; a bootstrap
  using a synthetic-only credential returned `ready` and clean shutdown without
  making a provider call. PyInstaller warned
  that optional hidden import `tzdata` was not found; package startup still
  passed. No Unity Play Mode or player-visible test was run.

## Next acceptance

Restart the Editor sidecar and test one generated reply in Unity. Confirm the
visible VN beats and supported expressions render correctly. Keep TTS and any
additional provider experiments separately gated.
