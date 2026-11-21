using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Tracks the catalog repository and on-disk path for the currently open catalog so host API adapters can
/// project entries and open plugin storage without depending on a specific window or view model.
/// </summary>
public sealed class OpenCatalogState
{
    /// <summary>Gets the repository of the open catalog, or null when no catalog is open.</summary>
    public CatalogDocumentRepository? Repository { get; private set; }

    /// <summary>Gets or sets the on-disk path of the open catalog, or null when none is open.</summary>
    public string? Path { get; set; }

    /// <summary>Attaches the repository of a newly opened catalog.</summary>
    public void Attach(CatalogDocumentRepository? repository) => Repository = repository;
}
