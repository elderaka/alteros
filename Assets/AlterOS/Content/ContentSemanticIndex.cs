using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Semantics;

namespace AlterOS.Content
{
    internal static class ContentSemanticValidation
    {
        internal static bool ValidateProfile(EmbeddingProfileRecord profile, out string code, out string message)
        {
            code = "embedding_profile_invalid";
            message = "An embedding profile is malformed or unsupported.";
            if (profile == null || !Identifier(profile.id) || string.IsNullOrWhiteSpace(profile.modelId) || profile.modelId.Length > 128 ||
                string.IsNullOrWhiteSpace(profile.modelRevision) || profile.modelRevision.Length > 128 ||
                !Sha256(profile.tokenizerHash) || !Sha256(profile.pipelineHash) || profile.dimensions < 1 || profile.dimensions > 8192 ||
                profile.dtype != "float32-le" || (profile.normalization != "none" && profile.normalization != "l2") ||
                (profile.metric != "cosine" && profile.metric != "dot" && profile.metric != "l2")) return false;
            code = string.Empty; message = string.Empty; return true;
        }

        internal static bool ValidateVector(byte[] bytes, EmbeddingProfileRecord profile, out string code, out string message)
        {
            code = "embedding_profile_invalid"; message = "A valid embedding profile is required.";
            if (!ValidateProfile(profile, out code, out message)) return false;
            code = "embedding_dimension_mismatch";
            message = "Vector bytes must contain exactly one little-endian float32 value per profile dimension.";
            if (bytes == null || bytes.Length != profile.dimensions * sizeof(float)) return false;

            double normSquared = 0d;
            for (int index = 0; index < profile.dimensions; index++)
            {
                float value = ReadFloat32LittleEndian(bytes, index * sizeof(float));
                if (float.IsNaN(value) || float.IsInfinity(value))
                { code = "embedding_nonfinite"; message = "Embedding values must all be finite."; return false; }
                normSquared += (double)value * value;
            }
            if (normSquared <= 0d)
            { code = "embedding_zero_norm"; message = "An embedding cannot have zero magnitude."; return false; }
            if (profile.normalization == "l2" && Math.Abs(Math.Sqrt(normSquared) - 1d) > 0.001d)
            { code = "embedding_normalization_invalid"; message = "An L2-normalized profile requires unit-length vectors."; return false; }
            code = string.Empty; message = string.Empty; return true;
        }

        internal static string ProfileFingerprint(EmbeddingProfileRecord profile)
        {
            return Hash(Encoding.UTF8.GetBytes(string.Join("\0", profile.id, profile.modelId, profile.modelRevision, profile.tokenizerHash,
                profile.pipelineHash, profile.dimensions.ToString(CultureInfo.InvariantCulture), profile.dtype,
                profile.normalization, profile.metric)));
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes ?? new byte[0]));
        }

        internal static string HashFields(params string[] values)
        {
            var builder = new StringBuilder();
            foreach (string value in values)
            {
                if (value == null) builder.Append("-1:");
                else builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
                builder.Append('|');
            }
            return Hash(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        internal static float ReadFloat32LittleEndian(byte[] bytes, int offset)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(bytes, offset);
            byte[] word = new byte[sizeof(float)];
            Buffer.BlockCopy(bytes, offset, word, 0, sizeof(float));
            if (!BitConverter.IsLittleEndian) Array.Reverse(word);
            return BitConverter.ToSingle(word, 0);
        }

        internal static bool Sha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f')) return false;
            return true;
        }

        private static bool Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value == "." || value == "..") return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':')) return false;
            return true;
        }

        private static string Hex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }

    internal sealed class ContentSemanticStoreData
    {
        internal readonly List<AnchorRecord> anchors = new List<AnchorRecord>();
        internal readonly List<AnchorLinkRecord> links = new List<AnchorLinkRecord>();
        internal readonly List<EmbeddingProfileRecord> profiles = new List<EmbeddingProfileRecord>();
        internal readonly List<EmbeddingRecord> embeddings = new List<EmbeddingRecord>();
    }

    public sealed class ContentSemanticAnchorLinkSnapshot
    {
        public readonly string id;
        public readonly string anchorId;
        public readonly string ownerKind;
        public readonly string ownerId;
        public readonly string ownerRevision;
        public readonly string role;

        internal ContentSemanticAnchorLinkSnapshot(AnchorLinkRecord source)
        {
            id = source.id; anchorId = source.anchorId; ownerKind = source.ownerKind;
            ownerId = source.ownerId; ownerRevision = source.ownerRevision; role = source.role;
        }
    }

    public sealed class ContentSemanticAnchorSnapshot
    {
        private readonly float[] vector;
        public readonly string id;
        public readonly string language;
        public readonly string text;
        public readonly string textHash;
        public readonly ReadOnlyCollection<ContentSemanticAnchorLinkSnapshot> links;

        internal ContentSemanticAnchorSnapshot(AnchorRecord source, IEnumerable<AnchorLinkRecord> sourceLinks, float[] values)
        {
            id = source.id; language = source.language; text = source.text; textHash = source.textHash;
            vector = values == null ? new float[0] : (float[])values.Clone();
            links = new ReadOnlyCollection<ContentSemanticAnchorLinkSnapshot>((sourceLinks ?? Enumerable.Empty<AnchorLinkRecord>())
                .Select(item => new ContentSemanticAnchorLinkSnapshot(item)).ToList());
        }

        public float[] CopyVector() { return (float[])vector.Clone(); }
        internal float[] Vector { get { return vector; } }
    }

    public sealed class ContentSemanticSnapshot
    {
        private readonly Dictionary<string, ContentSemanticAnchorSnapshot> anchorsById;
        private readonly RouteGroup[] routeGroups;
        private readonly int dimension;

        public string ReleaseId { get; private set; }
        public string IndexFingerprint { get; private set; }
        public string ProfileId { get; private set; }
        public int Dimension { get { return dimension; } }
        public int AnchorCount { get { return anchorsById.Count; } }
        public int RouteCount { get { return routeGroups.Length; } }
        public int LinkCount { get { return anchorsById.Values.Sum(item => item.links.Count); } }

        internal ContentSemanticSnapshot(string releaseId, string indexFingerprint, string profileId,
            EmbeddingProfileRecord profile, IEnumerable<AnchorRecord> anchors, IEnumerable<AnchorLinkRecord> links,
            IDictionary<string, float[]> vectors)
        {
            ReleaseId = releaseId ?? string.Empty;
            IndexFingerprint = indexFingerprint ?? string.Empty;
            ProfileId = profileId ?? string.Empty;
            dimension = profile == null ? 0 : profile.dimensions;
            var linksByAnchor = (links ?? Enumerable.Empty<AnchorLinkRecord>()).GroupBy(item => item.anchorId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            anchorsById = new Dictionary<string, ContentSemanticAnchorSnapshot>(StringComparer.Ordinal);
            foreach (AnchorRecord anchor in anchors ?? Enumerable.Empty<AnchorRecord>())
            {
                float[] vector;
                List<AnchorLinkRecord> anchorLinks;
                if (!vectors.TryGetValue(anchor.id, out vector) || !linksByAnchor.TryGetValue(anchor.id, out anchorLinks))
                    throw new ArgumentException("A semantic snapshot requires a vector and release link for every Anchor.");
                anchorsById.Add(anchor.id, new ContentSemanticAnchorSnapshot(anchor, anchorLinks, vector));
            }
            RouteAnchor[] routeAnchors = anchorsById.Values.SelectMany(anchor => anchor.links
                .Where(link => (link.ownerKind == "canonical" && link.role == "gte_route") || link.ownerKind == "topic")
                .Select(link => new RouteAnchor(link.ownerId, anchor.text, anchor.Vector)))
                .GroupBy(item => new { item.routeId, item.text })
                .Select(group => group.First())
                .OrderBy(item => item.routeId, StringComparer.Ordinal)
                .ThenBy(item => item.text, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.text, StringComparer.Ordinal)
                .ToArray();
            routeGroups = routeAnchors.GroupBy(item => item.routeId, StringComparer.Ordinal)
                .Select(group => new RouteGroup(group.Key, group.ToArray())).ToArray();
        }

        public ContentSemanticAnchorSnapshot GetAnchor(string anchorId)
        {
            ContentSemanticAnchorSnapshot result;
            return anchorsById.TryGetValue(anchorId ?? string.Empty, out result) ? result : null;
        }

        public DialogueRouteMatch[] RankRoutes(float[] query, float minimumSimilarity = 0.45f)
        {
            ValidateQuery(query, minimumSimilarity);
            var matches = new List<DialogueRouteMatch>(routeGroups.Length);
            foreach (RouteGroup route in routeGroups)
            {
                var nearest = new List<KeyValuePair<RouteAnchor, float>>(3);
                foreach (RouteAnchor anchor in route.anchors)
                {
                    float similarity = Dot(anchor.vector, query);
                    int insertAt = 0;
                    while (insertAt < nearest.Count && nearest[insertAt].Value >= similarity) insertAt++;
                    if (insertAt >= 3) continue;
                    nearest.Insert(insertAt, new KeyValuePair<RouteAnchor, float>(anchor, similarity));
                    if (nearest.Count > 3) nearest.RemoveAt(3);
                }
                if (nearest.Count == 0) continue;
                float weighted = 0f, weights = 0f;
                int support = 0;
                for (int index = 0; index < nearest.Count; index++)
                {
                    float similarity = nearest[index].Value;
                    float weight = 1f / (index + 1);
                    weighted += similarity * weight; weights += weight;
                    if (similarity >= minimumSimilarity) support++;
                }
                weighted /= weights;
                float best = nearest[0].Value;
                matches.Add(new DialogueRouteMatch(route.routeId, weighted + 0.40f * (best - weighted), best,
                    support, nearest[0].Key.text));
            }
            matches.Sort((left, right) => right.Score.CompareTo(left.Score));
            return matches.ToArray();
        }

        public TopicFamilySemanticMatch[] RankTopicFamilies(float[] query, float minimumSimilarity = TopicFamilySemanticMatcher.DefaultMinimumSimilarity)
        {
            if (query == null || query.Length != dimension)
                throw new ArgumentException("Query vector dimension does not match the semantic snapshot.", nameof(query));
            var familyAnchors = new List<TopicFamilyAnchorVector>();
            foreach (ContentSemanticAnchorSnapshot anchor in anchorsById.Values)
                foreach (ContentSemanticAnchorLinkSnapshot link in anchor.links)
                    if (link.ownerKind == "topic")
                        familyAnchors.Add(new TopicFamilyAnchorVector { TopicId = link.ownerId, Anchor = anchor.text, Embedding = anchor.Vector });
            return TopicFamilySemanticMatcher.Rank(query, familyAnchors, minimumSimilarity);
        }

        private void ValidateQuery(float[] query, float minimumSimilarity)
        {
            if (query == null || query.Length != dimension)
                throw new ArgumentException("Query vector dimension does not match the semantic snapshot.", nameof(query));
            if (float.IsNaN(minimumSimilarity) || float.IsInfinity(minimumSimilarity))
                throw new ArgumentOutOfRangeException(nameof(minimumSimilarity));
            for (int index = 0; index < query.Length; index++)
                if (float.IsNaN(query[index]) || float.IsInfinity(query[index]))
                    throw new ArgumentException("Query vectors must contain only finite values.", nameof(query));
        }

        private static float Dot(float[] left, float[] right)
        {
            float result = 0f;
            for (int index = 0; index < right.Length; index++) result += left[index] * right[index];
            return result;
        }

        private sealed class RouteAnchor
        {
            internal readonly string routeId;
            internal readonly string text;
            internal readonly float[] vector;
            internal RouteAnchor(string route, string anchor, float[] values)
            { routeId = route; text = anchor; vector = values; }
        }

        private sealed class RouteGroup
        {
            internal readonly string routeId;
            internal readonly RouteAnchor[] anchors;
            internal RouteGroup(string id, RouteAnchor[] values) { routeId = id; anchors = values; }
        }
    }

    public static class ContentSemanticIndex
    {
        public static ContentResult<ContentSemanticSnapshot> Load(ContentRepository repository, ContentRelease release, string profileId)
        {
            if (repository == null || release == null || string.IsNullOrWhiteSpace(release.releaseId))
                return Failure("semantic_load_request_invalid", "A repository and exact compiled release are required.");
            ContentResult<ContentSemanticStoreData> read = repository.ReadSemanticData(release);
            if (!read.IsReady) return Failure(read.status, FirstCode(read.diagnostics), FirstMessage(read.diagnostics));
            ContentSemanticStoreData data = read.value;
            var indexPackage = new ContentPackage { embeddingProfiles = data.profiles, embeddings = data.embeddings };
            string fingerprint = ContentReleaseCompiler.BuildIndexFingerprint(indexPackage);
            if (fingerprint != release.indexFingerprint)
                return Failure(ContentStatuses.Incompatible, "semantic_index_fingerprint_mismatch", "The SQL profile/vector set differs from the pinned release fingerprint.");
            if (data.profiles.Count == 0 && data.anchors.Count == 0)
            {
                if (!string.IsNullOrEmpty(profileId))
                    return Failure(ContentStatuses.Missing, "semantic_profile_missing", "The anchor-free release has no selected semantic profile.");
                return ContentResult<ContentSemanticSnapshot>.Ready(new ContentSemanticSnapshot(release.releaseId,
                    release.indexFingerprint, string.Empty, null, data.anchors, data.links, new Dictionary<string, float[]>(StringComparer.Ordinal)));
            }

            EmbeddingProfileRecord profile = data.profiles.FirstOrDefault(item => item.id == profileId);
            if (profile == null)
                return Failure(ContentStatuses.Missing, "semantic_profile_missing", "The exact requested profile is not in the release.");
            foreach (EmbeddingProfileRecord candidate in data.profiles)
            {
                int profileVectors = data.embeddings.Count(item => item.profileId == candidate.id);
                if (profileVectors != data.anchors.Count)
                    return Failure(ContentStatuses.Unavailable, "semantic_index_incomplete", "Every Anchor in the release must have one validated vector for each published profile.");
            }

            var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (EmbeddingRecord embedding in data.embeddings.Where(item => item.profileId == profile.id))
            {
                float[] decoded = new float[profile.dimensions];
                for (int index = 0; index < decoded.Length; index++)
                    decoded[index] = ContentSemanticValidation.ReadFloat32LittleEndian(embedding.vector, index * sizeof(float));
                vectors.Add(embedding.anchorId, decoded);
            }
            if (data.links.Any(item => !data.anchors.Any(anchor => anchor.id == item.anchorId)))
                return Failure(ContentStatuses.Incompatible, "semantic_anchor_link_missing", "A released Anchor link points outside the exact Anchor set.");
            try
            {
                return ContentResult<ContentSemanticSnapshot>.Ready(new ContentSemanticSnapshot(release.releaseId,
                    release.indexFingerprint, profile.id, profile, data.anchors, data.links, vectors));
            }
            catch (Exception exception)
            { return Failure(ContentStatuses.Incompatible, "semantic_snapshot_invalid", exception.Message); }
        }

        private static string FirstCode(IList<ContentDiagnostic> diagnostics)
        { return diagnostics == null || diagnostics.Count == 0 ? "semantic_index_unavailable" : diagnostics[0].code; }

        private static string FirstMessage(IList<ContentDiagnostic> diagnostics)
        { return diagnostics == null || diagnostics.Count == 0 ? "The exact semantic index could not be loaded." : diagnostics[0].message; }

        private static ContentResult<ContentSemanticSnapshot> Failure(string code, string message)
        { return Failure(ContentStatuses.Invalid, code, message); }

        private static ContentResult<ContentSemanticSnapshot> Failure(string status, string code, string message)
        { return ContentResult<ContentSemanticSnapshot>.Failure(status, code, "semantic_index", message); }
    }
}
