using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Formatting;

namespace SkyCD.Documents;

[CouchbaseDocument("catalog", repositoryType: typeof(CatalogDocumentRepository))]
public sealed class CatalogDocument
{
    [Id] public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    [ParentId] public string? ParentId { get; init; }

    public CatalogDocumentType Type { get; init; } = CatalogDocumentType.File;

    public long Size { get; init; }

    public long ChildrenCount { get; init; }

    public string? Status { get; set; }

    public PropertiesCollection Properties { get; init; } = new();

    public string DisplayType => Type.ToDisplayName();

    public string DisplaySize => SizeFormatting.FormatBytes(Size, "0.##");

    public string IconGlyph => ResolveIconGlyph();

    private string ResolveIconGlyph()
    {
        if (Properties.TryGetValue("Icon", out var iconValue) && iconValue is not null)
        {
            if (iconValue is byte[] bytes)
            {
                return $"data:image/x-icon;base64,{System.Convert.ToBase64String(bytes)}";
            }

            var iconStr = iconValue.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(iconStr))
            {
                return iconStr;
            }
        }

        if (Type == CatalogDocumentType.File)
        {
            var ext = System.IO.Path.GetExtension(Name);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                return $"ext:{ext.TrimStart('.').ToLowerInvariant()}";
            }
        }

        return Type.ResolveIconGlyph();
    }
}
