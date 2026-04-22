using System.IO.Compression;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Ascd;

namespace SkyCD.LegacyFormats.Tests;

public class LegacyAscdPluginTests
{
    [Fact]
    public async Task ReadAsync_ParsesLegacyFixtures()
    {
        var plugin = new LegacyAscdPlugin();
        var fixtures = new[] { "my-documents.ascd", "ftpz.ascd" };
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        if (!Directory.Exists(fixtureDir) ||
            fixtures.All(f =>
                !File.Exists(Path.Combine(fixtureDir,
                    f)))) return; // Skip if fixtures are not available (e.g., in CI without legacy folder)

        foreach (var fixture in fixtures)
        {
            var fixturePath = Path.Combine(fixtureDir, fixture);
            if (!File.Exists(fixturePath))
                continue; // Skip missing fixtures
            var bytes = await File.ReadAllBytesAsync(fixturePath);
            await using var source = new MemoryStream(bytes);

            var result = await plugin.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "legacy-ascd",
                Source = source,
                FileName = fixture
            });

            Assert.True(result.Success, result.Error);
            var payload = Assert.IsType<LegacyAscdCatalog>(result.Payload);
            Assert.NotEmpty(payload.Entries);
            Assert.All(payload.Entries, entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Id));
                Assert.False(string.IsNullOrWhiteSpace(entry.Name));
                Assert.False(string.IsNullOrWhiteSpace(entry.Type));
            });
        }
    }

    [Fact]
    public async Task ReadThenWriteThenReadAsync_RoundTripsEntries()
    {
        var plugin = new LegacyAscdPlugin();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "my-documents.ascd");

        if (!File.Exists(fixturePath)) return; // Skip if fixture is not available (e.g., in CI without legacy folder)
        var bytes = await File.ReadAllBytesAsync(fixturePath);
        await using var source = new MemoryStream(bytes);

        var firstRead = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-ascd",
            Source = source,
            FileName = "my-documents.ascd"
        });

        Assert.True(firstRead.Success, firstRead.Error);
        var payload = Assert.IsType<LegacyAscdCatalog>(firstRead.Payload);

        await using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "legacy-ascd",
            Target = target,
            Payload = payload,
            FileName = "my-documents.ascd"
        });

        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var secondRead = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-ascd",
            Source = target,
            FileName = "my-documents.ascd"
        });

        Assert.True(secondRead.Success, secondRead.Error);
        var reparsed = Assert.IsType<LegacyAscdCatalog>(secondRead.Payload);
        Assert.Equal(payload.Entries.Count, reparsed.Entries.Count);
        Assert.Equal(payload.Entries[0].Name, reparsed.Entries[0].Name);
        Assert.Equal(payload.Entries[0].SizeBytes, reparsed.Entries[0].SizeBytes);
    }

    [Fact]
    public async Task ReadAsync_RejectsInvalidHeader()
    {
        var plugin = new LegacyAscdPlugin();
        var invalidText =
            "INSERT INTO list (`ID`, `Name`, `ParentID`, `Type`, `Properties`,`Size`, `AID`) VALUES ('0', 'Root', '-1', 'scdFolder', '', '0', '<?Application_ID?>')";
        var compressed = CompressText(invalidText);
        await using var source = new MemoryStream(compressed);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-ascd",
            Source = source
        });

        Assert.False(result.Success);
        Assert.Contains("header", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RejectsUnexpectedSqlPayload()
    {
        var plugin = new LegacyAscdPlugin();
        var payload =
            """
            # format: skycd-nf 1.0
            INSERT INTO list (`ID`, `Name`, `ParentID`, `Type`, `Properties`,`Size`, `AID`) VALUES ('0', 'Root', '-1', 'scdFolder', '', '0', '<?Application_ID?>'); DROP TABLE list;
            """;
        var compressed = CompressText(payload);
        await using var source = new MemoryStream(compressed);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "legacy-ascd",
            Source = source
        });

        Assert.False(result.Success);
        Assert.Contains("single VALUES", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CompressText(string text)
    {
        using var output = new MemoryStream();
        using (var compressed = new DeflateStream(output, CompressionMode.Compress, true))
        using (var writer = new StreamWriter(compressed))
        {
            writer.Write(text);
        }

        return output.ToArray();
    }
}