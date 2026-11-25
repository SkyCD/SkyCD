using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.App;
using SkyCD.App.Views;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.FileFormat.CDPlayer;
using SkyCD.Plugin.FileFormat.CDPlayer.Exceptions;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;
using SkyCD.Plugin.FileFormat.CDPlayer.Parsing;

namespace SkyCD.Plugin.FileFormat.CDPlayer.Tests;

public sealed class CdPlayerIniIntegrationTests
{
    [Fact]
    public void CanHandle_ValidCdPlayerIni_ReturnsTrue()
    {
        var text = "[12ab34cd]\r\nartist=Pink Floyd\r\ntitle=The Dark Side\r\nnumtracks=2\r\n0=Speak to Me\r\n1=Breathe\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var canHandle = CdPlayerIniParser.CanHandle(stream);

        Assert.True(canHandle);
    }

    [Fact]
    public void CanHandle_GenericNonCdIni_ReturnsFalse()
    {
        var text = "[Settings]\r\nTheme=Dark\r\nFontSize=14\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var canHandle = CdPlayerIniParser.CanHandle(stream);

        Assert.False(canHandle);
    }

    [Fact]
    public async Task LoadAsync_WithStandardFixture_PersistsAndReopensAccurately()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CdPlayerIni", "cdplayer-standard.ini");
        var plugin = new CdPlayerIniPlugin();
        var manager = new FileFormatManager([plugin]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.NotEmpty(documents);

            // 2 root media + 10 tracks + 5 tracks = 17 documents
            Assert.Equal(17, documents.Count);

            Persist(documents, vm =>
            {
                var rootKeys = vm.TreeNodes.Select(node => node.Key).ToList();
                Assert.Contains("cd:12ab34cd", rootKeys);
                Assert.Contains("cd:abcdef01", rootKeys);

                var pinkFloydNode = vm.TreeNodes.First(node => node.Key == "cd:12ab34cd");
                Assert.Equal("The Dark Side of the Moon", pinkFloydNode.Title);

                var milesDavisNode = vm.TreeNodes.First(node => node.Key == "cd:abcdef01");
                Assert.Equal("Kind of Blue", milesDavisNode.Title);

                // Select disc and verify items in browser list
                vm.SelectedTreeNode = pinkFloydNode;
                Assert.Equal(10, vm.BrowserItems.Count);

                vm.SelectedTreeNode = milesDavisNode;
                Assert.Equal(5, vm.BrowserItems.Count);
            });

            return documents.Count;
        }, new Progress<int>());

        Assert.Equal(17, loadedCount);
    }

    [Fact]
    public async Task LoadAsync_WithSparseFixture_GeneratesDefaultTrackNames()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CdPlayerIni", "cdplayer-sparse.ini");
        var plugin = new CdPlayerIniPlugin();
        var manager = new FileFormatManager([plugin]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.Equal(5, documents.Count); // 1 root + 4 tracks

            var track1 = documents.Single(d => d.ParentId == "cd:55aa33cc" && d.Name == "Hunter");
            var track2 = documents.Single(d => d.ParentId == "cd:55aa33cc" && d.Name == "Track 02");
            var track3 = documents.Single(d => d.ParentId == "cd:55aa33cc" && d.Name == "Jóga");
            var track4 = documents.Single(d => d.ParentId == "cd:55aa33cc" && d.Name == "Track 04");

            Assert.NotNull(track1);
            Assert.NotNull(track2);
            Assert.NotNull(track3);
            Assert.NotNull(track4);

            return documents.Count;
        }, new Progress<int>());

        Assert.Equal(5, loadedCount);
    }

    [Fact]
    public async Task LoadAsync_WithAnsiFixture_DecodesSpecialCharactersAccurately()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CdPlayerIni", "cdplayer-ansi.ini");
        var plugin = new CdPlayerIniPlugin();
        var manager = new FileFormatManager([plugin]);
        var loadService = new CatalogLoadService(manager);

        var loadedCount = await loadService.LoadAsync(fixturePath, (payload, _) =>
        {
            var documents = Extract(payload!);
            Assert.Equal(3, documents.Count); // 1 root + 2 tracks

            var root = documents.Single(d => d.ParentId is null);
            Assert.Equal("Dr. Feelgood", root.Name);
            Assert.Equal("Mötley Crüe", root.Properties["Artist"]?.ToString());

            var track2 = documents.Single(d => d.ParentId == "cd:fe01ba98" && d.Name == "Dr. Feelgood");
            Assert.NotNull(track2);

            return documents.Count;
        }, new Progress<int>());

        Assert.Equal(3, loadedCount);
    }

    [Fact]
    public void Parse_InvalidIniWithoutCdSections_ThrowsCdPlayerInvalidFormatException()
    {
        var text = "[General]\r\nVersion=1.0\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        Assert.Throws<CdPlayerInvalidFormatException>(() => CdPlayerIniParser.Parse(stream));
    }

    [Fact]
    public void Parse_ExceedingFileSizeLimit_ThrowsCdPlayerFileSizeLimitExceededException()
    {
        var text = "[12345678]\r\nartist=Test\r\ntitle=Test\r\nnumtracks=1\r\n0=Track 1\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        Assert.Throws<CdPlayerFileSizeLimitExceededException>(() => CdPlayerIniParser.Parse(stream, maxSizeBytes: 10));
    }

    [Fact]
    public void Parse_DuplicateSections_MergesEntries()
    {
        var text = "[12345678]\r\nartist=Artist A\r\ntitle=Album A\r\nnumtracks=2\r\n0=Track 1\r\n\r\n[12345678]\r\n1=Track 2\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var doc = CdPlayerIniParser.Parse(stream);
        Assert.Equal(3, doc.Count); // 1 root + 2 tracks
    }

    [Fact]
    public void Parse_OverwritesDuplicateKeysWithinSection()
    {
        var text = "[12345678]\r\nartist=Old Artist\r\nartist=New Artist\r\ntitle=Album A\r\nnumtracks=1\r\n0=Track 1\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var doc = CdPlayerIniParser.Parse(stream);
        var root = doc.Single(r => r["parentId"] is null);
        var props = Assert.IsAssignableFrom<IDictionary<string, object?>>(root["properties"]);
        Assert.Equal("New Artist", props["Artist"]);
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void Persist(IReadOnlyList<CatalogDocument> documents, Action<MainWindowViewModel>? verify = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-cdplayer-" + Guid.NewGuid().ToString("N"));
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
