using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Toolbars;

namespace SkyCD.Plugin.Host.Catalog;

/// <summary>
/// Host facade that aggregates catalog feature contributions (decorations, details columns, advanced-search
/// fields, per-entry commands and per-entry Properties tabs) from every enabled feature plugin and isolates
/// a failing plugin from the rest. It is domain-neutral: no specific feature is referenced.
/// </summary>
public sealed class CatalogFeatureExtensionManager
{
    private readonly IReadOnlyList<ICatalogFeaturePluginCapability> _capabilities;

    /// <summary>Raised when any contributing plugin signals that its derived data changed.</summary>
    public event EventHandler? DataChanged;

    /// <summary>Raised when a contributing plugin asks the host to open one of its entry details tabs.</summary>
    public event EventHandler<EntryDetailsTabRequestEventArgs>? DetailsTabRequested;

    public CatalogFeatureExtensionManager(IEnumerable<ICatalogFeaturePluginCapability> catalogFeatureCapabilities)
    {
        _capabilities = catalogFeatureCapabilities.ToList();
        foreach (var capability in _capabilities)
        {
            capability.DataChanged += OnCapabilityDataChanged;
            capability.DetailsTabRequested += OnCapabilityDetailsTabRequested;
        }
    }

    /// <summary>Computes decorations across all plugins for the given entries, in one pass per plugin.</summary>
    public IReadOnlyList<EntryDecoration> GetDecorations(IReadOnlyList<CatalogEntry> entries)
    {
        var decorations = new List<EntryDecoration>();
        foreach (var capability in _capabilities)
        {
            try
            {
                decorations.AddRange(capability.GetEntryDecorations(entries));
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(capability.GetType(), "catalog", exception);
            }
        }

        return decorations;
    }

    /// <summary>Gets every contributed details column, de-duplicated by key and ordered.</summary>
    public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns() => Aggregate(
        static capability => capability.GetDetailsColumns(),
        static column => column.Key,
        static column => column.Order);

    /// <summary>Gets every contributed advanced-search field, de-duplicated by key and ordered by title.</summary>
    public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields()
    {
        var fields = new List<AdvancedSearchFieldContribution>();
        foreach (var capability in _capabilities)
        {
            try
            {
                fields.AddRange(capability.GetAdvancedSearchFields());
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(capability.GetType(), "catalog", exception);
            }
        }

        return fields
            .DistinctBy(static field => field.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static field => field.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Gets every contributed per-entry command, de-duplicated by id and ordered.</summary>
    public IReadOnlyList<EntryCommandContribution> GetEntryCommands() => Aggregate(
        static capability => capability.GetEntryCommands(),
        static command => command.CommandId,
        static command => command.Order);

    public IReadOnlyList<ToolbarContribution> GetToolbars() => Aggregate(
        capability =>
        {
            var ids = capability.GetEntryCommands().Select(command => command.CommandId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return capability.GetToolbars().Select(toolbar => toolbar with
            {
                Buttons = toolbar.Buttons.Where(button => ids.Contains(button.CommandId)).ToArray()
            }).ToArray();
        },
        static toolbar => toolbar.Id,
        static toolbar => toolbar.Order);

    public static bool CanExecute(EntryCommandContribution command, IReadOnlyList<CatalogEntry> entries) =>
        entries.Count > 0 && (command.AppliesTo is not { } predicate || entries.All(predicate));

    /// <summary>Gets every contributed Properties tab for the entry, de-duplicated by key and ordered.</summary>
    public IReadOnlyList<EntryDetailsTabContribution> GetEntryDetailsTabs(CatalogEntry entry) => Aggregate(
        capability => capability.GetEntryDetailsTabs(entry),
        static tab => tab.Key,
        static tab => tab.Order);

    /// <summary>Runs a contributed command on the owning plugin and reports whether the host should refresh.</summary>
    public Task<bool> ExecuteEntryCommandAsync(
        string commandId,
        CatalogEntry entry,
        CancellationToken cancellationToken = default) =>
        ExecuteEntryCommandAsync(commandId, [entry], cancellationToken);

    public async Task<bool> ExecuteEntryCommandAsync(string commandId, IReadOnlyList<CatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        foreach (var capability in _capabilities)
        {
            try
            {
                var command = capability.GetEntryCommands().FirstOrDefault(contribution =>
                    contribution.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase));
                if (command is not null)
                {
                    return CanExecute(command, entries)
                        && await capability.ExecuteEntryCommandAsync(commandId, entries, cancellationToken)
                            .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(capability.GetType(), "catalog", exception);
            }
        }

        return false;
    }

    private IReadOnlyList<T> Aggregate<T>(
        Func<ICatalogFeaturePluginCapability, IReadOnlyList<T>> selector,
        Func<T, string> keySelector,
        Func<T, int> orderSelector)
    {
        var results = new List<T>();
        foreach (var capability in _capabilities)
        {
            try
            {
                results.AddRange(selector(capability));
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(capability.GetType(), "catalog", exception);
            }
        }

        return results
            .DistinctBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .OrderBy(orderSelector)
            .ToList();
    }

    private void OnCapabilityDataChanged(object? sender, EventArgs e) => DataChanged?.Invoke(this, EventArgs.Empty);

    private void OnCapabilityDetailsTabRequested(object? sender, EntryDetailsTabRequestEventArgs e) =>
        DetailsTabRequested?.Invoke(sender, e);
}
