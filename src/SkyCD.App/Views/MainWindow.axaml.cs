using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SkyCD.App.Models;
using SkyCD.App.Services;
using SkyCD.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SkyCD.App.Views;

public partial class MainWindow : Window
{
    private readonly AppOptionsStore appOptionsStore = new();
    private readonly RuntimePluginDiscoveryService pluginDiscoveryService = new();
    private MainWindowViewModel? subscribedViewModel;
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;

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
            subscribedViewModel.AboutRequested -= OnAboutRequested;
            subscribedViewModel.OptionsRequested -= OnOptionsRequested;
            subscribedViewModel.PropertiesRequested -= OnPropertiesRequested;
            subscribedViewModel.ExitRequested -= OnExitRequested;
        }

        subscribedViewModel = DataContext as MainWindowViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.AddToListRequested += OnAddToListRequested;
            subscribedViewModel.AboutRequested += OnAboutRequested;
            subscribedViewModel.OptionsRequested += OnOptionsRequested;
            subscribedViewModel.PropertiesRequested += OnPropertiesRequested;
            subscribedViewModel.ExitRequested += OnExitRequested;
        }
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Close();
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
            appOptionsStore.Save(options);
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
            var position = new PixelPoint(options.WindowLeft.Value, options.WindowTop.Value);
            
            // Validate position is not off-screen
            if (IsPositionOnScreen(position))
            {
                Position = position;
            }
        }

        // Restore window state
        if (string.Equals(options.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
        {
            WindowState = WindowState.Maximized;
        }
    }

    private bool IsPositionOnScreen(PixelPoint position)
    {
        var screens = Screens.All;
        foreach (var screen in screens)
        {
            var screenBounds = screen.WorkingArea;
            if (position.X >= screenBounds.X && 
                position.Y >= screenBounds.Y &&
                position.X < screenBounds.Right &&
                position.Y < screenBounds.Bottom)
            {
                return true;
            }
        }
        return false;
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
}
