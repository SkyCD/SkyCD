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
using SkyCD.Plugin.Ai.Provider.Exceptions;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Ai.Provider.Configuration;

/// <summary>
/// Edits the OpenAI-compatible provider connection shown as a plugin configuration page in Options.
/// Accepting the draft validates and persists it through the plugin, which enables generation.
/// </summary>
public sealed partial class AiProviderSettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly AiProviderPlugin plugin;
    private readonly IReadOnlyDictionary<string, string?> currentSettings;
    private readonly CancellationTokenSource lifetime = new();
    private AiProviderSettings savedSettings = new();
    private bool applying;

    public AiProviderSettingsPageViewModel(AiProviderPlugin plugin,
        IReadOnlyDictionary<string, string?> currentSettings)
    {
        this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        this.currentSettings = currentSettings ?? new Dictionary<string, string?>();

        foreach (var provider in plugin.Providers)
        {
            AiProviders.Add(provider);
        }

        foreach (var language in LocalizationService.Current.GetAvailableLanguages()
                     .Select(static language => language.DisplayName)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AiProviderLanguages.Add(language);
        }

        try
        {
            ApplySettings(plugin.GetSettings());
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            AlertMessage = LocalizationService.Current.Format(UiKeys.AiProviderLoadError, exception.Message);
            ApplySettings(new AiProviderSettings());
        }
    }

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
    [ObservableProperty] private bool canStartServer;
    [ObservableProperty] private string connectionStatus = string.Empty;
    [ObservableProperty] private string alertMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    public bool ShowAlert => !string.IsNullOrWhiteSpace(AlertMessage);

    public bool AiProviderCanStartServer => CanStartServer && !IsBusy;

    public bool CanUseActions => !IsBusy;

    /// <summary>Help page for the selected provider's API key, or null when it has no key concept.</summary>
    public string? ApiKeyHelpUrl => SelectedAiProvider?.ApiKeyHelpUrl;

    public bool ShowApiKeyHelp => IsLaunchable(ApiKeyHelpUrl);

    /// <summary>Validates the draft, persists it through the plugin and returns the host's save outcome.</summary>
    public PluginConfigurationResult Accept()
    {
        try
        {
            plugin.SaveSettings(BuildSettings(enabled: true));
            AlertMessage = string.Empty;
            return new PluginConfigurationResult { Success = true, Settings = currentSettings };
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            AlertMessage = exception.Message;
            return new PluginConfigurationResult { Success = false, Error = exception.Message };
        }
    }

    private void ApplySettings(AiProviderSettings settings)
    {
        savedSettings = settings;
        applying = true;
        try
        {
            SelectedAiProvider = AiProviders.FirstOrDefault(provider =>
                                    string.Equals(provider.Id, settings.ProviderId, StringComparison.OrdinalIgnoreCase))
                                ?? AiProviders.FirstOrDefault();
            AiProviderProtocol = settings.Protocol;
            AiProviderAddress = settings.Address;
            AiProviderPort = settings.Port;
            AiProviderApiKey = settings.ApiKey;
            AiProviderModels.Clear();
            if (SelectedAiProvider?.SuggestedModels is { Count: > 0 } suggestedModels)
            {
                foreach (var model in suggestedModels)
                {
                    AddModel(model);
                }
            }

            AiProviderModel = settings.Model;
            AddModel(settings.Model);
            var resolvedLanguage = ResolveLanguage(settings.Language);
            if (!AiProviderLanguages.Contains(resolvedLanguage, StringComparer.OrdinalIgnoreCase))
            {
                AiProviderLanguages.Add(resolvedLanguage);
            }

            AiProviderLanguage = resolvedLanguage;
            AiProviderResizeLargeImages = settings.ResizeLargeImages;
            CanStartServer = SelectedAiProvider?.CanStartServer == true;
            ConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderNotChecked);
        }
        finally
        {
            applying = false;
        }

        UpdateEndpointUrl();
    }

    private AiProviderSettings BuildSettings(bool enabled) => savedSettings with
    {
        ProviderId = SelectedAiProvider?.Id ?? savedSettings.ProviderId,
        Protocol = AiProviderProtocol,
        Address = AiProviderAddress?.Trim() ?? string.Empty,
        Port = AiProviderPort,
        ApiKey = AiProviderApiKey ?? string.Empty,
        Model = AiProviderModel?.Trim() ?? string.Empty,
        Language = AiProviderLanguage,
        ResizeLargeImages = AiProviderResizeLargeImages,
        Enabled = enabled
    };

    private void UpdateEndpointUrl()
    {
        var settings = BuildSettings(enabled: false);
        AiProviderEndpointUrl = plugin.BuildEndpointUrl(settings);
    }

    private void AddModel(string? model)
    {
        if (!string.IsNullOrWhiteSpace(model) &&
            !AiProviderModels.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            AiProviderModels.Add(model);
        }
    }

    private void ReplaceModels(IReadOnlyList<string> models)
    {
        AiProviderModels.Clear();
        foreach (var model in models)
        {
            AddModel(model);
        }

        if (!AiProviderModels.Contains(AiProviderModel, StringComparer.OrdinalIgnoreCase))
        {
            AiProviderModel = AiProviderModels.FirstOrDefault() ?? AiProviderModel;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseActions))]
    private async Task CheckAiProviderConnectionAsync()
    {
        IsBusy = true;
        AlertMessage = string.Empty;
        ConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderChecking);
        try
        {
            var result = await plugin.CheckConnectionAsync(BuildSettings(enabled: true), lifetime.Token);
            ConnectionStatus = result.Message;
            if (result.IsConnected)
            {
                ReplaceModels(result.Models);
            }
            else
            {
                AlertMessage = result.Message;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            ConnectionStatus = exception.Message;
            AlertMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseActions))]
    private async Task RefreshAiProviderModelsAsync()
    {
        IsBusy = true;
        AlertMessage = string.Empty;
        try
        {
            var models = await plugin.ListModelsAsync(BuildSettings(enabled: true), lifetime.Token);
            ReplaceModels(models);
            ConnectionStatus = models.Count == 0
                ? LocalizationService.Current.Get(UiKeys.AiProviderNoModels)
                : LocalizationService.Current.Format(UiKeys.AiProviderModelsLoaded, models.Count);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            AlertMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(AiProviderCanStartServer))]
    private async Task StartAiProviderServerAsync()
    {
        IsBusy = true;
        AlertMessage = string.Empty;
        try
        {
            var error = await plugin.StartServerAsync(BuildSettings(enabled: true), lifetime.Token);
            if (error is null)
            {
                ConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderServerStarted);
            }
            else
            {
                AlertMessage = error;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            AlertMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(ShowApiKeyHelp))]
    private void OpenAiProviderApiKeyHelp() => TryOpenLink(ApiKeyHelpUrl);

    private static bool IsLaunchable(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void TryOpenLink(string? value)
    {
        if (!IsLaunchable(value))
        {
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = value, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A missing default browser leaves the link inert; the URL stays visible for copying.
        }
    }

    private static bool IsProviderFailure(Exception exception) => exception is InvalidOperationException
        or IOException or UnauthorizedAccessException or JsonException or ArgumentException
        or NotSupportedException or HttpRequestException or TaskCanceledException or AiProviderException;

    partial void OnSelectedAiProviderChanged(AiProviderChoice? value)
    {
        OnPropertyChanged(nameof(ApiKeyHelpUrl));
        OnPropertyChanged(nameof(ShowApiKeyHelp));
        OpenAiProviderApiKeyHelpCommand.NotifyCanExecuteChanged();
        if (applying || value is null)
        {
            return;
        }

        AiProviderProtocol = value.Protocol;
        AiProviderAddress = value.Address;
        AiProviderPort = value.Port;
        CanStartServer = value.CanStartServer;
        AiProviderModels.Clear();
        if (value.SuggestedModels is { Count: > 0 })
        {
            foreach (var model in value.SuggestedModels)
            {
                AddModel(model);
            }

            AiProviderModel = value.DefaultModel ?? value.SuggestedModels[0];
            AddModel(AiProviderModel);
        }
        else if (value.DefaultModel is not null)
        {
            AiProviderModel = value.DefaultModel;
            AddModel(value.DefaultModel);
        }
        else
        {
            AiProviderModel = string.Empty;
        }

        ConnectionStatus = LocalizationService.Current.Get(UiKeys.AiProviderNotChecked);
        AlertMessage = string.Empty;
        UpdateEndpointUrl();
    }

    partial void OnAiProviderProtocolChanged(string value) => UpdateEndpointUrl();

    partial void OnAiProviderAddressChanged(string value) => UpdateEndpointUrl();

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

        UpdateEndpointUrl();
    }

    partial void OnAlertMessageChanged(string value) => OnPropertyChanged(nameof(ShowAlert));

    partial void OnCanStartServerChanged(bool value) => OnPropertyChanged(nameof(AiProviderCanStartServer));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(AiProviderCanStartServer));
        OnPropertyChanged(nameof(CanUseActions));
        CheckAiProviderConnectionCommand.NotifyCanExecuteChanged();
        RefreshAiProviderModelsCommand.NotifyCanExecuteChanged();
        StartAiProviderServerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Display name of the active UI culture as listed in the output-language choices.</summary>
    private string GetUiLanguage()
    {
        var culture = LocalizationService.Current.CurrentCulture;
        foreach (var language in AiProviderLanguages)
        {
            if (LanguageCultureMatches(language, null, culture))
            {
                return language;
            }
        }

        return AiProviderLanguages.FirstOrDefault() ?? AiProviderLanguage;
    }

    private static bool LanguageCultureMatches(string displayName, string? cultureCode, CultureInfo culture)
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
    /// returned verbatim so an entry saved elsewhere stays selectable.
    /// </summary>
    private string ResolveLanguage(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            return GetUiLanguage();
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

        if (LooksLikeCultureCode(normalized))
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

    private static bool MatchesCultureName(string normalized, CultureInfo culture) =>
        MatchesCandidateName(normalized, culture.Name) ||
        MatchesCandidateName(normalized, culture.EnglishName) ||
        MatchesCandidateName(normalized, culture.NativeName);

    private static bool MatchesCandidateName(string normalized, string candidate) =>
        normalized.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals(StripLanguageSuffix(candidate), StringComparison.OrdinalIgnoreCase);

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

    private static bool LooksLikeCultureCode(string value)
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

    public void Dispose()
    {
        try { lifetime.Cancel(); }
        catch (Exception exception) when (exception is ObjectDisposedException) { }
        lifetime.Dispose();
    }
}
