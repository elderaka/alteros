using System;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using AlterOS.Dialogue;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    internal static class TopicTestData
    {
        internal static Type Type(string name)
        {
            var type = typeof(DialogueDefinition).Assembly.GetType("AlterOS.Topics." + name);
            Assert.IsNotNull(type, "Missing production type: " + name);
            return type;
        }

        internal static dynamic New(string name) { return Activator.CreateInstance(Type(name)); }

        internal static dynamic Call(string type, string method, params object[] args)
        {
            try { return Type(type).GetMethod(method).Invoke(null, args); }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        internal static dynamic Family()
        {
            dynamic definition = New("TopicDefinition");
            definition.topicId = "beverage.drink";
            definition.anchors.Add("Want a drink?");
            definition.menuLabel = "A drink together";
            definition.prompt = "Talking about having a drink together.";
            definition.scenarios.Add(Scenario("a", 0, 1));
            return definition;
        }

        internal static dynamic Scenario(string id, int priority, double weight)
        {
            dynamic scenario = New("TopicScenario");
            scenario.scenarioId = id; scenario.priority = priority; scenario.baseWeight = weight;
            scenario.nodes.Add(new CanonicalDialogueNode { id = "start", text = "A drink sounds nice." });
            return scenario;
        }

        internal static dynamic Rule(string kind, params object[] children)
        {
            dynamic rule = New("TopicCondition"); rule.kind = kind;
            foreach (object child in children) ((IList)rule.children).Add(child);
            return rule;
        }

        internal static dynamic Stat(string id, string operation, float threshold)
        {
            dynamic rule = Rule("stat"); rule.statId = id; rule.operation = operation; rule.value = threshold;
            return rule;
        }

        internal static IList Errors(object definition) { return (IList)Call("TopicDefinitionValidator", "Validate", definition); }
    }
}
