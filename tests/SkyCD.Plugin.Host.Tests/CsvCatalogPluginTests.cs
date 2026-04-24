using System.Text;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.Managers;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Csv;

namespace SkyCD.Plugin.Host.Tests;

public class CsvCatalogPluginTests
{
    [Fact]
    public void GetOpenAndSaveFormats_ExposesCsvPluginMetadata()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-csv" && format.Extensions.Contains(".csv"));
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-csv" && format.Extensions.Contains(".csv"));
    }

    [Fact]
    public async Task ReadAndWriteAsync_PreservesHierarchyAndSizeFields()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Csv", "catalog-hierarchy.csv");

        await using var source = File.OpenRead(fixturePath);
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-csv",
            Source = source
        });

        Assert.True(readResult.Success);

        var rows = Assert.IsType<List<Dictionary<string, object?>>>(readResult.Payload);
        Assert.Equal(3, rows.Count);
        Assert.Equal("folder", rows[0]["Kind"]);
        Assert.Equal("2", rows[2]["ParentId"]);
        Assert.Equal("12345", rows[2]["SizeBytes"]);

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv",
            Target = target,
            Payload = rows
        });

        Assert.True(writeResult.Success);
        var written = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("NodeId,ParentId,Kind,Name,SizeBytes", written);
        Assert.Contains("\"Music, Archive\"", written);
        Assert.Contains("\"Track \"\"A\"\".mp3\"", written);

        target.Position = 0;
        var roundTripRead = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-csv",
            Source = target
        });

        Assert.True(roundTripRead.Success);
        var roundTripRows = Assert.IsType<List<Dictionary<string, object?>>>(roundTripRead.Payload);
        Assert.Equal("Track \"A\".mp3", roundTripRows[2]["Name"]);
    }

    [Fact]
    public async Task WriteAsync_AcceptsJsonArrayPayload()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """
            [
              { "nodeId":"10", "parentId":"", "kind":"folder", "name":"Docs", "sizeBytes":"0" },
              { "nodeId":"11", "parentId":"10", "kind":"file", "name":"readme.txt", "sizeBytes":"15" }
            ]
            """);

        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv",
            Target = target,
            Payload = payload
        });

        Assert.True(result.Success);
        var text = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("10,,folder,Docs,0", text);
        Assert.Contains("11,10,file,readme.txt,15", text);
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new CsvCatalogPlugin();
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.csv",
                Name = "CsvCatalogPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
