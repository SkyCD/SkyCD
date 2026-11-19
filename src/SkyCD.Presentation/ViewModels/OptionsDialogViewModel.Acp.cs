using System;
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
    public bool CanChooseAcpAgent => initialAcpSettings is not null;

    [ObservableProperty] private AcpAgentChoice? selectedAcpAgent;
    [ObservableProperty] private bool acpEnabled;
    [ObservableProperty] private string? acpRegistryStatus;
    [ObservableProperty] private string? acpAlertMessage;

    [RelayCommand]
    private async Task RefreshAcpRegistryAsync(CancellationToken cancellationToken)
    {
        if (acpSettingsCapability is not { } capability) return;
        AcpAlertMessage = null;
        try
        {
            await capability.RefreshRegistryAsync(cancellationToken);
            var selected = SelectedAcpAgent;
            var selectionChanged = !Equals(selected, initialAcpSettings?.SelectedAgent);
            var enabled = AcpEnabled;
            LoadAcpSettings(capability);
            AcpEnabled = enabled;
            if (selectionChanged && selected?.IsInstalled == true)
            {
                SelectedAcpAgent = AcpAgents.FirstOrDefault(agent => Equals(agent, selected));
                if (SelectedAcpAgent is null)
                {
                    AcpAgents.Add(selected);
                    SelectedAcpAgent = selected;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpRegistryDownloadError, exception.Message);
        }
    }

    public void LoadAcpSettings(IAcpAgentSettingsCapability? capability)
    {
        acpSettingsCapability = capability;
        try
        {
            initialAcpSettings = capability?.GetAgentSettings();
            AcpRegistryStatus = capability is null
                ? LocalizationService.Current.Get(UiKeys.EnableAiDescriptionPlugin)
                : initialAcpSettings?.RegistryStatus;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            initialAcpSettings = null;
            AcpRegistryStatus = LocalizationService.Current.Format(UiKeys.AcpSettingsLoadError, exception.Message);
        }

        AcpAgents.Clear();
        if (initialAcpSettings is { } settings)
        {
            foreach (var agent in settings.Agents) AcpAgents.Add(agent);
        }

        SelectedAcpAgent = initialAcpSettings?.SelectedAgent;
        AcpEnabled = initialAcpSettings?.Enabled == true;
        OnPropertyChanged(nameof(CanChooseAcpAgent));
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

        var agent = new AcpAgentChoice(Path.GetFileName(path), path, []);
        AcpAgents.Add(agent);
        SelectedAcpAgent = agent;
    }

    private bool TrySaveAcpSettings()
    {
        AcpAlertMessage = null;
        if (initialAcpSettings is not { } initial || acpSettingsCapability is null ||
            (Equals(initial.SelectedAgent, SelectedAcpAgent) && initial.Enabled == AcpEnabled))
            return true;

        if ((AcpEnabled && SelectedAcpAgent?.IsInstalled != true) ||
            (SelectedAcpAgent is { IsInstalled: false } && !Equals(initial.SelectedAgent, SelectedAcpAgent)))
        {
            SettingsSearchText = string.Empty;
            SelectedTabIndex = 5;
            AcpAlertMessage = LocalizationService.Current.Get(UiKeys.ChooseInstalledAcpAgent);
            return false;
        }

        try
        {
            acpSettingsCapability.SaveAgentSettings(SelectedAcpAgent, AcpEnabled);
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
