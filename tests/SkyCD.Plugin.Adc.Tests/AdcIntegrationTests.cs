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
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.Adc.Tests;

public sealed class AdcIntegrationTests
{
    [Fact]
    public void DiscoveredPluginFactory_ResolvesAdcPluginWithoutPluginJson()
    {
        var discovered = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AdcPlugin).Assembly);

        Assert.Equal("skycd.advanced-disk-catalog", discovered.Id);
        Assert.Contains("Advanced Disk Catalog", discovered.Name);
        var capability = Assert.Single(discovered.Capabilities.OfType<AdcPlugin>());
        Assert.Equal("advanced-disk-catalog", capability.SupportedFormat.FormatId);

        var assemblyDirectory = Path.GetDirectoryName(typeof(AdcPlugin).Assembly.Location)!;
        Assert.False(File.Exists(Path.Combine(assemblyDirectory, "plugin.json")));
    }

    [Fact]
    public async Task ReadAsync_PersistsAndReopensHierarchy()
    {
        var plugin = new AdcPlugin();
        using var stream = new MemoryStream(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog()), writable: false);
        var result = await plugin.ReadAsync(AdcFixtures.ReadRequest(stream, "nested.cat"));
        Assert.True(result.Success, result.Error);

        var documents = Extract(result.Payload!);
        Assert.Contains(documents, document => document.Name == "Empty" && document.Type == CatalogDocumentType.Folder);
        Assert.Contains(documents, document => document.Name == "main.cs" && document.Type == CatalogDocumentType.File);

        Persist(documents, vm =>
        {
            var media = Assert.Single(vm.TreeNodes, node => node.Key.StartsWith("media:", StringComparison.Ordinal));
            Assert.NotEmpty(media.Children);
        });
    }

    [Fact]
    public async Task ReadAsync_DuplicateBasenames_KeepDistinctParents()
    {
        var plugin = new AdcPlugin();
        using var stream = new MemoryStream(AdcFixtureWriter.Write(AdcFixtures.CommentsCatalog()), writable: false);
        var result = await plugin.ReadAsync(AdcFixtures.ReadRequest(stream, "comments.cat"));
        Assert.True(result.Success, result.Error);

        var documents = Extract(result.Payload!);
        var duplicates = documents.Where(document => document.Name == "setup.exe").ToList();
        Assert.Equal(2, duplicates.Count);
        Assert.Equal(2, duplicates.Select(document => document.ParentId).Distinct().Count());

        Persist(documents);
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod(
            "ExtractCatalogEntries",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-adc-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var database = new Database("catalog", new DatabaseConfiguration { Directory = directory }))
            {
                var collection = database.GetDefaultCollection();
                foreach (var entry in documents)
                {
                    using var document = entry.ToMutableDocument(entry.Id);
                    collection.Save(document);
                }
            }

            using var reopened = new Database("catalog", new DatabaseConfiguration { Directory = directory });
            var reopenedCollection = reopened.GetDefaultCollection();
            foreach (var entry in documents)
            {
                using var saved = reopenedCollection.GetDocument(entry.Id);
                var restored = saved.FromDocument<CatalogDocument>();
                Assert.NotNull(restored);
                Assert.Equal(entry.Name, restored.Name);
                Assert.Equal(entry.ParentId, restored.ParentId);
                Assert.Equal(entry.Size, restored.Size);
                Assert.Equal(entry.Type, restored.Type);
            }

            var repository = new CatalogDocumentRepository();
            repository.Initialize(typeof(CatalogDocument), reopenedCollection.Name, reopenedCollection);
            var constructor = typeof(MainWindowViewModel)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(candidate => candidate.GetParameters().Length == 5);
            using var vm = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
                [repository, new PropertyValueLocalizer(), new MenuExtensionManager([]), false, null]));

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
