using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;
using SkyCD.Plugin.WinCatalog.Csv.Models;

namespace SkyCD.Plugin.WinCatalog.Csv.Tests;

public sealed class WinCatalogCsvEncodingTests
{
    private const string Report = "Disk,Catalog Path,Name,Comments\r\nDisque,Catalog\\Disque\\été\\café.txt,café.txt,déjà vu\r\n";

    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf8", true)]
    [InlineData("le", false)]
    [InlineData("le", true)]
    [InlineData("be", false)]
    [InlineData("be", true)]
    [InlineData("1252", false)]
    public async Task RecognitionAndImport_PreserveUnicodeAndStreamPosition(string kind, bool bom)
    {
        var encoding = kind switch
        {
            "le" => (Encoding)new UnicodeEncoding(false, bom, true),
            "be" => new UnicodeEncoding(true, bom, true),
            "1252" => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
            _ => new UTF8Encoding(bom, true)
        };
        var data = encoding.GetPreamble().Concat(encoding.GetBytes(Report)).ToArray();
        using var stream = new MemoryStream(new byte[] { 42 }.Concat(data).ToArray());
        stream.Position = 1;
        var plugin = new WinCatalogCsvPlugin();
        Assert.True(plugin.CanHandleContent(stream));
        Assert.Equal(1, stream.Position);
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);
        Assert.Contains(document.Rows, row => Equals(row["name"], "café.txt"));
        Assert.Contains(document.Rows, row => Equals(row["name"], "été"));
        Assert.DoesNotContain(document.Rows, row => row.Values.Any(value => value?.ToString()?.Contains('\uFFFD') == true));
    }

    [Theory]
    [InlineData("wincatalog-ansi-1252.csv")]
    [InlineData("wincatalog-reordered-columns.csv")]
    [InlineData("wincatalog-quoted-fields.csv")]
    [InlineData("wincatalog-real-export-2026-utf8.csv")]
    public async Task Fixtures_AreRecognizedAndImported(string name)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", name));
        var plugin = new WinCatalogCsvPlugin();
        Assert.True(plugin.CanHandleContent(stream));
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);
        Assert.Equal(0, document.Summary.SkippedRecordsCount);
        if (name == "wincatalog-ansi-1252.csv" || name == "wincatalog-real-export-2026-utf8.csv")
        {
            var folder = Assert.Single(document.Rows, row => Equals(row["name"], "Café Notes"));
            Assert.Equal("folder", folder["kind"]);
            var file = Assert.Single(document.Rows, row => Equals(row["name"], "crème brûlée.txt"));
            Assert.Equal(folder["nodeId"], file["parentId"]);
            var properties = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(file["properties"]);
            Assert.Contains("Café Notes", properties["WinCatalogPath"]?.ToString());
            Assert.Equal(name == "wincatalog-ansi-1252.csv" ? "Commentaire déjà vu — naïve" : "dessert\\n", properties["Comments"]);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xE9 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41 })]
    [InlineData(new byte[] { 0x81, 0xFF })]
    public async Task InvalidEncoding_IsRejectedConsistently(byte[] suffix)
    {
        var data = suffix.Concat(Encoding.ASCII.GetBytes(Report)).ToArray();
        using var stream = new MemoryStream(data);
        var plugin = new WinCatalogCsvPlugin();
        Assert.False(plugin.CanHandleContent(stream));
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });
        Assert.False(result.Success);
    }
}
