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
using SkyCD.Plugin.Ai.Description.Models;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Ai.Description.Views;

/// <summary>
/// Selects the installed ACP agent that generates descriptions, with registry browsing, install,
/// update, uninstall and manual executable location. Accepting persists an explicitly chosen agent.
/// </summary>
public sealed partial class AcpAgentsPageViewModel : ObservableObject, IDisposable
{
    private readonly Func<AcpAgentSettings> readSettings;
    private readonly Func<CancellationToken, Task> refreshRegistry;
    private readonly Func<string, CancellationToken, Task<AcpAgentChoice>> installAgent;
    private readonly Func<string, CancellationToken, Task<AcpAgentChoice>> updateAgent;
    private readonly Action<string> uninstallAgent;
    private readonly Action<AcpAgentChoice, bool> saveAgentSettings;
    private readonly IReadOnlyDictionary<string, string?> currentSettings;
    private readonly CancellationTokenSource lifetime = new();
    private AcpAgentSettings? initialSettings;
    private bool explicitlySelectedAcpAgent;
    private bool started;

    public AcpAgentsPageViewModel(AiDescriptionPlugin plugin,
        IReadOnlyDictionary<string, string?> currentSettings)
        : this(plugin.GetAgentSettings, plugin.RefreshRegistryAsync, plugin.InstallAgentAsync,
            plugin.UpdateAgentAsync, plugin.UninstallAgent, plugin.SaveAgentSettings, currentSettings)
    {
    }

    internal AcpAgentsPageViewModel(Func<AcpAgentSettings> readSettings,
        Func<CancellationToken, Task> refreshRegistry,
        Func<string, CancellationToken, Task<AcpAgentChoice>> installAgent,
        Func<string, CancellationToken, Task<AcpAgentChoice>> updateAgent,
        Action<string> uninstallAgent,
        Action<AcpAgentChoice, bool> saveAgentSettings,
        IReadOnlyDictionary<string, string?> currentSettings)
    {
        this.readSettings = readSettings ?? throw new ArgumentNullException(nameof(readSettings));
        this.refreshRegistry = refreshRegistry ?? throw new ArgumentNullException(nameof(refreshRegistry));
        this.installAgent = installAgent ?? throw new ArgumentNullException(nameof(installAgent));
        this.updateAgent = updateAgent ?? throw new ArgumentNullException(nameof(updateAgent));
        this.uninstallAgent = uninstallAgent ?? throw new ArgumentNullException(nameof(uninstallAgent));
        this.saveAgentSettings = saveAgentSettings ?? throw new ArgumentNullException(nameof(saveAgentSettings));
        this.currentSettings = currentSettings ?? new Dictionary<string, string?>();
    }

    public ObservableCollection<AcpAgentChoice> AcpAgents { get; } = [];

    public bool CanChooseAcpAgent => initialSettings is not null && !IsAcpLoading;
    public IReadOnlyList<AcpAgentChoice> InstalledAcpAgents { get; private set; } = [];
    public IReadOnlyList<AcpAgentChoice> AvailableAcpAgents { get; private set; } = [];
    public bool HasInstalledAcpAgents => InstalledAcpAgents.Count != 0;
    public bool HasAvailableAcpAgents => AvailableAcpAgents.Count != 0;
    public bool HasNoMatchingAcpAgents => !HasInstalledAcpAgents && !HasAvailableAcpAgents;

    public AcpAgentChoice? AcpCardAgent => explicitlySelectedAcpAgent || initialSettings?.IsGenerationEnabled == true
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

    /// <summary>Browses the registry once and loads the local agent snapshot; safe to call repeatedly.</summary>
    public async Task InitializeAsync()
    {
        if (started)
        {
            return;
        }

        started = true;
        IsAcpLoading = true;
        try
        {
            await RefreshRegistryOnOpenAsync(lifetime.Token);
            await ReadAcpSettingsAsync(preserveSelection: false, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Closing Options cancels background work.
        }
        finally
        {
            IsAcpLoading = false;
        }
    }

    /// <summary>Persists an explicitly chosen agent; a change of selection enables generation.</summary>
    public PluginConfigurationResult Accept()
    {
        if (IsAcpLoading && AcpOperationLabel is not null)
        {
            return new PluginConfigurationResult { Success = false, Error = AcpLoadingLabel };
        }

        AcpAlertMessage = null;
        if (initialSettings is not { } initial || SelectedAcpAgent is not { } selected ||
            (!explicitlySelectedAcpAgent && Equals(initial.SelectedAgent, selected)))
        {
            return new PluginConfigurationResult { Success = true, Settings = currentSettings };
        }

        if (!selected.IsInstalled)
        {
            AcpAlertMessage = LocalizationService.Current.Get(UiKeys.ChooseInstalledAcpAgent);
            return new PluginConfigurationResult { Success = false, Error = AcpAlertMessage };
        }

        try
        {
            saveAgentSettings(selected, explicitlySelectedAcpAgent);
            return new PluginConfigurationResult { Success = true, Settings = currentSettings };
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                               or UnauthorizedAccessException or JsonException)
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpSettingsSaveError, exception.Message);
            return new PluginConfigurationResult { Success = false, Error = AcpAlertMessage };
        }
    }

    public Task<string?> InstallAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
        => ChangeManagedAgentAsync(agent, AcpAgentOperation.Install, cancellationToken);

    public Task<string?> UpdateAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
        => ChangeManagedAgentAsync(agent, AcpAgentOperation.Update, cancellationToken);

    public Task<string?> UninstallAcpAgentAsync(AcpAgentChoice agent, CancellationToken cancellationToken)
        => ChangeManagedAgentAsync(agent, AcpAgentOperation.Uninstall, cancellationToken);

    public CancellationToken Lifetime => lifetime.Token;

    private enum AcpAgentOperation { Install, Update, Uninstall }

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

    private async Task RefreshRegistryOnOpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await refreshRegistry(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or IOException or UnauthorizedAccessException
                or InvalidOperationException or OperationCanceledException)
        {
            AcpAlertMessage = LocalizationService.Current.Format(UiKeys.AcpRegistryDownloadError, exception.Message);
        }
    }

    private async Task<string?> ChangeManagedAgentAsync(AcpAgentChoice agent, AcpAgentOperation operation,
        CancellationToken cancellationToken)
    {
        if (IsAcpLoading || agent.RegistryId is not { } id)
        {
            return null;
        }

        var (allowed, progressKey, errorKey) = operation switch
        {
            AcpAgentOperation.Update => (agent.CanUpdate, UiKeys.UpdatingAcpAgent, UiKeys.AcpAgentUpdateError),
            AcpAgentOperation.Uninstall => (agent.IsManaged, UiKeys.UninstallingAcpAgent, UiKeys.AcpAgentUninstallError),
            _ => (agent.CanInstall, UiKeys.InstallingAcpAgent, UiKeys.AcpAgentInstallError)
        };
        if (!allowed)
        {
            return null;
        }

        IsAcpLoading = true;
        AcpOperationLabel = LocalizationService.Current.Format(progressKey, agent.Name);
        AcpAlertMessage = null;
        try
        {
            var wasPending = explicitlySelectedAcpAgent && SelectedAcpAgent?.RegistryId == id;
            AcpAgentChoice? installed = null;
            if (operation == AcpAgentOperation.Uninstall)
                await Task.Run(() => uninstallAgent(id), cancellationToken);
            else if (operation == AcpAgentOperation.Update)
                installed = await updateAgent(id, cancellationToken);
            else
                installed = await installAgent(id, cancellationToken);

            if (wasPending) explicitlySelectedAcpAgent = false;
            await ReadAcpSettingsAsync(preserveSelection: true, cancellationToken, replacedRegistryId: id);
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

    private async Task ReadAcpSettingsAsync(bool preserveSelection, CancellationToken cancellationToken,
        string? replacedRegistryId = null)
    {
        try
        {
            var settings = await Task.Run(readSettings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var pending = preserveSelection && SelectedAcpAgent is { } current &&
                (replacedRegistryId is null || current.RegistryId != replacedRegistryId) &&
                !Equals(SelectedAcpAgent, initialSettings?.SelectedAgent)
                ? SelectedAcpAgent : null;
            initialSettings = settings;
            AcpRegistryStatus = settings.RegistryStatus;

            AcpAgents.Clear();
            foreach (var agent in settings.Agents)
            {
                AcpAgents.Add(agent);
            }

            SelectedAcpAgent = settings.SelectedAgent;
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
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                               or UnauthorizedAccessException or JsonException)
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

    public void Dispose()
    {
        try { lifetime.Cancel(); }
        catch (Exception exception) when (exception is ObjectDisposedException) { }
        lifetime.Dispose();
    }
}
