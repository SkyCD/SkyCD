using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Arj;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;

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

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-arj-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                var bufferPath = Path.Combine(tempRoot, ".skycd-arj-source");
                await using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                }

                bufferedSource = new FileStream(bufferPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                archiveStream = bufferedSource;
            }

            var items = await ExtractEntriesAsync(archiveStream, tempRoot, limits, request.Progress, cancellationToken)
                .ConfigureAwait(false);

            return new IndexWorkItemSet(items, () =>
            {
                DeleteTemporaryDirectory(tempRoot);
                return ValueTask.CompletedTask;
            });
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

            if (!ArchiveEntryPath.TryNormalize(entry.Key, out var normalized, out var safe)
                || !safe
                || !ArchiveEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
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
        catch (OperationCanceledException)
        {
            TryDeleteFile(fullPath);
            throw;
        }
        catch (ArchiveEntryLimitExceededException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return (null, true);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
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
