using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Zoo.Exceptions;
using SkyCD.Plugin.Metadata.Zoo.Models;
using SkyCD.Plugin.Metadata.Zoo.Parsing;

namespace SkyCD.Plugin.Metadata.Zoo.Import;

/// <summary>
/// Extracts ZOO entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class ZooIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ZooIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-zoo-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            FileStream? buffered = null;
            try
            {
                var archiveStream = source;
                if (!source.CanSeek)
                {
                    var bufferPath = Path.Combine(tempRoot, ".skycd-zoo-source");
                    await using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write,
                                     FileShare.None, CopyBufferBytes, FileOptions.Asynchronous))
                    {
                        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }

                    buffered = new FileStream(bufferPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        CopyBufferBytes, FileOptions.SequentialScan);
                    archiveStream = buffered;
                }

                var entries = ZooArchiveReader.ReadEntries(archiveStream, limits.MaxEntries, cancellationToken);
                var items = new List<IndexWorkItem>();
                var budget = new ExtractionBudget();
                var total = entries.Count;

                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = TryExtractEntry(archiveStream, entries[index], tempRoot, limits, budget,
                        cancellationToken);
                    if (item is not null)
                    {
                        items.Add(item);
                    }

                    Report(request.Progress, index + 1, total);
                }

                request.Progress?.Report(100);
                return new IndexWorkItemSet(items, () =>
                {
                    DeleteTemporaryDirectory(tempRoot);
                    return ValueTask.CompletedTask;
                });
            }
            finally
            {
                buffered?.Dispose();
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

    private static IndexWorkItem? TryExtractEntry(
        Stream archiveStream,
        ZooArchiveEntry entry,
        string tempRoot,
        ZooIndexLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        if (entry.PackingMethod is not (0 or 1))
        {
            return null;
        }

        if (!ZooEntryPath.TryNormalize(entry.Path, out var normalized, out var safe)
            || !safe
            || !ZooEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
        {
            return null;
        }

        if (!budget.TryReserve(entry.OriginalBytes, limits))
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

            using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       CopyBufferBytes, FileOptions.SequentialScan))
            {
                ZooArchiveReader.ExtractEntry(archiveStream, entry, output, limits.MaxEntryBytes, cancellationToken);
            }

            return new IndexWorkItem(normalized, entry.OriginalBytes, token => OpenExtractedAsync(fullPath, token),
                entry.ModifiedUtc);
        }
        catch (OperationCanceledException)
        {
            budget.Release(entry.OriginalBytes);
            TryDeleteFile(fullPath);
            throw;
        }
        catch (Exception exception) when (exception is ZooEntryLimitExceededException
                                              or ZooUnsupportedMethodException or ZooDamagedArchiveException)
        {
            budget.Release(entry.OriginalBytes);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            budget.Release(entry.OriginalBytes);
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

    private static void Report(IProgress<int>? progress, int completed, int total)
    {
        if (progress is null || total <= 0)
        {
            return;
        }

        progress.Report(Math.Clamp((int)(100L * completed / total), 0, 100));
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException or ArgumentException;

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

        public bool TryReserve(long bytes, ZooIndexLimits limits)
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
