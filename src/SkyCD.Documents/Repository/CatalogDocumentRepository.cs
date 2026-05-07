using System.Collections.Generic;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents.Enum;

namespace SkyCD.Documents.Repository;

public sealed class CatalogDocumentRepository : TreeRepository
{
    public IReadOnlyList<CatalogDocument> CreateDefaultEntries()
    {
        return
        [
            new CatalogDocument
            {
                Id = "library",
                Name = "Library",
                ParentId = null,
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 3L
            },
            new CatalogDocument
            {
                Id = "movies",
                Name = "Movies",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 128L
            },
            new CatalogDocument
            {
                Id = "music",
                Name = "Music",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 340L
            },
            new CatalogDocument
            {
                Id = "projects",
                Name = "Projects",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 56L
            },
            new CatalogDocument
            {
                Id = "interstellar",
                Name = "Interstellar.mkv",
                ParentId = "movies",
                Type = CatalogDocumentType.Media,
                Size = 12100000000L,
                ChildrenCount = 0L
            },
            new CatalogDocument
            {
                Id = "arrival",
                Name = "Arrival.mkv",
                ParentId = "movies",
                Type = CatalogDocumentType.Media,
                Size = 9400000000L,
                ChildrenCount = 0L
            },
            new CatalogDocument
            {
                Id = "classical-collection",
                Name = "Classical Collection",
                ParentId = "music",
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 42L
            },
            new CatalogDocument
            {
                Id = "concert-2025",
                Name = "Concert-2025.flac",
                ParentId = "music",
                Type = CatalogDocumentType.Media,
                Size = 414000000L,
                ChildrenCount = 0L
            },
            new CatalogDocument
            {
                Id = "skycd-v3",
                Name = "SkyCD v3",
                ParentId = "projects",
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 11L
            },
            new CatalogDocument
            {
                Id = "plugin-benchmarks",
                Name = "Plugin Benchmarks",
                ParentId = "projects",
                Type = CatalogDocumentType.Folder,
                Size = 0L,
                ChildrenCount = 6L
            }
        ];
    }
}
