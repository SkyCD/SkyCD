using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using SkyCD.Plugin.Metadata.DiskImages.Iso.Exceptions;
using SkyCD.Plugin.Metadata.DiskImages.Iso.Models;
using SkyCD.Plugin.Metadata.DiskImages.Exceptions;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.Iso.Parsing;

/// <summary>
/// Opens the ISO 9660 or UDF filesystem stored in a disk image, lists its entries and copies single
/// entries out of it.
/// </summary>
internal static class IsoFileSystems
{
    private const int CopyBufferBytes = 81_920;
    private const int MaxDirectoryDepth = 256;

    /// <summary>Lists every folder and file in the image's filesystem.</summary>
    public static IReadOnlyList<IsoImageEntry> ReadEntries(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureSeekable(source);

        var kind = Detect(source);
        source.Position = 0;
        using var fileSystem = Open(source, kind);
        var entries = new List<IsoImageEntry>();
        Traverse(fileSystem, string.Empty, kind, entries, depth: 0);
        return entries;
    }

    /// <summary>Copies one stored file entry into <paramref name="destination"/>, bounded by its size limit.</summary>
    public static async Task CopyEntryAsync(Stream source, string readerPath, Stream destination, long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(readerPath);
        ArgumentNullException.ThrowIfNull(destination);
        EnsureSeekable(source);

        var kind = Detect(source);
        source.Position = 0;
        using var fileSystem = Open(source, kind);
        await using var input = fileSystem.OpenFile(readerPath, FileMode.Open, FileAccess.Read);
        await CopyBoundedAsync(input, destination, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether an exception means the image is damaged, unreadable, or uses an unsupported feature.</summary>
    public static bool IsImageFailure(Exception exception) =>
        exception is InvalidDataException or EndOfStreamException or IOException or NotSupportedException
            or ArgumentException or InvalidOperationException or UnauthorizedAccessException;

    private static IsoImageKind Detect(Stream source)
    {
        source.Position = 0;
        bool isUdf;
        try
        {
            isUdf = UdfReader.Detect(source);
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            isUdf = false;
        }

        source.Position = 0;
        bool isIso9660;
        try
        {
            isIso9660 = CDReader.Detect(source);
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            isIso9660 = false;
        }

        source.Position = 0;
        if (isUdf)
        {
            return IsoImageKind.Udf;
        }

        if (isIso9660)
        {
            return IsoImageKind.Iso9660;
        }

        throw new IsoUnsupportedImageException("no ISO 9660 or UDF volume was found.");
    }

    private static DiscFileSystem Open(Stream source, IsoImageKind kind)
    {
        source.Position = 0;
        var readerSource = new NonClosingStream(source);
        return kind switch
        {
            IsoImageKind.Udf => new UdfReader(readerSource),
            _ => new CDReader(readerSource, joliet: true, hideVersions: true)
        };
    }

    private static void Traverse(DiscFileSystem fileSystem, string lookupPath, IsoImageKind kind,
        List<IsoImageEntry> entries, int depth)
    {
        if (depth > MaxDirectoryDepth)
        {
            return;
        }

        string[] directories;
        string[] files;
        try
        {
            directories = fileSystem.GetDirectories(lookupPath);
            files = fileSystem.GetFiles(lookupPath);
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            if (depth == 0)
            {
                throw new IsoImageFormatException(exception.Message);
            }

            return;
        }

        foreach (var directory in directories)
        {
            Add(entries, fileSystem, directory, isDirectory: true, kind);
            Traverse(fileSystem, directory, kind, entries, depth + 1);
        }

        foreach (var file in files)
        {
            Add(entries, fileSystem, file, isDirectory: false, kind);
        }
    }

    private static void Add(List<IsoImageEntry> entries, DiscFileSystem fileSystem, string readerPath,
        bool isDirectory, IsoImageKind kind)
    {
        var path = DiskImageEntryPath.NormalizeOptical(readerPath, kind == IsoImageKind.Iso9660);
        if (path.Length == 0)
        {
            return;
        }

        long size = 0;
        if (!isDirectory)
        {
            try
            {
                size = Math.Max(fileSystem.GetFileLength(readerPath), 0);
            }
            catch (Exception exception) when (IsImageFailure(exception))
            {
                return;
            }
        }

        DateTimeOffset? modified = null;
        try
        {
            modified = ToUtc(fileSystem.GetLastWriteTimeUtc(readerPath));
        }
        catch (Exception exception) when (IsImageFailure(exception))
        {
            modified = null;
        }

        entries.Add(new IsoImageEntry(path, readerPath, isDirectory, size, modified));
    }

    private static DateTimeOffset? ToUtc(DateTime value) =>
        value == default
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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

    private static void EnsureSeekable(Stream source)
    {
        if (!source.CanSeek)
        {
            throw new IsoImageFormatException("the source stream is not seekable");
        }
    }

    /// <summary>Keeps ownership of the image source with the plugin while DiscUtils disposes its reader.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override int ReadByte() => inner.ReadByte();

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
        }
    }
}
