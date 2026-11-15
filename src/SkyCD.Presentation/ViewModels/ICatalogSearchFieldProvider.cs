using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

/// <summary>Resolves the contributed searchable fields of one catalog entry.</summary>
public delegate IEnumerable<CatalogSearchField> CatalogSearchFieldLookup(CatalogDocument document);

/// <summary>
/// Contributes searchable fields beyond the catalog path. The find dialog matches whatever a provider
/// returns, so newly captured metadata becomes searchable without the search UI learning anything about the
/// media format or plugin that produced it.
/// </summary>
public interface ICatalogSearchFieldProvider
{
    /// <summary>
    /// Opens one search pass. Called once per search rather than once per entry, so an implementation can
    /// read its stored data in bulk instead of turning a catalog-wide search into one query per entry.
    /// </summary>
    CatalogSearchFieldLookup OpenLookup();
}
