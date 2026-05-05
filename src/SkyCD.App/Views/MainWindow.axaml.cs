using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.App.Services;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Managers;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;
using SkyCD.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly CouchbaseLocalStore localStore;
    private readonly PluginManager pluginManager;
    private FileFormatManager fileFormatManager;
    private MainWindowViewModel? subscribedViewModel;
    private bool isCompletingConfirmedClose;
    private bool isSessionStateLoaded;
    private ColumnDefinition TreePaneColumn => MainLayoutGrid.ColumnDefinitions[0];

    public MainWindow()
        : this(
            new CouchbaseLocalStore(),
            new PluginManager(
                NullLogger<PluginManager>.Instance,
                new SkyCD.Plugin.Runtime.Factories.AssembliesListFactory(NullLogger<SkyCD.Plugin.Runtime.Factories.AssembliesListFactory>.Instance),
                new SkyCD.Plugin.Runtime.Factories.DiscoveredPluginFactory(),
                new SkyCD.Plugin.Runtime.Factories.PluginDocumentFactory(),
                CreateDesignTimeRepositoryManager()),
            new FileFormatManager([]))
    {
    }

    public MainWindow(
        CouchbaseLocalStore localStore,
        PluginManager pluginManager,
        FileFormatManager fileFormatManager)
    {
        this.localStore = localStore;
        this.pluginManager = pluginManager;
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

        var options = LoadAppOptions();
        ApplyWindowBounds(options);
        vm.ApplySessionState(
            options.Browser.ViewMode,
            options.Browser.SortMode,
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

        var fileTypeChoices = BuildDialogFilters(fileFormatManager.GetOpenFormats()).ToList();
        fileTypeChoices.Add(new FilePickerFileType("All files")
        {
            Patterns = ["*.*"]
        });

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
            var capability = fileFormatManager.GetInstanceFor(localPath);
            if (!capability.SupportedFormat.CanRead)
            {
                throw new InvalidOperationException($"Format '{capability.SupportedFormat.FormatId}' is not readable.");
            }

            await using var source = File.OpenRead(localPath);
            await fileFormatManager.ReadAsync(new SkyCD.Plugin.Abstractions.Capabilities.FileFormats.FileFormatReadRequest
            {
                FormatId = capability.SupportedFormat.FormatId,
                Source = source,
                FileName = Path.GetFileName(localPath)
            });

            vm.CompleteOpenCatalog();
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

        var targetPath = vm.CurrentCatalogPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var fileTypeChoices = BuildDialogFilters(fileFormatManager.GetSaveFormats()).ToList();

            fileTypeChoices.Add(new FilePickerFileType("All files")
            {
                Patterns = ["*.*"]
            });

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
                throw new InvalidOperationException($"Format '{capability.SupportedFormat.FormatId}' is read-only.");
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

        var fileTypeChoices = BuildDialogFilters(fileFormatManager.GetSaveFormats()).ToList();

        fileTypeChoices.Add(new FilePickerFileType("All files")
        {
            Patterns = ["*.*"]
        });

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
                throw new InvalidOperationException($"Format '{capability.SupportedFormat.FormatId}' is read-only.");
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
            RebuildPluginRuntimeServices(options.PluginPath);
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
        SaveAppOptions(options);
    }

    private AppOptionsDocument LoadAppOptions()
    {
        return localStore.GetRepository<AppOptionsDocument>()
            .GetOrCreate<AppOptionsDocument>(AppOptionsDocument.DocumentId);
    }

    private void SaveAppOptions(AppOptionsDocument options)
    {
        localStore.GetRepository<AppOptionsDocument>()
            .Save(AppOptionsDocument.DocumentId, options);
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

        RebuildPluginRuntimeServices(dialogVm.PluginPath);
        RefreshPlugins(dialogVm);
    }

    private void RefreshPlugins(OptionsDialogViewModel dialogVm)
    {
        dialogVm.CapturePluginStates();
        pluginManager.Discover(dialogVm.PluginPath, new Version(3, 0, 0));
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
                        id: loaded.Id);
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
                    id: descriptor.Id);
            })
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        dialogVm.SetPlugins(plugins);
    }

    private void RebuildPluginRuntimeServices(string? pluginPath)
    {
        var resolvedPluginPath = string.IsNullOrWhiteSpace(pluginPath)
            ? ResolveDefaultPluginPath()
            : pluginPath;

        pluginManager.Discover(resolvedPluginPath, new Version(3, 0, 0));

        var discoveredPlugins = pluginManager.Plugins.ToList();
        var pluginById = discoveredPlugins.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);

        IServiceCollection mergedServices = new ServiceCollection()
            .AddRegistrator<CommonRuntimeServiceRegistrator>();

        mergedServices.AddSingleton<IReadOnlyList<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin>>(discoveredPlugins);
        mergedServices.AddSingleton<IReadOnlyCollection<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin>>(discoveredPlugins);
        mergedServices.AddSingleton<IReadOnlyDictionary<string, SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin>>(pluginById);
        mergedServices.AddPluginRegistrator(discoveredPlugins);

        PluginServiceProvider.RebuildGlobal();
        var runtimeProvider = PluginServiceProvider.Instance;
        runtimeProvider.Register(mergedServices);
        fileFormatManager = runtimeProvider.GetRequiredService<FileFormatManager>();
    }

    private static string ResolveDefaultPluginPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "Plugins"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Plugins")),
            Path.Combine(Environment.CurrentDirectory, "Plugins", "samples"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Plugins", "samples"))
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
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

    private static IReadOnlyList<FilePickerFileType> BuildDialogFilters(IReadOnlyList<FileFormatDescriptor> formats)
    {
        return formats
            .Select(format => new FilePickerFileType(format.DisplayName)
            {
                Patterns = format.Extensions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(static extension => $"*{extension}")
                    .ToArray()
            })
            .ToArray();
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

    private static RepositoryManager CreateDesignTimeRepositoryManager()
    {
        var databaseManager = new DatabaseManager();
        var directory = Path.Combine(Path.GetTempPath(), "SkyCD", "MainWindow");
        Directory.CreateDirectory(directory);
        databaseManager.Connect("default", directory);
        return new RepositoryManager(databaseManager);
    }

}
