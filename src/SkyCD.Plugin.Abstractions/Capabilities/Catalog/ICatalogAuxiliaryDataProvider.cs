using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>
/// Optional capability through which a feature plugin describes catalog-wide data it owns so the host
/// can include it in file-format loss assessments.
/// </summary>
public interface ICatalogAuxiliaryDataProvider : IPluginCapability
{
    /// <summary>Gets a snapshot of the plugin-owned catalog data currently present in the open catalog.</summary>
    IReadOnlyList<CatalogAuxiliaryDataGroup> GetAuxiliaryData();
}
