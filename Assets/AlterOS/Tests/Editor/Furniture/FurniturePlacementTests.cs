using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AlterOS.Furniture;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurniturePlacementTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> objects = new List<GameObject>();
        private Transform room;
        private Component controller;
        private System.Type controllerType;
        private System.Type supportSurfaceType;
        private System.Type placedIdentityType;
        private FurnitureCatalogueSnapshot catalogue;

        [SetUp]
        public void SetUp()
        {
            catalogue = FurnitureCatalogueRegistry.Load(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json"));
            var roomObject = new GameObject("Furniture placement test room");
            roomObject.transform.position = new Vector3(100f, 50f, 100f);
            room = roomObject.transform;
            objects.Add(roomObject);

            var controllerObject = new GameObject("Furniture placement test controller");
            controllerObject.transform.position = new Vector3(-100f, -100f, -100f);
            objects.Add(controllerObject);
            controllerType = FindType("Pleiades.Furniture.FurnitureHandlingController");
            supportSurfaceType = FindType("Pleiades.Furniture.FurnitureSupportSurface");
            placedIdentityType = FindType("Pleiades.Furniture.FurniturePlacedInstanceIdentity");
            Assert.IsNotNull(controllerType);
            Assert.IsNotNull(supportSurfaceType);
            Assert.IsNotNull(placedIdentityType);
            controller = controllerObject.AddComponent(controllerType);
            SetField(controller, "catalogue", catalogue);
            SetField(controller, "roomRoot", room);
            SetField(controller, "placementMask", (LayerMask)(-1));
            SetField(controller, "preview", null);
            SetField(controller, "heldSource", null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects)
                if (item != null) Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void FloorPlacementIgnoresOnlyItsAuthoredSupportCollider()
        {
            Component floor = CreateSupport("room.floor", "roomFloor", Vector3.zero,
                new Vector3(8f, 0.2f, 8f), false);
            FurnitureDefinition seat = Definition("furniture.seat.basic");

            Assert.IsTrue(IsPhysicsClear(seat, Pose(Vector3.zero), floor, out string reason), reason);
        }

        [Test]
        public void WindowFitsAnAuthoredWallOpeningAndUsesThatSupportIdentity()
        {
            CreateCollider("Opening left wall", new Vector3(-1.45f, 1.5f, 4.1f), new Vector3(1.1f, 3f, 0.2f));
            CreateCollider("Opening right wall", new Vector3(1.45f, 1.5f, 4.1f), new Vector3(1.1f, 3f, 0.2f));
            CreateCollider("Opening sill", new Vector3(0f, 0.6f, 4.1f), new Vector3(1.2f, 1.2f, 0.2f));
            CreateCollider("Opening lintel", new Vector3(0f, 2.8f, 4.1f), new Vector3(1.2f, 0.4f, 0.2f));
            Component opening = CreateSupport("room.wall.north.opening.01", "wallOpening",
                new Vector3(0f, 1.9f, 4.1f), new Vector3(1.8f, 1.4f, 0.02f), true);
            FurnitureDefinition window = Definition("furniture.window.basic");
            FurnitureSupportSnapshot snapshot = null;

            object[] snapshotArgs = { room, null };
            Assert.IsTrue((bool)supportSurfaceType.GetMethod("TryCreateSnapshot").Invoke(opening, snapshotArgs));
            snapshot = (FurnitureSupportSnapshot)snapshotArgs[1];
            Assert.AreEqual("wallOpening", snapshot.supportKind);
            Assert.AreEqual("room.wall.north.opening.01", snapshot.supportId);
            Assert.Contains("wallOpening", window.supportKinds);
            Assert.IsTrue(IsPhysicsClear(window, Pose(new Vector3(0f, 1.9f, 4.1f)), opening, out string reason), reason);
        }

        [Test]
        public void ActorAndWorldCollidersBlockButHeldSourceDoesNotBlockItsOwnMove()
        {
            Component floor = CreateSupport("room.floor", "roomFloor", Vector3.zero,
                new Vector3(8f, 0.2f, 8f), false);
            FurnitureDefinition seat = Definition("furniture.seat.basic");
            GameObject actor = CreateCollider("Actor", Vector3.zero, Vector3.one);

            Assert.IsFalse(IsPhysicsClear(seat, Pose(Vector3.zero), floor, out string blockedReason));
            Assert.AreEqual("world_collision", blockedReason);

            Object.DestroyImmediate(actor);
            objects.Remove(actor);
            GameObject source = CreateCollider("Held source", Vector3.zero, Vector3.one);
            SetField(controller, "heldSource", source.transform);
            Assert.IsTrue(IsPhysicsClear(seat, Pose(Vector3.zero), floor, out string clearReason), clearReason);
        }

        [Test]
        public void CarpetMayLayerWithFurnitureInEitherPlacementOrderButNotThroughWallsOrActors()
        {
            Component floor = CreateSupport("room.floor", "roomFloor", Vector3.zero,
                new Vector3(8f, 0.2f, 8f), false);
            FurnitureDefinition seat = Definition("furniture.seat.basic");
            FurnitureDefinition carpet = Definition("furniture.carpet.basic");
            GameObject rug = CreateCollider("Existing rug", new Vector3(0f, 0.015f, 0f), new Vector3(1.8f, 0.03f, 1.8f));
            ConfigureIdentity(rug, "rug.existing", carpet.definitionId);

            Assert.IsTrue(IsPhysicsClear(seat, Pose(Vector3.zero), floor, out string chairReason), chairReason);

            Object.DestroyImmediate(rug);
            objects.Remove(rug);
            GameObject chair = CreateCollider("Existing chair", new Vector3(0f, 0.45f, 0f), new Vector3(0.56f, 0.9f, 0.56f));
            ConfigureIdentity(chair, "chair.existing", seat.definitionId);
            Assert.IsTrue(IsPhysicsClear(carpet, Pose(Vector3.zero), floor, out string rugReason), rugReason);

            GameObject wall = CreateCollider("Solid wall", Vector3.zero, Vector3.one);
            Assert.IsFalse(IsPhysicsClear(carpet, Pose(Vector3.zero), floor, out string wallReason));
            Assert.AreEqual("world_collision", wallReason);
            Object.DestroyImmediate(wall);
            objects.Remove(wall);
            Object.DestroyImmediate(chair);
            objects.Remove(chair);

            CreateCollider("Actor", Vector3.zero, Vector3.one);
            Assert.IsFalse(IsPhysicsClear(carpet, Pose(Vector3.zero), floor, out string actorReason));
            Assert.AreEqual("world_collision", actorReason);
        }

        private Component CreateSupport(string id, string kind, Vector3 position, Vector3 size, bool trigger)
        {
            GameObject supportObject = CreateCollider(id, position, size);
            BoxCollider collider = supportObject.GetComponent<BoxCollider>();
            collider.isTrigger = trigger;
            Component surface = supportObject.AddComponent(supportSurfaceType);
            SetField(surface, "supportId", id);
            SetField(surface, "supportKind", kind);
            SetField(surface, "frameRevision", "1");
            SetField(surface, "useRoomOriginFrame", kind == "roomFloor");
            SetField(surface, "surfaceCollider", collider);
            return surface;
        }

        private GameObject CreateCollider(string name, Vector3 localPosition, Vector3 size)
        {
            var item = new GameObject(name);
            objects.Add(item);
            item.transform.SetParent(room, false);
            item.transform.localPosition = localPosition;
            BoxCollider collider = item.AddComponent<BoxCollider>();
            collider.size = size;
            return item;
        }

        private bool IsPhysicsClear(FurnitureDefinition definition, FurniturePlacementPose pose,
            Component support, out string reason)
        {
            MethodInfo method = controllerType.GetMethod("IsPhysicsClear", PrivateInstance);
            Assert.IsNotNull(method);
            object[] args = { definition, pose, support, null };
            bool result = (bool)method.Invoke(controller, args);
            reason = (string)args[3];
            Physics.SyncTransforms();
            return result;
        }

        private FurnitureDefinition Definition(string id)
        { return catalogue.definitions.Single(item => item.definitionId == id); }

        private void ConfigureIdentity(GameObject target, string instanceId, string definitionId)
        {
            Component identity = target.AddComponent(placedIdentityType);
            placedIdentityType.GetMethod("Configure").Invoke(identity, new object[] { instanceId, definitionId });
        }

        private static FurniturePlacementPose Pose(Vector3 position)
        {
            return new FurniturePlacementPose
            {
                positionMeters = new FurnitureVector3 { x = position.x, y = position.y, z = position.z },
                rotation = FurnitureLayoutTestData.Identity(),
                support = new FurnitureLayoutSupportReference
                {
                    supportId = "room.floor", supportKind = "roomFloor", supportFrameRevision = "1",
                    attachmentPointId = "origin", localPositionMeters = new FurnitureVector3 { x = position.x, y = position.y, z = position.z },
                    localRotation = FurnitureLayoutTestData.Identity()
                }
            };
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, "Expected field " + name);
            field.SetValue(target, value);
        }

        private static System.Type FindType(string fullName)
        {
            return System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }
    }
}
