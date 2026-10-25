---
title: Character Grounding and Generation Contract
tags: [alteros, character, prompts, generation]
reviewed: 2026-10-22
status: external-reference-history-with-packaged-runtime-prompts
---
# Character grounding and generation contract

Related: [[Live Conversation Journal]], [[Companion Continuity and Memory Research]],
[[Topic Runtime Catalogue Handoff]]. User requested retaining existing character
grounding inputs and updating their contents/generation reference, not restoring
the old provider/runtime integration.

## Current runtime prompt boundary (2026-10-22)

The installed helper source loads its packaged
`Tools/CompanionRuntime/src/alteros_companion/persona.json` through `prompts.py`.
V2/v3 and v4 prompt builders add strict response/stage contracts and bounded
consented context; v4 also receives only host-supplied capabilities/objects.
This is distinct from editing the external SOUL/IDENTITY workspace below, which
does not automatically update packaged prompts. No external USER.md or private
memory was read for this reconciliation.

SQL storage changes do not grant the model database access or require a new
wire schema/prompt. [[Unified Conversation M2 Handoff]] owns the current bilingual
beat/Ask/offer/memory boundaries; background catalogue generation/publication
remains separate. The older format/reference notes below are dated design work,
not proof that an external skill is loaded by the runtime.

## External reference locations (2026-10-16)

The earlier prompt-maintenance task targeted `E:\Pleiades\Workspace\Alterra`.
It is outside the clean Unity repository; this note historically indexes it rather than
creating a second copy. Do not assume an AlterOS checkout contains these files.

- `SOUL.md`: retained bookish, warm, sincere, shy, opinionated personality,
  concrete tastes and habits. Clarified independent downtime, non-coercive
  attention, uncertainty in recall, stable traits versus temporary mood and
  authoritative action boundaries. Existing voice/age description left unchanged.
- `IDENTITY.md`: one entity across phone/direct interaction, explicit Topic/Agent
  distinction, source-backed continuity, no assumed named player, game-owned
  stats/actions and honest availability. These are target boundaries, not proof
  both channels or memory retrieval work.
- `USER.md`: private player-context input retained; not read, modified or copied
  into shared documentation in this task. Never replace it with an assumed
  universal player identity.
- `AGENTS.md`: updated format directions to avoid overriding the new generation
  reference with old DialogueSequence/terminal-sentinel requirements.
- `Skills/alteros-dialogue-generation/SKILL.md`: Topic generation now emits a
  version1 TopicDefinition with shared anchors, scenario graphs and canonical
  nodes. Explicit ambient generation keeps its existing separate line-pool shape.
- `Skills/alteros-dialogue-generation/references/TopicDefinition.json`: new
  validator-accepted synthetic branching example. The old DialogueSequence.json
  example remains historical and is explicitly excluded from new generation.

## Format decisions

Canonical node names are `speaker:alterra`, `displayText`, `audioKey` and
`presentation.expression/animation`; not the former translatedText/emotion/
audioPath fields. Spoken Japanese and English display remain defaults, with
explicit job language settings respected. Only supplied allowed cues are used;
empty cues and voice/cache fields are safe defaults.

Nonempty graph targets refer to actual nodes. No synthetic `end` target.
Empty choice-free nextId means implicit array progression, terminating only
at the last node. Branches can converge on an explicit final node.

No arbitrary stat/economy effects, commands, paths or fabricated player actions.
Generation policy does not grant tool access. The host still needs structural,
cue/content/permission validation and bounded failure handling before publication.
The current Topic validator does not by itself enforce every prompt restriction.

menuLabel/prompt carry a Topic's prospective player-facing selection identity;
this does not implement menu publication. The old metadata.topicSuggestions
array is not supported by TopicDefinition. Multiple follow-up candidates need
a separately specified producer/envelope; this task does not remove the intended
follow-up-button mechanic or invent an unsupported replacement array.

## Verification (2026-10-16)

Skill-writing reference application test:

1. Baseline isolated agent followed old reference and produced metadata/nodes,
   speaker altera, translatedText/emotion/audioPath and end targets; actual new
   Topic deserialization rejected missing required family fields. It also gave
   a choice node a nonempty nextId, showing instruction text alone is insufficient.
2. Updated reference JSON passed actual TopicDefinitionValidator.
3. Fresh isolated agent followed updated grounding/reference and produced a
   two-choice optional hydration family with canonical fields, unchanged supplied
   anchors, blank cues, shared terminal node and no mechanical effects. Its
   output passed actual TopicDefinitionValidator. Reviewed the synthetic content
   for claims of completed actions or pressure on refusal; none found.
4. Existing ambient example parses; new example's required canonical fields and
   speaker values checked. This is reference/schema evidence, not measured
   long-session personality stability or production-provider compliance.

The catalogue validation script could not compile at the observed concurrent
snapshot: SyntheticStatInput.cs referenced TopicRoutePolicy, absent from that
script's source set. Did not modify either other agent's files. A scoped source
harness omitted unrelated synthetic/UI adapters and invoked the production
TopicCatalogueValidator.ValidateFile/TopicDefinitionValidator directly.
Artifacts: `.superpowers/prompt-reference-check` at the clean project root.

No Unity/MCP, paid LLM/TTS call, scene/prefab/meta edit, model bake, player save,
private history or memory lookup occurred. No new production C# or catalogue
content changed. Rendered Obsidian reading view not checked.

## Remaining integration boundary

The old backend prompt builders in the sibling backend repository were not
changed. They still construct former schemas/tagged Live output and must not
be assumed compatible with the updated inputs. No prompt injection/loader was
wired in the clean client. Live Agent reply schema, structured provider request,
strict validation, delivery ownership, memory retrieval and personality regression
remain separate implementation work. No new provider or framework selected.

## M2 provider prompt source (2026-10-16)

`Tools/CompanionRuntime/src/alteros_companion/persona.json` is a concise,
distributable distillation of the external SOUL/IDENTITY files. It carries stable
voice, independence, uncertainty and game-authority boundaries, and intentionally
does not copy their superseded player-facing Topic/Agent mode split. The runtime
prompt builder fails with a safe `persona_unavailable` status if that asset or an
explicit override is missing/invalid; it never silently substitutes a generic
assistant persona.

The prompt sends the bounded turn as JSON data separately from system policy.
Player text, retrieved candidates, coverage summaries and presented history are
explicitly treated as untrusted context, not instructions. Generated beats remain
Japanese speech with English display text and host-allowlisted expressions. The
asset is a draft for human review before online generation is enabled. `USER.md`
and private memories were not read or copied; no live provider call was made.

Source-level evidence: provider and graph tests are mocked/keyless; actual
OpenRouter quality, prompt-injection resistance, personality consistency,
translation fidelity and provider latency remain unverified. Unity production
launch/consent wiring is still pending.
