using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Presentation.Plugins;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    /// <summary>Index of the AI provider category; it follows the AI description category.</summary>
    private const int AiProviderCategoryIndex = AiDescriptionCategoryIndex + 1;

    private IAiProviderSettingsCapability? aiProviderCapability;
    private AiProviderSettings aiProviderSavedSettings = new();
    private bool isApplyingAiProviderSettings;

    public ObservableCollection<AiDescriptionImplementationChoice> AiDescriptionImplementations { get; } = [];

    [ObservableProperty] private AiDescriptionImplementationChoice? selectedAiDescriptionImplementation;

    public ObservableCollection<AiProviderChoice> AiProviders { get; } = [];
    public ObservableCollection<string> AiProviderProtocols { get; } = ["http", "https"];
    public ObservableCollection<string> AiProviderModels { get; } = [];
    public ObservableCollection<string> AiProviderLanguages { get; } = [];

    [ObservableProperty] private AiProviderChoice? selectedAiProvider;
    [ObservableProperty] private string aiProviderProtocol = "https";
    [ObservableProperty] private string aiProviderAddress = string.Empty;
    [ObservableProperty] private int aiProviderPort = 443;
    [ObservableProperty] private string aiProviderApiKey = string.Empty;
    [ObservableProperty] private string aiProviderEndpointUrl = string.Empty;
    [ObservableProperty] private string aiProviderModel = string.Empty;
    [ObservableProperty] private string aiProviderLanguage = "English";
    [ObservableProperty] private bool aiProviderResizeLargeImages = true;
    [ObservableProperty] private bool aiProviderCanStartServer;
    [ObservableProperty] private string aiProviderConnectionStatus = string.Empty;
    [ObservableProperty] private string aiProviderAlertMessage = string.Empty;
    [ObservableProperty] private bool isAiProviderBusy;

    public bool IsAiProviderImplementationSelected => string.Equals(
        SelectedAiDescriptionImplementation?.Id, AiDescriptionImplementationIds.Provider,
        StringComparison.OrdinalIgnoreCase);

    public bool ShowAiDescriptionImplementationSection => AiDescriptionImplementations.Count > 1;

    public bool IsAiProviderCategorySelected => SelectedBuiltInIndex == AiProviderCategoryIndex;

    public bool ShowAiProviderSection =>
        IsAiDescriptionAvailable &&
        IsAiProviderCategorySelected &&
        IsCurrentCategoryVisibleInSearch &&
        aiProviderCapability is not null;

    public bool ShowAiProviderAlert => !string.IsNullOrWhiteSpace(AiProviderAlertMessage);

    public bool CanStartAiProviderServer => AiProviderCanStartServer && !IsAiProviderBusy;

    public bool CanUseAiProviderActions => !IsAiProviderBusy && aiProviderCapability is not null;

    /// <summary>Help page for the selected provider's API key, or null when it has no key concept.</summary>
    public string? AiProviderApiKeyHelpUrl => SelectedAiProvider?.ApiKeyHelpUrl;

    public bool ShowAiProviderApiKeyHelp => ExternalLinkLauncher.IsLaunchable(AiProviderApiKeyHelpUrl);

    /// <summary>Loads the installed implementations and restores the host-selected one when still available.</summary>
    public void SetAiDescriptionImplementations(IEnumerable<AiDescriptionImplementationChoice> implementations,
        string? selectedId)
    {
        AiDescriptionImplementations.Clear();
        foreach (var implementation in implementations.DistinctBy(
                     static implementation => implementation.Id, StringComparer.OrdinalIgnoreCase))
        {
            AiDescriptionImplementations.Add(implementation);
        }

        SelectedAiDescriptionImplementation = AiDescriptionImplementations.FirstOrDefault(implementation =>
                                                string.Equals(implementation.Id, selectedId,
                                                    StringComparison.OrdinalIgnoreCase))
                                            ?? AiDescriptionImplementations.FirstOrDefault();
        OnPropertyChanged(nameof(ShowAiDescriptionImplementationSection));
    }

    /// <summary>Reads saved provider settings; a missing or unreadable configuration leaves defaults in place.</summary>
    public void LoadAiProviderSettings(IAiProviderSettingsCapability? capability)
    {
        aiProviderCapability = capability;
        AiProviders.Clear();
        AiProviderModels.Clear();
        AiProviderLanguages.Clear();
        AiProviderAlertMessage = string.Empty;
        if (capability is null)
        {
            aiProviderSavedSettings = new AiProviderSettings();
            NotifyAiProviderVisibilityChanged();
            return;
        }

        foreach (var provider in capability.Providers)
        {
            AiProviders.Add(provider);
        }

        AiProviderLanguages.Clear();
        foreach (var language in Languages.Select(static language => language.Name)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AiProviderLanguages.Add(language);
        }

        try
        {
            ApplyAiProviderSettings(capability.GetSettings());
        }
        catch (Exception exception) when (IsAiProviderFailure(exception))
        {
            AiProviderAlertMessage = LocalizationService.Current.Format(UiKeys.AiProviderLoadError, exception.Message);
            ApplyAiProviderSettings(new AiProviderSettings());
        }

        NotifyAiProviderVisibilityChanged();
    }

    /// <summary>
    /// Defaults the output language to the active UI language. Called from the constructor after
    /// <see cref="OptionsDialogViewModel.Languages"/> is populated, so a fresh profile opens with
    /// a valid selection instead of the hard-coded English fallback.
    /// </summary>
    private void InitializeAiProviderLanguageDefault()
    {
        AiProviderLanguage = GetUiAiProviderLanguage();
    }

    /// <summary>
    /// Rebuilds the output-language list after a UI language change and keeps the selection valid:
    /// the current value is re-mapped onto the new list instead of being cleared.
    /// </summary>
    private void RefreshAiProviderLanguages()
    {
        if (AiProviderLanguages.Count == 0)
        {
            return;
        }

        var current = AiProviderLanguage;
        AiProviderLanguages.Clear();
        foreach (var language in Languages.Select(static language => language.Name)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AiProviderLanguages.Add(language);
        }

        var resolved = ResolveAiProviderLanguage(current);
        if (!AiProviderLanguages.Contains(resolved, StringComparer.OrdinalIgnoreCase))
        {
            AiProviderLanguages.Add(resolved);
        }

        AiProviderLanguage = resolved;
    }

    /// <summary>Display name of the active UI culture as listed in <see cref="AiProviderLanguages"/>.</summary>
    private string GetUiAiProviderLanguage()
    {
        var culture = localizationService?.CurrentCulture ?? LocalizationService.Current.CurrentCulture;
        foreach (var language in Languages)
        {
            if (LanguageCultureMatches(language.Name, language.CultureCode, culture))
            {
                return language.Name;
            }
        }

        return Languages.FirstOrDefault()?.Name ?? AiProviderLanguage;
    }

    private static bool LanguageCultureMatches(string displayName, string cultureCode, CultureInfo culture)
    {
        if (!string.IsNullOrWhiteSpace(cultureCode) &&
            string.Equals(cultureCode.Trim(), culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryResolveLanguageCulture(displayName) is { } resolved &&
               string.Equals(resolved.Name, culture.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps a saved language value to a listed entry: exact match first, then a culture-based
    /// match (so a plain <c>English</c> finds <c>English (US)</c>), otherwise the value is
    /// returned verbatim so the existing append-if-unknown behavior keeps working.
    /// </summary>
    private string ResolveAiProviderLanguage(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            return GetUiAiProviderLanguage();
        }

        var trimmed = saved.Trim();
        var exact = AiProviderLanguages.FirstOrDefault(entry =>
            string.Equals(entry, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        if (TryResolveLanguageCulture(trimmed) is { } savedCulture)
        {
            var byCulture = AiProviderLanguages.FirstOrDefault(entry =>
                TryResolveLanguageCulture(entry) is { } entryCulture &&
                string.Equals(entryCulture.Name, savedCulture.Name, StringComparison.OrdinalIgnoreCase));
            if (byCulture is not null)
            {
                return byCulture;
            }
        }

        return trimmed;
    }

    private static readonly Lazy<IReadOnlyList<CultureInfo>> SpecificCultures = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures));

    /// <summary>
    /// Resolves a language name to a culture: plain English/Lithuanian names, parenthesized or
    /// dashed display variants (<c>English (US)</c>), culture codes, or the culture whose
    /// English or native name matches. Returns null when nothing matches.
    /// </summary>
    private static CultureInfo? TryResolveLanguageCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        if (normalized.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("en-US");
        }

        if (normalized.Equals("Lithuanian", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("lt-LT");
        }

        var stripped = StripLanguageSuffix(normalized);
        if (!stripped.Equals(normalized, StringComparison.Ordinal))
        {
            var byBase = TryResolveLanguageCulture(stripped);
            if (byBase is not null)
            {
                return byBase;
            }
        }

        if (LanguageCultureResolverLooksLikeCode(normalized))
        {
            try
            {
                return CultureInfo.GetCultureInfo(normalized);
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        foreach (var culture in SpecificCultures.Value)
        {
            if (MatchesCultureName(normalized, culture))
            {
                return culture;
            }
        }

        return null;
    }

    private static bool MatchesCultureName(string normalized, CultureInfo culture)
    {
        return MatchesCandidateName(normalized, culture.Name) ||
               MatchesCandidateName(normalized, culture.EnglishName) ||
               MatchesCandidateName(normalized, culture.NativeName);
    }

    private static bool MatchesCandidateName(string normalized, string candidate)
    {
        return normalized.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(StripLanguageSuffix(candidate), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripLanguageSuffix(string name)
    {
        var bracket = name.IndexOf('(');
        if (bracket > 0)
        {
            return name[..bracket].Trim();
        }

        var dash = name.IndexOf('-');
        return dash > 0 ? name[..dash].Trim() : name;
    }

    private static bool LanguageCultureResolverLooksLikeCode(string value)
    {
        foreach (var part in value.Split('-'))
        {
            if (part.Length < 2)
            {
                return false;
            }

            foreach (var character in part)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    return false;
                }
            }
        }

        return value.Contains('-', StringComparison.Ordinal) || value.Length is 2 or 3;
    }

    private void ApplyAiProviderSettings(AiProviderSettings settings)
    {
        aiProviderSavedSettings = settings;
        isApplyingAiProviderSettings = true;
        try
        {
            SelectedAiProvider = AiProviders.FirstOrDefault(provider =>
                                    string.Equals(provider.Id, settings.ProviderId, StringComparison.OrdinalIgnoreCase))
                                ?? AiProviders.FirstOrDefault();
            AiProviderProtocol = settings.Protocol;
            AiProviderAddress = settings.Address;
            AiProviderPort = settings.Port;
            AiProviderApiKey = settings.ApiKey;
            AiProviderModel = settings.Model;
            AddAiProviderModel(settings.Model);
            var resolvedLanguage = ResolveAiProviderLanguage(settings.Language);
            if (!AiProviderLanguages.Contains(resolvedLanguage, StringComparer.OrdinalIgnoreCase))
            {
                AiProviderLanguages.Add(resolvedLanguage);
            }

            AiProviderLanguage = resolvedLanguage;
            AiProviderResizeLargeImages = settings.ResizeLargeImages;
            AiProviderCanStartServer = SelectedAiProvider?.CanStartServer == true;
            AiProviderConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderNotChecked);
        }
        finally
        {
            isApplyingAiProviderSettings = false;
        }

        UpdateAiProviderEndpointUrl();
    }

    private AiProviderSettings BuildAiProviderSettings(bool enabled)
    {
        return aiProviderSavedSettings with
        {
            ProviderId = SelectedAiProvider?.Id ?? aiProviderSavedSettings.ProviderId,
            Protocol = AiProviderProtocol,
            Address = AiProviderAddress?.Trim() ?? string.Empty,
            Port = AiProviderPort,
            ApiKey = AiProviderApiKey ?? string.Empty,
            Model = AiProviderModel?.Trim() ?? string.Empty,
            Language = AiProviderLanguage,
            ResizeLargeImages = AiProviderResizeLargeImages,
            Enabled = enabled
        };
    }

    private void UpdateAiProviderEndpointUrl()
    {
        var settings = BuildAiProviderSettings(enabled: false);
        AiProviderEndpointUrl = aiProviderCapability?.BuildEndpointUrl(settings)
                                ?? $"{settings.Protocol}://{settings.Address}:{settings.Port}/v1";
    }

    private void AddAiProviderModel(string? model)
    {
        if (!string.IsNullOrWhiteSpace(model) &&
            !AiProviderModels.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            AiProviderModels.Add(model);
        }
    }

    private void ReplaceAiProviderModels(IReadOnlyList<string> models)
    {
        AiProviderModels.Clear();
        foreach (var model in models)
        {
            AddAiProviderModel(model);
        }

        if (!AiProviderModels.Contains(AiProviderModel, StringComparer.OrdinalIgnoreCase))
        {
            AiProviderModel = AiProviderModels.FirstOrDefault() ?? AiProviderModel;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAiProviderActions))]
    private async Task CheckAiProviderConnectionAsync()
    {
        if (aiProviderCapability is null)
        {
            return;
        }

        IsAiProviderBusy = true;
        AiProviderAlertMessage = string.Empty;
        AiProviderConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderChecking);
        try
        {
            var result = await aiProviderCapability.CheckConnectionAsync(BuildAiProviderSettings(enabled: true),
                CancellationToken.None);
            AiProviderConnectionStatus = result.Message;
            if (result.IsConnected)
            {
                ReplaceAiProviderModels(result.Models);
            }
            else
            {
                AiProviderAlertMessage = result.Message;
            }
        }
        catch (Exception exception) when (IsAiProviderFailure(exception))
        {
            AiProviderConnectionStatus = exception.Message;
            AiProviderAlertMessage = exception.Message;
        }
        finally
        {
            IsAiProviderBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAiProviderActions))]
    private async Task RefreshAiProviderModelsAsync()
    {
        if (aiProviderCapability is null)
        {
            return;
        }

        IsAiProviderBusy = true;
        AiProviderAlertMessage = string.Empty;
        try
        {
            var models = await aiProviderCapability.ListModelsAsync(BuildAiProviderSettings(enabled: true),
                CancellationToken.None);
            ReplaceAiProviderModels(models);
            AiProviderConnectionStatus = models.Count == 0
                ? LocalizationService.Current.Get(UiKeys.AiProviderNoModels)
                : LocalizationService.Current.Format(UiKeys.AiProviderModelsLoaded, models.Count);
        }
        catch (Exception exception) when (IsAiProviderFailure(exception))
        {
            AiProviderAlertMessage = exception.Message;
        }
        finally
        {
            IsAiProviderBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartAiProviderServer))]
    private async Task StartAiProviderServerAsync()
    {
        if (aiProviderCapability is null)
        {
            return;
        }

        IsAiProviderBusy = true;
        AiProviderAlertMessage = string.Empty;
        try
        {
            var error = await aiProviderCapability.StartServerAsync(BuildAiProviderSettings(enabled: true),
                CancellationToken.None);
            if (error is null)
            {
                AiProviderConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderServerStarted);
            }
            else
            {
                AiProviderAlertMessage = error;
            }
        }
        catch (Exception exception) when (IsAiProviderFailure(exception))
        {
            AiProviderAlertMessage = exception.Message;
        }
        finally
        {
            IsAiProviderBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenAiProviderApiKeyHelp))]
    private void OpenAiProviderApiKeyHelp()
    {
        ExternalLinkLauncher.TryOpen(AiProviderApiKeyHelpUrl);
    }

    private bool CanOpenAiProviderApiKeyHelp() => ExternalLinkLauncher.IsLaunchable(AiProviderApiKeyHelpUrl);

    private bool TrySaveAiProviderSettings()
    {
        if (!IsAiProviderImplementationSelected || aiProviderCapability is null)
        {
            return true;
        }

        try
        {
            aiProviderCapability.SaveSettings(BuildAiProviderSettings(enabled: true));
            AiProviderAlertMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsAiProviderFailure(exception))
        {
            SelectedSettingCategory = SettingCategories.FirstOrDefault(category =>
                category.BuiltInIndex == AiProviderCategoryIndex);
            AiProviderAlertMessage = LocalizationService.Current.Format(UiKeys.AiProviderSaveError,
                exception.Message);
            return false;
        }
    }

    private static bool IsAiProviderFailure(Exception exception) => exception is InvalidOperationException
        or IOException or UnauthorizedAccessException or JsonException or ArgumentException
        or NotSupportedException or HttpRequestException or TaskCanceledException;

    partial void OnSelectedAiDescriptionImplementationChanged(AiDescriptionImplementationChoice? value)
    {
        NotifyAiProviderVisibilityChanged();
    }

    partial void OnSelectedAiProviderChanged(AiProviderChoice? value)
    {
        NotifyAiProviderApiKeyHelpChanged();
        if (isApplyingAiProviderSettings || value is null)
        {
            return;
        }

        AiProviderProtocol = value.Protocol;
        AiProviderAddress = value.Address;
        AiProviderPort = value.Port;
        AiProviderCanStartServer = value.CanStartServer;
        if (value.SuggestedModels is { Count: > 0 })
        {
            ReplaceAiProviderModels(value.SuggestedModels);
            AiProviderModel = value.DefaultModel ?? value.SuggestedModels[0];
            AddAiProviderModel(AiProviderModel);
        }
        else if (value.DefaultModel is not null)
        {
            AiProviderModel = value.DefaultModel;
            AddAiProviderModel(value.DefaultModel);
        }

        AiProviderConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderNotChecked);
        AiProviderAlertMessage = string.Empty;
        UpdateAiProviderEndpointUrl();
    }

    partial void OnAiProviderProtocolChanged(string value) => UpdateAiProviderEndpointUrl();

    partial void OnAiProviderAddressChanged(string value) => UpdateAiProviderEndpointUrl();

    partial void OnAiProviderPortChanged(int value)
    {
        if (value < 1)
        {
            AiProviderPort = 1;
            return;
        }

        if (value > 65535)
        {
            AiProviderPort = 65535;
            return;
        }

        UpdateAiProviderEndpointUrl();
    }

    partial void OnAiProviderAlertMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAiProviderAlert));
    }

    partial void OnIsAiProviderBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartAiProviderServer));
        OnPropertyChanged(nameof(CanUseAiProviderActions));
        CheckAiProviderConnectionCommand.NotifyCanExecuteChanged();
        RefreshAiProviderModelsCommand.NotifyCanExecuteChanged();
        StartAiProviderServerCommand.NotifyCanExecuteChanged();
    }

    private void NotifyAiProviderVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsAiProviderImplementationSelected));
        OnPropertyChanged(nameof(IsAiProviderCategorySelected));
        OnPropertyChanged(nameof(ShowAiProviderSection));
        OnPropertyChanged(nameof(ShowAiProviderAlert));
        NotifyAiProviderApiKeyHelpChanged();
    }

    private void NotifyAiProviderApiKeyHelpChanged()
    {
        OnPropertyChanged(nameof(AiProviderApiKeyHelpUrl));
        OnPropertyChanged(nameof(ShowAiProviderApiKeyHelp));
        OpenAiProviderApiKeyHelpCommand.NotifyCanExecuteChanged();
    }
}
