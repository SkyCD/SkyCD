using System;
using System.IO;
using Xunit;

namespace SkyCD.Plugin.Katalog.CsvIdx.Tests;

public sealed class KatalogDetectionTests
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
        var temp = Path.Combine(Path.GetTempPath(), "empty-katalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Assert.False(plugin.CanHandleFolder(temp));
        }
        finally
        {
            Directory.Delete(temp);
        }
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
