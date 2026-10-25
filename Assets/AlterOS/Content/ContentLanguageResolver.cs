using System;

namespace AlterOS.Content
{
    /// <summary>Resolves stored translations without inventing a speech-language fallback.</summary>
    public static class ContentLanguageResolver
    {
        public static ContentResult<ContentTextResolution> Resolve(ContentRepository repository, ContentRef reference,
            string displayLanguage, string speechLanguage)
        {
            if (repository == null)
                return ContentResult<ContentTextResolution>.Failure(ContentStatuses.Invalid, "content_repository_required", "", "A content repository is required.");

            ContentResult<TextRecord> read = repository.ReadText(reference);
            if (!read.IsReady)
            {
                string code = "text_read_failed";
                string message = "The exact Text revision could not be read.";
                if (read.diagnostics.Count > 0)
                {
                    code = read.diagnostics[0].code;
                    message = read.diagnostics[0].message;
                }
                return ContentResult<ContentTextResolution>.Failure(read.status, code,
                    reference == null ? "" : reference.id, message);
            }

            return Resolve(read.value, displayLanguage, speechLanguage);
        }

        public static ContentResult<ContentTextResolution> Resolve(TextRecord record,
            string displayLanguage, string speechLanguage)
        {
            if (record == null)
                return ContentResult<ContentTextResolution>.Failure(ContentStatuses.Invalid,
                    "content_text_required", "text", "A Text record is required.");
            string display = Normalize(displayLanguage);
            string speech = Normalize(speechLanguage);
            string displayText = Find(record, display);
            string resolvedDisplayLanguage = display;
            if (displayText == null)
            {
                displayText = Find(record, "en");
                resolvedDisplayLanguage = displayText == null ? "" : "en";
            }
            if (displayText == null)
                return ContentResult<ContentTextResolution>.Failure(ContentStatuses.Unavailable,
                    "display_translation_unavailable", record.id,
                    "The requested display translation and the English display fallback are unavailable.");

            string speechText = Find(record, speech);
            if (speechText == null) speechText = string.Empty;
            return ContentResult<ContentTextResolution>.Ready(new ContentTextResolution(
                ContentStatuses.Ready, displayText, speechText, resolvedDisplayLanguage,
                speechText.Length == 0 ? "" : speech));
        }

        public static string Normalize(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return string.Empty;
            string value = language.Trim().ToLowerInvariant();
            if (value == "english") return "en";
            if (value == "japanese") return "ja";
            return value;
        }

        private static string Find(TextRecord record, string language)
        {
            if (record == null || record.translations == null || string.IsNullOrEmpty(language)) return null;
            foreach (TranslationRecord translation in record.translations)
                if (translation != null && string.Equals(translation.language, language, StringComparison.Ordinal))
                    return translation.text;
            return null;
        }
    }
}
