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
using SkyCD.Plugin.FileFormat.Librer.Models;

namespace SkyCD.Plugin.FileFormat.Librer.Tests;

public sealed class LibrerIntegrationTests
{
    [Fact]
    public async Task ReadAsync_StandardArchive_BuildsDocumentAndSummary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "librer_int_" + Guid.NewGuid().ToString("N"));
        try
        {
            LibrerTestHelper.EnsureFixtureFilesExist(tempDir);
            var samplePath = Path.Combine(tempDir, "sample.librer_data");
            var plugin = new LibrerPlugin();

            using var fileStream = File.OpenRead(samplePath);
            var result = await plugin.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "librer",
                Source = fileStream,
                SourcePath = samplePath
            });

            Assert.True(result.Success, result.Error);
            var reportDoc = Assert.IsType<LibrerReportDocument>(result.Payload);
            Assert.NotNull(reportDoc.Summary);
            Assert.Equal("ArchiveDisk2024", reportDoc.Summary.CatalogLabel);
            Assert.Equal("/media/backup", reportDoc.Summary.ScanPath);
            Assert.Equal(3, reportDoc.Summary.TotalFiles);
            Assert.Equal(2, reportDoc.Summary.TotalFolders);
            Assert.Equal(1550000L, reportDoc.Summary.TotalSizeBytes);
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
    public async Task LoadAsync_WithCatalogLoadService_PersistsAndReconstructsHierarchy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "librer_int_" + Guid.NewGuid().ToString("N"));
        try
        {
            LibrerTestHelper.EnsureFixtureFilesExist(tempDir);
            var samplePath = Path.Combine(tempDir, "sample.librer_data");
            var manager = new FileFormatManager([new LibrerPlugin()]);
            var loadService = new CatalogLoadService(manager);

            var loadedCount = await loadService.LoadAsync(samplePath, (payload, _) =>
            {
                var documents = Extract(payload!);
                Assert.NotEmpty(documents);

                Persist(documents, vm =>
                {
                    // Verify tree roots (media items)
                    var rootNode = vm.TreeNodes.FirstOrDefault(node => node.Title == "ArchiveDisk2024");
                    Assert.NotNull(rootNode);

                    // Root should have children: Documents and Photos
                    Assert.Contains(rootNode.Children, child => child.Title == "Documents");
                    Assert.Contains(rootNode.Children, child => child.Title == "Photos");

                    var docsNode = rootNode.Children.First(child => child.Title == "Documents");
                    Assert.NotNull(docsNode);

                    var photosNode = rootNode.Children.First(child => child.Title == "Photos");
                    Assert.NotNull(photosNode);

                    // Verify documents extracted and persisted
                    var reportDoc = documents.FirstOrDefault(d => d.Name == "report.pdf");
                    Assert.NotNull(reportDoc);
                    Assert.Equal(500000L, reportDoc.Size);
                    Assert.Equal(docsNode.Key, reportDoc.ParentId);
                    Assert.True(reportDoc.Properties.ContainsKey("CustomData"));
                    Assert.Equal("PDF Document / 12 pages", reportDoc.Properties["CustomData"]?.ToString());

                    var notesDoc = documents.FirstOrDefault(d => d.Name == "notes.txt");
                    Assert.NotNull(notesDoc);
                    Assert.Equal(50000L, notesDoc.Size);
                    Assert.Equal(docsNode.Key, notesDoc.ParentId);

                    var sunsetDoc = documents.FirstOrDefault(d => d.Name == "sunset.jpg");
                    Assert.NotNull(sunsetDoc);
                    Assert.Equal(1000000L, sunsetDoc.Size);
                    Assert.Equal(photosNode.Key, sunsetDoc.ParentId);
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
    public async Task WriteAsync_ReturnsFailure_BecauseFormatIsReadOnly()
    {
        var plugin = new LibrerPlugin();
        using var stream = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "librer",
            Target = stream,
            Payload = new List<Dictionary<string, object?>>()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RespectsCancellationToken()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "librer_int_" + Guid.NewGuid().ToString("N"));
        try
        {
            LibrerTestHelper.EnsureFixtureFilesExist(tempDir);
            var samplePath = Path.Combine(tempDir, "sample.librer_data");

            var plugin = new LibrerPlugin();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            using var fileStream = File.OpenRead(samplePath);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await plugin.ReadAsync(new FileFormatReadRequest
                {
                    FormatId = "librer",
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
        var directory = Path.Combine(Path.GetTempPath(), "skycd-librer-" + Guid.NewGuid().ToString("N"));
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
