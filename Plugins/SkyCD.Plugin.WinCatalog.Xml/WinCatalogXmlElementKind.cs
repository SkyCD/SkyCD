namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Semantic element kinds recognized in WinCatalog XML report exports.
/// </summary>
public enum WinCatalogXmlElementKind
{
    /// <summary>Wrapper or metadata element that is not a catalog entry.</summary>
    Transparent = 0,

    /// <summary>Storage media element (disk, disc, drive, volume).</summary>
    Media,

    /// <summary>Folder or directory element.</summary>
    Folder,

    /// <summary>File element.</summary>
    File
}
