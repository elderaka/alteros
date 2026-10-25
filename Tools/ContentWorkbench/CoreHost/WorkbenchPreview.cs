using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AlterOS.Dialogue;
using AlterOS.Scenarios;
using AlterOS.Stats;
using AlterOS.Topics;

namespace AlterOS.ContentWorkbench
{
    internal static class WorkbenchPreview
    {
        private const int MaximumEvents = 256;

        public static WorkbenchResult Validate(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            if (JsonWire.String(payload, "kindId").Length > 0)
                return WorkbenchSchemaCommands.Validate(payload);

            string kind = JsonWire.String(payload, "kind");
            object rawDocument;
            if (kind != "topic" && kind != "scenario")
                return WorkbenchResult.Failure("invalid", "content_kind_invalid", "/kind", "kind must be topic or scenario.");
            if (!payload.TryGetValue("document", out rawDocument) || JsonWire.AsObject(rawDocument) == null)
                return WorkbenchResult.Failure("invalid", "content_document_invalid", "/document", "document must be a JSON object.");

            TrustedFixtureContext fixture;
            WorkbenchResult fixtureFailure;
            if (!TryGetFixture(environment, payload, out fixture, out fixtureFailure)) return fixtureFailure;

            var diagnostics = new List<WorkbenchDiagnostic>();
            if (kind == "topic")
            {
                TopicDefinition definition;
                try { definition = (TopicDefinition)JsonWire.ConvertTo(rawDocument, typeof(TopicDefinition)); }
                catch (Exception exception)
                {
                    return WorkbenchResult.Failure("invalid", "topic_document_invalid", "/document", "Topic JSON cannot be projected to the production TopicDefinition: " + exception.GetType().Name);
                }
                foreach (string error in TopicDefinitionValidator.Validate(definition))
                {
                    string path = TopicErrorPath(error);
                    diagnostics.Add(new WorkbenchDiagnostic("topic_validation_error", path, error));
                }
            }
            else
            {
                ScenarioDefinition definition;
                try { definition = (ScenarioDefinition)JsonWire.ConvertTo(rawDocument, typeof(ScenarioDefinition)); }
                catch (Exception exception)
                {
                    return WorkbenchResult.Failure("invalid", "scenario_document_invalid", "/document", "Scenario JSON cannot be projected to the production ScenarioDefinition: " + exception.GetType().Name);
                }
                ScenarioRuntimeSnapshot context = fixture.CreateRuntimeSnapshot(definition == null ? "" : definition.scenarioId);
                IReadOnlyList<string> errors = ScenarioDefinitionValidator.Validate(definition, context);
                foreach (string error in errors)
                    diagnostics.Add(MapScenarioError(error, definition, context));
            }

            string status = diagnostics.Count == 0 ? "ok" : "invalid";
            return WorkbenchResult.WithDiagnostics(status, new Dictionary<string, object>
            {
                { "kind", kind },
                { "valid", diagnostics.Count == 0 },
                { "fixtureId", fixture.FixtureId },
                { "fixtureOnly", true },
            }, diagnostics);
        }

        public static WorkbenchResult Run(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        {
            string kind;
            WorkbenchResult kindFailure;
            if (!WorkbenchSchemaCommands.TryResolvePreviewKind(payload, out kind, out kindFailure))
                return kindFailure;
            if (JsonWire.String(payload, "kindId").Length > 0)
            {
                if (!TryGetFixture(environment, payload, out TrustedFixtureContext typedFixture, out WorkbenchResult typedFixtureFailure))
                    return typedFixtureFailure;
                return WorkbenchPreviewRuntime.Run(environment, payload, kind, typedFixture);
            }
            object rawDocument;
            if (kind != "topic" && kind != "scenario")
                return WorkbenchResult.Failure("invalid", "content_kind_invalid", "/kind", "kind must be topic or scenario.");
            if (!payload.TryGetValue("document", out rawDocument) || JsonWire.AsObject(rawDocument) == null)
                return WorkbenchResult.Failure("invalid", "content_document_invalid", "/document", "document must be a JSON object.");

            TrustedFixtureContext fixture;
            WorkbenchResult fixtureFailure;
            if (!TryGetFixture(environment, payload, out fixture, out fixtureFailure)) return fixtureFailure;

            if (kind == "topic") return RunTopic(payload, rawDocument, fixture);
            return RunScenario(payload, rawDocument, fixture);
        }

        private static WorkbenchResult RunTopic(IDictionary<string, object> payload, object rawDocument, TrustedFixtureContext fixture)
        {
            TopicDefinition definition;
            try { definition = (TopicDefinition)JsonWire.ConvertTo(rawDocument, typeof(TopicDefinition)); }
            catch (Exception exception)
            {
                return WorkbenchResult.Failure("invalid", "topic_document_invalid", "/document", "Topic JSON cannot be projected to the production TopicDefinition: " + exception.GetType().Name);
            }

            List<string> errors = TopicDefinitionValidator.Validate(definition);
            if (errors.Count != 0)
                return WorkbenchResult.WithDiagnostics("invalid", null, errors.Select(error => new WorkbenchDiagnostic("topic_validation_error", TopicErrorPath(error), error)));

            string instanceId = JsonWire.String(payload, "instanceId");
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 128)
                return WorkbenchResult.Failure("invalid", "preview_instance_invalid", "/instanceId", "Topic preview needs an explicit occurrence ID of at most 128 characters.");
            string source = JsonWire.String(payload, "source", "authored");
            if (source != "authored" && source != "generated")
                return WorkbenchResult.Failure("invalid", "preview_source_invalid", "/source", "Topic source must be authored or generated.");
            string replayPolicy = JsonWire.String(payload, "replayPolicy");
            if (replayPolicy.Length == 0) replayPolicy = null;

            double draw;
            if (!JsonWire.TryDouble(payload, "draw", out draw) || double.IsNaN(draw) || double.IsInfinity(draw) || draw < 0d || draw >= 1d)
                return WorkbenchResult.Failure("invalid", "preview_draw_invalid", "/draw", "draw must be finite and in [0,1).");
            DateTimeOffset nowUtc;
            string timestamp = JsonWire.String(payload, "nowUtc");
            if (!TryParseExplicitUtc(timestamp, out nowUtc))
                return WorkbenchResult.Failure("invalid", "preview_time_invalid", "/nowUtc", "nowUtc must be an ISO-8601 timestamp with an explicit UTC offset.");

            var context = fixture.CreateTopicContext();
            TopicInstance instance;
            try { instance = TopicInstance.Create(definition, instanceId, source, replayPolicy); }
            catch (Exception exception)
            {
                return WorkbenchResult.Failure("invalid", "topic_instance_invalid", "/instanceId", "Topic occurrence is invalid: " + exception.Message);
            }

            var harness = new TopicPlaygroundTopicHarness(new TopicHistory());
            TopicPlaygroundTopicRun run;
            try { run = harness.Select(definition, instance, context, nowUtc, draw); }
            catch (Exception exception)
            {
                return WorkbenchResult.Failure("invalid", "topic_selection_invalid", "/document", "The production Topic selector rejected this preview: " + exception.Message);
            }

            var eventResults = new List<object>();
            IEnumerable events;
            if (payload.TryGetValue("events", out object rawEvents))
            {
                events = JsonWire.AsArray(rawEvents);
                if (events == null || rawEvents is string)
                    return WorkbenchResult.Failure("invalid", "preview_events_invalid", "/events", "events must be a JSON array.");
                var buffered = new List<object>();
                foreach (object item in events) buffered.Add(item);
                if (buffered.Count > MaximumEvents)
                    return WorkbenchResult.Failure("invalid", "preview_events_too_many", "/events", "Preview supports at most 256 events.");
                foreach (object item in buffered)
                {
                    IDictionary<string, object> input = JsonWire.AsObject(item);
                    if (input == null)
                        return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", "Each Topic event must be a JSON object.");
                    WorkbenchResult eventFailure;
                    object eventResult = ApplyTopicEvent(harness, run, input, ref nowUtc, out eventFailure);
                    if (eventFailure != null) return eventFailure;
                    eventResults.Add(eventResult);
                }
            }

            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kind", "topic" },
                { "fixtureId", fixture.FixtureId },
                { "fixtureOnly", true },
                { "routingMode", "direct synthetic family; not semantic routing" },
                { "furniture", FurnitureFixtureStatus(fixture) },
                { "selectedScenarioId", run.Selection.SelectedScenario == null ? "" : run.Selection.SelectedScenario.scenarioId },
                { "selectionReason", run.Selection.Reason },
                { "winningPriority", run.Selection.WinningPriority.HasValue ? (object)run.Selection.WinningPriority.Value : null },
                { "draw", run.Selection.Draw },
                { "candidates", TopicCandidates(run.Selection) },
                { "currentNode", run.Playback == null ? null : DialogueNode(run.Playback.Current) },
                { "historyDirty", harness.History.IsDirty },
                { "historyPersistence", "detached-memory-only; no player save" },
                { "events", eventResults },
                { "providerCalls", 0 },
                { "ttsCalls", 0 },
            });
        }

        private static object ApplyTopicEvent(TopicPlaygroundTopicHarness harness, TopicPlaygroundTopicRun run,
            IDictionary<string, object> input, ref DateTimeOffset currentTime, out WorkbenchResult failure)
        {
            failure = null;
            string action = JsonWire.String(input, "action");
            try
            {
                switch (action)
                {
                    case "continue":
                        return new Dictionary<string, object> { { "action", action }, { "currentNode", DialogueNode(harness.Continue(run)) } };
                    case "choose":
                        int choice;
                        if (!JsonWire.TryInt(input, "index", out choice))
                            throw new InvalidDataException("choose requires an integer index.");
                        return new Dictionary<string, object> { { "action", action }, { "currentNode", DialogueNode(harness.Choose(run, choice)) } };
                    case "present":
                    case "complete":
                        DateTimeOffset eventTime;
                        if (!TryParseExplicitUtc(JsonWire.String(input, "atUtc"), out eventTime) || eventTime < currentTime)
                            throw new InvalidDataException("Lifecycle atUtc must be explicit and monotonic.");
                        currentTime = eventTime;
                        TopicPlaybackResult result = action == "present"
                            ? harness.Present(run, JsonWire.String(input, "nodeId"), eventTime)
                            : harness.Complete(run, eventTime);
                        return new Dictionary<string, object>
                        {
                            { "action", action }, { "status", result.Status }, { "isConsumed", result.IsConsumed },
                            { "requiresSave", result.RequiresSave }, { "historyDirty", harness.History.IsDirty },
                        };
                    case "cancel":
                    case "fail":
                        TopicPlaybackResult terminal = action == "cancel" ? harness.Cancel(run) : harness.Fail(run);
                        return new Dictionary<string, object>
                        {
                            { "action", action }, { "status", terminal.Status }, { "isConsumed", terminal.IsConsumed },
                            { "requiresSave", terminal.RequiresSave }, { "historyDirty", harness.History.IsDirty },
                        };
                    default:
                        throw new InvalidDataException("Unsupported Topic preview event.");
                }
            }
            catch (Exception exception)
            {
                failure = WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", exception.Message);
                return null;
            }
        }

        private static WorkbenchResult RunScenario(IDictionary<string, object> payload, object rawDocument, TrustedFixtureContext fixture)
        {
            ScenarioDefinition definition;
            try { definition = (ScenarioDefinition)JsonWire.ConvertTo(rawDocument, typeof(ScenarioDefinition)); }
            catch (Exception exception)
            {
                return WorkbenchResult.Failure("invalid", "scenario_document_invalid", "/document", "Scenario JSON cannot be projected to the production ScenarioDefinition: " + exception.GetType().Name);
            }
            ScenarioRuntimeSnapshot context = fixture.CreateRuntimeSnapshot(definition == null ? "" : definition.scenarioId);
            IReadOnlyList<string> validationErrors = ScenarioDefinitionValidator.Validate(definition, context);
            if (validationErrors.Count != 0)
                return WorkbenchResult.WithDiagnostics("invalid", null, validationErrors.Select(error => MapScenarioError(error, definition, context)));

            var session = new ScenarioPlaygroundSession(definition, fixture.RunId, context);
            var reports = new List<object> { ScenarioReport(session.InitialReport) };
            IEnumerable events;
            if (payload.TryGetValue("events", out object rawEvents))
            {
                events = JsonWire.AsArray(rawEvents);
                if (events == null || rawEvents is string)
                    return WorkbenchResult.Failure("invalid", "preview_events_invalid", "/events", "events must be a JSON array.");
                var buffered = new List<object>();
                foreach (object item in events) buffered.Add(item);
                if (buffered.Count > MaximumEvents)
                    return WorkbenchResult.Failure("invalid", "preview_events_too_many", "/events", "Preview supports at most 256 events.");

                foreach (object item in buffered)
                {
                    IDictionary<string, object> eventInput = JsonWire.AsObject(item);
                    if (eventInput == null)
                        return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", "Each Scenario event must be a JSON object.");
                    ScenarioEvent scenarioEvent;
                    double atSeconds;
                    string error;
                    if (!TryScenarioEvent(eventInput, fixture.RunId, out scenarioEvent, out atSeconds, out error))
                        return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", error);
                    try { reports.Add(ScenarioReport(session.Apply(scenarioEvent, atSeconds))); }
                    catch (Exception exception)
                    {
                        return WorkbenchResult.Failure("invalid", "preview_event_rejected", "/events", exception.Message);
                    }
                }
            }

            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kind", "scenario" },
                { "fixtureId", fixture.FixtureId },
                { "fixtureOnly", true },
                { "furniture", FurnitureFixtureStatus(fixture) },
                { "runId", fixture.RunId },
                { "reports", reports },
                { "providerCalls", 0 },
                { "ttsCalls", 0 },
                { "note", "Scenario actions are simulated only by the production ScenarioPlaygroundSession and fixture-owned capability allowlist." },
            });
        }

        private static bool TryScenarioEvent(IDictionary<string, object> input, string trustedRunId,
            out ScenarioEvent scenarioEvent, out double atSeconds, out string error)
        {
            scenarioEvent = null;
            atSeconds = 0d;
            error = "";
            string kind = JsonWire.String(input, "kind");
            if (!new[] { "dialogue_complete", "answer", "skip", "cancel", "wrap_up", "activity_completed", "player_interaction", "can_talk_available", "time_advanced", "condition_met", "condition_failed", "activity_result", "continue_suspended" }.Contains(kind, StringComparer.Ordinal))
            {
                error = "Unsupported Scenario event kind.";
                return false;
            }
            if (!JsonWire.TryDouble(input, "atSeconds", out atSeconds) || double.IsNaN(atSeconds) || double.IsInfinity(atSeconds) || atSeconds < 0d)
            {
                error = "atSeconds must be finite and nonnegative.";
                return false;
            }
            bool success;
            if (input.ContainsKey("success") && !JsonWire.TryBoolean(input, "success", out success))
            {
                error = "success must be a boolean.";
                return false;
            }
            JsonWire.TryBoolean(input, "success", out success);
            bool canTalk;
            JsonWire.TryBoolean(input, "canTalk", out canTalk);
            bool quiet;
            JsonWire.TryBoolean(input, "quiet", out quiet);
            bool? focusAllowed = null;
            bool focus;
            if (JsonWire.TryBoolean(input, "focusAllowed", out focus)) focusAllowed = focus;

            scenarioEvent = new ScenarioEvent
            {
                kind = kind,
                runId = trustedRunId,
                stepId = JsonWire.String(input, "stepId"),
                questionId = JsonWire.String(input, "questionId"),
                answerId = JsonWire.String(input, "answerId"),
                freeText = JsonWire.String(input, "freeText"),
                value = JsonWire.String(input, "value"),
                activityId = JsonWire.String(input, "activityId"),
                conditionId = JsonWire.String(input, "conditionId"),
                participant = JsonWire.String(input, "participant"),
                elapsedSeconds = atSeconds,
                success = success,
                canTalk = canTalk,
                quiet = quiet,
                focusAllowed = focusAllowed,
            };
            return true;
        }

        private static object ScenarioReport(ScenarioPlaygroundReport report)
        {
            return new Dictionary<string, object>
            {
                { "atSeconds", report.nowSeconds },
                { "currentStepId", report.currentStepId },
                { "status", report.status },
                { "hasSimulatedActivity", report.hasSimulatedActivity },
                { "paidStageRequestCount", report.paidStageRequestCount },
                { "effects", report.effects.Select(ScenarioEffectView).ToList() },
            };
        }

        private static object ScenarioEffectView(ScenarioEffect effect)
        {
            var result = new Dictionary<string, object>
            {
                { "type", effect.type }, { "runId", effect.runId }, { "stepId", effect.stepId },
                { "outcome", effect.outcome }, { "beatId", effect.beatId }, { "eventId", effect.eventId },
                { "beatIndex", effect.beatIndex },
            };
            if (effect.step != null)
                result.Add("step", new Dictionary<string, object> { { "id", effect.step.id }, { "kind", effect.step.kind }, { "questionId", effect.step.questionId } });
            if (effect.beat != null) result.Add("beat", ScenarioBeatView(effect.beat));
            if (effect.activityRequest != null)
            {
                result.Add("activityRequest", new Dictionary<string, object>
                {
                    { "capabilityId", effect.activityRequest.capabilityId },
                    { "arguments", effect.activityRequest.arguments.Select(item => new Dictionary<string, object> { { "name", item.name }, { "value", item.value } }).ToList() },
                    { "synthetic", true },
                });
            }
            return result;
        }

        private static object ScenarioBeatView(ScenarioBeat beat)
        {
            return new Dictionary<string, object>
            {
                { "id", beat.id }, { "text", beat.text }, { "displayText", beat.displayText },
                { "language", beat.language }, { "expression", beat.expression },
            };
        }

        private static object TopicCandidates(TopicSelectionResult selection)
        {
            var candidates = new List<object>();
            foreach (TopicCandidateTrace candidate in selection.Candidates)
            {
                candidates.Add(new Dictionary<string, object>
                {
                    { "scenarioId", candidate.ScenarioId },
                    { "priority", candidate.Priority },
                    { "reason", candidate.Reason },
                    { "effectiveWeight", candidate.EffectiveWeight },
                    { "gate", TopicConditionView(candidate.Gate) },
                    { "modifiers", candidate.Modifiers.Select(TopicModifierView).ToList() },
                });
            }
            return candidates;
        }

        private static object TopicModifierView(TopicWeightTrace modifier)
        {
            return new Dictionary<string, object>
            {
                { "truth", modifier.Truth.ToString().ToLowerInvariant() },
                { "authoredAmount", modifier.AuthoredAmount },
                { "appliedAmount", modifier.AppliedAmount },
                { "condition", TopicConditionView(modifier.Condition) },
            };
        }

        private static object TopicConditionView(TopicConditionResult condition)
        {
            if (condition == null) return null;
            return new Dictionary<string, object>
            {
                { "truth", condition.Truth.ToString().ToLowerInvariant() },
                { "reason", condition.Reason },
                { "boundObjectId", condition.BoundObjectId },
                { "children", condition.Children.Select(TopicConditionView).ToList() },
            };
        }

        private static object DialogueNode(CanonicalDialogueNode node)
        {
            if (node == null) return null;
            return new Dictionary<string, object>
            {
                { "id", node.id }, { "speaker", node.speaker }, { "text", node.text }, { "displayText", node.displayText },
                { "language", node.language }, { "expression", node.presentation == null ? "" : node.presentation.expression },
                { "choices", (node.choices ?? new List<CanonicalDialogueChoice>()).Select(choice => new Dictionary<string, object> { { "label", choice.label }, { "nextId", choice.nextId } }).ToList() },
            };
        }

        private static object FurnitureFixtureStatus(TrustedFixtureContext fixture)
        {
            return new Dictionary<string, object>
            {
                { "integration", "unavailable" },
                { "fixtureOnly", true },
                { "state", fixture.State },
                { "reasonKey", fixture.ReasonKey },
                { "message", "These are host-owned synthetic inputs, not a FurnitureCatalogueRegistry or ArrangementResolver result." },
            };
        }

        private static WorkbenchDiagnostic MapScenarioError(string error, ScenarioDefinition definition, ScenarioRuntimeSnapshot context)
        {
            if (error == "Scenario successor does not exist.")
            {
                string missingPath = FindMissingSuccessorPath(definition);
                return new WorkbenchDiagnostic("scenario_missing_successor", missingPath, error);
            }
            if (error == "Action capability is not host-allowlisted.")
            {
                ScenarioStep step = definition.steps.FirstOrDefault(item => item != null && item.kind == "action" && !context.capabilities.Any(capability => capability.capabilityId == item.capabilityId));
                return new WorkbenchDiagnostic("scenario_capability_not_allowlisted", step == null ? "/steps" : "/steps/" + step.id + "/capabilityId", error);
            }
            if (error == "Scenario Topic is outside the host context.")
                return new WorkbenchDiagnostic("scenario_topic_not_allowlisted", "/topicId", error);
            if (error == "Beat expression is not host-allowlisted.")
            {
                ScenarioStep step = definition.steps.FirstOrDefault(item => item != null &&
                    ((item.beats != null && item.beats.Any(beat => beat != null && !string.IsNullOrEmpty(beat.expression) && !context.allowedExpressions.Contains(beat.expression))) ||
                     (item.question != null && !string.IsNullOrEmpty(item.question.expression) && !context.allowedExpressions.Contains(item.question.expression))));
                return new WorkbenchDiagnostic("scenario_expression_not_allowlisted", step == null ? "/steps" : "/steps/" + step.id, error);
            }
            if (error == "Wait condition is not host-allowlisted.")
            {
                ScenarioStep step = definition.steps.FirstOrDefault(item => item != null && item.kind == "wait" && !context.waitConditions.Contains(item.conditionId));
                return new WorkbenchDiagnostic("scenario_wait_not_allowlisted", step == null ? "/steps" : "/steps/" + step.id + "/conditionId", error);
            }
            string genericPath = error.IndexOf("step", StringComparison.OrdinalIgnoreCase) >= 0 ? "/steps" : "/document";
            return new WorkbenchDiagnostic("scenario_validation_error", genericPath, error);
        }

        private static string FindMissingSuccessorPath(ScenarioDefinition definition)
        {
            if (definition == null || definition.steps == null) return "/steps";
            var ids = new HashSet<string>(definition.steps.Where(step => step != null).Select(step => step.id ?? ""), StringComparer.Ordinal);
            if (!ids.Contains(definition.entryStepId ?? "")) return "/entryStepId";
            foreach (ScenarioStep step in definition.steps)
            {
                if (step == null) continue;
                string prefix = "/steps/" + step.id + "/";
                string[] names = { "nextStepId", "freeTextNextStepId", "skipNextStepId", "cancelNextStepId", "successNextStepId", "failureNextStepId", "defaultNextStepId" };
                foreach (string name in names)
                {
                    string target = (string)typeof(ScenarioStep).GetField(name).GetValue(step);
                    if (HasMissing(ids, target)) return prefix + name;
                }
                if (step.options != null)
                    for (int index = 0; index < step.options.Count; index++)
                        if (step.options[index] != null && HasMissing(ids, step.options[index].nextStepId))
                            return prefix + "options/" + index + "/nextStepId";
                if (step.cases != null)
                    for (int index = 0; index < step.cases.Count; index++)
                        if (step.cases[index] != null && HasMissing(ids, step.cases[index].nextStepId))
                            return prefix + "cases/" + index + "/nextStepId";
            }
            return "/steps";
        }

        private static bool HasMissing(HashSet<string> ids, string target)
        {
            return !string.IsNullOrEmpty(target) && !ids.Contains(target);
        }

        private static string TopicErrorPath(string error)
        {
            if (error.IndexOf("schemaVersion", StringComparison.OrdinalIgnoreCase) >= 0) return "/schemaVersion";
            if (error.IndexOf("topicId", StringComparison.OrdinalIgnoreCase) >= 0) return "/topicId";
            if (error.IndexOf("anchor", StringComparison.OrdinalIgnoreCase) >= 0) return "/anchors";
            if (error.IndexOf("scenario", StringComparison.OrdinalIgnoreCase) >= 0 || error.IndexOf("graph", StringComparison.OrdinalIgnoreCase) >= 0) return "/scenarios";
            return "/document";
        }

        private static bool TryGetFixture(WorkbenchEnvironment environment, IDictionary<string, object> payload,
            out TrustedFixtureContext fixture, out WorkbenchResult failure)
        {
            string fixtureId = JsonWire.String(payload, "fixtureId");
            if (string.IsNullOrWhiteSpace(fixtureId))
            {
                fixture = null;
                failure = WorkbenchResult.Failure("invalid", "synthetic_fixture_required", "/fixtureId", "Choose a trusted synthetic fixture context explicitly.");
                return false;
            }
            if (!TrustedFixtureContext.TryLoad(environment, fixtureId, out fixture, out string error))
            {
                failure = WorkbenchResult.Failure("unavailable", "synthetic_fixture_unavailable", "/fixtureId", error);
                return false;
            }
            failure = null;
            return true;
        }

        private static bool TryParseExplicitUtc(string value, out DateTimeOffset timestamp)
        {
            timestamp = default(DateTimeOffset);
            if (string.IsNullOrWhiteSpace(value)) return false;
            bool explicitOffset = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(value, "[+-][0-9]{2}:[0-9]{2}$");
            return explicitOffset && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
        }
    }

    internal sealed class WorkbenchPreviewFurnitureInstance
    {
        public string instanceId = string.Empty;
        public string definitionId = string.Empty;
        public string usePointId = string.Empty;
        public string useId = string.Empty;
        public int usePointRevision;
        public long instanceRevision;
        public bool powerKnown;
        public string powerState = string.Empty;
        public bool available;
        public bool occupied;
        public string reservationOwnerId = string.Empty;
        public float distanceToPlayer;
    }

    internal sealed class TrustedFixtureContext
    {
        public string FixtureId;
        public string RunId;
        public string TurnId;
        public long Revision;
        public string State;
        public string ReasonKey;
        private List<string> topicIds;
        private List<string> expressions;
        private List<string> waitConditions;
        private List<ScenarioCapabilitySnapshot> capabilities;
        private List<ScenarioObjectSnapshot> objects;
        private IDictionary<string, object> topicContext;
        private bool hasPreviewState;
        public long PreviewLayoutRevision;
        public long PreviewGameRevision;
        public string PreviewPlayerId = string.Empty;
        public bool PreviewReturnAvailable;
        public bool PreviewBarkVisible;
        public readonly List<WorkbenchPreviewFurnitureInstance> PreviewFurniture = new List<WorkbenchPreviewFurnitureInstance>();
        public readonly Dictionary<string, long> PreviewItemQuantities = new Dictionary<string, long>(StringComparer.Ordinal);

        public bool HasPreviewState { get { return hasPreviewState; } }
        public IEnumerable<ScenarioCapabilitySnapshot> PreviewCapabilities { get { return capabilities; } }

        public ScenarioRuntimeSnapshot CreateRuntimeSnapshot(string scenarioId)
        {
            var runtimeObjects = new List<ScenarioObjectSnapshot>(objects);
            foreach (WorkbenchPreviewFurnitureInstance item in PreviewFurniture)
            {
                int knownIndex = runtimeObjects.FindIndex(existing => existing.objectId == item.instanceId);
                var activityTarget = new ScenarioObjectSnapshot(item.instanceId, "activity_target");
                if (knownIndex < 0) runtimeObjects.Add(activityTarget);
                else runtimeObjects[knownIndex] = activityTarget;
            }
            return new ScenarioRuntimeSnapshot(scenarioId, RunId, TurnId, Revision, "", topicIds, expressions, capabilities, runtimeObjects, waitConditions);
        }

        public TopicContext CreateTopicContext()
        {
            var context = new TopicContext();
            object rawHour;
            int hour = -1;
            if (topicContext.TryGetValue("localHour", out rawHour) && JsonWire.IsNumber(rawHour))
                hour = Convert.ToInt32(rawHour, CultureInfo.InvariantCulture);
            if (hour < -1 || hour > 23) throw new InvalidDataException("Trusted fixture localHour is outside the supported range.");
            context.localHour = hour;

            object rawStats;
            if (topicContext.TryGetValue("stats", out rawStats))
            {
                foreach (object item in RequireArray(rawStats, "topicContext.stats"))
                {
                    IDictionary<string, object> entry = RequireObject(item, "topicContext.stats[]");
                    if (!JsonWire.TryBoolean(entry, "isKnown", out bool isKnown) || !isKnown) continue;
                    string id = JsonWire.String(entry, "statId");
                    if (!JsonWire.TryDouble(entry, "value", out double value))
                        throw new InvalidDataException("Trusted fixture has an invalid known stat: " + id);
                    string setError;
                    if (!context.stats.TrySet(id, Convert.ToSingle(value, CultureInfo.InvariantCulture), out setError))
                        throw new InvalidDataException("Trusted fixture has an invalid known stat: " + setError);
                }
            }

            object rawFacts;
            if (topicContext.TryGetValue("facts", out rawFacts))
            {
                foreach (object item in RequireArray(rawFacts, "topicContext.facts"))
                {
                    IDictionary<string, object> entry = RequireObject(item, "topicContext.facts[]");
                    if (!JsonWire.TryBoolean(entry, "isKnown", out bool known) || !known) continue;
                    if (string.IsNullOrWhiteSpace(JsonWire.String(entry, "key")) || !JsonWire.TryBoolean(entry, "value", out bool value))
                        throw new InvalidDataException("Trusted fixture has an invalid known fact.");
                    context.facts.Add(JsonWire.String(entry, "key"), value);
                }
            }

            if (topicContext.TryGetValue("objectsComplete", out object rawComplete))
            {
                if (!(rawComplete is bool)) throw new InvalidDataException("Trusted fixture objectsComplete must be boolean.");
                context.objectsComplete = (bool)rawComplete;
            }
            object rawObjects;
            if (topicContext.TryGetValue("objects", out rawObjects))
            {
                foreach (object item in RequireArray(rawObjects, "topicContext.objects"))
                {
                    IDictionary<string, object> entry = RequireObject(item, "topicContext.objects[]");
                    var snapshot = new TopicObjectSnapshot { id = JsonWire.String(entry, "id"), kind = JsonWire.String(entry, "kind") };
                    foreach (object flagRaw in ReadOptionalArray(entry, "flags"))
                    {
                        IDictionary<string, object> flag = RequireObject(flagRaw, "topicContext.objects[].flags[]");
                        if (JsonWire.TryBoolean(flag, "isKnown", out bool known) && known)
                        {
                            if (!JsonWire.TryBoolean(flag, "value", out bool value)) throw new InvalidDataException("Trusted fixture object flag is invalid.");
                            snapshot.flags.Add(JsonWire.String(flag, "key"), value);
                        }
                    }
                    foreach (object numberRaw in ReadOptionalArray(entry, "numbers"))
                    {
                        IDictionary<string, object> number = RequireObject(numberRaw, "topicContext.objects[].numbers[]");
                        if (JsonWire.TryBoolean(number, "isKnown", out bool known) && known)
                        {
                            if (!JsonWire.TryDouble(number, "value", out double value)) throw new InvalidDataException("Trusted fixture object number is invalid.");
                            snapshot.numbers.Add(JsonWire.String(number, "key"), value);
                        }
                    }
                    context.objects.Add(snapshot);
                }
            }
            ValidatePreviewState();
            return context;
        }

        private void ValidatePreviewState()
        {
            if (!hasPreviewState) return;
            if (PreviewLayoutRevision < 0 || PreviewGameRevision < 0 || string.IsNullOrWhiteSpace(PreviewPlayerId))
                throw new InvalidDataException("Trusted preview state identity or revisions are invalid.");
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkbenchPreviewFurnitureInstance item in PreviewFurniture)
                if (item == null || string.IsNullOrWhiteSpace(item.instanceId) || !instanceIds.Add(item.instanceId) ||
                    string.IsNullOrWhiteSpace(item.definitionId) || string.IsNullOrWhiteSpace(item.usePointId) ||
                    string.IsNullOrWhiteSpace(item.useId) || item.usePointRevision < 0 || item.instanceRevision < 0 ||
                    float.IsNaN(item.distanceToPlayer) || float.IsInfinity(item.distanceToPlayer) || item.distanceToPlayer < 0f ||
                    (item.powerKnown && item.powerState != "Off" && item.powerState != "On") ||
                    (!item.powerKnown && item.powerState.Length > 0) || string.IsNullOrWhiteSpace(item.reservationOwnerId) && !item.available)
                    throw new InvalidDataException("Trusted preview Furniture instance is malformed.");
            foreach (KeyValuePair<string, long> item in PreviewItemQuantities)
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value < 0)
                    throw new InvalidDataException("Trusted preview Item quantity is malformed.");
        }

        public static bool TryLoad(WorkbenchEnvironment environment, string fixtureId, out TrustedFixtureContext selected, out string error)
        {
            selected = null;
            error = "Trusted synthetic fixture context is unavailable.";
            try
            {
                string path = Path.GetFullPath(environment.HostContextsPath);
                string root = Path.GetFullPath(Path.Combine(environment.ProjectRoot, "Tools", "ContentWorkbench", "Fixtures"));
                if (!IsUnderRoot(root, path) || HasReparse(path) || !File.Exists(path)) return false;
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > 1024 * 1024) return false;
                IDictionary<string, object> document = JsonWire.AsObject(JsonWire.Deserialize(bytes));
                if (document == null || !JsonWire.TryInt(document, "schemaVersion", out int version) || version != 1) return false;
                object rawContexts;
                if (!document.TryGetValue("contexts", out rawContexts)) return false;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (object raw in RequireArray(rawContexts, "contexts"))
                {
                    IDictionary<string, object> entry = RequireObject(raw, "contexts[]");
                    string id = JsonWire.String(entry, "fixtureId");
                    if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) return false;
                    if (id != fixtureId) continue;
                    selected = Parse(entry);
                    if (ScenarioDefinitionValidator.ValidateRuntime(selected.CreateRuntimeSnapshot("fixture-validation")).Count != 0)
                    {
                        selected = null;
                        return false;
                    }
                    selected.CreateTopicContext();
                    error = "";
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = "Trusted synthetic fixture is invalid: " + exception.GetType().Name;
                return false;
            }
            return false;
        }

        private static TrustedFixtureContext Parse(IDictionary<string, object> entry)
        {
            var context = new TrustedFixtureContext
            {
                FixtureId = JsonWire.String(entry, "fixtureId"),
                RunId = JsonWire.String(entry, "runId"),
                TurnId = JsonWire.String(entry, "turnId"),
                State = "unknown",
                ReasonKey = "evidence_unknown",
                topicIds = StringArray(entry, "topicIds"),
                expressions = StringArray(entry, "expressions"),
                waitConditions = StringArray(entry, "waitConditions"),
                capabilities = new List<ScenarioCapabilitySnapshot>(),
                objects = new List<ScenarioObjectSnapshot>(),
                topicContext = RequireObject(entry.ContainsKey("topicContext") ? entry["topicContext"] : null, "topicContext"),
            };
            if (!JsonWire.TryInt(entry, "revision", out int revision) || revision < 0 || string.IsNullOrWhiteSpace(context.RunId) || string.IsNullOrWhiteSpace(context.TurnId))
                throw new InvalidDataException("Fixture run identity is invalid.");
            context.Revision = revision;

            object rawCapabilities;
            foreach (object raw in entry.TryGetValue("capabilities", out rawCapabilities) ? RequireArray(rawCapabilities, "capabilities") : new object[0])
            {
                IDictionary<string, object> capability = RequireObject(raw, "capabilities[]");
                string capabilityId = JsonWire.String(capability, "capabilityId");
                var bindings = new List<ScenarioArgumentBinding>();
                object rawBindings;
                foreach (object bindingRaw in capability.TryGetValue("argumentBindings", out rawBindings) ? RequireArray(rawBindings, "capabilities[].argumentBindings") : new object[0])
                {
                    IDictionary<string, object> binding = RequireObject(bindingRaw, "capabilities[].argumentBindings[]");
                    bindings.Add(new ScenarioArgumentBinding(JsonWire.String(binding, "name"), StringArray(binding, "values")));
                }
                context.capabilities.Add(new ScenarioCapabilitySnapshot(capabilityId, bindings));
            }

            object rawObjects;
            foreach (object raw in entry.TryGetValue("objects", out rawObjects) ? RequireArray(rawObjects, "objects") : new object[0])
            {
                IDictionary<string, object> item = RequireObject(raw, "objects[]");
                context.objects.Add(new ScenarioObjectSnapshot(JsonWire.String(item, "objectId"), JsonWire.String(item, "kind")));
            }

            IDictionary<string, object> fixtureState = JsonWire.AsObject(entry.ContainsKey("fixtureState") ? entry["fixtureState"] : null);
            if (fixtureState != null)
            {
                context.State = JsonWire.String(fixtureState, "state", "unknown");
                context.ReasonKey = JsonWire.String(fixtureState, "reasonKey", "evidence_unknown");
            }
            context.ParsePreviewState(entry);
            return context;
        }

        private void ParsePreviewState(IDictionary<string, object> entry)
        {
            if (!entry.TryGetValue("previewState", out object rawState)) return;
            IDictionary<string, object> state = RequireObject(rawState, "previewState");
            hasPreviewState = true;
            if (!JsonWire.TryLong(state, "layoutRevision", out PreviewLayoutRevision) ||
                !JsonWire.TryLong(state, "gameRevision", out PreviewGameRevision) ||
                !JsonWire.TryBoolean(state, "returnAvailable", out PreviewReturnAvailable) ||
                !JsonWire.TryBoolean(state, "barkVisible", out PreviewBarkVisible))
                throw new InvalidDataException("Trusted preview state needs explicit revisions and presentation outcomes.");
            PreviewPlayerId = JsonWire.String(state, "playerId");
            foreach (object raw in ReadOptionalArray(state, "furniture"))
            {
                IDictionary<string, object> item = RequireObject(raw, "previewState.furniture[]");
                if (!JsonWire.TryInt(item, "usePointRevision", out int usePointRevision) ||
                    !JsonWire.TryLong(item, "instanceRevision", out long instanceRevision) ||
                    !JsonWire.TryBoolean(item, "powerKnown", out bool powerKnown) ||
                    !JsonWire.TryBoolean(item, "available", out bool available) ||
                    !JsonWire.TryBoolean(item, "occupied", out bool occupied))
                    throw new InvalidDataException("Trusted preview Furniture instance fields are invalid.");
                double distanceToPlayer = 0d;
                if (item.ContainsKey("distanceToPlayer") && (!JsonWire.TryDouble(item, "distanceToPlayer", out distanceToPlayer) ||
                    double.IsNaN(distanceToPlayer) || double.IsInfinity(distanceToPlayer) || distanceToPlayer < 0d || distanceToPlayer > float.MaxValue))
                    throw new InvalidDataException("Trusted preview Furniture distance must be finite and nonnegative.");
                PreviewFurniture.Add(new WorkbenchPreviewFurnitureInstance
                {
                    instanceId = JsonWire.String(item, "instanceId"), definitionId = JsonWire.String(item, "definitionId"),
                    usePointId = JsonWire.String(item, "usePointId"), useId = JsonWire.String(item, "useId"),
                    usePointRevision = usePointRevision, instanceRevision = instanceRevision, powerKnown = powerKnown,
                    powerState = JsonWire.String(item, "powerState"), available = available, occupied = occupied,
                    reservationOwnerId = JsonWire.String(item, "reservationOwnerId"),
                    distanceToPlayer = Convert.ToSingle(distanceToPlayer, CultureInfo.InvariantCulture)
                });
            }
            foreach (object raw in ReadOptionalArray(state, "items"))
            {
                IDictionary<string, object> item = RequireObject(raw, "previewState.items[]");
                if (!JsonWire.TryLong(item, "quantity", out long quantity) || quantity < 0)
                    throw new InvalidDataException("Trusted preview Item quantity is invalid.");
                string id = JsonWire.String(item, "itemId");
                if (string.IsNullOrWhiteSpace(id) || PreviewItemQuantities.ContainsKey(id))
                    throw new InvalidDataException("Trusted preview Item IDs must be nonempty and unique.");
                PreviewItemQuantities.Add(id, quantity);
            }
            ValidatePreviewState();
        }

        private static List<string> StringArray(IDictionary<string, object> source, string key)
        {
            object raw;
            if (!source.TryGetValue(key, out raw)) return new List<string>();
            var values = new List<string>();
            foreach (object item in RequireArray(raw, key))
            {
                if (!(item is string)) throw new InvalidDataException("Fixture " + key + " must contain strings.");
                values.Add((string)item);
            }
            return values;
        }

        private static IEnumerable ReadOptionalArray(IDictionary<string, object> source, string key)
        {
            object raw;
            return source.TryGetValue(key, out raw) ? RequireArray(raw, key) : new object[0];
        }

        private static IEnumerable RequireArray(object value, string path)
        {
            IEnumerable result = JsonWire.AsArray(value);
            if (result == null || value is string) throw new InvalidDataException("Fixture " + path + " must be an array.");
            return result;
        }

        private static IDictionary<string, object> RequireObject(object value, string path)
        {
            IDictionary<string, object> result = JsonWire.AsObject(value);
            if (result == null) throw new InvalidDataException("Fixture " + path + " must be an object.");
            return result;
        }

        private static bool IsUnderRoot(string root, string path)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasReparse(string path)
        {
            string current = Path.GetPathRoot(path);
            string relative = path.Substring(current.Length);
            foreach (string component in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
            }
            return false;
        }
    }
}
