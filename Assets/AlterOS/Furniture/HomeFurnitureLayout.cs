using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AlterOS.Furniture
{
    [DataContract]
    public sealed class HomeFurnitureLayoutDocument
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public int schemaVersion;
        [DataMember(Name = "homeId", Order = 1)] public string homeId;
        [DataMember(Name = "roomId", Order = 2)] public string roomId;
        [DataMember(Name = "catalogueVersion", Order = 3)] public string catalogueVersion;
        [DataMember(Name = "seedVersion", Order = 4)] public string seedVersion;
        [DataMember(Name = "coordinateFrame", Order = 5)] public string coordinateFrame;
        [DataMember(Name = "lengthUnit", Order = 6)] public string lengthUnit;
        [DataMember(Name = "layoutRevision", Order = 7)] public long layoutRevision;
        [DataMember(Name = "instances", Order = 8)] public List<HomeFurnitureLayoutInstance> instances = new List<HomeFurnitureLayoutInstance>();
        [DataMember(Name = "inventory", Order = 9, EmitDefaultValue = false)] public List<FurnitureInventoryEntry> inventory;
    }

    [DataContract]
    public sealed class HomeFurnitureLayoutInstance
    {
        [DataMember(Name = "instanceId", Order = 0)] public string instanceId;
        [DataMember(Name = "definitionId", Order = 1)] public string definitionId;
        [DataMember(Name = "positionMeters", Order = 2)] public FurnitureVector3 positionMeters;
        [DataMember(Name = "rotation", Order = 3)] public FurnitureQuaternion rotation;
        [DataMember(Name = "scale", Order = 4)] public FurnitureVector3 scale;
        [DataMember(Name = "support", Order = 5)] public FurnitureLayoutSupportReference support;
        [DataMember(Name = "enabled", Order = 6)] public bool enabled;
        [DataMember(Name = "conditionState", Order = 7)] public string conditionState;
        [DataMember(Name = "resourceState", Order = 8)] public List<FurnitureResourceState> resourceState = new List<FurnitureResourceState>();
        [DataMember(Name = "instanceRevision", Order = 9)] public long instanceRevision;
    }

    [DataContract]
    public sealed class FurnitureLayoutSupportReference
    {
        [DataMember(Name = "supportId", Order = 0)] public string supportId;
        [DataMember(Name = "supportKind", Order = 1)] public string supportKind;
        [DataMember(Name = "supportFrameRevision", Order = 2)] public string supportFrameRevision;
        [DataMember(Name = "attachmentPointId", Order = 3)] public string attachmentPointId;
        [DataMember(Name = "localPositionMeters", Order = 4)] public FurnitureVector3 localPositionMeters;
        [DataMember(Name = "localRotation", Order = 5)] public FurnitureQuaternion localRotation;
    }

    [DataContract]
    public sealed class FurnitureResourceState
    {
        [DataMember(Name = "resourceId", Order = 0)] public string resourceId;
        [DataMember(Name = "value", Order = 1)] public double value;
        [DataMember(Name = "known", Order = 2)] public bool known;
    }

    public sealed class HomeFurnitureLayoutLoadResult
    {
        public string state;
        public HomeFurnitureLayoutDocument document;
        public string contentHash;
        public IReadOnlyList<string> orphanInstanceIds;
        public IReadOnlyList<FurnitureDiagnostic> diagnostics;
    }

    public sealed class HomeFurnitureLayoutSaveResult
    {
        public string state;
        public string contentHash;
        public FurnitureDiagnostic diagnostic;
    }

    public sealed class FurnitureLayoutSnapshot
    {
        public HomeFurnitureLayoutDocument document;
        public string contentHash;
    }

    public sealed class FurnitureLayoutCommand
    {
        public string operation;
        public string homeId;
        public string roomId;
        public string instanceId;
        public long expectedInstanceRevision;
        public HomeFurnitureLayoutInstance newInstance;
        public FurnitureVector3 positionMeters;
        public FurnitureQuaternion rotation;
        public FurnitureLayoutSupportReference support;
    }

    public sealed class FurniturePlacementEvidence
    {
        public FurnitureCatalogueSnapshot catalogue;
        public bool evidenceComplete;
        public FurnitureVector3 roomMinimum;
        public FurnitureVector3 roomMaximum;
        public List<FurnitureSupportSnapshot> supports = new List<FurnitureSupportSnapshot>();
        public List<FurniturePointReference> reservedPointRefs = new List<FurniturePointReference>();
    }

    public sealed class FurnitureLayoutCommandResult
    {
        public bool success;
        public string state;
        public string reasonCode;
        public FurnitureLayoutSnapshot snapshot;
        public string expectedSaveHash;
        public FurnitureDiagnostic diagnostic;
    }
}
