using System;
using AlterOS.Stats;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicConditionTests
    {
        private static string Truth(object rule, object context)
        { return TopicTestData.Call("TopicConditionEvaluator", "Evaluate", rule, context).Truth.ToString(); }

        [Test] public void UnknownNegationNeverBecomesTrueAndZeroIsKnown()
        {
            dynamic context = TopicTestData.New("TopicContext");
            dynamic rule = TopicTestData.Stat("alterra.hydration", "at_most", 0);
            Assert.AreEqual("Unknown", Truth(rule, context));
            Assert.AreEqual("Unknown", Truth(TopicTestData.Rule("not", rule), context));
            string error; ((StatSnapshot)context.stats).TrySet("alterra.hydration", 0, out error);
            Assert.AreEqual("True", Truth(rule, context));
        }

        [Test] public void AllAndAnyUseThreeValuedTruthTables()
        {
            dynamic context = TopicTestData.New("TopicContext"); context.facts["yes"] = true; context.facts["no"] = false;
            dynamic yes = TopicTestData.Rule("fact"); yes.key = "yes";
            dynamic no = TopicTestData.Rule("fact"); no.key = "no";
            dynamic unknown = TopicTestData.Rule("fact"); unknown.key = "missing";
            Assert.AreEqual("Unknown", Truth(TopicTestData.Rule("all", yes, unknown), context));
            Assert.AreEqual("False", Truth(TopicTestData.Rule("all", no, unknown), context));
            Assert.AreEqual("True", Truth(TopicTestData.Rule("any", yes, unknown), context));
            Assert.AreEqual("Unknown", Truth(TopicTestData.Rule("any", no, unknown), context));
            Assert.AreEqual("True", Truth(TopicTestData.Rule("not", no), context));
            Assert.AreEqual("False", Truth(TopicTestData.Rule("not", yes), context));
        }

        [Test] public void TimeWindowsRespectBoundariesAndUnknownHour()
        {
            dynamic context = TopicTestData.New("TopicContext");
            dynamic rule = TopicTestData.Rule("timeWindow"); rule.startHour = 20; rule.endHour = 6;
            Assert.AreEqual("Unknown", Truth(rule, context));
            context.localHour = 20; Assert.AreEqual("True", Truth(rule, context));
            context.localHour = 5; Assert.AreEqual("True", Truth(rule, context));
            context.localHour = 6; Assert.AreEqual("False", Truth(rule, context));
        }

        [Test] public void KnownEmptyAndUnobservedInventoryHaveDifferentMeaning()
        {
            dynamic context = TopicTestData.New("TopicContext");
            dynamic rule = TopicTestData.Rule("object"); rule.objectKind = "dispenser";
            Assert.AreEqual("Unknown", Truth(rule, context));
            context.objectsComplete = true; Assert.AreEqual("False", Truth(rule, context));
        }

        [Test] public void ObjectPropertiesCannotBeCombinedAcrossDifferentDispensers()
        {
            dynamic context = TopicTestData.New("TopicContext"); context.objectsComplete = true;
            dynamic first = TopicTestData.New("TopicObjectSnapshot"); first.id = "a"; first.kind = "dispenser";
            first.flags["expensive"] = true; first.flags["empty"] = false;
            dynamic second = TopicTestData.New("TopicObjectSnapshot"); second.id = "b"; second.kind = "dispenser";
            second.flags["expensive"] = false; second.flags["empty"] = true;
            context.objects.Add(first); context.objects.Add(second);
            dynamic expensive = TopicTestData.Rule("objectFlag"); expensive.key = "expensive";
            dynamic empty = TopicTestData.Rule("objectFlag"); empty.key = "empty";
            dynamic rule = TopicTestData.Rule("object", expensive, empty); rule.objectKind = "dispenser";
            Assert.AreEqual("False", Truth(rule, context));
            second.flags["expensive"] = true;
            dynamic result = TopicTestData.Call("TopicConditionEvaluator", "Evaluate", rule, context);
            Assert.AreEqual("True", result.Truth.ToString()); Assert.AreEqual("b", result.BoundObjectId);
        }

        [Test] public void MissingObjectPropertyIsUnknownAndBindingsAreStable()
        {
            dynamic context = TopicTestData.New("TopicContext"); context.objectsComplete = true;
            dynamic first = TopicTestData.New("TopicObjectSnapshot"); first.id = "z"; first.kind = "dispenser";
            context.objects.Add(first);
            dynamic property = TopicTestData.Rule("objectNumber"); property.key = "water"; property.operation = "at_least"; property.value = 1f;
            dynamic rule = TopicTestData.Rule("object", property); rule.objectKind = "dispenser";
            Assert.AreEqual("Unknown", Truth(rule, context));
            first.numbers["water"] = 5d;
            dynamic second = TopicTestData.New("TopicObjectSnapshot"); second.id = "a"; second.kind = "dispenser"; second.numbers["water"] = 2d;
            context.objects.Add(second);
            Assert.AreEqual("a", TopicTestData.Call("TopicConditionEvaluator", "Evaluate", rule, context).BoundObjectId);
        }

        [Test] public void InvalidTreesAndContextAreErrorsNotUnknownFacts()
        {
            dynamic context = TopicTestData.New("TopicContext");
            Assert.Throws<ArgumentException>(() => { Truth(TopicTestData.Rule("unknownKind"), context); });
            Assert.Throws<ArgumentException>(() => { Truth(TopicTestData.Rule("all"), context); });
            dynamic not = TopicTestData.Rule("not"); not.children.Add(not);
            Assert.Throws<ArgumentException>(() => { Truth(not, context); });
            context.localHour = 24;
            Assert.Throws<ArgumentException>(() => { Truth(null, context); });
        }
    }
}
