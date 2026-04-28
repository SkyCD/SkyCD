using SkyCD.App.Models;
using SkyCD.App.Services;
using SkyCD.Presentation.ViewModels;

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
    public void AppOptionsStore_PersistsOptionsAcrossInstances()
    {
        var expected = new AppOptions
        {
            WindowLeft = 25,
            WindowTop = 35,
            WindowWidth = 1200,
            WindowHeight = 850,
            WindowState = "Maximized",
            TreePaneWidth = 320,
            IsStatusBarVisible = false,
            BrowserViewMode = BrowserViewMode.Tiles,
            BrowserSortMode = BrowserSortMode.Type,
            PluginPath = @"C:\plugins\custom",
            Language = "Lithuanian",
            DisabledPluginIds = ["plugin.a", "plugin.b"],
            OptionsTabIndex = 2
        };

        using (var writerLocalStore = new CouchbaseLocalStore())
        {
            var writerStore = new AppOptionsStore(writerLocalStore);
            writerStore.Save(expected);
        }

        using var readerLocalStore = new CouchbaseLocalStore();
        var readerStore = new AppOptionsStore(readerLocalStore);
        var actual = readerStore.Load();

        Assert.Equal(expected.WindowLeft, actual.WindowLeft);
        Assert.Equal(expected.WindowTop, actual.WindowTop);
        Assert.Equal(expected.WindowWidth, actual.WindowWidth);
        Assert.Equal(expected.WindowHeight, actual.WindowHeight);
        Assert.Equal(expected.WindowState, actual.WindowState);
        Assert.Equal(expected.TreePaneWidth, actual.TreePaneWidth);
        Assert.Equal(expected.IsStatusBarVisible, actual.IsStatusBarVisible);
        Assert.Equal(expected.BrowserViewMode, actual.BrowserViewMode);
        Assert.Equal(expected.BrowserSortMode, actual.BrowserSortMode);
        Assert.Equal(expected.PluginPath, actual.PluginPath);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.DisabledPluginIds, actual.DisabledPluginIds);
        Assert.Equal(expected.OptionsTabIndex, actual.OptionsTabIndex);
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
