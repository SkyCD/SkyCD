using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro;
using Xunit;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Models;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Tests;

public sealed class SmartCdCatalogProReaderTests
{
    private readonly SmartCdCatalogProPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_MapsMediaRootsAndHierarchy()
    {
        var document = await ImportAsync();

        var roots = document.Rows.Where(row => Kind(row) == "media").ToList();
        Assert.Equal(2, roots.Count);
        Assert.Equal("MUSIC_01", Name(roots.Single(row => NodeId(row) == "media:scdc:1")));
        Assert.Equal("\\\\server\\share\\", Name(roots.Single(row => NodeId(row) == "media:scdc:2")));

        Assert.Equal("folder", Kind(Node(document, "folder:scdc:1:1000")));
        Assert.Equal("media:scdc:1", ParentId(Node(document, "folder:scdc:1:1000")));

        var sub = Node(document, "folder:scdc:1:1007");
        Assert.Equal("Sub", Name(sub));
        Assert.Equal("folder:scdc:1:1000", ParentId(sub));

        var deep = Node(document, "file:scdc:1:1008");
        Assert.Equal("deep.bin", Name(deep));
        Assert.Equal("folder:scdc:1:1007", ParentId(deep));
        Assert.Equal("42", Size(deep));

        Assert.Equal("folder:scdc:2:1100", ParentId(Node(document, "file:scdc:2:1101")));
        Assert.Equal("folder:scdc:2:1100", ParentId(Node(document, "file:scdc:2:1102")));

        Assert.Equal(2, document.Summary.MediaRootCount);
        Assert.Equal(3, document.Summary.FolderCount);
        Assert.Equal(9, document.Summary.FileCount);
    }

    [Fact]
    public async Task ReadAsync_PreservesNamesSizesAndMetadata()
    {
        var document = await ImportAsync();

        var audio = Node(document, "file:scdc:1:1001");
        Assert.Equal("söng ☃.mp3", Name(audio));
        Assert.Equal("9876543210", Size(audio));
        Assert.True(Prop<bool>(audio, "IsAudio"));
        Assert.Equal((short)3, Prop<short>(audio, "TrackNumber"));
        Assert.Equal("00:03:45", Prop<string>(audio, "Duration"));
        Assert.Equal("mp3", Prop<string>(audio, "Extension"));
        Assert.False(Prop<bool>(audio, "ReadOnly"));
        Assert.True(Prop<bool>(audio, "Compressed"));
        Assert.Equal("audio comment", Prop<string>(audio, "Comments"));
        Assert.StartsWith("2021-01-01T01:01:01", Prop<string>(audio, "DateCreated"));
        Assert.Equal("E:\\Albums\\söng ☃.mp3", Prop<string>(audio, "SourcePath"));

        Assert.Equal("0", Size(Node(document, "file:scdc:1:1002")));
        Assert.Equal("1099511627776", Size(Node(document, "file:scdc:1:1006")));

        var unicode = Node(document, "file:scdc:2:1102");
        Assert.Equal("crème brûlée.txt", Name(unicode));
    }

    [Fact]
    public async Task ReadAsync_MapsRenameState()
    {
        var document = await ImportAsync();
        var renamed = Node(document, "file:scdc:1:1003");

        Assert.Equal("actual.log", Name(renamed));
        Assert.Equal("renamed.log", Prop<string>(renamed, "ElementName"));
        Assert.Equal("actual.log", Prop<string>(renamed, "RealName"));
        Assert.True(Prop<bool>(renamed, "IsRenamed"));
        Assert.Equal("E:\\Albums\\actual.log", Prop<string>(renamed, "SourcePath"));
    }

    [Fact]
    public async Task ReadAsync_MapsArchiveMetadata()
    {
        var document = await ImportAsync();
        var archived = Node(document, "file:scdc:1:1005");

        Assert.True(Prop<bool>(archived, "IsArchive"));
        Assert.Equal(456L, Prop<long>(archived, "UnpackedSize"));
        Assert.Equal(2, Prop<int>(archived, "ScannedArchives"));
        Assert.True(Prop<bool>(archived, "IsAudio"));
    }

    [Fact]
    public async Task ReadAsync_MapsClassificationsBorrowerAndLoanHistory()
    {
        var document = await ImportAsync();
        var firstRoot = Node(document, "media:scdc:1");

        Assert.Equal("Music / Rock", Prop<string>(firstRoot, "Category"));
        Assert.Equal(2, Prop<int>(firstRoot, "CategoryId"));
        Assert.Equal("Shelf A", Prop<string>(firstRoot, "Box"));
        Assert.Equal(1, Prop<int>(firstRoot, "BoxId"));
        Assert.Equal("Alice", Prop<string>(firstRoot, "Borrower"));
        Assert.Equal(1, Prop<int>(firstRoot, "BorrowerId"));
        Assert.True(Prop<bool>(firstRoot, "IsLent"));
        Assert.Contains("lent", Prop<string>(firstRoot, "LoanHistory"), StringComparison.Ordinal);

        var secondRoot = Node(document, "media:scdc:2");
        Assert.False(Properties(secondRoot).ContainsKey("Borrower"));
        Assert.Equal(1, Prop<int>(secondRoot, "LoanHistoryCount"));
        Assert.Contains(
            "2020-01-01 -> 2020-02-01: lent: old loan",
            Prop<string>(secondRoot, "LoanHistory"),
            StringComparison.Ordinal);

        Assert.True(Prop<bool>(firstRoot, "AccessModeDefaultIsFull"));
        Assert.True(Prop<bool>(firstRoot, "AccessModeFullPasswordSet"));
        Assert.True(Prop<bool>(firstRoot, "AccessModeReadOnlyPasswordSet"));

        // Application passwords and borrower contact data are never imported.
        var importedStrings = document.Rows.SelectMany(row => Properties(row).Values).OfType<string>().ToList();
        Assert.DoesNotContain(importedStrings, value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(importedStrings, value => value.Contains("alice@example.test", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(importedStrings, value => value.Contains("555-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_UsesIdHierarchyInsteadOfCachedCatpath()
    {
        var document = await ImportAsync();
        var root = Node(document, "media:scdc:1");

        Assert.Equal("Music / Rock", Prop<string>(root, "Category"));
        Assert.DoesNotContain(
            Properties(root).Values.OfType<string>(),
            value => value.Contains("WRONG", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_WithMissingCoreTable_ReturnsSchemaError()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync(
            SmartCdCatalogProFixture.Password,
            async writer =>
            {
                await writer.CreateTableAsync("CDs", SmartCdCatalogProFixture.CdsColumns(true));
                await writer.CreateTableAsync("Paths", SmartCdCatalogProFixture.PathsColumns(true));
            });

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("schema", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Elements", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_WithMissingCoreColumn_ReturnsSchemaError()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync(
            SmartCdCatalogProFixture.Password,
            async writer =>
            {
                await writer.CreateTableAsync("CDs", SmartCdCatalogProFixture.CdsColumns(true));
                await writer.CreateTableAsync("Paths", SmartCdCatalogProFixture.PathsColumns(true));
                await writer.CreateTableAsync(
                    "Elements",
                    SmartCdCatalogProFixture.ElementsColumns(true)
                        .Where(column => !column.Name.Equals("ElementName", StringComparison.OrdinalIgnoreCase))
                        .ToList());
            });

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("ElementName", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_WithWrongPassword_ReturnsPasswordError()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync("another-password", async writer =>
        {
            await SmartCdCatalogProFixture.CreateStandardSchemaAsync(writer);
            await SmartCdCatalogProFixture.SeedStandardDataAsync(writer);
        });

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("password", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WithDanglingPath_RejectsCatalog()
    {
        using var stream = await CreateWithAdditionalElementAsync(
            ("ID", 2000),
            ("IDPATH", 999),
            ("ElementName", "dangling.txt"),
            ("IsFolder", false), ("IsRenamed", false), ("ReadOnly", false), ("Archive", false),
            ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false));

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("missing path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WithDuplicateIds_RejectsCatalog()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync(
            SmartCdCatalogProFixture.Password,
            async writer =>
            {
                await SmartCdCatalogProFixture.CreateStandardSchemaAsync(writer, usePrimaryKeys: false);
                await SmartCdCatalogProFixture.SeedStandardDataAsync(writer);
                await writer.InsertRowAsync("CDs", SmartCdCatalogProFixture.Row(
                    ("IDCD", 1),
                    ("ReadOnly", false),
                    ("Archive", false),
                    ("Hidden", false),
                    ("System", false),
                    ("Compressed", false),
                    ("IsLend", false),
                    ("IsFolder", false),
                    ("IsAudio", false)));
            });

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("duplicate", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WithHierarchyCycle_RejectsCatalog()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync(
            SmartCdCatalogProFixture.Password,
            async writer =>
            {
                await SmartCdCatalogProFixture.CreateStandardSchemaAsync(writer);
                await SmartCdCatalogProFixture.SeedStandardDataAsync(writer);
                await writer.InsertRowAsync("Paths", SmartCdCatalogProFixture.Row(
                    ("IDPATH", 120), ("IDCD", 1), ("Path", "E:\\Cycle\\"), ("IsArchive", false)));
                await writer.InsertRowAsync("Elements", SmartCdCatalogProFixture.Row(
                    ("ID", 2000), ("IDPATH", 120), ("IDPATHSELF", 120), ("ElementName", "Loop"),
                    ("IsFolder", true), ("IsRenamed", false), ("ReadOnly", false), ("Archive", false),
                    ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false)));
            });

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("cycle", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WithInvalidSizes_OmitsSizesAndReportsDiagnostics()
    {
        using var stream = await SmartCdCatalogProFixture.CreateAsync(
            SmartCdCatalogProFixture.Password,
            async writer =>
            {
                await SmartCdCatalogProFixture.CreateStandardSchemaAsync(writer);
                await SmartCdCatalogProFixture.SeedStandardDataAsync(writer);
                await InsertSizedFileAsync(writer, 3000, -5d);
                await InsertSizedFileAsync(writer, 3001, 1.5d);
                await InsertSizedFileAsync(writer, 3002, double.MaxValue);
            });

        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<SmartCdCatalogProReportDocument>(result.Payload);
        foreach (var id in new[] { "file:scdc:1:3000", "file:scdc:1:3001", "file:scdc:1:3002" })
        {
            Assert.Equal("0", Size(Node(document, id)));
        }

        Assert.True(document.Summary.Diagnostics.Count >= 3);
    }

    [Fact]
    public async Task ReadAsync_WithCancellation_Throws()
    {
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream), cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_DoesNotModifySourceOrCreateLockFile()
    {
        using var catalog = await SmartCdCatalogProFixture.CreateCatalogAsync();
        var path = Path.Combine(Path.GetTempPath(), $"scdc-source-{Guid.NewGuid():N}.scdc");
        try
        {
            await File.WriteAllBytesAsync(path, catalog.ToArray());
            var before = await File.ReadAllBytesAsync(path);
            using (var source = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(source, Path.GetFileName(path)));
                Assert.True(result.Success, result.Error);
            }

            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(Path.ChangeExtension(path, ".ldb")));
            Assert.False(File.Exists(path + ".jdw-journal"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private async Task<SmartCdCatalogProReportDocument> ImportAsync()
    {
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);
        return Assert.IsType<SmartCdCatalogProReportDocument>(result.Payload);
    }

    private static async Task<MemoryStream> CreateWithAdditionalElementAsync(params (string Column, object? Value)[] values)
    {
        return await SmartCdCatalogProFixture.CreateAsync(
            SmartCdCatalogProFixture.Password,
            async writer =>
            {
                await SmartCdCatalogProFixture.CreateStandardSchemaAsync(writer);
                await SmartCdCatalogProFixture.SeedStandardDataAsync(writer);
                await writer.InsertRowAsync("Elements", SmartCdCatalogProFixture.Row(values));
            });
    }

    private static async Task InsertSizedFileAsync(JetDatabaseWriter.AccessWriter writer, int id, double size)
    {
        await writer.InsertRowAsync("Elements", SmartCdCatalogProFixture.Row(
            ("ID", id), ("IDPATH", 101), ("ElementName", $"bad-{id}.bin"), ("ElementSize", size),
            ("IsFolder", false), ("IsRenamed", false), ("ReadOnly", false), ("Archive", false),
            ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false)));
    }

    private static Dictionary<string, object?> Node(SmartCdCatalogProReportDocument document, string nodeId) =>
        document.Rows.Single(row => NodeId(row) == nodeId);

    private static string NodeId(IReadOnlyDictionary<string, object?> row) => (string)row["nodeId"]!;

    private static string Name(IReadOnlyDictionary<string, object?> row) => (string)row["name"]!;

    private static string Kind(IReadOnlyDictionary<string, object?> row) => (string)row["kind"]!;

    private static string? ParentId(IReadOnlyDictionary<string, object?> row) => row["parentId"] as string;

    private static string Size(IReadOnlyDictionary<string, object?> row) => (string)row["sizeBytes"]!;

    private static Dictionary<string, object?> Properties(IReadOnlyDictionary<string, object?> row) =>
        (Dictionary<string, object?>)row["properties"]!;

    private static T Prop<T>(IReadOnlyDictionary<string, object?> row, string key)
    {
        var properties = Properties(row);
        Assert.True(properties.TryGetValue(key, out var value), $"Expected property '{key}'.");
        return Assert.IsType<T>(value);
    }
}
