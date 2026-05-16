using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Html;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class HtmlCatalogExportPluginTests
{
    [Fact]
    public void SaveFormats_IncludeHtml_ButOpenFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.DoesNotContain(openFormats, format => format.FormatId == "skycd-html");
        Assert.Contains(saveFormats, format => format.FormatId == "skycd-html" && format.Extensions.Contains(".html"));
    }

    [Fact]
    public async Task ReadAsync_IsBlocked_ForWriteOnlyHtmlFormat()
    {
        var service = CreateService();
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("<html></html>"));

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-html",
                Source = source
            }));

        Assert.Contains("not readable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_ExportsNavigationStructure_WithEscapedNames()
    {
        var service = CreateService();
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

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new HtmlCatalogExportPlugin()]);
    }
}
