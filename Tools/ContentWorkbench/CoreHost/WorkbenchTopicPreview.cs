using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlterOS.Content;
using AlterOS.Scenarios;
using AlterOS.Topics;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Runs a directly selected Topic family through the production selector, Flow session and detached history.</summary>
    internal static class WorkbenchTopicPreview
    {
        private const int MaximumEvents = 256;

        public static WorkbenchResult Run(WorkbenchPreviewSnapshot snapshot, IDictionary<string, object> payload,
            TrustedFixtureContext fixture)
        {
            TopicDefinition definition;
            try { definition = (TopicDefinition)JsonWire.ConvertTo(snapshot.RootDocument, typeof(TopicDefinition)); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "preview_topic_projection_failed", "/document", exception.GetType().Name); }

            string instanceId = JsonWire.String(payload, "instanceId");
            string source = JsonWire.String(payload, "source", "authored");
            string replay = JsonWire.String(payload, "replayPolicy");
            double draw;
            DateTimeOffset now;
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 128)
                return WorkbenchResult.Failure("invalid", "preview_instance_invalid", "/instanceId", "Topic preview needs an explicit occurrence ID of at most 128 characters.");
            if (!JsonWire.TryDouble(payload, "draw", out draw) || double.IsNaN(draw) || double.IsInfinity(draw) || draw < 0 || draw >= 1)
                return WorkbenchResult.Failure("invalid", "preview_draw_invalid", "/draw", "draw must be finite and in [0,1).");
            if (!TryParseExplicitUtc(JsonWire.String(payload, "nowUtc"), out now))
                return WorkbenchResult.Failure("invalid", "preview_time_invalid", "/nowUtc", "nowUtc must include an explicit UTC offset.");

            TopicInstance instance;
            TopicContext topicContext = fixture.CreateTopicContext();
            TopicSelectionResult selection;
            TopicHistory history = new TopicHistory();
            try
            {
                instance = TopicInstance.Create(definition, instanceId, source, string.IsNullOrEmpty(replay) ? null : replay);
                selection = TopicScenarioSelector.Select(definition, instance, topicContext, history, now, draw);
            }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "preview_topic_selection_failed", "/document", exception.Message); }
            if (selection.SelectedScenario == null)
                return WorkbenchResult.Ok(SelectionResult(snapshot, fixture, selection, instance, history, null, null, new List<object>()));

            TopicScenario selected = selection.SelectedScenario;
            if (selected.scenarioRef == null)
                return WorkbenchResult.Failure("unavailable", "preview_topic_inline_entry_unsupported", "/document/scenarios",
                    "Typed Topic preview currently requires an exact reusable Scenario Flow reference; legacy inline traversal remains on its existing path.");
            ScenarioRuntimeSnapshot runtime = fixture.CreateRuntimeSnapshot(selected.scenarioRef.id);
            ContentResult<ContentExecutionPlan> prepared = ContentFlowAdapter.Prepare(snapshot.ResolvedRelease,
                selected.scenarioRef, null, runtime, JsonWire.String(payload, "displayLanguage", "en"),
                JsonWire.String(payload, "speechLanguage", "ja"));
            if (!prepared.IsReady)
            {
                ContentDiagnostic issue = prepared.diagnostics.FirstOrDefault();
                return WorkbenchResult.Failure("invalid", issue == null ? "preview_topic_flow_prepare_failed" : issue.code,
                    issue == null ? "/document/scenarios" : issue.path,
                    issue == null ? "The selected Flow could not be prepared from this exact release." : issue.message);
            }

            var session = new ScenarioSession();
            IReadOnlyList<ScenarioEffect> effects = session.Start(prepared.value, fixture.RunId, runtime);
            ScenarioInstanceView running = session.Running;
            if (running == null || running.status != "running")
                return WorkbenchResult.Failure("invalid", "preview_topic_flow_start_failed", "/document/scenarios",
                    effects.FirstOrDefault() == null ? "The selected Flow could not start." : effects[0].outcome);
            TopicPlaybackHandle handle;
            try { handle = history.BeginFlowPlayback(definition, instance, selected.scenarioId, fixture.RunId, running.currentInvocationId, now); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "preview_topic_history_start_failed", "/instanceId", exception.Message); }

            var eventResults = new List<object>();
            var hostEffects = new WorkbenchSyntheticHostEffectPort(snapshot.Package, fixture);
            IReadOnlyList<ScenarioEffect> resolvedStartEffects = WorkbenchPreviewFlowEffectDriver.Resolve(session, runtime,
                fixture.RunId, snapshot.Package, snapshot.Release, fixture, effects, hostEffects,
                out List<object> startActivityRuns);
            string startHistoryStatus = string.Empty;
            if (session.Running == null)
            {
                string startOutcome = resolvedStartEffects.Where(effect => effect.type == "end_scenario")
                    .Select(effect => effect.outcome).LastOrDefault() ?? "failed";
                startHistoryStatus = history.CompleteFlow(handle, fixture.RunId, handle.FlowInvocationId,
                    startOutcome, now).Status;
            }
            DateTimeOffset currentTime = now;
            if (payload.TryGetValue("events", out object rawEvents))
            {
                IEnumerable events = JsonWire.AsArray(rawEvents);
                if (events == null || rawEvents is string)
                    return WorkbenchResult.Failure("invalid", "preview_events_invalid", "/events", "events must be a JSON array.");
                var buffered = new List<object>(); foreach (object item in events) buffered.Add(item);
                if (buffered.Count > MaximumEvents)
                    return WorkbenchResult.Failure("invalid", "preview_events_too_many", "/events", "Preview supports at most 256 events.");
                foreach (object raw in buffered)
                {
                    IDictionary<string, object> input = JsonWire.AsObject(raw);
                    if (input == null) return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", "Each event must be an object.");
                    string action = JsonWire.String(input, "action");
                    if (action == "reselect" || action == "new_occurrence")
                    {
                        try
                        {
                            object historyResult = ApplyHistoryProbe(action, input, definition, instance, source, replay,
                                topicContext, history, session, ref currentTime);
                            eventResults.Add(historyResult);
                        }
                        catch (Exception exception)
                        { return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", exception.Message); }
                        continue;
                    }
                    WorkbenchResult failure;
                    object result = ApplyEvent(input, session, runtime, handle, history, fixture.RunId,
                        snapshot.Package, snapshot.Release, fixture, hostEffects, ref currentTime, out failure);
                    if (failure != null) return failure;
                    eventResults.Add(result);
                }
            }

            Dictionary<string, object> output = SelectionResult(snapshot, fixture, selection, instance, history, session, handle, eventResults);
            output.Add("resolvedStartEffects", resolvedStartEffects.Select(effect => (object)new Dictionary<string, object>
                { { "type", effect.type }, { "stepId", effect.stepId },
                  { "outcome", effect.type == "release_activity" ? "released" : effect.outcome } }).ToList());
            output.Add("startActivityRuns", startActivityRuns);
            output.Add("startHistoryStatus", startHistoryStatus);
            if (session.Running == null && string.IsNullOrEmpty(JsonWire.String(output, "flowOutcome")))
            {
                output["flowStatus"] = "ended";
                output["flowOutcome"] = resolvedStartEffects.Where(effect => effect.type == "end_scenario")
                    .Select(effect => effect.outcome).LastOrDefault() ?? "failed";
            }
            output.Add("syntheticHostEffectChanges", hostEffects.Changes);
            output.Add("fixtureFurniture", fixture.PreviewFurniture.OrderBy(item => item.instanceId, StringComparer.Ordinal)
                .Select(item => (object)new Dictionary<string, object> { { "instanceId", item.instanceId },
                    { "definitionId", item.definitionId }, { "useId", item.useId },
                    { "powerState", item.powerKnown ? item.powerState : "unknown" },
                    { "instanceRevision", item.instanceRevision }, { "reservationOwnerId", item.reservationOwnerId } }).ToList());
            output.Add("fixtureItemQuantities", fixture.PreviewItemQuantities.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => (object)new Dictionary<string, object> { { "itemId", item.Key }, { "quantity", item.Value } }).ToList());
            return WorkbenchResult.Ok(output);
        }

        private static Dictionary<string, object> SelectionResult(WorkbenchPreviewSnapshot snapshot,
            TrustedFixtureContext fixture, TopicSelectionResult selection, TopicInstance instance,
            TopicHistory history, ScenarioSession session, TopicPlaybackHandle handle, IList<object> events)
        {
            ScenarioInstanceView running = session == null ? null : session.Running;
            ScenarioStep step = running == null || running.definition == null ? null :
                running.definition.steps.FirstOrDefault(item => item != null && item.id == running.currentStepId);
            ScenarioBeat beat = step == null || step.beats == null ? null : step.beats.FirstOrDefault(item => item != null);
            IDictionary<string, object> lastEvent = events.Count == 0 ? null : JsonWire.AsObject(events[events.Count - 1]);
            string flowStatus = running != null ? running.status : JsonWire.String(lastEvent, "flowStatus", "not_started");
            string flowOutcome = running != null ? running.outcome : JsonWire.String(lastEvent, "flowOutcome");
            return new Dictionary<string, object>
            {
                { "kindId", "topic" }, { "fixtureId", fixture.FixtureId }, { "fixtureOnly", true },
                { "rootIsSynthetic", snapshot.RootIsSynthetic }, { "sourceSnapshot", snapshot.SourceSnapshot },
                { "ephemeralReleaseId", snapshot.Release.releaseId },
                { "routingMode", "direct synthetic family; not semantic routing" },
                { "selectedTopicId", instance.topicId },
                { "selectedScenarioId", selection.SelectedScenario == null ? "" : selection.SelectedScenario.scenarioId },
                { "selectionReason", selection.Reason }, { "winningPriority", selection.WinningPriority.HasValue ? (object)selection.WinningPriority.Value : null },
                { "draw", selection.Draw }, { "candidates", CandidateViews(selection) },
                { "flowStatus", flowStatus }, { "flowOutcome", flowOutcome },
                { "currentDialogue", beat == null ? null : new Dictionary<string, object>
                    { { "stepId", step.id }, { "beatId", beat.id }, { "speaker", beat.speaker },
                      { "text", beat.text }, { "displayText", beat.displayText }, { "language", beat.language },
                      { "expression", beat.expression }, { "voice", beat.voice }, { "audioKey", beat.audioKey } } },
                { "historyDirty", history.IsDirty }, { "historyPersistence", "detached-memory-only; no player save" },
                { "activeOccurrence", handle == null ? null : new Dictionary<string, object>
                    { { "scenarioId", handle.ScenarioId }, { "replayPolicy", handle.ReplayPolicy }, { "flowBacked", handle.IsFlowBacked } } },
                { "isConsumed", history.IsConsumed(instance.instanceId) },
                { "scenarioCompletionCount", selection.SelectedScenario == null ? 0 : history.GetCompletionCount(instance.instanceId, selection.SelectedScenario.scenarioId) },
                { "events", events }, { "providerCalls", 0 }, { "ttsCalls", 0 }
            };
        }

        private static object ApplyHistoryProbe(string action, IDictionary<string, object> input,
            TopicDefinition definition, TopicInstance instance, string source, string replay,
            TopicContext context, TopicHistory history, ScenarioSession session, ref DateTimeOffset currentTime)
        {
            if (session.Running != null && session.Running.status == "running")
                throw new InvalidOperationException("A replay probe is only valid after the current Flow occurrence has ended.");
            if (!TryParseExplicitUtc(JsonWire.String(input, "atUtc"), out DateTimeOffset atUtc) || atUtc < currentTime)
                throw new InvalidOperationException("Event atUtc must be explicit and monotonic.");
            currentTime = atUtc;
            TopicInstance probe = instance;
            if (action == "new_occurrence")
            {
                string newId = JsonWire.String(input, "instanceId");
                if (string.IsNullOrWhiteSpace(newId) || newId.Length > 128 || newId == instance.instanceId)
                    throw new InvalidOperationException("new_occurrence needs a distinct explicit occurrence ID.");
                probe = TopicInstance.Create(definition, newId, source, string.IsNullOrEmpty(replay) ? null : replay);
            }
            TopicSelectionResult selection = TopicScenarioSelector.Select(definition, probe, context, history,
                currentTime, 0d);
            return new Dictionary<string, object>
            {
                { "action", action }, { "instanceId", probe.instanceId },
                { "selectedScenarioId", selection.SelectedScenario == null ? "" : selection.SelectedScenario.scenarioId },
                { "selectionReason", selection.Reason }, { "candidates", CandidateViews(selection) },
                { "isConsumed", history.IsConsumed(probe.instanceId) }
            };
        }

        private static object CandidateViews(TopicSelectionResult selection)
        {
            return selection.Candidates.Select(item => (object)new Dictionary<string, object>
            {
                { "scenarioId", item.ScenarioId }, { "priority", item.Priority },
                { "effectiveWeight", item.EffectiveWeight }, { "reason", item.Reason },
                { "gate", item.Gate == null ? "not_evaluated" : item.Gate.Truth.ToString() },
                { "modifiers", (item.Modifiers ?? new TopicWeightTrace[0]).Select(modifier => (object)new Dictionary<string, object>
                    { { "authoredAmount", modifier.AuthoredAmount }, { "appliedAmount", modifier.AppliedAmount },
                      { "truth", modifier.Condition == null ? "unknown" : modifier.Condition.Truth.ToString() } }).ToList() }
            }).ToList();
        }

        private static object ApplyEvent(IDictionary<string, object> input, ScenarioSession session,
            ScenarioRuntimeSnapshot runtime, TopicPlaybackHandle handle, TopicHistory history, string runId,
            ContentPackage package, ContentRelease release, TrustedFixtureContext fixture,
            WorkbenchSyntheticHostEffectPort hostEffects, ref DateTimeOffset currentTime, out WorkbenchResult failure)
        {
            failure = null;
            string action = JsonWire.String(input, "action");
            try
            {
                ScenarioInstanceView before = session.Running;
                if (before == null || before.status != "running") throw new InvalidOperationException("The selected Flow occurrence is no longer running.");
                if (!TryParseExplicitUtc(JsonWire.String(input, "atUtc"), out DateTimeOffset atUtc) || atUtc < currentTime)
                    throw new InvalidOperationException("Event atUtc must be explicit and monotonic.");
                currentTime = atUtc;
                if (action == "cancel")
                {
                    session.Advance(new ScenarioEvent { kind = "wrap_up", runId = runId }, runtime);
                    TopicPlaybackResult closed = history.CompleteFlow(handle, runId, handle.FlowInvocationId, "player_ended", currentTime);
                    return new Dictionary<string, object> { { "action", action }, { "historyStatus", closed.Status }, { "flowStatus", session.Running == null ? "ended" : session.Running.status } };
                }
                if (action != "continue") throw new InvalidOperationException("Only acknowledged dialogue continue and cancel are supported in this preview path.");
                if (before.definition == null || !before.definition.steps.Any(item => item != null && item.id == before.currentStepId && item.kind == "dialogue"))
                    throw new InvalidOperationException("This event cannot acknowledge the current non-dialogue Flow step.");
                ScenarioStep completedStep = before.definition.steps.First(item => item != null && item.id == before.currentStepId);
                ScenarioBeat completedBeat = completedStep.beats == null ? null : completedStep.beats.FirstOrDefault(item => item != null);
                IReadOnlyList<ScenarioEffect> effects = session.Advance(new ScenarioEvent
                    { kind = "dialogue_complete", runId = runId, stepId = before.currentStepId }, runtime);
                effects = WorkbenchPreviewFlowEffectDriver.Resolve(session, runtime, runId, package, release,
                    fixture, effects, hostEffects, out List<object> activityRuns);
                var observation = new ScenarioHistoryObservation
                {
                    operationKey = "preview:" + runId + ":" + before.currentStepExecutionId,
                    kind = "scenario_step_completed", runId = runId, scenarioId = before.definition.scenarioId,
                    invocationId = before.currentInvocationId, parentInvocationId = before.currentParentInvocationId,
                    callExecutionId = before.currentCallExecutionId, flowId = before.currentFlowId,
                    flowStoreId = before.currentFlowStoreId, flowRevision = before.currentFlowRevision,
                    flowHash = before.currentFlowHash, stepExecutionId = before.currentStepExecutionId,
                    visitOrdinal = before.currentVisitOrdinal,
                    stepId = string.IsNullOrWhiteSpace(completedStep.sourceStepId) ? before.currentStepId : completedStep.sourceStepId,
                    occurredAtUtc = currentTime.ToString("O", CultureInfo.InvariantCulture)
                };
                TopicPlaybackResult checkpoint = history.ReportFlowStepCompleted(handle, observation, currentTime);
                ScenarioInstanceView after = session.Running;
                string flowStatus = after == null ? "ended" : after.status;
                string outcomeValue = after == null
                    ? effects.Where(effect => effect.type == "end_scenario").Select(effect => effect.outcome).LastOrDefault() ?? "failed"
                    : after.outcome;
                string historyStatus = checkpoint.Status;
                if (after == null || after.status == "ended")
                {
                    TopicPlaybackResult completed = history.CompleteFlow(handle, runId, handle.FlowInvocationId, outcomeValue, currentTime);
                    historyStatus = completed.Status;
                }
                return new Dictionary<string, object>
                {
                    { "action", action }, { "completedStepId", before.currentStepId },
                    { "flowOutcome", outcomeValue },
                    { "acknowledgedDialogue", completedBeat == null ? null : new Dictionary<string, object>
                        { { "beatId", completedBeat.id }, { "text", completedBeat.text },
                          { "displayText", completedBeat.displayText }, { "language", completedBeat.language },
                          { "expression", completedBeat.expression } } },
                    { "flowStatus", flowStatus }, { "effects", effects.Select(effect => (object)new Dictionary<string, object>
                        { { "type", effect.type }, { "stepId", effect.stepId },
                          { "outcome", effect.type == "release_activity" ? "released" : effect.outcome } }).ToList() },
                    { "activityRuns", activityRuns },
                    { "historyStatus", historyStatus }, { "historyDirty", history.IsDirty },
                    { "isConsumed", history.IsConsumed(handle.InstanceId) }
                };
            }
            catch (Exception exception)
            {
                failure = WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", exception.Message);
                return null;
            }
        }

        private static bool TryParseExplicitUtc(string value, out DateTimeOffset timestamp)
        {
            timestamp = default(DateTimeOffset);
            bool hasOffset = !string.IsNullOrWhiteSpace(value) && (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(value, "[+-][0-9]{2}:[0-9]{2}$"));
            return hasOffset && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
        }
    }
}
