using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Host.Catalog;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class CatalogFeatureExtensionManagerTests
{
    private static CatalogEntry Entry(string id = "1") =>
        new(id, "name", "a/b", null, CatalogEntryKind.File, 1, new Dictionary<string, string>());

    [Fact]
    public void GetDetailsColumns_DeduplicatesByKeyAndOrders()
    {
        var a = new FakeFeatureCapability(columns:
        [
            new DetailsColumnContribution("b", "B", 2, _ => null),
            new DetailsColumnContribution("a", "A", 1, _ => null)
        ]);
        var b = new FakeFeatureCapability(columns:
        [
            new DetailsColumnContribution("a", "dup", 5, _ => null),
            new DetailsColumnContribution("c", "C", 0, _ => null)
        ]);

        var manager = new CatalogFeatureExtensionManager([a, b]);
        var columns = manager.GetDetailsColumns();

        Assert.Equal(new[] { "c", "a", "b" }, columns.Select(static column => column.Key).ToArray());
    }

    [Fact]
    public void GetDecorations_ConcatenatesAcrossPlugins()
    {
        var a = new FakeFeatureCapability(decorations: [new EntryDecoration("1", "x", null, null, false)]);
        var b = new FakeFeatureCapability(decorations: [new EntryDecoration("2", "y", null, null, true)]);

        var manager = new CatalogFeatureExtensionManager([a, b]);
        var decorations = manager.GetDecorations([Entry("1"), Entry("2")]);

        Assert.Equal(2, decorations.Count);
    }

    [Fact]
    public void GetDetailsColumns_IsolatesFailingPlugin()
    {
        var failing = new FakeFeatureCapability(columnsError: true);
        var healthy = new FakeFeatureCapability(columns: [new DetailsColumnContribution("k", "K", 0, _ => null)]);

        var manager = new CatalogFeatureExtensionManager([failing, healthy]);
        var columns = manager.GetDetailsColumns();

        Assert.Single(columns);
        Assert.Equal("k", columns[0].Key);
    }

    [Fact]
    public void DataChanged_ForwardsFromCapability()
    {
        var capability = new FakeFeatureCapability();
        var manager = new CatalogFeatureExtensionManager([capability]);
        var raised = false;
        manager.DataChanged += (_, _) => raised = true;

        capability.RaiseDataChanged();

        Assert.True(raised);
    }

    [Fact]
    public async Task ExecuteEntryCommand_RoutesToOwningPlugin()
    {
        var capability = new FakeFeatureCapability(
            commands: [new EntryCommandContribution("x", "X", 0)],
            commandResult: true);
        var manager = new CatalogFeatureExtensionManager([capability]);

        Assert.True(await manager.ExecuteEntryCommandAsync("x", Entry()));
        Assert.False(await manager.ExecuteEntryCommandAsync("unknown", Entry()));
    }

    [Fact]
    public async Task ExecuteEntryCommand_IsolatesFailingPlugin()
    {
        var capability = new FakeFeatureCapability(
            commands: [new EntryCommandContribution("x", "X", 0)],
            commandError: true);
        var manager = new CatalogFeatureExtensionManager([capability]);

        Assert.False(await manager.ExecuteEntryCommandAsync("x", Entry()));
    }

    private sealed class FakeFeatureCapability(
        IReadOnlyList<EntryDecoration>? decorations = null,
        IReadOnlyList<DetailsColumnContribution>? columns = null,
        IReadOnlyList<AdvancedSearchFieldContribution>? searchFields = null,
        IReadOnlyList<EntryCommandContribution>? commands = null,
        bool columnsError = false,
        bool commandError = false,
        bool commandResult = false) : ICatalogFeaturePluginCapability
    {
        public event EventHandler? DataChanged;

        public IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries) =>
            decorations ?? [];

        public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns()
        {
            if (columnsError)
            {
                throw new InvalidOperationException("boom");
            }

            return columns ?? [];
        }

        public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields() => searchFields ?? [];

        public IReadOnlyList<EntryCommandContribution> GetEntryCommands() => commands ?? [];

        public Task<bool> ExecuteEntryCommandAsync(
            string commandId, CatalogEntry entry, CancellationToken cancellationToken = default)
        {
            if (commandError)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(commandResult);
        }

        public void RaiseDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
