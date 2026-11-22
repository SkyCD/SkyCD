using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Compressors.ZStandard;

namespace SkyCD.Plugin.Metadata.Archives.Zstd.Tests;

/// <summary>
/// Builds Zstandard frames with the SharpCompress encoder and derives damaged variants. A committed
/// fixture produced by an independent encoder is used where an external stream is desirable.
/// </summary>
internal static class ZstdTestArchiveBuilder
{
    public const string FixtureContent = "SkyCD zstandard fixture content for catalog indexing.\n";

    public static MemoryStream Create(string content)
    {
        var stream = new MemoryStream();
        using (var compressor = new CompressionStream(stream, level: 3, bufferSize: 81_920, leaveOpen: true))
        {
            var data = Encoding.UTF8.GetBytes(content);
            compressor.Write(data);
        }

        stream.Position = 0;
        return stream;
    }

    public static MemoryStream CreateConcatenated(string first, string second)
    {
        var stream = new MemoryStream();
        using (var head = Create(first))
        using (var tail = Create(second))
        {
            head.CopyTo(stream);
            tail.CopyTo(stream);
        }

        stream.Position = 0;
        return stream;
    }

    public static MemoryStream CreateCommittedFixture() => new(ReadFixture());

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not a zstandard archive"));

    public static MemoryStream CreateSkippable()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), 0x184D2A50u);
        return new MemoryStream(bytes);
    }

    public static MemoryStream CreateUnsupportedFlags()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), 0xFD2FB528u);
        bytes[4] = 0b0000_1000;
        return new MemoryStream(bytes);
    }

    public static MemoryStream CreateTruncated() => new(ReadFixture()[..8]);

    public static MemoryStream CreateCorruptedPayload()
    {
        // Keep the valid magic and descriptor so the payload itself is what fails to decode.
        var corrupt = ReadFixture()[..5].Concat(Encoding.ASCII.GetBytes(new string('g', 64))).ToArray();
        return new MemoryStream(corrupt);
    }

    private static byte[] ReadFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Zstd", "catalog.zst"));
}
