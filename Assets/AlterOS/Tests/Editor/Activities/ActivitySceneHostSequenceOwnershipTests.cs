using System;
using System.Collections.Generic;
using System.Reflection;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class ActivitySceneHostSequenceOwnershipTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void FurnitureReservationAndBindingRequireTheExactCurrentSequenceOwner()
        {
            FurnitureUsePoint point = CreatePoint();
            var registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 0f);
            ActivityHost activityHost = CreateActivityHost();
            ActivitySequenceRequest request = CreateRequest("action.current");
            Assert.AreEqual("accepted", activityHost.RequestSequence(request).status);

            ActivitySceneHost sceneHost = CreateSceneHost(activityHost, registry);
            FurnitureBinding binding;
            Assert.IsFalse(sceneHost.TryReserveSequenceFurniture("furniture.use", "", "action.stale", null, out binding));
            Assert.IsNull(binding);
            Assert.IsTrue(sceneHost.TryReserveSequenceFurniture("furniture.use", "", "action.current", null, out binding));
            Assert.IsNotNull(binding);
            Assert.IsTrue(sceneHost.IsCurrentSequenceFurnitureBinding(binding));

            Assert.IsTrue(activityHost.Stop("action.current", "test_complete"));
            Assert.IsFalse(sceneHost.IsCurrentSequenceFurnitureBinding(binding));
        }

        [Test]
        public void ScenePrimitivePortUsesInterfaceStartWithoutDeclaringAUnityStartMessage()
        {
            Type portType = Type.GetType("AlterOS.Activities.ActivitySequenceScenePrimitivePort, Assembly-CSharp");
            Assert.IsNotNull(portType, "The test requires the production Assembly-CSharp primitive port.");
            MethodInfo start = portType.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.IsNull(start, "Unity message lookup must not find a parameterized Start on this MonoBehaviour.");

            InterfaceMapping mapping = portType.GetInterfaceMap(typeof(IActivityPrimitivePort));
            int index = Array.IndexOf(mapping.InterfaceMethods, typeof(IActivityPrimitivePort).GetMethod("Start"));
            Assert.GreaterOrEqual(index, 0, "The existing primitive port contract must remain implemented.");
            Assert.IsTrue(mapping.TargetMethods[index].IsPrivate,
                "The interface method should remain available through IActivityPrimitivePort, not as a Unity message.");
        }

        private FurnitureUsePoint CreatePoint()
        {
            GameObject root = new GameObject("sequence-owner-point");
            objects.Add(root);
            FurnitureUsePoint point = root.AddComponent<FurnitureUsePoint>();
            Set(point, "instanceId", "fixture.furniture");
            Set(point, "usePointId", "fixture.use-point");
            Set(point, "supportedUseIds", new[] { "furniture.use" });
            Set(point, "approach", Child(root.transform, "approach"));
            Set(point, "pose", Child(root.transform, "pose"));
            Set(point, "use", Child(root.transform, "use"));
            Set(point, "exit", Child(root.transform, "exit"));
            return point;
        }

        private ActivitySceneHost CreateSceneHost(ActivityHost activityHost, FurnitureRegistry registry)
        {
            GameObject root = new GameObject("sequence-owner-host");
            objects.Add(root);
            ActivitySceneHost sceneHost = root.AddComponent<ActivitySceneHost>();
            Set(sceneHost, "host", activityHost);
            Set(sceneHost, "furniture", registry);
            return sceneHost;
        }

        private static ActivityHost CreateActivityHost()
        {
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>
            {
                { ActivityExecutorKind.ActivitySequence,
                    context => new ActivitySequenceExecution(context, new PendingRunner()) }
            };
            return new ActivityHost(new[]
            { new ActivitySettings(ActivityCapabilityIds.ActivitySequence, ActivityExecutorKind.ActivitySequence) }, factories);
        }

        private static ActivitySequenceRequest CreateRequest(string activityId)
        {
            var definition = new ActivitySequenceDefinition("action.owner-test", "revision",
                new[] { new ActivitySequenceStep("finish", ActivityPrimitiveIds.Complete,
                    new ActivitySequenceArgument[0], new ActivitySequenceResultBinding[0], "", "", "",
                    ActivitySequenceCheckpointPolicy.None) });
            var context = new ScenarioRuntimeSnapshot("scenario.owner-test", "run.owner-test", "turn", 1L,
                "scenario-step", null, null, new ScenarioCapabilitySnapshot[0], null, null);
            return new ActivitySequenceRequest(definition,
                new ContentRef("actions", definition.id, definition.revision, "hash"), context.runId,
                "flow.owner-test", "step-execution.owner-test", activityId, context, scenarioStep: "scenario-step");
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected private field " + fieldName);
            field.SetValue(target, value);
        }

        private sealed class PendingRunner : IActivitySequenceRunner
        {
            public ActivitySequenceStartResult Start(ActivitySequenceRequest request)
            {
                return new ActivitySequenceStartResult(ActivitySequenceStatuses.Accepted, request.scenarioRunId,
                    request.flowInvocationId, request.stepExecutionId, request.actionInvocationId, "");
            }
            public ActivitySequenceView Tick(double nowSeconds) { return null; }
            public ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion) { return null; }
            public ActivitySequenceView Cancel(ActivitySequenceCancellation cancellation)
            { return new ActivitySequenceView(ActivitySequenceStatuses.Cancelled, cancellation.scenarioRunId,
                cancellation.flowInvocationId, cancellation.stepExecutionId, cancellation.actionInvocationId, "", cancellation.reasonCode); }
            public ActivitySequenceView Shutdown(string reason) { return null; }
        }
    }
}
