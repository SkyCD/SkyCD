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

namespace SkyCD.Plugin.CdCat.Hcf.Tests;

public sealed class CdCatHcfIntegrationTests
{
    [Fact]
    public async Task ReadAsync_StandardHcfFile_BuildsDocumentAndSummary()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        CdCatTestHelper.EnsureFixtureFilesExist(fixtureDir);

        var samplePath = Path.Combine(fixtureDir, "cdcat-sample-v2.1.hcf");
        var plugin = new CdCatHcfPlugin();

        using var fileStream = File.OpenRead(samplePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "cdcat-hcf",
            Source = fileStream,
            SourcePath = samplePath
        });

        Assert.True(result.Success, result.Error);
        var reportDoc = Assert.IsType<CdCatHcfReportDocument>(result.Payload);
        Assert.NotNull(reportDoc.Summary);
        Assert.Equal("2.1", reportDoc.Summary.CatalogVersion);
        Assert.Equal(2, reportDoc.Summary.MediaCount);
        Assert.Equal(2, reportDoc.Summary.DirectoryCount);
        Assert.Equal(5, reportDoc.Summary.FileCount);
    }

    [Fact]
    public async Task LoadAsync_WithCatalogLoadService_PersistsAndReopensAccurately()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        CdCatTestHelper.EnsureFixtureFilesExist(fixtureDir);

        var samplePath = Path.Combine(fixtureDir, "cdcat-sample-v2.1.hcf");
        var manager = new FileFormatManager([new CdCatHcfPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(samplePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            Persist(documents, vm =>
            {
                // Verify tree roots (media items)
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("media:1_Audio CD", rootKeys);
                Assert.Contains("media:2_External Drive", rootKeys);

                var media1Node = vm.TreeNodes.First(node => node.Key == "media:1_Audio CD");
                Assert.Contains(media1Node.Children, child => child.Key == "folder:1_Music");

                // Verify file documents have sizes, comments, tags
                var trackDoc = documents.FirstOrDefault(d => d.Name == "track01.mp3");
                Assert.NotNull(trackDoc);
                Assert.Equal(4718592L, trackDoc.Size);
                Assert.True(trackDoc.Properties.ContainsKey("Album"));
                Assert.Equal("Best Hits", trackDoc.Properties["Album"]?.ToString());
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    [Fact]
    public async Task WriteAsync_ReturnsFailure_BecauseFormatIsReadOnly()
    {
        var plugin = new CdCatHcfPlugin();
        using var stream = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "cdcat-hcf",
            Target = stream,
            Payload = new List<Dictionary<string, object?>>()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RespectsCancellationToken()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        CdCatTestHelper.EnsureFixtureFilesExist(fixtureDir);
        var samplePath = Path.Combine(fixtureDir, "cdcat-sample-v2.1.hcf");

        var plugin = new CdCatHcfPlugin();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var fileStream = File.OpenRead(samplePath);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await plugin.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "cdcat-hcf",
                Source = fileStream
            }, cts.Token);
        });
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-cdcat-" + Guid.NewGuid().ToString("N"));
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
