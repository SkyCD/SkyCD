using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Arc;
using SkyCD.PackagePaths;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Import;

/// <summary>
/// Extracts ARC entries with the SharpCompress reader into a unique temporary directory with bounded resource
/// usage and exposes each extracted file as a host index work item. Disposing the returned set deletes the
/// directory. Stored, packed (RLE90), squeezed and crunched entries are decoded; methods the reader cannot
/// decode are reported as <c>ARC_UNSUPPORTED</c> without writing their raw bytes.
/// </summary>
internal static class ArcIndexWorkItems
{
    private const byte EndOfArchiveMarker = 0x1A;
    private const string BufferedSourceName = ".skycd-arc-source";
    private const string UnsupportedCode = "ARC_UNSUPPORTED";
    private const string DamagedCode = "ARC_DAMAGED";
    private const int CopyBufferBytes = 81_920;

    public static async Task<ArchiveIndexResult> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("arc");
        FileStream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            Stream archive = source;
            if (!source.CanSeek)
            {
                bufferedSource = await NonSeekableSourceBuffer.BufferAsync(source,
                    workspace.GetPath(BufferedSourceName), cancellationToken).ConfigureAwait(false);
                archive = bufferedSource;
            }

            if (archive.Length == 0)
            {
                workspace.Dispose();
                return ArchiveIndexResult.Empty;
            }

            if (!await StartsWithEndOfArchiveMarkerAsync(archive, cancellationToken).ConfigureAwait(false))
            {
                workspace.Dispose();
                return ArchiveIndexResult.Failure(DamagedCode, "The file does not start with the ARC marker.");
            }

            archive.Position = 0;
            return await ExtractAsync(archive, workspace, limits, request.Progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            workspace.Dispose();
            throw;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            workspace.Dispose();
            return ArchiveIndexResult.Failure(DamagedCode, exception.Message);
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

    private static async Task<ArchiveIndexResult> ExtractAsync(
        Stream archive,
        ArchiveExtractionWorkspace workspace,
        ArchiveIndexLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexWorkItem>();
        var skipped = new List<ArchiveSkipEntry>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0L;
        var index = 0;
        var damaged = false;

        await using var reader = await ArcReader
            .OpenAsyncReader(archive, ReaderOptions.ForExternalStream, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            bool moved;
            try
            {
                moved = await reader.MoveToNextEntryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The reader takes the 29-byte end-of-archive marker (and any trailing padding) for a header
                // and fails on its name field; in that position it only means the entry list ended.
                break;
            }
            catch (Exception exception) when (IsArchiveFailure(exception))
            {
                damaged = true;
                break;
            }

            if (!moved)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            index++;

            var entry = reader.Entry;
            var name = entry.Key;
            if (IsDirectoryName(name))
            {
                continue;
            }

            if (!IsDecodable(entry.CompressionType))
            {
                skipped.Add(new ArchiveSkipEntry(name ?? string.Empty, UnsupportedCode,
                    $"Compression method {entry.CompressionType} is not supported."));
                continue;
            }

            if (!PackageEntryPath.TryNormalize(name, out var normalized, out var safe) || !safe
                || !seenNames.Add(normalized)
                || !PackageEntryPath.TryResolveUnderRoot(workspace.Root, normalized, out var fullPath)
                || File.Exists(fullPath))
            {
                continue;
            }

            if (index > limits.MaxEntries)
            {
                workspace.Dispose();
                return ArchiveIndexResult.Empty;
            }

            var remainingBytes = limits.MaxTotalBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                skipped.Add(new ArchiveSkipEntry(normalized, UnsupportedCode,
                    "The entry exceeds the configured size budget."));
                continue;
            }

            var dataStart = archive.Position;
            long written;
            try
            {
                written = await ExtractEntryAsync(reader, fullPath, Math.Min(limits.MaxEntryBytes, remainingBytes),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ArchiveEntryLimitExceededException)
            {
                DeleteQuietly(fullPath);
                if (!TrySkipEntry(archive, dataStart, entry.CompressedSize))
                {
                    damaged = true;
                    break;
                }

                skipped.Add(new ArchiveSkipEntry(normalized, UnsupportedCode,
                    "The entry exceeds the configured size budget."));
                continue;
            }
            catch (NotSupportedException exception)
            {
                // Opening failed before the entry was marked as written, so the reader skips its data on the
                // next move; moving the stream here would skip twice.
                DeleteQuietly(fullPath);
                skipped.Add(new ArchiveSkipEntry(normalized, UnsupportedCode, exception.Message));
                continue;
            }
            catch (Exception exception) when (IsArchiveFailure(exception))
            {
                damaged = true;
                break;
            }

            if (entry.CompressionType == CompressionType.None && written != entry.CompressedSize)
            {
                damaged = true;
                break;
            }

            totalBytes += written;
            items.Add(new IndexWorkItem(normalized, written, token => OpenExtractedAsync(fullPath, token)));
        }

        if (damaged)
        {
            workspace.Dispose();
            return ArchiveIndexResult.Failure(DamagedCode, "The archive is damaged or truncated.");
        }

        progress?.Report(100);
        return new ArchiveIndexResult(
            new IndexWorkItemSet(items, () =>
            {
                workspace.Dispose();
                return ValueTask.CompletedTask;
            }),
            skipped);
    }

    private static async Task<long> ExtractEntryAsync(
        IAsyncReader reader,
        string fullPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var input = await reader.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await CopyBoundedAsync(input, output, maxBytes, cancellationToken).ConfigureAwait(false);
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
            var written = 0L;
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

    /// <summary>
    /// Moves the archive stream past the data of an entry whose copy was abandoned, so the next header is read
    /// from the right position.
    /// </summary>
    private static bool TrySkipEntry(Stream archive, long dataStart, long compressedSize)
    {
        var dataEnd = dataStart + compressedSize;
        if (compressedSize < 0 || dataEnd > archive.Length)
        {
            return false;
        }

        archive.Position = dataEnd;
        return true;
    }

    private static async ValueTask<bool> StartsWithEndOfArchiveMarkerAsync(
        Stream archive,
        CancellationToken cancellationToken)
    {
        var marker = new byte[1];
        archive.Position = 0;
        return await archive.ReadAsync(marker.AsMemory(), cancellationToken).ConfigureAwait(false) == 1
            && marker[0] == EndOfArchiveMarker;
    }

    private static bool IsDecodable(CompressionType compressionType) =>
        compressionType is CompressionType.None or CompressionType.Packed or CompressionType.Squeezed
            or CompressionType.Crunched;

    private static bool IsDirectoryName(string? name) =>
        !string.IsNullOrEmpty(name) && (name.EndsWith('/') || name.EndsWith('\\'));

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The workspace cleanup removes anything left behind.
        }
        catch (UnauthorizedAccessException)
        {
            // The workspace cleanup removes anything left behind.
        }
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is SharpCompressException or IOException or NotSupportedException or ArgumentException
            or InvalidOperationException or UnauthorizedAccessException;
}
