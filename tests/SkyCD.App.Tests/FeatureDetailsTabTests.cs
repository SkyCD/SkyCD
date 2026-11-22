using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class FeatureDetailsTabTests
{
    [Fact]
    public void PluginCommand_OpensPropertiesOnTheRequestedDetailsTab()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var plugin = new FakeDetailsFeaturePlugin();
        vm.RefreshPluginMenuServices(new MenuExtensionManager([plugin]));
        vm.RefreshCatalogFeatureServices(new CatalogFeatureExtensionManager([plugin]));
        vm.SelectedTreeNode = vm.TreeNodes[0].Children.Single(node => node.Key == "movies");
        vm.SelectedBrowserItem = vm.BrowserItems.First();

        PropertiesDialogRequestedEventArgs? requested = null;
        vm.PropertiesRequested += (_, args) => requested = args;

        var group = Assert.Single(vm.BrowserContextMenuItems, item => item.Key == "feature_Demo");
        group.Items.Single(item => item.Header == "Lend").Command!.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal("demo.details", requested.Dialog.InitialDetailsTabKey);
        Assert.Equal("demo.details", Assert.Single(requested.Dialog.DetailsTabs).Key);
        requested.Dialog.Dispose();
    }

    private sealed class FakeDetailsFeaturePlugin : ICatalogFeaturePluginCapability, IMenuPluginCapability
    {
        public event EventHandler? DataChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<EntryDetailsTabRequestEventArgs>? DetailsTabRequested;

        public IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries) => [];

        public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns() => [];

        public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields() => [];

        public IReadOnlyList<EntryCommandContribution> GetEntryCommands() => [];

        public Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry,
            CancellationToken cancellationToken = default)
        {
            DetailsTabRequested?.Invoke(this, new EntryDetailsTabRequestEventArgs(entry.Id, "demo.details"));
            return Task.FromResult(false);
        }

        public IReadOnlyList<EntryDetailsTabContribution> GetEntryDetailsTabs(CatalogEntry entry) =>
        [
            new("demo.details", "Demo", 0, () => new EntryDetailsTab(() => new object()))
        ];

        public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
        [
            new("demo.lend", "Lend", "Demo", 10)
        ];

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteEntryCommandAsync(commandId,
                new CatalogEntry("entry", "Entry", "/entry", null, CatalogEntryKind.File, 0,
                    new Dictionary<string, string>()), cancellationToken);
    }
}
