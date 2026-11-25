using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Tests;

/// <summary>
/// Builds ARC archives in the 29-byte header layout the SharpCompress reader expects: 0x1A marker, method,
/// a 13-byte NUL-terminated name, packed size, date, CRC-16 and original size, followed by the entry data.
/// The archive is terminated with the 0x1A end-of-archive marker. Names are limited to twelve bytes because
/// the extended name layout is not supported.
/// </summary>
internal sealed class ArcTestArchiveBuilder
{
    private const byte Marker = 0x1A;
    private const byte StoredMethod = 1;
    private const byte PackedMethod = 3;
    private const byte Dle = 0x90;
    private const int NameFieldBytes = 13;
    private const uint DosDate = 1_700_000_000;

    private readonly MemoryStream stream = new();

    private ArcTestArchiveBuilder()
    {
    }

    /// <summary>Gets the number of bytes written so far, including the end marker.</summary>
    public long Length => stream.Length;

    public static ArcTestArchiveBuilder Create() => new();

    /// <summary>Writes a stored (method 1) entry whose data follows the header verbatim.</summary>
    public ArcTestArchiveBuilder AddStored(string name, string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return AddEntry(StoredMethod, name, data, data, data);
    }

    /// <summary>Writes a packed (method 3) entry whose data is RLE90-compressed.</summary>
    public ArcTestArchiveBuilder AddPacked(string name, string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return AddEntry(PackedMethod, name, EncodeRle90(data), data, EncodeRle90(data));
    }

    /// <summary>Writes an entry with a method the reader maps to a compression it cannot decode.</summary>
    public ArcTestArchiveBuilder AddWithMethod(byte method, string name, string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return AddEntry(method, name, data, data, data);
    }

    /// <summary>Writes an explicit directory entry: a name ending in a slash and no data.</summary>
    public ArcTestArchiveBuilder AddDirectory(string name)
    {
        var headerName = name.EndsWith('/') ? name : name + "/";
        return AddEntry(StoredMethod, headerName, [], [], []);
    }

    /// <summary>
    /// Writes a stored entry that advertises its full size but omits the tail of its data, so extraction
    /// reaches the end of the stream early.
    /// </summary>
    public ArcTestArchiveBuilder AddTruncatedStored(string name, string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return AddEntry(StoredMethod, name, data, data, data[..(data.Length / 2)]);
    }

    /// <summary>Produces the archive, optionally omitting the end-of-archive marker.</summary>
    public MemoryStream Build(bool withEndMarker = true)
    {
        if (withEndMarker)
        {
            stream.WriteByte(Marker);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>Copies everything written so far without an end marker, for truncation tests.</summary>
    public byte[] WithoutEndMarker() => stream.ToArray();

    private ArcTestArchiveBuilder AddEntry(byte method, string name, byte[] storedData, byte[] original,
        byte[] writtenData)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        if (nameBytes.Length >= NameFieldBytes)
        {
            throw new ArgumentException($"ARC names are limited to {NameFieldBytes - 1} bytes.", nameof(name));
        }

        stream.WriteByte(Marker);
        stream.WriteByte(method);
        stream.Write(nameBytes);
        stream.Write(new byte[NameFieldBytes - nameBytes.Length]);

        WriteUInt32((uint)storedData.Length);
        WriteUInt32(DosDate);
        WriteUInt16(Crc16(original));
        WriteUInt32((uint)original.Length);
        stream.Write(writtenData);
        return this;
    }

    /// <summary>
    /// Encodes data in the RLE90 scheme of the packed (method 3) decoder. The first byte of a run is written
    /// literally; a run of two adds one more literal byte and a longer run adds 0x90 followed by the run
    /// length. A literal 0x90 is always written as 0x90 0x00.
    /// </summary>
    private static byte[] EncodeRle90(byte[] data)
    {
        var output = new List<byte>(data.Length);
        var lastChar = -1;
        var count = 0;
        foreach (var value in data)
        {
            if (value == lastChar && count < 255)
            {
                count++;
            }
            else
            {
                EmitRun(output, lastChar, count);
                output.Add(value);
                lastChar = value;
                count = 1;
            }

            if (value == Dle)
            {
                output.Add(0);
                lastChar = -1;
            }
        }

        EmitRun(output, lastChar, count);
        return [.. output];
    }

    private static void EmitRun(List<byte> output, int lastChar, int count)
    {
        if (count == 2)
        {
            output.Add((byte)lastChar);
        }
        else if (count > 2)
        {
            output.Add(Dle);
            output.Add((byte)count);
        }
    }

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
}
