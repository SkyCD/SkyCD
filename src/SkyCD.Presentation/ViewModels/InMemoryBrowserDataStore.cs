namespace SkyCD.Presentation.ViewModels;

public sealed class InMemoryBrowserDataStore : IBrowserDataStore
{
    private readonly IReadOnlyDictionary<string, string> _translations;

    public InMemoryBrowserDataStore() : this(new Dictionary<string, string>())
    {
    }

    public InMemoryBrowserDataStore(IReadOnlyDictionary<string, string> translations)
    {
        _translations = translations ?? throw new ArgumentNullException(nameof(translations));
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var moviesNode = new BrowserTreeNode("movies", _translations.TryGetValue("BrowserDataStore.Movies", out var moviesText) ? moviesText : "Movies", "folder");
        var musicNode = new BrowserTreeNode("music", _translations.TryGetValue("BrowserDataStore.Music", out var musicText) ? musicText : "Music", "folder");
        var projectsNode = new BrowserTreeNode("projects", _translations.TryGetValue("BrowserDataStore.Projects", out var projectsText) ? projectsText : "Projects", "folder");

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
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Library.Movies", out var moviesLibText) ? moviesLibText : "Movies", "Folder", "128 items", "folder"),
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Library.Music", out var musicLibText) ? musicLibText : "Music", "Folder", "340 items", "folder"),
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Library.Projects", out var projectsLibText) ? projectsLibText : "Projects", "Folder", "56 items", "folder")
            ],
            "movies" =>
            [
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Movies.Interstellar", out var interstellarText) ? interstellarText : "Interstellar.mkv", "Video", "12.1 GB", "video"),
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Movies.Arrival", out var arrivalText) ? arrivalText : "Arrival.mkv", "Video", "9.4 GB", "video")
            ],
            "music" =>
            [
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Music.ClassicalCollection", out var classicalText) ? classicalText : "Classical Collection", "Folder", "42 items", "folder"),
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Music.Concert2025", out var concertText) ? concertText : "Concert-2025.flac", "Audio", "414 MB", "audio")
            ],
            "projects" =>
            [
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Projects.SkyCDv3", out var skyCdText) ? skyCdText : "SkyCD v3", "Folder", "11 items", "folder"),
                new BrowserItem(_translations.TryGetValue("BrowserDataStore.Projects.PluginBenchmarks", out var benchmarksText) ? benchmarksText : "Plugin Benchmarks", "Folder", "6 items", "folder")
            ],
            _ => []
        };
    }
}
