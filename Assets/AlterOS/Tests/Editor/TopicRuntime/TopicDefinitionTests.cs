using System;
using System.Collections;
using AlterOS.Dialogue;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicDefinitionTests
    {
        [Test] public void OneScenarioUsesTheSharedAnchorBank()
        {
            dynamic family = TopicTestData.Family();
            Assert.AreEqual(0, TopicTestData.Errors(family).Count);
            family.scenarios.Add(TopicTestData.Scenario("b", 10, 3));
            Assert.AreEqual(0, TopicTestData.Errors(family).Count);
        }

        [Test] public void BadVersionEmptyAnchorsAndDuplicateScenariosAreRejected()
        {
            dynamic family = TopicTestData.Family(); family.schemaVersion = 99;
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
            family = TopicTestData.Family(); family.anchors.Clear();
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
            family = TopicTestData.Family(); family.scenarios.Add(TopicTestData.Scenario("a", 0, 1));
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
        }

        [Test] public void InvalidGraphsAndCheckpointsAreRejected()
        {
            dynamic family = TopicTestData.Family(); family.scenarios[0].nodes[0].nextId = "missing";
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
            family = TopicTestData.Family(); family.scenarios[0].consumptionNodeId = "missing";
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
            family = TopicTestData.Family();
            family.scenarios[0].nodes.Add(new CanonicalDialogueNode { id = "start", text = "Duplicate" });
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
        }

        [Test] public void InvalidWeightsReplayAndConditionsCannotBecomeEligible()
        {
            foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
            {
                dynamic family = TopicTestData.Family(); family.scenarios[0].baseWeight = invalid;
                Assert.Greater(TopicTestData.Errors(family).Count, 0);
            }
            dynamic bad = TopicTestData.Family(); bad.defaultReplayPolicy = "mystery";
            Assert.Greater(TopicTestData.Errors(bad).Count, 0);
            bad = TopicTestData.Family(); bad.scenarios[0].condition = TopicTestData.Stat("alterra.hydration", "invented", 50);
            Assert.Greater(TopicTestData.Errors(bad).Count, 0);
        }

        [Test] public void BooleanExpectationCannotSilentlyNegateNumericConditions()
        {
            dynamic family = TopicTestData.Family();
            dynamic rule = TopicTestData.Stat("alterra.hydration", "at_most", 30);
            rule.expected = false; family.scenarios[0].condition = rule;
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
        }

        [Test] public void DeepOrCyclicConditionsAreRejectedBeforeEvaluation()
        {
            dynamic family = TopicTestData.Family(); dynamic cycle = TopicTestData.Rule("not");
            cycle.children.Add(cycle); family.scenarios[0].condition = cycle;
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
            dynamic wide = TopicTestData.Rule("all");
            for (int i = 0; i < 256; i++) wide.children.Add(TopicTestData.Rule("fact"));
            family.scenarios[0].condition = wide;
            Assert.Greater(TopicTestData.Errors(family).Count, 0);
        }
    }
}
