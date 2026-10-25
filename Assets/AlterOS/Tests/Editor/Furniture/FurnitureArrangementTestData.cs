using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    internal static class FurnitureArrangementTestData
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        public static FurnitureArrangementRequest CreateSeatPair(bool addTable, bool tableBetween)
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var world = new FurnitureWorldSnapshot
            {
                homeId = "home.test",
                roomId = "room.test",
                catalogueHash = catalogue.catalogueHash,
                evidenceComplete = true,
                instances = new List<FurnitureInstanceSnapshot>(),
                supports = new List<FurnitureSupportSnapshot>(),
                points = new List<FurnitureWorldPointSnapshot>(),
                accessEvidence = new List<FurniturePointAccessEvidence>(),
                occupiedPointRefs = new List<FurniturePointReference>()
            };
            world.supports.Add(new FurnitureSupportSnapshot { supportId = "room.floor", supportKind = "roomFloor", revision = "1" });
            AddFurniture(catalogue, world, "seat.a", "furniture.seat.basic", new FurnitureVector3 { x = -0.7, y = 0, z = 0 }, Yaw(90), "room.floor");
            AddFurniture(catalogue, world, "seat.b", "furniture.seat.basic", new FurnitureVector3 { x = 0.7, y = 0, z = 0 }, Yaw(-90), "room.floor");
            if (addTable)
            {
                var z = tableBetween ? 0d : 3d;
                AddFurniture(catalogue, world, "table.unrelated", "furniture.table.general", new FurnitureVector3 { x = 0, y = 0, z = z }, Identity(), "room.floor");
            }
            return new FurnitureArrangementRequest
            {
                catalogue = catalogue,
                world = world,
                relationshipRuleId = addTable ? "relationship.seat_pair.table_context" : "relationship.seat.face_pair",
                intendedRoleBindings = null
            };
        }

        public static FurnitureArrangementRequest CreateCompetingPairs(bool reverse)
        {
            var request = CreateSeatPair(false, false);
            request.relationshipRuleId = "relationship.seat.face_pair";
            AddFurniture(request.catalogue, request.world, "seat.c", "furniture.seat.basic", new FurnitureVector3 { x = -0.7, y = 0, z = 5 }, Yaw(90), "room.floor");
            AddFurniture(request.catalogue, request.world, "seat.d", "furniture.seat.basic", new FurnitureVector3 { x = 0.7, y = 0, z = 5 }, Yaw(-90), "room.floor");
            if (reverse)
            {
                request.world.instances.Reverse();
                request.world.points.Reverse();
                request.world.accessEvidence.Reverse();
            }
            return request;
        }

        public static FurnitureArrangementRequest CreateSharedInstancePoints()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var world = new FurnitureWorldSnapshot
            {
                homeId = "home.test", roomId = "room.test", catalogueHash = catalogue.catalogueHash,
                evidenceComplete = true, instances = new List<FurnitureInstanceSnapshot>(),
                supports = new List<FurnitureSupportSnapshot> { new FurnitureSupportSnapshot { supportId = "room.floor", supportKind = "roomFloor", revision = "1" } },
                points = new List<FurnitureWorldPointSnapshot>(), accessEvidence = new List<FurniturePointAccessEvidence>(),
                occupiedPointRefs = new List<FurniturePointReference>()
            };
            world.instances.Add(new FurnitureInstanceSnapshot
            {
                instanceId = "chair.shared", definitionId = "furniture.seat.basic",
                positionMeters = new FurnitureVector3(), rotation = Identity(), scale = One(),
                supportId = "room.floor", instanceRevision = "1", enabled = true
            });
            world.points.Add(Point("chair.shared", "seat.a", "seat.sit", new FurnitureVector3(), new FurnitureVector3 { x = 0, y = 0, z = 1 }));
            world.points.Add(Point("chair.shared", "seat.b", "seat.sit", new FurnitureVector3(), new FurnitureVector3 { x = 0, y = 0, z = 1 }));
            return new FurnitureArrangementRequest { catalogue = catalogue, world = world, relationshipRuleId = "relationship.seat.face_pair" };
        }

        public static FurniturePointReference Ref(string instanceId, string pointId)
        {
            return new FurniturePointReference { instanceId = instanceId, pointId = pointId };
        }

        public static FurnitureQuaternion Identity()
        {
            return new FurnitureQuaternion { x = 0, y = 0, z = 0, w = 1 };
        }

        public static FurnitureQuaternion Yaw(double degrees)
        {
            var half = degrees * System.Math.PI / 360d;
            return new FurnitureQuaternion { x = 0, y = System.Math.Sin(half), z = 0, w = System.Math.Cos(half) };
        }

        public static FurnitureVector3 One()
        {
            return new FurnitureVector3 { x = 1, y = 1, z = 1 };
        }

        private static void AddFurniture(FurnitureCatalogueSnapshot catalogue, FurnitureWorldSnapshot world, string instanceId,
            string definitionId, FurnitureVector3 position, FurnitureQuaternion rotation, string supportId)
        {
            var definition = catalogue.definitions.Single(item => item.definitionId == definitionId);
            world.instances.Add(new FurnitureInstanceSnapshot
            {
                instanceId = instanceId, definitionId = definitionId, positionMeters = position,
                rotation = rotation, scale = One(), supportId = supportId, instanceRevision = "1", enabled = true
            });
            foreach (var template in definition.points)
            {
                world.points.Add(Point(instanceId, template.pointId, template.pointTypeId, template.localPosition, template.localFacing));
                world.accessEvidence.Add(new FurniturePointAccessEvidence
                {
                    instanceId = instanceId, pointId = template.pointId,
                    state = "reachable", pathClear = true, travelCostMeters = 0.1
                });
            }
        }

        private static FurnitureWorldPointSnapshot Point(string instanceId, string pointId, string pointTypeId,
            FurnitureVector3 localPosition, FurnitureVector3 localFacing)
        {
            return new FurnitureWorldPointSnapshot
            {
                instanceId = instanceId, pointId = pointId, pointTypeId = pointTypeId,
                localPosition = localPosition, localFacing = localFacing, useIds = new List<string>()
            };
        }
    }
}
