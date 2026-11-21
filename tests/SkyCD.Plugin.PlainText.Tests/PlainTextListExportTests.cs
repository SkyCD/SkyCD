using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.PlainText;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.PlainText.Models;

namespace SkyCD.Plugin.Host.Tests;

public class PlainTextListExportTests
{
    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new PlainTextListExportPlugin()]);
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
            FormatId = "skycd-txt-list",
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
            format => format.FormatId == "skycd-txt-list" && format.Extensions.Contains(".txt"));
        Assert.DoesNotContain(service.GetOpenFormats(),
            format => format.FormatId == "skycd-txt-list");
    }

    [Fact]
    public async Task GoldenLayout_HeaderSeparatorRowsAndAlignment()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("readme.txt", "D / readme.txt", "file", 512,
                new DateTimeOffset(2024, 1, 5, 10, 1, 0, TimeSpan.Zero), "docs", "D"),
            Row("notes.txt", "D / notes.txt", "file", 7, null, null, "D")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest { Rows = rows, Columns = ["Name", "SizeBytes", "Modified", "Description"] });
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("Name        SizeBytes  Modified                           Description", lines[0]);
        Assert.Equal("----------  ---------  ---------------------------------  -----------", lines[1]);
        Assert.Equal("readme.txt        512  2024-01-05T10:01:00.0000000+00:00  docs       ", lines[2]);
        Assert.Equal(69, lines[3].Length);
        Assert.Equal("notes.txt           7                                                ", lines[3]);
    }

    [Fact]
    public async Task Columns_ExportInExactOrder()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("b.txt", "D / b.txt", "file", 2, null, null, "D"),
            Row("a.txt", "D / a.txt", "file", 1, null, null, "D")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest { Rows = rows, Columns = ["Disk", "Name", "SizeBytes"] });
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("Disk  Name   SizeBytes", lines[0]);
        Assert.Equal("D     b.txt          2", lines[2]);
        Assert.Equal("D     a.txt          1", lines[3]);
    }

    [Fact]
    public async Task LongAndMultilineValues_TruncateAndNormalize()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row(new string('x', 200), "D", "file", 1, null, "a\tb\nc\rd", "D")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest { Rows = rows, Columns = ["Name", "Description"], MaxColumnWidth = 10 });
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("xxxxxxxxx…  a b c d   ", lines[2]);
    }

    [Fact]
    public async Task GroupedRows_SeparateGroupsWithBlankLines()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("b", "D", "file", 2, null, null, "X"),
            Row("a", "D", "file", 1, null, null, "Y"),
            Row("c", "D", "file", 3, null, null, "X")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest { Rows = rows, Columns = ["Disk", "Name"], GroupBy = "Disk" });
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("X     b   ", lines[2]);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal("Y     a   ", lines[4]);
        Assert.Equal(string.Empty, lines[5]);
        Assert.Equal("X     c   ", lines[6]);
    }

    [Fact]
    public async Task TabDelimited_WritesMachineReadableRows()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("a;b", "D", "file", 1234567, new DateTime(2024, 1, 5, 10, 1, 0, DateTimeKind.Utc), "x", "D")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest
            {
                Rows = rows,
                Columns = ["Name", "SizeBytes", "Modified"],
                Layout = PlainTextListLayout.TabDelimited
            });
        var lines = SplitLines(text.TrimStart('\uFEFF'));
        Assert.Equal("a;b\t1234567\t2024-01-05T10:01:00.0000000+00:00", lines[1]);
    }

    [Fact]
    public async Task Encodings_WriteBomOnlyForDefaultUtf8()
    {
        var service = CreateService();
        List<Dictionary<string, object?>> Rows() => [Row("café.txt", "D", "file", 1, null, null, "D")];

        async Task<byte[]> RawAsync(PlainTextListExportRequest request)
        {
            await using var target = new MemoryStream();
            var result = await service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-txt-list",
                Target = target,
                Payload = request
            });
            Assert.True(result.Success, $"Export failed: {result.Error}");
            return target.ToArray();
        }

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await RawAsync(new PlainTextListExportRequest { Rows = Rows() }))[..3]);
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, (await RawAsync(new PlainTextListExportRequest { Rows = Rows(), EncodingName = "utf-8-nobom" }))[..3]);
        Assert.Contains((byte)0xE9, await RawAsync(new PlainTextListExportRequest { Rows = Rows(), EncodingName = "windows-1252" }));

        await using var badTarget = new MemoryStream();
        var exception = await Assert.ThrowsAsync<FileFormatWriteFailedException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-txt-list",
            Target = badTarget,
            Payload = new PlainTextListExportRequest { Rows = Rows(), EncodingName = "ebcdic" }
        }));
        Assert.Contains("encoding", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisplayCulture_FormatsSizesAndTimestamps()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("a", "D", "file", 1234567, new DateTime(2024, 1, 5, 10, 1, 0), null, "D")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest { Rows = rows, Columns = ["SizeBytes", "Modified"], CultureName = "de-DE" });
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("1.234.567  05.01.2024 10:01:00", lines[2]);
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
            new PlainTextListExportRequest { Rows = rows, Columns = ["Name", "SizeBytes"] });

        Assert.Contains("huge.iso  5000000000", SplitLines(text.TrimStart('\uFEFF'))[2]);
    }

    [Fact]
    public async Task UnicodeMultilineNullAndEmpty_RoundTripSafely()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("夏祭り.mp3", "祭 / 夏祭り.mp3", "file", 4500000, "2024-07-01 10:00:00", "Expósito\nlínea 2", "祭"),
            new(StringComparer.OrdinalIgnoreCase) { ["Name"] = null, ["Description"] = string.Empty }
        };

        var text = await ExportToStringAsync(service, new PlainTextListExportRequest { Rows = rows });

        Assert.Contains("夏祭り.mp3", text);
        Assert.Contains("Expósito línea 2", text);
        Assert.DoesNotContain("\n\n\n", text.Replace("\r\n", "\n"));
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
            new PlainTextListExportRequest { Rows = rows, Columns = ["Name", "NoSuchColumn"] });
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("a.txt" + new string(' ', 14), lines[2]);
    }

    [Fact]
    public async Task JsonArrayPayload_UsesDefaults()
    {
        var service = CreateService();
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """[{ "name":"Docs", "kind":"folder", "sizeBytes":0 }]""");

        var text = await ExportToStringAsync(service, payload);
        var lines = SplitLines(text.TrimStart('\uFEFF'));

        Assert.Equal("Name  Path  Kind    SizeBytes  Modified  Description  Disk", lines[0]);
        Assert.Equal("----  ----  ------  ---------  --------  -----------  ----", lines[1]);
        Assert.StartsWith("Docs        folder", lines[2]);
    }

    [Fact]
    public async Task NoHeader_OmitsHeaderAndSeparator()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            Row("a.txt", "D", "file", 1, null, null, "D")
        };

        var text = await ExportToStringAsync(service,
            new PlainTextListExportRequest { Rows = rows, Columns = ["Name"], IncludeHeader = false });

        Assert.Equal("a.txt\r\n", text.TrimStart('\uFEFF'));
    }

    [Fact]
    public async Task FileTarget_WritesAtomicallyWithoutLeftovers()
    {
        var service = CreateService();
        var directory = Path.Combine(Path.GetTempPath(), "txt-atomic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "out.txt");
            await File.WriteAllTextAsync(destination, "OLD");
            await using (var target = File.Create(destination))
            {
                var result = await service.WriteAsync(new FileFormatWriteRequest
                {
                    FormatId = "skycd-txt-list",
                    Target = target,
                    FileName = "out.txt",
                    Payload = new PlainTextListExportRequest
                    {
                        Rows = new List<Dictionary<string, object?>> { Row("a.txt", "D", "file", 1, null, null, "D") },
                        Columns = ["Name"]
                    }
                });
                Assert.True(result.Success, $"Export failed: {result.Error}");
            }

            Assert.Contains("a.txt", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.GetFiles(directory, ".*.tmp*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Streaming_PreservesOrderReportsProgressAndHonoursCancellation()
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
            FormatId = "skycd-txt-list",
            Target = target,
            Payload = new PlainTextListExportRequest
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
        Assert.StartsWith("file-0000", lines[0]);
        Assert.Equal("file-0000.txt          0", lines[0]);
        Assert.StartsWith("file-4999", lines[4999]);

        await using var cancelledTarget = new MemoryStream();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.WriteAsync(
            new FileFormatWriteRequest
            {
                FormatId = "skycd-txt-list",
                Target = cancelledTarget,
                Payload = new PlainTextListExportRequest { Rows = Generate(5000) }
            },
            cancelled.Token));
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
