using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Qcow2.Tests;

public sealed class Qcow2ImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeQcow2_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new Qcow2ImageIndexPlugin()]);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats,
            format => format.FormatId == "skycd-qcow2" && format.Extensions.Contains(".qcow2"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-qcow2");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyQcow2Format()
    {
        var service = new FileFormatManager([new Qcow2ImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-qcow2",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsFoldersAndFiles()
    {
        var disk = Qcow2TestImage.CreateFatDisk(("DEEP/NOTES.TXT", "hello qcow2"));
        var result = await ReadAsync(Qcow2TestImage.Build(disk));

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "DEEP"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("NOTES.TXT", file["name"]);
        Assert.Equal("11", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_CompressedClusters_AreDecompressed()
    {
        var disk = Qcow2TestImage.CreateFatDisk(("DEEP/NOTES.TXT", "compressed content"));
        var result = await ReadAsync(Qcow2TestImage.Build(disk, compress: true));

        Assert.True(result.Success, result.Error);
        var file = Assert.Single(Rows(result), row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("18", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_WholeDiskFileSystem_IsReadable()
    {
        var volume = Qcow2TestImage.CreateFatVolume(("notes.txt", "floppy"));
        var result = await ReadAsync(Qcow2TestImage.Build(volume));

        Assert.True(result.Success, result.Error);
        var file = Assert.Single(Rows(result), row => Equals(row["fullPath"], "NOTES.TXT"));
        Assert.Equal("6", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_EncryptedImage_ReportsError()
    {
        var disk = Qcow2TestImage.CreateFatDisk(("notes.txt", "secret"));

        var result = await ReadAsync(Qcow2TestImage.Build(disk, cryptMethod: 1));

        Assert.False(result.Success);
        Assert.Contains("encrypted", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0b100UL, 0u)]
    [InlineData(0b1_0000UL, 0u)]
    [InlineData(0b1000UL, 1u)]
    public async Task ReadAsync_UnsupportedFeature_ReportsError(ulong incompatibleFeatures, uint compressionType)
    {
        var disk = Qcow2TestImage.CreateFatDisk(("notes.txt", "content"));
        var image = Qcow2TestImage.Build(disk, incompatibleFeatures: incompatibleFeatures,
            compressionType: compressionType);

        var result = await ReadAsync(image);

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_DamagedImage_ReportsError()
    {
        var result = await ReadAsync([0x51, 0x46, 0x49, 0xfb, 0, 0, 0]);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ReadAsync_ImageWithoutSupportedFileSystem_ReportsError()
    {
        var image = Qcow2TestImage.Build(new byte[16 * Qcow2TestImage.ClusterSize]);

        var result = await ReadAsync(image);

        Assert.False(result.Success);
        Assert.Contains("file system", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_Cancellation_Propagates()
    {
        var disk = Qcow2TestImage.CreateFatDisk(("notes.txt", "content"));
        var image = Qcow2TestImage.Build(disk);
        await using var source = new MemoryStream(image);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new Qcow2ImageIndexPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-qcow2",
                Source = source,
                FileName = "disk.qcow2"
            }, cancellation.Token));
    }

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static async Task<FileFormatReadResult> ReadAsync(byte[] image)
    {
        await using var source = new MemoryStream(image);
        return await new Qcow2ImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-qcow2",
            Source = source,
            FileName = "disk.qcow2"
        });
    }
}
