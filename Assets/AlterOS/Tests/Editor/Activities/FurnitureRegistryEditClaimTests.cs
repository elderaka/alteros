using System;
using System.Collections.Generic;
using System.Reflection;
using AlterOS.Activities;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class FurnitureRegistryEditClaimTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void EditClaimBlocksActivityReservationUntilTheClaimIsReleased()
        {
            FurnitureUsePoint point = CreateUsePoint();
            var registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 0f);

            object claim = ClaimForEdit(registry, point, "edit:one");

            Assert.IsTrue(IsClaimed(registry, point));
            FurnitureBinding activity;
            Assert.IsFalse(registry.TryReserve("needs.drink_water", null, "activity:one", out activity));
            Assert.IsTrue(ReleaseEditClaim(registry, claim));
            Assert.IsFalse(IsClaimed(registry, point));
            Assert.IsTrue(registry.TryReserve("needs.drink_water", null, "activity:one", out activity));
        }

        [Test]
        public void ExistingActivityReservationPreventsFurnitureEditClaim()
        {
            FurnitureUsePoint point = CreateUsePoint();
            var registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 0f);
            FurnitureBinding activity;
            Assert.IsTrue(registry.TryReserve("needs.drink_water", null, "activity:one", out activity));

            Assert.IsNull(ClaimForEditIfAvailable(registry, point, "edit:one"));
            Assert.IsTrue(IsClaimed(registry, point));
        }

        [Test]
        public void StaleEditClaimCannotReleaseAReplacementClaim()
        {
            FurnitureUsePoint point = CreateUsePoint();
            var registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 0f);
            object first = ClaimForEdit(registry, point, "edit:one");
            Assert.IsTrue(ReleaseEditClaim(registry, first));
            object replacement = ClaimForEdit(registry, point, "edit:one");

            Assert.IsFalse(ReleaseEditClaim(registry, first));
            Assert.IsTrue(IsClaimed(registry, point));
            Assert.IsTrue(ReleaseEditClaim(registry, replacement));
        }

        [Test]
        public void DisablingFurnitureControllerReleasesItsActivityResourceClaim()
        {
            FurnitureUsePoint point = CreateUsePoint();
            var registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 0f);
            FurnitureEditClaim claim;
            Assert.IsTrue(registry.TryClaimForEdit(point, "edit:disable", out claim));
            var hostObject = new GameObject("Synthetic activity host");
            objects.Add(hostObject);
            ActivitySceneHost host = hostObject.AddComponent<ActivitySceneHost>();
            SetField(host, "furniture", registry);

            var controllerObject = new GameObject("Synthetic furniture controller");
            objects.Add(controllerObject);
            System.Type controllerType = FindType("Pleiades.Furniture.FurnitureHandlingController");
            Assert.IsNotNull(controllerType);
            Component controller = controllerObject.AddComponent(controllerType);
            SetField(controller, "activityHost", host);
            SetField(controller, "heldEditClaim", claim);

            controllerType.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

            Assert.IsFalse(registry.IsClaimed(point));
        }

        private FurnitureUsePoint CreateUsePoint()
        {
            var root = new GameObject("Synthetic furniture use point");
            objects.Add(root);
            var point = root.AddComponent<FurnitureUsePoint>();
            SetField(point, "instanceId", "fixture.dispenser");
            SetField(point, "usePointId", "fixture.dispenser.drink");
            SetField(point, "supportedUseIds", new[] { "needs.drink_water" });
            SetField(point, "approach", Child(root.transform, "Approach"));
            SetField(point, "pose", Child(root.transform, "Pose"));
            SetField(point, "use", Child(root.transform, "Use"));
            SetField(point, "exit", Child(root.transform, "Exit"));
            Assert.IsTrue(point.Supports("needs.drink_water"));
            return point;
        }

        private Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            objects.Add(child);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static object ClaimForEdit(FurnitureRegistry registry, FurnitureUsePoint point, string owner)
        {
            object claim = ClaimForEditIfAvailable(registry, point, owner);
            Assert.IsNotNull(claim, "A free registered use point should accept a furniture edit claim.");
            return claim;
        }

        private static object ClaimForEditIfAvailable(FurnitureRegistry registry, FurnitureUsePoint point, string owner)
        {
            MethodInfo method = typeof(FurnitureRegistry).GetMethod("TryClaimForEdit", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(method, "FurnitureRegistry must expose edit claims through the shared furniture resource owner.");
            object[] args = { point, owner, null };
            return (bool)method.Invoke(registry, args) ? args[2] : null;
        }

        private static bool ReleaseEditClaim(FurnitureRegistry registry, object claim)
        {
            MethodInfo method = typeof(FurnitureRegistry).GetMethod("ReleaseEditClaim", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(method, "FurnitureRegistry must release the exact edit claim token.");
            return (bool)method.Invoke(registry, new[] { claim });
        }

        private static bool IsClaimed(FurnitureRegistry registry, FurnitureUsePoint point)
        {
            MethodInfo method = typeof(FurnitureRegistry).GetMethod("IsClaimed", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(method, "The shared resource owner must expose its current claim state.");
            return (bool)method.Invoke(registry, new object[] { point });
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected serialized field " + name);
            field.SetValue(target, value);
        }

        private static System.Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
