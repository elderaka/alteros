---
title: Dialogue Catalogue
tags: [alteros, catalogue, dialogue]
snapshot: 2026-10-15
---
# Dialogue Catalogue

Related: [[Home]], [[Tracking/Semantic Index]], [[Tracking/Feature Register]], [[Tracking/Maintenance]], [[Tracking/State and Effects]].

## Authored route snapshot

The historical E greeting hierarchy and selection rules are in
[[Verification/2026-10-20 E Interaction Menu and Activity Debug]]. Root action
shortcuts from the earlier menu pass are superseded: Follow/Stop is in Favor,
Together opens input; three topic slots prefer eligible LLM offers then sample
eligible authored options. `greetings.json` owns branch dialogue, not semantic anchors.

Local activity-menu configuration is separate from semantic routes:
`Assets/StreamingAssets/Dialogue/topic/activity_options.json` supplies Come here,
Follow me and Drink water labels/capability targets. Host eligibility controls
visibility and selection; no LLM call or semantic anchor needed. See
[[Verification/2026-10-20 Activity Greeting Choices]]. Generic route snapshots
below are unchanged by these menu entries.

This is the 2026-10-15 legacy snapshot: 57 routes and 161 definitions extracted
from `Assets/StreamingAssets/Dialogue/Generic/`, with paths/hashes, gates, priority
and node/choice/example counts. It is not a fresh SQL catalogue report. Earlier
family migration under `Tools/TopicRuntime/StagedCatalogue/` is provenance; see
[[Topic Catalogue Migration]] and its `migration-map.json`.

Runtime readers now consume `RuntimeContentStore`'s pinned SQL release, not the
legacy source directory. The installed 439-source release passed read-only integrity
and trusted-release inspection and matches authoring revision 63; normal startup and
player acceptance remain open. SQL is also the editable authoring/draft authority;
Workbench typed CRUD commands and the Editor build entry use the authoring store.
The approved human editing UI is the Flow-first graph with selected-step inspector,
sharing the typed commands used by agents. W2-W5 of the existing Workbench plan
remain open; CLI CRUD alone is not the finished editor. The current import and review
export are separate from this historical 2026-10-15 route snapshot. JSON remains
retained import/export/provenance, not a competing runtime or authoring writer. See
[[Content Data Integration Handoff]] and [[SQLite Authoring Cutover Implementation Plan]].
The table/route notes remain unchanged dated snapshots. No refreshed content
hashes/counts or automated snapshot refresh are claimed by this review.

| Route | Variants | Declared hour windows (start/end) |
| --- | --- | --- |
| [[Tracking/Routes/affection_confession\|affection_confession]] | 1 | -1 / -1 |
| [[Tracking/Routes/affection_gesture\|affection_gesture]] | 3 | -1 / -1 |
| [[Tracking/Routes/apology\|apology]] | 3 | -1 / -1 |
| [[Tracking/Routes/ask_activity\|ask_activity]] | 5 | -1 / -1 |
| [[Tracking/Routes/ask_availability\|ask_availability]] | 2 | -1 / -1 |
| [[Tracking/Routes/ask_memory\|ask_memory]] | 3 | -1 / -1 |
| [[Tracking/Routes/ask_missed_me\|ask_missed_me]] | 3 | -1 / -1 |
| [[Tracking/Routes/ask_name_or_identity\|ask_name_or_identity]] | 2 | -1 / -1 |
| [[Tracking/Routes/ask_opinion\|ask_opinion]] | 2 | -1 / -1 |
| [[Tracking/Routes/ask_preference\|ask_preference]] | 3 | -1 / -1 |
| [[Tracking/Routes/ask_status\|ask_status]] | 5 | -1 / -1 |
| [[Tracking/Routes/ask_task_help_short\|ask_task_help_short]] | 2 | -1 / -1 |
| [[Tracking/Routes/back_to_work\|back_to_work]] | 2 | -1 / -1 |
| [[Tracking/Routes/compliment\|compliment]] | 3 | -1 / -1 |
| [[Tracking/Routes/dont_understand\|dont_understand]] | 2 | -1 / -1 |
| [[Tracking/Routes/empty_input\|empty_input]] | 3 | -1 / -1 |
| [[Tracking/Routes/farewell\|farewell]] | 2 | -1 / -1 |
| [[Tracking/Routes/finish_focus\|finish_focus]] | 2 | -1 / -1 |
| [[Tracking/Routes/food_or_tea_ritual\|food_or_tea_ritual]] | 2 | -1 / -1 |
| [[Tracking/Routes/goodnight\|goodnight]] | 4 | 20 / 6 |
| [[Tracking/Routes/gratitude\|gratitude]] | 3 | -1 / -1 |
| [[Tracking/Routes/greeting_afternoon\|greeting_afternoon]] | 3 | 12 / 17 |
| [[Tracking/Routes/greeting_evening\|greeting_evening]] | 4 | 17 / 22 |
| [[Tracking/Routes/greeting_general\|greeting_general]] | 3 | -1 / -1 |
| [[Tracking/Routes/greeting_latenight\|greeting_latenight]] | 3 | 22 / 5 |
| [[Tracking/Routes/greeting_morning\|greeting_morning]] | 4 | 5 / 12 |
| [[Tracking/Routes/hostility_major\|hostility_major]] | 2 | -1 / -1 |
| [[Tracking/Routes/hostility_medium\|hostility_medium]] | 2 | -1 / -1 |
| [[Tracking/Routes/inappropriate_boundary\|inappropriate_boundary]] | 3 | -1 / -1 |
| [[Tracking/Routes/insult_boundary_light\|insult_boundary_light]] | 2 | -1 / -1 |
| [[Tracking/Routes/keyboard_mash\|keyboard_mash]] | 1 | -1 / -1 |
| [[Tracking/Routes/missed_you\|missed_you]] | 4 | -1 / -1 |
| [[Tracking/Routes/no_response_needed\|no_response_needed]] | 4 | -1 / -1 |
| [[Tracking/Routes/player_bored\|player_bored]] | 4 | -1 / -1 |
| [[Tracking/Routes/player_happy_short\|player_happy_short]] | 4 | -1 / -1 |
| [[Tracking/Routes/player_lonely_short\|player_lonely_short]] | 3 | -1 / -1 |
| [[Tracking/Routes/player_needs_motivation\|player_needs_motivation]] | 3 | -1 / -1 |
| [[Tracking/Routes/player_overworking\|player_overworking]] | 3 | -1 / -1 |
| [[Tracking/Routes/player_proud\|player_proud]] | 3 | -1 / -1 |
| [[Tracking/Routes/player_sad_short\|player_sad_short]] | 3 | -1 / -1 |
| [[Tracking/Routes/player_self_doubt_short\|player_self_doubt_short]] | 3 | -1 / -1 |
| [[Tracking/Routes/player_stressed\|player_stressed]] | 4 | -1 / -1 |
| [[Tracking/Routes/player_tired\|player_tired]] | 4 | -1 / -1 |
| [[Tracking/Routes/procrastination_short\|procrastination_short]] | 3 | -1 / -1 |
| [[Tracking/Routes/profanity_minor\|profanity_minor]] | 2 | -1 / -1 |
| [[Tracking/Routes/returning\|returning]] | 3 | -1 / -1 |
| [[Tracking/Routes/room_or_desk_comment\|room_or_desk_comment]] | 2 | -1 / -1 |
| [[Tracking/Routes/sit_with_me\|sit_with_me]] | 4 | -1 / -1 |
| [[Tracking/Routes/sleep_nudge\|sleep_nudge]] | 2 | 21 / 6; 22 / 6 |
| [[Tracking/Routes/start_focus\|start_focus]] | 3 | -1 / -1 |
| [[Tracking/Routes/take_break\|take_break]] | 2 | -1 / -1 |
| [[Tracking/Routes/tease_playful\|tease_playful]] | 2 | -1 / -1 |
| [[Tracking/Routes/too_many_compliments\|too_many_compliments]] | 2 | -1 / -1 |
| [[Tracking/Routes/too_many_status_checks\|too_many_status_checks]] | 2 | -1 / -1 |
| [[Tracking/Routes/unclear_short\|unclear_short]] | 2 | -1 / -1 |
| [[Tracking/Routes/weather_smalltalk\|weather_smalltalk]] | 3 | -1 / -1 |
| [[Tracking/Routes/you_there\|you_there]] | 3 | -1 / -1 |

## Generated content

Current source seam: `Assets/Scripts/Interaction/GeneratedDialogueArchive.cs`; manifest path default `Assets/StreamingAssets/Dialogue/Generated/generated_dialogue_manifest.json`. Archive registration metadata exists, not an active validated background publication pipeline. Do not treat an archive `Ready` flag as proof of compatible indexing, speech, or supported gameplay.

Approved target: generate candidate → validate schema/semantic references and supporting facts → prepare required indexing/speech → publish user-only ready content. Separate content validation, index readiness, speech readiness, and publication state; exact state machine/schema remains implementation work. Reject or invalidate candidates when their supporting facts no longer hold.

Generated player-specific content stays outside the shared documentation vault. Shared notes may reference redacted test fixture IDs only. See [[Architecture]] for retrieved-catalogue manifests and low-activity indexing.

## Review queue

- Migrate flat route examples into the approved canonical/local-anchor contract without losing IDs or content.
- Review per-branch mappings, semantic ambiguity, and specificity instead of guessing from names.
- Verify declared gates are actually enforced; fix the playback-success gap before claiming readiness.
- Validate selected variants in Play Mode, including speech/fallback and control restoration.
- Implement supported generated publication/overlay before exposing generated choices.

Refresh instructions: [[Tracking/Maintenance#Route snapshot refresh]].
