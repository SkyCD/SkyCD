using SharpCompress.Common;
using SharpCompress.Readers.Rar;
using SharpCompress.Readers;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using SkyCD.PackagePaths;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Metadata.Archives.Rar.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Rar.Parsing;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Rar.Import;

/// <summary>
/// Extracts RAR entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class RarIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("rar");

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                bufferedSource = await NonSeekableSourceBuffer.BufferAsync(source,
                    workspace.GetPath(".skycd-rar-source"), cancellationToken).ConfigureAwait(false);
                archiveStream = bufferedSource;
            }

            if (!RarArchiveSignature.Matches(archiveStream))
            {
                workspace.Dispose();
                return IndexWorkItemSet.Empty;
            }

            var status = RarHeaderScan.Inspect(archiveStream,
                RarVolumeCompanions.FindCompanion(request.SourcePath) is not null);
            if (!status.IsReadable)
            {
                workspace.Dispose();
                return IndexWorkItemSet.Empty;
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
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
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
        var extractedFiles = new HashSet<string>(StringComparer.Ordinal);

        archiveStream.Position = 0;
        using var reader = RarReader.OpenReader(RarVolumeAwareStream.Wrap(archiveStream),
            RarHeaderScan.CreateReaderOptions());

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = reader.Entry;
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                // The header scan proved the headers readable, so an unnamed header inside extraction means the
                // archive changed underneath us or continues in a volume.
                throw new MultiVolumeMissingException();
            }

            // Only file headers carry data. RAR5 stores directories with a trailing separator and an archiver
            // that also records the service headers (such as the Quick Open cache) as directory like entries;
            // those must never be extracted as catalog files.
            if (entry.IsDirectory || entry.IsEncrypted || !HasContent(entry))
            {
                continue;
            }

            if (!PackageEntryPath.TryNormalize(entry.Key, out var normalized, out var safe, PackageEntryPathRules.SkipCurrentDirectorySegments)
                || !safe
                || !PackageEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
            {
                continue;
            }

            var length = TryGetLength(entry);
            if (items.Count >= limits.MaxEntries)
            {
                throw new RarIndexedItemsLimitExceededException();
            }

            if (!budget.TryReserve(length, limits))
            {
                continue;
            }

            if (!extractedFiles.Add(fullPath))
            {
                // Duplicate entry names inside one archive: the first extracted file wins.
                budget.Release(length);
                continue;
            }

            var (item, aborted) = await ExtractEntryAsync(reader, entry, normalized, fullPath, limits, budget,
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
        IReader reader,
        IEntry entry,
        string normalized,
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

            cancellationToken.ThrowIfCancellationRequested();
            await using var input = reader.OpenEntryStream();
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var written = await CopyBoundedAsync(input, output, limits.MaxEntryBytes, cancellationToken)
                .ConfigureAwait(false);

            return (new IndexWorkItem(normalized, written, token => OpenExtractedAsync(fullPath, token),
                TryGetModified(entry)), false);
        }
        catch (ArchiveEntryLimitExceededException)
        {
            return (null, true);
        }
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
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

    private static async Task<long> CopyBoundedAsync(
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

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool HasContent(IEntry entry)
    {
        try
        {
            return entry.Size > 0 || entry.CompressedSize > 0;
        }
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
            return false;
        }
    }

    private static long TryGetLength(IEntry entry)
    {
        try
        {
            return Math.Max(entry.Size, 0);
        }
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
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
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
            return null;
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
