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
using SkyCD.Plugin.Katalog.CsvIdx;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.Plugin.Katalog.CsvIdx.Tests;

public sealed class KatalogIntegrationTests
{
    [Fact]
    public async Task ReadAsync_StandardCollection_BuildsDocumentAndSummary()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection");
        var plugin = new KatalogCsvIdxPlugin();

        var result = await plugin.ReadAsync(new Abstractions.Capabilities.FileFormats.FileFormatReadRequest
        {
            FormatId = "katalog-csv-idx",
            SourcePath = fixtureDir
        });

        Assert.True(result.Success, result.Error);
        var reportDoc = Assert.IsType<KatalogCsvIdxReportDocument>(result.Payload);
        Assert.NotNull(reportDoc.Summary);
        Assert.Equal("3.0", reportDoc.Summary.AppVersion);
        Assert.True(reportDoc.Summary.TotalCatalogs >= 2);
        Assert.True(reportDoc.Summary.TotalFiles >= 2);
    }

    [Fact]
    public async Task LoadAsync_WithCatalogLoadService_PersistsAndReopensAccurately()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection");
        var manager = new FileFormatManager([new KatalogCsvIdxPlugin()]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixtureDir, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            Persist(documents, vm =>
            {
                // Verify tree roots
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("device:1", rootKeys); // Virtual Backups Group
                Assert.Contains("device:3", rootKeys); // Photos 2026 Catalog

                // Check group children
                var groupNode = vm.TreeNodes.First(node => node.Key == "device:1");
                Assert.Contains(groupNode.Children, child => child.Key == "device:2"); // Backup Disk 1

                // Verify file documents have sizes, checksums, tags
                var planDoc = documents.FirstOrDefault(d => d.Name == "project_plan.pdf");
                Assert.NotNull(planDoc);
                Assert.Equal(1400000, planDoc.Size);
                Assert.True(planDoc.Properties.ContainsKey("SHA256"));
                Assert.True(planDoc.Properties.ContainsKey("Tags"));
                Assert.Contains("important", planDoc.Properties["Tags"]?.ToString());
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.True(loadedCount > 0);
    }

    [Fact]
    public async Task ReadAsync_DetectsPathTraversal_AndFailsGracefully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "katalog-attack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var attackIdx = Path.Combine(AppContext.BaseDirectory, "Fixtures", "traversal-attack.idx");
            File.Copy(attackIdx, Path.Combine(tempDir, "attack.idx"));

            var plugin = new KatalogCsvIdxPlugin();
            var result = await plugin.ReadAsync(new Abstractions.Capabilities.FileFormats.FileFormatReadRequest
            {
                FormatId = "katalog-csv-idx",
                SourcePath = tempDir
            });

            Assert.False(result.Success);
            Assert.Contains("traverse outside", result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-katalog-" + Guid.NewGuid().ToString("N"));
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
