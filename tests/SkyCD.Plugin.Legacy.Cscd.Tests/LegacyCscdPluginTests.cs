using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Cscd;
using Xunit;

namespace SkyCD.LegacyFormats.Tests;

public class LegacyCscdPluginTests
{
    [Fact]
    public async Task ReadAsync_ParsesOwnedCscdFixture()
    {
        var plugin = new LegacyCscdPlugin();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cscd", "catalog-sample.cscd");

        await using var source = File.OpenRead(fixturePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-cscd",
            Source = source,
            FileName = "catalog-sample.cscd"
        });

        Assert.True(result.Success, result.Error);
        var catalog = Assert.IsType<LegacyCscdCatalog>(result.Payload);
        Assert.Equal(5, catalog.Entries.Count);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Games\Doom.exe");
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Pictures" && entry.SizeBytes == null);
    }

    [Fact]
    public async Task ReadAsync_ParsesPathWithSquareBrackets()
    {
        var plugin = new LegacyCscdPlugin();

        // Create test content with a path that contains square brackets
        var testContent = @"[1MB] [Disk]\Games\Doom.exe
[512KB] [Disk]\Music\Song [Remix].mp3
[Disk]\Software [2023]
[2GB] [Disk]\Backup\Archive [2022-12-31].zip
";

        // Compress the content
        byte[] compressedBytes;
        using (var memoryStream = new MemoryStream())
        {
            using (var deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress))
            using (var writer = new StreamWriter(deflateStream, System.Text.Encoding.UTF8))
            {
                await writer.WriteAsync(testContent);
            }
            compressedBytes = memoryStream.ToArray();
        }

        // Read the compressed content with the plugin
        await using var compressedStream = new MemoryStream(compressedBytes);
        var readResult = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-cscd",
            Source = compressedStream,
            FileName = "test.cscd"
        });

        Assert.True(readResult.Success, readResult.Error);
        var catalog = Assert.IsType<LegacyCscdCatalog>(readResult.Payload);
        Assert.Equal(4, catalog.Entries.Count);

        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Games\Doom.exe" && entry.SizeBytes == 1048576);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Music\Song [Remix].mp3" && entry.SizeBytes == 524288);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Software [2023]" && entry.SizeBytes == null);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Backup\Archive [2022-12-31].zip" && entry.SizeBytes == 2147483648);
    }

    [Theory]
    [InlineData("1,50181961 GB   ", 1612566527L)]
    [InlineData("455,15625  KB   ", 466080L)]
    [InlineData("1.5 MB", 1572864L)]
    [InlineData("8 Bits", 1L)]
    [InlineData("0 Bits", 0L)]
    [InlineData("1024 Bytes", 1024L)]
    [InlineData("1 TB", 1099511627776L)]
    public async Task ReadAsync_ParsesLegacyLeafSize(string token, long expected)
    {
        await using var source = await CompressAsync($"[Gamez]\\Folder [2023]\\[{token}] File [Remix].iso");
        var result = await new LegacyCscdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-cscd", Source = source });
        Assert.True(result.Success, result.Error);
        var entry = Assert.Single(Assert.IsType<LegacyCscdCatalog>(result.Payload).Entries);
        Assert.Equal(@"[Gamez]\Folder [2023]\File [Remix].iso", entry.Path);
        Assert.Equal(expected, entry.SizeBytes);
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData("[Disk]")]
    [InlineData(@"[Disk]\Folder [2023]")]
    [InlineData(@"[Disk]\Folder\[unknown] File.txt")]
    [InlineData(@"[Disk]\[999999999999999999999999999999 TB] File.txt")]
    public async Task ReadAsync_PreservesPathsWithoutValidSizes(string path)
    {
        await using var source = await CompressAsync("# comment\n\n" + path);
        var result = await new LegacyCscdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-cscd", Source = source });
        Assert.True(result.Success, result.Error);
        var entry = Assert.Single(Assert.IsType<LegacyCscdCatalog>(result.Payload).Entries);
        Assert.Equal(path, entry.Path);
        Assert.Null(entry.SizeBytes);
    }

    [Theory]
    [InlineData(0L, "0          Bits ")]
    [InlineData(1024L, "1024       Bytes")]
    [InlineData(1048576L, "1024       KB   ")]
    [InlineData(1073741824L, "1024       MB   ")]
    [InlineData(1099511627776L, "1024       GB   ")]
    [InlineData(2199023255552L, "2          TB   ")]
    [InlineData(1200L, "1.171875   KB   ")]
    public async Task WriteAsync_UsesLegacyLayoutAndPadding(long bytes, string token)
    {
        var catalog = new LegacyCscdCatalog();
        catalog.Entries.Add(new LegacyCscdEntry { Path = @"[Disk]\Folder\File.txt", SizeBytes = bytes });
        await using var target = new MemoryStream();
        var result = await new LegacyCscdPlugin().WriteAsync(new FileFormatWriteRequest { FormatId = "legacy-cscd", Target = target, Payload = catalog });
        Assert.True(result.Success, result.Error);
        Assert.True(target.CanWrite);
        target.Position = 0;
        using var deflate = new DeflateStream(target, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(deflate);
        Assert.Equal($"[Disk]\\Folder\\[{token}] File.txt" + Environment.NewLine, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadWriteReadAsync_PreservesHistoricalCatalog()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cscd", "gamez.scd"));
        await using var source = await CompressAsync(text);
        var plugin = new LegacyCscdPlugin();
        var first = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "legacy-cscd", Source = source });
        Assert.True(first.Success, first.Error);
        var catalog = Assert.IsType<LegacyCscdCatalog>(first.Payload);
        Assert.NotEmpty(catalog.Entries);
        Assert.Equal(466080L, catalog.Entries[0].SizeBytes);
        Assert.Equal(@"[Gamez]\Jill of the Jungle episodes 1-3 [Epic Megagames][1992].rar", catalog.Entries[0].Path);
        await using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest { FormatId = "legacy-cscd", Target = target, Payload = catalog });
        Assert.True(write.Success, write.Error);
        target.Position = 0;
        var second = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "legacy-cscd", Source = target });
        Assert.True(second.Success, second.Error);
        var reparsed = Assert.IsType<LegacyCscdCatalog>(second.Payload);
        Assert.Equal(catalog.Entries.Count, reparsed.Entries.Count);
        for (var i = 0; i < catalog.Entries.Count; i++)
        {
            Assert.Equal(catalog.Entries[i].Path, reparsed.Entries[i].Path);
            Assert.NotNull(catalog.Entries[i].SizeBytes);
            Assert.NotNull(reparsed.Entries[i].SizeBytes);
            // The original writer truncates its numeric field to ten characters.
            Assert.InRange(Math.Abs(catalog.Entries[i].SizeBytes!.Value - reparsed.Entries[i].SizeBytes!.Value), 0, 1024);
        }
    }

    private static async Task<MemoryStream> CompressAsync(string text)
    {
        var stream = new MemoryStream();
        using (var compressed = new DeflateStream(stream, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new StreamWriter(compressed))
        {
            await writer.WriteAsync(text);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task WriteAndReadAsync_RoundTripsCompressedCatalog()
    {
        var plugin = new LegacyCscdPlugin();
        var catalog = new LegacyCscdCatalog();
        catalog.Entries.Add(new LegacyCscdEntry { Path = @"[Disk]\Games\Doom.exe", SizeBytes = 1_048_576 });
        catalog.Entries.Add(new LegacyCscdEntry { Path = @"[Disk]\Games\Readme.txt" });

        await using var compressedStream = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "legacy-cscd",
            Target = compressedStream,
            Payload = catalog
        });

        Assert.True(write.Success);

        compressedStream.Position = 0;
        var read = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-cscd",
            Source = compressedStream
        });

        Assert.True(read.Success);
        var parsed = Assert.IsType<LegacyCscdCatalog>(read.Payload);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(@"[Disk]\Games\Doom.exe", parsed.Entries[0].Path);
    }
}
