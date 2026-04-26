using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Abstractions.Localization;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IBrowserDataStore browserDataStore;
    private readonly II18nService i18n;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByKey;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByTitle;
    private readonly Dictionary<string, string> commentsByObjectKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BrowserItem>> addedItemsByNodeKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> deletedItemNamesByNodeKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> renamedBrowserItemNamesByNodeKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> statusTransitions = [];
    private readonly List<int> progressTransitions = [];
    private readonly string defaultStatusText;

    public event EventHandler? AddToListRequested;
    public event EventHandler? NewCatalogRequested;
    public event EventHandler? OpenCatalogRequested;
    public event EventHandler? SaveCatalogAsRequested;
    public event EventHandler? SaveCatalogRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler? ExitRequested;

    public MainWindowViewModel()
        : this(new InMemoryBrowserDataStore(), new I18nService())
    {
    }

    public MainWindowViewModel(IBrowserDataStore browserDataStore)
        : this(browserDataStore, new I18nService())
    {
    }

    public MainWindowViewModel(IBrowserDataStore browserDataStore, II18nService i18n)
    {
        this.browserDataStore = browserDataStore ?? throw new ArgumentNullException(nameof(browserDataStore));
        this.i18n = i18n ?? throw new ArgumentNullException(nameof(i18n));
        defaultStatusText = T("common.done");
        statusText = defaultStatusText;
        TreeNodes = browserDataStore.GetTreeNodes();

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        treeNodesByTitle = allTreeNodes.ToDictionary(static node => node.Title, StringComparer.OrdinalIgnoreCase);
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
    }

    public IReadOnlyList<BrowserTreeNode> TreeNodes { get; }

    public bool IsSaveEnabled => IsDirtyDocument;

    public bool IsDeleteEnabled => SelectedBrowserItem is not null;

    public bool IsPropertiesEnabled => SelectedBrowserItem is not null || SelectedTreeNode is not null;

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
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private int progressValue;

    [ObservableProperty]
    private BrowserItem? clipboardItem;

    [ObservableProperty]
    private string? currentCatalogPath;

    public bool IsCopyEnabled => SelectedBrowserItem is not null;

    public bool IsPasteEnabled => ClipboardItem is not null;

    public bool IsCutEnabled => SelectedBrowserItem is not null;

    [RelayCommand]
    private void NewCatalog()
    {
        if (NewCatalogRequested is not null)
        {
            NewCatalogRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        CompleteNewCatalog();
    }

    public void CompleteNewCatalog()
    {
        commentsByObjectKey.Clear();
        addedItemsByNodeKey.Clear();
        deletedItemNamesByNodeKey.Clear();
        renamedBrowserItemNamesByNodeKey.Clear();
        CurrentCatalogPath = null;
        SelectedBrowserItem = null;
        ClipboardItem = null;
        RefreshBrowserItemsForSelection();
        IsDirtyDocument = false;
        StatusText = T("status.created_new_catalog");
    }

    [RelayCommand]
    private void OpenCatalog()
    {
        if (OpenCatalogRequested is not null)
        {
            OpenCatalogRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        CompleteOpenCatalog();
    }

    public void CompleteOpenCatalog()
    {
        StartOperation(T("status.loading_catalog"));
        SetProgress(35, T("status.parsing_catalog"));
        SetProgress(80, T("status.updating_browser"));
        CompleteOperation();

        IsDirtyDocument = false;
    }

    [RelayCommand(CanExecute = nameof(IsSaveEnabled))]
    private void SaveCatalog()
    {
        if (SaveCatalogRequested is not null)
        {
            SaveCatalogRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentCatalogPath))
        {
            StatusText = T("status.use_save_as");
            return;
        }

        CompleteSaveCatalog(CurrentCatalogPath);
    }

    public void CompleteSaveCatalog(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        StartOperation(T("status.saving_catalog"));
        SetProgress(40, T("status.parsing_items"));
        SetProgress(90, T("status.updating_indexes"));
        CompleteOperation();

        CurrentCatalogPath = filePath;
        StatusText = F("status.saved_catalog_to", GetDisplayFileName(filePath));
        IsDirtyDocument = false;
    }

    [RelayCommand]
    private void SaveCatalogAs()
    {
        if (SaveCatalogAsRequested is not null)
        {
            SaveCatalogAsRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        CompleteSaveCatalogAs("catalog.scd");
    }

    public void CompleteSaveCatalogAs(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        StartOperation(T("status.saving_catalog"));
        SetProgress(50, T("status.parsing_items"));
        SetProgress(95, T("status.updating_indexes"));
        CompleteOperation();

        CurrentCatalogPath = filePath;
        StatusText = F("status.saved_catalog_as", GetDisplayFileName(filePath));
        IsDirtyDocument = false;
    }

    [RelayCommand(CanExecute = nameof(IsPropertiesEnabled))]
    private void OpenProperties()
    {
        if (!TryBuildPropertiesDialog(out var dialog))
        {
            StatusText = T("status.unknown_selected_object");
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

                ApplyBrowserItemRenameIfNeeded(dialog);
                commentsByObjectKey[dialog.ObjectKey] = comments;
                IsDirtyDocument = true;
                StatusText = defaultStatusText;
            }
        });
    }

    [RelayCommand]
    private void ExitApplication()
    {
        StatusText = T("status.exit_requested");
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddItem()
    {
        AddToListRequested?.Invoke(this, EventArgs.Empty);
    }

    public void AddImportedItem(string? suggestedName)
    {
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        var itemName = string.IsNullOrWhiteSpace(suggestedName)
            ? F("status.imported_item_default", DateTime.Now.ToString("HHmmss"))
            : suggestedName.Trim();

        if (!addedItemsByNodeKey.TryGetValue(nodeKey, out var addedItems))
        {
            addedItems = [];
            addedItemsByNodeKey[nodeKey] = addedItems;
        }

        var importedItem = new BrowserItem(itemName, "Folder", "1 item", "folder");
        addedItems.Add(importedItem);
        RefreshBrowserItemsForSelection();
        SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
            item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        IsDirtyDocument = true;
        StatusText = F("status.added_item", itemName);
    }

    [RelayCommand(CanExecute = nameof(IsDeleteEnabled))]
    private void DeleteItem()
    {
        if (SelectedBrowserItem is null)
        {
            return;
        }

        var nodeKey = SelectedTreeNode?.Key ?? "library";
        if (!deletedItemNamesByNodeKey.TryGetValue(nodeKey, out var deletedNames))
        {
            deletedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            deletedItemNamesByNodeKey[nodeKey] = deletedNames;
        }

        deletedNames.Add(SelectedBrowserItem.Name);
        var deletedName = SelectedBrowserItem.Name;
        RefreshBrowserItemsForSelection();
        IsDirtyDocument = true;
        StatusText = F("status.deleted_item", deletedName);
    }

    [RelayCommand]
    private void OpenOptions()
    {
        if (OptionsRequested is null)
        {
            StatusText = T("status.options_not_implemented");
            return;
        }

        var dialog = new OptionsDialogViewModel(i18n, ["English", "Lithuanian"]);
        OptionsRequested.Invoke(this, new OptionsDialogRequestedEventArgs
        {
            Dialog = dialog,
            Complete = (accepted, pluginPath, language) =>
            {
                if (!accepted)
                {
                    return;
                }

                StatusText = F("status.options_saved_language", language);
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
            StatusText = T("status.opening_sourceforge");
        }
        catch (Exception ex)
        {
            StatusText = F("status.failed_open_sourceforge", ex.Message);
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
            StatusText = T("status.opening_github");
        }
        catch (Exception ex)
        {
            StatusText = F("status.failed_open_github", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        if (AboutRequested is null)
        {
            StatusText = T("status.about_not_implemented");
            return;
        }

        AboutRequested.Invoke(this, EventArgs.Empty);
        StatusText = defaultStatusText;
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
        StatusText = F("status.expanded", targetNode.Title);
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
        StatusText = F("status.collapsed", targetNode.Title);
    }

    [RelayCommand]
    private void SetViewMode(string modeKey)
    {
        if (Enum.TryParse<BrowserViewMode>(modeKey, true, out var mode))
        {
            CurrentViewMode = mode;
            StatusText = F("status.view_mode", GetViewModeDisplayName(mode));
        }
    }

    [RelayCommand]
    private void SetSortMode(string sortKey)
    {
        if (Enum.TryParse<BrowserSortMode>(sortKey, true, out var sortMode))
        {
            CurrentSortMode = sortMode;
            RefreshBrowserItemsForSelection();
            StatusText = F("status.arrange_icons_by", GetSortModeDisplayName(sortMode));
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
        StartOperation(T("status.updating_view"));
        SetProgress(60, T("status.parsing_catalog"));
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
        StatusText = F("status.copied", SelectedBrowserItem.Name);
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
        StatusText = F("status.pasted", ClipboardItem.Name);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToFolder))]
    private void NavigateToFolder()
    {
        if (TryResolveNodeFromBrowserSelection(out var targetNode))
        {
            SelectedTreeNode = targetNode;
            StatusText = F("status.navigated", targetNode.Title);
        }
    }

    private bool CanNavigateToFolder()
    {
        return TryResolveNodeFromBrowserSelection(out _);
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
        StatusText = F("status.cut", SelectedBrowserItem.Name);
    }

    public void ApplySessionState(BrowserViewMode viewMode, BrowserSortMode sortMode, bool isStatusBarVisible)
    {
        CurrentViewMode = viewMode;
        CurrentSortMode = sortMode;
        IsStatusBarVisible = isStatusBarVisible;
        RefreshBrowserItemsForSelection();
    }

    private string GetViewModeDisplayName(BrowserViewMode viewMode)
    {
        return viewMode switch
        {
            BrowserViewMode.SmallIcons => T("view_mode.small_icons"),
            BrowserViewMode.LargeIcons => T("view_mode.large_icons"),
            _ => viewMode.ToString()
        };
    }

    private string GetSortModeDisplayName(BrowserSortMode sortMode)
    {
        return sortMode switch
        {
            BrowserSortMode.Name => T("sort_mode.name"),
            BrowserSortMode.Type => T("sort_mode.type"),
            BrowserSortMode.Size => T("sort_mode.size"),
            _ => sortMode.ToString()
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
            var nodeTitle = SelectedTreeNode?.Title ?? T("browser.library");
            var infoProperties = BuildBrowserItemInfoProperties(SelectedBrowserItem, nodeTitle);

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedBrowserItem.Name,
                SelectedBrowserItem.IconGlyph,
                comments,
                infoProperties,
                i18n);
            return true;
        }

        if (SelectedTreeNode is not null)
        {
            var objectKey = GetTreeNodeObjectKey(SelectedTreeNode);
            var comments = GetObjectComments(objectKey);

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedTreeNode.Title,
                SelectedTreeNode.IconGlyph,
                comments,
                new Dictionary<string, object?>(),
                i18n);
            return true;
        }

        dialog = null;
        return false;
    }

    private IReadOnlyDictionary<string, object?> BuildBrowserItemInfoProperties(BrowserItem item, string nodeTitle)
    {
        if (!SupportsInfoTab(item.Type))
        {
            return new Dictionary<string, object?>();
        }

        return new Dictionary<string, object?>(StringComparer.CurrentCultureIgnoreCase)
        {
            [T("browser.info.type")] = item.Type,
            [T("browser.info.size")] = item.Size,
            [T("browser.info.location")] = nodeTitle
        };
    }

    private static bool SupportsInfoTab(string? itemType)
    {
        return !string.Equals(itemType, "Folder", StringComparison.OrdinalIgnoreCase);
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
        var originalName = ResolveOriginalBrowserItemName(nodeKey, item.Name);
        return $"item:{nodeKey}:{originalName}";
    }

    private static string GetTreeNodeObjectKey(BrowserTreeNode node)
    {
        return $"tree:{node.Key}";
    }

    private static string GetDisplayFileName(string filePath)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(fileName) ? filePath : fileName;
    }

    private void RefreshBrowserItemsForSelection()
    {
        var previouslySelectedName = SelectedBrowserItem?.Name;
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        var baseItems = browserDataStore.GetBrowserItems(nodeKey);
        if (deletedItemNamesByNodeKey.TryGetValue(nodeKey, out var deletedNames) && deletedNames.Count > 0)
        {
            baseItems = baseItems
                .Where(item => !deletedNames.Contains(item.Name))
                .ToArray();
        }

        var addedItems = addedItemsByNodeKey.TryGetValue(nodeKey, out var runtimeItems)
            ? runtimeItems
            : [];
        var items = baseItems.Concat(addedItems).ToArray();
        if (deletedItemNamesByNodeKey.TryGetValue(nodeKey, out deletedNames) && deletedNames.Count > 0)
        {
            items = items.Where(item => !deletedNames.Contains(item.Name)).ToArray();
        }

        if (renamedBrowserItemNamesByNodeKey.TryGetValue(nodeKey, out var renamedItems) && renamedItems.Count > 0)
        {
            items = items
                .Select(item =>
                {
                    return renamedItems.TryGetValue(item.Name, out var renamedName)
                        ? item with { Name = renamedName }
                        : item;
                })
                .ToArray();
        }

        if (items.Length == 0)
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

    private void ApplyBrowserItemRenameIfNeeded(PropertiesDialogViewModel dialog)
    {
        if (SelectedBrowserItem is null || SelectedTreeNode is null)
        {
            return;
        }

        var nodeKey = SelectedTreeNode.Key;
        var currentDisplayName = SelectedBrowserItem.Name;
        var requestedName = dialog.Name.Trim();
        if (string.IsNullOrWhiteSpace(requestedName) ||
            requestedName.Equals(currentDisplayName, StringComparison.Ordinal))
        {
            return;
        }

        var originalName = ResolveOriginalBrowserItemName(nodeKey, currentDisplayName);
        if (!renamedBrowserItemNamesByNodeKey.TryGetValue(nodeKey, out var renamedItems))
        {
            renamedItems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            renamedBrowserItemNamesByNodeKey[nodeKey] = renamedItems;
        }

        renamedItems[originalName] = requestedName;
        RefreshBrowserItemsForSelection();
        SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
            item.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveOriginalBrowserItemName(string nodeKey, string displayName)
    {
        if (!renamedBrowserItemNamesByNodeKey.TryGetValue(nodeKey, out var renamedItems))
        {
            return displayName;
        }

        foreach (var (original, renamed) in renamedItems)
        {
            if (renamed.Equals(displayName, StringComparison.OrdinalIgnoreCase))
            {
                return original;
            }
        }

        return displayName;
    }

    partial void OnSelectedTreeNodeChanged(BrowserTreeNode? value)
    {
        RefreshBrowserItemsForSelection();
        OnPropertyChanged(nameof(IsPropertiesEnabled));
        OpenPropertiesCommand.NotifyCanExecuteChanged();
        ExpandSelectionCommand.NotifyCanExecuteChanged();
        CollapseSelectionCommand.NotifyCanExecuteChanged();
        NavigateToFolderCommand.NotifyCanExecuteChanged();
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
        SetStatus(defaultStatusText);
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
        OnPropertyChanged(nameof(IsPropertiesEnabled));
        OnPropertyChanged(nameof(IsCopyEnabled));
        OnPropertyChanged(nameof(IsCutEnabled));
        DeleteItemCommand.NotifyCanExecuteChanged();
        OpenPropertiesCommand.NotifyCanExecuteChanged();
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

    private string T(string key)
    {
        return i18n.Get(key);
    }

    private string F(string key, params object[] args)
    {
        return i18n.Format(key, args);
    }
}
