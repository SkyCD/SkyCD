using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Metadata.Archives.Zoo.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Zoo.Models;
using SkyCD.Plugin.Metadata.Archives.Zoo.Parsing;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Zoo.Import;

/// <summary>
/// Extracts ZOO entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class ZooIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("zoo");

        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            FileStream? buffered = null;
            try
            {
                var archiveStream = source;
                if (!source.CanSeek)
                {
                    buffered = await NonSeekableSourceBuffer.BufferAsync(source,
                        workspace.GetPath(".skycd-zoo-source"), cancellationToken).ConfigureAwait(false);
                    archiveStream = buffered;
                }

                var entries = ZooArchiveReader.ReadEntries(archiveStream, limits.MaxEntries, cancellationToken);
                var items = new List<IndexWorkItem>();
                var budget = new ExtractionBudget();
                var total = entries.Count;

                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = TryExtractEntry(archiveStream, entries[index], workspace.Root, limits, budget,
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
                    workspace.Dispose();
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
            workspace.Dispose();
            throw;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            workspace.Dispose();
            return IndexWorkItemSet.Empty;
        }
        catch (Exception)
        {
            workspace.Dispose();
            throw;
        }
    }

    private static IndexWorkItem? TryExtractEntry(
        Stream archiveStream,
        ZooArchiveEntry entry,
        string tempRoot,
        ArchiveIndexLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        if (entry.PackingMethod is not (0 or 1))
        {
            return null;
        }

        if (!ArchiveEntryPath.TryNormalize(entry.Path, out var normalized, out var safe)
            || !safe
            || !ArchiveEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
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
            throw;
        }
        catch (Exception exception) when (exception is ArchiveEntryLimitExceededException
                                              or ZooUnsupportedMethodException or ZooDamagedArchiveException)
        {
            budget.Release(entry.OriginalBytes);
            return null;
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            budget.Release(entry.OriginalBytes);
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
