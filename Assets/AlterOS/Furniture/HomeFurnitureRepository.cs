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
    public static class HomeFurnitureRepository
    {
        private const string CoordinateFrame = "room_local_y_up_positive_z_forward";
        private static readonly object SaveGate = new object();

        public static HomeFurnitureLayoutLoadResult Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return LoadFailure("read_error", "path", "Layout path is required.");
            if (!File.Exists(path))
                return new HomeFurnitureLayoutLoadResult
                {
                    state = "missing", document = null, contentHash = string.Empty,
                    orphanInstanceIds = new List<string>().AsReadOnly(), diagnostics = new List<FurnitureDiagnostic>().AsReadOnly()
                };

            byte[] bytes;
            HomeFurnitureLayoutDocument document;
            try
            {
                bytes = File.ReadAllBytes(path);
                using (var stream = new MemoryStream(bytes, false))
                    document = (HomeFurnitureLayoutDocument)new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument)).ReadObject(stream);
            }
            catch (Exception exception)
            {
                return LoadFailure("parse_error", "layout", SafeMessage(exception));
            }
            if (document == null) return LoadFailure("invalid_document", "$", "Layout JSON did not contain a document.");

            var diagnostics = ValidateDocument(document).ToList();
            var hash = Hash(bytes);
            return new HomeFurnitureLayoutLoadResult
            {
                state = diagnostics.Any(diagnostic => diagnostic.severity == "error") ? "invalid" : "loaded",
                document = document,
                contentHash = hash,
                orphanInstanceIds = new List<string>().AsReadOnly(),
                diagnostics = Sort(diagnostics)
            };
        }

        public static HomeFurnitureLayoutLoadResult Load(
            string path,
            FurnitureCatalogueSnapshot catalogue,
            IList<FurnitureSupportSnapshot> supports)
        {
            var result = Load(path);
            if (result.state != "loaded" || result.document == null) return result;

            var knownDefinitions = new HashSet<string>((catalogue == null ? new List<FurnitureDefinition>() : catalogue.definitions)
                .Where(definition => definition != null).Select(definition => definition.definitionId), StringComparer.Ordinal);
            var supportById = (supports ?? new List<FurnitureSupportSnapshot>())
                .Where(support => support != null && !string.IsNullOrEmpty(support.supportId))
                .GroupBy(support => support.supportId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var orphanIds = new HashSet<string>(StringComparer.Ordinal);
            var diagnostics = result.diagnostics.ToList();
            for (var index = 0; index < result.document.instances.Count; index++)
            {
                var instance = result.document.instances[index];
                var recordPath = "instances[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!knownDefinitions.Contains(instance.definitionId))
                {
                    orphanIds.Add(instance.instanceId);
                    diagnostics.Add(new FurnitureDiagnostic("missing_definition", "warning", recordPath + ".definitionId",
                        "Saved furniture definition is unavailable; the instance remains preserved and inert."));
                }

                FurnitureSupportSnapshot support;
                if (instance.support == null || !supportById.TryGetValue(instance.support.supportId ?? string.Empty, out support) ||
                    support.revision != instance.support.supportFrameRevision || support.supportKind != instance.support.supportKind)
                {
                    orphanIds.Add(instance.instanceId);
                    diagnostics.Add(new FurnitureDiagnostic("missing_support", "warning", recordPath + ".support",
                        "Saved furniture support is unavailable or changed; the instance remains preserved and inert."));
                }
            }

            return new HomeFurnitureLayoutLoadResult
            {
                state = result.state,
                document = result.document,
                contentHash = result.contentHash,
                orphanInstanceIds = orphanIds.OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly(),
                diagnostics = Sort(diagnostics)
            };
        }

        public static HomeFurnitureLayoutLoadResult LoadOrSeed(string path, HomeFurnitureLayoutDocument sceneSeed)
        {
            var existing = Load(path);
            if (existing.state != "missing") return existing;
            var save = Save(path, sceneSeed, null);
            if (save.state == "saved") return Load(path);
            if (save.state == "conflict" && File.Exists(path)) return Load(path);
            return LoadFailure(save.state == "invalid" ? "invalid_seed" : "seed_save_failed", "seed", save.diagnostic == null ? "Could not initialize the home layout." : save.diagnostic.message);
        }

        public static HomeFurnitureLayoutSaveResult Save(string path, HomeFurnitureLayoutDocument document, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(path)) return SaveFailure("invalid", "path", "Layout path is required.");
            if (document == null) return SaveFailure("invalid", "$", "Layout document is required.");
            var diagnostics = ValidateDocument(document);
            var blocking = diagnostics.FirstOrDefault(diagnostic => diagnostic.severity == "error");
            if (blocking != null) return new HomeFurnitureLayoutSaveResult { state = "invalid", contentHash = string.Empty, diagnostic = blocking };

            byte[] bytes;
            try
            {
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument)).WriteObject(stream, document);
                    bytes = stream.ToArray();
                }
            }
            catch (Exception exception)
            {
                return SaveFailure("error", "layout", SafeMessage(exception));
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) return SaveFailure("error", "path", "Layout parent directory is unavailable.");
            try
            {
                Directory.CreateDirectory(directory);
                lock (SaveGate)
                {
                    var exists = File.Exists(fullPath);
                    if (exists)
                    {
                        if (expectedHash == null) return SaveFailure("conflict", "expectedHash", "A layout already exists; an expected hash is required to replace it.");
                        var currentHash = Hash(File.ReadAllBytes(fullPath));
                        if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
                            return SaveFailure("conflict", "expectedHash", "Saved layout changed since it was loaded.");
                    }
                    else if (expectedHash != null)
                    {
                        return SaveFailure("conflict", "expectedHash", "The expected saved layout no longer exists.");
                    }

                    var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
                    var backupPath = fullPath + ".bak-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            output.Write(bytes, 0, bytes.Length);
                            output.Flush(true);
                        }
                        if (exists)
                            File.Replace(temporaryPath, fullPath, backupPath, true);
                        else
                            File.Move(temporaryPath, fullPath);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                    }
                }
                return new HomeFurnitureLayoutSaveResult { state = "saved", contentHash = Hash(bytes), diagnostic = null };
            }
            catch (Exception exception)
            {
                return SaveFailure("error", "layout", SafeMessage(exception));
            }
        }

        internal static IReadOnlyList<FurnitureDiagnostic> ValidateDocument(HomeFurnitureLayoutDocument document)
        {
            var diagnostics = new List<FurnitureDiagnostic>();
            if (document.schemaVersion != 1 && document.schemaVersion != 2)
                diagnostics.Add(Error("unsupported_schema_version", "schemaVersion", "Home furniture layout schema version is unsupported."));
            if (string.IsNullOrWhiteSpace(document.homeId)) diagnostics.Add(Error("missing_home_id", "homeId", "Home ID is required."));
            if (string.IsNullOrWhiteSpace(document.roomId)) diagnostics.Add(Error("missing_room_id", "roomId", "Room ID is required."));
            if (string.IsNullOrWhiteSpace(document.catalogueVersion)) diagnostics.Add(Error("missing_catalogue_version", "catalogueVersion", "Catalogue version is required."));
            if (string.IsNullOrWhiteSpace(document.seedVersion)) diagnostics.Add(Error("missing_seed_version", "seedVersion", "Seed version is required."));
            if (document.coordinateFrame != CoordinateFrame) diagnostics.Add(Error("unsupported_coordinate_frame", "coordinateFrame", "Layout coordinate frame is unsupported."));
            if (document.lengthUnit != "m") diagnostics.Add(Error("unsupported_length_unit", "lengthUnit", "Layout length unit must be metres (m)."));
            if (document.layoutRevision < 0) diagnostics.Add(Error("invalid_layout_revision", "layoutRevision", "Layout revision cannot be negative."));
            if (document.schemaVersion == 2)
                diagnostics.AddRange(FurnitureInventoryValidator.Validate(document.inventory));
            else if (document.schemaVersion == 1 && document.inventory != null && document.inventory.Count > 0)
                diagnostics.Add(Error("inventory_requires_schema_two", "inventory", "Inventory data requires home furniture layout schema version 2."));
            if (document.instances == null)
            {
                diagnostics.Add(Error("missing_instances", "instances", "Instance list is required."));
                return diagnostics.AsReadOnly();
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < document.instances.Count; index++)
            {
                var instance = document.instances[index];
                var path = "instances[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (instance == null)
                {
                    diagnostics.Add(Error("invalid_instance", path, "Instance record is null."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(instance.instanceId)) diagnostics.Add(Error("missing_instance_id", path + ".instanceId", "Instance ID is required."));
                else if (!seenIds.Add(instance.instanceId)) diagnostics.Add(Error("duplicate_instance_id", path + ".instanceId", "Instance IDs must be unique."));
                if (string.IsNullOrWhiteSpace(instance.definitionId)) diagnostics.Add(Error("missing_definition_id", path + ".definitionId", "Definition ID is required."));
                ValidateVector(instance.positionMeters, path + ".positionMeters", diagnostics, false);
                ValidateVector(instance.scale, path + ".scale", diagnostics, true);
                ValidateQuaternion(instance.rotation, path + ".rotation", diagnostics);
                if (instance.instanceRevision < 0) diagnostics.Add(Error("invalid_instance_revision", path + ".instanceRevision", "Instance revision cannot be negative."));
                if (string.IsNullOrWhiteSpace(instance.conditionState)) diagnostics.Add(Error("missing_condition_state", path + ".conditionState", "Condition state is required."));
                if (instance.support == null)
                    diagnostics.Add(Error("missing_support", path + ".support", "Support identity and attachment pose are required."));
                else
                {
                    if (string.IsNullOrWhiteSpace(instance.support.supportId)) diagnostics.Add(Error("missing_support_id", path + ".support.supportId", "Support ID is required."));
                    if (string.IsNullOrWhiteSpace(instance.support.supportKind)) diagnostics.Add(Error("missing_support_kind", path + ".support.supportKind", "Support kind is required."));
                    if (string.IsNullOrWhiteSpace(instance.support.supportFrameRevision)) diagnostics.Add(Error("missing_support_revision", path + ".support.supportFrameRevision", "Support frame revision is required."));
                    if (string.IsNullOrWhiteSpace(instance.support.attachmentPointId)) diagnostics.Add(Error("missing_attachment_point", path + ".support.attachmentPointId", "Attachment point ID is required."));
                    ValidateVector(instance.support.localPositionMeters, path + ".support.localPositionMeters", diagnostics, false);
                    ValidateQuaternion(instance.support.localRotation, path + ".support.localRotation", diagnostics);
                }
                if (instance.resourceState == null)
                {
                    diagnostics.Add(Error("missing_resource_state", path + ".resourceState", "Resource state list is required (it may be empty)."));
                    continue;
                }
                var resourceIds = new HashSet<string>(StringComparer.Ordinal);
                for (var resourceIndex = 0; resourceIndex < instance.resourceState.Count; resourceIndex++)
                {
                    var resource = instance.resourceState[resourceIndex];
                    if (resource == null || string.IsNullOrWhiteSpace(resource.resourceId) || !IsFinite(resource.value))
                        diagnostics.Add(Error("invalid_resource_state", path + ".resourceState[" + resourceIndex.ToString(CultureInfo.InvariantCulture) + "]", "Resource state requires an ID and finite value."));
                    else if (!resourceIds.Add(resource.resourceId))
                        diagnostics.Add(Error("duplicate_resource_id", path + ".resourceState[" + resourceIndex.ToString(CultureInfo.InvariantCulture) + "].resourceId", "Resource IDs must be unique per instance."));
                }
            }
            return diagnostics.OrderBy(diagnostic => diagnostic.path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.code, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        internal static HomeFurnitureLayoutDocument CloneDocument(HomeFurnitureLayoutDocument document)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(HomeFurnitureLayoutDocument));
                serializer.WriteObject(stream, document);
                stream.Position = 0;
                return (HomeFurnitureLayoutDocument)serializer.ReadObject(stream);
            }
        }

        internal static HomeFurnitureLayoutInstance CloneInstance(HomeFurnitureLayoutInstance instance)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(HomeFurnitureLayoutInstance));
                serializer.WriteObject(stream, instance);
                stream.Position = 0;
                return (HomeFurnitureLayoutInstance)serializer.ReadObject(stream);
            }
        }

        internal static FurnitureLayoutSupportReference CloneSupport(FurnitureLayoutSupportReference support)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(FurnitureLayoutSupportReference));
                serializer.WriteObject(stream, support);
                stream.Position = 0;
                return (FurnitureLayoutSupportReference)serializer.ReadObject(stream);
            }
        }

        private static void ValidateVector(FurnitureVector3 value, string path, List<FurnitureDiagnostic> diagnostics, bool positive)
        {
            if (value == null)
            {
                diagnostics.Add(Error("missing_vector", path, "Vector is required."));
                return;
            }
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                diagnostics.Add(Error("non_finite_transform", path, "Transform values must be finite."));
            if (positive && IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && (value.x <= 0d || value.y <= 0d || value.z <= 0d))
                diagnostics.Add(Error("invalid_scale", path, "Scale values must be positive."));
        }

        private static void ValidateQuaternion(FurnitureQuaternion value, string path, List<FurnitureDiagnostic> diagnostics)
        {
            if (value == null)
            {
                diagnostics.Add(Error("missing_rotation", path, "Quaternion rotation is required."));
                return;
            }
            var components = new[] { value.x, value.y, value.z, value.w };
            if (components.Any(component => !IsFinite(component)))
            {
                diagnostics.Add(Error("non_finite_transform", path, "Quaternion components must be finite."));
                return;
            }
            var magnitude = Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (Math.Abs(magnitude - 1d) > 0.001d)
                diagnostics.Add(Error("invalid_rotation", path, "Quaternion must be normalized within 0.001."));
        }

        private static HomeFurnitureLayoutLoadResult LoadFailure(string code, string path, string message)
        {
            return new HomeFurnitureLayoutLoadResult
            {
                state = code == "parse_error" || code == "invalid_document" ? "invalid" : "error",
                document = null,
                contentHash = string.Empty,
                orphanInstanceIds = new List<string>().AsReadOnly(),
                diagnostics = new List<FurnitureDiagnostic> { Error(code, path, message) }.AsReadOnly()
            };
        }

        private static HomeFurnitureLayoutSaveResult SaveFailure(string state, string path, string message)
        {
            return new HomeFurnitureLayoutSaveResult
            {
                state = state,
                contentHash = string.Empty,
                diagnostic = Error(state + "_layout", path, message)
            };
        }

        private static FurnitureDiagnostic Error(string code, string path, string message)
        {
            return new FurnitureDiagnostic(code, "error", path, message);
        }

        private static List<FurnitureDiagnostic> Sort(IEnumerable<FurnitureDiagnostic> diagnostics)
        {
            return diagnostics.OrderBy(diagnostic => diagnostic.path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.code, StringComparer.Ordinal).ToList();
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(bytes);
                var builder = new StringBuilder(digest.Length * 2);
                foreach (var value in digest) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string SafeMessage(Exception exception)
        {
            return exception == null ? "Unknown layout error." : exception.Message;
        }
    }
}
