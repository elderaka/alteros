using NUnit.Framework;

namespace AlterOS.Tests
{
    public class StatSnapshotTests
    {
        [Test]
        public void MissingValuesRemainUnknownIncludingAfterCloning()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            float value;
            Assert.IsFalse(StatsTestAccess.TryGet(snapshot, "alterra.hydration", out value));
            object clone = StatsTestAccess.Invoke(snapshot, "Clone");
            Assert.IsFalse(StatsTestAccess.TryGet(clone, "alterra.hydration", out value));
        }

        [Test]
        public void ExplicitZeroIsKnownForNeedsAndAxes()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            foreach (string id in new[] { "alterra.hydration", "alterra.emotion.valence" })
            {
                string error;
                Assert.IsTrue(StatsTestAccess.TrySet(snapshot, id, 0f, out error));
                Assert.IsNull(error);
                float value;
                Assert.IsTrue(StatsTestAccess.TryGet(snapshot, id, out value));
                Assert.AreEqual(0f, value);
            }
        }

        [Test]
        public void NeedAndAxisBoundariesAreAcceptedWithoutClamping()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            foreach (float boundary in new[] { 0f, 100f })
                SetAndRead(snapshot, "alterra.hydration", boundary);
            foreach (float boundary in new[] { -1f, 1f })
                SetAndRead(snapshot, "alterra.emotion.valence", boundary);
        }

        [Test]
        public void EveryReviewedNeedAndAxisCanBeSetIndependently()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            foreach (string id in new[] { "alterra.energy", "alterra.hydration", "alterra.satiety",
                "alterra.engagement", "alterra.connection", "alterra.physical_comfort" })
                SetAndRead(snapshot, id, 65f);
            foreach (string id in new[] { "alterra.emotion.valence", "alterra.emotion.activation", "alterra.emotion.control" })
                SetAndRead(snapshot, id, -0.5f);
            float energy;
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, "alterra.energy", out energy));
            Assert.AreEqual(65f, energy);
        }

        [Test]
        public void InvalidNeedInputsDoNotReplaceKnownValues()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            SetAndRead(snapshot, "alterra.hydration", 65f);
            foreach (float invalid in new[] { -1f, 101f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                string error;
                Assert.IsFalse(StatsTestAccess.TrySet(snapshot, "alterra.hydration", invalid, out error));
                Assert.IsNotEmpty(error);
                float value;
                Assert.IsTrue(StatsTestAccess.TryGet(snapshot, "alterra.hydration", out value));
                Assert.AreEqual(65f, value);
            }
        }

        [Test]
        public void InvalidAxesDoNotCreateKnownValues()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            foreach (float invalid in new[] { -1.01f, 1.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                string error;
                Assert.IsFalse(StatsTestAccess.TrySet(snapshot, "alterra.emotion.control", invalid, out error));
                Assert.IsNotEmpty(error);
                float value;
                Assert.IsFalse(StatsTestAccess.TryGet(snapshot, "alterra.emotion.control", out value));
            }
        }

        [Test]
        public void UnknownActorAndMalformedIdsCannotImpersonateKnownStats()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            SetAndRead(snapshot, "alterra.energy", 70f);
            foreach (string invalid in new[] { null, "", "altera.energy", "player.energy", "alien.energy",
                "alterra.Energy", " alterra.energy", "alterra.energy ", "alterra.unknown" })
            {
                string error;
                Assert.IsFalse(StatsTestAccess.TrySet(snapshot, invalid, 10f, out error));
                Assert.IsNotEmpty(error);
                float ignored;
                Assert.IsFalse(StatsTestAccess.TryGet(snapshot, invalid, out ignored));
            }
            float energy;
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, "alterra.energy", out energy));
            Assert.AreEqual(70f, energy);
        }

        [Test]
        public void CloneCopiesKnownValuesWithoutSharingMutableStorage()
        {
            object snapshot = StatsTestAccess.NewSnapshot();
            SetAndRead(snapshot, "alterra.energy", 70f);
            object clone = StatsTestAccess.Invoke(snapshot, "Clone");
            float value;
            Assert.IsTrue(StatsTestAccess.TryGet(clone, "alterra.energy", out value));
            Assert.AreEqual(70f, value);
            SetAndRead(clone, "alterra.energy", 20f);
            SetAndRead(snapshot, "alterra.engagement", 50f);
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, "alterra.energy", out value));
            Assert.AreEqual(70f, value);
            Assert.IsFalse(StatsTestAccess.TryGet(clone, "alterra.engagement", out value));
        }

        private static void SetAndRead(object snapshot, string id, float expected)
        {
            string error;
            Assert.IsTrue(StatsTestAccess.TrySet(snapshot, id, expected, out error), error);
            float actual;
            Assert.IsTrue(StatsTestAccess.TryGet(snapshot, id, out actual));
            Assert.AreEqual(expected, actual);
        }
    }
}
