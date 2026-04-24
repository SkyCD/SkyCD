using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.Managers;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Yaml;

namespace SkyCD.Plugin.Host.Tests;

public class YamlCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesYamlExtensions()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format =>
            format.FormatId == "skycd-yaml" &&
            format.Extensions.Contains(".yaml") &&
            format.Extensions.Contains(".yml"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-yaml");
    }

    [Fact]
    public async Task ReadAndWriteAsync_RoundTripsFixture()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Yaml", "catalog-v1.yaml");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-yaml",
            Source = source
        });

        Assert.True(readResult.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(readResult.Payload);
        Assert.Equal("track.mp3", rows[1]["name"]);

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-yaml",
            Target = target,
            Payload = rows
        });

        Assert.True(writeResult.Success);
        var text = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("schemaVersion: skycd.catalog.v1", text);
        Assert.Contains("payload:", text);
    }

    [Fact]
    public async Task ReadAsync_ReturnsTypedError_ForUnsupportedConstructs()
    {
        const string unsupported = """
                                   defaults: &defaults
                                     name: Root
                                   schemaVersion: skycd.catalog.v1
                                   payload:
                                     - <<: *defaults
                                       nodeId: "1"
                                       parentId: ""
                                       kind: folder
                                       sizeBytes: "0"
                                   """;
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(unsupported));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-yaml",
            Source = source
        }));

        Assert.Contains("YAML_UNSUPPORTED_CONSTRUCT", exception.Message);
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new YamlCatalogPlugin();
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.yaml",
                Name = "YamlCatalogPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
