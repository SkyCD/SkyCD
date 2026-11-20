using System;
using System.Buffers;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Tar.Metadata.Exceptions;

namespace SkyCD.Plugin.Tar.Metadata;

/// <summary>
/// Extracts TAR entries into a unique temporary directory with bounded resource usage and exposes each
/// extracted file as a host index work item. Disposing the returned set deletes the directory.
/// </summary>
internal static class TarMetadataWorkItems
{
    private const int CopyBufferBytes = 81_920;

    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        TarMetadataLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-tar-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            using var tarStream = PrepareStream(source, tempRoot);
            var reader = new TarReader(tarStream, leaveOpen: true);

            var items = new List<IndexWorkItem>();
            var budget = new ExtractionBudget();
            var index = 0;

            while (reader.GetNextEntry(copyData: false) is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++index > limits.MaxEntries)
                {
                    break;
                }

                var item = await ExtractEntryAsync(entry, tempRoot, limits, budget, cancellationToken)
                    .ConfigureAwait(false);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

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
    }

    private static async Task<IndexWorkItem?> ExtractEntryAsync(
        TarEntry entry,
        string tempRoot,
        TarMetadataLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        if (!IsRegularFile(entry.EntryType) || entry.DataStream is null)
        {
            return null;
        }

        if (!TarEntryPath.TryNormalize(entry.Name, out var normalized, out var safe)
            || !safe
            || !TarEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
        {
            return null;
        }

        var length = entry.Length;
        if (length < 0 || !budget.TryReserve(length, limits))
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

            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyBoundedAsync(entry.DataStream, output, limits.MaxEntryBytes, cancellationToken)
                .ConfigureAwait(false);

            return new IndexWorkItem(normalized, length, token => OpenExtractedAsync(fullPath, token),
                entry.ModificationTime);
        }
        catch (OperationCanceledException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            throw;
        }
        catch (TarEntryLimitExceededException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (NotSupportedException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (InvalidDataException)
        {
            budget.Release(length);
            TryDeleteFile(fullPath);
            return null;
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
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

    private static Stream PrepareStream(Stream source, string tempRoot)
    {
        var seekable = source;
        if (!source.CanSeek)
        {
            var bufferPath = Path.Combine(tempRoot, ".skycd-tar-source");
            using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(buffer);
            }

            seekable = new FileStream(bufferPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        return IsGzip(seekable)
            ? new GZipStream(seekable, CompressionMode.Decompress, leaveOpen: false)
            : seekable;
    }

    private static bool IsGzip(Stream stream)
    {
        Span<byte> header = stackalloc byte[2];
        var position = stream.Position;
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        stream.Position = position;
        return read == header.Length && header[0] == 0x1F && header[1] == 0x8B;
    }

    private static bool IsRegularFile(TarEntryType entryType) =>
        entryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;

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
                    throw new TarEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException or ArgumentException
            or FormatException or OverflowException;

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

        public bool TryReserve(long bytes, TarMetadataLimits limits)
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
