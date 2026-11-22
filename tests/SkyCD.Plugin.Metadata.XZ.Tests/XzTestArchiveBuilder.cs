using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.Metadata.XZ.Tests;

/// <summary>
/// Provides xz streams for tests. Compressing xz requires a real encoder, so valid streams come from a
/// committed fixture; damaged, truncated, and unsupported variants are derived from it or built by hand.
/// </summary>
internal static class XzTestArchiveBuilder
{
    public const string FixtureContent = "SkyCD xz fixture content for catalog indexing.\n";

    public static MemoryStream CreateValid() => new(ReadFixture());

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not an xz archive"));

    public static MemoryStream CreateTruncated() => new(ReadFixture()[..20]);

    public static MemoryStream CreateCorruptedPayload()
    {
        var fixture = ReadFixture();
        var corrupt = fixture[..12].Concat(Encoding.ASCII.GetBytes(new string('g', 64))).ToArray();
        return new MemoryStream(corrupt);
    }

    public static MemoryStream CreateUnsupportedFlags()
    {
        var stream = new MemoryStream();
        stream.Write([0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00]);
        byte[] flags = [0x00, 0x0B];
        stream.Write(flags);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32(flags));
        stream.Write(crc);
        stream.Position = 0;
        return stream;
    }

    private static byte[] ReadFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Xz", "catalog.xz"));

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

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
