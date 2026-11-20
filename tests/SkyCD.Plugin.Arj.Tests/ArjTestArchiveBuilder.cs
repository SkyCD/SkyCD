using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Arj.Tests;

/// <summary>
/// Builds minimal stored-method ARJ archives so tests do not depend on binary fixtures or external tools.
/// </summary>
internal static class ArjTestArchiveBuilder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static MemoryStream CreateStored(params (string Path, string Content)[] entries) =>
        Create(flags: 0, entries);

    public static MemoryStream CreateEncrypted(params (string Path, string Content)[] entries) =>
        Create(flags: 0x01, entries);

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not an arj archive"));

    private static MemoryStream Create(byte flags, (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        var modified = new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

        WriteMainHeader(stream, flags, modified);
        foreach (var (path, content) in entries)
        {
            WriteFileEntry(stream, path, Encoding.UTF8.GetBytes(content), modified);
        }

        WriteEndMarker(stream);
        stream.Position = 0;
        return stream;
    }

    private static void WriteMainHeader(Stream stream, byte flags, DateTime modified)
    {
        var body = new List<byte>
        {
            30, // basic header size
            11, // archiver version
            1, // minimum version to extract
            0, // host OS (MS-DOS)
            flags, // main header flags (0x01 marks an encrypted archive)
            0, // security version
            0, // file type
            0 // reserved
        };
        body.AddRange(UInt32Bytes(ToDosDateTime(modified)));
        body.AddRange(new byte[4]); // compressed size
        body.AddRange(new byte[4]); // archive size
        body.AddRange(new byte[4]); // security envelope
        body.AddRange(new byte[2]); // file spec position
        body.AddRange(new byte[2]); // security envelope length
        body.Add(0); // encryption version
        body.Add(0); // last chapter
        AddNullTerminated(body, "skycd-test");
        AddNullTerminated(body, string.Empty);

        WriteHeader(stream, body);
    }

    private static void WriteFileEntry(Stream stream, string path, byte[] content, DateTime modified)
    {
        var body = new List<byte>
        {
            30, // basic header size
            11, // archiver version
            1, // minimum version to extract
            0, // host OS (MS-DOS)
            0, // local header flags
            0, // compression method (stored)
            0, // file type (binary)
            0 // reserved
        };
        body.AddRange(UInt32Bytes(ToDosDateTime(modified)));
        body.AddRange(UInt32Bytes((uint)content.Length)); // compressed size
        body.AddRange(UInt32Bytes((uint)content.Length)); // original size
        body.AddRange(UInt32Bytes(ComputeCrc(content))); // original CRC-32
        body.AddRange(new byte[2]); // file spec position
        body.AddRange(new byte[2]); // file access mode
        body.Add(0); // first chapter
        body.Add(0); // last chapter
        AddNullTerminated(body, path);
        AddNullTerminated(body, string.Empty);

        WriteHeader(stream, body);
        stream.Write(content);
    }

    private static void WriteHeader(Stream stream, List<byte> body)
    {
        var bytes = body.ToArray();
        WriteUInt16(stream, 0xEA60);
        WriteUInt16(stream, (ushort)bytes.Length);
        stream.Write(bytes);
        WriteUInt32(stream, ComputeCrc(bytes));
        stream.WriteByte(0); // extended header terminator
        stream.WriteByte(0);
    }

    private static void WriteEndMarker(Stream stream)
    {
        WriteUInt16(stream, 0xEA60);
        WriteUInt16(stream, 0);
    }

    private static void AddNullTerminated(List<byte> body, string value)
    {
        body.AddRange(Encoding.ASCII.GetBytes(value));
        body.Add(0);
    }

    private static byte[] UInt32Bytes(uint value) =>
        [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private static uint ToDosDateTime(DateTime value)
    {
        var date = (uint)(((value.Year - 1980) << 9) | (value.Month << 5) | value.Day);
        var time = (uint)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));
        return (date << 16) | time;
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var entry = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) == 1 ? (entry >> 1) ^ 0xEDB88320u : entry >> 1;
            }

            table[index] = entry;
        }

        return table;
    }
}
