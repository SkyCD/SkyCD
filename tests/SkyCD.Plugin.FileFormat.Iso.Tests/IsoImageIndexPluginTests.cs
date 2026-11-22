using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Iso;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class IsoImageIndexPluginTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(IsoImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-iso", fileFormat.SupportedFormat.FormatId);
        Assert.Equal(".iso", Assert.Single(fileFormat.SupportedFormat.Extensions));
    }

    [Fact]
    public void OpenFormats_IncludeIso_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-iso" && format.Extensions.Contains(".iso"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-iso");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyIsoFormat()
    {
        var service = CreateService();
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-iso",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsIso9660Tree_WithSizesAndTimestamps()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("catalog-sample.iso"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = source
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "ROOT"));
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "ROOT/DEEP"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT")
            && Equals(row["sizeBytes"], "19")
            && row.ContainsKey("modifiedUtc"));
    }

    [Fact]
    public async Task ReadAsync_ListsUdfTree_AndPreservesStoredCase()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("catalog-sample-udf.iso"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = source
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "ROOT/DEEP"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT")
            && Equals(row["sizeBytes"], "17"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "README.TXT")
            && Equals(row["sizeBytes"], "10"));
    }

    [Fact]
    public async Task ReadAsync_DamagedImage_ReportsTypedFailure()
    {
        await using var truncated = TruncatedFixture();

        var result = await new IsoImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = truncated
        });

        Assert.False(result.Success);
        Assert.StartsWith("ISO_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_NonImageSource_ReportsUnsupported()
    {
        await using var zeros = new MemoryStream(new byte[64 * 1024]);

        var result = await new IsoImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = zeros
        });

        Assert.False(result.Success);
        Assert.StartsWith("ISO_UNSUPPORTED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_ForwardOnlySource_IsBufferedAndSucceeds()
    {
        await using var fixture = File.OpenRead(FixturePath("catalog-sample.iso"));
        await using var forwardOnly = new ForwardOnlyStream(fixture);

        var result = await new IsoImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = forwardOnly
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    [Fact]
    public async Task ReadAsync_Cancellation_Propagates()
    {
        using var source = File.OpenRead(FixturePath("catalog-sample.iso"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new IsoImageIndexPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-iso",
                Source = source
            }, cancellation.Token));
    }

    private static FileFormatManager CreateService() => new([new IsoImageIndexPlugin()]);

    internal static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Iso", fileName);

    private static MemoryStream TruncatedFixture()
    {
        var bytes = File.ReadAllBytes(FixturePath("catalog-sample.iso"));
        return new MemoryStream(bytes[..40_000]);
    }
}
