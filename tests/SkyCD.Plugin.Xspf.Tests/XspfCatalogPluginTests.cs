using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Xspf;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class XspfCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesXspfPluginMetadata()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-xspf" && format.Extensions.Contains(".xspf"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-xspf" && format.Extensions.Contains(".xspf"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsPlaylistEntries()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Xspf", "catalog-playlist.xspf");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xspf",
            Source = source
        });

        Assert.True(readResult.Success);
        var document = Assert.IsType<XspfPlaylistDocument>(readResult.Payload);
        Assert.Equal(3, document.Entries.Count);
        Assert.Contains(document.Entries,
            entry => entry.Path == @"C:\Music\Archive\Track A.mp3");
        Assert.Contains(document.Entries, entry => entry.Path == "../shared/song3.ogg");
        Assert.Contains(document.Entries,
            entry => entry.Location == "https://radio.example.com/live.mp3" &&
                     entry.Title == "Example Radio" &&
                     entry.Creator == "SkyCD FM" &&
                     entry.DurationMilliseconds == 0);

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xspf",
            Target = target,
            Payload = document
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("<playlist", written);
        Assert.Contains("http://xspf.org/ns/0/", written);
        Assert.Contains("<trackList>", written);

        target.Position = 0;
        var roundTrip = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xspf",
            Source = target
        });

        Assert.True(roundTrip.Success);
        var roundTripDocument = Assert.IsType<XspfPlaylistDocument>(roundTrip.Payload);
        Assert.Equal(3, roundTripDocument.Entries.Count);
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new XspfCatalogPlugin()]);
    }
}
