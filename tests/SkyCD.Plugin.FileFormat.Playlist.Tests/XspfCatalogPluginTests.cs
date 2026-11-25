using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Playlist.Xspf;
using SkyCD.Plugin.FileFormat.Playlist.Xspf.Models;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Playlist.Tests;

public sealed class XspfCatalogPluginTests
{
    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsPlaylistEntries()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Xspf", "catalog-playlist.xspf");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await new XspfCatalogPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xspf",
            Source = source
        });

        Assert.True(readResult.Success);
        var document = Assert.IsType<XspfPlaylistDocument>(readResult.Payload);
        Assert.Equal(3, document.Entries.Count);
        Assert.Contains(document.Entries, entry => entry.Path == @"C:\Music\Archive\Track A.mp3");
        Assert.Contains(document.Entries, entry => entry.Path == "../shared/song3.ogg");
        Assert.Contains(document.Entries,
            entry => entry.Location == "https://radio.example.com/live.mp3" &&
                     entry.Title == "Example Radio" &&
                     entry.Creator == "SkyCD FM" &&
                     entry.DurationMilliseconds == 0);

        await using var target = new MemoryStream();
        var writeResult = await new XspfCatalogPlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xspf",
            Target = target,
            Payload = document
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        var expected = string.Join(Environment.NewLine,
        [
            "<playlist version=\"1\" xmlns=\"http://xspf.org/ns/0/\">",
            "  <trackList>",
            "    <track>",
            @"      <location>C:\Music\Archive\Track A.mp3</location>",
            "      <title>Track A</title>",
            "      <creator>SkyCD Artist</creator>",
            "      <duration>187000</duration>",
            "    </track>",
            "    <track>",
            "      <location>../shared/song3.ogg</location>",
            "    </track>",
            "    <track>",
            "      <location>https://radio.example.com/live.mp3</location>",
            "      <title>Example Radio</title>",
            "      <creator>SkyCD FM</creator>",
            "      <duration>0</duration>",
            "    </track>",
            "  </trackList>",
            "</playlist>"
        ]);
        Assert.Equal(expected, written);

        target.Position = 0;
        var roundTrip = await new XspfCatalogPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xspf",
            Source = target
        });

        Assert.True(roundTrip.Success);
        var roundTripDocument = Assert.IsType<XspfPlaylistDocument>(roundTrip.Payload);
        Assert.Equal(3, roundTripDocument.Entries.Count);
        Assert.Equal(document.Entries, roundTripDocument.Entries);
    }
}
