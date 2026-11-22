using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Iso.Models;
using SkyCD.Plugin.Metadata.Iso.Parsing;

namespace SkyCD.Plugin.Metadata.Iso.Import;

/// <summary>
/// Lists a disk image's files and exposes each one to the host index as a lazily materialized work item.
/// Only the entry currently being indexed exists on disk; disposing its stream deletes the temporary file.
/// </summary>
internal static class IsoIndexWorkItems
{
    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        IsoIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IReadOnlyList<IsoImageEntry> entries;
            await using (var source = await request.OpenSource(cancellationToken).ConfigureAwait(false))
            {
                var (stream, owned) = await IsoSeekableSource.EnsureAsync(source, cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    entries = IsoFileSystems.ReadEntries(stream);
                }
                finally
                {
                    if (owned)
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            var items = new List<IndexWorkItem>();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory || !IsoEntryPath.IsSafeRelativePath(entry.Path) ||
                    entry.SizeBytes < 0 || entry.SizeBytes > limits.MaxEntryBytes)
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

            request.Progress?.Report(100);
            return new IndexWorkItemSet(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsoFileSystems.IsImageFailure(exception))
        {
            return IndexWorkItemSet.Empty;
        }
    }

    private static async Task<Stream> ExtractAsync(
        IndexWorkItemRequest request,
        string readerPath,
        IsoIndexLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = Path.Combine(Path.GetTempPath(), $"skycd-iso-entry-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            IsoSeekableSource.BufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        Stream? buffered = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var (stream, owned) = await IsoSeekableSource.EnsureAsync(source, cancellationToken).ConfigureAwait(false);
            buffered = owned ? stream : null;
            await IsoFileSystems.CopyEntryAsync(stream, readerPath, output, limits.MaxEntryBytes, cancellationToken)
                .ConfigureAwait(false);
            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
