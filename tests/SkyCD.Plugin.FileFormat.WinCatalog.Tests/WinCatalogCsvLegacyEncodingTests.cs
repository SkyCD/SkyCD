using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Models;
using SkyCD.SDK.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Tests;

/// <summary>
/// Covers the character encoding detection the WinCatalog CSV importer resolves through UTF.Unknown.
/// </summary>
public sealed class WinCatalogCsvLegacyEncodingTests
{
    private const string Header = "Disk,Catalog Path,Name,Size,Type,Modified,Comments\n";

    [Theory]
    [InlineData("wincatalog-real-export-2026-utf8.csv", "UTF-8")]
    [InlineData("wincatalog-utf8-bom.csv", "UTF-8 with BOM")]
    [InlineData("wincatalog-ansi-1252.csv", "Windows-1252 (ANSI)")]
    [InlineData("wincatalog-cp1251.csv", "Windows-1251")]
    [InlineData("wincatalog-shift-jis.csv", "Shift-JIS")]
    [InlineData("wincatalog-gb18030.csv", "GB18030")]
    public async Task LegacyCodePageFixtures_AreDetectedAndImported(string name, string expectedEncoding)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", name));
        var plugin = new WinCatalogCsvPlugin();
        Assert.True(plugin.CanHandleContent(stream));

        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);
        Assert.Equal(expectedEncoding, document.Summary.DetectedEncoding);
        Assert.Equal(0, document.Summary.SkippedRecordsCount);
        Assert.DoesNotContain(document.Rows, row => row.Values.Any(value => value?.ToString()?.Contains('\uFFFD') == true));
    }

    [Theory]
    [InlineData("wincatalog-cp1251.csv", "Первый сериал")]
    [InlineData("wincatalog-shift-jis.csv", "第一シリーズ")]
    [InlineData("wincatalog-gb18030.csv", "第一部")]
    public async Task LegacyCodePageFixtures_DecodeNamesWithoutMojibake(string name, string expectedName)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogCsv", name));
        var plugin = new WinCatalogCsvPlugin();
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);
        Assert.Contains(document.Rows, row => Equals(row["name"], expectedName));
    }

    [Theory]
    [InlineData("utf8", false, "UTF-8")]
    [InlineData("utf8", true, "UTF-8 with BOM")]
    [InlineData("utf16le", false, "UTF-16 LE")]
    [InlineData("utf16le", true, "UTF-16 LE")]
    [InlineData("utf16be", false, "UTF-16 BE")]
    [InlineData("utf16be", true, "UTF-16 BE")]
    [InlineData("cp1252", false, "Windows-1252 (ANSI)")]
    [InlineData("cp1251", false, "Windows-1251")]
    [InlineData("shift-jis", false, "Shift-JIS")]
    [InlineData("gb18030", false, "GB18030")]
    public async Task LegacyCodePages_AreDetectedAndImported(string kind, bool byteOrderMark, string expectedEncoding)
    {
        var (name, comment) = SampleFor(kind);
        var encoding = EncodingFor(kind, byteOrderMark);
        var payload = encoding.GetPreamble().Concat(encoding.GetBytes(BuildReport(name, comment))).ToArray();
        using var stream = new MemoryStream(payload);
        var plugin = new WinCatalogCsvPlugin();

        Assert.True(plugin.CanHandleContent(stream));

        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<WinCatalogCsvReportDocument>(result.Payload);
        Assert.Equal(expectedEncoding, document.Summary.DetectedEncoding);
        Assert.Contains(document.Rows, row => Equals(row["name"], name));
        Assert.DoesNotContain(document.Rows, row => row.Values.Any(value => value?.ToString()?.Contains('\uFFFD') == true));
    }

    [Fact]
    public async Task UndetectableEncoding_ProducesActionableImportError()
    {
        using var stream = new MemoryStream([]);
        var plugin = new WinCatalogCsvPlugin();

        Assert.False(plugin.CanHandleContent(stream));

        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "wincatalog-csv", Source = stream });

        Assert.False(result.Success);
        Assert.Contains("UTF-8", result.Error);
        Assert.Contains("not decoded with a lossy fallback", result.Error);
    }

    private static string BuildReport(string name, string comment) =>
        $"{Header}\"Archive\",\"Catalog\\Archive\\{name}\",\"{name}\",1024,File,2026-03-01 10:16:00,\"{comment}\"\n";

    private static (string Name, string Comment) SampleFor(string kind) => kind switch
    {
        "cp1251" => ("Первый сериал", "Тоже видел"),
        "shift-jis" => ("第一シリーズ", "見た"),
        "gb18030" => ("第一部", "看过了"),
        _ => ("café.txt", "déjà vu")
    };

    private static Encoding EncodingFor(string kind, bool byteOrderMark) => kind switch
    {
        "utf8" => new UTF8Encoding(byteOrderMark, true),
        "utf16le" => new UnicodeEncoding(false, byteOrderMark, true),
        "utf16be" => new UnicodeEncoding(true, byteOrderMark, true),
        "cp1252" => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
        "cp1251" => CodePagesEncodingProvider.Instance.GetEncoding(1251)!,
        "shift-jis" => CodePagesEncodingProvider.Instance.GetEncoding(932)!,
        "gb18030" => CodePagesEncodingProvider.Instance.GetEncoding(54936)!,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported encoding kind.")
    };
}
