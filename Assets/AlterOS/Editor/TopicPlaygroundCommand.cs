using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AlterOS.Dialogue;
using AlterOS.Semantics;
using AlterOS.Stats;
using AlterOS.Topics;
using AlterOS.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AlterOS.Editor
{
    [Serializable]
    sealed class TopicPlaygroundCommandReport
    {
        public bool ok;
        public string query;
        public int localHour;
        public string modelId;
        public string modelFingerprint;
        public string modelFingerprintStatus;
        public string backend;
        public string indexFingerprint;
        public string fixtureSet;
        public int routeCount;
        public int anchorCount;
        public string selectedRouteId;
        public string selectedVariantPath;
        public List<TopicPlaygroundStatReport> syntheticStats = new List<TopicPlaygroundStatReport>();
        public float queryDurationMs;
        public bool usedFallback;
        public string fallbackReason;
        public string error;
        public List<TopicPlaygroundMatchReport> rankedRoutes = new List<TopicPlaygroundMatchReport>();
        public List<TopicPlaygroundAttemptReport> routeAttempts = new List<TopicPlaygroundAttemptReport>();
        public List<string> transcript = new List<string>();
        public string topicMode;
        public bool semanticRoutingAttempted;
        public string routingModeLabel;
        public string familySelectionReason;
        public string scenarioSelectionReason;
        public string selectedFamilyId;
        public string selectedScenarioId;
        public bool hasWinningPriority;
        public int winningPriority;
        public double draw;
        public string selectionTimeUtc;
        public string historyPersistence;
        public bool historyDirty;
        public bool pendingSave;
        public List<TopicPlaygroundFamilyRunReport> familyRuns = new List<TopicPlaygroundFamilyRunReport>();
    }

    [Serializable]
    sealed class TopicPlaygroundFamilyRunReport
    {
        public string query;
        public string topicFixtureSet;
        public bool semanticRoutingAttempted;
        public string routingModeLabel;
        public string familySelectionReason;
        public string scenarioSelectionReason;
        public string selectedFamilyId;
        public string instanceId;
        public string source;
        public string replayPolicy;
        public string selectedScenarioId;
        public bool hasWinningPriority;
        public int winningPriority;
        public double draw;
        public string selectionTimeUtc;
        public int localHour;
        public List<TopicPlaygroundStatReport> syntheticStats = new List<TopicPlaygroundStatReport>();
        public List<TopicPlaygroundFactReport> syntheticFacts = new List<TopicPlaygroundFactReport>();
        public bool objectsComplete;
        public List<TopicPlaygroundObjectReport> syntheticObjects = new List<TopicPlaygroundObjectReport>();
        public List<TopicPlaygroundFamilyMatchReport> familyMatches = new List<TopicPlaygroundFamilyMatchReport>();
        public List<TopicPlaygroundScenarioCandidateReport> candidates = new List<TopicPlaygroundScenarioCandidateReport>();
        public List<TopicPlaygroundLifecycleEventReport> lifecycle = new List<TopicPlaygroundLifecycleEventReport>();
        public List<string> previewTranscript = new List<string>();
        public string historyPersistence;
        public bool historyDirty;
        public bool pendingSave;
    }

    [Serializable]
    sealed class TopicPlaygroundFamilyMatchReport
    {
        public string topicId;
        public float score;
        public float bestSimilarity;
        public int supportCount;
        public string bestAnchor;
    }

    [Serializable]
    sealed class TopicPlaygroundFactReport
    {
        public string key;
        public bool isKnown;
        public bool value;
    }

    [Serializable]
    sealed class TopicPlaygroundObjectReport
    {
        public string id;
        public string kind;
        public List<TopicPlaygroundPropertyReport> flags = new List<TopicPlaygroundPropertyReport>();
        public List<TopicPlaygroundPropertyReport> numbers = new List<TopicPlaygroundPropertyReport>();
    }

    [Serializable]
    sealed class TopicPlaygroundPropertyReport
    {
        public string key;
        public bool isKnown;
        public bool flagValue;
        public double numberValue;
    }

    [Serializable]
    sealed class TopicPlaygroundScenarioCandidateReport
    {
        public string scenarioId;
        public int priority;
        public string reason;
        public bool hasGate;
        public TopicPlaygroundTopicConditionReport gate;
        public double effectiveWeight;
        public List<TopicPlaygroundModifierReport> modifiers = new List<TopicPlaygroundModifierReport>();
    }

    [Serializable]
    sealed class TopicPlaygroundTopicConditionReport
    {
        public string truth;
        public string reason;
        public string boundObjectId;
        public List<TopicPlaygroundTopicConditionReport> children = new List<TopicPlaygroundTopicConditionReport>();
    }

    [Serializable]
    sealed class TopicPlaygroundModifierReport
    {
        public string truth;
        public double authoredAmount;
        public double appliedAmount;
        public TopicPlaygroundTopicConditionReport condition;
    }

    [Serializable]
    sealed class TopicPlaygroundLifecycleEventReport
    {
        public string action;
        public string nodeId;
        public string status;
        public string timeUtc;
        public bool consumed;
        public bool requiresSave;
        public bool pendingSave;
        public string persistenceError;
    }

    [Serializable]
    sealed class TopicPlaygroundMatchReport
    {
        public string routeId;
        public float score;
        public float bestSimilarity;
        public int supportCount;
        public string bestAnchor;
    }

    [Serializable]
    sealed class TopicPlaygroundAttemptReport
    {
        public string routeId;
        public List<TopicPlaygroundCandidateReport> candidates = new List<TopicPlaygroundCandidateReport>();
    }

    [Serializable]
    sealed class TopicPlaygroundCandidateReport
    {
        public string path;
        public string status;
        public string reason;
        public string eligibilityStatus;
        public string eligibilityReason;
        public List<TopicPlaygroundConditionReport> conditions = new List<TopicPlaygroundConditionReport>();
    }

    [Serializable]
    sealed class TopicPlaygroundStatReport
    {
        public string statId;
        public bool isKnown;
        public float value;
    }

    [Serializable]
    sealed class TopicPlaygroundConditionReport
    {
        public string statId;
        public string operation;
        public string outcome;
        public bool hasActualValue;
        public float actualValue;
        public float threshold;
        public string reason;
    }

    [Serializable]
    sealed class ScenarioPlaygroundEventReport
    {
        public double nowSeconds;
        public string status;
        public string currentStepId;
        public bool hasSimulatedActivity;
        public List<string> effects = new List<string>();
    }

    [Serializable]
    sealed class ScenarioPlaygroundCliReport
    {
        public bool ok;
        public string scenarioId;
        public string runId;
        public int paidStageRequestCount;
        public bool hasSimulatedActivity;
        public List<ScenarioPlaygroundEventReport> events = new List<ScenarioPlaygroundEventReport>();
        public string error;
    }

    public static class TopicPlaygroundCommand
    {
        const string ResultMarker = "TOPIC_PLAYGROUND_JSON ";
        const string ScenarioResultMarker = "SCENARIO_PLAYGROUND_JSON ";
        static TopicPlaygroundScenario s_Scenario;
        static TopicPlaygroundContext s_Context;
        static GteSemanticQueryRunner s_Gte;
        static TopicPlaygroundSession s_Session;
        static TopicPlaygroundQueryResult s_QueryResult;
        static TopicPlaygroundFamilyQueryResult s_FamilyResult;
        static readonly List<TopicPlaygroundFamilyQueryResult> s_FamilyRuns = new List<TopicPlaygroundFamilyQueryResult>();
        static DateTimeOffset s_SelectionTimeUtc;
        static IEnumerator s_Routine;
        static string s_Error;

        public static void Run()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogError("TopicPlaygroundCommand.Run is for the documented batch-mode command.");
                return;
            }

            try
            {
                string scenarioPath = GetArgument("-topicScenario");
                if (string.IsNullOrWhiteSpace(scenarioPath) || !File.Exists(scenarioPath))
                    throw new FileNotFoundException("Pass -topicScenario with a readable scenario JSON path.", scenarioPath);

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                TopicPlaygroundScenario scenario = JsonUtility.FromJson<TopicPlaygroundScenario>(File.ReadAllText(scenarioPath));
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.query))
                    throw new InvalidDataException("Scenario requires non-empty query text.");
                string normalizedMode;
                if (!TopicPlaygroundScenario.TryNormalizeTopicMode(scenario.topicMode, out normalizedMode))
                    throw new InvalidDataException("topicMode must be generic-route, semantic-family, or direct-family.");
                scenario.topicMode = normalizedMode;
                List<string> contextErrors;
                if (!TopicPlaygroundContextBuilder.TryBuild(
                        scenario.localHour,
                        scenario.stats,
                        scenario.fixtureSet,
                        IsFamilyMode(scenario.topicMode) ? scenario.facts : null,
                        IsFamilyMode(scenario.topicMode) && scenario.objectsComplete,
                        IsFamilyMode(scenario.topicMode) ? scenario.objects : null,
                        out s_Context,
                        out contextErrors))
                    throw new InvalidDataException(string.Join(" ", contextErrors.ToArray()));
                if (IsFamilyMode(scenario.topicMode) &&
                    !TopicPlaygroundScenario.TryParseSelectionTimeUtc(scenario.selectionTimeUtc, out s_SelectionTimeUtc))
                    throw new InvalidDataException("selectionTimeUtc must be ISO-8601 with an explicit UTC offset.");
                s_Scenario = scenario;
                s_FamilyRuns.Clear();
                s_Routine = ExecuteScenarioSteps();
                EditorApplication.update += Advance;
            }
            catch (Exception exception)
            {
                Finish(new TopicPlaygroundCommandReport
                {
                    ok = false,
                    error = exception.ToString(),
                }, 2);
            }
        }

        public static void RunScenarioFixture()
        {
            var report = new ScenarioPlaygroundCliReport();
            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("RunScenarioFixture is for the documented batch-mode command.");
                string fixturePath = GetArgument("-scenarioFixture");
                if (string.IsNullOrWhiteSpace(fixturePath) || !File.Exists(fixturePath))
                    throw new FileNotFoundException("Pass -scenarioFixture with a readable Playground fixture path.", fixturePath);
                ScenarioPlaygroundFixture fixture = JsonUtility.FromJson<ScenarioPlaygroundFixture>(File.ReadAllText(fixturePath));
                if (fixture == null || fixture.definition == null || string.IsNullOrWhiteSpace(fixture.runId))
                    throw new InvalidDataException("Scenario Playground fixture requires definition and runId.");

                ScenarioRuntimeSnapshot context = ScenarioPlaygroundSession.CreateSyntheticContext(fixture.definition, fixture.runId, fixture.turnId);
                List<string> errors = new List<string>(ScenarioDefinitionValidator.Validate(fixture.definition, context));
                if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors.ToArray()));

                var session = new ScenarioPlaygroundSession(fixture.definition, fixture.runId, context);
                ScenarioPlaygroundReport last = session.InitialReport;
                report.scenarioId = fixture.definition.scenarioId;
                report.runId = fixture.runId;
                report.events.Add(CreateScenarioFixtureEventReport(session.InitialReport));
                foreach (ScenarioPlaygroundFixtureEvent item in fixture.events ?? new List<ScenarioPlaygroundFixtureEvent>())
                {
                    if (item == null || item.input == null) throw new InvalidDataException("Scenario Playground events require an input.");
                    if (string.IsNullOrWhiteSpace(item.input.runId)) item.input.runId = fixture.runId;
                    last = session.Apply(item.input, item.nowSeconds);
                    report.events.Add(CreateScenarioFixtureEventReport(last));
                }
                report.hasSimulatedActivity = last.hasSimulatedActivity;
                report.paidStageRequestCount = 0;
                report.ok = true;
            }
            catch (Exception exception)
            {
                report.ok = false;
                report.error = exception.ToString();
            }

            Debug.Log(ScenarioResultMarker + JsonUtility.ToJson(report, false));
            EditorApplication.Exit(report.ok ? 0 : 2);
        }

        static ScenarioPlaygroundEventReport CreateScenarioFixtureEventReport(ScenarioPlaygroundReport source)
        {
            var report = new ScenarioPlaygroundEventReport
            {
                nowSeconds = source.nowSeconds,
                status = source.status,
                currentStepId = source.currentStepId,
                hasSimulatedActivity = source.hasSimulatedActivity,
            };
            foreach (ScenarioEffect effect in source.effects)
                report.effects.Add(string.IsNullOrEmpty(effect.outcome) ? effect.type : effect.type + ":" + effect.outcome);
            return report;
        }

        static IEnumerator ExecuteScenarioSteps()
        {
            IEnumerator loading = GteSemanticQueryRunner.LoadFromResourcesSteps(
                runner => s_Gte = runner,
                stage => Debug.Log($"Topic Playground: {stage}"));
            try
            {
                while (loading.MoveNext())
                    yield return null;
            }
            finally
            {
                (loading as IDisposable)?.Dispose();
            }

            if (s_Gte == null)
                throw new InvalidOperationException("GTE initialization ended without a query runner.");

            s_Session = new TopicPlaygroundSession(s_Gte);
            s_Gte = null;
            if (!string.IsNullOrWhiteSpace(s_Scenario.scratchHistoryPath))
                s_Session.ConfigureTopicHistoryScratch(s_Scenario.scratchHistoryPath);

            if (IsFamilyMode(s_Scenario.topicMode))
            {
                IEnumerator query = QueryFamily(s_Scenario.instanceId, s_SelectionTimeUtc);
                try
                {
                    while (query.MoveNext())
                        yield return null;
                }
                finally { (query as IDisposable)?.Dispose(); }

                if (s_FamilyResult == null)
                    throw new InvalidOperationException("Topic family selection ended without a result.");

                if (s_Scenario.steps != null)
                {
                    foreach (TopicPlaygroundScenarioStep step in s_Scenario.steps)
                    {
                        IEnumerator action = s_Session.ExecuteFamilyScenarioStep(
                            s_FamilyResult,
                            step,
                            s_Scenario,
                            s_Context,
                            s_SelectionTimeUtc,
                            result => { s_FamilyResult = result; s_FamilyRuns.Add(result); },
                            stage => Debug.Log($"Topic Playground: {stage}"));
                        try
                        {
                            while (action.MoveNext())
                                yield return null;
                        }
                        finally { (action as IDisposable)?.Dispose(); }
                    }
                }

                TopicPlaygroundFamilyQueryResult lastRun = s_FamilyResult;
                string selectedScenarioId = lastRun.TopicRun == null ? string.Empty :
                    lastRun.TopicRun.Selection.SelectedScenario == null ? string.Empty : lastRun.TopicRun.Selection.SelectedScenario.scenarioId;
                bool expectedMatches = string.IsNullOrWhiteSpace(s_Scenario.expectedRouteId) ||
                    string.Equals(selectedScenarioId, s_Scenario.expectedRouteId, StringComparison.OrdinalIgnoreCase);
                s_Error = expectedMatches ? null : $"Expected scenario '{s_Scenario.expectedRouteId}', selected '{selectedScenarioId}'.";
            }
            else
            {
                IEnumerator query = s_Session.QuerySteps(
                    s_Scenario.query, s_Context, result => s_QueryResult = result,
                    stage => Debug.Log($"Topic Playground: {stage}"));
                try { while (query.MoveNext()) yield return null; }
                finally { (query as IDisposable)?.Dispose(); }
                if (s_QueryResult == null)
                    throw new InvalidOperationException("Topic routing ended without a dialogue result.");

                if (s_Scenario.steps != null)
                {
                    foreach (TopicPlaygroundScenarioStep step in s_Scenario.steps)
                    {
                        if (step == null) throw new InvalidDataException("Scenario steps cannot contain null entries.");
                        if (string.Equals(step.action, "choose", StringComparison.OrdinalIgnoreCase)) s_QueryResult.Choose(step.index);
                        else if (string.Equals(step.action, "continue", StringComparison.OrdinalIgnoreCase)) s_QueryResult.Continue();
                        else throw new InvalidDataException($"Unsupported authored-route action '{step.action}'. Use choose or continue.");
                    }
                }

                bool expectedMatches = string.IsNullOrWhiteSpace(s_Scenario.expectedRouteId) ||
                    string.Equals(s_QueryResult.SelectedRouteId, s_Scenario.expectedRouteId, StringComparison.OrdinalIgnoreCase);
                s_Error = expectedMatches ? null : $"Expected route '{s_Scenario.expectedRouteId}', selected '{s_QueryResult.SelectedRouteId}'.";
            }
        }

        static IEnumerator QueryFamily(string instanceId, DateTimeOffset selectionTimeUtc)
        {
            IEnumerator query = s_Session.QueryTopicFamilySteps(
                s_Scenario.query,
                s_Context,
                s_Scenario.topicFixtureSet,
                string.Equals(s_Scenario.topicMode, "semantic-family", StringComparison.Ordinal),
                s_Scenario.topicFamilyId,
                instanceId,
                s_Scenario.source,
                s_Scenario.replayPolicy,
                s_Scenario.draw,
                selectionTimeUtc,
                result => { s_FamilyResult = result; s_FamilyRuns.Add(result); },
                stage => Debug.Log($"Topic Playground: {stage}"));
            try { while (query.MoveNext()) yield return null; }
            finally { (query as IDisposable)?.Dispose(); }
        }

        static void Advance()
        {
            try
            {
                if (s_Routine != null && s_Routine.MoveNext())
                    return;
            }
            catch (Exception exception)
            {
                TopicPlaygroundCommandReport report = new TopicPlaygroundCommandReport
                {
                    ok = false,
                    error = exception.ToString(),
                };
                Finish(report, 2);
                return;
            }

            TopicPlaygroundCommandReport completed = s_Session != null && s_FamilyRuns.Count > 0
                ? CreateFamilyReport(s_Session, s_FamilyRuns)
                : s_Session != null && s_QueryResult != null
                    ? CreateReport(s_Session, s_QueryResult)
                    : new TopicPlaygroundCommandReport { ok = false, error = "Scenario ended before producing a dialogue result." };
            if (s_Error != null)
            {
                completed.ok = false;
                completed.error = s_Error;
            }
            Finish(completed, completed.ok ? 0 : 2);
        }

        static void Finish(TopicPlaygroundCommandReport report, int exitCode)
        {
            EditorApplication.update -= Advance;
            (s_Routine as IDisposable)?.Dispose();
            s_Routine = null;
            s_Session?.Dispose();
            s_Session = null;
            s_Gte?.Dispose();
            s_Gte = null;
            s_Scenario = null;
            s_Context = null;
            s_QueryResult = null;
            s_FamilyResult = null;
            s_FamilyRuns.Clear();
            s_Error = null;
            Debug.Log(ResultMarker + JsonUtility.ToJson(report, false));
            EditorApplication.Exit(exitCode);
        }

        static TopicPlaygroundCommandReport CreateReport(TopicPlaygroundSession session, TopicPlaygroundQueryResult result)
        {
            TopicPlaygroundModelFingerprintSnapshot fingerprint = session.ModelFingerprintSnapshot;
            var report = new TopicPlaygroundCommandReport
            {
                ok = true,
                query = result.Query,
                localHour = result.LocalHour,
                modelId = session.ModelId,
                modelFingerprint = fingerprint.Fingerprint,
                modelFingerprintStatus = fingerprint.DisplayValue,
                backend = session.Backend,
                indexFingerprint = session.IndexFingerprint,
                fixtureSet = result.FixtureSet,
                routeCount = session.RouteCount,
                anchorCount = session.AnchorCount,
                selectedRouteId = result.SelectedRouteId,
                selectedVariantPath = result.SelectedPath,
                queryDurationMs = result.QueryDurationMs,
                usedFallback = result.Decision.UsedFallback || !string.IsNullOrEmpty(result.FallbackReason),
                fallbackReason = result.FallbackReason,
                topicMode = TopicPlaygroundScenario.GenericRouteMode,
                semanticRoutingAttempted = true,
                routingModeLabel = "Legacy Generic route selection",
            };

            foreach (SyntheticStatInput input in result.SyntheticStats)
                report.syntheticStats.Add(new TopicPlaygroundStatReport
                {
                    statId = input.statId,
                    isKnown = input.isKnown,
                    value = input.value,
                });

            foreach (DialogueRouteMatch match in result.Matches)
            {
                report.rankedRoutes.Add(new TopicPlaygroundMatchReport
                {
                    routeId = match.RouteId,
                    score = match.Score,
                    bestSimilarity = match.BestSimilarity,
                    supportCount = match.SupportCount,
                    bestAnchor = match.BestAnchor,
                });
            }

            foreach (TopicPlaygroundRouteAttempt attempt in result.Attempts)
            {
                var attemptReport = new TopicPlaygroundAttemptReport { routeId = attempt.RouteId };
                foreach (AuthoredDialogueCandidateCheck candidate in attempt.Selection.Candidates)
                {
                    var candidateReport = new TopicPlaygroundCandidateReport
                    {
                        path = candidate.Path,
                        status = candidate.Status.ToString(),
                        reason = candidate.Reason,
                    };
                    if (candidate.Eligibility != null)
                    {
                        candidateReport.eligibilityStatus = candidate.Eligibility.Status.ToString();
                        candidateReport.eligibilityReason = candidate.Eligibility.Reason;
                        foreach (StatConditionEvaluation condition in candidate.Eligibility.Conditions)
                            candidateReport.conditions.Add(new TopicPlaygroundConditionReport
                            {
                                statId = condition.StatId,
                                operation = condition.Operation,
                                outcome = condition.Outcome.ToString(),
                                hasActualValue = condition.HasActualValue,
                                actualValue = condition.ActualValue,
                                threshold = condition.Threshold,
                                reason = condition.Reason,
                            });
                    }
                    attemptReport.candidates.Add(candidateReport);
                }
                report.routeAttempts.Add(attemptReport);
            }

            foreach (CanonicalDialogueNode node in result.Transcript)
            {
                string text = string.IsNullOrWhiteSpace(node.displayText) ? node.text : node.displayText;
                report.transcript.Add($"{node.speaker}: {text}");
            }

            return report;
        }

        static TopicPlaygroundCommandReport CreateFamilyReport(TopicPlaygroundSession session,
            List<TopicPlaygroundFamilyQueryResult> runs)
        {
            TopicPlaygroundFamilyQueryResult last = runs[runs.Count - 1];
            TopicScenario selected = last.TopicRun == null ? null : last.TopicRun.Selection.SelectedScenario;
            TopicPlaygroundModelFingerprintSnapshot fingerprint = session.ModelFingerprintSnapshot;
            var report = new TopicPlaygroundCommandReport
            {
                ok = true,
                query = last.Query,
                localHour = last.LocalHour,
                modelId = session.ModelId,
                modelFingerprint = fingerprint.Fingerprint,
                modelFingerprintStatus = fingerprint.DisplayValue,
                backend = session.Backend,
                indexFingerprint = session.IndexFingerprint,
                fixtureSet = last.TopicFixtureSet,
                routeCount = session.RouteCount,
                anchorCount = session.AnchorCount,
                selectedRouteId = selected == null ? string.Empty : selected.scenarioId,
                selectedVariantPath = string.Empty,
                queryDurationMs = last.QueryDurationMs,
                usedFallback = selected == null,
                fallbackReason = selected == null
                    ? last.TopicRun == null ? last.FamilySelectionReason : last.TopicRun.Selection.Reason
                    : string.Empty,
                topicMode = s_Scenario.topicMode,
                semanticRoutingAttempted = last.SemanticRoutingAttempted,
                routingModeLabel = last.RoutingModeLabel,
                familySelectionReason = last.FamilySelectionReason,
                scenarioSelectionReason = last.ScenarioSelectionReason,
                selectedFamilyId = last.SelectedFamilyId,
                selectedScenarioId = selected == null ? string.Empty : selected.scenarioId,
                hasWinningPriority = last.TopicRun != null && last.TopicRun.Selection.WinningPriority.HasValue,
                winningPriority = last.TopicRun != null && last.TopicRun.Selection.WinningPriority.HasValue
                    ? last.TopicRun.Selection.WinningPriority.Value : 0,
                draw = last.Draw,
                selectionTimeUtc = last.SelectionTimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                historyPersistence = last.PersistenceStatus,
                historyDirty = last.HistoryDirty,
                pendingSave = last.PendingSave,
            };

            foreach (SyntheticStatInput input in last.SyntheticStats)
                report.syntheticStats.Add(new TopicPlaygroundStatReport
                    { statId = input.statId, isKnown = input.isKnown, value = input.value });
            foreach (TopicPlaygroundFamilyQueryResult run in runs)
                report.familyRuns.Add(CreateFamilyRunReport(run));
            return report;
        }

        static TopicPlaygroundFamilyRunReport CreateFamilyRunReport(TopicPlaygroundFamilyQueryResult run)
        {
            TopicSelectionResult selection = run.TopicRun == null ? null : run.TopicRun.Selection;
            TopicScenario selected = selection == null ? null : selection.SelectedScenario;
            var report = new TopicPlaygroundFamilyRunReport
            {
                query = run.Query,
                topicFixtureSet = run.TopicFixtureSet,
                semanticRoutingAttempted = run.SemanticRoutingAttempted,
                routingModeLabel = run.RoutingModeLabel,
                familySelectionReason = run.FamilySelectionReason,
                scenarioSelectionReason = run.ScenarioSelectionReason,
                selectedFamilyId = run.SelectedFamilyId,
                instanceId = run.Instance == null ? string.Empty : run.Instance.instanceId,
                source = run.Instance == null ? string.Empty : run.Instance.source,
                replayPolicy = run.Instance == null ? string.Empty : run.Instance.replayPolicy,
                selectedScenarioId = selected == null ? string.Empty : selected.scenarioId,
                hasWinningPriority = selection != null && selection.WinningPriority.HasValue,
                winningPriority = selection != null && selection.WinningPriority.HasValue ? selection.WinningPriority.Value : 0,
                draw = run.Draw,
                selectionTimeUtc = run.SelectionTimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                localHour = run.LocalHour,
                objectsComplete = run.ObjectsComplete,
                historyPersistence = run.PersistenceStatus,
                historyDirty = run.HistoryDirty,
                pendingSave = run.PendingSave,
            };

            foreach (SyntheticStatInput input in run.SyntheticStats)
                report.syntheticStats.Add(new TopicPlaygroundStatReport
                    { statId = input.statId, isKnown = input.isKnown, value = input.value });
            foreach (SyntheticFactInput input in run.SyntheticFacts)
                report.syntheticFacts.Add(new TopicPlaygroundFactReport
                    { key = input.key, isKnown = input.isKnown, value = input.value });
            foreach (SyntheticObjectInput input in run.SyntheticObjects)
            {
                var objectReport = new TopicPlaygroundObjectReport { id = input.id, kind = input.kind };
                if (input.flags != null)
                    foreach (SyntheticObjectFlagInput flag in input.flags)
                        objectReport.flags.Add(flag == null ? null : new TopicPlaygroundPropertyReport
                            { key = flag.key, isKnown = flag.isKnown, flagValue = flag.value });
                if (input.numbers != null)
                    foreach (SyntheticObjectNumberInput number in input.numbers)
                        objectReport.numbers.Add(number == null ? null : new TopicPlaygroundPropertyReport
                            { key = number.key, isKnown = number.isKnown, numberValue = number.value });
                report.syntheticObjects.Add(objectReport);
            }
            foreach (TopicFamilySemanticMatch match in run.FamilyMatches)
                report.familyMatches.Add(new TopicPlaygroundFamilyMatchReport
                {
                    topicId = match.TopicId,
                    score = match.Score,
                    bestSimilarity = match.BestSimilarity,
                    supportCount = match.SupportCount,
                    bestAnchor = match.BestAnchor,
                });
            if (selection != null)
            {
                foreach (TopicCandidateTrace candidate in selection.Candidates)
                {
                    var candidateReport = new TopicPlaygroundScenarioCandidateReport
                    {
                        scenarioId = candidate.ScenarioId,
                        priority = candidate.Priority,
                        reason = candidate.Reason,
                        hasGate = candidate.Gate != null,
                        gate = candidate.Gate == null ? null : CreateConditionReport(candidate.Gate),
                        effectiveWeight = candidate.EffectiveWeight,
                    };
                    foreach (TopicWeightTrace modifier in candidate.Modifiers)
                        candidateReport.modifiers.Add(new TopicPlaygroundModifierReport
                        {
                            truth = modifier.Truth.ToString(),
                            authoredAmount = modifier.AuthoredAmount,
                            appliedAmount = modifier.AppliedAmount,
                            condition = CreateConditionReport(modifier.Condition),
                        });
                    report.candidates.Add(candidateReport);
                }
            }
            foreach (TopicPlaygroundLifecycleEvent lifecycle in run.LifecycleEvents)
                report.lifecycle.Add(new TopicPlaygroundLifecycleEventReport
                {
                    action = lifecycle.Action,
                    nodeId = lifecycle.NodeId,
                    status = lifecycle.Status,
                    timeUtc = lifecycle.TimeUtc,
                    consumed = lifecycle.IsConsumed,
                    requiresSave = lifecycle.RequiresSave,
                    pendingSave = lifecycle.PendingSave,
                    persistenceError = lifecycle.PersistenceError,
                });
            foreach (CanonicalDialogueNode node in run.Transcript)
            {
                string text = string.IsNullOrWhiteSpace(node.displayText) ? node.text : node.displayText;
                report.previewTranscript.Add($"{node.speaker}: {text}");
            }
            return report;
        }

        static TopicPlaygroundTopicConditionReport CreateConditionReport(TopicConditionResult condition)
        {
            if (condition == null)
                return null;
            var report = new TopicPlaygroundTopicConditionReport
            {
                truth = condition.Truth.ToString(),
                reason = condition.Reason,
                boundObjectId = condition.BoundObjectId ?? string.Empty,
            };
            foreach (TopicConditionResult child in condition.Children)
                report.children.Add(CreateConditionReport(child));
            return report;
        }

        static bool IsFamilyMode(string mode)
        {
            return string.Equals(mode, TopicPlaygroundScenario.SemanticFamilyMode, StringComparison.Ordinal) ||
                   string.Equals(mode, TopicPlaygroundScenario.DirectFamilyMode, StringComparison.Ordinal);
        }

        static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }

            return null;
        }
    }
}
