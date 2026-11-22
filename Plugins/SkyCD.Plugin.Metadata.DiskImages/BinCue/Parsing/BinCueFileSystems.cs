using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Metadata.DiskImages.BinCue.Exceptions;
using SkyCD.Plugin.Metadata.DiskImages.BinCue.Models;
using SkyCD.Plugin.Metadata.DiskImages.Exceptions;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.BinCue.Parsing;

/// <summary>One file or folder reported by the ISO 9660 filesystem stored in a BIN data track.</summary>
internal sealed record BinCueFileEntry(
    string Path,
    string ReaderPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

/// <summary>
/// Reads the ISO 9660/Joliet filesystem stored in a BIN data track and copies single entries out of the
/// BIN file without extracting the whole image.
/// </summary>
internal static class BinCueFileSystems
{
    private const int CopyBufferBytes = 81_920;

    /// <summary>Lists every folder and file in the data track, bounded by the configured limits.</summary>
    public static IReadOnlyList<BinCueFileEntry> ReadEntries(string imagePath, BinCueDataTrack track,
        DiskImageIndexLimits limits, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        using var image = OpenImage(imagePath);
        using var data = new BinCueDataTrackStream(image, track, leaveOpen: true);
        using var reader = new CDReader(data, joliet: true, hideVersions: true);
        var entries = new List<BinCueFileEntry>();
        Traverse(reader, string.Empty, depth: 0, entries, limits, cancellationToken);
        return entries;
    }

    /// <summary>Copies one stored file entry into <paramref name="destination"/>, bounded by its size limit.</summary>
    public static async Task CopyEntryAsync(string imagePath, BinCueDataTrack track, string readerPath,
        Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(destination);

        using var image = OpenImage(imagePath);
        using var data = new BinCueDataTrackStream(image, track, leaveOpen: true);
        using var reader = new CDReader(data, joliet: true, hideVersions: true);
        await using var input = reader.OpenFile(readerPath, FileMode.Open, FileAccess.Read);
        await CopyBoundedAsync(input, destination, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    private static FileStream OpenImage(string imagePath) =>
        new(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes);

    private static void Traverse(CDReader reader, string path, int depth, List<BinCueFileEntry> entries,
        DiskImageIndexLimits limits, CancellationToken cancellationToken)
    {
        if (depth > (limits.MaxDepth ?? DiskImageIndexLimits.OpticalMaxDepth))
        {
            return;
        }

        string[] directories;
        string[] files;
        var lookupPath = path.TrimStart('/', '\\');
        try
        {
            directories = reader.GetDirectories(lookupPath);
            files = reader.GetFiles(lookupPath);
        }
        catch (Exception exception) when (BinCueImageFailures.IsImageFailure(exception))
        {
            return;
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(ReadShape(reader, directory, isDirectory: true));
            Traverse(reader, directory, depth + 1, entries, limits, cancellationToken);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(ReadShape(reader, file, isDirectory: false));
        }
    }

    private static BinCueFileEntry ReadShape(CDReader reader, string readerPath, bool isDirectory)
    {
        long size = 0;
        DateTimeOffset? modified = null;
        if (!isDirectory)
        {
            try
            {
                size = reader.GetFileLength(readerPath);
                modified = new DateTimeOffset(DateTime.SpecifyKind(reader.GetLastWriteTimeUtc(readerPath),
                    DateTimeKind.Utc));
            }
            catch (Exception exception) when (BinCueImageFailures.IsImageFailure(exception))
            {
                size = 0;
                modified = null;
            }
        }

        return new BinCueFileEntry(DiskImageEntryPath.NormalizeIso9660(readerPath), readerPath, isDirectory, size, modified);
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maxBytes,
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
                    throw new DiskImageEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
