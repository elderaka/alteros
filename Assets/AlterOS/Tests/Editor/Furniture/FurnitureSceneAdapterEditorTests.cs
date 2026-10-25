using System.IO;
using AlterOS.Furniture;
using NUnit.Framework;
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Activities;
using AlterOS.Content;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureSceneAdapterEditorTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

#if UNITY_EDITOR
        private static FurnitureCatalogueDocument ReadCanonicalDocument()
        {
            using (var stream = File.OpenRead(CataloguePath))
                return (FurnitureCatalogueDocument)new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).ReadObject(stream);
        }
#endif

        [Test]
        public void SceneSeedUsesCatalogueAliasAndRoomLocalTransform()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var item = new FurnitureHomeSeedItem
            {
                legacyInstanceId = "testbox.water_dispenser",
                legacyUsePointId = "testbox.water_dispenser.drink",
                positionMeters = new FurnitureVector3 { x = -3.1, y = 0, z = 2.4 },
                rotation = new FurnitureQuaternion { x = 0, y = 0.3007058, z = 0, w = 0.9537169 },
                scale = new FurnitureVector3 { x = 1, y = 1, z = 1 }
            };

            HomeFurnitureLayoutDocument seed = FurnitureHomeSeedBuilder.Create(
                "home.synthetic", "room.synthetic", "seed.synthetic", catalogue, new[] { item });

            Assert.IsNotNull(seed);
            Assert.AreEqual(1, seed.schemaVersion);
            Assert.AreEqual("home.synthetic", seed.homeId);
            Assert.AreEqual("room.synthetic", seed.roomId);
            Assert.AreEqual(1, seed.instances.Count);
            Assert.AreEqual("testbox.water_dispenser", seed.instances[0].instanceId);
            Assert.AreEqual("furniture.water_dispenser", seed.instances[0].definitionId);
            Assert.AreEqual(-3.1, seed.instances[0].positionMeters.x, 0.0001);
            Assert.AreEqual(2.4, seed.instances[0].positionMeters.z, 0.0001);
            Assert.AreEqual("room.floor", seed.instances[0].support.supportId);
            Assert.AreEqual("roomFloor", seed.instances[0].support.supportKind);
        }

        [Test]
        public void UnknownSceneFurniturePreventsPartialSeed()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var known = new FurnitureHomeSeedItem
            {
                legacyInstanceId = "testbox.water_dispenser",
                legacyUsePointId = "testbox.water_dispenser.drink",
                positionMeters = new FurnitureVector3(),
                rotation = new FurnitureQuaternion { w = 1 },
                scale = new FurnitureVector3 { x = 1, y = 1, z = 1 }
            };
            var unknown = new FurnitureHomeSeedItem
            {
                legacyInstanceId = "fixture.unknown",
                legacyUsePointId = "fixture.unknown.use",
                positionMeters = new FurnitureVector3(),
                rotation = new FurnitureQuaternion { w = 1 },
                scale = new FurnitureVector3 { x = 1, y = 1, z = 1 }
            };

            Assert.IsNull(FurnitureHomeSeedBuilder.Create("home.synthetic", "room.synthetic", "seed.synthetic",
                catalogue, new[] { known, unknown }));
        }

#if UNITY_EDITOR
        [Test]
        public void UnconfiguredAdapterDefaultsToLegacyJsonCompatibility()
        {
            var room = new GameObject("furniture-legacy-catalogue-default-test");
            try
            {
                Type adapterType = BridgeType("Pleiades.Furniture.FurnitureSceneAdapter");
                Component adapter = room.AddComponent(adapterType);
                FurnitureCatalogueSnapshot expected = FurnitureCatalogueRegistry.Load(CataloguePath);
                FurnitureCatalogueSnapshot actual = ReadCatalogue(adapter);

                Assert.IsFalse(actual.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
                Assert.AreEqual(expected.catalogueHash, actual.catalogueHash);
                Assert.AreEqual(8, actual.definitions.Count);
            }
            finally { Object.DestroyImmediate(room); }
        }

        [Test]
        public void SceneTargetResolverChecksExactClaimedPointAliasAndRevision()
        {
            var room = new GameObject("furniture-target-test-room");
            var catalogue = new TextAsset(FurnitureFunctionTestCatalogue.CreateJson());
            try
            {
                Type adapterType = BridgeType("Pleiades.Furniture.FurnitureSceneAdapter");
                Component adapter = room.AddComponent(adapterType);
                var host = room.AddComponent<ActivitySceneHost>();
                var pointRoot = new GameObject("tv-a-use-point");
                pointRoot.transform.SetParent(room.transform, false);
                FurnitureUsePoint point = pointRoot.AddComponent<FurnitureUsePoint>();
                Transform approach = NewChild(pointRoot.transform, "approach");
                Transform pose = NewChild(pointRoot.transform, "pose");
                Transform use = NewChild(pointRoot.transform, "use");
                Transform exit = NewChild(pointRoot.transform, "exit");
                Set(point, "instanceId", "tv.a");
                Set(point, "usePointId", "use.tv.a.power");
                Set(point, "supportedUseIds", new[] { "activity.turn_on_tv" });
                Set(point, "approach", approach);
                Set(point, "pose", pose);
                Set(point, "use", use);
                Set(point, "exit", exit);
                int pointRevision = point.Revision;

                var registry = new FurnitureRegistry(new[] { point }, _ => true, _ => 0f);
                FurnitureBinding binding;
                Assert.IsTrue(registry.TryReserve("activity.turn_on_tv", "tv.a", "activity.a", out binding));
                Set(host, "furniture", registry);
                MethodInfo configure = adapterType.GetMethod("ConfigureForTests", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(ActivitySceneHost), typeof(TextAsset) }, null);
                MethodInfo validate = adapterType.GetMethod("ValidateCurrent", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(FurnitureFunctionReservationRequest), typeof(string) }, null);
                Assert.IsNotNull(configure);
                Assert.IsNotNull(validate);
                configure.Invoke(adapter, new object[] { host, catalogue });

                var valid = new FurnitureFunctionReservationRequest("activity.a", "tv.a", "furniture.tv.basic",
                    "use.tv.a.power", "activity.turn_on_tv", pointRevision);
                FurnitureFunctionTargetValidation accepted = (FurnitureFunctionTargetValidation)validate.Invoke(adapter,
                    new object[] { valid, "view.target" });
                FurnitureFunctionTargetValidation stale = (FurnitureFunctionTargetValidation)validate.Invoke(adapter,
                    new object[]
                    {
                        new FurnitureFunctionReservationRequest("activity.a", "tv.a", "furniture.tv.basic",
                            "use.tv.a.power", "activity.turn_on_tv", pointRevision - 1),
                        "view.target"
                    });

                Assert.AreEqual(FurnitureFunctionTargetStatuses.Available, accepted.status);
                Assert.AreEqual(FurnitureFunctionTargetStatuses.Conflict, stale.status);
                Assert.AreEqual("stale_furniture_binding", stale.diagnosticCode);

                registry.Release("activity.a");
                FurnitureFunctionTargetValidation unclaimed = (FurnitureFunctionTargetValidation)validate.Invoke(adapter,
                    new object[] { valid, "view.target" });
                Assert.AreEqual(FurnitureFunctionTargetStatuses.Unavailable, unclaimed.status);
                Assert.AreEqual("furniture_reservation_unavailable", unclaimed.diagnosticCode);
            }
            finally
            {
                Object.DestroyImmediate(room);
                Object.DestroyImmediate(catalogue);
            }
        }

        [Test]
        public void CompiledSqlModeDoesNotFallBackToValidLegacyAssetWhenSettingsAreMissing()
        {
            using (var release = new SyntheticFurnitureSqlRelease(false, null, Guid.NewGuid().ToString("D")))
            {
                release.Open();
                var room = new GameObject("furniture-compiled-missing-settings-test");
                var legacyAsset = new TextAsset(File.ReadAllText(CataloguePath));
                try
                {
                    Component adapter = CreateSceneAdapter(room, legacyAsset);
                    FurnitureCatalogueSnapshot legacy = ReadCatalogue(adapter);
                    Assert.AreEqual(8, legacy.definitions.Count);
                    SetCatalogueSourceMode(adapter, "CompiledSql");

                    FurnitureCatalogueSnapshot compiled = ReadCatalogue(adapter);

                    Assert.IsNull(compiled);
                    Assert.That(ReadCatalogueSourceDiagnostics(adapter)
                        .Any(diagnostic => diagnostic.code == "furniture_catalogue_settings_unavailable"), Is.True);
                }
                finally
                {
                    Object.DestroyImmediate(room);
                    Object.DestroyImmediate(legacyAsset);
                }
            }
        }

        [Test]
        public void CompiledSqlModePreservesRulesCapabilitiesAliasesAndEightStarters()
        {
            using (var release = new SyntheticFurnitureSqlRelease(true, null, Guid.NewGuid().ToString("D")))
            {
                release.Open();
                var room = new GameObject("furniture-compiled-catalogue-test");
                try
                {
                    Component adapter = CreateSceneAdapter(room, null);
                    SetCatalogueSourceMode(adapter, "CompiledSql");
                    FurnitureCatalogueSnapshot legacy = FurnitureCatalogueRegistry.Load(CataloguePath);

                    FurnitureCatalogueSnapshot compiled = ReadCatalogue(adapter);

                    Assert.IsFalse(compiled.diagnostics.Any(diagnostic => diagnostic.severity == "error"));
                    Assert.AreEqual(8, compiled.definitions.Count);
                    Assert.That(compiled.definitions.Select(definition => definition.definitionId),
                        Is.EquivalentTo(legacy.definitions.Select(definition => definition.definitionId)));
                    Assert.That(compiled.relationshipRules.Select(rule => rule.relationshipRuleId),
                        Is.EquivalentTo(legacy.relationshipRules.Select(rule => rule.relationshipRuleId)));
                    Assert.That(compiled.capabilities.Select(capability => capability.capabilityId),
                        Is.EquivalentTo(legacy.capabilities.Select(capability => capability.capabilityId)));
                    Assert.That(compiled.legacyAliases.Select(alias => alias.legacyId),
                        Is.EquivalentTo(legacy.legacyAliases.Select(alias => alias.legacyId)));
                    Assert.AreNotEqual(legacy.catalogueHash, compiled.catalogueHash);
                    Assert.AreSame(compiled, ReadCatalogue(adapter));
                    Assert.IsEmpty(ReadCatalogueSourceDiagnostics(adapter));
                }
                finally { Object.DestroyImmediate(room); }
            }
        }

        [Test]
        public void CompiledSqlModeInvalidatesCachedSnapshotWhenSettingsFingerprintChanges()
        {
            string storeId = Guid.NewGuid().ToString("D");
            using (var originalRelease = new SyntheticFurnitureSqlRelease(true, null, storeId))
            using (var changedSettingsRelease = new SyntheticFurnitureSqlRelease(true, "settings", storeId))
            {
                var room = new GameObject("furniture-compiled-settings-revision-test");
                try
                {
                    originalRelease.Open();
                    Component adapter = CreateSceneAdapter(room, null);
                    SetCatalogueSourceMode(adapter, "CompiledSql");
                    FurnitureCatalogueSnapshot original = ReadCatalogue(adapter);

                    changedSettingsRelease.Open();
                    FurnitureCatalogueSnapshot changed = ReadCatalogue(adapter);

                    Assert.AreNotSame(original, changed);
                    Assert.AreEqual("test-catalogue-v2", changed.catalogueVersion);
                    Assert.AreNotEqual(original.catalogueHash, changed.catalogueHash);
                }
                finally { Object.DestroyImmediate(room); }
            }
        }

        [Test]
        public void CompiledSqlModeInvalidatesCacheWhenFurnitureRevisionChangesWithoutBodyChange()
        {
            string storeId = Guid.NewGuid().ToString("D");
            using (var originalRelease = new SyntheticFurnitureSqlRelease(true, null, storeId))
            using (var changedDefinitionRelease = new SyntheticFurnitureSqlRelease(true, "furniture.seat.basic", storeId))
            {
                var room = new GameObject("furniture-compiled-reference-revision-test");
                try
                {
                    RuntimeContentStore originalStore = originalRelease.Open();
                    ContentResult<CompiledFurnitureCatalogue> originalContent = originalStore.ReadFurnitureCatalogue();
                    Assert.IsTrue(originalContent.IsReady, ContentDetails(originalContent));
                    Component adapter = CreateSceneAdapter(room, null);
                    SetCatalogueSourceMode(adapter, "CompiledSql");
                    FurnitureCatalogueSnapshot original = ReadCatalogue(adapter);

                    RuntimeContentStore changedStore = changedDefinitionRelease.Open();
                    ContentResult<CompiledFurnitureCatalogue> changedContent = changedStore.ReadFurnitureCatalogue();
                    Assert.IsTrue(changedContent.IsReady, ContentDetails(changedContent));
                    Assert.AreNotEqual(originalContent.value.fingerprint, changedContent.value.fingerprint);
                    Assert.AreEqual(FurnitureCatalogueRegistry.CreateSnapshot(originalContent.value.document).catalogueHash,
                        FurnitureCatalogueRegistry.CreateSnapshot(changedContent.value.document).catalogueHash,
                        "The test must change the pinned Furniture revision without changing its decoded body.");

                    FurnitureCatalogueSnapshot changed = ReadCatalogue(adapter);

                    Assert.AreNotSame(original, changed);
                    Assert.AreEqual(original.catalogueVersion, changed.catalogueVersion);
                    Assert.AreNotEqual(original.catalogueHash, changed.catalogueHash);
                }
                finally { Object.DestroyImmediate(room); }
            }
        }

        [Test]
        public void PlacedIdentityResolvesToPersistentMonoScriptAsset()
        {
            const string expectedPath = "Assets/Scripts/Furniture/FurniturePlacedInstanceIdentity.cs";
            var instanceObject = new GameObject("placed-identity-script-asset-test");
            try
            {
                Type identityType = BridgeType("Pleiades.Furniture.FurniturePlacedInstanceIdentity");
                var identity = instanceObject.AddComponent(identityType) as MonoBehaviour;
                Assert.IsNotNull(identity);

                MonoScript script = MonoScript.FromMonoBehaviour(identity);
                Assert.IsNotNull(script);
                Assert.AreEqual(identityType, script.GetClass());
                Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(script));
                Assert.IsTrue(EditorUtility.IsPersistent(script));

                string guid;
                long localId;
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out localId));
                Assert.IsNotEmpty(guid);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<MonoScript>(expectedPath), script);
            }
            finally { Object.DestroyImmediate(instanceObject); }
        }

        [Test]
        public void PowerStateViewUsesPerRendererPropertyBlocksWithSharedMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (shader == null) Assert.Ignore("No simple shared test shader is available in this Editor session.");

            var material = new Material(shader);
            var firstObject = new GameObject("power-view-first");
            var secondObject = new GameObject("power-view-second");
            try
            {
                Type viewType = BridgeType("Pleiades.Furniture.FurniturePowerStateView");
                MeshRenderer firstRenderer = firstObject.AddComponent<MeshRenderer>();
                MeshRenderer secondRenderer = secondObject.AddComponent<MeshRenderer>();
                firstRenderer.sharedMaterial = material;
                secondRenderer.sharedMaterial = material;
                Component firstView = firstObject.AddComponent(viewType);
                Component secondView = secondObject.AddComponent(viewType);
                viewType.GetField("stateRenderers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(firstView, new Renderer[] { firstRenderer });
                viewType.GetField("stateRenderers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(secondView, new Renderer[] { secondRenderer });
                MethodInfo apply = viewType.GetMethod("Apply", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(FurniturePowerVisualState) }, null);
                Assert.IsNotNull(apply);

                apply.Invoke(firstView, new object[] { FurniturePowerVisualState.On });
                apply.Invoke(secondView, new object[] { FurniturePowerVisualState.Off });

                var firstBlock = new MaterialPropertyBlock();
                var secondBlock = new MaterialPropertyBlock();
                firstRenderer.GetPropertyBlock(firstBlock);
                secondRenderer.GetPropertyBlock(secondBlock);
                int property = Shader.PropertyToID("_FurniturePowerState");
                Assert.AreEqual((float)FurniturePowerVisualState.On, firstBlock.GetFloat(property));
                Assert.AreEqual((float)FurniturePowerVisualState.Off, secondBlock.GetFloat(property));
                Assert.AreSame(material, firstRenderer.sharedMaterial);
                Assert.AreSame(material, secondRenderer.sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(material);
            }
        }

        private static Transform NewChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Set(object target, string fieldName, object value)
        { target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }

        private static Type BridgeType(string fullName)
        {
            Type type = Type.GetType(fullName + ", Assembly-CSharp");
            Assert.IsNotNull(type, fullName + " must remain in the Assembly-CSharp bridge assembly.");
            return type;
        }

        private static Component CreateSceneAdapter(GameObject room, TextAsset asset)
        {
            Type adapterType = BridgeType("Pleiades.Furniture.FurnitureSceneAdapter");
            Component adapter = room.AddComponent(adapterType);
            MethodInfo configure = adapterType.GetMethod("ConfigureForTests", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(ActivitySceneHost), typeof(TextAsset) }, null);
            Assert.IsNotNull(configure);
            configure.Invoke(adapter, new object[] { null, asset });
            return adapter;
        }

        private static FurnitureCatalogueSnapshot ReadCatalogue(Component adapter)
        {
            PropertyInfo catalogue = adapter.GetType().GetProperty("Catalogue", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(catalogue);
            return (FurnitureCatalogueSnapshot)catalogue.GetValue(adapter, null);
        }

        private static ContentDiagnostic[] ReadCatalogueSourceDiagnostics(Component adapter)
        {
            PropertyInfo diagnostics = adapter.GetType().GetProperty("CatalogueSourceDiagnostics", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(diagnostics);
            return ((IReadOnlyList<ContentDiagnostic>)diagnostics.GetValue(adapter, null)).ToArray();
        }

        private static void SetCatalogueSourceMode(Component adapter, string value)
        {
            FieldInfo field = adapter.GetType().GetField("catalogueSourceMode", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(adapter, Enum.Parse(field.FieldType, value));
        }

        private static ContentPackage BuildFurnitureCataloguePackage(bool includeSettings, string changed, string storeId)
        {
            FurnitureCatalogueDocument legacy = ReadCanonicalDocument();
            var sources = new List<ContentSourceDocument>();
            if (includeSettings)
            {
                var settings = new FurnitureCatalogueSettingsSource
                {
                    schemaVersion = legacy.schemaVersion,
                    catalogueVersion = changed == "settings" ? "test-catalogue-v2" : legacy.catalogueVersion,
                    lengthUnit = legacy.lengthUnit,
                    angleUnit = legacy.angleUnit,
                    coordinateConvention = legacy.coordinateConvention,
                    relationshipRules = legacy.relationshipRules,
                    capabilities = legacy.capabilities,
                    legacyAliases = legacy.legacyAliases
                };
                byte[] settingsBytes = SerializeDataContract(settings);
                sources.Add(new ContentSourceDocument(ContentSourceKinds.FurnitureCatalogueSettings,
                    "Furniture/catalogue-settings.json", "furniture.catalogue", Sha256(settingsBytes), settingsBytes));
            }

            foreach (FurnitureDefinition definition in legacy.definitions)
            {
                string id = definition.definitionId;
                string displayName = changed == id ? id + " updated" : id;
                var source = new FurnitureDefinitionSource
                {
                    schemaVersion = 1,
                    id = id,
                    displayName = new FurnitureDisplayNameSource
                    {
                        id = definition.displayNameKey,
                        translations = new List<FurnitureTextTranslationSource>
                        { new FurnitureTextTranslationSource { language = "en", text = displayName } }
                    },
                    definition = CloneDataContract(definition),
                    functions = new List<FurnitureFunctionContractSource>()
                };
                byte[] sourceBytes = SerializeDataContract(source);
                sources.Add(new ContentSourceDocument(ContentSourceKinds.FurnitureDefinition,
                    "Furniture/" + id + ".json", id, Sha256(sourceBytes), sourceBytes));
            }

            ContentResult<ContentPackage> imported = LegacyContentAdapter.ImportDocuments(
                "furniture-scene-adapter-fixture", storeId, sources);
            Assert.IsTrue(imported.IsReady, ContentDetails(imported));
            return imported.value;
        }

        private static byte[] SerializeDataContract<T>(T document)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, document);
                return stream.ToArray();
            }
        }

        private static T CloneDataContract<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder();
                foreach (byte value in sha.ComputeHash(bytes))
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string ContentDetails<T>(ContentResult<T> result)
        {
            return string.Join("; ", result.diagnostics.Select(item => item.code + ": " + item.message).ToArray());
        }

        private static string ContentDetails(CompiledReleaseResult result)
        {
            return string.Join("; ", result.diagnostics.Select(item => item.code + ": " + item.message).ToArray());
        }

        private sealed class SyntheticFurnitureSqlRelease : IDisposable
        {
            private readonly string root;

            public SyntheticFurnitureSqlRelease(bool includeSettings, string changed, string storeId)
            {
                root = Path.Combine(Path.GetTempPath(), "furniture-scene-sql-" + Guid.NewGuid().ToString("N"));
                string contentRoot = Path.Combine(root, "Content");
                Directory.CreateDirectory(contentRoot);

                ContentPackage package = BuildFurnitureCataloguePackage(includeSettings, changed, storeId);
                CompiledReleaseResult compiled = ContentReleaseCompiler.Compile(package);
                Assert.IsTrue(compiled.IsReady, ContentDetails(compiled));
                package.buildManifest = ContentBuildManifest.Create(package, compiled.release);

                ContentResult<ContentRepository> opened = ContentRepository.Open(
                    Path.Combine(contentRoot, "content.sqlite3"), ContentStoreMode.ReadWrite, package.storeId);
                Assert.IsTrue(opened.IsReady, ContentDetails(opened));
                ContentResult<string> staged = opened.value.Stage(package);
                Assert.IsTrue(staged.IsReady, ContentDetails(staged));
            }

            public RuntimeContentStore Open()
            {
                ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(root);
                Assert.IsTrue(opened.IsReady, ContentDetails(opened));
                return opened.value;
            }

            public void Dispose()
            {
                RuntimeContentStore.ResetShippedCache();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [DataContract]
        private sealed class FurnitureCatalogueSettingsSource
        {
            [DataMember(Name = "schemaVersion", Order = 0)] public int schemaVersion;
            [DataMember(Name = "catalogueVersion", Order = 1)] public string catalogueVersion;
            [DataMember(Name = "lengthUnit", Order = 2)] public string lengthUnit;
            [DataMember(Name = "angleUnit", Order = 3)] public string angleUnit;
            [DataMember(Name = "coordinateConvention", Order = 4)] public string coordinateConvention;
            [DataMember(Name = "relationshipRules", Order = 5)] public List<FurnitureRelationshipRule> relationshipRules;
            [DataMember(Name = "capabilities", Order = 6)] public List<FurnitureCapabilityDescriptor> capabilities;
            [DataMember(Name = "legacyAliases", Order = 7)] public List<FurnitureLegacyAlias> legacyAliases;
        }

        [DataContract]
        private sealed class FurnitureDefinitionSource
        {
            [DataMember(Name = "schemaVersion", Order = 0)] public int schemaVersion;
            [DataMember(Name = "id", Order = 1)] public string id;
            [DataMember(Name = "displayName", Order = 2)] public FurnitureDisplayNameSource displayName;
            [DataMember(Name = "definition", Order = 3)] public FurnitureDefinition definition;
            [DataMember(Name = "functions", Order = 4)] public List<FurnitureFunctionContractSource> functions;
        }

        [DataContract]
        private sealed class FurnitureDisplayNameSource
        {
            [DataMember(Name = "id", Order = 0)] public string id;
            [DataMember(Name = "translations", Order = 1)] public List<FurnitureTextTranslationSource> translations;
        }

        [DataContract]
        private sealed class FurnitureTextTranslationSource
        {
            [DataMember(Name = "language", Order = 0)] public string language;
            [DataMember(Name = "text", Order = 1)] public string text;
        }

        [DataContract]
        private sealed class FurnitureFunctionContractSource { }
#endif
    }
}
