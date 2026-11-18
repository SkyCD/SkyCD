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
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.AbeMeda.Native.Tests;

public sealed class AbeMedaNativeIntegrationTests
{
    [Fact]
    public async Task ReadAsync_PersistsAndReopensAccurately()
    {
        var plugin = new AbeMedaNativePlugin();
        using var stream = AbeMedaNativeFixture.CreateCatalog();
        var result = await plugin.ReadAsync(AbeMedaNativeFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);

        var documents = Extract(result.Payload!);
        Assert.NotEmpty(documents);

        Persist(documents, viewModel =>
        {
            Assert.Contains(viewModel.TreeNodes.Select(node => node.Key),
                key => key.StartsWith("media:", StringComparison.Ordinal));
            var mediaNode = viewModel.TreeNodes.First(node => node.Key.StartsWith("media:", StringComparison.Ordinal));
            Assert.NotEmpty(mediaNode.Children);
        });
    }

    [Fact]
    public async Task ReadAsync_ArtifactFolderKeepsDistinctParents()
    {
        var plugin = new AbeMedaNativePlugin();
        using var stream = AbeMedaNativeFixture.CreateCatalog();
        var result = await plugin.ReadAsync(AbeMedaNativeFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);

        var documents = Extract(result.Payload!);
        var media = Assert.Single(documents, document => document.Type == CatalogDocumentType.Media);
        var folder = Assert.Single(documents, document => document.Type == CatalogDocumentType.Folder);
        var artifacts = documents.Where(document => document.Type == CatalogDocumentType.File).ToList();

        Assert.Equal(3, artifacts.Count);
        Assert.Equal(media.Id, folder.ParentId);
        Assert.All(artifacts, artifact => Assert.Equal(folder.Id, artifact.ParentId));
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-abemeda-native-" + Guid.NewGuid().ToString("N"));
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
                using var viewModel = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
                    [repository, new PropertyValueLocalizer(), new MenuExtensionManager([]), false, null]));

                if (documents.Any(entry => entry.ParentId is null && entry.Type != CatalogDocumentType.File))
                {
                    Assert.NotEmpty(viewModel.TreeNodes);
                }

                verify?.Invoke(viewModel);
            }
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
