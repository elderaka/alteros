---
title: Conversation Cost Budget
tags: [alteros, cost, conversation, research]
updated: 2026-10-18
status: estimates-with-linked-smoke-evidence
---
# Conversation Cost Budget

Related: [[Unified Conversation Design]], [[Unified Conversation M2 Handoff]],
[[Player Memory Handoff]], [[Player Memory Design]].

## Pricing snapshot and assumptions

2026-10-18 source update: conversation generation is now restricted to
DigitalOcean (`provider.only: [digitalocean]`, fallbacks off), with reasoning
explicitly disabled. Public endpoint rates at inspection: **USD0.195/M input,
USD0.78/M output, USD0.006/M cached input**. No fresh paid request measured this
configuration, and existing helper binaries are not rebuilt. Earlier cheapest-
first smoke prices/timings below are historical, not this route's performance.
See [[../Verification/2026-10-18 Conversation Smoke#DigitalOcean source policy update|source update and verification]].

Public OpenRouter DeepSeek V4.1 Flash listing inspected2026-10-18: lowest displayed
rates$0.04/M input and$0.144/M output; several standard providers list$0.30/M input
and$1.20/M output, with other/faster routes higher. A low advertised rate does not
guarantee that route supports our strict schema or serves a given request. Provider
code currently sets require_parameters=true but no provider price ceiling.
[Model and provider rates](https://openrouter.ai/deepseek/deepseek-v4.1-flash).

For budgeting below use$0.30/M input,$1.20/M output, uncached, text generation only.
This is a planning rate, NOT an implemented hard maximum or observed spend.
Input/output token quantities are illustrative workloads, not measured current
DeepSeek tokenization. Bilingual text, JSON, suggestions, memory proposals and
possible reasoning consume output; instructions/candidates/history/schema/context
contribute to billed input. Credits/top-up fees and unrelated account usage are
not modeled. No private account/key/balance was inspected.

`costUSD = (inputTokens * inputRatePerMillion + outputTokens * outputRatePerMillion) / 1000000`

| Illustrative request | Input | Output | USD/request | USD/100 requests |
| --- | ---: | ---: | ---: | ---: |
| Authored-selection judgment | 4000 | 200 | 0.00144 | 0.144 |
| Generated exchange | 8000 | 800 | 0.00336 | 0.336 |
| Long generated reply | 12000 | 2000 | 0.00600 | 0.600 |
| Same input, output reaches configured4096 limit | 12000 | 4096 | 0.0085152 | 0.85152 |

At the generated-exchange workload,30requests cost approximately$0.1008 and
100requests$0.336 at the planning rate. At$0.04/$0.144 the same request would be
$0.0004352, but don't use the lowest route as a guaranteed testing budget.

## Session growth and actual source limits

Source inspection: provider.py fixes model deepseek/deepseek-v4.1-flash,
max_completion_tokens4096, one generation request/no automatic HTTP retries.
The earlier pre-update source had no explicit reasoning-mode setting. Current
source explicitly disables reasoning; there is still no cost-based session cutoff.
Limits include12 recent utterances/6000history characters,10000candidate/coverage
characters,8000characterRules characters and a64KiB inner turn payload. Proposed
memory context is bounded separately; these are not an exact input-token ceiling.
Full serialized prompts/schema/metadata must also be measured as memory integration
finishes. The provider route is not pinned by model ID alone.

Bounded recent history avoids the growing-full-transcript pattern where later
requests resend every prior turn. Average turn cost can rise during warm-up, then
approximately plateau if context/metadata remain bounded. Lifetime SQLite/JSONL
storage growth does not by itself require injecting all stored content. Natural
recall/proposals are designed to share the same response, not add extraction calls.
At a steady mean turn cost, total session cost is approximately linear in turns.

Offline prepared dialogue does not call the LLM. Online LLM selection of an
authored response still costs input plus a short judgment, even if no dialogue
prose is generated. One turn can contain many VN nodes; count requests, not nodes
or canonical Topic families, when forecasting charges.

Prompt caching may reduce input charges, but provider/matching-prefix conditions
matter; no cache discount is assumed in the table.
[OpenRouter caching](https://openrouter.ai/docs/guides/best-practices/prompt-caching).

## Before live acceptance

- Finish source integration, compile/automated checks and no-key readiness first;
  don't spend credits diagnosing a missing helper/wiring failure.
- Recommend an explicit provider price ceiling, bounded test request count and
  per-session spend guard before broad testing; not implemented by this note.
- Capture per-request input/output/cached/reasoning tokens when supplied, provider,
  cost, status and cumulative spend without storing prompts/secrets in shared docs.
- Existing provider reads usage/cost on accepted responses. Its failure path can
  return zero tokens/cost unknown after rejection/timeout; do NOT interpret that
  as a free request. Preserve known billed usage on invalid output, and reconcile
  ambiguous failures against provider activity rather than relying on zeroes.
- First real samples calibrate estimates; no narrow dollar cap is guaranteed until
  price/usage enforcement exists. Human-owned final tests use explicit consent.
- TTS remains separately priced by characters and is excluded from all rows.
  Existing Qwen adapter is still configured; selecting Kokoro requires the M3
  migration and model/voice-aware cache handling first.

This was read-only source/public pricing research plus a saved report. No provider
generation/synthesis call, private key read, account query, Unity operation or
runtime change occurred. No token count, latency or billing measurement is claimed.
