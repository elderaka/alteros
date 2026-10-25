using System;
using System.IO;
using System.Text;

namespace AlterOS.Recording
{
    /// <summary>Manual local batch preview, confirmation and export. It never sends data.</summary>
    public sealed class ContributionPreview
    {
        private readonly InteractionRecorder owner;

        internal ContributionPreview(InteractionRecorder recorder) { owner = recorder; }

        public ContributionPreviewResult Create(ContributionSelection selection)
        {
            lock (owner.Gate)
            {
                if (owner.ActiveRepository == null || owner.ActivePolicy == null)
                    return new ContributionPreviewResult { state = "disabled", errorCode = "recording_disabled", warning = string.Empty };
                try
                {
                    DateTime now = owner.NowUtc;
                    owner.ActiveRepository.PruneExpired(now);
                    return owner.ActiveRepository.CreatePreview(selection, owner.ActivePolicy.captureRawText, now);
                }
                catch (Exception exception)
                { return new ContributionPreviewResult { state = "unavailable", errorCode = "preview_failed", message = exception.GetType().Name + ": " + exception.Message, warning = string.Empty }; }
            }
        }

        public ContributionResult Confirm(string previewId, string expectedHash)
        {
            lock (owner.Gate)
            {
                if (owner.ActiveRepository == null)
                    return new ContributionResult { state = "disabled", errorCode = "recording_disabled" };
                try { return owner.ActiveRepository.Confirm(previewId, expectedHash, owner.NowUtc); }
                catch (Exception exception)
                { return new ContributionResult { state = "unavailable", errorCode = "confirmation_failed", message = exception.GetType().Name + ": " + exception.Message }; }
            }
        }

        public ContributionExportResult ExportConfirmed(string contributionId, string trustedPath)
        {
            lock (owner.Gate)
            {
                if (owner.ActiveRepository == null)
                    return new ContributionExportResult { state = "disabled", errorCode = "recording_disabled" };
                if (string.IsNullOrWhiteSpace(trustedPath) || !Path.IsPathRooted(trustedPath))
                    return new ContributionExportResult { state = "invalid", errorCode = "trusted_path_required" };
                string fullPath;
                try { fullPath = Path.GetFullPath(trustedPath); }
                catch (Exception exception)
                { return new ContributionExportResult { state = "invalid", errorCode = "trusted_path_invalid", message = exception.Message }; }
                if (File.Exists(fullPath))
                    return new ContributionExportResult { state = "conflict", contributionId = contributionId, errorCode = "export_exists" };

                string contentHash;
                string document;
                try { document = owner.ActiveRepository.ReadConfirmedExport(contributionId, out contentHash); }
                catch (Exception exception)
                { return new ContributionExportResult { state = "unavailable", errorCode = "export_read_failed", message = exception.GetType().Name + ": " + exception.Message }; }
                if (document == null)
                    return new ContributionExportResult { state = "invalid", contributionId = contributionId, errorCode = "contribution_not_confirmed" };

                string directory = Path.GetDirectoryName(fullPath);
                string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    if (string.IsNullOrEmpty(directory)) throw new IOException("The export parent directory is unavailable.");
                    Directory.CreateDirectory(directory);
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] bytes = new UTF8Encoding(false).GetBytes(document);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    File.Move(temporary, fullPath);
                    try { owner.ActiveRepository.MarkExported(contributionId, owner.NowUtc); }
                    catch { /* The local file is already exported; this is not a delivery receipt. */ }
                    return new ContributionExportResult
                    {
                        state = "exported", contributionId = contributionId, path = fullPath, contentHash = contentHash
                    };
                }
                catch (Exception exception)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                    return new ContributionExportResult
                    { state = "unavailable", contributionId = contributionId, errorCode = "export_write_failed", message = exception.GetType().Name + ": " + exception.Message };
                }
            }
        }
    }
}
