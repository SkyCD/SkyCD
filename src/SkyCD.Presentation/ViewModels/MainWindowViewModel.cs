using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Lists;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly IReadOnlyList<CatalogDocument>? inMemoryCatalogEntries;
    private readonly IStringLocalizer propertyValueLocalizer;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByKey;
    private readonly IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByTitle;
    private readonly Dictionary<string, string> commentsByObjectKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<CatalogDocument>> addedItemsByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<string>> deletedItemNamesByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, string>> renamedBrowserItemNamesByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> statusTransitions = [];
    private readonly List<int> progressTransitions = [];
    private readonly IReadOnlyList<MainMenuItemViewModel> topMenuItems;
    private const string DefaultStatusText = "Done.";

    public event EventHandler? AddToListRequested;
    public event EventHandler? NewCatalogRequested;
    public event EventHandler? OpenCatalogRequested;
    public event EventHandler? SaveCatalogAsRequested;
    public event EventHandler? SaveCatalogRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler? ExitRequested;

    public MainWindowViewModel(CatalogDocumentRepository catalogRepository)
        : this(
            catalogRepository,
            new PropertyValueLocalizer())
    {
    }

    public MainWindowViewModel(
        CatalogDocumentRepository catalogRepository,
        IStringLocalizer propertyValueLocalizer)
    {
        this.catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
        this.propertyValueLocalizer =
            propertyValueLocalizer ?? throw new ArgumentNullException(nameof(propertyValueLocalizer));
        EnsureSeedData();
        TreeNodes = GetTreeNodes();

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        treeNodesByTitle = allTreeNodes.ToDictionary(static node => node.Title, StringComparer.OrdinalIgnoreCase);
        topMenuItems = BuildTopMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
    }

    public MainWindowViewModel(
        IReadOnlyList<CatalogDocument> catalogEntries,
        IStringLocalizer? propertyValueLocalizer = null)
    {
        inMemoryCatalogEntries = catalogEntries ?? throw new ArgumentNullException(nameof(catalogEntries));
        this.propertyValueLocalizer = propertyValueLocalizer ?? new PropertyValueLocalizer();
        TreeNodes = BuildTreeNodesFromEntries(inMemoryCatalogEntries);

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        treeNodesByTitle = allTreeNodes.ToDictionary(static node => node.Title, StringComparer.OrdinalIgnoreCase);
        topMenuItems = BuildTopMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
    }

    public IReadOnlyList<BrowserTreeNode> TreeNodes { get; }

    public IReadOnlyList<BrowserDetailsColumn> BrowserDetailsColumns { get; } =
    [
        new()
        {
            Header = "Name", ValuePath = "Name",
            Width = new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star)
        },
        new()
        {
            Header = "Type", ValuePath = "DisplayType",
            Width = new Avalonia.Controls.GridLength(150, Avalonia.Controls.GridUnitType.Pixel)
        },
        new()
        {
            Header = "Size", ValuePath = "DisplaySize",
            Width = new Avalonia.Controls.GridLength(120, Avalonia.Controls.GridUnitType.Pixel),
            HeaderAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            ValueAlignment = Avalonia.Layout.HorizontalAlignment.Right
        }
    ];

    public bool IsSaveEnabled => IsDirtyDocument;

    public bool IsDeleteEnabled => SelectedBrowserItem is not null;

    public bool IsPropertiesEnabled => SelectedBrowserItem is not null || SelectedTreeNode is not null;

    public string ProgressText => $"{ProgressValue}%";

    public bool IsTilesViewChecked => CurrentViewMode == BrowserViewMode.Tiles;

    public bool IsSmallIconsViewChecked => CurrentViewMode == BrowserViewMode.SmallIcons;

    public bool IsLargeIconsViewChecked => CurrentViewMode == BrowserViewMode.LargeIcons;

    public bool IsListViewChecked => CurrentViewMode == BrowserViewMode.List;

    public bool IsDetailsViewChecked => CurrentViewMode == BrowserViewMode.Details;

    public bool IsSortByNameChecked => IsSortMode("Name");

    public bool IsSortByTypeChecked => IsSortMode("Type");

    public bool IsSortBySizeChecked => IsSortMode("Size");

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

    public IReadOnlyList<MainMenuItemViewModel> TopMenuItems => topMenuItems;
    public IReadOnlyList<MainMenuItemViewModel> FileMenuItems => topMenuItems[0].Items;
    public IReadOnlyList<MainMenuItemViewModel> EditMenuItems => topMenuItems[1].Items;
    public IReadOnlyList<MainMenuItemViewModel> ViewMenuItems => topMenuItems[2].Items;
    public IReadOnlyList<MainMenuItemViewModel> ToolsMenuItems => topMenuItems[3].Items;
    public IReadOnlyList<MainMenuItemViewModel> HelpMenuItems => topMenuItems[4].Items;

    public IReadOnlyList<MainMenuItemViewModel> BrowserContextMenuItems => BuildBrowserContextMenuItems();

    public IReadOnlyList<MainMenuItemViewModel> TreeContextMenuItems => BuildTreeContextMenuItems();

    [ObservableProperty] private IReadOnlyList<CatalogDocument> browserItems = [];

    [ObservableProperty] private BrowserTreeNode? selectedTreeNode;

    [ObservableProperty] private CatalogDocument? selectedBrowserItem;

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
    [NotifyPropertyChangedFor(nameof(IsSortBySizeChecked))]
    private string currentSortMode = "Name";

    [ObservableProperty] private bool isStatusBarVisible = true;

    [ObservableProperty] private bool isDirtyDocument;

    [ObservableProperty] private string statusText = DefaultStatusText;

    [ObservableProperty] private bool isProgressVisible;

    [ObservableProperty] private int progressValue;

    [ObservableProperty] private CatalogDocument? clipboardItem;

    [ObservableProperty] private string? currentCatalogPath;

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
        StatusText = "Created a new catalog.";
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
        StartOperation("Loading catalog...");
        SetProgress(35, "Parsing catalog...");
        SetProgress(80, "Updating browser...");
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
            StatusText = "Use Save As to select file location.";
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

        StartOperation("Saving catalog...");
        SetProgress(40, "Parsing items...");
        SetProgress(90, "Updating indexes...");
        CompleteOperation();

        CurrentCatalogPath = filePath;
        StatusText = $"Saved catalog to {GetDisplayFileName(filePath)}.";
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

        StartOperation("Saving catalog...");
        SetProgress(50, "Parsing items...");
        SetProgress(95, "Updating indexes...");
        CompleteOperation();

        CurrentCatalogPath = filePath;
        StatusText = $"Saved catalog as {GetDisplayFileName(filePath)}.";
        IsDirtyDocument = false;
    }

    [RelayCommand(CanExecute = nameof(IsPropertiesEnabled))]
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

                ApplyBrowserItemRenameIfNeeded(dialog);
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

    public void AddImportedItem(string? suggestedName)
    {
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        var itemName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"Imported Item {DateTime.Now:HHmmss}"
            : suggestedName.Trim();

        if (!addedItemsByNodeKey.TryGetValue(nodeKey, out var addedItems))
        {
            addedItems = [];
            addedItemsByNodeKey[nodeKey] = addedItems;
        }

        var importedItem = new CatalogDocument
        {
            Id = $"imported-{Guid.NewGuid():N}",
            Name = itemName,
            ParentId = nodeKey,
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0
        };
        addedItems.Add(importedItem);
        RefreshBrowserItemsForSelection();
        SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
            item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        IsDirtyDocument = true;
        StatusText = $"Added {itemName}.";
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
        StatusText = $"Deleted {deletedName}.";
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
        CurrentSortMode = NormalizeSortMode(sortKey);
        RefreshBrowserItemsForSelection();
        StatusText = $"Arrange icons by: {CurrentSortMode}.";
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

    [RelayCommand(CanExecute = nameof(CanNavigateToFolder))]
    private void NavigateToFolder()
    {
        if (TryResolveNodeFromBrowserSelection(out var targetNode))
        {
            SelectedTreeNode = targetNode;
            StatusText = $"Navigated to {targetNode.Title}.";
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
        StatusText = $"Cut {SelectedBrowserItem.Name}.";
    }

    public void ApplySessionState(BrowserViewMode viewMode, string? sortMode, bool isStatusBarVisible)
    {
        CurrentViewMode = viewMode;
        CurrentSortMode = NormalizeSortMode(sortMode);
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
            SelectedBrowserItem.Type == CatalogDocumentType.Folder)
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
            var infoProperties = GetBrowserItemInfoProperties(SelectedBrowserItem.Id);

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedBrowserItem.Name,
                SelectedBrowserItem.IconGlyph,
                comments,
                infoProperties,
                propertyValueLocalizer);
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
                new PropertiesCollection(),
                propertyValueLocalizer);
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

    private string GetBrowserItemObjectKey(CatalogDocument item)
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
        var baseItems = GetBrowserItems(nodeKey);
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
                    if (!renamedItems.TryGetValue(item.Name, out var renamedName))
                    {
                        return item;
                    }

                    return new CatalogDocument
                    {
                        Id = item.Id,
                        Name = renamedName,
                        ParentId = item.ParentId,
                        Type = item.Type,
                        Size = item.Size,
                        ChildrenCount = item.ChildrenCount,
                        Properties = item.Properties
                    };
                })
                .ToArray();
        }

        if (items.Length == 0)
        {
            BrowserItems = [];
            SelectedBrowserItem = null;
            return;
        }

        var refreshedItems = NormalizeSortMode(CurrentSortMode) switch
        {
            "Type" => items.OrderBy(static item => item.DisplayType)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "Size" => items.OrderBy(static item => item.Size)
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

    partial void OnSelectedBrowserItemChanged(CatalogDocument? value)
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
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
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
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
    }

    partial void OnCurrentSortModeChanged(string value)
    {
        CurrentSortMode = NormalizeSortMode(value);
        OnPropertyChanged(nameof(IsSortByNameChecked));
        OnPropertyChanged(nameof(IsSortByTypeChecked));
        OnPropertyChanged(nameof(IsSortBySizeChecked));
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
    }

    private bool IsSortMode(string expected)
    {
        return string.Equals(CurrentSortMode, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSortMode(string? sortMode)
    {
        return sortMode?.Trim().ToLowerInvariant() switch
        {
            "type" => "Type",
            "size" => "Size",
            _ => "Name"
        };
    }

    partial void OnIsDirtyDocumentChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSaveEnabled));
        SaveCatalogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
    }

    partial void OnProgressValueChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnClipboardItemChanged(CatalogDocument? value)
    {
        OnPropertyChanged(nameof(IsPasteEnabled));
        PasteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
    }

    partial void OnIsStatusBarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildTopMenuItems()
    {
        return
        [
            new MainMenuItemViewModel
            {
                Header = "_File",
                Items =
                [
                    new MainMenuItemViewModel { Header = "_New", HotKey = "Ctrl+N", Command = NewCatalogCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = "_Open...", HotKey = "Ctrl+O", Command = OpenCatalogCommand },
                    new MainMenuItemViewModel { Header = "_Save", HotKey = "Ctrl+S", Command = SaveCatalogCommand },
                    new MainMenuItemViewModel
                        { Header = "Save _As...", HotKey = "F12", Command = SaveCatalogAsCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = "_Properties...", Command = OpenPropertiesCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = "E_xit", Command = ExitApplicationCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = "_Edit",
                Items =
                [
                    new MainMenuItemViewModel { Header = "_Add...", HotKey = "F2", Command = AddItemCommand },
                    new MainMenuItemViewModel { Header = "_Delete", HotKey = "Delete", Command = DeleteItemCommand },
                    Separator(),
                    new MainMenuItemViewModel
                        { Header = "_Properties", HotKey = "Alt+Enter", Command = OpenPropertiesCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = "_View",
                Items =
                [
                    CheckedMenuItem(IsStatusBarVisible, "_StatusBar", ToggleStatusBarCommand, key: "statusbar"),
                    Separator(),
                    CheckedMenuItem(IsTilesViewChecked, "_Tiles", SetViewModeCommand, "Tiles", "view_tiles"),
                    CheckedMenuItem(IsSmallIconsViewChecked, "Small _Icons", SetViewModeCommand, "SmallIcons",
                        "view_small"),
                    CheckedMenuItem(IsLargeIconsViewChecked, "L_arge Icons", SetViewModeCommand, "LargeIcons",
                        "view_large"),
                    CheckedMenuItem(IsListViewChecked, "_List", SetViewModeCommand, "List", "view_list"),
                    CheckedMenuItem(IsDetailsViewChecked, "_Details", SetViewModeCommand, "Details", "view_details"),
                    Separator(),
                    new MainMenuItemViewModel
                    {
                        Header = "Arrange Icons By",
                        Items =
                        [
                            CheckedMenuItem(IsSortByNameChecked, "_Name", SetSortModeCommand, "Name", "sort_name"),
                            CheckedMenuItem(IsSortByTypeChecked, "_Type", SetSortModeCommand, "Type", "sort_type"),
                            CheckedMenuItem(IsSortBySizeChecked, "_Size", SetSortModeCommand, "Size", "sort_size")
                        ]
                    },
                    new MainMenuItemViewModel { Header = "_Refresh", HotKey = "F5", Command = RefreshCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = "_Tools",
                Items =
                [
                    new MainMenuItemViewModel
                        { Header = "_Options...", HotKey = "Ctrl+Alt+O", Command = OpenOptionsCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = "_Help",
                Items =
                [
                    new MainMenuItemViewModel
                        { Header = "Project website in _SourceForge.NET", Command = OpenProjectWebsiteCommand },
                    new MainMenuItemViewModel { Header = "Project area in _GitHub", Command = OpenGithubAreaCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = "_About...", Command = OpenAboutCommand }
                ]
            }
        ];
    }

    private static MainMenuItemViewModel Separator()
    {
        return new MainMenuItemViewModel { Header = "-" };
    }

    private void RefreshTopMenuState()
    {
        var byKey = FlattenMenuItems(topMenuItems)
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key!, StringComparer.Ordinal);

        SetMenuHeader(byKey, "statusbar", CheckedHeader(IsStatusBarVisible, "_StatusBar"));
        SetMenuHeader(byKey, "view_tiles", CheckedHeader(IsTilesViewChecked, "_Tiles"));
        SetMenuHeader(byKey, "view_small", CheckedHeader(IsSmallIconsViewChecked, "Small _Icons"));
        SetMenuHeader(byKey, "view_large", CheckedHeader(IsLargeIconsViewChecked, "L_arge Icons"));
        SetMenuHeader(byKey, "view_list", CheckedHeader(IsListViewChecked, "_List"));
        SetMenuHeader(byKey, "view_details", CheckedHeader(IsDetailsViewChecked, "_Details"));
        SetMenuHeader(byKey, "sort_name", CheckedHeader(IsSortByNameChecked, "_Name"));
        SetMenuHeader(byKey, "sort_type", CheckedHeader(IsSortByTypeChecked, "_Type"));
        SetMenuHeader(byKey, "sort_size", CheckedHeader(IsSortBySizeChecked, "_Size"));
    }

    private static IEnumerable<MainMenuItemViewModel> FlattenMenuItems(IEnumerable<MainMenuItemViewModel> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenMenuItems(item.Items))
            {
                yield return child;
            }
        }
    }

    private static void SetMenuHeader(
        IReadOnlyDictionary<string, MainMenuItemViewModel> byKey,
        string key,
        string header)
    {
        if (byKey.TryGetValue(key, out var item))
        {
            item.Header = header;
        }
    }

    private static string CheckedHeader(bool isChecked, string title)
    {
        return title;
    }

    private static MainMenuItemViewModel CheckedMenuItem(
        bool isChecked,
        string title,
        IRelayCommand command,
        object? commandParameter = null,
        string? key = null)
    {
        return new MainMenuItemViewModel
        {
            Key = key,
            Header = CheckedHeader(isChecked, title),
            Icon = isChecked ? "✓" : "\u00A0",
            Command = command,
            CommandParameter = commandParameter
        };
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildBrowserContextMenuItems()
    {
        return
        [
            new MainMenuItemViewModel
                { Header = "_Expand", Command = ExpandSelectionCommand, CommandParameter = "list" },
            new MainMenuItemViewModel
                { Header = "C_ollapse", Command = CollapseSelectionCommand, CommandParameter = "list" },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = "_View",
                Items =
                [
                    CheckedMenuItem(IsSmallIconsViewChecked, "Small _Icons", SetViewModeCommand, "SmallIcons"),
                    CheckedMenuItem(IsLargeIconsViewChecked, "L_arge Icons", SetViewModeCommand, "LargeIcons"),
                    CheckedMenuItem(IsListViewChecked, "_List", SetViewModeCommand, "List"),
                    CheckedMenuItem(IsDetailsViewChecked, "_Details", SetViewModeCommand, "Details"),
                    CheckedMenuItem(IsTilesViewChecked, "_Tiles", SetViewModeCommand, "Tiles")
                ]
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = "Arrange Icons By",
                Items =
                [
                    CheckedMenuItem(IsSortByNameChecked, "_Name", SetSortModeCommand, "Name"),
                    CheckedMenuItem(IsSortByTypeChecked, "_Type", SetSortModeCommand, "Type")
                ]
            },
            new MainMenuItemViewModel { Header = "_Refresh", Command = RefreshCommand },
            Separator(),
            new MainMenuItemViewModel { Header = "_Add...", Command = AddItemCommand },
            new MainMenuItemViewModel { Header = "_Delete", Command = DeleteItemCommand },
            Separator(),
            new MainMenuItemViewModel { Header = "_Properties...", Command = OpenPropertiesCommand }
        ];
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildTreeContextMenuItems()
    {
        return
        [
            new MainMenuItemViewModel
                { Header = "_Expand", Command = ExpandSelectionCommand, CommandParameter = "tree" },
            new MainMenuItemViewModel
                { Header = "C_ollapse", Command = CollapseSelectionCommand, CommandParameter = "tree" },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = "_View",
                Items =
                [
                    CheckedMenuItem(IsSmallIconsViewChecked, "Small _Icons", SetViewModeCommand, "SmallIcons"),
                    CheckedMenuItem(IsLargeIconsViewChecked, "L_arge Icons", SetViewModeCommand, "LargeIcons"),
                    CheckedMenuItem(IsListViewChecked, "_List", SetViewModeCommand, "List"),
                    CheckedMenuItem(IsDetailsViewChecked, "_Details", SetViewModeCommand, "Details"),
                    CheckedMenuItem(IsTilesViewChecked, "_Tiles", SetViewModeCommand, "Tiles")
                ]
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = "Arrange Icons By",
                Items =
                [
                    CheckedMenuItem(IsSortByNameChecked, "_Name", SetSortModeCommand, "Name"),
                    CheckedMenuItem(IsSortByTypeChecked, "_Type", SetSortModeCommand, "Type"),
                    CheckedMenuItem(IsSortBySizeChecked, "_Size", SetSortModeCommand, "Size")
                ]
            },
            new MainMenuItemViewModel { Header = "_Refresh", Command = RefreshCommand },
            Separator(),
            new MainMenuItemViewModel { Header = "_Add...", Command = AddItemCommand },
            new MainMenuItemViewModel { Header = "_Delete", Command = DeleteItemCommand },
            Separator(),
            new MainMenuItemViewModel { Header = "_Copy", HotKey = "Ctrl+C", Command = CopyCommand },
            new MainMenuItemViewModel { Header = "_Paste", HotKey = "Ctrl+V", Command = PasteCommand },
            new MainMenuItemViewModel { Header = "Cu_t", HotKey = "Ctrl+X", Command = CutCommand },
            Separator(),
            new MainMenuItemViewModel { Header = "_Properties...", Command = OpenPropertiesCommand }
        ];
    }

    private void EnsureSeedData()
    {
        if (catalogRepository is null || catalogRepository.GetAll().Count > 0)
        {
            return;
        }

        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }
    }

    private IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        if (catalogRepository is not null)
        {
            var roots = catalogRepository
                .GetRoots()
                .Where(entry => entry.Type != CatalogDocumentType.File)
                .ToArray();

            var treeNodes = roots
                .Select(root =>
                {
                    var descendants = catalogRepository
                        .GetDescendantsOf(root.Id)
                        .Where(entry => entry.Type != CatalogDocumentType.File)
                        .ToList();
                    descendants.Add(root);

                    var byParent = descendants
                        .GroupBy(entry => entry.ParentId ?? "__root__", StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

                    return BuildTreeNodeFromLookup(root, byParent, isExpanded: true);
                })
                .ToArray();

            if (treeNodes.Length > 0)
            {
                return treeNodes;
            }

            return BuildTreeNodesFromEntries(catalogRepository.CreateDefaultEntries());
        }

        return BuildTreeNodesFromEntries(inMemoryCatalogEntries ?? []);
    }

    private IReadOnlyList<CatalogDocument> GetBrowserItems(string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return [];
        }

        if (catalogRepository is not null)
        {
            var entries = catalogRepository.GetChildrenOf(nodeKey);
            if (entries.Count > 0)
            {
                return entries;
            }

            var defaults = catalogRepository.CreateDefaultEntries()
                .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
                .ToArray();
            return defaults;
        }

        var inMemoryEntries = (inMemoryCatalogEntries ?? [])
            .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
            .ToArray();
        return inMemoryEntries;
    }

    private PropertiesCollection GetBrowserItemInfoProperties(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return new PropertiesCollection();
        }

        if (catalogRepository is not null)
        {
            return catalogRepository.Get(itemId)?.Properties ?? new PropertiesCollection();
        }

        return (inMemoryCatalogEntries ?? [])
            .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.Ordinal))
            ?.Properties ?? new PropertiesCollection();
    }

    private static BrowserTreeNode BuildTreeNodeFromLookup(
        CatalogDocument entry,
        IReadOnlyDictionary<string, List<CatalogDocument>> byParent,
        bool isExpanded)
    {
        byParent.TryGetValue(entry.Id, out var childrenOfCurrent);

        var children = (childrenOfCurrent ?? [])
            .Select(child => BuildTreeNodeFromLookup(child, byParent, isExpanded: false))
            .ToArray();

        return new BrowserTreeNode(
            entry.Id,
            entry.Name,
            entry.Type.ResolveIconGlyph(),
            children,
            isExpanded);
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodesFromEntries(IReadOnlyList<CatalogDocument> entries)
    {
        var filteredEntries = entries
            .Where(entry => entry.Type != CatalogDocumentType.File)
            .ToList();
        var byId = filteredEntries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        return filteredEntries
            .Where(entry => string.IsNullOrWhiteSpace(entry.ParentId))
            .Select(entry => BuildTreeNode(entry, byId, isExpanded: true))
            .ToArray();
    }

    private static BrowserTreeNode BuildTreeNode(
        CatalogDocument entry,
        IReadOnlyDictionary<string, CatalogDocument> byId,
        bool isExpanded)
    {
        var children = byId.Values
            .Where(candidate => string.Equals(candidate.ParentId, entry.Id, StringComparison.Ordinal))
            .Select(child => BuildTreeNode(child, byId, isExpanded: false))
            .ToArray();

        return new BrowserTreeNode(
            entry.Id,
            entry.Name,
            entry.Type.ResolveIconGlyph(),
            children,
            isExpanded);
    }
}