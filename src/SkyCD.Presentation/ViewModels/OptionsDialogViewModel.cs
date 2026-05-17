using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;

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
    public ObservableCollection<StatusVariantItemViewModel> StatusVariants { get; } = [];

    [ObservableProperty] private string pluginPath = string.Empty;

    [ObservableProperty] private OptionsPluginItem? selectedPlugin;

    [ObservableProperty] private LanguageItem selectedLanguage;

    [ObservableProperty] private string infoMessage = string.Empty;

    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty] private int selectedTabIndex;

    [ObservableProperty] private string settingsSearchText = string.Empty;

    [ObservableProperty] private string? selectedSettingCategory;

    [ObservableProperty] private int mcpPort = 8765;

    [ObservableProperty] private bool isMcpServerEnabled = true;

    [ObservableProperty] private bool isMcpStatusIconVisible = true;

    [ObservableProperty] private string mcpCopyTooltip = "Copy URL";

    [ObservableProperty] private string mcpAlertMessage = string.Empty;
    [ObservableProperty] private StatusVariantItemViewModel? selectedStatusVariant;
    [ObservableProperty] private string statusAlertMessage = string.Empty;

    public bool ShowStatusAlert => !string.IsNullOrWhiteSpace(StatusAlertMessage);

    public bool ShowMcpAlert => !string.IsNullOrWhiteSpace(McpAlertMessage);

    public IReadOnlyList<string> SettingCategories { get; } = ["Plugins", "Language", "MCP", "Statuses"];

    public string CurrentCategoryName =>
        SettingCategories[Math.Clamp(SelectedTabIndex, 0, SettingCategories.Count - 1)];

    public bool IsCurrentCategoryVisibleInSearch =>
        string.IsNullOrWhiteSpace(SettingsSearchText) ||
        FilteredSettingCategories.Contains(CurrentCategoryName);

    public bool IsPluginsCategorySelected => SelectedTabIndex == 0;

    public bool IsLanguageCategorySelected => SelectedTabIndex == 1;

    public bool ShowProjectSettingsSection =>
        IsPluginsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowPluginPathSection =>
        ShowProjectSettingsSection;

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

    public bool IsMcpCategorySelected => SelectedTabIndex == 2;
    public bool IsStatusesCategorySelected => SelectedTabIndex == 3;

    public bool ShowMcpSection =>
        IsMcpCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowStatusesSection =>
        IsStatusesCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool HasVisibleCategoryContent =>
        ShowPluginPathSection ||
        ShowPluginListSection ||
        ShowPluginActionsSection ||
        ShowPluginInfoSection ||
        ShowLanguageSection ||
        ShowMcpSection ||
        ShowStatusesSection;

    public string McpBaseUrl => $"http://127.0.0.1:{McpPort}/mcp";

    public bool ShowNoSearchResults => !HasVisibleCategoryContent;

    public event EventHandler? BrowsePluginPathRequested;

    public event EventHandler? RefreshPluginsRequested;

    [RelayCommand]
    private void AddStatusVariant()
    {
        var candidate = new StatusVariantItemViewModel
        {
            Name = "New Status",
            IconGlyph = string.Empty
        };
        StatusVariants.Add(candidate);
        SelectedStatusVariant = candidate;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveStatusVariant))]
    private void RemoveStatusVariant()
    {
        if (SelectedStatusVariant is null)
        {
            return;
        }

        StatusVariants.Remove(SelectedStatusVariant);
        SelectedStatusVariant = StatusVariants.FirstOrDefault();
    }

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

    [RelayCommand(CanExecute = nameof(CanOpenSelectedPluginAuthorUrl))]
    private void OpenSelectedPluginAuthorUrl()
    {
        if (SelectedPlugin is null || string.IsNullOrWhiteSpace(SelectedPlugin.AuthorUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedPlugin.AuthorUrl,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void Confirm()
    {
        if (StatusVariants.Any(static status => string.IsNullOrWhiteSpace(status.IconGlyph)))
        {
            StatusAlertMessage = "All status items must have an icon selected.";
            return;
        }

        StatusAlertMessage = string.Empty;
        DialogAccepted = true;
    }

    private bool CanConfigure()
    {
        return SelectedPlugin is not null && SelectedPlugin.SupportsConfiguration;
    }

    private bool CanOpenSelectedPluginAuthorUrl()
    {
        return SelectedPlugin is not null && SelectedPlugin.HasAuthorUrl;
    }

    public void SetPlugins(IEnumerable<OptionsPluginItem> plugins)
    {
        var snapshot = plugins.ToArray();
        Plugins.Clear();
        foreach (var plugin in snapshot)
        {
            if (disabledPluginIds.Contains(plugin.Id))
            {
                plugin.IsEnabled = false;
            }

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

    public void SetStatusVariants(IEnumerable<StatusVariantDocument>? variants)
    {
        StatusVariants.Clear();
        foreach (var variant in (variants ?? [])
                     .Where(static variant => !string.IsNullOrWhiteSpace(variant.Name))
                     .Select(StatusVariantItemViewModel.FromDocument))
        {
            StatusVariants.Add(variant);
        }

        SelectedStatusVariant = StatusVariants.FirstOrDefault();
        StatusAlertMessage = string.Empty;
        RemoveStatusVariantCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<StatusVariantDocument> GetStatusVariants()
    {
        return StatusVariants
            .Select(static variant => variant.ToDocument())
            .Where(static variant => !string.IsNullOrWhiteSpace(variant.Name))
            .ToArray();
    }

    partial void OnSelectedPluginChanged(OptionsPluginItem? value)
    {
        ConfigurePluginCommand.NotifyCanExecuteChanged();
        OpenSelectedPluginAuthorUrlCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedStatusVariantChanged(StatusVariantItemViewModel? value)
    {
        RemoveStatusVariantCommand.NotifyCanExecuteChanged();
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

    partial void OnMcpPortChanged(int value)
    {
        if (value < 1)
        {
            McpPort = 1;
            return;
        }

        if (value > 65535)
        {
            McpPort = 65535;
            return;
        }

        OnPropertyChanged(nameof(McpBaseUrl));
    }

    partial void OnMcpAlertMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowMcpAlert));
    }

    partial void OnStatusAlertMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowStatusAlert));
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
        OnPropertyChanged(nameof(IsMcpCategorySelected));
        OnPropertyChanged(nameof(IsStatusesCategorySelected));
        OnPropertyChanged(nameof(ShowProjectSettingsSection));
        OnPropertyChanged(nameof(ShowPluginPathSection));
        OnPropertyChanged(nameof(ShowMcpSection));
        OnPropertyChanged(nameof(ShowStatusesSection));
        OnPropertyChanged(nameof(ShowPluginListSection));
        OnPropertyChanged(nameof(ShowPluginActionsSection));
        OnPropertyChanged(nameof(ShowPluginInfoSection));
        OnPropertyChanged(nameof(ShowLanguageSection));
        OnPropertyChanged(nameof(HasVisibleCategoryContent));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    private bool CanRemoveStatusVariant()
    {
        return SelectedStatusVariant is not null;
    }
}
