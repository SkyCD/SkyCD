using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.BrokenX.Models;

namespace SkyCD.Plugin.BrokenX.Tests;

public sealed class BxdReaderTests
{
    private readonly BxdPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_WithCanonicalEmpty_ReturnsEmptyMedia()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.CanonicalEmpty());
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<BxdReportDocument>(result.Payload);
        Assert.Single(document.Rows);
        var media = document.Rows[0];
        Assert.Equal("media", media["kind"]?.ToString());
        Assert.Equal(0, document.Summary.ImportedRecordsCount);
        Assert.Equal("0x40", document.Summary.DetectedVersion);
    }

    [Fact]
    public async Task ReadAsync_WithOneItem_PreservesFields()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.OneItemDatabase()));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<BxdReportDocument>(result.Payload);
        var file = document.Rows.First(r => r["name"]?.ToString() == "readme.txt");
        Assert.Equal("file", file["kind"]?.ToString());
        var props = Assert.IsType<Dictionary<string, object?>>(file["properties"]);
        Assert.Equal("7", props["BxdField14"]?.ToString());
        Assert.Equal("9", props["BxdField18"]?.ToString());
        Assert.Equal("0", props["BxdBaseFlags"]?.ToString());
    }

    [Fact]
    public async Task ReadAsync_WithSiblingsAndNesting_BuildsTree()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.SiblingDatabase()));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<BxdReportDocument>(result.Payload);
        var rows = document.Rows.ToList();
        Assert.Contains(rows, r => r["name"]?.ToString() == "Docs" && r["kind"]?.ToString() == "folder");
        Assert.Contains(rows, r => r["name"]?.ToString() == "loose.txt");
        var docs = rows.First(r => r["name"]?.ToString() == "Docs");
        Assert.Equal(2, rows.Count(r => r["parentId"]?.ToString() == docs["nodeId"]?.ToString()));
    }

    [Fact]
    public async Task ReadAsync_WithUnicode_PreservesNames()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.UnicodeDatabase()));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<BxdReportDocument>(result.Payload);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "crème brûlée.txt");
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "日本語.txt");
    }

    [Fact]
    public async Task ReadAsync_WithUnknownProperties_ReportsAndPreserves()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.UnknownPropertyDatabase()));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<BxdReportDocument>(result.Payload);
        Assert.Contains("0x45", document.Summary.UnsupportedFields);
        Assert.Contains("0x99", document.Summary.UnsupportedFields);
        Assert.Contains("0xDA", document.Summary.UnsupportedFields);
        var file = document.Rows.First(r => r["name"]?.ToString() == "mystery.bin");
        var props = Assert.IsType<Dictionary<string, object?>>(file["properties"]);
        Assert.Equal("57005", props["BxdField14"]?.ToString());
    }

    [Fact]
    public async Task ReadAsync_WithEncryptedRecord_ReturnsActionableError()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.EncryptedDatabase()));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("password-protected", result.Error);
        Assert.Contains("secret.txt", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithBadSignature_ReturnsCorruptError()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Mutate(BxdFixture.CanonicalEmpty(), b => b[0] = 0x00));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("signature", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithUnsupportedVersion_ReturnsVersionError()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Mutate(BxdFixture.CanonicalEmpty(), b => b[21] = 0x41));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("0x41", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithTruncatedInput_ReturnsCorruptError()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.CanonicalEmpty()[..30]);
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ReadAsync_WithInvalidDescriptor_ReturnsCorruptError()
    {
        var bytes = BxdFixture.Serialize(BxdFixture.OneItemDatabase());
        // Root property block: count at 22, first descriptor at 23. Clear kind bits -> 0x00.
        using var stream = BxdFixture.StreamOf(BxdFixture.Mutate(bytes, b => b[23] = 0x02));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("descriptor", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithLyingItemCount_ReturnsCorruptError()
    {
        var bytes = BxdFixture.Serialize(BxdFixture.OneItemDatabase());
        // Item count sits right after the 16-byte root property block (offset 38).
        using var stream = BxdFixture.StreamOf(BxdFixture.Mutate(bytes, b => b[38] = 0xFF));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ReadAsync_WithTrailingGarbage_ReturnsCorruptError()
    {
        var bytes = new List<byte>(BxdFixture.Serialize(BxdFixture.OneItemDatabase())) { 0x00 }.ToArray();
        using var stream = BxdFixture.StreamOf(bytes);
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("trailing", result.Error);
    }

    [Fact]
    public async Task ReadAsync_WithExcessiveDepth_ReturnsCorruptError()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.DeepDatabase(70)));
        var result = await plugin.ReadAsync(BxdFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("depth", result.Error);
    }

    [Fact]
    public async Task ReadAsync_SupportsCancellation()
    {
        using var stream = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.SiblingDatabase()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            plugin.ReadAsync(BxdFixture.ReadRequest(stream), cts.Token));
    }
}
