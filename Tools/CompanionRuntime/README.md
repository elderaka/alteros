# AlterOS Companion Runtime

This is the private Python child process for bounded conversation work. Unity
owns gameplay state, eligibility, presentation and durable history; this helper
receives a bounded v2 turn over inherited stdin/stdout and never listens on a
network port.

## Local checks

From this directory, run `uv sync --locked --group dev` and then
`uv run --locked --group dev pytest tests -q`. The tests are synthetic and do
not call OpenRouter.

The child protocol is UTF-8 newline-delimited JSON. Standard output is protocol
only; malformed/oversized lines terminate the helper. Standard error is not a
diagnostic channel and must not contain prompts, credentials or provider data.
The Unity parent drains and discards it. Bootstrap is the only message carrying
the API key. Keys do not belong in arguments, turn jobs, graph state, saved
configuration or transcripts.

Version 2 error envelopes may include an optional allowlisted `diagnosticCode`
for safe Console triage. Provider-stage categories distinguish response JSON,
provider envelope/termination, judgment schema, and usage metadata; the graph
uses `judgment_validation` for host policy failures. Do not add provider text,
validation messages, prompts or response bodies to this field.

## Windows onedir package

Current source conversation policy (2026-10-19): the pinned DeepSeek model uses
OpenRouter's `digitalocean` provider only, with provider fallbacks disabled and
`reasoning.enabled=false`. Both ordinary and memory-aware turns share this
policy. The provider is asked for JSON mode; Pydantic strict models and the
application judgment validator enforce the response structure and semantics.
Generated v2 judgments use `reply.beats`; authored Topic JSON and the internal
canonical VN runner retain their `nodes` arrays. The host performs that mapping
only after strict beat validation. Suggested reply labels/prompts use the current
English display language; the host omits non-Latin suggestions without dropping
an otherwise valid generated reply.
The existing token limit and one-request/no-retry behavior remain unchanged.
An unavailable DigitalOcean route fails through the existing provider-error
handling, rather than silently switching providers. The opt-in smoke script
preserves this policy and adds its own price/output caps.
No Fast/Think preference is exposed yet. Rebuild the helper before expecting this
source policy in Unity: existing packaged executables retain their old settings.

On Windows with Python 3.13 and `uv` installed, run
`powershell -NoProfile -ExecutionPolicy Bypass -File .\build_windows.ps1`.
This creates `Build/CompanionRuntime/AlterOSCompanion/AlterOSCompanion.exe` and
its supporting files. It refuses to overwrite an existing output/work folder.
The Unity Editor's opt-in development helper is fixed to
`Build/CompanionRuntimeV4/AlterOSCompanion/AlterOSCompanion.exe`; it does not
fall back to older acceptance builds. Build it side-by-side with
`powershell -NoProfile -ExecutionPolicy Bypass -File .\build_windows.ps1 -OutputFolder Build/CompanionRuntimeV4 -WorkFolder Build/CompanionRuntimeV4Work`.
The script refuses to overwrite either folder. For player builds, continue to
package/copy the helper to the player's `CompanionRuntime/AlterOSCompanion`
directory.

The executable must be launched with redirected standard input/output and a
hidden window. Do not use `--windowed` or `--onefile`: the child must keep its
stdio protocol available and the onedir package is the first diagnosable
distribution shape.

The helper implements one non-streaming, structured OpenRouter request per
explicit turn using the pinned model configured in `provider.py`. It does not
start provider work at process startup, retry, repair, invoke tools, or save a
conversation. Unity's online toggle remains opt-in/default-off; the Editor dev
helper path is fixed to the v4 package above. No live request has been made.

`src/alteros_companion/persona.json` is a short, distributable distillation of
the external SOUL/IDENTITY guidance. It deliberately excludes their superseded
mode-switch text. Missing/invalid grounding is a setup error; there is no
generic-persona fallback. Treat this asset as a draft that needs human review
before online generation is enabled. No player-specific USER or memory file is
bundled.

## Scope and privacy

Transcript files belong to Unity under its private persistent-data directory,
not this process. The helper does not checkpoint conversation or credentials.
No LangSmith tracing is enabled. Private player profile/memory files are not
packaged; reviewed persona grounding is a separate prompt-stage asset.
