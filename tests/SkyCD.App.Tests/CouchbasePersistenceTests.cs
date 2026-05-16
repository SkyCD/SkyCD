using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Lists;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.ViewModels;
using Xunit;

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
    public void MainWindowViewModel_LoadsSeededCatalogDataFromRepository()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        ClearCatalog(catalogRepository);
        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 99;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
        var viewModel = new MainWindowViewModel(repositoryManager);

        var roots = viewModel.TreeNodes;
        Assert.Single(roots);
        Assert.Equal("Library", roots[0].Title);

        viewModel.SelectedTreeNode = roots[0].Children.Single(child => child.Key == "movies");
        Assert.Equal(2, viewModel.BrowserItems.Count);
        Assert.Contains(viewModel.BrowserItems, static item => item.Name == "Interstellar.mkv");
    }

    [Fact]
    public void MainWindowViewModel_DoesNotReseedCatalogAfterFirstAppStart()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();

        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 1;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);

        var secondStartViewModel = new MainWindowViewModel(repositoryManager);

        Assert.Empty(secondStartViewModel.TreeNodes);

        options = appOptionsRepository.GetOrCreateAppOptions();
        Assert.Equal(2, options.AppStartCount);
    }

    [Fact]
    public void MainWindowViewModel_DeleteItem_SyncsWithRepositoryState()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        ClearCatalog(catalogRepository);
        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 99;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);

        var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        var moviesNode = libraryNode.Children.Single(child => child.Key == "movies");
        viewModel.SelectedTreeNode = moviesNode;
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == "arrival");

        viewModel.DeleteItemCommand.Execute(null);

        var reloadedViewModel = new MainWindowViewModel(repositoryManager);
        var reloadedMoviesNode = reloadedViewModel.TreeNodes.Single().Children.Single(child => child.Key == "movies");
        reloadedViewModel.SelectedTreeNode = reloadedMoviesNode;

        Assert.DoesNotContain(reloadedViewModel.BrowserItems, static item => item.Id == "arrival");
        Assert.Single(reloadedViewModel.BrowserItems);
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
                SortMode = "Type"
            },
            PluginPath = @"C:\plugins\custom",
            IsMcpServerEnabled = false,
            McpPort = 8787,
            IsMcpStatusIconVisible = false,
            Language = "Lithuanian",
            OptionsTabIndex = 2,
            AppStartCount = 7
        };

        var databaseDirectory = Path.Combine(appDataRoot, "SkyCD");
        Directory.CreateDirectory(databaseDirectory);

        var configuration = new DatabaseConfiguration
        {
            Directory = databaseDirectory
        };

        using (var writerDb = new Database("default", configuration))
        {
            var writerCollection = writerDb.GetCollection("settings", Collection.DefaultScopeName)
                                   ?? writerDb.CreateCollection("settings", Collection.DefaultScopeName);
            using var writerDocument = expected.ToMutableDocument(AppOptionsDocument.DocumentId);
            writerCollection.Save(writerDocument);
        }

        using var readerDb = new Database("default", configuration);
        var readerCollection = readerDb.GetCollection("settings", Collection.DefaultScopeName)
                               ?? readerDb.CreateCollection("settings", Collection.DefaultScopeName);
        using var readerDocument = readerCollection.GetDocument(AppOptionsDocument.DocumentId);
        var actual = readerDocument?.FromDocument<AppOptionsDocument>();

        Assert.NotNull(actual);

        Assert.Equal(expected.Window.Left, actual!.Window.Left);
        Assert.Equal(expected.Window.Top, actual.Window.Top);
        Assert.Equal(expected.Window.Width, actual.Window.Width);
        Assert.Equal(expected.Window.Height, actual.Window.Height);
        Assert.Equal(expected.Window.State, actual.Window.State);
        Assert.Equal(expected.Window.TreePaneWidth, actual.Window.TreePaneWidth);
        Assert.Equal(expected.IsStatusBarVisible, actual.IsStatusBarVisible);
        Assert.Equal(expected.Browser.ViewMode, actual.Browser.ViewMode);
        Assert.Equal(expected.Browser.SortMode, actual.Browser.SortMode);
        Assert.Equal(expected.PluginPath, actual.PluginPath);
        Assert.Equal(expected.IsMcpServerEnabled, actual.IsMcpServerEnabled);
        Assert.Equal(expected.McpPort, actual.McpPort);
        Assert.Equal(expected.IsMcpStatusIconVisible, actual.IsMcpStatusIconVisible);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.OptionsTabIndex, actual.OptionsTabIndex);
        Assert.Equal(expected.AppStartCount, actual.AppStartCount);
    }

    [Fact]
    public void DocumentSerialization_WorksWithoutMappingExtensions_UsingInMemoryDocuments()
    {
        var catalog = new CatalogDocumentRepository().CreateDefaultEntries().First();
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
                SortMode = "Name"
            },
            PluginPath = "vfs://plugins",
            IsMcpServerEnabled = false,
            McpPort = 8787,
            IsMcpStatusIconVisible = false,
            Language = "English",
            OptionsTabIndex = 1,
            AppStartCount = 3
        };

        using var optionsDoc = options.ToMutableDocument("app-options");
        var restoredOptions = optionsDoc.FromDocument<AppOptionsDocument>();

        Assert.NotNull(restoredOptions);
        Assert.Equal(options.PluginPath, restoredOptions!.PluginPath);
        Assert.Equal(options.IsMcpServerEnabled, restoredOptions.IsMcpServerEnabled);
        Assert.Equal(options.McpPort, restoredOptions.McpPort);
        Assert.Equal(options.IsMcpStatusIconVisible, restoredOptions.IsMcpStatusIconVisible);
        Assert.Equal(options.Browser.ViewMode, restoredOptions.Browser.ViewMode);
        Assert.Equal(options.AppStartCount, restoredOptions.AppStartCount);
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
        browser.SetString("SortMode", "Size");

        using var doc = new MutableDocument("app-options");
        doc.SetDictionary("Window", window);
        doc.SetBoolean("IsStatusBarVisible", true);
        doc.SetDictionary("Browser", browser);
        doc.SetString("PluginPath", @"C:\plugins\legacy");
        doc.SetBoolean("IsMcpServerEnabled", false);
        doc.SetInt("McpPort", 8787);
        doc.SetBoolean("IsMcpStatusIconVisible", false);
        doc.SetString("Language", "English");
        doc.SetInt("OptionsTabIndex", 1);
        doc.SetInt("AppStartCount", 5);

        var result = doc.FromDocument<AppOptionsDocument>();

        Assert.NotNull(result);
        Assert.Equal(@"C:\plugins\legacy", result!.PluginPath);
        Assert.False(result.IsMcpServerEnabled);
        Assert.Equal(8787, result.McpPort);
        Assert.False(result.IsMcpStatusIconVisible);
        Assert.Equal(5, result.AppStartCount);
    }

    [Fact]
    public void FromDocument_ParsesDateTimeOffset_WhenValueStoredAsString()
    {
        const string isoValue = "2026-05-03T00:24:03.007+00:00";
        using var doc = new MutableDocument("date-mapping");
        doc.SetString("Timestamp", isoValue);

        var result = doc.FromDocument<DateContainerDocument>();

        Assert.NotNull(result);
        Assert.Equal(DateTimeOffset.Parse(isoValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            result!.Timestamp);
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

    private sealed class DateContainerDocument
    {
        public DateTimeOffset Timestamp { get; set; }
    }

    private static void ClearCatalog(CatalogDocumentRepository catalogRepository)
    {
        foreach (var entry in catalogRepository.GetAll())
        {
            using var document = catalogRepository.Collection.GetDocument(entry.Id);
            if (document is not null)
            {
                catalogRepository.Collection.Delete(document);
            }
        }
    }
}
