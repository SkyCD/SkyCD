using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cab;
using RecursiveAI.OneCompress;
using SkyCD.PackagePaths;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.Archives.Cab.Import;

/// <summary>
/// Cabinet handling for the indexing path. Listing and decoding are delegated to RecursiveAI.OneArchive,
/// which supports stored, MSZIP and LZX folders. Entries are copied one at a time into a unique temporary
/// directory with the archive limits enforced by this plugin.
/// </summary>
internal static class CabIndexWorkItems
{
    private const int CopyBufferBytes = 81_920;
    private const int LegacyPrefixBytes = 2;

    /// <summary>
    /// Extracts the entries of the first cabinet file only. Returns an empty set for non-cabinet input or
    /// damaged, multi-volume, and unsupported cabinets; the temporary directory is removed in every case.
    /// </summary>
    public static async Task<CabIndexResult> CreateAsync(
        IndexWorkItemRequest request,
        ArchiveIndexLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        limits.Validate();

        var workspace = new ArchiveExtractionWorkspace("cab");

        Stream? bufferedSource = null;
        try
        {
            await using var source = await request.OpenSource(cancellationToken).ConfigureAwait(false);
            var archiveStream = source;
            if (!source.CanSeek)
            {
                bufferedSource = await NonSeekableSourceBuffer.BufferAsync(source,
                    workspace.GetPath(".skycd-cab-source"), cancellationToken).ConfigureAwait(false);
                archiveStream = bufferedSource;
            }

            if (!HasSignature(archiveStream))
            {
                workspace.Dispose();
                return CabIndexResult.Empty;
            }

            archiveStream.Position = 0;
            using var reader = new CabReader(archiveStream, CreateReaderOptions(limits), leaveOpen: true);
            var header = reader.Header;
            if (IsMultiVolume(header))
            {
                // Only the first cabinet of a set is available, so a partial listing would be misleading.
                workspace.Dispose();
                return CabIndexResult.Empty;
            }

            var legacyNames = ReadLegacyNames(archiveStream, header, reader.Items.Count);
            var extraction = await ExtractEntriesAsync(reader, legacyNames, workspace.Root, limits,
                request.Progress, cancellationToken).ConfigureAwait(false);

            var set = new IndexWorkItemSet(extraction.Items, () =>
            {
                workspace.Dispose();
                return ValueTask.CompletedTask;
            });
            return new CabIndexResult(set, extraction.Skipped);
        }
        catch (OperationCanceledException)
        {
            workspace.Dispose();
            throw;
        }
        catch (ArchiveSafetyException exception)
        {
            workspace.Dispose();
            return CabIndexResult.ForFailure("CAB_UNSAFE", exception.Message);
        }
        catch (ArchiveUnsupportedException exception)
        {
            workspace.Dispose();
            return CabIndexResult.ForFailure("CAB_UNSUPPORTED", exception.Message);
        }
        catch (CompressionException exception)
        {
            workspace.Dispose();
            return CabIndexResult.ForFailure("CAB_DAMAGED", exception.Message);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            workspace.Dispose();
            return CabIndexResult.ForFailure("CAB_DAMAGED", exception.Message);
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

    private static CabReaderOptions CreateReaderOptions(ArchiveIndexLimits limits) => new()
    {
        // The plugin enforces its own entry and size budgets while copying, so the reader limits only act
        // as a backstop and never reject a cabinet the plugin would otherwise accept in part.
        Limits = new ArchiveLimits
        {
            MaxEntryCount = Math.Max(limits.MaxEntries, ArchiveLimits.Default.MaxEntryCount),
            MaxTotalSize = Math.Max(limits.MaxTotalBytes, ArchiveLimits.Default.MaxTotalSize)
        },
        LegacyNameEncoding = Encoding.Latin1
    };

    private static async Task<Extraction> ExtractEntriesAsync(
        CabReader reader,
        IReadOnlyList<string>? legacyNames,
        string tempRoot,
        ArchiveIndexLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexWorkItem>();
        var skipped = new List<CabSkippedEntry>();
        var budget = new ExtractionBudget();
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);

        await Task.CompletedTask.ConfigureAwait(false);

        if (reader.Items.Count == 0)
        {
            progress?.Report(100);
            return new Extraction(items, skipped);
        }

        var groups = reader.Items
            .GroupBy(entry => entry.FolderIndex)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = reader.Folders[group.Key];
            var supported = IsSupportedFolder(folder);

            foreach (var entry in group.OrderBy(candidate => candidate.FolderOffset))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Kind != ArchiveEntryKind.File)
                {
                    continue;
                }

                if (entry.Continuation != CabContinuation.None)
                {
                    skipped.Add(new CabSkippedEntry(NameOf(entry, legacyNames), "CAB_UNSUPPORTED",
                        "The entry starts in or continues into another cabinet."));
                    continue;
                }

                if (!supported)
                {
                    // The folder needs a decoder this plugin does not have; its members are listed but not extracted.
                    skipped.Add(new CabSkippedEntry(NameOf(entry, legacyNames), "CAB_UNSUPPORTED",
                        $"Compression type 0x{folder.RawCompressionType:X4} is not supported."));
                    continue;
                }

                var candidate = string.Empty;
                var fullPathCandidate = string.Empty;
                var safeName = PackageEntryPath.TryNormalize(NameOf(entry, legacyNames), out candidate, out var safe);
                var keep = safeName
                           && safe
                           && uniqueNames.Add(candidate)
                           && PackageEntryPath.TryResolveUnderRoot(tempRoot, candidate, out fullPathCandidate)
                           && !budget.IsExhausted(uniqueNames.Count, entry.Size, limits);
                var normalized = candidate;
                var fullPath = fullPathCandidate;
                if (!keep)
                {
                    // Unsafe, duplicate, or over-budget: the entry contributes no work item.
                    continue;
                }

                budget.Reserve(entry.Size);
                var (written, complete) = await ExtractEntryAsync(reader, entry, fullPath, limits,
                    budget, cancellationToken).ConfigureAwait(false);
                if (!complete)
                {
                    skipped.Add(new CabSkippedEntry(normalized, "CAB_DAMAGED",
                        $"The entry data ended after {written} of {entry.Size} bytes."));
                    continue;
                }

                items.Add(new IndexWorkItem(normalized, entry.Size,
                    token => OpenExtractedAsync(fullPath, token), entry.ModifiedTime));
            }

            progress?.Report((int)(100L * (group.Key + 1) / reader.Folders.Count));
        }

        return new Extraction(items, skipped);
    }

    private static bool IsSupportedFolder(CabFolder folder) =>
        folder.Compression is CabCompression.None or CabCompression.MsZip or CabCompression.Lzx;

    /// <summary>
    /// Writes one member out of the folder stream. Returns the number of bytes written and whether the
    /// entry data matched its declared size.
    /// </summary>
    private static async Task<(long Written, bool Complete)> ExtractEntryAsync(
        CabReader reader,
        CabEntry entry,
        string fullPath,
        ArchiveIndexLimits limits,
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

            await using var input = reader.OpenEntryStream(entry);
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            written = await CopyBoundedAsync(input, output, entry.Size, limits.MaxEntryBytes,
                cancellationToken).ConfigureAwait(false);
            if (written < entry.Size)
            {
                budget.Release(entry.Size);
                return (written, false);
            }

            return (written, true);
        }
        catch (OperationCanceledException)
        {
            budget.Release(entry.Size);
            throw;
        }
        catch (ArchiveEntryLimitExceededException)
        {
            budget.Release(entry.Size);
            return (written, false);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            budget.Release(entry.Size);
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
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            long remainingInEntry = Math.Max(entrySize, 0);
            long written = 0;
            while (remainingInEntry > 0)
            {
                var wanted = (int)Math.Min(remainingInEntry, buffer.Length);
                var read = await input.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                written += read;
                if (written > maxEntryBytes)
                {
                    throw new ArchiveEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remainingInEntry -= read;
            }

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task<Stream> OpenExtractedAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static string NameOf(CabEntry entry, IReadOnlyList<string>? legacyNames) =>
        legacyNames is not null && entry.Index >= 0 && entry.Index < legacyNames.Count
            ? legacyNames[entry.Index]
            : entry.Name;

    /// <summary>
    /// Cabinets older than version 1.3 store a two-byte DOS attribute word in front of every file name.
    /// The reader stops at the zero inside that word, so the attribute prefix is removed here.
    /// </summary>
    private static IReadOnlyList<string>? ReadLegacyNames(Stream stream, CabHeader header, int fileCount)
    {
        if (header.FormatVersion.Minor >= 3 || fileCount == 0)
        {
            return null;
        }

        stream.Position = header.FilesOffset;
        var names = new List<string>(fileCount);
        var record = new byte[16];
        var prefix = new byte[LegacyPrefixBytes];
        for (var index = 0; index < fileCount; index++)
        {
            if (ReadExactly(stream, record) != record.Length
                || ReadExactly(stream, prefix) != prefix.Length)
            {
                return names;
            }

            names.Add(DecodeLegacyName(ReadZeroTerminated(stream)));
        }

        return names;
    }

    private static string DecodeLegacyName(byte[] raw) =>
        Encoding.Latin1.GetString(raw).Replace('\\', '/');

    private static byte[] ReadZeroTerminated(Stream stream)
    {
        var bytes = new byte[256];
        var length = 0;
        while (length < bytes.Length)
        {
            var value = stream.ReadByte();
            if (value <= 0)
            {
                break;
            }

            bytes[length++] = (byte)value;
        }

        return bytes.AsSpan(0, length).ToArray();
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

    private static bool HasSignature(Stream stream)
    {
        stream.Position = 0;
        Span<byte> signature = stackalloc byte[4];
        return stream.Read(signature) == signature.Length && signature.SequenceEqual("MSCF"u8);
    }

    private static bool IsMultiVolume(CabHeader header) =>
        !string.IsNullOrEmpty(header.PreviousCabinet)
        || !string.IsNullOrEmpty(header.NextCabinet)
        || (header.Flags & (CabHeaderFeatures.PreviousCabinet | CabHeaderFeatures.NextCabinet)) != 0;

    private static bool IsArchiveFailure(Exception exception) =>
        exception is CompressionException or ArchiveUnsupportedException or ArchiveSafetyException
            or IOException or NotSupportedException or ArgumentException or InvalidOperationException;

    private sealed record Extraction(List<IndexWorkItem> Items, List<CabSkippedEntry> Skipped);

    private sealed class ExtractionBudget
    {
        private long totalBytes;

        public bool IsExhausted(int entryCount, long bytes, ArchiveIndexLimits limits) =>
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

/// <summary>An entry that was listed but not contributed for extraction, with its typed failure code.</summary>
internal sealed record CabSkippedEntry(string Name, string Code, string Reason);

/// <summary>The work items one cabinet pass contributes plus the entries it skipped.</summary>
internal sealed record CabIndexResult(IndexWorkItemSet WorkItems, IReadOnlyList<CabSkippedEntry> Skipped)
{
    /// <summary>Gets a set that contributes nothing and owns no resources.</summary>
    public static CabIndexResult Empty { get; } = new(IndexWorkItemSet.Empty, []);

    /// <summary>Gets a set that contributes nothing and reports one typed archive-level failure code.</summary>
    public static CabIndexResult ForFailure(string code, string reason) =>
        new(IndexWorkItemSet.Empty, [new CabSkippedEntry(string.Empty, code, reason)]);
}
