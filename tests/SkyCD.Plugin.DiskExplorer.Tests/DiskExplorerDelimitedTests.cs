using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.DiskExplorer;
using SkyCD.Plugin.DiskExplorer.Delimited;
using SkyCD.Plugin.DiskExplorer.Delimited.Exceptions;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class DiskExplorerDelimitedTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskExplorer", name);

    private static async Task<DiskExplorerReportDocument> ReadAsync(string name, string? formatId = null)
    {
        var service = new FileFormatManager([new DiskExplorerDelimitedPlugin()]);
        await using var source = File.OpenRead(FixturePath(name));
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = formatId ?? "disk-explorer-delimited",
            Source = source,
            FileName = name
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        return Assert.IsType<DiskExplorerReportDocument>(result.Payload);
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(DiskExplorerReportDocument document) =>
        document.Rows.ToDictionary(
            row => Convert.ToString(row["id"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            row => row,
            StringComparer.Ordinal);

    private static string TextOf(Dictionary<string, object?> row, string key) =>
        Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static Dictionary<string, object?> PropertiesOf(Dictionary<string, object?> row) =>
        Assert.IsType<Dictionary<string, object?>>(row["properties"]);

    [Fact]
    public void GetFormats_RegistersDelimitedAsReadableAndWritable()
    {
        var service = new FileFormatManager([new DiskExplorerDelimitedPlugin()]);
        var format = Assert.Single(service.GetOpenFormats(), f => f.FormatId == "disk-explorer-delimited");

        Assert.Contains(".tsv", format.Extensions);
        Assert.True(format.CanRead);
        Assert.True(format.CanWrite);
    }

    [Fact]
    public async Task TabImport_BuildsVolumesFoldersAndFiles()
    {
        var document = await ReadAsync("delimited-tab.tsv");
        var nodes = IndexById(document);

        Assert.Equal(7, document.Count);
        Assert.True(nodes.ContainsKey("volume:Media1"));
        Assert.True(nodes.ContainsKey("volume:Media2"));
        Assert.True(nodes.ContainsKey("folder:Media1/Docs"));
        Assert.True(nodes.ContainsKey("folder:Media1/Docs/Photos"));
        Assert.True(nodes.ContainsKey("file:Media1/Docs/Photos/sunset.jpg"));

        var sunset = nodes["file:Media1/Docs/Photos/sunset.jpg"];
        Assert.Equal("3145728", TextOf(sunset, "size"));
        Assert.Equal("2024-02-01", PropertiesOf(sunset)["Date"]);
        Assert.Equal("10:00:00", PropertiesOf(sunset)["Time"]);
        Assert.Equal("JPEG Image", PropertiesOf(sunset)["FormatType"]);
        Assert.Equal("Beach, \"sunny\" day", PropertiesOf(sunset)["Description"]);

        var big = nodes["file:Media2/big.bin"];
        Assert.Equal("5000000000", TextOf(big, "size"));

        var cafe = nodes["file:Media1/Docs/café.txt"];
        Assert.Equal("A\tB tabbed", PropertiesOf(cafe)["Description"]);

        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(2, document.Summary.Folders);
        Assert.Equal(3, document.Summary.Files);
        Assert.Equal(0, document.Summary.SkippedRows);
        Assert.DoesNotContain(document.Summary.Notes, note => note.Contains("partial"));
    }

    [Fact]
    public async Task TabImport_CreatesCategoryFolders()
    {
        var document = await ReadAsync("delimited-tab.tsv");
        var nodes = IndexById(document);

        Assert.True(nodes.ContainsKey("folder:Media1/Docs/Photos"));
        var photo = nodes["file:Media1/Docs/Photos/sunset.jpg"];
        Assert.Equal(nodes["folder:Media1/Docs/Photos"]["id"], photo["parentId"]);
    }

    [Fact]
    public async Task CommaImport_HandlesQuotesMultilineAndLowercaseLabels()
    {
        var document = await ReadAsync("delimited-comma.csv");
        var nodes = IndexById(document);

        Assert.True(nodes.ContainsKey("file:Media1/Docs/Reports/file1.txt"));
        var file = nodes["file:Media1/Docs/Reports/file1.txt"];
        Assert.Equal("Line one\nLine two, quoted", PropertiesOf(file)["Description"]);
        Assert.True(nodes.ContainsKey("folder:Media1/Docs/Reports"));
    }

    [Fact]
    public async Task SemicolonImport_RetainsUnknownColumnsAsExtensionMetadata()
    {
        var document = await ReadAsync("delimited-semicolon.csv");
        var nodes = IndexById(document);

        var file = Assert.Single(nodes.Values, row => TextOf(row, "name") == "doc.txt");
        var properties = PropertiesOf(file);
        Assert.Equal("extra-value", properties["DiskExplorer.CUSTOMX"]);
        Assert.Contains(document.Summary.Notes, note => note.Contains("DiskExplorer.CUSTOMX"));
    }

    [Fact]
    public async Task PipeFlatImport_AttachesFlatItemsAndMarksPartial()
    {
        var document = await ReadAsync("delimited-pipe-flat.txt");

        var file = Assert.Single(document.Rows, row => TextOf(row, "name") == "loose.txt");
        Assert.Equal("volume:Media1", TextOf(file, "parentId"));
        Assert.Equal("A-42", PropertiesOf(file)["DiskExplorer.SHELF"]);
        Assert.Contains(document.Summary.Notes, note => note.Contains("partial"));
    }

    [Fact]
    public async Task Windows1252Import_DecodesHistoricalBytes()
    {
        var document = await ReadAsync("delimited-1252.csv");
        var nodes = IndexById(document);

        var file = Assert.Single(nodes.Values, row => TextOf(row, "name") == "café");
        Assert.Equal("naïve", PropertiesOf(file)["Description"]);
    }

    [Fact]
    public async Task Utf8BomImport_StripsPreamble()
    {
        var document = await ReadAsync("delimited-utf8bom.tsv");

        var file = Assert.Single(document.Rows, row => TextOf(row, "name") == "utf8.txt");
        Assert.Equal("volume:Media1", TextOf(file, "parentId"));
    }

    [Fact]
    public async Task NoHeader_ThrowsAmbiguous()
    {
        var service = new FileFormatManager([new DiskExplorerDelimitedPlugin()]);
        await using var source = File.OpenRead(FixturePath("delimited-noheader.csv"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-delimited",
            Source = source,
            FileName = "delimited-noheader.csv"
        }));

        Assert.Contains("header", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InconsistentFieldCount_ThrowsMalformed()
    {
        var service = new FileFormatManager([new DiskExplorerDelimitedPlugin()]);
        await using var source = File.OpenRead(FixturePath("delimited-inconsistent.csv"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-delimited",
            Source = source,
            FileName = "delimited-inconsistent.csv"
        }));

        Assert.Contains("expected 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HeaderOnly_ThrowsEmptyReport()
    {
        var service = new FileFormatManager([new DiskExplorerDelimitedPlugin()]);
        await using var source = File.OpenRead(FixturePath("delimited-header-only.tsv"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-delimited",
            Source = source,
            FileName = "delimited-header-only.tsv"
        }));

        Assert.Contains("no entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognizer_AcceptsDelimitedAndRejectsOthers()
    {
        var plugin = new DiskExplorerDelimitedPlugin();

        using (var stream = File.OpenRead(FixturePath("delimited-tab.tsv")))
        {
            Assert.True(plugin.CanHandleContent(stream, "report.tsv"));
            Assert.Equal(0, stream.Position);
        }

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "\"Name\",\"Size\",\"Ext\",\"Last modified\",\"PhysicalPath\",\"Description\"\r\n\"a\",1,\"x\",\"\",\"p\",\"\"\r\n")))
        {
            Assert.False(plugin.CanHandleContent(stream, "vvv.csv"));
        }

        using (var stream = File.OpenRead(FixturePath("complete.xml")))
        {
            Assert.False(plugin.CanHandleContent(stream, "complete.xml"));
        }
    }

    [Fact]
    public async Task Export_DefaultDialectRoundTrips()
    {
        var document = await ReadAsync("delimited-tab.tsv");
        var plugin = new DiskExplorerDelimitedPlugin();

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "disk-explorer-delimited",
            FileName = "export.csv",
            Payload = new DiskExplorerDelimitedExportRequest { Rows = document.Rows }
        });
        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var import = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-delimited",
            Source = target,
            FileName = "export.csv"
        });
        Assert.True(import.Success, import.Error);
        var roundTripped = Assert.IsType<DiskExplorerReportDocument>(import.Payload);
        Assert.Equal(document.Count, roundTripped.Count);
    }

    [Fact]
    public async Task Export_GuardsFormulasAndUsesCompatibilityEncoding()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "volume:V",
                ["name"] = "V",
                ["parentId"] = null,
                ["type"] = "media",
                ["size"] = 0L,
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Description"] = "=cmd|'/c calc'!A0"
                }
            }
        };

        var plugin = new DiskExplorerDelimitedPlugin();
        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "disk-explorer-delimited",
            FileName = "export.csv",
            Payload = new DiskExplorerDelimitedExportRequest
            {
                Rows = rows,
                Encoding = DiskExplorerDelimitedEncoding.Windows1252,
                Columns = ["NAME", "DESC"]
            }
        });
        Assert.True(write.Success, write.Error);

        var bytes = target.ToArray();
        var text = System.Text.CodePagesEncodingProvider.Instance.GetEncoding("windows-1252")!.GetString(bytes);
        Assert.StartsWith("NAME,DESC\r\n", text);
        Assert.Contains("\"'=cmd", text);
    }

    [Fact]
    public async Task CancelledImport_ThrowsOperationCanceled()
    {
        await using var source = File.OpenRead(FixturePath("delimited-tab.tsv"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await Task.Run(() => DiskExplorerDelimitedParser.Parse(source, "delimited-tab.tsv", canceled.Token));
        });
    }
}

