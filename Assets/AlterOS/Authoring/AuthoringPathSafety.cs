using System;
using System.IO;

namespace AlterOS.Authoring
{
    public static class AuthoringPathSafety
    {
        public static bool ContainsReparsePoint(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string current = Path.GetPathRoot(fullPath);
                if (string.IsNullOrEmpty(current)) return true;
                string[] components = fullPath.Substring(current.Length).Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (PathHasReparsePoint(current)) return true;
                foreach (string component in components)
                {
                    current = Path.Combine(current, component);
                    if (PathHasReparsePoint(current)) return true;
                    if (!File.Exists(current) && !Directory.Exists(current)) break;
                }
                return false;
            }
            catch { return true; }
        }

        private static bool PathHasReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
    }
}
