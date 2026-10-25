# Intent: `fallback`

## Meaning

The input does not provide enough reliable language to select another Intent or Topic.

## Includes

- Empty input, keyboard mash, and short unclear fragments.

## Excludes

- A valid acknowledgement (`repair`).
- A short but recognizable topic or request.

## Current route coverage

`empty_input`, `keyboard_mash`, `unclear_short`.

## Notes

Fallback is an explicit safe result, not permission for the model to guess a topic.

