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
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5IntegrationTests
{
    [Fact]
    public async Task LoadAsync_DcmfWithMultipleVolumesAndUnicode_PersistsAndReopens()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var catalogPath = Path.Combine(directory, "archive.dcmf");
            await File.WriteAllBytesAsync(catalogPath, BuildMultiVolumeCatalog());

            var service = new CatalogLoadService(new FileFormatManager([new DiskCatalogMakerV5Plugin()]));
            var count = await service.LoadAsync(catalogPath, (payload, _) =>
            {
                var documents = Extract(payload!);
                Assert.Equal(6, documents.Count);
                Assert.Equal(documents.Count, documents.Select(entry => entry.Id).Distinct().Count());
                Assert.All(documents, entry => Assert.DoesNotContain(entry.Id, char.IsControl));

                var roots = documents.Where(entry => entry.ParentId is null).ToList();
                Assert.Equal(2, roots.Count);
                Assert.Contains(roots, entry => entry.Name == "Archive Disk");
                Assert.Contains(roots, entry => entry.Name == "Backup 2026");

                var folder = Assert.Single(documents, entry => entry.Name == "ドキュメント");
                var file = Assert.Single(documents, entry => entry.Name == "résumé.pdf");
                var archive = roots.Single(entry => entry.Name == "Archive Disk");
                Assert.Equal(archive.Id, folder.ParentId);
                Assert.Equal(folder.Id, file.ParentId);
                Assert.Equal(100, file.Size);

                var files = documents.Where(entry => entry.Name is "résumé.pdf" or "grüße.txt").ToList();
                Assert.Equal(2, files.Count);
                Assert.Equal(2, files.Select(entry => entry.ParentId).Distinct().Count());

                Persist(documents, viewModel =>
                {
                    var rootKeys = viewModel.TreeNodes.Select(node => node.Key).ToList();
                    Assert.Contains("media:1", rootKeys);
                    Assert.Contains("media:2", rootKeys);
                    Assert.NotEmpty(viewModel.TreeNodes.Single(node => node.Key == "media:1").Children);
                });

                return documents.Count;
            }, new Progress<int>());

            Assert.Equal(6, count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_DcmdPackage_PersistsThumbnailMetadataAndUnicodeNames()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "Catalog.dcm"), BuildThumbnailCatalog());
            var thumbnail = Path.Combine(root, "Thumbnails", "0a", "0b", "0c", "0d.png");
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
            await File.WriteAllBytesAsync(thumbnail, [1, 2, 3]);

            var service = new CatalogLoadService(new FileFormatManager([new DiskCatalogMakerV5Plugin()]));
            var count = await service.LoadAsync(root, (payload, _) =>
            {
                var documents = Extract(payload!);
                var media = Assert.Single(documents, entry => entry.ParentId is null);
                Assert.Equal("写真アーカイブ", media.Name);

                var file = Assert.Single(documents, entry => entry.Name == "pic.jpg");
                Assert.Equal("Thumbnails/0a/0b/0c/0d.png", file.Properties["Thumbnail"]?.ToString());
                Assert.Equal("true", file.Properties["ThumbnailResolved"]?.ToString());

                Persist(documents, viewModel =>
                    Assert.Single(viewModel.TreeNodes, node => node.Key == "media:1"));

                return documents.Count;
            }, new Progress<int>());

            Assert.Equal(2, count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_CorruptDcmf_SurfacesReadFailure()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var catalogPath = Path.Combine(directory, "broken.dcmf");
            await File.WriteAllBytesAsync(catalogPath, [1, 2, 3, 4]);
            var service = new CatalogLoadService(new FileFormatManager([new DiskCatalogMakerV5Plugin()]));

            await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.LoadAsync(
                catalogPath,
                (payload, _) => payload,
                new Progress<int>()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] BuildMultiVolumeCatalog()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Archive Disk");
        builder.AddVolume(2, "Backup 2026");
        builder.AddItem(1, 10, null, "ドキュメント", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(1, 11, 10, "résumé.pdf", sizeBytes: 100);
        builder.AddItem(2, 10, null, "Photos", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(2, 11, 10, "grüße.txt", sizeBytes: 200);
        return builder.Build();
    }

    private static byte[] BuildThumbnailCatalog()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "写真アーカイブ");
        builder.AddItem(1, 2, null, "pic.jpg", sizeBytes: 512, thumbnailKey: 0x0A0B0C0D);
        builder.AddThumbnail(0x0A0B0C0D, 2, "png");
        return builder.Build();
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-dcm5-couch-" + Guid.NewGuid().ToString("N"));
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
            using var viewModel = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
                [repository, new PropertyValueLocalizer(), new MenuExtensionManager([]), false, null]));

            if (documents.Any(entry => entry.ParentId is null && entry.Type != CatalogDocumentType.File))
            {
                Assert.NotEmpty(viewModel.TreeNodes);
            }

            verify?.Invoke(viewModel);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "skycd-dcm5-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
