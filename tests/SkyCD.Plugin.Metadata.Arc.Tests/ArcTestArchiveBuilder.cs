using System;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Metadata.Arc.Tests;

/// <summary>
/// Builds ARC archives byte by byte so tests do not depend on external tools. Header layouts mirror the reader:
/// short entries carry 16-bit CRC, size, time, date and header length; extended entries terminate the name with
/// 0xFE, append size and date/time extensions, use 32-bit fields, and end with the absolute header offset.
/// </summary>
internal sealed class ArcTestArchiveBuilder
{
    private const byte EndOfArchive = 0x1A;
    private const byte ExtendedNameMarker = 0xFE;
    private const int ExtensionBytes = 4;
    private const int FixedLongBytes = 4 + 4 + 2 + 2 + 4; // CRC-32, size, time, date, header end offset

    private readonly MemoryStream stream = new();

    private ArcTestArchiveBuilder()
    {
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    public long Length => stream.Length;

    public static ArcTestArchiveBuilder Create() => new();

    /// <summary>Writes a stored entry using the short (16-bit) header layout.</summary>
    public ArcTestArchiveBuilder AddStored(string name, string content, DateTime? modified = null) =>
        AddShort(name, method: 0, Encoding.UTF8.GetBytes(content), modified ?? DefaultDate);

    /// <summary>Writes an entry whose method byte the reader must classify without decoding the data.</summary>
    public ArcTestArchiveBuilder AddWithMethod(byte method, string name, string content) =>
        AddShort(name, method, Encoding.UTF8.GetBytes(content), DefaultDate);

    /// <summary>Writes an explicit directory entry: a name ending in a slash and no data.</summary>
    public ArcTestArchiveBuilder AddDirectory(string name) =>
        AddShort(name.TrimEnd('/') + "/", method: 0, [], DefaultDate);

    /// <summary>
    /// Writes a stored entry using the extended (32-bit) layout: the name ends with 0xFE, a 4-byte extension
    /// follows, then CRC-32, size, time, date, and the absolute header end offset.
    /// </summary>
    public ArcTestArchiveBuilder AddStoredExtended(string name, string content, DateTime? modified = null)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var nameBytes = Encoding.Latin1.GetByteCount(name);
        var value = modified ?? DefaultDate;

        stream.WriteByte(0);
        stream.Write(Encoding.Latin1.GetBytes(name));
        stream.WriteByte(ExtendedNameMarker);
        stream.Write([1, 0, 0, 0]); // one extension: this header's index within the archive

        var headerStart = stream.Length - (1 + nameBytes + 1 + ExtensionBytes);
        // The extended name terminator replaces the NUL, so it counts toward the header length too.
        var headerEnd = headerStart + 1 + nameBytes + 1 + ExtensionBytes + FixedLongBytes;
        WriteUInt32((uint)Crc32(data));
        WriteUInt32((uint)data.Length);
        WriteUInt16(DosTime(value));
        WriteUInt16(DosDate(value));
        WriteUInt32((uint)headerEnd);
        stream.Write(data);
        return this;
    }

    /// <summary>Writes the "=TX=" text-mode settings header that old archives place before text files.</summary>
    public ArcTestArchiveBuilder AddTextModeModel()
    {
        WriteShortcutHeader(method: 0, "=TX=");
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16(DosDate(DefaultDate));
        WriteUInt16(DosTime(DefaultDate));
        WriteUInt16(1 + Encoding.Latin1.GetByteCount("=TX=") + 1 + 8 + 2);
        return this;
    }

    /// <summary>Produces the archive, optionally omitting the end-of-archive marker.</summary>
    public MemoryStream Build(bool withEndMarker = true)
    {
        if (withEndMarker)
        {
            stream.WriteByte(EndOfArchive);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>Copies everything written so far without an end marker, for truncation tests.</summary>
    public byte[] WithoutEndMarker() => stream.ToArray();

    private ArcTestArchiveBuilder AddShort(string name, byte method, byte[] data, DateTime modified)
    {
        WriteShortcutHeader(method, name);
        WriteUInt16(Crc16(data));
        WriteUInt16((ushort)data.Length);
        WriteUInt16(DosDate(modified));
        WriteUInt16(DosTime(modified));
        WriteUInt16(1 + Encoding.Latin1.GetByteCount(name) + 1 + 8 + 2);
        stream.Write(data);
        return this;
    }

    private void WriteShortcutHeader(byte method, string name)
    {
        stream.WriteByte(method);
        stream.Write(Encoding.Latin1.GetBytes(name));
        stream.WriteByte(0);
    }

    private static DateTime DefaultDate => new(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

    private static ushort DosDate(DateTime value) =>
        (ushort)(((value.Year - 1980) << 9) | (value.Month << 5) | value.Day);

    private static ushort DosTime(DateTime value) =>
        (ushort)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));

    private void WriteBytes(byte[] value) => stream.Write(value);

    private void WriteUInt16(int value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private void WriteUInt32(uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private static ushort Crc16(byte[] data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
