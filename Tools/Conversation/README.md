# Unified Conversation M1 checks

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Conversation/Check-UnifiedConversation.ps1
```

The focused source runner compiles the production C# Topic/conversation code
and invokes the NUnit tests without launching Unity, making provider calls,
loading GTE, or writing production saves. `Fixtures/` provides synthetic
contracts and inputs; the fixture test parses each request/judgment with the
production contract validator and calls `UnifiedConversationPlanner.Plan`.
It fails if any named fixture is missing or unexecuted.

These checks prove deterministic source behavior only. They do not prove Unity
import/assembly compilation, Editor or Play Mode behavior, real GTE ranking,
model judgment quality, sidecar communication, phone presentation, audio, or
end-to-end latency. See `Documentation/Tracking/Unified Conversation Design.md`
for M1 scope and later milestones.
