using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AlterOS.Activities
{
    public sealed class ActivityArgumentDescriptor
    {
        public readonly string name;
        public readonly string valueType;
        public readonly bool required;
        public readonly double? minimum;
        public readonly double? maximum;
        public readonly ReadOnlyCollection<string> allowedValues;
        public readonly string entityKind;
        public readonly string requiredProducerPrimitiveId;

        public ActivityArgumentDescriptor(string argumentName, string type, bool isRequired,
            double? min = null, double? max = null, IEnumerable<string> values = null,
            string requiredEntityKind = "", string requiredProducer = "")
        {
            name = argumentName ?? "";
            valueType = type ?? "";
            required = isRequired;
            minimum = min;
            maximum = max;
            allowedValues = Array.AsReadOnly((values ?? new string[0]).ToArray());
            entityKind = requiredEntityKind ?? "";
            requiredProducerPrimitiveId = requiredProducer ?? "";
        }
    }

    public sealed class ActivityResultDescriptor
    {
        public readonly string name;
        public readonly string valueType;
        public readonly double? minimum;
        public readonly double? maximum;
        public readonly ReadOnlyCollection<string> allowedValues;
        public readonly string entityKind;

        public ActivityResultDescriptor(string resultName, string type,
            double? min = null, double? max = null, IEnumerable<string> values = null,
            string resultEntityKind = "")
        {
            name = resultName ?? "";
            valueType = type ?? "";
            minimum = min;
            maximum = max;
            allowedValues = Array.AsReadOnly((values ?? new string[0]).ToArray());
            entityKind = resultEntityKind ?? "";
        }
    }

    /// <summary>Trusted host description of one primitive; content may select but cannot register it.</summary>
    public sealed class ActivityPrimitiveDescriptor
    {
        public readonly string primitiveId;
        public readonly string version;
        public readonly ActivityPrimitiveOperationKind operationKind;
        public readonly string requiredCapabilityId;
        public readonly ReadOnlyCollection<ActivityArgumentDescriptor> arguments;
        public readonly ReadOnlyCollection<ActivityResultDescriptor> results;

        public ActivityPrimitiveDescriptor(string id, string descriptorVersion,
            ActivityPrimitiveOperationKind operation, string capability,
            IEnumerable<ActivityArgumentDescriptor> argumentDescriptors,
            IEnumerable<ActivityResultDescriptor> resultDescriptors)
        {
            if (!ActivitySequenceIdentifiers.IsValid(id)) throw new ArgumentException("A valid primitive ID is required.", "id");
            if (string.IsNullOrWhiteSpace(descriptorVersion)) throw new ArgumentException("A primitive descriptor version is required.", "descriptorVersion");
            if (!Enum.IsDefined(typeof(ActivityPrimitiveOperationKind), operation)) throw new ArgumentOutOfRangeException("operation");
            if (!string.IsNullOrEmpty(capability) && !ActivitySequenceIdentifiers.IsValid(capability))
                throw new ArgumentException("A required capability ID must be a valid identifier.", "capability");
            primitiveId = id;
            version = descriptorVersion;
            operationKind = operation;
            requiredCapabilityId = capability ?? "";

            ActivityArgumentDescriptor[] argumentArray = (argumentDescriptors ?? new ActivityArgumentDescriptor[0]).ToArray();
            ActivityResultDescriptor[] resultArray = (resultDescriptors ?? new ActivityResultDescriptor[0]).ToArray();
            if (argumentArray.Any(item => item == null || !ActivitySequenceIdentifiers.IsValid(item.name) ||
                    !ActivitySequenceValueTypes.IsSupported(item.valueType)) ||
                argumentArray.Select(item => item.name).Distinct(StringComparer.Ordinal).Count() != argumentArray.Length)
                throw new ArgumentException("Primitive arguments must have unique valid names and supported value types.", "argumentDescriptors");
            if (resultArray.Any(item => item == null || !ActivitySequenceIdentifiers.IsValid(item.name) ||
                    !ActivitySequenceValueTypes.IsSupported(item.valueType)) ||
                resultArray.Select(item => item.name).Distinct(StringComparer.Ordinal).Count() != resultArray.Length)
                throw new ArgumentException("Primitive results must have unique valid names and supported value types.", "resultDescriptors");
            foreach (ActivityArgumentDescriptor argument in argumentArray)
            {
                if ((!string.IsNullOrEmpty(argument.entityKind) &&
                     (argument.valueType != "entity" || !ActivitySequenceIdentifiers.IsValid(argument.entityKind))) ||
                    (!string.IsNullOrEmpty(argument.requiredProducerPrimitiveId) &&
                     !ActivitySequenceIdentifiers.IsValid(argument.requiredProducerPrimitiveId)))
                    throw new ArgumentException("Primitive argument provenance metadata is invalid.", "argumentDescriptors");
                if ((argument.minimum.HasValue && (double.IsNaN(argument.minimum.Value) || double.IsInfinity(argument.minimum.Value))) ||
                    (argument.maximum.HasValue && (double.IsNaN(argument.maximum.Value) || double.IsInfinity(argument.maximum.Value))))
                    throw new ArgumentException("Primitive argument bounds must be finite.", "argumentDescriptors");
                if (argument.minimum.HasValue && argument.maximum.HasValue && argument.minimum.Value > argument.maximum.Value)
                    throw new ArgumentException("Primitive argument bounds are inverted.", "argumentDescriptors");
                if (argument.allowedValues.Any(string.IsNullOrWhiteSpace) ||
                    argument.allowedValues.Distinct(StringComparer.Ordinal).Count() != argument.allowedValues.Count)
                    throw new ArgumentException("Primitive argument enum values must be non-empty and unique.", "argumentDescriptors");
            }
            foreach (ActivityResultDescriptor result in resultArray)
            {
                if (!string.IsNullOrEmpty(result.entityKind) &&
                    (result.valueType != "entity" || !ActivitySequenceIdentifiers.IsValid(result.entityKind)))
                    throw new ArgumentException("Primitive result entity metadata is invalid.", "resultDescriptors");
                if ((result.minimum.HasValue && (double.IsNaN(result.minimum.Value) || double.IsInfinity(result.minimum.Value))) ||
                    (result.maximum.HasValue && (double.IsNaN(result.maximum.Value) || double.IsInfinity(result.maximum.Value))))
                    throw new ArgumentException("Primitive result bounds must be finite.", "resultDescriptors");
                if (result.minimum.HasValue && result.maximum.HasValue && result.minimum.Value > result.maximum.Value)
                    throw new ArgumentException("Primitive result bounds are inverted.", "resultDescriptors");
                if (result.allowedValues.Any(string.IsNullOrWhiteSpace) ||
                    result.allowedValues.Distinct(StringComparer.Ordinal).Count() != result.allowedValues.Count)
                    throw new ArgumentException("Primitive result enum values must be non-empty and unique.", "resultDescriptors");
            }
            if (primitiveId == ActivityPrimitiveIds.Wait)
            {
                ActivityArgumentDescriptor duration = argumentArray.FirstOrDefault(item => item.name == "durationSeconds");
                if (duration == null || !duration.required || duration.valueType != "double" ||
                    !duration.minimum.HasValue || duration.minimum.Value < 0d ||
                    !duration.maximum.HasValue || duration.maximum.Value > 300d)
                    throw new ArgumentException("The wait primitive must bound durationSeconds to a finite range within 0..300.", "argumentDescriptors");
            }
            arguments = Array.AsReadOnly(argumentArray);
            results = Array.AsReadOnly(resultArray);
        }
    }

    public sealed class ActivityPrimitiveRegistry
    {
        private readonly ReadOnlyDictionary<string, ActivityPrimitiveDescriptor> descriptors;

        public ActivityPrimitiveRegistry(IEnumerable<ActivityPrimitiveDescriptor> items)
        {
            var map = new Dictionary<string, ActivityPrimitiveDescriptor>(StringComparer.Ordinal);
            foreach (ActivityPrimitiveDescriptor descriptor in items ?? new ActivityPrimitiveDescriptor[0])
            {
                if (descriptor == null || map.ContainsKey(descriptor.primitiveId))
                    throw new ArgumentException("Primitive descriptors must be non-null and have unique IDs.", "items");
                map.Add(descriptor.primitiveId, descriptor);
            }
            foreach (ActivityPrimitiveDescriptor descriptor in map.Values)
            foreach (ActivityArgumentDescriptor argument in descriptor.arguments)
            {
                if (string.IsNullOrEmpty(argument.requiredProducerPrimitiveId)) continue;
                ActivityPrimitiveDescriptor producer;
                if (!map.TryGetValue(argument.requiredProducerPrimitiveId, out producer) ||
                    !producer.results.Any(result => result.valueType == argument.valueType &&
                        (string.IsNullOrEmpty(argument.entityKind) || result.entityKind == argument.entityKind)))
                    throw new ArgumentException("A required result producer must be registered with a compatible result type and entity kind.", "items");
            }
            descriptors = new ReadOnlyDictionary<string, ActivityPrimitiveDescriptor>(map);
        }

        public bool TryResolve(string primitiveId, out ActivityPrimitiveDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(primitiveId))
            {
                descriptor = null;
                return false;
            }
            return descriptors.TryGetValue(primitiveId, out descriptor);
        }

        public IReadOnlyCollection<ActivityPrimitiveDescriptor> All
        {
            get { return Array.AsReadOnly(descriptors.Values.ToArray()); }
        }
    }

    internal static class ActivitySequenceIdentifiers
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':'))
                    return false;
            return true;
        }
    }

    internal static class ActivitySequenceValueTypes
    {
        private static readonly HashSet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "bool", "int64", "double", "text", "enum", "entity"
        };

        public static bool IsSupported(string valueType)
        {
            return Supported.Contains(valueType ?? "");
        }
    }
}
