using System;
using System.Text;
using AlterOS.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AlterOS.Authoring
{
    /// <summary>Projects an exact authoring revision into the existing content compiler input.</summary>
    public static class AuthoringContentSourceProjection
    {
        public static AuthoringResult<ContentSourceDocument> Project(AuthoringDocument document)
        {
            if (document == null || !AuthoringContentKinds.IsSupported(document.kind) ||
                !AuthoringRepository.IsLogicalPath(document.logicalPath) || document.documentBytes == null ||
                document.documentBytes.Length == 0 || AuthoringRepository.Hash(document.documentBytes) != document.revisionHash)
                return Failure("authoring_source_document_invalid", document == null ? string.Empty : document.logicalPath,
                    "A supported, hash-matched authoring document is required.");

            string error;
            if (!AuthoringRepository.IsJsonDocument(document.documentBytes, out error))
                return Failure("authoring_source_json_invalid", document.logicalPath, error);

            try
            {
                string kind = document.kind;
                JToken root = ContentJson.Parse(new UTF8Encoding(false, true).GetString(document.documentBytes).TrimStart('\uFEFF'));
                if (kind == "topic" || kind == "scenario")
                {
                    JObject wrapper = root as JObject;
                    JObject inner = wrapper == null ? null : wrapper["document"] as JObject;
                    if (inner == null)
                        return Failure("authoring_draft_document_missing", document.logicalPath,
                            "A Workbench draft wrapper must contain its typed document object before compilation.");
                    string contentKind = kind == "topic" ? ContentSourceKinds.TopicDefinition : ContentSourceKinds.ScenarioDefinition;
                    string identityField = kind == "topic" ? "topicId" : "scenarioId";
                    string sourceId = ReadIdentity(inner, identityField);
                    if (string.IsNullOrEmpty(sourceId))
                        return Failure("authoring_content_identity_missing", document.logicalPath, "The selected draft document has no stable content ID.");
                    if (!string.IsNullOrEmpty(document.sourceId) && document.sourceId != sourceId)
                        return Failure("authoring_source_identity_changed", document.logicalPath, "The content ID differs from this document's stored stable source ID.");

                    byte[] projected = Encoding.UTF8.GetBytes(inner.ToString(Formatting.None));
                    return AuthoringResult<ContentSourceDocument>.Ready(new ContentSourceDocument(
                        contentKind, document.logicalPath, sourceId, AuthoringRepository.Hash(projected), projected));
                }

                string sourceIdentity;
                string field = IdentityField(kind);
                if (field.Length == 0)
                    sourceIdentity = string.IsNullOrEmpty(document.sourceId) ? document.documentId : document.sourceId;
                else
                {
                    JObject content = root as JObject;
                    sourceIdentity = content == null ? string.Empty : ReadIdentity(content, field);
                    if (string.IsNullOrEmpty(sourceIdentity))
                        return Failure("authoring_content_identity_missing", document.logicalPath, "The selected content document has no stable ID.");
                    if (!string.IsNullOrEmpty(document.sourceId) && document.sourceId != sourceIdentity)
                        return Failure("authoring_source_identity_changed", document.logicalPath, "The content ID differs from this document's stored stable source ID.");
                }

                return AuthoringResult<ContentSourceDocument>.Ready(new ContentSourceDocument(
                    kind, document.logicalPath, sourceIdentity, document.revisionHash, document.documentBytes));
            }
            catch (Exception exception)
            { return Failure("authoring_source_projection_failed", document.logicalPath, exception.Message); }
        }

        private static string IdentityField(string kind)
        {
            if (kind == ContentSourceKinds.GenericDialogue) return "id";
            if (kind == ContentSourceKinds.TopicDefinition) return "topicId";
            if (kind == ContentSourceKinds.ScenarioDefinition) return "scenarioId";
            return string.Empty;
        }

        private static string ReadIdentity(JObject document, string field)
        {
            JToken value;
            if (document == null || !document.TryGetValue(field, out value) || ContentJson.IsNull(value)) return string.Empty;
            string identity = (string)value;
            return AuthoringRepository.IsIdentifier(identity) ? identity : string.Empty;
        }

        private static AuthoringResult<ContentSourceDocument> Failure(string code, string path, string message)
        { return AuthoringResult<ContentSourceDocument>.Failure(AuthoringStatuses.Invalid, code, path, message); }
    }
}
