using System;
using System.Collections.Generic;
using System.Text;
using AlterOS.Activities;
using Newtonsoft.Json.Linq;

namespace AlterOS.Content
{
    /// <summary>Validates one editable source document with the same structural rules used by the release importer.</summary>
    public static class ContentAuthoringValidator
    {
        public static ContentAuthoringValidationResult Validate(string kind, byte[] documentBytes)
        { return Validate(kind, documentBytes, ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder()); }

        public static ContentAuthoringValidationResult Validate(string kind, byte[] documentBytes,
            ActivitySequenceBuilder sequenceBuilder)
        {
            var diagnostics = new List<ContentDiagnostic>();
            if (documentBytes == null || documentBytes.Length == 0 || documentBytes.Length > 4 * 1024 * 1024)
            {
                diagnostics.Add(new ContentDiagnostic("content_authoring_bytes_invalid", "document",
                    "A non-empty source document of at most four megabytes is required."));
                return new ContentAuthoringValidationResult(false, diagnostics);
            }

            try
            {
                string json = new UTF8Encoding(false, true).GetString(documentBytes).TrimStart('\uFEFF');
                JToken root = ContentJson.Parse(json);
                string sourceKind = kind;
                if (kind == "topic" || kind == "scenario")
                {
                    JObject wrapper = root as JObject;
                    JObject inner = wrapper == null ? null : wrapper["document"] as JObject;
                    if (inner == null)
                    {
                        diagnostics.Add(new ContentDiagnostic("authoring_draft_document_missing", "document.document",
                            "A Topic or Scenario draft must contain its typed document before validation."));
                        return new ContentAuthoringValidationResult(false, diagnostics);
                    }
                    root = inner;
                    sourceKind = kind == "topic" ? ContentSourceKinds.TopicDefinition : ContentSourceKinds.ScenarioDefinition;
                }

                if (!IsSupported(sourceKind))
                {
                    diagnostics.Add(new ContentDiagnostic("content_authoring_kind_unsupported", "kind",
                        "The source kind is not one of the six reusable content kinds or retained Generic dialogue."));
                    return new ContentAuthoringValidationResult(false, diagnostics);
                }
                diagnostics.AddRange(LegacyContentAdapter.ValidateSourceShape(sourceKind, root, "document", sequenceBuilder));
            }
            catch (Exception exception)
            {
                diagnostics.Add(new ContentDiagnostic("content_authoring_json_invalid", "document", exception.Message));
            }
            return new ContentAuthoringValidationResult(diagnostics.Count == 0, diagnostics);
        }

        private static bool IsSupported(string kind)
        {
            return kind == ContentSourceKinds.GenericDialogue || kind == ContentSourceKinds.TopicDefinition ||
                kind == ContentSourceKinds.ScenarioDefinition || kind == ContentSourceKinds.DialogueDefinition ||
                kind == ContentSourceKinds.FurnitureDefinition || kind == ContentSourceKinds.FurnitureCatalogueSettings || kind == ContentSourceKinds.ItemDefinition ||
                kind == ContentSourceKinds.ActionDefinition;
        }
    }
}
