using SharpCompress.Common;
using SharpCompress.Readers.Arj;
using SharpCompress.Readers;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkyCD.PackagePaths;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Arj.Import;

/// <summary>
/// Extracts ARJ entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class ArjIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("arj");

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                bufferedSource = await NonSeekableSourceBuffer.BufferAsync(source,
                    workspace.GetPath(".skycd-arj-source"), cancellationToken).ConfigureAwait(false);
                archiveStream = bufferedSource;
            }

            var items = await ExtractEntriesAsync(archiveStream, workspace.Root, limits, request.Progress, cancellationToken)
                .ConfigureAwait(false);

            return new IndexWorkItemSet(items, () =>
            {
                workspace.Dispose();
                return ValueTask.CompletedTask;
            });
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
        finally
        {
            bufferedSource?.Dispose();
        }
    }

    private static async Task<List<IndexWorkItem>> ExtractEntriesAsync(
        Stream archiveStream,
        string tempRoot,
        ArchiveIndexLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexWorkItem>();
        var budget = new ExtractionBudget();
        var index = 0;

        await using var reader = await ArjReader
            .OpenAsyncReader(archiveStream, ReaderOptions.ForExternalStream, cancellationToken)
            .ConfigureAwait(false);

        while (await reader.MoveToNextEntryAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            var entry = reader.Entry;
            if (entry.IsDirectory)
            {
                continue;
            }

            if (!PackageEntryPath.TryNormalize(entry.Key, out var normalized, out var safe)
                || !safe
                || !PackageEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
            {
                continue;
            }

            var length = TryGetLength(entry);
            if (index > limits.MaxEntries || !budget.TryReserve(length, limits))
            {
                continue;
            }

            var (item, aborted) = await ExtractEntryAsync(reader, entry, normalized, length, fullPath, limits, budget,
                cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                items.Add(item);
            }

            if (aborted)
            {
                break;
            }
        }

        progress?.Report(100);
        return items;
    }

    private static async Task<(IndexWorkItem? Item, bool Aborted)> ExtractEntryAsync(
        IAsyncReader reader,
        IEntry entry,
        string normalized,
        long length,
        string fullPath,
        ArchiveIndexLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var input = await reader.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyBoundedAsync(input, output, limits.MaxEntryBytes, cancellationToken).ConfigureAwait(false);

            return (new IndexWorkItem(normalized, length, token => OpenExtractedAsync(fullPath, token),
                TryGetModified(entry)), false);
        }
        catch (ArchiveEntryLimitExceededException)
        {
            budget.Release(length);
            return (null, true);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            budget.Release(length);
            return (null, true);
        }
    }

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
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

    private static long TryGetLength(IEntry entry)
    {
        try
        {
            return Math.Max(entry.Size, 0);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetModified(IEntry entry)
    {
        try
        {
            return entry.LastModifiedTime is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : null;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return null;
        }
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is SharpCompressException or IOException or NotSupportedException or ArgumentException
            or InvalidOperationException;

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
