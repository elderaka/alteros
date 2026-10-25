using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Text;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureInventoryTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [Test]
        public void InitialTestStockAddsTenUnitsForEachStarterDefinitionSeparateFromPlacedFurniture()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var inventory = FurnitureInventorySeed.CreateInitial(catalogue);

            Assert.AreEqual(8, inventory.Count);
            Assert.AreEqual(8, catalogue.definitions.Count);
            foreach (var definition in catalogue.definitions)
                Assert.AreEqual(10, inventory.Single(item => item.definitionId == definition.definitionId).storedCount);
        }

        [Test]
        public void InventoryValidationRejectsBadIdsDuplicatesAndNegativeStockButKeepsRemovedDefinitions()
        {
            var diagnostics = FurnitureInventoryValidator.Validate(new[]
            {
                new FurnitureInventoryEntry { definitionId = "furniture.removed.old_chair", storedCount = 2 },
                new FurnitureInventoryEntry { definitionId = "furniture.seat.basic", storedCount = -1 },
                new FurnitureInventoryEntry { definitionId = "furniture.seat.basic", storedCount = 1 },
                new FurnitureInventoryEntry { definitionId = "../invalid", storedCount = 1 }
            });

            CollectionAssert.AreEquivalent(new[] { "duplicate_definition", "invalid_definition_id", "negative_stock" },
                diagnostics.Select(diagnostic => diagnostic.code));
        }

        [Test]
        public void InventoryDefinitionIdsFollowTheCatalogueStableIdentifierRules()
        {
            Assert.IsTrue(FurnitureInventoryValidator.IsValidDefinitionId("furniture.seat.basic"));
            Assert.IsFalse(FurnitureInventoryValidator.IsValidDefinitionId("Furniture.SEAT"));
            Assert.IsFalse(FurnitureInventoryValidator.IsValidDefinitionId("furniture..seat"));
            Assert.IsFalse(FurnitureInventoryValidator.IsValidDefinitionId("furniture.-seat"));
        }

        [Test]
        public void LaterRegistryDefinitionIsDiscoverableButReceivesNoInitialGrant()
        {
            var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
            FurnitureCatalogueDocument document;
            using (var input = File.OpenRead(CataloguePath))
                document = (FurnitureCatalogueDocument)serializer.ReadObject(input);
            document.definitions.Add(new FurnitureDefinition
            {
                definitionId = "furniture.seat.test_variant", displayNameKey = "furniture.seat.test_variant",
                category = "seat", scalePolicy = "fixed", defaultScale = new FurnitureVector3 { x = 1, y = 1, z = 1 },
                localBounds = new FurnitureBounds
                {
                    minimum = new FurnitureVector3 { x = -0.25, y = 0, z = -0.25 },
                    maximum = new FurnitureVector3 { x = 0.25, y = 0.5, z = 0.25 }
                },
                supportKinds = new List<string> { "roomFloor" }, blocksNavigation = true,
                points = new List<FurnitureFunctionalPoint>()
            });

            string json;
            using (var output = new MemoryStream())
            {
                serializer.WriteObject(output, document);
                json = Encoding.UTF8.GetString(output.ToArray());
            }
            var extendedCatalogue = FurnitureCatalogueRegistry.Parse(json);
            var inventory = FurnitureInventorySeed.CreateInitial(extendedCatalogue);

            Assert.That(extendedCatalogue.diagnostics, Is.Empty);
            Assert.That(extendedCatalogue.definitions.Any(item => item.definitionId == "furniture.seat.test_variant"), Is.True);
            Assert.That(inventory.Any(item => item.definitionId == "furniture.seat.test_variant"), Is.False);
            Assert.AreEqual(8, inventory.Count);
        }

        [Test]
        public void InvalidOrIncompleteCatalogueCannotPartiallySeedInitialStock()
        {
            var json = File.ReadAllText(CataloguePath).Replace(
                "\"definitionId\": \"furniture.table.study\"",
                "\"definitionId\": \"furniture.table.general\"");
            var invalidCatalogue = FurnitureCatalogueRegistry.Parse(json);

            Assert.That(invalidCatalogue.diagnostics.Any(item => item.severity == "error"), Is.True);
            Assert.That(FurnitureInventorySeed.CreateInitial(invalidCatalogue), Is.Empty);
        }
    }
}
