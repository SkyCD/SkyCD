using System.Collections.Generic;
using System;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.ViewModels;

public sealed class InMemoryBrowserDataStore : IBrowserDataStore
{
    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var moviesNode = new BrowserTreeNode("movies", "Movies", "folder");
        var musicNode = new BrowserTreeNode("music", "Music", "folder");
        var projectsNode = new BrowserTreeNode("projects", "Projects", "folder");

        var libraryNode = new BrowserTreeNode(
            "library",
            "Library",
            "cd",
            [moviesNode, musicNode, projectsNode],
            true);

        return [libraryNode];
    }

    public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
    {
        return nodeKey.ToLowerInvariant() switch
        {
            "library" =>
            [
                new BrowserItem("Movies", "Folder", "128 items", "folder") { Id = "movies" },
                new BrowserItem("Music", "Folder", "340 items", "folder") { Id = "music" },
                new BrowserItem("Projects", "Folder", "56 items", "folder") { Id = "projects" }
            ],
            "movies" =>
            [
                new BrowserItem("Interstellar.mkv", "Video", "12.1 GB", "video") { Id = "interstellar" },
                new BrowserItem("Arrival.mkv", "Video", "9.4 GB", "video") { Id = "arrival" }
            ],
            "music" =>
            [
                new BrowserItem("Classical Collection", "Folder", "42 items", "folder") { Id = "classical-collection" },
                new BrowserItem("Concert-2025.flac", "Audio", "414 MB", "audio") { Id = "concert-2025" }
            ],
            "projects" =>
            [
                new BrowserItem("SkyCD v3", "Folder", "11 items", "folder") { Id = "skycd-v3" },
                new BrowserItem("Plugin Benchmarks", "Folder", "6 items", "folder") { Id = "plugin-benchmarks" }
            ],
            _ => []
        };
    }

    public PropertiesCollection GetBrowserItemInfoProperties(string itemId)
    {
        return itemId.ToLowerInvariant() switch
        {
            "interstellar" => new PropertiesCollection(new Dictionary<string, object?>(StringComparer.CurrentCultureIgnoreCase)
            {
                ["Codec"] = "H.264",
                ["Resolution"] = "1920x1080"
            }),
            "arrival" => new PropertiesCollection(new Dictionary<string, object?>(StringComparer.CurrentCultureIgnoreCase)
            {
                ["Codec"] = "H.265",
                ["Resolution"] = "1920x1080"
            }),
            "concert-2025" => new PropertiesCollection(new Dictionary<string, object?>(StringComparer.CurrentCultureIgnoreCase)
            {
                ["Bitrate"] = "320 kbps",
                ["Format"] = "FLAC"
            }),
            _ => new PropertiesCollection()
        };
    }
}
