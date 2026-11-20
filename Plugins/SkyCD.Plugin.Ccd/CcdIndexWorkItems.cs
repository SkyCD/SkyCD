using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;

namespace SkyCD.Plugin.Ccd;

/// <summary>
/// Lists a CloneCD image's files and exposes each one to the host index as a lazily materialized work
/// item. Only the entry currently being indexed exists on disk; disposing its stream deletes the
/// temporary file, including when extraction fails or is cancelled.
/// </summary>
internal static class CcdIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        CcdIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var descriptor = await ReadDescriptorAsync(request, cancellationToken).ConfigureAwait(false);
            var imagePath = CcdCompanion.ResolveImagePath(request.SourcePath, descriptor);
            var track = CcdDataTrack.Resolve(descriptor, new FileInfo(imagePath).Length);
            var entries = CcdFileSystems.ReadEntries(imagePath, track, limits, cancellationToken);

            var items = new List<IndexWorkItem>();
            long totalBytes = 0;
            var total = entries.Count;
            var index = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                Report(request.Progress, index, total);

                if (entry.IsDirectory || entry.SizeBytes < 0 || entry.SizeBytes > limits.MaxEntryBytes ||
                    totalBytes + entry.SizeBytes > limits.MaxTotalBytes ||
                    !CcdEntryPath.IsSafeRelativePath(entry.Path))
                {
                    continue;
                }

                if (items.Count >= limits.MaxEntries)
                {
                    break;
                }

                totalBytes += entry.SizeBytes;
                var readerPath = entry.ReaderPath;
                items.Add(new IndexWorkItem(
                    entry.Path,
                    entry.SizeBytes,
                    token => ExtractAsync(imagePath, track, readerPath, limits.MaxEntryBytes, token),
                    entry.ModifiedUtc));
            }

            Report(request.Progress, total, total);
            return new IndexWorkItemSet(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (CcdImageFailures.IsImageFailure(exception))
        {
            return IndexWorkItemSet.Empty;
        }
    }

    private static async Task<Stream> ExtractAsync(
        string imagePath,
        CcdDataTrack track,
        string readerPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = Path.Combine(Path.GetTempPath(), $"skycd-ccd-entry-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await CcdFileSystems.CopyEntryAsync(imagePath, track, readerPath, output, maxBytes, cancellationToken)
                .ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<CcdDescriptor> ReadDescriptorAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            await using var file = File.OpenRead(request.SourcePath);
            return await CcdDescriptorReader.ReadAsync(file, cancellationToken).ConfigureAwait(false);
        }

        await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
        return await CcdDescriptorReader.ReadAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private static void Report(IProgress<int>? progress, int completed, int total) =>
        progress?.Report(total <= 0 ? 100 : Math.Clamp(completed * 100 / total, 0, 100));
}
