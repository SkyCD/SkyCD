using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Ace.Exceptions;
using SkyCD.Plugin.Metadata.Ace.Models;
using SkyCD.Plugin.Metadata.Ace.Parsing;

namespace SkyCD.Plugin.Metadata.Ace.Import;

/// <summary>
/// Extracts stored ACE entries into a unique temporary directory with bounded resource usage and exposes
/// each extracted file as a host index work item. Disposing the returned set deletes the directory.
/// Compressed, encrypted, split and damaged entries are listed during scanning but skipped here, because
/// this plugin ships no ACE decompressor.
/// </summary>
internal static class AceIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    /// <summary>Extracts every eligible entry and reports the entries skipped with a typed failure.</summary>
    public static async Task<AceIndexResult> CreateAsync(
        IndexWorkItemRequest request,
        AceIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-ace-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            byte[] archiveBytes;
            await using (var source = await request.OpenSource(cancellationToken).ConfigureAwait(false))
            {
                archiveBytes = await ReadAllBytesAsync(source, tempRoot, cancellationToken).ConfigureAwait(false);
            }

            var headers = new AceHeaderReader().Read(new MemoryStream(archiveBytes, writable: false),
                cancellationToken);
            var extraction = ExtractEntries(headers, archiveBytes, tempRoot, limits, request.Progress,
                cancellationToken);

            var set = new IndexWorkItemSet(extraction.Items, () =>
            {
                DeleteTemporaryDirectory(tempRoot);
                return ValueTask.CompletedTask;
            });
            return new AceIndexResult(set, extraction.Skipped);
        }
        catch (OperationCanceledException)
        {
            DeleteTemporaryDirectory(tempRoot);
            throw;
        }
        catch (AceEncryptedArchiveException exception)
        {
            DeleteTemporaryDirectory(tempRoot);
            return AceIndexResult.ForFailure("ACE_ENCRYPTED", exception.Message);
        }
        catch (AceUnsupportedBlockException exception)
        {
            DeleteTemporaryDirectory(tempRoot);
            return AceIndexResult.ForFailure("ACE_UNSUPPORTED", exception.Message);
        }
        catch (Exception exception)
        {
            DeleteTemporaryDirectory(tempRoot);
            if (!AceArchiveFailures.IsArchiveFailure(exception))
            {
                throw;
            }

            return AceIndexResult.Damaged(exception.Message);
        }
    }

    private static Extraction ExtractEntries(
        List<AceHeader> headers,
        byte[] archiveBytes,
        string tempRoot,
        AceIndexLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexWorkItem>();
        var skipped = new List<AceSkippedEntry>();
        var budget = new ExtractionBudget();
        var index = 0;

        foreach (var header in headers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (header.BlockType != AceBlockType.File)
            {
                continue;
            }

            index++;
            if (header.IsDirectory)
            {
                continue;
            }

            if (header.IsEncrypted)
            {
                skipped.Add(new AceSkippedEntry(header.Name, "ACE_ENCRYPTED",
                    "Encrypted entries cannot be extracted."));
                continue;
            }

            if (header.Method != 0 || header.IsMultiVolume)
            {
                // ACE_UNSUPPORTED rather than a damaged archive: the header is intact, the plugin simply
                // ships no decompressor and cannot join split volumes.
                skipped.Add(new AceSkippedEntry(header.Name, "ACE_UNSUPPORTED",
                    header.IsMultiVolume
                        ? "Multi-volume archive parts cannot be extracted without their companion volumes."
                        : $"Compression method {header.Method} is not supported."));
                continue;
            }

            if (index > limits.MaxEntries)
            {
                skipped.Add(new AceSkippedEntry(header.Name, "ACE_UNSUPPORTED",
                    "The archive exceeds the configured entry limit."));
                continue;
            }

            if (!AceEntryPath.TryNormalize(header.Name, out var normalized, out var safe)
                || !safe
                || !AceEntryPath.TryResolveUnderRoot(tempRoot, normalized, out var fullPath))
            {
                skipped.Add(new AceSkippedEntry(header.Name, "ACE_UNSAFE",
                    "The entry path escapes the extraction directory."));
                continue;
            }

            if (header.PackSize < 0 || header.DataOffset < 0
                || header.DataOffset > archiveBytes.Length - header.PackSize)
            {
                skipped.Add(new AceSkippedEntry(normalized, "ACE_DAMAGED",
                    "The stored entry data is outside the archive."));
                continue;
            }

            var length = header.PackSize;
            if (!budget.TryReserve(length, limits))
            {
                skipped.Add(new AceSkippedEntry(header.Name, "ACE_UNSUPPORTED",
                    "The entry exceeds the configured size budget."));
                continue;
            }

            var item = TryExtract(header, archiveBytes, normalized, fullPath, skipped);
            if (item is null)
            {
                budget.Release(length);
                continue;
            }

            items.Add(item);
        }

        progress?.Report(100);
        return new Extraction(items, skipped);
    }

    private static IndexWorkItem? TryExtract(AceHeader header, byte[] archiveBytes, string normalized,
        string fullPath, List<AceSkippedEntry> skipped)
    {
        var payload = archiveBytes.AsSpan((int)header.DataOffset, (int)header.PackSize);
        if (AceCrc32.Compute(payload) != header.FileCrc)
        {
            skipped.Add(new AceSkippedEntry(normalized, "ACE_DAMAGED",
                "The stored entry data does not match its CRC-32."));
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, payload);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(fullPath);
            skipped.Add(new AceSkippedEntry(normalized, "ACE_DAMAGED", exception.Message));
            return null;
        }

        return new IndexWorkItem(normalized, payload.Length, token => OpenExtractedAsync(fullPath, token),
            header.ModifiedUtc);
    }

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    /// <summary>
    /// Buffers the source in memory so the header list and entry payloads can be walked twice. Sources are
    /// never written to disk outside the plugin's temporary directory.
    /// </summary>
    private static async Task<byte[]> ReadAllBytesAsync(Stream source, string tempRoot,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length <= int.MaxValue)
        {
            var length = (int)source.Length;
            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                var chunk = await source.ReadAsync(buffer.AsMemory(read), cancellationToken)
                    .ConfigureAwait(false);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            return read == length ? buffer : buffer[..read];
        }

        var bufferPath = Path.Combine(tempRoot, ".skycd-ace-source");
        await using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write,
                         FileShare.None, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        return await File.ReadAllBytesAsync(bufferPath, cancellationToken).ConfigureAwait(false);
    }

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

    private sealed record Extraction(List<IndexWorkItem> Items, List<AceSkippedEntry> Skipped);

    private sealed class ExtractionBudget
    {
        private long totalBytes;

        public bool TryReserve(long bytes, AceIndexLimits limits)
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

/// <summary>An entry that was listed but not contributed for extraction, with its typed failure code.</summary>
internal sealed record AceSkippedEntry(string Name, string Code, string Reason);

/// <summary>The work items one ACE pass contributes plus the entries it skipped.</summary>
internal sealed record AceIndexResult(IndexWorkItemSet WorkItems, IReadOnlyList<AceSkippedEntry> Skipped)
{
    /// <summary>Gets a set that contributes nothing and reports one typed archive-level failure.</summary>
    public static AceIndexResult Damaged(string reason) => ForFailure("ACE_DAMAGED", reason);

    /// <summary>Gets a set that contributes nothing and reports one typed archive-level failure code.</summary>
    public static AceIndexResult ForFailure(string code, string reason) =>
        new(IndexWorkItemSet.Empty, [new AceSkippedEntry(string.Empty, code, reason)]);
}
