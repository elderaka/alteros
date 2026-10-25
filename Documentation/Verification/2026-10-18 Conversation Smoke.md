---
title: Conversation Smoke 2026-10-18
date: 2026-10-18
tags: [alteros, verification, conversation]
status: partial
---

# Conversation smoke — 2026-10-18

Related: [[../Tracking/Unified Conversation M2 Handoff]],
[[../Tracking/Player Memory Handoff]], [[../Tracking/Conversation Cost Budget]].

## Scope and safety

The player explicitly requested smoke/MCP testing while away, after reporting a
saved OpenRouter key and approximately USD 0.50 credit. Announced scope: keyless
checks followed by at most two synthetic text requests under a USD 0.02 budget;
no TTS. Both requests used the existing provider/prompt/parser via a diagnostic
transport, not the Unity UI. No real player conversation/profile was sent.
The credential was resolved inside the test process and never printed or copied.
No production gameplay code, scene, prefab, package, credential or saved online
setting was changed. No Editor restart/kill, Play Mode or build was performed.

## Keyless results

- `Tools/CompanionRuntime/.venv/Scripts/python.exe -m pytest tests -q`: **119 passed**, 13.69 seconds.
- `Tools/Conversation/Check-UnifiedConversation.ps1`: **97 passed, 0 failed**.
  Scratch artifacts: `.superpowers/sdd/Unified Conversation Implementation Plan/0df5cbba0d0945dba861adbd9aab2d94`.
  This is a supplemental source harness, not full Unity compilation.
- `Tools/TopicRuntime/Validate-TopicCatalogue.ps1`: **57 families / 161 scenarios /
  161 mapped legacy files**, using the production validator.

## Live text results

Pinned model: `deepseek/deepseek-v4.1-flash`. Diagnostic script:
`Tools/CompanionRuntime/smoke_openrouter.py`. Explicit `--live` is required;
the default invocation makes no network request. This is an opt-in diagnostic,
not a production spending guard or a repeatable permission to spend.

The transport reduced `max_completion_tokens` to 768, disabled provider
fallbacks, required parameter support, sorted by price and set `max_price` to
USD 0.30/M prompt, USD 1.20/M completion, zero per-request pricing. It rejected
request bodies over 18,000 UTF-8 bytes and made no retries. These differ from
production defaults; successful smoke does not validate production routing caps.
Price-filter semantics were checked against the
[official provider-routing documentation](https://openrouter.ai/docs/guides/routing/provider-selection#max-price).

| Synthetic case | Result | Input tokens | Completion tokens | Reported cost USD | Provider duration |
| --- | --- | ---: | ---: | ---: | ---: |
| “I am tired. Can we take a break?” | HTTP 200; `existing`, `player_tired` | 1281 | 862 | 0.0001751376 | 26.598 s |
| “Do you like cats? Tell me briefly in one or two dialogue beats.” | HTTP 200; `novel_candidate`, empty topicId; two JP/EN nodes | 1182 | 604 | 0.0001340256 | 21.293 s |

**Total reported cost: USD 0.0003091632** for exactly two text requests.
This is response usage accounting, not an independently reconciled account debit.
Both returned `finish_reason: stop` and passed the existing Pydantic provider
response parser. Cat response used `gentle` and `thinking`, both allowed by the
synthetic turn. The authored response also contained generated prose; production
must ignore that prose and select host-owned authored content, as its source
planner/coordinator tests assert. The provider smoke did not execute that UI path.

Important: the first response reported **862 completion tokens despite a 768
requested limit**. Cause is unverified; do not assume this field is a demonstrated
hard billing ceiling. The diagnostic now stops before its second request if cost
is unknown, negative or above USD 0.01. The very small sample cannot establish
typical quality, average latency, long-session growth or Japanese translation quality.

## Unity/MCP evidence and blocker

- Correct instance observed: `alteros@48a268a4`, Unity **6000.4.4f1**,
  `E:/Pleiades/alteros/Assets`.
- A read-only executed query confirmed not compiling, not playing, only
  `Assets/Scenes/SplashScene.unity` loaded, with `isDirty: false`.
- Console read showed three pre-existing `OpenRouter TTS request failed (400)`
  entries. They were present before any test request in this run; no speech
  endpoint was invoked by this diagnostic. Their root cause is not established.
- Initial test filter incorrectly used `AlterOS.Tests.Editor.ConversationProtocolTests`:
  job `75aae40537f54a8fb7ac2a05bdac9d6d` selected **zero tests**. Its reported
  success is explicitly **not test evidence**.
- Corrected focused filter selected `AlterOS.Tests.ConversationProtocolTests`
  and `AlterOS.Tests.DirectConversationCoordinatorTests`:
  job `d53d1fa7a43c4fd49a2691d4d91d4ca1` started, then result queries reported
  no Editor instance and later timed out. No result/count was obtained.
- Instance listing returned intermittently; Windows still reported Unity
  processes running/responding. Editor log ended after test-run startup. This
  does not establish a crash, deadlock, failure or successful test completion.
  No further test job was submitted, and the Editor was left intact.

## Resume gate

Recover/check the existing Editor safely and inspect the outstanding focused
test result before submitting another run. Then verify current import/compile
and run the no-paid-call startup-through-E smoke with all dialogue/ambient TTS
paths suppressed before starting. Do not enable saved online consent implicitly.
Splash transition, real GTE query, E interaction, Canvas UI, history/late delivery,
M2.1 integration, audio and packaged helper remain unverified in this run.

M2 is **not runtime-accepted** on this evidence. The live-provider/parser boundary
works for these two synthetic turns; the Unity presentation boundary remains open.

## Latency investigation — same day

Player requested investigation of the 21–27-second delay. This investigation
made only unauthenticated public metadata GETs; **no new generation or TTS
requests**, key access, Unity mutation or production-code changes.

### Confirmed boundaries and configuration

- These were direct Python provider calls, not Unity/graph/gameplay turns. GTE,
  startup model loading, SQLite, animation and TTS are outside their measured path.
- Existing reported `providerMs` was 26,598 / 21,293; `totalMs` was 26,599 /
  21,294. Response parsing after provider timing added about **1 ms** in each.
  Provider timing includes connection, server work and full response download;
  it is not a time-to-first-token measurement.
- Production `provider.py` and the smoke transport send no `reasoning` setting.
  The public [models API](https://openrouter.ai/api/v1/models) currently reports
  this model's reasoning metadata as `mandatory: false`, `default_enabled: true`,
  `default_effort: high`, supported efforts `max`, `high`, `low`, `none`.
  Thus the requests leave high-default reasoning in play. Exact reasoning tokens
  used by the two requests were not retained and cannot be established here.
- The smoke transport explicitly set `provider.sort: price` and disabled
  fallbacks. This was a test-only budget precaution, not production latency tuning.
- Public [endpoint metadata](https://openrouter.ai/api/v1/models/deepseek/deepseek-v4.1-flash/endpoints)
  identifies the cheapest endpoint as OpenInference, tag `open-inference/fp4`,
  with prompt/completion/cache-read rates 0.04 / 0.144 / 0.0016 USD per million.
  Both observed charges match these rates **exactly if six prompt tokens were
  cached**. This strongly suggests that route but is not proof of provider identity.
- At inspection, the [model listing](https://openrouter.ai/deepseek/deepseek-v4.1-flash)
  displayed Open Inference P50 throughput **23 tokens/s**, versus Relace 93,
  Decart 79 and some providers over 200. These are changing public aggregates,
  not measured performance for this account/schema/reasoning configuration.
- Observed completion tokens divided by full wall time give **32.41 / 28.37
  tokens/s**. These are end-to-end effective rates, not isolated decoding speed.
- Two metadata requests completed in **0.086 / 0.153 seconds** using one HTTP
  client. This does not prove the original generation connection/queue latency,
  but public API reachability was fast during the investigation.
- The response is intentionally non-streaming and waits for all JSON before
  validation/presentation. This makes the whole generation visible as waiting;
  streaming alone would not shorten server generation or bypass validation.
- The authored-selection response unnecessarily generated two prose nodes
  despite the system prompt saying `existing` has no generated reply. The host
  ignores such prose, but the provider already spent tokens/time producing it.

### Assessment and next experiment

Leading explanation: **high-default reasoning plus cheapest-first serving and
full structured completion**, not Unity or local validation overhead. The prior
test does not establish exact queue, prefill, reasoning, decode or network shares:
its diagnostic receipts omitted generation ID, provider name,
`completion_tokens_details.reasoning_tokens` and detailed network timestamps.
Those cannot be reconstructed reliably from costs alone.

Recommend a separately approved, tiny controlled comparison: same synthetic
turn, schema and pinned provider, change only reasoning from explicit high to
disabled/none; then, separately, compare provider throughput with reasoning held
constant and the same price ceiling. Preserve generation ID, provider, reasoning
token count and measured request duration without prompts/secrets. Where available,
the [generation metadata endpoint](https://openrouter.ai/docs/api/api-reference/generations/get-request-&-usage-metadata-for-a-generation)
can provide provider identity, generation time, latency and native reasoning tokens.
Do not assume `reasoning.exclude: true` disables reasoning; it only hides it
([reasoning controls](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens)).

No fix or paid comparison was executed. Do not promise a particular new response
time until measured. Production's 4096-token cap and broader provider routing also
mean the two budget-limited smoke results are not a general production benchmark.

## DigitalOcean source policy update

After the investigation, the user selected DigitalOcean and the lowest reasoning
setting. `provider.py` now sends `provider.only: [digitalocean]`,
`allow_fallbacks: false`, `require_parameters: true` and `reasoning.enabled: false`
for ordinary and memory-aware turns. Model, VN schema, output limit, no-retry
policy, credentials, permissions and gameplay authority are unchanged. A route
outage uses existing provider-error handling; it never silently picks a different
provider. There is no new production price/spend guard.

The public endpoints API confirmed tag `digitalocean`, support for `reasoning`,
`response_format` and `structured_outputs`, and prices USD0.195/M input,
USD0.78/M completion, USD0.006/M cached input. The model metadata permits reasoning
to be disabled. These are advertised capabilities, not acceptance of a real
schema request on this route. The OpenRouter model page displayed P50 0.83s
latency /168tokens-per-second throughput; those aggregate figures are not our
measured full-response latency.

Regression test added for each shared provider entry point. Both initially failed
because only `require_parameters` was being sent, then passed after the source
change. Full Python suite: **121 passed in 11.61 seconds**, exit0. The opt-in smoke
transport now preserves the production provider policy when adding its own price
ceiling, rather than overriding it with cheapest-first routing.

No new generation/TTS request, Unity mutation, package rebuild or UI change was
performed. `DirectConversationBridge.ResolveHelperPath` still points its Editor
development path at `Build/CompanionRuntimeM2Task4Acceptance/AlterOSCompanion/AlterOSCompanion.exe`,
an older executable. **Source configuration is verified; the running Unity helper
does not acquire these settings until rebuilt/deployed.** A new package must also
respect the current M2.1 source integration/acceptance boundary.

Fast/Think remains a **proposal**, not implemented or fully approved: Fast as
default (reasoning disabled), optional per-message Think for harder tasks (effort
to be agreed/tested), no automatic escalation or repeat request. Same companion
identity, memory, host permissions and bilingual VN output in both. It must not
reintroduce a player-facing Topic/Agent split. Future static controls belong in
an Editor-authored Canvas, not a runtime-constructed HUD.

## Authorized DigitalOcean live comparison

The player explicitly approved trying the new settings. Scope remained at most
two synthetic text requests under the announced USD0.02 ceiling, no TTS or
Unity changes. Provider pin, reasoning disabled, schema, prompt cases and the
diagnostic output/price limits were preserved. The diagnostic now retains
generation ID, returned provider name and reasoning-token count. No credentials
or real player context were logged.

| Synthetic case | Wall time | Provider | Reasoning tokens | Completion tokens | Reported cost USD | Acceptance |
| --- | ---: | --- | ---: | ---: | ---: | --- |
| Authored tired | 2.144s | DigitalOcean | 0 | 1 | 0.000152490 | `invalid_reply` |
| Novel cats | 1.340s | DigitalOcean | 0 | 1 | 0.000036417 | `invalid_reply` |

**Both requests failed usable-response acceptance.** HTTP200 and `finish_reason:
stop` did not mean a valid conversation reply. The second response was inspected
in memory: message keys were `role`, `content`, `refusal`, `reasoning`; `content`
was **null**, with no refusal and no tool calls. The first request's content was
not retained, so only its one completion token and parser rejection are known.
Do not describe these as a successful 10–20x conversation-speed improvement:
there was no accepted dialogue to compare against the original valid replies.

Two generation requests total, no retries, no speech calls. The normal smoke
script stopped after the first invalid result. A separate diagnostic used the
remaining authorized allowance for the distinct cat case, rather than retrying
the failed tired case. **Total reported new cost: USD0.000188907**; this is API
usage reporting, not a reconciled account debit.

Read-only authenticated generation metadata GET for the first response confirmed
DigitalOcean, 778 native prompt tokens, one completion token, zero reasoning
tokens, latency1261ms, generation_time1265ms, native stop and the same charge.
The immediate metadata GET for the second returned404; its completion response
already identified DigitalOcean and its usage. No further generation was made.

Generation IDs for private account-side reconciliation (synthetic tests only):
`gen-1792327479-fi4WOa9oPDj8Aka0WjJ1`,
`gen-1792327511-EkyeQ7jwSqsITSqBrFVE`.

Current conclusion: DigitalOcean routing and reasoning-off are honored in the
reported responses, but **this configuration is not accepted for conversation**.
Cause of the empty response is not established; possible model/provider/schema/
reasoning interaction needs a controlled follow-up, not a silent schema relaxation
or unapproved fallback. A sensible next separately approved test changes only
reasoning to explicit low on the same provider/contract, inspecting raw synthetic
response shape and usage. No setting was changed after these failures. The existing
provider parser rejected the invalid output; no Unity presentation was attempted.
Unity startup/presentation and packaging remain outside this provider test.

## Authorized low-reasoning diagnostic

Player approved the follow-up on DigitalOcean and asked whether the game supports
“Alterra is thinking.” Two synthetic cases were sent with **only the diagnostic
reasoning setting changed to `effort: low`**. Production source remains reasoning
disabled; no helper rebuild, saved settings, Unity asset or TTS change occurred.
Same 768 requested completion-token limit, provider-only routing, strict schema,
price limits, prompts and no-retry policy. No real player history was accessed.

| Case | Wall time | Reasoning tokens | Completion tokens | Reported cost USD | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Authored tired | 3.352s | 335 | 337 | 0.000419445 | HTTP200, stop, null content, invalid_reply |
| Novel cats | 1.515s | 62 | 64 | 0.000090432 | HTTP200, stop, null content, invalid_reply |

Returned provider was DigitalOcean in both. **Total USD0.000509877** reported;
exactly two generation requests, no TTS, no retries. Reasoning tokens were present
but final `message.content` remained null. This disproves the narrow hypothesis
that disabling reasoning alone caused the missing dialogue. It does not identify
which provider/model/structured-output interaction causes it. Do not adopt low
as an accepted fix or claim conversational speedup. The next useful separately
approved experiment isolates structured schema versus ordinary completion on
the same route and reasoning setting; do not weaken the production contract to
make a provider response pass.

Synthetic generation IDs:
`gen-1792327643-MXOBJKtbiS4im6YgSYcj`,
`gen-1792327647-HsR8E4qql2djI5zegJMC`.

### Existing waiting presentation — source inspection only

- `DialogueManager.SubmitConversationText` clears the previous sequence and calls
  `ShowInputBusy("Listening...")` before submitting through the direct bridge.
- `ShowInputBusy` keeps the existing Canvas input panel visible, writes the busy
  message into the input field and makes it non-interactable.
- `DirectConversationBridge.IsTurnInFlight` exposes coordinator pending state;
  the coordinator prevents duplicate submissions. Response/error paths replace
  the busy surface with dialogue or retry input.
- No dedicated LLM-wait “Alterra is thinking” text, automatic thinking pose or
  reasoning-progress presentation was found along this call path. Existing
  expression support is distinct from wiring an expression to request lifecycle.
- The older `FetchAndPlayTTS` thinking filler and loading animation are specific
  to audio generation and gated by TTS settings. They are not proof of an LLM-
  pending presentation and must not be enabled merely to display waiting feedback.
- This is code inspection, not Play Mode/UI/animation acceptance. No waiting UI
  or animation was modified in this test run.

## 2026-10-19 source/recovery follow-up (no provider calls)

No additional OpenRouter/TTS request was made; the null-content DigitalOcean
failure above remains unresolved. Source-only recovery coverage now includes
explicit timeout versus cancellation, cancelled/dismissed late replies, duplicate
submission, helper stop/restart, transcript persistence and memory consent/forget
races. The latest combined conversation/memory source run passed106/106; Python
passed129/129 with the packaged helper smoke enabled. A new Windows onedir helper
was built side-by-side and keyless persistence/restart was exercised. See
[[2026-10-19 Player Memory M2.1 Implementation]] for exact path/hash and the
Unity/player acceptance gaps. This does not replace the stalled Editor test or
the user's own Splash→E test.
