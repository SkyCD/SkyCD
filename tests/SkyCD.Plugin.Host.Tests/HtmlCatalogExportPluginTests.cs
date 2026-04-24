using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.Managers;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Html;

namespace SkyCD.Plugin.Host.Tests;

public class HtmlCatalogExportPluginTests
{
    [Fact]
    public void SaveFormats_IncludeHtml_ButOpenFormatsDoNot()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.DoesNotContain(openFormats, format => format.FormatId == "skycd-html");
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-html" && format.Extensions.Contains(".html"));
    }

    [Fact]
    public async Task ReadAsync_IsBlocked_ForWriteOnlyHtmlFormat()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("<html></html>"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-html",
            Source = source
        }));

        Assert.Contains("not readable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_ExportsNavigationStructure_WithEscapedNames()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        var payload = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "1",
                ["parentId"] = "",
                ["kind"] = "folder",
                ["name"] = "Root <Catalog>",
                ["sizeBytes"] = "0"
            },
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "2",
                ["parentId"] = "1",
                ["kind"] = "file",
                ["name"] = "track & one.mp3",
                ["sizeBytes"] = "42"
            }
        };

        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-html",
            Target = target,
            Payload = payload
        });

        Assert.True(result.Success);
        var html = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("<nav", html);
        Assert.Contains("href=\"#node-1\"", html);
        Assert.Contains("Root &lt;Catalog&gt;", html);
        Assert.Contains("track &amp; one.mp3", html);
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new HtmlCatalogExportPlugin();
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.html",
                Name = "HtmlCatalogExportPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
