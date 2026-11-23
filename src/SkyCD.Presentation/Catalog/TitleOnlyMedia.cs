using System;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.Catalog;

public static class TitleOnlyMedia
{
    public const string ContentsStatePropertyKey = "ContentsState";

    public static CatalogDocument Create(string? mediaName, string? parentId)
    {
        if (string.IsNullOrWhiteSpace(mediaName))
        {
            throw new MediaNameRequiredException();
        }

        var properties = new PropertiesCollection
        {
            [ContentsStatePropertyKey] = CatalogContentsState.NotScanned.ToString(),
            // Nothing was measured for a title-only entry, so a zero here must not read as a real 0 b.
            [CatalogDocument.SizeKnownPropertyKey] = false
        };

        return new CatalogDocument
        {
            Id = $"media-{Guid.NewGuid():N}",
            Name = mediaName.Trim(),
            ParentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId,
            Type = CatalogDocumentType.Media,
            Size = 0,
            ChildrenCount = 0,
            ContentsState = CatalogContentsState.NotScanned,
            Properties = properties
        };
    }
}
