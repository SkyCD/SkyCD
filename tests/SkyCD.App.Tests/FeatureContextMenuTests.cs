using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class FeatureContextMenuTests
{
    [Fact]
    public void EntryCommandsAlreadyInAMenuGroup_AreNotRepeatedInTheContextMenu()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var plugin = new FakeFeaturePlugin(
            commands:
            [
                new EntryCommandContribution("demo.first", "First", 10),
                new EntryCommandContribution("demo.second", "Second", 20)
            ],
            menus:
            [
                new MenuContribution("demo.first", "First", "Demo", 10),
                new MenuContribution("demo.second", "Second", "Demo", 20),
                new MenuContribution("demo.contacts", "Contacts", "Demo", 30)
            ]);
        vm.RefreshPluginMenuServices(new MenuExtensionManager([plugin]));
        vm.RefreshCatalogFeatureServices(new CatalogFeatureExtensionManager([plugin]));
        SelectFirstBrowserItem(vm);

        var group = Assert.Single(vm.BrowserContextMenuItems, item => item.Key == "feature_Demo");

        Assert.Equal(["First", "Second", "Contacts"], group.Items.Select(item => item.Header));
        Assert.DoesNotContain(vm.BrowserContextMenuItems, item => item.Key == "demo.first");
    }

    [Fact]
    public void EntryCommandsWithoutAMenuGroup_AppearDirectlyInTheContextMenu()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var plugin = new FakeFeaturePlugin(
            commands: [new EntryCommandContribution("demo.first", "First", 10)],
            menus: []);
        vm.RefreshCatalogFeatureServices(new CatalogFeatureExtensionManager([plugin]));
        SelectFirstBrowserItem(vm);

        var item = Assert.Single(vm.BrowserContextMenuItems, entry => entry.Key == "demo.first");

        Assert.Equal("First", item.Header);
        Assert.DoesNotContain(vm.BrowserContextMenuItems, entry => entry.Key != null && entry.Key.StartsWith("feature_"));
    }

    private static void SelectFirstBrowserItem(MainWindowViewModel vm)
    {
        vm.SelectedTreeNode = vm.TreeNodes[0].Children.Single(node => node.Key == "movies");
        vm.SelectedBrowserItem = vm.BrowserItems.First();
    }

    private sealed class FakeFeaturePlugin(
        IReadOnlyList<EntryCommandContribution> commands,
        IReadOnlyCollection<MenuContribution> menus)
        : ICatalogFeaturePluginCapability, IMenuPluginCapability
    {
        public event EventHandler? DataChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries) => [];

        public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns() => [];

        public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields() => [];

        public IReadOnlyList<EntryCommandContribution> GetEntryCommands() => commands;

        public Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public IReadOnlyCollection<MenuContribution> GetMenuContributions() => menus;

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
