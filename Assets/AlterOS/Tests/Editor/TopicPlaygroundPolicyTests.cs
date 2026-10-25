using System.Collections.Generic;
using System.IO;
using AlterOS.Dialogue;
using AlterOS.Semantics;
using AlterOS.Stats;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests
{
    public class TopicPlaygroundPolicyTests
    {
        [Test]
        public void LowConfidenceMatchSelectsConfiguredFallback()
        {
            DialogueRouteMatch[] matches =
            {
                new DialogueRouteMatch("player_tired", 0.63f, 0.41f, 2, "I'm exhausted."),
            };

            TopicRouteDecision decision = TopicRoutePolicy.Decide(matches, 0.45f, "unclear_short");

            Assert.IsTrue(decision.UsedFallback);
            Assert.AreEqual("unclear_short", decision.SelectedRouteId);
            Assert.AreEqual(TopicRouteFallbackReason.BelowMinimumSimilarity, decision.Reason);
        }

        [Test]
        public void SupportedMatchKeepsTheTopRoute()
        {
            DialogueRouteMatch[] matches =
            {
                new DialogueRouteMatch("player_tired", 0.72f, 0.61f, 2, "I'm exhausted."),
                new DialogueRouteMatch("player_stressed", 0.65f, 0.58f, 2, "I'm overwhelmed."),
            };

            TopicRouteDecision decision = TopicRoutePolicy.Decide(matches, 0.45f, "unclear_short");

            Assert.IsFalse(decision.UsedFallback);
            Assert.AreEqual("player_tired", decision.SelectedRouteId);
            Assert.AreEqual(TopicRouteFallbackReason.None, decision.Reason);
        }

        [Test]
        public void RouteWithoutAnySupportedAnchorsUsesFallback()
        {
            DialogueRouteMatch[] matches =
            {
                new DialogueRouteMatch("player_tired", 0.39f, 0.40f, 0, "I'm exhausted."),
            };

            TopicRouteDecision decision = TopicRoutePolicy.Decide(matches, 0.35f, "unclear_short");

            Assert.IsTrue(decision.UsedFallback);
            Assert.AreEqual(TopicRouteFallbackReason.NoSupportingAnchors, decision.Reason);
        }

        [Test]
        public void SelectorSkipsInvalidAndOutOfWindowVariantsThenPicksStableEligiblePath()
        {
            AuthoredDialogueVariantCandidate[] candidates =
            {
                Candidate("Generic/player_tired/player_tired_002.json", Dialogue("wrong_route", "wrong")),
                Candidate("Generic/player_tired/player_tired_001.json", Dialogue("player_tired", "morning", 6, 10)),
                Candidate("Generic/player_tired/player_tired_003.json", Dialogue("player_tired", "anytime")),
            };

            AuthoredDialogueSelectionResult result = AuthoredDialogueVariantSelector.Select("player_tired", candidates, 14);

            Assert.IsNotNull(result.SelectedDialogue);
            Assert.AreEqual("anytime", result.SelectedDialogue.nodes[0].id);
            Assert.AreEqual("Generic/player_tired/player_tired_003.json", result.SelectedPath);
            Assert.AreEqual(3, result.Candidates.Count);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.IneligibleTimeWindow, result.Candidates[0].Status);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.RouteMismatch, result.Candidates[1].Status);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.Selected, result.Candidates[2].Status);
        }

        [Test]
        public void SelectorReportsWhenEveryVariantIsOutsideItsTimeWindow()
        {
            AuthoredDialogueVariantCandidate[] candidates =
            {
                Candidate("Generic/night/night_001.json", Dialogue("night", "late", 22, 6)),
            };

            AuthoredDialogueSelectionResult result = AuthoredDialogueVariantSelector.Select("night", candidates, 12);

            Assert.IsNull(result.SelectedDialogue);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.IneligibleTimeWindow, result.Candidates[0].Status);
        }

        [Test]
        public void SelectorSkipsMalformedJsonAndReportsTheReason()
        {
            AuthoredDialogueVariantCandidate[] candidates =
            {
                new AuthoredDialogueVariantCandidate("Generic/greet/greet_001.json", "{"),
                Candidate("Generic/greet/greet_002.json", Dialogue("greet", "hello")),
            };

            AuthoredDialogueSelectionResult result = AuthoredDialogueVariantSelector.Select("greet", candidates, 12);

            Assert.AreEqual("Generic/greet/greet_002.json", result.SelectedPath);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.Invalid, result.Candidates[0].Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Candidates[0].Reason));
        }

        [Test]
        public void SelectorTreatsUnknownStatAsUnknownAndExplicitZeroAsAValue()
        {
            DialogueDefinition dialogue = Dialogue("player_tired", "low_hydration");
            dialogue.routing.eligibility = Eligibility(Condition("alterra.hydration", "at_most", 25f));
            AuthoredDialogueVariantCandidate[] candidates =
            {
                Candidate("Generic/player_tired/player_tired_001.json", dialogue),
            };

            AuthoredDialogueSelectionResult unknown = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, -1, new StatSnapshot());
            var knownZero = new StatSnapshot();
            string error;
            Assert.IsTrue(knownZero.TrySet("alterra.hydration", 0f, out error), error);
            AuthoredDialogueSelectionResult zero = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, -1, knownZero);

            Assert.IsNull(unknown.SelectedDialogue);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.IneligibleCondition, unknown.Candidates[0].Status);
            Assert.AreEqual("UnknownValue", unknown.Candidates[0].Eligibility.Conditions[0].Outcome.ToString());
            Assert.IsNotNull(zero.SelectedDialogue);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.Selected, zero.Candidates[0].Status);
            Assert.AreEqual("Satisfied", zero.Candidates[0].Eligibility.Conditions[0].Outcome.ToString());
        }

        [Test]
        public void SelectorReportsUnsupportedContextRulesInsteadOfIgnoringThem()
        {
            DialogueDefinition dialogue = Dialogue("player_tired", "tagged");
            dialogue.routing.requiredContext.Add("night");

            AuthoredDialogueSelectionResult result = AuthoredDialogueVariantSelector.Select(
                "player_tired",
                new[] { Candidate("Generic/player_tired/player_tired_001.json", dialogue) },
                -1,
                new StatSnapshot());

            Assert.IsNull(result.SelectedDialogue);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.UnsupportedCondition, result.Candidates[0].Status);
            StringAssert.Contains("requiredContext", result.Candidates[0].Reason);
        }

        [Test]
        public void JsonUtilityPreservesUnsupportedAnyRuleForDiagnostic()
        {
            string json = "{\"schemaVersion\":1,\"id\":\"gated\",\"source\":\"generic\",\"routing\":{" +
                "\"routeId\":\"player_tired\",\"examples\":[\"example\"]," +
                "\"eligibility\":{\"schemaVersion\":1,\"all\":[],\"any\":[{" +
                "\"statId\":\"alterra.hydration\",\"operation\":\"at_least\",\"value\":75}]}}," +
                "\"nodes\":[{\"id\":\"start\",\"text\":\"text\",\"speaker\":\"alterra\"}]}";
            DialogueDefinition dialogue = JsonUtility.FromJson<DialogueDefinition>(json);

            AuthoredDialogueSelectionResult result = AuthoredDialogueVariantSelector.Select(
                "player_tired",
                new[] { Candidate("fixture.json", dialogue) },
                -1,
                new StatSnapshot());

            Assert.IsNull(result.SelectedDialogue);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.UnsupportedCondition, result.Candidates[0].Status);
            StringAssert.Contains("only the versioned 'all' list", result.Candidates[0].Reason);
        }

        [Test]
        public void SyntheticStatFixtureChangesSelectionWithoutEnteringGenericCatalogue()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fixtureRoot = Path.Combine(projectRoot, "Tools", "TopicPlayground", "Fixtures", "stat-context-v1", "Generic", "player_tired");
            string[] fixtureFiles = Directory.GetFiles(fixtureRoot, "*.json");
            Assert.GreaterOrEqual(fixtureFiles.Length, 3);

            var candidates = new List<AuthoredDialogueVariantCandidate>();
            foreach (string file in fixtureFiles)
            {
                string label = "SYNTHETIC FIXTURE v1/Generic/player_tired/" + Path.GetFileName(file);
                candidates.Add(new AuthoredDialogueVariantCandidate(label, File.ReadAllText(file)));
            }

            TopicPlaygroundContext lowContext = BuildContext("alterra.hydration", 20f);
            TopicPlaygroundContext highContext = BuildContext("alterra.hydration", 80f);
            TopicPlaygroundContext mixedEmotionContext = BuildContext(
                "alterra.emotion.valence", -0.75f,
                "alterra.emotion.control", -0.5f);
            TopicPlaygroundContext unknownContext = BuildContext();

            AuthoredDialogueSelectionResult low = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, lowContext.LocalHour, lowContext.CopyStats());
            AuthoredDialogueSelectionResult high = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, highContext.LocalHour, highContext.CopyStats());
            AuthoredDialogueSelectionResult mixedEmotion = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, mixedEmotionContext.LocalHour, mixedEmotionContext.CopyStats());
            AuthoredDialogueSelectionResult unknown = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, unknownContext.LocalHour, unknownContext.CopyStats());

            Assert.IsNotNull(low.SelectedDialogue);
            StringAssert.Contains("hydration_low", low.SelectedPath);
            StringAssert.Contains("hydration_high", high.SelectedPath);
            StringAssert.Contains("emotion_mixed", mixedEmotion.SelectedPath);
            Assert.IsNull(unknown.SelectedDialogue);
            Assert.AreEqual(AuthoredDialogueCandidateStatus.IneligibleCondition, unknown.Candidates[0].Status);
            string genericRoot = Path.Combine(Application.streamingAssetsPath, "Dialogue", "Generic");
            AuthoredDialogueRouteCatalog authored = AuthoredDialogueRouteCatalog.FromDirectory(genericRoot);
            CollectionAssert.DoesNotContain(
                authored.GetSequencePaths("player_tired"),
                "Generic/player_tired/player_tired_001_hydration_low.json");
        }

        [Test]
        public void SyntheticFixtureFallbackRouteRemainsInAuthoredCatalogue()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fixtureRoot = Path.Combine(projectRoot, "Tools", "TopicPlayground", "Fixtures", "stat-context-v1", "Generic");
            string authoredRoot = Path.Combine(Application.streamingAssetsPath, "Dialogue", "Generic");
            AuthoredDialogueRouteCatalog authored = AuthoredDialogueRouteCatalog.FromDirectory(authoredRoot);

            Assert.IsFalse(Directory.Exists(Path.Combine(fixtureRoot, "unclear_short")));
            Assert.IsNotEmpty(authored.GetSequencePaths("unclear_short"));
        }

        [Test]
        public void ScenarioJsonAndEquivalentWindowContextSelectSameFixtureAndDiagnostics()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string scenarioPath = Path.Combine(projectRoot, "Tools", "TopicPlayground", "sample-scenario.json");
            TopicPlaygroundScenario scenario = JsonUtility.FromJson<TopicPlaygroundScenario>(File.ReadAllText(scenarioPath));
            TopicPlaygroundContext scenarioContext;
            List<string> errors;
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(
                scenario.localHour, scenario.stats, scenario.fixtureSet, out scenarioContext, out errors), string.Join(" ", errors.ToArray()));

            TopicPlaygroundContext windowContext = BuildContext("alterra.hydration", 20f);
            string fixtureRoot = Path.Combine(projectRoot, "Tools", "TopicPlayground", "Fixtures", scenario.fixtureSet, "Generic", "player_tired");
            var candidates = new List<AuthoredDialogueVariantCandidate>();
            foreach (string file in Directory.GetFiles(fixtureRoot, "*.json"))
                candidates.Add(new AuthoredDialogueVariantCandidate("SYNTHETIC FIXTURE " + scenario.fixtureSet + "/" + Path.GetFileName(file), File.ReadAllText(file)));

            AuthoredDialogueSelectionResult fromScenario = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, scenarioContext.LocalHour, scenarioContext.CopyStats());
            AuthoredDialogueSelectionResult fromWindow = AuthoredDialogueVariantSelector.Select(
                "player_tired", candidates, windowContext.LocalHour, windowContext.CopyStats());

            Assert.AreEqual(fromScenario.SelectedPath, fromWindow.SelectedPath);
            Assert.AreEqual(fromScenario.Candidates.Count, fromWindow.Candidates.Count);
            for (int index = 0; index < fromScenario.Candidates.Count; index++)
            {
                Assert.AreEqual(fromScenario.Candidates[index].Status, fromWindow.Candidates[index].Status);
                Assert.AreEqual(fromScenario.Candidates[index].Reason, fromWindow.Candidates[index].Reason);
            }
        }

        [Test]
        public void NoEligibleSyntheticVariantCanResolveToAuthoredFallback()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fixtureRouteRoot = Path.Combine(projectRoot, "Tools", "TopicPlayground", "Fixtures", "stat-context-v1", "Generic", "player_tired");
            string authoredRoot = Path.Combine(Application.streamingAssetsPath, "Dialogue", "Generic");
            var fixtureCandidates = new List<AuthoredDialogueVariantCandidate>();
            foreach (string file in Directory.GetFiles(fixtureRouteRoot, "*.json"))
                fixtureCandidates.Add(new AuthoredDialogueVariantCandidate("SYNTHETIC FIXTURE v1/" + Path.GetFileName(file), File.ReadAllText(file)));

            TopicPlaygroundContext unknown = BuildContext();
            AuthoredDialogueSelectionResult rejected = AuthoredDialogueVariantSelector.Select(
                "player_tired", fixtureCandidates, unknown.LocalHour, unknown.CopyStats());
            Assert.IsNull(rejected.SelectedDialogue);

            AuthoredDialogueRouteCatalog authored = AuthoredDialogueRouteCatalog.FromDirectory(authoredRoot);
            var fallbackCandidates = new List<AuthoredDialogueVariantCandidate>();
            foreach (string relativePath in authored.GetSequencePaths("unclear_short"))
            {
                string fullPath = Path.Combine(Application.streamingAssetsPath, "Dialogue", relativePath.Replace('/', Path.DirectorySeparatorChar));
                fallbackCandidates.Add(new AuthoredDialogueVariantCandidate(relativePath, File.ReadAllText(fullPath)));
            }
            AuthoredDialogueSelectionResult fallback = AuthoredDialogueVariantSelector.Select(
                "unclear_short", fallbackCandidates, unknown.LocalHour, unknown.CopyStats());

            Assert.IsNotNull(fallback.SelectedDialogue);
            StringAssert.DoesNotContain("SYNTHETIC FIXTURE", fallback.SelectedPath);
        }

        [Test]
        public void CanonicalPlaybackTraversesChoiceAndCompletes()
        {
            DialogueDefinition dialogue = Dialogue("greet", "start");
            dialogue.nodes[0].choices = new List<CanonicalDialogueChoice>
            {
                new CanonicalDialogueChoice { label = "Continue", nextId = "reply" },
            };
            dialogue.nodes.Add(new CanonicalDialogueNode { id = "reply", text = "Here I am." });

            var playback = new DialoguePlayback(dialogue);

            Assert.AreEqual("reply", playback.Choose(0).id);
            Assert.IsNull(playback.Continue());
            Assert.IsTrue(playback.IsComplete);
        }

        [TestCase(23, 22, 6, true)]
        [TestCase(2, 22, 6, true)]
        [TestCase(6, 22, 6, false)]
        [TestCase(9, 6, 10, true)]
        [TestCase(10, 6, 10, false)]
        [TestCase(8, 8, 8, true)]
        [TestCase(7, 8, 8, false)]
        [TestCase(-1, 6, 10, true)]
        public void TimeWindowMatchesRuntimeBoundaries(int hour, int start, int end, bool expected)
        {
            Assert.AreEqual(expected, CanonicalTimeWindow.IsWithin(start, end, hour));
        }

        private static AuthoredDialogueVariantCandidate Candidate(string path, DialogueDefinition definition)
        {
            return new AuthoredDialogueVariantCandidate(path, JsonUtility.ToJson(definition));
        }

        private static CanonicalStatEligibility Eligibility(params CanonicalStatCondition[] conditions)
        {
            return new CanonicalStatEligibility { all = new List<CanonicalStatCondition>(conditions) };
        }

        private static CanonicalStatCondition Condition(string statId, string operation, float value)
        {
            return new CanonicalStatCondition { statId = statId, operation = operation, value = value };
        }

        private static TopicPlaygroundContext BuildContext(params object[] idValues)
        {
            var inputs = new List<SyntheticStatInput>();
            for (int index = 0; index < idValues.Length; index += 2)
                inputs.Add(new SyntheticStatInput
                {
                    statId = (string)idValues[index],
                    isKnown = true,
                    value = (float)idValues[index + 1],
                });

            TopicPlaygroundContext context;
            List<string> errors;
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(-1, inputs, "stat-context-v1", out context, out errors), string.Join(" ", errors.ToArray()));
            return context;
        }

        private static DialogueDefinition Dialogue(string routeId, string nodeId, int startHour = -1, int endHour = -1)
        {
            return new DialogueDefinition
            {
                id = routeId + "." + nodeId,
                routing = new DialogueRouting
                {
                    routeId = routeId,
                    startHour = startHour,
                    endHour = endHour,
                    examples = new List<string> { "example" },
                },
                nodes = new List<CanonicalDialogueNode>
                {
                    new CanonicalDialogueNode { id = nodeId, text = "Dialogue text." },
                },
            };
        }
    }
}
