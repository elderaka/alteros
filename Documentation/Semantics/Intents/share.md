# Intent: `share`

## Meaning

The player is volunteering a feeling, condition, observation, or current situation without primarily asking a question.

## Includes

- Reporting tiredness, stress, sadness, loneliness, boredom, happiness, pride, overwork, self-doubt, or procrastination.
- Comments about the room, desk, weather, or another immediate observation.

## Excludes

- A direct request for help or an action (`request`).
- A relationship-oriented statement aimed at Alterra (`relationship`).

## Current route coverage

`player_bored`, `player_happy_short`, `player_lonely_short`, `player_overworking`, `player_proud`, `player_sad_short`, `player_self_doubt_short`, `player_stressed`, `player_tired`, `procrastination_short`, `room_or_desk_comment`, `weather_smalltalk`.

## Notes

The player’s condition is a Topic, not a new Intent. A single line can produce secondary evidence for `request` when it also asks for an action.

