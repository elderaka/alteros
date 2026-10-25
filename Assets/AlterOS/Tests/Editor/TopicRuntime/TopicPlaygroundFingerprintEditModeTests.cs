using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Editor;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlterOS.Tests
{
    public sealed class TopicPlaygroundFingerprintEditModeTests
    {
        [UnityTest]
        public IEnumerator CachedDisplayInvalidationAndFailureStayNonBlocking()
        {
            string directory = Path.Combine(Path.GetTempPath(), "AlterOS-TopicPlayground-EditMode-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "model.onnx");
            Write(path, "edit-mode-model-v1");
            var fingerprint = new TopicPlaygroundModelFingerprint(path);

            try
            {
                for (int i = 0; i < 100; i++)
                    Assert.IsNotEmpty(fingerprint.DisplayValue);

                yield return new UnityEngine.WaitUntil(() => !fingerprint.IsCalculating);
                string firstHash = fingerprint.Fingerprint;
                Assert.AreEqual(Hash("edit-mode-model-v1"), firstHash);

                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Write | FileShare.Delete))
                {
                    for (int i = 0; i < 100; i++)
                        Assert.AreEqual(firstHash, fingerprint.DisplayValue);
                }

                Write(path, "edit-mode-model-v2");
                fingerprint.Invalidate();
                yield return new UnityEngine.WaitUntil(() => !fingerprint.IsCalculating);
                Assert.AreEqual(Hash("edit-mode-model-v2"), fingerprint.Fingerprint);

                File.Delete(path);
                fingerprint.Invalidate();
                yield return new UnityEngine.WaitUntil(() => !fingerprint.IsCalculating);
                StringAssert.StartsWith("Unavailable: ", fingerprint.DisplayValue);

                Write(path, "edit-mode-model-v3");
                fingerprint.Invalidate();
                yield return new UnityEngine.WaitUntil(() => !fingerprint.IsCalculating);
                Assert.AreEqual(Hash("edit-mode-model-v3"), fingerprint.Fingerprint);
            }
            finally
            {
                fingerprint.Dispose();
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        static void Write(string path, string value)
        {
            File.WriteAllText(path, value, new UTF8Encoding(false));
        }

        static string Hash(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
                return System.BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
