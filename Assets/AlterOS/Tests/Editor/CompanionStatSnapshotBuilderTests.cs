using System;
using NUnit.Framework;

namespace AlterOS.Tests
{
    public class CompanionStatSnapshotBuilderTests
    {
        [Test]
        public void NullLegacyStateProducesUnknownNeedsAndAxes()
        {
            object snapshot = Build(null);
            AssertUnknown(snapshot, "alterra.energy", "alterra.engagement", "alterra.hydration",
                "alterra.satiety", "alterra.connection", "alterra.physical_comfort",
                "alterra.emotion.valence", "alterra.emotion.activation", "alterra.emotion.control");
        }

        [Test]
        public void LegacyEnergyAndEngagementMapWithoutReinterpretingOtherFields()
        {
            object legacy = NewLegacy();
            SetField(legacy, "energy", 70f);
            SetField(legacy, "engagement", 50f);
            SetField(legacy, "attention", 10f);
            SetField(legacy, "reassurance", 20f);
            SetField(legacy, "comfort", 30f);
            SetField(legacy, "stimulation", 90f);
            object snapshot = Build(legacy);
            AssertValue(snapshot, "alterra.energy", 70f);
            AssertValue(snapshot, "alterra.engagement", 50f);
            AssertUnknown(snapshot, "alterra.hydration", "alterra.satiety", "alterra.connection",
                "alterra.physical_comfort", "alterra.emotion.valence", "alterra.emotion.activation",
                "alterra.emotion.control", "altera.energy");
        }

        [Test]
        public void SnapshotChangesLeaveLegacyValuesMoodAndAvailabilityUntouched()
        {
            object legacy = NewLegacy();
            SetField(legacy, "energy", 70f);
            SetField(legacy, "engagement", 50f);
            SetField(legacy, "mood", "warm");
            SetField(legacy, "availability", "quiet");
            object snapshot = Build(legacy);
            string error;
            Assert.IsTrue(StatsTestAccess.TrySet(snapshot, "alterra.energy", 10f, out error));
            Assert.AreEqual(70f, legacy.GetType().GetField("energy").GetValue(legacy));
            Assert.AreEqual("warm", legacy.GetType().GetField("mood").GetValue(legacy));
            Assert.AreEqual("quiet", legacy.GetType().GetField("availability").GetValue(legacy));
            SetField(legacy, "engagement", 20f);
            AssertValue(snapshot, "alterra.engagement", 50f);
        }

        [Test]
        public void InvalidLegacyValuesAreOmittedInsteadOfClamped()
        {
            foreach (float invalid in new[] { -1f, 101f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                object legacy = NewLegacy();
                SetField(legacy, "energy", invalid);
                SetField(legacy, "engagement", invalid);
                AssertUnknown(Build(legacy), "alterra.energy", "alterra.engagement");
            }
        }

        [Test]
        public void LegacyZeroRemainsKnownAndEachBuildIsIndependent()
        {
            object legacy = NewLegacy();
            SetField(legacy, "energy", 0f);
            SetField(legacy, "engagement", 100f);
            object first = Build(legacy);
            AssertValue(first, "alterra.energy", 0f);
            AssertValue(first, "alterra.engagement", 100f);
            SetField(legacy, "energy", 50f);
            AssertValue(Build(legacy), "alterra.energy", 50f);
            AssertValue(first, "alterra.energy", 0f);
        }

        [Test]
        public void HydrationMapsOnlyWhenProgressionStateMarksItKnown()
        {
            object legacy = NewLegacy();
            SetField(legacy, "hydration", 63f);
            AssertUnknown(Build(legacy), "alterra.hydration");
            SetField(legacy, "hydrationKnown", true);
            AssertValue(Build(legacy), "alterra.hydration", 63f);
        }

        private static object NewLegacy()
        {
            return Activator.CreateInstance(StatsTestAccess.Find("AlterOS.Progression.AlteraStatsState"));
        }

        private static object Build(object legacy)
        {
            Type builder = StatsTestAccess.Find("AlterOS.Progression.CompanionStatSnapshotBuilder");
            return builder.GetMethod("Build").Invoke(null, new[] { legacy });
        }

        private static void SetField(object legacy, string name, object value)
        {
            legacy.GetType().GetField(name).SetValue(legacy, value);
        }

        private static void AssertUnknown(object snapshot, params string[] ids)
        {
            foreach (string id in ids)
            {
                float value;
                Assert.IsFalse(StatsTestAccess.TryGet(snapshot, id, out value), id);
            }
        }

        private static void AssertValue(object snapshot, string id, float expected)
        {
            float actual;
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, id, out actual), id);
            Assert.AreEqual(expected, actual, id);
        }
    }
}
