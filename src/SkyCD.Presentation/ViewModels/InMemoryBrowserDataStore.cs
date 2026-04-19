namespace SkyCD.Presentation.ViewModels;

public sealed class InMemoryBrowserDataStore : IBrowserDataStore
{
    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var moviesNode = new BrowserTreeNode("movies", "Movies", "🎬");
        var musicNode = new BrowserTreeNode("music", "Music", "🎵");
        var projectsNode = new BrowserTreeNode("projects", "Projects", "🗂");

        var libraryNode = new BrowserTreeNode(
            "library",
            "Library",
            "📚",
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
                new BrowserItem("Movies", "Folder", "128 items", "📁"),
                new BrowserItem("Music", "Folder", "340 items", "📁"),
                new BrowserItem("Projects", "Folder", "56 items", "📁")
            ],
            "movies" =>
            [
                new BrowserItem("Interstellar.mkv", "Video", "12.1 GB", "🎞"),
                new BrowserItem("Arrival.mkv", "Video", "9.4 GB", "🎞")
            ],
            "music" =>
            [
                new BrowserItem("Classical Collection", "Folder", "42 items", "📁"),
                new BrowserItem("Concert-2025.flac", "Audio", "414 MB", "🎧")
            ],
            "projects" =>
            [
                new BrowserItem("SkyCD v3", "Folder", "11 items", "📁"),
                new BrowserItem("Plugin Benchmarks", "Folder", "6 items", "📁")
            ],
            _ => []
        };
    }
}
