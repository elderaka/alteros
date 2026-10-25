# AlterOS Semantic Catalog v0

Last reviewed: 2026-10-13. This is the human-review catalogue, not the
runtime index itself. See [UnityProjectContext.md](../UnityProjectContext.md)
for the current implementation and [Architecture.md](../Architecture.md) for
the target routing boundaries.

This folder is the human-auditable seed catalogue for AlterOS dialogue routing. The catalogue is deliberately small: it defines reusable Intent and Tone matrices plus a starter set of Topic IDs. It is a design and review surface; runtime vector assets are derived from accepted entries and their model revision.

## Contents

- `Intents/` — one document for each initial Intent row.
- `Tones/` — one document for each initial Tone row.
- `Topics/` — one document for each initial reusable Topic. Subfolders are namespaces, so `Topics/state/tired.md` owns `state.tired` and `Topics/beverage/coffee.md` owns `beverage.coffee`.

## Starter entries

### Intent

[`greet`](Intents/greet.md) · [`farewell`](Intents/farewell.md) · [`presence`](Intents/presence.md) · [`ask`](Intents/ask.md) · [`request`](Intents/request.md) · [`share`](Intents/share.md) · [`relationship`](Intents/relationship.md) · [`repair`](Intents/repair.md) · [`boundary`](Intents/boundary.md) · [`fallback`](Intents/fallback.md)

### Tone

[`neutral`](Tones/neutral.md) · [`warm`](Tones/warm.md) · [`playful`](Tones/playful.md) · [`serious`](Tones/serious.md) · [`distressed`](Tones/distressed.md) · [`urgent`](Tones/urgent.md) · [`hostile`](Tones/hostile.md)

### Topic

[`state.tired`](Topics/state/tired.md) · [`state.stressed`](Topics/state/stressed.md) · [`state.sad`](Topics/state/sad.md) · [`state.lonely`](Topics/state/lonely.md) · [`state.bored`](Topics/state/bored.md) · [`state.motivated`](Topics/state/motivated.md) · [`activity.focus`](Topics/activity/focus.md) · [`activity.break`](Topics/activity/break.md) · [`activity.work`](Topics/activity/work.md) · [`activity.sleep`](Topics/activity/sleep.md) · [`beverage.coffee`](Topics/beverage/coffee.md) · [`beverage.tea`](Topics/beverage/tea.md) · [`relationship.affection`](Topics/relationship/affection.md) · [`relationship.companionship`](Topics/relationship/companionship.md) · [`personal.memory`](Topics/personal/memory.md) · [`personal.identity`](Topics/personal/identity.md) · [`personal.preference`](Topics/personal/preference.md) · [`environment.weather`](Topics/environment/weather.md)

Local anchors such as `time.night`, `entity.coffee`, a named task, or a user-specific fact remain branch-owned constraints. They do not need universal catalogue documents until the same concept becomes reusable across features.

## Contract

- Every dialogue branch declares at least one `topicId`.
- Intent describes what the player is trying to do; Topic describes what they are talking about; Tone describes how the line is presented.
- A Topic can be combined with other Topics and branch-local anchors. `beverage.coffee` is not a child of `state.tired`.
- New wording adds an anchor to an existing entry. A new response adds a variant. A new reusable concept adds a Topic. A different behavior contract adds or splits an Intent.
- LLM output can suggest IDs and aliases but cannot silently create or promote catalogue entries.

## Bootstrap status

The current 57 migrated routes predate this separation and still store routing examples per dialogue variant. They remain the content and evaluation corpus while the target schema moves reusable anchors into this catalogue. The initial Intent and Tone entries are approved as a starting point; anchor phrases, thresholds, and Topic coverage still require review.
