using System;

namespace SkyCD.Documents.Enum;

[AttributeUsage(AttributeTargets.Field)]
public sealed class CatalogDocumentTypeIconGlyphAttribute(string glyph) : Attribute
{
    public string Glyph { get; } = glyph;
}