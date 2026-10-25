---
title: Semantic Index
tags: [alteros, catalogue, semantics]
reviewed: 2026-10-15
---
# Semantic Index

Related: [[Home]], [[Tracking/Dialogue Catalogue]], [[Tracking/Design Decisions]], [[Semantics/README]].

The existing individual documents are the review surface. No duplicate taxonomy was created. Their catalogue is an approved starter design, not the current flat runtime index.

Intent = what the player is doing; Topic = subject; Tone = delivery. Every target branch needs canonical topic identity even if deterministic local anchors suffice. New paraphrases extend an existing bank; more dialogue alone does not justify more universal IDs.

## Intent

- [[Semantics/Intents/ask|ask]]
- [[Semantics/Intents/boundary|boundary]]
- [[Semantics/Intents/fallback|fallback]]
- [[Semantics/Intents/farewell|farewell]]
- [[Semantics/Intents/greet|greet]]
- [[Semantics/Intents/presence|presence]]
- [[Semantics/Intents/relationship|relationship]]
- [[Semantics/Intents/repair|repair]]
- [[Semantics/Intents/request|request]]
- [[Semantics/Intents/share|share]]

## Tone

- [[Semantics/Tones/distressed|distressed]]
- [[Semantics/Tones/hostile|hostile]]
- [[Semantics/Tones/neutral|neutral]]
- [[Semantics/Tones/playful|playful]]
- [[Semantics/Tones/serious|serious]]
- [[Semantics/Tones/urgent|urgent]]
- [[Semantics/Tones/warm|warm]]

## Topic

- [[Semantics/Topics/activity/break|activity.break]]
- [[Semantics/Topics/activity/focus|activity.focus]]
- [[Semantics/Topics/activity/sleep|activity.sleep]]
- [[Semantics/Topics/activity/work|activity.work]]
- [[Semantics/Topics/beverage/coffee|beverage.coffee]]
- [[Semantics/Topics/beverage/tea|beverage.tea]]
- [[Semantics/Topics/environment/weather|environment.weather]]
- [[Semantics/Topics/personal/identity|personal.identity]]
- [[Semantics/Topics/personal/memory|personal.memory]]
- [[Semantics/Topics/personal/preference|personal.preference]]
- [[Semantics/Topics/relationship/affection|relationship.affection]]
- [[Semantics/Topics/relationship/companionship|relationship.companionship]]
- [[Semantics/Topics/state/bored|state.bored]]
- [[Semantics/Topics/state/lonely|state.lonely]]
- [[Semantics/Topics/state/motivated|state.motivated]]
- [[Semantics/Topics/state/sad|state.sad]]
- [[Semantics/Topics/state/stressed|state.stressed]]
- [[Semantics/Topics/state/tired|state.tired]]

## Anchors and migration

Current trigger examples remain in authored JSON and are copied verbatim into route snapshots. The existing topic notes contain meaning, coverage, and anchor plans, not fully evaluated runtime banks.

For each reviewed entry, retain stable ID, meaning, positive and ambiguous/negative examples, related IDs, model/index revision, evaluation evidence, and change rationale. Track branch-specific conditions separately from fuzzy paraphrases. Coffee at night is not automatically a sleep request.

No automatic mapping from all 57 routes to this taxonomy was invented. Route notes link exact ID mentions in existing notes as review references only.
