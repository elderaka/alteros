using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureCatalogueTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [Test]
        public void CanonicalCatalogueContainsExactlyEightStableStarterDefinitions()
        {
            var snapshot = FurnitureCatalogueRegistry.Load(CataloguePath);
            var expected = new[]
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

            Assert.That(snapshot.diagnostics.Any(d => d.severity == "error"), Is.False);
            Assert.That(snapshot.definitions.Select(d => d.definitionId), Is.EquivalentTo(expected));
            Assert.AreEqual(8, snapshot.definitions.Count);
        }

        [Test]
        public void ValidatorRejectsDuplicateDefinitionIds()
        {
            var document = ReadCanonicalDocument();
            document.definitions.Add(Clone(document.definitions[0]));

            var diagnostics = FurnitureCatalogueValidator.Validate(document);

            Assert.That(diagnostics.Any(d => d.code == "duplicate_definition_id" && d.severity == "error"), Is.True);
        }

        [Test]
        public void ValidatorRejectsUnsupportedMajorSchemaVersion()
        {
            var document = ReadCanonicalDocument();
            document.schemaVersion = 2;

            var diagnostics = FurnitureCatalogueValidator.Validate(document);

            Assert.That(diagnostics.Any(d => d.code == "unsupported_schema_version" && d.severity == "error"), Is.True);
        }

        [Test]
        public void SingleDefinitionValidationDoesNotRequireTheStarterCatalogue()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition tv = document.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");
            tv.points.Single(point => point.pointId == "view.target").useIds.Add("activity.turn_on_tv");
            tv.functionBindings = new System.Collections.Generic.List<FurnitureFunctionBindingDescriptor>
            {
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.target",
                    useId = "activity.turn_on_tv"
                }
            };

            var diagnostics = FurnitureCatalogueValidator.ValidateDefinition(tv);

            Assert.IsFalse(diagnostics.Any(diagnostic => diagnostic.severity == "error"));
        }

        [Test]
        public void SingleDefinitionValidationRejectsGeometryCategorySupportAndBindingErrors()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition tv = document.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");
            tv.category = "unknown";
            tv.supportKinds = new System.Collections.Generic.List<string> { "unsupported" };
            tv.points[0].localPosition.x = double.NaN;
            tv.functionBindings = new System.Collections.Generic.List<FurnitureFunctionBindingDescriptor>
            {
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = "power.arbitrary",
                    pointId = "missing.point",
                    useId = "use.invalid"
                }
            };
            tv.presentationBindings = new System.Collections.Generic.List<FurniturePresentationBindingDescriptor>
            {
                new FurniturePresentationBindingDescriptor
                {
                    descriptorId = "presentation.arbitrary",
                    pointId = "missing.point",
                    useId = "use.invalid"
                }
            };

            var diagnostics = FurnitureCatalogueValidator.ValidateDefinition(tv);

            Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.code == "non_finite_geometry"));
            Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.code == "unsupported_category"));
            Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.code == "unsupported_support_kind"));
            Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.code == "unknown_function_id"));
            Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.code == "unknown_function_point"));
            Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.code == "unknown_presentation_descriptor"));
        }

        [Test]
        public void LegacySchemaOneCatalogueWithoutOptInsRemainsUsableAndUnprivileged()
        {
            var snapshot = FurnitureCatalogueRegistry.Load(CataloguePath);
            var tv = snapshot.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");

            Assert.AreEqual(1, snapshot.schemaVersion);
            Assert.IsFalse(snapshot.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
            Assert.IsNotNull(tv.functionBindings);
            Assert.IsEmpty(tv.functionBindings);
            Assert.IsNotNull(tv.presentationBindings);
            Assert.IsEmpty(tv.presentationBindings);
        }

        [Test]
        public void SchemaOnePowerOptInIsParsedAndValidatedAgainstItsPointAndUse()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition tv = document.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");
            tv.points.Single(point => point.pointId == "view.target").useIds.Add("activity.turn_on_tv");
            tv.functionBindings = new System.Collections.Generic.List<FurnitureFunctionBindingDescriptor>
            {
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.target",
                    useId = "activity.turn_on_tv"
                }
            };

            var snapshot = FurnitureCatalogueRegistry.Parse(SerializeDocument(document));
            FurnitureDefinition parsedTv = snapshot.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");

            Assert.AreEqual(1, snapshot.schemaVersion);
            Assert.IsFalse(snapshot.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
            Assert.AreEqual(FurnitureFunctionIds.PowerSet, parsedTv.functionBindings[0].functionId);
            Assert.AreEqual("view.target", parsedTv.functionBindings[0].pointId);
            Assert.AreEqual("activity.turn_on_tv", parsedTv.functionBindings[0].useId);
        }

        [Test]
        public void UnknownFunctionIdFailsClosedAndDropsOnlyItsDefinition()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition tv = document.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");
            tv.points.Single(point => point.pointId == "view.target").useIds.Add("activity.turn_on_tv");
            tv.functionBindings = new System.Collections.Generic.List<FurnitureFunctionBindingDescriptor>
            {
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = "power.arbitrary",
                    pointId = "view.target",
                    useId = "activity.turn_on_tv"
                }
            };

            var snapshot = FurnitureCatalogueRegistry.Parse(SerializeDocument(document));

            Assert.IsTrue(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "unknown_function_id" && diagnostic.severity == "error"));
            Assert.IsFalse(snapshot.definitions.Any(definition => definition.definitionId == "furniture.tv.basic"));
            Assert.AreEqual(7, snapshot.definitions.Count);
        }

        [Test]
        public void FunctionBindingRejectsMissingPointUseAndDuplicateDescriptors()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition tv = document.definitions.Single(definition => definition.definitionId == "furniture.tv.basic");
            tv.functionBindings = new System.Collections.Generic.List<FurnitureFunctionBindingDescriptor>
            {
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "missing.point",
                    useId = "activity.turn_on_tv"
                },
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.target",
                    useId = "activity.turn_on_tv"
                },
                new FurnitureFunctionBindingDescriptor
                {
                    functionId = FurnitureFunctionIds.PowerSet,
                    pointId = "view.target",
                    useId = "activity.turn_on_tv"
                }
            };

            var snapshot = FurnitureCatalogueRegistry.Parse(SerializeDocument(document));

            Assert.IsTrue(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "unknown_function_point" && diagnostic.severity == "error"));
            Assert.IsTrue(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "unknown_function_use_id" && diagnostic.severity == "error"));
            Assert.IsTrue(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "duplicate_function_binding" && diagnostic.severity == "error"));
            Assert.IsFalse(snapshot.definitions.Any(definition => definition.definitionId == "furniture.tv.basic"));
        }

        [Test]
        public void MockPresentationOptInIsSeparateAndUsesItsDeclaredPoint()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition dispenser = document.definitions.Single(definition => definition.definitionId == "furniture.water_dispenser");
            dispenser.presentationBindings = new System.Collections.Generic.List<FurniturePresentationBindingDescriptor>
            {
                new FurniturePresentationBindingDescriptor
                {
                    descriptorId = FurniturePresentationDescriptorIds.DispenserCollectMock,
                    pointId = "drink",
                    useId = "needs.drink_water"
                }
            };

            var snapshot = FurnitureCatalogueRegistry.Parse(SerializeDocument(document));
            FurnitureDefinition parsed = snapshot.definitions.Single(definition => definition.definitionId == "furniture.water_dispenser");

            Assert.IsFalse(snapshot.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
            Assert.IsEmpty(parsed.functionBindings);
            Assert.AreEqual(FurniturePresentationDescriptorIds.DispenserCollectMock, parsed.presentationBindings[0].descriptorId);
        }

        [Test]
        public void UnknownPresentationDescriptorFailsClosed()
        {
            FurnitureCatalogueDocument document = ReadCanonicalDocument();
            FurnitureDefinition dispenser = document.definitions.Single(definition => definition.definitionId == "furniture.water_dispenser");
            dispenser.presentationBindings = new System.Collections.Generic.List<FurniturePresentationBindingDescriptor>
            {
                new FurniturePresentationBindingDescriptor
                {
                    descriptorId = "presentation.arbitrary",
                    pointId = "drink",
                    useId = "needs.drink_water"
                }
            };

            var snapshot = FurnitureCatalogueRegistry.Parse(SerializeDocument(document));

            Assert.IsTrue(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "unknown_presentation_descriptor" && diagnostic.severity == "error"));
            Assert.IsFalse(snapshot.definitions.Any(definition => definition.definitionId == "furniture.water_dispenser"));
        }

        [Test]
        public void ValidatorRejectsNonFinitePointGeometry()
        {
            var document = ReadCanonicalDocument();
            document.definitions[0].points[0].localPosition.x = double.NaN;

            var diagnostics = FurnitureCatalogueValidator.Validate(document);

            Assert.That(diagnostics.Any(d => d.code == "non_finite_geometry" && d.severity == "error"), Is.True);
        }

        [Test]
        public void ValidatorRejectsBrokenRelationshipPointTypeReferences()
        {
            var document = ReadCanonicalDocument();
            document.relationshipRules[0].pointTypeIds.Add("missing.point_type");

            var diagnostics = FurnitureCatalogueValidator.Validate(document);

            Assert.That(diagnostics.Any(d => d.code == "unknown_point_type" && d.severity == "error"), Is.True);
        }

        [Test]
        public void ReloadDiscoversAddedChairVariantWithoutASecondManifest()
        {
            var document = ReadCanonicalDocument();
            var variant = Clone(document.definitions.Single(d => d.definitionId == "furniture.seat.basic"));
            variant.definitionId = "furniture.seat.comfy";
            variant.displayNameKey = "furniture.seat.comfy";
            document.definitions.Add(variant);
            var path = WriteTemporaryDocument(document);
            try
            {
                var snapshot = FurnitureCatalogueRegistry.Load(path);

                Assert.That(snapshot.diagnostics.Any(d => d.severity == "error"), Is.False);
                Assert.That(snapshot.definitions.Any(d => d.definitionId == "furniture.seat.comfy"), Is.True);
                Assert.AreEqual(9, snapshot.definitions.Count);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void IdenticalCatalogueDataProducesStableHashAcrossLoads()
        {
            var first = FurnitureCatalogueRegistry.Load(CataloguePath);
            var second = FurnitureCatalogueRegistry.Load(CataloguePath);

            Assert.That(first.catalogueHash, Is.Not.Empty);
            Assert.AreEqual(first.catalogueHash, second.catalogueHash);
        }

        [Test]
        public void PublicSnapshotFactoryPreservesCanonicalLegacyHash()
        {
            var parsed = FurnitureCatalogueRegistry.Parse(SerializeDocument(ReadCanonicalDocument()));
            var snapshot = FurnitureCatalogueRegistry.CreateSnapshot(ReadCanonicalDocument());

            Assert.IsFalse(snapshot.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
            Assert.AreEqual(parsed.catalogueHash, snapshot.catalogueHash);
            Assert.AreEqual(parsed.catalogueVersion, snapshot.catalogueVersion);
            Assert.That(snapshot.definitions.Select(definition => definition.definitionId),
                Is.EquivalentTo(parsed.definitions.Select(definition => definition.definitionId)));
        }

        [Test]
        public void PublicSnapshotFactoryRejectsNullDocumentWithoutThrowing()
        {
            var snapshot = FurnitureCatalogueRegistry.CreateSnapshot(null);

            Assert.IsNotNull(snapshot, "A null document should return a diagnostic snapshot instead of throwing.");
            Assert.That(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "invalid_document" &&
                diagnostic.severity == "error"), Is.True);
        }

        [Test]
        public void CompiledSnapshotIdentityIncludesCanonicalHashAndFingerprint()
        {
            var parsed = FurnitureCatalogueRegistry.Parse(SerializeDocument(ReadCanonicalDocument()));
            string fingerprint = new string('a', 64);
            var compiled = FurnitureCatalogueRegistry.CreateCompiledSnapshot(ReadCanonicalDocument(), fingerprint);
            var otherRelease = FurnitureCatalogueRegistry.CreateCompiledSnapshot(ReadCanonicalDocument(), new string('b', 64));
            string expectedHash = ComputeSha256Hex("compiled-furniture-catalogue-v1\n" + parsed.catalogueHash + "\n" + fingerprint);

            Assert.IsFalse(compiled.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
            Assert.AreEqual(expectedHash, compiled.catalogueHash);
            Assert.AreNotEqual(compiled.catalogueHash, otherRelease.catalogueHash);
            Assert.AreEqual(parsed.catalogueHash,
                FurnitureCatalogueRegistry.Parse(SerializeDocument(ReadCanonicalDocument())).catalogueHash);
        }

        [Test]
        public void CompiledSnapshotRetainsCatalogueRulesCapabilitiesAliasesAndStarters()
        {
            var document = ReadCanonicalDocument();
            var snapshot = FurnitureCatalogueRegistry.CreateCompiledSnapshot(document, new string('c', 64));

            Assert.IsFalse(snapshot.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
            Assert.That(snapshot.definitions.Select(definition => definition.definitionId),
                Is.EquivalentTo(document.definitions.Select(definition => definition.definitionId)));
            Assert.That(snapshot.relationshipRules.Select(rule => rule.relationshipRuleId),
                Is.EquivalentTo(document.relationshipRules.Select(rule => rule.relationshipRuleId)));
            Assert.That(snapshot.capabilities.Select(capability => capability.capabilityId),
                Is.EquivalentTo(document.capabilities.Select(capability => capability.capabilityId)));
            Assert.That(snapshot.legacyAliases.Select(alias => alias.legacyId),
                Is.EquivalentTo(document.legacyAliases.Select(alias => alias.legacyId)));
            Assert.AreEqual(8, snapshot.definitions.Count);
        }

        [Test]
        public void CompiledSnapshotPreservesDiagnosticsAndIncludesFingerprintForInvalidCatalogue()
        {
            var document = ReadCanonicalDocument();
            document.relationshipRules = null;
            string fingerprint = new string('d', 64);
            string canonicalHash = FurnitureCatalogueRegistry.Parse(SerializeDocument(document)).catalogueHash;

            var snapshot = FurnitureCatalogueRegistry.CreateCompiledSnapshot(document, fingerprint);

            Assert.That(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "missing_relationship_rules" &&
                diagnostic.severity == "error"), Is.True);
            Assert.AreEqual(ComputeSha256Hex("compiled-furniture-catalogue-v1\n" + canonicalHash + "\n" + fingerprint),
                snapshot.catalogueHash);
        }

        [Test]
        public void CompiledSnapshotRejectsFingerprintsThatAreNotSha256Hex()
        {
            string[] invalidFingerprints = { null, string.Empty, new string('a', 63), new string('g', 64), new string('a', 65) };
            foreach (string fingerprint in invalidFingerprints)
            {
                var snapshot = FurnitureCatalogueRegistry.CreateCompiledSnapshot(ReadCanonicalDocument(), fingerprint);

                Assert.That(snapshot.diagnostics.Any(diagnostic => diagnostic.code == "compiled_fingerprint_invalid" &&
                    diagnostic.severity == "error"), Is.True);
                Assert.IsEmpty(snapshot.catalogueHash);
                Assert.IsEmpty(snapshot.definitions);
            }
        }

        [Test]
        public void RegistryPreservesExplicitLegacyDispenserAliases()
        {
            var snapshot = FurnitureCatalogueRegistry.Load(CataloguePath);

            Assert.That(snapshot.legacyAliases.Any(a => a.legacyId == "testbox.water_dispenser" && a.definitionId == "furniture.water_dispenser"), Is.True);
            Assert.That(snapshot.legacyAliases.Any(a => a.legacyId == "testbox.water_dispenser.drink" && a.definitionId == "furniture.water_dispenser" && a.pointId == "drink"), Is.True);
        }

        [Test]
        public void MalformedCatalogueReturnsInspectableErrorInsteadOfThrowingOrSucceedingEmpty()
        {
            var path = Path.Combine(Path.GetTempPath(), "furniture-catalogue-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{", Encoding.UTF8);
            try
            {
                var snapshot = FurnitureCatalogueRegistry.Load(path);

                Assert.That(snapshot.diagnostics.Any(d => d.code == "parse_error" && d.severity == "error"), Is.True);
                Assert.That(snapshot.definitions, Is.Empty);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ParsingTextAssetContentProducesSameCatalogueHashAsPathLoad()
        {
            var loaded = FurnitureCatalogueRegistry.Load(CataloguePath);
            var parsed = FurnitureCatalogueRegistry.Parse(File.ReadAllText(CataloguePath));

            Assert.AreEqual(loaded.catalogueHash, parsed.catalogueHash);
            Assert.AreEqual(8, parsed.definitions.Count);
            Assert.IsFalse(parsed.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
        }

        private static FurnitureCatalogueDocument ReadCanonicalDocument()
        {
            using (var stream = File.OpenRead(CataloguePath))
                return (FurnitureCatalogueDocument)new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).ReadObject(stream);
        }

        private static string ComputeSha256Hex(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest)
                    builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static T Clone<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }

        private static string SerializeDocument(FurnitureCatalogueDocument document)
        {
            var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, document);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string WriteTemporaryDocument(FurnitureCatalogueDocument document)
        {
            var path = Path.Combine(Path.GetTempPath(), "furniture-catalogue-" + Guid.NewGuid().ToString("N") + ".json");
            using (var stream = File.Create(path))
                new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).WriteObject(stream, document);
            return path;
        }
    }
}
