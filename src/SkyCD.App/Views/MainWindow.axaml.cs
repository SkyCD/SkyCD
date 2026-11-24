using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DryIoc;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase;
using SkyCD.App.Icons;
using SkyCD.App.Marketplace;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Abstractions;
using Avalonia.Threading;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Core.Versioning;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Plugins;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Controls.Toolbars;
using SkyCD.UI.Controls.Tree;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private static readonly IStringLocalizer PickerLocalizer = new PropertyValueLocalizer();
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
    private Task? catalogLoadTask;
    private IReadOnlyList<MediaDriveInfo>? dragDrives;
    private readonly SemaphoreSlim pluginErrorDialogGate = new(1, 1);
    private readonly Dictionary<string, IImage> toolbarIcons = new(StringComparer.Ordinal);
    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    public MainWindow(
        RepositoryManager repositoryManager,
        PluginManager pluginManager,
        FileFormatManager fileFormatManager,
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
        hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();
        this.fileFormatManager = fileFormatManager;
        scanCompletionService = new ScanCompletionService(
            mediaEjector,
            new DesktopMainWindowLocator(),
            ServiceProvider.Resolve<ILogger<ScanCompletionService>>());
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
        Dispatcher.UIThread.Post(async () =>
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
        });
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
            subscribedViewModel.AboutRequested -= OnAboutRequested;
            subscribedViewModel.FindCatalogRequested -= OnFindCatalogRequested;
            subscribedViewModel.CollectionsRequested -= OnCollectionsRequested;
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
            subscribedViewModel.AboutRequested += OnAboutRequested;
            subscribedViewModel.FindCatalogRequested += OnFindCatalogRequested;
            subscribedViewModel.CollectionsRequested += OnCollectionsRequested;
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

    private void OnToolbarItemsChanged(object? sender, EventArgs e)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            RebuildToolbar(viewModel);
        }
    }

    private void OnToolbarStateChanged(object? sender, EventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        foreach (var command in viewModel.ToolbarCommands())
        {
            if (command.IsToggle)
            {
                MainToolbar.SetCheckedState(command.Id, command.IsChecked);
            }
        }
    }

    private void OnToolbarLayoutChanged(object? sender, EventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        var options = LoadAppOptions();
        options.Toolbar = viewModel.ToolbarEntries.ToList();
        SaveAppOptions(options);
    }

    private void RebuildToolbar(MainWindowViewModel viewModel)
    {
        var commands = viewModel.ToolbarCommands().ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase);
        MainToolbar.Items.Clear();
        foreach (var entry in viewModel.ToolbarEntries)
        {
            if (string.Equals(entry.Kind, ToolbarItemDocument.SeparatorKind, StringComparison.OrdinalIgnoreCase))
            {
                MainToolbar.Items.Add(new ClassicToolbarSeparator());
                continue;
            }

            if (entry.CommandId is not { } commandId || !commands.TryGetValue(commandId, out var descriptor))
            {
                continue;
            }

            MainToolbar.Items.Add(new ClassicToolbarButton
            {
                CommandId = descriptor.Id,
                Command = descriptor.Command,
                CommandParameter = descriptor.CommandParameter,
                Text = descriptor.Label,
                ImageSrc = LoadToolbarIcon(descriptor.IconPath),
                IsToggle = descriptor.IsToggle,
                IsChecked = descriptor.IsChecked
            });
        }
    }

    private IImage? LoadToolbarIcon(string? iconPath)
    {
        if (string.IsNullOrEmpty(iconPath))
        {
            return null;
        }

        if (toolbarIcons.TryGetValue(iconPath, out var cached))
        {
            return cached;
        }

        // Keys of the project-owned glyph dictionary render as vector images; asset paths fall back to bitmaps.
        var icon = ToolbarIconResolver.Resolve(iconPath)
            ?? throw new InvalidOperationException($"Toolbar command icon '{iconPath}' could not be resolved.");
        toolbarIcons[iconPath] = icon;
        return icon;
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

    private void OnFocusBrowserRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");
            if (browserView is not null)
            {
                browserView.FocusActiveControl();
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Browser;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnFocusTreeRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var treeView = this.FindControl<TreeView>("MainTreeView");
            if (treeView is null || subscribedViewModel is null)
            {
                return;
            }

            TreeViewFocus.FocusItem(treeView, subscribedViewModel.SelectedTreeNode);
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }, DispatcherPriority.Loaded);
    }

    private void OnRenameRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (subscribedViewModel?.EditingBrowserItemId is not null)
            {
                FocusBrowserRenameEditor(attempts: 3);
                return;
            }

            var treeView = this.FindControl<TreeView>("MainTreeView");
            if (treeView is null || subscribedViewModel?.SelectedTreeNode is not { IsRenaming: true } node)
            {
                return;
            }

            var container = TreeViewFocus.FindContainer(treeView, node) as TreeViewItem;
            var editor = container?.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(static textBox => textBox.IsVisible);
            if (editor is null)
            {
                return;
            }

            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    // The row may not be realized yet when the edit starts, so retry until virtualization has
    // produced the editor.
    private void FocusBrowserRenameEditor(int attempts)
    {
        var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");
        if (browserView?.FocusRenameEditor() == true || attempts <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => FocusBrowserRenameEditor(attempts - 1), DispatcherPriority.Background);
    }

    private void OnBrowserRenameCommitted(object? sender, EventArgs e)
    {
        subscribedViewModel?.CommitBrowserItemRename();
    }

    private void OnBrowserRenameCancelled(object? sender, EventArgs e)
    {
        subscribedViewModel?.CancelBrowserItemRename();
    }

    private void OnBrowserRenameAbandoned(object? sender, EventArgs e)
    {
        subscribedViewModel?.CancelBrowserItemRename(restoreFocus: false);
    }

    private void OnRenameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (subscribedViewModel is null || sender is not TextBox { DataContext: BrowserTreeNode node })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            subscribedViewModel.CommitRename(node);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            subscribedViewModel.CancelRename(node);
        }
    }

    private void OnRenameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (subscribedViewModel is null || sender is not TextBox { DataContext: BrowserTreeNode node } || !node.IsRenaming)
        {
            return;
        }

        subscribedViewModel.CancelRename(node, restoreFocus: false);
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TreeView treeView || subscribedViewModel is null)
        {
            return;
        }

        if (!e.TryGetPosition(treeView, out var point))
        {
            e.Handled = subscribedViewModel.SelectedTreeNode is null;
            return;
        }

        var hit = treeView.InputHitTest(point) as Visual;
        var treeViewItem = FindAncestor<TreeViewItem>(hit);
        if (treeViewItem?.DataContext is BrowserTreeNode node)
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
            subscribedViewModel.SelectedTreeNode = node;
            return;
        }

        e.Handled = true;
    }

    private void AttachPaneFocusHandlers()
    {
        var treeView = this.FindControl<TreeView>("MainTreeView");
        if (treeView is not null)
        {
            treeView.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Tree;
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            treeView.AddHandler(InputElement.GotFocusEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Tree;
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");
        if (browserView is not null)
        {
            browserView.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Browser;
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            browserView.AddHandler(InputElement.GotFocusEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Browser;
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (OwnedWindows is { Count: > 0 } && OwnedWindows.Any(static w => w?.IsVisible == true))
            {
                return;
            }
        }
        catch
        {
            // In headless or unit test environments OwnedWindows may not be initialized
        }

        if (subscribedViewModel is { IsQuickFilterOpen: true } vm && e.Key == Key.Escape)
        {
            vm.CloseQuickFilterCommand.Execute(null);
            MainBrowserItemsView.FocusActiveControl();
            e.Handled = true;
            return;
        }

        if (subscribedViewModel?.ShowQuickFindResults == true &&
            e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None && GetFocusedElement() == QuickFindBox)
        {
            QuickFindResultList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && e.Source is Visual source &&
            (source == QuickFindResultList || QuickFindResultList.IsVisualAncestorOf(source)))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            var focused = GetFocusedElement();
            if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
            {
                return;
            }

            if (subscribedViewModel is not null)
            {
                UpdateActivePaneFromFocusedElement(focused);

                if (subscribedViewModel.DeleteItemCommand.CanExecute(null))
                {
                    subscribedViewModel.DeleteItemCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
        else if ((e.Key == Key.Back || e.Key == Key.BrowserBack) && e.KeyModifiers == KeyModifiers.None)
        {
            var focused = GetFocusedElement();
            if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
            {
                return;
            }

            if (subscribedViewModel is not null)
            {
                UpdateActivePaneFromFocusedElement(focused);

                if (subscribedViewModel.NavigateToParentFolderCommand.CanExecute(null))
                {
                    subscribedViewModel.NavigateToParentFolderCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
        else if (e.Key == Key.Z && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
        {
            var focused = GetFocusedElement();
            if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
            {
                return;
            }

            if (subscribedViewModel?.UndoCommand.CanExecute(null) == true)
            {
                subscribedViewModel.UndoCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if ((e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control && !OperatingSystem.IsMacOS())
            || (e.Key == Key.Z && e.KeyModifiers == ((OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift)))
        {
            var focused = GetFocusedElement();
            if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
            {
                return;
            }

            if (subscribedViewModel?.RedoCommand.CanExecute(null) == true)
            {
                subscribedViewModel.RedoCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            if (subscribedViewModel?.FindCatalogCommand.CanExecute(null) == true)
            {
                subscribedViewModel.FindCatalogCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnFindCatalogRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (subscribedViewModel?.IsQuickFilterOpen != true) return;
            QuickFindBox.Focus();
            QuickFindBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnQuickFindResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not null)
        {
            OpenQuickFindResult();
        }
    }

    private void OnQuickFindResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        OpenQuickFindResult();
        e.Handled = true;
    }

    private void OpenQuickFindResult()
    {
        if (subscribedViewModel is not { SelectedQuickFindResult: not null } vm) return;
        vm.OpenQuickFindResultCommand.Execute(null);
        MainBrowserItemsView.FocusActiveControl();
    }

    private async void OnCollectionsRequested(object? sender, CollectionsDialogRequestedEventArgs e)
    {
        await new CollectionsWindow { DataContext = e.Dialog }.ShowDialog<bool?>(this);
    }

    private async void OnAdvancedFindRequested(object? sender, AdvancedFindDialogRequestedEventArgs e)
    {
        await new AdvancedCatalogFindWindow { DataContext = e.Dialog }.ShowDialog<bool?>(this);
    }

    private IInputElement? GetFocusedElement()
    {
        try
        {
            return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }
        catch
        {
            return null;
        }
    }

    private void UpdateActivePaneFromFocusedElement(IInputElement? focused)
    {
        if (focused is not Visual visual || subscribedViewModel is null)
        {
            return;
        }

        var treeView = this.FindControl<TreeView>("MainTreeView");
        var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");

        if (treeView is not null && (visual == treeView || treeView.IsVisualAncestorOf(visual)))
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }
        else if (browserView is not null && (visual == browserView || browserView.IsVisualAncestorOf(visual)))
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
        }
    }

    private void OnTreeGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }
    }

    private void OnBrowserContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.ActivePane = ActivePane.Browser;

        if (!e.TryGetPosition(control, out var point))
        {
            e.Handled = subscribedViewModel.SelectedBrowserItem is null;
            return;
        }

        var hit = control.InputHitTest(point) as Visual;
        var listBoxItem = FindAncestor<ListBoxItem>(hit);
        if (listBoxItem?.DataContext is CatalogDocument item)
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
            var currentSelected = subscribedViewModel.SelectedBrowserItems.OfType<CatalogDocument>().ToList();
            if (!currentSelected.Any(sel => string.Equals(sel.Id, item.Id, StringComparison.Ordinal)))
            {
                subscribedViewModel.SelectedBrowserItem = item;
                subscribedViewModel.SelectedBrowserItems = new List<CatalogDocument> { item };
            }
            return;
        }

        e.Handled = true;
    }

    private void OnBrowserGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
        }
    }

    private void OnBrowserPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
        }
    }

    private void OnBrowserListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.NavigateToFolderCommand.Execute(null);
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

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (isSessionStateLoaded || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var options = LoadAppOptions();
        if (isWindowPlacementDeferred)
        {
            // Screen geometry was not queryable before the first show; apply the placement now.
            ApplyWindowBounds(options);
        }

        vm.ApplySessionState(
            options.Browser.ViewMode,
            options.Browser.SortMode,
            options.IsStatusBarVisible);
        vm.ApplyDetailsColumnSettings(options.Browser.DetailsColumns);
        vm.SetStatusVariants(statusVariantRepository.GetOrdered());
        ApplyLanguage(options.Language);
        if (!string.IsNullOrWhiteSpace(options.LastOpenedCatalogPath) && File.Exists(options.LastOpenedCatalogPath))
        {
            EnsureFileFormatProvidersLoaded();
            try
            {
                await TryLoadCatalogIntoViewModelAsync(options.LastOpenedCatalogPath);
            }
            catch (Exception exception)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalog, exception.Message);
            }
        }

        if (Application.Current is App app)
        {
            await app.BackgroundServicesStarted;
        }

        RefreshMcpStatusIndicator();

        isSessionStateLoaded = true;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (isCompletingConfirmedClose)
        {
            return;
        }

        if (catalogLoadTask is { IsCompleted: false } loading)
        {
            // Keep the window responsive, and let the database operation finish
            // before shutdown disposes its repositories.
            e.Cancel = true;
            try { await loading; }
            catch (Exception) { /* The open/startup handler displays the failure. */ }
            Close();
            return;
        }

        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.IsDirtyDocument)
        {
            e.Cancel = true;
            var decision = await ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel)
            {
                return;
            }

            if (decision == UnsavedChangesDecision.Save && !await SaveCatalogAsync(vm))
            {
                return;
            }

            SaveUiState(vm);
            isCompletingConfirmedClose = true;
            Close();
            return;
        }

        SaveUiState(vm);
    }

    private async void OnAddToListRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await ShowAddToListDialogAsync(vm);
        }
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        var supported = TryResolveDrop(e, out _);
        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = supported;
        e.Handled = true;
    }

    private void OnWindowDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        dragDrives = null;
        e.Handled = true;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;
        if (!TryResolveDrop(e, out var sources) || DataContext is not MainWindowViewModel vm)
        {
            dragDrives = null;
            return;
        }

        dragDrives = null;
        await ShowAddToListDialogAsync(vm, sources);
    }

    private bool TryResolveDrop(DragEventArgs e, out IReadOnlyList<DroppedSource> sources)
    {
        sources = [];
        if (DataContext is not MainWindowViewModel { IsCatalogInteractionEnabled: true })
        {
            return false;
        }

        var droppedPaths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => new DroppedPath(item.TryGetLocalPath() ?? string.Empty, item is IStorageFolder))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path));
        dragDrives ??= mediaDriveProvider.GetSupportedDrives();
        sources = DropSourceResolver.Resolve(droppedPaths, e.DataTransfer.TryGetText(), dragDrives);
        return sources.Count > 0;
    }

    private async Task ShowAddToListDialogAsync(
        MainWindowViewModel vm,
        IReadOnlyList<DroppedSource>? droppedSources = null)
    {
        var dialogVm = new AddToListDialogViewModel(mediaDriveProvider, locationOptions: vm.GetLocationChoices());
        if (droppedSources is { Count: > 0 })
        {
            dialogVm.ApplyDroppedSources(droppedSources);
        }

        var dialog = new AddToListWindow
        {
            DataContext = dialogVm
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted != true)
        {
            dialogVm.ClearInternetSecrets();
            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Internet)
        {
            var targetParentId = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
                ? null
                : vm.SelectedTreeNode?.Key ?? "library";
            var scanErrorOptions = LoadAppOptions();
            var scanTraversal = BuildScanTraversalOptions(ScanSourceProfile.Network);
            var scanOptions = dialogVm.CreateInternetScanOptions(
                targetParentId,
                scanTraversal,
                includeExtendedInfo: scanErrorOptions.IncludeExtendedInfo);
            dialogVm.ClearInternetSecrets();

            var scanner = new RemoteCatalogScanner();
            var progressVm = new AddingProgressDialogViewModel
            {
                CanCancel = true,
                SourceKind = AddingProgressSourceKind.Internet
            };
            var progressDialog = new AddingProgressWindow
            {
                DataContext = progressVm
            };
            progressDialog.Show(this);

            IReadOnlyList<ScanIssue> scanIssues = [];
            var scanCompleted = false;
            try
            {
                var scanResult = await Task.Run(() => scanner.ScanDetailed(scanOptions, progressVm, progressVm.CancellationToken));
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                progressVm.ReportScanStatistics(scanResult.Statistics);
                scanIssues = progressVm.GetScanIssues();
                if (scanResult.Statistics.Failed > 0 && !scanErrorOptions.ShowScanErrorsAfterScan)
                {
                    vm.StatusText = LocalizationService.Current.Format(TextKeys.ScanFailedItems, scanResult.Statistics.Failed);
                    progressVm.ReportError(vm.StatusText);
                    return;
                }
                var scannedDocuments = scanResult.Documents;

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), stagePercentage: 100, overallPercentage: 80);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications));
                await Task.Delay(50, progressVm.CancellationToken);

                progressVm.ReportStage("Importing documents...", stagePercentage: null, overallPercentage: 90);
                progressVm.ReportInfo($"Importing {scannedDocuments.Count} catalog documents into library.");
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                await vm.AddImportedDocumentsAsync(scannedDocuments, progressVm.CancellationToken, progressVm,
                    scanTraversal);
                progressVm.CanCancel = false;

                if (scanErrorOptions.SaveScanErrorsToComments)
                {
                    vm.ApplyScanIssuesToComments(progressVm.GetScanIssues());
                }

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.UpdatingIndexes), stagePercentage: 100, overallPercentage: 100);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
                await Task.Delay(50);

                vm.StatusText = LocalizationService.Current.Get(TextKeys.Done);
                scanCompleted = true;
            }
            catch (OperationCanceledException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.ScanCanceled);
                progressVm.ReportInfo(vm.StatusText);
            }
            catch (Exception ex)
            {
                var message = SecretSanitizer.Sanitize(ex.Message);
                progressVm.ReportError(message);
                vm.StatusText = message;
            }
            finally
            {
                progressVm.FinishCancellation();
                progressDialog.Close();
            }

            if (scanCompleted)
            {
                ApplyScanCompletionEffects(scanErrorOptions, mediaSource: null);
            }

            if (scanErrorOptions.ShowScanErrorsAfterScan && scanIssues.Count > 0)
            {
                await new ScanIssuesWindow(scanIssues).ShowDialog(this);
            }

            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.TitleOnly)
        {
            var targetParentId = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
                ? null
                : vm.SelectedTreeNode?.Key ?? "library";
            try
            {
                vm.AddTitleOnlyMedia(dialogVm.MediaName, targetParentId, dialogVm.SelectedLocationId);
            }
            catch (Exception ex)
            {
                vm.StatusText = ex.Message;
            }

            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Media)
        {
            var targetParentId = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
                ? null
                : vm.SelectedTreeNode?.Key ?? "library";

            var rootType = CatalogDocumentType.Media;

            var selectedDrivePath = dialogVm.SelectedDrive?.RootDirectory ?? dialogVm.SelectedDrive?.Name ?? string.Empty;

            var scanErrorOptions = LoadAppOptions();
            var scanTraversal = BuildScanTraversalOptions(ScanSourceClassifier.FromDrive(dialogVm.SelectedDrive));
            var scanOptions = new MediaScanOptions(
                DrivePathOrName: selectedDrivePath,
                MediaName: string.IsNullOrWhiteSpace(dialogVm.MediaName) ? dialogVm.SelectedDrive?.DisplayName : dialogVm.MediaName,
                TargetParentId: targetParentId,
                RootType: rootType,
                IncludeMediaInfo: dialogVm.IncludeMediaInfo,
                IncludeExtendedInfo: scanErrorOptions.IncludeExtendedInfo,
                IncludeSubfolders: dialogVm.IncludeSubfolders,
                IncludeFiles: dialogVm.IncludeFiles,
                Traversal: scanTraversal,
                LocationId: dialogVm.SelectedLocationId);

            var scanner = new MediaCatalogScanner(mediaDriveProvider,
                volumeMetadata: ServiceProvider.Resolve<VolumeExtensionManager>());
            var progressVm = new AddingProgressDialogViewModel
            {
                CanCancel = true,
                SourceKind = AddingProgressSourceKind.Media
            };
            var progressDialog = new AddingProgressWindow
            {
                DataContext = progressVm
            };
            progressDialog.Show(this);

            IReadOnlyList<ScanIssue> scanIssues = [];
            var scanCompleted = false;
            try
            {
                var scanResult = await Task.Run(() => scanner.ScanDetailed(scanOptions, progressVm, progressVm.CancellationToken));
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                progressVm.ReportScanStatistics(scanResult.Statistics);
                scanIssues = progressVm.GetScanIssues();
                if (scanResult.Statistics.Failed > 0 && !scanErrorOptions.ShowScanErrorsAfterScan)
                {
                    vm.StatusText = LocalizationService.Current.Format(TextKeys.ScanFailedItems, scanResult.Statistics.Failed);
                    progressVm.ReportError(vm.StatusText);
                    return;
                }
                var scannedDocuments = scanResult.Documents;

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), stagePercentage: 100, overallPercentage: 60);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications));
                await Task.Delay(50, progressVm.CancellationToken);

                progressVm.ReportStage("Importing documents...", stagePercentage: null, overallPercentage: 80);
                progressVm.ReportInfo($"Importing {scannedDocuments.Count} catalog documents into library.");
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                await vm.AddImportedDocumentsAsync(scannedDocuments, progressVm.CancellationToken, progressVm,
                    scanTraversal);
                progressVm.CanCancel = false;

                if (scanErrorOptions.SaveScanErrorsToComments)
                {
                    vm.ApplyScanIssuesToComments(progressVm.GetScanIssues());
                }

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.UpdatingIndexes), stagePercentage: 100, overallPercentage: 100);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
                await Task.Delay(50);

                vm.StatusText = LocalizationService.Current.Get(TextKeys.Done);
                scanCompleted = true;
            }
            catch (OperationCanceledException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.ScanCanceled);
                progressVm.ReportInfo(vm.StatusText);
            }
            catch (Exception ex)
            {
                progressVm.ReportError($"Failed to scan/add media: {ex.Message}", ex);
                vm.StatusText = ex.Message;
            }
            finally
            {
                progressVm.FinishCancellation();
                progressDialog.Close();
            }

            if (scanCompleted)
            {
                ApplyScanCompletionEffects(scanErrorOptions, selectedDrivePath);
            }

            if (scanErrorOptions.ShowScanErrorsAfterScan && scanIssues.Count > 0)
            {
                await new ScanIssuesWindow(scanIssues).ShowDialog(this);
            }
            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Folder)
        {
            var targetParentId = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
                ? null
                : vm.SelectedTreeNode?.Key ?? "library";

            var rootType = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
                ? CatalogDocumentType.Media
                : CatalogDocumentType.Folder;

            var scanErrorOptions = LoadAppOptions();
            var scanTraversal = BuildScanTraversalOptions(ScanSourceClassifier.FromPath(dialogVm.SourceValue));
            var scanOptions = new FolderScanOptions(
                FolderPath: dialogVm.SourceValue,
                MediaName: string.IsNullOrWhiteSpace(dialogVm.MediaName) ? null : dialogVm.MediaName,
                TargetParentId: targetParentId,
                RootType: rootType,
                IncludeSubfolders: dialogVm.IncludeSubfolders,
                IncludeFiles: dialogVm.IncludeFiles,
                IncludeExtendedInfo: scanErrorOptions.IncludeExtendedInfo,
                Traversal: scanTraversal);

            var scanner = new FolderCatalogScanner();
            var progressVm = new AddingProgressDialogViewModel
            {
                CanCancel = true,
                SourceKind = AddingProgressSourceKind.Folder
            };
            var progressDialog = new AddingProgressWindow
            {
                DataContext = progressVm
            };
            progressDialog.Show(this);

            IReadOnlyList<ScanIssue> scanIssues = [];
            var scanCompleted = false;
            try
            {
                var scanResult = await Task.Run(() => scanner.ScanDetailed(scanOptions, progressVm, progressVm.CancellationToken));
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                progressVm.ReportScanStatistics(scanResult.Statistics);
                scanIssues = progressVm.GetScanIssues();
                if (scanResult.Statistics.Failed > 0 && !scanErrorOptions.ShowScanErrorsAfterScan)
                {
                    vm.StatusText = LocalizationService.Current.Format(TextKeys.ScanFailedItems, scanResult.Statistics.Failed);
                    progressVm.ReportError(vm.StatusText);
                    return;
                }
                var scannedDocuments = scanResult.Documents;

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), stagePercentage: 100, overallPercentage: 60);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications));
                await Task.Delay(50, progressVm.CancellationToken);

                progressVm.ReportStage("Importing documents...", stagePercentage: null, overallPercentage: 80);
                progressVm.ReportInfo($"Importing {scannedDocuments.Count} catalog documents into library.");
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                await vm.AddImportedDocumentsAsync(scannedDocuments, progressVm.CancellationToken, progressVm,
                    scanTraversal);
                progressVm.CanCancel = false;

                if (scanErrorOptions.SaveScanErrorsToComments)
                {
                    vm.ApplyScanIssuesToComments(progressVm.GetScanIssues());
                }

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.UpdatingIndexes), stagePercentage: 100, overallPercentage: 100);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
                await Task.Delay(50);

                vm.StatusText = LocalizationService.Current.Get(TextKeys.Done);
                scanCompleted = true;
            }
            catch (OperationCanceledException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.ScanCanceled);
                progressVm.ReportInfo(vm.StatusText);
            }
            catch (Exception ex)
            {
                progressVm.ReportError($"Failed to scan/add folder: {ex.Message}", ex);
                vm.StatusText = ex.Message;
            }
            finally
            {
                progressVm.FinishCancellation();
                progressDialog.Close();
            }

            if (scanCompleted)
            {
                ApplyScanCompletionEffects(scanErrorOptions, mediaSource: null);
            }

            if (scanErrorOptions.ShowScanErrorsAfterScan && scanIssues.Count > 0)
            {
                await new ScanIssuesWindow(scanIssues).ShowDialog(this);
            }
            return;
        }

        await ShowAddProgressAsync(dialogVm);

        vm.AddImportedItem(ResolveImportedName(dialogVm));
    }

    private async void OnNewCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.IsDirtyDocument)
        {
            var decision = await ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel)
            {
                return;
            }

            if (decision == UnsavedChangesDecision.Save && !await SaveCatalogAsync(vm))
            {
                return;
            }
        }

        // The new catalog runs on the shared working database, so the previous catalog's background sync must
        // stop before that database is cleared and handed to the new view model.
        var resumeBackgroundSync = vm.IsBackgroundMediaSyncEnabled;
        vm.IsBackgroundMediaSyncEnabled = false;
        MainWindowViewModel? newCatalog = null;
        try
        {
            newCatalog = MainWindowViewModel.CreateNewCatalog(repositoryManager);
            newCatalog.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
            newCatalog.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                ServiceProvider.Resolve<VolumeExtensionManager>(),
                ServiceProvider.Resolve<IndexWorkItemSequence>(),
                ServiceProvider.Resolve<IAudioCdMetadataProvider>());
            newCatalog.RefreshDescriptionServices(ServiceProvider.Resolve<DescriptionExtensionManager>());
            newCatalog.ApplySessionState(vm.CurrentViewMode, vm.CurrentSortMode, vm.IsStatusBarVisible);
            newCatalog.ApplyDetailsColumnSettings(vm.GetDetailsColumnSettings());
        }
        catch (Exception ex)
        {
            newCatalog?.Dispose();
            vm.IsBackgroundMediaSyncEnabled = resumeBackgroundSync;
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToCreateNewCatalog,
                ex.GetBaseException().Message);
            return;
        }

        DataContext = newCatalog;
    }

    private async void OnOpenCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        EnsureFileFormatProvidersLoaded();

        if (vm.IsDirtyDocument)
        {
            var decision = await ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel)
            {
                return;
            }

            if (decision == UnsavedChangesDecision.Save && !await SaveCatalogAsync(vm))
            {
                return;
            }
        }

        var fileTypeChoices = fileFormatManager.GetOpenFilters()
            .ToFilePickerTypes(
                allSupportedFilesLabel: PickerLocalizer["AllSupportedFiles"].Value,
                allFilesLabel: PickerLocalizer["AllFiles"].Value);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Current.Get(TextKeys.OpenCatalog),
            AllowMultiple = false,
            FileTypeFilter = fileTypeChoices
        });

        var localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        try
        {
            await TryLoadCatalogIntoViewModelAsync(localPath);
        }
        catch (UnsupportedFileFormatException)
        {
            var extension = Path.GetExtension(localPath);
            var readableExtensions = fileFormatManager.GetReadableFormats()
                .SelectMany(static format => format.Extensions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var supportedText = readableExtensions.Length == 0
                ? LocalizationService.Current.Get(TextKeys.None)
                : string.Join(", ", readableExtensions);

            vm.StatusText = string.IsNullOrWhiteSpace(extension)
                ? LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogUnsupportedFileFormatReadableExtensions, supportedText)
                : LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogNoReadableHandlerMappedForReadableExtensions, extension, supportedText);
        }
        catch (FileFormatHandlerResolutionException)
        {
            vm.StatusText = LocalizationService.Current.Get(TextKeys.FailedToOpenCatalogFileFormatHandlerIsUnavailableCheckPluginSettings);
        }
        catch (FileFormatNotReadableException ex)
        {
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogPluginCannotReadThisFormat, ex.Message);
        }
        catch (FileFormatReadFailedException ex)
        {
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogPluginReadError, ex.Message);
        }
        catch (Exception ex)
        {
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalog, ex.Message);
        }
    }

    private async void OnSaveCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await SaveCatalogAsync(vm);
        }
    }

    private async void OnSaveCatalogAsRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await SaveCatalogAsync(vm, saveAs: true);
        }
    }

    private async Task<bool> SaveCatalogAsync(MainWindowViewModel vm, bool saveAs = false)
    {
        var targetPath = saveAs ? null : vm.CurrentCatalogPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var fileTypeChoices = fileFormatManager.GetSaveFilters()
                .ToFilePickerTypes(
                    allSupportedFilesLabel: null,
                    allFilesLabel: saveAs ? PickerLocalizer["AllFiles"].Value : null);
            var defaultExtension = fileFormatManager.GetSaveFormats().Any(format => format.FormatId == "skycd-json")
                ? "json"
                : fileFormatManager.GetPreferredSaveExtension();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.Current.Get(saveAs ? TextKeys.SaveCatalogAs : TextKeys.SaveCatalog),
                SuggestedFileName = $"catalog.{defaultExtension}",
                DefaultExtension = defaultExtension,
                FileTypeChoices = fileTypeChoices
            });

            targetPath = file?.TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        try
        {
            var scopeItems = CatalogScopeDataService.Collect(
                virtualCollectionRepository.GetOrdered(),
                statusVariantRepository.GetOrdered(),
                scanExclusionRuleRepository.GetOrdered(),
                aiScanExclusionRepository.GetOrdered(),
                pluginManager.GetCapabilities<ICatalogAuxiliaryDataProvider>());
            var saved = await new CatalogSaveService(fileFormatManager).SaveAsync(
                targetPath,
                vm.GetAllCatalogEntries(),
                scopeItems,
                async preview =>
                {
                    var dialog = new SaveDataLossWindow(preview.FormatDisplayName,
                        CatalogDataLossText.BuildLines(preview.Items));
                    return await dialog.ShowDialog<bool>(this);
                });
            if (!saved)
            {
                return false;
            }

            if (saveAs) vm.CompleteSaveCatalogAs(targetPath);
            else vm.CompleteSaveCatalog(targetPath);
            return true;
        }
        catch (Exception ex)
        {
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToSaveCatalog, ex.Message);
            return false;
        }
    }

    private async void OnPropertiesRequested(object? sender, PropertiesDialogRequestedEventArgs e)
    {
        e.Dialog.ApplyCustomPropertyDefaults(LoadAppOptions().CustomProperties);
        var dialog = new PropertiesWindow
        {
            DataContext = e.Dialog
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        e.Complete(accepted == true, e.Dialog.Comments);
    }

    private async void OnTagsRequested(object? sender, TagsDialogRequestedEventArgs e)
    {
        var dialog = new TagsWindow
        {
            DataContext = e.Dialog
        };
        await dialog.ShowDialog(this);
    }

    private async void OnOptionsRequested(object? sender, OptionsDialogRequestedEventArgs e)
    {
        var options = LoadAppOptions();
        var pluginPath = options.PluginPath;

        e.Dialog.PluginPath = pluginPath;
        e.Dialog.IsMcpServerEnabled = options.IsMcpServerEnabled;
        e.Dialog.McpPort = options.McpPort;
        e.Dialog.IsMcpStatusIconVisible = options.IsMcpStatusIconVisible;
        e.Dialog.SetStatusVariants(statusVariantRepository.GetOrdered());
        e.Dialog.SetCustomProperties(options.CustomProperties);
        e.Dialog.SetDetailsColumns(DataContext is MainWindowViewModel mainViewModel
            ? mainViewModel.GetDetailsColumnSettings()
            : options.Browser.DetailsColumns);
        LoadScanOptionsIntoDialog(e.Dialog, options);
        e.Dialog.SetMediaEjectionSupported(mediaEjector.IsPlatformSupported);
        e.Dialog.SetAiDescriptionAvailable(pluginManager.GetCapabilities<IGeneratedDescriptionCapability>().Any());
        e.Dialog.SetAiDescriptionImplementations(
            ServiceProvider.Resolve<DescriptionExtensionManager>().Implementations,
            options.AiDescriptionImplementation);
        e.Dialog.SetAiScanExclusions(aiScanExclusionRepository.GetOrdered());
        e.Dialog.AiScanMaxFileSizeMegabytes = ToAiScanMaxFileSizeMegabytes(options.AiScanMaxFileSizeBytes);
        // Discovery must never block opening Options; an unreachable bridge keeps the stored selection intact.
        e.Dialog.InitializeMcpTools(options.ExposedMcpTools);
        try
        {
            var mcpTools = Application.Current is App app ? await app.GetMcpToolsAsync() : [];
            e.Dialog.SetMcpTools(mcpTools.Select(tool => (tool.Name, tool.CommandPath)), options.ExposedMcpTools);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Leave the page empty; saving preserves the existing selection.
        }
        if (DataContext is MainWindowViewModel toolbarViewModel)
        {
            e.Dialog.SetToolbarCustomization(toolbarViewModel.ToolbarCommands(), toolbarViewModel.ToolbarEntries);
        }
        if (DataContext is MainWindowViewModel locationsViewModel)
        {
            e.Dialog.SetLocations(locationsViewModel.GetLocationSnapshots());
        }
        e.Dialog.SetPluginMarketplace(pluginMarketplaceService);
        e.Dialog.SetPluginUninstaller(pluginUninstaller);
        e.Dialog.BrowsePluginPathRequested += OnBrowsePluginPathRequested;
        e.Dialog.RefreshPluginsRequested += OnRefreshPluginsRequested;
        e.Dialog.ResetStatusVariantsRequested += OnResetStatusVariantsRequested;
        e.Dialog.ResetScanExclusionRulesRequested += OnResetScanExclusionRulesRequested;
        e.Dialog.ResetAiScanExclusionsRequested += OnResetAiScanExclusionsRequested;
        e.Dialog.ToolbarLayoutApplied += OnOptionsToolbarLayoutApplied;
        RefreshPlugins(e.Dialog);
        e.Dialog.RestoreSettingCategory(options.OptionsCategoryId, options.OptionsTabIndex);
        if (!string.IsNullOrWhiteSpace(e.PreselectSettingCategory))
        {
            e.Dialog.SettingsSearchText = string.Empty;
            e.Dialog.SelectedSettingCategory = e.Dialog.SettingCategories.FirstOrDefault(category =>
                category.BuiltInIndex >= 0 && category.Title == e.PreselectSettingCategory);
        }

        var dialog = new OptionsWindow
        {
            DataContext = e.Dialog,
            AcpSettingsCapability = pluginManager.GetCapabilities<IAcpAgentSettingsCapability>().FirstOrDefault(),
            AiProviderSettingsCapability = pluginManager.GetCapabilities<IAiProviderSettingsCapability>().FirstOrDefault()
        };

        bool? accepted = false;
        try
        {
            accepted = await dialog.ShowDialog<bool?>(this);
        }
        finally
        {
            e.Dialog.CompleteLanguageSelection(accepted == true);
        }
        if (accepted == true)
        {
            var pluginStates = e.Dialog.Plugins
                .Select(static plugin => (plugin.Id, plugin.IsEnabled))
                .ToArray();

            options.CustomProperties = e.Dialog.GetCustomProperties();
            options.PluginPath = e.Dialog.PluginPath;
            options.IsMcpServerEnabled = e.Dialog.IsMcpServerEnabled;
            options.McpPort = e.Dialog.McpPort;
            options.IsMcpStatusIconVisible = e.Dialog.IsMcpStatusIconVisible;
            options.ExposedMcpTools = e.Dialog.GetExposedMcpTools()?.ToList();
            options.Language = e.Dialog.SelectedLanguage.CultureCode;
            options.OptionsTabIndex = Math.Max(0, e.Dialog.SelectedSettingCategory?.BuiltInIndex ?? 0);
            options.OptionsCategoryId = e.Dialog.SelectedSettingCategory?.Id;
            options.Browser.DetailsColumns = e.Dialog.GetDetailsColumns().ToList();
            options.AiScanMaxFileSizeBytes = e.Dialog.AiScanMaxFileSizeMegabytes * 1024L * 1024L;
            options.AiDescriptionImplementation = e.Dialog.SelectedAiDescriptionImplementation?.Id
                                                  ?? options.AiDescriptionImplementation;
            if (e.Dialog.GetToolbarLayout() is { } toolbarLayout)
            {
                options.Toolbar = toolbarLayout.ToList();
            }

            SaveScanOptionsFromDialog(e.Dialog, options);
            SaveAppOptions(options);
            statusVariantRepository.ReplaceAll(e.Dialog.GetStatusVariants());
            scanExclusionRuleRepository.ReplaceAll(e.Dialog.GetScanExclusionRules());
            aiScanExclusionRepository.ReplaceAll(e.Dialog.GetAiScanExclusions());
            pluginManager.SavePluginEnabledStates(pluginStates);
            SyncPluginRuntimeState();
            ApplyLanguage(options.Language);
            if (Application.Current is App app)
            {
                await app.ApplyMcpSettingsAsync();
            }
            RefreshMcpStatusIndicator();
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetStatusVariants(statusVariantRepository.GetOrdered());
                vm.ApplyDetailsColumnSettings(options.Browser.DetailsColumns);
                vm.ApplyLocationChanges(e.Dialog.BuildLocationChangeSet());
            }

            // Trigger UI refresh to apply new language
            InvalidateVisual();
        }

        e.Dialog.BrowsePluginPathRequested -= OnBrowsePluginPathRequested;
        e.Dialog.RefreshPluginsRequested -= OnRefreshPluginsRequested;
        e.Dialog.ResetStatusVariantsRequested -= OnResetStatusVariantsRequested;
        e.Dialog.ResetScanExclusionRulesRequested -= OnResetScanExclusionRulesRequested;
        e.Dialog.ResetAiScanExclusionsRequested -= OnResetAiScanExclusionsRequested;
        e.Dialog.ToolbarLayoutApplied -= OnOptionsToolbarLayoutApplied;

        e.Complete(accepted == true, e.Dialog.PluginPath, e.Dialog.SelectedLanguage.Name);
    }

    private void OnOptionsToolbarLayoutApplied(object? sender, ToolbarLayoutAppliedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ApplyToolbarLayout(e.Layout);
        }
    }

    private async void OnAboutRequested(object? sender, EventArgs e)
    {
        var dialogVm = AboutDialogViewModel.CreateFromMainAssembly(typeof(App).Assembly);
        var dialog = new AboutWindow
        {
            DataContext = dialogVm
        };

        await dialog.ShowDialog<bool?>(this);
    }

    private async Task ShowAddProgressAsync(AddToListDialogViewModel addDialog)
    {
        var progressVm = new AddingProgressDialogViewModel
        {
            SourceKind = addDialog.SourceMode switch
            {
                AddToListSourceMode.Media => AddingProgressSourceKind.Media,
                AddToListSourceMode.Folder => AddingProgressSourceKind.Folder,
                AddToListSourceMode.Internet => AddingProgressSourceKind.Internet,
                _ => AddingProgressSourceKind.Generic
            }
        };
        var progressDialog = new AddingProgressWindow
        {
            DataContext = progressVm
        };

        progressDialog.Show(this);
        try
        {
            var steps = BuildAddProgressSteps(addDialog);
            for (int i = 0; i < steps.Count; i++)
            {
                var (text, value) = steps[i];
                var stagePercent = (double)(i + 1) / steps.Count * 100;
                progressVm.ReportStage(text, stagePercentage: stagePercent, overallPercentage: value);
                progressVm.ReportInfo(text);
                await Task.Delay(140);
            }
        }
        finally
        {
            progressDialog.Close();
        }
    }

    private static IReadOnlyList<(string Text, int Value)> BuildAddProgressSteps(AddToListDialogViewModel addDialog)
    {
        return addDialog.SourceMode switch
        {
            AddToListSourceMode.Internet =>
            [
                (LocalizationService.Current.Get(TextKeys.ReadingDirectoryFromRemoteServer), 20),
                (LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), 55),
                (LocalizationService.Current.Get(TextKeys.UpdatingIndexes), 100)
            ],
            AddToListSourceMode.Folder =>
            [
                (LocalizationService.Current.Get(TextKeys.ReadingSourceFolder), 25),
                (LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), 60),
                (LocalizationService.Current.Get(TextKeys.UpdatingIndexes), 100)
            ],
            _ =>
            [
                (LocalizationService.Current.Get(TextKeys.ReadingMediaMetadata), 35),
                (LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), 70),
                (LocalizationService.Current.Get(TextKeys.UpdatingIndexes), 100)
            ]
        };
    }

    private async Task<UnsavedChangesDecision> ShowUnsavedChangesPromptAsync()
    {
        var dialog = new UnsavedChangesWindow();
        var result = await dialog.ShowDialog<UnsavedChangesDecision?>(this);
        return result ?? UnsavedChangesDecision.Cancel;
    }

    private void SaveUiState(MainWindowViewModel vm)
    {
        var options = LoadAppOptions();

        // Don't save window position if window is minimized
        if (WindowState == WindowState.Normal)
        {
            options.Window.Left = Position.X;
            options.Window.Top = Position.Y;
            options.Window.Width = Width;
            options.Window.Height = Height;
            options.Window.State = WindowState.Normal;
        }
        else if (WindowState == WindowState.Maximized)
        {
            options.Window.State = WindowState.Maximized;
        }

        if (TreePaneColumn.Width.IsAbsolute)
        {
            options.Window.TreePaneWidth = TreePaneColumn.Width.Value;
        }

        options.IsStatusBarVisible = vm.IsStatusBarVisible;
        options.Browser.ViewMode = vm.CurrentViewMode;
        options.Browser.SortMode = vm.CurrentSortMode;
        options.Browser.DetailsColumns = vm.GetDetailsColumnSettings().ToList();
        if (!string.IsNullOrWhiteSpace(vm.CurrentCatalogPath))
        {
            options.LastOpenedCatalogPath = vm.CurrentCatalogPath;
        }
        SaveAppOptions(options);
    }

    private Task TryLoadCatalogIntoViewModelAsync(string localPath)
    {
        if (catalogLoadTask is { IsCompleted: false }) return catalogLoadTask;
        return catalogLoadTask = LoadCatalogAsync(localPath);
    }

    private async Task LoadCatalogAsync(string localPath)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.BeginCatalogLoad();
        var progress = new Progress<int>(vm.ReportCatalogLoadProgress);
        MainWindowViewModel? reloadedViewModel = null;
        IReadOnlyList<string> importWarnings = [];
        // Import hooks reach plugin storage through the open-catalog state, so the repository and path must
        // be visible before the load callback runs. Update them on the UI thread to keep plugin event
        // handlers, which refresh decorations, off the worker thread.
        var previousCatalogPath = openCatalogState.Path;
        openCatalogState.Path = localPath;
        openCatalogState.Attach(catalogRepository);
        var loaded = false;
        try
        {
            reloadedViewModel = await new CatalogLoadService(fileFormatManager).LoadAsync(localPath, (payload, importProgress) =>
            {
                var entries = ExtractCatalogEntries(payload);
                catalogRepository.Collection.Database.InBatch(() =>
                {
                    ReplaceCatalogContent(entries, importProgress);
                });

                // The plugin sees the entries after they are written, so its own storage and warnings stay
                // a plugin concern; the host only folds the warnings into the status line.
                var byId = entries
                    .GroupBy(static entry => entry.Id, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
                var imported = entries.Select(entry => CatalogApiAdapter.Project(entry, byId)).ToArray();
                importWarnings = ServiceProvider.Resolve<CatalogImportExtensionManager>()
                    .RunAsync(imported, TimeSpan.FromSeconds(30))
                    .GetAwaiter()
                    .GetResult()
                    .Warnings;
                importProgress.Report(90);
                return new MainWindowViewModel(repositoryManager);
            }, progress);
            reloadedViewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
            reloadedViewModel.RefreshCatalogFeatureServices(ServiceProvider.Resolve<CatalogFeatureExtensionManager>());
            reloadedViewModel.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                ServiceProvider.Resolve<VolumeExtensionManager>(),
                ServiceProvider.Resolve<IndexWorkItemSequence>(),
                ServiceProvider.Resolve<IAudioCdMetadataProvider>());
            var options = LoadAppOptions();
            reloadedViewModel.ApplySessionState(options.Browser.ViewMode, options.Browser.SortMode, options.IsStatusBarVisible);
            reloadedViewModel.ApplyDetailsColumnSettings(options.Browser.DetailsColumns);
            reloadedViewModel.CurrentCatalogPath = localPath;
            options.LastOpenedCatalogPath = localPath;
            SaveAppOptions(options);
            reloadedViewModel.CompleteOpenCatalog();
            if (importWarnings.Count > 0)
            {
                reloadedViewModel.StatusText = LocalizationService.Current.Format(UiKeys.PluginImportWarnings, string.Join("; ", importWarnings));
            }
            reloadedViewModel.AttachOpenCatalogState(openCatalogState);
            DataContext = reloadedViewModel;
            reloadedViewModel = null; // Ownership passes to the window.
            loaded = true;
        }
        finally
        {
            if (!loaded)
            {
                openCatalogState.Path = previousCatalogPath;
            }

            reloadedViewModel?.Dispose();
            vm.EndCatalogLoad();
        }
    }

    private AppOptionsDocument LoadAppOptions()
    {
        return appOptionsRepository.GetOrCreateAppOptions();
    }

    private ScanTraversalOptions BuildScanTraversalOptions(ScanSourceProfile? sourceProfile = null)
    {
        return ScanSettingsApplicator.BuildTraversalOptions(
            LoadAppOptions(),
            scanExclusionRuleRepository.GetOrdered(),
            sourceProfile);
    }

    private void SaveAppOptions(AppOptionsDocument options)
    {
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
    }

    private void LoadScanOptionsIntoDialog(OptionsDialogViewModel dialog, AppOptionsDocument options)
    {
        dialog.SetScanOptions(new ScanSettingsSnapshot(
            options.SkipSymbolicLinks,
            options.HiddenSystemFilePolicy,
            options.SkipRecycleBins,
            options.SkipWellKnownSystemFolders,
            options.SkipEmptyFolders,
            options.SkipOnlineOnlyCloudFiles,
            options.IncludeExtendedInfo,
            options.ShowScanErrorsAfterScan,
            options.SaveScanErrorsToComments,
            options.EjectMediaAfterScan,
            options.ActivateMainWindowAfterScan,
            options.PlaySoundAfterScan,
            options.ScanConcurrencyMode,
            options.ScanConcurrencyManualWorkers,
            options.ScanConcurrencyCpuSharePercent,
            options.ScanConcurrencyLoadShrinkPercent,
            options.ScanConcurrencyLoadSuspendPercent));
        dialog.SetScanExclusionRules(scanExclusionRuleRepository.GetOrdered());
    }

    private void SaveScanOptionsFromDialog(OptionsDialogViewModel dialog, AppOptionsDocument options)
    {
        var scanSettings = dialog.GetScanOptions();
        options.SkipSymbolicLinks = scanSettings.SkipSymbolicLinks;
        options.HiddenSystemFilePolicy = scanSettings.HiddenSystemFilePolicy;
        options.SkipRecycleBins = scanSettings.SkipRecycleBins;
        options.SkipWellKnownSystemFolders = scanSettings.SkipWellKnownSystemFolders;
        options.SkipEmptyFolders = scanSettings.SkipEmptyFolders;
        options.SkipOnlineOnlyCloudFiles = scanSettings.SkipOnlineOnlyCloudFiles;
        options.IncludeExtendedInfo = scanSettings.IncludeExtendedInfo;
        options.ShowScanErrorsAfterScan = scanSettings.ShowScanErrorsAfterScan;
        options.SaveScanErrorsToComments = scanSettings.SaveScanErrorsToComments;
        options.EjectMediaAfterScan = scanSettings.EjectMediaAfterScan;
        options.ActivateMainWindowAfterScan = scanSettings.ActivateMainWindowAfterScan;
        options.PlaySoundAfterScan = scanSettings.PlaySoundAfterScan;
        options.ScanConcurrencyMode = scanSettings.ScanConcurrencyMode;
        options.ScanConcurrencyManualWorkers = scanSettings.ScanConcurrencyManualWorkers;
        options.ScanConcurrencyCpuSharePercent = scanSettings.ScanConcurrencyCpuSharePercent;
        options.ScanConcurrencyLoadShrinkPercent = scanSettings.ScanConcurrencyLoadShrinkPercent;
        options.ScanConcurrencyLoadSuspendPercent = scanSettings.ScanConcurrencyLoadSuspendPercent;
    }

    /// <summary>
    /// Runs the completion actions the user enabled (eject, activate, sound) after a scan and its import
    /// finished successfully. Failures are logged inside the service and never disturb the finished scan.
    /// </summary>
    private void ApplyScanCompletionEffects(AppOptionsDocument options, string? mediaSource)
    {
        var settings = ScanCompletionSettings.FromOptions(options);
        if (!settings.HasAnyAction)
        {
            return;
        }

        scanCompletionService.Complete(settings, mediaSource);
    }

    private void ApplyWindowBounds(AppOptionsDocument options)
    {
        var layout = MainWindowBoundsResolver.Resolve(options.Window, GetScreenWorkingAreas());

        Width = layout.Width;
        Height = layout.Height;

        if (layout.Position is { } position)
        {
            Position = position;
        }

        if (layout.TreePaneWidth is { } treePaneWidth)
        {
            TreePaneColumn.Width = new GridLength(treePaneWidth, GridUnitType.Pixel);
        }

        WindowState = layout.State;
        isWindowPlacementDeferred = layout.Position is null;
    }

    private IReadOnlyList<ScreenWorkingArea> GetScreenWorkingAreas()
    {
        try
        {
            return Screens.All
                .Select(static screen => new ScreenWorkingArea(screen.WorkingArea, screen.Scaling, screen.IsPrimary))
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            // Screens may not be queryable on some backends until the window is shown.
            return [];
        }
    }

    private static BrowserViewMode ParseBrowserViewMode(string? value)
    {
        return Enum.TryParse<BrowserViewMode>(value, true, out var mode)
            ? mode
            : BrowserViewMode.Details;
    }

    private async void OnBrowsePluginPathRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current.Get(TextKeys.SelectPlugInDirectory),
            AllowMultiple = false
        });

        if (picked.Count == 0)
        {
            return;
        }

        var pickedPath = picked[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        dialogVm.PluginPath = pickedPath;
        RefreshPlugins(dialogVm);
    }

    private void OnRefreshPluginsRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        RefreshPlugins(dialogVm);
    }

    private async void OnResetStatusVariantsRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var confirmed = await ShowResetStatusesPromptAsync();
        if (!confirmed)
        {
            return;
        }

        statusVariantRepository.ReplaceAll(statusVariantRepository.CreateDefaultEntries());
        dialogVm.SetStatusVariants(statusVariantRepository.GetOrdered());
    }

    private async void OnResetScanExclusionRulesRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var confirmed = await ShowResetScanRulesPromptAsync();
        if (!confirmed)
        {
            return;
        }

        scanExclusionRuleRepository.ReplaceAll(scanExclusionRuleRepository.CreateDefaultEntries());
        dialogVm.SetScanExclusionRules(scanExclusionRuleRepository.GetOrdered());
    }

    private async void OnResetAiScanExclusionsRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var confirmed = await ShowResetMimeExclusionsPromptAsync();
        if (!confirmed)
        {
            return;
        }

        aiScanExclusionRepository.ReplaceAll(aiScanExclusionRepository.CreateDefaultEntries());
        dialogVm.SetAiScanExclusions(aiScanExclusionRepository.GetOrdered());
    }

    private void RefreshPlugins(OptionsDialogViewModel dialogVm)
    {
        try
        {
            RefreshPluginsCore(dialogVm);
        }
        catch (Exception exception)
        {
            // Reachable from async void event handlers, so an escaping plugin failure would crash the app.
            logger.LogWarning(exception, "Refreshing the plugin list failed; the Options dialog remains usable.");
        }
    }

    private void RefreshPluginsCore(OptionsDialogViewModel dialogVm)
    {
        dialogVm.CapturePluginStates();
        pluginManager.Discover(dialogVm.PluginPath, hostVersionProvider.Current);
        var descriptors = pluginManager.GetPluginDescriptors();
        var discoveredById = pluginManager.AvailablePlugins
            .ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase);
        var installStates = pluginMarketplaceService.GetInstallStates();
        var configurationManager = ServiceProvider.Resolve<PluginConfigurationManager>();

        var plugins = descriptors
            .Select(descriptor => CreateOptionsPluginItem(descriptor, discoveredById, installStates))
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        dialogVm.SetPlugins(plugins);
        dialogVm.SetPluginConfigurations(configurationManager, PluginConfigurationTimeout);
    }

    /// <summary>
    /// Combines the persisted descriptor (enabled state, stale plugins) with the freshly discovered
    /// assembly metadata so the Additional Info tab can show links, version, size and update source.
    /// </summary>
    private static OptionsPluginItem CreateOptionsPluginItem(
        PluginDocument descriptor,
        IReadOnlyDictionary<string, DiscoveredPlugin> discoveredById,
        IReadOnlyDictionary<string, PluginInstallState> installStates)
    {
        discoveredById.TryGetValue(descriptor.Id, out var discovered);

        var authorName = discovered?.Author?.Name ?? descriptor.Author?.Name;
        var version = discovered is null ? descriptor.Version : discovered.Version.ToString();
        return new OptionsPluginItem(
            discovered?.Name ?? descriptor.Name,
            string.IsNullOrWhiteSpace(authorName) ? LocalizationService.Current.Get(TextKeys.UnknownAuthor) : authorName,
            $"{descriptor.Id} v{version}",
            isEnabled: descriptor.IsEnabled,
            id: descriptor.Id,
            authorUrl: discovered?.Author?.Url ?? descriptor.Author?.Url,
            description: discovered?.Description ?? descriptor.Description,
            primaryExtension: discovered?.PrimaryExtension ?? descriptor.PrimaryExtension,
            projectUrl: discovered?.ProjectUrl ?? descriptor.ProjectUrl,
            version: version,
            capabilityType: ResolveCapabilityType(discovered),
            sizeBytes: PluginFolderSize.TryComputeBytes(discovered?.AssemblyLocation),
            updateSource: ResolveUpdateSource(descriptor.Id, discovered, installStates));
    }

    private static string? ResolveCapabilityType(DiscoveredPlugin? plugin)
    {
        if (plugin is null || plugin.Capabilities.Count == 0)
        {
            return null;
        }

        return string.Join(", ", plugin.Capabilities
            .Select(static capability => capability.GetType().Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
    }

    private static PluginUpdateSource? ResolveUpdateSource(
        string pluginId,
        DiscoveredPlugin? discovered,
        IReadOnlyDictionary<string, PluginInstallState> installStates)
    {
        if (discovered is null)
        {
            return null;
        }

        return installStates.TryGetValue(pluginId, out var state) &&
               state.Status != PluginInstallOperationStatus.NotInstalled
            ? PluginUpdateSource.Marketplace
            : PluginUpdateSource.PluginFolder;
    }

    private void SyncPluginRuntimeState()
    {
        var options = appOptionsRepository.GetOrCreateAppOptions();
        var resolvedPluginPath = ResolvePluginDiscoveryPath(options.PluginPath);
        options.PluginPath = resolvedPluginPath;
        SaveAppOptions(options);

        pluginManager.Discover(resolvedPluginPath, hostVersionProvider.Current);

        ServiceProvider.ReregisterPluginsService();
        // Re-discovery builds fresh capability instances; without re-injection they would resolve from the
        // new container without the host APIs (catalog repository, dialogs, storage) they depend on.
        (Application.Current as App)?.InitializePluginConsumers(pluginManager);
        ServiceProvider.Resolve<PluginConfigurationManager>().RefreshConsumers();
        fileFormatManager = ServiceProvider.Resolve<FileFormatManager>();
        var descriptionManager = ServiceProvider.Resolve<DescriptionExtensionManager>();
        descriptionManager.SelectImplementation(options.AiDescriptionImplementation);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
            viewModel.RefreshCatalogFeatureServices(ServiceProvider.Resolve<CatalogFeatureExtensionManager>());
            viewModel.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                ServiceProvider.Resolve<VolumeExtensionManager>(),
                ServiceProvider.Resolve<IndexWorkItemSequence>(),
                ServiceProvider.Resolve<IAudioCdMetadataProvider>());
            viewModel.RefreshDescriptionServices(descriptionManager);
            PushFileFormatAvailability();
        }
    }

    private void EnsureFileFormatProvidersLoaded()
    {
        if (fileFormatManager.GetReadableFormats().Count == 0)
        {
            SyncPluginRuntimeState();
            if (fileFormatManager.GetReadableFormats().Count == 0)
            {
                var availableDescriptors = pluginManager.GetAvailablePluginIds<IFileFormatPluginCapability>()
                    .Select(static id => (id, IsEnabled: true))
                    .ToArray();
                if (availableDescriptors.Length > 0)
                {
                    pluginManager.SavePluginEnabledStates(availableDescriptors);
                    SyncPluginRuntimeState();
                }
            }
        }

        // Pushed after the auto-enable fallback so installed-but-disabled plugins are reflected correctly.
        PushFileFormatAvailability();
    }

    private void PushFileFormatAvailability()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetFileFormatAvailability(
                fileFormatManager.GetOpenFormats().Count > 0,
                fileFormatManager.GetSaveFormats().Count > 0);
        }
    }

    private static string ResolvePluginDiscoveryPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        var baseDirPlugins = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Plugins"));
        if (Directory.Exists(baseDirPlugins))
        {
            return baseDirPlugins;
        }

        var current = Directory.GetCurrentDirectory();
        var repoPlugins = Path.GetFullPath(Path.Combine(current, "Plugins"));
        if (Directory.Exists(repoPlugins))
        {
            return repoPlugins;
        }

        return configuredPath ?? string.Empty;
    }

    private static string? ResolveImportedName(AddToListDialogViewModel dialogVm)
    {
        if (!string.IsNullOrWhiteSpace(dialogVm.MediaName))
        {
            return dialogVm.MediaName;
        }

        if (!string.IsNullOrWhiteSpace(dialogVm.SourceValue))
        {
            var value = dialogVm.SourceValue.Trim();
            return Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
    }

    private static T? FindAncestor<T>(Visual? visual) where T : class
    {
        var current = visual;
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static void ApplyLanguage(string? languageName)
    {
        LocalizationService.Current.SetLanguage(languageName);
    }

    private void ReplaceCatalogContent(IReadOnlyList<CatalogDocument> entries, IProgress<int>? progress = null)
    {
        var existingEntries = catalogRepository.GetAll();
        var total = Math.Max(1, existingEntries.Count + entries.Count);
        var processed = 0;
        foreach (var existing in existingEntries)
        {
            using var document = catalogRepository.Collection.GetDocument(existing.Id);
            if (document is not null)
            {
                catalogRepository.Collection.Delete(document);
            }
            progress?.Report(++processed * 90 / total);
        }

        foreach (var entry in entries)
        {
            catalogRepository.Save(entry.Id, entry);
            progress?.Report(++processed * 90 / total);
        }

        // Imported rows may carry source-specific location data (PhysicalLocation, ZdcStorageLocation);
        // canonicalize it into the location model now that the entries are persisted. The raw source
        // keys stay in Properties so export round-trips remain lossless.
        if (CreateLocationService() is { } locationService)
        {
            foreach (var entry in entries)
            {
                locationService.CanonicalizeImportedLocation(entry);
            }
        }
    }

    /// <summary>
    /// Builds a location service on the catalog database for import canonicalization without
    /// changing the window constructor surface.
    /// </summary>
    private SkyCD.Presentation.Locations.CatalogLocationService? CreateLocationService()
    {
        try
        {
            if (repositoryManager.For<LocationDocument>() is not LocationDocumentRepository locationRepository)
            {
                return null;
            }

            return new SkyCD.Presentation.Locations.CatalogLocationService(catalogRepository, locationRepository);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<CatalogDocument> ExtractCatalogEntries(object? payload)
    {
        if (TryExtractRows(payload, out var rows))
        {
            return BuildEntriesFromRows(rows);
        }

        if (TryExtractPathEntries(payload, out var pathEntries))
        {
            return BuildEntriesFromPaths(pathEntries);
        }

        throw new InvalidCatalogPayloadException();
    }

    private static bool TryExtractRows(object? payload, out IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (payload is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("nodes", out var nodes)) json = nodes;
                else if (json.TryGetProperty("items", out var items)) json = items;
            }
            if (json.ValueKind == JsonValueKind.Array)
            {
                rows = json.EnumerateArray().Select(item => item.EnumerateObject().ToDictionary(
                    property => property.Name, property => (object?)property.Value.Clone(), StringComparer.OrdinalIgnoreCase)).ToArray();
                return true;
            }
        }

        if (payload is IEnumerable<Dictionary<string, object?>> typedRows)
        {
            rows = typedRows.Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)).ToArray();
            return true;
        }

        rows = [];
        return false;
    }

    private static bool TryExtractPathEntries(object? payload, out IReadOnlyList<PathEntry> entries)
    {
        entries = [];
        if (payload is null)
        {
            return false;
        }

        var entriesProperty = payload.GetType().GetProperty("Entries", BindingFlags.Instance | BindingFlags.Public);
        if (entriesProperty?.GetValue(payload) is not System.Collections.IEnumerable rawEntries)
        {
            return false;
        }

        var result = new List<PathEntry>();
        foreach (var raw in rawEntries)
        {
            if (raw is null)
            {
                continue;
            }

            var path = raw.GetType().GetProperty("Path", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw) as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var sizeValue = raw.GetType().GetProperty("SizeBytes", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw);
            var size = sizeValue is null ? 0L : Convert.ToInt64(sizeValue, CultureInfo.InvariantCulture);

            var normalizedPath = path.Trim();
            var isHttps = Uri.TryCreate(normalizedPath, UriKind.Absolute, out var uri) &&
                          uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var domain = isHttps ? uri!.Host : null;

            var isDirectory = raw.GetType().GetProperty("IsDirectory", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw) is true;
            var icon = raw.GetType().GetProperty("Icon", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(raw) as string;
            result.Add(new PathEntry(normalizedPath, size, isHttps, domain, isDirectory, icon));
        }

        entries = result;
        return true;
    }

    private static IReadOnlyList<CatalogDocument> BuildEntriesFromRows(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count > 0 && rows.All(row => !string.IsNullOrWhiteSpace(ReadString(row, "fullPath"))))
        {
            return BuildEntriesFromPaths(rows.Select(row => new PathEntry(
                ReadString(row, "fullPath"), ReadLong(row, "size", "sizeBytes"), false, null,
                ReadString(row, "kind").Equals("folder", StringComparison.OrdinalIgnoreCase),
                ReadString(row, "icon", "Icon"))).ToArray());
        }

        var results = new List<CatalogDocument>();
        foreach (var row in rows)
        {
            var id = ReadString(row, "id");
            if (string.IsNullOrWhiteSpace(id)) id = ReadString(row, "nodeId");
            var name = ReadString(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var parentId = ReadString(row, "parentId");
            if (string.IsNullOrWhiteSpace(parentId) || string.Equals(parentId, "-1", StringComparison.Ordinal))
            {
                parentId = null;
            }

            var normalizedType = ReadString(row, "type").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedType)) normalizedType = ReadString(row, "kind").ToLowerInvariant();
            var documentType = normalizedType switch
            {
                "folder" => CatalogDocumentType.Folder,
                "file" => CatalogDocumentType.File,
                "media" => CatalogDocumentType.Media,
                // Collection entries (e.g. Tellico items) are catalog objects, not
                // filesystem files; they map to media rather than falling through.
                "entry" => CatalogDocumentType.Media,
                "networkfolder" => CatalogDocumentType.NetworkFolder,
                "networkresource" => CatalogDocumentType.NetworkResource,
                _ => CatalogDocumentType.Media
            };
            var status = ReadString(row, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                status = ReadString(row, "statusName");
            }

            var icon = ReadString(row, "icon");
            if (string.IsNullOrWhiteSpace(icon))
            {
                icon = ReadString(row, "Icon");
            }

            var properties = new SkyCD.Documents.Collections.PropertiesCollection();
            if (row.TryGetValue("properties", out var rawProps) && rawProps is not null)
            {
                CopyProperties(properties, rawProps);
            }
            else if (row.TryGetValue("Properties", out rawProps) && rawProps is not null)
            {
                CopyProperties(properties, rawProps);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                properties["StatusName"] = status;
            }

            if (!string.IsNullOrWhiteSpace(icon) && !properties.ContainsKey("Icon"))
            {
                properties["Icon"] = icon;
            }

            results.Add(new CatalogDocument
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"doc-{Guid.NewGuid():N}" : id,
                Name = name,
                ParentId = parentId,
                Type = documentType,
                Size = ReadLong(row, "size", "sizeBytes"),
                ChildrenCount = ReadLong(row, "childrenCount"),
                ContentsState = ReadContentsState(row, properties),
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Properties = properties
            });
        }

        return results;
    }

    private static void CopyProperties(SkyCD.Documents.Collections.PropertiesCollection target, object rawProps)
    {
        if (rawProps is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in json.EnumerateObject())
            {
                target[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText()
                };
            }
        }
        else if (rawProps is IDictionary<string, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                if (value is not null)
                {
                    target[key] = value;
                }
            }
        }
    }

    private static IReadOnlyList<CatalogDocument> BuildEntriesFromPaths(IReadOnlyList<PathEntry> pathEntries)
    {
        var entries = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
        var rootId = "library";
        entries[rootId] = new CatalogDocument
        {
            Id = rootId,
            Name = "Library",
            ParentId = null,
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0
        };

        foreach (var pathEntry in pathEntries
                     .OrderBy(static entry => entry.IsHttps ? 0 : 1)
                     .ThenBy(static entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (pathEntry.IsHttps)
            {
                BuildHttpsEntry(entries, rootId, pathEntry);
                continue;
            }

            var parts = pathEntry.Path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var currentParentId = rootId;
            for (var i = 0; i < parts.Length; i++)
            {
                var name = parts[i];
                var isFile = i == parts.Length - 1 && !pathEntry.IsDirectory;
                var isMediaRoot = i == 0 &&
                                  ((name.Length == 2 && char.IsLetter(name[0]) && name[1] == ':') ||
                                   (name.StartsWith('[') && name.EndsWith(']')));
                var id = CreateImportedNodeId(currentParentId, name);
                if (entries.ContainsKey(id))
                {
                    currentParentId = id;
                    continue;
                }

                var entryProperties = new SkyCD.Documents.Collections.PropertiesCollection();
                if (isFile && !string.IsNullOrWhiteSpace(pathEntry.Icon))
                {
                    entryProperties["Icon"] = pathEntry.Icon;
                }

                entries[id] = new CatalogDocument
                {
                    Id = id,
                    Name = name,
                    ParentId = currentParentId,
                    Type = isFile
                        ? CatalogDocumentType.File
                        : isMediaRoot
                            ? CatalogDocumentType.Media
                            : CatalogDocumentType.Folder,
                    Size = isFile ? pathEntry.Size : 0L,
                    ChildrenCount = 0L,
                    Properties = entryProperties
                };

                currentParentId = id;
            }
        }

        return entries.Values.ToArray();
    }

    private static void BuildHttpsEntry(IDictionary<string, CatalogDocument> entries, string rootId, PathEntry pathEntry)
    {
        if (string.IsNullOrWhiteSpace(pathEntry.Domain))
        {
            return;
        }

        var domainNodeId = CreateImportedNodeId(rootId, pathEntry.Domain);
        if (!entries.ContainsKey(domainNodeId))
        {
            entries[domainNodeId] = new CatalogDocument
            {
                Id = domainNodeId,
                Name = pathEntry.Domain,
                ParentId = rootId,
                Type = CatalogDocumentType.NetworkFolder,
                Size = 0L,
                ChildrenCount = 0L
            };
        }

        var resourceNodeId = CreateImportedNodeId(domainNodeId, pathEntry.Path);
        if (!entries.ContainsKey(resourceNodeId))
        {
            var entryProperties = new SkyCD.Documents.Collections.PropertiesCollection();
            if (!string.IsNullOrWhiteSpace(pathEntry.Icon))
            {
                entryProperties["Icon"] = pathEntry.Icon;
            }

            entries[resourceNodeId] = new CatalogDocument
            {
                Id = resourceNodeId,
                Name = pathEntry.Path,
                ParentId = domainNodeId,
                Type = CatalogDocumentType.NetworkResource,
                Size = pathEntry.Size,
                ChildrenCount = 0L,
                Properties = entryProperties
            };
        }
    }

    private static CatalogContentsState ReadContentsState(
        IReadOnlyDictionary<string, object?> row,
        SkyCD.Documents.Collections.PropertiesCollection properties)
    {
        var raw = ReadString(row, "contentsState", "ContentsState");
        if (string.IsNullOrWhiteSpace(raw) &&
            properties.TryGetValue("ContentsState", out var propertyValue))
        {
            raw = propertyValue switch
            {
                JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
                JsonElement json => json.ToString(),
                _ => propertyValue?.ToString() ?? string.Empty
            };
        }

        return Enum.TryParse<CatalogContentsState>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : CatalogContentsState.Known;
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value is not null)
            {
                var str = value is JsonElement { ValueKind: JsonValueKind.Null } ? string.Empty : value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(str))
                {
                    return str;
                }
            }
        }

        return string.Empty;
    }

    private static long ReadLong(IReadOnlyDictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value is long direct)
            {
                return direct;
            }

            if (long.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0L;
    }

    private static string CreateImportedNodeId(string parentId, string name)
    {
        // Catalog paths and URLs can exceed Couchbase's document-ID limit.
        // Hash each parent/name pair so IDs stay bounded at every tree depth.
        var identity = parentId + "/" + name.ToLowerInvariant();
        return "import-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private sealed record PathEntry(string Path, long Size, bool IsHttps, string? Domain, bool IsDirectory, string? Icon = null);

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

    private async Task<bool> ShowResetStatusesPromptAsync()
    {
        var dialog = new ResetStatusesConfirmWindow();
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private async Task<bool> ShowResetScanRulesPromptAsync()
    {
        var dialog = new ResetScanRulesConfirmWindow();
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private async Task<bool> ShowResetMimeExclusionsPromptAsync()
    {
        var dialog = new ResetMimeExclusionsConfirmWindow();
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private static int ToAiScanMaxFileSizeMegabytes(long maxFileSizeBytes)
    {
        const long bytesPerMegabyte = 1024L * 1024L;
        var normalized = maxFileSizeBytes > 0 ? maxFileSizeBytes : AiScanPolicy.DefaultMaxFileSizeBytes;
        return (int)Math.Clamp(normalized / bytesPerMegabyte, 1, int.MaxValue);
    }

}

