using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Katalog.CsvIdx;
using SkyCD.Plugin.Katalog.Sqlite.Exceptions;
using Xunit;

namespace SkyCD.Plugin.Katalog.Sqlite.Tests;

public sealed class KatalogSqliteReaderTests : IDisposable
{
    private readonly KatalogFixtureBuilder fixtures = new();
    private readonly KatalogSqlitePlugin plugin = new();

    public void Dispose() => fixtures.Dispose();

    private async Task<KatalogCsvIdxReportDocument> ReadAsync(string path, string? sourcePath = null)
    {
        await using var stream = File.OpenRead(path);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "katalog-sqlite",
            FileName = Path.GetFileName(path),
            SourcePath = sourcePath ?? path
        });
        Assert.True(result.Success, result.Error);
        return Assert.IsType<KatalogCsvIdxReportDocument>(result.Payload);
    }

    private static Dictionary<string, object?> RowByName(KatalogCsvIdxReportDocument document, string name) =>
        document.Rows.Single(row => string.Equals((string)row["name"]!, name, StringComparison.Ordinal));

    private static Dictionary<string, object?> PropertiesOf(Dictionary<string, object?> row) =>
        Assert.IsType<Dictionary<string, object?>>(row["properties"]);

    [Fact]
    public async Task FullCollection_BuildsDevicesCatalogsFilesAndFolders()
    {
        var document = await ReadAsync(fixtures.CreateFullCollection());

        Assert.Equal("2.10", document.Summary.AppVersion);
        Assert.Equal(3, document.Summary.TotalDevices);
        Assert.Equal(2, document.Summary.TotalCatalogs);
        Assert.Equal(3, document.Summary.TotalFiles);

        // Catalog 10 owned by device 2 (PhotoDVD); catalog 11 standalone via file_catalog.
        var photoMedia = RowByName(document, "PhotoDVD");
        Assert.Equal("media", photoMedia["kind"]);
        var musicMedia = RowByName(document, "MusicFiles");
        Assert.Equal("media", musicMedia["kind"]);

        var photo = RowByName(document, "img.jpg");
        Assert.Equal("file", photo["kind"]);
        Assert.Equal(photoMedia["nodeId"], photo["parentId"]);
        var photoProps = PropertiesOf(photo);
        Assert.Equal("abc123", photoProps["SHA256"]);
        Assert.Equal("image/jpeg", photoProps["MimeType"]);
        Assert.Equal("vacation", photoProps["Tags"]);
        Assert.Equal("800x600", photoProps["Dimensions"]);

        var song = RowByName(document, "song.mp3");
        var songProps = PropertiesOf(song);
        Assert.Equal("def456", songProps["SHA256"]);
        Assert.Equal("rock", songProps["Tags"]);
        Assert.Equal("Artist", songProps["Artist"]);
        Assert.Equal("180.5", songProps["AudioDuration"]);
    }

    [Fact]
    public async Task FullCollection_DeviceHierarchyAndStorageMetadata()
    {
        var document = await ReadAsync(fixtures.CreateFullCollection());

        var group = RowByName(document, "Archive Group");
        Assert.Equal("folder", group["kind"]);
        Assert.Null(group["parentId"]);

        var catalog = RowByName(document, "PhotoDVD");
        Assert.Equal(group["nodeId"], catalog["parentId"]);

        var storage = RowByName(document, "MusicStick");
        Assert.Null(storage["parentId"]);
        var props = PropertiesOf(storage);
        Assert.Equal("PHOTOS_2024", props["VolumeLabel"]);
        Assert.Equal("Brand", props["Brand"]);
        Assert.Equal("SN123", props["SerialNumber"]);
    }

    [Fact]
    public async Task MinimalSchemaVariation_ImportsWithoutOptionalColumns()
    {
        var document = await ReadAsync(fixtures.CreateMinimalCollection());

        Assert.Equal("2.8", document.Summary.AppVersion);
        var file = Assert.Single(document.Rows, row => string.Equals((string)row["kind"]!, "file", StringComparison.Ordinal));
        Assert.Equal("readme.txt", (string)file["name"]!);
        Assert.Equal(42L, Convert.ToInt64(file["sizeBytes"]));
        Assert.DoesNotContain(document.Rows, row => ((string)row["name"]!).Contains("storage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingRequiredTable_ThrowsUnsupportedSchema()
    {
        var path = fixtures.CreateNonKatalogDatabase();
        await using var stream = File.OpenRead(path);
        var exception = await Assert.ThrowsAsync<KatalogSqliteUnsupportedSchemaException>(async () =>
        {
            await KatalogSqliteReader.ReadAsync(stream, path);
        });

        Assert.Contains("required table", exception.Message);
    }

    [Fact]
    public async Task OldSchemaVersion_ReturnsFailure()
    {
        var path = fixtures.CreateMinimalCollection("old.db", version: "2.5");
        await using var stream = File.OpenRead(path);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "katalog-sqlite",
            FileName = "old.db",
            SourcePath = path
        });

        Assert.False(result.Success);
        Assert.Contains("2.5", result.Error);
    }

    [Fact]
    public async Task EmptyCatalogs_ReturnsFailure()
    {
        var path = fixtures.CreateEmptyCatalogs();
        await using var stream = File.OpenRead(path);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "katalog-sqlite",
            FileName = "empty.db",
            SourcePath = path
        });

        Assert.False(result.Success);
        Assert.Contains("no catalogs", result.Error);
    }

    [Fact]
    public async Task GarbageBytes_ReturnsFailure()
    {
        var path = Path.Combine(fixtures.DirectoryPath, "garbage.db");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
        await using var stream = File.OpenRead(path);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "katalog-sqlite",
            FileName = "garbage.db",
            SourcePath = path
        });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var path = fixtures.CreateFullCollection("cancel.db");
        await using var stream = File.OpenRead(path);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await KatalogSqliteReader.ReadAsync(stream, path, progress: null, cancellationToken: source.Token);
        });
    }

    [Fact]
    public async Task ImportUnderActiveWalWriter_SeesConsistentSnapshot()
    {
        var path = fixtures.CreateFullCollection("wal.db");
        using var writer = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=false");
        writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "BEGIN IMMEDIATE";
            command.ExecuteNonQuery();
        }

        using (var command = writer.CreateCommand())
        {
            command.CommandText = "INSERT INTO device (device_id, device_parent_id, device_name, device_type, device_external_id, device_path) VALUES (99, 0, 'Uncommitted', 'Group', 0, '')";
            command.ExecuteNonQuery();
        }

        try
        {
            // The OS file share this stack grants SQLite handles requires
            // read/write sharing; plain File.OpenRead is denied while any
            // connection holds the database, so Katalog must be closed first.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var result = await plugin.ReadAsync(new FileFormatReadRequest
            {
                Source = stream,
                FormatId = "katalog-sqlite",
                FileName = "wal.db",
                SourcePath = path
            });
            Assert.True(result.Success, result.Error);
            var document = Assert.IsType<KatalogCsvIdxReportDocument>(result.Payload);
            Assert.Equal(3, document.Summary.TotalDevices);
        }
        finally
        {
            using var rollback = writer.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            rollback.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task RepeatedImports_ProduceStableNodeIds()
    {
        var path = fixtures.CreateFullCollection("stable.db");
        var first = await ReadAsync(path);
        var second = await ReadAsync(path);

        var firstIds = first.Rows.Select(row => (string)row["nodeId"]!).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var secondIds = second.Rows.Select(row => (string)row["nodeId"]!).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task SourceDatabase_IsNeverModified()
    {
        var path = fixtures.CreateFullCollection("readonly.db");
        var before = File.GetLastWriteTimeUtc(path);
        await ReadAsync(path);

        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(path + "-shm"));
    }
}
