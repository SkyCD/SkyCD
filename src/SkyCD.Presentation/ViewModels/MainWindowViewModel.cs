using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BrowserItem>> browserItemsByNodeKey;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByKey;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByTitle;
    private const string DefaultStatusText = "Done.";

    public MainWindowViewModel()
    {
        var moviesNode = new BrowserTreeNode("movies", "Movies", "🎬");
        var musicNode = new BrowserTreeNode("music", "Music", "🎵");
        var projectsNode = new BrowserTreeNode("projects", "Projects", "🗂");

        var libraryNode = new BrowserTreeNode(
            "library",
            "Library",
            "📚",
            [moviesNode, musicNode, projectsNode],
            true);

        TreeNodes =
        [
            libraryNode
        ];

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        treeNodesByTitle = allTreeNodes.ToDictionary(static node => node.Title, StringComparer.OrdinalIgnoreCase);

        browserItemsByNodeKey = new Dictionary<string, IReadOnlyList<BrowserItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["library"] =
            [
                new BrowserItem("Movies", "Folder", "128 items", "📁"),
                new BrowserItem("Music", "Folder", "340 items", "📁"),
                new BrowserItem("Projects", "Folder", "56 items", "📁")
            ],
            ["movies"] =
            [
                new BrowserItem("Interstellar.mkv", "Video", "12.1 GB", "🎞"),
                new BrowserItem("Arrival.mkv", "Video", "9.4 GB", "🎞")
            ],
            ["music"] =
            [
                new BrowserItem("Classical Collection", "Folder", "42 items", "📁"),
                new BrowserItem("Concert-2025.flac", "Audio", "414 MB", "🎧")
            ],
            ["projects"] =
            [
                new BrowserItem("SkyCD v3", "Folder", "11 items", "📁"),
                new BrowserItem("Plugin Benchmarks", "Folder", "6 items", "📁")
            ]
        };

        SelectedTreeNode = TreeNodes[0];
        RefreshBrowserItemsForSelection();
    }

    public IReadOnlyList<BrowserTreeNode> TreeNodes { get; }

    public bool IsSaveEnabled => IsDirtyDocument;

    public bool IsDeleteEnabled => SelectedBrowserItem is not null;

    public string ProgressText => $"{ProgressValue}%";

    public bool IsTilesViewChecked => CurrentViewMode == BrowserViewMode.Tiles;

    public bool IsSmallIconsViewChecked => CurrentViewMode == BrowserViewMode.SmallIcons;

    public bool IsLargeIconsViewChecked => CurrentViewMode == BrowserViewMode.LargeIcons;

    public bool IsListViewChecked => CurrentViewMode == BrowserViewMode.List;

    public bool IsDetailsViewChecked => CurrentViewMode == BrowserViewMode.Details;

    public bool IsSortByNameChecked => CurrentSortMode == BrowserSortMode.Name;

    public bool IsSortByTypeChecked => CurrentSortMode == BrowserSortMode.Type;

    public bool IsIconGridMode =>
        CurrentViewMode is BrowserViewMode.Tiles or BrowserViewMode.SmallIcons or BrowserViewMode.LargeIcons;

    public bool IsListLikeMode => !IsIconGridMode;

    public bool IsTilesMode => CurrentViewMode == BrowserViewMode.Tiles;

    public double BrowserIconFontSize => CurrentViewMode switch
    {
        BrowserViewMode.SmallIcons => 14,
        BrowserViewMode.LargeIcons => 24,
        BrowserViewMode.Tiles => 20,
        _ => 16
    };

    public double BrowserGridItemWidth => CurrentViewMode switch
    {
        BrowserViewMode.SmallIcons => 120,
        BrowserViewMode.LargeIcons => 170,
        BrowserViewMode.Tiles => 300,
        _ => 220
    };

    public bool ShowDetailsColumns => CurrentViewMode == BrowserViewMode.Details;

    [ObservableProperty]
    private IReadOnlyList<BrowserItem> browserItems = [];

    [ObservableProperty]
    private BrowserTreeNode? selectedTreeNode;

    [ObservableProperty]
    private BrowserItem? selectedBrowserItem;

    [ObservableProperty]
    private BrowserViewMode currentViewMode = BrowserViewMode.Details;

    [ObservableProperty]
    private BrowserSortMode currentSortMode = BrowserSortMode.Name;

    [ObservableProperty]
    private bool isStatusBarVisible = true;

    [ObservableProperty]
    private bool isDirtyDocument;

    [ObservableProperty]
    private string statusText = DefaultStatusText;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private int progressValue;

    [RelayCommand]
    private void NewCatalog()
    {
        IsDirtyDocument = false;
        StatusText = "Created a new catalog.";
    }

    [RelayCommand]
    private void OpenCatalog()
    {
        IsDirtyDocument = true;
        StatusText = "Opened catalog.";
    }

    [RelayCommand(CanExecute = nameof(IsSaveEnabled))]
    private void SaveCatalog()
    {
        IsDirtyDocument = false;
        StatusText = "Saved catalog.";
    }

    [RelayCommand]
    private void SaveCatalogAs()
    {
        IsDirtyDocument = false;
        StatusText = "Saved catalog as.";
    }

    [RelayCommand]
    private void OpenProperties()
    {
        StatusText = "Properties dialog is not implemented yet.";
    }

    [RelayCommand]
    private void ExitApplication()
    {
        StatusText = "Exit requested.";
    }

    [RelayCommand]
    private void AddItem()
    {
        IsDirtyDocument = true;
        StatusText = "Add dialog is not implemented yet.";
    }

    [RelayCommand(CanExecute = nameof(IsDeleteEnabled))]
    private void DeleteItem()
    {
        if (SelectedBrowserItem is null)
        {
            return;
        }

        IsDirtyDocument = true;
        StatusText = $"Deleted {SelectedBrowserItem.Name}.";
    }

    [RelayCommand]
    private void OpenOptions()
    {
        StatusText = "Options dialog is not implemented yet.";
    }

    [RelayCommand]
    private void OpenProjectWebsite()
    {
        StatusText = "Open SourceForge project website.";
    }

    [RelayCommand]
    private void OpenGithubArea()
    {
        StatusText = "Open GitHub project area.";
    }

    [RelayCommand]
    private void OpenAbout()
    {
        StatusText = "About dialog is not implemented yet.";
    }

    [RelayCommand(CanExecute = nameof(CanExpandSelection))]
    private void ExpandSelection(string? context)
    {
        if (!TryResolveContextNode(context, out var targetNode))
        {
            return;
        }

        targetNode.IsExpanded = true;
        SelectedTreeNode = targetNode;
        StatusText = $"Expanded {targetNode.Title}.";
    }

    [RelayCommand(CanExecute = nameof(CanCollapseSelection))]
    private void CollapseSelection(string? context)
    {
        if (!TryResolveContextNode(context, out var targetNode))
        {
            return;
        }

        targetNode.IsExpanded = false;
        SelectedTreeNode = targetNode;
        StatusText = $"Collapsed {targetNode.Title}.";
    }

    [RelayCommand]
    private void SetViewMode(string modeKey)
    {
        if (Enum.TryParse<BrowserViewMode>(modeKey, true, out var mode))
        {
            CurrentViewMode = mode;
            StatusText = $"View mode: {GetViewModeDisplayName(mode)}.";
        }
    }

    [RelayCommand]
    private void SetSortMode(string sortKey)
    {
        if (Enum.TryParse<BrowserSortMode>(sortKey, true, out var sortMode))
        {
            CurrentSortMode = sortMode;
            RefreshBrowserItemsForSelection();
            StatusText = $"Arrange icons by: {sortMode}.";
        }
    }

    [RelayCommand]
    private void ToggleStatusBar()
    {
        IsStatusBarVisible = !IsStatusBarVisible;
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshBrowserItemsForSelection();
        StatusText = DefaultStatusText;
    }

    private static string GetViewModeDisplayName(BrowserViewMode viewMode)
    {
        return viewMode switch
        {
            BrowserViewMode.SmallIcons => "Small Icons",
            BrowserViewMode.LargeIcons => "Large Icons",
            _ => viewMode.ToString()
        };
    }

    private static IEnumerable<BrowserTreeNode> FlattenNodes(IEnumerable<BrowserTreeNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenNodes(root.Children))
            {
                yield return child;
            }
        }
    }

    private bool CanExpandSelection(string? context)
    {
        return TryResolveContextNode(context, out _);
    }

    private bool CanCollapseSelection(string? context)
    {
        return TryResolveContextNode(context, out _);
    }

    private bool TryResolveContextNode(string? context, out BrowserTreeNode targetNode)
    {
        if (string.Equals(context, "list", StringComparison.OrdinalIgnoreCase) &&
            TryResolveNodeFromBrowserSelection(out targetNode))
        {
            return true;
        }

        if (SelectedTreeNode is not null)
        {
            targetNode = SelectedTreeNode;
            return true;
        }

        return TryResolveNodeFromBrowserSelection(out targetNode);
    }

    private bool TryResolveNodeFromBrowserSelection(out BrowserTreeNode targetNode)
    {
        if (SelectedBrowserItem is not null &&
            SelectedBrowserItem.Type.Equals("Folder", StringComparison.OrdinalIgnoreCase))
        {
            if (treeNodesByTitle.TryGetValue(SelectedBrowserItem.Name, out targetNode))
            {
                return true;
            }

            var normalizedKey = SelectedBrowserItem.Name.Replace(" ", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            if (treeNodesByKey.TryGetValue(normalizedKey, out targetNode))
            {
                return true;
            }
        }

        targetNode = null!;
        return false;
    }

    private void RefreshBrowserItemsForSelection()
    {
        var previouslySelectedName = SelectedBrowserItem?.Name;
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        if (!browserItemsByNodeKey.TryGetValue(nodeKey, out var items))
        {
            BrowserItems = [];
            SelectedBrowserItem = null;
            return;
        }

        var refreshedItems = CurrentSortMode switch
        {
            BrowserSortMode.Type => items.OrderBy(static item => item.Type)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => items.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };

        BrowserItems = refreshedItems;
        SelectedBrowserItem = refreshedItems.FirstOrDefault(item =>
                                 item.Name.Equals(previouslySelectedName, StringComparison.OrdinalIgnoreCase))
                             ?? refreshedItems.FirstOrDefault();
    }

    partial void OnSelectedTreeNodeChanged(BrowserTreeNode? value)
    {
        RefreshBrowserItemsForSelection();
        ExpandSelectionCommand.NotifyCanExecuteChanged();
        CollapseSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBrowserItemChanged(BrowserItem? value)
    {
        OnPropertyChanged(nameof(IsDeleteEnabled));
        DeleteItemCommand.NotifyCanExecuteChanged();
        ExpandSelectionCommand.NotifyCanExecuteChanged();
        CollapseSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentViewModeChanged(BrowserViewMode value)
    {
        OnPropertyChanged(nameof(IsTilesViewChecked));
        OnPropertyChanged(nameof(IsSmallIconsViewChecked));
        OnPropertyChanged(nameof(IsLargeIconsViewChecked));
        OnPropertyChanged(nameof(IsListViewChecked));
        OnPropertyChanged(nameof(IsDetailsViewChecked));
        OnPropertyChanged(nameof(IsIconGridMode));
        OnPropertyChanged(nameof(IsListLikeMode));
        OnPropertyChanged(nameof(IsTilesMode));
        OnPropertyChanged(nameof(BrowserIconFontSize));
        OnPropertyChanged(nameof(BrowserGridItemWidth));
        OnPropertyChanged(nameof(ShowDetailsColumns));
    }

    partial void OnCurrentSortModeChanged(BrowserSortMode value)
    {
        OnPropertyChanged(nameof(IsSortByNameChecked));
        OnPropertyChanged(nameof(IsSortByTypeChecked));
    }

    partial void OnIsDirtyDocumentChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSaveEnabled));
        SaveCatalogCommand.NotifyCanExecuteChanged();
    }

    partial void OnProgressValueChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
    }
}
