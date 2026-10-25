using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Scenarios;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Runs a detached typed Scenario through the compiled Flow plan and production ScenarioSession.</summary>
    internal static class WorkbenchScenarioPreview
    {
        private const int MaximumEvents = 256;

        public static WorkbenchResult Run(WorkbenchPreviewSnapshot snapshot, IDictionary<string, object> payload,
            TrustedFixtureContext fixture)
        {
            FlowRecord flow = (snapshot.Package.flows ?? new List<FlowRecord>()).FirstOrDefault(item =>
                item != null && item.id == snapshot.RootSourceId && item.kind == "scenario");
            if (flow == null)
                return WorkbenchResult.Failure("invalid", "preview_scenario_projection_missing", "/document/scenarioId",
                    "The compiled package does not contain the selected Scenario Flow.");
            ScenarioRuntimeSnapshot runtime = fixture.CreateRuntimeSnapshot(flow.id);
            var reference = new ContentRef(snapshot.Package.storeId, flow.id, flow.revision, flow.semanticHash);
            ContentResult<ContentExecutionPlan> prepared = ContentFlowAdapter.Prepare(snapshot.ResolvedRelease,
                reference, null, runtime, JsonWire.String(payload, "displayLanguage", "en"),
                JsonWire.String(payload, "speechLanguage", "ja"));
            if (!prepared.IsReady)
            {
                ContentDiagnostic issue = prepared.diagnostics.FirstOrDefault();
                return WorkbenchResult.Failure("invalid", issue == null ? "preview_scenario_prepare_failed" : issue.code,
                    issue == null ? "/document" : issue.path,
                    issue == null ? "The exact compiled Scenario could not be prepared." : issue.message);
            }

            var session = new ScenarioSession();
            IReadOnlyList<ScenarioEffect> startEffects = session.Start(prepared.value, fixture.RunId, runtime);
            var hostEffects = new WorkbenchSyntheticHostEffectPort(snapshot.Package, fixture);
            IReadOnlyList<ScenarioEffect> resolvedStartEffects = WorkbenchPreviewFlowEffectDriver.Resolve(session, runtime,
                fixture.RunId, snapshot.Package, snapshot.Release, fixture, startEffects, hostEffects,
                out List<object> startActivityRuns);
            var eventResults = new List<object>();
            if (payload.TryGetValue("events", out object rawEvents))
            {
                IEnumerable entries = JsonWire.AsArray(rawEvents);
                if (entries == null || rawEvents is string)
                    return WorkbenchResult.Failure("invalid", "preview_events_invalid", "/events", "events must be a JSON array.");
                var buffered = new List<object>(); foreach (object item in entries) buffered.Add(item);
                if (buffered.Count > MaximumEvents)
                    return WorkbenchResult.Failure("invalid", "preview_events_too_many", "/events", "Preview supports at most 256 events.");
                foreach (object raw in buffered)
                {
                    IDictionary<string, object> input = JsonWire.AsObject(raw);
                    if (input == null) return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", "Each event must be an object.");
                    WorkbenchResult failure;
                    object result = ApplyEvent(input, session, runtime, fixture.RunId,
                        snapshot.Package, snapshot.Release, fixture, hostEffects, out failure);
                    if (failure != null) return failure;
                    eventResults.Add(result);
                }
            }

            ScenarioInstanceView running = session.Running;
            IDictionary<string, object> last = eventResults.Count == 0 ? null : JsonWire.AsObject(eventResults[eventResults.Count - 1]);
            string startOutcome = resolvedStartEffects.Where(effect => effect.type == "end_scenario")
                .Select(effect => effect.outcome).LastOrDefault() ?? "";
            string flowStatus = running != null ? running.status : JsonWire.String(last, "flowStatus",
                string.IsNullOrEmpty(startOutcome) ? "idle" : "ended");
            string outcome = running != null ? running.outcome : JsonWire.String(last, "flowOutcome", startOutcome);
            ScenarioStep step = running == null || running.definition == null ? null : running.definition.steps.FirstOrDefault(item =>
                item != null && item.id == running.currentStepId);
            ScenarioBeat beat = step == null || step.beats == null ? null : step.beats.FirstOrDefault(item => item != null);
            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kindId", "scenario" }, { "fixtureId", fixture.FixtureId }, { "fixtureOnly", true },
                { "rootIsSynthetic", snapshot.RootIsSynthetic }, { "sourceSnapshot", snapshot.SourceSnapshot },
                { "ephemeralReleaseId", snapshot.Release.releaseId }, { "flowId", flow.id },
                { "flowStatus", flowStatus }, { "flowOutcome", outcome },
                { "currentDialogue", beat == null ? null : new Dictionary<string, object>
                    { { "stepId", step.id }, { "beatId", beat.id }, { "speaker", beat.speaker },
                      { "text", beat.text }, { "displayText", beat.displayText }, { "language", beat.language },
                      { "expression", beat.expression } } },
                { "startEffects", EffectViews(startEffects) }, { "resolvedStartEffects", EffectViews(resolvedStartEffects) },
                { "startActivityRuns", startActivityRuns }, { "events", eventResults },
                { "syntheticHostEffectChanges", hostEffects.Changes },
                { "fixtureFurniture", fixture.PreviewFurniture.OrderBy(item => item.instanceId, StringComparer.Ordinal)
                    .Select(item => (object)new Dictionary<string, object> { { "instanceId", item.instanceId },
                        { "definitionId", item.definitionId }, { "useId", item.useId },
                        { "powerState", item.powerKnown ? item.powerState : "unknown" },
                        { "instanceRevision", item.instanceRevision }, { "reservationOwnerId", item.reservationOwnerId } }).ToList() },
                { "fixtureItemQuantities", fixture.PreviewItemQuantities.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => (object)new Dictionary<string, object> { { "itemId", item.Key }, { "quantity", item.Value } }).ToList() },
                { "runtimeExecution", "shared ScenarioSession; action host effects are not auto-granted" },
                { "providerCalls", 0 }, { "ttsCalls", 0 }
            });
        }

        private static object ApplyEvent(IDictionary<string, object> input, ScenarioSession session,
            ScenarioRuntimeSnapshot runtime, string runId, ContentPackage package, ContentRelease release,
            TrustedFixtureContext fixture, WorkbenchSyntheticHostEffectPort hostEffects,
            out WorkbenchResult failure)
        {
            failure = null;
            string action = JsonWire.String(input, "action");
            ScenarioInstanceView before = session.Running;
            if (before == null || before.status != "running")
            {
                failure = WorkbenchResult.Failure("invalid", "preview_scenario_not_running", "/events", "There is no running Scenario to advance.");
                return null;
            }
            try
            {
                if (action == "cancel")
                {
                    IReadOnlyList<ScenarioEffect> cancelled = session.Advance(new ScenarioEvent { kind = "wrap_up", runId = runId }, runtime);
                    return EventResult(action, before, cancelled, session.Running, "", null, new List<object>());
                }
                if (action != "continue") throw new InvalidOperationException("Only continue and cancel events are supported here.");
                ScenarioStep step = before.definition.steps.FirstOrDefault(item => item != null && item.id == before.currentStepId);
                if (step == null || step.kind != "dialogue")
                    throw new InvalidOperationException("Continue acknowledges dialogue only; this Flow is waiting on a non-dialogue host operation.");
                ScenarioBeat beat = step.beats == null ? null : step.beats.FirstOrDefault(item => item != null);
                IReadOnlyList<ScenarioEffect> effects = session.Advance(new ScenarioEvent
                    { kind = "dialogue_complete", runId = runId, stepId = before.currentStepId }, runtime);
                effects = WorkbenchPreviewFlowEffectDriver.Resolve(session, runtime, runId, package, release, fixture,
                    effects, hostEffects, out List<object> activityRuns);
                ScenarioInstanceView after = session.Running;
                string outcome = after == null
                    ? effects.Where(effect => effect.type == "end_scenario").Select(effect => effect.outcome).LastOrDefault() ?? "failed"
                    : after.outcome;
                return EventResult(action, before, effects, after, outcome, beat, activityRuns);
            }
            catch (Exception exception)
            {
                failure = WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", exception.Message);
                return null;
            }
        }

        private static object EventResult(string action, ScenarioInstanceView before, IReadOnlyList<ScenarioEffect> effects,
            ScenarioInstanceView after, string outcome, ScenarioBeat beat = null, IList<object> activityRuns = null)
        {
            return new Dictionary<string, object>
            {
                { "action", action }, { "completedStepId", action == "continue" ? before.currentStepId : "" },
                { "acknowledgedDialogue", beat == null ? null : new Dictionary<string, object>
                    { { "beatId", beat.id }, { "text", beat.text }, { "displayText", beat.displayText }, { "language", beat.language } } },
                { "flowStatus", after == null ? "ended" : after.status }, { "flowOutcome", outcome },
                { "effects", EffectViews(effects) }, { "activityRuns", activityRuns ?? new List<object>() }
            };
        }

        private static object EffectViews(IEnumerable<ScenarioEffect> effects)
        {
            return (effects ?? new ScenarioEffect[0]).Select(effect => (object)new Dictionary<string, object>
                { { "type", effect.type }, { "stepId", effect.stepId }, { "outcome", SafeOutcome(effect) } }).ToList();
        }

        private static string SafeOutcome(ScenarioEffect effect)
        { return effect.type == "release_activity" ? "released" : effect.outcome; }
    }
}
