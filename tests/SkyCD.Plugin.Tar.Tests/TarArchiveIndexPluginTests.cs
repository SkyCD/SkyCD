using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Tar;
using Xunit;

namespace SkyCD.Plugin.Tar.Tests;

public class TarArchiveIndexPluginTests
{
    [Fact]
    public void Capability_IsFileFormatOnly_WithoutIndexWorkItems()
    {
        Assert.Equal("skycd-tar", new TarArchiveIndexPlugin().SupportedFormat.FormatId);
        Assert.DoesNotContain(typeof(TarArchiveIndexPlugin).GetInterfaces(),
            type => typeof(IIndexWorkItemProvider).IsAssignableFrom(type));
    }

    [Fact]
    public void OpenFormats_IncludeTarVariants_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format =>
            format.FormatId == "skycd-tar" &&
            format.Extensions.Contains(".tar") &&
            format.Extensions.Contains(".tar.gz") &&
            format.Extensions.Contains(".tgz"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-tar");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyTarFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-tar",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesTarAndGzipTar_WithPathNormalizationAndMetadata()
    {
        var service = CreateService();

        // Read from physical tar file
        var tarPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tar", "sample.tar");
        await using var tarFileStream = new FileStream(tarPath, FileMode.Open);
        var tarResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-tar",
            Source = tarFileStream
        });
        Assert.True(tarResult.Success);
        var tarRows = Assert.IsType<List<Dictionary<string, object?>>>(tarResult.Payload);
        Assert.Contains(tarRows, row => Equals(row["fullPath"], "root/deep/įrašas.txt"));

        // Read from physical tar.gz file
        var tarGzPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tar", "sample.tar.gz");
        await using var tarGzFileStream = new FileStream(tarGzPath, FileMode.Open);
        var tarGzResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-tar",
            Source = tarGzFileStream
        });
        Assert.True(tarGzResult.Success);
        var tarGzRows = Assert.IsType<List<Dictionary<string, object?>>>(tarGzResult.Payload);
        Assert.Contains(tarGzRows, row => Equals(row["kind"], "file") && Equals(row["sizeBytes"], "5"));
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new TarArchiveIndexPlugin()]);
    }
}
