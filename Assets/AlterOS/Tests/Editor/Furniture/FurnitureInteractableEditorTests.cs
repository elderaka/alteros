using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AlterOS.Activities;
using AlterOS.Furniture;
using AlterOS.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureInteractableEditorTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in objects) if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }

        [Test]
        public void ExactExistingDispenserAliasesResolveToTheInstalledDrinkAction()
        {
            var point = CreatePoint("testbox.water_dispenser", "testbox.water_dispenser.drink", "needs.drink_water");
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);

            var options = ResolveOptions(catalogue, new[] { ActivityCapabilityIds.DrinkWater }, point,
                "", "", _ => true);

            Assert.AreEqual(1, options.Count);
            Assert.AreEqual("available", options[0].state);
            Assert.AreEqual("furniture.water_dispenser", options[0].definitionId);
            Assert.AreEqual("drink", options[0].pointId);
            Assert.AreEqual(point.UsePointId, options[0].usePointId);
        }

        [Test]
        public void OwnedFurnitureCannotOfferADeclaredActionWithoutAnExecutor()
        {
            var point = CreatePoint("testbox.water_dispenser", "testbox.water_dispenser.drink", "needs.drink_water");
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);

            var options = ResolveOptions(catalogue, new string[0], point,
                "", "", _ => true);

            Assert.AreEqual(1, options.Count);
            Assert.AreEqual("unavailable", options[0].state);
            Assert.AreEqual("executor_unavailable", options[0].reasonCode);
        }

        [Test]
        public void FurnitureInteractableUsesExistingPlayerEInterface()
        {
            var target = new GameObject("FurnitureInteractable");
            objects.Add(target);
            var interactableType = Type.GetType("Pleiades.Furniture.FurnitureInteractable, Assembly-CSharp");
            Assert.IsNotNull(interactableType);
            target.AddComponent(interactableType);
            string[] interfaces = Array.ConvertAll(interactableType.GetInterfaces(), item => item.FullName);
            CollectionAssert.Contains(interfaces, "Pleiades.Player.IPlayerInteractable");
            CollectionAssert.Contains(interfaces, "Pleiades.Player.IRevisionBoundPlayerInteractable");
        }

        [Test]
        public void FurnitureRequestUsesSameActivityHostWithoutLeakingScenarioEvents()
        {
            var settings = new ActivitySettings(ActivityCapabilityIds.DrinkWater, ActivityExecutorKind.DrinkWater);
            var host = new ActivityHost(new[] { settings },
                new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
                {
                    { ActivityExecutorKind.DrinkWater, context => new FakeExecution(context) }
                }, () => "direct-activity");
            var runId = "furniture-e-test-run";
            var stepId = "furniture.use";
            var targetId = "testbox.water_dispenser.drink";
            var capabilities = new[]
            {
                new ScenarioCapabilitySnapshot(ActivityCapabilityIds.DrinkWater,
                    new[] { new ScenarioArgumentBinding("target", new[] { targetId }) })
            };
            var objectsSnapshot = new[] { new ScenarioObjectSnapshot(targetId, "use_point") };
            var context = new ScenarioRuntimeSnapshot("furniture-interaction", runId, "turn", 1, stepId,
                new string[0], new string[0], capabilities, objectsSnapshot, new string[0]);
            var request = new ActivityRequest(runId, stepId, ActivityCapabilityIds.DrinkWater,
                new[] { new ScenarioActionArgument { name = "target", value = targetId } });

            var result = host.RequestFurnitureInteraction(request, context);
            var retry = host.RequestFurnitureInteraction(request, context);

            Assert.AreEqual("accepted", result.status);
            Assert.AreEqual(result.activityId, retry.activityId);
            Assert.AreEqual("direct-activity", host.Current.activityId);
            var competingRun = "furniture-e-competing";
            var competingContext = new ScenarioRuntimeSnapshot("furniture-interaction", competingRun, "turn",
                1, stepId, new string[0], new string[0], capabilities, objectsSnapshot, new string[0]);
            var competingRequest = new ActivityRequest(competingRun, stepId, ActivityCapabilityIds.DrinkWater,
                new[] { new ScenarioActionArgument { name = "target", value = targetId } });
            var busy = host.RequestFurnitureInteraction(competingRequest, competingContext);
            Assert.AreEqual("unavailable", busy.status);
            Assert.AreEqual("activity_busy", busy.reason);
            Assert.IsEmpty(host.DrainEvents());
            Assert.IsTrue(host.Stop(result.activityId, "player_stopped"));
            Assert.IsEmpty(host.DrainEvents());
        }

        [Test]
        public void StandaloneFurnitureRequestCannotTakeOwnershipFromScenario()
        {
            var root = new GameObject("ActivitySceneHost");
            objects.Add(root);
            var sceneHost = root.AddComponent<ActivitySceneHost>();
            sceneHost.SetScenarioOwnerActive(true);

            var result = sceneHost.TryRequestFurnitureUse(null, ActivityCapabilityIds.DrinkWater, "click-1", 1);

            Assert.AreEqual("unavailable", result.status);
            Assert.AreEqual("scenario_owns_interaction", result.reason);
            Assert.IsTrue(sceneHost.HasScenarioOwner);
        }

        private FurnitureUsePoint CreatePoint(string instanceId, string pointId, string useId)
        {
            var root = new GameObject(instanceId);
            objects.Add(root);
            var point = root.AddComponent<FurnitureUsePoint>();
            Set(point, "instanceId", instanceId);
            Set(point, "usePointId", pointId);
            Set(point, "supportedUseIds", new[] { useId });
            Set(point, "approach", Child(root.transform, "Approach"));
            Set(point, "pose", Child(root.transform, "Pose"));
            Set(point, "use", Child(root.transform, "Use"));
            Set(point, "exit", Child(root.transform, "Exit"));
            return point;
        }

        private static IReadOnlyList<FurnitureInteractionOption> ResolveOptions(FurnitureCatalogueSnapshot catalogue,
            IEnumerable<string> capabilities, FurnitureUsePoint point, string definition, string pointId,
            Func<FurnitureUsePoint, bool> reachable)
        {
            var type = Type.GetType("Pleiades.Furniture.FurnitureSceneAdapter, Assembly-CSharp");
            Assert.IsNotNull(type);
            var signature = new[]
            {
                typeof(FurnitureCatalogueSnapshot), typeof(IEnumerable<string>), typeof(FurnitureUsePoint),
                typeof(string), typeof(string), typeof(Func<FurnitureUsePoint, bool>)
            };
            MethodInfo method = type.GetMethod("ResolveOptions", BindingFlags.Public | BindingFlags.Static,
                null, signature, null);
            Assert.IsNotNull(method);
            return (IReadOnlyList<FurnitureInteractionOption>)method.Invoke(null,
                new object[] { catalogue, capabilities, point, definition, pointId, reachable });
        }

        private Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Set(FurnitureUsePoint target, string field, object value)
        {
            typeof(FurnitureUsePoint).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private sealed class FakeExecution : IActivityExecution
        {
            private readonly ActivityExecutionContext context;
            private readonly List<ActivityEvent> events = new List<ActivityEvent>();
            public ActivityView View { get; private set; }
            public FakeExecution(ActivityExecutionContext executionContext) { context = executionContext; }
            public void Start(double nowSeconds) { SetView(ActivityTerminalReason.None, "active"); }
            public void Tick(double nowSeconds) { }
            public bool Stop(string reason, double nowSeconds) { SetView(ActivityTerminalReason.PlayerStopped, reason); return true; }
            public void BeginExchange(string exchangeId, double nowSeconds) { }
            public void EndExchange(string exchangeId, double nowSeconds) { }
            public void Shutdown(string reason, double nowSeconds) { SetView(ActivityTerminalReason.InterruptedByQuit, reason); }
            public IReadOnlyList<ActivityEvent> DrainEvents() { var result = events.ToArray(); events.Clear(); return result; }
            private void SetView(ActivityTerminalReason reason, string phase)
            {
                View = new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                    context.settings.capabilityId, phase, reason, true, true);
            }
        }
    }
}
