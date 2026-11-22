using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.DiskImages.Nrg.Parsing;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.Nrg.Import;

/// <summary>
/// Lists an NRG image's files and exposes each one to the host index as a lazily materialized work item.
/// Only the entry currently being indexed exists on disk; disposing its stream deletes the temporary file.
/// </summary>
internal static class NrgIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        DiskImageIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        try
        {
            IReadOnlyList<NrgImageEntry> entries;
            await using (var source = await request.OpenSource(cancellationToken).ConfigureAwait(false))
            {
                entries = NrgFileSystems.ReadEntries(source);
            }

            var items = new List<IndexWorkItem>();
            var total = entries.Count;
            var index = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                Report(request.Progress, index, total);

                if (entry.IsDirectory || entry.SizeBytes < 0 || entry.SizeBytes > limits.MaxEntryBytes ||
                    !DiskImageEntryPath.IsSafeRelativePath(entry.Path))
                {
                    continue;
                }

                if (items.Count >= limits.MaxEntries)
                {
                    break;
                }

                var readerPath = entry.ReaderPath;
                items.Add(new IndexWorkItem(
                    entry.Path,
                    entry.SizeBytes,
                    token => ExtractAsync(request, readerPath, limits, token),
                    entry.ModifiedUtc));
            }

            Report(request.Progress, total, total);
            return new IndexWorkItemSet(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            return IndexWorkItemSet.Empty;
        }
    }

    private static async Task<Stream> ExtractAsync(
        IndexWorkItemRequest request,
        string readerPath,
        DiskImageIndexLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = Path.Combine(Path.GetTempPath(), $"skycd-nrg-entry-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            await NrgFileSystems.CopyEntryAsync(source, readerPath, output, limits.MaxEntryBytes, cancellationToken)
                .ConfigureAwait(false);
            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsImageFailure(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException or ArgumentException;

    private static void Report(IProgress<int>? progress, int completed, int total) =>
        progress?.Report(total <= 0 ? 100 : Math.Clamp(completed * 100 / total, 0, 100));
}
