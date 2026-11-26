using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.TestSupport;
using SkyCD.SDK.Capabilities.FileFormats;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerEncodingTests
{
    private const string Report = "Name\tPath\tSize\tKind\tDate Modified\r\ncaf\u00e9.txt\t/Volumes/Disque/\u00e9t\u00e9/caf\u00e9.txt\t100\tPlain text\t2026-01-01 10:00:00\r\n";

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
        var plugin = new DiskCatalogMakerPlugin();
        Assert.True(plugin.CanHandleContent(stream));
        Assert.Equal(1, stream.Position);
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "diskcatalogmaker-text", Source = stream });
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);
        Assert.Contains(document.Rows, row => Equals(row["name"], "caf\u00e9.txt"));
        Assert.Contains(document.Rows, row => Equals(row["name"], "\u00e9t\u00e9"));
        Assert.DoesNotContain(document.Rows, row => row.Values.Any(value => value?.ToString()?.Contains('\uFFFD') == true));
    }

    [Theory]
    [InlineData("diskcatalogmaker-standard-en.txt")]
    [InlineData("diskcatalogmaker-reordered-columns.txt")]
    [InlineData("diskcatalogmaker-localized-de.txt")]
    [InlineData("diskcatalogmaker-hfs-paths.txt")]
    public async Task Fixtures_AreRecognizedAndImported(string name)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", name));
        var plugin = new DiskCatalogMakerPlugin();
        Assert.True(plugin.CanHandleContent(stream));
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "diskcatalogmaker-text", Source = stream });
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);
        Assert.Equal(0, document.Summary.SkippedRecordsCount);
        if (name == "diskcatalogmaker-standard-en.txt")
        {
            var folder = Assert.Single(document.Rows, row => Equals(row["name"], "Photos"));
            Assert.Equal("folder", folder["kind"]);
            var file = Assert.Single(document.Rows, row => Equals(row["name"], "ski-trip.jpg"));
            Assert.Equal(folder["nodeId"], document.Rows.Single(row => Equals(row["name"], "2026"))["parentId"]);
            Assert.Equal(
                document.Rows.Single(row => Equals(row["name"], "2026"))["nodeId"],
                file["parentId"]);
            var properties = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(file["properties"]);
            Assert.Contains("/Volumes/Archive Disk 1/Photos/2026/ski-trip.jpg", properties["DiskCatalogMakerPath"]?.ToString());
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
        var plugin = new DiskCatalogMakerPlugin();
        Assert.False(plugin.CanHandleContent(stream));
        var result = await plugin.ReadAsync(new FileFormatReadRequest { FormatId = "diskcatalogmaker-text", Source = stream });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task EmptyInput_ReportsActionableEncodingError()
    {
        var plugin = new DiskCatalogMakerPlugin();
        using var stream = new MemoryStream();

        var result = await plugin.ReadAsync(
            new FileFormatReadRequest { FormatId = "diskcatalogmaker-text", Source = stream });

        Assert.False(result.Success);
        Assert.Contains("UTF-8", result.Error);
        Assert.Contains("not imported", result.Error);
    }

    [Theory]
    [MemberData(nameof(LegacyEncodingSamples.Cases), MemberType = typeof(LegacyEncodingSamples))]
    public async Task Import_DetectsAndPreservesLegacyEncodings(
        string encodingName,
        bool includeByteOrderMark,
        string expectedComment)
    {
        var report = $"Name\tPath\tSize\tKind\tComments\r\nfile.txt\t/Volumes/Archive/file.txt\t100\tPlain text\t{expectedComment}\r\n";
        using var stream = new MemoryStream(LegacyEncodingSamples.Encode(encodingName, includeByteOrderMark, report));
        var plugin = new DiskCatalogMakerPlugin();

        Assert.True(plugin.CanHandleContent(stream));
        var result = await plugin.ReadAsync(
            new FileFormatReadRequest { FormatId = "diskcatalogmaker-text", Source = stream });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);
        var file = Assert.Single(document.Rows, row => Equals(row["name"], "file.txt"));
        var properties = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(file["properties"]);
        Assert.Equal(expectedComment, properties["Comments"]);
    }
}
