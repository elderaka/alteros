using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.Activities
{
    /// <summary>Typed parameters accepted by one trusted sequence composer version.</summary>
    public sealed class ActivitySequenceComposerDescriptor
    {
        public readonly string composerId;
        public readonly string version;
        public readonly ReadOnlyCollection<ActivityArgumentDescriptor> arguments;

        public ActivitySequenceComposerDescriptor(string id, string descriptorVersion,
            IEnumerable<ActivityArgumentDescriptor> argumentDescriptors)
        {
            if (!ActivitySequenceIdentifiers.IsValid(id)) throw new ArgumentException("A valid composer ID is required.", "id");
            if (!ActivitySequenceIdentifiers.IsValid(descriptorVersion)) throw new ArgumentException("A valid composer descriptor version is required.", "descriptorVersion");
            ActivityArgumentDescriptor[] items = (argumentDescriptors ?? new ActivityArgumentDescriptor[0]).ToArray();
            if (items.Any(item => item == null || !ActivitySequenceIdentifiers.IsValid(item.name) ||
                    !ActivitySequenceValueTypes.IsSupported(item.valueType)) ||
                items.Select(item => item.name).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new ArgumentException("Composer arguments must have unique valid names and supported value types.", "argumentDescriptors");
            if (items.Any(item => !string.IsNullOrEmpty(item.entityKind) ||
                    !string.IsNullOrEmpty(item.requiredProducerPrimitiveId)))
                throw new ArgumentException("Composer arguments cannot require sequence result bindings.", "argumentDescriptors");
            if (items.Any(item =>
                    (item.minimum.HasValue && (double.IsNaN(item.minimum.Value) || double.IsInfinity(item.minimum.Value))) ||
                    (item.maximum.HasValue && (double.IsNaN(item.maximum.Value) || double.IsInfinity(item.maximum.Value))) ||
                    (item.minimum.HasValue && item.maximum.HasValue && item.minimum.Value > item.maximum.Value) ||
                    item.allowedValues.Any(string.IsNullOrWhiteSpace) ||
                    item.allowedValues.Distinct(StringComparer.Ordinal).Count() != item.allowedValues.Count))
                throw new ArgumentException("Composer argument bounds and enum values must be valid.", "argumentDescriptors");
            composerId = id;
            version = descriptorVersion;
            arguments = Array.AsReadOnly(items);
        }
    }

    public interface IActivitySequenceComposer
    {
        string composerId { get; }
        ActivitySequenceDefinition Compose(ActivitySequenceSource source);
    }

    /// <summary>One immutable trusted default set shared by content validation and runtime normalization.</summary>
    public sealed class ActivitySequenceCatalog
    {
        private readonly ReadOnlyCollection<IActivitySequenceComposer> trustedComposers;
        public readonly ActivityPrimitiveRegistry Primitives;
        public readonly ReadOnlyCollection<ActivitySequenceComposerDescriptor> Composers;

        internal ActivitySequenceCatalog(ActivityPrimitiveRegistry primitives,
            IEnumerable<IActivitySequenceComposer> composers,
            IEnumerable<ActivitySequenceComposerDescriptor> descriptors)
        {
            Primitives = primitives ?? throw new ArgumentNullException("primitives");
            IActivitySequenceComposer[] composerItems = (composers ?? new IActivitySequenceComposer[0]).ToArray();
            ActivitySequenceComposerDescriptor[] descriptorItems =
                (descriptors ?? new ActivitySequenceComposerDescriptor[0]).ToArray();
            trustedComposers = Array.AsReadOnly(composerItems);
            Composers = Array.AsReadOnly(descriptorItems);
        }

        public ActivitySequenceBuilder CreateBuilder()
        {
            return new ActivitySequenceBuilder(Primitives, trustedComposers, Composers);
        }
    }

    public static class ActivitySequenceCatalogFactory
    {
        public static ActivitySequenceCatalog CreateDefault()
        {
            var primitives = new ActivityPrimitiveRegistry(new[]
            {
                Primitive(ActivityPrimitiveIds.ReserveNearest, ActivityPrimitiveOperationKind.Pure,
                    ActivitySequenceCapabilityIds.ReserveNearest,
                    new[]
                    {
                        Arg("useId", "text", true),
                        Arg("functionId", "enum", false, values: new[] { "power.set", "dispenser.collect_mock" })
                    },
                    new[] { Result("targetToken", "entity", entityKind: ActivitySequenceEntityKinds.ReservedTarget) }),
                Primitive(ActivityPrimitiveIds.MoveTo, ActivityPrimitiveOperationKind.Pure, "",
                    new[] { TargetArgument("target") }, new[] { Result("arrived", "bool") }),
                Primitive(ActivityPrimitiveIds.Face, ActivityPrimitiveOperationKind.Pure, "",
                    new[] { TargetArgument("target") }, new ActivityResultDescriptor[0]),
                Primitive(ActivityPrimitiveIds.Bark, ActivityPrimitiveOperationKind.Presentation,
                    ActivitySequenceCapabilityIds.Bark,
                    new[] { Arg("dialogueId", "text", true) }, new[] { Result("delivered", "bool") }),
                Primitive(ActivityPrimitiveIds.Wait, ActivityPrimitiveOperationKind.Pure, "",
                    new[] { Arg("durationSeconds", "double", true, 0d, ActivitySequenceValidator.MaximumWaitSeconds) },
                    new ActivityResultDescriptor[0]),
                Primitive(ActivityPrimitiveIds.InvokeFurniture, ActivityPrimitiveOperationKind.FurnitureFunction, "",
                    new[]
                    {
                        TargetArgument("target"),
                        Arg("functionId", "enum", true, values: new[] { "power.set", "dispenser.collect_mock" }),
                        Arg("desiredState", "enum", false, values: new[] { "Off", "On" })
                    },
                    new[]
                    {
                        Result("functionStatus", "enum", values: new[]
                        { "applied", "no_change", "replayed", "unavailable", "conflict", "failed", "completed", "cancelled" })
                    }),
                Primitive(ActivityPrimitiveIds.MoveNearPlayer, ActivityPrimitiveOperationKind.Pure,
                    ActivitySequenceCapabilityIds.MoveNearPlayer, new ActivityArgumentDescriptor[0],
                    new[] { Result("arrived", "bool") }),
                Primitive(ActivityPrimitiveIds.Checkpoint, ActivityPrimitiveOperationKind.Pure,
                    ActivitySequenceCapabilityIds.Checkpoint,
                    new[] { Arg("checkpointId", "text", true) }, new[] { Result("recorded", "bool") }),
                Primitive(ActivityPrimitiveIds.Complete, ActivityPrimitiveOperationKind.Pure,
                    ActivitySequenceCapabilityIds.Complete, new ActivityArgumentDescriptor[0], new ActivityResultDescriptor[0])
            });
            var composer = new WaitThenCompleteComposer();
            var descriptor = new ActivitySequenceComposerDescriptor(composer.composerId, "1",
                new[] { Arg("durationSeconds", "double", true, 0d, ActivitySequenceValidator.MaximumWaitSeconds) });
            return new ActivitySequenceCatalog(primitives, new[] { composer }, new[] { descriptor });
        }

        private static ActivityPrimitiveDescriptor Primitive(string id, ActivityPrimitiveOperationKind operation,
            string capabilityId, IEnumerable<ActivityArgumentDescriptor> arguments,
            IEnumerable<ActivityResultDescriptor> results)
        {
            return new ActivityPrimitiveDescriptor(id, "1", operation, capabilityId, arguments, results);
        }

        private static ActivityArgumentDescriptor Arg(string name, string type, bool required,
            double? minimum = null, double? maximum = null, IEnumerable<string> values = null)
        { return new ActivityArgumentDescriptor(name, type, required, minimum, maximum, values); }

        private static ActivityArgumentDescriptor TargetArgument(string name)
        {
            return new ActivityArgumentDescriptor(name, "entity", true, requiredEntityKind: ActivitySequenceEntityKinds.ReservedTarget,
                requiredProducer: ActivityPrimitiveIds.ReserveNearest);
        }

        private static ActivityResultDescriptor Result(string name, string type,
            double? minimum = null, double? maximum = null, IEnumerable<string> values = null, string entityKind = "")
        { return new ActivityResultDescriptor(name, type, minimum, maximum, values, entityKind); }
    }

    public static class ActivitySequenceCapabilityIds
    {
        public const string ReserveNearest = "furniture.reserve_nearest";
        public const string Bark = "dialogue.bark";
        public const string MoveNearPlayer = "movement.near_player";
        public const string Checkpoint = "scenario.checkpoint";
        public const string Complete = "scenario.complete";
    }

    public static class ActivitySequenceEntityKinds
    {
        public const string ReservedTarget = "activity_target";
    }

    internal sealed class WaitThenCompleteComposer : IActivitySequenceComposer
    {
        public string composerId { get { return "sequence.wait_then_complete.v1"; } }

        public ActivitySequenceDefinition Compose(ActivitySequenceSource source)
        {
            double durationSeconds = source.composerArguments.Single(item => item.name == "durationSeconds")
                .value.literal.doubleValue;
            return new ActivitySequenceDefinition(source.id, source.revision, new[]
            {
                new ActivitySequenceStep("wait", ActivityPrimitiveIds.Wait,
                    new[] { ActivitySequenceArgument.FromLiteral("durationSeconds", FlowValue.Double(durationSeconds)) },
                    success: "complete"),
                new ActivitySequenceStep("complete", ActivityPrimitiveIds.Complete)
            });
        }
    }

    public sealed class ActivitySequenceBuildResult
    {
        public readonly bool isValid;
        public readonly ActivitySequenceDefinition definition;
        public readonly ReadOnlyCollection<ContentDiagnostic> diagnostics;

        internal ActivitySequenceBuildResult(bool valid, ActivitySequenceDefinition sequence,
            IEnumerable<ContentDiagnostic> items)
        {
            isValid = valid;
            definition = sequence;
            diagnostics = Array.AsReadOnly((items ?? new ContentDiagnostic[0]).ToArray());
        }
    }

    /// <summary>Normalizes authored steps or a registered trusted composer through one validator.</summary>
    public sealed class ActivitySequenceBuilder
    {
        private readonly ActivitySequenceValidator validator;
        private readonly Dictionary<string, IActivitySequenceComposer> composers;
        private readonly Dictionary<string, ActivitySequenceComposerDescriptor> composerDescriptors;

        public ActivitySequenceBuilder(ActivityPrimitiveRegistry primitives,
            IEnumerable<IActivitySequenceComposer> trustedComposers,
            IEnumerable<ActivitySequenceComposerDescriptor> composerDescriptorMetadata = null)
        {
            if (primitives == null) throw new ArgumentNullException("primitives");
            validator = new ActivitySequenceValidator(primitives);
            composers = new Dictionary<string, IActivitySequenceComposer>(StringComparer.Ordinal);
            composerDescriptors = new Dictionary<string, ActivitySequenceComposerDescriptor>(StringComparer.Ordinal);
            foreach (IActivitySequenceComposer composer in trustedComposers ?? new IActivitySequenceComposer[0])
            {
                if (composer == null || !ActivitySequenceIdentifiers.IsValid(composer.composerId) ||
                    composers.ContainsKey(composer.composerId))
                    throw new ArgumentException("Trusted composers must be non-null and have unique valid IDs.", "trustedComposers");
                composers.Add(composer.composerId, composer);
            }
            foreach (ActivitySequenceComposerDescriptor descriptor in composerDescriptorMetadata ?? new ActivitySequenceComposerDescriptor[0])
            {
                if (descriptor == null || !composers.ContainsKey(descriptor.composerId) || composerDescriptors.ContainsKey(descriptor.composerId))
                    throw new ArgumentException("Composer metadata must have one trusted composer with a unique matching ID.", "composerDescriptorMetadata");
                composerDescriptors.Add(descriptor.composerId, descriptor);
            }
        }

        public ActivitySequenceBuildResult Build(ActivitySequenceSource source)
        {
            if (source == null)
                return Invalid("sequence_source_missing", "source", "An ActivitySequenceSource is required.");

            bool hasSteps = source.steps != null;
            bool hasComposer = !string.IsNullOrWhiteSpace(source.composerId);
            bool hasComposerArguments = source.composerArguments != null && source.composerArguments.Count > 0;
            if (hasSteps == hasComposer)
                return Invalid("sequence_source_ambiguous", "source",
                    "An activity_sequence Action must provide exactly one of steps or composerId.");
            if (hasSteps && hasComposerArguments)
                return Invalid("sequence_composer_arguments_unexpected", "composerArguments",
                    "Composer arguments are only valid when composerId is selected.");

            ActivitySequenceDefinition definition;
            if (hasSteps)
            {
                definition = new ActivitySequenceDefinition(source.id, source.revision, source.steps);
            }
            else
            {
                IActivitySequenceComposer composer;
                if (!composers.TryGetValue(source.composerId, out composer))
                    return Invalid("sequence_composer_unregistered", "composerId", "The trusted composer is not registered.");
                ActivitySequenceComposerDescriptor descriptor;
                if (composerDescriptors.TryGetValue(source.composerId, out descriptor))
                {
                    ContentDiagnostic argumentError = ValidateComposerArguments(source.composerArguments, descriptor);
                    if (argumentError != null)
                        return Invalid(argumentError.code, argumentError.path, argumentError.message);
                }
                else if (hasComposerArguments)
                    return Invalid("sequence_composer_schema_missing", "composerArguments",
                        "Parameterized composers require registered argument metadata.");
                try
                {
                    definition = composer.Compose(source);
                }
                catch
                {
                    return Invalid("sequence_composer_failed", "composerId", "The trusted composer could not build the sequence.");
                }
                if (definition == null || definition.id != source.id || definition.revision != source.revision)
                    return Invalid("sequence_composer_identity_mismatch", "composerId",
                        "The trusted composer must preserve the selected Action identity and revision.");
            }

            ActivitySequenceValidationResult validation = validator.Validate(definition);
            return new ActivitySequenceBuildResult(validation.isValid,
                validation.isValid ? definition : null, validation.diagnostics);
        }

        private static ContentDiagnostic ValidateComposerArguments(IEnumerable<ActivitySequenceArgument> source,
            ActivitySequenceComposerDescriptor descriptor)
        {
            ActivitySequenceArgument[] supplied = (source ?? new ActivitySequenceArgument[0]).ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActivitySequenceArgument argument in supplied)
            {
                if (argument == null || !ActivitySequenceIdentifiers.IsValid(argument.name) || !names.Add(argument.name))
                    return new ContentDiagnostic("sequence_composer_argument_invalid", "composerArguments",
                        "Composer argument names must be valid and unique.");
                ActivityArgumentDescriptor expected = descriptor.arguments.FirstOrDefault(item => item.name == argument.name);
                if (expected == null)
                    return new ContentDiagnostic("sequence_composer_argument_unknown", "composerArguments." + argument.name,
                        "The trusted composer does not declare this argument.");
                if (argument.value == null || argument.value.literal == null || !string.IsNullOrEmpty(argument.value.bindingId))
                    return new ContentDiagnostic("sequence_composer_argument_not_literal", "composerArguments." + argument.name,
                        "Composer arguments must be typed literal values.");
                FlowValue value = argument.value.literal;
                if (value.type != expected.valueType)
                    return new ContentDiagnostic("sequence_composer_argument_type_mismatch", "composerArguments." + argument.name,
                        "The argument type does not match the trusted composer descriptor.");
                if (!WithinBounds(value, expected))
                    return new ContentDiagnostic("sequence_composer_argument_out_of_range", "composerArguments." + argument.name,
                        "The argument is outside the trusted composer descriptor bounds.");
            }
            foreach (ActivityArgumentDescriptor expected in descriptor.arguments.Where(item => item.required))
                if (!names.Contains(expected.name))
                    return new ContentDiagnostic("sequence_composer_argument_required", "composerArguments." + expected.name,
                        "A required trusted composer argument is missing.");
            return null;
        }

        private static bool WithinBounds(FlowValue value, ActivityArgumentDescriptor expected)
        {
            if (expected.allowedValues.Count > 0 &&
                ((value.type != "enum" && value.type != "text") ||
                 !expected.allowedValues.Contains(value.textValue, StringComparer.Ordinal))) return false;
            double number;
            if (value.type == "double") number = value.doubleValue;
            else if (value.type == "int64") number = value.int64Value;
            else return true;
            return (!expected.minimum.HasValue || number >= expected.minimum.Value) &&
                (!expected.maximum.HasValue || number <= expected.maximum.Value);
        }

        private static ActivitySequenceBuildResult Invalid(string code, string path, string message)
        {
            return new ActivitySequenceBuildResult(false, null,
                new[] { new ContentDiagnostic(code, path, message) });
        }
    }
}
