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
using UnityEngine;

namespace AlterOS.Editor
{
    public sealed class TopicPlaygroundWindow : EditorWindow
    {
        enum PlaygroundMode
        {
            AuthoredRoutes,
            SemanticTopicFamily,
            DirectTopicFamilyTest,
        }

        string m_Query = "ngl im kinda tired";
        PlaygroundMode m_Mode = PlaygroundMode.AuthoredRoutes;
        int m_LocalHour = 12;
        bool m_UseSyntheticLocalHour;
        bool m_UseSyntheticFixtures;
        List<SyntheticStatInput> m_SyntheticStats;
        List<SyntheticFactInput> m_SyntheticFacts = new List<SyntheticFactInput>();
        List<SyntheticObjectInput> m_SyntheticObjects = new List<SyntheticObjectInput>();
        bool m_ObjectsComplete;
        string m_TopicFixtureSet = "topic-runtime-v1";
        string m_TopicFamilyId = "beverage.drink";
        string m_TopicInstanceId = "playground-occurrence-1";
        string m_TopicSource = "authored";
        string m_TopicReplayOverride = string.Empty;
        double m_TopicDraw;
        string m_TopicSelectionTimeUtc = "2026-10-16T12:00:00Z";
        double m_TopicElapsedSeconds;
        int m_OccurrenceOrdinal;
        string m_TopicScratchHistoryPath = string.Empty;
        bool m_UseScratchHistory;
        List<TopicDefinition> m_TopicFamilies;
        string m_LoadedTopicFixtureSet;
        List<TopicPlaygroundScenarioStep> m_LoadedScenarioSteps;
        TopicPlaygroundScenario m_LoadedScenario;
        TopicPlaygroundContext m_LastBuiltContext;
        bool m_ExecuteLoadedScenarioSteps;
        int m_LoadedScenarioStepIndex;
        DateTimeOffset m_LoadedScenarioBaseTime;
        string m_LoadedExpectedRouteId;
        bool m_LoadedScenarioAssertionFailed;
        string m_Status = "Load the shipped GTE model and baked index to begin.";
        TopicPlaygroundSession m_Session;
        TopicPlaygroundQueryResult m_Result;
        TopicPlaygroundFamilyQueryResult m_FamilyResult;
        ScenarioPlaygroundSession m_ScenarioPreview;
        ScenarioPlaygroundReport m_ScenarioPreviewReport;
        double m_ScenarioPreviewTime;
        ScenarioPlaygroundFixture m_DetachedFixture;
        ScenarioPlaygroundSession m_DetachedFixtureSession;
        ScenarioPlaygroundReport m_DetachedFixtureReport;
        int m_DetachedFixtureEventIndex;
        IEnumerator m_LoadRoutine;
        IEnumerator m_QueryRoutine;
        string m_LastModelFingerprintDisplay;
        Vector2 m_Scroll;

        [MenuItem("AlterOS/Dialogue/Topic Playground")]
        static void Open()
        {
            GetWindow<TopicPlaygroundWindow>("Topic Playground");
        }

        void OnEnable()
        {
            TopicPlaygroundModelAssetPostprocessor.AssetChanged -= OnModelAssetChanged;
            TopicPlaygroundModelAssetPostprocessor.AssetChanged += OnModelAssetChanged;
        }

        void OnDisable()
        {
            TopicPlaygroundModelAssetPostprocessor.AssetChanged -= OnModelAssetChanged;
            EditorApplication.update -= AdvanceModelFingerprint;
            StopLoading();
            StopQuery();
            m_Session?.Dispose();
            m_Session = null;
        }

        void StartLoading()
        {
            m_Status = "Loading GTE resources...";
            m_LoadRoutine = GteSemanticQueryRunner.LoadFromResourcesSteps(
                runner => m_Session = new TopicPlaygroundSession(runner),
                stage => m_Status = stage);
            EditorApplication.update += AdvanceLoading;
        }

        void AdvanceLoading()
        {
            if (m_LoadRoutine == null)
                return;

            try
            {
                if (!m_LoadRoutine.MoveNext())
                {
                    EditorApplication.update -= AdvanceLoading;
                    m_LoadRoutine = null;
                    if (m_Session != null)
                    {
                        m_Status = $"Ready: {m_Session.RouteCount} routes, {m_Session.AnchorCount} anchors, {m_Session.Backend}.";
                        StartModelFingerprintMonitor();
                    }
                }
            }
            catch (Exception exception)
            {
                EditorApplication.update -= AdvanceLoading;
                (m_LoadRoutine as IDisposable)?.Dispose();
                m_LoadRoutine = null;
                m_Session?.Dispose();
                m_Session = null;
                m_Status = exception.Message;
            }

            Repaint();
        }

        void StopLoading()
        {
            EditorApplication.update -= AdvanceLoading;
            (m_LoadRoutine as IDisposable)?.Dispose();
            m_LoadRoutine = null;
        }

        void StartQuery()
        {
            TopicPlaygroundContext context;
            List<string> errors;
            if (!TryBuildCurrentContext(out context, out errors))
            {
                m_Result = null;
                m_FamilyResult = null;
                m_Status = string.Join(" ", errors.ToArray());
                return;
            }
            m_LastBuiltContext = context;

            if (m_UseScratchHistory && string.IsNullOrWhiteSpace(m_Session.TopicScratchHistoryPath))
            {
                try
                {
                    m_Session.ConfigureTopicHistoryScratch(m_TopicScratchHistoryPath);
                }
                catch (Exception exception)
                {
                    m_Result = null;
                    m_FamilyResult = null;
                    m_Status = exception.Message;
                    return;
                }
            }

            m_Result = null;
            m_FamilyResult = null;
            m_LoadedScenarioStepIndex = 0;
            m_LoadedScenarioAssertionFailed = false;
            m_Status = "Preparing query...";
            if (m_Mode == PlaygroundMode.AuthoredRoutes)
            {
                m_QueryRoutine = m_Session.QuerySteps(
                    m_Query,
                    context,
                    result => m_Result = result,
                    stage => m_Status = stage);
            }
            else
            {
                DateTimeOffset selectionTime;
                if (!TopicPlaygroundScenario.TryParseSelectionTimeUtc(m_TopicSelectionTimeUtc, out selectionTime))
                {
                    m_Status = "Selection time must be an ISO-8601 timestamp with an explicit UTC offset.";
                    return;
                }
                if (m_ExecuteLoadedScenarioSteps && m_LoadedScenario != null)
                {
                    m_LoadedScenario.query = m_Query;
                    m_LoadedScenario.topicMode = m_Mode == PlaygroundMode.SemanticTopicFamily ? "semantic-family" : "direct-family";
                    m_LoadedScenario.topicFixtureSet = m_TopicFixtureSet;
                    m_LoadedScenario.topicFamilyId = m_TopicFamilyId;
                    m_LoadedScenario.instanceId = m_TopicInstanceId;
                    m_LoadedScenario.source = m_TopicSource;
                    m_LoadedScenario.replayPolicy = m_TopicReplayOverride;
                    m_LoadedScenario.draw = m_TopicDraw;
                    m_LoadedScenario.selectionTimeUtc = m_TopicSelectionTimeUtc;
                    m_LoadedScenarioBaseTime = selectionTime;
                }
                m_QueryRoutine = m_Session.QueryTopicFamilySteps(
                    m_Query,
                    context,
                    m_TopicFixtureSet,
                    m_Mode == PlaygroundMode.SemanticTopicFamily,
                    m_TopicFamilyId,
                    m_TopicInstanceId,
                    m_TopicSource,
                    m_TopicReplayOverride,
                    m_TopicDraw,
                    selectionTime,
                    result =>
                    {
                        m_FamilyResult = result;
                    },
                    stage => m_Status = stage);
                m_LoadedScenarioBaseTime = selectionTime;
            }
            EditorApplication.update += AdvanceQuery;
        }

        bool TryBuildCurrentContext(out TopicPlaygroundContext context, out List<string> errors)
        {
            bool familyMode = m_Mode != PlaygroundMode.AuthoredRoutes;
            return TopicPlaygroundContextBuilder.TryBuild(
                m_UseSyntheticLocalHour ? m_LocalHour : -1,
                m_SyntheticStats,
                m_UseSyntheticFixtures ? "stat-context-v1" : string.Empty,
                familyMode ? m_SyntheticFacts : null,
                familyMode && m_ObjectsComplete,
                familyMode ? m_SyntheticObjects : null,
                out context,
                out errors);
        }

        void AdvanceQuery()
        {
            if (m_QueryRoutine == null)
                return;

            try
            {
                if (!m_QueryRoutine.MoveNext())
                {
                    EditorApplication.update -= AdvanceQuery;
                    m_QueryRoutine = null;
                    if (m_Result != null)
                    {
                        if (m_ExecuteLoadedScenarioSteps)
                            ExecuteLoadedRouteSteps();
                        if (!m_LoadedScenarioAssertionFailed)
                            m_Status = $"Selected {m_Result.SelectedRouteId} / {m_Result.SelectedPath} ({m_Result.QueryDurationMs:F1} ms total)";
                    }
                    else if (m_FamilyResult != null)
                    {
                        if (m_ExecuteLoadedScenarioSteps)
                        {
                            m_QueryRoutine = ExecuteLoadedFamilySteps();
                            m_Status = "Executing loaded scenario steps through the shared Topic session...";
                            EditorApplication.update += AdvanceQuery;
                            Repaint();
                            return;
                        }
                        if (!m_LoadedScenarioAssertionFailed)
                            m_Status = string.IsNullOrEmpty(m_FamilyResult.SelectedFamilyId)
                                ? m_FamilyResult.FamilySelectionReason
                                : $"Selected family {m_FamilyResult.SelectedFamilyId} / {m_FamilyResult.TopicRun?.Selection.SelectedScenario?.scenarioId ?? "no scenario"}";
                    }
                }
            }
            catch (Exception exception)
            {
                EditorApplication.update -= AdvanceQuery;
                (m_QueryRoutine as IDisposable)?.Dispose();
                m_QueryRoutine = null;
                m_Result = null;
                m_FamilyResult = null;
                m_Status = exception.Message;
            }

            Repaint();
        }

        void StopQuery()
        {
            EditorApplication.update -= AdvanceQuery;
            (m_QueryRoutine as IDisposable)?.Dispose();
            m_QueryRoutine = null;
        }

        void LoadScenarioIntoWindow()
        {
            string path = EditorUtility.OpenFilePanel("Load Topic Playground scenario", Path.Combine(Application.dataPath, "..", "Tools", "TopicPlayground"), "json");
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                TopicPlaygroundScenario scenario = JsonUtility.FromJson<TopicPlaygroundScenario>(File.ReadAllText(path));
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.query))
                    throw new InvalidDataException("Scenario requires non-empty query text.");
                string normalizedMode;
                if (!TopicPlaygroundScenario.TryNormalizeTopicMode(scenario.topicMode, out normalizedMode))
                    throw new InvalidDataException("topicMode must be generic-route, semantic-family, or direct-family.");

                m_LoadedScenario = scenario;
                m_Query = scenario.query;
                m_LocalHour = scenario.localHour;
                m_UseSyntheticLocalHour = scenario.localHour >= 0;
                m_UseSyntheticFixtures = scenario.fixtureSet == "stat-context-v1";
                m_SyntheticStats = scenario.stats ?? new List<SyntheticStatInput>();
                m_SyntheticFacts = scenario.facts ?? new List<SyntheticFactInput>();
                m_SyntheticObjects = scenario.objects ?? new List<SyntheticObjectInput>();
                m_ObjectsComplete = scenario.objectsComplete;
                m_TopicFixtureSet = string.IsNullOrWhiteSpace(scenario.topicFixtureSet) ? "topic-runtime-v1" : scenario.topicFixtureSet;
                m_TopicFamilyId = scenario.topicFamilyId ?? string.Empty;
                m_TopicInstanceId = scenario.instanceId ?? string.Empty;
                m_TopicSource = scenario.source == "generated" ? "generated" : "authored";
                m_TopicReplayOverride = scenario.replayPolicy ?? string.Empty;
                m_TopicDraw = scenario.draw;
                m_TopicSelectionTimeUtc = scenario.selectionTimeUtc ?? string.Empty;
                m_TopicScratchHistoryPath = scenario.scratchHistoryPath ?? string.Empty;
                m_UseScratchHistory = !string.IsNullOrWhiteSpace(m_TopicScratchHistoryPath);
                m_Mode = normalizedMode == TopicPlaygroundScenario.SemanticFamilyMode ? PlaygroundMode.SemanticTopicFamily
                    : normalizedMode == TopicPlaygroundScenario.DirectFamilyMode ? PlaygroundMode.DirectTopicFamilyTest
                    : PlaygroundMode.AuthoredRoutes;
                m_LoadedScenarioSteps = scenario.steps ?? new List<TopicPlaygroundScenarioStep>();
                m_LoadedExpectedRouteId = scenario.expectedRouteId ?? string.Empty;
                m_ExecuteLoadedScenarioSteps = m_LoadedScenarioSteps.Count > 0;
                m_LoadedScenarioStepIndex = 0;
                m_LoadedScenarioAssertionFailed = false;
                if (!TopicPlaygroundScenario.TryParseSelectionTimeUtc(m_TopicSelectionTimeUtc, out m_LoadedScenarioBaseTime))
                    m_LoadedScenarioBaseTime = default(DateTimeOffset);
                m_LoadedTopicFixtureSet = null;
                m_Status = "Loaded scenario JSON into synthetic controls. Direct-family scenarios are not semantic-routing proof.";
            }
            catch (Exception exception)
            {
                m_Status = "Scenario load failed: " + exception.Message;
            }
        }

        IEnumerator ExecuteLoadedFamilySteps()
        {
            if (m_LoadedScenarioSteps == null)
                yield break;

            while (m_LoadedScenarioStepIndex < m_LoadedScenarioSteps.Count)
            {
                TopicPlaygroundScenarioStep step = m_LoadedScenarioSteps[m_LoadedScenarioStepIndex++];
                IEnumerator action = m_Session.ExecuteFamilyScenarioStep(
                    m_FamilyResult,
                    step,
                    m_LoadedScenario,
                    m_LastBuiltContext,
                    m_LoadedScenarioBaseTime,
                    result => m_FamilyResult = result,
                    stage => m_Status = stage);
                try
                {
                    while (action.MoveNext())
                        yield return null;
                }
                finally { (action as IDisposable)?.Dispose(); }
            }

            m_ExecuteLoadedScenarioSteps = false;
            m_LoadedScenarioAssertionFailed = !string.IsNullOrWhiteSpace(m_LoadedExpectedRouteId) &&
                !string.Equals(m_FamilyResult?.TopicRun?.Selection.SelectedScenario?.scenarioId,
                    m_LoadedExpectedRouteId, StringComparison.Ordinal);
            if (m_LoadedScenarioAssertionFailed)
                m_Status = "Scenario assertion failed: expected '" + m_LoadedExpectedRouteId + "', selected '" +
                    (m_FamilyResult?.TopicRun?.Selection.SelectedScenario?.scenarioId ?? "<none>") + "'.";
        }

        void ExecuteLoadedRouteSteps()
        {
            if (!m_ExecuteLoadedScenarioSteps || m_LoadedScenarioSteps == null || m_Result == null)
                return;
            foreach (TopicPlaygroundScenarioStep step in m_LoadedScenarioSteps)
            {
                if (step == null) throw new InvalidDataException("Scenario steps cannot contain null entries.");
                if (string.Equals(step.action, "choose", StringComparison.OrdinalIgnoreCase)) m_Result.Choose(step.index);
                else if (string.Equals(step.action, "continue", StringComparison.OrdinalIgnoreCase)) m_Result.Continue();
                else throw new InvalidDataException("Authored route scenarios support only choose and continue.");
            }
            m_ExecuteLoadedScenarioSteps = false;
            m_LoadedScenarioAssertionFailed = !string.IsNullOrWhiteSpace(m_LoadedExpectedRouteId) &&
                !string.Equals(m_Result.SelectedRouteId, m_LoadedExpectedRouteId, StringComparison.OrdinalIgnoreCase);
            if (m_LoadedScenarioAssertionFailed)
                m_Status = "Scenario assertion failed: expected route '" + m_LoadedExpectedRouteId + "'.";
        }

        void OnGUI()
        {
            EnsureSyntheticStats();
            EditorGUILayout.LabelField("Authored Topic Playground", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Legacy Generic JSON / synthetic fixture preview only. Published SQL Topic/Scenario sentence acceptance uses Tools/TopicPlayground/topic-playground.ps1 -Query (PowerShell 7). This window is not proof of compiled Scenario routing.",
                MessageType.Warning);
            EditorGUILayout.HelpBox(
                "All context below is synthetic and detached from progression, saves, the system clock, mood, activities, and player signals. Emotional axes are inputs only. The Playground never updates them.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scenario fixture runner", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("Load Scenario fixture", GUILayout.Width(180)))
                LoadDetachedScenarioFixture();
            EditorGUILayout.EndHorizontal();
            if (m_DetachedFixture != null)
                DrawDetachedScenarioFixture();

            if (m_Session == null)
            {
                using (new EditorGUI.DisabledScope(m_LoadRoutine != null))
                {
                    if (GUILayout.Button(m_LoadRoutine == null ? "Load GTE and dialogue catalogue" : "Loading GTE..."))
                    {
                        StartLoading();
                    }
                }

                EditorGUILayout.HelpBox(m_Status, m_Session == null ? MessageType.Warning : MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Model: {m_Session.ModelId}", EditorStyles.miniLabel);
            TopicPlaygroundModelFingerprintSnapshot fingerprint = m_Session.ModelFingerprintSnapshot;
            EditorGUILayout.LabelField($"Model file SHA-256: {fingerprint.DisplayValue}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Index SHA-256: {m_Session.IndexFingerprint}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(m_Status, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(m_QueryRoutine != null))
            {
                m_Query = EditorGUILayout.TextField("Player text", m_Query);
                m_Mode = (PlaygroundMode)EditorGUILayout.EnumPopup("Routing mode", m_Mode);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Batch scenario parity", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Load scenario JSON into window", GUILayout.Width(220)))
                    LoadScenarioIntoWindow();
                EditorGUILayout.EndHorizontal();
                if (m_Mode != PlaygroundMode.AuthoredRoutes)
                    DrawTopicModeControls();
                if (m_LoadedScenarioSteps != null && m_LoadedScenarioSteps.Count > 0)
                    m_ExecuteLoadedScenarioSteps = EditorGUILayout.Toggle("Execute loaded scenario steps", m_ExecuteLoadedScenarioSteps);
                DrawContextControls();
                bool validTopicDraw = m_Mode == PlaygroundMode.AuthoredRoutes ||
                    (!double.IsNaN(m_TopicDraw) && !double.IsInfinity(m_TopicDraw) && m_TopicDraw >= 0d && m_TopicDraw < 1d);
                if (m_Mode != PlaygroundMode.AuthoredRoutes && !validTopicDraw)
                    EditorGUILayout.HelpBox("Scenario draw must be finite and in [0, 1). The supplied value is not changed or clamped.", MessageType.Error);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(m_Query) || !validTopicDraw))
                {
                    string button = m_QueryRoutine == null
                        ? m_Mode == PlaygroundMode.AuthoredRoutes ? "Route authored dialogue" : "Route family and select scenario"
                        : "Routing...";
                    if (GUILayout.Button(button))
                        StartQuery();
                }
            }

            if (m_Result == null && m_FamilyResult == null)
                return;

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_FamilyResult != null)
                DrawFamilyResult();
            else
            {
                DrawContextEvidence();
                DrawMatches();
                DrawAttempts();
                DrawTranscript();
            }
            EditorGUILayout.EndScrollView();
        }

        void LoadDetachedScenarioFixture()
        {
            string fixtureRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/ScenarioRuntime/Fixtures/Playground"));
            string path = EditorUtility.OpenFilePanel("Load synthetic Scenario fixture", fixtureRoot, "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Scenario fixture must be loaded from Tools/ScenarioRuntime/Fixtures/Playground.");
                ScenarioPlaygroundFixture fixture = JsonUtility.FromJson<ScenarioPlaygroundFixture>(File.ReadAllText(fullPath));
                if (fixture == null || fixture.definition == null || string.IsNullOrWhiteSpace(fixture.runId))
                    throw new InvalidDataException("Fixture requires a Scenario definition and run ID.");
                ScenarioRuntimeSnapshot context = ScenarioPlaygroundSession.CreateSyntheticContext(fixture.definition, fixture.runId, fixture.turnId);
                List<string> errors = new List<string>(ScenarioDefinitionValidator.Validate(fixture.definition, context));
                if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors.ToArray()));

                m_DetachedFixture = fixture;
                m_DetachedFixtureSession = new ScenarioPlaygroundSession(fixture.definition, fixture.runId, context);
                m_DetachedFixtureReport = m_DetachedFixtureSession.InitialReport;
                m_DetachedFixtureEventIndex = 0;
                m_Status = "Loaded synthetic Scenario fixture; it does not call providers or production activities.";
            }
            catch (Exception exception)
            {
                m_Status = "Scenario fixture load failed: " + exception.Message;
            }
        }

        void DrawDetachedScenarioFixture()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Standalone Scenario fixture: " + m_DetachedFixture.definition.scenarioId, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Same fixture/event contract as the batch Scenario Playground. Activity outcomes are synthetic; no provider or real activity is called.", MessageType.Info);
            if (m_DetachedFixtureReport != null)
            {
                EditorGUILayout.LabelField("State", m_DetachedFixtureReport.status + " / " + m_DetachedFixtureReport.currentStepId);
                EditorGUILayout.LabelField("Provider requests", m_DetachedFixtureReport.paidStageRequestCount.ToString(CultureInfo.InvariantCulture));
                if (m_DetachedFixtureReport.hasSimulatedActivity)
                    EditorGUILayout.HelpBox("This timeline contains an explicitly simulated activity result.", MessageType.Warning);
                foreach (ScenarioEffect effect in m_DetachedFixtureReport.effects)
                    EditorGUILayout.LabelField(effect.type, effect.outcome);
            }
            if (m_DetachedFixture.events != null && m_DetachedFixtureEventIndex < m_DetachedFixture.events.Count)
            {
                ScenarioPlaygroundFixtureEvent next = m_DetachedFixture.events[m_DetachedFixtureEventIndex];
                using (new EditorGUI.DisabledScope(next == null || next.input == null))
                {
                    string eventName = next == null || next.input == null ? "Invalid event" : next.input.kind;
                    if (GUILayout.Button("Apply fixture event " + (m_DetachedFixtureEventIndex + 1) + ": " + eventName))
                    {
                        try
                        {
                            m_DetachedFixtureReport = m_DetachedFixtureSession.Apply(next.input, next.nowSeconds);
                            m_DetachedFixtureEventIndex++;
                        }
                        catch (Exception exception)
                        {
                            m_Status = "Fixture event rejected: " + exception.Message;
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Timeline", "Complete");
            }
        }

        void EnsureSyntheticStats()
        {
            if (m_SyntheticStats == null)
                m_SyntheticStats = new List<SyntheticStatInput>();

            var previous = new Dictionary<string, SyntheticStatInput>(StringComparer.Ordinal);
            foreach (SyntheticStatInput input in m_SyntheticStats)
                if (input != null && !string.IsNullOrEmpty(input.statId))
                    previous[input.statId] = input;

            m_SyntheticStats.Clear();
            foreach (StatDefinition definition in StatCatalogue.Definitions)
            {
                SyntheticStatInput input;
                if (!previous.TryGetValue(definition.Id, out input))
                    input = new SyntheticStatInput { statId = definition.Id };
                m_SyntheticStats.Add(input);
            }
        }

        void DrawContextControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Synthetic context", EditorStyles.boldLabel);
            m_UseSyntheticLocalHour = EditorGUILayout.Toggle("Provide local hour", m_UseSyntheticLocalHour);
            if (m_UseSyntheticLocalHour)
                m_LocalHour = EditorGUILayout.IntSlider("Synthetic local hour", m_LocalHour, 0, 23);
            else
                EditorGUILayout.LabelField("Synthetic local hour", m_Mode == PlaygroundMode.AuthoredRoutes
                    ? "Unknown (legacy route time windows are not restricted)"
                    : "Unknown (Topic time-window gates evaluate Unknown and do not qualify)");

            m_UseSyntheticFixtures = EditorGUILayout.Toggle("Use synthetic dialogue fixtures", m_UseSyntheticFixtures);
            if (m_UseSyntheticFixtures)
                EditorGUILayout.HelpBox("stat-context-v1 fixtures replace authored variants only for routes covered by the fixture set. Other routes, including fallback, use the authored catalogue.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset all to unknown"))
                ResetSyntheticContext();
            if (GUILayout.Button("Synthetic preset: baseline"))
                ApplyBaselinePreset();
            if (GUILayout.Button("Synthetic preset: low hydration"))
                ApplyLowHydrationPreset();
            if (GUILayout.Button("Synthetic preset: mixed emotion"))
                ApplyMixedEmotionPreset();
            EditorGUILayout.EndHorizontal();
            if (m_Mode != PlaygroundMode.AuthoredRoutes && GUILayout.Button("Synthetic Topic preset: low hydration + matching dispenser"))
                ApplyTopicWaterBreakPreset();

            bool drawingEmotion = false;
            foreach (StatDefinition definition in StatCatalogue.Definitions)
            {
                bool isEmotionAxis = definition.Id.StartsWith("alterra.emotion.", StringComparison.Ordinal);
                if (isEmotionAxis && !drawingEmotion)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Emotional axes (synthetic inputs only)", EditorStyles.miniBoldLabel);
                    drawingEmotion = true;
                }
                else if (!isEmotionAxis && !drawingEmotion && definition == StatCatalogue.Definitions[0])
                {
                    EditorGUILayout.LabelField("Needs (synthetic inputs only)", EditorStyles.miniBoldLabel);
                }

                SyntheticStatInput input = FindInput(definition.Id);
                EditorGUILayout.BeginHorizontal();
                input.isKnown = EditorGUILayout.ToggleLeft(definition.Id, input.isKnown, GUILayout.Width(250));
                if (input.isKnown)
                    input.value = EditorGUILayout.Slider(input.value, definition.Minimum, definition.Maximum);
                else
                    EditorGUILayout.LabelField("Unknown (not zero)", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            if (m_Mode != PlaygroundMode.AuthoredRoutes)
            {
                DrawSyntheticFacts();
                DrawSyntheticObjects();
            }
        }

        void DrawTopicModeControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New Topic family runtime", EditorStyles.boldLabel);
            m_TopicFixtureSet = EditorGUILayout.TextField("Synthetic Topic fixture set", m_TopicFixtureSet);
            EnsureTopicFamilies();

            if (m_Mode == PlaygroundMode.SemanticTopicFamily)
            {
                EditorGUILayout.HelpBox(
                    "Stage 1 embeds the query and each shared family anchor, then applies cosine similarity and the 0.45 minimum. Stage 2 uses the shared TopicScenarioSelector with synthetic context, history, priority, modifiers, weights, and the supplied draw.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Direct family selection bypasses semantic routing and is only for isolated scenario/context tests. It is not evidence that natural language selects this family.",
                    MessageType.Warning);
                if (m_TopicFamilies != null && m_TopicFamilies.Count > 0)
                {
                    int selectedIndex = Mathf.Max(0, m_TopicFamilies.FindIndex(item => item.topicId == m_TopicFamilyId));
                    string[] ids = m_TopicFamilies.ConvertAll(item => item.topicId).ToArray();
                    selectedIndex = EditorGUILayout.Popup("Direct test family", selectedIndex, ids);
                    m_TopicFamilyId = ids[selectedIndex];
                }
            }

            m_TopicInstanceId = EditorGUILayout.TextField("Occurrence instance ID", m_TopicInstanceId);
            m_TopicSource = EditorGUILayout.Popup("Instance source", m_TopicSource == "generated" ? 1 : 0,
                new[] { "authored", "generated" }) == 1 ? "generated" : "authored";
            m_TopicReplayOverride = EditorGUILayout.TextField("Replay override (optional)", m_TopicReplayOverride);
            m_TopicDraw = EditorGUILayout.DoubleField("Deterministic scenario draw (0 inclusive, 1 exclusive)", m_TopicDraw);
            m_TopicSelectionTimeUtc = EditorGUILayout.TextField("Synthetic selection time (ISO-8601 UTC)", m_TopicSelectionTimeUtc);

            if (!string.IsNullOrEmpty(m_Session.TopicScratchHistoryPath))
            {
                EditorGUILayout.HelpBox(m_Session.TopicScratchHistoryPath + "\n" + m_Session.TopicPersistenceStatus,
                    m_Session.TopicHistoryDirty ? MessageType.Warning : MessageType.Info);
            }
            else
            {
                m_UseScratchHistory = EditorGUILayout.Toggle("Persist to explicit temp scratch file", m_UseScratchHistory);
                using (new EditorGUI.DisabledScope(!m_UseScratchHistory))
                {
                    EditorGUILayout.BeginHorizontal();
                    m_TopicScratchHistoryPath = EditorGUILayout.TextField("Scratch history path", m_TopicScratchHistoryPath);
                    if (GUILayout.Button("Browse", GUILayout.Width(70)))
                    {
                        string scratchRoot = Path.Combine(Path.GetTempPath(), "AlterOS-TopicPlayground");
                        Directory.CreateDirectory(scratchRoot);
                        string chosen = EditorUtility.SaveFilePanel("Choose Playground scratch history", scratchRoot, "history.json", "json");
                        if (!string.IsNullOrEmpty(chosen))
                            m_TopicScratchHistoryPath = chosen;
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox("The session rejects paths outside this dedicated OS temp folder. No player save path is read or written.", MessageType.None);
                }
            }
        }

        void EnsureTopicFamilies()
        {
            if (m_Session == null || string.Equals(m_LoadedTopicFixtureSet, m_TopicFixtureSet, StringComparison.Ordinal))
                return;
            try
            {
                m_TopicFamilies = m_Session.LoadTopicFamilies(m_TopicFixtureSet);
                m_LoadedTopicFixtureSet = m_TopicFixtureSet;
                if (m_TopicFamilies.Count > 0 && !m_TopicFamilies.Exists(item => item.topicId == m_TopicFamilyId))
                    m_TopicFamilyId = m_TopicFamilies[0].topicId;
            }
            catch (Exception exception)
            {
                m_TopicFamilies = new List<TopicDefinition>();
                m_Status = exception.Message;
            }
        }

        void DrawSyntheticFacts()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Explicit synthetic facts (not player evidence)", EditorStyles.miniBoldLabel);
            for (int index = 0; index < m_SyntheticFacts.Count; index++)
            {
                SyntheticFactInput fact = m_SyntheticFacts[index];
                if (fact == null)
                    continue;
                EditorGUILayout.BeginHorizontal();
                fact.key = EditorGUILayout.TextField(fact.key, GUILayout.MinWidth(140));
                fact.isKnown = EditorGUILayout.ToggleLeft("Known", fact.isKnown, GUILayout.Width(65));
                if (fact.isKnown)
                    fact.value = EditorGUILayout.ToggleLeft("True", fact.value, GUILayout.Width(65));
                else
                    EditorGUILayout.LabelField("Unknown (not false)", EditorStyles.miniLabel);
                if (GUILayout.Button("−", GUILayout.Width(24)))
                {
                    m_SyntheticFacts.RemoveAt(index);
                    index--;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add synthetic fact", GUILayout.Width(150)))
                m_SyntheticFacts.Add(new SyntheticFactInput());
        }

        void DrawSyntheticObjects()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Synthetic object snapshots", EditorStyles.miniBoldLabel);
            m_ObjectsComplete = EditorGUILayout.Toggle("Inventory snapshot is complete", m_ObjectsComplete);
            EditorGUILayout.HelpBox(
                m_ObjectsComplete ? "No matching object means false; missing properties on listed objects remain unknown." : "No matching object may mean unknown because the inventory snapshot is incomplete.",
                MessageType.None);

            for (int objectIndex = 0; objectIndex < m_SyntheticObjects.Count; objectIndex++)
            {
                SyntheticObjectInput item = m_SyntheticObjects[objectIndex];
                if (item == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                item.id = EditorGUILayout.TextField("Object ID", item.id);
                item.kind = EditorGUILayout.TextField("Kind", item.kind);
                if (GUILayout.Button("Remove", GUILayout.Width(64)))
                {
                    m_SyntheticObjects.RemoveAt(objectIndex);
                    objectIndex--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }
                EditorGUILayout.EndHorizontal();

                if (item.flags == null) item.flags = new List<SyntheticObjectFlagInput>();
                for (int index = 0; index < item.flags.Count; index++)
                {
                    SyntheticObjectFlagInput flag = item.flags[index];
                    if (flag == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Flag", GUILayout.Width(38));
                    flag.key = EditorGUILayout.TextField(flag.key);
                    flag.isKnown = EditorGUILayout.ToggleLeft("Known", flag.isKnown, GUILayout.Width(65));
                    if (flag.isKnown) flag.value = EditorGUILayout.ToggleLeft("True", flag.value, GUILayout.Width(65));
                    else EditorGUILayout.LabelField("Unknown", EditorStyles.miniLabel);
                    if (GUILayout.Button("−", GUILayout.Width(24))) { item.flags.RemoveAt(index); index--; }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add flag", GUILayout.Width(100))) item.flags.Add(new SyntheticObjectFlagInput());

                if (item.numbers == null) item.numbers = new List<SyntheticObjectNumberInput>();
                for (int index = 0; index < item.numbers.Count; index++)
                {
                    SyntheticObjectNumberInput number = item.numbers[index];
                    if (number == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Number", GUILayout.Width(50));
                    number.key = EditorGUILayout.TextField(number.key);
                    number.isKnown = EditorGUILayout.ToggleLeft("Known", number.isKnown, GUILayout.Width(65));
                    if (number.isKnown) number.value = EditorGUILayout.DoubleField(number.value);
                    else EditorGUILayout.LabelField("Unknown (not zero)", EditorStyles.miniLabel);
                    if (GUILayout.Button("−", GUILayout.Width(24))) { item.numbers.RemoveAt(index); index--; }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add number", GUILayout.Width(100))) item.numbers.Add(new SyntheticObjectNumberInput());
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add synthetic object", GUILayout.Width(160)))
                m_SyntheticObjects.Add(new SyntheticObjectInput());
        }

        void DrawContextEvidence()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Context used for this selection", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Synthetic local hour", m_Result.LocalHour < 0 ? "Unknown" : m_Result.LocalHour.ToString());
            EditorGUILayout.LabelField("Dialogue source", string.IsNullOrEmpty(m_Result.FixtureSet) ? "Authored catalogue" : "SYNTHETIC FIXTURE " + m_Result.FixtureSet);
            foreach (SyntheticStatInput input in m_Result.SyntheticStats)
                EditorGUILayout.LabelField(input.statId, input.isKnown ? input.value.ToString("G9") : "Unknown (not zero)");
        }

        SyntheticStatInput FindInput(string statId)
        {
            foreach (SyntheticStatInput input in m_SyntheticStats)
                if (string.Equals(input.statId, statId, StringComparison.Ordinal))
                    return input;
            throw new InvalidOperationException("StatCatalogue entry has no Playground control: " + statId);
        }

        void ResetSyntheticContext()
        {
            foreach (SyntheticStatInput input in m_SyntheticStats)
            {
                input.isKnown = false;
                input.value = 0f;
            }
            m_UseSyntheticLocalHour = false;
            m_LocalHour = 12;
            m_UseSyntheticFixtures = false;
            m_SyntheticFacts.Clear();
            m_SyntheticObjects.Clear();
            m_ObjectsComplete = false;
        }

        void ApplyBaselinePreset()
        {
            foreach (StatDefinition definition in StatCatalogue.Definitions)
            {
                SyntheticStatInput input = FindInput(definition.Id);
                input.isKnown = true;
                input.value = definition.Id.StartsWith("alterra.emotion.", StringComparison.Ordinal)
                    ? Mathf.Clamp(0f, definition.Minimum, definition.Maximum)
                    : (definition.Minimum + definition.Maximum) * 0.5f;
            }
        }

        void ApplyLowHydrationPreset()
        {
            ResetSyntheticValuesOnly();
            SyntheticStatInput hydration = FindInput("alterra.hydration");
            hydration.isKnown = true;
            hydration.value = 20f;
            m_UseSyntheticFixtures = true;
        }

        void ApplyMixedEmotionPreset()
        {
            ResetSyntheticValuesOnly();
            SyntheticStatInput valence = FindInput("alterra.emotion.valence");
            SyntheticStatInput control = FindInput("alterra.emotion.control");
            valence.isKnown = true;
            valence.value = -0.75f;
            control.isKnown = true;
            control.value = -0.5f;
            m_UseSyntheticFixtures = true;
        }

        void ApplyTopicWaterBreakPreset()
        {
            ResetSyntheticValuesOnly();
            m_SyntheticFacts.Clear();
            m_SyntheticObjects.Clear();
            SyntheticStatInput hydration = FindInput("alterra.hydration");
            hydration.isKnown = true;
            hydration.value = 20f;
            m_ObjectsComplete = true;
            var dispenser = new SyntheticObjectInput { id = "synthetic-dispenser-a", kind = "dispenser" };
            dispenser.flags.Add(new SyntheticObjectFlagInput { key = "drinkable", isKnown = true, value = true });
            dispenser.numbers.Add(new SyntheticObjectNumberInput { key = "water_level", isKnown = true, value = 2d });
            m_SyntheticObjects.Add(dispenser);
            m_SyntheticFacts.Add(new SyntheticFactInput { key = "user_confirmed.has_bottle", isKnown = true, value = true });
        }

        void ResetSyntheticValuesOnly()
        {
            foreach (SyntheticStatInput input in m_SyntheticStats)
            {
                input.isKnown = false;
                input.value = 0f;
            }
        }

        void DrawMatches()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ranked routes", EditorStyles.boldLabel);
            if (m_Result.Decision.UsedFallback)
                EditorGUILayout.HelpBox($"Fallback: {m_Result.Decision.Reason}", MessageType.Warning);
            int count = Mathf.Min(10, m_Result.Matches.Length);
            for (int index = 0; index < count; index++)
            {
                DialogueRouteMatch match = m_Result.Matches[index];
                EditorGUILayout.LabelField(
                    $"{index + 1}. {match.RouteId}  score {match.Score:F3}  best {match.BestSimilarity:F3}  support {match.SupportCount}",
                    $"{match.BestAnchor}");
            }
        }

        void DrawAttempts()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Variant eligibility", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(m_Result.FallbackReason))
                EditorGUILayout.HelpBox(m_Result.FallbackReason, MessageType.Info);

            foreach (TopicPlaygroundRouteAttempt attempt in m_Result.Attempts)
            {
                EditorGUILayout.LabelField(attempt.RouteId, EditorStyles.miniBoldLabel);
                foreach (AuthoredDialogueCandidateCheck candidate in attempt.Selection.Candidates)
                {
                    EditorGUILayout.LabelField(
                        $"{candidate.Status}: {candidate.Path}",
                        candidate.Reason);
                    if (candidate.Eligibility != null)
                    {
                        foreach (StatConditionEvaluation condition in candidate.Eligibility.Conditions)
                        {
                            string actual = condition.HasActualValue ? condition.ActualValue.ToString("G9") : "unknown";
                            EditorGUILayout.LabelField(
                                $"  {condition.Outcome}: {condition.StatId} {condition.Operation} {condition.Threshold:G9}; actual {actual}",
                                condition.Reason);
                        }
                    }
                }
                if (attempt.Selection.Candidates.Count == 0)
                    EditorGUILayout.LabelField("No files found for this route.", EditorStyles.miniLabel);
            }
        }

        void DrawTranscript()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dialogue", EditorStyles.boldLabel);
            foreach (CanonicalDialogueNode node in m_Result.Transcript)
            {
                string label = string.IsNullOrWhiteSpace(node.displayText) ? node.text : node.displayText;
                EditorGUILayout.HelpBox($"{node.speaker}: {label}", MessageType.None);
            }

            CanonicalDialogueNode current = m_Result.Playback.Current;
            if (current == null)
            {
                EditorGUILayout.LabelField("Dialogue complete.", EditorStyles.miniLabel);
            }
            else if (current.choices != null && current.choices.Count > 0)
            {
                for (int index = 0; index < current.choices.Count; index++)
                {
                    int choiceIndex = index;
                    if (GUILayout.Button(current.choices[index].label))
                        m_Result.Choose(choiceIndex);
                }
            }
            else if (!string.IsNullOrWhiteSpace(current.nextId))
            {
                if (GUILayout.Button("Continue"))
                    m_Result.Continue();
            }
            else if (GUILayout.Button("Finish"))
            {
                m_Result.Continue();
            }
        }

        void DrawFamilyResult()
        {
            TopicPlaygroundFamilyQueryResult result = m_FamilyResult;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Two-stage Topic selection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(result.RoutingModeLabel + "\n" + result.FamilySelectionReason, result.SemanticRoutingAttempted ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField("Stage 1 family", string.IsNullOrEmpty(result.SelectedFamilyId) ? "No semantic family selected" : result.SelectedFamilyId);
            if (result.SemanticRoutingAttempted)
            {
                EditorGUILayout.LabelField($"GTE {m_Session.ModelId} / {m_Session.Backend} / {result.QueryDurationMs:F1} ms total");
                EditorGUILayout.LabelField("Shared-anchor cosine matches", EditorStyles.miniBoldLabel);
                foreach (TopicFamilySemanticMatch match in result.FamilyMatches)
                    EditorGUILayout.LabelField(
                        $"{match.TopicId}: best {match.BestSimilarity:F3}; support {match.SupportCount}; anchor: {match.BestAnchor}");
            }
            else
            {
                EditorGUILayout.LabelField("Semantic family matches", "Not run (direct family isolation test)");
            }

            DrawFamilyContextEvidence(result);
            if (result.TopicRun == null)
                return;

            TopicSelectionResult selection = result.TopicRun.Selection;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stage 2 scenario selection", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Family / occurrence", result.Instance.topicId + " / " + result.Instance.instanceId);
            EditorGUILayout.LabelField("Scenario draw", selection.Draw.ToString("G17", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Winning priority", selection.WinningPriority.HasValue ? selection.WinningPriority.Value.ToString() : "None");
            EditorGUILayout.LabelField("Selected scenario", selection.SelectedScenario == null ? "No eligible positive-weight scenario" : selection.SelectedScenario.scenarioId);
            EditorGUILayout.LabelField("Selection result", selection.Reason);
            EditorGUILayout.LabelField("Replay policy", result.Instance.replayPolicy);
            DrawFamilyCandidates(selection);
            DrawFamilyDialogue(result);
            DrawScenarioPreview(result);
            DrawLifecycleTrace(result);

            if (m_Session.CanStartAnotherTopicOccurrence)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Retry same occurrence"))
                    StartAnotherTopicQuery(result.Instance.instanceId, result.Instance.topicId);
                if (GUILayout.Button("New occurrence of same Topic"))
                {
                    m_OccurrenceOrdinal++;
                    StartAnotherTopicQuery(result.Instance.instanceId + "-next-" + m_OccurrenceOrdinal.ToString("D2"), result.Instance.topicId);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("Retry keeps history identity. New occurrence uses a distinct instance ID and can be selected independently.", MessageType.None);
            }
        }

        void DrawScenarioPreview(TopicPlaygroundFamilyQueryResult result)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scenario runner preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Detached, synthetic playback through the shared ScenarioSession. It does not alter Topic history, persist completion, call a provider, or perform a real activity.", MessageType.Info);
            TopicScenario selected = result.TopicRun == null ? null : result.TopicRun.Selection.SelectedScenario;
            using (new EditorGUI.DisabledScope(selected == null))
            {
                if (GUILayout.Button(m_ScenarioPreview == null ? "Start detached Scenario preview" : "Restart detached Scenario preview"))
                    StartScenarioPreview(result, selected);
            }

            if (m_ScenarioPreviewReport == null)
                return;

            EditorGUILayout.LabelField("Run / current step", m_ScenarioPreviewReport.status + " / " + m_ScenarioPreviewReport.currentStepId);
            EditorGUILayout.LabelField("Synthetic time", m_ScenarioPreviewReport.nowSeconds.ToString("G", CultureInfo.InvariantCulture) + " s");
            EditorGUILayout.LabelField("Provider requests", m_ScenarioPreviewReport.paidStageRequestCount.ToString(CultureInfo.InvariantCulture));
            if (m_ScenarioPreviewReport.hasSimulatedActivity)
                EditorGUILayout.HelpBox("Synthetic activity result was injected by the Playground. No in-game activity occurred.", MessageType.Warning);

            ScenarioEffect effect = FindPreviewEffect("show_dialogue");
            if (effect != null && effect.step != null)
            {
                foreach (ScenarioBeat beat in effect.step.beats)
                    EditorGUILayout.HelpBox("[" + beat.id + "] " + beat.displayText + "  <" + beat.expression + ">", MessageType.None);
                if (GUILayout.Button("Advance dialogue"))
                    ApplyPreviewEvent(new ScenarioEvent { kind = "dialogue_complete", runId = effect.runId, stepId = effect.stepId });
            }

            effect = FindPreviewEffect("ask_player");
            if (effect != null && effect.step != null)
            {
                EditorGUILayout.HelpBox(effect.step.question.displayText, MessageType.Info);
                foreach (ScenarioQuestionOption option in effect.step.options)
                {
                    if (GUILayout.Button(option.label))
                        ApplyPreviewEvent(new ScenarioEvent { kind = "answer", runId = effect.runId, stepId = effect.stepId,
                            questionId = effect.step.questionId, answerId = option.optionId, contextRevision = 1 });
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Skip question"))
                    ApplyPreviewEvent(new ScenarioEvent { kind = "skip", runId = effect.runId, stepId = effect.stepId,
                        questionId = effect.step.questionId, contextRevision = 1 });
                if (GUILayout.Button("Cancel question"))
                    ApplyPreviewEvent(new ScenarioEvent { kind = "cancel", runId = effect.runId, stepId = effect.stepId,
                        questionId = effect.step.questionId, contextRevision = 1 });
                EditorGUILayout.EndHorizontal();
            }

            effect = FindPreviewEffect("wait_for_condition");
            if (effect != null && effect.step != null && GUILayout.Button("Simulate: " + effect.step.objective))
                ApplyPreviewEvent(new ScenarioEvent { kind = "condition_met", runId = effect.runId, stepId = effect.stepId,
                    activityId = LastSimulatedActivityId() ?? effect.step.activityId, conditionId = effect.step.conditionId });
        }

        void StartScenarioPreview(TopicPlaygroundFamilyQueryResult result, TopicScenario selected)
        {
            try
            {
                ScenarioDefinition definition = AuthoredScenarioAdapter.Convert(result.Family, selected);
                string runId = "playground-" + Guid.NewGuid().ToString("N");
                ScenarioRuntimeSnapshot context = ScenarioPlaygroundSession.CreateSyntheticContext(definition, runId, "playground-turn");
                m_ScenarioPreview = new ScenarioPlaygroundSession(definition, runId, context);
                m_ScenarioPreviewReport = m_ScenarioPreview.InitialReport;
                m_ScenarioPreviewTime = 0d;
            }
            catch (Exception exception)
            {
                m_Status = "Scenario preview rejected: " + exception.Message;
            }
        }

        void ApplyPreviewEvent(ScenarioEvent input)
        {
            try
            {
                m_ScenarioPreviewTime += 1d;
                m_ScenarioPreviewReport = m_ScenarioPreview.Apply(input, m_ScenarioPreviewTime);
            }
            catch (Exception exception)
            {
                m_Status = "Scenario preview event rejected: " + exception.Message;
            }
        }

        ScenarioEffect FindPreviewEffect(string type)
        {
            if (m_ScenarioPreviewReport == null) return null;
            for (int index = m_ScenarioPreviewReport.effects.Count - 1; index >= 0; index--)
                if (m_ScenarioPreviewReport.effects[index].type == type)
                    return m_ScenarioPreviewReport.effects[index];
            return null;
        }

        string LastSimulatedActivityId()
        {
            ScenarioEffect effect = FindPreviewEffect("simulated_activity_result");
            return effect == null || string.IsNullOrWhiteSpace(effect.outcome) ? null : effect.outcome;
        }

        void DrawFamilyContextEvidence(TopicPlaygroundFamilyQueryResult result)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Synthetic context consumed by Stage 2", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Local hour", result.LocalHour < 0 ? "Unknown" : result.LocalHour.ToString());
            EditorGUILayout.LabelField("Selection time UTC", result.SelectionTimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            foreach (SyntheticStatInput stat in result.SyntheticStats)
                EditorGUILayout.LabelField(stat.statId, stat.isKnown ? stat.value.ToString("G9", CultureInfo.InvariantCulture) : "Unknown (not zero)");
            foreach (SyntheticFactInput fact in result.SyntheticFacts)
                EditorGUILayout.LabelField("fact " + fact.key, fact.isKnown ? fact.value.ToString() : "Unknown (not false)");
            EditorGUILayout.LabelField("Object inventory", result.ObjectsComplete ? "Complete" : "Incomplete / unknown remainder");
            foreach (SyntheticObjectInput item in result.SyntheticObjects)
            {
                EditorGUILayout.LabelField("object " + item.id + " / " + item.kind, EditorStyles.miniBoldLabel);
                foreach (SyntheticObjectFlagInput flag in item.flags)
                    EditorGUILayout.LabelField("  flag " + flag.key, flag.isKnown ? flag.value.ToString() : "Unknown");
                foreach (SyntheticObjectNumberInput number in item.numbers)
                    EditorGUILayout.LabelField("  number " + number.key, number.isKnown ? number.value.ToString("G9", CultureInfo.InvariantCulture) : "Unknown (not zero)");
            }
            EditorGUILayout.LabelField("Player evidence", "Separate contract; not supplied as ordinary stats.");
        }

        void DrawFamilyCandidates(TopicSelectionResult selection)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scenario candidate diagnostics", EditorStyles.boldLabel);
            foreach (TopicCandidateTrace candidate in selection.Candidates)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    $"{candidate.ScenarioId} — priority {candidate.Priority}, effective weight {candidate.EffectiveWeight:G9}",
                    candidate.Reason);
                if (candidate.Gate == null)
                    EditorGUILayout.LabelField("Eligibility gate not evaluated", candidate.Reason);
                else
                    DrawConditionTrace(candidate.Gate, 0);
                foreach (TopicWeightTrace modifier in candidate.Modifiers)
                {
                    EditorGUILayout.LabelField(
                        $"Modifier: {modifier.Truth}; authored {modifier.AuthoredAmount:G9}; applied {modifier.AppliedAmount:G9}");
                    DrawConditionTrace(modifier.Condition, 1);
                }
                EditorGUILayout.EndVertical();
            }
        }

        void DrawConditionTrace(TopicConditionResult condition, int depth)
        {
            string indent = new string(' ', Mathf.Min(depth, 8) * 2);
            string binding = string.IsNullOrEmpty(condition.BoundObjectId) ? string.Empty : " [object " + condition.BoundObjectId + "]";
            EditorGUILayout.LabelField(indent + condition.Truth + binding, condition.Reason);
            foreach (TopicConditionResult child in condition.Children)
                DrawConditionTrace(child, depth + 1);
        }

        void DrawFamilyDialogue(TopicPlaygroundFamilyQueryResult result)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Traversed dialogue preview (not a delivery log)", EditorStyles.boldLabel);
            if (result.Playback == null)
            {
                EditorGUILayout.HelpBox("No dialogue node was started because there was no eligible scenario.", MessageType.Warning);
                return;
            }

            foreach (CanonicalDialogueNode node in result.Transcript)
            {
                string text = string.IsNullOrWhiteSpace(node.displayText) ? node.text : node.displayText;
                EditorGUILayout.HelpBox(node.speaker + " [" + node.id + "]: " + text, MessageType.None);
            }
            CanonicalDialogueNode current = result.Playback.Current;
            if (current == null)
            {
                EditorGUILayout.LabelField("Cursor is past the terminal node. Completion is a separate lifecycle action.", EditorStyles.miniLabel);
            }
            else
            {
                string text = string.IsNullOrWhiteSpace(current.displayText) ? current.text : current.displayText;
                EditorGUILayout.HelpBox("Current node (preview only; acknowledge separately): " + current.id + " — " + text, MessageType.Info);
                if (current.choices != null && current.choices.Count > 0)
                {
                    for (int index = 0; index < current.choices.Count; index++)
                    {
                        int choiceIndex = index;
                        if (GUILayout.Button("Move cursor: " + current.choices[index].label))
                            result.Choose(choiceIndex);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(current.nextId) || result.Playback.Current != null)
                {
                    if (GUILayout.Button("Move cursor forward (does not acknowledge delivery)"))
                        result.Continue();
                }
            }

            m_TopicElapsedSeconds = EditorGUILayout.DoubleField("Synthetic event offset from selection (seconds)", m_TopicElapsedSeconds);
            DateTimeOffset eventTime;
            if (!TryGetCurrentTopicEventTime(result, out eventTime))
                EditorGUILayout.HelpBox("Event offset/time is invalid or outside the supported date range.", MessageType.Error);
            else
            {
                EditorGUILayout.LabelField("Synthetic event time", eventTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                if (current != null && GUILayout.Button("Acknowledge current node as presented"))
                    RunLifecycle(() => m_Session.AcknowledgeCurrentNode(result, eventTime));
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!result.Playback.IsComplete))
                    if (GUILayout.Button("Complete occurrence")) RunLifecycle(() => m_Session.CompleteTopic(result, eventTime));
                if (GUILayout.Button("Cancel occurrence")) RunLifecycle(() => m_Session.CancelTopic(result, eventTime));
                if (GUILayout.Button("Fail occurrence")) RunLifecycle(() => m_Session.FailTopic(result, eventTime));
                EditorGUILayout.EndHorizontal();
            }
        }

        bool TryGetCurrentTopicEventTime(TopicPlaygroundFamilyQueryResult result, out DateTimeOffset eventTime)
        {
            eventTime = default(DateTimeOffset);
            if (double.IsNaN(m_TopicElapsedSeconds) || double.IsInfinity(m_TopicElapsedSeconds) || m_TopicElapsedSeconds < 0d)
                return false;
            try { eventTime = result.SelectionTimeUtc.AddSeconds(m_TopicElapsedSeconds); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        void RunLifecycle(Func<TopicPlaygroundLifecycleEvent> action)
        {
            try
            {
                TopicPlaygroundLifecycleEvent outcome = action();
                m_Status = outcome.Action + ": " + outcome.Status + (outcome.PendingSave ? " — PENDING SAVE (not durable)" : string.Empty);
            }
            catch (Exception exception)
            {
                m_Status = exception.Message;
            }
        }

        void DrawLifecycleTrace(TopicPlaygroundFamilyQueryResult result)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Occurrence lifecycle", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("History", result.HistoryDirty ? "Dirty in memory" : "Clean / no unsaved changes");
            EditorGUILayout.HelpBox(result.PersistenceStatus, result.PendingSave ? MessageType.Error : MessageType.Info);
            foreach (TopicPlaygroundLifecycleEvent lifecycle in result.LifecycleEvents)
            {
                string message = $"{lifecycle.TimeUtc}: {lifecycle.Action} {lifecycle.NodeId} — {lifecycle.Status}; consumed={lifecycle.IsConsumed}; requires-save={lifecycle.RequiresSave}";
                if (!string.IsNullOrEmpty(lifecycle.PersistenceError)) message += "; pending: " + lifecycle.PersistenceError;
                EditorGUILayout.LabelField(message);
            }
        }

        void StartAnotherTopicQuery(string instanceId, string topicId)
        {
            TopicPlaygroundContext context;
            List<string> errors;
            if (!TryBuildCurrentContext(out context, out errors))
            {
                m_Status = string.Join(" ", errors.ToArray());
                return;
            }
            DateTimeOffset selectionTime;
            if (!TopicPlaygroundScenario.TryParseSelectionTimeUtc(m_TopicSelectionTimeUtc, out selectionTime))
            {
                m_Status = "Selection time must be an ISO-8601 timestamp with an explicit UTC offset.";
                return;
            }
            m_TopicInstanceId = instanceId;
            m_TopicFamilyId = topicId;
            m_QueryRoutine = m_Session.QueryTopicFamilySteps(
                m_Query, context, m_TopicFixtureSet, m_Mode == PlaygroundMode.SemanticTopicFamily,
                m_TopicFamilyId, instanceId, m_TopicSource, m_TopicReplayOverride, m_TopicDraw, selectionTime,
                result => m_FamilyResult = result,
                stage => m_Status = stage);
            m_Result = null;
            EditorApplication.update += AdvanceQuery;
        }

        void StartModelFingerprintMonitor()
        {
            if (m_Session == null)
                return;

            TopicPlaygroundModelFingerprintSnapshot fingerprint = m_Session.ModelFingerprintSnapshot;
            m_LastModelFingerprintDisplay = fingerprint.DisplayValue;
            EditorApplication.update -= AdvanceModelFingerprint;
            if (fingerprint.IsCalculating)
                EditorApplication.update += AdvanceModelFingerprint;
        }

        void AdvanceModelFingerprint()
        {
            if (m_Session == null)
            {
                EditorApplication.update -= AdvanceModelFingerprint;
                return;
            }

            TopicPlaygroundModelFingerprintSnapshot fingerprint = m_Session.ModelFingerprintSnapshot;
            if (!string.Equals(fingerprint.DisplayValue, m_LastModelFingerprintDisplay, StringComparison.Ordinal))
            {
                m_LastModelFingerprintDisplay = fingerprint.DisplayValue;
                Repaint();
            }

            if (!fingerprint.IsCalculating)
                EditorApplication.update -= AdvanceModelFingerprint;
        }

        void OnModelAssetChanged()
        {
            if (m_Session == null)
                return;

            m_Session.InvalidateModelFingerprint();
            StartModelFingerprintMonitor();
            Repaint();
        }
    }

    public sealed class TopicPlaygroundModelAssetPostprocessor : AssetPostprocessor
    {
        public static event Action AssetChanged;

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ContainsModel(importedAssets) || ContainsModel(deletedAssets) ||
                ContainsModel(movedAssets) || ContainsModel(movedFromAssetPaths))
                AssetChanged?.Invoke();
        }

        static bool ContainsModel(string[] paths)
        {
            if (paths == null)
                return false;

            foreach (string path in paths)
            {
                if (string.Equals(path, TopicPlaygroundModelFingerprint.AssetPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
