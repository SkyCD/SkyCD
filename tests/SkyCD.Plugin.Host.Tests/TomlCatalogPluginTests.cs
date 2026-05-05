using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Toml;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class TomlCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesTomlMetadata()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-toml" && format.Extensions.Contains(".toml"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-toml" && format.Extensions.Contains(".toml"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsFixtureHierarchyAndMetadata()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
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

    [Fact]
    public async Task ReadAsync_WithMissingKeys_ShouldNotThrow()
    {
        var plugin = new TomlCatalogPlugin();
        var toml = @"
[schema]
version = ""skycd.catalog.v1""
hierarchy = ""adjacency-list""

[[nodes]]
name = ""Missing IDs""
";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(toml));
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-toml",
            Source = source
        });

        Assert.True(result.Success, $"Should be successful but got error: {result.Error}");
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Single(rows);
        Assert.False(rows[0].ContainsKey("nodeId") && rows[0]["nodeId"] != null, "nodeId should be null or missing");
    }

    [Fact]
    public async Task ReadAsync_WithIntegerSizeBytes_ShouldWork()
    {
        var plugin = new TomlCatalogPlugin();
        var toml = @"
[schema]
version = ""skycd.catalog.v1""
hierarchy = ""adjacency-list""

[[nodes]]
nodeId = ""1""
name = ""Test""
sizeBytes = 123
";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(toml));
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-toml",
            Source = source
        });

        Assert.True(result.Success, $"Should be successful but got error: {result.Error}");
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal("123", rows[0]["sizeBytes"]);
    }

    private static PluginManager CreateCatalog()
    {
        var plugin = new TomlCatalogPlugin();
        var catalog = PluginManagerTestFactory.Create();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.toml",
                Name = "TomlCatalogPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
