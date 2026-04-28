using Couchbase.Lite;
using Couchbase.Lite.Mapping;
using System.Collections.Generic;

namespace SkyCD.App.Services.Documents;

public sealed class CatalogDocument
{
    public IReadOnlyList<Dictionary<string, object?>> Entries { get; init; } = [];

    public static CatalogDocument CreateDefault()
    {
        var entries = new List<Dictionary<string, object?>>
        {
            // Root level
            new Dictionary<string, object?>
            {
                { "Id", "library" },
                { "Name", "Library" },
                { "ParentId", null },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 3L }
            },
            
            // Library subfolders
            new Dictionary<string, object?>
            {
                { "Id", "movies" },
                { "Name", "Movies" },
                { "ParentId", "library" },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 128L }
            },
            new Dictionary<string, object?>
            {
                { "Id", "music" },
                { "Name", "Music" },
                { "ParentId", "library" },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 340L }
            },
            new Dictionary<string, object?>
            {
                { "Id", "projects" },
                { "Name", "Projects" },
                { "ParentId", "library" },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 56L }
            },
            
            // Movies
            new Dictionary<string, object?>
            {
                { "Id", "interstellar" },
                { "Name", "Interstellar.mkv" },
                { "ParentId", "movies" },
                { "Type", "Media" },
                { "Size", 12100000000L },
                { "ChildrenCount", 0L }
            },
            new Dictionary<string, object?>
            {
                { "Id", "arrival" },
                { "Name", "Arrival.mkv" },
                { "ParentId", "movies" },
                { "Type", "Media" },
                { "Size", 9400000000L },
                { "ChildrenCount", 0L }
            },
            
            // Music
            new Dictionary<string, object?>
            {
                { "Id", "classical-collection" },
                { "Name", "Classical Collection" },
                { "ParentId", "music" },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 42L }
            },
            new Dictionary<string, object?>
            {
                { "Id", "concert-2025" },
                { "Name", "Concert-2025.flac" },
                { "ParentId", "music" },
                { "Type", "Media" },
                { "Size", 414000000L },
                { "ChildrenCount", 0L }
            },
            
            // Projects
            new Dictionary<string, object?>
            {
                { "Id", "skycd-v3" },
                { "Name", "SkyCD v3" },
                { "ParentId", "projects" },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 11L }
            },
            new Dictionary<string, object?>
            {
                { "Id", "plugin-benchmarks" },
                { "Name", "Plugin Benchmarks" },
                { "ParentId", "projects" },
                { "Type", "Folder" },
                { "Size", 0L },
                { "ChildrenCount", 6L }
            }
        };

        return new CatalogDocument { Entries = entries };
    }

    public static CatalogDocument? FromDocument(Document? document)
    {
        return document?.ToObject<CatalogDocument>();
    }
}