using System.Reflection;

namespace SkyCD.Documents.Enum;

public static class CatalogDocumentTypeExtensions
{
    public static string ResolveIconGlyph(this CatalogDocumentType type)
    {
        var field = typeof(CatalogDocumentType).GetField(type.ToString(), BindingFlags.Public | BindingFlags.Static);
        var glyphAttribute = field?.GetCustomAttribute<CatalogDocumentTypeIconGlyphAttribute>();
        return glyphAttribute?.Glyph ?? "file";
    }

    public static string ToDisplayName(this CatalogDocumentType type)
    {
        return type switch
        {
            CatalogDocumentType.File => "File",
            CatalogDocumentType.Media => "Media",
            CatalogDocumentType.Folder => "Folder",
            CatalogDocumentType.NetworkResource => "Network Resource",
            _ => "Unknown"
        };
    }
}
