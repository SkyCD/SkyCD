using System;
using System.Collections;
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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SkyCD.App.Models;
using SkyCD.App.Services;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private readonly AppOptionsStore appOptionsStore = new();
    private readonly FileFormatRoutingService fileFormatRoutingService;
    private readonly List<object> dynamicToolsMenuItems = [];
    private readonly MenuExtensionService menuExtensionService;
    private readonly PluginCatalog pluginCatalog = new();
    private readonly RuntimePluginDiscoveryService pluginDiscoveryService = new();
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;
    private MainWindowViewModel? subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        fileFormatRoutingService = new FileFormatRoutingService(pluginCatalog);
        menuExtensionService = new MenuExtensionService(pluginCatalog);
        LoadPluginsForFileFormats();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.AddToListRequested -= OnAddToListRequested;
            subscribedViewModel.NewCatalogRequested -= OnNewCatalogRequested;
            subscribedViewModel.OpenCatalogRequested -= OnOpenCatalogRequested;
            subscribedViewModel.SaveCatalogAsRequested -= OnSaveCatalogAsRequested;
            subscribedViewModel.SaveCatalogRequested -= OnSaveCatalogRequested;
            subscribedViewModel.SaveCatalogAsRequested -= OnSaveCatalogAsRequested;
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
            subscribedViewModel.SaveCatalogAsRequested += OnSaveCatalogAsRequested;
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
        if (e.PropertyName == nameof(MainWindowViewModel.IsDirtyDocument)) UpdateWindowTitle();
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TreeView treeView || subscribedViewModel is null) return;

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
        if (sender is not ListBox listBox || subscribedViewModel is null) return;

        if (!e.TryGetPosition(listBox, out var point))
        {
            e.Handled = subscribedViewModel.SelectedBrowserItem is null;
            return;
        }

        var hit = listBox.InputHitTest(point) as Visual;
        var listBoxItem = FindAncestor<ListBoxItem>(hit);
        if (listBoxItem?.DataContext is BrowserItem item)
        {
            subscribedViewModel.SelectedBrowserItem = item;
            return;
        }

        e.Handled = true;
    }

    private void OnBrowserListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (subscribedViewModel is null) return;

        subscribedViewModel.NavigateToFolderCommand.Execute(null);
    }

    private void UpdateWindowTitle()
    {
        if (subscribedViewModel is not null && subscribedViewModel.IsDirtyDocument)
            Title = "* SkyCD";
        else
            Title = "SkyCD";
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (isSessionStateLoaded || DataContext is not MainWindowViewModel vm) return;

        var options = appOptionsStore.Load();
        ApplyWindowBounds(options);
        vm.ApplySessionState(
            ParseBrowserViewMode(options.BrowserViewMode),
            ParseBrowserSortMode(options.BrowserSortMode),
            options.IsStatusBarVisible);
        ApplyLanguage(options.Language);

        isSessionStateLoaded = true;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (isCompletingConfirmedClose) return;

        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsDirtyDocument)
        {
            e.Cancel = true;
            var decision = await ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel) return;

            if (decision == UnsavedChangesDecision.Save) vm.SaveCatalogCommand.Execute(null);

            SaveUiState(vm);
            isCompletingConfirmedClose = true;
            Close();
            return;
        }

        SaveUiState(vm);
    }

    private async void OnAddToListRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dialogVm = new AddToListDialogViewModel();
        var dialog = new AddToListWindow
        {
            DataContext = dialogVm
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted != true) return;

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
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsDirtyDocument)
        {
            var decision = await ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel) return;

            if (decision == UnsavedChangesDecision.Save) vm.SaveCatalogCommand.Execute(null);
        }

        vm.CompleteNewCatalog();
    }

    private async void OnOpenCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsDirtyDocument)
        {
            var decision = await ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel) return;

            if (decision == UnsavedChangesDecision.Save) vm.SaveCatalogCommand.Execute(null);
        }

        vm.CompleteOpenCatalog();
    }

    private async void OnSaveCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var targetPath = vm.CurrentCatalogPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var saveFormats = fileFormatRoutingService.GetSaveFormats();
            var fileTypeChoices = new List<FilePickerFileType>();

            foreach (var format in saveFormats)
            {
                var patterns = format.Extensions.Select(ext => $"*{ext}").ToArray();
                fileTypeChoices.Add(new FilePickerFileType(format.DisplayName)
                {
                    Patterns = patterns
                });
            }

            fileTypeChoices.Add(new FilePickerFileType("All files")
            {
                Patterns = ["*.*"]
            });

            var defaultFormat = saveFormats.FirstOrDefault();
            var defaultExtension = defaultFormat?.Extensions.FirstOrDefault()?.TrimStart('.') ?? "scd";

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save catalog",
                SuggestedFileName = $"catalog.{defaultExtension}",
                DefaultExtension = defaultExtension,
                FileTypeChoices = fileTypeChoices
            });

            targetPath = file?.TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(targetPath)) return;

        try
        {
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
        if (DataContext is not MainWindowViewModel vm) return;

        var saveFormats = fileFormatRoutingService.GetSaveFormats();
        var fileTypeChoices = new List<FilePickerFileType>();

        foreach (var format in saveFormats)
        {
            var patterns = format.Extensions.Select(ext => $"*{ext}").ToArray();
            fileTypeChoices.Add(new FilePickerFileType(format.DisplayName)
            {
                Patterns = patterns
            });
        }

        fileTypeChoices.Add(new FilePickerFileType("All files")
        {
            Patterns = ["*.*"]
        });

        var defaultFormat = saveFormats.FirstOrDefault();
        var defaultExtension = defaultFormat?.Extensions.FirstOrDefault()?.TrimStart('.') ?? "scd";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save catalog as",
            SuggestedFileName = $"catalog.{defaultExtension}",
            DefaultExtension = defaultExtension,
            FileTypeChoices = fileTypeChoices
        });

        var localPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;

        try
        {
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
        var options = appOptionsStore.Load();
        var pluginPath = ResolveConfiguredOrDefaultPluginPath(options.PluginPath);

        e.Dialog.PluginPath = pluginPath;
        if (!string.IsNullOrWhiteSpace(options.Language) &&
            e.Dialog.Languages.FirstOrDefault(language =>
                string.Equals(language.Name, options.Language, StringComparison.OrdinalIgnoreCase)) is { } language)
            e.Dialog.SelectedLanguage = language;

        e.Dialog.SetDisabledPluginIds(options.DisabledPluginIds);
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
            options.PluginPath = e.Dialog.PluginPath;
            options.Language = e.Dialog.SelectedLanguage.Name;
            options.DisabledPluginIds = e.Dialog.GetDisabledPluginIds().ToList();
            options.OptionsTabIndex = Math.Max(0, e.Dialog.SelectedTabIndex);
            appOptionsStore.Save(options);
            LoadPluginsForFileFormats();
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
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "3.0.0";
        var dialogVm = new AboutDialogViewModel("SkyCD", version, "https://github.com/SkyCD/SkyCD");
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
        var options = appOptionsStore.Load();

        // Don't save window position if window is minimized
        if (WindowState == WindowState.Normal)
        {
            options.WindowLeft = Position.X;
            options.WindowTop = Position.Y;
            options.WindowWidth = Width;
            options.WindowHeight = Height;
            options.WindowState = "Normal";
        }
        else if (WindowState == WindowState.Maximized)
        {
            options.WindowState = "Maximized";
        }

        if (TreePaneColumn.Width.IsAbsolute) options.TreePaneWidth = TreePaneColumn.Width.Value;

        options.IsStatusBarVisible = vm.IsStatusBarVisible;
        options.BrowserViewMode = vm.CurrentViewMode.ToString();
        options.BrowserSortMode = vm.CurrentSortMode.ToString();
        appOptionsStore.Save(options);
    }

    private void ApplyWindowBounds(AppOptions options)
    {
        if (options.WindowWidth is > 0) Width = options.WindowWidth.Value;

        if (options.WindowHeight is > 0) Height = options.WindowHeight.Value;

        if (options.WindowLeft.HasValue && options.WindowTop.HasValue)
            Position = ClampPositionToVisibleBounds(
                new PixelPoint(options.WindowLeft.Value, options.WindowTop.Value),
                Width,
                Height);

        if (options.TreePaneWidth is >= 160)
            TreePaneColumn.Width = new GridLength(options.TreePaneWidth.Value, GridUnitType.Pixel);

        // Restore window state
        if (string.Equals(options.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
            WindowState = WindowState.Maximized;
    }

    private PixelPoint ClampPositionToVisibleBounds(PixelPoint requestedPosition, double requestedWidth,
        double requestedHeight)
    {
        var windowWidth = Math.Max(1, (int)Math.Round(requestedWidth));
        var windowHeight = Math.Max(1, (int)Math.Round(requestedHeight));

        foreach (var screen in Screens.All)
            if (Intersects(requestedPosition, windowWidth, windowHeight, screen.WorkingArea))
                return ClampToScreen(requestedPosition, windowWidth, windowHeight, screen.WorkingArea);

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

    private static BrowserSortMode ParseBrowserSortMode(string? value)
    {
        return Enum.TryParse<BrowserSortMode>(value, true, out var mode)
            ? mode
            : BrowserSortMode.Name;
    }

    private async void OnBrowsePluginPathRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm) return;

        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select plug-in directory",
            AllowMultiple = false
        });

        if (picked.Count == 0) return;

        var pickedPath = picked[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(pickedPath)) return;

        dialogVm.PluginPath = pickedPath;
        RefreshPlugins(dialogVm);
    }

    private void OnRefreshPluginsRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm) return;

        RefreshPlugins(dialogVm);
    }

    private void RefreshPlugins(OptionsDialogViewModel dialogVm)
    {
        dialogVm.CapturePluginStates();
        var plugins = pluginDiscoveryService.Discover(dialogVm.PluginPath);
        dialogVm.SetPlugins(plugins);
    }

    private void LoadPluginsForFileFormats()
    {
        var options = appOptionsStore.Load();
        var pluginPath = ResolveConfiguredOrDefaultPluginPath(options.PluginPath);

        if (string.IsNullOrWhiteSpace(pluginPath) || !Directory.Exists(pluginPath))
        {
            pluginCatalog.SetPlugins([]);
            RebuildToolsMenuContributions();
            return;
        }

        var hostVersion = new Version(3, 0, 0);
        var discoveryService = new PluginDiscoveryService();
        var discoveredPlugins = new List<DiscoveredPlugin>();
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dllPaths = EnumerateCandidatePluginDlls(pluginPath);
        foreach (var dllPath in dllPaths)
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                var plugins = discoveryService.DiscoverFromAssembly(assembly, hostVersion);
                foreach (var plugin in plugins)
                    if (seenPluginIds.Add(plugin.Plugin.Descriptor.Id))
                        discoveredPlugins.Add(plugin);
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }

        if (options.DisabledPluginIds.Count > 0)
        {
            var disabledPluginIds = new HashSet<string>(options.DisabledPluginIds, StringComparer.OrdinalIgnoreCase);
            discoveredPlugins = discoveredPlugins
                .Where(plugin => !disabledPluginIds.Contains(plugin.Plugin.Descriptor.Id))
                .ToList();
        }

        pluginCatalog.SetPlugins(discoveredPlugins);
        RebuildToolsMenuContributions();
    }

    private void RebuildToolsMenuContributions()
    {
        if (ToolsMenuItem.Items is not IList items) return;

        foreach (var dynamicItem in dynamicToolsMenuItems)
        {
            if (dynamicItem is MenuItem menuItem) menuItem.Click -= OnPluginToolsMenuItemClick;
            items.Remove(dynamicItem);
        }

        dynamicToolsMenuItems.Clear();

        var contributions = menuExtensionService.GetMenuContributions("Tools");
        var uniqueContributions = contributions
            .GroupBy(contribution => contribution.CommandId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(contribution => contribution.Order).First())
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueContributions.Length == 0) return;

        var insertionIndex = items.IndexOf(ToolsOptionsMenuItem);
        if (insertionIndex < 0) insertionIndex = items.Count;

        var separator = new Separator();
        items.Insert(insertionIndex, separator);
        dynamicToolsMenuItems.Add(separator);
        insertionIndex++;

        foreach (var contribution in uniqueContributions)
        {
            var menuItem = new MenuItem
            {
                Header = contribution.Title,
                Tag = contribution.CommandId
            };
            menuItem.Click += OnPluginToolsMenuItemClick;
            items.Insert(insertionIndex, menuItem);
            dynamicToolsMenuItems.Add(menuItem);
            insertionIndex++;
        }
    }

    private async void OnPluginToolsMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string commandId }) return;

        var executionResult = await menuExtensionService.ExecuteAsync(
            commandId,
            new MenuCommandContext
            {
                HostApi = new MainWindowHostCommandApi(this)
            },
            timeout: TimeSpan.FromSeconds(5));

        if (!executionResult.Success && DataContext is MainWindowViewModel vm)
            vm.StatusText = executionResult.Error ?? $"Failed to execute command '{commandId}'.";
    }

    private static string ResolveConfiguredOrDefaultPluginPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)
            && Directory.Exists(configuredPath)
            && Directory.EnumerateFiles(configuredPath, "*.dll", SearchOption.AllDirectories).Any())
            return configuredPath;

        return ResolveDefaultPluginPath();
    }

    private static string ResolveDefaultPluginPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Plugins"),
            Path.Combine(Environment.CurrentDirectory, "Plugins"),
            Path.Combine(AppContext.BaseDirectory, "Plugins", "samples"),
            Path.Combine(Environment.CurrentDirectory, "Plugins", "samples")
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "Plugins"));
            candidates.Add(Path.Combine(current.FullName, "Plugins", "samples"));
            current = current.Parent;
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static IReadOnlyList<string> EnumerateCandidatePluginDlls(string pluginPath)
    {
        return Directory
            .EnumerateFiles(pluginPath, "*.dll", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                       StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}",
                                       StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}",
                                       StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveImportedName(AddToListDialogViewModel dialogVm)
    {
        if (!string.IsNullOrWhiteSpace(dialogVm.MediaName)) return dialogVm.MediaName;

        if (!string.IsNullOrWhiteSpace(dialogVm.SourceValue))
        {
            var value = dialogVm.SourceValue.Trim();
            if (dialogVm.SourceMode == AddToListSourceMode.Internet) return value;

            return Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
    }

    private static T? FindAncestor<T>(Visual? visual) where T : class
    {
        var current = visual;
        while (current is not null)
        {
            if (current is T target) return target;

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

    private sealed class MainWindowHostCommandApi(MainWindow owner) : IHostCommandApi
    {
        public Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default)
        {
            if (owner.DataContext is MainWindowViewModel vm)
                vm.StatusText = $"Navigate request to node {nodeId} is not implemented yet.";
            return Task.CompletedTask;
        }

        public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
        {
            if (owner.DataContext is MainWindowViewModel vm)
                vm.StatusText = message;
            return Task.CompletedTask;
        }

    }
}
