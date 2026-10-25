using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace AlterOS.Security
{
    public enum OpenRouterCredentialStatus
    {
        Missing,
        EnvironmentOverride,
        Saved,
        SavedUnavailable
    }

    public static class OpenRouterCredentialStore
    {
        public const string EnvironmentVariableName = "OPENROUTER_API_KEY";

        private const string FileName = "openrouter-api-key.bin";

        public static string ResolveApiKey(string environmentKey, string storedKey)
        {
            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                return environmentKey.Trim();
            }

            return string.IsNullOrWhiteSpace(storedKey) ? string.Empty : storedKey.Trim();
        }

        public static string GetApiKey()
        {
            string environmentKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (!string.IsNullOrWhiteSpace(environmentKey)) return environmentKey.Trim();
            return LoadUserApiKey();
        }

        public static OpenRouterCredentialStatus GetStatus()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName)))
                return OpenRouterCredentialStatus.EnvironmentOverride;

            string path = Path.Combine(Application.persistentDataPath, FileName);
            if (!File.Exists(path)) return OpenRouterCredentialStatus.Missing;
            return string.IsNullOrWhiteSpace(LoadUserApiKey())
                ? OpenRouterCredentialStatus.SavedUnavailable
                : OpenRouterCredentialStatus.Saved;
        }

        public static bool SaveUserApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return false;
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                byte[] plaintext = Encoding.UTF8.GetBytes(apiKey.Trim());
                try
                {
                    byte[] protectedBytes = ProtectWithWindowsDpapi(plaintext);
                    string directory = Application.persistentDataPath;
                    Directory.CreateDirectory(directory);
                    WriteAtomically(Path.Combine(directory, FileName), protectedBytes);
                    return true;
                }
                finally
                {
                    Array.Clear(plaintext, 0, plaintext.Length);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AlterOS] Could not save the OpenRouter key securely: {exception.Message}");
                return false;
            }
#else
            Debug.LogError("[AlterOS] Secure OpenRouter key storage is not implemented on this platform.");
            return false;
#endif
        }

        private static void WriteAtomically(string path, byte[] contents)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public static void ClearUserApiKey()
        {
            string path = Path.Combine(Application.persistentDataPath, FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static bool HasUserApiKey()
        {
            return !string.IsNullOrWhiteSpace(LoadUserApiKey());
        }

        private static string LoadUserApiKey()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string path = Path.Combine(Application.persistentDataPath, FileName);
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                byte[] protectedBytes = File.ReadAllBytes(path);
                byte[] plaintext = UnprotectWithWindowsDpapi(protectedBytes);
                try
                {
                    return Encoding.UTF8.GetString(plaintext);
                }
                finally
                {
                    Array.Clear(plaintext, 0, plaintext.Length);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AlterOS] Could not read the stored OpenRouter key: {exception.Message}");
                return string.Empty;
            }
#else
            return string.Empty;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        private const uint CryptProtectUiForbidden = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            uint flags,
            ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            uint flags,
            ref DataBlob dataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] ProtectWithWindowsDpapi(byte[] plaintext)
        {
            return TransformWithWindowsDpapi(plaintext, ProtectTransform);
        }

        private static byte[] UnprotectWithWindowsDpapi(byte[] protectedBytes)
        {
            return TransformWithWindowsDpapi(protectedBytes, UnprotectTransform);
        }

        private static bool ProtectTransform(ref DataBlob input, ref DataBlob output)
        {
            return CryptProtectData(
                ref input,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                CryptProtectUiForbidden,
                ref output);
        }

        private static bool UnprotectTransform(ref DataBlob input, ref DataBlob output)
        {
            return CryptUnprotectData(
                ref input,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                CryptProtectUiForbidden,
                ref output);
        }

        private static byte[] TransformWithWindowsDpapi(byte[] input, DpapiTransform transform)
        {
            IntPtr inputMemory = Marshal.AllocHGlobal(input.Length);
            DataBlob inputBlob = new DataBlob
            {
                cbData = input.Length,
                pbData = inputMemory
            };
            DataBlob outputBlob = default(DataBlob);

            try
            {
                Marshal.Copy(input, 0, inputMemory, input.Length);
                if (!transform(ref inputBlob, ref outputBlob))
                {
                    throw new InvalidOperationException($"Windows DPAPI failed with error {Marshal.GetLastWin32Error()}.");
                }

                byte[] output = new byte[outputBlob.cbData];
                Marshal.Copy(outputBlob.pbData, output, 0, output.Length);
                return output;
            }
            finally
            {
                Marshal.FreeHGlobal(inputMemory);
                if (outputBlob.pbData != IntPtr.Zero)
                {
                    LocalFree(outputBlob.pbData);
                }
            }
        }

        private delegate bool DpapiTransform(ref DataBlob input, ref DataBlob output);
#endif
    }
}
