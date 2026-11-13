using System.Collections.Generic;
using SkyCD.Plugin.WinCatalog.Csv;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// A single catalog entry element parsed from a WinCatalog XML report.
/// </summary>
/// <param name="Index">Provisional parse index; child items always precede their parent.</param>
/// <param name="Kind">Semantic element kind (media, folder, or file).</param>
/// <param name="ParentIndex">Provisional index of the enclosing entry element, or -1 when none.</param>
/// <param name="HasItemChildren">True when the element encloses other entry elements (a structural container).</param>
/// <param name="Fields">Mapped WinCatalog fields collected from attributes and child elements.</param>
/// <param name="IconValue">Unmapped icon passthrough value, when present.</param>
public sealed record WinCatalogXmlItem(
    int Index,
    WinCatalogXmlElementKind Kind,
    int ParentIndex,
    bool HasItemChildren,
    IReadOnlyDictionary<WinCatalogCsvColumn, string> Fields,
    string? IconValue);
