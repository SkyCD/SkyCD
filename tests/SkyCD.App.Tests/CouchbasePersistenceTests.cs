using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CouchbasePersistenceTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    [Fact]
    public void ToolbarLayout_PersistsCustomCommandsAndSeparatorsAcrossRepositoryInstances()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var optionsRepository = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();
        var options = optionsRepository.GetOrCreateAppOptions();
        Assert.Null(options.Toolbar);
        options.Toolbar =
        [
            ToolbarItemDocument.Command(ToolbarCommandIds.Refresh),
            ToolbarItemDocument.Separator(),
            ToolbarItemDocument.Command(ToolbarCommandIds.Options)
        ];
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);

        var restored = optionsRepository.GetOrCreateAppOptions();

        Assert.Collection(restored.Toolbar!,
            entry => Assert.Equal(ToolbarCommandIds.Refresh, entry.CommandId),
            entry => Assert.Equal(ToolbarItemDocument.SeparatorKind, entry.Kind),
            entry => Assert.Equal(ToolbarCommandIds.Options, entry.CommandId));
    }

    [Fact]
    public void CustomProperties_PersistSettingsAndAcceptedEditsAcrossRepositoryInstances()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var optionsRepository = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();
        var options = optionsRepository.GetOrCreateAppOptions();
        options.CustomProperties = [new() { Name = "Owner", DefaultValue = "Alex" }];
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);
        var restored = optionsRepository.GetOrCreateAppOptions();
        Assert.Equal("Alex", Assert.Single(restored.CustomProperties).DefaultValue);

        using var vm = new MainWindowViewModel(repositories);
        var selectedId = vm.SelectedBrowserItem?.Id ?? vm.SelectedTreeNode!.Key;
        vm.PropertiesRequested += (_, args) =>
        {
            args.Dialog.ApplyCustomPropertyDefaults(restored.CustomProperties);
            args.Dialog.CustomProperties[0].DefaultValue = "Sam";
            args.Complete(true, args.Dialog.Comments);
        };
        vm.OpenPropertiesCommand.Execute(null);

        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        Assert.Equal("Sam", catalog.Get(selectedId)!.Properties["Owner"]);

        using var reopened = new MainWindowViewModel(repositories);
        reopened.PropertiesRequested += (_, args) =>
        {
            args.Dialog.ApplyCustomPropertyDefaults(restored.CustomProperties);
            Assert.Equal("Sam", args.Dialog.CustomProperties[0].DefaultValue);
            args.Dialog.CustomProperties[0].DefaultValue = "Cancelled";
            args.Complete(false, args.Dialog.Comments);
        };
        reopened.OpenPropertiesCommand.Execute(null);
        Assert.Equal("Sam", catalog.Get(selectedId)!.Properties["Owner"]);
    }

    [Fact]
    public void MainWindowViewModel_LoadsSeededCatalogDataFromRepository()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        Assert.StartsWith(sandbox.DatabaseDirectory + Path.DirectorySeparatorChar,
            provider.Resolve<DatabaseManager>().GetDatabase().Path, StringComparison.Ordinal);
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
        using var viewModel = new MainWindowViewModel(repositoryManager);

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

        using var secondStartViewModel = new MainWindowViewModel(repositoryManager);

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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        var moviesNode = libraryNode.Children.Single(child => child.Key == "movies");
        viewModel.SelectedTreeNode = moviesNode;
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == "arrival");

        viewModel.DeleteItemCommand.Execute(null);

        using var reloadedViewModel = new MainWindowViewModel(repositoryManager);
        var reloadedMoviesNode = reloadedViewModel.TreeNodes.Single().Children.Single(child => child.Key == "movies");
        reloadedViewModel.SelectedTreeNode = reloadedMoviesNode;

        Assert.DoesNotContain(reloadedViewModel.BrowserItems, static item => item.Id == "arrival");
        Assert.Single(reloadedViewModel.BrowserItems);
    }

    [Fact]
    public void MainWindowViewModel_DeleteMultiBrowserSelection_RemovesSiblingEntriesAndPersistsAcrossReopen()
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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        var moviesNode = libraryNode.Children.Single(child => child.Key == "movies");
        viewModel.SelectedTreeNode = moviesNode;

        var arrival = viewModel.BrowserItems.Single(item => item.Id == "arrival");
        var interstellar = viewModel.BrowserItems.Single(item => item.Id == "interstellar");
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { arrival, interstellar };
        viewModel.ActivePane = ActivePane.Browser;

        Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
        Assert.True(viewModel.IsDeleteEnabled);

        viewModel.DeleteItemCommand.Execute(null);

        Assert.Contains("Arrival.mkv", viewModel.StatusText);
        Assert.Contains("Interstellar.mkv", viewModel.StatusText);
        Assert.Empty(viewModel.BrowserItems);
        Assert.True(viewModel.IsDirtyDocument);

        using var reloadedViewModel = new MainWindowViewModel(repositoryManager);
        var reloadedMoviesNode = reloadedViewModel.TreeNodes.Single().Children.Single(child => child.Key == "movies");
        reloadedViewModel.SelectedTreeNode = reloadedMoviesNode;

        Assert.Empty(reloadedViewModel.BrowserItems);
        Assert.Null(catalogRepository.Get("arrival"));
        Assert.Null(catalogRepository.Get("interstellar"));
    }

    [Fact]
    public void MainWindowViewModel_DeleteTreeNode_RemovesNestedFolderAndDescendantsConsistently()
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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        var moviesNode = libraryNode.Children.Single(child => child.Key == "movies");
        viewModel.SelectedTreeNode = moviesNode;
        viewModel.ActivePane = ActivePane.Tree;

        Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
        Assert.True(viewModel.IsDeleteEnabled);

        viewModel.DeleteItemCommand.Execute(null);

        Assert.Equal("Deleted Movies.", viewModel.StatusText);
        Assert.Equal("music", viewModel.SelectedTreeNode?.Key);
        Assert.DoesNotContain(viewModel.TreeNodes.Single().Children, child => child.Key == "movies");
        Assert.DoesNotContain(viewModel.BrowserItems, item => item.Id == "movies");

        using var reloadedViewModel = new MainWindowViewModel(repositoryManager);
        Assert.DoesNotContain(reloadedViewModel.TreeNodes.Single().Children, child => child.Key == "movies");
        Assert.Null(catalogRepository.Get("movies"));
        Assert.Null(catalogRepository.Get("arrival"));
        Assert.Null(catalogRepository.Get("interstellar"));
    }

    [Fact]
    public void MainWindowViewModel_TreeDelete_WithUnrelatedBrowserSelection_TargetsTreeFolderNotBrowserItem()
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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        var musicNode = libraryNode.Children.Single(child => child.Key == "music");
        viewModel.SelectedTreeNode = musicNode;

        // Tree is focused
        viewModel.ActivePane = ActivePane.Tree;

        // Execute Delete with Tree active
        viewModel.DeleteItemCommand.Execute(null);

        Assert.Equal("Deleted Music.", viewModel.StatusText);
        Assert.Equal("projects", viewModel.SelectedTreeNode?.Key);
        Assert.DoesNotContain(viewModel.TreeNodes.Single().Children, child => child.Key == "music");

        // Music and its descendants are deleted
        Assert.Null(catalogRepository.Get("music"));
        Assert.Null(catalogRepository.Get("classical-collection"));
        Assert.Null(catalogRepository.Get("concert-2025"));

        // Movies and its contents remain intact
        Assert.NotNull(catalogRepository.Get("movies"));
        Assert.NotNull(catalogRepository.Get("arrival"));
        Assert.NotNull(catalogRepository.Get("interstellar"));
    }

    [Fact]
    public void MainWindowViewModel_DeleteMultipleFoldersAndItems_PerformsFastBatchDeletion()
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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        viewModel.SelectedTreeNode = libraryNode;
        viewModel.ActivePane = ActivePane.Browser;

        var movies = viewModel.BrowserItems.Single(item => item.Id == "movies");
        var music = viewModel.BrowserItems.Single(item => item.Id == "music");
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { movies, music };

        Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
        viewModel.DeleteItemCommand.Execute(null);

        Assert.Null(catalogRepository.Get("movies"));
        Assert.Null(catalogRepository.Get("arrival"));
        Assert.Null(catalogRepository.Get("interstellar"));
        Assert.Null(catalogRepository.Get("music"));
        Assert.Null(catalogRepository.Get("classical-collection"));
        Assert.Null(catalogRepository.Get("concert-2025"));
        Assert.NotNull(catalogRepository.Get("projects"));
    }

    [Fact]
    public void MainWindowViewModel_DeleteSyntheticRoot_IsDisabledAndProtected()
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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var libraryNode = viewModel.TreeNodes.Single();
        viewModel.SelectedTreeNode = libraryNode;
        viewModel.ActivePane = ActivePane.Tree;

        Assert.False(viewModel.DeleteItemCommand.CanExecute(null));
        Assert.False(viewModel.IsDeleteEnabled);

        viewModel.DeleteItemCommand.Execute(null);

        Assert.NotNull(catalogRepository.Get("library"));
        Assert.NotEmpty(viewModel.TreeNodes);
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
                SortMode = "Type",
                DetailsColumns =
                [
                    new DetailsColumnSettingDocument { Id = "location", IsVisible = true },
                    new DetailsColumnSettingDocument { Id = "name", IsVisible = true },
                    new DetailsColumnSettingDocument { Id = "size", IsVisible = false }
                ]
            },
            PluginPath = @"C:\plugins\custom",
            IsMcpServerEnabled = false,
            McpPort = 8787,
            IsMcpStatusIconVisible = false,
            Language = "Lithuanian",
            OptionsTabIndex = 2,
            AppStartCount = 7
        };

        var testDatabaseDirectory = sandbox.DatabaseDirectory;
        Directory.CreateDirectory(testDatabaseDirectory);

        var configuration = new DatabaseConfiguration
        {
            Directory = testDatabaseDirectory
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
        Assert.Equal(expected.Browser.DetailsColumns.Count, actual.Browser.DetailsColumns.Count);
        for (var i = 0; i < expected.Browser.DetailsColumns.Count; i++)
        {
            Assert.Equal(expected.Browser.DetailsColumns[i].Id, actual.Browser.DetailsColumns[i].Id);
            Assert.Equal(expected.Browser.DetailsColumns[i].IsVisible, actual.Browser.DetailsColumns[i].IsVisible);
        }
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
        doc.SetBoolean("IsScanHashingEnabled", true);

        var result = doc.FromDocument<AppOptionsDocument>();

        Assert.NotNull(result);
        Assert.Equal(@"C:\plugins\legacy", result!.PluginPath);
        Assert.False(result.IsMcpServerEnabled);
        Assert.Equal(8787, result.McpPort);
        Assert.False(result.IsMcpStatusIconVisible);
        Assert.Equal(5, result.AppStartCount);
        using var saved = result.ToMutableDocument("app-options");
        Assert.Null(saved.GetValue("IsScanHashingEnabled"));
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

    public void Dispose() => sandbox.Dispose();

    private sealed class DateContainerDocument
    {
        public DateTimeOffset Timestamp { get; set; }
    }

    [Fact]
    public async Task InlineTreeRename_PersistsAcrossRepositoryInstancesAndIsUndoable()
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

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var movies = viewModel.TreeNodes[0].Children.Single(node => node.Key == "movies");
        viewModel.SelectedTreeNode = movies;

        viewModel.RenameItemCommand.Execute("tree");
        Assert.True(movies.IsRenaming);

        movies.RenameText = "Films";
        viewModel.CommitRename(movies);

        Assert.False(movies.IsRenaming);
        Assert.Equal("Films", movies.Title);
        Assert.Equal("Films", catalogRepository.Get("movies")!.Name);

        using (var reopened = new MainWindowViewModel(repositoryManager))
        {
            Assert.Equal("Films", reopened.TreeNodes[0].Children.Single(node => node.Key == "movies").Title);
        }

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Movies", catalogRepository.Get("movies")!.Name);
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
