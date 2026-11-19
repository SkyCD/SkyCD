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
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private IAcpAgentSettingsCapability? acpSettingsCapability;
    private AcpAgentSettings? initialAcpSettings;

    public ObservableCollection<AcpAgentChoice> AcpAgents { get; } = [];
    public bool ShowAcpSection => SelectedTabIndex == 5 && IsCurrentCategoryVisibleInSearch;
    public bool CanChooseAcpAgent => initialAcpSettings is not null && !IsAcpLoading;
    public bool CanRefreshAcpRegistry => acpSettingsCapability is not null && !IsAcpLoading;
    public IEnumerable<AcpAgentChoice> InstalledAcpAgents => FilterAcpAgents(installed: true);
    public IEnumerable<AcpAgentChoice> AvailableAcpAgents => FilterAcpAgents(installed: false);

    [ObservableProperty] private AcpAgentChoice? selectedAcpAgent;
    [ObservableProperty] private string acpSearchText = string.Empty;
    [ObservableProperty] private string? acpRegistryStatus;
    [ObservableProperty] private string? acpAlertMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseAcpAgent), nameof(CanRefreshAcpRegistry))]
    private bool isAcpLoading;

    partial void OnAcpSearchTextChanged(string value) => NotifyAcpAgentListsChanged();

    private IEnumerable<AcpAgentChoice> FilterAcpAgents(bool installed)
    {
        var search = AcpSearchText.Trim();
        return AcpAgents.Where(agent => agent.IsInstalled == installed &&
            (agent.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             agent.Description?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true))
            .ToArray();
    }

    private void NotifyAcpAgentListsChanged()
    {
        OnPropertyChanged(nameof(InstalledAcpAgents));
        OnPropertyChanged(nameof(AvailableAcpAgents));
    }

    [RelayCommand]
    private async Task RefreshAcpRegistryAsync(CancellationToken cancellationToken)
    {
        if (acpSettingsCapability is not { } capability || IsAcpLoading) return;
        IsAcpLoading = true;
        AcpAlertMessage = null;
        try
        {
            await capability.RefreshRegistryAsync(cancellationToken);
            await ReadAcpSettingsAsync(capability, preserveSelection: true, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpRegistryDownloadError, exception.Message);
        }
        finally
        {
            IsAcpLoading = false;
        }
    }

    public async Task LoadAcpSettingsAsync(IAcpAgentSettingsCapability? capability)
    {
        acpSettingsCapability = capability;
        IsAcpLoading = true;
        try
        {
            await ReadAcpSettingsAsync(capability, preserveSelection: false, CancellationToken.None);
        }
        finally
        {
            IsAcpLoading = false;
        }
    }

    private async Task ReadAcpSettingsAsync(IAcpAgentSettingsCapability? capability, bool preserveSelection,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = capability is null ? null : await Task.Run(capability.GetAgentSettings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var pending = preserveSelection && !Equals(SelectedAcpAgent, initialAcpSettings?.SelectedAgent)
                ? SelectedAcpAgent : null;
            initialAcpSettings = settings;
            AcpRegistryStatus = capability is null
                ? LocalizationService.Current.Get(UiKeys.EnableAiDescriptionPlugin)
                : settings?.RegistryStatus;

            AcpAgents.Clear();
            if (settings is not null)
            {
                foreach (var agent in settings.Agents) AcpAgents.Add(agent);
            }

            SelectedAcpAgent = settings?.SelectedAgent;
            if (pending?.Executable is { } executable)
            {
                SelectedAcpAgent = AcpAgents.FirstOrDefault(agent => agent.Executable == executable &&
                    agent.Arguments.SequenceEqual(pending.Arguments));
                if (SelectedAcpAgent is null)
                {
                    AcpAgents.Add(pending);
                    SelectedAcpAgent = pending;
                }
            }
            NotifyAcpAgentListsChanged();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            AcpRegistryStatus = LocalizationService.Current.Format(UiKeys.AcpSettingsLoadError, exception.Message);
        }
    }

    public void ChooseLocalAcpAgent(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var existing in AcpAgents)
        {
            if (!string.Equals(path, existing.Executable, comparison)) continue;
            SelectedAcpAgent = existing;
            return;
        }

        var agent = SelectedAcpAgent is { IsInstalled: false } cached
            ? cached with { Executable = path }
            : new AcpAgentChoice(Path.GetFileName(path), path, []);
        AcpAgents.Add(agent);
        SelectedAcpAgent = agent;
        NotifyAcpAgentListsChanged();
    }

    private bool TrySaveAcpSettings()
    {
        AcpAlertMessage = null;
        if (initialAcpSettings is not { } initial || acpSettingsCapability is null ||
            SelectedAcpAgent is not { } selected || Equals(initial.SelectedAgent, selected))
            return true;

        if (!selected.IsInstalled)
        {
            SettingsSearchText = string.Empty;
            SelectedTabIndex = 5;
            AcpAlertMessage = LocalizationService.Current.Get(UiKeys.ChooseInstalledAcpAgent);
            return false;
        }

        try
        {
            acpSettingsCapability.SaveAgentSettings(selected);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            SettingsSearchText = string.Empty;
            SelectedTabIndex = 5;
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpSettingsSaveError, exception.Message);
            return false;
        }
    }
}
