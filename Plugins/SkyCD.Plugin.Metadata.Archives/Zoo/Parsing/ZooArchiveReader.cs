using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.Metadata.Archives.Zoo.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Zoo.Models;
using SkyCD.Plugin.Metadata.Archives.Exceptions;

namespace SkyCD.Plugin.Metadata.Archives.Zoo.Parsing;

/// <summary>
/// Minimal reader for ZOO archives (format 1.0-2.10). It parses the linked directory entries and can
/// decompress stored and LZD (method 1) members; LZH members (method 2) are listed but not extracted.
/// </summary>
internal static class ZooArchiveReader
{
    private const uint ArchiveTag = 0xFDC4A7DC;
    private const int MinimumHeaderBytes = 34;
    private const int LongHeaderBytes = 42;
    private const int ShortEntryBytes = 51;
    private const int LongEntryBytes = 56;
    private const int MaxVariableEntryBytes = 527;
    private const int CopyBufferBytes = 81_920;

    /// <summary>Reads every live directory entry, following the entry chain until it terminates.</summary>
    public static IReadOnlyList<ZooArchiveEntry> ReadEntries(Stream source, int maxEntries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("ZOO archive source must be readable and seekable.", nameof(source));
        }

        var length = source.Length;
        var entriesOffset = ReadEntriesOffset(source, length, cancellationToken);
        var entries = new List<ZooArchiveEntry>();
        long position = entriesOffset;

        while (position != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position < MinimumHeaderBytes || position >= length)
            {
                throw new ZooDamagedArchiveException();
            }

            var parsed = ReadEntry(source, position, cancellationToken);
            if (parsed.NextOffset == 0)
            {
                break;
            }

            if (parsed.NextOffset < parsed.EndOffset || parsed.NextOffset >= length)
            {
                throw new ZooDamagedArchiveException();
            }

            if (!parsed.IsDeleted && parsed.Entry is not null)
            {
                ValidateDataRange(parsed.Entry, length);
                entries.Add(parsed.Entry);
                if (entries.Count >= maxEntries)
                {
                    break;
                }
            }

            position = parsed.NextOffset;
        }

        return entries;
    }

    /// <summary>
    /// Extracts one member into <paramref name="destination"/> and verifies its length and CRC.
    /// </summary>
    public static long ExtractEntry(Stream source, ZooArchiveEntry entry, Stream destination, long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("ZOO archive source must be readable and seekable.", nameof(source));
        }

        if (entry.Offset < 0 || entry.CompressedBytes < 0 || entry.OriginalBytes < 0)
        {
            throw new ZooDamagedArchiveException();
        }

        if (entry.OriginalBytes > maxBytes)
        {
            throw new ArchiveEntryLimitExceededException();
        }

        var length = source.Length;
        if (entry.Offset > length || entry.CompressedBytes > length - entry.Offset)
        {
            throw new ZooDamagedArchiveException();
        }

        source.Seek(entry.Offset, SeekOrigin.Begin);
        var crc = new ZooCrc16();
        long written;
        switch (entry.PackingMethod)
        {
            case 0:
                if (entry.CompressedBytes != entry.OriginalBytes)
                {
                    throw new ZooDamagedArchiveException();
                }

                written = CopyExactly(source, destination, entry.CompressedBytes, crc, cancellationToken);
                break;
            case 1:
                written = ZooLzdDecoder.Decode(new BoundedReadStream(source, entry.CompressedBytes), destination,
                    entry.OriginalBytes, crc, cancellationToken);
                break;
            default:
                throw new ZooUnsupportedMethodException(entry.PackingMethod);
        }

        if (written != entry.OriginalBytes || crc.Value != entry.Crc16)
        {
            throw new ZooDamagedArchiveException();
        }

        return written;
    }

    private static long ReadEntriesOffset(Stream source, long length, CancellationToken cancellationToken)
    {
        if (length < MinimumHeaderBytes)
        {
            throw new ZooDamagedArchiveException();
        }

        Span<byte> header = stackalloc byte[LongHeaderBytes];
        var headerLength = (int)Math.Min(LongHeaderBytes, length);
        source.Seek(0, SeekOrigin.Begin);
        ReadExactly(source, header[..headerLength], cancellationToken);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[20..]) != ArchiveTag)
        {
            throw new ZooDamagedArchiveException();
        }

        var start = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
        var minus = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
        if ((long)start + minus != 0)
        {
            throw new ZooDamagedArchiveException();
        }

        if (start < MinimumHeaderBytes || start > length)
        {
            throw new ZooDamagedArchiveException();
        }

        if (start != MinimumHeaderBytes && start < LongHeaderBytes)
        {
            throw new ZooDamagedArchiveException();
        }

        if (header[32] == 0)
        {
            throw new ZooDamagedArchiveException();
        }

        return start;
    }

    private static ParsedEntry ReadEntry(Stream source, long position, CancellationToken cancellationToken)
    {
        Span<byte> fixedBytes = stackalloc byte[LongEntryBytes];
        source.Seek(position, SeekOrigin.Begin);
        ReadExactly(source, fixedBytes[..ShortEntryBytes], cancellationToken);

        if (BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes) != ArchiveTag)
        {
            throw new ZooDamagedArchiveException();
        }

        var type = fixedBytes[4];
        if (type is not (1 or 2))
        {
            throw new ZooDamagedArchiveException();
        }

        var packingMethod = fixedBytes[5];
        var nextOffset = BinaryPrimitives.ReadInt32LittleEndian(fixedBytes[6..]);
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(fixedBytes[10..]);
        var dosDate = BinaryPrimitives.ReadUInt16LittleEndian(fixedBytes[14..]);
        var dosTime = BinaryPrimitives.ReadUInt16LittleEndian(fixedBytes[16..]);
        var crc = BinaryPrimitives.ReadUInt16LittleEndian(fixedBytes[18..]);
        var originalBytes = BinaryPrimitives.ReadInt32LittleEndian(fixedBytes[20..]);
        var compressedBytes = BinaryPrimitives.ReadInt32LittleEndian(fixedBytes[24..]);
        var isDeleted = fixedBytes[30] != 0;
        var shortName = ReadText(fixedBytes.Slice(38, 13));

        string longName = string.Empty;
        string directoryName = string.Empty;
        long endOffset = position + ShortEntryBytes;

        if (type == 2)
        {
            ReadExactly(source, fixedBytes[ShortEntryBytes..LongEntryBytes], cancellationToken);
            var variableLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedBytes[51..]);
            if (variableLength > MaxVariableEntryBytes)
            {
                throw new ZooDamagedArchiveException();
            }

            endOffset = position + LongEntryBytes + variableLength;
            if (variableLength > 0)
            {
                var variable = new byte[variableLength];
                ReadExactly(source, variable, cancellationToken);
                var nameLength = variable[0];
                var directoryLength = variableLength > 1 ? variable[1] : 0;
                if (2 + nameLength + directoryLength > variableLength)
                {
                    throw new ZooDamagedArchiveException();
                }

                longName = ReadText(variable.AsSpan(2, nameLength));
                directoryName = ReadText(variable.AsSpan(2 + nameLength, directoryLength));
            }
        }

        if (nextOffset < 0)
        {
            throw new ZooDamagedArchiveException();
        }

        var name = longName.Length > 0 ? longName : shortName;
        var path = CombinePath(directoryName, name);
        var entry = path.Length > 0
            ? new ZooArchiveEntry(path, packingMethod, dataOffset, compressedBytes, originalBytes, crc,
                ToModifiedUtc(dosDate, dosTime))
            : null;

        return new ParsedEntry(entry, isDeleted, nextOffset, endOffset);
    }

    private static void ValidateDataRange(ZooArchiveEntry entry, long length)
    {
        if (entry.Offset <= 0 || entry.Offset > length)
        {
            throw new ZooDamagedArchiveException();
        }

        if (entry.CompressedBytes < 0 || entry.OriginalBytes < 0
            || entry.CompressedBytes > length - entry.Offset)
        {
            throw new ZooDamagedArchiveException();
        }
    }

    private static long CopyExactly(Stream input, Stream output, long count, ZooCrc16 crc,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferBytes];
        long copied = 0;
        while (copied < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count - copied));
            if (read <= 0)
            {
                throw new ZooDamagedArchiveException();
            }

            crc.Append(buffer.AsSpan(0, read));
            output.Write(buffer, 0, read);
            copied += read;
        }

        return copied;
    }

    private static void ReadExactly(Stream source, Span<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = source.Read(buffer[read..]);
            if (count <= 0)
            {
                throw new ZooDamagedArchiveException();
            }

            read += count;
        }
    }

    private static string ReadText(ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            bytes = bytes[..terminator];
        }

        return Encoding.Latin1.GetString(bytes).Trim();
    }

    private static string CombinePath(string directory, string name)
    {
        var normalizedDirectory = directory.Replace('\\', '/').Trim('/');
        var normalizedName = name.Replace('\\', '/').TrimEnd('/');
        if (normalizedName.Trim('/').Length == 0)
        {
            return string.Empty;
        }

        return normalizedDirectory.Length == 0
            ? normalizedName
            : normalizedDirectory + "/" + normalizedName;
    }

    private static DateTimeOffset? ToModifiedUtc(ushort dosDate, ushort dosTime)
    {
        var year = 1980 + ((dosDate >> 9) & 0x7F);
        var month = (dosDate >> 5) & 0x0F;
        var day = dosDate & 0x1F;
        var hour = (dosTime >> 11) & 0x1F;
        var minute = (dosTime >> 5) & 0x3F;
        var second = (dosTime & 0x1F) * 2;

        if (month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private sealed record ParsedEntry(ZooArchiveEntry? Entry, bool IsDeleted, long NextOffset, long EndOffset);

    /// <summary>Read-only window over an archive stream that never reads past the compressed member end.</summary>
    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream inner;
        private readonly long length;
        private long remaining;

        public BoundedReadStream(Stream inner, long length)
        {
            this.inner = inner;
            this.length = length;
            remaining = length;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => length - remaining;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
            remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            remaining -= read;
            return read;
        }

        public override int ReadByte()
        {
            if (remaining <= 0)
            {
                return -1;
            }

            var value = inner.ReadByte();
            if (value >= 0)
            {
                remaining--;
            }

            return value;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
