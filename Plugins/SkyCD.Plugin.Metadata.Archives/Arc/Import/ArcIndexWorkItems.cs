using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Arc.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Arc.Models;
using SkyCD.Plugin.Metadata.Archives.Arc.Parsing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Import;

/// <summary>
/// Extracts stored ARC entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class ArcIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;
    private const string BufferedSourceName = ".skycd-arc-source";

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-arc-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        FileStream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                var bufferPath = Path.Combine(tempRoot, BufferedSourceName);
                await using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                }

                bufferedSource = new FileStream(bufferPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                archiveStream = bufferedSource;
            }

            var listing = ArcLister.List(archiveStream, cancellationToken);
            var items = await ExtractEntriesAsync(archiveStream, listing, tempRoot, limits, request.Progress,
                cancellationToken).ConfigureAwait(false);

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
        catch (ArcEntryCountExceededException)
        {
            DeleteTemporaryDirectory(tempRoot);
            return IndexWorkItemSet.Empty;
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
        ArcListing listing,
        string tempRoot,
        ArchiveIndexLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexWorkItem>();
        var budget = new ExtractionBudget();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (var entry in listing.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            if (!CanExtract(entry))
            {
                continue;
            }

            if (!ArchiveEntryPath.TryNormalize(entry.Name, out var normalized, out var safe)
                || !safe
                || !seenNames.Add(normalized)
                || !ArchiveEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
            {
                continue;
            }

            var length = Math.Max(entry.UncompressedSize, 0);
            if (index > limits.MaxEntries)
            {
                throw new ArcEntryCountExceededException();
            }

            if (!budget.TryReserve(length, limits))
            {
                continue;
            }

            var item = await ExtractEntryAsync(archiveStream, entry, normalized, length, fullPath, limits, budget,
                cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        progress?.Report(100);
        return items;
    }

    /// <summary>Only stored, non-directory entries with a decodable payload become work items.</summary>
    private static bool CanExtract(ArcEntry entry) =>
        !entry.IsDirectory
        && entry.Method != ArcMethod.Reserved
        && entry.UncompressedSize >= 0;

    private static async Task<IndexWorkItem?> ExtractEntryAsync(
        Stream archiveStream,
        ArcEntry entry,
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

            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await ArcEntryExtractor.CopyStoredAsync(archiveStream, entry, output, limits.MaxEntryBytes,
                    cancellationToken).ConfigureAwait(false);
            }

            return new IndexWorkItem(normalized, length, token => OpenExtractedAsync(fullPath, token),
                entry.ModifiedUtc);
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
        catch (Exception exception) when (IsArchiveFailure(exception))
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

    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
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
