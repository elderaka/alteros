# Intent: `ask`

## Meaning

The player is requesting information, clarification, or Alterra’s opinion.

## Includes

- Questions about activity, status, memory, identity, opinions, and preferences.
- “I don’t understand” when the desired response is clarification.

## Excludes

- Asking Alterra to perform an action (`request`).
- A statement about the player’s own state (`share`).

## Current route coverage

`ask_activity`, `ask_memory`, `ask_missed_me`, `ask_name_or_identity`, `ask_opinion`, `ask_preference`, `ask_status`, `dont_understand`.

## Notes

The requested subject is a Topic. Questions may also carry `tone.serious`, `tone.playful`, or another compatible Tone.

