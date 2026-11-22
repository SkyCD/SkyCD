using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.Metadata.Archives.Arc.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Arc.Models;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Parsing;

/// <summary>
/// Reads the header chain of an ARC archive. Every entry is listed from its header alone, so an entry whose
/// data cannot be decoded (encrypted or compressed with a method this plugin does not decode) does not
/// prevent the rest of the archive from being cataloged. Payload bytes are only skipped here.
/// </summary>
/// <remarks>
/// An entry is the method byte (1), the NUL-terminated name, then the fixed fields and the stored data:
/// <list type="bullet">
/// <item>16-bit layout: CRC-16, compressed size, DOS date, DOS time, 16-bit header size, data.</item>
/// <item>32-bit layout (the marker byte 0xFE terminates the name, followed by a 4-byte size extension):
/// CRC-32, 32-bit compressed size, 16-bit time, 16-bit date, and the absolute offset just past the header,
/// then data.</item>
/// </list>
/// </remarks>
internal static class ArcArchiveReader
{
    /// <summary>Marks the end of an ARC archive and terminates the header chain.</summary>
    internal const byte EndOfArchive = 0x1A;

    /// <summary>Replaces the NUL name terminator when extended size and date fields follow the name.</summary>
    internal const byte ExtendedNameMarker = 0xFE;

    private const int ExtensionBytes = 4;
    private const int ShortFixedBytes = 8;
    private const int ShortHeaderFixedBytes = 1 + ShortFixedBytes + sizeof(ushort);
    private const int LongFixedBytes = sizeof(uint) + sizeof(uint) + sizeof(ushort) + sizeof(ushort);
    private const int LongHeaderFixedBytes = 1 + LongFixedBytes + sizeof(uint);
    private const int MaxPathBytes = 4_096;
    private const string TextModeModelName = "=TX=";

    private static readonly Encoding NameEncoding = Encoding.Latin1;

    /// <summary>
    /// Enumerates every header in the archive, adding the entries that parse cleanly to <paramref name="entries"/>.
    /// The stream must be seekable and positioned at the first header, which is offset zero for a plain ARC file.
    /// A damaged header throws; the list keeps the entries found before it.
    /// </summary>
    public static void ReadEntries(Stream stream, List<ArcEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("ARC reading requires a seekable stream.", nameof(stream));
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var marker = stream.ReadByte();
            if (marker < 0)
            {
                throw new ArcDamagedArchiveException();
            }

            if (marker == EndOfArchive)
            {
                return;
            }

            entries.Add(ReadEntry(stream, (byte)marker));
        }
    }

    private static ArcEntry ReadEntry(Stream stream, byte method)
    {
        var headerStart = stream.Position - 1;
        var name = ReadName(stream);
        var isLongLayout = name.IsLongLayout;
        var fixedFields = new byte[LongFixedBytes];
        ReadExactly(stream, fixedFields.AsSpan(0, isLongLayout ? LongFixedBytes : ShortFixedBytes));

        int crc;
        int compressedSize;
        int date;
        int time;
        long dataOffset;
        if (isLongLayout)
        {
            crc = ReadInt32(fixedFields, 0);
            compressedSize = ReadInt32(fixedFields, 4);
            time = ReadUInt16(fixedFields, 8);
            date = ReadUInt16(fixedFields, 10);
            if (compressedSize < 0)
            {
                throw new ArcDamagedArchiveException();
            }

            var headerEnd = new byte[sizeof(uint)];
            ReadExactly(stream, headerEnd);
            dataOffset = ReadUInt32(headerEnd, 0);
            if (dataOffset < headerStart + LongHeaderFixedBytes + name.Length
                || dataOffset > stream.Length)
            {
                throw new ArcDamagedArchiveException();
            }
        }
        else
        {
            crc = ReadUInt16(fixedFields, 0);
            compressedSize = ReadUInt16(fixedFields, 2);
            date = ReadUInt16(fixedFields, 4);
            time = ReadUInt16(fixedFields, 6);

            var headerLength = new byte[sizeof(ushort)];
            ReadExactly(stream, headerLength);
            dataOffset = headerStart + ShortHeaderFixedBytes + name.Length;
            if (ReadUInt16(headerLength, 0) != dataOffset - headerStart)
            {
                throw new ArcDamagedArchiveException();
            }
        }

        if (dataOffset + compressedSize > stream.Length)
        {
            throw new ArcDamagedArchiveException();
        }

        stream.Position = dataOffset + compressedSize;

        var decodedName = DecodeName(name.Bytes);
        var isReserved = method == 0 && string.Equals(decodedName, TextModeModelName, StringComparison.Ordinal);
        var uncompressedSize = method switch
        {
            0 or 4 when !isReserved => compressedSize,
            7 => 0,
            _ => -1
        };

        return new ArcEntry(
            decodedName,
            isReserved ? ArcMethod.Reserved : DecodeMethod(method),
            method,
            compressedSize,
            uncompressedSize,
            dataOffset,
            IsDirectory: HasDirectoryName(decodedName),
            CrcBits: isLongLayout ? 32 : 16,
            Crc: isLongLayout ? (uint)crc : (ushort)crc,
            HasExtendedDate: isLongLayout,
            ModifiedUtc: TryDecodeTimestamp(date, time));
    }

    /// <summary>
    /// Maps the raw method byte. Stored data (methods 0, 4, and 7) is extracted; methods 1-3, 5, and 6 need
    /// decoders that are out of scope, so those entries are listed but never extracted.
    /// </summary>
    private static ArcMethod DecodeMethod(byte method) => method switch
    {
        0 or 4 or 7 => ArcMethod.Stored,
        1 => ArcMethod.Packed,
        _ => ArcMethod.Unsupported
    };

    /// <summary>
    /// Reads the NUL-terminated name. A 0xFE byte in place of the terminator switches to the 32-bit layout and
    /// is followed by a 4-byte extension that is not part of the name.
    /// </summary>
    private static ArcNameField ReadName(Stream stream)
    {
        var name = new List<byte>(32);

        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0)
            {
                throw new ArcDamagedArchiveException();
            }

            if (value == 0)
            {
                return new ArcNameField(name.ToArray(), IsLongLayout: false);
            }

            if (value == ExtendedNameMarker)
            {
                var extension = new byte[ExtensionBytes];
                ReadExactly(stream, extension);
                // 0xFE terminates the name instead of NUL; the extension belongs to the header, not the name.
                return new ArcNameField(name.ToArray(), IsLongLayout: true);
            }

            name.Add((byte)value);
            if (name.Count > MaxPathBytes)
            {
                throw new ArcDamagedArchiveException();
            }
        }
    }

    private static DateTimeOffset? TryDecodeTimestamp(int date, int time)
    {
        var day = date & 0x1F;
        var month = (date >> 5) & 0x0F;
        var year = 1980 + ((date >> 9) & 0x7F);
        var second = (time & 0x1F) * 2;
        var minute = (time >> 5) & 0x3F;
        var hour = (time >> 11) & 0x1F;

        if (month is < 1 or > 12 || day < 1 || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }

        try
        {
            return day > DateTime.DaysInMonth(year, month)
                ? null
                : new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool HasDirectoryName(string name) => name.EndsWith('/') || name.EndsWith('\\');

    private static string DecodeName(byte[] nameBytes) =>
        nameBytes.Length == 0 ? string.Empty : NameEncoding.GetString(nameBytes);

    private static int ReadUInt16(byte[] buffer, int offset) => buffer[offset] | (buffer[offset + 1] << 8);

    private static int ReadUInt32(byte[] buffer, int offset) =>
        ReadUInt16(buffer, offset) | (ReadUInt16(buffer, offset + 2) << 16);

    private static int ReadInt32(byte[] buffer, int offset) => ReadUInt32(buffer, offset);

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException)
        {
            throw new ArcDamagedArchiveException();
        }
    }

    private readonly record struct ArcNameField(byte[] Bytes, bool IsLongLayout)
    {
        /// <summary>Gets the header bytes the name field occupies, including its NUL or 0xFE terminator.</summary>
        public int Length => Bytes.Length + 1 + (IsLongLayout ? ExtensionBytes : 0);
    }
}
