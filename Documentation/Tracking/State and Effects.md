---
title: State and Effects
tags: [alteros, design, state]
reviewed: 2026-10-22
---
# State and Effects

Related: [[Feature Register]], [[Agent Tools]], [[Design Decisions]], [[Dialogue Catalogue]].

Mood display and pending numerical/cue catalogue decisions are tracked in
[[Stats and Mood]]. That note distinguishes locked presentation from open
simulation/balancing; existing fields below are not a final approved catalogue.

## Two meanings of status

- Development status: designed, implemented, wired, runtime verified. See [[Maintenance]].
- Game-state transition: a game event changes mode, activity, needs, relationship, or economy. Never infer a runtime transition from a documentation status.

## Trigger and transition register

| Entry / event | Conditions | Result / effect owner | Current standing |
| --- | --- | --- | --- |
| Player interacts | Looking at Alterra; valid range/collider/input | Eligible Scenario continuation before ordinary greeting; existing dialogue/interaction owner | T is wired to `Player.Interact`; E manages inventory/held furniture; physical T/E and current Play Mode acceptance pending |
| Free text submitted, local path | Embedding/release readiness; eligible prepared content; no player-facing mode switch | Shared retrieval/selection, local authored playback; no LLM required | Source-integrated; installed SQL artifact passed read-only inspection; startup/player acceptance pending |
| Free text submitted, online path | Explicit online consent, key, host snapshot and valid request; storage ready | Bounded v4 head/main or supported earlier reply; independent host validation/presentation | Source-integrated; no arbitrary model effects; v4 provider-failure local fallback remains a known gap |
| Ambient idle/proximity | Existing idle/proximity rules and dialogue locks | Non-modal ambient speech | Existing path; attention gate missing |
| Low companion need | Need policy, initiative ceiling, cooldown, attention gate | Quiet self-care or defer an optional shared offer | Approved target; no complete need/activity owner |
| Shared offer accepted | Player acceptance; recheck context and need | Begin game-owned activity; preserve Agent context | Planned |
| Activity performing/checkpoint | Valid active instance/phase and deduplicated evidence | Game-owned effects; preserve earned checkpoint progress on cancellation | Bounded M3 sip checkpoints and SQL receipts exist; general continuous needs simulation is not complete |
| Activity completed | Valid active instance and terminal identity | Correlated result/wait/follow-up; no duplicate payout of already-earned benefits | M3 source contract exists; live return/bark/closing/persistence acceptance remains pending |
| Focus / quiet enabled | Explicit player setting or protected work | Suppress optional takeover/TTS; do not queue a prompt barrage | Approved target; integration absent |
| Generated branch published | Valid schema/IDs/facts; compatible index; explicit speech policy | Add ready user-only content to overlay | Planned; archive status alone is insufficient |

## Existing stat code versus intended needs

Existing implementation: `Assets/Scripts/Progression/`, including `ProgressionTickSystem.cs` and `StatMoodResolver.cs`. Existing fields include energy, engagement, stimulation, attention, reassurance, comfort, affinity, trust, bond, player state, environment, and credits. These are not proof that the approved hunger/hydration design is implemented.

Persistence/effect reconciliation 2026-10-22: the SQLite store rejects
malformed or incomplete schema-4 snapshots before default normalization. The
dialogue runner enables mechanical effects only for host-approved routes in a
pinned validated SQL release, not because JSON came from a Generic directory.
Public `PlayCinematic`, `PlayFromJson`, generic legacy `Play(path)`,
local sequence choices, and test-JSON starts use the untrusted path. The host
allows only known add-only stat IDs, at most 12 combined changes per operation
and at most 5 units per stat delta. Dialogue cannot alter credits or invoke
reward/memory effects; those require their owning validated game or memory
system. Standalone bundled-compiler and source-policy probes pass; the Unity
corrected focused regressions and player acceptance remain pending. Source import/
compilation passed in the 2026-10-22 runtime cutover; this doc review reran neither.
See [[Data Management Handoff]].

Source defaults in the ticker (not approved balancing, not proof that the component runs): per minute energy +0.35 below 75; engagement -0.2; attention -0.15; stimulation -0.1; environment cleanliness -0.02; player focus energy +0.2 outside focus. Serialized settings may differ. The prior audit found no ticker attachment/runtime creation.

The mood resolver gives low energy priority over later conditions; mood/availability are derived outputs, not an LLM emotion report. Consult code for the full ordering. Do not confuse the existing `altera` serialization key with a naming migration.

## Effect contract to record for every new activity

- Stable activity/event ID and authoritative owning system.
- Trigger, prerequisites, explicit consent, cooldown, cancellation, completion.
- Which actor changes: Alterra, player, relationship, environment, or economy.
- Authored effect rates/checkpoints by phase; one baseline-decay owner; pause behavior.
- Separate start/continue/finish conditions and success/cancel/failure outcomes.
- Deduplicated checkpoints/terminal handling; no duplicate completion payout.
- Before/after evidence and failure/no-effect reason.
- Save/reload and duplicate-completion acceptance checks.

No general-purpose “set stat” or “grant currency” tool. Declining care or being away must not cause relationship punishment. Need severity cannot override focus. A low stat can influence eligible dialogue/style, not hijack the player's requested topic.
