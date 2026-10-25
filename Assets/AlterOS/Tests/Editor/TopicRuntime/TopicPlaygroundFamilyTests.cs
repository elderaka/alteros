using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using AlterOS.Dialogue;
using AlterOS.Semantics;
using AlterOS.Stats;
using AlterOS.Topics;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicPlaygroundFamilyTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void DetachedContextKeepsUnknownZeroFactsObjectsAndAxesDistinct()
        {
            var stats = new List<SyntheticStatInput>
            {
                new SyntheticStatInput { statId = "alterra.hydration", isKnown = false },
                new SyntheticStatInput { statId = "alterra.emotion.valence", isKnown = true, value = -1f },
            };
            var facts = new List<SyntheticFactInput>
            {
                new SyntheticFactInput { key = "user_confirmed.has_bottle", isKnown = false, value = true },
                new SyntheticFactInput { key = "room.quiet", isKnown = true, value = false },
            };
            var objects = new List<SyntheticObjectInput>
            {
                new SyntheticObjectInput
                {
                    id = "dispenser-a",
                    kind = "dispenser",
                    flags = new List<SyntheticObjectFlagInput>
                    {
                        new SyntheticObjectFlagInput { key = "drinkable", isKnown = true, value = true },
                    },
                    numbers = new List<SyntheticObjectNumberInput>
                    {
                        new SyntheticObjectNumberInput { key = "water_level", isKnown = false, value = 1d },
                    },
                },
            };

            TopicPlaygroundContext context;
            List<string> errors;
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(
                9, stats, string.Empty, facts, true, objects, out context, out errors), string.Join(" ", errors));

            float hydration;
            Assert.IsFalse(context.TryGet("alterra.hydration", out hydration), "Unknown hydration must not become zero.");
            Assert.IsTrue(context.TryGet("alterra.emotion.valence", out hydration));
            Assert.AreEqual(-1f, hydration);

            TopicContext topicContext = TopicPlaygroundTopicContextAdapter.Create(context);
            Assert.IsFalse(topicContext.facts.ContainsKey("user_confirmed.has_bottle"));
            Assert.IsTrue(topicContext.facts.ContainsKey("room.quiet"));
            Assert.IsFalse(topicContext.facts["room.quiet"]);
            Assert.IsTrue(topicContext.objectsComplete);
            Assert.AreEqual("dispenser-a", topicContext.objects[0].id);
            Assert.IsTrue(topicContext.objects[0].flags["drinkable"]);
            Assert.IsFalse(topicContext.objects[0].numbers.ContainsKey("water_level"));

            topicContext.objects[0].flags["drinkable"] = false;
            topicContext.facts["room.quiet"] = true;
            TopicContext independentCopy = TopicPlaygroundTopicContextAdapter.Create(context);
            Assert.IsTrue(independentCopy.objects[0].flags["drinkable"]);
            Assert.IsFalse(independentCopy.facts["room.quiet"]);
        }

        [Test]
        public void InvalidSyntheticFactsObjectsAndNonfiniteNumbersFailWithoutPublishingContext()
        {
            TopicPlaygroundContext context;
            List<string> errors;
            Assert.IsFalse(TopicPlaygroundContextBuilder.TryBuild(
                -1,
                new[] { new SyntheticStatInput { statId = "player.sentiment.frustration", isKnown = true, value = 1f } },
                string.Empty, null, false, null, out context, out errors));
            Assert.IsNull(context);
            StringAssert.Contains("unknown stat ID", string.Join(" ", errors));

            var duplicateFacts = new[]
            {
                new SyntheticFactInput { key = "room.quiet", isKnown = true, value = true },
                new SyntheticFactInput { key = "room.quiet", isKnown = false },
            };
            Assert.IsFalse(TopicPlaygroundContextBuilder.TryBuild(-1, null, string.Empty,
                duplicateFacts, false, null, out context, out errors));
            Assert.IsNull(context);

            var badObjects = new[]
            {
                new SyntheticObjectInput
                {
                    id = "dispenser-a", kind = "dispenser",
                    numbers = new List<SyntheticObjectNumberInput>
                    {
                        new SyntheticObjectNumberInput { key = "water_level", isKnown = true, value = double.NaN },
                    },
                },
            };
            Assert.IsFalse(TopicPlaygroundContextBuilder.TryBuild(-1, null, string.Empty,
                null, true, badObjects, out context, out errors));
            Assert.IsNull(context);
            StringAssert.Contains("finite", string.Join(" ", errors));

            var tooManyObjects = new List<SyntheticObjectInput>();
            for (int index = 0; index < 1025; index++)
                tooManyObjects.Add(new SyntheticObjectInput { id = "object-" + index, kind = "prop" });
            Assert.IsFalse(TopicPlaygroundContextBuilder.TryBuild(-1, null, string.Empty,
                null, true, tooManyObjects, out context, out errors));
            Assert.IsNull(context);
            StringAssert.Contains("1024", string.Join(" ", errors));
        }

        [Test]
        public void ScenarioJsonAndEquivalentWindowInputsSelectSameScenarioAndDiagnostics()
        {
            string root = Path.Combine(Environment.CurrentDirectory, "Tools", "TopicPlayground", "Fixtures", "topic-runtime-v1");
            string scenarioPath = Path.Combine(root, "deterministic-parity.json");
            TopicPlaygroundScenario scenario = Deserialize<TopicPlaygroundScenario>(File.ReadAllText(scenarioPath));
            TopicDefinition family = Deserialize<TopicDefinition>(File.ReadAllText(
                Path.Combine(root, "Topics", "beverage.drink.json")));
            Assert.AreEqual("direct-family", scenario.topicMode, "Direct fixture selection is not semantic-routing proof.");
            Assert.AreEqual("beverage.drink", scenario.topicFamilyId);
            StringAssert.StartsWith(root, Path.GetFullPath(scenarioPath));
            StringAssert.DoesNotContain(Path.Combine("Assets", "StreamingAssets"), Path.GetFullPath(scenarioPath));

            TopicPlaygroundContext fromScenario;
            List<string> errors;
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(
                scenario.localHour, scenario.stats, scenario.fixtureSet, scenario.facts,
                scenario.objectsComplete, scenario.objects, out fromScenario, out errors), string.Join(" ", errors));

            // The Editor window's fields are serialized into the same detached input DTOs before building.
            TopicPlaygroundContext fromWindowControls;
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(
                scenario.localHour, new List<SyntheticStatInput>(scenario.stats), scenario.fixtureSet,
                new List<SyntheticFactInput>(scenario.facts), scenario.objectsComplete,
                new List<SyntheticObjectInput>(scenario.objects), out fromWindowControls, out errors), string.Join(" ", errors));

            TopicInstance instance = TopicInstance.Create(family, scenario.instanceId, scenario.source,
                string.IsNullOrWhiteSpace(scenario.replayPolicy) ? null : scenario.replayPolicy);
            DateTimeOffset selectionTime;
            Assert.IsTrue(TopicPlaygroundScenario.TryParseSelectionTimeUtc(scenario.selectionTimeUtc, out selectionTime));
            var firstHarness = new TopicPlaygroundTopicHarness();
            var secondHarness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun fromJson = firstHarness.Select(
                family, instance, TopicPlaygroundTopicContextAdapter.Create(fromScenario), selectionTime, scenario.draw);
            TopicPlaygroundTopicRun fromWindow = secondHarness.Select(
                family, instance, TopicPlaygroundTopicContextAdapter.Create(fromWindowControls), selectionTime, scenario.draw);

            Assert.IsNotNull(fromJson.Selection.SelectedScenario);
            Assert.AreEqual(fromJson.Selection.SelectedScenario.scenarioId, fromWindow.Selection.SelectedScenario.scenarioId);
            Assert.AreEqual(fromJson.Selection.Draw, fromWindow.Selection.Draw);
            Assert.AreEqual(fromJson.Selection.WinningPriority, fromWindow.Selection.WinningPriority);
            Assert.AreEqual(fromJson.Selection.Candidates.Count, fromWindow.Selection.Candidates.Count);
            for (int index = 0; index < fromJson.Selection.Candidates.Count; index++)
            {
                TopicCandidateTrace left = fromJson.Selection.Candidates[index];
                TopicCandidateTrace right = fromWindow.Selection.Candidates[index];
                Assert.AreEqual(left.ScenarioId, right.ScenarioId);
                Assert.AreEqual(left.Priority, right.Priority);
                Assert.AreEqual(left.Reason, right.Reason);
                Assert.AreEqual(left.EffectiveWeight, right.EffectiveWeight);
                Assert.AreEqual(left.Gate == null ? null : left.Gate.Truth.ToString(),
                    right.Gate == null ? null : right.Gate.Truth.ToString());
                if (left.Gate != null)
                    AssertConditionEqual(left.Gate, right.Gate);
                Assert.AreEqual(left.Modifiers.Count, right.Modifiers.Count);
                for (int modifierIndex = 0; modifierIndex < left.Modifiers.Count; modifierIndex++)
                {
                    Assert.AreEqual(left.Modifiers[modifierIndex].AuthoredAmount, right.Modifiers[modifierIndex].AuthoredAmount);
                    Assert.AreEqual(left.Modifiers[modifierIndex].AppliedAmount, right.Modifiers[modifierIndex].AppliedAmount);
                    AssertConditionEqual(left.Modifiers[modifierIndex].Condition, right.Modifiers[modifierIndex].Condition);
                }
            }
        }

        [Test]
        public void DoublePrecisionDrawNearWeightBoundarySelectsSameScenarioWithoutFloatRounding()
        {
            TopicDefinition family = LoadFamily();
            var context = new TopicContext { localHour = -1, objectsComplete = true };
            context.stats.TrySet("alterra.hydration", 80f, out _);
            double drawImmediatelyBelowTwoThirds = (2d / 3d) - 1e-15d;
            var exactHarness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun exact = exactHarness.Select(
                family,
                TopicInstance.Create(family, "draw-boundary-exact", "authored"),
                context,
                Now,
                drawImmediatelyBelowTwoThirds);

            var lossyHarness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun rounded = lossyHarness.Select(
                family,
                TopicInstance.Create(family, "draw-boundary-rounded", "authored"),
                context,
                Now,
                (double)(float)drawImmediatelyBelowTwoThirds);

            Assert.AreEqual("casual_a", exact.Selection.SelectedScenario.scenarioId);
            Assert.AreEqual("casual_b", rounded.Selection.SelectedScenario.scenarioId,
                "This proves why the Editor control must preserve its supplied double near a weight boundary.");
            Assert.AreEqual(drawImmediatelyBelowTwoThirds, exact.Selection.Draw);
        }

        [Test]
        public void CursorMovementIsNotDeliveryAndCheckpointCancellationRemainsConsumed()
        {
            TopicDefinition family = LoadFamily();
            TopicScenario scenario = family.scenarios.Find(item => item.scenarioId == "station_break");
            var instance = TopicInstance.Create(family, "once-cancel", "generated");
            var history = new TopicHistory();
            var harness = new TopicPlaygroundTopicHarness(history);
            var context = new TopicContext { localHour = -1, objectsComplete = true };
            context.stats.TrySet("alterra.hydration", 20f, out _);
            var dispenser = new TopicObjectSnapshot { id = "dispenser-a", kind = "dispenser" };
            dispenser.flags.Add("drinkable", true);
            dispenser.numbers.Add("water_level", 1d);
            context.objects.Add(dispenser);

            TopicPlaygroundTopicRun run = harness.Select(family, instance, context, Now, 0.2d);
            Assert.AreEqual(scenario.scenarioId, run.Selection.SelectedScenario.scenarioId);
            Assert.IsFalse(history.IsConsumed(instance.instanceId));
            Assert.AreEqual("sip", harness.Continue(run).id);
            Assert.IsFalse(history.IsConsumed(instance.instanceId), "Moving the dialogue cursor is not presentation acknowledgement.");
            TopicPlaybackResult start = harness.Present(run, "start", Now);
            Assert.AreEqual("Applied", start.Status);
            Assert.IsFalse(start.IsConsumed);
            Assert.IsFalse(history.IsConsumed(instance.instanceId));
            TopicPlaybackResult checkpoint = harness.Present(run, "sip", Now.AddSeconds(1));
            Assert.AreEqual("Applied", checkpoint.Status);
            Assert.IsTrue(checkpoint.IsConsumed);

            harness.Cancel(run);
            Assert.IsTrue(history.IsConsumed(instance.instanceId), "Cancelling cannot restore a consumed occurrence.");
            TopicPlaygroundTopicRun sameOccurrence = harness.Select(family, instance, context, Now.AddSeconds(2), 0d);
            Assert.IsNull(sameOccurrence.Selection.SelectedScenario);
            TopicPlaygroundTopicRun nextOccurrence = harness.Select(
                family, TopicInstance.Create(family, "once-cancel-next", "generated"), context,
                Now.AddSeconds(2), 0d);
            Assert.AreEqual(scenario.scenarioId, nextOccurrence.Selection.SelectedScenario.scenarioId);
        }

        [Test]
        public void RepeatableCooldownStartsAtCompletionAndOnlyThenBlocksSameOccurrence()
        {
            TopicDefinition family = LoadFamily();
            family.cooldownSeconds = 60d;
            var instance = TopicInstance.Create(family, "repeat-cooldown", "authored");
            var context = new TopicContext { localHour = -1, objectsComplete = false };
            var harness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun run = harness.Select(family, instance, context, Now, 0d);
            Assert.IsNotNull(run.Selection.SelectedScenario);
            harness.Present(run, "start", Now);
            TopicPlaybackResult completion = harness.Complete(run, Now.AddSeconds(10));
            Assert.AreEqual("Applied", completion.Status);
            Assert.IsFalse(completion.IsConsumed, "Repeatable completion must not consume the occurrence.");

            TopicPlaygroundTopicRun cooling = harness.Select(family, instance, context, Now.AddSeconds(69), 0d);
            Assert.IsNull(cooling.Selection.SelectedScenario);
            Assert.AreEqual("CoolingDown", cooling.Selection.Candidates[0].Reason);
            TopicPlaygroundTopicRun ready = harness.Select(family, instance, context, Now.AddSeconds(70), 0d);
            Assert.IsNotNull(ready.Selection.SelectedScenario);
        }

        [Test]
        public void FailureBeforeCheckpointDoesNotConsumeAndAllowsRetry()
        {
            TopicDefinition family = LoadFamily();
            var instance = TopicInstance.Create(family, "failed-attempt", "generated");
            var context = new TopicContext { localHour = -1, objectsComplete = true };
            context.stats.TrySet("alterra.hydration", 20f, out _);
            context.objects.Add(Dispenser("station", true, 1d));
            var harness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun run = harness.Select(family, instance, context, Now, 0d);
            Assert.AreEqual("Applied", harness.Present(run, "start", Now).Status);
            Assert.AreEqual("Applied", harness.Fail(run).Status);
            Assert.IsFalse(harness.History.IsConsumed(instance.instanceId));

            TopicPlaygroundTopicRun retry = harness.Select(family, instance, context, Now.AddSeconds(1), 0d);
            Assert.AreEqual("station_break", retry.Selection.SelectedScenario.scenarioId);
        }

        [Test]
        public void ObjectGateCannotCombineTwoDispenserSnapshotsAndExplainsUnknown()
        {
            TopicDefinition family = LoadFamily();
            var instance = TopicInstance.Create(family, "same-object-check", "authored");
            var context = new TopicContext { localHour = -1, objectsComplete = true };
            context.stats.TrySet("alterra.hydration", 20f, out _);
            TopicObjectSnapshot first = Dispenser("a", true, 0d);
            TopicObjectSnapshot second = Dispenser("b", false, 3d);
            context.objects.Add(first);
            context.objects.Add(second);

            TopicSelectionResult mismatch = TopicScenarioSelector.Select(
                family, instance, context, new TopicHistory(), Now, 0d);
            Assert.AreEqual("hydration_prompt", mismatch.SelectedScenario.scenarioId);
            TopicCandidateTrace station = Candidate(mismatch, "station_break");
            Assert.AreEqual("station_break", station.ScenarioId);
            Assert.AreEqual(TopicTruth.False, station.Gate.Truth);
            Assert.IsNull(station.Gate.BoundObjectId);
            Assert.AreEqual(2, station.Gate.Children[1].Children.Count);
            Assert.AreEqual("a", station.Gate.Children[1].Children[0].BoundObjectId);
            Assert.AreEqual("b", station.Gate.Children[1].Children[1].BoundObjectId);

            context.objectsComplete = false;
            TopicSelectionResult incompleteInventory = TopicScenarioSelector.Select(
                family, instance, context, new TopicHistory(), Now, 0d);
            Assert.AreEqual(TopicTruth.Unknown, Candidate(incompleteInventory, "station_break").Gate.Truth);
            Assert.AreEqual("hydration_prompt", incompleteInventory.SelectedScenario.scenarioId);
        }

        [Test]
        public void UnknownHydrationDoesNotActLikeZeroForTheSameScenarioGate()
        {
            TopicDefinition family = LoadFamily();
            var inputStats = new[]
            {
                new SyntheticStatInput { statId = "alterra.hydration", isKnown = false },
            };
            var objects = new[]
            {
                new SyntheticObjectInput
                {
                    id = "station", kind = "dispenser",
                    flags = new List<SyntheticObjectFlagInput>
                    {
                        new SyntheticObjectFlagInput { key = "drinkable", isKnown = true, value = true },
                    },
                    numbers = new List<SyntheticObjectNumberInput>
                    {
                        new SyntheticObjectNumberInput { key = "water_level", isKnown = true, value = 1d },
                    },
                },
            };
            TopicPlaygroundContext unknownContext;
            TopicPlaygroundContext zeroContext;
            List<string> errors;
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(-1, inputStats, string.Empty, null, true,
                objects, out unknownContext, out errors), string.Join(" ", errors));
            Assert.IsTrue(TopicPlaygroundContextBuilder.TryBuild(-1,
                new[] { new SyntheticStatInput { statId = "alterra.hydration", isKnown = true, value = 0f } },
                string.Empty, null, true, objects, out zeroContext, out errors), string.Join(" ", errors));

            TopicPlaygroundTopicRun unknown = new TopicPlaygroundTopicHarness().Select(
                family, TopicInstance.Create(family, "unknown-hydration", "authored"),
                TopicPlaygroundTopicContextAdapter.Create(unknownContext), Now, 0d);
            TopicPlaygroundTopicRun zero = new TopicPlaygroundTopicHarness().Select(
                family, TopicInstance.Create(family, "zero-hydration", "authored"),
                TopicPlaygroundTopicContextAdapter.Create(zeroContext), Now, 0d);
            Assert.AreNotEqual("station_break", unknown.Selection.SelectedScenario.scenarioId);
            Assert.AreEqual(TopicTruth.Unknown, Candidate(unknown.Selection, "station_break").Gate.Truth);
            Assert.AreEqual("station_break", zero.Selection.SelectedScenario.scenarioId);
        }

        [Test]
        public void PriorityWeightsAndModifierTraceAreVisibleInSharedHarness()
        {
            TopicDefinition family = LoadFamily();
            var context = new TopicContext { localHour = -1, objectsComplete = true };
            context.stats.TrySet("alterra.hydration", 50f, out _);
            var harness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun weighted = harness.Select(
                family, TopicInstance.Create(family, "weight-draw", "authored"), context, Now, 0.7d);
            Assert.AreEqual("casual_b", weighted.Selection.SelectedScenario.scenarioId);
            Assert.AreEqual(1, weighted.Selection.WinningPriority);
            Assert.AreEqual(0.7d, weighted.Selection.Draw);
            Assert.AreEqual("Selected", Candidate(weighted.Selection, "casual_b").Reason);

            harness.Cancel(weighted);
            context.stats.TrySet("alterra.hydration", 20f, out _);
            TopicObjectSnapshot dispenser = Dispenser("bottle-station", true, 2d);
            context.objects.Add(dispenser);
            context.facts.Add("user_confirmed.has_bottle", true);
            TopicPlaygroundTopicRun modified = harness.Select(
                family, TopicInstance.Create(family, "modifier-check", "authored"), context, Now, 0d);
            Assert.AreEqual("station_break", modified.Selection.SelectedScenario.scenarioId);
            TopicWeightTrace modifier = Candidate(modified.Selection, "station_break").Modifiers[0];
            Assert.AreEqual(TopicTruth.True, modifier.Truth);
            Assert.AreEqual(1d, modifier.AuthoredAmount);
            Assert.AreEqual(1d, modifier.AppliedAmount);
            Assert.AreEqual(2d, Candidate(modified.Selection, "station_break").EffectiveWeight);
        }

        [Test]
        public void FailedScratchSaveLeavesHarnessHistoryPending()
        {
            TopicDefinition family = LoadFamily();
            var instance = TopicInstance.Create(family, "pending-save", "generated");
            var context = new TopicContext { localHour = -1, objectsComplete = false };
            context.stats.TrySet("alterra.hydration", 20f, out _);
            context.objectsComplete = true;
            context.objects.Add(Dispenser("station", true, 1d));
            var harness = new TopicPlaygroundTopicHarness();
            TopicPlaygroundTopicRun run = harness.Select(family, instance, context, Now, 0d);
            harness.Present(run, "start", Now);
            harness.Present(run, "sip", Now.AddSeconds(1));
            TopicHistorySaveResult failed = TopicHistoryRepository.Save(Path.GetTempPath(), harness.History);
            Assert.IsFalse(failed.Success);
            Assert.IsTrue(harness.History.IsDirty);
            Assert.IsTrue(harness.History.IsConsumed(instance.instanceId));
        }

        [Test]
        public void SemanticFamilyMatcherRejectsNonfiniteAndRanksCosineDeterministically()
        {
            var anchors = new[]
            {
                new TopicFamilyAnchorVector { TopicId = "state.rest", Anchor = "tired", Embedding = new[] { 0f, 2f } },
                new TopicFamilyAnchorVector { TopicId = "beverage.drink", Anchor = "water", Embedding = new[] { 3f, 0f } },
                new TopicFamilyAnchorVector { TopicId = "beverage.drink", Anchor = "drink", Embedding = new[] { 1f, 0f } },
            };

            TopicFamilySemanticMatch[] ranked = TopicFamilySemanticMatcher.Rank(new[] { 4f, 0f }, anchors, 0.8f);
            Assert.AreEqual("beverage.drink", ranked[0].TopicId);
            Assert.AreEqual("drink", ranked[0].BestAnchor, "Equal cosine anchors use ordinal text order.");
            Assert.AreEqual(2, ranked[0].SupportCount);
            Assert.AreEqual("state.rest", ranked[1].TopicId);
            Assert.AreEqual(0, ranked[1].SupportCount);

            Assert.Throws<ArgumentException>(() => TopicFamilySemanticMatcher.Rank(
                new[] { float.NaN, 1f }, anchors));
        }

        [Test]
        public void UnsupportedScenarioModeIsRejectedInsteadOfFallingThroughToGenericRouting()
        {
            string normalized;
            Assert.IsTrue(TopicPlaygroundScenario.TryNormalizeTopicMode(string.Empty, out normalized));
            Assert.AreEqual("generic-route", normalized);
            Assert.IsFalse(TopicPlaygroundScenario.TryNormalizeTopicMode("semantic-ish", out normalized));
            Assert.IsTrue(TopicPlaygroundScenario.TryNormalizeTopicMode("direct-family", out normalized));
        }

        private static TopicObjectSnapshot Dispenser(string id, bool drinkable, double waterLevel)
        {
            var snapshot = new TopicObjectSnapshot { id = id, kind = "dispenser" };
            snapshot.flags.Add("drinkable", drinkable);
            snapshot.numbers.Add("water_level", waterLevel);
            return snapshot;
        }

        private static TopicCandidateTrace Candidate(TopicSelectionResult selection, string scenarioId)
        {
            foreach (TopicCandidateTrace candidate in selection.Candidates)
                if (candidate.ScenarioId == scenarioId)
                    return candidate;
            Assert.Fail("Missing candidate trace for " + scenarioId);
            return null;
        }

        private static void AssertConditionEqual(TopicConditionResult left, TopicConditionResult right)
        {
            Assert.AreEqual(left.Truth, right.Truth);
            Assert.AreEqual(left.Reason, right.Reason);
            Assert.AreEqual(left.BoundObjectId, right.BoundObjectId);
            Assert.AreEqual(left.Children.Count, right.Children.Count);
            for (int index = 0; index < left.Children.Count; index++)
                AssertConditionEqual(left.Children[index], right.Children[index]);
        }

        private static TopicDefinition LoadFamily()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Tools", "TopicPlayground", "Fixtures",
                "topic-runtime-v1", "Topics", "beverage.drink.json");
            TopicDefinition family = Deserialize<TopicDefinition>(File.ReadAllText(path));
            List<string> errors = TopicDefinitionValidator.Validate(family);
            Assert.AreEqual(0, errors.Count, string.Join(" ", errors));
            return family;
        }

        private static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
