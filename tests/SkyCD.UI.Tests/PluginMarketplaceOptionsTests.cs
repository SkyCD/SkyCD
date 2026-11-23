using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class PluginMarketplaceOptionsTests
{
    [AvaloniaFact]
    public async Task Load_MapsCatalogStatesAndFiltersByText()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(
                Package("acme.one", "1.2.0"),
                Package("acme.two", "2.0.0"),
                Package("acme.three", "1.0.0", compatible: false),
                PackageWithoutRelease("acme.four"),
                Package("acme.five", "3.0.0"))
        };
        service.States["acme.two"] = ActiveState("acme.two", "1.0.0");
        service.States["acme.five"] = ActiveState("acme.five", "3.0.0");
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        Assert.True(vm.IsMarketplaceBrowseSelected);
        Assert.True(vm.ShowMarketplaceSection);
        Assert.False(vm.ShowInstalledPluginListSection);
        Assert.Equal(5, vm.FilteredMarketplacePlugins.Count);

        var one = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.one");
        Assert.Equal(MarketplacePluginItemState.Available, one.State);
        Assert.True(one.ShowPrimaryAction);

        var two = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.two");
        Assert.Equal(MarketplacePluginItemState.UpdateAvailable, two.State);
        Assert.Equal("2.0.0", two.AvailableVersion);
        Assert.Equal("1.0.0", two.ActiveVersion);

        var three = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.three");
        Assert.Equal(MarketplacePluginItemState.Incompatible, three.State);
        Assert.False(three.CanInstallOrUpdate);

        var four = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.four");
        Assert.Equal(MarketplacePluginItemState.Unavailable, four.State);

        var five = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.five");
        Assert.Equal(MarketplacePluginItemState.Installed, five.State);
        Assert.False(five.ShowPrimaryAction);

        vm.MarketplaceFilterText = "two";
        Assert.Single(vm.FilteredMarketplacePlugins);
        Assert.Equal("acme.two", vm.FilteredMarketplacePlugins[0].PackageId);
    }

    [AvaloniaFact]
    public async Task Load_DeduplicatesPackageIdsAcrossSources()
    {
        var first = Package("acme.shared", "1.0.0", source: "https://one.example/plugins.json");
        var second = Package("acme.shared", "1.4.0", source: "https://two.example/plugins.json");
        var service = new FakeMarketplaceService { Snapshot = SnapshotAt(first, second) };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.FilteredMarketplacePlugins);
        Assert.Equal("1.4.0", item.AvailableVersion);
        Assert.Equal("two.example", item.Source);
    }

    [AvaloniaFact]
    public async Task Browse_LoadsOnceAndRefreshForcesAnotherLoad()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "1.0.0"))
        };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        Assert.Single(service.Loads);
        Assert.False(service.Loads[0].ForceRefresh);

        await vm.RefreshMarketplaceCommand.ExecuteAsync(null);
        Assert.Equal(2, service.Loads.Count);
        Assert.True(service.Loads[1].ForceRefresh);
    }

    [AvaloniaFact]
    public async Task CatalogFailure_ReportsStatusAndKeepsInstalledPluginsAvailable()
    {
        var service = new FakeMarketplaceService
        {
            LoadFailure = new InvalidOperationException("network down")
        };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        Assert.False(vm.IsMarketplaceLoading);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginCatalogFailure), vm.MarketplaceStatusMessage);
        Assert.True(vm.ShowMarketplaceStatusMessage);
        Assert.Empty(vm.FilteredMarketplacePlugins);
        Assert.True(vm.ShowMarketplaceEmptyState);

        vm.ShowInstalledPluginsCommand.Execute(null);
        Assert.True(vm.ShowInstalledPluginListSection);
        Assert.False(vm.ShowMarketplaceSection);
    }

    [AvaloniaFact]
    public async Task OfflineCatalog_ShowsOfflineMessageAndEmptyState()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = new PluginCatalogSnapshot
            {
                ResolvedAt = DateTimeOffset.UnixEpoch,
                CatalogStatus = PluginCatalogStatus.Offline,
                CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Offline)
            }
        };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginCatalogOffline), vm.MarketplaceStatusMessage);
        Assert.True(vm.ShowMarketplaceEmptyState);
    }

    [AvaloniaFact]
    public async Task PartialFailure_ReportsPartialMessageWithResults()
    {
        var package = Package("acme.one", "1.0.0");
        var failing = new PluginSourceSnapshot
        {
            ManifestUri = new Uri("https://down.example/plugins.json"),
            Status = PluginSourceStatus.Offline,
            Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Offline, 0)
        };
        var snapshot = SnapshotAt(package) with { Sources = [.. SnapshotAt(package).Sources, failing] };
        var service = new FakeMarketplaceService { Snapshot = snapshot };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginCatalogPartialFailure),
            vm.MarketplaceStatusMessage);
        Assert.Single(vm.FilteredMarketplacePlugins);
    }

    [AvaloniaFact]
    public async Task Request_ShowsConfirmationWithPublisherAndSourceBeforeDownloading()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "1.0.0"))
        };
        using var vm = CreateViewModel(service);
        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        var item = vm.FilteredMarketplacePlugins.Single();

        vm.RequestMarketplaceInstallCommand.Execute(item);

        Assert.True(vm.IsInstallConfirmationVisible);
        Assert.Equal(item, vm.SelectedMarketplacePlugin);
        Assert.Contains("acme.one", vm.ConfirmationPrompt);
        Assert.Contains("Acme Publishing", vm.ConfirmationPrompt);
        Assert.Contains("publisher.example", item.SourceLabel);
        Assert.Empty(service.StagedReleases);

        vm.CancelMarketplaceInstallCommand.Execute(null);
        Assert.False(vm.IsInstallConfirmationVisible);
        Assert.Empty(service.StagedReleases);
    }

    [AvaloniaFact]
    public async Task Confirm_StagesReleaseAndBlocksDuplicateRequestsUntilRestart()
    {
        var item = Package("acme.one", "1.0.0");
        var service = new FakeMarketplaceService { Snapshot = SnapshotAt(item) };
        service.StageResult = PluginInstallResult.Success(PendingState("acme.one", "1.0.0"));
        using var vm = CreateViewModel(service);
        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        var plugin = vm.FilteredMarketplacePlugins.Single();

        vm.RequestMarketplaceInstallCommand.Execute(plugin);
        await vm.ConfirmMarketplaceInstallCommand.ExecuteAsync(null);

        Assert.Single(service.StagedReleases);
        Assert.Equal(item.BestRelease!.Version.Normalized, service.StagedReleases[0].Version.Normalized);
        Assert.False(vm.IsInstallConfirmationVisible);
        Assert.Equal(MarketplacePluginItemState.RestartRequired, plugin.State);
        Assert.False(plugin.CanInstallOrUpdate);
        Assert.True(plugin.HasStatusMessage);
        Assert.Contains("restart", plugin.StatusMessage, StringComparison.OrdinalIgnoreCase);

        vm.RequestMarketplaceInstallCommand.Execute(plugin);
        Assert.False(vm.IsInstallConfirmationVisible);
        await vm.ConfirmMarketplaceInstallCommand.ExecuteAsync(null);
        Assert.Single(service.StagedReleases);
    }

    [AvaloniaFact]
    public async Task Failure_IsRetryableAndReportsCurrentVersionAvailability()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "2.0.0"))
        };
        service.States["acme.one"] = ActiveState("acme.one", "1.0.0");
        service.StageResult = PluginInstallResult.Failure(
            PluginInstallFailureReason.DownloadFailed,
            "download failed",
            FailedState("acme.one", "1.0.0", "2.0.0"));
        using var vm = CreateViewModel(service);
        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        var plugin = vm.FilteredMarketplacePlugins.Single();

        vm.RequestMarketplaceInstallCommand.Execute(plugin);
        await vm.ConfirmMarketplaceInstallCommand.ExecuteAsync(null);

        Assert.True(plugin.HasError);
        Assert.Contains("download failed", plugin.ErrorMessage);
        Assert.True(plugin.ShowRetryAction);
        Assert.Contains("1.0.0", plugin.StatusMessage);

        vm.RequestMarketplaceInstallCommand.Execute(plugin);
        Assert.True(vm.IsInstallConfirmationVisible);
        await vm.ConfirmMarketplaceInstallCommand.ExecuteAsync(null);
        Assert.Equal(2, service.StagedReleases.Count);
    }

    [AvaloniaFact]
    public async Task Switch_BackToInstalledKeepsCatalogSnapshot()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "1.0.0"))
        };
        using var vm = CreateViewModel(service);
        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        Assert.True(vm.ShowPluginViewSwitcher);

        vm.ShowInstalledPluginsCommand.Execute(null);

        Assert.False(vm.IsMarketplaceBrowseSelected);
        Assert.True(vm.ShowInstalledPluginListSection);
        Assert.False(vm.ShowMarketplaceSection);
        Assert.Empty(service.StagedReleases);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        Assert.Single(service.Loads);
        Assert.Single(vm.FilteredMarketplacePlugins);
    }

    [AvaloniaFact]
    public async Task OverviewEmptyState_FollowsTheSelectedPackageSummary()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(PackageWithoutRelease("acme.blank"), Package("acme.full", "1.0.0"))
        };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        var blank = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.blank");
        Assert.False(blank.HasSummary);
        vm.SelectedMarketplacePlugin = blank;
        Assert.True(vm.ShowMarketplacePluginSummaryEmptyState);

        var full = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.full");
        vm.SelectedMarketplacePlugin = full;
        Assert.False(vm.ShowMarketplacePluginSummaryEmptyState);
    }

    private static OptionsDialogViewModel CreateViewModel(FakeMarketplaceService service)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginMarketplace(service);
        return vm;
    }

    private static PluginCatalogSnapshot SnapshotAt(params MarketplacePackage[] packages)
    {
        var source = new PluginSourceSnapshot
        {
            ManifestUri = new Uri("https://publisher.example/plugins.json"),
            Status = PluginSourceStatus.Loaded,
            Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Loaded, 1),
            Publisher = "Acme Publishing",
            Packages = packages
        };
        return new PluginCatalogSnapshot
        {
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CatalogStatus = PluginCatalogStatus.Loaded,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Loaded),
            CatalogEndpoint = new Uri("https://catalog.example/plugins.json"),
            Sources = [source],
            Packages = packages
        };
    }

    private static MarketplacePackage Package(
        string packageId, string version, bool compatible = true, string source = "https://publisher.example/plugins.json")
    {
        var sourceUri = new Uri(source);
        var release = new MarketplaceRelease
        {
            SourceUri = sourceUri,
            FeedUri = new Uri("https://feed.example/index.json"),
            PackageId = packageId,
            Version = PluginPackageVersion.Parse(version),
            MinHostVersion = new Version(3, 0, 0),
            Compatibility = compatible
                ? PluginCompatibilityReport.Eligible()
                : PluginCompatibilityReport.HostTooOld(new Version(9, 0, 0), new Version(3, 0, 0))
        };
        return new MarketplacePackage
        {
            SourceUri = sourceUri,
            PackageId = packageId,
            DisplayName = packageId,
            Publisher = "Acme Publishing",
            Summary = "Test package.",
            Releases = [release],
            BestRelease = compatible ? release : null
        };
    }

    private static MarketplacePackage PackageWithoutRelease(string packageId) => new()
    {
        SourceUri = new Uri("https://publisher.example/plugins.json"),
        PackageId = packageId,
        DisplayName = packageId,
        Publisher = "Acme Publishing"
    };

    private static PluginInstallState ActiveState(string pluginId, string activeVersion) => new()
    {
        PluginId = pluginId,
        PackageId = pluginId,
        Status = PluginInstallOperationStatus.Installed,
        ActiveVersion = activeVersion
    };

    private static PluginInstallState PendingState(string pluginId, string version) => new()
    {
        PluginId = pluginId,
        PackageId = pluginId,
        Status = PluginInstallOperationStatus.PendingRestart,
        PendingVersion = version
    };

    private static PluginInstallState FailedState(string pluginId, string activeVersion, string failedVersion) => new()
    {
        PluginId = pluginId,
        PackageId = pluginId,
        Status = PluginInstallOperationStatus.Failed,
        ActiveVersion = activeVersion,
        PendingVersion = failedVersion,
        FailureReason = PluginInstallFailureReason.DownloadFailed,
        Error = "download failed"
    };

    private sealed class FakeMarketplaceService : IPluginMarketplaceService
    {
        public List<(bool ForceRefresh, PluginCatalogSnapshot Snapshot)> Loads { get; } = [];

        public PluginCatalogSnapshot Snapshot { get; set; } = new()
        {
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CatalogStatus = PluginCatalogStatus.Loaded,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Loaded)
        };

        public Exception? LoadFailure { get; set; }

        public List<MarketplaceRelease> StagedReleases { get; } = [];

        public PluginInstallResult StageResult { get; set; } =
            PluginInstallResult.Success(PendingState("acme.one", "1.0.0"));

        public Dictionary<string, PluginInstallState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<PluginCatalogSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            Loads.Add((forceRefresh, Snapshot));
            if (LoadFailure is not null)
            {
                throw LoadFailure;
            }

            return Task.FromResult(Snapshot);
        }

        public Task<PluginInstallResult> StageInstallAsync(MarketplaceRelease release, CancellationToken cancellationToken)
        {
            StagedReleases.Add(release);
            return Task.FromResult(StageResult);
        }

        public IReadOnlyDictionary<string, PluginInstallState> GetInstallStates() => States;
    }
}
