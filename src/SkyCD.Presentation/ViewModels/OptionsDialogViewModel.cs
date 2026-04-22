using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel : ObservableObject
{
    private readonly HashSet<string> disabledPluginIds = new(StringComparer.OrdinalIgnoreCase);

    public OptionsDialogViewModel()
        : this(["English", "Lithuanian"])
    {
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

        selectedLanguage = Languages[0];
        RefreshFilteredCategories();
        selectedSettingCategory = CurrentCategoryName;
    }

    public ObservableCollection<OptionsPluginItem> Plugins { get; } = [];

    public ObservableCollection<LanguageItem> Languages { get; } = [];

    public ObservableCollection<string> FilteredSettingCategories { get; } = [];

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

    [ObservableProperty]
    private string settingsSearchText = string.Empty;

    [ObservableProperty]
    private string? selectedSettingCategory;

    public IReadOnlyList<string> SettingCategories { get; } = ["Plugins", "Language"];

    public string CurrentCategoryName => SettingCategories[Math.Clamp(SelectedTabIndex, 0, SettingCategories.Count - 1)];

    public bool IsCurrentCategoryVisibleInSearch =>
        string.IsNullOrWhiteSpace(SettingsSearchText) ||
        FilteredSettingCategories.Contains(CurrentCategoryName);

    public bool IsPluginsCategorySelected => SelectedTabIndex == 0;

    public bool IsLanguageCategorySelected => SelectedTabIndex == 1;

    public bool ShowPluginPathSection =>
        IsPluginsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowPluginListSection =>
        IsPluginsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowPluginActionsSection =>
        IsPluginsCategorySelected &&
        MatchesSearch("actions", "refresh", "configure", "plugin actions");

    public bool ShowPluginInfoSection =>
        IsPluginsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowLanguageSection =>
        IsLanguageCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool HasVisibleCategoryContent =>
        ShowPluginPathSection ||
        ShowPluginListSection ||
        ShowPluginActionsSection ||
        ShowPluginInfoSection ||
        ShowLanguageSection;

    public bool ShowNoSearchResults => !HasVisibleCategoryContent;

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
        DialogAccepted = true;
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

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value < 0)
        {
            SelectedTabIndex = 0;
            return;
        }

        if (value >= SettingCategories.Count)
        {
            SelectedTabIndex = SettingCategories.Count - 1;
            return;
        }

        SelectedSettingCategory = CurrentCategoryName;
        NotifySettingsVisibilityChanged();
    }

    partial void OnSettingsSearchTextChanged(string value)
    {
        RefreshFilteredCategories();
        NotifySettingsVisibilityChanged();
    }

    private bool MatchesSearch(params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(SettingsSearchText))
        {
            return true;
        }

        return terms.Any(term => term.Contains(SettingsSearchText, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedSettingCategoryChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var index = SettingCategories
            .Select((category, index) => new { category, index })
            .FirstOrDefault(item => string.Equals(item.category, value, StringComparison.OrdinalIgnoreCase))
            ?.index;

        if (index is null || index == SelectedTabIndex)
        {
            return;
        }

        SelectedTabIndex = index.Value;
    }

    private void RefreshFilteredCategories()
    {
        FilteredSettingCategories.Clear();
        foreach (var category in SettingCategories.Where(category => MatchesSearch(category)))
        {
            FilteredSettingCategories.Add(category);
        }

        if (FilteredSettingCategories.Count == 0)
        {
            SelectedSettingCategory = null;
            return;
        }

        if (SelectedSettingCategory is null ||
            !FilteredSettingCategories.Contains(SelectedSettingCategory))
        {
            SelectedSettingCategory = FilteredSettingCategories[0];
        }
    }

    private void NotifySettingsVisibilityChanged()
    {
        OnPropertyChanged(nameof(CurrentCategoryName));
        OnPropertyChanged(nameof(IsCurrentCategoryVisibleInSearch));
        OnPropertyChanged(nameof(IsPluginsCategorySelected));
        OnPropertyChanged(nameof(IsLanguageCategorySelected));
        OnPropertyChanged(nameof(ShowPluginPathSection));
        OnPropertyChanged(nameof(ShowPluginListSection));
        OnPropertyChanged(nameof(ShowPluginActionsSection));
        OnPropertyChanged(nameof(ShowPluginInfoSection));
        OnPropertyChanged(nameof(ShowLanguageSection));
        OnPropertyChanged(nameof(HasVisibleCategoryContent));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }
}
