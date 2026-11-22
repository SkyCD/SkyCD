using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.Vvv;
using SkyCD.Plugin.FileFormat.Vvv.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.Vvv.Import;
using SkyCD.Plugin.FileFormat.Vvv.Models;
using SkyCD.Plugin.FileFormat.Vvv.Parsing;

namespace SkyCD.Plugin.FileFormat.Vvv.Tests;

public sealed class VvvParserTests
{
    private static async Task<VvvParseResult> ParseFixtureAsync(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vvv", fileName);
        await using var stream = File.OpenRead(fixturePath);
        return await VvvParser.ParseAsync(stream);
    }

    private static async Task<VvvParseResult> ParseTextAsync(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await VvvParser.ParseAsync(stream);
    }

    [Fact]
    public async Task PhysicalComma_MapsEntriesAndQuoting()
    {
        var result = await ParseFixtureAsync("physical-comma.csv");

        Assert.Equal(VvvExportScope.Physical, result.Scope);
        Assert.Equal(',', result.Delimiter);
        Assert.True(result.HasHeader);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(0, result.SkippedCount);

        var first = result.Entries[0];
        Assert.Equal("DVD1", first.Volume);
        Assert.Equal("Photos/2024", first.Path);
        Assert.Equal("img.jpg", first.Name);
        Assert.Equal(1024, first.Size);
        Assert.Equal("jpg", first.Extension);
        Assert.Equal("2024-02-01 10:00:00", first.LastModified);
        Assert.Equal("Sunset, \"quoted\"", first.Description);
        Assert.Null(first.PhysicalPath);

        Assert.Equal("söng.mp3", result.Entries[2].Name);
        Assert.Equal("Rock | Pop", result.Entries[2].Description);
    }

    [Fact]
    public async Task PhysicalTab_HeaderlessParses()
    {
        var result = await ParseFixtureAsync("physical-tab.tsv");

        Assert.Equal(VvvExportScope.Physical, result.Scope);
        Assert.Equal('\t', result.Delimiter);
        Assert.False(result.HasHeader);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("USB1", result.Entries[0].Volume);
        Assert.Equal("Docs/Manuals", result.Entries[1].Path);
        Assert.Equal("A | B", result.Entries[1].Description);
    }

    [Fact]
    public async Task PhysicalPipe_RepeatedFilenamesStayDistinct()
    {
        var result = await ParseFixtureAsync("physical-pipe.txt");

        Assert.Equal(VvvExportScope.Physical, result.Scope);
        Assert.Equal('|', result.Delimiter);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("CD1", result.Entries[0].Volume);
        Assert.Equal("CD2", result.Entries[1].Volume);

        var document = VvvHierarchyBuilder.Compose(result, "pipe.txt");
        var ids = document.Rows.Select(row => (string)row["nodeId"]!).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task VirtualComma_MapsPhysicalPaths()
    {
        var result = await ParseFixtureAsync("virtual-comma.csv");

        Assert.Equal(VvvExportScope.Virtual, result.Scope);
        Assert.True(result.HasHeader);
        Assert.Equal(2, result.Entries.Count);

        var first = result.Entries[0];
        Assert.Equal(string.Empty, first.Volume);
        Assert.Equal(@"D:\Photos\2024\img.jpg", first.PhysicalPath);
        Assert.Equal("D:/Photos/2024", first.Path);

        var document = VvvHierarchyBuilder.Compose(result, "virtual-comma");
        Assert.Equal("virtual-comma", document.Rows[0]["name"]);
        Assert.Contains(document.Rows, row => string.Equals((string)row["name"]!, "Photos", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalizedHeader_TreatedAsHeaderWithWarning()
    {
        var result = await ParseFixtureAsync("localized-header.csv");

        Assert.Equal(VvvExportScope.Physical, result.Scope);
        Assert.True(result.HasHeader);
        Assert.Single(result.Entries);
        Assert.Equal("foto.jpg", result.Entries[0].Name);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task BadRows_SkippedWithReasons()
    {
        var result = await ParseFixtureAsync("bad-rows.csv");

        Assert.Single(result.Entries);
        Assert.Equal("good.txt", result.Entries[0].Name);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(2, result.SkippedReasons.Count);
        Assert.Contains(result.SkippedReasons, reason => reason.Contains("not a number"));
        Assert.Contains(result.SkippedReasons, reason => reason.Contains("no file name"));
    }

    [Fact]
    public async Task AmbiguousColumns_ThrowsAmbiguousFormat()
    {
        await Assert.ThrowsAsync<VvvAmbiguousFormatException>(() => ParseFixtureAsync("ambiguous.csv"));
    }

    [Fact]
    public async Task SingleColumn_ThrowsAmbiguousFormat()
    {
        await Assert.ThrowsAsync<VvvAmbiguousFormatException>(() => ParseFixtureAsync("single-column.txt"));
    }

    [Fact]
    public async Task HeaderOnly_ThrowsEmptyReport()
    {
        await Assert.ThrowsAsync<VvvEmptyReportException>(() => ParseFixtureAsync("header-only.csv"));
    }

    [Fact]
    public async Task EmptyInput_ThrowsMalformed()
    {
        await Assert.ThrowsAsync<VvvMalformedException>(() => ParseTextAsync(string.Empty));
    }

    [Fact]
    public async Task NonUtf8Input_ThrowsMalformed()
    {
        using var stream = new MemoryStream([0x56, 0x6F, 0x6C, 0xFF, 0x0A]);
        await Assert.ThrowsAsync<VvvMalformedException>(() => VvvParser.ParseAsync(stream));
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vvv", "physical-comma.csv");
        await using var stream = File.OpenRead(fixturePath);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VvvParser.ParseAsync(stream, source.Token));
    }

    [Fact]
    public async Task PhysicalComma_BuildsHierarchyWithStableIds()
    {
        var result = await ParseFixtureAsync("physical-comma.csv");
        var first = VvvHierarchyBuilder.Compose(result, "disc.csv");
        var second = VvvHierarchyBuilder.Compose(result, "disc.csv");

        // 2 volumes + 3 folders (Photos, Photos/2024 shared, Music) + 3 files
        Assert.Equal(8, first.Count);
        var firstIds = first.Rows.Select(row => (string)row["nodeId"]!).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var secondIds = second.Rows.Select(row => (string)row["nodeId"]!).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(firstIds, secondIds);

        var media = first.Rows.Single(row => string.Equals((string)row["name"]!, "DVD1", StringComparison.Ordinal));
        Assert.Equal("media", media["kind"]);
        Assert.Null(media["parentId"]);
        var file = first.Rows.Single(row => string.Equals((string)row["name"]!, "img.jpg", StringComparison.Ordinal));
        Assert.Equal("file", file["kind"]);
        Assert.Equal(1024L, file["sizeBytes"]);
    }
}
