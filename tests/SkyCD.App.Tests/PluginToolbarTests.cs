using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Catalog;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class PluginToolbarTests
{
    [Fact]
    public async Task SwitchingRestoresLayoutAndUpdatesSelectionAwareCommands()
    {
        var plugin = new TestFeature();
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(
        [
            new CatalogDocument { Id = "folder", Name = "Folder", Type = CatalogDocumentType.Folder },
            new CatalogDocument { Id = "disc", Name = "Disc", Type = CatalogDocumentType.Media, ParentId = "folder" },
            new CatalogDocument { Id = "file", Name = "File", Type = CatalogDocumentType.File, ParentId = "folder" }
        ]);
        vm.SelectedTreeNode = Assert.Single(vm.TreeNodes, node => node.Key == "folder");
        vm.RefreshCatalogFeatureServices(new CatalogFeatureExtensionManager([plugin]));

        var toolbarMenu = Assert.Single(vm.ViewMenuItems, item => item.Key == "view_toolbars");
        Assert.Equal(MenuItemToggleType.Radio, toolbarMenu.Items[0].ToggleType);
        Assert.True(toolbarMenu.Items[0].IsChecked);

        toolbarMenu.Items[1].Command!.Execute(null);
        Assert.True(toolbarMenu.Items[1].IsChecked);
        Assert.False(toolbarMenu.Items[0].IsChecked);
        Assert.Equal(TestFeature.CommandId, Assert.Single(vm.ToolbarEntries).CommandId);

        var command = Assert.Single(vm.ToolbarCommands(trackSelection: true)).Command!;
        var changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;
        var disc = Assert.Single(vm.BrowserItems, item => item.Id == "disc");
        var file = Assert.Single(vm.BrowserItems, item => item.Id == "file");

        vm.SelectedBrowserItems = new List<CatalogDocument> { disc };
        Assert.True(command.CanExecute(null));
        Assert.True(changes > 0);
        Assert.True(Assert.Single(vm.BrowserContextMenuItems, item => item.Key == TestFeature.CommandId).IsEnabled);

        vm.SelectedBrowserItems = new List<CatalogDocument> { disc, file };
        Assert.False(command.CanExecute(null));
        Assert.False(Assert.Single(vm.BrowserContextMenuItems, item => item.Key == TestFeature.CommandId).IsEnabled);
        Assert.False(await new CatalogFeatureExtensionManager([plugin]).ExecuteEntryCommandAsync(TestFeature.CommandId,
            [new CatalogEntry("file", "File", "/File", null, CatalogEntryKind.File, 0, new Dictionary<string, string>())]));
        Assert.Equal(0, plugin.Executions);

        vm.ApplyToolbarLayout([]);
        var options = new AppOptionsDocument();
        vm.SaveToolbarSettings(options);
        toolbarMenu.Items[0].Command!.Execute(null);
        Assert.Equal(MainWindowViewModel.FileToolbarId, vm.ActiveToolbarId);

        vm.LoadToolbarLayout(options.Toolbar, options.ActiveToolbarId, options.PluginToolbarLayouts);
        Assert.Equal(TestFeature.ToolbarId, vm.ActiveToolbarId);
        Assert.Empty(vm.ToolbarEntries);
    }

    private sealed class TestFeature : ICatalogFeaturePluginCapability
    {
        public const string ToolbarId = "test.toolbar";
        public const string CommandId = "test.command";

        public int Executions { get; private set; }

        public event EventHandler? DataChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries) => [];

        public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns() => [];

        public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields() => [];

        public IReadOnlyList<EntryCommandContribution> GetEntryCommands() =>
        [
            new(CommandId, "Test action", 0) { AppliesTo = entry => entry.Kind == CatalogEntryKind.Media }
        ];

        public IReadOnlyList<ToolbarContribution> GetToolbars() =>
        [
            new(ToolbarId, "Test toolbar", 0, [new(CommandId, ToolbarIconKeys.Properties), new("missing", "ignored")])
        ];

        public Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(true);
        }
    }
}
