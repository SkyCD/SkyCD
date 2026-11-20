using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SkyCD.Plugin.Gzip.Tests;

/// <summary>
/// Builds small gzip members by hand so tests can control every header field, including unsafe names,
/// and do not depend on binary fixtures or external tools.
/// </summary>
internal static class GzipTestArchiveBuilder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static DateTime ModifiedUtc { get; } = new(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

    public static MemoryStream Create(string content, string? name = null, DateTime? modifiedUtc = null,
        byte method = 8)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream();
        stream.WriteByte(0x1F);
        stream.WriteByte(0x8B);
        stream.WriteByte(method);
        stream.WriteByte(name is null ? (byte)0 : (byte)0x08);

        var header = new byte[6];
        if (modifiedUtc is { } modified)
        {
            var utc = new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc));
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)utc.ToUnixTimeSeconds());
        }

        header[4] = 0;
        header[5] = 3;
        stream.Write(header);

        if (name is not null)
        {
            stream.Write(Encoding.Latin1.GetBytes(name));
            stream.WriteByte(0);
        }

        using (var deflate = new DeflateStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        var trailer = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, Crc32(data));
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(4), (uint)data.Length);
        stream.Write(trailer);
        stream.Position = 0;
        return stream;
    }

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not a gzip archive"));

    public static MemoryStream CreateUnsupportedMethod(string content) => Create(content, method: 7);

    public static MemoryStream CreateTruncated(string content)
    {
        using var complete = Create(content);
        var truncated = new MemoryStream(complete.ToArray()[..10]);
        return truncated;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
