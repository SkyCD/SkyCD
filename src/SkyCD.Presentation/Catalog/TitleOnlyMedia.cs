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
            [ContentsStatePropertyKey] = CatalogContentsState.NotScanned.ToString()
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
