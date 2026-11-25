using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>View-model surface of the JetBrains-style plug-ins page added for the redesigned Options pane.</summary>
public sealed class OptionsPluginsPageTests
{
    [Fact]
    public async Task PluginSearchText_FiltersInstalledAndMarketplaceLists()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(
                Package("acme.xml", "1.0.0", displayName: "XML"),
                Package("acme.json", "1.0.0", displayName: "JSON"))
        };
        using var vm = CreateViewModel(service);
        vm.SetPlugins(
        [
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "acme.xml"),
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "acme.json")
        ]);
        await vm.LoadMarketplaceAsync(forceRefresh: false);
        Assert.Equal(2, vm.FilteredMarketplacePlugins.Count);

        vm.PluginSearchText = "xml";

        Assert.Equal("xml", vm.PluginFilterText);
        Assert.Equal("xml", vm.MarketplaceFilterText);
        Assert.Equal(["XML"], vm.FilteredPlugins.Select(plugin => plugin.Name).ToArray());
        Assert.Equal("acme.xml", Assert.Single(vm.FilteredMarketplacePlugins).PackageId);
        Assert.Equal(1, vm.InstalledPluginsCount);
        Assert.Equal(1, vm.MarketplacePluginsCount);
        Assert.Contains("1", vm.InstalledPluginsHeader);
        Assert.Contains("1", vm.MarketplacePluginsHeader);

        vm.PluginSearchText = string.Empty;
        Assert.Equal(2, vm.FilteredPlugins.Count);
        Assert.Equal(2, vm.FilteredMarketplacePlugins.Count);
    }

    [Fact]
    public async Task MarketplaceRows_UseTheCatalogIconAndFallBackToTheNeutralGlyph()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(
                Package("acme.xml", "1.0.0", displayName: "XML", icon: "Lucide:Archive"),
                Package("acme.json", "1.0.0", displayName: "JSON", icon: "https://evil.example/icon.png"),
                Package("acme.csv", "1.0.0", displayName: "CSV"))
        };
        using var vm = CreateViewModel(service);

        await vm.LoadMarketplaceAsync(forceRefresh: false);

        var byId = vm.FilteredMarketplacePlugins.ToDictionary(item => item.PackageId);
        Assert.Equal("Lucide:Archive", byId["acme.xml"].IconKey);
        Assert.True(byId["acme.xml"].HasGlyphIcon);
        Assert.Equal("lucide:Puzzle", byId["acme.json"].IconKey);
        Assert.Equal("lucide:Puzzle", byId["acme.csv"].IconKey);
    }

    [Fact]
    public async Task EmptyCatalog_ShowsCatalogEmptyStateInsteadOfFilterMessage()
    {
        var service = new FakeMarketplaceService();
        using var vm = CreateViewModel(service);

        await vm.ToggleMarketplaceSectionCommand.ExecuteAsync(null);

        Assert.True(vm.ShowMarketplacePaneEmptyState);
        Assert.False(vm.ShowMarketplacePaneFilterEmptyState);
        Assert.False(vm.ShowMarketplacePaneList);
        Assert.False(vm.ShowMarketplaceStatusMessage);
        Assert.Equal(string.Empty, vm.MarketplaceStatusMessage);
    }

    [Fact]
    public async Task FilterWithNoMatches_ShowsEmptyStatesAndKeepsHeaderCounts()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.xml", "1.0.0", displayName: "XML"))
        };
        using var vm = CreateViewModel(service);
        vm.SetPlugins([new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "acme.xml")]);
        await vm.LoadMarketplaceAsync(forceRefresh: false);
        await vm.ToggleMarketplaceSectionCommand.ExecuteAsync(null);

        Assert.False(vm.ShowInstalledPluginsEmptyState);
        Assert.True(vm.ShowMarketplacePaneList);
        Assert.False(vm.ShowMarketplacePaneFilterEmptyState);
        Assert.False(vm.ShowMarketplacePaneEmptyState);

        vm.PluginSearchText = "no-such-plugin";

        Assert.True(vm.ShowInstalledPluginsEmptyState);
        Assert.False(vm.ShowMarketplacePaneList);
        Assert.True(vm.ShowMarketplacePaneFilterEmptyState);
        Assert.False(vm.ShowMarketplacePaneEmptyState);
        Assert.Empty(vm.FilteredPlugins);
        Assert.Empty(vm.FilteredMarketplacePlugins);
        Assert.Equal(0, vm.InstalledPluginsCount);
        Assert.Equal(0, vm.MarketplacePluginsCount);
        Assert.Contains("0", vm.InstalledPluginsHeader);
        Assert.Contains("0", vm.MarketplacePluginsHeader);
        Assert.False(vm.ShowMarketplaceStatusMessage);

        vm.PluginSearchText = string.Empty;

        Assert.False(vm.ShowInstalledPluginsEmptyState);
        Assert.True(vm.ShowMarketplacePaneList);
        Assert.False(vm.ShowMarketplacePaneFilterEmptyState);
    }

    [Fact]
    public async Task CatalogFailure_ShowsOnlyTheFailureStatusWithoutEmptyStateMessages()
    {
        var service = new FakeMarketplaceService { LoadFailure = new InvalidOperationException("network down") };
        using var vm = CreateViewModel(service);

        await vm.ToggleMarketplaceSectionCommand.ExecuteAsync(null);

        Assert.True(vm.ShowMarketplaceStatusMessage);
        Assert.False(vm.ShowMarketplacePaneEmptyState);
        Assert.False(vm.ShowMarketplacePaneFilterEmptyState);
        Assert.False(vm.ShowMarketplacePaneList);
    }

    [Fact]
    public async Task MarketplaceSection_StartsCollapsed_AndExpandsWithOneLoad()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "1.0.0"))
        };
        using var vm = CreateViewModel(service);

        Assert.False(vm.IsMarketplaceSectionExpanded);
        Assert.False(vm.ShowMarketplaceSectionContent);
        Assert.Empty(service.Loads);

        await vm.ToggleMarketplaceSectionCommand.ExecuteAsync(null);

        Assert.True(vm.IsMarketplaceSectionExpanded);
        Assert.True(vm.ShowMarketplaceSectionContent);
        Assert.False(vm.ShowMarketplacePaneEmptyState);
        var load = Assert.Single(service.Loads);
        Assert.False(load.ForceRefresh);

        await vm.ToggleMarketplaceSectionCommand.ExecuteAsync(null);
        Assert.False(vm.IsMarketplaceSectionExpanded);
        await vm.ToggleMarketplaceSectionCommand.ExecuteAsync(null);
        Assert.True(vm.IsMarketplaceSectionExpanded);
        Assert.Single(service.Loads);
    }

    [Fact]
    public async Task UpdateAllCommand_RequiresAnAvailableUpdate_AndForcesRefresh()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "1.0.0"))
        };
        // Staged for the next restart: no further install or update may be offered until the host restarts.
        service.States["acme.one"] = PendingState("acme.one", "1.0.0");
        using var vm = CreateViewModel(service);
        await vm.LoadMarketplaceAsync(forceRefresh: false);
        Assert.False(vm.HasAvailableMarketplaceUpdates);
        // The installed rescan the dialog runs on Save is not observable here, so the gated action is the contract.
        Assert.False(CanUpdateAllPlugins(vm));

        service.Snapshot = SnapshotAt(Package("acme.one", "2.0.0"));
        service.States["acme.one"] = ActiveState("acme.one", "1.0.0");
        await vm.RefreshMarketplaceCommand.ExecuteAsync(null);

        Assert.Equal(2, service.Loads.Count);
        Assert.True(service.Loads[^1].ForceRefresh);
        Assert.True(vm.SelectedMarketplacePlugin!.UpdateAvailable);
        Assert.True(vm.HasAvailableMarketplaceUpdates);
        Assert.True(CanUpdateAllPlugins(vm));

        var refreshed = false;
        vm.RefreshPluginsRequested += (_, _) => refreshed = true;
        await vm.UpdateAllPluginsCommand.ExecuteAsync(null);

        Assert.True(refreshed);
        Assert.Equal(3, service.Loads.Count);
        Assert.True(service.Loads[^1].ForceRefresh);
    }

    [Fact]
    public async Task SelectingItems_SwitchesTheDetailPane()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.first", "1.0.0"), Package("acme.second", "1.0.0"))
        };
        using var vm = CreateViewModel(service);
        var installed = new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "acme.xml");
        vm.SetPlugins([installed]);
        await vm.LoadMarketplaceAsync(forceRefresh: false);
        // Loading a catalog auto-selects its first item, which switches the detail pane to the catalog.
        Assert.True(vm.IsMarketplaceBrowseSelected);

        vm.ShowInstalledPluginsCommand.Execute(null);
        Assert.False(vm.IsMarketplaceBrowseSelected);
        Assert.Same(installed, vm.SelectedPlugin);
        Assert.Equal("xml v2.0.0", vm.SelectedPluginDetailsVersion);
        Assert.Equal("acme.xml", vm.SelectedPluginDetailsId);
        Assert.Equal("IFileFormatPluginCapability", vm.SelectedPluginDetailsType);
        Assert.Equal(string.Empty, vm.SelectedPluginDetailsExtension);
        Assert.False(vm.HasSelectedPluginDetailsExtension);

        vm.SelectedMarketplacePlugin = vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.second");
        Assert.True(vm.IsMarketplaceBrowseSelected);
        Assert.Same(vm.FilteredMarketplacePlugins.Single(item => item.PackageId == "acme.second"), vm.SelectedMarketplacePlugin);
    }

    [Fact]
    public void ConfigurationTab_LoadsTheSelectedPluginsPage()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins([new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "acme.xml")]);

        Assert.Null(vm.SelectedPluginConfigurationPage);
        Assert.False(vm.HasPluginConfigurationPage);
        Assert.False(vm.IsPluginConfigurationTabSelected);

        vm.IsPluginConfigurationTabSelected = true;
        Assert.Null(vm.SelectedPluginConfigurationPage);
    }

    /// <summary>
    /// Regression guard for the pre-existing marketplace Browse view: the plug-ins page additions must not change
    /// the "installed vs. catalog" switch, the load-once behavior or the empty-state reporting.
    /// </summary>
    [Fact]
    public async Task MarketplaceBrowse_KeepsItsFirstLoadAndSectionSwitch()
    {
        var service = new FakeMarketplaceService
        {
            Snapshot = SnapshotAt(Package("acme.one", "1.0.0"))
        };
        using var vm = CreateViewModel(service);

        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);
        await vm.ShowMarketplacePluginsCommand.ExecuteAsync(null);

        Assert.True(vm.ShowMarketplaceSection);
        Assert.False(vm.ShowInstalledPluginListSection);
        Assert.True(vm.ShowPluginViewSwitcher);
        var load = Assert.Single(service.Loads);
        Assert.False(load.ForceRefresh);

        vm.ShowInstalledPluginsCommand.Execute(null);
        Assert.False(vm.IsMarketplaceBrowseSelected);
        Assert.True(vm.ShowInstalledPluginListSection);
        Assert.False(vm.ShowMarketplaceSection);
    }

    /// <summary>Evaluates the private update-all guard; the cached async-command CanExecute state is not polled here.</summary>
    private static bool CanUpdateAllPlugins(OptionsDialogViewModel vm) =>
        (bool)typeof(OptionsDialogViewModel)
            .GetMethod("CanUpdateAllPlugins", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(vm, [])!;

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
            Detail = PluginSourceStatusDetail.Create(PluginSourceStatus.Loaded, packages.Length),
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
        string packageId,
        string version,
        string? displayName = null,
        string source = "https://publisher.example/plugins.json",
        string? icon = null)
    {
        var sourceUri = new Uri(source);
        var release = new MarketplaceRelease
        {
            SourceUri = sourceUri,
            FeedUri = new Uri("https://feed.example/index.json"),
            PackageId = packageId,
            Version = PluginPackageVersion.Parse(version),
            MinHostVersion = new Version(3, 0, 0),
            Compatibility = PluginCompatibilityReport.Eligible()
        };
        return new MarketplacePackage
        {
            SourceUri = sourceUri,
            PackageId = packageId,
            DisplayName = displayName ?? packageId,
            Publisher = "Acme Publishing",
            Summary = "Test package.",
            Icon = icon,
            Releases = [release],
            BestRelease = release
        };
    }

    private static PluginInstallState PendingState(string pluginId, string pendingVersion) => new()
    {
        PluginId = pluginId,
        PackageId = pluginId,
        Status = PluginInstallOperationStatus.PendingRestart,
        PendingVersion = pendingVersion
    };

    private static PluginInstallState ActiveState(string pluginId, string activeVersion) => new()
    {
        PluginId = pluginId,
        PackageId = pluginId,
        Status = PluginInstallOperationStatus.Installed,
        ActiveVersion = activeVersion
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

        public List<MarketplaceRelease> StagedReleases { get; } = [];

        public Exception? LoadFailure { get; set; }

        public Dictionary<string, PluginInstallState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<PluginCatalogSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (LoadFailure is not null)
            {
                return Task.FromException<PluginCatalogSnapshot>(LoadFailure);
            }

            // Record the snapshot and the install states handed out for that load, not live references.
            LastStates = new Dictionary<string, PluginInstallState>(States, StringComparer.OrdinalIgnoreCase);
            Loads.Add((forceRefresh, Snapshot));
            return Task.FromResult(Snapshot);
        }

        public Task<PluginInstallResult> StageInstallAsync(MarketplaceRelease release, CancellationToken cancellationToken)
        {
            StagedReleases.Add(release);
            return Task.FromResult(PluginInstallResult.Failure(
                PluginInstallFailureReason.DownloadFailed,
                "not used by these tests",
                ActiveState(release.PackageId, "1.0.0")));
        }

        private Dictionary<string, PluginInstallState>? LastStates { get; set; }

        public IReadOnlyDictionary<string, PluginInstallState> GetInstallStates() =>
            LastStates ?? States;
    }
}
