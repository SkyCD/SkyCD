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
using DryIoc;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.Localization;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Abstractions;
using Avalonia.Threading;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Core.Versioning;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Lists;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private static readonly IStringLocalizer PickerLocalizer = new PropertyValueLocalizer();
    private readonly RepositoryManager repositoryManager;
    private readonly AppOptionsDocumentRepository appOptionsRepository;
    private readonly CatalogDocumentRepository catalogRepository;
    private readonly StatusVariantDocumentRepository statusVariantRepository;
    private readonly PluginManager pluginManager;
    private readonly HostVersionProvider hostVersionProvider;
    private FileFormatManager fileFormatManager;
    private MainWindowViewModel? subscribedViewModel;
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;
    private Task? catalogLoadTask;
    private readonly SemaphoreSlim pluginErrorDialogGate = new(1, 1);
    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    public MainWindow(
        RepositoryManager repositoryManager,
        PluginManager pluginManager,
        FileFormatManager fileFormatManager)
    {
        this.repositoryManager = repositoryManager;
        appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        statusVariantRepository = (StatusVariantDocumentRepository)repositoryManager.For<StatusVariantDocument>();
        this.pluginManager = pluginManager;
        hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();
        this.fileFormatManager = fileFormatManager;
        InitializeComponent();
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
            subscribedViewModel.OptionsRequested -= OnOptionsRequested;
            subscribedViewModel.PropertiesRequested -= OnPropertiesRequested;
            subscribedViewModel.ExitRequested -= OnExitRequested;
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
            subscribedViewModel.OptionsRequested += OnOptionsRequested;
            subscribedViewModel.PropertiesRequested += OnPropertiesRequested;
            subscribedViewModel.ExitRequested += OnExitRequested;
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            subscribedViewModel.SetStatusVariants(statusVariantRepository.GetOrdered());
            UpdateWindowTitle();
        }
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsDirtyDocument) or nameof(MainWindowViewModel.CurrentCatalogPath))
        {
            UpdateWindowTitle();
        }
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
            subscribedViewModel.SelectedTreeNode = node;
            return;
        }

        e.Handled = true;
    }

    private void OnBrowserContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox listBox || subscribedViewModel is null)
        {
            return;
        }

        if (!e.TryGetPosition(listBox, out var point))
        {
            e.Handled = subscribedViewModel.SelectedBrowserItem is null;
            return;
        }

        var hit = listBox.InputHitTest(point) as Visual;
        var listBoxItem = FindAncestor<ListBoxItem>(hit);
        if (listBoxItem?.DataContext is CatalogDocument item)
        {
            subscribedViewModel.SelectedBrowserItem = item;
            return;
        }

        e.Handled = true;
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
        ApplyWindowBounds(options);
        vm.ApplySessionState(
            options.Browser.ViewMode,
            options.Browser.SortMode,
            options.IsStatusBarVisible);
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

            if (decision == UnsavedChangesDecision.Save)
            {
                vm.SaveCatalogCommand.Execute(null);
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
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var dialogVm = new AddToListDialogViewModel();
        var dialog = new AddToListWindow
        {
            DataContext = dialogVm
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted != true)
        {
            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Internet)
        {
            var loginVm = new LoginDialogViewModel();
            var loginDialog = new LoginWindow
            {
                DataContext = loginVm
            };

            var loginAccepted = await loginDialog.ShowDialog<bool?>(this);
            if (loginAccepted != true)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.LoginCanceled);
                return;
            }
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

            if (decision == UnsavedChangesDecision.Save)
            {
                vm.SaveCatalogCommand.Execute(null);
            }
        }

        vm.CompleteNewCatalog();
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

            if (decision == UnsavedChangesDecision.Save)
            {
                vm.SaveCatalogCommand.Execute(null);
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
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        EnsureFileFormatProvidersLoaded();

        var targetPath = vm.CurrentCatalogPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var fileTypeChoices = fileFormatManager.GetSaveFilters()
                .ToFilePickerTypes(
                    allSupportedFilesLabel: null,
                    allFilesLabel: null);

            var defaultExtension = fileFormatManager.GetPreferredSaveExtension();

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.Current.Get(TextKeys.SaveCatalog),
                SuggestedFileName = $"catalog.{defaultExtension}",
                DefaultExtension = defaultExtension,
                FileTypeChoices = fileTypeChoices
            });

            targetPath = file?.TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        try
        {
            var capability = fileFormatManager.GetInstanceFor(targetPath);
            if (!capability.SupportedFormat.CanWrite)
            {
                throw new FileFormatReadOnlyException(capability.SupportedFormat.FormatId);
            }

            var content = """
                          # SkyCD catalog placeholder
                          # TODO: replace with full catalog serialization pipeline
                          """;
            File.WriteAllText(targetPath, content);
            vm.CompleteSaveCatalog(targetPath);
        }
        catch (Exception ex)
        {
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToSaveCatalog, ex.Message);
        }
    }

    private async void OnSaveCatalogAsRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        EnsureFileFormatProvidersLoaded();

        var fileTypeChoices = fileFormatManager.GetSaveFilters()
            .ToFilePickerTypes(
                allSupportedFilesLabel: null,
                allFilesLabel: PickerLocalizer["AllFiles"].Value);

        var defaultExtension = fileFormatManager.GetPreferredSaveExtension();

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Current.Get(TextKeys.SaveCatalogAs),
            SuggestedFileName = $"catalog.{defaultExtension}",
            DefaultExtension = defaultExtension,
            FileTypeChoices = fileTypeChoices
        });

        var localPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        try
        {
            var capability = fileFormatManager.GetInstanceFor(localPath);
            if (!capability.SupportedFormat.CanWrite)
            {
                throw new FileFormatReadOnlyException(capability.SupportedFormat.FormatId);
            }

            var content = """
                          # SkyCD catalog placeholder
                          # TODO: replace with full catalog serialization pipeline
                          """;
            File.WriteAllText(localPath, content);
            vm.CompleteSaveCatalogAs(localPath);
        }
        catch (Exception ex)
        {
            vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToSaveCatalog, ex.Message);
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
        e.Dialog.SelectedTabIndex = Math.Max(0, options.OptionsTabIndex);
        e.Dialog.BrowsePluginPathRequested += OnBrowsePluginPathRequested;
        e.Dialog.RefreshPluginsRequested += OnRefreshPluginsRequested;
        e.Dialog.ResetStatusVariantsRequested += OnResetStatusVariantsRequested;
        RefreshPlugins(e.Dialog);

        var dialog = new OptionsWindow
        {
            DataContext = e.Dialog
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
            options.Language = e.Dialog.SelectedLanguage.CultureCode;
            options.OptionsTabIndex = Math.Max(0, e.Dialog.SelectedTabIndex);
            SaveAppOptions(options);
            statusVariantRepository.ReplaceAll(e.Dialog.GetStatusVariants());
            pluginManager.SavePluginEnabledStates(pluginStates);
            SyncPluginRuntimeState();
            ApplyLanguage(options.Language);
            (Application.Current as App)?.ApplyMcpSettings();
            RefreshMcpStatusIndicator();
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetStatusVariants(statusVariantRepository.GetOrdered());
            }

            // Trigger UI refresh to apply new language
            InvalidateVisual();
        }

        e.Dialog.BrowsePluginPathRequested -= OnBrowsePluginPathRequested;
        e.Dialog.RefreshPluginsRequested -= OnRefreshPluginsRequested;
        e.Dialog.ResetStatusVariantsRequested -= OnResetStatusVariantsRequested;

        e.Complete(accepted == true, e.Dialog.PluginPath, e.Dialog.SelectedLanguage.Name);
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
        var progressVm = new AddingProgressDialogViewModel();
        var progressDialog = new AddingProgressWindow
        {
            DataContext = progressVm
        };

        progressDialog.Show(this);
        try
        {
            foreach (var (text, value) in BuildAddProgressSteps(addDialog))
            {
                progressVm.OperationText = text;
                progressVm.ProgressValue = value;
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
        try
        {
            reloadedViewModel = await new CatalogLoadService(fileFormatManager).LoadAsync(localPath, (payload, importProgress) =>
            {
                var entries = ExtractCatalogEntries(payload);
                ReplaceCatalogContent(entries, importProgress);
                importProgress.Report(90);
                return new MainWindowViewModel(repositoryManager);
            }, progress);
            reloadedViewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
            var options = LoadAppOptions();
            reloadedViewModel.ApplySessionState(options.Browser.ViewMode, options.Browser.SortMode, options.IsStatusBarVisible);
            reloadedViewModel.CurrentCatalogPath = localPath;
            options.LastOpenedCatalogPath = localPath;
            SaveAppOptions(options);
            reloadedViewModel.CompleteOpenCatalog();
            DataContext = reloadedViewModel;
            reloadedViewModel = null; // Ownership passes to the window.
        }
        finally
        {
            reloadedViewModel?.Dispose();
            vm.EndCatalogLoad();
        }
    }

    private AppOptionsDocument LoadAppOptions()
    {
        return appOptionsRepository.GetOrCreateAppOptions();
    }

    private void SaveAppOptions(AppOptionsDocument options)
    {
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);
    }

    private void ApplyWindowBounds(AppOptionsDocument options)
    {
        if (options.Window.Width is > 0)
        {
            Width = options.Window.Width.Value;
        }

        if (options.Window.Height is > 0)
        {
            Height = options.Window.Height.Value;
        }

        if (options.Window.Left.HasValue && options.Window.Top.HasValue)
        {
            Position = ClampPositionToVisibleBounds(
                new PixelPoint(options.Window.Left.Value, options.Window.Top.Value),
                Width,
                Height);
        }

        if (options.Window.TreePaneWidth is >= 160)
        {
            TreePaneColumn.Width = new GridLength(options.Window.TreePaneWidth.Value, GridUnitType.Pixel);
        }

        // Restore window state
        WindowState = options.Window.State;
    }

    private PixelPoint ClampPositionToVisibleBounds(PixelPoint requestedPosition, double requestedWidth,
        double requestedHeight)
    {
        var windowWidth = Math.Max(1, (int)Math.Round(requestedWidth));
        var windowHeight = Math.Max(1, (int)Math.Round(requestedHeight));

        foreach (var screen in Screens.All)
        {
            if (Intersects(requestedPosition, windowWidth, windowHeight, screen.WorkingArea))
            {
                return ClampToScreen(requestedPosition, windowWidth, windowHeight, screen.WorkingArea);
            }
        }

        var fallbackScreen = Screens.Primary?.WorkingArea ?? Screens.All.First().WorkingArea;
        return ClampToScreen(requestedPosition, windowWidth, windowHeight, fallbackScreen);
    }

    private static bool Intersects(PixelPoint position, int width, int height, PixelRect bounds)
    {
        var right = position.X + width;
        var bottom = position.Y + height;

        return position.X < bounds.Right &&
               right > bounds.X &&
               position.Y < bounds.Bottom &&
               bottom > bounds.Y;
    }

    private static PixelPoint ClampToScreen(PixelPoint position, int width, int height, PixelRect bounds)
    {
        var maxX = Math.Max(bounds.X, bounds.Right - width);
        var maxY = Math.Max(bounds.Y, bounds.Bottom - height);

        var clampedX = Math.Clamp(position.X, bounds.X, maxX);
        var clampedY = Math.Clamp(position.Y, bounds.Y, maxY);
        return new PixelPoint(clampedX, clampedY);
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

    private void RefreshPlugins(OptionsDialogViewModel dialogVm)
    {
        dialogVm.CapturePluginStates();
        pluginManager.Discover(dialogVm.PluginPath, hostVersionProvider.Current);
        var descriptors = pluginManager.GetPluginDescriptors();
        var loadedById = pluginManager.Plugins
            .ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase);

        var plugins = descriptors
            .Select(descriptor =>
            {
                if (loadedById.TryGetValue(descriptor.Id, out var loaded))
                {
                    return new OptionsPluginItem(
                        loaded.Name,
                        string.IsNullOrWhiteSpace(loaded.Author?.Name) ? LocalizationService.Current.Get(TextKeys.UnknownAuthor) : loaded.Author.Name,
                        $"{loaded.Id} v{loaded.Version}",
                        isEnabled: descriptor.IsEnabled,
                        id: loaded.Id,
                        authorUrl: loaded.Author?.Url);
                }

                var authorSummary = string.IsNullOrWhiteSpace(descriptor.Author?.Name)
                    ? LocalizationService.Current.Get(TextKeys.UnknownAuthor)
                    : descriptor.Author.Name;
                var extendedInfo = $"{descriptor.Id} v{descriptor.Version}";

                return new OptionsPluginItem(
                    descriptor.Name,
                    authorSummary,
                    extendedInfo,
                    isEnabled: descriptor.IsEnabled,
                    id: descriptor.Id,
                    authorUrl: descriptor.Author?.Url);
            })
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        dialogVm.SetPlugins(plugins);
    }

    private void SyncPluginRuntimeState()
    {
        var options = appOptionsRepository.GetOrCreateAppOptions();
        var resolvedPluginPath = ResolvePluginDiscoveryPath(options.PluginPath);
        options.PluginPath = resolvedPluginPath;
        SaveAppOptions(options);

        pluginManager.Discover(resolvedPluginPath, hostVersionProvider.Current);

        ServiceProvider.ReregisterPluginsService();
        fileFormatManager = ServiceProvider.Resolve<FileFormatManager>();

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
        }
    }

    private void EnsureFileFormatProvidersLoaded()
    {
        if (fileFormatManager.GetReadableFormats().Count > 0)
        {
            return;
        }

        SyncPluginRuntimeState();
        if (fileFormatManager.GetReadableFormats().Count > 0)
        {
            return;
        }

        var availableDescriptors = pluginManager.GetPluginDescriptors()
            .Where(static descriptor => descriptor.IsAvailable)
            .Select(static descriptor => (descriptor.Id, IsEnabled: true))
            .ToArray();
        if (availableDescriptors.Length == 0)
        {
            return;
        }

        pluginManager.SavePluginEnabledStates(availableDescriptors);
        SyncPluginRuntimeState();
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
            if (dialogVm.SourceMode == AddToListSourceMode.Internet)
            {
                return value;
            }

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
            result.Add(new PathEntry(normalizedPath, size, isHttps, domain, isDirectory));
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
                ReadString(row, "kind").Equals("folder", StringComparison.OrdinalIgnoreCase))).ToArray());
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
                "networkfolder" => CatalogDocumentType.NetworkFolder,
                "networkresource" => CatalogDocumentType.NetworkResource,
                _ => CatalogDocumentType.Media
            };
            var status = ReadString(row, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                status = ReadString(row, "statusName");
            }

            var properties = new SkyCD.Documents.Collections.PropertiesCollection();
            if (!string.IsNullOrWhiteSpace(status))
            {
                properties["StatusName"] = status;
            }

            results.Add(new CatalogDocument
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"doc-{Guid.NewGuid():N}" : id,
                Name = name,
                ParentId = parentId,
                Type = documentType,
                Size = ReadLong(row, "size", "sizeBytes"),
                ChildrenCount = ReadLong(row, "childrenCount"),
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Properties = properties
            });
        }

        return results;
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
                    ChildrenCount = 0L
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
            entries[resourceNodeId] = new CatalogDocument
            {
                Id = resourceNodeId,
                Name = pathEntry.Path,
                ParentId = domainNodeId,
                Type = CatalogDocumentType.NetworkResource,
                Size = pathEntry.Size,
                ChildrenCount = 0L
            };
        }
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var value) && value is not null
            ? value is JsonElement { ValueKind: JsonValueKind.Null } ? string.Empty : value.ToString() ?? string.Empty
            : string.Empty;
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

    private sealed record PathEntry(string Path, long Size, bool IsHttps, string? Domain, bool IsDirectory);

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

}

