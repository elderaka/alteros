using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace AlterOS
{
    public static class TTSCacheUtility
    {
        private static readonly string[] PreferredExtensions = { ".ogg", ".mp3", ".wav" };
        public const string PrebakedDirectoryName = "TTSPrebaked";
        public const string RuntimeCacheDirectoryName = "TTSCache";

        public static bool TryFindCachedClip(string cacheDir, string cacheKey, out string path, out AudioType audioType)
        {
            foreach (string extension in PreferredExtensions)
            {
                string candidate = Path.Combine(cacheDir, cacheKey + extension);
                if (!File.Exists(candidate))
                {
                    continue;
                }

                path = candidate;
                audioType = GetAudioTypeFromExtension(extension);
                return true;
            }

            path = null;
            audioType = AudioType.UNKNOWN;
            return false;
        }

        public static bool TryFindCachedClipInDirectories(string cacheKey, out string path, out AudioType audioType, params string[] cacheDirs)
        {
            foreach (string cacheDir in cacheDirs)
            {
                if (string.IsNullOrWhiteSpace(cacheDir) || !Directory.Exists(cacheDir))
                {
                    continue;
                }

                if (TryFindCachedClip(cacheDir, cacheKey, out path, out audioType))
                {
                    return true;
                }
            }

            path = null;
            audioType = AudioType.UNKNOWN;
            return false;
        }

        public static string BuildCachePath(string cacheDir, string cacheKey, string extension)
        {
            string normalizedExtension = NormalizeExtension(extension);
            return Path.Combine(cacheDir, cacheKey + normalizedExtension);
        }

        public static AudioType GetAudioTypeFromPath(string path)
        {
            return GetAudioTypeFromExtension(Path.GetExtension(path));
        }

        public static AudioType GetAudioTypeFromExtension(string extension)
        {
            switch (NormalizeExtension(extension))
            {
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".mp3":
                    return AudioType.MPEG;
                case ".wav":
                    return AudioType.WAV;
                default:
                    return AudioType.UNKNOWN;
            }
        }

        public static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return ".wav";
            }

            string normalized = extension.Trim().ToLowerInvariant();
            return normalized.StartsWith(".") ? normalized : "." + normalized;
        }

        public static void PruneOldCacheFiles(string cacheDir, long maxBytes)
        {
            if (maxBytes <= 0 || !Directory.Exists(cacheDir))
            {
                return;
            }

            DirectoryInfo directory = new DirectoryInfo(cacheDir);
            if (string.Equals(directory.Name, PrebakedDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[TTSCacheUtility] Refusing to prune prebaked dialogue directory '{cacheDir}'.");
                return;
            }

            FileInfo[] files = directory.GetFiles();
            Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

            long totalBytes = 0;
            foreach (FileInfo file in files)
            {
                totalBytes += file.Length;
            }

            if (totalBytes <= maxBytes)
            {
                return;
            }

            foreach (FileInfo file in files)
            {
                try
                {
                    totalBytes -= file.Length;
                    file.Delete();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TTSCacheUtility] Failed to prune '{file.FullName}': {ex.Message}");
                }

                if (totalBytes <= maxBytes)
                {
                    break;
                }
            }
        }
    }
}
