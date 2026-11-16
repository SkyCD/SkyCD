using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.CdCollect.Tests;

public sealed class CdCollectIntegrationTests
{
    [Fact]
    public async Task LegacySqlite2Fixture_LoadsIntoDomainAndPersistsHierarchyAndMetadata()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sqlite2", "cds.db");
        var service = new CatalogLoadService(new FileFormatManager([new CdCollectPlugin(), new CdCollectSqlite2Plugin()]));
        var count = await service.LoadAsync(path, (payload, _) =>
        {
            var documents = Extract(payload!);
            var file = documents.Single(d => d.Name == "O'Brien.mp3");
            Assert.Equal(CatalogDocumentType.File, file.Type);
            Assert.Equal(123456L, file.Size);
            Assert.Equal("Music", documents.Single(d => d.Id == file.ParentId).Name);
            Assert.Equal("audio/mpeg", file.Properties["MimeType"]);
            Assert.Equal("Queen", file.Properties["Artist"]);
            var media = documents.Single(d => d.Name == "Legacy music");
            Assert.Equal("rock, backups", media.Properties["Keywords"]);
            Assert.Equal("2005-03-15", media.Properties["Date"]);
            Assert.Equal("cdrom", media.Properties["Icon"]);
            Persist(documents);
            return documents.Count;
        }, new Progress<int>());
        Assert.Equal(6, count);
    }

    [Fact]
    public async Task CommittedFixture_ImportsIntoDomainAndPersistsThumbnail()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cds3.db");
        using var source = File.OpenRead(path);
        var result = await new CdCollectPlugin().ReadAsync(new FileFormatReadRequest
        { Source = source, FormatId = "cdcollect-db" });
        Assert.True(result.Success, result.Error);
        var documents = Extract(result.Payload!);
        var cover = documents.Single(d => d.Name == "cover.png");
        Assert.StartsWith("data:image/png;base64,", cover.Properties["Thumbnail"]?.ToString());
        Persist(documents);
    }

    [Fact]
    public async Task ReadAsync_StandardDatabase_BuildsDocumentAndSummary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_int_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var samplePath = Path.Combine(tempDir, "cdcollect-sample-v4.db");
            var plugin = new CdCollectPlugin();

            using var fileStream = File.OpenRead(samplePath);
            var result = await plugin.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "cdcollect-db",
                Source = fileStream,
                SourcePath = samplePath
            });

            Assert.True(result.Success, result.Error);
            var reportDoc = Assert.IsType<CdCollectReportDocument>(result.Payload);
            Assert.NotNull(reportDoc.Summary);
            Assert.Equal(4, reportDoc.Summary.CatalogVersion);
            Assert.Equal(2, reportDoc.Summary.MediaCount);
            Assert.Equal(2, reportDoc.Summary.DirectoryCount);
            Assert.Equal(4, reportDoc.Summary.FileCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task LoadAsync_WithCatalogLoadService_PersistsAndReopensAccurately()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_int_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var samplePath = Path.Combine(tempDir, "cdcollect-sample-v4.db");
            var manager = new FileFormatManager([new CdCollectPlugin()]);
            var loadService = new CatalogLoadService(manager);

            var loadedCount = await loadService.LoadAsync(samplePath, (payload, _) =>
            {
                var documents = Extract(payload!);
                Assert.NotEmpty(documents);

                Persist(documents, vm =>
                {
                    // Verify tree roots (media items)
                    var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                    Assert.Contains("cd_1", rootKeys);
                    Assert.Contains("cd_2", rootKeys);

                    var rockNode = vm.TreeNodes.First(node => node.Key == "cd_1");
                    Assert.Equal("Rock Albums 2024", rockNode.Title);
                    Assert.Contains(rockNode.Children, child => child.Key == "cd_1_n1");

                    var musicFolderNode = rockNode.Children.First(child => child.Key == "cd_1_n1");
                    Assert.Equal("Music", musicFolderNode.Title);

                    // Verify track01 document
                    var trackDoc = documents.FirstOrDefault(d => d.Name == "track01.mp3");
                    Assert.NotNull(trackDoc);
                    Assert.Equal(8400000L, trackDoc.Size);
                    Assert.True(trackDoc.Properties.ContainsKey("Album"));
                    Assert.Equal("A Night at the Opera", trackDoc.Properties["Album"]?.ToString());
                    Assert.Equal("Queen", trackDoc.Properties["Artist"]?.ToString());
                });

                return documents.Count;
            }, new Progress<int>());

            Assert.True(loadedCount > 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task WriteAsync_EmptyCatalog_ReturnsActionableFailure()
    {
        var plugin = new CdCollectPlugin();
        using var stream = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "cdcollect-db",
            Target = stream,
            Payload = new List<Dictionary<string, object?>>()
        });

        Assert.False(result.Success);
        Assert.Contains("no CD media", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RespectsCancellationToken()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_int_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var samplePath = Path.Combine(tempDir, "cdcollect-sample-v4.db");

            var plugin = new CdCollectPlugin();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            using var fileStream = File.OpenRead(samplePath);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await plugin.ReadAsync(new FileFormatReadRequest
                {
                    FormatId = "cdcollect-db",
                    Source = fileStream
                }, cts.Token);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
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
        var directory = Path.Combine(Path.GetTempPath(), "skycd-cdcollect-" + Guid.NewGuid().ToString("N"));
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

            verify?.Invoke(vm);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                try { Directory.Delete(directory, true); } catch { /* ignore */ }
            }
        }
    }
}
