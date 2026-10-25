using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Serialization.Json;
using AlterOS.Furniture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    [Serializable]
    public sealed class ContentBuildSource
    {
        public string kind = string.Empty;
        public string path = string.Empty;
        public string sourceId = string.Empty;
        public string sourceHash = string.Empty;
        public string payloadHash = string.Empty;
        public string contentId = string.Empty;
        public string contentRevision = string.Empty;
    }

    [Serializable]
    public sealed class ContentBuildCoverage
    {
        public string kind = string.Empty;
        public int count;
    }

    [Serializable]
    public sealed class FurnitureCatalogueReleaseMetadata
    {
        public FurnitureCatalogueDocument settings = new FurnitureCatalogueDocument();
        public List<ContentRef> definitions = new List<ContentRef>();
        public string fingerprint = string.Empty;
    }

    /// <summary>Exact source closure and coverage summary for one baked, immutable SQL release.</summary>
    [Serializable]
    public sealed class ContentBuildManifest
    {
        public int schemaVersion = 1;
        public string packageId = string.Empty;
        public string storeId = string.Empty;
        public string authoringStoreId = string.Empty;
        public string authoringSnapshotHash = string.Empty;
        public string releaseId = string.Empty;
        public string sourceManifestHash = string.Empty;
        public string semanticProfileId = string.Empty;
        public int anchorCount;
        public int embeddingProfileCount;
        public int embeddingCount;
        public List<ContentBuildSource> sources = new List<ContentBuildSource>();
        public List<ContentBuildCoverage> coverage = new List<ContentBuildCoverage>();
        public FurnitureCatalogueReleaseMetadata furnitureCatalogue;

        public string Hash { get { return Sha256(ToJson()); } }

        internal ContentBuildManifest DetachedCopy()
        {
            ContentBuildManifest copy;
            string error;
            if (!TryParse(ToJson(), out copy, out error) || !copy.Validate(out error))
                throw new InvalidOperationException("The build manifest could not be copied as a validated detached value: " + error);
            return copy;
        }

        internal static FurnitureCatalogueReleaseMetadata CloneFurnitureCatalogueMetadata(FurnitureCatalogueReleaseMetadata source)
        {
            if (source == null) return null;
            return new FurnitureCatalogueReleaseMetadata
            {
                settings = CloneSettings(source.settings),
                definitions = (source.definitions ?? new List<ContentRef>()).Select(item => item == null ? null :
                    new ContentRef(item.store, item.id, item.revision, item.expectedHash)).ToList(),
                fingerprint = source.fingerprint ?? string.Empty
            };
        }

        public static ContentBuildManifest Create(ContentPackage package, ContentRelease release)
        { return Create(package, release, string.Empty, string.Empty); }

        public static ContentBuildManifest Create(ContentPackage package, ContentRelease release,
            string authoringStoreId, string authoringSnapshotHash)
        {
            if (package == null || release == null || package.legacySources == null)
                throw new ArgumentException("A complete imported package and compiled release are required.");

            bool hasAuthoringProvenance = !string.IsNullOrEmpty(authoringStoreId) || !string.IsNullOrEmpty(authoringSnapshotHash);

            var manifest = new ContentBuildManifest
            {
                schemaVersion = package.furnitureCatalogueSettings != null ? 3 : (hasAuthoringProvenance ? 2 : 1),
                packageId = package.packageId,
                storeId = package.storeId,
                authoringStoreId = authoringStoreId ?? string.Empty,
                authoringSnapshotHash = authoringSnapshotHash ?? string.Empty,
                releaseId = release.releaseId,
                sourceManifestHash = package.sourceManifestHash,
                anchorCount = package.anchors == null ? 0 : package.anchors.Count,
                embeddingProfileCount = package.embeddingProfiles == null ? 0 : package.embeddingProfiles.Count,
                embeddingCount = package.embeddings == null ? 0 : package.embeddings.Count
            };
            if (manifest.embeddingProfileCount > 1)
                throw new InvalidOperationException("The shipped dialogue release must select one exact semantic embedding profile.");
            if (manifest.embeddingProfileCount == 1)
                manifest.semanticProfileId = package.embeddingProfiles[0].id;

            if (package.furnitureCatalogueSettings != null)
            {
                List<ContentReleaseDependency> furnitureDependencies = (release.dependencies ?? new List<ContentReleaseDependency>())
                    .Where(item => item != null && item.kind == "furniture")
                    .OrderBy(item => item.id, StringComparer.Ordinal).ThenBy(item => item.revision, StringComparer.Ordinal).ToList();
                manifest.furnitureCatalogue = new FurnitureCatalogueReleaseMetadata
                {
                    settings = CloneSettings(package.furnitureCatalogueSettings),
                    definitions = furnitureDependencies.Select(item => new ContentRef(item.store, item.id, item.revision, item.expectedHash)).ToList()
                };
                manifest.furnitureCatalogue.fingerprint = ComputeFurnitureCatalogueFingerprint(manifest.furnitureCatalogue.settings,
                    manifest.furnitureCatalogue.definitions);
                manifest.schemaVersion = 3;
            }

            foreach (LegacySourceRecord source in package.legacySources.OrderBy(item => item.kind, StringComparer.Ordinal)
                .ThenBy(item => NormalizePath(item.relativePath), StringComparer.Ordinal))
            {
                if (source.sourceBytes == null || source.sourceBytes.Length == 0 || Sha256(source.sourceBytes) != source.payloadHash)
                    throw new InvalidOperationException("A full SQL release requires exact source bytes for " + source.relativePath + ".");
                manifest.sources.Add(new ContentBuildSource
                {
                    kind = source.kind,
                    path = NormalizePath(source.relativePath),
                    sourceId = source.sourceId,
                    sourceHash = source.sourceHash,
                    payloadHash = source.payloadHash,
                    contentId = source.contentId,
                    contentRevision = source.contentRevision
                });
            }
            manifest.coverage = manifest.sources.GroupBy(item => item.kind, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ContentBuildCoverage { kind = group.Key, count = group.Count() }).ToList();

            string error;
            if (!manifest.Validate(out error)) throw new InvalidOperationException(error);
            if (!manifest.ValidateForPackage(package, out error)) throw new InvalidOperationException(error);
            return manifest;
        }

        public bool ValidateForPackage(ContentPackage package, out string error)
        {
            error = string.Empty;
            if (!Validate(out error)) return false;
            if (package == null || package.packageId != packageId || package.storeId != storeId ||
                package.sourceManifestHash != sourceManifestHash || package.legacySources == null ||
                package.legacySources.Count != sources.Count || package.releases == null ||
                !package.releases.Any(item => item != null && item.releaseId == releaseId))
            { error = "The build manifest does not pin this package's exact source list and compiled release."; return false; }

            ContentRelease selectedRelease = package.releases.FirstOrDefault(item => item != null && item.releaseId == releaseId);
            if (!MatchesPackageFurnitureCatalogue(package.furnitureCatalogueSettings, selectedRelease, out error)) return false;

            foreach (ContentBuildSource entry in sources)
            {
                LegacySourceRecord source = package.legacySources.FirstOrDefault(item => item != null &&
                    item.kind == entry.kind && NormalizePath(item.relativePath) == entry.path);
                if (source == null || source.sourceId != entry.sourceId || source.sourceHash != entry.sourceHash ||
                    source.payloadHash != entry.payloadHash || source.contentId != entry.contentId || source.contentRevision != entry.contentRevision ||
                    source.sourceBytes == null || Sha256(source.sourceBytes) != entry.payloadHash)
                { error = "A build manifest source does not match its exact imported bytes or stable mapping: " + entry.path; return false; }
            }
            return true;
        }

        public bool Validate(out string error)
        {
            error = string.Empty;
            if ((schemaVersion != 1 && schemaVersion != 2 && schemaVersion != 3) || !Identifier(packageId) || !Guid.TryParse(storeId, out _) || !Identifier(releaseId) ||
                !Sha256String(sourceManifestHash) || sources == null || sources.Count == 0 || sources.Count > 10000 ||
                coverage == null || anchorCount < 0 || embeddingProfileCount < 0 || embeddingCount < 0)
            { error = "The build manifest header or counts are invalid."; return false; }
            bool hasAuthoringStore = !string.IsNullOrEmpty(authoringStoreId);
            bool hasAuthoringSnapshot = !string.IsNullOrEmpty(authoringSnapshotHash);
            if (schemaVersion == 1 && (hasAuthoringStore || hasAuthoringSnapshot) ||
                schemaVersion == 2 && (!Guid.TryParseExact(authoringStoreId, "D", out _) || !Sha256String(authoringSnapshotHash)) ||
                schemaVersion == 3 && (hasAuthoringStore != hasAuthoringSnapshot ||
                    hasAuthoringStore && (!Guid.TryParseExact(authoringStoreId, "D", out _) || !Sha256String(authoringSnapshotHash))))
            { error = "The build manifest authoring provenance is missing, malformed or incompatible with its schema version."; return false; }
            if (embeddingProfileCount == 0 && (!string.IsNullOrEmpty(semanticProfileId) || embeddingCount != 0) ||
                embeddingProfileCount == 1 && !Identifier(semanticProfileId) || embeddingProfileCount > 1)
            { error = "The build manifest semantic profile selection is missing or ambiguous."; return false; }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentBuildSource source in sources)
                if (source == null || !Identifier(source.kind) || !SafeRelativePath(source.path) || !Identifier(source.sourceId) ||
                    !Sha256String(source.sourceHash) || !Sha256String(source.payloadHash) || !Identifier(source.contentId) || !Sha256String(source.contentRevision) ||
                    !paths.Add(NormalizePath(source.path)))
                { error = "The build manifest contains a malformed or duplicate source entry."; return false; }

            List<ContentBuildCoverage> expected = sources.GroupBy(item => item.kind, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ContentBuildCoverage { kind = group.Key, count = group.Count() }).ToList();
            if (coverage.Count != expected.Count || coverage.Where((item, index) => item == null ||
                item.kind != expected[index].kind || item.count != expected[index].count).Any())
            { error = "The build manifest source-kind coverage does not match its source list."; return false; }
            if (ComputeSourceManifestHash(packageId, storeId, sources) != sourceManifestHash)
            { error = "The build manifest source-list hash does not match its exact source entries."; return false; }
            if (schemaVersion == 3)
            {
                if (furnitureCatalogue == null || !ValidateFurnitureCatalogueMetadata(furnitureCatalogue, storeId, out error)) return false;
                ContentBuildSource[] settingsSources = sources.Where(item => item.kind == "furniture_catalogue_settings").ToArray();
                if (settingsSources.Length != 1 || settingsSources[0].sourceId != "furniture.catalogue" ||
                    settingsSources[0].path != "Furniture/catalogue-settings.json")
                { error = "A schema-3 Furniture catalogue must pin exactly one singleton settings source."; return false; }
            }
            else if (furnitureCatalogue != null)
            { error = "Only build manifest schema 3 may contain a compiled Furniture catalogue."; return false; }
            else if (sources.Any(item => item.kind == "furniture_catalogue_settings"))
            { error = "A Furniture settings source requires compiled catalogue metadata in schema 3."; return false; }
            return true;
        }

        public string ToJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"schemaVersion\":").Append(schemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\"packageId\":").Append(Quote(packageId))
                .Append(",\"storeId\":").Append(Quote(storeId));
            if (schemaVersion == 2 || schemaVersion == 3)
                builder.Append(",\"authoringStoreId\":").Append(Quote(authoringStoreId))
                    .Append(",\"authoringSnapshotHash\":").Append(Quote(authoringSnapshotHash));
            builder.Append(",\"releaseId\":").Append(Quote(releaseId))
                .Append(",\"sourceManifestHash\":").Append(Quote(sourceManifestHash))
                .Append(",\"semanticProfileId\":").Append(Quote(semanticProfileId))
                .Append(",\"anchorCount\":").Append(anchorCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"embeddingProfileCount\":").Append(embeddingProfileCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"embeddingCount\":").Append(embeddingCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"sources\":[");
            for (int index = 0; index < sources.Count; index++)
            {
                if (index > 0) builder.Append(',');
                ContentBuildSource item = sources[index];
                builder.Append("{\"kind\":").Append(Quote(item.kind)).Append(",\"path\":").Append(Quote(item.path))
                    .Append(",\"sourceId\":").Append(Quote(item.sourceId)).Append(",\"sourceHash\":").Append(Quote(item.sourceHash))
                    .Append(",\"payloadHash\":").Append(Quote(item.payloadHash))
                    .Append(",\"contentId\":").Append(Quote(item.contentId)).Append(",\"contentRevision\":").Append(Quote(item.contentRevision)).Append('}');
            }
            builder.Append("],\"coverage\":[");
            for (int index = 0; index < coverage.Count; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append("{\"kind\":").Append(Quote(coverage[index].kind)).Append(",\"count\":")
                    .Append(coverage[index].count.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            if (schemaVersion == 3)
                builder.Append("],\"furnitureCatalogue\":").Append(FurnitureCatalogueJson(furnitureCatalogue));
            else builder.Append(']');
            return builder.Append('}').ToString();
        }

        public static bool TryParse(string json, out ContentBuildManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;
            try
            {
                JsonObject root = ContentJson.Parse(json) as JsonObject;
                if (root == null) { error = "The build manifest JSON has an unexpected shape."; return false; }
                int version = (int)root["schemaVersion"];
                int expectedKeys = version == 1 ? 11 : version == 2 ? 13 : version == 3 ? 14 : -1;
                if (expectedKeys < 0 || root.Count != expectedKeys)
                { error = "The build manifest JSON has an unexpected shape."; return false; }
                var value = new ContentBuildManifest
                {
                    schemaVersion = version, packageId = (string)root["packageId"], storeId = (string)root["storeId"],
                    authoringStoreId = version >= 2 ? (string)root["authoringStoreId"] : string.Empty,
                    authoringSnapshotHash = version >= 2 ? (string)root["authoringSnapshotHash"] : string.Empty,
                    releaseId = (string)root["releaseId"], sourceManifestHash = (string)root["sourceManifestHash"],
                    semanticProfileId = (string)root["semanticProfileId"],
                    anchorCount = (int)root["anchorCount"], embeddingProfileCount = (int)root["embeddingProfileCount"],
                    embeddingCount = (int)root["embeddingCount"]
                };
                JsonArray sourceRows = root["sources"] as JsonArray;
                JsonArray coverageRows = root["coverage"] as JsonArray;
                if (sourceRows == null || coverageRows == null) { error = "The build manifest arrays are missing."; return false; }
                foreach (JsonValue item in sourceRows)
                {
                    JsonObject row = item as JsonObject;
                    if (row == null || row.Count != 7) { error = "A build manifest source row has an unexpected shape."; return false; }
                    value.sources.Add(new ContentBuildSource
                    {
                        kind = (string)row["kind"], path = (string)row["path"], sourceId = (string)row["sourceId"],
                        sourceHash = (string)row["sourceHash"], payloadHash = (string)row["payloadHash"],
                        contentId = (string)row["contentId"], contentRevision = (string)row["contentRevision"]
                    });
                }
                foreach (JsonValue item in coverageRows)
                {
                    JsonObject row = item as JsonObject;
                    if (row == null || row.Count != 2) { error = "A build manifest coverage row has an unexpected shape."; return false; }
                    value.coverage.Add(new ContentBuildCoverage { kind = (string)row["kind"], count = (int)row["count"] });
                }
                if (version == 3)
                {
                    JsonObject catalogue = root["furnitureCatalogue"] as JsonObject;
                    if (catalogue == null || catalogue.Count != 3 || !(catalogue["settings"] is JsonObject) || !(catalogue["definitions"] is JsonArray))
                    { error = "The compiled Furniture catalogue metadata has an unexpected shape."; return false; }
                    JsonObject settings = catalogue["settings"] as JsonObject;
                    if (settings.Count != 8) { error = "The Furniture settings envelope has an unexpected shape."; return false; }
                    List<ContentDiagnostic> settingsDiagnostics = LegacyContentAdapter.ValidateSourceShape(
                        ContentSourceKinds.FurnitureCatalogueSettings, settings, "furnitureCatalogue.settings");
                    if (settingsDiagnostics.Count > 0)
                    { error = settingsDiagnostics[0].code + ": " + settingsDiagnostics[0].message; return false; }
                    var parsedSettings = JsonConvert.DeserializeObject<FurnitureCatalogueDocument>(settings.ToString(Formatting.None));
                    parsedSettings.definitions = new List<FurnitureDefinition>();
                    var references = new List<ContentRef>();
                    foreach (JsonValue item in (JsonArray)catalogue["definitions"])
                    {
                        JsonObject reference = item as JsonObject;
                        if (reference == null || reference.Count != 4)
                        { error = "A compiled Furniture reference has an unexpected shape."; return false; }
                        references.Add(new ContentRef((string)reference["store"], (string)reference["id"],
                            (string)reference["revision"], (string)reference["expectedHash"]));
                    }
                    value.furnitureCatalogue = new FurnitureCatalogueReleaseMetadata
                    { settings = parsedSettings, definitions = references, fingerprint = (string)catalogue["fingerprint"] };
                }
                manifest = value;
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static string ComputeSourceManifestHash(string package, string store, IEnumerable<ContentBuildSource> entries)
        {
            var text = new StringBuilder().Append(package).Append('\n').Append(store).Append('\n');
            foreach (ContentBuildSource item in entries.OrderBy(item => item.kind, StringComparer.Ordinal).ThenBy(item => item.path, StringComparer.Ordinal))
                text.Append(item.kind).Append('\n').Append(item.path).Append('\n').Append(item.sourceHash).Append('\n')
                    .Append(item.payloadHash).Append('\n');
            return Sha256(text.ToString());
        }

        private bool MatchesPackageFurnitureCatalogue(FurnitureCatalogueDocument settings, ContentRelease release, out string error)
        {
            error = string.Empty;
            if (settings == null)
            {
                if (furnitureCatalogue != null)
                { error = "The manifest contains Furniture settings that are absent from the compiled package."; return false; }
                return true;
            }
            if (release == null || furnitureCatalogue == null || FurnitureCatalogueRegistry.ComputeHash(settings) !=
                FurnitureCatalogueRegistry.ComputeHash(furnitureCatalogue.settings))
            { error = "The selected release does not carry the exact compiled Furniture settings envelope."; return false; }

            ContentReleaseDependency[] dependencies = (release.dependencies ?? new List<ContentReleaseDependency>())
                .Where(item => item != null && item.kind == "furniture")
                .OrderBy(item => item.id, StringComparer.Ordinal).ThenBy(item => item.revision, StringComparer.Ordinal).ToArray();
            if (dependencies.Length != furnitureCatalogue.definitions.Count || dependencies.Where((item, index) =>
                item.store != furnitureCatalogue.definitions[index].store || item.id != furnitureCatalogue.definitions[index].id ||
                item.revision != furnitureCatalogue.definitions[index].revision || item.expectedHash != furnitureCatalogue.definitions[index].expectedHash).Any())
            { error = "The manifest Furniture references differ from the exact selected release dependency set."; return false; }
            return true;
        }

        private static bool ValidateFurnitureCatalogueMetadata(FurnitureCatalogueReleaseMetadata metadata, string manifestStoreId, out string error)
        {
            error = string.Empty;
            FurnitureCatalogueDocument settings = metadata == null ? null : metadata.settings;
            if (metadata == null || settings == null || settings.schemaVersion != 1 || string.IsNullOrWhiteSpace(settings.catalogueVersion) ||
                settings.lengthUnit != "m" || settings.angleUnit != "deg" ||
                settings.coordinateConvention != "unity_local_y_up_positive_z_forward" ||
                settings.definitions != null && settings.definitions.Count != 0 || settings.relationshipRules == null ||
                settings.capabilities == null || settings.legacyAliases == null || metadata.definitions == null || metadata.definitions.Count == 0)
            { error = "The Furniture settings envelope or exact reference list is incomplete."; return false; }

            if (!ValidateFurnitureSettingsShape(settings, out error)) return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentRef reference in metadata.definitions)
                if (reference == null || reference.store != manifestStoreId || !Identifier(reference.id) || !Sha256String(reference.revision) ||
                    !Sha256String(reference.expectedHash) || !ids.Add(reference.id))
                { error = "The manifest has a malformed, foreign, or duplicate Furniture definition reference."; return false; }
            if (metadata.fingerprint != ComputeFurnitureCatalogueFingerprint(settings, metadata.definitions))
            { error = "The Furniture catalogue fingerprint does not match its canonical settings and exact definition refs."; return false; }
            return true;
        }

        private static bool ValidateFurnitureSettingsShape(FurnitureCatalogueDocument settings, out string error)
        {
            error = string.Empty;
            try
            {
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).WriteObject(stream, CloneSettings(settings));
                    JsonObject settingsObject = JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                    settingsObject.Remove("definitions");
                    List<ContentDiagnostic> diagnostics = LegacyContentAdapter.ValidateSourceShape(
                        ContentSourceKinds.FurnitureCatalogueSettings, settingsObject, "furnitureCatalogue.settings");
                    if (diagnostics.Count > 0)
                    {
                        error = diagnostics[0].code + ": " + diagnostics[0].message;
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        internal static string ComputeFurnitureCatalogueFingerprint(FurnitureCatalogueDocument settings, IList<ContentRef> definitions)
        {
            string settingsHash = FurnitureCatalogueRegistry.ComputeHash(CloneSettings(settings));
            string[] references = (definitions ?? new List<ContentRef>()).Where(item => item != null)
                .OrderBy(item => item.id, StringComparer.Ordinal).ThenBy(item => item.revision, StringComparer.Ordinal)
                .Select(item => "furniture\0" + item.id + "\0" + item.revision + "\0" + item.store + "\0" + item.expectedHash).ToArray();
            return Sha256("compiled-furniture-catalogue-v1\n" + settingsHash + "\n" + string.Join("\n", references));
        }

        private static FurnitureCatalogueDocument CloneSettings(FurnitureCatalogueDocument source)
        {
            if (source == null) return null;
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
                serializer.WriteObject(stream, source);
                stream.Position = 0;
                FurnitureCatalogueDocument clone = (FurnitureCatalogueDocument)serializer.ReadObject(stream);
                clone.definitions = new List<FurnitureDefinition>();
                return clone;
            }
        }

        private static string FurnitureCatalogueJson(FurnitureCatalogueReleaseMetadata metadata)
        {
            FurnitureCatalogueDocument settings = CloneSettings(metadata.settings);
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument)).WriteObject(stream, settings);
                var settingsObject = JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                settingsObject.Remove("definitions");
                var builder = new StringBuilder("{\"settings\":").Append(settingsObject.ToString(Formatting.None)).Append(",\"definitions\":[");
                for (int index = 0; index < metadata.definitions.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    ContentRef reference = metadata.definitions[index];
                    builder.Append("{\"store\":").Append(Quote(reference.store)).Append(",\"id\":").Append(Quote(reference.id))
                        .Append(",\"revision\":").Append(Quote(reference.revision)).Append(",\"expectedHash\":").Append(Quote(reference.expectedHash)).Append('}');
                }
                return builder.Append("],\"fingerprint\":").Append(Quote(metadata.fingerprint)).Append('}').ToString();
            }
        }

        private static string NormalizePath(string path) { return (path ?? string.Empty).Replace('\\', '/'); }
        private static string Quote(string value) { return ContentJson.Quote(value); }
        private static string Sha256(string value) { return Sha256(Encoding.UTF8.GetBytes(value ?? string.Empty)); }
        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return ToHex(hash.ComputeHash(bytes ?? new byte[0]));
        }
        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
        private static bool Sha256String(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char item in value) if (!(item >= '0' && item <= '9' || item >= 'a' && item <= 'f')) return false;
            return true;
        }
        private static bool Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value == "." || value == "..") return false;
            foreach (char item in value) if (!(char.IsLetterOrDigit(item) || item == '.' || item == '_' || item == '-' || item == ':')) return false;
            return true;
        }
        private static bool SafeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.StartsWith("/", StringComparison.Ordinal) || value.Contains(":") ||
                value.Contains("\\")) return false;
            return value.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
        }
    }
}
