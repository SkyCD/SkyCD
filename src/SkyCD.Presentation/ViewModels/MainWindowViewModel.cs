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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Couchbase.Lite;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Controls.Preview;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.Staging;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Core.UndoRedo;
using SkyCD.Presentation.Tags;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Presentation.Locations;
using SkyCD.Presentation.Collections;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Presentation.Scanning;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private BackgroundMediaSyncService? backgroundMediaSync;

    [ObservableProperty]
    private bool isBackgroundMediaSyncEnabled;

    partial void OnIsBackgroundMediaSyncEnabledChanged(bool value)
    {
        backgroundMediaSync?.Dispose();
        backgroundMediaSync = null;
        if (value && catalogRepository is not null && !isDisposed)
        {
            backgroundMediaSync = new BackgroundMediaSyncService(catalogRepository,
                async (documents, token, canPublish) =>
                {
                    var published = await PublishWithMetadataAsync(documents, () =>
                    {
                        new CatalogScanPublisher().PublishDirect(documents, catalogRepository,
                            new ScanPublishOptions { CancellationToken = token }, undoManager?.History);
                        IsDirtyDocument = true;
                        UpdateUndoRedoState();
                        RefreshTreeNodes();
                        RefreshBrowserItemsForSelection();
                        return true;
                    }, null, token, canPublish: canPublish);
                    if (published) RefreshPreview();
                }, () => !isDisposed && !IsProgressVisible, volumeMetadata: volumeMetadataProvider,
                traversalFactory: scanTraversalFactory);
            backgroundMediaSync.Failed += message => StatusText = $"Background media sync failed: {message}";
            backgroundMediaSync.Start();
        }
        RebuildMenuItems();
    }

    [RelayCommand]
    private void ToggleBackgroundMediaSync() => IsBackgroundMediaSyncEnabled = !IsBackgroundMediaSyncEnabled;
    private readonly IDisposable localizationSubscription;
    private bool isDisposed;
    private bool previewArtifactReclaimPending;

    private readonly CatalogDocumentRepository? catalogRepository;
    private OpenCatalogState? openCatalogState;
    private CatalogFeatureExtensionManager? catalogFeatures;
    private readonly CatalogUndoManager? undoManager;
    private readonly CatalogTagService? tagService;
    private readonly TagDocumentRepository? tagRepository;
    private readonly CatalogLocationService? locationService;
    private readonly LocationDocumentRepository? locationRepository;
    private readonly VirtualCollectionService? collectionService;
    private readonly VirtualCollectionDocumentRepository? collectionRepository;
    private readonly Func<ScanTraversalOptions>? scanTraversalFactory;
    private readonly Func<AiScanPolicy>? aiScanPolicyFactory;
    private readonly bool shouldSeedDefaultCatalog;
    private readonly List<CatalogDocument>? inMemoryCatalogEntries;
    private readonly IStringLocalizer propertyValueLocalizer;
    private Dictionary<string, BrowserTreeNode> treeNodesByKey;
    private BrowserTreeNode? renamingNode;
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
    // RefreshTopMenuState, which mutates the cached items in place. The lists notify collection
    // changes so a rebound context menu picks up contributions added or removed with a plugin.
    private readonly ObservableCollection<MainMenuItemViewModel> browserContextMenuItems = [];
    private readonly ObservableCollection<MainMenuItemViewModel> treeContextMenuItems = [];
    private List<DetailsColumnSettingDocument> detailsColumnSettings = DetailsColumnsCatalog.CreateDefaultSettings();
    private IReadOnlyList<MainMenuItemViewModel> detailsHeaderMenuItems = [];
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
    public event EventHandler? FindCatalogRequested;
    public event EventHandler<CollectionsDialogRequestedEventArgs>? CollectionsRequested;
    public event EventHandler<AdvancedFindDialogRequestedEventArgs>? AdvancedFindRequested;
    public event EventHandler<OptionsDialogRequestedEventArgs>? OptionsRequested;
    public event EventHandler? DetailsColumnsChanged;
    public event EventHandler<PropertiesDialogRequestedEventArgs>? PropertiesRequested;
    public event EventHandler<TagsDialogRequestedEventArgs>? TagsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? FocusBrowserRequested;
    public event EventHandler? FocusTreeRequested;
    public event EventHandler? RenameRequested;

    private MenuExtensionManager menuExtensionManager;
    private DescriptionExtensionManager descriptionExtensionManager = new([]);
    private AiDescriptionService? aiDescriptionService;
    private CancellationTokenSource? pluginCommandCancellation;
    private MediaMetadataService? mediaMetadataService;
    private IVolumeMetadataProvider? volumeMetadataProvider;
    private AudioCdMetadataService? audioCdMetadataService;
    private readonly SemaphoreSlim metadataPublicationGate = new(1, 1);
    private readonly CancellationTokenSource metadataLifetime = new();
    private readonly object viewRefreshLock = new();

    public void RefreshMediaServices(MediaExtensionManager updatedManager,
        IVolumeMetadataProvider? volumeMetadataProvider = null, IndexWorkItemSequence? indexWorkItems = null,
        IAudioCdMetadataProvider? audioCdMetadataProvider = null)
    {
        ArgumentNullException.ThrowIfNull(updatedManager);
        mediaMetadataService = new MediaMetadataService(updatedManager, mediaCacheStore, workItems: indexWorkItems);
        this.volumeMetadataProvider = volumeMetadataProvider;
        InvalidateMediaCatalogEffects();
        audioCdMetadataService = audioCdMetadataProvider is null
            ? null
            : new AudioCdMetadataService(audioCdMetadataProvider, mediaCacheStore);
        if (backgroundMediaSync is not null)
        {
            OnIsBackgroundMediaSyncEnabledChanged(IsBackgroundMediaSyncEnabled);
        }
    }

    private static bool HasLocalSource(CatalogDocument entry, IReadOnlyDictionary<string, CatalogDocument> catalog)
    {
        if (entry.Type != CatalogDocumentType.File ||
            !entry.Properties.TryGetValue(MediaMetadataService.SourcePathPropertyKey, out var path) ||
            string.IsNullOrWhiteSpace(path?.ToString()))
        {
            return false;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Id };
        var parentId = entry.ParentId;
        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId) && catalog.TryGetValue(parentId, out var parent))
        {
            if (parent.Type == CatalogDocumentType.NetworkFolder) return false;
            parentId = parent.ParentId;
        }

        return true;
    }

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

    /// <summary>
    /// Rebinds the AI description capability after plugins are discovered or reloaded. Descriptions that
    /// were already captured stay readable both with and without a provider.
    /// </summary>
    public void RefreshDescriptionServices(DescriptionExtensionManager updatedManager)
    {
        descriptionExtensionManager = updatedManager ?? throw new ArgumentNullException(nameof(updatedManager));
        RebuildDescriptionService();
        RebuildMenuItems();
        OnPropertyChanged(nameof(ToolsMenuItems));
        RefreshTopMenuState();
    }

    private void RebuildDescriptionService()
    {
        aiDescriptionService = catalogRepository is null
            ? null
            : new AiDescriptionService(catalogRepository, mediaCacheStore, descriptionExtensionManager.Capability,
                aiScanPolicyFactory);
    }

    private void RebuildMenuItems()
    {
        topMenuItems = BuildTopMenuItems();
        ReplaceMenuItems(browserContextMenuItems, BuildBrowserContextMenuItems());
        ReplaceMenuItems(treeContextMenuItems, BuildTreeContextMenuItems());
        RefreshDetailsHeaderMenuItems();
    }

    private static void ReplaceMenuItems(
        ObservableCollection<MainMenuItemViewModel> target,
        IReadOnlyList<MainMenuItemViewModel> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void RefreshDetailsHeaderMenuItems()
    {
        if (detailsHeaderMenuItems.Count == 0)
        {
            var items = new List<MainMenuItemViewModel>(DetailsColumnsCatalog.All.Count + 2);
            foreach (var definition in DetailsColumnsCatalog.All)
            {
                items.Add(new MainMenuItemViewModel
                {
                    Key = "details_column_" + definition.Id,
                    Command = ToggleDetailsColumnCommand,
                    CommandParameter = definition.Id,
                    ToggleType = MenuItemToggleType.CheckBox
                });
            }

            items.Add(Separator());
            items.Add(new MainMenuItemViewModel
            {
                Key = "details_columns_options",
                Command = OpenDetailsColumnsOptionsCommand
            });
            detailsHeaderMenuItems = items;
        }

        var visibility = detailsColumnSettings.ToDictionary(
            static setting => setting.Id,
            static setting => setting.IsVisible,
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in detailsHeaderMenuItems)
        {
            if (item.Key == "details_columns_options")
            {
                item.Header = LocalizationService.Current.Get(UiKeys.ConfigureColumns);
                continue;
            }

            var definition = DetailsColumnsCatalog.Find(item.CommandParameter as string);
            if (definition is null)
            {
                continue;
            }

            item.Header = definition.IsRequired
                ? LocalizationService.Current.Format(UiKeys.DetailsColumnRequiredHeader, definition.Header)
                : definition.Header;
            item.IsEnabled = !definition.IsRequired;
            item.IsChecked = visibility.TryGetValue(definition.Id, out var isVisible) && isVisible;
        }
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
        this.tagRepository = CreateTagDocuments(this.catalogRepository);
        this.tagService = this.tagRepository is not null
            ? new CatalogTagService(this.catalogRepository, this.tagRepository)
            : null;
        this.locationRepository = CreateLocationDocuments(this.catalogRepository);
        this.locationService = this.locationRepository is not null
            ? new CatalogLocationService(this.catalogRepository, this.locationRepository)
            : null;
        this.collectionRepository = CreateCollectionDocuments(this.catalogRepository);
        this.collectionService = this.collectionRepository is not null
            ? new VirtualCollectionService(this.catalogRepository, this.collectionRepository, () => BuildAdvancedFindMetadata().Lookup, entries => ProjectLocations(entries))
            : null;
        this.scanTraversalFactory = CreateScanTraversalFactory(this.catalogRepository);
        this.aiScanPolicyFactory = CreateAiScanPolicyFactory(this.catalogRepository);
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
        RebuildDescriptionService();

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

    /// <summary>
    /// Connects the host-owned open-catalog state that the plugin catalog and storage APIs project from.
    /// </summary>
    public void AttachOpenCatalogState(OpenCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        openCatalogState = state;
        state.Attach(catalogRepository);
        state.Path = CurrentCatalogPath;
    }

    /// <summary>
    /// Connects the enabled feature plugins whose decorations, columns and commands the host renders.
    /// </summary>
    public void RefreshCatalogFeatureServices(CatalogFeatureExtensionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (catalogFeatures is not null)
        {
            catalogFeatures.DataChanged -= OnCatalogFeatureDataChanged;
        }

        catalogFeatures = manager;
        manager.DataChanged += OnCatalogFeatureDataChanged;
        DetailsColumnsCatalog.SetContributedColumns(manager.GetDetailsColumns().Select(contribution =>
            new DetailsColumnDefinition
            {
                Id = contribution.Key,
                HeaderKey = contribution.Title,
                ValuePath = "Plugin:" + contribution.Key,
                Width = new Avalonia.Controls.GridLength(140, Avalonia.Controls.GridUnitType.Pixel),
                ValueSelector = item => item is CatalogDocument document
                    ? contribution.SelectValue(ToCatalogEntry(document))
                    : null
            }));
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        RefreshFeatureContextMenus();
        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();
    }

    private static CatalogEntry ToCatalogEntry(CatalogDocument document) =>
        new(
            document.Id,
            document.Name,
            "/" + document.Name,
            document.ParentId,
            document.Type switch
            {
                CatalogDocumentType.Folder or CatalogDocumentType.NetworkFolder => CatalogEntryKind.Folder,
                CatalogDocumentType.File or CatalogDocumentType.Media => CatalogEntryKind.File,
                _ => CatalogEntryKind.Other
            },
            document.Size,
            document.Properties.ToDictionary(
                static property => property.Key,
                static property => property.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal));

    private void OnCatalogFeatureDataChanged(object? sender, EventArgs e)
    {
        // Feature plugins raise this from their own threads (dialog close, import worker); the catalog
        // collections are bound to the UI and can only be refreshed there.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnCatalogFeatureDataChanged(sender, e));
            return;
        }

        if (isDisposed)
        {
            return;
        }

        RefreshTreeNodes();
        RefreshBrowserItemsForSelection();
    }

    private IReadOnlyList<CatalogDocument> ApplyCatalogDecorations(IReadOnlyList<CatalogDocument> entries)
    {
        foreach (var entry in entries)
        {
            entry.SetDecoration(null, null, null, attention: false);
        }

        if (catalogFeatures is null || entries.Count == 0)
        {
            return entries;
        }

        var byId = entries
            .GroupBy(static entry => entry.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var projected = entries.Select(entry => CatalogApiAdapter.Project(entry, byId)).ToArray();
        foreach (var decoration in catalogFeatures.GetDecorations(projected))
        {
            if (byId.TryGetValue(decoration.EntryId, out var document))
            {
                document.SetDecoration(decoration.BadgeText, decoration.BadgeIcon, decoration.Tooltip,
                    decoration.Attention);
            }
        }

        return entries;
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

    /// <summary>
    /// Opens the tag repository on the catalog database without changing the
    /// view-model constructor surface: persistence tests construct this type
    /// through a fixed-arity private constructor, so new services must be wired
    /// internally rather than as extra parameters.
    /// </summary>
    private static TagDocumentRepository? CreateTagDocuments(CatalogDocumentRepository catalog)
    {
        try
        {
            var database = catalog.Collection?.Database;
            if (database is null)
            {
                return null;
            }

            var mapping = (CouchbaseDocument?)typeof(TagDocument)
                .GetCustomAttributes(typeof(CouchbaseDocument), inherit: false)
                .FirstOrDefault();
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.CollectionName))
            {
                return null;
            }

            var collection = database.GetCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName)
                ?? database.CreateCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName);
            var repository = new TagDocumentRepository();
            repository.Initialize(typeof(TagDocument), mapping.CollectionName, collection);
            return repository;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the background-sync traversal policy from persisted scan settings, opening the
    /// sibling repositories on the catalog database like <see cref="CreateTagDocuments"/>.
    /// </summary>
    private static Func<ScanTraversalOptions>? CreateScanTraversalFactory(CatalogDocumentRepository catalog)
    {
        var appOptions = OpenSiblingRepository<AppOptionsDocument, AppOptionsDocumentRepository>(catalog);
        var scanRules = OpenSiblingRepository<ScanExclusionRuleDocument, ScanExclusionRuleDocumentRepository>(catalog);
        if (appOptions is null || scanRules is null)
        {
            return null;
        }

        return () => ScanSettingsApplicator.BuildTraversalOptions(
            appOptions.GetOrCreateAppOptions(),
            scanRules.GetOrdered());
    }

    private static Func<AiScanPolicy>? CreateAiScanPolicyFactory(CatalogDocumentRepository catalog)
    {
        var appOptions = OpenSiblingRepository<AppOptionsDocument, AppOptionsDocumentRepository>(catalog);
        var exclusions = OpenSiblingRepository<AiScanExclusionDocument, AiScanExclusionDocumentRepository>(catalog);
        if (appOptions is null || exclusions is null)
        {
            return null;
        }

        return () => new AiScanPolicy(
            exclusions.GetOrdered().Select(static entry => entry.MimeType),
            appOptions.GetOrCreateAppOptions().AiScanMaxFileSizeBytes);
    }

    private static TRepository? OpenSiblingRepository<TDocument, TRepository>(CatalogDocumentRepository catalog)
        where TDocument : class, new()
        where TRepository : RepositoryBase<TDocument>, new()
    {
        try
        {
            var database = catalog.Collection?.Database;
            if (database is null)
            {
                return null;
            }

            var mapping = (CouchbaseDocument?)typeof(TDocument)
                .GetCustomAttributes(typeof(CouchbaseDocument), inherit: false)
                .FirstOrDefault();
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.CollectionName))
            {
                return null;
            }

            var collection = database.GetCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName)
                ?? database.CreateCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName);
            var repository = new TRepository();
            repository.Initialize(typeof(TDocument), mapping.CollectionName, collection);
            return repository;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the location repository on the catalog database without changing
    /// the view-model constructor surface, matching <see cref="CreateTagDocuments"/>.
    /// </summary>
    private static LocationDocumentRepository? CreateLocationDocuments(CatalogDocumentRepository catalog)
    {
        try
        {
            var database = catalog.Collection?.Database;
            if (database is null)
            {
                return null;
            }

            var mapping = (CouchbaseDocument?)typeof(LocationDocument)
                .GetCustomAttributes(typeof(CouchbaseDocument), inherit: false)
                .FirstOrDefault();
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.CollectionName))
            {
                return null;
            }

            var collection = database.GetCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName)
                ?? database.CreateCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName);
            var repository = new LocationDocumentRepository();
            repository.Initialize(typeof(LocationDocument), mapping.CollectionName, collection);
            return repository;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the collections repository on the catalog database without changing
    /// the view-model constructor surface: persistence tests construct this type
    /// through a fixed-arity private constructor, so new services must be wired
    /// internally rather than as extra parameters.
    /// </summary>
    private static VirtualCollectionDocumentRepository? CreateCollectionDocuments(
        CatalogDocumentRepository catalog)
    {
        try
        {
            var database = catalog.Collection?.Database;
            if (database is null)
            {
                return null;
            }

            var mapping = (CouchbaseDocument?)typeof(VirtualCollectionDocument)
                .GetCustomAttributes(typeof(CouchbaseDocument), inherit: false)
                .FirstOrDefault();
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.CollectionName))
            {
                return null;
            }

            var collection = database.GetCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName)
                ?? database.CreateCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName);
            var repository = new VirtualCollectionDocumentRepository();
            repository.Initialize(typeof(VirtualCollectionDocument), mapping.CollectionName, collection);
            return repository;
        }
        catch
        {
            return null;
        }
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
        RefreshDetailsHeaderMenuItems();
        ToolbarItemsChanged?.Invoke(this, EventArgs.Empty);
        StatusText = DefaultStatusText;
        entryPreview.RefreshLocalization();
        RefreshBrowserItemsForSelection();
    }

    public void Dispose()
    {
        quickFindCancellation?.Cancel();
        if (isDisposed) return;
        isDisposed = true;
        pluginCommandCancellation?.Cancel();
        metadataLifetime.Cancel();
        backgroundMediaSync?.Dispose();
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

    /// <summary>
    /// Starts an empty catalog on the application's working database. A new catalog provisions no database of
    /// its own: the working database is already open, and its content is replaced the same way opening a catalog
    /// replaces it, so File -> New does not depend on creating a new database file.
    /// </summary>
    public static MainWindowViewModel CreateNewCatalog(RepositoryManager repositoryManager)
    {
        ArgumentNullException.ThrowIfNull(repositoryManager);
        if (repositoryManager.For<CatalogDocument>() is not CatalogDocumentRepository repository)
        {
            throw new InvalidOperationException("Catalog repository must be CatalogDocumentRepository.");
        }

        ClearCatalogDocuments(repository);
        var catalog = new MainWindowViewModel(repository, new PropertyValueLocalizer(),
            CreateEmptyMenuExtensionManager(), shouldSeedDefaultCatalog: false, new CatalogUndoManager(repository));
        catalog.CompleteNewCatalog();
        return catalog;
    }

    private static void ClearCatalogDocuments(CatalogDocumentRepository repository)
    {
        var entries = repository.GetAll();
        if (entries.Count == 0)
        {
            return;
        }

        var collection = repository.Collection;
        collection.Database.InBatch(() =>
        {
            foreach (var entry in entries)
            {
                using var document = collection.GetDocument(entry.Id);
                if (document is not null)
                {
                    collection.Delete(document);
                }
            }
        });
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

    /// <summary>Visible details columns in the configured order.</summary>
    public IReadOnlyList<BrowserDetailsColumn> BrowserDetailsColumns =>
        DetailsColumnsCatalog.CreateVisibleColumns(detailsColumnSettings);

    /// <summary>
    /// Cached menu for the details list header. The item instances are mutated in place so an open
    /// context menu keeps its items while visibility changes.
    /// </summary>
    public IReadOnlyList<MainMenuItemViewModel> DetailsHeaderMenuItems => detailsHeaderMenuItems;

    public bool IsSaveEnabled => IsDirtyDocument;

    public bool IsDeleteEnabled => CanDeleteItem(null);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
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
        RenameItemCommand.NotifyCanExecuteChanged();
    }

    public bool IsCopyEnabled => SelectedBrowserItem is not null;

    public bool IsPasteEnabled => ClipboardItem is not null;

    public bool IsCutEnabled => SelectedBrowserItem is not null;

    [RelayCommand]
    private void FindCatalog()
    {
        IsQuickFilterOpen = true;
        FindCatalogRequested?.Invoke(this, EventArgs.Empty);
    }

    public CatalogFindDialogViewModel CreateCatalogFindDialogViewModel() =>
        new(GetAllCatalogEntries(), NavigateToFindResult, searchFieldProvider);

    [RelayCommand]
    private void AdvancedFindCatalog() => AdvancedFindRequested?.Invoke(
        this, new AdvancedFindDialogRequestedEventArgs { Dialog = CreateAdvancedCatalogFindDialogViewModel() });

    public AdvancedCatalogFindDialogViewModel CreateAdvancedCatalogFindDialogViewModel()
    {
        var entries = ProjectLocations(GetAllCatalogEntries());
        var (lookup, metadataKeys) = BuildAdvancedFindMetadata();
        return new AdvancedCatalogFindDialogViewModel(
            entries,
            NavigateToFindResult,
            lookup,
            metadataKeys,
            DiscoverCustomPropertyKeys(entries),
            DiscoverStatusValues(entries),
            collectionService,
            () =>
            {
                IsDirtyDocument = true;
                RefreshCollectionsMenuItems();
            },
            SelectedTreeNode?.Key,
            BuildContributedSearchFields());
    }

    private IReadOnlyList<ContributedSearchFieldDefinition> BuildContributedSearchFields() =>
        catalogFeatures?.GetAdvancedSearchFields()
            .Select(field => new ContributedSearchFieldDefinition(
                field.Key,
                field.Title,
                document => field.Matches(ToCatalogEntry(document)),
                field.SelectValue is null
                    ? null
                    : document => field.SelectValue(ToCatalogEntry(document))))
            .ToArray() ?? [];

    private (AdvancedCatalogSearchService.MetadataLookup? Lookup, IReadOnlyList<string> Keys) BuildAdvancedFindMetadata()
    {
        IReadOnlyList<MediaCacheRecord> records;
        try
        {
            records = mediaCacheStore.GetAllRecords();
        }
        catch
        {
            records = [];
        }

        if (records.Count == 0)
        {
            return (null, []);
        }

        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var byEntry = new Dictionary<string, IReadOnlyList<MediaCacheRecord>>(StringComparer.Ordinal);
        foreach (var group in records
            .Where(static record => !string.IsNullOrWhiteSpace(record.EntryId))
            .GroupBy(static record => record.EntryId, StringComparer.Ordinal))
        {
            var list = (IReadOnlyList<MediaCacheRecord>)group.ToList();
            byEntry[group.Key] = list;
            foreach (var record in list)
            {
                foreach (var field in record.SchemaFields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Key))
                    {
                        keys.Add(field.Key);
                    }
                }

                foreach (var value in record.Metadata)
                {
                    if (!string.IsNullOrWhiteSpace(value.Key))
                    {
                        keys.Add(value.Key);
                    }
                }
            }
        }

        AdvancedCatalogSearchService.MetadataLookup lookup = document =>
        {
            if (string.IsNullOrWhiteSpace(document.Id) || !byEntry.TryGetValue(document.Id, out var entryRecords))
            {
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }

            var projected = MediaEntryMetadataProjection.Project(document, entryRecords);
            var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in projected)
            {
                if (!string.IsNullOrWhiteSpace(field.Key) && !metadata.ContainsKey(field.Key))
                {
                    metadata[field.Key] = field.Value;
                }
            }

            return metadata;
        };
        return (lookup, keys.ToArray());
    }

    private static IReadOnlyList<string> DiscoverCustomPropertyKeys(IReadOnlyList<CatalogDocument> entries)
    {
        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            foreach (var property in entry.Properties)
            {
                if (!string.IsNullOrWhiteSpace(property.Key))
                {
                    keys.Add(property.Key);
                }
            }
        }

        return keys.ToArray();
    }

    private static IReadOnlyList<string> DiscoverStatusValues(IReadOnlyList<CatalogDocument> entries)
    {
        var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Status))
            {
                values.Add(entry.Status);
            }
        }

        return values.ToArray();
    }

    public IReadOnlyList<CatalogDocument> GetAllCatalogEntries() =>
        catalogRepository?.GetAll() ?? (IReadOnlyList<CatalogDocument>)(inMemoryCatalogEntries?.ToArray() ?? []);

    private void NavigateToFindResult(CatalogFindResult result)
    {
        var document = result.Document;
        var nodeKey = IsTreeVisibleType(document) ? document.Id : document.ParentId ?? "library";
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
        inMemoryCatalogEntries?.Clear();
        commentsByObjectKey.Clear();
        addedItemsByNodeKey.Clear();
        deletedItemNamesByNodeKey.Clear();
        renamedBrowserItemNamesByNodeKey.Clear();
        CurrentCatalogPath = null;
        SelectedBrowserItem = null;
        SelectedBrowserItems = new List<CatalogDocument>();
        ClipboardItem = null;

        // History describes edits to the catalog that was open. Carrying it into a new catalog would let Undo
        // write records into a database they no longer belong to, and would keep preview artifacts pinned for
        // entries that no longer exist.
        ResetUndoHistory();

        RefreshTreeNodes();
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

        var isTreeProperties = ActivePane == ActivePane.Tree && SelectedTreeNode is not null;
        var selectedId = isTreeProperties
            ? SelectedTreeNode?.Key
            : SelectedBrowserItem?.Id ?? SelectedTreeNode?.Key;

        PropertiesRequested?.Invoke(this, new PropertiesDialogRequestedEventArgs
        {
            Dialog = dialog,
            Complete = (accepted, comments) =>
            {
                if (!accepted)
                {
                    return;
                }

                RunCatalogEdit(LocalizationService.Current.Get(TextKeys.Properties), () =>
                {
                    if (selectedId is not null)
                    {
                        var document = catalogRepository?.Get(selectedId)
                            ?? inMemoryCatalogEntries?.FirstOrDefault(item => item.Id == selectedId);
                        if (document is not null)
                        {
                            var previous = new PropertiesCollection(document.Properties);
                            var properties = dialog.GetProperties();
                            foreach (var property in dialog.CustomProperties)
                            {
                                document.Properties[property.Name] = properties[property.Name];
                            }
                            if (comments.Length > 0 || document.Properties.ContainsKey("Comment"))
                            {
                                document.Properties["Comment"] = comments;
                            }
                            catalogRepository?.Save(document.Id, document);
                            foreach (var key in previous.Keys.Union(document.Properties.Keys))
                            {
                                previous.TryGetValue(key, out var oldValue);
                                document.Properties.TryGetValue(key, out var newValue);
                                undoManager?.RecordPropertyChange(document, key, oldValue, newValue);
                            }
                        }
                    }
                    if (dialog.HasTagsTab && tagService is not null && catalogRepository is not null
                        && !isTreeProperties && selectedId is not null)
                    {
                        var oldTagIds = tagService.GetEntryTagIds(selectedId);
                        var newTagIds = dialog.GetAssignedTagIds();
                        if (!oldTagIds.SequenceEqual(newTagIds, StringComparer.Ordinal))
                        {
                            tagService.SetEntryTags(selectedId, newTagIds);
                            var changed = catalogRepository.Get(selectedId);
                            if (changed is not null)
                            {
                                undoManager?.RecordTagsChange(changed, oldTagIds, newTagIds);
                            }

                            RefreshBrowserItemsForSelection();
                            RefreshTagsMenuItems();
                        }
                    }
                    ApplyBrowserItemRenameIfNeeded(dialog, selectedId);
                    commentsByObjectKey[dialog.ObjectKey] = comments;
                });
                UpdateUndoRedoState();
                RefreshBrowserItemsForSelection();
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

    private void RunCatalogEdit(string displayName, Action apply)
    {
        undoManager?.BeginTransaction(displayName);
        try
        {
            if (catalogRepository is not null)
            {
                catalogRepository.Collection.Database.InBatch(apply);
            }
            else
            {
                apply();
            }
            undoManager?.CommitTransaction();
        }
        catch
        {
            undoManager?.RollbackTransaction();
            throw;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        if (undoManager is null)
        {
            return;
        }

        var actionName = undoManager.UndoActionName;
        var selectedIds = GetSelectedBrowserItems().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
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

        // Rebind the logical selection to fresh repository instances after replay.
        SelectedBrowserItem = null;
        SelectedBrowserItems = new List<CatalogDocument>();

        UpdateUndoRedoState();
        RefreshTreeNodes();
        RevealRestoredTreeNode();
        RefreshBrowserItemsForSelection();
        RestoreHistorySelection(selectedIds);
        RefreshTagsMenuItems();
        RefreshCollectionsMenuItems();
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
        var selectedIds = GetSelectedBrowserItems().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
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
        RestoreHistorySelection(selectedIds);
        RefreshTagsMenuItems();
        RefreshCollectionsMenuItems();
        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.RedoCompleted, actionName ?? string.Empty);
    }

    private void RestoreHistorySelection(IReadOnlySet<string> selectedIds)
    {
        var selected = BrowserItems.Where(item => selectedIds.Contains(item.Id)).ToList();
        SelectedBrowserItems = selected;
        SelectedBrowserItem = selected.FirstOrDefault();
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

    public CatalogDocument AddTitleOnlyMedia(string? mediaName, string? parentId, string? locationId = null)
    {
        var document = TitleOnlyMedia.Create(mediaName, parentId);
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            document.Properties[CatalogLocationService.LocationPropertyKey] = locationId;
        }

        PublishImportedDocuments([document], CancellationToken.None);
        if (treeNodesByKey.TryGetValue(document.Id, out var node))
        {
            SelectedTreeNode = node;
        }

        return document;
    }

    public async Task<ScanPublishResult?> PublishStagedScanAsync(IStagingStore store, string sessionId, ScanPublishOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var staged = store.GetStagedDocuments(sessionId);
        var cancellationToken = options?.CancellationToken ?? CancellationToken.None;
        var result = await PublishWithMetadataAsync(staged, () =>
        {
            if (catalogRepository is null)
            {
                PublishImportedDocuments(staged, cancellationToken);
                store.DropSession(sessionId);
                return new ScanPublishResult
                {
                    GenerationId = $"gen-{Guid.NewGuid():N}",
                    PublishedDocumentsCount = staged.Count,
                    PublishedDocuments = staged
                };
            }

            return new CatalogScanPublisher().Publish(store, sessionId, catalogRepository, options, undoManager?.History);
        }, null, cancellationToken, sessionId, describeNewFiles: true);

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
        RefreshPreview();
        return result;
    }

    public async Task AddImportedDocumentsAsync(
        IReadOnlyList<CatalogDocument> documents,
        CancellationToken cancellationToken = default,
        IOperationProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        cancellationToken.ThrowIfCancellationRequested();
        if (documents.Count == 0)
        {
            return;
        }

        await PublishWithMetadataAsync(documents, () =>
        {
            PublishImportedDocuments(documents, cancellationToken);
            return true;
        }, progress, cancellationToken, describeNewFiles: true);
        RefreshPreview();
    }

    /// <summary>
    /// Appends scan errors to the comments of the catalog entries they affected.
    /// </summary>
    public int ApplyScanIssuesToComments(IReadOnlyList<ScanIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0)
        {
            return 0;
        }

        if (catalogRepository is not null)
        {
            var changed = ScanIssueComments.Apply(catalogRepository.GetAll(), issues);
            foreach (var document in changed)
            {
                catalogRepository.Save(document.Id, document);
            }

            if (changed.Count > 0)
            {
                RefreshBrowserItemsForSelection();
            }

            return changed.Count;
        }

        return inMemoryCatalogEntries is null ? 0 : ScanIssueComments.Apply(inMemoryCatalogEntries, issues).Count;
    }

    private async Task<T> PublishWithMetadataAsync<T>(
        IReadOnlyList<CatalogDocument> documents,
        Func<T> publish,
        IOperationProgress? progress,
        CancellationToken cancellationToken,
        string? sessionId = null,
        Func<bool>? canPublish = null,
        bool describeNewFiles = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, metadataLifetime.Token);
        var token = linked.Token;
        T result;
        string[] addedFileIds = [];
        await metadataPublicationGate.WaitAsync(token);
        try
        {
            while (true)
            {
                var existing = catalogRepository?.GetAll()
                    ?? (IReadOnlyList<CatalogDocument>)(inMemoryCatalogEntries?.ToArray() ?? []);
                var prepared = catalogRepository is null ? documents : new CatalogEntryMerger().Merge(documents, existing);
                var preparedIds = prepared.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
                var catalog = existing.Where(entry => !preparedIds.Contains(entry.Id)).Concat(prepared).ToArray();
                var batchId = await StageMetadataAsync(prepared, catalog, progress, token, sessionId);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (catalogRepository is not null)
                    {
                        var current = new CatalogEntryMerger().Merge(documents, catalogRepository.GetAll());
                        if (!current.Select(entry => (entry.Id, entry.ParentId))
                            .SequenceEqual(prepared.Select(entry => (entry.Id, entry.ParentId))))
                        {
                            if (batchId is not null) mediaCacheStore.DiscardBatch(batchId);
                            continue;
                        }
                    }

                    if (canPublish is not null && !canPublish())
                    {
                        if (batchId is not null) mediaCacheStore.DiscardBatch(batchId);
                        return default!;
                    }

                    IReadOnlyList<MediaCacheRecord> stagedRecords =
                        batchId is null ? [] : mediaCacheStore.GetStagedRecords(batchId);
                    result = publish();
                    if (batchId is not null)
                    {
                        try
                        {
                            mediaCacheStore.PublishBatch(batchId);
                            InvalidateMediaCatalogEffects();
                            try
                            {
                                ApplyPluginTags(stagedRecords, prepared);
                                RefreshBrowserItemsForSelection();
                            }
                            catch (Exception exception)
                            {
                                progress?.ReportWarning(exception.Message);
                            }
                        }
                        catch (Exception exception)
                        {
                            progress?.ReportWarning(exception.Message);
                            try
                            {
                                mediaCacheStore.DiscardBatch(batchId);
                            }
                            catch (Exception discardException)
                            {
                                progress?.ReportWarning(discardException.Message);
                            }
                        }
                    }
                    if (describeNewFiles && aiDescriptionService is not null)
                    {
                        var existingIds = existing.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
                        var published = result is ScanPublishResult scan ? scan.PublishedDocuments : prepared;
                        addedFileIds = published.Where(entry => entry.Type == CatalogDocumentType.File &&
                            !existingIds.Contains(entry.Id)).Select(entry => entry.Id).ToArray();
                    }
                    break;
                }
                catch
                {
                    if (batchId is not null && !isDisposed) mediaCacheStore.DiscardBatch(batchId);
                    throw;
                }
            }
        }
        finally
        {
            metadataPublicationGate.Release();
        }

        await DescribeAddedFilesAsync(addedFileIds, progress, token);
        return result;
    }

    private async Task DescribeAddedFilesAsync(string[] entryIds, IOperationProgress? progress, CancellationToken cancellationToken)
    {
        if (entryIds.Length == 0 || aiDescriptionService is not { } service || cancellationToken.IsCancellationRequested)
            return;

        try
        {
            if (!service.GetProviderInfo().IsEnabled) return;

            var localization = LocalizationService.Current;
            var stage = localization.Get(UiKeys.AiDescriptionBatch);
            progress?.ReportStage(stage, stageCurrent: 0, stageTotal: entryIds.Length);
            IProgress<AiDescriptionBatchProgress>? descriptionProgress = progress is null ? null :
                new Progress<AiDescriptionBatchProgress>(item => progress.ReportItem(item.Item.Name,
                    stagePercentage: 100.0 * item.Completed / item.Total, stageCurrent: item.Completed, stageTotal: item.Total));
            var result = await Task.Run(() => service.GenerateBatchAsync(entryIds, regenerate: false,
                descriptionProgress, cancellationToken, attachOriginalFiles: true));
            var summary = localization.Format(TextKeys.AiDescriptionBatchSummary, result.GeneratedCount,
                result.SkippedCount, result.FailedCount, result.CancelledCount);
            if (result.FailedCount > 0) progress?.ReportWarning(summary, stage: stage);
            else progress?.ReportInfo(summary, stage: stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The catalog publication has already completed.
        }
        catch (Exception exception)
        {
            progress?.ReportWarning(LocalizationService.Current.Format(TextKeys.AiDescriptionBatchFailed, exception.Message));
        }
    }

    private async Task<string?> StageMetadataAsync(
        IReadOnlyList<CatalogDocument> documents,
        IReadOnlyList<CatalogDocument> catalog,
        IOperationProgress? progress,
        CancellationToken cancellationToken,
        string? sessionId)
    {
        var service = mediaMetadataService is { CanCollect: true } ? mediaMetadataService : null;
        var audioCdService = audioCdMetadataService is { CanCollect: true } ? audioCdMetadataService : null;

        var byId = catalog.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        IReadOnlyList<CatalogDocument> files = service is null
            ? []
            : documents.Where(entry => HasLocalSource(entry, byId)).ToArray();
        IReadOnlyList<AudioCdDiscRoot> discs = audioCdService is null
            ? []
            : AudioCdMetadataService.FindDiscRoots(documents);
        if (files.Count == 0 && discs.Count == 0)
        {
            return null;
        }

        string batchId;
        try
        {
            batchId = mediaCacheStore.BeginBatch(sessionId).BatchId;
        }
        catch (Exception exception)
        {
            progress?.ReportWarning(exception.Message);
            return null;
        }

        try
        {
            if (audioCdService is not null && discs.Count > 0)
            {
                progress?.ReportStage(LocalizationService.Current.Get(TextKeys.ReadingMediaMetadata));
                try
                {
                    await audioCdService.CollectAsync(documents, catalog, batchId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    progress?.ReportWarning(exception.Message);
                }
            }

            for (var index = 0; index < files.Count; index++)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                progress?.ReportStage(LocalizationService.Current.Get(TextKeys.ReadingMediaMetadata),
                    stagePercentage: index * 100d / files.Count, stageCurrent: index, stageTotal: files.Count);
                progress?.ReportItem(files[index].Name);
                try
                {
                    var currentIndex = index;
                    var itemProgress = progress is null ? null : new Progress<MediaProgress>(value =>
                        progress.ReportStage(LocalizationService.Current.Get(TextKeys.ReadingMediaMetadata),
                            stagePercentage: (currentIndex + Math.Clamp(value.Fraction, 0, 1)) * 100d / files.Count,
                            stageCurrent: currentIndex, stageTotal: files.Count));
                    var outcome = await service!.CollectAsync(files[index], catalog, itemProgress, cancellationToken,
                        batchId);
                    if (outcome.SkipReason == MediaMetadataSkipReason.Failed)
                    {
                        progress?.ReportWarning(outcome.FailureReason ?? outcome.SkipReason.ToString(), itemPath: files[index].Name);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    progress?.ReportWarning(exception.Message, itemPath: files[index].Name);
                }
            }
            return batchId;
        }
        catch
        {
            if (!isDisposed) mediaCacheStore.DiscardBatch(batchId);
            throw;
        }
    }

    private void PublishImportedDocuments(IReadOnlyList<CatalogDocument> documents, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (documents.Count == 0)
        {
            return;
        }

        if (catalogRepository is not null)
        {
            new CatalogScanPublisher().PublishDirect(documents, catalogRepository,
                new ScanPublishOptions { CancellationToken = cancellationToken }, undoManager?.History);
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
        var nodeKey = SelectedTreeNode?.Key;
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

        PublishImportedDocuments([importedItem], CancellationToken.None);
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

        RefreshQuickFind();

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

    public bool CanRenameTreeNode(BrowserTreeNode? node)
    {
        return node is not null && !string.IsNullOrWhiteSpace(node.Key);
    }

    public bool CanRenameItem(object? parameter = null)
    {
        return ResolveTargetPane(parameter) == ActivePane.Tree && CanRenameTreeNode(SelectedTreeNode);
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void RenameItem(object? parameter = null)
    {
        if (!CanRenameItem(parameter))
        {
            return;
        }

        BeginRename(SelectedTreeNode!);
    }

    /// <summary>
    /// Swaps the node's tree label for an inline editor. The label itself is not touched until the
    /// edit is confirmed, so cancelling leaves the displayed name exactly as it was.
    /// </summary>
    public void BeginRename(BrowserTreeNode node)
    {
        if (!CanRenameTreeNode(node))
        {
            return;
        }

        if (renamingNode is not null && !ReferenceEquals(renamingNode, node))
        {
            CancelRename(renamingNode, restoreFocus: false);
        }

        renamingNode = node;
        node.RenameError = null;
        node.RenameText = node.Title;
        node.IsRenaming = true;
        SelectedTreeNode = node;
        RenameRequested?.Invoke(this, EventArgs.Empty);
    }

    public void CommitRename(BrowserTreeNode? node = null)
    {
        node ??= renamingNode;
        if (node is null || !node.IsRenaming)
        {
            return;
        }

        var newName = (node.RenameText ?? string.Empty).Trim();
        if (newName.Length == 0)
        {
            node.RenameError = LocalizationService.Current.Get(TextKeys.CatalogNameRequired);
            StatusText = node.RenameError;
            return;
        }

        var previousName = node.Title;
        if (string.Equals(newName, previousName, StringComparison.Ordinal))
        {
            FinishRename(node);
            FocusTreeRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!TryPersistTreeRename(node, newName))
        {
            FinishRename(node);
            StatusText = LocalizationService.Current.Format(TextKeys.RenameStaleEntry, previousName);
            FocusTreeRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        node.Title = newName;
        FinishRename(node);
        RefreshBrowserItemsForSelection();
        IsDirtyDocument = true;
        StatusText = LocalizationService.Current.Format(TextKeys.Renamed, newName);
        FocusTreeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <param name="restoreFocus">
    /// False when the edit ended because the user moved focus elsewhere; pulling focus back to the
    /// tree in that case would fight the click that ended the edit.
    /// </param>
    public void CancelRename(BrowserTreeNode? node = null, bool restoreFocus = true)
    {
        node ??= renamingNode;
        if (node is null)
        {
            return;
        }

        FinishRename(node);
        if (restoreFocus)
        {
            FocusTreeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FinishRename(BrowserTreeNode node)
    {
        node.IsRenaming = false;
        node.RenameText = string.Empty;
        node.RenameError = null;
        if (ReferenceEquals(renamingNode, node))
        {
            renamingNode = null;
        }
    }

    private bool TryPersistTreeRename(BrowserTreeNode node, string newName)
    {
        if (catalogRepository is not null)
        {
            var stored = catalogRepository.Get(node.Key);
            if (stored is null)
            {
                return false;
            }

            RunCatalogEdit(LocalizationService.Current.Get(TextKeys.Rename), () =>
            {
                catalogRepository.Save(stored.Id, stored.WithName(newName));
                undoManager?.RecordRename(stored, stored.Name, newName);
            });
            return true;
        }

        if (inMemoryCatalogEntries is null)
        {
            return false;
        }

        var index = inMemoryCatalogEntries.FindIndex(entry =>
            string.Equals(entry.Id, node.Key, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        inMemoryCatalogEntries[index] = inMemoryCatalogEntries[index].WithName(newName);
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
        lock (viewRefreshLock)
        {
            RefreshTreeNodesCore(preferredSelectedKey);
        }
    }

    private void RefreshTreeNodesCore(string? preferredSelectedKey)
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
        ShowOptions(preselectSettingCategory: null);
    }

    [RelayCommand]
    private void OpenDetailsColumnsOptions()
    {
        ShowOptions(LocalizationService.Current.Get(UiKeys.DetailsColumns));
    }

    private void ShowOptions(string? preselectSettingCategory)
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
            PreselectSettingCategory = preselectSettingCategory,
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
    private void ToggleDetailsColumn(string? columnId)
    {
        var definition = DetailsColumnsCatalog.Find(columnId);
        if (definition is null || definition.IsRequired)
        {
            return;
        }

        var setting = detailsColumnSettings.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (setting is null)
        {
            return;
        }

        setting.IsVisible = !setting.IsVisible;
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        RefreshDetailsHeaderMenuItems();
        DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyDetailsColumnSettings(IEnumerable<DetailsColumnSettingDocument>? settings)
    {
        detailsColumnSettings = DetailsColumnsCatalog.Normalize(settings);
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        RefreshDetailsHeaderMenuItems();
    }

    /// <summary>
    /// Moves a visible column to a new position in the visible order. Returns <c>true</c> when the
    /// order actually changed, in which case <see cref="DetailsColumnsChanged"/> is raised so the host
    /// can persist the layout.
    /// </summary>
    public bool MoveDetailsColumn(string columnId, int targetVisibleIndex)
    {
        var updated = DetailsColumnsCatalog.MoveVisibleColumn(detailsColumnSettings, columnId, targetVisibleIndex);
        if (!SettingsDiffer(detailsColumnSettings, updated))
        {
            return false;
        }

        detailsColumnSettings = updated;
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        RefreshDetailsHeaderMenuItems();
        DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Stores a dragged column width. Returns <c>true</c> when the width actually changed, in which
    /// case <see cref="DetailsColumnsChanged"/> is raised so the host can persist the layout.
    /// </summary>
    public bool ResizeDetailsColumn(string columnId, double width)
    {
        var updated = DetailsColumnsCatalog.SetColumnWidth(detailsColumnSettings, columnId, width);
        if (!SettingsDiffer(detailsColumnSettings, updated))
        {
            return false;
        }

        detailsColumnSettings = updated;
        OnPropertyChanged(nameof(BrowserDetailsColumns));
        RefreshDetailsHeaderMenuItems();
        DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyList<DetailsColumnSettingDocument> GetDetailsColumnSettings()
    {
        return detailsColumnSettings
            .Select(static setting => new DetailsColumnSettingDocument
            {
                Id = setting.Id,
                IsVisible = setting.IsVisible,
                Width = setting.Width
            })
            .ToArray();
    }

    private static bool SettingsDiffer(
        IReadOnlyList<DetailsColumnSettingDocument> current,
        IReadOnlyList<DetailsColumnSettingDocument> updated)
    {
        if (current.Count != updated.Count)
        {
            return true;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].Id, updated[i].Id, StringComparison.OrdinalIgnoreCase) ||
                current[i].IsVisible != updated[i].IsVisible ||
                current[i].Width != updated[i].Width)
            {
                return true;
            }
        }

        return false;
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
        var oldProperties = targets.ToDictionary(t => t.Id, t => new PropertiesCollection(t.Properties));
        RunCatalogEdit(LocalizationService.Current.Get(TextKeys.Status), () =>
        {

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
                    foreach (var key in oldProperties[target.Id].Keys.Union(target.Properties.Keys))
                    {
                        oldProperties[target.Id].TryGetValue(key, out var oldValue);
                        target.Properties.TryGetValue(key, out var newValue);
                        undoManager?.RecordPropertyChange(target, key, oldValue, newValue);
                    }
                }
            }

        });
        UpdateUndoRedoState();
        IsDirtyDocument = true;
        RefreshBrowserItemsForSelection();
    }

    private bool IsTaggingEnabled() =>
        tagService is not null && GetSelectedBrowserItems().Count > 0;

    /// <summary>
    /// Tri-state bulk tag toggle: when every selected entry already carries the
    /// tag it is removed from all of them, otherwise it is added to the entries
    /// missing it. Unrelated tags are never touched.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsTaggingEnabled))]
    private void ToggleTagForEntries(string? tagId)
    {
        var targets = GetSelectedBrowserItems();
        if (tagService is null || targets.Count == 0 || string.IsNullOrWhiteSpace(tagId))
        {
            return;
        }

        var tagName = tagService.GetTags()
            .FirstOrDefault(tag => string.Equals(tag.Id, tagId, StringComparison.Ordinal))?.Name ?? tagId;
        var oldById = targets.ToDictionary(
            target => target.Id,
            target => (IReadOnlyList<string>)tagService.GetEntryTagIds(target.Id));
        var allAssigned = oldById.Values.All(ids => ids.Contains(tagId, StringComparer.Ordinal));
        var changes = new Dictionary<string, (IReadOnlyList<string> OldTagIds, IReadOnlyList<string> NewTagIds)>(StringComparer.Ordinal);
        var affectedCount = 0;

        foreach (var target in targets)
        {
            var oldIds = oldById[target.Id];
            IReadOnlyList<string> newIds = allAssigned
                ? oldIds.Where(id => !string.Equals(id, tagId, StringComparison.Ordinal)).ToArray()
                : oldIds.Contains(tagId, StringComparer.Ordinal)
                    ? oldIds
                    : [.. oldIds, tagId];
            if (!oldIds.SequenceEqual(newIds, StringComparer.Ordinal))
            {
                tagService.SetEntryTags(target.Id, newIds);
                changes[target.Id] = (oldIds, newIds);
                affectedCount++;
            }
        }

        if (changes.Count > 0)
        {
            undoManager?.RecordTagsBatchChange(changes);
        }

        StatusText = allAssigned
            ? LocalizationService.Current.Format(TextKeys.TagRemovedFromItems, tagName, affectedCount)
            : LocalizationService.Current.Format(TextKeys.TagAddedToItems, tagName, affectedCount);

        UpdateUndoRedoState();
        IsDirtyDocument = true;
        RefreshTagsMenuItems();
        RefreshBrowserItemsForSelection();
    }

    private (int Assigned, int Total) GetTagAssignmentCounts(string tagId)
    {
        var targets = GetSelectedBrowserItems();
        if (tagService is null || targets.Count == 0)
        {
            return (0, 0);
        }

        var assigned = targets.Count(target =>
            tagService.GetEntryTagIds(target.Id).Contains(tagId, StringComparer.Ordinal));
        return (assigned, targets.Count);
    }

    [RelayCommand]
    private void OpenTagsDialog()
    {
        if (tagService is null || tagRepository is null || catalogRepository is null)
        {
            return;
        }

        var dialog = new TagsDialogViewModel(
            tagService,
            undoManager,
            catalogRepository,
            tagRepository,
            onChanged: () =>
            {
                IsDirtyDocument = true;
                UpdateUndoRedoState();
                RefreshTagsMenuItems();
                RefreshBrowserItemsForSelection();
            },
            navigate: NavigateToDocument);
        TagsRequested?.Invoke(this, new TagsDialogRequestedEventArgs { Dialog = dialog });
    }

    [RelayCommand]
    private void OpenLocationsDialog()
    {
        ShowOptions(LocalizationService.Current.Get(UiKeys.Locations));
    }

    private void NavigateToDocument(CatalogDocument document)
    {
        NavigateToFindResult(new CatalogFindResult(document, string.Empty, string.Empty));
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
        if (ActivePane == ActivePane.Tree && SelectedTreeNode is not null)
        {
            var objectKey = GetTreeNodeObjectKey(SelectedTreeNode);
            var comments = GetObjectComments(objectKey, SelectedTreeNode.Key);

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedTreeNode.Title,
                SelectedTreeNode.IconGlyph,
                comments,
                GetBrowserItemInfoProperties(SelectedTreeNode.Key),
                propertyValueLocalizer);
            return true;
        }

        if (SelectedBrowserItem is not null)
        {
            var objectKey = GetBrowserItemObjectKey(SelectedBrowserItem);
            var comments = GetObjectComments(objectKey, SelectedBrowserItem.Id);
            var infoProperties = GetBrowserItemInfoProperties(SelectedBrowserItem.Id);

            dialog = new PropertiesDialogViewModel(
                objectKey,
                SelectedBrowserItem.Name,
                SelectedBrowserItem.IconGlyph,
                comments,
                infoProperties,
                propertyValueLocalizer);
            if (tagService is not null)
            {
                dialog.InitializeTags(
                    tagService.GetTags().Select(tag => (tag.Id, tag.Name)),
                    tagService.GetEntryTagIds(SelectedBrowserItem.Id));
            }

            if (aiDescriptionService is not null)
            {
                dialog.InitializeDescription(aiDescriptionService, SelectedBrowserItem.Id);
            }

            return true;
        }

        if (SelectedTreeNode is not null)
        {
            var objectKey = GetTreeNodeObjectKey(SelectedTreeNode);
            var comments = GetObjectComments(objectKey, SelectedTreeNode.Key);

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

    private string GetObjectComments(string objectKey, string documentId)
    {
        if (FindCatalogDocument(documentId) is { } document)
        {
            return document.Properties.TryGetValue("Comment", out var comment) ? comment?.ToString() ?? string.Empty : string.Empty;
        }
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
        lock (viewRefreshLock)
        {
            RefreshBrowserItemsForSelectionCore();
        }
    }

    private void RefreshBrowserItemsForSelectionCore()
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

        items = ProjectLocations(ApplyCatalogDecorations(items)).Where(MatchesLocationFilter).ToArray();
        ApplyPreferredNames(items);

        if (items.Length == 0)
        {
            BrowserItems = [];
            SelectedBrowserItem = null;
            SelectedBrowserItems = new List<CatalogDocument>();
            UpdateSelectedContentsNotice();
            RefreshQuickFind();
            return;
        }

        var refreshedItems = NormalizeSortMode(CurrentSortMode) switch
        {
            "Type" => items.OrderBy(static item => item.DisplayType)
                .ThenBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "Size" => items.OrderBy(static item => item.Size)
                .ThenBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => items.OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray()
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
        RefreshQuickFind();
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

    private void ApplyBrowserItemRenameIfNeeded(PropertiesDialogViewModel dialog, string? selectedId)
    {
        var requestedName = dialog.Name.Trim();
        if (selectedId is not null && catalogRepository?.Get(selectedId) is { } stored)
        {
            if (requestedName.Length == 0 || requestedName == stored.Name)
            {
                return;
            }
            catalogRepository.Save(stored.Id, new CatalogDocument
            {
                Id = stored.Id,
                Name = requestedName,
                ParentId = stored.ParentId,
                Type = stored.Type,
                Size = stored.Size,
                ChildrenCount = stored.ChildrenCount,
                ContentsState = stored.ContentsState,
                Status = stored.Status,
                Properties = new PropertiesCollection(stored.Properties)
            });
            undoManager?.RecordRename(stored, stored.Name, requestedName);
            RefreshTreeNodes();
            RefreshBrowserItemsForSelection();
            SelectedBrowserItem = BrowserItems.FirstOrDefault(item => item.Id == stored.Id);
            return;
        }
        if (SelectedBrowserItem is null || SelectedTreeNode is null)
        {
            return;
        }

        var nodeKey = SelectedTreeNode.Key;
        var currentDisplayName = SelectedBrowserItem.Name;
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
        RenameItemCommand.NotifyCanExecuteChanged();
        OpenPropertiesCommand.NotifyCanExecuteChanged();
        ExpandSelectionCommand.NotifyCanExecuteChanged();
        CollapseSelectionCommand.NotifyCanExecuteChanged();
        NavigateToFolderCommand.NotifyCanExecuteChanged();
        NavigateToParentFolderCommand.NotifyCanExecuteChanged();
        RefreshCollectionsMenuItems();
        ToggleCollectionForEntriesCommand.NotifyCanExecuteChanged();
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
        ToggleTagForEntriesCommand.NotifyCanExecuteChanged();
        RefreshTagsMenuItems();
        RefreshCollectionsMenuItems();
        RefreshFeatureContextMenus();
        ToggleCollectionForEntriesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBrowserItemsChanged(System.Collections.IList value)
    {
        lock (viewRefreshLock)
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
            ToggleTagForEntriesCommand.NotifyCanExecuteChanged();
            RefreshTagsMenuItems();
            RefreshCollectionsMenuItems();
            RefreshFeatureContextMenus();
            ToggleCollectionForEntriesCommand.NotifyCanExecuteChanged();
        }
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
        var undoGesture = OperatingSystem.IsMacOS() ? "Meta+Z" : "Ctrl+Z";
        var redoGesture = OperatingSystem.IsMacOS() ? "Meta+Shift+Z" : "Ctrl+Y";

        var menuItems = new List<MainMenuItemViewModel>
        {
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.File),
                Items =
                [
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.New), HotKey = "Ctrl+N", InputGesture = "Ctrl+N", Command = NewCatalogCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Open), HotKey = "Ctrl+O", InputGesture = "Ctrl+O", Command = OpenCatalogCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Save), HotKey = "Ctrl+S", InputGesture = "Ctrl+S", Command = SaveCatalogCommand },
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.SaveAs), HotKey = "F12", InputGesture = "F12", Command = SaveCatalogAsCommand },
                    Separator(),
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.Properties), HotKey = "Alt+Enter", InputGesture = "Alt+Enter", Command = OpenPropertiesCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Exit), Command = ExitApplicationCommand }
                ]
            },
            new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(TextKeys.Edit),
                Items =
                [
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Undo), HotKey = undoGesture, InputGesture = undoGesture, Command = UndoCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Redo), HotKey = redoGesture, InputGesture = redoGesture, Command = RedoCommand },
                    Separator(),
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Add), HotKey = "F2", InputGesture = "F2", Command = AddItemCommand },
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Delete), HotKey = "Delete", InputGesture = "Delete", Command = DeleteItemCommand },
                    new MainMenuItemViewModel
                    {
                        Key = "edit_status",
                        Header = LocalizationService.Current.Get(TextKeys.Status),
                        IsEnabled = IsDeleteEnabled,
                        Items = BuildStatusMenuItems()
                    },
                    Separator(),
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.Find), HotKey = "Ctrl+F", InputGesture = "Ctrl+F", Command = FindCatalogCommand },
                    new MainMenuItemViewModel
                        { Header = LocalizationService.Current.Get(TextKeys.AdvancedFind), Command = AdvancedFindCatalogCommand }
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
                    new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Refresh), HotKey = "F5", InputGesture = "F5", Command = RefreshCommand }
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
                { Header = LocalizationService.Current.Get(TextKeys.Options), HotKey = "Ctrl+Alt+O", InputGesture = "Ctrl+Alt+O", Command = OpenOptionsCommand },
            new MainMenuItemViewModel
            {
                Header = "Automatically sync mounted media",
                Command = ToggleBackgroundMediaSyncCommand,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = IsBackgroundMediaSyncEnabled
            }
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
                Properties = (ActivePane == ActivePane.Tree
                        ? SelectedTreeNode?.Key
                        : SelectedBrowserItem?.Id ?? SelectedTreeNode?.Key) is { Length: > 0 } entryId
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["EntryId"] = entryId }
                    : null,
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
            else
            {
                StatusText = LocalizationService.Current.Get(TextKeys.Done);
            }
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Current.Format(TextKeys.PluginCommandError, ex.Message);
        }
        finally { pluginCommandCancellation = null; }
    }

    private sealed class ViewModelHostCommandApi(MainWindowViewModel viewModel) : ICatalogCaptureHostApi, IDescriptionHostApi
    {
        private readonly string? rootId = viewModel.TreeNodes.FirstOrDefault()?.Key;
        private readonly string? catalogPath = viewModel.CurrentCatalogPath;
        private readonly string? descriptionEntryId = viewModel.GetSelectedBrowserItems() is { Count: 1 } selected ? selected[0].Id : null;

        public Task<DescriptionContext> GetDescriptionContextAsync(DescriptionContextOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (viewModel.isDisposed || catalogPath != viewModel.CurrentCatalogPath || descriptionEntryId is null || viewModel.catalogRepository is null)
                throw new DescriptionContextChangedException();
            return Task.FromResult(new CatalogDescriptionService(viewModel.catalogRepository, viewModel.mediaCacheStore).Build(descriptionEntryId, options));
        }

        public async Task SaveGeneratedDescriptionAsync(DescriptionContext context, GeneratedDescription description, CancellationToken cancellationToken)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (viewModel.isDisposed || catalogPath != viewModel.CurrentCatalogPath || descriptionEntryId != context.EntryId || viewModel.catalogRepository is null)
                    throw new DescriptionContextChangedException();
                new CatalogDescriptionService(viewModel.catalogRepository, viewModel.mediaCacheStore).Save(context, description);
                viewModel.IsDirtyDocument = true;
                viewModel.RefreshBrowserItemsForSelection();
            });
        }

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
        lock (viewRefreshLock)
        {
            ApplyMenuState(topMenuItems);
            ApplyMenuState(browserContextMenuItems);
            ApplyMenuState(treeContextMenuItems);
        }
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
            new MainMenuItemViewModel
            {
                Key = "edit_collections",
                Header = LocalizationService.Current.Get(UiKeys.Collections),
                Items = BuildCollectionsMenuItems()
            },
            Separator(),
            new MainMenuItemViewModel
            {
                Key = "edit_tags",
                Header = LocalizationService.Current.Get(UiKeys.Tags),
                Items = BuildTagsMenuItems()
            },
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
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Add), HotKey = "F2", InputGesture = "F2", Command = AddItemCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Delete), HotKey = "Delete", InputGesture = "Delete", Command = DeleteItemCommand, CommandParameter = "list" },
            Separator(),
            BuildLocationsMenu(),
            ..BuildFeatureMenuGroups(),
            ..BuildFeatureEntryCommandItems(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Properties), Command = OpenPropertiesCommand }
        ];
    }

    private CatalogEntry? GetSelectedCatalogEntry()
    {
        var document = SelectedBrowserItem;
        if (document is null && ActivePane == ActivePane.Tree && SelectedTreeNode?.Key is { Length: > 0 } key)
        {
            document = (catalogRepository is not null ? catalogRepository.GetAll() : inMemoryCatalogEntries ?? [])
                .FirstOrDefault(entry => entry.Id == key);
        }

        return document is null ? null : ToCatalogEntry(document);
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildFeatureEntryCommandItems()
    {
        // A plugin that already exposes a command through its own menu group keeps it there, so the
        // same command is not listed twice in one context menu.
        var groupedIds = menuExtensionManager.GetMenuContributions()
            .Select(contribution => contribution.CommandId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commands = (catalogFeatures?.GetEntryCommands() ?? [])
            .Where(command => !groupedIds.Contains(command.CommandId))
            .ToArray();
        if (commands.Length == 0 || GetSelectedCatalogEntry() is null)
        {
            return [];
        }

        var items = new List<MainMenuItemViewModel> { Separator() };
        foreach (var command in commands)
        {
            var commandId = command.CommandId;
            items.Add(new MainMenuItemViewModel
            {
                Key = commandId,
                Header = command.Title,
                Command = new AsyncRelayCommand(() => ExecuteFeatureEntryCommandAsync(commandId))
            });
        }

        return items;
    }

    private async Task ExecuteFeatureEntryCommandAsync(string commandId)
    {
        var entry = GetSelectedCatalogEntry();
        if (catalogFeatures is null || entry is null)
        {
            return;
        }

        try
        {
            await catalogFeatures.ExecuteEntryCommandAsync(commandId, entry);
        }
        catch (Exception exception)
        {
            StatusText = LocalizationService.Current.Format(TextKeys.PluginCommandError, exception.Message);
        }
    }

    private void RefreshFeatureContextMenus()
    {
        lock (viewRefreshLock)
        {
            ReplaceMenuItems(browserContextMenuItems, BuildBrowserContextMenuItems());
            ReplaceMenuItems(treeContextMenuItems, BuildTreeContextMenuItems());
            OnPropertyChanged(nameof(BrowserContextMenuItems));
            OnPropertyChanged(nameof(TreeContextMenuItems));
        }
    }

    private IReadOnlyList<MainMenuItemViewModel> BuildFeatureMenuGroups()
    {
        var groups = menuExtensionManager.GetMenuContributions()
            .Where(contribution => !string.Equals(contribution.Location, "Tools", StringComparison.OrdinalIgnoreCase))
            .GroupBy(contribution => contribution.Location, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToArray();
        if (groups.Length == 0)
        {
            return [];
        }

        var items = new List<MainMenuItemViewModel> { Separator() };
        foreach (var group in groups)
        {
            items.Add(new MainMenuItemViewModel
            {
                Key = "feature_" + group.Key,
                Header = group.Key,
                Items =
                [
                    .. group.Select(contribution => new MainMenuItemViewModel
                    {
                        Header = contribution.Title,
                        Command = new RelayCommand(() => ExecutePluginMenuCommand(contribution.CommandId))
                    })
                ]
            });
        }

        return items;
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
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Add), HotKey = "F2", InputGesture = "F2", Command = AddItemCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Rename), Command = RenameItemCommand, CommandParameter = "tree" },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Delete), HotKey = "Delete", InputGesture = "Delete", Command = DeleteItemCommand, CommandParameter = "tree" },
            Separator(),
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Copy), HotKey = "Ctrl+C", InputGesture = "Ctrl+C", Command = CopyCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.Paste), HotKey = "Ctrl+V", InputGesture = "Ctrl+V", Command = PasteCommand },
            new MainMenuItemViewModel { Header = LocalizationService.Current.Get(TextKeys.CutMenu), HotKey = "Ctrl+X", InputGesture = "Ctrl+X", Command = CutCommand },
            Separator(),
            BuildLocationsMenu(),
            ..BuildFeatureMenuGroups(),
            ..BuildFeatureEntryCommandItems(),
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
        var source = catalogRepository is not null
            ? catalogRepository.GetAll()
            : inMemoryCatalogEntries ?? [];
        return BuildTreeNodesFromEntries(ProjectLocations(ApplyCatalogDecorations(source)));
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

        var needsContentsState = document.ContentsState == CatalogContentsState.NotScanned &&
            !document.Properties.ContainsKey(TitleOnlyMedia.ContentsStatePropertyKey);
        // The effective location name is stored data resolved from the repository only, so it is
        // available offline without touching the media.
        var locationName = locationService is null ? string.Empty : locationService.GetEffectiveLocationName(document);
        if (!needsContentsState && locationName.Length == 0)
        {
            return document.Properties;
        }

        var copy = new PropertiesCollection(document.Properties);
        if (needsContentsState)
        {
            copy[TitleOnlyMedia.ContentsStatePropertyKey] = CatalogContentsState.NotScanned.ToString();
        }

        if (locationName.Length > 0)
        {
            copy["Location"] = locationName;
        }

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
            isExpanded)
        {
            DecorationIndicator = entry.BadgeText,
            LocationIndicator = entry.HasLocation ? entry.Location : null
        };
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

    /// <summary>
    /// Builds the Tags context submenu with one tri-state toggle per reusable
    /// tag (checked when all selected entries carry it, partial when only
    /// some do) plus the manager dialog entry.
    /// </summary>
    private IReadOnlyList<MainMenuItemViewModel> BuildTagsMenuItems()
    {
        var items = new List<MainMenuItemViewModel>();
        if (tagService is null)
        {
            items.Add(new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(UiKeys.Tags),
                IsEnabled = false
            });
            return items;
        }

        foreach (var tag in tagService.GetTags())
        {
            var (assigned, total) = GetTagAssignmentCounts(tag.Id);
            var icon = total == 0 || assigned == 0 ? "☐" : assigned == total ? "☑" : "◐";
            items.Add(new MainMenuItemViewModel
            {
                Header = tag.Name,
                Icon = icon,
                Command = ToggleTagForEntriesCommand,
                CommandParameter = tag.Id,
                IsEnabled = total > 0
            });
        }

        if (items.Count > 0)
        {
            items.Add(Separator());
        }

        items.Add(new MainMenuItemViewModel
        {
            Header = LocalizationService.Current.Get(UiKeys.ManageTags),
            Command = OpenTagsDialogCommand
        });
        return items;
    }

    private void RefreshTagsMenuItems()
    {
        var parent = browserContextMenuItems.FirstOrDefault(item => string.Equals(item.Key, "edit_tags", StringComparison.Ordinal));
        parent?.Items = BuildTagsMenuItems();
    }

    private bool IsCollectionMembershipEnabled() =>
        collectionService is not null && GetSelectedBrowserItems().Count > 0;

    /// <summary>
    /// Tri-state collection toggle: when every selected entry already belongs
    /// to the collection it is removed from all of them, otherwise it is added
    /// to the entries missing it. Duplicate memberships are impossible.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsCollectionMembershipEnabled))]
    private void ToggleCollectionForEntries(string? collectionId)
    {
        var targets = GetSelectedBrowserItems();
        var service = collectionService;
        if (service is null || catalogRepository is null || collectionRepository is null
            || targets.Count == 0 || string.IsNullOrWhiteSpace(collectionId))
        {
            return;
        }

        var collection = service.GetRequired(collectionId);
        if (collection.Kind != Documents.Enum.VirtualCollectionKind.Static)
        {
            return;
        }

        var targetIds = targets
            .Select(static target => target.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targetIds.Length == 0)
        {
            return;
        }

        var oldIds = service.GetEntryIds(collectionId);
        var memberIds = new HashSet<string>(oldIds, StringComparer.Ordinal);
        var allMember = targetIds.All(memberIds.Contains);
        var affectedCount = allMember
            ? targetIds.Length
            : targetIds.Count(id => !memberIds.Contains(id));
        if (affectedCount == 0)
        {
            return;
        }

        if (allMember)
        {
            service.RemoveEntries(collectionId, targetIds);
        }
        else
        {
            service.AddEntries(collectionId, targetIds);
        }

        undoManager?.RecordCollectionEntriesChanged(
            collectionRepository,
            collectionId,
            oldIds,
            service.GetEntryIds(collectionId));

        StatusText = allMember
            ? LocalizationService.Current.Format(TextKeys.CollectionRemovedFrom, collection.Name, affectedCount)
            : LocalizationService.Current.Format(TextKeys.CollectionAddedTo, collection.Name, affectedCount);

        UpdateUndoRedoState();
        IsDirtyDocument = true;
        RefreshCollectionsMenuItems();
        RefreshBrowserItemsForSelection();
    }

    /// <summary>
    /// Builds the Collections context submenu with one tri-state toggle per
    /// static collection (checked when all selected entries belong, partial
    /// when only some do) plus new-collection and manager entries. Dynamic
    /// collections are listed for navigation but cannot gain members.
    /// </summary>
    private IReadOnlyList<MainMenuItemViewModel> BuildCollectionsMenuItems()
    {
        var items = new List<MainMenuItemViewModel>();
        if (collectionService is null)
        {
            items.Add(new MainMenuItemViewModel
            {
                Header = LocalizationService.Current.Get(UiKeys.Collections),
                IsEnabled = false
            });
            return items;
        }

        foreach (var collection in collectionService.GetCollections())
        {
            if (collection.Kind != Documents.Enum.VirtualCollectionKind.Static)
            {
                items.Add(new MainMenuItemViewModel
                {
                    Header = collection.Name,
                    Command = OpenCollectionsDialogCommand
                });
                continue;
            }

            var (assigned, total) = GetCollectionAssignmentCounts(collection.Id);
            var icon = total == 0 || assigned == 0 ? "☐" : assigned == total ? "☑" : "◐";
            items.Add(new MainMenuItemViewModel
            {
                Header = collection.Name,
                Icon = icon,
                Command = ToggleCollectionForEntriesCommand,
                CommandParameter = collection.Id,
                IsEnabled = total > 0
            });
        }

        if (items.Count > 0)
        {
            items.Add(Separator());
        }

        items.Add(new MainMenuItemViewModel
        {
            Header = LocalizationService.Current.Get(UiKeys.NewCollection),
            Command = CreateCollectionCommand
        });
        items.Add(new MainMenuItemViewModel
        {
            Header = LocalizationService.Current.Get(UiKeys.ManageCollections),
            Command = OpenCollectionsDialogCommand
        });
        return items;
    }

    private (int Assigned, int Total) GetCollectionAssignmentCounts(string collectionId)
    {
        var targets = GetSelectedBrowserItems();
        if (collectionService is null || targets.Count == 0)
        {
            return (0, 0);
        }

        var memberIds = new HashSet<string>(collectionService.GetEntryIds(collectionId), StringComparer.Ordinal);
        var assigned = targets.Count(target => memberIds.Contains(target.Id));
        return (assigned, targets.Count);
    }

    private void RefreshCollectionsMenuItems()
    {
        var parent = browserContextMenuItems.FirstOrDefault(item => string.Equals(item.Key, "edit_collections", StringComparison.Ordinal));
        parent?.Items = BuildCollectionsMenuItems();
    }

    [RelayCommand]
    private void CreateCollection()
    {
        if (collectionService is null || collectionRepository is null)
        {
            return;
        }

        var created = collectionService.CreateCollection("Untitled collection");
        undoManager?.RecordCollectionCreated(collectionRepository, created.Id, created.Name);
        IsDirtyDocument = true;
        UpdateUndoRedoState();
        RefreshCollectionsMenuItems();
    }

    [RelayCommand]
    private void OpenCollectionsDialog()
    {
        if (collectionService is null || collectionRepository is null || catalogRepository is null)
        {
            return;
        }

        var dialog = new CollectionsDialogViewModel(
            collectionService,
            undoManager,
            catalogRepository,
            collectionRepository,
            onChanged: () =>
            {
                IsDirtyDocument = true;
                UpdateUndoRedoState();
                RefreshCollectionsMenuItems();
                RefreshBrowserItemsForSelection();
            },
            navigate: NavigateToDocument);
        CollectionsRequested?.Invoke(this, new CollectionsDialogRequestedEventArgs { Dialog = dialog });
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

