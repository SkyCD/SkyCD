using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;

namespace SkyCD.Plugin.Metadata.Archives.Zip.Import;

/// <summary>
/// Extracts ZIP entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class ZipIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            using var archive = OpenArchive(source, tempRoot, out var ownedSource);
            try
            {
                var items = new List<IndexWorkItem>();
                var budget = new ExtractionBudget();
                var total = archive.Entries.Count;

                for (var index = 0; index < total; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = await ExtractEntryAsync(archive.Entries[index], index + 1, tempRoot, limits, budget,
                        cancellationToken).ConfigureAwait(false);
                    if (item is not null)
                    {
                        items.Add(item);
                    }

                    Report(request.Progress, index + 1, total);
                }

                Report(request.Progress, total, total);
                return new IndexWorkItemSet(items, () =>
                {
                    DeleteTemporaryDirectory(tempRoot);
                    return ValueTask.CompletedTask;
                });
            }
            finally
            {
                ownedSource?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            DeleteTemporaryDirectory(tempRoot);
            throw;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            DeleteTemporaryDirectory(tempRoot);
            return IndexWorkItemSet.Empty;
        }
        catch (Exception)
        {
            DeleteTemporaryDirectory(tempRoot);
            throw;
        }
    }

    private static async Task<IndexWorkItem?> ExtractEntryAsync(
        ZipArchiveEntry entry,
        int index,
        string tempRoot,
        ArchiveIndexLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        if (IsDirectoryEntry(entry))
        {
            return null;
        }

        var modified = TryGetModified(entry);
        if (!ArchiveEntryPath.TryNormalize(entry.FullName, out var normalized, out var safe))
        {
            return null;
        }

        var length = TryGetLength(entry);
        if (index > limits.MaxEntries || !safe || !ArchiveEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
        {
            return null;
        }

        if (!budget.TryReserve(length, limits))
        {
            return null;
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

            return new IndexWorkItem(normalized, length, token => OpenExtractedAsync(fullPath, token), modified);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(fullPath);
            throw;
        }
        catch (ArchiveEntryLimitExceededException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (NotSupportedException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (InvalidDataException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
    }

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static ZipArchive OpenArchive(Stream source, string tempRoot, out Stream? ownedSource)
    {
        ownedSource = null;
        if (!source.CanSeek)
        {
            var bufferPath = Path.Combine(tempRoot, ".skycd-index-source");
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
                    throw new ArchiveEntryLimitExceededException();
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

    private static bool IsArchiveFailure(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException or ArgumentException;

    private static void Report(IProgress<int>? progress, int completed, int total) =>
        progress?.Report(total <= 0 ? 100 : Math.Clamp(completed * 100 / total, 0, 100));

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

    /// <summary>Deletes a temporary extraction directory. Cleanup is best-effort and never throws.</summary>
    private static void DeleteTemporaryDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                TryDeleteFile(file);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
    }

    private sealed class ExtractionBudget
    {
        private long totalBytes;

        public bool TryReserve(long bytes, ArchiveIndexLimits limits)
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
