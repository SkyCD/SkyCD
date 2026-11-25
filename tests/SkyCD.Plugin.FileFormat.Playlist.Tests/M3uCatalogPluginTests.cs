using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Playlist.M3u;
using SkyCD.Plugin.FileFormat.Playlist.M3u.Models;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Playlist.Tests;

public sealed class M3uCatalogPluginTests
{
    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsPlaylistEntries()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M3u", "catalog-playlist.m3u8");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await new M3uCatalogPlugin().ReadAsync(new FileFormatReadRequest
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
        var writeResult = await new M3uCatalogPlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-m3u",
            Target = target,
            Payload = document
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        var expected = string.Join(Environment.NewLine,
        [
            "#EXTM3U",
            "",
            "#EXTINF:213,Daft Punk - Around the World",
            "music/around_the_world.mp3",
            "",
            "#EXTINF:187,The Prodigy - Firestarter",
            "music/firestarter.flac",
            "",
            "#EXTINF:-1,Example Radio",
            "https://radio.example.com/live.mp3",
            "",
            "../shared/song3.ogg",
            "",
            ""
        ]);
        Assert.Equal(expected, written);

        target.Position = 0;
        var roundTrip = await new M3uCatalogPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-m3u",
            Source = target
        });

        Assert.True(roundTrip.Success);
        var roundTripDocument = Assert.IsType<M3uPlaylistDocument>(roundTrip.Payload);
        Assert.Equal(4, roundTripDocument.Entries.Count);
        Assert.Equal(document.Entries, roundTripDocument.Entries);
    }
}
