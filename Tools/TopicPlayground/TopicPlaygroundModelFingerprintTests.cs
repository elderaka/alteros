using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;

public sealed class TopicPlaygroundModelFingerprintTests
{
    [Test]
    public void RepeatedDisplayReadsReuseTheCachedFingerprintWithoutReadingTheFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AlterOS-TopicPlayground-Fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "model.onnx");
        const string contents = "alteros model version one";
        File.WriteAllText(path, contents, new UTF8Encoding(false));

        Type fingerprintType = typeof(TopicPlaygroundModelFingerprintTests).Assembly
            .GetType("AlterOS.Editor.TopicPlaygroundModelFingerprint", false);
        Assert.NotNull(fingerprintType, "The Editor fingerprint cache should expose a testable cached display value.");

        var scheduled = new Queue<Action>();
        int hashCalls = 0;
        Action<Action> scheduler = action => scheduled.Enqueue(action);
        Func<string, CancellationToken, string> hasher = (filePath, cancellationToken) =>
        {
            hashCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha256 = SHA256.Create())
                return ToLowerHex(sha256.ComputeHash(stream));
        };

        object fingerprint = null;
        try
        {
            ConstructorInfo constructor = fingerprintType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(Action<Action>), typeof(Func<string, CancellationToken, string>) },
                null);
            Assert.NotNull(constructor, "The cache should keep its scheduler/hasher injection seam private.");
            fingerprint = constructor.Invoke(new object[] { path, scheduler, hasher });
            PropertyInfo displayValue = fingerprintType.GetProperty("DisplayValue", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(displayValue);

            Assert.AreEqual("Calculating...", displayValue.GetValue(fingerprint, null));
            for (int i = 0; i < 100; i++)
                Assert.AreEqual("Calculating...", displayValue.GetValue(fingerprint, null));
            Assert.AreEqual(0, hashCalls, "Reading the display while pending must not run the hasher.");
            Assert.AreEqual(1, scheduled.Count, "Display reads must not enqueue duplicate work.");

            scheduled.Dequeue().Invoke();
            string expected;
            using (SHA256 sha256 = SHA256.Create())
                expected = ToLowerHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(contents)));
            string completed = (string)displayValue.GetValue(fingerprint, null);
            Assert.AreEqual(expected, completed);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Write | FileShare.Delete))
            {
                for (int i = 0; i < 100; i++)
                    Assert.AreEqual(completed, displayValue.GetValue(fingerprint, null));
            }

            Assert.AreEqual(1, hashCalls, "Repeated display reads after completion must not reopen or rehash the model file.");
            Assert.AreEqual(0, scheduled.Count);
        }
        finally
        {
            if (fingerprint != null)
                fingerprintType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)?.Invoke(fingerprint, null);
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Test]
    public void HashFailureIsVisibleAndInvalidationRetriesTheChangedFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AlterOS-TopicPlayground-Fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "model.onnx");
        File.WriteAllText(path, "alteros model before retry", new UTF8Encoding(false));

        Type fingerprintType = typeof(TopicPlaygroundModelFingerprintTests).Assembly
            .GetType("AlterOS.Editor.TopicPlaygroundModelFingerprint", false);
        Assert.NotNull(fingerprintType, "The Editor fingerprint cache should expose a testable invalidation path.");

        var scheduled = new Queue<Action>();
        int hashCalls = 0;
        bool fail = true;
        Action<Action> scheduler = action => scheduled.Enqueue(action);
        Func<string, CancellationToken, string> hasher = (filePath, cancellationToken) =>
        {
            hashCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (fail)
                throw new IOException("fixture read failed");
            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha256 = SHA256.Create())
                return ToLowerHex(sha256.ComputeHash(stream));
        };

        object fingerprint = null;
        try
        {
            ConstructorInfo constructor = fingerprintType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(Action<Action>), typeof(Func<string, CancellationToken, string>) },
                null);
            Assert.NotNull(constructor);
            fingerprint = constructor.Invoke(new object[] { path, scheduler, hasher });
            PropertyInfo displayValue = fingerprintType.GetProperty("DisplayValue", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(displayValue);

            scheduled.Dequeue().Invoke();
            Assert.AreEqual("Unavailable: fixture read failed", displayValue.GetValue(fingerprint, null));
            for (int i = 0; i < 25; i++)
                Assert.AreEqual("Unavailable: fixture read failed", displayValue.GetValue(fingerprint, null));
            Assert.AreEqual(1, hashCalls, "Reading a failure must not silently retry file I/O.");

            fail = false;
            File.WriteAllText(path, "alteros model after retry", new UTF8Encoding(false));
            fingerprintType.GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.Public).Invoke(fingerprint, null);
            Assert.AreEqual("Calculating...", displayValue.GetValue(fingerprint, null));
            Assert.AreEqual(1, scheduled.Count);
            scheduled.Dequeue().Invoke();

            string expected;
            using (SHA256 sha256 = SHA256.Create())
                expected = ToLowerHex(sha256.ComputeHash(Encoding.UTF8.GetBytes("alteros model after retry")));
            Assert.AreEqual(expected, displayValue.GetValue(fingerprint, null));
            Assert.AreEqual(2, hashCalls);
        }
        finally
        {
            if (fingerprint != null)
                fingerprintType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)?.Invoke(fingerprint, null);
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    static string ToLowerHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }
}
