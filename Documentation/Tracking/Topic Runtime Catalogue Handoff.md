---
title: Topic Runtime Catalogue Handoff
tags: [alteros, topics, handoff]
reviewed: 2026-10-16
---
# Topic runtime: catalogue handoff

Related: [[Topic Runtime Implementation Plan]], [[Topic Scenario Selection]],
[[Topic Runtime Playground Handoff]]. Work in `E:\Pleiades\alteros` only.

## Ownership and acceptance

The new `AlterOS.Topics` runtime is additive and not connected to gameplay yet.
The authored corpus migration is now staged at
`Tools/TopicRuntime/StagedCatalogue/`. The existing authored corpus, routing
IDs and baked vectors remain unchanged. No MCP, scene or prefab edits are needed
to author/validate JSON. Do not switch the live loader without an explicit
adapter and routing compatibility check. Do not rebake merely for new scenarios
when shared anchors and routing identities have not changed.

All 57 legacy routes remain separate Topic families. This intentionally avoids
merging time-of-day greetings while v1 has only family-level anchors and no
scenario-local anchor bank.

The staged catalogue contains 57 families and 161 scenarios, with a per-source
mapping. See [[Topic Catalogue Migration]] for conversion decisions and
verification. Headless C# validation accepted the full staged directory;
migration unit checks 8/8, Topic source checks 33/33 and stats/Playground source
checks 31/31 pass. The catalogue remains staged pending Playground/content review.

One `TopicDefinition` owns one shared anchor bank and one or more scenarios.
`TopicInstance` identifies an occurrence, not a scenario. Source `authored`
defaults to the definition's replay policy; source `generated` defaults to
`once`. A new generated occurrence needs a new instance ID even if it reuses
the same topic. Personal definitions are runtime/private content, not shared
catalogue notes. Generation, dedup and publication workers are not implemented.

## Minimal new-family JSON example

This is the new schema, not a drop-in replacement for current route JSON:

```json
{
  "schemaVersion": 1,
  "topicId": "beverage.drink",
  "menuLabel": "Have a drink",
  "prompt": "Ask about having a drink together",
  "anchors": ["Let's get a drink", "I'm thirsty"],
  "defaultReplayPolicy": "repeatable",
  "cooldownSeconds": 60,
  "scenarios": [{
    "scenarioId": "thirsty",
    "priority": 10,
    "baseWeight": 1,
    "condition": {
      "kind": "stat", "statId": "alterra.hydration",
      "operation": "at_most", "value": 30
    },
    "modifiers": [],
    "consumptionNodeId": "",
    "nodes": [{"id": "start", "text": "A drink sounds nice."}]
  }]
}
```

Use actual IDs from [[Stat Catalogue]]; validate the example against that
catalogue before adopting it. Numeric comparisons support `at_most` and
`at_least`, not arbitrary operators. No LLM runs during Topic selection.

Condition vocabulary: `all`, `any` (nonempty `children`), `not` (one child),
`stat`, `timeWindow` (`startHour`, `endHour`), `fact` (`key`, `expected`),
`object` (`objectKind`, children), and inside one object binding only:
`objectFlag` (`key`, `expected`), `objectNumber` (`key`, `operation`, `value`).
An object node's children apply to the same object. Nested object bindings
are rejected. Null root means no additional gate. Missing evidence is unknown,
including under negation. Only true gates compete.

Each modifier has `condition` and finite additive `amount`. True adds its amount;
unknown adds nothing. Final weight is clamped to zero. Highest priority with
positive weights wins; draw among that tier by relative weight. A huge lower
weight never overrides higher priority. Local specificity can be supplied as
explicit context facts; this slice does not infer local anchors automatically.

Limits: 256 scenarios/modifiers, 4096 nodes per graph, depth16/nodes256 per
condition tree, absolute authored weights/modifiers <=1e6, cooldown <=365 days.
These are defensive authoring limits, not game balance.

## Validation

Deserialize into `TopicDefinition` using the project's chosen JSON adapter,
then call `TopicDefinitionValidator.Validate(definition)`; require no errors.
It reuses canonical dialogue graph validation and checks checkpoint membership.
Add catalogue-specific fixtures to catch wrong IDs and intended scenario gates.
`Tools/TopicRuntime/Validate-TopicCatalogue.ps1` loads every staged family into
the actual C# DTOs, invokes `TopicDefinitionValidator`, and checks the
per-source migration map. `python Tools/TopicRuntime/migrate_authored_catalogue.py
--verify` checks source coverage and payload preservation. The source fixture
suite is `Tools/TopicRuntime/Check-TopicRuntime.ps1`; it does not validate an
arbitrary catalogue directory.

Do not add per-scenario consumption flags. Default repeatable families and
explicit once milestones share the same lifecycle contract. Checkpoint delivery
can consume before cancellation; otherwise only successful completion consumes.
Cooldown starts at completion. Selection alone never consumes.
