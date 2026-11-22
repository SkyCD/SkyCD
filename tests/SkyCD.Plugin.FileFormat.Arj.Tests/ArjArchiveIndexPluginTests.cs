using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Arj;
using SkyCD.Plugin.FileFormat.Arj.Tests;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ArjArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeArj_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-arj" && format.Extensions.Contains(".arj"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-arj");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyArjFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-arj",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesNestedPathsAndMetadata()
    {
        var service = CreateService();
        await using var archive = ArjTestArchiveBuilder.CreateStored(
            ("root/notes.txt", "abc"),
            ("root/deep/leaf.txt", "hello"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-arj",
            Source = archive
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root"));
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root/deep"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "root/deep/leaf.txt")
            && Equals(row["sizeBytes"], "5"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/leaf.txt") && row.ContainsKey("modifiedUtc"));
    }

    [Fact]
    public async Task ReadAsync_CommittedFixture_ListsEntries()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Arj", "catalog.arj");
        await using var stream = new FileStream(fixturePath, FileMode.Open);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-arj",
            Source = stream
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/readme.txt"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/data/track.bin"));
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = ArjTestArchiveBuilder.CreateDamaged();

        var result = await new ArjArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-arj",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("ARJ_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_EncryptedArchive_ReportsEncryptionFailure()
    {
        await using var archive = ArjTestArchiveBuilder.CreateEncrypted(("secret.txt", "hidden"));

        var result = await new ArjArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-arj",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("ARJ_ENCRYPTED", result.Error);
    }

    private static FileFormatManager CreateService() => new([new ArjArchiveIndexPlugin()]);
}
