using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.App.Views;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogImportTests
{
    [Theory]
    [InlineData("binary-sample.scd")]
    [InlineData("text-sample.scd")]
    public async Task Import_PersistsLegacyTrees(string file)
    {
        await using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = source });
        Assert.True(result.Success, result.Error);
        var documents = Extract(result.Payload!);
        Assert.Equal(5, documents.Count);
        Assert.Equal(CatalogDocumentType.Media, documents.Single(entry => entry.Name == "[Disk]").Type);
        Assert.Equal(CatalogDocumentType.Folder, documents.Single(entry => entry.Name == "Empty").Type);
        var folder = documents.Single(entry => entry.Name == "Folder [2023]");
        var leaf = documents.Single(entry => entry.Name == "Readme.txt");
        Assert.Equal(folder.Id, leaf.ParentId);
        Assert.Equal(1536L, leaf.Size);
        Assert.Equal(CatalogDocumentType.File, leaf.Type);
        Persist(documents);
    }

    [Fact]
    public void Import_PersistsLongUnicodePathsAndUrlsWithStableDistinctIds()
    {
        var segment = new string('字', 300);
        var catalog = new LegacyScdCatalog();
        catalog.Entries.Add(new LegacyScdEntry { Path = $"[Disk]\\{segment}\\{segment}\\File.txt", SizeBytes = 123 });
        catalog.Entries.Add(new LegacyScdEntry { Path = $"[Disk]\\{segment}\\{segment}\\Other.txt", SizeBytes = 456 });
        catalog.Entries.Add(new LegacyScdEntry { Path = "https://example.com/" + segment, SizeBytes = 789 });
        var documents = Extract(catalog);
        Assert.Equal(documents.Count, documents.Select(entry => entry.Id).Distinct().Count());
        Assert.All(documents, entry => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(entry.Id), 1, 240));
        Assert.Equal(documents.Select(entry => entry.Id), Extract(catalog).Select(entry => entry.Id));
        Assert.All(documents.Where(entry => entry.ParentId is not null), entry => Assert.Contains(documents, parent => parent.Id == entry.ParentId));
        Persist(documents);
    }

    [Fact]
    public void Import_RejectsUnsupportedPayloadAndAcceptsEmptyCatalog()
    {
        var error = Assert.Throws<TargetInvocationException>(() => Extract(new object()));
        Assert.IsAssignableFrom<InvalidOperationException>(error.InnerException);
        Assert.Contains("unsupported catalog payload", error.InnerException!.Message);
        Assert.Equal("library", Assert.Single(Extract(new LegacyScdCatalog())).Id);
    }

    [Fact]
    public void Import_PreservesJsonNodeHierarchy()
    {
        using var json = System.Text.Json.JsonDocument.Parse("""
            {"nodes":[
              {"nodeId":"root","parentId":null,"kind":"folder","name":"Root","sizeBytes":0},
              {"nodeId":"child","parentId":"root","kind":"file","name":"File.txt","sizeBytes":42}
            ]}
            """);
        var entries = Extract(json.RootElement);
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].ParentId);
        Assert.Equal(entries[0].Id, entries[1].ParentId);
        Assert.Equal(CatalogDocumentType.File, entries[1].Type);
        Assert.Equal(42L, entries[1].Size);
        Persist(entries);
    }

    internal static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    internal static void Persist(IReadOnlyList<CatalogDocument> documents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var database = new Database("catalog", new DatabaseConfiguration { Directory = directory });
            var collection = database.GetDefaultCollection();
            foreach (var entry in documents)
            {
                using var document = entry.ToMutableDocument(entry.Id);
                collection.Save(document);
            }
            foreach (var entry in documents)
            {
                using var saved = collection.GetDocument(entry.Id);
                var restored = saved.FromDocument<CatalogDocument>();
                Assert.NotNull(restored);
                Assert.Equal(entry.Name, restored.Name);
                Assert.Equal(entry.ParentId, restored.ParentId);
                Assert.Equal(entry.Size, restored.Size);
                Assert.Equal(entry.Type, restored.Type);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
