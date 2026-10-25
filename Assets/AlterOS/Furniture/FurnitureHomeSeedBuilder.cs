using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Furniture
{
    public sealed class FurnitureHomeSeedItem
    {
        public string legacyInstanceId;
        public string legacyUsePointId;
        public FurnitureVector3 positionMeters;
        public FurnitureQuaternion rotation;
        public FurnitureVector3 scale;
    }

    public static class FurnitureHomeSeedBuilder
    {
        public static HomeFurnitureLayoutDocument Create(string homeId, string roomId, string seedVersion,
            FurnitureCatalogueSnapshot catalogue, IEnumerable<FurnitureHomeSeedItem> sceneItems)
        {
            if (string.IsNullOrWhiteSpace(homeId) || string.IsNullOrWhiteSpace(roomId) || catalogue == null ||
                catalogue.schemaVersion != 1 || catalogue.diagnostics.Any(item => item.severity == "error") || sceneItems == null)
                return null;

            var document = new HomeFurnitureLayoutDocument
            {
                schemaVersion = 1,
                homeId = homeId,
                roomId = roomId,
                catalogueVersion = catalogue.catalogueVersion,
                seedVersion = seedVersion ?? string.Empty,
                coordinateFrame = "room_local_y_up_positive_z_forward",
                lengthUnit = "m",
                layoutRevision = 0,
                instances = new List<HomeFurnitureLayoutInstance>()
            };

            foreach (FurnitureHomeSeedItem item in sceneItems.OrderBy(value => value == null ? string.Empty : value.legacyInstanceId, StringComparer.Ordinal))
            {
                if (item == null || !IsUnitScale(item.scale) || !IsUnitQuaternion(item.rotation)) return null;
                string definitionId;
                string pointId;
                if (!FurnitureInteractionPolicy.TryResolvePoint(catalogue, item.legacyInstanceId, item.legacyUsePointId,
                    string.Empty, string.Empty, out definitionId, out pointId)) return null;

                var definition = catalogue.definitions.FirstOrDefault(value => value.definitionId == definitionId);
                if (definition == null || definition.supportKinds == null || !definition.supportKinds.Contains("roomFloor")) return null;
                var position = Clone(item.positionMeters);
                var rotation = Clone(item.rotation);
                document.instances.Add(new HomeFurnitureLayoutInstance
                {
                    instanceId = item.legacyInstanceId,
                    definitionId = definitionId,
                    positionMeters = position,
                    rotation = rotation,
                    scale = Clone(item.scale),
                    support = new FurnitureLayoutSupportReference
                    {
                        supportId = "room.floor",
                        supportKind = "roomFloor",
                        supportFrameRevision = "1",
                        attachmentPointId = "origin",
                        localPositionMeters = Clone(position),
                        localRotation = Clone(rotation)
                    },
                    enabled = true,
                    conditionState = "good",
                    resourceState = new List<FurnitureResourceState>(),
                    instanceRevision = 1
                });
            }

            return HomeFurnitureRepository.ValidateDocument(document).Any(item => item.severity == "error")
                ? null : document;
        }

        private static bool IsUnitScale(FurnitureVector3 value)
        {
            return value != null && Math.Abs(value.x - 1d) < 0.001d && Math.Abs(value.y - 1d) < 0.001d && Math.Abs(value.z - 1d) < 0.001d;
        }

        private static bool IsUnitQuaternion(FurnitureQuaternion value)
        {
            if (value == null) return false;
            double magnitude = Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return !double.IsNaN(magnitude) && !double.IsInfinity(magnitude) && Math.Abs(magnitude - 1d) < 0.001d;
        }

        private static FurnitureVector3 Clone(FurnitureVector3 value)
        {
            return value == null ? null : new FurnitureVector3 { x = value.x, y = value.y, z = value.z };
        }

        private static FurnitureQuaternion Clone(FurnitureQuaternion value)
        {
            return value == null ? null : new FurnitureQuaternion { x = value.x, y = value.y, z = value.z, w = value.w };
        }
    }
}
