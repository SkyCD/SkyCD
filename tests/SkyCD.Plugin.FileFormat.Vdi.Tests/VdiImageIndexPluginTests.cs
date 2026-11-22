using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Vdi.Tests;

public sealed class VdiImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeVdi_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new VdiImageIndexPlugin()]);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-vdi");
        Assert.Equal([".vdi"], format.Extensions);
        Assert.Contains("application/x-virtualbox-vdi", format.MimeTypes);
        Assert.Contains("application/x-vdi", format.MimeTypes);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-vdi");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyVdiFormat()
    {
        var service = new FileFormatManager([new VdiImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-vdi",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_ListsFoldersAndFiles(bool fixedImage)
    {
        var disk = VdiTestImage.CreateFatVolume(("DEEP/NOTES.TXT", "hello image"));
        var image = fixedImage ? VdiTestImage.BuildFixed(disk) : VdiTestImage.BuildDynamic(disk);

        var result = await ReadAsync(image, "disk.vdi");

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "DEEP"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "DEEP/NOTES.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("NOTES.TXT", file["name"]);
        Assert.Equal("11", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_DamagedHeader_ReportsError()
    {
        var bytes = VdiTestImage.BuildDynamic(VdiTestImage.CreateFatVolume(("notes.txt", "x")));
        // Declare a header size that exceeds the whole file.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x48, 4), 0x0000_0010);

        var result = await ReadAsync(bytes, "disk.vdi");

        Assert.False(result.Success);
        Assert.Contains("damaged", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_UnrecognizedSignature_ReportsError()
    {
        var result = await ReadAsync(Encoding.UTF8.GetBytes(new string('x', 4096)), "disk.vdi");

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ImageWithoutSupportedFileSystem_ReportsError()
    {
        var image = VdiTestImage.BuildDynamic(new byte[64 * 1024]);

        var result = await ReadAsync(image, "empty.vdi");

        Assert.False(result.Success);
        Assert.Contains("file system", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_SnapshotLayer_ReportsMissingParent()
    {
        var result = await ReadAsync(VdiTestImage.BuildWithHeader(imageFlags: 0x1), "snapshot.vdi");

        Assert.False(result.Success);
        Assert.Contains("parent", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_Cancellation_Propagates()
    {
        var disk = VdiTestImage.CreateFatVolume(("notes.txt", "content"));
        await using var source = new MemoryStream(VdiTestImage.BuildDynamic(disk));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VdiImageIndexPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-vdi",
                Source = source,
                FileName = "disk.vdi"
            }, cancellation.Token));
    }

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static async Task<FileFormatReadResult> ReadAsync(byte[] image, string fileName)
    {
        await using var source = new MemoryStream(image);
        return await new VdiImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-vdi",
            Source = source,
            FileName = fileName
        });
    }
}
