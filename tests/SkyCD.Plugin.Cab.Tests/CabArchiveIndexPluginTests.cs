using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Cab;
using SkyCD.Plugin.Cab.Tests;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class CabArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeCab_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-cab" && format.Extensions.Contains(".cab"));
        Assert.Contains(openFormats, format => format.FormatId == "skycd-cab"
                                               && format.MimeTypes.Contains("application/vnd.ms-cab-compressed"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-cab");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyCabFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-cab",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ReadAsync_IndexesNestedPathsAndMetadata(int compressionType)
    {
        var service = CreateService();
        await using var archive = CabTestArchiveBuilder.Create(compressionType,
            new System.Collections.Generic.List<CabTestArchiveBuilder.Member>
            {
                new("root/notes.txt", "abc"u8.ToArray()),
                new("root/deep/leaf.txt", "hello"u8.ToArray())
            });

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-cab",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root"));
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root/deep"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "root/deep/leaf.txt")
            && Equals(row["sizeBytes"], "5"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/leaf.txt") && row.ContainsKey("modifiedUtc"));
    }

    [Fact]
    public async Task ReadAsync_PrefixedAttributeNames_AreListed()
    {
        await using var archive = CabTestArchiveBuilder.CreateStoredWithPrefixedNames(
            new System.Collections.Generic.List<CabTestArchiveBuilder.Member>
            {
                new("folder/item.txt", "x"u8.ToArray())
            });

        var result = await new CabArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-cab",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "folder/item.txt"));
    }

    [Fact]
    public async Task ReadAsync_CommittedFixture_ListsEntries()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cab", "catalog.cab");
        await using var stream = new FileStream(fixturePath, FileMode.Open);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-cab",
            Source = stream
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/readme.txt"));
        Assert.Contains(rows, row => Equals(row["kind"], "file") && Equals(row["fullPath"], "catalog/data/track.txt"));
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = CabTestArchiveBuilder.CreateDamaged();

        var result = await new CabArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-cab",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("CAB_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedCompression_ListsEntriesButExtractionSkipsThem()
    {
        var members = new System.Collections.Generic.List<CabTestArchiveBuilder.Member> { new("packed.bin", new byte[64]) };
        await using var archive = CabTestArchiveBuilder.Create(3, members);

        var result = await new CabArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-cab",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "packed.bin"));
        Assert.Equal("packed.bin", Assert.Single(rows, row => Equals(row["kind"], "file"))["name"]);
    }

    private static FileFormatManager CreateService() => new([new CabArchiveIndexPlugin()]);
}
