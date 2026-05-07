using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Cscd;
using Xunit;

namespace SkyCD.LegacyFormats.Tests;

public class LegacyCscdPluginTests
{
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
