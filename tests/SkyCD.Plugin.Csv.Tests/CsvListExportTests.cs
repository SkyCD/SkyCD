using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.Csv.Models;
using SkyCD.Plugin.Csv.Parsing;

namespace SkyCD.Plugin.Host.Tests;

public class CsvListExportTests
{
    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new CsvCatalogPlugin(), new CsvListExportPlugin()]);
    }

    private static Dictionary<string, object?> Row(
        string name, string path, string kind, long size, object? modified, string? description, string? disk)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = name,
            ["FullPath"] = path,
            ["Kind"] = kind,
            ["SizeBytes"] = size,
            ["Modified"] = modified,
            ["Description"] = description,
            ["Disk"] = disk
        };
    }

    private static async Task<string> ExportToStringAsync(
        FileFormatManager service, object payload, Encoding? encoding = null)
    {
        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = target,
            Payload = payload
        });
        Assert.True(result.Success, $"Export failed: {result.Error}");
        return (encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            .GetString(target.ToArray());
    }

    private static string[] SplitLines(string text) =>
        text.Split(["\r\n"], StringSplitOptions.None);

    [Fact]
    public void GetSaveFormats_RegistersListExportAsExportOnly()
    {
        var service = CreateService();

        Assert.Contains(service.GetSaveFormats(),
            format => format.FormatId == "skycd-csv-list" && format.Extensions.Contains(".csv"));
        Assert.DoesNotContain(service.GetOpenFormats(),
            format => format.FormatId == "skycd-csv-list");
    }

    [Fact]
    public async Task Defaults_WriteHeaderBomInvariantSizeAndIsoTimestamp()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("readme.txt", "Disc A / readme.txt", "file", 512,
                new DateTimeOffset(2024, 1, 5, 10, 1, 0, TimeSpan.Zero), "docs", "Disc A")
        };

        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = target,
            Payload = rows
        });

        Assert.True(result.Success, $"Export failed: {result.Error}");
        var bytes = target.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        var lines = SplitLines(text.TrimStart('\uFEFF'));
        Assert.Equal("Name,Path,Kind,SizeBytes,Modified,Description,Disk", lines[0]);
        Assert.Equal(
            "readme.txt,Disc A / readme.txt,file,512,2024-01-05T10:01:00.0000000+00:00,docs,Disc A",
            lines[1]);
    }

    [Fact]
    public async Task SelectedColumns_ExportInExactOrder()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("b.txt", "D / b.txt", "file", 2, null, null, "D"),
            Row("a.txt", "D / a.txt", "file", 1, null, null, "D")
        };
        var request = new CsvListExportRequest
        {
            Rows = rows,
            Columns = ["Disk", "Name", "SizeBytes"]
        };

        var text = await ExportToStringAsync(service, request);
        var lines = SplitLines(text.TrimStart('\uFEFF'));
        Assert.Equal("Disk,Name,SizeBytes", lines[0]);
        Assert.Equal("D,b.txt,2", lines[1]);
        Assert.Equal("D,a.txt,1", lines[2]);
    }

    [Fact]
    public async Task Quoting_CoversSeparatorsQuotesLineBreaksAndEdgeSpaces()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("a,b.txt", "D / x", "file", 1, null, "line1\nline2", "D"),
            Row("q\"q.txt", "D / y", "file", 1, null, " padded ", "D"),
            Row("plain.txt", "D / z", "file", 1, null, "a\rb", "D")
        };
        var request = new CsvListExportRequest { Rows = rows, Columns = ["Name", "Description"] };

        var text = await ExportToStringAsync(service, request);
        var lines = SplitLines(text.TrimStart('\uFEFF'));
        Assert.Equal("Name,Description", lines[0]);
        Assert.Equal("\"a,b.txt\",\"line1\nline2\"", lines[1]);
        Assert.Equal("\"q\"\"q.txt\",\" padded \"", lines[2]);
        Assert.Equal("plain.txt,\"a\rb\"", lines[3]);
    }

    [Fact]
    public async Task FormulaValues_AreNeutralizedByDefaultAndExactInRawMode()
    {
        var service = CreateService();
        List<Dictionary<string, object?>> Rows() =>
        [
            Row("=2+2", "D", "file", 1, null, null, "D"),
            Row("@SUM(A1)", "D", "file", 1, null, null, "D"),
            Row("-5", "D", "file", 1, null, null, "D")
        ];

        var guarded = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = Rows(), Columns = ["Name"] });
        var guardedLines = SplitLines(guarded.TrimStart('\uFEFF'));
        Assert.Equal("'=2+2", guardedLines[1]);
        Assert.Equal("'@SUM(A1)", guardedLines[2]);
        Assert.Equal("'-5", guardedLines[3]);

        var raw = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = Rows(), Columns = ["Name"], AllowFormulas = true });
        var rawLines = SplitLines(raw.TrimStart('\uFEFF'));
        Assert.Equal("=2+2", rawLines[1]);
        Assert.Equal("@SUM(A1)", rawLines[2]);
    }

    [Fact]
    public async Task NullAndEmpty_AreDistinct()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["Name"] = null, ["Description"] = string.Empty }
        };
        var text = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = rows, Columns = ["Name", "Description"] });
        var lines = SplitLines(text.TrimStart('\uFEFF'));
        Assert.Equal(",\"\"", lines[1]);
    }

    [Fact]
    public async Task Encodings_WriteBomOnlyForDefaultUtf8()
    {
        var service = CreateService();
        List<Dictionary<string, object?>> Rows() => [Row("café.txt", "D", "file", 1, null, null, "D")];

        await using var defaultTarget = new MemoryStream();
        var defaultResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = defaultTarget,
            Payload = new CsvListExportRequest { Rows = Rows() }
        });
        Assert.True(defaultResult.Success);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, defaultTarget.ToArray()[..3]);

        await using var noBomTarget = new MemoryStream();
        var noBomResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = noBomTarget,
            Payload = new CsvListExportRequest { Rows = Rows(), EncodingName = "utf-8-nobom" }
        });
        Assert.True(noBomResult.Success);
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, noBomTarget.ToArray()[..3]);

        await using var latinTarget = new MemoryStream();
        var latinResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = latinTarget,
            Payload = new CsvListExportRequest { Rows = Rows(), EncodingName = "windows-1252" }
        });
        Assert.True(latinResult.Success);
        var latinBytes = latinTarget.ToArray();
        Assert.Contains((byte)0xE9, latinBytes);

        await using var badTarget = new MemoryStream();
        var exception = await Assert.ThrowsAsync<FileFormatWriteFailedException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = badTarget,
            Payload = new CsvListExportRequest { Rows = Rows(), EncodingName = "ebcdic" }
        }));
        Assert.Contains("encoding", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, badTarget.Length);
    }

    [Fact]
    public async Task Delimiters_SemicolonAndTabAreQuotedCorrectly()
    {
        var service = CreateService();
        List<Dictionary<string, object?>> Rows() =>
            [Row("a;b", "D", "file", 1, null, "x\ty", "D")];

        var semi = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = Rows(), Columns = ["Name", "Description"], Separator = ';' });
        Assert.Contains("\"a;b\";x\ty", SplitLines(semi.TrimStart('\uFEFF'))[1]);

        var tab = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = Rows(), Columns = ["Name", "Description"], Separator = '\t' });
        Assert.Contains("a;b\t\"x\ty\"", SplitLines(tab.TrimStart('\uFEFF'))[1]);

        await using var badTarget = new MemoryStream();
        var exception = await Assert.ThrowsAsync<FileFormatWriteFailedException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = badTarget,
            Payload = new CsvListExportRequest { Rows = Rows(), Separator = '"' }
        }));
        Assert.Contains("separator", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, badTarget.Length);
    }

    [Fact]
    public async Task LargeSizes_RenderInvariantBeyond4GiB()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("huge.iso", "D", "file", 5_000_000_000L, null, null, "D")
        };
        var text = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = rows, Columns = ["Name", "SizeBytes"] });
        Assert.Contains("huge.iso,5000000000", SplitLines(text.TrimStart('\uFEFF'))[1]);
    }

    [Fact]
    public async Task UnicodeAndMultiline_RoundTripThroughCsvParser()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("夏祭り.mp3", "祭 / 夏祭り.mp3", "file", 4500000, "2024-07-01 10:00:00", "Expósito\nlínea 2", "祭")
        };
        var text = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = rows });

        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        var parsed = await CsvParser.ReadCsvAsync(reader);

        Assert.Equal(',', parsed.Delimiter);
        Assert.Equal(CsvListExportPlugin.DefaultColumns, parsed.Rows[0]);
        var row = Assert.Single(parsed.Rows.Skip(1));
        Assert.Equal(
            new[] { "夏祭り.mp3", "祭 / 夏祭り.mp3", "file", "4500000", "2024-07-01T10:00:00.0000000+00:00", "Expósito\nlínea 2", "祭" },
            row);
    }

    [Fact]
    public async Task UnknownColumns_ExportAsEmptyFields()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("a.txt", "D", "file", 1, null, null, "D")
        };
        var text = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = rows, Columns = ["Name", "NoSuchColumn"] });
        Assert.Equal("a.txt,", SplitLines(text.TrimStart('\uFEFF'))[1]);
    }

    [Fact]
    public async Task JsonArrayPayload_UsesDefaults()
    {
        var service = CreateService();
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """[{ "name":"Docs", "kind":"folder", "sizeBytes":0 }]""");
        var text = await ExportToStringAsync(service, payload);
        var lines = SplitLines(text.TrimStart('\uFEFF'));
        Assert.Equal("Name,Path,Kind,SizeBytes,Modified,Description,Disk", lines[0]);
        Assert.StartsWith("Docs,,folder,0,,,", lines[1]);
    }

    [Fact]
    public async Task NoHeader_OmitsHeaderRow()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("a.txt", "D", "file", 1, null, null, "D")
        };
        var text = await ExportToStringAsync(service,
            new CsvListExportRequest { Rows = rows, Columns = ["Name"], IncludeHeader = false });
        Assert.Equal("a.txt\r\n", text.TrimStart('\uFEFF'));
    }

    [Fact]
    public async Task Streaming_PreservesOrderReportsProgressAndHonorsCancellation()
    {
        var service = CreateService();
        static IEnumerable<Dictionary<string, object?>> Generate(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = $"file-{i:0000}.txt",
                    ["SizeBytes"] = (long)i
                };
            }
        }

        var seen = new List<int>();
        IProgress<int> progress = new InlineProgress(seen.Add);
        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-csv-list",
            Target = target,
            Payload = new CsvListExportRequest
            {
                Rows = Generate(5000),
                Columns = ["Name", "SizeBytes"],
                IncludeHeader = false
            },
            Progress = progress
        });

        Assert.True(result.Success);
        Assert.Equal(100, seen[^1]);
        Assert.True(seen.SequenceEqual(seen.OrderBy(v => v)));
        var lines = SplitLines(Encoding.UTF8.GetString(target.ToArray()).TrimStart('\uFEFF'));
        Assert.Equal(5000, lines.Count(line => line.Length > 0));
        Assert.Equal("file-0000.txt,0", lines[0]);
        Assert.Equal("file-4999.txt,4999", lines[4999]);

        await using var cancelledTarget = new MemoryStream();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.WriteAsync(
            new FileFormatWriteRequest
            {
                FormatId = "skycd-csv-list",
                Target = cancelledTarget,
                Payload = new CsvListExportRequest { Rows = Generate(5000) }
            },
            cancelled.Token));
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
