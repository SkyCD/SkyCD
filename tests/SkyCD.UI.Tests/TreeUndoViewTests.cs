using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Tree;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Reproduces catalog undo through the real <see cref="TreeView"/> wiring, because the view model can report a
/// restored subtree while the pane the user is looking at stays unchanged.
/// </summary>
public sealed class TreeUndoViewTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-tree-undo-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");
    private DryIoc.Container? container;
    private bool databaseClosed;

    public TreeUndoViewTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    [AvaloniaFact]
    public async Task UndoAfterTreeDelete_BringsTheNodeBackInTheTreeView()
    {
        var catalog = CreateCatalog();
        Seed(catalog);

        using var viewModel = new MainWindowViewModel(catalog.Owner);
        var treeView = BuildTreeView(viewModel);
        var window = new Window { Width = 400, Height = 400, Content = treeView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Expand(treeView, viewModel, "library");
        Expand(treeView, viewModel, "movies");
        Dispatcher.UIThread.RunJobs();

        var deletedNode = FindNode(viewModel, "scifi");
        Assert.NotNull(deletedNode);
        viewModel.SelectedTreeNode = deletedNode;
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(TreeViewFocus.FindContainer(treeView, deletedNode));

        viewModel.DeleteItemCommand.Execute("tree");
        Dispatcher.UIThread.RunJobs();

        Assert.Null(FindNode(viewModel, "scifi"));

        await viewModel.UndoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var restored = FindNode(viewModel, "scifi");

        // The view model is only half the story: the pane has to realize a container for the node again,
        // under an expanded parent, or the user sees an unchanged tree even though the record came back.
        Assert.NotNull(restored);
        Assert.NotNull(FindNode(viewModel, "movies"));
        Assert.True(IsContainerRealized(treeView, restored!), "the restored node has no realized TreeViewItem");

        window.Close();
    }

    [AvaloniaFact]
    public async Task BrowserDelete_DetachesTheNodeFromTheTreeViewSoNoGhostIsLeftBehind()
    {
        var catalog = CreateCatalog();
        Seed(catalog);

        using var viewModel = new MainWindowViewModel(catalog.Owner);
        var treeView = BuildTreeView(viewModel);
        var window = new Window { Width = 400, Height = 400, Content = treeView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Expand(treeView, viewModel, "library");
        Expand(treeView, viewModel, "movies");
        Dispatcher.UIThread.RunJobs();

        var folderNode = FindNode(viewModel, "scifi");
        Assert.NotNull(folderNode);
        Assert.NotNull(TreeViewFocus.FindContainer(treeView, folderNode));

        viewModel.SelectedTreeNode = FindNode(viewModel, "movies");
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == "scifi");
        Dispatcher.UIThread.RunJobs();

        viewModel.DeleteItemCommand.Execute("list");
        Dispatcher.UIThread.RunJobs();

        // A folder deleted in the browser pane is a tree node too. Leaving its container realized means the
        // tree keeps offering a node whose record is gone, and deleting that ghost next has nothing
        // authoritative left to snapshot.
        Assert.Null(FindNode(viewModel, "scifi"));
        Assert.Null(TreeViewFocus.FindContainer(treeView, folderNode!));
        Assert.NotNull(TreeViewFocus.FindContainer(treeView, FindNode(viewModel, "drama")!));

        await viewModel.UndoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var restored = FindNode(viewModel, "scifi");
        Assert.NotNull(restored);
        Assert.True(IsContainerRealized(treeView, restored!), "the restored node has no realized TreeViewItem");

        window.Close();
    }

    private static bool IsContainerRealized(TreeView treeView, BrowserTreeNode node) =>
        TreeViewFocus.FindContainer(treeView, node) is not null;

    private static TreeView BuildTreeView(MainWindowViewModel viewModel)
    {
        var treeView = new TreeView
        {
            ItemsSource = viewModel.TreeNodes,
            ItemTemplate = new FuncTreeDataTemplate<BrowserTreeNode>(
                (node, _) => new TextBlock { Text = node.Title },
                node => node.Children)
        };

        // Mirrors MainWindow.axaml, where TreeViewItem.IsExpanded follows BrowserTreeNode.IsExpanded.
        var expansionStyle = new Style(x => x.OfType<TreeViewItem>());
        expansionStyle.Setters.Add(new Setter(
            TreeViewItem.IsExpandedProperty,
            new Binding(nameof(BrowserTreeNode.IsExpanded)) { Mode = BindingMode.TwoWay }));
        treeView.Styles.Add(expansionStyle);

        return treeView;
    }

    private static void Expand(TreeView treeView, MainWindowViewModel viewModel, string key)
    {
        var node = FindNode(viewModel, key);
        Assert.NotNull(node);
        node!.IsExpanded = true;

        var container = TreeViewFocus.FindContainer(treeView, node) as TreeViewItem;
        if (container is not null)
        {
            container.IsExpanded = true;
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static BrowserTreeNode? FindNode(MainWindowViewModel viewModel, string key) =>
        Flatten(viewModel.TreeNodes).FirstOrDefault(node => node.Key == key);

    private static IEnumerable<BrowserTreeNode> Flatten(IEnumerable<BrowserTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private (RepositoryManager Owner, CatalogDocumentRepository Catalog) CreateCatalog()
    {
        var created = new DryIoc.Container();
        container = created;
        new CouchbaseServiceRegistrator().RegisterServices(created);
        var repositories = created.Resolve<RepositoryManager>();

        var options = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();
        var appOptions = options.GetOrCreateAppOptions();
        appOptions.AppStartCount = 99;
        options.Save(AppOptionsDocument.DocumentId, appOptions);

        return (repositories, (CatalogDocumentRepository)repositories.For<CatalogDocument>());
    }

    private static void Seed((RepositoryManager Owner, CatalogDocumentRepository Catalog) catalog)
    {
        Save(catalog.Catalog, new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder });
        Save(catalog.Catalog, new CatalogDocument { Id = "movies", Name = "Movies", ParentId = "library", Type = CatalogDocumentType.Folder });
        Save(catalog.Catalog, new CatalogDocument { Id = "music", Name = "Music", ParentId = "library", Type = CatalogDocumentType.Folder });
        Save(catalog.Catalog, new CatalogDocument { Id = "scifi", Name = "Sci-Fi", ParentId = "movies", Type = CatalogDocumentType.Folder });
        Save(catalog.Catalog, new CatalogDocument { Id = "drama", Name = "Drama", ParentId = "movies", Type = CatalogDocumentType.Folder });
        Save(catalog.Catalog, new CatalogDocument
        {
            Id = "interstellar",
            Name = "Interstellar.mkv",
            ParentId = "scifi",
            Type = CatalogDocumentType.File,
            Size = 4096
        });
    }

    private static void Save(CatalogDocumentRepository catalog, CatalogDocument document) =>
        catalog.Save(document.Id, document);

    public void Dispose()
    {
        try
        {
            if (!databaseClosed && container is not null)
            {
                databaseClosed = true;
                container.Resolve<DatabaseManager>().GetDatabase().Close();
            }
        }
        catch (Exception)
        {
            // Already closed.
        }

        container?.Dispose();

        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        try
        {
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // The database file can stay locked until the process releases it.
        }
    }
}
