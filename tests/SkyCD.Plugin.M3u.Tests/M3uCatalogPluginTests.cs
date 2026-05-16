using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.M3u;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class M3uCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesM3uPluginMetadata()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-m3u" &&
                                               format.Extensions.Contains(".m3u") &&
                                               format.Extensions.Contains(".m3u8"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-m3u" &&
                                               format.Extensions.Contains(".m3u") &&
                                               format.Extensions.Contains(".m3u8"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsPlaylistEntries()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M3u", "catalog-playlist.m3u8");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-m3u",
            Source = source
        });

        Assert.True(readResult.Success);
        var document = Assert.IsType<M3uPlaylistDocument>(readResult.Payload);
        Assert.Equal(4, document.Entries.Count);
        Assert.Contains(document.Entries, entry => entry.Path == "../shared/song3.ogg" && entry.Title is null);
        Assert.Contains(document.Entries, entry => entry.Path == "https://radio.example.com/live.mp3" &&
                                                   entry.Title == "Example Radio" &&
                                                   entry.DurationSeconds == -1);

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-m3u",
            Target = target,
            Payload = document
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("#EXTM3U", written);
        Assert.Contains("#EXTINF:213,Daft Punk - Around the World", written);

        target.Position = 0;
        var roundTrip = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-m3u",
            Source = target
        });

        Assert.True(roundTrip.Success);
        var roundTripDocument = Assert.IsType<M3uPlaylistDocument>(roundTrip.Payload);
        Assert.Equal(4, roundTripDocument.Entries.Count);
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new M3uCatalogPlugin()]);
    }
}
