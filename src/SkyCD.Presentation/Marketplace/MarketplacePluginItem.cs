using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Plugins;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Marketplace;

/// <summary>User-facing state of one catalog package in the Browse view.</summary>
public enum MarketplacePluginItemState
{
    Available,
    UpdateAvailable,
    Installed,
    Installing,
    RestartRequired,
    Incompatible,
    Unavailable,
    Failed
}

/// <summary>
/// One package advertised by the plugin catalog together with the catalog-owned install state of the same
/// package. The item never downloads on its own; <c>OptionsDialogViewModel</c> drives staging.
/// </summary>
public sealed partial class MarketplacePluginItem : ObservableObject
{
    public MarketplacePluginItem(MarketplacePackage package, PluginInstallState? installState = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        Package = package;
        Release = package.BestRelease;
        PackageId = package.PackageId;
        Title = package.Title;
        Publisher = string.IsNullOrWhiteSpace(package.Publisher)
            ? LocalizationService.Current.Get(TextKeys.PluginUnknownPublisher)
            : package.Publisher;
        Source = package.SourceUri.IsAbsoluteUri ? package.SourceUri.Host : package.SourceUri.OriginalString;
        ProjectUrl = package.ProjectUrl?.AbsoluteUri;
        Summary = package.Summary ?? string.Empty;
        CompatibilityMessage = ResolveCompatibilityMessage(package);
        ApplyState(installState);
    }

    public MarketplacePackage Package { get; }

    /// <summary>The highest release eligible for automatic selection; <c>null</c> when none can be installed.</summary>
    public MarketplaceRelease? Release { get; }

    public string PackageId { get; }

    public string Title { get; }

    public string Publisher { get; }

    public string Source { get; }

    public string? ProjectUrl { get; }

    public string Summary { get; }

    /// <summary>Icon declared by the catalog entry, or the neutral marketplace glyph.</summary>
    public string IconKey => PluginIconKey.ForMarketplace(Package.Icon);

    public bool HasGlyphIcon => PluginIconKey.IsGlyph(IconKey);

    public bool HasBitmapIcon => !HasGlyphIcon;

    public string CompatibilityMessage { get; }

    public bool HasSummary => !string.IsNullOrEmpty(Summary);

    public string AvailableVersion =>
        (Release ?? Package.Releases.FirstOrDefault())?.Version.Normalized
        ?? LocalizationService.Current.Get(TextKeys.PluginNoRelease);

    public bool HasRelease => Release is not null;

    /// <summary>A package can list releases while none of them is selectable on this host.</summary>
    public bool HasAnyRelease => Package.Releases.Count > 0;

    public bool IsCompatible => Release?.IsEligible == true;

    public string? ActiveVersion { get; private set; }

    public string? PendingVersion { get; private set; }

    public bool IsInstalled => !string.IsNullOrEmpty(ActiveVersion);

    /// <summary>True when the active files are owned by the catalog installer and may be deleted by it.</summary>
    public bool IsCatalogOwned { get; private set; }

    /// <summary>Install folder id of the catalog-owned install; <c>null</c> for manual or bundled plugins.</summary>
    public string? InstalledPluginId { get; private set; }

    public bool IsPendingRestart => State == MarketplacePluginItemState.RestartRequired;

    public bool UpdateAvailable => IsInstalled && HasRelease && IsCompatible && CompareVersions(AvailableVersion, ActiveVersion!) > 0;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private string? errorMessage;

    [ObservableProperty] private string statusMessage = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public MarketplacePluginItemState State
    {
        get
        {
            if (IsBusy) return MarketplacePluginItemState.Installing;
            if (HasError) return MarketplacePluginItemState.Failed;
            if (PendingVersion is not null && IsRestartPending) return MarketplacePluginItemState.RestartRequired;
            if (!HasRelease) return HasAnyRelease ? MarketplacePluginItemState.Incompatible : MarketplacePluginItemState.Unavailable;
            if (!IsCompatible) return MarketplacePluginItemState.Incompatible;
            if (UpdateAvailable) return MarketplacePluginItemState.UpdateAvailable;
            return IsInstalled ? MarketplacePluginItemState.Installed : MarketplacePluginItemState.Available;
        }
    }

    /// <summary>Duplicate requests are blocked while staging and until the staged version activates.</summary>
    public bool CanInstallOrUpdate =>
        HasRelease && IsCompatible && !IsBusy && !IsRestartPending && !IsCurrentVersionStaged;

    public bool ShowPrimaryAction =>
        CanInstallOrUpdate && (!IsInstalled || UpdateAvailable);

    public bool ShowRetryAction => HasError && HasRelease && IsCompatible;

    /// <summary>Something the primary split-button action can do: install, update or retry.</summary>
    public bool CanRunPrimaryAction => ShowPrimaryAction || ShowRetryAction;

    /// <summary>An active catalog-owned install can be removed from disk; manual installs cannot.</summary>
    public bool CanUninstall => IsCatalogOwned && IsInstalled && !IsBusy;

    public string StatusLabel => State switch
    {
        MarketplacePluginItemState.Available => LocalizationService.Current.Get(TextKeys.PluginInstallAction),
        MarketplacePluginItemState.UpdateAvailable => LocalizationService.Current.Get(TextKeys.PluginUpdateAction),
        MarketplacePluginItemState.Installed => LocalizationService.Current.Get(TextKeys.PluginInstalledAction),
        MarketplacePluginItemState.Installing => LocalizationService.Current.Get(TextKeys.PluginDownloading),
        MarketplacePluginItemState.RestartRequired => LocalizationService.Current.Get(TextKeys.PluginRestartRequired),
        MarketplacePluginItemState.Incompatible => LocalizationService.Current.Get(TextKeys.PluginIncompatible),
        MarketplacePluginItemState.Unavailable => LocalizationService.Current.Get(TextKeys.PluginUnavailable),
        _ => LocalizationService.Current.Get(TextKeys.PluginRetryAction)
    };

    public string PrimaryActionLabel => State == MarketplacePluginItemState.Failed
        ? LocalizationService.Current.Get(TextKeys.PluginRetryAction)
        : UpdateAvailable
            ? LocalizationService.Current.Get(TextKeys.PluginUpdateAction)
            : LocalizationService.Current.Get(TextKeys.PluginInstallAction);

    public string InstalledVersionLabel => IsInstalled
        ? ActiveVersion!
        : LocalizationService.Current.Get(TextKeys.PluginNotInstalled);

    public string SourceLabel => LocalizationService.Current.Format(TextKeys.PluginSource, Source);

    public string VersionSummary => LocalizationService.Current.Format(
        TextKeys.PluginVersionSummary, AvailableVersion, ActiveVersion ?? "-");

    public bool MatchesFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               PackageId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               Source.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public void BeginInstall()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = string.Empty;
    }

    public void ApplyResult(PluginInstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsBusy = false;
        ApplyState(result.State);
        if (result.IsSuccess)
        {
            ErrorMessage = null;
            StatusMessage = result.Status == PluginInstallOperationStatus.PendingRestart
                ? LocalizationService.Current.Format(TextKeys.PluginStagedMessage, Title,
                    result.State.PendingVersion ?? AvailableVersion)
                : string.Empty;
        }
        else
        {
            ErrorMessage = result.Message ?? LocalizationService.Current.Get(TextKeys.PluginInstallFailed);
            StatusMessage = result.State.ActiveVersion is { Length: > 0 } active
                ? LocalizationService.Current.Format(TextKeys.PluginStillInstalled, active)
                : LocalizationService.Current.Get(TextKeys.PluginNotInstalled);
        }

        NotifyStateChanged();
    }

    public void ApplyFailure(string message)
    {
        IsBusy = false;
        ErrorMessage = message;
        StatusMessage = IsInstalled
            ? LocalizationService.Current.Format(TextKeys.PluginStillInstalled, ActiveVersion!)
            : LocalizationService.Current.Get(TextKeys.PluginNotInstalled);
        NotifyStateChanged();
    }

    private bool IsRestartPending => pendingRestart;

    private bool pendingRestart;

    private bool IsCurrentVersionStaged =>
        pendingRestart && string.Equals(PendingVersion, Release?.Version.Normalized, StringComparison.OrdinalIgnoreCase);

    private void ApplyState(PluginInstallState? state)
    {
        if (state is null)
        {
            ActiveVersion = null;
            PendingVersion = null;
            pendingRestart = false;
            InstalledPluginId = null;
            IsCatalogOwned = false;
            return;
        }

        ActiveVersion = state.ActiveVersion;
        pendingRestart = state.Status == PluginInstallOperationStatus.PendingRestart;
        PendingVersion = pendingRestart ? state.PendingVersion : null;
        IsCatalogOwned = state.Status != PluginInstallOperationStatus.NotInstalled;
        InstalledPluginId = IsCatalogOwned ? state.PluginId : null;
        if (state.Status is PluginInstallOperationStatus.Failed or PluginInstallOperationStatus.RolledBack &&
            !string.IsNullOrWhiteSpace(state.Error))
        {
            ErrorMessage = state.Error;
            StatusMessage = state.ActiveVersion is { Length: > 0 } active
                ? LocalizationService.Current.Format(TextKeys.PluginStillInstalled, active)
                : LocalizationService.Current.Get(TextKeys.PluginNotInstalled);
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyStateChanged();

    partial void OnErrorMessageChanged(string? value) => NotifyStateChanged();

    partial void OnStatusMessageChanged(string value) => NotifyStateChanged();

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsPendingRestart));
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasStatusMessage));
        OnPropertyChanged(nameof(CanInstallOrUpdate));
        OnPropertyChanged(nameof(ShowPrimaryAction));
        OnPropertyChanged(nameof(ShowRetryAction));
        OnPropertyChanged(nameof(CanRunPrimaryAction));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(IsCatalogOwned));
        OnPropertyChanged(nameof(InstalledPluginId));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(InstalledVersionLabel));
        OnPropertyChanged(nameof(VersionSummary));
        OnPropertyChanged(nameof(ActiveVersion));
        OnPropertyChanged(nameof(PendingVersion));
    }

    private static string ResolveCompatibilityMessage(MarketplacePackage package)
    {
        if (package.BestRelease is { } release)
        {
            return release.Compatibility.Message;
        }

        return package.Releases.FirstOrDefault()?.Compatibility.Message
               ?? LocalizationService.Current.Get(TextKeys.PluginUnavailable);
    }

    private static int CompareVersions(string version, string other)
    {
        if (PluginPackageVersion.TryParse(version, out var left) &&
            PluginPackageVersion.TryParse(other, out var right) &&
            left is not null && right is not null)
        {
            return left.CompareTo(right);
        }

        return string.Equals(version, other, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }
}
