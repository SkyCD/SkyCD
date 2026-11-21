using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>
/// Domain-neutral surface through which a catalog feature plugin decorates entries and contributes details
/// columns, advanced-search fields and per-entry commands. Implementations must stay feature-agnostic: the
/// host treats every contribution generically and names no specific feature.
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

    /// <summary>Runs one contributed entry command and returns whether the host should refresh its projection.</summary>
    Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry, CancellationToken cancellationToken = default);
}
