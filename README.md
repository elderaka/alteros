# AlterOS

AlterOS is an interactive companion game where the player can talk, study, and play with Alterra through natural-language interaction.

## Core

AlterOS has three initial capabilities:

1. **User-aware dialogue routing** — the current client uses a local GTE embedding model to route free text to ready authored dialogue; personalized routing is planned.
2. **Background adaptation** — the target is a BYOK LLM workflow that learns useful user context and prepares personalized dialogue branches; that worker is not wired yet.
3. **Text-to-speech** — generic dialogue can use prebaked/cache audio, while the current provider seam can generate speech through OpenRouter.

## Product boundary

Unity owns the companion experience: authored dialogue, character state, presentation, animation, audio playback, and player interaction.

The synchronous Topic path chooses among already-ready dialogue and never waits for an LLM. Explicit Agent mode is planned as a bounded BYOK conversation/tool loop with in-game actions first.

The intended asynchronous path records interaction, updates the user model, generates and validates personalized branches, prepares their speech, and makes them available to future conversations. The current client only has session persistence and generated-dialogue archive scaffolding.

Generic dialogue remains available without an API key. BYOK currently enables OpenRouter TTS; background adaptation and future explicit agent-powered activities remain planned.

The architecture baseline approved on 2026-10-14 has three model roles:
embedding for routing, sentiment evidence for adaptive behavior, and the LLM
for Agent replies and background generation. TTS is a separate speech service.
Sentiment implementation is undecided; stats and currency remain game-owned,
and focus/quiet settings take priority over autonomous invitations.

See [Documentation/Architecture.md](Documentation/Architecture.md) for the target system boundary.

For a current implementation snapshot, read
[Documentation/UnityProjectContext.md](Documentation/UnityProjectContext.md).
The [Session Zero inventory](Documentation/SessionZero.md) lists implemented
features, wiring gaps, executed checks, and the acceptance path required before
the first study/adaptation milestone.
The semantic catalogue is maintained under
[Documentation/Semantics/](Documentation/Semantics/README.md).

Open `Documentation` as an Obsidian vault and start with
[the design home](Documentation/Home.md) for linked feature, dialogue, state,
tool, and decision registers. Route notes are dated JSON snapshots, not runtime
configuration or an automatically refreshed database.

The standalone [Semantic Lab](Tools/SemanticLab/README.md) compares every dialogue route and measures embedding latency without launching Unity.

## Status

Last reviewed: 2026-10-14

- Unity: 6000.4.4f1
- Dialogue core: shared data, strict validation, and deterministic branch playback implemented as plain C#
- Generic content: 57 semantic routes and 161 ready dialogue variants migrated to the new schema
- Embedding: GTE multilingual base integrated through Sentis; automatic startup warm-up, route ranking, and authored free-text topic playback implemented
- Rendering and player input surface: local AlterOS runtime with authored interaction and no external control-plane dependency
- TTS: prebaked/cache playback retained; OpenRouter Qwen-Audio-3.0-TTS Plus is wired through a provider-facing client
- TTS credentials: `OPENROUTER_API_KEY` is the development override; Windows user keys are encrypted with DPAPI under Unity's persistent data path
- Generated-dialogue archive: lifecycle metadata exists; generated topics are not surfaced until playback is implemented, and no LLM generation, baking worker, or personalized routing overlay is wired yet

The current playable route is `SplashScene -> ModelTest`. The project is still a
transitional Unity client: focused provider-neutral code lives under
`Assets/AlterOS/`, while scene-wired dialogue and presentation bridges remain
under `Assets/Scripts/` and `Assets/Altera/`.
