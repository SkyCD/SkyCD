using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel : ObservableObject
{
    private readonly HashSet<string> disabledPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private LanguageItem originalSelectedLanguage;

    public OptionsDialogViewModel()
        : this(LoadLanguagePacksFromFilesystem())
    {
    }

    internal static IEnumerable<string> LoadLanguagePacksFromFilesystem()
    {
        // Load and validate language codes from ./LanguagePacks directory
        try
        {
            var languageDir = Path.Combine(AppContext.BaseDirectory, "LanguagePacks");
            if (!Directory.Exists(languageDir))
            {
                return ["English"];
            }

            var validCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
                .Select(c => c.Name.ToLowerInvariant())
                .ToHashSet();

            return Directory.EnumerateFiles(languageDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                .Where(code => validCultures.Contains(code))
                .DefaultIfEmpty("English")
                .Distinct();
        }
        catch (IOException)
        {
            // Log error (implement logging)
            return ["English"];
        }
    }

    public OptionsDialogViewModel(IEnumerable<string> availableLanguages)
    {
        foreach (var language in availableLanguages.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Languages.Add(LanguageItem.Create(language));
        }

        if (Languages.Count == 0)
        {
            Languages.Add(LanguageItem.Create("English"));
        }

        SelectedLanguage = Languages[0];
originalSelectedLanguage = SelectedLanguage;
    }

    public ObservableCollection<OptionsPluginItem> Plugins { get; } = [];

    public ObservableCollection<LanguageItem> Languages { get; } = [];

    [ObservableProperty]
    private string pluginPath = string.Empty;

    [ObservableProperty]
    private OptionsPluginItem? selectedPlugin;

    [ObservableProperty]
    private LanguageItem selectedLanguage;

    [ObservableProperty]
    private string infoMessage = string.Empty;

    [ObservableProperty]
    private bool dialogAccepted;

    [ObservableProperty]
    private int selectedTabIndex;

    public event EventHandler? BrowsePluginPathRequested;

    public event EventHandler? RefreshPluginsRequested;

    [RelayCommand]
    private void BrowsePluginPath()
    {
        BrowsePluginPathRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RefreshPlugins()
    {
        RefreshPluginsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void ConfigurePlugin()
    {
        if (SelectedPlugin is null)
        {
            return;
        }

        InfoMessage = $"Configure '{SelectedPlugin.Name}' is not implemented yet.";
    }

    [RelayCommand]
    private void Confirm()
    {
        // Save selected language to settings (implement settings persistence)
        originalSelectedLanguage = SelectedLanguage;
        DialogAccepted = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        // Revert to original language
        SelectedLanguage = originalSelectedLanguage;
        DialogAccepted = false;
    }

    private bool CanConfigure()
    {
        return SelectedPlugin is not null && SelectedPlugin.SupportsConfiguration;
    }

    public void SetPlugins(IEnumerable<OptionsPluginItem> plugins)
    {
        var snapshot = plugins.ToArray();
        Plugins.Clear();
        foreach (var plugin in snapshot)
        {
            plugin.IsEnabled = !disabledPluginIds.Contains(plugin.Id);
            Plugins.Add(plugin);
        }

        SelectedPlugin = Plugins.FirstOrDefault();
        InfoMessage = $"Loaded {Plugins.Count} plugin(s).";
        ConfigurePluginCommand.NotifyCanExecuteChanged();
    }

    public void SetDisabledPluginIds(IEnumerable<string>? pluginIds)
    {
        disabledPluginIds.Clear();
        if (pluginIds is null)
        {
            return;
        }

        foreach (var pluginId in pluginIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            disabledPluginIds.Add(pluginId);
        }
    }

    public void CapturePluginStates()
    {
        if (Plugins.Count == 0)
        {
            return;
        }

        disabledPluginIds.Clear();
        foreach (var plugin in Plugins.Where(static plugin => !plugin.IsEnabled))
        {
            disabledPluginIds.Add(plugin.Id);
        }
    }

    public IReadOnlyList<string> GetDisabledPluginIds()
    {
        CapturePluginStates();
        return disabledPluginIds
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    partial void OnSelectedPluginChanged(OptionsPluginItem? value)
    {
        ConfigurePluginCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLanguageChanged(LanguageItem value)
    {
        // Apply language change with error handling
        try
        {
            if (value != null && !string.IsNullOrWhiteSpace(value.Name))
            {
                CultureInfo.CurrentUICulture = new CultureInfo(value.Name);
            }
            // Trigger UI binding refresh (Avalonia-specific logic)
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(null));
            InfoMessage = string.Empty;
        }
        catch (CultureNotFoundException)
        {
            // Revert to valid language
            SelectedLanguage = originalSelectedLanguage;
            InfoMessage = "Invalid language selected - reverted to original";
        }
    }
}
