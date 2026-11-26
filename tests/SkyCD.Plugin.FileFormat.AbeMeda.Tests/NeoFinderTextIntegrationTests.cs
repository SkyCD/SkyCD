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
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.FileFormat.AbeMeda.Text;
using SkyCD.Plugin.FileFormat.AbeMeda.Text.Models;
using SkyCD.TestSupport;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

public sealed class NeoFinderTextIntegrationTests
{
    [Fact]
    public async Task ReadAsync_PersistsAndReopensAccurately()
    {
        var plugin = new NeoFinderTextPlugin();
        using var stream = NeoFinderTextFixture.CreateStandardExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var documents = Extract(result.Payload!);
        Assert.NotEmpty(documents);

        Persist(documents, vm =>
        {
            Assert.Contains(vm.TreeNodes.Select(node => node.Key), key => key.StartsWith("media:", StringComparison.Ordinal));
            var mediaNode = vm.TreeNodes.First(node => node.Key.StartsWith("media:", StringComparison.Ordinal));
            Assert.NotEmpty(mediaNode.Children);
        });
    }

    [Fact]
    public async Task ReadAsync_DuplicateNamesInDifferentFolders_KeepDistinctParents()
    {
        var plugin = new NeoFinderTextPlugin();
        using var stream = NeoFinderTextFixture.CreateDuplicateNameExport();
        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var documents = Extract(result.Payload!);
        var dupes = documents.Where(d => d.Name == "readme.txt").ToList();
        Assert.Equal(2, dupes.Count);
        Assert.Equal(2, dupes.Select(d => d.ParentId).Distinct().Count());
        Persist(documents);
    }

    [Theory]
    [MemberData(nameof(LegacyEncodingSamples.Cases), MemberType = typeof(LegacyEncodingSamples))]
    public async Task ReadAsync_DetectsAndPreservesLegacyEncodings(
        string encodingName,
        bool includeByteOrderMark,
        string expectedComment)
    {
        var report = $"Name\tKind\tSize\tWhere\tVolume\tComments\r\nfile.txt\tPlain Text\t1\t/Media/file.txt\tMedia\t{expectedComment}\r\n";
        using var stream = new MemoryStream(LegacyEncodingSamples.Encode(encodingName, includeByteOrderMark, report));
        var plugin = new NeoFinderTextPlugin();

        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<NeoFinderTextReportDocument>(result.Payload);
        var file = Assert.Single(document.Rows, row => Equals(row["name"], "file.txt"));
        var properties = Assert.IsType<Dictionary<string, object?>>(file["properties"]);
        Assert.Equal(expectedComment, properties["Comments"]);
    }

    [Fact]
    public async Task ReadAsync_EmptyInput_ReportsActionableEncodingError()
    {
        var plugin = new NeoFinderTextPlugin();
        using var stream = new MemoryStream();

        var result = await plugin.ReadAsync(NeoFinderTextFixture.ReadRequest(stream));

        Assert.False(result.Success);
        Assert.Contains("UTF-8", result.Error);
        Assert.Contains("not decoded with a lossy fallback", result.Error);
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-neofinder-text-" + Guid.NewGuid().ToString("N"));
        try
        {
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
                Assert.Equal(entry.Size, restored.Size);
                Assert.Equal(entry.Type, restored.Type);
            }

            var repository = new CatalogDocumentRepository();
            repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
            var constructor = typeof(MainWindowViewModel).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(candidate => candidate.GetParameters().Length == 5);
            using var vm = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
                [repository, new PropertyValueLocalizer(), new MenuExtensionManager([]), false, null]));

            if (documents.Any(entry => entry.ParentId is null && entry.Type != CatalogDocumentType.File))
            {
                Assert.NotEmpty(vm.TreeNodes);
            }

            verify?.Invoke(vm);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
