using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.TestSupport;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5PluginTests
{
    private readonly DiskCatalogMakerV5Plugin plugin = new();

    [Fact]
    public async Task ReadAsync_FromStream_ReconstructsRowsAndProgress()
    {
        var progress = new ProgressCollector();
        using var stream = new MemoryStream(BuildCatalog());
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = stream,
            FileName = "catalog.dcmf",
            Progress = progress
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerV5ReportDocument>(result.Payload);
        Assert.Equal(3, document.Count);
        Assert.Null(document.Summary.PackageRoot);
        Assert.Equal(100, progress.Values[^1]);
        Assert.Contains(5, progress.Values);
        Assert.Contains(100, progress.Values);
    }

    [Fact]
    public async Task ReadAsync_PackageDirectory_ResolvesThumbnailObjects()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;
        WritePackage(workspace, withCatalog: true, withThumbnail: true);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            FileName = root,
            SourcePath = root
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerV5ReportDocument>(result.Payload);
        Assert.Equal(root, document.Summary.PackageRoot);
        var properties = Properties(Find(document, "item:1:3"));
        Assert.Equal("Thumbnails/0a/0b/0c/0d.png", properties["Thumbnail"]);
        Assert.Equal("true", properties["ThumbnailResolved"]);
    }

    [Fact]
    public async Task ReadAsync_PackageWithoutThumbnailObject_ReportsUnresolvedButSucceeds()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;
        WritePackage(workspace, withCatalog: true, withThumbnail: false);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            SourcePath = root
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerV5ReportDocument>(result.Payload);
        Assert.Equal("false", Properties(Find(document, "item:1:3"))["ThumbnailResolved"]);
    }

    [Fact]
    public async Task ReadAsync_CatalogDcmFile_UsesParentDirectoryAsPackageRoot()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;
        WritePackage(workspace, withCatalog: true, withThumbnail: true);
        var catalogPath = Path.Combine(root, "Catalog.dcm");

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            FileName = catalogPath,
            SourcePath = catalogPath
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerV5ReportDocument>(result.Payload);
        Assert.Equal(root, document.Summary.PackageRoot);
        Assert.Equal("true", Properties(Find(document, "item:1:3"))["ThumbnailResolved"]);
    }

    [Fact]
    public async Task ReadAsync_PackageWithoutCatalog_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            SourcePath = root
        });

        Assert.False(result.Success);
        Assert.Contains("Catalog.dcm", result.Error, StringComparison.Ordinal);
        Assert.Contains(root, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_CorruptCatalog_ReturnsFailureWithDiagnostic()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;
        workspace.CreateFile("Catalog.dcm", [1, 2, 3, 4]);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            SourcePath = root
        });

        Assert.False(result.Success);
        Assert.Contains("shorter than", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsFailureInsteadOfThrowing()
    {
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            FileName = "catalog.dcmf"
        });

        Assert.False(result.Success);
        Assert.Contains("shorter than", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_Cancellation_Propagates()
    {
        using var stream = new MemoryStream(BuildCatalog());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = plugin.SupportedFormat.FormatId,
                Source = stream,
                FileName = "catalog.dcmf"
            },
            cancellation.Token));
    }

    [Fact]
    public async Task WriteAsync_ReportsReadOnlyFormat()
    {
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Target = Stream.Null,
            Payload = new object()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildCatalog()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "Photos", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(1, 3, 2, "pic.jpg", thumbnailKey: 0x0A0B0C0D);
        builder.AddThumbnail(0x0A0B0C0D, 3, "png");
        return builder.Build();
    }

    private static void WritePackage(TestWorkspace workspace, bool withCatalog, bool withThumbnail)
    {
        if (withCatalog)
        {
            workspace.CreateFile("Catalog.dcm", BuildCatalog());
        }

        if (withThumbnail)
        {
            workspace.CreateFile("Thumbnails/0a/0b/0c/0d.png", [1, 2, 3]);
        }
    }

    private static Dictionary<string, object?> Find(DiskCatalogMakerV5ReportDocument document, string nodeId)
    {
        return Assert.Single(document.Rows, row => (string?)row["nodeId"] == nodeId);
    }

    private static Dictionary<string, object?> Properties(Dictionary<string, object?> row)
    {
        return Assert.IsType<Dictionary<string, object?>>(row["properties"]);
    }

    private sealed class ProgressCollector : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }
}
