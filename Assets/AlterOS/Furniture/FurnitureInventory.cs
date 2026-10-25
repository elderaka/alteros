using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AlterOS.Furniture
{
    [DataContract]
    public sealed class FurnitureInventoryEntry
    {
        [DataMember(Name = "definitionId", Order = 0)] public string definitionId;
        [DataMember(Name = "storedCount", Order = 1)] public int storedCount;
    }

    public static class FurnitureInventorySeed
    {
        public const int InitialStoredCount = 10;

        private static readonly string[] InitialDefinitionIds =
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

        public static List<FurnitureInventoryEntry> CreateInitial()
        {
            return InitialDefinitionIds.Select(definitionId => new FurnitureInventoryEntry
            {
                definitionId = definitionId,
                storedCount = InitialStoredCount
            }).ToList();
        }

        public static List<FurnitureInventoryEntry> CreateInitial(FurnitureCatalogueSnapshot catalogue)
        {
            var availableIds = new HashSet<string>((catalogue == null ? new List<FurnitureDefinition>() : catalogue.definitions)
                .Where(definition => definition != null).Select(definition => definition.definitionId), System.StringComparer.Ordinal);
            return InitialDefinitionIds.Where(availableIds.Contains)
                .Select(definitionId => new FurnitureInventoryEntry { definitionId = definitionId, storedCount = InitialStoredCount })
                .ToList();
        }
    }

    public static class FurnitureInventoryValidator
    {
        public static IReadOnlyList<FurnitureDiagnostic> Validate(IEnumerable<FurnitureInventoryEntry> entries)
        {
            var diagnostics = new List<FurnitureDiagnostic>();
            if (entries == null)
                return new[] { new FurnitureDiagnostic("missing_inventory", "error", "inventory", "Inventory list is required.") };

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            var index = 0;
            foreach (var entry in entries)
            {
                var path = "inventory[" + index + "]";
                if (entry == null)
                {
                    diagnostics.Add(new FurnitureDiagnostic("invalid_entry", "error", path, "Inventory entry is null."));
                }
                else
                {
                    if (!IsValidDefinitionId(entry.definitionId))
                        diagnostics.Add(new FurnitureDiagnostic("invalid_definition_id", "error", path + ".definitionId", "Inventory definition ID is invalid."));
                    else if (!seen.Add(entry.definitionId))
                        diagnostics.Add(new FurnitureDiagnostic("duplicate_definition", "error", path + ".definitionId", "Inventory definition IDs must be unique."));
                    if (entry.storedCount < 0)
                        diagnostics.Add(new FurnitureDiagnostic("negative_stock", "error", path + ".storedCount", "Stored inventory count cannot be negative."));
                }
                index++;
            }
            return diagnostics.OrderBy(diagnostic => diagnostic.path, System.StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.code, System.StringComparer.Ordinal).ToList().AsReadOnly();
        }

        public static bool IsValidDefinitionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Trim() != value) return false;
            var segmentStart = true;
            foreach (char character in value)
            {
                if (character == '.')
                {
                    if (segmentStart) return false;
                    segmentStart = true;
                    continue;
                }
                if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && character != '_')
                    return false;
                if (character == '_' && segmentStart) return false;
                segmentStart = false;
            }
            return !segmentStart;
        }
    }

    public sealed class FurnitureHomeReadResult
    {
        public string state;
        public string errorCode;
        public string message;
        public HomeFurnitureLayoutDocument document;
        public long gameRevision;
        public long layoutRevision;
    }

    public sealed class FurnitureHomeCommitResult
    {
        public string state;
        public string errorCode;
        public string message;
        public bool durable;
        public HomeFurnitureLayoutDocument document;
        public long gameRevision;
        public long layoutRevision;
    }

    public interface IFurnitureHomeStore
    {
        FurnitureHomeReadResult Read();
        FurnitureHomeCommitResult Commit(string operationId, long expectedGameRevision,
            long expectedLayoutRevision, HomeFurnitureLayoutDocument candidate);
    }
}
