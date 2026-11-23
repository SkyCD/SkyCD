using System;
using System.Collections.Generic;
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
using SkyCD.TestSupport;
using SkyCD.UI.Controls.Tree;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Reproduces catalog undo through the real <see cref="TreeView"/> wiring, because the view model can report a
/// restored subtree while the pane the user is looking at stays unchanged.
/// </summary>
public sealed class TreeUndoViewTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();
    private DryIoc.Container? container;
    private bool databaseClosed;

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

    /// <summary>
    /// Mirrors the shape of a real imported catalog: two roots, one holding several hundred children, most
    /// of them scrolled out of view and therefore never realized. A small tree realizes every container, so
    /// it cannot show whether a rebuild survives virtualization.
    /// </summary>
    [AvaloniaFact]
    public async Task UndoAfterTreeDelete_AtRealCatalogScale_RestoresTheNode()
    {
        var catalog = CreateCatalog();
        SeedWide(catalog, childCount: 507, victimIndex: 250);

        using var viewModel = new MainWindowViewModel(catalog.Owner);
        var treeView = BuildTreeView(viewModel);
        var window = new Window { Width = 400, Height = 400, Content = treeView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var ftpRoot = FindNode(viewModel, "ftp-root");
        Assert.NotNull(ftpRoot);
        Assert.Equal(507, ftpRoot!.Children.Count);

        var victim = FindNode(viewModel, "victim-250");
        Assert.NotNull(victim);

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = victim;
        Dispatcher.UIThread.RunJobs();

        viewModel.DeleteItemCommand.Execute("tree");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Deleted VICTIM-250.", viewModel.StatusText);
        Assert.True(viewModel.CanUndo);
        Assert.Null(FindNode(viewModel, "victim-250"));
        Assert.Equal(506, FindNode(viewModel, "ftp-root")!.Children.Count);

        // Moving elsewhere before undo must not leave the restored folder hidden.
        FindNode(viewModel, "ftp-root")!.IsExpanded = false;
        viewModel.SelectedTreeNode = FindNode(viewModel, "library");

        await viewModel.UndoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Undone: Delete VICTIM-250.", viewModel.StatusText);

        var restored = FindNode(viewModel, "victim-250");
        Assert.True(restored is not null, "the restored leaf is missing from the rebuilt tree");
        Assert.Equal(507, FindNode(viewModel, "ftp-root")!.Children.Count);
        Assert.True(
            FindNode(viewModel, "ftp-root")!.Children.Any(child => child.Key == "victim-250"),
            "the restored leaf is not re-attached under its original parent");
        Assert.True(FindNode(viewModel, "ftp-root")!.IsExpanded, "the parent chain is collapsed, hiding the node");
        Assert.Same(restored, viewModel.SelectedTreeNode);

        window.Close();
    }

    private static bool IsContainerRealized(TreeView treeView, BrowserTreeNode node) =>
        TreeViewFocus.FindContainer(treeView, node) is not null;

    /// <summary>
    /// Expands the way a user does — through the realized container only — and reports whether the TwoWay
    /// binding writes it back to the model. <see cref="MainWindowViewModel"/> preserves expansion across a
    /// tree rebuild by reading the model, so if this write-back does not happen, undo collapses the tree.
    /// </summary>
    [AvaloniaFact]
    public void ExpandingThroughTheContainer_WritesBackToTheModelSoARebuildKeepsIt()
    {
        var catalog = CreateCatalog();
        Seed(catalog);

        using var viewModel = new MainWindowViewModel(catalog.Owner);
        var treeView = BuildTreeView(viewModel);
        var window = new Window { Width = 400, Height = 400, Content = treeView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var library = FindNode(viewModel, "library");
        Assert.NotNull(library);

        // Roots are built expanded, so collapse first to get a known starting state.
        library!.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();

        var libraryContainer = TreeViewFocus.FindContainer(treeView, library) as TreeViewItem;
        Assert.NotNull(libraryContainer);
        Assert.False(libraryContainer!.IsExpanded);

        libraryContainer.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(library.IsExpanded, "the TwoWay IsExpanded binding did not write back to the model");

        var movies = FindNode(viewModel, "movies");
        Assert.NotNull(movies);
        var moviesContainer = TreeViewFocus.FindContainer(treeView, movies) as TreeViewItem;
        Assert.NotNull(moviesContainer);

        moviesContainer!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(movies!.IsExpanded, "a nested node's expansion did not write back to the model");

        window.Close();
    }

    private static TreeView BuildTreeView(MainWindowViewModel viewModel)
    {
        var treeView = new TreeView
        {
            ItemsSource = viewModel.TreeNodes,
            ItemTemplate = new FuncTreeDataTemplate<BrowserTreeNode>(
                (node, _) => new TextBlock { Text = node.Title },
                node => node.Children)
        };

        // MainWindow.axaml also binds SelectedItem two-way. RefreshTreeNodes clears and refills the
        // collection, which drives SelectedItem to null and writes that straight back into the view model,
        // so omitting this binding hides re-entrancy the real window has to survive.
        treeView.Bind(TreeView.SelectedItemProperty,
            new Binding(nameof(MainWindowViewModel.SelectedTreeNode))
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay
            });

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

    /// <summary>
    /// Two roots, the second holding <paramref name="childCount"/> leaf folders, so most of the tree is
    /// outside the viewport and never realized — the shape a real imported catalog has.
    /// </summary>
    private static void SeedWide(
        (RepositoryManager Owner, CatalogDocumentRepository Catalog) catalog,
        int childCount,
        int victimIndex)
    {
        Save(catalog.Catalog, new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder });
        Save(catalog.Catalog, new CatalogDocument
        {
            Id = "gamez",
            Name = "[Gamez]",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        });
        Save(catalog.Catalog, new CatalogDocument
        {
            Id = "ftp-root",
            Name = "demo scene by artists",
            Type = CatalogDocumentType.NetworkFolder
        });

        for (var i = 0; i < childCount; i++)
        {
            Save(catalog.Catalog, new CatalogDocument
            {
                Id = $"victim-{i}",
                Name = $"VICTIM-{i}",
                ParentId = "ftp-root",
                Type = CatalogDocumentType.Folder
            });
        }

        // The victim sits far enough down that its container is not realized in a 400px window.
        Assert.True(victimIndex < childCount);
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

        // The sandbox deletes its directory, so the database and container above are released first.
        sandbox.Dispose();
    }
}
