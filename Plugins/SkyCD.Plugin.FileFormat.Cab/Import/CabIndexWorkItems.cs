using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Cab.Exceptions;
using SkyCD.Plugin.FileFormat.Cab.Models;
using SkyCD.Plugin.FileFormat.Cab.Parsing;

namespace SkyCD.Plugin.FileFormat.Cab.Import;

/// <summary>
/// Shared cabinet handling for the read (listing) and indexing (extraction) paths. Listing parses only
/// headers; extraction decodes the folder streams into a unique temporary directory.
/// </summary>
internal static class CabIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;

    /// <summary>
    /// Extracts the entries of the first cabinet file only. Returns an empty set for non-cabinet input or
    /// damaged, multi-volume, and unsupported cabinets; the temporary directory is removed in every case.
    /// </summary>
    public static async Task<IndexWorkItemSet> CreateAsync(
        IndexWorkItemRequest request,
        CabIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"skycd-cab-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                var bufferPath = Path.Combine(tempRoot, ".skycd-cab-source");
                await using (var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                }

                bufferedSource = new FileStream(bufferPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                archiveStream = bufferedSource;
            }

            if (!CabArchive.HasSignature(archiveStream))
            {
                DeleteTemporaryDirectory(tempRoot);
                return IndexWorkItemSet.Empty;
            }

            var archive = CabArchive.Read(archiveStream);
            if (archive.IsMultiVolume)
            {
                // Only the first cabinet of a set is available, so a partial listing would be misleading.
                DeleteTemporaryDirectory(tempRoot);
                return IndexWorkItemSet.Empty;
            }

            var items = await ExtractEntriesAsync(archiveStream, archive, tempRoot, limits, request.Progress,
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

    /// <summary>
    /// Lists the catalog rows for the first cabinet file only. Returns a typed failure when the input is
    /// damaged or belongs to an unavailable multi-volume set.
    /// </summary>
    public static Task<(bool Success, string? Error, List<Dictionary<string, object?>> Rows)> ListAsync(
        Stream source,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!CabArchive.HasSignature(source))
            {
                return Task.FromResult((false, (string?)"CAB_DAMAGED: The source does not start with a cabinet (MSCF) header.", new List<Dictionary<string, object?>>()));
            }

            var archive = CabArchive.Read(source);
            progress?.Report(100);
            return Task.FromResult((true, (string?)null, BuildRows(archive)));
        }
        catch (Exception exception) when (exception is CabDamagedException or IOException or ArgumentException
                                              or InvalidOperationException or NotSupportedException)
        {
            return Task.FromResult((false, (string?)($"CAB_DAMAGED: {exception.Message}"), new List<Dictionary<string, object?>>()));
        }
    }

    private static List<Dictionary<string, object?>> BuildRows(CabArchive archive)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in archive.Files)
        {
            if (!CabEntryPath.TryNormalize(file.Name, out var normalized, out var safe) || !safe)
            {
                continue;
            }

            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            for (var index = 0; index < parts.Length - 1; index++)
            {
                var directoryPath = string.Join('/', parts.Take(index + 1));
                if (!seenDirectories.Add(directoryPath))
                {
                    continue;
                }

                rows.Add(Row("folder", directoryPath, parts[index], 0, file.ModifiedUtc));
            }

            rows.Add(Row("file", normalized, parts[^1], file.Size, file.ModifiedUtc));
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, object?> Row(string kind, string fullPath, string name, long size,
        DateTimeOffset modified) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind,
            ["fullPath"] = fullPath,
            ["name"] = name,
            ["sizeBytes"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["modifiedUtc"] = modified.ToString("O")
        };

    private static async Task<List<IndexWorkItem>> ExtractEntriesAsync(
        Stream archiveStream,
        CabArchive archive,
        string tempRoot,
        CabIndexLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexWorkItem>();
        var budget = new ExtractionBudget();
        var folderStarts = new long[archive.Folders.Count];
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);

        await Task.CompletedTask.ConfigureAwait(false);

        if (archive.Files.Count == 0)
        {
            progress?.Report(100);
            return items;
        }

        // Folders are stored contiguously in header order, so each folder starts where the previous one ended.
        var start = archive.CabinetStart;
        for (var index = 0; index < archive.Folders.Count; index++)
        {
            folderStarts[index] = start;
            start = AdvanceFolder(archiveStream, start, archive.Folders[index], archive.FolderReserveBytes);
        }

        var filesByFolder = archive.Files
            .GroupBy(file => file.FolderIndex)
            .OrderBy(group => group.Key);

        foreach (var group in filesByFolder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = archive.Folders[group.Key];
            using var folderStream = new CabFolderStream(archiveStream, folder, folderStarts[group.Key],
                archive.FolderReserveBytes);

            if (!folderStream.IsSupported)
            {
                // The folder needs a decoder this plugin does not have; its members stay listable but are skipped.
                continue;
            }

            long folderPosition = 0;
            foreach (var file in group.OrderBy(entry => entry.OffsetInFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.OffsetInFolder < folderPosition
                    || !TryReserveOffset(folderStream, file.OffsetInFolder - folderPosition, cancellationToken))
                {
                    break;
                }

                folderPosition = file.OffsetInFolder;

                var candidate = string.Empty;
                var fullPathCandidate = string.Empty;
                var safeName = CabEntryPath.TryNormalize(file.Name, out candidate, out var safe);
                var keep = safeName
                           && safe
                           && uniqueNames.Add(candidate)
                           && CabEntryPath.TryResolveUnderRoot(tempRoot, candidate, out fullPathCandidate)
                           && !budget.IsExhausted(uniqueNames.Count, file.Size, limits);
                var normalized = candidate;
                var fullPath = fullPathCandidate;
                if (!keep)
                {
                    // Unsafe, duplicate, or over-budget: skip the member's bytes without writing anything.
                    folderStream.Skip(file.Size, cancellationToken);
                    folderPosition += file.Size;
                    continue;
                }

                budget.Reserve(file.Size);
                var (extracted, complete) = await ExtractEntryAsync(folderStream, file, fullPath, limits, budget,
                    cancellationToken).ConfigureAwait(false);
                folderPosition += extracted;
                if (!complete)
                {
                    // The data ran out or no longer matches the folder layout, so the rest is unreadable.
                    break;
                }

                items.Add(new IndexWorkItem(normalized, file.Size,
                    token => OpenExtractedAsync(fullPath, token), file.ModifiedUtc));
            }

            progress?.Report((int)(100L * (group.Key + 1) / archive.Folders.Count));
        }

        return items;
    }

    private static bool TryReserveOffset(CabFolderStream folderStream, long distance,
        CancellationToken cancellationToken)
    {
        if (distance <= 0)
        {
            return true;
        }

        return folderStream.Skip(distance, cancellationToken) == distance;
    }

    /// <summary>
    /// Writes one member out of the folder stream. Returns the number of folder bytes consumed and whether
    /// the folder layout can still be trusted afterwards.
    /// </summary>
    private static async Task<(long Consumed, bool Complete)> ExtractEntryAsync(
        CabFolderStream folderStream,
        CabFile file,
        string fullPath,
        CabIndexLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        long written = 0;
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
                written = await CopyBoundedAsync(folderStream, output, file.Size, limits.MaxEntryBytes,
                    cancellationToken).ConfigureAwait(false);
            }

            if (written < file.Size)
            {
                // The folder ended before the declared entry size: the rest of the folder is unreadable.
                budget.Release(file.Size);
                TryDeleteFile(fullPath);
                return (written, false);
            }

            return (written, true);
        }
        catch (OperationCanceledException)
        {
            budget.Release(file.Size);
            TryDeleteFile(fullPath);
            throw;
        }
        catch (CabEntryLimitExceededException)
        {
            budget.Release(file.Size);
            TryDeleteFile(fullPath);
            return (written, false);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            budget.Release(file.Size);
            TryDeleteFile(fullPath);
            return (written, false);
        }
    }

    private static async Task<long> CopyBoundedAsync(
        Stream input,
        Stream output,
        long entrySize,
        long maxEntryBytes,
        CancellationToken cancellationToken)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            long remainingInEntry = Math.Max(entrySize, 0);
            long written = 0;
            while (remainingInEntry > 0)
            {
                var wanted = (int)Math.Min(remainingInEntry, buffer.Length);
                var read = ReadFrom(input, buffer, wanted, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                written += read;
                if (written > maxEntryBytes)
                {
                    throw new CabEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remainingInEntry -= read;
            }

            return written;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    /// <summary>
    /// Walks the CFDATA frames of one folder in the source cabinet and returns the offset of the block
    /// that follows it.
    /// </summary>
    private static long AdvanceFolder(Stream archiveStream, long start, CabFolder folder, int reserveBytes)
    {
        var position = start;
        var reserve = Math.Max(reserveBytes, 0);
        var header = new byte[8];
        for (var block = 0; block < folder.DataBlockCount; block++)
        {
            archiveStream.Position = position;
            if (ReadExactly(archiveStream, header) != header.Length)
            {
                throw new CabDamagedException();
            }

            var cbData = header[4] | (header[5] << 8);
            if (cbData < 8 + reserve)
            {
                throw new CabDamagedException();
            }

            position += cbData;
        }

        return position;
    }

    private static int ReadFrom(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        if (stream is CabFolderStream folderStream)
        {
            return folderStream.Read(buffer, 0, count, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return stream.Read(buffer, 0, count);
    }

    private static int ReadExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is CabDamagedException or CabUnsupportedEntryException
            or IOException or NotSupportedException or ArgumentException or InvalidOperationException;

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

        public bool IsExhausted(int entryCount, long bytes, CabIndexLimits limits) =>
            entryCount > limits.MaxEntries
            || bytes < 0
            || bytes > limits.MaxEntryBytes
            || totalBytes + bytes > limits.MaxTotalBytes;

        public void Reserve(long bytes) => totalBytes += Math.Max(bytes, 0);

        public void Release(long bytes)
        {
            if (bytes > 0)
            {
                totalBytes -= bytes;
            }
        }
    }
}
