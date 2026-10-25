using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace AlterOS.Tests
{
    public class TopicPlaygroundStatContractTests
    {
        [Test]
        public void SyntheticInputsKeepUnknownSeparateFromExplicitZero()
        {
            object unknown = Input("alterra.hydration", false, 0f);
            object zero = Input("alterra.emotion.valence", true, 0f);

            object snapshot = Build(unknown, zero);

            float value;
            Assert.IsFalse(StatsTestAccess.TryGet(snapshot, "alterra.hydration", out value));
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, "alterra.emotion.valence", out value));
            Assert.AreEqual(0f, value);
        }

        [Test]
        public void SyntheticInputsAcceptCatalogueBoundariesWithoutClamping()
        {
            object snapshot = Build(
                Input("alterra.energy", true, 0f),
                Input("alterra.hydration", true, 100f),
                Input("alterra.emotion.valence", true, -1f),
                Input("alterra.emotion.control", true, 1f));

            AssertValue(snapshot, "alterra.energy", 0f);
            AssertValue(snapshot, "alterra.hydration", 100f);
            AssertValue(snapshot, "alterra.emotion.valence", -1f);
            AssertValue(snapshot, "alterra.emotion.control", 1f);
        }

        [Test]
        public void InvalidSyntheticInputFailsWithoutPublishingPartialSnapshot()
        {
            object[] arguments = BuildArguments(
                Input("alterra.energy", true, 70f),
                Input("alterra.hydration", true, float.NaN));

            bool succeeded = InvokeBuild(arguments);

            Assert.IsFalse(succeeded);
            Assert.IsNotEmpty((IList)arguments[2]);
            float value;
            Assert.IsFalse(StatsTestAccess.TryGet(arguments[1], "alterra.energy", out value));
            Assert.IsFalse(StatsTestAccess.TryGet(arguments[1], "alterra.hydration", out value));
        }

        [Test]
        public void MissingHydrationRejectsConditionWhileKnownZeroCanSatisfyIt()
        {
            object eligibility = Eligibility(Condition("alterra.hydration", "at_most", 25f));
            object missing = Build();
            object zero = Build(Input("alterra.hydration", true, 0f));

            object missingResult = Evaluate(eligibility, missing);
            object zeroResult = Evaluate(eligibility, zero);

            Assert.AreEqual("Rejected", Property(missingResult, "Status").ToString());
            Assert.AreEqual("UnknownValue", Property(First(Property(missingResult, "Conditions")), "Outcome").ToString());
            Assert.AreEqual("Eligible", Property(zeroResult, "Status").ToString());
            Assert.AreEqual("Satisfied", Property(First(Property(zeroResult, "Conditions")), "Outcome").ToString());
        }

        [Test]
        public void NeedAndEmotionThresholdBoundariesAreInclusive()
        {
            AssertEvaluation("alterra.hydration", "at_most", 25f, 25f, "Eligible");
            AssertEvaluation("alterra.hydration", "at_most", 25f, 26f, "Rejected");
            AssertEvaluation("alterra.hydration", "at_least", 75f, 75f, "Eligible");
            AssertEvaluation("alterra.hydration", "at_least", 75f, 74f, "Rejected");
            AssertEvaluation("alterra.emotion.valence", "at_most", -0.5f, -0.5f, "Eligible");
            AssertEvaluation("alterra.emotion.control", "at_least", 0.5f, 0.5f, "Eligible");
        }

        [Test]
        public void AllConditionsMustPassAndFailedConditionExplainsTheValue()
        {
            object eligibility = Eligibility(
                Condition("alterra.hydration", "at_most", 25f),
                Condition("alterra.emotion.valence", "at_most", -0.5f));
            object snapshot = Build(
                Input("alterra.hydration", true, 20f),
                Input("alterra.emotion.valence", true, 0f));

            object evaluation = Evaluate(eligibility, snapshot);
            IList conditions = (IList)Property(evaluation, "Conditions");

            Assert.AreEqual("Rejected", Property(evaluation, "Status").ToString());
            Assert.AreEqual(2, conditions.Count);
            Assert.AreEqual("Satisfied", Property(conditions[0], "Outcome").ToString());
            Assert.AreEqual("Failed", Property(conditions[1], "Outcome").ToString());
            StringAssert.Contains("0", (string)Property(conditions[1], "Reason"));
            StringAssert.Contains("-0.5", (string)Property(conditions[1], "Reason"));
        }

        [Test]
        public void UnsupportedVersionOperatorAndPlayerSignalAreVisible()
        {
            object unsupportedVersion = Eligibility(Condition("alterra.hydration", "at_most", 25f));
            unsupportedVersion.GetType().GetField("schemaVersion").SetValue(unsupportedVersion, 2);
            object unknownOperator = Eligibility(Condition("alterra.hydration", "equals", 25f));
            object playerSignal = Eligibility(Condition("player.stress", "at_most", 25f));
            object snapshot = Build(Input("alterra.hydration", true, 20f));

            Assert.AreEqual("Unsupported", Property(Evaluate(unsupportedVersion, snapshot), "Status").ToString());
            Assert.AreEqual("Unsupported", Property(Evaluate(unknownOperator, snapshot), "Status").ToString());
            Assert.AreEqual("Unsupported", Property(Evaluate(playerSignal, snapshot), "Status").ToString());
        }

        [Test]
        public void UnsupportedAnyRuleIsNotSilentlyIgnored()
        {
            object eligibility = Eligibility(Condition("alterra.hydration", "at_most", 25f));
            Type conditionType = StatsTestAccess.Find("AlterOS.Stats.CanonicalStatCondition");
            Type listType = typeof(List<>).MakeGenericType(conditionType);
            IList any = (IList)Activator.CreateInstance(listType);
            any.Add(Condition("alterra.hydration", "at_least", 75f));
            eligibility.GetType().GetField("any").SetValue(eligibility, any);
            IList none = (IList)Activator.CreateInstance(listType);
            none.Add(Condition("alterra.hydration", "at_least", 75f));
            eligibility.GetType().GetField("none").SetValue(eligibility, none);
            eligibility.GetType().GetField("not").SetValue(
                eligibility,
                Eligibility(Condition("alterra.hydration", "at_least", 75f)));

            object result = Evaluate(eligibility, Build(Input("alterra.hydration", true, 20f)));

            Assert.AreEqual("Unsupported", Property(result, "Status").ToString());
            StringAssert.Contains("only the versioned 'all' list", (string)Property(result, "Reason"));
            IList conditions = (IList)Property(result, "Conditions");
            Assert.AreEqual(4, conditions.Count);
            Assert.AreEqual("Unsupported", Property(conditions[1], "Outcome").ToString());
            Assert.AreEqual("Unsupported", Property(conditions[2], "Outcome").ToString());
            Assert.AreEqual("Unsupported", Property(conditions[3], "Outcome").ToString());
        }

        [Test]
        public void NonFiniteOrOutOfBoundsConditionThresholdIsInvalid()
        {
            object nonFinite = Eligibility(Condition("alterra.hydration", "at_most", float.PositiveInfinity));
            object outOfRange = Eligibility(Condition("alterra.emotion.valence", "at_most", 1.01f));
            object snapshot = Build(Input("alterra.hydration", true, 20f), Input("alterra.emotion.valence", true, 0f));

            Assert.AreEqual("Invalid", Property(Evaluate(nonFinite, snapshot), "Status").ToString());
            Assert.AreEqual("Invalid", Property(Evaluate(outOfRange, snapshot), "Status").ToString());
        }

        [Test]
        public void UnsupportedLegacyContextTagsDoNotPassSilently()
        {
            Type routingType = StatsTestAccess.Find("AlterOS.Dialogue.DialogueRouting");
            object routing = Activator.CreateInstance(routingType);
            IList required = (IList)routingType.GetField("requiredContext").GetValue(routing);
            required.Add("night");
            object evaluation = EvaluateCanonical(routing, Build());

            Assert.AreEqual("Unsupported", Property(evaluation, "Status").ToString());
            StringAssert.Contains("requiredContext", (string)Property(evaluation, "Reason"));
        }

        [Test]
        public void ScenarioAndEditorControlInputsUseEquivalentSnapshotValidation()
        {
            object[] editorValues =
            {
                Input("alterra.hydration", true, 20f),
                Input("alterra.emotion.valence", false, 0f)
            };
            object[] scenarioValues =
            {
                Input("alterra.hydration", true, 20f),
                Input("alterra.emotion.valence", false, 0f)
            };

            object editorSnapshot = Build(editorValues);
            object scenarioSnapshot = Build(scenarioValues);
            object eligibility = Eligibility(Condition("alterra.hydration", "at_most", 25f));

            Assert.AreEqual(Property(Evaluate(eligibility, editorSnapshot), "Status").ToString(),
                Property(Evaluate(eligibility, scenarioSnapshot), "Status").ToString());
            AssertValue(editorSnapshot, "alterra.hydration", 20f);
            AssertValue(scenarioSnapshot, "alterra.hydration", 20f);
            float ignored;
            Assert.IsFalse(StatsTestAccess.TryGet(editorSnapshot, "alterra.emotion.valence", out ignored));
            Assert.IsFalse(StatsTestAccess.TryGet(scenarioSnapshot, "alterra.emotion.valence", out ignored));
        }

        [Test]
        public void PlaygroundContextKeepsUnknownHourAndClonesSyntheticStats()
        {
            Type inputType = StatsTestAccess.Find("AlterOS.Stats.SyntheticStatInput");
            Type listType = typeof(List<>).MakeGenericType(inputType);
            IList inputs = (IList)Activator.CreateInstance(listType);
            inputs.Add(Input("alterra.hydration", true, 0f));
            Type builder = StatsTestAccess.Find("AlterOS.Stats.TopicPlaygroundContextBuilder");
            object[] arguments = { -1, inputs, null, null };

            bool succeeded = (bool)FindContextBuilderMethod(builder, 4).Invoke(null, arguments);
            object context = arguments[2];
            object copy = context.GetType().GetMethod("CopyStats").Invoke(context, null);
            string error;
            Assert.IsTrue(succeeded, ListText(arguments[3]));
            Assert.AreEqual(-1, Property(context, "LocalHour"));
            AssertValue(copy, "alterra.hydration", 0f);
            Assert.IsTrue(StatsTestAccess.TrySet(copy, "alterra.hydration", 80f, out error), error);
            AssertValue(context, "alterra.hydration", 0f);
        }

        [Test]
        public void PlaygroundContextRejectsInvalidSyntheticHour()
        {
            Type builder = StatsTestAccess.Find("AlterOS.Stats.TopicPlaygroundContextBuilder");
            object[] arguments = { 24, BuildArguments()[0], null, null };

            bool succeeded = (bool)FindContextBuilderMethod(builder, 4).Invoke(null, arguments);

            Assert.IsFalse(succeeded);
            Assert.IsNull(arguments[2]);
            StringAssert.Contains("localHour", ListText(arguments[3]));
        }

        [Test]
        public void ScenarioAndEditorContextBuilderShareNormalizedValuesAndDiagnostics()
        {
            Type builder = StatsTestAccess.Find("AlterOS.Stats.TopicPlaygroundContextBuilder");
            Type inputType = StatsTestAccess.Find("AlterOS.Stats.SyntheticStatInput");
            Type listType = typeof(List<>).MakeGenericType(inputType);
            IList editorInputs = (IList)Activator.CreateInstance(listType);
            IList scenarioInputs = (IList)Activator.CreateInstance(listType);
            editorInputs.Add(Input("alterra.hydration", true, 20f));
            editorInputs.Add(Input("alterra.emotion.valence", false, 0f));
            scenarioInputs.Add(Input("alterra.hydration", true, 20f));
            scenarioInputs.Add(Input("alterra.emotion.valence", false, 0f));

            object[] editorArguments = { -1, editorInputs, "stat-context-v1", null, null };
            object[] scenarioArguments = { -1, scenarioInputs, "stat-context-v1", null, null };
            System.Reflection.MethodInfo method = FindContextBuilderMethod(builder, 5);

            Assert.IsTrue((bool)method.Invoke(null, editorArguments));
            Assert.IsTrue((bool)method.Invoke(null, scenarioArguments));
            object editorContext = editorArguments[3];
            object scenarioContext = scenarioArguments[3];
            Assert.AreEqual(Property(editorContext, "LocalHour"), Property(scenarioContext, "LocalHour"));
            Assert.AreEqual(Property(editorContext, "FixtureSet"), Property(scenarioContext, "FixtureSet"));
            object editorSnapshot = editorContext.GetType().GetMethod("CopyStats").Invoke(editorContext, null);
            object scenarioSnapshot = scenarioContext.GetType().GetMethod("CopyStats").Invoke(scenarioContext, null);
            Assert.AreEqual(Property(Evaluate(Eligibility(Condition("alterra.hydration", "at_most", 25f)), editorSnapshot), "Status").ToString(),
                Property(Evaluate(Eligibility(Condition("alterra.hydration", "at_most", 25f)), scenarioSnapshot), "Status").ToString());

            editorInputs.Add(Input("alterra.energy", true, float.PositiveInfinity));
            scenarioInputs.Add(Input("alterra.energy", true, float.PositiveInfinity));
            object[] invalidEditor = { -1, editorInputs, "stat-context-v1", null, null };
            object[] invalidScenario = { -1, scenarioInputs, "stat-context-v1", null, null };
            Assert.IsFalse((bool)method.Invoke(null, invalidEditor));
            Assert.IsFalse((bool)method.Invoke(null, invalidScenario));
            CollectionAssert.AreEqual((IList)invalidEditor[4], (IList)invalidScenario[4]);
        }

        [Test]
        public void UnsupportedFixtureSetIsRejectedByTheSharedContextBuilder()
        {
            Type builder = StatsTestAccess.Find("AlterOS.Stats.TopicPlaygroundContextBuilder");
            object[] arguments = { -1, BuildArguments()[0], "unknown-set", null, null };

            bool succeeded = (bool)FindContextBuilderMethod(builder, 5).Invoke(null, arguments);

            Assert.IsFalse(succeeded);
            Assert.IsNull(arguments[3]);
            StringAssert.Contains("fixtureSet", ListText(arguments[4]));
        }

        private static void AssertEvaluation(string statId, string operation, float threshold, float value, string expected)
        {
            object eligibility = Eligibility(Condition(statId, operation, threshold));
            object snapshot = Build(Input(statId, true, value));
            Assert.AreEqual(expected, Property(Evaluate(eligibility, snapshot), "Status").ToString());
        }

        private static object[] BuildArguments(params object[] values)
        {
            Type inputType = StatsTestAccess.Find("AlterOS.Stats.SyntheticStatInput");
            Type listType = typeof(List<>).MakeGenericType(inputType);
            IList inputs = (IList)Activator.CreateInstance(listType);
            foreach (object value in values)
                inputs.Add(value);
            return new[] { (object)inputs, null, null };
        }

        private static bool InvokeBuild(object[] arguments)
        {
            Type builder = StatsTestAccess.Find("AlterOS.Stats.SyntheticStatSnapshotBuilder");
            return (bool)builder.GetMethod("TryBuild").Invoke(null, arguments);
        }

        private static object Build(params object[] values)
        {
            object[] arguments = BuildArguments(values);
            Assert.IsTrue(InvokeBuild(arguments), ListText(arguments[2]));
            return arguments[1];
        }

        private static object Input(string id, bool known, float value)
        {
            Type type = StatsTestAccess.Find("AlterOS.Stats.SyntheticStatInput");
            object input = Activator.CreateInstance(type);
            type.GetField("statId").SetValue(input, id);
            type.GetField("isKnown").SetValue(input, known);
            type.GetField("value").SetValue(input, value);
            return input;
        }

        private static object Condition(string id, string operation, float value)
        {
            Type type = StatsTestAccess.Find("AlterOS.Stats.CanonicalStatCondition");
            object condition = Activator.CreateInstance(type);
            type.GetField("statId").SetValue(condition, id);
            type.GetField("operation").SetValue(condition, operation);
            type.GetField("value").SetValue(condition, value);
            return condition;
        }

        private static object Eligibility(params object[] conditions)
        {
            Type setType = StatsTestAccess.Find("AlterOS.Stats.CanonicalStatEligibility");
            Type conditionType = StatsTestAccess.Find("AlterOS.Stats.CanonicalStatCondition");
            Type listType = typeof(List<>).MakeGenericType(conditionType);
            IList all = (IList)Activator.CreateInstance(listType);
            foreach (object condition in conditions)
                all.Add(condition);

            object eligibility = Activator.CreateInstance(setType);
            setType.GetField("schemaVersion").SetValue(eligibility, 1);
            setType.GetField("all").SetValue(eligibility, all);
            return eligibility;
        }

        private static object Evaluate(object eligibility, object snapshot)
        {
            Type evaluator = StatsTestAccess.Find("AlterOS.Stats.StatEligibilityEvaluator");
            return evaluator.GetMethod("Evaluate").Invoke(null, new[] { eligibility, snapshot });
        }

        private static object EvaluateCanonical(object routing, object snapshot)
        {
            Type evaluator = StatsTestAccess.Find("AlterOS.Dialogue.CanonicalDialogueEligibilityEvaluator");
            return evaluator.GetMethod("Evaluate").Invoke(null, new[] { routing, snapshot });
        }

        private static object Property(object target, string name)
        {
            return target.GetType().GetProperty(name).GetValue(target);
        }

        private static object First(object enumerable)
        {
            foreach (object value in (IEnumerable)enumerable)
                return value;
            Assert.Fail("Expected an item in the diagnostic list.");
            return null;
        }

        private static string ListText(object values)
        {
            var items = new List<string>();
            foreach (object value in (IEnumerable)values)
                items.Add(value == null ? string.Empty : value.ToString());
            return string.Join(" ", items.ToArray());
        }

        private static System.Reflection.MethodInfo FindContextBuilderMethod(Type builder, int parameterCount)
        {
            foreach (System.Reflection.MethodInfo method in builder.GetMethods())
                if (method.Name == "TryBuild" && method.GetParameters().Length == parameterCount)
                    return method;
            Assert.Fail("Missing Topic Playground context builder overload with " + parameterCount + " parameters.");
            return null;
        }

        private static void AssertValue(object snapshot, string id, float expected)
        {
            float value;
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, id, out value), id);
            Assert.AreEqual(expected, value, id);
        }
    }
}
