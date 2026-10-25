# Intent: `request`

## Meaning

The player is asking Alterra or the game to do, start, stop, or help with something.

## Includes

- Task help and motivation requests.
- Focus, work, break, sleep, tea/food, or companionship actions.

## Excludes

- Merely asking what Alterra is doing (`ask`).
- Reporting that the player already feels or did something (`share`).

## Current route coverage

`ask_task_help_short`, `back_to_work`, `finish_focus`, `food_or_tea_ritual`, `player_needs_motivation`, `sit_with_me`, `sleep_nudge`, `start_focus`, `take_break`.

## Notes

Requests may carry multiple Topics, such as `state.tired` + `beverage.coffee` + `time.night`.

