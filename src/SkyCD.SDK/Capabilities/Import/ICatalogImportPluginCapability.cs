using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Catalog;

namespace SkyCD.SDK.Capabilities.Import;

/// <summary>
/// Result of one plugin post-import pass: user-facing warnings and how many entries the plugin acted on.
/// A null result means the plugin found nothing to do.
/// </summary>
public sealed record ImportOutcome(IReadOnlyList<string> Warnings, int AffectedEntries);

/// <summary>
/// Optional capability the host invokes after imported entries are written, so a feature plugin converts
/// plain entry properties into its own storage. This replaces the former hard-coded per-feature import
/// coupling in the catalog load path; the property keys are a plugin-to-plugin contract the core never sees.
/// </summary>
public interface ICatalogImportPluginCapability : IPluginCapability
{
    /// <summary>Converts freshly imported entries into the plugin's own storage and reports any warnings.</summary>
    /// <param name="entries">The entries written by the import.</param>
    /// <param name="cancellationToken">Cancels the pass; the host still isolates and bounds each plugin.</param>
    /// <returns>The import outcome, or null when the plugin had nothing to do.</returns>
    Task<ImportOutcome?> OnEntriesImportedAsync(
        IReadOnlyList<CatalogEntry> entries,
        CancellationToken cancellationToken = default);
}
