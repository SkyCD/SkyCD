using System;
using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;
using Xunit;

namespace SkyCD.LegacyFormats.Tests;

public class LegacyScdPluginTests
{
    [Fact]
    public async Task ReadAsync_ParsesOwnedScdFixture()
    {
        var plugin = new LegacyScdPlugin();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scd", "catalog-sample.scd");

        await using var source = File.OpenRead(fixturePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-scd",
            Source = source,
            FileName = "catalog-sample.scd"
        });

        Assert.True(result.Success, result.Error);
        var catalog = Assert.IsType<LegacyScdCatalog>(result.Payload);
        Assert.Equal(5, catalog.Entries.Count);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Games\Doom.exe");
        Assert.Contains(catalog.Entries, entry => entry.Path == @"\Pictures" && entry.SizeBytes == null);
    }

    [Fact]
    public async Task ReadAsync_ParsesPathWithSquareBrackets()
    {
        var plugin = new LegacyScdPlugin();

        // Create test content with a path that contains square brackets
        var testContent = @"[1MB] [Disk]\Games\Doom.exe
[512KB] [Disk]\Music\Song [Remix].mp3
[Disk]\Software [2023]
[2GB] [Disk]\Backup\Archive [2022-12-31].zip
";

        // Read the test content
        await using var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(testContent));
        var readResult = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-scd",
            Source = memoryStream,
            FileName = "test.scd"
        });

        Assert.True(readResult.Success, readResult.Error);
        var catalog = Assert.IsType<LegacyScdCatalog>(readResult.Payload);
        Assert.Equal(4, catalog.Entries.Count);

        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Games\Doom.exe" && entry.SizeBytes == 1048576);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Music\Song [Remix].mp3" && entry.SizeBytes == 524288);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"\Software [2023]" && entry.SizeBytes == null);
        Assert.Contains(catalog.Entries, entry => entry.Path == @"[Disk]\Backup\Archive [2022-12-31].zip" && entry.SizeBytes == 2147483648);
    }

    [Fact]
    public async Task ReadAsync_ParsesLegacyScdSample()
    {
        var plugin = new LegacyScdPlugin();
        var samplePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "gamez.scd");

        if (!File.Exists(samplePath))
        {
            return; // Skip if fixture is not available (e.g., in CI without legacy folder)
        }

        var bytes = await File.ReadAllBytesAsync(samplePath);
        await using var stream = new MemoryStream(bytes);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-scd",
            Source = stream,
            FileName = "gamez.scd"
        });

        Assert.True(result.Success);
        var payload = Assert.IsType<LegacyScdCatalog>(result.Payload);
        Assert.NotEmpty(payload.Entries);
    }

    [Fact]
    public async Task WriteAsync_RoundTripsCatalog()
    {
        var plugin = new LegacyScdPlugin();
        var catalog = new LegacyScdCatalog();
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk]\Folder\File.txt", SizeBytes = 1200 });
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk]\Readme.md" });

        await using var writeStream = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "legacy-scd",
            Target = writeStream,
            Payload = catalog
        });

        Assert.True(write.Success);
        writeStream.Position = 0;

        var read = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-scd",
            Source = writeStream
        });

        Assert.True(read.Success);
        var parsed = Assert.IsType<LegacyScdCatalog>(read.Payload);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(@"[Disk]\Folder\File.txt", parsed.Entries[0].Path);
    }

    [Fact]
    public async Task ReadThenWriteThenReadAsync_RoundTripsFixtureEntries()
    {
        var plugin = new LegacyScdPlugin();
        var samplePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "gamez.scd");

        if (!File.Exists(samplePath))
        {
            return; // Skip if fixture is not available (e.g., in CI without legacy folder)
        }

        var sourceBytes = await File.ReadAllBytesAsync(samplePath);
        await using var source = new MemoryStream(sourceBytes);

        var firstRead = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-scd",
            Source = source,
            FileName = "gamez.scd"
        });

        Assert.True(firstRead.Success);
        var parsed = Assert.IsType<LegacyScdCatalog>(firstRead.Payload);
        Assert.NotEmpty(parsed.Entries);

        await using var serialized = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "legacy-scd",
            Target = serialized,
            Payload = parsed,
            FileName = "gamez.scd"
        });

        Assert.True(write.Success);

        serialized.Position = 0;
        var secondRead = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-scd",
            Source = serialized,
            FileName = "gamez.scd"
        });

        Assert.True(secondRead.Success);
        var reparsed = Assert.IsType<LegacyScdCatalog>(secondRead.Payload);
        Assert.Equal(parsed.Entries.Count, reparsed.Entries.Count);
        Assert.Equal(parsed.Entries[0].Path, reparsed.Entries[0].Path);
    }
}