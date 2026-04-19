using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BrowserItem>> browserItemsByNodeKey;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByKey;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByTitle;
    private readonly Dictionary<string, string> commentsByObjectKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> statusTransitions = [];
    private readonly List<int> progressTransitions = [];
    private const string DefaultStatusText = "Done.";

    public event EventHandler? AddToListRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler? ExitRequested;

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

    public bool IsSortBySizeChecked => CurrentSortMode == BrowserSortMode.Size;

    public bool IsDetailsMode => CurrentViewMode == BrowserViewMode.Details;

    public bool IsListMode => CurrentViewMode == BrowserViewMode.List;

    public bool IsSmallIconsMode => CurrentViewMode == BrowserViewMode.SmallIcons;

    public bool IsLargeIconsMode => CurrentViewMode == BrowserViewMode.LargeIcons;

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

    public double BrowserGridItemHeight => CurrentViewMode switch
    {
        BrowserViewMode.LargeIcons => 90,
        BrowserViewMode.Tiles => 80,
        _ => 60
    };

    public bool ShowDetailsColumns => CurrentViewMode == BrowserViewMode.Details;

    public IReadOnlyList<string> StatusTransitions => statusTransitions;

    public IReadOnlyList<int> ProgressTransitions => progressTransitions;

    [ObservableProperty]
    private IReadOnlyList<BrowserItem> browserItems = [];

    [ObservableProperty]
    private BrowserTreeNode? selectedTreeNode;

    [ObservableProperty]
    private BrowserItem? selectedBrowserItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTilesViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsSmallIconsViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsLargeIconsViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsListViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsDetailsViewChecked))]
    [NotifyPropertyChangedFor(nameof(IsDetailsMode))]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(IsSmallIconsMode))]
    [NotifyPropertyChangedFor(nameof(IsLargeIconsMode))]
    [NotifyPropertyChangedFor(nameof(IsIconGridMode))]
    [NotifyPropertyChangedFor(nameof(IsListLikeMode))]
    [NotifyPropertyChangedFor(nameof(IsTilesMode))]
    [NotifyPropertyChangedFor(nameof(BrowserIconFontSize))]
    [NotifyPropertyChangedFor(nameof(BrowserGridItemWidth))]
    [NotifyPropertyChangedFor(nameof(BrowserGridItemHeight))]
    [NotifyPropertyChangedFor(nameof(ShowDetailsColumns))]
    private BrowserViewMode currentViewMode = BrowserViewMode.Details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortByNameChecked))]
    [NotifyPropertyChangedFor(nameof(IsSortByTypeChecked))]
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

    [ObservableProperty]
    private BrowserItem? clipboardItem;

    public bool IsCopyEnabled => SelectedBrowserItem is not null;

    public bool IsPasteEnabled => ClipboardItem is not null;

    public bool IsCutEnabled => SelectedBrowserItem is not null;

    [RelayCommand]
    private void NewCatalog()
    {
        IsDirtyDocument = false;
        StatusText = "Created a new catalog.";
    }

    [RelayCommand]
    private void OpenCatalog()
    {
        StartOperation("Loading catalog...");
        SetProgress(35, "Parsing catalog...");
        SetProgress(80, "Updating browser...");
        CompleteOperation();

        IsDirtyDocument = true;
    }

    [RelayCommand(CanExecute = nameof(IsSaveEnabled))]
    private void SaveCatalog()
    {
        StartOperation("Saving catalog...");
        SetProgress(40, "Parsing items...");
        SetProgress(90, "Updating indexes...");
        CompleteOperation();

        IsDirtyDocument = false;
    }

    [RelayCommand]
    private void SaveCatalogAs()
    {
        StartOperation("Saving catalog...");
        SetProgress(50, "Parsing items...");
        SetProgress(95, "Updating indexes...");
        CompleteOperation();

        IsDirtyDocument = false;
    }

    [RelayCommand]
    private void OpenProperties()
    {
        if (!TryBuildPropertiesDialog(out var dialog))
        {
            StatusText = "Unknown selected object.";
            return;
        }

        PropertiesRequested?.Invoke(this, new PropertiesDialogRequestedEventArgs
        {
            Dialog = dialog,
            Complete = (accepted, comments) =>
            {
                if (!accepted)
                {
                    return;
                }

                commentsByObjectKey[dialog.ObjectKey] = comments;
                IsDirtyDocument = true;
                StatusText = DefaultStatusText;
            }
        });
    }

    [RelayCommand]
    private void ExitApplication()
    {
        StatusText = "Exit requested.";
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddItem()
    {
        AddToListRequested?.Invoke(this, EventArgs.Empty);
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
        if (OptionsRequested is null)
        {
            StatusText = "Options dialog is not implemented yet.";
            return;
        }

        var dialog = new OptionsDialogViewModel(["English", "Lithuanian"]);
        OptionsRequested.Invoke(this, new OptionsDialogRequestedEventArgs
        {
            Dialog = dialog,
            Complete = (accepted, pluginPath, language) =>
            {
                if (!accepted)
                {
                    return;
                }

                StatusText = $"Options saved (Language: {language}).";
            }
        });
    }

    [RelayCommand]
    private void OpenProjectWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://sourceforge.net/projects/skycd/",
                UseShellExecute = true
            });
            StatusText = "Opening SourceForge project website...";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open SourceForge website: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenGithubArea()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/SkyCD/SkyCD",
                UseShellExecute = true
            });
            StatusText = "Opening GitHub project area...";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open GitHub area: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        if (AboutRequested is null)
        {
            StatusText = "About dialog is not implemented yet.";
            return;
        }

        AboutRequested.Invoke(this, EventArgs.Empty);
        StatusText = DefaultStatusText;
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
        StartOperation("Updating view...");
        SetProgress(60, "Parsing catalog...");
        RefreshBrowserItemsForSelection();
        CompleteOperation();
    }

    [RelayCommand(CanExecute = nameof(IsCopyEnabled))]
    private void Copy()
    {
        if (SelectedBrowserItem is null)
        {
            return;
        }

        ClipboardItem = SelectedBrowserItem;
        StatusText = $"Copied {SelectedBrowserItem.Name}.";
    }

    [RelayCommand(CanExecute = nameof(IsPasteEnabled))]
    private void Paste()
    {
        if (ClipboardItem is null)
        {
            return;
        }

        // In a real implementation, this would add a copy of the item to the current location
        // For now, we'll just show a status message
        IsDirtyDocument = true;
        StatusText = $"Pasted {ClipboardItem.Name}.";
    }

    [RelayCommand(CanExecute = nameof(IsCutEnabled))]
    private void Cut()
    {
        if (SelectedBrowserItem is null)
        {
            return;
        }

        ClipboardItem = SelectedBrowserItem;
        IsDirtyDocument = true;
        StatusText = $"Cut {SelectedBrowserItem.Name}.";
    }

    public void ApplySessionState(BrowserViewMode viewMode, BrowserSortMode sortMode, bool isStatusBarVisible)
    {
        CurrentViewMode = viewMode;
        CurrentSortMode = sortMode;
        IsStatusBarVisible = isStatusBarVisible;
        RefreshBrowserItemsForSelection();
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

    private bool TryResolveContextNode(string? context, [NotNullWhen(true)] out BrowserTreeNode? targetNode)
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

    private bool TryResolveNodeFromBrowserSelection([NotNullWhen(true)] out BrowserTreeNode? targetNode)
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

        targetNode = null;
        return false;
    }

    private bool TryBuildPropertiesDialog([NotNullWhen(true)] out PropertiesDialogViewModel? dialog)
    {
        if (SelectedBrowserItem is not null)
        {
            var objectKey = GetBrowserItemObjectKey(SelectedBrowserItem);
            var comments = GetObjectComments(objectKey);
            var nodeTitle = SelectedTreeNode?.Title ?? "Library";

            var infoProperties = new List<PropertiesInfoItem>
            {
                new("Type", SelectedBrowserItem.Type),
                new("Size", SelectedBrowserItem.Size),
                new("Location", nodeTitle)
            };

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedBrowserItem.Name,
                SelectedBrowserItem.IconGlyph,
                comments,
                infoProperties);
            return true;
        }

        if (SelectedTreeNode is not null)
        {
            var objectKey = GetTreeNodeObjectKey(SelectedTreeNode);
            var comments = GetObjectComments(objectKey);

            var infoProperties = new List<PropertiesInfoItem>
            {
                new("Type", "Folder"),
                new("Children", SelectedTreeNode.Children.Count.ToString())
            };

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedTreeNode.Title,
                SelectedTreeNode.IconGlyph,
                comments,
                infoProperties);
            return true;
        }

        dialog = null;
        return false;
    }

    private string GetObjectComments(string objectKey)
    {
        return commentsByObjectKey.TryGetValue(objectKey, out var comments)
            ? comments
            : string.Empty;
    }

    private string GetBrowserItemObjectKey(BrowserItem item)
    {
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        return $"item:{nodeKey}:{item.Name}";
    }

    private static string GetTreeNodeObjectKey(BrowserTreeNode node)
    {
        return $"tree:{node.Key}";
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
            BrowserSortMode.Size => items.OrderBy(static item => item.Size, StringComparer.OrdinalIgnoreCase)
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

    private void StartOperation(string initialStatus)
    {
        statusTransitions.Clear();
        progressTransitions.Clear();
        IsProgressVisible = true;
        ProgressValue = 0;
        TrackProgress(0);
        SetStatus(initialStatus);
    }

    private void SetProgress(int value, string? status = null)
    {
        ProgressValue = Math.Clamp(value, 0, 100);
        TrackProgress(ProgressValue);
        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }
    }

    private void CompleteOperation()
    {
        SetProgress(100);
        SetStatus(DefaultStatusText);
        IsProgressVisible = false;
        ProgressValue = 0;
        TrackProgress(0);
    }

    private void SetStatus(string value)
    {
        StatusText = value;
        statusTransitions.Add(value);
    }

    private void TrackProgress(int value)
    {
        progressTransitions.Add(value);
    }

    partial void OnSelectedBrowserItemChanged(BrowserItem? value)
    {
        OnPropertyChanged(nameof(IsDeleteEnabled));
        OnPropertyChanged(nameof(IsCopyEnabled));
        OnPropertyChanged(nameof(IsCutEnabled));
        DeleteItemCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(IsDetailsMode));
        OnPropertyChanged(nameof(IsListMode));
        OnPropertyChanged(nameof(IsSmallIconsMode));
        OnPropertyChanged(nameof(IsLargeIconsMode));
        OnPropertyChanged(nameof(IsIconGridMode));
        OnPropertyChanged(nameof(IsListLikeMode));
        OnPropertyChanged(nameof(IsTilesMode));
        OnPropertyChanged(nameof(BrowserIconFontSize));
        OnPropertyChanged(nameof(BrowserGridItemWidth));
        OnPropertyChanged(nameof(BrowserGridItemHeight));
        OnPropertyChanged(nameof(ShowDetailsColumns));
    }

    partial void OnCurrentSortModeChanged(BrowserSortMode value)
    {
        OnPropertyChanged(nameof(IsSortByNameChecked));
        OnPropertyChanged(nameof(IsSortByTypeChecked));
        OnPropertyChanged(nameof(IsSortBySizeChecked));
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

    partial void OnClipboardItemChanged(BrowserItem? value)
    {
        OnPropertyChanged(nameof(IsPasteEnabled));
        PasteCommand.NotifyCanExecuteChanged();
    }
}
