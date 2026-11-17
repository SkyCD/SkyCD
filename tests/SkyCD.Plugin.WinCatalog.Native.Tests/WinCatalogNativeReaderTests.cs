using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Native.Tests;

public sealed class WinCatalogNativeReaderTests
{
    private readonly WinCatalogNativePlugin plugin = new();

    [Fact]
    public async Task ReadAsync_WithStandardCatalog_BuildsHierarchy()
    {
        using var stream = WinCatalogNativeFixture.CreateCatalog();
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<WinCatalogNativeReportDocument>(result.Payload);
        Assert.True(document.Count >= 5);

        var rows = document.Rows.ToList();
        Assert.Contains(rows, r => r["kind"]?.ToString() == "media");
        Assert.Contains(rows, r => r["name"]?.ToString() == "readme.txt");
        Assert.Contains(rows, r => r["name"]?.ToString() == "crème brûlée.txt");
        Assert.Equal(4, document.Summary.ImportedRecordsCount);
        Assert.Contains("catalog_disks", document.Summary.DetectedTables);
        Assert.Contains("catalog_entries", document.Summary.DetectedTables);
    }

    [Fact]
    public async Task ReadAsync_WithNestedCatalog_PreservesDepth()
    {
        using var stream = WinCatalogNativeFixture.CreateNestedCatalog();
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream, "nested.w3cat"));
        Assert.True(result.Success);
        var document = Assert.IsType<WinCatalogNativeReportDocument>(result.Payload);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "deep.txt");
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "sib1.txt");
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "sib2.txt");
    }

    [Fact]
    public async Task ReadAsync_WithEmptyCatalog_ReturnsEmptyError()
    {
        using var stream = WinCatalogNativeFixture.CreateEmptyCatalog();
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("no importable entries", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithTruncatedInput_ReturnsCorruptError()
    {
        using var stream = new MemoryStream(WinCatalogNativeFixture.TruncatedHeader(), writable: false);
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ReadAsync_SupportsCancellation()
    {
        using var stream = WinCatalogNativeFixture.CreateCatalog();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream), cts.Token));
    }

    [Fact]
    public async Task ReadAsync_WithMalformedTable_SkipsWithoutDataLoss()
    {
        using var stream = WinCatalogNativeFixture.CreateCatalog(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO catalog_entries (entry_name, full_path, entry_type, file_size) VALUES
                  (NULL, 'Media/bad', 'file', 10),
                  ('good.txt', 'Media/good.txt', 'file', 10);
                """;
            command.ExecuteNonQuery();
        });
        var result = await plugin.ReadAsync(WinCatalogNativeFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<WinCatalogNativeReportDocument>(result.Payload);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "good.txt");
    }
}
