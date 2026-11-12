using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.App.Views;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogImportTests
{
    [Theory]
    [InlineData("binary-sample.scd")]
    [InlineData("text-sample.scd")]
    public async Task Import_PersistsLegacyTrees(string file)
    {
        await using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = source });
        Assert.True(result.Success, result.Error);
        var documents = Extract(result.Payload!);
        Assert.Equal(5, documents.Count);
        Assert.Equal(CatalogDocumentType.Media, documents.Single(entry => entry.Name == "[Disk]").Type);
        Assert.Equal(CatalogDocumentType.Folder, documents.Single(entry => entry.Name == "Empty").Type);
        var folder = documents.Single(entry => entry.Name == "Folder [2023]");
        var leaf = documents.Single(entry => entry.Name == "Readme.txt");
        Assert.Equal(folder.Id, leaf.ParentId);
        Assert.Equal(1536L, leaf.Size);
        Assert.Equal(CatalogDocumentType.File, leaf.Type);
        Persist(documents);
    }

    [Fact]
    public void Import_PersistsLongUnicodePathsAndUrlsWithStableDistinctIds()
    {
        var segment = new string('字', 300);
        var catalog = new LegacyScdCatalog();
        catalog.Entries.Add(new LegacyScdEntry { Path = $"[Disk]\\{segment}\\{segment}\\File.txt", SizeBytes = 123 });
        catalog.Entries.Add(new LegacyScdEntry { Path = $"[Disk]\\{segment}\\{segment}\\Other.txt", SizeBytes = 456 });
        catalog.Entries.Add(new LegacyScdEntry { Path = "https://example.com/" + segment, SizeBytes = 789 });
        var documents = Extract(catalog);
        Assert.Equal(documents.Count, documents.Select(entry => entry.Id).Distinct().Count());
        Assert.All(documents, entry => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(entry.Id), 1, 240));
        Assert.Equal(documents.Select(entry => entry.Id), Extract(catalog).Select(entry => entry.Id));
        Assert.All(documents.Where(entry => entry.ParentId is not null), entry => Assert.Contains(documents, parent => parent.Id == entry.ParentId));
        Persist(documents);
    }

    [Fact]
    public void Import_RejectsUnsupportedPayloadAndAcceptsEmptyCatalog()
    {
        var error = Assert.Throws<TargetInvocationException>(() => Extract(new object()));
        Assert.IsAssignableFrom<InvalidOperationException>(error.InnerException);
        Assert.Contains("unsupported catalog payload", error.InnerException!.Message);
        Assert.Equal("library", Assert.Single(Extract(new LegacyScdCatalog())).Id);
    }

    [Fact]
    public void Import_PreservesJsonNodeHierarchy()
    {
        using var json = System.Text.Json.JsonDocument.Parse("""
            {"nodes":[
              {"nodeId":"root","parentId":null,"kind":"folder","name":"Root","sizeBytes":0},
              {"nodeId":"child","parentId":"root","kind":"file","name":"File.txt","sizeBytes":42}
            ]}
            """);
        var entries = Extract(json.RootElement);
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].ParentId);
        Assert.Equal(entries[0].Id, entries[1].ParentId);
        Assert.Equal(CatalogDocumentType.File, entries[1].Type);
        Assert.Equal(42L, entries[1].Size);
        Persist(entries);
    }

    internal static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    [Fact]
    public void Import_DuplicateFolderTitlesNavigateByIdWithinTheirParent()
    {
        var catalog = new LegacyScdCatalog();
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk A]\disk1", IsDirectory = true });
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk B]\disk1", IsDirectory = true });
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk C]\DISK1", IsDirectory = true });
        var documents = Extract(catalog);
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(documents);
        foreach (var folder in documents.Where(entry => entry.Name.Equals("disk1", StringComparison.OrdinalIgnoreCase)))
        {
            var diskNode = vm.TreeNodes.Single().Children.Single(node => node.Key == folder.ParentId);
            vm.SelectedTreeNode = diskNode;
            vm.SelectedBrowserItem = vm.BrowserItems.Single(item => item.Id == folder.Id);
            vm.NavigateToFolderCommand.Execute(null);
            Assert.Equal(folder.Id, vm.SelectedTreeNode!.Key);
        }
        Persist(documents);
    }

    [Fact]
    public void ClosedImportedCatalog_DoesNotQueryDisposedDatabaseOnCultureChange()
    {
        var original = LocalizationService.Current.CurrentCulture;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        MainWindowViewModel? retained = null;
        var documents = new[]
        {
            new CatalogDocument { Id = "root", Name = "Root", Type = CatalogDocumentType.Folder },
            new CatalogDocument { Id = "child", ParentId = "root", Name = "File", Type = CatalogDocumentType.File }
        };
        Persist(documents, vm => retained = vm);
        Assert.NotNull(retained);
        retained.StatusText = "Closed catalog";
        try
        {
            LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo(original.Name == "lt" ? "en" : "lt"));
            Assert.Equal("Closed catalog", retained.StatusText);
            retained.Dispose(); // Disposal is safe when called more than once.
            retained.RefreshLocalization();
            Assert.Equal("Closed catalog", retained.StatusText);
        }
        finally
        {
            LocalizationService.Current.SetCulture(original);
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            GC.KeepAlive(retained);
        }
    }

    internal static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-import-" + Guid.NewGuid().ToString("N"));
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

            // Opening a catalog continues past persistence into tree construction.
            var repository = new CatalogDocumentRepository();
            repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
            var constructor = typeof(MainWindowViewModel).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(candidate => candidate.GetParameters().Length == 5);
            using var vm = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
                [repository, new PropertyValueLocalizer(), new MenuExtensionManager(Array.Empty<IMenuPluginCapability>()), false, null]));
            if (documents.Any(entry => entry.ParentId is null && entry.Type != CatalogDocumentType.File))
                Assert.NotEmpty(vm.TreeNodes);
            verify?.Invoke(vm);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
