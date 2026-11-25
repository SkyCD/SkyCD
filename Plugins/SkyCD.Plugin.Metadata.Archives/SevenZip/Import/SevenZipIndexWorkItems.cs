using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkyCD.PackagePaths;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.SevenZip.Import;

/// <summary>
/// Extracts 7z entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class SevenZipIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("sevenzip");

        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            using var archive = OpenArchive(source, workspace.Root, out var ownedSource,
                cancellationToken);
            try
            {
                var entries = archive.Entries.ToList();
                var items = new List<IndexWorkItem>();
                var budget = new ExtractionBudget();

                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = await ExtractEntryAsync(entries[index], index + 1, workspace.Root, limits, budget,
                        cancellationToken).ConfigureAwait(false);
                    if (item is not null)
                    {
                        items.Add(item);
                    }

                    Report(request.Progress, index + 1, entries.Count);
                }

                Report(request.Progress, entries.Count, entries.Count);
                return new IndexWorkItemSet(items, () =>
                {
                    workspace.Dispose();
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

    private static async Task<IndexWorkItem?> ExtractEntryAsync(
        SevenZipArchiveEntry entry,
        int index,
        string tempRoot,
        ArchiveIndexLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        if (entry.IsDirectory || entry.IsEncrypted)
        {
            return null;
        }

        var modified = TryGetModified(entry);
        if (!PackageEntryPath.TryNormalize(entry.Key, out var normalized, out var safe))
        {
            return null;
        }

        var length = TryGetLength(entry);
        if (index > limits.MaxEntries || !safe ||
            !PackageEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
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

            await using var input = await entry.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyBoundedAsync(input, output, limits.MaxEntryBytes, cancellationToken).ConfigureAwait(false);

            return new IndexWorkItem(normalized, length, token => OpenExtractedAsync(fullPath, token), modified);
        }
        catch (ArchiveEntryLimitExceededException)
        {
            budget.Release(length);
            return null;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            budget.Release(length);
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

    private static SevenZipArchive OpenArchive(Stream source, string tempRoot, out Stream? ownedSource,
        CancellationToken cancellationToken)
    {
        ownedSource = source.CanSeek
            ? null
            : NonSeekableSourceBuffer.BufferAsync(source, Path.Combine(tempRoot, ".skycd-index-source"),
                cancellationToken).GetAwaiter().GetResult();

        return (SevenZipArchive)SevenZipArchive.OpenArchive(ownedSource ?? source);
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

    private static long TryGetLength(SevenZipArchiveEntry entry)
    {
        try
        {
            return Math.Max(0, entry.Size);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetModified(SevenZipArchiveEntry entry)
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

    /// <summary>
    /// Encrypted headers, damaged data and unsupported compression methods surface as one of these
    /// exceptions; the archive is then treated as unindexable instead of failing the whole scan.
    /// </summary>
    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidFormatException or ExtractionException
            or IncompleteArchiveException or NotSupportedException or ArgumentException or InvalidOperationException
            or SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException;

    private static void Report(IProgress<int>? progress, int completed, int total) =>
        progress?.Report(total <= 0 ? 100 : Math.Clamp(completed * 100 / total, 0, 100));

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
