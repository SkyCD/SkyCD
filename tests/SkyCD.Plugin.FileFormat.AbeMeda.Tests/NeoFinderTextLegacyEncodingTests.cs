using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.AbeMeda.Text;
using SkyCD.Plugin.FileFormat.AbeMeda.Text.Models;
using SkyCD.SDK.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

/// <summary>
/// Covers the character encoding detection the NeoFinder text importer resolves through UTF.Unknown.
/// </summary>
public sealed class NeoFinderTextLegacyEncodingTests
{
    private const string Header = "Name\tKind\tSize\tWhere\tVolume\tComments\n";

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
        var plugin = new NeoFinderTextPlugin();

        Assert.True(plugin.CanHandleContent(stream));

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "neofinder-text",
            Source = stream,
            FileName = "export.txt",
            SourcePath = "export.txt"
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        Assert.Equal(expectedEncoding, document.Summary.DetectedEncoding);
        Assert.Contains(document.Rows, row => Equals(row["name"], name));
        Assert.DoesNotContain(document.Rows, row => row.Values.Any(value => value?.ToString()?.Contains('\uFFFD') == true));
    }

    [Fact]
    public async Task UndetectableEncoding_ProducesActionableImportError()
    {
        using var stream = new MemoryStream([]);
        var plugin = new NeoFinderTextPlugin();

        Assert.False(plugin.CanHandleContent(stream));

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "neofinder-text",
            Source = stream,
            FileName = "export.txt",
            SourcePath = "export.txt"
        });

        Assert.False(result.Success);
        Assert.Contains("UTF-8", result.Error);
        Assert.Contains("not decoded with a lossy fallback", result.Error);
    }

    private static string BuildReport(string name, string comment) =>
        $"{Header}{name}\tPlain Text\t1024\t/Archive/{name}\tArchive\t{comment}\n";

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
