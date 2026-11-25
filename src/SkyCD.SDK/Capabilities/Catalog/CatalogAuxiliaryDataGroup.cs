namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>
/// Catalog-wide data owned by a feature plugin that file formats may not be able to store.
/// </summary>
/// <param name="Category">Stable category identifier used for host localization.</param>
/// <param name="Name">Item identifier inside the category.</param>
/// <param name="AffectedCount">Number of affected records.</param>
/// <param name="Sample">Optional short sample of affected names.</param>
public sealed record CatalogAuxiliaryDataGroup(
    string Category,
    string Name,
    int AffectedCount,
    string? Sample = null);
