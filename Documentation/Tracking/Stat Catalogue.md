---
title: Stat Catalogue
tags: [alteros, catalogue, stats]
updated: 2026-10-20
status: proposed
---
# Stat Catalogue

Related: [[Stats and Mood]], [[State and Effects]], [[Live Conversation Journal]], [[Topic Playground Handoff]].

## Status and purpose

### Foundation source implemented (2026-10-16)

The nine Alterra IDs/bounds below are implemented by `StatCatalogue` and
`StatSnapshot`; this is not a runtime simulation or persistent-state migration.
The read-only legacy adapter maps energy/engagement, plus hydration only when
the progression state marks it known. M3 source adds a bounded game-owned
checkpoint writer; old saves without hydration metadata stay unknown. Unity
import and ModelTest activity/hydration bridge wiring are confirmed. Editor path
and definition checks pass; hydration persistence and Play Mode behavior remain
unverified. Other values remain unknown. The shared canonical selector now consumes the optional numeric
eligibility contract, and the Topic Playground exposes detached synthetic
context. Supplemental source checks pass 31/31; Unity import/full Editor suite
remain blocked by the stale bridge. See [[Verification/2026-10-16 Stats Foundation]]
and [[Topic Playground Handoff]]. Environment definitions and
[[Player Context Evidence]] remain design-only.

### Approved starter boundaries (2026-10-16)

The user approved six needs for the initial catalogue: energy, hydration,
satiety, engagement, social connection and physical comfort. Physical comfort
is bodily ease, distinct from room coziness; it may influence emotion but
does not determine mood on its own. Exact IDs and balancing remain proposals.

The game follows real time, with the player's local day/night. Separate local
wall-clock context (greetings, day/night) from elapsed simulation time used for
need changes/activity progress. Timezone/clock changes must not duplicate
benefits or cause sudden need depletion. Offline catch-up limits and eventual
cross-device authoritative time are still open.

An initial adequate self-care target band of 50..75 satisfaction was accepted
as a tunable starting point, not a hard cap, floor, instant refill or guaranteed
equilibrium. Recovery must arise from valid activities and available resources;
values may exceed the band. Decay/recovery rates, trigger/stop thresholds and
unavailable-resource behavior still need definition. The fuller life-simulation
and cross-client brainstorm remains roadmap discussion, not current runtime.

Draft for discussion, not runtime configuration, approved balancing or a save
migration. The user selected three groups: Alterra, environment and hidden
player variables. This note proposes concrete fields and their meanings.
Definitions below are deliberately separate from current serialized fields.

The Topic Playground's current canonical numeric rule is optional
`routing.eligibility` version 1: an `all` list of inclusive `at_most` and
`at_least` conditions keyed by exact `StatCatalogue` IDs. Unknown is distinct
from zero; unknown values reject a dependent condition. Other operators,
versions, `any`/`none`/`not` rules and nonempty string context tags are
explicitly unsupported. The only
currently authored examples are labelled synthetic fixtures outside
StreamingAssets. This does not implement stat simulation or justify converting
unknown values to zero. No anchors need rebaking for this contract.

## Proposed record contract

Every numerical definition should specify stable ID, actor/scope, meaning,
unit/range/direction, default policy, persistence, authoritative writer,
natural change, activity rates/checkpoints, condition thresholds and display.
Keep the definition separate from the current value. Inferred player signals
add provenance, confidence, observation time and expiry, not fake certainty.

Thresholds, initial values, rates, offline catch-up behavior and schema version
remain open. Real-time progression is approved above; stable IDs below remain proposals.

## Alterra - needs (proposed)

User confirmed engagement and social connection should be distinct. Other
counterparts may eventually satisfy connection; that does not automatically
change the player's relationship. See [[Brainstorm Inbox]] for the future
socializing proposal and unfinished self-care/absence policy.

All use 0..100 satisfaction: high means satisfied, not urgent. Low hunger
satisfaction is expressed as hungry, rather than interpreting a larger
"hunger" value inconsistently with the other meters.

| Proposed ID | Meaning | Candidate recovery source | Existing field relationship |
| --- | --- | --- | --- |
| alterra.energy | Physical rest/resource | Rest/sleep performing time | energy exists; balancing not approved |
| alterra.hydration | Hydration satisfaction | Actual drinking checkpoints | New legacy field and known flag; old saves remain unknown; checkpoint persistence and runtime acceptance pending |
| alterra.satiety | Food satisfaction | Eating checkpoints | No dedicated field |
| alterra.engagement | Mentally occupied versus bored | Watching, reading, playing, meaningful conversation | engagement exists; review distinction from stimulation |
| alterra.connection | Recent satisfying companionship, not durable affection | Shared activity/conversation | attention/reassurance are not automatic aliases |
| alterra.physical_comfort | Bodily ease | Suitable rest/seating/environment | comfort exists; meaning needs review |

Low connection must not create guilt, punish absence/declines or force the
player to replenish it. Self-directed coping and player focus protection
remain requirements. Exact independent/shared recovery rules are undecided.

## Alterra - relationship (proposed)

Affinity and trust are durable progression, not minute-decaying needs. Their
scales/defaults/event rules remain open. Existing bond XP/level is held for
review, not removed or promised as an unlock system. Do not blindly map old
values or reset player saves when the schema changes.

## Alterra - emotional axes (proposed numeric contract)

| Proposed ID | Proposed range | Meaning |
| --- | --- | --- |
| alterra.emotion.valence | -1..1 | Unpleasant to pleasant |
| alterra.emotion.activation | -1..1 | Subdued to activated, not physical energy |
| alterra.emotion.control | -1..1 | Overwhelmed to perceived control, not tool permissions |

Descriptions such as joyful/frustrated are derived presentation, not extra
independent stat bars. Recent event/target context may preserve mixed feelings;
the exact representation and update rules are still open. Zero on an axis
is a midpoint, not missing data and not necessarily the long-term baseline.
No arbitrary LLM-written updates. Numeric storage/bounds exist; emotional
updates and descriptions are not implemented. See [[Stats and Mood]] for locked display.

## Environment - per-room (proposed)

| Proposed ID | Proposed range | Direction/source |
| --- | --- | --- |
| environment.cleanliness | 0..100 | High is clean; mess/cleaning events |
| environment.coziness | 0..100 | High is cozy; furnishings/room rules |
| environment.noise | 0..100 | High is loud, not inherently bad; active sources |
| environment.lighting | 0..100 | High is bright, not inherently good; room/light rules |

Avoid competing stored and derived authorities. Decide which values are
object-derived before implementation. Time/weather, powered TV, seat occupancy,
room links and use-point existence are context/object state, not these meters.
Temperature and other existing fields remain under review; not silently deleted.

## Player - contextual evidence (proposed)

- Inferred valence: optional estimate with uncertainty; distinct from Alterra's axis.
- Frustration evidence: optional short-lived estimate; not a permanent trait.
- Provenance/confidence/observed-at/expires-at: mandatory when inference is used.
- Focus/quiet and communication preferences: explicit settings/facts, not inferred
  permission or emotional measurements. Unknown/expired inference stays unknown.

Not normally on the HUD does not mean undisclosed profiling. These signals
cannot grant consent, change relationship rewards, diagnose, or override focus.
The player's existing credits/tasks/progression remain separate save data;
these three categories do not authorize deleting those unrelated systems.

## First discussion gate

### Adaptive routines direction (2026-10-16)

Separate routine records from needs/emotions. User opts in initially to learning;
supported observations can gradually adapt unpinned routines without approval
for every small change. Player-entered edits/pins win. Keep observed patterns
distinct from the active reminder schedule; missing reports are unknown, not
skipped meals. Changes must be visible/reversible; large shifts or stronger
reminder behavior need renewed approval. Evidence, adaptation bounds, grace,
cooldown, timezone and reminder timing remain to be specified. No routine
implementation or silent profile inference is approved by documenting this.

Human-rhythm research informs routines, not universal medical decay rates.
Shared care is a bounded scheduling preference, not slower biology or waiting
indefinitely for the player. Actual recorded meals can generate authored Topics
such as asking whether the player has eaten; they do not establish player intake.

Current source audit: [[Stats Revamp Audit]]. Proposed bounded next step:
[[Stats Foundation Implementation Plan]]. Source tasks executed with supplemental
checks; Unity validation remains pending, not an end-to-end completion claim.

### Agreed probabilistic Topic direction (2026-10-16)

Needs contribute urgency/selection weight instead of deterministically firing
a Topic at a threshold. With satisfaction meters, lower satisfaction generally
means more need urgency; do not directly equate satisfaction 90 with a 90%
chance to request care. Eligibility and player focus/consent gates remain hard
rules. At bounded opportunities, choose among eligible candidates and a
"say nothing" outcome. Never independently reroll per frame until it fires.
Support seeded scenario tests and record selection/rejection reasons.
Weights, opportunity cadence, cooldowns and exact probability function remain
unapproved balancing; no runtime picker was implemented by this journal entry.

### Current self-care proposal

Needs change with time alone, but Alterra generally maintains an adequate
baseline through independent activity rather than needing player rescue.
Shared time can be richer without making absence relationship punishment.
The initial 50..75 target band is approved above as a tuning baseline, not an
automatic refill or hard cap. Decay, activity recovery and offline catch-up
rules are not finalized. See
[[Brainstorm Inbox#I-03 - Independent self-care without making her a pet (2026-10-16)]].

### Remaining definition work

Need selection and real-time progression are approved; next settle defaults,
thresholds and recovery rules. Keep runtime naming compatibility (existing altera keys)
separate from public proposed alterra IDs. Give the Playground versioned,
explicitly supported fixture fields only once approved; do not silently alias
attention/reassurance/stimulation to new needs.
