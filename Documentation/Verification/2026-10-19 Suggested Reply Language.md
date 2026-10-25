---
title: Suggested Reply Display Language 2026-10-19
date: 2026-10-19
tags: [alteros, verification, conversation, ui]
status: source-verified
---

# Suggested reply display language — 2026-10-19

## Cause

The provider prompt required suggested replies to contain `{label, prompt}` but
did not specify their language. The Unity `WorldChoiceButton` label uses the
Latin-only LiberationSans SDF asset. The supplied Console output showed
TextMeshPro replacing Japanese characters `し` and `て` with squares while the
world-space choice layout measured the label.

## Fix

- Prompt now requires both suggestion fields to use the current display
  language, English, matching the generated `displayText`.
- The host preserves a valid generated reply but omits suggestion entries whose
  label or prompt contains non-Latin letters or supplementary characters.
  This prevents the observed Japanese glyph failure without rejecting the
  conversation.
- Authored dialogue and its choices are unchanged.

The host check is a script-level guard, not a general language detector; a
different Latin-script language may pass. If additional display locales are
added, carry an explicit display-language value through the job and validate
against that locale.

## Evidence

- Regression test confirms a Japanese suggestion is omitted while the English
  suggestion and generated reply remain available.
- Full Python suite: **140 passed, 1 skipped**.
- Unified Conversation C# source harness: **115 passed, 0 failed**. This is not
  Unity Editor import, Play Mode, or visual proof.
- Rebuilt the Editor sidecar at
  `Build/CompanionRuntimeM2EnglishSuggestions`; its synthetic-key bootstrap
  returned `ready` and shut down cleanly without a provider request.
- No provider request, TTS, prefab edit, or Unity Play Mode test was run.

## Next acceptance

Restart the current Editor sidecar, rerun one synthetic generated reply with a
suggested follow-up, and confirm the button label renders in English without
TextMeshPro missing-glyph warnings.
