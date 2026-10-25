using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicScenarioSelectionTests
    {
        internal static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

        private static dynamic Select(object family, object context, double draw = 0)
        {
            object instance = TopicTestData.Call("TopicInstance", "Create", family, "occurrence-a", "authored", null);
            return TopicTestData.Call("TopicScenarioSelector", "Select", family, instance, context, TopicTestData.New("TopicHistory"), Now, draw);
        }

        [Test] public void HigherEligiblePriorityAlwaysOverridesHugeLowerWeight()
        {
            dynamic family = TopicTestData.Family(); family.scenarios[0].baseWeight = 9999d;
            family.scenarios.Add(TopicTestData.Scenario("special", 10, 1));
            Assert.AreEqual("special", Select(family, TopicTestData.New("TopicContext"), .999).SelectedScenario.scenarioId);
        }

        [Test] public void IneligibleOrZeroWeightHighTierDoesNotBlockLowerTier()
        {
            dynamic family = TopicTestData.Family(); dynamic special = TopicTestData.Scenario("special", 10, 1);
            special.condition = TopicTestData.Stat("alterra.hydration", "at_most", 10);
            family.scenarios.Add(special);
            Assert.AreEqual("a", Select(family, TopicTestData.New("TopicContext")).SelectedScenario.scenarioId);
            special.condition = null; special.baseWeight = 0d;
            Assert.AreEqual("a", Select(family, TopicTestData.New("TopicContext")).SelectedScenario.scenarioId);
        }

        [Test] public void SameTierDrawsUseStableRelativeWeights()
        {
            dynamic family = TopicTestData.Family(); family.scenarios[0].baseWeight = 3d;
            family.scenarios.Add(TopicTestData.Scenario("b", 0, 1));
            dynamic context = TopicTestData.New("TopicContext");
            Assert.AreEqual("a", Select(family, context, 0).SelectedScenario.scenarioId);
            Assert.AreEqual("a", Select(family, context, .749).SelectedScenario.scenarioId);
            Assert.AreEqual("b", Select(family, context, .75).SelectedScenario.scenarioId);
            family.scenarios.Reverse();
            Assert.AreEqual("b", Select(family, context, .999).SelectedScenario.scenarioId);
        }

        [Test] public void UnknownModifierDoesNothingAndKnownModifierIsExplained()
        {
            dynamic family = TopicTestData.Family(); dynamic modifier = TopicTestData.New("TopicWeightModifier");
            modifier.condition = TopicTestData.Stat("alterra.hydration", "at_most", 20); modifier.amount = 4d;
            family.scenarios[0].modifiers.Add(modifier);
            dynamic context = TopicTestData.New("TopicContext");
            dynamic result = Select(family, context);
            Assert.AreEqual(1d, result.Candidates[0].EffectiveWeight);
            Assert.AreEqual("Unknown", result.Candidates[0].Modifiers[0].Truth.ToString());
            string error; context.stats.TrySet("alterra.hydration", 10f, out error);
            result = Select(family, context);
            Assert.AreEqual(5d, result.Candidates[0].EffectiveWeight);
            Assert.AreEqual(1d, family.scenarios[0].baseWeight);
        }

        [Test] public void AllZeroReturnsNoSelectionAndInvalidDrawsAreRejected()
        {
            dynamic family = TopicTestData.Family(); family.scenarios[0].baseWeight = 0d;
            dynamic context = TopicTestData.New("TopicContext");
            Assert.IsNull(Select(family, context).SelectedScenario);
            foreach (double invalid in new[] { -.1, 1d, double.NaN, double.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => { Select(family, context, invalid); });
        }

        [Test] public void GeneratedDefaultsToOnceAndAuthoredToRepeatable()
        {
            dynamic family = TopicTestData.Family();
            Assert.AreEqual("once", TopicTestData.Call("TopicInstance", "Create", family, "g", "generated", null).replayPolicy);
            Assert.AreEqual("repeatable", TopicTestData.Call("TopicInstance", "Create", family, "a", "authored", null).replayPolicy);
        }
    }
}
