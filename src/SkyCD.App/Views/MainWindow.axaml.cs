using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SkyCD.App.Marketplace;
using SkyCD.App.Plugins;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.Versioning;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan PluginConfigurationTimeout = TimeSpan.FromMinutes(5);
    private readonly RepositoryManager repositoryManager;
    private readonly AppOptionsDocumentRepository appOptionsRepository;
    private readonly CatalogDocumentRepository catalogRepository;
    private readonly StatusVariantDocumentRepository statusVariantRepository;
    private readonly ScanExclusionRuleDocumentRepository scanExclusionRuleRepository;
    private readonly AiScanExclusionDocumentRepository aiScanExclusionRepository;
    private readonly VirtualCollectionDocumentRepository virtualCollectionRepository;
    private readonly PluginManager pluginManager;
    private readonly HostVersionProvider hostVersionProvider;
    private readonly PluginRuntimeSynchronizer pluginRuntimeSynchronizer;
    private readonly ILogger<MainWindow> logger = ServiceProvider.Resolve<ILogger<MainWindow>>();
    private readonly OpenCatalogState openCatalogState;
    private readonly IMediaDriveProvider mediaDriveProvider;
    private readonly SystemMediaEjector mediaEjector = new();
    private readonly ScanCompletionService scanCompletionService;
    private readonly PluginMarketplaceService pluginMarketplaceService;
    private readonly IPluginPackageUninstaller pluginUninstaller;
    private FileFormatManager fileFormatManager;
    private MainWindowViewModel? subscribedViewModel;
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;
    private bool isWindowPlacementDeferred;
    private readonly CatalogFileActions catalogFiles;
    private readonly MainWindowSessionState sessionState;
    private IReadOnlyList<MediaDriveInfo>? dragDrives;
    private Control? dropTargetContainer;
    private DropDestination dropDestination = DropDestination.LibraryRoot;
    private readonly SemaphoreSlim pluginErrorDialogGate = new(1, 1);
    private readonly Dictionary<string, IImage> toolbarIcons = new(StringComparer.Ordinal);
    private readonly List<AddingProgressDialogViewModel> openAddingDialogs = [];
    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    public MainWindow(
        RepositoryManager repositoryManager,
        PluginManager pluginManager,
        PluginRuntimeSynchronizer pluginRuntimeSynchronizer,
        PluginMarketplaceService pluginMarketplaceService,
        IPluginPackageUninstaller pluginUninstaller,
        OpenCatalogState openCatalogState,
        IMediaDriveProvider? mediaDriveProvider = null)
    {
        this.repositoryManager = repositoryManager;
        this.pluginMarketplaceService = pluginMarketplaceService;
        this.pluginUninstaller = pluginUninstaller;
        this.openCatalogState = openCatalogState;
        this.mediaDriveProvider = mediaDriveProvider ?? new SystemMediaDriveProvider();
        appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        statusVariantRepository = (StatusVariantDocumentRepository)repositoryManager.For<StatusVariantDocument>();
        scanExclusionRuleRepository =
            (ScanExclusionRuleDocumentRepository)repositoryManager.For<ScanExclusionRuleDocument>();
        aiScanExclusionRepository =
            (AiScanExclusionDocumentRepository)repositoryManager.For<AiScanExclusionDocument>();
        virtualCollectionRepository =
            (VirtualCollectionDocumentRepository)repositoryManager.For<VirtualCollectionDocument>();
        this.pluginManager = pluginManager;
        this.pluginRuntimeSynchronizer = pluginRuntimeSynchronizer;
        hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();
        // File-format capabilities live in the plugin runtime container, never in the host container this
        // window is resolved from; a constructor-injected facade would stay empty for the whole session.
        fileFormatManager = ServiceProvider.Resolve<FileFormatManager>();
        scanCompletionService = new ScanCompletionService(
            mediaEjector,
            new DesktopMainWindowLocator(),
            ServiceProvider.Resolve<ILogger<ScanCompletionService>>());
        catalogFiles = new CatalogFileActions(this);
        sessionState = new MainWindowSessionState(appOptionsRepository);
        InitializeComponent();
        ApplyWindowBounds(LoadAppOptions());
        AttachPaneFocusHandlers();
        AddHandler(InputElement.KeyDownEvent, OnMainWindowKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        QuickFindResultList.AddHandler(InputElement.KeyDownEvent, OnQuickFindResultKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        PluginFailure.Reported += OnPluginFailure;
        Closed += (_, _) =>
        {
            PluginFailure.Reported -= OnPluginFailure;
            subscribedViewModel?.Dispose();
        };
    }

    private void OnPluginFailure(PluginFailure failure)
    {
        // While an add is running, the failure belongs in its progress log instead of a modal on top
        // of the Adding dialog; the routed log line keeps the fingerprint for later triage.
        if (openAddingDialogs.LastOrDefault() is { } adding)
        {
            adding.ReportPluginFailure(failure);
            return;
        }

        Dispatcher.UIThread.Post(() => _ = RunUiActionAsync(async () =>
        {
            await pluginErrorDialogGate.WaitAsync();
            try
            {
                if (IsVisible)
                {
                    await new PluginErrorWindow(failure).ShowDialog(this);
                }
            }
            finally
            {
                pluginErrorDialogGate.Release();
            }
        }));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.AddToListRequested -= OnAddToListRequested;
            subscribedViewModel.NewCatalogRequested -= OnNewCatalogRequested;
            subscribedViewModel.OpenCatalogRequested -= OnOpenCatalogRequested;
            subscribedViewModel.SaveCatalogAsRequested -= OnSaveCatalogAsRequested;
            subscribedViewModel.SaveCatalogRequested -= OnSaveCatalogRequested;
            subscribedViewModel.SplitCatalogRequested -= OnSplitCatalogRequested;
            subscribedViewModel.ExportCatalogRequested -= OnExportCatalogRequested;
            subscribedViewModel.AboutRequested -= OnAboutRequested;
            subscribedViewModel.FindCatalogRequested -= OnFindCatalogRequested;
            subscribedViewModel.AdvancedFindRequested -= OnAdvancedFindRequested;
            subscribedViewModel.OptionsRequested -= OnOptionsRequested;
            subscribedViewModel.PropertiesRequested -= OnPropertiesRequested;
            subscribedViewModel.TagsRequested -= OnTagsRequested;
            subscribedViewModel.ExitRequested -= OnExitRequested;
            subscribedViewModel.FocusBrowserRequested -= OnFocusBrowserRequested;
            subscribedViewModel.FocusTreeRequested -= OnFocusTreeRequested;
            subscribedViewModel.RenameRequested -= OnRenameRequested;
            subscribedViewModel.DetailsColumnsChanged -= OnDetailsColumnsChanged;
            subscribedViewModel.ToolbarItemsChanged -= OnToolbarItemsChanged;
            subscribedViewModel.ToolbarStateChanged -= OnToolbarStateChanged;
            subscribedViewModel.ToolbarLayoutChanged -= OnToolbarLayoutChanged;
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.ExternalLinkFailureLog = null;
            if (!ReferenceEquals(subscribedViewModel, DataContext)) subscribedViewModel.Dispose();
        }

        subscribedViewModel = DataContext as MainWindowViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.AddToListRequested += OnAddToListRequested;
            subscribedViewModel.NewCatalogRequested += OnNewCatalogRequested;
            subscribedViewModel.OpenCatalogRequested += OnOpenCatalogRequested;
            subscribedViewModel.SaveCatalogAsRequested += OnSaveCatalogAsRequested;
            subscribedViewModel.SaveCatalogRequested += OnSaveCatalogRequested;
            subscribedViewModel.SplitCatalogRequested += OnSplitCatalogRequested;
            subscribedViewModel.ExportCatalogRequested += OnExportCatalogRequested;
            subscribedViewModel.AboutRequested += OnAboutRequested;
            subscribedViewModel.FindCatalogRequested += OnFindCatalogRequested;
            subscribedViewModel.AdvancedFindRequested += OnAdvancedFindRequested;
            subscribedViewModel.OptionsRequested += OnOptionsRequested;
            subscribedViewModel.PropertiesRequested += OnPropertiesRequested;
            subscribedViewModel.TagsRequested += OnTagsRequested;
            subscribedViewModel.ExitRequested += OnExitRequested;
            subscribedViewModel.FocusBrowserRequested += OnFocusBrowserRequested;
            subscribedViewModel.FocusTreeRequested += OnFocusTreeRequested;
            subscribedViewModel.RenameRequested += OnRenameRequested;
            subscribedViewModel.DetailsColumnsChanged += OnDetailsColumnsChanged;
            subscribedViewModel.ToolbarItemsChanged += OnToolbarItemsChanged;
            subscribedViewModel.ToolbarStateChanged += OnToolbarStateChanged;
            subscribedViewModel.ToolbarLayoutChanged += OnToolbarLayoutChanged;
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            subscribedViewModel.ExternalLinkFailureLog =
                message => logger.LogWarning("{ExternalLinkFailure}", message);
            subscribedViewModel.SetStatusVariants(statusVariantRepository.GetOrdered());
            var appOptions = LoadAppOptions();
            subscribedViewModel.IsBackgroundMediaSyncEnabled = appOptions.IsBackgroundMediaSyncEnabled;
            subscribedViewModel.LoadToolbarLayout(appOptions.Toolbar);
            PushFileFormatAvailability();
            UpdateWindowTitle();
        }
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnDetailsColumnsChanged(object? sender, EventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        var options = LoadAppOptions();
        options.Browser.DetailsColumns = viewModel.GetDetailsColumnSettings().ToList();
        SaveAppOptions(options);
    }

    private void OnDetailsColumnReordered(object? sender, DetailsColumnReorderedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.MoveDetailsColumn(e.ColumnId, e.TargetIndex);
        }
    }

    private void OnDetailsColumnResized(object? sender, DetailsColumnResizedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ResizeDetailsColumn(e.ColumnId, e.Width);
        }
    }

    private void OnDetailsColumnClicked(object? sender, DetailsColumnClickedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SortByColumnCommand.Execute(e.ColumnId);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsBackgroundMediaSyncEnabled)
            && sender is MainWindowViewModel viewModel)
        {
            var options = LoadAppOptions();
            options.IsBackgroundMediaSyncEnabled = viewModel.IsBackgroundMediaSyncEnabled;
            SaveAppOptions(options);
        }
        if (e.PropertyName is nameof(MainWindowViewModel.IsDirtyDocument) or nameof(MainWindowViewModel.CurrentCatalogPath))
        {
            UpdateWindowTitle();
        }
    }

    private void UpdateWindowTitle()
    {
        var currentPath = subscribedViewModel?.CurrentCatalogPath;
        var baseTitle = string.IsNullOrWhiteSpace(currentPath)
            ? "SkyCD"
            : $"SkyCD - {Path.GetFileName(currentPath)}";

        if (subscribedViewModel is not null && subscribedViewModel.IsDirtyDocument)
        {
            Title = $"* {baseTitle}";
        }
        else
        {
            Title = baseTitle;
        }
    }

    private void OnPropertiesRequested(object? sender, PropertiesDialogRequestedEventArgs e) =>
        _ = RunUiActionAsync(() => PropertiesRequestedAsync(e));

    private async Task PropertiesRequestedAsync(PropertiesDialogRequestedEventArgs e)
    {
        e.Dialog.ApplyCustomPropertyDefaults(LoadAppOptions().CustomProperties);
        var dialog = new PropertiesWindow
        {
            DataContext = e.Dialog
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        e.Complete(accepted == true, e.Dialog.Comments);
    }

    private void OnTagsRequested(object? sender, TagsDialogRequestedEventArgs e) =>
        _ = RunUiActionAsync(() => TagsRequestedAsync(e));

    private async Task TagsRequestedAsync(TagsDialogRequestedEventArgs e)
    {
        var dialog = new TagsWindow
        {
            DataContext = e.Dialog
        };
        await dialog.ShowDialog(this);
    }

    private void OnAboutRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(AboutRequestedAsync);

    private async Task AboutRequestedAsync()
    {
        var options = LoadAppOptions();
        var dialogVm = AboutDialogViewModel.CreateFromMainAssembly(
            typeof(App).Assembly,
            applicationSnapshot: CreateAboutApplicationSnapshot());
        dialogVm.RestoreSelectedTab(options.AboutTabKey);
        var dialog = new AboutWindow
        {
            DataContext = dialogVm
        };

        await dialog.ShowDialog<bool?>(this);

        options.AboutTabKey = dialogVm.SelectedTabKey;
        SaveAppOptions(options);
    }

    private AboutApplicationSnapshot CreateAboutApplicationSnapshot()
    {
        var enabledPluginIds = pluginManager.GetPluginDescriptors()
            .Where(static descriptor => descriptor.IsEnabled && !string.IsNullOrWhiteSpace(descriptor.Id))
            .Select(static descriptor => descriptor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabledPluginCount = pluginManager.AvailablePlugins
            .Count(plugin => enabledPluginIds.Contains(plugin.Id));
        var catalogPath = openCatalogState.Path;

        return new AboutApplicationSnapshot(
            HostVersion: hostVersionProvider.Current.ToString(),
            LoadedPluginCount: pluginManager.Plugins.Count,
            EnabledPluginCount: enabledPluginCount,
            CatalogName: string.IsNullOrWhiteSpace(catalogPath) ? null : Path.GetFileName(catalogPath),
            CatalogEntryCount: openCatalogState.Repository?.GetAll().Count);
    }

    private void ApplyWindowBounds(AppOptionsDocument options) =>
        isWindowPlacementDeferred = MainWindowPlacement.Apply(this, TreePaneColumn, options.Window);

    private AppOptionsDocument LoadAppOptions()
    {
        return appOptionsRepository.GetOrCreateAppOptions();
    }

    private void SaveAppOptions(AppOptionsDocument options)
    {
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
    }

    private static void ApplyLanguage(string? languageName)
    {
        LocalizationService.Current.SetLanguage(languageName);
    }

    private void RefreshMcpStatusIndicator()
    {
        if (Application.Current is not App app)
        {
            MainStatusBar.IsMcpStatusVisible = true;
            MainStatusBar.McpStatusGlyph = "◎";
            MainStatusBar.McpStatusColor = "#9CA3AF";
            MainStatusBar.McpStatusTooltip = LocalizationService.Current.Get(TextKeys.MCPServerUnavailable);
            return;
        }

        var options = LoadAppOptions();
        MainStatusBar.IsMcpStatusVisible = options.IsMcpStatusIconVisible;

        var (isRunning, baseUrl) = app.GetMcpStatus();
        MainStatusBar.McpStatusGlyph = isRunning ? "◉" : "◎";
        MainStatusBar.McpStatusColor = isRunning ? "#22C55E" : "#9CA3AF";
        MainStatusBar.McpStatusTooltip = isRunning
            ? LocalizationService.Current.Format(TextKeys.MCPServerRunningAt, baseUrl ?? string.Empty)
            : LocalizationService.Current.Get(TextKeys.MCPServerStopped);
    }

    private async Task RunUiActionAsync(Func<Task> action, [CallerMemberName] string handler = "")
    {
        try
        {
            // Start on the UI thread immediately so Closing and routed events can set their flags synchronously.
            await action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Main window action {Handler} failed.", handler);
            if (DataContext is MainWindowViewModel vm)
            {
                vm.StatusText = $"{LocalizationService.Current.Get(UiKeys.Failed)}: {exception.GetBaseException().Message}";
            }
        }
    }
}
