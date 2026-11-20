using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
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

    public bool ShowAiProviderSection =>
        ShowAiDescriptionSection && IsAiProviderImplementationSelected && aiProviderCapability is not null;

    public bool ShowAiProviderAlert => !string.IsNullOrWhiteSpace(AiProviderAlertMessage);

    public bool CanStartAiProviderServer => AiProviderCanStartServer && !IsAiProviderBusy;

    public bool CanUseAiProviderActions => !IsAiProviderBusy && aiProviderCapability is not null;

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
            if (!string.IsNullOrWhiteSpace(settings.Language) &&
                !AiProviderLanguages.Contains(settings.Language, StringComparer.OrdinalIgnoreCase))
            {
                AiProviderLanguages.Add(settings.Language);
            }

            AiProviderLanguage = settings.Language;
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
            SelectedTabIndex = AiDescriptionCategoryIndex;
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
        OnPropertyChanged(nameof(ShowAiProviderSection));
        OnPropertyChanged(nameof(ShowAiProviderAlert));
    }
}
