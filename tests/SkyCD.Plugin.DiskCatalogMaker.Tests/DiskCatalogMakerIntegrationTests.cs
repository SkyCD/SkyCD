using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.App;
using SkyCD.App.Views;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.DiskExplorer.Delimited;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerIntegrationTests
{
    [Fact]
    public async Task LoadAsync_WithStandardFixture_PersistsAndReopensAccurately()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-standard-en.txt");
        var manager = new FileFormatManager([new DiskCatalogMakerPlugin(), new DiskExplorerDelimitedPlugin(), new CsvCatalogPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            // Verify persistence to database and reopen verification
            Persist(documents, vm =>
            {
                // Verify tree roots correspond to the 2 distinct disks
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("media:Archive Disk 1", rootKeys);
                Assert.Contains("media:Backup 2026", rootKeys);

                // Verify multiple media navigation
                var disk1Node = vm.TreeNodes.First(node => node.Key == "media:Archive Disk 1");
                Assert.NotEmpty(disk1Node.Children);

                var disk2Node = vm.TreeNodes.First(node => node.Key == "media:Backup 2026");
                Assert.NotEmpty(disk2Node.Children);

                // Verify distinct items
                var duplicateReadmes = documents.Where(d => d.Name == "readme.txt").ToList();
                Assert.Equal(2, duplicateReadmes.Count);
                Assert.Equal(2, duplicateReadmes.Select(d => d.ParentId).Distinct().Count());
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    [Fact]
    public async Task LoadAsync_GermanLocalizedExport_PersistsAndReopensAccurately()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-localized-de.txt");
        var manager = new FileFormatManager([new DiskCatalogMakerPlugin(), new DiskExplorerDelimitedPlugin(), new CsvCatalogPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var count = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            Persist(documents, vm =>
            {
                Assert.Contains("media:Sicherung 2026", vm.TreeNodes.Select(node => node.Key));
                var mediaNode = vm.TreeNodes.Single(node => node.Key == "media:Sicherung 2026");
                Assert.NotEmpty(mediaNode.Children);
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(count > 0);
    }

    [Theory]
    [InlineData("diskcatalogmaker-standard-en.txt", "résumé.pdf")]
    [InlineData("diskcatalogmaker-localized-de.txt", "grüße.txt")]
    public async Task LoadAsync_AccentedNames_PersistAndReopen(string fixture, string expectedName)
    {
        var manager = new FileFormatManager([new DiskCatalogMakerPlugin(), new DiskExplorerDelimitedPlugin(), new CsvCatalogPlugin()]);
        var service = new CatalogLoadService(manager);
        var count = await service.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", fixture), (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.Contains(documents, entry => entry.Name == expectedName);
            Persist(documents);
            return documents.Count;
        }, new Progress<int>());
        Assert.True(count > 0);
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-diskcatalogmaker-" + Guid.NewGuid().ToString("N"));
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
                foreach (var property in entry.Properties)
                {
                    Assert.Equal(property.Value?.ToString(), restored.Properties[property.Key]?.ToString());
                }
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
