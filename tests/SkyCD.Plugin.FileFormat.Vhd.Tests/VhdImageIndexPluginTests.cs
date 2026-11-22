using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Vhd.Tests;

public sealed class VhdImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeVhdAndVhdx_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new VhdImageIndexPlugin()]);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-vhd");
        Assert.Equal([".vhd", ".vhdx"], format.Extensions);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-vhd");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyVhdFormat()
    {
        var service = new FileFormatManager([new VhdImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-vhd",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("vhd")]
    [InlineData("vhdx")]
    public async Task ReadAsync_ListsFoldersAndFiles(string extension)
    {
        var disk = VhdTestImage.CreateFatVolume(("DEEP/NOTES.TXT", "hello image"));
        var result = await ReadAsync(Build(extension, disk), $"disk.{extension}");

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "DEEP"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("NOTES.TXT", file["name"]);
        Assert.Equal("11", file["sizeBytes"]);
    }

    [Theory]
    [InlineData("vhd")]
    [InlineData("vhdx")]
    public async Task ReadAsync_DamagedImage_ReportsError(string extension)
    {
        var result = await ReadAsync([0x63, 0x6F, 0x6E, 0x65, 0x63, 0x74, 0x69, 0x78], $"disk.{extension}");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("vhd")]
    [InlineData("vhdx")]
    public async Task ReadAsync_ImageWithoutSupportedFileSystem_ReportsError(string extension)
    {
        var result = await ReadAsync(Build(extension, new byte[64 * 1024]), $"empty.{extension}");

        Assert.False(result.Success);
        Assert.Contains("file system", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_UnrecognizedSignature_ReportsError()
    {
        var result = await ReadAsync(System.Text.Encoding.UTF8.GetBytes("not a disk image"), "disk.vhd");

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("vhd")]
    [InlineData("vhdx")]
    public async Task ReadAsync_Cancellation_Propagates(string extension)
    {
        var disk = VhdTestImage.CreateFatVolume(("notes.txt", "content"));
        await using var source = new MemoryStream(Build(extension, disk));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VhdImageIndexPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-vhd",
                Source = source,
                FileName = $"disk.{extension}"
            }, cancellation.Token));
    }

    private static byte[] Build(string extension, byte[] diskContent) =>
        extension == "vhdx" ? VhdTestImage.BuildVhdx(diskContent) : VhdTestImage.BuildVhd(diskContent);

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static async Task<FileFormatReadResult> ReadAsync(byte[] image, string fileName)
    {
        await using var source = new MemoryStream(image);
        return await new VhdImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-vhd",
            Source = source,
            FileName = fileName
        });
    }
}
