using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.App;
using SkyCD.App.Views;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Access;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using UCanAccess.File;
using Xunit;
using Database = Couchbase.Lite.Database;

namespace SkyCD.Plugin.FileFormat.MyCatalogue.Tests;

public sealed class MyCatalogueIntegrationTests
{
    [Fact]
    public async Task HostOpen_ImportsHierarchyAndMetadataWithoutChangingSource()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("catalog");
        try
        {
            var path = Path.Combine(directory, "catalog.mdb");
            MyCatalogueFixture.Create(path);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var plugin = new MyCataloguePlugin();
            var manager = new FileFormatManager([new AccessListExportPlugin(), plugin]);
            var discovered = new DiscoveredPluginFactory().BuildFromAssembly(typeof(MyCataloguePlugin).Assembly);
            Assert.Contains(discovered.Capabilities, capability => capability is MyCataloguePlugin);
            Assert.Contains(manager.GetOpenFormats(), format => format.FormatId == "mycatalogue-mdb" && format.CanRead && !format.CanWrite);
            Assert.DoesNotContain(manager.GetSaveFormats(), format => format.FormatId == "mycatalogue-mdb");

            using (var source = File.OpenRead(path))
            {
                source.Position = 7;
                Assert.True(plugin.CanHandleContent(source, path));
                Assert.Equal(7, source.Position);
                Assert.IsType<MyCataloguePlugin>(manager.GetInstanceFor(path, source));
            }

            var documents = await new CatalogLoadService(manager).LoadAsync(path, (payload, _) => Extract(payload!),
                new Progress<int>());
            Assert.Equal(11, documents.Count);
            var disk = documents.Single(item => item.Id == "media:mycatalogue:1");
            Assert.Equal("Disc α", disk.Name);
            Assert.Equal(CatalogDocumentType.Media, disk.Type);
            Assert.Equal("group:mycatalogue:2", disk.ParentId);
            Assert.Equal("SN-01", disk.Properties["SerialNumber"]);
            Assert.Equal("Žanrai", disk.Properties["MyCatalogueGroupName"]);
            var group = documents.Single(item => item.Id == "group:mycatalogue:2");
            Assert.Equal("group:mycatalogue:1", group.ParentId);
            Assert.Equal(CatalogDocumentType.Folder, group.Type);
            Assert.Equal(true, group.Properties["MyCatalogueGroup"]);
            Assert.Contains(documents, item => item.Id == "group:mycatalogue:3" && item.Name == "Empty group");

            var first = documents.Single(item => item.Id == "item:mycatalogue:2");
            var second = documents.Single(item => item.Id == "item:mycatalogue:4");
            Assert.Equal("same ü.txt", first.Name);
            Assert.Equal(first.Name, second.Name);
            Assert.Equal("item:mycatalogue:1", first.ParentId);
            Assert.Equal("item:mycatalogue:3", second.ParentId);
            Assert.Equal(9876543210L, first.Size);
            Assert.Equal("80000000", first.Properties["CRC32"]);
            Assert.False(second.Properties.ContainsKey("CRC32"));
            Assert.Equal(false, second.Properties["SizeKnown"]);
            Assert.False(second.HasKnownSize);
            Assert.Equal(string.Empty, second.DisplaySize);
            Assert.True(first.HasKnownSize);
            Assert.Equal("2021-02-03T04:05:06.0000000", first.Properties["DateCreated"]);

            var index = new CatalogSearchIndex();
            index.Rebuild(documents);
            var missing = SearchSize(index, AdvancedCatalogOperator.IsMissing);
            Assert.Contains(missing, match => match.Document.Id == second.Id);
            Assert.DoesNotContain(missing, match => match.Document.Id == "item:mycatalogue:6");
            var zero = SearchSize(index, AdvancedCatalogOperator.Equals, 0L);
            Assert.DoesNotContain(zero, match => match.Document.Id == second.Id);
            Assert.Contains(zero, match => match.Document.Id == "item:mycatalogue:6");
            Assert.Equal(CatalogDocumentType.Folder, documents.Single(item => item.Name == "Empty").Type);
            Assert.Equal("media:mycatalogue:2", documents.Single(item => item.Name == "root.bin").ParentId);
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
            Assert.Single(Directory.GetFiles(directory));
            VerifyPersistence(workspace, documents);
        }
        finally { workspace.Dispose(); }
    }

    [Fact]
    public async Task ReadAsync_PreservesGroupsEvenWithoutDisks()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("catalog");
        try
        {
            var path = Path.Combine(directory, "catalog.mdb");
            MyCatalogueFixture.Create(path, "groups-only");
            await using var source = File.OpenRead(path);
            var result = await new MyCataloguePlugin().ReadAsync(Request(source));
            Assert.True(result.Success, result.Error);
            var documents = Extract(result.Payload!);
            Assert.Equal(3, documents.Count);
            Assert.All(documents, document => Assert.Equal(CatalogDocumentType.Folder, document.Type));
            Assert.Equal("group:mycatalogue:1", documents.Single(document => document.Id == "group:mycatalogue:2").ParentId);
            Assert.Contains(documents, document => document.Name == "Empty group");
        }
        finally { workspace.Dispose(); }
    }

    [Theory]
    [InlineData("group-cycle", "cycle")]
    [InlineData("item-cycle", "cycle")]
    [InlineData("missing-group", "missing group")]
    [InlineData("missing-parent", "missing parent")]
    [InlineData("file-parent", "not a directory")]
    [InlineData("cross-disk", "same disk")]
    [InlineData("negative-size", "Items.Bytes")]
    [InlineData("duplicate-id", "duplicate item")]
    [InlineData("null-id", "Items.ID_Item")]
    [InlineData("null-parent", "Items.Item_ID")]
    [InlineData("missing-crc", "Items.CRC32")]
    [InlineData("missing-column", "Groups.GroupName")]
    [InlineData("version", "version 1.2")]
    public async Task ReadAsync_RejectsInvalidRelationshipsAndValues(string variant, string message)
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("catalog");
        try
        {
            var path = Path.Combine(directory, "catalog.mdb");
            MyCatalogueFixture.Create(path, variant);
            await using var source = File.OpenRead(path);
            var result = await new MyCataloguePlugin().ReadAsync(Request(source));
            Assert.False(result.Success);
            Assert.Contains(message, result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { workspace.Dispose(); }
    }

    [Fact]
    public async Task DetectionAndRead_RejectTruncatedUnrelatedAndUnsupportedCatalogs()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("catalog");
        try
        {
            var path = Path.Combine(directory, "catalog.mdb");
            MyCatalogueFixture.Create(path);
            var bytes = await File.ReadAllBytesAsync(path);
            var unrelated = Path.Combine(directory, "unrelated.mdb");
            using (var database = UCanAccess.File.Database.Create(unrelated, version: "2003"))
                database.CreateTable("ItemList", [new ColumnBuilder("Name", DataType.Text).WithLength(100)]);
            var plugin = new MyCataloguePlugin();
            foreach (var invalid in new[] { bytes[..128], "not a database"u8.ToArray(), await File.ReadAllBytesAsync(unrelated) })
            {
                using var stream = new MemoryStream(invalid);
                Assert.False(plugin.CanHandleContent(stream));
                var result = await plugin.ReadAsync(Request(stream));
                Assert.False(result.Success);
                Assert.Contains("MyCatalogue", result.Error, StringComparison.OrdinalIgnoreCase);
            }
            var unsupported = Path.Combine(directory, "unsupported.mdb");
            MyCatalogueFixture.Create(unsupported, "version");
            await using var unsupportedStream = File.OpenRead(unsupported);
            Assert.False(plugin.CanHandleContent(unsupportedStream));
        }
        finally { workspace.Dispose(); }
    }

    [Fact]
    public async Task ReadAsync_RejectsDatabaseOverSizeLimit()
    {
        using var source = new OversizedStream();
        var plugin = new MyCataloguePlugin();
        Assert.False(plugin.CanHandleContent(source));
        var result = await plugin.ReadAsync(Request(source));
        Assert.False(result.Success);
        Assert.Contains("2 GiB", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_CancellationDuringImportIsPropagated()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("catalog");
        try
        {
            var path = Path.Combine(directory, "catalog.mdb");
            MyCatalogueFixture.Create(path);
            using var cancellation = new CancellationTokenSource();
            await using var source = File.OpenRead(path);
            var request = new FileFormatReadRequest
            {
                FormatId = "mycatalogue-mdb",
                Source = source,
                Progress = new CancelProgress(cancellation)
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MyCataloguePlugin().ReadAsync(request, cancellation.Token));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { workspace.Dispose(); }
    }

    [Fact]
    public async Task WriteAsync_IsReadOnly()
    {
        using var target = new MemoryStream();
        var result = await new MyCataloguePlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "mycatalogue-mdb",
            Target = target,
            Payload = new object()
        });
        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyPersistence(TestWorkspace workspace, IReadOnlyList<CatalogDocument> documents)
    {
        var directory = workspace.CreateDirectory("persistence");
        using var database = new Database("catalog", new DatabaseConfiguration { Directory = directory });
        var collection = database.GetDefaultCollection();
        foreach (var entry in documents)
        {
            using var document = entry.ToMutableDocument(entry.Id);
            collection.Save(document);
        }
        database.Close();

        using var reopened = new Database("catalog", new DatabaseConfiguration { Directory = directory });
        collection = reopened.GetDefaultCollection();
        foreach (var entry in documents)
        {
            using var saved = collection.GetDocument(entry.Id);
            var restored = saved.FromDocument<CatalogDocument>();
            Assert.NotNull(restored);
            Assert.Equal(entry.Name, restored.Name);
            Assert.Equal(entry.ParentId, restored.ParentId);
            Assert.Equal(entry.Type, restored.Type);
            Assert.Equal(entry.Size, restored.Size);
            Assert.Equal(entry.HasKnownSize, restored.HasKnownSize);
        }
        using var missingSize = collection.GetDocument("item:mycatalogue:4");
        Assert.Equal(string.Empty, missingSize.FromDocument<CatalogDocument>()!.DisplaySize);
        using var checkedFile = collection.GetDocument("item:mycatalogue:2");
        var restoredFile = checkedFile.FromDocument<CatalogDocument>()!;
        Assert.Equal("80000000", restoredFile.Properties["CRC32"]);
        Assert.Equal("2021-02-03T04:05:06.0000000", restoredFile.Properties["DateCreated"]);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
        var constructor = typeof(MainWindowViewModel).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate => candidate.GetParameters().Length == 5);
        using var vm = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
            [repository, new PropertyValueLocalizer(), new MenuExtensionManager([]), false, null]));
        var group = vm.TreeNodes.Single(node => node.Key == "group:mycatalogue:1");
        var nested = group.Children.Single(node => node.Key == "group:mycatalogue:2");
        Assert.Contains(nested.Children, node => node.Key == "media:mycatalogue:1");
        Assert.Contains(vm.TreeNodes, node => node.Key == "group:mycatalogue:3");
    }

    private static IReadOnlyList<CatalogFindResult> SearchSize(CatalogSearchIndex index, AdvancedCatalogOperator comparison, object? value = null) =>
        AdvancedCatalogSearchService.Search(index, new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates = [new AdvancedCatalogPredicate { Field = AdvancedCatalogField.SizeBytes, Operator = comparison, Value = value }]
            }
        });

    private static FileFormatReadRequest Request(Stream source) => new()
    {
        FormatId = "mycatalogue-mdb",
        FileName = "catalog.mdb",
        Source = source
    };

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private sealed class OversizedStream : MemoryStream
    {
        public override long Length => 2L * 1024 * 1024 * 1024 + 1;
    }

    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<int>
    {
        public void Report(int value)
        {
            if (value >= 25) source.Cancel();
        }
    }
}
