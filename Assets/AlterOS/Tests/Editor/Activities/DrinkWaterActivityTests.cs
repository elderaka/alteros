using System.Collections.Generic;
using System.Reflection;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class DrinkWaterActivityTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject instance in objects) if (instance != null) Object.DestroyImmediate(instance);
            objects.Clear();
        }

        [Test]
        public void HydrationChangesOnlyAtSipCheckpoint()
        {
            FurnitureUsePoint point = Point();
            FurnitureRegistry registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 1f);
            var hydration = new List<float>();
            var sink = new HydrationCheckpointSink(amount => { hydration.Add(amount); return true; });
            var motion = new FakeMotion();
            DrinkWaterActivity activity = Create(registry, motion, sink);

            activity.Start(0);
            Assert.IsEmpty(hydration);
            activity.Tick(0.1);
            activity.Tick(0.2);
            activity.Tick(0.8);
            Assert.IsEmpty(hydration);
            activity.Tick(1.3);

            CollectionAssert.AreEqual(new[] { 10f }, hydration);
            Assert.IsFalse(activity.View.isTerminal);
            Assert.AreEqual(1, Count(activity.DrainEvents(), ActivityEventKind.Checkpoint));
        }

        [Test]
        public void ExplicitUnavailableDispenserFailsWithoutMovingOrApplyingHydration()
        {
            var registry = new FurnitureRegistry(new FurnitureUsePoint[0], _ => true, _ => 0f);
            int effects = 0;
            var sink = new HydrationCheckpointSink(_ => { effects++; return true; });
            var motion = new FakeMotion();
            DrinkWaterActivity activity = Create(registry, motion, sink, "missing.dispenser");

            activity.Start(0);

            Assert.AreEqual(ActivityTerminalReason.Failed, activity.View.terminalReason);
            Assert.AreEqual(0, motion.MoveCalls);
            Assert.AreEqual(0, effects);
        }

        [Test]
        public void MovingBoundDispenserFailsClosedBeforeSip()
        {
            FurnitureUsePoint point = Point();
            FurnitureRegistry registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 1f);
            int effects = 0;
            var sink = new HydrationCheckpointSink(_ => { effects++; return true; });
            DrinkWaterActivity activity = Create(registry, new FakeMotion(), sink);
            activity.Start(0);
            point.transform.position += Vector3.right;
            activity.Tick(0.1);
            Assert.AreEqual(ActivityTerminalReason.Failed, activity.View.terminalReason);
            Assert.AreEqual(0, effects);
            Assert.AreEqual(0, Count(activity.DrainEvents(), ActivityEventKind.Checkpoint));
        }

        private DrinkWaterActivity Create(FurnitureRegistry registry, IActivityMotion motion,
            IHydrationCheckpointSink sink, string target = "available_dispenser")
        {
            ActivitySettings settings = new ActivitySettings(ActivityCapabilityIds.DrinkWater, ActivityExecutorKind.DrinkWater);
            ActivityRequest request = new ActivityRequest("run", "step", ActivityCapabilityIds.DrinkWater,
                new[] { new ScenarioActionArgument { name = "target", value = target } });
            ScenarioRuntimeSnapshot snapshot = new ScenarioRuntimeSnapshot("test", "run", "turn", 1, "step", null, null,
                new[] { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.DrinkWater,
                    new[] { new ScenarioArgumentBinding("target", new[] { "available_dispenser", "missing.dispenser" }) }) }, null, null);
            return new DrinkWaterActivity(new ActivityExecutionContext("activity", request, snapshot, settings), registry, motion, sink);
        }

        private FurnitureUsePoint Point()
        {
            GameObject root = new GameObject("water fixture");
            objects.Add(root);
            FurnitureUsePoint point = root.AddComponent<FurnitureUsePoint>();
            Set(point, "instanceId", "testbox.water_dispenser");
            Set(point, "usePointId", "drink_water");
            Set(point, "supportedUseIds", new[] { ActivityCapabilityIds.DrinkWater });
            Set(point, "approach", Child(root.transform, "approach"));
            Set(point, "pose", Child(root.transform, "pose"));
            Set(point, "use", Child(root.transform, "use"));
            Set(point, "exit", Child(root.transform, "exit"));
            return point;
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Set(FurnitureUsePoint target, string name, object value)
        { typeof(FurnitureUsePoint).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }

        private static int Count(IReadOnlyList<ActivityEvent> events, ActivityEventKind kind)
        {
            int count = 0;
            foreach (ActivityEvent item in events) if (item.kind == kind) count++;
            return count;
        }

        private sealed class FakeMotion : IActivityMotion
        {
            public int MoveCalls;
            public bool PlayerAvailable { get { return true; } }
            public float DistanceToPlayer { get { return 3f; } }
            public float RemainingDistance { get { return 0f; } }
            public bool HasArrived { get { return true; } }
            public bool IsBlocked { get { return false; } }
            public bool TryResolveNearPlayer(float separation, out Vector3 destination) { destination = Vector3.zero; return true; }
            public bool TryMoveTo(Vector3 destination) { MoveCalls++; return true; }
            public void Suspend() { }
            public void Resume() { }
            public void Clear() { }
            public void FacePlayer() { }
            public void Face(Vector3 worldPosition) { }
        }
    }
}
