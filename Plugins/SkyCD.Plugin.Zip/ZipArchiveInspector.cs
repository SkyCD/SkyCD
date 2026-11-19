using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Archives;
using SkyCD.Plugin.Zip.Exceptions;

namespace SkyCD.Plugin.Zip;

/// <summary>
/// Lists and safely extracts ZIP archive entries with bounded resource usage.
/// </summary>
internal static class ZipArchiveInspector
{
    private const int CopyBufferBytes = 81_920;

    public static ArchiveInspectionResult Inspect(Stream source, ArchiveLimits limits)
    {
        limits.Validate();
        try
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var entries = new List<ArchiveEntry>(archive.Entries.Count);
            var budget = new ExtractionBudget();

            for (var index = 0; index < archive.Entries.Count; index++)
            {
                entries.Add(InspectEntry(archive.Entries[index], index + 1, limits, budget));
            }

            return new ArchiveInspectionResult { Success = true, Entries = entries };
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return new ArchiveInspectionResult { Success = false, Error = exception.Message };
        }
    }

    public static async Task<ArchiveExtractionResult> ExtractAsync(
        Stream source,
        string? targetDirectory,
        ArchiveLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits.Validate();

        var parent = string.IsNullOrWhiteSpace(targetDirectory)
            ? Path.GetTempPath()
            : Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(parent);

        var tempRoot = Path.Combine(parent, $"skycd-archive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        Stream? ownedSource = null;
        try
        {
            using var archive = OpenArchive(source, tempRoot, out ownedSource);
            var entries = new List<ArchiveExtractedEntry>(archive.Entries.Count);
            var budget = new ExtractionBudget();
            var total = archive.Entries.Count;

            for (var index = 0; index < total; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await ExtractEntryAsync(archive.Entries[index], index + 1, tempRoot, limits, budget,
                    cancellationToken).ConfigureAwait(false);
                entries.Add(entry);
                Report(progress, index + 1, total);
            }

            Report(progress, total, total);
            var inspection = new ArchiveInspectionResult
            {
                Success = true,
                Entries = entries.Select(ToEntry).ToList()
            };

            return new ArchiveExtractionResult(inspection, tempRoot, entries);
        }
        catch (OperationCanceledException)
        {
            ArchiveExtractionResult.DeleteTemporaryDirectory(tempRoot);
            throw;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            ArchiveExtractionResult.DeleteTemporaryDirectory(tempRoot);
            return ArchiveExtractionResult.Failed(exception.Message);
        }
        finally
        {
            ownedSource?.Dispose();
        }
    }

    private static ArchiveEntry InspectEntry(
        ZipArchiveEntry entry,
        int index,
        ArchiveLimits limits,
        ExtractionBudget budget)
    {
        try
        {
            var isDirectory = IsDirectoryEntry(entry);
            var kind = isDirectory ? ArchiveEntryKind.Directory : ArchiveEntryKind.File;
            var modified = TryGetModified(entry);
            if (!ZipEntryPath.TryNormalize(entry.FullName, out var normalized, out var safe))
            {
                return new ArchiveEntry(string.Empty, kind, 0, modified,
                    ArchiveEntrySkipReason.Damaged, "Entry has an empty path.");
            }

            var size = isDirectory ? 0 : TryGetLength(entry);
            if (index > limits.MaxEntries)
            {
                return new ArchiveEntry(normalized, kind, size, modified,
                    ArchiveEntrySkipReason.LimitExceeded, "Archive exceeds the maximum entry count.");
            }

            if (!safe)
            {
                return new ArchiveEntry(normalized, kind, size, modified,
                    ArchiveEntrySkipReason.UnsafePath, "Entry path would escape the extraction directory.");
            }

            if (!isDirectory && !budget.TryReserve(size, limits))
            {
                return new ArchiveEntry(normalized, kind, size, modified,
                    ArchiveEntrySkipReason.LimitExceeded, "Entry exceeds the configured archive limits.");
            }

            return new ArchiveEntry(normalized, kind, size, modified);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return new ArchiveEntry(string.Empty, ArchiveEntryKind.File, 0, null,
                ArchiveEntrySkipReason.Damaged, exception.Message);
        }
    }

    private static async Task<ArchiveExtractedEntry> ExtractEntryAsync(
        ZipArchiveEntry entry,
        int index,
        string tempRoot,
        ArchiveLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        var isDirectory = IsDirectoryEntry(entry);
        var kind = isDirectory ? ArchiveEntryKind.Directory : ArchiveEntryKind.File;
        var modified = TryGetModified(entry);

        if (!ZipEntryPath.TryNormalize(entry.FullName, out var normalized, out var safe))
        {
            return new ArchiveExtractedEntry(string.Empty, kind, 0, modified,
                ArchiveEntrySkipReason.Damaged, "Entry has an empty path.");
        }

        var length = isDirectory ? 0 : TryGetLength(entry);
        if (index > limits.MaxEntries)
        {
            return new ArchiveExtractedEntry(normalized, kind, length, modified,
                ArchiveEntrySkipReason.LimitExceeded, "Archive exceeds the maximum entry count.");
        }

        if (!safe || !ZipEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
        {
            return new ArchiveExtractedEntry(normalized, kind, length, modified,
                ArchiveEntrySkipReason.UnsafePath, "Entry path would escape the extraction directory.");
        }

        if (isDirectory)
        {
            Directory.CreateDirectory(fullPath);
            return new ArchiveExtractedEntry(normalized, kind, 0, modified, FullPath: fullPath);
        }

        if (!budget.TryReserve(length, limits))
        {
            return new ArchiveExtractedEntry(normalized, kind, length, modified,
                ArchiveEntrySkipReason.LimitExceeded, "Entry exceeds the configured archive limits.");
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var input = entry.Open();
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyBoundedAsync(input, output, limits.MaxEntryBytes, cancellationToken).ConfigureAwait(false);

            return new ArchiveExtractedEntry(normalized, kind, length, modified, FullPath: fullPath);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(fullPath);
            throw;
        }
        catch (ZipEntryLimitExceededException exception)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return new ArchiveExtractedEntry(normalized, kind, length, modified,
                ArchiveEntrySkipReason.LimitExceeded, exception.Message);
        }
        catch (NotSupportedException exception)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return new ArchiveExtractedEntry(normalized, kind, length, modified,
                ArchiveEntrySkipReason.Unsupported, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            var reason = IsEncrypted(exception)
                ? ArchiveEntrySkipReason.Encrypted
                : ArchiveEntrySkipReason.Damaged;
            return new ArchiveExtractedEntry(normalized, kind, length, modified, reason, exception.Message);
        }
        catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return new ArchiveExtractedEntry(normalized, kind, length, modified,
                ArchiveEntrySkipReason.Damaged, exception.Message);
        }
    }

    private static ZipArchive OpenArchive(Stream source, string tempRoot, out Stream? ownedSource)
    {
        ownedSource = null;
        if (!source.CanSeek)
        {
            var bufferPath = Path.Combine(tempRoot, ".skycd-archive-source");
            using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(buffer);
            }

            ownedSource = new FileStream(bufferPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        return new ZipArchive(ownedSource ?? source, ZipArchiveMode.Read, leaveOpen: true);
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        Stream output,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > maxBytes)
                {
                    throw new ZipEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith("/", StringComparison.Ordinal)
        || entry.FullName.EndsWith("\\", StringComparison.Ordinal);

    private static long TryGetLength(ZipArchiveEntry entry)
    {
        try
        {
            return entry.Length;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetModified(ZipArchiveEntry entry)
    {
        try
        {
            return entry.LastWriteTime;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return null;
        }
    }

    private static bool IsEncrypted(Exception exception) =>
        exception.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchiveFailure(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException or ArgumentException;

    private static void Report(IProgress<int>? progress, int completed, int total) =>
        progress?.Report(total <= 0 ? 100 : Math.Clamp(completed * 100 / total, 0, 100));

    private static ArchiveEntry ToEntry(ArchiveExtractedEntry entry) =>
        new(entry.Path, entry.Kind, entry.SizeBytes, entry.ModifiedUtc, entry.SkipReason, entry.Detail);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private sealed class ExtractionBudget
    {
        private long totalBytes;

        public bool TryReserve(long bytes, ArchiveLimits limits)
        {
            if (bytes < 0 || bytes > limits.MaxEntryBytes || totalBytes + bytes > limits.MaxTotalBytes)
            {
                return false;
            }

            totalBytes += bytes;
            return true;
        }

        public void Release(long bytes)
        {
            if (bytes > 0)
            {
                totalBytes -= bytes;
            }
        }
    }
}
