using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Toml;

namespace SkyCD.Plugin.Host.Tests;

public class TomlCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesTomlMetadata()
    {
        var service = new FileFormatRoutingService(CreateCatalog());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-toml" && format.Extensions.Contains(".toml"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-toml" && format.Extensions.Contains(".toml"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsFixtureHierarchyAndMetadata()
    {
        var service = new FileFormatRoutingService(CreateCatalog());
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Toml", "catalog-v1.toml");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-toml",
            Source = source
        });

        Assert.True(readResult.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(readResult.Payload);
        Assert.Equal(2, rows.Count);
        Assert.Equal("1", rows[1]["parentId"]);
        Assert.Equal("16", rows[1]["sizeBytes"]);

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-toml",
            Target = target,
            Payload = rows
        });

        Assert.True(writeResult.Success);
        var text = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("[schema]", text);
        Assert.Contains("version = \"skycd.catalog.v1\"", text);
        Assert.Contains("hierarchy = \"adjacency-list\"", text);
        Assert.Contains("[[nodes]]", text);
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new TomlCatalogPlugin();
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Plugin = plugin,
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
