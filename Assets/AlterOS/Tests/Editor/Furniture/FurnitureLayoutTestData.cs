using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    internal static class FurnitureLayoutTestData
    {
        public static HomeFurnitureLayoutDocument Seed(string seedVersion)
        {
            return new HomeFurnitureLayoutDocument
            {
                schemaVersion = 1, homeId = "home.test", roomId = "room.test",
                catalogueVersion = "1.0.0", seedVersion = seedVersion,
                coordinateFrame = "room_local_y_up_positive_z_forward", lengthUnit = "m", layoutRevision = 1,
                instances = new List<HomeFurnitureLayoutInstance>
                {
                    Instance("seat.a", "furniture.seat.basic", new FurnitureVector3 { x = -0.7, y = 0, z = 0 }, Yaw(90), "room.floor"),
                    Instance("seat.b", "furniture.seat.basic", new FurnitureVector3 { x = 0.7, y = 0, z = 0 }, Yaw(-90), "room.floor")
                }
            };
        }

        public static HomeFurnitureLayoutInstance Instance(string id, string definitionId, FurnitureVector3 position,
            FurnitureQuaternion rotation, string supportId)
        {
            return new HomeFurnitureLayoutInstance
            {
                instanceId = id, definitionId = definitionId,
                positionMeters = position, rotation = rotation, scale = new FurnitureVector3 { x = 1, y = 1, z = 1 },
                support = new FurnitureLayoutSupportReference
                {
                    supportId = supportId,
                    supportKind = supportId.StartsWith("room.wall.", System.StringComparison.Ordinal) ? "wallOpening" : "roomFloor",
                    supportFrameRevision = "1",
                    attachmentPointId = supportId == "room.floor" ? "origin" : "opening.center",
                    localPositionMeters = supportId == "room.floor"
                        ? new FurnitureVector3 { x = position.x, y = position.y, z = position.z }
                        : new FurnitureVector3(),
                    localRotation = supportId == "room.floor"
                        ? new FurnitureQuaternion { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w }
                        : Identity()
                },
                enabled = true, conditionState = "good",
                resourceState = new List<FurnitureResourceState>(), instanceRevision = 1
            };
        }

        public static FurniturePlacementEvidence Evidence(FurnitureCatalogueSnapshot catalogue)
        {
            return new FurniturePlacementEvidence
            {
                catalogue = catalogue, evidenceComplete = true,
                roomMinimum = new FurnitureVector3 { x = -4, y = 0, z = -4 },
                roomMaximum = new FurnitureVector3 { x = 4, y = 3, z = 4 },
                supports = new List<FurnitureSupportSnapshot>
                {
                    new FurnitureSupportSnapshot { supportId = "room.floor", supportKind = "roomFloor", revision = "1", positionMeters = new FurnitureVector3(), rotation = Identity() },
                    new FurnitureSupportSnapshot { supportId = "room.wall.north.opening.01", supportKind = "wallOpening", revision = "1", positionMeters = new FurnitureVector3 { x = 0, y = 0, z = 3.4 }, rotation = Identity() }
                },
                reservedPointRefs = new List<FurniturePointReference>()
            };
        }

        public static FurnitureLayoutSnapshot Snapshot(HomeFurnitureLayoutDocument document)
        {
            return new FurnitureLayoutSnapshot { document = document, contentHash = "fixture-hash" };
        }

        public static HomeFurnitureLayoutDocument Clone(HomeFurnitureLayoutDocument document)
        {
            var serializer = new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, document);
                stream.Position = 0;
                return (HomeFurnitureLayoutDocument)serializer.ReadObject(stream);
            }
        }

        public static byte[] Serialize(HomeFurnitureLayoutDocument document)
        {
            var serializer = new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, document);
                return stream.ToArray();
            }
        }

        public static FurnitureLayoutCommand Place(string id, string definitionId, FurnitureVector3 position, FurnitureQuaternion rotation, string supportId)
        {
            return new FurnitureLayoutCommand
            {
                operation = "place", homeId = "home.test", roomId = "room.test", instanceId = id,
                newInstance = Instance(id, definitionId, position, rotation, supportId)
            };
        }

        public static FurnitureWorldSnapshot ToWorld(HomeFurnitureLayoutDocument document, FurnitureCatalogueSnapshot catalogue)
        {
            var world = new FurnitureWorldSnapshot
            {
                homeId = document.homeId, roomId = document.roomId, catalogueHash = catalogue.catalogueHash,
                evidenceComplete = true, instances = new List<FurnitureInstanceSnapshot>(),
                supports = new List<FurnitureSupportSnapshot>
                {
                    new FurnitureSupportSnapshot { supportId = "room.floor", supportKind = "roomFloor", revision = "1" },
                    new FurnitureSupportSnapshot { supportId = "room.wall.north.opening.01", supportKind = "wallOpening", revision = "1" }
                },
                points = new List<FurnitureWorldPointSnapshot>(), accessEvidence = new List<FurniturePointAccessEvidence>(),
                occupiedPointRefs = new List<FurniturePointReference>()
            };
            foreach (var placed in document.instances)
            {
                world.instances.Add(new FurnitureInstanceSnapshot
                {
                    instanceId = placed.instanceId, definitionId = placed.definitionId, positionMeters = placed.positionMeters,
                    rotation = placed.rotation, scale = placed.scale, supportId = placed.support.supportId,
                    instanceRevision = placed.instanceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), enabled = placed.enabled
                });
                var definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == placed.definitionId);
                if (definition == null) continue;
                foreach (var point in definition.points)
                {
                    world.points.Add(new FurnitureWorldPointSnapshot
                    {
                        instanceId = placed.instanceId, pointId = point.pointId, pointTypeId = point.pointTypeId,
                        localPosition = point.localPosition, localFacing = point.localFacing, useIds = point.useIds
                    });
                    world.accessEvidence.Add(new FurniturePointAccessEvidence
                    {
                        instanceId = placed.instanceId, pointId = point.pointId, state = "reachable", pathClear = true, travelCostMeters = 0.1
                    });
                }
            }
            return world;
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
    }
}
