using SkyCD.App.Services;
using SkyCD.Documents;
using SkyCD.Couchbase.Mapping;
using SkyCD.Presentation.ViewModels;
using Couchbase.Lite;
using Avalonia.Controls;

namespace SkyCD.App.Tests;

public sealed class CouchbasePersistenceTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-cblite-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

    public CouchbasePersistenceTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
    }

    [Fact]
    public void BrowserDataStore_LoadsSeededCatalogData()
    {
        using var localStore = new CouchbaseLocalStore();
        var dataStore = new CouchbaseLiteBrowserDataStore(localStore);

        var roots = dataStore.GetTreeNodes();
        Assert.Single(roots);
        Assert.Equal("Library", roots[0].Title);

        var items = dataStore.GetBrowserItems("movies");
        Assert.Equal(2, items.Count);
        Assert.Contains(items, static item => item.Name == "Interstellar.mkv");
    }

    [Fact]
    public void AppOptionsRepository_PersistsOptionsAcrossInstances()
    {
        var expected = new AppOptionsDocument
        {
            Window = new WindowOptionsDocument
            {
                Left = 25,
                Top = 35,
                Width = 1200,
                Height = 850,
                State = WindowState.Maximized,
                TreePaneWidth = 320
            },
            IsStatusBarVisible = false,
            Browser = new BrowserOptionsDocument
            {
                ViewMode = BrowserViewMode.Tiles,
                SortMode = BrowserSortMode.Type
            },
            PluginPath = @"C:\plugins\custom",
            Language = "Lithuanian",
            DisabledPluginIds = ["plugin.a", "plugin.b"],
            OptionsTabIndex = 2
        };

        using (var writerLocalStore = new CouchbaseLocalStore())
        {
            var writerRepository = writerLocalStore.GetRepository<AppOptionsDocument>();
            writerRepository.Save(AppOptionsDocument.DocumentId, expected);
        }

        using var readerLocalStore = new CouchbaseLocalStore();
        var readerRepository = readerLocalStore.GetRepository<AppOptionsDocument>();
        var actual = readerRepository.GetOrCreate<AppOptionsDocument>(AppOptionsDocument.DocumentId);

        Assert.Equal(expected.Window.Left, actual.Window.Left);
        Assert.Equal(expected.Window.Top, actual.Window.Top);
        Assert.Equal(expected.Window.Width, actual.Window.Width);
        Assert.Equal(expected.Window.Height, actual.Window.Height);
        Assert.Equal(expected.Window.State, actual.Window.State);
        Assert.Equal(expected.Window.TreePaneWidth, actual.Window.TreePaneWidth);
        Assert.Equal(expected.IsStatusBarVisible, actual.IsStatusBarVisible);
        Assert.Equal(expected.Browser.ViewMode, actual.Browser.ViewMode);
        Assert.Equal(expected.Browser.SortMode, actual.Browser.SortMode);
        Assert.Equal(expected.PluginPath, actual.PluginPath);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.DisabledPluginIds, actual.DisabledPluginIds);
        Assert.Equal(expected.OptionsTabIndex, actual.OptionsTabIndex);
    }

    [Fact]
    public void DocumentSerialization_WorksWithoutMappingExtensions_UsingInMemoryDocuments()
    {
        var catalog = CatalogDocument.CreateDefaultEntries().First();
        using var catalogDoc = catalog.ToMutableDocument(catalog.Id);
        var restoredCatalog = catalogDoc.FromDocument<CatalogDocument>();

        Assert.NotNull(restoredCatalog);
        Assert.Equal(catalog.Id, restoredCatalog!.Id);
        Assert.Equal(catalog.Name, restoredCatalog.Name);

        var options = new AppOptionsDocument
        {
            Window = new WindowOptionsDocument
            {
                Left = 10,
                Top = 20,
                Width = 800,
                Height = 600,
                State = WindowState.Normal,
                TreePaneWidth = 250
            },
            IsStatusBarVisible = true,
            Browser = new BrowserOptionsDocument
            {
                ViewMode = BrowserViewMode.Details,
                SortMode = BrowserSortMode.Name
            },
            PluginPath = "vfs://plugins",
            Language = "English",
            DisabledPluginIds = ["plugin.x"],
            OptionsTabIndex = 1
        };

        using var optionsDoc = options.ToMutableDocument("app-options");
        var restoredOptions = optionsDoc.FromDocument<AppOptionsDocument>();

        Assert.NotNull(restoredOptions);
        Assert.Equal(options.PluginPath, restoredOptions!.PluginPath);
        Assert.Equal(options.DisabledPluginIds, restoredOptions.DisabledPluginIds);
        Assert.Equal(options.Browser.ViewMode, restoredOptions.Browser.ViewMode);
    }

    [Fact]
    public void AppOptionsDocument_FromDocument_ReadsMappedKeysIncludingPluginPath()
    {
        var window = new MutableDictionaryObject();
        window.SetInt("Left", 12);
        window.SetInt("Top", 14);
        window.SetDouble("Width", 1024);
        window.SetDouble("Height", 768);
        window.SetString("State", "Normal");
        window.SetDouble("TreePaneWidth", 280);

        var browser = new MutableDictionaryObject();
        browser.SetString("ViewMode", BrowserViewMode.LargeIcons.ToString());
        browser.SetString("SortMode", BrowserSortMode.Size.ToString());

        var disabledPlugins = new MutableArrayObject();
        disabledPlugins.AddString("legacy.plugin");

        using var doc = new MutableDocument("app-options");
        doc.SetDictionary("Window", window);
        doc.SetBoolean("IsStatusBarVisible", true);
        doc.SetDictionary("Browser", browser);
        doc.SetString("PluginPath", @"C:\plugins\legacy");
        doc.SetString("Language", "English");
        doc.SetArray("DisabledPluginIds", disabledPlugins);
        doc.SetInt("OptionsTabIndex", 1);

        var result = doc.FromDocument<AppOptionsDocument>();

        Assert.NotNull(result);
        Assert.Equal(@"C:\plugins\legacy", result!.PluginPath);
        Assert.Contains("legacy.plugin", result.DisabledPluginIds);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);

        if (Directory.Exists(appDataRoot))
        {
            try
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
            catch (IOException)
            {
                // Couchbase Lite can release file handles slightly after dispose on some systems.
            }
        }
    }
}
