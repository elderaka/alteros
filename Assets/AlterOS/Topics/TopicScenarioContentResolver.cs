using System;
using System.Linq;
using AlterOS.Content;

namespace AlterOS.Topics
{
    /// <summary>Resolves a semantic Topic ID only inside the host-selected compiled release.</summary>
    public static class TopicScenarioContentResolver
    {
        public static ContentResult<ContentRef> ResolveTopicReference(ContentRelease release,
            string storeId, string topicId)
        {
            if (release == null || string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(topicId))
                return ContentResult<ContentRef>.Failure(ContentStatuses.Invalid, "topic_reference_request_invalid",
                    topicId ?? string.Empty, "An exact selected release, store ID and Topic ID are required.");
            ContentReleaseDependency[] matches = (release.dependencies ?? new System.Collections.Generic.List<ContentReleaseDependency>())
                .Where(item => item != null && item.kind == "topic" && item.store == storeId && item.id == topicId).ToArray();
            if (matches.Length == 0)
                return ContentResult<ContentRef>.Failure(ContentStatuses.Missing, "topic_release_dependency_missing",
                    topicId, "The selected release does not contain this Topic.");
            if (matches.Length != 1 || !IsSha256(matches[0].revision) || !IsSha256(matches[0].expectedHash))
                return ContentResult<ContentRef>.Failure(ContentStatuses.Incompatible, "topic_release_dependency_ambiguous",
                    topicId, "The selected release must pin exactly one complete Topic reference.");
            ContentReleaseDependency dependency = matches[0];
            return ContentResult<ContentRef>.Ready(new ContentRef(dependency.store, dependency.id,
                dependency.revision, dependency.expectedHash));
        }

        public static bool SameScenarioReference(ContentRef selected, ContentRef compiled)
        {
            return selected != null && compiled != null && selected.store == compiled.store &&
                selected.id == compiled.id && selected.revision == compiled.revision &&
                selected.expectedHash == compiled.expectedHash;
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'))) return false;
            return true;
        }
    }
}
