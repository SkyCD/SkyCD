using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private readonly PluginManager pluginManager;
    private readonly HostVersionProvider hostVersionProvider;
    private FileFormatManager fileFormatManager;
    private MainWindowViewModel? subscribedViewModel;
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;
    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    public MainWindow(
        RepositoryManager repositoryManager,
        PluginManager pluginManager,
        FileFormatManager fileFormatManager)
    {
        this.repositoryManager = repositoryManager;
        appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        this.pluginManager = pluginManager;
        hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();
        this.fileFormatManager = fileFormatManager;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closing += OnClosing;
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

    private void OnOpened(object? sender, EventArgs e)
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
        ApplyLanguage(options.Language);
        if (!string.IsNullOrWhiteSpace(options.LastOpenedCatalogPath) && File.Exists(options.LastOpenedCatalogPath))
        {
            EnsureFileFormatProvidersLoaded();
            _ = TryLoadCatalogIntoViewModelAsync(options.LastOpenedCatalogPath);
        }

        isSessionStateLoaded = true;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (isCompletingConfirmedClose)
        {
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
                vm.StatusText = "Login canceled.";
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
            Title = "Open catalog",
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
                ? "none"
                : string.Join(", ", readableExtensions);

            vm.StatusText = string.IsNullOrWhiteSpace(extension)
                ? $"Failed to open catalog: unsupported file format. Readable extensions: {supportedText}."
                : $"Failed to open catalog: no readable handler mapped for '{extension}'. Readable extensions: {supportedText}.";
        }
        catch (FileFormatHandlerResolutionException)
        {
            vm.StatusText = "Failed to open catalog: file format handler is unavailable. Check plugin settings.";
        }
        catch (FileFormatNotReadableException ex)
        {
            vm.StatusText = $"Failed to open catalog: plugin cannot read this format ({ex.Message}).";
        }
        catch (FileFormatReadFailedException ex)
        {
            vm.StatusText = $"Failed to open catalog: plugin read error ({ex.Message}).";
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Failed to open catalog: {ex.Message}";
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
                Title = "Save catalog",
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
            vm.StatusText = $"Failed to save catalog: {ex.Message}";
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
            Title = "Save catalog as",
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
            vm.StatusText = $"Failed to save catalog: {ex.Message}";
        }
    }

    private async void OnPropertiesRequested(object? sender, PropertiesDialogRequestedEventArgs e)
    {
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
        if (!string.IsNullOrWhiteSpace(options.Language) &&
            e.Dialog.Languages.FirstOrDefault(language =>
                string.Equals(language.Name, options.Language, StringComparison.OrdinalIgnoreCase)) is { } language)
        {
            e.Dialog.SelectedLanguage = language;
        }

        e.Dialog.SelectedTabIndex = Math.Max(0, options.OptionsTabIndex);
        e.Dialog.BrowsePluginPathRequested += OnBrowsePluginPathRequested;
        e.Dialog.RefreshPluginsRequested += OnRefreshPluginsRequested;
        RefreshPlugins(e.Dialog);

        var dialog = new OptionsWindow
        {
            DataContext = e.Dialog
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted == true)
        {
            var pluginStates = e.Dialog.Plugins
                .Select(static plugin => (plugin.Id, plugin.IsEnabled))
                .ToArray();

            options.PluginPath = e.Dialog.PluginPath;
            options.Language = e.Dialog.SelectedLanguage.Name;
            options.OptionsTabIndex = Math.Max(0, e.Dialog.SelectedTabIndex);
            SaveAppOptions(options);
            pluginManager.SavePluginEnabledStates(pluginStates);
            SyncPluginRuntimeState();
            ApplyLanguage(options.Language);

            // Trigger UI refresh to apply new language
            InvalidateVisual();
        }

        e.Dialog.BrowsePluginPathRequested -= OnBrowsePluginPathRequested;
        e.Dialog.RefreshPluginsRequested -= OnRefreshPluginsRequested;

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
                ("Reading directory from remote server...", 20),
                ("Preparing database for modifications...", 55),
                ("Updating indexes...", 100)
            ],
            AddToListSourceMode.Folder =>
            [
                ("Reading source folder...", 25),
                ("Preparing database for modifications...", 60),
                ("Updating indexes...", 100)
            ],
            _ =>
            [
                ("Reading media metadata...", 35),
                ("Preparing database for modifications...", 70),
                ("Updating indexes...", 100)
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

    private async Task TryLoadCatalogIntoViewModelAsync(string localPath)
    {
        var capability = fileFormatManager.GetInstanceFor(localPath);
        if (!capability.SupportedFormat.CanRead)
        {
            throw new FileFormatNotReadableException(capability.SupportedFormat.FormatId);
        }

        await using var source = File.OpenRead(localPath);
        var readResult = await fileFormatManager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = capability.SupportedFormat.FormatId,
            Source = source,
            FileName = Path.GetFileName(localPath)
        });

        ReplaceCatalogContent(ExtractCatalogEntries(readResult.Payload));
        var reloadedViewModel = new MainWindowViewModel(repositoryManager);
        reloadedViewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
        var options = LoadAppOptions();
        reloadedViewModel.ApplySessionState(
            options.Browser.ViewMode,
            options.Browser.SortMode,
            options.IsStatusBarVisible);
        reloadedViewModel.CurrentCatalogPath = localPath;
        options.LastOpenedCatalogPath = localPath;
        SaveAppOptions(options);
        DataContext = reloadedViewModel;
        reloadedViewModel.CompleteOpenCatalog();
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
            Title = "Select plug-in directory",
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
                        string.IsNullOrWhiteSpace(loaded.Author?.Name) ? "Unknown author" : loaded.Author.Name,
                        $"{loaded.Id} v{loaded.Version}",
                        isEnabled: descriptor.IsEnabled,
                        id: loaded.Id,
                        authorUrl: loaded.Author?.Url);
                }

                var authorSummary = string.IsNullOrWhiteSpace(descriptor.Author?.Name)
                    ? "Unknown author"
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
        var culture = LanguageCultureResolver.ResolveCulture(languageName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private void ReplaceCatalogContent(IReadOnlyList<CatalogDocument> entries)
    {
        foreach (var existing in catalogRepository.GetAll())
        {
            using var document = catalogRepository.Collection.GetDocument(existing.Id);
            if (document is not null)
            {
                catalogRepository.Collection.Delete(document);
            }
        }

        foreach (var entry in entries)
        {
            catalogRepository.Save(entry.Id, entry);
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

        return [];
    }

    private static bool TryExtractRows(object? payload, out IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (payload is IEnumerable<Dictionary<string, object?>> typedRows)
        {
            rows = typedRows.ToArray();
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

            result.Add(new PathEntry(normalizedPath, size, isHttps, domain));
        }

        entries = result;
        return result.Count > 0;
    }

    private static IReadOnlyList<CatalogDocument> BuildEntriesFromRows(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var results = new List<CatalogDocument>();
        foreach (var row in rows)
        {
            var id = ReadString(row, "id");
            var name = ReadString(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var parentId = ReadString(row, "parentId");
            if (string.Equals(parentId, "-1", StringComparison.Ordinal))
            {
                parentId = null;
            }

            var normalizedType = ReadString(row, "type").ToLowerInvariant();
            var documentType = normalizedType switch
            {
                "folder" => CatalogDocumentType.Folder,
                "media" => CatalogDocumentType.Media,
                _ => CatalogDocumentType.Media
            };

            results.Add(new CatalogDocument
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"doc-{Guid.NewGuid():N}" : id,
                Name = name,
                ParentId = parentId,
                Type = documentType,
                Size = ReadLong(row, "size", "sizeBytes"),
                ChildrenCount = ReadLong(row, "childrenCount")
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
                var isFile = i == parts.Length - 1;
                var id = $"{currentParentId}/{name}".ToLowerInvariant();
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
                    Type = isFile ? CatalogDocumentType.Media : CatalogDocumentType.Folder,
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

        const string internetNodeId = "library/internet";
        if (!entries.ContainsKey(internetNodeId))
        {
            entries[internetNodeId] = new CatalogDocument
            {
                Id = internetNodeId,
                Name = "Internet",
                ParentId = rootId,
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 0L
            };
        }

        var domainNodeId = $"{internetNodeId}/{pathEntry.Domain.ToLowerInvariant()}";
        if (!entries.ContainsKey(domainNodeId))
        {
            entries[domainNodeId] = new CatalogDocument
            {
                Id = domainNodeId,
                Name = pathEntry.Domain,
                ParentId = internetNodeId,
                Type = CatalogDocumentType.NetworkResource,
                Size = 0L,
                ChildrenCount = 0L
            };
        }

        var resourceNodeId = $"{domainNodeId}/{pathEntry.Path.ToLowerInvariant()}";
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
            ? value.ToString() ?? string.Empty
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

    private sealed record PathEntry(string Path, long Size, bool IsHttps, string? Domain);

}

