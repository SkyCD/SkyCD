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
using SkyCD.Presentation.ViewModels;
using Xunit;
using SkyCD.Plugin.FileFormat.Cathy.Models;

namespace SkyCD.Plugin.FileFormat.Cathy.Tests;

public class CathyPluginIntegrationTests
{
    [Fact]
    public async Task Plugin_ReadAsync_ReturnsValidCatalogDocument()
    {
        var plugin = new CathyPlugin();
        var bytes = CathyTestHelper.CreateSampleCaf(version: 8, volumeName: "CD_2026", comment: "Test Cathy");
        using var ms = new MemoryStream(bytes);

        var request = new SkyCD.Plugin.Abstractions.Capabilities.FileFormats.FileFormatReadRequest
        {
            FormatId = "cathy-caf",
            Source = ms,
            FileName = "cd_2026.caf"
        };

        var result = await plugin.ReadAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Payload);
        Assert.IsType<CathyCatalogDocument>(result.Payload);

        var doc = (CathyCatalogDocument)result.Payload;
        Assert.Equal("CD_2026", doc.Summary.VolumeName);
        Assert.Equal(4, doc.Count); // Root + Docs + Readme.txt + Manual.pdf
    }

    [Fact]
    public async Task FileFormatManager_LoadsAndPersistsCathyCatalog()
    {
        var plugin = new CathyPlugin();
        var manager = new FileFormatManager([plugin]);
        var loadService = new CatalogLoadService(manager);

        var tempCaf = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.caf");
        try
        {
            var bytes = CathyTestHelper.CreateSampleCaf(version: 8, volumeName: "Archive Disk 1", comment: "Cathy Test");
            await File.WriteAllBytesAsync(tempCaf, bytes);

            var count = await loadService.LoadAsync(tempCaf, (payload, _) =>
            {
                var documents = Extract(payload!);
                Assert.NotEmpty(documents);

                Persist(documents, vm =>
                {
                    Assert.NotEmpty(vm.TreeNodes);
                    var libraryNode = vm.TreeNodes.First();
                    Assert.Equal("library", libraryNode.Key);
                    Assert.NotEmpty(libraryNode.Children);
                    Assert.Contains(libraryNode.Children, child => child.Title == "Archive Disk 1");
                });

                return documents.Count;
            }, new Progress<int>());

            Assert.True(count > 0);
        }
        finally
        {
            if (File.Exists(tempCaf))
            {
                File.Delete(tempCaf);
            }
        }
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-cathy-" + Guid.NewGuid().ToString("N"));
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
