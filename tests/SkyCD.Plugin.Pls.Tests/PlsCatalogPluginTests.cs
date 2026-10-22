using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Pls;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class PlsCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesPlsPluginMetadata()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-pls" && format.Extensions.Contains(".pls"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-pls" && format.Extensions.Contains(".pls"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsPlaylistEntries()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pls", "catalog-playlist.pls");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-pls",
            Source = source
        });

        Assert.True(readResult.Success);
        var document = Assert.IsType<PlsPlaylistDocument>(readResult.Payload);
        Assert.Equal(3, document.Entries.Count);
        Assert.Contains(document.Entries, entry => entry.Path == @"Music\Archive\Track A.mp3");

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-pls",
            Target = target,
            Payload = document
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("[playlist]", written);
        Assert.Contains("NumberOfEntries=3", written);
        Assert.Contains("Version=2", written);

        target.Position = 0;
        var roundTrip = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-pls",
            Source = target
        });

        Assert.True(roundTrip.Success);
        var roundTripDocument = Assert.IsType<PlsPlaylistDocument>(roundTrip.Payload);
        Assert.Equal(3, roundTripDocument.Entries.Count);
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new PlsCatalogPlugin()]);
    }
}
