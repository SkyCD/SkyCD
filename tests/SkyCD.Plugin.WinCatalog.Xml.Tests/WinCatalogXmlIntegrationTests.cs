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
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.WinCatalog.Xml;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.WinCatalog.Xml.Tests;

public sealed class WinCatalogXmlIntegrationTests
{
    [Fact]
    public async Task LoadAsync_WithNestedMultiMediaFixture_PersistsAndReopensAccurately()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogXml", "wincatalog-nested-en.xml");
        var manager = new FileFormatManager([new SkyCD.Plugin.Xml.XmlCatalogPlugin(), new WinCatalogXmlPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var report = Assert.IsType<WinCatalogXmlReportDocument>(payload);
            Assert.False(report.Summary.IsPartialReport);

            var documents = Extract(payload!);
            Assert.Equal(10, documents.Count);

            Persist(documents, vm =>
            {
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("media:Archive Disk 1", rootKeys);
                Assert.Contains("media:Archive Disk 2", rootKeys);

                var disk1Node = vm.TreeNodes.First(node => node.Key == "media:Archive Disk 1");
                Assert.NotEmpty(disk1Node.Children);

                var readme = documents.Single(document => document.Name == "readme.txt");
                Assert.Equal("folder:Archive Disk 1/Documents", readme.ParentId);
                Assert.Equal(CatalogDocumentType.File, readme.Type);
                Assert.Equal(1024, readme.Size);
                Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", readme.Properties["MD5"]);

                var duplicateReadmes = documents.Where(document => document.Name == "readme.txt").ToList();
                Assert.Single(duplicateReadmes);
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    [Fact]
    public async Task LoadAsync_WithUnicodeFixture_PersistsCyrillicNames()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogXml", "wincatalog-nested-ru.xml");
        var manager = new FileFormatManager([new SkyCD.Plugin.Xml.XmlCatalogPlugin(), new WinCatalogXmlPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            Persist(documents, vm =>
            {
                Assert.Contains("media:Музыка", vm.TreeNodes.Select(node => node.Key));
                var mediaNode = vm.TreeNodes.Single(node => node.Key == "media:Музыка");
                Assert.NotEmpty(mediaNode.Children);

                var flac = documents.Single(document =>
                    document.Name == "Чайковский - Лебединое озеро.flac");
                Assert.Equal(13107200, flac.Size);
                Assert.Equal("2025-02-15 18:30:00", flac.Properties["DateModified"]);
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    [Fact]
    public async Task LoadAsync_WithFlatSearchResults_SynthesizesFoldersAndMarksPartial()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WinCatalogXml", "wincatalog-flat-search-results.xml");
        var manager = new FileFormatManager([new SkyCD.Plugin.Xml.XmlCatalogPlugin(), new WinCatalogXmlPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var report = Assert.IsType<WinCatalogXmlReportDocument>(payload);
            Assert.True(report.Summary.IsPartialReport);
            Assert.Equal("Flat", report.Summary.ImportMode);

            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            Persist(documents, vm =>
            {
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("media:Archive Disk 1", rootKeys);
                Assert.Contains("media:Archive Disk 2", rootKeys);

                // Intermediate folders were synthesized from paths.
                var documentsFolder = documents.SingleOrDefault(document =>
                    document.Name == "Documents" && document.Type == CatalogDocumentType.Folder);
                Assert.NotNull(documentsFolder);
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-wincatalog-xml-" + Guid.NewGuid().ToString("N"));
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
