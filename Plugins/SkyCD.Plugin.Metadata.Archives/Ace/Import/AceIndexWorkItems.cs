using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Common.Ace.Headers;
using SharpCompress.Readers;
using SharpCompress.Readers.Ace;
using AceCompressionType = SharpCompress.Common.CompressionType;
using SkyCD.PackagePaths;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Ace.Parsing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Ace.Import;

/// <summary>
/// Extracts stored ACE entries with the SharpCompress reader into a unique temporary directory with bounded
/// resource usage and exposes each extracted file as a host index work item. Disposing the returned set
/// deletes the directory. Compressed, encrypted, split and damaged entries are reported through typed
/// diagnostics and contribute no work item; stored entries are verified against the ACE CRC-32.
/// </summary>
internal static class AceIndexWorkItems
{
    private const string BufferedSourceName = ".skycd-ace-source";
    private const string EncryptedCode = "ACE_ENCRYPTED";
    private const string UnsupportedCode = "ACE_UNSUPPORTED";
    private const string DamagedCode = "ACE_DAMAGED";
    private const string UnsafeCode = "ACE_UNSAFE";
    private const int CopyBufferBytes = 81_920;

    public static async Task<ArchiveIndexResult> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("ace");
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

            if (!IsAceArchive(archive))
            {
                workspace.Dispose();
                return ArchiveIndexResult.Failure(DamagedCode, "The file does not contain the ACE signature.");
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
        catch (CryptographicException exception)
        {
            workspace.Dispose();
            return ArchiveIndexResult.Failure(EncryptedCode, exception.Message);
        }
        catch (MultiVolumeExtractionException exception)
        {
            workspace.Dispose();
            return ArchiveIndexResult.Failure(UnsupportedCode, exception.Message);
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
        var budget = new ExtractionBudget();
        var index = 0;

        await using var reader = await AceReader
            .OpenAsyncReader(archive, ReaderOptions.ForExternalStream, cancellationToken).ConfigureAwait(false);

        while (await reader.MoveToNextEntryAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = reader.Entry;
            index++;
            if (entry.IsDirectory)
            {
                continue;
            }

            var name = entry.Key ?? string.Empty;
            if (entry.IsEncrypted)
            {
                skipped.Add(new ArchiveSkipEntry(name, EncryptedCode, "Encrypted entries cannot be extracted."));
                continue;
            }

            if (entry.CompressionType != AceCompressionType.None)
            {
                skipped.Add(new ArchiveSkipEntry(name, UnsupportedCode,
                    $"Compression method {entry.CompressionType} is not supported."));
                continue;
            }

            if (index > limits.MaxEntries)
            {
                skipped.Add(new ArchiveSkipEntry(name, UnsupportedCode,
                    "The archive exceeds the configured entry limit."));
                continue;
            }

            if (!PackageEntryPath.TryNormalize(name, out var normalized, out var safe) || !safe
                || !PackageEntryPath.TryResolveUnderRoot(workspace.Root, normalized, out var fullPath,
                    suppressErrors: true))
            {
                skipped.Add(new ArchiveSkipEntry(name, UnsafeCode,
                    "The entry path escapes the extraction directory."));
                continue;
            }

            var length = entry.Size;
            if (length < 0 || !budget.TryReserve(length, limits))
            {
                skipped.Add(new ArchiveSkipEntry(name, UnsupportedCode,
                    "The entry exceeds the configured size budget."));
                continue;
            }

            try
            {
                var (written, crc) = await ExtractEntryAsync(reader, fullPath, limits.MaxEntryBytes,
                    cancellationToken).ConfigureAwait(false);
                if (written != length)
                {
                    DeleteQuietly(fullPath);
                    budget.Release(length);
                    skipped.Add(new ArchiveSkipEntry(normalized, DamagedCode,
                        "The archive ends inside the entry."));
                    continue;
                }

                if (crc != (uint)entry.Crc)
                {
                    DeleteQuietly(fullPath);
                    budget.Release(length);
                    skipped.Add(new ArchiveSkipEntry(normalized, DamagedCode,
                        "The stored entry data does not match its CRC-32."));
                    continue;
                }

                items.Add(new IndexWorkItem(normalized, written, token => OpenExtractedAsync(fullPath, token),
                    ToTimestamp(entry.LastModifiedTime)));
            }
            catch (ArchiveEntryLimitExceededException)
            {
                DeleteQuietly(fullPath);
                budget.Release(length);
                skipped.Add(new ArchiveSkipEntry(normalized, UnsupportedCode,
                    "The entry exceeds the configured size budget."));
            }
            catch (Exception exception) when (IsArchiveFailure(exception))
            {
                // The reader cannot resume after a failed entry read, so the rest of the archive is dropped.
                workspace.Dispose();
                return ArchiveIndexResult.Failure(DamagedCode, exception.Message);
            }
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

    private static async Task<(long Written, uint Crc)> ExtractEntryAsync(
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

        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        var crc = new AceCrc32();
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

                crc.Append(buffer.AsSpan(0, read));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return (written, crc.Value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Validates the <c>**ACE**</c> signature the reader itself accepts without complaint, so a file with an
    /// <c>.ace</c> name that is not an ACE archive is still reported as damaged.
    /// </summary>
    private static bool IsAceArchive(Stream archive)
    {
        if (archive.Length < 14)
        {
            return false;
        }

        var position = archive.Position;
        try
        {
            archive.Position = 0;
            return AceHeader.IsArchive(archive);
        }
        finally
        {
            archive.Position = position;
        }
    }

    private static DateTimeOffset? ToTimestamp(DateTime? value) =>
        value is DateTime date
            ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
            : null;

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
            or InvalidOperationException or UnauthorizedAccessException or FormatException or OverflowException;

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
