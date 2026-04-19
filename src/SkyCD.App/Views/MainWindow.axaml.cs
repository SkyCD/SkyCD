using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SkyCD.App.Models;
using SkyCD.App.Services;
using SkyCD.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private readonly AppOptionsStore appOptionsStore = new();
    private readonly RuntimePluginDiscoveryService pluginDiscoveryService = new();
    private MainWindowViewModel? subscribedViewModel;
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;
    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    public MainWindow()
    {
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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDirtyDocument))
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
        if (listBoxItem?.DataContext is BrowserItem item)
        {
            subscribedViewModel.SelectedBrowserItem = item;
            return;
        }

        e.Handled = true;
    }

    private void UpdateWindowTitle()
    {
        if (subscribedViewModel is not null && subscribedViewModel.IsDirtyDocument)
        {
            Title = "* SkyCD";
        }
        else
        {
            Title = "SkyCD";
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (isSessionStateLoaded || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

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

        vm.StatusText = "Done.";
        vm.IsDirtyDocument = true;
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

        vm.CompleteOpenCatalog();
    }

    private async void OnSaveCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var targetPath = vm.CurrentCatalogPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save catalog",
                SuggestedFileName = "catalog.scd",
                DefaultExtension = "scd",
                FileTypeChoices =
                [
                    new FilePickerFileType("SkyCD Catalog")
                    {
                        Patterns = ["*.scd"]
                    },
                    new FilePickerFileType("All files")
                    {
                        Patterns = ["*.*"]
                    }
                ]
            });

            targetPath = file?.TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

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
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save catalog as",
            SuggestedFileName = "catalog.scd",
            DefaultExtension = "scd",
            FileTypeChoices =
            [
                new FilePickerFileType("SkyCD Catalog")
                {
                    Patterns = ["*.scd"]
                },
                new FilePickerFileType("All files")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        var localPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

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
        var pluginPath = string.IsNullOrWhiteSpace(options.PluginPath)
            ? ResolveDefaultPluginPath()
            : options.PluginPath;

        e.Dialog.PluginPath = pluginPath;
        if (!string.IsNullOrWhiteSpace(options.Language) &&
            e.Dialog.Languages.FirstOrDefault(language =>
                string.Equals(language.Name, options.Language, StringComparison.OrdinalIgnoreCase)) is { } language)
        {
            e.Dialog.SelectedLanguage = language;
        }

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
            ApplyLanguage(options.Language);
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

        if (TreePaneColumn.Width.IsAbsolute)
        {
            options.TreePaneWidth = TreePaneColumn.Width.Value;
        }

        options.IsStatusBarVisible = vm.IsStatusBarVisible;
        options.BrowserViewMode = vm.CurrentViewMode.ToString();
        options.BrowserSortMode = vm.CurrentSortMode.ToString();
        appOptionsStore.Save(options);
    }

    private void ApplyWindowBounds(AppOptions options)
    {
        if (options.WindowWidth is > 0)
        {
            Width = options.WindowWidth.Value;
        }

        if (options.WindowHeight is > 0)
        {
            Height = options.WindowHeight.Value;
        }

        if (options.WindowLeft.HasValue && options.WindowTop.HasValue)
        {
            Position = ClampPositionToVisibleBounds(
                new PixelPoint(options.WindowLeft.Value, options.WindowTop.Value),
                Width,
                Height);
        }

        if (options.TreePaneWidth is >= 160)
        {
            TreePaneColumn.Width = new GridLength(options.TreePaneWidth.Value, GridUnitType.Pixel);
        }

        // Restore window state
        if (string.Equals(options.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
        {
            WindowState = WindowState.Maximized;
        }
    }

    private PixelPoint ClampPositionToVisibleBounds(PixelPoint requestedPosition, double requestedWidth, double requestedHeight)
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

    private static BrowserSortMode ParseBrowserSortMode(string? value)
    {
        return Enum.TryParse<BrowserSortMode>(value, true, out var mode)
            ? mode
            : BrowserSortMode.Name;
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
        var plugins = pluginDiscoveryService.Discover(dialogVm.PluginPath);
        dialogVm.SetPlugins(plugins);
    }

    private static string ResolveDefaultPluginPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "Plugins", "samples"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Plugins", "samples"))
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
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
}
