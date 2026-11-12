using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Lists;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Presentation.UndoRedo;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable localizationSubscription;
    private bool isDisposed;
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly CatalogUndoManager? undoManager;
    private readonly bool shouldSeedDefaultCatalog;
    private readonly List<CatalogDocument>? inMemoryCatalogEntries;
    private readonly IStringLocalizer propertyValueLocalizer;
    private IReadOnlyDictionary<string, BrowserTreeNode> treeNodesByKey;
    private readonly Dictionary<string, string> commentsByObjectKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<CatalogDocument>> addedItemsByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<string>> deletedItemNamesByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, string>> renamedBrowserItemNamesByNodeKey =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> statusTransitions = [];
    private readonly List<int> progressTransitions = [];
    private readonly List<StatusVariantDocument> statusVariants = [];
    private IReadOnlyList<MainMenuItemViewModel> topMenuItems;
    private static string DefaultStatusText => LocalizationService.Current.Get(TextKeys.Done);
    private const string ItemStatusNamePropertyKey = "StatusName";
    private const string ItemStatusIconGlyphPropertyKey = "StatusIconGlyph";
    private const string ItemStatusIconColorPropertyKey = "StatusIconColor";
    private const string ItemStatusIconTextPropertyKey = "StatusIconText";

    public event EventHandler? AddToListRequested;
    public event EventHandler? NewCatalogRequested;
    public event EventHandler? OpenCatalogRequested;
    public event EventHandler? SaveCatalogAsRequested;
    public event EventHandler? SaveCatalogRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler? ExitRequested;

    private MenuExtensionManager menuExtensionManager;
    private CancellationTokenSource? pluginCommandCancellation;

    public void RefreshPluginMenuServices(MenuExtensionManager updatedManager)
    {
        menuExtensionManager = updatedManager ?? throw new ArgumentNullException(nameof(updatedManager));
        topMenuItems = BuildTopMenuItems();
        OnPropertyChanged(nameof(TopMenuItems));
        OnPropertyChanged(nameof(FileMenuItems));
        OnPropertyChanged(nameof(EditMenuItems));
        OnPropertyChanged(nameof(ViewMenuItems));
        OnPropertyChanged(nameof(ToolsMenuItems));
        OnPropertyChanged(nameof(HelpMenuItems));
    }

    private MainWindowViewModel(
        IRepository<CatalogDocument> catalogRepository,
        IStringLocalizer propertyValueLocalizer,
        MenuExtensionManager menuExtensionManager,
        bool shouldSeedDefaultCatalog,
        CatalogUndoManager? undoManager = null)
    {
        this.catalogRepository = catalogRepository as CatalogDocumentRepository
                                 ?? throw new InvalidOperationException(
                                     "Catalog repository must be CatalogDocumentRepository.");
        this.undoManager = undoManager;
        this.propertyValueLocalizer =
            propertyValueLocalizer ?? throw new ArgumentNullException(nameof(propertyValueLocalizer));
        this.menuExtensionManager = menuExtensionManager ?? throw new ArgumentNullException(nameof(menuExtensionManager));
        this.shouldSeedDefaultCatalog = shouldSeedDefaultCatalog;

        EnsureSeedData();
        TreeNodes = GetTreeNodes();

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        topMenuItems = BuildTopMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
        localizationSubscription = LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public MainWindowViewModel(
        RepositoryManager repositoryManager)
        : this(
            repositoryManager.For<CatalogDocument>(),
            new PropertyValueLocalizer(),
            CreateEmptyMenuExtensionManager(),
            RegisterAppStart(repositoryManager),
            new CatalogUndoManager((CatalogDocumentRepository)repositoryManager.For<CatalogDocument>()))
    {
    }

    private MainWindowViewModel(
        IReadOnlyList<CatalogDocument> catalogEntries,
        IStringLocalizer? propertyValueLocalizer = null,
        MenuExtensionManager? menuExtensionManager = null)
    {
        inMemoryCatalogEntries = catalogEntries?.ToList() ?? throw new ArgumentNullException(nameof(catalogEntries));
        this.propertyValueLocalizer = propertyValueLocalizer ?? new PropertyValueLocalizer();
        this.menuExtensionManager = menuExtensionManager ?? CreateEmptyMenuExtensionManager();
        shouldSeedDefaultCatalog = false;
        TreeNodes = BuildTreeNodesFromEntries(inMemoryCatalogEntries);

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        topMenuItems = BuildTopMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
        localizationSubscription = LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public void RefreshLocalization()
    {
        if (isDisposed) return;
        RefreshPluginMenuServices(menuExtensionManager);
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        StatusText = DefaultStatusText;
        RefreshBrowserItemsForSelection();
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        localizationSubscription.Dispose();
        GC.SuppressFinalize(this);
    }

    public static MainWindowViewModel CreateForInMemoryCatalog(
        IReadOnlyList<CatalogDocument> catalogEntries,
        IStringLocalizer? propertyValueLocalizer = null,
        MenuExtensionManager? menuExtensionManager = null)
    {
        return new MainWindowViewModel(catalogEntries, propertyValueLocalizer, menuExtensionManager);
    }

    private static MenuExtensionManager CreateEmptyMenuExtensionManager()
    {
        return new MenuExtensionManager(Array.Empty<IMenuPluginCapability>());
    }

    private static bool RegisterAppStart(RepositoryManager repositoryManager)
    {
        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount++;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
        return options.AppStartCount <= 1;
    }

    public IReadOnlyList<BrowserTreeNode> TreeNodes { get; private set; }

    public IReadOnlyList<BrowserDetailsColumn> BrowserDetailsColumns =>
    [
        new()
        {
            Header = LocalizationService.Current.Get(TextKeys.Name2), ValuePath = "Name",
            Width = new Avalonia.Controls.GridLength(500, Avalonia.Controls.GridUnitType.Pixel)
        },
        new()
        {
            Header = LocalizationService.Current.Get(TextKeys.Size), ValuePath = "DisplaySize",
            Width = new Avalonia.Controls.GridLength(100, Avalonia.Controls.GridUnitType.Pixel),
            HeaderAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            ValueAlignment = Avalonia.Layout.HorizontalAlignment.Right
        }
    ];

    public bool IsSaveEnabled => IsDirtyDocument;

    public bool IsDeleteEnabled => CanDeleteItem(null);

    public bool CanUndo => undoManager?.CanUndo ?? false;
    public bool CanRedo => undoManager?.CanRedo ?? false;

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
    [ObservableProperty] private System.Collections.IList selectedBrowserItems = new List<CatalogDocument>();

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarShown))]
    private bool isStatusBarVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusBarShown))]
    [NotifyPropertyChangedFor(nameof(IsCatalogInteractionEnabled))]
    private bool isCatalogLoading;

    public bool IsStatusBarShown => IsStatusBarVisible || IsCatalogLoading;
    public bool IsCatalogInteractionEnabled => !IsCatalogLoading;

    [ObservableProperty] private bool isDirtyDocument;

    [ObservableProperty] private string statusText = DefaultStatusText;

    [ObservableProperty] private bool isProgressVisible;

    [ObservableProperty] private int progressValue;

    [ObservableProperty] private CatalogDocument? clipboardItem;

    [ObservableProperty] private string? currentCatalogPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteEnabled))]
    private ActivePane activePane = ActivePane.Browser;

    partial void OnActivePaneChanged(ActivePane value)
    {
        OnPropertyChanged(nameof(IsDeleteEnabled));
        DeleteItemCommand.NotifyCanExecuteChanged();
    }

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
        StatusText = LocalizationService.Current.Get(TextKeys.CreatedANewCatalog);
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

    public void BeginCatalogLoad()
    {
        IsCatalogLoading = true;
        StartOperation(LocalizationService.Current.Get(TextKeys.LoadingCatalog));
    }

    public void ReportCatalogLoadProgress(int value)
    {
        if (!IsCatalogLoading) return;
        SetProgress(Math.Max(ProgressValue, Math.Clamp(value, 0, 100)),
            LocalizationService.Current.Get(value < 50 ? TextKeys.ParsingCatalog :
                value < 95 ? TextKeys.UpdatingIndexes : TextKeys.UpdatingBrowser));
    }

    public void EndCatalogLoad()
    {
        IsCatalogLoading = false;
        IsProgressVisible = false;
        ProgressValue = 0;
    }

    public void CompleteOpenCatalog()
    {
        StartOperation(LocalizationService.Current.Get(TextKeys.LoadingCatalog));
        SetProgress(35, LocalizationService.Current.Get(TextKeys.ParsingCatalog));
        SetProgress(80, LocalizationService.Current.Get(TextKeys.UpdatingBrowser));
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
            StatusText = LocalizationService.Current.Get(TextKeys.UseSaveAsToSelectFileLocation);
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

        StartOperation(LocalizationService.Current.Get(TextKeys.SavingCatalog));
        SetProgress(40, LocalizationService.Current.Get(TextKeys.ParsingItems));
        SetProgress(90, LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
        CompleteOperation();

        CurrentCatalogPath = filePath;
        StatusText = LocalizationService.Current.Format(TextKeys.SavedCatalogTo, GetDisplayFileName(filePath));
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

        StartOperation(LocalizationService.Current.Get(TextKeys.SavingCatalog));
        SetProgress(50, LocalizationService.Current.Get(TextKeys.ParsingItems));
        SetProgress(95, LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
        CompleteOperation();

        CurrentCatalogPath = filePath;
        StatusText = LocalizationService.Current.Format(TextKeys.SavedCatalogAs, GetDisplayFileName(filePath));
        IsDirtyDocument = false;
    }

    [RelayCommand(CanExecute = nameof(IsPropertiesEnabled))]
    private void OpenProperties()
    {
        if (!TryBuildPropertiesDialog(out var dialog))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.UnknownSelectedObject);
            return;
        }

        var selectedId = SelectedBrowserItem?.Id ?? SelectedTreeNode?.Key;
        PropertiesRequested?.Invoke(this, new PropertiesDialogRequestedEventArgs
        {
            Dialog = dialog,
            Complete = (accepted, comments) =>
            {
                if (!accepted)
                {
                    return;
                }

                if (selectedId is not null && dialog.HasCustomProperties)
                {
                    var document = catalogRepository?.Get(selectedId)
                        ?? inMemoryCatalogEntries?.FirstOrDefault(item => item.Id == selectedId);
                    if (document is not null)
                    {
                        foreach (var property in dialog.GetProperties())
                        {
                            document.Properties[property.Key] = property.Value;
                        }
                        catalogRepository?.Save(document.Id, document);
                    }
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
        StatusText = LocalizationService.Current.Get(TextKeys.ExitRequested);
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        if (undoManager is not null)
        {
            await undoManager.UndoAsync();
            StatusText = LocalizationService.Current.Get(TextKeys.UndoAction);
            RefreshTreeNodes();
            RefreshBrowserItemsForSelection();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task Redo()
    {
        if (undoManager is not null)
        {
            await undoManager.RedoAsync();
            StatusText = LocalizationService.Current.Get(TextKeys.RedoAction);
            RefreshTreeNodes();
            RefreshBrowserItemsForSelection();
        }
    }

    [RelayCommand]
    private void AddItem()
    {
        AddToListRequested?.Invoke(this, EventArgs.Empty);
    }

    public void AddImportedDocuments(IReadOnlyList<CatalogDocument> documents)
    {
        if (documents is null || documents.Count == 0)
        {
            return;
        }

        if (catalogRepository is not null)
        {
            catalogRepository.Collection.Database.InBatch(() =>
            {
                foreach (var doc in documents)
                {
                    catalogRepository.Save(doc.Id, doc);
                }
            });
        }
        else if (inMemoryCatalogEntries is not null)
        {
            inMemoryCatalogEntries.AddRange(documents);
        }
        else
        {
            foreach (var doc in documents)
            {
                var parentKey = doc.ParentId ?? "library";
                if (!addedItemsByNodeKey.TryGetValue(parentKey, out var list))
                {
                    list = [];
                    addedItemsByNodeKey[parentKey] = list;
                }
                list.Add(doc);
            }
        }

        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();

        var rootImported = documents[0];
        SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
            string.Equals(item.Id, rootImported.Id, StringComparison.Ordinal) ||
            item.Name.Equals(rootImported.Name, StringComparison.OrdinalIgnoreCase));
        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.Added, rootImported.Name);
    }

    public void AddImportedItem(string? suggestedName)
    {
        var nodeKey = SelectedTreeNode?.Key ?? "library";
        var itemName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"Imported Item {DateTime.Now:HHmmss}"
            : suggestedName.Trim();

        var importedItem = new CatalogDocument
        {
            Id = $"imported-{Guid.NewGuid():N}",
            Name = itemName,
            ParentId = nodeKey,
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0
        };

        AddImportedDocuments([importedItem]);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem(object? parameter = null)
    {
        var targetPane = ResolveTargetPane(parameter);
        if (targetPane == ActivePane.Tree)
        {
            DeleteSelectedTreeNode();
        }
        else
        {
            DeleteSelectedBrowserItems();
        }
    }

    public bool CanDeleteItem(object? parameter = null)
    {
        var targetPane = ResolveTargetPane(parameter);
        if (targetPane == ActivePane.Tree)
        {
            return CanDeleteTreeNode(SelectedTreeNode);
        }

        return GetSelectedBrowserItems().Count > 0;
    }

    private ActivePane ResolveTargetPane(object? parameter)
    {
        if (parameter is string str)
        {
            if (string.Equals(str, "tree", StringComparison.OrdinalIgnoreCase))
            {
                return ActivePane.Tree;
            }

            if (string.Equals(str, "list", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(str, "browser", StringComparison.OrdinalIgnoreCase))
            {
                return ActivePane.Browser;
            }
        }
        else if (parameter is ActivePane pane)
        {
            return pane;
        }

        return ActivePane;
    }

    private void DeleteSelectedBrowserItems()
    {
        var itemsToDelete = GetSelectedBrowserItems();
        if (itemsToDelete.Count == 0)
        {
            return;
        }

        var deletedNames = itemsToDelete.Select(item => item.Name).ToList();

        if (catalogRepository is not null)
        {
            DeleteCatalogEntriesFromRepository(itemsToDelete);
        }
        else if (inMemoryCatalogEntries is not null)
        {
            DeleteCatalogEntriesFromInMemory(itemsToDelete);
        }
        else
        {
            var nodeKey = SelectedTreeNode?.Key ?? "library";
            if (!deletedItemNamesByNodeKey.TryGetValue(nodeKey, out var deletedNamesSet))
            {
                deletedNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                deletedItemNamesByNodeKey[nodeKey] = deletedNamesSet;
            }

            foreach (var item in itemsToDelete)
            {
                deletedNamesSet.Add(item.Name);
            }
        }

        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();

        SelectedBrowserItem = BrowserItems.FirstOrDefault();
        SelectedBrowserItems = SelectedBrowserItem is not null
            ? new List<CatalogDocument> { SelectedBrowserItem }
            : new List<CatalogDocument>();

        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.Deleted, string.Join(", ", deletedNames));
    }

    private void DeleteSelectedTreeNode()
    {
        if (SelectedTreeNode is null || !CanDeleteTreeNode(SelectedTreeNode))
        {
            return;
        }

        var targetNode = SelectedTreeNode;
        var parentNode = FindParentTreeNode(TreeNodes, targetNode.Key);

        if (catalogRepository is not null)
        {
            var descendantIds = catalogRepository
                .GetDescendantsOf(targetNode.Key)
                .Select(static entry => entry.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var allIdsToDelete = new[] { targetNode.Key }.Concat(descendantIds).Distinct(StringComparer.Ordinal).ToArray();

            catalogRepository.Collection.Database.InBatch(() =>
            {
                foreach (var id in allIdsToDelete)
                {
                    using var descendantDocument = catalogRepository.Collection.GetDocument(id);
                    if (descendantDocument is not null)
                    {
                        catalogRepository.Collection.Delete(descendantDocument);
                    }
                }
            });

            foreach (var descendantId in allIdsToDelete)
            {
                addedItemsByNodeKey.Remove(descendantId);
                deletedItemNamesByNodeKey.Remove(descendantId);
                renamedBrowserItemNamesByNodeKey.Remove(descendantId);
            }
        }
        else if (inMemoryCatalogEntries is not null)
        {
            var doc = inMemoryCatalogEntries.FirstOrDefault(entry => string.Equals(entry.Id, targetNode.Key, StringComparison.Ordinal));
            if (doc is not null)
            {
                DeleteCatalogEntriesFromInMemory([doc]);
            }
            else
            {
                var dummyDoc = new CatalogDocument { Id = targetNode.Key, Name = targetNode.Title };
                DeleteCatalogEntriesFromInMemory([dummyDoc]);
            }
        }

        RefreshTreeNodes(parentNode?.Key);

        RefreshBrowserItemsForSelection();
        SelectedBrowserItem = BrowserItems.FirstOrDefault();
        SelectedBrowserItems = SelectedBrowserItem is not null
            ? new List<CatalogDocument> { SelectedBrowserItem }
            : new List<CatalogDocument>();

        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.Deleted, targetNode.Title);
    }

    private void DeleteCatalogEntriesFromInMemory(IReadOnlyList<CatalogDocument> items)
    {
        if (inMemoryCatalogEntries is null || items.Count == 0)
        {
            return;
        }

        var toDelete = new HashSet<string>(
            items.Select(static entry => entry.Id).Where(static id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        var added = true;
        while (added)
        {
            added = false;
            foreach (var entry in inMemoryCatalogEntries)
            {
                if (entry.ParentId is not null && toDelete.Contains(entry.ParentId) && toDelete.Add(entry.Id))
                {
                    added = true;
                }
            }
        }

        inMemoryCatalogEntries.RemoveAll(entry => toDelete.Contains(entry.Id));

        foreach (var deletedId in toDelete)
        {
            addedItemsByNodeKey.Remove(deletedId);
            deletedItemNamesByNodeKey.Remove(deletedId);
            renamedBrowserItemNamesByNodeKey.Remove(deletedId);
        }
    }

    private bool CanDeleteTreeNode(BrowserTreeNode? node)
    {
        if (node is null)
        {
            return false;
        }

        if (TreeNodes.Any(root => string.Equals(root.Key, node.Key, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(node.Key, "library", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static BrowserTreeNode? FindParentTreeNode(IEnumerable<BrowserTreeNode> nodes, string childKey)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Any(child => string.Equals(child.Key, childKey, StringComparison.OrdinalIgnoreCase)))
            {
                return node;
            }

            var foundInChild = FindParentTreeNode(node.Children, childKey);
            if (foundInChild is not null)
            {
                return foundInChild;
            }
        }

        return null;
    }

    private void RefreshTreeNodes(string? preferredSelectedKey = null)
    {
        var targetKey = preferredSelectedKey ?? SelectedTreeNode?.Key;
        var expandedKeys = FlattenNodes(TreeNodes)
            .Where(static node => node.IsExpanded)
            .Select(static node => node.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        TreeNodes = GetTreeNodes();
        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        foreach (var node in allTreeNodes)
        {
            if (expandedKeys.Contains(node.Key))
            {
                node.IsExpanded = true;
            }
        }

        treeNodesByKey = allTreeNodes
            .GroupBy(static node => node.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(TreeNodes));

        if (targetKey is not null && treeNodesByKey.TryGetValue(targetKey, out var matchedNode))
        {
            SelectedTreeNode = matchedNode;
        }
        else
        {
            SelectedTreeNode = TreeNodes.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void OpenOptions()
    {
        if (OptionsRequested is null)
        {
            StatusText = LocalizationService.Current.Get(TextKeys.OptionsDialogIsNotImplementedYet);
            return;
        }

        var dialog = new OptionsDialogViewModel();
        OptionsRequested.Invoke(this, new OptionsDialogRequestedEventArgs
        {
            Dialog = dialog,
            Complete = (accepted, pluginPath, language) =>
            {
                if (!accepted)
                {
                    return;
                }

                StatusText = LocalizationService.Current.Format(TextKeys.OptionsSavedLanguage, language);
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
            StatusText = LocalizationService.Current.Get(TextKeys.OpeningSourceForgeProjectWebsite);
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenSourceForgeWebsite, ex.Message);
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
            StatusText = LocalizationService.Current.Get(TextKeys.OpeningGitHubProjectArea);
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenGitHubArea, ex.Message);
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        if (AboutRequested is null)
        {
            StatusText = LocalizationService.Current.Get(TextKeys.AboutDialogIsNotImplementedYet);
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
        StatusText = LocalizationService.Current.Format(TextKeys.Expanded, targetNode.Title);
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
        StatusText = LocalizationService.Current.Format(TextKeys.Collapsed, targetNode.Title);
    }

    [RelayCommand]
    private void SetViewMode(string modeKey)
    {
        if (Enum.TryParse<BrowserViewMode>(modeKey, true, out var mode))
        {
            CurrentViewMode = mode;
            StatusText = LocalizationService.Current.Format(TextKeys.ViewMode, GetViewModeDisplayName(mode));
        }
    }

    [RelayCommand]
    private void SetSortMode(string sortKey)
    {
        CurrentSortMode = NormalizeSortMode(sortKey);
        RefreshBrowserItemsForSelection();
        StatusText = LocalizationService.Current.Format(TextKeys.ArrangeIconsBy, LocalizationService.Current.Get(CurrentSortMode switch { "Type" => TextKeys.Type, "Size" => TextKeys.Size, _ => TextKeys.Name2 }));
    }

    [RelayCommand]
    private void ToggleStatusBar()
    {
        IsStatusBarVisible = !IsStatusBarVisible;
    }

    [RelayCommand]
    private void Refresh()
    {
        StartOperation(LocalizationService.Current.Get(TextKeys.UpdatingView));
        SetProgress(60, LocalizationService.Current.Get(TextKeys.ParsingCatalog));
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
        StatusText = LocalizationService.Current.Format(TextKeys.Copied, SelectedBrowserItem.Name);
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
        StatusText = LocalizationService.Current.Format(TextKeys.Pasted, ClipboardItem.Name);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToFolder))]
    private void NavigateToFolder()
    {
        if (TryResolveNodeFromBrowserSelection(out var targetNode))
        {
            SelectedTreeNode = targetNode;
            StatusText = LocalizationService.Current.Format(TextKeys.NavigatedTo, targetNode.Title);
        }
    }

    private bool CanNavigateToFolder()
    {
        return TryResolveNodeFromBrowserSelection(out _);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToParentFolder))]
    private void NavigateToParentFolder()
    {
        if (SelectedTreeNode is null)
        {
            return;
        }

        var parentNode = FindParentTreeNode(TreeNodes, SelectedTreeNode.Key);
        if (parentNode is not null)
        {
            SelectedTreeNode = parentNode;
            StatusText = LocalizationService.Current.Format(TextKeys.NavigatedTo, parentNode.Title);
        }
    }

    private bool CanNavigateToParentFolder()
    {
        return SelectedTreeNode is not null && FindParentTreeNode(TreeNodes, SelectedTreeNode.Key) is not null;
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
        StatusText = LocalizationService.Current.Format(TextKeys.Cut, SelectedBrowserItem.Name);
    }

    [RelayCommand(CanExecute = nameof(IsDeleteEnabled))]
    private void ApplyBrowserItemStatus(string? statusName)
    {
        var targets = GetSelectedBrowserItems();
        if (targets.Count == 0)
        {
            return;
        }

        var status = statusVariants.FirstOrDefault(variant =>
            string.Equals(variant.Name, statusName, StringComparison.OrdinalIgnoreCase));

        if (status is null)
        {
            foreach (var target in targets)
            {
                target.Status = null;
                target.Properties.Remove(ItemStatusNamePropertyKey);
                target.Properties.Remove(ItemStatusIconGlyphPropertyKey);
                target.Properties.Remove(ItemStatusIconColorPropertyKey);
                target.Properties.Remove(ItemStatusIconTextPropertyKey);
            }

            StatusText = targets.Count == 1
                ? LocalizationService.Current.Format(TextKeys.ClearedStatusFor, targets[0].Name)
                : LocalizationService.Current.Format(TextKeys.ClearedStatusForItems, targets.Count);
        }
        else
        {
            var applicableTargets = targets
                .Where(target => IsStatusApplicableToItem(status, target))
                .ToArray();
            foreach (var target in applicableTargets)
            {
                target.Status = status.Name;
                target.Properties[ItemStatusNamePropertyKey] = status.Name;
                target.Properties[ItemStatusIconGlyphPropertyKey] = status.IconGlyph;
                target.Properties[ItemStatusIconColorPropertyKey] =
                    string.IsNullOrWhiteSpace(status.IconColor) ? "#FFFFFF" : status.IconColor;
                target.Properties[ItemStatusIconTextPropertyKey] = ResolveMenuStatusIconText(status.IconGlyph);
            }

            StatusText = applicableTargets.Length == 0
                ? LocalizationService.Current.Format(TextKeys.StatusIsNotSupportedForSelectedItemTypes, status.Name)
                : applicableTargets.Length == 1
                    ? LocalizationService.Current.Format(TextKeys.StatusAppliedTo, status.Name, applicableTargets[0].Name)
                    : LocalizationService.Current.Format(TextKeys.StatusAppliedToItems, status.Name, applicableTargets.Length);
        }

        IsDirtyDocument = true;
        RefreshBrowserItemsForSelection();
    }

    public void ApplySessionState(BrowserViewMode viewMode, string? sortMode, bool isStatusBarVisible)
    {
        CurrentViewMode = viewMode;
        CurrentSortMode = NormalizeSortMode(sortMode);
        IsStatusBarVisible = isStatusBarVisible;
        RefreshBrowserItemsForSelection();
    }

    public void SetStatusVariants(IEnumerable<StatusVariantDocument>? variants)
    {
        statusVariants.Clear();
        foreach (var variant in (variants ?? [])
                     .Where(static variant => !string.IsNullOrWhiteSpace(variant.Name))
                     .Select(static variant => new StatusVariantDocument
                     {
                         Name = variant.Name.Trim(),
                         IconGlyph = variant.IconGlyph.Trim(),
                         IconColor = string.IsNullOrWhiteSpace(variant.IconColor) ? "#FFFFFF" : variant.IconColor.Trim(),
                         ItemTypes = variant.ItemTypes
                     }))
        {
            statusVariants.Add(variant);
        }

        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
    }

    private static string GetViewModeDisplayName(BrowserViewMode viewMode)
    {
        return viewMode switch
        {
            BrowserViewMode.SmallIcons => LocalizationService.Current.Get(TextKeys.SmallIcons),
            BrowserViewMode.LargeIcons => LocalizationService.Current.Get(TextKeys.LargeIcons),
            BrowserViewMode.Tiles => LocalizationService.Current.Get(TextKeys.Tiles2),
            BrowserViewMode.List => LocalizationService.Current.Get(TextKeys.List2),
            BrowserViewMode.Details => LocalizationService.Current.Get(TextKeys.Details2),
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
            SelectedBrowserItem.Type is CatalogDocumentType.Folder
                or CatalogDocumentType.NetworkFolder
                or CatalogDocumentType.Media)
        {
            if (treeNodesByKey.TryGetValue(SelectedBrowserItem.Id, out targetNode))
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
                GetBrowserItemInfoProperties(SelectedTreeNode.Key),
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
            SelectedBrowserItems = new List<CatalogDocument>();
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
        SelectedBrowserItems = SelectedBrowserItem is not null
            ? new List<CatalogDocument> { SelectedBrowserItem }
            : new List<CatalogDocument>();
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
        OnPropertyChanged(nameof(IsDeleteEnabled));
        OnPropertyChanged(nameof(IsPropertiesEnabled));
        DeleteItemCommand.NotifyCanExecuteChanged();
        OpenPropertiesCommand.NotifyCanExecuteChanged();
        ExpandSelectionCommand.NotifyCanExecuteChanged();
        CollapseSelectionCommand.NotifyCanExecuteChanged();
        NavigateToFolderCommand.NotifyCanExecuteChanged();
        NavigateToParentFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
        RefreshTopMenuState();
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
        if (value is null)
        {
            if (selectedBrowserItems.Count > 0)
            {
                SelectedBrowserItems = new List<CatalogDocument>();
            }
        }
        else if (selectedBrowserItems.Count <= 1 ||
                 !selectedBrowserItems
                     .OfType<CatalogDocument>()
                     .Any(item => string.Equals(item.Id, value.Id, StringComparison.Ordinal)))
        {
            SelectedBrowserItems = new List<CatalogDocument> { value };
        }

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
        ApplyBrowserItemStatusCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBrowserItemsChanged(System.Collections.IList value)
    {
        if (value.Count > 0)
        {
            var first = value[0] as CatalogDocument;
            if (!ReferenceEquals(selectedBrowserItem, first))
            {
                selectedBrowserItem = first;
                OnPropertyChanged(nameof(SelectedBrowserItem));
            }
        }
        else if (selectedBrowserItem is not null)
        {
            selectedBrowserItem = null;
            OnPropertyChanged(nameof(SelectedBrowserItem));
        }

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
        ApplyBrowserItemStatusCommand.NotifyCanExecuteChanged();
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
        var menuItems = new List<MainMenuItemViewModel>
        {
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.File),
                Items =
                [
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.New), HotKey = "Ctrl+N", Command = NewCatalogCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Open), HotKey = "Ctrl+O", Command = OpenCatalogCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Save), HotKey = "Ctrl+S", Command = SaveCatalogCommand },
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.SaveAs), HotKey = "F12", Command = SaveCatalogAsCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Properties), Command = OpenPropertiesCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Exit), Command = ExitApplicationCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.Edit),
                Items =
                [
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Undo), HotKey = "Ctrl+Z", Command = UndoCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Redo), HotKey = "Ctrl+Y", Command = RedoCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Add), HotKey = "F2", Command = AddItemCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Delete), HotKey = "Delete", Command = DeleteItemCommand },
                    new MainMenuItemViewModel
                    {
                        Key = "edit_status",
                        Header = LocalizationService.Current.Get(TextKeys.Status),
                        IsEnabled = IsDeleteEnabled,
                        Items = BuildStatusMenuItems()
                    },
                    Separator(),
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.Properties2), HotKey = "Alt+Enter", Command = OpenPropertiesCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.View),
                Items =
                [
                    CheckedMenuItem(IsStatusBarVisible, LocalizationService.Current.Get(TextKeys.StatusBar), ToggleStatusBarCommand, key: "statusbar"),
                    Separator(),
                    CheckedMenuItem(IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles), SetViewModeCommand, "Tiles", "view_tiles"),
                    CheckedMenuItem(IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2), SetViewModeCommand, "SmallIcons",
                        "view_small"),
                    CheckedMenuItem(IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu), SetViewModeCommand, "LargeIcons",
                        "view_large"),
                    CheckedMenuItem(IsListViewChecked, LocalizationService.Current.Get(TextKeys.List), SetViewModeCommand, "List", "view_list"),
                    CheckedMenuItem(IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details), SetViewModeCommand, "Details", "view_details"),
                    Separator(),
                    new MainMenuItemViewModel
                    {
                        Header = LocalizationService.Current.Get(TextKeys.ArrangeIconsBy2),
                        Items =
                        [
                            CheckedMenuItem(IsSortByNameChecked, LocalizationService.Current.Get(TextKeys.Name), SetSortModeCommand, "Name", "sort_name"),
                            CheckedMenuItem(IsSortByTypeChecked, LocalizationService.Current.Get(TextKeys.Type2), SetSortModeCommand, "Type", "sort_type"),
                            CheckedMenuItem(IsSortBySizeChecked, LocalizationService.Current.Get(TextKeys.Size2), SetSortModeCommand, "Size", "sort_size")
                        ]
                    },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Refresh), HotKey = "F5", Command = RefreshCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.Tools),
                Items = GetToolsMenuItems()
            },
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.Help),
                Items =
                [
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.ProjectWebsiteInSourceForgeNET), Command = OpenProjectWebsiteCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.ProjectAreaInGitHub), Command = OpenGithubAreaCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.About), Command = OpenAboutCommand }
                ]
            }
        };

        return menuItems;
    }

    private IReadOnlyList<MainMenuItemViewModel> GetToolsMenuItems()
    {
        var toolsMenuItems = new List<MainMenuItemViewModel>
        {
            new MainMenuItemViewModel
                { Header = LocalizationService.Current.Get(TextKeys.Options), HotKey = "Ctrl+Alt+O", Command = OpenOptionsCommand }
        };

        // Add plugin menu contributions to Tools menu
        var menuContributions = menuExtensionManager.GetMenuContributions("Tools");
        if (menuContributions.Any())
        {
            toolsMenuItems.Add(Separator());

            foreach (var contribution in menuContributions)
            {
                toolsMenuItems.Add(new MainMenuItemViewModel
                {
                    Header = contribution.Title,
                    Command = new RelayCommand(() => ExecutePluginMenuCommand(contribution.CommandId))
                });
            }
        }

        return toolsMenuItems;
    }

    private async void ExecutePluginMenuCommand(string commandId)
    {
        if (pluginCommandCancellation is not null) { pluginCommandCancellation.Cancel(); return; }
        using var cancellation = new CancellationTokenSource();
        pluginCommandCancellation = cancellation;
        try
        {
            StatusText = LocalizationService.Current.Format(TextKeys.ExecutingPluginCommand, commandId);

            var context = new MenuCommandContext
            {
                ActiveCatalogId = null,
                SelectedNodeId = null,
                Properties = null,
                HostApi = new ViewModelHostCommandApi(this)
            };

            var result = await menuExtensionManager.ExecuteAsync(commandId, context,
                commandId == "motion-detector.capture" ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30), cancellation.Token);

            if (result.TimedOut)
            {
                StatusText = LocalizationService.Current.Format(TextKeys.PluginCommandTimedOut, commandId);
            }
            else if (!result.Success)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? LocalizationService.Current.Format(TextKeys.PluginCommandFailed, commandId)
                    : LocalizationService.Current.Format(TextKeys.PluginCommandFailed2, result.Error);
            }
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Current.Format(TextKeys.PluginCommandError, ex.Message);
        }
        finally { pluginCommandCancellation = null; }
    }

    private sealed class ViewModelHostCommandApi(MainWindowViewModel viewModel) : ICatalogCaptureHostApi
    {
        private readonly string? rootId = viewModel.TreeNodes.FirstOrDefault()?.Key;
        private readonly string? catalogPath = viewModel.CurrentCatalogPath;

        public async Task ImportCapturedFilesAsync(IReadOnlyList<CapturedCatalogFile> files, CancellationToken cancellationToken = default)
        {
            var repository = viewModel.catalogRepository;
            if (repository is null || rootId is null || catalogPath != viewModel.CurrentCatalogPath)
                throw new MotionCaptureCatalogUnavailableException();
            await Task.Run(() => repository.Collection.Database.InBatch(() =>
            {
                string Folder(string parent, string name)
                {
                    var existing = repository.GetChildrenOf(parent).FirstOrDefault(x => x.Name == name && x.Type == CatalogDocumentType.Folder);
                    if (existing is not null) return existing.Id;
                    var id = Guid.NewGuid().ToString("N");
                    repository.Save(id, new CatalogDocument { Id = id, ParentId = parent, Name = name, Type = CatalogDocumentType.Folder });
                    return id;
                }
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parent = Folder(Folder(rootId, file.Folder), file.Date);
                    var id = Guid.NewGuid().ToString("N");
                    var properties = new PropertiesCollection();
                    foreach (var property in file.Properties) properties[property.Key] = property.Value;
                    repository.Save(id, new CatalogDocument { Id = id, ParentId = parent, Name = file.Name, Size = file.Size, Properties = properties });
                }
            }), cancellationToken);
            viewModel.RefreshTreeNodes();
            viewModel.RefreshBrowserItemsForSelection();
            viewModel.IsDirtyDocument = true;
        }
        public Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default)
        {
            viewModel.StatusText = LocalizationService.Current.Format(TextKeys.NavigatedToNode, nodeId);
            return Task.CompletedTask;
        }

        public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
        {
            viewModel.StatusText = message;
            return Task.CompletedTask;
        }
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

        SetMenuHeader(byKey, "statusbar", CheckedHeader(IsStatusBarVisible, LocalizationService.Current.Get(TextKeys.StatusBar)));
        SetMenuHeader(byKey, "view_tiles", CheckedHeader(IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles)));
        SetMenuHeader(byKey, "view_small", CheckedHeader(IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2)));
        SetMenuHeader(byKey, "view_large", CheckedHeader(IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu)));
        SetMenuHeader(byKey, "view_list", CheckedHeader(IsListViewChecked, LocalizationService.Current.Get(TextKeys.List)));
        SetMenuHeader(byKey, "view_details", CheckedHeader(IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details)));
        SetMenuHeader(byKey, "sort_name", CheckedHeader(IsSortByNameChecked, LocalizationService.Current.Get(TextKeys.Name)));
        SetMenuHeader(byKey, "sort_type", CheckedHeader(IsSortByTypeChecked, LocalizationService.Current.Get(TextKeys.Type2)));
        SetMenuHeader(byKey, "sort_size", CheckedHeader(IsSortBySizeChecked, LocalizationService.Current.Get(TextKeys.Size2)));
        SetMenuEnabled(byKey, "edit_status", IsDeleteEnabled);
        SetMenuItems(byKey, "edit_status", BuildStatusMenuItems());
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

    private static void SetMenuEnabled(
        IReadOnlyDictionary<string, MainMenuItemViewModel> byKey,
        string key,
        bool isEnabled)
    {
        if (byKey.TryGetValue(key, out var item))
        {
            item.IsEnabled = isEnabled;
        }
    }

    private static void SetMenuItems(
        IReadOnlyDictionary<string, MainMenuItemViewModel> byKey,
        string key,
        IReadOnlyList<MainMenuItemViewModel> items)
    {
        if (byKey.TryGetValue(key, out var item))
        {
            item.Items = items;
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
                { Header = LocalizationService.Current.Get(TextKeys.Expand), Command = ExpandSelectionCommand, CommandParameter = "list" },
            new MainMenuItemViewModel
                { Header = LocalizationService.Current.Get(TextKeys.Collapse), Command = CollapseSelectionCommand, CommandParameter = "list" },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.Status),
                Items = BuildStatusMenuItems()
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.View),
                Items =
                [
                    CheckedMenuItem(IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2), SetViewModeCommand, "SmallIcons"),
                    CheckedMenuItem(IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu), SetViewModeCommand, "LargeIcons"),
                    CheckedMenuItem(IsListViewChecked, LocalizationService.Current.Get(TextKeys.List), SetViewModeCommand, "List"),
                    CheckedMenuItem(IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details), SetViewModeCommand, "Details"),
                    CheckedMenuItem(IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles), SetViewModeCommand, "Tiles")
                ]
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.ArrangeIconsBy2),
                Items =
                [
                    CheckedMenuItem(IsSortByNameChecked, LocalizationService.Current.Get(TextKeys.Name), SetSortModeCommand, "Name"),
                    CheckedMenuItem(IsSortByTypeChecked, LocalizationService.Current.Get(TextKeys.Type2), SetSortModeCommand, "Type")
                ]
            },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Refresh), Command = RefreshCommand },
            Separator(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Add), Command = AddItemCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Delete), Command = DeleteItemCommand, CommandParameter = "list" },
            Separator(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Properties), Command = OpenPropertiesCommand }
        ];
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildTreeContextMenuItems()
    {
        return
        [
            new MainMenuItemViewModel
                { Header = LocalizationService.Current.Get(TextKeys.Expand), Command = ExpandSelectionCommand, CommandParameter = "tree" },
            new MainMenuItemViewModel
                { Header = LocalizationService.Current.Get(TextKeys.Collapse), Command = CollapseSelectionCommand, CommandParameter = "tree" },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.View),
                Items =
                [
                    CheckedMenuItem(IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2), SetViewModeCommand, "SmallIcons"),
                    CheckedMenuItem(IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu), SetViewModeCommand, "LargeIcons"),
                    CheckedMenuItem(IsListViewChecked, LocalizationService.Current.Get(TextKeys.List), SetViewModeCommand, "List"),
                    CheckedMenuItem(IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details), SetViewModeCommand, "Details"),
                    CheckedMenuItem(IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles), SetViewModeCommand, "Tiles")
                ]
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.ArrangeIconsBy2),
                Items =
                [
                    CheckedMenuItem(IsSortByNameChecked, LocalizationService.Current.Get(TextKeys.Name), SetSortModeCommand, "Name"),
                    CheckedMenuItem(IsSortByTypeChecked, LocalizationService.Current.Get(TextKeys.Type2), SetSortModeCommand, "Type"),
                    CheckedMenuItem(IsSortBySizeChecked, LocalizationService.Current.Get(TextKeys.Size2), SetSortModeCommand, "Size")
                ]
            },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Refresh), Command = RefreshCommand },
            Separator(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Add), Command = AddItemCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Delete), Command = DeleteItemCommand, CommandParameter = "tree" },
            Separator(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Copy), HotKey = "Ctrl+C", Command = CopyCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Paste), HotKey = "Ctrl+V", Command = PasteCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.CutMenu), HotKey = "Ctrl+X", Command = CutCommand },
            Separator(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Properties), Command = OpenPropertiesCommand }
        ];
    }

    private void EnsureSeedData()
    {
        if (!shouldSeedDefaultCatalog || catalogRepository is null || catalogRepository.GetAll().Count > 0)
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
            return BuildTreeNodesFromEntries(catalogRepository.GetAll());
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
            return entries;
        }

        var inMemoryEntries = (inMemoryCatalogEntries ?? [])
            .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
            .ToArray();
        return inMemoryEntries;
    }

    private void DeleteCatalogEntryFromRepository(CatalogDocument item)
    {
        DeleteCatalogEntriesFromRepository([item]);
    }

    private void DeleteCatalogEntriesFromRepository(IReadOnlyList<CatalogDocument> items)
    {
        if (catalogRepository is null || items.Count == 0)
        {
            return;
        }

        var itemIds = items
            .Select(static entry => entry.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var descendantIds = catalogRepository
            .GetDescendantsOf(itemIds)
            .Select(static entry => entry.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var allIdsToDelete = itemIds.Concat(descendantIds).Distinct(StringComparer.Ordinal).ToArray();

        catalogRepository.Collection.Database.InBatch(() =>
        {
            foreach (var id in allIdsToDelete)
            {
                using var descendantDocument = catalogRepository.Collection.GetDocument(id);
                if (descendantDocument is not null)
                {
                    catalogRepository.Collection.Delete(descendantDocument);
                }
            }
        });

        foreach (var descendantId in allIdsToDelete)
        {
            addedItemsByNodeKey.Remove(descendantId);
            deletedItemNamesByNodeKey.Remove(descendantId);
            renamedBrowserItemNamesByNodeKey.Remove(descendantId);
        }
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
            entry.IconGlyph,
            children,
            isExpanded);
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodesFromEntries(IReadOnlyList<CatalogDocument> entries)
    {
        var filteredEntries = entries
            .Where(IsTreeVisibleType)
            .ToList();
        var byParent = filteredEntries
            .GroupBy(
                entry => string.IsNullOrWhiteSpace(entry.ParentId) ? string.Empty : entry.ParentId,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        if (!byParent.TryGetValue(string.Empty, out var roots))
        {
            return [];
        }

        return roots
            .Select(root => BuildTreeNodeFromLookup(root, byParent, isExpanded: true))
            .ToArray();
    }

    private static bool IsTreeVisibleType(CatalogDocument entry)
    {
        return entry.Type is CatalogDocumentType.Folder
            or CatalogDocumentType.NetworkFolder
            or CatalogDocumentType.Media;
    }

    private static bool HasAssignedStatus(CatalogDocument? item)
    {
        if (item is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(item.Status))
        {
            return true;
        }

        return item.Properties.TryGetValue(ItemStatusNamePropertyKey, out var value) &&
               !string.IsNullOrWhiteSpace(value?.ToString());
    }

    private static bool IsAssignedStatus(CatalogDocument? item, string? expectedStatusName)
    {
        if (item is null)
        {
            return false;
        }

        var assignedStatusName = !string.IsNullOrWhiteSpace(item.Status)
            ? item.Status
            : item.Properties.TryGetValue(ItemStatusNamePropertyKey, out var value)
                ? value?.ToString()
                : null;

        if (string.IsNullOrWhiteSpace(assignedStatusName))
        {
            return expectedStatusName is null;
        }

        if (expectedStatusName is null)
        {
            return true;
        }

        return string.Equals(assignedStatusName, expectedStatusName, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildStatusMenuItems()
    {
        var hasDefaultAssigned = !HasAssignedStatus(SelectedBrowserItem);

        var statusItems = new List<MainMenuItemViewModel>
        {
            new()
            {
                Header = LocalizationService.Current.Get(TextKeys.Default),
                Icon = "○",
                Command = ApplyBrowserItemStatusCommand,
                CommandParameter = null,
                IsEnabled = !hasDefaultAssigned
            }
        };

        statusItems.AddRange(statusVariants
            .Select(variant =>
            {
                var isAssigned = IsAssignedStatus(SelectedBrowserItem, variant.Name);
                return new MainMenuItemViewModel
                {
                    Header = variant.Name,
                    Icon = new StatusMenuIcon(variant.IconGlyph, variant.IconColor),
                    Command = ApplyBrowserItemStatusCommand,
                    CommandParameter = variant.Name,
                    IsEnabled = !isAssigned
                };
            }));

        return statusItems;
    }

    private IReadOnlyList<CatalogDocument> GetSelectedBrowserItems()
    {
        var validBrowserItems = BrowserItems;
        if (validBrowserItems.Count == 0)
        {
            return [];
        }

        var validIds = new HashSet<string>(
            validBrowserItems.Select(static item => item.Id).Where(static id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        var validNames = new HashSet<string>(
            validBrowserItems.Select(static item => item.Name),
            StringComparer.OrdinalIgnoreCase);

        if (SelectedBrowserItems.Count > 0)
        {
            var selected = SelectedBrowserItems
                .OfType<CatalogDocument>()
                .Where(item => validIds.Contains(item.Id) || validNames.Contains(item.Name))
                .ToArray();

            if (selected.Length > 0)
            {
                return selected;
            }
        }

        if (SelectedBrowserItem is not null &&
            (validIds.Contains(SelectedBrowserItem.Id) || validNames.Contains(SelectedBrowserItem.Name)))
        {
            return [SelectedBrowserItem];
        }

        return [];
    }

    private static bool IsStatusApplicableToItem(StatusVariantDocument status, CatalogDocument item)
    {
        return IsStatusApplicableToType(status, item.Type);
    }

    private static bool IsStatusApplicableToType(StatusVariantDocument status, CatalogDocumentType itemType)
    {
        if (status.ItemTypes is { Count: > 0 })
        {
            return status.ItemTypes.Contains(itemType);
        }

        return true;
    }

    private static string ResolveMenuStatusIconText(string iconGlyphKey) => iconGlyphKey;
}
