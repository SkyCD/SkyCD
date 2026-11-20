using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Dmg.Tests;

public sealed class DmgImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeDmg_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new DmgImageIndexPlugin()]);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats,
            format => format.FormatId == "skycd-dmg" && format.Extensions.Contains(".dmg"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-dmg");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyDmgFormat()
    {
        var service = new FileFormatManager([new DmgImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-dmg",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsFoldersAndFiles()
    {
        var disk = DmgTestImage.CreateFatDisk(("DEEP/NOTES.TXT", "hello dmg"), ("README.TXT", "readme"));
        var result = await ReadAsync(DmgTestImage.Build(disk));

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "DEEP"));
        var notes = Assert.Single(rows, row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("file", notes["kind"]);
        Assert.Equal("NOTES.TXT", notes["name"]);
        Assert.Equal("9", notes["sizeBytes"]);
        Assert.Single(rows, row => Equals(row["fullPath"], "README.TXT"));
    }

    [Fact]
    public async Task ReadAsync_CompressedBlocks_AreDecompressed()
    {
        var disk = DmgTestImage.CreateFatDisk(("DEEP/NOTES.TXT", "compressed content"));
        var result = await ReadAsync(DmgTestImage.Build(disk, compress: true));

        Assert.True(result.Success, result.Error);
        var file = Assert.Single(Rows(result), row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("18", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_WholeDiskFileSystem_IsReadable()
    {
        var volume = DmgTestImage.CreateFatVolume(("notes.txt", "floppy"));
        var result = await ReadAsync(DmgTestImage.Build(volume, compress: true));

        Assert.True(result.Success, result.Error);
        var file = Assert.Single(Rows(result), row => Equals(row["fullPath"], "NOTES.TXT"));
        Assert.Equal("6", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_ZeroBlocks_DoNotHideStoredEntries()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "payload"));
        var result = await ReadAsync(DmgTestImage.Build(disk, compress: true, trailingZeroSectors: 64));

        Assert.True(result.Success, result.Error);
        Assert.Single(Rows(result), row => Equals(row["fullPath"], "NOTES.TXT"));
    }

    [Fact]
    public async Task ReadAsync_EncryptedImage_ReportsError()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "secret"));

        var result = await ReadAsync(DmgTestImage.Build(disk, encrypted: true));

        Assert.False(result.Success);
        Assert.Contains("encrypted", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_SegmentedImage_ReportsError()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "segment"));

        var result = await ReadAsync(DmgTestImage.Build(disk, segmentNumber: 2, segmentCount: 3));

        Assert.False(result.Success);
        Assert.Contains("segment 2 of 3", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_CorruptedTrailerSignature_ReportsDamage()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "content"));

        var result = await ReadAsync(DmgTestImage.Build(disk, corruptSignature: true));

        Assert.False(result.Success);
        Assert.Contains("damaged", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_MissingPropertyList_ReportsDamage()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "content"));

        var result = await ReadAsync(DmgTestImage.Build(disk, omitPropertyList: true));

        Assert.False(result.Success);
        Assert.Contains("damaged", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ShortFile_ReportsError()
    {
        var result = await ReadAsync([0x6B, 0x6F, 0x6C, 0x79, 0, 0, 0]);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ReadAsync_ImageWithoutSupportedFileSystem_ReportsError()
    {
        var result = await ReadAsync(DmgTestImage.Build(new byte[64 * 512]));

        Assert.False(result.Success);
        Assert.Contains("file system", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_Cancellation_Propagates()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "content"));
        var image = DmgTestImage.Build(disk);
        await using var source = new MemoryStream(image);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DmgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-dmg",
                Source = source,
                FileName = "disk.dmg"
            }, cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_SampleFixture_IsReadable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog-sample.dmg");

        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await new DmgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-dmg",
            Source = source,
            SourcePath = path,
            FileName = "catalog-sample.dmg"
        });

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["fullPath"], "DEEP"));
        var notes = Assert.Single(rows, row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("9", notes["sizeBytes"]);
        Assert.Single(rows, row => Equals(row["fullPath"], "README.TXT"));
    }

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static async Task<FileFormatReadResult> ReadAsync(byte[] image)
    {
        await using var source = new MemoryStream(image);
        return await new DmgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-dmg",
            Source = source,
            FileName = "disk.dmg"
        });
    }
}
