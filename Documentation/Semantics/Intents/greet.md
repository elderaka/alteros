# Intent: `greet`

## Meaning

The player is opening or re-opening ordinary contact with Alterra.

## Includes

- Hello, hi, and time-of-day greetings.
- Returning after an absence when the action is to reopen contact.

## Excludes

- Checking whether Alterra is present (`presence`).
- Ending the interaction (`farewell`).
- Affection or a relationship statement (`relationship`).

## Current route coverage

`greeting_general`, `greeting_morning`, `greeting_afternoon`, `greeting_evening`, `greeting_latenight`, `returning`.

## Notes

Time of day is a Topic/local anchor, not a new Intent. A greeting can still carry any compatible Tone.

