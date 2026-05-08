using System.Collections.Generic;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;

namespace SkyCD.App.Tests;

internal static class TestCatalogEntries
{
    public static IReadOnlyList<CatalogDocument> Default()
    {
        return
        [
            new CatalogDocument
            {
                Id = "library",
                Name = "Library",
                ParentId = null,
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 3
            },
            new CatalogDocument
            {
                Id = "movies",
                Name = "Movies",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 128
            },
            new CatalogDocument
            {
                Id = "music",
                Name = "Music",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 340
            },
            new CatalogDocument
            {
                Id = "projects",
                Name = "Projects",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 56
            },
            new CatalogDocument
            {
                Id = "interstellar",
                Name = "Interstellar.mkv",
                ParentId = "movies",
                Type = CatalogDocumentType.Media,
                Size = 12100000000,
                ChildrenCount = 0,
                Properties = new PropertiesCollection(new Dictionary<string, object?>
                {
                    ["Codec"] = "H.264",
                    ["Resolution"] = "1920x1080"
                })
            },
            new CatalogDocument
            {
                Id = "arrival",
                Name = "Arrival.mkv",
                ParentId = "movies",
                Type = CatalogDocumentType.Media,
                Size = 9400000000,
                ChildrenCount = 0,
                Properties = new PropertiesCollection(new Dictionary<string, object?>
                {
                    ["Codec"] = "H.265",
                    ["Resolution"] = "1920x1080"
                })
            },
            new CatalogDocument
            {
                Id = "classical-collection",
                Name = "Classical Collection",
                ParentId = "music",
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 42
            },
            new CatalogDocument
            {
                Id = "concert-2025",
                Name = "Concert-2025.flac",
                ParentId = "music",
                Type = CatalogDocumentType.Media,
                Size = 414000000,
                ChildrenCount = 0,
                Properties = new PropertiesCollection(new Dictionary<string, object?>
                {
                    ["Bitrate"] = "320 kbps",
                    ["Format"] = "FLAC"
                })
            },
            new CatalogDocument
            {
                Id = "skycd-v3",
                Name = "SkyCD v3",
                ParentId = "projects",
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 11
            },
            new CatalogDocument
            {
                Id = "plugin-benchmarks",
                Name = "Plugin Benchmarks",
                ParentId = "projects",
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 6
            }
        ];
    }
}
