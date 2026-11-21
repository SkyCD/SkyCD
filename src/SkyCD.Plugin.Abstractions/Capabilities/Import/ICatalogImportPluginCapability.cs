using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;

namespace SkyCD.Plugin.Abstractions.Capabilities.Import;

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
    Task<ImportOutcome?> OnEntriesImportedAsync(
        IReadOnlyList<CatalogEntry> entries,
        CancellationToken cancellationToken = default);
}
