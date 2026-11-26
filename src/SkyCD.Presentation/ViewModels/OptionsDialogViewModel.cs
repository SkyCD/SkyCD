using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Globalization;
using SkyCD.SDK.Localization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Plugins;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel : ObservableObject, IDisposable
{
    private const string PluginsSettingsCategoryId = "plugins-settings";
    private const string McpCategoryId = "mcp";
    private const int McpCategoryIndex = 2;

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

    public OptionsDialogViewModel(IEnumerable<string> availableLanguages, TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
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
        InitializeAiProviderLanguageDefault();
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
    public ObservableCollection<OptionsSidebarEntry> SidebarEntries { get; } = [];
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

    [ObservableProperty] private OptionsSidebarEntry? selectedSidebarEntry;

    private bool isSynchronizingSidebarSelection;

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
            var pluginsGroup = LocalizationService.Current.Get(TextKeys.Plugins);
            // Presentation settings share one UI group. Its members stay contiguous so the sidebar
            // renders the whole group at the position of its first entry.
            var uiGroup = LocalizationService.Current.Get(UiKeys.UiGroup);
            List<OptionsSettingCategory> categories =
            [
                new("plugins", LocalizationService.Current.Get(UiKeys.Installed), 0,
                    Group: pluginsGroup),
                new("language", LocalizationService.Current.Get(TextKeys.Language), 1),
                new("statuses", LocalizationService.Current.Get(TextKeys.Statuses), 3),
                new("custom-properties", LocalizationService.Current.Get(UiKeys.CustomProperties), 4)
            ];

            if (CollectionsPage is not null)
            {
                categories.Add(new(CollectionsCategoryId, LocalizationService.Current.Get(UiKeys.Collections),
                    CollectionsCategoryIndex));
            }

            // Account connectors live with the other catalog data pages and stay listed even when
            // no account plugin is enabled, so the Add To List empty state can link here.
            categories.Add(new(AccountsCategoryId, LocalizationService.Current.Get(UiKeys.Accounts),
                AccountsCategoryIndex));

            // Plugin-contributed pages sit next to Collections so location-style management surfaces live
            // with the other catalog data pages rather than after the settings/plugin groups.
            categories.AddRange(contributedPages.Values
                .Where(page => Plugins.Any(plugin =>
                    plugin.IsEnabled && plugin.Id.Equals(page.PluginId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static page => page.Order)
                .ThenBy(static page => page.Title, StringComparer.OrdinalIgnoreCase)
                .Select(page => new OptionsSettingCategory(page.Id, page.Title, ContributedPage: page)));

            categories.AddRange(
            [
                new("scan", LocalizationService.Current.Get(TextKeys.Scan), 6),
                new("details-columns", LocalizationService.Current.Get(UiKeys.DetailsColumns), 7,
                    Group: uiGroup),
                new(ToolbarCategoryId, LocalizationService.Current.Get(UiKeys.Toolbar), ToolbarCategoryIndex,
                    Group: uiGroup)
            ]);

            // Every AI surface shares one appended group. MCP and ACP agents keep their historical
            // built-in indexes but are appended here so the group stays below the always-present pages.
            var aiGroup = LocalizationService.Current.Get(UiKeys.AiGroup);
            if (IsAiDescriptionAvailable)
            {
                categories.Add(new("ai-description", LocalizationService.Current.Get(UiKeys.AiDescriptionShort),
                    AiDescriptionCategoryIndex, Group: aiGroup));
                categories.Add(new("ai-provider", LocalizationService.Current.Get(UiKeys.AiProvider),
                    AiProviderCategoryIndex, Group: aiGroup));
            }

            categories.Add(new(McpCategoryId, LocalizationService.Current.Get(TextKeys.MCP), McpCategoryIndex,
                Group: aiGroup));
            categories.Add(new(AcpCategoryId, LocalizationService.Current.Get(UiKeys.AcpAgents), AcpCategoryIndex,
                Group: aiGroup));

            // Tool exposure applies to the MCP server itself, so the page stays available without AI plugins.
            categories.Add(new(McpToolsCategoryId, LocalizationService.Current.Get(UiKeys.McpTools),
                McpToolsCategoryIndex, Group: aiGroup));

            categories.AddRange(pluginPages.Values
                .Where(page => Plugins.Any(plugin => plugin.IsEnabled && plugin.Id.Equals(page.Registration.PluginId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(page => page.Registration.PluginName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
                .Select(page => new OptionsSettingCategory(
                    page.Id,
                    page.Registration.Descriptor.Title,
                    PluginPage: page,
                    Group: NormalizeGroup(page.Registration.Descriptor.Group))));

            // The settings page lives in the Plugins group; keeping it last keeps the legacy index mapping intact.
            categories.Add(new(PluginsSettingsCategoryId, LocalizationService.Current.Get(UiKeys.Settings),
                Group: pluginsGroup));
            return categories;
        }
    }

    private static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    private OptionsSettingCategory CurrentCategory => SettingCategories[Math.Clamp(SelectedTabIndex, 0, SettingCategories.Count - 1)];
    private int SelectedBuiltInIndex => CurrentCategory.BuiltInIndex;
    public string CurrentCategoryName => CurrentCategory.Title;
    public string CurrentCategoryPath => CurrentCategory.Group is { } group
        ? $"{group}  >  {CurrentCategory.Title}"
        : CurrentCategory.Title;
    public OptionsPluginPage? SelectedPluginPage => CurrentCategory.PluginPage;
    public bool ShowPluginSettingsSection =>
        SelectedPluginPage is not null && IsCurrentCategoryVisibleInSearch && !IsMarketplaceBrowseSelected;

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

    public bool IsMcpCategorySelected => SelectedBuiltInIndex == McpCategoryIndex;
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
        ShowDetailsColumnsSection || ShowToolbarSection ||
        ShowAiDescriptionSection || ShowAiProviderSection || ShowMcpToolsSection ||
        ShowPluginSettingsSection || ShowPluginsSettingsSection ||
        ShowCollectionsSection || ShowAccountsSection || ShowContributedOptionsPageSection;

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
        ExternalLinkLauncher.TryOpen(SelectedPlugin?.AuthorUrl);
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedPluginProjectUrl))]
    private void OpenSelectedPluginProjectUrl()
    {
        ExternalLinkLauncher.TryOpen(SelectedPlugin?.ProjectUrl);
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
        if (!TryCommitPluginRepositories()) return;
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
        return ExternalLinkLauncher.IsLaunchable(SelectedPlugin?.AuthorUrl);
    }

    private bool CanOpenSelectedPluginProjectUrl()
    {
        return ExternalLinkLauncher.IsLaunchable(SelectedPlugin?.ProjectUrl);
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
        NotifyPluginsPageChanged();
        InfoMessage = LocalizationService.Current.Format(TextKeys.LoadedPluginS, Plugins.Count);
        OnPropertyChanged(nameof(SelectedPluginDescription));
        OnPropertyChanged(nameof(HasPluginDescription));
    }

    /// <summary>Distinguishes a real plugin description from the localized placeholder in the Overview tab.</summary>
    public bool HasPluginDescription => !string.IsNullOrWhiteSpace(SelectedPlugin?.Description);

    public string SelectedPluginDescription =>
        HasPluginDescription
            ? SelectedPlugin!.Description
            : LocalizationService.Current.Get(UiKeys.NoPluginDescription);

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
        OnPropertyChanged(nameof(HasPluginDescription));
        OpenSelectedPluginAuthorUrlCommand.NotifyCanExecuteChanged();
        OpenSelectedPluginProjectUrlCommand.NotifyCanExecuteChanged();
        if (value is not null && !pluginSelectionIsAuto) IsMarketplaceBrowseSelected = false;
        NotifySelectedPluginDetailsChanged();
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

        // Group labels carry the expanded abbreviations ("User Interface (UI)",
        // "Artificial Intelligence (AI)"), so a search for the abbreviation keeps the group's pages.
        if (category.Group is { } group && IsExpandedAbbreviationGroup(group) && MatchesSearch(group))
        {
            return true;
        }

        if (category.Id == "plugins")
        {
            return MatchesSearch(
                LocalizationService.Current.Get(TextKeys.Plugins),
                LocalizationService.Current.Get(UiKeys.Installed),
                "plugin",
                "installed");
        }

        if (category.Id == ToolbarCategoryId)
        {
            return MatchesSearch(
                LocalizationService.Current.Get(UiKeys.CustomizeToolbar),
                LocalizationService.Current.Get(UiKeys.CustomizeToolbarHint),
                LocalizationService.Current.Get(UiKeys.ToolbarCommands),
                LocalizationService.Current.Get(UiKeys.ToolbarButtons));
        }

        if (category.Id == McpToolsCategoryId)
        {
            return MatchesSearch(
                LocalizationService.Current.Get(UiKeys.McpTools),
                LocalizationService.Current.Get(UiKeys.McpServer),
                "tools",
                "exposed");
        }

        return false;
    }

    private static bool IsExpandedAbbreviationGroup(string group) =>
        string.Equals(group, LocalizationService.Current.Get(UiKeys.UiGroup), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(group, LocalizationService.Current.Get(UiKeys.AiGroup), StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedSettingCategoryChanged(OptionsSettingCategory? value)
    {
        if (isRefreshingLocalization || value is null) return;
        var index = SettingCategories.ToList().FindIndex(category => category.Id == value.Id);
        if (index >= 0) SelectedTabIndex = index;
        SynchronizeSidebarSelection(value);
        NotifySettingsVisibilityChanged();
        EnsureMarketplaceSnapshot();
    }

    partial void OnSelectedSidebarEntryChanged(OptionsSidebarEntry? value)
    {
        if (isSynchronizingSidebarSelection || value is null) return;
        if (value.IsGroupHeader)
        {
            var firstChild = SidebarEntries
                .SkipWhile(entry => !ReferenceEquals(entry, value))
                .Skip(1)
                .TakeWhile(entry => !entry.IsGroupHeader)
                .FirstOrDefault();
            if (firstChild?.Category is not { } child) return;
            isSynchronizingSidebarSelection = true;
            try { SelectedSidebarEntry = firstChild; } finally { isSynchronizingSidebarSelection = false; }
            SelectedSettingCategory = child;
            return;
        }

        if (value.Category is { } category)
        {
            SelectedSettingCategory = category;
        }
    }

    private void SynchronizeSidebarSelection(OptionsSettingCategory? category)
    {
        if (isSynchronizingSidebarSelection) return;
        var entry = category is null
            ? null
            : SidebarEntries.FirstOrDefault(item => item.Category is { } candidate && candidate.Id == category.Id);
        if (ReferenceEquals(entry, SelectedSidebarEntry)) return;
        isSynchronizingSidebarSelection = true;
        try { SelectedSidebarEntry = entry; } finally { isSynchronizingSidebarSelection = false; }
    }

    private void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SettingCategories));
        McpCopyTooltip = LocalizationService.Current.Get(TextKeys.CopyURL);
        RefreshAiProviderLanguages();
        RefreshScanLocalization();
        RefreshDetailsColumnsLocalization();
        RefreshCategoriesPreservingSelection();
        NotifySelectedPluginDetailsChanged();
        NotifyMcpToolsLocalizationChanged();
        RefreshPluginRepositoryLocalization();
        RelocalizeAccounts();
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

        OnPropertyChanged(nameof(InstalledPluginsHeader));
        OnPropertyChanged(nameof(InstalledPluginsCount));
        OnPropertyChanged(nameof(ShowInstalledPluginsEmptyState));
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
        RefreshSidebarEntries();
    }

    private void RefreshSidebarEntries()
    {
        var entries = new List<OptionsSidebarEntry>();
        var emittedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in FilteredSettingCategories)
        {
            if (category.Group is { } group)
            {
                if (!emittedGroups.Add(group)) continue;
                entries.Add(new OptionsSidebarEntry(group, null));
                entries.AddRange(FilteredSettingCategories
                    .Where(child => string.Equals(child.Group, group, StringComparison.OrdinalIgnoreCase))
                    .Select(child => new OptionsSidebarEntry(child.Title, child, Depth: 1)));
            }
            else
            {
                entries.Add(new OptionsSidebarEntry(category.Title, category));
            }
        }

        SidebarEntries.Clear();
        foreach (var entry in entries) SidebarEntries.Add(entry);
        SynchronizeSidebarSelection(SelectedSettingCategory);
    }

    private void NotifySettingsVisibilityChanged()
    {
        OnPropertyChanged(nameof(SelectedPluginPage));
        OnPropertyChanged(nameof(ShowPluginSettingsSection));
        OnPropertyChanged(nameof(CurrentCategoryName));
        OnPropertyChanged(nameof(CurrentCategoryPath));
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
        OnPropertyChanged(nameof(IsCollectionsCategorySelected));
        OnPropertyChanged(nameof(ShowCollectionsSection));
        OnPropertyChanged(nameof(IsAccountsCategorySelected));
        OnPropertyChanged(nameof(ShowAccountsSection));
        OnPropertyChanged(nameof(SelectedContributedPage));
        OnPropertyChanged(nameof(ShowContributedOptionsPageSection));
        OnPropertyChanged(nameof(IsDetailsColumnsCategorySelected));
        OnPropertyChanged(nameof(ShowDetailsColumnsSection));
        OnPropertyChanged(nameof(IsToolbarCategorySelected));
        OnPropertyChanged(nameof(ShowToolbarSection));
        OnPropertyChanged(nameof(ShowPluginListSection));
        OnPropertyChanged(nameof(ShowPluginActionsSection));
        OnPropertyChanged(nameof(ShowPluginInfoSection));
        OnPropertyChanged(nameof(ShowPluginViewSwitcher));
        OnPropertyChanged(nameof(ShowInstalledPluginPathSection));
        OnPropertyChanged(nameof(ShowInstalledPluginListSection));
        OnPropertyChanged(nameof(ShowInstalledPluginInfoSection));
        OnPropertyChanged(nameof(ShowMarketplaceSection));
        OnPropertyChanged(nameof(ShowMarketplaceEmptyState));
        OnPropertyChanged(nameof(ShowLanguageSection));
        OnPropertyChanged(nameof(HasVisibleCategoryContent));
        OnPropertyChanged(nameof(ShowAcpSection));
        OnPropertyChanged(nameof(IsScanCategorySelected));
        OnPropertyChanged(nameof(ShowScanSection));
        OnPropertyChanged(nameof(IsAiDescriptionCategorySelected));
        OnPropertyChanged(nameof(ShowAiDescriptionSection));
        OnPropertyChanged(nameof(IsAiProviderCategorySelected));
        OnPropertyChanged(nameof(ShowAiProviderSection));
        OnPropertyChanged(nameof(IsMcpToolsCategorySelected));
        OnPropertyChanged(nameof(ShowMcpToolsSection));
        OnPropertyChanged(nameof(IsPluginsSettingsCategorySelected));
        OnPropertyChanged(nameof(ShowPluginsSettingsSection));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    private bool CanRemoveStatusVariant()
    {
        return SelectedStatusVariant is not null;
    }
}
