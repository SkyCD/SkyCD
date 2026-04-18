using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BrowserItem>> browserItemsByNodeKey;

    public MainWindowViewModel()
    {
        var moviesNode = new BrowserTreeNode("movies", "Movies");
        var musicNode = new BrowserTreeNode("music", "Music");
        var projectsNode = new BrowserTreeNode("projects", "Projects");

        TreeNodes =
        [
            new BrowserTreeNode(
                "library",
                "Library",
                [moviesNode, musicNode, projectsNode])
        ];

        browserItemsByNodeKey = new Dictionary<string, IReadOnlyList<BrowserItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["library"] =
            [
                new BrowserItem("Movies", "Folder", "128 items"),
                new BrowserItem("Music", "Folder", "340 items"),
                new BrowserItem("Projects", "Folder", "56 items")
            ],
            ["movies"] =
            [
                new BrowserItem("Interstellar.mkv", "Video", "12.1 GB"),
                new BrowserItem("Arrival.mkv", "Video", "9.4 GB")
            ],
            ["music"] =
            [
                new BrowserItem("Classical Collection", "Folder", "42 items"),
                new BrowserItem("Concert-2025.flac", "Audio", "414 MB")
            ],
            ["projects"] =
            [
                new BrowserItem("SkyCD v3", "Folder", "11 items"),
                new BrowserItem("Plugin Benchmarks", "Folder", "6 items")
            ]
        };

        SelectedTreeNode = TreeNodes[0];
        RefreshBrowserItemsForSelection();
    }

    public IReadOnlyList<BrowserTreeNode> TreeNodes { get; }

    public bool IsSaveEnabled => IsDirtyDocument;

    public string ProgressText => $"{ProgressValue}%";

    public bool IsTilesViewChecked => CurrentViewMode == BrowserViewMode.Tiles;

    public bool IsSmallIconsViewChecked => CurrentViewMode == BrowserViewMode.SmallIcons;

    public bool IsLargeIconsViewChecked => CurrentViewMode == BrowserViewMode.LargeIcons;

    public bool IsListViewChecked => CurrentViewMode == BrowserViewMode.List;

    public bool IsDetailsViewChecked => CurrentViewMode == BrowserViewMode.Details;

    public bool IsSortByNameChecked => CurrentSortMode == BrowserSortMode.Name;

    public bool IsSortByTypeChecked => CurrentSortMode == BrowserSortMode.Type;

    [ObservableProperty]
    private IReadOnlyList<BrowserItem> browserItems = [];

    [ObservableProperty]
    private BrowserTreeNode? selectedTreeNode;

    [ObservableProperty]
    private BrowserViewMode currentViewMode = BrowserViewMode.Details;

    [ObservableProperty]
    private BrowserSortMode currentSortMode = BrowserSortMode.Name;

    [ObservableProperty]
    private bool isStatusBarVisible = true;

    [ObservableProperty]
    private bool isDirtyDocument;

    [ObservableProperty]
    private string statusText = "Done.";

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private int progressValue;

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
        StatusText = "Done.";
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

    private void RefreshBrowserItemsForSelection()
    {
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        if (!browserItemsByNodeKey.TryGetValue(nodeKey, out var items))
        {
            BrowserItems = [];
            return;
        }

        BrowserItems = CurrentSortMode switch
        {
            BrowserSortMode.Type => items.OrderBy(static item => item.Type)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => items.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    partial void OnSelectedTreeNodeChanged(BrowserTreeNode? value)
    {
        RefreshBrowserItemsForSelection();
    }

    partial void OnCurrentViewModeChanged(BrowserViewMode value)
    {
        OnPropertyChanged(nameof(IsTilesViewChecked));
        OnPropertyChanged(nameof(IsSmallIconsViewChecked));
        OnPropertyChanged(nameof(IsLargeIconsViewChecked));
        OnPropertyChanged(nameof(IsListViewChecked));
        OnPropertyChanged(nameof(IsDetailsViewChecked));
    }

    partial void OnCurrentSortModeChanged(BrowserSortMode value)
    {
        OnPropertyChanged(nameof(IsSortByNameChecked));
        OnPropertyChanged(nameof(IsSortByTypeChecked));
    }

    partial void OnIsDirtyDocumentChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSaveEnabled));
    }

    partial void OnProgressValueChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
    }
}
