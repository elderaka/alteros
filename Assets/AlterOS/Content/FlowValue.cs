using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using JsonArray = Newtonsoft.Json.Linq.JArray;
using JsonObject = Newtonsoft.Json.Linq.JObject;
using JsonPrimitive = Newtonsoft.Json.Linq.JValue;
using JsonValue = Newtonsoft.Json.Linq.JToken;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Content
{
    public sealed class FlowEntityReference
    {
        public readonly string kind;
        public readonly string id;
        public FlowEntityReference(string entityKind, string entityId)
        { kind = entityKind ?? string.Empty; id = entityId ?? string.Empty; }
    }

    /// <summary>Immutable bounded runtime value shared by Flow validation and execution.</summary>
    public sealed class FlowValue
    {
        public readonly string type;
        public readonly bool boolValue;
        public readonly long int64Value;
        public readonly double doubleValue;
        public readonly string textValue;
        public readonly FlowEntityReference entityValue;

        private FlowValue(string valueType, bool boolean, long integer, double number, string text, FlowEntityReference entity)
        {
            type = valueType ?? string.Empty; boolValue = boolean; int64Value = integer;
            doubleValue = number; textValue = text ?? string.Empty; entityValue = entity;
        }

        public static FlowValue Boolean(bool value) { return new FlowValue("bool", value, 0, 0d, "", null); }
        public static FlowValue Int64(long value) { return new FlowValue("int64", false, value, 0d, "", null); }
        public static FlowValue Double(double value)
        { if (!Finite(value)) throw new ArgumentOutOfRangeException("value"); return new FlowValue("double", false, 0, value, "", null); }
        public static FlowValue Text(string value) { return new FlowValue("text", false, 0, 0d, value, null); }
        public static FlowValue Enum(string value) { return new FlowValue("enum", false, 0, 0d, value, null); }
        public static FlowValue Entity(string kind, string id) { return new FlowValue("entity", false, 0, 0d, "", new FlowEntityReference(kind, id)); }

        public static ContentResult<FlowValue> ParseJson(string json, string declaredType,
            ContentHostVocabulary vocabulary, ScenarioRuntimeSnapshot context)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 2048 || string.IsNullOrWhiteSpace(declaredType))
                return Invalid("flow_value_size_invalid", "A Flow value must be non-empty and at most 2048 characters.");
            JsonValue parsed;
            try { parsed = ContentJson.Parse(json); }
            catch (Exception exception) { return Invalid("flow_value_json_invalid", exception.Message); }
            try
            {
                if (declaredType == "bool" && parsed.Type == JTokenType.Boolean) return ContentResult<FlowValue>.Ready(Boolean((bool)parsed));
                if (declaredType == "int64" && ContentJson.IsNumber(parsed))
                {
                    long integer;
                    if (long.TryParse(parsed.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                        return ContentResult<FlowValue>.Ready(Int64(integer));
                }
                if (declaredType == "double" && ContentJson.IsNumber(parsed))
                {
                    double number;
                    if (double.TryParse(parsed.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && Finite(number))
                        return ContentResult<FlowValue>.Ready(Double(number));
                }
                if ((declaredType == "text" || declaredType == "enum") && parsed.Type == JTokenType.String)
                {
                    string value = (string)parsed;
                    if (value.Length > 2048) return Invalid("flow_value_text_limit", "Text and enum values are limited to 2048 characters.");
                    if (declaredType == "enum" && (vocabulary == null || !vocabulary.IsEnumValueAllowed(value)))
                        return Invalid("flow_enum_unregistered", "The enum value is not present in the host vocabulary.");
                    return ContentResult<FlowValue>.Ready(declaredType == "enum" ? Enum(value) : Text(value));
                }
                if (declaredType == "entity")
                {
                    JsonObject entity = parsed as JsonObject;
                    if (entity != null && entity.Count == 2 && entity.ContainsKey("kind") && entity.ContainsKey("id"))
                    {
                        string kind = (string)entity["kind"];
                        string id = (string)entity["id"];
                        if (IsIdentifier(kind) && IsIdentifier(id) && context != null && context.objects != null &&
                            context.objects.Count(item => item != null && item.kind == kind && item.objectId == id) == 1)
                            return ContentResult<FlowValue>.Ready(Entity(kind, id));
                    }
                    return Invalid("flow_entity_unavailable", "The entity reference is malformed, missing, ambiguous, or not in the current host snapshot.");
                }
            }
            catch (Exception exception) { return Invalid("flow_value_type_invalid", exception.Message); }
            return Invalid("flow_value_type_mismatch", "The JSON value does not match its declared Flow type.");
        }

        public bool IsAvailableIn(ScenarioRuntimeSnapshot context)
        {
            if (type != "entity") return true;
            return entityValue != null && context != null && context.objects != null &&
                context.objects.Count(item => item != null && item.kind == entityValue.kind && item.objectId == entityValue.id) == 1;
        }

        public string ToHostArgument()
        {
            switch (type)
            {
                case "bool": return boolValue ? "true" : "false";
                case "int64": return int64Value.ToString(CultureInfo.InvariantCulture);
                case "double": return doubleValue.ToString("R", CultureInfo.InvariantCulture);
                case "text": case "enum": return textValue;
                case "entity": return entityValue == null ? string.Empty : entityValue.id;
                default: return string.Empty;
            }
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == ':')) return false;
            return true;
        }

        private static ContentResult<FlowValue> Invalid(string code, string message)
        { return ContentResult<FlowValue>.Failure(ContentStatuses.Invalid, code, "flow_value", message); }
    }
}
