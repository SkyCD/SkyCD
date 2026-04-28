using System.Collections.Generic;

namespace SkyCD.Presentation.ViewModels.Catalog;

public sealed class CatalogDocument
{
    public IReadOnlyList<CatalogEntry> Entries { get; init; } = [];

    public static CatalogDocument CreateDefault()
    {
        var entries = new List<CatalogEntry>
        {
            // Root level
            new CatalogEntry
            {
                Id = "library",
                Name = "Library",
                ParentId = null,
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 3
            },
            
            // Library subfolders
            new CatalogEntry
            {
                Id = "movies",
                Name = "Movies",
                ParentId = "library",
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 128
            },
            new CatalogEntry
            {
                Id = "music",
                Name = "Music",
                ParentId = "library",
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 340
            },
            new CatalogEntry
            {
                Id = "projects",
                Name = "Projects",
                ParentId = "library",
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 56
            },
            
            // Movies
            new CatalogEntry
            {
                Id = "interstellar",
                Name = "Interstellar.mkv",
                ParentId = "movies",
                Type = CatalogEntryType.Media,
                Size = 12100000000, // ~12.1 GB
                ChildrenCount = 0
            },
            new CatalogEntry
            {
                Id = "arrival",
                Name = "Arrival.mkv",
                ParentId = "movies",
                Type = CatalogEntryType.Media,
                Size = 9400000000, // ~9.4 GB
                ChildrenCount = 0
            },
            
            // Music
            new CatalogEntry
            {
                Id = "classical-collection",
                Name = "Classical Collection",
                ParentId = "music",
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 42
            },
            new CatalogEntry
            {
                Id = "concert-2025",
                Name = "Concert-2025.flac",
                ParentId = "music",
                Type = CatalogEntryType.Media,
                Size = 414000000, // ~414 MB
                ChildrenCount = 0
            },
            
            // Projects
            new CatalogEntry
            {
                Id = "skycd-v3",
                Name = "SkyCD v3",
                ParentId = "projects",
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 11
            },
            new CatalogEntry
            {
                Id = "plugin-benchmarks",
                Name = "Plugin Benchmarks",
                ParentId = "projects",
                Type = CatalogEntryType.Folder,
                Size = 0,
                ChildrenCount = 6
            }
        };

        return new CatalogDocument { Entries = entries };
    }
}
