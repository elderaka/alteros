using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using AlterOS.Activities;
using AlterOS.Furniture;
using AlterOS.Scenarios;
using AlterOS.Tests.Furniture;
using NUnit.Framework;

namespace AlterOS.Tests.Activities
{
    public sealed class ActivitySceneHostCapabilityTests
    {
        [Test]
        public void InvalidFurnitureCatalogueCannotAdvertiseSequenceReserveUses()
        {
            FurnitureCatalogueDocument document = ReadDocument(FurnitureFunctionTestCatalogue.CreateJson());
            document.relationshipRules.Add(new FurnitureRelationshipRule
            {
                relationshipRuleId = "fixture.invalid-rule",
                kind = "unsupported_rule_kind",
                pointTypeIds = new List<string> { "missing.point.type" },
                broadPhaseRadiusMeters = 1d,
                minimumDistanceMeters = 0d,
                maximumDistanceMeters = 1d,
                maximumFacingErrorDegrees = 180d,
                maximumHeightDifferenceMeters = 1d,
                lateralAllowanceMeters = 1d
            });
            FurnitureCatalogueSnapshot invalid = FurnitureCatalogueRegistry.Parse(WriteDocument(document));

            Assert.IsTrue(invalid.definitions.Count > 0, "The fixture must retain usable-looking definitions alongside its error.");
            Assert.IsTrue(invalid.diagnostics.Any(item => item.severity == "error"));
            Assert.IsNull(ActivitySceneHost.BuildSequenceFurnitureCapability(invalid));
        }

        [Test]
        public void ValidFurnitureCatalogueAdvertisesItsRegisteredSequenceUses()
        {
            FurnitureCatalogueSnapshot valid = FurnitureCatalogueRegistry.Parse(FurnitureFunctionTestCatalogue.CreateJson());

            ScenarioCapabilitySnapshot capability = ActivitySceneHost.BuildSequenceFurnitureCapability(valid);

            Assert.IsNotNull(capability);
            Assert.AreEqual(ActivitySequenceCapabilityIds.ReserveNearest, capability.capabilityId);
            Assert.IsTrue(capability.argumentBindings.Single().values.Contains("activity.turn_on_tv"));
        }

        private static FurnitureCatalogueDocument ReadDocument(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
            using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                return (FurnitureCatalogueDocument)serializer.ReadObject(input);
        }

        private static string WriteDocument(FurnitureCatalogueDocument document)
        {
            var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
            using (var output = new MemoryStream())
            {
                serializer.WriteObject(output, document);
                return System.Text.Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
