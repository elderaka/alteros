using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;

namespace AlterOS.Content
{
    /// <summary>Builds deterministic immutable release fingerprints; it does not publish a head.</summary>
    public static class ContentReleaseCompiler
    {
        private static readonly object ValidatedCompilerAttestation = new object();

        internal static bool HasValidatedCompilerAttestation(ContentRelease release)
        { return release != null && ReferenceEquals(release.compilerAttestation, ValidatedCompilerAttestation); }

        public static CompiledReleaseResult Compile(ContentPackage package)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.packageId) || string.IsNullOrWhiteSpace(package.storeId) ||
                package.legacySources == null || package.legacySources.Count == 0 || !IsSha256(package.sourceManifestHash))
                return Failure("content_package_incomplete", "A host-imported package with source records and a manifest hash is required.");
            ContentDiagnostic catalogueIssue = ContentPackageValidator.ValidateFurnitureCatalogueSource(package);
            if (catalogueIssue != null)
                return CompiledReleaseResult.Failure(ContentStatuses.Invalid, catalogueIssue.code, catalogueIssue.path, catalogueIssue.message);
            if ((package.actions ?? new List<ActionRecord>()).Any(item => item != null &&
                item.implementationKind != "legacy_activity" && !string.IsNullOrEmpty(item.implementationKind)))
                return Failure("content_host_validation_required",
                    "New Action implementations require the trusted host vocabulary and registered ActivitySequenceBuilder before compilation.");

            return CompileCore(package, BuildDependencies(package), "legacy-bake-v1");
        }

        /// <summary>Validates host-owned capabilities and exact package references before compiling a publishable release.</summary>
        public static CompiledReleaseResult Compile(ContentPackage package, ContentHostVocabulary hostVocabulary)
        {
            ContentPackageValidationResult validation = ContentPackageValidator.Validate(package, hostVocabulary);
            if (!validation.isValid)
            {
                ContentDiagnostic first = validation.diagnostics.Count == 0 ? null : validation.diagnostics[0];
                return CompiledReleaseResult.Failure(ContentStatuses.Invalid,
                    first == null ? "content_package_invalid" : first.code,
                    first == null ? "" : first.path,
                    first == null ? "The content package failed host validation." : first.message);
            }
            if (package == null || string.IsNullOrWhiteSpace(package.packageId) || string.IsNullOrWhiteSpace(package.storeId) ||
                package.legacySources == null || package.legacySources.Count == 0 || !IsSha256(package.sourceManifestHash))
                return Failure("content_package_incomplete", "A host-imported package with source records and a manifest hash is required.");
            return CompileCore(package, BuildDependencies(package), ContentPackageValidator.ValidatorVersion);
        }

        /// <summary>Returns the exact immutable package closure after host vocabulary and graph validation.</summary>
        public static ContentResult<List<ContentReleaseDependency>> ResolveDependencies(ContentPackage package,
            ContentHostVocabulary hostVocabulary)
        {
            ContentPackageValidationResult validation = ContentPackageValidator.Validate(package, hostVocabulary);
            if (!validation.isValid)
            {
                ContentDiagnostic first = validation.diagnostics.Count == 0 ? null : validation.diagnostics[0];
                return ContentResult<List<ContentReleaseDependency>>.Failure(ContentStatuses.Invalid,
                    first == null ? "content_package_invalid" : first.code,
                    first == null ? "" : first.path,
                    first == null ? "The content package failed host validation." : first.message);
            }
            List<ContentReleaseDependency> dependencies = BuildDependencies(package);
            if (dependencies.Count == 0)
                return ContentResult<List<ContentReleaseDependency>>.Failure(ContentStatuses.Invalid,
                    "content_release_empty", "", "A release cannot be compiled without immutable content dependencies.");
            return ContentResult<List<ContentReleaseDependency>>.Ready(dependencies);
        }

        private static CompiledReleaseResult CompileCore(ContentPackage package,
            List<ContentReleaseDependency> dependencies, string validatorVersion)
        {
            if (dependencies == null || dependencies.Count == 0)
                return Failure("content_release_empty", "A release cannot be compiled without immutable content dependencies.");

            string graphFingerprint = Hash(string.Join("\n", dependencies.OrderBy(item => item.kind, StringComparer.Ordinal)
                .ThenBy(item => item.id, StringComparer.Ordinal).ThenBy(item => item.revision, StringComparer.Ordinal)
                .ThenBy(item => item.store, StringComparer.Ordinal).ThenBy(item => item.expectedHash, StringComparer.Ordinal)
                .Select(DependencyKey).ToArray()));
            string indexFingerprint = BuildIndexFingerprint(package);
            string releaseId = "release_" + Hash("release\0" + graphFingerprint + "\0" + indexFingerprint + "\0" + package.sourceManifestHash).Substring(0, 48);
            string manifestJson = BuildManifest(releaseId, package, package.sourceManifestHash, graphFingerprint, indexFingerprint, dependencies);
            if (manifestJson.Length > 262144)
                return Failure("content_manifest_too_large", "The release manifest is " + manifestJson.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " characters; the bounded maximum is 262144.");

            string manifestHash = Hash(manifestJson);
            var release = new ContentRelease
            {
                releaseId = releaseId,
                manifestJson = manifestJson,
                manifestHash = manifestHash,
                validatorVersion = validatorVersion,
                graphFingerprint = graphFingerprint,
                indexFingerprint = indexFingerprint,
                dependencies = dependencies
            };
            if (validatorVersion == ContentPackageValidator.ValidatorVersion)
                release.compilerAttestation = ValidatedCompilerAttestation;

            int existingIndex = package.releases.FindIndex(item => item != null && item.releaseId == releaseId);
            if (existingIndex >= 0)
            {
                ContentRelease existing = package.releases[existingIndex];
                if (existing.manifestHash != release.manifestHash)
                    return Failure("content_release_conflict", "An immutable release ID already has a different manifest.");
                if (validatorVersion == ContentPackageValidator.ValidatorVersion)
                {
                    package.releases[existingIndex] = release;
                    return CompiledReleaseResult.Ready(package, release);
                }
                return CompiledReleaseResult.Ready(package, existing);
            }
            package.releases.Add(release);
            return CompiledReleaseResult.Ready(package, release);
        }

        private static List<ContentReleaseDependency> BuildDependencies(ContentPackage package)
        {
            var result = new List<ContentReleaseDependency>();
            Add(result, package.topics, "topic", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.flows, "flow", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.dialogues, "dialogue", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.texts, "text", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.actions, "action", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.items, "item", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.furnitureDefinitions, "furniture", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            Add(result, package.rules, "rule", item => item.id, item => item.revision, item => item.semanticHash, package.storeId);
            foreach (AnchorRecord item in package.anchors ?? new List<AnchorRecord>())
                result.Add(new ContentReleaseDependency { kind = "anchor", id = item.id, revision = item.textHash,
                    store = package.storeId, expectedHash = item.textHash });
            foreach (AnchorLinkRecord item in package.anchorLinks ?? new List<AnchorLinkRecord>())
            {
                string linkHash = HashFields(item.anchorId, item.ownerKind, item.ownerId, item.ownerRevision, item.role);
                result.Add(new ContentReleaseDependency { kind = "anchor_link", id = item.id, revision = linkHash,
                    store = package.storeId, expectedHash = linkHash });
            }
            foreach (EmbeddingProfileRecord item in package.embeddingProfiles ?? new List<EmbeddingProfileRecord>())
            {
                string profileHash = ContentSemanticValidation.ProfileFingerprint(item);
                result.Add(new ContentReleaseDependency { kind = "embedding_profile", id = item.id,
                    revision = profileHash, store = package.storeId, expectedHash = profileHash });
            }
            foreach (EmbeddingRecord item in package.embeddings ?? new List<EmbeddingRecord>())
                result.Add(new ContentReleaseDependency { kind = "embedding", id = item.anchorId + "." + item.profileId,
                    revision = item.vectorHash, store = package.storeId, expectedHash = item.vectorHash });
            return result.OrderBy(item => item.kind, StringComparer.Ordinal).ThenBy(item => item.id, StringComparer.Ordinal)
                .ThenBy(item => item.revision, StringComparer.Ordinal).ToList();
        }

        private static void Add<T>(List<ContentReleaseDependency> output, IEnumerable<T> values, string kind,
            Func<T, string> id, Func<T, string> revision, Func<T, string> hash, string store)
        {
            foreach (T item in values ?? Enumerable.Empty<T>())
                output.Add(new ContentReleaseDependency { kind = kind, id = id(item), revision = revision(item),
                    store = store, expectedHash = hash(item) });
        }

        internal static string BuildIndexFingerprint(ContentPackage package)
        {
            var parts = new List<string>();
            foreach (EmbeddingProfileRecord item in package.embeddingProfiles ?? new List<EmbeddingProfileRecord>())
                parts.Add("profile\0" + item.id + "\0" + item.modelId + "\0" + item.modelRevision + "\0" + item.tokenizerHash +
                    "\0" + item.pipelineHash + "\0" + item.dimensions + "\0" + item.dtype + "\0" + item.normalization + "\0" + item.metric);
            foreach (EmbeddingRecord item in package.embeddings ?? new List<EmbeddingRecord>())
                parts.Add("vector\0" + item.anchorId + "\0" + item.profileId + "\0" + item.vectorHash);
            parts.Sort(StringComparer.Ordinal);
            return Hash("index-v1\n" + string.Join("\n", parts.ToArray()));
        }

        private static string BuildManifest(string releaseId, ContentPackage package, string sourceManifestHash, string graphFingerprint,
            string indexFingerprint, IEnumerable<ContentReleaseDependency> dependencies)
        {
            string dependencyCount = dependencies.Count().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return "{\"schemaVersion\":1,\"releaseId\":" + Quote(releaseId) +
                ",\"storeId\":" + Quote(package.storeId) +
                ",\"sourceManifestHash\":" + Quote(sourceManifestHash) +
                ",\"graphFingerprint\":" + Quote(graphFingerprint) +
                ",\"indexFingerprint\":" + Quote(indexFingerprint) +
                ",\"dependencyCount\":" + dependencyCount + "}";
        }

        private static string DependencyKey(ContentReleaseDependency item)
        { return item.kind + "\0" + item.id + "\0" + item.revision + "\0" + item.store + "\0" + item.expectedHash; }

        private static string Quote(string value) { return ContentJson.Quote(value); }

        private static string Hash(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return ToHex(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        private static string HashFields(params string[] values)
        {
            var builder = new StringBuilder();
            foreach (string value in values)
            {
                if (value == null) builder.Append("-1:");
                else builder.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(value);
                builder.Append('|');
            }
            return Hash(builder.ToString());
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f')) return false;
            return true;
        }

        private static CompiledReleaseResult Failure(string code, string message)
        { return CompiledReleaseResult.Failure(ContentStatuses.Invalid, code, "", message); }
    }
}
