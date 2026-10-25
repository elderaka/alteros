---
title: Runtime Observability
tags: [alteros, design, diagnostics]
status: proposed
reviewed: 2026-10-15
---
# Runtime Observability

Related: [[Feature Register]], [[Dialogue Catalogue]], [[State and Effects]], [[Agent Tools]].

This is the agreed tracking direction, not an implemented inspector or event collector.

## Inspector: why did Alterra do that?

Show selected mode, current activity, attention/focus state, initiative ceiling, authoritative needs, router candidates/scores, branch exclusions, selected variant, playback state, speech source/wait/fallback, pending background jobs, and the most recent game-owned effects.

Distinguish model evidence from permission. A sentiment estimate has confidence, source, freshness, and abstention; it is not a durable player emotion/stat.

## Timeline: what happened?

Proposed event families:

| Family | Useful evidence |
| --- | --- |
| Input and routing | Input event ID, selected mode, candidate scores, exclusions, chosen content ID/revision |
| Playback | Branch/node start/end/cancel, control restoration, stale reply rejected |
| State | Activity/event ID, owner, before/after values, fixed effect and duplicate rejection |
| Agent tool | Request ID, tool ID, validated arguments summary, consent, result/error |
| Speech | Text/content hash, provider/model/voice revision, cache hit/miss, latency, wait/fallback |
| Background | Candidate ID, validation/index/audio state, publication or rejection reason |

Use bounded local retention. Raw player text, model payloads, and private generated dialogue are opt-in diagnostics, not shared catalogue content. Never log credentials. Export a redacted test report only with player/developer intent.

## Acceptance evidence

A useful future report follows one input through selection, presentation, speech, and effects. Mark missing events as unknown, not success. Live Editor access and a zero-error Console are useful but do not establish player-visible behavior.

Session Zero remains the first end-to-end gate; milestone one comes afterward.
