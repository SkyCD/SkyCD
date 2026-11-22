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
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Tests;

public sealed class SmartCdCatalogProIntegrationTests
{
    [Fact]
    public async Task ReadAsync_PersistsAndReopensAccurately()
    {
        var plugin = new SmartCdCatalogProPlugin();
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);
        var documents = Extract(result.Payload!);
        Assert.NotEmpty(documents);

        Persist(documents, vm =>
        {
            Assert.Contains(vm.TreeNodes.Select(node => node.Key), key => key.StartsWith("media:scdc:", StringComparison.Ordinal));
            var mediaNode = vm.TreeNodes.First(node => node.Key == "media:scdc:1");
            Assert.NotEmpty(mediaNode.Children);
        });
    }

    [Fact]
    public async Task ReadAsync_DuplicateNamesInDifferentFolders_KeepDistinctParents()
    {
        var plugin = new SmartCdCatalogProPlugin();
        using var stream = await SmartCdCatalogProFixture.CreateCatalogAsync();
        var result = await plugin.ReadAsync(SmartCdCatalogProFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);
        var documents = Extract(result.Payload!);

        var sameName = documents.Where(document => document.Name == "deep.bin").ToList();
        Assert.Single(sameName);
        Assert.NotNull(sameName[0].ParentId);

        Persist(documents);
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-smartcdc-" + Guid.NewGuid().ToString("N"));
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
