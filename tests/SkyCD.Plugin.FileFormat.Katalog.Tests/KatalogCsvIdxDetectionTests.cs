using System;
using System.IO;
using SkyCD.Plugin.FileFormat.Katalog.CsvIdx;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Katalog.Tests;

public sealed class KatalogCsvIdxDetectionTests
{
    private readonly KatalogCsvIdxPlugin plugin = new();

    [Fact]
    public void CanHandleFolder_WithValidCollectionFolder_ReturnsTrue()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection");
        Assert.True(plugin.CanHandleFolder(fixtureDir));
    }

    [Fact]
    public void CanHandleFolder_WithNonExistentFolder_ReturnsFalse()
    {
        Assert.False(plugin.CanHandleFolder("C:\\nonexistent\\directory\\path"));
    }

    [Fact]
    public void CanHandleFolder_WithEmptyDirectory_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        var temp = workspace.CreateDirectory("empty-katalog");

        Assert.False(plugin.CanHandleFolder(temp));
    }

    [Fact]
    public void CanHandleContent_WithKatalogIdxStream_ReturnsTrue()
    {
        var idxPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "Backup Disk 1.idx");
        using var stream = File.OpenRead(idxPath);
        Assert.True(plugin.CanHandleContent(stream, "Backup Disk 1.idx"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_WithUnrelatedStream_ReturnsFalse()
    {
        using var ms = new MemoryStream("Name,Size,Type\nItem,100,File\n"u8.ToArray());
        Assert.False(plugin.CanHandleContent(ms, "test.csv"));
        Assert.Equal(0, ms.Position);
    }
}
