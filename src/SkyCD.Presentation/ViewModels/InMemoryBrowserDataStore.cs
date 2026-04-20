namespace SkyCD.Presentation.ViewModels;

public sealed class InMemoryBrowserDataStore : IBrowserDataStore
{
    private readonly IReadOnlyList<BrowserTreeNode>? customTreeNodes;
    private readonly Dictionary<string, IReadOnlyList<BrowserItem>>? customItemsByNodeKey;

    public InMemoryBrowserDataStore()
    {
    }

    public InMemoryBrowserDataStore(IReadOnlyList<BrowserTreeNode> treeNodes)
    {
        customTreeNodes = treeNodes;
    }

    public InMemoryBrowserDataStore(IReadOnlyList<BrowserTreeNode> treeNodes, Dictionary<string, IReadOnlyList<BrowserItem>> itemsByNodeKey)
    {
        customTreeNodes = treeNodes;
        customItemsByNodeKey = itemsByNodeKey;
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        return customTreeNodes ?? GetDefaultTreeNodes();
    }

    private static IReadOnlyList<BrowserTreeNode> GetDefaultTreeNodes()
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
        if (customItemsByNodeKey is not null && customItemsByNodeKey.TryGetValue(nodeKey, out var customItems))
        {
            return customItems;
        }

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
