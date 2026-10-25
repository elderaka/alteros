using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Linq;
using System.Text;

namespace AlterOS.Content
{
    public sealed class GteSemanticImportResult
    {
        public readonly string profileId;
        public readonly string sourceContentHash;
        public readonly int anchors;
        public readonly int routeLinks;
        public readonly int vectors;

        internal GteSemanticImportResult(string profile, string sourceHash, int anchorCount, int linkCount, int vectorCount)
        {
            profileId = profile ?? string.Empty;
            sourceContentHash = sourceHash ?? string.Empty;
            anchors = anchorCount;
            routeLinks = linkCount;
            vectors = vectorCount;
        }
    }

    /// <summary>Projects the existing baked GTE metadata/vector assets into an authored package without model inference.</summary>
    public static class GteSemanticAssetImporter
    {
        private const int LegacyMetadataSchema = 1;
        private const int SupportedMetadataSchema = 2;
        private const string SupportedModel = "Alibaba-NLP/gte-multilingual-base";
        private const double DuplicateAnchorMinimumCosine = 0.99999d;

        public static ContentResult<GteSemanticImportResult> ImportInto(ContentPackage package, byte[] metadataUtf8,
            byte[] vectorBytes, byte[] tokenizerBytes, string expectedContentHash, ContentHostVocabulary vocabulary)
        {
            if (package == null || package.anchors == null || package.anchorLinks == null || package.embeddingProfiles == null ||
                package.embeddings == null || vocabulary == null || metadataUtf8 == null || vectorBytes == null || tokenizerBytes == null)
                return Failure(ContentStatuses.Invalid, "gte_import_arguments_invalid", "A package, host vocabulary and exact metadata/vector/tokenizer bytes are required.");

            JsonObject metadata;
            try { metadata = ContentJson.Parse(Encoding.UTF8.GetString(metadataUtf8)) as JsonObject; }
            catch (Exception exception) { return Failure(ContentStatuses.Invalid, "gte_metadata_invalid", exception.Message); }
            if (metadata == null || metadata.Count != 7 || !HasFields(metadata, "schemaVersion", "model", "modelRevision", "codeRevision", "dimension", "contentHash", "anchors"))
                return Failure(ContentStatuses.Invalid, "gte_metadata_fields_invalid", "GTE metadata must contain the supported exact schema fields.");

            try
            {
                int schema = (int)metadata["schemaVersion"];
                string modelId = (string)metadata["model"];
                string modelRevision = (string)metadata["modelRevision"];
                string pipelineRevision = (string)metadata["codeRevision"];
                int dimensions = (int)metadata["dimension"];
                string sourceHash = (string)metadata["contentHash"];
                JsonArray anchors = metadata["anchors"] as JsonArray;
                if ((schema != LegacyMetadataSchema && schema != SupportedMetadataSchema) || modelId != SupportedModel || string.IsNullOrWhiteSpace(modelRevision) || modelRevision.Length > 128 ||
                    string.IsNullOrWhiteSpace(pipelineRevision) || pipelineRevision.Length > 128 || !ContentSemanticValidation.Sha256(sourceHash) ||
                    sourceHash != expectedContentHash || dimensions < 1 || dimensions > 8192 || anchors == null || anchors.Count == 0 ||
                    vectorBytes.Length != checked(anchors.Count * dimensions * sizeof(float)))
                    return Failure(ContentStatuses.Incompatible, "gte_asset_fingerprint_mismatch",
                        "The baked model/schema/source fingerprint or float32 vector byte count differs from the trusted build manifest.");

                var profile = new EmbeddingProfileRecord
                {
                    modelId = modelId,
                    modelRevision = modelRevision,
                    tokenizerHash = ContentSemanticValidation.Hash(tokenizerBytes),
                    pipelineHash = ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes(pipelineRevision)),
                    dimensions = dimensions,
                    dtype = "float32-le",
                    normalization = "l2",
                    metric = "dot"
                };
                string profileFingerprint = ContentSemanticValidation.ProfileFingerprint(profile);
                profile.id = "gte." + profileFingerprint.Substring(0, 40);
                string profileCode, profileMessage;
                if (!ContentSemanticValidation.ValidateProfile(profile, out profileCode, out profileMessage))
                    return Failure(ContentStatuses.Invalid, profileCode, profileMessage);

                var pendingAnchors = new Dictionary<string, AnchorRecord>(StringComparer.Ordinal);
                var pendingLinks = new Dictionary<string, AnchorLinkRecord>(StringComparer.Ordinal);
                var pendingVectors = new Dictionary<string, EmbeddingRecord>(StringComparer.Ordinal);
                var routeTextKeys = new HashSet<string>(StringComparer.Ordinal);
                int vectorOffset = 0;
                for (int index = 0; index < anchors.Count; index++)
                {
                    JsonObject item = anchors[index] as JsonObject;
                    if (item == null || schema == LegacyMetadataSchema && (item.Count != 2 || !HasFields(item, "routeId", "text")) ||
                        schema == SupportedMetadataSchema && (item.Count != 5 || !HasFields(item, "ownerKind", "ownerId", "ownerRevision", "role", "text")))
                        return Failure(ContentStatuses.Invalid, "gte_anchor_metadata_invalid", "Each GTE anchor must contain the exact fields for its supported metadata schema.");
                    string ownerKind = schema == LegacyMetadataSchema ? "canonical" : (string)item["ownerKind"];
                    string ownerId = schema == LegacyMetadataSchema ? (string)item["routeId"] : (string)item["ownerId"];
                    string ownerRevision = schema == LegacyMetadataSchema
                        ? ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes("gte-route-owner\0" + ownerId))
                        : (string)item["ownerRevision"];
                    string role = schema == LegacyMetadataSchema ? "gte_route" : (string)item["role"];
                    string text = (string)item["text"];
                    if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128 || string.IsNullOrWhiteSpace(text) || text.Length > 2048 ||
                        !ContentSemanticValidation.Sha256(ownerRevision) || string.IsNullOrWhiteSpace(role) || role.Length > 128 ||
                        !routeTextKeys.Add(ownerKind + "\0" + ownerId + "\0" + role + "\0" + text))
                        return Failure(ContentStatuses.Invalid, "gte_anchor_owner_invalid", "A GTE anchor is empty, duplicated, oversized or has malformed ownership.");

                    if (ownerKind == "canonical")
                    {
                        if (role != "gte_route" || !vocabulary.IsCanonicalOwnerAllowed(ownerId) ||
                            ownerRevision != ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes("gte-route-owner\0" + ownerId)))
                            return Failure(ContentStatuses.Invalid, "gte_anchor_owner_invalid", "A canonical GTE anchor must name its exact host-registered route owner.");
                    }
                    else if (ownerKind == "topic")
                    {
                        TopicRecord topic = package.topics.FirstOrDefault(candidate => candidate != null &&
                            candidate.id == ownerId && candidate.revision == ownerRevision);
                        if (topic == null)
                            return Failure(ContentStatuses.Invalid, "gte_anchor_owner_invalid", "A Topic GTE anchor must point to an exact Topic revision in the selected package.");
                    }
                    else return Failure(ContentStatuses.Invalid, "gte_anchor_owner_invalid", "GTE anchors may be owned only by canonical routes or selected Topics.");

                    byte[] textBytes = Encoding.UTF8.GetBytes(text);
                    string anchorId = ContentSemanticValidation.Hash(textBytes);
                    AnchorRecord existingAnchor = package.anchors.FirstOrDefault(candidate => candidate != null && candidate.id == anchorId);
                    AnchorRecord anchor = existingAnchor ?? new AnchorRecord { id = anchorId, textHash = anchorId, text = text, language = "und" };
                    if (anchor.text != text || anchor.textHash != anchorId)
                        return Failure(ContentStatuses.Invalid, "gte_anchor_identity_conflict", "An immutable Anchor ID already names different source text.");
                    if (!pendingAnchors.ContainsKey(anchorId)) pendingAnchors.Add(anchorId, anchor);

                    string linkId;
                    AnchorLinkRecord link;
                    if (ownerKind == "canonical")
                    {
                        linkId = "gte." + ContentSemanticValidation.Hash(Encoding.UTF8.GetBytes(ownerId + "\0" + anchorId)).Substring(0, 40);
                        link = new AnchorLinkRecord
                        { id = linkId, anchorId = anchorId, ownerKind = ownerKind, ownerId = ownerId, ownerRevision = ownerRevision, role = role };
                    }
                    else
                    {
                        AnchorRecord exactAnchor = package.anchors.FirstOrDefault(candidate => candidate != null && candidate.id == anchorId);
                        link = package.anchorLinks.FirstOrDefault(candidate => candidate != null && candidate.anchorId == anchorId &&
                            candidate.ownerKind == ownerKind && candidate.ownerId == ownerId && candidate.ownerRevision == ownerRevision && candidate.role == role);
                        if (exactAnchor == null || exactAnchor.text != text || link == null)
                            return Failure(ContentStatuses.Invalid, "gte_topic_anchor_not_in_snapshot", "A Topic GTE anchor must match an existing exact Topic anchor link in the SQL snapshot.");
                        linkId = link.id;
                    }
                    AnchorLinkRecord existingLink = package.anchorLinks.FirstOrDefault(candidate => candidate != null && candidate.id == linkId);
                    if (existingLink != null && !SameLink(existingLink, link))
                        return Failure(ContentStatuses.Invalid, "gte_anchor_link_conflict", "A route Anchor link ID was reused with different ownership.");
                    if (!pendingLinks.ContainsKey(linkId)) pendingLinks.Add(linkId, existingLink ?? link);

                    byte[] vector = new byte[dimensions * sizeof(float)];
                    Buffer.BlockCopy(vectorBytes, vectorOffset, vector, 0, vector.Length);
                    vectorOffset += vector.Length;
                    string vectorCode, vectorMessage;
                    if (!ContentSemanticValidation.ValidateVector(vector, profile, out vectorCode, out vectorMessage))
                        return Failure(ContentStatuses.Invalid, vectorCode, "GTE vector " + index.ToString(CultureInfo.InvariantCulture) + ": " + vectorMessage);
                    EmbeddingRecord embedding = new EmbeddingRecord
                    { anchorId = anchorId, profileId = profile.id, vector = vector, vectorHash = ContentSemanticValidation.Hash(vector) };
                    EmbeddingRecord duplicate;
                    if (pendingVectors.TryGetValue(anchorId, out duplicate))
                    {
                        // Identical text has one immutable Anchor ID; tiny repeated-encode noise stays one deterministic vector.
                        if (duplicate.vectorHash != embedding.vectorHash && Similarity(duplicate.vector, embedding.vector) < DuplicateAnchorMinimumCosine)
                            return Failure(ContentStatuses.Invalid, "gte_duplicate_vector_conflict",
                                "Repeated text under the same content-addressed Anchor has materially different baked vectors.");
                    }
                    else pendingVectors.Add(anchorId, embedding);
                }

                EmbeddingProfileRecord existingProfile = package.embeddingProfiles.FirstOrDefault(candidate => candidate != null && candidate.id == profile.id);
                if (existingProfile != null && ContentSemanticValidation.ProfileFingerprint(existingProfile) != profileFingerprint)
                    return Failure(ContentStatuses.Invalid, "gte_profile_conflict", "The profile ID already has a different model or pipeline fingerprint.");
                foreach (KeyValuePair<string, EmbeddingRecord> pair in pendingVectors)
                {
                    EmbeddingRecord existing = package.embeddings.FirstOrDefault(candidate => candidate != null &&
                        candidate.anchorId == pair.Key && candidate.profileId == profile.id);
                    if (existing != null && existing.vectorHash != pair.Value.vectorHash)
                        return Failure(ContentStatuses.Invalid, "gte_vector_conflict", "An immutable Anchor/profile pair already has different vector bytes.");
                }

                if (existingProfile == null) package.embeddingProfiles.Add(profile);
                foreach (KeyValuePair<string, AnchorRecord> pair in pendingAnchors)
                    if (!package.anchors.Any(candidate => candidate != null && candidate.id == pair.Key)) package.anchors.Add(pair.Value);
                foreach (KeyValuePair<string, AnchorLinkRecord> pair in pendingLinks)
                    if (!package.anchorLinks.Any(candidate => candidate != null && candidate.id == pair.Key)) package.anchorLinks.Add(pair.Value);
                foreach (KeyValuePair<string, EmbeddingRecord> pair in pendingVectors)
                    if (!package.embeddings.Any(candidate => candidate != null && candidate.anchorId == pair.Key && candidate.profileId == profile.id))
                        package.embeddings.Add(pair.Value);

                return ContentResult<GteSemanticImportResult>.Ready(new GteSemanticImportResult(profile.id, sourceHash,
                    pendingAnchors.Count, pendingLinks.Count, pendingVectors.Count));
            }
            catch (Exception exception)
            { return Failure(ContentStatuses.Invalid, "gte_metadata_value_invalid", exception.Message); }
        }

        private static bool HasFields(JsonObject value, params string[] names)
        { return names.All(value.ContainsKey); }

        private static bool SameLink(AnchorLinkRecord left, AnchorLinkRecord right)
        { return left.anchorId == right.anchorId && left.ownerKind == right.ownerKind && left.ownerId == right.ownerId &&
            left.ownerRevision == right.ownerRevision && left.role == right.role; }

        private static double Similarity(byte[] left, byte[] right)
        {
            double dot = 0d, leftNorm = 0d, rightNorm = 0d;
            for (int offset = 0; offset < left.Length; offset += sizeof(float))
            {
                double a = ContentSemanticValidation.ReadFloat32LittleEndian(left, offset);
                double b = ContentSemanticValidation.ReadFloat32LittleEndian(right, offset);
                dot += a * b; leftNorm += a * a; rightNorm += b * b;
            }
            return dot / Math.Sqrt(leftNorm * rightNorm);
        }

        private static ContentResult<GteSemanticImportResult> Failure(string status, string code, string message)
        { return ContentResult<GteSemanticImportResult>.Failure(status, code, "gte_semantic_assets", message); }
    }
}
