using System;
using System.Linq;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Playlist.M3u;
using SkyCD.Plugin.FileFormat.Playlist.Pls;
using SkyCD.Plugin.FileFormat.Playlist.Xspf;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Playlist.Tests;

public sealed class PlaylistPluginTests
{
    [Fact]
    public void Assembly_DiscoversOnePluginWithThreeFormatCapabilities()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(M3uCatalogPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.FileFormat.Playlist", plugin.Id);
        var formats = plugin.Capabilities
            .OfType<IFileFormatPluginCapability>()
            .Select(capability => capability.SupportedFormat.FormatId)
            .OrderBy(formatId => formatId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["skycd-m3u", "skycd-pls", "skycd-xspf"], formats);
    }

    [Fact]
    public void GetOpenAndSaveFormats_ExposesAllPlaylistFormats()
    {
        var service = new FileFormatManager(
        [
            new M3uCatalogPlugin(),
            new PlsCatalogPlugin(),
            new XspfCatalogPlugin()
        ]);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-m3u" &&
                                               format.Extensions.Contains(".m3u") &&
                                               format.Extensions.Contains(".m3u8"));
        Assert.Contains(openFormats, format => format.FormatId == "skycd-pls" && format.Extensions.Contains(".pls"));
        Assert.Contains(openFormats, format => format.FormatId == "skycd-xspf" && format.Extensions.Contains(".xspf"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-m3u" &&
                                               format.Extensions.Contains(".m3u") &&
                                               format.Extensions.Contains(".m3u8"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-pls" && format.Extensions.Contains(".pls"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-xspf" && format.Extensions.Contains(".xspf"));
    }
}
