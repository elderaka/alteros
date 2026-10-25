using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace AlterOS.Editor
{
    public readonly struct TopicPlaygroundModelFingerprintSnapshot
    {
        public readonly string Fingerprint;
        public readonly string DisplayValue;
        public readonly bool IsCalculating;

        internal TopicPlaygroundModelFingerprintSnapshot(string fingerprint, string displayValue, bool isCalculating)
        {
            Fingerprint = fingerprint;
            DisplayValue = displayValue;
            IsCalculating = isCalculating;
        }
    }

    public sealed class TopicPlaygroundModelFingerprint : IDisposable
    {
        public const string AssetPath = "Assets/Resources/AlterOS/Semantics/Models/gte-multilingual-base-fp16.onnx";

        const int BufferSize = 1024 * 1024;

        readonly object m_Gate = new object();
        readonly string m_Path;
        readonly Action<Action> m_Scheduler;
        readonly Func<string, CancellationToken, string> m_Hasher;
        CancellationTokenSource m_Cancellation;
        string m_Fingerprint;
        string m_Error;
        int m_Generation;
        bool m_IsHashing;
        bool m_RefreshPending;
        bool m_Disposed;

        public TopicPlaygroundModelFingerprint(string path)
            : this(path, ScheduleBackground, ComputeSha256)
        {
        }

        internal TopicPlaygroundModelFingerprint(
            string path,
            Action<Action> scheduler,
            Func<string, CancellationToken, string> hasher)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A model file path is required.", nameof(path));
            m_Path = Path.GetFullPath(path);
            m_Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_Hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));

            lock (m_Gate)
                StartHashLocked();
        }

        public string Fingerprint
        {
            get
            {
                lock (m_Gate)
                    return m_Fingerprint ?? string.Empty;
            }
        }

        public string DisplayValue
        {
            get
            {
                lock (m_Gate)
                    return GetDisplayValueLocked();
            }
        }

        public TopicPlaygroundModelFingerprintSnapshot Snapshot
        {
            get
            {
                lock (m_Gate)
                    return new TopicPlaygroundModelFingerprintSnapshot(
                        m_Fingerprint ?? string.Empty,
                        GetDisplayValueLocked(),
                        m_IsHashing || m_RefreshPending);
            }
        }

        public bool IsCalculating
        {
            get
            {
                lock (m_Gate)
                    return m_IsHashing || m_RefreshPending;
            }
        }

        public void Invalidate()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                    return;

                m_Generation++;
                m_Fingerprint = null;
                m_Error = null;
                if (m_IsHashing)
                {
                    m_RefreshPending = true;
                    m_Cancellation?.Cancel();
                    return;
                }

                StartHashLocked();
            }
        }

        void StartHashLocked()
        {
            if (m_Disposed || m_IsHashing)
                return;

            m_IsHashing = true;
            m_RefreshPending = false;
            m_Fingerprint = null;
            m_Error = null;
            m_Cancellation = new CancellationTokenSource();
            int generation = m_Generation;
            CancellationToken token = m_Cancellation.Token;
            try
            {
                m_Scheduler(() => ComputeAndPublish(generation, token));
            }
            catch (Exception exception)
            {
                m_IsHashing = false;
                m_Cancellation.Dispose();
                m_Cancellation = null;
                m_Error = exception.Message;
            }
        }

        void ComputeAndPublish(int generation, CancellationToken token)
        {
            string fingerprint = null;
            string error = null;
            try
            {
                token.ThrowIfCancellationRequested();
                fingerprint = m_Hasher(m_Path, token);
                if (string.IsNullOrWhiteSpace(fingerprint))
                    throw new InvalidDataException("The model fingerprint was empty.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            CancellationTokenSource completedCancellation;
            lock (m_Gate)
            {
                m_IsHashing = false;
                completedCancellation = m_Cancellation;
                m_Cancellation = null;

                if (!m_Disposed && generation == m_Generation && !token.IsCancellationRequested)
                {
                    m_Fingerprint = fingerprint;
                    m_Error = error;
                }

                if (!m_Disposed && m_RefreshPending)
                    StartHashLocked();
            }

            completedCancellation?.Dispose();
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                m_Fingerprint = null;
                m_Error = null;
                m_RefreshPending = false;
                m_Cancellation?.Cancel();
            }
        }

        string GetDisplayValueLocked()
        {
            if (!string.IsNullOrEmpty(m_Fingerprint))
                return m_Fingerprint;
            if (!string.IsNullOrEmpty(m_Error))
                return "Unavailable: " + m_Error;
            return m_Disposed ? "Unavailable: fingerprint service closed." : "Calculating...";
        }

        static void ScheduleBackground(Action action)
        {
            if (!ThreadPool.QueueUserWorkItem(_ => action()))
                throw new InvalidOperationException("Could not queue model fingerprint work.");
        }

        static string ComputeSha256(string path, CancellationToken cancellationToken)
        {
            var before = new FileInfo(path);
            before.Refresh();
            if (!before.Exists)
                throw new FileNotFoundException("The model file is missing.", path);
            long originalLength = before.Length;
            DateTime originalWriteUtc = before.LastWriteTimeUtc;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sha256.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha256.TransformFinalBlock(new byte[0], 0, 0);

                var after = new FileInfo(path);
                after.Refresh();
                if (!after.Exists || after.Length != originalLength || after.LastWriteTimeUtc != originalWriteUtc)
                    throw new IOException("The model file changed while its fingerprint was being calculated.");

                return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
