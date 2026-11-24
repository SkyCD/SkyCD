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

/// <summary>View-model contract of the Plugins → Settings child page holding the staged path and repository list.</summary>
public sealed class OptionsPluginsSettingsPageTests
{
    [Fact]
    public void SettingCategories_NestSettingsPageUnderThePluginsGroup()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        var installed = Assert.Single(vm.SettingCategories, category => category.Id == "plugins");
        Assert.Equal("Installed", installed.Title);
        Assert.Equal("Plugins", installed.Group);
        Assert.Equal(0, installed.BuiltInIndex);

        var settings = Assert.Single(vm.SettingCategories, category => category.Id == "plugins-settings");
        Assert.Equal("Settings", settings.Title);
        Assert.Equal("Plugins", settings.Group);
        Assert.Equal(-1, settings.BuiltInIndex);
    }

    [Fact]
    public void Sidebar_ShowsPluginsGroupWithInstalledAndSettingsChildren()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        Assert.Contains(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == "Plugins");
        var children = vm.SidebarEntries
            .Where(entry => !entry.IsGroupHeader && entry.Category?.Group == "Plugins")
            .ToArray();

        Assert.Equal(["Installed", "Settings"], children.Select(entry => entry.Title));
        Assert.All(children, entry => Assert.Equal(1, entry.Depth));
    }

    [Fact]
    public void SelectingTheGroupHeader_OpensTheInstalledPage()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var group = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == "Plugins");

        vm.SelectedSidebarEntry = group;

        Assert.Equal("plugins", vm.SelectedSettingCategory?.Id);
        Assert.True(vm.ShowPluginListSection);
        Assert.False(vm.ShowPluginsSettingsSection);
    }

    [Fact]
    public void SelectingSettingsPage_SwapsTheListForThePathAndRepositorySection()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == "plugins-settings");

        Assert.True(vm.ShowPluginsSettingsSection);
        Assert.False(vm.ShowPluginListSection);
        Assert.Equal("Plugins  >  Settings", vm.CurrentCategoryPath);
    }

    [Fact]
    public void SetPluginRepositories_PutsTheBuiltInCatalogFirstAndSkipsDuplicates()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetPluginRepositories([
            vm.BuiltInCatalogUrl,
            "https://repo.example/plugins.json",
            " https://repo.example/plugins.json ",
            "   "
        ]);

        Assert.Equal(2, vm.PluginRepositories.Count);
        var builtIn = vm.PluginRepositories[0];
        Assert.True(builtIn.IsBuiltIn);
        Assert.False(builtIn.IsEditable);
        Assert.Equal(PluginCatalogOptions.DefaultCatalogEndpoint.AbsoluteUri, builtIn.Url);
        Assert.Equal("https://repo.example/plugins.json", vm.PluginRepositories[1].Url);
        Assert.Same(builtIn, vm.SelectedPluginRepository);
    }

    [Fact]
    public void AddAndRemove_OnlyStageUserRows()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginRepositories(null);
        Assert.False(vm.RemovePluginRepositoryCommand.CanExecute(null));

        vm.AddPluginRepositoryCommand.Execute(null);
        Assert.Equal(2, vm.PluginRepositories.Count);
        var added = vm.PluginRepositories[1];
        Assert.False(added.IsBuiltIn);
        Assert.Same(added, vm.SelectedPluginRepository);
        Assert.True(vm.RemovePluginRepositoryCommand.CanExecute(null));

        added.Url = "https://repo.example/plugins.json";
        vm.RemovePluginRepositoryCommand.Execute(null);

        Assert.Single(vm.PluginRepositories);
        Assert.True(vm.SelectedPluginRepository!.IsBuiltIn);
        Assert.False(vm.RemovePluginRepositoryCommand.CanExecute(null));
    }

    [Fact]
    public void GetPluginRepositories_ReturnsTheUserRowsWithoutTheBuiltInCatalog()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginRepositories(["https://repo.example/plugins.json"]);
        vm.AddPluginRepositoryCommand.Execute(null);

        Assert.Equal(["https://repo.example/plugins.json"], vm.GetPluginRepositories());

        vm.PluginRepositories[2].Url = "  https://other.example/plugins.json  ";
        Assert.Equal(["https://repo.example/plugins.json", "https://other.example/plugins.json"],
            vm.GetPluginRepositories());
    }

    [Fact]
    public void Confirm_InvalidRepositoryUrl_BlocksAcceptanceAndReportsInline()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginRepositories(["http://insecure.example/plugins.json"]);

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.DialogAccepted);
        var invalid = vm.PluginRepositories[1];
        Assert.True(invalid.HasError);
        Assert.False(string.IsNullOrWhiteSpace(invalid.Error));
        Assert.Equal("plugins-settings", vm.SelectedSettingCategory?.Id);
        Assert.True(vm.ShowPluginsSettingsSection);
    }

    [Fact]
    public void Confirm_ValidRepositoryUrls_NormalizeBeforeTheyAreSaved()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginRepositories(["  HTTPS://Repo.Example/plugins.json  "]);

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
        var repository = vm.PluginRepositories[1];
        Assert.False(repository.HasError);
        Assert.Equal("https://repo.example/plugins.json", repository.Url);
        Assert.Equal(["https://repo.example/plugins.json"], vm.GetPluginRepositories());
    }

    [Fact]
    public void ApplyPluginRepositoryStates_MapsCatalogAndSourceStatuses()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginRepositories([
            "https://offline.example/plugins.json",
            "https://bad.example/plugins.json",
            "https://unchecked.example/plugins.json"
        ]);

        vm.ApplyPluginRepositoryStates(new PluginCatalogSnapshot
        {
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CatalogStatus = PluginCatalogStatus.Loaded,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Loaded),
            Sources =
            [
                Source("https://offline.example/plugins.json", PluginSourceStatus.Offline),
                Source("https://bad.example/plugins.json", PluginSourceStatus.Rejected)
            ]
        });

        Assert.Equal(PluginRepositoryState.Ok, vm.PluginRepositories[0].State);
        Assert.Equal("OK", vm.PluginRepositories[0].StateLabel);
        Assert.Equal(PluginRepositoryState.Offline, vm.PluginRepositories[1].State);
        Assert.Equal("Offline", vm.PluginRepositories[1].StateLabel);
        Assert.Equal(PluginRepositoryState.Rejected, vm.PluginRepositories[2].State);
        Assert.Equal(PluginRepositoryState.Unknown, vm.PluginRepositories[3].State);

        vm.ApplyPluginRepositoryStates(null);

        Assert.All(vm.PluginRepositories, entry => Assert.Equal(PluginRepositoryState.Unknown, entry.State));
    }

    [Fact]
    public void SelectingSettingsPage_LoadsTheCatalogForLiveRepositoryStates()
    {
        var service = new RecordingMarketplaceService
        {
            Snapshot = new PluginCatalogSnapshot
            {
                ResolvedAt = DateTimeOffset.UnixEpoch,
                CatalogStatus = PluginCatalogStatus.Loaded,
                CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Loaded),
                Sources = [Source("https://repo.example/plugins.json", PluginSourceStatus.Offline)]
            }
        };
        using var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPluginMarketplace(service);
        vm.SetPluginRepositories(["https://repo.example/plugins.json"]);

        vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == "plugins-settings");

        // The fake marketplace answers synchronously, so the snapshot is applied by the time selection returns.
        Assert.Equal(1, service.LoadCount);
        Assert.Equal(PluginRepositoryState.Offline, vm.PluginRepositories[1].State);
    }

    private static PluginSourceSnapshot Source(string url, PluginSourceStatus status) => new()
    {
        ManifestUri = new Uri(url),
        Status = status,
        Detail = PluginSourceStatusDetail.Create(status, 0)
    };

    private sealed class RecordingMarketplaceService : IPluginMarketplaceService
    {
        public int LoadCount { get; private set; }

        public PluginCatalogSnapshot Snapshot { get; set; } = new()
        {
            ResolvedAt = DateTimeOffset.UnixEpoch,
            CatalogStatus = PluginCatalogStatus.Loaded,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.Loaded)
        };

        public Task<PluginCatalogSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(Snapshot);
        }

        public Task<PluginInstallResult> StageInstallAsync(MarketplaceRelease release,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(PluginInstallResult.Failure(
                PluginInstallFailureReason.DownloadFailed,
                "not used by these tests",
                PluginInstallState.NotInstalled(release.PackageId)));
        }

        public IReadOnlyDictionary<string, PluginInstallState> GetInstallStates() => new Dictionary<string, PluginInstallState>();
    }
}
