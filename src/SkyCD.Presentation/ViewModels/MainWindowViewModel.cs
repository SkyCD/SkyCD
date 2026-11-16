using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using SkyCD.UI.Controls.Preview;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Staging;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Core.UndoRedo;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable localizationSubscription;
    private bool isDisposed;
    private bool previewArtifactReclaimPending;
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly CatalogUndoManager? undoManager;
    private readonly bool shouldSeedDefaultCatalog;
    private readonly List<CatalogDocument>? inMemoryCatalogEntries;
    private readonly IStringLocalizer propertyValueLocalizer;
    private Dictionary<string, BrowserTreeNode> treeNodesByKey;
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
    private readonly IMediaCacheStore mediaCacheStore;
    private readonly bool ownsMediaCacheStore;
    private readonly MediaPreviewImageService mediaPreviewImages;
    private readonly MediaPreviewResolver mediaPreviewResolver;
    private readonly EntryPreviewViewModel entryPreview;
    private readonly MediaCacheThumbnailProvider browserThumbnailProvider;
    private readonly MediaCacheSearchFieldProvider searchFieldProvider;
    private IReadOnlyList<MainMenuItemViewModel> topMenuItems = [];

    // Cached rather than computed on each get: an open ContextMenu binds ItemsSource to these, and
    // handing it a new list tears down its items mid-click. State changes go through
    // RefreshTopMenuState, which mutates the cached items in place.
    private IReadOnlyList<MainMenuItemViewModel> browserContextMenuItems = [];
    private IReadOnlyList<MainMenuItemViewModel> treeContextMenuItems = [];
    private static string DefaultStatusText => LocalizationService.Current.Get(TextKeys.Done);
    private const string ItemStatusNamePropertyKey = "StatusName";
    private const string ItemStatusIconGlyphPropertyKey = "StatusIconGlyph";
    private const string ItemStatusIconColorPropertyKey = "StatusIconColor";
    private const string ItemStatusIconTextPropertyKey = "StatusIconText";

    public event EventHandler? AddToListRequested;
    public event EventHandler? NewCatalogRequested;
    public event EventHandler? OpenCatalogRequested;
    public event EventHandler? OpenCatalogFolderRequested;
    public event EventHandler? SaveCatalogAsRequested;
    public event EventHandler? SaveCatalogRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? FindCatalogRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? FocusBrowserRequested;
    public event EventHandler? FocusTreeRequested;

    private MenuExtensionManager menuExtensionManager;
    private CancellationTokenSource? pluginCommandCancellation;

    public void RefreshPluginMenuServices(MenuExtensionManager updatedManager)
    {
        menuExtensionManager = updatedManager ?? throw new ArgumentNullException(nameof(updatedManager));
        RebuildMenuItems();
        OnPropertyChanged(nameof(TopMenuItems));
        OnPropertyChanged(nameof(FileMenuItems));
        OnPropertyChanged(nameof(EditMenuItems));
        OnPropertyChanged(nameof(ViewMenuItems));
        OnPropertyChanged(nameof(ToolsMenuItems));
        OnPropertyChanged(nameof(HelpMenuItems));
        OnPropertyChanged(nameof(BrowserContextMenuItems));
        OnPropertyChanged(nameof(TreeContextMenuItems));
    }

    private void RebuildMenuItems()
    {
        topMenuItems = BuildTopMenuItems();
        browserContextMenuItems = BuildBrowserContextMenuItems();
        treeContextMenuItems = BuildTreeContextMenuItems();
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

        mediaCacheStore = MediaCacheStore.Create(this.catalogRepository.Collection?.Database);
        ownsMediaCacheStore = true;
        mediaPreviewResolver = new MediaPreviewResolver(mediaCacheStore);
        mediaPreviewImages = new MediaPreviewImageService(mediaPreviewResolver);
        entryPreview = new EntryPreviewViewModel(mediaPreviewResolver, mediaPreviewImages, this.propertyValueLocalizer);
        browserThumbnailProvider = new MediaCacheThumbnailProvider(mediaPreviewResolver, mediaPreviewImages);
        searchFieldProvider = new MediaCacheSearchFieldProvider(mediaCacheStore);

        EnsureSeedData();
        treeNodes.Clear();
        foreach (var node in GetTreeNodes()) treeNodes.Add(node);

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        RebuildMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
        localizationSubscription = LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
        AttachHistoryNotifications();

        try
        {
            if (this.catalogRepository?.Collection?.Database is { } db)
            {
                using var store = new CouchbaseScanStagingStore(db);
                store.CleanupAbandonedSessions(TimeSpan.FromHours(24));
            }
        }
        catch
        {
            // Best effort cleanup of abandoned staging sessions on startup
        }
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
        MenuExtensionManager? menuExtensionManager = null,
        IMediaCacheStore? mediaCacheStore = null)
    {
        inMemoryCatalogEntries = catalogEntries?.ToList() ?? throw new ArgumentNullException(nameof(catalogEntries));
        this.propertyValueLocalizer = propertyValueLocalizer ?? new PropertyValueLocalizer();
        this.menuExtensionManager = menuExtensionManager ?? CreateEmptyMenuExtensionManager();
        shouldSeedDefaultCatalog = false;

        this.mediaCacheStore = mediaCacheStore ?? MediaCacheStore.Create();
        ownsMediaCacheStore = mediaCacheStore is null;
        mediaPreviewResolver = new MediaPreviewResolver(this.mediaCacheStore);
        mediaPreviewImages = new MediaPreviewImageService(mediaPreviewResolver);
        entryPreview = new EntryPreviewViewModel(mediaPreviewResolver, mediaPreviewImages, this.propertyValueLocalizer);
        browserThumbnailProvider = new MediaCacheThumbnailProvider(mediaPreviewResolver, mediaPreviewImages);
        searchFieldProvider = new MediaCacheSearchFieldProvider(this.mediaCacheStore);

        treeNodes.Clear();
        foreach (var node in BuildTreeNodesFromEntries(inMemoryCatalogEntries)) treeNodes.Add(node);

        var allTreeNodes = FlattenNodes(TreeNodes).ToArray();
        treeNodesByKey = allTreeNodes.ToDictionary(static node => node.Key, StringComparer.OrdinalIgnoreCase);
        RebuildMenuItems();
        SelectedTreeNode = TreeNodes.FirstOrDefault();
        RefreshBrowserItemsForSelection();
        RefreshTopMenuState();
        localizationSubscription = LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
        AttachHistoryNotifications();
    }

    /// <summary>
    /// Wires the bounded history's notifications so capacity events reach the user. Without this the history
    /// silently drops its oldest entry and an operation that could not be recorded leaves Undo pointing at
    /// something else entirely.
    /// </summary>
    private void AttachHistoryNotifications()
    {
        if (undoManager is null)
        {
            return;
        }

        undoManager.ActionEvicted += OnHistoryActionEvicted;
        undoManager.ActionRejected += OnHistoryActionRejected;
    }

    public void RefreshLocalization()
    {
        if (isDisposed) return;
        RefreshPluginMenuServices(menuExtensionManager);
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        StatusText = DefaultStatusText;
        entryPreview.RefreshLocalization();
        RefreshBrowserItemsForSelection();
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        localizationSubscription.Dispose();

        if (undoManager is not null)
        {
            undoManager.ActionEvicted -= OnHistoryActionEvicted;
            undoManager.ActionRejected -= OnHistoryActionRejected;

            // Disposing history disposes every retained action, which releases its cache pins. Doing this
            // here is what stops a discarded view model from pinning preview artifacts forever.
            undoManager.Dispose();
        }

        entryPreview.Dispose();
        mediaPreviewImages.Dispose();
        if (ownsMediaCacheStore)
        {
            mediaCacheStore.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public static MainWindowViewModel CreateForInMemoryCatalog(
        IReadOnlyList<CatalogDocument> catalogEntries,
        IStringLocalizer? propertyValueLocalizer = null,
        MenuExtensionManager? menuExtensionManager = null,
        IMediaCacheStore? mediaCacheStore = null)
    {
        return new MainWindowViewModel(catalogEntries, propertyValueLocalizer, menuExtensionManager, mediaCacheStore);
    }

    /// <summary>
    /// The offline details and preview surface of the browser. It follows the browser selection and renders
    /// only what is already stored, so it keeps working while the media is disconnected.
    /// </summary>
    public EntryPreviewViewModel Preview => entryPreview;

    /// <summary>Cached-thumbnail source for realized browser items.</summary>
    public IPreviewThumbnailProvider BrowserThumbnailProvider => browserThumbnailProvider;

    /// <summary>The store previews and captured metadata are read from.</summary>
    public IMediaCacheStore MediaCache => mediaCacheStore;

    /// <summary>
    /// The bounded undo/redo history for the open catalog, or null when the view model runs without one.
    /// </summary>
    /// <remarks>
    /// Exposed so a host can size the history and its per-action footprint budget, read the pending action
    /// names for menu tooltips, and observe capacity events. Snapshot capture and replay stay internal to the
    /// recorded actions; this is configuration and visibility, not a way to push arbitrary edits.
    /// </remarks>
    public CatalogUndoManager? UndoManager => undoManager;

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

    private ObservableCollection<BrowserTreeNode> treeNodes = new ObservableCollection<BrowserTreeNode>();
    public IReadOnlyList<BrowserTreeNode> TreeNodes => treeNodes;

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

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

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

    public IReadOnlyList<MainMenuItemViewModel> BrowserContextMenuItems => browserContextMenuItems;

    public IReadOnlyList<MainMenuItemViewModel> TreeContextMenuItems => treeContextMenuItems;

    [ObservableProperty] private ObservableCollection<CatalogDocument> browserItems = [];

    [ObservableProperty] private string? selectedContentsNotice;

    [ObservableProperty] private bool showContentsNotScannedNotice;

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

    /// <summary>Whether the offline details and preview pane is shown under the browser.</summary>
    [ObservableProperty]
    private bool isPreviewPaneVisible = true;

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
    private void FindCatalog() => FindCatalogRequested?.Invoke(this, EventArgs.Empty);

    public CatalogFindDialogViewModel CreateCatalogFindDialogViewModel() =>
        new(GetAllCatalogEntries(), NavigateToFindResult, searchFieldProvider);

    private IReadOnlyList<CatalogDocument> GetAllCatalogEntries() =>
        catalogRepository?.GetAll() ?? (IReadOnlyList<CatalogDocument>)(inMemoryCatalogEntries?.ToArray() ?? []);

    private void NavigateToFindResult(CatalogFindResult result)
    {
        var document = result.Document;
        var nodeKey = IsTreeVisibleType(document) ? document.Id : document.ParentId;
        if (string.IsNullOrWhiteSpace(nodeKey) || !treeNodesByKey.TryGetValue(nodeKey, out var node)) return;

        ExpandAncestors(node.Key);
        SelectedTreeNode = node;
        if (!IsTreeVisibleType(document))
        {
            SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
                string.Equals(item.Id, document.Id, StringComparison.Ordinal));
        }
    }

    private void ExpandAncestors(string nodeKey)
    {
        var current = FindParentTreeNode(TreeNodes, nodeKey);
        while (current is not null)
        {
            current.IsExpanded = true;
            current = FindParentTreeNode(TreeNodes, current.Key);
        }
    }

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

        // History describes edits to the catalog that was open. Carrying it into a new catalog would let Undo
        // write records into a database they no longer belong to, and would keep preview artifacts pinned for
        // entries that no longer exist.
        ResetUndoHistory();

        RefreshBrowserItemsForSelection();
        IsDirtyDocument = false;
        StatusText = LocalizationService.Current.Get(TextKeys.CreatedANewCatalog);
    }

    /// <summary>Drops all undo/redo entries and releases the cache pins they held.</summary>
    private void ResetUndoHistory()
    {
        if (undoManager is null)
        {
            return;
        }

        undoManager.Clear();
        previewArtifactReclaimPending = false;
        ReclaimUnreferencedPreviewArtifacts();
        UpdateUndoRedoState();
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

    [RelayCommand]
    private void OpenCatalogFolder()
    {
        if (OpenCatalogFolderRequested is not null)
        {
            OpenCatalogFolderRequested.Invoke(this, EventArgs.Empty);
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
        // Opening a different catalog invalidates every recorded edit: the snapshots refer to records of the
        // catalog that was open, and undoing one against the newly opened database would be meaningless.
        ResetUndoHistory();

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

    private void UpdateUndoRedoState()
    {
        CanUndo = undoManager?.CanUndo ?? false;
        CanRedo = undoManager?.CanRedo ?? false;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        if (undoManager is null)
        {
            return;
        }

        var actionName = undoManager.UndoActionName;
        try
        {
            await undoManager.UndoAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The history has already put the action back on the undo stack, so state is consistent; what
            // must not happen is an exception escaping a command into the dispatcher.
            UpdateUndoRedoState();
            StatusText = LocalizationService.Current.Format(TextKeys.UndoFailed, exception.Message);
            return;
        }

        // Restored records are different instances from whatever the browser still holds, and redo replays
        // captured ids rather than the live selection. Dropping the selection here is what stops a following
        // delete or redo from acting on a detached object that merely shares a name.
        SelectedBrowserItem = null;
        SelectedBrowserItems = new List<CatalogDocument>();

        UpdateUndoRedoState();
        RefreshTreeNodes();
        RevealRestoredTreeNode();
        RefreshBrowserItemsForSelection();
        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.UndoCompleted, actionName ?? string.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task Redo()
    {
        if (undoManager is null)
        {
            return;
        }

        var actionName = undoManager.RedoActionName;
        try
        {
            await undoManager.RedoAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            UpdateUndoRedoState();
            StatusText = LocalizationService.Current.Format(TextKeys.RedoFailed, exception.Message);
            return;
        }

        SelectedBrowserItem = null;
        SelectedBrowserItems = new List<CatalogDocument>();

        UpdateUndoRedoState();
        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();
        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.RedoCompleted, actionName ?? string.Empty);
    }

    /// <summary>
    /// Runs a media cache retention pass that treats history-pinned entries as still present.
    /// </summary>
    /// <remarks>
    /// A deleted entry whose snapshot is still in undo history is not an orphan: restoring it brings back the
    /// same id, name and ancestor chain, which is exactly what the cache fingerprint is built from. Reclaiming
    /// its previews early would leave the restored entry pointing at artifacts that no longer exist. Once
    /// history evicts that snapshot the pin is released and this pass reclaims the records; blob files are
    /// reclaimed by compaction, which is left to explicit maintenance rather than run inline on the UI thread.
    /// </remarks>
    public MediaCacheCleanupResult? ReclaimUnreferencedPreviewArtifacts()
    {
        if (mediaCacheStore is null)
        {
            return null;
        }

        var pins = undoManager?.CachePins;
        return mediaCacheStore.EnforceRetention(
            new MediaCacheRetentionPolicy(CompactAfterCleanup: false),
            entryExists: entryId => EntryExistsForRetention(entryId, pins));
    }

    private bool EntryExistsForRetention(string entryId, CatalogHistoryCachePins? pins)
    {
        if (pins?.IsPinned(entryId) == true)
        {
            return true;
        }

        if (catalogRepository is not null)
        {
            return catalogRepository.Get(entryId) is not null;
        }

        return inMemoryCatalogEntries?.Any(entry =>
            string.Equals(entry.Id, entryId, StringComparison.Ordinal)) == true;
    }

    private void OnHistoryActionEvicted(object? sender, UndoActionEvictedEventArgs e)
    {
        if (isDisposed) return;

        // The eviction released whatever that action pinned, so its previews are now reclaimable.
        previewArtifactReclaimPending = true;
        StatusText = LocalizationService.Current.Format(TextKeys.HistoryCapacityReached, e.DisplayName);
        UpdateUndoRedoState();
    }

    private void OnHistoryActionRejected(object? sender, UndoActionRejectedEventArgs e)
    {
        if (isDisposed) return;
        StatusText = LocalizationService.Current.Get(TextKeys.OperationNotUndoable);
        UpdateUndoRedoState();
    }

    [RelayCommand]
    private void AddItem()
    {
        AddToListRequested?.Invoke(this, EventArgs.Empty);
    }

    public CatalogDocument AddTitleOnlyMedia(string? mediaName, string? parentId)
    {
        var document = TitleOnlyMedia.Create(mediaName, parentId);
        AddImportedDocuments([document]);
        if (treeNodesByKey.TryGetValue(document.Id, out var node))
        {
            SelectedTreeNode = node;
        }

        return document;
    }

    public ScanPublishResult? PublishStagedScan(IStagingStore store, string sessionId, ScanPublishOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (catalogRepository is null)
        {
            var staged = store.GetStagedDocuments(sessionId);
            AddImportedDocuments(staged);
            store.DropSession(sessionId);
            return new ScanPublishResult
            {
                GenerationId = $"gen-{Guid.NewGuid():N}",
                PublishedDocumentsCount = staged.Count,
                PublishedDocuments = staged
            };
        }

        var publisher = new CatalogScanPublisher();
        var result = publisher.Publish(store, sessionId, catalogRepository, options, undoManager?.History);

        UpdateUndoRedoState();
        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();

        if (result.PublishedDocuments.Count > 0)
        {
            var rootImported = result.PublishedDocuments[0];
            SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
                string.Equals(item.Id, rootImported.Id, StringComparison.Ordinal) ||
                item.Name.Equals(rootImported.Name, StringComparison.OrdinalIgnoreCase));
            StatusText = LocalizationService.Current.Format(TextKeys.Added, rootImported.Name);
        }

        IsDirtyDocument = true;
        return result;
    }

    public void AddImportedDocuments(IReadOnlyList<CatalogDocument> documents)
    {
        if (documents is null || documents.Count == 0)
        {
            return;
        }

        if (catalogRepository is not null)
        {
            var publisher = new CatalogScanPublisher();
            publisher.PublishDirect(documents, catalogRepository, new ScanPublishOptions(), undoManager?.History);
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

        UpdateUndoRedoState();
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

        // Capture the index of the currently selected item before deletion
        // so we can restore focus to the next item at the same position.
        var selectedIndex = -1;
        for (var i = 0; i < BrowserItems.Count; i++)
        {
            if (ReferenceEquals(BrowserItems[i], SelectedBrowserItem))
            {
                selectedIndex = i;
                break;
            }
        }

        // The removed set includes every descendant, which the selection alone does not: recording only the
        // selection is what made undoing a folder deletion restore the folder and lose its contents.
        IReadOnlyList<CatalogDocument> removedDocuments;
        if (catalogRepository is not null)
        {
            removedDocuments = DeleteCatalogEntriesFromRepository(itemsToDelete);
        }
        else if (inMemoryCatalogEntries is not null)
        {
            removedDocuments = DeleteCatalogEntriesFromInMemory(itemsToDelete);
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

            removedDocuments = itemsToDelete;
        }

        // A refusal only counts when there is a history to refuse it; the in-memory view model has none and
        // must keep reporting a plain deletion.
        var deletionIsNotUndoable = undoManager is not null && !undoManager.RecordDeletion(removedDocuments);
        UpdateUndoRedoState();

        // Remove deleted items from BrowserItems directly instead of calling RefreshBrowserItemsForSelection()
        foreach (var item in itemsToDelete)
        {
            BrowserItems.Remove(item);
        }

        // Restore selection to the same position, or next available
        if (BrowserItems.Count > 0 && selectedIndex >= 0)
        {
            var index = Math.Min(selectedIndex, BrowserItems.Count - 1);
            SelectedBrowserItem = BrowserItems[index];
        }
        else
        {
            SelectedBrowserItem = null;
        }

        SelectedBrowserItems = SelectedBrowserItem is not null
            ? new List<CatalogDocument> { SelectedBrowserItem }
            : new List<CatalogDocument>();

        // A folder deleted here is a tree node too. Leaving it in the tree produces a node that points at a
        // record which no longer exists, and deleting that ghost afterwards has nothing authoritative to
        // snapshot — which is how undo ends up with a placeholder instead of the real subtree.
        DetachTreeNodesForRemovedDocuments(removedDocuments);

        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(
            deletionIsNotUndoable ? TextKeys.DeletedNotUndoable : TextKeys.Deleted,
            string.Join(", ", deletedNames));
        FocusBrowserRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteSelectedTreeNode()
    {
        if (SelectedTreeNode is null || !CanDeleteTreeNode(SelectedTreeNode))
        {
            return;
        }

        var targetNode = SelectedTreeNode;

        var parentNode = FindParentTreeNode(TreeNodes, targetNode.Key);
        if (parentNode is null || parentNode.Children.IndexOf(targetNode) < 0)
        {
            return;
        }

        // Routed through the same helpers as browser deletion so both paths snapshot the complete subtree.
        // Only authoritative documents are snapshotted. A placeholder for a node whose record is already
        // gone would carry the default Type (File) and no ParentId, so undo would write back a record the
        // tree filters out of view and the browser cannot place — the user is told the delete was undone
        // while the tree shows nothing at all.
        IReadOnlyList<CatalogDocument> removedDocuments;
        if (catalogRepository is not null)
        {
            var target = catalogRepository.Get(targetNode.Key);
            removedDocuments = target is null ? [] : DeleteCatalogEntriesFromRepository([target]);
        }
        else if (inMemoryCatalogEntries is not null)
        {
            var target = inMemoryCatalogEntries.FirstOrDefault(entry =>
                string.Equals(entry.Id, targetNode.Key, StringComparison.Ordinal));
            removedDocuments = target is null ? [] : DeleteCatalogEntriesFromInMemory([target]);
        }
        else
        {
            removedDocuments = [];
        }

        // Nothing removed means the node was a stale artifact: detaching it is the whole operation. Leaving
        // it out of history matters, because an empty snapshot records no action and Ctrl+Z would then
        // quietly replay some earlier, unrelated operation.
        var nodeHadNoRecord = removedDocuments.Count == 0;
        var deletionIsNotUndoable = nodeHadNoRecord ||
                                    (undoManager is not null && !undoManager.RecordDeletion(removedDocuments));
        UpdateUndoRedoState();

        DetachTreeNode(parentNode, targetNode);

        if (!nodeHadNoRecord)
        {
            IsDirtyDocument = true;
        }

        StatusText = LocalizationService.Current.Format(
            nodeHadNoRecord ? TextKeys.DeletedStaleEntry
            : deletionIsNotUndoable ? TextKeys.DeletedNotUndoable
            : TextKeys.Deleted,
            targetNode.Title);
        FocusTreeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Detaches <paramref name="deletedNode"/> from the live tree and moves the selection to the
    /// sibling that took its place, mirroring list view deletion. Only the deleted subtree is
    /// touched, so sibling TreeViewItems stay realized and can receive keyboard focus.
    /// </summary>
    private void DetachTreeNode(BrowserTreeNode parentNode, BrowserTreeNode deletedNode)
    {
        foreach (var descendant in FlattenNodes([deletedNode]))
        {
            treeNodesByKey.Remove(descendant.Key);
        }

        var siblings = parentNode.Children;
        var deletedIndex = siblings.IndexOf(deletedNode);
        siblings.RemoveAt(deletedIndex);

        SelectedTreeNode = siblings.Count == 0
            ? parentNode
            : siblings[Math.Min(deletedIndex, siblings.Count - 1)];
    }

    /// <summary>
    /// Detaches every tree node whose record was removed through the browser pane.
    /// </summary>
    /// <remarks>
    /// In place, like <see cref="DetachTreeNode"/>, rather than by rebuilding: a rebuild destroys every
    /// realized TreeViewItem including the one holding keyboard focus. Only ids that are actually tree
    /// nodes are considered, so deleting plain files costs a dictionary lookup each and no tree walk.
    /// </remarks>
    private void DetachTreeNodesForRemovedDocuments(IReadOnlyList<CatalogDocument> removedDocuments)
    {
        var ids = removedDocuments
            .Select(static document => document.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id) && treeNodesByKey.ContainsKey(id))
            .ToHashSet(StringComparer.Ordinal);

        if (ids.Count == 0)
        {
            return;
        }

        foreach (var id in ids)
        {
            treeNodesByKey.Remove(id);
        }

        DetachMatchingTreeNodes(treeNodes, ids);

        // The browser pane deletes children of the selected node, so the selection normally survives. When
        // it does not, keeping a detached node selected would leave the browser bound to a record that is
        // gone.
        if (SelectedTreeNode is not null && ids.Contains(SelectedTreeNode.Key))
        {
            SelectedTreeNode = FindParentTreeNode(TreeNodes, SelectedTreeNode.Key) ?? TreeNodes.FirstOrDefault();
        }
    }

    private static void DetachMatchingTreeNodes(IList<BrowserTreeNode> nodes, IReadOnlySet<string> ids)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];

            // Children first, so each removal reaches the TreeView as a targeted notification against a
            // parent that is still attached.
            DetachMatchingTreeNodes(node.Children, ids);

            if (ids.Contains(node.Key))
            {
                nodes.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// In-memory counterpart of <see cref="DeleteCatalogEntriesFromRepository"/>: expands to the full subtree
    /// and returns what was removed, so the caller records a snapshot undo can actually restore.
    /// </summary>
    private IReadOnlyList<CatalogDocument> DeleteCatalogEntriesFromInMemory(IReadOnlyList<CatalogDocument> items)
    {
        if (inMemoryCatalogEntries is null || items.Count == 0)
        {
            return [];
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

        var removed = inMemoryCatalogEntries
            .Where(entry => toDelete.Contains(entry.Id))
            .ToList();

        inMemoryCatalogEntries.RemoveAll(entry => toDelete.Contains(entry.Id));

        foreach (var deletedId in toDelete)
        {
            addedItemsByNodeKey.Remove(deletedId);
            deletedItemNamesByNodeKey.Remove(deletedId);
            renamedBrowserItemNamesByNodeKey.Remove(deletedId);
        }

        return removed;
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

    private void RevealRestoredTreeNode()
    {
        var restoredNode = undoManager?.History.LastCompletedAction?.AffectedEntryIds
            .Select(id => treeNodesByKey.GetValueOrDefault(id))
            .FirstOrDefault(node => node is not null);
        if (restoredNode is null)
        {
            return;
        }

        var parent = FindParentTreeNode(TreeNodes, restoredNode.Key);
        while (parent is not null)
        {
            parent.IsExpanded = true;
            parent = FindParentTreeNode(TreeNodes, parent.Key);
        }

        SelectedTreeNode = restoredNode;
        FocusTreeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshTreeNodes(string? preferredSelectedKey = null)
    {
        var targetKey = preferredSelectedKey ?? SelectedTreeNode?.Key;
        var expandedKeys = FlattenNodes(TreeNodes)
            .Where(static node => node.IsExpanded)
            .Select(static node => node.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        treeNodes.Clear();
        foreach (var node in GetTreeNodes()) treeNodes.Add(node);
        var allTreeNodes = FlattenNodes(treeNodes).ToArray();
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
    private void TogglePreviewPane()
    {
        IsPreviewPaneVisible = !IsPreviewPaneVisible;
    }

    [RelayCommand]
    private void Refresh()
    {
        StartOperation(LocalizationService.Current.Get(TextKeys.UpdatingView));
        SetProgress(60, LocalizationService.Current.Get(TextKeys.ParsingCatalog));

        // The tree is built from the repository, so anything that changed underneath it — a restore, an
        // import, an edit made while the pane was stale — only becomes visible once it is rebuilt. Without
        // this the command refreshed the browser and the preview but left the tree showing whatever it held.
        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();
        RefreshPreview();

        // Deferred from history eviction: reclaiming inline with the eviction would run a cache scan on the
        // UI thread in the middle of a deletion, so it waits for a point the user already expects work.
        if (previewArtifactReclaimPending)
        {
            previewArtifactReclaimPending = false;
            ReclaimUnreferencedPreviewArtifacts();
        }

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

        // Record old statuses for undo
        var oldStatuses = targets.ToDictionary(t => t, t => t.Status);

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

            // Record status changes for undo
            foreach (var target in applicableTargets)
            {
                if (oldStatuses.TryGetValue(target, out var oldStatus))
                {
                    undoManager?.RecordStatusChange(target, oldStatus, status.Name);
                }
            }
        }

        // Record status clears for undo
        if (status is null)
        {
            foreach (var target in targets)
            {
                if (oldStatuses.TryGetValue(target, out var oldStatus) && oldStatus != null)
                {
                    undoManager?.RecordStatusChange(target, oldStatus, null);
                }
            }
        }

        // Save changes to repository
        if (catalogRepository is not null)
        {
            foreach (var target in targets)
            {
                catalogRepository.Save(target.Id, target);
            }
        }

        UpdateUndoRedoState();
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

        RefreshTopMenuState();
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
                        ContentsState = item.ContentsState,
                        Status = item.Status,
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
            UpdateSelectedContentsNotice();
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

        BrowserItems.Clear();
        foreach (var item in refreshedItems) BrowserItems.Add(item);
        SelectedBrowserItem = BrowserItems.FirstOrDefault(item =>
                              item.Name.Equals(previouslySelectedName, StringComparison.OrdinalIgnoreCase))
                              ?? refreshedItems.FirstOrDefault();
        SelectedBrowserItems = SelectedBrowserItem is not null
            ? new List<CatalogDocument> { SelectedBrowserItem }
            : new List<CatalogDocument>();
        UpdateSelectedContentsNotice();
    }

    private void UpdateSelectedContentsNotice()
    {
        var document = SelectedTreeNode is null ? null : FindCatalogDocument(SelectedTreeNode.Key);
        var notice = document?.ContentsState == CatalogContentsState.NotScanned
            ? LocalizationService.Current.Get(TextKeys.ContentsNotScanned)
            : null;
        SelectedContentsNotice = notice;
        ShowContentsNotScannedNotice = !string.IsNullOrWhiteSpace(notice) && BrowserItems.Count == 0;
    }

    private CatalogDocument? FindCatalogDocument(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        if (catalogRepository is not null)
        {
            return catalogRepository.Get(itemId);
        }

        return (inMemoryCatalogEntries ?? [])
            .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.Ordinal));
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
        UpdatePreviewFor(value);

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
        RefreshTopMenuState();
        ApplyBrowserItemStatusCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBrowserItemsChanged(System.Collections.IList value)
    {
        var first = value.Count > 0 ? value[0] as CatalogDocument : null;

        if (value.Count > 0)
        {
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

        UpdatePreviewFor(first);

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
        RefreshTopMenuState();
        ApplyBrowserItemStatusCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Points the preview pane at <paramref name="item"/>. A selection that is echoed back through
    /// <see cref="SelectedBrowserItems"/> keeps the same entry id and must not restart the resolve, because
    /// restarting would cancel a load that is already about to finish.
    /// </summary>
    private void UpdatePreviewFor(CatalogDocument? item)
    {
        if (entryPreview.State != MediaPreviewState.Loading &&
            string.Equals(entryPreview.CurrentSnapshot?.EntryId, item?.Id, StringComparison.Ordinal))
        {
            return;
        }

        entryPreview.Show(item);
    }

    /// <summary>
    /// Drops decoded images and re-resolves the current selection, so a refresh that captured new data or
    /// recovered a missing artifact is reflected without restarting the application.
    /// </summary>
    public void RefreshPreview()
    {
        mediaPreviewImages.ClearCache();
        entryPreview.Show(SelectedBrowserItem);
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
        RefreshTopMenuState();
    }

    partial void OnCurrentSortModeChanged(string value)
    {
        CurrentSortMode = NormalizeSortMode(value);
        OnPropertyChanged(nameof(IsSortByNameChecked));
        OnPropertyChanged(nameof(IsSortByTypeChecked));
        OnPropertyChanged(nameof(IsSortBySizeChecked));
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
        RefreshTopMenuState();
    }

    partial void OnIsStatusBarVisibleChanged(bool value)
    {
        RefreshTopMenuState();
    }

    partial void OnIsPreviewPaneVisibleChanged(bool value)
    {
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
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.OpenFolder), HotKey = "Ctrl+Shift+O", Command = OpenCatalogFolderCommand },
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
                        { Header = LocalizationService.Current.Get(TextKeys.Properties2), HotKey = "Alt+Enter", Command = OpenPropertiesCommand },
                    Separator(),
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.Find), HotKey = "Ctrl+F", Command = FindCatalogCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.View),
                Items =
                [
                    CheckedMenuItem(IsStatusBarVisible, LocalizationService.Current.Get(TextKeys.StatusBar), ToggleStatusBarCommand, key: "statusbar"),
                    CheckedMenuItem(IsPreviewPaneVisible, LocalizationService.Current.Get(PreviewKeys.PreviewPane), TogglePreviewPaneCommand,
                        key: "previewpane"),
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
        ApplyMenuState(topMenuItems);
        ApplyMenuState(browserContextMenuItems);
        ApplyMenuState(treeContextMenuItems);
    }

    private void ApplyMenuState(IEnumerable<MainMenuItemViewModel> menuItems)
    {
        var byKey = FlattenMenuItems(menuItems)
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key!, StringComparer.Ordinal);

        SetMenuChecked(byKey, "statusbar", IsStatusBarVisible, LocalizationService.Current.Get(TextKeys.StatusBar));
        SetMenuChecked(byKey, "previewpane", IsPreviewPaneVisible, LocalizationService.Current.Get(PreviewKeys.PreviewPane));
        SetMenuChecked(byKey, "view_tiles", IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles));
        SetMenuChecked(byKey, "view_small", IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2));
        SetMenuChecked(byKey, "view_large", IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu));
        SetMenuChecked(byKey, "view_list", IsListViewChecked, LocalizationService.Current.Get(TextKeys.List));
        SetMenuChecked(byKey, "view_details", IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details));
        SetMenuChecked(byKey, "sort_name", IsSortByNameChecked, LocalizationService.Current.Get(TextKeys.Name));
        SetMenuChecked(byKey, "sort_type", IsSortByTypeChecked, LocalizationService.Current.Get(TextKeys.Type2));
        SetMenuChecked(byKey, "sort_size", IsSortBySizeChecked, LocalizationService.Current.Get(TextKeys.Size2));
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

    private static void SetMenuChecked(
        IReadOnlyDictionary<string, MainMenuItemViewModel> byKey,
        string key,
        bool isChecked,
        string title)
    {
        if (byKey.TryGetValue(key, out var item))
        {
            item.Header = title;
            item.Icon = CheckedIcon(isChecked);
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

    private static object CheckedIcon(bool isChecked)
    {
        return isChecked ? "✓" : "\u00A0";
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
            Header = title,
            Icon = CheckedIcon(isChecked),
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
                Key = "edit_status",
                Header = LocalizationService.Current.Get(TextKeys.Status),
                Items = BuildStatusMenuItems()
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.View),
                Items =
                [
                    CheckedMenuItem(IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2), SetViewModeCommand,
                        "SmallIcons", "view_small"),
                    CheckedMenuItem(IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu), SetViewModeCommand,
                        "LargeIcons", "view_large"),
                    CheckedMenuItem(IsListViewChecked, LocalizationService.Current.Get(TextKeys.List), SetViewModeCommand, "List", "view_list"),
                    CheckedMenuItem(IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details), SetViewModeCommand,
                        "Details", "view_details"),
                    CheckedMenuItem(IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles), SetViewModeCommand,
                        "Tiles", "view_tiles")
                ]
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.ArrangeIconsBy2),
                Items =
                [
                    CheckedMenuItem(IsSortByNameChecked, LocalizationService.Current.Get(TextKeys.Name), SetSortModeCommand, "Name", "sort_name"),
                    CheckedMenuItem(IsSortByTypeChecked, LocalizationService.Current.Get(TextKeys.Type2), SetSortModeCommand, "Type", "sort_type")
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
                    CheckedMenuItem(IsSmallIconsViewChecked, LocalizationService.Current.Get(TextKeys.SmallIcons2), SetViewModeCommand,
                        "SmallIcons", "view_small"),
                    CheckedMenuItem(IsLargeIconsViewChecked, LocalizationService.Current.Get(TextKeys.LargeIconsMenu), SetViewModeCommand,
                        "LargeIcons", "view_large"),
                    CheckedMenuItem(IsListViewChecked, LocalizationService.Current.Get(TextKeys.List), SetViewModeCommand, "List", "view_list"),
                    CheckedMenuItem(IsDetailsViewChecked, LocalizationService.Current.Get(TextKeys.Details), SetViewModeCommand,
                        "Details", "view_details"),
                    CheckedMenuItem(IsTilesViewChecked, LocalizationService.Current.Get(TextKeys.Tiles), SetViewModeCommand,
                        "Tiles", "view_tiles")
                ]
            },
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

    /// <summary>
    /// Deletes the given entries together with every descendant, and returns the complete set of documents
    /// that was removed.
    /// </summary>
    /// <returns>
    /// The authoritative snapshot of everything deleted, which is what undo has to restore. Returning it is
    /// the point: the caller cannot reconstruct the descendant set afterwards, because the records are gone.
    /// Documents are read from the repository rather than taken from the caller's list, so the snapshot holds
    /// what was actually stored and not a view-model overlay copy.
    /// </returns>
    private IReadOnlyList<CatalogDocument> DeleteCatalogEntriesFromRepository(IReadOnlyList<CatalogDocument> items)
    {
        if (catalogRepository is null || items.Count == 0)
        {
            return [];
        }

        var itemIds = items
            .Select(static entry => entry.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var descendants = catalogRepository.GetDescendantsOf(itemIds);
        var descendantsById = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
        foreach (var descendant in descendants)
        {
            if (!string.IsNullOrWhiteSpace(descendant.Id))
            {
                descendantsById.TryAdd(descendant.Id, descendant);
            }
        }

        var allIdsToDelete = itemIds
            .Concat(descendantsById.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var snapshot = new List<CatalogDocument>(allIdsToDelete.Length);
        foreach (var id in allIdsToDelete)
        {
            var stored = catalogRepository.Get(id);
            if (stored is null)
            {
                descendantsById.TryGetValue(id, out stored);
            }

            stored ??= items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (stored is not null)
            {
                snapshot.Add(stored);
            }
        }

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

        return snapshot;
    }

    private PropertiesCollection GetBrowserItemInfoProperties(string itemId)
    {
        var document = FindCatalogDocument(itemId);
        if (document is null)
        {
            return new PropertiesCollection();
        }

        if (document.ContentsState != CatalogContentsState.NotScanned ||
            document.Properties.ContainsKey(TitleOnlyMedia.ContentsStatePropertyKey))
        {
            return document.Properties;
        }

        var copy = new PropertiesCollection(document.Properties)
        {
            [TitleOnlyMedia.ContentsStatePropertyKey] = CatalogContentsState.NotScanned.ToString()
        };
        return copy;
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
