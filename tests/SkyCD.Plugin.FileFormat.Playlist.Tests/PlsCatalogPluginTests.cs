using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Playlist.Pls;
using SkyCD.Plugin.FileFormat.Playlist.Pls.Models;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Playlist.Tests;

public sealed class PlsCatalogPluginTests
{
    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsPlaylistEntries()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pls", "catalog-playlist.pls");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await new PlsCatalogPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-pls",
            Source = source
        });

        Assert.True(readResult.Success);
        var document = Assert.IsType<PlsPlaylistDocument>(readResult.Payload);
        Assert.Equal(3, document.Entries.Count);
        Assert.Contains(document.Entries, entry => entry.Path == @"Music\Archive\Track A.mp3");

        await using var target = new MemoryStream();
        var writeResult = await new PlsCatalogPlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-pls",
            Target = target,
            Payload = document
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        var expected = string.Join(Environment.NewLine,
        [
            "[playlist]",
            @"File1=Music\Archive\Track A.mp3",
            "Title1=Track A",
            "Length1=12345",
            @"File2=Music\Archive\Track B.mp3",
            "Title2=Track B",
            "Length2=0",
            @"File3=Podcasts\Episode-01.mp3",
            "Title3=Episode 01",
            "Length3=777",
            "NumberOfEntries=3",
            "Version=2",
            ""
        ]);
        Assert.Equal(expected, written);

        target.Position = 0;
        var roundTrip = await new PlsCatalogPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-pls",
            Source = target
        });

        Assert.True(roundTrip.Success);
        var roundTripDocument = Assert.IsType<PlsPlaylistDocument>(roundTrip.Payload);
        Assert.Equal(3, roundTripDocument.Entries.Count);
        Assert.Equal(document.Entries, roundTripDocument.Entries);
    }
}
