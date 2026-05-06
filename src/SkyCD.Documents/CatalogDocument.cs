using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;

namespace SkyCD.Documents;

[CouchbaseDocument("catalog")]
public sealed class CatalogDocument
{
    [Id]
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    [ParentId]
    public string? ParentId { get; init; }

    public string Type { get; init; } = "File";

    public long Size { get; init; }

    public long ChildrenCount { get; init; }

    public static IReadOnlyList<CatalogDocument> CreateDefaultEntries()
    {
        return
        [
            new() { Id = "library", Name = "Library", ParentId = null, Type = "Folder", Size = 0L, ChildrenCount = 3L },
            new() { Id = "movies", Name = "Movies", ParentId = "library", Type = "Folder", Size = 0L, ChildrenCount = 128L },
            new() { Id = "music", Name = "Music", ParentId = "library", Type = "Folder", Size = 0L, ChildrenCount = 340L },
            new() { Id = "projects", Name = "Projects", ParentId = "library", Type = "Folder", Size = 0L, ChildrenCount = 56L },
            new() { Id = "interstellar", Name = "Interstellar.mkv", ParentId = "movies", Type = "Media", Size = 12100000000L, ChildrenCount = 0L },
            new() { Id = "arrival", Name = "Arrival.mkv", ParentId = "movies", Type = "Media", Size = 9400000000L, ChildrenCount = 0L },
            new() { Id = "classical-collection", Name = "Classical Collection", ParentId = "music", Type = "Folder", Size = 0L, ChildrenCount = 42L },
            new() { Id = "concert-2025", Name = "Concert-2025.flac", ParentId = "music", Type = "Media", Size = 414000000L, ChildrenCount = 0L },
            new() { Id = "skycd-v3", Name = "SkyCD v3", ParentId = "projects", Type = "Folder", Size = 0L, ChildrenCount = 11L },
            new() { Id = "plugin-benchmarks", Name = "Plugin Benchmarks", ParentId = "projects", Type = "Folder", Size = 0L, ChildrenCount = 6L }
        ];
    }
}
