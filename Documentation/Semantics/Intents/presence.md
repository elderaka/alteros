# Intent: `presence`

## Meaning

The player is checking whether Alterra is available, listening, or able to continue.

## Includes

- “Are you there?”
- “Are you free?” or “Can we talk?”

## Excludes

- A general information question (`ask`).
- A greeting that does not question availability (`greet`).

## Current route coverage

`you_there`, `ask_availability`.

## Notes

Presence is separate from greeting because it can gate whether a live interaction starts.

