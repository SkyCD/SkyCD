using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Split Enable/Disable/Install button plus the confirmed Uninstall action of the JetBrains-style plugins page.
/// The uninstaller is always a fake here: no test deletes a real plugin folder.
/// </summary>
public sealed class OptionsPluginActionTests
{
    private const string PluginId = "skycd.plugin.demo";

    [Fact]
    public void EnabledPlugin_ShowsDisableAsPrimaryAction()
    {
        using var vm = CreateViewModel();
        vm.SetPlugins([Plugin("Demo")]);

        Assert.True(Assert.IsType<OptionsPluginItem>(vm.SelectedPlugin).IsEnabled);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginDisableAction), vm.PluginPrimaryActionLabel);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginEnabledActionToolTip),
            vm.PluginPrimaryActionToolTip);
    }

    [Fact]
    public void PrimaryAction_TogglesEnableState_AndPersistsOnlyDisabledIds()
    {
        using var vm = CreateViewModel();
        vm.SetPlugins([Plugin("Demo")]);

        vm.ToggleSelectedPluginEnabledCommand.Execute(null);

        Assert.False(vm.SelectedPlugin!.IsEnabled);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginEnableAction), vm.PluginPrimaryActionLabel);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginDisabledActionToolTip),
            vm.PluginPrimaryActionToolTip);
        Assert.Equal([PluginId], vm.GetDisabledPluginIds());

        vm.ToggleSelectedPluginEnabledCommand.Execute(null);

        Assert.True(vm.SelectedPlugin!.IsEnabled);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginDisableAction), vm.PluginPrimaryActionLabel);
        Assert.Empty(vm.GetDisabledPluginIds());
    }

    [Fact]
    public void DisabledPlugin_InitiallyShowsEnableAsPrimaryAction()
    {
        using var vm = CreateViewModel();
        vm.SetDisabledPluginIds([PluginId]);
        vm.SetPlugins([Plugin("Demo")]);

        Assert.False(vm.SelectedPlugin!.IsEnabled);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginEnableAction), vm.PluginPrimaryActionLabel);
    }

    [Fact]
    public void SelectingAnotherPlugin_UpdatesThePrimaryActionLabel()
    {
        using var vm = CreateViewModel();
        vm.SetDisabledPluginIds([PluginId]);
        vm.SetPlugins([Plugin("Demo"), new OptionsPluginItem("Other", "IFileFormatPluginCapability", "other v1", id: "other")]);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginEnableAction), vm.PluginPrimaryActionLabel);

        vm.SelectedPlugin = vm.Plugins[1];

        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginDisableAction), vm.PluginPrimaryActionLabel);
    }

    [Fact]
    public async Task NotInstalledMarketplacePackage_ShowsInstallWithoutUninstall()
    {
        var service = new FakeMarketplaceService { Snapshot = SnapshotAt(Package(PluginId, "1.0.0")) };
        using var vm = CreateViewModel(service);
        await vm.LoadMarketplaceAsync(forceRefresh: false);

        var item = Assert.Single(vm.FilteredMarketplacePlugins);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginInstallAction), item.StatusLabel);
        Assert.True(item.CanRunPrimaryAction);
        Assert.False(item.CanUninstall);
        Assert.Null(item.InstalledPluginId);
    }

    [Fact]
    public async Task InstalledMarketplacePackage_OffersUninstallAndNoPrimaryAction()
    {
        var service = new FakeMarketplaceService { Snapshot = SnapshotAt(Package(PluginId, "1.0.0")) };
        service.States[PluginId] = ActiveState(PluginId, "1.0.0");
        using var vm = CreateViewModel(service);
        await vm.LoadMarketplaceAsync(forceRefresh: false);

        var item = Assert.Single(vm.FilteredMarketplacePlugins);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginInstalledAction), item.StatusLabel);
        Assert.True(item.CanUninstall);
        Assert.Equal(PluginId, item.InstalledPluginId);
        Assert.False(item.CanRunPrimaryAction);
    }

    [Fact]
    public async Task Uninstall_RequiresConfirmation_ThenDeletesAndRefreshes()
    {
        var uninstaller = new FakeUninstaller();
        using var vm = CreateViewModel();
        vm.SetPluginUninstaller(uninstaller);
        vm.SetPlugins([Plugin("Demo")]);
        var refreshes = 0;
        vm.RefreshPluginsRequested += (_, _) => refreshes++;

        vm.RequestPluginUninstallCommand.Execute(null);

        Assert.True(vm.HasPluginUninstallConfirmation);
        Assert.Equal(LocalizationService.Current.Format(TextKeys.PluginUninstallConfirmation, "Demo"),
            vm.PluginUninstallConfirmationPrompt);
        Assert.Empty(uninstaller.Calls);
        Assert.Single(vm.Plugins);

        vm.CancelPluginUninstallCommand.Execute(null);
        Assert.False(vm.HasPluginUninstallConfirmation);
        Assert.Empty(uninstaller.Calls);

        vm.RequestPluginUninstallCommand.Execute(null);
        uninstaller.Result = PluginInstallResult.Success(PluginInstallState.NotInstalled(PluginId));
        await vm.ConfirmPluginUninstallCommand.ExecuteAsync(null);

        Assert.Equal([PluginId], uninstaller.Calls);
        Assert.False(vm.HasPluginUninstallConfirmation);
        Assert.Empty(vm.Plugins);
        Assert.Empty(vm.FilteredPlugins);
        Assert.Null(vm.SelectedPlugin);
        Assert.Empty(vm.GetDisabledPluginIds());
        Assert.Equal(1, refreshes);
        Assert.False(vm.HasPluginActionAlert);
    }

    [Fact]
    public async Task FailedUninstall_KeepsThePluginListedWithItsDisabledState()
    {
        var uninstaller = new FakeUninstaller();
        using var vm = CreateViewModel();
        vm.SetPluginUninstaller(uninstaller);
        vm.SetDisabledPluginIds([PluginId]);
        vm.SetPlugins([Plugin("Demo")]);
        var refreshes = 0;
        vm.RefreshPluginsRequested += (_, _) => refreshes++;

        vm.RequestPluginUninstallCommand.Execute(null);
        uninstaller.Result = PluginInstallResult.Failure(
            PluginInstallFailureReason.NotCatalogOwned,
            "not catalog owned",
            ActiveState(PluginId, "1.0.0"));
        await vm.ConfirmPluginUninstallCommand.ExecuteAsync(null);

        var plugin = Assert.Single(vm.Plugins);
        Assert.False(plugin.IsEnabled);
        Assert.Equal([PluginId], vm.GetDisabledPluginIds());
        Assert.Equal(0, refreshes);
        Assert.True(vm.HasPluginActionAlert);
        Assert.Equal(LocalizationService.Current.Format(TextKeys.PluginUninstallFailed, "Demo",
            LocalizationService.Current.Get(TextKeys.PluginUninstallNotCatalogOwned)), vm.PluginActionAlertMessage);
    }

    [Fact]
    public async Task Uninstall_ReportsEachRefusalWithItsOwnReason()
    {
        var cases = new (PluginInstallFailureReason Reason, string Key)[]
        {
            (PluginInstallFailureReason.InvalidPluginId, TextKeys.PluginUninstallInvalidId),
            (PluginInstallFailureReason.UnsafeInstallPath, TextKeys.PluginUninstallUnsafePath),
            (PluginInstallFailureReason.DestinationUnavailable, TextKeys.PluginUninstallFilesInUse)
        };

        foreach (var (reason, key) in cases)
        {
            var uninstaller = new FakeUninstaller();
            using var vm = CreateViewModel();
            vm.SetPluginUninstaller(uninstaller);
            vm.SetPlugins([Plugin("Demo")]);

            vm.RequestPluginUninstallCommand.Execute(null);
            uninstaller.Result = PluginInstallResult.Failure(reason, "refused", ActiveState(PluginId, "1.0.0"));
            await vm.ConfirmPluginUninstallCommand.ExecuteAsync(null);

            Assert.Equal(LocalizationService.Current.Format(TextKeys.PluginUninstallFailed, "Demo",
                LocalizationService.Current.Get(key)), vm.PluginActionAlertMessage);
            Assert.Single(vm.Plugins);
        }
    }

    [Fact]
    public async Task UninstallFromMarketplacePane_RefreshesTheInstalledAndMarketplaceLists()
    {
        var service = new FakeMarketplaceService { Snapshot = SnapshotAt(Package(PluginId, "1.0.0")) };
        service.States[PluginId] = ActiveState(PluginId, "1.0.0");
        var uninstaller = new FakeUninstaller
        {
            Result = PluginInstallResult.Success(PluginInstallState.NotInstalled(PluginId))
        };
        using var vm = CreateViewModel(service);
        vm.SetPluginUninstaller(uninstaller);
        vm.SetPlugins([Plugin("Demo")]);
        await vm.LoadMarketplaceAsync(forceRefresh: false);
        var item = Assert.Single(vm.FilteredMarketplacePlugins);
        vm.SelectedMarketplacePlugin = item;

        vm.RequestMarketplacePluginUninstallCommand.Execute(null);
        await vm.ConfirmPluginUninstallCommand.ExecuteAsync(null);

        Assert.Equal([PluginId], uninstaller.Calls);
        Assert.Empty(vm.Plugins);
        Assert.False(item.IsInstalled);
        Assert.False(item.CanUninstall);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginInstallAction), item.StatusLabel);
    }

    [Fact]
    public async Task UninstallWithoutInstaller_IsRefusedWithoutConfirmation()
    {
        using var vm = CreateViewModel();
        vm.SetPlugins([Plugin("Demo")]);

        vm.RequestPluginUninstallCommand.Execute(null);

        Assert.False(vm.HasPluginUninstallConfirmation);
        await vm.ConfirmPluginUninstallCommand.ExecuteAsync(null);
        Assert.Single(vm.Plugins);
    }

    [Fact]
    public void ChangingSelection_DismissesAnOpenUninstallConfirmation()
    {
        using var vm = CreateViewModel();
        vm.SetPluginUninstaller(new FakeUninstaller());
        vm.SetPlugins([Plugin("Demo"), new OptionsPluginItem("Other", "IFileFormatPluginCapability", "other v1", id: "other")]);
        vm.RequestPluginUninstallCommand.Execute(null);
        Assert.True(vm.HasPluginUninstallConfirmation);

        vm.SelectedPlugin = vm.Plugins[1];

        Assert.False(vm.HasPluginUninstallConfirmation);
    }

    private static OptionsPluginItem Plugin(string name) =>
        new(name, "IFileFormatPluginCapability", "demo v1.0.0", id: PluginId);

    private static OptionsDialogViewModel CreateViewModel(FakeMarketplaceService? service = null)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        if (service is not null)
        {
            vm.SetPluginMarketplace(service);
        }

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

    private static MarketplacePackage Package(string packageId, string version)
    {
        var sourceUri = new Uri("https://publisher.example/plugins.json");
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
            DisplayName = packageId,
            Publisher = "Acme Publishing",
            Summary = "Test package.",
            Releases = [release],
            BestRelease = release
        };
    }

    private static PluginInstallState ActiveState(string pluginId, string activeVersion) => new()
    {
        PluginId = pluginId,
        PackageId = pluginId,
        Status = PluginInstallOperationStatus.Installed,
        ActiveVersion = activeVersion
    };

    private sealed class FakeUninstaller : IPluginPackageUninstaller
    {
        public List<string> Calls { get; } = [];

        public PluginInstallResult Result { get; set; } =
            PluginInstallResult.Success(PluginInstallState.NotInstalled(PluginId));

        public Task<PluginInstallResult> UninstallAsync(string pluginId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(pluginId);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeMarketplaceService : IPluginMarketplaceService
    {
        public PluginCatalogSnapshot Snapshot { get; set; } = new()
        {
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CatalogStatus = PluginCatalogStatus.Loaded,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Loaded)
        };

        public Dictionary<string, PluginInstallState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<PluginCatalogSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<PluginInstallResult> StageInstallAsync(MarketplaceRelease release,
            CancellationToken cancellationToken) =>
            Task.FromResult(PluginInstallResult.Failure(PluginInstallFailureReason.DownloadFailed,
                "not used by these tests", ActiveState(release.PackageId, "1.0.0")));

        public IReadOnlyDictionary<string, PluginInstallState> GetInstallStates() => States;
    }
}
