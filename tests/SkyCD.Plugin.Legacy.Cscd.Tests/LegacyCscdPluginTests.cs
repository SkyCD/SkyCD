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
        Assert.Contains(catalog.Entries, entry => entry.Path == @"\Pictures" && entry.SizeBytes == null);
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
        Assert.Contains(catalog.Entries, entry => entry.Path == @"\Software [2023]" && entry.SizeBytes == null);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Backup\Archive [2022-12-31].zip" && entry.SizeBytes == 2147483648);
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