using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Nrg.Exceptions;
using SkyCD.Plugin.Nrg.Models;

namespace SkyCD.Plugin.Nrg.Parsing;

/// <summary>
/// Reads the ISO 9660/Joliet filesystem stored in an NRG data track and copies single entries out of it.
/// </summary>
internal static class NrgFileSystems
{
    private const int CopyBufferBytes = 81_920;

    /// <summary>Lists every folder and file in the image's filesystem.</summary>
    public static IReadOnlyList<NrgImageEntry> ReadEntries(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var reader = OpenReader(source);
        var entries = new List<NrgImageEntry>();
        Traverse(reader, string.Empty, entries);
        return entries;
    }

    /// <summary>Copies one stored file entry into <paramref name="destination"/>, bounded by its size limit.</summary>
    public static async Task CopyEntryAsync(Stream source, string readerPath, Stream destination, long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        using var reader = OpenReader(source);
        await using var input = reader.OpenFile(readerPath, FileMode.Open, FileAccess.Read);
        await CopyBoundedAsync(input, destination, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    private static CDReader OpenReader(Stream source)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var track = NrgImageReader.ReadDataTrack(source);
        var data = new NrgDataTrackStream(source, track, leaveOpen: true);
        return new CDReader(data, joliet: true);
    }

    private static void Traverse(CDReader reader, string path, List<NrgImageEntry> entries)
    {
        var lookupPath = path.TrimStart('/', '\\');

        foreach (var directory in reader.GetDirectories(lookupPath))
        {
            Add(entries, reader, directory, isDirectory: true);
            Traverse(reader, directory, entries);
        }

        foreach (var file in reader.GetFiles(lookupPath))
        {
            Add(entries, reader, file, isDirectory: false);
        }
    }

    private static void Add(List<NrgImageEntry> entries, CDReader reader, string readerPath, bool isDirectory)
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
            catch (Exception exception) when (IsImageFailure(exception))
            {
                size = 0;
                modified = null;
            }
        }

        entries.Add(new NrgImageEntry(NrgEntryPath.Normalize(readerPath), readerPath, isDirectory, size, modified));
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
                    throw new NrgEntryLimitExceededException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsImageFailure(Exception exception) =>
        exception is InvalidDataException or IOException or NotSupportedException or ArgumentException;
}

/// <summary>One folder or file discovered inside an NRG image.</summary>
internal sealed record NrgImageEntry(
    string Path,
    string ReaderPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);
