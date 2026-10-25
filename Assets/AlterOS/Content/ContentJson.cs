using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AlterOS.Content
{
    /// <summary>Shared JSON parsing rules for Unity runtime and content tooling.</summary>
    internal static class ContentJson
    {
        internal static JToken Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            using (var reader = new StrictJsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                if (!reader.Read()) throw new InvalidDataException("JSON input is empty.");
                JToken value = JToken.ReadFrom(reader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace
                });
                if (reader.SawComment) throw new InvalidDataException("JSON comments are not supported.");
                if (reader.Read()) throw new InvalidDataException("JSON input contains trailing content.");
                if (reader.SawComment) throw new InvalidDataException("JSON comments are not supported.");
                return value;
            }
        }

        internal static bool IsNull(JToken value)
        { return value == null || value.Type == JTokenType.Null; }

        internal static bool IsNumber(JToken value)
        { return value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float); }

        internal static string Quote(string value)
        { return JsonConvert.ToString(value ?? string.Empty); }

        internal static string SerializeCompact(JToken value)
        { return value == null ? "null" : value.ToString(Formatting.None); }

        private sealed class StrictJsonTextReader : JsonTextReader
        {
            internal bool SawComment { get; private set; }

            internal StrictJsonTextReader(TextReader textReader) : base(textReader) { }

            public override bool Read()
            {
                bool hasToken = base.Read();
                if (hasToken && TokenType == JsonToken.Comment) SawComment = true;
                return hasToken;
            }
        }
    }
}
