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
                new BrowserItem("Movies", "Folder", "128 items", "folder"),
                new BrowserItem("Music", "Folder", "340 items", "folder"),
                new BrowserItem("Projects", "Folder", "56 items", "folder")
            ],
            "movies" =>
            [
                new BrowserItem("Interstellar.mkv", "Video", "12.1 GB", "video"),
                new BrowserItem("Arrival.mkv", "Video", "9.4 GB", "video")
            ],
            "music" =>
            [
                new BrowserItem("Classical Collection", "Folder", "42 items", "folder"),
                new BrowserItem("Concert-2025.flac", "Audio", "414 MB", "audio")
            ],
            "projects" =>
            [
                new BrowserItem("SkyCD v3", "Folder", "11 items", "folder"),
                new BrowserItem("Plugin Benchmarks", "Folder", "6 items", "folder")
            ],
            _ => []
        };
    }
}