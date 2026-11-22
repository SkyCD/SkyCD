using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace SkyCD.Plugin.Metadata.Bzip2.Tests;

/// <summary>
/// Builds bzip2 streams with the SharpCompress encoder and derives damaged variants. A committed fixture
/// is used where an independently produced stream is desirable.
/// </summary>
internal static class Bzip2TestArchiveBuilder
{
    public const string FixtureContent = "SkyCD bzip2 fixture content for catalog indexing.\n";

    public static MemoryStream Create(string content)
    {
        var stream = new MemoryStream();
        using (var compressor = BZip2Stream.Create(stream, CompressionMode.Compress,
                   decompressConcatenated: false, leaveOpen: true, tolerateTruncatedStream: false))
        {
            var data = Encoding.UTF8.GetBytes(content);
            compressor.Write(data);
        }

        stream.Position = 0;
        return stream;
    }

    public static MemoryStream CreateCommittedFixture() =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bzip2", "catalog.bz2")));

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not a bzip2 archive"));

    public static MemoryStream CreateInvalidLevel() =>
        new(Encoding.ASCII.GetBytes("BZh0 not a valid level"));

    public static MemoryStream CreateTruncated() =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bzip2", "catalog.bz2"))[..8]);

    public static MemoryStream CreateCorruptedPayload()
    {
        var fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bzip2", "catalog.bz2"));
        var corrupt = fixture[..4].Concat(Encoding.ASCII.GetBytes(new string('g', 64))).ToArray();
        return new MemoryStream(corrupt);
    }
}
