using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.DiskExplorer.Ded;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.DiskExplorer.Ded.Models;
using SkyCD.Plugin.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.DiskExplorer.Tests.Ded;

public sealed class DedPluginTests
{
    private static FileFormatManager CreateManager() => new([new DedPlugin()]);

    [Fact]
    public void Descriptor_IsWritableDedFormat()
    {
        var format = new DedPlugin().SupportedFormat;

        Assert.Equal("disk-explorer-ded", format.FormatId);
        Assert.Equal("Disk Explorer Professional Database", format.DisplayName);
        Assert.Contains(".ded", format.Extensions);
        Assert.True(format.CanRead);
        Assert.True(format.CanWrite);

        var formats = CreateManager();
        Assert.Contains(formats.GetOpenFormats(), item => item.FormatId == "disk-explorer-ded");
        Assert.Contains(formats.GetSaveFormats(), item => item.FormatId == "disk-explorer-ded");
        Assert.Equal("disk-explorer-ded", formats.ResolveFormatId(null, "catalog.DED", forWrite: true));
    }

    [Fact]
    public void Detection_RequiresDedbHeaderAndBounds()
    {
        var database = DedTestData.BuildSampleDatabase();
        using var valid = DedTestData.WriteToStream(database);
        Assert.True(new DedPlugin().CanHandleContent(valid, "catalog.ded"));
        Assert.Equal(0, valid.Position);

        using var garbage = new MemoryStream(new byte[512]);
        Assert.False(new DedPlugin().CanHandleContent(garbage, "catalog.ded"));

        using var wrongExtension = DedTestData.WriteToStream(database);
        Assert.False(new DedPlugin().CanHandleContent(wrongExtension, "catalog.bin"));

        var truncated = DedTestData.WriteToStream(database).ToArray();
        DedTestData.WriteUInt32(truncated, 0x08, 100_000);
        using var badTable = new MemoryStream(truncated);
        Assert.False(new DedPlugin().CanHandleContent(badTable, "catalog.ded"));
    }

    [Fact]
    public async Task ReadThroughManager_BuildsRowsWithParents()
    {
        var manager = CreateManager();
        using var source = DedTestData.WriteToStream(DedTestData.BuildSampleDatabase());
        var result = await manager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = string.Empty,
            Source = source,
            FileName = "sample.ded"
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DedReportDocument>(result.Payload);
        Assert.Equal(8, document.Rows.Count);
        var ids = document.Rows.Select(row => (string)row["id"]!).ToHashSet(StringComparer.Ordinal);
        Assert.All(
            document.Rows.Where(row => row["parentId"] is not null),
            row => Assert.Contains((string)row["parentId"]!, ids));
    }

    [Fact]
    public async Task WriteRowsThroughManager_RoundTripsHierarchy()
    {
        var manager = CreateManager();
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["nodeId"] = "n1",
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = "Volume A",
                ["sizeBytes"] = "1099511627776"
            },
            new()
            {
                ["nodeId"] = "n2",
                ["parentId"] = "n1",
                ["kind"] = "folder",
                ["name"] = "Folder A",
                ["sizeBytes"] = "0"
            },
            new()
            {
                ["nodeId"] = "n3",
                ["parentId"] = "n2",
                ["kind"] = "file",
                ["name"] = "movie.mkv",
                ["sizeBytes"] = "4294967296"
            }
        };

        using var target = new MemoryStream();
        var write = await manager.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = string.Empty,
            FileName = "catalog.ded",
            Target = target,
            Payload = rows
        });
        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var read = await manager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = string.Empty,
            Source = target,
            FileName = "catalog.ded"
        });
        Assert.True(read.Success, read.Error);

        var document = Assert.IsType<DedReportDocument>(read.Payload);
        Assert.Equal(
            new object?[] { "Volume A", "Folder A", "movie.mkv" },
            document.Rows.Select(row => row["name"]).ToArray());
        Assert.Equal(
            new object?[] { "media", "folder", "file" },
            document.Rows.Select(row => row["kind"]).ToArray());
        Assert.Equal("1099511627776", document.Rows[0]["sizeBytes"]);
        Assert.Equal("4294967296", document.Rows[2]["sizeBytes"]);
        Assert.Equal("ded:1", document.Rows[1]["parentId"]);
        Assert.Equal("ded:2", document.Rows[2]["parentId"]);
    }

    [Fact]
    public async Task WriteReportDocument_PreservesOpaqueData()
    {
        var source = DedTestData.BuildSampleDatabase();
        using var first = DedTestData.WriteToStream(source);
        var manager = CreateManager();
        var read = await manager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = string.Empty,
            Source = first,
            FileName = "sample.ded"
        });
        Assert.True(read.Success, read.Error);

        using var target = new MemoryStream();
        var write = await manager.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = string.Empty,
            FileName = "copy.ded",
            Target = target,
            Payload = read.Payload!
        });
        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var reread = DedReader.Read(target);
        Assert.Equal(source.Objects.Count, reread.Objects.Count);
        Assert.Equal(source.Find(6)!.Payloads.Select(payload => Convert.ToBase64String(payload.Data)),
            reread.Find(6)!.Payloads.Select(payload => Convert.ToBase64String(payload.Data)));
        Assert.Equal(0x0000000Fu, reread.Find(6)!.Flags);
    }

    [Fact]
    public async Task PasswordProtectedRead_ReturnsTypedFailure()
    {
        var database = DedTestData.BuildSampleDatabase();
        database.HeaderFlags |= DedFormat.HeaderFlagPasswordProtected;
        using var source = DedTestData.WriteToStream(database);

        var result = await new DedPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-ded",
            Source = source,
            FileName = "protected.ded"
        });

        Assert.False(result.Success);
        Assert.Contains("password", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_PropagatesFromPlugin()
    {
        using var source = DedTestData.WriteToStream(DedTestData.BuildSampleDatabase());
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DedPlugin().ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = "disk-explorer-ded",
                Source = source,
                FileName = "sample.ded"
            },
            cancellation.Token));
    }
}
