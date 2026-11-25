using System;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Catalog;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Tracks the catalog repository and on-disk path for the currently open catalog so host API adapters and
/// feature plugins can project entries and open plugin storage without depending on a specific window.
/// </summary>
public sealed class OpenCatalogState : ICatalogRepositoryProvider
{
    private string? path;

    /// <summary>Gets the repository of the open catalog, or null when no catalog is open.</summary>
    public CatalogDocumentRepository? Repository { get; private set; }

    /// <summary>Gets or sets the on-disk path of the open catalog, or null when none is open.</summary>
    public string? Path
    {
        get => path;
        set
        {
            if (string.Equals(path, value, StringComparison.Ordinal))
            {
                return;
            }

            path = value;
            Changed?.Invoke(this, value is null ? CatalogLifecycle.Closed : CatalogLifecycle.Changed);
        }
    }

    /// <summary>Raised when the catalog opens, closes or its path changes.</summary>
    public event EventHandler<CatalogLifecycle>? Changed;

    string? ICatalogRepositoryProvider.CatalogPath => path;

    /// <summary>Attaches the repository of a newly opened catalog.</summary>
    public void Attach(CatalogDocumentRepository? repository)
    {
        if (repository is not null && ReferenceEquals(Repository, repository))
        {
            return;
        }

        Repository = repository;
        Changed?.Invoke(this, repository is null ? CatalogLifecycle.Closed : CatalogLifecycle.Opened);
    }
}
