using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>
/// Domain-neutral surface through which a catalog feature plugin decorates entries and contributes details
/// columns, advanced-search fields, per-entry commands and per-entry Properties tabs. Implementations must
/// stay feature-agnostic: the host treats every contribution generically and names no specific feature.
/// </summary>
public interface ICatalogFeaturePluginCapability : IPluginCapability
{
    /// <summary>Raised when plugin-owned data changes so the host re-queries its decorations and columns.</summary>
    event EventHandler? DataChanged;

    /// <summary>Computes decorations for the given entries in one batch.</summary>
    IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries);

    /// <summary>Gets the details columns this plugin contributes.</summary>
    IReadOnlyList<DetailsColumnContribution> GetDetailsColumns();

    /// <summary>Gets the advanced-search fields this plugin contributes.</summary>
    IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields();

    /// <summary>Gets the per-entry context-menu commands this plugin contributes.</summary>
    IReadOnlyList<EntryCommandContribution> GetEntryCommands();

    /// <summary>Gets switchable toolbars that reference this feature's entry commands.</summary>
    IReadOnlyList<ToolbarContribution> GetToolbars() => [];

    /// <summary>Runs an entry command for a selection. By default only the primary entry is passed.</summary>
    Task<bool> ExecuteEntryCommandAsync(string commandId, IReadOnlyList<CatalogEntry> entries,
        CancellationToken cancellationToken = default) =>
        entries.Count == 0 ? Task.FromResult(false) : ExecuteEntryCommandAsync(commandId, entries[0], cancellationToken);

    /// <summary>Runs one contributed entry command and returns whether the host should refresh its projection.</summary>
    Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the plugin wants the host to open the entry's Properties dialog on one of its tabs.
    /// The host stays feature-agnostic; implementations without tabs never raise it.
    /// </summary>
    event EventHandler<EntryDetailsTabRequestEventArgs>? DetailsTabRequested
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Gets the optional tabs this plugin embeds in the host's Properties dialog for the given entry.
    /// Return an empty list when the entry cannot host the plugin's surface.
    /// </summary>
    IReadOnlyList<EntryDetailsTabContribution> GetEntryDetailsTabs(CatalogEntry entry) => [];
}
