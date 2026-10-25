using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace AlterOS.Furniture
{
    public static class FurnitureCatalogueRegistry
    {
        public static FurnitureCatalogueSnapshot Load(string cataloguePath)
        {
            if (string.IsNullOrWhiteSpace(cataloguePath))
                return Failure("catalogue_path_required", "cataloguePath", "Catalogue path is required.");

            FurnitureCatalogueDocument document;
            try
            {
                using (var stream = File.OpenRead(cataloguePath))
                    document = (FurnitureCatalogueDocument)new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).ReadObject(stream);
            }
            catch (Exception exception)
            {
                return Failure("parse_error", "catalogue", SafeExceptionMessage(exception));
            }

            if (document == null)
            {
                return Failure("invalid_document", "$", "Catalogue JSON did not contain a document.");
            }

            return CreateSnapshot(document);
        }

        public static FurnitureCatalogueSnapshot Parse(string catalogueJson)
        {
            if (string.IsNullOrWhiteSpace(catalogueJson))
                return Failure("catalogue_json_required", "catalogue", "Catalogue JSON is required.");
            try
            {
                var bytes = Encoding.UTF8.GetBytes(catalogueJson);
                FurnitureCatalogueDocument document;
                using (var stream = new MemoryStream(bytes, false))
                    document = (FurnitureCatalogueDocument)new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).ReadObject(stream);
                return document == null
                    ? Failure("invalid_document", "$", "Catalogue JSON did not contain a document.")
                    : CreateSnapshot(document);
            }
            catch (Exception exception)
            {
                return Failure("parse_error", "catalogue", SafeExceptionMessage(exception));
            }
        }

        public static FurnitureCatalogueSnapshot CreateSnapshot(FurnitureCatalogueDocument document)
        {
            if (document == null)
                return Failure("invalid_document", "$", "Catalogue document is null.");

            var diagnostics = FurnitureCatalogueValidator.Validate(document).ToList();
            string hash;
            try
            {
                hash = ComputeHash(document);
            }
            catch (Exception exception)
            {
                hash = string.Empty;
                diagnostics.Add(new FurnitureDiagnostic("catalogue_hash_error", "error", "catalogue", SafeExceptionMessage(exception)));
                diagnostics = SortDiagnostics(diagnostics);
            }

            foreach (FurnitureDefinition definition in document.definitions ?? new List<FurnitureDefinition>())
            {
                if (definition == null) continue;
                if (definition.functionBindings == null) definition.functionBindings = new List<FurnitureFunctionBindingDescriptor>();
                if (definition.presentationBindings == null) definition.presentationBindings = new List<FurniturePresentationBindingDescriptor>();
            }

            var supportedSchema = document.schemaVersion == 1 &&
                document.lengthUnit == "m" &&
                document.angleUnit == "deg" &&
                document.coordinateConvention == "unity_local_y_up_positive_z_forward";
            var missingStarter = diagnostics.Any(diagnostic => diagnostic.code == "missing_starter_definition");
            if (!supportedSchema || missingStarter)
                return Snapshot(document, hash, new List<FurnitureDefinition>(), new List<FurnitureRelationshipRule>(),
                    new List<FurnitureCapabilityDescriptor>(), new List<FurnitureLegacyAlias>(), diagnostics);

            var invalidDefinitionIndexes = RecordIndexes(diagnostics, "definitions");
            var definitions = (document.definitions ?? new List<FurnitureDefinition>())
                .Where((definition, index) => definition != null && !invalidDefinitionIndexes.Contains(index))
                .OrderBy(definition => definition.definitionId, StringComparer.Ordinal)
                .ToList();
            var usablePointTypes = new HashSet<string>(definitions
                .Where(definition => definition.points != null)
                .SelectMany(definition => definition.points)
                .Where(point => point != null)
                .Select(point => point.pointTypeId), StringComparer.Ordinal);
            var usableUseIds = new HashSet<string>(definitions
                .Where(definition => definition.points != null)
                .SelectMany(definition => definition.points)
                .Where(point => point != null && point.useIds != null)
                .SelectMany(point => point.useIds), StringComparer.Ordinal);

            var invalidRuleIndexes = RecordIndexes(diagnostics, "relationshipRules");
            var relationshipRules = (document.relationshipRules ?? new List<FurnitureRelationshipRule>())
                .Where((rule, index) => rule != null && !invalidRuleIndexes.Contains(index) &&
                    rule.pointTypeIds != null && rule.pointTypeIds.All(usablePointTypes.Contains))
                .OrderBy(rule => rule.relationshipRuleId, StringComparer.Ordinal)
                .ToList();

            var invalidCapabilityIndexes = RecordIndexes(diagnostics, "capabilities");
            var capabilities = (document.capabilities ?? new List<FurnitureCapabilityDescriptor>())
                .Where((capability, index) => capability != null && !invalidCapabilityIndexes.Contains(index) &&
                    (capability.requiredPointTypeIds == null || capability.requiredPointTypeIds.All(usablePointTypes.Contains)) &&
                    (capability.requiredUseIds == null || capability.requiredUseIds.All(usableUseIds.Contains)))
                .OrderBy(capability => capability.capabilityId, StringComparer.Ordinal)
                .ToList();

            var invalidAliasIndexes = RecordIndexes(diagnostics, "legacyAliases");
            var usableDefinitionIds = new HashSet<string>(definitions.Select(definition => definition.definitionId), StringComparer.Ordinal);
            var pointRefs = new HashSet<string>(definitions
                .Where(definition => definition.points != null)
                .SelectMany(definition => definition.points.Where(point => point != null)
                    .Select(point => definition.definitionId + "/" + point.pointId)), StringComparer.Ordinal);
            var legacyAliases = (document.legacyAliases ?? new List<FurnitureLegacyAlias>())
                .Where((alias, index) => alias != null && !invalidAliasIndexes.Contains(index) &&
                    usableDefinitionIds.Contains(alias.definitionId) &&
                    (string.IsNullOrEmpty(alias.pointId) || pointRefs.Contains(alias.definitionId + "/" + alias.pointId)))
                .OrderBy(alias => alias.legacyId, StringComparer.Ordinal)
                .ToList();

            return Snapshot(document, hash, definitions, relationshipRules, capabilities, legacyAliases, diagnostics);
        }

        public static FurnitureCatalogueSnapshot CreateCompiledSnapshot(FurnitureCatalogueDocument document, string fingerprint)
        {
            if (!IsSha256Fingerprint(fingerprint))
                return Failure("compiled_fingerprint_invalid", "fingerprint", "A 64-character SHA-256 fingerprint is required.");

            FurnitureCatalogueSnapshot contentSnapshot = CreateSnapshot(document);
            if (document == null) return contentSnapshot;

            string compiledIdentity = "compiled-furniture-catalogue-v1\n" + contentSnapshot.catalogueHash + "\n" + fingerprint;
            string compiledHash;
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(compiledIdentity));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                compiledHash = builder.ToString();
            }

            return new FurnitureCatalogueSnapshot(
                contentSnapshot.schemaVersion,
                contentSnapshot.catalogueVersion,
                compiledHash,
                contentSnapshot.definitions.ToList(),
                contentSnapshot.relationshipRules.ToList(),
                contentSnapshot.capabilities.ToList(),
                contentSnapshot.legacyAliases.ToList(),
                contentSnapshot.diagnostics.ToList());
        }

        private static bool IsSha256Fingerprint(string fingerprint)
        {
            if (fingerprint == null || fingerprint.Length != 64) return false;
            foreach (char value in fingerprint)
            {
                if ((value < '0' || value > '9') && (value < 'a' || value > 'f') &&
                    (value < 'A' || value > 'F')) return false;
            }
            return true;
        }

        internal static string ComputeHash(FurnitureCatalogueDocument document)
        {
            var canonical = Canonicalize(document);
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
                serializer.WriteObject(stream, canonical);
                using (var sha = SHA256.Create())
                {
                    var digest = sha.ComputeHash(stream.ToArray());
                    var builder = new StringBuilder(digest.Length * 2);
                    foreach (var value in digest)
                        builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                    return builder.ToString();
                }
            }
        }

        private static FurnitureCatalogueDocument Canonicalize(FurnitureCatalogueDocument source)
        {
            var clone = Clone(source);
            clone.definitions = (clone.definitions ?? new List<FurnitureDefinition>())
                .OrderBy(definition => definition == null ? string.Empty : definition.definitionId, StringComparer.Ordinal)
                .ToList();
            foreach (var definition in clone.definitions.Where(item => item != null))
            {
                definition.supportKinds = (definition.supportKinds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal).ToList();
                definition.points = (definition.points ?? new List<FurnitureFunctionalPoint>())
                    .OrderBy(point => point == null ? string.Empty : point.pointId, StringComparer.Ordinal)
                    .ToList();
                foreach (var point in definition.points.Where(item => item != null))
                    point.useIds = (point.useIds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal).ToList();
                if (definition.functionBindings != null)
                    definition.functionBindings = definition.functionBindings
                        .OrderBy(binding => binding == null ? string.Empty : binding.functionId, StringComparer.Ordinal)
                        .ThenBy(binding => binding == null ? string.Empty : binding.pointId, StringComparer.Ordinal)
                        .ThenBy(binding => binding == null ? string.Empty : binding.useId, StringComparer.Ordinal)
                        .ToList();
                if (definition.presentationBindings != null)
                    definition.presentationBindings = definition.presentationBindings
                        .OrderBy(binding => binding == null ? string.Empty : binding.descriptorId, StringComparer.Ordinal)
                        .ThenBy(binding => binding == null ? string.Empty : binding.pointId, StringComparer.Ordinal)
                        .ThenBy(binding => binding == null ? string.Empty : binding.useId, StringComparer.Ordinal)
                        .ToList();
            }
            clone.relationshipRules = (clone.relationshipRules ?? new List<FurnitureRelationshipRule>())
                .OrderBy(rule => rule == null ? string.Empty : rule.relationshipRuleId, StringComparer.Ordinal)
                .ToList();
            foreach (var rule in clone.relationshipRules.Where(item => item != null))
                rule.pointTypeIds = (rule.pointTypeIds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal).ToList();
            clone.capabilities = (clone.capabilities ?? new List<FurnitureCapabilityDescriptor>())
                .OrderBy(capability => capability == null ? string.Empty : capability.capabilityId, StringComparer.Ordinal)
                .ToList();
            foreach (var capability in clone.capabilities.Where(item => item != null))
            {
                capability.requiredPointTypeIds = (capability.requiredPointTypeIds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal).ToList();
                capability.requiredUseIds = (capability.requiredUseIds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal).ToList();
            }
            clone.legacyAliases = (clone.legacyAliases ?? new List<FurnitureLegacyAlias>())
                .OrderBy(alias => alias == null ? string.Empty : alias.legacyId, StringComparer.Ordinal)
                .ToList();
            return clone;
        }

        private static T Clone<T>(T source)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, source);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }

        private static HashSet<int> RecordIndexes(IList<FurnitureDiagnostic> diagnostics, string recordName)
        {
            var result = new HashSet<int>();
            var prefix = recordName + "[";
            foreach (var diagnostic in diagnostics)
            {
                if (!diagnostic.path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var close = diagnostic.path.IndexOf(']', prefix.Length);
                int index;
                if (close > prefix.Length && int.TryParse(diagnostic.path.Substring(prefix.Length, close - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index))
                    result.Add(index);
            }
            return result;
        }

        private static FurnitureCatalogueSnapshot Failure(string code, string path, string message)
        {
            var diagnostic = new FurnitureDiagnostic(code, "error", path, message);
            return new FurnitureCatalogueSnapshot(0, string.Empty, string.Empty, null, null, null, null, new[] { diagnostic });
        }

        private static FurnitureCatalogueSnapshot Snapshot(
            FurnitureCatalogueDocument document,
            string hash,
            IList<FurnitureDefinition> definitions,
            IList<FurnitureRelationshipRule> relationshipRules,
            IList<FurnitureCapabilityDescriptor> capabilities,
            IList<FurnitureLegacyAlias> legacyAliases,
            IList<FurnitureDiagnostic> diagnostics)
        {
            return new FurnitureCatalogueSnapshot(
                document.schemaVersion,
                document.catalogueVersion,
                hash,
                definitions,
                relationshipRules,
                capabilities,
                legacyAliases,
                SortDiagnostics(diagnostics));
        }

        private static List<FurnitureDiagnostic> SortDiagnostics(IEnumerable<FurnitureDiagnostic> diagnostics)
        {
            return diagnostics
                .OrderBy(diagnostic => diagnostic.path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.message, StringComparer.Ordinal)
                .ToList();
        }

        private static string SafeExceptionMessage(Exception exception)
        {
            return exception == null ? "Unknown catalogue error." : exception.Message;
        }
    }
}
