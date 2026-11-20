using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Vmdk.Tests;

public sealed class VmdkDiskImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeVmdk_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "skycd-vmdk" && format.Extensions.Contains(".vmdk"));
        Assert.DoesNotContain(service.GetSaveFormats(), format => format.FormatId == "skycd-vmdk");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyVmdkFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-vmdk",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesContainedFoldersAndFiles()
    {
        using var fixture = VmdkImageFixture.Create(("folder/notes.txt", "hello"));
        var service = CreateService();
        await using var source = File.OpenRead(fixture.ImagePath);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-vmdk",
            FileName = "disk.vmdk",
            SourcePath = fixture.ImagePath,
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row =>
            string.Equals(row["fullPath"] as string, "folder", StringComparison.OrdinalIgnoreCase)
            && Equals(row["kind"], "folder"));
        Assert.Contains(rows, row =>
            string.Equals(row["fullPath"] as string, "folder/notes.txt", StringComparison.OrdinalIgnoreCase)
            && Equals(row["kind"], "file")
            && Equals(row["sizeBytes"], "5"));
        Assert.Contains(rows, row => string.Equals(row["name"] as string, "notes.txt",
            StringComparison.OrdinalIgnoreCase));
        Assert.All(rows, row => Assert.True(row.ContainsKey("modifiedUtc")));
    }

    [Fact]
    public async Task ReadAsync_FromStream_ReadsMonolithicSparseImage()
    {
        using var fixture = VmdkImageFixture.Create(("notes.txt", "stream"));
        var service = CreateService();
        await using var source = File.OpenRead(fixture.ImagePath);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-vmdk",
            FileName = "disk.vmdk",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => string.Equals(row["fullPath"] as string, "notes.txt",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadAsync_DamagedImage_ReturnsFailure()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("not a vmdk image"));

        var result = await new VmdkDiskImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-vmdk",
            FileName = "broken.vmdk",
            Source = source
        });

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ReadAsync_DescriptorExtentOutsideImageDirectory_ReturnsFailure()
    {
        using var fixture = VmdkImageFixture.Create(DiscUtils.Vmdk.DiskCreateType.MonolithicFlat);
        var descriptor = await File.ReadAllTextAsync(fixture.ImagePath);
        var unsafeDescriptor = descriptor.Replace("disk-flat.vmdk", "../disk-flat.vmdk",
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(descriptor, unsafeDescriptor);
        await File.WriteAllTextAsync(fixture.ImagePath, unsafeDescriptor);
        await using var source = File.OpenRead(fixture.ImagePath);

        var result = await new VmdkDiskImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-vmdk",
            FileName = "disk.vmdk",
            SourcePath = fixture.ImagePath,
            Source = source
        });

        Assert.False(result.Success);
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new VmdkDiskImageIndexPlugin()]);
    }
}
