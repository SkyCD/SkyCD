using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Sample.Markdown;

namespace SkyCD.Plugin.Host.Tests;

public class MarkdownCatalogExportPluginTests
{
    [Fact]
    public void SaveFormats_IncludeMarkdown_ButOpenFormatsDoNot()
    {
        var service = new FileFormatRoutingService(CreateCatalog());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.DoesNotContain(openFormats, format => format.FormatId == "skycd-md");
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-md" && format.Extensions.Contains(".md"));
    }

    [Fact]
    public async Task ReadAsync_IsBlocked_ForWriteOnlyMarkdownFormat()
    {
        var service = new FileFormatRoutingService(CreateCatalog());
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("# export"));

        var exception = await Assert.ThrowsAsync<FileFormatRoutingException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-md",
            Source = source
        }));

        Assert.Contains("not readable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_ExportsDeterministicHierarchy_WithEscaping()
    {
        var service = new FileFormatRoutingService(CreateCatalog());
        var payload = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "1",
                ["parentId"] = "",
                ["kind"] = "folder",
                ["name"] = "Root_[Catalog]",
                ["sizeBytes"] = "0"
            },
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "2",
                ["parentId"] = "1",
                ["kind"] = "file",
                ["name"] = "track*(demo).mp3",
                ["sizeBytes"] = "42"
            }
        };

        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-md",
            Target = target,
            Payload = payload
        });

        Assert.True(result.Success);
        var markdown = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("# SkyCD Catalog Export", markdown);
        Assert.Contains("- `folder` Root\\_\\[Catalog\\] (`nodeId=1`)", markdown);
        Assert.Contains("- `file` track\\*\\(demo\\).mp3 (`nodeId=2`)", markdown);
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new MarkdownCatalogExportPlugin();
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
