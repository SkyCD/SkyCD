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
using SkyCD.SDK.Capabilities.Media;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private const string AcpCategoryId = "acp";
    private const int AcpCategoryIndex = 5;

    private IAcpAgentSettingsCapability? acpSettingsCapability;
    private AcpAgentSettings? initialAcpSettings;
    private bool explicitlySelectedAcpAgent;

    public ObservableCollection<AcpAgentChoice> AcpAgents { get; } = [];
    public bool ShowAcpSection => SelectedBuiltInIndex == AcpCategoryIndex && IsCurrentCategoryVisibleInSearch;
    public bool CanChooseAcpAgent => initialAcpSettings is not null && !IsAcpLoading;
    public IReadOnlyList<AcpAgentChoice> InstalledAcpAgents { get; private set; } = [];
    public IReadOnlyList<AcpAgentChoice> AvailableAcpAgents { get; private set; } = [];
    public bool HasInstalledAcpAgents => InstalledAcpAgents.Count != 0;
    public bool HasAvailableAcpAgents => AvailableAcpAgents.Count != 0;
    public bool HasNoMatchingAcpAgents => !HasInstalledAcpAgents && !HasAvailableAcpAgents;

    public AcpAgentChoice? AcpCardAgent => explicitlySelectedAcpAgent || initialAcpSettings?.IsGenerationEnabled == true
        ? SelectedAcpAgent : null;
    public bool HasAcpRegistryStatus => !string.IsNullOrWhiteSpace(AcpRegistryStatus);
    public bool HasAcpAlert => !string.IsNullOrWhiteSpace(AcpAlertMessage);
    public string AcpLoadingLabel => AcpOperationLabel ?? LocalizationService.Current.Get(UiKeys.LoadingAcpAgents);

    [ObservableProperty]
    private AcpAgentChoice? selectedAcpAgent;
    [ObservableProperty] private string acpSearchText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAcpRegistryStatus))]
    private string? acpRegistryStatus;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAcpAlert))]
    private string? acpAlertMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcpLoadingLabel))]
    private string? acpOperationLabel;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseAcpAgent))]
    private bool isAcpLoading;

    partial void OnAcpSearchTextChanged(string value) => NotifyAcpAgentListsChanged();

    private void NotifyAcpAgentListsChanged()
    {
        var search = AcpSearchText.Trim();
        var matching = AcpAgents.Where(agent => agent.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            agent.Description?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true).ToArray();
        InstalledAcpAgents = matching.Where(agent => agent.IsInstalled || agent.IsManaged).ToArray();
        AvailableAcpAgents = matching.Where(agent => !agent.IsInstalled && !agent.IsManaged).ToArray();
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
        explicitlySelectedAcpAgent = false;
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

    public Task<string?> InstallAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
        => ChangeManagedAgentAsync(agent, AcpAgentOperation.Install, cancellationToken);

    public Task<string?> UpdateAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
        => ChangeManagedAgentAsync(agent, AcpAgentOperation.Update, cancellationToken);

    public Task<string?> UninstallAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
        => ChangeManagedAgentAsync(agent, AcpAgentOperation.Uninstall, cancellationToken);

    private enum AcpAgentOperation { Install, Update, Uninstall }

    private async Task<string?> ChangeManagedAgentAsync(AcpAgentChoice agent, AcpAgentOperation operation,
        CancellationToken cancellationToken)
    {
        if (acpSettingsCapability is not { } capability || IsAcpLoading || agent.RegistryId is not { } id) return null;
        var (allowed, progressKey, errorKey) = operation switch
        {
            AcpAgentOperation.Update => (agent.CanUpdate, UiKeys.UpdatingAcpAgent, UiKeys.AcpAgentUpdateError),
            AcpAgentOperation.Uninstall => (agent.IsManaged, UiKeys.UninstallingAcpAgent, UiKeys.AcpAgentUninstallError),
            _ => (agent.CanInstall, UiKeys.InstallingAcpAgent, UiKeys.AcpAgentInstallError)
        };
        if (!allowed) return null;

        IsAcpLoading = true;
        AcpOperationLabel = LocalizationService.Current.Format(progressKey, agent.Name);
        AcpAlertMessage = null;
        try
        {
            var wasPending = explicitlySelectedAcpAgent && SelectedAcpAgent?.RegistryId == id;
            AcpAgentChoice? installed = null;
            if (operation == AcpAgentOperation.Uninstall)
                await Task.Run(() => capability.UninstallAgent(id), cancellationToken);
            else if (operation == AcpAgentOperation.Update)
                installed = await capability.UpdateAgentAsync(id, cancellationToken);
            else
                installed = await capability.InstallAgentAsync(id, cancellationToken);

            if (wasPending) explicitlySelectedAcpAgent = false;
            await ReadAcpSettingsAsync(capability, preserveSelection: true, cancellationToken, replacedRegistryId: id);
            if (installed is not null && (operation == AcpAgentOperation.Install || wasPending))
                SelectAcpAgent(AcpAgents.FirstOrDefault(candidate => candidate.RegistryId == id && candidate.IsInstalled) ?? installed);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or
            UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or JsonException or
            ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return LocalizationService.Current.Format(errorKey, exception.Message);
        }
        finally
        {
            AcpOperationLabel = null;
            IsAcpLoading = false;
        }
    }

    private async Task ReadAcpSettingsAsync(IAcpAgentSettingsCapability? capability, bool preserveSelection,
        CancellationToken cancellationToken, string? replacedRegistryId = null)
    {
        try
        {
            var settings = capability is null ? null : await Task.Run(capability.GetAgentSettings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var pending = preserveSelection && SelectedAcpAgent is { } current &&
                (replacedRegistryId is null || current.RegistryId != replacedRegistryId) &&
                !Equals(SelectedAcpAgent, initialAcpSettings?.SelectedAgent)
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
            OnPropertyChanged(nameof(AcpCardAgent));
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
        explicitlySelectedAcpAgent = true;
        SelectedAcpAgent = agent;
        OnPropertyChanged(nameof(AcpCardAgent));
    }

    private bool TrySaveAcpSettings()
    {
        if (IsAcpLoading && AcpOperationLabel is not null) return false;
        AcpAlertMessage = null;
        if (initialAcpSettings is not { } initial || acpSettingsCapability is null ||
            SelectedAcpAgent is not { } selected || (!explicitlySelectedAcpAgent && Equals(initial.SelectedAgent, selected)))
            return true;

        if (!selected.IsInstalled)
        {
            SettingsSearchText = string.Empty;
            SelectedSettingCategory = SettingCategories.First(category => category.Id == AcpCategoryId);
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
            SelectedSettingCategory = SettingCategories.First(category => category.Id == AcpCategoryId);
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpSettingsSaveError, exception.Message);
            return false;
        }
    }
}
