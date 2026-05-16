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
        var field = typeof(CatalogDocumentType).GetField(type.ToString(), BindingFlags.Public | BindingFlags.Static);
        var displayNameAttribute = field?.GetCustomAttribute<DisplayNameAttribute>();
        return displayNameAttribute?.Value ?? "Unknown";
    }
}