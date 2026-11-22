using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Metadata.Mdf.Exceptions;
using SkyCD.Plugin.Metadata.Mdf.Models;

namespace SkyCD.Plugin.Metadata.Mdf.Parsing;

/// <summary>One file or folder reported by the ISO 9660 filesystem stored in an MDF data track.</summary>
internal sealed record MdfFileEntry(
    string Path,
    string ReaderPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

/// <summary>
/// Reads the ISO 9660/Joliet filesystem stored in an MDF data track and copies single entries out of
/// the image without decrypting or extracting the whole image.
/// </summary>
internal static class MdfFileSystems
{
    private const int CopyBufferBytes = 81_920;

    /// <summary>Lists every folder and file in the data track, bounded by the configured limits.</summary>
    public static IReadOnlyList<MdfFileEntry> ReadEntries(string imagePath, MdfDataTrack track,
        MdfIndexLimits limits, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        using var image = OpenImage(imagePath);
        using var data = new MdfDataTrackStream(image, track, leaveOpen: true);
        using var reader = new CDReader(data, joliet: true, hideVersions: true);
        var entries = new List<MdfFileEntry>();
        Traverse(reader, string.Empty, depth: 0, entries, limits, cancellationToken);
        return entries;
    }

    /// <summary>Copies one stored file entry into <paramref name="destination"/>, bounded by its size limit.</summary>
    public static async Task CopyEntryAsync(string imagePath, MdfDataTrack track, string readerPath,
        Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(destination);

        using var image = OpenImage(imagePath);
        using var data = new MdfDataTrackStream(image, track, leaveOpen: true);
        using var reader = new CDReader(data, joliet: true, hideVersions: true);
        await using var input = reader.OpenFile(readerPath, FileMode.Open, FileAccess.Read);
        await CopyBoundedAsync(input, destination, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    private static FileStream OpenImage(string imagePath) =>
        new(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes);

    private static void Traverse(CDReader reader, string path, int depth, List<MdfFileEntry> entries,
        MdfIndexLimits limits, CancellationToken cancellationToken)
    {
        if (depth > limits.MaxDepth)
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
        catch (Exception exception) when (MdfImageFailures.IsImageFailure(exception))
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

    private static MdfFileEntry ReadShape(CDReader reader, string readerPath, bool isDirectory)
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
            catch (Exception exception) when (MdfImageFailures.IsImageFailure(exception))
            {
                size = 0;
                modified = null;
            }
        }

        return new MdfFileEntry(MdfEntryPath.Normalize(readerPath), readerPath, isDirectory, size, modified);
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
                    throw new MdfEntryLimitExceededException();
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
