---
title: Stats and Mood
tags: [alteros, design, stats, emotion]
updated: 2026-10-16
status: partial-design-approved
---
# Stats and Mood

Related: [[Live Conversation Journal]], [[State and Effects]], [[Design Decisions]], [[Semantic Index]], [[Feature Register]].

## Locked player-facing mood presentation (2026-10-16)

- Collapsed view shows one short current-mood summary, such as "Alterra seems
  cheerful." A daily label requires an explicitly defined slower daily mood;
  do not imply a whole-day assessment from a momentary state.
- Expanding shows her current nuanced description and unnumbered emotional
  spectra. Example descriptions are alternatives, not a list of simultaneous
  statuses: "Relaxed and content", "Frustrated, but determined", or "Quiet and
  downcast". Mixed feelings may be described without forcing an exclusive label.
- Pleasantness, activation and perceived control explain the underlying
  dimensions. Literal labels such as "Pleasantness: positive" are placeholders
  for later authored flavor text, not locked final copy.
- Supporting palette: green/teal for relaxed/content, yellow/gold for cheerful/
  excited, blue for subdued/downcast, orange/red for agitated/frustrated.
  These are authored visual conventions, not scientific measurements or a
  single good-to-bad/severity gradient. Text remains understandable without color.
- Raw numerical values belong in developer inspection, not the default player
  mood display. Needs may use bars/numbers; emotion is not a reward meter.
- Display labels and permitted expressions use the same authoritative game
  state, not an independent LLM opinion. Use meaningful stable changes rather
  than flickering labels for tiny fluctuations. Thresholds, transition timing,
  exact colors and final flavor wording remain to be defined.
- Static panel, tracks and styling must be authored through Canvas/prefabs;
  runtime binds state and animates the authored presentation.

Approval is for design direction. No panel, emotional axes, or conditional
expression catalogue was implemented or runtime verified by this journal pass.

## State organization and open catalogue work

[[Stat Catalogue]] holds the initial field-level proposal. Root `TODO.md`
tracks the cross-session work queue; the Topic Playground is being handled
in another Luna session per the user's report, not independently verified here.

User-requested groups are Alterra, environment, and hidden player variables.
Within Alterra, distinguish needs, durable relationship progression, and
short-lived emotional state. Exact needs/relationship fields are still a
starter proposal, not final approved balancing or a migration specification.

The proposed emotional representation uses valence, activation and perceived
control axes plus relevant event context. A single joy-minus-sadness number
cannot preserve every mixed feeling. Actor/target matters: enjoying the
player's company while frustrated with a game is not automatically "tilted".
Exact event appraisal/update rules, axis ranges and mixed-target storage remain
open; no psychological model is claimed to be an exact simulation of a person.

Player sentiment remains uncertain, confidence-bearing, expiring evidence.
Hidden from the HUD must not mean undisclosed profiling. It cannot grant
consent, change authoritative relationship state, or override explicit focus.

## Research references considered

- [Russell (1980), A Circumplex Model of Affect](https://pdodds.w3.uvm.edu/research/papers/others/1980/russell1980a.pdf): valence and activation foundation.
- [Mehrabian (1995), Framework for a comprehensive description and measurement of emotional states](https://pubmed.ncbi.nlm.nih.gov/7557355/): pleasure, arousal and dominance; perceived control is our proposed player-facing adaptation.
- [Larsen, McGraw and Cacioppo (2001), Can People Feel Happy and Sad at the Same Time?](https://leeds-faculty.colorado.edu/mcgrawp/pdf/larsen.mcgraw.cacioppo.2001.pdf): evidence of mixed happiness/sadness; a single bipolar score is a simplification.
- [Fontaine et al. (2007), The World of Emotions Is Not Two-Dimensional](https://journals.sagepub.com/doi/abs/10.1111/j.1467-9280.2007.02024.x): additional dimensions in emotion-word meanings; not proof of universal game thresholds.
- [Scherer and Moors (2019), The Emotion Process](https://www.annualreviews.org/content/journals/10.1146/annurev-psych-122216-011854): event appraisal as context for emotional responses.

The game-specific palette, labels and numeric mappings are design choices,
not validated outputs or scales supplied by these papers.

## Before implementation: bounded catalogue reconciliation

1. Define stable stat IDs, meanings, actor, ranges, defaults, persistence,
   natural change, effects, and threshold semantics. Review existing fields
   as keep/replace/remove; preserve saves through an explicit migration.
2. Define mood regions, mixed descriptions, baseline/recovery rules and
   anti-flicker behavior against synthetic state fixtures.
3. Inventory supported expression/gesture IDs, their assets, prerequisites,
   activity compatibility and neutral fallback. Revalidate after generation.
4. Review affected semantic Tone/Topic notes and dialogue conditions for
   mismatched terms. Player Topic state.sad is not an assertion that Alterra
   herself is sad; Tone is delivery, not an authoritative emotion value.
5. Update activity effect contracts to phase-specific rates/checkpoints,
   distinct from completion outcomes. Establish one owner for baseline decay.

This does not require renaming all routes, rewriting all dialogue, or rebaking
GTE merely to change mood display. Revisit embeddings only when routing
anchors, catalogue/index content, or model configuration actually change.

Immediate Live conversation/optional TTS remains the agreed next gameplay
slice. Full needs simulation, furniture activities, player sentiment and
background adaptation must not silently become prerequisites for a reply.
