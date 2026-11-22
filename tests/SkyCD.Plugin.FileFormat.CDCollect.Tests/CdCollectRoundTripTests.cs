using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.CDCollect.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.CDCollect.Models;
using SkyCD.Plugin.FileFormat.CDCollect.Parsing;

namespace SkyCD.Plugin.FileFormat.CDCollect.Tests;

public sealed class CdCollectRoundTripTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "cds3.db");

    [Fact]
    public async Task Version4Fixture_ImportsMetadataAndThumbnail_AndExportsCompatibleSchema()
    {
        var plugin = new CdCollectPlugin();
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.True(plugin.SupportedFormat.CanWrite);
        using var source = File.OpenRead(Fixture);
        var read = await plugin.ReadAsync(new FileFormatReadRequest
        { Source = source, SourcePath = Fixture, FormatId = "cdcollect-db" });
        Assert.True(read.Success, read.Error);
        var original = Assert.IsType<CdCollectReportDocument>(read.Payload);
        Assert.Equal(2, original.Summary.MediaCount);
        Assert.Equal(1, original.Summary.InfoPluginsCount);
        Assert.Equal(1, original.Summary.CompPluginsCount);
        var cover = original.Single(r => r["Name"]?.ToString() == "cover.png");
        var props = Assert.IsType<Dictionary<string, object?>>(cover["Properties"]);
        Assert.Equal("  original information  ", props["Info"]);
        Assert.StartsWith("data:image/png;base64,", props["Thumbnail"]?.ToString());
        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        { Target = target, Payload = original, FormatId = "cdcollect-db" });
        Assert.True(write.Success, write.Error);
        Assert.Equal("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(target.ToArray(), 0, 16));
        target.Position = 0;
        var restored = CdCollectParser.Parse(target);
        Assert.Equal(original.Count, restored.Count);
        foreach (var row in original)
        {
            var actual = restored.Single(r => Equals(r["Name"], row["Name"]));
            Assert.Equal(row["Type"], actual["Type"]);
            Assert.Equal(row["Size"], actual["Size"]);
            var parentName = original.FirstOrDefault(r => Equals(r["Id"], row["ParentId"]))?["Name"];
            var actualParentName = restored.FirstOrDefault(r => Equals(r["Id"], actual["ParentId"]))?["Name"];
            Assert.Equal(parentName, actualParentName);
            var expectedProps = (Dictionary<string, object?>)row["Properties"]!;
            var actualProps = (Dictionary<string, object?>)actual["Properties"]!;
            foreach (var key in new[] { "Info", "Keywords", "Date", "Icon", "MimeType", "Location", "IsPacked" })
                if (expectedProps.TryGetValue(key, out var value)) Assert.Equal(value, actualProps[key]);
        }
        Assert.Equal(0, restored.Summary.InfoPluginsCount);
        Assert.Equal(0, restored.Summary.CompPluginsCount);
    }

    [Fact]
    public async Task Export_AcceptsDomainDocuments_AndPreservesHierarchy()
    {
        var documents = new[]
        {
            new CatalogDocument { Id = "media", Name = "Disc", Type = CatalogDocumentType.Media },
            new CatalogDocument { Id = "folder", ParentId = "media", Name = "Folder", Type = CatalogDocumentType.Folder },
            new CatalogDocument { Id = "file", ParentId = "folder", Name = "O'Brien.txt", Size = 123 }
        };
        using var target = new MemoryStream();
        var result = await new CdCollectPlugin().WriteAsync(new FileFormatWriteRequest
        { Target = target, Payload = documents, FormatId = "cdcollect-db" });
        Assert.True(result.Success, result.Error);
        target.Position = 0;
        var restored = CdCollectParser.Parse(target);
        var file = restored.Single(r => Equals(r["Name"], "O'Brien.txt"));
        Assert.Equal(123L, file["Size"]);
        Assert.Equal("Folder", restored.Single(r => Equals(r["Id"], file["ParentId"]))["Name"]);
    }

    [Theory]
    [InlineData("UPDATE cdcollect_version SET ver=99", typeof(CdCollectUnsupportedVersionException))]
    [InlineData("UPDATE cdcollect_version SET ver='broken'", typeof(CdCollectCorruptDatabaseException))]
    [InlineData("DELETE FROM cdcollect_version", typeof(CdCollectCorruptDatabaseException))]
    [InlineData("INSERT INTO cdcollect_version VALUES(4)", typeof(CdCollectCorruptDatabaseException))]
    [InlineData("DROP TABLE files_v1", typeof(CdCollectInvalidFormatException))]
    [InlineData("UPDATE files_v1 SET path='missing:0' WHERE path='1:0:0'", typeof(CdCollectCorruptDatabaseException))]
    [InlineData("UPDATE files_v1 SET type=9", typeof(CdCollectCorruptDatabaseException))]
    public void Import_RejectsUnknownOrMalformedSchemaAndHierarchy(string sql, Type exceptionType)
    {
        WithDatabase(sql, path =>
        {
            using var source = File.OpenRead(path);
            Assert.Throws(exceptionType, () => CdCollectParser.Parse(source));
        });
    }

    [Fact]
    public void Detection_RequiresSchemaEvenWithCdCollectExtension_AndPreservesPosition()
    {
        WithDatabase("DROP TABLE files_v1", path =>
        {
            using var source = File.OpenRead(path);
            source.Position = 8;
            Assert.False(new CdCollectPlugin().CanHandleContent(source, "catalog.cdcollect"));
            Assert.Equal(8, source.Position);
        });
    }

    [Fact]
    public void Import_ExecutableConfigurationIsInert()
    {
        var sentinel = Path.Combine(Path.GetTempPath(), "cdcollect_sentinel_" + Guid.NewGuid().ToString("N"));
        WithDatabase("UPDATE infoplugins SET program='cmd.exe', arguments='/c echo executed > " + sentinel + "'", path =>
        {
            using var source = File.OpenRead(path);
            var report = CdCollectParser.Parse(source);
            Assert.Equal(1, report.Summary.InfoPluginsCount);
            Assert.False(File.Exists(sentinel));
        });
    }

    [Fact]
    public async Task Export_RejectsUnrepresentableSize_WithoutWritingTarget()
    {
        var rows = new[]
        {
            new CatalogDocument { Id = "m", Name = "Disc", Type = CatalogDocumentType.Media },
            new CatalogDocument { Id = "f", ParentId = "m", Name = "large", Size = (long)int.MaxValue + 1 }
        };
        using var target = new MemoryStream();
        var result = await new CdCollectPlugin().WriteAsync(new FileFormatWriteRequest
        { Target = target, Payload = rows, FormatId = "cdcollect-db" });
        Assert.False(result.Success);
        Assert.Contains("32-bit", result.Error);
        Assert.Equal(0, target.Length);
    }

    [Fact]
    public async Task Export_RespectsCancellation()
    {
        using var target = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CdCollectPlugin().WriteAsync(
            new FileFormatWriteRequest { Target = target, Payload = Array.Empty<object>(), FormatId = "cdcollect-db" },
            new CancellationToken(true)));
    }

    private static void WithDatabase(string sql, Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cdcollect_test_{Guid.NewGuid():N}.db");
        File.Copy(Fixture, path);
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            test(path);
        }
        finally { File.Delete(path); }
    }
}
