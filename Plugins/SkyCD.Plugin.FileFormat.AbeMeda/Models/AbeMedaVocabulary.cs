namespace SkyCD.Plugin.FileFormat.AbeMeda.Models;

/// <summary>
/// Element roles in an abeMeda/NeoFinder-family XML export.
/// </summary>
public enum AbeMedaElementKind
{
    Unknown,
    Transparent,
    Volume,
    Folder,
    File,
    Field
}

/// <summary>
/// Recognized catalog fields. Everything else is retained with a source-qualified key.
/// </summary>
public enum AbeMedaField
{
    Unknown,
    Name,
    Path,
    Size,
    Kind,
    Created,
    Modified,
    Accessed,
    Comment,
    Label,
    Keywords
}
