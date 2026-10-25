using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Authoring;
using AlterOS.Content;
using AlterOS.Dialogue;
using AlterOS.Furniture;
using AlterOS.Scenarios;
using AlterOS.Topics;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Workbench presentation metadata over the released source schemas and trusted registries.</summary>
    internal static class WorkbenchSchemaCommands
    {
        private sealed class KindContract
        {
            public string kindId;
            public string displayName;
            public string defaultSourceKind;
            public string identityField;
            public int? schemaVersion;
            public List<string> sourceKinds;
            public List<string> referenceSourceKinds;
            public List<object> sections;
            public object documentTemplate;
            public object graph;
            public List<object> references;
            public List<object> deferredFields;
            public bool preview;
            public string previewReason;
        }

        public static WorkbenchResult Describe(IDictionary<string, object> payload)
        {
            string kindId = JsonWire.String(payload, "kindId");
            KindContract contract = GetContract(kindId);
            if (contract == null)
                return WorkbenchResult.Failure("invalid", "kind_id_invalid", "/kindId",
                    "kindId must be one of dialogue, scenario, topic, furniture, item, or action.");

            string sourceKind = JsonWire.String(payload, "sourceKind", contract.defaultSourceKind);
            if (kindId == "dialogue" && sourceKind == ContentSourceKinds.GenericDialogue)
                contract = DialogueContract(true);
            if (kindId == "furniture" && sourceKind == ContentSourceKinds.FurnitureCatalogueSettings)
                contract = FurnitureSettingsContract();
            if (!contract.sourceKinds.Contains(sourceKind, StringComparer.Ordinal))
                return WorkbenchResult.Failure("invalid", "source_kind_invalid", "/sourceKind",
                    "sourceKind is not registered for this Workbench kind.");

            var registered = new HashSet<string>(AuthoringContentKinds.SourceKinds, StringComparer.Ordinal);
            bool sourceAvailable = contract.sourceKinds.All(registered.Contains);
            var capabilities = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "list", sourceAvailable }, { "read", sourceAvailable }, { "create", sourceAvailable },
                { "edit", sourceAvailable }, { "validate", sourceAvailable }, { "preview", sourceAvailable && contract.preview },
                { "clone", false }, { "retire", false }
            };
            var unavailableReasons = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "create", sourceAvailable ? "" : "The source kind is not registered by AuthoringContentKinds." },
                { "edit", sourceAvailable ? "" : "The source kind is not registered by AuthoringContentKinds." },
                { "validate", sourceAvailable ? "" : "The source kind is not registered by AuthoringContentKinds." },
                { "preview", contract.preview ? "" : contract.previewReason },
                { "clone", "No ownership-aware atomic clone command is released." },
                { "retire", "No ownership-aware atomic retire command is released." }
            };
            var sourceContracts = contract.sourceKinds.Select(kind =>
            {
                var sourceContract = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "kind", kind }, { "schemaVersion", SourceSchemaVersion(kind) },
                    { "identityField", IdentityField(kind) }
                };
                if (kind == ContentSourceKinds.FurnitureCatalogueSettings)
                {
                    sourceContract.Add("fixedDocumentId", "furniture.catalogue");
                    sourceContract.Add("fixedLogicalPath", "Furniture/catalogue-settings.json");
                }
                return (object)sourceContract;
            }).ToList();

            var descriptor = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "descriptorVersion", 1 }, { "kindId", contract.kindId }, { "displayName", contract.displayName },
                { "sourceKinds", contract.sourceKinds }, { "defaultSourceKind", contract.defaultSourceKind },
                { "sourceKind", sourceKind }, { "sourceContracts", sourceContracts },
                { "referenceSourceKinds", contract.referenceSourceKinds ?? contract.sourceKinds },
                { "schemaVersion", SourceSchemaVersion(sourceKind) }, { "validatorVersion", 1 },
                { "available", sourceAvailable }, { "unavailableReason", sourceAvailable ? "" : "A producer source kind is not registered." },
                { "capabilities", capabilities }, { "unavailableReasons", unavailableReasons },
                { "sections", contract.sections }, { "documentTemplate", contract.documentTemplate },
                { "graph", contract.graph }, { "references", contract.references },
                { "deferredFields", contract.deferredFields },
                { "validationScope", "ContentAuthoringValidator.Validate; producer/domain validators only" }
            };
            if (contract.kindId == "action") descriptor.Add("actionCatalog", DescribeActivityCatalog());
            if (contract.kindId == "furniture") descriptor.Add("furnitureCatalog", DescribeFurnitureCatalog());
            if (contract.kindId == "action") descriptor.Add("hostEffectCatalog", DescribeHostEffects());
            return WorkbenchResult.Ok(new Dictionary<string, object> { { "descriptor", descriptor } });
        }

        public static WorkbenchResult Validate(IDictionary<string, object> payload)
        {
            string kindId = JsonWire.String(payload, "kindId");
            if (string.IsNullOrEmpty(kindId))
                return WorkbenchResult.Failure("invalid", "kind_id_required", "/kindId", "New validation requests require kindId.");
            KindContract contract = GetContract(kindId);
            if (contract == null)
                return WorkbenchResult.Failure("invalid", "kind_id_invalid", "/kindId", "The Workbench kind is not registered.");

            string legacyKind = JsonWire.String(payload, "kind");
            if (legacyKind.Length > 0 && legacyKind != kindId)
                return WorkbenchResult.Failure("invalid", "kind_alias_conflict", "/kind", "kind and kindId must identify the same Workbench kind.");
            string sourceKind = JsonWire.String(payload, "sourceKind", contract.defaultSourceKind);
            if (!contract.sourceKinds.Contains(sourceKind, StringComparer.Ordinal))
                return WorkbenchResult.Failure("invalid", "source_kind_invalid", "/sourceKind", "sourceKind is not registered for this Workbench kind.");
            object rawDocument;
            if (!payload.TryGetValue("document", out rawDocument) || JsonWire.AsObject(rawDocument) == null)
                return WorkbenchResult.Failure("invalid", "content_document_invalid", "/document", "document must be a JSON object.");

            byte[] bytes;
            try { bytes = JsonWire.SerializeBytes(rawDocument); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "content_document_invalid", "/document", "Document JSON could not be serialized: " + exception.GetType().Name); }
            ContentAuthoringValidationResult validation = ContentAuthoringValidator.Validate(sourceKind, bytes);
            var diagnostics = new List<WorkbenchDiagnostic>();
            if (validation.diagnostics != null)
                foreach (ContentDiagnostic item in validation.diagnostics)
                    diagnostics.Add(new WorkbenchDiagnostic(item.code, item.path, item.message));

            if (validation.isValid && sourceKind == ContentSourceKinds.GenericDialogue)
            {
                try
                {
                    DialogueDefinition definition = (DialogueDefinition)JsonWire.ConvertTo(rawDocument, typeof(DialogueDefinition));
                    foreach (string error in DialogueValidator.Validate(definition))
                        diagnostics.Add(new WorkbenchDiagnostic("dialogue_validation_error", "/document", error));
                }
                catch (Exception exception)
                { diagnostics.Add(new WorkbenchDiagnostic("dialogue_document_invalid", "/document", exception.GetType().Name)); }
            }
            else if (validation.isValid && sourceKind == ContentSourceKinds.TopicDefinition)
            {
                try
                {
                    TopicDefinition definition = (TopicDefinition)JsonWire.ConvertTo(rawDocument, typeof(TopicDefinition));
                    foreach (string error in TopicDefinitionValidator.Validate(definition))
                        diagnostics.Add(new WorkbenchDiagnostic("topic_validation_error", "/document", error));
                }
                catch (Exception exception)
                { diagnostics.Add(new WorkbenchDiagnostic("topic_document_invalid", "/document", exception.GetType().Name)); }
            }
            else if (validation.isValid && sourceKind == ContentSourceKinds.ScenarioDefinition)
            {
                try
                {
                    ScenarioDefinition definition = (ScenarioDefinition)JsonWire.ConvertTo(rawDocument, typeof(ScenarioDefinition));
                    foreach (string error in ScenarioDefinitionValidator.Validate(definition, null))
                        diagnostics.Add(new WorkbenchDiagnostic("scenario_validation_error", "/document", error));
                }
                catch (Exception exception)
                { diagnostics.Add(new WorkbenchDiagnostic("scenario_document_invalid", "/document", exception.GetType().Name)); }
            }

            bool valid = diagnostics.Count == 0;
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "kindId", kindId }, { "sourceKind", sourceKind }, { "validator", "ContentAuthoringValidator" },
                { "validatorVersion", 1 }, { "validationScope", "source" }, { "valid", valid },
                { "readyForPublishedRelease", false }
            };
            if (kindId == "topic")
                result.Add("readinessNote", "The producer source supports scenarioRef, but this Workbench cannot yet materialize the exact canonical Flow ContentRef from the selected source package. Legacy embedded nodes remain readable and are not upgraded here.");
            return WorkbenchResult.WithDiagnostics(valid ? "ok" : "invalid", result, diagnostics);
        }

        public static bool TryResolvePreviewKind(IDictionary<string, object> payload, out string kind,
            out WorkbenchResult failure)
        {
            kind = JsonWire.String(payload, "kind");
            string kindId = JsonWire.String(payload, "kindId");
            failure = null;
            if (kindId.Length == 0) return true;
            KindContract contract = GetContract(kindId);
            if (contract == null)
            {
                failure = WorkbenchResult.Failure("invalid", "kind_id_invalid", "/kindId", "kindId is not registered.");
                return false;
            }
            if (kind.Length > 0 && kind != kindId)
            {
                failure = WorkbenchResult.Failure("invalid", "kind_alias_conflict", "/kind", "kind and kindId must identify the same Workbench kind.");
                return false;
            }
            if (!contract.preview)
            {
                failure = WorkbenchResult.Failure("unavailable", "preview_kind_unavailable", "/kindId", contract.previewReason);
                return false;
            }
            kind = kindId;
            return true;
        }

        private static KindContract GetContract(string kindId)
        {
            switch (kindId)
            {
                case "dialogue": return DialogueContract(false);
                case "scenario": return ScenarioContract();
                case "topic": return TopicContract();
                case "furniture": return FurnitureContract();
                case "item": return ItemContract();
                case "action": return ActionContract();
                default: return null;
            }
        }

        private static KindContract DialogueContract(bool legacy)
        {
            var kinds = new[] { ContentSourceKinds.DialogueDefinition, ContentSourceKinds.GenericDialogue };
            if (legacy)
                return new KindContract
                {
                    kindId = "dialogue", displayName = "Dialogue", defaultSourceKind = ContentSourceKinds.GenericDialogue,
                    identityField = "id", schemaVersion = DialogueDefinition.CurrentSchemaVersion,
                    sourceKinds = kinds.ToList(), preview = false,
                    previewReason = "Dialogue preview is awaiting the shared Scenario/Dialogue runtime preview release.",
                    sections = new List<object>
                    {
                        Section("identity", "Dialogue identity", F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1), F("id", "id", "Dialogue ID", "text", required:true), F("source", "source", "Source", "enum", options:new[] { "generic", "personalized" })),
                        Section("routing", "Legacy route", F("routing", "routing", "Routing", "object", children: Fields(
                            F("routeId", "routeId", "Route ID", "text"), F("priority", "priority", "Priority", "number"),
                            F("startHour", "startHour", "Start hour", "integer", min:-1, max:23), F("endHour", "endHour", "End hour", "integer", min:-1, max:23),
                            F("examples", "examples", "Semantic examples", "array", itemControl:"textarea"), F("requiredContext", "requiredContext", "Required context", "array", itemControl:"text"),
                            F("blockedContext", "blockedContext", "Blocked context", "array", itemControl:"text"), F("eligibility", "eligibility", "Eligibility (advanced JSON)", "json", description:"Advanced legacy-route rule data. The source validator remains authoritative; no visual eligibility builder is exposed.")))),
                        Section("dialogue", "Dialogue nodes", F("nodes", "nodes", "Nodes", "array", children: Fields(
                            F("id", "id", "Node ID", "text", required:true), F("speaker", "speaker", "Speaker", "text"), F("text", "text", "Speech text", "textarea"),
                            F("displayText", "displayText", "Display text", "textarea"), F("language", "language", "Language", "text"), F("voice", "voice", "Voice", "text"),
                            F("audioKey", "audioKey", "Audio key", "text"), F("nextId", "nextId", "Next node ID", "text"), F("presentation", "presentation", "Presentation (advanced JSON)", "json", description:"Optional producer-defined presentation metadata."),
                            F("choices", "choices", "Choices", "array", children: Fields(F("label", "label", "Choice label", "text"), F("nextId", "nextId", "Next node ID", "text"))))))
                    },
                    documentTemplate = new Dictionary<string, object>
                    {
                        { "schemaVersion", 1 }, { "id", "" }, { "source", "generic" },
                        { "routing", new Dictionary<string, object> { { "routeId", "" }, { "priority", 1 }, { "startHour", -1 }, { "endHour", -1 }, { "examples", new List<object>() }, { "requiredContext", new List<object>() }, { "blockedContext", new List<object>() }, { "eligibility", null } } },
                        { "nodes", new List<object> { new Dictionary<string, object> { { "id", "node.opening" }, { "speaker", "alterra" }, { "text", "" }, { "displayText", "" }, { "language", "en" }, { "voice", "" }, { "audioKey", "" }, { "nextId", "" }, { "presentation", new Dictionary<string, object>() }, { "choices", new List<object>() } } } }
                    },
                    graph = Graph("nodes", "id", new[] { "nextId" }, false, "Legacy Generic graph; edits retain the original source kind and fields.")
                };

            return new KindContract
            {
                kindId = "dialogue", displayName = "Dialogue", defaultSourceKind = ContentSourceKinds.DialogueDefinition,
                identityField = "id", schemaVersion = 1, sourceKinds = kinds.ToList(), preview = true,
                previewReason = "",
                sections = new List<object>
                {
                    Section("identity", "Dialogue identity", F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1), F("id", "id", "Dialogue ID", "text", required:true)),
                    Section("beats", "Beats and localized text", F("beats", "beats", "Ordered beats", "array", min:1, max:64, children: Fields(
                        F("beatId", "beatId", "Beat ID", "text", required:true), F("text", "", "Localized Text", "localizedText", ownedPath:"text", referencePath:"textRef", referenceKind:"text"),
                        F("expression", "expression", "Expression", "text"), F("presentation", "presentation", "Presentation (advanced JSON)", "json", description:"Optional producer-defined presentation metadata."))))
                },
                documentTemplate = D("schemaVersion", 1, "id", "", "beats", A(
                    D("beatId", "beat.opening", "text", D("id", "text.opening", "translations", A(
                        D("language", "en", "text", ""), D("language", "ja", "text", ""))),
                        "expression", "", "presentation", D()))),
                graph = Graph("beats", "beatId", new string[0], false, "Dialogue beats are ordered records, not executable Scenario steps."),
                references = new List<object> { Reference("text", new[] { "textRef" }, "Exact pinned Text revision; owned localized Text is preferred for new content.") }
            };
        }

        private static KindContract ScenarioContract()
        {
            return new KindContract
            {
                kindId = "scenario", displayName = "Scenario", defaultSourceKind = ContentSourceKinds.ScenarioDefinition,
                identityField = "scenarioId", schemaVersion = null, sourceKinds = new List<string> { ContentSourceKinds.ScenarioDefinition },
                preview = true, previewReason = "",
                sections = new List<object>
                {
                    Section("identity", "Scenario identity", F("scenarioId", "scenarioId", "Scenario ID", "text", required:true),
                        F("topicId", "topicId", "Topic ID", "text"), F("entryStepId", "entryStepId", "Entry step", "text", required:true)),
                    Section("steps", "Flow steps", F("steps", "steps", "Ordered graph steps", "array", min:1, max:64, children: ScenarioStepFields()))
                },
                documentTemplate = D("scenarioId", "", "topicId", "", "entryStepId", "step.end", "steps", A(D("id", "step.end", "kind", "end", "outcome", "success"))),
                graph = Graph("steps", "id", new[] { "nextStepId", "freeTextNextStepId", "skipNextStepId", "cancelNextStepId", "successNextStepId", "failureNextStepId", "defaultNextStepId" }, false, "The selected-step inspector edits the producer's ScenarioDefinition shape."),
                references = ScenarioReferences()
            };
        }

        private static KindContract TopicContract()
        {
            return new KindContract
            {
                kindId = "topic", displayName = "Topic", defaultSourceKind = ContentSourceKinds.TopicDefinition,
                identityField = "topicId", schemaVersion = 1, sourceKinds = new List<string> { ContentSourceKinds.TopicDefinition },
                preview = true, previewReason = "",
                sections = new List<object>
                {
                    Section("identity", "Topic identity and anchors", F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1),
                        F("topicId", "topicId", "Topic ID", "text", required:true), F("menuLabel", "menuLabel", "Menu label", "textarea"),
                        F("prompt", "prompt", "Prompt", "textarea"), F("anchors", "anchors", "Universal anchors", "array", itemControl:"textarea"),
                        F("defaultReplayPolicy", "defaultReplayPolicy", "Replay policy", "enum", options:new[] { TopicReplayPolicy.Repeatable, TopicReplayPolicy.Once }),
                        F("cooldownSeconds", "cooldownSeconds", "Cooldown seconds", "number", min:0, max:31536000)),
                    Section("scenarios", "Scenario entries", F("scenarios", "scenarios", "Topic scenarios", "array", min:0, max:256,
                        description:"New entries pin an importer-resolved Scenario Flow. Existing inline nodes remain intact; convert them only by creating a separate reusable Scenario and explicitly replacing the entry source.",
                        children:TopicScenarioFields()))
                },
                documentTemplate = new Dictionary<string, object> { { "schemaVersion", 1 }, { "topicId", "" }, { "menuLabel", "" }, { "prompt", "" }, { "anchors", new List<object>() }, { "defaultReplayPolicy", TopicReplayPolicy.Repeatable }, { "cooldownSeconds", 0 }, { "scenarios", new List<object>() } },
                graph = Graph("scenarios", "scenarioId", new string[0], true, "Legacy inline nodes remain preserved and read-only. New entries can pin an exact importer-resolved Scenario Flow."),
                references = new List<object>(),
                deferredFields = new List<object>()
            };
        }

        private static List<Dictionary<string, object>> TopicScenarioFields()
        {
            return Fields(
                F("scenarioId", "scenarioId", "Topic entry ID", "text", required:true),
                F("priority", "priority", "Priority", "integer", defaultValue:0),
                F("baseWeight", "baseWeight", "Base weight", "number", defaultValue:1d, min:0, max:1000000),
                F("condition", "condition", "Eligibility condition (advanced JSON)", "json", defaultValue:new Dictionary<string, object>(),
                    description:"Canonical Topic eligibility expression. It is producer-validated; this Workbench does not yet provide a visual rule builder."),
                F("channels", "channels", "Channels", "array", itemControl:"text", defaultValue:new List<object>()),
                F("modifiers", "modifiers", "Weight modifiers (advanced JSON)", "json", defaultValue:new List<object>(),
                    description:"Canonical weight modifier array. It remains explicit JSON until a typed modifier editor is released."),
                F("consumptionNodeId", "consumptionNodeId", "Consumption checkpoint", "text", defaultValue:""),
                F("scenarioRef", "scenarioRef", "Reusable Scenario Flow", "reference", targetKind:"scenario",
                    defaultValue:new Dictionary<string, object> { { "store", "" }, { "id", "" }, { "revision", "" }, { "expectedHash", "" } },
                    description:"Select an exact reference resolved from an explicitly selected, pinned authoring snapshot."),
                F("nodes", "nodes", "Legacy inline nodes (preserved)", "readOnlyArray", readOnly:true,
                    description:"Existing inline nodes are preserved read-only. They are not automatically removed or converted."));
        }

        private static KindContract ItemContract()
        {
            return new KindContract
            {
                kindId = "item", displayName = "Item", defaultSourceKind = ContentSourceKinds.ItemDefinition,
                identityField = "id", schemaVersion = 1, sourceKinds = new List<string> { ContentSourceKinds.ItemDefinition },
                preview = true, previewReason = "",
                sections = new List<object>
                {
                    Section("identity", "Item definition", F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1),
                        F("id", "id", "Item ID", "text", required:true), F("displayName", "", "Display Text", "localizedText", ownedPath:"displayName", referencePath:"displayNameRef", referenceKind:"text"),
                        F("stackLimit", "stackLimit", "Stack limit", "int64", required:true, min:1, description:"Definition limit only; player quantity is game-owned and is never editable here."))
                },
                documentTemplate = D("schemaVersion", 1, "id", "", "displayName", D("id", "text.display_name", "translations", A(
                    D("language", "en", "text", ""), D("language", "ja", "text", ""))), "stackLimit", 99),
                graph = Graph("", "", new string[0], true, "Items are definitions, not inventory stacks."),
                references = new List<object> { Reference("text", new[] { "displayNameRef" }, "Exact pinned Text revision; owned localized Text is preferred for new Items.") }
            };
        }

        private static KindContract FurnitureContract()
        {
            return new KindContract
            {
                kindId = "furniture", displayName = "Furniture", defaultSourceKind = ContentSourceKinds.FurnitureDefinition,
                identityField = "id", schemaVersion = 1, sourceKinds = new List<string> { ContentSourceKinds.FurnitureDefinition, ContentSourceKinds.FurnitureCatalogueSettings },
                referenceSourceKinds = new List<string> { ContentSourceKinds.FurnitureDefinition, ContentSourceKinds.FurnitureCatalogueSettings },
                preview = true, previewReason = "",
                sections = new List<object>
                {
                    Section("identity", "Furniture definition", F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1),
                        F("id", "id", "Furniture ID", "text", required:true), F("displayName", "", "Display Text", "localizedText", ownedPath:"displayName", referencePath:"displayNameRef", referenceKind:"text", mirrorPath:"definition.displayNameKey"),
                        F("definition", "definition", "Definition and use-points", "object", children: FurnitureDefinitionFields())),
                    Section("bindings", "Registered operation bindings", F("functions", "functions", "Persistent function contracts", "array", min:0, max:64, children: FurnitureFunctionFields(), syncPath:"definition.functionBindings"),
                        F("definition.presentationBindings", "definition.presentationBindings", "Presentation bindings", "array", children: FurniturePresentationFields(), options:new[] { FurniturePresentationDescriptorIds.DispenserCollectMock }))
                },
                documentTemplate = FurnitureTemplate(),
                graph = Graph("definition.points", "pointId", new string[0], true, "Functional points are catalogue data, not live scene instances."),
                references = new List<object> { Reference("text", new[] { "displayNameRef" }, "Exact pinned Text revision; owned localized Text is preferred for new Furniture.") }
            };
        }

        private static KindContract FurnitureSettingsContract()
        {
            return new KindContract
            {
                kindId = "furniture", displayName = "Furniture", defaultSourceKind = ContentSourceKinds.FurnitureDefinition,
                identityField = "catalogueVersion", schemaVersion = 1,
                sourceKinds = new List<string> { ContentSourceKinds.FurnitureDefinition, ContentSourceKinds.FurnitureCatalogueSettings },
                referenceSourceKinds = new List<string> { ContentSourceKinds.FurnitureDefinition, ContentSourceKinds.FurnitureCatalogueSettings },
                preview = false,
                previewReason = "Catalogue settings are validated and saved as a singleton. Preview a Furniture definition with settings pinned as a source.",
                sections = new List<object>
                {
                    Section("catalogue", "Furniture catalogue conventions",
                        F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1),
                        F("catalogueVersion", "catalogueVersion", "Catalogue version", "text", required:true),
                        F("lengthUnit", "lengthUnit", "Length unit", "enum", required:true, options:new[] { "m" }),
                        F("angleUnit", "angleUnit", "Angle unit", "enum", required:true, options:new[] { "deg" }),
                        F("coordinateConvention", "coordinateConvention", "Coordinate convention", "enum", required:true,
                            options:new[] { "unity_local_y_up_positive_z_forward" })),
                    Section("relationships", "Relationship rules",
                        F("relationshipRules", "relationshipRules", "Rules", "array", min:0, max:64,
                            description:"Rules are checked against the point types in the selected Furniture catalogue at release validation.",
                            children: Fields(
                                F("relationshipRuleId", "relationshipRuleId", "Rule ID", "text", required:true),
                                F("kind", "kind", "Relationship kind", "text", required:true),
                                F("pointTypeIds", "pointTypeIds", "Point type IDs", "array", itemControl:"text", min:1),
                                F("broadPhaseRadiusMeters", "broadPhaseRadiusMeters", "Broad-phase radius (m)", "number", required:true, min:0),
                                F("minimumDistanceMeters", "minimumDistanceMeters", "Minimum distance (m)", "number", required:true, min:0),
                                F("maximumDistanceMeters", "maximumDistanceMeters", "Maximum distance (m)", "number", required:true, min:0),
                                F("maximumFacingErrorDegrees", "maximumFacingErrorDegrees", "Maximum facing error (degrees)", "number", required:true, min:0, max:180),
                                F("maximumHeightDifferenceMeters", "maximumHeightDifferenceMeters", "Maximum height difference (m)", "number", required:true, min:0),
                                F("lateralAllowanceMeters", "lateralAllowanceMeters", "Lateral allowance (m)", "number", required:true, min:0)))),
                    Section("capabilities", "Capability descriptors",
                        F("capabilities", "capabilities", "Capabilities", "array", min:0, max:64,
                            children: Fields(
                                F("capabilityId", "capabilityId", "Capability ID", "text", required:true),
                                F("requiredPointTypeIds", "requiredPointTypeIds", "Required point types", "array", itemControl:"text"),
                                F("requiredUseIds", "requiredUseIds", "Required use IDs", "array", itemControl:"text")))),
                    Section("aliases", "Legacy aliases",
                        F("legacyAliases", "legacyAliases", "Aliases", "array", min:0, max:256,
                            children: Fields(
                                F("legacyId", "legacyId", "Legacy ID", "text", required:true),
                                F("definitionId", "definitionId", "Furniture definition ID", "text", required:true),
                                F("pointId", "pointId", "Optional point ID", "text"))))
                },
                documentTemplate = D("schemaVersion", 1, "catalogueVersion", "", "lengthUnit", "m", "angleUnit", "deg",
                    "coordinateConvention", "unity_local_y_up_positive_z_forward", "relationshipRules", A(), "capabilities", A(), "legacyAliases", A()),
                graph = new Dictionary<string, object> { { "enabled", false }, { "nodesPath", "" }, { "idField", "" },
                    { "edgeFields", new string[0] }, { "readOnly", true }, { "note", "Catalogue settings are structured data, not a graph." } },
                references = new List<object>()
            };
        }

        private static KindContract ActionContract()
        {
            return new KindContract
            {
                kindId = "action", displayName = "Action", defaultSourceKind = ContentSourceKinds.ActionDefinition,
                identityField = "id", schemaVersion = 1, sourceKinds = new List<string> { ContentSourceKinds.ActionDefinition },
                preview = true, previewReason = "",
                sections = new List<object>
                {
                    Section("identity", "Action identity", F("schemaVersion", "schemaVersion", "Schema version", "integer", readOnly:true, defaultValue:1),
                        F("id", "id", "Action ID", "text", required:true),
                        F("implementationKind", "implementationKind", "Implementation", "enum", required:true, options:new[] { "activity_sequence", "host_effect" }),
                        F("capabilityId", "capabilityId", "Registered capability", "registeredCapability", required:true),
                        F("data", "data", "Activity sequence data", "actionData", required:true,
                            visibleWhenPath:"implementationKind", visibleWhenValue:"activity_sequence"),
                        F("functionId", "data.functionId", "Host-effect function", "enum", required:true,
                            options:ContentHostEffectFunctionRegistry.RegisteredCapabilityIds.ToArray(),
                            visibleWhenPath:"implementationKind", visibleWhenValue:"host_effect"),
                        F("itemRef", "data.arguments.itemRef", "Granted Item", "reference", targetKind:"item",
                            description:"Exact Item reference for player.item.grant.", visibleWhenPath:"data.functionId", visibleWhenValue:ContentHostEffectFunctionRegistry.PlayerItemGrant),
                        F("quantity", "data.arguments.quantity", "Grant quantity", "int64", required:true, min:1,
                            visibleWhenPath:"data.functionId", visibleWhenValue:ContentHostEffectFunctionRegistry.PlayerItemGrant),
                        F("desiredState", "data.arguments.desiredState", "Desired power state", "enum", required:true, options:new[] { "Off", "On" },
                            visibleWhenPath:"data.functionId", visibleWhenValue:ContentHostEffectFunctionRegistry.FurniturePowerSet))
                },
                documentTemplate = D("schemaVersion", 1, "id", "", "capabilityId", ActivitySequenceCapabilityIds.Complete,
                    "implementationKind", "activity_sequence", "data", D("steps", A(D("stepId", "step.complete",
                        "primitiveId", ActivityPrimitiveIds.Complete, "arguments", A(), "resultBindings", A(), "successNextStepId", "",
                        "failureNextStepId", "", "cancelNextStepId", "", "checkpointPolicy", "none")))),
                graph = Graph("data.steps", "stepId", new[] { "successNextStepId", "failureNextStepId", "cancelNextStepId" }, false, "The graph vocabulary is read from ActivitySequenceCatalogFactory."),
                references = new List<object>
                {
                    Reference("dialogue", new[] { "data.steps[].arguments[].value" }, "Bark dialogue IDs are release-local typed literals.", "dialogueId"),
                    Reference("item", new[] { "data.arguments.itemRef" }, "Exact pinned Item reference for the registered player.item.grant host effect.")
                }
            };
        }

        private static List<Dictionary<string, object>> ScenarioStepFields()
        {
            return new List<Dictionary<string, object>>
            {
                F("id", "id", "Step ID", "text", required:true), F("kind", "kind", "Step kind", "enum", required:true,
                    options:new[] { "dialogue", "ask", "action", "host_effect", "wait", "branch", "end" }),
                F("beats", "beats", "Dialogue beats", "array", children: Fields(F("id", "id", "Beat ID", "text"), F("speaker", "speaker", "Speaker", "text"), F("text", "text", "Text", "textarea"), F("displayText", "displayText", "Display text", "textarea"), F("language", "language", "Language", "text"), F("expression", "expression", "Expression", "text"))),
                F("dialogueRef", "dialogueRef", "Pinned Dialogue", "reference", targetKind:"dialogue"),
                F("actionRef", "actionRef", "Pinned Action", "reference", targetKind:"action"),
                F("nextStepId", "nextStepId", "Next step", "text"), F("closeExchange", "closeExchange", "Close exchange", "boolean"),
                F("questionId", "questionId", "Question ID", "text"), F("question", "question", "Question", "object", children: Fields(F("id", "id", "Beat ID", "text"), F("text", "text", "Text", "textarea"), F("displayText", "displayText", "Display text", "textarea"), F("language", "language", "Language", "text"))),
                F("options", "options", "Question options", "array", max:8, children: Fields(F("optionId", "optionId", "Option ID", "text"), F("label", "label", "Label", "text"), F("value", "value", "Value", "text"), F("nextStepId", "nextStepId", "Next step", "text"))),
                F("allowFreeText", "allowFreeText", "Allow free text", "boolean"), F("freeTextNextStepId", "freeTextNextStepId", "Free-text next step", "text"),
                F("skipNextStepId", "skipNextStepId", "Skip next step", "text"), F("cancelNextStepId", "cancelNextStepId", "Cancel next step", "text"),
                F("capabilityId", "capabilityId", "Capability ID", "text"), F("arguments", "arguments", "Action arguments", "array", children: Fields(F("name", "name", "Argument name", "text"), F("value", "value", "Value", "text"))),
                F("successNextStepId", "successNextStepId", "Success next step", "text"), F("failureNextStepId", "failureNextStepId", "Failure next step", "text"),
                F("conditionId", "conditionId", "Condition ID", "text"), F("objective", "objective", "Objective", "text"), F("sourceId", "sourceId", "Source ID", "text"),
                F("cases", "cases", "Branch cases", "array", children: Fields(F("value", "value", "Value", "text"), F("nextStepId", "nextStepId", "Next step", "text"))),
                F("defaultNextStepId", "defaultNextStepId", "Default next step", "text"), F("outcome", "outcome", "Outcome", "text"),
                F("data", "data", "Effect or step data (advanced JSON)", "json",
                    description:"Producer-defined operation data. The typed Action sequence editor covers registered ActivitySequence primitives only.")
            };
        }

        private static List<Dictionary<string, object>> FurnitureDefinitionFields()
        {
            return Fields(F("definitionId", "definitionId", "Definition ID", "text", required:true, mirrorPath:"id"),
                F("displayNameKey", "displayNameKey", "Display Text ID", "text", readOnly:true),
                F("category", "category", "Category", "text", required:true), F("scalePolicy", "scalePolicy", "Scale policy", "enum", options:new[] { "fixed" }),
                F("defaultScale", "defaultScale", "Default scale", "object", children: Fields(F("x", "x", "X", "number"), F("y", "y", "Y", "number"), F("z", "z", "Z", "number"))),
                F("localBounds", "localBounds", "Local bounds", "object", children: Fields(
                    F("minimum", "minimum", "Minimum", "object", children: VectorFields()), F("maximum", "maximum", "Maximum", "object", children: VectorFields()))),
                F("supportKinds", "supportKinds", "Support kinds", "array", itemControl:"text"), F("blocksNavigation", "blocksNavigation", "Blocks navigation", "boolean"),
                F("points", "points", "Functional points", "array", max:64, children: Fields(
                    F("pointId", "pointId", "Point ID", "text", required:true), F("pointTypeId", "pointTypeId", "Point type", "text", required:true),
                    F("localPosition", "localPosition", "Local position", "object", children: VectorFields()), F("localFacing", "localFacing", "Local facing", "object", children: VectorFields()),
                    F("useIds", "useIds", "Use IDs", "array", itemControl:"text"))),
                F("functionBindings", "functionBindings", "Function bindings", "array", readOnly:true, itemFields: Fields(
                    F("functionId", "functionId", "Function ID", "text"), F("pointId", "pointId", "Point ID", "text"), F("useId", "useId", "Use ID", "text"))));
        }

        private static List<Dictionary<string, object>> FurnitureFunctionFields()
        {
            return Fields(F("functionId", "functionId", "Function ID", "registeredFunction", required:true), F("pointId", "pointId", "Point ID", "text", required:true),
                F("useId", "useId", "Use ID", "text", required:true), F("contractVersion", "contractVersion", "Contract version", "integer", readOnly:true, defaultValue:1),
                F("contract", "contract", "Function contract", "registeredFunctionContract"));
        }

        private static List<Dictionary<string, object>> FurniturePresentationFields()
        {
            return Fields(F("descriptorId", "descriptorId", "Presentation descriptor", "registeredPresentation", required:true),
                F("pointId", "pointId", "Point ID", "text", required:true), F("useId", "useId", "Use ID", "text", required:true));
        }

        private static List<Dictionary<string, object>> VectorFields()
        { return Fields(F("x", "x", "X", "number", required:true), F("y", "y", "Y", "number", required:true), F("z", "z", "Z", "number", required:true)); }

        private static List<object> ScenarioReferences()
        {
            return new List<object> { Reference("dialogue", new[] { "steps[].dialogueRef" }, "Pinned independent Dialogue revisions are shared across flows."),
                Reference("action", new[] { "steps[].actionRef" }, "Pinned Action revision selected from the same authoring store.") };
        }

        private static object DescribeActivityCatalog()
        {
            ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
            var primitives = catalog.Primitives.All.OrderBy(item => item.primitiveId, StringComparer.Ordinal).Select(item => (object)new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "primitiveId", item.primitiveId }, { "version", item.version }, { "operationKind", item.operationKind.ToString() },
                { "requiredCapabilityId", item.requiredCapabilityId }, { "arguments", item.arguments.Select(DescribeArgument).ToList() },
                { "results", item.results.Select(DescribeResult).ToList() }
            }).ToList();
            var composers = catalog.Composers.OrderBy(item => item.composerId, StringComparer.Ordinal).Select(item => (object)new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "composerId", item.composerId }, { "version", item.version }, { "arguments", item.arguments.Select(DescribeArgument).ToList() }
            }).ToList();
            var capabilities = catalog.Primitives.All.Select(item => item.requiredCapabilityId)
                .Concat(ContentHostEffectFunctionRegistry.RegisteredCapabilityIds).Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList();
            return new Dictionary<string, object>(StringComparer.Ordinal)
            { { "primitives", primitives }, { "composers", composers }, { "capabilities", capabilities } };
        }

        private static object DescribeArgument(ActivityArgumentDescriptor argument)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "name", argument.name }, { "valueType", argument.valueType }, { "required", argument.required },
                { "minimum", argument.minimum.HasValue ? (object)argument.minimum.Value : null },
                { "maximum", argument.maximum.HasValue ? (object)argument.maximum.Value : null },
                { "allowedValues", argument.allowedValues.ToList() }, { "entityKind", argument.entityKind },
                { "requiredProducerPrimitiveId", argument.requiredProducerPrimitiveId }
            };
        }

        private static object DescribeResult(ActivityResultDescriptor result)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "name", result.name }, { "valueType", result.valueType },
                { "minimum", result.minimum.HasValue ? (object)result.minimum.Value : null },
                { "maximum", result.maximum.HasValue ? (object)result.maximum.Value : null },
                { "allowedValues", result.allowedValues.ToList() }, { "entityKind", result.entityKind }
            };
        }

        private static object DescribeFurnitureCatalog()
        {
            return D("functions", A(D("functionId", FurnitureFunctionIds.PowerSet, "kind", "persistent", "contractVersion", 1,
                    "states", new[] { "Off", "On" })),
                "presentations", A(D("descriptorId", FurniturePresentationDescriptorIds.DispenserCollectMock,
                    "kind", "presentation", "persistentEffect", false)));
        }

        private static object DescribeHostEffects()
        {
            return ContentHostEffectFunctionRegistry.RegisteredCapabilityIds.OrderBy(item => item, StringComparer.Ordinal)
                .Select(item => (object)new Dictionary<string, object> { { "functionId", item }, { "capabilityId", item }, { "registered", true } }).ToList();
        }

        private static object Section(string id, string title, params Dictionary<string, object>[] fields)
        { return new Dictionary<string, object> { { "id", id }, { "title", title }, { "fields", fields.Cast<object>().ToList() } }; }

        private static List<Dictionary<string, object>> Fields(params Dictionary<string, object>[] fields)
        { return fields.ToList(); }

        private static Dictionary<string, object> D(params object[] values)
        {
            if (values == null || values.Length % 2 != 0) throw new ArgumentException("Object values must be key/value pairs.", "values");
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int index = 0; index < values.Length; index += 2)
                result.Add(Convert.ToString(values[index], System.Globalization.CultureInfo.InvariantCulture), values[index + 1]);
            return result;
        }

        private static List<object> A(params object[] values)
        { return (values ?? new object[0]).ToList(); }

        private static Dictionary<string, object> F(string id, string path, string label, string control,
            bool required = false, bool readOnly = false, object defaultValue = null, string description = "",
            string targetKind = "", string ownedPath = "", string referencePath = "", string referenceKind = "",
            string itemControl = "", List<Dictionary<string, object>> children = null,
            double? min = null, double? max = null, string[] options = null, string mirrorPath = "",
            string syncPath = "", List<Dictionary<string, object>> itemFields = null,
            string visibleWhenPath = "", string visibleWhenValue = "")
        {
            var field = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "fieldId", id }, { "path", path }, { "label", label }, { "control", control },
                { "required", required }, { "readOnly", readOnly }, { "description", description }
            };
            if (defaultValue != null) field.Add("defaultValue", defaultValue);
            if (targetKind.Length > 0) field.Add("targetKind", targetKind);
            if (ownedPath.Length > 0) field.Add("ownedPath", ownedPath);
            if (referencePath.Length > 0) field.Add("referencePath", referencePath);
            if (referenceKind.Length > 0) field.Add("referenceKind", referenceKind);
            if (itemControl.Length > 0) field.Add("itemControl", itemControl);
            if (children != null) field.Add("fields", children.Cast<object>().ToList());
            if (itemFields != null) field.Add("itemFields", itemFields.Cast<object>().ToList());
            if (min.HasValue) field.Add("minimum", min.Value);
            if (max.HasValue) field.Add("maximum", max.Value);
            if (options != null) field.Add("options", options);
            if (mirrorPath.Length > 0) field.Add("mirrorPath", mirrorPath);
            if (syncPath.Length > 0) field.Add("syncPath", syncPath);
            if (visibleWhenPath.Length > 0) field.Add("visibleWhenPath", visibleWhenPath);
            if (visibleWhenValue.Length > 0) field.Add("visibleWhenValue", visibleWhenValue);
            return field;
        }

        private static object Graph(string nodesPath, string idField, string[] edgeFields, bool readOnly, string note)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            { { "enabled", true }, { "nodesPath", nodesPath }, { "idField", idField }, { "edgeFields", edgeFields }, { "readOnly", readOnly }, { "note", note } };
        }

        private static object Reference(string targetKind, string[] paths, string note, string argumentName = "")
        {
            var reference = new Dictionary<string, object>(StringComparer.Ordinal)
            { { "targetKind", targetKind }, { "paths", paths }, { "policy", "pinned-revision" }, { "note", note } };
            if (!string.IsNullOrEmpty(argumentName)) reference.Add("argumentName", argumentName);
            return reference;
        }

        private static object Deferred(string fieldId, string targetKind, string reason)
        { return new Dictionary<string, object>(StringComparer.Ordinal) { { "fieldId", fieldId }, { "targetKind", targetKind }, { "available", false }, { "reason", reason } }; }

        private static object FurnitureTemplate()
        {
            object function = D("functionId", FurnitureFunctionIds.PowerSet, "pointId", "point.use", "useId", "use.power",
                "contractVersion", 1, "contract", D("desiredState", new[] { "Off", "On" }));
            return D("schemaVersion", 1, "id", "",
                "displayName", D("id", "text.display_name", "translations", A(D("language", "en", "text", ""), D("language", "ja", "text", ""))),
                "definition", D("definitionId", "", "displayNameKey", "text.display_name", "category", "", "scalePolicy", "fixed",
                    "defaultScale", D("x", 1, "y", 1, "z", 1),
                    "localBounds", D("minimum", D("x", -0.5, "y", 0, "z", -0.5), "maximum", D("x", 0.5, "y", 1, "z", 0.5)),
                    "supportKinds", A(), "blocksNavigation", true,
                    "points", A(D("pointId", "point.use", "pointTypeId", "use", "localPosition", D("x", 0, "y", 0, "z", 0),
                        "localFacing", D("x", 0, "y", 0, "z", 1), "useIds", new[] { "use.power" })),
                    "functionBindings", A(D("functionId", FurnitureFunctionIds.PowerSet, "pointId", "point.use", "useId", "use.power")),
                    "presentationBindings", A()),
                "functions", A(function));
        }

        private static int? SourceSchemaVersion(string sourceKind)
        {
            if (sourceKind == ContentSourceKinds.DialogueDefinition || sourceKind == ContentSourceKinds.ItemDefinition ||
                sourceKind == ContentSourceKinds.FurnitureDefinition || sourceKind == ContentSourceKinds.FurnitureCatalogueSettings || sourceKind == ContentSourceKinds.ActionDefinition ||
                sourceKind == ContentSourceKinds.TopicDefinition || sourceKind == ContentSourceKinds.GenericDialogue) return 1;
            return null;
        }

        private static string IdentityField(string sourceKind)
        {
            if (sourceKind == ContentSourceKinds.GenericDialogue || sourceKind == ContentSourceKinds.DialogueDefinition ||
                sourceKind == ContentSourceKinds.ItemDefinition || sourceKind == ContentSourceKinds.FurnitureDefinition || sourceKind == ContentSourceKinds.ActionDefinition) return "id";
            if (sourceKind == ContentSourceKinds.FurnitureCatalogueSettings) return "catalogueVersion";
            if (sourceKind == ContentSourceKinds.TopicDefinition) return "topicId";
            if (sourceKind == ContentSourceKinds.ScenarioDefinition) return "scenarioId";
            return string.Empty;
        }
    }
}
