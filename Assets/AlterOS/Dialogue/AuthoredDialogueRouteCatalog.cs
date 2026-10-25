using System;
using System.Collections.Generic;
using System.IO;
using AlterOS.Content;

namespace AlterOS.Dialogue
{
    public readonly struct AuthoredDialogueRouteEntry
    {
        public AuthoredDialogueRouteEntry(string routeId, string sequencePath)
        {
            RouteId = routeId;
            SequencePath = sequencePath;
        }

        public string RouteId { get; }
        public string SequencePath { get; }
    }

    public sealed class AuthoredDialogueRouteCatalog
    {
        private readonly Dictionary<string, List<string>> _pathsByRoute =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public AuthoredDialogueRouteCatalog(IEnumerable<AuthoredDialogueRouteEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (AuthoredDialogueRouteEntry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.RouteId) || string.IsNullOrWhiteSpace(entry.SequencePath))
                {
                    continue;
                }

                if (!_pathsByRoute.TryGetValue(entry.RouteId, out List<string> paths))
                {
                    paths = new List<string>();
                    _pathsByRoute.Add(entry.RouteId, paths);
                }

                if (!paths.Contains(entry.SequencePath))
                {
                    paths.Add(entry.SequencePath);
                }
            }

            foreach (List<string> paths in _pathsByRoute.Values)
            {
                paths.Sort(StringComparer.Ordinal);
            }
        }

        public int RouteCount => _pathsByRoute.Count;

        public IEnumerable<string> GetSequencePaths(string routeId)
        {
            List<string> paths;
            return !string.IsNullOrWhiteSpace(routeId) && _pathsByRoute.TryGetValue(routeId, out paths)
                ? new List<string>(paths).AsReadOnly()
                : (IEnumerable<string>)new string[0];
        }

        public bool TryGetSequencePath(string routeId, out string sequencePath)
        {
            sequencePath = null;
            return !string.IsNullOrWhiteSpace(routeId) &&
                   _pathsByRoute.TryGetValue(routeId, out List<string> paths) &&
                   paths.Count > 0 &&
                   (sequencePath = paths[0]) != null;
        }

        public static AuthoredDialogueRouteCatalog FromDirectory(string genericRoot, string relativeRoot = "Generic")
        {
            List<AuthoredDialogueRouteEntry> entries = new List<AuthoredDialogueRouteEntry>();
            if (!string.IsNullOrWhiteSpace(genericRoot) && Directory.Exists(genericRoot))
            {
                foreach (string filePath in Directory.GetFiles(genericRoot, "*.json", SearchOption.AllDirectories))
                {
                    string routeId = new DirectoryInfo(Path.GetDirectoryName(filePath)).Name;
                    string relativePath = relativeRoot.TrimEnd('/', '\\') + "/" + routeId + "/" + Path.GetFileName(filePath);
                    entries.Add(new AuthoredDialogueRouteEntry(routeId, relativePath.Replace('\\', '/')));
                }
            }

            return new AuthoredDialogueRouteCatalog(entries);
        }

        public static AuthoredDialogueRouteCatalog FromDocuments(IEnumerable<RuntimeContentDocument> documents,
            string relativeRoot = "Generic")
        {
            var entries = new List<AuthoredDialogueRouteEntry>();
            foreach (RuntimeContentDocument document in documents ?? Array.Empty<RuntimeContentDocument>())
            {
                if (document == null || document.kind != ContentSourceKinds.GenericDialogue ||
                    !document.relativePath.StartsWith("Generic/", StringComparison.OrdinalIgnoreCase)) continue;
                string directory = Path.GetDirectoryName(document.relativePath.Replace('/', Path.DirectorySeparatorChar));
                string routeId = Path.GetFileName(directory);
                string relativePath = relativeRoot.TrimEnd('/', '\\') + "/" + routeId + "/" + Path.GetFileName(document.relativePath);
                entries.Add(new AuthoredDialogueRouteEntry(routeId, relativePath.Replace('\\', '/')));
            }
            return new AuthoredDialogueRouteCatalog(entries);
        }
    }
}
