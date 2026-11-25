using System;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Catalog;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// First-party host surface that exposes the open catalog repository to an enabled feature plugin.
/// It carries no feature-specific member: the plugin reads the repository type it owns.
/// </summary>
public interface ICatalogRepositoryProvider
{
    /// <summary>Gets the repository of the open catalog, or null when no catalog is open.</summary>
    CatalogDocumentRepository? Repository { get; }

    /// <summary>Gets the on-disk path of the open catalog, or null when none is open.</summary>
    string? CatalogPath { get; }

    /// <summary>Raised when the catalog opens, closes or its path changes.</summary>
    event EventHandler<CatalogLifecycle>? Changed;
}

/// <summary>Optional first-party hook the host calls to hand a feature plugin the open catalog repository.</summary>
public interface ICatalogRepositoryConsumer
{
    /// <summary>Receives the provider for the host process lifetime.</summary>
    void InitializeCatalogRepository(ICatalogRepositoryProvider provider);
}
