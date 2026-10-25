using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AlterOS.Furniture
{
    [DataContract]
    public sealed class FurnitureCatalogueDocument
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public int schemaVersion;
        [DataMember(Name = "catalogueVersion", Order = 1)] public string catalogueVersion;
        [DataMember(Name = "lengthUnit", Order = 2)] public string lengthUnit;
        [DataMember(Name = "angleUnit", Order = 3)] public string angleUnit;
        [DataMember(Name = "coordinateConvention", Order = 4)] public string coordinateConvention;
        [DataMember(Name = "definitions", Order = 5)] public List<FurnitureDefinition> definitions;
        [DataMember(Name = "relationshipRules", Order = 6)] public List<FurnitureRelationshipRule> relationshipRules;
        [DataMember(Name = "capabilities", Order = 7)] public List<FurnitureCapabilityDescriptor> capabilities;
        [DataMember(Name = "legacyAliases", Order = 8)] public List<FurnitureLegacyAlias> legacyAliases;
    }

    [DataContract]
    public sealed class FurnitureDefinition
    {
        [DataMember(Name = "definitionId", Order = 0)] public string definitionId;
        [DataMember(Name = "displayNameKey", Order = 1)] public string displayNameKey;
        [DataMember(Name = "category", Order = 2)] public string category;
        [DataMember(Name = "scalePolicy", Order = 3)] public string scalePolicy;
        [DataMember(Name = "defaultScale", Order = 4)] public FurnitureVector3 defaultScale;
        [DataMember(Name = "localBounds", Order = 5)] public FurnitureBounds localBounds;
        [DataMember(Name = "supportKinds", Order = 6)] public List<string> supportKinds;
        [DataMember(Name = "blocksNavigation", Order = 7)] public bool blocksNavigation;
        [DataMember(Name = "points", Order = 8)] public List<FurnitureFunctionalPoint> points;
        [DataMember(Name = "functionBindings", Order = 9, EmitDefaultValue = false)] public List<FurnitureFunctionBindingDescriptor> functionBindings;
        [DataMember(Name = "presentationBindings", Order = 10, EmitDefaultValue = false)] public List<FurniturePresentationBindingDescriptor> presentationBindings;
    }

    [DataContract]
    public sealed class FurnitureFunctionBindingDescriptor
    {
        [DataMember(Name = "functionId", Order = 0)] public string functionId;
        [DataMember(Name = "pointId", Order = 1)] public string pointId;
        [DataMember(Name = "useId", Order = 2)] public string useId;
    }

    [DataContract]
    public sealed class FurniturePresentationBindingDescriptor
    {
        [DataMember(Name = "descriptorId", Order = 0)] public string descriptorId;
        [DataMember(Name = "pointId", Order = 1)] public string pointId;
        [DataMember(Name = "useId", Order = 2)] public string useId;
    }

    [DataContract]
    public sealed class FurnitureFunctionalPoint
    {
        [DataMember(Name = "pointId", Order = 0)] public string pointId;
        [DataMember(Name = "pointTypeId", Order = 1)] public string pointTypeId;
        [DataMember(Name = "localPosition", Order = 2)] public FurnitureVector3 localPosition;
        [DataMember(Name = "localFacing", Order = 3)] public FurnitureVector3 localFacing;
        [DataMember(Name = "useIds", Order = 4)] public List<string> useIds;
    }

    [DataContract]
    public sealed class FurnitureBounds
    {
        [DataMember(Name = "minimum", Order = 0)] public FurnitureVector3 minimum;
        [DataMember(Name = "maximum", Order = 1)] public FurnitureVector3 maximum;
    }

    [DataContract]
    public sealed class FurnitureVector3
    {
        [DataMember(Name = "x", Order = 0)] public double x;
        [DataMember(Name = "y", Order = 1)] public double y;
        [DataMember(Name = "z", Order = 2)] public double z;
    }

    [DataContract]
    public sealed class FurnitureRelationshipRule
    {
        [DataMember(Name = "relationshipRuleId", Order = 0)] public string relationshipRuleId;
        [DataMember(Name = "kind", Order = 1)] public string kind;
        [DataMember(Name = "pointTypeIds", Order = 2)] public List<string> pointTypeIds;
        [DataMember(Name = "broadPhaseRadiusMeters", Order = 3)] public double broadPhaseRadiusMeters;
        [DataMember(Name = "minimumDistanceMeters", Order = 4)] public double minimumDistanceMeters;
        [DataMember(Name = "maximumDistanceMeters", Order = 5)] public double maximumDistanceMeters;
        [DataMember(Name = "maximumFacingErrorDegrees", Order = 6)] public double maximumFacingErrorDegrees;
        [DataMember(Name = "maximumHeightDifferenceMeters", Order = 7)] public double maximumHeightDifferenceMeters;
        [DataMember(Name = "lateralAllowanceMeters", Order = 8)] public double lateralAllowanceMeters;
    }

    [DataContract]
    public sealed class FurnitureCapabilityDescriptor
    {
        [DataMember(Name = "capabilityId", Order = 0)] public string capabilityId;
        [DataMember(Name = "requiredPointTypeIds", Order = 1)] public List<string> requiredPointTypeIds;
        [DataMember(Name = "requiredUseIds", Order = 2)] public List<string> requiredUseIds;
    }

    [DataContract]
    public sealed class FurnitureLegacyAlias
    {
        [DataMember(Name = "legacyId", Order = 0)] public string legacyId;
        [DataMember(Name = "definitionId", Order = 1)] public string definitionId;
        [DataMember(Name = "pointId", Order = 2, EmitDefaultValue = false)] public string pointId;
    }

    public sealed class FurnitureDiagnostic
    {
        public readonly string code;
        public readonly string severity;
        public readonly string path;
        public readonly string message;

        public FurnitureDiagnostic(string code, string severity, string path, string message)
        {
            this.code = code ?? string.Empty;
            this.severity = severity ?? string.Empty;
            this.path = path ?? string.Empty;
            this.message = message ?? string.Empty;
        }
    }

    public sealed class FurnitureCatalogueSnapshot
    {
        public readonly int schemaVersion;
        public readonly string catalogueVersion;
        public readonly string catalogueHash;
        public readonly IReadOnlyList<FurnitureDefinition> definitions;
        public readonly IReadOnlyList<FurnitureRelationshipRule> relationshipRules;
        public readonly IReadOnlyList<FurnitureCapabilityDescriptor> capabilities;
        public readonly IReadOnlyList<FurnitureLegacyAlias> legacyAliases;
        public readonly IReadOnlyList<FurnitureDiagnostic> diagnostics;

        internal FurnitureCatalogueSnapshot(
            int schemaVersion,
            string catalogueVersion,
            string catalogueHash,
            IList<FurnitureDefinition> definitions,
            IList<FurnitureRelationshipRule> relationshipRules,
            IList<FurnitureCapabilityDescriptor> capabilities,
            IList<FurnitureLegacyAlias> legacyAliases,
            IList<FurnitureDiagnostic> diagnostics)
        {
            this.schemaVersion = schemaVersion;
            this.catalogueVersion = catalogueVersion ?? string.Empty;
            this.catalogueHash = catalogueHash ?? string.Empty;
            this.definitions = new List<FurnitureDefinition>(definitions ?? new List<FurnitureDefinition>()).AsReadOnly();
            this.relationshipRules = new List<FurnitureRelationshipRule>(relationshipRules ?? new List<FurnitureRelationshipRule>()).AsReadOnly();
            this.capabilities = new List<FurnitureCapabilityDescriptor>(capabilities ?? new List<FurnitureCapabilityDescriptor>()).AsReadOnly();
            this.legacyAliases = new List<FurnitureLegacyAlias>(legacyAliases ?? new List<FurnitureLegacyAlias>()).AsReadOnly();
            this.diagnostics = new List<FurnitureDiagnostic>(diagnostics ?? new List<FurnitureDiagnostic>()).AsReadOnly();
        }
    }
}
