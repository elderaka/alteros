using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlterOS.Furniture
{
    public static class FurnitureCatalogueValidator
    {
        private static readonly string[] StarterDefinitionIds =
        {
            "furniture.seat.basic",
            "furniture.table.general",
            "furniture.table.study",
            "furniture.bed.basic",
            "furniture.water_dispenser",
            "furniture.tv.basic",
            "furniture.window.basic",
            "furniture.carpet.basic"
        };

        private static readonly HashSet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
        {
            "seat", "table", "bed", "dispenser", "tv", "window", "carpet"
        };

        private static readonly HashSet<string> SupportKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "roomFloor", "wallSurface", "wallOpening", "furnitureSurface"
        };

        private static readonly HashSet<string> RuleKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "seatFacePair", "seatPairTableContext", "seatWorkspace", "seatViewing"
        };

        public static IReadOnlyList<FurnitureDiagnostic> Validate(FurnitureCatalogueDocument document)
        {
            var diagnostics = new List<FurnitureDiagnostic>();
            if (document == null)
            {
                diagnostics.Add(Error("invalid_document", "$", "Catalogue document is null."));
                return diagnostics.AsReadOnly();
            }

            if (document.schemaVersion != 1)
                diagnostics.Add(Error("unsupported_schema_version", "schemaVersion", "Only furniture catalogue schema version 1 is supported."));
            if (string.IsNullOrWhiteSpace(document.catalogueVersion))
                diagnostics.Add(Error("missing_catalogue_version", "catalogueVersion", "Catalogue version is required."));
            if (document.lengthUnit != "m")
                diagnostics.Add(Error("unsupported_length_unit", "lengthUnit", "Length unit must be metres (m)."));
            if (document.angleUnit != "deg")
                diagnostics.Add(Error("unsupported_angle_unit", "angleUnit", "Angle unit must be degrees (deg)."));
            if (document.coordinateConvention != "unity_local_y_up_positive_z_forward")
                diagnostics.Add(Error("unsupported_coordinate_convention", "coordinateConvention", "Coordinate convention must be unity_local_y_up_positive_z_forward."));

            ValidateDefinitions(document.definitions, diagnostics);
            ValidateRules(document.relationshipRules, document.definitions, diagnostics);
            ValidateCapabilities(document.capabilities, document.definitions, diagnostics);
            ValidateAliases(document.legacyAliases, document.definitions, diagnostics);

            return diagnostics
                .OrderBy(diagnostic => diagnostic.path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.message, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        public static IReadOnlyList<FurnitureDiagnostic> ValidateDefinition(FurnitureDefinition definition)
        {
            var diagnostics = new List<FurnitureDiagnostic>();
            ValidateDefinitionRules(definition, "definition", diagnostics);
            return diagnostics
                .OrderBy(diagnostic => diagnostic.path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.message, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        internal static IReadOnlyList<string> RequiredStarterDefinitionIds
        {
            get { return Array.AsReadOnly(StarterDefinitionIds); }
        }

        private static void ValidateDefinitions(IList<FurnitureDefinition> definitions, List<FurnitureDiagnostic> diagnostics)
        {
            if (definitions == null)
            {
                diagnostics.Add(Error("missing_definitions", "definitions", "Definition list is required."));
                return;
            }

            var idPaths = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var path = "definitions[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (definition == null)
                {
                    ValidateDefinitionRules(null, path, diagnostics);
                    continue;
                }

                if (IsIdentifier(definition.definitionId))
                {
                    List<int> matches;
                    if (!idPaths.TryGetValue(definition.definitionId, out matches))
                    {
                        matches = new List<int>();
                        idPaths.Add(definition.definitionId, matches);
                    }
                    matches.Add(index);
                }

                ValidateDefinitionRules(definition, path, diagnostics);
            }

            foreach (var pair in idPaths.Where(pair => pair.Value.Count > 1))
                foreach (var index in pair.Value)
                    diagnostics.Add(Error("duplicate_definition_id", "definitions[" + index.ToString(CultureInfo.InvariantCulture) + "].definitionId", "Definition IDs must be unique."));

            var existingIds = new HashSet<string>(idPaths.Keys, StringComparer.Ordinal);
            for (var index = 0; index < StarterDefinitionIds.Length; index++)
                if (!existingIds.Contains(StarterDefinitionIds[index]))
                    diagnostics.Add(Error("missing_starter_definition", "definitions", "Required starter definition is missing: " + StarterDefinitionIds[index] + "."));
        }

        private static void ValidateDefinitionRules(FurnitureDefinition definition, string path,
            List<FurnitureDiagnostic> diagnostics)
        {
            if (definition == null)
            {
                diagnostics.Add(Error("invalid_definition", path, "Definition entry is null."));
                return;
            }

            if (!IsIdentifier(definition.definitionId))
                diagnostics.Add(Error("invalid_definition_id", path + ".definitionId", "Definition ID must be a stable lowercase identifier."));
            if (string.IsNullOrWhiteSpace(definition.displayNameKey))
                diagnostics.Add(Error("missing_display_name_key", path + ".displayNameKey", "Display name key is required."));
            if (!Categories.Contains(definition.category ?? string.Empty))
                diagnostics.Add(Error("unsupported_category", path + ".category", "Furniture category is unsupported."));
            if (definition.scalePolicy != "fixed")
                diagnostics.Add(Error("unsupported_scale_policy", path + ".scalePolicy", "Version 1 starter definitions use fixed scale."));
            ValidateVector(definition.defaultScale, path + ".defaultScale", diagnostics, true);
            if (definition.defaultScale != null &&
                (!NearlyEqual(definition.defaultScale.x, 1d) || !NearlyEqual(definition.defaultScale.y, 1d) || !NearlyEqual(definition.defaultScale.z, 1d)))
                diagnostics.Add(Error("invalid_fixed_scale", path + ".defaultScale", "Fixed scale must be (1,1,1) in catalogue version 1."));
            ValidateBounds(definition.localBounds, path + ".localBounds", diagnostics);

            if (definition.supportKinds == null || definition.supportKinds.Count == 0)
                diagnostics.Add(Error("missing_support_kind", path + ".supportKinds", "At least one support kind is required."));
            else
            {
                ValidateUniqueStrings(definition.supportKinds, path + ".supportKinds", "duplicate_support_kind", diagnostics);
                for (var supportIndex = 0; supportIndex < definition.supportKinds.Count; supportIndex++)
                    if (!SupportKinds.Contains(definition.supportKinds[supportIndex] ?? string.Empty))
                        diagnostics.Add(Error("unsupported_support_kind", path + ".supportKinds[" + supportIndex.ToString(CultureInfo.InvariantCulture) + "]", "Support kind is unsupported."));
            }

            if (definition.points == null)
            {
                diagnostics.Add(Error("missing_points", path + ".points", "Functional point list is required (it may be empty)."));
                return;
            }

            var pointIds = new HashSet<string>(StringComparer.Ordinal);
            for (var pointIndex = 0; pointIndex < definition.points.Count; pointIndex++)
            {
                var point = definition.points[pointIndex];
                var pointPath = path + ".points[" + pointIndex.ToString(CultureInfo.InvariantCulture) + "]";
                if (point == null)
                {
                    diagnostics.Add(Error("invalid_functional_point", pointPath, "Functional point entry is null."));
                    continue;
                }
                if (!IsIdentifier(point.pointId))
                    diagnostics.Add(Error("invalid_point_id", pointPath + ".pointId", "Point ID must be a stable lowercase identifier."));
                else if (!pointIds.Add(point.pointId))
                    diagnostics.Add(Error("duplicate_point_id", pointPath + ".pointId", "Point IDs must be unique within a definition."));
                if (!IsIdentifier(point.pointTypeId))
                    diagnostics.Add(Error("invalid_point_type_id", pointPath + ".pointTypeId", "Point type ID must be a stable lowercase identifier."));
                ValidateVector(point.localPosition, pointPath + ".localPosition", diagnostics, false);
                ValidateVector(point.localFacing, pointPath + ".localFacing", diagnostics, false);
                if (point.localFacing != null && LengthSquared(point.localFacing) <= 1e-12d)
                    diagnostics.Add(Error("invalid_facing_vector", pointPath + ".localFacing", "Point facing must be non-zero."));
                if (point.useIds == null)
                    diagnostics.Add(Error("missing_use_ids", pointPath + ".useIds", "Use ID list is required (it may be empty)."));
                else
                {
                    ValidateUniqueStrings(point.useIds, pointPath + ".useIds", "duplicate_use_id", diagnostics);
                    for (var useIndex = 0; useIndex < point.useIds.Count; useIndex++)
                        if (!IsIdentifier(point.useIds[useIndex]))
                            diagnostics.Add(Error("invalid_use_id", pointPath + ".useIds[" + useIndex.ToString(CultureInfo.InvariantCulture) + "]", "Use ID must be a stable lowercase identifier."));
                }
            }

            ValidateFunctionBindings(definition, path, diagnostics);
            ValidatePresentationBindings(definition, path, diagnostics);
        }

        private static void ValidateFunctionBindings(FurnitureDefinition definition, string definitionPath,
            List<FurnitureDiagnostic> diagnostics)
        {
            if (definition.functionBindings == null) return;
            var descriptors = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < definition.functionBindings.Count; index++)
            {
                FurnitureFunctionBindingDescriptor binding = definition.functionBindings[index];
                string path = definitionPath + ".functionBindings[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (binding == null)
                {
                    diagnostics.Add(Error("invalid_function_binding", path, "Function binding entry is null."));
                    continue;
                }
                if (!IsIdentifier(binding.functionId))
                    diagnostics.Add(Error("invalid_function_id", path + ".functionId", "Function ID must be a stable lowercase identifier."));
                else if (binding.functionId != FurnitureFunctionIds.PowerSet)
                    diagnostics.Add(Error("unknown_function_id", path + ".functionId", "Function ID is not registered by the trusted Furniture runtime."));

                if (!IsIdentifier(binding.pointId))
                    diagnostics.Add(Error("invalid_function_point_id", path + ".pointId", "Function point ID must be a stable lowercase identifier."));
                if (!IsIdentifier(binding.useId))
                    diagnostics.Add(Error("invalid_function_use_id", path + ".useId", "Function use ID must be a stable lowercase identifier."));

                FurnitureFunctionalPoint point = (definition.points ?? new List<FurnitureFunctionalPoint>())
                    .FirstOrDefault(candidate => candidate != null && candidate.pointId == binding.pointId);
                if (point == null)
                    diagnostics.Add(Error("unknown_function_point", path + ".pointId", "Function binding references a point not declared by this definition."));
                else if (point.useIds == null || !point.useIds.Contains(binding.useId, StringComparer.Ordinal))
                    diagnostics.Add(Error("unknown_function_use_id", path + ".useId", "Function use ID must be declared by its bound functional point."));

                string key = (binding.functionId ?? string.Empty) + "\0" + (binding.pointId ?? string.Empty) + "\0" + (binding.useId ?? string.Empty);
                if (!descriptors.Add(key))
                    diagnostics.Add(Error("duplicate_function_binding", path, "Function bindings must be unique by function, point and use ID."));
            }
        }

        private static void ValidatePresentationBindings(FurnitureDefinition definition, string definitionPath,
            List<FurnitureDiagnostic> diagnostics)
        {
            if (definition.presentationBindings == null) return;
            var descriptors = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < definition.presentationBindings.Count; index++)
            {
                FurniturePresentationBindingDescriptor binding = definition.presentationBindings[index];
                string path = definitionPath + ".presentationBindings[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (binding == null)
                {
                    diagnostics.Add(Error("invalid_presentation_binding", path, "Presentation binding entry is null."));
                    continue;
                }
                if (!IsIdentifier(binding.descriptorId))
                    diagnostics.Add(Error("invalid_presentation_descriptor_id", path + ".descriptorId", "Presentation descriptor ID must be a stable lowercase identifier."));
                else if (binding.descriptorId != FurniturePresentationDescriptorIds.DispenserCollectMock)
                    diagnostics.Add(Error("unknown_presentation_descriptor", path + ".descriptorId", "Presentation descriptor is not registered by the trusted Furniture runtime."));

                if (!IsIdentifier(binding.pointId))
                    diagnostics.Add(Error("invalid_presentation_point_id", path + ".pointId", "Presentation point ID must be a stable lowercase identifier."));
                if (!IsIdentifier(binding.useId))
                    diagnostics.Add(Error("invalid_presentation_use_id", path + ".useId", "Presentation use ID must be a stable lowercase identifier."));

                FurnitureFunctionalPoint point = (definition.points ?? new List<FurnitureFunctionalPoint>())
                    .FirstOrDefault(candidate => candidate != null && candidate.pointId == binding.pointId);
                if (point == null)
                    diagnostics.Add(Error("unknown_presentation_point", path + ".pointId", "Presentation binding references a point not declared by this definition."));
                else if (point.useIds == null || !point.useIds.Contains(binding.useId, StringComparer.Ordinal))
                    diagnostics.Add(Error("unknown_presentation_use_id", path + ".useId", "Presentation use ID must be declared by its bound functional point."));

                string key = (binding.descriptorId ?? string.Empty) + "\0" + (binding.pointId ?? string.Empty) + "\0" + (binding.useId ?? string.Empty);
                if (!descriptors.Add(key))
                    diagnostics.Add(Error("duplicate_presentation_binding", path, "Presentation bindings must be unique by descriptor, point and use ID."));
            }
        }

        private static void ValidateRules(IList<FurnitureRelationshipRule> rules, IList<FurnitureDefinition> definitions, List<FurnitureDiagnostic> diagnostics)
        {
            if (rules == null)
            {
                diagnostics.Add(Error("missing_relationship_rules", "relationshipRules", "Relationship rule list is required."));
                return;
            }
            var pointTypes = GetPointTypes(definitions);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                var path = "relationshipRules[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (rule == null)
                {
                    diagnostics.Add(Error("invalid_relationship_rule", path, "Relationship rule entry is null."));
                    continue;
                }
                if (!IsIdentifier(rule.relationshipRuleId))
                    diagnostics.Add(Error("invalid_relationship_rule_id", path + ".relationshipRuleId", "Relationship rule ID must be a stable lowercase identifier."));
                else if (!ids.Add(rule.relationshipRuleId))
                    diagnostics.Add(Error("duplicate_relationship_rule_id", path + ".relationshipRuleId", "Relationship rule IDs must be unique."));
                if (!RuleKinds.Contains(rule.kind ?? string.Empty))
                    diagnostics.Add(Error("unsupported_relationship_kind", path + ".kind", "Relationship kind is unsupported."));
                if (rule.pointTypeIds == null || rule.pointTypeIds.Count == 0)
                    diagnostics.Add(Error("missing_relationship_point_types", path + ".pointTypeIds", "At least one point type is required."));
                else
                {
                    ValidateUniqueStrings(rule.pointTypeIds, path + ".pointTypeIds", "duplicate_relationship_point_type", diagnostics);
                    for (var typeIndex = 0; typeIndex < rule.pointTypeIds.Count; typeIndex++)
                    {
                        var pointTypeId = rule.pointTypeIds[typeIndex];
                        if (!IsIdentifier(pointTypeId) || !pointTypes.Contains(pointTypeId ?? string.Empty))
                            diagnostics.Add(Error("unknown_point_type", path + ".pointTypeIds[" + typeIndex.ToString(CultureInfo.InvariantCulture) + "]", "Relationship references a point type not declared by a furniture definition."));
                    }
                }

                ValidateNonNegativeFinite(rule.broadPhaseRadiusMeters, path + ".broadPhaseRadiusMeters", diagnostics, true);
                ValidateNonNegativeFinite(rule.minimumDistanceMeters, path + ".minimumDistanceMeters", diagnostics, false);
                ValidateNonNegativeFinite(rule.maximumDistanceMeters, path + ".maximumDistanceMeters", diagnostics, true);
                ValidateNonNegativeFinite(rule.maximumFacingErrorDegrees, path + ".maximumFacingErrorDegrees", diagnostics, false);
                ValidateNonNegativeFinite(rule.maximumHeightDifferenceMeters, path + ".maximumHeightDifferenceMeters", diagnostics, false);
                ValidateNonNegativeFinite(rule.lateralAllowanceMeters, path + ".lateralAllowanceMeters", diagnostics, false);
                if (IsFinite(rule.minimumDistanceMeters) && IsFinite(rule.maximumDistanceMeters) && rule.minimumDistanceMeters > rule.maximumDistanceMeters)
                    diagnostics.Add(Error("invalid_relationship_distance", path, "Minimum distance cannot exceed maximum distance."));
                if (IsFinite(rule.maximumFacingErrorDegrees) && rule.maximumFacingErrorDegrees > 180d)
                    diagnostics.Add(Error("invalid_facing_tolerance", path + ".maximumFacingErrorDegrees", "Facing tolerance must be between 0 and 180 degrees."));
            }
        }

        private static void ValidateCapabilities(IList<FurnitureCapabilityDescriptor> capabilities, IList<FurnitureDefinition> definitions, List<FurnitureDiagnostic> diagnostics)
        {
            if (capabilities == null)
            {
                diagnostics.Add(Error("missing_capabilities", "capabilities", "Capability descriptor list is required."));
                return;
            }
            var pointTypes = GetPointTypes(definitions);
            var useIds = GetUseIds(definitions);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < capabilities.Count; index++)
            {
                var capability = capabilities[index];
                var path = "capabilities[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (capability == null)
                {
                    diagnostics.Add(Error("invalid_capability_descriptor", path, "Capability descriptor entry is null."));
                    continue;
                }
                if (!IsIdentifier(capability.capabilityId))
                    diagnostics.Add(Error("invalid_capability_id", path + ".capabilityId", "Capability ID must be a stable lowercase identifier."));
                else if (!ids.Add(capability.capabilityId))
                    diagnostics.Add(Error("duplicate_capability_id", path + ".capabilityId", "Capability IDs must be unique."));

                if ((capability.requiredPointTypeIds == null || capability.requiredPointTypeIds.Count == 0) &&
                    (capability.requiredUseIds == null || capability.requiredUseIds.Count == 0))
                    diagnostics.Add(Error("missing_capability_requirements", path, "Capability descriptor must reference at least one point type or use ID."));
                if (capability.requiredPointTypeIds != null)
                {
                    ValidateUniqueStrings(capability.requiredPointTypeIds, path + ".requiredPointTypeIds", "duplicate_capability_point_type", diagnostics);
                    for (var typeIndex = 0; typeIndex < capability.requiredPointTypeIds.Count; typeIndex++)
                        if (!pointTypes.Contains(capability.requiredPointTypeIds[typeIndex] ?? string.Empty))
                            diagnostics.Add(Error("unknown_point_type", path + ".requiredPointTypeIds[" + typeIndex.ToString(CultureInfo.InvariantCulture) + "]", "Capability references a point type not declared by a furniture definition."));
                }
                if (capability.requiredUseIds != null)
                {
                    ValidateUniqueStrings(capability.requiredUseIds, path + ".requiredUseIds", "duplicate_capability_use_id", diagnostics);
                    for (var useIndex = 0; useIndex < capability.requiredUseIds.Count; useIndex++)
                    {
                        var useId = capability.requiredUseIds[useIndex];
                        if (!IsIdentifier(useId) || !useIds.Contains(useId ?? string.Empty))
                            diagnostics.Add(Error("unknown_use_id", path + ".requiredUseIds[" + useIndex.ToString(CultureInfo.InvariantCulture) + "]", "Capability references a use ID not declared by a functional point."));
                    }
                }
            }
        }

        private static void ValidateAliases(IList<FurnitureLegacyAlias> aliases, IList<FurnitureDefinition> definitions, List<FurnitureDiagnostic> diagnostics)
        {
            if (aliases == null)
            {
                diagnostics.Add(Error("missing_legacy_aliases", "legacyAliases", "Legacy alias list is required (it may be empty)."));
                return;
            }
            var definitionsById = (definitions ?? new List<FurnitureDefinition>())
                .Where(definition => definition != null && IsIdentifier(definition.definitionId))
                .GroupBy(definition => definition.definitionId, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < aliases.Count; index++)
            {
                var alias = aliases[index];
                var path = "legacyAliases[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (alias == null)
                {
                    diagnostics.Add(Error("invalid_legacy_alias", path, "Legacy alias entry is null."));
                    continue;
                }
                if (!IsIdentifier(alias.legacyId))
                    diagnostics.Add(Error("invalid_legacy_id", path + ".legacyId", "Legacy ID must be a stable lowercase identifier."));
                else if (!ids.Add(alias.legacyId))
                    diagnostics.Add(Error("duplicate_legacy_id", path + ".legacyId", "Legacy IDs must be unique."));
                FurnitureDefinition definition;
                if (!definitionsById.TryGetValue(alias.definitionId ?? string.Empty, out definition))
                {
                    diagnostics.Add(Error("unknown_definition_reference", path + ".definitionId", "Legacy alias references an unknown or duplicate definition."));
                    continue;
                }
                if (!string.IsNullOrEmpty(alias.pointId) &&
                    (definition.points == null || !definition.points.Any(point => point != null && point.pointId == alias.pointId)))
                    diagnostics.Add(Error("unknown_point_reference", path + ".pointId", "Legacy alias references a point not declared by its definition."));
            }
        }

        private static HashSet<string> GetPointTypes(IList<FurnitureDefinition> definitions)
        {
            return new HashSet<string>((definitions ?? new List<FurnitureDefinition>())
                .Where(definition => definition != null && definition.points != null)
                .SelectMany(definition => definition.points)
                .Where(point => point != null && IsIdentifier(point.pointTypeId))
                .Select(point => point.pointTypeId), StringComparer.Ordinal);
        }

        private static HashSet<string> GetUseIds(IList<FurnitureDefinition> definitions)
        {
            return new HashSet<string>((definitions ?? new List<FurnitureDefinition>())
                .Where(definition => definition != null && definition.points != null)
                .SelectMany(definition => definition.points)
                .Where(point => point != null && point.useIds != null)
                .SelectMany(point => point.useIds)
                .Where(IsIdentifier), StringComparer.Ordinal);
        }

        private static void ValidateBounds(FurnitureBounds bounds, string path, List<FurnitureDiagnostic> diagnostics)
        {
            if (bounds == null)
            {
                diagnostics.Add(Error("missing_bounds", path, "Local bounds are required."));
                return;
            }
            ValidateVector(bounds.minimum, path + ".minimum", diagnostics, false);
            ValidateVector(bounds.maximum, path + ".maximum", diagnostics, false);
            if (bounds.minimum != null && bounds.maximum != null &&
                (bounds.minimum.x >= bounds.maximum.x || bounds.minimum.y >= bounds.maximum.y || bounds.minimum.z >= bounds.maximum.z))
                diagnostics.Add(Error("invalid_bounds", path, "Each local bounds minimum must be below its maximum."));
        }

        private static void ValidateVector(FurnitureVector3 value, string path, List<FurnitureDiagnostic> diagnostics, bool positive)
        {
            if (value == null)
            {
                diagnostics.Add(Error("missing_geometry", path, "Vector geometry is required."));
                return;
            }
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                diagnostics.Add(Error("non_finite_geometry", path, "Geometry values must be finite numbers."));
            if (positive && IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && (value.x <= 0d || value.y <= 0d || value.z <= 0d))
                diagnostics.Add(Error("invalid_scale", path, "Scale components must be positive."));
        }

        private static void ValidateNonNegativeFinite(double value, string path, List<FurnitureDiagnostic> diagnostics, bool positive)
        {
            if (!IsFinite(value))
                diagnostics.Add(Error("non_finite_geometry", path, "Geometry values must be finite numbers."));
            else if (positive ? value <= 0d : value < 0d)
                diagnostics.Add(Error("invalid_geometry_range", path, "Geometry value is outside the supported non-negative range."));
        }

        private static void ValidateUniqueStrings(IList<string> values, string path, string code, List<FurnitureDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < values.Count; index++)
            {
                if (!seen.Add(values[index] ?? string.Empty))
                    diagnostics.Add(Error(code, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", "Values in this list must be unique."));
            }
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var segmentStart = true;
            foreach (var character in value)
            {
                if (character == '.')
                {
                    if (segmentStart) return false;
                    segmentStart = true;
                    continue;
                }
                if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && character != '_') return false;
                if (character == '_' && segmentStart) return false;
                segmentStart = false;
            }
            return !segmentStart;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double LengthSquared(FurnitureVector3 value)
        {
            return value.x * value.x + value.y * value.y + value.z * value.z;
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) <= 1e-9d;
        }

        private static FurnitureDiagnostic Error(string code, string path, string message)
        {
            return new FurnitureDiagnostic(code, "error", path, message);
        }
    }
}
