using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;

namespace SkyCD.LegacyFormats.Tests;

public class LegacyScdPluginTests
{
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
