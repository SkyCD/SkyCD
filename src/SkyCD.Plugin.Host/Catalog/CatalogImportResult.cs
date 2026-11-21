using System.Collections.Generic;

namespace SkyCD.Plugin.Host.Catalog;

/// <summary>Aggregate of every post-import hook outcome across the enabled plugins.</summary>
public sealed record CatalogImportResult(IReadOnlyList<string> Warnings, int AffectedEntries);
