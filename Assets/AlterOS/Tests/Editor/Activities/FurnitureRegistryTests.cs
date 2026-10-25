using System;
using System.Collections.Generic;
using System.Reflection;
using AlterOS.Activities;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class FurnitureRegistryTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject instance in objects) if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }

        [Test]
        public void SupportedUseControlsAvailabilityInsteadOfObjectName()
        {
            FurnitureUsePoint point = Point("decorative_mesh", "water-point", "drink_water");
            FurnitureRegistry registry = Registry(new[] { point }, _ => true, _ => 1f);
            FurnitureBinding binding;
            Assert.IsTrue(registry.TryReserve("drink_water", "", "activity-1", out binding));
            Assert.AreEqual(point.UsePointId, binding.usePointId);
        }

        [Test]
        public void NearestReachableCandidateWinsWithStableIdTieBreak()
        {
            FurnitureUsePoint farther = Point("fixture.b", "point-b", "drink_water");
            FurnitureUsePoint nearerZ = Point("fixture.a", "point-z", "drink_water");
            FurnitureUsePoint nearerA = Point("fixture.a", "point-a", "drink_water");
            FurnitureRegistry registry = Registry(new[] { farther, nearerZ, nearerA }, point => point != farther,
                point => point == nearerZ || point == nearerA ? 2f : 1f);
            FurnitureBinding binding;
            Assert.IsTrue(registry.TryReserve("drink_water", "available_dispenser", "activity-1", out binding));
            Assert.AreEqual("point-a", binding.usePointId);
        }

        [Test]
        public void ReservationEligibilityFiltersBeforeChoosingNearestCandidate()
        {
            FurnitureUsePoint ineligibleNear = Point("fixture.near", "point-near", "drink_water");
            FurnitureUsePoint eligibleFar = Point("fixture.far", "point-far", "drink_water");
            FurnitureRegistry registry = Registry(new[] { ineligibleNear, eligibleFar }, _ => true,
                point => point == ineligibleNear ? 1f : 4f);

            FurnitureBinding binding;
            Assert.IsTrue(registry.TryReserve("drink_water", "", "action-1",
                point => point == eligibleFar, out binding));

            Assert.AreSame(eligibleFar, binding.point);
            Assert.IsTrue(registry.IsValid(binding));
            Assert.IsFalse(registry.IsClaimed(ineligibleNear));
        }

        [Test]
        public void ExplicitUnavailableObjectDoesNotSubstitute()
        {
            FurnitureUsePoint point = Point("fixture.available", "point-a", "drink_water");
            FurnitureRegistry registry = Registry(new[] { point }, _ => true, _ => 1f);
            FurnitureBinding binding;
            Assert.IsFalse(registry.TryReserve("drink_water", "fixture.missing", "activity-1", out binding));
            Assert.IsNull(binding);
        }

        [Test]
        public void DuplicateUsePointIdsFailRegistration()
        {
            FurnitureUsePoint first = Point("fixture.a", "same-point", "drink_water");
            FurnitureUsePoint second = Point("fixture.b", "same-point", "drink_water");
            Assert.Throws<ArgumentException>(() => Registry(new[] { first, second }, _ => true, _ => 1f));
        }

        [Test]
        public void MovedOrDisabledBindingInvalidatesAndCanBeReleased()
        {
            FurnitureUsePoint point = Point("fixture.a", "point-a", "drink_water");
            FurnitureRegistry registry = Registry(new[] { point }, _ => true, _ => 1f);
            FurnitureBinding binding;
            Assert.IsTrue(registry.TryReserve("drink_water", "fixture.a", "activity-1", out binding));
            Assert.IsTrue(registry.IsValid(binding));
            point.transform.position += Vector3.right;
            Assert.IsFalse(registry.IsValid(binding));
            registry.Release("activity-1");
            registry.Release("activity-1");
            point.enabled = false;
            Assert.IsFalse(registry.TryReserve("drink_water", "fixture.a", "activity-2", out binding));
        }

        [Test]
        public void BindingExposesTheRevisionCapturedAtReservation()
        {
            FurnitureUsePoint point = Point("fixture.a", "point-a", "drink_water");
            FurnitureRegistry registry = Registry(new[] { point }, _ => true, _ => 1f);
            FurnitureBinding binding;
            Assert.IsTrue(registry.TryReserve("drink_water", "fixture.a", "activity-1", out binding));
            int capturedRevision = point.Revision;

            Assert.AreEqual(capturedRevision, binding.CapturedRevision);
            point.transform.position += Vector3.right;
            Assert.AreEqual(capturedRevision, binding.CapturedRevision);
            Assert.IsFalse(registry.IsValid(binding));
        }

        private FurnitureUsePoint Point(string instanceId, string pointId, string useId)
        {
            GameObject root = new GameObject(instanceId);
            objects.Add(root);
            FurnitureUsePoint point = root.AddComponent<FurnitureUsePoint>();
            Set(point, "instanceId", instanceId);
            Set(point, "usePointId", pointId);
            Set(point, "supportedUseIds", new[] { useId });
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
        {
            typeof(FurnitureUsePoint).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static FurnitureRegistry Registry(IEnumerable<FurnitureUsePoint> points,
            Func<FurnitureUsePoint, bool> reachable, Func<FurnitureUsePoint, float> distance)
        { return new FurnitureRegistry(points, reachable, distance); }
    }
}
