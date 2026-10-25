using System;
using System.IO;
using System.Text;

namespace AlterOS.Memory
{
    /// <summary>Nonpersonal per-install marker that online turns must enforce memory suppression.</summary>
    public sealed class PlayerMemoryPolicyFlag
    {
        private readonly string markerPath;

        public PlayerMemoryPolicyFlag(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath) || !Path.IsPathRooted(persistentDataPath))
                throw new ArgumentException("Persistent data path must be absolute.", nameof(persistentDataPath));
            markerPath = Path.Combine(Path.GetFullPath(persistentDataPath), "Memory", "memory-policy-required.flag");
        }

        public bool IsRequired
        {
            get { return File.Exists(markerPath); }
        }

        public void MarkRequired()
        {
            string directory = Path.GetDirectoryName(markerPath);
            Directory.CreateDirectory(directory);
            string temporary = markerPath + ".tmp";
            try
            {
                File.WriteAllText(temporary, "required\n", new UTF8Encoding(false));
                if (File.Exists(markerPath)) File.Delete(temporary);
                else File.Move(temporary, markerPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
