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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ContextMenus_NeverShowConsecutiveOrEdgeSeparators(bool hasMenuGroup, bool hasEntryCommand)
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var plugin = new FakeFeaturePlugin(
            commands: hasEntryCommand ? [new EntryCommandContribution("demo.action", "Action", 10)] : [],
            menus: hasMenuGroup ? [new MenuContribution("demo.contacts", "Contacts", "Demo", 10)] : []);
        vm.RefreshPluginMenuServices(new MenuExtensionManager([plugin]));
        vm.RefreshCatalogFeatureServices(new CatalogFeatureExtensionManager([plugin]));
        SelectFirstBrowserItem(vm);

        AssertSeparatorPlacement(vm.BrowserContextMenuItems);
        AssertSeparatorPlacement(vm.TreeContextMenuItems);
    }

    [Fact]
    public void MenuGroup_IsPrecededByExactlyOneSeparator()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var plugin = new FakeFeaturePlugin(
            commands: [],
            menus: [new MenuContribution("demo.contacts", "Contacts", "Demo", 10)]);
        vm.RefreshPluginMenuServices(new MenuExtensionManager([plugin]));
        SelectFirstBrowserItem(vm);

        foreach (var items in new[] { vm.BrowserContextMenuItems, vm.TreeContextMenuItems })
        {
            var index = items.ToList().FindIndex(item => item.Key == "feature_Demo");
            Assert.True(index > 1);
            Assert.Equal("-", items[index - 1].Header);
            Assert.NotEqual("-", items[index - 2].Header);
        }
    }

    private static void AssertSeparatorPlacement(IReadOnlyList<MainMenuItemViewModel> items)
    {
        Assert.NotEmpty(items);
        Assert.NotEqual("-", items[0].Header);
        Assert.NotEqual("-", items[^1].Header);
        for (var i = 1; i < items.Count; i++)
        {
            Assert.False(items[i - 1].Header == "-" && items[i].Header == "-",
                $"Two consecutive separators at index {i - 1}.");
        }
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
