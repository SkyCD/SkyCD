using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Tree;
using Xunit;

namespace SkyCD.UI.Tests;

public class TreeViewDeleteFocusTests
{
    [AvaloniaFact]
    public void DeleteTreeNode_KeepsNeighbourContainerAliveSoFocusStaysInTheTree()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(CreateCatalog());

        // The menu bar sits above the tree, matching MainWindow, so focus that escapes the tree
        // lands where the reported bug put it.
        var menuBar = new Menu();
        menuBar.Items.Add(new MenuItem { Header = "File" });

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

        var layout = new DockPanel();
        DockPanel.SetDock(menuBar, Dock.Top);
        layout.Children.Add(menuBar);
        layout.Children.Add(treeView);

        var window = new Window { Width = 400, Height = 400, Content = layout };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rootNode = viewModel.TreeNodes.Single();
        var moviesNode = rootNode.Children.Single(node => node.Key == "movies");
        var musicNode = rootNode.Children.Single(node => node.Key == "music");
        var moviesContainer = Assert.IsType<TreeViewItem>(TreeViewFocus.FindContainer(treeView, moviesNode));
        var musicContainerBefore = Assert.IsType<TreeViewItem>(TreeViewFocus.FindContainer(treeView, musicNode));

        moviesContainer.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(moviesContainer, window.FocusManager?.GetFocusedElement());

        viewModel.SelectedTreeNode = moviesNode;
        viewModel.DeleteItemCommand.Execute("tree");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("music", viewModel.SelectedTreeNode?.Key);
        Assert.Null(TreeViewFocus.FindContainer(treeView, moviesNode));
        Assert.False(moviesContainer.IsAttachedToVisualTree());

        // The neighbour keeps the container it already had, which is what gives focus somewhere to land.
        var musicContainerAfter = Assert.IsType<TreeViewItem>(TreeViewFocus.FindContainer(treeView, musicNode));
        Assert.Same(musicContainerBefore, musicContainerAfter);

        // This is what MainWindow.OnFocusTreeRequested does once the delete completes.
        Assert.True(TreeViewFocus.FocusItem(treeView, viewModel.SelectedTreeNode));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(musicContainerAfter, window.FocusManager?.GetFocusedElement());

        window.Close();
    }

    private static CatalogDocument[] CreateCatalog()
    {
        return
        [
            new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder },
            new CatalogDocument { Id = "movies", Name = "Movies", ParentId = "library", Type = CatalogDocumentType.Folder },
            new CatalogDocument { Id = "music", Name = "Music", ParentId = "library", Type = CatalogDocumentType.Folder },
            new CatalogDocument { Id = "projects", Name = "Projects", ParentId = "library", Type = CatalogDocumentType.Folder }
        ];
    }
}
