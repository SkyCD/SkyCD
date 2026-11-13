using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.LegacyFormats.Tests;

public sealed class LegacyScdBinaryFormatRoutingTests
{
    [Fact]
    public async Task SaveFormats_ExposeTextAndBinaryScdChoices_AndRouteTheSelectedBinaryFormat()
    {
        var manager = new FileFormatManager([new LegacyScdPlugin(), new LegacyScdBinaryPlugin()]);
        var formats = manager.GetSaveFormats();
        Assert.Equal(["legacy-scd-binary", "legacy-scd"], formats.Select(format => format.FormatId).ToArray());

        var catalog = new LegacyScdCatalog();
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk]\File.txt", SizeBytes = 1 });
        await using var target = new MemoryStream();
        var result = await manager.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "legacy-scd-binary",
            FileName = "catalog.scd",
            Target = target,
            Payload = catalog
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal("# SkyCD File", System.Text.Encoding.ASCII.GetString(target.ToArray(), 0, 12));
    }
}
