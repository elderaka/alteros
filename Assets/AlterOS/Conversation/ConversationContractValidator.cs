using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using AlterOS.Dialogue;

namespace AlterOS.Conversation
{
    public static class ConversationContractValidator
    {
        public const int MaximumProtocolBytes = 128 * 1024;
        public const int MaximumRequestCharacters = 2048;
        public const int MaximumReplyNodes = 32;
        public const int MaximumNodeCharacters = 600;
        public const int MaximumReplyCharacters = 12000;
        public const int MaximumSuggestions = 3;

        private const string Direct = "direct";
        private const string Phone = "phone";
        private static readonly string[] JudgmentFields = { "schemaVersion", "requestId", "kind", "topicId", "referencedTopicIds", "reply", "suggestedReplies" };
        private static readonly string[] NodeFields = { "id", "speaker", "text", "displayText", "language", "voice", "audioKey", "nextId", "presentation", "choices" };

        public static ConversationRequest ParseRequest(string json)
        {
            XmlElement root = ReadRoot(json);
            Dictionary<string, XmlElement> fields = ReadObject(root,
                new[] { "schemaVersion", "requestId", "sessionId", "channel", "text" },
                new[] { "activeResponseId", "activeTopicId" });
            var request = new ConversationRequest
            {
                schemaVersion = ReadInt(Required(fields, "schemaVersion")),
                requestId = ReadString(Required(fields, "requestId")),
                sessionId = ReadString(Required(fields, "sessionId")),
                channel = ReadString(Required(fields, "channel")),
                text = ReadString(Required(fields, "text")),
                activeResponseId = ReadOptionalString(fields, "activeResponseId"),
                activeTopicId = ReadOptionalString(fields, "activeTopicId"),
            };
            ThrowIfInvalid(ValidateRequest(request));
            return request;
        }

        public static ConversationJudgment ParseJudgment(string json)
        {
            XmlElement root = ReadRoot(json);
            Dictionary<string, XmlElement> fields = ReadObject(root, JudgmentFields);
            var judgment = new ConversationJudgment
            {
                schemaVersion = ReadInt(Required(fields, "schemaVersion")),
                requestId = ReadString(Required(fields, "requestId")),
                kind = ReadString(Required(fields, "kind")),
                topicId = ReadString(Required(fields, "topicId")),
                referencedTopicIds = ReadStringArray(Required(fields, "referencedTopicIds"), 8),
                reply = ReadNullableReply(Required(fields, "reply")),
                suggestedReplies = ReadSuggestions(Required(fields, "suggestedReplies")),
            };
            ThrowIfInvalid(ValidateJudgmentShape(judgment, null, false));
            return judgment;
        }

        public static List<string> ValidateRequest(ConversationRequest request)
        {
            var errors = new List<string>();
            if (request == null) { errors.Add("Request is required."); return errors; }
            if (request.schemaVersion != 1) errors.Add("Unsupported request schemaVersion.");
            RequireText(request.requestId, "requestId", 128, errors);
            RequireText(request.sessionId, "sessionId", 128, errors);
            RequireText(request.text, "text", MaximumRequestCharacters, errors);
            if (request.channel != Direct && request.channel != Phone) errors.Add("Unsupported conversation channel.");
            if (!string.IsNullOrEmpty(request.activeTopicId) && string.IsNullOrEmpty(request.activeResponseId))
                errors.Add("activeTopicId requires activeResponseId.");
            if (request.activeResponseId != null && request.activeResponseId.Length > 128) errors.Add("activeResponseId exceeds the limit.");
            if (request.activeTopicId != null && request.activeTopicId.Length > 128) errors.Add("activeTopicId exceeds the limit.");
            if (request.explicitTopicId != null && request.explicitTopicId.Length > 128) errors.Add("explicitTopicId exceeds the limit.");
            return errors;
        }

        public static List<string> ValidateJudgment(ConversationRequest request, TopicCandidateSet candidates,
            ConversationJudgment judgment, ISet<string> allowedExpressions)
        {
            var errors = ValidateRequest(request);
            errors.AddRange(ValidateCandidates(candidates, true));
            errors.AddRange(ValidateJudgmentShape(judgment, allowedExpressions, true));
            if (errors.Count != 0 || request == null || candidates == null || candidates.cards == null ||
                judgment == null || judgment.referencedTopicIds == null) return errors;
            if (!string.Equals(judgment.requestId, request.requestId, StringComparison.Ordinal)) errors.Add("Judgment requestId does not match.");
            var suppliedIds = new HashSet<string>(candidates.cards.Where(card => card != null).Select(card => card.topicId), StringComparer.Ordinal);
            foreach (string topicId in judgment.referencedTopicIds)
                if (!suppliedIds.Contains(topicId) && !string.Equals(topicId, request.activeTopicId, StringComparison.Ordinal))
                    errors.Add("Judgment references a Topic outside its supplied context.");
            if (!string.IsNullOrEmpty(judgment.topicId) && !suppliedIds.Contains(judgment.topicId)) errors.Add("Judgment selected a Topic outside the candidate set.");
            if (judgment.kind == "existing" || judgment.kind == "existing_uncovered")
            {
                TopicCandidateCard selected = candidates.cards.Find(card => card != null && card.topicId == judgment.topicId);
                if (selected == null) errors.Add("Selected Topic is not a candidate.");
                else if (selected.authoredAvailability == "unchecked") errors.Add("Candidates must be prepared before judgment.");
                if (!judgment.referencedTopicIds.Contains(judgment.topicId)) errors.Add("Selected Topic must be included in references.");
            }
            if (judgment.kind == "continue" && !string.IsNullOrEmpty(request.activeTopicId) &&
                !judgment.referencedTopicIds.Contains(request.activeTopicId)) errors.Add("Continuation must retain the host-bound active Topic reference.");
            if (judgment.kind == "continue" && (string.IsNullOrEmpty(request.activeResponseId) || string.IsNullOrEmpty(request.activeTopicId)))
                errors.Add("Continuation requires a host-bound active response and Topic.");
            return errors;
        }

        public static List<string> ValidateCandidateSet(TopicCandidateSet candidates, bool requirePrepared = false) { return ValidateCandidates(candidates, requirePrepared); }

        private static List<string> ValidateCandidates(TopicCandidateSet candidates, bool requirePrepared)
        {
            var errors = new List<string>();
            if (candidates == null) { errors.Add("Candidate set is required."); return errors; }
            if (string.IsNullOrWhiteSpace(candidates.indexIdentity) || candidates.indexIdentity.Length > 256) errors.Add("Candidate index identity is required and bounded.");
            if (candidates.source != "local_gte" && candidates.source != "synthetic") errors.Add("Unsupported candidate source.");
            if (candidates.cards == null || candidates.cards.Count > 8) { errors.Add("Candidate set must contain zero to eight cards."); return errors; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TopicCandidateCard card in candidates.cards)
            {
                if (card == null) { errors.Add("Candidate card cannot be null."); continue; }
                RequireText(card.topicId, "candidate.topicId", 128, errors);
                if (!ids.Add(card.topicId ?? "")) errors.Add("Candidate Topic IDs must be unique.");
                if (card.prompt != null && card.prompt.Length > 512) errors.Add("Candidate prompt exceeds the limit.");
                if (card.examples == null || card.examples.Count > 3) errors.Add("Candidate examples must contain zero to three anchors.");
                else foreach (string example in card.examples) RequireText(example, "candidate.example", 256, errors);
                if (!Finite(card.similarity)) errors.Add("Candidate similarity must be finite.");
                if (card.supportCount < 0) errors.Add("Candidate supportCount cannot be negative.");
                if (card.authoredAvailability != "unchecked" && card.authoredAvailability != "ready" && card.authoredAvailability != "blocked")
                    errors.Add("Unknown authored availability.");
                if (requirePrepared && card.authoredAvailability == "unchecked") errors.Add("Every candidate must be prepared before judgment.");
            }
            return errors;
        }

        private static List<string> ValidateJudgmentShape(ConversationJudgment judgment, ISet<string> allowedExpressions, bool validateExpressionAllowlist)
        {
            var errors = new List<string>();
            if (judgment == null) { errors.Add("Judgment is required."); return errors; }
            if (judgment.schemaVersion != 1) errors.Add("Unsupported judgment schemaVersion.");
            RequireText(judgment.requestId, "judgment.requestId", 128, errors);
            if (judgment.kind != "existing" && judgment.kind != "existing_uncovered" && judgment.kind != "continue" &&
                judgment.kind != "clarify" && judgment.kind != "novel_candidate") errors.Add("Unsupported judgment kind.");
            if (judgment.topicId == null || judgment.topicId.Length > 128) errors.Add("topicId is invalid.");
            if (judgment.referencedTopicIds == null || judgment.referencedTopicIds.Count > 8) errors.Add("Invalid Topic reference list.");
            else if (judgment.referencedTopicIds.Distinct(StringComparer.Ordinal).Count() != judgment.referencedTopicIds.Count ||
                     judgment.referencedTopicIds.Any(string.IsNullOrWhiteSpace)) errors.Add("Topic references must be nonempty and unique.");
            if (judgment.suggestedReplies == null || judgment.suggestedReplies.Count > MaximumSuggestions) errors.Add("Suggestions must contain zero to three pairs.");
            else
            {
                var pairs = new HashSet<string>(StringComparer.Ordinal);
                foreach (SuggestedPlayerReply suggestion in judgment.suggestedReplies)
                {
                    if (suggestion == null) { errors.Add("Suggested reply cannot be null."); continue; }
                    RequireText(suggestion.label, "suggestion.label", 80, errors);
                    RequireText(suggestion.prompt, "suggestion.prompt", 512, errors);
                    if (!pairs.Add(suggestion.label + "\0" + suggestion.prompt)) errors.Add("Duplicate suggested reply.");
                }
            }
            if (judgment.kind == "existing" || judgment.kind == "existing_uncovered")
            {
                RequireText(judgment.topicId, "topicId", 128, errors);
                if (judgment.reply != null) errors.Add("Authored judgment cannot include generated reply prose.");
            }
            else
            {
                if (judgment.kind == "novel_candidate" && !string.IsNullOrEmpty(judgment.topicId)) errors.Add("Novel candidate cannot mint a canonical Topic ID.");
                if ((judgment.kind == "continue" || judgment.kind == "clarify" || judgment.kind == "novel_candidate") && string.IsNullOrWhiteSpace(judgment.topicId) == false && judgment.kind != "continue")
                    errors.Add("This judgment kind cannot select a canonical Topic ID.");
                ValidateReply(judgment.reply, allowedExpressions, validateExpressionAllowlist, errors);
            }
            return errors;
        }

        private static void ValidateReply(ConversationReply reply, ISet<string> allowedExpressions, bool validateExpressionAllowlist, List<string> errors)
        {
            if (reply == null || reply.nodes == null || reply.nodes.Count < 1 || reply.nodes.Count > MaximumReplyNodes)
            { errors.Add("Generated reply must contain one to thirty-two nodes."); return; }
            int total = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < reply.nodes.Count; index++)
            {
                CanonicalDialogueNode node = reply.nodes[index];
                if (node == null) { errors.Add("Generated node cannot be null."); continue; }
                RequireText(node.id, "node.id", 64, errors);
                if (!ids.Add(node.id ?? "")) errors.Add("Generated node IDs must be unique.");
                if (node.speaker != "alterra") errors.Add("Generated reply nodes must be spoken by Alterra.");
                RequireText(node.text, "node.text", MaximumNodeCharacters, errors);
                total += node.text == null ? 0 : node.text.Length;
                if (node.displayText == null || node.displayText.Length > MaximumNodeCharacters) errors.Add("displayText is invalid.");
                if (node.language == null || node.language.Length > 32 || node.voice != "" || node.audioKey != "") errors.Add("Generated voice and audio fields must remain empty and language bounded.");
                string expectedNext = index + 1 < reply.nodes.Count && reply.nodes[index + 1] != null ? reply.nodes[index + 1].id : "";
                if (node.nextId != "" && node.nextId != expectedNext) errors.Add("Generated replies must use linear ordered nodes.");
                if (node.choices != null && node.choices.Count > 0) errors.Add("Generated dialogue cannot contain graph choices.");
                if (node.presentation == null || node.presentation.animation != "") errors.Add("Generated animations are not allowed in this contract.");
                else if (validateExpressionAllowlist && node.presentation.expression != "" &&
                         (allowedExpressions == null || !allowedExpressions.Contains(node.presentation.expression)))
                    errors.Add("Generated expression is not in the host allowlist.");
            }
            if (total > MaximumReplyCharacters) errors.Add("Generated reply exceeds the total text limit.");
        }

        private static ConversationReply ReadNullableReply(XmlElement element)
        { return TypeOf(element) == "null" ? null : ReadReply(element); }

        private static ConversationReply ReadReply(XmlElement element)
        {
            Dictionary<string, XmlElement> fields = ReadObject(element, new[] { "nodes" });
            List<XmlElement> nodes = ReadArray(Required(fields, "nodes"), MaximumReplyNodes);
            var reply = new ConversationReply();
            foreach (XmlElement item in nodes)
            {
                Dictionary<string, XmlElement> nodeFields = ReadObject(item, NodeFields);
                Dictionary<string, XmlElement> presentation = ReadObject(Required(nodeFields, "presentation"), new[] { "expression", "animation" });
                List<XmlElement> choices = ReadArray(Required(nodeFields, "choices"), 32);
                var node = new CanonicalDialogueNode
                {
                    id = ReadString(Required(nodeFields, "id")), speaker = ReadString(Required(nodeFields, "speaker")),
                    text = ReadString(Required(nodeFields, "text")), displayText = ReadString(Required(nodeFields, "displayText")),
                    language = ReadString(Required(nodeFields, "language")), voice = ReadString(Required(nodeFields, "voice")),
                    audioKey = ReadString(Required(nodeFields, "audioKey")), nextId = ReadString(Required(nodeFields, "nextId")),
                    presentation = new DialoguePresentation { expression = ReadString(Required(presentation, "expression")), animation = ReadString(Required(presentation, "animation")) },
                    choices = new List<CanonicalDialogueChoice>()
                };
                foreach (XmlElement choiceElement in choices)
                {
                    Dictionary<string, XmlElement> choiceFields = ReadObject(choiceElement, new[] { "label", "nextId" });
                    node.choices.Add(new CanonicalDialogueChoice { label = ReadString(Required(choiceFields, "label")), nextId = ReadString(Required(choiceFields, "nextId")) });
                }
                reply.nodes.Add(node);
            }
            return reply;
        }

        private static List<SuggestedPlayerReply> ReadSuggestions(XmlElement element)
        {
            var result = new List<SuggestedPlayerReply>();
            foreach (XmlElement item in ReadArray(element, MaximumSuggestions))
            {
                Dictionary<string, XmlElement> fields = ReadObject(item, new[] { "label", "prompt" });
                result.Add(new SuggestedPlayerReply { label = ReadString(Required(fields, "label")), prompt = ReadString(Required(fields, "prompt")) });
            }
            return result;
        }

        internal static List<string> ReadStringArray(XmlElement element, int maximum)
        {
            var values = new List<string>();
            foreach (XmlElement item in ReadArray(element, maximum)) values.Add(ReadString(item));
            return values;
        }

        internal static XmlElement ReadRoot(string json, int maximumBytes = MaximumProtocolBytes)
        {
            try
            {
                if (json == null) throw new FormatException("JSON payload is required.");
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
                if (bytes.Length == 0 || bytes.Length > maximumBytes) throw new FormatException("JSON payload is empty or exceeds its byte limit.");
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 40, MaxStringContentLength = maximumBytes, MaxArrayLength = 4096, MaxBytesPerRead = 4096, MaxNameTableCharCount = 4096 };
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas))
                {
                    var document = new XmlDocument { XmlResolver = null };
                    document.Load(reader);
                    if (document.DocumentElement == null || document.DocumentElement.Name != "root" || TypeOf(document.DocumentElement) != "object")
                        throw new FormatException("JSON root must be an object.");
                    return document.DocumentElement;
                }
            }
            catch (FormatException) { throw; }
            catch (Exception exception) { throw new FormatException("Malformed or oversized JSON payload.", exception); }
        }

        internal static Dictionary<string, XmlElement> ReadObject(XmlElement element, string[] required, string[] optional = null)
        {
            if (element == null || TypeOf(element) != "object") throw new FormatException("Expected a JSON object.");
            var allowed = new HashSet<string>(required, StringComparer.Ordinal);
            if (optional != null) allowed.UnionWith(optional);
            var fields = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            foreach (XmlNode child in element.ChildNodes)
            {
                XmlElement field = child as XmlElement;
                if (field == null) { if (!string.IsNullOrWhiteSpace(child.Value)) throw new FormatException("Unexpected object text."); continue; }
                if (!allowed.Contains(field.Name)) throw new FormatException("Unknown JSON property: " + field.Name);
                if (fields.ContainsKey(field.Name)) throw new FormatException("Duplicate JSON property: " + field.Name);
                fields.Add(field.Name, field);
            }
            foreach (string name in required) if (!fields.ContainsKey(name)) throw new FormatException("Missing JSON property: " + name);
            return fields;
        }

        internal static List<XmlElement> ReadArray(XmlElement element, int maximum)
        {
            if (element == null || TypeOf(element) != "array") throw new FormatException("Expected a JSON array.");
            var result = new List<XmlElement>();
            foreach (XmlNode child in element.ChildNodes)
            {
                XmlElement item = child as XmlElement;
                if (item == null || item.Name != "item") throw new FormatException("Invalid JSON array element.");
                if (result.Count == maximum) throw new FormatException("JSON array exceeds its limit.");
                result.Add(item);
            }
            return result;
        }

        internal static XmlElement Required(Dictionary<string, XmlElement> fields, string key)
        { XmlElement element; if (!fields.TryGetValue(key, out element)) throw new FormatException("Missing JSON property: " + key); return element; }

        internal static string ReadString(XmlElement element)
        { if (TypeOf(element) != "string") throw new FormatException("Expected a JSON string."); return element.InnerText; }

        internal static string ReadOptionalString(Dictionary<string, XmlElement> fields, string key)
        { XmlElement element; return fields.TryGetValue(key, out element) ? ReadString(element) : ""; }

        internal static int ReadInt(XmlElement element)
        {
            int value;
            if (TypeOf(element) != "number" || !int.TryParse(element.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Expected an integer JSON number.");
            return value;
        }

        internal static bool ReadBoolean(XmlElement element)
        {
            if (TypeOf(element) != "boolean") throw new FormatException("Expected a JSON boolean.");
            if (element.InnerText == "true") return true;
            if (element.InnerText == "false") return false;
            throw new FormatException("Invalid JSON boolean.");
        }

        internal static double ReadDouble(XmlElement element)
        {
            double value;
            if (TypeOf(element) != "number" || !double.TryParse(element.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("Expected a finite JSON number.");
            return value;
        }

        private static List<string> ThrowIfInvalid(List<string> errors)
        { if (errors.Count > 0) throw new FormatException(string.Join(" ", errors)); return errors; }

        private static void RequireText(string value, string field, int maximum, List<string> errors)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) errors.Add(field + " must be nonempty and within its character limit."); }

        internal static string TypeOf(XmlElement element) { return element.GetAttribute("type"); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
