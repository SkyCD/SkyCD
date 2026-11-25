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
using SkyCD.TestSupport;
using Xunit;
using SkyCD.Plugin.FileFormat.Basenji.Models;

namespace SkyCD.Plugin.FileFormat.Basenji.Tests;

public sealed class BasenjiVdbIntegrationTests
{
    [Fact]
    public async Task ReadAsync_StandardVdbFile_BuildsDocumentAndSummary()
    {
        using var workspace = new TestWorkspace();
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        var tempFile = workspace.CreateFile("sample.vdb", bytes);

        var plugin = new BasenjiVdbPlugin();

        using var fileStream = File.OpenRead(tempFile);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "basenji-vdb",
            Source = fileStream,
            SourcePath = tempFile
        });

        Assert.True(result.Success, result.Error);
        var reportDoc = Assert.IsType<BasenjiVdbReportDocument>(result.Payload);
        Assert.NotNull(reportDoc.Summary);
        Assert.Equal("My Basenji Collection", reportDoc.Summary.DatabaseName);
        Assert.Equal(4, reportDoc.Summary.DatabaseVersion);
        Assert.Equal(2, reportDoc.Summary.MediaCount);
        Assert.Equal(1, reportDoc.Summary.DirectoryCount);
        Assert.Equal(4, reportDoc.Summary.FileCount);
    }

    [Fact]
    public async Task LoadAsync_WithCatalogLoadService_PersistsAndReopensAccurately()
    {
        using var workspace = new TestWorkspace();
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        var tempFile = workspace.CreateFile("sample-load.vdb", bytes);

        var manager = new FileFormatManager([new BasenjiVdbPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(tempFile, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            Persist(workspace, documents, vm =>
            {
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("media:1", rootKeys);
                Assert.Contains("media:2", rootKeys);

                var media1Node = vm.TreeNodes.First(node => node.Key == "media:1");
                Assert.Equal("Backup CD 1", media1Node.Title);
                Assert.Contains(media1Node.Children, child => child.Key == "item:1_2" && child.Title == "Docs");

                var pdfDoc = documents.FirstOrDefault(d => d.Name == "report.pdf");
                Assert.NotNull(pdfDoc);
                Assert.Equal(5242880L, pdfDoc.Size);
                Assert.Equal(CatalogDocumentType.File, pdfDoc.Type);
                Assert.Equal("hash-md5-1234", pdfDoc.Properties["Hash"]?.ToString());

                var songDoc = documents.FirstOrDefault(d => d.Name == "song.mp3");
                Assert.NotNull(songDoc);
                Assert.Equal("The Band", songDoc.Properties["Artist"]?.ToString());
                Assert.Equal("Best Hits", songDoc.Properties["Album"]?.ToString());
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    [Fact]
    public async Task WriteAsync_ReturnsFailure_BecauseFormatIsReadOnly()
    {
        var plugin = new BasenjiVdbPlugin();
        using var stream = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "basenji-vdb",
            Target = stream,
            Payload = new List<Dictionary<string, object?>>()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RespectsCancellationToken()
    {
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        var plugin = new BasenjiVdbPlugin();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var memoryStream = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await plugin.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "basenji-vdb",
                Source = memoryStream
            }, cts.Token);
        });
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(TestWorkspace workspace, IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = workspace.CreateDirectory("catalog");
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
        database.Close();
    }
}
