using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AlterOS.Conversation
{
    internal sealed class TranscriptSourceSnapshot
    {
        internal string sourceHash;
        internal long byteCount;
        internal long recordCount;
        internal long nextSequence;
    }

    public static class TranscriptJsonlImporter
    {
        private const long MaximumSourceBytes = 1024L * 1024L * 1024L;
        private const int BackupChunkBytes = 64 * 1024;

        public static ConversationTranscriptImportResult ImportOnce(string legacyPath, SqliteTranscriptStore store)
        { return ImportOnce(legacyPath, store, OpenSource); }

        internal static ConversationTranscriptImportResult ImportOnce(string legacyPath, SqliteTranscriptStore store,
            Func<string, Stream> sourceReader)
        {
            if (store == null) return new ConversationTranscriptImportResult { State = "recovery_required", Error = "The private transcript store is unavailable." };
            if (sourceReader == null) return new ConversationTranscriptImportResult { State = "recovery_required", Error = "The transcript source reader is unavailable." };
            ConversationTranscriptImportResult beforeRead = store.CheckImportState();
            if (beforeRead.State != "missing") return beforeRead;
            if (string.IsNullOrWhiteSpace(legacyPath)) return store.ImportMissing();
            string fullPath;
            try { fullPath = Path.GetFullPath(legacyPath); }
            catch (Exception exception) { return Failure(exception); }
            if (!File.Exists(fullPath))
            {
                if (Directory.Exists(fullPath)) return new ConversationTranscriptImportResult { State = "recovery_required", Error = "The transcript source path is a directory." };
                return store.ImportMissing();
            }
            try
            {
                TranscriptSourceSnapshot snapshot = Scan(fullPath, null, null, sourceReader);
                return store.ImportLegacy(fullPath, snapshot, sourceReader);
            }
            catch (FormatException exception)
            {
                return new ConversationTranscriptImportResult { State = "recovery_required", Error = "transcript_corrupt: " + exception.Message };
            }
            catch (Exception exception)
            {
                return Failure(exception);
            }
        }

        internal static TranscriptSourceSnapshot CopyAndVerify(string path, TranscriptSourceSnapshot expected,
            Action<ConversationTranscriptDiskRecord> consumeRecord, Action<byte[], int> consumeSourceChunk,
            Func<string, Stream> sourceReader)
        {
            if (expected == null) throw new ArgumentNullException("expected");
            TranscriptSourceSnapshot actual = Scan(path, consumeRecord, consumeSourceChunk, sourceReader);
            if (actual.byteCount != expected.byteCount || actual.recordCount != expected.recordCount ||
                !string.Equals(actual.sourceHash, expected.sourceHash, StringComparison.Ordinal))
                throw new IOException("Transcript source changed between validation and import.");
            return actual;
        }

        private static TranscriptSourceSnapshot Scan(string path, Action<ConversationTranscriptDiskRecord> consumeRecord,
            Action<byte[], int> consumeSourceChunk, Func<string, Stream> sourceReader)
        {
            using (Stream stream = sourceReader(path))
            using (SHA256 hash = SHA256.Create())
            {
                if (stream == null || !stream.CanRead) throw new IOException("Transcript source reader did not return a readable stream.");
                if (stream.Length > MaximumSourceBytes) throw new InvalidDataException("Transcript source exceeds its import limit.");
                var line = new MemoryStream();
                var buffer = new byte[8192];
                var sourceChunk = new byte[BackupChunkBytes];
                int sourceChunkLength = 0;
                long sourceBytes = 0;
                long recordCount = 0;
                long expectedSequence = 1;
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sourceBytes += bytesRead;
                    if (sourceBytes > MaximumSourceBytes) throw new InvalidDataException("Transcript source exceeds its import limit.");
                    hash.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    for (int index = 0; index < bytesRead; index++)
                    {
                        byte current = buffer[index];
                        if (consumeSourceChunk != null)
                        {
                            sourceChunk[sourceChunkLength++] = current;
                            if (sourceChunkLength == sourceChunk.Length)
                            {
                                consumeSourceChunk(sourceChunk, sourceChunkLength);
                                sourceChunk = new byte[BackupChunkBytes];
                                sourceChunkLength = 0;
                            }
                        }
                        if (current != (byte)'\n')
                        {
                            if (line.Length >= ConversationTranscript.MaximumEventBytes)
                                throw new FormatException("Transcript line exceeds its byte limit.");
                            line.WriteByte(current);
                            continue;
                        }
                        if (line.Length > 0 && line.GetBuffer()[line.Length - 1] == (byte)'\r') line.SetLength(line.Length - 1);
                        if (line.Length == 0) throw new FormatException("Transcript contains an empty record.");
                        ConversationTranscriptDiskRecord record = ConversationTranscript.DecodeLegacyLine(line.ToArray());
                        if (record.sequence != expectedSequence++) throw new FormatException("Transcript sequence is invalid.");
                        if (consumeRecord != null) consumeRecord(record);
                        recordCount++;
                        line.SetLength(0);
                    }
                }
                if (line.Length != 0) throw new FormatException("Transcript ends with an incomplete line.");
                hash.TransformFinalBlock(new byte[0], 0, 0);
                if (consumeSourceChunk != null && sourceChunkLength > 0)
                    consumeSourceChunk(sourceChunk, sourceChunkLength);
                return new TranscriptSourceSnapshot
                {
                    sourceHash = ToHex(hash.Hash),
                    byteCount = sourceBytes,
                    recordCount = recordCount,
                    nextSequence = expectedSequence
                };
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static ConversationTranscriptImportResult Failure(Exception exception)
        { return new ConversationTranscriptImportResult { State = "recovery_required", Error = SafeMessage(exception) }; }
        private static string SafeMessage(Exception exception)
        { return exception == null ? "Transcript import failed." : exception.Message; }

        private static Stream OpenSource(string path)
        { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
    }
}
