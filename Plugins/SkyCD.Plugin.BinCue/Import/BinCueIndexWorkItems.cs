using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.BinCue.Exceptions;
using SkyCD.Plugin.BinCue.Models;
using SkyCD.Plugin.BinCue.Parsing;

namespace SkyCD.Plugin.BinCue.Import;

/// <summary>
/// Lists a BIN/CUE image's files and exposes each one to the host index as a lazily materialized work
/// item. Only the entry currently being indexed exists on disk; disposing its stream deletes the
/// temporary file, including when extraction fails or is cancelled.
/// </summary>
internal static class BinCueIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        BinCueIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sheet = await ReadSheetAsync(request, cancellationToken).ConfigureAwait(false);
            var (track, file) = BinCueDataTrack.SelectData(sheet);
            var imagePath = BinCueCompanion.ResolveImagePath(request.SourcePath, file);
            var data = BinCueDataTrack.Map(sheet, track, new FileInfo(imagePath).Length);
            var entries = BinCueFileSystems.ReadEntries(imagePath, data, limits, cancellationToken);

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
                    !BinCueEntryPath.IsSafeRelativePath(entry.Path))
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
                    token => ExtractAsync(imagePath, data, readerPath, limits.MaxEntryBytes, token),
                    entry.ModifiedUtc));
            }

            Report(request.Progress, total, total);
            return new IndexWorkItemSet(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (BinCueImageFailures.IsImageFailure(exception))
        {
            return IndexWorkItemSet.Empty;
        }
    }

    private static async Task<Stream> ExtractAsync(
        string imagePath,
        BinCueDataTrack track,
        string readerPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = Path.Combine(Path.GetTempPath(), $"skycd-bincue-entry-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await BinCueFileSystems.CopyEntryAsync(imagePath, track, readerPath, output, maxBytes,
                cancellationToken).ConfigureAwait(false);
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

    private static async Task<CueSheet> ReadSheetAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            await using var file = File.OpenRead(request.SourcePath);
            return await CueSheet.ReadAsync(file, cancellationToken).ConfigureAwait(false);
        }

        await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
        return await CueSheet.ReadAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private static void Report(IProgress<int>? progress, int completed, int total) =>
        progress?.Report(total <= 0 ? 100 : Math.Clamp(completed * 100 / total, 0, 100));
}
