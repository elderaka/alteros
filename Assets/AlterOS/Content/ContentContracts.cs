using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AlterOS.Activities;
using AlterOS.Furniture;

namespace AlterOS.Content
{
    /// <summary>Installed host-effect function-to-capability bindings; authored Actions cannot create entries.</summary>
    public static class ContentHostEffectFunctionRegistry
    {
        public const string PlayerItemGrant = "player.item.grant";
        public const string FurniturePowerSet = "power.set";

        public static IReadOnlyCollection<string> RegisteredCapabilityIds
        { get { return Array.AsReadOnly(new[] { PlayerItemGrant, FurniturePowerSet }); } }

        public static bool TryGetRequiredCapability(string functionId, out string capabilityId)
        {
            if (functionId == PlayerItemGrant) { capabilityId = PlayerItemGrant; return true; }
            if (functionId == FurniturePowerSet) { capabilityId = FurniturePowerSet; return true; }
            capabilityId = string.Empty;
            return false;
        }
    }

    public enum ContentStoreMode
    {
        ReadOnly,
        ReadWrite
    }

    public static class ContentStatuses
    {
        public const string Ready = "ready";
        public const string Invalid = "invalid";
        public const string Missing = "missing";
        public const string Incompatible = "incompatible";
        public const string Unavailable = "unavailable";
    }

    public sealed class ContentDiagnostic
    {
        public readonly string code;
        public readonly string path;
        public readonly string message;

        public ContentDiagnostic(string diagnosticCode, string diagnosticPath, string diagnosticMessage)
        {
            code = diagnosticCode ?? string.Empty;
            path = diagnosticPath ?? string.Empty;
            message = diagnosticMessage ?? string.Empty;
        }
    }

    public sealed class ContentResult<T>
    {
        public readonly string status;
        public readonly T value;
        public readonly ReadOnlyCollection<ContentDiagnostic> diagnostics;

        public bool IsReady { get { return status == ContentStatuses.Ready; } }

        private ContentResult(string resultStatus, T resultValue, IEnumerable<ContentDiagnostic> resultDiagnostics)
        {
            status = resultStatus;
            value = resultValue;
            diagnostics = Array.AsReadOnly(new List<ContentDiagnostic>(resultDiagnostics ?? new ContentDiagnostic[0]).ToArray());
        }

        public static ContentResult<T> Ready(T value)
        {
            return new ContentResult<T>(ContentStatuses.Ready, value, null);
        }

        public static ContentResult<T> Failure(string status, string code, string path, string message)
        {
            return new ContentResult<T>(status, default(T), new[] { new ContentDiagnostic(code, path, message) });
        }
    }

    public sealed class ContentPackageValidationResult
    {
        public readonly bool isValid;
        public readonly ReadOnlyCollection<ContentDiagnostic> diagnostics;

        public ContentPackageValidationResult(bool valid, IEnumerable<ContentDiagnostic> items)
        {
            isValid = valid;
            diagnostics = Array.AsReadOnly(new List<ContentDiagnostic>(items ?? new ContentDiagnostic[0]).ToArray());
        }
    }

    public sealed class ContentAuthoringValidationResult
    {
        public readonly bool isValid;
        public readonly ReadOnlyCollection<ContentDiagnostic> diagnostics;

        public ContentAuthoringValidationResult(bool valid, IEnumerable<ContentDiagnostic> items)
        {
            isValid = valid;
            diagnostics = Array.AsReadOnly(new List<ContentDiagnostic>(items ?? new ContentDiagnostic[0]).ToArray());
        }
    }

    public static class ContentChoiceStates
    {
        public const string Available = "available";
        public const string Locked = "locked";
        public const string Hidden = "hidden";
    }

    public sealed class ContentRuleEvaluation
    {
        public readonly string status;
        public readonly string truth;
        public readonly string reason;

        public ContentRuleEvaluation(string resultStatus, string resultTruth, string resultReason)
        { status = resultStatus ?? ContentStatuses.Unavailable; truth = resultTruth ?? "Unknown"; reason = resultReason ?? string.Empty; }
    }

    public sealed class ContentChoiceEvaluation
    {
        public readonly string status;
        public readonly string state;
        public readonly string truth;
        public readonly ContentRef lockedHintTextRef;

        public ContentChoiceEvaluation(string resultStatus, string resultState, string resultTruth, ContentRef hint)
        { status = resultStatus ?? ContentStatuses.Unavailable; state = resultState ?? ContentChoiceStates.Hidden; truth = resultTruth ?? "Unknown"; lockedHintTextRef = hint; }
    }

    /// <summary>Exact store/revision reference. ExpectedHash is required by cross-store resolvers.</summary>
    [Serializable]
    public sealed class ContentRef
    {
        public string store = string.Empty;
        public string id = string.Empty;
        public string revision = string.Empty;
        public string expectedHash = string.Empty;

        public ContentRef() { }

        public ContentRef(string storeId, string contentId, string contentRevision, string hash = "")
        {
            store = storeId ?? string.Empty;
            id = contentId ?? string.Empty;
            revision = contentRevision ?? string.Empty;
            expectedHash = hash ?? string.Empty;
        }
    }

    /// <summary>Resolves typed content only through a host-selected, validated release.</summary>
    public interface IContentReleaseReader
    {
        ContentResult<ActionRecord> ReadAction(ContentRef reference);
        ContentResult<ItemRecord> ReadItem(ContentRef reference);
        ContentResult<FlowRecord> ReadFlow(ContentRef reference);
        ContentResult<FlowRecord> ReadFlowById(string flowId);
    }

    /// <summary>Host-owned allowlists. Content can reference these entries but cannot register new ones.</summary>
    public sealed class ContentHostVocabulary
    {
        private readonly HashSet<string> actions;
        private readonly HashSet<string> expressions;
        private readonly HashSet<string> enumValues;
        private readonly HashSet<string> contextFacts;
        private readonly HashSet<string> canonicalOwners;
        public ActivitySequenceBuilder SequenceBuilder { get; private set; }

        public ContentHostVocabulary(IEnumerable<string> registeredActions, IEnumerable<string> registeredExpressions,
            IEnumerable<string> registeredEnumValues, IEnumerable<string> registeredContextFacts,
            IEnumerable<string> registeredCanonicalOwners)
            : this(registeredActions, registeredExpressions, registeredEnumValues, registeredContextFacts,
                registeredCanonicalOwners, null) { }

        public ContentHostVocabulary(IEnumerable<string> registeredActions, IEnumerable<string> registeredExpressions,
            IEnumerable<string> registeredEnumValues, IEnumerable<string> registeredContextFacts,
            IEnumerable<string> registeredCanonicalOwners, ActivitySequenceBuilder sequenceBuilder)
        {
            actions = Copy(registeredActions);
            expressions = Copy(registeredExpressions);
            enumValues = Copy(registeredEnumValues);
            contextFacts = Copy(registeredContextFacts);
            canonicalOwners = Copy(registeredCanonicalOwners);
            SequenceBuilder = sequenceBuilder;
        }

        public bool IsActionRegistered(string id) { return Contains(actions, id); }
        public bool HasSameRegistrationsAs(ContentHostVocabulary other)
        {
            return other != null && ReferenceEquals(SequenceBuilder, other.SequenceBuilder) &&
                actions.SetEquals(other.actions) && expressions.SetEquals(other.expressions) &&
                enumValues.SetEquals(other.enumValues) && contextFacts.SetEquals(other.contextFacts) &&
                canonicalOwners.SetEquals(other.canonicalOwners);
        }
        public bool IsHostEffectFunctionRegistered(string functionId, string capabilityId)
        {
            string requiredCapability;
            return ContentHostEffectFunctionRegistry.TryGetRequiredCapability(functionId, out requiredCapability) &&
                requiredCapability == capabilityId && IsActionRegistered(capabilityId);
        }
        public bool IsExpressionAllowed(string id) { return Contains(expressions, id); }
        public bool IsEnumValueAllowed(string id) { return Contains(enumValues, id); }
        public bool IsContextFactAllowed(string id) { return Contains(contextFacts, id); }
        public bool IsCanonicalOwnerAllowed(string id) { return Contains(canonicalOwners, id); }

        private static HashSet<string> Copy(IEnumerable<string> values)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (values != null)
                foreach (string value in values)
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            return result;
        }

        private static bool Contains(HashSet<string> values, string value)
        {
            return !string.IsNullOrWhiteSpace(value) && values.Contains(value);
        }
    }

    [Serializable]
    public sealed class ContentPackage
    {
        public string packageId = string.Empty;
        public string storeId = string.Empty;
        public string sourceManifestHash = string.Empty;
        public List<TopicRecord> topics = new List<TopicRecord>();
        public List<FlowRecord> flows = new List<FlowRecord>();
        public List<DialogueRecord> dialogues = new List<DialogueRecord>();
        public List<TextRecord> texts = new List<TextRecord>();
        public List<ActionRecord> actions = new List<ActionRecord>();
        public List<ItemRecord> items = new List<ItemRecord>();
        public List<FurnitureDefinitionRecord> furnitureDefinitions = new List<FurnitureDefinitionRecord>();
        public FurnitureCatalogueDocument furnitureCatalogueSettings;
        public List<RuleRecord> rules = new List<RuleRecord>();
        public List<AnchorRecord> anchors = new List<AnchorRecord>();
        public List<AnchorLinkRecord> anchorLinks = new List<AnchorLinkRecord>();
        public List<EmbeddingProfileRecord> embeddingProfiles = new List<EmbeddingProfileRecord>();
        public List<EmbeddingRecord> embeddings = new List<EmbeddingRecord>();
        public List<LegacySourceRecord> legacySources = new List<LegacySourceRecord>();
        public List<ContentRelease> releases = new List<ContentRelease>();
        public ContentBuildManifest buildManifest;
    }

    [Serializable]
    public sealed class ContentRelease
    {
        internal object compilerAttestation;
        public string releaseId = string.Empty;
        public string manifestJson = "{}";
        public string manifestHash = string.Empty;
        public string validatorVersion = string.Empty;
        public string graphFingerprint = string.Empty;
        public string indexFingerprint = string.Empty;
        public List<ContentReleaseDependency> dependencies = new List<ContentReleaseDependency>();
    }

    [Serializable]
    public sealed class ContentReleaseDependency
    {
        public string kind = string.Empty;
        public string id = string.Empty;
        public string revision = string.Empty;
        public string store = string.Empty;
        public string expectedHash = string.Empty;
    }

    [Serializable]
    public sealed class ReleaseHead
    {
        public string headName = string.Empty;
        public string releaseId = string.Empty;
        public long revision;
    }

    public sealed class CompiledReleaseResult
    {
        public readonly string status;
        public readonly ContentPackage package;
        public readonly ContentRelease release;
        public readonly ReadOnlyCollection<ContentDiagnostic> diagnostics;

        public bool IsReady { get { return status == ContentStatuses.Ready; } }

        private CompiledReleaseResult(string resultStatus, ContentPackage compiledPackage,
            ContentRelease compiledRelease, IEnumerable<ContentDiagnostic> resultDiagnostics)
        {
            status = resultStatus ?? ContentStatuses.Unavailable;
            package = compiledPackage;
            release = compiledRelease;
            diagnostics = Array.AsReadOnly(new List<ContentDiagnostic>(resultDiagnostics ?? new ContentDiagnostic[0]).ToArray());
        }

        public static CompiledReleaseResult Ready(ContentPackage package, ContentRelease release)
        { return new CompiledReleaseResult(ContentStatuses.Ready, package, release, null); }

        public static CompiledReleaseResult Failure(string status, string code, string path, string message)
        { return new CompiledReleaseResult(status, null, null, new[] { new ContentDiagnostic(code, path, message) }); }
    }

    [Serializable]
    public sealed class TopicRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public string routeId = string.Empty;
        public ContentRef titleTextRef;
        public ContentRef promptTextRef;
        public string defaultReplayPolicy = "repeatable";
        public double cooldownSeconds;
        public List<TopicEntryRecord> entries = new List<TopicEntryRecord>();
    }

    [Serializable]
    public sealed class TopicEntryRecord
    {
        public string id = string.Empty;
        public ContentRef flowRef;
        public int priority;
        public double baseWeight = 1d;
        public string conditionJson = "{}";
        public string modifiersJson = "[]";
        public string channelsJson = "[]";
        public string consumptionNodeId = string.Empty;
    }

    [Serializable]
    public sealed class LegacySourceRecord
    {
        public string kind = string.Empty;
        public string relativePath = string.Empty;
        public string sourceId = string.Empty;
        public string sourceHash = string.Empty;
        public string payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        public string contentId = string.Empty;
        public string contentRevision = string.Empty;
        public string metadataJson = "{}";
        public byte[] sourceBytes = new byte[0];
    }

    public sealed class ContentTextResolution
    {
        public readonly string status;
        public readonly string displayText;
        public readonly string speechText;
        public readonly string displayLanguage;
        public readonly string speechLanguage;

        public ContentTextResolution(string resultStatus, string resolvedDisplayText, string resolvedSpeechText,
            string resolvedDisplayLanguage, string resolvedSpeechLanguage)
        {
            status = resultStatus ?? ContentStatuses.Unavailable;
            displayText = resolvedDisplayText ?? string.Empty;
            speechText = resolvedSpeechText ?? string.Empty;
            displayLanguage = resolvedDisplayLanguage ?? string.Empty;
            speechLanguage = resolvedSpeechLanguage ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class FlowRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public string kind = string.Empty;
        public string entryStepId = string.Empty;
        public List<StepRecord> steps = new List<StepRecord>();
        public List<TransitionRecord> transitions = new List<TransitionRecord>();
        public List<ChoiceRecord> choices = new List<ChoiceRecord>();
        public List<FlowParameterRecord> parameters = new List<FlowParameterRecord>();
        public List<FlowVariableRecord> variables = new List<FlowVariableRecord>();
        public List<StepBindingRecord> bindings = new List<StepBindingRecord>();
        public List<FlowExitRecord> exits = new List<FlowExitRecord>();
        public List<ExitMappingRecord> exitMappings = new List<ExitMappingRecord>();
    }

    [Serializable]
    public sealed class StepRecord
    {
        public string id = string.Empty;
        public string kind = string.Empty;
        public ContentRef dialogueRef;
        public ContentRef actionRef;
        public ContentRef calledFlowRef;
        public string dataJson = "{}";
    }

    [Serializable]
    public sealed class TransitionRecord
    {
        public string id = string.Empty;
        public string fromStepId = string.Empty;
        public string toStepId = string.Empty;
        public string purpose = string.Empty;
        public ContentRef ruleRef;
    }

    [Serializable]
    public sealed class ChoiceRecord
    {
        public string stepId = string.Empty;
        public string id = string.Empty;
        public string transitionId = string.Empty;
        public string valueJson = "{}";
        public ContentRef labelTextRef;
        public ContentRef availabilityRuleRef;
        public ContentRef lockedHintTextRef;
    }

    [Serializable]
    public sealed class DialogueRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public List<BeatRecord> beats = new List<BeatRecord>();
    }

    [Serializable]
    public sealed class BeatRecord
    {
        public string id = string.Empty;
        public int sequenceOrder;
        public ContentRef textRef;
        public string expression = string.Empty;
        public string presentationJson = "{}";
    }

    [Serializable]
    public sealed class TextRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public List<TranslationRecord> translations = new List<TranslationRecord>();
    }

    [Serializable]
    public sealed class TranslationRecord
    {
        public string language = string.Empty;
        public string text = string.Empty;
    }

    [Serializable]
    public sealed class ActionRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public string capabilityId = string.Empty;
        public string contractJson = "{}";
        public string implementationKind = "legacy_activity";
        public string composerId = string.Empty;
        public string functionId = string.Empty;
        public string argumentsJson = string.Empty;
        public List<ActionSequenceStepRecord> sequenceSteps = new List<ActionSequenceStepRecord>();
    }

    [Serializable]
    public sealed class ActionSequenceStepRecord
    {
        public int ordinal;
        public string stepId = string.Empty;
        public string primitiveId = string.Empty;
        public string argumentsJson = "[]";
        public string outputsJson = "[]";
        public string successEdgeJson = "{}";
        public string failureEdgeJson = "{}";
        public string cancelEdgeJson = "{}";
        public string checkpointPolicy = "none";
    }

    [Serializable]
    public sealed class ItemRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public ContentRef displayTextRef;
        public long stackLimit;
    }

    [Serializable]
    public sealed class FurnitureDefinitionRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public ContentRef displayTextRef;
        public string definitionJson = "{}";
        public List<FurnitureFunctionBindingRecord> functionBindings = new List<FurnitureFunctionBindingRecord>();
    }

    public sealed class CompiledFurnitureCatalogue
    {
        public readonly FurnitureCatalogueDocument document;
        public readonly string fingerprint;

        internal CompiledFurnitureCatalogue(FurnitureCatalogueDocument compiledDocument, string compiledFingerprint)
        {
            document = compiledDocument;
            fingerprint = compiledFingerprint ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class FurnitureFunctionBindingRecord
    {
        public string functionId = string.Empty;
        public string pointId = string.Empty;
        public string useId = string.Empty;
        public int functionContractVersion;
        public string contractJson = "{}";
    }

    [Serializable]
    public sealed class RuleRecord
    {
        public string id = string.Empty;
        public string revision = string.Empty;
        public string semanticHash = string.Empty;
        public string expressionJson = "{}";
    }

    [Serializable]
    public sealed class FlowParameterRecord
    {
        public string name = string.Empty;
        public string direction = string.Empty;
        public string valueType = string.Empty;
        public bool required;
        public string defaultValueJson = string.Empty;
    }

    [Serializable]
    public sealed class FlowVariableRecord
    {
        public string name = string.Empty;
        public string valueType = string.Empty;
        public bool mutable;
        public string defaultValueJson = string.Empty;
    }

    [Serializable]
    public sealed class StepBindingRecord
    {
        public string stepId = string.Empty;
        public string destinationName = string.Empty;
        public string sourceKind = string.Empty;
        public string literalJson = string.Empty;
        public string sourceName = string.Empty;
        public string hostFactId = string.Empty;
        public string resultStepId = string.Empty;
        public string resultName = string.Empty;
    }

    [Serializable]
    public sealed class FlowExitRecord
    {
        public string name = string.Empty;
        public string contractJson = "{}";
    }

    [Serializable]
    public sealed class ExitMappingRecord
    {
        public string callStepId = string.Empty;
        public ContentRef calleeFlowRef;
        public string exitName = string.Empty;
        public string transitionId = string.Empty;
        public string outputMappingsJson = "[]";
    }

    [Serializable]
    public sealed class AnchorRecord
    {
        public string id = string.Empty;
        public string language = string.Empty;
        public string text = string.Empty;
        public string textHash = string.Empty;
    }

    [Serializable]
    public sealed class AnchorLinkRecord
    {
        public string id = string.Empty;
        public string anchorId = string.Empty;
        public string ownerKind = string.Empty;
        public string ownerId = string.Empty;
        public string ownerRevision = string.Empty;
        public string role = string.Empty;
    }

    [Serializable]
    public sealed class EmbeddingProfileRecord
    {
        public string id = string.Empty;
        public string modelId = string.Empty;
        public string modelRevision = string.Empty;
        public string tokenizerHash = string.Empty;
        public string pipelineHash = string.Empty;
        public int dimensions;
        public string dtype = "float32-le";
        public string normalization = "none";
        public string metric = "cosine";
    }

    [Serializable]
    public sealed class EmbeddingRecord
    {
        public string anchorId = string.Empty;
        public string profileId = string.Empty;
        public byte[] vector = new byte[0];
        public string vectorHash = string.Empty;
    }

    public sealed class ContentJobRecord
    {
        public readonly string jobId;
        public readonly string idempotencyKey;
        public readonly string kind;
        public readonly string sourceHash;
        public readonly string state;
        public readonly string errorCode;
        public readonly int attempts;

        internal ContentJobRecord(string id, string key, string jobKind, string source, string jobState, string error, int attemptCount)
        {
            jobId = id ?? string.Empty; idempotencyKey = key ?? string.Empty; kind = jobKind ?? string.Empty;
            sourceHash = source ?? string.Empty; state = jobState ?? string.Empty; errorCode = error ?? string.Empty;
            attempts = attemptCount;
        }
    }
}
