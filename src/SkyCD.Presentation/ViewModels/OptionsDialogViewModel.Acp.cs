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
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private IAcpAgentSettingsCapability? acpSettingsCapability;
    private AcpAgentSettings? initialAcpSettings;
    private bool explicitlySelectedAcpAgent;

    public ObservableCollection<AcpAgentChoice> AcpAgents { get; } = [];
    public bool ShowAcpSection => SelectedTabIndex == 5 && IsCurrentCategoryVisibleInSearch;
    public bool CanChooseAcpAgent => initialAcpSettings is not null && !IsAcpLoading;
    public IReadOnlyList<AcpAgentChoice> InstalledAcpAgents { get; private set; } = [];
    public IReadOnlyList<AcpAgentChoice> AvailableAcpAgents { get; private set; } = [];
    public bool HasInstalledAcpAgents => InstalledAcpAgents.Count != 0;
    public bool HasAvailableAcpAgents => AvailableAcpAgents.Count != 0;
    public bool HasNoMatchingAcpAgents => !HasInstalledAcpAgents && !HasAvailableAcpAgents;

    [ObservableProperty] private AcpAgentChoice? selectedAcpAgent;
    [ObservableProperty] private string acpSearchText = string.Empty;
    [ObservableProperty] private string? acpRegistryStatus;
    [ObservableProperty] private string? acpAlertMessage;
    [ObservableProperty] private string? installingAcpAgentName;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseAcpAgent))]
    private bool isAcpLoading;

    partial void OnAcpSearchTextChanged(string value) => NotifyAcpAgentListsChanged();

    private void NotifyAcpAgentListsChanged()
    {
        var search = AcpSearchText.Trim();
        var matching = AcpAgents.Where(agent => agent.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            agent.Description?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true).ToArray();
        InstalledAcpAgents = matching.Where(agent => agent.IsInstalled).ToArray();
        AvailableAcpAgents = matching.Where(agent => !agent.IsInstalled).ToArray();
        OnPropertyChanged(nameof(InstalledAcpAgents));
        OnPropertyChanged(nameof(AvailableAcpAgents));
        OnPropertyChanged(nameof(HasInstalledAcpAgents));
        OnPropertyChanged(nameof(HasAvailableAcpAgents));
        OnPropertyChanged(nameof(HasNoMatchingAcpAgents));
    }

    private async Task RefreshAcpRegistryAsync(IAcpAgentSettingsCapability capability, CancellationToken cancellationToken)
    {
        try
        {
            await capability.RefreshRegistryAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpRegistryDownloadError, exception.Message);
        }
    }

    public async Task LoadAcpSettingsAsync(IAcpAgentSettingsCapability? capability, bool refreshRegistry = false,
        CancellationToken cancellationToken = default)
    {
        acpSettingsCapability = capability;
        IsAcpLoading = true;
        try
        {
            if (refreshRegistry && capability is not null)
                await RefreshAcpRegistryAsync(capability, cancellationToken);
            await ReadAcpSettingsAsync(capability, preserveSelection: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing Options cancels background work.
        }
        finally
        {
            IsAcpLoading = false;
        }
    }

    public async Task InstallAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
    {
        if (acpSettingsCapability is not { } capability || IsAcpLoading || !agent.CanInstall ||
            agent.RegistryId is not { } id) return;
        IsAcpLoading = true;
        InstallingAcpAgentName = agent.Name;
        AcpAlertMessage = null;
        try
        {
            var installed = await capability.InstallAgentAsync(id, cancellationToken);
            await ReadAcpSettingsAsync(capability, preserveSelection: true, cancellationToken);
            SelectAcpAgent(AcpAgents.FirstOrDefault(candidate => candidate.RegistryId == id && candidate.IsInstalled) ?? installed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the dialog cancels unfinished installs.
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or
            InvalidOperationException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception)
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpAgentInstallError, exception.Message);
        }
        finally
        {
            InstallingAcpAgentName = null;
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

    public void ChooseLocalAcpAgent(AcpAgentChoice agent, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFileName(path), agent.ExpectedExecutable, comparison) || !File.Exists(path))
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpFindExecutable, agent.ExpectedExecutable ?? string.Empty);
            return;
        }

        AcpAlertMessage = null;
        var local = agent with { Executable = path, CanInstall = false };
        AcpAgents.Remove(agent);
        AcpAgents.Add(local);
        SelectAcpAgent(local);
        NotifyAcpAgentListsChanged();
    }

    public void SelectAcpAgent(AcpAgentChoice agent)
    {
        SelectedAcpAgent = agent;
        explicitlySelectedAcpAgent = true;
    }

    private bool TrySaveAcpSettings()
    {
        if (IsAcpLoading && InstallingAcpAgentName is not null) return false;
        AcpAlertMessage = null;
        if (initialAcpSettings is not { } initial || acpSettingsCapability is null ||
            SelectedAcpAgent is not { } selected || (!explicitlySelectedAcpAgent && Equals(initial.SelectedAgent, selected)))
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
            acpSettingsCapability.SaveAgentSettings(selected, activate: explicitlySelectedAcpAgent);
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
