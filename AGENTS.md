# AlterOS Development Guidelines

## Scope

- This is the clean AlterOS Unity project.
- Work in `E:\Pleiades\alteros`. `E:\Pleiades\pleiades.os` is archive-only; verify the project root before making changes or selecting a Unity MCP instance.
- Alterra is the companion. Use `Alterra` consistently for her name.
- The approved product direction includes user-aware Topic routing, bounded in-game Agent conversation, BYOK background adaptation, sentiment evidence, and replaceable TTS; distinguish this target from implemented features.
- Add only assets and behavior required by the current vertical slice.

## Orientation and documentation

- Read root `TODO.md` for the shared cross-session roadmap, active work and handoff boundaries. Keep detailed designs/evidence in the linked documents, not duplicated task lists.
- Start at `Documentation/Home.md`, the Obsidian-ready documentation entry point. Read the linked material relevant to the task rather than loading the entire vault.
- `Documentation/Architecture.md` owns approved target boundaries; `Documentation/UnityProjectContext.md` describes the implementation snapshot. Neither substitutes for checking current source and wiring.
- `Documentation/Tracking/Feature Register.md` tracks feature coverage; `Documentation/SessionZero.md` owns readiness findings and acceptance evidence.
- `Documentation/Semantics/` owns individual Intent, Tone, and Topic review notes. `Documentation/Tracking/Routes/` contains dated authored-JSON snapshots, not runtime configuration or a second editable content database.
- Keep changing feature status, detailed design, and session evidence in those documents, not in this instruction file. Reuse existing notes rather than introducing a parallel memory system.
- When a task changes features, triggers/conditions, anchors, state effects, tool contracts, or content formats, update the affected register/design notes and authored route snapshots as applicable. Follow `Documentation/Tracking/Maintenance.md`; do not claim snapshots refresh automatically.
- Shared catalogues list authored content only. Do not enumerate or copy in-game AI-generated personal dialogue, user-specific catalogues/overlays, memories, or conversations into this vault. Document their shared format, validation/publication rules, storage ownership, and limitations instead; use synthetic fixtures when examples are needed.
- Distinguish current supported schema from proposed fields and archive scaffolding. A generated record's status alone does not prove validation, indexing, speech readiness, publication, or playback support.

## Session coordination

- Follow `Documentation/Tracking/Multi-Agent Coordination.md` for persistent domain sessions, stable thread IDs and scoped human-authorized communication. Alterra Behavior may consult DBMS and Furniture and Environment directly for the approved slice's technical questions, and those owners may reply; escalate product decisions or new authority to main/the human.
- Agree on one writer before changing shared files or using the Unity Editor. A question, an idle status or an unanswered message is not an ownership transfer or implementation START. Do not create replacement sessions or duplicate systems to avoid coordination.
- While an approved goal remains unfinished, main maintains one continuation heartbeat for one minute after the next relevant usage reset, using fresh usage-limit data. Reuse/reschedule the same heartbeat; do not duplicate it per domain session. Delete it when the goal is achieved, respect explicit human pause/cancel, and never redeem a reset credit without separate explicit confirmation. See the coordination note for the recovery procedure.

## Ownership

- Generic and personalized dialogue use one shared content schema and dialogue runner.
- Unified conversation is Topic-centred and authored-first: retrieval supplies candidates, optional online LLM judgment interprets context, and the host enforces eligibility. Local prepared content never requires an LLM. M2/M2.1 and v4 Scenario source integration exist; do not rebuild them from older local-only audit notes. See `Documentation/Tracking/Unified Conversation Design.md` and the current handoff for SQL release readiness, the known v4 provider-failure fallback gap and pending runtime acceptance.
- Every interaction may be recorded, but background work may batch or discard trivial events instead of calling an LLM per turn.
- The dialogue router selects ready content; it does not own UI, TTS, background generation, or game mechanics.
- Live Agent conversation and background adaptation share a provider-neutral BYOK boundary. Unity must not depend on LangChain, LangGraph, or any provider's internal protocol.
- Stats influence dialogue and Agent expression; only validated game events apply stat, relationship, or currency effects. Never expose arbitrary stat deltas or currency minting to the LLM.
- Player focus/quiet settings override initiative. Optional low needs must not force an interlude or block an active Agent task; sentiment evidence cannot grant permissions or override explicit player settings.
- TTS receives finalized text and returns playable speech through `TTSSystem`. Keep provider details out of dialogue/content systems even though the current adapter is OpenRouter.
- User state supplies explicit eligibility and reranking context. Do not embed entire chat histories or save files by default.
- Generated dialogue must be validated before it becomes routable.
- Unity prefabs and scenes own presentation composition and serialized references.
- Static HUD and UI structure, layout, sizes, and styling must be authored on Canvas objects or UI prefabs through the Unity Editor, not constructed or overridden in runtime code. Code may bind content/state, instantiate authored templates for dynamic lists, and animate or position genuinely dynamic UI (for example moving world-space choices). Keep its static appearance/layout in the authored template.
- SQLite owns shared dialogue content/routing metadata and drafts through the revision/CAS authoring repository; Workbench commands and Editor builds use that store. JSON is explicit import/export only. Follow `Documentation/Tracking/SQLite Authoring Cutover Implementation Plan.md`, preserve original files, and do not dual-edit or silently auto-import. Runtime consumes a validated pinned compiled release; use current handoffs for artifact identity/readiness, not stale absence claims or file existence alone. Focused runtime systems still own mechanics and permissions; notes never execute or authorize them.
- Keep explicit player interaction and non-modal ambience distinct. Approved input remapping is documented in `Documentation/Tracking/Furniture Inventory and Debug Design.md` (T interacts; E opens inventory when not holding furniture); do not assume older E bindings have already been migrated. The approved unified design removes the player-facing Topic/Agent switch; direct/phone are channels, not permission levels. Saving a key alone does not authorize automatic billable generation; online consent and tool permissions remain explicit.
- Current legacy Generic import/export layout is `Assets/StreamingAssets/Dialogue/Generic/<routeId>/`; every variant keeps that same `routing.routeId` and the shared schema. Do not delete these files or infer SQL authoring acceptance from the approved target.

## Shared Debug Mode

- F5 is reserved for one shared game-wide Debug Mode flag. Read `Documentation/Tracking/Debug Mode.md` before implementing debug controls, unlimited furniture, or diagnostic overlays.
- Reuse the single state owner (planned `AlterOS.Debugging.DebugModeState` under `Assets/AlterOS/Debugging/`; inspect current source first). Do not add per-system debug booleans, duplicate F5 listeners, or substitute Editor/development-build status for the runtime flag. This is an approved contract, not proof that the owner/input wiring exists.
- Debug Mode enables unlimited furniture supply and gates the existing above-Alterra state/action/Scenario overlay and diagnostic views. Normal inventory/placement remain usable with Debug Mode off. Preserve finite stock; do not reset it on toggles or bypass placement safety, reservations, capability checks, provider consent, or stat/currency authority.
- Keep detailed debug policy and implementation/verification status in the linked design note; static presentation remains Canvas/prefab-authored.

## Security

- Never commit API keys, tokens, model-provider credentials, or generated secret configuration.
- Do not store BYOK secrets in `PlayerPrefs` or authored assets.
- The current local provider uses `OPENROUTER_API_KEY` as a development override and Windows current-user DPAPI for saved keys. Do not broaden this to other platforms without choosing their secure storage explicitly.

## Unity workflow

- Use Unity 6000.4.4f1.
- The user authorizes force-closing a genuinely stalled AlterOS Editor. First verify the exact project/process, preserve available failure evidence, and check the last known unsaved-work state. A stale MCP connection or slow import alone is not proof of a hang. Stop only the verified stalled Editor; never blanket-kill Unity processes or delete project/lock data. Record the recovery and any possible loss of unsaved work.
- Make scene, prefab, and serialized-reference changes through the Unity Editor. Do not hand-edit Unity YAML or `.meta` files.
- Preserve GUIDs when intentionally importing existing Unity assets; copy the asset and its `.meta` together.
- Put new provider-neutral runtime code under `Assets/AlterOS/`. Existing scene-wired bridge code remains under `Assets/Scripts/` and `Assets/Altera/`; do not mass-migrate it during focused work. Put editor-only code in an `Editor` folder and tests in a dedicated test assembly.
- Keep deterministic dialogue rules in plain C#; add MonoBehaviours only for an actual Unity lifecycle or presentation responsibility.
- Prefer Unity and .NET facilities already present before adding packages.
- Do not add an embedding runtime, agent framework, or TTS SDK until its implementation task names the chosen option.

## Validation

- Human cadence override (2026-10-24): implement a cohesive approved goal before testing/verifying. Do not run per-change tests, repeated review/fix loops or validation-only detours. Perform one consolidated relevant validation pass at goal completion; preserve earlier evidence without rerunning accepted work. Safety checks before destructive operations and production publication validation remain required. Report unverified behavior honestly.
- Routing acceptance must exercise natural-language input against the installed pinned SQL release through production candidate selection and Scenario resolution. Schema/build checks, baked vector counts, explicit Topic-ID previews and synthetic similarity alone do not satisfy it. Use `Tools/TopicPlayground/topic-playground.ps1 -Query` (PowerShell 7); the older Generic-JSON/fixture Editor modes are not this gate. Keep Unity encoder/in-scene behavior evidence separate.
- Keep the project compiling after each implementation slice.
- Validate routing behavior with a small labeled phrase set before expanding the dialogue corpus.
- Report compile/import evidence separately from Play Mode, audio-quality, latency, and visual proof.
- Track design approval, implementation, scene wiring, and runtime verification separately. Date evidence and name checks not run or blocked; never turn a source-only finding into a gameplay pass.
- Use `SplashScene -> ModelTest` for normal startup acceptance; opening ModelTest directly does not prove the Splash transition.
- Documentation-only work does not require Play Mode, package changes, builds, or reimports. Run those only when relevant to the requested task.
- End a work handoff with what changed, what was verified (and how), remaining gaps, and the next relevant acceptance check. Keep it concise; do not mark a feature complete merely because code compiles.

## Reference

- `Documentation/Architecture.md` is the target architecture baseline.
- `Documentation/UnityProjectContext.md` is the current implementation snapshot; update it when startup, packages, assemblies, or major runtime ownership changes.
