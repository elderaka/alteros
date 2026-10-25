using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.ContentWorkbench
{
    internal static class WorkbenchItemPreview
    {
        public static WorkbenchResult Run(WorkbenchPreviewSnapshot snapshot, string fixtureId)
        {
            ItemRecord[] matches = (snapshot.Package.items ?? new List<ItemRecord>())
                .Where(item => item != null && item.id == snapshot.RootSourceId).ToArray();
            if (matches.Length != 1)
                return WorkbenchResult.Failure("invalid", "preview_item_projection_missing", "/document/id",
                    "The detached package does not contain exactly one imported Item definition for this source.");

            ItemRecord item = matches[0];
            TextRecord text = (snapshot.Package.texts ?? new List<TextRecord>()).FirstOrDefault(candidate => candidate != null &&
                item.displayTextRef != null && candidate.id == item.displayTextRef.id && candidate.revision == item.displayTextRef.revision);
            ContentResult<ContentTextResolution> display = ContentLanguageResolver.Resolve(text, "en", "ja");
            if (!display.IsReady)
            {
                ContentDiagnostic first = display.diagnostics.FirstOrDefault();
                return WorkbenchResult.Failure("invalid", first == null ? "preview_item_text_unavailable" : first.code,
                    first == null ? "/document/displayName" : first.path,
                    first == null ? "The exact Item display Text could not be resolved." : first.message);
            }

            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kindId", "item" }, { "fixtureId", fixtureId }, { "fixtureOnly", true },
                { "rootIsSynthetic", snapshot.RootIsSynthetic }, { "sourceSnapshot", snapshot.SourceSnapshot },
                { "ephemeralReleaseId", snapshot.Release.releaseId },
                { "item", new Dictionary<string, object>
                    { { "id", item.id }, { "revision", item.revision }, { "expectedHash", item.semanticHash },
                      { "displayName", display.value.displayText }, { "speechName", display.value.speechText },
                      { "stackLimit", item.stackLimit }, { "displayTextRef", ContentRefView(item.displayTextRef) },
                      { "fixtureQuantityChanges", new List<object>() } } },
                { "providerCalls", 0 }, { "ttsCalls", 0 },
            });
        }

        internal static object ContentRefView(ContentRef reference)
        {
            return reference == null ? null : new Dictionary<string, object>
            {
                { "store", reference.store }, { "id", reference.id }, { "revision", reference.revision },
                { "expectedHash", reference.expectedHash }
            };
        }
    }
}
