using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Globalization;
using SkyCD.Plugin.Abstractions.Localization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel : ObservableObject, IDisposable
{
    private readonly HashSet<string> disabledPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> pendingPluginStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILocalizationService? localizationService;
    private readonly CultureInfo? originalLanguage;
    private bool isRefreshingLocalization;

    public OptionsDialogViewModel()
        : this(LocalizationService.Current)
    {
    }

    public OptionsDialogViewModel(ILocalizationService localizationService)
        : this(localizationService.GetAvailableLanguages().Select(language => language.DisplayName))
    {
        this.localizationService = localizationService;
        originalLanguage = localizationService.CurrentCulture;
        Languages.Clear();
        foreach (var language in localizationService.GetAvailableLanguages())
        {
            Languages.Add(new LanguageItem(language.DisplayName, language.Flag) { CultureCode = language.CultureCode });
        }

        selectedLanguage = Languages.FirstOrDefault(language =>
            string.Equals(language.CultureCode, originalLanguage.Name, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
    }

    partial void OnSelectedLanguageChanged(LanguageItem value)
    {
        localizationService?.SetLanguage(value.CultureCode);
    }

    public void CompleteLanguageSelection(bool accepted)
    {
        if (!accepted && originalLanguage is not null)
        {
            localizationService?.SetCulture(originalLanguage);
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

        selectedLanguage = Languages[0];
        SetDetailsColumns(settings: null);
        RefreshScanLocalization();
        RefreshFilteredCategories();
        selectedSettingCategory = CurrentCategory;
        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public ObservableCollection<CustomPropertyItemViewModel> CustomProperties { get; } = [];
    [ObservableProperty] private CustomPropertyItemViewModel? selectedCustomProperty;
    [ObservableProperty] private string customPropertyAlertMessage = string.Empty;
    public bool ShowCustomPropertiesSection => SelectedBuiltInIndex == 4 && IsCurrentCategoryVisibleInSearch;

    public void SetCustomProperties(IEnumerable<CustomPropertyDocument>? properties)
    {
        CustomProperties.Clear();
        foreach (var property in properties ?? [])
        {
            CustomProperties.Add(new CustomPropertyItemViewModel { Name = property.Name, DefaultValue = property.DefaultValue });
        }
        SelectedCustomProperty = CustomProperties.FirstOrDefault();
    }

    public List<CustomPropertyDocument> GetCustomProperties() => CustomProperties.Select(property =>
        new CustomPropertyDocument { Name = property.Name.Trim(), DefaultValue = property.DefaultValue }).ToList();

    [RelayCommand]
    private void AddCustomProperty()
    {
        SelectedCustomProperty = new CustomPropertyItemViewModel();
        CustomProperties.Add(SelectedCustomProperty);
    }

    [RelayCommand]
    private void RemoveCustomProperty()
    {
        if (SelectedCustomProperty is not null)
        {
            CustomProperties.Remove(SelectedCustomProperty);
            SelectedCustomProperty = CustomProperties.FirstOrDefault();
        }
    }

    public ObservableCollection<OptionsPluginItem> Plugins { get; } = [];

    public ObservableCollection<OptionsPluginItem> FilteredPlugins { get; } = [];

    private OptionsPluginItem? pluginSelectionBeforeFilter;
    private bool isAutoSelectingPlugin;
    private bool pluginSelectionIsAuto;

    public ObservableCollection<LanguageItem> Languages { get; } = [];

    public ObservableCollection<OptionsSettingCategory> FilteredSettingCategories { get; } = [];
    public ObservableCollection<StatusVariantItemViewModel> StatusVariants { get; } = [];

    [ObservableProperty] private string pluginPath = string.Empty;

    [ObservableProperty] private OptionsPluginItem? selectedPlugin;

    [ObservableProperty] private LanguageItem selectedLanguage;

    [ObservableProperty] private string infoMessage = string.Empty;

    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty] private int selectedTabIndex;

    [ObservableProperty] private string pluginFilterText = string.Empty;

    [ObservableProperty] private string settingsSearchText = string.Empty;

    [ObservableProperty] private OptionsSettingCategory? selectedSettingCategory;

    [ObservableProperty] private int mcpPort = 8765;

    [ObservableProperty] private bool isMcpServerEnabled = true;

    [ObservableProperty] private bool isMcpStatusIconVisible = true;

    [ObservableProperty] private string mcpCopyTooltip = LocalizationService.Current.Get(TextKeys.CopyURL);

    [ObservableProperty] private string mcpAlertMessage = string.Empty;
    [ObservableProperty] private StatusVariantItemViewModel? selectedStatusVariant;
    [ObservableProperty] private string statusAlertMessage = string.Empty;

    public bool ShowStatusAlert => !string.IsNullOrWhiteSpace(StatusAlertMessage);

    public bool ShowCustomPropertyAlert => !string.IsNullOrWhiteSpace(CustomPropertyAlertMessage);

    public bool ShowMcpAlert => !string.IsNullOrWhiteSpace(McpAlertMessage);

    public IReadOnlyList<OptionsSettingCategory> SettingCategories
    {
        get
        {
            List<OptionsSettingCategory> categories =
            [
                new("plugins", LocalizationService.Current.Get(TextKeys.Plugins), 0),
                new("language", LocalizationService.Current.Get(TextKeys.Language), 1),
                new("mcp", LocalizationService.Current.Get(TextKeys.MCP), 2),
                new("statuses", LocalizationService.Current.Get(TextKeys.Statuses), 3),
                new("custom-properties", LocalizationService.Current.Get(UiKeys.CustomProperties), 4),
                new("acp", LocalizationService.Current.Get(UiKeys.AcpAgents), 5),
                new("scan", LocalizationService.Current.Get(TextKeys.Scan), 6),
                new("details-columns", LocalizationService.Current.Get(UiKeys.DetailsColumns), 7),
                new(ToolbarCategoryId, LocalizationService.Current.Get(UiKeys.Toolbar), ToolbarCategoryIndex)
            ];
            if (IsAiDescriptionAvailable)
            {
                categories.Add(new("ai-description", LocalizationService.Current.Get(UiKeys.AiDescription), AiDescriptionCategoryIndex));
                categories.Add(new("ai-provider", LocalizationService.Current.Get(UiKeys.AiProviderCategory), AiProviderCategoryIndex));
            }

            categories.AddRange(pluginPages.Values
                .Where(page => Plugins.Any(plugin => plugin.IsEnabled && plugin.Id.Equals(page.Registration.PluginId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(page => page.Registration.PluginName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
                .Select(page => new OptionsSettingCategory(page.Id, page.Registration.Descriptor.Title, PluginPage: page)));
            return categories;
        }
    }

    private OptionsSettingCategory CurrentCategory => SettingCategories[Math.Clamp(SelectedTabIndex, 0, SettingCategories.Count - 1)];
    private int SelectedBuiltInIndex => CurrentCategory.BuiltInIndex;
    public string CurrentCategoryName => CurrentCategory.Title;
    public OptionsPluginPage? SelectedPluginPage => CurrentCategory.PluginPage;
    public bool ShowPluginSettingsSection => SelectedPluginPage is not null && IsCurrentCategoryVisibleInSearch;

    public bool IsCurrentCategoryVisibleInSearch =>
        string.IsNullOrWhiteSpace(SettingsSearchText) || FilteredSettingCategories.Any(category => category.Id == CurrentCategory.Id);

    public bool IsPluginsCategorySelected => SelectedBuiltInIndex == 0;

    public bool IsLanguageCategorySelected => SelectedBuiltInIndex == 1;

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
        MatchesSearch("actions", "refresh", "plugin actions",
            LocalizationService.Current.Get(UiKeys.Actions),
            LocalizationService.Current.Get(UiKeys.Refresh));

    public bool ShowPluginInfoSection =>
        IsPluginsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool ShowLanguageSection =>
        IsLanguageCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public bool IsMcpCategorySelected => SelectedBuiltInIndex == 2;
    public bool IsStatusesCategorySelected => SelectedBuiltInIndex == 3;

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
        ShowStatusesSection || ShowCustomPropertiesSection || ShowAcpSection || ShowScanSection ||
        ShowDetailsColumnsSection || ShowToolbarSection || ShowAiDescriptionSection || ShowAiProviderSection ||
        ShowPluginSettingsSection;

    public string McpBaseUrl => $"http://127.0.0.1:{McpPort}/mcp";

    public bool ShowNoSearchResults => !HasVisibleCategoryContent;

    public event EventHandler? BrowsePluginPathRequested;

    public event EventHandler? RefreshPluginsRequested;

    public event EventHandler? ResetStatusVariantsRequested;

    [RelayCommand]
    private void AddStatusVariant()
    {
        var candidate = new StatusVariantItemViewModel
        {
            Name = LocalizationService.Current.Get(TextKeys.NewStatus),
            IconGlyph = string.Empty,
            IconColor = "#FFFFFF"
        };
        candidate.SetTypeSelected(CatalogDocumentType.Media, true);
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

    [RelayCommand]
    private void ResetStatusVariants()
    {
        ResetStatusVariantsRequested?.Invoke(this, EventArgs.Empty);
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
    private async Task ConfirmAsync()
    {
        if (CustomProperties.Any(property => string.IsNullOrWhiteSpace(property.Name)) ||
            CustomProperties.Select(property => property.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != CustomProperties.Count)
        {
            CustomPropertyAlertMessage = LocalizationService.Current.Get(UiKeys.InvalidPropertyNames);
            return;
        }
        CustomPropertyAlertMessage = string.Empty;
        if (StatusVariants.Any(static status => string.IsNullOrWhiteSpace(status.IconGlyph)))
        {
            StatusAlertMessage = LocalizationService.Current.Get(TextKeys.AllStatusItemsMustHaveAnIconSelected);
            return;
        }

        StatusAlertMessage = string.Empty;
        if (ScanExclusionRules.Any(static rule => string.IsNullOrWhiteSpace(rule.Pattern)))
        {
            ScanAlertMessage = LocalizationService.Current.Get(TextKeys.AllRulesNeedPattern);
            return;
        }

        ScanAlertMessage = string.Empty;
        if (AiScanExclusions.Any(static entry => string.IsNullOrWhiteSpace(entry.MimeType)))
        {
            AiDescriptionAlertMessage = LocalizationService.Current.Get(TextKeys.AllMimeTypesNeedValue);
            return;
        }

        AiDescriptionAlertMessage = string.Empty;
        var pluginSettings = await PreparePluginSettingsAsync();
        if (pluginSettings is null || disposed) return;
        if (!TrySaveAiProviderSettings()) return;
        if (!TrySaveAcpSettings()) return;
        if (!SavePluginSettings(pluginSettings)) return;
        if (GetToolbarLayout() is { } toolbarLayout)
        {
            ToolbarLayoutApplied?.Invoke(this, new ToolbarLayoutAppliedEventArgs { Layout = toolbarLayout });
        }

        DialogAccepted = true;
    }

    private bool CanOpenSelectedPluginAuthorUrl()
    {
        return SelectedPlugin is not null && SelectedPlugin.HasAuthorUrl;
    }

    public void SetPlugins(IEnumerable<OptionsPluginItem> plugins)
    {
        pluginSelectionBeforeFilter = null;
        var snapshot = plugins.ToArray();
        foreach (var plugin in Plugins) plugin.PropertyChanged -= OnPluginEnabledChanged;
        Plugins.Clear();
        foreach (var plugin in snapshot)
        {
            if (pendingPluginStates.TryGetValue(plugin.Id, out var enabled))
            {
                plugin.IsEnabled = enabled;
            }
            else if (disabledPluginIds.Contains(plugin.Id))
            {
                plugin.IsEnabled = false;
            }

            Plugins.Add(plugin);
            plugin.PropertyChanged += OnPluginEnabledChanged;
        }

        RefreshCategoriesPreservingSelection();
        RefreshFilteredPlugins();
        InfoMessage = LocalizationService.Current.Format(TextKeys.LoadedPluginS, Plugins.Count);
        OnPropertyChanged(nameof(SelectedPluginDescription));
    }

    public string SelectedPluginDescription =>
        string.IsNullOrWhiteSpace(SelectedPlugin?.Description)
            ? LocalizationService.Current.Get(UiKeys.NoPluginDescription)
            : SelectedPlugin.Description;

    public void SetDisabledPluginIds(IEnumerable<string>? pluginIds)
    {
        disabledPluginIds.Clear();
        pendingPluginStates.Clear();
        foreach (var pluginId in (pluginIds ?? []).Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            disabledPluginIds.Add(pluginId);
        }
        foreach (var plugin in Plugins) plugin.IsEnabled = !disabledPluginIds.Contains(plugin.Id);
    }

    public void CapturePluginStates()
    {
        if (Plugins.Count == 0)
        {
            return;
        }

        disabledPluginIds.Clear();
        pendingPluginStates.Clear();
        foreach (var plugin in Plugins) pendingPluginStates[plugin.Id] = plugin.IsEnabled;
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
        pluginSelectionIsAuto = isAutoSelectingPlugin;
        OnPropertyChanged(nameof(SelectedPluginDescription));
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

        SelectedSettingCategory = CurrentCategory;
        NotifySettingsVisibilityChanged();
    }

    partial void OnPluginFilterTextChanged(string value)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value);
        if (!isEmpty && pluginSelectionBeforeFilter is null)
        {
            pluginSelectionBeforeFilter = SelectedPlugin;
        }

        RefreshFilteredPlugins();

        if (isEmpty && pluginSelectionBeforeFilter is not null)
        {
            var snapshot = pluginSelectionBeforeFilter;
            pluginSelectionBeforeFilter = null;
            if (pluginSelectionIsAuto)
            {
                isAutoSelectingPlugin = true;
                try
                {
                    SelectedPlugin = Plugins.Contains(snapshot) ? snapshot : FilteredPlugins.FirstOrDefault();
                }
                finally
                {
                    isAutoSelectingPlugin = false;
                }
            }
        }
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

    partial void OnCustomPropertyAlertMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowCustomPropertyAlert));
    }

    private bool MatchesSearch(params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(SettingsSearchText))
        {
            return true;
        }

        return terms.Any(term => term.Contains(SettingsSearchText, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesCategorySearch(OptionsSettingCategory category)
    {
        if (MatchesSearch(category.Title))
        {
            return true;
        }

        return category.Id == ToolbarCategoryId &&
               MatchesSearch(
                   LocalizationService.Current.Get(UiKeys.CustomizeToolbar),
                   LocalizationService.Current.Get(UiKeys.CustomizeToolbarHint),
                   LocalizationService.Current.Get(UiKeys.ToolbarCommands),
                   LocalizationService.Current.Get(UiKeys.ToolbarButtons));
    }

    partial void OnSelectedSettingCategoryChanged(OptionsSettingCategory? value)
    {
        if (isRefreshingLocalization || value is null) return;
        var index = SettingCategories.ToList().FindIndex(category => category.Id == value.Id);
        if (index >= 0) SelectedTabIndex = index;
        NotifySettingsVisibilityChanged();
    }

    private void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SettingCategories));
        McpCopyTooltip = LocalizationService.Current.Get(TextKeys.CopyURL);
        RefreshScanLocalization();
        RefreshDetailsColumnsLocalization();
        RefreshCategoriesPreservingSelection();
        NotifySettingsVisibilityChanged();
    }

    private void RefreshFilteredPlugins()
    {
        var filter = PluginFilterText.Trim();
        IEnumerable<OptionsPluginItem> matching = string.IsNullOrEmpty(filter)
            ? Plugins
            : Plugins.Where(plugin => plugin.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        FilteredPlugins.Clear();
        foreach (var plugin in matching)
        {
            FilteredPlugins.Add(plugin);
        }

        if (SelectedPlugin is null || !FilteredPlugins.Contains(SelectedPlugin))
        {
            isAutoSelectingPlugin = true;
            try
            {
                SelectedPlugin = FilteredPlugins.FirstOrDefault();
            }
            finally
            {
                isAutoSelectingPlugin = false;
            }
        }
    }

    private void RefreshFilteredCategories()
    {
        var id = SelectedSettingCategory?.Id ?? CurrentCategory.Id;
        isRefreshingLocalization = true;
        try
        {
            FilteredSettingCategories.Clear();
            foreach (var category in SettingCategories.Where(MatchesCategorySearch))
                FilteredSettingCategories.Add(category);
        }
        finally { isRefreshingLocalization = false; }
        SelectedSettingCategory = FilteredSettingCategories.FirstOrDefault(category => category.Id == id)
                                  ?? FilteredSettingCategories.FirstOrDefault();
    }

    private void NotifySettingsVisibilityChanged()
    {
        OnPropertyChanged(nameof(SelectedPluginPage));
        OnPropertyChanged(nameof(ShowPluginSettingsSection));
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
        OnPropertyChanged(nameof(ShowCustomPropertiesSection));
        OnPropertyChanged(nameof(IsDetailsColumnsCategorySelected));
        OnPropertyChanged(nameof(ShowDetailsColumnsSection));
        OnPropertyChanged(nameof(IsToolbarCategorySelected));
        OnPropertyChanged(nameof(ShowToolbarSection));
        OnPropertyChanged(nameof(ShowPluginListSection));
        OnPropertyChanged(nameof(ShowPluginActionsSection));
        OnPropertyChanged(nameof(ShowPluginInfoSection));
        OnPropertyChanged(nameof(ShowLanguageSection));
        OnPropertyChanged(nameof(HasVisibleCategoryContent));
        OnPropertyChanged(nameof(ShowAcpSection));
        OnPropertyChanged(nameof(IsScanCategorySelected));
        OnPropertyChanged(nameof(ShowScanSection));
        OnPropertyChanged(nameof(IsAiDescriptionCategorySelected));
        OnPropertyChanged(nameof(ShowAiDescriptionSection));
        OnPropertyChanged(nameof(IsAiProviderCategorySelected));
        OnPropertyChanged(nameof(ShowAiProviderSection));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    private bool CanRemoveStatusVariant()
    {
        return SelectedStatusVariant is not null;
    }
}
