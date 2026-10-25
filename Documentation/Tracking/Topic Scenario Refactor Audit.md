---
title: Topic Scenario Refactor Audit
tags: [alteros, audit, dialogue, topics]
reviewed: 2026-10-16
status: source-reviewed
---
# Topic Scenario Refactor Audit

Related: [[Topic Scenario Selection]], [[Stat Catalogue]], [[Feature Register]], [[Topic Playground Handoff]], [[Architecture]].

## Scope and evidence

Follow-up recheck later on 2026-10-16: the other session added StatSnapshot-based
eligibility to AuthoredDialogueVariantSelector and DialogueRouter, plus synthetic
Playground context. CanonicalStatEligibility v1 supports all-of numeric leaves;
general any/not/object gates remain unsupported. The baseline table below is
historical, not a reason to remove those additions. See [[Topic Runtime Implementation Plan]]
for the proposed runtime-only scope and preserved downstream ownership.

Source-only comparison against the approved direction in [[Topic Scenario Selection]].
Read canonical schema/validator/catalogue/selector/playback, game router and
completion/cancel paths, menu suggestions, Playground session, session/generated
archive models, repetition APIs and anchor loader/baker. Parsed all 161 Generic
JSON variants across 57 route IDs; nine variants declare a priority other than 1.
No Unity tools, compilation, tests, Play Mode, private save reads, schema/content
edits, rebake or gameplay changes were performed. Concurrent Playground work may
advance after this snapshot; recheck before changing its interfaces.

## Current versus target

| Area | Current source | Refactor impact |
| --- | --- | --- |
| Semantic routing | GTE ranks flat route IDs, with examples stored in each variant; Python loader deduplicates examples per route | Reuse encoder/scoring; move anchor ownership to a shared Topic definition through compatibility mapping |
| Canonical content | DialogueDefinition schema v1 combines route metadata and one dialogue graph; validator requires examples in every definition | Versioned family/scenario schema or compatible envelope; keep reusable graph/node payload |
| File discovery | AuthoredDialogueRouteCatalog derives route ID from immediate containing directory and constructs flat Generic/route/file paths | Must change before nested Topic/scenario layout; merely adding folders will misidentify routes or construct wrong paths |
| Variant choice | AuthoredDialogueVariantSelector sorts paths and picks first valid matching time-eligible graph | Replace selection policy with gate evaluation, explicit priority tier and weighted winner; retain diagnostics shell and stable candidate enumeration |
| Priority | routing.priority exists in schema; current selector does not read it. Menu TopicSuggestion priority only orders/caps suggestions | Existing priority fields/menu sorting do not implement scenario override; migrate semantics deliberately |
| Gates/context | Canonical selector accepts localHour only. requiredContext/blockedContext are declared, but no evaluator consumer found in inspected canonical path. Legacy TriggerConditionEvaluator is flat all-of with numeric/flag/time predicates | Introduce one typed context/condition evaluator used by game and Playground; all/any/not/object bindings are not already supported |
| Weighting | Semantic route score combines anchor similarities; no scenario-weight policy | Separate semantic score from need-driven scenario weights; implement seeded selection/trace, no reinterpretation of embedding similarity as weight |
| Repeat history | ConversationSessionStore can count repeated keys and remembers last ambient line; relevant APIs lack Topic-playback callers in inspected code | Reuse persistence lessons, not claim a functioning replay policy. Add instance-keyed history, cooldown and consumption ownership |
| Generated archive | GeneratedDialogueRecord has generatedDialogueId/wasPlayed/lastPlayedAt; MarkPlayed sets a flag, GetReadyRecords does not exclude it | Not a consumed ledger or shared authored/generated eligibility policy. Keep readiness separate from consumption |
| Generated identity/storage | IDs hash prompt/title; repeated same seed reuses record. Manifest writes under StreamingAssets | Distinct occurrences need stable instance IDs, not prompt dedup identity; private mutable lifecycle state belongs in per-user persistent storage with migration |
| Completion/cancel | DialogueManager has optional completion callbacks, deferred until camera exit; DismissActiveSequence uses EndSequence(false). Router currently starts without a Topic-instance callback | Useful seam, but add explicit playback outcome/checkpoint identity; do not make consumption depend solely on camera-exit UI timing |
| Canonical traversal | DialoguePlayback supports Continue/Choose and IsComplete | Reuse traversal; graph movement/current node is not proof the line/checkpoint was actually presented |
| Playground | Window/batch use TopicPlaygroundSession and shared selector; inspected query signature takes text/localHour, no history/instance context | Extend the shared selection/result contract and synthetic history together; do not build a second selector |

Source anchors: `Assets/AlterOS/Dialogue/{DialogueDefinition,DialogueValidator,
AuthoredDialogueRouteCatalog,AuthoredDialogueVariantSelector,DialoguePlayback}.cs`;
`Assets/Scripts/Dialogue/{DialogueRouter,DialogueManager,TopicOptionCatalog,DialogueData}.cs`;
`Assets/Scripts/Interaction/{ConversationModels,ConversationSessionStore,GeneratedDialogueArchive}.cs`;
`Assets/AlterOS/Editor/TopicPlaygroundSession.cs`;
`Tools/SemanticLab/{semantic_router,bake_gte_anchors}.py`.

## Findings that matter before migrating content

- High migration risk (confirmed source behavior): nested folders and removal
  of per-variant anchors break assumptions in both discovery and validation.
  Preserve route-ID mapping and add legacy compatibility tests before moving files.
- High acceptance gap (confirmed absent source contract): no persistent
  Topic-instance consumption/checkpoint enforcement, including no MarkPlayed
  caller found for current Topic playback. A persisted wasPlayed field is not
  replay prevention. Test siblings, later occurrences, cancellation and reload.
- Medium correctness gap (confirmed): declared priorities are ignored in
  canonical variant selection. Nine non-default values need review rather than
  blindly enabling them and silently changing existing dialogue selection.
- Medium lifecycle risk (source-supported, not reproduced): completion callback
  is tied to camera exit while the target is content delivery. Design an
  idempotent content outcome so cancelled/stale UI callbacks cannot consume a
  successor instance or lose a consumed checkpoint.
- Medium storage/identity risk (confirmed scaffold): archive ready registration
  can mark a record ready even after parse failure, and prompt/title identity
  conflates later experiences. Do not wire this scaffold into new selection
  without deterministic validation/instance identity/private storage.

## Size and recommended staged approach

This is a medium-to-large dialogue-domain change if implemented fully, but not
a model, UI or whole-game rewrite. No credible day estimate from this inspection.

1. **Shared selection core + fixtures (bounded first slice):** candidate gates,
   priority tiers, seedable relative weights and explanations against detached
   context. Keep current JSON/discovery via a compatibility adapter. Numeric
   gates must coordinate with the other Playground session's active extension.
2. **Instance lifecycle + persistence:** whole-Topic replay policy and instance
   identity, independent synthetic ledger, outcomes/checkpoints, idempotent
   per-user saves, restart/cancel tests. Only then wire real one-time Topics.
3. **Family schema + small content migration:** shared routing definition and
   separate scenario graphs for drinking/companionship/entertainment fixtures;
   validators/discovery/conversion and tooling updated together. Preserve
   existing 57 route IDs until a reviewed taxonomy mapping exists.
4. **Broader corpus and generated publication:** refresh route snapshots after
   actual content changes, then connect validated personal instances through
   the same selector. No foreground LLM calls or full generation worker needed
   for the first synthetic test slices.

Reusable: model/tokenizer/vector space, confidence/fallback policy (unless a
separate change is approved), dialogue text/choices/graphs, expression/TTS
presentation, Canvas layout and plain stat snapshots. The existing selector is
already shared: replace its policy once instead of expanding DialogueManager.

## Embedding bake qualification

Conditions, priority, weights and replay-state changes do not inherently change
anchor vectors. However, the Python anchor loader currently hashes every file's
path and raw JSON, and assumes routing.routeId/examples on every JSON file.
New family files would fail that assumption. Update corpus loading/fingerprints
with the schema, distinguish semantic-anchor identity from content identity,
and regenerate compatibility metadata as needed. Re-embed only if actual
anchors/model/vector-space settings change; never claim old bake metadata is
automatically compatible merely because prose/conditions need no new vectors.

## Open decisions / work boundaries

This is a refactor assessment, not an executable implementation plan. Exact
schemas, unknown/negation truth rules, object-binding selection, zero-weight
behavior, cooldown semantics, checkpoint acknowledgement and migration/backups
must be specified before that slice. No tests were added/run by this audit.

## Runtime-only implementation follow-up (2026-10-16)

The approved [[Topic Runtime Implementation Plan]] has since produced the
additive `AlterOS.Topics` core and31 source fixtures. Definitions/scenarios,
unknown logic, same-object binding, highest-positive-weight priority, completion
cooldown, whole-occurrence consumption and metadata persistence are implemented
there. The old selector, corpus, loader and bake identity are not migrated.
Those earlier audit findings remain relevant to the adapter tasks. See
[[Topic Runtime Catalogue Handoff]], [[Topic Runtime Playground Handoff]] and
[[../Verification/2026-10-16 Topic Runtime]] for current scope/evidence.
