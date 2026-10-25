---
title: Parallel Appetizer Spike
date: 2026-10-19
tags: [alteros, conversation, latency, verification]
status: isolated-spike-not-integrated
---
# Parallel Appetizer spike

Related: [[../Tracking/Unified Conversation M2 Handoff]],
[[2026-10-19 Agent Reply Schema Fix]], [[2026-10-18 Conversation Smoke]].

## Authorization and scope

Player approved trying the Appetizer design and proposed launching Appetizer
and main preparation concurrently. Announced ceiling: exactly two text requests,
USD0.02 budget, no TTS. Tested one parallel pair for synthetic “Do you like cats?”
on DigitalOcean, pinned `deepseek/deepseek-v4.1-flash`, reasoning disabled.
No second prompt pair or retries. No player history/memory, UI, scene, gameplay,
helper packaging or production provider/orchestration files changed.

Throwaway executable: `Tools/CompanionRuntime/spike_parallel_appetizer.py`.
`--self-test` uses a synthetic credential and MockTransport; `--live` requires
explicit new spend approval. Running without flags sends nothing. Reuses the
credential loader without printing/copying the key, current provider/graph and
strict `reply.beats` validation. Current source uses JSON-object provider mode
with strict local validation; it no longer sends the old provider JSON schema.

The main branch is a fuller **dialogue continuation**, not an implementation or
benchmark of the proposed action-capable Scenario generator. It cannot execute
tools or activities. Both branches receive the same immutable role/content brief;
neither receives the other's generated output. Persona already states Alterra
likes cats. No invented pet ownership or past experience was permitted.

## Measured result — one sample

All times below are seconds since `run_pair` began; this excludes initial Python
process startup and credential resolution. Requests are complete, non-streaming.
Ready means returned and accepted by Python parser plus graph host validation,
not first visible frame, first audible speech or time to first network token.

| Stage | Start offset | Ready offset | Stage duration | Input tokens | Output tokens | Reasoning | Reported USD |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Appetizer, 2 bilingual beats | 0.364 | 2.802 | 2.438 | 391 | 153 | 0 | 0.000195585 |
| Continuation, 3 bilingual beats | 0.629 | 3.291 | 2.662 | 1031 | 196 | 0 | 0.000257157 |

Both returned HTTP200, `stop`, DigitalOcean and valid strict judgments. They
overlapped despite 0.265s setup stagger. **Continuation was ready 0.489s after
the Appetizer. Total reported cost USD0.000452742**, not a reconciled account debit.
The Appetizer met the **provider-side readiness** target in this one sample,
with only 0.198s margin; the three-second player-visible guarantee is unproven.

Generation IDs (synthetic only):
`gen-1792399497-kgxSp64LVT8LB7JJG2DZ`,
`gen-1792399497-TZea9hVRnCFPsHLTO4UJ`.

Appetizer English subtitles:
- “I like cats.”
- “I like their quietness.”

Continuation English subtitles:
- “Yes, I do like cats.”
- “That self-possessed air of theirs — how they'll suddenly come close, yet never mind your convenience.”
- “What do you like about them?”

Japanese counterparts and allowed `gentle`/`thinking` expressions were returned.
Language quality was not independently reviewed.

## What the spike establishes and does not

- Running both requests concurrently avoids deliberately waiting for the head
  before starting the body. It does not make either individual request faster.
- The output exposed **duplicate acknowledgement** despite prompt instructions:
  the main continuation repeated “I like cats.” Two independently valid outputs
  are not automatically one coherent interaction. No deduplication or seamless
  presentation claim is made.
- The branches were short and different from the older complete-response tests;
  do not attribute all latency reduction to concurrency or compare as controlled
  speed ratios. No sequential control was run in this spend allowance.
- JSON/host correctness does not prove semantic agreement. Shared roles help,
  but a production handoff still needs a defined compatibility/admission policy.
- Appetizer reading duration, VN click speed, TTS, fallback at the three-second
  deadline, cancellation, late replies, superseding input, retries and atomic
  presentation were not exercised. No Unity/Play Mode/player-visible proof.
- No generation-spend guard or two-stage protocol was added to production.

## Guards and keyless check

Per-stage transport permits one request only, preserves DigitalOcean-only and
reasoning-off policy, rejects bodies over12000 UTF-8 bytes, and sends
`max_price` prompt0.30/M, completion1.20/M, per-request0. Output token requests:
256 head,768 body; no HTTP retries. These are conservative request constraints,
not independently proven hard account-level spend enforcement. No additional
call follows either result or an unknown cost.

Keyless `--self-test` passed: exactly two outgoing requests, respective token
limits, provider/price/reasoning guards, parser/graph acceptance and overlapping
stage start/end intervals. This is a focused spike self-check, not a full suite
or production two-stage cancellation/consistency test.

## Proposed next design boundary — not implemented

- One player turn ID/version; two generation branches, one presentation owner.
- Tiny head: reaction/acknowledgement only, no actions, fabricated memories or
  questions whose answers the main branch would immediately need.
- Main branch: substantive continuation/proposed scenario, no repeated opening;
  validate against latest world/consent state and already-presented head before
  admission. Shared outline alone is not a proof of consistency.
- If the full reply wins the race, skip the redundant Appetizer. If either
  branch becomes stale, it cannot take a successor UI or execute an activity.
- At three seconds, use a prepared local opening if no valid generated head
  has arrived; provider timing alone cannot enforce a UI deadline.
- Measure repeated samples and actual visible presentation before locking a
  latency promise; integrate only after design approval, separately from this spike.
